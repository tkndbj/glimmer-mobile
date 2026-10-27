using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A Shuffle run outliving the app (MODES.md 59s): the snapshot taken at a hand, the store it
    /// waits in, and the run folded back from it.
    ///
    /// <para>
    /// <b>The fixture that matters is <see cref="AResumedRunIsTheRunThatWasSuspended"/></b>: two
    /// runs dealt from one seed, one carried through a snapshot and one never stopped, take the
    /// same cards for a dozen hands and must hold the same build, figure for figure (read by
    /// reflection, so a figure added later is compared without anybody remembering to), and be
    /// dealt the same hands. A resume that drifted anywhere - a lost life, a stream moved, a
    /// hand number off by one - would be a different run, and this says where.
    /// </para>
    /// <para>
    /// Pure Domain and a memory store throughout, so every case here runs offline.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ShuffleResumeTests
    {
        static readonly LevelId Level = LevelId.Parse("s13_shuffle");

        EndlessCoins.MemoryStore _store;

        [SetUp]
        public void Prepare()
        {
            _store = new EndlessCoins.MemoryStore();
            ShuffleResume.UseStore(_store);
            CloudState.Reset();
        }

        [TearDown]
        public void Restore()
        {
            ShuffleResume.UseStore(null);
            ShuffleConfig.UseStore(null);
            CloudState.Reset();
        }

        // ------------------------------------------------------------------ helpers
        static SiegeBoard Board(ShuffleRun run)
        {
            var board = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
            board.Boosts = run.Build.Boosts;
            board.Refit();
            return board;
        }

        /// <summary>Deals <paramref name="hands"/> hands into <paramref name="run"/>, taking the card at <paramref name="pick"/> of each.</summary>
        static void Play(ShuffleRun run, int hands, Func<int, int> pick)
        {
            for (int h = 0; h < hands; h++)
            {
                var hand = run.Deal();
                if (hand.Count == 0) continue;
                Assert.IsTrue(run.Take(hand[pick(run.Hands) % hand.Count]), "a dealt card was refused");
            }
        }

        /// <summary>Every figure a build exposes, read by reflection, plus its stream.</summary>
        static string Figures(SiegeBoosts boosts)
        {
            var parts = new List<string>();
            foreach (var p in typeof(SiegeBoosts).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                 .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                                                 .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var t = p.PropertyType;
                if (t.IsPrimitive || t.IsEnum) parts.Add(p.Name + "=" + p.GetValue(boosts));
            }

            parts.Add("stream=" + boosts.Stream);
            return string.Join("|", parts);
        }

        static string Ids(IReadOnlyList<ShuffleCard> cards)
            => cards == null ? string.Empty : string.Join(",", cards.Select(c => c.Id));

        static ShuffleSnapshot RoundTrip(ShuffleSnapshot snapshot)
        {
            Assert.IsTrue(ShuffleSnapshot.TryDecode(snapshot.Encode(), out var back, out string fault), fault);
            return back;
        }

        // ------------------------------------------------------------------ the run
        [Test]
        public void AResumedRunIsTheRunThatWasSuspended()
        {
            foreach (uint seed in new[] { 1u, 77u, 948309542u, 4000000001u })
            {
                Func<int, int> pick = hand => (int)((seed + (uint)hand * 7u) % 5u);

                var walked = new ShuffleRun(seed);
                var carried = new ShuffleRun(seed);

                for (int hand = 1; hand <= 12; hand++)
                {
                    // Suspend the carried run with its hand on the table, as the screen does,
                    // and pick it up again before anything is taken.
                    var dealt = carried.Deal();
                    var board = Board(carried);
                    var snapshot = RoundTrip(ShuffleSnapshot.Of(Level, carried, board, null, 0, 0));

                    carried = ShuffleRun.Resume(snapshot, ShuffleTuning.Shipped, out string fault);
                    Assert.IsNotNull(carried, $"seed {seed}, hand {hand}: {fault}");

                    var walkedHand = walked.Deal();
                    Assert.AreEqual(Ids(walkedHand), Ids(carried.Open), $"seed {seed}: hand {hand} was dealt differently");
                    Assert.AreEqual(walked.Hands, carried.Hands, $"seed {seed}: hand {hand} counted differently");
                    if (walkedHand.Count == 0) continue;

                    int at = pick(hand) % walkedHand.Count;
                    Assert.IsTrue(walked.Take(walkedHand[at]));
                    Assert.IsTrue(carried.Take(carried.Open[at]));

                    Assert.AreEqual(Figures(walked.Build.Boosts), Figures(carried.Build.Boosts),
                                    $"seed {seed}: the builds parted at hand {hand}");
                    Assert.AreEqual(Ids(walked.Build.Taken), Ids(carried.Build.Taken));
                    Assert.AreEqual(walked.Build.Bonds.Count, carried.Build.Bonds.Count);
                    Assert.AreEqual(dealt.Count, walkedHand.Count);
                }
            }
        }

        [Test]
        public void ARunSuspendedBetweenHandsIsDealtItsNextHandUnchanged()
        {
            var walked = new ShuffleRun(31u);
            var carried = new ShuffleRun(31u);
            Play(walked, 5, h => h);
            Play(carried, 5, h => h);

            var snapshot = RoundTrip(ShuffleSnapshot.Of(Level, carried, Board(carried), null, 0, 0));
            Assert.AreEqual(0, snapshot.Open.Count, "a hand already taken from was suspended as open");

            carried = ShuffleRun.Resume(snapshot, ShuffleTuning.Shipped, out string fault);
            Assert.IsNotNull(carried, fault);
            Assert.IsNull(carried.Open);

            Assert.AreEqual(Ids(walked.Deal()), Ids(carried.Deal()));
            Assert.AreEqual(Figures(walked.Build.Boosts), Figures(carried.Build.Boosts));
        }

        [Test]
        public void ARefreshIsCarriedSoItsPriceKeepsDoubling()
        {
            var run = new ShuffleRun(5u);
            run.Deal();
            Assert.IsTrue(run.CanRefresh);
            Assert.IsNotNull(run.Refresh());
            Assert.IsNotNull(run.Refresh());

            var back = ShuffleRun.Resume(RoundTrip(ShuffleSnapshot.Of(Level, run, Board(run), null, 0, 0)),
                                         ShuffleTuning.Shipped, out string fault);
            Assert.IsNotNull(back, fault);

            Assert.AreEqual(2, back.Refreshes);
            Assert.AreEqual(run.RefreshPrice, back.RefreshPrice);
            Assert.AreEqual(Ids(run.Open), Ids(back.Open), "the refreshed hand is not the one put back");
            Assert.AreEqual(Ids(run.Refresh()), Ids(back.Refresh()), "the next refresh was dealt differently");
        }

        [Test]
        public void LivesSpentStaySpent()
        {
            var wind = ShuffleCards.All.First(c => c.Effect == ShuffleEffect.SecondWind);
            var phoenix = ShuffleCards.All.First(c => c.Effect == ShuffleEffect.Phoenix);

            // Three hands dealt, two of them answered by the two lives: a snapshot holds no more
            // cards than the run was dealt hands for.
            var run = new ShuffleRun(9u);
            run.Deal();
            run.Deal();
            Assert.IsTrue(run.Build.Take(wind));
            Assert.IsTrue(run.Build.Take(phoenix));
            run.Deal();

            int winds = run.Build.Boosts.SecondWinds;
            Assert.Greater(winds, 0);
            Assert.IsTrue(run.Build.Boosts.TakeSecondWind());
            Assert.IsTrue(run.Build.Boosts.TakePhoenix());

            var back = ShuffleRun.Resume(RoundTrip(ShuffleSnapshot.Of(Level, run, Board(run), null, 0, 0)),
                                         ShuffleTuning.Shipped, out string fault);
            Assert.IsNotNull(back, fault);
            Assert.AreEqual(winds - 1, back.Build.Boosts.SecondWinds, "a spent second wind came back");
            Assert.AreEqual(run.Build.Boosts.Phoenixes, back.Build.Boosts.Phoenixes, "a spent phoenix came back");
        }

        [Test]
        public void ARunOnAPublishedTuningIsResumedOnThatTuning()
        {
            string doc = ShuffleTuning.Shipped.ToJson(7);
            Assert.IsTrue(ShuffleTuning.TryRead(doc, false, out var tuning, out string fault), fault);

            var run = new ShuffleRun(12u, tuning);
            run.Deal();

            var snapshot = RoundTrip(ShuffleSnapshot.Of(Level, run, Board(run), doc, 0, 0));
            Assert.AreEqual(doc, snapshot.Tuning, "the document did not survive the codec");

            Assert.IsTrue(ShuffleTuning.TryRead(snapshot.Tuning, false, out var reread, out fault), fault);
            var back = ShuffleRun.Resume(snapshot, reread, out fault);
            Assert.IsNotNull(back, fault);
            Assert.AreEqual(7, back.Tuning.Revision);
        }

        [Test]
        public void TheConfigHandsOverTheDocumentItsTuningCameFrom()
        {
            ShuffleConfig.UseStore(new EndlessCoins.MemoryStore());

            var shipped = ShuffleConfig.CurrentWithBody(out string none);
            Assert.AreSame(ShuffleTuning.Shipped, shipped);
            Assert.IsNull(none, "the build's own figures named a document");

            string doc = ShuffleTuning.Shipped.ToJson(4);
            Assert.IsTrue(ShuffleConfig.Adopt(doc));

            var published = ShuffleConfig.CurrentWithBody(out string body);
            Assert.AreEqual(doc, body);
            Assert.AreEqual(4, published.Revision);
            Assert.AreSame(ShuffleConfig.Current, published);
        }

        // ------------------------------------------------------------------ refusals
        [Test]
        public void AWithdrawnCardRefusesTheWholeRun()
        {
            var run = new ShuffleRun(3u);
            Play(run, 3, h => 0);

            string encoded = ShuffleSnapshot.Of(Level, run, Board(run), null, 0, 0).Encode();
            string first = run.Build.Taken[0].Id;
            encoded = encoded.Replace("\"" + first + "\"", "\"no_such_card\"");

            Assert.IsTrue(ShuffleSnapshot.TryDecode(encoded, out var snapshot, out string fault), fault);
            Assert.IsNull(ShuffleRun.Resume(snapshot, ShuffleTuning.Shipped, out fault));
            StringAssert.Contains("no_such_card", fault);
        }

        [Test]
        public void ADamagedOrForeignSnapshotIsRefusedAndNeverThrows()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            string good = ShuffleSnapshot.Of(Level, run, Board(run), null, 0, 0).Encode();

            var bad = new Dictionary<string, string>
            {
                ["empty"] = "",
                ["not json"] = "{{{",
                ["array"] = "[]",
                ["future version"] = good.Replace("{\"v\":1,", "{\"v\":2,"),
                ["unknown field"] = good.Replace("{\"v\":1,", "{\"v\":1,\"extra\":0,"),
                ["missing field"] = good.Replace(",\"phoenixes\":0", ""),
                ["wave off its hand"] = good.Replace("\"wave\":3", "\"wave\":4"),
                ["negative hands"] = good.Replace("\"hands\":1", "\"hands\":-1"),
                ["a hand on the table with none dealt"] = good.Replace("\"hands\":1", "\"hands\":0").Replace("\"wave\":3", "\"wave\":1"),
                ["negative health"] = good.Replace("[0,1,", "[0,1,-"),
                ["bad level"] = good.Replace("s13_shuffle", "Not A Level!"),
                ["fuel as text"] = System.Text.RegularExpressions.Regex.Replace(good, @"\[0,1,(\d+),([^,]+),", "[0,1,$1,\"x\","),
                ["cut short"] = good.Substring(0, good.Length / 2),
            };

            foreach (var row in bad)
            {
                Assert.AreNotEqual(good, row.Value, $"the '{row.Key}' case did not change the document");
                Assert.IsFalse(ShuffleSnapshot.TryDecode(row.Value, out var snapshot, out string fault), row.Key);
                Assert.IsNull(snapshot, row.Key);
                Assert.IsNotNull(fault, row.Key);
            }

            Assert.IsTrue(ShuffleSnapshot.TryDecode(good, out _, out string ok), ok);
        }

        [Test]
        public void ALineWithNobodyStandingIsNotSomewhereToResume()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            var board = Board(run);

            for (int i = 0; i < board.Wards.Count; i++)
                Assert.IsTrue(board.Seat(i, false, 0, 0f, 0, 0));

            Assert.IsTrue(board.IsFinished);
            Assert.IsNull(ShuffleSnapshot.Of(Level, run, board, null, 0, 0), "a lost board was suspended");
        }

        [Test]
        public void ARunKeptBeforeItsFirstHandReopensOnWaveOne()
        {
            var run = new ShuffleRun(3u);
            Assert.IsNull(ShuffleSnapshot.Of(LevelId.None, run, Board(run), null, 0, 0));

            var board = Board(run);
            board.Wards[0].Health = Math.Max(1, board.Wards[0].Full / 2);

            var snapshot = RoundTrip(ShuffleSnapshot.Of(Level, run, board, null, 0, 0));
            Assert.AreEqual(0, snapshot.Hands);
            Assert.AreEqual(1, snapshot.Wave, "a run with no hand yet reopens anywhere but wave one");
            Assert.AreEqual(0, snapshot.Taken.Count);
            Assert.AreEqual(0, snapshot.Open.Count);

            var back = ShuffleRun.Resume(snapshot, ShuffleTuning.Shipped, out string fault);
            Assert.IsNotNull(back, fault);
            Assert.AreEqual(0, back.Hands);
            Assert.IsNull(back.Open);

            // Wave one is the ordinary opening: the board a resume deals is the board a fresh run deals.
            var resumed = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers(), new SiegeStart(snapshot.Wave, 0));
            Assert.AreEqual(1, resumed.StartWave);
            resumed.Boosts = back.Build.Boosts;
            resumed.Refit();
            snapshot.Seat(resumed);
            Assert.AreEqual(board.Wards[0].Health, resumed.Wards[0].Health);

            // And the first hand is dealt exactly as a walked run's first hand.
            Assert.AreEqual(Ids(new ShuffleRun(3u).Deal()), Ids(back.Deal()));
        }

        [Test]
        public void TheSaveLessonIsLiveAndWorded()
        {
            CollectionAssert.Contains(Mechanic.All, Mechanic.ShuffleSave);
            CollectionAssert.DoesNotContain(Mechanic.Retired, Mechanic.ShuffleSave);
            Assert.AreEqual(0, Mechanic.ShuffleSave.Args, "the lesson's sentence names no figure");
        }

        [Test]
        public void ASnapshotOnlyFitsTheLineItWasTakenOn()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            var snapshot = ShuffleSnapshot.Of(Level, run, Board(run), null, 0, 0);

            Assert.IsTrue(snapshot.Fits(ShuffleTests.Lane(), out string fault), fault);
            Assert.IsFalse(snapshot.Fits(null, out fault));

            string encoded = snapshot.Encode().Replace("\"posts\":[[0,", "\"posts\":[[1,");
            Assert.IsTrue(ShuffleSnapshot.TryDecode(encoded, out var moved, out fault), fault);
            Assert.IsFalse(moved.Fits(ShuffleTests.Lane(), out fault), "a post of another colour was seated");
        }

        // ------------------------------------------------------------------ the board
        [Test]
        public void EveryPostIsSeatedAsItStood()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            var board = Board(run);

            var w = board.Wards;
            w[0].Health = Math.Max(1, w[0].Full / 3);
            w[0].Fuel = w[0].Capacity * .37f;
            w[0].Charges = 1;
            w[1].Health = 0; w[1].Alive = false; w[1].Fuel = 0f;
            w[2].Fuel = w[2].Capacity * .999f;
            w[3].Rank = 2;

            var snapshot = RoundTrip(ShuffleSnapshot.Of(Level, run, board, null, 0, 0));

            var back = ShuffleRun.Resume(snapshot, ShuffleTuning.Shipped, out string fault);
            Assert.IsNotNull(back, fault);

            var resumed = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers(), new SiegeStart(snapshot.Wave, 0));
            resumed.Boosts = back.Build.Boosts;
            resumed.Refit();
            snapshot.Seat(resumed);

            for (int i = 0; i < w.Count; i++)
            {
                var a = w[i];
                var b = resumed.Wards[i];
                Assert.AreEqual(a.Alive, b.Alive, $"post {i} standing");
                Assert.AreEqual(a.Health, b.Health, $"post {i} health");
                Assert.AreEqual(a.Fuel, b.Fuel, $"post {i} fuel was not restored to the bit");
                Assert.AreEqual(a.Charges, b.Charges, $"post {i} charges");
                Assert.AreEqual(a.Rank, b.Rank, $"post {i} rank");
            }

            Assert.AreEqual(board.WardsStanding, resumed.WardsStanding);
        }

        [Test]
        public void ASeatIsClampedToWhatTheBuildAllows()
        {
            var run = new ShuffleRun(3u);
            var board = Board(run);
            var post = board.Wards[0];

            Assert.IsTrue(board.Seat(0, true, post.Full * 10, post.Capacity * 10f, 999, 99));
            Assert.AreEqual(post.Full, post.Health);
            Assert.AreEqual(post.Capacity, post.Fuel);
            Assert.AreEqual(post.ChargeCap, post.Charges);
            Assert.AreEqual(SiegeTuning.MaxRank, post.Rank);

            Assert.IsTrue(board.Seat(0, true, -5, float.NaN, -1, -1));
            Assert.AreEqual(1, post.Health, "a standing post was seated with no health");
            Assert.AreEqual(0f, post.Fuel);
            Assert.AreEqual(0, post.Charges);
            Assert.AreEqual(0, post.Rank);

            Assert.IsFalse(board.Seat(-1, true, 1, 0f, 0, 0));
            Assert.IsFalse(board.Seat(board.Wards.Count, true, 1, 0f, 0, 0));
        }

        [Test]
        public void TheResumedBoardOpensOnTheWaveAfterTheHandAndOwesNothingYet()
        {
            var walked = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
            var run = new ShuffleRun(3u);
            walked.Boosts = run.Build.Boosts;
            walked.Refit();
            walked.Sheltered = true;

            // Walk to the first hand, as AHandIsOwedEveryTwoWaves does.
            for (int i = 0; i < 60 * 80 && !run.Owed(walked); i++) walked.Advance(1f / 60f);
            Assert.IsTrue(run.Owed(walked), "no hand came");
            run.Deal();

            var snapshot = RoundTrip(ShuffleSnapshot.Of(Level, run, walked, null, 0, 0));
            Assert.AreEqual(3, snapshot.Wave, "the first hand reopens anywhere but wave three");

            var back = ShuffleRun.Resume(snapshot, ShuffleTuning.Shipped, out string fault);
            Assert.IsNotNull(back, fault);
            Assert.IsTrue(back.Take(back.Open[0]));

            var resumed = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers(), new SiegeStart(snapshot.Wave, 0));
            resumed.Boosts = back.Build.Boosts;
            resumed.Refit();
            snapshot.Seat(resumed);
            resumed.Sheltered = true;

            Assert.AreEqual(snapshot.Wave, resumed.StartWave);
            Assert.AreEqual(snapshot.Wave - 1, resumed.WavesCleared, "the resumed board forgot the waves behind it");
            Assert.AreEqual(0, resumed.WavesThisRun, "the resumed board counted waves it never saw as its own");
            Assert.IsFalse(back.Owed(resumed), "a hand was owed the moment the run came back");

            // The next hand is owed exactly where a walked run would owe it: two waves on.
            for (int i = 0; i < 60 * 200 && !back.Owed(resumed); i++) resumed.Advance(1f / 60f);
            Assert.IsTrue(back.Owed(resumed), "the resumed run was never owed its second hand");
            int cleared = resumed.OnTheHill == 0 && resumed.Wave > resumed.WavesCleared ? resumed.Wave : resumed.WavesCleared;
            Assert.AreEqual(4, cleared, "the second hand came somewhere a walked run would not deal it");
        }

        // ------------------------------------------------------------------ the store
        [Test]
        public void TheStoreKeepsOneRunPerLevelAndAccount()
        {
            var run = new ShuffleRun(3u);
            Play(run, 2, h => 1);
            run.Deal();
            var snapshot = ShuffleSnapshot.Of(Level, run, Board(run), null, 2, 1);

            Assert.IsFalse(ShuffleResume.Holds(Level));
            Assert.AreEqual(0, ShuffleResume.WaveFor(Level));

            SignIn("uid-a");
            ShuffleResume.Write(snapshot);
            Assert.IsTrue(ShuffleResume.Holds(Level));
            Assert.AreEqual(snapshot.Wave, ShuffleResume.WaveFor(Level));
            Assert.AreEqual(snapshot.Encode(), ShuffleResume.Read(Level).Encode());
            Assert.IsFalse(ShuffleResume.Holds(LevelId.Parse("s02_endless")), "another level saw the run");

            SignIn("uid-b");
            Assert.IsFalse(ShuffleResume.Holds(Level), "another account saw the run");
            ShuffleResume.Clear(Level);

            SignIn("uid-a");
            Assert.IsTrue(ShuffleResume.Holds(Level), "clearing another account's run cleared this one");

            ShuffleResume.Clear(Level);
            ShuffleResume.Clear(Level);
            Assert.IsFalse(ShuffleResume.Holds(Level));
        }

        [Test]
        public void AnUnreadableRunIsDroppedOnceAndReadsAsNone()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            ShuffleResume.Write(ShuffleSnapshot.Of(Level, run, Board(run), null, 0, 0));

            string slot = ShuffleResume.Key + ":" + Level.Value;
            Assert.IsNotEmpty(_store.Read(slot), "the run was not kept where the slot says");

            _store.Write(slot, "{\"v\":1,");
            Assert.IsNull(ShuffleResume.Read(Level));
            Assert.IsEmpty(_store.Read(slot), "an unreadable run was kept to be refused again");
        }

        [Test]
        public void ARunKeptUnderAnotherLevelsSlotIsNotThisLevels()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            var other = ShuffleSnapshot.Of(LevelId.Parse("s13_other"), run, Board(run), null, 0, 0);

            _store.Write(ShuffleResume.Key + ":" + Level.Value, other.Encode());
            Assert.IsNull(ShuffleResume.Read(Level));
        }

        [Test]
        public void ContinuesAreCarried()
        {
            var run = new ShuffleRun(3u);
            run.Deal();
            var back = RoundTrip(ShuffleSnapshot.Of(Level, run, Board(run), null, 3, 2));
            Assert.AreEqual(3, back.Continues);
            Assert.AreEqual(2, back.Watched);
        }

        static void SignIn(string uid)
            => typeof(CloudState).GetProperty(nameof(CloudState.UserId)).SetValue(null, uid);
    }
}
