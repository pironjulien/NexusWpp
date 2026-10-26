"""Renders the NexusWpp icons from branding/*.svg: icon.ico and the PNGs build_msix.ps1 packs.

The SVGs come from the Nexus icon kit (NexusPublish/communication/icones), with the medallion rebuilt at 600 px by
scripts/make_medallion.py. Rule of the kit: under 44 px the
medallion cut round with transparent corners (nexuswpp-round), from 44 px the pictogram in the julienpiron.fr
medallion on its dark square (nexuswpp-mark). The pictogram alone is never shown.
Run again after replacing the SVGs:  python scripts/export_icons.py   (needs cairosvg)
"""
from pathlib import Path
import struct
import cairosvg

ROOT = Path(__file__).resolve().parent.parent
BRANDING = ROOT / 'branding'
MARK_MIN = 44

ICO_SIZES = (16, 20, 24, 32, 48, 64, 128, 256)
# Every pixel size build_msix.ps1 asks for: Square44x44Logo targetsizes, then the 100/125/150/200/400 scales of
# StoreLogo (50), Square44x44Logo (44) and Square150x150Logo (150), rounded like PowerShell does.
MSIX_SIZES = (16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256,
              50, 62, 75, 100, 200, 55, 66, 88, 176, 150, 188, 225, 300, 600)


def kind(size):
    return 'mark' if size >= MARK_MIN else 'round'


def render(size):
    return cairosvg.svg2png(url=str(BRANDING / f'nexuswpp-{kind(size)}.svg'), output_width=size, output_height=size)


def write_ico(path, sizes):
    # PNG-compressed entries, as in the previous icon.ico (supported by Windows since Vista).
    images = [render(size) for size in sizes]
    offset = 6 + 16 * len(sizes)
    header = struct.pack('<HHH', 0, 1, len(sizes))
    entries = b''
    for size, data in zip(sizes, images):
        entries += struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(header + entries + b''.join(images))


def write_store_logos(out):
    # Partner Center listing logos (Logos Windows Store), uploaded by hand: dist/ is not versioned.
    # The medallion is a 600 px raster: the 1:1 and 9:16 images use their smallest accepted size so it stays at its
    # native resolution, on the dark green of the icon.
    out.mkdir(parents=True, exist_ok=True)
    for size in (300, 150, 71):
        (out / f'tile-{size}.png').write_bytes(render(size))
    mark = (BRANDING / 'nexuswpp-mark.svg').read_text(encoding='utf-8')
    inner = mark[mark.index('>') + 1:mark.rindex('</svg>')]
    background = '<rect width="100%" height="100%" fill="#07140c"/>'

    def compose(width, height, size, y, title=''):
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}">{background}'
               f'<svg x="{(width - size) / 2}" y="{y}" width="{size}" height="{size}" viewBox="0 0 108 108">{inner}</svg>'
               f'{title}</svg>')
        return cairosvg.svg2png(bytestring=svg.encode('utf-8'), output_width=width, output_height=height)

    (out / 'box-1080x1080.png').write_bytes(compose(1080, 1080, 640, 220))
    title = ('<text x="360" y="850" text-anchor="middle" font-family="Segoe UI" font-weight="700" font-size="76" '
             'letter-spacing="4" fill="#4be08a">NEXUSWPP</text>')
    (out / 'poster-720x1080.png').write_bytes(compose(720, 1080, 560, 190, title))


out = BRANDING / 'png'
out.mkdir(exist_ok=True)
for stale in out.glob('*.png'):
    stale.unlink()
for size in sorted(set(MSIX_SIZES)):
    (out / f'nexuswpp-{kind(size)}-{size}.png').write_bytes(render(size))
write_ico(ROOT / 'icon.ico', ICO_SIZES)
write_store_logos(ROOT / 'dist' / 'store-logos')
print(f'icon.ico ({len(ICO_SIZES)} sizes), {len(set(MSIX_SIZES))} PNG in branding/png')
