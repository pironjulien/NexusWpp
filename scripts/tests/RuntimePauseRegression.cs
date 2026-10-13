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
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
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
            // Records any resource or inline code blocked by the page's Content-Security-Policy.
            await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.cspViolations=[];document.addEventListener('securitypolicyviolation',e=>cspViolations.push(e.violatedDirective+' '+e.blockedURI));");
            view.Source = new Uri("http://nexuswpp.local/index.html");
            await Ready();
            await Check("document.querySelector('meta[http-equiv=\"Content-Security-Policy\"]') !== null", "page declares a Content-Security-Policy");
            await Check("runtimeSuspended && hostRuntimeSuspended && telemetryNodesInitialized", "startup covered paints real canvas and receives host pause");
            await Check("ctx.getImageData(0,0,width,height).data.some((v,i) => i % 4 === 3 && v > 0)", "startup covered contains painted scene pixels before resume");
            await Check("getComputedStyle(document.querySelector('.dashboard')).opacity === '1'", "startup covered keeps dashboard readable");
            await Check("!canvasFrameTimer && !canvasAnimationFrame && !clockTimer", "startup covered has no animation or clock callbacks");

            Control(false);
            await Task.Delay(300);
            await Check("!runtimeSuspended && clockTimer !== 0", "resume restarts clock");
            await view.CoreWebView2.ExecuteScriptAsync(@"
                document.querySelector('.gauge-card').classList.add('high-load');
                setCircularProgress(cpuRing, 75);
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
                    border: getComputedStyle(document.querySelector('.gauge-card')).borderColor,
                    ring: cpuRing.style.strokeDashoffset,
                    spinners: logoSpinners.map(s => s.element.style.transform).join(',')
                };");
            await Task.Delay(1200);
            await Check("cpuRing.style.strokeDashoffset === pauseTest.ring && logoSpinners.map(s => s.element.style.transform).join(',') === pauseTest.spinners", "pause holds gauge rings and logo spinners where they are");
            await Check("canvas.toDataURL() === pauseTest.pixels", "paused canvas preserves exact pixel buffer");
            await Check("coreParticles.length > 0 && dataPackets.length > 0 && JSON.stringify(coreParticles) === pauseTest.particles && JSON.stringify(dataPackets) === pauseTest.packets", "pause retains particles and packets without advancing them");
            await Check("telemetryNodeList.map(n => n.rotAngle).join(',') === pauseTest.angles", "pause retains orbit angles");
            await Check("document.getElementById('clock-s').textContent === pauseTest.clock && !clockTimer", "clock stays frozen under cover");
            await Check("document.querySelector('.gauge-card').classList.contains('high-load')", "pause preserves displayed load state");
            Console.WriteLine("ANIMATIONS " + await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify(document.getAnimations().filter(a => a.playState === 'running').map(a => ({type:a.constructor.name,name:a.animationName,property:a.transitionProperty,target:a.effect.target.className})))"));
            await Check("document.getAnimations().every(a => a.playState !== 'running')", "all CSS animations and gauge transitions are paused");
            await Check("getComputedStyle(document.querySelector('.gauge-card')).borderColor === pauseTest.border", "pause retains in-flight CSS transition appearance");

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

            // Readings never seen show a dash, on the cards and on the radar.
            await view.CoreWebView2.ExecuteScriptAsync(@"
                window.drawnLabels=[];const firstText=ctx.fillText;
                ctx.fillText=function(text,...args){drawnLabels.push(String(text));firstText.call(this,text,...args);};
                redrawCanvas();ctx.fillText=firstText;");
            await Check("[...document.querySelectorAll('.gauge-value')].every(v=>v.textContent==='—') && cpuGaugeContainer.getAttribute('aria-valuetext')===t('aria.waiting') && drawnLabels.filter(text=>text==='—').length>=5", "readings never seen show a dash on gauges and radar");

            // The fixture is exposed so tests can feed variations of the same sample.
            await view.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(Path.Combine(project,"scripts","tests","responsive_layout.js")).Replace("updateDOM(stats);", "window.fixtureStats = stats; updateDOM(stats);"));
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'stale'});");
            await Check("gpuTempSub.textContent==='—' && gpuTempSub.classList.contains('sensor-unavailable') && gpuTempSub.title===t('sensor.stale')", "a sensor that never produced a value shows a dash");
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});layoutFixture({hardware:'full',plans:4,dpr:1,state:'stale'});");
            await Check("gpuTempSub.textContent===num(76)+' °C' && gpuCoreSub.textContent===num(575)+' W' && gpuTempSub.classList.contains('sensor-unavailable') && gpuTempSub.title===t('sensor.held') && gpuCard.classList.contains('sensor-stale')", "a stale sensor keeps its last value, greyed, with the stale badge");

            // A missing sample keeps the last valid reading everywhere, flagged stale; the history line breaks.
            await view.CoreWebView2.ExecuteScriptAsync(@"
                layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});
                const missing=JSON.parse(JSON.stringify(fixtureStats));
                missing.cpu.utilization=-1;missing.gpu.utilization=null;missing.igpu.usedMb=-1;missing.ping=-1;
                window.ringBefore=cpuRing.style.strokeDashoffset;updateDOM(missing);
                window.drawnLabels=[];const heldText=ctx.fillText;
                ctx.fillText=function(text,...args){drawnLabels.push(String(text)+'@'+this.fillStyle);heldText.call(this,text,...args);};
                redrawCanvas();ctx.fillText=heldText;");
            await Check("cpuVal.textContent==='48%' && gpuVal.textContent==='73%' && cpuRing.style.strokeDashoffset===ringBefore", "a missing reading keeps the last gauge value and ring");
            await Check("cpuGaugeContainer.getAttribute('aria-valuenow')==='48' && cpuGaugeContainer.getAttribute('aria-valuetext')===t('aria.stale',{value:'48 %'}) && cpuCard.classList.contains('sensor-stale') && cpuCard.querySelector('.critical-alert').textContent===t('alert.sensorsStale')", "a held gauge is badged and announced as stale");
            await Check("drawnLabels.includes('48%@#afb7c7') && drawnLabels.includes('73%@#afb7c7') && !drawnLabels.some(text=>/^(-1|null|—)%?@/.test(text))", "radar keeps held readings in the stale grey instead of negative, fabricated or blank values");
            await Check("vramUsedSub.textContent===num(2,1)+' '+GB && vramUsedSub.classList.contains('sensor-unavailable') && netPingSub.textContent==='124 ms' && netPingSub.classList.contains('sensor-unavailable')", "sub-metrics keep their last text, greyed");
            await Check("cardHistories.find(h=>h.id==='cpu-card').values.slice(-1)[0]===null && cardHistories.find(h=>h.id==='gpu-card').values.slice(-1)[0]===null", "history records the missing sample as a gap");

            await view.CoreWebView2.ExecuteScriptAsync(@"
                layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});
                window.beforeExpiry={values:[...document.querySelectorAll('.gauge-value, .sub-metric .val')].map(v=>v.textContent).join('|'),
                    rings:[...document.querySelectorAll('.ring-fill')].map(r=>r.style.strokeDashoffset).join('|'),
                    nodes:telemetryNodeList.map(n=>n.value).join('|')};
                lastTelemetryReceived=performance.now()-7000;refreshTelemetryFreshness(true);");
            await Check("[...document.querySelectorAll('.gauge-value, .sub-metric .val')].map(v=>v.textContent).join('|')===beforeExpiry.values && [...document.querySelectorAll('.ring-fill')].map(r=>r.style.strokeDashoffset).join('|')===beforeExpiry.rings && telemetryNodeList.map(n=>n.value).join('|')===beforeExpiry.nodes && ![...document.querySelectorAll('.gauge-value')].some(v=>v.textContent==='—')", "expired stream keeps the last values on every card and on the radar");
            await Check("document.querySelectorAll('.sensor-stale').length===7 && [...document.querySelectorAll('.gauge-card .critical-alert')].every(b=>b.textContent===t('alert.dataExpired')) && telemetryNodeList.every(n=>n.stale) && [...document.querySelectorAll('[role=progressbar][aria-valuenow]')].every(g=>g.getAttribute('aria-valuetext')===t('aria.stale',{value:num(Number(g.getAttribute('aria-valuenow')))+' %'}))", "expired values are badged, greyed on the radar and announced as stale");

            // A long pause is not a telemetry outage: resume keeps the readings until the grace window ends.
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});telemetryExpired=false;lastTelemetryReceived=performance.now()-3600000;");
            Control(true);
            await Task.Delay(150);
            Control(false);
            await Task.Delay(200);
            await Check("document.querySelectorAll('.sensor-stale').length===0 && ![...document.querySelectorAll('.gauge-value')].some(v=>v.textContent==='—')", "resume after a long pause does not mark readings expired");
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});");
            await Check("document.querySelectorAll('.sensor-stale').length===0 && [...document.querySelectorAll('.critical-alert')].every(v=>v.textContent==='')", "fresh stream clears expired badges including disk and network");
            await Check("gpuVal.querySelector('span').textContent==='%' && gpuVal.textContent==='73%'", "recovered gauge restores both real value and unit");
            await Check("telemetryNodeList.every(n=>!n.stale) && ![...document.querySelectorAll('[role=progressbar]')].some(g=>g.hasAttribute('aria-valuetext'))", "fresh stream clears the stale radar and accessible values");

            // One 12 Hz clock: the logo rings step every 83 ms from script, with no CSS animation.
            // The layout fixture freezes the page; release it to observe live motion.
            const string spinnerState = "logoSpinners.map(s => s.element.style.transform).join(',')";
            const string countSpinnerSteps = @"new Promise(resolve => { const seen = []; const start = performance.now();
                const frame = () => { const value = " + spinnerState + @"; if (seen[seen.length - 1] !== value) seen.push(value);
                    if (performance.now() - start < 2000) requestAnimationFrame(frame); else resolve(seen.length - 1); };
                requestAnimationFrame(frame); })";
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});setRuntimeSuspended(false);endResumeSnap();");
            string steps = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                "{\"expression\":" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(countSpinnerSteps) + ",\"awaitPromise\":true,\"returnByValue\":true}");
            Console.WriteLine("SPINNER STEPS IN 2 S " + steps);
            int stepCount = int.Parse(System.Text.RegularExpressions.Regex.Match(steps, "\"value\":(\\d+)").Groups[1].Value);
            if (stepCount < 18 || stepCount > 26) throw new Exception("logo rings do not step at 12 Hz (" + stepCount + " steps in 2 s)");
            Console.WriteLine("PASS logo rings step at 12 Hz on the shared clock");
            await Check("document.getAnimations().filter(a => a.playState === 'running' && !(a instanceof CSSTransition)).length === 0", "no CSS animation runs at the display refresh rate");
            await view.CoreWebView2.ExecuteScriptAsync(@"window.ringWrites=0;const writeRing=window.writeRingOffset;
                window.writeRingOffset=function(ring,offset){if(ring===cpuRing)ringWrites++;writeRing(ring,offset);};
                window.ringStart=cpuRing.style.strokeDashoffset;setCircularProgress(cpuRing,20);");
            await Task.Delay(500);
            await Check("Math.abs(parseFloat(cpuRing.style.strokeDashoffset)-RING_CIRCUMFERENCE*0.8)<0.01 && ringWrites>=1 && ringWrites<=4 && ringTweens.size===0", "gauge rings ease to new values in at most 4 steps of the 12 Hz clock");
            await view.CoreWebView2.ExecuteScriptAsync("window.writeRingOffset=writeRing;");

            // Windows energy saver holds the logo rings still while visible, and releases them when it ends.
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal',energySaver:true});setRuntimeSuspended(false);window.spinHeld=" + spinnerState + ";");
            await Task.Delay(600);
            await Check("document.body.classList.contains('energy-saver') && !runtimeSuspended && " + spinnerState + "===spinHeld", "energy saver holds the logo rings still");
            await Check("gpuVal.textContent==='73%' && clockTimer !== 0", "energy saver keeps values and clock live");
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});setRuntimeSuspended(false);window.spinHeld=" + spinnerState + ";");
            await Task.Delay(600);
            await Check("!document.body.classList.contains('energy-saver') && " + spinnerState + "!==spinHeld", "leaving energy saver turns the logo rings again");

            // Windows "Animation effects" off (prefers-reduced-motion): hold decorative motion like energy saver.
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            await Task.Delay(100);
            await view.CoreWebView2.ExecuteScriptAsync("layoutFixture({hardware:'full',plans:4,dpr:1,state:'normal'});setRuntimeSuspended(false);endResumeSnap();window.spinHeld=" + spinnerState + ";");
            await Task.Delay(600);
            await Check("reducedMotionQuery.matches && !runtimeSuspended && " + spinnerState + "===spinHeld", "reduced motion holds the logo rings still");
            await Check("(setCircularProgress(cpuRing,10), Math.abs(parseFloat(cpuRing.style.strokeDashoffset)-RING_CIRCUMFERENCE*0.9)<0.01) && getComputedStyle(igpuVramBar).transitionDuration==='0s'", "reduced motion shows new gauge values at once");
            await Check("gpuVal.textContent==='73%' && clockTimer !== 0", "reduced motion keeps values and clock live");
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[]}");
            await Task.Delay(100);
            await view.CoreWebView2.ExecuteScriptAsync("window.spinHeld=" + spinnerState + ";");
            await Task.Delay(600);
            await Check("!reducedMotionQuery.matches && " + spinnerState + "!==spinHeld", "restoring animation effects turns the logo rings again");

            // High-density displays: the radar backing store follows devicePixelRatio (capped at 2).
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", "{\"width\":1920,\"height\":1080,\"deviceScaleFactor\":1.5,\"mobile\":false}");
            await Task.Delay(400);
            await Check("Math.abs(canvasDensity-Math.max(1,1.5*canvas.getBoundingClientRect().width/width))<0.02 && canvasDensity>1.3 && canvas.width===Math.round(width*canvasDensity) && telemetryNodes.cpu.offscreenCanvas.width===Math.ceil(76*canvasDensity) && bgCanvas && bgCanvas.width===canvas.width", "radar is drawn at 1.5x density on a 150 % display");
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", "{\"width\":1900,\"height\":1060,\"deviceScaleFactor\":3,\"mobile\":false}");
            await Task.Delay(400);
            await Check("Math.abs(canvasDensity-Math.max(1,2*canvas.getBoundingClientRect().width/width))<0.02 && canvas.width===Math.round(width*canvasDensity)", "radar density is capped at 2x");
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
            await Task.Delay(400);
            // Center logo from the host settings: custom image, unreadable image fallback, then the brand logo again.
            const string pixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SETTINGS\",\"logo\":\"" + pixel + "\"}");
            await Task.Delay(300);
            await Check("logoImg.getAttribute('src').startsWith('data:image/png') && logoLoaded", "custom center logo replaces the brand logo");
            view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SETTINGS\",\"logo\":\"data:image/png;base64,AAAA\"}");
            await Task.Delay(300);
            await Check("logoImg.getAttribute('src') === DEFAULT_LOGO && logoLoaded", "unreadable custom logo falls back to the brand logo");
            view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SETTINGS\",\"logo\":\"javascript:alert(1)\"}");
            await Task.Delay(200);
            await Check("logoImg.getAttribute('src') === DEFAULT_LOGO", "only image data URLs are accepted as logo");
            view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SETTINGS\",\"logo\":\"" + pixel + "\"}");
            await Task.Delay(300);
            view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SETTINGS\",\"logo\":null}");
            await Task.Delay(300);
            await Check("logoImg.getAttribute('src') === DEFAULT_LOGO && logoLoaded", "reset restores the brand logo");
            // 60 s history behind each card: drawn under the content, gaps for missing readings, clamped values.
            await Check("document.querySelectorAll('.gauge-card > svg.history-line').length === 7 && [...document.querySelectorAll('.history-line')].every(s => getComputedStyle(s).zIndex === '-1' && getComputedStyle(s).pointerEvents === 'none')", "every card has a history line behind its content");
            await Check("cardHistories.every(h => h.values.length > 0 && h.values.length <= HISTORY_LENGTH)", "history keeps at most 60 samples");
            await Check("historyPaths([10, null, 20, 30]).line.split('M').length - 1 === 2 && historyPaths([null, null]).line === ''", "missing readings break the line instead of drawing zero");
            await Check("historyPaths([150]).line === 'M59 8.0' && historyPaths([-5]).line === 'M59 100.0'", "history values are clamped to the card");
            await Check("cspViolations.length === 0", "page runs under its Content-Security-Policy without blocked content");
            exitCode = 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error); }
        finally { Close(); }
    }
}
