using System;
using System.Globalization;

namespace GlimmerGrove
{
    /// <summary>
    /// How long a run actually took, measured from the first conduit turned.
    ///
    /// <para>
    /// <b>An accumulator, never two readings of a wall clock.</b> The obvious
    /// implementation stores a start time and subtracts it at the end, and on a phone it
    /// is wrong within a day of shipping: a player who takes a call, gets a notification
    /// or simply locks the screen comes back to a record of forty minutes on a glade they
    /// solved in two. Nothing about elapsed real time describes what a player did. Time is
    /// therefore handed in a frame at a time by whoever knows the board is playable, and a
    /// suspended app hands in nothing because it is not running.
    /// </para>
    /// <para>
    /// <b>Every tick is clamped</b> (<see cref="MaxTick"/>). A resume, a long asset load,
    /// a rewarded video, a breakpoint in the Editor — each arrives as one enormous
    /// <c>deltaTime</c>, and one of those would put a nonsense record in the save file
    /// permanently, because a best time only ever goes down. The clamp costs a real player
    /// at most a quarter second per stutter and makes the failure impossible.
    /// </para>
    /// <para>
    /// <b>From the first turn, not from the board appearing.</b> A player who opens a
    /// glade and studies it is not doing worse than one who spins tiles at random, and a
    /// clock that starts on entry punishes exactly the behaviour this game is about. It
    /// also means the number is honest about what it measures, which matters because it is
    /// shown next to a move count that has always meant "turns you took".
    /// </para>
    /// <para>
    /// <b>Milliseconds, and never zero once started.</b> The save file needs an "absent"
    /// value that no real run can produce, exactly as
    /// <see cref="Social.LevelStats.MinRank"/> does for a standing — so a glade cleared
    /// before this existed reads as untimed rather than as instant. Seconds would not do:
    /// a one-turn tutorial board can genuinely be finished inside a second, and that would
    /// round to the sentinel.
    /// </para>
    /// <para>
    /// It holds no Unity types and no statics, so it is testable without a frame and a new
    /// run cannot inherit an old one's time.
    /// </para>
    /// </summary>
    public sealed class RunClock
    {
        /// <summary>
        /// The most any single tick may contribute, in seconds.
        ///
        /// A quarter second is four frames at 15fps — comfortably longer than any hitch a
        /// running game produces, and far shorter than the pauses that would corrupt a
        /// record. See the type summary.
        /// </summary>
        public const float MaxTick = .25f;

        float _elapsed;

        /// <summary>True once the first turn has been taken.</summary>
        public bool HasStarted { get; private set; }

        /// <summary>True once the run has resolved. No further time is accepted.</summary>
        public bool IsStopped { get; private set; }

        public float Elapsed => _elapsed;

        /// <summary>
        /// Elapsed milliseconds, or 0 when the clock never started.
        ///
        /// Floored at 1 once started, so 0 unambiguously means "never timed" everywhere
        /// this is stored or read.
        /// </summary>
        public int Millis
        {
            get
            {
                if (!HasStarted) return 0;

                int rounded = (int)(_elapsed * 1000f + .5f);
                return rounded < 1 ? 1 : rounded;
            }
        }

        /// <summary>
        /// Begins timing. Idempotent, which is what lets the caller poll the move count
        /// instead of subscribing to a turn event — one fewer subscription to unwind, and
        /// a poll cannot miss the edge it is looking for or catch it twice.
        /// </summary>
        public void Start()
        {
            if (HasStarted) return;
            HasStarted = true;
        }

        /// <summary>
        /// Adds a frame's worth of play. Ignored before <see cref="Start"/>, after
        /// <see cref="Stop"/>, and for any non-finite or negative value a broken frame
        /// might produce.
        /// </summary>
        public void Advance(float seconds)
        {
            if (!HasStarted || IsStopped) return;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;

            _elapsed += seconds > MaxTick ? MaxTick : seconds;
        }

        /// <summary>
        /// Freezes the reading. Called where a run resolves, so nothing that happens
        /// afterwards — a celebration, an overlay, a stray frame — can move a number that
        /// is about to be written to the save file.
        /// </summary>
        public void Stop() => IsStopped = true;

        /// <summary>
        /// Back to never-started. Every path that gives the player a fresh board has to
        /// call this: a restart, a retry after defeat, or the same screen being handed a
        /// different glade. A clock that survived one of those would hand the next run the
        /// previous run's time, and a best time only ever goes down — so it would stick.
        /// </summary>
        public void Reset()
        {
            _elapsed = 0f;
            HasStarted = false;
            IsStopped = false;
        }

        /// <summary>
        /// <c>M:SS</c>, or <c>H:MM:SS</c> once a run passes an hour.
        ///
        /// <para>
        /// Invariant culture, deliberately. This is a stopwatch reading rather than prose —
        /// the colon is not a decimal separator and the digits must not become Arabic-Indic
        /// on an Arabic device while the move count beside them stays Western. Minutes are
        /// not capped, so an hour-long run reads <c>1:04:12</c> rather than silently
        /// wrapping to something shorter than the truth.
        /// </para>
        /// </summary>
        public static string Format(int millis)
        {
            if (millis <= 0) return "0:00";

            int total = millis / 1000;
            int hours = total / 3600;
            int minutes = total / 60 % 60;
            int seconds = total % 60;

            var culture = CultureInfo.InvariantCulture;

            return hours > 0
                ? string.Concat(hours.ToString(culture), ":", minutes.ToString("00", culture),
                                ":", seconds.ToString("00", culture))
                : string.Concat((total / 60).ToString(culture), ":", seconds.ToString("00", culture));
        }

        /// <summary>The better of two recorded times, where 0 means "never timed".</summary>
        public static int Better(int a, int b)
        {
            if (a <= 0) return b < 0 ? 0 : b;
            if (b <= 0) return a;
            return Math.Min(a, b);
        }
    }
}
