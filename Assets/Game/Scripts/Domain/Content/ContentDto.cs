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

        /// <summary>
        /// The event calendar, in no particular order.
        ///
        /// Lives in the manifest for the same reason the companion roster does — the whole
        /// calendar is wanted at once, an entry is a few dozen bytes, and the boot path
        /// already reads this file — and for one more besides: an event's reward is derived
        /// from the star ledger, so the definitions have to be resident wherever credits
        /// are computed, which is everywhere.
        ///
        /// Optional, and deliberately so: this was added without raising
        /// <see cref="ContentSchema.Version"/> because an older client ignores the field
        /// and simply never runs an event, which is a working game rather than a refused
        /// manifest.
        /// </summary>
        public ManifestEventDto[] events;
    }

    /// <summary>
    /// One time-boxed run at a set of glades.
    ///
    /// <see cref="id"/> is permanent: it names the event's loc keys and will key its
    /// analytics, and a player's earned credits depend on it through the reward track.
    /// Renaming one is the same class of mistake as renaming a level id — it does not
    /// break anything visibly, it silently un-pays everybody who finished it.
    /// </summary>
    [Serializable]
    public sealed class ManifestEventDto
    {
        public string id;

        /// <summary>Unix seconds, inclusive. Compared against the trusted clock, never the device's.</summary>
        public long startUnix;

        /// <summary>Unix seconds, exclusive.</summary>
        public long endUnix;

        /// <summary>Set true to pull an event without deleting anyone's progress through it.</summary>
        public bool disabled;

        /// <summary>
        /// Which mark the event wears. Optional; empty draws the default.
        ///
        /// <para>
        /// It names <b>a mark the client knows how to draw</b>, not a sprite file, and that
        /// distinction is the whole design. An arbitrary art path could not work here:
        /// invariant 7 routes every sprite through <c>AssetLibrary</c> and <c>AssetManifest</c>
        /// decides what is registered, so a filename invented in a content push would resolve
        /// to nothing and the box would draw a white rectangle. A named mark degrades the
        /// other way — an unknown one falls back to the default, which is a working screen.
        /// </para>
        /// <para>
        /// So this buys a real content lever without lying about its reach: an event can pick
        /// any mark the shipped client already has, and a genuinely new one is an app update,
        /// which is honest because a new mark is new art either way.
        /// </para>
        /// </summary>
        public string icon;

        /// <summary>
        /// The glades this event runs over, by permanent id. They may belong to any
        /// chapter — an event is a lens on the catalog, not a chapter of its own, which is
        /// what lets one be run without shipping any new content at all.
        /// </summary>
        public string[] levels;

        /// <summary>The reward track, lowest goal first.</summary>
        public ManifestEventMilestoneDto[] milestones;
    }

    /// <summary>
    /// One rung: finish <c>goal</c> of the event's glades inside the window, earn
    /// <c>credits</c>. Credits and nothing else — see <c>EventLedger</c> for why a track
    /// that paid anything the server cannot re-derive could not be paid at all.
    /// </summary>
    [Serializable]
    public sealed class ManifestEventMilestoneDto
    {
        public int goal;
        public int credits;
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

        /// <summary>
        /// Credits that buy this companion outright, ignoring <see cref="unlockLevel"/>.
        /// <b>Zero or absent means it cannot be bought at all</b> — level only.
        ///
        /// <para>
        /// That sentinel is the safe direction, and it is chosen rather than inherited.
        /// <c>JsonUtility</c> writes a zero into every field an older manifest never had, so
        /// "absent" and "free" would be the same value if free were the meaning — and a
        /// manifest from before this field existed would put the entire roster on sale for
        /// nothing. Reading zero as "not for sale" makes a forgotten price cost a purchase
        /// nobody could make instead of giving away thirty companions, and it leaves
        /// "earnable only by playing" expressible, which is a legitimate thing to author.
        /// </para>
        /// <para>
        /// A price is a property of the companion rather than a row in
        /// <c>progression.json</c>, for the reason <see cref="unlockLevel"/> is: adding one
        /// is a portrait, a manifest row and a loc string, and splitting half a companion
        /// into a second published file would let a drop ship a roster whose prices had not
        /// arrived yet.
        /// </para>
        /// </summary>
        public int unlockCost;

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

        /// <summary>
        /// Seconds of clock per par turn. 0 takes the default; a negative value removes
        /// the timer entirely, which is the only way to author an untimed glade — a
        /// tutorial board, say, where a countdown teaches the wrong lesson.
        /// </summary>
        public float timeFactor;

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

        /// <summary>
        /// What rewarded ads pay. Optional: absent means the built-in table stands.
        ///
        /// Rides here for the same two reasons the daily block does — it is tuned in the
        /// same sitting as the chest rates and the reward curve, and a second file would
        /// be a second fetch on a phone for a few hundred bytes. It is also the block most
        /// likely to be changed alone, because the numbers that justify it (fill rate,
        /// eCPM) only exist once the game is live in a market.
        /// </summary>
        public AdsDto ads;

        /// <summary>
        /// What a run of consecutive days pays. Optional: absent means the built-in ladder
        /// stands.
        ///
        /// Rides here for the same reasons the two blocks above it do, plus one of its
        /// own: a streak reward and a chest reward are competing for the same evening, so
        /// tuning either without seeing the other is how a player ends up with no reason
        /// to open the game twice.
        /// </summary>
        public StreakDto streak;

        /// <summary>
        /// The golden bonus bands. Optional: absent means the built-in table stands.
        ///
        /// This is the one block that changes what an ordinary glade is worth, so it is
        /// tuned against the reward rule directly above it and never in isolation — the
        /// average multiplier and the credits-per-star are one number seen from two sides.
        /// </summary>
        public GoldenDto golden;

        /// <summary>
        /// The heart gate. Optional: absent means the built-in numbers stand.
        ///
        /// Rides here rather than in a file of its own for the reasons every block above it
        /// does, and one that is sharper than any of them: the gate decides how many
        /// sessions a player gets, so it multiplies every other number in this file. Tuning
        /// the reward curve without seeing the refill rate is tuning the pay-per-session of
        /// a game whose session count you just changed.
        /// </summary>
        public HeartsDto hearts;
    }

    /// <summary>
    /// How many hearts a player may hold and how fast they come back.
    ///
    /// <para>
    /// Every field is -1 for "not written, inherit", the same tri-state the reward rules
    /// use, because zero is a meaningful value for none of them and the difference between
    /// "the author set this to zero" and "the author said nothing" has to survive.
    /// </para>
    /// <para>
    /// Note what is deliberately absent: anything about <em>buying</em> hearts. The refill
    /// timer and the collect-past-the-cap ceiling are the only two ways hearts arrive
    /// today, and a price authored here would make the gate a storefront without anybody
    /// having designed one.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class HeartsDto
    {
        /// <summary>Where the refill timer stops. This is the number that paces free play.</summary>
        public int refillCap = -1;

        /// <summary>
        /// The most a player may hold once collected hearts stack on top. Safe to lower:
        /// it refuses new grants and never confiscates. See <c>HeartLimits.HardCeiling</c>.
        /// </summary>
        public int ceiling = -1;

        /// <summary>Seconds between refills.</summary>
        public int refillSeconds = -1;

        /// <summary>
        /// Seconds between refills while a heart boost runs. The reader holds this at
        /// <see cref="refillSeconds"/> if it is authored longer — a boost that slows hearts
        /// down is the feature working backwards.
        /// </summary>
        public int boostedRefillSeconds = -1;

        /// <summary>Longest boost window any single award may leave running, in hours.</summary>
        public int maxBoostHours = -1;

        /// <summary>Hearts one lost run costs.</summary>
        public int defeatCost = -1;
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

    /// <summary>
    /// The golden bonus: how often a glade pays more than the reward rule says, and by
    /// how much.
    ///
    /// <para>
    /// Order does not matter here — unlike the streak ladder, where position is the day —
    /// because a band is identified by its own percentage rather than by where it sits.
    /// The odds are the weights normalised, which is what lets them be printed as a list
    /// that sums to a hundred.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GoldenDto
    {
        public GoldenBandDto[] bands;
    }

    /// <summary>
    /// One outcome. <c>percent</c> is a multiplier on the glade's ordinary credit reward
    /// and <b>may never be below 100</b> — the bonus only ever adds. See <c>GoldenRules</c>.
    /// </summary>
    [Serializable]
    public sealed class GoldenBandDto
    {
        public int percent;
        public int weight;
    }

    /// <summary>
    /// The streak ladder: one entry per consecutive day, in order.
    ///
    /// Position <em>is</em> the day, which is why <c>StreakTable</c> refuses the whole
    /// block on a bad entry rather than skipping it the way the ads table does — dropping
    /// one rung renumbers every day above it and quietly changes what the player is owed.
    /// </summary>
    [Serializable]
    public sealed class StreakDto
    {
        /// <summary>
        /// Night one first. An entry with no <c>kind</c> pays nothing, which is how a night
        /// that only marks time is authored.
        ///
        /// The list is one <em>lap</em> rather than the whole ladder: night eight pays what
        /// night one pays, for ever. So its length is also the length of the board a player
        /// sees, and lengthening it lengthens the week.
        /// </summary>
        public StreakRungDto[] rungs;
    }

    /// <summary>
    /// One day of the streak ladder. <c>kind</c> reuses the chest drop vocabulary, and
    /// <c>amount</c> reads as hearts or as hours depending on which.
    ///
    /// <b>Currency is allowed, and it is adjudicated.</b> A currency rung is claimed as
    /// <c>streak:{day}:{night}:{currency}</c> and paid from the server's own copy of this
    /// ladder, so retuning it here and forgetting to re-seed means the server pays the old
    /// figure — see <c>StreakTable</c> for the whole path, and run the seeder after any
    /// change. The per-kind ceilings in <c>StreakRules</c> apply on both sides.
    /// </summary>
    [Serializable]
    public sealed class StreakRungDto
    {
        public string kind;
        public int amount;
    }

    /// <summary>
    /// What rewarded ads pay, and how often one may be watched.
    ///
    /// <para>
    /// Authored as data because a rewarded payout is the lever that balances ad revenue
    /// against the heart gate, and it is tuned against numbers nobody has until the game
    /// is live in a market. A payout that needs a store review to change is a payout that
    /// gets set once, from a guess, and never corrected.
    /// </para>
    /// <para>
    /// Note what is deliberately absent: a price. These are <b>earned by watching and
    /// cannot be bought</b>, exactly like the daily chests, and for the same reason —
    /// nothing here should ever gain a cost in currency, because that would turn a
    /// rewarded ad into a purchase of a randomised outcome.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AdsDto
    {
        /// <summary>
        /// Seconds between two rewarded ads, across every placement. -1 inherits.
        ///
        /// Global rather than per-placement on purpose. The thing being paced is the
        /// player's tolerance for watching videos, and that is not divided up by which
        /// button started one.
        /// </summary>
        public int cooldownSeconds = -1;

        /// <summary>
        /// One entry per offered placement. A placement with no entry is switched off,
        /// which is how an offer is withdrawn without a build.
        /// </summary>
        public AdPlacementDto[] placements;
    }

    /// <summary>
    /// One rewarded placement. <c>id</c> is a permanent placement id — <c>heart_refill</c>,
    /// <c>coin_bonus</c> — and <c>kind</c> reuses the drop vocabulary of the chest table,
    /// so <c>heart_boost</c> is measured in hours here too.
    /// </summary>
    [Serializable]
    public sealed class AdPlacementDto
    {
        public string id;
        public string kind;

        /// <summary>How much of <see cref="kind"/> one finished view pays.</summary>
        public int amount;

        /// <summary>
        /// Views that pay, per UTC day. The reader rejects anything below 1 — to switch a
        /// placement off, remove it, rather than leaving an entry that pays nothing.
        /// </summary>
        public int dailyCap;
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
