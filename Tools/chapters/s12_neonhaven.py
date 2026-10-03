"""Neonhaven - the eleventh Thornwatch chapter: the neon sky city, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-10-02, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 131 to 150**, on the owner's own painting - a neon city on floating
    rocks, server racks, satellite dishes and a noodle stall, joined by a glass road edged in cyan
    light - with every node standing **on the road** (`make_map_seats.TRACES[11]`, walked up the
    road the painting draws).
  * **"Slightly harder than the previous chapter - just a little."** The crowd is held to
    Windwreck's own shape: `prove` reads Windwreck's body through the same mirror and refuses this
    chapter unless it sends **at least as many** raiders and brutes, and **no more than a few per
    cent over** on any of them (`MOST_OVER`). **The surge is the dial, and it was set by
    measurement rather than by the ladder**: these fields and waves play harder than Windwreck's
    at one surge, so it deals **1.5** (`SiegeTuning.Traded`, row `(10, 15)`) against Windwreck's
    1.6. On three-star embers that held 34 of 180 against Windwreck's 43 - about a fifth harder;
    at 1.7 it held 13 and at 1.6 22 (`SiegeRuleTests.TheEleventhChapterIsALittleHarder`).
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *vanguard* (`SiegeMode.VanguardOrder`), a fifth square over the six chapter casts sharing no
    slot with the medley, the reunion, the gathering or the armada. The four duels are four pairs
    nobody has fought, of eight verbs, none twice in the chapter (37ey's restated rule).
  * **The curse carries on**, as it did through Windwreck: every rung deals the obsidian.

**The four duels, and what each pair asks.**

  * **Rung 5, a warlord and a blightcaller** - a smite on the line while a blight spreads over
    the crowd: the opening duel is the two oldest verbs in the mode, together for the first time.
  * **Rung 10, a thunderer and a colossus** - a storm drains the charges while a turret is buried
    under rubble and has to be dug out.
  * **Rung 15, a gorgon and a shackler** - the glare wastes what is poured into a ward while a
    chain holds another silent: two answers taken off the line at once.
  * **Rung 20, a hollowking and an ironclad** - the hollowking strikes every post that has fired
    nothing since its last cast while plate halves every bolt, so every ward has to keep working
    through the armour. **Cogs at fifty**, the finale's argument since Cloudkeep.

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s12_neonhaven.json"

CHAPTER = "s12_neonhaven"

#: The chapter this one is commissioned against: a little harder than it, and only a little.
BEFORE = OUT.parent / "s11_windwreck.json"

#: This chapter's place inside its own mode and track, which decides its map (`map11`, the owner's
#: neon sky city), its skies (the third block again - forty skies wrap at four chapters), its cast
#: (the vanguard), its charms (all six; `SiegeCharms.Upto` clamps) and its surge (the fourth row of
#: `SiegeTuning.Traded`).
ORDINAL = 11

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: The fewest and most raiders a wave of this chapter sends, **bosses aside** - Windwreck's own
#: bounds, because the crowd is not where this chapter's step is taken.
LEAST_WAVE, MOST_WAVE = 12, 17

#: The fewest brutes a rung that is not a duel sends - Windwreck's floor, kept.
LEAST_BRUTES = 6

#: **"Just a little"**, as a number: how far past Windwreck this chapter's crowd may go, in per
#: cent. Hill (par) is not bounded here, because it carries the surge, and the surge is set by
#: play rather than by arithmetic (see `SiegeTuning.Traded`).
MOST_OVER = {"raiders": 5, "brutes": 8}

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses
#: joined by `+` - or is empty for a siege that sends none. Fields open at six swaps and tighten
#: to five for the last four rungs, Windwreck's shape. **No seed is shared with any shipped
#: chapter**, so no field here is one a player has already seen.
LEVELS = (
    # ---------------------------------------------------------------- the lower city
    # **The glass pier.** Windwreck's opening, a creeper and a brute pair heavier - the first step
    # is the surge.
    dict(id="s12_glasspier", seed=703598, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyRGbyrg", "rgbyRGbyrgBYr", "rgbyRGBY#g#brgb"]),

    # Racks on the left-hand rock: brutes at the back of every wave.
    dict(id="s12_serverrow", seed=719480, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbRGy", "rgbyrgby#brgBY", "rgbyrgbyRGbyRGby"]),

    # The neon span: two shields a wave behind the brutes.
    dict(id="s12_neonspan", seed=708710, swaps=6, cogs=20, boss="",
         waves=["rgbyRG#gbyrgby", "rgbyRGby#r#yrgb", "rgBY#b#gRGbyrgby"]),

    # Bombers inside a crowd of brutes, under the noodle stall's sign.
    dict(id="s12_noodlestall", seed=716280, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!gRGby", "rgBY#rRGby!yrgb", "rgBY!b#gRGbyRGby"]),

    # **The first duel: a warlord and a blightcaller** - the two oldest verbs in the mode, together
    # for the first time. **Cogs at thirty-five, measured**: at Windwreck's twenty-five the
    # strongest line felled the pair at 2 of 9 rhythms against Windwreck's opening duel's 4; at
    # thirty-five it is 4 of 9, the same fight a tenth tougher. The lever moves no par.
    dict(id="s12_jetrock", seed=762933, swaps=6, cogs=35,
         boss="warlord:g+blightcaller:b",
         waves=["rgbyRGbyrgbyr", "rgby#y!rRGbyRG", "rgBY#g#rRGbyrg"]),

    # ---------------------------------------------------------------- the escalator rocks
    # **One colour at a time, in a flood**, its brutes in the middle of it.
    dict(id="s12_lanternlift", seed=770569, swaps=6, cogs=20, boss="",
         waves=["ggGGgggggg#gyyy", "yyYYyyyyyy#ybbb", "bbBB#bbbbbRRR#rrr"]),

    # Four waves up the escalator, the third of them armoured and bombed at once.
    dict(id="s12_escalator", seed=792824, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyr", "rgby#g#bRGbyRG", "rgby!ggrBY#yybr", "rgbyRGbyrgby"]),

    # Pipe alley: plate and a bomb behind a creeper crowd, brutes at the back of every wave.
    dict(id="s12_pipealley", seed=806841, swaps=6, cogs=20, boss="",
         waves=["rgBYrgbyrgbyRGb", "rgby#b#rrgbyRGby", "rgBY!yrgby#ggrBY"]),

    # **The rehearsal**: four waves and no boss, a brute pair in every one.
    dict(id="s12_rackhall", seed=830797, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyr", "rgby#grgbyRGb", "rgby#yrgBYrgby", "rgBYrgbyrgbyr"]),

    # **The second duel: a thunderer and a colossus.** A storm drains the charges a line has built
    # while a turret lies buried under rubble and has to be dug out by matching its colour. **Two
    # measurements decided it**: an overlord with the colossus was never felled on the strongest
    # line at any rhythm (a wall, which the brief is not), and every other free pair fell at 1-3
    # of 9; so this pair, the one that fought longest, with two of the crowd's brutes moved to
    # the opening rung and **cogs at forty-five**, which fells it at 9 of 9 - Windwreck's own
    # second duel's figure.
    dict(id="s12_signalyard", seed=847924, swaps=6, cogs=45,
         boss="thunderer:y+colossus:r",
         waves=["rgbyRGbyrgby", "rgby#gRGbyrgbrg", "rgby#b#yRGbyrg"]),

    # ---------------------------------------------------------------- the dish rocks
    # The crowd climbs: thirteen, sixteen, seventeen.
    dict(id="s12_holoplaza", seed=841567, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyg", "rgBY#g#brgbyRGby", "rgby!r#yRGbyrgBYb"]),

    # Sixteen from the first wave, the hologram's crowd.
    dict(id="s12_relaydeck", seed=802627, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrgby", "rgBY#y!gRGbyrgb", "rgby#r#brgbyRGBY"]),

    # Three shields in every wave, brutes behind them, and a bomb to answer them.
    dict(id="s12_carbridge", seed=821386, swaps=6, cogs=20, boss="",
         waves=["rgby#g#b#yRGbyr", "rgby#r#g!bRGbyrg", "rgby#b#y#rRGBYrg"]),

    # Four waves under the dishes, the bomb held for the last of them.
    dict(id="s12_dishhill", seed=796326, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyg", "rgby#ggrbyRGb", "rgby#b#yRGbyrg", "rgby!rRGbyrgb"]),

    # **The third duel: a gorgon and a shackler.** The glare wastes what is poured into a ward
    # while a chain holds another silent - two answers taken off the line at once.
    dict(id="s12_glowgate", seed=805579, swaps=6, cogs=25,
         boss="gorgon:r+shackler:g",
         waves=["rgbyRGbyrgby", "rgby#y!gRGbyRG", "rgBY#b#rRGbyrg"]),

    # ---------------------------------------------------------------- the high city
    # Brutes of one colour at a time in the middle of its creepers, then plate of it.
    dict(id="s12_skyladder", seed=844123, swaps=6, cogs=20, boss="",
         waves=["ggGGG#gggggbb", "bbBBB#bbbbb#byy", "yyYYY#yyyyRRRR#rr"]),

    # Plate and bombs in one wave, so the answer to one is standing in the other.
    dict(id="s12_fanhouse", seed=702740, swaps=5, cogs=20, boss="",
         waves=["rgbyRGbyRGbyrgb", "rgby#g#b!yRGbyrgb", "rgby#r#gRGbyRGby"]),

    # **The longest climb in the chapter**: four waves, brutes closing every one.
    dict(id="s12_glassway", seed=710176, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyg", "rgby#bbrgbyRGb", "rgby#y#r!grgbyRG", "rgbyrgbyRGBY"]),

    # Everything the chapter taught at once: brutes, three shields and a bomb in one rung.
    dict(id="s12_beaconroof", seed=710711, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGby", "rgby#g#b#yRGbyrgb", "rgby!r#gRGbyrgBY"]),

    # **The finale: a hollowking and an ironclad.** The hollowking strikes every post that has
    # fired nothing since its last cast and plate halves every bolt, so every ward has to keep
    # working through the armour. **Cogs at fifty**, Cloudkeep's finale's argument.
    dict(id="s12_neoncrown", seed=717314, swaps=5, cogs=50,
         boss="hollowking:y+ironclad:b",
         waves=["rgbyrgbyRGbyrg", "rgby#ggrbyRGbyr", "rgbyRGbyrgbyr"]),
)

#: What every rung here stands, deals and carries: the whole line, all four colours, the first
#: `ORDINAL` charms of the roster (which clamps at six, so all of them) - and the curse.
WARDS = GEMS = "rgby"


def _shipped_seeds():
    """The fields every shipped siege chapter already deals, so none is reused here. Read off
    the bodies rather than typed, so a chapter added later is covered without an edit."""
    taken = {}
    for path in sorted(OUT.parent.glob("s*.json")):
        if path.name == OUT.name:
            continue
        body = json.loads(path.read_text(encoding="utf-8"))
        for lv in body.get("levels", []):
            block = lv.get("siege") or {}
            rows = block.get("rows")
            if rows:
                taken["".join(rows)] = lv["id"]
    return taken


def rows_of(rung):
    """This rung's field, re-derived from its seed rather than typed."""
    cells = sweep.deal(rung["seed"], WIDE, TALL, GEMS)
    return sweep.rows_of(cells, WIDE, TALL)


def level(index, rung):
    x, y, afloat = mapart.places(ORDINAL)[index]

    block = {
        "width": WIDE,
        "height": TALL,
        "rows": rows_of(rung),
        "gems": GEMS,
        "wards": WARDS,
        "waves": list(rung["waves"]),
        "boss": rung["boss"],
    }

    if rung["cogs"] > 0:
        block["cogs"] = rung["cogs"]

    block["charms"] = siege.charms_upto(ORDINAL)

    # **The curse carries on from the chapter that taught it** - whether is content, how often
    # is the mode (`SiegeTuning.ObsidianPercent`), the charms' bargain.
    block["obsidian"] = True

    # **Derived from the chapter's ordinal and written down, never typed** (invariant 7c) - and
    # for this chapter the derivation names a trade (`siege.TRADED`, `SiegeTuning.Traded`).
    tough = siege.toughness_for(ORDINAL - 1)
    if tough > 10:
        block["tough"] = tough

    return {
        "id": rung["id"],
        "mapX": round(x, 3),
        "mapY": round(y, 3),
        "afloat": afloat,
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
        "accent": "#3DE0E8",
        "slate": "#1B1633",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def _layout(block):
    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
    return siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                        block.get("boss"), block.get("cogs", 0),
                        tough=block.get("tough", 0),
                        charms=block.get("charms", ""),
                        obsidian=block.get("obsidian", False))


def totals(levels):
    """Raiders, brutes and par summed over a chapter's rungs, through the one mirror."""
    raiders = brutes = hill = 0
    for lv in levels:
        layout = _layout(lv["siege"])
        read = siege.readings(layout)
        raiders += read["raiders"]
        brutes += read["brutes"]
        hill += siege.par(layout)
    return raiders, brutes, hill


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - the waves parse, every duel names two bosses the mode knows, the
    field has a legal swap - derives par and both star lines, and holds the crowd to the brief:
    at least Windwreck's, and no more than `MOST_OVER` past it. Whether a
    rung can be **held** is `SiegeRuleTests.TheEleventhChapterIsALittleHarder`, and whether every
    duel is a fight is `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 11:
        sys.exit("ordinal %d draws map%d, not the neon sky city" % (ORDINAL,
                                                                     mapart.map_of(ORDINAL)))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    # The surge is the measured figure (34 of 180 held on three-star embers against Windwreck's
    # 43); a change to it is a re-measurement, so the figure is held here rather than trusted.
    if siege.toughness_for(ORDINAL - 1) != 15:
        sys.exit("this chapter was measured at a surge of 15 tenths, and deals %d"
                 % siege.toughness_for(ORDINAL - 1))

    if siege.star_factors(ORDINAL) != siege.star_factors(ORDINAL - 1):
        sys.exit("the star lines are Windwreck's, carried over: %s against %s"
                 % (siege.star_factors(ORDINAL), siege.star_factors(ORDINAL - 1)))

    shipped = _shipped_seeds()
    verbs = {}
    total = 0

    for index, level_json in enumerate(written["levels"]):
        rung = LEVELS[index]
        block = level_json["siege"]
        layout = _layout(block)

        if layout.fault:
            sys.exit("%s: %s" % (level_json["id"], layout.fault))

        if not layout.cursed:
            sys.exit("%s: the curse carries on through this chapter and this rung does not "
                     "deal it" % level_json["id"])

        again = shipped.get("".join(block["rows"]))
        if again:
            sys.exit("%s deals the same field as the shipped %s" % (level_json["id"], again))

        # **Every fifth rung is a duel, and no other rung sends a boss.**
        want_duel = (index + 1) % BOSS_EVERY == 0
        if want_duel != (len(layout.boss_kinds) == 2) or (not want_duel and layout.boss_kinds):
            sys.exit("%s: rung %d %s" % (level_json["id"], index + 1,
                                         "must be a duel" if want_duel else "sends a boss"))

        # **No verb twice across this chapter's four duels** - every duel is a fight of its own.
        for kind in layout.boss_kinds:
            spell = siege.BOSSES[kind]["spell"]
            if spell in verbs:
                sys.exit("%s sends a %s and so does %s" % (level_json["id"], spell, verbs[spell]))
            verbs[spell] = level_json["id"]

        for w, line in enumerate(layout.coming):
            if w == layout.boss_wave:
                continue
            if not LEAST_WAVE <= len(line) <= MOST_WAVE:
                sys.exit("%s: wave %d sends %d raiders; this chapter's waves send %d to %d"
                         % (level_json["id"], w + 1, len(line), LEAST_WAVE, MOST_WAVE))

        if layout.raiders > siege.MAX_RAIDERS:
            sys.exit("%s: %d raiders" % (level_json["id"], layout.raiders))

        par = siege.par(layout)
        read = siege.readings(layout)
        total += read["raiders"]

        if not want_duel and read["brutes"] < LEAST_BRUTES:
            sys.exit("%s sends %d brutes; every ordinary rung of this chapter sends at least %d"
                     % (level_json["id"], read["brutes"], LEAST_BRUTES))

        made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
        if made != rung["swaps"]:
            sys.exit("%s: seed %d now deals %d opening swaps, not the %d recorded"
                     % (level_json["id"], rung["seed"], made, rung["swaps"]))

        widest = max(len(line) for line in layout.coming)
        tail = ""
        if read["kind"]:
            tail += ", a duel of %s (%s) last" % (read["kind"], read["spell"])

        print("%-18s par %-4d 3* %-4d 2* %-4d %2d raider(s) in %d wave(s), widest %2d, "
              "%2d brute(s), %d shielded, cogs %2d%% (~%d a run)%s"
              % (level_json["id"], par,
                 proto.over(par, round(siege.star_factors(ORDINAL)[0] * 100)),
                 proto.over(par, round(siege.star_factors(ORDINAL)[1] * 100)),
                 read["raiders"], read["waves"], widest, read["brutes"], read["bulwarks"],
                 read["cogs"], read["drops"], tail))

        if not read["swap"]:
            sys.exit("%s: this field has no opening swap" % level_json["id"])

    ids = [lv["id"] for lv in written["levels"]]
    if len(set(ids)) != len(ids):
        sys.exit("two levels of this chapter share an id")

    # **The crowd, against the chapter it was commissioned against**, read through one mirror:
    # never less than Windwreck and never more than "a little" over. Hill is printed only.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "hill (par)"), ours, theirs):
        over = (mine - before) * 100.0 / before
        print("%-11s %5d against Windwreck's %5d (%+.1f%%)" % (name, mine, before, over))
        if name not in MOST_OVER:
            continue
        if mine < before:
            sys.exit("the brief is a little harder than Windwreck, and this chapter sends %d %s "
                     "against %d" % (mine, name, before))
        if over > MOST_OVER[name]:
            sys.exit("the brief is *a little* harder than Windwreck, and %s is %+.1f%% over it "
                     "against a ceiling of %d%%" % (name, over, MOST_OVER[name]))

    print("%d raiders across the chapter" % total)
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
        OUT.write_bytes(text.encode("utf-8"))
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
