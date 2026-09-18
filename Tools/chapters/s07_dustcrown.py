"""Dustcrown - the sixth Thornwatch chapter: the court, a gorgon, a sunlord, and the anvil.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**Invariant 37br priced this chapter exactly as it priced the fourth and the fifth, and the bill
was the same: two boss verbs.** Ten verbs served five chapters; a sixth had to bring two more or
repeat a fight, which `SiegeRuleTests.NoBossVerbIsSentByAnyTwoChapters` refuses. So it brought a
**gorgon**, whose glare turns a ward stone-struck - it keeps firing, keeps burning the fuel each
shot costs, and lands nothing - and a **sunlord**, which seals a ward and gives the player a
deadline to fill it or lose it. The first takes the ward's *work* and the second takes the
player's *agenda*, which are the two things the first ten verbs left (`SiegeKind`). Neither
reaches par.

**And it brought the sixth charm.** A chapter deals the first *n* charms of the roster
(`SiegeCharms.Upto`), so this one's bill is the **anvil**: the whole hill is driven back up the
slope by a fifth of its length, and nothing is hurt. It is the first *defensive* payoff this mode
has ever had, which is a hole worth filling at the chapter where the hill is heaviest - see
`SiegeCharm.Anvil` for why a shove is not an hourglass said twice.

**The cast is the court: six top-down bodies out of the unit packs** (`make_siege_art.COURT_SET`),
every one of them looked down on and walking toward the player, which is the property that decides
a cast before anything else does (37db). Three robed wizards and a hooded archer creep - four
distinct creeper bodies, which no cast in this mode has had before and is worth having exactly
where the swarms are largest - a falcon-headed war-god is the brute, and a skeleton in bone plate
behind a shield is the bulwark. The two bosses are the two bodies in these packs that are plainly
not soldiers: a gorgon and a king.

**Where the difficulty comes from.** The same two levers every chapter past the second has, one
rung on:

  * **A toughness surge of four tenths** (`SiegeTuning.ToughnessFor`), against Thundercrag's
    three - so a raider here carries **40% more health than one in the first chapter**, which is
    the figure the chapter was commissioned against. Invariant 37bz measured a tenth as a cliff
    rather than a slope, and this is the fourth step on it - derived from the ordinal and written
    into the body, never typed.
  * **Composition, and at this surge it is the lever that comes *down* rather than up.** The
    first cut of this chapter carried four to six more bodies a wave than Thundercrag on top of
    the fourth tenth, on the reasoning that both levers had always moved together - and it held
    **14 of 90** runs on the starter against Thundercrag's 45, with five rungs held at no rhythm
    at all. Invariant 37bz measured a tenth as a cliff rather than a slope; what 37ef adds is
    that a chapter past the fourth gets its *difficulty* from the surge and its *identity* from
    its cast, its bosses and its charm. So what ships is the fifth chapter's own shapes, trimmed
    where the sweep found a wall and thickened where it found slack: armour from the first rung,
    two shields a wave from the third, three in one wave on the seventh, four-wave rungs on the
    eighth and ninth. Fields open at six swaps and tighten to five, the floor Barrowfell,
    Ashenhold and Thundercrag reach (a field under five reshuffles, `SiegeBoard.Settle`).

**This chapter's star lines are its own** (`siege.STAR_FACTORS[6]`), for the reason every siege
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s07_dustcrown.json"

CHAPTER = "s07_dustcrown"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies, its
#: grounds, its cast and its charms (invariant 7c, `SiegeMode.CastFor`, `SiegeCharms.Upto`). Six,
#: because it is the sixth Thornwatch chapter on the main ladder - so it draws `map6`, the
#: wasteland mesas, the *second* block of forty skies (there are only forty, so the skies wrap
#: where the maps no longer do), and the court.
#:
#: The id says `s07` because `s02` is the Infinite lane, which is a *track* rather than a place in
#: this ladder. An id is permanent and arbitrary; the ordinal is derived, and it is the ordinal that
#: everything reads.
ORDINAL = 6

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and is
#: recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled raider
#: leaves one on the hill, **per hundred kills**, and ``boss`` names one of the twelve bosses and the
#: colour it wears - or is empty for a siege that sends none.
LEVELS = (
    # **Plate from the first rung and two more bodies a wave than the fifth chapter opened with**,
    # which is the plainest statement this chapter makes about itself. The surge is what a player
    # feels first: the same shapes walk a tenth further than Thundercrag's before they fall.
    dict(id="s07_firstdune", seed=200061, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyrgbyr", "RGby#rRGby", "RGBY#g#bRGbyr"]),

    # The wizards come in numbers: a first wave of fourteen creepers, which is the largest swarm
    # the mode has sent and is why this cast has four distinct creeper bodies rather than one.
    dict(id="s07_bonefield", seed=200912, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyrgbyrgby", "RGby#rRGby", "RGBY#g#bRGbyr"]),

    # Two shields in every wave, in two colours, so the player is asked to feed two specific wards
    # while the rest of the hill walks.
    dict(id="s07_saltpan", seed=220674, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g#bRGby", "RGby#r#yRGbyr"]),

    # Bombers standing inside the plate, which is the rung that teaches holding a bomb for the
    # wave (invariant 40i): a bomb takes a plus of five boxes, and a bulwark standing in it is the
    # one raider a colour match is slowest against.
    dict(id="s07_dryreach", seed=228008, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g!gRG#bby", "RGby!b#rRGByby"]),

    # **The gorgon, alone, after three waves worth pouring into.** Her glare burns whatever is
    # poured into the ward it lands on, so the fight is about where the *next* few matches go -
    # and the three waves in front of it are the ones that teach a player to pour hard. She wears
    # green, which decides nothing (37dn) and is what the token's letter is for.
    dict(id="s07_gorgongate", seed=230636, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="gorgon:g",
         waves=["RGbyRGby", "RGby#rRG!bby", "RGby#gRGby"]),

    # **One colour at a time with plate in it**, and more of it than Thundercrag's rung of this
    # shape: a bolt is worth double against its own colour, so the one ward that can help is the
    # only ward that can, three waves running.
    dict(id="s07_sunscour", seed=232936, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#b#bbbYYY#y#yy"]),

    # Three shields in one wave - the most armour the mode carries without a new rule - with a
    # bomb in the wave behind it to answer them with.
    dict(id="s07_glassridge", seed=238398, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGby#g#b#yrgby", "RGBY!rRG#by"]),

    # Four waves and a bomb at the end of them, which is attrition with one answer held back.
    dict(id="s07_duststorm", seed=238766, swaps=6, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyrgby", "RGBY#rRGby", "RGby#g#brgby", "RGBY!yRGby"]),

    # **The longest climb in the mode**, four waves with plate in three of them and no boss at the
    # end, so what this rung is about is feeding the line evenly for the better part of two
    # minutes.
    dict(id="s07_thelongwalk", seed=203220, swaps=5, charms="plsfha",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["rgbyrgby", "RGby#rrgby", "rgby#brgbyby", "RGBYrgbyr"]),

    # **The sunlord, and it is the first boss in this mode the player can answer.** Every seal is
    # a ward taken in twelve seconds unless that ward's tube is filled first, so the finale asks
    # the one question the mode has never asked - *fill this, now, whatever you were doing* -
    # while a hill with plate in it is still coming. It can never seal the last ward standing, so
    # the run still ends the way every run in this mode ends. He wears amber, which decides
    # nothing (37dn).
    #
    # **Its cog rate is the chapter's one outlier, and the boss is why.** A sunlord at this
    # chapter's surge stands with 9,520 health, which is the largest thing in the mode by half
    # again - and its verb takes wards off the line while the player is trying to deliver it. At
    # the chapter's ordinary 25 the rung was held at **none** of the forty-nine rhythms
    # `SiegeRuleTests.Walled` tries on a starter line: a wall rather than a reason to buy a
    # turret. Measured up the rate, it saturates at 40 - 40, 45 and 50 all read 2 of 9 bare, 3 of
    # 9 on one bought rung and 6 of 9 on a good one - so 40 is where the lever stops paying
    # rather than a figure fitted to the gate. **Par and both star lines do not move by one**,
    # because a cog is a rank the player earns and not health on the hill: the rung asks the most
    # of the line in the chapter, so it hands the line the most ranks.
    dict(id="s07_crownfall", seed=217358, swaps=5, charms="plsfha",
         wards="rgby", gems="rgby", cogs=40, boss="sunlord:y",
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

    This proves the *layout* - that the waves parse, that the boss token is one of the twelve,
    that the field has a legal swap on it - and derives par and both star lines. What it cannot
    prove is that a rung can be **held**, because a siege has nothing to search: that is
    `SiegeRuleTests.TheSixthChapterIsFoughtOnABoughtLine`, which plays all ten with the real rules
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
