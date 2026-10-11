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

        /// <summary>
        /// The percentage a real-money bundle's seal prints - the welcome bundle at the top of the
        /// deals panel - or 0 for no seal.
        ///
        /// <para>
        /// <b>The same sentence as a deal's, measured the same honest way</b>: what the bundle's
        /// gems and coins would cost bought from the shop's own cheapest packs
        /// (<see cref="StoreCatalog.GemBase"/>, <see cref="StoreCatalog.CoinBase"/>), over what the
        /// bundle costs - all in the reference prices the ladder is proved against, never a figure
        /// typed for the badge. At the shipped shop, 500 gems and 22,500 coins for $2.99 against
        /// $4.95 and $5.97 of packs is "360% Value". Floored to ten, hidden at 100% or under, and
        /// capped, exactly as <see cref="Percent(long, long, long)"/> is.
        /// </para>
        /// </summary>
        public static int BundlePercent(StoreProduct product, StoreCatalog catalog)
        {
            if (product == null || product.IsDailyPass) return 0;
            return WorthPercent(product, product.Credits, product.Gems, catalog);
        }

        /// <summary>
        /// The percentage a seven-day pass's value badge prints: everything the pass pays over its
        /// days (<see cref="StoreProduct.TotalGems"/>, <see cref="StoreProduct.TotalCredits"/>) at
        /// the base packs' prices, over what the pass costs - <see cref="BundlePercent"/>'s honest
        /// arithmetic, on the whole of what is bought. At the shipped shop, 3,500 gems for $1.99
        /// against $34.65 of packs is "1740% Value".
        /// </summary>
        public static int PassPercent(StoreProduct product, StoreCatalog catalog)
        {
            if (product == null || !product.IsDailyPass) return 0;
            return WorthPercent(product, product.TotalCredits, product.TotalGems, catalog);
        }

        /// <summary><paramref name="credits"/> and <paramref name="gems"/> at the base packs' prices, over <paramref name="product"/>'s price.</summary>
        static int WorthPercent(StoreProduct product, long credits, long gems, StoreCatalog catalog)
        {
            if (product == null || catalog == null || !catalog.HasExchangeRate) return 0;
            if (product.IsContainer || product.ReferenceUsdCents <= 0) return 0;

            var gemBase = catalog.GemBase;
            var coinBase = catalog.CoinBase;
            if (gemBase == null || coinBase == null || gemBase.Gems <= 0 || coinBase.Credits <= 0) return 0;

            // Everything over the common denominator (bundle cents x coin pack's coins x gem pack's
            // gems), so no division happens until the end. In decimal, which is exact integer
            // arithmetic to 28 digits - never a float deciding a figure shown for money - because
            // the products of StoreLimits' bounds (grants 5e6, cents 1e5) overflow a long.
            decimal worth = (decimal)credits * coinBase.ReferenceUsdCents * gemBase.Gems
                          + (decimal)gems * gemBase.ReferenceUsdCents * coinBase.Credits;
            decimal price = (decimal)product.ReferenceUsdCents * coinBase.Credits * gemBase.Gems;
            if (worth <= 0m || price <= 0m) return 0;

            decimal whole = decimal.Floor(worth * 100m / price);
            if (whole > Most + Step) whole = Most + Step;
            long percent = (long)whole;
            if (percent <= 100L) return 0;

            percent -= percent % Step;
            return percent > Most ? Most : (int)percent;
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
