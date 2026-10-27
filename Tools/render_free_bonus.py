"""Draws the free bonus panel (`FreeBonusOverlay`, invariant 67) over the hub, for judging by eye.

    python Tools/render_free_bonus.py                    # both tasks offered, neither done
    python Tools/render_free_bonus.py --tasks name       # a linked account: the nickname alone
    python Tools/render_free_bonus.py --states           # the link done (CLAIM), the name claimed
    python Tools/render_free_bonus.py --lang de          # in another language
    python Tools/render_free_bonus.py --measure          # every caption in every language; fails on a floor

A mirror, not a gate for the frame: the panel is `render_deals.py`'s (`DealOverlay`'s), imported rather
than copied, because the owner asked for the same design and two mirrors of one frame drift. What this
adds is the row - `DealCard.PaintGems`, the task's name in the trough, the key - and `--measure`, which
is the gate for those captions (a trough caption is Best Fit, 19n).
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402
import render_deals as D  # noqa: E402

W, H = K.W, K.H
LANGS = ("en", "es", "pt", "fr", "de", "it", "tr", "pl", "ar")

# FreeBonusOverlay.BonusRow
NAME_W, NAME_H, NAME_SIZE, NAME_FLOOR = 480.0, 62.0, 34, 18
KEY_SIZE, KEY_FLOOR = 40, 22
GEMS = 60

KEY_FOR = {
    "todo": ("btn_violet", {"link": "ui.free_bonus.do_link", "name": "ui.free_bonus.do_name"}),
    "done": ("btn_orange", "ui.free_bonus.claim"),
    "paid": ("btn_blue", "ui.free_bonus.claimed"),
}


def table(lang):
    rows = json.loads((K.REPO / "Assets/StreamingAssets/Content/loc" / ("%s.json" % lang))
                      .read_text(encoding="utf-8"))["entries"]
    return {e["key"]: e["text"] for e in rows}


def upper(s, lang):
    """`Loc.Upper`: Turkish dotted i to İ first."""
    return s.replace("i", "İ").upper() if lang == "tr" else s.upper()


def face(s, size):
    """`GameText`: Arabic shaped and reordered in `GameFontArabic` (6d); anything else in GameFont."""
    import arabic_text
    if arabic_text.needs(s):
        from PIL import ImageFont
        return arabic_text.visual(s), ImageFont.truetype(str(K.REPO / "Assets/Game/Fonts/GameFontArabic.ttf"), int(size))
    return s, K.font(size)


def fit(s, room, size, floor):
    """The largest size from `size` to `floor` at which `s` fits `room` on one line, and whether it did."""
    while size > floor and face(s, size)[1].getlength(face(s, size)[0]) > room:
        size -= 1
    shown, f = face(s, size)
    return size, shown, f, f.getlength(shown) <= room and size > floor


def caption(sheet, s, cx, cy, room, size, floor, fill):
    size, shown, f, ok = fit(s, room, size, floor)
    from PIL import ImageDraw
    draw = ImageDraw.Draw(sheet)
    x, y = cx - f.getlength(shown) / 2, cy - size * .62
    for dx in range(-3, 4):
        for dy in range(-3, 4):
            if dx or dy:
                draw.text((x + dx, y + dy), shown, font=f, fill=(23, 36, 51, 240))
    draw.text((x, y), shown, font=f, fill=fill)
    return size, ok


def key_caption(task, state, strings, lang):
    skin, key = KEY_FOR[state]
    return skin, upper(strings[key[task] if isinstance(key, dict) else key], lang)


def row(sheet, cx, cy, task, state, strings, lang):
    """`BonusRow` centred on (cx, cy): the deal row's open card painted by `DealCard.PaintGems`."""
    lx = cx - D.ROW_W / 2
    ax = lx + D.ART_LEFT + D.ART / 2
    K.paste(sheet, K.glow(D.GLOW, 2.4, D.GLOW_INK, D.GLOW_ALPHA), ax, cy)
    K.paste(sheet, K.fit(Image.open(K.UI / "Shop" / "gems_3.png").convert("RGBA"), (D.ART, D.ART)), ax, cy)

    col = lx + D.ROW_W - D.COLUMN_W / 2
    said = f"{GEMS:,}"
    gem, art = 84.0, 162.0
    wide = K.font(D.AMOUNT_SIZE).getlength(said)
    left = col - (gem + D.GLYPH_GAP + wide) / 2
    K.paste(sheet, K.fit(Image.open(K.UI / "Shop" / "gems_1.png").convert("RGBA"), (art, art)), left + gem / 2, cy - D.AMOUNT_Y)
    K.text(sheet, said, left + gem + D.GLYPH_GAP + wide / 2, cy - D.AMOUNT_Y - round(D.AMOUNT_SIZE * .044),
           D.AMOUNT_SIZE, fill=K.BLOOM, outline=5)
    caption(sheet, upper(strings["ui.shop.gems"], lang), col, cy - D.UNIT_Y, D.COLUMN_W, 34, 22, K.CREAM)

    K.paste(sheet, K.skin("Hud/trough", NAME_W, NAME_H), col, cy - D.TIMER_Y)
    name = strings["ui.free_bonus.%s" % task]
    name_size, name_ok = caption(sheet, name, col, cy - D.TIMER_Y, NAME_W - 28, NAME_SIZE, NAME_FLOOR, (255, 255, 255))

    skin, word = key_caption(task, state, strings, lang)
    K.paste(sheet, K.skin(skin, D.BUY_W, D.BUY_H), col, cy - D.BUY_Y)
    key_size, key_ok = caption(sheet, word, col, cy - D.BUY_Y - 2, D.BUY_W - 40, KEY_SIZE, KEY_FLOOR, K.CREAM)
    return [(task + " name", name, name_size, name_ok), (task + " key", word, key_size, key_ok)]


def panel(tasks, lang):
    """`FreeBonusOverlay` on `VictoryFrame`, `DealOverlay`'s frame: drawn by `render_deals.panel` with no
    deal rows, then the task rows laid where the deal rows stand."""
    strings = table(lang)
    D.LOCS["ui.deal.title"] = strings["ui.free_bonus.title"]
    D.LOCS["ui.common.cancel"] = strings["ui.common.cancel"]

    # `render_deals.panel` sizes its window by the rows it is handed; hand it as many placeholders and
    # draw nothing in them, then lay the real rows on the same centres.
    drawn = D.row
    D.row = lambda *a, **k: None
    try:
        sheet = D.panel([(0, 0, 0, "ready")] * len(tasks)).convert("RGBA")
    finally:
        D.row = drawn

    list_h = D.PAD_TOP + len(tasks) * D.CELL_H + D.PAD_BOTTOM
    ph = D.ROWS_TOP + list_h + D.TAIL + D.FOOT_H
    top = H / 2 - D.CREST_REACH / 2 - ph / 2
    faults = []
    for i, (task, state) in enumerate(tasks):
        cy = top + D.ROWS_TOP + D.PAD_TOP + i * D.CELL_H + D.CELL_H / 2
        if i > 0:
            D.rule(sheet, W / 2, cy)
        faults += row(sheet, W / 2, cy, task, state, strings, lang)
    return sheet.convert("RGB"), faults


def measure():
    bad = []
    for lang in LANGS:
        strings = table(lang)
        for task in ("link", "name"):
            name_ok = fit(strings["ui.free_bonus.%s" % task], NAME_W - 28, NAME_SIZE, NAME_FLOOR)
            print("  %s %-4s name %2d pt%s" % (lang, task, name_ok[0], "" if name_ok[3] else "  <-- AT THE FLOOR"))
            if not name_ok[3]:
                bad.append((lang, task, "name"))
            for state in KEY_FOR:
                _, word = key_caption(task, state, strings, lang)
                k = fit(word, D.BUY_W - 40, KEY_SIZE, KEY_FLOOR)
                if not k[3]:
                    bad.append((lang, task, state))
                    print("  %s %-4s key %s: %r at the floor" % (lang, task, state, word))
    return bad


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--tasks", choices=("both", "link", "name"), default="both")
    ap.add_argument("--states", action="store_true", help="the link done (CLAIM), the name already claimed")
    ap.add_argument("--lang", default="en", choices=LANGS)
    ap.add_argument("--measure", action="store_true", help="every caption in every language; exits 1 on a floor")
    ap.add_argument("--out", type=Path, default=Path("free_bonus.png"))
    args = ap.parse_args()

    if args.measure:
        bad = measure()
        print("free bonus panel: %s" % ("every caption fits" if not bad else "%d fault(s)" % len(bad)))
        raise SystemExit(1 if bad else 0)

    tasks = [t for t in ("link", "name") if args.tasks in ("both", t)]
    states = {"link": "done", "name": "paid"} if args.states else {}
    out, faults = panel([(t, states.get(t, "todo")) for t in tasks], args.lang)
    for what, s, size, ok in faults:
        print("  %-10s %2d pt  %r%s" % (what, size, s, "" if ok else "  <-- AT THE FLOOR"))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}")


if __name__ == "__main__":
    main()
