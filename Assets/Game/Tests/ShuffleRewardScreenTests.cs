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
    /// <summary>Runs the real screen's ending hooks. Native components require the Editor.</summary>
    public sealed class ShuffleRewardScreenTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _host;
        ShuffleScreen _screen;
        SiegeBoard _board;

        [SetUp]
        public void Prepare()
        {
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            SaveService.Unload();
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore(), new AccountSwitchTests.MemoryArchive());
            _host = new GameObject("Shuffle reward ending test");
            _host.SetActive(false);
            _screen = _host.AddComponent<ShuffleScreen>();
            var view = _host.AddComponent<SiegeView>();
            var rules = new SiegeRules(ShuffleTests.Lane(), ShuffleTests.Breakers());
            _board = (SiegeBoard)rules.Fresh();
            typeof(SiegeView).GetField("_board", Private).SetValue(view, _board);
            typeof(SiegeScreen).GetField("_siege", Private).SetValue(_screen, view);
            var level = new LevelDefinition(LevelId.Parse("s13_shuffle"), ChapterId.Parse("s13_shufflewatch"),
                rules, LevelTuning.Climbing(24, .5f), new LevelPresentation(Vector2.zero, null, null, null));
            typeof(ModeScreen).GetProperty("Level", Private).SetValue(_screen, level);
        }

        [TearDown]
        public void Restore()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            SaveService.Unload();
            EndlessCoins.UseStore(null);
        }

        void Cleared(int waves) => typeof(SiegeBoard).GetField("_wave", Private).SetValue(_board, waves + 1);
        void Call(string name, params object[] args)
            => typeof(ShuffleScreen).GetMethod(name, Private).Invoke(_screen, args);

        [Test]
        public void ARepeatedEndingPaysAndReportsItsCoinsOnce()
        {
            Cleared(7);
            Call("Finished", 7);
            Call("Finished", 7);
            Assert.AreEqual(7L, ShuffleLedger.LifetimeWaves);
            Assert.AreEqual(70L, typeof(ProtoScreen).GetField("_banked", Private).GetValue(_screen));
        }

        [Test]
        public void ForfeitsPayOnlyClearedWavesAndCannotPayAgainAtAnotherEnding()
        {
            Call("NoteAbandoned", "back");
            Assert.AreEqual(0L, ShuffleLedger.LifetimeWaves);

            Call("Reseat");
            Cleared(4);
            Call("NoteAbandoned", "back");
            Call("NoteAbandoned", "back");
            Call("Finished", 4);
            Assert.AreEqual(4L, ShuffleLedger.LifetimeWaves);
        }

        [Test]
        public void ANewDealResetsThePayoutLatch()
        {
            Cleared(3);
            Call("Finished", 3);
            Call("Reseat");
            Cleared(2);
            Call("Finished", 2);
            Assert.AreEqual(5L, ShuffleLedger.LifetimeWaves);
        }
    }
}
