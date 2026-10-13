using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopHtmlHost
{
    // Hidden top-level window for shell broadcasts that the wallpaper, a WS_CHILD of
    // WorkerW, never receives: "TaskbarCreated" after an Explorer restart, and the
    // console display power state (off, on, dimmed).
    internal sealed class ShellEventWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filterStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerBroadcastSetting
        {
            public Guid PowerSetting;
            public int DataLength;
            public int Data;
        }

        internal const int WM_POWERBROADCAST = 0x0218;
        internal const int PBT_POWERSETTINGCHANGE = 0x8013;
        internal static readonly Guid ConsoleDisplayState = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        internal static readonly Guid MonitorPowerOn = new Guid("02731015-4510-4526-99e6-e5a17ebd1aea");
        internal static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        private readonly Action explorerRestarted;
        private readonly Action<bool> displayPowerChanged;
        private IntPtr powerNotification = IntPtr.Zero;

        internal ShellEventWindow(Action explorerRestarted, Action<bool> displayPowerChanged)
        {
            this.explorerRestarted = explorerRestarted;
            this.displayPowerChanged = displayPowerChanged;
            // Top-level (broadcasts skip message-only windows), never shown.
            CreateHandle(new CreateParams { Caption = "NexusWpp shell events", ExStyle = 0x00000080 /* WS_EX_TOOLWINDOW */ });
            // Explorer still reaches an elevated instance.
            if (TaskbarCreatedMessage != 0) ChangeWindowMessageFilterEx(Handle, TaskbarCreatedMessage, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);

            // Windows sends the current state right after registration.
            Guid setting = ConsoleDisplayState;
            powerNotification = RegisterPowerSettingNotification(Handle, ref setting, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
            if (powerNotification == IntPtr.Zero)
            {
                setting = MonitorPowerOn;
                powerNotification = RegisterPowerSettingNotification(Handle, ref setting, 0);
            }
        }

        internal bool DisplayNotificationsRegistered { get { return powerNotification != IntPtr.Zero; } }

        // Display state: 0 off, 1 on, 2 dimmed (still visible). Returns false for other settings.
        internal static bool TryReadDisplayPower(IntPtr setting, out bool displayOn)
        {
            displayOn = true;
            if (setting == IntPtr.Zero) return false;
            PowerBroadcastSetting value = (PowerBroadcastSetting)Marshal.PtrToStructure(setting, typeof(PowerBroadcastSetting));
            if ((value.PowerSetting != ConsoleDisplayState && value.PowerSetting != MonitorPowerOn) || value.DataLength < 4) return false;
            displayOn = value.Data != 0;
            return true;
        }

        // Test hook: builds the native payload Windows sends with PBT_POWERSETTINGCHANGE.
        internal static IntPtr AllocateDisplayPowerSetting(Guid setting, int state)
        {
            PowerBroadcastSetting value = new PowerBroadcastSetting { PowerSetting = setting, DataLength = 4, Data = state };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PowerBroadcastSetting)));
            Marshal.StructureToPtr(value, memory, false);
            return memory;
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (TaskbarCreatedMessage != 0 && m.Msg == unchecked((int)TaskbarCreatedMessage))
                {
                    if (explorerRestarted != null) explorerRestarted();
                }
                else if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt64() == PBT_POWERSETTINGCHANGE)
                {
                    bool displayOn;
                    if (TryReadDisplayPower(m.LParam, out displayOn) && displayPowerChanged != null) displayPowerChanged(displayOn);
                    m.Result = (IntPtr)1;
                    return;
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("Shell event error: " + ex.Message);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (powerNotification != IntPtr.Zero)
            {
                UnregisterPowerSettingNotification(powerNotification);
                powerNotification = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
