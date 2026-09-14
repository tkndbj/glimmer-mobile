using System.Collections.Generic;
using GlimmerGrove.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The chest pack the hub's box and the tasks page's ladder are both drawn from, and the
    /// panel a chest on that ladder opens.
    ///
    /// <para>
    /// <b>These are the only gates a drawing gets.</b> Nothing in this project opens a PNG on
    /// the way to a build, so the questions a render answers — does the arch read, is the
    /// grandest chest the biggest thing on the plate — are answered by eye against
    /// <c>Tools/render_tasks.py</c>. What can be pinned here is everything the picture rests
    /// on: that the crest holds the top of the ladder, that a row of any length comes out
    /// centred and packed rather than gapped, and that none of it is decided by a float's last
    /// digit. Each of those was a bug on the way in.
    /// </para>
    /// </summary>
    public sealed class ChestPackTests
    {
        const float Tall = 210f, Short = 142f, Dip = 26f, Floor = -88f, Overlap = .07f;

        static IReadOnlyList<ChestTier> Tiers => TaskTable.Default.Tiers;

        static ChestPack.Seat[] Lay(int count)
            => ChestPack.Lay(Take(count), Tall, Short, Dip, Floor, Overlap);

        static int Rank(IReadOnlyList<ChestTier> ladder, ChestTier tier)
        {
            for (int i = 0; i < ladder.Count; i++) if (ReferenceEquals(ladder[i], tier)) return i;
            return -1;
        }

        /// <summary>
        /// The first <paramref name="count"/> shipped tiers. Deliberately not a wrapped list —
        /// a row holding one tier twice cannot answer a question about *which* seat a tier took.
        /// </summary>
        static IReadOnlyList<ChestTier> Take(int count)
        {
            var list = new List<ChestTier>();
            for (int i = 0; i < count && i < Tiers.Count; i++) list.Add(Tiers[i]);
            return list;
        }

        /// <summary>How many tiers the shipped table holds, which bounds what can be asked.</summary>
        static int Most => Tiers.Count;

        // --------------------------------------------------------------- the crest
        /// <summary>
        /// The whole point of the arrangement: the best chest on the ladder is the biggest
        /// thing in the row. Drawn humblest-to-grandest instead — which is how both screens
        /// drew it — the royal chest lands on an end, at the smallest size the arch has, half
        /// behind its neighbour, on the two cards whose job is to advertise it.
        /// </summary>
        [Test]
        public void TheGrandestChestTakesTheCrest()
        {
            for (int n = 1; n <= Most; n++)
            {
                var seats = Lay(n);
                var ladder = Take(n);

                // Stated as "nothing stands taller than it" rather than "it is the tallest",
                // because an even row has two seats at the crest and they are equal by design.
                var grandest = seats[0];
                foreach (var seat in seats) if (ReferenceEquals(seat.Tier, ladder[n - 1])) grandest = seat;

                Assert.AreSame(ladder[n - 1], grandest.Tier, $"a row of {n} does not hold the last tier");
                foreach (var seat in seats)
                    Assert.LessOrEqual(seat.Tall, grandest.Tall + .001f,
                                       $"a row of {n} stands something taller than its grandest chest");

                Assert.AreEqual(Tall, grandest.Tall, .001f,
                                $"a row of {n} does not draw its grandest chest at the crest height");
            }
        }

        /// <summary>
        /// And the rest fall away from it in order, so a pack never reads as a shuffle: walking
        /// out from the crest, each chest is humbler than the one nearer the middle.
        /// </summary>
        [Test]
        public void TheRestFallAwayFromTheCrestInOrder()
        {
            // Said as a pairwise rule rather than as a sorted list, because two seats of equal
            // height are equal by design and a sort would be picking between them arbitrarily.
            for (int n = 3; n <= Most; n++)
            {
                var seats = Lay(n);
                var ladder = Take(n);

                foreach (var a in seats)
                    foreach (var b in seats)
                        if (a.Tall > b.Tall + .001f)
                            Assert.Greater(Rank(ladder, a.Tier), Rank(ladder, b.Tier),
                                           $"a row of {n} stands a humbler chest in a taller seat");
            }
        }

        // --------------------------------------------------------------- the shape
        [Test]
        public void TheRowIsCentredOnNought()
        {
            for (int n = 1; n <= Most; n++)
            {
                var seats = Lay(n);
                float left = float.MaxValue, right = float.MinValue;
                foreach (var seat in seats)
                {
                    left = Mathf.Min(left, seat.X - seat.Width * .5f);
                    right = Mathf.Max(right, seat.X + seat.Width * .5f);
                }

                Assert.AreEqual(0f, (left + right) * .5f, .01f, $"a row of {n} is not centred");
            }
        }

        /// <summary>
        /// Every neighbour stands exactly the fraction apart that was asked for — and the sign
        /// is the whole of what the two screens differ by. The hub's box is a picture of a pack
        /// and presses the chests together; the tasks page's ladder is four <em>buttons</em> and
        /// stands them apart, because overlapping targets have edges that belong to whichever
        /// was drawn last.
        /// </summary>
        [Test]
        public void EveryNeighbourStandsTheAskedFractionApart()
        {
            foreach (float fraction in new[] { Overlap, -.05f, 0f })
            {
                var seats = ChestPack.Lay(Take(4), Tall, Short, Dip, Floor, fraction);
                for (int i = 1; i < seats.Length; i++)
                {
                    float gap = (seats[i].X - seats[i - 1].X)
                              - (seats[i].Width + seats[i - 1].Width) * .5f;
                    float want = -Mathf.Min(seats[i].Width, seats[i - 1].Width) * fraction;

                    Assert.AreEqual(want, gap, .01f, $"seats {i - 1} and {i} do not stand as asked at {fraction}");
                    Assert.AreEqual(Mathf.Sign(-fraction), fraction == 0f ? Mathf.Sign(-fraction) : Mathf.Sign(gap),
                                    $"a fraction of {fraction} did not come out on the right side of touching");
                }
            }
        }

        [Test]
        public void TheArchIsSymmetricAndItsFeetDip()
        {
            var seats = Lay(4);

            Assert.AreEqual(seats[0].Tall, seats[3].Tall, .001f, "the ends are not the same height");
            Assert.AreEqual(seats[1].Tall, seats[2].Tall, .001f, "the crest is not level");
            Assert.AreEqual(-seats[0].X, seats[3].X, .01f, "the row is not symmetric about its middle");

            Assert.AreEqual(Short, seats[0].Tall, .001f, "an end is not the short height");
            Assert.Greater(seats[1].Tall, seats[0].Tall, "the middle is not taller than the end");
            Assert.Less(seats[1].Foot, seats[0].Foot, "the middle does not stand lower than the end");
            Assert.AreEqual(Floor, seats[0].Foot, .001f, "an end does not stand on the floor");
        }

        /// <summary>
        /// <b>The tie between two equal seats may not be decided by rounding.</b> The bell used
        /// to be read off a signed position, <c>i / (n-1) * 2 - 1</c>, which gives -.33333334
        /// and .33333337 for a row of four: the two middle seats differ by a float's last digit,
        /// the sort that hands out the crest sees a difference where the design says there is
        /// none, and which side the grandest chest stands on is decided by rounding noise — on
        /// two screens, and differently on two runtimes (a phone runs IL2CPP).
        /// </summary>
        [Test]
        public void TwoSeatsOfEqualHeightAreEqualToTheBit()
        {
            var seats = Lay(4);
            Assert.AreEqual(seats[1].Tall, seats[2].Tall,
                            "the two crest seats are not bit-identical, so the crest is decided by rounding");

            // ... and the answer is therefore the same every time it is asked.
            for (int i = 0; i < 8; i++)
            {
                var again = Lay(4);
                for (int k = 0; k < again.Length; k++)
                    Assert.AreSame(seats[k].Tier, again[k].Tier, "the seating is not deterministic");
            }
        }

        // --------------------------------------------------------------- the sprite
        /// <summary>
        /// The drawn chest and the sprite that holds it. The closed icon is frame nought of the
        /// opening reel, so it carries the lid's headroom and cannot be trimmed — and every
        /// screen that draws a pack has to convert, or the row stands a quarter of a chest apart
        /// and floats a quarter of a chest high.
        /// </summary>
        [Test]
        public void TheSpriteBoxHoldsTheDrawnChestAndHangsHigherThanIt()
        {
            var seat = Lay(4)[1];

            Assert.Greater(seat.Box.y, seat.Tall, "the sprite's box is not taller than the chest drawn in it");
            Assert.AreEqual(seat.Tall / ChestPack.Fill, seat.Box.y, .01f, "the box is not the drawn height over the fill");
            Assert.AreEqual(seat.Box.y * ChestPack.Aspect, seat.Box.x, .01f, "the box does not keep the sprite's aspect");
            Assert.Greater(seat.Anchor.y, seat.Middle.y, "the sprite does not hang higher than the chest it draws");
            Assert.AreEqual(seat.Tall * ChestPack.Lift, seat.Anchor.y - seat.Middle.y, .01f, "the lift is not the drawn height's own");
        }

        [Test]
        public void AnEmptyLadderLaysOutNothingRatherThanThrowing()
        {
            Assert.AreEqual(0, ChestPack.Lay(new List<ChestTier>(), Tall, Short, Dip, Floor, Overlap).Length);
            Assert.AreEqual(0, ChestPack.Lay(null, Tall, Short, Dip, Floor, Overlap).Length);
        }

        [Test]
        public void ADrawOrderRunsShortestFirst()
        {
            var order = ChestPack.InDrawOrder(Lay(4));
            for (int i = 1; i < order.Length; i++)
                Assert.LessOrEqual(order[i - 1].Tall, order[i].Tall,
                                   "an arch drawn out of order is an arch drawn back to front");
        }
        // --------------------------------------------------------------- the panel
        /// <summary>
        /// Every chest's odds panel fits the shortest canvas this game is drawn on, title and
        /// all. A modal is centred, so a panel that outgrows
        /// <see cref="Layout.PanelStack.TallestPanel"/> draws its own ribbon off the top of the
        /// screen — and <b>the thing that decides this height is content</b>: the panel grew a
        /// row per band, and how many bands a chest holds is a line in <c>progression.json</c>
        /// that ships without a build.
        /// </summary>
        [Test]
        public void EveryChestsPanelFitsTheShortestCanvas()
        {
            foreach (var tier in Tiers)
            {
                float height = ChestOddsOverlay.Height(tier);
                Assert.LessOrEqual(height, Layout.PanelStack.TallestPanel,
                                   $"the {tier.Id} chest's panel is taller than a centred panel may be");
                Assert.Greater(height, 600f, $"the {tier.Id} chest's panel is suspiciously short");
            }
        }

        /// <summary>
        /// And it is counted rather than reserved, which is the whole reason a wood chest's
        /// panel does not carry two rows of nothing where a royal chest's prizes would be.
        /// </summary>
        [Test]
        public void APanelIsAsTallAsWhatItHasToSay()
        {
            ChestTier fewest = null, most = null;
            foreach (var tier in Tiers)
            {
                int rows = tier.Chest.Guaranteed.Count + tier.Chest.Options.Count;
                if (fewest == null || rows < fewest.Chest.Guaranteed.Count + fewest.Chest.Options.Count) fewest = tier;
                if (most == null || rows > most.Chest.Guaranteed.Count + most.Chest.Options.Count) most = tier;
            }

            Assert.Less(ChestOddsOverlay.Height(fewest), ChestOddsOverlay.Height(most),
                        "a panel with fewer prizes on it is not shorter, so one of them has a hole in it");
        }
    }
}
