using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The flourish ladder, held to the one claim that makes it worth having: <b>every rung draws
    /// something the rung below it did not</b>.
    ///
    /// <para>
    /// Groovekeeper's celebration used to be one picture at five sizes — the same flower, ring
    /// and sparks with a bigger swell and a louder knock — which is exactly the mistake Budburst
    /// made and had to be rewritten out of. A number going up is not something anybody sees.
    /// </para>
    /// </summary>
    public sealed class KeeperSpectacleTests
    {
        [Test]
        public void EveryRungDrawsSomethingTheOneBelowItDidNot()
        {
            int had = KeeperSpectacle.For(0, false).Kinds;

            for (int blooms = 1; blooms <= KeeperFlourish.Most; blooms++)
            {
                int now = KeeperSpectacle.For(blooms, false).Kinds;

                Assert.Greater(now, had,
                               blooms + " blooms draws no more kinds of thing than "
                               + (blooms - 1) + " does");
                had = now;
            }
        }

        [Test]
        public void NothingIsEverTakenAway()
        {
            // A layer switching off would read as the flourish running out of steam exactly as
            // it runs hardest.
            var last = KeeperSpectacle.For(0, false);

            for (int blooms = 1; blooms <= KeeperFlourish.Most + 4; blooms++)
            {
                var now = KeeperSpectacle.For(blooms, false);

                Assert.GreaterOrEqual(now.Jolt, last.Jolt);
                Assert.IsTrue(now.Sweep || !last.Sweep);
                Assert.IsTrue(now.Rays || !last.Rays);
                Assert.IsTrue(now.Fireworks || !last.Fireworks);
                Assert.IsTrue(now.Confetti || !last.Confetti);

                last = now;
            }
        }

        [Test]
        public void TheLadderIsSpentInsideWhatTheRulesAllow()
        {
            // Five is a fact about the board rather than a taste, so unlike a chain there is
            // nowhere for a ladder to hide: a rung above the top would be decoration for a
            // flourish nobody can ever make.
            Assert.LessOrEqual(KeeperSpectacle.ConfettiFrom, KeeperFlourish.Most);

            var top = KeeperSpectacle.For(KeeperFlourish.Most, false);
            Assert.IsTrue(top.Confetti, "the top of the ladder is not reachable");

            Assert.AreEqual(top.Kinds, KeeperSpectacle.For(KeeperFlourish.Most + 3, false).Kinds,
                            "past the top rung the ladder holds");
        }

        [Test]
        public void TheCommonestGoodMoveIsAlreadyWorthWatching()
        {
            // One tile, one bed, par advanced. It is the commonest thing a player does on
            // purpose in this mode, and a ladder whose first rung is bare teaches them that most
            // of what they do does not count.
            var bare = KeeperSpectacle.For(1, false);
            var bed = KeeperSpectacle.For(1, true);

            Assert.Greater(bed.Kinds, bare.Kinds);
            Assert.IsTrue(bed.Sweep, "opening a bed draws nothing a bare bloom does not");
        }

        [Test]
        public void ABedCanOnlyEverRaiseTheReading()
        {
            for (int blooms = 0; blooms <= KeeperFlourish.Most + 2; blooms++)
            {
                var bare = KeeperSpectacle.For(blooms, false);
                var bed = KeeperSpectacle.For(blooms, true);

                Assert.GreaterOrEqual(bed.Kinds, bare.Kinds);
                Assert.GreaterOrEqual(bed.Jolt, bare.Jolt);
            }
        }

        [Test]
        public void APlantingThatOpensNothingDrawsNothing()
        {
            var nothing = KeeperSpectacle.For(0, false);

            Assert.AreEqual(0f, nothing.Jolt);
            Assert.IsFalse(nothing.Sweep);
            Assert.IsFalse(nothing.Rays);
            Assert.IsFalse(nothing.Fireworks);
            Assert.IsFalse(nothing.Confetti);
            Assert.AreEqual(0, nothing.Rockets);

            Assert.AreEqual(0f, KeeperTempo.Shake(0, false));
        }

        [Test]
        public void TheKnockIsBoundedAndComesFromTheOneLadder()
        {
            for (int blooms = 0; blooms <= KeeperFlourish.Most + 6; blooms++)
            {
                float knock = KeeperTempo.Shake(blooms, false);

                Assert.GreaterOrEqual(knock, 0f);
                Assert.LessOrEqual(knock, KeeperTempo.MostShake);
                Assert.AreEqual(KeeperSpectacle.For(blooms, false).Jolt * KeeperTempo.MostShake,
                                knock, 1e-4f,
                                "the knock is a second ladder rather than a reading of the one");
            }
        }
    }
}
