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

    // Each shot is declared by its exact output size (Store accepts up to 3840x2160); the viewport is derived from it
    // and must divide exactly by the scale, so Chromium never rounds a fractional pixel (1707 x 1.5 gave 2561).
    internal static readonly (string Name, int Width, int Height, double Scale)[] Shots = {
        ("nexuswpp-3840x2160", 3840, 2160, 2.0),
        ("nexuswpp-2560x1440", 2560, 1440, 2.0),
        ("nexuswpp-1920x1080", 1920, 1080, 1.0),
        ("nexuswpp-ultrawide-3440x1440", 3440, 1440, 1.25),
        ("nexuswpp-portrait-1080x1920", 1080, 1920, 1.0),
    };

    // CSS viewport of an output size at a scale, refused when it is not a whole number of pixels.
    internal static (int Width, int Height) Viewport(int width, int height, double scale)
    {
        double w = width / scale, h = height / scale;
        if (w != Math.Floor(w) || h != Math.Floor(h))
            throw new ArgumentException($"{width}x{height} at scale {scale} gives a fractional viewport ({w}x{h}).");
        return ((int)w, (int)h);
    }

    // Width and height from a PNG's IHDR chunk.
    internal static (int Width, int Height) PngSize(byte[] png)
    {
        if (png.Length < 24 || png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R')
            throw new InvalidDataException("Not a PNG image.");
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    // Demonstration data of the capture mode: fixed power plans in the screenshot's language, so the Store images
    // never show the names of this PC's own plans (renamed or in the Windows display language).
    internal static JsonArray DemoPowerPlans(string language)
    {
        bool french = language == "fr";
        var plans = new JsonArray();
        void Add(string guid, string name, string kind, bool active) =>
            plans.Add(new JsonObject { ["guid"] = guid, ["name"] = name, ["active"] = active, ["kind"] = kind, ["mode"] = "plan" });
        Add(PowerSchemes.SaverPlan, french ? "Économies d’énergie" : "Power saver", "eco", false);
        Add(PowerSchemes.BalancedPlan, french ? "Utilisation normale" : "Balanced", "balanced", true);
        Add(PowerSchemes.HighPerformancePlan, french ? "Performances élevées" : "High performance", "performance", false);
        return plans;
    }

    // Real telemetry with the power plans replaced by the demonstration ones.
    internal static string WithDemoPowerPlans(string stats, string language)
    {
        JsonObject root = JsonNode.Parse(stats)?.AsObject() ?? throw new InvalidDataException("Telemetry is not a JSON object.");
        root["powerPlans"] = DemoPowerPlans(language);
        return root.ToJsonString();
    }

    // Run by HarnessTests on an STA thread, after the host's startup configuration (TestEntryPoint).
    internal static int Run(params string[] args)
    {
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
            view.CoreWebView2.PostWebMessageAsJson(WithDemoPowerPlans(stats, language));
            await Task.Delay(1000);
        }
    }

    async Task CaptureShot(string name, int width, int height, double scale)
    {
        var (w, h) = Viewport(width, height, scale);
        await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
            JsonSerializer.Serialize(new { width = w, height = h, deviceScaleFactor = scale, mobile = false, screenWidth = w, screenHeight = h }));
        await FeedTelemetry(4);
        var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
        string data = JsonNode.Parse(response)?["data"]?.GetValue<string>() ?? throw new InvalidDataException("Page.captureScreenshot returned no image.");
        byte[] png = Convert.FromBase64String(data);
        var size = PngSize(png);
        if (size != (width, height))
            throw new InvalidDataException($"{name}: captured {size.Width}x{size.Height}, expected {width}x{height}.");
        string path = Path.Combine(output, name + "-" + language + ".png");
        File.WriteAllBytes(path, png);
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
                await CaptureShot(shot.Name, shot.Width, shot.Height, shot.Scale);

            // The settings feature, exactly as the host sends a user's own logo to the page.
            if (customLogoPath != null)
            {
                view.CoreWebView2.PostWebMessageAsJson(UserSettings.BuildSettingsMessage(customLogoPath));
                await CaptureShot("nexuswpp-custom-logo-3840x2160", 3840, 2160, 2.0);
            }
            result = 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally { Close(); }
    }
}
