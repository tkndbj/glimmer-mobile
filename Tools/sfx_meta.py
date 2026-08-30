# -*- coding: utf-8 -*-
"""Writes one importer setting for every sound effect, preserving its GUID.

    python Tools/sfx_meta.py            # write them
    python Tools/sfx_meta.py --check    # prove the shipped .meta files are these

**Why this exists at all.** The twenty `.meta` files had drifted into two different
shapes - nine at `serializedVersion: 7` with ADPCM, eleven at `8` with Vorbis, one with
`3D: 1` - because each was written by whichever Editor version happened to import it.
None of that is visible in the game and all of it is a setting somebody chose by
accident. A clip's import settings decide its memory cost and its fidelity, so they are
a decision, and a decision belongs in one place.

**An existing GUID is never touched, and that is the whole constraint.** Addressables keys
an entry on the GUID, not the path, so a regenerated `.meta` with a fresh GUID silently
unaddresses every sound in the game (invariant 7a) - the audit would catch it, but only
after a build gate has failed. So this rewrites the `AudioImporter` block of an existing
file and leaves its `guid:` line exactly where it found it.

**A clip with no `.meta` at all gets one minted**, derived from the slot name rather than
random. Letting Unity generate it is the obvious alternative and it costs a round trip:
a new sound could not be finished with the Editor closed, and its Addressables entry could
not be written until somebody had opened the project once. A derived GUID is reproducible
on a fresh clone, so the meta and the group asset agree without anybody opening Unity, and
Unity adopts an existing `.meta` rather than replacing it. It refuses to mint one that is
already claimed anywhere under `Assets/` - a duplicate GUID is a corruption Unity resolves
by silently reassigning one of the two.

**`normalize: 0` is the one that matters.** Unity's importer peak-normalises when it
downmixes, and `make_sfx.py`'s whole point is that the twenty clips arrive at one
*perceived* loudness so the volumes authored at 115 call sites mean what they say. A peak
normalise on import would undo that silently and leave nothing to notice - the game would
simply be mixed wrong. It is inert while `forceToMono` is 0, which it is, and it is set
explicitly anyway so that turning `forceToMono` on later cannot quietly destroy the match.

**PCM rather than Vorbis, deliberately.** Twenty clips, 9.5 seconds between them: 840 KB
decompressed, which is what they cost in memory under `DecompressOnLoad` whatever the
format on disk. So the only thing Vorbis buys is about 480 KB of APK, and what it costs is
lossy artefacts on the sharp attack of a sound the player hears thousands of times - `tick`
alone runs nine to the second. That is the wrong side of that trade in a game whose
backdrops are 2048 px.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SFX = REPO / "Assets" / "Game" / "Audio" / "Sfx"

# Verified against the Editor's own assembly rather than remembered:
# AudioCompressionFormat 0=PCM 1=Vorbis 2=ADPCM; AudioClipLoadType 0=DecompressOnLoad.
BLOCK = """AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)


def rendered(guid):
    return f"fileFormatVersion: 2\nguid: {guid}\n" + BLOCK.format()


def minted(slot):
    """A GUID for a clip that does not have one yet.

    Unity would generate one on import, and letting it is the obvious thing - but it
    means a new sound cannot be finished with the Editor closed, and the addressable
    entry cannot be written until somebody has opened Unity once. Deriving it from the
    slot name instead makes the whole step offline and reproducible: re-running this on
    a fresh clone produces the same GUID, so the group asset and the meta agree without
    a round trip.

    Unity adopts an existing `.meta` rather than replacing it, so this only ever applies
    to a genuinely new file. `main()` refuses to mint one that already exists anywhere
    in the project, because a duplicate GUID is a corruption Unity resolves by silently
    reassigning one of the two.
    """
    return hashlib.md5(f"glimmergrove.audio.sfx.{slot}".encode()).hexdigest()


def guids_in_project(repo):
    """Every GUID already claimed, so a minted one cannot collide with it."""
    seen = set()
    for meta in (repo / "Assets").rglob("*.meta"):
        m = GUID.search(meta.read_text(encoding="utf-8", errors="replace"))
        if m:
            seen.add(m.group(1))
    return seen


ENTRY = re.compile(r"  - m_GUID: ([0-9a-f]{32})\n")
TAIL = "FlaggedDuringContentUpdateRestriction: 0\n"


def register(slot, guid):
    """Give a newly minted clip its Addressables entry.

    `AddressableAutoRegister` does this in the Editor as the file imports, and that is
    still the ordinary path - but it cannot run with the Editor closed, which is exactly
    when a new sound gets added. Leaving it for later means the clip is on disk, in the
    preload list and played by the code, and *throws at the first play*: the one failure
    `Tools/verify/sfxnames.py` was extended to catch, having caught it twice.

    Entries are stored in GUID order, so the new one is spliced into place rather than
    appended - otherwise the next thing the Editor writes reorders the whole file and the
    diff becomes unreadable.

    Returns True if it wrote one, False if the address was already there.
    """
    group = REPO / "Assets" / "AddressableAssetsData" / "AssetGroups" / "Glimmer Global.asset"
    if not group.exists():
        return False
    s = group.read_text(encoding="utf-8")
    if guid in s:
        return False

    block = (f"  - m_GUID: {guid}\n"
             f"    m_Address: Audio/Sfx/{slot}\n"
             f"    m_ReadOnly: 0\n"
             f"    m_SerializedLabels: []\n"
             f"    {TAIL}")
    before = [g for g in ENTRY.findall(s) if g < guid]
    if before:
        i = s.index(f"  - m_GUID: {max(before)}\n")
        j = s.index(TAIL, i) + len(TAIL)
        s = s[:j] + block + s[j:]
    else:
        i = s.index("  - m_GUID: ")
        s = s[:i] + block + s[i:]

    group.write_text(s, encoding="utf-8")
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    clips = sorted(SFX.glob("*.wav"))
    if not clips:
        raise SystemExit(f"no clips under {SFX}")

    bad, wrote, minted_now, addressable = [], 0, [], []
    claimed = None

    for clip in clips:
        meta = SFX / (clip.name + ".meta")
        if meta.exists():
            text = meta.read_text(encoding="utf-8")
            m = GUID.search(text)
            if not m:
                bad.append(f"{meta.name}: no guid line - refusing to touch it")
                continue
            guid = m.group(1)
        else:
            guid = minted(clip.stem)
            if claimed is None:
                claimed = guids_in_project(REPO)
            if guid in claimed:
                bad.append(f"{clip.name}: minted guid {guid} is already in use - rename the slot")
                continue
            if args.check:
                bad.append(f"{clip.name}: no .meta yet (run without --check to mint one)")
                continue
            text = ""
            minted_now.append((clip.stem, guid))

        addressable.append((clip.stem, guid))

        want = rendered(guid)
        if args.check:
            if text.replace("\r\n", "\n") != want:
                bad.append(f"{meta.name}: differs from Tools/sfx_meta.py")
        elif text.replace("\r\n", "\n") != want:
            meta.write_text(want, encoding="utf-8", newline="\n")
            wrote += 1

    for slot, guid in minted_now:
        print(f"  minted a GUID for a new clip: {slot} -> {guid}")

    # Registration is checked for every clip rather than only the ones just minted.
    # A clip can lose its entry without losing its meta - a bad merge on the group
    # asset, or a mint that happened in an earlier run whose registration never
    # got as far as being written - and the symptom either way is a throw at the
    # first play rather than anything at author time.
    if not args.check:
        for slot, guid in addressable:
            if register(slot, guid):
                print(f"  addressed {slot} at Audio/Sfx/{slot}")

    if bad:
        for b in bad:
            print("  " + b)
        return 1
    print(f"ok - {len(clips)} importer settings"
          + (" match" if args.check else f", {wrote} written"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
