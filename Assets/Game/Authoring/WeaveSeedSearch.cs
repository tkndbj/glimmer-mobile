using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>One seed a sweep accepted, and everything that decided it.</summary>
    public readonly struct WeaveSeedHit
    {
        public readonly uint Seed;

        /// <summary>The least total detour any arrangement of this board has — its difficulty.</summary>
        public readonly int Slack;

        /// <summary>How many arrangements land within <see cref="WeaveSolver.Latitude"/> of the best.</summary>
        public readonly int Ways;

        /// <summary>What the board would be graded against — <see cref="WeaveLayout.Par"/>.</summary>
        public readonly int Par;

        /// <summary>How far apart the closest pair's two ends are. The placement bar's reading.</summary>
        public readonly int Reach;

        /// <summary>Positions the measurement examined. Reported so a scarce band says so.</summary>
        public readonly int Nodes;

        public WeaveSeedHit(uint seed, int slack, int ways, int par, int reach, int nodes)
        {
            Seed = seed;
            Slack = slack;
            Ways = ways;
            Par = par;
            Reach = reach;
            Nodes = nodes;
        }

        public override string ToString()
            => "seed " + Seed + " slack " + Slack + " ways " + Ways + " par " + Par
             + " reach " + Reach + " nodes " + Nodes;
    }

    /// <summary>
    /// Which seed a Lightweave level should author, decided by measurement rather than by taste.
    ///
    /// <para>
    /// <b>Why this is its own assembly rather than the Editor tool that calls it.</b> A weave board is
    /// generated, so a level's difficulty is entirely a property of the number in its
    /// <c>seed</c> field — and that number is chosen by sweeping seeds and keeping the ones whose
    /// board lands in the band a rung wants. That rule had exactly one caller while there was one
    /// chapter and one way to run it. A second chapter is authored offline, where the Editor is
    /// closed and the only thing that can run the shipped generator is a harness compiled against
    /// these very files, so the rule now has two. Two <em>copies</em> of "which boards may a
    /// ladder use" would be two chances to author a rung against a bar the suite does not hold it
    /// to — invariant 9a's lesson, for the mode whose entire difficulty is a search. It sits in
    /// <c>GlimmerGrove.Authoring</c>, which is Editor-only: nothing a player runs sweeps seeds,
    /// so nothing a player installs should carry the sweep.
    /// </para>
    /// <para>
    /// <b>Everything here refuses rather than reports.</b> <c>WeaveSurvey</c> is happy to print
    /// "undecided" beside a shipped level, because a reading nobody can take is still worth
    /// seeing. A sweep must not: a capped search has looked at some arrangements and not others,
    /// so its slack is an upper bound, and believing it would call a board <em>harder</em> than
    /// it is — the one direction an authoring bar must never be wrong in. The same goes for a
    /// board given fewer beads than its rung asked for, and for a carve that leaves ground
    /// untouched.
    /// </para>
    /// </summary>
    public static class WeaveSeedSearch
    {
        /// <summary>
        /// How far the counting goes, and how hard the search tries. One pair of numbers on
        /// purpose.
        ///
        /// <para>
        /// A sweep is what <em>chooses</em> a board and <c>WeaveLadderTests</c> is what
        /// <em>pins</em> it, so a board this can decide and the suite cannot is a rung that fails
        /// the moment it is authored. They were 600,000 and two million once, which is exactly
        /// how a finale was picked that the suite then could not count.
        /// </para>
        /// <para>
        /// Spelled out rather than read from <see cref="WeaveSolver"/>'s own defaults, for the
        /// reason the suite spells them out too: these are a statement about what a <em>ladder</em>
        /// is willing to decide, and a retune of the solver's defaults must not silently move the
        /// bar a whole chapter was authored against without anything failing.
        /// </para>
        /// </summary>
        public const int Cap = 500;

        /// <inheritdoc cref="Cap"/>
        public const int Budget = 2_000_000;

        /// <summary>
        /// Whether a dealt grove is one a ladder may use at all, before any band is considered.
        ///
        /// <para>
        /// Two refusals, and both are boards that would look perfectly authored in the JSON. A
        /// carve that leaves ground untouched has not spread its endpoints across the grove, so
        /// its channels never have to get past each other. A grove short of the beads its rung
        /// asked for has a difficulty nobody chose, silently one bead easier than the ladder says.
        /// </para>
        /// </summary>
        public static bool Admissible(WeaveLayout grove, int beads)
            => grove != null && grove.IsComplete && grove.Beads.Count >= beads;

        /// <summary>
        /// Deals one seed's board and measures it, or reports that it may not be believed.
        /// </summary>
        /// <returns>False for a board that is inadmissible, could not be measured inside the
        /// budget, whose near-best arrangements were not counted to the end, or that lets every
        /// pair take its shortest route at once (<see cref="WeaveGenerator.MinSlack"/>).</returns>
        public static bool TryMeasure(int width, int height, int pairs, int beads, uint seed,
                                      out WeaveSeedHit hit, int cap = Cap, int budget = Budget)
        {
            hit = default;

            var grove = WeaveGenerator.Build(width, height, pairs, seed, beads);
            if (!Admissible(grove, beads)) return false;

            var tally = WeaveSolver.Measure(grove, cap, budget);
            if (!tally.Solved || !tally.Exhausted) return false;
            if (tally.Slack < WeaveGenerator.MinSlack) return false;

            int reach = int.MaxValue;
            for (int p = 0; p < grove.Pairs.Count; p++)
            {
                int apart = grove.Distance(grove.Pairs[p].Heart, grove.Pairs[p].Critter) + 1;
                if (apart < reach) reach = apart;
            }

            hit = new WeaveSeedHit(seed, tally.Slack, tally.Ways, grove.Par, reach, tally.Nodes);
            return true;
        }

        /// <summary>
        /// Sweeps a range of seeds for one grove shape and keeps the boards landing in a band.
        /// </summary>
        /// <param name="wantSlack">The exact detour this rung wants forced. Climbing this down a
        /// chapter is what makes ten groves a ladder rather than ten groves.</param>
        /// <param name="wantLow">Fewest near-best arrangements that is acceptable. A grove with
        /// one is a single routing the player has to find exactly, which is a wall rather than a
        /// puzzle.</param>
        /// <param name="wantHigh">Most that is acceptable for this rung.</param>
        /// <param name="from">First seed to try, inclusive. Seeds start at one because zero is
        /// what a level authoring no seed writes, and that means "derive one from the id".</param>
        /// <param name="to">Last seed to try, inclusive.</param>
        /// <param name="most">Stop once this many have been found.</param>
        public static List<WeaveSeedHit> Sweep(int width, int height, int pairs, int beads,
                                               int wantSlack, int wantLow, int wantHigh,
                                               uint from = 1, uint to = 4000, int most = 12,
                                               int cap = Cap, int budget = Budget)
        {
            var hits = new List<WeaveSeedHit>();

            for (uint seed = from; seed <= to && hits.Count < most; seed++)
            {
                if (!TryMeasure(width, height, pairs, beads, seed, out var hit, cap, budget))
                    continue;

                if (hit.Slack != wantSlack) continue;
                if (hit.Ways < wantLow || hit.Ways > wantHigh) continue;

                hits.Add(hit);
            }

            return hits;
        }
    }
}
