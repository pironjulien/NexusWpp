using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// A/B measurements of an identical, test-only workload in an isolated WebView2.
// GPU numbers are process-group engine occupancy, not watts or DWM attribution.
class GraphicsProfile : Form
{
    readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly string project, assets, output;
    int result = 1;
    bool ready;
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles();
        using (var form = new GraphicsProfile(args)) { Application.Run(form); return form.result; }
    }
    GraphicsProfile(string[] args) {
        project = args[0]; assets = args[1]; output = args[2];
        Text = "NexusWpp — mesure graphique A/B";
        ClientSize = new Size(1600, 950); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(view); Shown += Run;
    }
    class Counter { public double Time, Stamp; }
    class Sample { public Dictionary<string, Counter> Gpu = new Dictionary<string, Counter>(); public double Cpu; public DateTime At; }
    Sample Snapshot() {
        var ids = new HashSet<int> { Process.GetCurrentProcess().Id };
        using (var q = new ManagementObjectSearcher("SELECT ProcessId,ParentProcessId FROM Win32_Process")) {
            var children = new List<Tuple<int, int>>();
            foreach (ManagementObject p in q.Get()) children.Add(Tuple.Create(Convert.ToInt32(p["ProcessId"]), Convert.ToInt32(p["ParentProcessId"])));
            bool change; do { change = false; foreach (var p in children) if (ids.Contains(p.Item2)) change |= ids.Add(p.Item1); } while (change);
        }
        var sample = new Sample { At = DateTime.UtcNow };
        foreach (int id in ids) try { using (var p = Process.GetProcessById(id)) sample.Cpu += p.TotalProcessorTime.TotalSeconds; } catch { }
        using (var q = new ManagementObjectSearcher("SELECT Name,RunningTime,Timestamp_Sys100NS FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine")) {
            foreach (ManagementObject p in q.Get()) {
                string name = Convert.ToString(p["Name"]); string[] parts = name.Split('_'); int pid;
                if (parts.Length < 3 || !int.TryParse(parts[1], out pid) || !ids.Contains(pid)) continue;
                sample.Gpu[name] = new Counter { Time = Convert.ToDouble(p["RunningTime"]), Stamp = Convert.ToDouble(p["Timestamp_Sys100NS"]) };
            }
        }
        return sample;
    }
    async Task<string> Script(string expression) {
        string response=await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",json.Serialize(new {expression=expression,returnByValue=true,awaitPromise=true}));
        if(response.Contains("\"exceptionDetails\""))throw new Exception(response);
        return response;
    }
    async void Run(object sender, EventArgs e) {
        try {
            Directory.CreateDirectory(output);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "profile"));
            await view.EnsureCoreWebView2Async(env);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("nexuswpp.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived+=delegate(object source,CoreWebView2WebMessageReceivedEventArgs message) {
                if(message.TryGetWebMessageAsString()=="REQUEST_RUNTIME_STATE")ready=true;
            };
            view.Source = new Uri("http://nexuswpp.local/index.html");
            for(int i=0;i<100 && !ready;i++)await Task.Delay(100);
            if(!ready)throw new Exception("Page did not complete the host handshake");
            string fixture = File.ReadAllText(Path.Combine(project, "scripts", "tests", "responsive_layout.js"));
            fixture = fixture.Replace("updateDOM(stats);", "window.profileStats = stats; updateDOM(stats);");
            await Script(fixture);
            await Script(@"layoutFixture({hardware:'full',plans:4,dpr:devicePixelRatio});
                setRuntimeSuspended(false); window.profileTick=0;
                window.profileLoop=setInterval(()=>{const s=window.profileStats;
                  s.cpu.utilization=25+Math.round(12*Math.sin(++window.profileTick/3));
                  s.gpu.utilization=35+Math.round(16*Math.cos(window.profileTick/4)); updateDOM(s);},500);
                window.profileStyle=document.createElement('style');document.head.append(profileStyle);");
            var variants = new Dictionary<string, string> {
                {"baseline", ".ring-fill{transition-duration:.55s!important}"},
                {"short-transitions", ".ring-fill{transition-duration:.18s!important}"},
                {"contained-layers", ".glass-panel{transform:none!important;will-change:auto!important;isolation:isolate}.ring-fill,#physics-canvas{will-change:auto!important}"},
                {"halos", ".ring-fill{filter:none!important}"},
                {"short-halos", ".ring-fill{transition-duration:.18s!important;filter:none!important}"}
            };
            var results = new List<object>();
            for (int run=0; run<3; run++) {
                var order = (run % 2 == 0 ? variants.Keys : variants.Keys.Reverse()).ToArray();
                foreach (string label in order) {
                    await Script("profileStyle.textContent=" + json.Serialize(variants[label]));
                    await Script("profileTick=0; dataPackets.length=0; coreParticles.length=0; Object.keys(lastPacketNodeTime).forEach(k=>delete lastPacketNodeTime[k]); setRuntimeSuspended(false); wakeCanvas();");
                    await Task.Delay(3000);
                    var before = await Task.Run((Func<Sample>)Snapshot);
                    await Task.Delay(10000);
                    var after = await Task.Run((Func<Sample>)Snapshot);
                    var engines = new Dictionary<string,double>();
                    foreach (var entry in after.Gpu) {
                        Counter old; if (!before.Gpu.TryGetValue(entry.Key, out old) || entry.Value.Stamp <= old.Stamp) continue;
                        string key = entry.Key.Substring(entry.Key.IndexOf('_',4)+1);
                        double value = Math.Max(0, (entry.Value.Time-old.Time)/(entry.Value.Stamp-old.Stamp)*100);
                        if (!engines.ContainsKey(key)) engines[key]=0; engines[key]+=value;
                    }
                    var row = new { variant=label, run=run+1, cpuCores=(after.Cpu-before.Cpu)/(after.At-before.At).TotalSeconds,
                        gpuMax=engines.Count>0?(double?)engines.Values.Max():null, engines=engines };
                    string state=await Script("({hidden:document.hidden,paused:runtimeSuspended,ticks:profileTick,valid:profileTick>=20,framesRunning:isCanvasLoopRunning,packets:dataPackets.length})");
                    if(state.Contains("\"hidden\":true") || state.Contains("\"paused\":true") || state.Contains("\"valid\":false")) throw new Exception("Invalid profile workload: "+state);
                    results.Add(row); Console.WriteLine(label+" run "+(run+1)+" GPU "+row.gpuMax+" CPU "+row.cpuCores+" "+state);
                    File.WriteAllText(Path.Combine(output,"results.json"),json.Serialize(new { runtime=env.BrowserVersionString, results=results }));
                }
            }
            result=0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); }
        finally { Close(); }
    }
}
