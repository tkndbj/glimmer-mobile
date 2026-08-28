using GlimmerGrove.Localization;
using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What happens when somebody taps a real-money product, and what is said when nothing
    /// can.
    ///
    /// <para>
    /// <b>It is here because there are now two places a product can be tapped.</b> The shop
    /// screen was the only one until a lost run began offering gems without navigating
    /// anywhere (<c>ContinueOverlay</c>), and the second caller wants exactly the first
    /// caller's behaviour: open the store's own sheet when it can, and otherwise say which of
    /// the six things went wrong. Copying that would have been six sentences maintained twice,
    /// on the one screen in the game where real money changes hands — invariant 9a's argument,
    /// at the smallest scale it applies at.
    /// </para>
    /// <para>
    /// <b>There is deliberately no confirmation.</b> The payment sheet <em>is</em> the
    /// confirmation: it names the product, states the price in the player's own currency, and
    /// on both platforms asks for a password, a fingerprint or a face before a penny moves. A
    /// panel of ours in front of it is a tap for a question about to be asked properly.
    /// </para>
    /// <para>
    /// Every refusal is a toast rather than a panel, because none of them is a decision — they
    /// are all statements about the store, and three of the four resolve by waiting.
    /// </para>
    /// </summary>
    public static class StoreTap
    {
        /// <summary>
        /// Opens the payment sheet for <paramref name="product"/>, or says why it cannot.
        ///
        /// <para>
        /// Returns whether the sheet was opened, so a caller with chrome to update knows
        /// whether anything is now in flight. It is never a statement that money moved: on
        /// Android the sheet outlives the process, so the only thing that reports a purchase
        /// is <c>StoreService.Granted</c>.
        /// </para>
        /// </summary>
        /// <param name="host">Whose content the refusal is toasted over. Usually the caller.</param>
        public static bool Buy(View host, StoreProduct product)
        {
            if (product == null) return false;

            var offer = StoreService.OfferFor(product);

            switch (offer.State)
            {
                case StoreOfferState.Ready:
                    var result = StoreService.Buy(product);
                    if (result.Ok) return true;

                    Say(host, StoreWording.Failure(result.Failure));
                    return false;

                case StoreOfferState.Owned:
                    Say(host, ("ui.shop.already_owned", Pal.Mint));
                    return false;

                case StoreOfferState.Included:
                    Say(host, ("ui.shop.already_included", Pal.Mint));
                    return false;

                case StoreOfferState.AwaitingGrant:
                    Say(host, ("ui.shop.awaiting", Pal.Sun));
                    return false;

                case StoreOfferState.Purchasing:
                    Say(host, ("ui.shop.purchasing", Pal.Aqua));
                    return false;

                case StoreOfferState.Loading:
                    Say(host, ("ui.shop.connecting", Pal.Aqua));
                    return false;

                default:
                    Say(host, ("ui.shop.offline", Pal.Sun));
                    return false;
            }
        }

        static void Say(View host, (string Key, Color Tint) line)
        {
            if (!host) return;
            Scenery.Toast(host.Content, Loc.Get(line.Key), line.Tint, 2.6f);
        }
    }

    /// <summary>
    /// One sentence per store failure, in one place.
    ///
    /// <para>
    /// Written out rather than composed from the enum name, for the reason every key in this
    /// project is: a key built by concatenation is invisible to the build gate's string
    /// scanner and ships missing in whichever language nobody tested (invariant 6).
    /// </para>
    /// <para>
    /// Separate from <see cref="StoreTap"/> because the shop screen needs the wording without
    /// the tapping — it hears failures asynchronously, from <c>StoreService.Failed</c>, long
    /// after whatever tap caused them.
    /// </para>
    /// </summary>
    public static class StoreWording
    {
        /// <summary>
        /// What a badge says, or null for a card that carries none.
        ///
        /// Here rather than beside the shelf that reads it because <see cref="ProductCard"/> is
        /// the only thing that draws one, and it is drawn on two screens.
        /// </summary>
        public static string Badge(StoreBadge badge)
        {
            switch (badge)
            {
                case StoreBadge.Popular: return "ui.shop.badge_popular";
                case StoreBadge.BestValue: return "ui.shop.badge_best";
                case StoreBadge.Starter: return "ui.shop.badge_starter";
                default: return null;
            }
        }

        /// <summary>
        /// One sentence per reason a gem-priced good cannot be bought.
        ///
        /// Read by the card that draws the refusal on its price face and by the panel that
        /// confirms the purchase, which is why it is neither of theirs.
        /// </summary>
        public static string GoodRefusal(GoodOfferState state)
        {
            switch (state)
            {
                case GoodOfferState.ShortOfGems: return "ui.shop.need_gems";
                case GoodOfferState.HeartsNearlyFull: return "ui.shop.hearts_full";
                case GoodOfferState.BoostNearlyFull: return "ui.shop.boost_full";
                default: return "ui.shop.unknown_product";
            }
        }

        public static (string Key, Color Tint) Failure(StoreFailure failure)
        {
            switch (failure)
            {
                case StoreFailure.NotConnected: return ("ui.shop.offline", Pal.Sun);
                case StoreFailure.UnknownProduct: return ("ui.shop.unknown_product", Pal.Sun);
                case StoreFailure.AlreadyOwned: return ("ui.shop.already_owned", Pal.Mint);
                case StoreFailure.PaymentFailed: return ("ui.shop.payment_failed", Pal.Rose);
                case StoreFailure.AwaitingGrant: return ("ui.shop.awaiting", Pal.Sun);
                case StoreFailure.Deferred: return ("ui.shop.deferred", Pal.Aqua);
                case StoreFailure.Unavailable: return ("ui.shop.unavailable", Pal.Sun);
                default: return ("ui.shop.failed", Pal.Rose);
            }
        }
    }
}
