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
            if (!ReferralLanding.PaysInto(_owner, CloudState.UserId)) return null;

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
        /// <see cref="Claim"/> rather than by a lock — asymmetrically, because the two are not
        /// equals: a read yields to a write and a write yields to nothing.
        /// </para>
        /// </summary>
        static int _reading, _writing;

        /// <summary>
        /// Bumped by every <see cref="Adopt"/>, so a slow <em>read</em> cannot overwrite a fresh
        /// write.
        ///
        /// <para>
        /// Without it: the page opens and asks for the state; the player types a code; the
        /// redeem lands and the state becomes <em>referred</em>; the read that was already in
        /// flight comes back with the pre-redeem answer and adopts it, and the page goes back
        /// to offering the code the player has just used. A read captures this before it calls
        /// and drops its reply if anything moved meanwhile, which is correct rather than merely
        /// safe — the thing it would be writing is known to be older than what is already held.
        /// </para>
        /// <para>
        /// <b>A write is deliberately not ordered by it</b> (<see cref="Claim.Write"/>), and
        /// making it so was a bug that cost a redeem: with the calls overlapping the other way
        /// round — redeem out, read out, read back, redeem back — the read's arrival bumped
        /// this and the redeem's own reply was then discarded as stale. A write is the freshest
        /// word there is about this account, because the server has just acted on it.
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
            => Reached(State, Table, kind, goal);

        static bool Reached(ReferralState state, ReferralTable table, ReferralClaimKind kind, int goal)
            => kind == ReferralClaimKind.Invitee
                ? state.Referred && state.MilestoneReached
                : goal >= 1 && goal <= table.MaxBound
                  && state.FriendStatus(goal) == ReferralFriendStatus.Finished;

        /// <summary>
        /// Where the <paramref name="friend"/>-th row of the board stands, capped by the table:
        /// a seat past the cap is never anything but open, whatever the count says.
        /// </summary>
        public static ReferralFriendStatus StatusOf(int friend)
            => friend >= 1 && friend <= Table.MaxBound ? State.FriendStatus(friend) : ReferralFriendStatus.Open;

        /// <summary>Whether a tap on this payment would open a chest.</summary>
        public static bool Claimable(ReferralClaimKind kind, int goal)
            => Reached(kind, goal) && NextIndex(kind, goal) > 0;

        /// <summary>
        /// How many chests this account could open right now, both sides added together: the
        /// invitee's own welcome chests once the milestone is reached, and every unpaid chest of
        /// every friend who finished. What the badge on the invite door counts.
        ///
        /// <para>
        /// A count of <em>chests</em> rather than of rows, because that is what a tap opens and
        /// what the hub's task badge counts — a row paying two chests with one taken is one
        /// thing still to collect, and a badge reading the row would say nothing had changed
        /// after the first ceremony.
        /// </para>
        /// </summary>
        public static int WaitingCount => Waiting(State, Table);

        /// <summary>The rule under <see cref="WaitingCount"/>, over any state and table, so a test can reach it.</summary>
        internal static int Waiting(ReferralState state, ReferralTable table)
        {
            if (state == null || table == null || !table.Offers) return 0;

            int waiting = 0;

            var own = table.Invitee;
            if (own.IsValid && Reached(state, table, ReferralClaimKind.Invitee, 0))
                waiting += own.Count - state.PaidCount(ReferralClaimKind.Invitee, 0, own.Count);

            var per = table.PerInvitee;
            if (per.IsValid)
            {
                int finished = Math.Min(state.Finished, table.MaxBound);
                for (int friend = 1; friend <= finished; friend++)
                {
                    if (!Reached(state, table, ReferralClaimKind.Rung, friend)) continue;
                    waiting += per.Count - state.PaidCount(ReferralClaimKind.Rung, friend, per.Count);
                }
            }

            return waiting < 0 ? 0 : waiting;
        }

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

            // The listener was watching the account that has just been left. `SettleListener`
            // stops it and attaches one for whoever is signed in now, or nothing if that is
            // nobody — a stream against a signed-out uid is a permission error on a loop.
            SettleListener();

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
        /// <summary>
        /// A call's claim on the answer it is waiting for.
        ///
        /// <para>
        /// <b>A read and a write claim different things, and conflating them cost a redeem.</b>
        /// A read asks a question, so anything that landed while it was out is fresher and the
        /// read's answer is dropped. A write <em>is</em> the freshest word there is about this
        /// account — the server has just acted on it — so a read that happens to come back
        /// after it must not discard it. Both were <see cref="Take"/> for a while, and the
        /// order redeem-out, read-out, read-back, redeem-back left the ledger never learning
        /// the code had been bound: the panel closed, the toast said welcome, and the page went
        /// on offering to type a code.
        /// </para>
        /// <para>
        /// What both care about is <em>whose</em> answer it is. A switch is local and instant
        /// (invariant 17a) and a call is not, so either kind is dropped outright when somebody
        /// else is signed in by the time it lands.
        /// </para>
        /// </summary>
        readonly struct Claim
        {
            readonly string _uid;
            readonly int _generation;
            readonly bool _ordered;

            Claim(string uid, int generation, bool ordered)
            {
                _uid = uid;
                _generation = generation;
                _ordered = ordered;
            }

            /// <summary>For a read: droppable by anything that lands while it is out.</summary>
            public static Claim Take()
                => new Claim(CloudState.UserId ?? string.Empty,
                             Volatile.Read(ref ReferralLedger._generation), true);

            /// <summary>For a redeem or a claim: only a change of account may discard it.</summary>
            public static Claim Write()
                => new Claim(CloudState.UserId ?? string.Empty, 0, false);

            /// <summary>
            /// A claim that always holds, for a caller that has already proved the answer is
            /// wanted by other means — <see cref="Landed"/>, whose payout checked its own owner
            /// before it banked a thing (<see cref="ReferralPayout.Land"/>).
            /// </summary>
            public static Claim Open => new Claim(null, 0, false);

            /// <summary>Whether this answer may still be adopted.</summary>
            public bool Holds
                => StillWanted(_uid, _generation, _ordered,
                               CloudState.UserId ?? string.Empty,
                               Volatile.Read(ref ReferralLedger._generation));
        }

        /// <summary>
        /// The staleness rule, as a function of what was true when a call went out and what is
        /// true now. A null <paramref name="uidThen"/> is <see cref="Claim.Open"/>;
        /// <paramref name="ordered"/> is false for a write, which nothing but a change of
        /// account may discard.
        ///
        /// <para>
        /// Pure, named and tested, rather than four operators inside a property — because it is
        /// the rule that decides whether one player's referral state may be written over
        /// another's, and that is the one failure on this page with no undo.
        /// </para>
        /// </summary>
        internal static bool StillWanted(string uidThen, int generationThen, bool ordered,
                                         string uidNow, int generationNow)
            => uidThen == null
               || (string.Equals(uidThen, uidNow ?? string.Empty, StringComparison.Ordinal)
                   && (!ordered || generationThen == generationNow));

        /// <summary>
        /// Whether an answer says anything the device did not already hold, and so whether
        /// anything should be raised or cached for it.
        ///
        /// <para>
        /// Two clauses, and the second is the one that is easy to leave out. The state may say
        /// exactly what <see cref="ReferralState.Empty"/> says and still be a change, because
        /// the *first* answer turns <see cref="ReferralState.IsKnown"/> on — and that is what
        /// moves the offer row off the device's own guess and onto the server's word.
        /// </para>
        /// </summary>
        internal static bool SaysSomethingNew(ReferralState before, ReferralState after)
        {
            if (after == null) return false;
            if (before == null) return true;

            return !before.Matches(after) || (after.IsKnown && !before.IsKnown);
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

        // ------------------------------------------------------------- watching
        /// <summary>
        /// How often something watching this should ask, in seconds — with a live listener, and
        /// without one.
        ///
        /// <para>
        /// <b>A referral count is the one thing here no local event can announce.</b> How many
        /// strangers typed the code and how many cleared the chapter are facts about other
        /// people's play (invariant 51), so nothing on this device fires when one changes — the
        /// sync hook (<see cref="OnSettled"/>) is driven by *this* account's saves and says
        /// nothing about an invitee's.
        /// </para>
        /// <para>
        /// <b>The listener is the answer and the timer is the net.</b> When a watch is attached
        /// a change arrives in the moment it happens and the timer is a slow backstop, for the
        /// three cases a listener cannot cover: a deployment whose server half does not bump the
        /// feed yet, a backend that cannot watch at all, and the window between the app being
        /// backgrounded and the listener being re-attached. Without a watch it is the whole
        /// mechanism, so it runs at the shorter figure.
        /// </para>
        /// <para>
        /// <b>Both figures are a bill, and the bill is per minute on the page rather than per
        /// player.</b> Every ask is a callable that opens a transaction and reads this account's
        /// referral document <em>and its whole save</em> (<c>getReferral</c> judges the milestone
        /// off the save the server holds, invariant 51a), so a poll is the most expensive thing a
        /// standing screen in this game does per minute — for a number that moves when a stranger
        /// finishes a chapter, which is rare. The listener already carries the common case in the
        /// moment it happens, so the net behind it is slow (ten minutes) and the one without it,
        /// which is the failure path rather than the normal one, is two minutes: a friend who
        /// finished is seen within that on a backend that cannot watch, and a redeem or a claim
        /// answers with the state directly, so nothing a player does on the page waits on either.
        /// A returning app asks once regardless (<see cref="Resumed"/>).
        /// </para>
        /// <para>
        /// The clock that keeps either one is <c>ReferralWatch</c>'s, in Presentation, because
        /// deciding whether to ask reads <c>Net</c> and <c>Domain</c> may never reference
        /// <c>Presentation</c> (invariant 3). What lives here is the numbers, the listener and
        /// <see cref="Poke"/>; what lives there is the frame to count on.
        /// </para>
        /// </summary>
        public const float PollSeconds = 120f, WatchedPollSeconds = 600f;

        /// <summary>
        /// How long an answer counts as fresh enough that asking again would be waste.
        ///
        /// It is what makes <see cref="Poke"/> safe to call from anywhere that thinks the answer
        /// may have moved — a screen opening, a tab returning, a watcher ticking — without three
        /// of them in a row costing three calls.
        /// </summary>
        const double FreshSeconds = 5d;

        static double _lastAsk = double.NegativeInfinity;

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>
        /// Asks the server for a fresh copy, unless something asked a moment ago. Answers
        /// whether a call actually went out.
        /// </summary>
        public static bool Poke()
        {
            if (!IsAvailable) return false;
            if (Now - _lastAsk < FreshSeconds) return false;

            _lastAsk = Now;
            _ = RefreshAsync();
            return true;
        }

        // ------------------------------------------------------------- the listener
        //
        // The whole of the lifetime rule, in one place: a listener exists exactly while
        // somebody is watching, the app is in the foreground, and an account is signed in.
        // Every path that can change one of those three ends at `Settle`, which attaches or
        // detaches to match — so there is no sequence of screen changes, backgroundings and
        // account switches that can leave one running with nobody to hear it.

        static readonly ReferralFeedWatch Feed = new ReferralFeedWatch(
            open: moved => Backend?.WatchReferral(moved),
            account: () => CloudState.UserId ?? string.Empty,
            available: () => IsAvailable,
            moved: OnFeedMoved);

        static int _signalled;

        /// <summary>Whether a live listener is attached, so a caller can slow its own timer.</summary>
        public static bool IsWatching => Feed.IsWatching;

        /// <summary>
        /// Starts watching on somebody's behalf, and keeps watching until the handle is
        /// disposed. Ref-counted, so two watchers are one listener and the last one out turns
        /// it off.
        /// </summary>
        public static IDisposable Watch() => Feed.Hold();

        /// <summary>
        /// The app is going away. The listener goes with it.
        ///
        /// <para>
        /// <b>A socket held open across a backgrounding is the one cost this design must not
        /// pay.</b> Firestore would keep the stream alive and the OS may or may not let it —
        /// on a handset that is a radio kept warm for a page nobody is looking at, and on iOS
        /// it is a connection that will be torn down under us anyway. Detaching is cheap and
        /// re-attaching is cheaper than being wrong about which.
        /// </para>
        /// </summary>
        public static void Paused()
        {
            Feed.Pause();

            // A flag raised by the listener that has just been stopped is answered by the ask
            // `Resumed` makes, not by a pump on the way out.
            Interlocked.Exchange(ref _signalled, 0);
        }

        /// <summary>
        /// Back in the foreground: the listener returns, and the state is asked for once.
        ///
        /// <para>
        /// <b>The ask is the half that is easy to leave out.</b> Nothing was listening while the
        /// app was away, so anything that happened in the gap produced no callback and never
        /// will — a re-attached listener reports the document as it is now, which is a snapshot
        /// this device has no reason to think is new. Without the poke a friend who finished
        /// overnight would not show until the backstop timer came round.
        /// </para>
        /// </summary>
        public static void Resumed()
        {
            Feed.Resume();
            Poke();
        }

        /// <summary>Re-points the listener after an account switch, or drops it on a sign-out.</summary>
        static void SettleListener() => Feed.Settle();

        /// <summary>
        /// The feed moved. <b>This may be on any thread</b>, so it does exactly one thing that
        /// is safe on any thread and leaves the rest to <see cref="Pump"/>.
        /// </summary>
        static void OnFeedMoved() => Interlocked.Exchange(ref _signalled, 1);

        /// <summary>
        /// Called once a frame from the main thread by whatever is watching. Turns a flag the
        /// listener set into the ask that answers it.
        ///
        /// <para>
        /// <b>This is the marshal, and it is here rather than in the backend because the
        /// backend cannot promise a thread.</b> The Firestore SDK ships as a DLL and its
        /// threading for snapshot callbacks is not something this project can read off disk, so
        /// nothing assumes it: were a callback to run <see cref="Adopt"/> on a pool thread it
        /// would raise <see cref="Changed"/> straight into screen code and write
        /// <c>PlayerPrefs</c>, neither of which survives it. A flag costs nothing and is right
        /// either way — and it coalesces a burst of changes into one ask for free.
        /// </para>
        /// </summary>
        public static void Pump()
        {
            if (Interlocked.Exchange(ref _signalled, 0) == 0) return;
            Poke();
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
                if (result.Ok && reply != null) Adopt(reply.State, claim);
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
                var claim = Claim.Write();

                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok) return ReferralRedeemOutcome.Unavailable;

                var (result, reply) = await Backend.RedeemReferralAsync(code, cancellation);
                if (!result.Ok || reply == null) return ReferralRedeemOutcome.Unavailable;

                // The bind itself stands whatever happened here — it is the server's, and
                // it is keyed on the account that asked. What must not happen is *this*
                // account's state being written over whoever is signed in now.
                Adopt(reply.State, claim);

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
                var claim = Claim.Write();

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
                if (verdict != ReferralLanding.Verdict.Bank) Adopt(reply.State, claim);

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
            Adopt(state, Claim.Open);
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
        /// <para>
        /// <b>The staleness test is in here rather than at the three call sites</b>, so there is
        /// one place that decides and it decides in the same breath as it writes — a caller
        /// cannot forget it, and there is no window between asking and acting for a reader of
        /// this file to wonder about.
        /// </para>
        /// <para>
        /// <b>Threading.</b> This, and everything it raises, is main-thread only: it writes
        /// <c>PlayerPrefs</c> and raises <see cref="Changed"/> straight into screen code, and
        /// neither tolerates anything else. The interlocked counters above are not a claim to
        /// the contrary — they guard the two <em>in-flight flags</em>, which are set on either
        /// side of an <c>await</c> and which <see cref="IsBusy"/> may be read from anywhere.
        /// </para>
        /// </summary>
        static void Adopt(ReferralState state, Claim claim)
        {
            if (state == null || !claim.Holds) return;
            EnsureLoaded();

            var before = _state;
            bool news = SaysSomethingNew(before, state);

            _state = state;

            // `?? string.Empty` because `EnsureLoaded` compares against the same coalescing:
            // a null here against an empty string there is "a different account", and the very
            // next read of `State` would wipe what was just adopted back to `Empty`.
            _stateFor = CloudState.UserId ?? string.Empty;

            // Bumped whatever the answer said, including one that said nothing new: a read that
            // was in flight across this call is still older than this, and dropping it costs
            // nothing because it can only be carrying what is already held.
            Interlocked.Increment(ref _generation);

            if (CloudState.IsSignedIn && news)
                DevicePrefs.WriteString(CacheKey + ":" + CloudState.UserId, JsonUtility.ToJson(state.ToDto()));

            if (state.Finished > before.Finished)
                Telemetry.Track("referral_finished", "finished", state.Finished, "bound", state.Bound);

            if (state.MilestoneReached && !before.MilestoneReached && state.Referred)
                Telemetry.Track("referral_milestone");

            if (news) Raise();
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
        /// The most notes one account may carry for a given table: every subject that table can
        /// mint, and no more.
        ///
        /// <para>
        /// A note is written before a claim and cleared when its drops land, so a reply lost on
        /// the way back leaves one standing for ever — that is the design (see
        /// <see cref="ReferralLanding"/>), and it is what makes the retry bank rather than skip.
        /// The list is already bounded in the ordinary case, because a subject is noted at most
        /// once and there are only so many subjects; what this guards is the string outliving
        /// the table that minted it, after a retune cuts <c>maxBound</c> or a payment's count.
        /// The oldest goes, which is the note least likely to still be owed.
        /// </para>
        /// <para>
        /// <b>Derived rather than typed</b> (invariant 5's habit): a figure written down beside
        /// the thing it is meant to bound is a figure that stops bounding it the first time the
        /// content moves. It is the referrer's rungs plus the invitee's own chests, which is
        /// exactly what <see cref="ReferralLanding.Subject"/> can spell, with a floor so a
        /// withdrawn table cannot bound the list at nought and evict a note that is still owed.
        /// </para>
        /// </summary>
        internal static int NotesCeiling(ReferralTable table)
        {
            if (table == null) return 16;

            int rungs = Math.Max(0, table.MaxBound) * Math.Max(0, table.PerInvitee.Count);
            return Math.Max(16, rungs + Math.Max(0, table.Invitee.Count));
        }

        static void NoteInFlight(string subject)
        {
            if (!CloudState.IsSignedIn) return;
            var list = InFlight();
            if (list.Contains(subject)) return;
            list.Add(subject);

            int most = NotesCeiling(Table);
            while (list.Count > most) list.RemoveAt(0);

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
            Feed.Reset();

            _state = ReferralState.Empty;
            _stateFor = null;
            _askedRevision = -1;
            _lastAsk = double.NegativeInfinity;
            Interlocked.Exchange(ref _signalled, 0);
            Interlocked.Exchange(ref _reading, 0);
            Interlocked.Exchange(ref _writing, 0);
            Interlocked.Exchange(ref _generation, 0);
        }
    }
}
