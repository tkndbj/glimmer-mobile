using System;
using System.Collections.Generic;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// Which rewarded ad unit is being loaded, and which are waiting their turn - one load in
    /// flight at a time, in the order they were asked for.
    ///
    /// <para>
    /// <b>Why one at a time.</b> Loading a rewarded video is not a download: the mediation
    /// SDK runs an auction across its adapters and then pre-renders the winning creative into a
    /// web view, on the main thread. Six placements loaded the moment the SDK started meant six
    /// of those landing in the same few frames - the hub lurched on every connected launch (see
    /// <c>LaunchCalm</c>). Spread one after another, each costs the same and none of them is
    /// felt, and the first placement is ready in the time it always was.
    /// </para>
    /// <para>
    /// Pure, so the ordering and the stall rule are provable without the SDK: the provider owns
    /// the clock and the units and asks this what to do next. A load the network never answers
    /// would otherwise block every placement behind it for the whole session, so a load older
    /// than the stall is let go of - the provider's retry backoff asks for it again later.
    /// </para>
    /// </summary>
    public sealed class AdLoadQueue
    {
        /// <summary>How long a load may go unanswered before the queue moves on without it.</summary>
        public const double DefaultStallSeconds = 30;

        readonly List<string> _waiting = new List<string>();
        readonly double _stallSeconds;

        string _inFlight;
        double _startedAt;

        public AdLoadQueue(double stallSeconds = DefaultStallSeconds)
        {
            _stallSeconds = stallSeconds > 0 ? stallSeconds : DefaultStallSeconds;
        }

        /// <summary>The placement whose load is in flight, or null when nothing is.</summary>
        public string InFlight => _inFlight;

        /// <summary>How many placements are waiting behind the one in flight.</summary>
        public int Waiting => _waiting.Count;

        /// <summary>
        /// Asks for a placement to be loaded. Answers false - and changes nothing - for an
        /// empty id, one already waiting, or the one in flight: a load already under way will
        /// answer for itself.
        /// </summary>
        public bool Request(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return false;
            if (string.Equals(_inFlight, placementId, StringComparison.Ordinal)) return false;
            if (_waiting.Contains(placementId)) return false;

            _waiting.Add(placementId);
            return true;
        }

        /// <summary>
        /// Hands out the next load when nothing is in flight. <paramref name="now"/> is the
        /// provider's clock in seconds, remembered so the load can be judged stalled later.
        /// </summary>
        public bool TryStart(double now, out string placementId)
        {
            placementId = null;
            if (_inFlight != null || _waiting.Count == 0) return false;

            placementId = _waiting[0];
            _waiting.RemoveAt(0);
            _inFlight = placementId;
            _startedAt = now;
            return true;
        }

        /// <summary>
        /// The load in flight has answered, loaded or not. Answers false for anything else,
        /// which is what an SDK callback for a placement this queue is not waiting on gets.
        /// </summary>
        public bool Settle(string placementId)
        {
            if (_inFlight == null || !string.Equals(_inFlight, placementId, StringComparison.Ordinal))
                return false;

            _inFlight = null;
            return true;
        }

        /// <summary>
        /// Lets go of a load that has been in flight for the stall or longer, and names it;
        /// null when nothing has stalled. The placement is not re-queued - a stalled load is a
        /// failed one as far as the retry backoff is concerned.
        /// </summary>
        public string Expire(double now)
        {
            if (_inFlight == null || now - _startedAt < _stallSeconds) return null;

            string stalled = _inFlight;
            _inFlight = null;
            return stalled;
        }
    }
}
