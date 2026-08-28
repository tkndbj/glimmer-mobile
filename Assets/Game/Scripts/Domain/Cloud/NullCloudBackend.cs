using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// The backend used when none is configured. Reports itself unavailable and does
    /// nothing else.
    ///
    /// The same arrangement <c>ResourcesAssetProvider</c> had before Addressables: the
    /// seam is live from the first build, the game is completely playable through it,
    /// and turning the real one on is one assignment in <c>Boot</c> rather than a
    /// refactor. Everything that would otherwise be written twice — the merge, the
    /// ledger arithmetic, the retry policy — is already exercised against this.
    /// </summary>
    public sealed class NullCloudBackend : ICloudSaveBackend, Social.IGroveBoardBackend
    {
        public bool IsAvailable => false;

        public CloudIdentity CurrentIdentity => CloudIdentity.None;

        public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(
            CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Unauthenticated, "no cloud backend configured"),
                                CloudIdentity.None));

        /// <summary>
        /// Successful, and nobody is signed in. That is the honest answer with no backend and
        /// it is the one <see cref="AccountGate"/> wants: a save that names an account keeps
        /// naming it, and nothing is ever created behind the player's back.
        /// </summary>
        public Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(
            CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Success, CloudIdentity.None));

        public Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Unauthenticated, "no cloud backend configured"),
                                CloudIdentity.None));

        public Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Unauthenticated, "no cloud backend configured"),
                                CloudIdentity.None));

        public Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
            string userId, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                CloudSnapshot.Missing));

        public Task<CloudResult> PushAsync(
            string userId, SaveFileDto snapshot, SaveDelta delta, CancellationToken cancellation = default)
            => Task.FromResult(CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"));

        public Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
            string userId, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                new List<CloudWalletState>()));

        public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
            string userId, IReadOnlyList<SpendEntryDto> spends, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                new List<CloudWalletState>()));

        public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
            string userId, IReadOnlyList<GrantEntryDto> awards, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                new List<CloudWalletState>()));

        public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
            RedeemPurchaseAsync(string userId, PurchaseReceipt receipt,
                                CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                new List<CloudWalletState>(), CloudRedemption.Nothing));

        /// <summary>
        /// Nothing to compare against, which is exactly right: with no backend there is no
        /// population, and a percentile over nobody is a number pretending to be a fact.
        /// </summary>
        public Task<(CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)>
            ReadGroveStatsAsync(CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                               new Dictionary<Content.LevelId, Social.LevelStats>()));

        /// <summary>
        /// With no backend there is no board, and that is the correct behaviour rather than a
        /// degraded one: the leaderboard tab reads every one of these as "not available here"
        /// and says so, exactly as the account panel does. Nothing in the game becomes
        /// unplayable, and nothing pretends to have ranked anybody.
        /// </summary>
        public Task<(CloudResult result, GroveCard card)> PublishGroveAsync(
            string userId, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                GroveCard.Empty));

        public Task<CloudResult> WithdrawGroveAsync(
            string userId, CancellationToken cancellation = default)
            => Task.FromResult(CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"));

        /// <summary>
        /// With nothing to adjudicate against there is no such thing as a name somebody else
        /// holds, so this is an offline failure rather than "free" — and the difference matters
        /// at the one call site. <c>KeeperNames</c> reads a failure as "nothing was decided" and
        /// the rename goes through untouched; answering "free" would be this backend asserting
        /// a fact about a population it cannot see.
        /// </summary>
        public Task<(CloudResult result, string holderId)> ReadNameHolderAsync(
            string nameKey, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                string.Empty));

        public Task<(CloudResult result, NameClaim claim)> ClaimNameAsync(
            string storedName, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                NameClaim.Unavailable));

        public Task<(CloudResult result, NameReportOutcome outcome)> ReportKeeperNameAsync(
            string keeperId, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                NameReportOutcome.Unavailable));

        public Task<(CloudResult result, GroveCard card)> ReadGroveCardAsync(
            string ownerId, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                GroveCard.Empty));

        public Task<(CloudResult result, LeaderboardBoard board)> ReadLeaderboardAsync(
            string boardId, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                LeaderboardBoard.None));

        public Task<(CloudResult result, GroveRankTable table,
                     Dictionary<string, int> population, long builtUnix)> ReadGroveRanksAsync(
            CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                GroveRankTable.None,
                                new Dictionary<string, int>(), 0L));

        /// <summary>
        /// Refused, and the panel never asks: <see cref="AccountDeletion.Offered"/> reads
        /// <see cref="IsAvailable"/> and draws no control at all without a backend. With none
        /// configured there is no account anywhere — the save has never left the handset — so
        /// a "delete my account" button would be offering to erase something that does not
        /// exist, which is worse than not offering it.
        /// </summary>
        /// <summary>Nobody to re-authenticate against, and nothing asks. See below.</summary>
        public Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Unauthenticated,
                                                   "no cloud backend configured"), string.Empty));

        public Task<CloudResult> DeleteAccountAsync(
            string userId, string appleAuthorizationCode = null,
            CancellationToken cancellation = default)
            => Task.FromResult(CloudResult.Failed(CloudFailure.Unauthenticated,
                                                  "no cloud backend configured"));
    }
}
