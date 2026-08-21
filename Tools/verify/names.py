#!/usr/bin/env python3
"""
Prove the keeper-name fold against the shared vectors, on Unity's own runtime, without
opening the Editor.

Why this exists as its own tool
-------------------------------
`GroveNames.Key` and `functions/src/names.ts` must produce the same string from the same name,
because the client folds to read the right reservation document and the server folds to decide
the claim. The two run on different Unicode implementations and they genuinely disagree: three
separate divergences were found by these vectors and closed by hand, and 27 of the BMP's 256
blocks still differ somewhere outside the covered set. Nothing but running both halves against
one file can see any of that.

**It runs on Unity's Mono, and that is the whole point.** The first version of this tool compiled
the fold and ran it on the bundled .NET 8, which has current ICU and therefore agrees with Node
about everything -- so it passed happily with the Cherokee mapping deliberately deleted. A check
that cannot fail is not a check. `MonoBleedingEdge/bin/mono.exe` is the same runtime the Editor
executes this code on, so a divergence reproduces here exactly as it does in a Test Runner run,
and the deleted mapping fails it.

The server half already runs the vectors in `functions/test/grove.mjs`. The client half otherwise
only ran inside the Editor, which is the one place this project cannot rely on: the Editor is
often shut, the MCP bridge dies with it, and a domain reload can wedge it.

What it does not cover: `IsPublishable`'s callers, the panel, or anything with a Unity type in it.
Those are `NameCheckTests` and the Editor suite.

Note what is compiled: `Assets/Game/Scripts/Domain/Social/GroveNames.cs` itself, never a copy.
A proved copy proves nothing (invariant 9a's lesson).

Usage:  python Tools/verify/names.py
"""

import glob
import io
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.join(ROOT, "Assets", "Game", "Scripts", "Domain", "Social", "GroveNames.cs")
VECTORS = os.path.join(ROOT, "firebase", "shared", "grove-vectors.json")


def unity_data():
    """The newest installed 6000.x editor's Data folder. Mirrors compile.py."""
    hits = sorted(glob.glob("C:/Program Files/Unity/Hub/Editor/*/Editor/Data"))
    if not hits:
        sys.exit("no Unity install found under C:/Program Files/Unity/Hub/Editor")
    return hits[-1]


DATA = unity_data()
CSC = os.path.join(DATA, "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore", "csc.dll")
DOTNET = os.path.join(DATA, "DotNetSdk", "dotnet.exe")
MONO_LIB = os.path.join(DATA, "MonoBleedingEdge", "lib", "mono", "4.5")
MONO = os.path.join(DATA, "MonoBleedingEdge", "bin", "mono.exe")


# The harness reads code points rather than strings, for the reason the vector file carries them:
# the bidi and zero-width cases cannot survive a round trip through every JSON reader, and a file
# that disagreed with itself would be worse than either encoding alone.
HARNESS = '''
using System;
using System.IO;
using System.Text;

static class FoldHarness
{
    static string Rebuild(string codes)
    {
        if (codes.Trim().Length == 0) return string.Empty;

        var parts = codes.Split(',');
        var sb = new StringBuilder(parts.Length);

        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0) sb.Append((char)int.Parse(t));
        }

        return sb.ToString();
    }

    static int Main(string[] args)
    {
        int seen = 0, failed = 0;

        foreach (var line in File.ReadAllLines(args[0]))
        {
            var f = line.Split('|');
            if (f.Length != 4) continue;

            string stored = Rebuild(f[0]);
            string wantKey = Rebuild(f[1]);
            string wantPublic = Rebuild(f[2]);
            bool wantClaimable = f[3] == "1";

            seen++;

            string gotKey = GlimmerGrove.Social.GroveNames.Key(stored);
            if (gotKey != wantKey)
            {
                failed++;
                Console.WriteLine("  FAIL key    " + Show(stored) + "  want " + Show(wantKey) + "  got " + Show(gotKey));
            }

            string gotPublic = GlimmerGrove.Social.GroveNames.Public(stored);
            if (gotPublic != wantPublic)
            {
                failed++;
                Console.WriteLine("  FAIL public " + Show(stored) + "  want " + Show(wantPublic) + "  got " + Show(gotPublic));
            }

            // Only checked in the direction a client can see: the word filter is server-only, so
            // a name it refuses is *expected* to look publishable here. What must hold is that
            // anything the server would reserve, this client would offer to save.
            if (wantClaimable && !GlimmerGrove.Social.GroveNames.IsPublishable(stored))
            {
                failed++;
                Console.WriteLine("  FAIL offer  " + Show(stored) + " is reservable but this client refuses it");
            }
        }

        Console.WriteLine(seen + " vector(s), " + failed + " failure(s)");
        return failed == 0 ? 0 : 1;
    }

    static string Show(string s)
    {
        var sb = new StringBuilder();
        sb.Append('"');

        foreach (char c in s)
        {
            if (c >= ' ' && c <= '~') sb.Append(c);
            else sb.Append((char)92).Append((char)117).Append(((int)c).ToString("x4"));
        }

        return sb.Append('"').ToString();
    }
}
'''


def main():
    if not os.path.exists(VECTORS):
        sys.exit("no vector file at " + VECTORS)

    with io.open(VECTORS, encoding="utf-8") as handle:
        cases = json.load(handle)["nameCases"]

    if not cases:
        sys.exit("the vector file carries no name cases")

    work = tempfile.mkdtemp(prefix="glimmer-names-")
    harness = os.path.join(work, "FoldHarness.cs")
    binary = os.path.join(work, "fold.exe")
    table = os.path.join(work, "vectors.txt")

    with io.open(harness, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(HARNESS)

    with io.open(table, "w", encoding="utf-8", newline="\n") as handle:
        for case in cases:
            handle.write("|".join([
                ",".join(str(c) for c in case["storedCodes"]),
                ",".join(str(c) for c in case["keyCodes"]),
                ",".join(str(c) for c in case["publicCodes"]),
                "1" if case.get("claimable") else "0",
            ]) + "\n")

    # Compiled against Mono's own framework assemblies rather than the netstandard reference
    # set, so the binary loads on the runtime that is about to execute it.
    build = subprocess.run(
        [DOTNET, CSC, "-nologo", "-nostdlib", "-noconfig", "-target:exe", "-out:" + binary]
        + ["-r:" + os.path.join(MONO_LIB, dll)
           for dll in ("mscorlib.dll", "System.dll", "System.Core.dll")]
        + [harness, SOURCE],
        capture_output=True, text=True)

    if build.returncode != 0:
        print(build.stdout or build.stderr)
        sys.exit("the fold did not compile")

    run = subprocess.run([MONO, binary, table], capture_output=True, text=True)
    print(run.stdout.strip() or run.stderr.strip())

    if run.returncode == 0:
        print("OK")
        return

    # A harness that could not start is a broken tool, not a broken fold, and saying so is the
    # difference between somebody fixing a runtime and somebody hunting a Unicode bug.
    if "failure(s)" not in run.stdout:
        sys.exit("the harness did not run; the fold was never checked")

    sys.exit(chr(10).join([
        "the client's fold disagrees with the server's.",
        "That is a wrong 'is this taken' hint rather than a duplicate name - the claim is",
        "still adjudicated server-side - but it is invisible from either side alone, which",
        "is what these vectors exist for. See GroveNames.Agree.",
    ]))


if __name__ == "__main__":
    main()
