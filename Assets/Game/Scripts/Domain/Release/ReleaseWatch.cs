using UnityEngine;

namespace GlimmerGrove.Release
{
    /// <summary>
    /// Decides <em>when</em> to ask the deployment what it requires. Knows nothing about how to
    /// ask, and nothing about what the answer means.
    ///
    /// <para>
    /// The same bargain <c>SyncScheduler</c> makes and for the same reason: it holds no clock
    /// and no socket, it is handed elapsed time and told whether the network is up, so the whole
    /// policy is runnable in the test suite. What differs is that nothing is ever waiting on
    /// this — there is no local work owed to a server — so there is no debounce and no
    /// exponential backoff, only a cadence.
    /// </para>
    /// <para>
    /// <b>The cadence is the cost.</b> One published document read per attempt, per player.
    /// Asking every frame, or on every foreground, would be a per-player-per-day bill that grows
    /// with how twitchy somebody's phone is rather than with how often they play. Asking only at
    /// launch would leave a device that was in flight mode when it started running unwalled
    /// until the next cold start — which, on a phone, can be a fortnight.
    /// </para>
    /// <para>
    /// <b>The resume check is the one that earns its keep.</b> A launch with no signal learns
    /// nothing; a player who reaches a network and brings the game back is the first moment the
    /// answer can arrive, and it is also the moment a player who has just updated comes back
    /// having <em>not</em> updated. <see cref="ResumeGapSeconds"/> is what stops forty
    /// app-switches in a minute becoming forty reads.
    /// </para>
    /// </summary>
    public sealed class ReleaseWatch
    {
        /// <summary>
        /// The gap after an answer that changed nothing. Fifteen minutes: far enough apart to be
        /// free, near enough that a release forced while somebody is mid-session reaches them
        /// inside one sitting.
        /// </summary>
        public const float RecheckSeconds = 900f;

        /// <summary>
        /// The gap after a failure, and the gap while a wall is standing.
        ///
        /// <para>
        /// A failure is the state in which this device might be running something it should not
        /// be, and a standing wall is the state in which it might be walled out of a game it is
        /// entitled to play — a rolled-back requirement reaches a stuck player through this
        /// number and nothing else. Both are worth a minute rather than a quarter of an hour,
        /// and neither is competing with anything: a failed read costs nothing and a walled
        /// device has no gameplay to interrupt.
        /// </para>
        /// </summary>
        public const float RetrySeconds = 60f;

        /// <summary>
        /// The pause after the app comes back before the first attempt, and after the network
        /// returns.
        ///
        /// <c>NetworkReachability</c> flips when an interface comes up, which is somewhat before
        /// it carries traffic, and a foregrounded app is busy for a moment with things the player
        /// can see. Two seconds buys both and costs nothing anybody notices.
        /// </summary>
        public const float ResumeSeconds = 2f;

        /// <summary>
        /// How long since the last attempt before coming back to the app is worth another one.
        ///
        /// Without it, a player flicking between this game and a messaging app pays a read per
        /// flick. Two minutes is comfortably shorter than any human update cycle and comfortably
        /// longer than an app switch.
        /// </summary>
        public const float ResumeGapSeconds = 120f;

        bool _inFlight;
        bool _reachable = true;
        float _wait;
        float _sinceAttempt = ResumeGapSeconds;

        /// <summary>Seconds until the next attempt. For diagnostics and for the fixtures.</summary>
        public float SecondsUntilAttempt => _wait;

        /// <summary>True while a read is out.</summary>
        public bool InFlight => _inFlight;

        /// <summary>
        /// One frame. True means start a read now — and the claim is taken as it is answered, so
        /// a caller that ignores the answer simply does not check this time round.
        /// </summary>
        public bool Tick(float elapsed, bool reachable)
        {
            if (elapsed > 0f && _sinceAttempt < ResumeGapSeconds) _sinceAttempt += elapsed;

            NetworkChanged(reachable);

            if (_inFlight || !_reachable) return false;

            _wait -= elapsed;
            if (_wait > 0f) return false;

            return Claim();
        }

        /// <summary>
        /// Takes the in-flight claim, or refuses because one is already out.
        ///
        /// <para>
        /// Public because the launch does not wait for a tick: the splash starts a check beside
        /// everything else it fires and forgets, and this is what keeps that from racing the
        /// first frame's tick into two reads on every single launch.
        /// </para>
        /// </summary>
        public bool Claim()
        {
            if (_inFlight) return false;
            _inFlight = true;
            _wait = RetrySeconds;
            _sinceAttempt = 0f;
            return true;
        }

        /// <summary>
        /// A read came back. <paramref name="ok"/> is whether the deployment actually answered;
        /// <paramref name="urgent"/> asks for the short cadence — see <see cref="RetrySeconds"/>.
        /// </summary>
        public void Answered(bool ok, bool urgent = false)
        {
            _inFlight = false;
            _wait = ok && !urgent ? RecheckSeconds : RetrySeconds;
        }

        /// <summary>
        /// The app came back to the foreground.
        ///
        /// Brings the next attempt forward, unless one has been made recently enough that the
        /// answer cannot plausibly have moved.
        /// </summary>
        public void Resumed()
        {
            if (_sinceAttempt < ResumeGapSeconds) return;
            _wait = Mathf.Min(_wait, ResumeSeconds);
        }

        /// <summary>
        /// The device's connectivity, polled by the caller each frame.
        ///
        /// Regaining it brings the next attempt forward for the reason a resume does, and it is
        /// the more important half: a launch in a tunnel is answered by this rather than by
        /// anything the player does.
        /// </summary>
        public void NetworkChanged(bool reachable)
        {
            bool regained = reachable && !_reachable;
            _reachable = reachable;

            if (regained) _wait = Mathf.Min(_wait, ResumeSeconds);
        }
    }
}
