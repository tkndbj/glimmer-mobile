using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That a run's price survives the run.
    ///
    /// <para>
    /// <b>This fixture exists because of a bug that got as far as compiling, validating and
    /// passing 1,272 tests.</b> The stake was first latched at <c>Commit</c> and cleared by
    /// <c>Resolve</c>, which reads as obviously right — a run is owed for between those two
    /// calls and not otherwise. It is wrong, and nothing structural says so: <b>both modes call
    /// <c>Resolve</c> a few lines before <c>RunLedger.Loss</c></b>, deliberately, so that a
    /// crash in the middle of a defeat cannot charge twice. A stake cleared by <c>Resolve</c>
    /// therefore reads "free" at the exact instant the heart is taken — and every lost glade in
    /// the game becomes free, silently, with the heart gate still drawn on every screen.
    /// </para>
    /// <para>
    /// So the stake is a fact about the <em>level</em>, resolved once per screen, and these are
    /// the cases that pin it there. They build a bare <c>RunScreen</c> rather than a mode: the
    /// rule under test belongs to the base class, and a mode would drag a board in with it.
    /// Nothing calls <c>Init</c>, so no UI is built and no <c>Flow</c> is touched.
    /// </para>
    /// <para>
    /// Editor-only, like <c>RunGuardTests</c> next door and for the same reason: the marker is
    /// <c>PlayerPrefs</c> and the charge is <c>Wallet</c>, and a faked pair would prove the
    /// arithmetic rather than the thing that matters.
    /// </para>
    /// </summary>
    public sealed class RunStakeLifecycleTests
    {
        /// <summary>
        /// A <c>RunScreen</c> with no board, no panels and no opinions — just enough to answer
        /// the base class's questions and to let a test drive the two lifecycle calls.
        ///
        /// <para>
        /// It lives in the test assembly deliberately: <c>RunStakeTests</c> scans
        /// <c>typeof(RunScreen).Assembly</c> for modes carrying part of the stake, so a probe
        /// declared here cannot be mistaken for one.
        /// </para>
        /// </summary>
        sealed class StakeProbe : RunScreen
        {
            public LevelId Level = LevelId.None;
            public readonly List<string> Abandonments = new List<string>();

            protected override void Build() { }
            public override void RetryAfterDefeat() { }

            // `protected` rather than `protected internal`: overriding across assemblies drops
            // the internal half, which C# requires rather than merely allows.
            protected override LevelId StakeLevel => Level;
            protected override bool RunOver => false;
            protected override void Rewind() { }
            protected override void NoteAbandoned(string reason) => Abandonments.Add(reason);

            // The run frame. A probe never runs one, but it still has to answer: the two members
            // are abstract precisely so that nothing which is a run screen can decline to.
            protected override bool Runnable => false;
            protected override void Running(bool running) { }

            // The base class's own members, opened just wide enough to drive. Note what is
            // *not* reimplemented: the decision each of these reaches is still the base
            // class's, which is the whole point of testing here rather than in a mode.
            public bool Priced => Staked;
            public HeartPrice Cost => Price;
            public bool Begun => Committed;
            public void Begin() => Commit();
            public void End() => Resolve();

            /// <summary>
            /// The real exit, minus the navigation. <c>LeaveToMap</c> would run
            /// <c>Flow.Go&lt;LevelsScreen&gt;</c> and build a screen; where the player is sent
            /// is not what these tests are about, and the pricing decision in
            /// <c>ConfirmForfeit</c> is reached identically either way.
            /// </summary>
            public bool Left;
            public void WalkAway()
                => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "back", () => Left = true);
        }

        const string Free = "g1", Paid = "g4", Beaten = "g5";

        LevelCatalog _catalogBefore;
        readonly List<StakeProbe> _probes = new List<StakeProbe>();

        [SetUp]
        public void Publish()
        {
            _catalogBefore = GameContent.Catalog;
            GameContent.Publish(LevelCatalog.FromLoaded(Catalog(), Array.Empty<ChapterBody>()));

            Grace(3);

            // A fresh save as well as a fresh table: the stake now reads what the player has
            // finished, and another fixture in this assembly may have left records behind. The
            // offline runner promises no order, so independence is taken rather than assumed.
            PlayerProgress.LoadFrom(new SaveFileDto());

            RunGuard.Resolve();
            RunGuard.NoteReported();
        }

        [TearDown]
        public void Restore()
        {
            for (int i = 0; i < _probes.Count; i++)
                if (_probes[i] != null) UnityEngine.Object.DestroyImmediate(_probes[i].gameObject);
            _probes.Clear();

            RunGuard.Resolve();
            RunGuard.NoteReported();
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            GameContent.Publish(_catalogBefore);
        }

        /// <summary>
        /// Records the named glades as finished, straight into the save. Not through
        /// <c>RecordRun</c>, which would write a file.
        /// </summary>
        static void Finished(params string[] levels)
        {
            var dto = new SaveFileDto { levels = new LevelRecordDto[levels.Length] };

            for (int i = 0; i < levels.Length; i++)
                dto.levels[i] = new LevelRecordDto
                {
                    levelId = levels[i],
                    stars = 1,
                    bestMoves = 10,
                    clears = 1,
                };

            PlayerProgress.LoadFrom(dto);
        }

        /// <summary>One five-glade chapter, so the window covers the first three of it.</summary>
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "g1", "g2", "g3", "g4", "g5" },
            }, 1);
            return builder.Build();
        }

        /// <summary>Republishes the rules table with a different free window.</summary>
        static void Grace(int levels)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                hearts = new HeartsDto { graceLevels = levels },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        StakeProbe On(string level)
        {
            var probe = new GameObject("StakeProbe").AddComponent<StakeProbe>();
            probe.Level = LevelId.Parse(level);
            _probes.Add(probe);
            return probe;
        }

        // ------------------------------------------------------------------- the fact
        [Test]
        public void AFreeOpeningIsNotStakedAndALaterGladeIs()
        {
            Assert.IsFalse(On(Free).Priced, "the third glade of the first chapter");
            Assert.IsTrue(On(Paid).Priced, "the fourth");
        }

        [Test]
        public void AGladeAlreadyFinishedIsNotStakedEitherAndSaysWhy()
        {
            // The second clause, at the screen. Same catalog, same window — the only thing that
            // moved is what the player has beaten.
            Finished(Beaten);

            var beaten = On(Beaten);
            Assert.IsFalse(beaten.Priced, "a glade this player has already finished");
            Assert.AreEqual(HeartPrice.Replay, beaten.Cost);

            Assert.IsTrue(On(Paid).Priced, "and its unfinished neighbour still costs");
            Assert.AreEqual(HeartPrice.Opening, On(Free).Cost, "the window still says so first");
        }

        [Test]
        public void AFinishedGladeIsWalkedAwayFromWithoutBeingAskedAbout()
        {
            // What the player actually reported feeling: a warning panel about a heart nobody
            // is taking. ConfirmForfeit asks only when there is something to charge, so the
            // exit has to complete on its own with no modal raised — which is what Left being
            // true synchronously proves, since the confirmation would leave it false until
            // somebody tapped it.
            Finished(Beaten);

            var probe = On(Beaten);
            probe.Begin();
            probe.WalkAway();

            Assert.IsTrue(probe.Left, "leaving a glade you have beaten stopped to ask");
            Assert.IsFalse(probe.Begun, "and it is still forfeited rather than left owed for");
            CollectionAssert.AreEqual(new[] { "back" }, probe.Abandonments,
                                      "the abandonment is written down whatever it cost");
        }

        [Test]
        public void ClearingAGladeMidScreenNeverTurnsAFreeRunIntoAChargedOne()
        {
            // The latch, in the direction that matters. A price the player has been told is
            // free is kept for the life of the screen however the rules move underneath it;
            // the opposite direction — a first clear making a restart free — costs nobody
            // anything and is the honest reading of a rule that just changed in their favour.
            var probe = On(Free);
            Assert.IsFalse(probe.Priced);

            Grace(0);
            Assert.IsFalse(probe.Priced, "a content push mid-run cannot start charging for it");
        }

        [Test]
        public void TheStakeIsKnownBeforeTheRunIsCommittedTo()
        {
            // Read before Commit, which is what the door on the map effectively asks and what a
            // mode may ask at any point in its own ending.
            var probe = On(Paid);

            Assert.IsFalse(probe.Begun);
            Assert.IsTrue(probe.Priced, "the price of a board does not wait for somebody to play it");
        }

        // ------------------------------------------------------- the bug, both directions
        [Test]
        public void ResolvingARunDoesNotTurnAPaidGladeFree()
        {
            // The regression, stated as the modes actually sequence it: Resolve, *then* the
            // ledger reads the stake. A stake tied to the run's lifecycle reads false here and
            // no lost glade in the game is ever charged for again.
            var probe = On(Paid);

            probe.Begin();
            probe.End();

            Assert.IsTrue(probe.Priced,
                          "Resolve ran before RunLedger.Loss, so clearing the stake here makes "
                          + "every defeat free");
        }

        [Test]
        public void ResolvingARunDoesNotTurnAFreeGladeIntoAChargedOne()
        {
            // The other direction, which would take a heart off the beginner the window exists
            // to protect.
            var probe = On(Free);

            probe.Begin();
            probe.End();

            Assert.IsFalse(probe.Priced);
        }

        [Test]
        public void TheStakeIsTheSameAnswerEveryTimeItIsAsked()
        {
            // What lets a mode read it from anywhere in an ending without thinking about order.
            var probe = On(Free);

            bool before = probe.Priced;
            probe.Begin();
            bool during = probe.Priced;
            probe.End();
            bool after = probe.Priced;

            Assert.AreEqual(before, during);
            Assert.AreEqual(during, after);
        }

        // ------------------------------------------------------------------ the marker
        [Test]
        public void AFreeRunLeavesNothingForTheNextLaunchToChargeFor()
        {
            On(Free).Begin();

            Assert.IsFalse(RunGuard.Claim(),
                           "a free run that the process never finished owes nothing, and Boot "
                           + "cannot ask whether it did — no content is loaded there");
        }

        [Test]
        public void APaidRunIsStillWrittenDownForTheNextLaunch()
        {
            // The half that must not be lost in making the other half free.
            On(Paid).Begin();

            Assert.IsTrue(RunGuard.Claim());
            Assert.AreEqual(LevelId.Parse(Paid), RunGuard.Unfinished);
        }

        // ----------------------------------------------------------------- the forfeit
        [Test]
        public void WalkingOutOfAFreeRunIsStillWrittenDownAsAnAbandonment()
        {
            // Free means "costs no heart", never "did not happen". The analytics still want it,
            // and the run still has to stop being owed for.
            var probe = On(Free);
            probe.Begin();

            probe.WalkAway();

            CollectionAssert.AreEqual(new[] { "back" }, probe.Abandonments);
            Assert.IsTrue(probe.Left, "and the player still goes where they asked to go");
            Assert.IsFalse(probe.Begun, "a forfeited run is resolved whatever it cost");
        }
    }
}
