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

        /// <summary>
        /// Bumped when <c>homestead.json</c> changes, so the refresher knows to pull it.
        ///
        /// <para>
        /// One integer, and the grove catalog itself is a <b>body</b> — read when the player
        /// opens the Grovement and dropped when they leave, exactly like a chapter. That is
        /// invariant 4a applied to the thing most likely to break it: a shop is the part of a
        /// game that grows fastest, and two hundred pieces plus twenty plots of slots is tens
        /// of kilobytes. Carried in the manifest like the companion roster, it would be
        /// parsed at every launch on every device forever to answer a question nothing on the
        /// boot path asks. The roster is in the manifest because the hub has to draw a
        /// companion before anything else happens; nothing draws a fence until somebody taps
        /// a tab.
        /// </para>
        /// <para>
        /// Optional, and deliberately so: this was added without raising
        /// <see cref="ContentSchema.Version"/>, because an older client ignores the field and
        /// simply has no grove — a working game rather than a refused manifest.
        /// </para>
        /// </summary>
        public int groveVersion;
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

        /// <summary>
        /// How this chapter is played - "glade" (turn the conduits) or "chain" (drag through
        /// motes of light). Empty means glade, so every chapter authored before modes existed
        /// keeps working with its file untouched.
        ///
        /// <para>
        /// A chapter naming a mode this build cannot play is skipped whole, exactly as
        /// <see cref="minAppVersion"/> skips one needing newer code - and for the same reason.
        /// A mode is code, so content that names one this client lacks is content from the
        /// future, and the honest response is to lose that chapter rather than to open it into
        /// a screen that cannot run it.
        /// </para>
        /// </summary>
        public string mode;
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

        /// <summary>
        /// Where the end-of-chapter marker sits across the map, 0..1. Leave at 0 to take
        /// <see cref="ChapterMap.TeaserX"/>, which is the right answer for a chapter whose
        /// last glade is on the left.
        ///
        /// Only the across-axis is authorable: how far *up* the marker floats is derived
        /// from the highest glade and the header's clearance, and a typed one could drift
        /// off the top of a chapter that later gained a strip. The across-axis cannot —
        /// the map is one canvas width whatever the chapter's length.
        /// </summary>
        public float teaserX;

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

        /// <summary>
        /// Turns allowed before the run is lost, as a multiple of par. 0 takes the
        /// default; a negative value removes the budget entirely, which is the only way
        /// to author a glade that cannot be lost on moves.
        /// </summary>
        public float budgetFactor;

        // ---- presentation, all optional ------------------------------------
        // ---- the mode blocks ----------------------------------------------
        // A level carries exactly one of these, or none and a `rows` grid, which is a glade.
        // Which one it is decides how the level is played - see LevelModes. Adding a mode is a
        // field here plus a LevelMode subclass, and nothing else in the game changes.
        public FallDto fall;
        public KeeperDto keeper;
        public BudDto bud;

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
    /// <summary>
    /// Lightfall's well: how big it is, what is standing in it, and what it deals.
    ///
    /// <para>
    /// <b>There is no difficulty number here and there must never be one.</b> Par is the fewest
    /// drops that empty <see cref="rows"/> using <see cref="motes"/>, found by search
    /// (<c>FallSolver</c>), and both star lines and the supply the run is dealt are multiples of
    /// it. What an author tunes is the board: how big the well is, how much is standing in it,
    /// how close to the brim it starts and what order the colours arrive in.
    /// </para>
    /// <para>
    /// <b><c>seed</c> is retired.</b> The mode used to deal random colours from it, which is
    /// what made it a score attack rather than a level — a board with no fixed future cannot be
    /// searched, so it could author no goal, no budget and no star line, and two players on the
    /// same level were not playing the same level. The field is not re-pointed at anything: a
    /// chapter body carrying one is content written for a build that no longer exists, and
    /// <c>ContentValidation</c> says so rather than ignoring it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FallDto
    {
        public int width;
        public int height;

        /// <summary>
        /// What is standing in the well, top row first — one letter per column in R, G, B, Y, M
        /// and C, with '.' for empty ground. Spaces are ignored, so a row may be spaced out for
        /// reading.
        ///
        /// Row nought is the brim and must be empty: a mote that comes to rest there floods the
        /// well and ends the run, so a level that begins in that state begins lost.
        /// </summary>
        public string[] rows;

        /// <summary>
        /// The procession, in order, written in R, G and B. It repeats, so it never needs to be
        /// longer than one lap — and it must carry all three channels, or a mote missing one of
        /// them could never be finished however many drops were bought.
        ///
        /// Never a blend: the whole mode is that a blend has to be made.
        /// </summary>
        public string motes;

        /// <summary>
        /// Wasted drops this well forgives, above par. 0 takes <c>FallRules.DefaultSpare</c>,
        /// which is what every level ships with.
        ///
        /// A count rather than a factor, because a wrong drop costs about the same wherever it
        /// happens — it is gone, and the mote it left behind has to be cooked like any other —
        /// while a fraction of par gives a short well almost no room at all. See
        /// <c>LevelTuning.Slack</c>.
        /// </summary>
        public int spare;

        /// <summary>
        /// <b>Retired.</b> See the remarks above. Kept only so validation can name a stale one
        /// rather than JsonUtility silently discarding it and the author believing a number that
        /// does nothing — the same tripwire <see cref="ChapterDto.order"/> is.
        /// </summary>
        public int seed;

        /// <summary>
        /// Whether this block was authored. <b>Never test the block itself for null</b> -
        /// JsonUtility instantiates a [Serializable] class field on every level in the game, so
        /// absence has to be a value a real block cannot hold.
        /// </summary>
        public bool IsAuthored => width > 0 || height > 0;
    }

    /// <summary>
    /// Groovekeeper's grove: the ground, the beds that have to bloom, and the procession of tiles
    /// to do it with.
    ///
    /// <para>
    /// <b>No par and no difficulty number, and that is the whole of why this mode stopped being a
    /// prototype.</b> It used to author a tile <em>count</em> and roll its colours from a seed,
    /// which is a board with no fixed future: par cannot be derived from one, so it authored no
    /// goal, no star line and no fail state, and two players on the same level were not playing
    /// the same board (invariant 26). Everything graded now derives from a search over these
    /// three fields — see <c>KeeperSolver</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class KeeperDto
    {
        public int width;
        public int height;

        /// <summary>
        /// The ground, one row per line and one letter per column: <c>.</c> bare ground,
        /// <c>#</c> stone, <c>*</c> a bed, <c>r</c>/<c>g</c>/<c>b</c> a heartbed that only its own
        /// colour may be planted on, and <c>R</c>/<c>G</c>/<c>B</c> a sprig already standing.
        /// </summary>
        public string[] rows;

        /// <summary>
        /// The ordered procession this grove deals, written in R, G, B and P for a prism. It
        /// repeats, so it never needs to be longer than one lap — see <c>KeeperDeal</c>.
        /// </summary>
        public string tiles;

        /// <summary>
        /// Wasted tiles this grove forgives above par. Absent takes <c>KeeperRules.DefaultSpare</c>,
        /// which is what every level ships with.
        ///
        /// A count rather than a factor, because a wrong tile costs about the same wherever it
        /// happens — it is gone, and it has taken a cell of ground with it — while a fraction of
        /// par gives a short grove almost no room at all. See <c>LevelTuning.Slack</c>.
        /// </summary>
        public int spare;

        /// <summary>
        /// <b>Retired.</b> A grove was dealt from a seed and is now authored. Kept only so
        /// validation can name a stale one rather than JsonUtility silently discarding it and the
        /// author believing a number that does nothing — the same tripwire
        /// <see cref="ChapterDto.order"/> is.
        /// </summary>
        public int seed;

        /// <summary>
        /// Whether this block was authored. <b>Never test the block itself for null</b> —
        /// JsonUtility instantiates a [Serializable] class field on every level in the game, so
        /// absence has to be a value a real block cannot hold.
        /// </summary>
        public bool IsAuthored => width > 0 || height > 0;
    }

    /// <summary>
    /// Budburst's grove: the flowers standing in it and the colours it is dealt.
    ///
    /// <para>
    /// <b>No seed and no difficulty number.</b> Par is the fewest taps that free every critter,
    /// found by search (<c>BudSolver</c>), and both star lines and the tap budget derive from it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BudDto
    {
        public int width;
        public int height;

        /// <summary>
        /// The grove, one row per line and one letter per column: <c>R</c>/<c>G</c>/<c>B</c> a
        /// flower of that pure colour, <c>Y</c>/<c>M</c>/<c>C</c>/<c>W</c> one already wearing a
        /// blend, <c>o</c> a cocoon with a critter in it, <c>O</c> one that takes two cracks,
        /// <c>#</c> old wood, which no burst crosses, and <c>.</c> bare ground.
        ///
        /// A grove must be authored <em>settled</em> — no bunch of three alike already touching —
        /// or it would go off before anybody had touched it.
        /// </summary>
        public string[] rows;

        /// <summary>
        /// The ordered basket, written in <c>R</c>, <c>G</c> and <c>B</c>. It repeats, so one lap
        /// is enough.
        ///
        /// <b>Pure colour only.</b> Every blend on the board is one the player made by mixing,
        /// which is what keeps the basket three symbols wide however rich the grove gets.
        /// </summary>
        public string colours;

        /// <summary>
        /// The ordered strip new flowers grow from, written in <c>R</c>, <c>G</c> and <c>B</c>.
        /// It repeats, exactly as <see cref="colours"/> does.
        ///
        /// <para>
        /// <b>Its presence is what makes a grove a growing one.</b> A grove with a strip never
        /// thins out: what bursts leaves a hole, everything above slides down into it, and new
        /// flowers grow in along the top. That is the whole reason cascades compound here rather
        /// than getting rarer as the board empties, and it is why a growing grove authors no bare
        /// ground and no old wood — every cell is filled and everything falls.
        /// </para>
        /// <para>
        /// Absent leaves the grove as it was: a fixed board that only ever loses flowers. Both
        /// shapes parse and both are proved, because the first chapter was authored one way
        /// before it was authored the other and a level format that cannot read what it shipped
        /// last month is a format nobody can roll back.
        /// </para>
        /// </summary>
        public string regrow;

        /// <summary>
        /// Wasted taps this grove forgives above par. Absent takes <c>BudRules.DefaultSpare</c>.
        ///
        /// A count rather than a factor, for <see cref="KeeperDto.spare"/>'s reason: a wasted tap
        /// costs about the same wherever it happens — one colour spent and whatever small chain it
        /// set off — while a fraction of par gives a short grove almost no room at all.
        /// </summary>
        public int spare;

        /// <summary>
        /// Whether this block was authored. <b>Never test the block itself for null</b> —
        /// JsonUtility instantiates a [Serializable] class field on every level in the game, so
        /// absence has to be a value a real block cannot hold.
        /// </summary>
        public bool IsAuthored => width > 0 || height > 0;
    }

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

        /// <summary>
        /// The hint pool. Optional: absent means the built-in numbers stand.
        ///
        /// Rides here beside the heart gate because it is the same lever from the other
        /// side. Hearts decide how many attempts a day a player gets; hints decide how many
        /// of those attempts they can rescue. Both multiply the count of glades finished per
        /// day, which is what every credit figure in this file is paid per.
        /// </summary>
        public HintsDto hints;

        public PromptsDto prompts;

        /// <summary>
        /// How many stars of a chapter open the chapter after it. Optional: absent means the
        /// built-in gate stands.
        ///
        /// Rides here rather than in the manifest for two reasons. It is a <em>rule</em> and
        /// not a fact about any one chapter — the manifest owns membership and order
        /// (invariant 4a) and a per-chapter number there would be one more thing
        /// <c>Sync Manifest</c> has to carry through a rewrite. And it is the pacing lever
        /// with the widest reach in the file: it decides how much of a chapter a player has
        /// to master before the next one opens, which is how many glades a day they have
        /// left worth playing, which is what every credit figure above it is paid per.
        /// </summary>
        public ChapterGateDto chapterGate;

        /// <summary>
        /// What a second chance costs. Optional: absent means the built-in price stands.
        ///
        /// <para>
        /// Rides here rather than in the store block, which is the distinction worth keeping:
        /// a continue is not a <em>good</em>. It grants no currency, it is not adjudicated,
        /// the seeder does not read it, and nothing about it reaches a receipt — it is a gem
        /// price on a rule the phone enforces, which is the same shape the heart gate has.
        /// The block it is tuned against is <c>hearts</c>, not <c>store</c>: what a continue is
        /// worth is entirely a question of what losing costs.
        /// </para>
        /// </summary>
        public ContinueDto continueRun;

        /// <summary>
        /// What the shop sells. Optional: absent means the built-in ladder stands.
        ///
        /// <para>
        /// Rides here for the reasons every block above it does, and one that is sharper
        /// than any of them: this is the block the <em>server</em> also reads. The seeder
        /// derives <c>config/products</c> from it, so what a card promises and what a
        /// receipt is honoured for are one authored list rather than two — invariant 9a
        /// applied to money. A shop tuned in a file the seeder does not read would show one
        /// number and pay another, against a real payment, which is the one class of
        /// mistake in this project that cannot be quietly fixed later.
        /// </para>
        /// <para>
        /// It is also the block most obviously tuned against the ones above it: what a heart
        /// costs in gems is only meaningful beside how fast hearts come back, and what a
        /// coin pack is worth is only meaningful beside what a day of play earns.
        /// </para>
        /// </summary>
        public StoreDto store;
    }

    /// <summary>
    /// The shop: products bought with money, goods bought with gems.
    ///
    /// <para>
    /// Note what is deliberately absent. There is <b>no price field</b> on a product — a
    /// price lives in App Store Connect and the Play Console and is read back from the
    /// store SDK at runtime, because it differs per storefront and per tax regime, and
    /// drawing a hardcoded one is a review rejection on both stores. And there is <b>no
    /// currency field</b> on a good, because a good is bought with gems and paid for out of
    /// a ledger the server already governs; a good that paid out currency would need the
    /// server to mint it, which is what products are for.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class StoreDto
    {
        /// <summary>Bought with money. Order within a shelf is the order they are drawn.</summary>
        public StoreProductDto[] products;

        /// <summary>Bought with gems.</summary>
        public StoreGoodDto[] goods;
    }

    /// <summary>
    /// One product, exactly as both stores know it, plus what this game grants for it.
    ///
    /// <para>
    /// Fields are <b>not</b> tri-stated with -1 the way the reward rules are, and that is
    /// the point rather than an oversight. A reward rule inherits from a fallback, so
    /// "unwritten" has to be distinguishable from zero; a product does not inherit from
    /// anything, so an unwritten grant is simply a product that grants nothing, and the
    /// reader drops it by name instead of guessing what was meant. Guessing is exactly the
    /// wrong instinct here — this is the one table in the project where a wrong number is
    /// charged to somebody's card.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class StoreProductDto
    {
        /// <summary>Permanent, and identical in App Store Connect and the Play Console.</summary>
        public string id;

        /// <summary>
        /// <c>consumable</c> or <c>nonconsumable</c>. Not interchangeable, and not ours to
        /// choose freely: the store enforces that a nonconsumable is sold once per account,
        /// which is how a one-time starter offer is made one-time without a flag in a save
        /// file that two devices would have to agree about.
        /// </summary>
        public string kind;

        /// <summary><c>gems</c>, <c>coins</c>, <c>bundles</c>, or <c>supplies</c> for a
        /// heart container.</summary>
        public string shelf;

        /// <summary>Credits the server grants against a validated receipt.</summary>
        public long credits;

        /// <summary>Gems the server grants against a validated receipt.</summary>
        public long gems;

        /// <summary>
        /// Where this moves the heart refill cap to, for a container; absent or nought on a
        /// currency product.
        ///
        /// <para>
        /// The one non-currency thing a real-money product may grant, and the reasoning that
        /// permits it is in <c>StoreProduct.HeartCapacity</c>. Nought is "not a container"
        /// rather than "a container worth nothing", which is safe here for the reason it is
        /// not safe on <c>ContinueDto.enabled</c>: a zero written by <c>JsonUtility</c> into a
        /// field an older file never had means exactly what an older file meant.
        /// </para>
        /// <para>
        /// A capacity, not a bonus: what a player holds is the largest container they own, so
        /// a restore, a re-delivery or buying the rungs out of order all resolve to the same
        /// number.
        /// </para>
        /// </summary>
        public int heartCapacity;

        /// <summary>
        /// What this is expected to cost, in US cents. <b>Never displayed.</b> It is what
        /// <c>Validate Content</c> proves the value ladder against, and what the "+40%
        /// extra" badge is derived from. The player is always shown the store's own
        /// localised price string.
        /// </summary>
        public int referenceUsdCents;

        /// <summary><c>popular</c>, <c>best_value</c>, <c>starter</c>, or absent.</summary>
        public string badge;
    }

    /// <summary>One thing gems buy. See <c>StoreGood</c> for why the list is this short.</summary>
    [Serializable]
    public sealed class StoreGoodDto
    {
        public string id;

        /// <summary><c>hearts</c> or <c>heart_boost</c>. Currency is deliberately not an option.</summary>
        public string kind;

        /// <summary>Hearts, or hours of boost.</summary>
        public int amount;

        /// <summary>What it costs in gems.</summary>
        public long gems;
    }

    /// <summary>
    /// How often the game may ask an anonymous player to attach a real account.
    ///
    /// <para>
    /// Every field is -1 for "not written, inherit", the same tri-state the reward rules and
    /// the heart gate use — and here the distinction genuinely carries weight, because
    /// <b>zero is a meaningful budget</b>: it is how a trigger is switched off from a config
    /// push without an app update. See <c>AccountPromptLimits.MinBudget</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class PromptsDto
    {
        /// <summary>Times a finished chapter may raise the account panel, ever.</summary>
        public int chapterBudget = -1;

        /// <summary>Times a completed purchase may raise it, ever.</summary>
        public int purchaseBudget = -1;

        /// <summary>Hours between any two automatic asks, whatever raised them.</summary>
        public int quietHours = -1;
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

        /// <summary>
        /// What buying a way back onto a lost board costs, in gems. See <c>HeartRescue</c>.
        ///
        /// Zero is refused rather than obeyed — a free heart is a gate that no longer gates,
        /// and there is a field next door that says "no offer" properly.
        /// </summary>
        public long rescueGems = -1L;

        /// <summary>
        /// Hearts that purchase hands over. <b>Zero withdraws the offer entirely</b>, which is
        /// why it is the switch and the price is not: an offer that hands over nothing is not a
        /// cheap offer, it is no offer, so the two readings cannot be confused.
        ///
        /// <para>
        /// -1 is "not written, inherit" and it has to be, for <c>ContinueDto.enabled</c>'s
        /// reason: <c>JsonUtility</c> instantiates this class even when the JSON has no such
        /// block, so a default of zero would silently withdraw the feature on every client that
        /// had not yet taken a content push.
        /// </para>
        /// </summary>
        public int rescueHearts = -1;

        /// <summary>
        /// How many glades at the start of a mode cost no heart at all — the window in which
        /// a player is still working out what the mode <em>is</em>.
        ///
        /// <para>
        /// Zero is legal and switches the window off, which is why the tri-state matters here
        /// more than anywhere else in this block: "not written" has to mean the built-in three
        /// rather than none. See <c>HeartStake</c> for what the number is counted over — it is
        /// the first chapter of <em>each</em> mode, so a mode shipped later opens as gently as
        /// the first one did.
        /// </para>
        /// </summary>
        public int graceLevels = -1;
    }

    /// <summary>
    /// The hint pool, as a content file writes it.
    ///
    /// <para>
    /// Every field is -1 for "not written, inherit", the same tri-state the heart block and
    /// the reward rules use.
    /// </para>
    /// <para>
    /// Note what is deliberately absent, twice over. There is no per-level allowance any
    /// more — a hint is spent from the account, so a glade has no opinion about how many of
    /// them a player may use on it. And there is nothing about <em>buying</em> hints: the
    /// refill timer and a watched video are the only two ways one arrives, and a price
    /// authored here would make the pool a storefront without anybody having designed one.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class HintsDto
    {
        /// <summary>Where the refill timer stops. This is the number that paces free help.</summary>
        public int refillCap = -1;

        /// <summary>
        /// The most a player may hold once granted hints stack on top. Equal to
        /// <see cref="refillCap"/> as shipped, which means a hint granted at a full pool is
        /// refused — safe only because nothing offers one without checking first. Safe to
        /// lower: it refuses new grants and never confiscates.
        /// </summary>
        public int ceiling = -1;

        /// <summary>Seconds between refills.</summary>
        public int refillSeconds = -1;
    }

    /// <summary>
    /// What it takes to open the next chapter.
    ///
    /// <para>
    /// One number, and it is written <em>per level</em> rather than as a total on purpose.
    /// A chapter is not a fixed size — chapters ship every two to four weeks and nothing
    /// says the next one holds ten glades — so a total of 20 would be two thirds of one
    /// chapter and a fifth of another, and the rule a player learned on the first would
    /// quietly stop being the rule. Two stars a level is a sentence that survives a chapter
    /// of any length, and it is what the panel that explains the gate says.
    /// </para>
    /// <para>
    /// -1 is "not written, inherit", the tri-state every other optional number in this file
    /// uses. Zero is a legal value and is the lever's whole point: it opens every chapter at
    /// once, which is the change that has to be available in minutes if the gate turns out to
    /// be a wall. Bounded by <c>ChapterGateLimits</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ChapterGateDto
    {
        /// <summary>Stars per level of the chapter behind it. 3 means perfect play.</summary>
        public int starsPerLevel = -1;
    }

    /// <summary>
    /// What it costs to carry a lost run on, and how much allowance that buys.
    ///
    /// <para>
    /// Every field is the tri-state this file uses throughout: <b>-1 is "not written,
    /// inherit"</b>, and it has to be, because <c>JsonUtility</c> instantiates a
    /// <c>[Serializable]</c> class field even when the JSON carries no such key — so "the
    /// block is absent" and "the block is present and empty" arrive here as the same object,
    /// and only a value a real setting cannot hold can tell them apart. The same rule
    /// invariant 11b states for the save file, in the other direction.
    /// </para>
    /// <para>
    /// That is also why <see cref="enabled"/> is an integer rather than a <c>bool</c>. A bool
    /// would read <c>false</c> for a file written before this block existed, which would
    /// withdraw the offer from every client that had not taken a content push — the exact
    /// failure the tri-state exists to prevent, on the one field where it would be silent.
    /// Zero switches it off; anything below zero inherits, which is on.
    /// </para>
    /// <para>
    /// Bounded by <c>ContinueLimits</c>, which is where the reasoning behind each number
    /// lives.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ContinueDto
    {
        /// <summary>0 withdraws the offer entirely. -1 inherits, which is on.</summary>
        public int enabled = -1;

        /// <summary>What the first continue on a run costs, in gems.</summary>
        public long gems = -1L;

        /// <summary>
        /// What each continue already taken adds to the next one's price. 0 is flat, which
        /// is what ships — a run may be continued as often as the player can pay for.
        /// </summary>
        public long gemsStep = -1L;

        /// <summary>Turns a glade's continue hands over, above whatever it took to un-lose it.</summary>
        public int turns = -1;

        /// <summary>
        /// <b>Retired.</b> Cells of light a Lightweave continue handed over. The mode is gone;
        /// the field stays because <c>progression.json</c> is content that ships on its own
        /// cadence and a live document still carries this key — deleting it would make every
        /// published table read as malformed for no gain. See <c>ContinueUnit.Ink</c>.
        /// </summary>
        public int ink = -1;

        /// <summary>Taps a thicket's continue hands over, on the same terms.</summary>
        public int taps = -1;

        /// <summary>Motes a well's continue hands over, on the same terms.</summary>
        public int motes = -1;

        /// <summary>Tiles a grove's continue hands over, on the same terms.</summary>
        public int tiles = -1;
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

        /// <summary>
        /// The wheel <c>win_bonus</c> is spun for, or absent for a flat offer.
        ///
        /// <para>
        /// Optional, and its absence means the <em>flat</em> offer rather than a built-in
        /// ladder — the one table here that does not fall back to a default. A published file
        /// that has never heard of the wheel must keep paying exactly the amount it authored,
        /// or a client taking a content push would start drawing multipliers that the server
        /// reading the same file would never grant. See <c>BonusWheel.None</c>.
        /// </para>
        /// </summary>
        public AdWheelDto wheel;
    }

    /// <summary>
    /// The bonus wheel: a list of multipliers on <c>win_bonus</c>'s own amount.
    ///
    /// <para>
    /// Every slice is the same size and every slice is equally likely, so there are no
    /// weights here and there must never be: equal wedges drawn over unequal odds is a lie
    /// the picture tells, and it is the one loot-box regulation exists to catch. Variance is
    /// authored as the spread of <see cref="AdWheelSliceDto.percent"/>, where the player can
    /// see all of it at once.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AdWheelDto
    {
        /// <summary>
        /// The rim, in the order it is drawn. Four to twelve of them; see <c>WheelRules</c>.
        /// </summary>
        public AdWheelSliceDto[] slices;
    }

    /// <summary>
    /// One wedge. <c>percent</c> is a multiplier on the placement's ordinary payout and
    /// <b>may never be below 100</b> — the wheel only ever adds. See <c>WheelRules</c>.
    /// </summary>
    [Serializable]
    public sealed class AdWheelSliceDto
    {
        public int percent;
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

    /// <summary>
    /// The grove catalog: every plot the land can grow to, and everything that can stand
    /// on one. Read from <c>homestead.json</c>, lazily, on entering the Grovement.
    /// </summary>
    [Serializable]
    public sealed class HomesteadBodyDto
    {
        public int schemaVersion;

        public GroveFloorDto floor;
        public HomesteadPieceDto[] pieces;

        /// <summary>
        /// The star ladder the grove's score is read against. Optional: a body without one
        /// falls back to the built-in ladder, which is what a body written before this field
        /// existed produces and what <c>GroveScoreTable.Default</c> is for.
        /// </summary>
        public GroveScoreDto score;
    }

    /// <summary>
    /// What a grove has to be worth to earn each star.
    ///
    /// <para>
    /// Content rather than constants because the catalog grows with every drop, so the score
    /// a finished grove reaches climbs for the life of the game — see <c>GroveScoreTable</c>.
    /// A nested object rather than a bare array on the body so a later reading of the same
    /// idea (a name per tier, say) is a field here rather than a second top-level key.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GroveScoreDto
    {
        /// <summary>
        /// Ascending credit thresholds, one per star. Must rise strictly; the build gate
        /// refuses a ladder that does not, because a repeated or falling rung awards a star
        /// nobody can distinguish from the one below it.
        /// </summary>
        public int[] stars;
    }

    /// <summary>
    /// The ground the grove is built on: one isometric tile field, and the regions it is sold
    /// in.
    ///
    /// Replaces the island list from v2. See <c>GroveFloor</c> for why a field of identical
    /// tiles is a different design from a ladder of authored compositions rather than a
    /// simplification of one.
    /// </summary>
    [Serializable]
    public sealed class GroveFloorDto
    {
        /// <summary>
        /// How many tiles the field is across and deep.
        ///
        /// <b>It may only ever grow.</b> Tile ids are absolute coordinates and they are keys in
        /// the save file, so shrinking the field strands whatever stands beyond the new edge and
        /// a field that grew at the left would renumber every tile in the world.
        /// </summary>
        public int cols;
        public int rows;

        /// <summary>Sprite key relative to <c>Art/</c> for one floor tile. Empty draws a generated one.</summary>
        public string tileArt;

        /// <summary>
        /// The tile the grove hall stands on, as a tile id — the one square nothing can be
        /// placed into, because the hall is drawn from the best home the player owns.
        /// </summary>
        public string hallTile;

        /// <summary>
        /// Where the starter companion stands until the player moves them.
        ///
        /// Nothing is written to the save for it: the tile shows the starter while it has no
        /// row of its own. A stored default is what invariant 11c forbids, and a fresh install
        /// stamping one would outrank a device where the player had already moved them.
        /// </summary>
        public string starterTile;

        public GroveRegionDto[] regions;
    }

    /// <summary>One rectangle of the floor, sold as a unit.</summary>
    [Serializable]
    public sealed class GroveRegionDto
    {
        /// <summary>
        /// Permanent id. <b>Written into the save file</b> as an entitlement, so it is under
        /// invariant 1: never renamed, never reused, never derived from position.
        /// </summary>
        public string id;

        /// <summary>Top-left tile of the region, in absolute floor coordinates.</summary>
        public int col;
        public int row;

        public int cols;
        public int rows;

        /// <summary>
        /// Credits that buy this region. <b>Zero means open from the first launch.</b>
        ///
        /// Zero rather than a flag, because <c>JsonUtility</c> writes a zero into every field an
        /// older file never had — so "absent" and "free" have to be the same fact.
        /// <c>ContentValidation</c> fails the build when nothing is free, since a first visit to
        /// a grove with no ground is a feature that looks broken.
        /// </summary>
        public int cost;
    }



    /// <summary>One thing a player can put in their grove.</summary>
    [Serializable]
    public sealed class HomesteadPieceDto
    {
        /// <summary>
        /// Permanent id. Written into the save file twice over — into the owned set and into
        /// every slot holding one — so invariant 1 applies in full.
        /// </summary>
        public string id;

        /// <summary>
        /// Art key relative to <c>Art/</c>. A whole relative path rather than a leaf under
        /// one folder, because residents draw the board's own critter flipbooks — global art
        /// the game has already paid for — while decor lives under <c>Art/Homestead/</c>.
        /// Empty means "same as the id", under <c>Art/Homestead/</c>.
        /// </summary>
        public string art;

        /// <summary>True when <see cref="art"/> names a folder of frames rather than a sprite.</summary>
        public bool animated;

        /// <summary>
        /// Which slots this piece belongs in — the same vocabulary <c>HomesteadSlotDto.kind</c>
        /// uses. Empty means ground.
        ///
        /// Read for decor only: a resident stands anywhere but the hearth and a dwelling stands
        /// only on it, neither of which consults this.
        /// </summary>
        public string slot;

        /// <summary>
        /// Where a dwelling sits on the home ladder — 1 is the cabin every grove starts with.
        /// Ignored for anything that is not a dwelling.
        ///
        /// Authored rather than inferred from the order of the file, because inserting a tier
        /// in the middle must not silently repaint every home in the world.
        /// </summary>
        public int tier;

        /// <summary>
        /// <c>"resident"</c>, <c>"decor"</c> or <c>"dwelling"</c>. Anything else is reported
        /// and read as decor.
        ///
        /// The distinction is not mechanical — both are placed by the same code into the same
        /// slots. It exists so the build gate can prove a resident is never for sale, which is
        /// the whole endowment argument for the feature: a resident is proof of a glade the
        /// player finished, and one that can be bought turns the grove into a receipt.
        /// </summary>
        public string kind;

        /// <summary>
        /// Credits that buy this piece outright. <b>Zero or absent means it cannot be
        /// bought</b>, for <c>ManifestCompanionDto.unlockCost</c>'s reason: JsonUtility writes
        /// a zero into every field an older file never had, so reading zero as "free" would
        /// put a whole shop on sale for nothing.
        /// </summary>
        public int cost;

        /// <summary>
        /// How many copies one purchase grants. <b>Zero or absent means one</b>, which is
        /// <c>scale</c>'s convention and the reason a catalog written before this field existed
        /// still sells one of everything rather than none.
        ///
        /// The small repeatable things — fences, flowers, paving, ground cover — are sold in
        /// tens at the price a single one used to cost, because a player wants a dozen of each
        /// and buying them one tap at a time is a chore rather than a decision. It is authored
        /// per piece rather than derived from <c>slot</c>: the slot kind is the shop's shelf,
        /// not a claim about how many of a thing anybody wants.
        /// </summary>
        public int bundle;

        /// <summary>A glade whose clear earns this piece, or empty.</summary>
        public string requiresLevel;

        /// <summary>A chapter whose completion earns this piece, or empty.</summary>
        public string requiresChapter;

        /// <summary>Size relative to the art as authored. 0 means 1.</summary>
        public float scale;

        /// <summary>
        /// How far up its slot the piece sits, as a fraction of its own drawn height. The
        /// shipped art stands on the ground, so most pieces want about 0.5. A property of the
        /// image rather than of the slot, for <c>UIKit.PillFaceLift</c>'s reason.
        /// </summary>
        public float lift;

        /// <summary>Set true to retire a piece without deleting it from anybody's grove.</summary>
        public bool disabled;
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
