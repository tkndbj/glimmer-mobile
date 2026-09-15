# -*- coding: utf-8 -*-
"""Which map a chapter draws, and which sky each of its levels does. One rule, once.

A chapter's art used to be a decision per chapter: pick a source pack, add a row to
`chapter_art.tsv`, cut a map and ten backdrops, name them after the chapter. Four glade
chapters and five others did that nine different ways, and the result read as several
games - `f02_glasswater` drew `c03_amberwood`'s painting graded blue, `f03_whorlwater`
drew `c02_millvale`'s graded orange, and every Lightfall, Groovekeeper and Budburst level
sat on **one** picture for the whole chapter.

Now it is arithmetic:

  * **The map is a function of a chapter's ordinal inside its own mode.** Every mode's
    first chapter draws `map1`, every mode's second draws `map2`. A mode is told apart on
    the map by its **perch** alone - the floating tile a node stands on, `ModeLook.Perch` -
    which is the rule that was already written down there and is now true of the art as
    well.
  * **The sky is a function of that ordinal and the level's place in the chapter.** Forty
    skies, ten per ordinal, all the same cloud painting at forty different colours
    (`Tools/make_sky_art.py`).

What that buys, and it is the reason to keep it: **a chapter published next year needs no
art at all.** It names an ordinal and gets a map and ten skies. Nothing to cut, no row to
add, no name to invent, and no way for two chapters of two modes to disagree about what the
second chapter of the game looks like.

The ordinal is 1-based and is the chapter's position **within its mode**, which is what the
manifest's `order` already says - glade `c01..c04` are 1..4, Lightfall `f01..f03` are 1..3,
and `k01`/`b01` are both 1. It is written into each generator rather than derived here
because a chapter body carries no opinion about its own position (that is the manifest's
job, `ChapterIndexEntry`), and this file writes bodies.
"""

#: How many strips each ordinal's map is cut into. A fact about the painting rather than a
#: preference: `make_chapter_art.py` scales a source to *whole* strips, so the same picture
#: at six strips instead of four is 1.5x zoomed and loses 40% of its width off the sides.
#: These are the counts the four paintings were cut at and look right at.
#:
#: A chapter's map height is its strip count x 1200 canvas units, and `mapX`/`mapY` are
#: fractions of it - so changing one of these numbers changes every distance on the maps of
#: every chapter at that ordinal. `ChapterMapValidator` is what proves the nodes still
#: clear each other afterwards.
STRIPS = {1: 6, 2: 4, 3: 5, 4: 6}

#: Skies per chapter. Every chapter shipped so far has exactly ten levels; a chapter with
#: more wraps round inside its own block rather than borrowing the next ordinal's, so two
#: chapters of two modes at the same ordinal can never disagree.
PER_CHAPTER = 10

#: How many blocks of skies exist, which is `make_sky_art.COUNT / PER_CHAPTER`. A fifth
#: chapter wraps to the first block; that is forty levels away from the chapter it repeats,
#: which is cheaper than cutting art nobody asked for.
BLOCKS = 4


#: Which side of the map each rung of a chapter **prefers**, and nothing more.
#:
#: This used to be half of a table that decided where every node in the game stood: odd glades
#: on the right, even on the left, at ten heights spaced down the map. It was the right shape
#: for a node standing on a **perch** - a floating island tile with a shadow under it, which
#: brings its own ground and so does not care what the painting has underneath. It is the
#: wrong shape for a node standing on the painting, and every one of the four paintings said
#: so: the serpentine put glades in a river, on a rooftop, in a lake and off a cliff.
#:
#: So where a node stands is now read off the picture (`SEATS`), and this survives as what the
#: search aims at when the ground gives it a choice - which is most of the time, because all
#: four paintings wind from side to side anyway.
XS = (0.70, 0.28, 0.74, 0.30, 0.72, 0.26, 0.70, 0.32, 0.74, 0.28)

#: Where a chapter's ten nodes stand on each map, as fractions of that map.
#:
#: **Generated, never typed** - `python Tools/make_map_seats.py --write` finds the ground each
#: painting draws and seats the chain on it, and `--check` proves this table is still what it
#: finds. One layout per *map* rather than per chapter, which is the same bargain the art
#: itself makes (above): every mode's first chapter draws `map1`, so every mode's first
#: chapter stands its glades in the same ten places, and a chapter published next year costs
#: no art and no coordinates.
#:
#: Keyed by map rather than by strip count, which is the one thing that changed shape here.
#: The old table was keyed by strip count because the spacing was arithmetic and only the
#: height of the map could affect it; these are facts about four particular pictures, and two
#: paintings cut into six strips have nothing else in common.
SEATS = {
    1: (
        (0.325, 0.017, True),
        (0.586, 0.119, True),
        (0.781, 0.194, True),
        (0.659, 0.291, True),
        (0.658, 0.409, False),
        (0.611, 0.489, False),
        (0.700, 0.608, True),
        (0.284, 0.716, False),
        (0.578, 0.790, False),
        (0.689, 0.868, False),
    ),
    2: (
        (0.700, 0.061, False),
        (0.280, 0.143, False),
        (0.740, 0.218, False),
        (0.207, 0.294, False),
        (0.620, 0.372, False),
        (0.260, 0.472, False),
        (0.790, 0.549, False),
        (0.401, 0.627, False),
        (0.740, 0.739, False),
        (0.280, 0.819, False),
    ),
    3: (
        (0.700, 0.076, False),
        (0.281, 0.181, False),
        (0.740, 0.258, False),
        (0.209, 0.334, False),
        (0.720, 0.392, False),
        (0.195, 0.494, False),
        (0.700, 0.560, False),
        (0.224, 0.644, False),
        (0.796, 0.744, False),
        (0.280, 0.825, False),
    ),
    4: (
        (0.375, 0.055, False),
        (0.428, 0.166, False),
        (0.655, 0.249, False),
        (0.520, 0.332, False),
        (0.380, 0.453, False),
        (0.450, 0.603, False),
        (0.680, 0.678, False),
        (0.290, 0.750, False),
        (0.732, 0.806, False),
        (0.356, 0.833, False),
    ),
}

#: Where the end-of-chapter marker stands on each map, found the same way.
MARKERS = {
    1: (0.333, 0.903, True),
    2: (0.660, 0.854, False),
    3: (0.826, 0.883, False),
    4: (0.712, 0.903, False),
}


def ordinal_of(chapter_id, mode_chapters):
    """1-based position of a chapter inside its own mode, given that mode's ids in order."""
    return list(mode_chapters).index(chapter_id) + 1


def strips(ordinal):
    """The map strips a chapter at this ordinal draws, bottom to top."""
    which = map_of(ordinal)
    return ["map%d_strip%d" % (which, i) for i in range(STRIPS[which])]


def map_of(ordinal):
    """Which of the four paintings a chapter at this ordinal draws."""
    return (ordinal - 1) % len(STRIPS) + 1


def places(ordinal):
    """
    Where a chapter at this ordinal stands its ten nodes, in play order.

    Each is `(mapX, mapY, afloat)`. **`afloat` is the one thing a seat carries that is not a
    coordinate**: `map1` is an archipelago and half its chain stands on the current the
    painting draws between the islands, so those nodes get a floating tile back under them
    while the ones on its island paths do not. It is false on every road map.
    """
    return list(SEATS[map_of(ordinal)])


def marker(ordinal):
    """
    Where the end-of-chapter marker stands, as a chapter's `teaserX`.

    Only the x is ours: the marker's height is derived by `ChapterMap.TeaserPosition` from the
    highest glade, and `ChapterDefinition` gives a chapter no way to author it. So this is the
    one number a body carries about it, and `make_map_seats.py` finds it at exactly the height
    the game will draw it at.
    """
    return MARKERS[map_of(ordinal)][0]


def marker_afloat(ordinal):
    """Whether the end-of-chapter marker stands on a floating tile. See `places`."""
    return MARKERS[map_of(ordinal)][2]


#: The worlds a mode can be set in, and the family of backdrops each one draws.
#:
#: **One axis added to the rule above, and it belongs to the mode rather than to the
#: chapter.** Every sky in this game is the same cloud painting at forty colours, which is
#: exactly right while every mode is set in the same forest and exactly wrong the first time
#: one is not: a cloud sky behind a raided village at night is not a grading problem, it is the
#: wrong room. So a mode names a world, a world names a family, and the arithmetic below is
#: unchanged - same ordinal, same level index, same forty. Nothing about any existing chapter
#: moves, and a second Nova Raid chapter still costs no art (`Tools/make_village_art.py`).
#:
#: The map is deliberately **not** part of this. Invariant 7c says a mode is told apart on
#: the map by its perch and by nothing else, and that is still true: every mode of every
#: world draws `map1` for its first chapter.
WORLDS = {
    "grove": "sky",
    "village": "village",
}

#: Which world each mode is set in. Absent means the grove, so every mode that shipped
#: before there was a second world keeps its art with its entry unwritten.
MODE_WORLD = {
}

# `quarry`, then `march` and `ember`, were the entries here; all three modes are retired. A
# retired mode id is not re-pointed and is not kept: nothing loads a chapter naming one
# (invariant 20), so an entry for it would be a line that can never be read. The village
# backdrops themselves stay, because they are a *world* and not a mode - the next mode set
# outside the grove costs one line here and no art.


def world_of(mode):
    """The world a mode is set in. `mode` may be None or '' for the classic glade."""
    return MODE_WORLD.get(mode or "glade", "grove")


def sky(ordinal, level_index, mode=None):
    """The backdrop one level draws."""
    family = WORLDS[world_of(mode)]
    block = (ordinal - 1) % BLOCKS
    return "%s_%02d" % (family, block * PER_CHAPTER + level_index % PER_CHAPTER)


def skies(ordinal, count=PER_CHAPTER, mode=None):
    """The backdrops a chapter's levels draw, in play order."""
    return [sky(ordinal, i, mode) for i in range(count)]
