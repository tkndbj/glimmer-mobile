using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The field dealt again for want of a move (<see cref="SiegeTurn.Shuffled"/>), held from
    /// the view's side of the wire.
    ///
    /// <para>
    /// <b>What this guards is the fault a player met on 2026-10-03</b>: the board reshuffled a
    /// dead field and told nobody, so the view's picture of the field - built turn by turn from
    /// the swaps, the clears and the drops it is handed - was wrong until the end-of-turn repaint
    /// snapped every cell at once. The fixture keeps that picture exactly as the view does
    /// (<c>SiegeView.Resolve</c> and <c>Beat</c>) and asks, after every turn, that it is the
    /// model's field - and when it is not, that the turn said so and said exactly how.
    /// </para>
    /// <para>
    /// <b>Played on Neonhaven's field with both stones dealt</b>, because that is where a dead
    /// field is commonest (they pile up and line up with no colour), so a few thousand turns are
    /// enough to meet the shuffle many times rather than hoping to meet it once.
    /// </para>
    /// </summary>
    public sealed class SiegeShuffleTests
    {
        static readonly string[] Field =
        {
            "gybbyygb",
            "brbbgrgy",
            "rrgyrryg",
            "ybbgygbr",
            "rgyyrbrr",
        };

        static SiegeLayout Layout()
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, 8, 5, SiegeLayout.Cells, out var grid,
                                            out string error), error);

            var layout = new SiegeLayout(grid, "rgby", "rgby", new[] { "rgbrgb" }, null, 0, null,
                                         0, "plsfha", obsidian: true, singularity: true);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        /// <summary>
        /// **The view's picture plus the turn's shuffle record is always the model's field**, on
        /// every turn of several thousand - and a field that changed with no record never happens.
        /// </summary>
        [Test]
        public void TheViewsPictureIsAlwaysTheFieldOnceTheShuffleIsDrawn()
        {
            var layout = Layout();
            var pick = new System.Random(11);
            int shuffles = 0, turns = 0;

            for (int run = 0; run < 60 && shuffles < 6; run++)
            {
                var board = SiegeBoard.Build(layout);
                int n = board.Width * board.Height;

                var picture = new int[n];
                for (int i = 0; i < n; i++) picture[i] = board.ColourAt(i);

                for (int t = 0; t < 120; t++)
                {
                    var swap = board.FindSwap(pick.Next(n * 2));
                    if (!swap.Found) break;

                    var turn = board.Swap(swap.A, swap.B);
                    Assert.IsNotNull(turn, "the board refused a swap it had offered");
                    turns++;

                    Follow(picture, turn, board.Width);

                    if (turn.Shuffled != null)
                    {
                        shuffles++;
                        AssertPermutation(turn.Shuffled, n);

                        var was = (int[])picture.Clone();
                        for (int to = 0; to < n; to++) picture[to] = was[turn.Shuffled[to]];
                    }

                    for (int i = 0; i < n; i++)
                        Assert.AreEqual(board.ColourAt(i), picture[i],
                                        $"run {run}, turn {t}: cell {i} changed and the turn "
                                        + (turn.Shuffled == null ? "recorded no shuffle"
                                                                 : "recorded a different one"));

                    Assert.IsTrue(board.AnySwap(), "a turn ended on a field with no move");
                }
            }

            Assert.GreaterOrEqual(shuffles, 3,
                                  $"only {shuffles} shuffles in {turns} turns - the fixture no "
                                  + "longer meets the case it exists for");
        }

        /// <summary>
        /// **A field with a move on it is never recorded as shuffled**, so the view never turns
        /// a board over for nothing - asked of every ordinary turn the walk above plays too, but
        /// here on the first chapter's field, where a dead board never happens.
        /// </summary>
        [Test]
        public void AFieldWithAMoveIsNeverShuffled()
        {
            Assert.IsTrue(ProtoGrid.TryRead(new[]
                          {
                              "brbrgbgg", "rrggbbrg", "bbgrrggr", "gbrbgrbr", "rggrrbgb",
                          }, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            var layout = new SiegeLayout(grid, "rgb", "rgb", new[] { "rgbrgb" }, null);
            var pick = new System.Random(5);

            for (int run = 0; run < 20; run++)
            {
                var board = SiegeBoard.Build(layout);
                for (int t = 0; t < 120; t++)
                {
                    var swap = board.FindSwap(pick.Next(board.Width * board.Height * 2));
                    if (!swap.Found) break;

                    var turn = board.Swap(swap.A, swap.B);
                    Assert.IsNull(turn.Shuffled, "a field that had a move was turned over");
                }
            }
        }

        /// <summary>
        /// **The view's turn-over fits the time it is given**: it starts after the slowest
        /// refill has landed, and every gem - however late its stagger - has landed by the end.
        /// </summary>
        [Test]
        public void TheTurnOverWaitsForTheRefillAndEndsOnTime()
        {
            float settle = SiegeView.DropMost - SiegeTuning.BeatFor * .5f + .05f;
            Assert.GreaterOrEqual(SiegeTuning.BeatFor * .5f + System.Math.Max(0f, settle),
                                  SiegeView.DropMost, "the turn-over starts on a gem still falling");

            Assert.AreEqual(SiegeView.ShuffleLift + SiegeView.ShuffleStagger + SiegeView.ShuffleFly
                            + SiegeView.ShuffleLand, SiegeView.ShuffleFor, 1e-5,
                            "the last gem lands after the turn-over is over");
        }

        /// <summary>The view's bookkeeping, exactly as <c>SiegeView.Resolve</c> and <c>Beat</c> keep it.</summary>
        static void Follow(int[] picture, SiegeTurn turn, int width)
        {
            int k = picture[turn.A];
            picture[turn.A] = picture[turn.B];
            picture[turn.B] = k;

            foreach (var beat in turn.Beats)
            {
                foreach (int cell in beat.Cleared) picture[cell] = int.MinValue;

                foreach (var drop in beat.Drops)
                {
                    int to = drop.To * width + drop.Column;

                    if (drop.IsNew)
                    {
                        picture[to] = drop.Colour;
                        continue;
                    }

                    int from = drop.From * width + drop.Column;
                    picture[to] = picture[from];
                    picture[from] = int.MinValue;
                }
            }
        }

        static void AssertPermutation(int[] from, int n)
        {
            Assert.AreEqual(n, from.Length, "the shuffle record is not one entry a cell");

            var seen = new bool[n];
            for (int to = 0; to < n; to++)
            {
                int f = from[to];
                Assert.That(f >= 0 && f < n, $"cell {to} came from {f}, which is off the field");
                Assert.IsFalse(seen[f], $"two cells claim the gem from {f}");
                seen[f] = true;
            }
        }
    }
}
