using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using DesktopHtmlHost;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Store screenshots from the real page fed with this PC's real telemetry
// (same collector as the host). Viewport emulation only; nothing on the desktop changes.
sealed class StoreScreenshots : Form
{
    readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
    readonly TelemetryCollector collector = new TelemetryCollector();
    readonly string project, output, language;
    readonly int warmupSeconds;
    readonly string? customLogoPath;
    bool ready;
    int result = 1;

    // Output pixels = viewport x scale (Store accepts up to 3840x2160).
    static readonly object[][] Shots = {
        new object[] { "nexuswpp-3840x2160", 1920, 1080, 2.0 },
        new object[] { "nexuswpp-2560x1440", 1707, 960, 1.5 },
        new object[] { "nexuswpp-1920x1080", 1920, 1080, 1.0 },
        new object[] { "nexuswpp-ultrawide-3440x1440", 2752, 1152, 1.25 },
        new object[] { "nexuswpp-portrait-1080x1920", 1080, 1920, 1.0 },
    };

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (var form = new StoreScreenshots(args)) { Application.Run(form); return form.result; }
    }

    StoreScreenshots(string[] args)
    {
        project = args[0]; output = args[1]; language = args.Length > 2 ? args[2] : "fr";
        warmupSeconds = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 4;
        customLogoPath = args.Length > 4 ? args[4] : null;
        ClientSize = new Size(960, 540);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
        Controls.Add(view);
        Shown += Run;
    }

    // Off-screen and never activated: the run must not take the keyboard focus or the
    // foreground from whoever is using the PC.
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) collector.Dispose();
        base.Dispose(disposing);
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

    async Task CaptureShot(string name, int w, int h, double scale)
    {
        await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
            JsonSerializer.Serialize(new { width = w, height = h, deviceScaleFactor = scale, mobile = false, screenWidth = w, screenHeight = h }));
        await FeedTelemetry(4);
        var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
        string data = JsonNode.Parse(response)?["data"]?.GetValue<string>() ?? throw new InvalidDataException("Page.captureScreenshot returned no image.");
        string path = Path.Combine(output, name + "-" + language + ".png");
        File.WriteAllBytes(path, Convert.FromBase64String(data));
        Console.WriteLine("SAVED " + path);
    }

    async void Run(object? sender, EventArgs args)
    {
        try
        {
            Directory.CreateDirectory(output);
            await Task.Run(() => collector.Initialize());
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(project, "work", "store-screenshot-profile"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("nexuswpp.local", project, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived += delegate(object? source, CoreWebView2WebMessageReceivedEventArgs message) {
                if (message.TryGetWebMessageAsString() == "REQUEST_RUNTIME_STATE") {
                    view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"RESUME\"}");
                    ready = true;
                }
            };
            view.Source = new Uri("http://nexuswpp.local/index.html?lang=" + language);
            for (int i = 0; i < 100 && !ready; i++) await Task.Delay(100);
            if (!ready) throw new InvalidOperationException("Page did not initialize");
            await FeedTelemetry(warmupSeconds); // real deltas, and history behind the cards

            foreach (var shot in Shots)
                await CaptureShot((string)shot[0], (int)shot[1], (int)shot[2], (double)shot[3]);

            // The settings feature, exactly as the host sends a user's own logo to the page.
            if (customLogoPath != null)
            {
                view.CoreWebView2.PostWebMessageAsJson(UserSettings.BuildSettingsMessage(customLogoPath));
                await CaptureShot("nexuswpp-custom-logo-3840x2160", 1920, 1080, 2.0);
            }
            result = 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally { Close(); }
    }
}
