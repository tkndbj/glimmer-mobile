#!/usr/bin/env python3
"""Builds firebase/functions/src/name-blocklist.json.

    python Tools/make_name_blocklist.py                  # rebuild, fetching upstream
    python Tools/make_name_blocklist.py --source-dir DIR  # rebuild from a local upstream clone
    python Tools/make_name_blocklist.py --offline         # keep the vendored section as it is
    python Tools/make_name_blocklist.py --check           # prove the checked-in file matches

The file it writes is imported by `blocklist.ts` as the built-in floor AND published to
`config/names` by `seed-config.mjs`, which overrides it. It lives under `src/` rather than
in `firebase/shared/` because `shared/` means "both runtimes read this" and no client ever
sees a word of it -- see invariant 19b. Three of the four sections in it are *curated* and live in this script,
reviewed in the same diff as any change to them; the fourth is *vendored* and refreshed from
upstream, which is the only part a rerun rewrites.

Why the split, and why the curated part is the small one
--------------------------------------------------------
`anywhere` is matched as a substring, so every entry in it is a false-positive risk against
every name in the world. That is not hypothetical here: `rape` was in the shipped list and it
matches **Grapevine**, in a game about a garden. So it is short, hand-written, argued over, and
guarded by the rules at the bottom of this file.

`exact` is matched against the whole name and against each of its words, which cannot have a
false positive by construction -- a whole-string comparison either is the word or is not. That
is what makes it safe for it to be thousands of entries long and to come from somebody else.

Upstream is LDNOOBW, "List of Dirty Naughty Obscene and Otherwise Bad Words", CC-BY-4.0,
27 languages. It is the list most of this industry starts from. It is deliberately *not* used
for substring matching, which is the mistake that gives that list its reputation for blocking
Scunthorpe.

What this script will not do
----------------------------
It will not silently drop a curated entry, and it refuses rather than warns on the four ways a
list can be quietly wrong -- see `validate`. A blocklist that fails open looks exactly like a
blocklist that is working, which is the same reason `ManifestSync.SurvivesRoundTrip` refuses a
write rather than printing a warning beside the word "synced".
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "firebase" / "functions" / "src" / "name-blocklist.json"

UPSTREAM = (
    "https://raw.githubusercontent.com/LDNOOBW/"
    "List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words/master/"
)

LANGUAGES = [
    "ar", "cs", "da", "de", "en", "eo", "es", "fa", "fi", "fil", "fr", "hi", "hu",
    "it", "ja", "kab", "ko", "nl", "no", "pl", "pt", "ru", "sv", "th", "tlh", "tr", "zh",
]

# --------------------------------------------------------------- the curated sections

# Matched as a SUBSTRING. Everything here must be a word that essentially never occurs inside
# an innocent one, in any language, because that is precisely what this class does.
#
# Four entries that look conspicuously absent are absent on purpose, and each cost a real
# player somewhere a working name:
#
#   nazi   -- Nazir, Nazia, Nazim and Nazish are ordinary given names. In `exact` instead, so
#             "Nazi" alone is refused and "Nazir" is not.
#   porn   -- an extremely common element of Thai names in Latin script: Pornchai, Pornthip,
#             Supaporn. In `exact` only, which still refuses the bare word.
#   anal   -- analysis, analyst, canal, banal.
#   ass    -- bass, pass, class, glass, grass, embassy, assassin, Cassidy.
#   cock   -- peacock, cockatoo, shuttlecock, Hancock, Woodcock.
#   dick   -- Dickens, Dickinson, and it is a given name.
#
# All six are still refused as a whole name by the vendored list. That is the trade this class
# split exists to make: catch the embedded slur, and never cost somebody their own name.
ANYWHERE = [
    # Sexual and scatological, unambiguous as substrings.
    "fuck", "shit", "cunt", "whore", "slut", "bitch", "pussy", "penis", "vagina",
    "boobs", "dildo", "blowjob", "handjob", "rimjob", "cumshot", "creampie",
    "gangbang", "bukkake", "fisting", "twat", "wanker", "bollocks", "arsehole",
    "asshole", "cocksuck", "dickhead", "jerkoff",
    # Slurs.
    "nigger", "nigga", "faggot", "tranny", "shemale", "retard", "kukluxklan",
    # Abuse of a person, and the categories no board may carry at any size.
    "rape", "rapist", "molest", "incest", "pedophil", "paedophil", "bestiality",
    "zoophil", "hitler", "killyourself",
    # Pornography brands, which arrive as advertising rather than as insult.
    "pornhub", "pornstar", "xvideos", "onlyfans",
    # Scams that impersonate the game's own economy. A name is a cheap billboard and this is
    # the form the abuse takes in a game with an in-app shop.
    "freegem", "freecoin", "freecredit",
]

# Matched as a SUBSTRING, like ANYWHERE, and kept a separate class so a refusal can say which
# it was. Impersonation is `AdminFern` rather than `Admin`, so a whole-name rule would miss the
# form the abuse actually takes -- and a shipped vector already asserted `Fernadminmoss` is
# refused. It pays for that with entries in ALLOW: badminton, stafford, systemic.
#
# Minimal by construction: because this is a substring class, `admin` already covers `admins`,
# `administrator` and `administrators`, and the validator refuses the longer forms rather than
# letting them sit there looking like coverage. Every entry is at least five characters, which
# is what keeps the false-positive surface to the six words rescued in ALLOW.
RESERVED = [
    "admin", "moderator", "support", "staff", "official", "system",
    "helpdesk", "customerservice", "gamemaster",
    "glimmergrove", "glimmergroove",
]

# Cut out of the name before the `anywhere` substring test runs. This is the Scunthorpe repair,
# and every entry exists because some entry above matches inside it.
#
# `kshitij`, `kshitiz` and `shitala` are the ones worth not deleting on a tidy-up: they are
# ordinary Indian given names that contain `shit`, and they are exactly the kind of false
# positive nobody on an English-speaking team ever tests for.
ALLOW = [
    # admin / staff / system / support  (the reserved class is a substring class)
    "badminton", "stafford", "staffordshire", "staffy", "systemic", "systematic",
    "supportive", "supporter",
    # rape
    "grape", "grapes", "grapevine", "grapefruit", "grapeseed", "rapeseed",
    "drape", "drapes", "draped", "drapery", "scrape", "scraped", "scraper", "scrapes",
    "therapist", "therapeutic", "trapeze", "crape", "agrape",
    # shit
    "shiitake", "shitake", "mishit", "mishits", "kshitij", "kshitiz", "kshiti", "shitala",
    # cunt
    "scunthorpe",
    # penis
    "penistone",
]


# Whole-word entries upstream does not carry.
#
# **Upstream coverage is uneven and it is worth knowing how uneven.** LDNOOBW's Russian list
# has 151 entries and does not contain `сука`, which is among the two or three commonest
# obscenities in the language; its Japanese list has 180 and contains no word for genitalia at
# all. Both were found by the test suite asserting they were blocked and discovering they were
# not -- which is the only way a gap in a word list is ever found, and the reason the suite
# asserts on specific words rather than on a count.
#
# This is where a gap is closed without waiting on somebody else's repository. Everything here
# is matched as a whole name or a whole word, so an entry cannot cost anybody a name that
# merely contains it, which is what makes it safe to add on a report without a long argument.
EXTRA_EXACT = [
    # Russian
    "сука", "суки", "сучка", "пизда", "пизде", "пизду", "ебать", "ебал", "ебло", "ебаный",
    "мудак", "мудило", "пидор", "пидорас", "гандон", "залупа", "дрочить", "шлюха",
    "говно", "жопа", "долбоеб", "уебок",
    # Japanese
    "チンポ", "チンコ", "マンコ", "おっぱい", "セックス", "エロい", "きちがい", "死ね",
    "ちんぽ", "ちんこ", "まんこ",
    # Korean
    "씨발", "시발", "개새끼", "병신", "지랄", "좆같아", "니미", "썅",
    # Turkish
    "amk", "aq", "oç", "sikerim", "siktir", "yarrak", "piç", "orospu çocuğu",
    # Latin-script gaps
    "wanker", "bellend", "knobhead", "arsewipe", "shithead", "twatwaffle",
]


# ------------------------------------------------------------------------- upstream

def fetch(language: str, source_dir: Path | None) -> list[str]:
    """One language, from a local checkout if given and from upstream otherwise.

    `--source-dir` is not a convenience. Upstream is a third party's repository, so a build
    that can only work while it is reachable is a build that stops working on somebody else's
    schedule -- and the twenty-seven requests are slow enough over some links to look hung.
    Point it at a clone and this is deterministic and instant.
    """

    if source_dir is not None:
        local = source_dir / language
        if not local.exists():
            local = source_dir / f"{language}.txt"
        if not local.exists():
            sys.exit(f"--source-dir has no file for '{language}' (looked for "
                     f"'{language}' and '{language}.txt' in {source_dir})")

        body = local.read_text(encoding="utf-8")
    else:
        with urllib.request.urlopen(UPSTREAM + language, timeout=30) as response:
            body = response.read().decode("utf-8")

    return [line.strip() for line in body.splitlines() if line.strip()]


def vendored(offline: bool, source_dir: Path | None,
             previous: dict | None) -> tuple[list[str], list[str]]:
    """The `exact` section and the languages it came from."""

    if offline:
        if not previous:
            sys.exit("--offline needs an existing file to keep the vendored section from")

        return list(previous.get("exact", [])), list(previous.get("languages", []))

    words: set[str] = set()
    languages: list[str] = []

    for language in LANGUAGES:
        try:
            entries = fetch(language, source_dir)
        except (urllib.error.URLError, TimeoutError) as error:
            sys.exit(f"could not fetch '{language}' from upstream: {error}\n"
                     f"re-run with --offline to keep the vendored section as it is")

        languages.append(language)
        words.update(entries)

    return sorted(words), languages


# ------------------------------------------------------------------------ validation

def squeeze(text: str) -> str:
    """A run of one character becomes one. Mirrors `squeeze` in profanity.ts."""

    out: list[str] = []
    for ch in text:
        if not out or out[-1] != ch:
            out.append(ch)
    return "".join(out)


def rescues(word: str, blocked: list[str]) -> bool:
    """Whether an allowed word actually shadows a blocked one.

    **Tested against the squeezed form as well as the literal one, because the matcher is.**
    `shiitake` does not contain `shit` -- it contains `shiit` -- and it is a false positive
    anyway, because the matcher squeezes runs so that `fuuuck` reduces onto `fuck`, and the
    same reduction turns `shiitake` into `shitake`. A validator that models only half the
    matcher passes the entry that rescues nothing and fails the entry doing real work.
    """

    forms = (word.lower(), squeeze(word.lower()))
    return any(bad in form for bad in blocked for form in forms)


def validate(anywhere: list[str], reserved: list[str], allow: list[str],
             exact: list[str]) -> list[str]:
    """The four ways this list can be quietly wrong. All four are refusals, never warnings."""

    problems: list[str] = []

    # Both substring classes, because both can produce a false positive and both are rescued
    # by the same allow list. Keeping this as `anywhere` alone is what would let `badminton`
    # be deleted as dead weight and quietly take the name down again.
    lower = [w.lower() for w in anywhere] + [w.lower() for w in reserved]

    # 1. A short substring entry is a false-positive machine. Three characters matches inside
    #    a surprising number of ordinary words in a surprising number of languages; four is
    #    where that stops being true often enough to be worth the catch.
    for word in anywhere + reserved:
        if len(word) < 4:
            problems.append(f"anywhere entry '{word}' is shorter than four characters; "
                            f"a substring that short will match innocent names. Move it to "
                            f"the exact list, which matches whole names only.")

    # 2. An allowed word that IS a blocked word disables that entry completely and silently:
    #    it would be carved out of every name before the test ever ran.
    for word in allow:
        if word.lower() in lower:
            problems.append(f"allow entry '{word}' is also an anywhere entry, which would "
                            f"switch that entry off for every name in the game.")

    # 3. An allowed word that contains no blocked word is dead weight, and dead weight in a
    #    list like this is how somebody later concludes the rescue is not needed.
    for word in allow:
        if not rescues(word, lower):
            problems.append(f"allow entry '{word}' shadows no anywhere entry, literally or "
                            f"once squeezed, so it rescues nothing. Delete it, or the entry "
                            f"it was meant to rescue is missing.")

    # 4. One substring entry inside another means the longer one can never be the reason a
    #    name was refused, so the log would name the wrong word for ever.
    for word in anywhere + reserved:
        for other in anywhere + reserved:
            if word is not other and other.lower() in word.lower():
                problems.append(f"anywhere entry '{word}' contains '{other}', so it can never "
                                f"be the entry that matches. Drop the longer one.")

    # A reserved or exact entry that folds away is not checkable here -- that is the functions'
    # test, which runs the shipped fold. What is checkable is the empty string, which would
    # match every name ever.
    for word in exact:
        if not word.strip():
            problems.append("an empty entry would match every name in the game.")

    return problems


# ----------------------------------------------------------------------------- main

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                       help="fail if the checked-in file is not what this would write")
    parser.add_argument("--offline", action="store_true",
                       help="keep the vendored section from the existing file")
    parser.add_argument("--source-dir", type=Path, default=None,
                       help="read the upstream language files from here instead of fetching")
    args = parser.parse_args()

    previous = json.loads(OUT.read_text(encoding="utf-8")) if OUT.exists() else None

    # `--check` never reaches the network unless it was told exactly where to look. It is a gate,
    # and a gate that fails when a third party's repository is slow is a gate somebody switches
    # off. What it is actually checking is that the *curated* sections in this file agree with
    # what is committed -- the vendored half is data somebody deliberately refreshed.
    offline = args.offline or (args.check and args.source_dir is None)

    exact, languages = vendored(offline, args.source_dir, previous)

    # A word carried by both classes would be matched by the cheaper one and never by the
    # other, so the vendored section drops anything the curated sections already hold. This is
    # not tidiness: it keeps the refusal's reported class truthful.
    curated = {w.lower() for w in ANYWHERE} | {w.lower() for w in RESERVED}
    exact = sorted({w for w in list(exact) + EXTRA_EXACT if w.lower() not in curated})

    problems = validate(ANYWHERE, RESERVED, ALLOW, exact)
    if problems:
        for problem in problems:
            print(f"  error: {problem}", file=sys.stderr)
        print(f"\n{len(problems)} problem(s); nothing written.", file=sys.stderr)
        return 1

    version = (previous or {}).get("version", 0) + 1 if not args.check else \
        (previous or {}).get("version", 1)

    document = {
        "_comment": (
            "Generated by Tools/make_name_blocklist.py. The 'exact' section is vendored from "
            "LDNOOBW (CC-BY-4.0); the other three are curated in that script. Published to "
            "config/names by firebase/seed/seed-config.mjs."
        ),
        "_source": "https://github.com/LDNOOBW/"
                   "List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words",
        "_licence": "CC-BY-4.0",
        "version": version,
        "languages": languages,
        "anywhere": sorted(ANYWHERE),
        "reserved": sorted(RESERVED),
        "allow": sorted(ALLOW),
        "exact": exact,
    }

    text = json.dumps(document, ensure_ascii=False, indent=2) + "\n"

    if args.check:
        if not previous:
            print("no checked-in file to check", file=sys.stderr)
            return 1

        current = OUT.read_text(encoding="utf-8")
        same = json.loads(current) == json.loads(text)
        print("name-blocklist.json is up to date" if same
              else "name-blocklist.json differs from what this script would write")
        return 0 if same else 1

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8")

    print(f"wrote {OUT.relative_to(REPO)}  v{version}")
    print(f"  {len(ANYWHERE)} substring, {len(RESERVED)} reserved, {len(ALLOW)} allowed, "
          f"{len(exact)} whole-word across {len(languages)} language(s) "
          f"(+{len(EXTRA_EXACT)} curated)")
    print(f"  {len(text.encode('utf-8')) / 1024:.1f} KB")
    print("\nnext: npm --prefix firebase/functions run seed   (publishes config/names)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
