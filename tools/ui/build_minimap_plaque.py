#!/usr/bin/env python3
"""Draws the minimap's next boss plaque (HudMinimap.BossPlaqueArt, 130x23) in the party finder's style
(build_party_finder.py): a dark stone strip with a bevelled gold trim and a stud at each end. It carries no text (the game
writes the countdown on it).

    python3 tools/ui/build_minimap_plaque.py assets/Hud/Minimap

Needs Python 3 with Pillow. Writes nextboss.png into the given folder (created when missing), replacing it.
"""

import os
import sys

from PIL import ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_party_finder import grain, stud, trim, vertical

WIDTH, HEIGHT = 130, 23


def plaque():
    image = grain(vertical((WIDTH, HEIGHT), (46, 34, 30), (20, 15, 13)), 3, 11)
    draw = ImageDraw.Draw(image)
    trim(draw, (0, 0, WIDTH - 1, HEIGHT - 1))
    stud(draw, 5, HEIGHT // 2, 3)
    stud(draw, WIDTH - 6, HEIGHT // 2, 3)
    return image


def main():
    if len(sys.argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        return 1

    os.makedirs(sys.argv[1], exist_ok=True)
    plaque().save(os.path.join(sys.argv[1], "nextboss.png"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
