"""Bonereach - the seventh Thornwatch chapter: the dead lands, a harrower and a hollowking.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**Invariant 37br priced this chapter exactly as it priced the fourth, the fifth and the sixth,
and the bill was the same: two boss verbs.** Twelve verbs served six chapters; a seventh had to
bring two more or repeat a fight, which `SiegeRuleTests.NoBossVerbIsSentByAnyTwoChapters` refuses.
So it brought a **harrower**, which tears a rank off a ward and drops it on the hill as an
ordinary cog the player can walk over and pick up, and a **hollowking**, which strikes every ward
that has fired nothing since its last cast and leaves the ones that have been working alone. The
first takes a rank the player can get *back* - which is a sunder read the way a seal reads a
devour, a decision rather than a loss - and the second is the only verb in this mode answered
*before* it lands, by having kept all four tubes doing something. Neither reaches par
(`SiegeKind`).

**And it brought no charm at all, which is the first chapter since the third not to.** The roster
is six and `SiegeCharms.Upto` clamps, so a seventh chapter deals exactly what the sixth does -
the owner's call on 2026-09-20, and the honest reading of the ladder: six powers is what the mode
holds and a seventh would be the fourth free payoff added to a hill that 37cg already says is at
its ceiling.

**The cast is the insects, and that is arithmetic rather than a shortage.** `SiegeMode.MainCasts`
wraps at six, so the seventh chapter draws the first chapter's cast - which is the bargain
invariant 7c strikes on purpose ("a chapter published next year costs no cast at all"), and it is
six chapters and forty rungs away from the last time anybody saw a beetle. What it decided is the
*bosses*: this table's standing rule is that a boss is chosen against the cast it stands in front
of, and the five bosses already standing in front of the insects are `MONSTERS`' round cartoon
warlords - so the harrower and the hollowking are cut from the head-on monster packs
(`make_siege_art.BOSS_SET`), a bare skull in a hood and a horned body in deep blue.

**Where the difficulty comes from.** One lever, and 37ef is why it is only one:

  * **A toughness surge of five tenths** (`SiegeTuning.ToughnessFor`), against Dustcrown's four -
    so a raider here carries **50% more health than one in the first chapter**, which is the
    figure this chapter was commissioned against. Invariant 37bz measured a tenth as a cliff
    rather than a slope, and this is the fifth step on it - derived from the ordinal and written
    into the body, never typed.
  * **Composition, held at the sixth chapter's, deliberately.** 37ef is the measurement that
    settled this: Dustcrown's first cut carried four to six more bodies a wave than Thundercrag
    *on top of* a fresh tenth of surge and held 14 of 90 runs on the starter, with five rungs
    walled outright. What that bought is a rule an author can follow - a chapter past the fourth
    gets its **difficulty** from `ToughnessFor` and its **identity** from its cast, its bosses
    and its charm - so this chapter's waves are the sixth chapter's own shapes, trimmed where
    its own sweep found a wall. Fields open at six swaps and tighten to five, the floor
    Barrowfell, Ashenhold, Thundercrag and Dustcrown reach (a field under five reshuffles,
    `SiegeBoard.Settle`).

**This chapter's star lines are its own** (`siege.STAR_FACTORS[7]`), for the reason every siege
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s08_bonereach.json"

CHAPTER = "s08_bonereach"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies,
#: its grounds, its cast and its charms (invariant 7c, `SiegeMode.CastFor`, `SiegeCharms.Upto`).
#: Seven, because it is the seventh Thornwatch chapter on the main ladder - so it draws `map7`,
#: the dead lands, the *third* block of forty skies (there are only forty, so the skies wrap
#: where the maps no longer do), and the insects, which is where the cast table wraps.
#:
#: The id says `s08` because `s02` is the Infinite lane, which is a *track* rather than a place in
#: this ladder. An id is permanent and arbitrary; the ordinal is derived, and it is the ordinal
#: that everything reads.
ORDINAL = 7

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names one of the fourteen
#: bosses and the colour it wears - or is empty for a siege that sends none.
LEVELS = (
    # **Plate from the first rung, exactly as the sixth chapter opens**, so the one thing that is
    # different is the thing that is meant to be: the same shapes walk a tenth further than
    # Dustcrown's before they fall.
    dict(id="s08_firstreach", seed=259695, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyrgbyr", "RGby#rRGby", "RGBY#g#bRGbyr"]),

    # A swarm with nothing armoured in it, which is the rung that says what fifty per cent of
    # health really costs: the same bodies, and the line no longer clears them before they land.
    dict(id="s08_bonespur", seed=267464, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyrgbyrgby", "RGby#rRGby", "RGBY#g#bRGbyr"]),

    # Two shields in every wave, in two colours, so the player is asked to feed two specific
    # wards while the rest of the hill walks.
    dict(id="s08_ropebridge", seed=268133, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g#bRGby", "RGby#r#yRGbyr"]),

    # Bombers standing inside the plate, which is the rung that teaches holding a bomb for the
    # wave (invariant 40i): a bomb takes a plus of five boxes, and a bulwark standing in it is
    # the one raider a colour match is slowest against.
    dict(id="s08_crystalrise", seed=269448, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g!gRG#bby", "RGby!b#rRGByby"]),

    # **The harrower, after three waves worth ranking up through.** What it takes is a rank, and
    # the cog it drops is lying on the hill for as long as the player leaves it there - so the
    # three waves in front of it are the ones that hand the line its ranks in the first place,
    # and the fight is about whether a beat spent reaching down is a beat worth spending. It
    # wears red, which decides nothing (37dn) and is what the token's letter is for.
    dict(id="s08_harrowgate", seed=270069, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="harrower:r",
         waves=["RGbyRGby", "RGby#rRG!bby", "RGby#gRGby"]),

    # **One colour at a time with plate in it**: a bolt is worth double against its own colour,
    # so the one ward that can help is the only ward that can, three waves running.
    dict(id="s08_thinair", seed=270518, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#b#bbbYYY#y#yy"]),

    # Three shields in one wave - the most armour the mode carries without a new rule - with a
    # bomb in the wave behind it to answer them with.
    dict(id="s08_shatterstep", seed=275372, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGby#g#b#yrgby", "RGBY!rRG#by"]),

    # Four waves and a bomb at the end of them, which is attrition with one answer held back.
    dict(id="s08_deadfall", seed=276498, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyrgby", "RGBY#rRGby", "RGby#g#brgby", "RGBY!yRGby"]),

    # **The longest climb in the chapter**, four waves with plate in three of them and no boss at
    # the end, so what this rung is about is feeding the line evenly for the better part of two
    # minutes - which is the rehearsal the finale then bills.
    dict(id="s08_thelastspan", seed=252462, swaps=5, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyrgby", "RGby#rrgby", "rgby#brgbyby", "RGBYrgbyr"]),

    # **The hollowking, and it asks the one question this mode has never asked.** A wane strikes
    # every post that has landed nothing since its last cast and spares every post that has been
    # working, so the instruction is *keep all four going* - the opposite of the instruction
    # every other fight here gives, which is to pour into the colour that matters. It is also
    # the only boss verb that can be answered for nothing at all: a line fed evenly takes not a
    # point from it. It wears blue, which decides nothing (37dn).
    #
    # **Its cog rate is the chapter's one outlier, and the boss is why** - the sixth chapter's
    # finale set the precedent and the argument is the same one. A hollowking at this chapter's
    # surge stands with 10,800 health, the largest thing in the mode, and its verb bills the
    # player for every second any post is dry. At the chapter's ordinary 25 the rung read as a
    # wall on a starter line rather than as a reason to buy a turret; a cog is a rank the player
    # earns and not health on the hill, so the rung that asks the most of the line hands the line
    # the most ranks. **Par and both star lines do not move by one.**
    dict(id="s08_hollowcrown", seed=256059, swaps=5, charms="plsfha",
         wards="rgby", gems="rgby", cogs=40, boss="hollowking:b",
         waves=["rgbyrgby", "rgby#rrgby", "RGbyrgby"]),
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

    This proves the *layout* - that the waves parse, that the boss token is one of the fourteen,
    that the field has a legal swap on it - and derives par and both star lines. What it cannot
    prove is that a rung can be **held**, because a siege has nothing to search: that is
    `SiegeRuleTests.TheSeventhChapterIsFoughtOnABoughtLine`, which plays all ten with the real
    rules at nine rhythms on three loadouts.
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
        # chapter deals the first `ORDINAL` charms of the roster (`SiegeCharms.Upto`, which
        # clamps at the roster's own length), so a rung that quietly authored a different set
        # would be a chapter teaching something the ladder never granted it.
        want = siege.charms_upto(ORDINAL) if rung["charms"] else ""
        if rung["charms"] != want:
            sys.exit("%s: deals charms '%s'; a chapter at ordinal %d deals '%s'"
                     % (level_json["id"], rung["charms"], ORDINAL, want))

        par = siege.par(layout)
        read = siege.readings(layout)

        # The seed is recorded with what it measured, so a re-sweep is checked rather than
        # trusted.
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
