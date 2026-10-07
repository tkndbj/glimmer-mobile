#!/usr/bin/env python3
"""
Every key-shaped literal in the source resolves in the string table.

The offline half of invariant 6. `ContentValidation` runs the authoritative
version inside the Editor and the build gate fails on it; this is the same check
when the Editor is closed, so a missing key is caught in the commit that adds the
call site rather than in the build that ships it.

It deliberately reports both directions. A key used but not defined is a blank on
someone's screen. A key defined but unused is usually harmless, but it is also
what a renamed key leaves behind, so it is worth seeing before the table fills
with strings nobody can find the call site for.

It also counts placeholders against arguments, which is the other way a string
that resolves perfectly still reaches a player as gibberish. `Loc.Format` catches
the FormatException a missing argument raises and hands the *pattern* back — the
right behaviour on a screen, and silent — so `Loc.Format("ui.x", n)` against a
string reading "{0} turns  ·  {1}" prints the braces themselves. That shipped:
the record line kept its timed text after the clock was removed (invariant 22),
and every map node and victory panel in the game printed "{0} turns  ·  {1}".
Only a literal key can be checked here; the record line's own call site computes
its key from the level's mode, so `RecordWordingTests` pins that one in C#.

Exit code is 1 when a key is missing or a call site cannot fill its string.
"""

import io
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TABLES = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc")
TABLE = os.path.join(TABLES, "en.json")
SOURCE = os.path.join(ROOT, "Assets", "Game")

# "ui.something.else" — two or more dot-separated lowercase segments. The same
# shape the Editor validator looks for, and the reason keys must be written out
# rather than concatenated: a literal is the only thing either checker can see.
# **One dot is enough.** This asked for at least two segments after the prefix, so a two-part key
# was invisible to the scanner - and `ui.ok` sat in a shipping panel for months, defined nowhere,
# drawing the literal text "ui.ok" on a purchase button. The gate could not see it because of the
# shape of the name rather than anything about the key.
KEY = re.compile(r'"((?:ui|err|mech)\.[a-z0-9_]+(?:\.[a-z0-9_]+)*)"')

# Keys the game derives from a permanent id rather than writing out — a level's
# name, a companion's, a mechanic tip's. They are checked by the content
# validator, which knows the ids; here they would look like unused entries.
DERIVED_PREFIXES = ("level.", "chapter.", "ui.companion.", "ui.avatar.", "ui.tip.",
                    # A task and a chest tier are named from their permanent ids (invariant
                    # 5a): `task.{id}.name` (with an optional `.name_one`) and `chest.{id}.name`.
                    # Checked by content.py, which knows the ids.
                    "task.", "chest.",
                    # A reminder's two lines are derived from its kind's permanent id
                    # (`NotificationKinds.TitleKey`), which is what lets a content push
                    # reorder the slate without shipping a sentence. content.py walks the
                    # kinds and proves both halves of every one resolves.
                    "notify.",
                    "ui.piece.", "ui.land.", "ui.shelf.",
                    # A daily challenge names itself and states its rule from its permanent id
                    # (`ChallengeDefinition.NameKey` / `.BlurbKey`); content.py walks the slate.
                    "challenge.",
                    # A product and a good are named from their permanent id for the reason
                    # a glade is (invariant 5a): anything holding the id can name the thing
                    # without reading the catalog, which is what lets a purchase say what
                    # was bought long after the shop screen has gone.
                    "store.product.", "store.good.",
                    # A mode names itself from its own permanent id for the reason a glade
                    # does (invariant 5a): the switcher has to label a way of playing without
                    # reading anything, and an overridable key would put a file read in front
                    # of a control that is drawn before any chapter has loaded.
                    "mode.",
                    # A line of dialogue is named by the chapter body that says it, which is
                    # the one place in this game a key is *authored* rather than derived from
                    # an id (see `StoryLine.Key`). It is unreachable from source by
                    # construction, so it would read as unused here - and it is checked far
                    # more strictly by `content.py`, which resolves every key a chapter
                    # actually authors and errors on one that is missing.
                    "story.")

# Keys whose middle segment is a content id: `ui.event.<id>.name`. The prefix alone would
# also hide the event panel's own generic keys, which are written out and worth checking.
DERIVED_SHAPES = (re.compile(r"^ui\.event\.[a-z0-9_]+\.(name|blurb)$"),)

# A `Loc.Format("ui.x.y", a, b)` call site, with everything up to the closing
# bracket. Arguments are counted by splitting on top-level commas, so a nested
# call or an indexer inside one argument stays one argument. A call whose key is
# not a literal is invisible to this and is not meant to be caught here.
FORMAT = re.compile(r'Loc\.Format\(\s*"((?:ui|err|mech|mode|store|level|chapter)\.[a-z0-9_.]+)"\s*(,|\))', re.S)

# "{0}", "{1:0.0}", "{0,-8}" — the index is all that matters.
SLOT = re.compile(r"\{(\d+)[^}]*\}")


def arguments(text, at):
    """How many arguments follow the key, and where the call ends.

    Counts top-level commas from `at` (the character after the key's comma) to the
    bracket that closes the call, so `Compact.Number(n)` and `new[] { a, b }` each
    count once. Returns None when the call does not close in this file's text,
    which cannot happen in valid source and is not worth guessing about.
    """
    depth, count, i = 0, 1, at
    while i < len(text):
        c = text[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            if depth == 0:
                return count
            depth -= 1
        elif c == "," and depth == 0:
            count += 1
        i += 1
    return None


def underfilled(text, defined):
    """Call sites whose string asks for more arguments than they pass.

    Only under-filling is reported. A spare argument is ignored by string.Format
    and by every runtime this ships on, so complaining about it would be noise —
    and it is what a string legitimately shortened in one language looks like.
    """
    for m in FORMAT.finditer(text):
        key = m.group(1)
        if key not in defined:
            continue  # already reported as missing

        supplied = 0 if m.group(2) == ")" else arguments(text, m.end())
        if supplied is None:
            continue

        slots = [int(s) for s in SLOT.findall(defined[key])]
        wanted = max(slots) + 1 if slots else 0
        if wanted > supplied:
            yield key, wanted, supplied, defined[key]


# Comments are stripped before scanning. Documentation in this codebase quotes
# keys constantly — usually to explain why one is written out rather than
# assembled — and counting those as call sites reports a key as missing on the
# strength of a sentence saying nobody should ever use it.
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"^\s*//.*$", re.M)


def strip_comments(text):
    return LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", text))


# ------------------------------------------------------------------ translations
# A translation is held to English key for key, because `Loc` falls back silently: a key a
# table leaves out is drawn in English and a key it invents is drawn nowhere, and neither is
# visible from the language it was written in. What a key's text carries beyond its words is
# held too - the slots `Loc.Format` fills, the rich-text tags, the line breaks a tip is laid
# out around - since a translation that drops a `{1}` prints a sentence missing its number.
#
# Three characters are refused outright. An em dash: house style says none ships anywhere under
# Assets/. A double quote or a backslash: either is written as an escape in JSON, and
# `JsonUtility` truncates a string at an escape (CLAUDE.md, Serialisation) - the player would
# read half a sentence.
TAG = re.compile(r"</?[a-z]+(?:=[^>]*)?>")
REFUSED = {"—": "an em dash", '"': "a double quote", "\\": "a backslash"}

# UTF-8 read back as the Windows codepage: "ü" becomes "Ã¼", "ı" becomes "Ä±", "·" becomes "Â·".
# Every such pair is a lead letter followed by a continuation byte drawn as Latin-1 or cp1252, and
# no word in a shipped language puts one after the other ("NÃO" and "Âmbar" are followed by a
# letter). It is how a table edited through a shell that guessed the encoding wrong looks, and it
# passed every other check here once: same slots, same keys, gibberish on a German button.
MOJIBAKE = re.compile("[ÂÃÄÅ][\u0080-¿ŒœŠšŸ"
                      "Žžƒˆ˜–—‘-„†-•"
                      "…‰‹›€™]")


def shape(text):
    return (sorted(SLOT.findall(text)), sorted(TAG.findall(text)), text.count("\n"))


def translations(english):
    problems, same, tables = [], 0, 0

    for name in sorted(os.listdir(TABLES)):
        if not name.endswith(".json") or name == "en.json":
            continue
        tables += 1
        code = name[:-len(".json")]
        path = os.path.join(TABLES, name)

        table = json.load(io.open(path, encoding="utf-8"))
        if table.get("language") != code:
            problems.append("%s says it is language '%s'" % (name, table.get("language")))

        seen = {}
        for e in table.get("entries", []):
            if e["key"] in seen:
                problems.append("%s: %s is defined twice" % (name, e["key"]))
            seen[e["key"]] = e["text"]

        for key in english:
            if key not in seen:
                problems.append("%s: %s is missing" % (name, key))
        for key in seen:
            if key not in english:
                problems.append("%s: %s is not an English key" % (name, key))

        for key, text in seen.items():
            if key not in english:
                continue
            if not text.strip():
                problems.append("%s: %s is empty" % (name, key))
            if shape(text) != shape(english[key]):
                problems.append("%s: %s does not carry English's slots, tags and line breaks\n"
                                "      en: %r\n      %s: %r" % (name, key, english[key], code, text))
            for char, what in REFUSED.items():
                if char in text:
                    problems.append("%s: %s contains %s" % (name, key, what))
            if MOJIBAKE.search(text):
                problems.append("%s: %s was saved in the wrong encoding: %r" % (name, key, text))
            if text == english[key]:
                same += 1

    return problems, same, tables


def main():
    table = json.load(io.open(TABLE, encoding="utf-8"))
    entries = table["entries"] if isinstance(table, dict) and "entries" in table else table
    defined = {e["key"]: e["text"] for e in entries}

    used = {}
    short = []
    for dirpath, _, files in os.walk(SOURCE):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(dirpath, name)
            text = strip_comments(io.open(path, encoding="utf-8", errors="replace").read())
            for key in KEY.findall(text):
                used.setdefault(key, set()).add(os.path.relpath(path, ROOT))
            for key, wanted, supplied, pattern in underfilled(text, defined):
                short.append((key, wanted, supplied, pattern, os.path.relpath(path, ROOT)))

    missing = sorted(k for k in used if k not in defined)
    def derived(key):
        return key.startswith(DERIVED_PREFIXES) or any(p.match(key) for p in DERIVED_SHAPES)

    unused = sorted(k for k in defined if k not in used and not derived(k))

    for key in missing:
        print("MISSING  %s   (%s)" % (key, ", ".join(sorted(used[key]))))
    for key, wanted, supplied, pattern, where in short:
        print("UNFILLED %s   wants %d argument(s), given %d   \"%s\"   (%s)"
              % (key, wanted, supplied, pattern, where))
    for key in unused:
        print("unused   %s" % key)

    print("%d key(s) used, %d defined, %d missing, %d unfilled, %d unused"
          % (len(used), len(defined), len(missing), len(short), len(unused)))

    wrong, same, tables = translations(defined)
    for line in wrong:
        print("TRANSLATION " + line)
    print("%d translation(s), %d fault(s), %d string(s) left identical to English"
          % (tables, len(wrong), same))

    return 1 if missing or short or wrong else 0


if __name__ == "__main__":
    sys.exit(main())
