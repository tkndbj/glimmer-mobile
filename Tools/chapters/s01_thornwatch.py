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

  1. *First Watch* - the verb, and nothing else. Twelve creepers in two waves, four wards, no
     brutes, no cogs and no warlord. It is the one rung with nothing on the field but gems.
  2. *The Ironward* - the **cog**, standing on the field where it will be met. A cog never matches;
     it is destroyed by a run of gems *beside* it, and the colour of that run decides which turret
     goes up a rank. One brute, so the ramp adds a mechanic and a monster in different levels.
  3. *Stonewatch* - brutes in numbers, and cogs from the deal rather than the author.
  4. *Thornhollow* - **three wards and three colours**. Par dips here on purpose (par is length,
     not difficulty), and what makes it hard is that a third of the line's answers are gone.
  5. *The Warlord's Gate* - the **warlord**: it walks to the middle of the hill, stops, and throws
     at the line from where nothing can reach it.
  6. *Bramble Run* - the first hill that is mostly brutes.
  7. *Ashenfield* - and the first that is more brute than creeper.
  8. *Black March* - a warlord *behind* a hill of brutes, so the duel is fought on a bled line.
  9. *The Long Siege* - the longest hill in the chapter, and the most cogs.
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
#: ``seed`` and ``stood`` deal the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed
#: measured and is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is the rate a
#: fresh gem falls in as a cog, per hundred, and ``boss`` names one of the four bosses and the
#: colour it wears (``"warlord:r"``) - or is empty for a siege that sends none.
#:
#: **Four bosses across ten rungs, and each of them takes a different thing.** The chapter shipped
#: with two, told apart by their hue and nothing else, which read exactly as what it was. A
#: blightcaller **douses** a ward (its fuel and its fire, no health); a warlord **smites** one
#: (health, the classic duel); a warbringer **roars** (it charges the hill and walks to the line
#: itself, the only boss here that arrives); an overlord **sunders** (health *and* a rank the
#: player earned with a cog). Each has its own answer, and only two of the four are a mending.
LEVELS = (
    dict(id="s01_firstwatch", seed=413, swaps=10, stood=0,
         wards="rgby", gems="rgby", cogs=0, boss="",
         waves=["rgby", "rgbyrgby"]),

    dict(id="s01_ironward", seed=159, swaps=10, stood=1,
         wards="rgby", gems="rgby", cogs=4, boss="",
         waves=["rgbyrg", "rgbyrgby", "rgByrgby"]),

    dict(id="s01_stonewatch", seed=1338, swaps=10, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="blightcaller:b",
         waves=["rgbyRG", "rgbyRGby", "RGBYrgby"]),

    dict(id="s01_thornhollow", seed=4176, swaps=9, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="",
         waves=["rrrgggbb", "YYYYrrrr", "GGBBYY"]),

    dict(id="s01_warlordsgate", seed=4510, swaps=9, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="warlord:r",
         waves=["rgbyrg", "rgbyRGby", "RGby"]),

    dict(id="s01_bramblerun", seed=3603, swaps=8, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="",
         waves=["RGbyRGby", "RGbyRGby", "RGBYRG"]),

    dict(id="s01_ashenfield", seed=5206, swaps=8, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="",
         waves=["rgbyRGby", "RGBYRG", "RGBYRGby"]),

    dict(id="s01_blackmarch", seed=1952, swaps=8, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="warbringer:g",
         # A longer last wave than the rung would otherwise want, and a *lighter* one, and the
         # warbringer is the reason for both. Half its roar sets the hill charging, and a roar over
         # an empty hill is a mechanic that rejects nothing (invariant 5d) - so it comes early on
         # purpose (`SiegeTuning.WarbringerAfter`) and this wave is still walking when it arrives.
         # Six all-brute raiders charging at 1.55x took the line apart with a raider left
         # (`SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`, which is the only thing that can see
         # it); four brutes and two creepers is the same wave to rally and a rung that holds.
         waves=["rgbyRGby", "RGBYRGby", "RGBYrg"]),

    dict(id="s01_thornsiege", seed=7881, swaps=7, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="",
         waves=["rgbyRGby", "RGBYRGBY", "RGBYRGBY"]),

    dict(id="s01_lastlight", seed=11445, swaps=7, stood=0,
         wards="rgby", gems="rgby", cogs=3, boss="overlord:y",
         # Two of the last wave's brutes became creepers when a bolt's damage halved and its
         # cadence doubled (`SiegeTuning.FireEvery`). The totals are neutral by construction, but
         # the *dynamics* are not - a line that kills faster empties the hill sooner, and an empty
         # hill musters the next wave at once (37k), so the finale's waves stacked and it was lost
         # with one raider left. The cliff is one brute wide: `RGBYRgby` still loses.
         waves=["rgbyRGby", "RGBYRG", "RGBYrgby"]),
)


def rows_of(rung):
    """This rung's field, re-derived from its seed rather than typed."""
    cells = sweep.deal(rung["seed"], WIDE, TALL, rung["gems"], rung["stood"])
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

    return {
        "id": rung["id"],
        "mapX": round(x, 3),
        "mapY": round(y, 3),

        # No allowance, on every rung. A siege is lost when the last ward falls (invariant 24, and
        # the mode) - and both gates *error* on a siege that authors one.
        "budgetFactor": -1.0,
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
                              block.get("boss"), block.get("cogs", 0))

        if layout.fault:
            sys.exit("%s: %s" % (level_json["id"], layout.fault))

        par = siege.par(layout)
        read = siege.readings(layout)

        # The seed is recorded with what it measured, so a re-sweep is checked rather than trusted.
        made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
        if made != rung["swaps"]:
            sys.exit("%s: seed %d now deals %d opening swaps, not the %d recorded"
                     % (level_json["id"], rung["seed"], made, rung["swaps"]))

        print("%-18s par %-4d 3* %-4d 2* %-4d %2d raider(s) in %d wave(s), %2d brute(s), "
              "%d colour(s) against %d ward(s), cogs %2d%%%s"
              % (level_json["id"], par,
                 proto.over(par, proto.GOLD_HUNDREDTHS), proto.over(par, proto.SILVER_HUNDREDTHS),
                 read["raiders"], read["waves"], read["brutes"], read["colours"], read["wards"],
                 read["cogs"],
                 (", a '%s' %s (%s) last" % (read["boss"], read["kind"], read["spell"]))
                 if read["boss"] else ""))

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
