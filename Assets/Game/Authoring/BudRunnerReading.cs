using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What the vines on a Budburst grove are worth, read off the board as it is dealt.
    ///
    /// <para>
    /// <b>This is invariant 26g's test, and it exists as a type of its own because it is asked
    /// twice.</b> <c>BudValidator</c> asks it to decide whether a grove may ship, and
    /// <c>BudLadderTests</c> asks it of every grove that already has — and a second copy of the
    /// arithmetic would be a second thing to keep in step with <c>Tools/verify/bud.py</c>, which
    /// is the third copy and the one that cannot call either (invariant 9a). The fixture pins
    /// these numbers for the shipped chapter, so this class <em>is</em> the C# side of that
    /// contract.
    /// </para>
    /// <para>
    /// <b>Two mechanics were withdrawn from Lightfall for want of exactly this.</b> A mirror and
    /// then a wick both passed every other gate in this repository — solvable, correctly par'd,
    /// tight <c>ways</c>, every board green — while changing nothing about any board they stood
    /// on, because a decoration passes all of those. The comparison that catches one is a single
    /// line: replace the new object with the nearest existing one and see whether anything
    /// changes. Here the nearest existing thing is no runner at all.
    /// </para>
    /// <para>
    /// <b>Par is deliberately not what is compared</b>, and that is worth stating because it is
    /// the obvious choice and it does not work. A grove is dealt far more taps than its answer
    /// needs and its chains reach most of the board, so par sits on a floor set by how many
    /// critters are shut in and how far apart they are: measured over several thousand swept
    /// boards, cutting every vine moved par on <em>none</em> of them — including groves whose
    /// vine plainly decided how they played. A metric that answers "nothing" for every input is
    /// a broken gate rather than a strict one.
    /// </para>
    /// </summary>
    public readonly struct BudRunnerReading
    {
        /// <summary>
        /// Opening taps that leave a different grove behind with the vines than without them.
        ///
        /// <b>The gate.</b> Nought means the runners are scenery on the board as dealt.
        /// </summary>
        public readonly int Changed;

        /// <summary>
        /// Opening taps that burst <em>more</em> because a vine carried.
        ///
        /// <b>The goal rather than the gate</b>, and the number an author holds out for: a tap
        /// that merely leaves a different grove behind is the vine existing, where a tap that
        /// goes off harder is the arrangement the player made. Not refused on, because a
        /// chapter's first grove may legitimately teach with one that only moves a colour.
        /// </summary>
        public readonly int Caught;

        /// <summary>
        /// How many vines the <em>best</em> opening tap fires, which is what decides whether
        /// anybody will ever watch one.
        /// </summary>
        public readonly int Ran;

        /// <summary>How many opening taps there are at all, which the two above are out of.</summary>
        public readonly int Taps;

        BudRunnerReading(int changed, int caught, int ran, int taps)
        {
            Changed = changed;
            Caught = caught;
            Ran = ran;
            Taps = taps;
        }

        public static readonly BudRunnerReading Nothing = new BudRunnerReading(0, 0, 0, 0);

        /// <summary>
        /// Plays every opening tap of this grove twice — once as authored and once with every
        /// vine cut — and counts what differed.
        ///
        /// <para>
        /// <b>The whole grove is compared, not just the chain's four numbers.</b> A vine that
        /// carries a colour into a flower which does not then go off changes nothing a
        /// <c>BudChainResult</c> can report and everything about the position the player is left
        /// in — which is most of what a runner does, and all of what it does on a teaching
        /// board. Comparing the counts alone reads those taps as identical, which is how the C#
        /// copy and the Python mirror came to disagree by one on the shipped chapter's seventh
        /// grove.
        /// </para>
        /// </summary>
        public static BudRunnerReading Of(BudLayout layout)
        {
            if (layout == null || !layout.HasRunners) return Nothing;

            var strung = new BudBoard(layout);
            var cut = new BudBoard(layout.WithoutRunners());

            int colour = layout.Deal.At(0);
            int changed = 0, caught = 0, taps = 0, ran = 0;

            var best = BudChainResult.Nothing;
            var washes = new System.Collections.Generic.List<BudWash>(64);

            var here = new char[layout.Count + 4];
            var there = new char[layout.Count + 4];

            for (int i = 0; i < layout.Count; i++)
            {
                if (!strung.CanTap(i, colour)) continue;
                taps++;

                var withVines = new BudBoard(strung);
                var without = new BudBoard(cut);

                var a = withVines.Tap(i, colour, null, washes);
                var b = without.Tap(i, colour, null);

                withVines.KeyInto(here, out int hn);
                without.KeyInto(there, out int tn);

                if (a.Burst != b.Burst || a.Waves != b.Waves || a.Freed != b.Freed
                    || a.Cracked != b.Cracked
                    || !Same(here, hn, there, tn)) changed++;

                if (a.Burst > b.Burst) caught++;

                if (a.Waves < best.Waves
                    || (a.Waves == best.Waves && a.Burst <= best.Burst)) continue;

                best = a;
                ran = 0;
                foreach (var wash in washes) if (wash.Ran) ran++;
            }

            return new BudRunnerReading(changed, caught, ran, taps);
        }

        static bool Same(char[] a, int an, char[] b, int bn)
        {
            if (an != bn) return false;
            for (int i = 0; i < an; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
