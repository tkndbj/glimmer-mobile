using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The wire format, kept separate from the runtime model on purpose.
    ///
    /// These types mirror the JSON exactly and nothing else depends on them, so the
    /// file format can gain fields, rename things or grow a v2 without any of the
    /// game's logic changing shape. They are written for Unity's JsonUtility, which
    /// means public fields, no dictionaries and no properties — and, usefully, it
    /// silently ignores unknown fields, which is precisely the forward compatibility
    /// an old client needs when it meets newer content.
    ///
    /// Empty or zero always means "not specified, fall back" rather than a real
    /// value, so a content author only writes what actually differs.
    /// </summary>
    [Serializable]
    public sealed class ManifestDto
    {
        public int schemaVersion;
        public ManifestChapterDto[] chapters;

        /// <summary>
        /// Bumped when <c>progression.json</c> changes, so the refresher knows to pull
        /// it. Without a version the reward table could only be retuned by shipping a
        /// build, which is the thing keeping it in content was meant to avoid.
        /// 0 means never versioned, and the bundled copy stands.
        /// </summary>
        public int progressionVersion;
    }

    [Serializable]
    public sealed class ManifestChapterDto
    {
        public string id;

        /// <summary>Bumped whenever the chapter file changes, so the cache knows to refetch.</summary>
        public int version;

        /// <summary>Sort order across chapters. Leave gaps so later chapters can slot in.</summary>
        public int order;

        /// <summary>Set false to retire a chapter without deleting it from the server.</summary>
        public bool disabled;

        /// <summary>Minimum app build that may load this chapter. 0 means anything.</summary>
        public int minAppVersion;
    }

    [Serializable]
    public sealed class ChapterDto
    {
        public int schemaVersion;
        public string id;
        public int order;
        public string nameKey;

        // shared art, inherited by every level in the chapter
        public string accent;
        public string slate;
        public string backdrop;

        /// <summary>Map strips stacked bottom to top, forming this chapter's road.</summary>
        public string[] mapStrips;

        public LevelDto[] levels;
    }

    [Serializable]
    public sealed class LevelDto
    {
        public string id;

        // ---- layout, frozen once shipped -----------------------------------
        public int width;
        public int height;
        public string[] rows;

        // ---- tuning, safe to change after launch ---------------------------
        /// <summary>Leave at 0 to derive par from the board, which is always correct.</summary>
        public int par;
        public float goldFactor;
        public float silverFactor;
        public int hintAllowance;

        // ---- presentation, all optional ------------------------------------
        public float mapX;
        public float mapY;
        public string accent;
        public string slate;
        public string backdrop;

        // ---- text, referenced by key and never shown raw --------------------
        public string nameKey;
        public string taglineKey;
        public string lessonKey;
    }

    /// <summary>
    /// The XP curve and what a level pays out.
    ///
    /// Authored as increments rather than cumulative totals, because inserting a
    /// level band then changes one number instead of every number after it. The
    /// table covers the hand-tuned early game; <see cref="tailXpToNext"/> and
    /// <see cref="tailXpIncrement"/> continue it arithmetically forever, so the
    /// curve can never simply run out under a long-lived player.
    /// </summary>
    [Serializable]
    public sealed class ProgressionDto
    {
        public int schemaVersion;

        /// <summary>Level cap. Bounds the derivation loop as well as the design.</summary>
        public int maxLevel;

        /// <summary>xpToNext[0] is the cost of reaching level 2 from level 1.</summary>
        public int[] xpToNext;

        /// <summary>Cost of the first level past the authored table.</summary>
        public int tailXpToNext;

        /// <summary>Added to the tail cost for each level beyond that.</summary>
        public int tailXpIncrement;

        public RewardRuleDto rewards;

        /// <summary>Per-chapter overrides. A chapter with no entry uses the defaults.</summary>
        public ChapterRewardDto[] chapterRewards;
    }

    /// <summary>
    /// What one level pays out, as a function of the best result held on it.
    ///
    /// -1 means "not written, inherit" rather than zero, because zero is a legitimate
    /// payout for a tutorial chapter and the two must stay distinguishable.
    /// </summary>
    [Serializable]
    public sealed class RewardRuleDto
    {
        public int xpFirstClear = -1;
        public int xpPerStar = -1;
        public int creditsFirstClear = -1;
        public int creditsPerStar = -1;
    }

    /// <summary>
    /// Declares its own fields rather than inheriting them from <see cref="RewardRuleDto"/>.
    ///
    /// JsonUtility does read inherited fields, but relying on it here would mean the
    /// whole reward-override feature rested on a serialiser behaviour whose failure is
    /// silent — every chapter would quietly pay the default rate, and nothing in the
    /// file or the console would say so. Four repeated lines buy certainty.
    /// </summary>
    [Serializable]
    public sealed class ChapterRewardDto
    {
        public string chapterId;

        public int xpFirstClear = -1;
        public int xpPerStar = -1;
        public int creditsFirstClear = -1;
        public int creditsPerStar = -1;

        public RewardRuleDto AsRule() => new RewardRuleDto
        {
            xpFirstClear = xpFirstClear,
            xpPerStar = xpPerStar,
            creditsFirstClear = creditsFirstClear,
            creditsPerStar = creditsPerStar,
        };
    }

    [Serializable]
    public sealed class LocTableDto
    {
        public string language;
        public LocEntryDto[] entries;
    }

    [Serializable]
    public sealed class LocEntryDto
    {
        public string key;
        public string text;
    }
}
