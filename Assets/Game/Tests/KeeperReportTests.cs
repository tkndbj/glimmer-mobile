using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client's half of reporting a keeper.
    ///
    /// <para>
    /// There is deliberately very little of it. Everything that decides anything — the fold,
    /// the word classes, the threshold, the takedown — is on the server, where a modified
    /// client cannot reach it, and is proved by <c>firebase/functions/test/names.mjs</c> and
    /// <c>reports.mjs</c>. What is left here is a session note that makes a button say the right
    /// thing, and the reason it is worth testing at all is that its failure modes are all
    /// silent: a note that never clears greys a control for a keeper the player has never
    /// reported, one that grows without bound is a set the player can extend by tapping, and one
    /// that forgets which <em>subject</em> was reported tells somebody they have already
    /// reported a grovement they have never looked at.
    /// </para>
    /// </summary>
    public sealed class KeeperReportTests
    {
        [SetUp]
        public void Reset() => KeeperReports.Forget();

        [Test]
        public void AKeeperNobodyReportedIsNotMarked()
        {
            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Name, "keeper-a"));
            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Grove, "keeper-a"));
            Assert.IsFalse(KeeperReports.AllSent("keeper-a"));
        }

        [Test]
        public void AReportedKeeperIsMarkedAndNobodyElseIs()
        {
            KeeperReports.Remember(ReportSubject.Name, "keeper-a");

            Assert.IsTrue(KeeperReports.AlreadySent(ReportSubject.Name, "keeper-a"));
            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Name, "keeper-b"));
        }

        /// <summary>
        /// The two judgements are separate, which is the whole reason the row carries a subject.
        ///
        /// Folded into one set, a player who reported a name would open that keeper's grovement
        /// to a dead control — and the obvious reading of a dead control they never used is that
        /// the game is broken.
        ///
        /// <b>It asks the store and not the panel</b>, which is what lets it keep asking while
        /// the grovement subject is held (<see cref="ReportSubjects.Held"/>): what is being
        /// pinned is that a row is keyed on the pair and not on the keeper, and that is true
        /// whether or not anything currently offers the second subject. <c>AllSent</c> is
        /// deliberately not asserted here — it walks <see cref="ReportSubjects.All"/>, so it is
        /// a reading of the panel rather than of the store, and
        /// <see cref="AllSentIsTrueOnlyWhenEverySubjectHasBeenReported"/> is where it belongs.
        /// </summary>
        [Test]
        public void ReportingOneSubjectLeavesTheOtherOffered()
        {
            KeeperReports.Remember(ReportSubject.Name, "keeper-a");

            Assert.IsTrue(KeeperReports.AlreadySent(ReportSubject.Name, "keeper-a"));
            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Grove, "keeper-a"));
        }

        /// <summary>
        /// And the control is only spent once there is nothing left to offer, which is what
        /// <c>AllSent</c> is for — the one question both screens ask before greying it.
        /// </summary>
        [Test]
        public void AllSentIsTrueOnlyWhenEverySubjectHasBeenReported()
        {
            foreach (var subject in ReportSubjects.All)
            {
                Assert.IsFalse(KeeperReports.AllSent("keeper-a"));
                KeeperReports.Remember(subject, "keeper-a");
            }

            Assert.IsTrue(KeeperReports.AllSent("keeper-a"));
        }

        [Test]
        public void RememberingTwiceIsRememberingOnce()
        {
            KeeperReports.Remember(ReportSubject.Grove, "keeper-a");
            KeeperReports.Remember(ReportSubject.Grove, "keeper-a");

            Assert.IsTrue(KeeperReports.AlreadySent(ReportSubject.Grove, "keeper-a"));

            // Filling the rest of the bound must not evict the first entry, because a duplicate
            // added no row — if it had, a player who double-tapped would lose the oldest thing
            // they reported for every extra tap.
            for (int i = 0; i < KeeperReports.MaxRemembered - 1; i++)
                KeeperReports.Remember(ReportSubject.Name, "filler-" + i);

            Assert.IsTrue(KeeperReports.AlreadySent(ReportSubject.Grove, "keeper-a"),
                          "a duplicate spent a slot in the bounded set");
        }

        [Test]
        public void AnEmptyKeeperIsIgnored()
        {
            KeeperReports.Remember(ReportSubject.Name, null);
            KeeperReports.Remember(ReportSubject.Name, string.Empty);

            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Name, null));
            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Name, string.Empty));
            Assert.IsFalse(KeeperReports.AllSent(null));
        }

        /// <summary>
        /// The set is bounded, oldest first. A player can add to it by tapping, so an unbounded
        /// one grows for the life of the session.
        /// </summary>
        [Test]
        public void TheOldestEntryIsDroppedOnceTheBoundIsReached()
        {
            KeeperReports.Remember(ReportSubject.Name, "first");

            for (int i = 0; i < KeeperReports.MaxRemembered; i++)
                KeeperReports.Remember(ReportSubject.Name, "keeper-" + i);

            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Name, "first"),
                           "the bound did not evict anything");
            Assert.IsTrue(KeeperReports.AlreadySent(
                              ReportSubject.Name, "keeper-" + (KeeperReports.MaxRemembered - 1)),
                          "the newest entry was evicted instead of the oldest");
        }

        /// <summary>
        /// "Who I reported" belongs to the player rather than to the handset, so it goes with the
        /// account — carrying it across a switch would grey a control for somebody who has never
        /// used it, on a keeper they have never seen.
        /// </summary>
        [Test]
        public void ForgettingClearsEverySubject()
        {
            KeeperReports.Remember(ReportSubject.Name, "keeper-a");
            KeeperReports.Remember(ReportSubject.Grove, "keeper-b");

            KeeperReports.Forget();

            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Name, "keeper-a"));
            Assert.IsFalse(KeeperReports.AlreadySent(ReportSubject.Grove, "keeper-b"));
        }

        // ------------------------------------------------------------- the wire
        /// <summary>
        /// The wire spellings are permanent ids, for invariant 1's reason: the server keys a
        /// Firestore collection on this exact string, so a rename that looked like a tidy-up
        /// would file every later report into a collection nothing reads — silently, with a
        /// green build and a green suite.
        /// </summary>
        [Test]
        public void TheWireSpellingsAreTheOnesTheServerKeysOn()
        {
            Assert.AreEqual("name", ReportSubjects.Wire(ReportSubject.Name));
            Assert.AreEqual("grove", ReportSubjects.Wire(ReportSubject.Grove));
        }

        /// <summary>
        /// Every subject a panel can offer has copy to offer it with. A subject added to the enum
        /// and left out of the table draws its own key as the button's text, which is the failure
        /// invariant 6's build gate catches for a literal and cannot catch for a derived key.
        /// </summary>
        [Test]
        public void EverySubjectIsNamed()
        {
            // The shipped table off disk, not `Loc`: nothing publishes the string table in a
            // fixture, so asking `Loc.Has` would be asking an empty table. See `ShippedStrings`.
            var table = ShippedStrings.Table();

            foreach (var subject in ReportSubjects.All)
            {
                Assert.IsTrue(table.ContainsKey(ReportSubjects.ButtonKey(subject)),
                              $"{subject} has no button caption");
            }
        }

        /// <summary>
        /// And every member of the enum is either offered or named as held. A member in neither
        /// list is a subject the server will take reports about and no player can ever file one
        /// for — the shape invariant 40a describes for a raider kind nothing sends.
        ///
        /// <b>Two lists rather than one because a hold is a decision and an omission is a bug</b>,
        /// and from the enum alone they look identical. This is <c>TipTests</c>' rule for a
        /// retired lesson, asked of a report subject.
        /// </summary>
        [Test]
        public void EverySubjectIsEitherOfferedOrHeld()
        {
            foreach (ReportSubject subject in
                     System.Enum.GetValues(typeof(ReportSubject)))
            {
                bool offered = System.Array.IndexOf(ReportSubjects.All, subject) >= 0;
                bool held = System.Array.IndexOf(ReportSubjects.Held, subject) >= 0;

                Assert.IsTrue(offered || held,
                              $"{subject} is in the enum, is not on the panel and is not held");
                Assert.IsFalse(offered && held,
                               $"{subject} is offered and held at once");
            }
        }
    }
}
