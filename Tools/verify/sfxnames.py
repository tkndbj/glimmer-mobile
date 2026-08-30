# -*- coding: utf-8 -*-
"""Proves every sound the code asks for exists, and every sound that exists is asked for.

    python Tools/verify/sfxnames.py

Loc keys have had a build gate since the beginning (invariant 6) and asset names have
never had one, which is the gap this closes for audio. `AddressableAudit` proves every
file on disk *is* addressed; nothing proved the reverse - that a name a call site asks
for resolves to something. So `Audio.Sfx("tap")` was a runtime `InvalidKeyException` and
a silence, and it shipped green, twice, in `LeaderboardScreen`.

Four things are checked and each has been wrong in this repository at least once:

* **A name the code plays has a file.**  `"tap"` did not. Silent at author time, silent
  in the Editor, silent in the build.
* **A file is preloaded.**  `AssetManifest.Sfxs` is the list `GlobalAssets` warms. A clip
  missing from it still resolves, but it is fetched synchronously at the moment it is
  first played - which is by definition a moment something is happening.
* **A preloaded name has a file.**  The other direction: an entry naming nothing makes
  the boot path throw.
* **A file is played by somebody.**  `press.wav` sat in the project unreferenced by any
  code and absent from the manifest. Dead weight is cheap here and the same check is what
  catches a clip that lost its last caller in a refactor.

**Only `Presentation` is scanned**, because `Audio` lives there and Domain may never
reference it (invariant 3) - so a literal in Domain is never a sound name, whatever it
is called.

**It reads literals, so the code has to be written in literals** - the same rule
invariant 6 already imposes on loc keys, for the same reason. Two shapes are understood:
a literal inside an `Audio.Sfx`/`Audio.SfxVaried` argument list, which covers the
ternaries (`bed ? "chime" : "lit"`), and a literal bound to something *named* `sfx` -
a field like `ClickSfx`, a default like `string sfx = "tick"`, or a named argument like
`sfx: "coin"`. A name computed at runtime is invisible to this and always will be; the
answer is not to compute one.

This does **not** cover `Art.S`/`Art.Frames` sprite names, which have the same gap and
the same fix. See the asset-names-have-no-build-gate note.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
SCRIPTS = REPO / "Assets" / "Game" / "Scripts"

# Only Presentation is scanned, and that is an invariant rather than an optimisation:
# `Audio` lives in Presentation and Domain may never reference it (invariant 3), so a
# literal in Domain is never a sound name however it is spelled. Scanning it anyway is
# how the first version of this check failed on `const string KeySfx = "gg.sfx"`, a
# PlayerPrefs key in `LegacyPlayerPrefsImport` - a false positive on a file that is
# frozen by invariant 2 and could not have been changed to appease it.
PLAYED_IN = SCRIPTS / "Presentation"
SFX_DIR = REPO / "Assets" / "Game" / "Audio" / "Sfx"
MANIFEST = SCRIPTS / "Domain" / "AssetPipeline" / "AssetManifest.cs"

SINKS = ("Audio.Sfx", "Audio.SfxVaried")

# A literal bound to something named sfx: `ClickSfx = "click"`, `string sfx = "tick"`,
# `sfx: "coin"`. The name must *end* in sfx so `_sfx`, `ClickSfx` and `sfx` all match
# while an unrelated identifier does not.
NAMED = re.compile(r'\b\w*[Ss]fx\s*[:=]\s*"([^"]*)"')

STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')


def strip_comments_and_verbatim(text):
    """Line and block comments out, so a name in prose is not a call site. Verbatim and
    interpolated strings are left alone deliberately - a sound name is never either, so
    one appearing here is worth failing on rather than quietly skipping."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def call_args(text, start):
    """The text between the parentheses of a call whose name ends at `start`.

    Counts nesting and skips over string and char literals so a bracket inside one does
    not close the call early.
    """
    i = text.find("(", start)
    if i < 0:
        return ""
    depth, j = 0, i
    while j < len(text):
        c = text[j]
        if c == '"' or c == "'":
            quote, j = c, j + 1
            while j < len(text):
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == quote:
                    break
                j += 1
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[i + 1:j]
        j += 1
    return text[i + 1:]


def top_level_strings(args):
    """The string literals belonging to *this* call, not to one nested inside it.

    `Audio.Sfx(Loc.Get("ui.x"), .5f)` must not read `ui.x` as a sound name, and a
    ternary - `Audio.Sfx(bed ? "chime" : "lit")` - must still yield both of its arms.
    Depth is what separates the two, so it is counted rather than assumed.
    """
    out, depth, i = [], 0, 0
    while i < len(args):
        c = args[i]
        if c == '"':
            j, buf = i + 1, []
            while j < len(args):
                if args[j] == "\\":
                    buf.append(args[j:j + 2])
                    j += 2
                    continue
                if args[j] == '"':
                    break
                buf.append(args[j])
                j += 1
            if depth == 0:
                out.append("".join(buf))
            i = j + 1
            continue
        if c == "'":
            i += 3 if args[i + 1:i + 2] == "\\" else 2
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        i += 1
    return out


def asked_for():
    """Every sound name the code plays, as {name: [where]}."""
    found = {}

    def note(name, where):
        found.setdefault(name, []).append(where)

    for path in sorted(PLAYED_IN.rglob("*.cs")):
        raw = path.read_text(encoding="utf-8", errors="replace")
        text = strip_comments_and_verbatim(raw)
        rel = path.relative_to(REPO).as_posix()

        for sink in SINKS:
            for m in re.finditer(re.escape(sink) + r"\s*\(", text):
                for name in top_level_strings(call_args(text, m.start())):
                    note(name, f"{rel} ({sink})")

        for m in NAMED.finditer(text):
            note(m.group(1), f"{rel} (named sfx)")

    return found


def addressed():
    """{address: guid} for every Audio/Sfx entry in the global Addressables group.

    Checked here because `AddressableAudit` only runs in the Editor, and a clip that is
    on disk, preloaded and played but *not addressed* fails at the first `AssetLibrary`
    call rather than at build time. It is also the half that goes wrong when somebody
    adds a sound with the Editor closed - the importer hook cannot fire, so the entry is
    simply never written (see the unity-editor-gotchas note).
    """
    group = REPO / "Assets" / "AddressableAssetsData" / "AssetGroups" / "Glimmer Global.asset"
    if not group.exists():
        return None
    text = group.read_text(encoding="utf-8", errors="replace")
    return {a: g for g, a in re.findall(
        r"- m_GUID: ([0-9a-f]{32})\n\s+m_Address: (Audio/Sfx/\S+)", text)}


def guid_of(slot):
    meta = SFX_DIR / f"{slot}.wav.meta"
    if not meta.exists():
        return None
    m = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="replace"))
    return m.group(1) if m else None


def manifest_names():
    text = MANIFEST.read_text(encoding="utf-8")
    m = re.search(r"static readonly string\[\]\s+Sfxs\s*=\s*\{(.*?)\};", text, re.S)
    if not m:
        raise SystemExit("could not find AssetManifest.Sfxs - has it been renamed?")
    return [s.group(1) for s in STRING.finditer(m.group(1))]


def main():
    if not SFX_DIR.is_dir():
        raise SystemExit(f"no sound folder at {SFX_DIR}")

    on_disk = {p.stem for p in SFX_DIR.glob("*.wav")}
    played = asked_for()
    preloaded = manifest_names()

    errors, warnings = [], []

    for name, wheres in sorted(played.items()):
        if name and name not in on_disk:
            errors.append(f"nothing plays: Audio.Sfx(\"{name}\") has no "
                          f"Assets/Game/Audio/Sfx/{name}.wav\n"
                          + "".join(f"      {w}\n" for w in sorted(set(wheres))).rstrip())

    dupes = [n for n in preloaded if preloaded.count(n) > 1]
    for name in sorted(set(dupes)):
        errors.append(f"AssetManifest.Sfxs lists {name!r} more than once")

    for name in preloaded:
        if name not in on_disk:
            errors.append(f"AssetManifest.Sfxs names {name!r}, which has no wav - "
                          f"the boot preload will throw")

    for name in sorted(on_disk):
        if name not in preloaded:
            errors.append(f"{name}.wav is not in AssetManifest.Sfxs, so it is fetched "
                          f"synchronously the first time it is played")

    for name in sorted(on_disk):
        if name not in played:
            warnings.append(f"{name}.wav is never played - dead weight in the preload, "
                            f"or a caller was lost")

    entries = addressed()
    if entries is None:
        warnings.append("no Glimmer Global.asset - skipped the Addressables cross-check")
    else:
        for name in sorted(on_disk):
            guid, addr = guid_of(name), f"Audio/Sfx/{name}"
            if guid is None:
                errors.append(f"{name}.wav has no .meta - run python Tools/sfx_meta.py")
            elif addr not in entries:
                errors.append(f"{name}.wav is not addressed at {addr} - it will throw at the "
                              f"first play. Open the Editor, or add the entry by hand.")
            elif entries[addr] != guid:
                errors.append(f"{addr} is addressed to {entries[addr]} but {name}.wav.meta "
                              f"says {guid} - the entry points at a different asset")
        for addr in sorted(entries):
            if addr.rsplit("/", 1)[-1] not in on_disk:
                errors.append(f"{addr} is addressed but has no wav - a dangling entry")

    for w in warnings:
        print(f"  warning: {w}")
    for e in errors:
        print(f"  error: {e}")

    print(f"\n{len(on_disk)} clips, {len(played)} names played, {len(preloaded)} preloaded"
          f" - {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
