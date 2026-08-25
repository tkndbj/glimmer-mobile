using System.Text;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// What each Lightweave grove actually asks of a player, counted rather than argued about.
    ///
    /// <para>
    /// <b>This is the mode's <c>difficulty.py</c>.</b> A weave level authors a size, a pair
    /// count, a bead count and a seed, and the board is generated — so unlike a glade there is
    /// nothing in the file to read to find out whether it is hard. <see cref="WeaveGenerator"/>
    /// proves it can be solved and stops there, and "solvable" covers both a grove you have to
    /// find your way through and a grove joined by six straight drags.
    /// </para>
    /// <para>
    /// <b>Not a build gate, deliberately.</b> The search is exponential in the worst case and the
    /// honest answer is sometimes "I ran out of budget" — a gate reporting that would fail builds
    /// for reasons nobody can reproduce, and a gate that warned on every board would be a warning
    /// nobody reads, which this project has already written down twice. So this reports, and the
    /// ladder it reports on is pinned by <c>WeaveLadderTests</c>, which is where a regression
    /// actually fails something.
    /// </para>
    /// <para>
    /// <b>Slack</b> is the reading that matters most: the least total detour any arrangement of
    /// the grove has, over and above every pair's own shortest possible route. Zero means every
    /// pair can go as directly as it possibly could, all at once — the board is joined by drawing
    /// the obvious line at each critter in turn, and it asks nothing. It is meant to
    /// <em>climb</em> down the chapter.
    /// </para>
    /// <para>
    /// <b>Ways</b> is the other reading: how many arrangements land within a couple of cells of
    /// the best one, which is how much of what a tidy player tries will work. It is meant to
    /// <em>fall</em>. The two measure different things and a ladder needs both — a grove can
    /// admit a handful of near-best arrangements and still let every pair go straight, and a
    /// grove can force a long detour and still have a hundred ways to pay for it.
    /// </para>
    /// <para>
    /// Both come from <see cref="WeaveSolver"/>, and note what is <em>not</em> here any more: a
    /// per-pair detour. The mode used to refuse a channel that was the straight line between its
    /// own two ends, which sent every pair the long way round on a route the board had already
    /// picked. Slack is a bar on the pairs together, so any one route may be perfectly direct and
    /// what the board denies is all of them being direct at once.
    /// </para>
    /// </summary>
    public static class WeaveSurvey
    {
        /// <summary>
        /// How hard the counter tries before reporting "undecided".
        ///
        /// <para>
        /// It is the same figure <c>WeaveLadderTests</c> uses, and that is not a coincidence to
        /// be tidied away: the seed sweep is what <em>chooses</em> a board and the ladder test is
        /// what <em>pins</em> it, so a board decidable by one and not the other is a rung that
        /// was authored and then immediately fails. They were 600,000 and two million once, which
        /// is exactly how a finale was picked that the suite then could not count.
        /// </para>
        /// </summary>
        public const int Budget = 2_000_000;

        /// <summary>Where counting stops. Past this a grove is "many" and the difference is noise.</summary>
        public const int Cap = 500;

        /// <summary>
        /// The least slack a shipped grove may have — see the remarks on this class.
        ///
        /// Two rather than one because a detour cannot be odd, so this reads as "not everybody
        /// can go straight at once" rather than as a number somebody picked. It is
        /// <c>WeaveGenerator.MinSlack</c> and is referenced rather than repeated: this one is in
        /// the same solution as the thing it is checking, so unlike the copy in the test
        /// assembly there is nothing to be gained by spelling it out again.
        /// </summary>
        public const int MinSlack = WeaveGenerator.MinSlack;

        [MenuItem("Glimmer Grove/Content/Survey Lightweave", false, 43)]
        public static void Survey()
        {
            var load = EditorContentLoader.Load();
            if (load.Index.IsEmpty)
            {
                Debug.LogError("[Glimmer] no content found");
                return;
            }

            var sb = new StringBuilder("[Glimmer] Lightweave survey\n");
            sb.AppendLine($"{"#",-4}{"level id",-26}{"size",-8}{"pairs",-7}{"beads",-7}" +
                          $"{"par",-6}{"gold",-7}{"limit",-8}{"slack",-8}{"ways",-9}" +
                          $"{"reach",-7}{"channels",-11}nodes");

            int order = 0, found = 0;
            foreach (var level in load.AllLevels())
            {
                var rules = level.RulesAs<WeaveRules>();
                if (rules == null) continue;

                found++;
                var grove = rules.LayoutFor(level.Id);
                var tally = WeaveSolver.Measure(grove, Cap, Budget);

                int shortest = int.MaxValue, longest = 0, reach = int.MaxValue;
                for (int p = 0; p < grove.Pairs.Count; p++)
                {
                    int length = grove.Straight(p);
                    if (length < shortest) shortest = length;
                    if (length > longest) longest = length;

                    int apart = grove.Distance(grove.Pairs[p].Heart, grove.Pairs[p].Critter) + 1;
                    if (apart < reach) reach = apart;
                }

                var tuning = level.Tuning;
                string ways = tally.Exhausted ? tally.Ways.ToString() : tally.Ways + "+";
                string slack = tally.Solved ? tally.Slack.ToString() : "?";

                sb.AppendLine(
                    $"{++order,-4}{level.Id,-26}{grove.Width + "x" + grove.Height,-8}" +
                    $"{grove.Pairs.Count,-7}{grove.Beads.Count,-7}{tuning.Par,-6}" +
                    $"{Seconds(tuning.TimeGoldMillis),-7}{Seconds(tuning.TimeLimitMillis),-8}" +
                    $"{slack,-8}{ways,-9}{reach,-7}{shortest + ".." + longest,-11}{tally.Nodes}");

                if (!grove.IsComplete)
                    sb.AppendLine($"     ^ the carve leaves {grove.Count - grove.SolutionLength} " +
                                  "cell(s) untouched, so the endpoints are not spread across it");

                if (grove.Beads.Count < rules.BeadCount)
                    sb.AppendLine($"     ^ asked for {rules.BeadCount} bead(s) and placed " +
                                  $"{grove.Beads.Count}");

                if (tally.Solved && tally.Slack < MinSlack)
                    sb.AppendLine("     ^ every pair can take its shortest route at once, so " +
                                  "this grove asks the player nothing — re-seed with SeedSearch");

                if (!tally.Solved)
                    sb.AppendLine("     ^ could not be measured inside the budget; its difficulty " +
                                  "is unknown rather than high");
            }

            if (found == 0) { Debug.Log("[Glimmer] no Lightweave levels in this content"); return; }

            sb.AppendLine();
            sb.AppendLine("slack = the least total detour any arrangement has, in cells, over " +
                          "every pair's own shortest route.");
            sb.AppendLine("It should climb down the chapter, and it must never be 0 — that is a " +
                          "grove joined by drawing the obvious line at each critter.");
            sb.AppendLine("ways  = arrangements within " + WeaveSolver.Latitude + " cells of the " +
                          "best one, capped at " + Cap + "; '+' means the search hit a limit.");
            sb.AppendLine("It should fall: many ways is forgiving, few is a grove you have to find.");
            sb.AppendLine("reach = how far apart the closest pair's two ends are. Under " +
                          "WeaveGenerator.MinReach a pair is joined by a reflex.");
            Debug.Log(sb.ToString());
        }

        static string Seconds(int millis)
            => millis == int.MaxValue ? "-" : (millis / 1000f).ToString("0.#") + "s";

        /// <summary>
        /// Sweeps seeds for one grove shape and reports the ones whose boards land in a band.
        ///
        /// <para>
        /// This is how a level's <c>seed</c> is chosen and it is the whole reason the field
        /// exists. The seed is otherwise derived from the level id, which deals a perfectly
        /// solvable board of entirely unknown difficulty — fine for a prototype and not for a
        /// ladder. Authoring the number the sweep found makes a level's difficulty a decision on
        /// the record rather than an accident of what its name hashed to.
        /// </para>
        /// <para>
        /// Called from the Editor console or the MCP bridge rather than wired to a menu item: it
        /// takes several numbers, it is run a handful of times per content drop, and a dialog
        /// asking for them would be more to maintain than the two lines it replaces.
        /// </para>
        /// </summary>
        /// <param name="wantSlack">The exact slack this rung wants. Climbing this down the
        /// chapter is what makes ten groves a ladder rather than ten groves.</param>
        /// <param name="wantLow">Fewest near-best arrangements that is acceptable — below a
        /// handful a grove is one routing the player must find exactly, which is a wall rather
        /// than a puzzle when there is a clock running.</param>
        /// <param name="wantHigh">Most that is acceptable for this rung.</param>
        /// <remarks>
        /// <para>
        /// An undecided board is refused outright here, where the survey is happy to report one.
        /// A capped search has seen some arrangements and not others, so believing its slack
        /// would err towards calling a board <em>harder</em> than it is — the one direction an
        /// authoring bar must never be wrong in.
        /// </para>
        /// <para>
        /// So is a board that could not be given all the beads it asked for. A bead is only worth
        /// placing on a cell lying off every shortest route between its pair's ends, and a carve
        /// that offered too few of those is a board whose difficulty is not the one the rung was
        /// authored for — silently one bead short, which is exactly the kind of thing that ships.
        /// </para>
        /// </remarks>
        public static string SeedSearch(int width, int height, int pairs, int beads,
                                        int wantSlack, int wantLow, int wantHigh, int seeds = 4000)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"seed sweep {width}x{height} p{pairs} b{beads}, want slack " +
                          $"{wantSlack} and {wantLow}..{wantHigh} ways");

            int hits = 0;
            for (uint seed = 1; seed <= seeds && hits < 12; seed++)
            {
                var grove = WeaveGenerator.Build(width, height, pairs, seed, beads);
                if (!grove.IsComplete) continue;
                if (grove.Beads.Count < beads) continue;

                var tally = WeaveSolver.Measure(grove, Cap, Budget);
                if (!tally.Solved || !tally.Exhausted) continue;
                if (tally.Slack != wantSlack || tally.Slack < MinSlack) continue;
                if (tally.Ways < wantLow || tally.Ways > wantHigh) continue;

                int reach = int.MaxValue;
                for (int p = 0; p < grove.Pairs.Count; p++)
                {
                    int apart = grove.Distance(grove.Pairs[p].Heart, grove.Pairs[p].Critter) + 1;
                    if (apart < reach) reach = apart;
                }

                hits++;
                sb.AppendLine($"  seed {seed,-6}slack {tally.Slack,-6}ways {tally.Ways,-6}" +
                              $"par {grove.Par,-6}reach {reach,-6}nodes {tally.Nodes}");
            }

            if (hits == 0)
                sb.AppendLine("  nothing in band — widen it, sweep more seeds, or change the " +
                              "shape. High slack with few ways is scarce at six pairs.");
            return sb.ToString();
        }
    }
}
