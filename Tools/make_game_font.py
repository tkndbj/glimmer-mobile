# -*- coding: utf-8 -*-
"""Cuts the game's one font out of Titan One, and proves it can draw this game.

    python Tools/make_game_font.py --source <TitanOne-Regular.ttf>   # write it
    python Tools/make_game_font.py --check                           # prove it reproduces
    python Tools/make_game_font.py --coverage                        # what it can draw
    python Tools/make_game_font.py --proof <png>                     # look at the accents

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

**Why Titan One, and how it was chosen.** Nunito Black and Fredoka Bold were each cut,
installed, rendered on the real screens and rejected by the owner on sight. Guessing one face
at a time is what that cost; the third round cut **ten** display faces, drew the game's own
strings in each at the sizes the game draws them, and rendered the hub and tasks pages for
every one. **A specimen of ten decides in one message what ten rounds of one decide in ten.**
Titan One is heavy and rounded with real weight behind it — the register this game is aimed at
(`CRAFT.md`) — and it is one of the few in that set that still reads at task-row size.

**It is renamed, and that is a licence requirement rather than a preference.** Titan One
carries a **Reserved Font Name** ("Titan"), and OFL condition 3 forbids a *Modified Version*
from using it. Adding composed glyphs makes this a modified version, so what ships is called
**Gemfire Display**. Nunito and Fredoka declared no reserved name and kept theirs; the next
face has to be checked rather than assumed. The copyright and licence records (nameID 0 and
13) are preserved untouched, which is also what discharges OFL condition 2 — it accepts
*"machine-readable metadata fields within text or binary files"*, so the TTF carries its own
licence and `Assets/Game/Fonts/OFL.txt` is committed beside it for the humans.

**Why it builds accented glyphs.** Titan One ships 451 code points against Segoe's 2,192, and
what falls in that gap is not decoration: it is **ş ğ İ ć ę ł ń ś ź ż č ď ě ř ť ů ő ű** and
their capitals — Turkish, Polish, Czech, Hungarian, Baltic. A keeper name reaches a public
board (19), and a glyph Unity cannot find is drawn as **nothing at all**: no box, no question
mark, no log line. Each is built out of the face's *own* base letter and *own* combining mark,
so they are in its hand rather than a second typeface bolted alongside.

**Three things that build got wrong before the proof sheet caught them**, all the same lesson
— *read what the designer did, do not reason about what the glyph is called*:

- **Placement is learned, in x and in y.** "Centre the mark on the base" reproduces a font's
  own offset for most marks and is wrong for others by tens of units, because a face may lean
  its acute. Worse, the first cut learned x only and left **y at nought** — so every accented
  *capital* had its mark buried inside the letter and `ŚWIĘTO`, `ČESKÝ` and `ŁÓDŹ` drew bare.
  Both corrections are read off the composites the source ships.
- **Not the `.case` marks**, though a font may ship them and the name invites it: in Fredoka
  `uni0307.case` is `uni0307` moved *down* 97 units, which on a capital `I` buried the Turkish
  dotted capital's dot in its own stem.
- **Enumerate, never list by hand.** The first cut named eighteen characters picked off a
  coverage test that happened to exercise lowercase, so every capital of the same set was
  missing and nothing said so.

**What it cannot draw is stated rather than discovered later** — `--coverage` and the group
report at the end of a build both say so. Greek and Cyrillic are gone (Segoe had them); no
face here has ever had Arabic, Hebrew or CJK. The repair, if a keeper name ever needs one, is
`fallbackFontReferences` in the `.meta`, not another face.
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

#: Axes to pin, for a variable source. Empty for a static one like Titan One. A variable font
#: dropped in unpinned renders at its *default* instance in Unity's legacy `Font` — Light, for
#: most families — which is a change nothing fails on and everything looks wrong after.
AXES: dict[str, float] = {}

#: The name records Unity and the OS read. **Not "Titan One"**: the source carries a Reserved
#: Font Name and OFL condition 3 forbids a modified version from using it. nameID 0 and 13 —
#: the copyright and the licence — are deliberately left as the designer wrote them.
NAMES = {1: "Gemfire Display", 2: "Regular", 4: "Gemfire Display",
         6: "GemfireDisplay", 16: "Gemfire Display", 17: "Regular"}

#: Where an accented letter can come from. Vietnamese is included knowing most of it will not
#: build — it stacks two marks — and what cannot be built is counted rather than hidden.
RANGES = [(0x00C0, 0x017F), (0x0180, 0x024F), (0x1E00, 0x1EFF)]

#: What a combining mark is called in a font's glyph order. A face may ship the combining
#: form (`uni0307`), the historic name (`acutecomb`), or only the **spacing** accent
#: (`dotaccent`) — Titan One ships only the last, which is why a single spelling is not
#: enough and why the first cut against it built nothing at all and said so.
MARKNAME = {0x0300: "gravecomb", 0x0301: "acutecomb", 0x0303: "tildecomb"}
SPACING = {0x0300: "grave", 0x0301: "acute", 0x0302: "circumflex", 0x0303: "tilde",
           0x0304: "macron", 0x0306: "breve", 0x0307: "dotaccent", 0x0308: "dieresis",
           0x030A: "ring", 0x030B: "hungarumlaut", 0x030C: "caron", 0x0327: "cedilla",
           0x0328: "ogonek"}

#: Characters the UI assembles at runtime and that therefore appear in no string file.
RUNTIME = "0123456789+-x/%.,:;!?'\"()[]{}<>#@&*_=|\\~`^$ \u00b7\u2013\u2014\u2026\u00d7"

#: Names worth drawing before believing an accent is placed right (`--proof`).
PROOF = ["\u00C7a\u011fr\u0131 \u015eebnem G\u00f6k\u00e7e \u0130lkay",
         "\u0141\u00f3d\u017a Gda\u0144sk \u015awi\u0119to \u0106ma",
         "Dvo\u0159\u00e1k \u010cesk\u00fd \u0164\u016f\u0148 \u010e\u011a\u0158",
         "\u00c1RV\u00cdZT\u0170R\u0150 Bj\u00f8rn \u00c5sa \u00d1o\u00f1o",
         "\u00c7A\u011eRI \u0130LKAY \u015eEBNEM \u0141\u00d3D\u0179",
         "PLAY   1,240 gems   $0.99   5/5"]

#: Checked at the end of every build and printed. Not a gate — a face that cannot spell a
#: language is a fact to decide about, not an error — but it must never be silent.
GROUPS = {
    "Turkish": "\u011f\u011e\u0131\u0130\u015f\u015e\u00e7\u00c7\u00f6\u00d6\u00fc\u00dc",
    "Polish": "\u0105\u0104\u0107\u0106\u0119\u0118\u0142\u0141\u0144\u0143"
              "\u015b\u015a\u017a\u0179\u017c\u017b",
    "Czech": "\u010d\u010c\u010f\u010e\u011b\u011a\u0159\u0158\u0161\u0160"
             "\u0165\u0164\u016f\u016e\u017e\u017d\u00fd\u00dd",
    "Hungarian": "\u0151\u0150\u0171\u0170",
    "Baltic": "\u0101\u0113\u012b\u016b\u0117\u0173\u012f",
    "Nordic": "\u00e5\u00c5\u00e6\u00c6\u00f8\u00d8",
    "Romanian": "\u0103\u0102\u0219\u0218\u021b\u021a",
    "Vietnamese": "\u1ea1\u1eaf\u1ec7\u1ed1\u01b0\u1ee9",
    "Greek": "\u03b1\u03b2\u03b3\u03a3\u03c2",
    "Cyrillic": "\u0430\u0431\u0432\u042f\u0451",
}


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

    if AXES:
        if "fvar" not in f:
            sys.exit("%s is static but AXES asks for an instance" % source.name)
        instance(f, AXES, inplace=True, updateFontNames=True)
        f["OS/2"].usWeightClass = int(AXES.get("wght", f["OS/2"].usWeightClass))
    elif "fvar" in f:
        sys.exit("%s is variable — pin AXES, or Unity draws its default instance" % source.name)

    for nid, val in NAMES.items():
        f["name"].setName(val, nid, 3, 1, 0x409)
        f["name"].setName(val, nid, 1, 0, 0)

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

    def resolve_mark(cp):
        """What this font calls the mark for `cp`, or None if it has no glyph for it."""
        for cand in (MARKNAME.get(cp, "uni%04X" % cp), "uni%04X" % cp, SPACING.get(cp)):
            if cand and cand in glyf.keys() and box(cand) is not None:
                return cand
        return None

    def vbox(name):
        """Vertical extent, for the donor method below."""
        g = glyf[name]
        if g.numberOfContours == 0:
            return None
        ys = [p[1] for p in g.getCoordinates(glyf)[0]]
        return min(ys), max(ys)

    def from_donor(mark, upper):
        """Where this face puts `mark`, learned from a letter it drew as a single outline.

        Titan One draws every accented letter as **simple contours** rather than as a base
        plus a component, so `learned` is empty and there is no offset to copy. But the font
        still *contains* the answer: `Zdotaccent` is `Z` with a dot over it, so the dot's top
        is that glyph's top, and a mark placed to end there sits where the designer put it.
        Find any character whose decomposition is (some base, this mark) and whose base the
        font also has, then measure the difference. Only meaningful for marks that sit above
        the letter, which is every mark this is asked for.
        """
        want_cp = None
        for cp2, gname2 in cmap_all.items():
            d2 = unicodedata.decomposition(chr(cp2))
            if not d2 or d2.startswith("<"):
                continue
            p2 = [int(x, 16) for x in d2.split()]
            if len(p2) != 2 or resolve_mark(p2[1]) != mark:
                continue
            base2 = cmap_all.get(p2[0])
            if base2 is None or vbox(base2) is None or vbox(gname2) is None:
                continue
            if base2[:1].isupper() != upper:
                continue
            want_cp = (gname2, base2)
            break
        if want_cp is None:
            return None
        donor, base2 = want_cp
        mv, dv = vbox(mark), vbox(donor)
        return round(dv[1] - mv[1])          # lift the mark so its top matches the donor's

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
            mark = resolve_mark(parts[1])
            if base is None or mark is None or box(base) is None:
                unbuildable += 1
                continue

            up = base[:1].isupper()
            if (mark, up) in learned or (mark, not up) in learned:
                cx, dy = learned.get((mark, up), learned.get((mark, not up)))
            else:
                # No composite to copy: measure where the designer put this mark on a letter
                # they drew in one piece (see `from_donor`). x still centres.
                lift = from_donor(mark, up)
                if lift is None:
                    unbuildable += 1
                    continue
                cx, dy = 0, lift
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


def cmap_of(data: bytes) -> set[int]:
    TTFont = _tt()[0]
    cmap = set()
    for t in TTFont(io.BytesIO(data))["cmap"].tables:
        cmap |= set(t.cmap.keys())
    return cmap


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
    cmap = cmap_of(data)
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


def languages(data: bytes) -> None:
    """What this face can spell, printed on every build. A keeper name reaches a public
    board, so a gap here is a person's name drawn with holes in it — a thing to decide about
    rather than an error, but never a thing to be silent about."""
    cmap = cmap_of(data)
    for g, chars in GROUPS.items():
        have = sum(1 for c in chars if ord(c) in cmap)
        mark = "" if have == len(chars) else ("  <-- NONE" if have == 0 else "  <-- partial")
        print("  %-11s %2d/%-2d%s" % (g, have, len(chars), mark))


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
                    help="TitanOne-Regular.ttf from google/fonts (ofl/titanone)")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped GameFont.ttf is what this writes")
    ap.add_argument("--coverage", action="store_true",
                    help="report what the shipped font can and cannot draw")
    ap.add_argument("--proof", type=Path,
                    help="draw the accented names from the shipped font")
    a = ap.parse_args()

    if a.coverage:
        data = OUT.read_bytes()
        missing = coverage(data)
        print("\n".join("missing %s" % m for m in missing) if missing
              else "every character the shipped strings contain is in the font")
        languages(data)
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
    languages(data)

    if a.proof:
        proof(data, a.proof)

    if a.check:
        have = OUT.read_bytes()
        ok = hashlib.sha256(have).hexdigest() == hashlib.sha256(data).hexdigest()
        print("font is what the tool would write" if ok else
              "MISMATCH: shipped %d bytes, tool writes %d" % (len(have), len(data)))
        return 0 if ok else 1

    OUT.write_bytes(data)
    print("wrote %s (%d bytes)" % (OUT, len(data)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
