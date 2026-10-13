using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DesktopHtmlHost
{
    // Each field is optional: one unsupported sensor must not discard the others.
    internal sealed class NvidiaSample
    {
        internal double? Utilization, Temperature, CoreClock, MemoryClock, PowerWatts, TotalMiB, UsedMiB;
        internal static double? Number(string text, double maximum)
        {
            double value;
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= maximum ? (double?)value : null;
        }
        internal static NvidiaSample Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var v = line.Split(',');
            if (v.Length != 7) return null;
            var sample = new NvidiaSample {
                Utilization = Number(v[0], 100), Temperature = Number(v[1], 200),
                CoreClock = Number(v[2], 100000), MemoryClock = Number(v[3], 100000),
                PowerWatts = Number(v[4], 10000), TotalMiB = Number(v[5], 1048576), UsedMiB = Number(v[6], 1048576)
            };
            if (sample.TotalMiB.HasValue && sample.UsedMiB > sample.TotalMiB) sample.UsedMiB = null;
            return sample.Utilization.HasValue || sample.Temperature.HasValue || sample.PowerWatts.HasValue
                || sample.CoreClock.HasValue || sample.MemoryClock.HasValue || sample.TotalMiB.HasValue || sample.UsedMiB.HasValue ? sample : null;
        }
    }

    internal sealed class NvidiaTelemetry
    {
        internal const int IntervalMs = 5000;
        internal const int BatteryIntervalMs = 15000;
        internal const int MaxIntervalMs = 60000;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastAttempt = long.MinValue / 2, lastSuccess = -1;
        int failures, intervalMs = IntervalMs;
        NvidiaSample sample;
        string status;
        bool? available;
        internal string Status { get { return status == "fresh" && AgeMs > 2 * intervalMs ? "stale" : status; } }
        internal long AgeMs { get { return lastSuccess < 0 ? -1 : Math.Max(0, clock.ElapsedMilliseconds - lastSuccess); } }
        internal NvidiaTelemetry() { status = "unavailable"; }
        internal NvidiaSample Current { get { return Status == "fresh" ? sample : null; } }

        // nvidia-smi is installed with the driver and the adapter is an NVIDIA one.
        internal bool IsAvailable(string adapterName)
        {
            if (string.IsNullOrEmpty(adapterName) || adapterName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!available.HasValue) available = File.Exists(Executable);
            return available.Value;
        }

        // Every spawn costs about 50 ms of CPU and initialises NVML, which can wake a sleeping
        // Optimus GPU: wait longer on battery and after failures, and never poll a suspended GPU
        // (the last reading then turns stale, then unavailable; nothing is invented).
        internal static int NextIntervalMs(int failures, bool onBattery)
        {
            int interval = onBattery ? BatteryIntervalMs : IntervalMs;
            for (int i = 0; i < Math.Min(failures, 4); i++) interval *= 2;
            return Math.Min(MaxIntervalMs, interval);
        }

        internal NvidiaSample Read(string adapterName, bool adapterSuspended, bool onBattery)
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
        internal void Accept(NvidiaSample next, long now)
        {
            sample = next;
            if (next != null) { lastSuccess = now; status = "fresh"; failures = 0; }
            else { status = lastSuccess < 0 ? "unavailable" : "stale"; failures++; }
        }
        static string Executable { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvidia-smi.exe"); } }
        static NvidiaSample Query(string adapterName)
        {
            if (string.IsNullOrEmpty(adapterName) || adapterName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var executable = Executable;
            if (!File.Exists(executable)) return null;
            try {
                using (var process = Process.Start(new ProcessStartInfo(executable,
                    "--query-gpu=name,utilization.gpu,temperature.gpu,clocks.gr,clocks.mem,power.draw,memory.total,memory.used --format=csv,noheader,nounits") {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })) {
                    if (process == null) return null;
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(1500)) { try { process.Kill(); } catch { } return null; }
                    if (process.ExitCode != 0) return null;
                    string[] lines = stdout.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines) {
                        int comma = line.IndexOf(',');
                        if (comma < 0) continue;
                        string name = line.Substring(0, comma).Trim().Trim('"');
                        if (name.Equals(adapterName, StringComparison.OrdinalIgnoreCase)) return NvidiaSample.Parse(line.Substring(comma + 1));
                    }
                    // Do not attach another GPU's sensors to this adapter on multi-GPU PCs.
                    return null;
                }
            } catch (Exception ex) {
                Program.LogDebug("NVIDIA sensor query failed safely: " + ex.Message);
                return null;
            }
        }
    }
}
