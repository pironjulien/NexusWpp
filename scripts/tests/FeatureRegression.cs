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
            var npuEngines=new[]{
                new GpuEngineReading{Name="pid_1_luid_NPU_phys_0_eng_0_engtype_Neural",Utilization=30},
                new GpuEngineReading{Name="pid_2_luid_NPU_phys_0_eng_0_engtype_Neural",Utilization=40},
                new GpuEngineReading{Name="pid_1_luid_NPU_phys_0_eng_1_engtype_Neural",Utilization=50},
                new GpuEngineReading{Name="pid_1_luid_NPU_phys_0_eng_2_engtype_Compute",Utilization=65},
                new GpuEngineReading{Name="pid_1_luid_OTHER_phys_0_eng_0_engtype_Neural",Utilization=99}
            };
            Check(WindowsGpuTelemetry.BusiestNpuEngine(npuEngines,"luid_NPU")==70,"Neural processes aggregate per engine without adding distinct engines or adapters");
            var computeOnly=new[]{new GpuEngineReading{Name="pid_1_luid_NPU_phys_0_eng_0_engtype_Compute",Utilization=42}};
            Check(WindowsGpuTelemetry.BusiestNpuEngine(computeOnly,"luid_NPU")==42,"Compute-only NPU drivers remain supported");
            var idleNpu=new[]{new GpuEngineReading{Name="pid_1_luid_NPU_phys_0_eng_0_engtype_nEuRaL",Utilization=0}};
            Check(WindowsGpuTelemetry.BusiestNpuEngine(idleNpu,"luid_NPU")==0,"measured idle Neural engine is zero");
            idleNpu[0].Utilization=18;
            Check(WindowsGpuTelemetry.BusiestNpuEngine(idleNpu,"luid_NPU")==18,"Neural engine names are case insensitive");
            Check(WindowsGpuTelemetry.BusiestNpuEngine(npuEngines,"")==-1,"missing NPU adapter remains unavailable");

            // Busiest NPU client process.
            var npuClients=new[]{
                new GpuEngineReading{Name="pid_10_luid_0x0_0xNPU_phys_0_eng_0_engtype_Neural",Utilization=20},
                new GpuEngineReading{Name="pid_10_luid_0x0_0xNPU_phys_0_eng_0_engtype_Neural",Utilization=15},
                new GpuEngineReading{Name="pid_22_luid_0x0_0xNPU_phys_0_eng_1_engtype_Compute",Utilization=30},
                new GpuEngineReading{Name="pid_33_luid_0x0_0xGPU_phys_0_eng_0_engtype_3D",Utilization=90}};
            int topPid,topPct;
            WindowsGpuTelemetry.TopProcess(npuClients,"luid_0x0_0xNPU",new[]{"Neural","Compute"},out topPid,out topPct);
            Check(topPid==10 && topPct==35,"top NPU process sums one process's instances on an engine and ignores other adapters");
            WindowsGpuTelemetry.TopProcess(new[]{new GpuEngineReading{Name="pid_10_luid_0x0_0xNPU_phys_0_eng_0_engtype_Neural",Utilization=0}},"luid_0x0_0xNPU",new[]{"Neural"},out topPid,out topPct);
            Check(topPid==-1 && topPct==0,"idle NPU has no top process");
            WindowsGpuTelemetry.TopProcess(npuClients,"",new[]{"Neural"},out topPid,out topPct);
            Check(topPid==-1,"no NPU adapter, no top process");

            // Battery power flow (CallNtPowerInformation SystemBatteryState).
            Check(System.Runtime.InteropServices.Marshal.SizeOf(typeof(BatteryState.SYSTEM_BATTERY_STATE))==32,"SYSTEM_BATTERY_STATE is 32 bytes");
            var battery=BatteryState.FromRaw(new BatteryState.SYSTEM_BATTERY_STATE{BatteryPresent=1,Rate=unchecked((uint)-12400),EstimatedTime=5400});
            Check(battery.RateMilliwatts==-12400 && battery.EstimatedSeconds==5400,"discharge rate is negative milliwatts with Windows' estimate");
            battery=BatteryState.FromRaw(new BatteryState.SYSTEM_BATTERY_STATE{BatteryPresent=1,Rate=0x80000000,EstimatedTime=0xFFFFFFFF});
            Check(battery.RateMilliwatts==int.MinValue && battery.EstimatedSeconds==-1,"unknown battery rate and time stay unknown");
            BatteryState live;
            Console.WriteLine("INFO battery state readable on this machine: "+BatteryState.TryRead(out live)+(live.RateMilliwatts!=int.MinValue?" rate "+live.RateMilliwatts+" mW":""));

            // Native performance counters (PDH) replacing WMI formatted-data queries.
            using(var perf=new PdhQuery()) {
                Check(perf.IsOpen,"PDH query opens");
                Check(perf.Add(@"\System\Processes") && perf.Add(@"\Memory\Cache Bytes") && perf.Add(@"\Memory\Page Faults/sec"),"English counter paths resolve on this Windows language");
                Check(!perf.Add(@"\NexusWpp Missing\Nothing"),"missing counter is reported, not fabricated");
                Check(perf.Value(@"\NexusWpp Missing\Nothing")==null,"missing counter has no value");
                Check(perf.Add(@"\Processor(*)\% Processor Time"),"wildcard counter resolves");
                perf.Collect();System.Threading.Thread.Sleep(250);
                var watch=System.Diagnostics.Stopwatch.StartNew();
                Check(perf.Collect(),"PDH sample succeeds");
                watch.Stop();
                Check(perf.Value(@"\System\Processes")>10 && perf.Value(@"\Memory\Cache Bytes")>0,"live system values are real");
                Check(perf.Value(@"\Memory\Page Faults/sec")>=0,"rate counter is valid after two samples");
                var cpus=perf.Instances(@"\Processor(*)\% Processor Time");
                Check(cpus!=null && cpus.Count>=2 && cpus.Exists(c=>c.Key=="_Total"),"wildcard returns every instance with names");
                Check(watch.ElapsedMilliseconds<200,"PDH sample stays fast ("+watch.ElapsedMilliseconds+" ms)");
            }

            // GPU classification by dedicated memory (DirectX registry), one function for every vendor.
            Check(GpuClassifier.ClassifyGpu("Intel(R) Arc(TM) Graphics",134217728)=="igpu","Intel Arc iGPU (128 MiB carve-out) is integrated");
            Check(GpuClassifier.ClassifyGpu("Intel(R) Arc(TM) A770 Graphics",16L<<30)=="dgpu","Intel Arc A770 with 16 GiB is discrete");
            Check(GpuClassifier.ClassifyGpu("Intel(R) Arc(TM) B580 Graphics",0)=="dgpu","Intel Arc B580 without a size is discrete by model");
            Check(GpuClassifier.ClassifyGpu("Intel(R) UHD Graphics 770",0)=="igpu","Intel UHD without a size is integrated");
            Check(GpuClassifier.ClassifyGpu("NVIDIA GeForce RTX 4070 Laptop GPU",8334082048)=="dgpu","NVIDIA laptop GPU is discrete");
            Check(GpuClassifier.ClassifyGpu("AMD Radeon(TM) Graphics",2L<<30)=="igpu","AMD APU with a 2 GiB UMA carve-out stays integrated");
            Check(GpuClassifier.ClassifyGpu("AMD Radeon(TM) 780M Graphics",512L<<20)=="igpu","Radeon 780M APU is integrated");
            Check(GpuClassifier.ClassifyGpu("AMD Radeon RX 7800 XT",16L<<30)=="dgpu","Radeon RX board is discrete");
            Check(GpuClassifier.ClassifyGpu("Qualcomm(R) Adreno(TM) X1-85 GPU",0)=="igpu","Snapdragon Adreno is integrated");
            Check(GpuClassifier.ClassifyGpu("Microsoft Basic Render Driver",0)=="" && GpuClassifier.ClassifyGpu("",0)=="","software adapters are ignored");
            var adapters=new[]{
                new GpuAdapterInfo{Name="Intel(R) Arc(TM) Graphics",DedicatedBytes=134217728},
                new GpuAdapterInfo{Name="Microsoft Basic Render Driver"},
                new GpuAdapterInfo{Name="Intel(R) Arc(TM) A380 Graphics",DedicatedBytes=6L<<30},
                new GpuAdapterInfo{Name="NVIDIA GeForce RTX 4070 Laptop GPU",DedicatedBytes=8334082048}};
            GpuAdapterInfo igpu,dgpu;GpuClassifier.Select(adapters,out igpu,out dgpu);
            Check(igpu==adapters[0] && dgpu==adapters[3],"selection keeps the first iGPU and the discrete GPU with the most memory");
            Check(GpuClassifier.FormatLuid(0x152a4)=="0x00000000_0x000152a4","LUID formatted like GPU counter instances");

            // Driver-reported WDDM sensors (D3DKMT): struct layout and value rules.
            Check(WddmGpuSensors.PerfDataSize==64 && WddmGpuSensors.PerfCapsSize==40 && WddmGpuSensors.QueryInfoSize==24,"D3DKMT structures match the x64 layout");
            var wddm=WddmGpuSample.Parse(8001000000UL,0,0,524,0);
            Check(wddm!=null && wddm.TemperatureC==52.4 && wddm.MemoryClockMhz==8001 && wddm.FanRpm==null && wddm.PowerPercent==null,"deci-Celsius and Hz converted, unreported fan and power stay unavailable");
            Check(WddmGpuSample.Parse(0,0,0,0,0)==null,"driver without sensors reports nothing instead of zeros");
            wddm=WddmGpuSample.Parse(0,0,355,0,3200);
            Check(wddm.FanRpm==0 && wddm.PowerPercent==35.5,"stopped fan is real only when the driver declares a fan");
            using(var dx=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX")) {
                if(dx!=null) foreach(var key in dx.GetSubKeyNames()) using(var sub=dx.OpenSubKey(key)) {
                    object luid=sub.GetValue("AdapterLuid");if(luid==null||sub.GetValue("Description")==null)continue;
                    var read=WddmGpuSensors.Read(Convert.ToInt64(luid));
                    Console.WriteLine("INFO D3DKMT {0} [{1}]: {2}",sub.GetValue("Description"),GpuClassifier.ClassifyGpu(Convert.ToString(sub.GetValue("Description")),Convert.ToInt64(sub.GetValue("DedicatedVideoMemory")??0L)),
                        read==null?"no sensor":string.Format("temp={0} memClock={1} fan={2} power%={3}",read.TemperatureC,read.MemoryClockMhz,read.FanRpm,read.PowerPercent));
                    Check(read==null || read.TemperatureC==null || (read.TemperatureC>0 && read.TemperatureC<150),"live D3DKMT reading is plausible or absent");
                }
            }

            // nvidia-smi back-off.
            Check(NvidiaTelemetry.NextIntervalMs(0,false)==5000 && NvidiaTelemetry.NextIntervalMs(0,true)==15000,"nvidia-smi every 5 s on mains, 15 s on battery or energy saver");
            Check(NvidiaTelemetry.NextIntervalMs(1,false)==10000 && NvidiaTelemetry.NextIntervalMs(9,false)==60000,"failed nvidia-smi runs back off up to 60 s");
            var other=new NvidiaTelemetry();
            Check(!other.IsAvailable("AMD Radeon RX 7800 XT") && other.Read("AMD Radeon RX 7800 XT",false,false)==null && other.Status=="unavailable","nvidia-smi is never spawned for another vendor");

            // Network: identity filter, PDH instance names, Wi-Fi signal without location consent.
            Check(NetworkTelemetry.PdhInstanceName("Intel(R) Wi-Fi 6E AX211 160MHz")=="Intel[R] Wi-Fi 6E AX211 160MHz" && NetworkTelemetry.PdhInstanceName("Realtek PCIe GbE #2")=="Realtek PCIe GbE _2","adapter descriptions map to PDH instance names");
            Check(!NetworkTelemetry.IsCountedAdapter((System.Net.NetworkInformation.NetworkInterfaceType)53,"Tailscale Tunnel","Tailscale") && !NetworkTelemetry.IsCountedAdapter(System.Net.NetworkInformation.NetworkInterfaceType.Tunnel,"x","y") && !NetworkTelemetry.IsCountedAdapter(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,"Hyper-V Virtual Ethernet Adapter","vEthernet (WSL)"),"virtual and tunnel adapters are not counted");
            Check(NetworkTelemetry.IsCountedAdapter(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,"Realtek PCIe GbE Family Controller","Ethernet"),"physical Ethernet is counted");
            Check(NetworkTelemetry.RssiToQuality(-100)==0 && NetworkTelemetry.RssiToQuality(-75)==50 && NetworkTelemetry.RssiToQuality(-50)==100 && NetworkTelemetry.RssiToQuality(-30)==100 && NetworkTelemetry.RssiToQuality(-120)==0,"RSSI dBm converted to the Windows 0-100 quality scale");
            if(args.Length>1) {
                // Windows 11 24H2 gates wlan_intf_opcode_current_connection (7) behind location consent.
                var calls=new System.Collections.Generic.List<string>();
                foreach(var file in System.IO.Directory.GetFiles(args[1],"*.cs"))
                    foreach(System.Text.RegularExpressions.Match call in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(file),@"WlanQueryInterface\s*\(([^;]*)\)\s*;"))
                        if(!call.Groups[1].Value.Contains("IntPtr ")) calls.Add(call.Groups[1].Value); // skip the P/Invoke declaration
                Check(calls.Count>0 && calls.TrueForAll(c=>{var parts=c.Split(',');return parts.Length>=3 && parts[2].Trim()=="WlanOpcodeRssi";}),"every WLAN query uses wlan_intf_opcode_rssi, never the location-gated opcode 7");
                Check(System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(System.IO.Path.Combine(args[1],"NetworkTelemetry.cs")),@"WlanOpcodeRssi\s*=\s*0x10000102;"),"RSSI opcode value");
            }
            Check(NetworkTelemetry.WlanOpcodeRssi==0x10000102,"RSSI opcode constant");
            using(var perf=new PdhQuery()) {
                var net=new NetworkTelemetry();net.AddCounters(perf);perf.Collect();System.Threading.Thread.Sleep(300);
                bool sampled=perf.Collect();net.Update(perf,sampled);
                Console.WriteLine("INFO network type={0} name={1} rx={2} KiB/s tx={3} KiB/s link={4} Mb/s",net.Type,net.Name,net.RxKBps,net.TxKBps,net.LinkSpeedMbps);
                Check(net.Type=="none" ? net.Name=="" && net.Ip=="" && net.LinkSpeedMbps==0 : net.Name!="" && net.Ip!="","offline network has no invented identity");
                Check(net.RxKBps>=0 && net.TxKBps>=0,"network throughput from PDH is measured");
            }

            // Power panel: kinds from GUIDs and personality, overlays for single-plan PCs.
            Check(PowerSchemes.KindFromGuid("A1841308-3541-4fab-bc81-f71556f20b4a")=="eco" && PowerSchemes.KindFromGuid("e9a42b02-d5df-448d-aa00-03f14749eb61")=="performance" && PowerSchemes.KindFromGuid(PowerSchemes.OverlayBalanced)=="balanced" && PowerSchemes.KindFromGuid("db1209e5-2442-4354-9127-ce97b5e306cd")==null,"known plan and mode GUIDs");
            Check(PowerSchemes.KindFromPersonality(0)=="performance" && PowerSchemes.KindFromPersonality(1)=="eco" && PowerSchemes.KindFromPersonality(2)=="balanced","plan personality");
            Check(PowerSchemes.KindFromName("Économies d’énergie")=="eco" && PowerSchemes.KindFromName("MSI Recommended")=="balanced" && PowerSchemes.KindFromName("Performances élevées")=="performance" && PowerSchemes.KindFromName("Utilisation normale")=="balanced" && PowerSchemes.KindFromName("Performances extremes")=="performance" && PowerSchemes.KindFromName("Mon plan")==null,"explicit words of localized names, accents removed; silent names defer to the personality");
            var sorted=PowerSchemes.Sort(new[]{new PowerPlanInfo{Guid="c",Name="Turbo",Kind="performance"},new PowerPlanInfo{Guid="b",Name="Normal",Kind="balanced"},new PowerPlanInfo{Guid="a",Name="Silence",Kind="eco"}});
            Check(sorted[0].Kind=="eco" && sorted[1].Kind=="balanced" && sorted[2].Kind=="performance","buttons ordered by kind, not by localized name");
            var modes=PowerSchemes.OverlayModes("DED574B5-45A0-4F42-8737-46345C09C238",new System.Globalization.CultureInfo("fr-FR"));
            Check(modes.Count==3 && modes.TrueForAll(m=>m.Overlay) && modes[2].Active && !modes[0].Active && modes[1].Guid==PowerSchemes.OverlayBalanced && modes[1].Name=="Équilibré","Windows power modes with the active overlay marked");
            var plans=PowerSchemes.EnumeratePlans();
            Check(plans!=null && plans.Count>0 && plans.TrueForAll(p=>p.Name!="" && (p.Kind=="eco"||p.Kind=="balanced"||p.Kind=="performance")),"powrprof enumerates the installed plans ("+(plans==null?0:plans.Count)+")");
            foreach(var plan in plans) Console.WriteLine("INFO plan {0} {1} [{2}]",plan.Guid,plan.Name,plan.Kind);
            string overlay=PowerSchemes.EffectiveOverlay();
            Console.WriteLine("INFO effective power mode overlay: "+(overlay??"unavailable"));
            Check(overlay==null || Guid.TryParse(overlay,out _),"effective overlay is a GUID or unavailable");
            return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
