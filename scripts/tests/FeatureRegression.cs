using System;
using DesktopHtmlHost;
namespace DesktopHtmlHost { static class Program { internal static void LogDebug(string message) {} } }
class FeatureRegression
{
    static void Check(bool value,string name) {if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static int Main(string[] args) {
        try {
            var sample=NvidiaSample.Parse("53, 64, 1785, [N/A], 3.87, 16376, 900");
            Check(sample!=null && sample.PowerWatts==3.87 && sample.MemoryClock==null && sample.Utilization==53,"unsupported field preserves other NVIDIA readings");
            Check(NvidiaSample.Parse("N/A,N/A,N/A,N/A,N/A,N/A,N/A")==null,"fully unavailable NVIDIA sample");
            sample=NvidiaSample.Parse("101, -2, 1200, 500, NaN, 8192, 9000");
            Check(sample.Utilization==null && sample.Temperature==null && sample.PowerWatts==null && sample.UsedMiB==null,"non-finite, invalid ranges and inconsistent VRAM rejected");
            var nv=new NvidiaTelemetry();nv.Accept(NvidiaSample.Parse("0,31,210,405,3.8,16384,256"),0);
            Check(nv.Current!=null && nv.Status=="fresh","successful sensor sample cached");
            nv.Accept(null,10);Check(nv.Current==null && nv.Status=="stale","failed read invalidates all previous live values immediately");
            nv.Accept(NvidiaSample.Parse("1,32,N/A,N/A,4.1,N/A,N/A"),20);
            Check(nv.Current.PowerWatts==4.1 && nv.Current.CoreClock==null,"recovery never revives old unsupported fields");
            var engines=new[]{new GpuEngineReading{Name="pid_1_luid_A_phys_0_eng_0_engtype_3D",Utilization=30},new GpuEngineReading{Name="pid_2_luid_A_phys_0_eng_0_engtype_3D",Utilization=40},new GpuEngineReading{Name="pid_1_luid_A_phys_0_eng_1_engtype_3D",Utilization=50},new GpuEngineReading{Name="pid_1_luid_B_phys_0_eng_0_engtype_3D",Utilization=99}};
            Check(WindowsGpuTelemetry.BusiestEngine(engines,"luid_A","3D")==70,"GPU processes aggregate within each engine, separate engines and adapters stay distinct");
            Check(WindowsGpuTelemetry.BusiestEngine(engines,"","3D")==-1,"missing adapter is unavailable instead of idle");
            return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
