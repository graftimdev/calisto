"""Render the Calisto icon (option B: crescent-lit cratered moon) and save it as a multi-size .ico plus a 256px PNG."""
import sys
from PIL import Image, ImageDraw

out_ico, out_png = sys.argv[1], sys.argv[2]
S = 1024  # supersampled canvas
c = S / 2
R = S * 112 / 256  # base radius at 256-scale geometry

BASE = (0x2E, 0x34, 0x3C, 255)
LIT = (0x6B, 0x73, 0x7E, 255)
CRATER = (0x9A, 0xA2, 0xAC, 255)

def circle(draw, cx, cy, r, fill):
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
circle(d, c, c, R, BASE)

# lit side: a larger offset circle clipped to the base disc
lit = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ld = ImageDraw.Draw(lit)
# geometry is taken from the 44px-radius design and scaled to R
k = R / 44
circle(ld, c + 18 * k, c - 12 * k, 52 * k, LIT)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).ellipse([c - R, c - R, c + R, c + R], fill=255)
img.paste(lit, (0, 0), Image.composite(lit.split()[3], Image.new("L", (S, S), 0), mask))

d = ImageDraw.Draw(img)
circle(d, c + 21 * k, c - 16 * k, 8 * k, CRATER)
circle(d, c + 35 * k, c + 12 * k, 5 * k, CRATER)
circle(d, c + 7 * k, c + 22 * k, 4 * k, CRATER)

sizes = [256, 128, 64, 48, 32, 24, 16]
frames = [img.resize((s, s), Image.LANCZOS) for s in sizes]
frames[0].save(out_png)
frames[0].save(out_ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[1:])
print("wrote", out_ico, out_png)
