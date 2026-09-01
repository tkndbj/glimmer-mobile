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


def ordinal_of(chapter_id, mode_chapters):
    """1-based position of a chapter inside its own mode, given that mode's ids in order."""
    return list(mode_chapters).index(chapter_id) + 1


def strips(ordinal):
    """The map strips a chapter at this ordinal draws, bottom to top."""
    which = (ordinal - 1) % len(STRIPS) + 1
    return ["map%d_strip%d" % (which, i) for i in range(STRIPS[which])]


def sky(ordinal, level_index):
    """The backdrop one level draws."""
    block = (ordinal - 1) % BLOCKS
    return "sky_%02d" % (block * PER_CHAPTER + level_index % PER_CHAPTER)


def skies(ordinal, count=PER_CHAPTER):
    """The backdrops a chapter's levels draw, in play order."""
    return [sky(ordinal, i) for i in range(count)]
