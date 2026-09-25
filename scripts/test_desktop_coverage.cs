using System;
using System.Drawing;
using DesktopHtmlHost;

class CoverageTests
{
    static void Expect(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        Rectangle main = new Rectangle(0, 0, 1920, 1040);
        Rectangle left = new Rectangle(-1280, -200, 1280, 1024);
        var coverage = new DesktopCoverage(new[] { main, left });
        Expect(!coverage.IsCovered, "visible desktop");
        coverage.Cover(main);
        Expect(!coverage.IsCovered, "second monitor remains visible");
        coverage.Cover(new Rectangle(-1280, -200, 640, 1024));
        coverage.Cover(new Rectangle(-640, -200, 640, 1024));
        Expect(coverage.IsCovered, "tiled windows cover negative-coordinate monitor");

        coverage = new DesktopCoverage(new[] { main });
        coverage.Cover(new Rectangle(0, 0, 1200, 1040));
        coverage.Cover(new Rectangle(800, 0, 1000, 1040));
        Expect(!coverage.IsCovered, "overlap does not conceal uncovered strip");
        coverage.Cover(new Rectangle(1800, 0, 120, 1040));
        Expect(coverage.IsCovered, "last visible strip covered");

        coverage = new DesktopCoverage(new[] { main });
        coverage.Cover(new Rectangle(0, 0, 1920, 519));
        coverage.Cover(new Rectangle(0, 520, 1920, 520));
        Expect(!coverage.IsCovered, "one pixel gap stays visible");
        coverage.Cover(new Rectangle(0, 519, 1920, 1));
        Expect(coverage.IsCovered, "gap filled");

        coverage = new DesktopCoverage(new[] { main });
        coverage.Cover(new Rectangle(2000, 0, 500, 500));
        coverage.Cover(Rectangle.Empty);
        Expect(!coverage.IsCovered, "offscreen and empty windows ignored");
        coverage.Cover(new Rectangle(-100, -100, 2200, 1300));
        Expect(coverage.IsCovered, "oversized maximized frame");
        Expect(!new DesktopCoverage(new Rectangle[0]).IsCovered, "missing monitor information is not occlusion");
    }
}
