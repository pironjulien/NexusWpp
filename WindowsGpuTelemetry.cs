using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

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
        // Two WMI snapshots for all adapters, replacing repeated per-adapter queries.
        internal static WindowsGpuSample[] Read(string igpu,string dgpu,string npu) {
            string[] luids={igpu,dgpu,npu};var result=new[]{new WindowsGpuSample(),new WindowsGpuSample(),new WindowsGpuSample()};
            try {
                var rows=new List<GpuEngineReading>();
                using(var query=new ManagementObjectSearcher("SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")) {
                    query.Options.Timeout=TimeSpan.FromSeconds(2);
                    foreach(ManagementObject row in query.Get()) rows.Add(new GpuEngineReading {Name=Convert.ToString(row["Name"]),Utilization=Convert.ToInt32(row["UtilizationPercentage"]??0)});
                }
                for(int i=0;i<3;i++) {result[i].Utilization=BusiestEngine(rows,luids[i],i==2?"Compute":"3D");result[i].Decode=BusiestEngine(rows,luids[i],"VideoDecode");}
            } catch(ManagementException) { } catch(System.Runtime.InteropServices.COMException) { }
            try {
                using(var query=new ManagementObjectSearcher("SELECT Name,SharedUsage,DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory")) {
                    query.Options.Timeout=TimeSpan.FromSeconds(2);
                    foreach(ManagementObject row in query.Get()) {
                        string name=Convert.ToString(row["Name"]);
                        for(int i=0;i<3;i++) if(!string.IsNullOrEmpty(luids[i]) && name.IndexOf(luids[i],StringComparison.OrdinalIgnoreCase)>=0) {
                            result[i].SharedBytes=Math.Max(0,Convert.ToInt64(row["SharedUsage"]??0));
                            result[i].DedicatedBytes=Math.Max(0,Convert.ToInt64(row["DedicatedUsage"]??0));
                        }
                    }
                }
            } catch(ManagementException) { } catch(System.Runtime.InteropServices.COMException) { }
            return result;
        }
    }
}
