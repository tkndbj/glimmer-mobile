# -*- coding: utf-8 -*-
"""Cuts the game's Arabic face, and the shaping table that drives it.

    python Tools/make_arabic_font.py --source <BalooBhaijaan2[wght].ttf>   # write both
    python Tools/make_arabic_font.py --check                               # prove both
    python Tools/make_arabic_font.py --proof <png>                         # look at it

Two outputs, written together because each is only true of the other:

- `Assets/Game/Fonts/GameFontArabic.ttf` — a **fallback** of `GameFont`, never a face the
  game asks for. `GameFont.ttf.meta` names it in `fallbackFontReferences`, so a Latin letter
  is still Titan One and only what Titan One lacks (Arabic) is drawn from here. One face swap
  is still one file (`Fonts/GameFont` is a role, 44), and nothing reads this one by address.
- `Assets/Game/Scripts/Domain/Localization/ArabicForms.cs` — the letter → contextual-form
  table `ArabicText.Shape` reads.

**Why a font has to be cut at all.** uGUI's legacy `Text` draws one glyph per code point
through FreeType and never runs a shaper: no GSUB, no joining, no right-to-left. Arabic is a
joined script — each letter has up to four shapes picked by its neighbours — so the game shapes
in C# and hands the renderer the **presentation-form code points** (U+FB50–U+FDFF,
U+FE70–U+FEFF), one fixed shape each. A modern font draws those shapes only through its GSUB
and maps few of them in its cmap (Baloo Bhaijaan 2 maps 9 of the 141 Forms-B letters), so this
tool reads the `init`/`medi`/`fina` single substitutions and the `rlig` lam-alef ligatures and
publishes each resulting glyph under the code point Unicode gives that shape. **Enumerated, not
listed**: every presentation form whose compatibility decomposition names a letter the face
draws is tried, and the table holds exactly the forms that resolved — so `ArabicText` can never
emit a code point this font cannot draw.

**Why Baloo Bhaijaan 2.** The Arabic member of Ek Type's Baloo family: rounded terminals and real
weight, the register Titan One sets (`CRAFT.md`), pinned at its heaviest instance because a
variable font dropped in unpinned draws its default (Regular) in Unity's legacy `Font`. SIL OFL
1.1 with **no Reserved Font Name** (checked in the source's own OFL.txt), so renaming is not
required; it is renamed anyway so it can never collide with an installed face of the same name.
The copyright and licence records (nameID 0 and 13) are untouched, which with
`Assets/Game/Fonts/OFL-Arabic.txt` discharges OFL condition 2.

**Subset to the Arabic block and its forms.** Latin, digits and punctuation stay Titan One's:
the fallback is consulted only for a code point the primary lacks, and anything kept here that
Titan One also draws is dead weight in every bundle. GSUB/GPOS are dropped for the same reason
— Unity's legacy renderer never reads them.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import re
import sys
import unicodedata
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FONTS = REPO / "Assets" / "Game" / "Fonts"
OUT = FONTS / "GameFontArabic.ttf"
TABLE = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Localization" / "ArabicForms.cs"
PRIMARY_META = FONTS / "GameFont.ttf.meta"

#: The heaviest instance. See the module note on variable fonts.
AXES = {"wght": 800.0}

#: Not "Baloo Bhaijaan 2" — see the module note. nameID 0 and 13 are left as the designer wrote them.
NAMES = {1: "Gemfire Display Arabic", 2: "Regular", 4: "Gemfire Display Arabic",
         6: "GemfireDisplayArabic", 16: "Gemfire Display Arabic", 17: "Regular"}

#: What the face keeps besides the presentation forms: the Arabic block itself (the logical
#: letters, so a coverage check over the string tables can see them, and Arabic punctuation and
#: digits a player name may carry). Harakat are kept in the font but stripped by the shaper,
#: because the legacy renderer cannot stack a mark on a letter.
KEEP = [(0x0600, 0x06FF)]

#: How a presentation form's decomposition tag maps to the column of the generated table.
FORMS = {"<isolated>": 0, "<final>": 1, "<initial>": 2, "<medial>": 3}

#: The alefs lam fuses with (U+FEF5..U+FEFC), and the four ligature code points as
#: (isolated, final) per alef. Unicode encodes exactly these four and no other mandatory ligature.
LAM = 0x0644
LAM_ALEF = {0x0622: (0xFEF5, 0xFEF6), 0x0623: (0xFEF7, 0xFEF8),
            0x0625: (0xFEF9, 0xFEFA), 0x0627: (0xFEFB, 0xFEFC)}

#: Drawn to judge the cut by eye (`--proof`): the shapes, the ligatures and a mixed line.
PROOF = ["جواهر النار",
         "سلام لا لأ لإ لآ",
         "المستوى 12 — XP",
         "ببب ههه ععع ككك ييي"]


def _tt():
    try:
        from fontTools.ttLib import TTFont
        from fontTools.varLib.instancer import instantiateVariableFont
        from fontTools import subset
    except ImportError:
        sys.exit("fontTools is needed to cut the font: pip install --user fonttools")
    return TTFont, instantiateVariableFont, subset


def _single(gsub, tag):
    """Every single substitution reached from feature `tag`, merged across its lookups."""
    out = {}
    for fr in gsub.FeatureList.FeatureRecord:
        if fr.FeatureTag != tag:
            continue
        for li in fr.Feature.LookupListIndex:
            lookup = gsub.LookupList.Lookup[li]
            for st in lookup.SubTable:
                kind = lookup.LookupType
                if kind == 7:
                    kind, st = st.ExtensionLookupType, st.ExtSubTable
                if kind == 1:
                    for a, b in st.mapping.items():
                        out.setdefault(a, b)
    return out


def _ligatures(gsub, tag):
    """(first, second) -> ligature glyph for every two-glyph ligature under `tag`."""
    out = {}
    for fr in gsub.FeatureList.FeatureRecord:
        if fr.FeatureTag != tag:
            continue
        for li in fr.Feature.LookupListIndex:
            lookup = gsub.LookupList.Lookup[li]
            for st in lookup.SubTable:
                kind = lookup.LookupType
                if kind == 7:
                    kind, st = st.ExtensionLookupType, st.ExtSubTable
                if kind != 4:
                    continue
                for first, ligs in st.ligatures.items():
                    for lig in ligs:
                        if len(lig.Component) == 1:
                            out.setdefault((first, lig.Component[0]), lig.LigGlyph)
    return out


def build(source: Path) -> tuple[bytes, dict[int, list[int]], dict[int, tuple[int, int]]]:
    TTFont, instance, subset = _tt()

    # `recalcTimestamp=False`: without it fontTools stamps `head.modified` with the wall clock
    # and `--check` could never agree with a file written a second earlier.
    f = TTFont(str(source), recalcTimestamp=False)
    if "fvar" not in f:
        sys.exit("%s is static; AXES expects the variable source" % source.name)
    instance(f, AXES, inplace=True, updateFontNames=False)
    f["OS/2"].usWeightClass = int(AXES["wght"])

    cmap = f.getBestCmap()
    gsub = f["GSUB"].table
    form_of = {"<isolated>": {}, "<initial>": _single(gsub, "init"),
               "<medial>": _single(gsub, "medi"), "<final>": _single(gsub, "fina")}
    isol = _single(gsub, "isol")
    rlig = _ligatures(gsub, "rlig")

    added: dict[int, str] = {}
    letters: dict[int, list[int]] = {}
    for cp in list(range(0xFB50, 0xFE00)) + list(range(0xFE70, 0xFF00)):
        try:
            unicodedata.name(chr(cp))
        except ValueError:
            continue
        parts = unicodedata.decomposition(chr(cp)).split()
        if len(parts) != 2 or parts[0] not in FORMS:
            continue                                # a ligature, a mark form, or unassigned
        base = int(parts[1], 16)
        if not 0x0620 <= base <= 0x06FF or base not in cmap:
            continue
        gname = cmap[base]
        glyph = isol.get(gname, gname) if parts[0] == "<isolated>" else form_of[parts[0]].get(gname)
        if glyph is None:
            continue
        added[cp] = glyph
        row = letters.setdefault(base, [0, 0, 0, 0])
        # First encoding wins: Unicode has a few duplicate forms (e.g. Forms-A vs Forms-B), and
        # Forms-B, enumerated second, is the one every Arabic font and text engine agrees on.
        if row[FORMS[parts[0]]] == 0 or 0xFE70 <= cp:
            row[FORMS[parts[0]]] = cp

    # A letter is in the table only if it has its isolated and final shapes; a joining letter
    # also needs its initial and medial. Anything less would draw a letter in the wrong shape.
    for base in list(letters):
        iso, fin, ini, med = letters[base]
        if not iso or not fin or (ini == 0) != (med == 0):
            del letters[base]

    lam = cmap.get(LAM)
    lam_init, lam_medi = form_of["<initial>"].get(lam), form_of["<medial>"].get(lam)
    ligatures: dict[int, tuple[int, int]] = {}
    for alef, (iso_cp, fin_cp) in LAM_ALEF.items():
        a = cmap.get(alef)
        a_fin = form_of["<final>"].get(a)
        g_iso, g_fin = rlig.get((lam_init, a_fin)), rlig.get((lam_medi, a_fin))
        if g_iso and g_fin:
            added[iso_cp], added[fin_cp] = g_iso, g_fin
            ligatures[alef] = (iso_cp, fin_cp)

    for t in f["cmap"].tables:
        if t.isUnicode():
            for cp, g in added.items():
                t.cmap[cp] = g

    for nid, val in NAMES.items():
        f["name"].setName(val, nid, 3, 1, 0x409)
        f["name"].setName(val, nid, 1, 0, 0)

    keep = {cp for lo, hi in KEEP for cp in range(lo, hi + 1) if cp in cmap} | set(added)
    opts = subset.Options()
    opts.layout_features = []
    opts.drop_tables += ["GSUB", "GPOS", "GDEF", "STAT", "DSIG"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.glyph_names = False
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=sorted(keep))
    sub.subset(f)

    buf = io.BytesIO()
    f.save(buf)
    return buf.getvalue(), letters, ligatures


def table_source(letters: dict[int, list[int]], ligatures: dict[int, tuple[int, int]]) -> str:
    rows = "\n".join(
        "            { 0x%04X, new[] { 0x%04X, 0x%04X, 0x%04X, 0x%04X } },  // %s"
        % (base, *forms, unicodedata.name(chr(base)).replace("ARABIC LETTER ", "").lower())
        for base, forms in sorted(letters.items()))
    ligs = "\n".join(
        "            { 0x%04X, new[] { 0x%04X, 0x%04X } },  // lam + %s"
        % (alef, iso, fin, unicodedata.name(chr(alef)).replace("ARABIC LETTER ", "").lower())
        for alef, (iso, fin) in sorted(ligatures.items()))
    return (
        "// <auto-generated>\n"
        "// Written by Tools/make_arabic_font.py from the presentation forms GameFontArabic.ttf\n"
        "// draws. Do not edit: re-run the tool, and `--check` proves the two still agree.\n"
        "// </auto-generated>\n"
        "using System.Collections.Generic;\n"
        "\n"
        "namespace GlimmerGrove.Localization\n"
        "{\n"
        "    /// <summary>\n"
        "    /// Every Arabic letter the game's Arabic face can draw, and its four shapes as\n"
        "    /// presentation-form code points: isolated, final, initial, medial. A letter with no\n"
        "    /// initial (0) joins only to the letter before it.\n"
        "    /// </summary>\n"
        "    static class ArabicForms\n"
        "    {\n"
        "        internal static readonly Dictionary<int, int[]> Letters = new Dictionary<int, int[]>\n"
        "        {\n" + rows + "\n"
        "        };\n"
        "\n"
        "        /// <summary>Lam followed by one of these alefs is one glyph: isolated, final.</summary>\n"
        "        internal static readonly Dictionary<int, int[]> LamAlef = new Dictionary<int, int[]>\n"
        "        {\n" + ligs + "\n"
        "        };\n"
        "    }\n"
        "}\n")


def meta_guid(path: Path) -> str | None:
    m = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else None


def wiring() -> list[str]:
    """The fallback is a reference by GUID in GameFont's importer settings; prove it points here."""
    faults = []
    ours = Path(str(OUT) + ".meta")
    if not ours.exists():
        return ["%s has no .meta, so nothing can reference it" % OUT.name]
    guid = meta_guid(ours)
    text = PRIMARY_META.read_text(encoding="utf-8")
    if not re.search(r"fallbackFontReferences:\s*\n\s*- \{fileID: 12800000, guid: %s, type: 3\}" % guid, text):
        faults.append("GameFont.ttf.meta does not name GameFontArabic (%s) as a fallback" % guid)
    if "includeFontData: 1" not in ours.read_text(encoding="utf-8"):
        faults.append("GameFontArabic.ttf.meta must include its font data, or a device draws nothing")
    return faults


def proof(data: bytes, out: Path) -> None:
    """Shape the proof lines through the generated table's rules and draw them, right to left,
    beside Titan One at the same size — the size match is judged here, by eye."""
    from PIL import Image, ImageDraw, ImageFont
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from arabic_text import visual   # the Python mirror of ArabicText

    tmp = out.with_suffix(".tmp.ttf")
    tmp.write_bytes(data)
    try:
        pad, step, size = 22, 84, 56
        im = Image.new("RGB", (1300, pad * 2 + (len(PROOF) + 1) * step), (20, 20, 26))
        d = ImageDraw.Draw(im)
        ar = ImageFont.truetype(str(tmp), size)
        latin = ImageFont.truetype(str(FONTS / "GameFont.ttf"), size)
        y = pad
        d.text((pad, y), "Gemfire LEVEL 12 Turret", font=latin, fill=(232, 232, 238))
        y += step
        for line in PROOF:
            runs = _runs(visual(line))
            x = 1300 - pad - sum(d.textlength(t, font=latin if l else ar) for l, t in runs)
            for l, t in runs:
                f = latin if l else ar
                d.text((x, y), t, font=f, fill=(232, 232, 238))
                x += d.textlength(t, font=f)
            y += step
        im.save(out)
    finally:
        tmp.unlink(missing_ok=True)
    print("wrote %s — look at it" % out)


def _runs(text: str):
    """Split a visual line into (is_latin, text) runs, the way the primary/fallback split falls."""
    runs, cur, latin = [], "", None
    for c in text:
        l = not (0x0600 <= ord(c) <= 0x06FF or 0xFB50 <= ord(c) <= 0xFEFF)
        if latin is None or l == latin:
            cur += c
        else:
            runs.append((latin, cur))
            cur = c
        latin = l
    if cur:
        runs.append((latin, cur))
    return runs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path,
                    help="BalooBhaijaan2[wght].ttf from google/fonts (ofl/baloobhaijaan2)")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped font and table are what this writes, and the fallback is wired")
    ap.add_argument("--proof", type=Path, help="draw the proof lines")
    a = ap.parse_args()

    faults = wiring() if OUT.exists() else ["%s is missing" % OUT]

    if not a.source:
        if a.proof:
            proof(OUT.read_bytes(), a.proof)
            return 0
        if a.check:
            # Absent the source this is still a gate on what shipped, as every art tool here is.
            for fault in faults:
                print(fault)
            if not faults:
                print("no --source; the fallback is wired (pass --source to prove the cut reproduces)")
            return 1 if faults else 0
        ap.error("--source is required to write the font")

    data, letters, ligatures = build(a.source)
    source = table_source(letters, ligatures)
    print("%d letters, %d lam-alef ligatures, %d bytes" % (len(letters), len(ligatures), len(data)))

    if a.check:
        ok_font = OUT.exists() and hashlib.sha256(OUT.read_bytes()).digest() == hashlib.sha256(data).digest()
        ok_table = TABLE.exists() and TABLE.read_text(encoding="utf-8") == source
        print("font is what the tool would write" if ok_font else "MISMATCH: GameFontArabic.ttf")
        print("table is what the tool would write" if ok_table else "MISMATCH: ArabicForms.cs")
        for fault in faults:
            print(fault)
        return 0 if ok_font and ok_table and not faults else 1

    OUT.write_bytes(data)
    TABLE.write_text(source, encoding="utf-8", newline="\n")
    print("wrote %s and %s" % (OUT, TABLE))
    if a.proof:
        proof(data, a.proof)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
