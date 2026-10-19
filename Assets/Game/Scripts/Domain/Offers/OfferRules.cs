using System;
using System.Collections.Generic;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Offers
{
    /// <summary>Why nothing was dealt, in the order the rules ask. The spelling is the analytics value.</summary>
    public enum OfferRefusal
    {
        None = 0,
        Off,
        NoAccount,
        Holdout,
        TooEarly,
        Standing,
        DayFull,
        SessionFull,
        TooSoon,
        JustBought,
        BackingOff,
        NothingFits,
    }

    /// <summary>What the rules dealt: an offer and the product it sells, or a refusal saying why not.</summary>
    public readonly struct OfferDeal
    {
        public readonly OfferTemplate Offer;
        public readonly string ProductId;
        public readonly OfferRefusal Refusal;

        OfferDeal(OfferTemplate offer, string productId, OfferRefusal refusal)
        {
            Offer = offer;
            ProductId = productId;
            Refusal = refusal;
        }

        public bool Dealt => Offer != null;

        public static OfferDeal Of(OfferTemplate offer, string productId) => new OfferDeal(offer, productId, OfferRefusal.None);
        public static OfferDeal No(OfferRefusal why) => new OfferDeal(null, null, why);
    }

    /// <summary>What the store can say about a product, asked by the rules without knowing the store.</summary>
    public interface IOfferShelf
    {
        /// <summary>Whether the store can sell this product to this account right now (priced, connected, on the offers shelf).</summary>
        bool CanSell(string productId);

        /// <summary>The coins the product grants, for sizing an offer to a shortfall.</summary>
        long CreditsOf(string productId);
    }

    /// <summary>
    /// <b>Which personal offer, if any, answers this moment</b> (invariant 64), as one pure function so
    /// every rule is held offline (<c>OfferRulesTests</c>).
    ///
    /// <para>
    /// The pacing is asked first and is the same for every offer - on, signed in, outside the
    /// holdout, past the first levels, nothing already standing, under the day's and the session's
    /// count, a gap after any money popup, quiet after any purchase, and a backoff that lengthens with
    /// every close in a row. Then each offer answering the moment is asked about the player: in one
    /// of its segments and none of its exclusions, off its cooldown, under its lifetime counts, and
    /// with a product the store can sell. The highest priority wins; a tie is drawn by weight with a
    /// hash of (account, day, offer), so a device deals the same answer all day and two devices agree.
    /// </para>
    /// </summary>
    public static class OfferRules
    {
        /// <summary>Whether this account is in the share never dealt a personal offer. Stable for the life of the account.</summary>
        public static bool InHoldout(string account, int holdoutPercent)
        {
            if (holdoutPercent <= 0 || string.IsNullOrEmpty(account)) return false;
            return Hash("offers.holdout:" + account) % 100u < (uint)holdoutPercent;
        }

        /// <summary>The arm an account is measured in: <c>holdout</c> or <c>offers</c>.</summary>
        public static string Arm(string account, int holdoutPercent)
            => InHoldout(account, holdoutPercent) ? "holdout" : "offers";

        public static OfferDeal Choose(OfferTuning tuning, OfferProfile profile, OfferTrigger moment,
                                       OfferHistory history, IOfferShelf shelf, string account, long nowUnix)
        {
            if (tuning == null || !tuning.Enabled) return OfferDeal.No(OfferRefusal.Off);
            if (string.IsNullOrEmpty(account)) return OfferDeal.No(OfferRefusal.NoAccount);
            if (InHoldout(account, tuning.HoldoutPercent)) return OfferDeal.No(OfferRefusal.Holdout);
            if (profile.Get(OfferMeasure.ClearedLevels) < tuning.MinClearedLevels) return OfferDeal.No(OfferRefusal.TooEarly);

            history.Expire(nowUnix);
            if (history.Active != null) return OfferDeal.No(OfferRefusal.Standing);

            int today = DailyRules.DayKeyFor(nowUnix);
            if (history.ShowsOn(today) >= tuning.PerDay) return OfferDeal.No(OfferRefusal.DayFull);
            if (history.SessionShows >= tuning.PerSession) return OfferDeal.No(OfferRefusal.SessionFull);

            if (history.LastPopupUnix > 0 && nowUnix - history.LastPopupUnix < tuning.GapMinutes * 60L)
                return OfferDeal.No(OfferRefusal.TooSoon);

            if (history.LastPurchaseUnix > 0 && nowUnix - history.LastPurchaseUnix < tuning.QuietAfterPurchaseHours * 3600L)
                return OfferDeal.No(OfferRefusal.JustBought);

            long backoff = tuning.BackoffSeconds(history.Dismissals);
            if (backoff > 0 && history.LastDismissUnix > 0 && nowUnix - history.LastDismissUnix < backoff)
                return OfferDeal.No(OfferRefusal.BackingOff);

            OfferTemplate best = null;
            string bestProduct = null;
            ulong bestDraw = 0;

            foreach (var offer in tuning.Offers)
            {
                if (!offer.Answers(moment)) continue;
                if (!IsFor(tuning, offer, profile)) continue;

                if (offer.Shows > 0 && history.ShowsOf(offer.Id) >= offer.Shows) continue;
                if (offer.Buys > 0 && history.BuysOf(offer.Id) >= offer.Buys) continue;

                long last = history.LastShownOf(offer.Id);
                if (last > 0 && offer.CooldownHours > 0 && nowUnix - last < offer.CooldownHours * 3600L) continue;

                string product = PickProduct(offer, profile, shelf);
                if (product == null) continue;

                ulong draw = (ulong)offer.Weight * (Hash(account + ":" + today + ":" + offer.Id) % 1000u + 1u);
                if (best == null || offer.Priority > best.Priority || (offer.Priority == best.Priority && draw > bestDraw))
                {
                    best = offer;
                    bestProduct = product;
                    bestDraw = draw;
                }
            }

            return best != null ? OfferDeal.Of(best, bestProduct) : OfferDeal.No(OfferRefusal.NothingFits);
        }

        /// <summary>In one of the offer's segments and in none of its exclusions.</summary>
        public static bool IsFor(OfferTuning tuning, OfferTemplate offer, OfferProfile profile)
        {
            foreach (string id in offer.Exclude)
                if (tuning.Segment(id)?.Matches(profile) == true) return false;

            foreach (string id in offer.Segments)
                if (tuning.Segment(id)?.Matches(profile) == true) return true;

            return false;
        }

        /// <summary>
        /// The product an offer deals: the first the store can sell, or for a shortfall the cheapest
        /// (first listed) whose coins cover the gap. Null when none can be sold.
        /// </summary>
        public static string PickProduct(OfferTemplate offer, OfferProfile profile, IOfferShelf shelf)
        {
            long gap = profile.Get(OfferMeasure.CoinGap);
            if (offer.Pick == OfferPick.Gap && gap <= 0) return null;

            foreach (string product in offer.Products)
            {
                if (shelf == null || !shelf.CanSell(product)) continue;
                if (offer.Pick == OfferPick.Gap && shelf.CreditsOf(product) < gap) continue;
                return product;
            }

            return null;
        }

        /// <summary>The segments a player is in, for analytics: ids joined by commas, in the tuning's order.</summary>
        public static string SegmentsOf(OfferTuning tuning, OfferProfile profile)
        {
            if (tuning == null) return string.Empty;
            var ids = new List<string>();
            foreach (var segment in tuning.Segments)
                if (segment.Rules.Count > 0 && segment.Matches(profile)) ids.Add(segment.Id);
            return string.Join(",", ids);
        }

        /// <summary>
        /// FNV-1a then a xorshift finaliser, all 32-bit: the chest generator's arithmetic (9c), so the
        /// draw spreads across the whole range rather than clustering on the last characters.
        /// </summary>
        internal static uint Hash(string text)
        {
            const uint offset = 2166136261u, prime = 16777619u;
            uint h = offset;
            for (int i = 0; i < text.Length; i++) { h ^= text[i]; h *= prime; }
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return h;
        }
    }
}
