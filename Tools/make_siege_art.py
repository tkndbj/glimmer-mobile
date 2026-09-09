# -*- coding: utf-8 -*-
"""Draws Thornwatch's hill, its ward line and its field, and cuts its gems and its cast.

    python Tools/make_siege_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_siege_art.py --write
    python Tools/make_siege_art.py --contact   # a sheet, laid out at the size a phone draws it

**Why a tool at all.** Invariant 7a's argument one step earlier: a step somebody has to remember
will be forgotten, and art nobody can re-derive is art that quietly drifts from the thing that
produced it. `make_quarry_art.py` was never committed, so withdrawing the Iron Quarry deleted it
and left Hollowmarch drawing twelve cast flipbooks nothing can prove are what a tool would cut
(CLAUDE.md's owed item 18). This one is committed with its first drop and `--check` reproduces
every byte.

**The wards were drawn and are now cut, and that is the owner's verdict rather than a change of
mind about invariant 32b.** The first cut composed them out of primitives - a stone pillar with a
crystal in it - on the argument that a goal approximated out of scenery comes to look like
dressing. Played on a device the verdict was "I didn't like the turrets at all", and the reason is
the half 32b does not cover: *composed* buys legibility and cannot buy **character**, and a turret
is the thing the player is looking at for the whole run. So they come from a pack drawn for exactly
this - ten turrets, each with an idle and a **recoil**, all of them facing up the way this board's
hill runs - and the legibility 32b asks for is bought a different way: the body is **tinted at run
time** to the colour it burns, so it wears the same four colours the gems and the raiders do.

**Four models rather than one, and the tint rather than four painted turrets.** Four models so a
ward is told apart by silhouette as well as by hue (CRAFT.md's rule about the board's vocabulary);
one tint applied at run time so the colour cannot drift from `Pal`, and so a bullet, a muzzle
flash, a gem's burst and a ward all take their colour from one place.

**And the ground is a field rather than a gradient**, for the same verdict: the first cut was dark
earth with a black wash over the top of it and read as a hole. What is up there now is the
top-down pack's own grass.

Three rules from cutting the packs before this one, all still true:

  * **Take one bounding box per animation, not per frame.** Per-frame trimming makes a character
    jitter by several pixels, because a raised arm moves the box.
  * **A box shared between a standing and a lying-down animation puts the standing one
    off-centre.** A death animation gets its own box.
  * **The `Death Sprite` / `DeadSprite` folder in every character pack is not an explosion.** It
    renders as a ring of green slime. The explosions come from the explosion pack.
"""
from __future__ import annotations

import argparse
import io
import math
import re
import sys
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parent.parent

#: Where the licensed CraftPix packs live. Nothing in the repo records it, which is why the tool
#: passes when the folder is absent: a checkout without the packs still runs the gate.
SOURCE = Path(r"C:\Users\Digikey\Downloads\craftpix-assets")

#: The second folder, added when the wards were re-cut. Two rather than one because these three
#: packs were bought later and live where they were downloaded; `--source` moves the first and
#: `--tower` the second.
TOWER = Path(r"C:\Users\Digikey\Downloads\to-assets")

MATCH3 = "craftpix-net-298179-match-3-game-asset-set.zip"
BLASTS = "craftpix-517297-explosions-sprite.zip"
MONSTERS = "craftpix-net-205925-monster-v3-character-sprites.zip"
BRUTES = "craftpix-net-894353-monster-v4-character-sprites.zip"

#: The overlord: the greatest of the four, and the thing the chapter ends on.
#:
#: **A third pack, for the reason the second one exists.** Three creepers come from one pack so they
#: read as three of a kind; the brute comes from another so a player can see at a glance it is not
#: one of those; the warlord from a third at three times the size. An overlord has to clear that bar
#: again - it is the last thing in the chapter and the one object a player spends a whole minute
#: looking at - so it is a different alien in different armour, and it is drawn taller still.
OVERLORDS = "craftpix-net-104412-alien-v5-enemy-sprite-set.zip"

OVER_IDLE = "PNG/Alien02/Idle"
OVER_WALK = "PNG/Alien02/Walk"
OVER_ATTACK = "PNG/Alien02/Attack"

#: The warlord, from a pack none of the creepers came from.
#:
#: **A different pack on purpose, and it is the same argument the brute already makes one step
#: further.** Three creepers come from one pack so they read as three of a kind; the brute comes
#: from another so that the one thing a player has to see at a glance is that it is not one of
#: those. A boss has to clear that bar by more: it is an armoured alien in a machine, against four
#: round soft monsters, at three times the size.
WARLORDS = "craftpix-net-515480-alien-v4-character-sprites.zip"

#: The blightcaller: the first boss a chapter shows, and the only one that does not walk.
#:
#: **A floating eye, and the pack it comes from is the point.** Four bosses on one hill have to be
#: told apart before any of them has done anything, and the two that shipped were an armoured alien
#: and another armoured alien. This one hovers, has one enormous eye and a tail instead of legs -
#: nothing else on this board is round, airborne or watching. Its `Fly` reel stands in for a walk,
#: which is exactly right: it is the only thing here that arrives without touching the ground.
BLIGHTCALLERS = "craftpix-net-248806-robots-v4-game-sprite-set.zip"

BLIGHT_IDLE = "PNG/Char04/Idle"
BLIGHT_WALK = "PNG/Char04/Fly"
BLIGHT_ATTACK = "PNG/Char04/Attack"

#: The warbringer: the one boss in this mode that reaches the line.
#:
#: **A slab**, and it is chosen for the silhouette rather than for the paint. Everything else on
#: this hill is round - four monsters, an eye, two domed aliens - so the thing whose whole fight is
#: "it is coming and it will get here" is a walking rectangle with fists, which reads as weight at
#: any size and at any distance down the hill.
WARBRINGERS = "craftpix-net-684986-robot-v3-enemy-character-sprites.zip"

BRINGER_IDLE = "PNG/Char05/Idle"
BRINGER_WALK = "PNG/Char05/Walk"
BRINGER_ATTACK = "PNG/Char05/Attack"

#: The turrets, their bullets and their muzzle flash.
TURRETS = "craftpix-net-715522-turrets-asset-pack-for-merge-shooter.zip"

#: The top-down tower-defence pack, for its grass. It is the only ground in any of these packs
#: drawn to be looked down on, which is what this hill is.
FIELD = "craftpix-net-869102-tower-defense-neighborhood-top-down-2d-asset-pack.zip"

#: The merge-shooter kit, for one thing: a white ring of debris that tints to anything, which is
#: what a matched gem comes apart into.
KIT = "craftpix-net-239749-merge-shooter-cartoon-asset-kit.zip"

#: The mine tileset the hill is floored with. Its own download rather than one of the folders
#: above, so `--mine` points at it.
MINE = Path(r"C:\Users\Digikey\Downloads\graphicriver-95eo2prH-topdown-tiles-mine.zip")

#: The ground each rung of a siege chapter is fought over: a **gradient map** over the mine
#: tileset's own square slabs, the tiles it is laid from, and the seed that lays them.
#:
#: <b>Which ground is arithmetic on the level's place in its chapter</b> - invariant 7c's rule and
#: the backdrop's shape exactly, so ten grounds serve every siege chapter that ever ships and a
#: second one costs no art at all.
#:
#: <b>One tileset rather than ten, and that is a decision with a known cost.</b> The isometric
#: terrain packs on this machine cannot floor this board at all: their ground is drawn as diamonds
#: with the side faces baked into the pixels, so a tile cannot be un-skewed into a square, and
#: laying them as a field and cropping a rectangle out of it only hides the skirts - what comes
#: back is still diagonal. The mine set is the only **top-down** terrain here, so ten places are
#: made out of one material. What that buys is real (a gradient map turns near-grey stone into
#: moss, sandstone, clay, basalt and ice, and a different tile mix repaves it) and what it cannot
#: buy is a different *surface*: every rung is stone, cut the same way. Nine more top-down
#: tilesets would replace `TONES` with nine `zipped` calls and change nothing else.
#:
#: The **ramp is a place rather than a difficulty** - the raid reads as moving somewhere over ten
#: rungs - and the mine's own untouched floor keeps rung five, where the first warlord stands.
GROUNDS = [
    ("hill1",  "moss",      (0x12, 0x1B, 0x0E), (0x86, 0x9A, 0x55), (5, 10, 8, 2, 7),      11),
    ("hill2",  "steel",     (0x11, 0x13, 0x17), (0x84, 0x8E, 0x9C), (13, 5, 10, 2, 11),     5),
    ("hill3",  "sandstone", (0x1F, 0x17, 0x0D), (0xAE, 0x8E, 0x5C), (5, 8, 2, 7, 1),       17),
    ("hill4",  "bog",       (0x0D, 0x15, 0x12), (0x55, 0x7C, 0x6C), (14, 10, 8, 7, 11),     3),
    ("hill5",  None,        None,               None,               (13, 14, 5, 10, 8, 2), 23),
    ("hill6",  "clay",      (0x1E, 0x0F, 0x0B), (0xA0, 0x55, 0x3E), (5, 2, 7, 1, 11),      29),
    ("hill7",  "khaki",     (0x1A, 0x18, 0x0E), (0xA4, 0x9C, 0x62), (10, 8, 2, 1),         31),
    ("hill8",  "basalt",    (0x0C, 0x0C, 0x12), (0x66, 0x62, 0x80), (13, 14, 8, 7, 1, 11), 37),
    ("hill9",  "ice",       (0x0E, 0x16, 0x1E), (0x74, 0x9C, 0xB8), (5, 10, 2, 11),        41),
    ("hill10", "ember",     (0x1C, 0x0E, 0x07), (0xB0, 0x68, 0x34), (14, 5, 8, 7, 1),      43),
]

#: The most colour a ground may carry, as its mean distance from grey.
#:
#: <b>A gradient map has no natural ceiling, and the two ends of the ramp decide the saturation as
#: much as the hue.</b> Left alone, the moss and the ember came out at twice everything else's
#: chroma with their value identical to it, which reads as *brighter* - and a saturated ground is
#: the one thing on this board competing with the cast walking over it, which is 37f's argument
#: said about the floor instead. Twelve is unmistakably a colour and does not fight four
#: cartoon-bright monsters for it.
GROUND_CHROMA_CAP = 12.0

#: One gem, in pixels. An import-cap decision rather than a drawing one - `ArtImportRules.Caps`
#: gives this folder 512, and a texture costs its dimensions rather than its file size.
TILE = 192

#: How tall a cast frame is drawn. The width follows the animation's own box.
CAST = 180

#: Frames kept from an animation. The packs ship 10 to 40; a flipbook on a phone at 12 fps has
#: nothing to do with more than this, and every extra frame is a texture.
FRAMES = 12

#: Which jewel each gem colour is cut from, and what it is.
#:
#: **Four distinct silhouettes as well as four distinct hues**, which is CRAFT.md's rule about the
#: board's vocabulary applied to a mode where the colour of a match is the whole decision: a player
#: who cannot separate red from green has to be able to separate a heart from a rhombus.
GEMS = {
    "gem_r": ("PNG/8.png", "a red heart"),
    "gem_g": ("PNG/5.png", "a green cabochon"),
    "gem_b": ("PNG/7.png", "a blue rhombus"),
    "gem_y": ("PNG/6.png", "an amber emerald-cut"),
}

#: The two explosions, and which of the pack's seven each is cut from. A raider comes apart in fire
#: and a ward comes down in smoke, which is the difference between something being destroyed and
#: something being *lost*.
BLAST_SET = {
    "boom_fire": ("2", 0.0, 1.0),
    "boom_smoke": ("3", 0.0, 1.0),
}

#: Every cast flipbook: the pack, the folder inside it, and what it is.
#:
#: **Walks rather than idles**, which is the whole point of this mode's hill - a raider that stands
#: still while sliding down a lane reads as a sprite being moved rather than as something coming.
#: The three creepers come from one pack on purpose (three creatures drawn by three hands read as
#: three games) and the brute comes from another, because the one thing a player has to see about a
#: brute at a glance is that it is not one of those.
#: The packs the shielded raiders come from. Two of these are opened for nothing else.
BULWARKS_A = "craftpix-net-603718-alien-v1-enemy-sprite-set.zip"
BULWARKS_B = "craftpix-net-424965-alien-v3-enemy-character-sprites.zip"
BULWARKS_C = "craftpix-net-259073-alien-v2-enemy-sprite-set.zip"

#: Every raider that walks down the hill: one body per colour, per kind.
#:
#: **Four bodies a kind rather than three shared between four colours, and the tint is gone.** A
#: raider used to be the pack's own art multiplied by 62% toward a `Pal` entry, with a coloured
#: wash behind it. `Image.color` is a multiply, so that could only ever *darken*: what it drew was
#: four silhouettes of the same value, and everything these packs are actually good at - the
#: shading, the highlights, the face - was spent saying one bit. The wards learned this one folder
#: over and the answer is the same: a real **hue rotation baked into the sprite**, which keeps every
#: highlight and simply makes the body that colour.
#:
#: Once the colour is in the paint, the body is free to say it too - so each colour gets its own
#: model and a player who cannot separate red from green can still separate a mushroom from a
#: cyclops. That is eighty-three characters in these packs being asked to do the job four were
#: doing.
#:
#: **Three families, told apart by where they come from.** The creepers are one pack so they read
#: as a set; the brutes are another, so the one thing a player must see at a glance is that a brute
#: is not one of those; and the bulwarks are aliens that are literally carrying a shield - which is
#: the only honest way to draw a unit holding one, and it cost a folder name rather than a drawing.
RAIDER_SET = {
    # creepers - one pack, four bodies
    "mon_r":     (MONSTERS, "PNG/Monster 1/Walk"),
    "mon_g":     (MONSTERS, "PNG/Monster 2/Walk"),
    "mon_b":     (MONSTERS, "PNG/Monster 5/Walk"),
    "mon_y":     (MONSTERS, "PNG/Monster 4/Walk"),

    # brutes - a second pack, so they are visibly not creepers
    "brute_r":   (BRUTES, "PNG/Monster 4/Walk"),
    "brute_g":   (BRUTES, "PNG/Monster 2/Walk"),
    "brute_b":   (BRUTES, "PNG/Monster 1/Walk"),
    "brute_y":   (BRUTES, "PNG/Monster 5/Walk"),

    # bulwarks - the four characters in these packs that hold a shield through a whole walk cycle
    "bulwark_r": (BULWARKS_A, "PNG/Alien2/Walk"),
    "bulwark_g": (BULWARKS_B, "PNG/Alien01/Walk"),
    "bulwark_b": (WARLORDS, "PNG/Alien01/Walk"),
    "bulwark_y": (BULWARKS_C, "PNG/Alien2/Walk"),
}

#: The warlord's three animations, cut together by `paired`.
#:
#: **An idle *and* a walk, and shipping only the idle was a bug somebody had to play to find.** The
#: reasoning that left the walk out was half right: a warlord walks to the middle of the hill and
#: then stands there for the rest of the run (`SiegeTuning.BossHold`), so a walk cycle looping under
#: something that is not moving is the very thing this mode's walks were chosen to avoid. What it
#: misses is the ten seconds *before* that, which is the one stretch where it really is crossing
#: ground - and an idle sliding down a hill is the exact fault the rule names, reported from play in
#: one word: **floating**. The view wears whichever matches what the board says it is doing.
#:
#: The attack is the only one allowed to leave the frame sideways (see `paired`): it throws a fist
#: a long way out, where a walk only strides a few pixels wider than a stand.
BOSS_IDLE = "PNG/Alien05/Idle"
BOSS_WALK = "PNG/Alien05/Walk"
BOSS_ATTACK = "PNG/Alien05/Attack"

#: The four turret models a ward line is drawn with, in ward order, and the hue each is painted.
#:
#: **The colour is baked in here rather than tinted at run time, and that is a correction.** It was
#: a run-time tint on the argument that one `Pal` entry should feed the gems, the raiders and the
#: wards alike - which is true and was not worth what it cost, because `Image.color` is a
#: *multiply*: it can only ever darken, so a bright cartoon turret pulled toward a saturated red
#: comes back dark, and lifted toward white first comes back pastel. Both were played and both were
#: wrong ("too dim", then washed out). A hue rotation keeps every highlight and every bit of
#: shading the pack drew and simply makes them *that colour*, which is what "brighter and more
#: alive" actually asks for.
#:
#: The hues are `Pal.Poppy`, `Pal.Mint`, `Pal.Azure` and `Pal.Sun` measured as hue angles, so the
#: line still agrees with the gems that feed it - the agreement is now checked by eye through
#: `--contact` rather than enforced by sharing one `Color`.
#:
#: The models are picked for how different they are from one another head-on, which is the only
#: view this board has of them.
#: **Five tiers rather than four models, which is a change of what a ward's picture is *for*.**
#: A line used to be told apart by silhouette (four turrets from four moulds) and by hue; a ward can
#: now be *upgraded*, so the silhouette has to carry the tier instead - a player who has spent a cog
#: on the red ward must be able to see that they did, from across the board, without reading a
#: number. Colour is then carried by the hue rotation alone, which is exactly what it was already
#: doing (see `hued`), and by the badge the view pins to the turret's shoulder.
#:
#: The five are guns six to ten of the merge kit's own upgrade ladder, which is a ladder somebody
#: drew as a ladder: each tier adds plating, then a second barrel, then a core - so a rank reads as
#: *more machine* rather than as the same machine in a different colour. Guns one to five are the
#: same ladder's bottom half and are deliberately unused; at the size this board draws a turret they
#: are a plain box with a barrel, which is not a turret anybody wants to be given five of.
WARD_TIERS = ("Gun06", "Gun07", "Gun08", "Gun09", "Gun10")

#: Which colour each ward is painted, as a hue angle. `Pal.Poppy`, `Pal.Mint`, `Pal.Azure` and
#: `Pal.Sun`, measured - so the line still agrees with the gems that feed it.
WARD_HUES = (
    ("r", 0.986),               # Poppy   #F2404F
    ("g", 0.308),               # Mint    #7BD86A
    ("b", 0.561),               # Azure   #4FC1FF
    ("y", 0.122),               # Sun     #FFC93C
)

#: How tall a warlord is drawn, in pixels, and how many frames each of its two reels keeps.
#:
#: **Bigger than a creeper's source art as well as bigger on the board**, because `SiegeView`
#: draws it about three cells tall against a creeper's one and a bit: cutting it at `CAST` would
#: mean upscaling the one thing in this mode the player spends half a minute looking at. The cast
#: reel keeps more frames than the idle because it is played *once*, over `SiegeTuning.BossTell`,
#: and a fourteen-frame throw at twelve a second is the difference between a lunge and a jerk.
BOSS = 340
BOSS_FRAMES, BOSS_CAST_FRAMES = 12, 14

#: How tall an overlord is cut. Larger again, because the view draws it larger again - it is the
#: last thing in the chapter and the one object a player watches for a whole minute.
OVERLORD = 400

#: How tall the other two are cut. **Each one is cut at the height the view draws it**, so nothing
#: here is ever upscaled: `SiegeView.TallOf` gives a blightcaller 2.6 cells and a warbringer 3.3,
#: against a warlord's 3.1 and an overlord's 3.5, and the ladder those four numbers make is the
#: first thing a player reads about which boss has arrived.
BLIGHT = 290
WARBRINGER = 360

#: The four bosses, each from a pack none of the others came from.
#:
#: **Four bodies, four packs, four sizes**, because a boss has to be told apart before it has done
#: anything. The chapter shipped two of these drawn from one *reel* and separated by a run-time
#: hue, which is the thinnest possible difference and read exactly as what it was.
#:
#: Each row is (pack, [idle, walk, attack], height, folders allowed to overflow the frame). All
#: three reels of one boss come off **one canvas** (`paired`), or it changes size when it throws.
BOSS_SET = {
    "blight": (BLIGHTCALLERS, [BLIGHT_IDLE, BLIGHT_WALK, BLIGHT_ATTACK], BLIGHT,
               (BLIGHT_ATTACK,)),
    "boss": (WARLORDS, [BOSS_IDLE, BOSS_WALK, BOSS_ATTACK], BOSS, (BOSS_ATTACK,)),
    "bringer": (WARBRINGERS, [BRINGER_IDLE, BRINGER_WALK, BRINGER_ATTACK], WARBRINGER,
                (BRINGER_ATTACK,)),
    "over": (OVERLORDS, [OVER_IDLE, OVER_WALK, OVER_ATTACK], OVERLORD, (OVER_ATTACK,)),
}

#: How many frames of a turret's recoil are kept. They are 10 to 20 in the pack and the whole
#: motion is over in a fifth of a second on screen.
FIRE_FRAMES = 7

#: A ward, in pixels. Taller than it is wide, because a turret is.
WARD_W, WARD_H = 192, 240

#: How far a ward's pixels are pulled toward its own hue. **Not all the way**: at 1.0 a turret is
#: one flat colour, which is what the first bake shipped and what came back as needing "a touch of
#: different colours". A fifth of the pack's own spread survives at 0.8, which is enough for the
#: orange trim to stay warm against a red body and for the dome to stay cool against a green one.
HUE_PULL = 0.8

#: How far a **raider** is pulled toward its colour, and how hard its saturation is pushed.
#:
#: **Gentler than a ward's on all three counts, and the reason is what each picture is for.** A
#: turret has to read as *lit* against a bright green hill, so it is pulled 80% of the way, floored
#: at half saturation and lifted in value. A monster has to keep being a monster: a face, an eye, a
#: mouth and whatever the pack drew in a second colour are the things that tell four bodies apart,
#: and pushing those as hard as a turret's collapses them into one flat shape - which is precisely
#: the fault the run-time coat had and this exists to fix. Pulled 70% with a low floor, a red
#: creeper is unmistakably red and still has its own shading.
#:
#: **The pull was 0.70 first and that was wrong, which only a sheet could say.** These packs paint a
#: character in two or three colours of their own, so a partial pull lands each of them somewhere
#: different: the blue creeper came out teal, the yellow one brown and the yellow bulwark
#: red-and-green. At 0.92 every body reaches its colour and the internal contrast the models are
#: told apart by survives - the skull is still lighter than the body it stands on, the armour still
#: darker than the trim. Rendered side by side at 0.70, 0.92 and 1.0, and 1.0 is the flat one.
CAST_PULL, CAST_SAT_GAIN, CAST_SAT_FLOOR = 0.92, 0.62, 0.30

#: How much a raider's value is lifted. Small, and it is here for one colour.
#:
#: **Yellow only reads as yellow when it is bright**, so a body rotated onto the Sun hue off a dark
#: source comes out *brown* - which was the yellow creeper, at every pull. A modest lift fixes it
#: and costs the other three nothing; twice this much starts flattening the reds, which is the
#: thing the low saturation floor is protecting. Rendered at 1.0, 1.08 and 1.15 to pick it.
CAST_VAL_GAIN, CAST_VAL_LIFT = 1.08, 0.04

STONE_DARK = (38, 46, 58)
STONE_MID = (62, 74, 90)
STONE_LIT = (108, 124, 146)


# --------------------------------------------------------------------------- helpers


def zipped(name, where=None):
    """One of the licensed packs, or None when it is not on this machine."""
    path = (where or SOURCE) / name
    return zipfile.ZipFile(path) if path.exists() else None


def read(z, name):
    return Image.open(io.BytesIO(z.read(name))).convert("RGBA")


def box_of(images):
    """One bounding box over a whole animation. See the module docstring."""
    box = None
    for im in images:
        here = im.getbbox()
        if here is None:
            continue
        box = here if box is None else (min(box[0], here[0]), min(box[1], here[1]),
                                        max(box[2], here[2]), max(box[3], here[3]))
    return box


def fit(im, side, scale=1.0):
    """Trims to its own alpha and centres it on a square of `side`, at `scale` of the room."""
    bb = im.getbbox()
    if bb:
        im = im.crop(bb)

    room = max(1, int(side * scale))
    ratio = min(room / im.width, room / im.height)
    im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))), Image.LANCZOS)

    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    out.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    return out


def glow(w, h, cx, cy, radius, colour, strength=1.0, power=2.0):
    """A soft radial light. Everything drawn here that gives off light is built out of these."""
    y, x = np.mgrid[0:h, 0:w]
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / max(1.0, radius)

    a = np.clip(1.0 - d, 0.0, 1.0) ** power * strength
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., 0] = colour[0]
    rgba[..., 1] = colour[1]
    rgba[..., 2] = colour[2]
    rgba[..., 3] = a * 255.0
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


def speckle(w, h, seed, amount=14.0):
    """Deterministic grain, so a big stretched surface is not a flat colour."""
    rng = np.random.RandomState(seed)
    noise = rng.rand(h // 4 + 1, w // 4 + 1).astype(np.float32)
    grain = np.array(Image.fromarray((noise * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC),
                     dtype=np.float32) / 255.0
    return (grain - 0.5) * amount


def ground(w, h, top, bottom, seed):
    """A vertical gradient with grain in it, which is what every flat surface here is."""
    ramp = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None]

    a = np.zeros((h, w, 4), np.float32)
    for c in range(3):
        a[..., c] = top[c] + (bottom[c] - top[c]) * ramp
    a[..., :3] += speckle(w, h, seed)[..., None]
    a[..., 3] = 255.0

    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the board


def slabs():
    """The mine tileset's own square floor pieces, by number. None when the pack is absent."""
    z = zipped(MINE.name, MINE.parent)
    if z is None:
        return None

    art = {}
    for name in z.namelist():
        if not name.endswith(".png") or "__MACOSX" in name:
            continue

        stem = name.split("/")[-1].replace("Asset ", "").replace("xhdpi.png", "")
        if stem.isdigit():
            art[int(stem)] = read(z, name)

    # The 265-square pieces are the floor; everything taller is a wall block or an ore cluster.
    floor = {k: v for k, v in art.items() if v.size == (265, 265)}
    return floor or None


def floor(pieces, mix, seed, across=4, down=5):
    """One ground, laid from a named mix of the mine's slabs.

    <b>Three grounds in three sittings, and the reason it moved twice is worth keeping.</b> The
    first was drawn - dark earth under a black wash - and read as a hole. The second was the
    top-down pack's grass, which read correctly and was chosen for brightness. This is the owner's
    third call and it is a *place* rather than a brightness: a mine floor, which is what a line of
    turrets standing in front of a jewel field is plausibly defending.

    <b>The plate rule comes back with it</b> (CRAFT.md: a dark ground is what makes bright pieces
    read), so unlike the grass this needs no help - the raiders are saturated cartoon colours and
    every one of them now sits on something that is not competing with it. What it does need is to
    not be *flat*, which is what the mix is for: a different handful of slabs and a different seed
    repave the same stone, so two rungs sharing a tileset do not share a paving pattern.

    **No ore scattered over it.** The pack's ore clusters were laid across the floor to stop it
    reading as flat, drained most of the way to grey so they would not compete with the four
    colours this board spends on things the player has to tell apart. Played, they read as litter.
    """
    tiles = [pieces[k] for k in mix if k in pieces]
    if len(tiles) < 2:
        sys.exit("a ground needs at least two of the mine's slabs; got %r" % (mix,))

    side = 265
    im = Image.new("RGBA", (side * across, side * down), (26, 24, 26, 255))

    rng = np.random.RandomState(seed)
    for y in range(down):
        for x in range(across):
            im.alpha_composite(tiles[int(rng.rand() * len(tiles))], (x * side, y * side))

    return im.resize((512, 640), Image.LANCZOS)


def toned(im, shadow, light):
    """A gradient map: the slab's own luminance read through a two-point ramp.

    <b>A hue rotation cannot do this and that is why it is not used.</b> `hued` turns the wards,
    and it works because the kit paints them - rotating the hue of something that has none leaves
    it exactly as grey as it was, and the mine's stone sits at a chroma of about two. So the colour
    has to be *supplied*, and a ramp from a dark end to a light one is the way that keeps every
    seam, chip and edge the tileset drew: what changes is the material, not the masonry.
    """
    a = np.asarray(im.convert("RGB")).astype(np.float32)
    lum = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11

    low, high = float(lum.min()), float(lum.max())
    t = ((lum - low) / max(high - low, 1.0))[..., None]

    out = np.asarray(shadow, np.float32) + t * (np.asarray(light, np.float32) -
                                                np.asarray(shadow, np.float32))
    rgba = np.dstack([out, np.full(lum.shape, 255.0, np.float32)])
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


def lit(im):
    """A picture's luminance, which is the only thing about a ground that is not negotiable."""
    a = np.asarray(im.convert("RGB")).astype(np.float32)
    return a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11


def depth(im):
    """A little further away at the far end, so the top of the hill reads as distance rather than
    as the same floor twice. The same ramp on all ten, so they cannot disagree about which way the
    hill runs."""
    a = np.asarray(im).astype(np.float32)
    ramp = np.linspace(0.80, 1.10, im.height, dtype=np.float32)[:, None, None]
    a[..., :3] = np.clip(a[..., :3] * ramp + 16.0, 0, 255)
    a[..., 3] = 255.0
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def graded(im, mean, spread, chroma=1.0):
    """Put a ground on the mine floor's own value ladder, and cap what colour it may carry.

    <b>Value is the one thing a second ground may not change.</b> A gradient map picks its own two
    endpoints, so nothing about it keeps a ground where the cast can be seen against it - and this
    hill has already had that fault twice: the second ground it ever carried was the grass pack,
    "chosen for brightness", and the owner's third call moved it to a mine. Every rung is therefore
    normalised onto the mine's own <em>measured</em> mean and spread, so re-cutting the mine moves
    the other nine with it, and the variety a chapter gets is <b>hue and material, never
    brightness</b>.

    The map is affine on luminance, so `depth`'s ramp survives it as a ramp - which is why the
    grade runs last.
    """
    a = np.asarray(im.convert("RGB")).astype(np.float32)
    here = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11

    want = np.clip((here - here.mean()) / max(float(here.std()), 1.0) * spread + mean, 4.0, 255.0)
    cast = a - here[..., None]

    # The ceiling, measured rather than assumed: chroma scales with the factor, so one reading
    # says exactly what factor lands on it. See GROUND_CHROMA_CAP.
    carried = float(np.abs(cast * chroma).mean())
    if carried > GROUND_CHROMA_CAP:
        chroma *= GROUND_CHROMA_CAP / carried

    out = cast * chroma + want[..., None]
    rgba = np.dstack([np.clip(out, 0, 255), np.full(here.shape, 255.0, np.float32)])
    return Image.fromarray(rgba.astype(np.uint8), "RGBA")


def rampart():
    """The wall the wards stand on: stone, with a lit top edge so the line reads as a line.

    The capping course carries a warm light of its own. That is what makes the middle band read as
    *the thing being defended* rather than as a strip of masonry between two halves of the screen -
    the whole fail state of this mode is that edge, and a fail state has to be the second most
    legible thing on the board after the goal.
    """
    w, h = 512, 176
    im = ground(w, h, (74, 80, 94), (34, 38, 48), seed=19)

    d = ImageDraw.Draw(im)

    # Coursed blocks, offset row to row. Drawn rather than tiled, so it stretches without a seam,
    # and dark, so what stands on it wins.
    block = w // 8
    for row in range(3):
        y0 = int(h * (0.26 + row * 0.26))
        y1 = int(h * (0.26 + row * 0.26 + 0.22))
        offset = 0 if row % 2 == 0 else block // 2
        for i in range(-1, 9):
            x0 = i * block + offset
            d.rounded_rectangle([x0 + 4, y0, x0 + block - 4, y1], radius=6,
                                fill=(72 - row * 8, 78 - row * 8, 92 - row * 8, 255))

    # The capping course, last and brightest, because this is the edge the whole run is about.
    d.rectangle([0, 0, w, int(h * 0.15)], fill=(120, 132, 152, 255))
    d.rectangle([0, 0, w, int(h * 0.06)], fill=(168, 178, 196, 255))
    d.rectangle([0, int(h * 0.15), w, int(h * 0.22)], fill=(14, 16, 22, 255))

    im.alpha_composite(glow(w, h, w * 0.5, 0.0, w * 0.60, (255, 176, 96), 0.34, 1.2))

    return im


def plate():
    """The field's own backing: dark, because everything standing on it is a bright jewel."""
    side = 256
    im = Image.new("RGBA", (side, side), (0, 0, 0, 0))

    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, side - 1, side - 1], radius=26, fill=(12, 18, 26, 232))
    d.rounded_rectangle([0, 0, side - 1, side - 1], radius=26, outline=(84, 98, 118, 210), width=4)

    # **No highlight along the top, and that is a lesson about stretching a sprite.** There was a
    # 16-pixel band of white at 6% up there, which is a sheen on a 256-pixel panel and a *bar* on
    # one stretched to six hundred - it sat in the plate's own margin above the first row of gems
    # and read as a second container nobody had asked for. Anything drawn a fixed number of pixels
    # from the edge of a sprite that will be stretched is drawn at a size nobody chose.

    return im


def socket():
    """The plinth a ward stands on - a slab of the rampart's own stone, so a turret reads as
    standing on the line rather than floating over it."""
    w, h = 192, 96
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    d.ellipse([w * 0.04, h * 0.30, w * 0.96, h * 0.96], fill=STONE_DARK + (255,))
    d.ellipse([w * 0.08, h * 0.22, w * 0.92, h * 0.84], fill=STONE_MID + (255,))
    d.ellipse([w * 0.16, h * 0.28, w * 0.84, h * 0.70], fill=STONE_LIT + (255,))

    return im


def hued(im, hue, pull=None, sat_gain=0.48, sat_floor=0.50, val_gain=1.10, val_lift=0.06):
    """Paints a picture one hue, keeping every highlight and every bit of shading it had.

    Saturation is pulled up rather than replaced, so the pack's near-white specular edges stay
    near-white and the body becomes unmistakably one colour; value is lifted a little, because the
    thing this is for is a turret that has to read as *lit* on a bright green field.
    """
    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    span = mx - mn

    sat = np.where(mx > 1e-6, span / np.where(mx > 1e-6, mx, 1.0), 0.0)

    # Pulled up rather than set: a pixel that was grey metal stays greyish, a pixel that was
    # coloured becomes strongly coloured.
    sat = np.clip(sat * sat_gain + sat_floor, 0.0, 1.0)
    val = np.clip(mx * val_gain + val_lift, 0.0, 1.0)

    # **Most of the way to the target rather than all of it.** Setting every pixel to one hue
    # gives a turret that is *entirely* one colour, which came back from play as flat - the pack
    # drew orange trim on a blue body and a purple dome, and all of that collapsed into a single
    # red shape. Blending keeps a fifth of the original spread, so a red ward is unmistakably red
    # and still has warm and cool notes in it. The blend is circular, so a hue two thirds of the
    # way round the wheel takes the short way and not the long one.
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]

    was = np.zeros_like(mx)
    lit = span > 1e-6
    with np.errstate(invalid="ignore", divide="ignore"):
        safe = np.where(lit, span, 1.0)
        was = np.where(lit & (mx == r), ((g - b) / safe) % 6.0, was)
        was = np.where(lit & (mx == g), ((b - r) / safe) + 2.0, was)
        was = np.where(lit & (mx == b), ((r - g) / safe) + 4.0, was)
    was = was / 6.0

    step = ((hue - was + 0.5) % 1.0) - 0.5
    h = (was + step * (HUE_PULL if pull is None else pull)) % 1.0
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = val * (1.0 - sat)
    q = val * (1.0 - f * sat)
    t = val * (1.0 - (1.0 - f) * sat)
    i = i.astype(np.int32) % 6

    out = np.stack([
        np.choose(i, [val, q, p, p, t, val]),
        np.choose(i, [t, val, val, q, p, p]),
        np.choose(i, [p, p, t, val, val, q]),
    ], axis=-1)

    return Image.fromarray(
        (np.concatenate([np.clip(out, 0, 1), alpha], axis=-1) * 255.0 + 0.5).astype(np.uint8),
        "RGBA")


#: The tallest gun of the ladder, in source pixels, measured once so every tier is fitted to the
#: *same* scale. Gun10 is 156 x 192 and Gun06 is 135 x 198; fitting each to its own canvas would
#: draw them at two different scales, so a turret would visibly change size when it went up a rank
#: - which is the fault `paired` exists to stop happening to the warlord, met again one folder over.
TIER_BOX = (156, 200)


def turret(z, model, frame=None):
    """One turret of the upgrade ladder, cut from the kit and centred on a ward-sized canvas.

    <b>No colour is baked in here.</b> `hued` is what paints it; this is the shape.

    **Every tier is fitted to one box**, not to its own extent - see `TIER_BOX`. Its *foot* is
    pinned rather than its middle, because the tiers differ mostly at the top (a second barrel, a
    taller core) and a turret that rose off its plinth when it was upgraded would read as the
    plinth having sunk.
    """
    name = ("Png/Guns/%s/Idle/%s-Idle_0.png" % (model, model) if frame is None
            else "Png/Guns/%s/Shoot/%s-Shoot_%02d.png" % (model, model, frame))

    im = read(z, name)

    out = Image.new("RGBA", (WARD_W, WARD_H), (0, 0, 0, 0))
    ratio = min(WARD_W / TIER_BOX[0], WARD_H / TIER_BOX[1])
    im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))), Image.LANCZOS)

    out.alpha_composite(im, ((WARD_W - im.width) // 2, WARD_H - im.height))
    return out


def cog():
    """The upgrade gem: a machine part standing in a field of jewels.

    **Drawn rather than cut, and that is invariant 32b taken before it costs anything.** Five goals
    in this project have been approximated out of a licensed sheet and every one had to be re-done
    after somebody looked at it. This one has a harder job than most: it stands among four
    hand-drawn jewels and has to read as *not one of them* at a glance, at cell size, while still
    looking like it belongs on the same board. A gear does that by silhouette alone - it is the one
    shape here with teeth - and drawing it is what lets the teeth be big enough to survive being
    forty pixels wide.

    It is deliberately **grey steel with a white core** and wears none of the board's four colours,
    for the warlord's spell's reason (`SiegeView.Spellfire`): a cog takes whichever colour destroys
    it, so a cog that was already a colour would be saying something the rules do not mean.
    """
    side = TILE
    up = 4
    big = side * up
    im = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    mid = big / 2
    teeth = 8
    outer, root, bore = big * 0.455, big * 0.345, big * 0.150

    rim = (214, 222, 236, 255)
    body = (150, 163, 190, 255)
    dark = (74, 84, 110, 255)

    # The teeth first, as wedges around the rim, then the disc over their roots - so a tooth is a
    # tooth rather than a bump, which is the whole reason this shape reads at forty pixels.
    for t in range(teeth):
        a = 2 * math.pi * t / teeth
        wide = math.pi / teeth * 0.52
        pts = []
        for k, (r, off) in enumerate(((outer, -wide), (outer, wide),
                                      (root, wide * 1.9), (root, -wide * 1.9))):
            pts.append((mid + math.cos(a + off) * r, mid + math.sin(a + off) * r))
        d.polygon(pts, fill=dark)
        pts = [(mid + (x - mid) * 0.93, mid + (y - mid) * 0.93) for x, y in pts]
        d.polygon(pts, fill=body)

    d.ellipse([mid - root, mid - root, mid + root, mid + root], fill=dark)
    d.ellipse([mid - root * 0.9, mid - root * 0.9, mid + root * 0.9, mid + root * 0.9], fill=body)
    d.ellipse([mid - root * 0.9, mid - root * 0.9, mid + root * 0.62, mid + root * 0.62], fill=rim)
    d.ellipse([mid - root * 0.66, mid - root * 0.66, mid + root * 0.66, mid + root * 0.66],
              fill=body)

    # The core: a white eye, so the one thing that says "this is worth something" is the brightest
    # thing in the picture and survives being drawn over a dark socket.
    d.ellipse([mid - bore * 1.5, mid - bore * 1.5, mid + bore * 1.5, mid + bore * 1.5], fill=dark)
    d.ellipse([mid - bore, mid - bore, mid + bore, mid + bore], fill=(255, 252, 240, 255))

    im = im.resize((side, side), Image.LANCZOS)

    halo = glow(side, side, side / 2, side / 2, side * 0.44, (255, 250, 226), 0.42)
    halo.alpha_composite(im)
    return halo


def wrecked(z, model):
    """A ward that has fallen: the same turret, grey, dark and leaning.

    Grey rather than gone, because "three of four standing" has to be readable without counting -
    and the silhouette staying put is what makes the gap in the line legible at all.
    """
    im = turret(z, model)

    a = np.asarray(im).astype(np.float32)
    grey = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(grey * 0.55 + 12, 0, 255)

    im = Image.fromarray(a.astype(np.uint8), "RGBA")
    return im.rotate(-13, Image.BICUBIC, expand=False, center=(WARD_W / 2, WARD_H * 0.88))


def bullet(z):
    """What a ward fires: the pack's own round, drained of its colour so it can take the ward's."""
    im = fit(read(z, "Png/Bullets/Bullet1.png"), 96, 0.92)

    a = np.asarray(im).astype(np.float32)
    lit = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(lit * 0.55 + 118, 0, 255)

    return Image.fromarray(a.astype(np.uint8), "RGBA")


def white(im, side, lift=1.0):
    """Drains a frame to white so it can be tinted at run time, and fits it to a square."""
    a = np.asarray(fit(im, side, 0.98)).astype(np.float32)
    lum = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(lum * 0.35 + 190 * lift, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the packs


def blast_frames(z, folder, turns, saturate):
    names = sorted(n for n in z.namelist()
                   if n.startswith("png/%s/" % folder) and n.lower().endswith(".png"))

    frames = [read(z, n) for n in names]
    box = box_of(frames)

    out = []
    for im in frames:
        if box:
            im = im.crop(box)
        out.append(im.resize((TILE, TILE), Image.LANCZOS))
    return out


def ordered(z, folder):
    """Every frame of one animation, **in the order it was drawn**.

    <b>Numerically, and that is a bug fix rather than a nicety.</b> These packs number frames
    without padding - `Walk_0`, `Walk_1`, `Walk_10`, `Walk_2` - so a plain `sorted()` deals
    0, 1, 10, 11 ... 17, 2, 3 and every cast reel this tool has ever written has been a shuffled
    walk cycle. It reads as a jitter rather than as an error, which is why it survived: nothing
    about a scrambled loop looks *broken*, it just looks bad. Nothing numeric could have caught
    it either - the frames are all present, all the right size, and `--check` reproduces a
    scrambled reel exactly as faithfully as a correct one.
    """
    names = [n for n in z.namelist()
             if n.startswith(folder + "/") and n.lower().endswith(".png")]

    def index(name):
        digits = re.findall(r"_(\d+)\.png$", name)
        return (int(digits[0]) if digits else 0, name)

    return sorted(names, key=index)


def spaced(names, count):
    """`count` frames evenly spaced through an animation, or all of them if there are fewer.

    Evenly spaced rather than the first N, or a long walk cycle ships as its first half stride.
    """
    if len(names) <= count:
        return list(names)

    step = len(names) / float(count)
    return [names[min(len(names) - 1, int(i * step))] for i in range(count)]


def cast_frames(z, folder, count=FRAMES, tall=CAST):
    names = spaced(ordered(z, folder), count)
    if not names:
        return []

    frames = [read(z, n) for n in names]

    box = box_of(frames)
    if box is None:
        return []

    frames = [im.crop(box) for im in frames]

    width, height = frames[0].size
    ratio = tall / float(height)
    size = (max(1, int(width * ratio)), tall)

    return [im.resize(size, Image.LANCZOS) for im in frames]


def centroid(im):
    """The alpha-weighted middle of a frame, in its own pixels."""
    a = np.asarray(im)[..., 3].astype(np.float64)
    mass = a.sum()
    if mass <= 0:
        return 0.0, 0.0

    ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    return float((xs * a).sum() / mass), float((ys * a).sum() / mass)


def paired(z, folders, count, tall, overflow=()):
    """Two animations of one character, cut onto **one** canvas so the body cannot jump.

    <b>Why this is not two calls to `cast_frames`.</b> Each animation is exported on a canvas
    cropped to its own extent - this alien's idle is 326 wide and its attack 436, because the
    attack throws a fist a long way out - so trimming each to its own bounding box and fitting
    both into the same square draws the body at two different *sizes*. On screen that is a boss
    that shrinks by a quarter every time it casts and grows back afterwards, which reads as a bug
    in the game rather than as an animation.

    <b>The alignment is exact rather than approximate.</b> The first frame of each animation is
    the same pose, so the two frames hold the same pixels translated - which means the offset
    between the canvases is the difference of their alpha centroids, to the pixel. Measured on the
    shipped pair: (111, 41), against an overlap search that agreed. It is checked rather than
    assumed: if the two first frames do not carry the same mass they are not the same pose, and
    the caller is told rather than handed a silently misaligned reel.
    """
    reels = []
    for folder in folders:
        names = ordered(z, folder)
        if not names:
            return None
        reels.append([read(z, n) for n in names])

    base = centroid(reels[0][0])
    shifts = []

    for frames in reels:
        here = centroid(frames[0])
        shifts.append((int(round(here[0] - base[0])), int(round(here[1] - base[1]))))

        mass = np.asarray(frames[0])[..., 3].sum()
        want = np.asarray(reels[0][0])[..., 3].sum()
        if abs(int(mass) - int(want)) > want * 0.04:
            raise SystemExit("the first frames of %s are not the same pose as %s, so these two "
                             "animations cannot be aligned by centroid" % (folders, folders[0]))

    # Every frame of both animations, expressed in the first animation's own pixel space.
    def extent(over):
        box = None
        for frames, (dx, dy) in over:
            for im in frames:
                bb = im.getbbox()
                if bb is None:
                    continue
                here = (bb[0] - dx, bb[1] - dy, bb[2] - dx, bb[3] - dy)
                box = here if box is None else (min(box[0], here[0]), min(box[1], here[1]),
                                                max(box[2], here[2]), max(box[3], here[3]))
        return box

    kept = [(r, s) for r, s, f in zip(reels, shifts, folders) if f not in overflow]

    home = extent(kept) or extent([(reels[0], shifts[0])])
    whole = extent(list(zip(reels, shifts)))

    if home is None or whole is None:
        return None

    # **Nothing is cut off any more, and the frame is widened symmetrically to manage it.**
    #
    # It used to be *wide as what stays in frame, tall as everything*: the attack was named in
    # `overflow` and its thrown fist simply left the canvas, on the argument that "it threw
    # something" looks like that anyway and that framing the fist would put the body in 60% of a
    # 522-pixel canvas. Played, it came back as parts of a boss visibly cut off mid-animation, and
    # the argument was half wrong: the *view sizes a body by its height* (`SiegeView.Frame`), so a
    # wider canvas costs no size at all. What it really costs is **centring** - the canvas grows on
    # whichever side the fist goes, and the view centres the canvas on the lane, so the body would
    # stand off to one side.
    #
    # So the width is taken as far as the widest thing reaches on *either* side of the body's own
    # centre, and mirrored. The body stays exactly where it was and exactly the size it was; what
    # grows is transparent margin, which is what "expand the container" means.
    #
    # `overflow` therefore no longer decides what is *drawn* - it decides what defines the body's
    # centre, which is still the standing and walking reels, because a fist is not a body.
    #
    # *Tall*: a clipped head is not a throw, it is a mistake. Nothing is ever cut off the top.
    middle = (home[0] + home[2]) / 2.0
    reach = max(middle - whole[0], whole[2] - middle, middle - home[0], home[2] - middle)

    box = (int(math.floor(middle - reach)), whole[1], int(math.ceil(middle + reach)), whole[3])

    # The claim above, asserted rather than believed. It is the one property of this frame that
    # matters and the one nothing downstream could ever notice: a clipped fist imports, addresses,
    # audits and draws, and the only symptom is a boss losing an arm for four frames of a throw
    # that nobody is looking at closely (invariant 32b - no gate in this project opens a PNG).
    # Measured when this replaced the old `overflow` rule: the four bosses were losing 162, 102,
    # 106 and 6 pixels of their attacks.
    if not (box[0] <= whole[0] and box[2] >= whole[2]
            and box[1] <= whole[1] and box[3] >= whole[3]):
        raise SystemExit("%s: the shared canvas does not contain every frame of every reel, so "
                         "something is being cut off" % (folders,))

    wide, high = box[2] - box[0], box[3] - box[1]
    ratio = tall / float(high)
    size = (max(1, int(wide * ratio)), tall)

    out = []
    for frames, (dx, dy) in zip(reels, shifts):
        cut = []
        for im in spaced(frames, count):
            pane = Image.new("RGBA", (wide, high), (0, 0, 0, 0))
            pane.alpha_composite(im, (-box[0] - dx, -box[1] - dy))
            cut.append(pane.resize(size, Image.LANCZOS))
        out.append(cut)

    return out


# --------------------------------------------------------------------------- the drop


def build():
    """Every PNG this mode ships, as {relative path: image}. Empty when the packs are absent."""
    match3, blasts = zipped(MATCH3), zipped(BLASTS)
    turrets, kit = zipped(TURRETS, TOWER), zipped(KIT, TOWER)

    if match3 is None or blasts is None or turrets is None or kit is None:
        return None

    made = {}

    for key, (src, _) in GEMS.items():
        made["Siege/%s.png" % key] = fit(read(match3, src), TILE, 0.88)

    # **Ten grounds, one per place in a chapter** (invariant 7c). The mine's own untouched floor
    # is one of them and is measured first, because the other nine are normalised onto its value
    # ladder rather than onto a pair of typed numbers - see `graded`.
    pieces = slabs()
    if pieces is None:
        return None

    plain = None
    for key, tone, shadow, light, mix, seed in GROUNDS:
        if tone is None:
            plain = depth(floor(pieces, mix, seed))
            break

    if plain is None:
        return None

    light_of = lit(plain)
    mean, spread = float(light_of.mean()), float(light_of.std())

    for key, tone, shadow, light, mix, seed in GROUNDS:
        if tone is None:
            made["Siege/%s.png" % key] = plain
            continue

        made["Siege/%s.png" % key] = graded(
            depth(toned(floor(pieces, mix, seed), shadow, light)), mean, spread)
    made["Siege/rampart.png"] = rampart()
    made["Siege/plate.png"] = plate()
    made["Siege/socket.png"] = socket()

    # **The ward line: five tiers times four colours.** Twenty turrets and twenty recoils, because
    # a ward now carries its rank in its silhouette (see WARD_TIERS) and its colour in its hue.
    # Cut once per pair rather than tinting one reel four times, for `hued`'s reason: a tint is a
    # multiply and can only darken, and these have to read as *lit* on a bright hill.
    for tier, model in enumerate(WARD_TIERS, start=1):
        shots = sorted(n for n in kit.namelist()
                       if n.startswith("Png/Guns/%s/Shoot/" % model) and n.endswith(".png"))

        stand = turret(kit, model)
        step = max(1, len(shots) // FIRE_FRAMES)
        recoil = [turret(kit, model, min(len(shots) - 1, f * step)) for f in range(FIRE_FRAMES)]

        for letter, hue in WARD_HUES:
            made["Siege/ward%d_%s.png" % (tier, letter)] = hued(stand, hue)

            for f, frame in enumerate(recoil):
                made["Siege/fire%d_%s/f%02d.png" % (tier, letter, f)] = hued(frame, hue)

    # A fallen ward is the tier it stood up in, whatever rank it had reached: what the gap in the
    # line has to say is "this one is gone", and five wrecks would say "this one is gone and it was
    # a good one", which is a sentence nobody needs at the moment the line is coming down.
    made["Siege/ward_dead.png"] = wrecked(kit, WARD_TIERS[0])
    made["Siege/bullet.png"] = bullet(turrets)

    # The rank badge, pinned to a turret's shoulder. The kit's own shield, which is the shape its
    # upgrade ladder is drawn with - the number is written on it at run time, because a number
    # drawn into a texture is five textures and one of them will be the wrong colour.
    made["Siege/crest.png"] = fit(read(kit, "Png/User interfaces/game play area Ui/shield icon.png"),
                                  128, 0.96)

    # The cog: the one thing on the field that is not a jewel.
    made["Siege/gem_cog.png"] = cog()

    # The muzzle flash, drained to white so a ward's own colour can be put on it at run time.
    flashes = sorted(n for n in turrets.namelist()
                     if n.startswith("Png/Shoot Fx/") and n.endswith(".png"))
    for i, name in enumerate(flashes[:10]):
        made["Fx/Siege/flash/f%02d.png" % i] = white(read(turrets, name), TILE)

    # What a matched gem comes apart into: a ring of debris, white, tinted per gem at run time.
    # **Cut white on purpose.** Four painted copies would be eighty textures and four chances for
    # one of them to stop matching `Pal`; one white reel is twenty and cannot drift.
    pops = sorted(n for n in kit.namelist()
                  if n.startswith("Png/Dead fx/") and n.endswith(".png"))
    step = max(1, len(pops) // FRAMES)
    for i in range(FRAMES):
        made["Fx/Siege/pop/f%02d.png" % i] = white(read(kit, pops[min(len(pops) - 1, i * step)]),
                                                   TILE, 1.15)

    for key, (folder, turns, saturate) in BLAST_SET.items():
        for i, im in enumerate(blast_frames(blasts, folder, turns, saturate)):
            made["Fx/Siege/%s/f%02d.png" % (key, i)] = im

    packs = {}

    # **Every raider, hue-rotated into its own colour.** `CAST_HUE` is a gentler grade than the
    # wards get: a turret has to read as *lit* on a bright green field, where a monster has to keep
    # its own face - so saturation is pushed less far and the value is not lifted at all. Both go
    # through one function, because two copies of a hue rotation is two ways for a red raider and a
    # red bolt to disagree about what red is.
    hues = dict(WARD_HUES)

    for key, (pack, folder) in RAIDER_SET.items():
        if pack not in packs:
            packs[pack] = zipped(pack)
        z = packs[pack]
        if z is None:
            continue

        hue = hues.get(key[-1])
        for i, im in enumerate(cast_frames(z, folder)):
            made["Siege/%s/f%02d.png" % (key, i)] = im if hue is None else hued(
                im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=CAST_SAT_FLOOR,
                val_gain=CAST_VAL_GAIN, val_lift=CAST_VAL_LIFT)

    # The four bosses: three reels each, all off one canvas so none of them jumps or changes size
    # when it throws. One loop rather than one block per boss, which is what stopped a third and a
    # fourth being expensive - and what makes the *set* something a reader can see at once.
    for key, (pack, folders, tall, overflow) in BOSS_SET.items():
        if pack not in packs:
            packs[pack] = zipped(pack)

        source = packs[pack]
        if source is None:
            continue

        reels = paired(source, folders, max(BOSS_FRAMES, BOSS_CAST_FRAMES), tall,
                       overflow=overflow)
        if reels is None:
            continue

        for name, frames, want in ((key, reels[0], BOSS_FRAMES),
                                   (key + "_walk", reels[1], BOSS_FRAMES),
                                   (key + "_cast", reels[2], BOSS_CAST_FRAMES)):
            for i, im in enumerate(spaced(frames, want)):
                made["Siege/%s/f%02d.png" % (name, i)] = im

    return made


def raw(im):
    buffer = io.BytesIO()
    im.save(buffer, "PNG", optimize=False)
    return buffer.getvalue()


def path_of(rel):
    return REPO / "Assets" / "Game" / "Art" / rel


def write(made):
    for rel, im in sorted(made.items()):
        target = path_of(rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw(im))
    print("wrote %d files under Assets/Game/Art/Siege and Art/Fx/Siege" % len(made))


def check(made):
    missing, differ = [], []

    for rel, im in sorted(made.items()):
        target = path_of(rel)
        if not target.exists():
            missing.append(rel)
        elif target.read_bytes() != raw(im):
            differ.append(rel)

    if missing or differ:
        for rel in missing:
            print("missing: %s" % rel)
        for rel in differ:
            print("differs: %s" % rel)
        sys.exit("%d missing, %d differ - re-run with --write" % (len(missing), len(differ)))

    print("%d files are what this tool cuts" % len(made))


def contact(made):
    """A sheet at the size a phone really draws these. A check proves reproducibility and says
    nothing about quality. Look at it."""
    board = [k for k in sorted(made) if k.startswith("Siege/") and k.count("/") == 1]
    reels = sorted({k.split("/")[1] for k in made if k.startswith("Siege/") and k.count("/") == 2})
    booms = sorted({k.split("/")[2] for k in made if k.startswith("Fx/Siege/")})

    cell = 132
    cols = max(len(board), 9)
    rows = 1 + len(reels) + len(booms)

    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 18) + 24), (16, 22, 30, 255))
    draw = ImageDraw.Draw(sheet)

    def put(im, col, row, label):
        thumb = im.copy()
        thumb.thumbnail((cell - 10, cell - 10), Image.LANCZOS)
        sheet.alpha_composite(thumb, (col * cell + (cell - thumb.width) // 2,
                                      row * (cell + 18) + (cell - thumb.height) // 2))
        if label:
            draw.text((col * cell + 3, row * (cell + 18) + cell + 2), label[:18],
                      fill=(225, 225, 225, 255))

    for i, key in enumerate(board):
        put(made[key], i % cols, i // cols, key.split("/")[-1][:-4])

    row = (len(board) - 1) // cols + 1
    for name in reels:
        frames = sorted(k for k in made if k.startswith("Siege/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    for name in booms:
        frames = sorted(k for k in made if k.startswith("Fx/Siege/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    out = REPO / "Tools" / "siege_contact.png"
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    global SOURCE, TOWER, MINE

    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--source", default=str(SOURCE))
    ap.add_argument("--tower", default=str(TOWER))
    ap.add_argument("--mine", default=str(MINE))
    args = ap.parse_args()

    SOURCE = Path(args.source)
    TOWER = Path(args.tower)
    MINE = Path(args.mine)

    made = build()

    if made is None:
        # Passing when the packs are absent is deliberate: a checkout without them still runs the
        # gate, exactly as `make_ember_art.py` and `make_village_art.py` do.
        print("source packs not found at %s - nothing to do" % SOURCE)
        return

    if args.write:
        write(made)
    if args.contact:
        contact(made)
    if args.check or not (args.write or args.contact):
        check(made)


if __name__ == "__main__":
    main()
