using System;
using GlimmerGrove.Ads;

namespace GlimmerGrove.Analytics
{
    /// <summary>Analytics ownership only. Does not configure mediation, ad units, or placements.</summary>
    static class AdRevenueSources
    {
        public static AdRevenueSource[] ForCurrentPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Owner verified on 2026-10-24: Google is configured as a Partner Bidding instance,
            // not AdMob waterfall. A live coin_bonus impression supplied the exact SDK mapping:
            // Network="admob", InstanceName="Bidding", InstanceId=the Google partner ad-unit ID.
            // All eight units' load callbacks independently confirmed this same partner ID.
            // Evidence: Library/AnalyticsQA/FinalRevenue/source-map.json (2026-10-24).
            // Pin each observed pair; never authorize an unknown unit or a generic "Bidding" name.
            const string partner = "ca-app-pub-4604184534032643/7655758570";
            return new[]
            {
                new AdRevenueSource(AdConfig.AndroidHeartRefill, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidCoinBonus, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidWinBonus, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidHintRefill, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidXpBoost, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidChallengePlay, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidTurretDiscount, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.AndroidContinueVideo, "admob", partner, AdRevenueOwner.LevelPlay)
            };
            // ironSource Exchange was not observed in the available callbacks/configuration.
            // It, standard AdMob and any future/unrecognized instance remain excluded.
#elif UNITY_IOS && !UNITY_EDITOR
            // Verified on a live iPhone (store build 1.1.2 (34)) on 2026-10-24: Google is a Partner
            // Bidding instance here too, with its own iOS partner ad-unit ID. All eight units' load
            // callbacks reported network="admob", instance="Bidding" and this ID, and a watched
            // coin_bonus impression reported the identical triple (logged UnverifiedSource
            // before this entry existed). ironSource, Unity Ads and standard AdMob were not
            // observed and remain excluded; never authorize by network name alone.
            const string partner = "ca-app-pub-4604184534032643/5402954156";
            return new[]
            {
                new AdRevenueSource(AdConfig.IosHeartRefill, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosCoinBonus, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosWinBonus, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosHintRefill, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosXpBoost, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosChallengePlay, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosTurretDiscount, "admob", partner, AdRevenueOwner.LevelPlay),
                new AdRevenueSource(AdConfig.IosContinueVideo, "admob", partner, AdRevenueOwner.LevelPlay)
            };
#else
            // Editor mocks must never emit monetary events.
            return Array.Empty<AdRevenueSource>();
#endif
        }
    }
}
