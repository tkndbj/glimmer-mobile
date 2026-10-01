namespace GlimmerGrove
{
    /// <summary>
    /// A raider's walk down a challenge lane: one continuous motion toward the last seat the
    /// rules gave it, retargetable at any instant, and <b>the only thing that decides where a
    /// raider's widget is drawn</b> (<see cref="ChallengeHillView"/>).
    ///
    /// <para>
    /// <b>Why a spring rather than a tween per step.</b> The hill is turn-based and queued:
    /// a player moving faster than it replays hands it a second step while the first is still
    /// in flight, and the ending's repaint reads the model, which is already where the walk
    /// is going. Drawn as one tween per step, each of those was a second writer of the same
    /// position - a tween superseded mid-flight restarting from rest, a repaint snapping the
    /// widget ahead of a tween whose clock is capped per frame while the coroutine's wait is
    /// not - and the raider was seen to lurch, stall and step back (the owner, 2026-09-30:
    /// "they look like they step back and forth"). A critically damped spring on the
    /// <em>distance</em> carries one position and one speed, takes a new goal in its stride
    /// with the momentum it already has, and can be proved never to overshoot and never to
    /// move away from where it is going, so there is nothing left that could read as a step
    /// back.
    /// </para>
    /// <para>
    /// <b>It holds no Unity types and no statics</b>, for <see cref="TweenCycle"/>'s reason:
    /// motion is invisible to a screenshot and obvious on a device, so this is the arithmetic
    /// the game runs, driven a thousand frames at a time by <c>LaneWalkTests</c>, including
    /// the frames a phone produces and a fixture never would - a long hitch, a retarget in
    /// mid-stride, a burst of steps faster than any could finish.
    /// </para>
    /// <para>
    /// Distances are in <em>steps</em> from the line, the model's own unit, so the rule is the
    /// same on every hill length and every screen; the view turns a distance into a seat.
    /// </para>
    /// </summary>
    public static class LaneWalk
    {
        /// <summary>
        /// The spring's smooth time, in seconds: how quickly a walk answers a new goal. Tied
        /// to <see cref="ChallengeHillView.StepFor"/> by the fixture - a single step from rest
        /// has all but a hair of its stride behind it by the time the hill's replay moves on
        /// to the next beat, so a step reads as a step and a hurried pair reads as one longer
        /// stride rather than as two beginnings.
        /// </summary>
        public const float SmoothTime = .10f;

        /// <summary>
        /// Closer to the goal than this, in steps, and slower than <see cref="SnapSpeed"/>, and
        /// the walk is over: the position is set to the goal exactly and the speed to nought,
        /// so "arrived" is a fact the view can wait on rather than an asymptote it never
        /// reaches. On the shortest lane a step is about thirty units, so this is under half a
        /// unit - below what a screen can draw.
        /// </summary>
        public const float Snap = .012f;

        /// <summary>Steps per second below which a walk inside <see cref="Snap"/> is taken as arrived.</summary>
        public const float SnapSpeed = .3f;

        /// <summary>Whether a walk is standing on its goal.</summary>
        public static bool Arrived(float at, float speed, float goal) => at == goal && speed == 0f;

        /// <summary>
        /// Advance one frame. <paramref name="at"/> is the drawn distance and
        /// <paramref name="speed"/> the walk's own state, both owned by the caller and never
        /// read from anywhere else; <paramref name="goal"/> is where the rules last put the
        /// raider. Returns true once the walk has arrived.
        ///
        /// <para>
        /// Three guarantees, each held by the fixture: the position never crosses the goal;
        /// it never moves away from the goal, whatever the goal did since the last frame; and
        /// a frame of any length - the step is trimmed by <see cref="TweenCycle.Step"/> - leaves
        /// it somewhere between where it was and where it is going.
        /// </para>
        /// </summary>
        public static bool Advance(ref float at, ref float speed, float goal, float dt)
        {
            if (Arrived(at, speed, goal)) return true;

            dt = TweenCycle.Step(dt);
            if (dt <= 0f) return false;

            // A critically damped spring, integrated the way Unity's SmoothDamp integrates it:
            // an exponential approximated by a rational polynomial, stable at any step.
            float omega = 2f / SmoothTime;
            float x = omega * dt;
            float exp = 1f / (1f + x + .48f * x * x + .235f * x * x * x);

            float change = at - goal;
            float temp = (speed + omega * change) * dt;
            speed = (speed - omega * temp) * exp;
            float output = goal + (change + temp) * exp;

            // Never past the goal: a spring integrated in discrete frames can land beyond it
            // on a long step, and beyond it is the one place a walk may never be drawn.
            bool goalAbove = goal > at;
            if (goalAbove == (output > goal) || output == goal)
            {
                output = goal;
                speed = 0f;
            }

            // Never away from the goal: a goal that moved against the walk's momentum would
            // carry it the wrong way for a frame or two before the spring turned it. The hill
            // never raises a goal, but a rule that holds only while its callers behave is a
            // rule with a hole in it, so the walk stands still for that frame instead.
            else if (goalAbove ? output < at : output > at)
            {
                output = at;
                speed = 0f;
            }

            at = output;

            float gap = at > goal ? at - goal : goal - at;
            float pace = speed < 0f ? -speed : speed;
            if (gap <= Snap && pace <= SnapSpeed)
            {
                at = goal;
                speed = 0f;
                return true;
            }

            return false;
        }
    }
}
