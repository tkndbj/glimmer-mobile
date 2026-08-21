using System;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rename panel's debounce, which is the cost control for the whole uniqueness feature.
    ///
    /// <para>
    /// A reservation check is one document read, which is cheap; a check per keystroke, per
    /// player, for the life of the game is not. That single factor is roughly ten times the
    /// bill, so it is worth proving rather than asserting — and it can be proved here because
    /// <see cref="NameCheckScheduler"/> holds no clock, no socket and no Unity types, which is
    /// <c>SyncScheduler</c>'s and <c>TweenCycle</c>'s bargain.
    /// </para>
    /// </summary>
    public sealed class NameCheckTests
    {
        /// <summary>Runs a name through the field one character at a time, as a person does.</summary>
        static int TypeAndCount(NameCheckScheduler names, string name, float gapSeconds)
        {
            int asked = 0;

            for (int i = 1; i <= name.Length; i++)
            {
                names.Typed(name.Substring(0, i));

                if (names.Tick(gapSeconds, out string key))
                {
                    asked++;
                    names.Answered(key, taken: false, mine: false);
                }
            }

            // The pause after the last keystroke.
            if (names.Tick(NameCheckScheduler.DebounceSeconds, out string last))
            {
                asked++;
                names.Answered(last, taken: false, mine: false);
            }

            return asked;
        }

        [Test]
        public void ANameTypedStraightThroughCostsOneRead()
        {
            var names = new NameCheckScheduler();

            // 60ms a character is brisk but ordinary. Sixteen characters, one read.
            Assert.AreEqual(1, TypeAndCount(names, "Mossfoottheeld", .06f));
        }

        [Test]
        public void NothingIsAskedBeforeTheFieldHasBeenStill()
        {
            var names = new NameCheckScheduler();
            names.Typed("Fernwillow");

            Assert.AreEqual(NameAvailability.Checking, names.Availability);
            Assert.IsFalse(names.Tick(NameCheckScheduler.DebounceSeconds * .5f, out _),
                           "asked before the debounce elapsed");

            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds * .5f, out string key));
            Assert.AreEqual("fernwillow", key);
        }

        /// <summary>
        /// The saving that matters second-most, because it is what somebody actually does while
        /// choosing: type a name, delete a few characters, type them again.
        /// </summary>
        [Test]
        public void AnAnswerIsRememberedSoRetypingAskesNothing()
        {
            var names = new NameCheckScheduler();

            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string key));
            names.Answered(key, taken: true, mine: false);
            Assert.AreEqual(NameAvailability.Taken, names.Availability);

            names.Typed("Fernwil");
            names.Typed("Fernwillow");

            Assert.AreEqual(NameAvailability.Taken, names.Availability,
                            "a remembered answer is shown immediately");
            Assert.IsFalse(names.Tick(NameCheckScheduler.DebounceSeconds * 4f, out _),
                           "a name already answered was asked about twice");
        }

        [Test]
        public void ANameThatCouldNeverBeReservedIsNeverAsked()
        {
            var names = new NameCheckScheduler();

            foreach (string tooShort in new[] { "", " ", "A", "!!", "···" })
            {
                names.Typed(tooShort);

                Assert.AreEqual(NameAvailability.TooShort, names.Availability, $"'{tooShort}'");
                Assert.IsFalse(names.Tick(NameCheckScheduler.DebounceSeconds * 4f, out _),
                               $"'{tooShort}' was sent to the database");
            }
        }

        /// <summary>
        /// Opening the panel puts the player's own name in the field, which is by a wide margin
        /// the commonest state it is ever in. It must cost nothing.
        /// </summary>
        [Test]
        public void TheNameAlreadyHeldIsNeverAsked()
        {
            var names = new NameCheckScheduler();
            names.Hold("Fern Willow");
            names.Typed("Fern Willow");

            Assert.AreEqual(NameAvailability.Mine, names.Availability);
            Assert.IsFalse(names.Tick(NameCheckScheduler.DebounceSeconds * 4f, out _));

            // And the fold decides it, so a differently-spelled version of your own name is
            // still yours rather than reported as somebody else's.
            names.Typed("fernwillow");
            Assert.AreEqual(NameAvailability.Mine, names.Availability);
        }

        [Test]
        public void AReservationHeldByThisAccountReadsAsMineRatherThanTaken()
        {
            var names = new NameCheckScheduler();
            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string key));

            names.Answered(key, taken: true, mine: true);
            Assert.AreEqual(NameAvailability.Mine, names.Availability);
        }

        /// <summary>
        /// An answer that arrives after the player has typed on is remembered and not shown.
        /// Showing it would put a verdict about one name under a different one.
        /// </summary>
        [Test]
        public void AnAnswerForANameNoLongerInTheFieldIsNotDisplayed()
        {
            var names = new NameCheckScheduler();

            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string first));

            names.Typed("Mosswood");
            names.Answered(first, taken: true, mine: false);

            Assert.AreNotEqual(NameAvailability.Taken, names.Availability,
                               "a stale answer was shown against the current name");

            // Remembered, though — going back to it costs no second read.
            names.Typed("Fernwillow");
            Assert.AreEqual(NameAvailability.Taken, names.Availability);
        }

        /// <summary>
        /// A failed read must not become a retry loop in front of an open keyboard. The claim at
        /// the end is the authority, so there is nothing here worth a battery.
        /// </summary>
        [Test]
        public void AFailedReadIsNotRetriedUntilTheNameChanges()
        {
            var names = new NameCheckScheduler();

            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string key));
            names.Failed(key);

            Assert.AreEqual(NameAvailability.Unknown, names.Availability);
            Assert.IsFalse(names.Tick(NameCheckScheduler.DebounceSeconds * 10f, out _),
                           "a failed read was retried on its own");

            names.Typed("Fernwillo");
            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out _),
                          "typing again must ask again");
        }

        /// <summary>
        /// Only one read is ever outstanding. Without this a slow network plus a fast typist is
        /// a read per keystroke after all, which is the exact cost this class exists to remove.
        /// </summary>
        [Test]
        public void OnlyOneReadIsEverInFlight()
        {
            var names = new NameCheckScheduler();

            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string key));
            Assert.IsTrue(names.IsAsking);

            names.Typed("Mosswood");
            Assert.IsFalse(names.Tick(NameCheckScheduler.DebounceSeconds * 4f, out _),
                           "a second read was started while the first was outstanding");

            names.Answered(key, taken: false, mine: false);
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string second));
            Assert.AreEqual("mosswood", second);
        }

        /// <summary>
        /// The claim is adjudicated and the hint is not, so its verdict has to overwrite the
        /// hint — otherwise pressing save twice reports two different things about one name.
        /// </summary>
        [Test]
        public void TheClaimsVerdictOverridesWhatTheHintBelieved()
        {
            var names = new NameCheckScheduler();

            names.Typed("Fernwillow");
            Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string key));
            names.Answered(key, taken: false, mine: false);
            Assert.AreEqual(NameAvailability.Free, names.Availability);

            // Somebody else took it in the second between the hint and the press.
            names.Adopt(key, NameAvailability.Taken);
            Assert.AreEqual(NameAvailability.Taken, names.Availability);

            names.Typed("Fernwillo");
            names.Typed("Fernwillow");
            Assert.AreEqual(NameAvailability.Taken, names.Availability,
                            "the claim's verdict must be what is remembered");
        }

        // ------------------------------------------------------------- the panel's two tables

        /// <summary>
        /// <b>The property this whole feature has to hold.</b> Whatever the server answers, a
        /// player who pressed save either has the name or is left looking at a reason — never
        /// neither. "Neither" is a rename that vanished, which is the failure this codebase has
        /// already shipped once for a different reason (invariant 11c) and the one a player
        /// reports as "renaming does not work".
        ///
        /// Written over the enum rather than over a list of cases, so an outcome added later is
        /// covered by this the day it is added.
        /// </summary>
        [Test]
        public void NoAnswerFromTheServerCanLoseARename()
        {
            foreach (NameClaimOutcome outcome in Enum.GetValues(typeof(NameClaimOutcome)))
            {
                var step = RenameRules.ResolveClaim(outcome);

                Assert.IsTrue(step.StoresName || !step.Closes,
                              $"{outcome} neither stored the name nor left the panel open");

                // And a panel that stays open has to say why, or it is a button that did nothing.
                if (!step.Closes)
                {
                    Assert.AreNotEqual(string.Empty, step.MessageKey,
                                       $"{outcome} keeps the panel open and says nothing");
                }
            }
        }

        /// <summary>
        /// The two answers a player can act on keep the panel up; every other answer closes it.
        /// Applying a cooldown would leave this device and the board disagreeing until it
        /// expired, which is worse to explain than a countdown.
        /// </summary>
        [Test]
        public void OnlyTheTwoActionableRefusalsKeepThePanelOpen()
        {
            Assert.IsFalse(RenameRules.ResolveClaim(NameClaimOutcome.Taken).Closes);
            Assert.IsFalse(RenameRules.ResolveClaim(NameClaimOutcome.Cooldown).Closes);

            Assert.IsTrue(RenameRules.ResolveClaim(NameClaimOutcome.Claimed).Closes);
            Assert.IsTrue(RenameRules.ResolveClaim(NameClaimOutcome.Unchanged).Closes);
            Assert.IsTrue(RenameRules.ResolveClaim(NameClaimOutcome.Refused).Closes);
            Assert.IsTrue(RenameRules.ResolveClaim(NameClaimOutcome.Unavailable).Closes);
        }

        /// <summary>
        /// A name only the boards refuse is still the player's. Invariant 19b, and the reason
        /// the copy has to say so rather than let it fail silently.
        /// </summary>
        [Test]
        public void ARefusedNameIsKeptAndSaidOutLoud()
        {
            var step = RenameRules.ResolveClaim(NameClaimOutcome.Refused);

            Assert.IsTrue(step.StoresName, "a refused name must still be the player's");
            Assert.AreNotEqual(string.Empty, step.MessageKey,
                               "a name that will not appear on a board must not do so silently");
        }

        /// <summary>
        /// Being unreachable is not a refusal. Renaming on a train has to feel ordinary, and the
        /// next publish is what settles it.
        /// </summary>
        [Test]
        public void BeingUnreachableStoresTheNameAndSaysNothing()
        {
            var step = RenameRules.ResolveClaim(NameClaimOutcome.Unavailable);

            Assert.IsTrue(step.StoresName);
            Assert.IsTrue(step.Closes);
            Assert.AreEqual(string.Empty, step.MessageKey);
            Assert.IsFalse(step.IsSetback);
        }

        /// <summary>
        /// <b>A refused button always says why.</b> That is `AdOfferState`'s rule — a greyed
        /// control with no explanation is how players learn a feature is broken — and it is the
        /// assertion worth making over the whole enum, because it holds for states added later.
        ///
        /// <para>
        /// Exactly two states refuse, and they are the two the client can be sure about: a name
        /// somebody else holds, and one too short to publish. Length is knowable here; the word
        /// filter deliberately is not, which is why a name it would refuse is saved and reported
        /// afterwards rather than blocked at the field.
        /// </para>
        /// </summary>
        [Test]
        public void EveryStateThatRefusesTheSaveButtonSaysWhy()
        {
            foreach (NameAvailability availability in Enum.GetValues(typeof(NameAvailability)))
            {
                var line = RenameRules.LineFor(availability, fieldIsBlank: false);

                if (line.CanSave) continue;

                Assert.AreNotEqual(string.Empty, line.Key,
                                   $"{availability} refuses the button without saying why");

                // Muted is right for "too short" — it is guidance while typing rather than a
                // fault — but nothing that blocks the button may read as good news.
                Assert.AreNotEqual(NameTone.Good, line.Tone,
                                   $"{availability} refuses the button and reads as good news");
            }
        }

        [Test]
        public void OnlyTheTwoKnowableProblemsRefuseTheSaveButton()
        {
            var refusing = new System.Collections.Generic.List<NameAvailability>();

            foreach (NameAvailability availability in Enum.GetValues(typeof(NameAvailability)))
                if (!RenameRules.LineFor(availability, fieldIsBlank: false).CanSave)
                    refusing.Add(availability);

            CollectionAssert.AreEquivalent(
                new[] { NameAvailability.Taken, NameAvailability.TooShort }, refusing,
                "the set of states that block saving changed");
        }

        /// <summary>
        /// An empty field is a real choice — it stores the default name — so it is never scolded
        /// for being short. It is also the state the panel opens in for anyone who has never
        /// renamed, which makes it the commonest first frame there is.
        /// </summary>
        [Test]
        public void AnEmptyFieldIsNeverScolded()
        {
            var blank = RenameRules.LineFor(NameAvailability.TooShort, fieldIsBlank: true);
            Assert.AreEqual(string.Empty, blank.Key);
            Assert.IsTrue(blank.CanSave, "saving an empty field stores the default name");

            var typed = RenameRules.LineFor(NameAvailability.TooShort, fieldIsBlank: false);
            Assert.AreNotEqual(string.Empty, typed.Key);
            Assert.IsTrue(typed.TakesMinimum, "the line has to name the minimum it is asking for");
            Assert.IsFalse(typed.CanSave);
        }

        /// <summary>
        /// Nothing decided means nothing said. A hint that guessed would be a hint that lies on
        /// exactly the devices least able to check it.
        /// </summary>
        [Test]
        public void AnUndecidedCheckSaysNothingAndStillLetsYouSave()
        {
            var line = RenameRules.LineFor(NameAvailability.Unknown, fieldIsBlank: false);

            Assert.AreEqual(string.Empty, line.Key);
            Assert.IsTrue(line.CanSave);
        }

        /// <summary>Only a free name reads as good; only an obstacle reads as bad.</summary>
        [Test]
        public void ToneIsSpentOnlyWhereItMeansSomething()
        {
            Assert.AreEqual(NameTone.Good, RenameRules.LineFor(NameAvailability.Free, false).Tone);
            Assert.AreEqual(NameTone.Bad, RenameRules.LineFor(NameAvailability.Taken, false).Tone);
            Assert.AreEqual(NameTone.Muted, RenameRules.LineFor(NameAvailability.Mine, false).Tone);
            Assert.AreEqual(NameTone.Muted, RenameRules.LineFor(NameAvailability.Checking, false).Tone);
        }

        /// <summary>
        /// The cache is bounded, because this object lives as long as a panel and a panel can be
        /// held open indefinitely with a keyboard attached to it.
        /// </summary>
        [Test]
        public void TheRememberedAnswersAreBounded()
        {
            var names = new NameCheckScheduler();

            for (int i = 0; i < NameCheckScheduler.MaxRemembered * 3; i++)
            {
                string name = "keeper" + i;
                names.Typed(name);

                Assert.IsTrue(names.Tick(NameCheckScheduler.DebounceSeconds, out string key));
                names.Answered(key, taken: false, mine: false);
            }

            // The oldest has been dropped, so it is asked about again rather than answered from
            // a cache that grew without limit.
            names.Typed("keeper0");
            Assert.AreEqual(NameAvailability.Checking, names.Availability);
        }
    }
}
