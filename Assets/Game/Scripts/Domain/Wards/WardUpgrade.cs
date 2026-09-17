using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Wards
{
    /// <summary>Why an upgrade is not on offer, or that it is.</summary>
    public enum WardUpgradeState
    {
        /// <summary>Affordable and there is a star left to buy. The only live state.</summary>
        Ready,

        /// <summary>The turret is not held on this seat, so there is nothing to upgrade.</summary>
        NotHeld,

        /// <summary>Already at the top of the ladder.</summary>
        Topped,

        /// <summary>There is a star to buy and not enough credits for it.</summary>
        Short,
    }

    /// <summary>What the next star on one seat's turret would cost, and whether it can be had.</summary>
    public readonly struct WardUpgradeOffer
    {
        public readonly WardUpgradeState State;

        /// <summary>The star being bought, two to five, or nought when there is none.</summary>
        public readonly int Star;

        /// <summary>What it costs in credits, or nought.</summary>
        public readonly long Cost;

        /// <summary>What the player is holding, for a panel that shows the gap.</summary>
        public readonly long Balance;

        public WardUpgradeOffer(WardUpgradeState state, int star, long cost, long balance)
        {
            State = state;
            Star = star;
            Cost = cost;
            Balance = balance;
        }

        public bool CanBuy => State == WardUpgradeState.Ready;

        public long Shortfall => Cost > Balance ? Cost - Balance : 0L;
    }

    /// <summary>
    /// Buying the next star on a turret: what it costs, whether it can be had, and the one place
    /// it happens.
    ///
    /// <para>
    /// <b>Its own door rather than a second branch inside <c>WardLedger</c>.</b> Buying a turret
    /// and upgrading one are two purchases with two prices, two refusals and two telemetry lines;
    /// folded into one method they would be one method with a flag, and the flag would be the
    /// thing every caller has to get right. It is the split <c>HeartRescue</c> and
    /// <c>RunContinue</c> already make — two panels, two prices, one fixed order.
    /// </para>
    /// <para>
    /// <b>The money leaves through <c>PlayerProgression.TrySpend</c> and nowhere else</b>, and the
    /// ledger is only told after it has. Two places that could debit is two chances to charge for
    /// one thing, which is invariant 23's argument about the continue.
    /// </para>
    /// <para>
    /// <b>Credits and never gems</b>, deliberately. Gems buy a turret *sooner* (invariant 42c's
    /// shortcut); credits are what playing pays out, so the ladder is the sink that gives a
    /// long-running account something to spend on. Nothing about it is adjudicated, for
    /// <c>WardStarLedger</c>'s reason.
    /// </para>
    /// </summary>
    public static class WardUpgrade
    {
        /// <summary>What a debit says in support's free-text reason.</summary>
        const string SpendReason = "ward_star:";

        /// <summary>Raised after a star lands, so a shelf and a panel repaint together.</summary>
        public static event Action<WardModel> Bought;

        /// <summary>What the next star on this seat would cost, and whether it can be had.</summary>
        public static WardUpgradeOffer OfferFor(WardModel model, char colour)
        {
            long purse = PlayerProgression.Credits;

            if (model == null || !WardLedger.IsHeld(model, colour))
                return new WardUpgradeOffer(WardUpgradeState.NotHeld, 0, 0L, purse);

            int stars = WardStarLedger.StarsOf(model, colour);
            int price = WardStars.PriceOf(model, stars);

            if (price <= 0)
                return new WardUpgradeOffer(WardUpgradeState.Topped, 0, 0L, purse);

            var state = purse >= price ? WardUpgradeState.Ready : WardUpgradeState.Short;

            return new WardUpgradeOffer(state, stars + 1, price, purse);
        }

        /// <summary>The same for a colour index (0..3).</summary>
        public static WardUpgradeOffer OfferFor(WardModel model, int colour)
            => OfferFor(model, WardLine.Colours[colour < 0 || colour >= WardLine.Colours.Length
                                                ? 0 : colour]);

        /// <summary>
        /// Buys the next star, or answers false and takes nothing.
        ///
        /// <b>The offer is asked again here rather than trusted from the caller</b>, which is
        /// <c>WardLedger.TryBuy</c>'s rule: a panel can be looking at a price that a sync, a
        /// purchase or another panel has moved since it was drawn.
        /// </summary>
        public static bool TryBuy(WardModel model, char colour)
        {
            var offer = OfferFor(model, colour);
            if (!offer.CanBuy) return false;

            // The reason carries the seat and the star, because support reading a debit has to
            // know which of the four a player paid for and how far up - and for a legendary it
            // carries no seat at all, because there is one ladder rather than four
            // (`WardHolding.Row`). The debit's text and the ledger's key are the same string on
            // purpose: support reading a spend can look the row straight up.
            string row = WardHolding.Row(model, colour);

            if (!PlayerProgression.TrySpend(Currency.Credits, offer.Cost,
                                            SpendReason + row + ":" + offer.Star))
                return false;

            if (!WardStarLedger.Raise(model, colour, offer.Star)) return false;

            Telemetry.Track("ward_upgraded", "ward", model.Id,
                            "colour", model.Colourless ? "any" : colour.ToString(),
                            "star", offer.Star, "cost", offer.Cost);

            SaveService.Save();

            try { Bought?.Invoke(model); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

        /// <summary>The same for a colour index (0..3).</summary>
        public static bool TryBuy(WardModel model, int colour)
            => TryBuy(model, WardLine.Colours[colour < 0 || colour >= WardLine.Colours.Length
                                              ? 0 : colour]);
    }
}
