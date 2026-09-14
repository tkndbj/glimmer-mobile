# -*- coding: utf-8 -*-
"""Cuts the game's one font out of the variable Fredoka, and proves it can draw this game.

    python Tools/make_game_font.py --source <Fredoka[wdth,wght].ttf>   # write it
    python Tools/make_game_font.py --check                             # prove it reproduces
    python Tools/make_game_font.py --coverage                          # what it can draw
    python Tools/make_game_font.py --proof <png>                       # look at the accents

One output, `Assets/Game/Fonts/GameFont.ttf`, which is `Fonts/GameFont` in the manifest and
the only font this game has: `Art.Font` is a single accessor and nothing else anywhere reads
a typeface. **Whatever replaces it keeps that address** — `Fonts/GameFont` is a *role*, the
same bargain as `btn_green` (invariant 44), which is why a face swap is one file on disk and
no call sites at all.

**Why this file exists.** What shipped here until 2026-09-14 was **Segoe UI Black**,
Microsoft's, copied out of `C:\\Windows\\Fonts` and renamed — with `includeFontData: 1` in its
`.meta`, so a copy of it was inside every APK and IPA against a licence saying in the font's
own `name` table that any use outside a Microsoft product *"is prohibited"*. A redistribution
problem rather than an attribution one, and **no gate here could ever have seen it**: the
filename hid the identity and nothing in this repo opens a font (32b, on a file type nobody
thought of).

**Why Fredoka.** It is the register this game is aimed at — the heavy rounded sans of Royal
Match and Toy Blast (`CRAFT.md`). Nunito Black was tried first and rejected on sight for
reading as a *UI* font rather than a game one; its cap height was the closer match on paper
(0.705 em against Segoe's 0.700) and that turned out to decide nothing. Fredoka's cap height
is **0.700 em exactly**, so the swap moved no layout either.

**Why it is instanced rather than downloaded.** google/fonts ships Fredoka only as a variable
font. Unity's legacy `Font` renders a variable TTF at its **default instance**, so dropping
`Fredoka[wdth,wght].ttf` in as-is would quietly have set the whole game in Light — a change
nothing would fail on and everything would look wrong after. The axes are pinned here.

**Why it builds 243 glyphs.** Fredoka ships 320 code points against Segoe's 2,192, and what
falls in that gap is not decoration: it is **ş ğ İ ć ę ł ń ś ź ż č ď ě ř ť ů ő ű** and their
capitals — Turkish, Polish, Czech, Hungarian, Baltic. A keeper name reaches a public board
(19), and a glyph Unity cannot find is drawn as **nothing at all**: no box, no question mark,
no log line. Each is built out of Fredoka's *own* base letter and *own* combining mark, so
they are in the face's hand rather than a second typeface bolted alongside.

**Three things that build got wrong before the proof sheet caught them**, and all three are
the same lesson — *read what the designer did, do not reason about what the glyph is called*:

- **Placement is learned, in x and in y.** "Centre the mark on the base" reproduces this
  font's own offset for most marks and is wrong by ~38 units for acute on lowercase, because
  the face leans its acute. Worse, the first cut learned x only and left **y at nought** — so
  every accented *capital* had its mark buried inside the letter and `ŚWIĘTO`, `ČESKÝ` and
  `ŁÓDŹ` drew bare. This font raises an above-mark by ~190–214 units on a capital and by
  nothing on a lowercase, consistently, and both numbers are read off its own composites.
- **Not the `.case` marks**, though the font ships them and the name invites it:
  `uni0307.case` is `uni0307` moved *down* 97 units, which on a capital `I` (top 691, against
  the dot's 644) buried the Turkish dotted capital in its own stem. Fredoka's own capital
  composites all use the plain mark.
- **Enumerate, never list by hand.** The first cut named eighteen characters picked off a
  coverage test that happened to exercise lowercase, so every capital of the same set was
  missing and nothing said so.

**What it still cannot draw**, stated rather than discovered later: Vietnamese (stacks two
marks), Romanian `ș`/`ț` (needs a comma-below this font has no mark for), Greek and Cyrillic.
Segoe had Greek and Cyrillic; neither font has Arabic, Hebrew or CJK, so those names already
drew blank before any of this. If they ever matter the repair is `fallbackFontReferences` in
the `.meta`, not another face.

**OFL compliance needs no extra file.** Condition 2 wants the copyright notice and the licence
to travel with each copy and accepts *"machine-readable metadata fields within text or binary
files"*; the cut font keeps nameID 0 and nameID 13, which are exactly those.
`Assets/Game/Fonts/OFL.txt` is committed beside it for the humans. Fredoka declares **no
Reserved Font Name**, which is what makes it legal for a modified copy to still say Fredoka.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
import unicodedata
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Fonts" / "GameFont.ttf"
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc" / "en.json"

#: The instance to pin. Bold rather than the 700-max because this is the whole UI's voice and
#: the face it replaces was a Black; normal width because the screens were laid out to it.
AXES = {"wght": 700, "wdth": 100}

#: The name records Unity and the OS read. Set explicitly rather than left to
#: `updateFontNames`, which leaves the subfamily reading "Regular" at weight 700 — a font
#: somebody re-picks by accident a year later.
NAMES = {1: "Fredoka Bold", 2: "Regular", 4: "Fredoka Bold",
         6: "Fredoka-Bold", 16: "Fredoka", 17: "Bold"}

#: Where an accented letter can come from. Vietnamese is included knowing most of it will not
#: build — what cannot be built is counted and reported rather than skipped in silence.
RANGES = [(0x00C0, 0x017F), (0x0180, 0x024F), (0x1E00, 0x1EFF)]

#: A combining mark is spelled `uniXXXX` in this font except for three with historic names.
MARKNAME = {0x0300: "gravecomb", 0x0301: "acutecomb", 0x0303: "tildecomb"}

#: Characters the UI assembles at runtime and that therefore appear in no string file.
RUNTIME = "0123456789+-x/%.,:;!?'\"()[]{}<>#@&*_=|\\~`^$ \u00b7\u2013\u2014\u2026\u00d7"

#: Names worth drawing before believing an accent is placed right (`--proof`).
PROOF = ["\u00C7a\u011fr\u0131 \u015eebnem G\u00f6k\u00e7e \u0130lkay",
         "\u0141\u00f3d\u017a Gda\u0144sk \u015awi\u0119to \u0106ma",
         "Dvo\u0159\u00e1k \u010cesk\u00fd \u0164\u016f\u0148 \u010e\u011a\u0158",
         "\u00c1RV\u00cdZT\u0170R\u0150 Bj\u00f8rn \u00c5sa \u00d1o\u00f1o",
         "\u00c7A\u011eRI \u0130LKAY \u015eEBNEM \u0141\u00d3D\u0179",
         "1,240 gems   $0.99   5/5"]


def _tt():
    try:
        from fontTools.ttLib import TTFont
        from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent
        from fontTools.varLib.instancer import instantiateVariableFont
    except ImportError:
        sys.exit("fontTools is needed to cut the font: pip install --user fonttools brotli")
    return TTFont, Glyph, GlyphComponent, instantiateVariableFont


def build(source: Path) -> bytes:
    TTFont, Glyph, GlyphComponent, instance = _tt()

    # `recalcTimestamp=False` is load-bearing rather than tidiness: fontTools stamps
    # `head.modified` with the wall clock when it compiles, so without it two runs a second
    # apart write two different files and `--check` answers MISMATCH on a font nobody has
    # touched. Setting the field afterwards does not help — the stamp is applied during save.
    f = TTFont(str(source), recalcTimestamp=False)
    if "fvar" not in f:
        sys.exit("%s is not a variable font — this tool instances one" % source.name)

    instance(f, AXES, inplace=True, updateFontNames=True)
    for nid, val in NAMES.items():
        f["name"].setName(val, nid, 3, 1, 0x409)
        f["name"].setName(val, nid, 1, 0, 0)
    f["OS/2"].usWeightClass = AXES["wght"]

    glyf, hmtx = f["glyf"], f["hmtx"]
    cmap_all = {}
    for t in f["cmap"].tables:
        cmap_all.update(t.cmap)

    def box(name):
        g = glyf[name]
        if g.numberOfContours == 0:
            return None
        xs = [p[0] for p in g.getCoordinates(glyf)[0]]
        return min(xs), max(xs)

    def centre(base, mark):
        b, m = box(base), box(mark)
        return round((b[0] + b[1]) / 2 - (m[0] + m[1]) / 2)

    # ---- learn how this designer places a mark, in both axes, per (mark, case)
    corr = {}
    for gname in cmap_all.values():
        g = glyf[gname]
        if not g.isComposite() or len(g.components) != 2:
            continue
        base, mark = g.components[0].glyphName, g.components[1].glyphName
        if box(base) is None or box(mark) is None:
            continue
        corr.setdefault((mark, base[:1].isupper()), []).append(
            (g.components[1].x - centre(base, mark), g.components[1].y))

    learned = {k: (round(sum(x for x, _ in v) / len(v)),
                   round(sum(y for _, y in v) / len(v))) for k, v in corr.items()}

    # ---- enumerate what can be built, then build it
    built = unbuildable = 0
    for lo, hi in RANGES:
        for cp in range(lo, hi + 1):
            if cp in cmap_all:
                continue
            try:
                unicodedata.name(chr(cp))
            except ValueError:
                continue
            d = unicodedata.decomposition(chr(cp))
            if not d or d.startswith("<"):
                continue
            parts = [int(x, 16) for x in d.split()]
            if len(parts) != 2:
                unbuildable += 1
                continue
            base = cmap_all.get(parts[0])
            mark = MARKNAME.get(parts[1], "uni%04X" % parts[1])
            if (base is None or mark not in glyf.keys()
                    or box(base) is None or box(mark) is None):
                unbuildable += 1
                continue

            up = base[:1].isupper()
            cx, dy = learned.get((mark, up), learned.get((mark, not up), (0, 0)))
            dx = centre(base, mark) + cx

            g = Glyph()
            g.numberOfContours = -1
            g.components = []
            for gn, x, y in ((base, 0, 0), (mark, dx, dy)):
                c = GlyphComponent()
                c.glyphName, c.x, c.y = gn, x, y
                c.flags = 0x4                       # ARGS_ARE_XY_VALUES
                g.components.append(c)

            name = "uni%04X" % cp
            # `glyf[name] = g` appends to the glyf table's own glyphOrder; keeping a second
            # copy in step by hand is what made maxp's recalc assert on a length mismatch.
            glyf[name] = g

            # The advance is the base's, except where the mark hangs past it: a caron on `d`
            # or `t` is drawn as an apostrophe to the *right* of the letter, so keeping the
            # base's advance would tuck the next letter under it.
            aw, lsb = hmtx[base]
            right = max(box(base)[1], box(mark)[1] + dx)
            hmtx[name] = (max(aw, int(right) + (aw - box(base)[1])), lsb)

            for t in f["cmap"].tables:
                if t.isUnicode():
                    t.cmap[cp] = name
            built += 1

    f.setGlyphOrder(glyf.glyphOrder)
    print("  built %d accented glyph(s); %d could not be composed here" % (built, unbuildable))

    buf = io.BytesIO()
    f.save(buf)
    return buf.getvalue()


def shipped_strings() -> set[str]:
    used = set(RUNTIME)
    for e in json.load(io.open(LOC, encoding="utf-8"))["entries"]:
        used |= set(e["text"])
    return used - set("\n\r\t")


def coverage(data: bytes) -> list[str]:
    """Every character the shipped strings contain, looked up in the font's own cmap.

    This is the check that earns the tool. A glyph Unity cannot find is drawn as **nothing** —
    no box, no question mark, no warning in the log — so a face swap that drops one character
    takes a word off a screen and every other gate in this repo stays green.
    """
    TTFont = _tt()[0]
    cmap = set()
    for t in TTFont(io.BytesIO(data))["cmap"].tables:
        cmap |= set(t.cmap.keys())

    missing = []
    for c in sorted(shipped_strings()):
        if ord(c) in cmap:
            continue
        try:
            name = unicodedata.name(c)
        except ValueError:
            name = "?"
        missing.append("U+%04X %s" % (ord(c), name))
    return missing


def proof(data: bytes, out: Path) -> None:
    """Draw the accented names. An accent placed by arithmetic is only right if it looks
    right, and this is the only thing that can say so — it has already caught three faults
    that every numeric reading here was happy with."""
    from PIL import Image, ImageDraw, ImageFont

    tmp = out.with_suffix(".tmp.ttf")
    tmp.write_bytes(data)
    try:
        pad, step = 22, 58
        im = Image.new("RGB", (1450, pad * 2 + len(PROOF) * step), (20, 20, 26))
        d = ImageDraw.Draw(im)
        font = ImageFont.truetype(str(tmp), 44)
        y = pad
        for line in PROOF:
            d.text((pad, y), line, font=font, fill=(232, 232, 238))
            y += step
        im.save(out)
    finally:
        tmp.unlink(missing_ok=True)
    print("wrote %s — look at it" % out)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path,
                    help="Fredoka[wdth,wght].ttf from google/fonts (ofl/fredoka)")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped GameFont.ttf is what this writes")
    ap.add_argument("--coverage", action="store_true",
                    help="report what the shipped font can and cannot draw")
    ap.add_argument("--proof", type=Path,
                    help="draw the accented names from the shipped font")
    a = ap.parse_args()

    if a.coverage:
        missing = coverage(OUT.read_bytes())
        print("\n".join("missing %s" % m for m in missing) if missing
              else "every character the shipped strings contain is in the font")
        return 1 if missing else 0

    if a.proof and not a.source:
        proof(OUT.read_bytes(), a.proof)
        return 0

    if not a.source:
        # Absent the source this is still a gate on what shipped — the same bargain every
        # art tool here strikes: a checkout without the pack still runs the check.
        if a.check:
            missing = coverage(OUT.read_bytes())
            if missing:
                print("shipped font cannot draw: %s" % ", ".join(missing))
                return 1
            print("no --source; coverage of the shipped font is green "
                  "(pass --source to prove it reproduces)")
            return 0
        ap.error("--source is required to write the font")

    data = build(a.source)

    missing = coverage(data)
    if missing:
        print("refusing to write: the cut font cannot draw %s" % ", ".join(missing))
        return 1

    if a.proof:
        proof(data, a.proof)

    if a.check:
        have = OUT.read_bytes()
        ok = hashlib.sha256(have).hexdigest() == hashlib.sha256(data).hexdigest()
        print("font is what the tool would write" if ok else
              "MISMATCH: shipped %d bytes, tool writes %d" % (len(have), len(data)))
        return 0 if ok else 1

    OUT.write_bytes(data)
    print("wrote %s (%d bytes), Fredoka at %s" % (OUT, len(data), AXES))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
