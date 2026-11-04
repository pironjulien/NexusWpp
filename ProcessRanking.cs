using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopHtmlHost
{
    internal sealed record ProcessUsage(string Name, int CpuPercent, long RamMb);

    // Busiest processes from the per-process PDH counters. "Process V2" (Windows 11) names every
    // instance "name:pid"; the legacy "Process" object (Windows 10) repeats the executable name for
    // each instance ("svchost" x80) without any suffix in PdhGetFormattedCounterArray, so its rows
    // are paired by occurrence: every counter of one object is formatted from the same instance list.
    internal static class ProcessRanking
    {
        internal const string CpuV2 = @"\Process V2(*)\% Processor Time";
        internal const string RamV2 = @"\Process V2(*)\Working Set - Private";
        internal const string CpuV1 = @"\Process(*)\% Processor Time";
        internal const string RamV1 = @"\Process(*)\Working Set - Private";

        // "chrome:1234" -> ("chrome", 1234); "chrome" -> ("chrome", -1).
        internal static void ParseInstance(string instance, out string name, out int pid)
        {
            name = instance ?? ""; pid = -1;
            int colon = name.LastIndexOf(':');
            if (colon > 0 && int.TryParse(name.AsSpan(colon + 1), out int value)) { pid = value; name = name.Substring(0, colon); }
        }

        internal static bool IsAggregate(string name, int pid)
        {
            return name == "_Total" || name == "Idle" || pid == 0;
        }

        // Ranks every process: the four busiest by CPU (share of all logical processors) and the
        // largest private working set. pidNames receives pid -> name when the instances carry a pid.
        internal static void Rank(List<KeyValuePair<string, double>> cpuRows, List<KeyValuePair<string, double>> ramRows, int cpuCount,
            out ProcessUsage[] top, out ProcessUsage topRam, Dictionary<int, string> pidNames)
        {
            cpuCount = Math.Max(1, cpuCount);
            // Same instance key and the same occurrence of that key in both arrays.
            var ram = new Dictionary<string, Queue<double>>(StringComparer.Ordinal);
            foreach (var row in ramRows)
            {
                if (!ram.TryGetValue(row.Key, out var values)) ram[row.Key] = values = new Queue<double>();
                values.Enqueue(row.Value);
            }
            var processes = new List<ProcessUsage>(cpuRows.Count);
            foreach (var row in cpuRows)
            {
                double ramBytes = ram.TryGetValue(row.Key, out var values) && values.Count > 0 ? values.Dequeue() : 0;
                ParseInstance(row.Key, out string name, out int pid);
                if (IsAggregate(name, pid)) continue;
                if (pid > 0 && pidNames != null) pidNames[pid] = name;
                int cpuPercent = (int)Math.Min(99, Math.Round(Math.Max(0, row.Value) / cpuCount));
                processes.Add(new ProcessUsage(name, cpuPercent, (long)(Math.Max(0, ramBytes) / (1024 * 1024))));
            }
            top = processes.OrderByDescending(p => p.CpuPercent).Take(4).ToArray();
            topRam = processes.OrderByDescending(p => p.RamMb).FirstOrDefault();
        }
    }
}
