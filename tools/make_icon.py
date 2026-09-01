#!/usr/bin/env python3
"""Draws the mod's icon and Workshop thumbnail: a line-art robot, black
outline on transparency.

A robot because the mod's whole point is a synthetic voice, and a flat outline
because that is what reads at 256px in the mods list — a shaded illustration
turns to mush at that size, and Besiege's own mod list is a row of small
squares.

Everything is drawn at 4x and downsampled, which is the cheapest anti-aliasing
there is: PIL's own drawing is hard-edged, and a 4x box-filtered downsample
gives clean strokes at every angle without a single extra dependency.

    ./tools/make_icon.py                 writes Resources/icon.png + thumb.png
    ./tools/make_icon.py --preview p.png ... and a white-backed preview to look at
"""
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
RESOURCES = os.path.join(HERE, "..", "MultiplayerTTS", "Resources")

# The drawing is laid out in this square and scaled to whatever is asked for,
# so the proportions are fixed and only the resolution changes.
UNIT = 450.0
INK = (0, 0, 0, 255)


def draw_robot(size, supersample=4):
    """Render the robot at `size` px square, transparent background."""
    px = int(size * supersample)
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    k = px / UNIT                      # unit -> pixel
    w = max(1, int(round(5.6 * k)))    # stroke width

    def S(*v):
        return [x * k for x in v]

    def rrect(x0, y0, x1, y1, r):
        d.rounded_rectangle(S(x0, y0, x1, y1), radius=r * k, outline=INK, width=w)

    def ellipse(x0, y0, x1, y1):
        d.ellipse(S(x0, y0, x1, y1), outline=INK, width=w)

    def disc(cx, cy, r):
        d.ellipse(S(cx - r, cy - r, cx + r, cy + r), fill=INK)

    def line(x0, y0, x1, y1):
        d.line(S(x0, y0, x1, y1), fill=INK, width=w)

    def arc(x0, y0, x1, y1, a0, a1):
        d.arc(S(x0, y0, x1, y1), a0, a1, fill=INK, width=w)

    # ---- antenna ---------------------------------------------------
    ellipse(214, 44, 236, 68)          # the bulb on top
    line(225, 68, 225, 86)             # stem down to the head

    # ---- head ------------------------------------------------------
    # Ear tabs first, so the head outline is drawn over their inner edge
    # and the two read as one piece rather than as boxes stuck on.
    rrect(151, 99, 175, 127, 4)
    rrect(275, 99, 299, 127, 4)

    rrect(172, 84, 278, 150, 30)       # the head itself
    rrect(191, 97, 259, 137, 19)       # the visor

    for cx in (212, 238):              # eyes, each with a pupil
        ellipse(cx - 12, 105, cx + 12, 129)
        disc(cx, 117, 4.5)

    # ---- neck ------------------------------------------------------
    rrect(209, 150, 241, 172, 3)

    # ---- shoulders and body ----------------------------------------
    rrect(151, 176, 172, 200, 4)
    rrect(278, 176, 299, 200, 4)

    rrect(168, 170, 282, 296, 6)       # torso
    rrect(190, 206, 262, 248, 21)      # speaker plate
    for i in range(4):                 # the dots on it
        disc(206 + i * 12.7, 227, 3.4)

    # ---- arms ------------------------------------------------------
    # A quarter-circle from just under each shoulder, curving outwards and
    # down, then a short straight to the wrist. Drawn as an arc rather than
    # a polyline so the bend has no visible corner.
    arc(112, 196, 232, 316, 180, 270)   # left upper arm
    line(112, 256, 112, 268)
    arc(218, 196, 338, 316, 270, 360)   # right upper arm
    line(338, 256, 338, 268)

    # ---- hands -----------------------------------------------------
    # A wrist band and two prongs, angled outwards, which is what reads as a
    # claw at this size without drawing fingers.
    for cx, out in ((112, -1), (338, 1)):
        rrect(cx - 15, 266, cx + 15, 282, 3)
        line(cx - 11, 282, cx - 11 + out * 6, 300)
        line(cx + 11, 282, cx + 11 + out * 6, 300)
        line(cx - 11 + out * 6, 300, cx + 11 + out * 6, 300)

    # ---- hips and legs ---------------------------------------------
    rrect(174, 296, 276, 316, 3)
    rrect(182, 316, 214, 356, 3)
    rrect(236, 316, 268, 356, 3)

    # ---- feet ------------------------------------------------------
    # Each foot is a wedge that flares away from the centre line: the inner
    # edge continues the leg straight down, the sole is flat, and the outer
    # edge rakes back up to the leg's outer corner.
    def foot(leg_x0, leg_x1, out):
        inner = leg_x1 if out < 0 else leg_x0     # the edge under the leg
        outer_top = leg_x0 if out < 0 else leg_x1  # where the rake meets the leg
        toe = outer_top + out * 20

        line(inner, 356, inner, 390)   # inner edge, straight down from the leg
        line(inner, 390, toe, 390)     # sole
        line(toe, 390, outer_top, 356) # rake back up to the leg

    foot(182, 214, -1)
    foot(236, 268, +1)

    return img.resize((size, size), Image.LANCZOS)


def main():
    args = sys.argv[1:]
    preview = None
    if "--preview" in args:
        i = args.index("--preview")
        preview = args[i + 1]

    os.makedirs(RESOURCES, exist_ok=True)

    icon = draw_robot(256)
    icon.save(os.path.join(RESOURCES, "icon.png"))
    print("wrote Resources/icon.png   256x256 RGBA")

    thumb = draw_robot(512)
    thumb.save(os.path.join(RESOURCES, "thumb.png"))
    print("wrote Resources/thumb.png  512x512 RGBA")

    # The icon is transparent, so it is invisible against a white page and
    # against a dark one it is invisible the other way. Check it on both.
    if preview:
        pad = 24
        big = draw_robot(320)
        canvas = Image.new("RGB", (320 * 2 + pad * 3, 320 + pad * 2),
                           (255, 255, 255))
        canvas.paste((38, 41, 46), (320 + pad * 2, 0,
                                    320 * 2 + pad * 3, 320 + pad * 2))
        canvas.paste(big, (pad, pad), big)
        canvas.paste(big, (320 + pad * 2, pad), big)
        canvas.save(preview)
        print("wrote %s" % preview)


if __name__ == "__main__":
    main()
