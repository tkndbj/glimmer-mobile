using GlimmerGrove.Ads;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// Which shelf of the shop offers a video, and which placement it offers.
    ///
    /// <para>
    /// <b>Here rather than beside the screen, for <c>ChapterMap</c>'s reason</b> (invariant 8a):
    /// a shelf carrying a placement this build has retired is a card that silently stops being
    /// drawn — <c>AdRewardTable.Offer</c> answers <see cref="AdOffer.None"/> for an id it does
    /// not carry, the row count drops by one, and every other gate in this project stays green
    /// while a shelf quietly loses its free spot. That is checkable arithmetic over two tables,
    /// and arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can check.
    /// </para>
    /// <para>
    /// <b>The pairs are written out rather than derived from what a shelf sells.</b> A placement
    /// id is permanent and reaches the mediation dashboard, the published reward table, the
    /// server's grant log and every analytics row ever written (<see cref="AdPlacement"/>); a
    /// shelf is a browsing decision that could be re-cut tomorrow. Matching the two by what they
    /// happen to pay would quietly move a placement the day a shelf changed hands, which is the
    /// one thing an id of that kind may never do.
    /// </para>
    /// <para>
    /// <b>Three shelves deliberately have none.</b> Gems and bundles are what real money buys
    /// and no video pays either — a placement that paid gems would be an ad competing with the
    /// shelf it stands on. The kit is priced in gems for the same reason. The two placements
    /// that are <em>not</em> here at all, the victory bonus and the hint, are offered at the
    /// moment they are wanted rather than from a storefront, which is where their whole value
    /// comes from (<see cref="AdPlacement.HintRefill"/>).
    /// </para>
    /// </summary>
    public static class ShopAdShelf
    {
        /// <summary>
        /// The placement standing in this shelf's first spot, or null when it has none.
        ///
        /// <para>
        /// First rather than last, and that falls out of the sort rather than being a taste:
        /// every money shelf is ordered cheapest first, and nothing is cheaper than nothing. A
        /// free offer under six prices is an offer nobody scrolls to.
        /// </para>
        /// </summary>
        public static string For(StoreShelf shelf)
        {
            switch (shelf)
            {
                case StoreShelf.Coins: return AdPlacement.CoinBonus;
                case StoreShelf.Supplies: return AdPlacement.HeartRefill;
                default: return null;
            }
        }

        /// <summary>Whether this shelf offers a video at all, before any table is asked.</summary>
        public static bool Offers(StoreShelf shelf) => For(shelf) != null;
    }
}
