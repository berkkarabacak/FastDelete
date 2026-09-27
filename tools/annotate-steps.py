"""Annotate the walkthrough screenshots with numbered badges + highlights."""
import json
from PIL import Image, ImageDraw, ImageFont

meta = json.load(open("docs/shots.json", encoding="utf-8-sig"))

ACCENT = (0, 120, 215, 255)
RED = (196, 43, 28, 255)
WHITE = (255, 255, 255, 255)
YELLOW = (255, 200, 0, 255)

def load_font(size):
    for p in (r"C:\Windows\Fonts\segoeuib.ttf", r"C:\Windows\Fonts\arialbd.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            continue
    return ImageFont.load_default()

def annotate(src, dst, origin, rect, number):
    img = Image.open(src).convert("RGBA")
    d = ImageDraw.Draw(img)
    scale = img.width / 2240 if img.width > 1500 else 1.0  # badge sizing relative to capture size
    x = rect["x"] - origin["left"]
    y = rect["y"] - origin["top"]
    w, h = rect["w"], rect["h"]
    pad = int(6 * scale)
    # highlight ring
    d.rounded_rectangle([x - pad, y - pad, x + w + pad, y + h + pad],
                        radius=int(8 * scale), outline=ACCENT, width=max(3, int(4 * scale)))
    # number badge
    br = int(26 * scale)
    bx, by = x - br // 2, y - br // 2
    d.ellipse([bx, by, bx + br, by + br], fill=RED, outline=WHITE, width=max(2, int(3 * scale)))
    f = load_font(int(20 * scale))
    t = str(number)
    tb = d.textbbox((0, 0), t, font=f)
    d.text((bx + (br - (tb[2] - tb[0])) / 2, by + (br - (tb[3] - tb[1])) / 2 - tb[1]), t, font=f, fill=WHITE)
    img.convert("RGB").save(dst, quality=92)
    print("annotated", dst)

annotate("docs/step1-raw.png", "docs/step-1.png", meta["origin"], meta["step1"], 1)
annotate("docs/step2-raw.png", "docs/step-2.png", meta["origin"], meta["step2"], 2)
annotate("docs/step3-raw.png", "docs/step-3.png", meta["dialogOrigin"], meta["step3"], 3)
