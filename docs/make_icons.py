import sys
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]
SS = 1024  # supersampled canvas

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def gradient(size, c1, c2):
    img = Image.new("RGB", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * size - 2)
            px[x, y] = lerp(c1, c2, t)
    return img

def squircle_mask(size, radius_ratio=0.24):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    r = int(size * radius_ratio)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=r, fill=255)
    return m

def face_layer(size, color=(255, 255, 255, 255), with_scan=True):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    s = size
    # head
    hr = s * 0.17
    hc = (s * 0.5, s * 0.40)
    d.ellipse((hc[0] - hr, hc[1] - hr, hc[0] + hr, hc[1] + hr), fill=color)
    # shoulders (ellipse clipped by the squircle)
    sw, sh = s * 0.33, s * 0.30
    sc = (s * 0.5, s * 0.92)
    shoulders = Image.new("L", (s, s), 0)
    ImageDraw.Draw(shoulders).ellipse((sc[0] - sw, sc[1] - sh, sc[0] + sw, sc[1] + sh), fill=255)
    shoulders = Image.composite(shoulders, Image.new("L", (s, s), 0), squircle_mask(s))
    body = Image.new("RGBA", (s, s), color)
    layer.paste(body, (0, 0), shoulders)
    if with_scan:
        # corner "scan" brackets
        t = s * 0.045
        L = s * 0.16
        pad = s * 0.14
        col = (255, 255, 255, 170)
        for (x0, y0, dx, dy) in [(pad, pad, 1, 1), (s - pad, pad, -1, 1), (pad, s - pad, 1, -1), (s - pad, s - pad, -1, -1)]:
            d.rounded_rectangle((min(x0, x0 + dx * L), min(y0, y0 + dy * t), max(x0, x0 + dx * L), max(y0, y0 + dy * t)), radius=t / 2, fill=col)
            d.rounded_rectangle((min(x0, x0 + dx * t), min(y0, y0 + dy * L), max(x0, x0 + dx * t), max(y0, y0 + dy * L)), radius=t / 2, fill=col)
    return layer

def build(bg_rgb_or_pair, dot=None, with_scan=True):
    s = SS
    if isinstance(bg_rgb_or_pair[0], tuple):
        bg = gradient(s, *bg_rgb_or_pair).convert("RGBA")
    else:
        bg = Image.new("RGBA", (s, s), bg_rgb_or_pair + (255,))
    mask = squircle_mask(s)
    base = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    base.paste(bg, (0, 0), mask)
    # subtle gloss at the top
    gloss = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    gd = ImageDraw.Draw(gloss)
    gd.ellipse((-s * 0.3, -s * 0.7, s * 1.3, s * 0.5), fill=(255, 255, 255, 26))
    gloss = gloss.filter(ImageFilter.GaussianBlur(s * 0.06))
    gloss = Image.composite(gloss, Image.new("RGBA", (s, s), (0, 0, 0, 0)), mask)
    base = Image.alpha_composite(base, gloss)
    # silhouette shadow
    face = face_layer(s, with_scan=with_scan)
    shadow = face_layer(s, color=(0, 0, 0, 90), with_scan=False).filter(ImageFilter.GaussianBlur(s * 0.02))
    shadow = Image.composite(shadow, Image.new("RGBA", (s, s), (0, 0, 0, 0)), mask)
    base = Image.alpha_composite(base, shadow)
    base = Image.alpha_composite(base, face)
    if dot:
        d = ImageDraw.Draw(base)
        r = s * 0.12
        c = (s * 0.80, s * 0.80)
        ring = r + s * 0.035
        d.ellipse((c[0] - ring, c[1] - ring, c[0] + ring, c[1] + ring), fill=(255, 255, 255, 255))
        d.ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=dot + (255,))
    return base

def save_ico(img, path, sizes):
    frames = [img.resize((z, z), Image.LANCZOS) for z in sizes]
    frames[0].save(path, format="ICO", sizes=[(z, z) for z in sizes], append_images=frames[1:])

app = build(((67, 56, 202), (8, 145, 178)), dot=(46, 160, 67))
app.resize((256, 256), Image.LANCZOS).save(f"{OUT}/FacePresence-256.png")
save_ico(app, f"{OUT}/FacePresence.ico", [256, 128, 64, 48, 40, 32, 24, 20, 16])

states = {
    "present": (46, 160, 67),
    "absent": (230, 140, 20),
    "idle": (110, 110, 110),
    "error": (200, 40, 40),
    "info": (40, 120, 200),
}
for name, color in states.items():
    ico = build(color, with_scan=False)
    save_ico(ico, f"{OUT}/tray_{name}.ico", [48, 40, 32, 24, 20, 16])
    ico.resize((32, 32), Image.LANCZOS).save(f"{OUT}/tray_{name}-32.png")

# preview sheet: large + small sizes
sheet = Image.new("RGBA", (256 + 20 + 6 * 70, 256), (235, 235, 240, 255))
sheet.paste(app.resize((256, 256), Image.LANCZOS), (0, 0), app.resize((256, 256), Image.LANCZOS))
x = 276
for z in [48, 32, 24, 16]:
    im = app.resize((z, z), Image.LANCZOS)
    sheet.paste(im, (x, 20), im)
    x += z + 16
x = 276
for name in states:
    im = build(states[name], with_scan=False).resize((32, 32), Image.LANCZOS)
    sheet.paste(im, (x, 110), im)
    im16 = build(states[name], with_scan=False).resize((16, 16), Image.LANCZOS)
    sheet.paste(im16, (x + 8, 160), im16)
    x += 70
sheet.save(f"{OUT}/preview.png")
print("ok")


# ---- flags for the language switch (3:2, drawn at 4x and downscaled) ----
def flag_cs(w, h):
    im = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    d = ImageDraw.Draw(im)
    d.rectangle((0, h // 2, w, h), fill=(215, 20, 26, 255))
    d.polygon([(0, 0), (w // 2, h // 2), (0, h)], fill=(17, 69, 126, 255))
    return im

def flag_en(w, h):
    im = Image.new("RGBA", (w, h), (1, 33, 105, 255))
    d = ImageDraw.Draw(im)
    t = max(1, h // 10)
    d.line((0, 0, w, h), fill=(255, 255, 255, 255), width=t * 3)
    d.line((0, h, w, 0), fill=(255, 255, 255, 255), width=t * 3)
    d.line((0, 0, w, h), fill=(200, 16, 46, 255), width=t)
    d.line((0, h, w, 0), fill=(200, 16, 46, 255), width=t)
    d.rectangle((w // 2 - t * 2.5, 0, w // 2 + t * 2.5, h), fill=(255, 255, 255, 255))
    d.rectangle((0, h // 2 - t * 2.5, w, h // 2 + t * 2.5), fill=(255, 255, 255, 255))
    d.rectangle((w // 2 - t * 1.5, 0, w // 2 + t * 1.5, h), fill=(200, 16, 46, 255))
    d.rectangle((0, h // 2 - t * 1.5, w, h // 2 + t * 1.5), fill=(200, 16, 46, 255))
    return im

def rounded(im, r):
    m = Image.new("L", im.size, 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius=r, fill=255)
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    out.paste(im, (0, 0), m)
    return out

for name, fn in (("cs", flag_cs), ("en", flag_en)):
    big = rounded(fn(240, 160), 24)
    big.resize((60, 40), Image.LANCZOS).save(f"{OUT}/flag_{name}.png")
print("flags ok")
