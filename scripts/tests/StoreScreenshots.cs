using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DesktopHtmlHost;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Store screenshots from the real page fed with this PC's real telemetry
// (same collector as the host). Viewport emulation only; nothing on the desktop changes.
class StoreScreenshots : Form
{
    readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    readonly TelemetryCollector collector = new TelemetryCollector();
    readonly string project, output;
    bool ready;
    int result = 1;

    // Output pixels = viewport x scale (Store accepts up to 3840x2160).
    static readonly object[][] Shots = {
        new object[] { "nexuswpp-3840x2160", 1920, 1080, 2.0 },
        new object[] { "nexuswpp-2560x1440", 1707, 960, 1.5 },
        new object[] { "nexuswpp-1920x1080", 1920, 1080, 1.0 },
    };

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (var form = new StoreScreenshots(args)) { Application.Run(form); return form.result; }
    }

    StoreScreenshots(string[] args)
    {
        project = args[0]; output = args[1];
        ClientSize = new Size(960, 540);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
        Controls.Add(view);
        Shown += Run;
    }

    async Task FeedTelemetry(int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            string stats = await Task.Run(() => collector.CollectStats());
            view.CoreWebView2.PostWebMessageAsJson(stats);
            await Task.Delay(1000);
        }
    }

    async void Run(object sender, EventArgs args)
    {
        try
        {
            Directory.CreateDirectory(output);
            await Task.Run(() => collector.Initialize());
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(project, "work", "store-screenshot-profile"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("nexuswpp.local", project, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived += delegate(object source, CoreWebView2WebMessageReceivedEventArgs message) {
                if (message.TryGetWebMessageAsString() == "REQUEST_RUNTIME_STATE") {
                    view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"RESUME\"}");
                    ready = true;
                }
            };
            view.Source = new Uri("http://nexuswpp.local/index.html");
            for (int i = 0; i < 100 && !ready; i++) await Task.Delay(100);
            if (!ready) throw new Exception("Page did not initialize");
            await FeedTelemetry(4); // counters need a few samples for real deltas

            foreach (var shot in Shots)
            {
                int w = (int)shot[1], h = (int)shot[2];
                double scale = (double)shot[3];
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    json.Serialize(new { width = w, height = h, deviceScaleFactor = scale, mobile = false, screenWidth = w, screenHeight = h }));
                await FeedTelemetry(4);
                var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
                var screenshot = json.Deserialize<Dictionary<string, object>>(response);
                string path = Path.Combine(output, shot[0] + ".png");
                File.WriteAllBytes(path, Convert.FromBase64String((string)screenshot["data"]));
                Console.WriteLine("SAVED " + path);
            }
            result = 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally { Close(); }
    }
}
