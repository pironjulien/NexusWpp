using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace NexusWpp.Tests;

// The host's WinForms and WebView2 harnesses (Harnesses\), each one a test. They create only windows of this process,
// off screen and never activated, and keep their images and logs under work\. The ones that need the real desktop
// (a window on screen, the user's windows minimized) are explicit: `dotnet test` runs them only when asked
// (scripts\store_screenshots.ps1, scripts\test_desktop_visual_resume.ps1, or --explicit only with a filter).
[Collection(WindowHarnesses.Name)]
public sealed class HarnessTests
{
    // Window placement below WorkerW, recovery rules, shell events, the host message loop and one live telemetry read.
    [Fact]
    public void Host() => Assert.Equal(0, Sta.Run(() => HostRegression.Run(Repository.Output("feature-tests"))));

    // Custom logo import and the settings window (isolated output folder; the user's settings are untouched).
    [Fact]
    public void Settings() => Assert.Equal(0, Sta.Run(() => SettingsRegression.Run(Repository.Output("settings-tests"), Repository.Root)));

    // The shipped page in a real WebView2: pause, resume, scene kept, Content-Security-Policy.
    [Fact]
    public void RuntimePause() => Assert.Equal(0, Sta.Run(() => RuntimePauseRegression.Run(Repository.Root), dpiUnaware: true));

    // Click forwarding and the mouse hook life cycle, on hidden windows of this process and without injecting input.
    [Fact]
    public void MouseHook() => Assert.Equal(0, Sta.Run(() => MouseHookRegression.Run()));

    // Screen formats, Windows scales, portrait and hardware variants, in both interface languages.
    [Fact]
    public void ResponsiveLayoutQuick() => ResponsiveLayoutRun("quick", quick: true);

    // Every resolution and scale (several minutes).
    [Fact(Explicit = true), Trait("Category", "Exhaustive")]
    public void ResponsiveLayoutFull() => ResponsiveLayoutRun("full", quick: false);

    static void ResponsiveLayoutRun(string label, bool quick)
    {
        string output = Repository.Output("responsive", label);
        string cases = Path.Combine(output, "cases.json");
        File.WriteAllText(cases, ResponsiveCases.Build(quick).ToJsonString());
        // English labels have other lengths than French ones.
        foreach (string language in new[] { "fr", "en" })
        {
            string languageOutput = Repository.Output("responsive", label, language);
            int result = Sta.Run(() => ResponsiveLayout.Run(Repository.Root, languageOutput, cases, language), dpiUnaware: true);
            Assert.True(result == 0, $"Responsive layout failures ({language}, code {result}): see {Path.Combine(languageOutput, "results.json")}");
        }
    }

    // Microsoft Store screenshots from the real page with this PC's real telemetry, then the real settings window, shown
    // on screen (on top, never activated) for its capture. NEXUSWPP_STORE_WARMUP (seconds of telemetry before the first
    // shot, 70 by default) and NEXUSWPP_STORE_OUTPUT (dist\store-screenshots by default) come from store_screenshots.ps1.
    [Fact(Explicit = true), Trait("Category", "Desktop")]
    public void StoreScreenshotCapture()
    {
        string warmup = Environment.GetEnvironmentVariable("NEXUSWPP_STORE_WARMUP") is { Length: > 0 } seconds ? seconds : "70";
        string output = Environment.GetEnvironmentVariable("NEXUSWPP_STORE_OUTPUT") is { Length: > 0 } folder ? folder : Path.Combine(Repository.Root, "dist", "store-screenshots");
        Directory.CreateDirectory(output);
        foreach (string old in Directory.GetFiles(output, "*.png"))
            FileSystem.DeleteFile(old, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        string samples = Repository.Output("store-screenshots");
        foreach (string language in new[] { "fr", "en" })
        {
            // Neutral example image for the custom-logo screenshot (clearly not the brand logo).
            string sample = Path.Combine(samples, $"sample-logo-{language}.png");
            SampleLogo(sample, language == "fr" ? "MON\nLOGO" : "MY\nLOGO");
            Assert.True(Sta.Run(() => StoreScreenshots.Run(Repository.Root, output, language, warmup, sample), dpiUnaware: true) == 0, $"Store screenshot capture failed ({language}).");
            Sta.Run(() =>
            {
                StoreSettingsShot.Run(Path.Combine(Repository.Root, "julienpiron.png"), Path.Combine(output, $"nexuswpp-3840x2160-{language}.png"),
                    Path.Combine(output, $"nexuswpp-settings-3840x2160-{language}.png"), language);
                return 0;
            });
        }
        foreach (string shot in Directory.GetFiles(output, "*.png").Order())
            Console.WriteLine($"{Path.GetFileName(shot)} {new FileInfo(shot).Length}");
    }

    static void SampleLogo(string path, string text)
    {
        using var bitmap = new Bitmap(512, 512);
        using (var g = Graphics.FromImage(bitmap))
        using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(512, 512), Color.FromArgb(255, 140, 30), Color.FromArgb(210, 40, 120)))
        using (var font = new Font("Bahnschrift", 88, FontStyle.Bold))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.FillRectangle(brush, 0, 0, 512, 512);
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, 512, 512), format);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    // Physical return of the desktop after it was covered, measured on the screen with the running NexusWpp: the user's
    // windows are minimized (never activated) and restored. Arguments from scripts\test_desktop_visual_resume.ps1, which
    // checks the running package first (NEXUSWPP_VISUAL_RESUME_ARGS, a JSON array of option/value pairs).
    [Fact(Explicit = true), Trait("Category", "Desktop")]
    public void DesktopVisualResumeMeasure()
    {
        string json = Environment.GetEnvironmentVariable("NEXUSWPP_VISUAL_RESUME_ARGS")
            ?? throw new InvalidOperationException("Run scripts\\test_desktop_visual_resume.ps1: it checks the running NexusWpp and passes the options.");
        string[] arguments = JsonSerializer.Deserialize<string[]>(json) ?? [];
        Assert.Equal(0, Sta.Run(() => DesktopVisualResume.Run(arguments)));
    }
}

// The test assembly starts like the host: same DPI mode, set by the same generated startup configuration.
public sealed class HostSourceTests
{
    static string HighDpiMode(string project) =>
        XDocument.Load(Path.Combine(Repository.Root, project)).Descendants("ApplicationHighDpiMode").Single().Value;

    [Fact]
    public void TestsStartWithTheHostDpiMode()
    {
        Assert.Equal(HighDpiMode("NexusWpp.csproj"), HighDpiMode(Path.Combine("tests", "NexusWpp.Tests", "NexusWpp.Tests.csproj")));
        Assert.Contains("ApplicationConfiguration.Initialize();", File.ReadAllText(Path.Combine(Repository.Root, "Program.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void QuickResponsiveCasesKeepTheirCount()
    {
        JsonArray quick = ResponsiveCases.Build(quick: true), full = ResponsiveCases.Build(quick: false);
        Assert.Equal(804, quick.Count);
        Assert.True(full.Count > quick.Count);
        Assert.Equal(quick.Count, quick.Select(c => (string)c!["id"]!).Distinct().Count());
    }
}
