"""Generate Assets/app.ico for FastDelete: red rounded square + white trash can."""
from PIL import Image, ImageDraw

S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# rounded red square
d.rounded_rectangle([8, 8, S - 8, S - 8], radius=48, fill=(196, 43, 28, 255))

# trash can body
bx0, by0, bx1, by1 = 78, 92, 178, 206
d.rounded_rectangle([bx0, by0, bx1, by1], radius=10, fill=(255, 255, 255, 255))
# inner cut to suggest a bin
d.rounded_rectangle([bx0 + 12, by0 + 14, bx1 - 12, by1 - 10], radius=6, fill=(196, 43, 28, 255))
# vertical ribs
for x in (104, 128, 152):
    d.rounded_rectangle([x - 4, by0 + 22, x + 4, by1 - 18], radius=4, fill=(255, 255, 255, 255))
# lid
d.rounded_rectangle([66, 74, 190, 90], radius=8, fill=(255, 255, 255, 255))
# handle
d.rounded_rectangle([112, 62, 144, 76], radius=6, fill=(255, 255, 255, 255))

img.save(r"src/FastDelete.App/Assets/app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((512, 512)).save(r"docs/app-icon.png")
print("icon saved")
