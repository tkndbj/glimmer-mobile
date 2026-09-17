# -*- coding: utf-8 -*-
"""Proves every sprite and flipbook the code asks for exists on disk.

    python Tools/verify/artnames.py

**This is the gate `sfxnames.py` says it does not cover, and it was written the day the gap
finally cost something.** `MarchView.Boom` spelt its own folder out and asked for
`Art/March/boom_fire` when the explosions live under `Art/Fx/March/`. Every offline gate was
green, the Editor's `Validate Content` and `Validate Art` were green, `AddressableAudit` was
green — because all of those prove that what is *on disk* is addressed, and that what a
**mode declares** resolves. Not one of them proved that a name a **call site** asks for
resolves to anything.

On the device it was five `InvalidKeyException`s per level and, worse, a **white rectangle**
two cells wide drawn over the board at every burst: an `Image` with a null sprite is white,
not blank (invariant 7b), and the explosion is the largest widget this mode makes. It was
found by a player, which is the most expensive way there is.

Three shapes are read, and each resolves to a path under `Assets/Game/Art/`:

* `Art.S("Ui/thing")`            -> a single sprite file
* `Art.Frames("March/bolt")`     -> a folder of frames
* `AssetManifest.MarchArt("road")` and any sibling helper -> whichever of the two the call
  site uses it for

**The `AssetManifest` helpers are read out of `AssetManifest.cs` rather than listed here**,
so a mode that adds one is covered without this file being edited — and so the *prefix* is
taken from the one place invariant 7 says it may live. That is the whole point: the bug was
a call site inventing a prefix that `AssetManifest` already knew.

**It reads literals**, which is the same rule invariant 6 imposes on loc keys and
`sfxnames.py` imposes on clip names, for the same reason: a name computed at runtime is
invisible to this and always will be. The answer is not to compute one. A concatenation onto
a known prefix (`Piece("boom_" + kind)`) is reported as unreadable rather than skipped,
because a name nothing checks is the state this file exists to end.

Only `Presentation` and `Editor` are scanned: `Art` lives in Presentation and Domain may
never reference it (invariant 3), so a literal in Domain is never a sprite name.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
SCRIPTS = REPO / "Assets" / "Game" / "Scripts"
EDITOR = REPO / "Assets" / "Game" / "Editor"
ART = REPO / "Assets" / "Game" / "Art"
MANIFEST = SCRIPTS / "Domain" / "AssetPipeline" / "AssetManifest.cs"
GROUPS = REPO / "Assets" / "AddressableAssetsData" / "AssetGroups"

#: What counts as a picture. `.meta` is Unity's and never the asset.
PICTURES = (".png", ".jpg", ".jpeg", ".psd", ".tga", ".spriteatlasv2")

#: Roots a helper may hang off, as spelt in `AssetManifest`. `ArtRoot` is the only one this
#: file resolves; a helper built on any other root is reported rather than guessed at.
ART_ROOT = "Art/"

#: Calls that want a *folder* of frames rather than one file. A manifest helper is used for
#: both, so its shape is taken from whatever wraps it - `AssetLibrary.Frames(...)` on a screen,
#: and `AssetRequest.SpriteSet(...)` in the list a mode declares its art with.
FOLDER_CALLS = ("Art.Frames", "AssetLibrary.Frames", "AssetRequest.SpriteSet")


def registered():
    """Every address the Addressables groups carry, read off the group assets.

    <b>On disk is not the same question as addressable, and that gap ships.</b> `exists`
    proves a sprite is a file; nothing here proved anything could *load* it. The importer
    hook that registers art fires on reimport only, so art written while the Editor is shut
    or unfocused - which is every run of the art tools - has a file and no address. The
    Editor's own audit asks the opposite question (a registered entry whose asset has gone),
    every offline gate was green, and what reached a phone was
    `InvalidKeyException: No Location found for Key=Art/Ui/refer` at launch with a plate drawn
    round nothing. The repair is `Addressables > Sync All Assets`; this is what says it is
    needed before a build rather than after one.

    Read from the group assets rather than from the built catalog, because the catalog is a
    build output and the groups are what the build reads.
    """
    out = set()
    for group in sorted(GROUPS.glob("*.asset")):
        for line in group.read_text(encoding="utf-8", errors="replace").splitlines():
            stripped = line.strip()
            if stripped.startswith("m_Address:"):
                out.add(stripped[len("m_Address:"):].strip())
    return out


def root_of(fn, text):
    """The prefix under `Art/` that one helper puts in front of its key, or None.

    <b>Separate from `helpers` on purpose.</b> That one only follows a body that names
    `ArtRoot` itself, so `Ui(key) => UiRoot + key` falls straight through its first guard and
    never reaches the branch written to resolve it - which is why `Art/Ui/...` is not among
    the five prefixes it reports. Widening that guard would change which call sites the
    existing sweep checks, in the same change as a fix for something else; this resolves the
    handful of roots the preloaded lists hang off and leaves that sweep exactly as it was.
    """
    body = re.search(r"public\s+static\s+string\s+%s\s*\(\s*string\s+\w+\s*\)\s*=>\s*([^;]+);"
                     % re.escape(fn), text)
    if not body:
        return None
    body = body.group(1).strip()

    plain = re.fullmatch(r'ArtRoot\s*\+\s*"([^"]*)"\s*\+\s*\w+', body)
    if plain:
        return plain.group(1)

    if re.fullmatch(r"ArtRoot\s*\+\s*\w+", body):
        return ""

    rooted = re.fullmatch(r"(\w*Root)\s*\+\s*\w+", body)
    if rooted:
        const = re.search(r'public\s+const\s+string\s+%s\s*=\s*ArtRoot\s*\+\s*"([^"]*)"'
                          % re.escape(rooted.group(1)), text)
        if const:
            return const.group(1)

    return None


def preloaded(prefixes):
    """Every address `AssetManifest`'s standing lists preload, as (field, address under Art/).

    <b>The list and its root are two different facts and the root is not in the list.</b>
    `UiSprites` holds `"refer"` and means `Art/Ui/refer`, because one line further down it is
    wrapped in `Ui(...)`. A first cut of this check pasted `ArtRoot` straight onto the name,
    got `Art/refer`, found no such file, and skipped it - so the check passed against the very
    tree that shipped the fault. **A check that cannot fail is not a check.** The wrapper is
    read off the `foreach` that consumes each list and resolved through `helpers`, so a list
    added tomorrow is covered by whatever it is wrapped in.

    Lists wrapped in something this cannot resolve - a sound helper, or a shape not written as
    `foreach (var x in List) ... Fn(x)` - are returned as unreadable rather than guessed at.
    """
    text = MANIFEST.read_text(encoding="utf-8")

    wrapper = {}
    for match in re.finditer(
            r"foreach\s*\(\s*var\s+\w+\s+in\s+(\w+)\s*\)[^;]*?(\w+)\s*\(\s*\w+\s*\)\s*\)",
            text):
        wrapper[match.group(1)] = match.group(2)

    out, unreadable = [], []
    for match in re.finditer(
            r"static\s+readonly\s+string\[\]\s+(\w+)\s*=\s*\{(.*?)\};", text, re.S):
        field, body = match.group(1), match.group(2)

        fn = wrapper.get(field)
        if fn is None:
            unreadable.append(field)
            continue

        prefix = prefixes.get(fn)
        if prefix is None:
            prefix = root_of(fn, text)
        if prefix is None:
            continue          # a sound or music helper; `sfxnames.py` has those

        # Strip comments first: these lists are more prose than names.
        body = re.sub("//[^" + chr(10) + "]*", "", body)
        for name in re.findall(r'"([^"]+)"', body):
            out.append((field, prefix + name))

    return out, unreadable


def helpers():
    """`AssetManifest`'s one-line art helpers, as name -> prefix under `Art/`.

    Read rather than listed, so a mode that adds one is covered the day it is written. Only
    the `ArtRoot + "literal" + key` shape is understood; anything else is reported by
    `main` as a helper this cannot follow, which is the honest answer.
    """
    text = MANIFEST.read_text(encoding="utf-8")
    found, unreadable = {}, []

    for match in re.finditer(
            r"public\s+static\s+string\s+(\w+)\s*\(\s*string\s+\w+\s*\)\s*=>\s*([^;]+);", text):
        name, body = match.group(1), match.group(2).strip()

        if "ArtRoot" not in body:
            continue          # a sound or music helper; `sfxnames.py` has those

        plain = re.fullmatch(r'ArtRoot\s*\+\s*"([^"]*)"\s*\+\s*\w+', body)
        if plain:
            found[name] = plain.group(1)
            continue

        bare = re.fullmatch(r'ArtRoot\s*\+\s*\w+', body)
        if bare:
            found[name] = ""
            continue

        rooted = re.fullmatch(r'(\w*Root)\s*\+\s*\w+', body)
        if rooted:
            # A helper hanging off another root constant - resolve it if that constant is
            # itself `ArtRoot + "..."`.
            const = re.search(r'public\s+const\s+string\s+%s\s*=\s*ArtRoot\s*\+\s*"([^"]*)"'
                              % rooted.group(1), text)
            if const:
                found[name] = const.group(1)
                continue

        unreadable.append(name)

    return found, unreadable


def sources():
    # Domain is scanned too, and only for `AssetManifest` calls: a mode declares the art it
    # draws as a list of them (`LevelMode.Art`), which is the one place every address a mode
    # uses is written as a literal. `Art` itself is Presentation and can never appear there.
    for root in (SCRIPTS / "Presentation", SCRIPTS / "Domain", EDITOR):
        if root.exists():
            for path in sorted(root.rglob("*.cs")):
                yield path


def first_argument(rest):
    """The text of the first argument of a call whose opening bracket has just been passed."""
    depth, out = 0, []
    for ch in rest:
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            if depth == 0:
                break
            depth -= 1
        elif ch == "," and depth == 0:
            break
        out.append(ch)
    return "".join(out)


def wrappers(text, prefixes):
    """A file's own one-line art lookups, as name -> (prefix, wants_folder).

    **A gate that cannot follow a wrapper punishes the right thing.** Routing every lookup on
    a screen through one helper is what invariant 7 asks for — and it is also what made this
    check blind to the file that needed it most, because the literal moved from the call to
    the wrapper's caller. So a wrapper whose whole body is a manifest lookup is followed, and
    the names passed to it are checked as if they had been written out.
    """
    out = {}
    for match in re.finditer(
            r"(?:static\s+)?[\w.\[\]<>]+\s+(\w+)\s*\(\s*string\s+(\w+)\s*\)\s*=>\s*"
            r"AssetLibrary\.(Sprite|Frames)\s*\(\s*AssetManifest\.(\w+)\s*\(\s*\2\s*\)\s*\)\s*;",
            text):
        name, kind, helper = match.group(1), match.group(3), match.group(4)
        if helper in prefixes:
            out[name] = (prefixes[helper], kind == "Frames")
    return out


def asked(prefixes):
    """Every (where, address, wants_folder, readable) a call site asks for."""
    shared = {"Art.S": ("", False), "Art.Frames": ("", True),
              "AssetLibrary.Sprite": ("", False), "AssetLibrary.Frames": ("", True)}
    for name, prefix in prefixes.items():
        shared["AssetManifest." + name] = (prefix, None)

    out = []
    for path in sources():
        text = path.read_text(encoding="utf-8")
        where = path.relative_to(REPO).as_posix()

        calls = dict(shared)
        calls.update(wrappers(text, prefixes))

        # Longest first, or `Art.S` would match inside `AssetManifest.S...`.
        pattern = re.compile(
            r"(?<![\w.])(" + "|".join(re.escape(c) for c in
                                      sorted(calls, key=len, reverse=True)) + r")\s*\(")

        for match in pattern.finditer(text):
            call = match.group(1)
            prefix, folder = calls[call]

            rest = text[match.end():match.end() + 600]
            first = first_argument(rest)

            # A wrapper's own *declaration* is not a call at all - `Piece(string key)` - and
            # neither is its body, which passes the parameter through. The names that matter
            # are at the call sites, which are counted separately.
            if re.fullmatch(r"\s*(?:string|char|int)\s+\w+\s*", first or ""):
                continue

            if re.fullmatch(r"\s*\w+\s*", first or ""):
                if re.match(r"\s*\w+\s*\)\s*\)?\s*;", rest):
                    continue

            # A manifest helper wrapped in a lookup is the common nested shape:
            # AssetLibrary.Frames(AssetManifest.MarchFx("boom_fire")). The names are counted
            # at the inner call, which is the one that knows the prefix - matched on the whole
            # first argument rather than on a single literal, so a ternary inside it is skipped
            # here too rather than being read against the wrong prefix.
            nested = re.match(r"\s*AssetManifest\.(\w+)\s*\(", first or "")
            if nested and nested.group(1) in prefixes:
                continue

            # Every literal in the *first* argument, which is what makes a ternary readable -
            # `Piece(rubble ? "rubble" : "ground")` names two sprites and both are checked.
            # That is `sfxnames.py`'s rule, and the reason every art helper here takes its key
            # first: a widget name in front of it would be read as art.
            # A concatenation is a *fragment* and not a name - `Art.S("Ui/" + icon)` names
            # nothing that exists on its own - so it is unreadable rather than checked. Only a
            # bare literal or a ternary between literals is a name this can hold to disk.
            names = [] if "+" in (first or "") else re.findall(r'"([^"]*)"', first or "")
            if not names:
                out.append((where, None, folder, False))
                continue

            if folder is None:
                # A manifest helper: what it is depends on what the caller does with it, and
                # the caller is whatever wraps this call.
                before = text[max(0, match.start() - 60):match.start()]
                folder = any(before.rstrip().endswith(c + "(") for c in FOLDER_CALLS)

            for body in names:
                out.append((where, prefix + body, folder, True))

    return out


def exists(address, wants_folder):
    """Whether an address under `Art/` names something on disk."""
    target = ART / address

    if wants_folder:
        if not target.is_dir():
            return False
        return any(p.suffix.lower() in PICTURES for p in target.iterdir())

    if target.is_dir():
        return False
    for suffix in PICTURES:
        if target.with_suffix(suffix).exists():
            return True
    return False


def main():
    errors, warnings = [], []

    prefixes, unreadable = helpers()
    for name in unreadable:
        warnings.append(f"AssetManifest.{name} is an art helper this cannot follow, so every "
                        "call site using it goes unchecked")

    requests = asked(prefixes)
    checked = 0
    unreadable = []

    for where, address, wants_folder, readable in requests:
        if not readable:
            unreadable.append(where)
            continue

        checked += 1
        if exists(address, wants_folder):
            continue

        kind = "folder of frames" if wants_folder else "sprite"
        errors.append(f"{where}: Art/{address} is asked for as a {kind} and is not on disk - "
                      "a missing sprite draws as a WHITE RECTANGLE, not as nothing "
                      "(invariant 7b)")

    # ---- and whether anything can actually load them
    live = registered()
    standing, unwrapped = preloaded(prefixes)
    for field in unwrapped:
        warnings.append(f"AssetManifest.{field} is a preloaded list this cannot follow to a "
                        "root, so nothing checks that its art is addressable")

    seen = set()
    for field, name in standing:
        address = ART_ROOT + name
        if address in seen:
            continue
        seen.add(address)

        if not exists(name, False) and not exists(name, True):
            continue          # already reported by the sweep above: not this check's question

        if address not in live:
            errors.append(f"AssetManifest.{field}: {address} is on disk and has no Addressables "
                          "entry, so nothing can load it - a preload throws InvalidKeyException "
                          "at launch. Run `Glimmer Grove > Addressables > Sync All Assets` and "
                          "save (invariant 7a)")

    for e in errors:
        print("ERROR " + e)
    for w in warnings:
        print("WARN  " + w)

    # A name built from a variable is invisible to this and always will be - `Art.S(perch)`,
    # `Art.Frames("Critters/" + kind)`. Counted rather than failed, because most of them are
    # deliberate and a gate that errors on somebody's honest indirection is a gate that gets
    # switched off. What it does say out loud is how much of the surface goes unchecked.
    if unreadable:
        tally = {}
        for where in unreadable:
            tally[where] = tally.get(where, 0) + 1

        print(f"      {len(unreadable)} art name(s) are built rather than written, so nothing "
              f"checks them; the most in one file:")
        for where, n in sorted(tally.items(), key=lambda kv: (-kv[1], kv[0]))[:3]:
            print(f"        {n} in {where}")

    print(f"\n{checked} literal art name(s) checked across "
          f"{len({w for w, _, _, _ in requests})} file(s), {len(prefixes)} manifest helper(s), "
          f"{len(seen)} preloaded address(es) against {len(live)} registered"
          f" - {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
