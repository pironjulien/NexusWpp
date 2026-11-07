using System;
using System.Drawing;
using DesktopHtmlHost;

namespace NexusWpp.Tests;

// Monitor coverage behind the pause: negative origins, overlaps, gaps.
public sealed class DesktopCoverageTests
{
    static readonly Rectangle Main = new Rectangle(0, 0, 1920, 1040);
    static readonly Rectangle Left = new Rectangle(-1280, -200, 1280, 1024);

    [Fact]
    public void TiledWindowsCoverANegativeCoordinateMonitor()
    {
        var coverage = new DesktopCoverage(new[] { Main, Left });
        Assert.False(coverage.IsCovered, "visible desktop");
        coverage.Cover(Main);
        Assert.False(coverage.IsCovered, "second monitor remains visible");
        coverage.Cover(new Rectangle(-1280, -200, 640, 1024));
        coverage.Cover(new Rectangle(-640, -200, 640, 1024));
        Assert.True(coverage.IsCovered, "tiled windows cover negative-coordinate monitor");
    }

    [Fact]
    public void OverlapDoesNotConcealAnUncoveredStrip()
    {
        var coverage = new DesktopCoverage(new[] { Main });
        coverage.Cover(new Rectangle(0, 0, 1200, 1040));
        coverage.Cover(new Rectangle(800, 0, 1000, 1040));
        Assert.False(coverage.IsCovered, "overlap does not conceal uncovered strip");
        coverage.Cover(new Rectangle(1800, 0, 120, 1040));
        Assert.True(coverage.IsCovered, "last visible strip covered");
    }

    [Fact]
    public void OnePixelGapStaysVisible()
    {
        var coverage = new DesktopCoverage(new[] { Main });
        coverage.Cover(new Rectangle(0, 0, 1920, 519));
        coverage.Cover(new Rectangle(0, 520, 1920, 520));
        Assert.False(coverage.IsCovered, "one pixel gap stays visible");
        coverage.Cover(new Rectangle(0, 519, 1920, 1));
        Assert.True(coverage.IsCovered, "gap filled");
    }

    [Fact]
    public void OffscreenEmptyAndOversizedWindows()
    {
        var coverage = new DesktopCoverage(new[] { Main });
        coverage.Cover(new Rectangle(2000, 0, 500, 500));
        coverage.Cover(Rectangle.Empty);
        Assert.False(coverage.IsCovered, "offscreen and empty windows ignored");
        coverage.Cover(new Rectangle(-100, -100, 2200, 1300));
        Assert.True(coverage.IsCovered, "oversized maximized frame");
    }

    [Fact]
    public void MissingMonitorInformationIsNotOcclusion()
    {
        Assert.False(new DesktopCoverage(Array.Empty<Rectangle>()).IsCovered, "missing monitor information is not occlusion");
    }
}
