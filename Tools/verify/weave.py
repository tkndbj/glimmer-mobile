#!/usr/bin/env python3
"""
Prove that a Lightweave grove is the same board on every runtime, without opening the Editor.

Why this exists as its own tool
-------------------------------
A weave level authors a size, a pair count and a seed; the board itself is *generated*, at
authoring time on a desktop and again on the player's phone. So the whole mode rests on one
property that nothing else checks: `WeaveGenerator.Build` must deal the same grove everywhere.
If it does not, the board `Validate Content` proved solvable and `Survey Lightweave` measured for
difficulty is simply not the board anybody plays, and no amount of validation on one machine can
notice.

It did not hold. The walk budget was `(int)(free / (float)walksLeft * 1.3f)`, and 1.3 has no
exact binary form -- thirty free cells across three walks computes 12.99999952..., which
truncates to 13 in single precision and to 12 once promoted to double. Both are legal for a C#
compiler and the runtimes disagreed: Unity's Mono answered 12 and .NET 8 answered 13, so the
opening grove of the Weftwood was two different boards depending on who was asking. It was found
by `WeaveLadderTests` passing in the Editor and failing offline, which is the only reason anybody
saw it at all.

**So this runs the generator on both runtimes and diffs them**, rather than checking either
against a number. There is no expected table to go stale, and the property it tests is exactly
the one that matters. `MonoBleedingEdge/bin/mono.exe` is the same runtime the Editor executes
this code on, and the bundled .NET 8 is what `Tools/verify/tests.py` uses -- so a divergence
between the offline suite and the Editor suite reproduces here and is named, instead of looking
like a flaky test.

Note what is compiled: the shipped `WeaveGenerator.cs` and `WeaveSolver.cs`, never a copy. A
proved copy proves nothing (invariant 9a's lesson). What it does not cover: anything with a Unity
type in it, and the ladder's *difficulty*, which is `WeaveLadderTests` and `Survey Lightweave`.

It also reports `slack` -- the least total detour any arrangement of a board has, over and above
every pair's own shortest possible route. Zero means every pair can go as directly as it possibly
could, all at once, so the grove is joined by drawing the obvious line at each critter in turn and
asks the player nothing. All ten groves read zero when the mode first shipped, and it came back
from play as "each critter is literally next to their matching light". Diffed like everything else
here rather than checked against a number: it is a whole search over integer arithmetic, so it is
exactly the sort of thing that could come out differently on two runtimes and be believed.

Usage:  python Tools/verify/weave.py
"""

import glob
import io
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONTENT = os.path.join(ROOT, "Assets", "StreamingAssets", "Content")
CHAPTERS = os.path.join(CONTENT, "chapters")

DOMAIN = os.path.join(ROOT, "Assets", "Game", "Scripts", "Domain")
# The least that compiles: the board, how it is dealt and how it is measured. Deliberately not
# the run's economy — `WeaveInk`, `WeaveStrokes`, `WeaveVerdict` and `WeaveRun` are about paying
# for a grove rather than about what grove was dealt, nothing here calls them, and `WeaveVerdict`
# reaches `RunContinue`, which reaches the save, the wallet, the catalog and analytics. Compiling
# half of Domain to prove a generator deterministic is how a check like this stops being run.
SOURCES = [
    os.path.join(DOMAIN, "Board", "Energy.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveHedges.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveLayout.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveGenerator.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveBoard.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveSolver.cs"),
]


def unity_data():
    """The newest installed 6000.x editor's Data folder. Mirrors compile.py."""
    hits = sorted(glob.glob("C:/Program Files/Unity/Hub/Editor/*/Editor/Data"))
    if not hits:
        sys.exit("no Unity install found under C:/Program Files/Unity/Hub/Editor")
    return hits[-1]


DATA = unity_data()
CSC = os.path.join(DATA, "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore", "csc.dll")
DOTNET = os.path.join(DATA, "DotNetSdk", "dotnet.exe")
NET_REF = os.path.join(DATA, "DotNetSdk", "packs", "Microsoft.NETCore.App.Ref")
MONO_LIB = os.path.join(DATA, "MonoBleedingEdge", "lib", "mono", "4.5")
MONO = os.path.join(DATA, "MonoBleedingEdge", "bin", "mono.exe")


# Prints one line per grove: everything about the board that a player could tell apart. The
# endpoints and the path lengths are what the puzzle *is*; the count of fillings is what its
# difficulty is. Two runtimes agreeing on all of it is the property being proved.
HARNESS = '''
using System;
using System.IO;
using System.Text;
using GlimmerGrove.Modes;

static class WeaveHarness
{
    static int Main(string[] args)
    {
        foreach (var line in File.ReadAllLines(args[0]))
        {
            var f = line.Split(' ');
            if (f.Length != 7) continue;

            string id = f[0];
            int w = int.Parse(f[1]), h = int.Parse(f[2]), pairs = int.Parse(f[3]);
            int beads = int.Parse(f[4]), hedges = int.Parse(f[5]);
            uint seed = uint.Parse(f[6]);

            var grove = WeaveGenerator.Build(w, h, pairs, seed, beads, hedges);

            var sb = new StringBuilder();
            sb.Append(id).Append(' ').Append(grove.Width).Append('x').Append(grove.Height);
            sb.Append(" par=").Append(grove.Par);
            sb.Append(" full=").Append(grove.IsComplete ? 1 : 0);
            sb.Append(" beads=").Append(grove.Beads.Count);

            for (int p = 0; p < grove.Pairs.Count; p++)
            {
                sb.Append(" [").Append(grove.Pairs[p].Heart).Append('>')
                  .Append(grove.Pairs[p].Critter).Append(':')
                  .Append(grove.Straight(p)).Append(']');
            }

            // Every bead, in the order the generator placed them. Where a bead lands decides
            // the board as much as where a crystal does, so it is diffed too.
            foreach (var bead in grove.Beads)
                sb.Append(" {").Append(bead.Cell).Append('@').Append(bead.Pair).Append('}');

            // And every hedge, for the same reason and one stronger. A hedge is grown *before*
            // the carve, so a runtime that placed one differently would not merely draw a
            // different barrier -- every walk after it starts from different ground and the whole
            // grove is re-dealt. It is the first thing rolled, so it is the first thing to diff.
            sb.Append(" hedges=").Append(grove.Hedges.Count);
            foreach (var hedge in grove.Hedges)
                sb.Append(" <").Append(hedge.Cell).Append(hedge.Upright ? '|' : '_')
                  .Append(hedge.Length).Append('>');

            // What the hedges are actually costing: the floor over the ways that are open,
            // against the floor an unhedged grove of the same pairs would have. Equal means the
            // barriers change nobody's shortest route, which is invariant 5d's decoration.
            sb.Append(" bite=").Append(grove.StraightTotal - grove.UnhedgedTotal);

            // And how many channels that cost falls on, which the sum above cannot say. One pair
            // walking ten cells further and five walking two each are the same bite and opposite
            // boards -- a longer line for somebody, against a gap everybody wants. See
            // WeaveLayout.PairsBitten.
            sb.Append(" bitten=").Append(grove.PairsBitten);

            var run = new WeaveBoard(grove);
            sb.Append(" solvable=").Append(run.DrawSolution() && run.IsSolved ? 1 : 0);

            var tally = WeaveSolver.Measure(grove, 500, 2000000);
            sb.Append(" slack=").Append(tally.Solved ? tally.Slack.ToString() : "?");
            sb.Append(" ways=").Append(tally.Ways).Append(tally.Exhausted ? "" : "+");

            Console.WriteLine(sb.ToString());
        }
        return 0;
    }
}
'''


def newest_net_ref():
    versions = sorted(glob.glob(os.path.join(NET_REF, "*", "ref", "net8.0")))
    if not versions:
        sys.exit("no .NET reference assemblies under " + NET_REF)
    return versions[-1]


def build(binary, harness, refs, extra):
    result = subprocess.run(
        [DOTNET, CSC, "-nologo", "-nostdlib", "-noconfig", "-langversion:9",
         "-target:exe", "-out:" + binary]
        + ["-r:" + r for r in refs] + extra + [harness] + SOURCES,
        capture_output=True, text=True)

    if result.returncode != 0:
        print(result.stdout or result.stderr)
        sys.exit("the harness did not compile for " + os.path.basename(binary))


def groves():
    """Every weave level the shipped content authors, in chapter order."""
    found = []
    for path in sorted(glob.glob(os.path.join(CHAPTERS, "*.json"))):
        with io.open(path, encoding="utf-8") as handle:
            body = json.load(handle)

        for level in body.get("levels", []):
            block = level.get("weave")
            if not block:
                continue

            found.append((level.get("id", "?"),
                          block.get("width", 7), block.get("height", 9),
                          block.get("pairs", 4), block.get("beads", 0),
                          block.get("hedges", 0), block.get("seed", 0)))
    return found


def main():
    levels = groves()
    if not levels:
        sys.exit("no weave levels in the shipped content")

    # A level that authors no seed derives one from its id, which this harness cannot reproduce
    # without the whole content layer. Authoring the seed is what Survey Lightweave is for, and
    # a rung without one has an unmeasured difficulty anyway, so it is refused rather than
    # skipped quietly.
    unseeded = [lid for lid, _, _, _, _, _, seed in levels if seed <= 0]
    if unseeded:
        sys.exit("these weave levels author no seed, so their board is whatever their id hashes "
                 "to: " + ", ".join(unseeded))

    work = tempfile.mkdtemp(prefix="glimmer-weave-")
    harness = os.path.join(work, "WeaveHarness.cs")
    table = os.path.join(work, "groves.txt")

    io.open(harness, "w", encoding="utf-8", newline="\n").write(HARNESS)
    io.open(table, "w", encoding="utf-8", newline="\n").write(
        "".join("%s %d %d %d %d %d %d\n" % row for row in levels))

    core = os.path.join(work, "weave-core.dll")
    build(core, harness, sorted(glob.glob(os.path.join(newest_net_ref(), "*.dll"))), [])
    io.open(os.path.join(work, "weave-core.runtimeconfig.json"), "w").write(json.dumps({
        "runtimeOptions": {
            "tfm": "net8.0",
            "framework": {"name": "Microsoft.NETCore.App", "version": "8.0.0"},
            "rollForward": "latestMinor",
        }}, indent=2))

    mono = os.path.join(work, "weave-mono.exe")
    build(mono, harness,
          [os.path.join(MONO_LIB, dll) for dll in
           ("mscorlib.dll", "System.dll", "System.Core.dll")],
          [])

    runs = {}
    for name, command in (("net8", [DOTNET, core, table]), ("mono", [MONO, mono, table])):
        result = subprocess.run(command, capture_output=True, text=True)
        if result.returncode != 0:
            print(result.stdout or result.stderr)
            sys.exit("the harness did not run on " + name + "; nothing was checked")
        runs[name] = [l.rstrip() for l in result.stdout.strip().splitlines() if l.strip()]

    if len(runs["net8"]) != len(levels) or len(runs["mono"]) != len(levels):
        sys.exit("the harness reported %d/%d groves; expected %d"
                 % (len(runs["net8"]), len(runs["mono"]), len(levels)))

    problems = diverged = 0
    for net, mon in zip(runs["net8"], runs["mono"]):
        name = net.split(" ")[0]

        if net != mon:
            problems += 1
            diverged += 1
            print("  DIVERGES  " + name)
            print("      .NET 8 " + net)
            print("      Mono   " + mon)
            continue

        if " full=0" in net:
            problems += 1
            print("  SPARSE    " + name + " has a carve that leaves ground untouched, so its "
                  "endpoints are not spread across the grove")
        if " solvable=0" in net:
            problems += 1
            print("  BROKEN    " + name + " cannot be solved by its own solution")
        if " slack=0" in net:
            problems += 1
            print("  GIVEAWAY  " + name + " lets every crystal reach its critter by the shortest "
                  "route there is, all at once, so the grove asks the player nothing")
        if " slack=?" in net:
            problems += 1
            print("  UNKNOWN   " + name + " could not be measured inside the budget, so its "
                  "difficulty is unknown rather than high")
        if " hedges=0 " not in net + " " and " bite=0" in net:
            problems += 1
            print("  SCENERY   " + name + " grows hedges that change no pair's shortest route, "
                  "so the player draws the line they were going to draw anyway")
        elif " hedges=0 " not in net + " " and " bitten=1 " in net + " ":
            problems += 1
            print("  ONE PAIR  " + name + " grows hedges that reach exactly one of its channels, "
                  "so five of six are drawn as though the fence were not there -- a barrier is "
                  "worth what its gap is worth, and nobody is queueing at this one")

        print("  ok        " + net)

    print()
    print("%d grove(s) checked on .NET 8 and on Unity's Mono, %d problem(s)"
          % (len(levels), problems))

    if diverged:
        sys.exit("a grove is not the same board on both runtimes, so the board that was proved "
                 "is not the board that ships")
    if problems:
        sys.exit("every grove is the same board on both runtimes, but %d of them is not the "
                 "board it should be -- see the lines above" % problems)
    print("OK")


if __name__ == "__main__":
    main()
