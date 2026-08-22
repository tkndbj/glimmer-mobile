using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
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

        /// <summary>The pacing that ships in the build, which is what these cases are about.</summary>
        static AccountPromptRuleTable Shipped => AccountPromptRuleTable.Default;

        static int ChapterBudget => Shipped.ChapterBudget;
        static int PurchaseBudget => Shipped.PurchaseBudget;
        static long QuietSeconds => Shipped.QuietSeconds;

        /// <summary>An anonymous, healthy, backed-by-a-server device — the only state that asks.</summary>
        static bool Ask(AccountPromptPolicy policy, AccountPromptTrigger trigger, long now,
                        AccountPromptRuleTable rules = null)
            => policy.ShouldOffer(trigger, rules ?? Shipped,
                                  available: true, linked: false, mismatched: false, nowUnix: now);

        static AccountPromptRuleTable Published(int chapter = -1, int purchase = -1, int quietHours = -1,
                                                List<string> problems = null)
            => AccountPromptRuleTable.Resolve(
                new PromptsDto { chapterBudget = chapter, purchaseBudget = purchase, quietHours = quietHours },
                problems ?? new List<string>());

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

            Assert.IsFalse(policy.ShouldOffer(AccountPromptTrigger.Purchase, Shipped,
                                              available: true, linked: true, mismatched: false,
                                              nowUnix: Now));
        }

        [Test]
        public void ABuildWithNoBackendIsNeverAsked()
        {
            // Nothing to link to, so the panel would offer two buttons that cannot work.
            var policy = Guest();

            Assert.IsFalse(policy.ShouldOffer(AccountPromptTrigger.Purchase, Shipped,
                                              available: false, linked: false, mismatched: false,
                                              nowUnix: Now));
        }

        [Test]
        public void ADeviceCaughtBetweenTwoAccountsIsNeverAsked()
        {
            // A player here *is* signed in, so the guest copy would be false — and the profile
            // card is already telling them the thing that actually matters.
            var policy = Guest();

            Assert.IsFalse(policy.ShouldOffer(AccountPromptTrigger.Chapter, Shipped,
                                              available: true, linked: false, mismatched: true,
                                              nowUnix: Now));
        }

        // ------------------------------------------------------------------------- the budgets
        [Test]
        public void TheChapterNudgeStopsAfterItsBudget()
        {
            var policy = Guest();
            long now = Now;

            for (int i = 0; i < ChapterBudget; i++)
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
            Assert.Greater(PurchaseBudget, ChapterBudget);

            var policy = Guest();
            long now = Now;

            for (int i = 0; i < PurchaseBudget; i++)
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

            for (int i = 0; i < PurchaseBudget; i++)
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
                               Now + QuietSeconds - 1L));
            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Purchase,
                              Now + QuietSeconds));
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
                              Now + QuietSeconds));
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
            policy.Adopt(ChapterBudget, 0, 0L);

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

            for (int i = 0; i < PurchaseBudget; i++)
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

        // ------------------------------------------------------------- the published pacing
        /// <summary>
        /// An absent block is not an error. A client that predates the field keeps the pacing
        /// that shipped inside it — every optional block in this file works that way.
        /// </summary>
        [Test]
        public void AnAbsentBlockKeepsTheShippedPacing()
        {
            var problems = new List<string>();
            var rules = AccountPromptRuleTable.Resolve(null, problems);

            Assert.AreSame(AccountPromptRuleTable.Default, rules);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void AnUnsetFieldInheritsRatherThanReadingAsZero()
        {
            var problems = new List<string>();
            var rules = Published(chapter: 5, problems: problems);

            Assert.AreEqual(5, rules.ChapterBudget);
            Assert.AreEqual(AccountPromptLimits.DefaultPurchaseBudget, rules.PurchaseBudget);
            Assert.AreEqual(AccountPromptLimits.DefaultQuietHours * 3600L, rules.QuietSeconds);
            Assert.IsEmpty(problems);
        }

        /// <summary>
        /// The lever the whole block exists for: if the modal costs more conversion than the
        /// protection is worth, a push turns it off without a store review. Zero must therefore
        /// be a value an author can write and not be mistaken for "said nothing" — which is why
        /// the DTO's sentinel is -1.
        /// </summary>
        [Test]
        public void APublishedBudgetOfZeroTurnsThatTriggerOffAndLeavesTheOtherAlone()
        {
            var problems = new List<string>();
            var rules = Published(purchase: 0, problems: problems);
            var policy = Guest();

            Assert.IsEmpty(problems, "zero is a decision, not a mistake");
            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, Now, rules));
            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Chapter, Now, rules));
        }

        /// <summary>
        /// The bound whose absence is a hostile game rather than a mistuning — a file saying
        /// "ask every time" would put a modal in front of every purchase for every guest, with
        /// no app update to roll back.
        /// </summary>
        [Test]
        public void ABudgetAboveTheCeilingIsClampedAndSaidOutLoud()
        {
            var problems = new List<string>();
            var rules = Published(chapter: 9999, problems: problems);

            Assert.AreEqual(AccountPromptLimits.MaxBudget, rules.ChapterBudget);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("chapterBudget", problems[0]);
        }

        /// <summary>
        /// Zero hours would let two triggers land back to back, which is the failure the shared
        /// clock exists to prevent — and it is reachable by a typo rather than by a decision,
        /// which is exactly why it is clamped rather than honoured.
        /// </summary>
        [Test]
        public void AQuietPeriodOfNothingIsRefused()
        {
            var problems = new List<string>();
            var rules = Published(quietHours: 0, problems: problems);

            Assert.AreEqual(AccountPromptLimits.MinQuietHours * 3600L, rules.QuietSeconds);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("quietHours", problems[0]);
        }

        [Test]
        public void APublishedQuietPeriodIsWhatTheAskActuallyUses()
        {
            var rules = Published(quietHours: 6);
            var policy = Guest();

            policy.NoteOffered(AccountPromptTrigger.Purchase, Now);

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, Now + 5L * 3600L, rules));
            Assert.IsTrue(Ask(policy, AccountPromptTrigger.Purchase, Now + 6L * 3600L, rules));
        }

        /// <summary>
        /// A null table is the built-in pacing rather than a crash. This is reachable: the table
        /// is read live off <c>ProgressionRules</c>, and a content push landing mid-frame is the
        /// kind of thing that must never take a screen down.
        /// </summary>
        [Test]
        public void ANullTableFallsBackRatherThanThrowing()
        {
            var policy = Guest();

            Assert.IsTrue(policy.ShouldOffer(AccountPromptTrigger.Purchase, null,
                                             available: true, linked: false, mismatched: false,
                                             nowUnix: Now));
        }

        /// <summary>
        /// The table reaches the ask and nothing else. A guest is a guest whatever the pacing
        /// says, so switching every trigger off must not also take the bar off the shelf — that
        /// is the standing half of the warning and the reason the modal can be rare.
        /// </summary>
        [Test]
        public void TurningEveryPromptOffDoesNotTakeDownTheShopNotice()
        {
            var rules = Published(chapter: 0, purchase: 0);
            var policy = Guest();

            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Chapter, Now, rules));
            Assert.IsFalse(Ask(policy, AccountPromptTrigger.Purchase, Now, rules));
            Assert.IsTrue(AccountPromptPolicy.ShouldWarn(available: true, linked: false,
                                                         mismatched: false));
        }

        // ---------------------------------------------------- the wire from file to facade
        /// <summary>
        /// The published block reaches the live facade, which is the half no unit of the policy
        /// can see.
        ///
        /// <para>
        /// This project has twice shipped a field that reached a DTO and stopped there —
        /// `groveLandOwned` never reached the mapper, `unlockCost` never reached the manifest
        /// writer — and both were invisible because everything either side of the gap kept
        /// working. A pacing lever that parses correctly and never reaches the ask is the same
        /// bug wearing the same disguise: it would look exactly like a lever that had been
        /// pushed and had not helped.
        /// </para>
        /// </summary>
        [Test]
        public void APublishedBlockReachesTheLiveFacade()
        {
            try
            {
                var dto = new ProgressionDto
                {
                    schemaVersion = ProgressionSchema.Version,
                    xpToNext = new[] { 100 },
                    tailXpToNext = 100,
                    tailXpIncrement = 10,
                    prompts = new PromptsDto
                    {
                        chapterBudget = 1, purchaseBudget = 7, quietHours = 3,
                    },
                };

                Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
                ProgressionRules.Publish(table);

                Assert.AreEqual(1, AccountPromptRules.Table.ChapterBudget);
                Assert.AreEqual(7, AccountPromptRules.Table.PurchaseBudget);
                Assert.AreEqual(3L * 3600L, AccountPromptRules.Table.QuietSeconds);
            }
            finally { ProgressionRules.Reset(); }
        }

        /// <summary>A file with no block at all leaves the facade on the shipped pacing.</summary>
        [Test]
        public void AFileWithNoBlockLeavesTheFacadeOnTheShippedPacing()
        {
            try
            {
                var dto = new ProgressionDto
                {
                    schemaVersion = ProgressionSchema.Version,
                    xpToNext = new[] { 100 },
                    tailXpToNext = 100,
                    tailXpIncrement = 10,
                };

                Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
                ProgressionRules.Publish(table);

                Assert.AreEqual(AccountPromptLimits.DefaultChapterBudget,
                                AccountPromptRules.Table.ChapterBudget);
                Assert.AreEqual(AccountPromptLimits.DefaultPurchaseBudget,
                                AccountPromptRules.Table.PurchaseBudget);
            }
            finally { ProgressionRules.Reset(); }
        }
    }
}
