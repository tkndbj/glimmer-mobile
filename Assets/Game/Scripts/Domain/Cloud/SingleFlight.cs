using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// One call in flight at a time: whoever asks while it is out is handed the call already
    /// made rather than making a second.
    ///
    /// <para>
    /// <b>Why this exists.</b> Creating an account is "is anybody signed in? no - then sign in",
    /// and the answer to the question stays "no" for as long as the request is on the wire. Two
    /// callers asking in that window each made the request, and the provider minted an account
    /// for each: seventeen pairs of anonymous accounts created within a tenth of a second of
    /// each other were on the live project by 2026-10-02, one of each pair holding the save and
    /// the other holding nothing, or a referral code nobody would ever read. A check followed
    /// by an act is only one decision when nothing else can run between the two, and an
    /// <c>await</c> is where something else runs.
    /// </para>
    /// <para>
    /// <b>A finished call is never handed out again.</b> The next caller after it starts a new
    /// one, which is what lets a failed attempt be retried and a signed-out device sign in
    /// afresh. Nothing is cached here but the fact that a call is out.
    /// </para>
    /// <para>
    /// <b>A call that never comes back is given up on after <c>patience</c>.</b> The SDK this
    /// guards takes no timeout, so a request lost on a dropped connection would otherwise hold
    /// every later caller on a task that cannot finish, for the life of the process. Past the
    /// bound the lost call is treated as lost and the next caller starts another - by which
    /// time everybody who was waiting on the first has stopped waiting (see
    /// <c>CloudCancel.Within</c>, which is given the same figure).
    /// </para>
    /// <para>
    /// In Domain and free of SDK types so the rule can be run offline, for the reason
    /// <see cref="AccountGate"/> is: it guards a failure nothing in the Editor can show.
    /// Locked, although every caller today is on the main thread, because the cost of being
    /// wrong about that is the fault this class was written to remove.
    /// </para>
    /// </summary>
    public sealed class SingleFlight
    {
        readonly object _gate = new object();
        readonly double _patienceSeconds;
        readonly Func<double> _clock;

        Task _inFlight;
        double _startedAt;

        /// <param name="patienceSeconds">How long a call may be out before it is given up on.</param>
        /// <param name="clock">Seconds on a clock that never runs backwards. Defaults to the
        /// process's own stopwatch; a test hands in one it can move.</param>
        public SingleFlight(double patienceSeconds, Func<double> clock = null)
        {
            _patienceSeconds = patienceSeconds;
            _clock = clock ?? Monotonic;
        }

        static double Monotonic() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>Whether a call is out and still being waited for.</summary>
        public bool IsInFlight
        {
            get { lock (_gate) return Joinable(_clock()); }
        }

        /// <summary>
        /// The call in flight, or a new one from <paramref name="start"/> when none is.
        ///
        /// <para>
        /// Never throws: a <paramref name="start"/> that throws before it returns a task is
        /// handed back as a faulted one, so every caller meets a failure the same way - at the
        /// <c>await</c> - whichever of them happened to start the call.
        /// </para>
        /// </summary>
        public Task Run(Func<Task> start)
        {
            if (start == null) return Task.CompletedTask;

            lock (_gate)
            {
                double now = _clock();
                if (Joinable(now)) return _inFlight;

                Task started;
                try { started = start() ?? Task.CompletedTask; }
                catch (Exception e) { started = Task.FromException(e); }

                _inFlight = started;
                _startedAt = now;
                return started;
            }
        }

        bool Joinable(double now)
            => _inFlight != null
            && !_inFlight.IsCompleted
            && now - _startedAt < _patienceSeconds;
    }
}
