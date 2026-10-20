"""Wyrmforge - the thirteenth Thornwatch chapter: the dragon forge in the sky, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-10-20, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 171 to 190**, on the owner's own painting - a forge of floating rocks
    carrying smelting kilns, anvils and sword racks, a stone king leaning on his hammer and a
    dragon's skeleton, lava falling off every edge, joined by a flagstone road edged in bronze -
    with every node standing **on the road** (`make_map_seats.TRACES[13]`, walked up the road the
    painting draws).
  * **"Harder than the previous chapter, 90% health bonus."** The surge is the figure the owner
    named: 1.9 (`SiegeTuning.Traded`, row `(12, 19)` - two tenths over Ironbastion's 1.7, under
    the 2.1 the ladder would derive). The crowd is held to *more* than Ironbastion's on every count
    `prove` reads (raiders, brutes, shields, bombers) and to no more than `MOST_OVER` past it, so
    the health is the step and the crowd only leans on it - Ironbastion's lesson was that spiky
    rungs wall the good line long before a surge does. Whether it plays harder is measured, not
    read off these numbers: `SiegeRuleTests.TheThirteenthChapterIsHarder` holds it under
    Ironbastion on the good line and the strongest line, and over a floor that keeps it from
    being a wall.
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *court* again (`SiegeMode.MainCasts`): the table would otherwise wrap onto the first chapter's
    insects, and the court - skeleton knights in bone plate, every body of it swinging - is the
    crowd a dragon's forge asks for, unseen since the sixth chapter. The four duels are four pairs
    nobody has fought, of eight verbs, none twice in the chapter (37ey).
  * **The curse and the void stone carry on**, as every taught mechanic does: every rung deals the
    obsidian and the singularity.

**The four duels, and what each pair asks.**

  * **Rung 5, a gravemaw and a gorgon** - a devour takes every cog and bomb left lying on the hill
    while a glare turns the fullest ward stone-struck: the opening duel asks the player to spend
    what the hill drops at once, and to feed the colours the gorgon is not looking at.
  * **Rung 10, a warlord and a colossus** - a smite takes a ward's health while the colossus
    buries another under rubble only taps dig out: the line is hurt and short-handed at once.
  * **Rung 15, an overlord and a shackler** - a sunder takes health and a rank off a ward while a
    chain holds another silent: two wards answered at once, under the stone king.
  * **Rung 20, a hollow king and a warbringer** - a wane strikes every ward that has fired nothing
    since the last cast while a rally sets the whole hill charging: the finale asks the player to
    keep all four tubes working while the crowd runs at them. **Cogs at fifty**, the finale's
    argument since Cloudkeep.

**The duels were placed by the fight gate** (`EveryShippedBossRungIsAFight`, the strongest line).
At 1.9 a **sunlord walls the finale**: it was never felled beside a bonecaller or a thunderer, and
beside a blightcaller fell 1 or 2 of 9 at any colours, with a lighter lead-in moving nothing - so
the doom is left out of this chapter, and the crown is a hollow king and a warbringer (3 of 9, the
hardest of the four). The middle duels came down from 2 and 1 of 9 by **cogs** (45 to 50, 30 to
45), one shield and one bomber fewer and a raider less a wave - the lever that moves no par. The
four fall 8, 6, 6 and 3 of 9: a climb with its peak at the crown, Ironbastion's shape (6, 3, 7, 4
at 1.7).

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s15_wyrmforge.json"

CHAPTER = "s15_wyrmforge"

#: The chapter this one is commissioned against: harder than it.
BEFORE = OUT.parent / "s14_ironbastion.json"

#: This chapter's place inside its own mode and track, which decides its map (`map13`, the owner's
#: dragon forge), its skies (the first block again - forty skies wrap at four chapters), its cast
#: (`SiegeMode.MainCasts`, the court again), its charms (all six; `SiegeCharms.Upto` clamps) and its
#: surge (the sixth row of `SiegeTuning.Traded`).
ORDINAL = 13

#: The surge this chapter deals, in tenths - the owner's "90% health bonus", held here so a change
#: to `siege.TRADED` is a re-measurement rather than a silent retune.
SURGE = 19

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: The fewest and most raiders a wave of this chapter sends, **bosses aside** - Ironbastion's
#: bounds, kept: the step is the surge, and a wider wave is the spike that walls a good line.
LEAST_WAVE, MOST_WAVE = 12, 18

#: The fewest brutes a rung that is not a duel sends - Ironbastion's floor, kept.
LEAST_BRUTES = 6

#: **Harder, and not a wall**, as numbers: the crowd may not fall under Ironbastion's on any of
#: these and may not run further past it than this, in per cent - tighter than Ironbastion's own
#: ceilings, because this chapter's step is the surge the owner named and the crowd only leans on
#: it. Hill (par) is not bounded here, because it carries the surge.
MOST_OVER = {"raiders": 6, "brutes": 12, "shields": 25, "bombers": 50}

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses
#: joined by `+` - or is empty for a siege that sends none. Fields open at six swaps and tighten
#: to five for the last four rungs, Ironbastion's shape. **No seed is shared with any shipped
#: chapter**, so no field here is one a player has already seen.
LEVELS = (
    # ---------------------------------------------------------------- the rune rocks
    # **The rune gate.** Plate from the first wave: brutes and a shield in every one.
    dict(id="s15_runegate", seed=37133533, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#rgbyrgb", "rgby#bRGby!rgbyRG", "rgBY#y#grgbyRGbyr"]),

    # The lavafall under the first tower: the biggest crowd yet, brutes closing every wave.
    dict(id="s15_lavafall", seed=40068459, swaps=6, cogs=20, boss="",
         waves=["rgby#rgbyRGbyrgby", "rgbyRGby#rrgbyRGby", "rgbyRGbyrgBY#grgb"]),

    # **Pincers** up the ember stair: two colours, then the other two, then all four in plate.
    dict(id="s15_emberstair", seed=74585180, swaps=6, cogs=20, boss="",
         waves=["rrggRGrrgg#rrg", "bbyyBYbbyy#bby", "rgbyRGBY#r#bgyrgby"]),

    # Bombers under the ballista, two in each of the last waves.
    dict(id="s15_ballistaledge", seed=84527166, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!rRGbyrg", "rgBY#g!bRGby!yrg", "rgby!g#rRGbyRGby!b"]),

    # **The first duel: a gravemaw and a gorgon** - the hill's cogs and bombs are eaten while the
    # fullest ward is struck to stone.
    dict(id="s15_chainhollow", seed=86536001, swaps=6, cogs=35,
         boss="gravemaw:r+gorgon:y",
         waves=["rgbyRGbyrgbyrgb", "rgby#b!gRGbyRGb", "rgBY#y#gRGbyrg"]),

    # ---------------------------------------------------------------- the anvil rocks
    # **One colour at a time, in a flood**, a brute triple at its head and plate behind it.
    dict(id="s15_brandstone", seed=95835634, swaps=6, cogs=20, boss="",
         waves=["bbBBBbbbbb#brrr", "yyYYYyyyyy#yggg", "rrRR#rrrrGGG#gggg"]),

    # Four waves across the anvil rock, the third of them armoured and bombed at once.
    dict(id="s15_anvilrock", seed=110812706, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyrgb", "rgby#r#yRGbyRGb", "rgby!bbgRG#grrby", "rgbyRGby#rgbyRG"]),

    # **A column**: three shields at the head of every wave, brutes behind them, cogs at 25. **A
    # column is the most field-bound shape in the mode**: at 1.9 the strongest line held it 0 of 9
    # on the first seed and on ten of thirteen others, whatever the colours or the surge, and
    # 6 of 9 on this one (Ironbastion's own column holds 4 at 1.9).
    dict(id="s15_bladerack", seed=1312586, swaps=6, cogs=25, boss="",
         waves=["#y#r#gRGBYrgbyrg", "#g#b#rRGbyrgbyrgb", "#b#y#rRGbyrgbyrg"]),

    # **The rehearsal**: four waves and no boss, a brute pair in every one.
    dict(id="s15_beacontower", seed=256917377, swaps=6, cogs=20, boss="",
         waves=["rgby#rgbyRGbyrgb", "rgby#brgbyRGbyr", "rgby#g!yrgBYrgby", "rgbyrgbyRGBYrgb"]),

    # **The second duel: a warlord and a colossus** - a smite on one ward while another is buried
    # under rubble only taps dig out.
    dict(id="s15_kilnheart", seed=262063772, swaps=6, cogs=50,
         boss="warlord:g+colossus:b",
         waves=["rgbyRGbyrgbyr", "rgby#rRGbyrgbRG", "rgby#gRGbyrgb"]),

    # ---------------------------------------------------------------- the kiln rocks
    # **A wall of slag**: a pair of shields in every wave.
    dict(id="s15_slagrun", seed=268037445, swaps=6, cogs=20, boss="",
         waves=["rgby#g#bRGby!rgby", "rgby#r#yRGBYrgbyr", "rgBY#y#rRGbyrgby"]),

    # The crowd climbs under the banners: fifteen, sixteen, seventeen.
    dict(id="s15_bannerwatch", seed=339515338, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGby!rgb", "rgBY#b#yrgbyRGbyr", "rgby!g#rRGbyrgBYby"]),

    # Bombs and plate in every wave, so the answer to one is standing in the other.
    dict(id="s15_ashbridge", seed=344555554, swaps=6, cogs=20, boss="",
         waves=["rgby#b!yRGbyrgby", "rgby#g#r!yRGbyrgb", "rgBY!r#gRGbyrgBY"]),

    # **The rune spire**: the heaviest hill of the chapter outside a duel, brutes from the first.
    dict(id="s15_runespire", seed=345365259, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#rgbyrgb", "rgbyRGby#bRGbyrg", "RGbyrgby#yrgbyrgBY"]),

    # **The third duel: an overlord and a shackler** - a sunder takes a rank off one ward while a
    # chain holds another silent, under the stone king's hammer.
    dict(id="s15_kingshammer", seed=452549889, swaps=6, cogs=45,
         boss="overlord:y+shackler:r",
         waves=["rgbyRGbyrgbyr", "rgby#gRGbyRGb", "rgBY#rRGbyrgb"]),

    # ---------------------------------------------------------------- the wyrm rocks
    # Brutes of one colour at a time, a pair at the head of its creepers, then plate of it.
    dict(id="s15_stonewarden", seed=503142067, swaps=6, cogs=20, boss="",
         waves=["rrRRR#rrrrggy", "yyYYY#yyyyy#yb", "bbBBB#bbbbgg#gg"]),

    # **The longest climb in the chapter**: four waves past the dragon's bones, the last of them
    # led by every colour's brute.
    dict(id="s15_wyrmbones", seed=13489320, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrb", "rgby#yyrgbyRGby", "rgby#g#b!rrgbyRG", "RGBYrgbyRGbyrg"]),

    # The dragon's jaw: a bomber at the head of every wave and a second in the middle one.
    dict(id="s15_dragonsjaw", seed=66869575, swaps=5, cogs=20, boss="",
         waves=["!yrgbyRGbyrgbyRG", "!rrgby#gRGby!brgb", "!grgBY#bRGbyrgBYy"]),

    # Everything the chapter taught at once: brutes, three shields and a bomb in one rung.
    dict(id="s15_furnacecrown", seed=163158222, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGbyg", "rgby#r#g#yRGby!rgb", "rgby!b#yRGbyrgBYRG"]),

    # **The finale: a hollow king and a warbringer.** A wane strikes every idle ward while a rally
    # sets the hill charging - keep all four firing. **Cogs at fifty**, Cloudkeep's finale's
    # argument; the hardest duel of the chapter by the fight gate (3 of 9 on the strongest line).
    dict(id="s15_wyrmcrown", seed=200961832, swaps=5, cogs=50,
         boss="hollowking:y+warbringer:g",
         waves=["rgbyrgbyRGbyrg", "rgby#yyrbyRGbyr", "rgbyRGbyrgbyRG"]),
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
        "accent": "#FF8A3D",
        "slate": "#2A1C1A",
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
    more than Ironbastion's on every count, and no more than `MOST_OVER` past it. Whether a rung can
    be **held** is `SiegeRuleTests.TheTwelfthChapterIsHarder`, and whether every duel is a fight
    is `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 13:
        sys.exit("ordinal %d draws map%d, not the dragon forge"
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
    # never less than Ironbastion on any count, and never more than `MOST_OVER` past it. Hill is
    # printed only.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "shields", "bombers", "hill (par)"),
                                  ours, theirs):
        over = (mine - before) * 100.0 / before
        print("%-11s %5d against Ironbastion's %5d (%+.1f%%)" % (name, mine, before, over))
        if name not in MOST_OVER:
            continue
        if mine <= before:
            sys.exit("the brief is harder than Ironbastion, and this chapter sends %d %s against %d"
                     % (mine, name, before))
        if over > MOST_OVER[name]:
            sys.exit("the brief is harder than Ironbastion and not a wall, and %s is %+.1f%% over "
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
