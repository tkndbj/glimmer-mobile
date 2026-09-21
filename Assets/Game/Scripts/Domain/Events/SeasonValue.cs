using GlimmerGrove.Daily;
using GlimmerGrove.Store;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Events
{
    /// <summary>
    /// What a season's pass is worth against what it costs, as a percentage.
    ///
    /// <para>
    /// <b>Derived on every read rather than written down.</b> This is the shop's
    /// <c>BonusPercent</c> argument (invariant 18b) asked of the one thing sold outside the
    /// shop: a card claiming a figure that a retune has moved is a promise the game is not
    /// keeping, and every input here — the pass price, the ladder's tiers, each tier's chest
    /// and the gem/credit rate — is content that moves without a build. Authoring the
    /// percentage would be a fifth opinion sitting between four files that already imply one.
    /// </para>
    /// <para>
    /// <b>It measures the paid column alone</b>, because that is what the purchase buys. The
    /// free column is paid whether or not anybody owns a pass, so counting it would inflate
    /// the badge by what the player already has (<c>GroveEvent.TierOn</c>'s split).
    /// </para>
    /// <para>
    /// <b>Integer arithmetic throughout, in thousandths.</b> A chest's expectation is a
    /// weighted mean, so it is genuinely fractional — and a float deciding a displayed figure
    /// is the one shape this project refuses outright, because .NET, Mono and IL2CPP round it
    /// three ways and the badge would read differently on a phone than in the mirror. Each
    /// chest is summed over twice its own total weight, which makes the halves exact.
    /// </para>
    /// <para>
    /// Note what this is <b>not</b>: it is not a price a player can pay in reverse. Gems never
    /// buy credits (<c>StoreGood</c>), so <see cref="StoreCatalog.CreditsPerGem"/> is a scale
    /// for comparing two heaps and nothing about it reaches an economy.
    /// </para>
    /// </summary>
    public static class SeasonValue
    {
        /// <summary>The fixed-point scale the running totals are kept in.</summary>
        const long Scale = 1000L;

        /// <summary>
        /// What the paid column pays, as a percentage of the pass price — 258 meaning the
        /// chests are worth about two and a half times what the pass costs.
        ///
        /// <b>Nought is a real answer and means "do not draw a badge"</b>: a season with no
        /// pass, no paid rungs, or one naming a tier this build's table has never heard of
        /// (<c>GroveEvent.TierOn</c> answers null there, deliberately). A badge is the wrong
        /// place to report a content mismatch, so it simply does not appear — both content
        /// gates already error on that file.
        /// </summary>
        public static int PassPercent(GroveEvent season)
        {
            if (season == null || !season.HasPremium) return 0;

            long credits = 0L, gems = 0L;

            for (int i = 0; i < season.Milestones.Count; i++)
            {
                ChestTier tier = season.Milestones[i].TierOn(SeasonTrack.Pass);
                if (tier == null || tier.Chest == null) return 0;

                Expect(tier.Chest, ref credits, ref gems);
            }

            long perGem = StoreRules.Catalog.CreditsPerGem;
            if (perGem < 1L) perGem = 1L;

            long worth = credits / perGem + gems;
            long percent = worth * 100L / (season.PassGems * Scale);

            return percent < 0L ? 0 : percent > int.MaxValue ? int.MaxValue : (int)percent;
        }

        /// <summary>
        /// Adds one chest's expectation, in thousandths, to the running totals.
        ///
        /// The chest's own shape does the work: every guaranteed band lands, and exactly one
        /// option is picked (<c>ChestDefinition</c>), so the options contribute their weighted
        /// mean and nothing else. Summing over <c>2 * TotalWeight</c> keeps the band midpoints
        /// exact, since a midpoint is the only half here.
        /// </summary>
        static void Expect(ChestDefinition chest, ref long credits, ref long gems)
        {
            long weight = chest.TotalWeight;
            if (weight < 1L) weight = 1L;

            long c = 0L, g = 0L;

            for (int i = 0; i < chest.Guaranteed.Count; i++)
            {
                ChestBand band = chest.Guaranteed[i];
                long span = (long)band.Min + band.Max;

                if (band.Kind == ChestDropKind.Credits) c += span * weight;
                else if (band.Kind == ChestDropKind.Gems) g += span * weight;
            }

            for (int i = 0; i < chest.Options.Count; i++)
            {
                ChestOption option = chest.Options[i];
                long span = (long)option.Band.Min + option.Band.Max;

                if (option.Band.Kind == ChestDropKind.Credits) c += span * option.Weight;
                else if (option.Band.Kind == ChestDropKind.Gems) g += span * option.Weight;
            }

            credits += c * Scale / (2L * weight);
            gems += g * Scale / (2L * weight);
        }
    }
}
