using System;

namespace GlimmerGrove.Referral
{
    /// <summary>
    /// Owns at most one listener, and the three facts that decide whether it should exist:
    /// somebody is watching, the app is in the foreground, and an account is signed in.
    ///
    /// <para>
    /// <b>It is a class of its own so that the lifetime can be tested without a server.</b>
    /// Written inline in <see cref="ReferralLedger"/> it would have needed a Firestore, a
    /// signed-in account and a save file to exercise even once — which is to say it would
    /// never have been exercised, and "the listener is properly torn down" would have been an
    /// assertion rather than a fact. Here the thing that opens a listener is a function, so a
    /// test hands it one that counts.
    /// </para>
    /// <para>
    /// <b>Every path ends at <see cref="Settle"/>.</b> Holding, releasing, pausing, resuming and
    /// switching account all change one of the three facts and then ask the same question — is
    /// what I have what I should have — so no path has to know about any other, and no
    /// sequence of them can leave a listener running with nobody to hear it. That is the whole
    /// design, and it is why there is no `if` anywhere else that mentions a listener.
    /// </para>
    /// </summary>
    internal sealed class ReferralFeedWatch
    {
        readonly Func<Action<long>, IDisposable> _open;
        readonly Func<string> _account;
        readonly Func<bool> _available;
        readonly Action<long> _moved;

        int _watchers;
        bool _paused;

        IDisposable _live;
        string _liveFor;

        /// <param name="open">Opens a listener, calling back with the feed's counter whenever it is delivered. May answer null.</param>
        /// <param name="account">Who is signed in, or empty.</param>
        /// <param name="available">Whether the feature and the backend exist at all.</param>
        /// <param name="moved">Raised — possibly off the main thread — with the counter the feed now reads.</param>
        public ReferralFeedWatch(Func<Action<long>, IDisposable> open, Func<string> account,
                                 Func<bool> available, Action<long> moved)
        {
            _open = open;
            _account = account;
            _available = available;
            _moved = moved;
        }

        /// <summary>Whether a listener is attached right now.</summary>
        public bool IsWatching => _live != null;

        /// <summary>How many holders are asking for one. For tests and for reading.</summary>
        public int Watchers => _watchers;

        /// <summary>
        /// Asks for a listener until the handle is disposed. Ref-counted, so two holders are
        /// one listener and the last one out turns it off.
        /// </summary>
        public IDisposable Hold()
        {
            _watchers++;
            Settle();
            return new Holder(this);
        }

        /// <summary>
        /// One holder's claim. <b>Disposing twice is a no-op</b>, because a screen being
        /// destroyed and its object being switched off both reach for it, and a count that went
        /// negative would keep the listener down for the rest of the process.
        ///
        /// <para>
        /// The claim is spent by nulling the back-reference; the <c>&gt; 0</c> below is belt and
        /// braces and, on the paths that exist, unreachable. It is kept because the cost of
        /// being wrong about that is a listener that can never be re-attached for the life of
        /// the process, and the cost of keeping it is one comparison — but it is *not* what
        /// makes a second dispose safe, and a mutation test will not move for it.
        /// </para>
        /// </summary>
        sealed class Holder : IDisposable
        {
            ReferralFeedWatch _watch;

            public Holder(ReferralFeedWatch watch) { _watch = watch; }

            public void Dispose()
            {
                var watch = _watch;
                _watch = null;
                if (watch == null) return;

                if (watch._watchers > 0) watch._watchers--;
                watch.Settle();
            }
        }

        /// <summary>The app is going away; the listener goes with it.</summary>
        public void Pause()
        {
            _paused = true;
            Settle();
        }

        /// <summary>
        /// The app is back. Answers whether a listener was re-attached, so the caller knows
        /// whether it has just re-opened a window it was blind through — and can ask once for
        /// what it missed, which no callback will ever tell it.
        /// </summary>
        public bool Resume()
        {
            _paused = false;
            Settle();
            return IsWatching;
        }

        /// <summary>
        /// Whether a listener should exist. Pure, so every combination can be asked directly
        /// rather than arranged.
        /// </summary>
        internal static bool ShouldWatch(int watchers, bool paused, string account, bool available)
            => watchers > 0 && !paused && available && !string.IsNullOrEmpty(account);

        /// <summary>
        /// Makes what is attached match what should be. Idempotent and safe to call from
        /// anywhere, as often as you like.
        /// </summary>
        public void Settle()
        {
            string account = _account?.Invoke() ?? string.Empty;
            bool wanted = ShouldWatch(_watchers, _paused, account, _available?.Invoke() ?? false);

            // Already right, and pointed at the same account. The second half is what makes a
            // switch re-attach rather than keep a stream open against the account just left.
            if (wanted && _live != null && string.Equals(_liveFor, account, StringComparison.Ordinal)) return;

            Close();
            if (!wanted) return;

            _live = _open?.Invoke(_moved);

            // Only remember the account when something really opened, so the field means what
            // its name says. The early return above already refuses to trust it while `_live`
            // is null, so this is not what makes a refused attach try again — the test for that
            // (`ABackendThatCannotWatchIsAskedAgainNextTime`) passes either way. It is here so
            // the two can never come apart if that return is ever loosened.
            _liveFor = _live != null ? account : null;
        }

        /// <summary>Stops whatever is attached, exactly once, and never throws at the caller.</summary>
        void Close()
        {
            var live = _live;
            _live = null;
            _liveFor = null;
            if (live == null) return;

            try { live.Dispose(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>
        /// Drops everything: no holders, not paused, nothing attached. For a teardown that has
        /// to leave no thread running — a test, or an account being deleted.
        /// </summary>
        public void Reset()
        {
            _watchers = 0;
            _paused = false;
            Close();
        }
    }
}
