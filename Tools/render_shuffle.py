# -*- coding: utf-8 -*-
"""Draws a Shuffle hand - three upgrade cards over a held board - at the size a phone draws it.

    python Tools/render_shuffle.py                       # a common, a rare and an epic
    python Tools/render_shuffle.py --cards phoenix,sure_sight,heavy_bolts
    python Tools/render_shuffle.py --lang de             # the longest names are not English
    python Tools/render_shuffle.py --held heavy_bolts=2  # a card the build already holds
    python Tools/render_shuffle.py --marks chain_arc=s,heavy_bolts=e,second_wind=se
    python Tools/render_shuffle.py --unlock fire_dragon --lang de   # the unlock panel, settled
    python Tools/render_shuffle.py --unlock all --measure           # every bond in every language
    python Tools/render_shuffle.py --out out/hand.png

**Why this exists.** `ShuffleHandLayout.IsClear` proves the four things on a card do not overlap
and the row fits its panel, and says nothing about whether a translated name still reads at the
floor size, whether a note wraps a third line under the tier, or whether a picture painted on
its own ground reads in the kit's seat. Only a picture with the shipped face can, so this
mirrors `ShuffleChoiceOverlay` and prints the size every caption settled at.

**It is a mirror, and mirrors drift.** Every constant is named after the field it copies
(`ShuffleHandLayout.CardWidth`, `SeatDown`, `TierDown`); when a device disagrees with this
picture, the picture is the one that is wrong.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

REPO = K.REPO
ART = REPO / "Assets" / "Game" / "Art"
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc"
LAYOUT = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Layout" / "ShuffleHandLayout.cs"
UNLOCK = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Layout" / "ShuffleUnlockLayout.cs"
BONDS = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleSecrets.cs"
CATALOG = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleCard.cs"
NOTES = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleNotes.cs"
TUNING = REPO / "firebase" / "seed" / "shuffle.json"

W, H = K.W, K.H

#: `ShuffleChoiceOverlay.TintOf`, by tier.
TINTS = {"Common": K.MINT, "Rare": (79, 193, 255), "Epic": (180, 120, 255), "Legendary": K.GOLD}

#: `VictoryFrame`, which the hand wears since 2026-10-09 (the Deals sheet's frame): the crest's
#: reach above the window, the window's ink, the crown, the banner and the word's box.
CREST_REACH = 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0


def figures_of(path):
    """Every `public float/int X = n` on a layout class, read off the C#."""
    text = path.read_text(encoding="utf-8")
    found = {}
    for name, value in re.findall(r"public (?:float|int) (\w+) = ([\d.-]+)f?", text):
        found[name] = float(value)
    for line in re.findall(r"public (?:float|int) ([\w =\d.,f-]+);", text):
        for part in line.split(","):
            if "=" in part:
                name, value = part.split("=")
                found[name.strip()] = float(value.strip().rstrip("f"))
    return found


def layout():
    """The figures, read off the C# so the mirror cannot hold its own copy of them."""
    text = LAYOUT.read_text(encoding="utf-8")
    found = {}
    for name, value in re.findall(r"public (?:float|int) (\w+) = ([\d.]+)f?", text):
        found[name] = float(value)
    for line in re.findall(r"public (?:float|int) ([\w =\d.,f]+);", text):
        for part in line.split(","):
            if "=" in part:
                name, value = part.split("=")
                found[name.strip()] = float(value.strip().rstrip("f"))
    need = ("PanelWidth", "CrestFoot", "NoteY", "NoteHeightLine", "CardWidth", "CardGap", "CardHeight", "CardsTop", "Inset",
            "SeatSize", "IconSize", "SeatDown", "NameSize", "NameFloor", "NameHeight", "NameDown",
            "NoteSize", "NoteFloor", "NoteHeight", "NoteDown", "TierHeight", "TierDown",
            "TierSize", "TierFloor", "Foot", "DiamondDown", "DiamondSize", "DiamondGap")
    missing = [n for n in need if n not in found]
    if missing:
        sys.exit("ShuffleHandLayout.cs no longer carries %s" % ", ".join(missing))
    return found


def catalog():
    rows = re.findall(r'new ShuffleCard\("([a-z_]+)",\s*ShuffleTier\.(\w+)', CATALOG.read_text(encoding="utf-8"))
    return {cid: tier for cid, tier in rows}


def figures():
    """
    Each card's figures as players are dealt them: the published tuning
    (`firebase/seed/shuffle.json`, MODES.md 59l) over the catalog's own, so a retuned per cent is
    measured at the width it prints.
    """
    found = {cid: (int(m), int(e)) for cid, m, e in re.findall(
        r'new ShuffleCard\("([a-z_]+)",\s*ShuffleTier\.\w+,\s*ShuffleEffect\.\w+,\s*(\d+),\s*(\d+)',
        CATALOG.read_text(encoding="utf-8"))}
    if TUNING.exists():
        for row in json.loads(TUNING.read_text(encoding="utf-8")).get("cards", []):
            found[row["id"]] = (int(row["magnitude"]), int(row["extent"]))
    return found


def note_args(cid, figures):
    """`ShuffleNotes.Args`, read off its table: what fills the card's note slots."""
    text = NOTES.read_text(encoding="utf-8")
    row = re.search(r'\{ "%s", new Note\(([^)]*)\) \}' % re.escape(cid), text)
    if not row or cid not in figures:
        return []
    m, e = figures[cid]
    of = {"M": m, "M10": m * 10, "E": e, "ME": m * e}
    return [of[part.strip()] for part in row.group(1).split(",")[1:]]


def table(lang):
    rows = json.loads((LOC / ("%s.json" % lang)).read_text(encoding="utf-8"))["entries"]
    return {r["key"]: r["text"] for r in rows}


def wrapped(sheet, text, cx, top, box_w, box_h, size, floor, fill, outline, anchor_top):
    """`UIKit.Titled` with wrap on and `UIKit.Shrinkable`: shrunk until the lines fit the box."""
    while True:
        font = K.font(size)
        words, lines, line = text.split(), [], ""
        for word in words:
            trial = (line + " " + word).strip()
            if font.getlength(trial) <= box_w or not line:
                line = trial
            else:
                lines.append(line)
                line = word
        lines.append(line)
        pitch = size * 1.15
        if len(lines) * pitch <= box_h or size <= floor:
            break
        size -= 1
    pitch = size * 1.15
    start = top + pitch / 2 if anchor_top else top + box_h / 2 - (len(lines) - 1) * pitch / 2
    for i, each in enumerate(lines):
        K.text(sheet, each, cx, start + i * pitch, size, fill=fill, outline=outline)
    return size, len(lines)


def card(sheet, L, cid, tier, cx, top, strings, held, marks=""):
    tint = TINTS[tier]
    cw, ch = L["CardWidth"], L["CardHeight"]
    K.paste(sheet, K.skin("Hud/card", cw, ch), cx, top + ch / 2)

    seat_y = top + L["SeatDown"]
    K.paste(sheet, K.glow(L["SeatSize"] * 1.9, 2.1, tint, 0.18 if tier == "Common" else 0.34), cx, seat_y)
    K.paste(sheet, K.skin("Hud/slot", L["SeatSize"], L["SeatSize"]), cx, seat_y)

    pic = ART / "Ui" / "Shuffle" / ("%s.png" % cid)
    if pic.exists():
        K.paste(sheet, K.fit(Image.open(pic).convert("RGBA"), (L["IconSize"], L["IconSize"])), cx, seat_y)

    name = strings.get("shuffle.%s.name" % cid, cid)
    size, lines = wrapped(sheet, name, cx, top + L["NameDown"] - L["NameHeight"] / 2,
                          cw - L["Inset"] * 2, L["NameHeight"], int(L["NameSize"]), int(L["NameFloor"]),
                          K.GOLD, 3, False)
    print("  %-16s name %-34r %2d lines at %dpx (floor %d)" % (cid, name, lines, size, L["NameFloor"]))

    note = strings.get("shuffle.%s.note" % cid, "").format(*note_args(cid, figures()))
    size, lines = wrapped(sheet, note, cx, top + L["NoteDown"] - L["NoteHeight"] / 2,
                          cw - L["Inset"] * 2, L["NoteHeight"], int(L["NoteSize"]), int(L["NoteFloor"]),
                          (255, 255, 255), 2, True)
    flag = "  !!" if size <= L["NoteFloor"] else ""
    print("  %-16s note %2d lines at %dpx (floor %d)%s" % ("", lines, size, L["NoteFloor"], flag))

    # `ShuffleChoiceOverlay.Diamonds`: the secret's green, then the evolution's purple, centred.
    shown = [m for m in ("s", "e") if m in marks]
    pitch = L["DiamondSize"] + L["DiamondGap"]
    for i, m in enumerate(shown):
        dx = cx - (len(shown) - 1) * pitch / 2 + i * pitch
        dy = top + L["DiamondDown"]
        hue = (84, 228, 140) if m == "s" else (180, 120, 255)
        K.paste(sheet, K.glow(L["DiamondSize"] * 2.6, 2.1, hue, .6), dx, dy)
        mark = K.load("shuffle_secret" if m == "s" else "shuffle_evolution")[0]
        K.paste(sheet, K.fit(mark, (L["DiamondSize"], L["DiamondSize"])), dx, dy)

    K.paste(sheet, K.skin("Hud/trough", cw - L["Inset"] * 2, L["TierHeight"]), cx, top + L["TierDown"])
    word = strings.get("ui.shuffle.tier.%s" % tier.lower(), tier).upper()
    if held:
        word = strings.get("ui.shuffle.held", "{0} x{1}").format(word, held + 1).upper()
    px = K.shrunk(sheet, word, cx, top + L["TierDown"], cw - L["Inset"] * 2 - 24, L["TierHeight"],
                  int(L["TierSize"]), int(L["TierFloor"]), fill=tint, outline=3)
    print("  %-16s tier %-22r at %dpx (floor %d)" % ("", word, px, L["TierFloor"]))


def screen(cards, lang, held, wave, marks=None):
    marks = marks or {}
    L = layout()
    strings = table(lang)
    tiers = catalog()

    sheet = Image.new("RGBA", (W, H), (14, 20, 40, 255))
    # The held board under the scrim, as a flat band of the siege's ground colour: the panel is
    # what this mirror is about and the board is drawn by `render_siege.py`.
    scrim = Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .78)))
    sheet.alpha_composite(scrim)

    # `VictoryFrame.Build`, as `render_welcome.choice` draws it: the whole block on its own layer,
    # fitted to the canvas as `MakeFit` fits it.
    pw = L["PanelWidth"]
    # `ShuffleHandLayout.PanelHeightFor`: one row for three cards, rows of `PerRow` for a wider
    # hand (`grand_hand` deals five, three over two).
    per = int(L.get("PerRow", 3))
    rows = 1 if len(cards) <= per else (len(cards) + per - 1) // per
    ph = L["CardsTop"] + rows * L["CardHeight"] + (rows - 1) * L.get("RowGap", 0) + L["Foot"]
    reach = ph + CREST_REACH
    fit = min(1.0, (H - 20 * 2) / reach)

    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    bcx = bw / 2
    # `VictoryFrame` hangs the window half the crest *below* the layer's middle (Unity's y is up:
    # `-CrestReach * .5f`), so the crest and the window together are what is centred. This
    # read "- CREST_REACH / 2" (the window lifted instead), which a three-card window had the
    # room to hide and a five-card one does not.
    top = bh / 2 + CREST_REACH / 2 - ph / 2
    # `VictoryFrame.Build`'s `crestY` is the window's top edge; the fan and the bloom hang under it.
    crest_cy = top

    fan = K.rays(1680, 14)
    fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
    K.paste(block, fan_rgba, bcx, crest_cy + 240)
    K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), bcx, crest_cy + 200)

    K.paste(block, K.tint(K.skin("Win/window", pw, ph), PANEL_INK), bcx, top + ph / 2)
    K.paste(block, K.glow(int(pw - 60), 1.9, (255, 245, 209), .11), bcx, top + 70)

    K.paste(block, K.fit(K.load("Win/crown")[0], (180, 162)), bcx, top - CROWN_Y)
    K.paste(block, K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H)), bcx, top - BANNER_Y)
    # The banner carries the wave (one word, as DEALS and REWARD are) and the sentence sits
    # under it, where the Deals sheet prints its free line.
    word = strings.get("ui.shuffle.wave", "Wave {0}").format(wave).upper()
    px = K.shrunk(block, word, bcx, top - BANNER_Y - WORD_LIFT, WORD_W, WORD_H, 58, 32, outline=5)
    print("  word %-24r at %dpx (floor 32)%s" % (word, px, "  !!" if px <= 32 else ""))

    line = strings.get("ui.shuffle.choose", "Choose an upgrade")
    px = K.shrunk(block, line, bcx, top + L["NoteY"], pw - 80, L["NoteHeightLine"], 32, 20,
                  fill=(255, 245, 224), outline=2)
    print("  line %-28r at %dpx (floor 20)%s" % (line, px, "  !!" if px <= 20 else ""))

    pitch = L["CardWidth"] + L["CardGap"]
    for i, cid in enumerate(cards):
        if cid not in tiers:
            sys.exit("'%s' is not a card the catalog names" % cid)
        # `ShuffleHandLayout.Place`: each row centred on its own.
        row = 0 if len(cards) <= per else i // per
        in_row = len(cards) if len(cards) <= per else min(per, len(cards) - row * per)
        at = i if len(cards) <= per else i - row * per
        cx = bcx - (in_row - 1) * pitch / 2 + at * pitch
        card_top = top + L["CardsTop"] + row * (L["CardHeight"] + L.get("RowGap", 0))
        card(block, L, cid, tiers[cid], cx, card_top, strings, held.get(cid, 0), marks.get(cid, ""))

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)
    print("  hand: window %.0fx%.0f (+%.0f crest) fitted at %.2f" % (pw, ph, CREST_REACH, fit))

    return sheet.convert("RGB")


# ---------------------------------------------------------------------- the unlock panel
def bonds():
    """Every secret and evolution `ShuffleBonds.All` names: id -> (kind, tier, parts, needs)."""
    text = BONDS.read_text(encoding="utf-8")
    found = {}
    for bid, rest in re.findall(r'ShuffleBond\.Secret\("([a-z_]+)",\s*SiegePower\.\w+,\s*([^)]*)\)', text):
        parts = re.findall(r'"([a-z_]+)"', rest)
        tier = "Legendary" if len(parts) >= 4 else "Epic" if len(parts) == 3 else "Rare"
        found[bid] = ("secret", tier, parts, 1)
    for bid, tier, cid, needs in re.findall(
            r'ShuffleBond\.Evolution\("([a-z_]+)",\s*ShuffleTier\.(\w+),\s*"([a-z_]+)",\s*(\d+)', text):
        found[bid] = ("evolution", tier, [cid], int(needs))
    if not found:
        sys.exit("no bonds read out of %s - the row shape moved" % BONDS)
    return found


def upper(s, lang):
    """`Loc.Upper`: Turkish dotted i to dotted capital."""
    return s.replace("i", "İ").upper() if lang == "tr" else s.upper()


def unlock(bid, lang, art=True):
    """
    `ShuffleUnlockOverlay` settled: the room, the emblem, and every caption at the size Best
    Fit settles it at. Returns the drawing and the captions that sat at their floor.
    """
    U = figures_of(UNLOCK)
    kind, tier, parts, needs = bonds()[bid]
    strings = table(lang)
    tiers = catalog()
    secret = kind == "secret"
    grand = 3 if tier == "Legendary" else 2 if tier == "Epic" else 1
    hue = (84, 228, 140) if secret else (180, 120, 255)
    tint = TINTS[tier]
    floored = []

    sheet = Image.new("RGBA", (W, H), (14, 20, 40, 255))
    sheet.alpha_composite(Image.new("RGBA", (W, H), (10, 18, 26, int(255 * .9))))

    cx = W / 2

    def y(v):
        return H / 2 - v                                   # Unity's y is up

    ey = y(U["EmblemY"])
    fans = [(1300, 10 + grand * 4, hue, .3), (980, 6 + grand * 2, tint, .22)]
    if grand >= 3:
        fans.append((1600, 18, K.GOLD, .3))
    # `--measure` asks only about the captions, so the room is not drawn.
    for size, count, colour, alpha in fans if art else []:
        fan = K.rays(size, count)
        rgba = Image.new("RGBA", fan.size, colour + (0,))
        rgba.putalpha(fan.point(lambda v, a=alpha: int(v * a)))
        K.paste(sheet, rgba, cx, ey)
    K.paste(sheet, K.glow(900, 1.6, hue, .4 + .08 * grand), cx, ey)
    K.paste(sheet, K.glow(U["EmblemSize"] * 2.4, 2.0, hue, .7), cx, ey)
    K.paste(sheet, K.fit(K.load("shuffle_secret" if secret else "shuffle_evolution")[0],
                         (U["EmblemSize"], U["EmblemSize"])), cx, ey)

    def caption(what, s, cy, box_w, box_h, size, floor, fill, outline):
        px = K.shrunk(sheet, s, cx, cy, box_w, box_h, int(size), int(floor), fill=fill, outline=outline)
        flag = "  !!" if px <= floor else ""
        if flag:
            floored.append("%s %s %s" % (bid, lang, what))
        print("  %-8s %-44r at %dpx (floor %d)%s" % (what, s[:44], px, floor, flag))

    head = strings["ui.shuffle.secret_unlocked" if secret else "ui.shuffle.evolution_unlocked"]
    caption("headline", upper(head, lang), y(U["TitleY"]), U["Wide"], U["TitleH"], U["TitleSize"], U["TitleFloor"],
            tuple(int(c + (255 - c) * .35) for c in hue), 5)
    caption("tier", upper(strings["ui.shuffle.tier.%s" % tier.lower()], lang), y(U["TierY"]), U["Wide"], U["TierH"],
            U["TierSize"], U["TierFloor"], tint, 3)

    stem = "shuffle.%s.%s" % ("secret" if secret else "evolution", bid)
    caption("name", strings[stem + ".name"], y(U["NameY"]), U["Wide"], U["NameH"], U["NameSize"], U["NameFloor"],
            K.GOLD, 4)
    caption("note", strings[stem + ".note"], y(U["NoteY"]), U["NoteWide"], U["NoteH"], U["NoteSize"], U["NoteFloor"],
            K.CREAM, 3)

    # `ShuffleUnlockLayout.RecipeX`: the cards it took, a plus between two for a secret.
    n = len(parts)
    width = n * U["RecipeIcon"] + (n - 1) * U["RecipeGap"]
    for i, cid in enumerate(parts):
        rx = cx - width / 2 + U["RecipeIcon"] / 2 + i * (U["RecipeIcon"] + U["RecipeGap"])
        ry = y(U["RecipeY"])
        K.paste(sheet, K.glow(U["RecipeIcon"] * 1.6, 2.1, TINTS[tiers[cid]], .4), rx, ry)
        K.paste(sheet, K.skin("Hud/slot", U["RecipeIcon"], U["RecipeIcon"]), rx, ry)
        pic = ART / "Ui" / "Shuffle" / ("%s.png" % cid)
        if pic.exists():
            K.paste(sheet, K.fit(Image.open(pic).convert("RGBA"), (U["RecipeIcon"] * .86,) * 2), rx, ry)
        if secret and i + 1 < n:
            K.text(sheet, "+", rx + (U["RecipeIcon"] + U["RecipeGap"]) / 2, ry, 56, outline=3)
        if not secret:
            K.text(sheet, strings["ui.shuffle.times"].format(needs), rx + U["RecipeIcon"] * .62,
                   ry + U["RecipeIcon"] * .38, 44, fill=tint, outline=4)

    by = y(U["ButtonY"])
    K.paste(sheet, K.skin("btn_green", U["ButtonW"], U["ButtonH"]), cx, by)
    caption("key", upper(strings["ui.shuffle.okay"], lang), by - U["ButtonH"] * K.PILL_FACE_LIFT,
            U["ButtonW"] - 80, 70, 50, 28, K.CREAM, 0)

    return sheet.convert("RGB"), floored


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--cards", default="heavy_bolts,chain_arc,second_wind")
    ap.add_argument("--lang", default="en")
    ap.add_argument("--held", default="", help="card=copies, comma separated")
    ap.add_argument("--wave", type=int, default=3)
    ap.add_argument("--marks", default="", help="card=s|e|se: the green and purple diamonds")
    ap.add_argument("--unlock", default="", help="a bond id, or 'all': the unlock panel instead of a hand")
    ap.add_argument("--measure", action="store_true", help="with --unlock all: every language, fail on a floor")
    ap.add_argument("--out", type=Path, default=REPO / "Tools" / "out" / "shuffle_hand.png")
    args = ap.parse_args()

    held = {}
    for part in filter(None, args.held.split(",")):
        cid, n = part.split("=")
        held[cid] = int(n)

    if args.unlock:
        ids = sorted(bonds()) if args.unlock == "all" else [args.unlock]
        langs = sorted(p.stem for p in LOC.glob("*.json")) if args.measure else [args.lang]
        floored = []
        for lang in langs:
            for bid in ids:
                if bid not in bonds():
                    sys.exit("'%s' is not a bond the catalog names" % bid)
                print("%s %s" % (bid, lang))
                out, bad = unlock(bid, lang, art=not args.measure)
                floored += bad
        if args.measure:
            if floored:
                sys.exit("captions at their floor:\n  " + "\n  ".join(floored))
            print("unlock panel: every caption of %d bond(s) in %d language(s) fits" % (len(ids), len(langs)))
            return
        target = args.out if args.out.name != "shuffle_hand.png" else args.out.with_name("shuffle_unlock.png")
        target.parent.mkdir(parents=True, exist_ok=True)
        out.save(target)
        print("  wrote %s  %dx%d  - look at it" % (target, out.width, out.height))
        return

    marks = {}
    for part in filter(None, args.marks.split(",")):
        cid, which = part.split("=")
        marks[cid] = which

    out = screen(args.cards.split(","), args.lang, held, args.wave, marks)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
