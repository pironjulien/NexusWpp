using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopHtmlHost
{
    // Each field is optional: one unsupported sensor must not discard the others.
    internal sealed class NvidiaSample
    {
        internal double? Utilization, Temperature, CoreClock, MemoryClock, PowerWatts, TotalMiB, UsedMiB, FanPercent;
        internal static double? Number(double? value, double maximum)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                && value.Value >= 0 && value.Value <= maximum ? value : null;
        }
        // Validated sample from raw driver readings (null = not supported or failed).
        internal static NvidiaSample? Create(double? utilization, double? temperature, double? coreClock, double? memoryClock,
            double? powerWatts, double? totalMiB, double? usedMiB, double? fanPercent = null)
        {
            var sample = new NvidiaSample {
                Utilization = Number(utilization, 100), Temperature = Number(temperature, 200),
                CoreClock = Number(coreClock, 100000), MemoryClock = Number(memoryClock, 100000),
                PowerWatts = Number(powerWatts, 10000), TotalMiB = Number(totalMiB, 1048576), UsedMiB = Number(usedMiB, 1048576),
                FanPercent = Number(fanPercent, 100)
            };
            if (sample.TotalMiB.HasValue && sample.UsedMiB > sample.TotalMiB) sample.UsedMiB = null;
            return sample.Utilization.HasValue || sample.Temperature.HasValue || sample.PowerWatts.HasValue
                || sample.CoreClock.HasValue || sample.MemoryClock.HasValue || sample.TotalMiB.HasValue || sample.UsedMiB.HasValue ? sample : null;
        }
    }

    // NVIDIA Management Library, installed in System32 by every current NVIDIA display driver.
    // Only System32 is searched, never the application folder or PATH.
    internal static partial class Nvml
    {
        const string Library = "nvml.dll";
        internal const int Success = 0, NotSupported = 3, ArgumentVersionMismatch = 25;
        internal const int TemperatureGpu = 0, ClockGraphics = 0, ClockMemory = 2;

        [StructLayout(LayoutKind.Sequential)] internal struct Utilization { internal uint Gpu, Memory; }
        [StructLayout(LayoutKind.Sequential)] internal struct Memory { internal ulong Total, Free, Used; }
        // Reserved memory reported apart from the used memory, as nvidia-smi shows it.
        [StructLayout(LayoutKind.Sequential)] internal struct MemoryV2 { internal uint Version; internal ulong Total, Reserved, Free, Used; }
        internal static readonly uint MemoryV2Version = (uint)Marshal.SizeOf<MemoryV2>() | (2u << 24);

        [LibraryImport(Library, EntryPoint = "nvmlInit_v2"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int Init();
        [LibraryImport(Library, EntryPoint = "nvmlShutdown"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int Shutdown();
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetCount_v2"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetCount(out uint count);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetHandleByIndex_v2"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetHandleByIndex(uint index, out IntPtr device);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetName"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetName(IntPtr device, [Out] byte[] name, uint length);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetUtilizationRates"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetTemperature"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetTemperature(IntPtr device, int sensor, out uint celsius);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetClockInfo"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetClockInfo(IntPtr device, int clock, out uint mhz);
        // Speed the driver asks of the first fan, in % of its maximum (0 when the fans are stopped).
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetFanSpeed"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetFanSpeed(IntPtr device, out uint percent);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetPowerUsage"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetPowerUsage(IntPtr device, out uint milliwatts);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetMemoryInfo_v2"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetMemoryInfoV2(IntPtr device, ref MemoryV2 memory);
        [LibraryImport(Library, EntryPoint = "nvmlDeviceGetMemoryInfo"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DeviceGetMemoryInfo(IntPtr device, out Memory memory);

        internal static string Path { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), Library); } }
    }

    internal sealed class NvidiaTelemetry
    {
        internal const int IntervalMs = 5000;
        internal const int BatteryIntervalMs = 15000;
        internal const int MaxIntervalMs = 60000;
        internal const string Provider = "nvml";
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastAttempt = long.MinValue / 2, lastSuccess = -1;
        int failures, intervalMs = IntervalMs;
        NvidiaSample? sample;
        string status;
        bool? available;
        bool initialized, memoryV2Unsupported;
        IntPtr device;
        internal string Status { get { return status == "fresh" && AgeMs > 2 * intervalMs ? "stale" : status; } }
        internal long AgeMs { get { return lastSuccess < 0 ? -1 : Math.Max(0, clock.ElapsedMilliseconds - lastSuccess); } }
        internal NvidiaTelemetry() { status = "unavailable"; }
        internal NvidiaSample? Current { get { return Status == "fresh" ? sample : null; } }

        // NVML is installed with the driver and the adapter is an NVIDIA one.
        internal bool IsAvailable(string adapterName)
        {
            if (string.IsNullOrEmpty(adapterName) || adapterName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!available.HasValue) available = File.Exists(Nvml.Path);
            return available.Value;
        }

        // An NVML read costs well under a millisecond, but it can wake a sleeping Optimus GPU:
        // wait longer on battery and after failures, and never poll a suspended GPU (the last
        // reading then turns stale, then unavailable; nothing is invented).
        internal static int NextIntervalMs(int failures, bool onBattery)
        {
            int interval = onBattery ? BatteryIntervalMs : IntervalMs;
            for (int i = 0; i < Math.Min(failures, 4); i++) interval *= 2;
            return Math.Min(MaxIntervalMs, interval);
        }

        internal NvidiaSample? Read(string adapterName, bool adapterSuspended, bool onBattery)
        {
            if (!IsAvailable(adapterName)) { if (lastSuccess < 0) status = "unavailable"; return Current; }
            intervalMs = NextIntervalMs(0, onBattery);
            if (!adapterSuspended && clock.ElapsedMilliseconds - lastAttempt >= NextIntervalMs(failures, onBattery)) {
                lastAttempt = clock.ElapsedMilliseconds;
                Accept(Query(adapterName), clock.ElapsedMilliseconds);
            }
            // A failed attempt invalidates the old values immediately; history keeps the gap.
            return Current;
        }
        internal void Accept(NvidiaSample? next, long now)
        {
            sample = next;
            if (next != null) { lastSuccess = now; status = "fresh"; failures = 0; }
            else { status = lastSuccess < 0 ? "unavailable" : "stale"; failures++; }
        }

        NvidiaSample? Query(string adapterName)
        {
            try {
                if (!initialized) {
                    int init = Nvml.Init();
                    if (init != Nvml.Success) { Program.LogDebug("NVML init failed: " + init); return null; }
                    initialized = true;
                }
                if (device == IntPtr.Zero) device = FindDevice(adapterName);
                // Do not attach another GPU's sensors to this adapter on multi-GPU PCs. A session opened
                // before a driver update or restart no longer lists the GPU: the next attempt opens a new one.
                if (device == IntPtr.Zero) { _ = Nvml.Shutdown(); initialized = false; return null; }
                var next = ReadDevice(device);
                // A lost or replaced GPU (driver update, TDR) is looked up again next time.
                if (next == null) device = IntPtr.Zero;
                return next;
            } catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException) {
                // Old or broken driver: never retried in this process; the WDDM sensors take over.
                available = false;
                Program.LogDebug("NVML unavailable: " + ex.Message);
                return null;
            } catch (Exception ex) {
                Program.LogDebug("NVIDIA sensor query failed safely: " + ex.Message);
                return null;
            }
        }

        static IntPtr FindDevice(string adapterName)
        {
            uint count;
            if (Nvml.DeviceGetCount(out count) != Nvml.Success) return IntPtr.Zero;
            var buffer = new byte[96];
            for (uint i = 0; i < count; i++) {
                IntPtr handle;
                if (Nvml.DeviceGetHandleByIndex(i, out handle) != Nvml.Success) continue;
                Array.Clear(buffer);
                if (Nvml.DeviceGetName(handle, buffer, (uint)buffer.Length) != Nvml.Success) continue;
                int end = Array.IndexOf(buffer, (byte)0);
                string name = Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end).Trim();
                if (name.Equals(adapterName, StringComparison.OrdinalIgnoreCase)) return handle;
            }
            return IntPtr.Zero;
        }

        NvidiaSample? ReadDevice(IntPtr handle)
        {
            Nvml.Utilization rates; uint temperature, core, memory, milliwatts;
            double? utilization = Nvml.DeviceGetUtilizationRates(handle, out rates) == Nvml.Success ? rates.Gpu : (double?)null;
            double? celsius = Nvml.DeviceGetTemperature(handle, Nvml.TemperatureGpu, out temperature) == Nvml.Success ? temperature : (double?)null;
            double? coreClock = Nvml.DeviceGetClockInfo(handle, Nvml.ClockGraphics, out core) == Nvml.Success ? core : (double?)null;
            double? memoryClock = Nvml.DeviceGetClockInfo(handle, Nvml.ClockMemory, out memory) == Nvml.Success ? memory : (double?)null;
            double? watts = Nvml.DeviceGetPowerUsage(handle, out milliwatts) == Nvml.Success ? milliwatts / 1000.0 : (double?)null;
            // Passive or fanless cards answer "not supported": the slot then stays unavailable.
            double? fan = Nvml.DeviceGetFanSpeed(handle, out uint fanPercent) == Nvml.Success ? fanPercent : (double?)null;
            double? totalMiB = null, usedMiB = null;
            if (!memoryV2Unsupported) {
                var info = new Nvml.MemoryV2 { Version = Nvml.MemoryV2Version };
                int result;
                try { result = Nvml.DeviceGetMemoryInfoV2(handle, ref info); }
                catch (EntryPointNotFoundException) { result = Nvml.NotSupported; }
                if (result == Nvml.Success) { totalMiB = info.Total / 1048576.0; usedMiB = Math.Floor(info.Used / 1048576.0); }
                else if (result == Nvml.NotSupported || result == Nvml.ArgumentVersionMismatch) memoryV2Unsupported = true;
            }
            if (memoryV2Unsupported) {
                Nvml.Memory info;
                if (Nvml.DeviceGetMemoryInfo(handle, out info) == Nvml.Success) { totalMiB = info.Total / 1048576.0; usedMiB = Math.Floor(info.Used / 1048576.0); }
            }
            return NvidiaSample.Create(utilization, celsius, coreClock, memoryClock, watts, totalMiB, usedMiB, fan);
        }
    }
}
