using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What the refill may deal, and the one rule that decides it: <b>a dealt gem never lands
    /// already matched</b> (invariant 37eo).
    ///
    /// <para>
    /// <b>A file of its own rather than more of <c>SiegeRuleTests</c></b>, because this is a rule
    /// about the <em>field</em> rather than about a chapter — nothing here plays a hill, and the
    /// two questions it asks are asked of every siege ever authored.
    /// </para>
    /// <para>
    /// What bought the rule is a count. Before it, over ninety runs a chapter, <b>39% of every
    /// match cascaded</b> and the deepest chain reached <b>x15</b>; the three-colour opening rungs
    /// cleared <b>13 gems a match</b> against a par crediting 5.5, and chained 2.7 deep on
    /// average. A cascade nobody set up rejects no play, so it says nothing about any (invariant
    /// 5d) — the board was playing itself and taking the credit.
    /// </para>
    /// </summary>
    public sealed class SiegeFieldTests
    {
        const int Width = 8, Height = 5;

        // ------------------------------------------------------------------ the one-cell reading
        /// <summary>
        /// <see cref="SiegeLayout.Lined"/> answers about one cell exactly what
        /// <see cref="SiegeLayout.Runs"/> answers about the whole field.
        ///
        /// <para>
        /// <b>A second reading of one rule is the shape invariant 5b refuses</b>, and the refill
        /// needs the cheap one forty times a collapse. So the two are not left to agree by
        /// inspection: this walks random fields — wilds and all, at both colour counts the shipped
        /// chapters deal — and fails on the first cell they differ about.
        /// </para>
        /// <para>
        /// <b>Wilds are dealt far thicker here than any level deals them</b>, because the prism is
        /// the whole reason <c>Runs</c> is written per colour rather than per neighbour: at a
        /// level's own rate it would take a thousand boards to put two in one line.
        /// </para>
        /// </summary>
        [Test]
        public void OneCellReadsTheSameAsTheWholeField()
        {
            uint seed = 0x9E3779B9u;

            uint Next()
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                return seed;
            }

            string[] bags = { "rgb", "rgby" };
            var faults = new List<string>();

            for (int board = 0; board < 4000 && faults.Count == 0; board++)
            {
                string bag = bags[board % bags.Length];

                var cells = new char[Width * Height];
                var charms = new SiegeCharm[cells.Length];

                for (int i = 0; i < cells.Length; i++)
                {
                    cells[i] = bag[(int)(Next() % (uint)bag.Length)];

                    // A hole now and then, because a collapse asks this of a half-filled field.
                    if (Next() % 16 == 0) cells[i] = SiegeBoard.Hole;

                    charms[i] = Next() % 8 == 0 ? SiegeCharm.Prism : SiegeCharm.None;
                }

                var whole = SiegeLayout.Runs(cells, Width, Height, charms);

                for (int i = 0; i < cells.Length; i++)
                {
                    bool one = SiegeLayout.Lined(cells, Width, Height, charms, i);
                    if (one == whole.Contains(i)) continue;

                    faults.Add($"cell {i} of board {board}: the whole field says "
                               + $"{whole.Contains(i)} and the one-cell reading says {one}");
                    break;
                }
            }

            Assert.IsEmpty(faults, string.Join("; ", faults.ToArray()));
        }

        // ------------------------------------------------------------------ the refill
        /// <summary>
        /// Every gem the refill deals settles, unless no gem in the bag could.
        ///
        /// <para>
        /// <b>Asked of the rule rather than of its consequence.</b> <c>Deal</c> is what a collapse
        /// calls and it leaves the board exactly as it found it, so this can ask it for the gem it
        /// would drop into every cell of a settled field and then check that gem where it would
        /// have landed. The rate a played run shows is the other half
        /// (<see cref="ACascadeIsTheFieldFallingRatherThanTheDealAgreeingWithItself"/>) and it is
        /// a band; this one is exact.
        /// </para>
        /// <para>
        /// <b>The escape is a real state and is tested as one</b>: on a three-colour field a cell
        /// with two alike above it and two alike beside it has no settled answer at all, so what
        /// is required is not "never matched" but "never matched where another gem in the bag
        /// would not have been".
        /// </para>
        /// </summary>
        [Test]
        public void ADealtGemNeverLandsAlreadyMatched()
        {
            string[] bags = { "rgb", "rgby" };
            var faults = new List<string>();

            foreach (string bag in bags)
            {
                var board = Field(bag);

                var cells = new char[board.Count];
                var charms = new SiegeCharm[board.Count];

                for (int i = 0; i < board.Count; i++)
                {
                    cells[i] = board.At(i);
                    charms[i] = board.CharmAt(i);
                }

                // Several passes, because `Deal` walks the one random stream: the same cell asked
                // twice is asked of two different draws.
                for (int pass = 0; pass < 40; pass++)
                {
                    for (int at = 0; at < board.Count; at++)
                    {
                        char was = cells[at];
                        char gem = board.Deal(at, out var _);

                        cells[at] = gem;
                        bool lined = SiegeLayout.Lined(cells, board.Width, board.Height,
                                                       charms, at);
                        cells[at] = was;

                        if (!lined) continue;

                        bool cornered = true;

                        for (int i = 0; i < bag.Length; i++)
                        {
                            cells[at] = bag[i];
                            cornered &= SiegeLayout.Lined(cells, board.Width, board.Height,
                                                          charms, at);
                        }

                        cells[at] = was;

                        if (cornered) continue;

                        faults.Add($"a '{bag}' field dealt '{gem}' into cell {at}, where it lines "
                                   + "up and where another gem in the bag would not have");
                    }
                }
            }

            Assert.IsEmpty(faults, string.Join("; ", faults.ToArray()));
        }

        /// <summary>
        /// Over a played field a match cascades <em>occasionally</em> — and when it does, it is
        /// because the field fell into line rather than because the deal agreed with itself.
        ///
        /// <para>
        /// <b>A band, and both ends of it are the point.</b> Too high and the refill is dealing
        /// matches again, which is the fault this rule was written for. Too low and a cascade has
        /// stopped happening at all, which is a mode with nothing to set up — gravity dropping a
        /// gem into line with what was already there is the payoff a player earns, and it is the
        /// only one this mode pays for now.
        /// </para>
        /// <para>
        /// <b>It plays the field rather than a level</b>, so nothing here depends on a hill, a
        /// turret or a chapter: what a rung costs is
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> and it needs ninety runs to say
        /// anything. This asks only what a match does.
        /// </para>
        /// </summary>
        [Test]
        public void ACascadeIsTheFieldFallingRatherThanTheDealAgreeingWithItself()
        {
            int matches = 0, cascaded = 0, gems = 0;

            foreach (string bag in new[] { "rgb", "rgby" })
            {
                var board = Field(bag);

                for (int i = 0; i < 2000; i++)
                {
                    var swap = board.FindSwap(i);
                    if (!swap.Found) break;

                    var turn = board.Swap(swap.A, swap.B);
                    if (turn == null) continue;

                    matches++;
                    gems += turn.Worth;
                    if (turn.Beats.Count > 1) cascaded++;
                }
            }

            Assert.Greater(matches, 3000, "too few matches to read a rate off");

            int share = 100 * cascaded / matches;
            int each = 100 * gems / matches;

            Assert.LessOrEqual(share, 25,
                               $"{share}% of matches cascade and a match clears {each / 100f:0.00} "
                               + "gems, against the 8-12% and ~3.7 this rule leaves and the 39% "
                               + "and 6.4 it was written to stop - the refill is dealing matches "
                               + "again, so a chain is the board's work rather than the player's "
                               + "(invariant 37eo)");

            Assert.GreaterOrEqual(share, 3,
                                  $"only {share}% of matches cascade - gravity has stopped "
                                  + "dropping gems into line with each other, which is the one "
                                  + "cascade this mode is meant to pay for");
        }

        // ------------------------------------------------------------------ the field it plays
        /// <summary>
        /// A board of nothing but gems: no hill worth naming, no cogs, no charms.
        ///
        /// <b>The field alone</b>, because every question in this file is about what the deal puts
        /// in a cell, and raiders walking down at it would only decide when the reading stopped.
        /// </summary>
        static SiegeBoard Field(string bag)
        {
            string[] rows = bag.Length == 3
                ? new[] { "brbrgbgg", "rrggbbrg", "bbgrrggr", "gbrbgrbr", "rggrrbgb" }
                : new[] { "gbrryrbb", "rygbrrbr", "bbgybggy", "ybygyybb", "gyrbbggr" };

            Assert.IsTrue(ProtoGrid.TryRead(rows, Width, Height, SiegeLayout.Cells,
                                            out var grid, out string error), error);

            var layout = new SiegeLayout(grid, bag, bag, new[] { "rgb" }, null, 0);
            Assert.IsNull(layout.Fault, layout.Fault);

            return SiegeBoard.Build(layout);
        }
    }
}
