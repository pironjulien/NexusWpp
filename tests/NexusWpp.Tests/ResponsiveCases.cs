using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace NexusWpp.Tests;

// Screen formats checked by the responsive layout harness (Harnesses\ResponsiveLayout.cs, scripts\tests\responsive_layout.js):
// desktop resolutions at every Windows scale, plan rows, breakpoints, hardware variants, disks, battery and multi-monitor
// desktops, each in the normal, high and stale telemetry states. Quick keeps a representative subset.
static class ResponsiveCases
{
    static int Floor(double value) => (int)Math.Floor(value);
    static int Percent(double scale) => (int)Math.Round(scale * 100);

    static JsonObject Case(string id, int width, int height, double dpr, int plans, string hardware, bool capture) =>
        new() { ["id"] = id, ["width"] = width, ["height"] = height, ["dpr"] = dpr, ["plans"] = plans, ["hardware"] = hardware, ["capture"] = capture };

    internal static JsonArray Build(bool quick)
    {
        var cases = new List<JsonObject>();
        (int W, int H)[] resolutions = quick
            ? [(1280, 720), (1920, 1080), (2560, 1440), (3840, 2160), (3440, 1440), (1080, 1920)]
            : [(1280, 720), (1280, 800), (1366, 768), (1440, 900), (1600, 900), (1920, 1080), (1920, 1200),
               (1920, 1280), (2256, 1504), (2560, 1080), (2560, 1440), (2560, 1600), (2880, 1800),
               (3000, 2000), (3200, 1800), (3440, 1440), (3840, 1600), (3840, 2160), (3840, 2400),
               (5120, 1440), (5120, 2160), (5120, 2880), (6016, 3384), (7680, 2160), (7680, 4320),
               (1080, 1920), (1200, 1920), (1440, 2560), (2160, 3840)];
        double[] scales = quick ? [1, 1.5, 2, 3] : [1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3, 3.5, 4];
        string[] captured = ["1280x720-100pct", "2560x1440-125pct", "1080x1920-200pct", "3440x1440-100pct", "1920x1080-125pct"];
        foreach (var (rw, rh) in resolutions)
            foreach (double scale in scales)
            {
                int w = Floor(rw / scale), h = Floor(rh / scale);
                if ((w >= 800 && h >= 450) || (w >= 432 && h >= 768))
                {
                    string id = $"{rw}x{rh}-{Percent(scale)}pct";
                    bool capture = (rw == 3840 && rh == 2160 && new[] { 1.5, 2, 2.25, 2.5, 3, 4 }.Contains(scale)) || captured.Contains(id);
                    cases.Add(Case(id, w, h, scale, 4, "full", capture));
                }
            }
        // Five plans (a second row of buttons) on real desktop sizes, with long French names.
        foreach (var (sw, sh) in new[] { (1280, 720), (1920, 1080), (2560, 1440), (5120, 1440) })
            foreach (double scale in new[] { 1, 1.25 })
                cases.Add(Case($"plans5-{sw}x{sh}-{Percent(scale)}pct", Floor(sw / scale), Floor(sh / scale), scale, 5, "full", false));
        // Both sides of historical breakpoints, hardware removal, and plan reflow.
        foreach (var (sw, sh) in new[] { (1000, 900), (1001, 901), (1280, 900), (1280, 901), (1920, 1200), (1920, 1201), (2500, 1400), (2501, 1400), (800, 450), (432, 768) })
            foreach (int plans in new[] { 1, 3, 4 })
                cases.Add(Case($"boundary-{sw}x{sh}-{plans}", sw, sh, 1, plans, "minimal", false));
        foreach (var (sw, sh) in new[] { (800, 450), (960, 540), (1280, 720), (1706, 960), (432, 768), (540, 960), (1000, 900), (3840, 2160) })
            foreach (string hardware in new[] { "full", "dual-gpu", "integrated", "npu-only", "cpu-only" })
                foreach (int plans in new[] { 1, 2, 3, 4 })
                    cases.Add(Case($"hardware-{sw}x{sh}-{hardware}-{plans}", sw, sh, 1.25, plans, hardware, plans == 4 && hardware == "full" && (sw == 800 || sw == 432)));
        // Two, three and four fixed disks: one card each, in the right column (landscape) or in the flow (portrait).
        foreach (var (sw, sh) in new[] { (800, 450), (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160), (432, 768), (1080, 1920) })
            foreach (int disks in new[] { 2, 3, 4 })
            {
                var item = Case($"disks{disks}-{sw}x{sh}", sw, sh, 1.25, 4, "full", disks == 4 && sw == 1920);
                item["disks"] = disks;
                cases.Add(item);
            }
        // Laptop on battery: the longest battery line (percent, source, power, time left) and an NPU client name.
        foreach (var (sw, sh) in new[] { (800, 450), (432, 768), (1280, 720), (1920, 1080), (3840, 2160) })
            foreach (double dpr in new[] { 1, 1.5, 2 })
            {
                var item = Case($"battery-{sw}x{sh}-{Percent(dpr)}pct", sw, sh, dpr, 3, "full", sw == 1920 && dpr == 1);
                item["battery"] = new JsonObject { ["present"] = true, ["percent"] = 73, ["ac"] = false, ["rateW"] = -12.4, ["minutesLeft"] = 135 };
                cases.Add(item);
            }
        // Multi-monitor desktops: the window spans every monitor, the cockpit stays on the primary one. Window size, then
        // primary monitor rectangle relative to the window (negative virtual origins put the primary away from the
        // window's top-left corner).
        var topologies = new (string Id, int W, int H, int[] P)[]
        {
            ("side-by-side-primary-right", 3840, 1080, [1920, 0, 1920, 1080]),
            ("side-by-side-primary-left", 3840, 1080, [0, 0, 1920, 1080]),
            ("stacked-primary-below", 1920, 2160, [0, 1080, 1920, 1080]),
            ("l-shape-portrait-left", 3000, 1920, [1080, 420, 1920, 1080]),
            ("mixed-sizes-primary-right", 3584, 1440, [1024, 0, 2560, 1440]),
            ("three-monitors-middle", 5760, 1080, [1920, 0, 1920, 1080]),
            ("portrait-primary", 3000, 1920, [0, 0, 1080, 1920]),
        };
        foreach (var topology in topologies)
            foreach (double dpr in new[] { 1, 1.25 })
            {
                int w = Floor(topology.W / dpr), h = Floor(topology.H / dpr);
                var item = Case($"monitors-{topology.Id}-{Percent(dpr)}pct", w, h, dpr, 4, "full", dpr == 1);
                item["layout"] = new JsonObject
                {
                    ["control"] = "LAYOUT",
                    ["window"] = new JsonObject { ["w"] = w, ["h"] = h },
                    ["primary"] = new JsonObject { ["x"] = topology.P[0] / dpr, ["y"] = topology.P[1] / dpr, ["w"] = topology.P[2] / dpr, ["h"] = topology.P[3] / dpr },
                };
                cases.Add(item);
            }
        var expanded = new JsonArray();
        foreach (JsonObject item in cases)
            foreach (string state in new[] { "normal", "high", "stale" })
            {
                var variant = item.DeepClone().AsObject();
                variant["state"] = state;
                variant["id"] = (string)item["id"]! + "-" + state;
                expanded.Add(variant);
            }
        return expanded;
    }
}
