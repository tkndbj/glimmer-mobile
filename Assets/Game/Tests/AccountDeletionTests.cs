using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Deleting the account this device is holding.
    ///
    /// <para>
    /// <b>Like <c>AccountSwitchTests</c>, every test here is about an ordering</b>, and for a
    /// sharper reason. The arithmetic is nothing; what matters is that the server is asked
    /// <em>before</em> anything local is touched, and that a refusal leaves the device exactly
    /// as it was. Get that backwards and a player whose connection drops mid-tap is left with
    /// an empty phone and a full account — the one outcome here worse than the deletion simply
    /// failing, and the one the panel's copy promises cannot happen. Every failure sentence on
    /// <c>DeleteAccountOverlay</c> ends with "nothing has been deleted", so these are the tests
    /// that make that sentence true rather than hopeful.
    /// </para>
    /// <para>
    /// They run offline against an in-memory store, archive and backend — no Firebase, no
    /// filesystem, no Editor — which is what lets the most destructive path in the game be
    /// proved on every compile rather than whenever somebody remembers to open Unity.
    /// </para>
    /// </summary>
    public sealed class AccountDeletionTests
    {
        const string Mine = "uid-mine";
        const string Neighbour = "uid-neighbour";

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

        // ======================================================== what the rule decides
        [Test]
        public void AGuestIsAskedToConfirmAndNothingElse()
        {
            Assert.AreEqual(AccountDeletion.Verdict.ConfirmOnly,
                            AccountDeletion.Required(backendAvailable: true, linked: false));
        }

        [Test]
        public void ALinkedAccountHasToProveItself()
        {
            Assert.AreEqual(AccountDeletion.Verdict.Reauthenticate,
                            AccountDeletion.Required(backendAvailable: true, linked: true));
        }

        /// <summary>
        /// With no backend there is no account, so the control is not drawn at all — the
        /// complaint <c>GemChoice.Unavailable</c> exists to answer, applied here: a button that
        /// can never work is worse than no button.
        /// </summary>
        [Test]
        public void WithNoBackendThereIsNothingToDeleteAndNoControlIsOffered()
        {
            Assert.AreEqual(AccountDeletion.Verdict.Unavailable,
                            AccountDeletion.Required(backendAvailable: false, linked: true));
            Assert.IsFalse(AccountDeletion.Offered(backendAvailable: false));
            Assert.IsTrue(AccountDeletion.Offered(backendAvailable: true));
        }

        /// <summary>
        /// The panel says "nothing has been deleted" on every branch but one, so every branch
        /// but one has to mean it.
        /// </summary>
        [Test]
        public void EveryOutcomeButSuccessLeftTheAccountAlone()
        {
            Assert.IsFalse(AccountDeletion.Untouched(AccountDeletion.Outcome.Deleted));

            foreach (var outcome in new[]
            {
                AccountDeletion.Outcome.Cancelled, AccountDeletion.Outcome.Offline,
                AccountDeletion.Outcome.WrongAccount, AccountDeletion.Outcome.Busy,
                AccountDeletion.Outcome.Failed,
            })
                Assert.IsTrue(AccountDeletion.Untouched(outcome), outcome.ToString());
        }

        /// <summary>
        /// A closed provider sheet is the commonest way this ends and it is not a failure.
        /// Reporting it as one is the exact mistake the account flow was rewritten to stop
        /// making, one screen over.
        /// </summary>
        [Test]
        public void ClosingTheProviderSheetIsNotReportedAsAnError()
        {
            Assert.AreEqual(AccountDeletion.Outcome.Cancelled,
                            AccountDeletion.Read(CloudFailure.Cancelled));
            Assert.AreEqual(AccountDeletion.Outcome.Offline,
                            AccountDeletion.Read(CloudFailure.Offline));
            Assert.AreEqual(AccountDeletion.Outcome.WrongAccount,
                            AccountDeletion.Read(CloudFailure.AccountMismatch));
            Assert.AreEqual(AccountDeletion.Outcome.Failed,
                            AccountDeletion.Read(CloudFailure.Rejected));
        }

        // ============================================================== the local erasure
        [Test]
        public void ErasingTakesTheGroveAndTheAccountWithIt()
        {
            SignedInAs(Mine);
            Assume.That(PlayerProgress.ClearedCount, Is.EqualTo(1));

            Assert.IsTrue(SaveService.EraseAccount(Mine));

            Assert.AreEqual(0, PlayerProgress.ClearedCount, "the grove survived the erasure");
            Assert.AreEqual("", CloudState.UserId, "the file still names the deleted account");
        }

        /// <summary>
        /// The archive holds up to six groves and the others belong to accounts that are still
        /// playing. A deletion that cleared the folder would take a second player's local copy
        /// off a shared phone — a loss nothing would ever explain, because it is invisible
        /// until they try to switch back.
        /// </summary>
        [Test]
        public void ErasingOneAccountLeavesAnotherAccountsGroveOnTheDevice()
        {
            _archive.Stash(Neighbour, SaveWith(Neighbour, "c02_the_millers_knot"));

            SignedInAs(Mine);
            SaveService.EraseAccount(Mine);

            Assert.IsTrue(_archive.Has(Neighbour), "the neighbour's grove was deleted too");
            Assert.AreEqual(1, _archive.Count);
        }

        /// <summary>
        /// A switch archives what it leaves; this must not. Keeping a copy would leave the
        /// grove a player asked to be rid of sitting on the handset, and <c>SwitchTo</c> would
        /// cheerfully restore it if that uid ever came round again.
        /// </summary>
        [Test]
        public void ADeletedGroveIsNotFiledAwayTheWayASwitchedOneIs()
        {
            SignedInAs(Mine);
            _archive.Stash(Mine, SaveWith(Mine, "c01_first_light"));

            SaveService.EraseAccount(Mine);

            Assert.IsFalse(_archive.Has(Mine), "the deleted account was left in the archive");
        }

        /// <summary>
        /// Erasing is aimed at a named account and refuses anything else. This runs after a
        /// network round trip, so the session genuinely can have moved underneath it, and
        /// erasing whatever happens to be loaded is the one mistake here with no undo.
        /// </summary>
        [Test]
        public void ErasingRefusesToDestroyAGroveItWasNotAimedAt()
        {
            SignedInAs(Mine);

            Assert.IsFalse(SaveService.EraseAccount(Neighbour));
            Assert.AreEqual(1, PlayerProgress.ClearedCount, "somebody else's grove was erased");
            Assert.AreEqual(Mine, CloudState.UserId);
        }

        /// <summary>
        /// The handset's own preferences are not the account's. Resetting them because somebody
        /// deleted an account is a bug they would report as one.
        /// </summary>
        [Test]
        public void TheHandsetsOwnPreferencesSurviveADeletion()
        {
            SignedInAs(Mine);
            GameSettings.SetMusic(false);

            SaveService.EraseAccount(Mine);

            Assert.IsFalse(GameSettings.MusicOn, "the music setting was reset by a deletion");
        }

        // ========================================================== the whole thing, in order
        /// <summary>
        /// The ordering this feature exists to get right, driven end to end.
        /// </summary>
        [Test]
        public void TheServerIsAskedBeforeAnythingLocalIsTouched()
        {
            SignedInAs(Mine);

            var result = Wait(CloudSaveService.DeleteAccountAsync());

            Assert.IsTrue(result.Ok, "the deletion did not report success");
            Assert.AreEqual(1, _backend.Deletes);
            Assert.IsTrue(_backend.SaveStillLoadedAtDelete,
                          "the grove was erased before the server had agreed to delete anything");
        }

        [Test]
        public void ADeletionThatFailsLeavesTheGroveExactlyWhereItWas()
        {
            SignedInAs(Mine);
            _backend.DeleteFails = true;

            var result = Wait(CloudSaveService.DeleteAccountAsync());

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(AccountDeletion.Outcome.Offline, result.Outcome);
            Assert.AreEqual(1, PlayerProgress.ClearedCount, "a failed deletion erased the grove");
            Assert.AreEqual(Mine, CloudState.UserId, "a failed deletion moved the account");
        }

        /// <summary>
        /// The device does not end up holding nothing. There is no sign-in screen in this game,
        /// so an account-less device is a state nothing else knows how to draw — a player who
        /// deletes their account is starting the game again, not leaving it.
        /// </summary>
        [Test]
        public void TheDeviceIsLeftOnAFreshAccountWithAnEmptyGrove()
        {
            SignedInAs(Mine);

            Wait(CloudSaveService.DeleteAccountAsync());

            Assert.AreEqual(0, PlayerProgress.ClearedCount);
            Assert.AreEqual("uid-after-delete", CloudState.UserId,
                            "the device did not take the account the backend signed it in as");
        }

        /// <summary>
        /// Idempotent by construction. The server deletes the auth user last precisely so that
        /// a dropped reply can be retried, and the second call must not be a special case here
        /// either.
        /// </summary>
        [Test]
        public void DeletingTwiceIsNotWorseThanDeletingOnce()
        {
            SignedInAs(Mine);

            Assert.IsTrue(Wait(CloudSaveService.DeleteAccountAsync()).Ok);

            // The session has moved to the fresh account, so a second call deletes *that* —
            // which is exactly right, and must not throw, wedge, or resurrect the first grove.
            var again = Wait(CloudSaveService.DeleteAccountAsync());

            Assert.IsTrue(again.Ok);
            Assert.AreEqual(0, PlayerProgress.ClearedCount);
        }

        // ---------------------------------------------------------------- proving who asks
        [Test]
        public void ALinkedAccountIsProvedBeforeItIsDeleted()
        {
            SignedInAs(Mine);

            var result = Wait(CloudSaveService.DeleteAccountAsync(LinkCredential.ForApple()));

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(1, _backend.Reauths);
            Assert.IsTrue(_backend.ReauthBeforeDelete,
                          "the account was deleted before the provider had vouched for anybody");
        }

        /// <summary>
        /// Apple requires the Sign in with Apple grant to be revoked when the account it signed
        /// into is deleted. The code that does it is single-use and expires in minutes, so the
        /// only place it can be had is the re-authentication — which is why these two steps are
        /// one flow rather than two features.
        /// </summary>
        [Test]
        public void ApplesRevocationCodeIsCarriedFromTheProofIntoTheDeletion()
        {
            SignedInAs(Mine);
            _backend.AppleCode = "code-from-the-sheet";

            Wait(CloudSaveService.DeleteAccountAsync(LinkCredential.ForApple()));

            Assert.AreEqual("code-from-the-sheet", _backend.DeletedWithAppleCode);
        }

        /// <summary>Google has nothing to revoke, and must not be sent an Apple code.</summary>
        [Test]
        public void GoogleCarriesNoRevocationCode()
        {
            SignedInAs(Mine);
            _backend.AppleCode = "code-from-the-sheet";

            Wait(CloudSaveService.DeleteAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual("", _backend.DeletedWithAppleCode);
        }

        /// <summary>
        /// A player who closes the provider sheet has not asked for anything. Nothing may be
        /// deleted, and the panel must be able to say so truthfully.
        /// </summary>
        [Test]
        public void ClosingTheProviderSheetDeletesNothingAtAll()
        {
            SignedInAs(Mine);
            _backend.ReauthFails = CloudFailure.Cancelled;

            var result = Wait(CloudSaveService.DeleteAccountAsync(LinkCredential.ForApple()));

            Assert.AreEqual(AccountDeletion.Outcome.Cancelled, result.Outcome);
            Assert.AreEqual(0, _backend.Deletes, "a cancelled sheet still deleted the account");
            Assert.AreEqual(1, PlayerProgress.ClearedCount);
            Assert.AreEqual(Mine, CloudState.UserId);
        }

        /// <summary>
        /// Picking the wrong entry out of an account chooser is an ordinary mistake and it must
        /// cost nothing. It gets its own sentence rather than "something went wrong", because a
        /// deletion that silently did nothing after a provider sheet is indistinguishable from
        /// one that worked.
        /// </summary>
        [Test]
        public void ProvingWithSomebodyElsesAccountDeletesNothing()
        {
            SignedInAs(Mine);
            _backend.ReauthFails = CloudFailure.AccountMismatch;

            var result = Wait(CloudSaveService.DeleteAccountAsync(LinkCredential.ForGoogle()));

            Assert.AreEqual(AccountDeletion.Outcome.WrongAccount, result.Outcome);
            Assert.AreEqual(0, _backend.Deletes);
            Assert.AreEqual(1, PlayerProgress.ClearedCount);
        }

        /// <summary>
        /// The one orphan the server's own ordering cannot prevent, because the client causes
        /// it: a sync in flight is pull → join → push, so it would put the grove back into a
        /// document this call is in the middle of deleting — recreating <c>players/{uid}</c>
        /// seconds after it went, under a uid nothing can ever authenticate as again.
        ///
        /// <para>
        /// The latch <em>serialises</em> rather than refusing, which is deliberate and is why
        /// this waits rather than asserting a busy answer: a sync starts on every foreground,
        /// which is exactly when somebody opens the account screen, so failing fast would turn
        /// an ordinary background sync into "something went wrong". What has to be true is that
        /// the two never overlap — so the deletion is proved to still be waiting while the sync
        /// is held, and to go through once it is let go.
        /// </para>
        /// </summary>
        /// <remarks>
        /// A <c>UnityTest</c> rather than a plain one, and the reason is worth writing down
        /// because it looks like an affectation. Every other test here drives a fake that
        /// completes synchronously, so no <c>await</c> ever really suspends and a blocking
        /// <c>Task.Wait</c> is harmless. This one is the single case where the flow genuinely
        /// suspends — <c>ClaimAsync</c> polls the latch on a timer — and in the Editor that
        /// continuation is posted back to Unity's synchronisation context, which is the thread
        /// a <c>Wait</c> would be blocking. The test deadlocks itself and the product code is
        /// fine: nothing in the game ever blocks the main thread on this task. Yielding keeps
        /// the context pumping, so what is measured is the latch rather than the harness.
        /// </remarks>
        [UnityTest]
        public IEnumerator ADeletionWaitsForASyncRatherThanRacingIt()
        {
            SignedInAs(Mine);
            _backend.HoldPulls();

            var syncing = CloudSaveService.SyncAsync();
            var deleting = CloudSaveService.DeleteAccountAsync();

            // Long enough for the latch's poll to have come round several times.
            for (int i = 0; i < 30; i++) yield return null;

            Assert.IsFalse(deleting.IsCompleted, "the deletion ran while a sync held the latch");
            Assert.AreEqual(0, _backend.Deletes, "the deletion raced a live sync");

            _backend.ReleasePulls();

            for (int i = 0; i < 600 && !(syncing.IsCompleted && deleting.IsCompleted); i++)
                yield return null;

            Assert.IsTrue(syncing.IsCompleted, "the sync never finished");
            Assert.IsTrue(deleting.IsCompleted, "the deletion never finished");
            Assert.IsTrue(deleting.Result.Ok, "the deletion failed once the sync let go");
            Assert.AreEqual(1, _backend.Deletes);
        }

        // ================================================================== the scaffolding
        void SignedInAs(string userId, string glade = "c01_first_light")
        {
            _backend.Session = userId;
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

        static T Wait<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(10000), "the flow did not finish");
            return task.Result;
        }

        /// <summary>A save file that never reaches a disk. <c>AccountSwitchTests</c>' one.</summary>
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
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto)
            {
                _file = dto;
                return true;
            }

            public void Delete() => _file = null;
        }

        sealed class MemoryArchive : IAccountArchive
        {
            readonly Dictionary<string, SaveFileDto> _slots = new Dictionary<string, SaveFileDto>();

            public int Count => _slots.Count;

            public bool Has(string userId)
                => !string.IsNullOrEmpty(userId) && _slots.ContainsKey(userId);

            public SaveFileDto Read(string userId)
                => !string.IsNullOrEmpty(userId) && _slots.TryGetValue(userId, out var dto) ? dto : null;

            public bool Stash(string userId, SaveFileDto dto)
            {
                if (string.IsNullOrEmpty(userId) || dto == null) return false;

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

        /// <summary>
        /// A backend that records what it was asked and in what order.
        ///
        /// <para>
        /// Everything not on the deletion path answers <see cref="CloudFailure.Rejected"/>
        /// rather than a plausible success, deliberately: a double that quietly satisfies a
        /// call these tests did not intend to make is how a fixture comes to pass while proving
        /// something else. The two exceptions are the pull and the push, because a deletion
        /// ends by asking for a sync and a fixture that made that throw would be testing the
        /// scaffolding.
        /// </para>
        /// </summary>
        sealed class Backend : ICloudSaveBackend
        {
            public string Session;

            public int Deletes, Reauths;
            public string DeletedWithAppleCode;
            public bool SaveStillLoadedAtDelete, ReauthBeforeDelete;
            public bool DeleteFails;
            public CloudFailure ReauthFails = CloudFailure.None;
            public string AppleCode = "";

            TaskCompletionSource<bool> _held;

            public void HoldPulls() => _held = new TaskCompletionSource<bool>();

            public void ReleasePulls()
            {
                var held = _held;
                _held = null;
                held?.TrySetResult(true);
            }

            public bool IsAvailable => true;

            public CloudIdentity CurrentIdentity
                => string.IsNullOrEmpty(Session) ? CloudIdentity.None : new CloudIdentity(Session, true);

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

            public Task<CloudResult> DeleteAccountAsync(
                string userId, string appleAuthorizationCode = null, CancellationToken c = default)
            {
                Deletes++;

                // Read here rather than asserted afterwards, because "was the grove still on
                // the device when the server was asked" is a fact about a moment, and by the
                // time the test can look the moment has passed.
                SaveStillLoadedAtDelete = PlayerProgress.ClearedCount > 0;

                if (DeleteFails)
                    return Task.FromResult(CloudResult.Failed(CloudFailure.Offline, "no network"));

                DeletedWithAppleCode = appleAuthorizationCode ?? "";
                Session = "uid-after-delete";

                return Task.FromResult(CloudResult.Success);
            }

            // ------------------------------------------------ everything else, refused
            public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    CloudIdentity.None));

            public Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    CloudIdentity.None));

            public async Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
                string userId, CancellationToken c = default)
            {
                var held = _held;
                if (held != null) await held.Task;

                return (CloudResult.Success, CloudSnapshot.Missing);
            }

            public Task<CloudResult> PushAsync(
                string userId, SaveFileDto snapshot, SaveDelta delta, CancellationToken c = default)
                => Task.FromResult(CloudResult.Success);

            public Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
                string userId, CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
                string userId, IReadOnlyList<SpendEntryDto> spends, CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
                string userId, IReadOnlyList<GrantEntryDto> awards, CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
                RedeemPurchaseAsync(string userId, PurchaseReceipt receipt, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new List<CloudWalletState>(), CloudRedemption.Nothing));

            public Task<(CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)>
                ReadGroveStatsAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success,
                                    new Dictionary<Content.LevelId, Social.LevelStats>()));
        }
    }
}
