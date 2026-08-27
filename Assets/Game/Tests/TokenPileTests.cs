using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The shop's heaps of coins, gems and hearts.
    ///
    /// <para>
    /// A composition is the one kind of fault a compile, a validator and a screenshot of the
    /// source all agree looks fine, so what can be stated about one is stated here: it is
    /// symmetric, it fits its picture, every token appears exactly once, and it is handed back
    /// in an order that draws the front row last. The arrangement it replaced failed the first
    /// of those on every even count and nothing said so for as long as the shop has existed.
    /// </para>
    /// </summary>
    public sealed class TokenPileTests
    {
        /// <summary>Every count the shop actually piles: coins and gems, then the hearts.</summary>
        static readonly int[] Counts = { 1, 2, 3, 4, 5, 6, 8 };

        [Test]
        public void EveryPileIsSymmetricAboutItsCentre()
        {
            foreach (int total in Counts)
            {
                var spots = TokenPile.Of(total, 50f);

                foreach (var spot in spots)
                {
                    bool mirrored = false;

                    foreach (var other in spots)
                        if (System.Math.Abs(other.X + spot.X) < .001f
                            && System.Math.Abs(other.Y - spot.Y) < .001f)
                            mirrored = true;

                    Assert.IsTrue(mirrored,
                                  $"a pile of {total} has a token at {spot.X} with nothing " +
                                  "opposite it");
                }
            }
        }

        /// <summary>
        /// The lean is symmetric too. It was not: the arrangement this replaced dropped every
        /// second token, which is only a symmetric rule when the count is odd — so a pile of
        /// four came out heavier on one side and a pile of five did not, from one expression.
        /// </summary>
        [Test]
        public void SoIsTheLean()
        {
            foreach (int total in Counts)
            {
                float sum = 0f;
                foreach (var spot in TokenPile.Of(total, 50f)) sum += spot.Tilt;

                Assert.AreEqual(0f, sum, .001f, $"a pile of {total} leans one way overall");
            }
        }

        /// <summary>Nothing is drawn twice and nothing is missed.</summary>
        [Test]
        public void EveryTokenGetsExactlyOneSlot()
        {
            foreach (int total in Counts)
            {
                var seen = new bool[total];

                foreach (var spot in TokenPile.Of(total, 50f))
                {
                    Assert.IsFalse(seen[spot.Slot], $"slot {spot.Slot} came back twice");
                    seen[spot.Slot] = true;
                }

                foreach (bool taken in seen) Assert.IsTrue(taken);
            }
        }

        /// <summary>
        /// Slots run along the front row first, so a caller filling a mixed pile puts its gems
        /// where they can be seen. The draw order is the opposite way round, which is why the
        /// two are separate things.
        /// </summary>
        [Test]
        public void TheFrontRowIsDrawnLastAndNumberedFirst()
        {
            foreach (int total in Counts)
            {
                int front = TokenPile.FrontRow(total);
                var spots = TokenPile.Of(total, 50f);

                for (int i = 0; i < spots.Length; i++)
                {
                    bool inFront = spots[i].Slot < front;

                    Assert.AreEqual(inFront, i >= total - front,
                                    $"a pile of {total} draws slot {spots[i].Slot} at {i}");

                    if (inFront) Assert.Less(spots[i].Y, .001f, "the front row is not in front");
                }
            }
        }

        /// <summary>
        /// Each row is laid from its ends inwards, so the middle of a row is drawn over its
        /// neighbours and the overlap reads the same from either side. Left to right shingles
        /// the whole pile one way, which is the tell of a heap that was not composed.
        /// </summary>
        [Test]
        public void EachRowIsDrawnFromItsEndsInwards()
        {
            var spots = TokenPile.Of(8, 50f);
            float last = float.MaxValue;

            // The front row of five is the tail of the order: |x| must fall, never rise.
            for (int i = 8 - TokenPile.FrontRow(8); i < spots.Length; i++)
            {
                float from = System.Math.Abs(spots[i].X);

                Assert.LessOrEqual(from, last + .001f, "the row was not laid from the outside in");
                last = from;
            }
        }

        /// <summary>
        /// The front row is the larger half, so a heap is a pyramid rather than a column with
        /// a hat on. A single token, and a pair, are one row.
        /// </summary>
        [Test]
        public void APileIsWiderAtTheFront()
        {
            foreach (int total in Counts)
            {
                int front = TokenPile.FrontRow(total);

                Assert.GreaterOrEqual(front, total - front, $"a pile of {total} is top-heavy");
                Assert.LessOrEqual(front - (total - front), 2, $"a pile of {total} is a stalk");
            }

            Assert.AreEqual(1, TokenPile.FrontRow(1));
            Assert.AreEqual(2, TokenPile.FrontRow(2));
            Assert.AreEqual(0, TokenPile.Of(0, 50f).Length);
        }

        /// <summary>
        /// Every heap the shop draws fits the picture box it is drawn in. The token sizes are
        /// <c>ShopArt</c>'s and the box is the square it paints into, so this is the check that
        /// a rung added to a ladder cannot quietly overrun the card.
        /// </summary>
        [Test]
        public void EveryHeapTheShopDrawsFitsItsPicture()
        {
            const float Box = 236f;      // ShopArt paints into a square of the card's own art box

            Fits(2, .34f, Box);          // the coin and gem ladders
            Fits(3, .34f, Box);
            Fits(4, .27f, Box);
            Fits(5, .27f, Box);
            Fits(6, .23f, Box);
            Fits(8, .23f, Box);

            Fits(1, .68f, Box);          // a heart pack
            Fits(3, .44f, Box);
            Fits(5, .36f, Box);

            Fits(3, .26f, Box);          // a heart container's spill
            Fits(4, .26f, Box);
            Fits(5, .26f, Box);
        }

        static void Fits(int total, float fraction, float box)
        {
            float width = TokenPile.Width(total, box * fraction);

            Assert.LessOrEqual(width, box,
                               $"a heap of {total} at {fraction} of the box is {width} across");
        }
    }
}
