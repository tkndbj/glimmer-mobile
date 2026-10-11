namespace GlimmerGrove
{
    /// <summary>
    /// The count a continued run is handed back on: a beat of quiet while the line stands up,
    /// then three, two, one, GO - and the run moves on GO (2026-10-11).
    ///
    /// <para>
    /// <b>Why there is one.</b> A siege is continued with the hill exactly where it stood, every
    /// raider that broke the line still at it. A player coming back from the store's sheet or from
    /// a video needs a moment to find the board before it moves again; without one a raised line
    /// facing a full hill was falling before anyone had made a match (six to eight seconds idle on
    /// chapters three to twelve). Gem and video continues alike, because both are
    /// <c>RunContinueFlow</c>'s.
    /// </para>
    /// <para>
    /// <b>The run is held for all of it, and paced by the run's own clock.</b> The caller only
    /// advances this on frames where nothing but the countdown holds the run, so the pause menu or
    /// any panel opened mid-count stops the count with the run - the lesson the start-of-run
    /// count-in learned (<c>SiegeView.CountIn</c>), where a wall clock burned through a
    /// first-timer's tips. And unlike that count-in this one <em>is</em> a pause: nothing walks
    /// until GO, because the point is to look before the hill closes in again.
    /// </para>
    /// <para>
    /// <b>One beat a frame, the newest, and GO is never skipped.</b> A hitch that swallows a step
    /// costs the number it swallowed rather than printing two at once; the last frame always
    /// answers GO, because GO is the beat that says the run is moving. Each frame's step is clamped
    /// like the siege's own (<c>SiegeBoard.Advance</c>), so a resumed app cannot finish the count
    /// in one frame.
    /// </para>
    /// <para>Pure and Unity-free, so every frame of it is proved offline (<c>ResumeCountdownTests</c>).</para>
    /// </summary>
    public sealed class ResumeCountdown
    {
        /// <summary>The numbers counted down before GO.</summary>
        public const int Numbers = 3;

        /// <summary>
        /// Quiet before the first number, in seconds, so the line is seen standing up before it is
        /// counted over (the rally staggers four turrets a fifteenth of a second apart and flashes).
        /// </summary>
        public const float Lead = .35f;

        /// <summary>The most one frame may advance the count, the siege board's own clamp.</summary>
        public const float MostPerFrame = .25f;

        /// <summary>What <see cref="Advance"/> answers for GO.</summary>
        public const int Go = 0;

        /// <summary>What <see cref="Advance"/> answers on a frame that draws nothing.</summary>
        public const int Nothing = -1;

        float _elapsed;
        int _drawn;

        /// <summary>Seconds between beats. Set by <see cref="Start"/>.</summary>
        public float Step { get; private set; }

        /// <summary>True from <see cref="Start"/> until GO, or until cancelled.</summary>
        public bool Active { get; private set; }

        /// <summary>The whole count, lead to GO, in seconds of run.</summary>
        public float Length => Lead + Numbers * Step;

        /// <summary>
        /// Begins a count with <paramref name="step"/> seconds a beat. Nought or less starts
        /// nothing, which is how a mode with no count says so. Starting again restarts.
        /// </summary>
        public void Start(float step)
        {
            _elapsed = 0f;
            _drawn = 0;
            Step = step > 0f ? step : 0f;
            Active = Step > 0f;
        }

        /// <summary>Stops the count where it is. Nothing more is drawn.</summary>
        public void Cancel() => Active = false;

        /// <summary>
        /// Moves the count on by one frame of run. Answers the beat to draw now -
        /// <see cref="Numbers"/> down to 1, then <see cref="Go"/> - or <see cref="Nothing"/>. The
        /// frame that answers GO is the frame <see cref="Active"/> goes false and the run may move.
        /// </summary>
        public int Advance(float dt)
        {
            if (!Active) return Nothing;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return Nothing;
            if (dt > MostPerFrame) dt = MostPerFrame;

            _elapsed += dt;

            int owed = Owed(_elapsed, Step);
            if (_elapsed >= Length)
            {
                Active = false;
                owed = Numbers + 1;
            }

            if (owed <= _drawn) return Nothing;

            _drawn = owed;
            return Numbers + 1 - owed;
        }

        /// <summary>
        /// How many beats are owed after <paramref name="elapsed"/> seconds: none during the lead,
        /// then one a step, GO being the last.
        /// </summary>
        public static int Owed(float elapsed, float step)
        {
            if (step <= 0f) return Numbers + 1;

            float counted = elapsed - Lead;
            if (counted < 0f) return 0;

            int owed = (int)(counted / step) + 1;
            return owed < 1 ? 1 : owed > Numbers + 1 ? Numbers + 1 : owed;
        }
    }
}
