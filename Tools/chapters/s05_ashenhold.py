"""Ashenhold - the fourth Thornwatch chapter, and the first one that cost the mode *code*.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**Invariant 37br priced this chapter before it was commissioned, and the price was two boss
verbs.** Two bosses a chapter against a fixed set of verbs means the mode supports exactly
*verbs / 2* chapters and then needs more - and `SiegeRuleTests.NoBossVerbIsSentByAnyTwoChapters` is
what makes that a rule rather than an intention. Six verbs ran out at three chapters, so this one
brought a **shackler** and an **ironclad**, and what each of them takes is written up in
`SiegeKind`. The alternative was relaxing invariant 37z so a verb could come round again, which is
the same as telling a player who has met a smite that they have not met a smite.

**What else is new is a cast, and the thing that decides a cast is which way it faces.** Every
body this game sends down a hill is square-on - an insect from directly above, a blob and a
skeleton head-on, each mirror-symmetric about its own middle - so it reads as coming at the
player. The rabble is six scavengers in scrap armour out of the one pack here drawn that way: a
manhole cover and a padded helmet are the bulwarks, a sledgehammer and a bearskin the brutes, and
the two slightest creep (`make_siege_art.RABBLE_SET`).

**This chapter shipped two casts before it and the owner withdrew both.** The first was rendered
out of rigged 3D, on a written-down survey claiming there was nothing on the machine to cut from;
four packs are head-on and nobody had opened them, so the survey is a command now
(`make_siege_art.py --survey`). The second was cut from a high-resolution pack drawn in
three-quarter and profile, chosen because the top-down pack upscales 2.5x to 3.4x - and the
verdict was one line: *they look sideway, my other characters look downwards as they walk*.
**Facing outranks sharpness.** A flat cartoon body inside a heavy outline carries an upscale; no
resolution recovers a body facing the wrong way.

**The raiders still swing at the line**, and this pack draws a walk and nothing else - so the
swing is *built*: the body throws itself at the viewer and settles, which from this camera is what
a lunge is.

**And the two bosses are cut from flat packs now too.** A shackler and an ironclad were baked
beside the cast; they are a lashing tongue and a horned helm out of the monster packs
(`make_siege_art.BOSS_SET`), each chosen against the wave it stands in front of.

**Where the difficulty comes from.** Every constant in this mode is shared by all four chapters
(`SiegeTuning`), so a retune aimed at this one would retune the other three - which is why
difficulty is the boards' job. Two levers, and only two:

  * **A toughness surge of two tenths** (`SiegeTuning.ToughnessFor`), against Barrowfell's one and
    the first two chapters' none. Invariant 37bz measured what a tenth is worth and the answer is
    "a great deal": the same ten rungs read 54 of 90 unsurged, 28 at one tenth, and 5 at three.
    One tenth a chapter is the whole of what this lever can take, and it is derived from the
    chapter's ordinal rather than typed.
  * **Composition.** Armour from the first rung rather than the second, three shields in a wave
    where Barrowfell's worst was two, and bombers standing in the middle of it. Fields tighten from
    seven opening swaps to five, which is the same floor Barrowfell reaches and is close to it: a
    field under five reshuffles (`SiegeBoard.Settle`), and a shuffle is the mode taking a decision
    away.

**This chapter's star lines are its own**, for the reason in `siege.STAR_FACTORS`: a siege's par is
arithmetic rather than a search and it *overstates*, by a different amount on every chapter,
because a surge raises par in proportion while bombs, cogs and overcharges deliver a flat amount
that does not scale. They are set from this chapter's own sweep and no other chapter moves.

**And there is no move allowance anywhere in it** (invariant 37b): a siege is lost when the last
ward falls, and both gates error on one that authors a budget.
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s05_ashenhold.json"

CHAPTER = "s05_ashenhold"

#: This chapter's place inside its own mode and track, which is what decides its map, its skies, its
#: grounds and its cast (invariant 7c, and `SiegeMode.CastFor`). Four, because it is the fourth
#: Thornwatch chapter on the main ladder - so it draws `map4`, the fourth block of forty skies and
#: the iron cast, and every one of those is arithmetic rather than a decision.
#:
#: The id says `s05` because `s02` is the Infinite lane, which is a *track* rather than a place in
#: this ladder. An id is permanent and arbitrary; the ordinal is derived, and it is the ordinal that
#: everything reads.
ORDINAL = 4

#: Every field in this chapter, in cells - the same eight by five all three chapters before it use.
#: Unchanged on purpose: the field is the half of the screen the player already knows, and making it
#: bigger would be a second variable in an experiment about the hill.
WIDE, TALL = 8, 5

#: Every rung, in order.
#:
#: ``seed`` deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and is
#: recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled raider
#: leaves one on the hill, **per hundred kills**, and ``boss`` names one of the eight bosses and the
#: colour it wears - or is empty for a siege that sends none.
LEVELS = (
    # **The warband arrives already armoured, which is the plainest statement this chapter makes
    # about itself.** Barrowfell opened with no armour at all and brought it on the second rung;
    # these are people who came equipped, and the first rung says so in the one way a first rung
    # may - with the cast and with a single shield in the last wave, rather than with a new rule.
    dict(id="s05_firstiron", seed=76184, swaps=7, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBYRGby", "RGBY#rRGbyrg"]),

    # Two shields a wave from the second rung, which is where Barrowfell's *worst* rung sat.
    dict(id="s05_shieldline", seed=76189, swaps=7, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g#bRGby", "RGbyRGbyrg"]),

    # **Three shields in one wave, which is the most armour the mode can carry without a new
    # rule.** A bulwark halves every bolt that is not its own colour, so three of them in three
    # colours is a wave asking the player to feed three specific wards in order while everything
    # else walks - and taking the biggest match on the field is exactly the wrong answer to it.
    dict(id="s05_pikewall", seed=76422, swaps=7, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGby#g#b#yRGby", "RGByRGbyrg"]),

    # Bombers standing inside the armour, which is the rung that teaches holding a bomb for the
    # wave rather than tapping it where it fell (invariant 40i: the decision is *when*). A bomb
    # takes a plus of five boxes, and a bulwark standing in it is the one raider a colour match is
    # slowest against.
    dict(id="s05_emberrow", seed=88211, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY#g!gRG#bby", "RGby!bRGByby"]),

    # **The shackler, riding the head of its last authored wave.** It takes no ward health at all
    # (`SiegeTuning.EndangersTheLine`), so a wave of its own would be a boss loosing arrows over an
    # empty hill - invariant 37ad, and a real report from a device about the blightcaller. What a
    # chain costs is entirely what the chained ward was about to do, so the wave it rides has to be
    # one the line is genuinely answering: the validator refuses this rung if fewer than four
    # raiders come beside it.
    dict(id="s05_chainfall", seed=92781, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="shackler:g",
         waves=["RGBYRGBy", "RGBY#rRG!bby", "RGby#rRGBy"]),

    # The densest hill in the game: four waves, brutes in every colour, two shields. Nothing to
    # work out and everything to keep up with - a texture a chapter needs one of, and this is
    # Ashenhold's.
    dict(id="s05_ironyard", seed=94130, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBY#rRGby", "RGby#gRGby", "RGBYRGbyrg"]),

    # **One colour at a time with armour in it.** A bolt is worth double against its own colour, so
    # a wave that is all of one thing is a wave three of the four wards can barely help with - and
    # the shield in each makes the one ward that *can* help the only ward that can.
    dict(id="s05_hollowvigil", seed=95067, swaps=6, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#bbbYYY#yy"]),

    # Armour and bombers together on a tight field, which is the rung where a charge held back is
    # worth a whole wave.
    dict(id="s05_sunderway", seed=98747, swaps=5, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGby#rRGby", "RGBY!gRG#bby", "RGBY#bRGByby"]),

    # **The longest hill in the game**, four waves and no boss at the end of it, so what this rung
    # is about is attrition rather than a fight: the line has to be fed evenly for the better part
    # of two minutes, and a colour left dark for one wave too long is the colour that takes a ward
    # down.
    dict(id="s05_longmarch", seed=109462, swaps=5, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="",
         waves=["RGbyRGby", "RGBY#rRGby", "RGby#gRGby#bby", "RGBYRGby"]),

    # **The ironclad, and it is the first duel in this mode the whole line cannot fight.** Invariant
    # 37bq made every ward answer a boss whatever colour it wears, because a duel is one raider and
    # a lock that left three turrets idle would fight the biggest number in the mode with a quarter
    # of the loadout. This puts that back on purpose: only the ward wearing its colour will fire at
    # it, and the other three bank what they are given and throw it as overcharges. So the finale
    # asks the one question the mode has never asked - *have you been banking* - and the answer is
    # a mechanic that has never before been the answer to anything.
    #
    # **It wears blue and the line stands blue**, which the validator enforces as an error rather
    # than a warning: an ironclad no ward on the line carries could only be reached by overcharges,
    # which is a fight decided by whether the player happened to have banked rather than by what
    # they do about it.
    dict(id="s05_ashenheart", seed=150049, swaps=5, charms="pls",
         wards="rgby", gems="rgby", cogs=25, boss="ironclad:b",
         waves=["RGbyRGby", "RGBY#rRGby", "RGBY#gRG#bby"]),
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

        # The same rose and warm dark plate all three chapters before it wear. A mode's look belongs
        # to the mode rather than to the chapter (invariant 7c), so a fourth chapter that re-picked
        # them would be the same game claiming to be four.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        # The end-of-chapter marker stands on the painting like every glade does, and its
        # x is the only axis a body can author (`ChapterMap.TeaserPosition` derives its
        # height). Generated with the seats: `Tools/make_map_seats.py`.
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - that the waves parse, that the boss token is one of the eight, that
    the field has a legal swap on it - and derives par and both star lines. What it cannot prove is
    that a rung can be **held**, because a siege has nothing to search: that is
    `SiegeRuleTests.TheFourthChapterIsFoughtOnABoughtLine`, which plays all ten with the real rules
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
        # with nothing anywhere saying so. Capped at the roster's length, which is what makes a
        # fourth chapter deal the same three a third one does.
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
