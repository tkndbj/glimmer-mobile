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

        /// <summary>
        /// The companion roster, in display order.
        ///
        /// Lives in the manifest rather than a body of its own because the whole roster
        /// is wanted at once — the picker draws the locked ones too — and an entry is a
        /// few dozen bytes, so a hundred companions is a few kilobytes on a file the
        /// boot path already reads. A lazily-loaded companion file would add a read to
        /// a screen and save nothing.
        ///
        /// Optional, and deliberately so: this was added without raising
        /// <see cref="ContentSchema.Version"/> because an older client simply ignores
        /// the field and falls back to its built-in roster, which is a working game
        /// rather than a refused manifest.
        /// </summary>
        public ManifestCompanionDto[] companions;
    }

    /// <summary>
    /// One companion a player can wear on their profile.
    ///
    /// <see cref="id"/> is permanent — it is written into save files and will key
    /// analytics and, once the shop exists, purchases. Renaming one is the same class
    /// of mistake as renaming a level id.
    /// </summary>
    [Serializable]
    public sealed class ManifestCompanionDto
    {
        public string id;

        /// <summary>
        /// Sprite key under <c>Art/Companions/</c>. Kept separate from the id so art can
        /// be re-cut or re-named without the change reaching a single save file.
        /// Empty means "same as the id", which is the case for every companion so far.
        /// </summary>
        public string portrait;

        /// <summary>
        /// Optional sprite-set key under <c>Art/Critters/</c> for companions that also
        /// appear animated on a board. Most have none, and a still portrait is all the
        /// profile ever needs.
        /// </summary>
        public string animated;

        /// <summary>Keeper level this unlocks at. 0 means available from the first launch.</summary>
        public int unlockLevel;

        /// <summary>Set true to retire a companion without deleting anyone's choice of it.</summary>
        public bool disabled;
    }

    [Serializable]
    public sealed class ManifestChapterDto
    {
        public string id;

        /// <summary>Bumped whenever the chapter file changes, so the cache knows to refetch.</summary>
        public int version;

        /// <summary>
        /// Sort order across chapters. Leave gaps so later chapters can slot in.
        ///
        /// The manifest is the only place order is written. That is what lets a chapter
        /// be reordered, or a new one slotted between two shipped ones, by pushing this
        /// one small file — without reshipping a single chapter body.
        /// </summary>
        public int order;

        /// <summary>Set false to retire a chapter without deleting it from the server.</summary>
        public bool disabled;

        /// <summary>Minimum app build that may load this chapter. 0 means anything.</summary>
        public int minAppVersion;

        /// <summary>
        /// This chapter's level ids, in play order. Present in the manifest so the boot
        /// path can know the whole game's shape — which glades exist, in what order,
        /// belonging to which chapter — after reading one small file, instead of opening
        /// and parsing every chapter body on every launch.
        ///
        /// It is the authority on membership and order; the chapter body is the
        /// authority on what each level actually is. Nobody writes this list by hand —
        /// <c>Content ▸ Sync Manifest</c> derives it from the bodies, so the two cannot
        /// drift, and the build gate proves they have not.
        /// </summary>
        public string[] levels;
    }

    [Serializable]
    public sealed class ChapterDto
    {
        public int schemaVersion;
        public string id;
        public string nameKey;

        /// <summary>
        /// A tripwire, not a setting. Order lives in the manifest — see
        /// <see cref="ManifestChapterDto.order"/> — and a chapter that tried to state
        /// its own would be a second source of truth for where the game goes next.
        ///
        /// The field is kept only so validation can see a stale one and fail the build
        /// with an explanation, rather than JsonUtility silently discarding it and the
        /// author believing a number that does nothing.
        /// </summary>
        public int order;

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

        /// <summary>
        /// Turns allowed before the run is lost, as a multiple of par. 0 takes the
        /// default; a negative value removes the budget entirely, which is the only way
        /// to author a glade that cannot be lost on moves.
        /// </summary>
        public float budgetFactor;

        // ---- presentation, all optional ------------------------------------
        public float mapX;
        public float mapY;
        public string accent;
        public string slate;
        public string backdrop;

        // ---- text ------------------------------------------------------------
        // Deliberately absent. A level's loc keys are derived from its id — see
        // LevelDefinition.DefaultNameKey — so that anything holding a LevelId can name
        // a glade without reading this file. Overridable keys would have made the
        // manifest index insufficient for the map and the home screen.
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

        /// <summary>
        /// The daily chest table. Optional: absent means the built-in one stands.
        ///
        /// It rides here rather than in a file of its own because it is tuned in the same
        /// sitting as the reward rates — pulling one lever without seeing the other is how
        /// an economy ends up paying twice — and because a second file would be a second
        /// fetch on a phone for a few hundred bytes.
        /// </summary>
        public DailyChestDto daily;
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

    /// <summary>
    /// The daily chest table: how much play earns a chest, and what each one holds.
    ///
    /// <para>
    /// Authored as data because drop rates are the most retuned numbers in a live game,
    /// and because they are the numbers most likely to be asked about. Apple's review
    /// guidelines and several jurisdictions require published odds for randomised
    /// rewards; keeping the weights in a file means the disclosure is generated from the
    /// same source the game rolls against, rather than written by hand and drifting.
    /// </para>
    /// <para>
    /// These chests are <b>earned by playing and cannot be bought</b>, which is what keeps
    /// them outside loot-box rules almost everywhere rather than merely compliant with
    /// them. Nothing in this file should ever gain a price.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class DailyChestDto
    {
        /// <summary>Runs finished per chest earned. Three by default.</summary>
        public int runsPerChest = -1;

        /// <summary>In order, easiest first. The last one is the day's prize.</summary>
        public DailyChestEntryDto[] chests;
    }

    [Serializable]
    public sealed class DailyChestEntryDto
    {
        /// <summary>Paid every time. A chest with none of these is rejected by the reader.</summary>
        public DailyDropDto[] guaranteed;

        /// <summary>Exactly one of these is picked, by weight. May be empty.</summary>
        public DailyOptionDto[] options;
    }

    /// <summary>
    /// One reward band. <c>kind</c> is a permanent id — <c>credits</c>, <c>gems</c>,
    /// <c>hearts</c>, <c>heart_boost</c> — and <c>heart_boost</c> is measured in hours.
    /// </summary>
    [Serializable]
    public sealed class DailyDropDto
    {
        public string kind;
        public int min;
        public int max;
    }

    /// <summary>
    /// A band with a weight.
    ///
    /// Declares its own fields rather than inheriting <see cref="DailyDropDto"/>, for the
    /// reason <see cref="ChapterRewardDto"/> gives: a serialiser behaviour whose failure
    /// is silent has no business holding up an odds table.
    /// </summary>
    [Serializable]
    public sealed class DailyOptionDto
    {
        public string kind;
        public int min;
        public int max;

        /// <summary>Relative chance. The reader rejects anything below 1.</summary>
        public int weight;

        public DailyDropDto AsBand() => new DailyDropDto { kind = kind, min = min, max = max };
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
