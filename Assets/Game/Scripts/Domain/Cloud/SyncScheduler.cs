namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// Decides <em>when</em> a sync should run. Knows nothing about how to run one.
    ///
    /// <para>
    /// Before this, a sync happened at exactly three moments: after the splash, when the
    /// app was backgrounded, and when it came back. That is enough while everything the
    /// player changes is something they will keep changing for another twenty minutes —
    /// stars, hearts, chests — and it is not enough for a change they make once and
    /// expect to stick. Two failures came out of it, and both were reported as "it does
    /// not save".
    /// </para>
    /// <para>
    /// The first is that backgrounding is the <em>worst</em> moment to start a network
    /// call, not the best: the process is being frozen, the continuation may not run
    /// again for hours, and on Android it may not run at all. The second is that a sync
    /// which failed was simply forgotten — a player who renamed themselves on a train
    /// pushed nothing, and pushed nothing again when the signal came back, because
    /// nothing was watching for the signal coming back.
    /// </para>
    /// <para>
    /// So this is a debounce with a backoff and a reconnect. It holds no clock and no
    /// socket: it is handed elapsed time and told whether the network is up, which is
    /// what makes the whole policy runnable in the test suite — see <c>SyncTests</c>.
    /// That is the same bargain <c>RunClock</c> makes, for the same reason.
    /// </para>
    /// </summary>
    public sealed class SyncScheduler
    {
        /// <summary>
        /// How long a change waits before it is sent, so a burst becomes one write.
        ///
        /// Long enough that renaming and then changing companion is a single sync, short
        /// enough that the player has not put the phone down yet. Firestore bills per
        /// document write, so coalescing here is money as well as latency.
        /// </summary>
        public const float DebounceSeconds = 3f;

        /// <summary>The first retry after a failure. Doubles from here.</summary>
        public const float FirstRetrySeconds = 5f;

        /// <summary>
        /// The longest gap between retries. Five minutes: far enough apart to be free on
        /// a battery, near enough that a player who has been in a tunnel is up to date
        /// before they notice. A device is not left broken at this bound — a foreground,
        /// a background or the network returning all reset it.
        /// </summary>
        public const float MaxRetrySeconds = 300f;

        /// <summary>
        /// The pause after the network returns, before the first attempt.
        ///
        /// <c>NetworkReachability</c> flips the moment an interface is up, which is
        /// somewhat before it carries traffic. Attempting on that exact frame mostly buys
        /// one guaranteed failure and a backoff nobody needed.
        /// </summary>
        public const float ReconnectSeconds = 1f;

        bool _wanted;
        bool _inFlight;
        bool _reachable = true;
        float _wait;
        float _backoff;

        /// <summary>True when something is waiting to reach the server.</summary>
        public bool HasWork => _wanted || _inFlight;

        /// <summary>Seconds until the next attempt. For diagnostics only.</summary>
        public float SecondsUntilAttempt => _wait;

        /// <summary>
        /// Something local changed and the server has not heard about it.
        ///
        /// Safe to call as often as anything likes: a request during the debounce
        /// restarts it, and one made while a sync is in flight survives that sync
        /// finishing, because the snapshot it pushed was taken before the change.
        /// </summary>
        public void Request()
        {
            _wanted = true;

            // A backoff is not shortened by asking again. The server is failing or the
            // network is down, and neither is fixed by trying harder — the reconnect
            // below is what resets it, because that is genuinely new information.
            if (_backoff <= 0f) _wait = DebounceSeconds;
        }

        /// <summary>
        /// The device's connectivity, polled by the caller each frame.
        ///
        /// Regaining it schedules a sync whether or not anything local is pending: the
        /// point of coming back online is as much what the <em>other</em> device did while
        /// this one was away. It costs a document read and, if the two already agree, no
        /// write at all — <c>SaveDelta</c> makes that the cheap case.
        /// </summary>
        public void NetworkChanged(bool reachable)
        {
            bool regained = reachable && !_reachable;
            _reachable = reachable;

            if (!regained) return;

            _backoff = 0f;
            _wanted = true;
            _wait = ReconnectSeconds;
        }

        /// <summary>
        /// A sync has begun, so everything asked for up to now is about to be sent.
        ///
        /// <para>
        /// This is why <see cref="Request"/> is safe at any moment. The pending mark is
        /// consumed <em>when the snapshot is taken</em>, not when the push comes back, so
        /// a change made while the push is in flight leaves a fresh request behind rather
        /// than being cleared by the success of a sync that never carried it. That is the
        /// difference between a rename being lost by one unlucky second and not.
        /// </para>
        /// </summary>
        public void Started()
        {
            _wanted = false;
            _inFlight = true;
            _wait = 0f;
        }

        /// <summary>Clears the backoff without asking for a sync — a foreground, say.</summary>
        public void Settled()
        {
            _backoff = 0f;
            if (_wait > DebounceSeconds) _wait = DebounceSeconds;
        }

        /// <summary>The sync reached the server. Nothing is owed until something changes.</summary>
        public void Succeeded()
        {
            _inFlight = false;
            _backoff = 0f;
            _wait = _wanted ? DebounceSeconds : 0f;
        }

        /// <summary>
        /// The sync did not reach the server, so the work is still owed and the next
        /// attempt is further away than the last.
        /// </summary>
        public void Failed()
        {
            _inFlight = false;
            _wanted = true;

            _backoff = _backoff <= 0f ? FirstRetrySeconds : _backoff * 2f;
            if (_backoff > MaxRetrySeconds) _backoff = MaxRetrySeconds;

            _wait = _backoff;
        }

        /// <summary>
        /// Advances the policy by <paramref name="deltaSeconds"/> and answers whether a
        /// sync should start now.
        ///
        /// <para>
        /// Returns true once per attempt and then waits to be told the outcome, so a slow
        /// sync cannot be started twice — the service's own latch would refuse the second
        /// anyway, and a refusal recorded as a failure would back the timer off for a
        /// reason that was never real.
        /// </para>
        /// <para>
        /// The timer does not run while the network is down. A debounce counted out in a
        /// tunnel would arrive at zero the moment the player surfaced and fire before
        /// <see cref="NetworkChanged"/> had settled, which is the one attempt certain to
        /// fail.
        /// </para>
        /// </summary>
        public bool Tick(float deltaSeconds)
        {
            if (_inFlight || !_wanted || !_reachable) return false;

            if (deltaSeconds > 0f) _wait -= deltaSeconds;
            if (_wait > 0f) return false;

            Started();
            return true;
        }
    }
}
