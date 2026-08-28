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
import io
import re
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Library", "GlimmerVerify")

SCRIPTS = os.path.join(ROOT, "Assets", "Game", "Scripts")
SCRIPT_ASMS = os.path.join(ROOT, "Library", "ScriptAssemblies")
PACKAGE_CACHE = os.path.join(ROOT, "Library", "PackageCache")


# Where the Editor keeps the reference assemblies and the Roslyn it ships with. macOS
# buries the whole of Windows' `Editor/Data` under `Unity.app/Contents/Resources/Scripting`
# and drops the .exe suffixes; everything below that root is laid out identically, which is
# what lets one path decide the difference rather than a fork per tool.
UNITY_ROOTS = (
    "C:/Program Files/Unity/Hub/Editor/*/Editor/Data",
    "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting",
)


def unity_data():
    """The newest installed 6000.x editor's Data folder."""
    for pattern in UNITY_ROOTS:
        hits = sorted(glob.glob(pattern))
        if hits:
            return hits[-1]
    sys.exit("no Unity install found under any of:\n  " + "\n  ".join(UNITY_ROOTS))


def exe(name):
    """A bundled executable's file name on this platform."""
    return name + ".exe" if os.name == "nt" else name


DATA = unity_data()
ENGINE = os.path.join(DATA, "Managed", "UnityEngine")
CSC = (sorted(glob.glob(os.path.join(DATA, "DotNetSdk", "sdk", "*", "Roslyn",
                                    "bincore", "csc.dll"))) or [""])[-1]
DOTNET = os.path.join(DATA, "DotNetSdk", exe("dotnet"))
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

# Unity's iOS build-support module. Present only when that module is installed, which is why the
# pass that needs it is skipped rather than failed when it is missing — a Windows machine without
# iOS support is a legitimate way to work on this project.
IOS_XCODE = os.path.join(
    DATA, "PlaybackEngines", "iOSSupport", "UnityEditor.iOS.Extensions.Xcode.dll")

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
    ("authoring", dict(
        out="GlimmerGrove.Authoring",
        src=sources("Assets/Game/Authoring"),
        # Editor-only in the asmdef, so it is absent from every player build. What makes that
        # more than a claim is the entry *above*: `domain` is compiled without this on its
        # reference list, so the day a shipped type calls into an authoring rule, the domain
        # pass fails here rather than the rule quietly rejoining the build.
        refs=ENGINE_EDITOR + PKG_EDITOR + [NETSTANDARD] + SHIMS
             + compiled("GlimmerGrove.Domain"),
        defines=DEFINES + ["UNITY_EDITOR"],
    )),
    ("editor", dict(
        out="GlimmerGrove.Editor",
        src=sources("Assets/Game/Editor"),
        refs=ENGINE_EDITOR + PKG_EDITOR + [NETSTANDARD] + SHIMS
             + compiled("GlimmerGrove.Domain", "GlimmerGrove.Authoring", "GlimmerGrove.Cloud",
                        "GlimmerGrove.Ads", "GlimmerGrove.Privacy", "GlimmerGrove.Presentation"),
        defines=DEFINES + ["UNITY_EDITOR"],
    )),
    ("editor-ios", dict(
        out="GlimmerGrove.Editor.iOS",
        src=sources("Assets/Game/Editor"),
        # The same assembly again with UNITY_IOS defined, and it is not redundant: every iOS
        # build step in this project lives behind `#if UNITY_IOS`, so the ordinary editor pass
        # compiles *none* of it. IosPrivacyPlist writes the tracking usage description and links
        # AppTrackingTransparency.framework, and without this pass the first thing to compile it
        # would be a Mac, twenty minutes into an Xcode build, with the error naming Apple's
        # linker rather than our file.
        refs=ENGINE_EDITOR + PKG_EDITOR + [NETSTANDARD, IOS_XCODE] + SHIMS
             + compiled("GlimmerGrove.Domain", "GlimmerGrove.Authoring", "GlimmerGrove.Cloud",
                        "GlimmerGrove.Ads", "GlimmerGrove.Privacy", "GlimmerGrove.Presentation"),
        defines=DEFINES + ["UNITY_EDITOR", "UNITY_IOS"],
        # Skipped, not failed, when the iOS module is not installed.
        needs=IOS_XCODE,
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
             + compiled("GlimmerGrove.Domain", "GlimmerGrove.Authoring", "GlimmerGrove.Cloud",
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

    needs = spec.get("needs")
    if needs and not os.path.exists(needs):
        print("  %-7s skipped (needs %s)" % (key, os.path.basename(needs)))
        return True

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



# ---------------------------------------------------------------- Unity messages
# Unity's magic methods are a rule of the *engine*, not of the language, so a compiler
# has nothing to say about them. `public bool Awake(int i)` on a MonoBehaviour compiles
# perfectly and the Editor then refuses the script outright:
#
#     Script error (BoardView): Awake() can not take parameters.
#
# That is the one class of build failure this file is otherwise blind to, and it cost a
# round trip through the Editor to find - which is precisely what running the compiler
# offline exists to avoid. So it is checked here rather than in a seventh script nobody
# remembers to run.
#
# Only the messages that genuinely take no arguments are listed. Plenty of others are
# legitimately parameterised - OnApplicationPause(bool), OnCollisionEnter(Collision),
# OnAnimatorIK(int), OnAudioFilterRead(float[], int) - and flagging those would make the
# check noise.
NO_ARG_MESSAGES = {
    "Awake", "Start", "Update", "FixedUpdate", "LateUpdate",
    "OnEnable", "OnDisable", "OnDestroy", "OnGUI", "Reset", "OnValidate",
    "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderObject", "OnWillRenderObject",
    "OnBecameVisible", "OnBecameInvisible", "OnDrawGizmos", "OnDrawGizmosSelected",
    "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseEnter", "OnMouseExit",
    "OnMouseOver", "OnMouseDrag", "OnApplicationQuit", "OnAnimatorMove",
    "OnTransformChildrenChanged", "OnTransformParentChanged", "OnCanvasGroupChanged",
    "OnRectTransformDimensionsChange", "OnDidApplyAnimationProperties",
}

# Unity types that are MonoBehaviours without saying so in this repo's source.
ENGINE_BEHAVIOURS = {"MonoBehaviour", "UIBehaviour", "Graphic", "MaskableGraphic",
                     "Image", "Text", "Selectable", "Button", "ScrollRect", "EditorWindow"}

CLASS_DECL = re.compile(
    r"^\s*(?:public|internal|private|protected|sealed|abstract|static|partial|\s)*"
    r"class\s+(\w+)\s*(?::\s*([^{]+))?", re.M)

METHOD_DECL = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:public|private|protected|internal|virtual|override|sealed|static|extern|async|\s)*"
    r"(?:[\w<>\[\],\.\?]+\s+)?(\w+)\s*\(([^)]*)\)", re.M)


def behaviour_classes(files):
    """Every class in the project that ends up a MonoBehaviour, however deep the chain."""
    bases = {}
    for path in files:
        text = io.open(path, encoding="utf-8", errors="replace").read()
        for name, inherits in CLASS_DECL.findall(text):
            first = (inherits or "").split(",")[0].strip()
            first = re.sub(r"<.*", "", first).strip()
            bases[name] = first

    found = set()
    for name in bases:
        seen, at = set(), name
        while at and at not in seen:
            seen.add(at)
            parent = bases.get(at)
            if parent in ENGINE_BEHAVIOURS:
                found.add(name)
                break
            at = parent
    return found


def check_messages(files):
    """Reports MonoBehaviour methods named after a no-argument Unity message."""
    behaviours = behaviour_classes(files)
    problems = []

    for path in files:
        text = io.open(path, encoding="utf-8", errors="replace").read()

        # Which class each character offset belongs to, by brace depth. Cheap and good
        # enough: this source has no braces inside string literals on a class line.
        owners, stack, depth = [], [], 0
        for m in CLASS_DECL.finditer(text):
            owners.append((m.start(), m.group(1)))

        for m in METHOD_DECL.finditer(text):
            name, args = m.group(1), m.group(2).strip()
            if name not in NO_ARG_MESSAGES or not args:
                continue
            if args.startswith("this "):          # an extension method, not a message
                continue

            owner = None
            for start, cls in owners:
                if start < m.start():
                    owner = cls
            if owner not in behaviours:
                continue

            line = text.count("\n", 0, m.start()) + 1
            problems.append("%s:%d  %s.%s(%s) - Unity refuses a %s that takes parameters"
                            % (path.replace("\\", "/"), line, owner, name, args, name))

    return problems


# ------------------------------------------------------- a level is one of three things
# A LevelDefinition carries a conduit board, a hollow, or a Lab experiment - and exactly one of
# them. The other two are null, nothing in the language says so (nullable reference types are
# off), and a reader that forgets is a NullReferenceException in whichever tool touches it first.
#
# It has now happened twice, and the second time is why this checks all three. The first was
# `level.Layout.Width` in ContentValidation and ContentAuthoring, which blew up the moment a
# boardless level shipped. The second was a `level.Hollow.<field>` read in the same two files,
# guarded by `if (!level.HasBoard)` - a correct test right up until a third kind of level
# existed, at which point "not a board" stopped meaning "a hollow" and the Android build died
# in the validator. (The field it read was the hollow's duskcap count, and neither the field
# nor the mechanic exists any more - invariant 5f. The shape of the mistake is the point, so
# the story is kept and the dead symbol is not, because a name nobody can grep for reads as a
# note somebody forgot to finish.)
#
# The rule is coarse on purpose: a file that reads one of these must somewhere say it knows the
# thing can be absent. A handful of files touch them, so a false positive costs one word and a
# missing guard costs a crash on a path no offline gate runs.
ALTERNATIVES = (
    ("Layout", "HasBoard", re.compile(r"\.Layout\s*\."),
     ("HasBoard", "Layout == null", "Layout != null"),
     "a hollow and a lab level have no board"),
    ("Hollow", "HasHollow", re.compile(r"\.Hollow\s*\."),
     ("HasHollow", "Hollow == null", "Hollow != null"),
     "a glade and a lab level have no hollow"),
    ("Lab", "HasLab", re.compile(r"\.Lab\s*\."),
     ("HasLab", "Lab == null", "Lab != null"),
     "a glade and a hollow have no lab"),
)


def check_layout(files):
    problems = []

    for path in files:
        raw = io.open(path, encoding="utf-8", errors="replace").read()
        text = without_comments(raw)

        for name, has, pattern, guards, why in ALTERNATIVES:
            hit = pattern.search(text)
            if not hit:
                continue
            if any(guard in raw for guard in guards):
                continue

            line = text[:hit.start()].count("\n") + 1
            problems.append("%s:%d  reads .%s without ever checking %s - %s"
                            % (path.replace("\\", "/"), line, name, has, why))

    return problems


# ------------------------------------------------- a serialised DTO field is never null
# JsonUtility instantiates a [Serializable] class field even when the JSON has no such key,
# so `dto.block != null` is true for every file ever parsed. Testing it is not a weak check,
# it is an inverted one: it says "this was authored" about content that never mentioned it.
#
# That shipped. `dto.chain != null` read all forty existing glades as chain fields, dropped
# every one and failed the build with eighty errors - and no offline gate could see it,
# because Python's json returns nothing for a missing key where Unity returns an object.
#
# The rule: a class-typed field of a DTO gets an `IsAuthored`-style test on a value a real
# one cannot hold, never a null test. Arrays are exempt - JsonUtility does leave those null.
DTO_FIELD = re.compile(r"^\s*public\s+(\w+Dto)\s+(\w+)\s*;", re.M)

# Comments are stripped before any of these scan. This codebase documents its traps in prose
# next to the code that avoids them, so the wrong shape appears in a doc comment far more often
# than in a statement - and a checker that cannot tell them apart is one nobody keeps.
COMMENT = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)


def without_comments(text):
    """Blank out comments, keeping line numbers intact."""
    return COMMENT.sub(lambda m: "\n" * m.group(0).count("\n"), text)


def check_dto_nulls(files):
    fields = set()
    for path in files:
        if not path.endswith("ContentDto.cs"):
            continue
        text = without_comments(io.open(path, encoding="utf-8", errors="replace").read())
        for _, name in DTO_FIELD.findall(text):
            fields.add(name)

    if not fields:
        return []

    tests = re.compile(r"\.(" + "|".join(sorted(fields)) + r")\s*(?:!=|==)\s*null")
    problems = []

    for path in files:
        text = without_comments(io.open(path, encoding="utf-8", errors="replace").read())

        for m in tests.finditer(text):
            # A null test paired with a real "was this authored" test is the fixed shape.
            window = text[max(0, m.start() - 120):m.end() + 120]
            if "IsAuthored" in window:
                continue

            line = text[:m.start()].count("\n") + 1
            problems.append("%s:%d  tests '%s' for null, but JsonUtility never leaves a "
                            "serialised class field null" % (path.replace("\\", "/"), line, m.group(1)))

    return problems


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

    if ok:
        every = sorted(set(sum((spec["src"] for _, spec in ASSEMBLIES), [])))
        problems = check_messages(every)
        for line in problems:
            print("  message FAILED  " + line)

        boards = check_layout(every)
        for line in boards:
            print("  board   FAILED  " + line)

        dtos = check_dto_nulls(every)
        for line in dtos:
            print("  dto     FAILED  " + line)

        if problems or boards or dtos:
            ok = False

    print("OK" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
