using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DesktopHtmlHost
{
    // Native performance counters (PDH). A persistent query reads the same data as the WMI
    // "PerfFormattedData" classes in milliseconds instead of hundreds, without waking WmiPrvSE.
    // English counter paths work on every Windows display language.
    internal sealed class PdhQuery : IDisposable
    {
        const uint PDH_FMT_DOUBLE = 0x00000200;
        const uint PDH_FMT_NOCAP100 = 0x00008000;
        const uint PDH_MORE_DATA = 0x800007D2;
        const uint PDH_CSTATUS_VALID_DATA = 0x0;
        const uint PDH_CSTATUS_NEW_DATA = 0x1;

        [StructLayout(LayoutKind.Explicit)]
        struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct PDH_FMT_COUNTERVALUE_ITEM
        {
            [FieldOffset(0)] public IntPtr Name;
            [FieldOffset(8)] public uint CStatus;
            [FieldOffset(16)] public double DoubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQuery(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr query);

        IntPtr query;
        readonly Dictionary<string, IntPtr> counters = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        public PdhQuery()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) query = IntPtr.Zero;
        }

        public bool IsOpen { get { return query != IntPtr.Zero; } }

        // Adds a counter once; returns false when this machine does not expose it.
        public bool Add(string englishPath)
        {
            if (query == IntPtr.Zero) return false;
            if (counters.ContainsKey(englishPath)) return true;
            IntPtr counter;
            if (PdhAddEnglishCounter(query, englishPath, IntPtr.Zero, out counter) != 0) return false;
            counters[englishPath] = counter;
            return true;
        }

        public bool Collect()
        {
            return query != IntPtr.Zero && PdhCollectQueryData(query) == 0;
        }

        // Value from the last Collect, or null (missing counter, or a rate counter's first sample).
        public double? Value(string englishPath)
        {
            IntPtr counter;
            if (!counters.TryGetValue(englishPath, out counter)) return null;
            PDH_FMT_COUNTERVALUE value;
            if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, IntPtr.Zero, out value) != 0) return null;
            if (value.CStatus != PDH_CSTATUS_VALID_DATA && value.CStatus != PDH_CSTATUS_NEW_DATA) return null;
            return double.IsNaN(value.DoubleValue) || double.IsInfinity(value.DoubleValue) ? (double?)null : value.DoubleValue;
        }

        // Every instance of a wildcard counter, e.g. "\GPU Engine(*)\Utilization Percentage".
        // Instances that appear or disappear between samples are handled by PDH.
        public List<KeyValuePair<string, double>> Instances(string englishPath)
        {
            var result = new List<KeyValuePair<string, double>>();
            IntPtr counter;
            if (!counters.TryGetValue(englishPath, out counter)) return null;
            uint size = 0, count;
            uint status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, IntPtr.Zero);
            if (status != PDH_MORE_DATA) return status == 0 ? result : null;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buffer) != 0) return null;
                int itemSize = Marshal.SizeOf(typeof(PDH_FMT_COUNTERVALUE_ITEM));
                for (int i = 0; i < count; i++)
                {
                    var item = (PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + i * itemSize), typeof(PDH_FMT_COUNTERVALUE_ITEM));
                    if (item.CStatus != PDH_CSTATUS_VALID_DATA && item.CStatus != PDH_CSTATUS_NEW_DATA) continue;
                    if (double.IsNaN(item.DoubleValue) || double.IsInfinity(item.DoubleValue)) continue;
                    result.Add(new KeyValuePair<string, double>(Marshal.PtrToStringUni(item.Name), item.DoubleValue));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; }
            counters.Clear();
        }
    }
}
