using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using UnityEngine;

namespace GlimmerGrove.Referral
{
    /// <summary>
    /// One chest the server has agreed to pay, held until the ceremony lands it.
    ///
    /// <para>
    /// The server has already moved the currency by the time one of these exists; what is
    /// still to do is bank the rest of the drops and adopt the balances, and that happens in
    /// <see cref="Land"/> — inside the chest overlay's claim, so the payout's snapshot of the
    /// pills is taken before the grant lands (<c>RewardFlight.Begin</c>'s rule) and the
    /// ceremony reports a chest being opened rather than one that was opened a moment ago.
    /// </para>
    /// </summary>
    public sealed class ReferralPayout
    {
        readonly ReferralReply _reply;
        readonly string _subject;
        List<ChestDrop> _landed;

        internal ReferralPayout(ReferralClaimKind kind, int goal, int index, ChestTier tier, ReferralReply reply,
                                ReferralLanding.Verdict verdict, CloudResult result)
        {
            Kind = kind;
            Goal = goal;
            Index = index;
            Tier = tier;
            Outcome = reply?.Claim ?? ReferralClaimOutcome.Unavailable;
            Verdict = verdict;
            Result = result;
            _reply = reply;
            _subject = ReferralLanding.Subject(kind, goal, index);
        }

        public ReferralClaimKind Kind { get; }

        /// <summary>Which finished invitee, counting from one. Nought for the invitee's own chest.</summary>
        public int Goal { get; }

        /// <summary>Which of the payment's chests, counting from one.</summary>
        public int Index { get; }
        public ChestTier Tier { get; }
        public ReferralClaimOutcome Outcome { get; }
        public ReferralLanding.Verdict Verdict { get; }
        public CloudResult Result { get; }

        /// <summary>Whether there is a ceremony to open: drops this device should bank.</summary>
        public bool Opens => Verdict == ReferralLanding.Verdict.Bank && Tier != null && _reply != null;

        /// <summary>
        /// Banks the drops and adopts the balances. Answers the drops, or null when there is
        /// nothing to land — which is what <c>ChestClaim</c> shows by closing. Idempotent: a
        /// second call answers the same list and banks nothing again.
        /// </summary>
        public List<ChestDrop> Land()
        {
            if (_landed != null) return _landed;
            if (!Opens) return null;

            var drops = new List<ChestDrop>(_reply.Drops.Count);
            foreach (var drop in _reply.Drops)
            {
                if (!drop.IsValid) continue;
                drops.Add(drop);

                // Currency is already in the wallet the server answered with, so `Apply`
                // answering false for it is the right answer twice over: nothing to bank, and
                // nothing to claim (the server paid it, invariant 10a's alternative shape).
                BankedDrop.Apply(drop);
            }

            CloudSaveService.AdoptWalletStates(_reply.Wallets);
            ReferralLedger.Landed(_subject, _reply.State);

            _landed = drops;
            return drops;
        }
    }

    /// <summary>
    /// Refer-a-friend on the device: the account's code, what the server has counted, and
    /// the three calls that move it.
    ///
    /// <para>
    /// <b>Nothing here is in the save, and that is the design</b> (invariant 51). Every count
    /// is a fact about <em>other</em> accounts — how many typed this code, how many cleared a
    /// chapter — which no device can know and no merge could join, so the server owns the
    /// lot and this class holds a per-account cache for drawing. A chest is <em>paid by the
    /// server on request</em> rather than claimed with a derived id: the request needs the
    /// network anyway, because the count it pays on lives nowhere else, and a payout the
    /// server rolls is one the client never predicts. What the client still does is bank the
    /// drops that are not currency, exactly once, by <see cref="ReferralLanding"/>'s rule.
    /// </para>
    /// <para>
    /// <b>The invitee's milestone is settled by asking.</b> The server judges the chapter off
    /// the save it holds, so the ask goes out after a <em>settled</em> sync — never after the
    /// change itself (invariant 19j) — and only when this device's own ledger says the
    /// chapter is cleared, once per server revision. A device that has never asked at all
    /// asks once, so an invitee who typed the code on one phone and played on another is
    /// still settled.
    /// </para>
    /// </summary>
    public static class ReferralLedger
    {
        const string CacheKey = "glimmer_referral";
        const string InFlightKey = "glimmer_referral_inflight";

        static ReferralState _state = ReferralState.Empty;
        static string _stateFor;
        static int _busy;
        static bool _attached;
        static long _askedRevision = -1;

        /// <summary>Raised whenever the cached state is replaced. Screens repaint from this.</summary>
        public static event Action Changed;

        public static ReferralTable Table => ProgressionRules.Table.Referral;

        static IReferralBackend Backend => CloudSaveService.Backend as IReferralBackend;

        /// <summary>Whether the invitee's own chests are owed and at least one is unpaid.</summary>
        public static bool InviteeClaimable => Claimable(ReferralClaimKind.Invitee, 0);

        /// <summary>Whether the feature exists on this build and this backend.</summary>
        public static bool IsAvailable => CloudSaveService.IsAvailable && Backend != null && Table.Offers;

        public static bool IsBusy => Volatile.Read(ref _busy) != 0;

        /// <summary>The last thing the server said about this account, or <see cref="ReferralState.Empty"/>.</summary>
        public static ReferralState State
        {
            get { EnsureLoaded(); return _state; }
        }

        // ------------------------------------------------------------- derived
        /// <summary>
        /// The first chest of a payment the server has not paid, counting from one, or nought
        /// when every chest of it is paid. Says nothing about whether it is <em>reachable</em>;
        /// see <see cref="Claimable"/>.
        /// </summary>
        public static int NextIndex(ReferralClaimKind kind, int goal)
        {
            var payment = Table.PaymentFor(kind);
            if (!payment.IsValid) return 0;

            for (int n = 1; n <= payment.Count; n++)
                if (!State.HasPaid(ReferralLanding.Subject(kind, goal, n))) return n;
            return 0;
        }

        /// <summary>Whether the server has reached this payment: the goal-th friend finished, or the invitee's own milestone.</summary>
        public static bool Reached(ReferralClaimKind kind, int goal)
            => kind == ReferralClaimKind.Invitee
                ? State.Referred && State.MilestoneReached
                : goal >= 1 && goal <= Table.MaxBound && State.Finished >= goal;

        /// <summary>Whether a tap on this payment would open a chest.</summary>
        public static bool Claimable(ReferralClaimKind kind, int goal)
            => Reached(kind, goal) && NextIndex(kind, goal) > 0;

        /// <summary>The first friend row a tap would pay, counting from one, or nought.</summary>
        public static int FirstClaimableFriend
        {
            get
            {
                int finished = Math.Min(State.Finished, Table.MaxBound);
                for (int friend = 1; friend <= finished; friend++)
                    if (NextIndex(ReferralClaimKind.Rung, friend) > 0) return friend;
                return 0;
            }
        }

        /// <summary>Whether this device's own ledger shows the milestone cleared. A hint, never a payout.</summary>
        public static bool MilestoneClearedHere
            => ReferralMilestone.IsComplete(GameContent.Index, Table.Milestone, PlayerProgress.IsCleared);

        public static (int cleared, int total) MilestoneProgress
            => ReferralMilestone.Progress(GameContent.Index, Table.Milestone, PlayerProgress.IsCleared);

        /// <summary>The sentence the share sheet sends: the code and, when content has one, the link.</summary>
        public static string ShareMessage
        {
            get
            {
                string code = ReferralCode.Display(State.Code);
                string link = Table.ShareLink;
                return string.IsNullOrEmpty(link)
                    ? Loc.Format("ui.referral.share_text_bare", code)
                    : Loc.Format("ui.referral.share_text", code, link);
            }
        }

        // -------------------------------------------------------------- wiring
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            CloudSaveService.Settled += OnSettled;
            CloudSaveService.IdentityChanged += OnIdentityChanged;
        }

        static void OnIdentityChanged()
        {
            _stateFor = null;
            _askedRevision = -1;
            EnsureLoaded();
            Raise();
        }

        /// <summary>
        /// After a settled sync: ask the server once per revision when there is something it
        /// could settle, and once ever when this device has never asked at all.
        /// </summary>
        static void OnSettled(SyncReceipt receipt)
        {
            if (!receipt.IsValid || !IsAvailable || !CloudState.IsSignedIn) return;
            if (receipt.ServerRevision == _askedRevision) return;

            var state = State;
            bool never = !state.IsKnown;
            bool owed = state.Referred && !state.MilestoneReached && MilestoneClearedHere;
            if (!never && !owed) return;

            _askedRevision = receipt.ServerRevision;
            _ = RefreshAsync();
        }

        // ------------------------------------------------------------- reading
        public static async Task<CloudResult> RefreshAsync(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no referral backend");
            if (Interlocked.Exchange(ref _busy, 1) != 0)
                return CloudResult.Failed(CloudFailure.Busy, "a referral call is already out");

            try
            {
                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok) return authorised;

                var (result, reply) = await Backend.ReadReferralAsync(cancellation);
                if (result.Ok && reply != null) Adopt(reply.State);
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        // ------------------------------------------------------------ redeeming
        /// <summary>
        /// Types a code. The fold and the shape check happen here, so a hopeless string
        /// never costs a call; everything else is the server's answer.
        /// </summary>
        public static async Task<ReferralRedeemOutcome> RedeemAsync(string typed,
                                                                    CancellationToken cancellation = default)
        {
            string code = ReferralCode.Normalise(typed);
            if (!ReferralCode.IsValid(code)) return ReferralRedeemOutcome.BadCode;

            if (!IsAvailable) return ReferralRedeemOutcome.Unavailable;
            if (Interlocked.Exchange(ref _busy, 1) != 0) return ReferralRedeemOutcome.Unavailable;

            try
            {
                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok) return ReferralRedeemOutcome.Unavailable;

                var (result, reply) = await Backend.RedeemReferralAsync(code, cancellation);
                if (!result.Ok || reply == null) return ReferralRedeemOutcome.Unavailable;

                Adopt(reply.State);

                Telemetry.Track("referral_redeemed", "outcome", reply.Redeem.ToString());
                return reply.Redeem;
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        // ------------------------------------------------------------- claiming
        /// <summary>
        /// Asks the server for a chest. Answers a payout whose <see cref="ReferralPayout.Opens"/>
        /// says whether there is a ceremony to run; the state is adopted here whatever the
        /// answer, so a rung another device took is drawn as taken without a second call.
        /// </summary>
        public static async Task<ReferralPayout> ClaimAsync(ReferralClaimKind kind, int goal, int index,
                                                           CancellationToken cancellation = default)
        {
            var payment = Table.PaymentFor(kind);
            var tier = payment.IsValid && index >= 1 && index <= payment.Count ? payment.Tier : null;

            if (!IsAvailable || tier == null)
                return new ReferralPayout(kind, goal, index, tier, null, ReferralLanding.Verdict.Nothing,
                                          CloudResult.Failed(CloudFailure.Offline, "no referral backend"));

            if (Interlocked.Exchange(ref _busy, 1) != 0)
                return new ReferralPayout(kind, goal, index, tier, null, ReferralLanding.Verdict.Nothing,
                                          CloudResult.Failed(CloudFailure.Busy, "a referral call is already out"));

            string subject = ReferralLanding.Subject(kind, goal, index);

            try
            {
                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok)
                    return new ReferralPayout(kind, goal, index, tier, null, ReferralLanding.Verdict.Nothing, authorised);

                // Noted before the call, so a reply lost on the way back still banks on the
                // retry. See ReferralLanding.
                bool inFlightHere = IsInFlight(subject);
                NoteInFlight(subject);

                var (result, reply) = await Backend.ClaimReferralAsync(kind, goal, index, cancellation);
                if (!result.Ok || reply == null)
                    return new ReferralPayout(kind, goal, index, tier, null, ReferralLanding.Verdict.Nothing, result);

                var verdict = ReferralLanding.Decide(reply.Claim, inFlightHere);

                // A refusal, or a chest somebody else banked: the note is spent either way.
                // Only a bankable answer keeps it, and Land is what clears that one.
                if (verdict != ReferralLanding.Verdict.Bank) ClearInFlight(subject);
                if (verdict != ReferralLanding.Verdict.Bank) Adopt(reply.State);

                Telemetry.Track("referral_claimed",
                                "kind", kind == ReferralClaimKind.Invitee ? "invitee" : "rung",
                                "goal", goal,
                                "index", index,
                                "tier", tier.Id,
                                "outcome", reply.Claim.ToString(),
                                "verdict", verdict.ToString());

                return new ReferralPayout(kind, goal, index, tier, reply, verdict, result);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>Called by a payout once its drops are banked.</summary>
        internal static void Landed(string subject, ReferralState state)
        {
            ClearInFlight(subject);
            Adopt(state);
            SaveService.Save();
        }

        // ---------------------------------------------------------------- state
        static void Adopt(ReferralState state)
        {
            if (state == null) return;
            EnsureLoaded();

            var before = _state;
            _state = state;
            _stateFor = CloudState.UserId;

            if (CloudState.IsSignedIn)
                DevicePrefs.WriteString(CacheKey + ":" + CloudState.UserId, JsonUtility.ToJson(state.ToDto()));

            if (state.Finished > before.Finished)
                Telemetry.Track("referral_finished", "finished", state.Finished, "bound", state.Bound);

            if (state.MilestoneReached && !before.MilestoneReached && state.Referred)
                Telemetry.Track("referral_milestone");

            Raise();
        }

        static void EnsureLoaded()
        {
            string uid = CloudState.UserId ?? string.Empty;
            if (string.Equals(_stateFor, uid, StringComparison.Ordinal)) return;

            _stateFor = uid;
            _state = ReferralState.Empty;
            if (uid.Length == 0) return;

            string raw = PlayerPrefs.GetString(CacheKey + ":" + uid, string.Empty);
            if (raw.Length == 0) return;

            try { _state = ReferralState.FromDto(JsonUtility.FromJson<ReferralStateDto>(raw)); }
            catch (Exception) { _state = ReferralState.Empty; }
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // -------------------------------------------------------------- in flight
        static string InFlightFor => InFlightKey + ":" + CloudState.UserId;

        static List<string> InFlight()
        {
            var list = new List<string>();
            if (!CloudState.IsSignedIn) return list;

            string raw = PlayerPrefs.GetString(InFlightFor, string.Empty);
            foreach (var part in raw.Split(','))
                if (part.Length > 0) list.Add(part);
            return list;
        }

        static bool IsInFlight(string subject) => InFlight().Contains(subject);

        static void NoteInFlight(string subject)
        {
            if (!CloudState.IsSignedIn) return;
            var list = InFlight();
            if (list.Contains(subject)) return;
            list.Add(subject);
            DevicePrefs.WriteString(InFlightFor, string.Join(",", list));
        }

        static void ClearInFlight(string subject)
        {
            if (!CloudState.IsSignedIn) return;
            var list = InFlight();
            if (!list.Remove(subject)) return;
            DevicePrefs.WriteString(InFlightFor, string.Join(",", list));
        }

        // ---------------------------------------------------------------- tests
        internal static void Reset()
        {
            _state = ReferralState.Empty;
            _stateFor = null;
            _askedRevision = -1;
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
