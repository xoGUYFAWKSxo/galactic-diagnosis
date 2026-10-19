"""Builds the Galactic Diagnosis app icon: the Galactic Studio spiral galaxy from galaxy.py with a crossed
wrench and screwdriver in the bottom-right corner. galaxy.py is copied unchanged from Galactic Macro, and the
badge follows the Macro robot's style (navy outline, metal gradient, cyan glow).

Needs Pillow and NumPy:  python make_icon.py
Writes icon.png (1024 x 1024) and icon.ico (16-256 px) next to this script, and the GitHub social preview to
../docs/social-preview.png. The galaxy is seeded, so the output is the same every time."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

import galaxy

HERE = Path(__file__).parent
SIZE = 1024
SS = 4  # supersampling for the tools' vector shapes
K = 0.72  # tool scale: local units below are designed at 1.0
CENTRE = (736, 746)  # where the tools cross, in 1024 px units

OUTLINE = (12, 16, 40, 255)
METAL_TOP = np.array([236, 242, 252])
METAL_BOTTOM = np.array([140, 156, 184])
VISOR = (18, 26, 58, 255)
GLOW = (90, 230, 255)
HANDLE_TOP = np.array([150, 244, 255])
HANDLE_BOTTOM = np.array([40, 170, 215])


class Canvas:
    """A square supersampled canvas for one tool, drawn horizontally around its centre."""

    def __init__(self):
        self.c = int(820 * K * SS * 1.4)
        self.mid = self.c / 2

    def box(self, x0, y0, x1, y1):
        f = K * SS
        return [self.mid + x0 * f, self.mid + y0 * f, self.mid + x1 * f, self.mid + y1 * f]

    def pts(self, *xy):
        f = K * SS
        return [(self.mid + x * f, self.mid + y * f) for x, y in xy]

    def mask(self):
        return Image.new('L', (self.c, self.c), 0)


def gradient(c, mask, top, bottom, y0, y1, cv):
    """Vertical gradient between local y0 and y1, painted where mask is set."""
    f = K * SS
    t = np.clip((np.arange(c) - (cv.mid + y0 * f)) / ((y1 - y0) * f), 0, 1)[:, None, None]
    rgb = top * (1 - t) + bottom * t
    grad = np.broadcast_to(rgb, (c, c, 3)).astype(np.uint8)
    layer = Image.new('RGBA', (c, c), (0, 0, 0, 0))
    layer.paste(Image.fromarray(grad, 'RGB'), (0, 0), mask)
    return layer


def outlined(cv, union):
    """Dark outline: the shape grown by ~16 px (at 1024) via blur + threshold, which also rounds the corners."""
    grown = union.filter(ImageFilter.GaussianBlur(9 * SS)).point(lambda v: 255 if v > 6 else 0)
    layer = Image.new('RGBA', (cv.c, cv.c), (0, 0, 0, 0))
    layer.paste(OUTLINE, (0, 0), grown)
    return layer


def wrench():
    cv = Canvas()
    m = cv.mask()
    d = ImageDraw.Draw(m)
    d.rounded_rectangle(cv.box(-215, -36, 215, 36), radius=36 * K * SS, fill=255)  # shaft
    d.ellipse(cv.box(150, -104, 358, 104), fill=255)  # open-end head
    d.ellipse(cv.box(-340, -86, -168, 86), fill=255)  # ring end
    d.rectangle(cv.box(254, -48, 400, 48), fill=0)  # jaw slot...
    d.ellipse(cv.box(206, -48, 302, 48), fill=0)  # ...with a round bottom
    d.ellipse(cv.box(-298, -44, -210, 44), fill=0)  # ring hole
    layer = outlined(cv, m)
    layer = Image.alpha_composite(layer, gradient(cv.c, m, METAL_TOP, METAL_BOTTOM, -104, 104, cv))
    # A darker groove down the shaft, like a pressed wrench.
    g = ImageDraw.Draw(layer)
    g.rounded_rectangle(cv.box(-150, -9, 150, 9), radius=9 * K * SS, fill=tuple(METAL_BOTTOM - 30) + (255,))
    return cv, layer, None


def screwdriver():
    cv = Canvas()
    handle, metal = cv.mask(), cv.mask()
    ImageDraw.Draw(handle).rounded_rectangle(cv.box(-340, -66, -96, 66), radius=56 * K * SS, fill=255)
    dm = ImageDraw.Draw(metal)
    dm.rounded_rectangle(cv.box(-112, -42, -70, 42), radius=10 * K * SS, fill=255)  # ferrule
    dm.rectangle(cv.box(-80, -22, 236, 22), fill=255)  # shaft
    dm.polygon(cv.pts((230, -22), (300, -12), (300, 12), (230, 22)), fill=255)  # flat tip
    union = Image.fromarray(np.maximum(np.asarray(handle), np.asarray(metal)))
    layer = outlined(cv, union)
    layer = Image.alpha_composite(layer, gradient(cv.c, metal, METAL_TOP, METAL_BOTTOM, -42, 42, cv))
    layer = Image.alpha_composite(layer, gradient(cv.c, handle, HANDLE_TOP, HANDLE_BOTTOM, -66, 66, cv))
    g = ImageDraw.Draw(layer)
    for y in (-30, 0, 30):  # grip grooves
        g.rounded_rectangle(cv.box(-300, y - 6, -136, y + 6), radius=6 * K * SS, fill=VISOR)
    return cv, layer, handle


def place(cv, layer, angle):
    """Rotates a tool canvas and drops it onto a full-size transparent layer, centred on CENTRE."""
    rot = layer.rotate(angle, resample=Image.BICUBIC)
    full = Image.new('RGBA', (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    full.alpha_composite(rot, (int(CENTRE[0] * SS - cv.c / 2), int(CENTRE[1] * SS - cv.c / 2)))
    return full


def badge():
    """The crossed tools as a SIZE x SIZE RGBA layer: screwdriver behind (handle bottom-right, tip top-left),
    wrench in front (ring bottom-left, open end top-right). The screwdriver handle glows cyan."""
    scv, slayer, shandle = screwdriver()
    wcv, wlayer, _ = wrench()
    s = place(scv, slayer, 135)
    hglow = Image.new('RGBA', (scv.c, scv.c), (0, 0, 0, 0))
    hglow.paste(GLOW + (255,), (0, 0), shandle)
    glow = place(scv, hglow, 135).filter(ImageFilter.GaussianBlur(22 * SS))
    out = Image.alpha_composite(glow, s)
    out = Image.alpha_composite(out, place(wcv, wlayer, 45))
    return out.resize((SIZE, SIZE), Image.LANCZOS)


def compose():
    base = galaxy.icon(SIZE)
    tools = badge()
    # Drop shadow so the tools stand off the galaxy.
    a = np.asarray(tools)[..., 3].astype(float) / 255
    shadow = Image.fromarray((a * 200).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(14))
    shadow_rgba = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    shadow_rgba.paste((0, 0, 0, 255), (8, 14), shadow)
    out = Image.alpha_composite(base, shadow_rgba)
    out = Image.alpha_composite(out, tools)
    # Keep the tools and their shadow inside the icon's rounded square.
    out.putalpha(Image.fromarray(np.minimum(np.asarray(out)[..., 3], np.asarray(base)[..., 3])))
    return out


def scaled(img, px):
    small = img.resize((px, px), Image.LANCZOS)
    if px <= 48:
        small = small.filter(ImageFilter.UnsharpMask(radius=1, percent=60, threshold=0))
    return small


def social_preview(icon_img):
    """1280 x 640 card for link previews: starfield, a large faint galaxy, the icon, name and tagline."""
    w, h = 1280, 640
    gs = 1100
    light = galaxy.galaxy(gs, 0.45 * gs, gs / 2, gs / 2, rot=np.deg2rad(-28))
    g = galaxy.tonemap(light, 2.2) * 0.55
    big = np.zeros((h, w, 3))
    x0, y0 = w - gs // 2 - 230, h // 2 - gs // 2  # galaxy centred right of the text, bleeding off the edges
    sx0, sy0 = max(0, -x0), max(0, -y0)
    dx0, dy0 = max(0, x0), max(0, y0)
    cw, ch = min(w - dx0, gs - sx0), min(h - dy0, gs - sy0)
    big[dy0:dy0 + ch, dx0:dx0 + cw] = g[sy0:sy0 + ch, sx0:sx0 + cw]
    card = galaxy.to_image(galaxy.screen(galaxy.background(w, h, seed=5), big)).convert('RGBA')

    ic = 300
    card.alpha_composite(icon_img.resize((ic, ic), Image.LANCZOS), (90, (h - ic) // 2))

    d = ImageDraw.Draw(card)
    fonts = Path('C:/Windows/Fonts')
    title = ImageFont.truetype(str(fonts / 'segoeuib.ttf'), 88)
    tag = ImageFont.truetype(str(fonts / 'segoeui.ttf'), 38)
    small = ImageFont.truetype(str(fonts / 'seguisb.ttf'), 28)
    tx = 90 + ic + 60
    d.text((tx, 190), 'Galactic Diagnosis', font=title, fill=(255, 255, 255))
    d.text((tx, 318), 'Plain-English PC health reports', font=tag, fill=(205, 220, 255))
    d.text((tx, 364), 'for repair technicians', font=tag, fill=(205, 220, 255))
    d.text((tx, 440), 'Before / after  ·  Runs from USB  ·  Free & open source', font=small, fill=GLOW)
    card.convert('RGB').save(HERE.parent / 'docs' / 'social-preview.png', optimize=True)


if __name__ == '__main__':
    img = compose()
    img.save(HERE / 'icon.png')
    sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    frames = [scaled(img, s) for s in sizes]
    frames[0].save(HERE / 'icon.ico', sizes=[(s, s) for s in sizes], append_images=frames[1:])
    social_preview(img)
