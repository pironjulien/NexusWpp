using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Real Evergreen WebView2, with Chromium viewport/DPR emulation. No display
// configuration, installed wallpaper, power plan or user profile is modified.
sealed class ResponsiveLayout : Form
{
    readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
    readonly string project, output, casesPath, language;
    bool ready;
    int result = 1;

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        int exitCode = 1;
        using (var form = new ResponsiveLayout(args))
        {
            Application.Run(form);
            exitCode = form.result;
        }
        Environment.Exit(exitCode);
        return exitCode;
    }

    ResponsiveLayout(string[] args)
    {
        project = args[0]; output = args[1]; casesPath = args[2]; language = args.Length > 3 ? args[3] : "fr";
        Text = "NexusWpp — validation des formats d'écran";
        ClientSize = new Size(960, 640);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
        Controls.Add(view);
        Shown += Run;
    }

    // Off-screen and never activated: the run must not take the keyboard focus or the
    // foreground from whoever is using the PC.
    const int WS_EX_NOACTIVATE = 0x08000000;
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    // A missing node in the cases file or in a DevTools answer is a broken run, not a passed case.
    static JsonNode Node(JsonNode? node, string name) { return node ?? throw new InvalidDataException("Missing JSON value: " + name); }
    static T Field<T>(JsonNode node, string name) { return Node(node[name], name).GetValue<T>(); }

    async Task Script(string code)
    {
        var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = code, awaitPromise = true, returnByValue = true }));
        if (response.Contains("\"exceptionDetails\"")) throw new InvalidOperationException(response);
    }

    async void Run(object? sender, EventArgs args)
    {
        try
        {
            Directory.CreateDirectory(output);
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(project, "work", "responsive-test-profile"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("nexuswpp.local", project, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived += delegate(object? source, CoreWebView2WebMessageReceivedEventArgs message) {
                if (message.TryGetWebMessageAsString() == "REQUEST_RUNTIME_STATE") {
                    view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SUSPEND\"}");
                    ready = true;
                }
            };
            view.Source = new Uri("http://nexuswpp.local/index.html?lang=" + language);
            for (int i = 0; i < 100 && !ready; i++) await Task.Delay(100);
            if (!ready) throw new InvalidOperationException("Page did not initialize");
            await Script(File.ReadAllText(Path.Combine(project, "scripts", "tests", "responsive_layout.js")));
            var cases = Node(JsonNode.Parse(File.ReadAllText(casesPath)), "cases").AsArray();
            var results = new JsonArray();
            int failures = 0;
            foreach (JsonNode? entry in cases)
            {
                JsonObject item = Node(entry, "case").AsObject();
                int w = Field<int>(item, "width"), h = Field<int>(item, "height");
                double dpr = Field<double>(item, "dpr");
                string id = Field<string>(item, "id");
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    JsonSerializer.Serialize(new { width = w, height = h, deviceScaleFactor = dpr, mobile = false,
                        screenWidth = w, screenHeight = h }));
                await Script("layoutFixture(" + item.ToJsonString() + ")");
                await Script("new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                var raw = await view.CoreWebView2.ExecuteScriptAsync("inspectLayout()");
                var measurement = Node(JsonNode.Parse(raw), "inspectLayout()").AsObject();
                measurement["case"] = item.DeepClone();
                results.Add(measurement);
                var issues = Node(measurement["issues"], "issues").AsArray();
                if (issues.Count > 0) failures++;
                if (item.ContainsKey("capture") && Field<bool>(item, "capture"))
                {
                    var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
                    var screenshot = Node(JsonNode.Parse(response), "Page.captureScreenshot");
                    File.WriteAllBytes(Path.Combine(output, id + ".png"), Convert.FromBase64String(Field<string>(screenshot, "data")));
                }
                Console.WriteLine((issues.Count == 0 ? "PASS " : "FAIL ") + id + " (" + issues.Count + " issues)");
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new {
                    runtime = environment.BrowserVersionString, tested = results.Count, failures = failures, results = results }));
            }
            Console.WriteLine("RESULT " + results.Count + " cases, " + failures + " failures. " + output);
            result = failures == 0 ? 0 : 2;
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally
        {
            view.Dispose();
            Close();
        }
    }
}
