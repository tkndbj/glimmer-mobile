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
import json
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

#: The fifth folder: the survival-wizard pack the **third** chapter is cast out of.
#:
#: **A fifth root rather than a copy, for the reason the second, third and fourth exist** - copying
#: a licensed pack so one path works is a second copy nothing keeps in step. It is the downloads
#: folder itself rather than a folder inside it, because this pack ships as a bare zip; `--survival`
#: moves it, and the tool passes when it is absent exactly as it does for the others.
SURVIVAL = Path(r"C:\Users\Digikey\Downloads")

#: The sixth folder: a hundred RPG gem icons, which is where the two *charmed* gems are cut from.
#:
#: **A sixth root rather than a copy, for the reason the second through fifth exist** - copying a
#: licensed pack so one path works is a second copy nothing keeps in step. `--gems` moves it, and
#: the tool passes when it is absent exactly as it does for the others.
GEMPACK = Path(r"C:\Users\Digikey\Downloads\craftpix-net-668473-rpg-gems-icons-pack")

#: The seventh folder: the head-on cartoon monster packs the **fourth** chapter is cast out
#: of, and the two the three re-cut bosses come from.
#:
#: **A seventh root rather than a copy, for the reason the second through sixth exist** -
#: copying a licensed pack so one path works is a second copy nothing keeps in step.
#: `--cartoon` moves it, and the tool passes when it is absent exactly as it does for the
#: others.
CARTOON = Path(r"C:\Users\Digikey\Downloads\2D ASSETS")

#: The eighth folder: the **top-down boss packs**, which is where every boss in this mode that is
#: not one of the five merge-shooter blobs now comes from.
#:
#: <b>This root is what closed the one hole the seventh opened.</b> `CARTOON` holds head-on
#: bodies - the packs are drawn facing the viewer at eye level, not looked down on - and for one
#: drop three bosses were cut from it because the survey said there was nothing else. There is:
#: seven packs of top-down units, twenty-one bodies, each with Front, Back, Left and Right and a
#: real attack in every direction. The owner bought them when the angle was finally named as the
#: fault, and the whole of what it cost to undo the head-on cut was three rows in `BOSS_SET` -
#: which is the bargain a table is for, recorded here for the second time.
#:
#: **An eighth root rather than a copy, for the reason the second through seventh exist** -
#: copying a licensed pack so one path works is a second copy nothing keeps in step. `--units`
#: moves it, and the tool passes when it is absent exactly as it does for the others.
UNITS = Path(r"C:\Users\Digikey\Downloads\units")

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
#: The top-down tower-defence pack: its grass, and **the fourth chapter's whole cast**.
#:
#: <b>It is the one pack on this machine drawn walking *toward* the camera</b>, which is the
#: only view this board has and the property every shipped cast shares: an insect is seen
#: from above, a blob and a skeleton are square-on, and all three are mirror-symmetric about
#: their own middle because that is what a body facing you looks like. Ten bodies, in three
#: views each; `FRONT` is the one that matters.
#:
#: <b>It was rejected once, on a number, and the number was the wrong thing to weigh.</b> Its
#: bodies are 53 to 73 pixels tall against a raider cut at `CAST`, so they upscale 2.5x to
#: 3.4x where every other cast here is cut *down* or nearly so - and the first replacement
#: cast was chosen on that alone, out of a pack drawn in three-quarter and side profile. The
#: owner's verdict was immediate and is the rule: <b>a cast that faces the wrong way is not
#: a cast, however sharp it is.</b> Flat vector art inside a heavy dark outline carries an
#: upscale better than anything else could; facing is not recoverable at any resolution.
FIELD = "craftpix-net-869102-tower-defense-neighborhood-top-down-2d-asset-pack.zip"

#: Where the pack keeps its ten bodies, and the view of each that this board draws.
#:
#: **`Front view` is the walk toward the viewer** - down the hill, at the ward line. The pack
#: also draws a side and a back view for a game whose lanes turn corners; this one does not.
#: The rig, the vector art and the animation the fourth chapter's cast is baked from.
#:
#: <b>Baked rather than cut, and that is this pack's whole reason for being usable.</b> Its
#: exported PNG frames are 50 to 73 pixels a body against a raider cut at `CAST`, so cutting them
#: means a 2.5x to 3.4x upscale - measured, the interior line work carries **8.0** against the
#: bone cast's 17.1 and the insects' 27.6, and an unsharp mask recovers less than a fifth of that
#: gap because the detail is not in the file. The pack also ships the artwork as **vectors** and
#: the walk as a **Spine rig**, so the frames can be re-baked at whatever size this board wants:
#: at the height a raider is cut, the same measurement reads **30.8**.
#:
#: `Tools/spine_bake.py` is the baker, and it is held to the pack's own exported frames rather
#: than to anybody's eye - see `RABBLE_ANIM`.
RABBLE_RIG = "Json Atlas/Zombies/Zombies%02d/zombies%02d.json"
RABBLE_ART = "Ai/Enemy Characters.ai"
RABBLE_PARTS = "Spine/Enemy/zombies%02d/Images/"

#: Which animation of the three the board draws. See `FIELD`.
RABBLE_ANIM = "Front view walk"

#: The slots this cast never draws.
#:
#: <b>`Bg` is a layout guide</b> - a 160x162 rectangle the pack exports its frames against, which
#: is how the export box is known at all. <b>The `shade*` slots are the pack's baked ground
#: shadow</b>, and dropping them is strictly better than `deshadow`: that function has to guess a
#: shadow out of an alpha threshold and an ink colour and leaves the feathered rim behind by
#: design, where a rig knows which *part* the shadow is. The view draws its own under every
#: raider (`SiegeView.Hatch`).
RABBLE_SKIP = ("Bg", "shade", "shade2", "shade3")

#: How finely the vector art is rasterised before it is placed, in DPI.
#:
#: **Ten times PDF's own 72**, which puts every part well above the size it is ever drawn at, so
#: the only resampling that decides the result is the one `spine_bake.draw` does into the frame.
#: Higher costs bake time and changes nothing on screen.
RABBLE_DPI = 720

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

#: The survival-wizard pack: **five skeletons and the caster that raises them**, and the third
#: chapter's whole cast.
#:
#: <b>Five bodies against twelve slots, which is a smaller budget than either cast before it and
#: changes what the cast says.</b> The insects had fifteen bodies and the brood fifteen, so both
#: could give every kind four distinct silhouettes and let the shape carry the colour as well as
#: the kind. Five cannot, and pretending otherwise would mean cutting one body four times and
#: calling it four - so this cast says the two things it can say <em>plainly</em> instead: a
#: <b>bare rib cage creeps</b>, a <b>helm and a long weapon</b> is a brute, and a <b>shield or a
#: closed visor</b> is a bulwark. Three silhouettes on the hill rather than twelve, each one
#: unmistakable at a cell and a bit.
#:
#: <b>What pays for it is that the colour is still said three times</b> (invariant 37f): the body
#: is hue-rotated, the view tints it and the view rings it. None of those was ever the silhouette's
#: job - the twelve-body casts gave it a fourth reading for free, and five bodies buy the first
#: three back at full strength by making the kind readable instead.
#:
#: <b>It is drawn head-on, which is the one view this board has</b> (37ar). The skeletons face the
#: camera and stand upright, exactly as the brood's blobs do, so the hill still reads as one place.
#:
#: <b>And it is the first pack here whose raiders carry a second animation worth drawing.</b> Every
#: body ships a walk and an <em>attack</em>; see `SWING_ANIM`.
#:
#: <b>Only its five enemies are read.</b> Its wizard was this chapter's boss for one draft and was
#: withdrawn by the owner; see `BOSS_SET`.
WIZARD = "graphicriver-wPfi16JK-survival-wizard.zip"

#: Where the pack keeps its five skeletons. **Its wizard is deliberately not read**: it was
#: this chapter's boss for one draft and the owner's verdict on it was one line - wrong, use
#: something else. What replaced it is a body from a monster pack (`MONS_V4`), by way of a
#: bake that was itself withdrawn. See `BOSS_SET`.
BONES = "Survival Wizard/Png/Enemies/E%d"

#: The quirky-monsters pack. **Cut from nothing and kept in the survey on purpose.**
#:
#: <b>It was the fourth chapter's cast for one drop and the owner withdrew it in one line:
#: the bodies face sideways.</b> They do - a caterpillar in pure profile, a robot with a
#: cannon across its chest, a goblin turned three-quarter - and every cast this game ships is
#: square-on, because a hill is looked down on and a raider walks toward the player. It was
#: picked for the opposite reason: it is high resolution where the pack that faces the right
#: way is not, and **sharpness was weighed above facing, which is the wrong way round.**
#:
#: It stays in `CHARACTER_PACKS` so the next person meets it with that written under it.
QUIRK = "craftpix-net-731843-quirky-monsters-game-asset-pack-v19.zip"

#: Where that pack keeps its five bodies. **Read by `--survey` alone.**
QUIRK_BODY = "Png/Monster %d"

#: Which animation inside a body folder is its walk, and which is what it swings at the line.
#:
#: <b>The swing is a raider's second reel, and it is here because two of the five packs drew
#: one.</b> Every raider that reaches the ward line stands there hitting it every
#: `SiegeTuning.BlowEvery` until something kills it - and for two chapters what that looked
#: like was a walk cycle looping in place against a turret, which is invariant 37u's
#: complaint (a body doing the wrong thing where it stands) arriving through the art.
#:
#: <b>Short on purpose.</b> A swing is six frames against a walk's twelve: it is played at
#: the line, where a body is at its smallest and there are up to five of them, and every
#: frame is a texture that is resident for the whole run. The insects and the brood have no
#: swing at all and fall back to their walk (`SiegeMode.CastSwing` answers an empty address),
#: which is exactly what they do today - so this costs those two chapters nothing.
#:
#: <b>The same two words name a boss's second reel</b> (`BOSS_SWING`), because every pack
#: here that draws an attack files it under the same name.
WALK_ANIM, SWING_ANIM = "/Walk", "/Attack"

#: What a **boss** throws in, inside its own body folder.
#:
#: <b>A row in `BOSS_SET` names an animation, never a character folder, and that rule was
#: learned the expensive way.</b> This note used to read "the five blob bosses have exactly one
#: animation each and no suffix to add", which was true of the five `MONSTERS` rows and silently
#: false of the three added from the head-on packs three drops later: `PNG/MonsterV5` holds
#: *four* animations, so `ordered` collected all 72 frames of Attack, Idle, Jump and Walk and
#: sorted them by trailing number, interleaving four gestures into one reel. `spaced` then took
#: twelve evenly through the mixture, so the bonecaller, the shackler and the ironclad each
#: shipped a walk that cut between a jump and a lunge every frame: the body travelled **116 px
#: between consecutive frames** against the 0.5 px the five sound reels hold, wandered out of its
#: lane and off its footing, and read as a boss pasted on the wall at the wrong angle.
#:
#: <b>Nothing numeric could catch it and `--check` reproduced it perfectly</b>, which is what
#: `ordered`'s own docstring warns about one level down: the frames are all present, all the right
#: size, and a scrambled reel is reproduced as faithfully as a correct one. The measurement that
#: sees it is frame-to-frame centroid travel, and `Tools/verify/fxreels.py` asks it now.
BOSS_SWING = SWING_ANIM

#: Two of the four head-on cartoon monster packs on this machine, which is where the three
#: bosses that used to be baked out of 3D come from.
#:
#: <b>They were written off in this file for a year as "side-view" and they are not</b> - the
#: `v1` to `v4` packs are drawn head-on, exactly as `WIZARD`'s skeletons and the brood are,
#: and only `v6` and `v7` really face sideways. That mistake is what the bake was
#: commissioned around: `BOSS_SET` recorded "there is nothing else on this machine" when
#: there were twenty unused bodies in these four zips. **Rendered, not remembered** - the
#: survey sheet is `Tools/siege_pack_survey.png`.
#:
#: <b>Only bosses come from here and never a raider</b>, deliberately: these are round
#: cartoon blobs with arms and mouths, which is exactly what the <em>second</em> chapter's
#: brood already is (`BROOD_SET`), so a chapter cast out of them would be Broodmarch again
#: in new colours - invariant 37z's complaint asked of a chapter. A boss is the one thing
#: that may be a blob without saying anything about the wave behind it, because it is three
#: times the size and holds the middle of the hill.
#: <b>Nothing is cut from either any more.</b> They cast the bonecaller, the shackler and the
#: ironclad for one drop; all three are top-down bodies now (`UNIT_PACKS`). They stay named so
#: `--survey` still draws them, which is this file's standing rule about rejected packs.
MONS_V1 = "craftpix-net-167954-monster-v1-character-sprites.zip"
MONS_V4 = "craftpix-net-894353-monster-v4-character-sprites.zip"

#: The other four in the family. **Nothing is cut from these** - they are named so `--survey`
#: draws them, which is the whole point of that flag: the claim "there is nothing else on this
#: machine" is only worth anything if somebody can re-run the look that produced it.
MONS_V2 = "craftpix-net-154190-monster-v2-character-sprites.zip"
MONS_V3 = "craftpix-net-205925-monster-v3-character-sprites.zip"
MONS_V6 = "craftpix-net-534332-monster-v6-sprite-set.zip"
MONS_V7 = "craftpix-net-925935-monster-v7-sprite-pack.zip"

#: The seven top-down unit packs, and the three that cast a boss.
#:
#: <b>Every one of these is drawn looked-down-on</b>, which is the single property the whole
#: shelf above fails and the reason this root exists. They also share one shape: a body is a
#: named folder holding `Front`, `Back`, `Left` and `Right` crossed with Idle, Walking, Running,
#: Attacking, Hurt and Dying - so a boss's two reels are two folders rather than a folder plus a
#: guess, which is exactly the mistake that shipped three broken bosses (see `BOSS_SWING`).
MYTH = "craftpix-net-270646-mythology-2d-character-assets-anubis-medusa-horus.zip"
BOSSPACK2 = "craftpix-net-191083-top-down-fantasy-boss-characters-pack-2-yeti-ogre-cyclops.zip"
BONEUNITS = "craftpix-net-229181-top-down-skeleton-characters-pack-wizard-knight-archer.zip"

#: The other four, named so `--survey` draws them - and, since the fifth chapter, cut from.
#: **Seven of the fifteen bodies these packs held cast Thundercrag**: five raiders (`WILD_SET`)
#: and two bosses (`BOSS_SET`'s thunderer and colossus). Eight are still uncut - the skeleton
#: knight and archer, Medusa, Horus, the pharaoh and the three wizards - which is what a sixth
#: chapter comes out of without a purchase.
BOSSPACK3 = "craftpix-net-545831-top-down-boss-characters-pack-3-rock-earth-ice-monsters.zip"
ANCIENTS = "craftpix-net-947906-ancient-mythology-boss-characters-top-down-asset-pack.zip"
WIZUNITS = "craftpix-net-954187-top-down-wizard-characters-pack-male-veteran-female.zip"
MERGETURRETS = "craftpix-net-715522-turrets-asset-pack-for-merge-shooter.zip"

#: How a unit pack names a body's two reels. <b>The direction is part of the path</b>, and
#: `Front` is the one this board wants: a raider walks *down* the hill toward the player, so the
#: face it shows is its front. The other three directions are drawn and unused - the board never
#: turns a body - which is the one thing these packs carry that this mode has no use for.
UNIT_WALK = "%s/PNG/PNG Sequences/Front - Walking"
UNIT_CAST = "%s/PNG/PNG Sequences/Front - Attacking"

#: How those two name their bodies. `v1` is the odd one out and this is the pack's own
#: spelling - a body identified by a guess is a folder that is simply not there.
HORDE_V1, HORDE_V4 = "PNG/MonsterV%d", "PNG/Monster %d"

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
import spine_bake

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

#: The prism, which is the one charm that is a gem of its own rather than a mark worn by one.
#:
#: **The pack's own rainbow jewel, cut exactly as the four are.** A prism has no colour - it joins
#: a run of whatever is beside it (`SiegeCharm.Prism`) - so it is the one charm that cannot ride a
#: coloured gem, and the one that needs a picture the player reads as *a gem, and not one of the
#: four*. The pack draws a faceted stone shot through with every hue at once, by the same hand and
#: at the same size as the other eight; approximating one would be invariant 32b's fault paid for
#: nothing, when the exact picture is sitting in the same zip.
#:
#: Cut untouched, like the four: `Pal` never sees it, because a prism is not a colour and has
#: nothing to agree with.
PRISM_GEM = ("PNG/3.png", "a rainbow brilliant")

#: The other two charms, as **gems of their own** rather than as a mark worn over an ordinary one.
#:
#: **What was wrong before is that a charmed gem was an ordinary gem with a sticker on it.** The
#: first cut drew a white glyph over the four jewels (`charm_mark`, now gone) on the argument that
#: the colour underneath has to keep reading - which is true, and is answered by *cutting a
#: different jewel in the same colour* rather than by leaving the jewel alone and printing on it.
#: Reported in one sentence: *I told him to use new type of gems and he literally used the existing
#: gems and put icon on them.* A mark is also the weaker reading of the two on its own terms: it is
#: forty pixels of drawing over a saturated stone, where a silhouette is what the eye separates at
#: a glance on a board that also has a hill walking down it (invariant 37f's three readings, and
#: 34f's rule that pieces differ in silhouette as well as in hue).
#:
#: **Hue-rotated into each of the four rather than cut in one colour**, so a charm is still worth
#: the colour it is and a player can still see which ward it feeds. The pull is higher than a
#: raider's: these two stand *beside* the four gems the colour rule is defined by, so a stone that
#: came out pink where the gem beside it is poppy would be inventing a fifth colour on the one
#: board that cannot have one.
#:
#: **Why these shapes** (invariant 37z asked of a forty-pixel picture). A **stormglass** makes the
#: line fire at everything on the hill, so it is a *vortex orb*: round, which none of the four are,
#: with a spiral drawn into it that says something is turning. A **furnace** banks a charge on a
#: turret, so it is the one stone in the pack that is *rough*: a cracked nugget with light in the
#: fissures, which reads as a coal where the cut stones read as jewels. Neither can be mistaken for
#: a heart, a cabochon, a rhombus or an emerald-cut, which is the whole test.
#:
#: **The lance, the hourglass and the anvil are not here any more, and that is not a withdrawal.**
#: All three are drawn art now - a star, an hourglass and a clock face, supplied in all four board
#: colours - so they are cut by `Tools/make_charm_gems.py` from the owner's own folder and nothing
#: hue-rotates them. This table names only what is still cut from the pack, so the two tools never
#: write the same file and both `--check` runs stay honest. Their addresses are unchanged
#: (`Siege/gem_lance_r` and the rest), which is why the swap cost no Addressables work.
CHARM_GEMS = {
    "storm": ("23.png", "a vortex orb"),
    "furnace": ("22.png", "a cracked molten nugget"),
}

#: What a colossus's boulder leaves on a post: one rough grey stone out of the gem pack, cut
#: once and drawn three times at three sizes by the view (`SiegeView.Rubble`). A cut rock rather
#: than three discs, for invariant 47i's reason - a shape assembled out of primitives has no
#: artist in it, and this one stands on a turret for as long as the player leaves it there.
RUBBLE_GEM = ("49.png", "a grey rough stone")

#: How far a charmed gem is carried onto the ward colour, and how hard its colour is pushed.
#:
#: **Measured rather than typed** - four rows of both charms in all four colours were cut at .80,
#: .95 and 1.0 and looked at beside the four plain gems. At .80 the red reads *pink*, which on a
#: board where the colour is the whole decision is a fifth colour; at 1.0 the pack's own warm and
#: cool notes collapse and the stone goes flat (invariant 37p). .95 is where both are true at once.
CHARM_PULL, CHARM_SAT, CHARM_FLOOR = 0.95, 0.55, 0.58

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

#: The ten **legendary** turrets, in `WardCatalog.Default` order, and the pack body each wears.
#:
#: **A different pack, and that is the point rather than a convenience.** `WARD_MODELS` is twenty
#: machines out of one kit so that twenty read as a set (see `MERGE`); a legendary has to read as
#: *not one of them* from across a shelf, and the surest way to say that is a second hand. These
#: are the merge-shooter pack's own turrets - the same zip the bullets and the muzzle flash
#: already come from - drawn top-down, facing up the hill, with a ladder built into them exactly
#: as the kit's is: `Turret01` is a twin-barrel box and `Turret10` is a horned, plated mount with
#: a crown on it.
#:
#: **They are cut uncoloured, which is the whole feature and not a saving.** Every other turret is
#: hue-rotated onto the seat it stands on (`hued`, `WARD_HUES`), because a ward's colour is the
#: rule the mode is about. A legendary wears none - it stands on any seat and fires at anything
#: (`WardModel.Legendary`) - so rotating one onto a seat's hue would be the picture telling a
#: player the opposite of the rule. What it keeps instead is the pack's own paint, which is four
#: or five colours a piece and reads on a green hill without help.
#:
#: **So this is one picture per model rather than four**, and one recoil rather than four: the
#: addresses are `Siege/Wards/{id}` and `Siege/Wards/{id}_fire`, which is what `WardModel.ArtFor`
#: builds for a colourless turret and what both content gates walk.
LEGEND_MODELS = (
    ("tempest",    "Turret01"),
    ("ricochet",   "Turret02"),
    ("pyroclast",  "Turret03"),
    ("permafrost", "Turret04"),
    ("sunderer",   "Turret05"),
    ("stasis",     "Turret06"),
    ("railgun",    "Turret07"),
    ("starfall",   "Turret08"),
    ("wellspring", "Turret09"),
    ("eclipse",    "Turret10"),
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

#: How tall the gravemaw is cut, and it is the height `SiegeView.TallOf` draws it so nothing here
#: is ever upscaled. 3.2 cells, which puts it between the warlord and the warbringer on the ladder
#: a player reads first.
#:
#: **The bonecaller has no entry here**, because it is the one boss in this mode this tool does not
#: cut - see `BOSS_SET`.
GRAVEMAW = 350

#: How tall the three bosses that were **re-cut from 2D** are, in pixels.
#:
#: <b>They were baked out of rigged 3D until this drop and carried a discount for it.</b> A
#: reel trimmed to its own alpha fills about 0.93 of its frame where a 2D cut fills two
#: thirds, so the three baked bosses were given a *smaller* `SiegeTuning.TallOf` than the
#: 2D five to land on the same drawn body. The bake is gone, so the discount goes with it
#: and all eight bosses are back on one ladder - a simplification rather than a retune:
#: what a player reads first about which boss has arrived is that ladder, and a ladder with
#: two scales in it has a step nobody can see.
#:
#: The numbers are `SiegeTuning.TallOf` at this file's own 114 pixels a cell, exactly as
#: every row above: a shackler at 3.1 cells, a bonecaller at 3.4 and an ironclad at 3.5, so
#: nothing is ever upscaled by the view.
SHACKLER, BONECALLER, IRONCLAD = 350, 390, 400

#: How tall the fifth chapter's two are cut: `SiegeTuning.TallOf` at 114 pixels a cell, as every
#: row above. A thunderer stands beside the other rung-five bosses at 3.3 cells; a colossus tops
#: the whole mode at 3.8, because it is the widest body in the unit packs and the fifth chapter's
#: finale.
THUNDERER, COLOSSUS = 376, 433

#: How tall the sixth chapter's two are cut: `SiegeTuning.TallOf` at 114 pixels a cell, as every
#: row above. A gorgon stands a shade over the other rung-five bosses at 3.4 cells - the snakes
#: project sideways rather than upward, so the body reads wider than it is tall and needs the
#: height to hold its own against a hill of plate. A sunlord is 3.6, under a colossus and over
#: everything else: a finale, and a man rather than a giant.
GORGON, SUNLORD = 388, 410

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
#: <b>The two the third chapter brings, and they are the first pair here that had to be chosen
#: against a cast rather than against each other.</b> The bone cast is white, so a boss standing in
#: front of it may be anything except white: the <b>gravemaw</b> is the fifth blob this table
#: always held in reserve ("a fifth verb one day gets a body without a purchase"), a green barrel
#: with one eye and a mouth, which is what a thing that <em>eats what the hill drops</em> should
#: look like; the <b>bonecaller</b> is the survival pack's own caster, and a robed figure with a
#: staff standing at the head of a skeleton horde needs no explaining at all.
#:
#: <b>The bonecaller, the shackler and the ironclad are in this table now, and for a while
#: they were not.</b> All three were rendered out of rigged 3D on the grounds that there was
#: nothing left to cut: "the monster packs hold eighty-odd small cartoon blobs and none of
#: them is boss-shaped". That survey was wrong twice - it counted the `v1` to `v4` packs as
#: side-view when they are head-on, and it never opened them. See `MONS_V1`.
BOSS_SET = {
    "blight":  dict(pack=MONSTERS, body=WARLORDS % 2, cast=None, tall=BLIGHT),
    "boss":    dict(pack=MONSTERS, body=WARLORDS % 3, cast=None, tall=BOSS),
    "bringer": dict(pack=MONSTERS, body=WARLORDS % 5, cast=None, tall=WARBRINGER),
    "over":    dict(pack=MONSTERS, body=WARLORDS % 4, cast=None, tall=OVERLORD),

    "maw":     dict(pack=MONSTERS, body=WARLORDS % 1, cast=None, tall=GRAVEMAW),

    # ------------------------------------------------- the three that stopped being baked
    #
    # **These were rendered out of rigged 3D and are cut from flat packs now**, which is the
    # one entry in this table that undoes a decision rather than making one. The argument
    # for the bake was that there was nothing left to cut - and there were twenty unused
    # head-on bodies in the four monster packs this file had written off as side-view (see
    # `MONS_V1`). The owner withdrew the baked bodies; measured afterwards they also read as
    # *smudges* beside the bone cast, because a low-poly render carries no outline and every
    # 2D body in this game does.
    #
    # **Each is chosen against the cast it stands in front of** - this table's own rule, and
    # the one the warbringer was re-picked for. A bonecaller faces white bone, so it is deep
    # blue and horned; a shackler and an ironclad stand in front of the rabble, so neither
    # may be a small upright body carrying a prop.
    #
    # **And all three carry a real cast reel rather than `pulse`'s synthesised rear-up**,
    # which is the first time this table has had one: each of these packs draws an `Attack`.
    # A bought gesture beats a generated one (see `boss_reels`), and the three were picked
    # for what each gesture *does* - a horned caster throwing its arms up, a tongue lashing
    # out of a mouth, a helmed body lunging - because a boss is told apart by its verb and a
    # verb has to be drawn (invariant 37z).

    # ---------------------------------------------- the three cut from the top-down units
    #
    # **All three take `pulse`'s synthesised rear-up, and these packs draw a real attack.**
    # That looks like the wrong way round - a bought gesture beats a generated one, and this
    # table says so - so it is the one decision here that is a measurement rather than a
    # preference. `boss_reels` warns about it directly: the canvas is shared, whatever the
    # gesture reaches sets the frame, and the view sizes a body by its *frame*, so a reel that
    # flings something far out "draws the boss smaller for its whole life in exchange for a few
    # frames of reach". These three fling further than the blobs do, and it is not a small
    # effect. Frame fill, and the body it leaves on the ladder `SiegeTuning.TallOf` sets:
    #
    #     reel                    real attack        pulse
    #     bonecaller       0.65 -> 2.21 cells   0.73 -> 2.48
    #     shackler         0.65 -> 2.01         0.73 -> 2.28
    #     ironclad         0.60 -> 2.10         0.73 -> 2.55
    #
    # With the bought gesture an **ironclad draws the same body as a blightcaller** - the
    # finale of the mode tying its gentlest boss - because an overhead axe is mirrored on both
    # axes and takes a fifth of the frame with it. With the pulse all eight sit on one
    # monotonic ladder ending where `TallOf` says it ends, and the construction is the same one
    # the five blobs use. The Front-Attacking reels stay unused in the packs; the day
    # `one_canvas` can size a cast reel apart from a stand reel without the body changing size,
    # they are three rows away.

    # **A skeleton at the head of a skeleton horde**, which is the one boss here whose body
    # names its own wave rather than standing apart from it. That is a departure from this
    # table's rule and it is deliberate: the rule exists so a boss is not read as *a bigger
    # copy* of the raider, and the separation here is carried by the hat and the robe rather
    # than by being a different animal. A bonecaller raises the dead; what raises a skeleton
    # horde is a skeleton that got there first.
    "caller":  dict(pack=BONEUNITS, body=UNIT_WALK % "Skeleton Wizard",
                    cast=None, tall=BONECALLER),

    # **The god of the dead, over a rabble of the undead.** A shackler binds a ward rather
    # than breaking it, and Anubis is the one body in these packs whose whole myth is holding
    # the dead to a judgement. The ears are the point at this size: they project *sideways*,
    # which invariant 37bx says is the one thing a silhouette can do that survives this
    # camera, and black over a blue-green rabble is a value no raider in the chapter wears.
    "snare":   dict(pack=MYTH, body=UNIT_WALK % "Anubis",
                    cast=None, tall=SHACKLER),

    # **Full plate, because the mechanic is armour.** An ironclad is the one boss in this
    # mode whose rule is its shell - only the ward wearing its colour may touch it - so the
    # body has to say plating before the first bolt bounces off it, and a horned helm over a
    # banded cuirass says it without a word. It is also the heaviest silhouette on the shelf,
    # which is what the last rung of the last chapter should be.
    "clad":    dict(pack=BOSSPACK2, body=UNIT_WALK % "Armored Ogre",
                    cast=None, tall=IRONCLAD),

    # ------------------------------------------------ the two the fifth chapter brings
    #
    # **Both chosen against the wild** - two stone bulwarks, a yeti, a minotaur and a mud clod,
    # every one of them a monster - so the two bosses standing in front of them are the two
    # bodies in these packs that are plainly *not* monsters: a god and a giant.
    #
    # **Zeus, because the verb is lightning.** A thunderer drains the line's banked charges and
    # throws them back, and the one body on this shelf whose whole picture is a thunderbolt in a
    # raised hand needs no explaining. White robes over a hill of stone and moss is a value no
    # raider in the chapter wears.
    "thunder": dict(pack=ANCIENTS, body=UNIT_WALK % "Zeus",
                    cast=None, tall=THUNDERER),

    # **The cyclops, because the verb is a boulder.** A colossus hurls a rock that buries a
    # ward, so it is the widest body in the packs with the heaviest club - and one eye is the
    # silhouette that survives this camera: it projects nothing sideways and does not need to,
    # because the head is a third of the body. Drawn 3.8 cells, the biggest thing in the mode.
    "colossus": dict(pack=BOSSPACK2, body=UNIT_WALK % "Cyclops",
                     cast=None, tall=COLOSSUS),

    # ------------------------------------------------ the two the sixth chapter brings
    #
    # **Both chosen against the court**, this table's standing rule: the sixth cast is three
    # robed wizards, a hooded archer, a bone knight and a falcon-headed warrior - six bodies
    # that are all, in one way or another, *people*. So the two standing in front of them are
    # the two bodies in these packs that plainly are not: a monster and a king.
    #
    # **Medusa, because the verb is a look.** A gorgon's glare turns a ward stone-struck - it
    # keeps firing and lands nothing - and the one body on this shelf whose entire myth is what
    # happens when you meet its eye needs no explaining. The snakes are the point at this size:
    # they project *sideways*, which invariant 37bx says is the one thing a silhouette can do
    # that survives this camera, and green over a hill of bone and sand is a value no raider in
    # the chapter wears.
    "gorgon": dict(pack=MYTH, body=UNIT_WALK % "Medusa",
                   cast=None, tall=GORGON),

    # **The pharaoh, because the verb is a sentence.** A sunlord seals a ward and gives the
    # player a deadline to answer it, which is a thing only something with *authority* can
    # plausibly do - and this is the one body in these packs drawn as a ruler rather than as a
    # fighter. It is also the widest silhouette left after the cyclops: the nemes headdress is
    # a triangle half as wide as the body, which is what carries it at the top of the hill.
    "sunlord": dict(pack=ANCIENTS, body=UNIT_WALK % "Pharaoh",
                    cast=None, tall=SUNLORD),
}

# **Every boss in this mode is cut here now, and for one drop three of them were not.** The
# bonecaller, the shackler and the ironclad were baked out of rigged 3D by an Editor tool
# because this file had recorded that there was nothing left on the machine to cut them from.
# There was: twenty unused head-on bodies in four monster packs (`MONS_V1`). The bake is
# withdrawn, the models are off disk, and the whole of what it cost to undo was three rows in
# the table above - which is the bargain a table is for.
#
# **One tool owns one folder**, which is the rule that note really established: two tools
# writing `Art/Siege/caller` means `--check` fails against whichever ran last.

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

#: The third chapter's twelve raiders: five skeletons over three kinds. See `WIZARD` for why five
#: bodies is a different bargain from fifteen and what this cast says instead.
#:
#: <b>Which body is which kind is the pack's own drawing and needed no interpretation.</b> One of
#: the five wears nothing at all - a bare rib cage and a bone in its hand - so it is the creeper,
#: and four of it is a swarm. Two carry a long weapon over a helm, a <b>flail</b> and a
#: <b>scythe</b>, so they are the brutes. Two are <b>plated</b>: one holds a round wooden
#: <b>shield</b> and one has a closed visor over a mailed body, so they are the bulwarks - and the
#: shield is the more literal of the two on purpose, because the one thing a player has to read
#: about a bulwark before it is in range is that it is carrying something (`SiegeTuning.TallOf`).
#:
#: <b>Where a body is worn twice, the two colours it wears are opposite ones.</b> The flail is red
#: and blue and the scythe green and amber; the shield red and blue and the visor green and amber.
#: Pairing them as red-and-amber would put one silhouette on the two hues this mode's palette
#: already keeps closest together (37ak's 32 degrees), which is the one place a doubled body could
#: actually cost a reading.
BONE_SET = {
    # creepers - the one bare skeleton in the pack, four times, which is what a swarm of bones is
    "boneMon_r":     BONES % 2,
    "boneMon_g":     BONES % 2,
    "boneMon_b":     BONES % 2,
    "boneMon_y":     BONES % 2,

    # brutes - the two carrying a long weapon: a spiked flail and a scythe
    "boneBrute_r":   BONES % 3,
    "boneBrute_g":   BONES % 5,
    "boneBrute_b":   BONES % 3,
    "boneBrute_y":   BONES % 5,

    # bulwarks - the two wearing plate: a round shield, and a closed visor over mail
    "boneBulwark_r": BONES % 1,
    "boneBulwark_g": BONES % 4,
    "boneBulwark_b": BONES % 1,
    "boneBulwark_y": BONES % 4,
}

#: The **fourth** chapter's twelve raiders: six bodies, every one of them walking toward you.
#:
#: <b>Facing is the first question and it outranks everything else.</b> Every cast this game
#: ships is drawn square-on - an insect from directly above, a blob and a skeleton head-on - so
#: each body is mirror-symmetric about its own middle and reads as coming *down the hill at the
#: player*. This pack is the only one on the machine drawn that way (see `FIELD`), and the cast
#: that shipped before it was not: a first replacement was chosen out of a high-resolution pack
#: drawn in three-quarter and profile, and the owner withdrew it on sight. **Sharpness was
#: weighed above facing and that is the wrong way round** - an upscale costs a flat cartoon body
#: inside a heavy outline very little, and no resolution recovers a body that is facing the wrong
#: way.
#:
#: <b>Six bodies of the ten, and the four left out are a framing decision rather than a taste
#: one.</b> `SiegeView` sizes a body by its *frame*, so a silhouette carrying something tall above
#: its head is drawn with the creature itself small: measured, the two balloon bodies stand 98
#: pixels of which barely half is zombie, against 53 to 73 for the six below where the body fills
#: 0.92 to 0.95 of the frame. Those four would walk down the hill at about six tenths of the size
#: of the rest of their own wave.
#:
#: <b>The kind is said by what a body is wearing or carrying</b>, which is this pack's own
#: drawing and needed no interpretation: a **manhole cover** and a **padded helmet** are the two
#: that are plainly armoured, so they are the bulwarks; a **sledgehammer** and a tall **bearskin**
#: are the heaviest silhouettes, so they are the brutes; and the two slightest - bare-headed, and
#: a thin traffic cone - creep. Two bodies per kind is `BONE_SET`'s bargain, with its rule: where
#: a body is worn twice the two colours it wears are opposite ones, because pairing red with
#: amber would put one silhouette on the two hues this palette keeps closest together (37ak).
#:
#: <b>What this cast costs is the upscale, and it is written down rather than glossed</b>: these
#: reach `CAST` at 2.5x to 3.4x where the insects reach it at 1.6x and the skeletons are cut
#: *down*. It is the worst in the mode and it was accepted with that known.
RABBLE_SET = {
    # creepers - the two slightest, wearing nothing that reads as armour
    "rabbleMon_r":     5,
    "rabbleMon_g":     2,
    "rabbleMon_b":     5,
    "rabbleMon_y":     2,

    # brutes - the heaviest silhouettes: a sledgehammer, and a tall bearskin
    "rabbleBrute_r":   3,
    "rabbleBrute_g":   6,
    "rabbleBrute_b":   3,
    "rabbleBrute_y":   6,

    # bulwarks - the two carrying plate: a manhole cover held up, and a padded helmet
    "rabbleBulwark_r": 1,
    "rabbleBulwark_g": 4,
    "rabbleBulwark_b": 1,
    "rabbleBulwark_y": 4,
}

#: How hard this cast's saturation is floored, against `CAST_SAT_FLOOR`'s 0.30 for the other two.
#:
#: <b>A fact about what the pack paints rather than a preference, and it is the first cast here
#: that needed one.</b> `hued` pushes saturation rather than setting it - a pixel that was grey
#: metal stays greyish and a coloured one becomes strongly coloured - which is exactly right for a
#: monster drawn in two or three colours of its own. These bodies are <b>bone white</b>: they carry
#: no hue at all, so the floor <em>is</em> the colour, and at 0.30 a red skeleton and an amber one
#: are two pale creams half a hue apart. On a board where the colour of a raider is the whole
#: mechanic that is not a look, it is a rule that cannot be read.
#:
#: <b>0.60, rendered against 0.30, 0.45 and 0.75 to pick it.</b> Below it the creepers - the only
#: body in the pack wearing nothing but bone - stay washed; above it the skull stops having a skull
#: in it, which is the flattening `CAST_PULL`'s note is about arriving through saturation instead.
BONE_SAT_FLOOR = 0.60

#: How the unit packs name a body's two reels, relative to the body's own folder - the same
#: two paths `UNIT_WALK` and `UNIT_CAST` spell out for a boss, in the shape `walk_and_swing`
#: wants them.
UNIT_WALK_ANIM, UNIT_SWING_ANIM = "/PNG/PNG Sequences/Front - Walking", "/PNG/PNG Sequences/Front - Attacking"

#: And the one body on the whole shelf that draws its attack under another name, because it has
#: a bow rather than a weapon. See `COURT_SET`.
UNIT_SHOOT_ANIM = "/PNG/PNG Sequences/Front - Shooting"


#: The **fifth** chapter's twelve raiders: five top-down bodies out of the unit packs, and a
#: second reel per body cut from the packs' own `Front - Attacking`.
#:
#: <b>The kind is said by what a body is made of</b>, which is this pack family's own drawing
#: and needed no interpretation: two bodies are *stone* - a cracked grey golem and a blue ice
#: golem, plated head to foot - so they are the bulwarks; the two carrying a club and an axe,
#: the yeti and the minotaur, are the heaviest silhouettes and the brutes; and the mud clod,
#: round and soft with moss on it, is the one body here that reads as something that comes in
#: numbers, so it creeps.
#:
#: <b>Where a body is worn twice the two colours it wears are opposite ones</b> - `BONE_SET`'s
#: rule, for `BONE_SET`'s reason: the yeti is red and blue and the minotaur green and amber, the
#: rock red and blue and the ice green and amber, so no one silhouette sits on the two hues this
#: palette keeps closest together (37ak).
#:
#: <b>Every body faces the camera and walks toward it</b>, which is the property that decided
#: the cast before anything else did (`survey`, 37db) - these packs draw Front, Back, Left and
#: Right, and Front is the one this board wants. And every one of them carries a real attack,
#: cut on the walk's scale (`walk_and_swing`), so the swing is bought rather than built.
WILD_SET = {
    # creepers - the mud clod, four times, which is what a swarm of the hill's own ground is
    "wildMon_r":     (BOSSPACK3, "Earth Monster"),
    "wildMon_g":     (BOSSPACK3, "Earth Monster"),
    "wildMon_b":     (BOSSPACK3, "Earth Monster"),
    "wildMon_y":     (BOSSPACK3, "Earth Monster"),

    # brutes - the two carrying a weapon: a yeti with a club, a minotaur with an axe
    "wildBrute_r":   (BOSSPACK2, "Yeti"),
    "wildBrute_g":   (ANCIENTS, "Minotaur"),
    "wildBrute_b":   (BOSSPACK2, "Yeti"),
    "wildBrute_y":   (ANCIENTS, "Minotaur"),

    # bulwarks - the two made of stone: a cracked rock golem and a plated ice golem
    "wildBulwark_r": (BOSSPACK3, "Rock Monster"),
    "wildBulwark_g": (BOSSPACK3, "Ice Monster"),
    "wildBulwark_b": (BOSSPACK3, "Rock Monster"),
    "wildBulwark_y": (BOSSPACK3, "Ice Monster"),
}

#: How hard the wild's saturation is floored, between the bone cast's 0.60 and the others' 0.30.
#:
#: <b>Measured off the two extremes in the set</b>: the rock golem and the yeti are nearly grey
#: and white, so at 0.30 a red rock and an amber rock are two greys half a hue apart (the bone
#: cast's finding); the minotaur and the mud clod carry their own brown and green, so at 0.60
#: the moss and the fur stop having any shading in them. 0.48 is where a red golem is plainly
#: red and the yeti's fur still has a highlight.
WILD_SAT_FLOOR = 0.48

#: The **sixth** chapter's twelve raiders: six top-down bodies out of the unit packs, and a
#: second reel per body cut from the packs' own `Front - Attacking`.
#:
#: <b>The kind is said by what a body is wearing</b>, which is this pack family's own drawing and
#: needed no interpretation: three robed wizards and a hooded archer carry nothing that reads as
#: armour, so they creep; a falcon-headed war-god in scale and a pauldron is the heaviest
#: silhouette here, so it is the brute; and a skeleton in bone plate behind a shield is the one
#: body plainly *carrying* its armour, so it is the bulwark.
#:
#: <b>Four distinct creepers, which no cast before this one has had.</b> The insects manage four,
#: the brood four, the bones and the rabble two and the wild one; this chapter's swarm is four
#: different casters, and that is worth having precisely where the swarm is largest. Where a body
#: is worn twice the two colours it wears are opposite ones - `BONE_SET`'s rule, for `BONE_SET`'s
#: reason (37ak) - which is why the brute and the bulwark each take red and blue, then green and
#: amber, rather than any pairing that would put one silhouette on the two closest hues.
#:
#: <b>Two of the eight spare bodies are not here, and that is the boss table's doing</b>: Medusa
#: and the pharaoh stand in front of this cast rather than in it (`BOSS_SET`), which is the rule
#: that a boss may not be a raider drawn three times the size (invariant 37bd).
#: <b>A row may name its own attack, and the first cast here that needed to is why.</b> These
#: packs almost all draw `Front - Attacking`, and the skeleton archer draws `Front - Shooting` -
#: it has a bow, so the pack called it what it is. `walk_and_swing` answers an <em>empty</em>
#: swing for a folder that is not there and the build writes nothing, which is exactly the
#: shape of fault invariant 32b is about: the cast imported, addressed, audited and drew, and
#: one of the twelve simply never swung. It cost one missing folder in a `git status` to spot
#: and could as easily have shipped. `court_swings` refuses one now rather than warning.
COURT_SET = {
    # creepers - the three wizards and the archer: four robed bodies, nothing armoured
    "courtMon_r":     (WIZUNITS, "Wizard Male", UNIT_SWING_ANIM),
    "courtMon_g":     (WIZUNITS, "Wizard Female", UNIT_SWING_ANIM),
    "courtMon_b":     (WIZUNITS, "Wizard Veteran", UNIT_SWING_ANIM),
    "courtMon_y":     (BONEUNITS, "Skeleton Archer", UNIT_SHOOT_ANIM),

    # brutes - the war-god, the heaviest silhouette in these packs after the two bosses
    "courtBrute_r":   (MYTH, "Horus", UNIT_SWING_ANIM),
    "courtBrute_g":   (MYTH, "Horus", UNIT_SWING_ANIM),
    "courtBrute_b":   (MYTH, "Horus", UNIT_SWING_ANIM),
    "courtBrute_y":   (MYTH, "Horus", UNIT_SWING_ANIM),

    # bulwarks - bone plate behind a shield, which is the one body here carrying its armour
    "courtBulwark_r": (BONEUNITS, "Skeleton Knight", UNIT_SWING_ANIM),
    "courtBulwark_g": (BONEUNITS, "Skeleton Knight", UNIT_SWING_ANIM),
    "courtBulwark_b": (BONEUNITS, "Skeleton Knight", UNIT_SWING_ANIM),
    "courtBulwark_y": (BONEUNITS, "Skeleton Knight", UNIT_SWING_ANIM),
}

#: How hard the court's saturation is floored, between the wild's 0.48 and the bone cast's 0.60.
#:
#: <b>Measured off the two extremes in the set</b>, `WILD_SAT_FLOOR`'s method: the skeleton
#: knight is bone white and carries no hue at all, so a low floor leaves a red one and an amber
#: one two creams half a hue apart (the bone cast's own finding); Horus and the three wizards are
#: painted in strong robes of their own, so a high floor flattens the trim out of them. 0.55 is
#: where a red knight is plainly red and a wizard's robe still has folds in it.
COURT_SAT_FLOOR = 0.55


#: How many frames a swing keeps. See `SWING_ANIM`.
SWING_FRAMES = 6

#: How far a synthesised cast reel surges, and how far down the hill it leans.
#:
#: <b>A top-down beetle rears at the camera, so rearing is a change of size.</b> Three of the four
#: bosses have exactly one animation in the pack, and shipping them with no cast gesture at all
#: would have been the last verdict on this mode ("boring bosses") invited straight back - so the
#: reel is built: the body surges toward the viewer and a little down the hill over the same
#: `BossTell` window the ring and the crackle already fill, then settles. It is a real gesture
#: rather than a zoom because that is what the motion <em>is</em> from above; what would be a bug
#: is a boss that changed size and stayed changed, which is why the reel ends on the stand.
#:
#: <b>The shape of the gesture is a strike, not a sine, and the strike lands on the release.</b>
#: The first cut was a sine over the tell, which peaks half way through and is back on the
#: standing pose by the time the spell leaves the boss's hand - so at the one frame the drawing
#: has to say *thrown*, the body was doing nothing. A strike is three beats: a slow crouch
#: (`BOSS_CROUCH` smaller, leaning back up the hill) for the first half, a snap out to the full
#: rise over two frames, a hold at the peak that ends on the reel's second-to-last frame - which
#: `SiegeView.Cast` plays at `BossTell`, so the peak is the frame `Unleash` fires on - and a
#: recovery to the stand on the last frame, so the hand-back to the idle reel is not a snap.
#: The peak is the same number, but the canvas is the union of the frames that happen to sit at
#: it, so a re-cut can move how much of the frame a body fills by a few per cent - measure the
#: stand reel's alpha box before and after, and move `SiegeTuning.TallOf` by the ratio.
BOSS_RISE, BOSS_LEAN = 0.17, 0.05

#: How much smaller the body draws at the bottom of its crouch, and how far back up the hill it
#: leans there, as fractions of its own size.
BOSS_CROUCH, BOSS_RECOIL = 0.06, 0.03

#: Where the beats of the strike fall, as fractions of the reel: the crouch ends, the snap ends,
#: the hold ends. Everything after the hold is the recovery.
BOSS_BEATS = (0.50, 0.68, 0.90)

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


def charm_gem(im, hue):
    """One charmed gem: a jewel of its own, cut at the size the four are and painted one colour.

    **A different stone rather than a sticker on the same one**, which is the whole of what was
    wrong with the first cut - see `CHARM_GEMS`. The colour is carried nearly all the way, because
    these stand beside the four gems the colour rule is defined by; the *shape* is the pack's own
    and is what the player separates at a glance.

    Cut a shade larger than a plain gem (0.94 against 0.88): both of these are pointed or round
    where the four are broad, so fitted to the same box they draw visibly smaller than the stones
    beside them - which would say a charm is a lesser gem, and it is the opposite.
    """
    return fit(hued(im, hue, pull=CHARM_PULL, sat_gain=CHARM_SAT, sat_floor=CHARM_FLOOR),
               TILE, 0.94)


def rubble(im):
    """The stone a colossus leaves on a post: the pack's grey rock, graded to warm stone.

    Pulled all the way onto one warm hue at a low saturation, so it is plainly *rock* and plainly
    not one of the four gem colours - a boulder wearing red on a red turret would be a fifth way
    of saying red. Cut a little under a gem's room, because it is drawn three times over a
    chassis and the pile has to leave the turret readable under it.
    """
    return fit(hued(im, 0.08, pull=1.0, sat_gain=0.30, sat_floor=0.12, val_gain=0.88,
                    val_lift=0.0), TILE, 0.86)


def charm_ring():
    """The soft light a charmed gem stands in, drawn behind it and tinted at run time.

    **The second way a charm is said, and it exists because the first one is small.** A mark on the
    face of a jewel is forty pixels of drawing on a board that also has a hill walking down it; a
    halo is what makes the eye go there at all. Invariant 37f is about a raider saying its colour
    three times, and this is the same argument about a gem saying it is not an ordinary gem.

    White, so the view can put the charm's own colour on it - and a *ring* rather than a filled
    disc, because a disc behind a jewel washes the jewel out and the one thing this may not do is
    make the colour underneath harder to read.
    """
    size = TILE
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    wide = glow(size, size, size / 2, size / 2, size * 0.50, (255, 255, 255), 1.0, power=2.4)
    hole = glow(size, size, size / 2, size / 2, size * 0.34, (255, 255, 255), 1.0, power=2.4)

    a = np.asarray(wide).astype(np.float32)
    b = np.asarray(hole).astype(np.float32)
    a[..., 3] = np.clip(a[..., 3] - b[..., 3] * 0.92, 0, 255)

    out.alpha_composite(Image.fromarray(a.astype(np.uint8), "RGBA"))
    return out


#: Frames a lance's beam is drawn over. Ten at 30fps is a third of a second of crackle, which is
#: about as long as the stroke is at full brightness before it starts going out.
BEAM_FRAMES = 10


def beam():
    """The stroke a lance draws down its row and its column, as a **reel** rather than a bar.

    **A still bar is the whole of why the old one read as nothing.** It was one white gradient
    stretched across the row and faded out: it has no event in it, so what the player saw was a
    highlighter line appear and vanish over gems that were disappearing anyway. Reported as
    *horrendous*, and correctly. A beam is light under pressure - it has to flicker, it has to have
    filaments in it that move, and the core has to be hotter than the body.

    **Still white, still with no edge along its length**, because the view tints it to the charm's
    own colour and stretches it to whatever a field is wide (invariant 37au is about a picture of a
    *place*, which this is not; a beam is a thing of variable length and its sprite is drawn with
    no end along that axis precisely so it may be stretched).

    **Cut at half the old resolution on purpose.** `ArtImportRules` caps `/Art/Siege/` at 512, so a
    1536-wide strip imported at 512 and was blown back up by the view - ten frames of that is ten
    textures paying for detail the importer had already thrown away. This is cut at the size it
    ships at, which is the same rule the hill's ground learned the hard way.

    The crackle is a pair of counter-running sine sums rather than noise, for one reason: noise
    reseeded per frame boils, and what a beam does is *travel*. Two waves running opposite ways at
    different rates give a filament that moves along the beam and never repeats inside the reel.
    """
    long, thick = 512, 64
    y, x = np.mgrid[0:thick, 0:long].astype(np.float32)

    # Along: full for the middle and feathered into nothing at both ends, so two strokes crossing
    # at the charm do not show a seam and a stroke that overruns the field has no visible end.
    fade = long * 0.06
    along = np.clip(np.minimum(x, long - 1 - x) / fade, 0.0, 1.0)

    mid = thick / 2.0
    frames = []

    for f in range(BEAM_FRAMES):
        phase = math.tau * f / BEAM_FRAMES

        # The filament: where the hot thread of the beam actually lies, which is not the middle.
        # Two waves running opposite ways, so the thread crawls along the stroke.
        thread = (np.sin(x / 26.0 - phase * 2.0) * 3.4
                  + np.sin(x / 11.0 + phase * 3.0) * 2.1
                  + np.sin(x / 61.0 - phase) * 2.8)

        off = np.abs(y - mid - thread)

        # Three rungs, far apart, which is what makes a thing look *made of* light rather than
        # painted it (the strike's own ladder, invariant 37af): a white filament, a bright body,
        # and a wide haze the board can see the colour in.
        core = np.clip(1.0 - off / 2.6, 0.0, 1.0) ** 1.3
        body = np.clip(1.0 - np.abs(y - mid) / (thick * 0.21), 0.0, 1.0) ** 1.7
        haze = np.clip(1.0 - np.abs(y - mid) / (thick * 0.46), 0.0, 1.0) ** 2.4

        # The whole stroke breathes, so a beam held for a third of a second is never still.
        pulse = 0.86 + 0.14 * math.sin(phase * 2.0)

        a = np.zeros((thick, long, 4), np.float32)

        # White at the filament and a shade off it in the haze, so the run-time tint has something
        # to colour: a wholly white sprite multiplied by a hue is that hue everywhere, and the
        # thing that says "hot" is the part the tint cannot reach.
        lit = np.clip(core * 1.15, 0.0, 1.0)
        a[..., 0] = 255.0
        a[..., 1] = 255.0
        a[..., 2] = 255.0
        a[..., 3] = np.clip((core * 1.0 + body * 0.62 + haze * 0.34) * pulse, 0.0, 1.0) * along * 255.0

        # The core written back over the top, so the middle of the stroke is solid white whatever
        # the alpha sum came to - a beam whose brightest pixel is 80% is a beam with no filament.
        a[..., 3] = np.maximum(a[..., 3], lit * along * 255.0)

        frames.append(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"))

    return frames


#: Frames the hourglass's wavefront is drawn over.
#:
#: **Twenty-four, and the rate is no longer written down here.** It was twelve at 30fps because
#: that came to exactly the old `SiegeView.StillSweep` - which pinned the reel's length to a
#: constant in another file and quietly broke the moment the sweep was slowed. The view now derives
#: the rate from the reel (`frames.Length / StillSweep`), so this count is free to be whatever the
#: boil needs and the reel still plays through exactly once however long the wave takes.
STILLWAVE_FRAMES = 24


def noisefield(h, w, seed, rows, cols):
    """A smooth pseudo-random field: a small seeded grid blown up to the frame.

    **The one thing sines cannot do.** Everything in this file that has to look turbulent was
    built out of trigonometry, and a product of periodic functions is periodic however cleverly the
    wavelengths are chosen - which is how `heavefront` came to draw its embers as a lattice of
    neat dashes twice, once in x alone and once in both axes. A seeded grid has no period at all,
    and seeding it is what keeps `--check` able to reproduce the reel byte for byte.
    """
    rng = np.random.RandomState(seed)
    small = (rng.rand(rows + 1, cols + 1) * 255.0).astype(np.uint8)
    return np.asarray(Image.fromarray(small, "L").resize((w, h), Image.BICUBIC),
                      dtype=np.float32) / 255.0


def ramp(t, stops):
    """A colour ramp along `t`: `(position, rgb)` stops, interpolated.

    **Every front in this mode is painted rather than tinted now, and this is the whole of how.**
    A sprite cut white and lent a colour by the view can only ever be one hue multiplied down
    (invariant 37l: `Image.color` is a multiply), so its bright half is the *tint's* colour and
    its dim half is that colour going grey - which is precisely the "smokey, dead white" the two
    charm fronts were reported as. A ramp gives a front a hot core and a deep saturated body that
    are **different hues**, which is what fire and glass actually do and what no single tint can
    reach. The view lends `Color.white` so the paint survives, exactly as `Hurl` does for a boss
    spell.
    """
    pos = np.array([p for p, _ in stops], np.float32)
    cols = np.array([c for _, c in stops], np.float32)

    out = np.zeros(t.shape + (3,), np.float32)
    for i in range(3):
        out[..., i] = np.interp(t, pos, cols[:, i])
    return out


#: The hourglass's wall, front to back: a white-hot face, then lit glass, then saturated azure,
#: then the deep indigo it trails off into.
#:
#: **Azure is `Pal.Azure` exactly**, because the stop is the one payoff that hangs on the hill for
#: three seconds and a front that was nearly the board's blue would read as a fifth colour on the
#: one board that cannot have one (the charmed stones' own rule, said about an effect).
STILL_RAMP = (
    (0.00, (255, 255, 255)),
    (0.10, (222, 250, 255)),
    (0.30, (150, 232, 255)),
    (0.62, (79, 193, 255)),
    (0.86, (58, 110, 226)),
    (1.00, (34, 44, 140)),
)


def stillwave():
    """The wall of stopped time an hourglass sends up the hill.

    **It was the stormglass's `laser` stretched to the width of the board**, and that is the whole
    complaint: a laser is a *bar*, flat at full alpha across its middle, drawn to be a beam of
    variable length. Swept up a hill as a wavefront it is a blue stripe sliding past - it has no
    leading edge, nothing in it that reads as glass, and nothing that says *time*. It is `beam`'s
    own lesson (a still gradient has no event in it) arriving on the one charm whose whole payoff
    is a moment.

    **So this is a front rather than a band.** Three things make it one:

    * a **hard hot edge at the top**, which is the direction it travels - a wavefront without a
      leading edge is a smear;
    * **graduations** hanging off that edge at a fixed pitch, longer every fourth, which is the
      one piece of vocabulary that says *clock* rather than *frost*. Nothing else on this board is
      regularly spaced, so it reads as a made thing;
    * **shards** of varying height behind them, on a fixed ragged profile, so the body is
      crystalline rather than a gradient.

    **And it crystallises rather than arriving whole.** `grow` opens the graduations and the shards
    over the first half of the reel, so what the player sees is time *freezing* across the hill
    rather than a shape being flown across it.

    **And it is painted rather than tinted, which is the second complaint and a separate fault.**
    Cut white and lent `Pal.Glass` - a near-white `#DCEBF5` - the whole wall was one pale hue
    multiplied down, so its bright half was off-white and its dim half was grey: reported as
    *smokey, dead white*. A front has a hot core and a cold deep body and those are two different
    hues, which no single multiply can reach (invariant 37l is about the four *ward* colours, where
    one drawing has to become four; this is one drawing and may have its own paint, exactly as a
    boss spell does). So the depth carries `STILL_RAMP` - white face, lit glass, saturated azure,
    deep indigo - and the view lends `Color.white`.

    **It is also much thicker.** The body ran out at half the frame and the rest was haze, so
    two thirds of what crossed the hill was a wash. A wall of stopped time is *solid*: the body
    now reaches four fifths of the way back at nearly full alpha, faceted in **both** axes so the
    depth is crystal rather than a gradient.
    """
    long, thick = 512, 128
    y, x = np.mgrid[0:thick, 0:long].astype(np.float32)

    # 0 at the leading edge, 1 at the trailing one. The view sweeps it upward, so the top of the
    # sprite is the front.
    d = y / (thick - 1.0)

    # Feathered into nothing at both ends, so a front stretched past the field has no drawn end.
    fade = long * 0.045
    along = np.clip(np.minimum(x, long - 1 - x) / fade, 0.0, 1.0)

    # Where the graduations stand and how far back each one reaches. Every fourth is a long one,
    # which is what a dial's face does and what stops the pitch reading as a fence.
    pitch = 17.0
    step = np.minimum(x % pitch, pitch - (x % pitch))
    across = np.clip(1.0 - step / 2.6, 0.0, 1.0)
    longer = (np.floor(x / pitch) % 4.0) < 0.5
    reach = np.where(longer, 0.58, 0.31)

    # The ragged back edge: two long waves multiplied, so the shards differ all along the front
    # and the pattern never repeats inside the width.
    ragged = 0.30 + 0.26 * (np.sin(x / 7.3) * 0.5 + 0.5) * (np.sin(x / 23.0 + 0.6) * 0.5 + 0.5)
    comb = 0.55 + 0.45 * np.sin(x / 3.1) ** 2

    frames = []

    for f in range(STILLWAVE_FRAMES):
        u = f / (STILLWAVE_FRAMES - 1.0)
        phase = math.tau * f / STILLWAVE_FRAMES

        # The front freezing outward over the first third of the reel, then holding.
        grow = min(1.0, 0.34 + 2.4 * u)

        # **A solid bar at the front, not a feathered edge.** This was a soft falloff and it read
        # as a haze sliding past; what a wall of stopped time needs is a *face* - flat at full
        # alpha for a real depth, then a shoulder. `laser`'s own lesson about a bar, applied to the
        # one thing on this board that genuinely is one.
        # **Thin, and it was not.** The face ran to a seventh of the frame with a shoulder behind
        # it, which over a front drawn one and a half cells deep is a white stripe half a screen
        # wide - so the wall that had just been painted azure arrived reading as the white bar it
        # replaced. A leading edge is an *edge*: what carries the colour is the body behind it.
        flat = np.clip(1.0 - np.clip((d - 0.055) / 0.055, 0.0, 1.0), 0.0, 1.0)
        edge = np.clip(1.0 - d / 0.19, 0.0, 1.0) ** 1.35
        ticks = across * np.clip(1.0 - d / np.maximum(1e-3, reach * grow), 0.0, 1.0) ** 0.95
        shard = comb * np.clip(1.0 - d / np.maximum(1e-3, ragged * grow), 0.0, 1.0) ** 1.5

        # **The body, and it is the thick half of the complaint.** It ran out at `0.52` on a
        # square-ish curve, so the back two thirds of the wall was haze and what crossed the hill
        # was a lit edge with nothing behind it.
        body = np.clip(1.0 - d / 0.82, 0.0, 1.0) ** 1.05
        haze = np.clip(1.0 - d, 0.0, 1.0) ** 2.0

        # **Facets, varying in both axes** - `heavefront`'s own lesson, which was learned the hard
        # way over there: anything built out of `f(x)` alone is a picket fence however finely it is
        # tuned. These lean back with depth and drift across the reel, so the wall is *crystal*
        # rather than a gradient and is plainly moving even while it is held.
        facet = np.clip(0.70 + 0.30 * np.sin(x / 9.7 + d * 6.1 + phase)
                                   * np.sin(x / 31.0 - d * 3.3 - phase * 0.5), 0.0, 1.0)

        # A shimmer running along the front, so a wave held for a beat is never still.
        run = 0.93 + 0.07 * np.sin(x / 19.0 - phase * 2.0)

        # **One surge travelling the width of the face over the reel.** The front is a straight
        # line half a screen wide, and a straight line with nothing moving along it is furniture;
        # this is the one feature that says the wall is carrying something.
        travel = (x / long) - (u * 1.36 - 0.18)
        pulse = np.clip(1.0 - np.abs(travel) / 0.16, 0.0, 1.0) ** 2.0

        lit = np.clip(flat * 1.00 + edge * 0.95 + ticks * 1.30 + shard * 1.15
                      + body * facet * 0.98 + haze * 0.44
                      + pulse * np.maximum(flat, edge) * 0.55, 0.0, 1.0)

        a = np.zeros((thick, long, 4), np.float32)

        # **The paint: cold and saturated behind, white where it is hottest.** The ramp is read off
        # the depth and then burned toward white by how lit each pixel is, so the face and the
        # graduations go white-hot while the body stays glass - which is the two-hue reading a
        # tint cannot produce.
        heat = np.clip(flat + edge * 0.70 + ticks * 0.90 + pulse * 0.80, 0.0, 1.0)
        rgb = ramp(np.clip(d / 0.90, 0.0, 1.0), STILL_RAMP)
        a[..., :3] = rgb + (255.0 - rgb) * (0.80 * heat ** 2.1)[..., None]

        a[..., 3] = lit * run * along * 255.0

        # The face written back over the top at full, so the front is solid whatever the sum came
        # to - `beam`'s rule about a stroke whose brightest pixel is 80%, and the whole of why this
        # reads as a wall rather than as a glow.
        a[..., 3] = np.maximum(a[..., 3], np.maximum(flat, edge * 0.85) * along * 255.0)

        frames.append(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"))

    return frames



# ---------------------------------------------------------------------- the sixth chapter's spells
#
# **Drawn here rather than baked in the Editor, and that is a decision with a bill behind it.**
# Every boss spell before these six came out of `SiegeShotBake`, which renders a licensed particle
# pack through a camera - and the fifth chapter's two are *still owed* months later, because a
# batch-mode bake of that pack renders every frame shader-pink and only a running Editor can cut
# them (`CLAUDE.md`'s Owed list). A drop whose art cannot be produced without somebody sitting in
# front of the Editor is a drop that ships with red names in it.
#
# So these follow `Tools/make_legend_fx.py`, which made the same move for the legendary band and
# for the same reason (invariant 42j): numpy and Pillow, offline, `--check` proves reproducibility,
# and a checkout with no licensed pack anywhere near it still produces every pixel. What it also
# buys is what a bake cannot - a spell can be a *behaviour* rather than a photographed shape.
#
# **Both are drawn in their own colours rather than white.** A ward's bolt is cut white and tinted
# per colour because there are four of it (invariant 37l); a boss's spell is one picture, and
# `Hurl` lends it `Color.white` precisely so the reel's own paint survives.

#: Frames a flight is drawn over, and frames a muzzle or a landing is. Matched to what
#: `SiegeShotBake` cuts, so a reel drawn here and a reel baked there run at one rate.
SPELL_FRAMES, SPELL_BURST_FRAMES = 18, 16


def _canvas(w, h):
    """An empty RGBA float buffer and the two coordinate grids over it."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    return np.zeros((h, w, 4), np.float32), x, y


def _paint(a, mask, rgb, alpha=1.0):
    """Lays a colour over a buffer, keeping the brighter of what is already there."""
    m = np.clip(mask * alpha, 0.0, 1.0)
    for c in range(3):
        a[..., c] = np.maximum(a[..., c], rgb[c] * m)
    a[..., 3] = np.clip(a[..., 3] + m, 0.0, 1.0)


def _frame(a):
    out = np.zeros(a.shape, np.float32)
    out[..., :3] = np.clip(a[..., :3], 0.0, 255.0)
    out[..., 3] = np.clip(a[..., 3], 0.0, 1.0) * 255.0
    return Image.fromarray(out.astype(np.uint8), "RGBA")


#: The gorgon's colours: sun-bleached limestone for the shaft, a jade iris at its head.
#:
#: **Stone first and green second**, which is the way round the verb reads: what a glare *does* is
#: turn a machine to stone, and the green is the thing doing it. Drawn green-first it would be a
#: poison bolt, which is a blightcaller's hex one chapter and four verbs away.
GAZE_STONE, GAZE_IRIS, GAZE_CORE = (214, 200, 168), (126, 196, 132), (255, 252, 240)

#: The sunlord's: gold, with a white-hot core and a deep amber rim.
DECREE_GOLD, DECREE_CORE, DECREE_RIM = (255, 198, 74), (255, 250, 232), (196, 108, 36)


def gaze():
    """A gorgon's glare crossing the hill: one straight ray with an eye at its head.

    **A ray rather than a thrown object, and that is the whole reading.** Every other flight in
    this mode is something that was *launched* - orbs, an arrow, an axe, a boulder, a
    thunderbolt - and they all bow, tumble or trail. A look does none of those: it is simply
    there, all at once, along a line. So this is drawn as a hard-edged shaft with no wobble in it
    and an iris at the head that narrows as it flies, which is the one piece of motion in it.

    **It narrows and fades toward the tail** (invariant 37dw), and that is not a taste call: the
    view crops a comet's trail to however far the shot has flown (`SiegeView.Emerged`), so the far
    end of what is drawn sits parked on the turret's shoulder for the whole flight. A trail that
    is opaque and wide at the tail is a flat slab lying across the barrel, which is what two of
    the ten legendaries shipped as before anybody measured it.

    **The first cut was a hairline and that is a measurement rather than a taste.**
    `Tools/verify/fxreels.py` floors a reel's short side at 8% of its own frame precisely because
    a bolt two hundredths of a cell wide is a thread on a device and a picture in a contact sheet
    (37de). The shaft is a quarter of the frame at the head now, and the eye a third of it.
    """
    w, h = 128, 384
    frames = []

    for f in range(SPELL_FRAMES):
        u = f / (SPELL_FRAMES - 1.0)
        a, x, y = _canvas(w, h)

        cx = (w - 1) * 0.5
        head = (h - 1) * (1.0 - 0.82)          # `SiegeView.HeadAt`, measured from the bottom

        # How far down the shaft each pixel is, nought at the head and one at the tail.
        along = np.clip((y - head) / (h - 1.0 - head), 0.0, 1.0)

        # **The shaft narrows away from the head**, so the parked end is the thin end.
        wide = (w * 0.30) * (1.0 - along) ** 0.80 + w * 0.02
        across = np.abs(x - cx) / np.maximum(1e-3, wide)

        shaft = np.clip(1.0 - across, 0.0, 1.0) ** 0.7
        core = np.clip(1.0 - across / 0.42, 0.0, 1.0) ** 0.8

        # **And fades along it**, on a curve rather than a line, so the fall is quickest where the
        # eye is least likely to be looking.
        wane = (1.0 - along) ** 1.15

        # A ladder of bands running down the shaft, scrolling toward the head: the only thing in
        # it that moves, so the ray reads as being *sustained* rather than as a still decal.
        band = 0.78 + 0.22 * np.sin(along * 30.0 - u * 9.0) ** 2

        _paint(a, shaft * wane * band, GAZE_STONE, 0.96)
        _paint(a, core * wane, GAZE_CORE, 0.92)

        # The iris at the head: a ring that tightens across the reel, which is what a gaze
        # narrowing looks like from the wrong end of it.
        r = np.sqrt((x - cx) ** 2 + (y - head) ** 2)
        eye = w * (0.40 - 0.13 * u)
        ring = np.clip(1.0 - np.abs(r - eye) / (w * 0.10), 0.0, 1.0) ** 1.1
        pupil = np.clip(1.0 - r / (w * 0.19), 0.0, 1.0) ** 1.2

        _paint(a, ring, GAZE_IRIS, 1.0)
        _paint(a, pupil, GAZE_CORE, 1.0)

        # A halo on the head, floored well above where the crop lands (invariant 37dv).
        halo = np.exp(-(r / (w * 0.46)) ** 2) * 0.62
        _paint(a, halo, GAZE_IRIS, 0.7)

        frames.append(_frame(a))

    return frames


def gaze_muzzle():
    """The eye opening at the gorgon's own head: rings closing to a point."""
    w = h = 256
    frames = []

    for f in range(SPELL_BURST_FRAMES):
        u = f / (SPELL_BURST_FRAMES - 1.0)
        a, x, y = _canvas(w, h)

        cx = cy = (w - 1) * 0.5
        r = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)

        for i in range(3):
            # Three rings, each starting further out and arriving at the middle in turn.
            k = np.clip(u * 1.6 - i * 0.16, 0.0, 1.0)
            at = w * (0.46 - 0.40 * k) * (1.0 - i * 0.12)
            ring = np.clip(1.0 - np.abs(r - at) / (w * 0.035), 0.0, 1.0) ** 1.3
            _paint(a, ring, GAZE_IRIS, (1.0 - k * 0.45) * 0.9)

        pupil = np.clip(1.0 - r / (w * (0.05 + 0.13 * (1.0 - u))), 0.0, 1.0) ** 1.2
        _paint(a, pupil, GAZE_CORE, 1.0)
        _paint(a, np.exp(-(r / (w * 0.30)) ** 2) * (0.7 - u * 0.3), GAZE_STONE, 0.7)

        frames.append(_frame(a))

    return frames


def gaze_hit():
    """What a glare leaves on a post: stone cracking outward from where it landed."""
    w = h = 320
    frames = []

    rng = np.random.RandomState(9061)
    spokes = rng.uniform(0.0, math.tau, 9)
    lengths = rng.uniform(0.62, 1.0, 9)

    for f in range(SPELL_BURST_FRAMES):
        u = f / (SPELL_BURST_FRAMES - 1.0)
        a, x, y = _canvas(w, h)

        cx = cy = (w - 1) * 0.5
        dx, dy = x - cx, y - cy
        r = np.sqrt(dx ** 2 + dy ** 2)
        ang = np.arctan2(dy, dx)

        # **Cracks rather than a burst**, because what a glare does to a machine is set it. A
        # ring of sparks would be the drawing saying *an explosion*, which is what every other
        # landing in this mode already says.
        for k in range(len(spokes)):
            off = np.abs(((ang - spokes[k] + math.pi) % math.tau) - math.pi)
            reach = w * 0.46 * lengths[k] * min(1.0, u * 2.2)
            line = np.clip(1.0 - off / 0.055, 0.0, 1.0)
            run = np.clip(1.0 - r / np.maximum(1e-3, reach), 0.0, 1.0) ** 0.7
            _paint(a, line * run, GAZE_STONE, 0.95 * (1.0 - u * 0.35))

        shock = np.clip(1.0 - np.abs(r - w * 0.46 * u) / (w * 0.05), 0.0, 1.0) ** 1.4
        _paint(a, shock, GAZE_IRIS, (1.0 - u) * 0.9)

        heart = np.clip(1.0 - r / (w * (0.16 - 0.10 * u)), 0.0, 1.0) ** 1.1
        _paint(a, heart, GAZE_CORE, 1.0 - u * 0.7)

        frames.append(_frame(a))

    return frames


def decree():
    """A sunlord's seal crossing the hill: a disc of gold with a sentence turning in it.

    **An object rather than a ray, which is the other half of the pair.** A gorgon's spell is a
    look and this one is a *thing that has been issued* - so it is square-framed, it turns, and it
    carries a rim the eye can follow round. Nothing about it trails, because nothing about it was
    thrown.
    """
    w = h = 384
    frames = []

    for f in range(SPELL_FRAMES):
        u = f / (SPELL_FRAMES - 1.0)
        a, x, y = _canvas(w, h)

        cx = cy = (w - 1) * 0.5
        dx, dy = x - cx, y - cy
        r = np.sqrt(dx ** 2 + dy ** 2)
        ang = np.arctan2(dy, dx)

        # The rim, and a second one inside it, turning in opposite directions - which is the one
        # shape in this mode that reads as a *mechanism* rather than as weather.
        for i, (at, thick, spin, rgb) in enumerate((
                (0.40, 0.030, 1.0, DECREE_RIM),
                (0.31, 0.020, -1.6, DECREE_GOLD))):
            ring = np.clip(1.0 - np.abs(r - w * at) / (w * thick), 0.0, 1.0) ** 1.2
            teeth = 0.55 + 0.45 * np.sin(ang * (12 + i * 6) + u * math.tau * spin) ** 2
            _paint(a, ring * teeth, rgb, 0.95)

        # Spokes out of the middle, so the disc reads as a sun as well as a seal.
        spoke = np.clip(1.0 - np.abs(((ang * 8.0 + u * 2.0) % math.tau) - math.pi) / 0.30,
                        0.0, 1.0)
        reach = np.clip(1.0 - r / (w * 0.34), 0.0, 1.0) ** 1.4
        _paint(a, spoke * reach, DECREE_GOLD, 0.7)

        face = np.clip(1.0 - r / (w * 0.22), 0.0, 1.0) ** 1.1
        _paint(a, face, DECREE_GOLD, 0.85)

        heart = np.clip(1.0 - r / (w * (0.10 + 0.03 * math.sin(u * math.tau))), 0.0, 1.0) ** 1.0
        _paint(a, heart, DECREE_CORE, 1.0)

        _paint(a, np.exp(-(r / (w * 0.30)) ** 2) * 0.45, DECREE_GOLD, 0.55)

        frames.append(_frame(a))

    return frames


def decree_muzzle():
    """The sentence being passed: a column of gold going up out of the sunlord."""
    w, h = 256, 320
    frames = []

    for f in range(SPELL_BURST_FRAMES):
        u = f / (SPELL_BURST_FRAMES - 1.0)
        a, x, y = _canvas(w, h)

        cx = (w - 1) * 0.5
        foot = h - 1.0

        up = np.clip((foot - y) / (h - 1.0), 0.0, 1.0)
        top = min(1.0, 0.25 + u * 1.5)

        wide = (w * 0.20) * (1.0 - up * 0.55)
        across = np.abs(x - cx) / np.maximum(1e-3, wide)

        column = (np.clip(1.0 - across, 0.0, 1.0) ** 0.9
                  * np.clip(1.0 - np.clip((up - top) / 0.18, 0.0, 1.0), 0.0, 1.0))

        _paint(a, column * (1.0 - u * 0.4), DECREE_GOLD, 0.9)
        _paint(a, np.clip(1.0 - across / 0.3, 0.0, 1.0) ** 1.2 * column, DECREE_CORE, 0.95)

        # Motes rising inside it, which is what says the column is *carrying* something.
        motes = 0.5 + 0.5 * np.sin(up * 40.0 - u * 14.0)
        _paint(a, column * motes * 0.5, DECREE_CORE, 0.6)

        frames.append(_frame(a))

    return frames


def decree_hit():
    """The seal biting a post: a ring stamping down and locking."""
    w = h = 320
    frames = []

    for f in range(SPELL_BURST_FRAMES):
        u = f / (SPELL_BURST_FRAMES - 1.0)
        a, x, y = _canvas(w, h)

        cx = cy = (w - 1) * 0.5
        dx, dy = x - cx, y - cy
        r = np.sqrt(dx ** 2 + dy ** 2)
        ang = np.arctan2(dy, dx)

        # **It closes and then holds**, which is the opposite of every other landing here: a
        # burst opens outward and is gone, and a sentence arrives and stays.
        at = w * (0.46 - 0.16 * min(1.0, u * 1.8))
        ring = np.clip(1.0 - np.abs(r - at) / (w * 0.042), 0.0, 1.0) ** 1.2
        teeth = 0.5 + 0.5 * np.sin(ang * 16.0 + u * 3.0) ** 2
        _paint(a, ring * teeth, DECREE_RIM, 0.95)
        _paint(a, ring, DECREE_GOLD, 0.8)

        # Four keys driving inward, so the ring reads as locking rather than as shrinking.
        for k in range(4):
            off = np.abs(((ang - k * math.tau / 4.0 + math.pi) % math.tau) - math.pi)
            key = np.clip(1.0 - off / 0.13, 0.0, 1.0)
            run = np.clip(1.0 - np.abs(r - at * (1.0 - 0.45 * u)) / (w * 0.09), 0.0, 1.0)
            _paint(a, key * run, DECREE_CORE, 0.9 * (1.0 - u * 0.3))

        heart = np.clip(1.0 - r / (w * (0.08 + 0.10 * u)), 0.0, 1.0) ** 1.2
        _paint(a, heart, DECREE_CORE, 0.9 - u * 0.4)

        frames.append(_frame(a))

    return frames


#: Frames the anvil's front is drawn over.
#:
#: **Twenty-four, matched to the still wave's, and the rate is derived rather than written down.**
#: Twelve was chosen against a sweep of under half a second; the sweep is more than twice that now
#: and the view sets the rate from the reel's own length (`frames.Length / HeaveSweep`), so the
#: dust boils once across the climb whatever the climb costs.
HEAVEFRONT_FRAMES = 24

#: The anvil's front, lip to tail: white-hot, then gold, then the molten amber that is `Pal.Amber`
#: exactly, then ember red, then the deep rust it dies into.
#:
#: **The opposite half of `STILL_RAMP`, deliberately.** The two fronts are the only things in this
#: mode that cross the hill, a player has to tell them apart before either has finished, and the
#: whole of what they now differ by at a glance is *temperature*: one is glass going to indigo and
#: the other is fire going to rust. The old pair were both near-white, which is how a wall of
#: driven earth and a wall of stopped time came to read as the same smoke twice.
HEAVE_RAMP = (
    (0.00, (255, 255, 255)),
    (0.09, (255, 236, 176)),
    (0.24, (255, 178, 64)),
    (0.48, (255, 138, 43)),
    (0.74, (198, 70, 34)),
    (1.00, (86, 34, 24)),
)


def heavefront():
    """The wall of driven dust an anvil sends up the hill.

    **Drawn rather than borrowed, which is the hourglass's lesson paid forward** (invariant 37dy):
    the last charm whose payoff was a moment shipped wearing the stormglass's `laser` stretched
    across the board, and what crossed the hill was a stripe. A front has to have a front.

    **It is `stillwave`'s opposite in every reading, deliberately.** They are the only two things
    in this mode that sweep the hill, they arrive one chapter apart, and a player has to tell them
    apart in the quarter-second before either has finished:

    * a still wave is **cold, regular and crystalline** - a flat face, graduations at a fixed
      pitch, shards behind. What it says is *a made thing has stopped the clock*;
    * this is **hot, billowing and turbulent** - a thin lit lip, no repeating feature anywhere in
      it, and a deep soft body that rolls. What it says is *the ground moved*.

    So the one thing they share is the direction of travel, and everything a player reads off
    either of them differs.

    **The first cut of this was a comb, and that is worth writing down.** Every term in it varied
    with *x* alone, so a body meant to read as dust came out as a picket of vertical spikes -
    frost, which is the one thing it may not be, since frost is the charm it stands beside. The
    fix is that the turbulence has to vary in **both** axes: a mask built out of `f(x)` is a
    fence however finely it is tuned, and no amount of choosing frequencies escapes it.

    **And it was far too thin.** A front is mostly body: the lip carries the *speed* and the body
    carries the *weight*, and at a tenth of the frame it read as a scratch rather than as a shock.

    **It is painted rather than tinted now, which is the fault that outlived both of those.** Cut
    white and lent `Pal.Rope` - a dull tan `#D9C39A` - the whole shock was one desaturated hue
    multiplied down: bright half beige, dim half grey, reported as *smokey, dead white*. Driven
    ground is white-hot at the lip and rust in the tail, which is two hues and not one darkened
    (`ramp`, and `STILL_RAMP`'s note on why one drawing may carry its own paint where four ward
    colours may not). So the depth carries `HEAVE_RAMP` and the view lends `Color.white`.

    **And embers, which are what tell a shock from smoke.** The body is scattered with hot points
    that drift back through it over the reel - the one feature here that is *not* a smooth field,
    and the thing that stops a wall of dust reading as a wall of fog.
    """
    long, thick = 512, 128
    y, x = np.mgrid[0:thick, 0:long].astype(np.float32)

    # 0 at the leading edge, 1 at the trailing one. The view sweeps it upward, so the top of the
    # sprite is the front - `stillwave`'s convention, because two fronts drawn to two conventions
    # is one of them being flipped by whoever draws the second caller.
    d = y / (thick - 1.0)

    # Feathered into nothing at both ends, so a front stretched past the field has no drawn end.
    fade = long * 0.045
    along = np.clip(np.minimum(x, long - 1 - x) / fade, 0.0, 1.0)

    # The embers' own field, cut once and rolled per frame. Seeded, so `--check` reproduces it.
    embers = noisefield(thick, long, 7, 26, 104)

    frames = []

    for f in range(HEAVEFRONT_FRAMES):
        u = f / (HEAVEFRONT_FRAMES - 1.0)

        # **Billows, and every one of them varies in both axes.** Three low-frequency lobes at
        # incommensurate wavelengths, each sheared by `d` so a lobe leans as it goes back - which
        # is what stops the body reading as a row of columns and is the whole of what the first
        # cut got wrong.
        roll = np.sin(x / 47.0 + d * 2.3 + u * 4.1)
        swell = np.sin(x / 23.0 - d * 3.1 - u * 2.7)
        churn = np.sin(x / 13.0 + d * 5.2 + u * 6.3)

        # How deep the dust reaches behind the lip, rolling slowly along it. Deep: a shock is
        # mostly body, and at a tenth of the frame this read as a scratch.
        deep = 0.62 + 0.26 * (roll * 0.5 + 0.5) * (0.4 + 0.6 * (swell * 0.5 + 0.5))

        # The lip: thin, hot and slightly uneven, because a perfectly straight shock is a ruler.
        crest = 0.035 + 0.020 * (churn * 0.5 + 0.5)
        lead = np.clip(1.0 - d / np.maximum(1e-3, crest), 0.0, 1.0)
        lip = np.clip(1.0 - d / (crest + 0.085), 0.0, 1.0) ** 1.5

        # The body: deep, soft, and mottled by the three lobes together rather than by any one of
        # them - so the grain has no direction in it.
        mottle = 0.58 + 0.42 * ((roll * swell * 0.5 + 0.5) * 0.6
                                + (churn * 0.5 + 0.5) * 0.4)
        body = np.clip(1.0 - d / np.maximum(1e-3, deep), 0.0, 1.0) ** 1.15

        # And a wash behind all of it, so the front has no drawn end at its back either.
        haze = np.clip(1.0 - d, 0.0, 1.0) ** 1.9

        # It opens as it goes: the dust is thin on the first frames and full by the middle,
        # because a shock throws the ground up *after* it has passed.
        grow = min(1.0, 0.55 + 1.2 * u)

        # **Embers carried in the body, off a noise field rather than off sines.** The first cut
        # multiplied three high-frequency sines together, which is the same mistake the body was
        # re-cut for one paragraph up wearing a different hat: a product of periodic functions is
        # *periodic*, so what came out was a neat lattice of dashes - a stencil, on the one feature
        # whose whole job is to be irregular. A seeded field upsampled smoothly has no period in it
        # at all, and rolling it back through the dust as the reel runs is what makes the embers
        # travel with the shock rather than sit in it.
        ember = np.clip((np.roll(embers, int(u * thick * 0.45), axis=0) - 0.70) / 0.30,
                        0.0, 1.0) * body

        a = np.zeros((thick, long, 4), np.float32)

        lit = np.clip(lead * 1.0 + lip * 0.85
                      + body * mottle * 1.05 * grow + haze * 0.46 * grow
                      + ember * 0.85 * grow, 0.0, 1.0)

        # **The paint: white at the lip, amber through the body, rust in the tail** - and burned
        # back toward white wherever the frame is hottest, so the lip and the embers are the only
        # places the colour leaves the ramp.
        heat = np.clip(lead + lip * 0.75 + ember * 1.10, 0.0, 1.0)
        rgb = ramp(np.clip(d / 0.92, 0.0, 1.0), HEAVE_RAMP)
        a[..., :3] = rgb + (255.0 - rgb) * (0.84 * heat ** 1.9)[..., None]

        a[..., 3] = lit * along * 255.0

        # The lip written back over the top at full, so the leading line is solid whatever the sum
        # came to - `beam`'s rule about a stroke whose brightest pixel is 80%.
        a[..., 3] = np.maximum(a[..., 3], np.maximum(lead, lip * 0.8) * along * 255.0)

        frames.append(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"))

    return frames


#: Frames a dial is drawn over.
#:
#: **Twenty-four at 24fps is one second a revolution**, and the minute hand sweeps exactly once
#: across the reel - so a three second stop is three clean turns and the loop never shows a seam.
#: Tie these two together before changing either: a hand that does not come back to where it
#: started is a clock that jumps every time the reel wraps.
DIAL_FRAMES = 24

#: Both reels are the same clock at the same count, and that is the point rather than a saving:
#: the anvil's face is the hourglass's face running the other way, so anything that moves one and
#: not the other is a drift a player would read as two unrelated objects.
STILLDIAL_FRAMES = HEAVEDIAL_FRAMES = DIAL_FRAMES

#: The hourglass's face: ice, with the sand the one warm thing on it.
#:
#: **A face needs two hues or it is a decal.** Drawn white and lent `Pal.Glass` the whole dial was
#: one near-white multiplied down, which is what "smokey, dead" was about on the piece that is
#: supposed to *say* what the charm did. The rim and the graduations carry the cold, the hands are
#: white-hot so they are read first at any size, and the sand is warm - the only warm thing inside
#: a blue ring, which is why the eye goes to it.
STILL_DIAL = {
    "plate": (74, 140, 210),
    "ring": (150, 234, 255),
    "tick": (198, 246, 255),
    "hand": (255, 255, 255),
    "sand": (255, 198, 96),
}

#: The anvil's face: the same clock in ember, for `HEAVE_RAMP`'s reason. The two dials differ by
#: temperature and by which way the hand is going, and by nothing else.
HEAVE_DIAL = {
    "plate": (150, 74, 34),
    "ring": (255, 176, 66),
    "tick": (255, 216, 146),
    "hand": (255, 255, 255),
    "sand": (255, 238, 196),
}


def clockface(spin, paint, glass=True, arrow=False):
    """One dial: a graduated ring, a running minute hand, and what is inside it.

    **The piece that says what happened.** A wavefront sweeping the hill says *something arrived*;
    only a clock face says *time*, and for both of these charms the time is the entire thing the
    gem is bought for. It is drawn once, held while the payoff lasts and shattered at the end,
    which is the same three-beat shape every other charm here has.

    **Its hands run, and that is a reversal worth recording.** This shipped frozen, on the
    argument that a ticking clock is a clock that is working and what the charm has to say is that
    it is *not*. The argument is sound and the drawing was dead: a still face over a still hill is
    a decal, and the owner's verdict was that it could not be felt. A minute hand sweeping a full
    turn a second - with the smear that rate needs, or it reads as teleporting - says *time is
    doing something*, which is what a player is being sold.

    **And `spin` is the whole of what tells the two charms apart on this face** (the owner's
    instruction, 2026-09-18). An hourglass stops the hill, so its hand runs *forward* - time is
    being spent and the player is watching it go. An anvil gives ground *back*, and its stone is a
    clock face, so its hand runs **anti-clockwise**: the one gesture in the whole of human
    furniture that means *undo*. Everything follows the sign - the hand, the hour hand, the smear
    it drags and the glint running the other way round the rim - because a hand going one way with
    a wake going the other is a mistake a player sees before they can name it.

    **The smear is the direction, so it has to be read off one frame.** A still is all a render
    mirror can produce and all a contact sheet can show, and a hand with no wake says nothing
    about which way it is going. The anvil's face carries a **reversed arc arrow** as well, for
    the same reason: it is the only mark on either dial that is legible in a photograph.

    **Drawn as an outline rather than a solid.** It hangs over the thing the player is watching, so
    it has to be read through - which is also why `SiegeView.DialInk` holds it well under full.

    **Painted rather than tinted**, for `ramp`'s reason: a cold ring with warm sand inside it is
    two hues, and a multiply can only ever be one.
    """
    side = 256
    y, x = np.mgrid[0:side, 0:side].astype(np.float32)

    mid = (side - 1) / 2.0
    dx, dy = x - mid, y - mid
    r = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)

    rim = side * 0.44
    frames = []

    def wedge(angle, span_, reach, gain):
        """The smear a hand drags behind it: a soft fan back from where it is now.

        Behind is decided by `spin`, so a hand running backwards drags its wake forwards round
        the face rather than trailing into the way it is about to go.
        """
        delta = (ang - angle + math.pi) % math.tau - math.pi
        behind = np.clip(-spin * delta / max(1e-3, span_), 0.0, 1.0)
        return (np.clip(1.0 - behind, 0.0, 1.0) ** 1.6
                * np.clip(1.0 - r / reach, 0.0, 1.0) ** 0.8
                * np.clip(r / (reach * 0.16), 0.0, 1.0) * gain)

    def spoke(angle, reach, wide, taper=0.55):
        """A hand: a soft segment out of the middle, thinning along its length."""
        ux, uy = math.cos(angle), math.sin(angle)
        along = np.clip(dx * ux + dy * uy, 0.0, reach)
        off = np.hypot(dx - along * ux, dy - along * uy)
        thin = wide * (1.0 - taper * (along / max(1e-3, reach)))
        return (np.clip(1.0 - off / np.maximum(1e-3, thin), 0.0, 1.0) ** 1.4
                * np.clip(1.0 - along / (reach * 1.02), 0.0, 1.0) ** 0.35)

    # The face, which never changes: a rim, sixty fine graduations and twelve bold ones.
    ring = np.clip(1.0 - np.abs(r - rim) / 7.5, 0.0, 1.0) ** 0.9

    fine_phase = np.abs(((ang * 60.0 / math.tau) % 1.0) - 0.5)
    fine = (np.clip((fine_phase - 0.38) / 0.12, 0.0, 1.0)
            * np.clip(1.0 - np.abs(r - rim * 0.93) / (rim * 0.095), 0.0, 1.0))

    bold_phase = np.abs(((ang * 12.0 / math.tau) % 1.0) - 0.5)
    bold = (np.clip((bold_phase - 0.29) / 0.21, 0.0, 1.0)
            * np.clip(1.0 - np.abs(r - rim * 0.855) / (rim * 0.165), 0.0, 1.0))

    # A face rather than a hoop: a very faint fill so the dial reads as a disc hanging over the
    # hill. Low enough that every body under it is still plainly there.
    plate = np.clip(1.0 - r / (rim * 1.02), 0.0, 1.0) ** 0.5

    # **The glass itself, inside the ring, and it replaced a pair of clock hands.** Hands say
    # *a clock*, which on its own is a piece of furniture this game has nowhere else; an hourglass
    # says *this gem*, because it is the shape of the stone the player just matched. Drawn as an
    # outline rather than a solid so the bodies behind it are never hidden - the same reason the
    # face is held at well under full alpha. The anvil's stone is itself a clock, so its face
    # carries none of this and wears the arrow instead.
    glassH = rim * 0.34          # half its height
    waist, mouth = side * 0.012, rim * 0.19

    # Half the width the bulb has at this height: a waist in the middle opening to a mouth at
    # each end, which is two cones meeting - the glass the drawn hourglass stone carries.
    flare = waist + (mouth - waist) * np.clip(np.abs(dy) / glassH, 0.0, 1.0)
    inside = np.abs(dy) <= glassH

    walls = np.where(inside,
                     np.clip(1.0 - np.abs(np.abs(dx) - flare) / 3.2, 0.0, 1.0) ** 1.0, 0.0)

    # The two caps, and the thread of sand that is not running.
    caps = (np.clip(1.0 - np.abs(np.abs(dy) - glassH) / 3.0, 0.0, 1.0)
            * np.clip(1.0 - np.abs(dx) / (mouth * 1.12), 0.0, 1.0) ** 0.4)

    thread = (np.clip(1.0 - np.abs(dx) / 1.9, 0.0, 1.0)
              * np.clip(1.0 - np.abs(dy) / (glassH * 0.92), 0.0, 1.0) ** 0.6)

    sand = (walls + caps * 0.95 + thread * 0.55) if glass else np.zeros_like(r)

    # **The reversed arc arrow**, which is the only thing on either dial that says which way the
    # hand is going in a single frame. Three quarters of a turn of band at half the radius, with a
    # solid head on the end pointing the way `spin` runs.
    if arrow:
        arc_r = rim * 0.60
        start, span_ = -math.pi * 0.62, math.pi * 1.28

        band = np.clip(1.0 - np.abs(r - arc_r) / 3.4, 0.0, 1.0) ** 1.1
        walk = ((ang - start) * spin) % math.tau
        mark = band * ((walk > 0.0) & (walk < span_)).astype(np.float32)

        end = start + span_ * spin
        hx, hy = arc_r * math.cos(end), arc_r * math.sin(end)

        # The way the head points is the tangent at the end of the arc, turned by `spin`.
        tx, ty = -math.sin(end) * spin, math.cos(end) * spin
        px, py = dx - hx, dy - hy

        fwd = px * tx + py * ty
        off = np.abs(-px * ty + py * tx)
        wide = 9.0 * np.clip(1.0 - fwd / 13.0, 0.0, 1.0)

        head = (np.clip(1.0 - off / np.maximum(1e-3, wide), 0.0, 1.0)
                * ((fwd >= -2.0) & (fwd <= 13.0)).astype(np.float32))

        turn = np.clip(mark * 0.85 + head, 0.0, 1.0)
    else:
        turn = np.zeros_like(r)

    hub = np.clip(1.0 - r / (side * 0.034), 0.0, 1.0) ** 1.4
    inner = np.clip(1.0 - np.abs(r - rim * 0.74) / 2.6, 0.0, 1.0) ** 1.3

    def paint_of(key):
        return np.array(paint[key], np.float32)

    for f in range(DIAL_FRAMES):
        phase = math.tau * f / DIAL_FRAMES

        # Twelve o'clock is up, so a forward sweep runs clockwise from -90 degrees and a reversed
        # one runs back from it.
        minute = -math.pi * 0.5 + spin * phase
        hour = -math.pi * 0.5 + spin * phase / 12.0 + math.pi * 0.35

        hands = (spoke(hour, rim * 0.50, 9.0)
                 + spoke(minute, rim * 0.80, 6.0))

        # The smear it drags, which is what sells the speed - a hand drawn with no wake at this
        # rate reads as a hand teleporting round the face.
        smear = wedge(minute, math.pi * 0.55, rim * 0.80, 0.42)

        # The glint: a short bright arc travelling the rim against the hand, so the face is never
        # symmetrical with it.
        swept = ((ang + spin * phase) % math.tau) / math.tau
        glint = np.clip(1.0 - swept / 0.10, 0.0, 1.0) ** 2.0

        breath = 0.93 + 0.07 * math.sin(phase * 2.0)

        # **Weighted for a face drawn over a lit hill rather than on this sheet.** The rim and the
        # hands carry the shape at a glance and the graduations are what it turns out to be on a
        # second look, so the first two are near solid and the ticks sit under them.
        parts = (
            (plate * 0.16, "plate"),
            (ring * 1.35 + inner * 0.34, "ring"),
            (fine * 1.20 + bold * 1.55, "tick"),
            (sand * 0.85 + turn * 1.30, "sand"),
            (hands * 1.45 + smear + hub * 1.2, "hand"),
            (ring * glint * 1.1, "hand"),
        )

        lit = np.zeros_like(r)
        rgb = np.zeros((side, side, 3), np.float32)

        for mask, key in parts:
            lit = lit + mask
            rgb = rgb + mask[..., None] * paint_of(key)

        # A weighted mean, so a pixel two parts overlap on is the colour of whichever is carrying
        # it - and never the sum, which would run every crossing to white and put the dial back
        # where it started.
        rgb = rgb / np.maximum(lit, 1e-3)[..., None]

        a = np.zeros((side, side, 4), np.float32)
        a[..., :3] = rgb
        a[..., 3] = np.clip(lit * breath, 0.0, 1.0) * 255.0

        frames.append(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"))

    return frames


def stilldial():
    """The dial an hourglass hangs over the hill while the hill is stopped: ice, running forward."""
    return clockface(1, STILL_DIAL, glass=True, arrow=False)


def heavedial():
    """The dial an anvil hangs over the hill as it throws it: ember, running **backwards**.

    **The same clock the hourglass hangs, and that is the owner's call.** The anvil's stone is a
    clock face, and what the charm does is take ground back - so the face that explains it is a
    clock with its hand going the wrong way. The two are told apart by temperature and by
    direction, which is `HEAVE_RAMP`'s argument arriving on the piece that carries the meaning.
    """
    return clockface(-1, HEAVE_DIAL, glass=False, arrow=True)

#: Frames a stormglass's beam is drawn over. Eight at 30fps is a quarter of a second of ripple,
#: which is about as long as one beam stands at full width before it starts closing.
LASER_FRAMES = 8


def laser():
    """The beam a stormglass fires, as a reel — and a **different material** from `beam`.

    **Two beams, because one sprite cannot be both.** A lance's stroke is a thin plasma thread
    stretched across a whole row at two thirds of a cell: what makes that read is a hot wandering
    filament with a long soft tail. Drawn at the size a laser wants it is the same thread with the
    tail spread out, which disappears against a hill — scaled up to two and a half cells it still
    read as a coloured hair, because the sprite's brightness is concentrated in about a sixth of
    its height whatever that height is. Reported as *more visible, more dense, more thick, more
    bright — like a pure laser*, and it is a fact about the alpha profile rather than about a
    number in the view.

    **So this one is a bar with an edge, not a filament with a tail**: flat at full alpha across
    the middle third, then a short smooth falloff to nothing. Stretched to any thickness it stays a
    solid band, because what scales is the band rather than the fade.

    **Straight, too.** `beam`'s filament wanders, which is right for something arcing across a
    board and wrong for a laser: a beam that is not straight is not a beam. What moves here instead
    is *brightness along its length* — a ripple running outward — so it is alive without bending.

    White, like everything else the view tints (invariant 37l: `Image.color` is a multiply, so a
    coloured sprite could only ever be darkened).
    """
    long, thick = 512, 96
    y, x = np.mgrid[0:thick, 0:long].astype(np.float32)

    # Across: flat for the middle third, then a smooth shoulder. `u` is -1..1 over the height.
    u = np.abs(y - (thick - 1) / 2.0) / ((thick - 1) / 2.0)

    flat = 0.34
    edge = np.clip((u - flat) / (1.0 - flat), 0.0, 1.0)
    across = np.where(u <= flat, 1.0, 1.0 - (edge * edge * (3.0 - 2.0 * edge)))

    # Along: feathered into nothing at both ends, so a beam has no drawn end to read as a cut.
    fade = long * 0.04
    along = np.clip(np.minimum(x, long - 1 - x) / fade, 0.0, 1.0)

    frames = []

    for f in range(LASER_FRAMES):
        phase = math.tau * f / LASER_FRAMES

        # A ripple of brightness running along the beam - what makes it alive without bending it.
        run = (np.sin(x / 34.0 - phase * 2.0) * 0.5
               + np.sin(x / 13.0 - phase * 3.0) * 0.5)
        lit = np.clip(0.88 + run * 0.12, 0.0, 1.0)

        a = np.zeros((thick, long, 4), np.float32)
        a[..., 0] = a[..., 1] = a[..., 2] = 255.0
        a[..., 3] = across * along * lit * 255.0

        frames.append(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"))

    return frames


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


def legend_frames(z, folder):
    """One legendary turret's whole recoil, cut against **one** box.

    <b>One box over the reel rather than a box per frame</b>, which is the module docstring's own
    rule and is what `ward_model` cannot do: the merge kit bakes a number plate that overlaps the
    body, so each of those twenty has to be measured and trimmed one frame at a time. This pack
    bakes nothing under its turrets and its recoil is entirely *internal* - the barrels flash and
    the mount stays put, measured at one or two pixels of drift over twenty frames - so the honest
    cut is the reel's own union box. A box per frame would move the turret a pixel as it fired,
    which on a line of four is the flicker invariant 37u names.

    **Pinned by its foot into the ward box**, which is `ward_model`'s rule for `ward_model`'s
    reason: these ten differ at the *top* - a taller barrel, horns, a crown - so a turret that
    rose off its plinth when the player swapped it would read as the plinth having sunk.
    """
    names = sorted(pngs(z, "Png/%s/Shoot/" % folder))
    if not names:
        return []

    frames = [read(z, n) for n in names]
    box = box_of(frames)
    if box is None:
        return []

    cut = []

    for im in frames:
        im = im.crop(box)

        out = Image.new("RGBA", (WARD_W, WARD_H), (0, 0, 0, 0))
        ratio = min(WARD_W / max(1, im.width), WARD_H / max(1, im.height)) * 0.94
        im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))),
                       Image.LANCZOS)

        out.alpha_composite(im, ((WARD_W - im.width) // 2, WARD_H - im.height))
        cut.append(out)

    return cut


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

#: The same fact about the quirky pack, measured the same way: one dark plum at partial
#: alpha. Measured across all five bodies, every shaded pixel is within a unit of this.
SHADE_PLUM = (29, 4, 53)

#: And about the two head-on monster packs the re-cut bosses come from: a dark warm umber,
#: constant to under two units across every body in both zips.
SHADE_UMBER = (55, 20, 3)

#: What each pack bakes under a body, as (ink, tolerance), so one table decides what `deshadow`
#: cuts out of each rather than every call site remembering.
#:
#: <b>A tolerance of nought means "this pack bakes no shadow", and it is a value rather than a
#: branch.</b> The test is "within `tol` of the ink on every channel", which nothing satisfies at
#: nought - so those frames come through untouched, by the rule rather than around it. The
#: survival pack is the one: measured across its five bodies, every translucent pixel in it is the
#: drawing's own antialiased outline plus about fourteen rows of contact shading under the feet,
#: and there is no ellipse at all. Cutting "anything dark and see-through" out of bodies drawn
#: with a very thick black line would eat the line, which is the fault `SHADE_VIOLET`'s note is
#: about met from the other side.
PACK_SHADE = {
    MONSTERS: (SHADE_VIOLET, SHADOW_INK),
    KIT: (SHADE_VIOLET, SHADOW_INK),
    MERGE: ((0, 0, 0), SHADOW_INK),
    QUIRK: (SHADE_PLUM, SHADOW_INK),
    MONS_V1: (SHADE_UMBER, SHADOW_INK),
    MONS_V4: (SHADE_UMBER, SHADOW_INK),

    # Read by `--survey` alone, so that the height it prints is a *body* rather than a body
    # plus the ellipse under it - which is the number that decides whether a pack can cast a
    # chapter here, and the one the first pass of that survey got flatteringly wrong.
    MONS_V2: (SHADE_UMBER, SHADOW_INK),
    MONS_V3: (SHADE_UMBER, SHADOW_INK),
    MONS_V6: (SHADE_UMBER, SHADOW_INK),
    MONS_V7: (SHADE_UMBER, SHADOW_INK),
    FIELD: ((0, 0, 0), SHADOW_INK),
    WIZARD: ((0, 0, 0), 0),

    # **The unit packs bake no ground shadow at all**, measured rather than assumed: a walking
    # Anubis is 34,422 opaque pixels against 1,117 partial ones, and those are the antialiased
    # rim. A tolerance of nought is how this table says "there is nothing here to remove" -
    # the same answer `WIZARD` gives - and it matters, because a deshadow run against a pack
    # that bakes none eats the body's own soft edge.
    MYTH: ((0, 0, 0), 0),
    BOSSPACK2: ((0, 0, 0), 0),
    BONEUNITS: ((0, 0, 0), 0),
    BOSSPACK3: ((0, 0, 0), 0),
    ANCIENTS: ((0, 0, 0), 0),
    WIZUNITS: ((0, 0, 0), 0),
}


def cast_frames(z, folder, count=FRAMES, tall=CAST, ink=(0, 0, 0), tol=SHADOW_INK):
    names = spaced(ordered(z, folder), count)
    if not names:
        return []

    frames = deshadow([read(z, n) for n in names], ink, tol)

    box = box_of(frames)
    if box is None:
        return []

    frames = [im.crop(box) for im in frames]

    width, height = frames[0].size
    ratio = tall / float(height)
    size = (max(1, int(width * ratio)), tall)

    return [im.resize(size, Image.LANCZOS) for im in frames]


#: How far a synthesised lunge carries a body toward the viewer, and how far down the hill.
#:
#: <b>`BOSS_RISE`'s argument asked of a raider.</b> A body seen from above that throws itself
#: forward does not change shape, it changes *size* - so the fourth chapter's pack, whose rig
#: draws a walk and nothing else, can still swing at the line rather than looping its walk
#: against a turret, which is invariant 37u's complaint arriving through the art. Smaller than a
#: boss's rear-up because a raider is drawn at a cell and a bit and there are up to five of them
#: at the line: at a boss's 0.17 the whole rank pulses.
#:
#: <b>Built from the cut frames rather than from the source</b>, so the body inside the lunge is
#: the same pixels at the same size as the body inside the walk. `pulse` grows the canvas
#: symmetrically about its own middle and `SiegeView.Wear` draws a swing at its own frame's
#: height, so the two cancel and the raider neither jumps nor resizes when it arrives.
LUNGE_RISE, LUNGE_LEAN = 0.11, 0.04


def walk_and_swing(z, folder, tall, ink, tol, walk_anim=WALK_ANIM, swing_anim=SWING_ANIM):
    """A raider's two reels: what it walks in, and what it swings in at the ward line.

    <b>The swing is cut on a bigger canvas at the <em>walk's</em> scale, which is the whole of what
    is difficult here.</b> A boss's two reels share one canvas and are fitted to it together
    (`one_canvas`), because a boss stands still and the canvas costs it nothing. A raider cannot
    pay that: measured across these five bodies, the attack swings the weapon so far outside the
    walk's box that a shared canvas fitted to `CAST` would draw every skeleton at <b>59% to 73%</b>
    of its size <em>for the whole run</em>, for the sake of six frames at the line.

    So the walk is cut tight, exactly as every other cast in this mode is, and the swing is cut on
    the union of the two - mirrored about the walk body's own middle on both axes, so the body sits
    in the same place in both - and scaled by the <b>walk's</b> ratio rather than its own. What
    comes out is a taller, wider frame with the body drawn at identical pixels inside it, and
    `SiegeView.Wear` draws it at that same ratio bigger. The body therefore does not move or change
    size when a raider reaches the line, and nothing is clipped.

    <b>A body with no attack animation gets one reel and no swing</b>, which is what the insects and
    the brood do - `SiegeMode.CastSwing` answers an empty address for them and the view keeps
    walking, exactly as it does today.

    """
    walk = deshadow([read(z, n) for n in spaced(ordered(z, folder + walk_anim), FRAMES)], ink, tol)
    if not walk:
        return [], []

    home = box_of(walk)
    if home is None:
        return [], []

    ratio = tall / float(home[3] - home[1])

    def cut(frames, box, high=None):
        wide = int(box[2] - box[0])
        size = (max(1, int(wide * ratio)),
                tall if high is None else max(1, int(high * ratio)))
        return [im.crop(box).resize(size, Image.LANCZOS) for im in frames]

    swing = deshadow([read(z, n) for n in spaced(ordered(z, folder + swing_anim), SWING_FRAMES)],
                     ink, tol)

    if not swing:
        return cut(walk, home), []

    whole = box_of(walk + swing)

    across, down = (home[0] + home[2]) / 2.0, (home[1] + home[3]) / 2.0
    reach = max(across - whole[0], whole[2] - across, across - home[0], home[2] - across)
    fall = max(down - whole[1], whole[3] - down, down - home[1], home[3] - down)

    box = (math.floor(across - reach), math.floor(down - fall),
           math.ceil(across + reach), math.ceil(down + fall))

    # `one_canvas`'s promise, asserted here for the same reason: a clipped weapon imports,
    # addresses, audits and draws, and the only symptom is a flail losing its head for two frames
    # of a swing nobody is looking at closely.
    if not (box[0] <= whole[0] and box[2] >= whole[2]
            and box[1] <= whole[1] and box[3] >= whole[3]):
        raise SystemExit("a swing reel's canvas does not contain every frame of it")

    return cut(walk, home), cut(swing, box, box[3] - box[1])


def rabble_reels(zips, index, tall, frames_kept):
    """One rabble body's walk, baked from vector art at the size this board draws it.

    <b>Two passes, because the scale is a measurement rather than a number.</b> The first bakes
    the cycle at 1:1 to find how tall the body really is across every frame; the second bakes at
    whatever ratio puts that at `tall`. Doing it the other way - baking at a fixed scale and
    resizing - would throw away the whole point of having vectors.

    <b>The crop is the union over the cycle</b>, exactly as `cast_frames` and `walk_and_swing`
    take theirs, so the body neither moves nor resizes between frames.
    """
    rig = spine_bake.Rig(json.loads(zips["rig"]))
    rig.slots = [s for s in rig.slots if s["name"] not in RABBLE_SKIP]

    guide = rig.attachments["Bg"]["Bg"]
    box = (guide["x"] - guide["width"] / 2.0, guide["y"] - guide["height"] / 2.0,
           guide["x"] + guide["width"] / 2.0, guide["y"] + guide["height"] / 2.0)

    wanted = sorted({p[0] for i in range(frames_kept)
                     for p in rig.placements(RABBLE_ANIM,
                                             rig.duration(RABBLE_ANIM) * i / float(frames_kept))})
    parts = spine_bake.match_parts(zips["pages"], zips["exported"], wanted)

    probe = spine_bake.frames(rig, parts, RABBLE_ANIM, frames_kept, box, 1.0, supersample=1)
    home = box_of(probe)
    if home is None:
        return []

    ratio = tall / float(home[3] - home[1])
    full = spine_bake.frames(rig, parts, RABBLE_ANIM, frames_kept, box, ratio)

    here = box_of(full)
    cut = [im.crop(here) for im in full]

    # The two passes disagree by a pixel or so, because a bbox at 1:1 is a coarse ruler. Land it
    # exactly, so every cast in this mode is the same height and `SiegeView.Frame` can size a
    # body by it.
    high = cut[0].height
    if high != tall:
        wide = max(1, int(round(cut[0].width * tall / float(high))))
        cut = [im.resize((wide, tall), Image.LANCZOS) for im in cut]

    return cut


def centroid(im):
    """The alpha-weighted middle of a frame, in its own pixels."""
    a = np.asarray(im)[..., 3].astype(np.float64)
    mass = a.sum()
    if mass <= 0:
        return 0.0, 0.0

    ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    return float((xs * a).sum() / mass), float((ys * a).sum() / mass)



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


def strike(u, beats=BOSS_BEATS):
    """Where a body is in its strike at `u` of the reel: `(surge, lean)`, both -1..1.

    `surge` is -1 at the bottom of the crouch, 0 on the stand and 1 at the peak; `lean` runs
    the same way, back up the hill in the crouch and down it at the peak. See `BOSS_BEATS`.
    """
    crouch, snap, hold = beats

    def ease_in_out(x):
        return x * x * (3.0 - 2.0 * x)

    def ease_out(x):
        return 1.0 - (1.0 - x) * (1.0 - x)

    if u < crouch:
        k = ease_in_out(u / crouch)
        return -k, -k
    if u < snap:
        k = ease_out((u - crouch) / (snap - crouch))
        return -1.0 + 2.0 * k, -1.0 + 2.0 * k
    if u < hold:
        return 1.0, 1.0
    k = ease_in_out((u - hold) / max(1e-6, 1.0 - hold))
    return 1.0 - k, 1.0 - k


def pulse(frames, count, rise, lean):
    """A cast reel built out of a body reel: the insect crouches, strikes at the viewer and settles.

    <b>Three of the four bosses have exactly one animation in the pack</b>, and a boss that does
    nothing at all when it throws is the last verdict on this mode invited straight back. From
    above, rearing up <em>is</em> a change of size - so the body grows toward the viewer and leans
    a little down the hill over the same window the ring and the crackle already fill.

    <b>A strike whose peak is the release</b> - see `BOSS_BEATS` for the three beats and why a
    sine was the wrong shape. It begins and ends on the standing pose: a reel that left the boss
    bigger than it started would snap back on the hand-off to the idle, which is the bug
    invariant 37u names; the legs keep cycling underneath, because the body reel is still being
    walked through.
    """
    n = max(2, count)
    wide = int(math.ceil(frames[0].width * (1.0 + rise)))
    high = int(math.ceil(frames[0].height * (1.0 + rise + lean)))

    out = []
    for i in range(n):
        # The last frame is the stand, so the hand-off to the idle reel is not a snap; the
        # second-to-last is the peak's last frame, which is the one the release fires on.
        u = i / float(n - 1)
        surge, tilt = strike(u)
        src = frames[min(len(frames) - 1, int(i * len(frames) / float(n)))]

        scale = 1.0 + (rise * surge if surge >= 0 else BOSS_CROUCH * surge)
        big = src.resize((max(1, int(round(src.width * scale))),
                          max(1, int(round(src.height * scale)))), Image.LANCZOS)

        shift = (lean * high * tilt) if tilt >= 0 else (BOSS_RECOIL * high * tilt)

        pane = Image.new("RGBA", (wide, high), (0, 0, 0, 0))
        pane.alpha_composite(big, (int(round((wide - big.width) / 2.0)),
                                   int(round((high - big.height) / 2.0 + shift))))
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


def boss_reels(z, body, cast, tall, ink=(0, 0, 0), tol=SHADOW_INK):
    """A boss's two reels: the one it stands and walks in, and the one it throws in.

    The cast reel is a <b>there-and-back</b> whichever way it is come by - the pack's own take-off
    played forward and then reversed, or `pulse`'s sine - so it always begins and ends on the
    standing pose and there is no frame anywhere on which the boss snaps.

    <b>Every boss in this table comes by its cast reel through `pulse`</b>, because the pack drew
    each of them exactly one animation - see `BOSS_SET`. The `cast` branch below is what a pack
    that drew two would take, and the fraction it keeps is the one lesson worth carrying if one
    ever does: the canvas is shared, so whatever the gesture reaches sets the frame, and the view
    sizes a body by its frame - a reel that flings something far out draws the boss *smaller for
    its whole life* in exchange for a few frames of reach.
    """
    stand = deshadow([read(z, n) for n in ordered(z, body)], ink, tol)
    if not stand:
        return None

    if cast is None:
        thrown = pulse(stand, BOSS_CAST_FRAMES, BOSS_RISE, BOSS_LEAN)
    else:
        swung = deshadow([read(z, n) for n in ordered(z, cast)], ink, tol)
        if not swung:
            return None

        # **Only the first part of it**, and the reason is the shared canvas rather than the
        # gesture. The pack's take-off carries the insect 64 pixels off the ground out of a
        # 278-pixel frame, and `one_canvas` is tall as everything - so the whole reel would set
        # the frame's height and the body would be drawn a quarter smaller in *both* reels for
        # the sake of a rise nobody is looking at. Cut to a rear-up it costs nothing and reads
        # better: the overlord lifts, hurls, and settles.
        swung = swung[:max(2, int(len(swung) * BOSS_GESTURE))]
        thrown = swung + swung[::-1]

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

    # The gem-icon pack, which the two charmed gems are cut from. Absent is a checkout without it,
    # so the eight PNGs it cuts are simply not offered - the same bargain `icons` strikes.
    gems = (GEMPACK / "PNG") if (GEMPACK / "PNG").is_dir() else None

    # The skill-icon pack, of which this mode uses exactly one. Absent is a checkout without it,
    # so the one PNG it cuts is simply not offered and `--check` has nothing to hold it to.
    icons = ICONS if ICONS.exists() else None

    if match3 is None or blasts is None or turrets is None or kit is None:
        return None

    # The third chapter's twelve raiders.
    wizard = zipped(WIZARD, SURVIVAL)

    # The fourth chapter's five, and the two packs the three re-cut bosses come from. Absent
    # is a checkout without them rather than a mistake, exactly as every root above.
    field = zipped(FIELD, SOURCE)
    horde1, horde4 = zipped(MONS_V1, CARTOON), zipped(MONS_V4, CARTOON)

    # The three top-down unit packs the bosses are cut from. Absent is a checkout without them
    # rather than a mistake, exactly as every root above.
    myth, boss2, boneunits = zipped(MYTH, UNITS), zipped(BOSSPACK2, UNITS), zipped(BONEUNITS, UNITS)

    # The two the fifth chapter's cast and its thunderer come from. Absent, the same bargain.
    ancients, boss3 = zipped(ANCIENTS, UNITS), zipped(BOSSPACK3, UNITS)

    # And the one the sixth chapter's three creepers come from. Absent, the same bargain.
    wizunits = zipped(WIZUNITS, UNITS)

    # The bosses and half the second chapter's cast live here. Absent, this is a checkout without
    # the pack rather than a mistake - the same bargain every art tool in this project strikes -
    # so it is answered by cutting neither, and `--check` then has nothing to hold them to.
    if monsters is None or wizard is None:
        return None

    if field is None or horde1 is None or horde4 is None:
        return None

    if myth is None or boss2 is None or boneunits is None:
        return None

    if ancients is None or boss3 is None:
        return None

    if wizunits is None:
        return None

    #: Which zip a `BROOD_SET` row names. A table of bodies has to say which pack each is in, and
    #: one dict is how it says it without a branch per row.
    packs = {KIT: kit, MONSTERS: monsters, MONS_V1: horde1, MONS_V4: horde4,
             MYTH: myth, BOSSPACK2: boss2, BONEUNITS: boneunits,
             ANCIENTS: ancients, BOSSPACK3: boss3, WIZUNITS: wizunits}

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

    # **The ten legendary turrets: one picture each, and one recoil each.**
    #
    # A legendary wears no colour, so there is nothing to rotate and nothing to write four times
    # (see `LEGEND_MODELS`). That makes the whole band cost eleven pictures a model against the
    # roster's forty-one, which is also why the shelf can hold thirty turrets in one scope without
    # the loadout's memory moving (invariant 7b, `AssetManifest.WardShelfAssets`).
    #
    # **Stood on frame nought of its own recoil**, which is `ChestPack`'s rule met here: the idle
    # the pack ships is a separate drawing at a separate size, so a turret that swapped to it
    # between shots would change shape every time it fired.
    for model_id, folder in LEGEND_MODELS:
        frames = legend_frames(turrets, folder)
        if not frames:
            return None

        made["Siege/Wards/%s.png" % model_id] = frames[0]

        for f, frame in enumerate(spaced(frames, FIRE_FRAMES)):
            made["Siege/Wards/%s_fire/f%02d.png" % (model_id, f)] = frame

        # The shelf's own picture, out of the loadout's scope rather than the board's - the
        # roster's twenty have one and so does this ten, because the browse grid draws whatever
        # `AssetManifest.WardThumb` names whether or not anything else uses it today.
        made["Ui/Wards/%s.png" % model_id] = fit(read(
            turrets, "Png/%s/Idle/%s-Idle_0.png" % (folder, folder)), THUMB, 0.94)

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

    # **The charms, and every one of them is a gem of its own.** A prism is cut from the same pack
    # as the four and left its own colours; a stormglass and a furnace are cut from the gem icon
    # pack and hue-rotated into each of the four, so the stone is a *different stone* and still says
    # what the cell is worth. See `CHARM_GEMS` for what replaced the mark and why - and for why the
    # other three charms are cut by `make_charm_gems.py` instead.
    made["Siege/gem_prism.png"] = fit(read(match3, PRISM_GEM[0]), TILE, 0.88)

    # Absent is a checkout without the gem pack, which is the same bargain every other root here
    # strikes: the eight PNGs are simply not offered and `--check` has nothing to hold them to.
    if gems is not None:
        for charm, (src, _) in CHARM_GEMS.items():
            face = Image.open(gems / src).convert("RGBA")

            for letter, hue in WARD_HUES:
                made["Siege/gem_%s_%s.png" % (charm, letter)] = charm_gem(face, hue)

    made["Siege/charm_ring.png"] = charm_ring()

    # The colossus's rubble, out of the same pack as the charmed stones and absent with it.
    if gems is not None:
        made["Siege/rubble.png"] = rubble(Image.open(gems / RUBBLE_GEM[0]).convert("RGBA"))

    for i, frame in enumerate(beam()):
        made["Siege/beam/f%02d.png" % i] = frame

    # **A second beam, because a lance's thread and a stormglass's laser are different
    # materials** - see `laser`.
    for i, frame in enumerate(laser()):
        made["Siege/laser/f%02d.png" % i] = frame

    # **What an hourglass sends up the hill, and what it hangs over it.** Both are cut here rather
    # than borrowed: the wave was the stormglass's `laser` stretched to the board's width, which is
    # a bar sliding past rather than time freezing (see `stillwave`).
    for i, frame in enumerate(stillwave()):
        made["Siege/stillwave/f%02d.png" % i] = frame

    for i, frame in enumerate(stilldial()):
        made["Siege/stilldial/f%02d.png" % i] = frame

    # **And the anvil's own face, which is the same clock running backwards.** Its own reel rather
    # than the hourglass's played in reverse: a reversed reel reverses the smear too, so the hand
    # would drag its wake into the way it was going - and the two have to differ in colour as well
    # as in direction (`HEAVE_DIAL`, `clockface`).
    for i, frame in enumerate(heavedial()):
        made["Siege/heavedial/f%02d.png" % i] = frame

    # **What an anvil sends up the hill.** Its own front rather than the still wave re-tinted,
    # which is the same argument the still wave itself is the answer to (invariant 37dy): the two
    # are the only things in this mode that sweep the hill, they arrive one chapter apart, and a
    # player has to tell them apart inside a quarter of a second. See `heavefront`.
    for i, frame in enumerate(heavefront()):
        made["Siege/heavefront/f%02d.png" % i] = frame

    charged = charge(icons)
    if charged is not None:
        made["Siege/charge.png"] = charged

    # The Infinite lane's hub reads its three lines against these. See `HUB_ICONS`.
    made.update(hub_marks(icons))

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

    # **The sixth chapter's two boss spells, drawn rather than baked.** Every reel above them
    # comes out of `SiegeShotBake` and a licensed particle pack; these come out of numpy, for the
    # reason written over `gaze` - a drop whose art needs somebody sitting in front of the Editor
    # is a drop that ships red.
    for key, reel in (("gaze", gaze), ("gaze_muzzle", gaze_muzzle), ("gaze_hit", gaze_hit),
                      ("decree", decree), ("decree_muzzle", decree_muzzle),
                      ("decree_hit", decree_hit)):
        for i, frame in enumerate(reel()):
            made["Fx/Siege/%s/f%02d.png" % (key, i)] = frame

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

    # The third chapter's cast, out of the survival pack, and a **second reel per body**: what a
    # skeleton walks in, and what it swings in when it reaches the line. The same three lines the
    # other two casts are cut with, which is `BROOD_SET`'s claim holding a second time.
    ink, tol = PACK_SHADE[WIZARD]

    for key, folder in BONE_SET.items():
        hue = hues.get(key[-1])
        walk, swing = walk_and_swing(wizard, folder, CAST, ink, tol)

        # **One gain for both reels, measured off the walk.** `value_gain` reads how bright a body
        # is drawn and lifts it onto `CAST_VALUE`; measured per reel it would answer two different
        # numbers for one skeleton - a swing hides half the rib cage behind a shield - and a raider
        # that changed brightness the moment it reached the line is this file's own complaint about
        # a body that changes size, one channel over.
        gain = value_gain(walk)

        for suffix, frames in (("", walk), ("_swing", swing)):
            for i, im in enumerate(frames):
                made["Siege/%s%s/f%02d.png" % (key, suffix, i)] = im if hue is None else hued(
                    im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=BONE_SAT_FLOOR,
                    val_gain=gain, val_lift=CAST_VAL_LIFT)

    # The fourth chapter's cast, out of the top-down tower-defence pack, and a second reel per
    # body exactly as the skeletons have - the same five lines, because a cast set is a *table*
    # and not a code path, which is `BROOD_SET`'s claim holding a third time. Its saturation
    # floor is `CAST_SAT_FLOOR` rather than the bone cast's, because these bodies are painted
    # rather than bone white; and its swing is **built** rather than cut, because this pack draws
    # a walk and nothing else (`LUNGE_RISE`).
    pages = spine_bake.pages_of(field.read(RABBLE_ART), RABBLE_DPI)

    # **Baked once per body rather than once per row**, because half this table is one body in a
    # second colour: twelve rows draw six bodies (`RABBLE_SET`), and a bake is the most expensive
    # thing this tool does - it rasterises the vector source and walks the rig. The colour is
    # applied *after*, so the reels can be shared; `hued` returns new images and neither it nor
    # `value_gain` touches what it is given.
    baked = {}

    for key, body in RABBLE_SET.items():
        hue = hues.get(key[-1])

        if body not in baked:
            prefix = RABBLE_PARTS % body
            exported = {n[len(prefix):-4]: read(field, n) for n in field.namelist()
                        if n.startswith(prefix) and n.endswith(".png")}

            walk = rabble_reels({"rig": field.read(RABBLE_RIG % (body, body)),
                                 "pages": pages, "exported": exported}, body, CAST, FRAMES)
            baked[body] = (walk, pulse(walk, SWING_FRAMES, LUNGE_RISE, LUNGE_LEAN))

        walk, swing = baked[body]

        # **One gain for both reels, measured off the walk.** `value_gain` reads how bright a body
        # is drawn and lifts it onto `CAST_VALUE`; measured per reel it would answer two different
        # numbers for one body, and a raider that changed brightness the moment it reached the
        # line is this file's own complaint about a body that changes size, one channel over.
        gain = value_gain(walk)

        for suffix, frames in (("", walk), ("_swing", swing)):
            for i, im in enumerate(frames):
                made["Siege/%s%s/f%02d.png" % (key, suffix, i)] = im if hue is None else hued(
                    im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=CAST_SAT_FLOOR,
                    val_gain=gain, val_lift=CAST_VAL_LIFT)

    # The fifth chapter's cast, out of the top-down unit packs, and a second reel per body cut
    # from the packs' own attack - the same lines the bone cast is cut with, because a cast set
    # is a *table* and not a code path (`BROOD_SET`'s claim, holding a fourth time). **Cut once
    # per body rather than once per row**, for the rabble's reason: nine of the twelve rows draw
    # five bodies, and the colour is applied after.
    wild = {}

    for key, (pack, body) in WILD_SET.items():
        hue = hues.get(key[-1])
        ink, tol = PACK_SHADE[pack]

        if body not in wild:
            wild[body] = walk_and_swing(packs[pack], body, CAST, ink, tol,
                                        UNIT_WALK_ANIM, UNIT_SWING_ANIM)

        walk, swing = wild[body]

        # **One gain for both reels, measured off the walk**, for the bone cast's reason: a
        # raider that changed brightness the moment it reached the line is this file's own
        # complaint about a body that changes size, one channel over.
        gain = value_gain(walk)

        for suffix, frames in (("", walk), ("_swing", swing)):
            for i, im in enumerate(frames):
                made["Siege/%s%s/f%02d.png" % (key, suffix, i)] = im if hue is None else hued(
                    im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=WILD_SAT_FLOOR,
                    val_gain=gain, val_lift=CAST_VAL_LIFT)

    # The sixth chapter's cast, out of the same top-down unit packs the fifth came from and cut
    # on the same lines - a cast set is a *table* and not a code path (`BROOD_SET`'s claim,
    # holding a fifth time). **Cut once per body rather than once per row**: nine of the twelve
    # rows draw two bodies, and the colour is applied after.
    court = {}

    for key, (pack, body, attack) in COURT_SET.items():
        hue = hues.get(key[-1])
        ink, tol = PACK_SHADE[pack]

        if body not in court:
            court[body] = walk_and_swing(packs[pack], body, CAST, ink, tol,
                                         UNIT_WALK_ANIM, attack)

        walk, swing = court[body]

        # **Every body in this cast swings, and it is asserted rather than assumed.** A folder
        # this pack calls something else answers an empty reel and writes no files, and the
        # result is a raider that reaches the line and keeps walking - which imports, addresses,
        # audits and draws. See `COURT_SET`.
        if not swing:
            raise SystemExit("%s: '%s' has no frames under %s" % (key, body, attack))

        # **One gain for both reels, measured off the walk**, for the bone cast's reason.
        gain = value_gain(walk)

        for suffix, frames in (("", walk), ("_swing", swing)):
            for i, im in enumerate(frames):
                made["Siege/%s%s/f%02d.png" % (key, suffix, i)] = im if hue is None else hued(
                    im, hue, pull=CAST_PULL, sat_gain=CAST_SAT_GAIN, sat_floor=COURT_SAT_FLOOR,
                    val_gain=gain, val_lift=CAST_VAL_LIFT)

    # The twelve bosses: two reels each, both off one canvas so none of them jumps or changes size
    # when it throws. One loop rather than one block per boss, which is what stopped a third and a
    # fourth being expensive - and what makes the *set* something a reader can see at once.
    packs[WIZARD] = wizard

    for key, row in BOSS_SET.items():
        ink, tol = PACK_SHADE[row["pack"]]

        reels = boss_reels(packs[row["pack"]], row["body"], row["cast"], row["tall"],
                           ink=ink, tol=tol)
        if reels is None:
            continue

        # **Nothing here is hue-rotated**: a raider's colour is a rule and a boss's is not (37f),
        # so every one of these keeps what its pack painted.
        for name, frames in ((key, reels[0]), (key + "_cast", reels[1])):
            for i, im in enumerate(frames):
                made["Siege/%s/f%02d.png" % (name, i)] = im

    return made


#: How big the overcharge glyph is cut, and how round its corners are.
#:
#: Square, because the turret's chassis carries a square panel and a badge that fits it reads as
#: part of the machine rather than as a sticker on one. 128 is twice what a phone draws it at.
CHARGE_SIZE, CHARGE_ROUND = 128, 22


#: The three marks the Infinite lane's hub reads its lines against, and what each is for.
#:
#: **They are the one place in this game a bought icon carries a sentence rather than a control**,
#: and they were chosen by laying candidates in the kit's own seat and looking (`Tools/icons.png`):
#: a **phoenix** for a fight with no end, **rising bolts** for a hill that gets harder every wave,
#: and a **medal** for a place on the boards. Cut to `Ui/` rather than to `Siege/` because the hub
#: is drawn on the map screen, whose chapter scope is not loaded when its own art is asked for -
#: and an `Image` with no sprite is a white rectangle rather than a blank (invariant 7b).
HUB_ICONS = (("ic_endless", 90), ("ic_surge", 84), ("ic_rank", 27))

#: The hub's seats are smaller than a turret's chassis glyph, so its marks are cut smaller and
#: rounded tighter. `EndlessHubLayout.IconSize` is what they are drawn at.
HUB_SIZE, HUB_ROUND = 96, 16


def hub_marks(root):
    """The hub's three marks, cut exactly as the overcharge glyph is. See `HUB_ICONS`."""
    if root is None:
        return {}

    made = {}

    for name, number in HUB_ICONS:
        tile = tile_of(root, number, HUB_SIZE, HUB_ROUND)
        if tile is not None:
            made["Ui/%s.png" % name] = tile

    return made


def charge(root):
    """The overcharge glyph: one of the pack's skill icons, squared and rounded.

    **Kept as a tile rather than keyed out of its own background.** These icons are painted *on*
    their ground - the glow round the bolt is most of what makes it read - so a flood or a colour
    key takes the light with it and leaves a thin yellow scribble (the shop's own sheet-keying
    lesson, met again). A rounded tile on the turret's square chassis panel is what the art is
    already shaped like.
    """
    return tile_of(root, CHARGE_ICON, CHARGE_SIZE, CHARGE_ROUND)


def tile_of(root, number, size, round_to):
    """One of the pack's skill icons, squared, rounded and keylined.

    **One cutter for every caller**, because the overcharge glyph and the hub's three marks are the
    same shape at two sizes - and two copies of a rounding is two answers to how a bought icon is
    seated in this kit.
    """
    if root is None:
        return None

    source = root / "PNG" / ("skill icon %d.png" % number)
    if not source.exists():
        return None

    im = Image.open(source).convert("RGBA")

    side = min(im.width, im.height)
    im = im.crop(((im.width - side) // 2, (im.height - side) // 2,
                  (im.width - side) // 2 + side, (im.height - side) // 2 + side))
    im = im.resize((size, size), Image.LANCZOS)

    # A rounded mask, and a keyline in the interface kit's navy so it sits on a bright chassis the
    # way every other badge in this game does (44h's rule about a flat render on a cartoon board).
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=round_to, fill=255)

    tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    tile.paste(im, (0, 0), mask)

    edge = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle([2, 2, size - 3, size - 3], radius=round_to - 2,
                                           outline=(6, 24, 56, 255), width=5)
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


#: Every character pack on this machine, and where each one lives. `--survey` draws them.
#:
#: <b>This table exists because the sentence it replaces was wrong for a year.</b> This file
#: recorded that the only top-down cast here was the insects and that the other packs were
#: "eighty-three side-view cartoon characters" - and on the strength of that a whole Editor tool
#: was written to render a cast out of rigged 3D, and two chapters shipped bodies the owner then
#: withdrew. Four of those packs are drawn <em>head-on</em>, which is the view two of the three
#: shipped casts already use; one holds ten true top-down bodies. Nobody had opened them.
#:
#: <b>So the survey is a command rather than a paragraph</b> (invariant 32b: no gate in this
#: project opens a PNG, so the only answer to "what does this pack look like" is a picture).
#: It takes a minute and it is the first thing to run before anybody concludes there is nothing
#: left to cast a chapter from.
#:
#: Each row is (zip, root, how it names a body, which animations to try). A pack that is not on
#: this machine is skipped, exactly as every other source here is.
CHARACTER_PACKS = (
    (MONS_V1, "CARTOON", "PNG/MonsterV%d", "monster v1 - head-on, the shackler and ironclad"),
    (MONS_V2, "CARTOON", "PNG/Monster %d", "monster v2 - head-on"),
    (MONS_V3, "CARTOON", "PNG/Monster %d", "monster v3 - head-on"),
    (MONS_V4, "CARTOON", "PNG/Monster %d", "monster v4 - head-on, the bonecaller"),
    (MONS_V6, "CARTOON", "Png/Mons %d", "monster v6 - SIDE-VIEW, unusable here"),
    (MONS_V7, "CARTOON", "Png/Mons %d", "monster v7 - SIDE-VIEW, unusable here"),
    (QUIRK, "CARTOON", QUIRK_BODY, "quirky v19 - SIDE-ON, withdrawn, do not cast from it"),
    (WIZARD, "SURVIVAL", "Survival Wizard/Png/Enemies/E%d", "survival wizard - the bone cast"),
    (MONSTERS, "ENEMIES", "Png/Monster%02d", "top-down monsters - half the brood"),

    # The sci-fi half of the shelf. **Nothing here will ever cast a chapter and they are drawn
    # anyway**, which is the whole point of this table: the reason the bake was commissioned is
    # that somebody summarised these packs from memory instead of opening them. A survey that
    # leaves out the rows it expects to reject is the same mistake with better intentions.
    ("craftpix-net-603718-alien-v1-enemy-sprite-set.zip", "SOURCE", "PNG/Alien%d",
     "alien v1 - head-on, sci-fi"),
    ("craftpix-net-259073-alien-v2-enemy-sprite-set.zip", "SOURCE", "PNG/Alien%d",
     "alien v2 - head-on, sci-fi"),
    ("craftpix-net-424965-alien-v3-enemy-character-sprites.zip", "SOURCE", "PNG/Alien%02d",
     "alien v3 - head-on, sci-fi"),
    ("craftpix-net-515480-alien-v4-character-sprites.zip", "SOURCE", "PNG/Alien%02d",
     "alien v4 - head-on, sci-fi"),
    ("craftpix-net-104412-alien-v5-enemy-sprite-set.zip", "SOURCE", "PNG/Alien%02d",
     "alien v5 - head-on, sci-fi"),
    ("craftpix-net-374435-robots-v1-enemy-sprite-set.zip", "SOURCE", "PNG/Char%02d",
     "robot v1 - head-on, sci-fi"),
    ("craftpix-net-422037-robots-v2-enemy-sprite-set.zip", "SOURCE", "PNG/Char%02d",
     "robot v2 - head-on, sci-fi"),
    ("craftpix-net-684986-robot-v3-enemy-character-sprites.zip", "SOURCE", "PNG/Char%02d",
     "robot v3 - head-on, sci-fi"),
    ("craftpix-net-248806-robots-v4-game-sprite-set.zip", "SOURCE", "PNG/Char%02d",
     "robot v4 - head-on, sci-fi"),
    ("craftpix-net-422064-robot-v5-character-sprites.zip", "SOURCE", "PNG/Char%02d",
     "robot v5 - head-on, sci-fi"),
    ("craftpix-net-310523-3-robot-character-sprite-set.zip", "SOURCE", "PNG/Character%d",
     "3-robot set - head-on, sci-fi"),
    (FIELD, "SOURCE", "Png/Zombies/Zombies%02d/Front view",
     "neighbourhood TD - TOP-DOWN, the rabble (baked from its vectors, not these)"),

    # **The top-down shelf, and the answer to the row above it.** Every body here is looked
    # down on, which is the property the whole cartoon half of this table fails. Three of the
    # twenty-one cast a boss; the other eighteen are what a fifth chapter comes out of.
    (BONEUNITS, "UNITS", ("Skeleton Archer", "Skeleton Knight", "Skeleton Wizard"),
     "skeleton units - TOP-DOWN, the bonecaller"),
    (MYTH, "UNITS", ("Anubis", "Horus", "Medusa"),
     "mythology units - TOP-DOWN, the shackler"),
    (BOSSPACK2, "UNITS", ("Armored Ogre", "Cyclops", "Yeti"),
     "fantasy bosses 2 - TOP-DOWN, the ironclad, the colossus, the wild's yeti"),
    (BOSSPACK3, "UNITS", ("Earth Monster", "Ice Monster", "Rock Monster"),
     "fantasy bosses 3 - TOP-DOWN, the wild's clod and its two golems"),
    (ANCIENTS, "UNITS", ("Minotaur", "Pharaoh", "Zeus"),
     "ancient mythology bosses - TOP-DOWN, the thunderer and the wild's minotaur; pharaoh spare"),
    (WIZUNITS, "UNITS", ("Wizard Female", "Wizard Male", "Wizard Veteran"),
     "wizard units - TOP-DOWN, spare (a sixth chapter)"),
)

#: Which animation of a body the survey tries, in order. The packs disagree about the word and
#: some bodies fly rather than walk.
SURVEY_ANIMS = ("/Walk", "/Moving", "/FlyMove", "/Idle",
                "/PNG/PNG Sequences/Front - Walking", "")


def survey():
    """Draw one frame of every body in every character pack, at the size the board draws a raider.

    <b>The instrument the bake was commissioned for want of.</b> It answers the only two
    questions that decide whether a pack can cast a chapter here - which way the bodies face, and
    how many pixels tall they really are - and it answers the second one <em>in the label</em>,
    because that is the one a picture cannot show: a body upscaled 3.3x to reach `CAST` looks
    fine in a contact sheet and soft on a phone.
    """
    roots = {"SOURCE": SOURCE, "TOWER": TOWER, "ENEMIES": ENEMIES, "SURVIVAL": SURVIVAL,
             "CARTOON": CARTOON, "UNITS": UNITS}

    rows = []
    for name, root, shape, label in CHARACTER_PACKS:
        z = zipped(name, roots[root])
        if z is None:
            continue

        ink, tol = PACK_SHADE.get(name, ((0, 0, 0), SHADOW_INK))

        # **A shape is either a number pattern or a list of names**, because the two families of
        # pack on this machine name their bodies differently: the cartoon packs count (`Monster
        # 3`) and the unit packs spell (`Armored Ogre`). One branch here rather than a second
        # survey, so the sheet stays one picture - which is the only thing that makes it useful.
        stems = list(shape) if isinstance(shape, (tuple, list)) else [shape % i
                                                                     for i in range(1, 11)]

        bodies = []
        for stem in stems:
            for anim in SURVEY_ANIMS:
                frames = [read(z, n) for n in spaced(ordered(z, stem + anim), 1)]
                if frames:
                    # **Deshadowed, or the height printed below is a body plus the ellipse the
                    # pack bakes under it** - which on the top-down zombies is more than twice
                    # the body and is exactly the number this survey exists to get right.
                    bodies.append(deshadow(frames, ink, tol)[0])
                    break

        if bodies:
            rows.append((label, bodies))

    if not rows:
        print("no character packs on this machine - nothing to survey")
        return

    cell = 190
    wide = max(len(b) for _, b in rows)
    sheet = Image.new("RGBA", (cell * wide, cell * len(rows)), (24, 26, 32, 255))
    pen = ImageDraw.Draw(sheet)

    for r, (label, bodies) in enumerate(rows):
        heights = []
        for c, im in enumerate(bodies):
            box = im.getbbox()
            if box is None:
                continue

            body = im.crop(box)
            heights.append(body.height)

            ratio = min((cell - 26.0) / body.width, (cell - 26.0) / body.height)
            thumb = body.resize((max(1, int(body.width * ratio)),
                                 max(1, int(body.height * ratio))), Image.LANCZOS)
            sheet.alpha_composite(thumb, (c * cell + (cell - thumb.width) // 2,
                                          r * cell + (cell - thumb.height) // 2 + 8))

        # **The number, because the picture cannot show it, and it is the *shortest* body that
        # decides.** Every body in a cast is cut to `CAST`, so the worst upscale in the pack is
        # the one that has to be affordable - quoting the tallest is how a pack whose smallest
        # body needs 3.3x reads as needing 1.8x, which is the flattering half of exactly the
        # mistake this survey exists to stop.
        low, high = (min(heights), max(heights)) if heights else (1, 1)
        pen.text((4, r * cell + 3),
                 "%s   -  %d bodies, %d-%dpx (up to %.2fx to reach %d)"
                 % (label, len(bodies), low, high, CAST / float(max(1, low)), CAST),
                 fill=(255, 224, 120, 255))

    out = REPO / "Tools" / "siege_cast_survey.png"
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    global SOURCE, TOWER, ENEMIES, ICONS, SURVIVAL, GEMPACK, CARTOON, UNITS

    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--survey", action="store_true",
                    help="draw every character pack on this machine - see CHARACTER_PACKS")
    ap.add_argument("--source", default=str(SOURCE))
    ap.add_argument("--tower", default=str(TOWER))
    ap.add_argument("--enemies", default=str(ENEMIES))
    ap.add_argument("--icons", default=str(ICONS))
    ap.add_argument("--survival", default=str(SURVIVAL))
    ap.add_argument("--gems", default=str(GEMPACK))
    ap.add_argument("--cartoon", default=str(CARTOON))
    ap.add_argument("--units", default=str(UNITS))
    args = ap.parse_args()

    SOURCE = Path(args.source)
    TOWER = Path(args.tower)
    ENEMIES = Path(args.enemies)
    ICONS = Path(args.icons)
    SURVIVAL = Path(args.survival)
    GEMPACK = Path(args.gems)
    CARTOON = Path(args.cartoon)
    UNITS = Path(args.units)

    if args.survey:
        survey()
        return

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
