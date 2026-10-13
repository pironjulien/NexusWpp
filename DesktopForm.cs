using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace DesktopHtmlHost
{
    public partial class DesktopForm : Form, IDesktopHostWindow
    {
        private WebView2 webView;
        private string htmlPath;
        private NotifyIcon trayIcon;
        private SettingsForm settingsForm;
        private EventWaitHandle showSettingsEvent;
        private RegisteredWaitHandle showSettingsWait;
        private System.Windows.Forms.Timer searchTimer;
        private int retryCount = 0;
        private const int maxRetries = 300;
        private const int progmanFallbackRetries = 8;
        private bool webViewInitialized = false;
        private bool webViewInitializing = false;
        private bool webViewRecoveryPending = false;
        private CoreWebView2Environment webViewEnvironment;
        private bool desktopAttached = false;
        private bool attachedToTemporaryParent = false;
        private IntPtr currentDesktopParent = IntPtr.Zero;
        private DateTime startupTime = DateTime.Now;

        // --- Telemetry state ---
        private System.Windows.Forms.Timer telemetryTimer;
        private TelemetryCollector telemetryCollector;
        private bool telemetryReady = false;
        private bool telemetryCollectPending = false;
        private System.Windows.Forms.Timer fullscreenTimer;
        private bool runtimeSuspended = false;
        // Suspend only after the desktop stays covered this long; resume on the first visible check.
        private const int SuspendConfirmMs = 1500;
        private int coveredSince;
        private bool coveredPending;
        private WindowChangeWatcher windowWatcher;
        private System.Windows.Forms.Timer windowChangeTimer;
        private bool sessionInactive = false;
        private int telemetryGeneration = 0;
        private string fullscreenReason = "";
        private bool isClosing = false;
        private bool displayOff = false;
        private ShellEventWindow shellEvents;

        // --- Static Hook and Bounds State ---
        private static DesktopForm activeInstance;
        private static IntPtr hookId = IntPtr.Zero;
        private static LowLevelMouseProc mouseProc;
        private static Thread mouseHookThread;
        private static int mouseHookThreadId;
        private static int mouseHookStopRequested;
        private static Exception mouseHookError;
        private static System.Drawing.Rectangle remotePanelBounds = System.Drawing.Rectangle.Empty;
        private static IntPtr renderWindow = IntPtr.Zero;
        private static MouseRoutingSnapshot mouseRouting;

        // Published by the UI thread as one immutable snapshot. The mouse thread must
        // never invoke a WinForms control or wait for WebView2/the display driver.
        private sealed class MouseRoutingSnapshot
        {
            internal readonly System.Drawing.Rectangle ScreenBounds;
            internal readonly IntPtr HostWindow;
            internal readonly IntPtr RenderWindow;

            internal MouseRoutingSnapshot(System.Drawing.Rectangle bounds, IntPtr host, IntPtr render)
            {
                ScreenBounds = bounds;
                HostWindow = host;
                RenderWindow = render;
            }
        }

        // --- Win32 P/Invoke API Definitions ---

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT point;
            public uint privateData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, 
            uint Msg, 
            IntPtr wParam, 
            IntPtr lParam, 
            uint fuFlags, 
            uint uTimeout, 
            out IntPtr lpdwResult
        );

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoints, int cPoints);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        // Win32 Constants
        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int SW_SHOW = 5;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                return cp;
            }
        }

        public DesktopForm(string htmlPath)
        {
            activeInstance = this;
            this.htmlPath = htmlPath;

            // Configure Form to act as a stealth, borderless wallpaper container
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;

            // Start off-screen to avoid visual flashes before parenting
            this.Location = new System.Drawing.Point(-32000, -32000);
            this.Size = new System.Drawing.Size(100, 100);

            // Force handle creation
            IntPtr forceHandle = this.Handle;

            // Listen for system resolution or display setting changes
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            CreateTrayIcon();
            ListenForSettingsRequests();
            CreateShellEventWindow();

            // Create and configure the WebView2 UI component
            CreateWebViewControl();

            // Warm WebView2 immediately while Explorer is still preparing the desktop layer.
            InitializeWebViewAsync();

            // Initialize and start the non-blocking GUI timer to search for WorkerW.
            searchTimer = new System.Windows.Forms.Timer();
            searchTimer.Interval = 100;
            searchTimer.Tick += SearchTimer_Tick;
            searchTimer.Start();
        }

        private void CreateShellEventWindow()
        {
            try
            {
                shellEvents = new ShellEventWindow(
                    delegate { QueueDesktopReattach("Explorer restart (TaskbarCreated)"); },
                    delegate (bool on)
                    {
                        if (isClosing || IsDisposed || displayOff == !on) return;
                        displayOff = !on;
                        Program.LogDebug(on ? "Display turned on." : "Display turned off.");
                        FullscreenTimer_Tick(null, null);
                    });
                if (!shellEvents.DisplayNotificationsRegistered)
                {
                    Program.LogDebug("Display power notifications unavailable.");
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("Shell event window failed: " + ex.Message);
            }
        }

        // The native window was destroyed by someone else (Explorer exiting).
        public bool HandleLost { get { return handleLost; } }
        private bool handleLost;

        // Explorer destroys its child windows when it exits: our handle goes with WorkerW.
        // Stop work at once; DesktopHostContext disposes this form and creates a new one.
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (!isClosing && !RecreatingHandle)
            {
                Program.LogDebug("Wallpaper window destroyed outside a requested close (desktop parent lost).");
                handleLost = true;
                isClosing = true;
                if (searchTimer != null) searchTimer.Stop();
                if (telemetryTimer != null) telemetryTimer.Stop();
                if (fullscreenTimer != null) fullscreenTimer.Stop();
                if (recoveryDelayTimer != null) recoveryDelayTimer.Stop();
                StopMouseHook();
            }
            base.OnHandleDestroyed(e);
        }

        private void CreateWebViewControl()
        {
            ResilientWebView2 newWebView = new ResilientWebView2();
            newWebView.Dock = DockStyle.Fill;
            newWebView.Disposed += WebView_Disposed;
            newWebView.LifecycleFault += WebView_LifecycleFault;
            webView = newWebView;
            this.Controls.Add(webView);
        }

        private void WebView_Disposed(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed || !ReferenceEquals(sender, webView))
            {
                return;
            }

            QueueWebViewRecovery("unexpected WebView2 disposal");
        }

        private void WebView_LifecycleFault(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed || !ReferenceEquals(sender, webView)) return;
            QueueWebViewRecovery("suppressed WebView2 lifecycle exception");
        }

        private void SearchTimer_Tick(object sender, EventArgs e)
        {
            if (desktopAttached && !attachedToTemporaryParent)
            {
                searchTimer.Stop();
                return;
            }

            IntPtr workerw = IntPtr.Zero;

            // 1. Signal Progman to split the desktop
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr result;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 100, out result);
            }

            // 2. Discover the target WorkerW window designed to hold the background
            EnumWindows((hwnd, lParam) =>
            {
                IntPtr shellDll = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellDll != IntPtr.Zero)
                {
                    workerw = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (workerw == IntPtr.Zero)
                    {
                        workerw = FindWindowEx(hwnd, IntPtr.Zero, "WorkerW", null);
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (workerw != IntPtr.Zero)
            {
                AttachToDesktopParent(workerw, false);
                searchTimer.Stop();
            }
            else
            {
                if (!desktopAttached && progman != IntPtr.Zero && retryCount >= progmanFallbackRetries)
                {
                    AttachToDesktopParent(progman, true);
                }

                retryCount++;
                if (retryCount >= maxRetries)
                {
                    searchTimer.Stop();
                    if (!desktopAttached)
                    {
                        Application.Exit();
                    }
                }
            }
        }

        private void AttachToDesktopParent(IntPtr parent, bool temporary)
        {
            if (parent == IntPtr.Zero || (desktopAttached && currentDesktopParent == parent))
            {
                return;
            }

            bool firstAttach = !desktopAttached;

            // Inject our WinForms application handle into the desktop wallpaper layer.
            SetParent(this.Handle, parent);

            int style = GetWindowLong(this.Handle, GWL_STYLE);
            style |= WS_CHILD;
            style &= ~WS_POPUP;
            SetWindowLong(this.Handle, GWL_STYLE, style);
            SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

            desktopAttached = true;
            attachedToTemporaryParent = temporary;
            currentDesktopParent = parent;
            if (firstAttach && Program.FirstLaunch) ShowWelcome();

            UpdateBoundsToVirtualScreen();
            ShowWindow(this.Handle, SW_SHOW);

            double elapsed = (DateTime.Now - startupTime).TotalSeconds;
            if (temporary)
            {
                Program.LogDebug(string.Format(CultureInfo.InvariantCulture, "Wallpaper attached after {0:F2}s (Progman fallback while WorkerW is not ready).", elapsed));
            }
            else if (firstAttach)
            {
                Program.LogDebug(string.Format(CultureInfo.InvariantCulture, "Wallpaper attached after {0:F2}s.", elapsed));
            }
            else
            {
                Program.LogDebug(string.Format(CultureInfo.InvariantCulture, "Wallpaper reparented to WorkerW after {0:F2}s.", elapsed));
            }
        }

        // Covers the virtual screen. Once the window is a WS_CHILD of WorkerW/Progman,
        // SetWindowPos takes parent-client coordinates, so the screen rectangle is mapped
        // into the parent first (a monitor left of or above the primary gives a negative
        // virtual-screen origin, which is (0,0) in a WorkerW spanning the virtual screen).
        private void UpdateBoundsToVirtualScreen()
        {
            if (IsHandleCreated)
            {
                PlaceOverScreenRect(Handle, SystemInformation.VirtualScreen);
            }
            PublishMouseRouting();
            PostLayout();
        }

        internal static bool PlaceOverScreenRect(IntPtr window, System.Drawing.Rectangle screen)
        {
            POINT origin = new POINT { x = screen.Left, y = screen.Top };
            if ((GetWindowLong(window, GWL_STYLE) & WS_CHILD) != 0)
            {
                IntPtr parent = GetParent(window);
                if (parent != IntPtr.Zero) MapWindowPoints(IntPtr.Zero, parent, ref origin, 1);
            }
            return SetWindowPos(window, IntPtr.Zero, origin.x, origin.y, screen.Width, screen.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        // Window-relative primary monitor rectangle, in window pixels, so the page can keep
        // the cockpit on the primary monitor while the background spans every monitor.
        internal static string BuildLayoutMessage(System.Drawing.Rectangle window, System.Drawing.Rectangle primary)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"control\":\"LAYOUT\",\"window\":{{\"w\":{0},\"h\":{1}}},\"primary\":{{\"x\":{2},\"y\":{3},\"w\":{4},\"h\":{5}}}}}",
                window.Width, window.Height, primary.Left - window.Left, primary.Top - window.Top, primary.Width, primary.Height);
        }

        private string lastLayoutMessage;

        private void PostLayout()
        {
            if (isClosing || IsDisposed || !IsHandleCreated) return;
            try
            {
                RECT bounds;
                if (!GetWindowRect(Handle, out bounds)) return;
                System.Drawing.Rectangle window = System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                Screen primary = Screen.PrimaryScreen;
                if (window.Width <= 0 || window.Height <= 0 || primary == null) return;
                string message = BuildLayoutMessage(window, primary.Bounds);
                if (message != lastLayoutMessage)
                {
                    lastLayoutMessage = message;
                    Program.LogDebug("Layout: " + message);
                }
                PostWebMessageAsJsonSafe(message, "Layout post");
            }
            catch (Exception ex)
            {
                Program.LogDebug("Layout post error: " + ex.Message);
            }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            TryBeginInvoke((MethodInvoker)delegate {
                UpdateBoundsToVirtualScreen();
                // Still attached to the real desktop layer: resizing is enough. A full re-attach
                // detaches the window and flashes black, which happened on every GPU/display change.
                if (IsAttachedToLiveDesktop())
                {
                    Program.LogDebug("Display settings changed: wallpaper resized in place.");
                    return;
                }
                QueueDesktopReattach("display settings changed");
            });
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                QueueDesktopReattach("system resumed from sleep");
            }
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock ||
                e.Reason == SessionSwitchReason.RemoteDisconnect ||
                e.Reason == SessionSwitchReason.ConsoleDisconnect)
            {
                TryBeginInvoke((MethodInvoker)delegate {
                    sessionInactive = true;
                    FullscreenTimer_Tick(null, null);
                });
            }
            if (e.Reason == SessionSwitchReason.SessionUnlock ||
                e.Reason == SessionSwitchReason.SessionLogon ||
                e.Reason == SessionSwitchReason.RemoteConnect ||
                e.Reason == SessionSwitchReason.ConsoleConnect)
            {
                TryBeginInvoke((MethodInvoker)delegate {
                    sessionInactive = false;
                    FullscreenTimer_Tick(null, null);
                });
                QueueDesktopReattach("session became active: " + e.Reason);
            }
        }

        // Attached to Explorer's wallpaper layer (not the temporary Progman fallback) and still its child.
        private bool IsAttachedToLiveDesktop()
        {
            return desktopAttached && !attachedToTemporaryParent && IsHandleCreated &&
                   currentDesktopParent != IntPtr.Zero && IsWindow(currentDesktopParent) &&
                   GetParent(Handle) == currentDesktopParent;
        }

        private void QueueDesktopReattach(string reason)
        {
            if (isClosing || IsDisposed) return;

            TryBeginInvoke((MethodInvoker)delegate
            {
                if (isClosing || IsDisposed) return;
                RestartDesktopSearch(reason);
            });
        }

        private void RestartDesktopSearch(string reason)
        {
            Program.LogDebug("Restarting desktop attachment after " + reason + ".");

            desktopAttached = false;
            attachedToTemporaryParent = false;
            currentDesktopParent = IntPtr.Zero;
            retryCount = 0;
            startupTime = DateTime.Now;

            if (IsHandleCreated)
            {
                UpdateBoundsToVirtualScreen();
                ShowWindow(this.Handle, SW_SHOW);
            }

            if (searchTimer != null)
            {
                searchTimer.Stop();
                searchTimer.Start();
            }
        }

        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            Program.LogDebug("Windows session ending: closing NexusWpp cleanly.");
            BeginCleanShutdown();
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Close();
                });
            }
            catch
            {
                Close();
            }
        }

        private void BeginCleanShutdown()
        {
            isClosing = true;
            if (showSettingsWait != null) { showSettingsWait.Unregister(null); showSettingsWait = null; }
            if (showSettingsEvent != null) { showSettingsEvent.Dispose(); showSettingsEvent = null; }
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            telemetryCollectPending = false;
            runtimeSuspended = true;

            if (searchTimer != null) searchTimer.Stop();
            if (telemetryTimer != null) telemetryTimer.Stop();
            if (fullscreenTimer != null) fullscreenTimer.Stop();
            if (recoveryDelayTimer != null) recoveryDelayTimer.Stop();
            if (shellEvents != null) { shellEvents.Dispose(); shellEvents = null; }
            DisposeWindowWatcher();
            StopMouseHook();

            if (webView != null)
            {
                try
                {
                    ResilientWebView2 resilientWebView = webView as ResilientWebView2;
                    if (resilientWebView != null)
                    {
                        resilientWebView.BeginShutdown();
                    }

                    webView.Disposed -= WebView_Disposed;
                    if (resilientWebView != null) resilientWebView.LifecycleFault -= WebView_LifecycleFault;
                    webView.Dock = DockStyle.None;
                    webView.Visible = false;
                    Controls.Remove(webView);
                }
                catch (Exception ex)
                {
                    Program.LogDebug("WebView detach during shutdown failed: " + ex.Message);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            BeginCleanShutdown();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Tray icon, settings channel and WebView detach normally go through FormClosing.
                if (handleLost) BeginCleanShutdown();
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
                StopMouseHook();
                if (shellEvents != null) { shellEvents.Dispose(); shellEvents = null; }
                if (recoveryDelayTimer != null) { recoveryDelayTimer.Dispose(); recoveryDelayTimer = null; }
                if (searchTimer != null)
                {
                    searchTimer.Dispose();
                }
                if (telemetryTimer != null)
                {
                    telemetryTimer.Dispose();
                }
                if (fullscreenTimer != null)
                {
                    fullscreenTimer.Dispose();
                }
                if (webView != null)
                {
                    ResilientWebView2 resilientWebView = webView as ResilientWebView2;
                    if (resilientWebView != null)
                    {
                        resilientWebView.BeginShutdown();
                    }

                    webView.Disposed -= WebView_Disposed;
                    if (resilientWebView != null) resilientWebView.LifecycleFault -= WebView_LifecycleFault;
                    try
                    {
                        webView.Dispose();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Program.LogDebug("Suppressed WebView2 disposal after shutdown: " + ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        if (!ResilientWebView2.IsDisposedLifecycleException(ex))
                        {
                            throw;
                        }

                        Program.LogDebug("Suppressed WebView2 disposal after shutdown: " + ex.Message);
                    }
                    finally
                    {
                        webView = null;
                    }
                }
            }
            base.Dispose(disposing);
        }

        private bool TryBeginInvoke(MethodInvoker action)
        {
            if (isClosing || IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private bool TryGetCoreWebView2(out CoreWebView2 coreWebView)
        {
            coreWebView = null;
            // Startup messages are replayed by the page's runtime-state handshake.
            // A controller still being created does not need concurrent recovery.
            if (webViewInitializing) return false;
            if (isClosing || IsDisposed || webView == null || webView.IsDisposed)
            {
                if (!isClosing && !IsDisposed)
                {
                    QueueWebViewRecovery("CoreWebView2 unavailable");
                }

                return false;
            }

            try
            {
                coreWebView = webView.CoreWebView2;
                if (coreWebView == null)
                {
                    QueueWebViewRecovery("CoreWebView2 missing");
                    return false;
                }

                return coreWebView != null;
            }
            catch (ObjectDisposedException)
            {
                QueueWebViewRecovery("disposed CoreWebView2 access");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                if (ResilientWebView2.IsDisposedLifecycleException(ex))
                {
                    QueueWebViewRecovery("disposed CoreWebView2 lifecycle");
                    return false;
                }

                if (!isClosing)
                {
                    Program.LogDebug("WebView unavailable: " + ex.Message);
                }
                return false;
            }
        }

        // Consecutive failed posts tolerated before the WebView is recreated.
        internal const int MaxFailedPosts = 5;
        private int failedPosts;

        private bool PostWebMessageAsJsonSafe(string payload, string context)
        {
            CoreWebView2 coreWebView;
            if (!TryGetCoreWebView2(out coreWebView))
            {
                return false;
            }

            try
            {
                coreWebView.PostWebMessageAsJson(payload);
                failedPosts = 0;
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException || ex is InvalidOperationException || ex is COMException)) throw;
                OnWebMessageFailure(context, ex);
                return false;
            }
        }

        // A swallowed failure used to leave a dead WebView on the desktop for days:
        // a disposed core recovers at once, other failures after a short streak.
        private void OnWebMessageFailure(string context, Exception ex)
        {
            if (isClosing || IsDisposed) return;
            failedPosts++;
            if (failedPosts <= MaxFailedPosts)
            {
                Program.LogDebug(context + " skipped: " + ex.Message);
            }
            if (ShouldRecoverAfterPostFailure(ex, failedPosts))
            {
                failedPosts = 0;
                QueueWebViewRecovery(context + " failure: " + ex.Message);
            }
        }

        internal static bool ShouldRecoverAfterPostFailure(Exception ex, int consecutiveFailures)
        {
            return ex is ObjectDisposedException ||
                ResilientWebView2.IsDisposedLifecycleException(ex) ||
                consecutiveFailures >= MaxFailedPosts;
        }

    }
}
