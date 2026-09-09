using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using NUnit.Framework;

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
