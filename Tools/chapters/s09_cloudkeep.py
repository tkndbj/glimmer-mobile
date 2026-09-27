"""Cloudkeep - the eighth Thornwatch chapter: the sky islands, twenty rungs and four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-09-27, and every clause of it is a rule somewhere.**

  * **Twenty rungs** - the first chapter longer than ten. Nothing in the content pipeline counted
    to ten; what did was the map (`mapart.NODES`, a twenty-seat chain) and two fixtures that
    asserted a chapter's length (`SiegeRuleTests.EveryFifthRungSendsABossAndNoOtherDoes`).
  * **A boss rung every fifth rung, and each one is a duel** - two bosses walking on together,
    exactly as the Infinite lane sends a pair (`SiegeEndless.BossesAt`). The board has played that
    shape since the lane shipped; what a chapter needed was a way to say it, which is one
    character in the boss token (`SiegeLayout.BossJoin`): `"gravemaw:r+harrower:b"`.
  * **Every boss is one the player has already fought**, and that is the brief rather than a
    shortage: four duels, eight verbs, no verb twice, each pair chosen because the two verbs make
    *one* question the player has not been asked before (below). 37br's budget - two new verbs a
    chapter - is a rule about lone bosses; a duel spends combinations, and there are ninety-one.
  * **A crowd, and the surge steps down to pay for it.** 1.4, against Bonereach's 1.5 and the 1.6
    the ladder would have derived (`SiegeTuning.Traded`) - 37ef measured surge and composition
    together as a wall, so this chapter takes its difficulty from the bodies. Waves here hold
    ten to fifteen raiders where Bonereach's hold eight to thirteen, and a rung sends thirty to
    forty-two where Bonereach's send twenty-five to thirty-seven. **It was first written with
    waves of twelve to eighteen and eight brutes a wave, and a spot probe on the workhorse line
    held 7 runs of 40** - walls everywhere, even on the second rung - so the crowd is creepers
    and the brutes are capped at about four a wave. See `SiegeRuleTests.TheEighthChapterIsFought`
    `OnABoughtLine` for the sweep that sets its floors; this file was tuned against a probe of
    three to five rhythms. **Then made a little harder at the owner's word** ("a little
    harder, but not super hard"), measured side by side with Bonereach on the same probe - `ember`,
    rhythms 2.20 to 2.60 in five steps: Bonereach held 20 of 50 runs (40%), this chapter 38 of 100
    (38%), with no rung held at no rhythm but the finale, which is won three times in five - twice
    on the last ward. What bought it: cogs back to Bonereach's 25 (35 on the first and third duels,
    40 on the second, 60 on the finale), a brute or two more a wave from rung six, and a field
    re-dealt for the second duel - the same waves read 0/5 and 4/5 on two different seeds, so a
    field is a lever as strong as a wave. Bosses were tried at 65% and 70% of their health and the
    finale could not be won on any of five fields, so a duel stays at 60%.
  * **Every raider is one the player has already fought** - the cast is the *reunion*
    (`SiegeMode.ReunionOrder`), a second square over the six chapter casts dealt so that no slot
    draws the body the Infinite lane draws in it.

**The four duels, and what each pair asks.**

  * **Rung 5, a gravemaw and a harrower** - the harrower tears a rank off a ward and drops it on
    the hill as a cog; the gravemaw eats every loose thing on the hill when it casts. So the
    harrow's question, *when do I go and get it back*, gets a deadline.
  * **Rung 10, a blightcaller and a gorgon** - a douse is answered by pouring more into that ward
    and a glare by pouring into any ward but that one. Two instructions that can point at the same
    post, so the player has to read which boss marked which ward.
  * **Rung 15, a thunderer and a shackler** - a shackled ward cannot fire and banks what it is fed,
    which is a charge building up; the thunderer drains whichever ward holds the most. The chain
    makes the hoard, and the storm spends it.
  * **Rung 20, a sunlord and a hollowking** - a seal says *fill this one ward now*; a wane strikes
    every ward that has gone quiet. The finale asks for both at once: pour into one and keep all
    four working.

**And there is no move allowance anywhere in it** (invariant 37b).
**Retuned harder again on 2026-09-27**, with Bonereach, when the three-player balance run showed
a four-seat one-star ember line holding 40% of this chapter: chapter cog rate 25 -> 20, the duels
at 25/30/25 and the finale at 50, and brutes back into the rungs that a three-star line won every
time. Measured after it on four seats at nine rhythms: one-star ember 17%, three-star 53%,
five-star 85%, three-star pyre 92% - a step past Bonereach's 24 / 58 / 86 / 98, which is the
"slightly harder than the chapter before" the owner asked for.
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s09_cloudkeep.json"

CHAPTER = "s09_cloudkeep"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies,
#: its grounds, its cast and its charms (invariant 7c). Eight: it draws `map8`, the sky islands,
#: the fourth block of skies (twenty rungs wrap inside it, `mapart.sky`), the reunion cast, all
#: six charms (`SiegeCharms.Upto` clamps) and the surge `SiegeTuning.Traded` names.
ORDINAL = 8

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and
#: is recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled
#: raider leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses
#: joined by `+` - or is empty for a siege that sends none. Fields open at six swaps and tighten
#: to five for the last four rungs, the floor every chapter since Barrowfell reaches.
LEVELS = (
    # ---------------------------------------------------------------- the first island
    # **Ten and twelve a wave from the first rung**, which is what this chapter is: the same
    # bodies the player has met, more of them at once, and each a tenth softer than the last
    # chapter's.
    dict(id="s09_firstcloud", seed=302730, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgby", "RGbyrgbyRG", "RGbyRGby#rrg"]),

    # Twelve and fourteen with nothing armoured in them - the crowd, and nothing else.
    dict(id="s09_windward", seed=308800, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgby", "RGbyRGbyrgby", "RGbyRGbyrgbyrg"]),

    # Two shields a wave in two colours, inside a crowd that will not wait for them.
    dict(id="s09_stepstones", seed=313761, swaps=6, cogs=20, boss="",
         waves=["RGby#rRGby", "rgbyRGby#b#yRG", "RGby#r#gRGbyrg"]),

    # Bombers inside the crowd: a bomb is a plus of five boxes and this is the first hill where
    # five boxes reliably hold five raiders (invariant 40i).
    dict(id="s09_skyferry", seed=317101, swaps=6, cogs=20, boss="",
         waves=["RGbyRGby!rRG", "RGBY#gRGby!b", "RGBY!y#rRGbyrg"]),

    # **The first duel: a gravemaw and a harrower.** A harrow drops a rank on the hill as a cog;
    # a devour eats every loose thing lying there. So the three waves in front of it are the ones
    # that hand the line its ranks, and the fight is whether a beat spent reaching down is spent
    # before the maw opens. Cogs at thirty-five, because both verbs are about cogs.
    dict(id="s09_mawgate", seed=317517, swaps=6, cogs=20, boss="gravemaw:r+harrower:b",
         waves=["RGbyRGbyRG", "RGby#r!gRGby", "RGBY#b#yRGby"]),

    # ---------------------------------------------------------------- the second island
    # **One colour at a time, in a flood**: a dozen of one colour is a dozen bolts one ward has
    # to find, and the other three can only wait for their turn.
    dict(id="s09_tallgrass", seed=329889, swaps=6, cogs=20, boss="",
         waves=["RRRrrrr#rggg", "GGGggggg#gbbb", "BBB#bbbbYYY#yyy"]),

    # Four waves under the tower, the last of them twelve of nothing in particular.
    dict(id="s09_towerwatch", seed=334804, swaps=6, cogs=20, boss="",
         waves=["RGbyRGbyrg", "RGby#r#gRGby", "rgby!rRGBY#bby", "RGbyrgbyrgby"]),

    # Brutes at the head of every wave, and the plate behind them.
    dict(id="s09_hollowpine", seed=340908, swaps=6, cogs=20, boss="",
         waves=["RGBYrgbyrg", "RGby#g#brgby", "RGBY!grgby#r"]),

    # **The rehearsal**: four waves and no boss, so what this rung is about is feeding the line
    # evenly for two minutes - which is the question the finale of this island bills.
    dict(id="s09_longspan", seed=343696, swaps=6, cogs=20, boss="",
         waves=["RGbyrgbyrg", "rgby#rRGbyRG", "RGby#brgbyrgby", "RGBYrgbyrgby"]),

    # **The second duel: a blightcaller and a gorgon.** A douse is answered by pouring into the
    # ward it put out; a glare by pouring into any ward but the one it turned to stone. Both
    # instructions can land on one post, so the fight is reading which boss marked which ward.
    dict(id="s09_glarecrown", seed=485130, swaps=6, cogs=30, boss="blightcaller:g+gorgon:y",
         waves=["RGbyrgbyrg", "RGby#yRGbyrg", "rgby#g#rRGby"]),

    # ---------------------------------------------------------------- the third island
    # The crowd climbs: twelve, fourteen, sixteen.
    dict(id="s09_highmeadow", seed=372888, swaps=6, cogs=20, boss="",
         waves=["RGbyrgbyRGby", "RGBY#r#grgbyRG", "RGby!b#yRGbyrgby"]),

    # Fourteen from the first wave, which no rung before this chapter ever opened on.
    dict(id="s09_farmstead", seed=381269, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrgbyrg", "RGBY#b!rRGbyrg", "RGby#g#yrgbyrg"]),

    # Three shields in every wave - the most plate the mode carries - with a bomb to answer them.
    dict(id="s09_ironrain", seed=393306, swaps=6, cogs=20, boss="",
         waves=["RGby#r#g#brg", "RGby#y#r!gRGby", "RGby#g#b#yRGBYrg"]),

    # Four waves, and the bomb held for the last of them.
    dict(id="s09_cloudbreak", seed=394655, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyrg", "rgby#rRGbyrg", "rgby#g#brgby", "rgby!yrgbyrg"]),

    # **The third duel: a thunderer and a shackler.** A chained ward cannot fire and banks what it
    # is fed - which is a charge building - and a thunderer drains whichever ward holds the most.
    # The chain makes the hoard and the storm spends it, so the answer is to stop feeding the ward
    # that cannot use it, which is the opposite of what a chain alone asks.
    dict(id="s09_stormhold", seed=395867, swaps=6, cogs=25, boss="thunderer:b+shackler:r",
         waves=["RGbyRGbyRGby", "RGby#b!rRGbyRG", "RGBY#r#gRGbyrg"]),

    # ---------------------------------------------------------------- the last island
    # Three brutes of one colour at a time, then plate of it.
    dict(id="s09_thinline", seed=405562, swaps=6, cogs=20, boss="",
         waves=["RRRR#rrrrgg", "GGGG#gggg#gbb", "BBBB#bbbbYYYY#yyy"]),

    # Plate and bombs in one wave, so the answer to one is standing in the other.
    dict(id="s09_lanternrise", seed=330944, swaps=5, cogs=20, boss="",
         waves=["RGbyRGbyRGby", "rgby#r#g!bRGbyrg", "RGby#b#yRGbyrg"]),

    # **The longest climb in the chapter**: four waves, and the last of them brute-led.
    dict(id="s09_whitewater", seed=347017, swaps=5, cogs=20, boss="",
         waves=["RGbyrgbyRG", "rgby#rRGbyrg", "rgby#g#b!yRGby", "RGbyrgbyRGby"]),

    # Everything this chapter taught, at once: brutes, three shields and a bomb in sixteen.
    dict(id="s09_lastferry", seed=362625, swaps=5, cogs=20, boss="",
         waves=["RGbyrgbyRGbyrg", "RGby#r#g#brgbyrg", "RGby!y#rrgbyrg"]),

    # **The finale: a sunlord and a hollowking.** A seal says *fill this one ward before the sun
    # sets on it*; a wane strikes every ward that has landed nothing since its last cast. So the
    # last rung of the chapter asks for both at once - pour into one, and keep all four working -
    # which no boss in this mode has ever asked alone. **Cogs at sixty**, Bonereach's finale's
    # argument: the rung that asks the most of the line hands it the most ranks. Measured on the
    # workhorse at five rhythms: at fifty the duel was reached every time and never won; at sixty
    # it is won three times in five, twice on the last ward standing - a peak, not a wall.
    dict(id="s09_crownofclouds", seed=395888, swaps=5, cogs=50, boss="sunlord:y+hollowking:g",
         waves=["rgbyrgbyrg", "rgby#rrgbyrg", "rgbyrgby"]),
)

#: What every rung here stands, deals and carries: the whole line, all four colours, and the
#: first `ORDINAL` charms of the roster (which clamps at six, so all of them).
WARDS = GEMS = "rgby"


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
        "accent": "#E8615A",
        "slate": "#1A0F14",
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
    is `SiegeRuleTests.TheEighthChapterIsFoughtOnABoughtLine`, and whether every duel is a fight
    is `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    verbs = {}
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

        # **Every fifth rung is a duel, and no other rung sends a boss.**
        want_duel = (index + 1) % BOSS_EVERY == 0
        if want_duel != (len(layout.boss_kinds) == 2) or (not want_duel and layout.boss_kinds):
            sys.exit("%s: rung %d %s" % (level_json["id"], index + 1,
                                         "must be a duel" if want_duel else "sends a boss"))

        # **No verb twice across the four duels** - a pair spends combinations, never a repeat.
        for kind in layout.boss_kinds:
            spell = siege.BOSSES[kind]["spell"]
            if spell in verbs:
                sys.exit("%s sends a %s and so does %s" % (level_json["id"], spell, verbs[spell]))
            verbs[spell] = level_json["id"]

        if layout.raiders > siege.MAX_RAIDERS:
            sys.exit("%s: %d raiders" % (level_json["id"], layout.raiders))

        par = siege.par(layout)
        read = siege.readings(layout)

        made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
        if made != rung["swaps"]:
            sys.exit("%s: seed %d now deals %d opening swaps, not the %d recorded"
                     % (level_json["id"], rung["seed"], made, rung["swaps"]))

        widest = max(len(line) for line in layout.coming)
        tail = ""
        if read["kind"]:
            tail += ", a duel of %s (%s) last" % (read["kind"], read["spell"])

        print("%-20s par %-4d 3* %-4d 2* %-4d %2d raider(s) in %d wave(s), widest %2d, "
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
