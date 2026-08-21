using System;

namespace GlimmerGrove.Social
{
    /// <summary>What the policy wants done next.</summary>
    public enum GrovePublishAction
    {
        /// <summary>Nothing is owed. By a wide margin the commonest answer.</summary>
        None = 0,

        /// <summary>Rebuild this account's public card.</summary>
        Publish,

        /// <summary>
        /// Take this account's card down.
        ///
        /// Raised when the player turns the board off, and it is a separate outcome rather
        /// than "publish nothing" because the two are different acts against the database and
        /// only one of them is what somebody asked for. A card left standing after an opt-out
        /// is a data-protection failure, not a stale cache.
        /// </summary>
        Withdraw,
    }

    /// <summary>
    /// Decides <em>when</em> this account's public card should be rebuilt. Knows nothing
    /// about how to rebuild one.
    ///
    /// <para>
    /// <b>Why a policy rather than a Firestore trigger.</b> The obvious server-side design
    /// watches <c>players/{uid}</c> and rebuilds the card whenever the save is written. That
    /// is a function invocation per sync per player, for ever, on the busiest write path in
    /// the game — and a sync is raised by a star, a heart, a chest and a streak night, none
    /// of which change anything a visitor can see. A card is worth rebuilding a handful of
    /// times a week. So the client asks, only when the part of the grove a stranger can see
    /// has actually moved, and the server still recomputes everything it publishes: the
    /// request carries no score and no contents, which is what makes letting the client
    /// choose the moment safe. See <c>functions/src/grove.ts</c>.
    /// </para>
    /// <para>
    /// <b>A player who never asks is simply not on the board.</b> That is the whole exploit
    /// available here and it is self-punishing, which is the shape a forgeable trigger should
    /// have.
    /// </para>
    /// <para>
    /// <b>An empty grove is never published.</b> On the day this ships most accounts hold
    /// nothing, and a card for each of them is a document, a write and a row in a sample for
    /// a grove with nothing on it. <see cref="Worth"/> gates it, which also keeps the
    /// published distribution to keepers who have built something — see
    /// <see cref="GroveRankTable"/> for why that population is the one that means anything.
    /// </para>
    /// <para>
    /// It holds no clock, no socket and no Unity types: it is handed elapsed time and told
    /// what changed, which is <c>SyncScheduler</c>'s bargain and what makes the whole policy
    /// runnable offline in the test suite.
    /// </para>
    /// </summary>
    public sealed class GrovePublishPolicy
    {
        /// <summary>
        /// How long a change waits before it is sent, so a burst becomes one publish.
        ///
        /// Longer than <c>SyncScheduler.DebounceSeconds</c> on purpose. A rename is a change
        /// the player is watching for and wants to see stick; a shopping trip is five
        /// purchases and a rearrangement, and nobody is watching the board while they make
        /// it. Ten seconds turns a session of decorating into one write.
        /// </summary>
        public const float DebounceSeconds = 10f;

        /// <summary>The first retry after a failure. Doubles from here.</summary>
        public const float FirstRetrySeconds = 15f;

        /// <summary>
        /// The longest gap between retries. Ten minutes — twice
        /// <c>SyncScheduler.MaxRetrySeconds</c>, because nothing about a stale card costs the
        /// player anything, whereas a save that has not reached the server is progress at
        /// risk. Retrying this as hard as a sync would spend a battery on a leaderboard row.
        /// </summary>
        public const float MaxRetrySeconds = 600f;

        /// <summary>
        /// The least a grove may be worth before it is published at all.
        ///
        /// One credit: the bar is "has this player put anything into the place", not a
        /// threshold anybody has to tune. Free pieces are worth nothing (invariant 16g), so a
        /// brand-new grove with its starter furniture scores zero and stays off the board
        /// until its owner buys something — which is the first moment there is anything to
        /// show.
        /// </summary>
        public const long Worth = 1L;

        string _publishedFingerprint = string.Empty;
        string _wantedFingerprint = string.Empty;

        /// <summary>What the in-flight call is doing, or <see cref="GrovePublishAction.None"/>.</summary>
        GrovePublishAction _inFlight = GrovePublishAction.None;

        /// <summary>
        /// The fingerprint the in-flight publish carries.
        ///
        /// Held because the pending mark is consumed when the call <em>starts</em>, so a
        /// failure has to be able to put back exactly what it took — see <see cref="Failed"/>.
        /// </summary>
        string _inFlightFingerprint = string.Empty;

        bool _wanted;
        bool _withdrawWanted;
        bool _reachable = true;
        float _wait;
        float _backoff;

        /// <summary>True when something is waiting to reach the server.</summary>
        public bool HasWork
            => _wanted || _withdrawWanted || _inFlight != GrovePublishAction.None;

        /// <summary>Seconds until the next attempt. For diagnostics only.</summary>
        public float SecondsUntilAttempt => _wait;

        /// <summary>What the last successful publish put on the board. Empty when nothing has.</summary>
        public string PublishedFingerprint => _publishedFingerprint;

        /// <summary>
        /// Takes a fingerprint remembered from a previous launch as already published.
        ///
        /// <para>
        /// Without it, the first publish request of every session is a write — because a fresh
        /// policy knows nothing about the board and cannot tell "unchanged since yesterday"
        /// from "never sent". That is one document write per player per launch, for ever, for
        /// a grove that has not moved. With it, a relaunch costs nothing at all, which is the
        /// commonest case by a wide margin.
        /// </para>
        /// <para>
        /// It is only ever adopted, never asserted: if the remembered value is wrong — a
        /// publish that failed after the note was written, a card removed on the server — the
        /// cost is one stale card until the next real change, and the daily ranking job reads
        /// whatever is there. That is the right way round for a value nothing can verify.
        /// </para>
        /// </summary>
        public void Adopt(string fingerprint)
        {
            if (_inFlight != GrovePublishAction.None || _wanted || _withdrawWanted) return;

            _publishedFingerprint = fingerprint ?? string.Empty;
        }

        /// <summary>
        /// The grove changed. Safe to call as often as anything likes.
        ///
        /// <para>
        /// The fingerprint is what makes this cheap: <see cref="GroveCard.Fingerprint"/>
        /// covers exactly what a visitor can see, so a sync that moved a star rating asks for
        /// nothing. A request identical to what is already on the board is dropped here rather
        /// than at the server, which is the only place it can be dropped for free.
        /// </para>
        /// </summary>
        public void Request(string fingerprint, bool worthPublishing)
        {
            fingerprint = fingerprint ?? string.Empty;

            // Nothing worth showing yet. Not a refusal to be retried — the next request
            // carrying something reaches here on its own.
            if (!worthPublishing) return;

            if (string.Equals(fingerprint, _publishedFingerprint, StringComparison.Ordinal) &&
                !_wanted && _inFlight == GrovePublishAction.None)
            {
                return;
            }

            _wantedFingerprint = fingerprint;
            _withdrawWanted = false;
            _wanted = true;

            if (_backoff <= 0f) _wait = DebounceSeconds;
        }

        /// <summary>
        /// The player turned the board off, or the account changed to one that has.
        ///
        /// <para>
        /// Withdrawal outranks a pending publish and clears it, which is the only ordering
        /// that cannot leave a card standing: the alternative publishes and then deletes, and
        /// a device that dies between the two has published something somebody asked it not
        /// to. It is still owed when nothing was ever published, because this device cannot
        /// know what another one did.
        /// </para>
        /// </summary>
        public void RequestWithdrawal()
        {
            _wanted = false;
            _wantedFingerprint = string.Empty;
            _withdrawWanted = true;

            if (_backoff <= 0f) _wait = DebounceSeconds;
        }

        /// <summary>The device's connectivity, polled by the caller. See <c>SyncScheduler</c>.</summary>
        public void NetworkChanged(bool reachable)
        {
            bool regained = reachable && !_reachable;
            _reachable = reachable;

            if (!regained || !HasWork) return;

            _backoff = 0f;
            _wait = DebounceSeconds;
        }

        /// <summary>
        /// Forgets what is on the board, without asking for anything.
        ///
        /// <para>
        /// For an account switch. The fingerprint describes <em>an account's</em> card, so
        /// carrying one across a switch would let the incoming account's identical-looking
        /// grove suppress its own first publish — and the incoming player would find
        /// themselves absent from the board with nothing to do about it. Invariant 17's
        /// discipline: anything keyed to an account is dropped when the account changes.
        /// </para>
        /// </summary>
        public void Forget()
        {
            _publishedFingerprint = string.Empty;
            _wantedFingerprint = string.Empty;
            _inFlightFingerprint = string.Empty;
            _wanted = false;
            _withdrawWanted = false;
            _inFlight = GrovePublishAction.None;
            _backoff = 0f;
            _wait = 0f;
        }

        /// <summary>
        /// A publish has begun, so everything asked for up to now is about to be sent.
        ///
        /// The pending mark is consumed here rather than on the reply, which is
        /// <c>SyncScheduler.Started</c>'s rule and its reason: a change made while the call is
        /// in flight must leave a fresh request behind rather than being cleared by the
        /// success of a call that never carried it.
        /// </summary>
        public GrovePublishAction Started()
        {
            var action = _withdrawWanted ? GrovePublishAction.Withdraw
                       : _wanted ? GrovePublishAction.Publish
                       : GrovePublishAction.None;

            if (action == GrovePublishAction.None) return action;

            _inFlight = action;
            _wait = 0f;

            // Consumed here rather than on the reply. That is the whole of why a purchase made
            // while a call is in flight survives it: the reply clears nothing, so a request
            // that arrived after this line is still owed when the reply lands. Getting this
            // backwards is what lost a keeper's name for a year — see SyncScheduler.Started —
            // and GrovePublishPolicyTests caught it here on the first run.
            if (action == GrovePublishAction.Withdraw)
            {
                _withdrawWanted = false;
                _inFlightFingerprint = string.Empty;
            }
            else
            {
                _wanted = false;
                _inFlightFingerprint = _wantedFingerprint;
            }

            return action;
        }

        /// <summary>
        /// It reached the server. <paramref name="fingerprint"/> is what was sent — passed
        /// back rather than remembered, so a request that arrived mid-flight is not mistaken
        /// for the one that was published.
        /// </summary>
        public void Succeeded(string fingerprint)
        {
            var was = _inFlight;
            _inFlight = GrovePublishAction.None;
            _backoff = 0f;

            // A withdrawal leaves nothing on the board, so nothing is "already published" and
            // an identical grove has to go back up if the player opts in again.
            _publishedFingerprint = was == GrovePublishAction.Withdraw
                ? string.Empty
                : fingerprint ?? string.Empty;

            _wait = HasWork ? DebounceSeconds : 0f;
        }

        /// <summary>
        /// It did not reach the server, so the work is still owed and further away.
        ///
        /// The pending mark was consumed when the call started, so this puts it back — unless
        /// something newer has already asked for the opposite, in which case restoring it
        /// would resurrect a withdrawal over an opt-in the player has since made.
        /// </summary>
        public void Failed()
        {
            var was = _inFlight;
            _inFlight = GrovePublishAction.None;

            if (was == GrovePublishAction.Withdraw)
            {
                if (!_wanted) _withdrawWanted = true;
            }
            else if (was == GrovePublishAction.Publish)
            {
                if (!_wanted && !_withdrawWanted)
                {
                    _wanted = true;
                    _wantedFingerprint = _inFlightFingerprint;
                }
            }

            _backoff = _backoff <= 0f ? FirstRetrySeconds : _backoff * 2f;
            if (_backoff > MaxRetrySeconds) _backoff = MaxRetrySeconds;

            _wait = _backoff;
        }

        /// <summary>
        /// The server refused, permanently, for a reason that will still be true next time —
        /// an unpublishable name, an account with no save to publish.
        ///
        /// <para>
        /// Dropped rather than retried, which is invariant 13a's rule in the one place here it
        /// applies: the client resubmits a retryable failure for the life of the account, so a
        /// permanent refusal treated as retryable is a loop nobody ever notices. It is safe to
        /// drop because the next real change asks again.
        /// </para>
        /// </summary>
        public void Refused()
        {
            _inFlight = GrovePublishAction.None;
            _inFlightFingerprint = string.Empty;
            _wanted = false;
            _withdrawWanted = false;
            _backoff = 0f;
            _wait = 0f;
        }

        /// <summary>
        /// Advances the policy and answers what should be done now, if anything.
        ///
        /// The timer does not run while the network is down, which is
        /// <c>SyncScheduler.Tick</c>'s rule: a debounce counted out in a tunnel arrives at
        /// zero the moment the player surfaces and fires the one attempt certain to fail.
        /// </summary>
        public GrovePublishAction Tick(float deltaSeconds)
        {
            if (_inFlight != GrovePublishAction.None || !HasWork || !_reachable)
                return GrovePublishAction.None;

            if (deltaSeconds > 0f) _wait -= deltaSeconds;
            if (_wait > 0f) return GrovePublishAction.None;

            return Started();
        }

        /// <summary>The fingerprint the pending publish carries. For diagnostics and tests.</summary>
        public string WantedFingerprint => _wantedFingerprint;

        /// <summary>
        /// The fingerprint the call that has just started is carrying.
        ///
        /// Read by the caller immediately after <see cref="Tick"/> hands back
        /// <see cref="GrovePublishAction.Publish"/>, and handed straight back to
        /// <see cref="Succeeded"/>. A property of its own rather than reusing
        /// <see cref="WantedFingerprint"/>, which a request arriving a moment later moves —
        /// and a reply then credited to the wrong fingerprint would mark a grove published
        /// that never was.
        /// </summary>
        public string InFlightFingerprint => _inFlightFingerprint;
    }
}
