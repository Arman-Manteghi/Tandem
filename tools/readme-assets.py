"""
Builds the README images from the screenshots that `Tandem.exe --render-previews` writes:
the banner, and the Usage page cropped below its second account.

    python tools/readme-assets.py .

Needs Pillow and the Windows fonts Sitka and Segoe UI Variable.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

repo = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
img_dir = repo / "docs" / "images"
W, H = 1600, 520
FONTS = Path("C:/Windows/Fonts")


def font(name, size, variation=None):
    f = ImageFont.truetype(str(FONTS / name), size)
    if variation:
        try:
            f.set_variation_by_name(variation)
        except Exception:
            pass
    return f


def vertical_gradient(w, h, top, bottom):
    base = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(base)
    for y in range(h):
        t = y / (h - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        d.line([(0, y), (w, y)], fill=c)
    return base


def glow(size, color, alpha, blur):
    layer = Image.new("RGBA", (size * 2, size * 2), (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse([size // 2, size // 2, size * 3 // 2, size * 3 // 2], fill=color + (alpha,))
    return layer.filter(ImageFilter.GaussianBlur(blur))


def rounded(im, radius):
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.size[0] - 1, im.size[1] - 1], radius, fill=255)
    out = im.copy()
    out.putalpha(mask)
    return out


def shadow_of(im, blur, alpha, offset=(0, 14)):
    a = im.split()[-1].point(lambda v: v * alpha // 255)
    sh = Image.new("RGBA", (im.size[0] + blur * 4, im.size[1] + blur * 4), (0, 0, 0, 0))
    black = Image.new("RGBA", im.size, (0, 0, 0, 255))
    sh.paste(black, (blur * 2 + offset[0], blur * 2 + offset[1]), a)
    return sh.filter(ImageFilter.GaussianBlur(blur))


canvas = vertical_gradient(W, H, (34, 31, 28), (18, 17, 16))

# Subtle dot grid.
dots = Image.new("RGBA", (W, H), (0, 0, 0, 0))
dd = ImageDraw.Draw(dots)
for y in range(14, H, 28):
    for x in range(14, W, 28):
        dd.ellipse([x - 1, y - 1, x + 1, y + 1], fill=(255, 240, 220, 16))
canvas = Image.alpha_composite(canvas, dots)

# Warm glows: behind the logo and behind the screenshots.
for (cx, cy, r, color, alpha, blur) in [
    (190, 230, 300, (196, 103, 63), 70, 120),
    (1230, 260, 420, (196, 103, 63), 46, 160),
    (1500, 20, 260, (226, 176, 90), 26, 120),
]:
    g = glow(r, color, alpha, blur)
    canvas.alpha_composite(g, (cx - r, cy - r))

# Right: the Usage page's "most room" card and the switcher, from real screenshots.
usage = Image.open(img_dir / "manage-usage-dark.png").convert("RGBA")
card = usage.crop((370, 170, 1240, 640))
card = card.resize((int(card.width * 0.74), int(card.height * 0.74)), Image.LANCZOS)
card = rounded(card, 22)
fade = Image.new("L", card.size, 255)
fd = ImageDraw.Draw(fade)
for y in range(card.height):
    t = max(0.0, (y - card.height * 0.62) / (card.height * 0.38))
    fd.line([(0, y), (card.width, y)], fill=int(255 * (1 - t)))
card.putalpha(Image.composite(card.split()[-1], Image.new("L", card.size, 0), fade))
cx, cy = 900, 64
canvas.alpha_composite(shadow_of(card, 18, 140), (cx - 36, cy - 36))
canvas.alpha_composite(card, (cx, cy))

menu = Image.open(img_dir / "menu-dark.png").convert("RGBA")
menu = menu.resize((int(menu.width * 0.78), int(menu.height * 0.78)), Image.LANCZOS)
# Its right edge sits in the gap between the card's two meters, its top just below the
# "most room" box.
canvas.alpha_composite(menu, (cx + 322 - menu.width + 12, 150))

# Left: logo, name and tagline.
icon = Image.open(repo / "src" / "Tandem.App" / "Assets" / "Tandem.ico")
icon.size = (256, 256)
icon = icon.convert("RGBA").resize((124, 124), Image.LANCZOS)
canvas.alpha_composite(shadow_of(icon, 10, 120, (0, 6)), (96 - 20, 118 - 20))
canvas.alpha_composite(icon, (96, 118))

d = ImageDraw.Draw(canvas)
title = font("SitkaVF.ttf", 112, "Heading Semibold")
d.text((246, 106), "Tandem", font=title, fill=(244, 239, 230))
d.text((100, 282), "Every Claude account,", font=font("SegUIVar.ttf", 40, "Semibold"), fill=(244, 239, 230))
d.text((100, 332), "one click away.", font=font("SegUIVar.ttf", 40, "Semibold"), fill=(214, 128, 92))
d.text((100, 400), "Unofficial multi-account switcher for the Claude desktop app on Windows.",
       font=font("SegUIVar.ttf", 21), fill=(170, 162, 150))

# Feature chips, drawn on their own layer so their translucency blends.
chip_font = font("SegUIVar.ttf", 18, "Semibold")
chips = Image.new("RGBA", (W, H), (0, 0, 0, 0))
cd = ImageDraw.Draw(chips)
x = 100
for label in ["Windows 10 & 11", "Free & open source", "No telemetry"]:
    w = int(cd.textlength(label, font=chip_font)) + 30
    cd.rounded_rectangle([x, 446, x + w, 480], 17, fill=(255, 245, 230, 14), outline=(255, 235, 215, 52), width=1)
    x += w + 12
canvas = Image.alpha_composite(canvas, chips)
d = ImageDraw.Draw(canvas)
x = 100
for label in ["Windows 10 & 11", "Free & open source", "No telemetry"]:
    w = int(d.textlength(label, font=chip_font)) + 30
    d.text((x + 15, 451), label, font=chip_font, fill=(214, 206, 194))
    x += w + 12

out = img_dir / "banner.png"
canvas.convert("RGB").save(out, optimize=True)
print(out, canvas.size)


def crop_after_second_card(name):
    """Cuts the Usage page just below its second account card."""
    page = Image.open(img_dir / f"manage-usage-{name}.png").convert("RGBA")
    probe = page.convert("RGB")
    x = page.width * 7 // 10
    background = probe.getpixel((x, 160))
    inside = [probe.getpixel((x, y)) != background for y in range(page.height)]
    # Card edges below the first third of the page: (end of card, start of next card), ...
    edges = [y for y in range(page.height // 3, page.height) if inside[y] != inside[y - 1]]
    ends = [y for y in edges if not inside[y]]
    if len(ends) >= 2:
        bottom = min(page.height, ends[1] + 14)
        page = page.crop((0, 0, page.width, bottom))
    target = img_dir / f"usage-{name}.png"
    page.save(target, optimize=True)
    print(target, page.size)


for theme in ("light", "dark"):
    crop_after_second_card(theme)
