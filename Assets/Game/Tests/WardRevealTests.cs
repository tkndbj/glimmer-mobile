using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The turret reveal's arithmetic: where its bands sit, and how loud each rung is.
    ///
    /// <para>
    /// <b>This exists because nothing else in the project can look at that screen.</b> There is
    /// a render for the hub, the shop, the siege and Prismvale, and each of them has caught
    /// faults every numeric gate was green through — a widget hanging off a plate, a readout
    /// behind another one, a band collapsing on a shape nobody drew. There is no render for a
    /// modal ceremony, so the one fault that would ship unseen here is a caption drawn through
    /// the keyline above it. Checking the edges is a poor substitute for a picture and it is
    /// what there is; if this screen ever gets a render, that is the better instrument.
    /// </para>
    /// </summary>
    public sealed class WardRevealTests
    {
        /// <summary>The canvas the reveal is laid out on: 1080 wide by <c>Boot.RefHeight</c>.</summary>
        const float Top = Boot.RefHeight * .5f, Bottom = -Boot.RefHeight * .5f;

        /// <summary>Top and bottom edge of a band stated as a middle, which is how the file writes them.</summary>
        static (float top, float bottom) Edges(float middle, float height)
            => (middle + height * .5f, middle - height * .5f);

        [Test]
        public void TheBandsDescendAndNoneOfThemOverlaps()
        {
            var bands = new (string name, float top, float bottom)[]
            {
                Named("plate", WardRevealOverlay.PlateY, 700f),
                Named("pips", WardRevealOverlay.PipY, 26f),
                Named("name", WardRevealOverlay.NameY, WardRevealOverlay.NameH),
                Named("rule", WardRevealOverlay.RuleY, WardRevealOverlay.RuleH),
                Named("note", WardRevealOverlay.NoteY, WardRevealOverlay.NoteH),
                Named("key", WardRevealOverlay.ActY, WardRevealOverlay.ActH),
            };

            for (int i = 1; i < bands.Length; i++)
                Assert.Less(bands[i].top, bands[i - 1].bottom,
                            $"{bands[i].name} runs into {bands[i - 1].name}");
        }

        [Test]
        public void NothingIsDrawnOffTheCanvas()
        {
            // Both ends, because the two failures look completely different: a plate off the top
            // is a picture with its head cut off, and a key off the bottom is a control nobody
            // can press.
            Assert.LessOrEqual(WardRevealOverlay.PlateY + 350f, Top, "the plate's top");
            Assert.GreaterOrEqual(WardRevealOverlay.ActY - WardRevealOverlay.ActH * .5f, Bottom,
                                  "the key's foot");
        }

        [Test]
        public void EveryBandIsFarEnoughFromTheOneAboveToRead()
        {
            // Touching is not the bar. A wrapped ability note whose first line starts on the
            // keyline above it is legible and reads as a mistake, which is the whole reason
            // these numbers were re-spaced rather than merely made not to overlap.
            Assert.GreaterOrEqual(Gap(WardRevealOverlay.RuleY, WardRevealOverlay.RuleH,
                                      WardRevealOverlay.NoteY, WardRevealOverlay.NoteH), 20f,
                                  "the note under the rule");

            Assert.GreaterOrEqual(Gap(WardRevealOverlay.NoteY, WardRevealOverlay.NoteH,
                                      WardRevealOverlay.ActY, WardRevealOverlay.ActH), 20f,
                                  "the key under the note");
        }

        // ------------------------------------------------------------------- tier
        [Test]
        public void EveryTurretOnTheRosterLandsOnARealRung()
        {
            foreach (var model in WardLedger.Catalog.Models)
            {
                if (model == null) continue;

                int tier = WardRevealOverlay.TierOf(model);

                Assert.GreaterOrEqual(tier, 1, model.Id);
                Assert.LessOrEqual(tier, 5, model.Id);
            }
        }

        [Test]
        public void TheLadderClimbsInsideEachCurrencyAndNeverAcrossThem()
        {
            // **The half that matters is the second one.** Half this shelf is priced in gems and
            // half in credits, and 600 gems does not compare with 9,000 credits in either
            // direction — so a tier derived from a price would stand the dearest turrets in the
            // game on the bottom rung looking cheap, which is invariant 16j's trap exactly. The
            // rung is read inside one currency, so the two ladders each climb on their own.
            AssertClimbs(forGems: false);
            AssertClimbs(forGems: true);
        }

        static void AssertClimbs(bool forGems)
        {
            WardModel cheapest = null, dearest = null;

            foreach (var model in WardLedger.Catalog.Models)
            {
                if (model == null || model.IsStarter || model.ForGems != forGems) continue;

                if (cheapest == null || model.Order < cheapest.Order) cheapest = model;
                if (dearest == null || model.Order > dearest.Order) dearest = model;
            }

            Assert.NotNull(cheapest, $"the roster has no {(forGems ? "gem" : "credit")} turrets");

            int low = WardRevealOverlay.TierOf(cheapest);
            int high = WardRevealOverlay.TierOf(dearest);

            Assert.AreEqual(1, low, $"{cheapest.Id} is the cheapest of its ladder");
            Assert.AreEqual(5, high, $"{dearest.Id} is the dearest of its ladder");
        }

        [Test]
        public void TheStarterIsNeverLoud()
        {
            // It is free and nobody is ever shown this screen for it, so the only thing being
            // pinned is that the rung arithmetic cannot divide by an empty ladder.
            foreach (var model in WardLedger.Catalog.Models)
                if (model != null && model.IsStarter)
                    Assert.AreEqual(1, WardRevealOverlay.TierOf(model), model.Id);
        }

        static (string, float, float) Named(string name, float middle, float height)
        {
            var (top, bottom) = Edges(middle, height);
            return (name, top, bottom);
        }

        static float Gap(float upperMiddle, float upperHeight, float lowerMiddle, float lowerHeight)
            => (upperMiddle - upperHeight * .5f) - (lowerMiddle + lowerHeight * .5f);
    }
}
