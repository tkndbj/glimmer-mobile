"""Broodmarch - the second Thornwatch chapter, and the first one a player meets already knowing
the verb.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**What is new here is nothing, and that is the design.** Chapter one taught the whole mode - a
match feeds the ward of its colour, a bolt is worth double against its own, a cog upgrades the
turret whose colour takes it, a brute takes two matches, a bulwark halves the wrong colour, a
bomber leaves something to tap. This chapter adds no rule at all. What it adds is a hill that no
longer forgives a line standing at rank one, and the two boss verbs chapter one does not carry.

**Why the difficulty lives in the hill rather than in the numbers.** Every constant in this mode is
shared by both chapters (`SiegeTuning`), so a retune aimed at making this chapter harder would make
the first one harder too - which is invariant 5d's argument in reverse and the reason difficulty is
the boards' job (26e). So what moves here is what the level *sends*: more raiders, more armour,
denser waves, tighter fields. Cogs stay at chapter one's rate deliberately, because a cog is a rank
and ranks are the thing a player is being asked to *buy* their way past.

**The bar it is tuned against is the owner's own sentence: hard with the starter turret, doable
with a little better.** That is not a feeling here, it is a measurement -
`SiegeRuleTests.TheSecondChapterAsksForBetterTurrets` plays every rung at nine player rhythms with
the free bolt and again with one rung of the shelf bought, and holds the pair apart. A chapter that
an unhurried player clears on the starter as easily as chapter one is a chapter that did not get
harder; one that cannot be cleared on the starter at all is a wall rather than a reason to shop.

**Two bosses, on five and ten, and they are the two chapter one does not send.** A blightcaller
**douses** a ward - its fuel and its fire, no health at all - so the answer is another colour or a
surge, and it rides at the head of its last authored wave rather than walking on alone
(invariant 37ad: five seconds of one turret's dark over an empty hill costs nothing). A warbringer
**roars**, taking a little off every ward at once and sending the hill charging, so the answer is a
firepot into the hill *before* it lands. Neither is answered by a mending alone, and neither is the
duel chapter one ends on.

**Par is arithmetic rather than a proof, exactly as it is in chapter one** - a siege has nothing to
search (invariant 37a) - so it is the hill's health over the most one match could ever deliver, and
it over-states what a good run needs on any rung dealing cogs. That errs toward three stars being
reachable, which is the direction invariant 22 says to err in.

**And there is no move allowance anywhere in this chapter**, which is invariant 24 and also the
mode: a siege is lost when the last ward falls.
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s03_broodmarch.json"

CHAPTER = "s03_broodmarch"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies and
#: its cast (invariant 7c, and `SiegeMode.CastFor`). Two, because it is the second Thornwatch
#: chapter on the main ladder - so it draws `map2` and the second block of forty skies, both of
#: which are already cut. **A second chapter cost no art at all**, which is the cost curve that
#: decided 7c in the first place.
#:
#: The id says `s03` because `s02` is the Infinite lane, which is a *track* rather than a place in
#: this ladder. An id is permanent and arbitrary; the ordinal is derived, and it is the ordinal that
#: everything reads.
ORDINAL = 2

#: Every field in this chapter, in cells - the same eight by five chapter one uses. Unchanged on
#: purpose: the field is the half of the screen the player already knows, and making it bigger would
#: be a second variable in an experiment about the hill.
WIDE, TALL = 8, 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed
#: measured and is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often
#: a felled raider leaves one on the hill, **per hundred kills**, and ``boss`` names one of the four
#: bosses and the colour it wears - or is empty for a siege that sends none.
#:
#: **The fields tighten as the hill grows**, 9 legal opening swaps down to 6 against chapter one's
#: 10 down to 7. That is the one lever here that is about the *board* rather than the hill, and it
#: is small on purpose: a field with too few swaps shuffles itself (`SiegeBoard.Settle`), and a
#: shuffle is the mode taking a decision away.
LEVELS = (
    # **The brood arrives, and nothing else is new.** Three waves, no armour, no bombers: the rung
    # exists so that a player meets a hill that is simply *bigger* than the one they finished the
    # last chapter on, and finds out that the line they cleared it with is not obviously enough.
    dict(id="s03_firstbrood", seed=4658, swaps=9, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyRGby", "RGBYrgby", "RGBYRGby"]),

    # The first armour of the chapter, one to a wave, so the question is asked three times before
    # anything else is asked with it.
    dict(id="s03_hollowshell", seed=5289, swaps=9, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyRGby", "RGBY#rGby", "RGBY#gRGby"]),

    # Bombers, which are the one raider worth *more* to the player than it costs: each leaves a live
    # charge on the hill, and when to spend it is the decision. Three of them on a hill this heavy
    # is the rung that teaches holding one back for the wave rather than tapping it where it fell.
    dict(id="s03_mirewalk", seed=2392, swaps=8, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBY!rRGby", "RGBYRG!gBY!b"]),

    # Armour in a group rather than one at a time, and one of them in the opening wave - so the
    # slow shells are still walking when the brutes behind them arrive, which is the shape the
    # whole back half of this chapter is built on.
    dict(id="s03_spinecrest", seed=5765, swaps=8, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGby#gRGby", "RGby#bRGby"]),

    # **The blightcaller, and it rides the head of this wave rather than walking on alone.** It
    # takes a ward's fire and never its health (`SiegeTuning.EndangersTheLine` is false for it), so
    # a boss wave of its own would be five seconds of one turret's dark over an empty hill - a
    # mechanic that rejects nothing, which is invariant 5d and was a real report from a device. The
    # last wave is therefore written to still be walking when the douse lands, and the answer is to
    # feed a different colour or pour a surge into the dark one.
    dict(id="s03_blightfen", seed=7989, swaps=8, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="blightcaller:b",
         waves=["rgbyRGby", "RGbyRGby", "RGBY#rRGby"]),

    # **One colour at a time, which is the hardest thing this mode can ask without a new rule.** A
    # bolt is worth double against its own colour, so a wave that is all of one thing is a wave
    # three of the four wards can barely help with - and the match a player wants is the one the
    # field is least likely to be offering. Chapter one asked this once, early and gently; this is
    # the same question with armour in it.
    dict(id="s03_stillmire", seed=5687, swaps=7, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RRRrrrrg", "GGG#gGGgb", "BBBbbbbYYy"]),

    # The densest hill so far and the plainest: four brutes of every colour, twice over, and then a
    # third wave with armour in it. Nothing to work out, everything to keep up with.
    dict(id="s03_thornbrood", seed=6417, swaps=7, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyrgby", "RGbyRGbyrg", "RGBY#yRGbyrg"]),

    # Armour and bombers together, which is the rung where a charge held back is worth a whole
    # wave: a bomber's blast takes a plus of five boxes, and a bulwark standing in it is the one
    # raider a colour match is slowest against.
    dict(id="s03_gloamfield", seed=11465, swaps=7, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY!gRGby", "RGBY#bRGby"]),

    # **The longest hill in the game**, four waves and no boss at the end of it, so what this rung
    # is about is attrition rather than a fight: the line has to be fed evenly for a minute and a
    # half, and a colour left dark for one wave too long is the colour that takes a ward down.
    dict(id="s03_deepmire", seed=1112, swaps=6, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyRGby", "RGbyRGby", "RGby#rRGby#gby", "RGBYrgby"]),

    # **The warbringer, and its last authored wave is written light for the reason chapter one's
    # was.** Half a roar sets the hill charging, so a roar over an empty hill rejects nothing - the
    # wave has to still be walking when it lands, and a heavy one charging at 1.55x takes the line
    # apart. What makes this the chapter's finale rather than a repeat is that a roar cannot be
    # answered by protecting one turret: it takes a little off all four at once, so the answer is a
    # firepot into the hill before it comes.
    dict(id="s03_broodheart", seed=1651, swaps=6, charms="pl",
         wards="rgby", gems="rgby", cogs=25, boss="warbringer:g",
         waves=["RGbyRGby", "RGby#rRGby", "RGBYRG#gby"]),
)


def rows_of(rung):
    """This rung's field, re-derived from its seed rather than typed."""
    cells = sweep.deal(rung["seed"], WIDE, TALL, rung["gems"])
    return sweep.rows_of(cells, WIDE, TALL)


def level(index, rung):
    x, y, afloat = mapart.places(ORDINAL)[index]

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
        "afloat": afloat,

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

        # The same rose and warm dark plate chapter one wears. A mode's look belongs to the mode
        # rather than to the chapter (invariant 7c: `accent` and `slate` reach the board's own light
        # and its plate, and nothing else), so a second chapter that re-picked them would be the
        # same game claiming to be two.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        # The end-of-chapter marker stands on the painting like every glade does, and its
        # x is the only axis a body can author (`ChapterMap.TeaserPosition` derives its
        # height). Generated with the seats: `Tools/make_map_seats.py`.
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - that the waves parse, that the boss token is one of the four, that
    the field has a legal swap on it - and derives par and both star lines. What it cannot prove is
    that a rung can be **held**, because a siege has nothing to search: that is
    `SiegeRuleTests.TheSecondChapterAsksForBetterTurrets`, which plays all ten with the real rules
    at nine rhythms and two loadouts.
    """
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

        # A floor on a floor - see the note on the same line in `s01_thornwatch.py`. What proves a
        # rung is a siege is the hold simulation, not this.
        if not read["threat"]:
            print("      (no single wave carries fourteen blows; the hold simulation is what "
                  "proves this one can be lost)")

        if not read["swap"]:
            sys.exit("%s: this field has no opening swap" % level_json["id"])

    ids = [lv["id"] for lv in written["levels"]]
    if len(set(ids)) != len(ids):
        sys.exit("two levels of this chapter share an id")

    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    written = body()
    text = json.dumps(written, indent=1, ensure_ascii=False) + "\n"

    if not prove(written):
        return 1

    if args.write:
        OUT.write_text(text, encoding="utf-8")
        print("wrote %s" % OUT)
        return 0

    if not OUT.exists():
        print("%s does not exist - run with --write" % OUT)
        return 1

    if OUT.read_text(encoding="utf-8") != text:
        print("%s differs from what this file says - run with --write" % OUT)
        return 1

    print("%s is what this file says" % OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
