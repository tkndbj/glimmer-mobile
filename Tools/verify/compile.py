#!/usr/bin/env python3
"""
Compile every game assembly with Unity's own Roslyn, without opening the Editor.

Why this exists
---------------
The Editor is usually closed, and the MCP bridge is unavailable precisely when
scripts fail to compile — which is exactly when a compile check is wanted. This
runs the same compiler Unity would, one assembly at a time, in dependency order.

Compiling them *separately* is the point: it is what actually proves the layering
in the asmdefs rather than assuming it. Domain is built with no reference to
UnityEngine.UI and no reference to Presentation, so if it compiles, invariant 3
holds. Nothing here reads the asmdef files; the reference sets below are the
statement of what each assembly is allowed to see, and a violation shows up as a
missing type rather than as a passing build.

Usage
-----
    python Tools/verify/compile.py              # all assemblies
    python Tools/verify/compile.py domain pres  # just those

Exit code is 1 if anything failed to compile.

Notes that cost time to rediscover
----------------------------------
* Paths must be quoted inside the .rsp — "Program Files" has a space in it.
* -nostdlib+ -noconfig, and netstandard.dll as the only framework reference.
  Adding a real MonoBleedingEdge mscorlib gives CS0518 on every primitive.
* The Tests assembly targets net472, so it needs the netfx *shims* alongside
  netstandard, not instead of it.
* Duplicate assembly names across search paths give CS1704, so references are
  deduplicated by file name with the earlier source winning.
* The GLIMMER_* defines come from asmdef versionDefines, so they are on here
  whenever the corresponding package is actually resolved in Library/.
"""

import glob
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Library", "GlimmerVerify")

SCRIPTS = os.path.join(ROOT, "Assets", "Game", "Scripts")
SCRIPT_ASMS = os.path.join(ROOT, "Library", "ScriptAssemblies")
PACKAGE_CACHE = os.path.join(ROOT, "Library", "PackageCache")


def unity_data():
    """The newest installed 6000.x editor's Data folder."""
    hits = sorted(glob.glob("C:/Program Files/Unity/Hub/Editor/*/Editor/Data"))
    if not hits:
        sys.exit("no Unity install found under C:/Program Files/Unity/Hub/Editor")
    return hits[-1]


DATA = unity_data()
ENGINE = os.path.join(DATA, "Managed", "UnityEngine")
CSC = os.path.join(DATA, "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore", "csc.dll")
DOTNET = os.path.join(DATA, "DotNetSdk", "dotnet.exe")
NETSTANDARD = os.path.join(DATA, "NetStandard", "ref", "2.1.0", "netstandard.dll")
NETFX_SHIMS = os.path.join(DATA, "NetStandard", "compat", "2.1.0", "shims", "netfx")


def dlls(folder, keep=None, drop=None):
    found = []
    for path in sorted(glob.glob(os.path.join(folder, "*.dll"))):
        name = os.path.basename(path)
        if drop and any(name.startswith(d) for d in drop):
            continue
        if keep and not any(name.startswith(k) for k in keep):
            continue
        found.append(path)
    return found


def sources(*relative):
    found = []
    for rel in relative:
        for dirpath, _, files in os.walk(os.path.join(ROOT, rel)):
            for f in files:
                if f.endswith(".cs"):
                    found.append(os.path.join(dirpath, f))
    return sorted(found)


def package_plugins():
    """Firebase and friends ship as plugin DLLs inside the resolved package."""
    found = []
    for pattern in ("com.google.*/*/Plugins/*.dll", "com.google.*/Plugins/*.dll"):
        found += glob.glob(os.path.join(PACKAGE_CACHE, pattern))
    return sorted(found)


def compiled(*names):
    """Assemblies this script has already built, by our own output name."""
    return [os.path.join(OUT, n + ".dll") for n in names]


def nunit():
    hits = glob.glob(os.path.join(PACKAGE_CACHE, "com.unity.ext.nunit*", "net*", "unity-custom", "nunit.framework.dll"))
    hits += glob.glob(os.path.join(PACKAGE_CACHE, "com.unity.ext.nunit*", "**", "nunit.framework.dll"), recursive=True)
    return hits[:1]


# Runtime assemblies see the engine but never the editor. Package assemblies come
# from Library/ScriptAssemblies, which is what Unity itself compiled against.
ENGINE_RUNTIME = dlls(ENGINE, drop=["UnityEditor"])

# The per-platform editor extensions are not in Managed/ — UnityEditor.iOS and
# friends live beside their playback engine, and an editor script that touches
# build settings for a platform fails with CS0234 without them. Every installed
# platform is added, because which ones matter is a property of the scripts, not
# of this file.
ENGINE_EDITOR = (ENGINE_RUNTIME
                 + [os.path.join(DATA, "Managed", "UnityEditor.dll")]
                 + sorted(glob.glob(os.path.join(DATA, "PlaybackEngines", "*", "UnityEditor.*.dll"))))

PKG_RUNTIME = dlls(SCRIPT_ASMS, drop=["GlimmerGrove"]) + package_plugins()
PKG_EDITOR = PKG_RUNTIME

DEFINES = ["GLIMMER_ADDRESSABLES", "GLIMMER_HAS_ADDRESSABLES", "GLIMMER_FIREBASE", "GLIMMER_ADS",
           "UNITY_2021_1_OR_NEWER", "UNITY_6000_0_OR_NEWER"]

# The Firebase and LevelPlay plugin DLLs are net472, so anything referencing them
# needs the netfx *shims* alongside netstandard — not a real mscorlib, which gives
# CS0518 on every primitive instead. Unity does the equivalent internally; without
# them the first type that crosses into a plugin signature fails with CS0012.
SHIMS = [os.path.join(NETFX_SHIMS, "mscorlib.dll"), os.path.join(NETFX_SHIMS, "System.dll")]

# Order matters: each entry may reference the outputs of the ones above it.
ASSEMBLIES = [
    ("domain", dict(
        out="GlimmerGrove.Domain",
        src=sources("Assets/Game/Scripts/Domain"),
        # Deliberately no UnityEngine.UI and no Presentation. If this compiles,
        # the Domain/Presentation boundary holds.
        refs=ENGINE_RUNTIME + PKG_RUNTIME + [NETSTANDARD],
    )),
    ("cloud", dict(
        out="GlimmerGrove.Cloud",
        src=sources("Assets/Game/Scripts/Cloud"),
        refs=ENGINE_RUNTIME + PKG_RUNTIME + [NETSTANDARD] + SHIMS + compiled("GlimmerGrove.Domain"),
    )),
    ("ads", dict(
        out="GlimmerGrove.Ads",
        src=sources("Assets/Game/Scripts/Ads"),
        refs=ENGINE_RUNTIME + PKG_RUNTIME + [NETSTANDARD] + SHIMS + compiled("GlimmerGrove.Domain"),
    )),
    ("privacy", dict(
        out="GlimmerGrove.Privacy",
        src=sources("Assets/Game/Scripts/Privacy"),
        # No GLIMMER_UMP here, for the reason the iap entry gives about GLIMMER_IAP: the
        # Google Mobile Ads package has no DLL on disk until the Editor resolves it, so
        # this proves the assembly is sound *without* the CMP - which is the property that
        # keeps a fresh clone compiling. UmpConsentGateway is compiled by the Editor.
        refs=ENGINE_RUNTIME + PKG_RUNTIME + [NETSTANDARD] + SHIMS + compiled("GlimmerGrove.Domain"),
    )),
    ("iap", dict(
        out="GlimmerGrove.Iap",
        src=sources("Assets/Game/Scripts/Store"),
        # No GLIMMER_IAP here, deliberately: Unity IAP is a UPM package with no DLL on
        # disk until the Editor resolves it, so this proves only that the assembly is
        # empty without it — which is the property that matters, since it is what keeps
        # a clone of this repository compiling before anybody installs the SDK. The
        # contents are compiled by the Editor, and by nothing else.
        refs=ENGINE_RUNTIME + PKG_RUNTIME + [NETSTANDARD] + SHIMS + compiled("GlimmerGrove.Domain"),
    )),
    ("pres", dict(
        out="GlimmerGrove.Presentation",
        src=sources("Assets/Game/Scripts/Presentation"),
        refs=ENGINE_RUNTIME + PKG_RUNTIME + [NETSTANDARD] + SHIMS
             + compiled("GlimmerGrove.Domain", "GlimmerGrove.Cloud", "GlimmerGrove.Ads",
                        "GlimmerGrove.Privacy"),
    )),
    ("editor", dict(
        out="GlimmerGrove.Editor",
        src=sources("Assets/Game/Editor"),
        refs=ENGINE_EDITOR + PKG_EDITOR + [NETSTANDARD] + SHIMS
             + compiled("GlimmerGrove.Domain", "GlimmerGrove.Cloud", "GlimmerGrove.Ads",
                        "GlimmerGrove.Privacy", "GlimmerGrove.Presentation"),
        defines=DEFINES + ["UNITY_EDITOR"],
    )),
    ("tests", dict(
        out="GlimmerGrove.Tests",
        src=sources("Assets/Game/Tests"),
        # net472, so the netfx shims go alongside netstandard rather than replacing it.
        # Presentation is referenced for its *pure* logic — TweenCycle is the first, and
        # the reason is that a rule nothing can run is a rule nothing checks. A test that
        # needs a GameObject still belongs in Test Runner and the runner will say so.
        refs=ENGINE_EDITOR + PKG_EDITOR + [NETSTANDARD] + SHIMS
             + nunit()
             + compiled("GlimmerGrove.Domain", "GlimmerGrove.Cloud",
                        "GlimmerGrove.Ads", "GlimmerGrove.Privacy",
                        "GlimmerGrove.Presentation"),
        defines=DEFINES + ["UNITY_EDITOR", "UNITY_INCLUDE_TESTS"],
    )),
]


def build(key, spec):
    if not spec["src"]:
        print("  %-7s no sources, skipped" % key)
        return True

    os.makedirs(OUT, exist_ok=True)
    out_dll = os.path.join(OUT, spec["out"] + ".dll")

    seen = set()
    refs = []
    for r in spec["refs"]:
        name = os.path.basename(r).lower()
        if name in seen or not os.path.exists(r):
            continue
        seen.add(name)
        refs.append(r)

    lines = ["-nostdlib+", "-noconfig", "-langversion:9", "-target:library", "-nowarn:CS0169,CS0414,CS0649",
             '-out:"%s"' % out_dll.replace("\\", "/")]
    lines += ["-define:" + d for d in spec.get("defines", DEFINES)]
    lines += ['-r:"%s"' % r.replace("\\", "/") for r in refs]
    lines += ['"%s"' % s.replace("\\", "/") for s in spec["src"]]

    rsp = os.path.join(OUT, key + ".rsp")
    with open(rsp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")

    result = subprocess.run([DOTNET, CSC, "@" + rsp], capture_output=True, text=True)
    errors = [l for l in (result.stdout + result.stderr).splitlines() if ": error " in l]

    if errors:
        print("  %-7s FAILED  (%d source files, %d errors)" % (key, len(spec["src"]), len(errors)))
        for line in errors[:40]:
            print("      " + line.strip())
        if len(errors) > 40:
            print("      ... and %d more" % (len(errors) - 40))
        return False

    print("  %-7s ok      (%d source files)" % (key, len(spec["src"])))
    return True


def main():
    wanted = [a.lower() for a in sys.argv[1:]]
    print("Unity: %s" % DATA)

    ok = True
    for key, spec in ASSEMBLIES:
        if wanted and key not in wanted:
            continue
        if not build(key, spec):
            ok = False
            # Later assemblies reference this one's output, so their errors would
            # be noise rather than news.
            break

    print("OK" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
