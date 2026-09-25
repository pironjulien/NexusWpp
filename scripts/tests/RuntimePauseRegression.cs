using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Executes the shipped page in a real WebView2 renderer, with a test-only host.
// Complements physical desktop captures; this does not measure reveal latency.
class RuntimePauseRegression : Form
{
    readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
    readonly string project;
    bool startPaused = true;
    bool pageReady;
    int exitCode = 1;

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (var form = new RuntimePauseRegression(args[0]))
        {
            Application.Run(form);
            return form.exitCode;
        }
    }

    RuntimePauseRegression(string root)
    {
        project = root;
        Text = "NexusWpp — vérification automatique de la pause";
        ClientSize = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(view);
        Shown += Run;
    }

    async Task Check(string expression, string label)
    {
        string actual = await view.CoreWebView2.ExecuteScriptAsync("Boolean(" + expression + ")");
        if (actual != "true") throw new Exception(label + " (" + actual + ")");
        Console.WriteLine("PASS " + label);
    }

    async Task Ready()
    {
        for (int i = 0; i < 100 && !pageReady; i++) await Task.Delay(100);
        if (!pageReady) throw new Exception("Runtime-state handshake missing");
        await Task.Delay(250);
    }

    void Control(bool paused)
    {
        view.CoreWebView2.PostWebMessageAsJson(paused ? "{\"control\":\"SUSPEND\"}" : "{\"control\":\"RESUME\"}");
    }

    async void Run(object sender, EventArgs args)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(project, "work", "runtime-pause-test-profile"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("nexuswpp.local", project, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived += delegate(object source, CoreWebView2WebMessageReceivedEventArgs message)
            {
                if (message.TryGetWebMessageAsString() == "REQUEST_RUNTIME_STATE")
                {
                    Control(startPaused);
                    pageReady = true;
                }
            };
            view.Source = new Uri("http://nexuswpp.local/index.html");
            await Ready();
            await Check("runtimeSuspended && hostRuntimeSuspended && telemetryNodesInitialized", "startup covered paints real canvas and receives host pause");
            await Check("ctx.getImageData(0,0,width,height).data.some((v,i) => i % 4 === 3 && v > 0)", "startup covered contains painted scene pixels before resume");
            await Check("getComputedStyle(document.querySelector('.dashboard')).opacity === '1'", "startup covered keeps dashboard readable");
            await Check("!canvasFrameTimer && !canvasAnimationFrame && !clockTimer", "startup covered has no animation or clock callbacks");

            Control(false);
            await Task.Delay(300);
            await Check("!runtimeSuspended && clockTimer !== 0", "resume restarts clock");
            await view.CoreWebView2.ExecuteScriptAsync(@"
                document.body.classList.add('system-critical');
                document.querySelector('.gauge-card').classList.add('overload');
                spawnCoreSplash(telemetryNodes.npu.x, telemetryNodes.npu.y, '#ffffff');
                dataPackets.push(new DataPacket(telemetryNodes.cpu, '#ffffff'));
                wakeCanvas();");
            await Task.Delay(50);
            Control(true);
            await Task.Delay(150);
            await view.CoreWebView2.ExecuteScriptAsync(@"
                window.pauseTest = {
                    pixels: canvas.toDataURL(), particles: JSON.stringify(coreParticles),
                    packets: JSON.stringify(dataPackets), angles: telemetryNodeList.map(n => n.rotAngle).join(','),
                    clock: document.getElementById('clock-s').textContent,
                    filter: getComputedStyle(document.querySelector('.cosmos-bg')).filter
                };");
            await Task.Delay(1200);
            await Check("canvas.toDataURL() === pauseTest.pixels", "paused canvas preserves exact pixel buffer");
            await Check("coreParticles.length > 0 && dataPackets.length > 0 && JSON.stringify(coreParticles) === pauseTest.particles && JSON.stringify(dataPackets) === pauseTest.packets", "pause retains particles and packets without advancing them");
            await Check("telemetryNodeList.map(n => n.rotAngle).join(',') === pauseTest.angles", "pause retains orbit angles");
            await Check("document.getElementById('clock-s').textContent === pauseTest.clock && !clockTimer", "clock stays frozen under cover");
            await Check("document.body.classList.contains('system-critical')", "pause preserves actual critical state");
            Console.WriteLine("ANIMATIONS " + await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify(document.getAnimations().filter(a => a.playState === 'running').map(a => ({type:a.constructor.name,name:a.animationName,property:a.transitionProperty,target:a.effect.target.className})))"));
            await Check("document.getAnimations().every(a => a.playState !== 'running')", "all CSS animations including overload are paused");
            await Check("getComputedStyle(document.querySelector('.cosmos-bg')).filter === pauseTest.filter", "pause retains in-flight CSS transition appearance");

            await view.CoreWebView2.ExecuteScriptAsync("resizeCanvas();");
            await Check("canvas.toDataURL() === pauseTest.pixels", "same-size resize does not clear canvas");
            ClientSize = new Size(1160, 720);
            await Task.Delay(250);
            await Check("runtimeSuspended && !canvasFrameTimer && !canvasAnimationFrame && coreParticles.length > 0", "real resize repaints without starting animation");
            await Check("ctx.getImageData(0,0,width,height).data.some((v,i) => i % 4 === 3 && v > 0)", "resized paused canvas contains real scene pixels");
            await Check("JSON.stringify(coreParticles) === pauseTest.particles", "resize does not advance particle lifetime");

            // Host RESUME while document is hidden must not start work.
            view.Visible = false;
            await Task.Delay(150);
            Control(false);
            await Task.Delay(150);
            await Check("document.hidden && runtimeSuspended && !clockTimer", "hidden document remains paused despite host resume");
            view.Visible = true;
            await Task.Delay(300);
            await Check("!document.hidden && !runtimeSuspended && clockTimer !== 0", "document visibility resumes the existing scene");
            for (int i = 0; i < 20; i++) { Control(true); Control(false); }
            Control(true);
            await Task.Delay(200);
            await Check("runtimeSuspended && !canvasAnimationFrame && !canvasFrameTimer && !clockTimer", "rapid transitions leave no stale animation callbacks");

            startPaused = true;
            pageReady = false;
            view.CoreWebView2.Reload();
            await Ready();
            await Check("runtimeSuspended && telemetryNodesInitialized && !clockTimer", "navigation while covered re-synchronizes pause");
            await Check("ctx.getImageData(0,0,width,height).data.some((v,i) => i % 4 === 3 && v > 0)", "navigation while covered paints scene pixels before resume");
            exitCode = 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error); }
        finally { Close(); }
    }
}
