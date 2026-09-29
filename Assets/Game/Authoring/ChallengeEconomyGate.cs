using GlimmerGrove.Ads;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Whether the most a day of daily challenges could pay stays under
    /// <see cref="ChallengeLimits.MaxDailyCoins"/> once the advert plays are counted (56k, 56p).
    ///
    /// <para>
    /// <b>Why it is not in the reader.</b> <see cref="ChallengeTable.TryRead"/> already holds the
    /// largest deal under the ceiling, but it reads <c>challenges.json</c> alone - and the advert
    /// that sells a play (<c>challenge_play</c>) is an advert, so its daily cap lives in
    /// <c>progression.json</c>'s ads block. The two figures add (the pool is shared by every genre,
    /// so it adds once, on top of the largest deal in every genre), and nothing that reads only one
    /// file can see the sum. This is where both are legible at once: the build gate
    /// (<c>ContentValidation.ValidateChallenges</c>) and the suite. <c>content.py</c> and the
    /// seeder hold the same predicate on their side of the wire, which is <see cref="Ranks.RankGate"/>'s
    /// stance about a figure written in two files, for its reason.
    /// </para>
    /// <para>
    /// In <c>GlimmerGrove.Authoring</c> because no player ever runs it: it decides whether content
    /// is fit to ship.
    /// </para>
    /// </summary>
    public static class ChallengeEconomyGate
    {
        /// <summary>Extra plays the advert can buy in a day: plays a view times views a day, or nought.</summary>
        public static int AdPlaysPerDay(AdRewardTable ads)
        {
            if (ads == null) return 0;
            var offer = ads.Offer(AdPlacement.ChallengePlay);
            return offer.IsValid && offer.Kind == Daily.ChestDropKind.ChallengePlay ? offer.Amount * offer.DailyCap : 0;
        }

        /// <summary>
        /// The most credits a day could pay: the largest deal's plays in every genre plus the
        /// advert plays once, times the rate.
        /// </summary>
        public static long MostCoinsADay(ChallengeTable table, AdRewardTable ads)
        {
            if (table == null) return 0L;
            int plays = table.LargestTier != null ? table.LargestTier.Plays : table.FreePlays;
            return ((long)plays * table.Genres.Count + AdPlaysPerDay(ads)) * table.Rewards.Coins;
        }

        /// <summary>The refusal, or null when the day fits under the ceiling.</summary>
        public static string Check(ChallengeTable table, AdRewardTable ads)
        {
            long most = MostCoinsADay(table, ads);
            if (most <= ChallengeLimits.MaxDailyCoins) return null;

            return $"the largest deal and the day's {AdPlaysPerDay(ads)} advert play(s) could pay {most:N0} credits " +
                   $"a day across {table.Genres.Count} genre(s), above the {ChallengeLimits.MaxDailyCoins:N0} ceiling " +
                   "(ChallengeLimits.MaxDailyCoins); lower the rate, the plays, the advert's cap or the ceiling";
        }
    }
}
