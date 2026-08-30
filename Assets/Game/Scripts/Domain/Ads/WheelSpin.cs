using System;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// How a wheel gets from standing still to resting on a slice, expressed as arithmetic
    /// rather than as a tween.
    ///
    /// <para>
    /// <b>Why this is in Domain and has a test.</b> It is the house rule every timing rule in
    /// this game follows — <c>Cue</c>, <c>GroveGrowth</c>, <c>RippleTempo</c>, <c>CoachStroke</c>
    /// — and it earns it more than most, because the one thing a wheel must never do is stop
    /// somewhere other than where it said it would. That is not a feel question: the slice is
    /// what the server is granting, so a wheel resting half a degree into its neighbour is the
    /// panel disagreeing with the payout. Motion is the one subsystem whose failures show up
    /// only in play, so the arithmetic has to be reachable without an Editor.
    /// </para>
    /// <para>
    /// <b>The rate gives way, not the duration.</b> A twelve-slice wheel does not take longer
    /// to stop than a four-slice one; it turns faster. Anything else means retuning the table
    /// silently retunes how long a player waits, and the wait is the part they feel.
    /// </para>
    /// <para>
    /// Angles are Unity's: degrees, counter-clockwise positive, zero at twelve o'clock. The
    /// pointer is fixed at the top and the wheel turns under it, so a slice's resting rotation
    /// is simply the angle that brings its own centre back to zero. The travel is
    /// <em>negative</em> — the wheel turns clockwise, which is the direction every wheel a
    /// player has ever seen turns.
    /// </para>
    /// </summary>
    public static class WheelSpin
    {
        /// <summary>
        /// Whole revolutions before the landing arc. Enough to read as a spin rather than a
        /// nudge, and few enough that the tail is still watchable at the end.
        /// </summary>
        public const int Turns = 5;

        /// <summary>
        /// How long the spin takes, wherever it lands and however many slices there are.
        ///
        /// Three and a bit seconds is the shortest a spin can be and still have a tail worth
        /// watching. It is deliberately not tuned per slice count — see the type summary.
        /// </summary>
        public const float Seconds = 3.4f;

        /// <summary>
        /// The little backwards wind-up before the wheel goes, and how long it takes.
        ///
        /// It is not decoration. A wheel that leaves from a standstill at full speed reads as
        /// a jump cut; the same movement with eight degrees of load behind it reads as
        /// something being released, which is the whole gesture the player pressed a button
        /// for. Small enough that it can never carry a neighbouring slice under the pointer.
        /// </summary>
        public const float WindUpDegrees = 9f, WindUpSeconds = .24f;

        /// <summary>How long the pointer's kick lasts each time a peg goes past.</summary>
        public const float TickSeconds = .09f;

        /// <summary>
        /// The rotation that brings slice <paramref name="index"/>'s own centre under the
        /// pointer, in <c>[0, 360)</c>.
        ///
        /// Derived from the halves rather than from a centre offset added afterwards, so the
        /// arithmetic is exact wherever the slice count divides 720 — which every shipped
        /// count does.
        /// </summary>
        public static float Rest(int count, int index)
        {
            if (count <= 0) return 0f;

            int wrapped = ((index % count) + count) % count;
            return (2 * wrapped + 1) * 180f / count;
        }

        /// <summary>
        /// Where the spin ends: clockwise through <see cref="Turns"/> revolutions and on to the
        /// slice. Negative, because clockwise is negative, and never zero-length — a landing on
        /// the first slice still turns five times round.
        /// </summary>
        public static float Target(int count, int index)
            => Rest(count, index) - 360f * (Turns + 1);

        /// <summary>
        /// The wheel's rotation a fraction <paramref name="t"/> of the way through the spin.
        ///
        /// <para>
        /// Quintic ease-out: fast enough at the start that the figures blur, slow enough at the
        /// end that the last two or three pegs go past one at a time and the player can see
        /// which one they are going to get. Monotone by construction, so the wheel never
        /// visibly backs up on its way to a slice — which would read as the result being
        /// changed after the fact.
        /// </para>
        /// </summary>
        public static float AngleAt(int count, int index, float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return Target(count, index);

            float k = 1f - t;
            float eased = 1f - k * k * k * k * k;

            return Target(count, index) * eased;
        }

        /// <summary>
        /// How many pegs have gone past the pointer by <paramref name="angle"/>.
        ///
        /// <para>
        /// The view fires a tick each time this changes, rather than counting boundaries for
        /// itself — one arithmetic rule for where the slices are, asked by the thing that draws
        /// them and by the thing that sounds them. Two copies would be a wheel whose clicks
        /// drift out of step with its own edges, which is precisely the kind of fault nobody can
        /// name and everybody notices.
        /// </para>
        /// </summary>
        public static int PegsPassed(int count, float angle)
        {
            if (count <= 0) return 0;

            // Clockwise travel is negative; pegs are counted as a positive distance so the
            // sequence rises. Floor rather than truncate, so the count never stalls at a
            // boundary the wheel has genuinely crossed.
            double step = 360.0 / count;
            double travelled = -angle;
            if (travelled <= 0.0) return 0;

            return (int)Math.Floor(travelled / step);
        }

        /// <summary>
        /// How long a landing celebration should run before the panel becomes a question again.
        ///
        /// Scaled by how good the slice was, so the wheel's best result is allowed to be the
        /// loudest thing on the screen and its most ordinary one gets out of the way. Bounded
        /// at both ends: nothing here may ever be so short that it reads as a glitch, or so long
        /// that a player who wants the video is waiting for an animation to finish.
        /// </summary>
        public static float CelebrationSeconds(int percent)
        {
            if (percent <= WheelRules.MinPercent) return .55f;

            // Two hundred percent lands at about .8s and a thousand at the ceiling. Linear on
            // purpose — a curve here would be tuning nobody could explain from the numbers.
            float extra = (percent - WheelRules.MinPercent) / 400f;
            float seconds = .55f + extra * .55f;

            return seconds > 1.35f ? 1.35f : seconds;
        }
    }
}
