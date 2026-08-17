namespace GlimmerGrove.Social
{
    /// <summary>
    /// How good a standing is, coarsely. <see cref="None"/> is not drawn.
    ///
    /// <para>
    /// Values are explicit and permanent because they order: <c>Top10 &gt; Top25</c> is
    /// meaningful and is relied on. Nothing keys on them, so they are free to gain a
    /// member above <see cref="Top10"/> if the ladder is ever extended.
    /// </para>
    /// </summary>
    public enum RankBand : byte { None = 0, Top50 = 1, Top25 = 2, Top10 = 3 }

    /// <summary>
    /// Turns a stored standing into the band a map node wears.
    ///
    /// <para>
    /// <b>A band rather than the number.</b> The percentile behind it is a sample of a
    /// live population, so the raw figure moves for reasons that have nothing to do with
    /// the player — <c>publishGroveStats</c> reads five thousand fresh saves a day. A
    /// band absorbs that: the population has to shift a great deal to move somebody a
    /// whole tier, so what is painted on the map reads as a rank the player holds rather
    /// than as a statistic that twitches. It is also the only form in which the claim is
    /// safe to shorten — "top 10%" needs no sample size beside it to be true.
    /// </para>
    /// <para>
    /// <b>Why this is safe to store at all.</b> A standing is only ever promoted — see
    /// <see cref="Persistence.LevelRecord.WithRank"/> — so the number under the band is
    /// the best the player has ever held, against any population that was ever
    /// published. That is what makes it honest as a permanent mark: it cannot sag while
    /// the player is away, and beating their own move count can never lower it. The two
    /// alternatives both fail, and it is worth writing down which way. Recomputing live
    /// means a node quietly reading worse than it did last month. Freezing whatever was
    /// current when the record was set means a player who improves from twenty moves to
    /// fifteen against a bigger, better population is *demoted for playing better*,
    /// which is the one thing a progress display must never do.
    /// </para>
    /// <para>
    /// The thresholds live here rather than beside the screen because they decide what a
    /// player is told about themselves, which is a rule and not a layout. They are also
    /// what the tests pin.
    /// </para>
    /// </summary>
    public static class RankTier
    {
        /// <summary>
        /// Percent-slower floors, descending. Aligned with
        /// <see cref="LevelStats.IsWorthSaying"/> at the bottom end on purpose: the map
        /// and the victory panel draw the same comparison, so a glade that earned a line
        /// on the win screen is exactly one that wears a band on the map, and a player
        /// never sees the two disagree about whether they did well.
        /// </summary>
        public const int Top10Floor = 90;
        public const int Top25Floor = 75;
        public const int Top50Floor = 50;

        /// <summary>
        /// The band a stored standing falls in.
        ///
        /// <para>
        /// Zero — the value <c>JsonUtility</c> leaves in a field an older save never
        /// wrote — lands in <see cref="RankBand.None"/> without a special case, which is
        /// the whole reason this field needed no migration. So does the -1 that
        /// <see cref="LevelStats.PercentSlower"/> returns when it has nothing to say.
        /// </para>
        /// </summary>
        public static RankBand Of(int rank)
        {
            if (rank >= Top10Floor) return RankBand.Top10;
            if (rank >= Top25Floor) return RankBand.Top25;
            if (rank >= Top50Floor) return RankBand.Top50;
            return RankBand.None;
        }

        /// <summary>Whether this standing is worth drawing anywhere.</summary>
        public static bool IsShown(int rank) => Of(rank) != RankBand.None;

        /// <summary>
        /// The label for a band.
        ///
        /// <para>
        /// Written out rather than assembled from the enum name or the threshold, for the
        /// reason <c>WinOverlay.RankKeys</c> is: a key that only exists at runtime is
        /// invisible to the build's string checker and ships missing in whichever
        /// language nobody tested. <see cref="RankBand.None"/> has no key because it has
        /// no label — asking for one is a caller that forgot to check.
        /// </para>
        /// </summary>
        public static string KeyOf(RankBand band)
        {
            switch (band)
            {
                case RankBand.Top10: return "ui.rank.top10";
                case RankBand.Top25: return "ui.rank.top25";
                case RankBand.Top50: return "ui.rank.top50";
                default: return null;
            }
        }
    }
}
