#!/usr/bin/env python3
"""Draws the party finder's interface art in the game's style: dark stone panels with a bevelled gold trim and corner
studs, a carved title plate, list rows, tabs, buttons, check boxes, choices, text fields and a scroll bar. The images
carry no text (the game writes the titles and labels in the player's language).

    python3 tools/ui/build_party_finder.py assets/Hud/PartyFinder

Needs Python 3 with Pillow. Writes PNG files into the given folder (created when missing), replacing those it makes.
"""

import os
import random
import sys

from PIL import Image, ImageDraw

GOLD_LIGHT = (236, 206, 128)
GOLD = (186, 146, 72)
GOLD_DARK = (96, 68, 30)
STONE_TOP = (48, 42, 36)
STONE_BOTTOM = (24, 21, 18)
INSET = (14, 12, 10)
INSET_EDGE = (70, 58, 40)


def vertical(size, top, bottom):
    width, height = size
    image = Image.new("RGBA", size)
    pixels = image.load()
    for y in range(height):
        t = y / max(1, height - 1)
        colour = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        for x in range(width):
            pixels[x, y] = colour
    return image


def grain(image, strength, seed):
    """Speckles the image a little so its stone does not read as a flat fill."""
    rng = random.Random(seed)
    pixels = image.load()
    width, height = image.size
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            d = rng.randint(-strength, strength)
            pixels[x, y] = (max(0, min(255, r + d)), max(0, min(255, g + d)), max(0, min(255, b + d)), a)
    return image


def trim(draw, box, width=1):
    """A bevelled gold frame: light on the top and left, dark on the bottom and right, gold between."""
    x0, y0, x1, y1 = box
    for i in range(width):
        draw.rectangle((x0 + i, y0 + i, x1 - i, y1 - i), outline=GOLD)
    draw.line((x0, y0, x1, y0), fill=GOLD_LIGHT)
    draw.line((x0, y0, x0, y1), fill=GOLD_LIGHT)
    draw.line((x0, y1, x1, y1), fill=GOLD_DARK)
    draw.line((x1, y0, x1, y1), fill=GOLD_DARK)


def stud(draw, cx, cy, radius):
    """A small gold diamond at a corner."""
    draw.polygon([(cx, cy - radius), (cx + radius, cy), (cx, cy + radius), (cx - radius, cy)], fill=GOLD, outline=GOLD_DARK)
    draw.point((cx, cy - radius + 1), fill=GOLD_LIGHT)


def inset(draw, box):
    """A sunken dark area with a faint edge, where lists and text sit."""
    x0, y0, x1, y1 = box
    draw.rectangle(box, fill=INSET, outline=INSET_EDGE)
    draw.line((x0 + 1, y0 + 1, x1 - 1, y0 + 1), fill=(6, 5, 4))
    draw.line((x0 + 1, y0 + 1, x0 + 1, y1 - 1), fill=(6, 5, 4))


def panel(size, seed, title_plate=True, list_area=None):
    """A window: stone with a double gold trim, corner studs, a title plate and optionally a sunken list area."""
    width, height = size
    image = grain(vertical(size, STONE_TOP, STONE_BOTTOM), 4, seed)
    draw = ImageDraw.Draw(image)
    trim(draw, (0, 0, width - 1, height - 1), 2)
    draw.rectangle((4, 4, width - 5, height - 5), outline=GOLD_DARK)
    for cx, cy in ((6, 6), (width - 7, 6), (6, height - 7), (width - 7, height - 7)):
        stud(draw, cx, cy, 5)
    if title_plate:
        plate = (width // 2 - 110, 8, width // 2 + 110, 32)
        draw.rectangle(plate, fill=(36, 28, 20))
        trim(draw, plate)
        stud(draw, plate[0], (plate[1] + plate[3]) // 2, 4)
        stud(draw, plate[2], (plate[1] + plate[3]) // 2, 4)
    if list_area is not None:
        inset(draw, list_area)
    return image


def button(size, state):
    """A bevelled button: normal, lit under the pointer, or pressed."""
    top, bottom = {
        "normal": ((92, 74, 44), (52, 40, 22)),
        "lit": ((140, 112, 62), (82, 62, 30)),
        "pressed": ((44, 34, 18), (78, 60, 32)),
    }[state]
    image = vertical(size, top, bottom)
    draw = ImageDraw.Draw(image)
    trim(draw, (0, 0, size[0] - 1, size[1] - 1))
    if state == "lit":
        draw.line((2, 2, size[0] - 3, 2), fill=(255, 230, 160))
    return image


def tab(size, chosen):
    """A tab: a raised gold-trimmed one when chosen, a darker one otherwise."""
    top, bottom = ((110, 86, 46), (62, 46, 24)) if chosen else ((44, 38, 32), (28, 24, 20))
    image = vertical(size, top, bottom)
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, size[0] - 1, size[1] - 1), outline=GOLD if chosen else GOLD_DARK)
    if chosen:
        draw.line((1, 1, size[0] - 2, 1), fill=GOLD_LIGHT)
    return image


def check(on, round_mark):
    """A check box (or a round choice) of 14 pixels, empty or marked."""
    size = 14
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if round_mark:
        draw.ellipse((0, 0, size - 1, size - 1), fill=INSET, outline=GOLD)
        if on:
            draw.ellipse((4, 4, size - 5, size - 5), fill=GOLD_LIGHT)
    else:
        draw.rectangle((0, 0, size - 1, size - 1), fill=INSET, outline=GOLD)
        if on:
            draw.line((3, 7, 6, 10), fill=GOLD_LIGHT, width=2)
            draw.line((6, 10, 11, 3), fill=GOLD_LIGHT, width=2)
    return image


def field(size, focused):
    image = Image.new("RGBA", size)
    draw = ImageDraw.Draw(image)
    inset(draw, (0, 0, size[0] - 1, size[1] - 1))
    if focused:
        draw.rectangle((0, 0, size[0] - 1, size[1] - 1), outline=GOLD_LIGHT)
    return image


def row(size, lit):
    """A list row's highlight: a soft gold band, brighter under the pointer than when chosen."""
    width, height = size
    image = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    alpha = 90 if lit else 60
    for x in range(width):
        t = abs(x - width / 2) / (width / 2)
        a = int(alpha * (1 - t * t))
        draw.line((x, 1, x, height - 2), fill=(200, 160, 80, a))
    draw.line((0, 0, width - 1, 0), fill=(186, 146, 72, 120))
    draw.line((0, height - 1, width - 1, height - 1), fill=(186, 146, 72, 120))
    return image


def separator(width):
    image = Image.new("RGBA", (width, 1), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for x in range(width):
        t = abs(x - width / 2) / (width / 2)
        draw.point((x, 0), fill=(150, 118, 60, int(140 * (1 - t))))
    return image


def close(lit):
    size = 18
    image = button((size, size), "lit" if lit else "normal")
    draw = ImageDraw.Draw(image)
    colour = (255, 236, 180) if lit else GOLD_LIGHT
    draw.line((5, 5, 12, 12), fill=colour, width=2)
    draw.line((12, 5, 5, 12), fill=colour, width=2)
    return image


def scroll_track(height):
    image = Image.new("RGBA", (6, height))
    draw = ImageDraw.Draw(image)
    inset(draw, (0, 0, 5, height - 1))
    return image


def scroll_thumb():
    image = vertical((6, 24), GOLD_LIGHT, GOLD_DARK)
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 5, 23), outline=GOLD_DARK)
    return image


def main():
    if len(sys.argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        return 1

    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    def save(name, image):
        image.save(os.path.join(out, name))

    # The list window (374x430): its title plate, the column titles' strip and the sunken list below.
    list_window = panel((374, 430), 1, list_area=(24, 92, 352, 412))
    ImageDraw.Draw(list_window).line((24, 86, 352, 86), fill=GOLD_DARK)
    save("pt_list.png", list_window)
    # The information and settings windows (398x342): a sunken area for their text.
    save("pt_info.png", panel((398, 342), 2, list_area=(20, 44, 377, 284)))
    save("pt_settings.png", panel((398, 342), 3, list_area=(14, 40, 383, 300)))
    for state in ("normal", "lit", "pressed"):
        save(f"pt_button_{state}.png", button((100, 24), state))
        save(f"pt_join_{state}.png", button((114, 34), state))
    save("pt_tab.png", tab((90, 22), False))
    save("pt_tab_on.png", tab((90, 22), True))
    save("pt_check.png", check(False, False))
    save("pt_check_on.png", check(True, False))
    save("pt_choice.png", check(False, True))
    save("pt_choice_on.png", check(True, True))
    save("pt_field.png", field((180, 20), False))
    save("pt_field_on.png", field((180, 20), True))
    save("pt_row.png", row((326, 31), False))
    save("pt_row_lit.png", row((326, 31), True))
    save("pt_line.png", separator(326))
    save("pt_close.png", close(False))
    save("pt_close_lit.png", close(True))
    save("pt_track.png", scroll_track(300))
    save("pt_thumb.png", scroll_thumb())
    return 0


if __name__ == "__main__":
    sys.exit(main())
