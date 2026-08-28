using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Switching this device to a different account, without leaking a grove or losing one.
    ///
    /// <para>
    /// Every test here is about an ordering. The arithmetic in this feature is trivial; what is
    /// not trivial is that the outgoing grove reaches the server <em>before</em> anything is
    /// handed over, and that nothing local is destroyed until the replacement is in hand. Both
    /// are invisible in a screenshot, both look fine in the Editor — which never authenticates
    /// — and both are unrecoverable when wrong, because the thing they lose is somebody's
    /// account.
    /// </para>
    /// <para>
    /// They run offline, against an in-memory <see cref="ISaveStore"/> and a scripted backend,
    /// and that was worth a small seam in production code to get. What these assert is which
    /// grove is on the device and which account owns it — in-memory state — while the disk
    /// round trip they used to drag in is <c>SaveStoreTests</c>' subject and is covered there
    /// against a real filesystem. Paying for a temporary directory here bought nothing except
    /// <c>JsonUtility</c>, which is what kept the most consequential tests in the project
    /// behind somebody remembering to open the Editor.
    /// </para>
    /// <para>
    /// Two of them still need it — the two that complete a whole switch, because that path
    /// clears the in-flight run marker and <c>RunGuard</c> is <c>PlayerPrefs</c> by design.
    /// They say so when skipped.
    /// </para>
    /// </summary>
    public sealed class AccountSwitchTests
    {
        const string Mine = "uid-mine";
        const string Theirs = "uid-theirs";

        Backend _backend;
        MemoryArchive _archive;

        [SetUp]
        public void Open()
        {
            SaveService.Unload();

            _archive = new MemoryArchive();
            SaveService.LoadWith(new MemoryStore(), _archive);

            _backend = new Backend();
            CloudSaveService.UseBackend(_backend);
        }

        [TearDown]
        public void Close()
        {
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
        }

        /// <summary>
        /// A save file that never reaches a disk. Deliberately keeps what it was given rather
        /// than round-tripping it through JSON: serialisation is <c>SaveStoreTests</c>' subject,
        /// and borrowing it here would only put <c>JsonUtility</c> back in the way.
        /// </summary>
        sealed class MemoryStore : ISaveStore
        {
            SaveFileDto _file;

            public SaveFileDto Load() => _file ?? new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),

                // Otherwise the load reaches LegacyPlayerPrefsImport, which is PlayerPrefs,
                // which is the Editor. There is no legacy build to import from in a test.
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto)
            {
                _file = dto;
                return true;
            }

            public void Delete() => _file = null;
        }

        /// <summary>
        /// The account archive, in memory.
        ///
        /// It exists here for the same reason <see cref="MemoryStore"/> does: what these tests
        /// are about is <em>which grove is on the device and which account owns it</em>, and
        /// the disk round trip that answers it is <c>AccountArchiveTests</c>' subject, against
        /// a real filesystem. Keeping the object it was handed rather than copying it is
        /// deliberate and safe — <c>SaveService.Snapshot</c> builds a fresh file every call, so
        /// nothing here is ever holding a reference the game is still writing through.
        /// </summary>
        sealed class MemoryArchive : IAccountArchive
        {
            readonly Dictionary<string, SaveFileDto> _slots = new Dictionary<string, SaveFileDto>();

            /// <summary>Set to fail every stash, the way a full disk would.</summary>
            public bool Readonly;

            public int Count => _slots.Count;

            public bool Has(string userId)
                => !string.IsNullOrEmpty(userId) && _slots.ContainsKey(userId);

            public SaveFileDto Read(string userId)
                => !string.IsNullOrEmpty(userId) && _slots.TryGetValue(userId, out var dto) ? dto : null;

            public bool Stash(string userId, SaveFileDto dto)
            {
                if (Readonly || string.IsNullOrEmpty(userId) || dto == null) return false;

                dto.cloud ??= new CloudStateDto();
                dto.cloud.userId = userId;
                _slots[userId] = dto;
                return true;
            }

            public void Forget(string userId)
            {
                if (!string.IsNullOrEmpty(userId)) _slots.Remove(userId);
            }
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// Puts this device on an account with one glade behind it, and a sync history.
        ///
        /// Arranged through <see cref="SaveService.Adopt"/> rather than by poking the pieces,
        /// because that is the one door a save actually arrives through — and it is what puts a
        /// revision and a sync mark on the file, which is half of what the wipe has to drop.
        /// </summary>
        void SignedInAs(string userId, string glade = "c01_first_light")
        {
            SaveService.Adopt(SaveWith(userId, glade));
            CloudState.MarkSynced(1_700_000_000);
            SaveService.Flush();
        }

        static SaveFileDto SaveWith(string userId, string glade) => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            settings = new SettingsDto(),
            wallet = WalletDto.Unwritten(),
            legacyImportDone = true,
            levels = new[] { new LevelRecordDto { levelId = glade, stars = 3, bestMoves = 11, clears = 1 } },
            cloud = new CloudStateDto { userId = userId, revision = 4 },
        };

        /// <summary>What is on this device right now, read back the way a sync would read it.</summary>
        static string GladeOnDevice()
        {
            var levels = SaveService.Snapshot().levels;
            return levels == null || levels.Length == 0 ? "" : levels[0].levelId;
        }

        static T Wait<T>(Task<T> task)
        {
            // The service is written for Unity's single-threaded context and the fake backend
            // completes synchronously, so nothing here actually blocks. The wait is only so an
            // unexpected hang fails the test instead of the run.
            Assert.IsTrue(task.Wait(10000), "the flow did not finish");
            return task.Result;
        }

        // ================================================================== the leak
        /// <summary>
        /// The one that matters most. A sync is pull, join, push and the join is monotonic, so
        /// pointed at the wrong account it merges two strangers' saves and overwrites one.
        ///
        /// <para>
        /// The device no longer <em>stops</em> when the two disagree — it finishes the account
        /// change locally and carries on, which is what removed the state a player could sit
        /// stranded in. So this asserts the leak directly rather than by proxy: whatever
        /// reaches the other account's document, it is not this grove.
        /// </para>
        /// </summary>
        [Test]
        public void ASyncNeverPushesAGroveToAnAccountItDoesNotBelongTo()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;

            Wait(CloudSaveService.SyncAsync());

            Assert.AreEqual("", GladeOnDevice(), "the other account's grove is what is on the device now");

            if (_backend.Remote.TryGetValue(Theirs, out var pushed))
                Assert.AreEqual(0, pushed.levels == null ? 0 : pushed.levels.Length,
                                "this grove must never reach the other account's document");
        }

        /// <summary>
        /// The other half of the same rule: with no session at all, a save that names an account
        /// must not have a fresh anonymous one minted for it. That account could never match, so
        /// the device would be refused for ever — see <c>AccountGateTests</c>.
        /// </summary>
        [Test]
        public void ASaveThatNamesAnAccountNeverHasANewOneMintedForIt()
        {
            SignedInAs(Mine);
            _backend.Session = null;

            var result = Wait(CloudSaveService.SyncAsync());

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(0, _backend.SignIns, "signing in here would create an account, not restore one");
            Assert.AreEqual(1, _backend.Resumes);
            Assert.AreEqual(Mine, CloudState.UserId, "the save still belongs to who it belonged to");
        }

        /// <summary>
        /// Linking attaches a provider to whichever account the session is, and the backend
        /// will create one to attach it to. On a save that already names an account that is
        /// the same leak wearing a friendly name — so the provider must never be reached at
        /// all, which is why this asserts on the backend rather than on the outcome.
        /// </summary>
        [Test]
        public void LinkingNeverMovesAGroveOntoAnAccountTheSessionInvented()
        {
            SignedInAs(Mine);
            _backend.Session = null;

            var result = Wait(CloudSaveService.LinkAsync(LinkCredential.ForApple()));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(0, _backend.Links, "the provider must not be attached to an invented account");
            Assert.AreEqual(0, _backend.SignIns);
            Assert.AreEqual(Mine, CloudState.UserId, "and the grove keeps the account it belongs to");
            Assert.AreEqual(0, _backend.Pushes);
        }

        [Test]
        public void LinkingAGuestKeepsTheAccountItAlreadyHad()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;

            var result = Wait(CloudSaveService.LinkAsync(LinkCredential.ForGoogle()));

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(1, _backend.Links);
            Assert.AreEqual(Mine, CloudState.UserId, "linking keeps the uid; that is what it is for");
        }

        // ================================================================== the loss
        /// <summary>
        /// The promise the button makes. If the outgoing grove cannot be put somewhere safe, the
        /// switch does not happen at all — no sign-in, no wipe, nothing.
        /// </summary>
        [Test]
        public void AGroveThatCannotBeSavedIsNotSwitchedAwayFrom()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.PushFails = true;

            var result = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.NotSecured, result.Outcome);
            Assert.IsTrue(result.Untouched);
            Assert.AreEqual(0, _backend.CredentialSignIns, "nothing may be handed over before the grove is safe");
            Assert.AreEqual(Mine, CloudState.UserId);
            Assert.AreEqual("c01_first_light", GladeOnDevice(), "the grove is still here");
        }

        /// <summary>
        /// <b>The report this whole change came from.</b> A player switching between two of
        /// their own Google accounts could not read the incoming grove — one document, in the
        /// frame after an OAuth browser handed control back — and the old flow treated that as
        /// a failed switch: the device stayed authenticated as one account holding the other's
        /// save, stopped syncing, and said so.
        ///
        /// <para>
        /// The fetch is not part of the switch any more. What this pins is every half of that:
        /// the account did change, nothing is stranded, the grove that left is still on the
        /// phone, and the screen is told it does not yet know what the incoming account has —
        /// which is the one thing it must not guess, because "starting a new grove" would be a
        /// lie told to somebody with three chapters behind them.
        /// </para>
        /// </summary>
        [Test]
        public void AGroveThatCannotBeFetchedIsStillASwitchAndStrandsNobody()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.SignsInAs = Theirs;
            _backend.PullFailsFor = Theirs;

            var result = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.Pending, result.Outcome);
            Assert.IsTrue(result.Ok, "a switch that cannot reach the server is still a switch");
            Assert.AreEqual(Theirs, CloudState.UserId);
            Assert.IsFalse(CloudSaveService.AccountMismatched,
                           "nothing may be left holding one account's save while signed in as another");
            Assert.IsTrue(_archive.Has(Mine), "and the grove that left is still on this phone");
        }

        // ================================================================ the switch
        [Test]
        public void SwitchingSavesTheOldGroveBeforeSigningIn()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.SignsInAs = Theirs;
            _backend.Remote[Theirs] = SaveWith(Theirs, "c01_lantern_ring");

            var result = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.Adopted, result.Outcome);
            Assert.IsTrue(_backend.SecuredBeforeCredentialSignIn,
                          "the outgoing grove must reach the server first, or the switch is not reversible");
            Assert.AreEqual(Theirs, CloudState.UserId);
            Assert.AreEqual("c01_lantern_ring", GladeOnDevice());
        }

        /// <summary>
        /// Pushed <em>and</em> kept. The push is what makes the grove reachable from another
        /// handset; the copy is what makes coming back to it here instant and offline, which
        /// is the difference between switching accounts being an ordinary thing to do and
        /// being a download each way.
        /// </summary>
        [Test]
        public void TheGroveThatLeavesIsKeptOnTheDeviceAsWellAsPushed()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.SignsInAs = Theirs;

            Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.IsTrue(_archive.Has(Mine));
            Assert.IsTrue(SaveService.HasLocalGroveFor(Mine), "and the screens can say so before anything moves");
            Assert.IsTrue(_backend.Remote.ContainsKey(Mine), "and it is on the server too");
        }

        /// <summary>
        /// Switching back needs the credential and nothing else — no document read, no server
        /// at all. Arranged by killing the incoming account's pull outright, which under the
        /// previous design was exactly the failure that produced the report.
        /// </summary>
        [Test]
        public void SwitchingBackToAGrovePlayedHereBeforeNeedsNoNetwork()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;

            _backend.SignsInAs = Theirs;
            Assert.IsTrue(Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle())).Ok);
            Assert.AreEqual(Theirs, CloudState.UserId, "arrange: this phone is on the other account");

            _backend.SignsInAs = Mine;
            _backend.PullFailsFor = Mine;

            var back = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.Adopted, back.Outcome);
            Assert.AreEqual(Mine, CloudState.UserId);
            Assert.AreEqual("c01_first_light", GladeOnDevice(),
                            "the grove came back off this phone, not off the server");
            Assert.AreEqual(1, back.ClearedGlades, "and the panel can say what it found");
        }

        /// <summary>
        /// A restored copy is not a second source of truth: the ordinary sync joins it with
        /// whatever the server has, which is <c>SaveMerge</c>'s job and is monotonic, so the
        /// glade cleared on the other handset is on this one a moment later.
        /// </summary>
        [Test]
        public void ARestoredGroveIsStillJoinedWithWhatTheServerHas()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;

            _backend.SignsInAs = Theirs;
            Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            // Meanwhile, on another handset.
            _backend.Remote[Mine] = SaveWith(Mine, "c01_lantern_ring");

            _backend.SignsInAs = Mine;
            Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            var levels = SaveService.Snapshot().levels;
            var ids = new List<string>();
            foreach (var record in levels) ids.Add(record.levelId);

            CollectionAssert.Contains(ids, "c01_first_light", "what this phone had");
            CollectionAssert.Contains(ids, "c01_lantern_ring", "and what the other one did");
        }

        [Test]
        public void SwitchingToAnAccountThatHasNeverPlayedStartsAFreshGrove()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.SignsInAs = Theirs;

            var result = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.Started, result.Outcome);
            Assert.AreEqual(Theirs, CloudState.UserId);
            Assert.AreEqual("", GladeOnDevice(), "the incoming account has played nothing");
        }

        /// <summary>
        /// Picking the wrong entry out of a provider's account chooser is an ordinary mistake.
        /// It has to be a no-op rather than a refresh, and it has to be reported, because a
        /// switch that appears to do nothing reads as broken.
        /// </summary>
        [Test]
        public void SigningInAsTheAccountAlreadyHeldChangesNothing()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.SignsInAs = Mine;

            var result = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.SameAccount, result.Outcome);
            Assert.AreEqual(Mine, CloudState.UserId);
            Assert.AreEqual("c01_first_light", GladeOnDevice());
            Assert.AreEqual(0, _archive.Count, "and nothing was filed away, because nothing left");
        }

        /// <summary>
        /// A sync already running is not a grove that could not be saved, and the difference
        /// is a sentence on screen. One starts on every foreground, which is exactly when a
        /// player opens the account panel — so reporting contention as a refusal would make
        /// the commonest moment to switch the likeliest one to be told it had failed.
        ///
        /// <para>
        /// The provider is made to refuse so the flow stops immediately after the step being
        /// tested; what is asserted is that it got that far at all.
        /// </para>
        /// </summary>
        [Test]
        public void ASwitchWaitsForARunningSyncRatherThanCallingItAFailure()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.CredentialSignInFails = true;

            // The only test here with a genuine suspension in it, and the only one that has to
            // say anything about threads. Unity's Editor installs a SynchronizationContext on
            // the main thread, so every `await` in the flow posts its continuation back to a
            // thread this test is about to block by waiting — which is a deadlock, not a
            // failure of the rule. Every other case in this file completes synchronously and
            // never notices. Dropped for the duration and restored after; nothing on the path
            // touches a GameObject, which is what makes that safe.
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            SwitchResult result;
            try
            {
                _backend.HoldPulls();
                var background = CloudSaveService.SyncAsync();
                var switching = CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle());
                _backend.ReleasePulls();

                Wait(background);
                result = Wait(switching);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }

            Assert.AreNotEqual(SwitchOutcome.NotSecured, result.Outcome);
            Assert.AreEqual(1, _backend.CredentialSignIns,
                            "the switch waited out the sync and reached the provider");
            Assert.AreEqual(Mine, CloudState.UserId, "and a refused provider changes nothing");
        }

        // ============================================================== the repair
        /// <summary>
        /// A process death between the sign-in and the swap used to be permanent: the device
        /// sat authenticated as one account holding another's save, refusing every read and
        /// write, with a panel telling its owner they were signed in as somebody else and a
        /// button that led to a destructive prompt.
        ///
        /// <para>
        /// It is repaired without anybody being told now, and forward is the only direction it
        /// can be repaired in — the session only ever moves because a player chose an account,
        /// and Firebase persists that choice before this code sees it, so a disagreement means
        /// the authentication got further than the file did.
        /// </para>
        /// </summary>
        [Test]
        public void ASessionThatMovedAheadOfTheSaveIsRepairedByTheNextSync()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;

            var result = Wait(CloudSaveService.SyncAsync());

            Assert.IsTrue(result.Ok, "the sync finishes rather than refusing for ever");
            Assert.AreEqual(Theirs, CloudState.UserId);
            Assert.IsFalse(CloudSaveService.AccountMismatched);
            Assert.IsTrue(_archive.Has(Mine), "and the grove it was holding was filed, not dropped");
        }

        /// <summary>
        /// The repair is a swap, not a merge. Half of one grove arriving in the other is the
        /// failure <c>AccountGate</c> exists for, and it must stay unreachable however the two
        /// sides came to disagree.
        /// </summary>
        [Test]
        public void ARepairNeverMixesTheTwoGrovesTogether()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;
            _backend.Remote[Theirs] = SaveWith(Theirs, "c01_lantern_ring");

            Wait(CloudSaveService.SyncAsync());

            Assert.AreEqual("c01_lantern_ring", GladeOnDevice());

            var levels = SaveService.Snapshot().levels;
            Assert.AreEqual(1, levels.Length, "exactly the incoming grove, and nothing of the outgoing one");
        }

        /// <summary>
        /// The one path that must still refuse, and it is the money one. A receipt is redeemed
        /// against whichever account is authorised, so repairing first would move a purchase
        /// made under one account onto another — a support case with a proof of purchase
        /// attached. Refusing costs nothing: both stores re-deliver an unfinished transaction
        /// for ever, and by the retry the next sync has repaired the device anyway.
        /// </summary>
        [Test]
        public void ARepairNeverMovesAPurchaseOntoAnotherAccount()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;

            var (result, _) = Wait(CloudSaveService.RedeemPurchaseAsync(
                new PurchaseReceipt
                {
                    Store = "apple",
                    ProductId = "gems_small",
                    TransactionId = "txn-1",
                    Payload = "payload",
                }));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(CloudFailure.AccountMismatch, result.Failure);
            Assert.AreEqual(Mine, CloudState.UserId, "and the device is left exactly as it was");
            Assert.AreEqual("c01_first_light", GladeOnDevice());
        }

        /// <summary>
        /// A device that cannot file the outgoing grove anywhere must not swap it away. This is
        /// the only reason <c>AccountMismatched</c> still exists, and it is why the repair is
        /// conditional rather than unconditional.
        /// </summary>
        [Test]
        public void ARepairThatCannotFileTheOutgoingGroveDoesNotHappen()
        {
            SignedInAs(Mine);
            _archive.Readonly = true;
            _backend.Session = Theirs;

            var result = Wait(CloudSaveService.SyncAsync());

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(CloudFailure.AccountMismatch, result.Failure);
            Assert.AreEqual(Mine, CloudState.UserId, "the grove stays where it is until it can be filed");
            Assert.AreEqual("c01_first_light", GladeOnDevice());
            Assert.IsTrue(CloudSaveService.AccountMismatched);
        }

        // ============================================================== the local swap
        // Everything here calls SaveService.SwitchTo directly, which is where the switch
        // actually happens now — no backend, no network, and nothing about it that could not
        // be run in an aeroplane. That is the point of the redesign and it is what makes
        // these the cheapest tests in the feature to keep honest.

        /// <summary>
        /// Out and back, on a device with no network at all. This is the shape of the thing a
        /// player asked for — two of their own accounts on one phone — and neither direction
        /// costs a document read.
        /// </summary>
        [Test]
        public void TheLocalSwapKeepsBothGrovesAndGoesBothWays()
        {
            SignedInAs(Mine);

            Assert.AreEqual(SaveService.SwapResult.Started, SaveService.SwitchTo(Theirs, true));
            Assert.AreEqual(Theirs, CloudState.UserId);
            Assert.AreEqual("", GladeOnDevice(), "the other account has played nothing here");

            Assert.AreEqual(SaveService.SwapResult.Restored, SaveService.SwitchTo(Mine, true));
            Assert.AreEqual(Mine, CloudState.UserId);
            Assert.AreEqual("c01_first_light", GladeOnDevice(), "and this one is exactly as it was left");

            // Counted off the records, so it holds with no content catalog loaded — which is
            // the state this fixture is in and, for a moment after launch, the state a real
            // device is in. Read through PlayerProgression instead it comes back zero, and the
            // panel greets somebody's twenty-six glades with "here is a new grove".
            Assert.AreEqual(1, PlayerProgress.ClearedCount);
            Assert.IsTrue(CloudSaveService.HoldsAGrove);
        }

        /// <summary>
        /// The revision and the sync mark describe a conversation with one account's document.
        /// Carried into another they are worse than meaningless — a revision invented against
        /// somebody else's history is precisely the input a backend using it for optimistic
        /// concurrency must not be given.
        /// </summary>
        [Test]
        public void AnAccountsSyncHistoryNeverTravelsToTheNextOne()
        {
            SignedInAs(Mine);
            long before = CloudState.Revision;

            SaveService.SwitchTo(Theirs, true);

            Assert.AreEqual(0, CloudState.LastSyncedUnix, "this account has never been synced from here");
            Assert.Less(CloudState.Revision, before,
                        "a revision belongs to one account's history and cannot travel");
        }

        /// <summary>
        /// Music, sound and language describe the handset and the person holding it, not the
        /// grove, so signing in to a second account must not silently turn the music back on.
        /// </summary>
        [Test]
        public void ThePhonesOwnPreferencesSurviveASwap()
        {
            SignedInAs(Mine);
            GameSettings.SetMusic(false);

            SaveService.SwitchTo(Theirs, true);

            Assert.IsFalse(GameSettings.MusicOn);
        }

        [Test]
        public void SwappingToTheAccountAlreadyHeldIsANoOp()
        {
            SignedInAs(Mine);

            Assert.AreEqual(SaveService.SwapResult.Same, SaveService.SwitchTo(Mine, true));
            Assert.AreEqual("c01_first_light", GladeOnDevice());
            Assert.AreEqual(0, _archive.Count, "and nothing is filed, because nothing left");
        }


        // ================================================================ the backend
        /// <summary>
        /// A backend that does what the test tells it to and records what it was asked.
        ///
        /// Deliberately records <em>order</em> as well as counts — the whole feature is an
        /// ordering, so a fake that only counted calls would pass every test in this file while
        /// handing a grove away before saving it.
        /// </summary>
        sealed class Backend : ICloudSaveBackend
        {
            public string Session;
            public string SignsInAs;
            public bool PushFails;

            /// <summary>Set to make the provider refuse, the way a closed consent screen does.</summary>
            public bool CredentialSignInFails;

            /// <summary>
            /// Lets a test hold a sync open, which is the ordinary state of the world rather
            /// than an exotic one: a sync starts on every foreground, and opening the account
            /// panel is what a player does straight after foregrounding.
            /// </summary>
            TaskCompletionSource<bool> _held;

            public void HoldPulls() => _held = new TaskCompletionSource<bool>();

            public void ReleasePulls()
            {
                var held = _held;
                _held = null;
                held?.TrySetResult(true);
            }

            /// <summary>
            /// Which account cannot be read. Per-account rather than a flag, because the
            /// securing sync pulls too — a blanket failure stops the switch at step one and
            /// the test would pass while proving the wrong thing.
            /// </summary>
            public string PullFailsFor;

            public readonly Dictionary<string, SaveFileDto> Remote = new Dictionary<string, SaveFileDto>();

            public int Pulls, Pushes, SignIns, Resumes, CredentialSignIns, Links;
            public bool SecuredBeforeCredentialSignIn;

            public bool IsAvailable => true;

            public CloudIdentity CurrentIdentity
                => string.IsNullOrEmpty(Session) ? CloudIdentity.None : new CloudIdentity(Session, true);

            public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(CancellationToken c = default)
            {
                SignIns++;
                Session = string.IsNullOrEmpty(Session) ? "uid-fresh-anonymous" : Session;
                return Task.FromResult((CloudResult.Success, new CloudIdentity(Session, false)));
            }

            public Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(CancellationToken c = default)
            {
                Resumes++;
                return Task.FromResult((CloudResult.Success, CurrentIdentity));
            }

            public Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
                LinkCredential cr, CancellationToken c = default)
            {
                Links++;

                // What the real one does when the session has gone: makes an account to
                // attach the provider to. The test above is that we never get here.
                if (string.IsNullOrEmpty(Session)) Session = "uid-invented";

                return Task.FromResult((CloudResult.Success, new CloudIdentity(Session, true)));
            }

            public Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
                LinkCredential cr, CancellationToken c = default)
            {
                if (CredentialSignIns == 0) SecuredBeforeCredentialSignIn = Pushes > 0;
                CredentialSignIns++;

                if (CredentialSignInFails)
                    return Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "cancelled by the player"),
                                            CloudIdentity.None));

                Session = SignsInAs;
                return Task.FromResult((CloudResult.Success, new CloudIdentity(Session, true)));
            }

            public async Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
                string userId, CancellationToken c = default)
            {
                var held = _held;
                if (held != null) await held.Task;

                if (userId == PullFailsFor)
                    return (CloudResult.Failed(CloudFailure.Offline), CloudSnapshot.Missing);

                Pulls++;
                return (CloudResult.Success,
                        Remote.TryGetValue(userId, out var save)
                            ? new CloudSnapshot(save, true)
                            : CloudSnapshot.Missing);
            }

            public Task<CloudResult> PushAsync(string userId, SaveFileDto snapshot, SaveDelta delta,
                                               CancellationToken c = default)
            {
                if (PushFails) return Task.FromResult(CloudResult.Failed(CloudFailure.Offline));

                Pushes++;
                Remote[userId] = snapshot;
                return Task.FromResult(CloudResult.Success);
            }

            static Task<(CloudResult, List<CloudWalletState>)> NoWallet()
                => Task.FromResult((CloudResult.Success, new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
                string u, CancellationToken c = default) => NoWallet();

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
                string u, IReadOnlyList<SpendEntryDto> s, CancellationToken c = default) => NoWallet();

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
                string u, IReadOnlyList<GrantEntryDto> a, CancellationToken c = default) => NoWallet();

            public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
                RedeemPurchaseAsync(string u, PurchaseReceipt r, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Offline),
                                    new List<CloudWalletState>(), CloudRedemption.Nothing));

            public Task<(CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)>
                ReadGroveStatsAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, new Dictionary<Content.LevelId, Social.LevelStats>()));

            // ------------------------------------------------------------- deletion
            public int Reauths;

            /// <summary>Set to make the provider refuse — a closed sheet, or the wrong account.</summary>
            public CloudFailure ReauthFails = CloudFailure.None;

            /// <summary>What a successful Apple re-authentication hands back.</summary>
            public string AppleCode = "";

            /// <summary>Whether the account was still present when the proof was asked for.</summary>
            public bool ReauthBeforeDelete;

            public Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(
                LinkCredential credential, CancellationToken c = default)
            {
                Reauths++;
                ReauthBeforeDelete = Deletes == 0;

                if (ReauthFails != CloudFailure.None)
                    return Task.FromResult((CloudResult.Failed(ReauthFails, "refused"), string.Empty));

                return Task.FromResult((CloudResult.Success,
                                        credential.ProviderId == LinkCredential.Apple
                                            ? AppleCode : string.Empty));
            }

            public int Deletes;

            /// <summary>Which account the last delete was authenticated as.</summary>
            public string DeletedSession;

            /// <summary>The Apple authorization code the last delete carried, or null.</summary>
            public string DeletedWithAppleCode;

            /// <summary>Whether the local grove was still present when the delete was asked for.</summary>
            public bool SaveStillLoadedAtDelete;

            public bool DeleteFails;

            public Task<CloudResult> DeleteAccountAsync(
                string userId, string appleAuthorizationCode = null, CancellationToken c = default)
            {
                Deletes++;
                SaveStillLoadedAtDelete = PlayerProgress.ClearedCount > 0;

                // The real backend refuses when the session has moved out from under the
                // caller, and the refusal is the safety property — so the fake refuses too.
                if (!string.Equals(Session, userId, StringComparison.Ordinal))
                    return Task.FromResult(CloudResult.Failed(CloudFailure.AccountMismatch,
                                                              "session is not the account"));

                if (DeleteFails)
                    return Task.FromResult(CloudResult.Failed(CloudFailure.Offline, "no network"));

                DeletedSession = userId;
                DeletedWithAppleCode = appleAuthorizationCode;

                Remote.Remove(userId);

                // What the real one leaves behind: a brand new anonymous account.
                Session = "uid-after-delete";

                return Task.FromResult(CloudResult.Success);
            }
        }
    }
}
