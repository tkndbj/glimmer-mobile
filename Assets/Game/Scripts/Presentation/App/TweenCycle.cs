namespace GlimmerGrove
{
    /// <summary>
    /// One tween's frame of arithmetic: where a step leaves it, and what phase to hand the
    /// easing.
    ///
    /// <para>
    /// Split out of <see cref="Tween"/> for the reason <c>RunClock</c> is split out of
    /// <c>PlayScreen</c>: it holds no Unity types and no statics, so it can be run a
    /// thousand simulated frames at a time in the test suite without an Editor. That
    /// matters more here than it looks. This is the only piece of the game whose failures
    /// are invisible in a screenshot and obvious in motion — the bug it was extracted to
    /// fix had every <c>Loop(-1, true)</c> in the game snapping back at the end of its
    /// period for over a year, and no compile, no validator and no reviewer's eye had
    /// anything to say about it.
    /// </para>
    /// <para>
    /// It is the arithmetic the game actually runs, not a description of it. Proving a
    /// copy would prove nothing — see the note on <c>ProgressionLedger</c> for what it
    /// costs to keep one rule in two places on purpose, and this one has no reason to be.
    /// </para>
    /// </summary>
    public static class TweenCycle
    {
        /// <summary>
        /// The most any one frame may advance a tween, in seconds.
        ///
        /// <para>
        /// Here for the reason <c>RunClock.MaxTick</c> is, and it is the same fact about the
        /// same clock: <c>Time.deltaTime</c> is capped by <c>maximumDeltaTime</c> and
        /// <c>Time.unscaledDeltaTime</c> — which every tween here runs on — is not. So the
        /// first frame after the app is resumed carries however long the player was away,
        /// and the first frame after a long asset load carries the load.
        /// </para>
        /// <para>
        /// A quarter second is longer than any hitch a running game produces and shorter
        /// than the shortest thing here that loops, so nothing legitimate is slowed by it.
        /// It is a bound on damage rather than a schedule: a tween is not owed the time an
        /// app spent suspended, because nothing was on screen to have missed it.
        /// </para>
        /// </summary>
        public const float MaxStep = .25f;

        /// <summary>
        /// A frame's worth of time, trimmed to something a tween can act on.
        ///
        /// Non-finite and negative values are answered with zero rather than passed on: a
        /// broken frame should cost an animation nothing, and a negative step would walk a
        /// looping tween backwards past its own wrap.
        /// </summary>
        public static float Step(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return 0f;
            return seconds > MaxStep ? MaxStep : seconds;
        }

        /// <summary>Where a tween stands after a step.</summary>
        public struct Frame
        {
            /// <summary>Time into the current cycle, already wrapped.</summary>
            public float Elapsed;

            /// <summary>Cycles still owed. 0 = done looping, -1 = forever.</summary>
            public int Loops;

            /// <summary>What to hand the easing: 0..1, out and back again for a ping-pong.</summary>
            public float Phase;

            /// <summary>True on the frame a non-looping tween reaches its end.</summary>
            public bool Finished;
        }

        /// <summary>
        /// Advances one tween by <paramref name="step"/> seconds.
        ///
        /// <para>
        /// <b>A ping-pong's cycle is two durations long.</b> It runs out over the first and
        /// back over the second, so the half it is in has to be read from where it sits
        /// inside the pair. Wrapping at a single duration — which this used to do — meant
        /// <c>elapsed</c> never reached the second half at all, the return was unreachable,
        /// and every ping-pong in the game was a sawtooth that snapped back at the end of
        /// each period. On the hub that was the backdrop light dropping in one frame every
        /// 3.4 seconds and the feature card's beacon doing the same every 1.5.
        /// </para>
        /// <para>
        /// <b>Every whole cycle a step covered is drained at once</b>, not one of them per
        /// frame. Draining one leaves a large step's surplus sitting in <c>elapsed</c> for
        /// as many frames as it takes to work off, and the phase is read straight off
        /// <c>elapsed</c> — so a single large step used to swing a full cycle <em>per
        /// frame</em> for a good half second afterwards. <see cref="Step"/> bounds what a
        /// frame can hand in and this bounds what a frame can leave behind; both are
        /// wanted, because a clamp cannot help a loop shorter than <see cref="MaxStep"/>.
        /// </para>
        /// </summary>
        /// <param name="elapsed">Time into the current cycle before this step.</param>
        /// <param name="step">Seconds to advance by. Pass it through <see cref="Step"/> first.</param>
        /// <param name="duration">One traverse, in seconds. Assumed positive — <c>Tween.Run</c> floors it.</param>
        /// <param name="loops">Cycles still owed: 0 for a one-shot, -1 for forever.</param>
        /// <param name="pingPong">Whether the tween returns rather than restarting.</param>
        public static Frame Advance(float elapsed, float step, float duration, int loops, bool pingPong)
        {
            if (duration <= 0f) duration = 0.0001f;

            elapsed += step;

            float span = pingPong ? duration * 2f : duration;

            if (loops != 0 && elapsed >= span)
            {
                int cycles = (int)(elapsed / span);

                // Never more cycles than are owed, or a finite loop would be cut short of
                // its last traverse and end wherever the surplus happened to leave it.
                if (loops > 0 && cycles > loops) cycles = loops;

                elapsed -= cycles * span;
                if (loops > 0) loops -= cycles;
            }

            float u = elapsed / duration;
            float raw = u < 0f ? 0f : (u > 1f ? 1f : u);

            float phase = raw;
            if (pingPong && u > 1f) phase = u < 2f ? 2f - u : 0f;

            return new Frame
            {
                Elapsed = elapsed,
                Loops = loops,
                Phase = phase,

                // A looping tween never finishes; it is ended by its owner dying or by
                // running out of loops, and the frame that runs it out is caught by the
                // next call finding loops at zero.
                Finished = loops == 0 && raw >= 1f,
            };
        }
    }
}
