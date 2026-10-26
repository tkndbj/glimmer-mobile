namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The first sortie's funnel (invariant 53h): one event per step, in the order a new player
    /// meets them.
    ///
    /// <para>
    /// <b>One event name per step and no parameters</b>, which is the choice the report decides.
    /// A step carried as a parameter is invisible in GA4 until somebody registers a custom
    /// dimension for it (the property holds two), while an event name is a funnel step the day it
    /// arrives - and the question these answer is only ever "how many reached this", in order.
    /// The level the sortie ends in is <c>level_started</c>, which already exists.
    /// </para>
    /// <para>
    /// <b>The names are permanent</b>: a renamed event is a funnel cut in two at the release that
    /// renamed it.
    /// </para>
    /// </summary>
    public static class SortieAnalytics
    {
        /// <summary>The tutorial's own victory, and its finale raised.</summary>
        public const string Finished = "tutorial_finished";

        /// <summary>The tutorial's skip key, and its finale raised.</summary>
        public const string Skipped = "tutorial_skipped";

        /// <summary>The finale's key, on either route.</summary>
        public const string Continued = "sortie_continue";

        /// <summary>The guide has walked on, after the loadout's lessons.</summary>
        public const string GuideShown = "sortie_guide_shown";

        /// <summary>The guide's key. A player who goes back to the map instead never sends it.</summary>
        public const string GuideDismissed = "sortie_guide_dismissed";

        /// <summary>The BATTLE key that replaces the guide.</summary>
        public const string Battle = "sortie_battle";

        public static void Track(string step) => Telemetry.Track(step);
    }
}
