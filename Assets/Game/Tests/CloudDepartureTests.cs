using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What this device owes the server, and getting it there as the player leaves.
    ///
    /// <para>
    /// Written for a streak shield bought on one phone and missing on the other (2026-09-28).
    /// The cause was not the shield: the sync started as the app went to the background resumed
    /// on Unity's main thread after every <c>await</c>, Unity stops pumping that thread when the
    /// app is paused, and so the push waited for the player to come back. The first two cases
    /// here are that fault and its repair, side by side, under a synchronisation context that is
    /// never pumped - which is what a paused Unity is. Everything else holds the rules that make
    /// the departure safe to send: never to another account, never beside an account change,
    /// never when nothing is owed, and never racing the sync that follows it.
    /// </para>
    /// <para>
    /// Offline, against the switch fixture's in-memory store and scripted backend.
    /// </para>
    /// </summary>
    public sealed class CloudDepartureTests
    {
        const string Mine = "uid-mine";
        const string Theirs = "uid-theirs";

        AccountSwitchTests.Backend _backend;

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore(), new AccountSwitchTests.MemoryArchive());

            _backend = new AccountSwitchTests.Backend { Session = Mine };
            CloudSaveService.UseBackend(_backend);
            CloudSaveService.ResetDepartureForTests();
            CloudSaveService.ForgetSyncRequestForTests();
            SyncTriggers.Attach();

            // On this device, as this account, with one cleared glade - through Adopt, the one
            // door a save really arrives through.
            SaveService.Adopt(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                legacyImportDone = true,
                levels = new[] { new LevelRecordDto { levelId = "c01_first_light", stars = 3, bestMoves = 11, clears = 1 } },
                cloud = new CloudStateDto { userId = Mine, revision = 4 },
            });
        }

        [TearDown]
        public void Close()
        {
            CloudSaveService.ResetDepartureForTests();
            CloudSaveService.ForgetSyncRequestForTests();
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
            EndlessCoins.UseStore(null);
        }

        // ================================================================ the fault
        /// <summary>
        /// Unity's main thread while the app is in the background: work is posted to it and
        /// nothing runs until somebody pumps it, which in the game is the player coming back.
        /// </summary>
        sealed class PausedMainThread : SynchronizationContext
        {
            readonly Queue<(SendOrPostCallback, object)> _posted = new Queue<(SendOrPostCallback, object)>();

            public override void Post(SendOrPostCallback work, object state)
            {
                lock (_posted) _posted.Enqueue((work, state));
            }

            public override void Send(SendOrPostCallback work, object state)
                => throw new InvalidOperationException("nothing may block on a paused main thread");

            /// <summary>The player comes back: run what was posted, until nothing is left.</summary>
            public void Resume(Task until)
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!until.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        (SendOrPostCallback, object) next;
                        lock (_posted)
                        {
                            if (_posted.Count == 0) { Thread.Sleep(1); continue; }
                            next = _posted.Dequeue();
                        }
                        next.Item1(next.Item2);
                    }
                }
                finally { SetSynchronizationContext(previous); }
            }
        }

        /// <summary>Runs <paramref name="start"/> as if from inside a paused Unity's main thread.</summary>
        static T WhilePaused<T>(PausedMainThread paused, Func<T> start)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(paused);
            try { return start(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        /// <summary>
        /// The fault, kept as a differential so the repair below is measured against it. A sync
        /// started as the app goes out sends its read and then waits for the main thread - which,
        /// paused, is never. Only the player returning lets it push.
        /// </summary>
        [Test]
        public void ASyncStartedAsTheAppLeavesCannotPushUntilThePlayerComesBack()
        {
            _backend.Asynchronous = true;
            GameSettings.SetMusic(false);                  // something owed

            var paused = new PausedMainThread();
            var sync = WhilePaused(paused, () => CloudSaveService.SyncAsync());

            Assert.IsFalse(sync.Wait(500), "the sync finished with the main thread paused");
            Assert.AreEqual(0, _backend.Pushes, "nothing reached the server while the player was away");

            paused.Resume(sync);                           // the player comes back
            Assert.IsTrue(sync.IsCompleted, "and it finishes only once they have");
            Assert.AreEqual(1, _backend.Pushes);
        }

        /// <summary>
        /// The repair. The same change, the same paused thread - and the departure reaches the
        /// server without the main thread ever running again.
        /// </summary>
        [Test]
        public void ADepartureReachesTheServerWithTheMainThreadPaused()
        {
            _backend.Asynchronous = true;
            GameSettings.SetMusic(false);

            var paused = new PausedMainThread();
            var departure = WhilePaused(paused, () => CloudSaveService.Depart());

            Assert.IsTrue(departure.Wait(5000), "the departure waited on a paused main thread");
            Assert.AreEqual(1, _backend.Pushes);
            Assert.AreEqual(StoredFlag.Off, _backend.Remote[Mine].settings.music.state,
                            "and what it pushed is the change the player made");
            Assert.IsFalse(CloudSaveService.IsDeparting, "and it let go of the carrier");
        }

        // ============================================================== the wallet
        /// <summary>
        /// A purchase is two things - what it bought, in the save, and the debit, which reaches
        /// the server only as a submission. The shield arrived on the other device showing the
        /// old balance until both went; so both go.
        /// </summary>
        [Test]
        public void ADepartureOffersTheWalletsWaitingDebitsAndClaims()
        {
            Wallet.Ledger(Currency.Gems).GrantLocally(500);
            Assert.IsTrue(PlayerProgression.TrySpend(Currency.Gems, 120, "test:shield", "shield:20000"));
            Assert.IsTrue(PlayerProgression.Award(Currency.Credits, 40, "test:claim:credits", "test", 1_700_000_000));

            Assert.IsTrue(CloudSaveService.Depart().Wait(5000));

            CollectionAssert.Contains(_backend.SpendIdsOffered, "shield:20000");
            CollectionAssert.Contains(_backend.AwardIdsOffered, "test:claim:credits");
        }

        // ================================================================ the rules
        [Test]
        public void NothingOwedSendsNothing()
        {
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));
            int pulls = _backend.Pulls, pushes = _backend.Pushes;

            var departure = CloudSaveService.Depart();

            Assert.IsTrue(departure.IsCompleted, "decided on the spot, with no network");
            Assert.AreEqual(pulls, _backend.Pulls, "not even a read - a background used to cost one");
            Assert.AreEqual(pushes, _backend.Pushes);
        }

        /// <summary>Invariant 17, at the departure: a push is only ever addressed to the save's own account.</summary>
        [Test]
        public void ADepartureIsNeverAddressedToAnotherAccount()
        {
            GameSettings.SetMusic(false);
            _backend.Session = Theirs;

            Assert.IsTrue(CloudSaveService.Depart().Wait(5000));

            Assert.AreEqual(0, _backend.Pulls);
            Assert.AreEqual(0, _backend.Pushes);
            Assert.IsFalse(_backend.Remote.ContainsKey(Theirs));
        }

        [Test]
        public void ASaveThatNamesNoAccountHasNothingToDepartTo()
        {
            SaveService.Adopt(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                legacyImportDone = true,
                cloud = new CloudStateDto(),
            });
            GameSettings.SetMusic(false);

            Assert.IsTrue(CloudSaveService.Depart().Wait(5000));
            Assert.AreEqual(0, _backend.Pulls);
        }

        /// <summary>
        /// A sync asked for while a departure is still out is turned away and owed, never run
        /// beside it and never dropped - the foreground's sync used to be dropped outright on a
        /// busy answer.
        /// </summary>
        [Test]
        public void ASyncDuringADepartureIsTurnedAwayAndAskedForAgain()
        {
            GameSettings.SetMusic(false);
            _backend.HoldPulls();

            var departure = CloudSaveService.Depart();
            Assert.IsTrue(SpinUntil(() => CloudSaveService.IsDeparting));

            CloudSaveService.ForgetSyncRequestForTests();
            var sync = CloudSaveService.SyncAsync();

            Assert.IsTrue(sync.Wait(5000));
            Assert.AreEqual(CloudFailure.Busy, sync.Result.Failure);
            Assert.IsTrue(CloudSaveService.IsSyncPending, "the work it turned away is still owed");

            _backend.ReleasePulls();
            Assert.IsTrue(departure.Wait(5000));
            Assert.AreEqual(1, _backend.Pushes, "one push, the departure's");
        }

        /// <summary>
        /// Two pauses a moment apart are one carrier and the later file, never two pushes
        /// racing - and the later change is not lost to the first being out.
        /// </summary>
        [Test]
        public void ASecondDepartureWhileTheFirstIsOutCarriesTheLaterFile()
        {
            GameSettings.SetMusic(false);
            _backend.HoldPulls();
            var first = CloudSaveService.Depart();
            Assert.IsTrue(SpinUntil(() => CloudSaveService.IsDeparting));

            GameSettings.SetSfx(false);
            var second = CloudSaveService.Depart();

            _backend.ReleasePulls();
            Assert.IsTrue(first.Wait(5000) && second.Wait(5000));
            Assert.IsTrue(SpinUntil(() => !CloudSaveService.IsDeparting));

            Assert.AreEqual(StoredFlag.Off, _backend.Remote[Mine].settings.sfx.state,
                            "the second pause's change reached the server");
        }

        [Test]
        public void AFailedDepartureLosesNothingAndLetsGo()
        {
            GameSettings.SetMusic(false);
            _backend.PushFails = true;

            Assert.IsTrue(CloudSaveService.Depart().Wait(5000));

            Assert.IsFalse(CloudSaveService.IsDeparting);
            Assert.IsFalse(GameSettings.MusicOn, "the device still holds the change");

            _backend.PushFails = false;
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));
            Assert.AreEqual(StoredFlag.Off, _backend.Remote[Mine].settings.music.state,
                            "and the next sync carries it");
        }

        // ================================================================ what is owed
        [Test]
        public void AfterASyncAnUnchangedSaveOwesNothing()
        {
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));

            Assert.IsFalse(CloudSaveService.Owes(SaveService.Snapshot()));
        }

        [Test]
        public void AChangeNobodyListedIsStillOwed()
        {
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));

            GameSettings.SetMusic(false);

            Assert.IsTrue(CloudSaveService.Owes(SaveService.Snapshot()));
        }

        /// <summary>
        /// Booked on the ledger directly rather than through <c>PlayerProgression</c>, and that
        /// is the point: a spend through the game also moves a task counter, which is in the
        /// save, so the save alone would read as owed and this clause would be untested. The
        /// ledgers never travel in the save document - only as submissions - so this is the
        /// one change the file comparison cannot see.
        /// </summary>
        [Test]
        public void ANewWalletEntryIsOwedEvenWithTheSaveUnchanged()
        {
            var gems = Wallet.Ledger(Currency.Gems);
            gems.GrantLocally(500);
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));
            var before = SaveService.Snapshot();

            Assert.IsTrue(gems.TrySpend(20, 0, "test:thing", "test:thing:1", out _));

            Assert.IsTrue(SaveDelta.Between(before, SaveService.Snapshot()).IsEmpty,
                          "the fixture moved the save, so it is not asking the wallet clause");
            Assert.IsTrue(CloudSaveService.Owes(SaveService.Snapshot()));
        }

        /// <summary>
        /// An entry the server was offered and left unconfirmed (13a) is resubmitted by every
        /// sync anyway. Counting it as owed again would turn every write into a sync for as long
        /// as it waits.
        /// </summary>
        [Test]
        public void AnEntryAlreadyOfferedIsNotOwedAgain()
        {
            Wallet.Ledger(Currency.Gems).GrantLocally(500);
            Assert.IsTrue(PlayerProgression.TrySpend(Currency.Gems, 20, "test:thing", "test:thing:1"));

            // The fake confirms nothing, so the debit is still waiting after this.
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));

            Assert.IsFalse(CloudSaveService.Owes(SaveService.Snapshot()));
        }

        [Test]
        public void ASaveForAnotherAccountIsOwedWhateverItHolds()
        {
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));

            var file = SaveService.Snapshot();
            file.cloud.userId = Theirs;

            Assert.IsTrue(CloudSaveService.Owes(file));
        }

        // ============================================================ the triggers
        [Test]
        public void ALocalChangeAfterASyncAsksForOne()
        {
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));
            CloudSaveService.ForgetSyncRequestForTests();

            GameSettings.SetMusic(false);

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void AWriteThatChangedNothingAsksForNothing()
        {
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));
            CloudSaveService.ForgetSyncRequestForTests();

            SaveService.Save();

            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        /// <summary>
        /// The loop the whole trigger list is written to avoid: a sync adopts a merge, every
        /// ledger raises, and a trigger that heard it would schedule the next sync for ever. A
        /// merge that brought news is the sharpest case, so it is the one asked.
        /// </summary>
        [Test]
        public void ASyncThatLearnedSomethingDoesNotScheduleAnother()
        {
            var theirs = SaveService.Snapshot();
            theirs.levels = new[]
            {
                new LevelRecordDto { levelId = "c01_first_light", stars = 3, bestMoves = 11, clears = 1 },
                new LevelRecordDto { levelId = "c01_second_light", stars = 2, bestMoves = 20, clears = 1 },
            };
            _backend.Remote[Mine] = theirs;
            CloudSaveService.ForgetSyncRequestForTests();

            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));

            Assert.IsFalse(CloudSaveService.IsSyncPending, "the merge it adopted asked for another sync");
        }

        [Test]
        public void AClaimAsksForASync()
        {
            CloudSaveService.ForgetSyncRequestForTests();

            Assert.IsTrue(PlayerProgression.Award(Currency.Credits, 40, "test:claim:credits", "test", 1_700_000_000));

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void AClaimAlreadyHeldAsksForNothing()
        {
            Assert.IsTrue(PlayerProgression.Award(Currency.Credits, 40, "test:claim:credits", "test", 1_700_000_000));
            Assert.IsTrue(CloudSaveService.SyncAsync().Wait(5000));
            CloudSaveService.ForgetSyncRequestForTests();

            Assert.IsFalse(PlayerProgression.Award(Currency.Credits, 40, "test:claim:credits", "test", 1_700_000_000));

            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        static bool SpinUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) return false;
                Thread.Sleep(1);
            }
            return true;
        }
    }
}
