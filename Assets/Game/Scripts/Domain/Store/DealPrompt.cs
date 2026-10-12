using System.Collections.Generic;

namespace GlimmerGrove.Store
{
    /// <summary>Why a deals popup was raised. The wire spelling is the analytics value.</summary>
    public enum DealTrigger
    {
        None,

        /// <summary>A coin price was shown that the player could not pay.</summary>
        Shortfall,

        /// <summary>The player came back to the hub from a win.</summary>
        Win,

        /// <summary>A live deal is in its last hours.</summary>
        LastHours,

        /// <summary>The player is on the hub and has not been shown the deals today.</summary>
        Daily,
    }

    /// <summary>
    /// When the game puts limited-time deals in front of a player outside the shop (invariant
    /// 60c), as pure rules so <c>ShopDealTests</c> can hold every one without a frame of Unity.
    ///
    /// <para>
    /// <b>Who:</b> a player who has cleared <see cref="MinClearedLevels"/> levels - a new player's
    /// first sessions are for the game, not the shop. <b>What:</b> every live deal this account
    /// has not bought. <b>How often:</b> once a UTC day, carrying every live deal at once.
    /// <b>Where</b>, in this order: a coin shortfall (the strongest moment - the offer answers
    /// the problem in front of them), else the hub. The hub's trigger names why it is a good
    /// moment (a win just recorded, a deal in its last <see cref="LastHoursSeconds"/>) or is
    /// simply the day's showing; the name is the analytics value, the decision is the same.
    /// </para>
    /// <para>
    /// Where the player is standing, whether it is calm there, and whether today's popup has been
    /// shown are the caller's to say (<c>DealMoment</c>): these rules decide only whether that
    /// moment is one.
    /// </para>
    /// </summary>
    public static class DealPrompt
    {
        /// <summary>The levels a player must have cleared before any deal is raised outside the shop.</summary>
        public const int MinClearedLevels = 3;

        /// <summary>A deal this close to its end is raised on the hub without waiting for a win.</summary>
        public const long LastHoursSeconds = 3L * 3600L;

        /// <summary>The analytics value of a trigger. Written out, never built from the enum's name.</summary>
        public static string Id(DealTrigger trigger)
        {
            switch (trigger)
            {
                case DealTrigger.Shortfall: return "shortfall";
                case DealTrigger.Win: return "win";
                case DealTrigger.LastHours: return "last_hours";
                case DealTrigger.Daily: return "daily";
                default: return "none";
            }
        }

        /// <summary>
        /// Which trigger this moment answers, or <see cref="DealTrigger.None"/>.
        /// </summary>
        /// <param name="clearedLevels">Levels the player has cleared.</param>
        /// <param name="shownToday">A deals popup has already been raised today.</param>
        /// <param name="shortfallPending">A coin price the player could not pay was shown and not yet answered.</param>
        /// <param name="winPending">A win was recorded and the hub has not yet answered it.</param>
        /// <param name="onHub">The player is on the hub, calm.</param>
        /// <param name="onShortfallStage">The player is on a screen a shortfall may be answered on, calm.</param>
        /// <param name="offered">The live deals this account has not bought.</param>
        /// <param name="nowUnix">Now.</param>
        public static DealTrigger Choose(int clearedLevels, bool shownToday, bool shortfallPending,
                                         bool winPending, bool onHub, bool onShortfallStage,
                                         IReadOnlyList<ShopDeal> offered, long nowUnix)
        {
            if (clearedLevels < MinClearedLevels || shownToday) return DealTrigger.None;
            if (offered == null || offered.Count == 0) return DealTrigger.None;

            if (shortfallPending && onShortfallStage) return DealTrigger.Shortfall;
            if (!onHub) return DealTrigger.None;
            if (winPending) return DealTrigger.Win;

            foreach (var deal in offered)
                if (deal.IsLive(nowUnix) && deal.SecondsLeft(nowUnix) <= LastHoursSeconds) return DealTrigger.LastHours;

            return DealTrigger.Daily;
        }
    }
}
