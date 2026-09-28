using System;
using System.Collections.Generic;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// When a rewarded video's currency lands on the server, bring it onto the screen.
    ///
    /// <para>
    /// A coin or wheel reward is granted by the ad network's callback to our server
    /// (invariant 10d), which arrives a few seconds <em>after</em> the video closes. The sync
    /// the client starts at that moment therefore reads the balance too early, and the coins
    /// sat on the server until the next sync - a player watched an ad and saw nothing until
    /// they restarted the game (reported by the owner on 2026-09-28).
    /// </para>
    /// <para>
    /// So a view that pays currency <b>arms</b> this: the wallet document is watched with a
    /// listener (one read per server write, no polling), each change asks for the balances
    /// once, and the watch disarms itself the moment every currency it expects has risen above
    /// the figure it was armed at - or after <see cref="WindowSeconds"/>, or on an account
    /// switch. With no listener available it falls back to asking every
    /// <see cref="PollSeconds"/> inside the same window. Either way the ordinary sync is still
    /// the net: a grant this misses arrives on the next one, as it always did.
    /// </para>
    /// <para>
    /// It is a policy with no I/O - the caller does what <see cref="Next"/> says and reports
    /// back - so the lifetime is testable without a server (44q's reason). <see cref="Signal"/>
    /// is the one member safe from any thread, because the Firestore SDK does not say which
    /// thread a listener is called on.
    /// </para>
    /// </summary>
    public sealed class AdGrantWatch
    {
        /// <summary>How long a watch stays up waiting for a grant. LevelPlay retries a failed
        /// callback for a day, but a first delivery is seconds; past this the sync is the net.</summary>
        public const double WindowSeconds = 90;

        /// <summary>The fallback cadence when no listener could be attached.</summary>
        public const double PollSeconds = 6;

        public enum Step { None, Attach, Refresh, Detach }

        readonly Dictionary<string, long> _baselines = new Dictionary<string, long>();

        string _uid;
        double _deadline;
        double _nextPoll;
        bool _armed;
        bool _attachAsked;
        bool _attached;
        bool _refreshing;
        volatile bool _signalled;

        /// <summary>Whether a grant is still being waited for.</summary>
        public bool Armed => _armed;

        /// <summary>
        /// A view paid in <paramref name="currency"/>; wait for the server's baseline to rise
        /// above <paramref name="grantedNow"/>. A second view inside the window extends it and
        /// keeps the earlier baseline, so two quick views wait for the first grant's figure
        /// rather than forgetting it.
        /// </summary>
        public void Expect(string currency, long grantedNow, string uid, double now)
        {
            if (string.IsNullOrEmpty(currency) || string.IsNullOrEmpty(uid)) return;

            if (_armed && uid != _uid) Disarm();

            if (!_baselines.ContainsKey(currency)) _baselines[currency] = grantedNow;

            _uid = uid;
            _armed = true;
            _deadline = now + WindowSeconds;
            _nextPoll = now + PollSeconds;

            // Ask once straight away: the callback may already have landed before the
            // listener attaches, and a listener only reports changes from here on.
            _signalled = true;
        }

        /// <summary>The wallet document changed. Safe from any thread.</summary>
        public void Signal() => _signalled = true;

        /// <summary>
        /// What to do now. Called every frame from the main thread with the signed-in account
        /// and a reader for the local copy of each ledger's server baseline.
        /// </summary>
        public Step Next(double now, string uid, Func<string, long> granted)
        {
            if (_armed && (uid != _uid || now >= _deadline || Arrived(granted))) Disarm();

            if (!_armed)
            {
                if (!_attached) return Step.None;
                _attached = false;
                return Step.Detach;
            }

            if (!_attachAsked)
            {
                _attachAsked = true;
                return Step.Attach;
            }

            if (!_attached && now >= _nextPoll)
            {
                _nextPoll = now + PollSeconds;
                _signalled = true;
            }

            if (_signalled && !_refreshing)
            {
                _signalled = false;
                _refreshing = true;
                return Step.Refresh;
            }

            return Step.None;
        }

        /// <summary>Reports whether <see cref="Step.Attach"/> produced a listener.</summary>
        public void Attached(bool listening) => _attached = listening;

        /// <summary>Reports that a <see cref="Step.Refresh"/> has finished, however it ended.</summary>
        public void Refreshed() => _refreshing = false;

        bool Arrived(Func<string, long> granted)
        {
            if (granted == null || _baselines.Count == 0) return false;

            foreach (var pair in _baselines)
                if (granted(pair.Key) <= pair.Value) return false;

            return true;
        }

        void Disarm()
        {
            _armed = false;
            _attachAsked = false;
            _signalled = false;
            _baselines.Clear();
        }
    }
}
