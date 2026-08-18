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

Exit code is 1 when a key is missing.
"""

import io
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TABLE = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")
SOURCE = os.path.join(ROOT, "Assets", "Game")

# "ui.something.else" — two or more dot-separated lowercase segments. The same
# shape the Editor validator looks for, and the reason keys must be written out
# rather than concatenated: a literal is the only thing either checker can see.
KEY = re.compile(r'"((?:ui|err|mech)\.[a-z0-9_]+(?:\.[a-z0-9_]+)+)"')

# Keys the game derives from a permanent id rather than writing out — a level's
# name, a companion's, a mechanic tip's. They are checked by the content
# validator, which knows the ids; here they would look like unused entries.
DERIVED_PREFIXES = ("level.", "chapter.", "ui.companion.", "ui.avatar.", "ui.tip.",
                    "ui.piece.", "ui.plot.")

# Keys whose middle segment is a content id: `ui.event.<id>.name`. The prefix alone would
# also hide the event panel's own generic keys, which are written out and worth checking.
DERIVED_SHAPES = (re.compile(r"^ui\.event\.[a-z0-9_]+\.(name|blurb)$"),)

# Comments are stripped before scanning. Documentation in this codebase quotes
# keys constantly — usually to explain why one is written out rather than
# assembled — and counting those as call sites reports a key as missing on the
# strength of a sentence saying nobody should ever use it.
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"^\s*//.*$", re.M)


def strip_comments(text):
    return LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", text))


def main():
    table = json.load(io.open(TABLE, encoding="utf-8"))
    entries = table["entries"] if isinstance(table, dict) and "entries" in table else table
    defined = {e["key"] for e in entries}

    used = {}
    for dirpath, _, files in os.walk(SOURCE):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(dirpath, name)
            text = strip_comments(io.open(path, encoding="utf-8", errors="replace").read())
            for key in KEY.findall(text):
                used.setdefault(key, set()).add(os.path.relpath(path, ROOT))

    missing = sorted(k for k in used if k not in defined)
    def derived(key):
        return key.startswith(DERIVED_PREFIXES) or any(p.match(key) for p in DERIVED_SHAPES)

    unused = sorted(k for k in defined if k not in used and not derived(k))

    for key in missing:
        print("MISSING  %s   (%s)" % (key, ", ".join(sorted(used[key]))))
    for key in unused:
        print("unused   %s" % key)

    print("%d key(s) used, %d defined, %d missing, %d unused"
          % (len(used), len(defined), len(missing), len(unused)))

    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
