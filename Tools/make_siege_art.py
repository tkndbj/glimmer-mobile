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

#: The third folder, added when the bosses stopped being insects and the second chapter got a cast
#: of its own. A third root rather than a copy of the zip into one of the other two, for the reason
#: the second one exists: **copying a licensed pack so that one path works is a second copy nothing
#: keeps in step**, and the day the owner re-downloads it only one of them moves. `--enemies` moves
#: this one.
ENEMIES = Path(r"C:\Users\Digikey\Downloads\topdownenemies")

#: The fourth folder: a hundred RPG skill icons, of which this mode uses exactly one.
#:
#: **A fourth root rather than a copy, for the reason the second and third exist** - copying a
#: licensed pack so one path works is a second copy nothing keeps in step. `--icons` moves it, and
#: the tool passes when it is absent because the one PNG it cuts is committed.
ICONS = Path(r"C:\Users\Digikey\Downloads\craftpix-net-629015-100-skill-icons-pack-for-rpg")

#: Which of the hundred, and it is a decision a picture made.
#:
#: **The glyph sits on a turret's chassis at about half a cell, and at that size detail is mush.**
#: Six candidates were cut and laid over all four ward colours (`Tools/charge_pick.png`): the busy
#: ones - crackling fields, a fist, a burst - all collapsed into coloured noise, and the one that
#: survived is the one with a *silhouette*, a single gold bolt. Its dark ground is what makes it
#: read on red, green, blue and orange alike, which is the whole job: the ward under it is already
#: one of the four.
CHARGE_ICON = 70

MATCH3 = "craftpix-net-298179-match-3-game-asset-set.zip"
BLASTS = "craftpix-517297-explosions-sprite.zip"
#: Where every enemy in this mode comes from, and the whole of what it may draw from.
#:
#: <b>One pack, because it is the only one on this machine that draws insects at all.</b> The hill
#: used to be cast out of nine monster, alien and robot packs - eighty-three side-view cartoon
#: characters - and the owner's call is that the raid is insects and nothing else. Surveyed, those
#: nine packs hold ninety-six characters and not one beetle, fly, ant or wasp between them; the
#: idle-defence kit holds fifteen, drawn <b>top-down</b>, which is also the one view this board has
#: (the ground is a top-down tileset and the hill is looked down on, so the old side-view cast was
#: a mismatch nobody had named).
#:
#: <b>Fifteen bodies is the budget and it decided three things below.</b> A chapter draws twelve
#: raiders and the bosses take four, which is sixteen - so the cast is <b>one set rather than two</b>
#: (`SiegeMode.CastSets`), and one body is worn twice on purpose (see `BOSS_SET`). A second insect
#: pack would buy a second set for the price of one table here and no code, which is exactly what
#: that seam was built for.
#:
#: <b>It is `MERGE` - the same zip the twenty turrets come out of</b>, which is not a coincidence
#: worth hiding: one hand drew the machines and the things they shoot at, so the hill reads as one
#: game. There is deliberately no second name for it here, because two constants holding one file
#: name is two things a `--source` move can leave disagreeing.
#:
#: Where each family lives inside it. Three folders, and the split is the pack's own.
CRAWLERS = "Merge Turrets/Png/Enemies/Grounds Enemy/"
FLIERS = "Merge Turrets/Png/Enemies/Fly Enemy/"
HYBRIDS = "Merge Turrets/Png/Enemies/Hybrid Enemy/"

#: The turrets, their bullets and their muzzle flash.
TURRETS = "craftpix-net-715522-turrets-asset-pack-for-merge-shooter.zip"

#: The top-down tower-defence pack, for its grass. It is the only ground in any of these packs
#: drawn to be looked down on, which is what this hill is.
FIELD = "craftpix-net-869102-tower-defense-neighborhood-top-down-2d-asset-pack.zip"

#: The merge-shooter kit, for one thing: a white ring of debris that tints to anything, which is
#: what a matched gem comes apart into.
KIT = "craftpix-net-239749-merge-shooter-cartoon-asset-kit.zip"

#: The idle-defence kit, which is where the twenty turrets a player chooses between come from, and
#: where both insects come from.
#:
#: **One pack for the whole roster, and that is the point rather than a convenience.** A line holds
#: four turrets side by side and the player is choosing between twenty of them on one shelf, so the
#: set has to read as a set: twenty machines drawn by one hand, all facing up the way this board's
#: hill runs, with a silhouette ladder built into them (one barrel, then two, then four, then a
#: heavy mount). Twenty turrets assembled from four packs would be four games on one plinth.
MERGE = "craftpix-net-206389-free-idle-turret-defense-asset-kit.zip"

#: The top-down monster pack: **five bosses and five small monsters**, and the only pack on this
#: machine that draws a body big enough and odd enough to read as a boss from above.
#:
#: <b>It is what took the bosses off the insect roster.</b> The four bosses were four insects out of
#: the same fifteen the raiders come from, which made a boss a creeper drawn three times the size -
#: invariant 37z's complaint ("two bosses separated by a hue read as one boss") arriving one level
#: up, where the thing being compared is not two bosses but a boss and the wave it walks in front
#: of. The five here are horned, armed, crowned and helmed: they project sideways, which is the one
#: thing 37ar says a silhouette has to do, and none of them can be mistaken for a beetle.
#:
#: <b>And its five monsters are what buys the second cast set back.</b> 37ar recorded that a second
#: pack would cost "one table in `RAIDER_SET`, twelve rows in `SiegeMode`, and no code" - this is
#: that, and the prediction held. Five here plus the ten in `KIT` is fifteen bodies for twelve
#: slots, so the second chapter's cast needs no body worn twice.
MONSTERS = "craftpix-net-377044-top-down-monsters-asset-pack-for-merge-shooter.zip"

#: Where the two families live inside it. The pack spells "Bos", not "Boss".
BROOD = "Png/Monster%02d"
WARLORDS = "Png/Bos%02d"

#: The ten small monsters in `KIT`, which is the same zip the gem debris comes from. They are the
#: other half of the second chapter's cast and they are drawn by the same hand as `MONSTERS` -
#: the two packs are one family, which is what lets a chapter mix them without reading as two games.
KIT_BROOD = "Png/Monster/Monster%02d"

#: Where the number plate hangs, measured.
#:
#: **Every turret in the pack carries a baked level number on a plate under its feet**, which is
#: what the kit was drawn for and is exactly wrong here: a ward's number is its *rank*, drawn at run
#: time on the crest at its shoulder, and two numbers on one turret is two readouts nobody can read
#: (invariant 37y, said about a badge). The plate is at the same place on all twenty - the sprites
#: are one 500x500 layout - so it is a straight crop rather than a detection, and the cut edge is
#: hidden by the plinth the view already draws under every ward.
WARD_PLATE = 372

#: The top-down tile sheets the hill is floored from. Their own download rather than one of the
#: folders above, so `--tiles` points at it.
#:
#: Eight sheets of thirty square tiles each: stone, sandstone, ice, lava, granite, grass, cobble
#: and sand.

#: How the sheets are laid out. Every one of them is a six by five grid of one tile, with a
#: transparent margin around it on three of the eight - so the cut is taken from the sheet's own
#: alpha box rather than from its file size, and the tiles come out slightly rectangular because
#: they are (256 by 205 on the largest sheet). That is fine and is not corrected: a floor is laid
#: from them and resized whole, so what the proportion decides is the paving pattern rather than
#: the shape of anything the player reads.

#: The ground each rung of a siege chapter is fought over: which sheet it is paved from, which of
#: that sheet's thirty tiles are used, and the seed that lays them.
#:
#: <b>Which ground is arithmetic on the level's place in its chapter</b> - invariant 7c's rule and
#: the backdrop's shape exactly, so ten grounds serve every siege chapter that ever ships and a
#: second one costs no art at all. It is <em>not</em> rolled at run time, and that is the one place
#: this reads differently from how it was asked for: a chapter preloads exactly one ground per rung
#: (`SiegeMode.ArtFor`) and two switches in two assemblies have to agree about which
#: (`SiegeGroundTests`), so a floor chosen when the board opens is either ten grounds resident for
#: ever or a white rectangle over the whole hill (invariant 7b). What varies is the sheet, the tile
#: mix and the seed, which is the variety that was actually wanted.
#:
#: <b>Eight surfaces rather than one regraded ten ways, which is the cost this pays off.</b> The
#: ten grounds used to be the mine tileset's own grey stone read through ten gradient maps, and
#: that entry said so out loud: every rung was stone, cut the same way, and "nine more top-down
#: tilesets would replace `TONES` with nine `zipped` calls and change nothing else". These are
#: those tilesets. So there is no gradient map here at all - each sheet is already a material, and
#: supplying a colour to a surface that has one is how you get ten versions of the same place.
#:
#: <b>Two sheets are used twice, because there are eight of them and ten rungs.</b> The repeats sit
#: seven rungs from their twin and are paved from different tiles with a different seed, and
#: <b>no two rungs running share a material</b> - which is the half that matters, because what a
#: player can compare is the rung they are on against the one they just left.
#:
#: <b>The ramp is a place rather than a difficulty</b> - the raid reads as moving somewhere over ten
#: rungs: out of the grass, onto worked stone, through sand and cold, and down into the lava, which
#: is <b>the finale and the only rung that wears it</b>. Rung five keeps the plainest floor of the

import make_siege_ground as ground_art

#: The value ladder every ground is put on, and the cast's own ladder is what decides it.
#:
#: <b>A floor must be darker than what walks on it, and for two chapters this one was brighter.</b>
#: `CAST_VALUE` holds every raider, boss and beetle at a mean of 118; this stood at <b>147</b>, so
#: the hill was a third brighter than the monsters crossing it. That is CRAFT.md's plate rule
#: exactly inverted - and inverted <em>while citing it</em>, because the number was recorded as
#: "the value the cast was judged against" when what it had actually been measured off was one
#: tileset's own brightness. Nothing could see it: every gate here reads the model, a ground is
#: individually well-composed at any mean, and the two numbers live in different sections of one
#: file and had never been read side by side. What did see it is the owner, who said the tiles were
#: too bright.
#:
#: <b>Seventy-eight, which is two thirds of the cast.</b> Far enough below that a bright cartoon
#: raider reads as lit rather than as a silhouette, far enough above black that the rock still
#: shows the structure composed into it - `make_siege_ground` lays a chasm and a stratum and a
#: rockfall, and a floor normalised to near-black would deliver none of them. <b>The rule is the
#: relation, not the number</b>: if `CAST_VALUE` ever moves, this moves with it.
GROUND_MEAN, GROUND_SPREAD = 78.0, 30.0

#: How much of a ground's own colour survives being put on that ladder.
#:
#: <b>Above one, and the reason is arithmetic rather than taste.</b> `graded` maps luminance
#: affinely and leaves the colour offset alone - so lifting a dark tileset from a mean of 84 to
#: 147 divides its apparent saturation by the same 1.75, and what comes out is pastel. Putting the
#: colour back is a multiply on the offset, and 1.5 is where the mine reads as terracotta, slate
#: and violet rather than as a wash of each.
#:
#: <b>The ceiling above it is the one that matters</b>: the red floor carries nearly four times
#: the colour the grey one does, and pushed further it stops being terracotta and becomes
#: *orange* - which is `Pal.Amber`, one of the four colours this whole mode is told apart by
#: (37f). So the cap binds on that one material and touches none of the others.

#: The most colour a ground may carry, as its mean distance from grey.
#:
#: <b>A gradient map has no natural ceiling, and the two ends of the ramp decide the saturation as
#: much as the hue.</b> Left alone, the moss and the ember came out at twice everything else's
#: chroma with their value identical to it, which reads as *brighter* - and a saturated ground is
#: the one thing on this board competing with the cast walking over it, which is 37f's argument
#: said about the floor instead. Twelve is unmistakably a colour and does not fight four
#: cartoon-bright monsters for it.
GROUND_CHROMA_CAP = 24.0

GROUND_CHROMA = 1.5

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

#: Every raider that walks down the hill: one body per colour, per kind.
#:
#: <b>Every one of them is an insect, and the roster is the pack's own.</b> Fifteen bodies in three
#: families the pack already separates - eight beetles that crawl, three flies that hover, four
#: hybrids that do both - and what makes them a <em>set</em> is that one hand drew all fifteen,
#: which is the argument `MERGE` already makes about twenty turrets standing side by side.
#:
#: <b>The hue is baked and the body is the second reading.</b> A raider is hue-rotated into its
#: colour (`hued`, `CAST_PULL`), so which colour a source beetle happens to be painted decides
#: nothing - what a body is picked for is its <em>silhouette</em>, so a player who cannot separate
#: red from green can still separate a smooth shell from a horned one.
#:
#: <b>Three families, and the kinds are cut across them by weight rather than by folder.</b> A
#: creeper is the thing that comes in swarms, so it is light: two flies with their wings out and
#: the two smoothest beetles. A brute is heavier, so it is the four beetles that carry horns,
#: spikes or a fan. A bulwark is the one carrying its armour, which for an insect is not a held
#: shield but a <em>shell</em> - so it is the four hybrids, the only bodies here drawn as a hard
#: domed carapace with the legs tucked under it.
#:
#: <b>One set, not two, and that is the fifteen-body budget rather than a change of mind.</b> The
#: second set was twelve monsters from four packs; there is no second insect pack on this machine,
#: and twelve more distinct insects do not exist to cut. `SiegeMode.CastSets` is 1 and the seam is
#: unchanged - a second pack buys a second set for one table here and no code.
RAIDER_SET = {
    # creepers - light and plain: two fliers and the two smoothest shells
    "mon_r":     FLIERS + "FlyingBlue",
    "mon_g":     FLIERS + "FlyingOrange",
    "mon_b":     CRAWLERS + "BlackBeetle",
    "mon_y":     CRAWLERS + "BlueBeetle",

    # brutes - the four beetles that carry something on their backs
    "brute_r":   CRAWLERS + "RedBeetle",
    "brute_g":   CRAWLERS + "PurpleBeetle",
    "brute_b":   CRAWLERS + "DarkBeetle",
    "brute_y":   CRAWLERS + "OrangeBeetle",

    # bulwarks - the hard domed shell, which is the only honest way an insect carries plating.
    # **A hybrid's animations are in subfolders of its own**, where a beetle's are loose in its
    # folder - so every entry here is one whole path rather than a pair, which is also what keeps
    # the file names out of it: this pack ships `HybridBlue/Flying/HybridPur-Flying_10.png`, so a
    # body identified by its file name would be the wrong insect.
    "bulwark_r": HYBRIDS + "HybridRed/Move",
    "bulwark_g": HYBRIDS + "HybridGreen/Move",
    "bulwark_b": HYBRIDS + "HybridBlue/Move",
    "bulwark_y": HYBRIDS + "HybridPur/Move",
}

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
#: The hues are `Pal.Poppy`, `Pal.Mint`, `Pal.Azure` and `Pal.Amber` measured as hue angles, so the
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

#: The twenty turrets a player chooses between, in `WardCatalog.Default` order.
#:
#: **The id is the contract and the model is the picture.** The id is what the save keys on and what
#: `WardModel.ArtFor` builds an address from, so this table and `WardCatalog.Default` have to name
#: the same twenty things - which `Tools/verify/content.py` proves rather than trusting, because a
#: turret whose picture is missing draws as a white rectangle two cells tall (invariant 7b) on the
#: one object a player is looking at for a whole run.
#:
#: **The hull is the shelf rung, and that is a rule rather than a table somebody keeps in step.**
#: T1 is a single barrel on a plain hull and T20 is a four-barrel heavy mount, so a shelf ordered
#: cheapest-first climbs visibly from the free turret to the dearest. It was hand-paired once and
#: drifted the moment the shelf was re-rung - `apex`, the dearest turret in the game, was wearing
#: T2. **Re-rung the shelf, re-cut this list in the same order.**
WARD_MODELS = (
    ("bolt",        "T1"),
    ("siphon",      "T2"),
    ("beacon",      "T3"),
    ("ember",       "T4"),
    ("rime",        "T5"),
    ("cleaver",     "T6"),
    ("prism",       "T7"),
    ("lance",       "T8"),
    ("mortar",      "T9"),
    ("spark",       "T10"),
    ("leech",       "T11"),
    ("lighthouse",  "T12"),
    ("pyre",        "T13"),
    ("glacier",     "T14"),
    ("breaker",     "T15"),
    ("spectrum",    "T16"),
    ("harpoon",     "T17"),
    ("howitzer",    "T18"),
    ("arcstorm",    "T19"),
    ("apex",        "T20"),
)

#: How tall a turret's thumbnail is cut for the loadout shelf.
#:
#: **A grid cell draws one at about 150 points against art cut at 500**, so browsing the roster
#: reads thumbnails rather than the real turrets - invariant 16c's rule, which is also what keeps
#: the shelf's memory bounded by the shelf rather than by the roster. One picture per model rather
#: than one per model per colour, because on the shelf the *colour* is the slot the player is
#: filling and the model is what they are choosing.
THUMB = 176

#: The two insects, and the two things that separate them.
#:
#: **A weaver crawls and a thief flies**, which is the only thing a player has to read about them
#: from across the board: one of them is on the ground taking the field away a cell at a time and
#: the other is hovering over it stealing whole gems. The pack draws both, so it costs a folder
#: name rather than a decision.
#:
#: **Beetles for the weaver**, because a thing that spins webs over a board should look like it
#: could - and because eight of them ship, which is two colours more than this needs and room for a
#: second set later. **Hybrids for the thief**, whose `Flying` reel is a hover rather than a walk:
#: it is the one raider in this mode that stops and stays put over the hill, and a walk cycle
#: looping under something standing still is the fault invariant 37u names in one word (floating).
WEAVER_SET = ("RedBeetle", "GreenBeetle", "BlueBeetle", "OrangeBeetle")
THIEF_SET = ("HybridRed", "HybridGreen", "HybridBlue", "HybridPur")

#: How tall the two insects are cut. Between a creeper and a boss: they hold the middle of the hill
#: and have to be seen to be answered, and they are not the fight.
INSECT = 210

#: Which colour each ward is painted, as a hue angle. `Pal.Poppy`, `Pal.Mint`, `Pal.Azure` and
#: `Pal.Amber`, measured - so the line still agrees with the gems that feed it.
#:
#: **The fourth was `Pal.Sun` and the gem is what moved it.** Three of these sit within about
#: eight degrees of the jewel they are matched on; the fourth sat at 43 degrees against a gem cut
#: at 34, so the one colour slot in this mode was painted two ways - an orange gem feeding a
#: yellow turret, with a yellow raider walking down at it. Every half of that was individually
#: correct, so nothing here could see it: the gems are cut from the pack untouched and everything
#: else is rotated onto a `Pal` entry, and no gate in this project holds one to the other. It was
#: reported off a device in four words. `--contact` is what the agreement is checked by, and this
#: is the second time that has been the only instrument (invariant 37f).
#:
#: The letter stays `y`. It is a *cell* letter - it reaches the authored boards, the offline
#: mirror and `WardLine.Colours`, and a save keys a loadout on it - so renaming it to `o` would
#: be renaming a shipped id to fix a picture.
WARD_HUES = (
    ("r", 0.986),               # Poppy   #F2404F
    ("g", 0.308),               # Mint    #7BD86A
    ("b", 0.561),               # Azure   #4FC1FF
    ("y", 0.075),               # Amber   #FF8A2B
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

#: The four bosses: a body reel, a cast reel, and how tall each is cut.
#:
#: <b>Not insects, and that is the whole of what this table is for.</b> They were four insects out
#: of the same fifteen bodies the raiders wear, which made every boss in this game a creeper drawn
#: three times the size - and the owner's verdict, met from a device, is that the raid and the thing
#: leading it must not be the same animal. It is invariant 37z's rule asked one level up: that entry
#: is about two <em>bosses</em> told apart by nothing, and this is a boss and the wave it walks in
#: front of told apart by nothing but scale. A boss is what a chapter is remembered for, so it may
#: not be a bigger copy of what the player has been shooting for nine rungs.
#:
#: <b>Four bodies out of `MONSTERS`' five, chosen on what each one <em>projects</em></b> (37ar's
#: rule, which is the one thing that decides whether a body survives a top-down camera): `Bos02` is
#: hooded with white horns and drawn in the douse's own teal, so the <b>blightcaller</b> looks like
#: the caster it is; `Bos03` is helmed and <b>holds a mace</b>, which is a duellist, so it is the
#: <b>warlord</b>; `Bos05` is the widest and the oddest - a barrel body under a teal lid with a
#: mace in one hand and a disc in the other - so it is the <b>warbringer</b>, the one that roars at
#: the whole line rather than picking a turret; and `Bos04` wears a <b>gold crown</b> over magenta,
#: which is what invariant 37x already called the overlord in prose. The fifth is spare,
#: deliberately - a fifth verb one day gets a body without a purchase.
#:
#: <b>The warbringer was `Bos01` until a render put it beside the second chapter's cast.</b> A green
#: boss with arms stood next to a green brute with arms and the only thing telling them apart was
#: size, which is the whole complaint this table exists to answer arriving one chapter later. Brown
#: and teal is a pair of colours no raider in either chapter wears - and that is the general point:
#: **a boss body has to be picked against the cast it will stand in front of, not on its own.**
#:
#: <b>Nothing here is hue-rotated</b>, exactly as before: a raider's colour is a rule (37f) and a
#: boss's colour is not, so these keep what the pack painted and are told apart by silhouette.
#:
#: <b>Two reels rather than three, and the walk is what went.</b> A boss used to carry idle, walk
#: and attack because it was a biped: an idle sliding down a hill reads as <em>floating</em>
#: (invariant 37u), so something had to choose between them every frame. Nothing in this cast
#: strides - these bodies bob in place over 25 frames and travel nowhere - so standing and walking
#: are one picture and the board is the only thing that moves it. A boss that really walked would
#: want `WalkReel` back.
#:
#: <b>And none of the five has a second animation</b>, where the old overlord had a take-off. So all
#: four cast reels are `pulse`'s synthesised rear-up rather than three of four. That is a real loss
#: and it is priced: a bought gesture beats a generated one, and what it buys back is four bosses
#: that do not look like the wave behind them.
BOSS_SET = {
    "blight":  (WARLORDS % 2, None, BLIGHT),
    "boss":    (WARLORDS % 3, None, BOSS),
    "bringer": (WARLORDS % 5, None, WARBRINGER),
    "over":    (WARLORDS % 4, None, OVERLORD),
}

#: The second chapter's twelve raiders: which pack a body comes from, and where in it.
#:
#: <b>This is the second cast set 37ar said a second pack would buy, and it cost exactly what that
#: entry predicted</b> - one table here, twelve rows in `SiegeMode`, and no code. The first set is
#: insects and stays insects; this one is the blob family the two monster packs share, so a player
#: crossing from one chapter to the next meets a different <em>kind</em> of thing rather than the
#: same thing in new colours (invariant 37z's complaint, asked of a chapter).
#:
#: <b>The kind is said by the silhouette and by nothing else, because colour is already spoken
#: for.</b> Every raider is hue-rotated onto the colour it answers to (37f), so the pack's own paint
#: is overwritten and cannot carry the kind. What survives a rotation is shape: a <b>plain smooth
#: egg</b> creeps, <b>arms</b> make a brute, and a <b>hard crest, stalks or a banded shell</b> makes
#: a bulwark. That the brutes are exactly the four bodies in either pack with arms is luck; that the
#: rule is one a player can learn without being told is not.
#:
#: <b>Three bodies are left out and one of them on purpose</b>: `MONSTERS`' fifth is a money bag,
#: which is loot rather than a raider, and standing one in a wave would teach that a thing on the
#: hill might be worth something.
#:
#: <b>The sources are smaller than the insects' and are cut to the same `CAST`</b>, so there is a
#: mild upscale in the bake - about 1.4x off a 130-pixel body. Flat vector art carries that where
#: pixel art would not (37at's "upscaling is the plainest cheap signal there is" is about a body
#: drawn at 180 and blown up on a phone, which is the runtime's job and is unchanged here). Cutting
#: them smaller would only move the same stretch to the device.
BROOD_SET = {
    # creepers - four plain eggs with nothing on them, so a wave of them reads as a swarm
    "broodMon_r":     (MONSTERS, BROOD % 1),
    "broodMon_g":     (MONSTERS, BROOD % 2),
    "broodMon_b":     (MONSTERS, BROOD % 4),
    "broodMon_y":     (KIT, KIT_BROOD % 3),

    # brutes - the four bodies in either pack that have arms
    "broodBrute_r":   (KIT, KIT_BROOD % 2),
    "broodBrute_g":   (KIT, KIT_BROOD % 6),
    "broodBrute_b":   (KIT, KIT_BROOD % 7),
    "broodBrute_y":   (KIT, KIT_BROOD % 10),

    # bulwarks - a banded shell over one armoured eye, a spined crest, two stalks, a visored eye
    "broodBulwark_r": (MONSTERS, BROOD % 3),
    "broodBulwark_g": (KIT, KIT_BROOD % 5),
    "broodBulwark_b": (KIT, KIT_BROOD % 8),
    "broodBulwark_y": (KIT, KIT_BROOD % 4),
}

#: How far a synthesised cast reel surges, and how far down the hill it leans.
#:
#: <b>A top-down beetle rears at the camera, so rearing is a change of size.</b> Three of the four
#: bosses have exactly one animation in the pack, and shipping them with no cast gesture at all
#: would have been the last verdict on this mode ("boring bosses") invited straight back - so the
#: reel is built: the body surges toward the viewer and a little down the hill over the same
#: `BossTell` window the ring and the crackle already fill, then settles. It is a real gesture
#: rather than a zoom because that is what the motion <em>is</em> from above; what would be a bug
#: is a boss that changed size and stayed changed, which is why the pulse is a sine.
BOSS_RISE, BOSS_LEAN = 0.17, 0.05

#: How much of a real cast animation is kept. See `boss_reels`.
BOSS_GESTURE = 0.40

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
#: **A warm hue only reads warm when it is bright**, so a body rotated onto the fourth ward's hue
#: off a dark source comes out *brown* - which was the yellow creeper, at every pull. A modest
#: lift fixes it and costs the other three nothing; twice this much starts flattening the reds,
#: which is the thing the low saturation floor is protecting. Rendered at 1.0, 1.08 and 1.15 to
#: pick it.
#:
#: **It is kept now that the fourth ward is `Pal.Amber` rather than `Pal.Sun`**, and for a reason
#: worth stating: orange is more forgiving than yellow here, so the lift is doing less work than
#: it was - but red and orange are now 32 degrees apart where red and yellow were 48, and value is
#: one of the two things keeping them separate. Take this out and the orange raider walks toward
#: the red one.
CAST_VAL_GAIN, CAST_VAL_LIFT = 1.08, 0.04

#: The value every raider is lifted onto, and the most it may be lifted by.
#:
#: <b>Measured per body rather than typed once, because these shells are not all painted at the
#: same brightness.</b> `hued` sets a pixel's <em>hue</em> and pushes its saturation, and leaves
#: its value alone - which is right, and is why a body drawn near black comes out near black in
#: whatever colour it was asked for. Measured across the twelve: the mean opaque value of a
#: hybrid's shell is 140 and of the darkest beetle 47, so on a board where <b>the colour of a
#: raider is the whole mechanic</b> two of the four blues read as black.
#:
#: So the gain is `CAST_VALUE / whatever this body actually measures`, which is `graded`'s argument
#: about the ground asked of the cast: the variety a pack gives you is hue and material, never
#: brightness, because brightness is what decides whether anything can be read at all. The ceiling
#: is there so a near-black body is lifted a long way and never turned into a grey one.
CAST_VALUE, CAST_VALUE_CEILING = 118.0, 3.0

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


def web():
    """A weaver's lock, drawn over a gem.

    <b>Drawn rather than cut, and translucent rather than opaque.</b> What a web has to say is
    "this gem is still that colour and you cannot move it" - so the colour underneath has to read
    through it, which rules out anything the packs draw as an object. It is threads and rings in
    the board's own bone white, at a third alpha, with the anchors bright enough to survive being
    forty pixels wide.
    """
    size = TILE
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(out)

    mid = size / 2.0
    ink = (238, 240, 232)
    spokes = 8

    for i in range(spokes):
        a = math.tau * i / spokes
        draw.line((mid, mid, mid + math.cos(a) * mid * .96, mid + math.sin(a) * mid * .96),
                  fill=ink + (150,), width=max(2, size // 64))

    for ring in (0.34, 0.58, 0.82, 0.98):
        pts = []
        for i in range(spokes + 1):
            a = math.tau * (i % spokes) / spokes
            # Sagging between the spokes, which is what makes it read as a web and not a dartboard.
            sag = ring * (0.90 if i % 2 else 1.0)
            pts.append((mid + math.cos(a) * mid * sag, mid + math.sin(a) * mid * sag))

        draw.line(pts, fill=ink + (120,), width=max(2, size // 80), joint="curve")

    # The anchors: eight bright knots at the rim, which is the part that survives at cell size.
    for i in range(spokes):
        a = math.tau * i / spokes
        x, y = mid + math.cos(a) * mid * .94, mid + math.sin(a) * mid * .94
        r = size // 34
        draw.ellipse((x - r, y - r, x + r, y + r), fill=ink + (225,))

    return out


def sack():
    """What a thief leaves where a gem was.

    <b>Deliberately the dullest thing on the field.</b> A sack is not a colour and can never line
    up with anything, so it has to read as dead weight against four saturated jewels - which is a
    job for value and saturation rather than for shape. Dark canvas, one cinch, one highlight.
    """
    size = TILE
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(out)

    body = (86, 74, 62)
    dark = (54, 46, 38)
    tie = (150, 128, 96)

    mid = size / 2.0
    draw.ellipse((size * .12, size * .30, size * .88, size * .95), fill=body)
    draw.ellipse((size * .12, size * .30, size * .88, size * .95), outline=dark,
                 width=max(2, size // 40))

    # The neck, gathered.
    draw.polygon([(size * .34, size * .40), (size * .66, size * .40),
                  (size * .60, size * .14), (size * .40, size * .14)], fill=body)
    draw.polygon([(size * .34, size * .40), (size * .66, size * .40),
                  (size * .60, size * .14), (size * .40, size * .14)], outline=dark,
                 width=max(2, size // 48))

    draw.rounded_rectangle((size * .30, size * .34, size * .70, size * .46),
                           radius=int(size * .05), fill=tie)

    # One highlight, so it is an object rather than a hole.
    draw.ellipse((size * .26, size * .48, size * .48, size * .66), fill=(112, 98, 82))

    return out


def pngs(z, prefix):
    """Every real PNG under a prefix.

    <b>`__MACOSX` is the trap</b>: a zip made on a Mac carries a shadow `._name.png` beside every
    file, which passes an `endswith(".png")` filter and is not an image - so a folder listing that
    forgets it fails on the first frame with a decode error that names nothing useful.
    """
    return [n for n in z.namelist()
            if n.startswith(prefix) and n.lower().endswith(".png")
            and not n.startswith("__MACOSX") and "/._" not in n]


def ward_model(z, model, frame=0):
    """One of the roster's twenty turrets, cut clean of its number plate and fitted to the ward box.

    <b>Cut at the model's own base rather than at `WARD_PLATE`, and that was reported from a
    phone.</b> A straight crop at a measured height was written on the argument that the edge it
    leaves sits behind the plinth the view draws - and it does not: the plate *overlaps* the
    turret's lower body, so cutting above the plate cuts the body, and what shipped was four
    turrets on a ward line with their bases sliced flat. `body_base` finds where the silhouette
    itself ends and `undigited` wipes the baked figure that is then still in shot.

    **The base is measured on frame nought and used for every frame**, or a recoil whose barrel
    moved would be cut a pixel differently from the one before it and the line would flicker.

    **Fitted to one box, pinned by its foot**, which is `turret`'s rule for the same reason: the
    twenty differ mostly at the top - a taller barrel, a second mount - and a turret that rose off
    its plinth when the player swapped it would read as the plinth having sunk.
    """
    base = body_base(read(z, "Merge Turrets/Png/Turrets/%s/%s-Shoot_00.png" % (model, model))) + 6

    im = read(z, "Merge Turrets/Png/Turrets/%s/%s-Shoot_%02d.png" % (model, model, frame))
    im = im.crop((0, 0, im.width, min(im.height, base)))

    box = im.getbbox()
    if box is not None:
        im = im.crop(box)

    im = undigited(im)

    out = Image.new("RGBA", (WARD_W, WARD_H), (0, 0, 0, 0))
    ratio = min(WARD_W / max(1, im.width), WARD_H / max(1, im.height)) * 0.94
    im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))), Image.LANCZOS)

    out.alpha_composite(im, ((WARD_W - im.width) // 2, WARD_H - im.height))
    return out


def body_base(im):
    """The row the turret's own silhouette ends on, ignoring what hangs below it.

    `WARD_PLATE` is a straight crop at a measured height, which is right on the board because
    the view draws a plinth over the cut edge - and wrong on a shelf, where there is no plinth
    and the cut reads as a turret with its feet chopped off. What is wanted here is the model's
    own base, so this walks up from the bottom to the last row still carrying most of the
    widest run: below that there is nothing but the pack's number plate, which is narrow.
    """
    px = im.load()
    wide = [sum(1 for x in range(im.width) if px[x, y][3] > 40) for y in range(im.height)]
    most = max(wide) if wide else 0

    for y in range(len(wide) - 1, -1, -1):
        if wide[y] >= most * 0.62:
            return y

    return im.height - 1


def undigited(im):
    """The pack's baked level number wiped out of its window.

    **The plate cannot be cropped away, because it overlaps the turret's own base** - crop above
    it and the model loses its feet, crop below it and the digit ships. So the digit goes and
    the window stays: it is exactly `(255, 255, 0)` in every one of the twenty, drawn on the
    plate's black, and nothing else in the kit is pure yellow. What is left reads as a dark
    vent, which is what it looks like anyway.

    It has to go because a ward's number is its **rank**, drawn at run time on the crest at its
    shoulder - two numbers on one turret is two readouts nobody can read (invariant 37y), and a
    shelf showing every model wearing a different baked figure is twenty of them.
    """
    out = im.copy()
    px = out.load()

    # The digit's own extent first, off the one colour that is exact. Its antialiasing is not
    # exact and would survive a colour test, so what is wiped is the *box* it stands in grown by
    # a few pixels - which is still comfortably inside the window, because the pack sets every
    # figure with a margin. Wiping a box rather than a colour is also what makes this safe on the
    # models whose bodies are themselves orange.
    x0, y0, x1, y1 = out.width, out.height, -1, -1
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = px[x, y]
            if a > 40 and r > 210 and g > 210 and b < 90:
                if x < x0: x0 = x
                if y < y0: y0 = y
                if x > x1: x1 = x
                if y > y1: y1 = y

    if x1 < 0:
        return out

    pad = 3
    for y in range(max(0, y0 - pad), min(out.height, y1 + pad + 1)):
        for x in range(max(0, x0 - pad), min(out.width, x1 + pad + 1)):
            a = px[x, y][3]
            if a > 40:
                px[x, y] = (0, 0, 0, a)

    return out


def ward_thumb(z, model):
    """The one uncoloured picture the loadout shelf browses a turret with.

    **The whole turret, which is not what the board draws.** See `body_base` and `undigited`.
    """
    im = read(z, "Merge Turrets/Png/Turrets/%s/%s-Shoot_00.png" % (model, model))
    im = im.crop((0, 0, im.width, min(im.height, body_base(im) + 6)))

    box = im.getbbox()
    if box is not None:
        im = im.crop(box)

    return fit(undigited(im), THUMB, 0.94)


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


def cast_frames(z, folder, count=FRAMES, tall=CAST, ink=(0, 0, 0)):
    names = spaced(ordered(z, folder), count)
    if not names:
        return []

    frames = deshadow([read(z, n) for n in names], ink)

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


#: What a baked ground shadow is made of, measured: <b>pure black at partial alpha</b>. The pack
#: composites every insect over a soft ellipse drawn at alpha 87 and RGB (0, 0, 0); the body is at
#: alpha 255, and the one other thing drawn see-through is a fly's <em>wings</em>, which are white.
#: So the shadow is separable exactly, and by something more honest than a threshold on darkness -
#: a black leg is opaque and stays, a white wing is translucent and stays.
SHADOW_ALPHA, SHADOW_INK = 200, 24

#: The same fact about the monster pack, measured the same way: <b>one violet at partial alpha</b>.
#: Every shaded pixel in `MONSTERS` and in `KIT`'s monsters is RGB (85, 63, 136) at alpha 8-128,
#: and every body is opaque - so the separation is exact here too, and it is a <em>different</em>
#: exact ink rather than a looser version of the same one.
#:
#: <b>Which is why `deshadow` takes the ink rather than growing a second branch.</b> A baked shadow
#: is a flat ink under an opaque body; that is one rule, and two packs each naming their own ink is
#: the rule used twice. A second function would be invariant 5b's fault - the same rule written out
#: again, correct until one copy meets a case the other was never given. Note what a colour test
#: buys that a darkness test cannot: these bodies carry a <b>very thick dark outline</b>, so
#: "anything dark and see-through" would eat the drawing's own line.
SHADE_VIOLET = (85, 63, 136)


def value_gain(frames):
    """How far this body has to be lifted to read at all. See `CAST_VALUE`."""
    total, count = 0.0, 0
    for im in spaced(frames, 4):
        a = np.asarray(im).astype(np.float32)
        body = a[..., 3] > 200
        if not body.any():
            continue
        total += float(a[..., :3][body].max(axis=-1).mean())
        count += 1

    if count == 0 or total <= 0.0:
        return CAST_VAL_GAIN

    return min(CAST_VALUE_CEILING, max(CAST_VAL_GAIN, CAST_VALUE / (total / count)))


def deshadow(frames, ink=(0, 0, 0), tol=SHADOW_INK):
    """Drop the ground shadow a pack bakes under every body.

    <b>It has to go because of what it does to the *frame*, not because of how it looks.</b> The
    ellipse is wider than the insect and sits below it, so a bounding box taken over the animation
    is half as wide again and a third taller than the body - and `SiegeView` sizes a body by its
    frame's height. Left in, a bulwark came out in a 242x177 frame around a body 110 across: a
    small beetle adrift in a box, which is invariant 37u's fault arriving through the art rather
    than through the framing.

    <b>The view draws its own shadow under every raider</b> (`SiegeView.Hatch`), sized to the body
    and sitting still while the body bobs - so nothing is lost here, and what is gained is that the
    shadow stops bobbing with the thing casting it.

    <b>What this does not take is the ellipse's own feathered edge</b> - a rim of grey at alpha
    5-86 and a halo out at alpha 1, which is invisible and which `Image.getbbox` counts, so a fly
    is still framed a good deal taller than its body. Measured and left alone deliberately: closing
    it changes how every raider is framed and therefore how big each is drawn, and this cast has
    been played and approved. It is its own change, not a tidy-up.

    <b>`ink` is what this pack's shadow is made of</b>, and the default is the insect pack's black,
    so every existing call cuts exactly what it always did: for black, "within `tol` of the ink on
    every channel" *is* "max channel below `tol`". See `SHADE_VIOLET` for why the second pack
    needed a colour rather than a darkness.
    """
    ink = np.array(ink, dtype=np.int16)
    out = []
    for im in frames:
        a = np.array(im)
        near = np.abs(a[..., :3].astype(np.int16) - ink).max(axis=-1) < tol
        shade = (a[..., 3] < SHADOW_ALPHA) & near
        if not shade.any():
            out.append(im)
            continue

        a[shade, 3] = 0
        out.append(Image.fromarray(a, "RGBA"))

    return out


def pulse(frames, count, rise, lean):
    """A cast reel built out of a body reel: the insect surges at the viewer and settles.

    <b>Three of the four bosses have exactly one animation in the pack</b>, and a boss that does
    nothing at all when it throws is the last verdict on this mode invited straight back. From
    above, rearing up <em>is</em> a change of size - so the body grows toward the viewer and leans
    a little down the hill over the same window the ring and the crackle already fill.

    <b>A sine, so it begins and ends on the standing pose.</b> A ramp would leave the boss bigger
    than it started and the snap back would read as the bug invariant 37u names; the legs keep
    cycling underneath, because the body reel is still being walked through.
    """
    n = max(2, count)
    wide = int(math.ceil(frames[0].width * (1.0 + rise)))
    high = int(math.ceil(frames[0].height * (1.0 + rise + lean)))

    out = []
    for i in range(n):
        t = math.sin(math.pi * i / float(n - 1))
        src = frames[min(len(frames) - 1, int(i * len(frames) / float(n)))]

        scale = 1.0 + rise * t
        big = src.resize((max(1, int(round(src.width * scale))),
                          max(1, int(round(src.height * scale)))), Image.LANCZOS)

        pane = Image.new("RGBA", (wide, high), (0, 0, 0, 0))
        pane.alpha_composite(big, (int(round((wide - big.width) / 2.0)),
                                   int(round((high - big.height) / 2.0 + lean * high * t))))
        out.append(pane)

    return out


def one_canvas(reels, body, count, tall):
    """Every reel of one character, cut onto **one** canvas so the body cannot jump or resize.

    <b>Why this is not one call to `cast_frames` per reel.</b> Trimming each animation to its own
    bounding box and fitting each into its own frame draws the body at a different *size* in each -
    on screen that is a boss that shrinks by a quarter every time it casts and grows back
    afterwards, which reads as a bug in the game rather than as an animation.

    <b>The alignment is the canvas centre, which is exact rather than approximate.</b> Every
    animation of one insect in this pack is exported on the same canvas, and a reel this tool
    synthesises (`pulse`) is grown symmetrically about that centre - so two frames of one body sit
    at the same place by construction, with nothing to measure and nothing to get wrong. The
    version this replaced aligned two *differently sized* canvases by matching the alpha centroid
    of their first frames, which needed those frames to be the same pose and said so out loud;
    that is a stronger check than it sounds, and it is simply not needed once the canvas is shared.

    <b>Nothing is ever cut off, and the frame is widened symmetrically to manage it.</b> The view
    sizes a body by its *height* (`SiegeView.Frame`), so a wider canvas costs no size at all - what
    it costs is **centring**, because the view centres the canvas on the lane. So the width is
    taken as far as the widest thing reaches on either side of the body's own centre, and mirrored.
    """
    middles = [(r[0].width / 2.0, r[0].height / 2.0) for r in reels]

    def extent(over):
        box = None
        for frames, (cx, cy) in over:
            for im in frames:
                bb = im.getbbox()
                if bb is None:
                    continue
                here = (bb[0] - cx, bb[1] - cy, bb[2] - cx, bb[3] - cy)
                box = here if box is None else (min(box[0], here[0]), min(box[1], here[1]),
                                                max(box[2], here[2]), max(box[3], here[3]))
        return box

    pairs = list(zip(reels, middles))
    home = extent([pairs[body]])
    whole = extent(pairs)

    if home is None or whole is None:
        return None

    across = (home[0] + home[2]) / 2.0
    reach = max(across - whole[0], whole[2] - across, across - home[0], home[2] - across)

    # **Mirrored on both axes about the body's own middle, not just across.**
    #
    # The width has always been taken this way, for a stated reason: the view centres the canvas on
    # the lane, so a canvas that grew only on the side a thrown fist went would stand the body off
    # to one side. The *height* was taken as the plain union - tall as everything, so nothing is
    # ever clipped - and that has the same fault one axis over, which nothing noticed until the
    # cast became insects: a gesture that rises puts the body low in its own frame, and everything
    # the view positions relative to a body is positioned relative to the *frame*. What that looked
    # like on a device is a boss whose shadow sat further from it than its raiders' did.
    #
    # Mirroring only ever grows the canvas, so the promise below is untouched: a clipped head is
    # not a throw, it is a mistake, and nothing is cut off anywhere.
    down = (home[1] + home[3]) / 2.0
    fall = max(down - whole[1], whole[3] - down, down - home[1], home[3] - down)

    box = (math.floor(across - reach), math.floor(down - fall),
           math.ceil(across + reach), math.ceil(down + fall))

    # The claim above, asserted rather than believed. It is the one property of this frame that
    # matters and the one nothing downstream could ever notice: a clipped body imports, addresses,
    # audits and draws, and the only symptom is a boss losing a horn for four frames of a lunge
    # that nobody is looking at closely (invariant 32b - no gate in this project opens a PNG).
    if not (box[0] <= whole[0] and box[2] >= whole[2]
            and box[1] <= whole[1] and box[3] >= whole[3]):
        raise SystemExit("the shared canvas does not contain every frame of every reel, so "
                         "something is being cut off")

    wide, high = int(box[2] - box[0]), int(box[3] - box[1])
    ratio = tall / float(high)
    size = (max(1, int(wide * ratio)), tall)

    out = []
    for frames, (cx, cy) in pairs:
        cut = []
        for im in spaced(frames, count):
            pane = Image.new("RGBA", (wide, high), (0, 0, 0, 0))
            pane.alpha_composite(im, (int(round(-box[0] - cx)), int(round(-box[1] - cy))))
            cut.append(pane.resize(size, Image.LANCZOS))
        out.append(cut)

    return out


def boss_reels(z, body, cast, tall, ink=(0, 0, 0)):
    """A boss's two reels: the one it stands and walks in, and the one it throws in.

    The cast reel is a <b>there-and-back</b> whichever way it is come by - the pack's own take-off
    played forward and then reversed, or `pulse`'s sine - so it always begins and ends on the
    standing pose and there is no frame anywhere on which the boss snaps.
    """
    stand = deshadow([read(z, n) for n in ordered(z, body)], ink)
    if not stand:
        return None

    if cast is None:
        thrown = pulse(stand, BOSS_CAST_FRAMES, BOSS_RISE, BOSS_LEAN)
    else:
        gesture = deshadow([read(z, n) for n in ordered(z, cast)], ink)
        if not gesture:
            return None

        # **Only the first part of it**, and the reason is the shared canvas rather than the
        # gesture. The pack's take-off carries the insect 64 pixels off the ground out of a
        # 278-pixel frame, and `one_canvas` is tall as everything - so the whole reel would set
        # the frame's height and the body would be drawn a quarter smaller in *both* reels for
        # the sake of a rise nobody is looking at. Cut to a rear-up it costs nothing and reads
        # better: the overlord lifts, hurls, and settles.
        gesture = gesture[:max(2, int(len(gesture) * BOSS_GESTURE))]
        thrown = gesture + gesture[::-1]

    reels = one_canvas([stand, thrown], 0, max(BOSS_FRAMES, BOSS_CAST_FRAMES), tall)
    if reels is None:
        return None

    return spaced(reels[0], BOSS_FRAMES), spaced(reels[1], BOSS_CAST_FRAMES)


# --------------------------------------------------------------------------- the drop


def build():
    """Every PNG this mode ships, as {relative path: image}. Empty when the packs are absent."""
    match3, blasts = zipped(MATCH3), zipped(BLASTS)
    turrets, kit = zipped(TURRETS, TOWER), zipped(KIT, TOWER)
    monsters = zipped(MONSTERS, ENEMIES)

    # The skill-icon pack, of which this mode uses exactly one. Absent is a checkout without it,
    # so the one PNG it cuts is simply not offered and `--check` has nothing to hold it to.
    icons = ICONS if ICONS.exists() else None

    if match3 is None or blasts is None or turrets is None or kit is None:
        return None

    # The bosses and half the second chapter's cast live here. Absent, this is a checkout without
    # the pack rather than a mistake - the same bargain every art tool in this project strikes -
    # so it is answered by cutting neither, and `--check` then has nothing to hold them to.
    if monsters is None:
        return None

    #: Which zip a `BROOD_SET` row names. A table of bodies has to say which pack each is in, and
    #: one dict is how it says it without a branch per row.
    packs = {KIT: kit, MONSTERS: monsters}

    made = {}

    for key, (src, _) in GEMS.items():
        made["Siege/%s.png" % key] = fit(read(match3, src), TILE, 0.88)

    # **Ten grounds, one per place in a chapter** (invariant 7c). One rung's floor keeps whatever
    # value ladder its own sheet was drawn with and is measured first, because the other nine are
    # normalised onto it rather than onto a pair of typed numbers - see `graded`.
    # **Generated rather than laid** - see `make_siege_ground`. What did not change is the rule
    # underneath: one rung's floor still sets the value ladder the other nine are normalised onto,
    # because that is what keeps a hill darker and duller than the cast walking over it, and it
    # has been got wrong twice before. What changed is where the pixels come from, and that the
    # ten no longer need a folder nobody else has.
    raw = ground_art.grounds()

    for spec in ground_art.GROUND:
        key = spec["key"]
        made["Siege/%s.png" % key] = graded(depth(raw[key]), GROUND_MEAN,
                                            GROUND_SPREAD, GROUND_CHROMA)
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

    # **The roster: twenty turrets times four colours, plus one thumbnail each.**
    #
    # A run loads the four its player chose (`WardLine.Art`, scoped by the screen); the shelf
    # browses the thumbnails. Nothing ever loads eighty turrets, which is invariant 7b's whole
    # bargain - memory bounded by what is on the screen rather than by how much content exists.
    merge = zipped(MERGE)
    if merge is None:
        return None

    for model_id, model in WARD_MODELS:
        stand = ward_model(merge, model, 0)

        shots = sorted(pngs(merge, "Merge Turrets/Png/Turrets/%s/" % model))

        step = max(1, len(shots) // FIRE_FRAMES)
        recoil = [ward_model(merge, model, min(len(shots) - 1, f * step))
                  for f in range(FIRE_FRAMES)]

        # **Under `Ui/` rather than `Siege/`, because a thumbnail is a shelf's picture and never
        # the board's.** The four turrets a run stands come out of `AssetLibrary.LineScope`; these
        # twenty come out of the loadout screen's own scope and are released when it closes, which
        # is invariant 16c's rule - a shelf costs the shelf rather than the catalog.
        made["Ui/Wards/%s.png" % model_id] = ward_thumb(merge, model)

        for letter, hue in WARD_HUES:
            made["Siege/Wards/%s_%s.png" % (model_id, letter)] = hued(stand, hue)

            for f, frame in enumerate(recoil):
                made["Siege/Wards/%s_%s_fire/f%02d.png" % (model_id, letter, f)] = hued(frame, hue)

    # **The two insects.** A weaver crawls and a thief hovers; both hold the middle of the hill and
    # work on the field rather than on the line, so what they have to say from across the board is
    # which of the two they are - which is why one of them walks and one of them does not.
    for i, (letter, hue) in enumerate(WARD_HUES):
        beetle = WEAVER_SET[i]
        crawl = sorted(pngs(merge, "Merge Turrets/Png/Enemies/Grounds Enemy/%s/" % beetle))

        for f, name in enumerate(spaced(crawl, FRAMES)):
            made["Siege/weaver_%s/f%02d.png" % (letter, f)] = hued(
                fit(read(merge, name), INSECT, 0.94), hue, CAST_PULL, CAST_SAT_GAIN,
                CAST_SAT_FLOOR, CAST_VAL_GAIN, CAST_VAL_LIFT)

        hybrid = THIEF_SET[i]
        hover = sorted(pngs(merge, "Merge Turrets/Png/Enemies/Hybrid Enemy/%s/Flying/" % hybrid))

        for f, name in enumerate(spaced(hover, FRAMES)):
            made["Siege/thief_%s/f%02d.png" % (letter, f)] = hued(
                fit(read(merge, name), INSECT, 0.94), hue, CAST_PULL, CAST_SAT_GAIN,
                CAST_SAT_FLOOR, CAST_VAL_GAIN, CAST_VAL_LIFT)

    # What a weaver leaves, and what a thief leaves. Both drawn: a web has to let the gem's colour
    # read through it and a sack has to be the dullest thing on a field of four saturated jewels,
    # and neither is a job a licensed object sheet does (invariant 32b, from the other side).
    made["Siege/web.png"] = web()
    made["Siege/sack.png"] = sack()

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

    charged = charge(icons)
    if charged is not None:
        made["Siege/charge.png"] = charged

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

    # **Every raider, hue-rotated into its own colour.** A gentler grade than the wards get: a
    # turret has to read as *lit* on a bright hill, where a raider has to keep its own face - so
    # saturation is pushed less far and the value is barely lifted. Both go through one function,
    # because two copies of a hue rotation is two ways for a red raider and a red bolt to disagree
    # about what red is.
    hues = dict(WARD_HUES)

    for key, folder in RAIDER_SET.items():
        hue = hues.get(key[-1])
        frames = cast_frames(merge, folder)
        gain = value_gain(frames)

        for i, im in enumerate(frames):
            made["Siege/%s/f%02d.png" % (key, i)] = im if hue is None else hued(
                im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=CAST_SAT_FLOOR,
                val_gain=gain, val_lift=CAST_VAL_LIFT)

    # The second chapter's cast, out of the two blob packs. The same three lines as the insects
    # above, because a cast set is a *table* and not a code path - which is the whole of what
    # `BROOD_SET`'s entry claims and the reason a third set would cost nothing either.
    for key, (pack, folder) in BROOD_SET.items():
        hue = hues.get(key[-1])
        frames = cast_frames(packs[pack], folder, ink=SHADE_VIOLET)
        gain = value_gain(frames)

        for i, im in enumerate(frames):
            made["Siege/%s/f%02d.png" % (key, i)] = im if hue is None else hued(
                im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=CAST_SAT_FLOOR,
                val_gain=gain, val_lift=CAST_VAL_LIFT)

    # The four bosses: two reels each, both off one canvas so none of them jumps or changes size
    # when it throws. One loop rather than one block per boss, which is what stopped a third and a
    # fourth being expensive - and what makes the *set* something a reader can see at once.
    for key, (body, cast, tall) in BOSS_SET.items():
        reels = boss_reels(monsters, body, cast, tall, ink=SHADE_VIOLET)
        if reels is None:
            continue

        for name, frames in ((key, reels[0]), (key + "_cast", reels[1])):
            for i, im in enumerate(frames):
                made["Siege/%s/f%02d.png" % (name, i)] = im

    return made


#: How big the overcharge glyph is cut, and how round its corners are.
#:
#: Square, because the turret's chassis carries a square panel and a badge that fits it reads as
#: part of the machine rather than as a sticker on one. 128 is twice what a phone draws it at.
CHARGE_SIZE, CHARGE_ROUND = 128, 22


def charge(root):
    """The overcharge glyph: one of the pack's skill icons, squared and rounded.

    **Kept as a tile rather than keyed out of its own background.** These icons are painted *on*
    their ground - the glow round the bolt is most of what makes it read - so a flood or a colour
    key takes the light with it and leaves a thin yellow scribble (the shop's own sheet-keying
    lesson, met again). A rounded tile on the turret's square chassis panel is what the art is
    already shaped like.
    """
    if root is None:
        return None

    source = root / "PNG" / ("skill icon %d.png" % CHARGE_ICON)
    if not source.exists():
        return None

    im = Image.open(source).convert("RGBA")

    side = min(im.width, im.height)
    im = im.crop(((im.width - side) // 2, (im.height - side) // 2,
                  (im.width - side) // 2 + side, (im.height - side) // 2 + side))
    im = im.resize((CHARGE_SIZE, CHARGE_SIZE), Image.LANCZOS)

    # A rounded mask, and a keyline in the interface kit's navy so it sits on a bright chassis the
    # way every other badge in this game does (44h's rule about a flat render on a cartoon board).
    mask = Image.new("L", (CHARGE_SIZE, CHARGE_SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, CHARGE_SIZE - 1, CHARGE_SIZE - 1],
                                           radius=CHARGE_ROUND, fill=255)

    tile = Image.new("RGBA", (CHARGE_SIZE, CHARGE_SIZE), (0, 0, 0, 0))
    tile.paste(im, (0, 0), mask)

    edge = Image.new("RGBA", (CHARGE_SIZE, CHARGE_SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle([2, 2, CHARGE_SIZE - 3, CHARGE_SIZE - 3],
                                           radius=CHARGE_ROUND - 2, outline=(6, 24, 56, 255),
                                           width=5)
    tile.alpha_composite(edge)

    return tile


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

    shape(made)
    print("%d files are what this tool cuts" % len(made))


def shape(made):
    """The shape a hill is authored at, held to the one other place that has to know it.

    <b>A check that compares this tool to itself cannot fail, and the first cut of this was
    exactly that</b> - it asserted the pictures it had just generated were the size of the canvas
    it had just generated them on. What actually needs joining is the two *sides*: the hill's
    aspect decides how much `SiegeView.GroundSize` crops off a band, and the only place C# writes
    that number down is `SiegeGroundTests.GroundAspect`, which has to be typed because an offline
    run loads no sprite (a lookup that answers null whatever is on disk is not a check). So this
    reads the literal out of the fixture and holds it to `make_siege_ground.W/H`. Re-author the
    canvas without it and the test goes on proving the old shape covers a band - green, and about
    art that no longer exists.
    """
    want = (ground_art.W, ground_art.H)
    wrong = [rel for rel, im in sorted(made.items())
             if rel.startswith("Siege/hill") and im.size != want]
    if wrong:
        sys.exit("%s: not the authored %dx%d - see make_siege_ground.W/H"
                 % (", ".join(wrong), want[0], want[1]))

    fixture = Path(__file__).resolve().parent.parent / "Assets/Game/Tests/SiegeGroundTests.cs"
    if not fixture.exists():
        return

    said = re.search(r"GroundAspect\s*=\s*(\d+)f\s*/\s*(\d+)f", fixture.read_text(encoding="utf-8"))
    if not said:
        sys.exit("SiegeGroundTests: no GroundAspect to hold the hill's shape to")

    if (int(said.group(1)), int(said.group(2))) != want:
        sys.exit("SiegeGroundTests.GroundAspect is %s/%s and the hill is authored %dx%d"
                 % (said.group(1), said.group(2), want[0], want[1]))


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
    global SOURCE, TOWER, ENEMIES, ICONS

    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--source", default=str(SOURCE))
    ap.add_argument("--tower", default=str(TOWER))
    ap.add_argument("--enemies", default=str(ENEMIES))
    ap.add_argument("--icons", default=str(ICONS))
    args = ap.parse_args()

    SOURCE = Path(args.source)
    TOWER = Path(args.tower)
    ENEMIES = Path(args.enemies)
    ICONS = Path(args.icons)

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
