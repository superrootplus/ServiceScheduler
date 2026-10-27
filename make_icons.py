# -*- coding: utf-8 -*-
"""
make_icons.py — генерация иконок кнопок (PIL) и запись их в icons_data.py
в виде base64, чтобы exe оставался одним файлом.
"""
import base64
import io
import math
import os
from PIL import Image, ImageDraw

SIZE = 64  # исходный размер, в GUI масштабируется CTkImage

GREEN = (47, 165, 114, 255)
BLUE = (59, 142, 208, 255)
RED = (217, 59, 59, 255)
ORANGE = (232, 161, 61, 255)
GRAY = (107, 114, 128, 255)
WHITE = (255, 255, 255, 255)


def canvas():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def icon_add():
    img, d = canvas()
    d.ellipse([4, 4, SIZE - 4, SIZE - 4], fill=GREEN)
    c = SIZE // 2
    w = 7
    d.rectangle([c - w, 16, c + w, SIZE - 16], fill=WHITE)
    d.rectangle([16, c - w, SIZE - 16, c + w], fill=WHITE)
    return img


def icon_edit():
    img, d = canvas()
    # карандаш под 45°: корпус
    d.polygon([(14, 50), (40, 24), (46, 18), (54, 26), (48, 32), (22, 58)],
              fill=BLUE)
    # ластик
    d.polygon([(46, 18), (52, 10), (60, 18), (54, 26)], fill=(150, 200, 235, 255))
    # грифель
    d.polygon([(14, 50), (22, 58), (12, 60)], fill=(60, 60, 60, 255))
    return img


def icon_delete():
    img, d = canvas()
    # крышка
    d.rounded_rectangle([12, 12, SIZE - 12, 20], radius=3, fill=RED)
    d.rectangle([26, 6, 38, 14], fill=RED)
    # корпус корзины
    d.polygon([(16, 24), (48, 24), (45, 58), (19, 58)], fill=RED)
    # прорези
    for x in (25, 31, 37):
        d.rectangle([x, 30, x + 2, 52], fill=(0, 0, 0, 0))
    return img


def icon_run():
    img, d = canvas()
    d.ellipse([4, 4, SIZE - 4, SIZE - 4], fill=GREEN)
    d.polygon([(26, 18), (26, 46), (48, 32)], fill=WHITE)
    return img


def icon_power():
    img, d = canvas()
    # дуга 300°
    d.arc([12, 14, SIZE - 12, SIZE - 10], start=-60, end=240, fill=ORANGE, width=8)
    # штрих сверху
    d.rounded_rectangle([SIZE // 2 - 4, 4, SIZE // 2 + 4, 32], radius=4, fill=ORANGE)
    return img


def icon_refresh():
    img, d = canvas()
    # две дуги со стрелками
    d.arc([10, 10, SIZE - 10, SIZE - 10], start=60, end=230, fill=BLUE, width=7)
    d.arc([10, 10, SIZE - 10, SIZE - 10], start=240, end=50, fill=BLUE, width=7)
    # стрелки
    a1 = math.radians(60)
    x1, y1 = SIZE // 2 + 27 * math.cos(a1), SIZE // 2 - 27 * math.sin(a1)
    d.polygon([(x1 - 13, y1 + 3), (x1 + 4, y1 - 12), (x1 + 5, y1 + 14)], fill=BLUE)
    a2 = math.radians(240)
    x2, y2 = SIZE // 2 + 27 * math.cos(a2), SIZE // 2 - 27 * math.sin(a2)
    d.polygon([(x2 + 13, y2 - 3), (x2 - 4, y2 + 12), (x2 - 5, y2 - 14)], fill=BLUE)
    return img


def icon_journal():
    img, d = canvas()
    # документ
    d.rounded_rectangle([14, 6, 50, 58], radius=4, fill=GRAY)
    d.rectangle([18, 10, 46, 54], fill=WHITE)
    # строки текста
    for i, y in enumerate(range(18, 48, 8)):
        w = 24 if i < 3 else 14
        d.rectangle([21, y, 21 + w, y + 3], fill=GRAY)
    return img


def icon_help():
    from PIL import ImageFont
    img, d = canvas()
    d.ellipse([4, 4, SIZE - 4, SIZE - 4], fill=BLUE)
    try:
        font = ImageFont.truetype("arialbd.ttf", 40)
    except OSError:
        font = ImageFont.load_default()
    d.text((SIZE // 2, SIZE // 2 - 2), "?", font=font, fill=WHITE, anchor="mm")
    return img


ICONS = {
    "add": icon_add,
    "edit": icon_edit,
    "delete": icon_delete,
    "run": icon_run,
    "power": icon_power,
    "refresh": icon_refresh,
    "journal": icon_journal,
    "help": icon_help,
}


def main():
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "icons_data.py")
    parts = ["# -*- coding: utf-8 -*-",
             '"""Автогенерация make_icons.py — иконки кнопок в base64 (PNG)."""',
             "ICON_B64 = {"]
    for name, fn in ICONS.items():
        img = fn()
        buf = io.BytesIO()
        img.save(buf, format="PNG")
        b64 = base64.b64encode(buf.getvalue()).decode("ascii")
        parts.append('    "%s": "%s",' % (name, b64))
    parts.append("}")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(parts) + "\n")
    print("Записано:", out_path)


if __name__ == "__main__":
    main()
