"""Ironbastion - the twelfth Thornwatch chapter: the military sky fortress, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-10-19, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 151 to 170**, on the owner's own painting - a fortress of floating rocks
    carrying a tank, jet thrusters, cranes, a hangar, a mech bay, a radar dish and watchtowers,
    joined by a steel road edged in hazard stripes - with every node standing **on the road**
    (`make_map_seats.TRACES[12]`, walked up the road the painting draws).
  * **"Make sure it's harder than the previous chapter."** The crowd is held to *more* than
    Neonhaven's on every count `prove` reads (raiders, brutes, shields, bombers) and to no more
    than `MOST_OVER` past it, so the step is a step and not a wall; **the surge is the dial**, set
    by play (`SiegeTuning.Traded`, row `(11, 17)` - two tenths over Neonhaven's 1.5). A first cut at
    1.6 with spikier waves held 29 of 180 on three-star embers against Neonhaven's 64 - a wall - so
    it went to the owner's device at 1.5 with the spikes smoothed; the owner played it and found it
    "a little easy", and chose 1.7 (21 of 180 on three-star embers, 118 on the strongest line
    against Neonhaven's 151). Whether it plays harder is measured, not read off these numbers:
    `SiegeRuleTests.TheTwelfthChapterIsHarder` holds it under Neonhaven on the good line and the
    strongest line, and over a floor that keeps it from being a wall.
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *armada* again (`SiegeMode.MainCasts`): five squares exhaust the six casts, so the twelfth
    chapter wraps rather than cutting art, and it wraps onto the heaviest square - the one whose
    every brute and bulwark swings - rather than onto the first chapter's beetles. The four duels
    are four pairs nobody has fought, of eight verbs, none twice in the chapter (37ey).
  * **The curse and the void stone carry on**, as every taught mechanic does: every rung deals the
    obsidian and the singularity.

**The four duels, and what each pair asks.**

  * **Rung 5, a warbringer and a bonecaller** - a rally hastens the crowd while the bonecaller
    raises more of it: the opening duel is a hill that keeps filling.
  * **Rung 10, an overlord and a warlord** - a sunder takes ranks off a ward while a smite lands
    on the line: the two oldest heavy hitters, together for the first time.
  * **Rung 15, an ironclad and a shackler** - plate halves every bolt while a chain holds a ward
    silent: the answer has to come through the armour from the three still free.
  * **Rung 20, a sunlord and a harrower** - a doom counts down on a ward while the harrow throws
    its ranks on the hill as cogs to win back: the finale is fought for the line itself.
    **Cogs at fifty**, the finale's argument since Cloudkeep.

**The duels were placed by the fight gate** (`EveryShippedBossRungIsAFight`, the strongest line):
a sunlord made any rung it stood on the hardest of the chapter (a shackler beside it fell 3 of 9 at
any colours, a warlord beside it 3 of 9), so it is the finale's, where Windwreck's sunlord also
stands; the four fall 8, 8, 9 and 3 of 9 - a climb with its peak at the crown.

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s14_ironbastion.json"

CHAPTER = "s14_ironbastion"

#: The chapter this one is commissioned against: harder than it.
BEFORE = OUT.parent / "s12_neonhaven.json"

#: This chapter's place inside its own mode and track, which decides its map (`map12`, the owner's
#: military sky fortress), its skies (the fourth block again - forty skies wrap at four chapters),
#: its cast (`SiegeMode.MainCasts`, the armada again), its charms (all six; `SiegeCharms.Upto`
#: clamps) and its surge (the fifth row of `SiegeTuning.Traded`).
ORDINAL = 12

#: The surge this chapter was measured at, in tenths - held here so a change to `siege.TRADED` is a
#: re-measurement rather than a silent retune.
SURGE = 17

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: The fewest and most raiders a wave of this chapter sends, **bosses aside** - one over
#: Neonhaven's ceiling, because a heavier crowd is half of this chapter's step.
LEAST_WAVE, MOST_WAVE = 12, 18

#: The fewest brutes a rung that is not a duel sends - Neonhaven's floor, kept.
LEAST_BRUTES = 6

#: **Harder, and not a wall**, as numbers: the crowd may not fall under Neonhaven's on any of these
#: and may not run further past it than this, in per cent. Hill (par) is not bounded here, because
#: it carries the surge, and the surge is set by play rather than by arithmetic.
MOST_OVER = {"raiders": 10, "brutes": 20, "shields": 40, "bombers": 60}

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses
#: joined by `+` - or is empty for a siege that sends none. Fields open at six swaps and tighten
#: to five for the last four rungs, Neonhaven's shape. **No seed is shared with any shipped
#: chapter**, so no field here is one a player has already seen.
LEVELS = (
    # ---------------------------------------------------------------- the thruster rocks
    # **The tank yard.** Armour from the first wave: brutes and a shield in every one.
    dict(id="s14_tankyard", seed=1002745, swaps=6, cogs=20, boss="",
         waves=["rgbyRG#gbyrgbyr", "rgbyRGby#byRGby", "rgBY#rrgRGbyrgby"]),

    # The thruster deck: the biggest crowd yet, brutes closing every wave.
    dict(id="s14_thrusterdeck", seed=1002984, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbyRGby", "rgbyrgby#grgbyRGBY", "rgbyrgbyRGbyrgBYr"]),

    # **Pincers**: two colours down the hill, then the other two, then all four in plate.
    dict(id="s14_cranepoint", seed=1004146, swaps=6, cogs=20, boss="",
         waves=["rrggRGrrggrg#rg", "bbyyBBYYbbyy#by", "rgbyRGBY#r#g#b#yrg"]),

    # Bombers turning the field under the gear, two in each of the last waves.
    dict(id="s14_gearworks", seed=1010326, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!gRGbyr", "rgBY#r!yRGby!brg", "rgby!b#gRGbyRGby!r"]),

    # **The first duel: a warbringer and a bonecaller** - a rally hastens the crowd while the
    # bonecaller raises more of it.
    dict(id="s14_trestlegate", seed=1017057, swaps=6, cogs=35,
         boss="warbringer:r+bonecaller:y",
         waves=["rgbyRGbyrgbyrg", "rgby#y!rRGbyRGb", "rgBY#g#rRGbyrg"]),

    # ---------------------------------------------------------------- the stair rocks
    # **One colour at a time, in a flood**, a brute triple at its head and plate behind it.
    dict(id="s14_gunshipdock", seed=1018614, swaps=6, cogs=20, boss="",
         waves=["yyYYYyyyyy#yrrr", "rrRRRrrrrr#rggg", "ggGG#ggggBBB#bbbb"]),

    # Four waves up the long stair, the third of them armoured and bombed at once.
    dict(id="s14_longstair", seed=1025980, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyrg", "rgby#g#bRGbyRGb", "rgby!ggrBY#yybrg", "rgbyRGbyrgbyr"]),

    # **A column**: three shields at the head of every wave, brutes behind them.
    dict(id="s14_pipeworks", seed=1028314, swaps=6, cogs=20, boss="",
         waves=["#r#g#bRGBYrgbyrg", "#b#y#gRGbyrgbyrgb", "#g#r#yRGbyrgbyrg"]),

    # **The rehearsal**: four waves and no boss, a brute pair in every one.
    dict(id="s14_hangarbay", seed=1030356, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrg", "rgby#grgbyRGbyr", "rgby#y!rgBYrgby", "rgbyrgbyRGbyrg"]),

    # **The second duel: an overlord and a warlord.** A sunder takes ranks off a ward while a
    # smite lands on the line - the two oldest heavy hitters, together for the first time.
    dict(id="s14_chainbridge", seed=1039283, swaps=6, cogs=45,
         boss="overlord:b+warlord:y",
         waves=["rgbyRGbyrgbyr", "rgby#gRGbyrgbRGb", "rgby#b#yRGbyrg"]),

    # ---------------------------------------------------------------- the hangar rocks
    # **A wall of sandbags**: a pair of shields in every wave.
    dict(id="s14_sandbagrow", seed=1039941, swaps=6, cogs=20, boss="",
         waves=["rgby#r#gRGbyrgby", "rgby#y#bRGbyrgby", "rgBY#b#yRGbyrgby"]),

    # The crowd climbs over the grated bridge: fourteen, fifteen, sixteen.
    dict(id="s14_grateway", seed=1042384, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrg", "rgBY#g#brgbyRGbyr", "rgby!r#yRGbyrgBYbg"]),

    # Bombs and plate in every wave, so the answer to one is standing in the other.
    dict(id="s14_cratestack", seed=1045476, swaps=6, cogs=20, boss="",
         waves=["rgby#g!bRGbyrgby", "rgby#r#g!bRGbyrgb", "rgBY!y#bRGbyrgBY"]),

    # **The mech bay**: the heaviest hill of the chapter outside a duel, brutes from the first.
    dict(id="s14_mechbay", seed=1046033, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyrg", "rgbyRGby#rRGbyrg", "RGbyrgby#grgbyrgBY"]),

    # **The third duel: an ironclad and a shackler** - plate halves every bolt while a chain holds
    # a ward silent.
    dict(id="s14_watchtower", seed=1054705, swaps=6, cogs=25,
         boss="ironclad:r+shackler:b",
         waves=["rgbyRGbyrgbyg", "rgby#y!gRGbyRGb", "rgBY#b#rRGbyrg"]),

    # ---------------------------------------------------------------- the radar rocks
    # Brutes of one colour at a time, a pair at the head of its creepers, then plate of it.
    dict(id="s14_girderspan", seed=1092968, swaps=6, cogs=20, boss="",
         waves=["ggGG#gggggbbr", "bbBB#bbbbbb#by", "yyYY#yyyyyrr#rr"]),

    # **The longest climb in the chapter**: four waves, the last of them led by every colour's brute.
    dict(id="s14_radarpost", seed=1001998, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyg", "rgby#bbrgbyRGby", "rgby#y#r!grgbyRG", "RGBYrgbyRGbyrg"]),

    # The ammunition dump: a bomber at the head of every wave and a second in the middle one.
    dict(id="s14_ammodump", seed=1012856, swaps=5, cogs=20, boss="",
         waves=["!rrgbyRGbyrgbyRG", "!grgby#bRGby!yrgb", "!brgBY#rRGbyrgBYg"]),

    # Everything the chapter taught at once: brutes, three shields and a bomb in one rung.
    dict(id="s14_searchlight", seed=1016049, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGbyr", "rgby#g#b#yRGby!rgb", "rgby!r#gRGbyrgBYRG"]),

    # **The finale: a sunlord and a harrower.** A doom counts down on a ward while the harrow
    # throws its ranks on the hill as cogs to win back - the finale is fought for the line itself.
    # **Cogs at fifty**, Cloudkeep's finale's argument; the hardest duel of the chapter by the fight
    # gate (3 of 9 on the strongest line, Windwreck's sunlord finale 2).
    dict(id="s14_highcommand", seed=1052050, swaps=5, cogs=50,
         boss="sunlord:g+harrower:r",
         waves=["rgbyrgbyRGbyrg", "rgby#ggrbyRGbyrg", "rgbyRGbyrgbyRG"]),
)

#: What every rung here stands, deals and carries: the whole line, all four colours, the first
#: `ORDINAL` charms of the roster (which clamps at six, so all of them), the curse and the void.
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

    # **The curse and the void stone carry on from the chapters that taught them** - whether is
    # content, how often is the mode (`SiegeTuning.ObsidianPercent`, `.SingularityPermille`).
    block["obsidian"] = True
    block["singularity"] = True

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
        "accent": "#F2A93B",
        "slate": "#1C2230",
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
                        obsidian=block.get("obsidian", False),
                        singularity=block.get("singularity", False))


def totals(levels):
    """Raiders, brutes, shields, bombers and par summed over a chapter's rungs, through the one
    mirror."""
    raiders = brutes = shields = bombers = hill = 0
    for lv in levels:
        layout = _layout(lv["siege"])
        read = siege.readings(layout)
        raiders += read["raiders"]
        brutes += read["brutes"]
        shields += read["bulwarks"]
        bombers += sum(1 for line in layout.coming for _, kind in line if kind == "bomber")
        hill += siege.par(layout)
    return raiders, brutes, shields, bombers, hill


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - the waves parse, every duel names two bosses the mode knows, the
    field has a legal swap - derives par and both star lines, and holds the crowd to the brief:
    more than Neonhaven's on every count, and no more than `MOST_OVER` past it. Whether a rung can
    be **held** is `SiegeRuleTests.TheTwelfthChapterIsHarder`, and whether every duel is a fight
    is `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 12:
        sys.exit("ordinal %d draws map%d, not the military sky fortress"
                 % (ORDINAL, mapart.map_of(ORDINAL)))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    # The surge is a measured figure; a change to it is a re-measurement, so it is held here
    # rather than trusted.
    if siege.toughness_for(ORDINAL - 1) != SURGE:
        sys.exit("this chapter was measured at a surge of %d tenths, and deals %d"
                 % (SURGE, siege.toughness_for(ORDINAL - 1)))

    shipped = _shipped_seeds()
    verbs = {}
    total = 0

    for index, level_json in enumerate(written["levels"]):
        rung = LEVELS[index]
        block = level_json["siege"]
        layout = _layout(block)

        if layout.fault:
            sys.exit("%s: %s" % (level_json["id"], layout.fault))

        if not layout.cursed or not layout.singular:
            sys.exit("%s: the curse and the void stone carry on through this chapter and this "
                     "rung does not deal both" % level_json["id"])

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
    # never less than Neonhaven on any count, and never more than `MOST_OVER` past it. Hill is
    # printed only.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "shields", "bombers", "hill (par)"),
                                  ours, theirs):
        over = (mine - before) * 100.0 / before
        print("%-11s %5d against Neonhaven's %5d (%+.1f%%)" % (name, mine, before, over))
        if name not in MOST_OVER:
            continue
        if mine <= before:
            sys.exit("the brief is harder than Neonhaven, and this chapter sends %d %s against %d"
                     % (mine, name, before))
        if over > MOST_OVER[name]:
            sys.exit("the brief is harder than Neonhaven and not a wall, and %s is %+.1f%% over "
                     "it against a ceiling of %d%%" % (name, over, MOST_OVER[name]))

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
