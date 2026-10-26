"""Rebuilds the medallion embedded in branding/nexuswpp-mark.svg and nexuswpp-round.svg at 600 px.

The Nexus icon kit embeds a 240 px medallion: julienpiron.png with its inner disc (radius 36.6 % of the side)
kept at 20 % opacity so the product pictogram reads on top. Same effect here from the 600 px julienpiron.png,
so the Store artworks and the large MSIX logos stay sharp. Then run scripts/export_icons.py.
"""
from pathlib import Path
import base64
import io
import re
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
INNER_RADIUS = 88 / 240
INNER_OPACITY = 0.2
SUPERSAMPLE = 4

logo = Image.open(ROOT / 'julienpiron.png').convert('RGBA')
side = logo.width
big = side * SUPERSAMPLE
mask = Image.new('L', (big, big), 255)
radius = INNER_RADIUS * big
centre = big / 2
ImageDraw.Draw(mask).ellipse((centre - radius, centre - radius, centre + radius, centre + radius),
                             fill=round(255 * INNER_OPACITY))
mask = mask.resize((side, side), Image.LANCZOS)
logo.putalpha(ImageChops.multiply(logo.getchannel('A'), mask))

buffer = io.BytesIO()
logo.save(buffer, 'WEBP', quality=92, method=6)
uri = 'data:image/webp;base64,' + base64.b64encode(buffer.getvalue()).decode('ascii')
for name in ('nexuswpp-mark.svg', 'nexuswpp-round.svg'):
    path = ROOT / 'branding' / name
    svg = path.read_text(encoding='utf-8')
    svg, count = re.subn(r'data:image/\w+;base64,[A-Za-z0-9+/=]+', uri, svg)
    if count != 1:
        raise SystemExit(f'{name}: expected one embedded medallion, found {count}')
    path.write_text(svg, encoding='utf-8')
print(f'medallion {side} px, {len(buffer.getvalue()) // 1024} KB, written to the mark and round SVGs')
