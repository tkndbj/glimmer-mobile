using System;
using System.Globalization;

namespace GlimmerGrove
{
    /// <summary>
    /// How long a run actually took, and how much of the glade's clock is left.
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
    /// <b>It does not decide when it starts.</b> <see cref="Start"/> is idempotent and the
    /// screen calls it on whatever edge that screen considers the beginning of a run — which
    /// is now the board becoming playable, and used to be the first conduit turned. The
    /// second was right while this was only a record (a player who studies a glade is not
    /// doing worse than one who spins tiles at random) and wrong once it became a limit, at
    /// which point a clock the player can hold at full by not touching anything applies no
    /// pressure at all. Keeping the edge outside this type is what let that change be one
    /// line in <c>PlayScreen</c>.
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
    /// <b>It counts down, and it still measures up.</b> A glade carries a limit
    /// (<see cref="Content.LevelTuning.TimeLimitMillis"/>) and the run is lost when the
    /// clock reaches it. What is <em>stored</em> is still <see cref="Millis"/> — time taken,
    /// not time left — and that is the whole reason the countdown cost no save migration and
    /// no change to the population stats: the record, the map badge and
    /// <c>publishGroveStats</c> were all already reading elapsed play time, and elapsed play
    /// time is what a countdown produces. Remaining is derived for the HUD and nothing else.
    /// A limit of zero is an untimed glade, and everything below behaves exactly as it did
    /// before the limit existed.
    /// </para>
    /// <para>
    /// It holds no Unity types and no statics, so it is testable without a frame and a new
    /// run cannot inherit an old one's time — or an old one's limit.
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

        /// <summary>
        /// The glade's whole clock in milliseconds, or 0 for an untimed one.
        ///
        /// Set through <see cref="Reset"/> rather than a constructor, because the screen owns
        /// a clock before it knows which glade it is showing — and because every path that
        /// hands the player a fresh board already has to call Reset, so the limit cannot be
        /// left behind from the previous level by anyone who remembers the board but forgets
        /// the clock.
        /// </summary>
        public int LimitMillis { get; private set; }

        public bool HasLimit => LimitMillis > 0;

        /// <summary>True once the screen has declared the run under way.</summary>
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
        /// Begins timing. Idempotent, which is what lets the caller poll for its start edge
        /// instead of subscribing to an event — one fewer subscription to unwind, and a poll
        /// cannot miss the edge it is looking for or catch it twice.
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

            // Held exactly at the limit rather than allowed past it, so a run that times out
            // reads 0:00 remaining and not -0:01, and so Millis on an expired clock is a
            // number the glade could actually produce. Costs nothing on an untimed glade.
            if (HasLimit)
            {
                float limit = LimitMillis / 1000f;
                if (_elapsed > limit) _elapsed = limit;
            }
        }

        /// <summary>
        /// Milliseconds left on the clock, floored at zero. Zero on an untimed glade too —
        /// ask <see cref="HasLimit"/> before drawing this.
        /// </summary>
        public int RemainingMillis
        {
            get
            {
                if (!HasLimit) return 0;

                int left = LimitMillis - Millis;
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>
        /// True once the clock has run out on a glade that has one.
        ///
        /// <para>
        /// Gated on <see cref="HasStarted"/>, which is not redundant: the clock does not run
        /// until the screen says the run is under way, so a board still flying in is not on a
        /// countdown yet. Without the guard an untouched board with a limit already
        /// satisfies <c>Millis &gt;= LimitMillis</c> at zero and the run would be lost on the
        /// frame it appeared.
        /// </para>
        /// </summary>
        public bool Expired => HasLimit && HasStarted && Millis >= LimitMillis;

        /// <summary>
        /// Seconds bought back on this run, in milliseconds. Zero on a run nobody extended.
        ///
        /// Kept separately from <see cref="LimitMillis"/> rather than derived from it,
        /// because the level's own limit is a fact about the glade that anything may read,
        /// while this is a fact about one attempt. Analytics wants the split and so does the
        /// defeat panel: a run that ran out of ninety-eight seconds and one that ran out of
        /// ninety-eight plus four extensions are not the same story.
        /// </summary>
        public int ExtendedMillis { get; private set; }

        /// <summary>True once this run has been extended at least once.</summary>
        public bool WasExtended => ExtendedMillis > 0;

        /// <summary>
        /// Buys more clock. Returns false when there is nothing to buy it on.
        ///
        /// <para>
        /// <b>It raises the limit and never lowers the elapsed time</b>, and that one choice
        /// is what keeps the whole feature free of consequences. What this class measures and
        /// what the save file stores is <see cref="Millis"/> — time <em>taken</em> — so a
        /// continued run reports the truth: the player really did spend that long on the
        /// glade. Rewinding the elapsed instead would have been the same number of lines and
        /// would have silently corrupted three things at once — <c>bestMillis</c> would record
        /// a time nobody played, <c>StarsForTime</c> would hand out gold for a run that took
        /// three times the limit, and <c>publishGroveStats</c> would fold both into a
        /// population every other player is ranked against.
        /// </para>
        /// <para>
        /// So the grading is untouched by design rather than by care at each call site:
        /// <see cref="Content.LevelTuning.StarsFor"/> compares elapsed against thresholds
        /// derived from <em>par</em>, not against this clock's limit, so every extension pushes
        /// the run further down the time bands. A player who buys their way through a glade
        /// keeps the clear and loses the stars, which needs no separate rule to enforce and no
        /// number anywhere that says so.
        /// </para>
        /// <para>
        /// Refused on an untimed glade (there is no limit to raise), before the run has begun
        /// (nothing has been spent yet, and a pre-loaded clock would be a strictly better
        /// glade bought before it was needed) and after it has resolved (the reading is frozen
        /// and the run is over). Each of those is a caller mistake rather than a player state,
        /// which is why this reports rather than throws — an ad has already been watched by
        /// the time anybody asks, and refusing loudly would cost the player the reward.
        /// </para>
        /// </summary>
        /// <param name="millis">How much to add. Non-positive values are refused.</param>
        public bool Extend(int millis)
        {
            if (millis <= 0) return false;
            if (!HasLimit || !HasStarted || IsStopped) return false;

            // Bounded so a repeated grant cannot overflow the int the HUD, the record and the
            // remaining-time arithmetic all share. Nothing legitimate comes close — the cap is
            // days — but LimitMillis is added to on every extension and an int that wraps here
            // reads as a run that expired the instant it was extended.
            long raised = (long)LimitMillis + millis;
            if (raised > MaxLimitMillis) return false;

            LimitMillis = (int)raised;
            ExtendedMillis += millis;
            return true;
        }

        /// <summary>
        /// The largest clock this type will carry, extensions included.
        ///
        /// Twenty-four hours. Far past anything a glade or a player can produce, and present
        /// only so <see cref="Extend"/> has something to refuse against — see there.
        /// </summary>
        public const int MaxLimitMillis = 24 * 60 * 60 * 1000;

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
        /// <param name="limitMillis">
        /// The new glade's whole clock, or 0 for an untimed one. Passed on every reset rather
        /// than remembered, so a clock cannot carry the previous level's limit into this one —
        /// the same hazard the elapsed time has, and the reason both are cleared here.
        /// </param>
        public void Reset(int limitMillis = 0)
        {
            _elapsed = 0f;
            HasStarted = false;
            IsStopped = false;
            ExtendedMillis = 0;
            LimitMillis = limitMillis > 0 ? limitMillis : 0;
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
