"""Windwreck - the tenth Thornwatch chapter: the pirate sky islands, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-09-29, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 111 to 130**, on the owner's own painting - wrecked ships and a
    lighthouse on floating rocks, joined by stone slabs and plank bridges - with every node
    standing **on the road** (`make_map_seats.TRACES[10]`, walked up the road the painting draws).
  * **A tenth more health than Cogspire**: raiders carry six tenths over the baseline against
    Cogspire's five (`SiegeTuning.Traded`, row `(9, 16)`; the ladder would derive 1.8).
  * **More raiders, more brutes, more health on the hill** - and none of the three is trusted:
    `prove` reads Cogspire's own body through the same mirror and refuses this chapter unless it
    sends more of all three (raiders, brutes, and par, which is the hill's health in matches).
  * **"Genuinely players should need a good set of turrets to win or to get three stars."** The
    one-star workhorse line is meant to struggle here and a bought line is meant to win it, which
    is measured rather than argued: `SiegeRuleTests.TheTenthChapterAsksForAGoodLine` holds the
    workhorse under Cogspire's share and a three-star line over a floor.
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *armada* (`SiegeMode.ArmadaOrder`), a fourth square over the six chapter casts that shares no
    slot with the medley, the reunion or the gathering, with every body that swings dealt onto the
    brutes and bulwarks this chapter sends more of. The four duels are four pairs nobody has
    fought, of eight verbs, none twice in the chapter (37ey's restated rule).
  * **The curse carries on.** Every rung deals the obsidian, as every rung of Cogspire does: a
    mechanic a chapter teaches is a tool the next chapter keeps, exactly as the charms accumulate
    (`SiegeCharms.Upto`), and a harder hill is exactly where a hex is worth breaking.

**The four duels, and what each pair asks.**

  * **Rung 5, a warbringer and a gravemaw** - a roar sends the whole hill charging while a
    devour eats what the line has banked: the crowd arrives early and the answer is gone.
  * **Rung 10, a bonecaller and a shackler** - a raise brings a fresh crowd of creepers while a
    chain holds a ward silent; the curse broken over the raised crowd is the way through.
  * **Rung 15, a thunderer and a harrower** - a storm drains the charges and a harrow tears a rank
    off a ward and drops it on the hill as a cog to be won back.
  * **Rung 20, a sunlord and an ironclad** - a seal takes a ward off the line unless it is filled,
    and plate halves every bolt: the two heaviest bosses in the mode, together, over the biggest
    crowd it has sent. **Cogs at fifty**, the finale's argument since Cloudkeep.

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s11_windwreck.json"

CHAPTER = "s11_windwreck"

#: The chapter this one is commissioned against: harder than it in health, headcount and brutes.
BEFORE = OUT.parent / "s10_cogspire.json"

#: This chapter's place inside its own mode and track, which decides its map (`map10`, the owner's
#: pirate sky islands), its skies (the second block again - forty skies wrap at four chapters), its
#: cast (the armada), its charms (all six; `SiegeCharms.Upto` clamps) and its surge (the third row
#: of `SiegeTuning.Traded`).
ORDINAL = 10

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: The fewest and most raiders a wave of this chapter sends, **bosses aside**. The floor is one
#: over Cogspire's eleven, and it is proved below rather than trusted.
LEAST_WAVE, MOST_WAVE = 12, 17

#: The fewest brutes a rung that is not a duel sends. Cogspire's rungs sent none to eight; the
#: brief is *more brutes*, so no ordinary rung here is a crowd of creepers alone.
LEAST_BRUTES = 6

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses
#: joined by `+` - or is empty for a siege that sends none. Fields open at six swaps and tighten
#: to five for the last four rungs, Cogspire's shape. **No seed is shared with any shipped
#: chapter**, so no field here is one a player has already seen.
LEVELS = (
    # ---------------------------------------------------------------- the lower rocks
    # **The drift pier.** Thirteen from the first wave, a brute in every colour by the third - the
    # opening rung is already a step past where Cogspire opened.
    dict(id="s11_driftpier", seed=622279, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyr", "rgbyRGbyrgBYr", "rgbyRGBY#r#grgb"]),

    # Barrels on the right-hand rock: brutes at the back of every wave.
    dict(id="s11_barrelrow", seed=631498, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbRG", "rgbyrgby#rrgBY", "rgbyrgbyRGbyRGby"]),

    # The long bridge: two shields a wave behind the brutes.
    dict(id="s11_longplank", seed=635826, swaps=6, cogs=20, boss="",
         waves=["rgbyRG#rbyrgby", "rgbyRGby#b#yrgb", "rgBY#r#gRGbyrgby"]),

    # Bombers inside a crowd of brutes - a plus of five boxes on a hill this full is five raiders.
    dict(id="s11_chestrock", seed=637198, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!rRGby", "rgBY#gRGby!brgb", "rgBY!y#rRGbyRGby"]),

    # **The first duel: a warbringer and a gravemaw.** A roar sends the whole hill charging and a
    # devour eats what the line has banked - so the crowd lands early and the answer is gone.
    dict(id="s11_brokenkeel", seed=642853, swaps=6, cogs=25,
         boss="warbringer:y+gravemaw:r",
         waves=["rgbyRGbyrgby", "rgby#r!gRGbyRG", "rgBY#b#yRGbyrg"]),

    # ---------------------------------------------------------------- the lantern rocks
    # **One colour at a time, in a flood**, its brutes in the middle of it.
    dict(id="s11_lanternpost", seed=644476, swaps=6, cogs=20, boss="",
         waves=["rrRRrrrrr#rggg", "ggGGgggggg#gbbb", "bbBB#bbbbbYYY#yyy"]),

    # Four waves off the pier, the third of them armoured and bombed at once.
    dict(id="s11_saltdock", seed=646438, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgby", "rgby#r#gRGbyRG", "rgby!rrgBY#bbyr", "rgbyRGbyrgby"]),

    # The ladder: plate and a bomb behind a creeper crowd, brutes at the back of every wave.
    dict(id="s11_rigladder", seed=652798, swaps=6, cogs=20, boss="",
         waves=["rgBYrgbyrgbyRG", "rgby#g#brgbyRGby", "rgBY!grgby#rrgBY"]),

    # **The rehearsal**: four waves and no boss, a brute pair in every one.
    dict(id="s11_cannonreef", seed=654521, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGby", "rgby#rrgbyRGb", "rgby#brgBYrgby", "rgBYrgbyrgbyr"]),

    # **The second duel: a bonecaller and a shackler.** A raise brings a fresh crowd of creepers
    # while a chain holds a ward silent - a curse broken over the raised crowd is the way through.
    dict(id="s11_ropeway", seed=658753, swaps=6, cogs=30,
         boss="bonecaller:g+shackler:b",
         waves=["rgbyRGbyrgby", "rgby#yRGbyrgbRG", "rgby#g#rRGbyrg"]),

    # ---------------------------------------------------------------- the wreck rocks
    # The crowd climbs: thirteen, sixteen, seventeen.
    dict(id="s11_bowsprit", seed=660380, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyr", "rgBY#r#grgbyRGby", "rgby!b#yRGbyrgBYr"]),

    # Sixteen from the first wave, which nothing in this mode has opened on before.
    dict(id="s11_tattersail", seed=662012, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrgby", "rgBY#b!rRGbyrgb", "rgby#g#yrgbyRGBY"]),

    # Three shields in every wave, brutes behind them, and a bomb to answer them.
    dict(id="s11_bilgewater", seed=662346, swaps=6, cogs=20, boss="",
         waves=["rgby#r#g#bRGbyr", "rgby#y#r!gRGbyrg", "rgby#g#b#yRGBYrg"]),

    # Four waves under the wheel, the bomb held for the last of them.
    dict(id="s11_helmsway", seed=662675, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgby", "rgby#rrgbyRGb", "rgby#g#bRGbyrg", "rgby!yRGbyrgb"]),

    # **The third duel: a thunderer and a harrower.** A storm drains the charges a line has
    # built, and a harrow tears a rank off a ward and drops it on the hill as a cog to win back.
    dict(id="s11_lighthouse", seed=669273, swaps=6, cogs=25,
         boss="thunderer:b+harrower:r",
         waves=["rgbyRGbyrgby", "rgby#b!rRGbyRG", "rgBY#r#gRGbyrg"]),

    # ---------------------------------------------------------------- the high wrecks
    # Brutes of one colour at a time in the middle of its creepers, then plate of it.
    dict(id="s11_mastbridge", seed=674230, swaps=6, cogs=20, boss="",
         waves=["rrRRR#rrrrrgg", "ggGGG#ggggg#gbb", "bbBBB#bbbbYYYY#yy"]),

    # Plate and bombs in one wave, so the answer to one is standing in the other.
    dict(id="s11_goldhold", seed=616626, swaps=5, cogs=20, boss="",
         waves=["rgbyRGbyRGbyrg", "rgby#r#g!bRGbyrgb", "rgby#b#yRGbyRGby"]),

    # **The longest climb in the chapter**: four waves, brutes closing every one.
    dict(id="s11_swayline", seed=620082, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGby", "rgby#rrgbyRGb", "rgby#g#b!yrgbyRG", "rgbyrgbyRGBY"]),

    # Everything the chapter taught at once: brutes, three shields and a bomb in one rung.
    dict(id="s11_wreckdeck", seed=654109, swaps=5, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGby", "rgby#r#g#bRGbyrgb", "rgby!y#rRGbyrgBY"]),

    # **The finale: a sunlord and an ironclad.** A seal takes a ward off the line unless it is
    # filled, and plate halves every bolt: the two heaviest bosses in the mode, together, over the
    # biggest crowd it has sent. **Cogs at fifty**, Cloudkeep's finale's argument.
    dict(id="s11_captainsdeck", seed=669762, swaps=5, cogs=50,
         boss="sunlord:r+ironclad:b",
         waves=["rgbyrgbyRGbyr", "rgby#rrgbyRGbyr", "rgbyRGbyrgbyr"]),
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
        "accent": "#E8A33D",
        "slate": "#1A1F2B",
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
    field has a legal swap - derives par and both star lines, and holds the brief: more raiders,
    more brutes and more hill than Cogspire. Whether a rung can be **held** is
    `SiegeRuleTests.TheTenthChapterAsksForAGoodLine`, and whether every duel is a fight is
    `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 10:
        sys.exit("ordinal %d draws map%d, not the pirate sky islands" % (ORDINAL,
                                                                          mapart.map_of(ORDINAL)))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    if siege.toughness_for(ORDINAL - 1) != siege.toughness_for(ORDINAL - 2) + 1:
        sys.exit("the brief is one tenth of health over Cogspire: %d against %d"
                 % (siege.toughness_for(ORDINAL - 1), siege.toughness_for(ORDINAL - 2)))

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

        # **The crowd is the brief**, so it is held rather than trusted.
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

    # **The brief, against the chapter it was commissioned against**, read through one mirror.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "hill (par)"), ours, theirs):
        print("%-11s %5d against Cogspire's %5d (%+.0f%%)"
              % (name, mine, before, (mine - before) * 100.0 / before))
        if mine <= before:
            sys.exit("the brief is more %s than Cogspire, and this chapter sends %d against %d"
                     % (name, mine, before))

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
