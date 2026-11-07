using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace NexusWpp.Tests;

// Rules of the Store screenshot capture that hold without rendering anything.
public sealed class StoreScreenshotRulesTests
{
    static readonly string[] Kinds = { "eco", "balanced", "performance" };

    [Fact]
    public void EveryShotHasAWholePixelViewport()
    {
        foreach (var shot in StoreScreenshots.Shots)
        {
            var (w, h) = StoreScreenshots.Viewport(shot.Width, shot.Height, shot.Scale);
            Assert.Equal(shot.Width, (int)(w * shot.Scale));
            Assert.Equal(shot.Height, (int)(h * shot.Scale));
            Assert.EndsWith($"{shot.Width}x{shot.Height}", shot.Name);
        }
        Assert.Throws<ArgumentException>(() => StoreScreenshots.Viewport(2560, 1440, 1.5));
    }

    [Fact]
    public void PngSizeReadsTheHeader()
    {
        using var bitmap = new System.Drawing.Bitmap(37, 11);
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        Assert.Equal((37, 11), StoreScreenshots.PngSize(stream.ToArray()));
    }

    [Theory]
    [InlineData("en", "Power saver", "Balanced", "High performance")]
    [InlineData("fr", "Économies d’énergie", "Utilisation normale", "Performances élevées")]
    public void DemoPowerPlansReplaceThePcPlans(string language, string eco, string balanced, string performance)
    {
        string stats = "{\"cpu\":{\"load\":12},\"powerPlans\":[{\"guid\":\"x\",\"name\":\"MODE DEV\",\"active\":true,\"kind\":\"balanced\",\"mode\":\"plan\"}]}";
        JsonObject result = JsonNode.Parse(StoreScreenshots.WithDemoPowerPlans(stats, language))!.AsObject();
        var plans = result["powerPlans"]!.AsArray().Select(p => p!.AsObject()).ToArray();
        Assert.Equal(new[] { eco, balanced, performance }, plans.Select(p => (string)p["name"]!));
        Assert.Equal(Kinds, plans.Select(p => (string)p["kind"]!));
        Assert.Single(plans, p => (bool)p["active"]!);
        Assert.Equal(12, (int)result["cpu"]!["load"]!);
    }
}
