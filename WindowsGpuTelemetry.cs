using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DesktopHtmlHost
{
    internal sealed class GpuEngineReading { internal string Name; internal int Utilization; }
    internal sealed class WindowsGpuSample
    {
        internal int Utilization=-1, Decode=-1;
        internal long SharedBytes=-1, DedicatedBytes=-1;
    }
    internal static class WindowsGpuTelemetry
    {
        internal static int BusiestEngine(IEnumerable<GpuEngineReading> rows,string luid,string kind) {
            if(string.IsNullOrEmpty(luid))return -1;
            var totals=new Dictionary<string,long>();
            foreach(var row in rows) {
                int adapter=row.Name.IndexOf(luid,StringComparison.OrdinalIgnoreCase);
                if(adapter<0 || row.Name.IndexOf("engtype_"+kind,StringComparison.OrdinalIgnoreCase)<0)continue;
                string engine=row.Name.Substring(adapter);
                long old;totals.TryGetValue(engine,out old);totals[engine]=old+Math.Max(0,row.Utilization);
            }
            return totals.Count==0?0:(int)Math.Min(100,totals.Values.Max());
        }
        internal static int BusiestNpuEngine(IEnumerable<GpuEngineReading> rows,string luid) {
            // Intel exposes Neural engines; other NPU drivers expose Compute engines.
            // Separate engines run concurrently, so retain the busiest, not their sum.
            return Math.Max(BusiestEngine(rows,luid,"Neural"),BusiestEngine(rows,luid,"Compute"));
        }
        const string EngineUtilization=@"\GPU Engine(*)\Utilization Percentage";
        const string SharedUsage=@"\GPU Adapter Memory(*)\Shared Usage";
        const string DedicatedUsage=@"\GPU Adapter Memory(*)\Dedicated Usage";
        internal static void AddCounters(PdhQuery perf) {
            perf.Add(EngineUtilization);perf.Add(SharedUsage);perf.Add(DedicatedUsage);
        }
        // All adapters from the collection's PDH sample (instances follow processes as they come and go).
        internal static WindowsGpuSample[] Read(PdhQuery perf,string igpu,string dgpu,string npu) {
            string[] luids={igpu,dgpu,npu};var result=new[]{new WindowsGpuSample(),new WindowsGpuSample(),new WindowsGpuSample()};
            var engines=perf.Instances(EngineUtilization);
            if(engines!=null) {
                var rows=new List<GpuEngineReading>(engines.Count);
                foreach(var engine in engines) rows.Add(new GpuEngineReading {Name=engine.Key,Utilization=(int)Math.Round(Math.Max(0,engine.Value))});
                for(int i=0;i<3;i++) {result[i].Utilization=i==2?BusiestNpuEngine(rows,luids[i]):BusiestEngine(rows,luids[i],"3D");result[i].Decode=BusiestEngine(rows,luids[i],"VideoDecode");}
            }
            var shared=perf.Instances(SharedUsage);var dedicated=perf.Instances(DedicatedUsage);
            for(int i=0;i<3;i++) {
                if(string.IsNullOrEmpty(luids[i]))continue;
                if(shared!=null) foreach(var row in shared) if(row.Key.IndexOf(luids[i],StringComparison.OrdinalIgnoreCase)>=0) result[i].SharedBytes=(long)Math.Max(0,row.Value);
                if(dedicated!=null) foreach(var row in dedicated) if(row.Key.IndexOf(luids[i],StringComparison.OrdinalIgnoreCase)>=0) result[i].DedicatedBytes=(long)Math.Max(0,row.Value);
            }
            return result;
        }
    }

    // One DirectX adapter as Windows registers it (SOFTWARE\Microsoft\DirectX).
    internal sealed class GpuAdapterInfo
    {
        internal string Name = "", Luid = "", Kind = "";
        internal long LuidValue, DedicatedBytes, SharedBytes;
    }

    internal static class GpuClassifier
    {
        // Integrated GPUs only reserve a small carve-out (128 MiB on an Intel Arc iGPU); every
        // discrete board has at least 1 GiB of its own memory.
        internal const long DiscreteVramThreshold = 1L << 30;

        // "dgpu", "igpu", or "" for software and remote adapters.
        internal static string ClassifyGpu(string description, long dedicatedVideoMemory)
        {
            if (string.IsNullOrWhiteSpace(description)) return "";
            string n = description.ToLowerInvariant();
            if (n.Contains("microsoft basic") || n.Contains("remote display") || n.Contains("indirect display") ||
                n.Contains("virtual display") || n.Contains("hyper-v")) return "";
            if (n.Contains("nvidia")) return "dgpu";
            // AMD APUs can reserve a large UMA carve-out in firmware: their name decides first.
            if (IsIntegratedAmdName(n) || n.Contains("qualcomm") || n.Contains("adreno")) return "igpu";
            if (dedicatedVideoMemory >= DiscreteVramThreshold) return "dgpu";
            if (dedicatedVideoMemory > 0) return "igpu";
            // Size unknown: fall back to model names.
            if (Regex.IsMatch(n, @"\barc(\(tm\))?\s+(pro\s+)?[ab]\d{2,3}")) return "dgpu";
            if (n.Contains("intel")) return "igpu";
            if (n.Contains("radeon") || n.Contains("amd")) return "dgpu";
            return "";
        }

        // "AMD Radeon(TM) Graphics", "AMD Radeon(TM) 780M Graphics", "Radeon Vega 8 Graphics";
        // discrete boards carry a model number such as "AMD Radeon RX 7800 XT".
        internal static bool IsIntegratedAmdName(string lowerName)
        {
            return (lowerName.Contains("radeon") || lowerName.Contains("amd")) &&
                   lowerName.EndsWith("graphics") && !lowerName.Contains(" rx") && !lowerName.Contains(" pro ");
        }

        // The discrete adapter with the most memory, and the first integrated adapter.
        internal static void Select(IEnumerable<GpuAdapterInfo> adapters, out GpuAdapterInfo igpu, out GpuAdapterInfo dgpu)
        {
            igpu = null; dgpu = null;
            foreach (var adapter in adapters)
            {
                adapter.Kind = ClassifyGpu(adapter.Name, adapter.DedicatedBytes);
                if (adapter.Kind == "dgpu" && (dgpu == null || adapter.DedicatedBytes > dgpu.DedicatedBytes)) dgpu = adapter;
                else if (adapter.Kind == "igpu" && igpu == null) igpu = adapter;
            }
        }

        internal static string FormatLuid(long luid)
        {
            return string.Format("0x{0:x8}_0x{1:x8}", (uint)((luid >> 32) & 0xFFFFFFFF), (uint)(luid & 0xFFFFFFFF));
        }
    }

    // Driver-reported adapter sensors through the WDDM 2.4+ kernel thunks, the same source as
    // Task Manager: no vendor tool, no administrator rights. Zero means "not reported".
    internal sealed class WddmGpuSample
    {
        internal double? TemperatureC, MemoryClockMhz, PowerPercent;
        internal int? FanRpm;

        internal static WddmGpuSample Parse(ulong memoryFrequencyHz, uint fanRpm, uint powerTenthsPercent, uint temperatureDeciC, uint maxFanRpm)
        {
            var sample = new WddmGpuSample();
            if (temperatureDeciC > 0 && temperatureDeciC < 1500) sample.TemperatureC = Math.Round(temperatureDeciC / 10.0, 1);
            if (memoryFrequencyHz > 0 && memoryFrequencyHz < 100000000000UL) sample.MemoryClockMhz = Math.Round(memoryFrequencyHz / 1000000.0);
            if (powerTenthsPercent > 0 && powerTenthsPercent <= 10000) sample.PowerPercent = powerTenthsPercent / 10.0;
            // A stopped fan reads 0 RPM: only trusted when the driver declares a fan.
            if (maxFanRpm > 0 && fanRpm <= 50000) sample.FanRpm = (int)fanRpm;
            return sample.TemperatureC.HasValue || sample.MemoryClockMhz.HasValue || sample.PowerPercent.HasValue || sample.FanRpm.HasValue ? sample : null;
        }
    }

    internal static class WddmGpuSensors
    {
        const int KMTQAITYPE_ADAPTERPERFDATA = 62;
        const int KMTQAITYPE_ADAPTERPERFDATA_CAPS = 63;

        [StructLayout(LayoutKind.Sequential)]
        struct D3DKMT_OPENADAPTERFROMLUID { public uint LowPart; public int HighPart; public uint hAdapter; }
        [StructLayout(LayoutKind.Sequential)]
        struct D3DKMT_QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }
        // D3DKMT_ADAPTER_PERFDATA (64 bytes on x64).
        [StructLayout(LayoutKind.Sequential)]
        struct ADAPTER_PERFDATA
        {
            public uint PhysicalAdapterIndex;
            public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
            public uint FanRPM, Power, Temperature;
            public byte PowerStateOverride;
        }
        // D3DKMT_ADAPTER_PERFDATACAPS (40 bytes on x64).
        [StructLayout(LayoutKind.Sequential)]
        struct ADAPTER_PERFDATACAPS
        {
            public uint PhysicalAdapterIndex;
            public ulong MaxMemoryBandwidth, MaxPCIEBandwidth;
            public uint MaxFanRPM, TemperatureMax, TemperatureWarning;
        }

        [DllImport("gdi32.dll")] static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID args);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO args);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER args);

        internal static int PerfDataSize { get { return Marshal.SizeOf(typeof(ADAPTER_PERFDATA)); } }
        internal static int PerfCapsSize { get { return Marshal.SizeOf(typeof(ADAPTER_PERFDATACAPS)); } }
        internal static int QueryInfoSize { get { return Marshal.SizeOf(typeof(D3DKMT_QUERYADAPTERINFO)); } }

        static volatile bool unsupported;

        // True when the LUID belongs to an adapter present in this boot (stale DirectX registry
        // entries keep old LUIDs); null when the thunks are missing.
        internal static bool? AdapterExists(long luid)
        {
            uint handle;
            int status = Open(luid, out handle);
            if (status == int.MinValue) return null;
            if (status != 0) return false;
            Close(handle);
            return true;
        }

        internal static WddmGpuSample Read(long luid)
        {
            uint handle;
            if (luid == 0 || Open(luid, out handle) != 0) return null;
            try
            {
                var data = new ADAPTER_PERFDATA();
                var caps = new ADAPTER_PERFDATACAPS();
                if (!Query(handle, KMTQAITYPE_ADAPTERPERFDATA, ref data)) return null;
                Query(handle, KMTQAITYPE_ADAPTERPERFDATA_CAPS, ref caps);
                return WddmGpuSample.Parse(data.MemoryFrequency, data.FanRPM, data.Power, data.Temperature, caps.MaxFanRPM);
            }
            finally { Close(handle); }
        }

        // int.MinValue when gdi32 lacks the thunks (very old Windows).
        static int Open(long luid, out uint handle)
        {
            handle = 0;
            if (unsupported) return int.MinValue;
            try
            {
                var open = new D3DKMT_OPENADAPTERFROMLUID { LowPart = (uint)(luid & 0xFFFFFFFF), HighPart = (int)(luid >> 32) };
                int status = D3DKMTOpenAdapterFromLuid(ref open);
                handle = open.hAdapter;
                return status;
            }
            catch (Exception) { unsupported = true; return int.MinValue; }
        }

        static void Close(uint handle)
        {
            try { var close = new D3DKMT_CLOSEADAPTER { hAdapter = handle }; D3DKMTCloseAdapter(ref close); } catch { }
        }

        static bool Query<T>(uint adapter, int type, ref T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, buffer, false);
                var query = new D3DKMT_QUERYADAPTERINFO { hAdapter = adapter, Type = type, pPrivateDriverData = buffer, PrivateDriverDataSize = (uint)size };
                if (D3DKMTQueryAdapterInfo(ref query) != 0) return false;
                value = (T)Marshal.PtrToStructure(buffer, typeof(T));
                return true;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    // Current device power state from Plug and Play. A discrete GPU in D3 is runtime-suspended:
    // polling it can wake it up and drain the battery.
    internal static class DevicePower
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY { public Guid fmtid; public uint pid; }
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);

        // 0 = unknown, 1 = D0 (on) ... 4 = D3 (off).
        internal static int State(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return 0;
            try
            {
                uint devInst;
                if (CM_Locate_DevNodeW(out devInst, instanceId, 0) != 0) return 0;
                // DEVPKEY_Device_PowerData -> CM_POWER_DATA.PD_MostRecentPowerState
                var key = new DEVPROPKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 32 };
                byte[] buffer = new byte[64];
                uint size = (uint)buffer.Length, type;
                if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0) != 0 || size < 8) return 0;
                int state = BitConverter.ToInt32(buffer, 4);
                return state >= 1 && state <= 4 ? state : 0;
            }
            catch (Exception) { return 0; }
        }

        internal static bool IsSuspended(string instanceId) { return State(instanceId) >= 2; }
    }
}
