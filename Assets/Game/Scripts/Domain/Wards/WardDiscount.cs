using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;

namespace GlimmerGrove.Wards
{
    /// <summary>Whether the turret discount can be taken right now, and which kind of no it is.</summary>
    public enum WardDiscountState
    {
        /// <summary>Not drawn at all: switched off in content, or no ad unit on this platform.</summary>
        Off,

        /// <summary>Drawn, and the network has nothing loaded yet. Worth waiting a moment for.</summary>
        NotLoaded,

        /// <summary>A video is loaded. The key is live, provided the half price can be paid.</summary>
        Ready,
    }

    /// <summary>
    /// Watch a video, pay part of a turret's or a star's price (2026-10-10).
    ///
    /// <para>
    /// <b>The owner's answer to players not watching enough</b>, taken over a forced video before
    /// every purchase: the offer stands beside the price at the moment somebody has already decided
    /// they want the thing, and refusing it costs nothing. <b>Always offered</b> - no daily cap
    /// and no shared cooldown, at the owner's instruction. The networks' own fill (roughly ten to
    /// twenty-five rewarded views a user a day) is the only ceiling, and it reads as
    /// <see cref="WardDiscountState.NotLoaded"/>.
    /// </para>
    /// <para>
    /// <b>Nothing here is adjudicated</b>, and that is sound for the reason turret ownership is: a
    /// turret and a star are client-held entitlements priced on the device (invariant 15), the
    /// server takes whatever debit it is sent for them, and so a forged discount buys exactly what
    /// a forged price already could. The coins still leave through
    /// <c>PlayerProgression.TrySpend</c>, so the wallet the server keeps is debited the smaller
    /// figure like any other spend.
    /// </para>
    /// <para>
    /// <b>The video is spent on the purchase it was watched for, never banked.</b> Nothing records
    /// that a view happened, so there is nothing to merge, nothing to forge and nothing to lose on
    /// a crash between the video and the debit beyond the one discount.
    /// </para>
    /// </summary>
    public static class WardDiscount
    {
        /// <summary>The share taken off, in percent. Nought when the offer is switched off.</summary>
        public static int Percent => RewardedAds.Table.TurretDiscountPercent;

        /// <summary>
        /// Whether the offer belongs on screen at all. False keeps the key off the panel, which is
        /// the honest answer for a state that will not resolve by waiting on it.
        /// </summary>
        public static bool Offered => Percent > 0 && AdConfig.HasUnit(AdPlacement.TurretDiscount);

        /// <summary>Where the offer stands right now.</summary>
        public static WardDiscountState State
            => !Offered ? WardDiscountState.Off
             : RewardedAds.Provider.IsReady(AdPlacement.TurretDiscount) ? WardDiscountState.Ready
             : WardDiscountState.NotLoaded;

        /// <summary>
        /// What a price comes to after the discount. The share taken off rounds down, so the
        /// player pays the larger half of an odd price and a discount never makes something free.
        /// </summary>
        public static long PriceOf(long cost) => PriceOf(cost, Percent);

        /// <summary>The same arithmetic at a given percent. Integer only - no float decides a price.</summary>
        public static long PriceOf(long cost, int percent)
        {
            if (cost <= 0 || percent <= 0) return cost;
            if (percent > AdRules.MaxTurretDiscountPercent) percent = AdRules.MaxTurretDiscountPercent;

            return cost - cost * percent / 100;
        }

        /// <summary>
        /// Records a finished view of the offer, for the same funnel every other placement reports
        /// into. Called by the panel with the SDK's answer, whether or not it paid.
        /// </summary>
        public static void Note(AdShowResult result, string what)
        {
            if (!result.Rewarded) return;

            Telemetry.Track("rewarded_ad_completed",
                            "placement", AdPlacement.TurretDiscount,
                            "reward", what ?? string.Empty,
                            "percent", Percent);
        }
    }
}
