"""Thundercrag - the fifth Thornwatch chapter: the wild, a thunderer, a colossus, and the hourglass.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**Invariant 37br priced this chapter exactly as it priced the fourth, and the bill was the same:
two boss verbs.** Eight verbs served four chapters; a fifth had to bring two more or repeat a
fight, which `SiegeRuleTests.NoBossVerbIsSentByAnyTwoChapters` refuses. So it brought a
**thunderer**, which drains the line's banked overcharges and throws them back, and a
**colossus**, which buries a ward under rubble the player digs off by hand - the two things the
first eight left untaken (`SiegeKind`). Neither touches par.

**And it brought the fifth charm.** A chapter deals the first *n* charms of the roster
(`SiegeCharms.Upto`), so the fourth chapter's bill was a fourth charm - the **furnace**, which
banks a charge on the turret of its colour - and this one's is the **hourglass**, which stops the
hill for three seconds while the line keeps firing. Both are written up in `SiegeCharm`, and the
fourth chapter's bodies were rewritten to deal the furnace in the same drop
(`s05_ashenhold.py`, `charms="plsf"`).

**The cast is the wild: five top-down bodies out of the unit packs** (`make_siege_art.WILD_SET`),
every one of them looked down on and walking toward the player, which is the property that
decides a cast before anything else does (37db). Two stone golems are the bulwarks, a yeti and a
minotaur the brutes, and a mud clod creeps. Every body carries the pack's own attack, cut on the
walk's scale. The two bosses are a god and a giant - Zeus and a cyclops - chosen against that
cast for being the two bodies on the shelf that are plainly not monsters.

**Where the difficulty comes from.** The same two levers the fourth chapter had, one rung on:

  * **A toughness surge of three tenths** (`SiegeTuning.ToughnessFor`), against Ashenhold's two.
    Invariant 37bz measured a tenth as a cliff rather than a slope, and this is the third step
    on it - derived from the ordinal and written into the body, never typed.
  * **Composition.** Armour from the first rung, two shields a wave from the second, four-wave
    rungs on the eighth and ninth, and bombers standing inside the armour so a held bomb is worth
    a wave. Fields open at six swaps and tighten to five, the floor Barrowfell and Ashenhold
    reach (a field under five reshuffles, `SiegeBoard.Settle`).

**This chapter's star lines are its own** (`siege.STAR_FACTORS[5]`), for the reason every siege
chapter's are: a surge raises par in proportion and a run's flat payments do not, so a tougher
chapter's clears land at a lower share of par and its lines have to come down with it (37cb).
Set from this chapter's own sweep.

**And there is no move allowance anywhere in it** (invariant 37b).
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s06_thundercrag.json"

CHAPTER = "s06_thundercrag"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies, its
#: grounds, its cast and its charms (invariant 7c, `SiegeMode.CastFor`, `SiegeCharms.Upto`). Five,
#: because it is the fifth Thornwatch chapter on the main ladder - so it draws `map1` again
#: (`mapart.map_of` wraps at four paintings), the first block of forty skies again, and the wild.
#:
#: The id says `s06` because `s02` is the Infinite lane, which is a *track* rather than a place in
#: this ladder. An id is permanent and arbitrary; the ordinal is derived, and it is the ordinal that
#: everything reads.
ORDINAL = 5

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and is
#: recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled raider
#: leaves one on the hill, **per hundred kills**, and ``boss`` names one of the ten bosses and the
#: colour it wears - or is empty for a siege that sends none.
LEVELS = (
    # **Stone from the first rung**, which is the plainest statement this chapter makes about
    # itself: a golem in the second wave and two in the third, where Ashenhold opened with one in
    # its last. The surge is what a player feels first - the same waves as the fourth chapter's
    # opening walk a tenth further before they fall.
    dict(id="s06_firstcrag", seed=162312, swaps=6, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBY#rRGby", "RGBY#g#bRGbyrg"]),

    # The clods come in numbers: a first wave of twelve creepers, which is a swarm the line has
    # to spread its fire over rather than a rank to concentrate on.
    dict(id="s06_mudslide", seed=172429, swaps=6, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyrgbyrgby", "RGby#rRGbyrg", "RGBY#g#bRGby"]),

    # Two golems in every wave, in two colours, so the player is asked to feed two specific wards
    # while the rest of the hill walks.
    dict(id="s06_frostline", seed=181392, swaps=6, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g#bRGby", "RGby#r#yRGbyrg"]),

    # Bombers standing inside the armour, which is the rung that teaches holding a bomb for the
    # wave (invariant 40i): a bomb takes a plus of five boxes, and a golem standing in it is the
    # one raider a colour match is slowest against.
    dict(id="s06_hollowpeak", seed=183670, swaps=6, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g!gRG#bby", "RGby!b#rRGByby"]),

    # **The thunderer, alone, after a hill worth banking against.** It drains the ward holding
    # the most charges and throws them back, so the three waves in front of it are the ones a
    # player banks through - and the tell is the invitation to throw. It wears amber, which
    # decides nothing (37dn) and is what the token's letter is for.
    dict(id="s06_thunderhead", seed=162360, swaps=5, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="thunderer:y",
         waves=["RGBYRGby", "RGBY#rRG!bby", "RGby#gRGBy"]),

    # Three golems in one wave - the most armour the mode carries without a new rule - with a
    # bomb in the wave behind it to answer them with.
    dict(id="s06_glacierwall", seed=160235, swaps=5, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGby#g#b#yRGby", "RGBY!rRG#byrg"]),

    # **One colour at a time with armour in it**, and more of it than Ashenhold's own rung of
    # this shape: a bolt is worth double against its own colour, so the one ward that can help
    # is the only ward that can, three waves running.
    dict(id="s06_stoneward", seed=174643, swaps=5, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#b#bbbYYY#yy"]),

    # Four waves and a bomb at the end of them, which is attrition with one answer held back.
    dict(id="s06_boulderrun", seed=173445, swaps=5, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBY#rRGby", "RGby#g#bRGby", "RGBY!yRGbyrg"]),

    # **The longest climb in the mode**, four waves with a golem in three of them and no boss at
    # the end, so what this rung is about is feeding the line evenly for the better part of two
    # minutes.
    dict(id="s06_highpass", seed=193947, swaps=5, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyrgby", "RGby#rrgby", "rgby#brgbyby", "RGbyrgby"]),

    # **The colossus, and it is the first boss in this mode the player fights with their hands.**
    # Every boulder buries a ward under three pieces of rubble that only taps clear, so the
    # finale asks the one question the mode has never asked - *dig, or match* - while a hill
    # with armour in it is still coming. It wears blue, which decides nothing (37dn).
    dict(id="s06_cragheart", seed=180995, swaps=6, charms="plsfh",
         wards="rgby", gems="rgby", cogs=25, boss="colossus:b",
         waves=["rgbyrgby", "RGby#rrgby", "RGbyrgby"]),
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

    if rung["charms"]:
        block["charms"] = rung["charms"]

    # **Derived from the chapter's ordinal and written down, never typed** - the same bargain the
    # backdrop and the map strips strike (invariant 7c). A level still authors no numbers (37d);
    # this generator does, and both gates hold the body to the rule rather than to the file.
    tough = siege.toughness_for(ORDINAL - 1)
    if tough > 10:
        block["tough"] = tough

    return {
        "id": rung["id"],
        "mapX": round(x, 3),
        "mapY": round(y, 3),
        "afloat": afloat,

        # No allowance, on every rung. A siege is lost when the last ward falls (invariant 37b) -
        # and both gates *error* on a siege that authors one.
        "budgetFactor": -1.0,
        "goldFactor": siege.star_factors(ORDINAL)[0],
        "silverFactor": siege.star_factors(ORDINAL)[1],
        "backdrop": mapart.sky(ORDINAL, index, "siege"),

        "siege": block,
    }


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,

        # The same rose and warm dark plate every chapter before it wears. A mode's look belongs
        # to the mode rather than to the chapter (invariant 7c).
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        # The end-of-chapter marker stands on the painting like every glade does, and its x is
        # the only axis a body can author (`ChapterMap.TeaserPosition` derives its height).
        # Generated with the seats: `Tools/make_map_seats.py`.
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - that the waves parse, that the boss token is one of the ten, that
    the field has a legal swap on it - and derives par and both star lines. What it cannot prove is
    that a rung can be **held**, because a siege has nothing to search: that is
    `SiegeRuleTests.TheFifthChapterIsFoughtOnABoughtLine`, which plays all ten with the real rules
    at nine rhythms on three loadouts.
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
