"""Cogspire - the ninth Thornwatch chapter: the clockwork islands, twenty rungs, four duels and the curse.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-09-28, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 91 to 110**, on the owner's own painting - clockwork sky islands joined
    by a copper road - with every node standing **on the road** (`make_map_seats.TRACES`, which
    walks the chain up the road the painting draws rather than searching the picture for ground).
  * **Harder than Cloudkeep in health and in headcount, "but not too much".** Raiders carry five
    tenths more health than the baseline against Cloudkeep's four (`SiegeTuning.Traded`, one
    tenth, not the three the ladder would derive), and **8% more bodies** - 785 raiders against
    Cloudkeep's 726, eleven to fifteen a wave - with the extra bodies creepers and the brutes
    fewer, because a crowd of brutes is a wall (Cloudkeep's own tuning lesson).
    **Measured, not argued** (a throwaway probe of `SiegeRuleTests`' own player, four one-star ember
    turrets at nine rhythms, 2026-09-28): this cut holds **27 of 180** runs against Cloudkeep's 31.
    The first cut sent 849 raiders with Cloudkeep's brute share and held **10 of 180** - a third of
    Cloudkeep's, with the strongest line on the shelf never reaching the first duel - which is
    "much harder", not "not too much". The stones were not the cause: the same waves with no curse
    held 8, and at Cloudkeep's surge 16. **Headcount at a fresh tenth is the cliff** (37ef), so the
    trim is in the bodies' weight, not in the surge the brief asked for.
  * **A new mechanic: the obsidian** - the cursed stone from the Pairs challenges, dealt into the
    refill like a fifth gem but rarer (`SiegeTuning.ObsidianPercent`). It burns in no ward, so it is
    clutter; three in a line **break the curse**: every obsidian on the field is drawn into the
    break and shatters with it, and every raider on the hill is **hexed** - it takes half as much
    again from everything the line throws, for longer the more stones broke. Positive for the
    player, and a decision about *when* (break it over a crowd). See `SiegeLayout.Obsidian`.
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *gathering* (`SiegeMode.GatheringOrder`), a third square over the six chapter casts sharing no
    slot with the medley or the reunion; and the four duels pair the six verbs no duel had sent yet
    (a warlord, a warbringer, an overlord, a bonecaller, an ironclad and a colossus) and then two
    old enemies in a pair nobody has fought (a gorgon and a hollowking).
  * **Not balanced**, at the owner's word: tuned to be a step past Cloudkeep by construction (more
    bodies, one more tenth) and left for the owner's sweep, exactly as Cloudkeep's floors were -
    `SiegeRuleTests.TheNinthChapterIsFoughtOnABoughtLine` ships with its floors UNSET.

**The four duels, and what each pair asks.**

  * **Rung 5, a warlord and a warbringer** - a smite takes a ward and a roar sends the whole hill
    charging; a hex broken just before the roar is the curse at its most valuable.
  * **Rung 10, an overlord and a bonecaller** - a sunder takes a rank off a ward while the
    bonecaller raises a fresh crowd of creepers, which is exactly the hill a curse wants to fall on.
  * **Rung 15, an ironclad and a colossus** - plate that halves the line's bolts and a boulder that
    buries a turret: a hex is the answer to the first, and digging out is the answer to the second.
  * **Rung 20, a gorgon and a hollowking** - a glare says stop feeding one ward; a wane strikes
    every ward that has gone quiet. The finale asks for both at once.

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s10_cogspire.json"

CHAPTER = "s10_cogspire"

#: This chapter's place inside its own mode and track, which decides its map (`map9`, the owner's
#: clockwork islands), its skies (the first block again - forty skies wrap at four chapters), its
#: cast (the gathering), its charms (all six; `SiegeCharms.Upto` clamps) and its surge (the second
#: row of `SiegeTuning.Traded`).
ORDINAL = 9

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: The fewest and most raiders a wave of this chapter sends, **bosses aside**. The floor is the
#: brief - a crowd bigger than Cloudkeep's ten - and it is proved below rather than trusted.
LEAST_WAVE, MOST_WAVE = 11, 15

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses
#: joined by `+` - or is empty for a siege that sends none. Fields open at six swaps and tighten
#: to five for the last four rungs, Cloudkeep's shape. **No seed is shared with any shipped
#: chapter**, so no field here is one a player has already seen.
LEVELS = (
    # ---------------------------------------------------------------- the lower islands
    # **Thirteen from the first wave**, one past where Cloudkeep opened - and the first rung the
    # curse is dealt on, so the crowd is plain and the lesson has room.
    dict(id="s10_brassgate", seed=425125, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbyr", "rgbyrgbyRGbyr", "rgbyRGby#r#grgb"]),

    # Thirteen, thirteen, fifteen and nothing armoured: a crowd to break a curse over.
    dict(id="s10_windmill", seed=425480, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbyr", "rgbyRGbyrgbyr", "rgbyRGbyrgbyrgb"]),

    # Two shields a wave inside the crowd.
    dict(id="s10_lampwick", seed=431845, swaps=6, cogs=20, boss="",
         waves=["rgby#rRGbyrgb", "rgbyrgby#b#yRGb", "rgby#r#gRGbyrgb"]),

    # Bombers inside the crowd - a plus of five boxes on a hill this full is five raiders.
    dict(id="s10_gearhouse", seed=440130, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!rRGb", "rgBY#gRGby!br", "rgBY!y#rRGbyr"]),

    # **The first duel: a warlord and a warbringer.** A smite takes a ward; a roar sends the whole
    # hill charging - so a curse broken before the roar lands is the curse at its most valuable.
    dict(id="s10_boilerheart", seed=442670, swaps=6, cogs=25,
         boss="warlord:r+warbringer:y",
         waves=["rgbyRGbyrgb", "rgby#r!gRGbyr", "rgBY#b#yRGbyr"]),

    # ---------------------------------------------------------------- the dock islands
    # **One colour at a time, in a flood**, a dozen of it before the next arrives.
    dict(id="s10_steamvent", seed=520266, swaps=6, cogs=20, boss="",
         waves=["rrRrrrrrr#rgg", "ggGgggggg#gbbb", "bbB#bbbbbYYY#yyy"]),

    # Four waves past the airship, the third of them armoured and bombed at once.
    dict(id="s10_airdock", seed=449154, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgb", "rgby#r#gRGbyr", "rgby!rrgBY#bb", "rgbyrgbyrgb"]),

    # Plate and a bomb behind a creeper crowd, brutes at the back of the first and last waves.
    dict(id="s10_chainbridge", seed=453337, swaps=6, cogs=20, boss="",
         waves=["rgBYrgbyrgbyr", "rgby#g#brgbyrgb", "rgBY!grgby#rrgbyr"]),

    # **The rehearsal**: four waves and no boss - two minutes of feeding the line evenly.
    dict(id="s10_plankway", seed=453670, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgb", "rgby#rrgbyRG", "rgby#brgbyrgb", "rgBYrgbyrgb"]),

    # **The second duel: an overlord and a bonecaller.** A sunder takes a rank; a raise brings a
    # fresh crowd of creepers onto the hill - which is exactly the hill a curse wants to fall on.
    dict(id="s10_millwheel", seed=456098, swaps=6, cogs=30,
         boss="overlord:b+bonecaller:g",
         waves=["rgbyrgbyrgb", "rgby#yRGbyrgb", "rgby#g#rrgbyr"]),

    # ---------------------------------------------------------------- the middle islands
    # The crowd climbs: thirteen, fifteen, sixteen.
    dict(id="s10_axlegate", seed=465821, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyr", "rgBY#r#grgbyRGb", "rgby!b#yRGbyrgby"]),

    # Fifteen from the first wave, which nothing in this mode has opened on before.
    dict(id="s10_orecart", seed=468026, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbyrgb", "rgBY#b!rRGbyrgb", "rgby#g#yrgbyrgb"]),

    # Three shields in every wave - the most plate the mode carries - and a bomb to answer them.
    dict(id="s10_armillary", seed=469214, swaps=6, cogs=20, boss="",
         waves=["rgby#r#g#brgby", "rgby#y#r!gRGbyr", "rgby#g#b#yRGBYr"]),

    # Four waves under the coils, the bomb held for the last of them.
    dict(id="s10_coilyard", seed=476797, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgb", "rgby#rrgbyrg", "rgby#g#brgbyr", "rgby!yrgbyrg"]),

    # **The third duel: an ironclad and a colossus.** Plate that halves the line's bolts, and a
    # boulder that buries a turret - a hex is the answer to the first and digging out the second.
    dict(id="s10_sparkspire", seed=509968, swaps=6, cogs=25,
         boss="ironclad:y+colossus:r",
         waves=["rgbyRGbyrgb", "rgby#b!rRGbyr", "rgBY#r#gRGbyr"]),

    # ---------------------------------------------------------------- the high islands
    # Brutes of one colour at a time in the middle of its creepers, then plate of it.
    dict(id="s10_trussway", seed=517214, swaps=6, cogs=20, boss="",
         waves=["rrRR#rrrrrgg", "ggGG#ggggg#gbb", "bbBB#bbbbYYYY#yy"]),

    # Plate and bombs in one wave, so the answer to one is standing in the other.
    dict(id="s10_domewatch", seed=428615, swaps=5, cogs=20, boss="",
         waves=["rgbyRGbyRGbyr", "rgby#r#g!brgbyrgb", "rgby#b#yRGbyrgb"]),

    # **The longest climb in the chapter**: four waves, brutes closing the first and the last.
    dict(id="s10_starglass", seed=432544, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGb", "rgby#rrgbyrg", "rgby#g#b!yrgbyr", "rgbyrgbyRGb"]),

    # Everything the chapter taught at once: brutes, three shields and a bomb in one rung.
    dict(id="s10_nightrail", seed=435435, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrgb", "rgby#r#g#brgbyrgb", "rgby!y#rrgbyrgb"]),

    # **The finale: a gorgon and a hollowking.** A glare says *stop feeding this one*; a wane
    # strikes every ward that has landed nothing since its last cast. Both at once: feed three,
    # starve one, and keep the three working. **Cogs at fifty**, Cloudkeep's finale's argument.
    dict(id="s10_skyclock", seed=466914, swaps=5, cogs=50,
         boss="gorgon:y+hollowking:g",
         waves=["rgbyrgbyrgbyr", "rgby#rrgbyrgbyr", "rgbyrgbyrgbyr"]),
)

#: What every rung here stands, deals and carries: the whole line, all four colours, the first
#: `ORDINAL` charms of the roster (which clamps at six, so all of them) - and the curse.
WARDS = GEMS = "rgby"

#: The seeds every shipped siege chapter already deals, so none is reused here. Read off the
#: bodies rather than typed, so a chapter added later is covered without an edit.
def _shipped_seeds():
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

    # **The curse, on every rung of the chapter that introduces it** - whether is content, how
    # often is the mode (`SiegeTuning.ObsidianPercent`), the charms' bargain.
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
        "accent": "#D9824A",
        "slate": "#141620",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - the waves parse, every duel names two bosses the mode knows, the
    field has a legal swap - and derives par and both star lines. Whether a rung can be **held**
    is `SiegeRuleTests.TheNinthChapterIsFoughtOnABoughtLine`, and whether every duel is a fight
    is `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 9:
        sys.exit("ordinal %d draws map%d, not the clockwork islands" % (ORDINAL,
                                                                          mapart.map_of(ORDINAL)))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    shipped = _shipped_seeds()
    verbs = {}
    total = 0

    for index, level_json in enumerate(written["levels"]):
        rung = LEVELS[index]
        block = level_json["siege"]

        grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
        layout = siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                              block.get("boss"), block.get("cogs", 0),
                              tough=block.get("tough", 0),
                              charms=block.get("charms", ""),
                              obsidian=block.get("obsidian", False))

        if layout.fault:
            sys.exit("%s: %s" % (level_json["id"], layout.fault))

        if not layout.cursed:
            sys.exit("%s: this chapter introduces the curse and this rung does not deal it"
                     % level_json["id"])

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

        # **The crowd is the brief**, so it is held rather than trusted: every authored wave is
        # between `LEAST_WAVE` and `MOST_WAVE` bodies.
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
