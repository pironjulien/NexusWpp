"""Renders the NexusWpp icons from branding/*.svg: icon.ico and the PNGs build_msix.ps1 packs.

The SVGs come from the Nexus icon kit (NexusPublish/communication/icones). Rule of the kit: under 44 px the
pictogram alone (nexuswpp-icon), from 44 px the pictogram in the julienpiron.fr medallion (nexuswpp-mark).
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
    return 'mark' if size >= MARK_MIN else 'icon'


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


out = BRANDING / 'png'
out.mkdir(exist_ok=True)
for stale in out.glob('*.png'):
    stale.unlink()
for size in sorted(set(MSIX_SIZES)):
    (out / f'nexuswpp-{kind(size)}-{size}.png').write_bytes(render(size))
write_ico(ROOT / 'icon.ico', ICO_SIZES)
print(f'icon.ico ({len(ICO_SIZES)} sizes), {len(set(MSIX_SIZES))} PNG in branding/png')
