using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Referral
{
    /// <summary>What the server said to a code being typed. The wire spellings are in <c>referral.ts</c>.</summary>
    public enum ReferralRedeemOutcome
    {
        /// <summary>No answer: offline, signed out, or a reply this build could not read.</summary>
        Unavailable,

        /// <summary>Bound. The invitee now belongs to the code's owner.</summary>
        Bound,

        /// <summary>Not a code this game could have minted. Decided on the device, before any call.</summary>
        BadCode,

        /// <summary>A well-formed code nobody holds.</summary>
        UnknownCode,

        /// <summary>The account's own code.</summary>
        OwnCode,

        /// <summary>This account already typed a code. One referrer for life.</summary>
        AlreadyReferred,

        /// <summary>The code's owner has reached their cap of invitees.</summary>
        Full,

        /// <summary>This account has already cleared the milestone, so it is not a new player.</summary>
        TooLate,

        /// <summary>The server holds no save for this account yet; sync first.</summary>
        NoSave,
    }

    /// <summary>Which chest a claim asks for.</summary>
    public enum ReferralClaimKind
    {
        /// <summary>One of the referrer's chests for one finished invitee, named by which invitee.</summary>
        Rung,

        /// <summary>The invitee's own chest for clearing the milestone.</summary>
        Invitee,
    }

    /// <summary>What the server said to a claim.</summary>
    public enum ReferralClaimOutcome
    {
        /// <summary>No answer: offline, signed out, or a reply this build could not read.</summary>
        Unavailable,

        /// <summary>Paid by this call. The drops are what it paid.</summary>
        Paid,

        /// <summary>Paid by an earlier call, on this device or another. The drops are what that call paid.</summary>
        AlreadyPaid,

        /// <summary>Not reached yet: that many friends have not finished, or the milestone is not cleared on the server.</summary>
        NotYet,

        /// <summary>A chest the server's table does not hold. A content pack ahead of the seeder, or a forged index.</summary>
        Unknown,
    }

    /// <summary>
    /// One reply from the referral callables. Every call returns the state after it, so the
    /// device's cache is replaced whole rather than patched.
    /// </summary>
    public sealed class ReferralReply
    {
        public ReferralState State = ReferralState.Empty;

        public ReferralRedeemOutcome Redeem = ReferralRedeemOutcome.Unavailable;

        public ReferralClaimOutcome Claim = ReferralClaimOutcome.Unavailable;

        /// <summary>What the chest held, rolled by the server. Empty unless a claim was paid or already paid.</summary>
        public List<ChestDrop> Drops = new List<ChestDrop>();

        /// <summary>The balances after a claim, for the ledgers to adopt. Empty on a read or a redeem.</summary>
        public List<CloudWalletState> Wallets = new List<CloudWalletState>();
    }

    /// <summary>
    /// The three referral calls, beside <see cref="ICloudSaveBackend"/> and
    /// <see cref="Social.IGroveBoardBackend"/> rather than inside either: a referral is neither
    /// the save nor the boards, and the shape a fake needs in a test is three methods rather
    /// than thirty.
    /// </summary>
    public interface IReferralBackend
    {
        /// <summary>
        /// Reads the account's referral state, minting a code on first ask — and settling the
        /// invitee's milestone on the server if the save it holds shows the chapter cleared.
        /// </summary>
        Task<(CloudResult result, ReferralReply reply)> ReadReferralAsync(
            CancellationToken cancellation = default);

        /// <summary>Binds this account to the owner of a folded code.</summary>
        Task<(CloudResult result, ReferralReply reply)> RedeemReferralAsync(
            string code, CancellationToken cancellation = default);

        /// <summary>
        /// Asks the server to pay one chest. The server rolls it, records the grant and answers
        /// with the drops and the balances; the client's only job is to bank what is not
        /// currency. Idempotent: a second ask answers <see cref="ReferralClaimOutcome.AlreadyPaid"/>
        /// with the same drops.
        /// </summary>
        Task<(CloudResult result, ReferralReply reply)> ClaimReferralAsync(
            ReferralClaimKind kind, int goal, int index, CancellationToken cancellation = default);

        /// <summary>
        /// Watches for "this account's referral state moved", and calls
        /// <paramref name="onChanged"/> when it does. Answers null when this backend cannot
        /// watch — no Firestore, signed out — which the caller must cope with rather than
        /// require.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>It carries no state, and that is the design rather than a shortcut.</b> The
        /// document being watched holds a counter and nothing else, because the document that
        /// holds the real answer — <c>referrals/{uid}</c> — is deliberately unreadable by any
        /// client: it names the referrer, and a code owner's names every invitee, which is more
        /// than this feature ever promised anybody (see <c>firestore.rules</c>). So a listener
        /// learns only *that* something moved and the reply still comes from
        /// <see cref="ReadReferralAsync"/>, which is already the one authority on what the
        /// state is. No server rule is copied onto the client to disagree with later.
        /// </para>
        /// <para>
        /// <b><paramref name="onChanged"/> may arrive on any thread.</b> An implementation is
        /// not required to marshal, and the caller must not assume: see
        /// <see cref="ReferralLedger.Pump"/>, which is why the callback's whole job is to set a
        /// flag. Disposing stops the watch, and disposing twice is safe.
        /// </para>
        /// </remarks>
        IDisposable WatchReferral(Action onChanged);
    }
}
