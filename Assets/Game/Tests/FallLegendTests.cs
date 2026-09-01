using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The two things a well says to the player that are not the board itself: the colour
    /// arithmetic under the tray, and the chain it counts out loud.
    ///
    /// <para>
    /// Both are arithmetic and both would otherwise have lived in a <c>MonoBehaviour</c>, which
    /// is the one place in this project nothing can be proved. The legend's geometry is
    /// <c>ChapterMap</c>'s argument for the fifth time — whether two things on a screen overlap
    /// is a sum, and every time it was a paragraph instead the paragraph was wrong.
    /// </para>
    /// </summary>
    public sealed class FallLegendTests
    {
        // ------------------------------------------------------------------ the recipes
        /// <summary>
        /// Derived from the same masks the board mixes with, so there is no table to fall out of
        /// step with the rules. A hand-written "yellow needs blue" is a second answer waiting to
        /// be wrong.
        /// </summary>
        [Test]
        public void EveryRecipeIsTheBoardsOwnArithmetic()
        {
            Assert.AreEqual(3, FallMixing.Recipes.Count, "three pairs of three pure colours");

            foreach (var recipe in FallMixing.Recipes)
            {
                Assert.AreEqual(recipe.First | recipe.Second, recipe.Blend,
                                "the blend is the two of them together, which is what a drop does");

                Assert.AreEqual(Energy.All, recipe.Blend | recipe.Finish,
                                "and the finisher is whatever is left, which is what bursts it");

                Assert.AreNotEqual(recipe.First, recipe.Second);
                Assert.AreEqual(0, recipe.Finish & recipe.Blend,
                                "a blend cannot be finished by a colour it already holds");
            }
        }

        [Test]
        public void TheThreeBlendsAreAllDifferent()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var recipe in FallMixing.Recipes) Assert.IsTrue(seen.Add(recipe.Blend));
        }

        // ------------------------------------------------------------------ where it sits
        [Test]
        public void TheLegendSitsInsideTheBandBelowTheBoardOnEveryShapeOfDisplay()
        {
            // Both shapes, not the one that shipped. The short band is the whole reason this
            // file has a second case at all: it moves the legend, its size and the floor above
            // it together, and the failure it can produce — a legend drawn under the tray — is
            // one nothing else here could see.
            foreach (bool shortCanvas in new[] { false, true })
            {
                var band = FallBand.Of(shortCanvas);
                string where = shortCanvas ? "on a short display" : "on a phone";

                Assert.Less(band.LegendTop, band.BoardFloor,
                            "the legend runs up into the well's own floor " + where + ", so it " +
                            "would be drawn under the tray");

                Assert.Greater(band.LegendBottom, FallBand.BottomClearance,
                               "and down into the home indicator " + where);
            }
        }

        /// <summary>
        /// The shipped band, stated as itself. A short display is allowed to differ; a phone is
        /// not, and this is the case that says so — the tablet change was made under exactly
        /// that promise.
        /// </summary>
        [Test]
        public void APhoneGetsTheBandThatShipped()
        {
            var band = FallBand.Of(false);

            Assert.AreEqual(1f, band.LegendScale, .0001f);
            Assert.AreEqual(1f, band.TrayScale, .0001f);
            Assert.AreEqual(FallBand.LegendCentre, band.LegendCentre, .0001f);
            Assert.AreEqual(FallBand.BoardFloor, band.BoardFloor, .0001f);
            Assert.AreEqual(FallBand.TrayHeight, band.TrayHeight, .0001f);
        }

        /// <summary>
        /// What the short band is <em>for</em>: a 6x10 well is bound by height, so the furniture
        /// under it is charged straight to the cell. The numbers below are the shape reported
        /// from an iPad — 350 of header, the band, the tray, and 24 of home indicator — and what
        /// is asserted is that the change is worth making and does not go so far that a legend
        /// or a tray stops being legible.
        /// </summary>
        [Test]
        public void AShortDisplayGivesAHeightBoundWellItsRoomBack()
        {
            var phone = FallBand.Of(false);
            var squat = FallBand.Of(true);

            Assert.Less(squat.BoardFloor, phone.BoardFloor, "the floor has to come down");
            Assert.Less(squat.TrayHeight, phone.TrayHeight, "and the tray with it");

            float given = (phone.BoardFloor - squat.BoardFloor)
                        + (phone.TrayHeight - squat.TrayHeight);

            Assert.Greater(given, 150f,
                           "a band that gives back less than this is not worth a second shape: "
                           + "the well it is for loses a cell for every ten units of it");

            // Nothing is scaled away to the point of being unreadable. A short display is a
            // physically large one, so what looks small as a fraction of the screen is still
            // larger in the hand than the phone it was tuned on — but only within reason.
            Assert.GreaterOrEqual(squat.LegendScale, .6f);
            Assert.GreaterOrEqual(squat.TrayScale, .6f);
            Assert.LessOrEqual(squat.LegendScale, 1f);
            Assert.LessOrEqual(squat.TrayScale, 1f);
        }

        [Test]
        public void TheThreeRecipesFitTheNarrowestScreenThisGameIsDrawnOn()
        {
            float usable = FallBand.Canvas - FallBand.SideInset * 2f;

            foreach (bool shortCanvas in new[] { false, true })
                Assert.LessOrEqual(FallBand.Of(shortCanvas).LegendWidth, usable,
                                   "the outer recipes run off the narrowest canvas this game " +
                                   "is drawn on, which is a phone's — a widened one is never " +
                                   "narrower, so this is the case that binds");
        }

        [Test]
        public void TheRecipesAreLaidOutLeftToRightWithoutTouching()
        {
            for (int i = 1; i < FallBand.Chips; i++)
            {
                float gap = FallBand.ChipCentre(i) - FallBand.ChipCentre(i - 1);

                Assert.AreEqual(FallBand.ChipWidth + FallBand.ChipGap, gap, .001f,
                                "recipe " + i + " overlaps the one before it");
            }

            Assert.AreEqual(0f, FallBand.ChipCentre(0) + FallBand.ChipCentre(FallBand.Chips - 1),
                            .001f, "the row should be centred on the screen");
        }

        // ------------------------------------------------------------------ the chain ladder
        /// <summary>
        /// A single burst is most of what a drop does, so counting from one would put a number
        /// on the screen almost every turn and mean nothing by the second level.
        /// </summary>
        [Test]
        public void OneBurstIsNotAChainAndIsNeitherCountedNorNamed()
        {
            Assert.IsFalse(FallChain.Counts(1));
            Assert.IsNull(FallChain.WordKey(1));
            Assert.IsNull(FallChain.WordKey(2), "two is counted, and not yet worth a word");
            Assert.IsTrue(FallChain.Counts(2));
        }

        [Test]
        public void EveryNamedChainHasAWordAndTheyAreAllDifferent()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int waves = FallChain.NameFrom; waves <= 8; waves++)
            {
                string key = FallChain.WordKey(waves);
                Assert.IsNotNull(key, waves + " waves is named and has no word");
                seen.Add(key);
            }

            Assert.AreEqual(4, seen.Count,
                            "four words: three rungs and everything past them");

            Assert.AreEqual(FallChain.WordKey(6), FallChain.WordKey(30),
                            "the ladder tops out rather than running off the end of the table");
        }

        [Test]
        public void ABiggerChainIsDrawnBiggerAndNeverOffTheScreen()
        {
            int last = 0;

            for (int wave = 1; wave <= 30; wave++)
            {
                int points = FallChain.PointsFor(wave);

                Assert.GreaterOrEqual(points, last, "wave " + wave + " shrank");
                Assert.LessOrEqual(points, 150, "wave " + wave + " is wider than the well");
                last = points;
            }

            Assert.Greater(FallChain.WordPointsFor(5), FallChain.WordPointsFor(3),
                           "a legendary should land harder than an amazing");
        }

        [Test]
        public void TheTierClimbsAndThenHolds()
        {
            Assert.AreEqual(0, FallChain.Tier(1), "below a chain there is no tier to be on");
            Assert.AreEqual(0, FallChain.Tier(2));
            Assert.AreEqual(FallChain.TopTier, FallChain.Tier(6));
            Assert.AreEqual(FallChain.TopTier, FallChain.Tier(40),
                            "past the top the ladder holds rather than running away");
        }

        // ------------------------------------------------------------------ what it costs
        /// <summary>
        /// The count rides inside the cascade's own budget, so a chain still cannot outstay the
        /// ceiling that keeps the board from freezing. The word is the one beat allowed outside
        /// it — and it is bounded too.
        /// </summary>
        [Test]
        public void TheCelebrationIsBoundedHoweverFarAChainRuns()
        {
            for (int waves = 1; waves <= 60; waves++)
                Assert.LessOrEqual(FallTempo.CountPop(waves), FallTempo.Wave(waves) + .0001f,
                                   waves + ": a count that outlasts its own wave is still " +
                                   "arriving as the next one lands");

            Assert.Less(FallTempo.Fanfare, 1f, "the pay-off is a moment, not a cutscene");
        }
    }
}
