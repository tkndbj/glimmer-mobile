# -*- coding: utf-8 -*-
"""Writes Thornwatch's first chapter, and proves it is what ships.

    python Tools/chapters/s01_thornwatch.py            # check the shipped body is this
    python Tools/chapters/s01_thornwatch.py --write

**Ten rungs, and the ramp is what is coming rather than how fast it comes** - invariant 5d's
division of labour read across to a siege. Every level authors the same four things (a field, the
colours it refills from, a ward line, and the waves walking down at it) and no numbers at all: how
much fuel a match is worth, how hard a bolt hits and what a blow costs a ward are `SiegeTuning`,
constants in code, one place, retuned for every level at once.

**What each rung adds, in order.**

  1. *First Watch* - the verb, and nothing else. Fifteen creepers in two waves, **three** wards and
     three colours, no brutes, no cogs and no boss. It is the one rung with nothing on the hill but
     raiders and nothing on the field but gems.
  2. *The Ironward* - the **cog**. A raider the line kills leaves one lying on the hill and a
     finger takes it, so what this rung teaches is that the hill is somewhere a finger goes. One
     brute, so the ramp adds a mechanic and a monster in different levels.
  3. *Stonewatch* - brutes in numbers, and the last rung before the line grows.
  4. *Thornhollow* - **the fourth ward, and with it the fourth colour**. Par steps up here and it
     is arithmetic rather than difficulty: a four-colour field cascades less than a three-colour
     one, so a match delivers about two thirds of what it did and the same hill costs more matches
     (`SiegeTuning.MatchGemsTenthsFor`).
  5. *The Warlord's Gate* - the **warlord**: it walks to the middle of the hill, stops, and throws
     at the line from where nothing can reach it.
  6. *Bramble Run* - the **bulwark**, written `#r`: it carries a shield, so it halves every bolt
     that is not its own colour and takes its own in full, and it walks at half a creeper's pace.
     It is the first raider whose answer is a *colour* rather than a quantity - a player taking the
     biggest match on the field loses ground to one.
  7. *Ashenfield* - bulwarks in numbers, one of them in the opening wave.
  8. *Black March* - a warlord *behind* a hill of brutes, so the duel is fought on a bled line.
  9. *The Long Siege* - the longest hill in the chapter, and bulwarks in a group.
 10. *Last Light* - the **overlord**: a warlord of the greater kind, nearly twice the health, half
     again the casting rate, and a spell that takes five off a ward instead of three. It stops
     further up the hill than a warlord does, which is the compensation.

**The field is dealt and the siege is designed**, which is invariant 32d's split. Where the wards
stand, what comes down the hill and how often a cog turns up are things a player *reads*, so they
are written by hand below; which gem sits in which socket is exactly the sort of arrangement nobody
can eyeball, so each field is a seed swept for by `Tools/siege_sweep.py` and kept for what it
measured - an even spread of the colours, no three alike already touching, and a chosen number of
opening swaps. **The rows are re-derived from the seed here rather than typed**, so a board can
never drift from the thing that produced it.

**Par is arithmetic rather than a search** (invariant 37a), and on a rung that deals cogs it is
looser than a floor: a rank-four ward turns one match into 2.33 times what `SiegeTuning.PerfectMatch`
assumes. That errs toward three stars being reachable, which is the direction invariant 22 says to
err in - and what actually proves a rung is holdable is
`SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`, which plays every one of these with the real rules.

**And there is no move allowance anywhere in this chapter**, which is invariant 24 and also the
mode: a siege is lost when the last ward falls, so an allowance would be a second fail state whose
meter counts down to an ending that never happens.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "Tools"))
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import chapters.mapart as mapart                                        # noqa: E402
import proto                                                            # noqa: E402
import siege                                                            # noqa: E402
import siege_sweep as sweep                                             # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s01_thornwatch.json"

CHAPTER = "s01_thornwatch"

#: This chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). One, because it is the first Thornwatch chapter - so it draws `map1` and the
#: first block of skies, exactly as every other mode's first chapter does.
ORDINAL = 1

#: Every field in this chapter, in cells. Eight wide because a render said so - at seven the field
#: was a column of air either side of it, and the field is where the finger goes.
WIDE, TALL = 8, 5

#: The rungs.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed
#: measured and is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often
#: a felled raider leaves one on the hill, **per hundred kills**, and ``boss`` names one of the four
#: bosses and the colour it wears (``"warlord:r"``) - or is empty for a siege that sends none.
#:
#: **The denominator moved with the cog.** It used to be dealt gems, of which a run clears several
#: hundred, so 3 was a handful a run; it is kills now, of which a run has a couple of dozen, so 25
#: is the same handful. A rung that authors the old number would drop about one cog a run and ship
#: the mechanic, its art and its lesson to a player who never sees it - which `ModeValidator` warns
#: about by name.
#:
#: **``wards`` and ``gems`` are the same string, always.** A turret burns its own colour and
#: nothing else, so a gem no ward carries is a move spent on nothing - both content gates refuse a
#: level whose two disagree. The opening rungs stand **three** of each, which is the fewest a jewel
#: board can be dealt from at all (`SiegeLayout.MinWards` records the measurement).
#:
#: **Two bosses across ten rungs, on the fifth and the tenth, and the other two moved to the second
#: chapter.** It shipped with four, one every two or three rungs, and that is too many for one
#: reason a count makes obvious: a boss is what a rung is *remembered* for, and a chapter where
#: nearly half the rungs have one has no rungs that are remembered for anything else. Five and ten
#: is the shape the genre uses - a midpoint and a finale - and it leaves the six rungs between them
#: to be about the hill, the bulwark and the cog.
#:
#: A warlord **smites** a ward (health, the classic duel) and an overlord **sunders** one (health
#: *and* a rank the player earned with a cog). The blightcaller's **douse** and the warbringer's
#: **roar** are `s03_broodmarch`'s, so a player meets all four verbs across twenty rungs and never
#: the same fight twice (invariant 37z).
#:
#: **``charms`` is which powers this rung's refill may deal, and this chapter deals one.** A charm
#: rides an ordinary gem and goes off when that gem is cleared (`SiegeCharm`); a chapter at ordinal
#: *n* deals the first *n* of the roster, so this one deals the **prism** alone - a colourless gem
#: that joins a run of any colour and is paid as the colour it joined. The rate is the mode's
#: (`SiegeTuning.CharmWithin`, one charm every 112 dealt gems, which is two to nine a run);
#: what a rung authors is only whether its board has met the mechanic yet.
#:
#: **The first two rungs deal none, and they are the only ones in the chapter that do not.** Rung
#: one is the verb and nothing else - invariant 24's own rule about the worst moment to hand
#: somebody a second thing to learn - and rung two is the cog, which is a mechanic of its own; two
#: mechanics on one rung is the mistake this chapter's ramp was written to avoid. So the prism
#: lands on rung three, which is otherwise the least distinctive rung here.
#:
#: **What the two that left cost, and what paid it back.** Rung three was a blightcaller riding the
#: head of its last wave and rung eight a warbringer; between them they carried 3,500 health and one
#: boss's worth of pressure on the line. Taking them out drops par on both rungs and - on rung eight
#: - the whole reason its last wave was written *light* (a roar over an empty hill rejects nothing,
#: so the wave had to still be walking when it landed). Both waves are heavier now, measured back to
#: where they stood by `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`, which is the only instrument
#: that can see any of it (invariant 37j).
LEVELS = (
    dict(id="s01_firstwatch", seed=9753, swaps=14, charms="",
         wards="rgb", gems="rgb", cogs=0, boss="",
         waves=["rgbrgb", "rgbrgbrgb"]),

    # **The cog, on the rung where it can be met without anything else going on.** It is dropped by
    # a raider the line kills and lies on the hill until it is tapped, so what this rung really
    # teaches is that the hill is somewhere a finger goes. One brute, so a mechanic and a monster
    # arrive on different rungs.
    dict(id="s01_ironward", seed=419, swaps=15, charms="",
         wards="rgb", gems="rgb", cogs=25, boss="",
         waves=["rgbrgb", "rgbrgbrgb", "rgBrgbrg"]),

    # Brutes in numbers, on the last rung before the line grows.
    dict(id="s01_stonewatch", seed=10719, swaps=16, charms="p",
         wards="rgb", gems="rgb", cogs=25, boss="",
         waves=["rgbrgB", "rgbRGbrg", "RGBRGbrgb"]),

    # **The fourth ward, and with it the fourth colour.** The line has stood three since the first
    # rung; this is where it grows, and everything after it is a four-colour field. A player who
    # has learnt the lock on three meets the widest version of it here.
    dict(id="s01_thornhollow", seed=4176, swaps=9, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rrrgggbb", "YYYY!rrrr", "GGBBYY"]),

    dict(id="s01_warlordsgate", seed=4510, swaps=9, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="warlord:r",
         waves=["rgbyrg", "rgby!bRGby", "RGbyRG"]),

    # **The bulwark**, written `#r`. Under the colour lock every raider takes its own colour in
    # full, so what a shield means now is armour against everything a turret throws *sideways* - a
    # splash, a chain, a lance and an overcharge are all blunted by it, and only the colour it
    # wears is not. It is the one raider a relief valve does not answer.
    dict(id="s01_bramblerun", seed=3603, swaps=8, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rby", "RGBY!yRGby", "RGBYRG#gRGby"]),

    # Three bulwarks, and one of them in the opening wave, so the rung starts on the question
    # rather than working up to it.
    dict(id="s01_ashenfield", seed=5206, swaps=8, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["#bgyRGby", "RGBY#rG!g", "RGBYRG#y"]),

    # The ninth-hardest hill in the chapter and the last one before the finale: armour arriving
    # while brutes are still walking.
    dict(id="s01_blackmarch", seed=1952, swaps=8, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyRGby", "RGBYRGby", "RGBY#gRGby!y"]),

    # The longest hill, and the one that sends bulwarks in a group - three of them across the
    # last two waves, so the slow armour piles up while the brutes behind it are still coming.
    dict(id="s01_thornsiege", seed=7881, swaps=7, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyRGby", "RGBY#rGBY", "RGBY#gRGB#b!r"]),

    dict(id="s01_lastlight", seed=11445, swaps=7, charms="p",
         wards="rgby", gems="rgby", cogs=25, boss="overlord:y",
         # The middle wave's two bulwarks are the finale's own reason to keep reading the hill:
         # they are still walking when the overlord lands, so the player is answering armour and a
         # boss at the same time and neither is answered by the biggest match.
         waves=["rgby!bRGby", "RGB#rY#gG", "RGbyRGby"]),
)


def rows_of(rung):
    """This rung's field, re-derived from its seed rather than typed."""
    cells = sweep.deal(rung["seed"], WIDE, TALL, rung["gems"])
    return sweep.rows_of(cells, WIDE, TALL)


def level(index, rung):
    x, y = mapart.places(ORDINAL)[index]

    block = {
        "width": WIDE,
        "height": TALL,
        "rows": rows_of(rung),
        "gems": rung["gems"],
        "wards": rung["wards"],
        "waves": list(rung["waves"]),
        "boss": rung["boss"],
    }

    # Absent rather than nought on a rung that deals none: `JsonUtility` reads a missing int as
    # nought anyway, so writing it would be a field saying what its own absence already says.
    if rung["cogs"] > 0:
        block["cogs"] = rung["cogs"]

    # And the same for the charms, for the same reason - an absent field and an empty string are
    # one fact, so only one of them is written down.
    if rung["charms"]:
        block["charms"] = rung["charms"]

    return {
        "id": rung["id"],
        "mapX": round(x, 3),
        "mapY": round(y, 3),

        # No allowance, on every rung. A siege is lost when the last ward falls (invariant 24, and
        # the mode) - and both gates *error* on a siege that authors one.
        "budgetFactor": -1.0,
        # **This mode authors its own star lines**, because its par overstates what a run really
        # spends - see `siege.GOLD_FACTOR`. Every other mode derives par by search and takes the
        # shared 1.20 / 1.40.
        "goldFactor": siege.star_factors(ORDINAL)[0],
        "silverFactor": siege.star_factors(ORDINAL)[1],
        "backdrop": mapart.sky(ORDINAL, index, "siege"),

        "siege": block,
    }


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,

        # Rose and a warm dark plate, which is `SiegeLook`'s accent: this is the only mode in the
        # game where something is coming at the player while they think, and the one place a mode
        # is allowed to say what it feels like before it is opened.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def prove(written):
    """Everything this chapter claims about itself, checked against the mirrored rules."""
    for index, level_json in enumerate(written["levels"]):
        rung = LEVELS[index]
        block = level_json["siege"]

        grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
        layout = siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                              block.get("boss"), block.get("cogs", 0),
                              tough=block.get("tough", 0),
                              charms=block.get("charms", ""))

        if layout.fault:
            sys.exit("%s: %s" % (level_json["id"], layout.fault))

        # **The ladder is derived and then proved against the body, never read out of it.** A
        # chapter deals the first `ORDINAL` charms of the roster (`SiegeCharms.Upto`), so a rung
        # that quietly authored a different set would be a chapter teaching two new things at once
        # with nothing anywhere saying so.
        want = siege.charms_upto(ORDINAL) if rung["charms"] else ""
        if rung["charms"] != want:
            sys.exit("%s: deals charms '%s'; a chapter at ordinal %d deals '%s'"
                     % (level_json["id"], rung["charms"], ORDINAL, want))

        par = siege.par(layout)
        read = siege.readings(layout)

        # The seed is recorded with what it measured, so a re-sweep is checked rather than trusted.
        made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
        if made != rung["swaps"]:
            sys.exit("%s: seed %d now deals %d opening swaps, not the %d recorded"
                     % (level_json["id"], rung["seed"], made, rung["swaps"]))

        tail = ""
        if read["charms"]:
            tail += ", charms '%s' (~%d a run)" % (read["charms"], read["sparks"])
        if read["boss"]:
            tail += ", a '%s' %s (%s) last" % (read["boss"], read["kind"], read["spell"])

        print("%-18s par %-4d 3* %-4d 2* %-4d %2d raider(s) in %d wave(s), %2d brute(s), "
              "%d shielded, %d colour(s) against %d ward(s), cogs %2d%% (~%d a run)%s"
              % (level_json["id"], par,
                 proto.over(par, round(siege.star_factors(ORDINAL)[0] * 100)),
                 proto.over(par, round(siege.star_factors(ORDINAL)[1] * 100)),
                 read["raiders"], read["waves"], read["brutes"], read["bulwarks"],
                 read["colours"], read["wards"],
                 read["cogs"], read["drops"], tail))

        # **`threat` is reported here and never refused, which is a correction to how it reads.**
        # It counts *one blow each* from the largest wave, so it names any hill with fewer than
        # fourteen blows in a wave as one that cannot bring a ward down - and that is simply not
        # true, because a raider that reaches the line goes on swinging every
        # `SiegeTuning.BlowEvery` until something kills it. Eight creepers standing at the line for
        # six seconds are worth three times what this counts.
        #
        # So it is a floor on a floor, useful for saying "this hill is obviously lethal" and no use
        # at all for saying the opposite. What actually proves a rung is a siege is
        # `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`, which plays it and fails if the line
        # finishes *untouched* - a measurement rather than a heuristic, and the only instrument a
        # mode with no search has (invariant 37j).
        if not read["threat"]:
            print("      (no single wave carries fourteen blows; the hold simulation is what "
                  "proves this one can be lost)")

        if not read["swap"]:
            sys.exit("%s: this field has no opening swap" % level_json["id"])

    ids = [lv["id"] for lv in written["levels"]]
    if len(set(ids)) != len(ids):
        sys.exit("two levels of this chapter share an id")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    made = body()
    prove(made)

    text = json.dumps(made, indent=1, ensure_ascii=False) + "\n"

    if args.write:
        OUT.write_text(text, encoding="utf-8", newline="\n")
        print("wrote %s" % OUT)
        return

    if not OUT.exists():
        sys.exit("%s does not exist - run with --write" % OUT)

    if OUT.read_text(encoding="utf-8") != text:
        sys.exit("%s differs from what this script writes - re-run with --write" % OUT)

    print("%s is what this script writes" % OUT)


if __name__ == "__main__":
    main()
