"""
Builds the README images from the screenshots that `Tandem.exe --render-previews` writes:
the banner, the feature cards, the three steps, the "how it works" diagram and the framed
screenshots, each in a light and a dark version.

    python tools/readme-assets.py .

Needs Pillow and the Windows fonts Sitka, Segoe UI Variable and Segoe Fluent Icons.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

repo = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
shots = repo / "docs" / "images"
out = shots / "readme"
out.mkdir(exist_ok=True)
FONTS = Path("C:/Windows/Fonts")

CLAY = (196, 103, 63)
CREAM = (244, 239, 230)
ACCENTS = {
    "coral": (224, 122, 95),
    "sky": (59, 130, 196),
    "teal": (42, 157, 143),
    "amber": (217, 154, 43),
    "plum": (155, 89, 182),
    "clay": CLAY,
}
THEMES = {
    "light": dict(card=(255, 255, 255), border=(233, 229, 218), text=(31, 30, 28), muted=(107, 103, 94),
                  subtle=(156, 151, 139), soft=(246, 244, 238), stage=((245, 231, 224), (251, 243, 234)),
                  line=(201, 193, 180), tint=0.14),
    "dark": dict(card=(34, 32, 30), border=(58, 54, 51), text=(242, 238, 231), muted=(181, 174, 164),
                 subtle=(138, 131, 121), soft=(43, 40, 37), stage=((48, 36, 30), (24, 22, 20)),
                 line=(96, 89, 81), tint=0.22),
}


# ----------------------------------------------------------------------------- helpers

def font(name, size, variation=None):
    f = ImageFont.truetype(str(FONTS / name), size)
    if variation:
        try:
            f.set_variation_by_name(variation)
        except Exception:
            pass
    return f


def ui(size, weight=None):
    return font("SegUIVar.ttf", size, weight)


def icon_font(size):
    return font("SegoeIcons.ttf", size)


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def vertical_gradient(w, h, top, bottom):
    base = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(base)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=mix(top, bottom, y / max(1, h - 1)) + (255,))
    return base


def glow(size, color, alpha, blur):
    layer = Image.new("RGBA", (size * 2, size * 2), (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse([size // 2, size // 2, size * 3 // 2, size * 3 // 2], fill=color + (alpha,))
    return layer.filter(ImageFilter.GaussianBlur(blur))


def rounded(im, radius):
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.size[0] - 1, im.size[1] - 1], radius, fill=255)
    result = im.copy()
    result.putalpha(Image.composite(im.split()[-1], mask, mask) if im.mode == "RGBA" else mask)
    return result


def shadow_of(im, blur, alpha, offset=(0, 14)):
    a = im.split()[-1].point(lambda v: v * alpha // 255)
    sh = Image.new("RGBA", (im.size[0] + blur * 4, im.size[1] + blur * 4), (0, 0, 0, 0))
    sh.paste(Image.new("RGBA", im.size, (0, 0, 0, 255)), (blur * 2 + offset[0], blur * 2 + offset[1]), a)
    return sh.filter(ImageFilter.GaussianBlur(blur))


def wrap(draw, text, fnt, width):
    lines, line = [], ""
    for word in text.split():
        test = f"{line} {word}".strip()
        if draw.textlength(test, font=fnt) <= width:
            line = test
        else:
            lines.append(line)
            line = word
    if line:
        lines.append(line)
    return lines


def card(draw, box, theme, radius=28):
    draw.rounded_rectangle(box, radius, fill=theme["card"] + (255,), outline=theme["border"] + (255,), width=2)


def icon_badge(canvas, center, color, glyph, theme, size=84):
    """A soft colored circle with a Fluent icon in it, like the app's own badges."""
    d = ImageDraw.Draw(canvas)
    cx, cy = center
    r = size // 2
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=mix(theme["card"], color, theme["tint"] + 0.08) + (255,))
    f = icon_font(int(size * 0.44))
    l, t, rr, b = d.textbbox((0, 0), glyph, font=f)
    d.text((cx - (l + rr) / 2, cy - (t + b) / 2), glyph, font=f, fill=color)


def save(im, name):
    im.save(out / name, optimize=True)
    print(out / name, im.size)


# ----------------------------------------------------------------------------- banner

def banner():
    W, H = 1600, 520
    canvas = vertical_gradient(W, H, (34, 31, 28), (18, 17, 16))
    dots = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(dots)
    for y in range(14, H, 28):
        for x in range(14, W, 28):
            dd.ellipse([x - 1, y - 1, x + 1, y + 1], fill=(255, 240, 220, 16))
    canvas = Image.alpha_composite(canvas, dots)
    for (cx, cy, r, color, alpha, blur) in [(190, 230, 300, CLAY, 70, 120), (1230, 260, 420, CLAY, 46, 160),
                                            (1500, 20, 260, (226, 176, 90), 26, 120)]:
        canvas.alpha_composite(glow(r, color, alpha, blur), (cx - r, cy - r))

    usage = Image.open(shots / "manage-usage-dark.png").convert("RGBA")
    shot = usage.crop((370, 170, 1240, 640))
    shot = shot.resize((int(shot.width * 0.74), int(shot.height * 0.74)), Image.LANCZOS)
    shot = rounded(shot, 22)
    fade = Image.new("L", shot.size, 255)
    fd = ImageDraw.Draw(fade)
    for y in range(shot.height):
        t = max(0.0, (y - shot.height * 0.62) / (shot.height * 0.38))
        fd.line([(0, y), (shot.width, y)], fill=int(255 * (1 - t)))
    shot.putalpha(Image.composite(shot.split()[-1], Image.new("L", shot.size, 0), fade))
    cx, cy = 900, 64
    canvas.alpha_composite(shadow_of(shot, 18, 140), (cx - 36, cy - 36))
    canvas.alpha_composite(shot, (cx, cy))
    menu = Image.open(shots / "menu-dark.png").convert("RGBA")
    menu = menu.resize((int(menu.width * 0.78), int(menu.height * 0.78)), Image.LANCZOS)
    # Its right edge sits in the gap between the card's two meters, its top below the "most room" box.
    canvas.alpha_composite(menu, (cx + 322 - menu.width + 12, 150))

    icon = Image.open(repo / "src" / "Tandem.App" / "Assets" / "Tandem.ico")
    icon.size = (256, 256)
    icon = icon.convert("RGBA").resize((124, 124), Image.LANCZOS)
    canvas.alpha_composite(shadow_of(icon, 10, 120, (0, 6)), (96 - 20, 118 - 20))
    canvas.alpha_composite(icon, (96, 118))
    d = ImageDraw.Draw(canvas)
    d.text((246, 106), "Tandem", font=font("SitkaVF.ttf", 112, "Heading Semibold"), fill=CREAM)
    d.text((100, 282), "Every Claude account,", font=ui(40, "Semibold"), fill=CREAM)
    d.text((100, 332), "one click away.", font=ui(40, "Semibold"), fill=(214, 128, 92))
    d.text((100, 400), "The multi-account switcher for the Claude desktop app on Windows.", font=ui(21), fill=(170, 162, 150))

    chip_font = ui(18, "Semibold")
    labels = ["Windows 10 & 11", "Free & open source", "No telemetry"]
    chips = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    cd = ImageDraw.Draw(chips)
    x = 100
    for label in labels:
        w = int(cd.textlength(label, font=chip_font)) + 30
        cd.rounded_rectangle([x, 446, x + w, 480], 17, fill=(255, 245, 230, 14), outline=(255, 235, 215, 52), width=1)
        x += w + 12
    canvas = Image.alpha_composite(canvas, chips)
    d = ImageDraw.Draw(canvas)
    x = 100
    for label in labels:
        w = int(d.textlength(label, font=chip_font)) + 30
        d.text((x + 15, 451), label, font=chip_font, fill=(214, 206, 194))
        x += w + 12
    canvas.convert("RGB").save(shots / "banner.png", optimize=True)
    print(shots / "banner.png", canvas.size)


# ----------------------------------------------------------------------------- feature cards

FEATURES = [
    ("coral", "\ue8ab", "One click to switch", "Tray icon, Ctrl+Alt+A then 1 to 9, or a button on the Claude window."),
    ("sky", "\ue8d7", "Signed in for good", "Every account has its own data folder and stays signed in, like a second PC."),
    ("teal", "\ue9d2", "Usage at a glance", "5-hour and weekly limits, reset times, a 7-day chart, and the most room."),
    ("amber", "\ue8a9", "All accounts at once", "Open every account side by side, on one screen or across all of them."),
    ("plum", "\ue895", "The same Claude", "MCP servers, extensions, SSH, Code presets and history follow each account."),
    ("clay", "\uea18", "Safe by design", "No patching, no injection, no network, no telemetry. Sign-in stays untouched."),
]


def features(theme_name):
    theme = THEMES[theme_name]
    W, cols, gap, ch = 1760, 3, 28, 262
    cw = (W - gap * (cols - 1)) // cols
    rows = (len(FEATURES) + cols - 1) // cols
    canvas = Image.new("RGBA", (W, rows * ch + (rows - 1) * gap), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    title_font, body_font = ui(33, "Semibold"), ui(25)
    for i, (accent, glyph, title, body) in enumerate(FEATURES):
        x = (i % cols) * (cw + gap)
        y = (i // cols) * (ch + gap)
        color = ACCENTS[accent]
        card(d, [x, y, x + cw - 1, y + ch - 1], theme)
        # A thin accent stripe along the top edge.
        d.rounded_rectangle([x + 36, y, x + 120, y + 6], 3, fill=color)
        icon_badge(canvas, (x + 78, y + 86), color, glyph, theme)
        d.text((x + 138, y + 64), title, font=title_font, fill=theme["text"])
        for n, line in enumerate(wrap(d, body, body_font, cw - 80)[:3]):
            d.text((x + 40, y + 150 + n * 36), line, font=body_font, fill=theme["muted"])
    save(canvas, f"features-{theme_name}.png")


# ----------------------------------------------------------------------------- three steps

STEPS = [
    ("coral", "Download", "One file, no installer and no admin rights. Run Tandem.exe."),
    ("sky", "Add an account", "Pick a name and a color. A new Claude window opens: sign in once."),
    ("teal", "Switch", "Tray icon or Ctrl+Alt+A, then 1 to 9. Every account stays signed in."),
]


def steps(theme_name):
    theme = THEMES[theme_name]
    W, gap, ch = 1760, 64, 300
    cw = (W - gap * 2) // 3
    canvas = Image.new("RGBA", (W, ch), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    number_font, title_font, body_font = font("SitkaVF.ttf", 46, "Heading Semibold"), ui(34, "Semibold"), ui(25)
    for i, (accent, title, body) in enumerate(STEPS):
        x = i * (cw + gap)
        color = ACCENTS[accent]
        card(d, [x, 0, x + cw - 1, ch - 1], theme)
        d.ellipse([x + 40, 40, x + 124, 124], fill=color)
        num = str(i + 1)
        l, t, r, b = d.textbbox((0, 0), num, font=number_font)
        d.text((x + 82 - (l + r) / 2, 80 - (t + b) / 2), num, font=number_font, fill=(255, 255, 255))
        d.text((x + 40, 152), title, font=title_font, fill=theme["text"])
        for n, line in enumerate(wrap(d, body, body_font, cw - 80)[:2]):
            d.text((x + 40, 204 + n * 36), line, font=body_font, fill=theme["muted"])
        if i < len(STEPS) - 1:
            ax = x + cw + gap // 2
            arrow = icon_font(30)
            l, t, r, b = d.textbbox((0, 0), "\ue76c", font=arrow)
            d.text((ax - (l + r) / 2, ch / 2 - (t + b) / 2), "\ue76c", font=arrow, fill=theme["subtle"])
    save(canvas, f"steps-{theme_name}.png")


# ----------------------------------------------------------------------------- how it works

def curves(canvas, paths, color, width=4, ss=4):
    """Smooth S-curves with arrow heads, drawn at 4x and scaled down so they are anti-aliased."""
    layer = Image.new("RGBA", (canvas.width * ss, canvas.height * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for (x0, y0), (x1, y1) in paths:
        x0, y0, x1, y1 = x0 * ss, y0 * ss, x1 * ss, y1 * ss
        mid = x0 + (x1 - x0) * 0.5
        pts = []
        for i in range(81):
            t = i / 80
            # Cubic Bezier with horizontal tangents at both ends.
            x = (1 - t) ** 3 * x0 + 3 * (1 - t) ** 2 * t * mid + 3 * (1 - t) * t ** 2 * mid + t ** 3 * x1
            y = (1 - t) ** 3 * y0 + 3 * (1 - t) ** 2 * t * y0 + 3 * (1 - t) * t ** 2 * y1 + t ** 3 * y1
            pts.append((x, y))
        d.line(pts, fill=color + (255,), width=width * ss, joint="curve")
        d.polygon([(x1 + 2 * ss, y1), (x1 - 16 * ss, y1 - 10 * ss), (x1 - 16 * ss, y1 + 10 * ss)], fill=color + (255,))
    canvas.alpha_composite(layer.resize(canvas.size, Image.LANCZOS))


def diagram(theme_name):
    theme = THEMES[theme_name]
    W, H = 1760, 640
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    small, label_font = ui(23), ui(22, "Semibold")

    # Accounts on the right.
    accounts = [
        ("coral", "P", "Your original account", "Claude's own folder · started with no arguments"),
        ("sky", "W", "Work", "--user-data-dir=…\\Tandem\\Profiles\\work"),
        ("teal", "S", "Side project", "--user-data-dir=…\\Tandem\\Profiles\\side"),
    ]
    ax, aw, ah, agap = 1060, 700, 150, 45
    top = (H - (3 * ah + 2 * agap)) // 2
    centers = []
    for i, (accent, initial, name, detail) in enumerate(accounts):
        y = top + i * (ah + agap)
        card(d, [ax, y, ax + aw - 1, y + ah - 1], theme, radius=26)
        color = ACCENTS[accent]
        d.ellipse([ax + 32, y + 41, ax + 100, y + 109], fill=color)
        f = font("SitkaVF.ttf", 34, "Heading Semibold")
        l, t, r, b = d.textbbox((0, 0), initial, font=f)
        d.text((ax + 66 - (l + r) / 2, y + 73 - (t + b) / 2), initial, font=f, fill=(255, 255, 255))
        d.text((ax + 126, y + 38), name, font=ui(31, "Semibold"), fill=theme["text"])
        d.text((ax + 126, y + 84), detail, font=font("CascadiaMono.ttf", 19) if (FONTS / "CascadiaMono.ttf").exists() else small,
               fill=theme["muted"])
        centers.append((ax, y + ah // 2))

    # Tandem in the middle.
    tx, ty, tw, th = 560, H // 2 - 100, 300, 200
    tandem = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    td = ImageDraw.Draw(tandem)
    td.rounded_rectangle([0, 0, tw - 1, th - 1], 34, fill=CLAY + (255,))
    canvas.alpha_composite(shadow_of(tandem, 14, 90, (0, 10)), (tx - 28, ty - 28))
    canvas.alpha_composite(tandem, (tx, ty))
    icon = Image.open(repo / "src" / "Tandem.App" / "Assets" / "Tandem.ico")
    icon.size = (256, 256)
    icon = icon.convert("RGBA").resize((88, 88), Image.LANCZOS)
    canvas.alpha_composite(icon, (tx + (tw - 88) // 2, ty + 28))
    tf = font("SitkaVF.ttf", 40, "Heading Semibold")
    l, t, r, b = d.textbbox((0, 0), "Tandem", font=tf)
    d.text((tx + tw / 2 - (l + r) / 2, ty + 132), "Tandem", font=tf, fill=(255, 255, 255))

    paths = [((tx + tw + 6, H // 2), (cx - 10, cy)) for cx, cy in centers]
    caption = "starts the official app, each account with its own folder"
    for n, line in enumerate(wrap(d, caption, label_font, tw + 40)):
        lw = d.textlength(line, font=label_font)
        d.text((tx + tw / 2 - lw / 2, ty + th + 22 + n * 30), line, font=label_font, fill=theme["subtle"])

    # The browser hands sign-ins back through Tandem.
    bx, by, bw, bh = 0, H // 2 - 80, 420, 160
    card(d, [bx, by, bx + bw - 1, by + bh - 1], theme, radius=26)
    icon_badge(canvas, (bx + 74, by + bh // 2), ACCENTS["amber"], "\ue774", theme, size=76)
    d.text((bx + 132, by + 44), "Sign-in in", font=ui(29, "Semibold"), fill=theme["text"])
    d.text((bx + 132, by + 82), "your browser", font=ui(29, "Semibold"), fill=theme["text"])
    paths.append(((bx + bw + 6, H // 2), (tx - 10, H // 2)))
    curves(canvas, paths, theme["line"])
    d = ImageDraw.Draw(canvas)
    lw = d.textlength("claude:// link", font=label_font)
    d.text(((bx + bw + tx) / 2 - lw / 2, H // 2 - 50), "claude:// link", font=label_font, fill=theme["subtle"])
    save(canvas, f"how-it-works-{theme_name}.png")


# ----------------------------------------------------------------------------- framed screenshots

def stage(theme_name, w, h, radius=36):
    theme = THEMES[theme_name]
    top, bottom = theme["stage"]
    bg = vertical_gradient(w, h, top, bottom)
    for (cx, cy, r, color, alpha, blur) in [(int(w * 0.15), int(h * 0.1), int(w * 0.35), CLAY, 46 if theme_name == "dark" else 34, 160),
                                            (int(w * 0.9), int(h * 0.95), int(w * 0.3), (226, 176, 90), 30, 160)]:
        bg.alpha_composite(glow(r, color, alpha, blur), (cx - r, cy - r))
    return rounded(bg, radius)


def framed(name, theme_name, source, width=1760, pad=64):
    shot = Image.open(source).convert("RGBA")
    inner = width - 2 * pad
    shot = shot.resize((inner, int(shot.height * inner / shot.width)), Image.LANCZOS)
    shot = rounded(shot, 20)
    canvas = stage(theme_name, width, shot.height + 2 * pad)
    canvas.alpha_composite(shadow_of(shot, 22, 120), (pad - 44, pad - 44))
    canvas.alpha_composite(shot, (pad, pad))
    save(canvas, f"{name}-{theme_name}.png")


def duo(theme_name, width=1760, pad=72):
    """The switcher and the Add account dialog side by side."""
    menu = Image.open(shots / f"menu-{theme_name}.png").convert("RGBA")
    add = Image.open(shots / f"add-{theme_name}.png").convert("RGBA")
    pill = Image.open(shots / f"button-{theme_name}.png").convert("RGBA")
    height = 1000
    add = add.resize((int(add.width * height / add.height * 0.86), int(height * 0.86)), Image.LANCZOS)
    menu = menu.resize((int(menu.width * 1.3), int(menu.height * 1.3)), Image.LANCZOS)
    pill = pill.resize((int(pill.width * 1.6), int(pill.height * 1.6)), Image.LANCZOS)
    canvas = stage(theme_name, width, height)
    gap = 80
    total = menu.width + gap + add.width
    left = (width - total) // 2
    canvas.alpha_composite(menu, (left, (height - menu.height) // 2 + 40))
    canvas.alpha_composite(pill, (left + 30, (height - menu.height) // 2 - pill.height + 40))
    ax = left + menu.width + gap
    ay = (height - add.height) // 2
    framed_add = rounded(add, 22)
    canvas.alpha_composite(shadow_of(framed_add, 22, 110), (ax - 44, ay - 44))
    canvas.alpha_composite(framed_add, (ax, ay))
    save(canvas, f"switcher-{theme_name}.png")


def usage_cropped(theme_name):
    """The Usage page cut just below its second account card."""
    page = Image.open(shots / f"manage-usage-{theme_name}.png").convert("RGBA")
    probe = page.convert("RGB")
    x = page.width * 7 // 10
    background = probe.getpixel((x, 160))
    inside = [probe.getpixel((x, y)) != background for y in range(page.height)]
    edges = [y for y in range(page.height // 3, page.height) if inside[y] != inside[y - 1]]
    ends = [y for y in edges if not inside[y]]
    if len(ends) >= 2:
        page = page.crop((0, 0, page.width, min(page.height, ends[1] + 14)))
    return page


if __name__ == "__main__":
    banner()
    for theme_name in THEMES:
        features(theme_name)
        steps(theme_name)
        diagram(theme_name)
        framed("accounts", theme_name, shots / f"manage-accounts-{theme_name}.png")
        cropped = usage_cropped(theme_name)
        tmp = out / f"_usage-{theme_name}.png"
        cropped.save(tmp)
        framed("usage", theme_name, tmp)
        tmp.unlink()
        duo(theme_name)
