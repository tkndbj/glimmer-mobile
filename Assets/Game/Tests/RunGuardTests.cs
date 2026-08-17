using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The marker that makes an unfinished run cost what a lost one costs.
    ///
    /// <para>
    /// Every assertion here is about somebody's hearts, and the two failure directions are not
    /// equally bad. Charging twice, or charging a player who won, takes something real from
    /// somebody who did nothing wrong — so <see cref="RunGuard.Resolve"/> is idempotent and
    /// every ending clears the marker before anything else happens. Charging never is merely a
    /// gate that does not bind. The tests are written in that order of severity.
    /// </para>
    /// <para>
    /// These reach <c>PlayerPrefs</c> and <c>Wallet</c>, so they run in the Editor's Test
    /// Runner rather than offline. That is the right trade for the one piece of this feature
    /// that has to survive the process dying: a fake store would prove the arithmetic and not
    /// the thing that actually matters, which is that the marker is on disk before the crash.
    /// </para>
    /// </summary>
    public sealed class RunGuardTests
    {
        static readonly LevelId Glade = LevelId.Parse("c01_first_light");
        static readonly LevelId Other = LevelId.Parse("c01_twin_streams");

        [SetUp]
        public void Clear()
        {
            RunGuard.Resolve();
            RunGuard.NoteReported();
        }

        [TearDown]
        public void Tidy()
        {
            RunGuard.Resolve();
            RunGuard.NoteReported();
        }

        // -------------------------------------------------------------- the marker
        [Test]
        public void ARunThatResolvesLeavesNothingBehind()
        {
            RunGuard.Begin(Glade);
            RunGuard.Resolve();

            Assert.IsFalse(RunGuard.Claim(), "a finished run must not be charged for at launch");
        }

        [Test]
        public void ResolveIsIdempotentBecauseMissingOneCostsAHeart()
        {
            RunGuard.Begin(Glade);

            RunGuard.Resolve();
            RunGuard.Resolve();
            RunGuard.Resolve();

            Assert.IsFalse(RunGuard.Claim());
        }

        [Test]
        public void AnInvalidLevelIsNeverWrittenDown()
        {
            RunGuard.Begin(LevelId.None);

            Assert.IsFalse(RunGuard.Claim(), "nothing to charge for, and no id to name");
        }

        [Test]
        public void TheLatestRunIsTheOneOwedFor()
        {
            RunGuard.Begin(Glade);
            RunGuard.Begin(Other);

            Assert.IsTrue(RunGuard.Claim());
            Assert.AreEqual(Other, RunGuard.Unfinished, "one marker describes one run");
        }

        // --------------------------------------------------------------- the charge
        [Test]
        public void AnUnfinishedRunCostsExactlyOneHeart()
        {
            Wallet.GrantHearts(HeartRules.RefillCap);
            int before = Wallet.Hearts.Count;
            Assume.That(before, Is.GreaterThan(0), "the fixture needs a heart to take");

            RunGuard.Begin(Glade);

            Assert.IsTrue(RunGuard.Claim());
            Assert.AreEqual(before - HeartRules.DefeatCost, Wallet.Hearts.Count);
            Assert.IsTrue(RunGuard.UnfinishedWasCharged);
            Assert.AreEqual(Glade, RunGuard.Unfinished);
        }

        /// <summary>
        /// The one that would be a support ticket. A second launch, a navigation back to the
        /// hub, a re-read of the marker — none of them may charge again for the same run.
        /// </summary>
        [Test]
        public void TheSameRunIsNeverChargedTwice()
        {
            Wallet.GrantHearts(HeartRules.RefillCap);
            int before = Wallet.Hearts.Count;

            RunGuard.Begin(Glade);
            RunGuard.Claim();

            int afterFirst = Wallet.Hearts.Count;

            Assert.IsFalse(RunGuard.Claim(), "the marker is spent");
            Assert.IsFalse(RunGuard.Claim());
            Assert.AreEqual(afterFirst, Wallet.Hearts.Count);
            Assert.AreEqual(before - HeartRules.DefeatCost, Wallet.Hearts.Count);
        }

        /// <summary>
        /// A player already at zero owes nothing — and the marker still has to go, or some
        /// later launch that finds them solvent would charge them for a run they abandoned
        /// weeks ago.
        /// </summary>
        [Test]
        public void APlayerWithNoHeartsIsNotLeftOwingOne()
        {
            Wallet.TrySpendHeart(Wallet.Hearts.Count);
            Assume.That(Wallet.Hearts.Count, Is.EqualTo(0));

            RunGuard.Begin(Glade);

            Assert.IsTrue(RunGuard.Claim(), "there was a run outstanding");
            Assert.IsFalse(RunGuard.UnfinishedWasCharged, "but nothing to take for it");
            Assert.IsFalse(RunGuard.Claim(), "and it is not owed on the next launch either");

            Wallet.GrantHearts(HeartRules.RefillCap);
        }

        // -------------------------------------------------------------- the reporting
        [Test]
        public void TheNoticeIsGivenOnceRatherThanOnEveryVisitToTheHub()
        {
            Wallet.GrantHearts(HeartRules.RefillCap);

            RunGuard.Begin(Glade);
            RunGuard.Claim();

            Assert.IsTrue(RunGuard.Unfinished.IsValid);

            RunGuard.NoteReported();

            Assert.IsFalse(RunGuard.Unfinished.IsValid);
            Assert.IsFalse(RunGuard.UnfinishedWasCharged);
        }

        [Test]
        public void AQuietLaunchHasNothingToReport()
        {
            Assert.IsFalse(RunGuard.Claim());
            Assert.IsFalse(RunGuard.Unfinished.IsValid);
            Assert.IsFalse(RunGuard.UnfinishedWasCharged);
        }
    }
}
