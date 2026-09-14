"""Barrowfell - the third Thornwatch chapter, and the first one a player is expected to have
*shopped* for.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**What is new here is a cast, two boss verbs and nothing else.** Chapter one taught the mode and
chapter two added no rule at all (its entry says so out loud, and that was the design). This one
adds the two things the mode had run out of: a *third* twelve-body cast, and the fifth and sixth
boss verbs - because with four verbs and two bosses a chapter, the mode supported exactly two
chapters before a player would meet one fight twice. That is invariant 37z's rule arriving as a
cost curve rather than as a complaint, and `SiegeRuleTests.NoBossVerbIsSentByAnyTwoChapters` is
what makes it a rule rather than an intention.

**The brief was "genuinely harder than chapter two, and the player should have bought or upgraded
turrets by now", and that is a measurement rather than a feeling.**
`SiegeRuleTests.TheThirdChapterAsksForABoughtLine` plays every rung at nine player rhythms on three
lines - the free bolt, one rung of the shelf, and two - and holds the three apart. A chapter an
unhurried player clears on the starter as easily as chapter two is a chapter that did not get
harder; one that cannot be cleared on a *bought* line at all is a wall rather than a reason to shop.

**Where the difficulty comes from, and where it deliberately does not.** Every constant in this mode
is shared by all three chapters (`SiegeTuning`), so a retune aimed at this chapter would retune the
other two - which is why difficulty is the boards' job (invariant 26e, and s03's own entry). So what
moves here is what the level *sends*: more armour than chapter two ever asked for, bombers standing
in it, four-wave rungs, and fields dealt tighter (8 opening swaps down to 5, against chapter two's
9 down to 6). Cogs stay at the same rate for chapter two's reason - a cog is a rank, and ranks are
the thing a player is being asked to buy rather than to be given.

**The two bosses, and each takes something no other boss takes.**

  * A **gravemaw** on the fifth rung *eats what the hill owes the player* - every cog and every
    live bomb still lying on the ground when it feeds. It takes no ward health at all, so it rides
    the head of its last authored wave rather than walking on alone (invariant 37ad), and that wave
    is authored with a bomber and the rung deals cogs, because a gravemaw over bare ground is
    invariant 5d's decoration wearing a boss's body - which the validator refuses.
  * A **bonecaller** on the tenth *raises the dead*: three groups of four creepers in its own
    colour, at the top of the hill, walking down like any other wave. It is the only boss in this
    mode that adds to the board rather than subtracting from it, and it is capped at twelve
    precisely so that par can still be computed off the file (invariant 37a).

**Par is arithmetic rather than a proof, exactly as it is in both chapters before it** - a siege has
nothing to search - so it is the hill's health over the most one match could ever deliver, and on
the finale it counts every raider the bonecaller *could* raise whether it lives to raise them or
not. That over-states a run that kills it early, which is the direction invariant 22 says to err in.

**And there is no move allowance anywhere in this chapter** (invariant 37b): a siege is lost when
the last ward falls, and both gates error on one that authors a budget.
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s04_barrowfell.json"

CHAPTER = "s04_barrowfell"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies, its
#: grounds and its cast (invariant 7c, and `SiegeMode.CastFor`). Three, because it is the third
#: Thornwatch chapter on the main ladder - so it draws `map3`, the third block of forty skies and
#: the bone cast, and every one of those is arithmetic rather than a decision.
#:
#: The id says `s04` because `s02` is the Infinite lane, which is a *track* rather than a place in
#: this ladder. An id is permanent and arbitrary; the ordinal is derived, and it is the ordinal that
#: everything reads.
ORDINAL = 3

#: Every field in this chapter, in cells - the same eight by five both chapters before it use.
#: Unchanged on purpose: the field is the half of the screen the player already knows, and making it
#: bigger would be a second variable in an experiment about the hill.
WIDE, TALL = 8, 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and is
#: recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled raider
#: leaves one on the hill, **per hundred kills**, and ``boss`` names one of the six bosses and the
#: colour it wears - or is empty for a siege that sends none.
#:
#: **The fields tighten again**, 8 legal opening swaps down to 5 against chapter two's 9 down to 6
#: and chapter one's 10 down to 7. Five is the floor and it is close to it: a field with too few
#: swaps shuffles itself (`SiegeBoard.Settle`), and a shuffle is the mode taking a decision away.
LEVELS = (
    # **The bones arrive, and the rung's job is to say so.** Three waves, no armour, no bombers -
    # but every wave is heavier than the one that opened chapter two, because a player reaching
    # this chapter has thirty rungs behind them and the thing that has to be legible on the first
    # of ten is the *cast*, not a new rule.
    dict(id="s04_firstbone", seed=29186, swaps=8, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBYRGby", "RGBYRGbyrg"]),

    # Armour from the second rung rather than the fifth, which is the plainest statement this
    # chapter makes about itself: chapter two asked this question once a wave from rung two, and
    # here it is twice a wave from rung two.
    dict(id="s04_palerow", seed=30411, swaps=8, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBY#rRGby", "RGBY#gRGbyrg"]),

    # **Two shields in a wave, which is the hardest thing a wave can carry without a new rule.** A
    # bulwark halves every bolt that is not its own colour, so a pair of them in two different
    # colours is a wave that asks the player to feed two specific wards in order while everything
    # else walks - and taking the biggest match on the field is exactly the wrong answer to it.
    dict(id="s04_shieldwall", seed=20321, swaps=7, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGby#g#bRGby", "RGBY#y#rRGby"]),

    # Bombers, and three of them on a hill this heavy is the rung that teaches holding one back for
    # the wave rather than tapping it where it fell (invariant 40i: the decision is *when*).
    dict(id="s04_scytheway", seed=20651, swaps=7, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGBYRGBy", "RGBY#gRG!gby", "RGBY!bRG#yRGby"]),

    # **The gravemaw, and its last authored wave carries the thing it eats.** It takes no ward
    # health, so a wave of its own would be four seconds of a boss opening its mouth over an empty
    # hill - invariant 5d, and a real report from a device about the blightcaller. It rides the head
    # of the last wave instead, that wave stands a bomber, and the rung deals cogs at the chapter's
    # own rate: so there is always something on the ground and the answer is to reach for it before
    # the boss does. Both gates refuse this rung if either is taken away.
    dict(id="s04_hollowgrave", seed=34596, swaps=7, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="gravemaw:r",
         waves=["RGBYRGBy", "RGBY!rRG#bby", "RGBY#r#gRG!gby"]),

    # The densest hill in the game and the plainest: four waves of brutes in every colour with one
    # shield in the third. Nothing to work out, everything to keep up with - which is a texture a
    # chapter needs one of, and this is chapter three's.
    dict(id="s04_boneyard", seed=31909, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBYRGby", "RGBY#rRGbyrg"]),

    # **One colour at a time with armour in it, which chapter two asked once and this asks four
    # times.** A bolt is worth double against its own colour, so a wave that is all of one thing is
    # a wave three of the four wards can barely help with - and the shield in each makes the one
    # ward that *can* help the only ward that can.
    dict(id="s04_deadmarch", seed=37050, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGBYRGBy", "RGBYRGBY", "RGBY#rRGBy"]),

    # Armour and bombers together, which is the rung where a charge held back is worth a whole wave:
    # a bomb takes a plus of five boxes, and a bulwark standing in it is the one raider a colour
    # match is slowest against.
    dict(id="s04_lichgate", seed=40083, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGBY", "RGBY!gRGBy", "RGBY#b#yRGBy"]),

    # **The longest hill in the game**, four waves and no boss at the end of it, so what this rung
    # is about is attrition rather than a fight: the line has to be fed evenly for the better part
    # of two minutes, and a colour left dark for one wave too long is the colour that takes a ward
    # down.
    dict(id="s04_longbarrow", seed=42242, swaps=5, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGbyRGby", "RGby#rRGby#gby", "RGBY!yRGBY"]),

    # **The bonecaller, and it is a duel with company that arrives three times.** It endangers the
    # line without ever touching a ward - what it raises walks and swings - so it gets a wave of its
    # own and the long quiet in front of it (invariant 37t: a duel is never stacked on a wave still
    # swinging). What makes it the chapter's finale rather than a repeat is that the answer is not
    # damage on the boss alone: twelve creepers in one colour arrive while the line is already busy,
    # and a player who has banked nothing for that colour meets all of them at half weight.
    dict(id="s04_barrowheart", seed=63874, swaps=5, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="bonecaller:b",
         waves=["RGbyRGby", "RGBY#rRGby", "RGBYRG#gby"]),
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

    # **Derived from the chapter's ordinal and written down, never typed** - the same bargain the
    # backdrop and the map strips strike (invariant 7c). A level still authors no numbers (37d);
    # this generator does, and both gates hold the body to the rule rather than to the file.
    #
    # Absent on the first two chapters, which are the baseline and do not move.
    tough = siege.toughness_for(ORDINAL - 1)
    if tough > 10:
        block["tough"] = tough

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

        # The same rose and warm dark plate both chapters before it wear. A mode's look belongs to
        # the mode rather than to the chapter (invariant 7c: `accent` and `slate` reach the board's
        # own light and its plate, and nothing else), so a third chapter that re-picked them would
        # be the same game claiming to be three.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - that the waves parse, that the boss token is one of the six, that the
    field has a legal swap on it - and derives par and both star lines. What it cannot prove is that
    a rung can be **held**, because a siege has nothing to search: that is
    `SiegeRuleTests.TheThirdChapterAsksForABoughtLine`, which plays all ten with the real rules at
    nine rhythms on three loadouts.
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
