namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The launch's two milestones, for Analytics only (the attribution sink's allow-list does not
    /// name them, so they never reach an ad network).
    ///
    /// <para>
    /// <b><see cref="Ready"/></b> - once per launch, when the first screen after the splash has
    /// opened and can be tapped: how long the player waited (<c>load_ms</c>), whether this is the
    /// install's first launch, and the build. It answers the question <c>first_open</c> cannot: of
    /// the people who opened the game, how many reached something they could play, and how long it
    /// took on their phone.
    /// </para>
    /// <para>
    /// <b><see cref="TutorialStarted"/></b> - once per install, the first time the tutorial's board is
    /// on screen: the funnel step in front of <c>tutorial_finished</c>/<c>tutorial_skipped</c>
    /// (<see cref="SortieAnalytics"/>). A later launch that reopens an unfinished tutorial (the player
    /// left mid-way) is a different behaviour and gets its own name, <see cref="TutorialResumed"/>,
    /// rather than a parameter: a GA4 parameter is invisible until registered, an event name is a
    /// step the day it arrives (<see cref="SortieAnalytics"/>'s rule). Whether this install has sent
    /// it is device-local (<c>LaunchWatch</c>); a reinstall starts again, as <c>first_open</c> does.
    /// </para>
    /// <para>
    /// Both go through <see cref="Telemetry"/> like every other event, so they wait in the delivery
    /// queue until collection opens (invariant 55b) and carry whatever consent the first events
    /// carry. <b>The names are permanent</b> for <see cref="SortieAnalytics"/>'s reason.
    /// </para>
    /// </summary>
    public static class BootAnalytics
    {
        public const string Ready = "boot_ready";
        public const string TutorialStarted = "tutorial_started";
        public const string TutorialResumed = "tutorial_resumed";

        /// <summary>The largest wait reported; anything longer is a suspended process, not a load.</summary>
        public const long MaxLoadMs = 10L * 60L * 1000L;

        static bool _ready, _tutorial;

        /// <summary>
        /// The first interactive screen is up. Sent once per process; later calls do nothing.
        /// </summary>
        /// <param name="loadMs">Process start (or, where the platform cannot say, engine start) to now.</param>
        /// <param name="clock"><c>process</c> or <c>engine</c>: which start <paramref name="loadMs"/> is measured from.</param>
        /// <param name="firstLaunch">This install has never sent <see cref="Ready"/> before.</param>
        /// <param name="build">The running build (<c>Release.AppVersion.Running</c>); nought when unreadable.</param>
        public static bool NoteReady(long loadMs, string clock, bool firstLaunch, int build)
        {
            if (_ready) return false;
            _ready = true;

            Telemetry.Track(Ready,
                "load_ms", Clamp(loadMs),
                "load_clock", string.IsNullOrEmpty(clock) ? "engine" : clock,
                "first_launch", firstLaunch,
                "build", build > 0 ? build : 0);
            return true;
        }

        /// <summary>
        /// The tutorial's board is on screen: <see cref="TutorialStarted"/> on this install's first,
        /// <see cref="TutorialResumed"/> on any later launch. At most one of the two per process.
        /// </summary>
        /// <param name="startedBefore">This install has already sent <see cref="TutorialStarted"/>.</param>
        public static bool NoteTutorial(bool startedBefore)
        {
            if (_tutorial) return false;
            _tutorial = true;

            Telemetry.Track(startedBefore ? TutorialResumed : TutorialStarted);
            return true;
        }

        static long Clamp(long ms) => ms < 0L ? 0L : ms > MaxLoadMs ? MaxLoadMs : ms;

        /// <summary>For tests: forgets that this process has sent either.</summary>
        internal static void Reset()
        {
            _ready = false;
            _tutorial = false;
        }
    }
}
