using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The colour legend above a grove: what it says, and whether it fits where it is drawn.
    ///
    /// <para>
    /// <c>FallLegendTests</c>' argument for the second mode, and it is <c>ChapterMap</c>'s for
    /// the seventh time — whether two things on a screen overlap is a sum, and every time in
    /// this project that it was a paragraph instead, the paragraph was wrong. What makes this
    /// one worth proving twice over is that the legend does not merely sit beside the grove: it
    /// takes room <em>off</em> it, so a number here being wrong shrinks the board on the
    /// shortest screen this game is drawn on rather than merely colliding with something.
    /// </para>
    /// </summary>
    public sealed class BudLegendTests
    {
        // ------------------------------------------------------------------ the recipes
        /// <summary>
        /// Derived from the same <c>|</c> on the same masks that <c>BudBoard</c> mixes with, so
        /// there is no table here to fall out of step with the rule it describes.
        /// </summary>
        [Test]
        public void EveryRecipeIsTheGrovesOwnArithmetic()
        {
            Assert.AreEqual(3, BudMixing.Recipes.Count, "three pairs of three pure colours");

            foreach (var recipe in BudMixing.Recipes)
            {
                Assert.AreEqual(recipe.Flower | recipe.Hand, recipe.Made,
                                "what a tap makes is the two of them together, which is the mix");

                Assert.AreNotEqual(recipe.Flower, recipe.Hand,
                                   "a flower tapped with its own colour changes nothing, so it " +
                                   "is not a recipe anybody needs telling about");

                Assert.AreNotEqual(Energy.All, recipe.Made,
                                   "white is the one the board says rather than the legend");
            }
        }

        [Test]
        public void BothSidesOfARecipeArePureColours()
        {
            foreach (var recipe in BudMixing.Recipes)
            {
                Assert.AreEqual(1, Channels(recipe.Flower), "the flower on the board is pure");
                Assert.AreEqual(1, Channels(recipe.Hand), "and so is the colour in hand");
                Assert.AreEqual(2, Channels(recipe.Made), "and what they make is a blend");
            }
        }

        [Test]
        public void TheThreeBlendsAreAllDifferent()
        {
            var seen = new HashSet<int>();
            foreach (var recipe in BudMixing.Recipes)
                Assert.IsTrue(seen.Add(recipe.Made),
                              "three pairs of three colours make three different blends");
        }

        static int Channels(int mask)
        {
            int n = 0;
            if ((mask & Energy.R) != 0) n++;
            if ((mask & Energy.G) != 0) n++;
            if ((mask & Energy.B) != 0) n++;
            return n;
        }

        // ------------------------------------------------------------------ where it sits
        [Test]
        public void TheLegendFitsAcrossTheNarrowestScreenThisGameIsDrawnOn()
        {
            Assert.IsTrue(BudBand.LegendFits,
                          $"three recipes come to {BudBand.LegendWidth:0} wide, which does not " +
                          $"fit inside {BudBand.Canvas - BudBand.SideInset * 2f:0} — the outer " +
                          "two would run off a 4:3 portrait screen, which is the shape nobody " +
                          "happens to have open");
        }

        [Test]
        public void OneRecipeIsLaidOutWithClearAirBetweenEveryPiece()
        {
            Assert.IsTrue(BudBand.ChipIsClear(out var fault), fault);
        }

        [Test]
        public void TheChipsClearEachOther()
        {
            for (int i = 1; i < BudBand.Chips; i++)
            {
                float gap = BudBand.ChipCentre(i) - BudBand.ChipCentre(i - 1) - BudBand.ChipWidth;
                Assert.GreaterOrEqual(gap, 1f,
                                      $"recipes {i - 1} and {i} leave {gap:0} between them");
            }
        }

        /// <summary>
        /// Each recipe is drawn on a card of its own, and three cards have to read as three.
        ///
        /// One long plate behind all nine flowers shipped and was reported as confusing — a row
        /// of thirteen things inside one border, with the groupings left to the eye. The card is
        /// what does that work, so its width has to sit between what it holds and the slot it
        /// sits in, and the air between two of them has to be enough to read as a break rather
        /// than as a seam.
        /// </summary>
        [Test]
        public void EachRecipeGetsACardAndTheCardsReadAsThree()
        {
            Assert.IsTrue(BudBand.CardsAreClear(out var fault), fault);
        }

        /// <summary>
        /// The legend takes the strip the grove used to start at, and the grove starts under it.
        ///
        /// <b>Both numbers are one number.</b> Where the board begins is the legend's foot plus
        /// its air, derived rather than typed, because two copies of it is exactly how a legend
        /// comes to be drawn through the top row of a grid — <c>PanelStack</c>'s lesson, which
        /// this project has now paid for in three separate files.
        /// </summary>
        [Test]
        public void TheGroveStartsBelowTheLegendRatherThanUnderIt()
        {
            Assert.AreEqual(BudBand.LegendTop + BudBand.LegendHeight + BudBand.LegendGap,
                            BudBand.BoardCeiling, .001f);

            Assert.Greater(BudBand.BoardCeiling,
                           BudBand.LegendCentre + BudBand.LegendHeight * .5f,
                           "the board's ceiling is below the legend's foot");

            Assert.AreEqual(BudBand.LegendTop + BudBand.LegendHeight * .5f,
                            BudBand.LegendCentre, .001f,
                            "the centre a Box is given and the top the ceiling is measured " +
                            "from describe the same plate — UIKit.Box pivots at centre whatever " +
                            "it is anchored to, which is the one conversion a caller forgets");
        }

        /// <summary>
        /// And the grove still gets more room than the two things sandwiching it, on the
        /// shortest canvas this game is drawn on — portrait 4:3, so 1440 reference units.
        ///
        /// This is the check the legend actually needed: it is cheap to add a strip above a
        /// board and it is invisible until somebody opens the game on the one aspect ratio
        /// where the board had no room to give.
        /// </summary>
        [Test]
        public void TheGroveKeepsMostOfTheShortestScreenItIsDrawnOn()
        {
            const float shortest = 1440f;
            float grove = shortest - BudBand.BoardCeiling - BudBand.BoardFloor;

            Assert.Greater(grove, shortest * .40f,
                           $"the grove is left {grove:0} of {shortest:0} units once the header, " +
                           "the legend and the band have taken theirs");

            Assert.Greater(grove - BudBand.BandHeight, 500f,
                           "and enough of that is above the band to draw a six-wide grid at a " +
                           "size a thumb can hit");
        }

        // ------------------------------------------------------------------ the key row
        /// <summary>
        /// The hint key clears the band above it and its caption clears the key.
        ///
        /// <b>Whether two things on a screen overlap is arithmetic</b> (invariant 8a's rule, for
        /// the fourth time in this file), and this row arrived under a band that was already the
        /// tightest thing on the screen: the grove's floor is now <em>derived</em> from the row's
        /// height rather than typed beside it, which is what stops a taller key being drawn
        /// through the taps counter.
        /// </summary>
        [Test]
        public void TheHintKeyClearsTheBandAndItsOwnCaption()
        {
            Assert.IsTrue(BudBand.Clears,
                          $"the key row is {BudBand.KeyBarHeight:0} tall with its key centred at " +
                          $"{BudBand.KeyCentre:0} and its caption at {BudBand.KeyCaption:0}, " +
                          $"under a band of {BudBand.BandHeight:0} standing at " +
                          $"{BudBand.BoardFloor:0}");

            Assert.AreEqual(BudBand.KeyBarHeight + BudBand.KeyClear, BudBand.BoardFloor, .001f,
                            "the grove's floor is derived from the row under it rather than " +
                            "typed, so the two cannot come to disagree");
        }

        // ------------------------------------------------------------------ the flower
        /// <summary>
        /// One silhouette for every colour but white, which is the whole of the change the
        /// legend was drawn alongside — and the legend draws its flowers through the same
        /// answer, so a grove and its own explanation cannot come to disagree.
        /// </summary>
        [Test]
        public void EveryFlowerButWhiteIsDrawnWithTheSameNumberOfSides()
        {
            for (int mask = 0; mask < Energy.All; mask++)
                Assert.AreEqual(BudFlower.SidesOrdinary, BudFlower.Sides(mask),
                                $"the flower for mask {mask} is not the shape the rest are");

            Assert.AreEqual(BudFlower.SidesWhite, BudFlower.Sides(Energy.All),
                            "white is the one flower that can never be mixed into again, so it " +
                            "is the one that has to look different");

            Assert.AreNotEqual(BudFlower.SidesOrdinary, BudFlower.SidesWhite);
        }
    }
}
