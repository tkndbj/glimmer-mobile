using System.Collections.Generic;

namespace GlimmerGrove
{
    /// <summary>
    /// The reasons a run must not be under way yet.
    ///
    /// <para>
    /// <b>It exists because "is the board playable" was a boolean with two writers.</b> The
    /// clock's start edge was polled off <c>BoardView.Locked</c>, and so was a first-timer's
    /// lesson: the tip latched the board, and the intro sweep — scheduled half a second
    /// earlier, from a different object — unlatched it again a moment later. The last writer
    /// won, which was the animation, so the countdown ran behind a modal the player was
    /// reading and a long lesson could cost them the glade. Nothing in a compile, a validator
    /// or a screenshot can see that: both writes are correct, and only their order is wrong.
    /// </para>
    /// <para>
    /// So the reasons are <em>named and counted separately</em> rather than folded into one
    /// flag. Two things holding a run for two reasons release independently and in any order,
    /// and neither can cancel the other by writing <c>false</c> over it. A latch that says
    /// what is holding it is also the only kind that can be read in a log.
    /// </para>
    /// <para>
    /// <b>Idempotent both ways</b>, which is the same bargain <see cref="Persistence.SaveMerge"/>
    /// makes and for the same reason: a counter would need every take to be paired with exactly
    /// one release, and the one caller that releases twice — a panel with several exits, this
    /// project's oldest bug — would free a run that is still being taught. Taking a reason
    /// already held changes nothing and releasing one that is not held changes nothing, so no
    /// call site has to remember what it did last.
    /// </para>
    /// <para>
    /// Note which way it fails. A leaked hold leaves the clock stopped: the player keeps a
    /// glade they can still finish and cannot lose on time, which costs an economy nothing
    /// (stars are graded against par, and a run nobody ends pays nothing). A missing hold
    /// spends a real player's countdown while they read. Between the two, holding is the safe
    /// direction, and it is why <see cref="Opening"/> is taken at construction rather than
    /// switched on by whoever remembers to.
    /// </para>
    /// <para>
    /// Holds no Unity types, so the arithmetic is provable without an Editor.
    /// </para>
    /// </summary>
    public sealed class RunHold
    {
        /// <summary>
        /// The screen has been built but not yet presented.
        ///
        /// Held from construction and released by <c>RunScreen.OnPresented</c>, so a run
        /// cannot begin during the transition that is still hiding it — which is time the
        /// player has not been shown the board for, and on a mode whose board is built from
        /// a coroutine it is time they may not even have a board for.
        /// </summary>
        public const string Opening = "opening";

        /// <summary>
        /// A first-timer's lesson is pending or on screen.
        ///
        /// Covers the whole sequence rather than each modal: the beat before the first one
        /// appears, every tip in the queue, and the gap between two of them. A run held only
        /// while a panel exists would start ticking in those gaps.
        /// </summary>
        public const string Teaching = "teaching";

        /// <summary>
        /// Small enough that a list beats a set on every count that matters here — no
        /// hashing, no allocation on the first take, and it keeps the order for a log line.
        /// </summary>
        readonly List<string> _reasons = new List<string>(2);

        public RunHold(params string[] reasons)
        {
            if (reasons == null) return;
            for (int i = 0; i < reasons.Length; i++) Take(reasons[i]);
        }

        /// <summary>True while anything at all is holding the run back.</summary>
        public bool Held => _reasons.Count > 0;

        /// <summary>How many distinct reasons are held. For tests and logs.</summary>
        public int Count => _reasons.Count;

        public bool Holds(string reason) => !string.IsNullOrEmpty(reason) && _reasons.Contains(reason);

        /// <summary>Holds the run for a reason. Doing it twice is the same as doing it once.</summary>
        public void Take(string reason)
        {
            if (string.IsNullOrEmpty(reason) || _reasons.Contains(reason)) return;
            _reasons.Add(reason);
        }

        /// <summary>
        /// Lets one reason go. Returns whether it was being held, so a caller that wants to
        /// assert can — nobody has to.
        /// </summary>
        public bool Release(string reason)
            => !string.IsNullOrEmpty(reason) && _reasons.Remove(reason);

        /// <summary>
        /// Lets everything go.
        ///
        /// For a run being handed back wholesale — a retry after defeat, say — and never as a
        /// tidy-up for a release somebody forgot: that would put back exactly the bug this
        /// type exists to remove.
        /// </summary>
        public void ReleaseAll() => _reasons.Clear();

        /// <summary>What is holding the run, for a log line.</summary>
        public override string ToString()
            => _reasons.Count == 0 ? "free" : string.Join(", ", _reasons.ToArray());
    }
}
