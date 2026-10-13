using System;
using System.Collections.Generic;
using System.Linq;

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
}
