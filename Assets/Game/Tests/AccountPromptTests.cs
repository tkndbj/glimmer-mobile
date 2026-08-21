using GlimmerGrove.Cloud;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// When the game may ask an anonymous player to attach a real account.
    ///
    /// <para>
    /// Run offline, and the type is shaped the way it is so that they can be. Every situation
    /// this policy is about is one the Editor never reaches — a live SDK session, a real
    /// purchase, a device that has been away for two days, a player who changed the system
    /// clock — so a rule that could only be exercised by playing the shipped game is a rule
    /// nobody would ever exercise. <c>SyncScheduler</c>'s bargain and <c>TweenCycle</c>'s
    /// reason.
    /// </para>
    /// <para>
    /// The failures being guarded against are all quiet ones. A prompt that never fires looks
    /// exactly like a player who has not finished a chapter yet; a prompt that fires for ever
    /// looks like a bug in the panel; and a shared quiet period that swallows the wrong budget
    /// looks like nothing at all until a paying player is never asked to protect the money
    /// they spent.
    /// </para>
    /// </summary>
    public sealed class AccountPromptTests
    {
        const long Now = 1_770_000_000L;
        const long Day = 24L * 60L * 60L;

        static AccountPromptPolicy Guest() => new AccountPromptPolicy();

        /// <summary>An anonymous, healthy, backed-by-a-server device — the only state that asks.</summary>
        static bool Ask(AccountPromptPolicy policy, AccountPromptTrigger trigger, long now)
            => policy.ShouldOffer(trigger, available: true, linked: false, mismatched: false, now);

        // ------------------------------------------------------------ the states that stay quiet
        [Test]
        public void AGuestOnAWorkingBackendIsAsked()
        {
            Assert.IsTrue(Ask(Guest(), AccountPromptTrigger.Chapter, Now));
            Assert.IsTrue(Ask(Guest(), AccountPromptTrigger.Purchase, Now));
        }

        [Test]
        public void APlayerWhoIsAlreadyLinkedIsNeverAsked()
        {
            var policy = Guest();

            Assert.IsFalse(policy.ShouldOffer(AccountPromptTrigger.Purchase, available: true,
                                              linked: true, mismatched: false, Now));
        }

        [Test]
        public void ABuildWithNoBackendIsNeverAsked()
        {
            // Nothing to link to, so the panel would offer two buttons that cannot work.
            var policy = Guest();

            Assert.IsFalse(policy.ShouldOffer(AccountPromptTrigger.Purchase, available: false,
                                              linked: false, mismatched: false, Now));
        }

        [Test]
        public void ADeviceCaughtBetweenTwoAccountsIsNeverAsked()
        {
            // A player here *is* signed in, so the guest copy would be false — and the profile
            // card is already telling them the thing that actually matters.
            var policy = Guest();

            Assert.IsFalse(policy.ShouldOffer(AccountPromptTrigger.Chapter, available: true,
                                              linked: false, mismatched: true, Now));
        }

        // ------------------------------------------------------------------------- the budgets
        [Test]
        public void TheChapterNudgeStopsAfterItsBudget()
        {
            var policy = Guest();
            long now = Now;

            for (int i = 0; i < AccountPromptPolicy.ChapterBudget; i++)
            {
                Assert.IsTrue(Ask(policy, AccountPromptTrigger.Chapter, now), "offer " + i);
                policy.NoteOffered(AccountPromptTrigger.Chapter, now);
                now += 30L * Day;
            }

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Chapter, now));
        }

        [Test]
        public void ThePurchaseNudgeGetsOneMoreThanTheChapterNudge()
        {
            Assert.Greater(AccountPromptPolicy.PurchaseBudget, AccountPromptPolicy.ChapterBudget);

            var policy = Guest();
            long now = Now;

            for (int i = 0; i < AccountPromptPolicy.PurchaseBudget; i++)
            {
                Assert.IsTrue(Ask(policy, AccountPromptTrigger.Purchase, now), "offer " + i);
                policy.NoteOffered(AccountPromptTrigger.Purchase, now);
                now += 30L * Day;
            }

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, now));
        }

        /// <summary>
        /// The reason the counts are separate at all. A player who buys gems in their first week
        /// would otherwise spend the chapter nudge's allowance on purchase prompts and never be
        /// asked at the moment that reaches everybody else.
        /// </summary>
        [Test]
        public void SpendingThePurchaseBudgetLeavesTheChapterBudgetIntact()
        {
            var policy = Guest();
            long now = Now;

            for (int i = 0; i < AccountPromptPolicy.PurchaseBudget; i++)
            {
                policy.NoteOffered(AccountPromptTrigger.Purchase, now);
                now += 30L * Day;
            }

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, now));
            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Chapter, now));
        }

        // -------------------------------------------------------------------- the quiet period
        [Test]
        public void ASecondAskInsideTheQuietPeriodIsRefused()
        {
            var policy = Guest();
            policy.NoteOffered(AccountPromptTrigger.Purchase, Now);

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase,
                               Now + AccountPromptPolicy.QuietSeconds - 1L));
            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Purchase,
                              Now + AccountPromptPolicy.QuietSeconds));
        }

        /// <summary>
        /// The spacing is shared even though the budgets are not: a player who finished a
        /// chapter and then bought a coin pack would otherwise meet two account panels inside a
        /// minute, which is how a prompt teaches people to dismiss prompts.
        /// </summary>
        [Test]
        public void OneTriggerQuietensTheOther()
        {
            var policy = Guest();
            policy.NoteOffered(AccountPromptTrigger.Chapter, Now);

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, Now + 60L));
            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Purchase,
                              Now + AccountPromptPolicy.QuietSeconds));
        }

        // ------------------------------------------------------------------- a clock that lies
        /// <summary>
        /// A stamp in the future must not silence the prompt for the life of the installation.
        ///
        /// The obvious implementation — <c>now - last &lt; Quiet</c> — is negative here, reads as
        /// "inside the quiet period", and never recovers, because nothing ever writes a smaller
        /// stamp while every ask is refused. A player only has to change their device date once.
        /// </summary>
        [Test]
        public void AStampFromTheFutureDoesNotSilenceThePromptForEver()
        {
            var policy = Guest();
            policy.Adopt(0, 0, Now + 3650L * Day);

            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Purchase, Now));
        }

        [Test]
        public void OfferingHealsAStampFromTheFuture()
        {
            var policy = Guest();
            policy.Adopt(0, 0, Now + 3650L * Day);

            policy.NoteOffered(AccountPromptTrigger.Purchase, Now);

            Assert.AreEqual(Now, policy.LastOfferedUnix);
        }

        // -------------------------------------------------------------------- loading it back
        [Test]
        public void CountsSurviveARoundTripThroughAdopt()
        {
            var policy = Guest();
            policy.Adopt(1, 2, Now);

            Assert.AreEqual(1, policy.OffersMade(AccountPromptTrigger.Chapter));
            Assert.AreEqual(2, policy.OffersMade(AccountPromptTrigger.Purchase));
            Assert.AreEqual(Now, policy.LastOfferedUnix);
        }

        /// <summary>
        /// The counts come back from device storage a player can edit, and a negative one would
        /// hand out an unlimited supply of prompts for ever.
        /// </summary>
        [Test]
        public void EditedCountsAreClampedRatherThanTrusted()
        {
            var policy = Guest();
            policy.Adopt(-9, -9, -9);

            Assert.AreEqual(0, policy.OffersMade(AccountPromptTrigger.Chapter));
            Assert.AreEqual(0, policy.OffersMade(AccountPromptTrigger.Purchase));
            Assert.AreEqual(0L, policy.LastOfferedUnix);
        }

        /// <summary>
        /// A live installation that has already declined twice must not be handed a fresh
        /// allowance — which is why <c>AccountPrompts</c> keeps the shipped PlayerPrefs key.
        /// </summary>
        [Test]
        public void AnInstallationThatAlreadySpentTheChapterBudgetIsNotAskedAgain()
        {
            var policy = Guest();
            policy.Adopt(AccountPromptPolicy.ChapterBudget, 0, 0L);

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Chapter, Now));
        }

        // --------------------------------------------------------------- the standing notice
        /// <summary>
        /// The bar on the shop's money shelves is not rationed by either budget or the quiet
        /// period. It is not an interruption, and it is the thing that lets the modal be rare:
        /// a player who declines every prompt still sees it on every visit.
        /// </summary>
        [Test]
        public void TheShopNoticeIsNeverRationed()
        {
            var policy = Guest();

            for (int i = 0; i < AccountPromptPolicy.PurchaseBudget; i++)
                policy.NoteOffered(AccountPromptTrigger.Purchase, Now);

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, Now));
            Assert.IsTrue(AccountPromptPolicy.ShouldWarn(available: true, linked: false,
                                                         mismatched: false));
        }

        [Test]
        public void TheShopNoticeFollowsTheSameThreeStatesAsTheAsk()
        {
            Assert.IsFalse(AccountPromptPolicy.ShouldWarn(available: false, linked: false,
                                                          mismatched: false));
            Assert.IsFalse(AccountPromptPolicy.ShouldWarn(available: true, linked: true,
                                                          mismatched: false));
            Assert.IsFalse(AccountPromptPolicy.ShouldWarn(available: true, linked: false,
                                                          mismatched: true));
        }
    }
}
