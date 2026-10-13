using System;
using System.Runtime.InteropServices;

namespace DesktopHtmlHost
{
    // Battery power flow from the power manager (no WMI): rate in mW (+ charging, - discharging)
    // and Windows' own remaining-time estimate.
    internal struct BatteryState
    {
        internal int RateMilliwatts;     // int.MinValue when unknown
        internal long EstimatedSeconds;  // -1 when unknown

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_BATTERY_STATE
        {
            public byte AcOnLine, BatteryPresent, Charging, Discharging;
            public byte Spare1, Spare2, Spare3, Tag;
            public uint MaxCapacity, RemainingCapacity, Rate, EstimatedTime, DefaultAlert1, DefaultAlert2;
        }

        const int SystemBatteryStateLevel = 5;
        const uint UnknownRate = 0x80000000, UnknownTime = 0xFFFFFFFF;

        [DllImport("powrprof.dll")]
        static extern uint CallNtPowerInformation(int level, IntPtr input, int inputLength, out SYSTEM_BATTERY_STATE output, int outputLength);

        internal static bool TryRead(out BatteryState state)
        {
            state = new BatteryState { RateMilliwatts = int.MinValue, EstimatedSeconds = -1 };
            try
            {
                SYSTEM_BATTERY_STATE raw;
                if (CallNtPowerInformation(SystemBatteryStateLevel, IntPtr.Zero, 0, out raw, Marshal.SizeOf(typeof(SYSTEM_BATTERY_STATE))) != 0) return false;
                if (raw.BatteryPresent == 0) return false;
                state = FromRaw(raw);
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static BatteryState FromRaw(SYSTEM_BATTERY_STATE raw)
        {
            return new BatteryState
            {
                RateMilliwatts = raw.Rate == UnknownRate ? int.MinValue : unchecked((int)raw.Rate),
                EstimatedSeconds = raw.EstimatedTime == UnknownTime ? -1 : raw.EstimatedTime,
            };
        }
    }
}
