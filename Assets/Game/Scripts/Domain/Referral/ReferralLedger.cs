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

        /// <summary>
        /// Who this was rolled for. <see cref="Land"/> refuses if somebody else is signed in by
        /// the time the ceremony runs it — a payout is currency and hearts moving into a
        /// wallet, and there is no undo for putting them in the wrong one (invariant 17's
        /// reason). The account keeps its in-flight note, so it banks on its own next tap.
        /// </summary>
        readonly string _owner;

        List<ChestDrop> _landed;

        internal ReferralPayout(ReferralClaimKind kind, int goal, int index, ChestTier tier, ReferralReply reply,
                                ReferralLanding.Verdict verdict, CloudResult result)
        {
            _owner = CloudState.UserId ?? string.Empty;
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
            if (!string.Equals(_owner, CloudState.UserId ?? string.Empty, StringComparison.Ordinal)) return null;

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

        /// <summary>
        /// Two gates rather than one, and the split is what stops a background read from
        /// refusing the player's own tap.
        ///
        /// <para>
        /// One gate over all three calls meant the read <see cref="RefreshAsync"/> fires the
        /// moment the invite page opens refused the redeem the player typed a second later —
        /// reported to them as <em>unavailable</em>, which is a lie about a call that was never
        /// made. A read and a write do not conflict: the read asks a question, and if a write
        /// lands first the read's answer is simply out of date. So a read is skipped while
        /// another read is out (there is nothing to gain from two), a write is refused only by
        /// another write (which is a double tap), and the ordering between them is held by
        /// <see cref="_generation"/> rather than by a lock.
        /// </para>
        /// </summary>
        static int _reading, _writing;

        /// <summary>
        /// Bumped by every <see cref="Adopt"/>, so a slow read cannot overwrite a fresh write.
        ///
        /// <para>
        /// Without it: the page opens and asks for the state; the player types a code; the
        /// redeem lands and the state becomes <em>referred</em>; the read that was already in
        /// flight comes back with the pre-redeem answer and adopts it, and the page goes back
        /// to offering the code the player has just used. Every claim has the same shape. A
        /// read captures this before it calls and drops its reply if anything moved meanwhile,
        /// which is correct rather than merely safe — the thing it would be writing is known
        /// to be older than what is already held.
        /// </para>
        /// </summary>
        static int _generation;

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

        public static bool IsBusy => Volatile.Read(ref _writing) != 0 || Volatile.Read(ref _reading) != 0;

        /// <summary>Whether a redeem or a claim is out. A read is not a reason to refuse a tap.</summary>
        public static bool IsWriting => Volatile.Read(ref _writing) != 0;

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

            // Anything in flight belongs to the account that has just been left. Bumping here
            // is what makes `Reply` drop it; without it a read, a redeem or a claim issued as
            // one player could land as another — and `Adopt` would cache the first player's
            // code and counts under the second player's key. Invariant 17's shape, one layer
            // down: an answer may only ever be adopted by the account that asked for it.
            Interlocked.Increment(ref _generation);

            EnsureLoaded();
            Raise();
        }

        /// <summary>
        /// A call's claim on the answer it is waiting for: which account asked, and what the
        /// ledger had adopted at the time.
        ///
        /// <para>
        /// Taken before the call and tested after it. It fails closed — an answer that cannot
        /// prove it is still wanted is dropped rather than applied — and dropping one costs
        /// nothing, because whatever moved the ledger underneath it is by definition newer.
        /// </para>
        /// </summary>
        readonly struct Claim
        {
            readonly string _uid;
            readonly int _generation;

            Claim(string uid, int generation) { _uid = uid; _generation = generation; }

            public static Claim Take()
                => new Claim(CloudState.UserId ?? string.Empty, Volatile.Read(ref ReferralLedger._generation));

            /// <summary>Whether this answer may still be adopted.</summary>
            public bool Holds
                => string.Equals(_uid, CloudState.UserId ?? string.Empty, StringComparison.Ordinal)
                   && Volatile.Read(ref ReferralLedger._generation) == _generation;
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
            if (Interlocked.Exchange(ref _reading, 1) != 0)
                return CloudResult.Failed(CloudFailure.Busy, "a referral read is already out");

            try
            {
                // Taken before the call, not after: anything that lands while this is out is
                // newer than what comes back, and may be a different account's entirely.
                var claim = Claim.Take();

                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok) return authorised;

                var (result, reply) = await Backend.ReadReferralAsync(cancellation);
                if (result.Ok && reply != null && claim.Holds) Adopt(reply.State);
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _reading, 0);
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

            // Only another *write* refuses this, which on one device is a double tap the panel
            // has already blocked. A read in flight is no longer a reason to say no.
            if (Interlocked.Exchange(ref _writing, 1) != 0) return ReferralRedeemOutcome.Unavailable;

            try
            {
                var claim = Claim.Take();

                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok) return ReferralRedeemOutcome.Unavailable;

                var (result, reply) = await Backend.RedeemReferralAsync(code, cancellation);
                if (!result.Ok || reply == null) return ReferralRedeemOutcome.Unavailable;

                // The bind itself stands whatever happened here — it is the server's, and
                // it is keyed on the account that asked. What must not happen is *this*
                // account's state being written over whoever is signed in now.
                if (claim.Holds) Adopt(reply.State);

                Telemetry.Track("referral_redeemed", "outcome", reply.Redeem.ToString());
                return reply.Redeem;
            }
            finally
            {
                Interlocked.Exchange(ref _writing, 0);
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

            if (Interlocked.Exchange(ref _writing, 1) != 0)
                return new ReferralPayout(kind, goal, index, tier, null, ReferralLanding.Verdict.Nothing,
                                          CloudResult.Failed(CloudFailure.Busy, "a referral write is already out"));

            string subject = ReferralLanding.Subject(kind, goal, index);

            try
            {
                var claim = Claim.Take();

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

                // Somebody else is signed in now. The server has paid the account that asked
                // and the note is still standing on it, so the right thing is to do nothing
                // here: this player banks it the next time they sign in and tap. Opening a
                // ceremony would apply one account's hearts to another's wallet.
                if (!claim.Holds)
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
                Interlocked.Exchange(ref _writing, 0);
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
        /// <summary>
        /// Takes the server's answer as the truth about this account.
        ///
        /// <para>
        /// <b>It raises <see cref="Changed"/> only when the answer says something new</b>, and
        /// that is load-bearing rather than tidy. Every visit to the invite page fires a read,
        /// and the overwhelmingly common reply is the state the device already had; raising on
        /// it told the page a change had happened, and the page — which cannot tell a real
        /// change from a null one — redrew itself a network round-trip after it had drawn,
        /// replaying the entrance on all fifty rows and throwing away the scroll position. The
        /// comparison is <see cref="ReferralState.Matches"/>, which ignores the fetch stamp for
        /// exactly this reason.
        /// </para>
        /// <para>
        /// <b>The cache is written on the same test</b>, because <see cref="DevicePrefs"/>
        /// compares the serialised string and the fetch stamp is inside it — so an unconditional
        /// write is a whole-store <c>PlayerPrefs</c> flush on every screen entry, for a file
        /// whose content did not change. The first known answer is always written, or a device
        /// that read once and learned nothing new would never cache anything at all.
        /// </para>
        /// </summary>
        static void Adopt(ReferralState state)
        {
            if (state == null) return;
            EnsureLoaded();

            var before = _state;
            bool moved = !before.Matches(state);
            bool firstKnown = state.IsKnown && !before.IsKnown;

            _state = state;

            // `?? string.Empty` because `EnsureLoaded` compares against the same coalescing:
            // a null here against an empty string there is "a different account", and the very
            // next read of `State` would wipe what was just adopted back to `Empty`.
            _stateFor = CloudState.UserId ?? string.Empty;

            // Bumped whatever the answer said, including one that said nothing new: a read that
            // was in flight across this call is still older than this, and dropping it costs
            // nothing because it can only be carrying what is already held.
            Interlocked.Increment(ref _generation);

            if (CloudState.IsSignedIn && (moved || firstKnown))
                DevicePrefs.WriteString(CacheKey + ":" + CloudState.UserId, JsonUtility.ToJson(state.ToDto()));

            if (state.Finished > before.Finished)
                Telemetry.Track("referral_finished", "finished", state.Finished, "bound", state.Bound);

            if (state.MilestoneReached && !before.MilestoneReached && state.Referred)
                Telemetry.Track("referral_milestone");

            if (moved || firstKnown) Raise();
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

        /// <summary>
        /// The most notes one account may carry, and why there is a ceiling at all.
        ///
        /// <para>
        /// A note is written before a claim and cleared when its drops land, so a reply lost on
        /// the way back leaves one standing for ever — that is the design (see
        /// <see cref="ReferralLanding"/>), and it is what makes the retry bank rather than skip.
        /// The list is naturally bounded because a subject is only ever noted once, and the
        /// subject space is <c>maxBound * count + count</c>; the ceiling is a guard on a
        /// <em>retune</em> raising <c>maxBound</c>, not on the ordinary path, and it exists
        /// because this string is written to device storage and nothing else would ever bound
        /// it. The oldest goes, which is the note least likely to still be owed.
        /// </para>
        /// </summary>
        const int MaxNotes = 256;

        static void NoteInFlight(string subject)
        {
            if (!CloudState.IsSignedIn) return;
            var list = InFlight();
            if (list.Contains(subject)) return;
            list.Add(subject);
            while (list.Count > MaxNotes) list.RemoveAt(0);
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
            Interlocked.Exchange(ref _reading, 0);
            Interlocked.Exchange(ref _writing, 0);
            Interlocked.Exchange(ref _generation, 0);
        }
    }
}
