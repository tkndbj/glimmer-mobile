using GlimmerGrove.Ads;
using GlimmerGrove.Localization;

namespace GlimmerGrove
{
    /// <summary>
    /// What a rewarded offer says about itself, wherever one is drawn.
    ///
    /// <para>
    /// <b>One copy, because the honest states are the substance of the feature.</b> A rewarded
    /// ad has five ways of not happening and a player meets all of them: the network has
    /// nothing loaded, the day's allowance is spent, another video was watched a minute ago,
    /// the resource is already full, or there is no account to pay currency into. A panel that
    /// renders those as one greyed button teaches people the feature is broken and they stop
    /// looking at it, which costs far more than the ad it failed to show.
    /// </para>
    /// <para>
    /// It was already shared between the three buttons; the sentences under them were not, and
    /// lived inside <c>AdOfferOverlay</c> where a second panel could not reach them. The bonus
    /// wheel is that second panel, and a second copy of "another video in 0:32" is two places
    /// for one of them to keep saying WATCH while the button does nothing. Lifted out whole
    /// rather than duplicated, for invariant 9a's reason at the smallest scale it appears at.
    /// </para>
    /// </summary>
    static class AdOfferButton
    {
        /// <summary>What the button should say, given the placement's current state.</summary>
        public static string Caption(AdOfferStatus status, string readyKey)
        {
            switch (status.State)
            {
                case AdOfferState.Ready:
                    return Loc.Get(readyKey);

                case AdOfferState.CoolingDown:
                    return string.Format(Loc.Get("ui.ads.btn_cooling"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.CapReached:
                    return Loc.Get("ui.ads.btn_cap");

                default:
                    return Loc.Get("ui.ads.btn_loading");
            }
        }

        /// <summary>
        /// Repaints a button in place. Safe to call on a timer — the caption is only assigned
        /// when it actually changed, and the glyph beside it is only re-measured on the ticks
        /// where it really moved.
        /// </summary>
        public static void Paint(Btn button, string placementId, string readyKey)
        {
            if (button == null) return;

            var status = RewardedAds.Status(placementId);

            button.Interactable = status.CanShow;
            button.SetCaption(Caption(status, readyKey));
        }

        /// <summary>
        /// The sentence under the button. Every branch names the real reason and, where there
        /// is one, when it stops being true.
        /// </summary>
        public static string Explain(AdOfferStatus status)
        {
            switch (status.State)
            {
                case AdOfferState.Ready:
                    return Loc.Get("ui.ads.ready");

                case AdOfferState.NotLoaded:
                    return Loc.Get("ui.ads.loading");

                case AdOfferState.CoolingDown:
                    return string.Format(Loc.Get("ui.ads.cooling"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.CapReached:
                    return string.Format(Loc.Get("ui.ads.cap_reached"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.NothingToGain:
                    return Loc.Format("ui.ads.hearts_ceiling", Profile.HeartCeiling);

                case AdOfferState.NeedsAccount:
                    return Loc.Get("ui.ads.needs_account");

                default:
                    return Loc.Get("ui.ads.unavailable");
            }
        }

        /// <summary>Why nothing was paid, in the player's terms rather than the SDK's.</summary>
        public static string Refusal(AdOutcome outcome)
        {
            switch (outcome)
            {
                case AdOutcome.Dismissed: return Loc.Get("ui.ads.dismissed");
                case AdOutcome.NoFill: return Loc.Get("ui.ads.no_fill");
                case AdOutcome.Unavailable: return Loc.Get("ui.ads.unavailable");
                default: return Loc.Get("ui.ads.failed");
            }
        }
    }
}
