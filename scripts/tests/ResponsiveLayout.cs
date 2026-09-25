using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Real Evergreen WebView2, with Chromium viewport/DPR emulation. No display
// configuration, installed wallpaper, power plan or user profile is modified.
class ResponsiveLayout : Form
{
    readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    readonly string project, output, casesPath;
    bool ready;
    int result = 1;

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (var form = new ResponsiveLayout(args)) { Application.Run(form); return form.result; }
    }

    ResponsiveLayout(string[] args)
    {
        project = args[0]; output = args[1]; casesPath = args[2];
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
            json.Serialize(new { expression = code, awaitPromise = true, returnByValue = true }));
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
            view.Source = new Uri("http://nexuswpp.local/index.html");
            for (int i = 0; i < 100 && !ready; i++) await Task.Delay(100);
            if (!ready) throw new Exception("Page did not initialize");
            await Script(File.ReadAllText(Path.Combine(project, "scripts", "tests", "responsive_layout.js")));
            var cases = json.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(casesPath));
            var results = new List<object>();
            int failures = 0;
            foreach (var item in cases)
            {
                int w = Convert.ToInt32(item["width"]), h = Convert.ToInt32(item["height"]);
                double dpr = Convert.ToDouble(item["dpr"]);
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    json.Serialize(new { width = w, height = h, deviceScaleFactor = dpr, mobile = false,
                        screenWidth = w, screenHeight = h }));
                await Script("layoutFixture(" + json.Serialize(item) + ")");
                await Script("new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                var raw = await view.CoreWebView2.ExecuteScriptAsync("inspectLayout()");
                var measurement = json.Deserialize<Dictionary<string, object>>(raw);
                measurement["case"] = item;
                results.Add(measurement);
                var issues = (System.Collections.ArrayList)measurement["issues"];
                if (issues.Count > 0) failures++;
                if (item.ContainsKey("capture") && Convert.ToBoolean(item["capture"]))
                {
                    var response = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
                    var screenshot = json.Deserialize<Dictionary<string, object>>(response);
                    File.WriteAllBytes(Path.Combine(output, item["id"] + ".png"), Convert.FromBase64String((string)screenshot["data"]));
                }
                Console.WriteLine((issues.Count == 0 ? "PASS " : "FAIL ") + item["id"] + " (" + issues.Count + " issues)");
                File.WriteAllText(Path.Combine(output, "results.json"), json.Serialize(new {
                    runtime = environment.BrowserVersionString, tested = results.Count, failures = failures, results = results }));
            }
            Console.WriteLine("RESULT " + results.Count + " cases, " + failures + " failures. " + output);
            result = failures == 0 ? 0 : 2;
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally { Close(); }
    }
}
