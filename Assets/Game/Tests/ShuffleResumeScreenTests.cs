using System.Reflection;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Shuffle;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The screen's half of a suspended Shuffle run (MODES.md 59s): picking one up, paying for the
    /// waves it carried exactly once, and letting go of it at every way a run stops. Runs the
    /// real screen's hooks, as <see cref="ShuffleRewardScreenTests"/> does; native components
    /// require the Editor.
    /// </summary>
    public sealed class ShuffleResumeScreenTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly LevelId Level = LevelId.Parse("s13_shuffle");

        GameObject _host;
        ShuffleScreen _screen;
        SiegeView _view;
        EndlessCoins.MemoryStore _store;

        [SetUp]
        public void Prepare()
        {
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            _store = new EndlessCoins.MemoryStore();
            ShuffleResume.UseStore(_store);
            SaveService.Unload();
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore(), new AccountSwitchTests.MemoryArchive());

            _host = new GameObject("Shuffle resume test");
            _host.SetActive(false);
            _screen = _host.AddComponent<ShuffleScreen>();
            _view = _host.AddComponent<SiegeView>();
            typeof(SiegeScreen).GetField("_siege", Private).SetValue(_screen, _view);

            var rules = new SiegeRules(ShuffleTests.Lane(), ShuffleTests.Breakers());
            var level = new LevelDefinition(Level, ChapterId.Parse("s13_shufflewatch"),
                rules, LevelTuning.Climbing(24, .5f), new LevelPresentation(Vector2.zero, null, null, null));
            typeof(ModeScreen).GetProperty("Level", Private).SetValue(_screen, level);
            Stand(rules.Fresh());
        }

        [TearDown]
        public void Restore()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            RunGuard.Resolve();
            SaveService.Unload();
            ShuffleResume.UseStore(null);
            EndlessCoins.UseStore(null);
        }

        // ------------------------------------------------------------------ helpers
        SiegeBoard Board => _view.Siege;

        void Stand(IProtoBoard board) => typeof(SiegeView).GetField("_board", Private).SetValue(_view, board);

        object Call(string name, params object[] args)
            => typeof(ShuffleScreen).GetMethod(name, Private).Invoke(_screen, args);

        T Field<T>(string name) => (T)typeof(ShuffleScreen).GetField(name, Private).GetValue(_screen);

        bool Committed => (bool)typeof(RunScreen).GetProperty("Committed", Private).GetValue(_screen);

        /// <summary>The board a run reopened on wave <paramref name="wave"/> is dealt.</summary>
        void StandAt(int wave)
            => Stand(new SiegeRules(ShuffleTests.Lane(), ShuffleTests.Breakers()).From(new SiegeStart(wave, 0)).Fresh());

        /// <summary>The board, having seen off <paramref name="waves"/> waves of the whole run.</summary>
        void Cleared(int waves) => typeof(SiegeBoard).GetField("_wave", Private).SetValue(Board, waves + 1);

        /// <summary>A run of two hands, the second on the table, kept in the store.</summary>
        ShuffleSnapshot Waiting(string swapCard = null)
        {
            var run = new ShuffleRun(41u);
            var hand = run.Deal();
            Assert.IsTrue(run.Take(hand[0]));
            run.Deal();

            var board = new SiegeRules(ShuffleTests.Lane(), ShuffleTests.Breakers()).Fresh() as SiegeBoard;
            board.Boosts = run.Build.Boosts;
            board.Refit();

            var snapshot = ShuffleSnapshot.Of(Level, run, board, null, 1, 0);
            if (swapCard != null)
            {
                string encoded = snapshot.Encode().Replace("\"" + hand[0].Id + "\"", "\"" + swapCard + "\"");
                Assert.IsTrue(ShuffleSnapshot.TryDecode(encoded, out snapshot, out string fault), fault);
            }

            ShuffleResume.Write(snapshot);
            return snapshot;
        }

        // ------------------------------------------------------------------ picking up
        [Test]
        public void AWaitingRunIsFoldedBackBeforeTheBoardIsDealt()
        {
            var snapshot = Waiting();

            Call("Pickup");

            Assert.IsNotNull(Field<ShuffleSnapshot>("_resume"), "the waiting run was not picked up");
            var run = Field<ShuffleRun>("_resumed");
            Assert.AreEqual(snapshot.Hands, run.Hands);
            Assert.IsNotNull(run.Open, "the hand on the table was not put back");
            Assert.IsTrue(ShuffleResume.Holds(Level), "picking a run up let go of it before it was seated");
        }

        [Test]
        public void ARunThatCannotBeResumedIsLetGoAndItsWavesPaidOnce()
        {
            var snapshot = Waiting(swapCard: "a_card_withdrawn_by_an_update");

            Call("Pickup");

            Assert.IsNull(Field<ShuffleSnapshot>("_resume"));
            Assert.IsFalse(ShuffleResume.Holds(Level), "an unresumable run was kept");
            Assert.AreEqual(snapshot.Wave - 1, ShuffleLedger.LifetimeWaves, "its cleared waves were not paid");

            Call("Pickup");
            Assert.AreEqual(snapshot.Wave - 1, ShuffleLedger.LifetimeWaves, "its waves were paid twice");
        }

        // ------------------------------------------------------------------ seating
        [Test]
        public void AResumedRunIsSeatedCommittedAndPaidForEveryWaveItClearedOnce()
        {
            var snapshot = Waiting();
            Call("Pickup");
            StandAt(snapshot.Wave);

            Call("Reseat");

            var run = _screen.Run;
            Assert.AreEqual(snapshot.Hands, run.Hands);
            Assert.AreSame(run.Build.Boosts, Board.Boosts, "the board is not reading the resumed build");
            Assert.AreEqual(snapshot.Wave - 1, Field<int>("_carried"));
            Assert.IsTrue(Committed, "a resumed run was not committed, so its first move would charge a second heart");
            Assert.IsTrue(Field<bool>("_offerCarried"), "the hand on the table is not owed a panel");

            // Three more waves on the resumed board, then the player walks away.
            Cleared(snapshot.Wave - 1 + 3);
            Assert.AreEqual(3, Board.WavesThisRun);

            Call("NoteAbandoned", "back");
            Assert.AreEqual(snapshot.Wave - 1 + 3, ShuffleLedger.LifetimeWaves, "the run was not paid for every wave it cleared");
            Assert.IsFalse(ShuffleResume.Holds(Level), "a forfeited run could be resumed");

            Call("RunEnded", false);
            Assert.AreEqual(snapshot.Wave - 1 + 3, ShuffleLedger.LifetimeWaves, "an ending after the forfeit paid again");
        }

        [Test]
        public void AFreshRunCarriesNothing()
        {
            Call("Reseat");

            Assert.AreEqual(0, Field<int>("_carried"));
            Assert.IsFalse(Committed);
            Cleared(4);

            Call("NoteAbandoned", "back");
            Assert.AreEqual(4L, ShuffleLedger.LifetimeWaves);
        }

        // ------------------------------------------------------------------ suspending
        [Test]
        public void OnlyACommittedRunIsSuspendedAndALostLineLetsItGo()
        {
            Call("Reseat");
            _screen.Run.Deal();

            Call("Suspend");
            Assert.IsFalse(ShuffleResume.Holds(Level), "a run nobody committed to (and paid no heart for) was kept");

            typeof(RunScreen).GetMethod("Commit", Private).Invoke(_screen, null);
            Call("Suspend");
            Assert.IsTrue(ShuffleResume.Holds(Level), "a committed run at a hand was not kept");
            Assert.AreEqual(ShuffleSnapshot.WaveAfter(1), ShuffleResume.WaveFor(Level));

            for (int i = 0; i < Board.Wards.Count; i++) Board.Seat(i, false, 0, 0f, 0, 0);
            Call("Running", false);
            Assert.IsFalse(ShuffleResume.Holds(Level), "a fallen line could be resumed past the continue offer");
        }

        [Test]
        public void ARunIsKeptFromItsFirstMoveAndSaveAndQuitKeepsIt()
        {
            Call("Reseat");

            Call("Running", true);
            Assert.IsFalse(ShuffleResume.Holds(Level), "a run nobody had moved in was kept");

            typeof(RunScreen).GetMethod("Commit", Private).Invoke(_screen, null);
            Call("Running", true);
            Assert.AreEqual(1, ShuffleResume.WaveFor(Level), "a run was not kept from its first move");

            Assert.IsTrue((bool)typeof(ShuffleScreen).GetProperty("Suspends", Private).GetValue(_screen),
                          "the pause menu would not offer SAVE & QUIT on this lane");
            Assert.IsFalse(ShuffleLedger.LifetimeWaves > 0, "keeping a run paid for it");
        }

        [Test]
        public void ARestartLetsGoOfTheRunAndOpensAtWaveOne()
        {
            var snapshot = Waiting();
            Call("Arm", true);
            Assert.IsTrue(Field<bool>("_partway"), "a resumed run's rules do not open part-way up");
            StandAt(snapshot.Wave);
            Call("Reseat");
            Assert.IsTrue(ShuffleResume.Holds(Level));

            Call("Forget");
            Call("Arm", false);

            Assert.IsFalse(ShuffleResume.Holds(Level), "a restarted run could still be resumed");
            Assert.IsNull(Field<ShuffleSnapshot>("_resume"));
            Assert.IsFalse(Field<bool>("_partway"), "a restart would open part-way up");
        }
    }
}
