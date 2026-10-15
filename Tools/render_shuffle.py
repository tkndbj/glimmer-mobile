# -*- coding: utf-8 -*-
"""Draws a Shuffle hand - three upgrade cards over a held board - at the size a phone draws it.

    python Tools/render_shuffle.py                       # a common, a rare and an epic
    python Tools/render_shuffle.py --cards phoenix,sure_sight,heavy_bolts
    python Tools/render_shuffle.py --lang de             # the longest names are not English
    python Tools/render_shuffle.py --held heavy_bolts=2  # a card the build already holds
    python Tools/render_shuffle.py --marks chain_arc=thunderhead,heavy_bolts=e,kindling=frostfire+fire_dragon+e
    python Tools/render_shuffle.py --unlock fire_dragon --lang de   # the unlock panel, settled
    python Tools/render_shuffle.py --unlock all --measure           # every bond in every language
    python Tools/render_shuffle.py --report s_plus             # a graded run: victory panel + profile card
    python Tools/render_shuffle.py --report all --measure      # every grade in every language
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
                try:
                    found[name.strip()] = float(value.strip().rstrip("f"))
                except ValueError:
                    pass        # a figure written as another constant; the caller names it
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
            "TierSize", "TierFloor", "Foot", "DiamondDown", "DiamondSize", "DiamondGap", "DiamondGlow",
            "MostDiamonds", "RefreshGap", "RefreshWidth", "RefreshHeight")
    missing = [n for n in need if n not in found]
    if missing:
        sys.exit("ShuffleHandLayout.cs no longer carries %s" % ", ".join(missing))
    return found


def glow_of(bid):
    """`ShuffleChoiceOverlay.GlowOf`: a secret's hue at full brightness and .85 of its
    saturation; every evolution the kit's foxglove. Figures read out of `ShuffleSecrets.cs`."""
    import colorsys
    text = BONDS.read_text(encoding="utf-8")
    m = re.search(r'ShuffleBond\.Secret\("%s", SiegePower\.\w+, (\d+), (\d+),' % re.escape(bid), text)
    if not m:
        return (180, 120, 255)
    r, g, b = colorsys.hsv_to_rgb(int(m.group(1)) / 360.0, min(1.0, int(m.group(2)) / 100.0) * .85, 1.0)
    return (int(r * 255), int(g * 255), int(b * 255))


def mark_of(bid):
    """`ShuffleBond.Mark`: one picture per secret, one for every evolution."""
    return "shuffle_secret_" + bid if bonds()[bid][0] == "secret" else "shuffle_evolution"


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


# `ShuffleNotes.Total`, which reads the level off the C# fold itself; this mirror cannot, so it
# copies the arithmetic for the picture only (the C# side is held by ShuffleTuningTests).
TOTALS = {
    "keen_eyes": lambda m, e, n: m * n,
    "stunning_blows": lambda m, e, n: m * n,
    "twin_barrels": lambda m, e, n: m * n,
    "cursed_fire": lambda m, e, n: m * n,
    "repelling_wards": lambda m, e, n: min(m * n, 100),
    "execution": lambda m, e, n: m + (n - 1) * 10,
    "splash_shot": lambda m, e, n: (m + (n - 1) * 2) * 10,
    "frostbite": lambda m, e, n: min(m + (n - 1) * 2, 9) * 10,
    "rally_cry": lambda m, e, n: m * n * 10,
    "spin_up": lambda m, e, n: m * n * e,
}


def note_args(cid, figures, held=0):
    """`ShuffleNotes.Args`, read off its table: what fills the card's note slots."""
    text = NOTES.read_text(encoding="utf-8")
    row = re.search(r'\{ "%s", new Note\(([^)]*)\) \}' % re.escape(cid), text)
    if not row or cid not in figures:
        return []
    m, e = figures[cid]
    of = {"M": m, "M10": m * 10, "E": e, "ME": m * e}
    parts = [part.strip() for part in row.group(1).split(",")[1:]]
    if held > 0 and cid in TOTALS and len(parts) == 1:
        return [TOTALS[cid](m, e, held + 1)]
    return [of[part] for part in parts]


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

    note = strings.get("shuffle.%s.note" % cid, "").format(*note_args(cid, figures(), held))
    size, lines = wrapped(sheet, note, cx, top + L["NoteDown"] - L["NoteHeight"] / 2,
                          cw - L["Inset"] * 2, L["NoteHeight"], int(L["NoteSize"]), int(L["NoteFloor"]),
                          (255, 255, 255), 2, True)
    flag = "  !!" if size <= L["NoteFloor"] else ""
    print("  %-16s note %2d lines at %dpx (floor %d)%s" % ("", lines, size, L["NoteFloor"], flag))

    # `ShuffleChoiceOverlay.Diamonds`: one per secret in its own colour, then the evolution's
    # purple, centred, at most `MostDiamonds`.
    wants = [m for m in marks if m and m != "e"]
    evolve = "e" in marks
    secrets = wants[:int(L["MostDiamonds"]) - (1 if evolve else 0)]
    shown = [(mark_of(b), glow_of(b)) for b in secrets] + ([("shuffle_evolution", (180, 120, 255))] if evolve else [])
    pitch = L["DiamondSize"] + L["DiamondGap"]
    for i, (pic, hue) in enumerate(shown):
        dx = cx - (len(shown) - 1) * pitch / 2 + i * pitch
        dy = top + L["DiamondDown"]
        K.paste(sheet, K.glow(L["DiamondSize"] * L["DiamondGlow"], 2.1, hue, .6), dx, dy)
        K.paste(sheet, K.fit(K.load(pic)[0], (L["DiamondSize"], L["DiamondSize"])), dx, dy)

    K.paste(sheet, K.skin("Hud/trough", cw - L["Inset"] * 2, L["TierHeight"]), cx, top + L["TierDown"])
    word = strings.get("ui.shuffle.tier.%s" % tier.lower(), tier).upper()
    if held:
        word = strings.get("ui.shuffle.held", "{0} x{1}").format(word, held + 1).upper()
    px = K.shrunk(sheet, word, cx, top + L["TierDown"], cw - L["Inset"] * 2 - 24, L["TierHeight"],
                  int(L["TierSize"]), int(L["TierFloor"]), fill=tint, outline=3)
    print("  %-16s tier %-22r at %dpx (floor %d)" % ("", word, px, L["TierFloor"]))


# `UIKit.TextButton`'s figures, as `render_ad_offers.py` names them.
KEY_LIFT, GLYPH_GAP, LABEL_INSET, GLYPH_K = K.PILL_FACE_LIFT, 18.0, 40.0, .34
REFRESH_SIZE, REFRESH_FLOOR = 40, 24


def compact(n):
    """`Compact.Number` near enough to measure: whole below a thousand, then tenths of K and M."""
    if n < 1000:
        return str(n)
    if n < 1_000_000:
        return ("%.1f" % (n // 100 / 10)).rstrip("0").rstrip(".") + "K"
    return ("%.1f" % (n // 100_000 / 10)).rstrip("0").rstrip(".") + "M"


def refresh_price(taken):
    """`ShuffleRefreshTuning.PriceFor` over `firebase/seed/shuffle.json`'s `refresh` figures."""
    tuning = json.loads(TUNING.read_text(encoding="utf-8")).get("refresh") or {}
    price, factor = int(tuning.get("firstGems", 5)), int(tuning.get("factorHundredths", 200))
    if price <= 0:
        sys.exit("shuffle.json's refresh.firstGems is 0: no key is sold (draw with --refreshes -1)")
    for _ in range(taken):
        nxt = price * factor // 100
        if nxt >= 1_000_000_000:
            return 1_000_000_000
        if nxt <= price:
            return price
        price = nxt
    return price


def refresh_key(sheet, cx, cy, L, caption):
    """`ShuffleChoiceOverlay.RefreshKey`: the violet gem pill, the caption with the gem trailing it
    (`iconTrails`), shrunk on one line in the room the glyph leaves. Returns the size it landed on."""
    w, h = L["RefreshWidth"], L["RefreshHeight"]
    K.paste(sheet, K.skin("btn_violet", w, h), cx, cy)
    glyph = h * GLYPH_K
    room = w - LABEL_INSET - glyph - GLYPH_GAP
    got = REFRESH_SIZE
    while got > REFRESH_FLOOR and K.font(got).getlength(caption) > room:
        got -= 1
    wide = min(room, K.font(got).getlength(caption))
    face = cy - h * KEY_LIFT
    left = cx - (wide + GLYPH_GAP + glyph) / 2
    K.text(sheet, caption, left + wide / 2, face, got, outline=3)
    K.paste(sheet, K.fit(K.load("ic_gem")[0], (glyph, glyph)), left + wide + GLYPH_GAP + glyph / 2, face)
    return got


def screen(cards, lang, held, wave, marks=None, refreshes=0):
    """`refreshes` is how many this run has bought (MODES.md 59p); below nought draws no key, as
    when the deck has nothing fresh."""
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
    cards_foot = L["CardsTop"] + rows * L["CardHeight"] + (rows - 1) * L.get("RowGap", 0)
    keyed = refreshes >= 0
    ph = cards_foot + (L["RefreshGap"] + L["RefreshHeight"] if keyed else 0) + L["Foot"]
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
        card(block, L, cid, tiers[cid], cx, card_top, strings, held.get(cid, 0), marks.get(cid, []))

    if keyed:
        # `ShuffleRefreshTuning.PriceFor` off the published file's `refresh` section (MODES.md 59p).
        price = refresh_price(refreshes)
        caption = upper(strings.get("ui.shuffle.refresh", "Refresh {0}").format(compact(price)), lang)
        # `ShuffleHandLayout.RefreshX`/`OddsX`: the key and the odds "i" centred as one group.
        key_y = top + cards_foot + L["RefreshGap"] + L["RefreshHeight"] / 2
        odds, gap = L.get("OddsSize", 96.0), L.get("OddsGap", 20.0)
        key_x = bcx - (gap + odds) / 2
        px = refresh_key(block, key_x, key_y, L, caption)
        K.paste(block, K.skin("sq_orange", odds, odds),                     # Skins.Aside
                key_x + L["RefreshWidth"] / 2 + gap + odds / 2, key_y)
        K.paste(block, K.fit(K.load("ic_info")[0], (odds * .5, odds * .5)), key_x + L["RefreshWidth"] / 2 + gap + odds / 2, key_y)
        print("  key  %-28r at %dpx (floor %d)%s" % (caption, px, REFRESH_FLOOR, "  !!" if px <= REFRESH_FLOOR else ""))

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
    hue = glow_of(bid)
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
    K.paste(sheet, K.fit(K.load(mark_of(bid))[0], (U["EmblemSize"], U["EmblemSize"])), cx, ey)

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



# ---------------------------------------------------------------------- the graded run (59o)
REPORT = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Layout" / "ShuffleReportLayout.cs"
GRADES_CS = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleGrade.cs"

#: `ShuffleGradeLook.Ink` and `.Light`, by grade id, worst first.
GRADE_INK = {
    "d": ((255, 178, 30), 0.0), "c": ((63, 169, 255), 0.0), "b": ((179, 92, 255), 0.0),
    "a": ((255, 90, 174), 0.0), "aa": ((255, 90, 174), 0.0), "s": ((94, 220, 60), .38),
    "s_plus": ((124, 232, 62), .52), "s_plus_plus": ((178, 242, 58), .68),
}
GRADE_IDS = list(GRADE_INK)

#: `ShuffleStats.All`: id, kind, picture (under Art/Ui), in drawing order.
STATS = [("damage", "times", "Shuffle/heavy_bolts"), ("speed", "times", "Shuffle/quick_barrels"),
         ("crit", "percent", "Shuffle/keen_eyes"), ("health", "times", "Shuffle/iron_posts"),
         ("cards", "count", "ic_deck"), ("bonds", "count", "shuffle_evolution")]

#: A deep run's figures per grade, for the picture: points, wave, then the six stats.
SAMPLES = {
    "d": (180, 9, [100, 112, 0, 100, 4, 0]),
    "c": (520, 21, [145, 124, 10, 130, 10, 0]),
    "b": (1460, 44, [260, 236, 20, 160, 22, 1]),
    "a": (3150, 78, [560, 410, 30, 220, 38, 3]),
    "aa": (6050, 118, [1180, 760, 40, 280, 58, 6]),
    "s": (8240, 164, [2480, 1350, 45, 310, 82, 11]),
    "s_plus": (10180, 208, [6120, 1940, 50, 410, 104, 16]),
    "s_plus_plus": (12780, 296, [124000, 2650, 50, 520, 152, 22]),
}

#: `VictoryFrame.PanelWidth`, `WinOverlay.StarsBottom`, `ReportGap`, `Tail`, `ButtonBlock`.
VICTORY_W, STARS_BOTTOM, REPORT_GAP, TAIL, BUTTONS = 900.0, 402.0, 18.0, 40.0, 208.0
#: `PublicProfileScreen.CardWidth` and `BuildShuffleCard`'s rows.
PROFILE_W, PROFILE_TITLE, PROFILE_WAVE, PROFILE_FOOT = 980.0, 84.0, 60.0, 34.0
#: `PublicProfileScreen.ProfileGradeScale`, and its caption and note inks (Pal.Amber, Pal.Mint).
PROFILE_GRADE_SCALE = .70
AMBER, MINT = (0xFF, 0x8A, 0x2B), (0x7B, 0xD8, 0x6A)


def figure(kind, value, strings):
    """`ShuffleStats.Say`: x12.4 under ten, whole above; a chance with its sign; a count."""
    if kind == "times":
        if value >= 1000:
            n = "{:,}".format(value // 100)
        else:
            tenths = value // 10
            n = str(tenths // 10) if tenths % 10 == 0 else "%d.%d" % (tenths // 10, tenths % 10)
        return strings["ui.shuffle.times"].format(n)
    if kind == "percent":
        return strings["ui.shuffle.percent"].format(value)
    return "{:,}".format(value)


ARABIC_FONT = REPO / "Assets" / "Game" / "Fonts" / "GameFontArabic.ttf"


def face(s, size):
    """`GameText`: an Arabic string is shaped and reordered (`arabic_text.visual`, the mirror of
    `ArabicText`) and drawn in `GameFontArabic`, the face's fallback (6d); anything else in GameFont."""
    import arabic_text
    if arabic_text.needs(s):
        from PIL import ImageFont
        return arabic_text.visual(s), ImageFont.truetype(str(ARABIC_FONT), int(size))
    return s, K.font(size)


def left_line(sheet, s, left, cy, room, size, floor, fill, what, floored, outline=3):
    """A one-line `MiddleLeft` caption under `UIKit.Shrinkable` (its box is one line tall)."""
    while size > floor and face(s, size)[1].getlength(face(s, size)[0]) > room:
        size -= 1
    shown, f = face(s, size)
    draw = ImageDraw.Draw(sheet)
    y = cy - size * 0.62
    for dx in range(-outline, outline + 1):
        for dy in range(-outline, outline + 1):
            if dx or dy:
                draw.text((left + dx, y + dy), shown, font=f, fill=(23, 36, 51, 240))
    draw.text((left, y), shown, font=f, fill=fill)
    if size <= floor or f.getlength(shown) > room:
        floored.append("%s: %r at %d (floor %d)" % (what, s, size, floor))
    return size


def report_block(sheet, cx, top, grade, strings, note, note_ink, floored, art=True,
                 emblem_scale=1.0, caption_ink=(255, 242, 219)):
    """`ShuffleReportView.Build` at `top` (pixels, the block's top edge), centred on `cx`;
    `emblem_scale` and `caption_ink` are its optional `emblemScale` and `captionInk`."""
    R = figures_of(REPORT)
    points, wave, values = SAMPLES[grade]
    ink, light = GRADE_INK[grade]

    def at(x, y):
        return cx + x, top - y          # layout y runs down as negative numbers

    # `ShuffleReportView`: the letter and the score column centred as one pair by drawn widths
    # (`ShuffleReportLayout.CentredPair`), `GroupGap` apart; the column as wide as its widest line.
    ew, eh = R["EmblemW"] * emblem_scale, R["EmblemH"] * emblem_scale
    letter = K.fit(K.load("shuffle_grade_" + grade)[0], (int(ew), int(eh)))
    caption = strings["ui.shuffle.score"].upper()
    score = "{:,}".format(points)

    def drawn(s, size, outline):
        shown, f = face(s, size)
        return f.getlength(shown) + 2 * outline

    column = min(R["ColumnW"], max(drawn(caption, int(R["CaptionSize"]), 3), drawn(score, int(R["ScoreSize"]), 4),
                                   drawn(note, int(R["NoteSize"]), 3) if note else 0))
    total = letter.width + R["GroupGap"] + column
    lx = -total / 2 + letter.width / 2
    left = cx - total / 2 + letter.width + R["GroupGap"]

    ex, ey = at(lx, R["EmblemY"])
    if light > 0:
        K.paste(sheet, K.glow(int(ew * 2.2), 2.1, ink, light), ex, ey)
    K.paste(sheet, letter, ex, ey)

    left_line(sheet, caption, left, at(0, R["CaptionY"])[1], R["ColumnW"], int(R["CaptionSize"]),
              int(R["CaptionFloor"]), caption_ink, "score caption", floored)
    left_line(sheet, score, left, at(0, R["ScoreY"])[1], R["ColumnW"], int(R["ScoreSize"]),
              int(R["ScoreFloor"]), K.CREAM, "score", floored, outline=4)
    if note:
        left_line(sheet, note, left, at(0, R["NoteY"])[1], R["ColumnW"], int(R["NoteSize"]),
                  int(R["NoteFloor"]), note_ink, "note", floored)

    n = len(STATS)
    cols = int(R["Columns"])
    rows = (n + cols - 1) // cols
    for i, (sid, kind, pic) in enumerate(STATS):
        row, col = divmod(i, cols)
        in_row = n % cols if row == rows - 1 and n % cols else cols
        span = in_row * R["TileW"] + (in_row - 1) * R["Gap"]
        tx = -span / 2 + R["TileW"] / 2 + col * (R["TileW"] + R["Gap"])
        ty = R["GridTop"] - R["TileH"] / 2 - row * (R["TileH"] + R["Gap"])
        px, py = at(tx, ty)

        K.paste(sheet, K.skin("Rank/kit_row", R["TileW"], R["TileH"]), px, py)
        if art:
            K.paste(sheet, K.fit(K.load(pic)[0], (R["IconSize"], R["IconSize"])), px + R["IconX"], py)
        tl = px + R["TextX"] - R["TextW"] / 2
        left_line(sheet, figure(kind, values[i], strings), tl, py - R["FigureY"], R["TextW"],
                  int(R["FigureSize"]), int(R["FigureFloor"]), K.CREAM, "stat %s figure" % sid, floored)
        left_line(sheet, strings["shuffle.stat." + sid], tl, py - R["LabelY"], R["TextW"],
                  int(R["LabelSize"]), int(R["LabelFloor"]), (255, 242, 219), "stat %s label" % sid,
                  floored, outline=2)

    return -R["GridTop"] + rows * R["TileH"] + (rows - 1) * R["Gap"] + R["Tail"]


def report_height():
    R = figures_of(REPORT)
    rows = (len(STATS) + int(R["Columns"]) - 1) // int(R["Columns"])
    return -R["GridTop"] + rows * R["TileH"] + (rows - 1) * R["Gap"] + R["Tail"]


def report(grade, lang, art=True):
    """The victory window over the run line, and the profile's Shuffle card, side by side."""
    strings = table(lang)
    floored = []
    points, wave, _ = SAMPLES[grade]
    sheet = Image.new("RGBA", (W * 2, H), (14, 20, 40, 255))

    # ------------------------------------------------ the victory panel (`WinOverlay`)
    block_h = report_height()
    y = STARS_BOTTOM + 60.0                                  # the run line, no route on this lane
    report_top = y + REPORT_GAP
    ph = report_top + block_h + TAIL + BUTTONS
    top = (H - ph) / 2 + 60
    cx = W / 2

    K.paste(sheet, K.tint(K.skin("Win/window", VICTORY_W, ph), PANEL_INK), cx, top + ph / 2)
    K.paste(sheet, K.fit(K.load("Win/crown")[0], (180, 162)), cx, top - CROWN_Y)
    K.paste(sheet, K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H)), cx, top - BANNER_Y)
    stars = 3 if wave >= 24 else 2 if wave >= 12 else 1
    for i in range(3):
        star = K.fit(K.load("star_full" if i < stars else "star_empty")[0], (176, 176))
        K.paste(sheet, star, cx + (i - 1) * 186, top + 268)
    youcap = strings.get("ui.win.route_you", "You")
    K.text(sheet, youcap, cx - 360 + 180 - 180, top + y - 60 + 22, 27, fill=(255, 242, 219), anchor="l")
    K.text(sheet, strings["ui.board.row_wave"].format(wave), cx + 360, top + y - 60 + 22, 36, anchor="r")

    is_new = grade in ("s_plus_plus", "c")
    note = strings["ui.shuffle.new_best"].upper() if is_new else \
        strings["ui.shuffle.best"].format(strings["shuffle.grade.s"], "{:,}".format(8240))
    note_ink = K.GOLD if is_new else (255, 242, 219)
    report_block(sheet, cx, top + report_top, grade, strings, note, note_ink, floored, art)

    K.paste(sheet, K.skin("btn_green", 330, 140), cx + 180, top + ph - 120)
    K.paste(sheet, K.skin("btn_blue", 330, 140), cx - 180, top + ph - 120)
    print("  victory: window %.0fx%.0f, report %.0f tall" % (VICTORY_W, ph, block_h))

    # ------------------------------------------------ the profile card (`PublicProfileScreen`)
    ch = PROFILE_TITLE + PROFILE_WAVE + block_h + PROFILE_FOOT
    ccx, ctop = W + W / 2, (H - ch) / 2
    K.paste(sheet, K.skin("Hud/plate_blue", PROFILE_W, ch), ccx, ctop + ch / 2)
    title = strings["track.shuffle.name"].upper()
    left_line(sheet, title, ccx - PROFILE_W / 2 + 40, ctop + 44, PROFILE_W - 80, 30, 30, K.GOLD,
              "profile title", [], outline=3)
    wave_y = ctop + PROFILE_TITLE + PROFILE_WAVE / 2 - 14
    K.paste(sheet, K.glow(150, 2.1, K.SUN, .26), ccx - PROFILE_W / 2 + 104, wave_y)
    K.paste(sheet, K.fit(K.load("ic_shuffle")[0], (76, 76)), ccx - PROFILE_W / 2 + 104, wave_y)
    left_line(sheet, strings["ui.endless.best"].format(wave + 6), ccx - PROFILE_W / 2 + 190, wave_y - 2,
              560, 38, 22, K.CREAM, "profile wave", floored)
    report_block(sheet, ccx, ctop + PROFILE_TITLE + PROFILE_WAVE, grade, strings,
                 strings["ui.shuffle.run_wave"].format(wave), MINT, floored, art,
                 emblem_scale=PROFILE_GRADE_SCALE, caption_ink=AMBER)
    print("  profile: card %.0fx%.0f" % (PROFILE_W, ch))

    return sheet.convert("RGB"), floored


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--cards", default="heavy_bolts,chain_arc,second_wind")
    ap.add_argument("--lang", default="en")
    ap.add_argument("--held", default="", help="card=copies, comma separated")
    ap.add_argument("--wave", type=int, default=3)
    ap.add_argument("--refreshes", type=int, default=0, help="refreshes this run has bought (the key's price); -1 draws no key")
    ap.add_argument("--marks", default="", help="card=secret+secret+e: a diamond per secret id, e for the evolution's")
    ap.add_argument("--unlock", default="", help="a bond id, or 'all': the unlock panel instead of a hand")
    ap.add_argument("--measure", action="store_true", help="with --unlock all or --report all: every language, fail on a floor")
    ap.add_argument("--report", default="", help="a grade id (d .. s_plus_plus), or 'all': the graded run (MODES.md 59o)")
    ap.add_argument("--out", type=Path, default=REPO / "Tools" / "out" / "shuffle_hand.png")
    args = ap.parse_args()

    held = {}
    for part in filter(None, args.held.split(",")):
        cid, n = part.split("=")
        held[cid] = int(n)

    if args.report:
        grades = GRADE_IDS if args.report == "all" else [args.report]
        langs = sorted(p.stem for p in LOC.glob("*.json")) if args.measure else [args.lang]
        floored = []
        for lang in langs:
            for grade in grades:
                if grade not in GRADE_INK:
                    sys.exit("'%s' is not a grade (%s)" % (grade, ", ".join(GRADE_IDS)))
                print("%s %s" % (grade, lang))
                out, bad = report(grade, lang, art=not args.measure)
                floored += ["%s %s %s" % (lang, grade, b) for b in bad]
        if args.measure:
            if floored:
                sys.exit("captions at their floor:\n  " + "\n  ".join(floored))
            print("graded run: every caption of %d grade(s) in %d language(s) fits" % (len(grades), len(langs)))
            return
        target = args.out if args.out.name != "shuffle_hand.png" else args.out.with_name("shuffle_report.png")
        target.parent.mkdir(parents=True, exist_ok=True)
        out.save(target)
        print("  wrote %s  %dx%d  - look at it" % (target, out.width, out.height))
        return

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
        marks[cid] = which.split("+")

    out = screen(args.cards.split(","), args.lang, held, args.wave, marks, args.refreshes)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
