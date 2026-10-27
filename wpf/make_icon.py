from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
size = 256
im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((8, 8, 248, 248), radius=52, fill="#172554")
d.rounded_rectangle((18, 18, 238, 238), radius=43, outline="#2563EB", width=5)
# Календарь задания
d.rounded_rectangle((50, 52, 206, 207), radius=18, fill="#FFFFFF")
d.rounded_rectangle((50, 52, 206, 99), radius=18, fill="#3B82F6")
d.rectangle((50, 81, 206, 99), fill="#3B82F6")
for x in (83, 173):
    d.rounded_rectangle((x - 7, 38, x + 7, 72), radius=6, fill="#BFDBFE")
for y in (121, 151):
    for x in (82, 119, 156):
        d.ellipse((x - 6, y - 6, x + 6, y + 6), fill="#CBD5E1")
# Кнопка запуска поверх календаря
d.ellipse((126, 126, 220, 220), fill="#059669", outline="#FFFFFF", width=6)
d.polygon(((166, 149), (166, 197), (202, 173)), fill="#FFFFFF")
im.save(root / "app.png")
im.save(root / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(root / "app.ico")
