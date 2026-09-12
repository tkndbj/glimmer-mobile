using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The ten grounds a siege chapter is fought over, and the one thing about them that can
    /// silently come apart.
    ///
    /// <para>
    /// <b>A rung's ground is named twice</b> — once in <see cref="SiegeMode.Ground"/>, which is
    /// what a chapter <em>loads</em>, and once in <c>SiegeView.GroundAddress</c>, which is what a
    /// level <em>draws</em>. They are two switches of ten literals each because
    /// <c>Tools/verify/artnames.py</c> reads the literal at a lookup's call site and a key built
    /// from an index is ten names nothing checks (invariant 7). The price of that is the fault
    /// this fixture exists for: if the two disagree about a rung, the chapter loads one floor and
    /// the board asks for another, and an <c>Image</c> with a null sprite is a <b>white
    /// rectangle</b> rather than a blank (7b) — over the whole hill, on one rung, with every gate
    /// green. Nothing else in this project would see it: the address is real, it is registered, it
    /// is audited, and it is even loaded — by a different level.
    /// </para>
    /// <para>
    /// The view's half is Presentation and this assembly reaches both, which is the only reason
    /// the comparison can be made at all.
    /// </para>
    /// </summary>
    public sealed class SiegeGroundTests
    {
        /// <summary>
        /// The shape <c>make_siege_ground</c> authors a hill at. Written down here rather than
        /// read off a sprite because an offline run loads nothing, so a sprite lookup answers null
        /// whatever is on disk and a check that cannot fail is not a check
        /// (<c>SkinsTests</c>' bargain). <c>make_siege_art.py --check</c> is what holds the ten
        /// PNGs to it.
        /// </summary>
        const float GroundAspect = 1024f / 788f;

        /// <summary>
        /// <b>The ground is never drawn at an aspect other than its own.</b>
        ///
        /// <para>
        /// It was, on every device this game runs on, for as long as the hill has existed: the
        /// band's aspect is <b>0.99</b> on a 21:9 phone and <b>2.85</b> on an iPad, the art was
        /// authored at 0.80, and a plain <c>Image</c> sized to the band scales x and y
        /// independently — so every square rock plate arrived as a wide rectangle, by 1.42x on the
        /// shape most players hold. Nothing offline could see it (every gate reads the model) and
        /// <c>render_siege.py</c> stretched it identically, so the mirror agreed with the game and
        /// drew a picture that looked composed (invariant 44d). A player reported it.
        /// </para>
        /// <para>
        /// Swept over the screen shapes rather than asserted at one, because the fault is
        /// per-shape and the band's aspect is the thing that varies.
        /// </para>
        /// </summary>
        [Test]
        public void TheGroundCoversItsBandAndIsNeverDrawnStretched()
        {
            foreach (var band in new[]
                     {
                         new Vector2(1500f, 526f),   // 4:3 tablet, the widest band there is
                         new Vector2(1044f, 591f),   // 16:9
                         new Vector2(1044f, 778f),   // 18:9
                         new Vector2(1044f, 917f),   // 19.5:9, the shape most players hold
                         new Vector2(1044f, 1056f),  // 21:9, the tallest band there is
                     })
            {
                var drawn = SiegeView.GroundSize(band, GroundAspect);

                Assert.That(drawn.x / drawn.y, Is.EqualTo(GroundAspect).Within(1e-4f),
                            "the ground is stretched on a band of " + band);

                Assert.That(drawn.x, Is.GreaterThanOrEqualTo(band.x - 1e-3f),
                            "the ground leaves bare plate either side on a band of " + band);
                Assert.That(drawn.y, Is.GreaterThanOrEqualTo(band.y - 1e-3f),
                            "the ground leaves bare plate above or below on a band of " + band);

                // Smallest such rectangle: one axis meets the band exactly, so nothing is cropped
                // that did not have to be.
                Assert.That(Mathf.Min(drawn.x - band.x, drawn.y - band.y), Is.LessThan(1e-3f),
                            "the ground is drawn larger than covering needs on " + band);
            }
        }

        /// <summary>
        /// A sprite that failed to load has no aspect to keep, and the honest answer is the band
        /// itself — a <b>white rectangle over the whole hill</b>, which is what a missing address
        /// looks like everywhere else here (invariant 7b) and is the one thing that must not be
        /// hidden by drawing nothing at all.
        /// </summary>
        [Test]
        public void AGroundThatCouldNotBeLoadedStillFillsItsBand()
        {
            var band = new Vector2(1044f, 917f);

            Assert.That(SiegeView.GroundSize(band, 0f), Is.EqualTo(band));
            Assert.That(SiegeView.GroundSize(band, -1f), Is.EqualTo(band));
        }

        [Test]
        public void EveryRungOfAChapterAsksForAGroundOfItsOwn()
        {
            var seen = new HashSet<string>();

            for (int rung = 0; rung < SiegeMode.Grounds; rung++)
                Assert.That(seen.Add(SiegeMode.Ground(rung).Address), Is.True,
                            "rung " + rung + " repeats a ground already spent");
        }

        /// <summary>
        /// <b>The ladder wraps rather than falling off.</b> A chapter longer than ten rungs is
        /// legal — nothing anywhere caps a chapter's length — and the honest answer for its
        /// eleventh is the first ground again, never a name that resolves to nothing.
        /// </summary>
        [Test]
        public void AChapterLongerThanTheLadderWrapsBackToItsFirstGround()
        {
            Assert.That(SiegeMode.Ground(SiegeMode.Grounds).Address,
                        Is.EqualTo(SiegeMode.Ground(0).Address));

            Assert.That(SiegeMode.Ground(SiegeMode.Grounds + 3).Address,
                        Is.EqualTo(SiegeMode.Ground(3).Address));
        }

        /// <summary>
        /// A rung is a place in a list and can never be negative, but <see cref="SiegeMode.Ground"/>
        /// is public and a caller that has not found its level yet may well hold -1. It answers a
        /// real ground rather than throwing, because a floor is not worth a crash.
        /// </summary>
        [Test]
        public void ARungBeforeTheStartStillNamesAGround()
        {
            Assert.That(SiegeMode.Ground(-1).Address, Is.EqualTo(SiegeMode.Ground(9).Address));
            Assert.That(SiegeMode.Ground(-11).Address, Is.EqualTo(SiegeMode.Ground(9).Address));
        }

        /// <summary>
        /// <b>What the chapter loads is what the board draws.</b> The comparison this fixture is
        /// for — see the class note.
        /// </summary>
        [Test]
        public void TheGroundAChapterLoadsIsTheGroundTheBoardDraws()
        {
            for (int rung = 0; rung < SiegeMode.Grounds + 4; rung++)
                Assert.That(SiegeView.GroundAddress(rung),
                            Is.EqualTo(SiegeMode.Ground(rung).Address),
                            "rung " + rung + ": the view and the mode name different ground");
        }

        /// <summary>
        /// Every ground is a Thornwatch address, so it lands in this mode's own Addressables group
        /// rather than in the global set — which is what bounds the memory by what is on screen
        /// (invariant 7b) instead of by how many places the game has ever shipped.
        /// </summary>
        [Test]
        public void EveryGroundIsAddressedUnderThisModesOwnArt()
        {
            for (int rung = 0; rung < SiegeMode.Grounds; rung++)
                Assert.That(SiegeMode.Ground(rung).Address,
                            Is.EqualTo(AssetManifest.SiegeArt(
                                "hill" + (((rung % SiegeMode.Grounds) + SiegeMode.Grounds)
                                          % SiegeMode.Grounds + 1))));
        }
    }
}
