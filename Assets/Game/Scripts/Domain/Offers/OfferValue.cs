using GlimmerGrove.Store;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// The "+N%" an offer's seal prints (invariant 64): how much more its gems and coins are worth
    /// than the same amounts bought from the shop's own cheapest packs, over what the offer costs.
    ///
    /// <para>
    /// <b>Derived, never authored</b> - <see cref="DealValue.BundlePercent"/>'s honest arithmetic in
    /// the reference prices the ladder is proved against, so the claim moves by itself when the shop
    /// is retuned and nobody can type a figure that is not true. "+330%" is a worth of 430%: the
    /// extra over the shop, floored to ten, and no seal at all when the offer is not better.
    /// </para>
    /// </summary>
    public static class OfferValue
    {
        public static int Extra(StoreProduct product, StoreCatalog catalog)
        {
            int worth = DealValue.BundlePercent(product, catalog);
            return worth > 100 ? worth - 100 : 0;
        }
    }
}
