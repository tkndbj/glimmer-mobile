#!/usr/bin/env python3
"""
Choose the seed a Lightweave level should author, without opening the Editor.

Why this exists
---------------
A weave level authors a shape, a bead count and a seed; the board itself is *generated*. So the
seed is the entire difficulty of the level, and picking one by hand deals a perfectly solvable
board of entirely unknown difficulty — which is how the mode's first chapter came to ship ten
groves that every measurement later called giveaways.

`Glimmer Grove ▸ Content ▸ Survey Lightweave` sweeps seeds inside the Editor and is the right
tool while the Editor is open. Authoring a chapter is mostly done with it closed, and a sweep is
thousands of exponential searches, so this runs the same rule offline and *in parallel across
cores* — which is the difference between minutes and an afternoon.

**The rule is not copied.** `WeaveSeedSearch` is Domain, and this compiles the shipped
`WeaveGenerator.cs`, `WeaveSolver.cs` and `WeaveSeedSearch.cs` and calls it, exactly as
`Tools/verify/weave.py` does for the generator. A sweep that re-implemented "which boards may a
ladder use" would be a second bar for a rung to be authored against and for the suite never to
hold it to (invariant 9a).

Two runtimes, deliberately
--------------------------
By default the sweep runs on the bundled .NET 8, which is fast. `--runtime mono` runs the same
harness on Unity's own Mono — the runtime the Editor and `WeaveLadderTests` execute this code on.
They have disagreed before, over a float in the walk budget, and the disagreement was a
*different board* rather than a rounding error. So the `confirm` mode takes a shortlist of chosen
seeds and measures them on **both**, refusing any that do not agree. Run it on whatever a chapter
ends up authoring before writing the numbers into a test.

What is measured
----------------
`toll` is the reading a ladder climbs: `slack` (the least total detour any arrangement forces on
the pairs, over and above every pair's own floor) plus `bite` (what the hedges add to those
floors). A hedge raises the floors slack is measured against, so slack alone stops being
comparable the moment a chapter grows one, and the sum is what stays comparable — on a grove with
nothing grown the two are the same number. `ways` is how many arrangements land within a couple of
cells of the best one. Toll climbs down a chapter; ways falls.

Usage
-----
    # every usable board a shape deals, least demanding first — what a ladder is picked out of
    python Tools/weave_seeds.py pool --size 8x10 --pairs 6 --beads 5 --seeds 1..20000

    # sweep one shape for a band, and report every seed that lands in it
    python Tools/weave_seeds.py sweep --size 8x10 --pairs 6 --beads 5 \\
        --slack 10 --ways 2..30 --seeds 1..4000

    # the same for a hedged shape, banded on toll rather than on slack
    python Tools/weave_seeds.py sweep --size 8x11 --pairs 6 --beads 6 --hedges 2 \\
        --toll 18 --ways 2..60 --seeds 1..90000

    # probe a shape without a band, to find out what it can even produce
    python Tools/weave_seeds.py survey --size 8x10 --pairs 6 --beads 5 --seeds 1..400

    # measure named seeds on both runtimes and refuse any that disagree
    python Tools/weave_seeds.py confirm --size 8x10 --pairs 6 --beads 5 --seeds 17,204,991

High-toll boards are rare — budget tens of thousands of seeds a rung. And sweep *last*: any
change to `WeaveGenerator` re-deals every hedged board, so a pool swept before a generator fix is
worth nothing.
"""

import argparse
import glob
import io
import json
import os
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DOMAIN = os.path.join(ROOT, "Assets", "Game", "Scripts", "Domain")
AUTHORING = os.path.join(ROOT, "Assets", "Game", "Authoring")

# The shipped rule, never a copy of it. `Energy` comes along because the palette is built from
# the board's own colour arithmetic; then the mode's Domain half in dependency order, and the
# sweep itself out of `GlimmerGrove.Authoring` — Editor-only, so it is in no player build, and
# reachable from here because this compiles by file path rather than by assembly.
SOURCES = [
    os.path.join(DOMAIN, "Board", "Energy.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveHedges.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveLayout.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveGenerator.cs"),
    os.path.join(DOMAIN, "Modes", "Lab", "WeaveSolver.cs"),
    os.path.join(AUTHORING, "WeaveSeedSearch.cs"),
]


def unity_data():
    """The newest installed 6000.x editor's Data folder. Mirrors compile.py and weave.py."""
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


# One line in, one line out, so the work can be split across processes by splitting the input.
# `WeaveSeedSearch.TryMeasure` is the whole of the decision — this prints what it returned and
# says nothing about whether it is any good, because that is the band's job and the band is a
# comparison over integers that does not need a compiler.
HARNESS = r'''
using System;
using System.Globalization;
using System.IO;
using GlimmerGrove.Modes;

static class SeedHarness
{
    static int Main(string[] args)
    {
        var lines = File.ReadAllLines(args[0]);
        var head = lines[0].Split(' ');

        int w = int.Parse(head[0]), h = int.Parse(head[1]);
        int pairs = int.Parse(head[2]), beads = int.Parse(head[3]);
        int hedges = head.Length > 4 ? int.Parse(head[4]) : 0;

        var outp = new StreamWriter(Console.OpenStandardOutput());

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            uint seed = uint.Parse(lines[i], CultureInfo.InvariantCulture);

            WeaveSeedHit hit;
            if (!WeaveSeedSearch.TryMeasure(w, h, pairs, beads, seed, out hit,
                                            WeaveSeedSearch.Cap, WeaveSeedSearch.Budget, hedges))
            {
                outp.WriteLine(seed + " no");
                continue;
            }

            outp.WriteLine(seed + " yes " + hit.Slack + " " + hit.Ways + " " + hit.Par
                           + " " + hit.Reach + " " + hit.Bite + " " + hit.Bitten
                           + " " + hit.Nodes);
        }

        outp.Flush();
        return 0;
    }
}
'''


def newest_net_ref():
    versions = sorted(glob.glob(os.path.join(NET_REF, "*", "ref", "net8.0")))
    if not versions:
        sys.exit("no .NET reference assemblies under " + NET_REF)
    return versions[-1]


def build(work, runtime):
    """Compiles the harness plus the shipped sources for one runtime, returning how to run it."""
    harness = os.path.join(work, "SeedHarness.cs")
    io.open(harness, "w", encoding="utf-8", newline="\n").write(HARNESS)

    if runtime == "net8":
        binary = os.path.join(work, "seeds-core.dll")
        refs = sorted(glob.glob(os.path.join(newest_net_ref(), "*.dll")))
    else:
        binary = os.path.join(work, "seeds-mono.exe")
        refs = [os.path.join(MONO_LIB, dll)
                for dll in ("mscorlib.dll", "System.dll", "System.Core.dll")]

    result = subprocess.run(
        [DOTNET, CSC, "-nologo", "-nostdlib", "-noconfig", "-langversion:9",
         "-optimize+", "-target:exe", "-out:" + binary]
        + ["-r:" + r for r in refs] + [harness] + SOURCES,
        capture_output=True, text=True)

    if result.returncode != 0:
        print(result.stdout or result.stderr)
        sys.exit("the sweep harness did not compile for " + runtime)

    if runtime == "net8":
        io.open(os.path.join(work, "seeds-core.runtimeconfig.json"), "w").write(json.dumps({
            "runtimeOptions": {
                "tfm": "net8.0",
                "framework": {"name": "Microsoft.NETCore.App", "version": "8.0.0"},
                "rollForward": "latestMinor",
            }}, indent=2))
        return [DOTNET, binary]

    return [MONO, binary]


class Hit(object):
    __slots__ = ("seed", "ok", "slack", "ways", "par", "reach", "bite", "bitten", "nodes")

    def __init__(self, line):
        f = line.split()
        self.seed = int(f[0])
        self.ok = f[1] == "yes"
        if self.ok:
            (self.slack, self.ways, self.par, self.reach,
             self.bite, self.bitten, self.nodes) = (int(x) for x in f[2:9])
        else:
            self.slack = self.ways = self.par = self.reach = -1
            self.bite = self.bitten = self.nodes = -1

    def __eq__(self, other):
        return (self.ok, self.slack, self.ways, self.par, self.reach, self.bite,
                self.bitten) == \
               (other.ok, other.slack, other.ways, other.par, other.reach, other.bite,
                other.bitten)

    @property
    def toll(self):
        """Total cells of light this grove forces above the plainest reading of it.

        `slack` is the detour the pairs force on each other; `bite` is the detour the hedges
        force on the pairs. A hedge moves work out of the first and into the floor the first is
        measured against, so neither number alone is comparable between a hedged chapter and an
        open one -- and the sum is. It is what a ladder should climb; see WeaveLadderTests.
        """
        return self.slack + self.bite

    def __str__(self):
        if not self.ok:
            return "seed %-6d refused" % self.seed
        return ("seed %-6d toll %-4d slack %-4d bite %-4d bitten %-3d ways %-5d par %-5d "
                "reach %-4d nodes %d"
                % (self.seed, self.toll, self.slack, self.bite, self.bitten, self.ways, self.par,
                   self.reach, self.nodes))


def measure(run, work, shape, seeds, workers):
    """Every seed measured, in seed order. Split across processes because the search is the cost."""
    width, height, pairs, beads, hedges = shape
    seeds = list(seeds)
    if not seeds:
        return []

    lots = max(1, min(workers, len(seeds)))
    chunks = [seeds[i::lots] for i in range(lots)]

    def one(index):
        table = os.path.join(work, "seeds-%d-%d.txt" % (os.getpid(), index))
        io.open(table, "w", encoding="utf-8", newline="\n").write(
            "%d %d %d %d %d\n" % (width, height, pairs, beads, hedges)
            + "".join("%d\n" % s for s in chunks[index]))

        result = subprocess.run(run + [table], capture_output=True, text=True)
        if result.returncode != 0:
            print(result.stdout or result.stderr)
            sys.exit("the sweep harness did not run")
        return [Hit(l) for l in result.stdout.splitlines() if l.strip()]

    with ThreadPoolExecutor(max_workers=lots) as pool:
        found = [h for part in pool.map(one, range(lots)) for h in part]

    found.sort(key=lambda h: h.seed)
    return found


def shape_of(text):
    try:
        width, height = (int(n) for n in text.lower().split("x"))
    except ValueError:
        raise argparse.ArgumentTypeError("a size is written like 8x10")
    return width, height


def span(text):
    """`1..4000` or `17,204,991` — a range to sweep or a shortlist to confirm."""
    if ".." in text:
        low, high = (int(n) for n in text.split(".."))
        return list(range(low, high + 1))
    return [int(n) for n in text.split(",") if n.strip()]


def band(text):
    low, high = (int(n) for n in text.split(".."))
    return low, high


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mode", choices=("sweep", "survey", "pool", "confirm"))
    parser.add_argument("--size", type=shape_of, required=True, help="e.g. 8x10")
    parser.add_argument("--pairs", type=int, required=True)
    parser.add_argument("--beads", type=int, default=0)
    parser.add_argument("--hedges", type=int, default=0,
                        help="barriers grown between cells before the carve; a board whose "
                             "hedges change no pair's shortest route is refused")
    parser.add_argument("--seeds", type=span, default=span("1..2000"))
    parser.add_argument("--slack", type=int, help="sweep: the exact detour this rung wants")
    parser.add_argument("--toll", type=int,
                        help="sweep: the exact slack+bite this rung wants, which is the reading "
                             "that stays comparable once a chapter grows hedges")
    parser.add_argument("--ways", type=band, default=(1, 500),
                        help="sweep: acceptable near-best arrangement count, e.g. 2..30")
    parser.add_argument("--most", type=int, default=12, help="sweep: stop after this many hits")
    parser.add_argument("--bitten", type=int, default=0,
                        help="sweep/pool: fewest pairs the fence must send a longer way. `bite` "
                             "is a sum and cannot tell one pair detouring ten cells from five "
                             "detouring two; this is the reading that can")
    parser.add_argument("--runtime", choices=("net8", "mono"), default="net8")
    parser.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 4) - 1))
    args = parser.parse_args()

    width, height = args.size
    shape = (width, height, args.pairs, args.beads, args.hedges)
    work = tempfile.mkdtemp(prefix="glimmer-seeds-")

    if args.mode == "confirm":
        # Both runtimes, and a disagreement is the whole reason this mode exists: it means the
        # board a desktop measured is not the board a phone deals.
        runs = {r: measure(build(work, r), work, shape, args.seeds, args.jobs)
                for r in ("net8", "mono")}

        bad = 0
        for net, mono in zip(runs["net8"], runs["mono"]):
            if net != mono:
                bad += 1
                print("  DIVERGES  seed %d" % net.seed)
                print("      .NET 8 " + str(net))
                print("      Mono   " + str(mono))
            elif not net.ok:
                bad += 1
                print("  REFUSED   " + str(net) + "  (inadmissible, undecided, or slack 0)")
            else:
                print("  ok        " + str(net))

        print("\n%d seed(s), %d problem(s)" % (len(args.seeds), bad))
        return 1 if bad else 0

    run = build(work, args.runtime)
    found = measure(run, work, shape, args.seeds, args.jobs)
    usable = [h for h in found if h.ok]

    print("%dx%d  pairs %d  beads %d  hedges %d  on %s — %d of %d seed(s) usable"
          % (width, height, args.pairs, args.beads, args.hedges, args.runtime,
             len(usable), len(found)))

    if args.mode == "pool":
        # Every usable board this shape deals, one per line, cheapest detour first. What a ladder
        # is actually picked out of: a band is a filter over this, and seeing the whole pool is
        # how you find out that a shape simply cannot produce the rung you had in mind.
        for hit in sorted(usable, key=lambda h: (h.toll, h.ways)):
            if hit.bitten >= args.bitten:
                print("  " + str(hit))
        return 0

    if args.mode == "survey":
        # What this shape can even produce, which is the question before a band is chosen. A
        # shape whose slack never climbs past four cannot carry a late rung however it is swept.
        spread = {}
        for h in usable:
            spread.setdefault(h.toll, []).append(h)

        for toll in sorted(spread):
            group = spread[toll]
            ways = sorted(h.ways for h in group)
            pars = sorted(h.par for h in group)
            slacks = sorted(h.slack for h in group)
            bitten = sorted(h.bitten for h in group)
            print("  toll %-4d %-5d seed(s)   slack %d..%d   bitten %d..%d   "
                  "ways %d..%d (median %d)   par %d..%d"
                  % (toll, len(group), slacks[0], slacks[-1], bitten[0], bitten[-1],
                     ways[0], ways[-1], ways[len(ways) // 2], pars[0], pars[-1]))
        return 0

    if args.slack is None and args.toll is None:
        sys.exit("sweep needs --slack or --toll; run `survey` first to see what this shape "
                 "produces")

    low, high = args.ways
    hits = [h for h in usable
            if (args.slack is None or h.slack == args.slack)
            and (args.toll is None or h.toll == args.toll)
            and h.bitten >= args.bitten
            and low <= h.ways <= high][:args.most]

    print("want %s and %d..%d ways:"
          % ("slack %d" % args.slack if args.toll is None else "toll %d" % args.toll,
             low, high))
    for hit in hits:
        print("  " + str(hit))
    if not hits:
        print("  nothing in band — widen it, sweep more seeds, or change the shape.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
