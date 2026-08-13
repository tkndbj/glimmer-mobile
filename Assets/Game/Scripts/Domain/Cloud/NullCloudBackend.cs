using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Persistence;

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
    public sealed class NullCloudBackend : ICloudSaveBackend
    {
        public bool IsAvailable => false;

        public CloudIdentity CurrentIdentity => CloudIdentity.None;

        public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(
            CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Unauthenticated, "no cloud backend configured"),
                                CloudIdentity.None));

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

        public Task<(CloudResult result, List<CloudWalletState> wallets)> RedeemPurchaseAsync(
            string userId, PurchaseReceipt receipt, CancellationToken cancellation = default)
            => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "no cloud backend configured"),
                                new List<CloudWalletState>()));
    }
}
