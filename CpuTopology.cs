using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DesktopHtmlHost
{
    // Performance and efficiency cores of a hybrid processor (Intel 12th gen and later, some ARM
    // chips), from the CPU sets Windows gives any program; read once, the topology does not change.
    internal static partial class CpuTopology
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetSystemCpuSetInformation(byte* information, uint length, out uint returned, IntPtr process, uint flags);

        // "group,number" (the Processor Information counter instances) -> efficiency class; empty
        // when the processor is not hybrid (one class only) or the call fails.
        internal static unsafe Dictionary<string, int> EfficiencyClasses()
        {
            var classes = new Dictionary<string, int>(StringComparer.Ordinal);
            GetSystemCpuSetInformation(null, 0, out uint needed, IntPtr.Zero, 0);
            if (needed == 0 || needed > 1 << 20) return classes;
            var buffer = new byte[needed];
            fixed (byte* data = buffer)
            {
                if (!GetSystemCpuSetInformation(data, needed, out needed, IntPtr.Zero, 0)) return classes;
            }
            Parse(buffer.AsSpan(0, (int)needed), classes);
            return classes;
        }

        // SYSTEM_CPU_SET_INFORMATION entries: Size (4), Type (4, 0 = CpuSetInformation), then Id (4),
        // Group (2), LogicalProcessorIndex (1), CoreIndex, LastLevelCacheIndex, NumaNodeIndex,
        // EfficiencyClass (1, higher = faster cores).
        internal static void Parse(ReadOnlySpan<byte> data, Dictionary<string, int> classes)
        {
            var seen = new HashSet<int>();
            for (int offset = 0; offset + 20 <= data.Length;)
            {
                int size = BitConverter.ToInt32(data.Slice(offset));
                if (size < 20 || offset + size > data.Length) break;
                if (BitConverter.ToInt32(data.Slice(offset + 4)) == 0)
                {
                    int group = BitConverter.ToUInt16(data.Slice(offset + 12));
                    int index = data[offset + 14];
                    int efficiency = data[offset + 18];
                    classes[group + "," + index] = efficiency;
                    seen.Add(efficiency);
                }
                offset += size;
            }
            if (seen.Count < 2) classes.Clear();
        }

        // Average busy time of the performance cores (highest class) and of the efficiency cores
        // (every lower class), from the per-processor counter rows; -1 each when not hybrid.
        internal static void Loads(Dictionary<string, int> classes, List<KeyValuePair<string, double>>? rows, out int performance, out int efficiency)
        {
            performance = efficiency = -1;
            if (classes.Count == 0 || rows == null) return;
            int fastest = 0;
            foreach (int value in classes.Values) fastest = Math.Max(fastest, value);
            double pSum = 0, eSum = 0; int pCount = 0, eCount = 0;
            foreach (var row in rows)
            {
                if (!classes.TryGetValue(row.Key, out int value)) continue;
                double busy = Math.Max(0, Math.Min(100, row.Value));
                if (value == fastest) { pSum += busy; pCount++; } else { eSum += busy; eCount++; }
            }
            if (pCount > 0) performance = (int)Math.Round(pSum / pCount);
            if (eCount > 0) efficiency = (int)Math.Round(eSum / eCount);
        }
    }

    // What Windows says of a graphics adapter without administrator rights: its D3DKMT adapter type
    // (hybrid flags).
    internal static partial class GpuAdapterState
    {
        const int KMTQAITYPE_ADAPTERTYPE = 15;
        const uint HybridDiscreteBit = 1u << 4, HybridIntegratedBit = 1u << 5;

        [StructLayout(LayoutKind.Sequential)]
        struct OpenAdapter { public uint LowPart; public int HighPart; public uint Adapter; }
        [StructLayout(LayoutKind.Sequential)]
        struct QueryAdapterInfo { public uint Adapter; public int Type; public IntPtr Data; public uint DataSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct CloseAdapter { public uint Adapter; }
        [LibraryImport("gdi32.dll")] private static partial int D3DKMTOpenAdapterFromLuid(ref OpenAdapter args);
        [LibraryImport("gdi32.dll")] private static partial int D3DKMTQueryAdapterInfo(in QueryAdapterInfo args);
        [LibraryImport("gdi32.dll")] private static partial int D3DKMTCloseAdapter(in CloseAdapter args);
        // D3DKMT_ADAPTERTYPE flags of an adapter; -1 when it cannot be opened or queried.
        internal static unsafe long AdapterType(long luid)
        {
            if (luid == 0) return -1;
            try
            {
                var open = new OpenAdapter { LowPart = (uint)(luid & 0xFFFFFFFF), HighPart = (int)(luid >> 32) };
                if (D3DKMTOpenAdapterFromLuid(ref open) != 0) return -1;
                try
                {
                    uint flags = 0;
                    var query = new QueryAdapterInfo { Adapter = open.Adapter, Type = KMTQAITYPE_ADAPTERTYPE, Data = (IntPtr)(&flags), DataSize = 4 };
                    return D3DKMTQueryAdapterInfo(query) == 0 ? flags : -1;
                }
                finally { D3DKMTCloseAdapter(new CloseAdapter { Adapter = open.Adapter }); }
            }
            catch (EntryPointNotFoundException) { return -1; }
        }

        // Hybrid rendering: Windows lets the discrete GPU draw for windows shown through the
        // integrated one (the iGPU is flagged HybridIntegrated, the dGPU HybridDiscrete).
        internal static bool IsHybridIntegrated(long flags) { return flags >= 0 && (flags & HybridIntegratedBit) != 0; }
        internal static bool IsHybridDiscrete(long flags) { return flags >= 0 && (flags & HybridDiscreteBit) != 0; }
    }
}
