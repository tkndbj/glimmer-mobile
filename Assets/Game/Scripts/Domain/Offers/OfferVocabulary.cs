using System;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// A moment the game can answer with a personal offer (invariant 64). <b>Code, never content</b>:
    /// what the game can notice is a fixed list, and which offer answers it is the tuning's
    /// (<see cref="OfferTuning"/>). The wire spellings are <see cref="OfferTriggers"/>' and are
    /// permanent - a published tuning names them.
    /// </summary>
    public enum OfferTrigger
    {
        None = 0,

        /// <summary>The player is standing on the calm hub. The fallback every other moment shares.</summary>
        Hub,

        /// <summary>The first calm hub of a session (a launch, or a return after half an hour away).</summary>
        Session,

        /// <summary>Back on a calm screen after a won run.</summary>
        Win,

        /// <summary>Back after a win that opened the next chapter.</summary>
        Chapter,

        /// <summary>Back after a lost run.</summary>
        Loss,

        /// <summary>Back after a lost run that got at least <see cref="OfferTuning.NearMissPercent"/> of the way.</summary>
        NearMiss,

        /// <summary>A coin price was shown that the player could not pay (a turret, a star, a keeper level).</summary>
        Shortfall,
    }

    /// <summary>The permanent spellings of <see cref="OfferTrigger"/>.</summary>
    public static class OfferTriggers
    {
        public static readonly OfferTrigger[] All =
        {
            OfferTrigger.Hub, OfferTrigger.Session, OfferTrigger.Win, OfferTrigger.Chapter,
            OfferTrigger.Loss, OfferTrigger.NearMiss, OfferTrigger.Shortfall,
        };

        public static string Id(OfferTrigger moment)
        {
            switch (moment)
            {
                case OfferTrigger.Hub: return "hub";
                case OfferTrigger.Session: return "session";
                case OfferTrigger.Win: return "win";
                case OfferTrigger.Chapter: return "chapter";
                case OfferTrigger.Loss: return "loss";
                case OfferTrigger.NearMiss: return "near_miss";
                case OfferTrigger.Shortfall: return "shortfall";
                default: return "none";
            }
        }

        public static OfferTrigger Parse(string id)
        {
            foreach (var moment in All)
                if (string.Equals(Id(moment), id, StringComparison.Ordinal)) return moment;
            return OfferTrigger.None;
        }
    }

    /// <summary>
    /// What a segment rule can ask about a player (invariant 64): <b>a measure is code, a threshold is
    /// content</b> - the rank ladder's split (52a). Every measure is a whole number read off this
    /// device, the save or the server's wallet reply (<see cref="OfferProfile"/>).
    /// </summary>
    public enum OfferMeasure
    {
        None = 0,

        /// <summary>Whole days since this account was first seen on this device (0 on the first day).</summary>
        DaysPlaying,

        /// <summary>How many of the last seven days (today included) the game was opened on.</summary>
        ActiveDays7,

        /// <summary>Runs in the recent window (at most <see cref="OfferHistory.RunWindow"/>).</summary>
        Runs,

        /// <summary>Losses among the recent runs, as a whole percentage (0 with no runs).</summary>
        LossPercent,

        /// <summary>Losses in a row on the level last played.</summary>
        LossStreak,

        /// <summary>Rewarded videos finished in the last seven days.</summary>
        AdViews7,

        /// <summary>Real-money purchases the server has recorded for the account.</summary>
        Purchases,

        /// <summary>Everything those purchases cost, in reference US cents.</summary>
        SpendCents,

        /// <summary>The largest single purchase, in reference US cents (the price anchor).</summary>
        TopCents,

        /// <summary>Whole days since the last purchase, or <see cref="OfferProfile.Never"/> for none.</summary>
        DaysSincePurchase,

        /// <summary>Whole days between this session and the one before it (0 for a player who never left).</summary>
        DaysAway,

        /// <summary>Offers closed without buying, in a row.</summary>
        Dismissals,

        /// <summary>Coins short of the price just shown (0 when nothing was short).</summary>
        CoinGap,

        /// <summary>The keeper level the player stands at.</summary>
        KeeperLevel,

        /// <summary>Levels cleared.</summary>
        ClearedLevels,
    }

    /// <summary>The permanent spellings of <see cref="OfferMeasure"/>.</summary>
    public static class OfferMeasures
    {
        public static readonly OfferMeasure[] All =
        {
            OfferMeasure.DaysPlaying, OfferMeasure.ActiveDays7, OfferMeasure.Runs, OfferMeasure.LossPercent,
            OfferMeasure.LossStreak, OfferMeasure.AdViews7, OfferMeasure.Purchases, OfferMeasure.SpendCents,
            OfferMeasure.TopCents, OfferMeasure.DaysSincePurchase, OfferMeasure.DaysAway, OfferMeasure.Dismissals,
            OfferMeasure.CoinGap, OfferMeasure.KeeperLevel, OfferMeasure.ClearedLevels,
        };

        public static string Id(OfferMeasure measure)
        {
            switch (measure)
            {
                case OfferMeasure.DaysPlaying: return "days_playing";
                case OfferMeasure.ActiveDays7: return "active_days_7";
                case OfferMeasure.Runs: return "runs";
                case OfferMeasure.LossPercent: return "loss_percent";
                case OfferMeasure.LossStreak: return "loss_streak";
                case OfferMeasure.AdViews7: return "ad_views_7";
                case OfferMeasure.Purchases: return "purchases";
                case OfferMeasure.SpendCents: return "spend_cents";
                case OfferMeasure.TopCents: return "top_cents";
                case OfferMeasure.DaysSincePurchase: return "days_since_purchase";
                case OfferMeasure.DaysAway: return "days_away";
                case OfferMeasure.Dismissals: return "dismissals";
                case OfferMeasure.CoinGap: return "coin_gap";
                case OfferMeasure.KeeperLevel: return "keeper_level";
                case OfferMeasure.ClearedLevels: return "cleared_levels";
                default: return "none";
            }
        }

        public static OfferMeasure Parse(string id)
        {
            foreach (var measure in All)
                if (string.Equals(Id(measure), id, StringComparison.Ordinal)) return measure;
            return OfferMeasure.None;
        }
    }

    /// <summary>
    /// How an offer's panel is titled and dressed. <b>A fixed list, so a published offer can only
    /// wear words the build already carries in every language</b> (6): a tuning names a look, never a
    /// string. The loc keys are <c>ui.offer.look.{id}</c>, written out in <see cref="OfferLooks.TitleKey"/>.
    /// </summary>
    public enum OfferLook
    {
        None = 0,
        Special,
        Starter,
        Rescue,
        Boost,
        Comeback,
        Value,
    }

    /// <summary>The permanent spellings of <see cref="OfferLook"/> and the title each one wears.</summary>
    public static class OfferLooks
    {
        public static readonly OfferLook[] All =
        {
            OfferLook.Special, OfferLook.Starter, OfferLook.Rescue, OfferLook.Boost, OfferLook.Comeback,
            OfferLook.Value,
        };

        public static string Id(OfferLook look)
        {
            switch (look)
            {
                case OfferLook.Special: return "special";
                case OfferLook.Starter: return "starter";
                case OfferLook.Rescue: return "rescue";
                case OfferLook.Boost: return "boost";
                case OfferLook.Comeback: return "comeback";
                case OfferLook.Value: return "value";
                default: return "none";
            }
        }

        public static OfferLook Parse(string id)
        {
            foreach (var look in All)
                if (string.Equals(Id(look), id, StringComparison.Ordinal)) return look;
            return OfferLook.None;
        }

        /// <summary>The panel's title. Written out, never built (6), so the loc scanner sees every key.</summary>
        public static string TitleKey(OfferLook look)
        {
            switch (look)
            {
                case OfferLook.Starter: return "ui.offer.look.starter";
                case OfferLook.Rescue: return "ui.offer.look.rescue";
                case OfferLook.Boost: return "ui.offer.look.boost";
                case OfferLook.Comeback: return "ui.offer.look.comeback";
                case OfferLook.Value: return "ui.offer.look.value";
                default: return "ui.offer.look.special";
            }
        }

        /// <summary>The line under the title. Written out for <see cref="TitleKey"/>'s reason.</summary>
        public static string LineKey(OfferLook look)
        {
            switch (look)
            {
                case OfferLook.Starter: return "ui.offer.line.starter";
                case OfferLook.Rescue: return "ui.offer.line.rescue";
                case OfferLook.Boost: return "ui.offer.line.boost";
                case OfferLook.Comeback: return "ui.offer.line.comeback";
                case OfferLook.Value: return "ui.offer.line.value";
                default: return "ui.offer.line.special";
            }
        }
    }

    /// <summary>
    /// Which of an offer's products is dealt: the first the store can sell, or - for an answer to a
    /// coin shortfall - the cheapest whose coins cover the gap.
    /// </summary>
    public enum OfferPick
    {
        First = 0,
        Gap,
    }
}
