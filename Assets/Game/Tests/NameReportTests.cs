using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client's half of reporting a keeper's name.
    ///
    /// <para>
    /// There is deliberately very little of it. Everything that decides anything — the fold,
    /// the word classes, the threshold, the takedown — is on the server, where a modified
    /// client cannot reach it, and is proved by <c>firebase/functions/test/names.mjs</c>. What
    /// is left here is a session note that makes a button say the right thing, and the reason
    /// it is worth testing at all is that its two failure modes are both silent: a note that
    /// never clears greys a control for a keeper the player has never reported, and one that
    /// grows without bound is a set the player can extend by tapping.
    /// </para>
    /// </summary>
    public sealed class NameReportTests
    {
        [SetUp]
        public void Reset() => NameReports.Forget();

        [Test]
        public void AKeeperNobodyReportedIsNotMarked()
        {
            Assert.IsFalse(NameReports.AlreadySent("keeper-a"));
        }

        [Test]
        public void ReportingAKeeperMarksThatKeeperAndNoOther()
        {
            NameReports.Remember("keeper-a");

            Assert.IsTrue(NameReports.AlreadySent("keeper-a"));
            Assert.IsFalse(NameReports.AlreadySent("keeper-b"));
        }

        [Test]
        public void RememberingTwiceIsOneEntry()
        {
            NameReports.Remember("keeper-a");
            NameReports.Remember("keeper-a");

            Assert.IsTrue(NameReports.AlreadySent("keeper-a"));

            // Proved by filling the rest of the bound: a duplicate that took a second slot
            // would push the first entry out one report early.
            for (int i = 0; i < NameReports.MaxRemembered - 1; i++)
                NameReports.Remember("filler-" + i);

            Assert.IsTrue(NameReports.AlreadySent("keeper-a"),
                          "a duplicate must not consume a slot");
        }

        [Test]
        public void AnEmptyKeeperIsNeverRemembered()
        {
            NameReports.Remember(null);
            NameReports.Remember(string.Empty);

            Assert.IsFalse(NameReports.AlreadySent(null));
            Assert.IsFalse(NameReports.AlreadySent(string.Empty));
        }

        /// <summary>
        /// The bound. A player can add to this set by tapping, so it cannot be allowed to grow
        /// for the life of the session — and dropping the oldest is right rather than merely
        /// convenient, because the server is the authority and forgetting costs one refused
        /// write on a grove somebody visited hours ago.
        /// </summary>
        [Test]
        public void TheOldestReportIsForgottenPastTheBound()
        {
            NameReports.Remember("first");

            for (int i = 0; i < NameReports.MaxRemembered; i++)
                NameReports.Remember("keeper-" + i);

            Assert.IsFalse(NameReports.AlreadySent("first"),
                           "the oldest entry should have been evicted");
            Assert.IsTrue(NameReports.AlreadySent("keeper-" + (NameReports.MaxRemembered - 1)),
                          "the newest entry must survive");
        }

        /// <summary>
        /// <b>Who this device reported belongs to the player, not the handset.</b> Carrying it
        /// across an account switch would grey the control for somebody who has never used it,
        /// on a grove they have never seen — and it is why this is a session note rather than a
        /// save field, where merging it would arrive on a second device as a reason to stay
        /// quiet (invariant 11b).
        /// </summary>
        [Test]
        public void SwitchingAccountsForgetsEverything()
        {
            NameReports.Remember("keeper-a");
            NameReports.Remember("keeper-b");

            NameReports.Forget();

            Assert.IsFalse(NameReports.AlreadySent("keeper-a"));
            Assert.IsFalse(NameReports.AlreadySent("keeper-b"));
        }
    }
}
