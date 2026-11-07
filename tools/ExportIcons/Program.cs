// Renders the NexusWpp icons from branding/*.svg: icon.ico, the PNGs build_msix.ps1 packs (branding/png) and the
// Partner Center listing logos (dist/store-logos, uploaded by hand: dist/ is not versioned).
//
// The SVGs are the HD ones of the Nexus icon kit (NexusHarness/NexusCommunication/icones/svg/hd, copied here by its
// sync_icons.py): above 32 px the pictogram in the julienpiron.fr medallion on a transparent background
// (nexuswpp-mark), at 32 px and less the same medallion with only its unreadable "JULIENPIRON.FR" lettering erased
// (nexuswpp-round). The pictogram alone is never shown.
// Run again after replacing the SVGs:  dotnet run --project tools\ExportIcons [-- <output root>]
// The optional output root (default: the repository) lets a check render elsewhere and compare with the versioned files.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SkiaSharp;
using Svg.Skia;

const int MarkMin = 33;
int[] icoSizes = [16, 20, 24, 32, 48, 64, 128, 256];
// Every pixel size build_msix.ps1 asks for: Square44x44Logo targetsizes, then the 100/125/150/200/400 scales of
// StoreLogo (50), Square44x44Logo (44) and Square150x150Logo (150), rounded like PowerShell does.
int[] msixSizes = [16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256,
                   50, 62, 75, 100, 200, 55, 66, 88, 176, 150, 188, 225, 300, 600];

var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", ".."));
var branding = Path.Combine(root, "branding");
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : root;

var png = Directory.CreateDirectory(Path.Combine(output, "branding", "png"));
foreach (var stale in png.EnumerateFiles("*.png"))
{
    stale.Delete(); // generated files only, rewritten just below
}

var sizes = msixSizes.Distinct().Order().ToArray();
foreach (var size in sizes)
{
    File.WriteAllBytes(Path.Combine(png.FullName, $"nexuswpp-{Kind(size)}-{size}.png"), Render(size));
}

WriteIco(Path.Combine(output, "icon.ico"), icoSizes);
WriteStoreLogos(Path.Combine(output, "dist", "store-logos"));
Console.WriteLine($"icon.ico ({icoSizes.Length} sizes), {sizes.Length} PNG in branding/png");
return 0;

string Kind(int size) => size >= MarkMin ? "mark" : "round";

byte[] Render(int size) => RenderSvg(File.ReadAllText(Path.Combine(branding, $"nexuswpp-{Kind(size)}.svg")), size, size);

void WriteIco(string path, int[] icoSizes)
{
    // PNG-compressed entries, as in the previous icon.ico (supported by Windows since Vista).
    var images = icoSizes.Select(Render).ToList();
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)icoSizes.Length);
    var offset = 6 + 16 * icoSizes.Length;
    for (var i = 0; i < icoSizes.Length; i++)
    {
        writer.Write((byte)(icoSizes[i] % 256));
        writer.Write((byte)(icoSizes[i] % 256));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)images[i].Length);
        writer.Write((uint)offset);
        offset += images[i].Length;
    }

    foreach (var image in images)
    {
        writer.Write(image);
    }
}

void WriteStoreLogos(string folder)
{
    // The medallion is a 600 px raster: the 1:1 and 9:16 images use their smallest accepted size so it stays at its
    // native resolution, on the dark green of the icon.
    Directory.CreateDirectory(folder);
    foreach (var size in new[] { 300, 150, 71 })
    {
        File.WriteAllBytes(Path.Combine(folder, $"tile-{size}.png"), Render(size));
    }

    var mark = File.ReadAllText(Path.Combine(branding, "nexuswpp-mark.svg"));
    var inner = mark[(mark.IndexOf('>', StringComparison.Ordinal) + 1)..mark.LastIndexOf("</svg>", StringComparison.Ordinal)];
    const string Background = "<rect width=\"100%\" height=\"100%\" fill=\"#07140c\"/>";

    byte[] Compose(int width, int height, int size, int y, string title = "") => RenderSvg(string.Create(CultureInfo.InvariantCulture,
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\">{Background}" +
        $"<svg x=\"{(width - size) / 2.0}\" y=\"{y}\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 108 108\">{inner}</svg>" +
        $"{title}</svg>"), width, height);

    File.WriteAllBytes(Path.Combine(folder, "box-1080x1080.png"), Compose(1080, 1080, 640, 220));
    const string Title = "<text x=\"360\" y=\"850\" text-anchor=\"middle\" font-family=\"Segoe UI\" font-weight=\"700\" font-size=\"76\" " +
        "letter-spacing=\"4\" fill=\"#4be08a\">NEXUSWPP</text>";
    File.WriteAllBytes(Path.Combine(folder, "poster-720x1080.png"), Compose(720, 1080, 560, 190, Title));
}

// The SVG drawn on a transparent width x height canvas, scaled from its own size (viewBox), encoded as PNG. Below the
// medallion's native 600 px, the SVG is drawn at 600 px and reduced with mipmaps (an area filter, as cairo's image
// filter): drawn straight at 16-96 px, the raster would be point-sampled and alias.
static byte[] RenderSvg(string svg, int width, int height)
{
    const int Native = 600;
    using var document = SKSvg.CreateFromSvg(svg);
    var picture = document.Picture ?? throw new InvalidDataException("The SVG could not be read.");
    var scale = Math.Max(1, (int)Math.Ceiling(Native / (double)Math.Min(width, height)));
    using var full = Draw(picture, width * scale, height * scale);
    if (scale == 1)
    {
        return Encode(full);
    }

    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.DrawImage(full, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    using var reduced = surface.Snapshot();
    return Encode(reduced);
}

static SKImage Draw(SKPicture picture, int width, int height)
{
    var bounds = picture.CullRect;
    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(width / bounds.Width, height / bounds.Height);
    canvas.Translate(-bounds.Left, -bounds.Top);
    canvas.DrawPicture(picture);
    canvas.Flush();
    return surface.Snapshot();
}

static byte[] Encode(SKImage image)
{
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
