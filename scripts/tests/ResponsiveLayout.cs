using System;
using System.Collections.Generic;
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
class ResponsiveLayout : Form
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

    async Task Script(string code)
    {
        var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = code, awaitPromise = true, returnByValue = true }));
        if (response.Contains("\"exceptionDetails\"")) throw new Exception(response);
    }

    async void Run(object sender, EventArgs args)
    {
        try
        {
            Directory.CreateDirectory(output);
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(project, "work", "responsive-test-profile"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("nexuswpp.local", project, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived += delegate(object source, CoreWebView2WebMessageReceivedEventArgs message) {
                if (message.TryGetWebMessageAsString() == "REQUEST_RUNTIME_STATE") {
                    view.CoreWebView2.PostWebMessageAsJson("{\"control\":\"SUSPEND\"}");
                    ready = true;
                }
            };
            view.Source = new Uri("http://nexuswpp.local/index.html?lang=" + language);
            for (int i = 0; i < 100 && !ready; i++) await Task.Delay(100);
            if (!ready) throw new Exception("Page did not initialize");
            await Script(File.ReadAllText(Path.Combine(project, "scripts", "tests", "responsive_layout.js")));
            var cases = JsonNode.Parse(File.ReadAllText(casesPath)).AsArray();
            var results = new JsonArray();
            int failures = 0;
            foreach (JsonObject item in cases)
            {
                int w = (int)item["width"], h = (int)item["height"];
                double dpr = (double)item["dpr"];
                string id = (string)item["id"];
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    JsonSerializer.Serialize(new { width = w, height = h, deviceScaleFactor = dpr, mobile = false,
                        screenWidth = w, screenHeight = h }));
                await Script("layoutFixture(" + item.ToJsonString() + ")");
                await Script("new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                var raw = await view.CoreWebView2.ExecuteScriptAsync("inspectLayout()");
                var measurement = JsonNode.Parse(raw).AsObject();
                measurement["case"] = item.DeepClone();
                results.Add(measurement);
                var issues = measurement["issues"].AsArray();
                if (issues.Count > 0) failures++;
                if (item.ContainsKey("capture") && (bool)item["capture"])
                {
                    var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
                    var screenshot = JsonNode.Parse(response);
                    File.WriteAllBytes(Path.Combine(output, id + ".png"), Convert.FromBase64String((string)screenshot["data"]));
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
