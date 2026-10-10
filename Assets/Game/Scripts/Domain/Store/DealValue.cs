namespace GlimmerGrove.Store
{
    /// <summary>
    /// What a limited-time deal is worth against the shop's ordinary prices, as the percentage its
    /// card prints (invariant 60d): at the shipped shop's rate of 37 coins a gem, a deal of 50,000
    /// coins for 400 gems - which would otherwise buy 14,800 coins - is "330% value".
    ///
    /// <para>
    /// <b>Derived, never authored.</b> The figure is the deal's coins over what its gems buy at the
    /// shop's own exchange rate (<see cref="StoreCatalog.CreditsPerGem"/> - the rate the money
    /// shelves already imply, the same one the shelves rank their bonuses by), so it moves by itself
    /// when the shop is retuned and the admin page never types a claim it could get wrong.
    /// </para>
    /// <para>
    /// <b>Never overstated.</b> Floored to a multiple of ten in integer arithmetic (no float decides
    /// a figure shown for money), and not printed at all when the deal is not better than the shop
    /// (100% or under) or when the shop has no rate to measure by - "60% value" on a deal is worse
    /// than no badge, and a figure measured off the fallback rate of one coin per gem is fiction.
    /// </para>
    /// </summary>
    public static class DealValue
    {
        /// <summary>The figure is shown to the nearest ten below, so it never claims more than it is worth.</summary>
        public const int Step = 10;

        /// <summary>The ceiling on what a badge prints, so a deal mispriced by a slip on the admin page cannot print a six-digit figure.</summary>
        public const int Most = 9990;

        /// <summary>
        /// The percentage to print, or 0 for no badge.
        /// </summary>
        public static int Percent(ShopDeal deal, StoreCatalog catalog)
        {
            if (deal == null || catalog == null || !catalog.HasExchangeRate) return 0;
            return Percent(deal.Credits, deal.Gems, catalog.CreditsPerGem);
        }

        /// <summary>The arithmetic, on its own: <paramref name="credits"/> for <paramref name="gems"/> at <paramref name="creditsPerGem"/>.</summary>
        public static int Percent(long credits, long gems, long creditsPerGem)
        {
            if (credits <= 0 || gems <= 0 || creditsPerGem <= 0) return 0;

            // Both factors are bounded far below overflow: credits by ShopDeals.MaxCredits (10^7)
            // times 100, gems by MaxGems (10^5) times a rate in the tens.
            long worth = gems * creditsPerGem;
            long percent = credits * 100L / worth;

            if (percent <= 100L) return 0;

            percent -= percent % Step;
            return percent > Most ? Most : (int)percent;
        }
    }
}
