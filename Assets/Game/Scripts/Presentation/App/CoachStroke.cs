using System.Collections.Generic;

namespace GlimmerGrove
{
    /// <summary>
    /// One frame of a coaching gesture: where the hand is along the route it is
    /// demonstrating, how hard it is pressing, and how much of the stroke has been inked in
    /// behind it.
    /// </summary>
    public readonly struct CoachBeat
    {
        /// <summary>How far along the route the fingertip is, 0..1, already eased.</summary>
        public readonly float Along;

        /// <summary>The hand's own opacity. Zero between repeats.</summary>
        public readonly float Alpha;

        /// <summary>0 hovering, 1 pressed onto the board. Drives the dip on the tap.</summary>
        public readonly float Press;

        /// <summary>0 on the board, 1 fully raised — the reach in and the lift away.</summary>
        public readonly float Lift;

        /// <summary>How much of the route has been drawn behind the fingertip, 0..1.</summary>
        public readonly float Trail;

        /// <summary>That ink's opacity, so a repeat starts on clean ground.</summary>
        public readonly float TrailAlpha;

        public CoachBeat(float along, float alpha, float press, float lift,
                         float trail, float trailAlpha)
        {
            Along = along;
            Alpha = alpha;
            Press = press;
            Lift = lift;
            Trail = trail;
            TrailAlpha = trailAlpha;
        }
    }

    /// <summary>
    /// The timing of a hand shown drawing something, for the two lessons a Lightweave board
    /// cannot demonstrate on its own.
    ///
    /// <para>
    /// <b>Why a hand at all.</b> Every other lesson in this game is about a rule, and a
    /// sentence with a ring around the tile it is talking about is the right shape for a
    /// rule. Lightweave's first lesson is not a rule, it is a <em>verb</em>: after four
    /// chapters of tapping tiles, the one thing a player must know before anything else is
    /// that this mode is dragged. Words are a poor way to teach a gesture — the reader has to
    /// turn "drag from a crystal to the critter wearing its colour" back into a movement, and
    /// a picture of the movement skips that step entirely. The bead lesson is the same
    /// argument one notch weaker: the sentence says a ring must be passed through, and the
    /// hand shows what passing through looks like.
    /// </para>
    /// <para>
    /// <b>In its own type and tested, for <c>TweenCycle</c>'s reason.</b> This is animation
    /// arithmetic, which is the one kind of failure invisible in a screenshot and obvious only
    /// in motion — a hand that arrives before it has faded in, a trail still standing from the
    /// previous repeat, a five-cell demonstration that takes as long as a fifteen-cell one, a
    /// loop whose end does not meet its beginning. None of that can be caught by compiling and
    /// the Editor is usually not running, so the whole cycle is plain floats and
    /// <c>CoachStrokeTests</c> walks it frame by frame.
    /// </para>
    /// <para>
    /// <b>The total is bounded and the rate is what gives way</b>, which is
    /// <c>GroveGrowth.MaxSpread</c>'s rule for the fourth time. A demonstration is time the
    /// player is not playing, and it repeats for as long as they leave the panel up — so a
    /// drop that ships a wider grove must not silently ship a longer wait before the sentence
    /// can be read a second time.
    /// </para>
    /// </summary>
    public static class CoachStroke
    {
        /// <summary>The hand fading in above where it is about to press.</summary>
        public const float ReachSeconds = .38f;

        /// <summary>Settling onto the board. Short: this is a tap, not a decision.</summary>
        public const float PressSeconds = .20f;

        /// <summary>Lifting away once the route is drawn.</summary>
        public const float LiftSeconds = .28f;

        /// <summary>
        /// The beat before it happens again.
        ///
        /// Long enough that a repeat reads as a repeat rather than as a loop the player is
        /// trapped in, and it is where the ink is cleared — a stroke that began on top of the
        /// last one would say the route may be drawn twice.
        /// </summary>
        public const float RestSeconds = .85f;

        /// <summary>How long the ink takes to clear, at the end of the rest.</summary>
        public const float TrailFadeSeconds = .30f;

        /// <summary>Seconds the fingertip spends crossing one cell, before the ceiling.</summary>
        public const float CellSeconds = .105f;

        /// <summary>
        /// The shortest a stroke may take. A two-cell demonstration at the raw rate is a fifth
        /// of a second, which is not a hand travelling — it is the hand being somewhere else.
        /// </summary>
        public const float MinDraw = .52f;

        /// <summary>The longest, however far it has to go. Past this the rate gives way.</summary>
        public const float MaxDraw = 1.45f;

        /// <summary>How long the fingertip takes to cross <paramref name="cells"/> cells.</summary>
        public static float DrawSeconds(int cells)
        {
            if (cells < 1) return MinDraw;

            float raw = cells * CellSeconds;
            if (raw < MinDraw) return MinDraw;
            return raw > MaxDraw ? MaxDraw : raw;
        }

        /// <summary>One repeat, end to end.</summary>
        public static float Cycle(float draw)
            => ReachSeconds + PressSeconds + Clamped(draw) + LiftSeconds + RestSeconds;

        /// <summary>
        /// The gesture <paramref name="t"/> seconds into a repeat.
        ///
        /// <para>
        /// Read rather than scheduled, so the whole demonstration is one looping tween with no
        /// state of its own — a sequence of nested callbacks would have to be cancelled
        /// correctly on every way the panel can close, and this project has already recorded
        /// what a panel with several exits costs.
        /// </para>
        /// </summary>
        public static CoachBeat At(float t, float draw)
        {
            draw = Clamped(draw);

            if (t < 0f) t = 0f;
            float cycle = Cycle(draw);
            if (t > cycle) t = cycle;

            if (t < ReachSeconds)
            {
                float u = Smooth(t / ReachSeconds);
                return new CoachBeat(0f, u, 0f, 1f - u, 0f, 1f);
            }
            t -= ReachSeconds;

            if (t < PressSeconds)
            {
                float u = Smooth(t / PressSeconds);
                return new CoachBeat(0f, 1f, u, 0f, 0f, 1f);
            }
            t -= PressSeconds;

            if (t < draw)
            {
                float u = Smooth(t / draw);
                return new CoachBeat(u, 1f, 1f, 0f, u, 1f);
            }
            t -= draw;

            if (t < LiftSeconds)
            {
                float u = Smooth(t / LiftSeconds);
                return new CoachBeat(1f, 1f - u, 1f - u, u, 1f, 1f);
            }
            t -= LiftSeconds;

            // The rest. The hand is gone; the ink stands for a moment so the route can be
            // read as a whole, then clears.
            float hold = RestSeconds - TrailFadeSeconds;
            float fade = t <= hold ? 0f : Smooth((t - hold) / TrailFadeSeconds);
            return new CoachBeat(1f, 0f, 0f, 1f, 1f, 1f - fade);
        }

        /// <summary>
        /// Which segment of a route <paramref name="along"/> lands on, and how far into it.
        ///
        /// <para>
        /// Measured by length rather than by segment, which is the whole reason it is here:
        /// splitting a stroke evenly between its segments makes the hand crawl across a short
        /// leg and race across a long one, and the bead lesson's route is deliberately two
        /// legs of different lengths.
        /// </para>
        /// </summary>
        /// <returns>False when there is nothing to walk, in which case the route is a point.</returns>
        public static bool Walk(IReadOnlyList<float> lengths, float along, out int segment, out float fraction)
        {
            segment = 0;
            fraction = 0f;

            if (lengths == null || lengths.Count == 0) return false;

            float total = 0f;
            for (int i = 0; i < lengths.Count; i++) total += lengths[i] > 0f ? lengths[i] : 0f;
            if (total <= 0f) return false;

            if (along <= 0f) return true;
            if (along >= 1f)
            {
                segment = lengths.Count - 1;
                fraction = 1f;
                return true;
            }

            float want = along * total, walked = 0f;
            for (int i = 0; i < lengths.Count; i++)
            {
                float len = lengths[i] > 0f ? lengths[i] : 0f;
                if (len <= 0f) continue;

                if (want <= walked + len)
                {
                    segment = i;
                    fraction = (want - walked) / len;
                    return true;
                }
                walked += len;
            }

            // Only reachable on floating-point slop at the very end of the route.
            segment = lengths.Count - 1;
            fraction = 1f;
            return true;
        }

        static float Clamped(float draw)
            => draw < MinDraw ? MinDraw : draw > MaxDraw ? MaxDraw : draw;

        /// <summary>Smoothstep. Eased at both ends, so nothing here starts or stops abruptly.</summary>
        static float Smooth(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }
    }
}
