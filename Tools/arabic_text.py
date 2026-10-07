# -*- coding: utf-8 -*-
"""The Python mirror of `ArabicText.cs`: Arabic shaped and put in visual order, for the proof
renders. The rules are the C# file's (read its summary); the forms are read out of the
generated `ArabicForms.cs`, so the table exists once. `Tools/verify/arabic-vectors.json` holds
both copies to the same answers (`python Tools/arabic_text.py --check`, `ArabicTextTests`).
"""
from __future__ import annotations

import io
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FORMS = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Localization" / "ArabicForms.cs"
VECTORS = REPO / "Tools" / "verify" / "arabic-vectors.json"

LAM, TATWEEL, ZWNJ, ZWJ = 0x0644, 0x0640, 0x200C, 0x200D
NONE, RIGHT, DUAL, CAUSING, TRANSPARENT = range(5)


def _load():
    text = FORMS.read_text(encoding="utf-8")
    body, ligs = text.split("LamAlef", 1)
    row = re.compile(r"\{ 0x([0-9A-F]{4}), new\[\] \{ ([^}]*) \} \}")
    letters = {int(m.group(1), 16): [int(x, 16) for x in m.group(2).split(", ")] for m in row.finditer(body)}
    lam_alef = {int(m.group(1), 16): [int(x, 16) for x in m.group(2).split(", ")] for m in row.finditer(ligs)}
    return letters, lam_alef


LETTERS, LAM_ALEF = _load()


def is_arabic(c: str) -> bool:
    o = ord(c)
    return (0x0600 <= o <= 0x06FF or 0x0750 <= o <= 0x077F or 0x08A0 <= o <= 0x08FF
            or 0xFB50 <= o <= 0xFDFF or 0xFE70 <= o <= 0xFEFF)


def needs(text: str) -> bool:
    return any(is_arabic(c) for c in text or "")


def _is_mark(o: int) -> bool:
    return 0x064B <= o <= 0x065F or o == 0x0670 or (0x06D6 <= o <= 0x06ED and o not in (0x06E5, 0x06E6))


def _joining(o: int) -> int:
    if o in LETTERS:
        return DUAL if LETTERS[o][2] else RIGHT
    if o in (TATWEEL, ZWJ):
        return CAUSING
    if _is_mark(o):
        return TRANSPARENT
    return NONE


def shape(logical: str) -> str:
    if not needs(logical):
        return logical
    chars = [ord(c) for c in logical if _joining(ord(c)) != TRANSPARENT]
    out, i = [], 0
    while i < len(chars):
        c = chars[i]
        me = _joining(c)
        before = _joining(chars[i - 1]) if i > 0 else NONE
        after = _joining(chars[i + 1]) if i + 1 < len(chars) else NONE
        joins_before = me != NONE and before in (DUAL, CAUSING)
        if c == LAM and i + 1 < len(chars) and chars[i + 1] in LAM_ALEF:
            out.append(LAM_ALEF[chars[i + 1]][1 if joins_before else 0])
            i += 2
            continue
        if c in (ZWJ, ZWNJ):
            i += 1
            continue
        if c not in LETTERS:
            out.append(c)
            i += 1
            continue
        joins_after = me == DUAL and after in (DUAL, RIGHT, CAUSING)
        f = LETTERS[c]
        out.append(f[3] if joins_before and joins_after else f[1] if joins_before
                   else f[2] if joins_after else f[0])
        i += 1
    return "".join(map(chr, out))


RTL, LTR, NUMBER, SIGN, SEPARATOR, NEUTRAL = range(6)
MIRROR = dict(zip("()[]{}<>«»‹›", ")(][}{><»«›‹"))


def _kind(c: str) -> int:
    o = ord(c)
    if "0" <= c <= "9" or 0x0660 <= o <= 0x0669 or 0x06F0 <= o <= 0x06F9:
        return NUMBER
    if o == 0x060C:
        return SEPARATOR
    if is_arabic(c):
        return RTL
    if c in "+-−%٪$€£#°":
        return SIGN
    if c in ",.:/٫٬":
        return SEPARATOR
    if c.isalpha():
        return LTR
    return NEUTRAL


def reorder(line: str) -> str:
    """One shaped line in drawing order (no rich text: the proofs carry none)."""
    if not needs(line):
        return line
    k = [_kind(c) for c in line]
    n = len(k)
    for i in range(n):                                       # W4
        if k[i] == SEPARATOR and 0 < i < n - 1 and k[i - 1] == NUMBER and k[i + 1] == NUMBER:
            k[i] = NUMBER
    i = 0
    while i < n:                                             # W5 + signs
        if k[i] != SIGN:
            i += 1
            continue
        end = i
        while end + 1 < n and k[end + 1] == SIGN:
            end += 1
        touches = (i > 0 and k[i - 1] == NUMBER) or (end + 1 < n and k[end + 1] == NUMBER)
        for j in range(i, end + 1):
            k[j] = NUMBER if touches else NEUTRAL
        i = end + 1
    k = [NEUTRAL if x == SEPARATOR else x for x in k]
    strong = RTL                                             # W7
    for i in range(n):
        if k[i] in (RTL, LTR):
            strong = k[i]
        elif k[i] == NUMBER and strong == LTR:
            k[i] = LTR
    i = 0
    while i < n:                                             # N1/N2
        if k[i] != NEUTRAL:
            i += 1
            continue
        end = i
        while end + 1 < n and k[end + 1] == NEUTRAL:
            end += 1
        ltr = i > 0 and end + 1 < n and k[i - 1] == LTR and k[end + 1] == LTR
        for j in range(i, end + 1):
            k[j] = LTR if ltr else RTL
        i = end + 1
    level = [1 if x == RTL else 2 for x in k]
    j = n - 1
    while j >= 0 and line[j] == " ":                         # L1
        level[j] = 1
        j -= 1
    chars = list(line)
    i = 0
    while i < n:                                             # L2
        if level[i] != 2:
            i += 1
            continue
        end = i
        while end + 1 < n and level[end + 1] == 2:
            end += 1
        chars[i:end + 1] = chars[i:end + 1][::-1]
        level[i:end + 1] = level[i:end + 1][::-1]
        i = end + 1
    chars.reverse()
    level.reverse()
    return "".join(MIRROR.get(c, c) if lv == 1 else c for c, lv in zip(chars, level))


def visual(logical: str) -> str:
    if not needs(logical):
        return logical
    return "\n".join(reorder(line) for line in shape(logical).split("\n"))


def _cp(s):
    return [ord(c) for c in s]


def check() -> int:
    """Run the shared vectors. Each case carries code points as well as text, because
    `JsonUtility` truncates a string at an escape and the C# side reads the code points."""
    cases = json.load(io.open(VECTORS, encoding="utf-8"))["cases"]
    bad = 0
    for c in cases:
        got = shape(c["text"]) if c["rule"] == "shape" else visual(c["text"])
        if _cp(c["text"]) != c["textCodes"] or _cp(c["expected"]) != c["expectedCodes"]:
            print("STALE CODES %s: the text and its code points disagree" % c["name"])
            bad += 1
        if got != c["expected"]:
            print("FAIL %s: got %s want %s" % (c["name"], [hex(ord(x)) for x in got],
                                              [hex(ord(x)) for x in c["expected"]]))
            bad += 1
    print("%d of %d arabic vectors pass" % (len(cases) - bad, len(cases)))
    return 1 if bad else 0


if __name__ == "__main__":
    if "--check" in sys.argv:
        raise SystemExit(check())
    sys.stdout.reconfigure(encoding="utf-8")
    for arg in sys.argv[1:]:
        print(visual(arg))
