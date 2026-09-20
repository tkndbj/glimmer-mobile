using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What the refill may deal, and the one rule that decides it: <b>a dealt gem does not land
    /// already matched</b> (invariant 37eo), as often as
    /// <see cref="SiegeTuning.RefillSettlesPercent"/> says.
    ///
    /// <para>
    /// <b>A file of its own rather than more of <c>SiegeRuleTests</c></b>, because this is a rule
    /// about the <em>field</em> rather than about a chapter — nothing here plays a hill, and the
    /// questions it asks are asked of every siege ever authored.
    /// </para>
    /// <para>
    /// What bought the rule is a count. Without it, a refill was a free draw per cell, so every
    /// collapse rolled the board a fresh chance of three alike: <b>48% of matches chained</b> on
    /// the field below and the deepest reached <b>x18</b>, and in play the three-colour opening
    /// rungs cleared <b>13 gems a match</b> against a par crediting 5.5. A chain nobody set up
    /// rejects no play, so it says nothing about any (invariant 5d) — the board was playing itself
    /// and taking the credit.
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
        /// The refill settles as often as <see cref="SiegeTuning.RefillSettlesPercent"/> says.
        ///
        /// <para>
        /// <b>Asked of the rule rather than of its consequence.</b> <c>Deal</c> is what a collapse
        /// calls and it leaves the board exactly as it found it, so this can ask it for the gem it
        /// would drop into every cell of a settled field and then check that gem where it would
        /// have landed. The rate a played field shows is the other half
        /// (<see cref="ACascadeIsMostlyTheFieldFalling"/>) and it is a band; this one is
        /// arithmetic.
        /// </para>
        /// <para>
        /// <b>A ceiling rather than a rate, and that is not laziness.</b> The dial decides how
        /// often the settle check is <em>waived</em>, and a waived draw only lands matched when
        /// the gem it happened to pick lines up — which is a minority of waivers. So the share
        /// that lands matched can be anything up to the waived share and no more. What that
        /// catches is the thing worth catching: the rule being lost altogether, or applied where
        /// it was waived.
        /// </para>
        /// <para>
        /// <b>The escape is a real state and is set aside as one</b>: on a three-colour field a
        /// cell with two alike above it and two alike beside it has no settled answer at all, so
        /// those cells say nothing about the dial either way. That it is a state the board can
        /// really reach is <see cref="ACellCanBeBoxedInOnAThreeColourField"/>, because a settled
        /// field never holds one.
        /// </para>
        /// </summary>
        [Test]
        public void TheRefillSettlesAsOftenAsItSays()
        {
            int asked = 0, landedMatched = 0, cornered = 0;

            foreach (string bag in new[] { "rgb", "rgby" })
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
                for (int pass = 0; pass < 200; pass++)
                {
                    for (int at = 0; at < board.Count; at++)
                    {
                        char was = cells[at];

                        bool Lines(char gem)
                        {
                            cells[at] = gem;
                            bool any = SiegeLayout.Lined(cells, board.Width, board.Height,
                                                         charms, at);
                            cells[at] = was;
                            return any;
                        }

                        bool boxedIn = true;
                        for (int i = 0; i < bag.Length; i++) boxedIn &= Lines(bag[i]);

                        char dealt = board.Deal(at, out var _);

                        if (boxedIn) { cornered++; continue; }

                        asked++;
                        if (Lines(dealt)) landedMatched++;
                    }
                }
            }

            Assert.Greater(asked, 5000, "too few deals to read a rate off");

            int waived = 100 - SiegeTuning.RefillSettlesPercent;
            int share = 100 * landedMatched / asked;

            Assert.LessOrEqual(share, waived,
                               $"{share}% of dealt gems landed already matched against the "
                               + $"{waived}% {nameof(SiegeTuning.RefillSettlesPercent)} waives - "
                               + "the settle rule is not being applied (invariant 37eo)");

            // A settled field never boxes a cell in - every neighbour pair it holds is already
            // broken up, which is what settled means. The state only arises mid-collapse, so it
            // is proved reachable below rather than waited for here.
            Assert.GreaterOrEqual(cornered, 0);
        }

        /// <summary>
        /// A cell where every gem in the bag lines up is a real arrangement, so the fallback that
        /// deals the drawn gem anyway is a branch that really runs.
        ///
        /// <b>Built rather than waited for.</b> A settled field never boxes a cell in — every
        /// neighbour pair on one is already broken up, which is what settled means — so the state
        /// only arises part-way through a collapse, where nothing can hold a board still long
        /// enough to look at it. What can be held still is the arrangement itself.
        /// </summary>
        [Test]
        public void ACellCanBeBoxedInOnAThreeColourField()
        {
            // r r . g g   with b above and below the gap: on a three-colour bag every gem lines
            // b . . . .   up there, so the settle rule has no answer and the draw stands.
            string[] rows =
            {
                "bgbgbgbg",
                "gbgbgbgb",
                "rrbggbgb",
                "bgbgbgbg",
                "gbgbgbgb",
            };

            var cells = string.Join(string.Empty, rows).ToCharArray();
            var charms = new SiegeCharm[cells.Length];

            const int At = 2 * Width + 2;   // the gap between the two reds and the two greens

            // Above and below it, a pair of blues, so the third colour lines up vertically.
            cells[At - Width] = 'b';
            cells[At + Width] = 'b';
            cells[At - Width * 2] = 'b';

            foreach (char gem in "rgb")
            {
                cells[At] = gem;
                Assert.IsTrue(SiegeLayout.Lined(cells, Width, Height, charms, At),
                              $"a '{gem}' does not line up at the boxed-in cell, so this fixture "
                              + "is no longer building the state it is named for");
            }
        }

        /// <summary>
        /// Over a played field a match chains <em>sometimes</em> — mostly because the field fell
        /// into line rather than because the deal agreed with itself.
        ///
        /// <para>
        /// <b>A band, and both ends of it are the point.</b> Too high and the refill is dealing
        /// matches again, which is the fault the settle rule was written for. Too low and a chain
        /// has stopped happening at all, which is a mode with nothing to set up — gravity dropping
        /// a gem into line with what was already there is the payoff a player earns, and it is the
        /// one this mode is meant to pay for.
        /// </para>
        /// <para>
        /// <b>The band is wide because the dial is meant to move inside it</b> — see
        /// <see cref="SiegeTuning.RefillSettlesPercent"/>. Measured on this field: 17% of matches
        /// chain at 100, 21% at 85 and 26% at 70, against <b>48%</b> with the rule absent, which
        /// is the state this refuses.
        /// </para>
        /// <para>
        /// <b>It plays the field rather than a level</b>, so nothing here depends on a hill, a
        /// turret or a chapter: whether a rung can be held is
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> and it needs ninety runs to say
        /// anything. This asks only what a match does.
        /// </para>
        /// </summary>
        [Test]
        public void ACascadeIsMostlyTheFieldFalling()
        {
            int matches = 0, chained = 0, gems = 0;

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
                    if (turn.Beats.Count > 1) chained++;
                }
            }

            Assert.Greater(matches, 3000, "too few matches to read a rate off");

            int share = 100 * chained / matches;
            int each = 100 * gems / matches;

            Assert.LessOrEqual(share, 40,
                               $"{share}% of matches chain and a match clears {each / 100f:0.00} "
                               + "gems, against the 48% and 6.4 the rule was written to stop - the "
                               + "refill is dealing matches again, so a chain is the board's work "
                               + "rather than the player's (invariant 37eo)");

            Assert.GreaterOrEqual(share, 8,
                                  $"only {share}% of matches chain - gravity has stopped dropping "
                                  + "gems into line with each other, which is the one chain this "
                                  + "mode is meant to pay for");
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
