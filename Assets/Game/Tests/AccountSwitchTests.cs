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

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            SaveService.LoadWith(new MemoryStore());

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
        /// </summary>
        [Test]
        public void ASyncNeverTouchesAnAccountTheSaveDoesNotBelongTo()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;

            var result = Wait(CloudSaveService.SyncAsync());

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(CloudFailure.AccountMismatch, result.Failure);
            Assert.AreEqual(0, _backend.Pulls, "a mismatched device must not even read");
            Assert.AreEqual(0, _backend.Pushes, "a mismatched device must never write");
            Assert.IsTrue(CloudSaveService.AccountMismatched);
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
        /// Read before write. Once the session is the other account the device is holding two
        /// people's things, and the local one is only let go of when the replacement has landed.
        /// </summary>
        [Test]
        public void AGroveThatCannotBeFetchedDoesNotCostTheOneOnTheDevice()
        {
            SignedInAs(Mine);
            _backend.Session = Mine;
            _backend.SignsInAs = Theirs;
            _backend.PullFailsFor = Theirs;

            var result = Wait(CloudSaveService.SwitchAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.Interrupted, result.Outcome);
            Assert.AreEqual("c01_first_light", GladeOnDevice(), "nothing local may be destroyed on a failed fetch");
            Assert.IsTrue(CloudSaveService.AccountMismatched, "and the device must know it is between two accounts");
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
        /// The one that keeps the whole thing recoverable: arriving at the account already held
        /// is a no-op, not a refresh. It is what a player taps after an interrupted switch, and
        /// it must be safe to tap when they were never interrupted at all.
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
        }

        // ============================================================== the recovery
        [Test]
        public void RecoveringClearsTheMismatchAndTouchesNothing()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;
            Wait(CloudSaveService.SyncAsync());
            Assert.IsTrue(CloudSaveService.AccountMismatched, "arrange: the device is between two accounts");

            _backend.SignsInAs = Mine;
            var result = Wait(CloudSaveService.ResumeAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.SameAccount, result.Outcome);
            Assert.IsFalse(CloudSaveService.AccountMismatched);
            Assert.AreEqual("c01_first_light", GladeOnDevice());
        }

        /// <summary>
        /// A device that cannot save its grove anywhere must not be talked into replacing it.
        /// Recovery signs you back in; becoming somebody else from here is the destructive
        /// prompt's job, and that one asks twice.
        /// </summary>
        [Test]
        public void RecoveringRefusesToBecomeAThirdAccount()
        {
            SignedInAs(Mine);
            _backend.Session = Theirs;
            Wait(CloudSaveService.SyncAsync());

            _backend.SignsInAs = "uid-somebody-else";
            var result = Wait(CloudSaveService.ResumeAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(SwitchOutcome.DifferentAccount, result.Outcome);
            Assert.IsTrue(result.Untouched);
            Assert.AreEqual("c01_first_light", GladeOnDevice(), "the unsaveable grove is still here");
        }

        // =================================================================== the wipe
        [Test]
        public void AWipeThatForgetsTheAccountLeavesTheFileOwnedByNobody()
        {
            SignedInAs(Mine);

            long before = CloudState.Revision;

            SaveService.Wipe(forgetAccount: true);

            Assert.AreEqual("", CloudState.UserId);
            Assert.AreEqual(0, CloudState.LastSyncedUnix, "nothing here has ever been synced");

            // Not zero: the wipe writes the fresh file, and writing is what a revision counts.
            // What matters is that it no longer carries the outgoing account's history, which
            // is the number a backend would use for optimistic concurrency.
            Assert.Less(CloudState.Revision, before,
                        "a revision belongs to one account's history and cannot travel");
        }

        [Test]
        public void AWipeThatKeepsTheAccountStillDoes()
        {
            SignedInAs(Mine);

            SaveService.Wipe();

            Assert.AreEqual(Mine, CloudState.UserId,
                            "the adopt path overwrites this a line later and relies on it surviving");
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

                Session = SignsInAs;
                return Task.FromResult((CloudResult.Success, new CloudIdentity(Session, true)));
            }

            public Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
                string userId, CancellationToken c = default)
            {
                if (userId == PullFailsFor)
                    return Task.FromResult((CloudResult.Failed(CloudFailure.Offline), CloudSnapshot.Missing));

                Pulls++;
                return Task.FromResult((CloudResult.Success,
                                        Remote.TryGetValue(userId, out var save)
                                            ? new CloudSnapshot(save, true)
                                            : CloudSnapshot.Missing));
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
        }
    }
}
