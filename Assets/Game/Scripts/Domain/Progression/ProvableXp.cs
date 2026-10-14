using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The XP an account can prove: the star ledger, the Infinite lane, the daily challenges and
    /// the boost clamped against those three - the sum <c>publishGrove</c> derives before it
    /// reads a keeper level off the curve, and the sum <see cref="PlayerProgression"/> derives
    /// before it applies the device's high-water floors.
    ///
    /// <para>
    /// <b>Written once and read from two sources</b>, the shape <c>Ranks.IRankSource</c> has:
    /// <see cref="Of"/> is the rule, over four figures, and both the live ledgers
    /// (<c>PlayerProgression.EnsureFresh</c>) and a save file (<see cref="In"/>) hand their
    /// figures to it. Each addend already has a server twin held by shared vectors
    /// (<c>ProgressionLedger</c> 9a, <c>EndlessRewardTable</c> 9d, <c>XpBoost.BonusFrom</c> 9e,
    /// <c>ChallengeRewardRule</c> 56i); this is only the sum, and it is the sum the server takes.
    /// </para>
    /// <para>
    /// <b>No floor is applied here, deliberately.</b> <c>ProgressionStore</c>'s high-water marks
    /// keep a player from watching their level fall when levels leave the catalog (invariant 9),
    /// and they are client-written, so the server never reads them. A level shown to
    /// <em>strangers</em> is the server's, so anything that predicts what the server will
    /// publish reads this and never <see cref="PlayerProgression.Level"/>.
    /// </para>
    /// </summary>
    public readonly struct ProvableXp
    {
        public readonly long Stars;
        public readonly long Endless;
        public readonly long Challenges;
        public readonly long Boost;

        ProvableXp(long stars, long endless, long challenges, long boost)
        {
            Stars = stars;
            Endless = endless;
            Challenges = challenges;
            Boost = boost;
        }

        public long Total => Stars + Endless + Challenges + Boost;

        /// <summary>
        /// The rule. The boost is clamped against the three sources it could have been a
        /// percentage of, exactly as <c>xpBoostXp</c> clamps it on the server.
        /// </summary>
        public static ProvableXp Of(long starXp, long lifetimeWaves, long lifetimeClears,
                                    long boostHeld, ProgressionTable table,
                                    ChallengeRewardRule challengeRewards)
        {
            table ??= ProgressionTable.Default;

            long stars = starXp > 0L ? starXp : 0L;
            long endless = table.Endless.XpFor(lifetimeWaves);
            long challenges = challengeRewards != null ? challengeRewards.XpFor(lifetimeClears) : 0L;
            long boost = XpBoost.BonusFrom(stars + endless + challenges, boostHeld,
                                           table.XpBoost.MaxPercent);

            return new ProvableXp(stars, endless, challenges, boost);
        }

        /// <summary>
        /// The same figure off a save file - the file a sync has just agreed with the server,
        /// which is the one the server will build a card from.
        ///
        /// <para>
        /// Every reading is the file's: records through <see cref="LevelRecord.TryFromDto"/> and
        /// <see cref="ProgressionLedger.Compute"/> (the catalog bound and the star clamp), waves
        /// through <see cref="EndlessLedger.LifetimeWavesIn"/>, clears through
        /// <see cref="ChallengeLedger.LifetimeClearsIn"/>, the banked bonus off the file's
        /// wallet. A null catalog answers nought for the star ledger, as
        /// <see cref="ProgressionLedger.Compute"/> does for the live one.
        /// </para>
        /// </summary>
        public static ProvableXp In(SaveFileDto save, IChapterMap chapters, ProgressionTable table,
                                    ChallengeRewardRule challengeRewards)
        {
            long starXp = 0L;

            var rows = save?.levels;
            if (rows != null && chapters != null)
            {
                var records = new List<LevelRecord>(rows.Length);
                foreach (var row in rows)
                    if (LevelRecord.TryFromDto(row, out var record)) records.Add(record);

                // The player key seeds the golden bonus, which multiplies credits and never XP,
                // so the XP read here does not depend on it.
                starXp = ProgressionLedger.Compute(records, chapters, table).Xp;
            }

            return Of(starXp,
                      EndlessLedger.LifetimeWavesIn(save),
                      ChallengeLedger.LifetimeClearsIn(save),
                      save?.wallet?.xpBoostEarned ?? 0L,
                      table, challengeRewards);
        }
    }

    /// <summary>
    /// The keeper level the server publishes for a save: <see cref="ProvableXp"/> read off the
    /// curve, with no floor, plus the levels bought (invariant 57). <see cref="Earned"/> is what
    /// the rank ladder is asked about, <see cref="Level"/> is what the card carries - the split
    /// <c>publishGrove</c> makes between <c>earnedLevel</c> and <c>level</c>.
    /// </summary>
    public readonly struct ProvableLevel
    {
        public readonly PlayerLevel Earned;
        public readonly PlayerLevel Level;

        ProvableLevel(PlayerLevel earned, PlayerLevel level)
        {
            Earned = earned;
            Level = level;
        }

        public static ProvableLevel In(SaveFileDto save, IChapterMap chapters, ProgressionTable table,
                                       ChallengeRewardRule challengeRewards)
        {
            table ??= ProgressionTable.Default;

            var earned = table.LevelFor(ProvableXp.In(save, chapters, table, challengeRewards).Total);

            // The file's copy of the entitlement, which `ApplyServerState` folds the wallet's
            // `keeperBought` into; the server reads the wallet's own. Compose ignores nought and
            // below.
            int bought = save?.wallet?.keeperLevelsBought ?? 0;

            return new ProvableLevel(earned, KeeperLadder.Compose(earned, bought, table.MaxLevel));
        }
    }
}
