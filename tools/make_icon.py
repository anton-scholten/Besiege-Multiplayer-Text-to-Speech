#!/usr/bin/env python3
"""Draws the mod's icon: the speech glyph over Background.jpg.

    ./tools/make_icon.py
    ./tools/make_icon.py --preview      # also a 1024px look at it

Writes MultiplayerTTS/Resources/icon.png, 256px, which Mod.xml names as
<Icon> -- the tile in the game's mods menu.

The glyph is TTS.png, the same white page-and-speaker mark the hand-drawn
Thumbnail.xcf is built around, so the mods list and the Workshop page carry one
mark rather than two. The lettering stays on the thumbnail: at 256px a mod name
is a smear, and Besiege's mods list is a row of small squares.

The Workshop thumbnail is NOT written here. It is Thumbnail.png in the
repository root, drawn by hand and copied into Resources/, and this script would
otherwise overwrite it.

Everything is composed at 4x and scaled down at the end, which is the cheapest
anti-aliasing there is and what the other mods in this family do.
"""

import argparse
import os
import sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("This needs Pillow: pip install --user Pillow")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BACKGROUND = os.path.join(REPO, "Background.jpg")
GLYPH = os.path.join(REPO, "TTS.png")
RESOURCES = os.path.join(REPO, "MultiplayerTTS", "Resources")

SUPERSAMPLE = 4

# How much of the tile the glyph spans. The mark is wider than it is tall once
# its transparent margin is trimmed, so it is fitted to whichever side runs out
# first and centred in the other.
GLYPH_SPAN = 0.62

# The rounded frame: how round the corners are, how bright the rim is, and how
# heavy the darkening at the edges. Same numbers as the other mods in this
# family, so the tiles sit together in the mods list.
CORNER = 0.10
RIM = (255, 255, 255, 46)
RIM_WIDTH = 0.008
VIGNETTE = 90


def background(size):
    """The photograph, cropped square and covering the canvas."""
    image = Image.open(BACKGROUND).convert("RGBA")
    side = min(image.size)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    return image.crop((left, top, left + side, top + side)).resize(
        (size, size), Image.LANCZOS)


def glyph(size):
    """TTS.png trimmed to its ink and centred on a transparent canvas."""
    mark = Image.open(GLYPH).convert("RGBA")
    box = mark.getbbox()
    if box is not None:
        mark = mark.crop(box)

    span = int(size * GLYPH_SPAN)
    scale = min(span / float(mark.width), span / float(mark.height))
    mark = mark.resize((max(1, int(mark.width * scale)),
                        max(1, int(mark.height * scale))), Image.LANCZOS)

    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    layer.paste(mark, ((size - mark.width) // 2, (size - mark.height) // 2))
    return layer


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1],
                                           radius=radius, fill=255)
    return mask


def vignette(size, strength):
    """Darkens the edges, so a white glyph has something to sit against
    wherever the photograph happens to be pale.

    Drawn small and scaled up rather than drawn at full size: a stack of
    ellipses is a stack of hard edges, and at icon size those read as rings.
    Resampling from a sixty-fourth of the canvas turns them into a gradient."""
    coarse = 64
    shade = Image.new("L", (coarse, coarse), strength)
    draw = ImageDraw.Draw(shade)
    steps = 32
    # Darkest first and largest first: each ellipse is a little smaller and a
    # little lighter than the one under it, ending clear in the middle. The
    # first is bigger than the canvas, or the corners keep the flat fill.
    for i in range(steps):
        far = i / float(steps)
        inset = coarse * (-0.20 + 0.70 * far)
        draw.ellipse([inset, inset, coarse - 1 - inset, coarse - 1 - inset],
                     fill=int(strength * (1 - far) ** 1.6))
    return shade.resize((size, size), Image.BICUBIC)


def build(size):
    big = size * SUPERSAMPLE
    canvas = background(big)

    dark = Image.new("RGBA", (big, big), (0, 8, 20, 255))
    canvas = Image.composite(dark, canvas, vignette(big, VIGNETTE))

    canvas = Image.alpha_composite(canvas, glyph(big))

    frame = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle(
        [0, 0, big - 1, big - 1], radius=int(CORNER * big), outline=RIM,
        width=max(1, int(RIM_WIDTH * big)))
    canvas = Image.alpha_composite(canvas, frame)

    canvas.putalpha(rounded_mask(big, int(CORNER * big)))
    return canvas.resize((size, size), Image.LANCZOS)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true",
                        help="also write a 1024px preview beside the icon")
    args = parser.parse_args()

    for source in (BACKGROUND, GLYPH):
        if not os.path.isfile(source):
            sys.exit("No %s to draw with." % source)
    if not os.path.isdir(RESOURCES):
        os.makedirs(RESOURCES)

    wanted = [(os.path.join(RESOURCES, "icon.png"), 256)]
    if args.preview:
        # Outside Resources, which the mod folder ships whole: a preview is for
        # looking at while working on this, not for installing.
        wanted.append((os.path.join(REPO, "icon-preview.png"), 1024))

    for path, size in wanted:
        build(size).save(path)
        print("wrote %s (%dpx)" % (path, size))


if __name__ == "__main__":
    main()
