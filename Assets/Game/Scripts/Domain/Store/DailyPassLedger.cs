using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Store
{
    /// <summary>What a pass claim came to. The wire spellings are <c>claimDailyPass</c>'s.</summary>
    public enum DailyPassClaimOutcome
    {
        /// <summary>No answer: offline, signed out, a claim already out, or a reply this build could not read.</summary>
        Unavailable = 0,

        /// <summary>Paid by this call. <see cref="DailyPassClaim.Credits"/> and <see cref="DailyPassClaim.Gems"/> are what it paid.</summary>
        Paid,

        /// <summary>Today's piece was already paid - on this device or another. The button comes back tomorrow.</summary>
        NotYet,

        /// <summary>Nothing is owed: the pass is paid out (or was refunded). The shop shows the price again.</summary>
        None,
    }

    /// <summary>One claim's answer.</summary>
    public readonly struct DailyPassClaim
    {
        public readonly DailyPassClaimOutcome Outcome;
        public readonly long Credits;
        public readonly long Gems;

        public DailyPassClaim(DailyPassClaimOutcome outcome, long credits = 0L, long gems = 0L)
        {
            Outcome = outcome;
            Credits = credits < 0 ? 0 : credits;
            Gems = gems < 0 ? 0 : gems;
        }

        public bool Paid => Outcome == DailyPassClaimOutcome.Paid;
    }

    /// <summary>The server's word on one pass: pieces still owed, and the UTC day one was last paid.</summary>
    public readonly struct DailyPassStanding
    {
        public readonly int Remaining;
        public readonly int PaidDay;

        public DailyPassStanding(int remaining, int paidDay)
        {
            Remaining = remaining < 0 ? 0 : remaining;
            PaidDay = paidDay < 0 ? 0 : paidDay;
        }

        public bool Equals(DailyPassStanding other) => Remaining == other.Remaining && PaidDay == other.PaidDay;
    }

    /// <summary>The one pass call, beside <see cref="ICloudSaveBackend"/> for <c>IReferralBackend</c>'s reason.</summary>
    public interface IDailyPassBackend
    {
        /// <summary>
        /// Asks the server to pay today's piece of <paramref name="productId"/>. Answers the outcome,
        /// what it paid, and the balances after it - every time, so a device that thought it was
        /// owed something learns the truth in the same round trip.
        /// </summary>
        Task<(CloudResult result, DailyPassClaim claim, List<CloudWalletState> wallets)> ClaimDailyPassAsync(
            string productId, CancellationToken cancellation = default);
    }

    /// <summary>
    /// The seven-day passes on this device: what the server says each still owes, and the door
    /// that asks it to pay today's piece (<c>functions/src/daily-pass.ts</c>).
    ///
    /// <para>
    /// <b>A mirror, never a record.</b> The whole entitlement lives on the wallet document no
    /// client can write; this holds the last thing a wallet reply said about it and nothing else -
    /// nothing in the save (so nothing to merge, 11e) and no amount (10, 15). The shop reads it to
    /// decide whether a pass card shows its price or CLAIM, and a claim is the server paying, then
    /// this device adopting the balances it answered with. So no state here can strand a reward:
    /// the worst a stale mirror can do is draw CLAIM on a day the server answers "tomorrow", and
    /// that answer corrects it.
    /// </para>
    /// <para>
    /// <b>Nothing is offered until a reply has said what this account holds</b>
    /// (<see cref="Known"/>), the deals ledger's rule: a pass card drawn before that could sell a
    /// second pass to somebody already holding one (harmless - the server pays both in full - but
    /// not what they meant), and a deployment that predates passes answers nothing, so a client
    /// shipped ahead of the functions offers nothing the server cannot pay (12a's hazard, removed).
    /// </para>
    /// </summary>
    public static class DailyPassLedger
    {
        /// <summary>Raised when what is owed changes, or a claim starts or ends. Main thread.</summary>
        public static event Action Changed;

        static readonly Dictionary<string, DailyPassStanding> _held =
            new Dictionary<string, DailyPassStanding>(StringComparer.Ordinal);

        static readonly HashSet<string> _claiming = new HashSet<string>(StringComparer.Ordinal);

        static bool _known, _hooked;

        static DailyPassLedger() => Hook();

        /// <summary>Installs the account listener. Idempotent.</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CloudSaveService.IdentityChanged += OnIdentityChanged;
        }

        static IDailyPassBackend Backend => CloudSaveService.Backend as IDailyPassBackend;

        /// <summary>Whether a claim could be asked for at all: a cloud backend that speaks passes.</summary>
        public static bool IsAvailable => CloudSaveService.IsAvailable && Backend != null;

        /// <summary>Whether a wallet reply has said what this account holds, since the last account change.</summary>
        public static bool Known => _known;

        /// <summary>The UTC day the server counts a claim against: <c>todayKey</c>, whole days since the epoch.</summary>
        public static int Today(long nowUnix) => nowUnix <= 0 ? 0 : (int)(nowUnix / 86400L);

        /// <summary>What the server last said about <paramref name="productId"/>; nothing owed when it said nothing.</summary>
        public static DailyPassStanding StandingOf(string productId)
            => productId != null && _held.TryGetValue(productId, out var standing) ? standing : default;

        /// <summary>True while the pass owes pieces: its card claims rather than sells.</summary>
        public static bool Holds(string productId) => StandingOf(productId).Remaining > 0;

        /// <summary>True when today's piece has not been paid yet, on <paramref name="nowUnix"/>'s UTC day.</summary>
        public static bool CanClaim(string productId, long nowUnix)
        {
            var standing = StandingOf(productId);
            return standing.Remaining > 0 && standing.PaidDay < Today(nowUnix);
        }

        /// <summary>Seconds until the next UTC day, when a claimed-today pass may claim again.</summary>
        public static long SecondsToNextClaim(long nowUnix)
            => nowUnix <= 0 ? 0L : 86400L - (nowUnix % 86400L);

        /// <summary>True while a claim for <paramref name="productId"/> is out.</summary>
        public static bool IsClaiming(string productId) => productId != null && _claiming.Contains(productId);

        // ---------------------------------------------------------------- the server
        /// <summary>
        /// Folds in a wallet reply. <paramref name="carried"/> is asked apart from the rows for
        /// <c>DealLedger.ApplyServerState</c>'s reason: a deployment that predates passes sends
        /// nothing, an account holding none sends an empty list, and only the second is an answer.
        /// Replaces the mirror whole - a pass missing from the rows is one the server owes nothing on.
        /// </summary>
        public static void ApplyServerState(bool carried, IReadOnlyList<CloudPassRow> rows)
        {
            if (!carried) return;

            bool changed = !_known;
            _known = true;

            var incoming = new Dictionary<string, DailyPassStanding>(StringComparer.Ordinal);
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.Id) || row.Remaining <= 0) continue;
                    incoming[row.Id] = new DailyPassStanding(row.Remaining, row.Day);
                }
            }

            if (incoming.Count != _held.Count) changed = true;
            else
            {
                foreach (var pair in incoming)
                {
                    if (!_held.TryGetValue(pair.Key, out var was) || !was.Equals(pair.Value)) { changed = true; break; }
                }
            }

            if (!changed) return;

            _held.Clear();
            foreach (var pair in incoming) _held[pair.Key] = pair.Value;
            Raise();
        }

        /// <summary>
        /// Asks the server to pay today's piece of <paramref name="product"/>.
        ///
        /// <para>
        /// <b>The server pays and this adopts</b>: the balances come back with the answer and go
        /// through the one door every wallet reply uses (<c>CloudSaveService.AdoptWalletStates</c>),
        /// which also refreshes this mirror. Nothing is credited on the device first, so a reply
        /// lost on the way back costs nothing but a retry - which the server answers
        /// <see cref="DailyPassClaimOutcome.NotYet"/> with the balance the first call already raised.
        /// </para>
        /// <para>
        /// <b>Gated as a purchase redemption is</b> (<c>AuthoriseForCallAsync</c>: no account repair,
        /// no account creation), and the answer is adopted only if the same account is still signed
        /// in - the server paid the account that asked, and another account's balances must never
        /// land in this one's ledgers (17). One claim per product at a time; a second tap while one
        /// is out answers <see cref="DailyPassClaimOutcome.Unavailable"/> and does nothing.
        /// </para>
        /// </summary>
        public static async Task<DailyPassClaim> ClaimAsync(StoreProduct product, CancellationToken cancellation = default)
        {
            if (product == null || !product.IsDailyPass || !IsAvailable)
                return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);

            string id = product.Id;
            if (!_claiming.Add(id)) return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);
            Raise();

            try
            {
                string account = CloudState.UserId;

                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok || CloudState.UserId != account)
                    return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);

                account = CloudState.UserId;
                var backend = Backend;
                if (backend == null || string.IsNullOrEmpty(account))
                    return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);

                var (result, claim, wallets) = await backend.ClaimDailyPassAsync(id, cancellation);
                if (!result.Ok) return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);

                // The server paid the account that asked. If somebody else is signed in now, that
                // account banks it on its next sync; adopting it here would put one account's coins
                // in another's ledger.
                if (CloudState.UserId != account) return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);

                CloudSaveService.AdoptWalletStates(wallets);
                SaveService.Save();

                Telemetry.Track("daily_pass_claimed", "product", id, "outcome", claim.Outcome.ToString(),
                                "credits", claim.Credits, "gems", claim.Gems);
                return claim;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                return new DailyPassClaim(DailyPassClaimOutcome.Unavailable);
            }
            finally
            {
                _claiming.Remove(id);
                Raise();
            }
        }

        /// <summary>What is owed belongs to the account just left; the next wallet reply says the new one's.</summary>
        static void OnIdentityChanged()
        {
            if (!_known && _held.Count == 0) return;
            _known = false;
            _held.Clear();
            Raise();
        }

        /// <summary>For tests: forgets everything, as a fresh process would.</summary>
        internal static void ResetForTests()
        {
            _held.Clear();
            _claiming.Clear();
            _known = false;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
