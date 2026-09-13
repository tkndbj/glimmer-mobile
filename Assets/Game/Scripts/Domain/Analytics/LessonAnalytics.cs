using GlimmerGrove.Progression;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The teaching funnel, named once so every sink sees the same event shapes.
    ///
    /// <para>
    /// <b>This is the tutorial, because this game has no other one.</b> There is no scripted
    /// opening sequence to instrument — a player learns by meeting a lesson at the moment the
    /// board first demonstrates the rule (invariant 37bk), so the lessons <em>are</em> the first
    /// ten minutes, and the first ten minutes are what decides whether anybody comes back
    /// tomorrow. Without these events a retention number says people left and nothing says where.
    /// </para>
    /// <para>
    /// <b>Two events rather than one, for <c>LevelAnalytics.ContinueOffered</c>'s reason and one
    /// of its own.</b> A ratio needs a numerator and a denominator that can be counted
    /// independently. And the gap between them is itself the measurement that matters most: a
    /// tip that was shown and never finished is a player whose process died while a modal was up
    /// — the app swapped out, the phone killed it, or they closed it — which is the one exit a
    /// panel cannot report for itself, because <c>TipOverlay.OnDestroy</c> never runs. Inferring
    /// that from a single event carrying a flag would mean trusting an event that by definition
    /// is not sent.
    /// </para>
    /// <para>
    /// <b>Every parameter here is bounded on purpose.</b> A mechanic id is one of about
    /// thirty-five permanent strings and a screen is one of about twenty-five type names, so
    /// both group cleanly in a report for ever. Reading time is whole seconds rather than
    /// <c>LevelAnalytics.Round</c>'s tenths — a tip is read in one to thirty of them, and the
    /// tenths would be precision about nothing.
    /// </para>
    /// </summary>
    public static class LessonAnalytics
    {
        public const string Shown = "lesson_shown";
        public const string Finished = "lesson_finished";

        /// <summary>
        /// How a lesson ended.
        ///
        /// <para>
        /// The panel treats the back gesture as the OK button on purpose — a lesson is shown
        /// once in a player's life and must not be skippable in silence — so these two are one
        /// outcome to the game and deliberately two to a report. A player who backs out of every
        /// tip has read none of them, which looks identical to a player who read them all if the
        /// two exits are counted together, and the answers are opposite: one is a teaching
        /// problem, the other is not a problem at all.
        /// </para>
        /// <para>
        /// <see cref="ByNavigation"/> is the panel being torn down underneath itself — a screen
        /// navigating away mid-chain. It is the default, so an exit nobody thought to name is
        /// counted as the unexplained one rather than quietly as a completion.
        /// </para>
        /// </summary>
        public const string ByButton = "ok";
        public const string ByBack = "back";
        public const string ByNavigation = "gone";

        /// <summary>
        /// A lesson has been raised.
        /// </summary>
        /// <param name="screen">
        /// The screen underneath, which the caller supplies because Domain cannot see
        /// <c>Flow</c>. Null on a lesson raised with no screen behind it.
        /// </param>
        /// <param name="repeat">
        /// Whether the player had met this lesson before — what an info key asks for
        /// (<c>ScreenLessons.Add</c>). A repeat is a player looking something up, which is a
        /// different act from being taught and must not dilute the first-showing figures.
        /// </param>
        /// <param name="pointed">
        /// Whether anything on the screen is being ringed. A lesson with nothing to point at is
        /// a sentence about the screen as a whole, and invariant 6b is the reason to be able to
        /// tell them apart: a tip with no ring looks exactly like a tip whose subject was
        /// missing, and only one of those is intended.
        /// </param>
        public static void TrackShown(Mechanic mechanic, string screen, bool repeat, bool pointed)
        {
            if (!mechanic.IsValid) return;

            Telemetry.Track(Shown,
                "mechanic", mechanic.Id,
                "screen", Named(screen),
                "repeat", repeat,
                "pointed", pointed);
        }

        /// <summary>
        /// A lesson has gone away, however it went.
        /// </summary>
        /// <param name="seconds">
        /// How long it stood, on the unscaled clock. A modal sets <c>Time.timeScale</c> to
        /// nought (invariant 30h), so the scaled one would report nought on every lesson in
        /// the game and nothing about the number would look wrong.
        /// </param>
        public static void TrackFinished(Mechanic mechanic, string screen, string how,
                                         float seconds, bool repeat)
        {
            if (!mechanic.IsValid) return;

            Telemetry.Track(Finished,
                "mechanic", mechanic.Id,
                "screen", Named(screen),
                "how", string.IsNullOrEmpty(how) ? ByNavigation : how,
                "seconds", Seconds(seconds),
                "repeat", repeat);
        }

        /// <summary>
        /// Whole seconds, floored, and never negative.
        ///
        /// <para>
        /// Floored rather than rounded because the question this answers is "was there time to
        /// read it", and a tip dismissed after 900ms should report nought rather than one. The
        /// clamp is for the tip that is somehow destroyed in the same frame it was built, where
        /// the two timestamps can differ by a rounding error in the wrong direction.
        /// </para>
        /// </summary>
        static int Seconds(float seconds)
            => seconds > 0f ? (int)seconds : 0;

        /// <summary>
        /// A screen name that is always a value, because an absent parameter and a parameter
        /// meaning "there was no screen" are two different facts and only one of them is
        /// visible in a report.
        /// </summary>
        static string Named(string screen)
            => string.IsNullOrEmpty(screen) ? "none" : screen;
    }
}
