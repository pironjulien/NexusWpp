using System;
using System.Runtime.InteropServices;

// Every native library of the host is a Windows system DLL: never search the application folder or PATH.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace DesktopHtmlHost
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public unsafe MEMORYSTATUSEX()
        {
            dwLength = (uint)sizeof(MEMORYSTATUSEX);
        }
    }

    internal static partial class Win32
    {
        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // Each FILETIME is read as one 64-bit count of 100 ns intervals (low part first).
        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);

        [LibraryImport("powrprof.dll")]
        public static partial uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [LibraryImport("powrprof.dll")]
        public static partial uint PowerSetActiveScheme(IntPtr UserRootPowerKey, in Guid ActiveSchemeGuid);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr LocalFree(IntPtr hMem);

        [LibraryImport("kernel32.dll")]
        public static partial ulong GetTickCount64();

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
