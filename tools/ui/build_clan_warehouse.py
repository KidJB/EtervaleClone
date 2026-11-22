#!/usr/bin/env python3
"""Draws the Clan Warehouse window's art in the party finder's style (build_party_finder.py: stone, bevelled gold trim,
corner studs): the window with its header band, the sunken slot grid of one page and the side pane, tabs, buttons, the
close button, a log row band and the log's action icons. The images carry no text (the game writes it in the player's
language). The window's layout matches HudClanWarehouseWindow's constants.

    python3 tools/ui/build_clan_warehouse.py assets/Hud/ClanWarehouse

Needs Python 3 with Pillow. Writes PNG files into the given folder (created when missing), replacing those it makes.
"""

import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_party_finder import (GOLD, GOLD_DARK, GOLD_LIGHT, INSET, INSET_EDGE, button, close, grain, inset, row, stud, tab, trim,
                                vertical)

# HudClanWarehouseWindow's layout.
WIDTH, HEIGHT = 720, 440
MARGIN = 20
HEADER = 56
CELL = 22
COLUMNS = ROWS = 9
GRID_X, GRID_Y = MARGIN + 12, HEADER + 28
PANE_X = MARGIN + COLUMNS * CELL + 36
CONTENT_Y = HEADER + 16 + 24 + 14
FOOTER_Y = HEIGHT - 46
PAGE_Y = GRID_Y + ROWS * CELL + 18


def window():
    image = grain(vertical((WIDTH, HEIGHT), (50, 44, 38), (22, 19, 16)), 4, 7)
    draw = ImageDraw.Draw(image)
    # The header band: a darker carved strip under the title, closed by a gold rule.
    band = vertical((WIDTH - 12, HEADER - 6), (64, 52, 36), (34, 28, 22))
    image.paste(band, (6, 6))
    draw.line((10, HEADER, WIDTH - 11, HEADER), fill=GOLD_DARK)
    draw.line((10, HEADER + 1, WIDTH - 11, HEADER + 1), fill=GOLD)
    trim(draw, (0, 0, WIDTH - 1, HEIGHT - 1), 2)
    draw.rectangle((4, 4, WIDTH - 5, HEIGHT - 5), outline=GOLD_DARK)
    for cx, cy in ((7, 7), (WIDTH - 8, 7), (7, HEIGHT - 8), (WIDTH - 8, HEIGHT - 8)):
        stud(draw, cx, cy, 5)
    stud(draw, WIDTH // 2, HEADER, 4)

    # The grid well: a gold-trimmed frame around one page of sunken cells.
    well = (GRID_X - 10, GRID_Y - 10, GRID_X + COLUMNS * CELL + 9, GRID_Y + ROWS * CELL + 9)
    draw.rectangle(well, fill=(30, 25, 20))
    trim(draw, well)
    for row_index in range(ROWS):
        for column in range(COLUMNS):
            x0, y0 = GRID_X + column * CELL, GRID_Y + row_index * CELL
            draw.rectangle((x0, y0, x0 + CELL - 1, y0 + CELL - 1), fill=INSET, outline=(44, 37, 28))
            draw.line((x0 + 1, y0 + 1, x0 + CELL - 2, y0 + 1), fill=(8, 7, 6))
    # The page bar under the grid, and the capacity box under it.
    inset(draw, (GRID_X - 10, PAGE_Y - 6, GRID_X + COLUMNS * CELL + 9, PAGE_Y + 31))
    inset(draw, (GRID_X - 10, PAGE_Y + 42, GRID_X + COLUMNS * CELL + 9, HEIGHT - 16))

    # The side pane: tabs sit on its top edge, the content in the sunken area under them.
    pane = (PANE_X - 8, CONTENT_Y - 10, WIDTH - MARGIN + 6, FOOTER_Y - 10)
    inset(draw, pane)
    draw.line((pane[0], pane[1] - 1, pane[2], pane[1] - 1), fill=GOLD_DARK)
    # A thin gold divider between the grid column and the pane.
    divider = (GRID_X + COLUMNS * CELL + PANE_X - 8) // 2 + 4
    draw.line((divider, HEADER + 14, divider, HEIGHT - 14), fill=GOLD_DARK)
    stud(draw, divider, HEADER + 14, 3)
    stud(draw, divider, HEIGHT - 14, 3)
    return image


def icon(kind):
    """A 14-pixel mark for a log line: an item put in (green, down), taken out (red, up), gold given, an upgrade bought."""
    size = 14
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if kind == "in":
        draw.polygon([(7, 13), (1, 6), (5, 6), (5, 0), (9, 0), (9, 6), (13, 6)], fill=(110, 200, 110), outline=(40, 90, 40))
    elif kind == "out":
        draw.polygon([(7, 0), (13, 7), (9, 7), (9, 13), (5, 13), (5, 7), (1, 7)], fill=(222, 120, 96), outline=(110, 44, 30))
    elif kind == "gold":
        draw.ellipse((1, 1, 12, 12), fill=GOLD, outline=GOLD_DARK)
        draw.ellipse((4, 4, 9, 9), outline=GOLD_LIGHT)
    else:
        points = [(7, 0), (9, 5), (14, 5), (10, 8), (12, 13), (7, 10), (2, 13), (4, 8), (0, 5), (5, 5)]
        draw.polygon(points, fill=(140, 190, 255), outline=(40, 70, 130))
    return image


def checkbox(on):
    size = 18
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, size - 1, size - 1), fill=INSET, outline=GOLD if on else INSET_EDGE)
    if on:
        draw.line((4, 9, 7, 13), fill=GOLD_LIGHT, width=2)
        draw.line((7, 13, 14, 4), fill=GOLD_LIGHT, width=2)
    return image


def main():
    if len(sys.argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        return 1

    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    def save(name, image):
        image.save(os.path.join(out, name))

    save("cw_window.png", window())
    save("cw_tab.png", tab((90, 24), False))
    save("cw_tab_on.png", tab((90, 24), True))
    for state in ("normal", "lit", "pressed"):
        save(f"cw_button_{state}.png", button((110, 26), state))
    save("cw_close.png", close(False))
    save("cw_close_lit.png", close(True))
    save("cw_row.png", row((440, 20), False))
    save("cw_row_lit.png", row((440, 20), True))
    for kind in ("in", "out", "gold", "upgrade"):
        save(f"cw_{kind}.png", icon(kind))
    save("cw_check.png", checkbox(False))
    save("cw_check_on.png", checkbox(True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
