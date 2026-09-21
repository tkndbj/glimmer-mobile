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
    /// <see cref="id"/> is permanent: it names the season's loc keys, its save row, its
    /// analytics and every claim id its chests produce. Renaming one is the same class of
    /// mistake as renaming a level id — it does not break anything visibly, it silently
    /// resets everybody's track and orphans every claim already in flight.
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
        /// Set true and the season runs again for ever, back to back, instead of ending.
        ///
        /// <para>
        /// <c>startUnix</c> and <c>endUnix</c> then describe <b>cycle nought</b> and the gap
        /// between them is the period; cycle <c>n</c> runs <c>[start + n·period, …)</c>, and its
        /// id is this entry's <c>id</c> with the cycle number on the end. See
        /// <see cref="Events.SeasonCycle"/> for what that costs and what it buys — in short,
        /// nothing and everything: no content push a season, no re-seed a season, and no
        /// calendar for anybody to forget to extend.
        /// </para>
        /// <para>
        /// <b>Absent is a one-off season, which is what shipped before this field existed</b>,
        /// so an entry written against the old shape still reads exactly as it did — and a
        /// client too old to know the field reads a repeating season as its first window and
        /// then shows no season at all. That is the honest degradation rather than a wrong one,
        /// and it is only reachable through remote content delivery, which is off.
        /// </para>
        /// </summary>
        public bool repeats;

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

        /// <summary>The reward ladder, lowest goal first.</summary>
        public ManifestEventMilestoneDto[] milestones;

        /// <summary>
        /// What the pass track costs in <b>gems</b>, or nought for a season with only a free
        /// one.
        ///
        /// <para>
        /// <b>Gems rather than money, and that is a simplification rather than a discount.</b>
        /// A real-money product would have to be a non-consumable with a receipt, a
        /// server-written entitlement, a refund sweep and a store registration that can never
        /// be renamed — the shape invariant 18d describes and every line of which has to work
        /// before a single rung pays. A gem price is an <em>ordinary spend</em> (invariant 18),
        /// so the pass becomes what every other permanent thing in this game already is: an
        /// entitlement bought with a currency the player already holds.
        /// </para>
        /// </summary>
        public int passGems;
    }

    /// <summary>
    /// One rung: grow <c>goal</c> marks inside the window, open <c>tier</c> on the free
    /// track and <c>premiumTier</c> on the pass track.
    ///
    /// <para>
    /// <b>Tier names rather than amounts</b> — see <c>EventMilestone</c>. A rung that
    /// authored its own credits would be one of eighty numbers whose odds nobody can
    /// disclose and which no retune can reach; naming a <c>TaskTierDto.id</c> makes one
    /// disclosure per tier the odds for every rung that pays it.
    /// </para>
    /// <para>
    /// <c>premiumTier</c> is empty on a season with no product. On a season that has one it
    /// is required, because a paid column with a hole in it is a player looking at what they
    /// bought and seeing nothing.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ManifestEventMilestoneDto
    {
        public int goal;
        public string tier;
        public string premiumTier;
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

        /// <summary>
        /// Which ladder inside that mode this chapter is on: absent or <c>main</c> for the
        /// ordinary run of chapters, <c>infinite</c> for a lane whose waves never stop.
        ///
        /// <para>
        /// Absent is the main track and is never an error, which is what lets every chapter
        /// authored before tracks existed keep working with its file untouched. A track this
        /// build has never heard of is skipped whole, exactly as an unknown mode is (invariant
        /// 20) — see <c>GameTrack</c>.
        /// </para>
        /// </summary>
        public string track;

        /// <summary>
        /// The keeper level a player must have reached before this chapter opens at all. 0 or
        /// absent means no such wall, which is what every chapter that shipped before this
        /// field existed carries.
        ///
        /// <para>
        /// <b>Zero is the safe sentinel and it is chosen rather than inherited</b>, for
        /// <see cref="ManifestCompanionDto.unlockCost"/>'s reason: <c>JsonUtility</c> writes a
        /// zero into every field an older manifest never had, so "absent" and "no wall" have to
        /// be the same value or a manifest from before this field existed would padlock the
        /// whole catalog behind a level nobody has.
        /// </para>
        /// <para>
        /// <b>Content rather than code, and per chapter rather than per lane.</b> A wall in
        /// front of a way of playing is the one kind of tuning whose damage is players who stop
        /// playing rather than an economy that drifts — <c>ChapterGateTable</c>'s argument — so
        /// it has to be movable by pushing one small file rather than by a store review. Per
        /// chapter because a chapter is the catalog's unit and the star gate already lives
        /// there: a lane-wide field would be a second place a gate is written down, and the two
        /// would disagree the first time a lane grew a second chapter.
        /// </para>
        /// <para>
        /// <b>It is a wall, never a skip.</b> Unlike <see cref="minAppVersion"/> a chapter
        /// behind this is still listed, still drawn and still named — the player is meant to see
        /// what they are working towards. See <c>ChapterGate</c>.
        /// </para>
        /// </summary>
        public int minKeeperLevel;
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

        /// <summary>Whether the end-of-chapter marker is moored on a tile. See <c>afloat</c>.</summary>
        public bool teaserAfloat;

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

        // Prismvale, which authors the shared prototype block - a grid of rows, a deal and a
        // slack - and leaves `cores` empty: a field of gems is everything the level hands over,
        // and a board with anything dealt into it has a future nothing can search
        // (invariant 26). The name *is* the claim (LevelMode.Claims), rather than one field
        // plus a "which mode" string, so a level carrying two blocks is a level the mapper
        // reports on rather than one it quietly reads twice.
        //
        // `kindle`, `bud`, `march` and `ember` were fields here and are **retired block names
        // that must never be reused**, for the duskcap's reason (invariant 5f): JsonUtility
        // drops an unknown field without a word, so a chapter body still carrying one would
        // index, derive a plausible glade and ship as something nobody authored.
        // `Tools/verify/content.py` refuses all four by name.
        public ProtoDto prism;

        // Thornwatch, which is the one mode here that could not author the shared block: a siege
        // carries a ward line and a list of waves as well as a field, and a field that refills has
        // no `spare` because it has no move allowance at all - the fail state is the ward line
        // (invariant 22b in the unit this mode is graded in). See SiegeDto.
        public SiegeDto siege;


        // `quarry`, `topple`, `nova`, `keeper`, `nectar`, `ribbon`, `fling`, `warren`, `orbit`
        // and `moonwake` are **retired field names and must never be reused**, for the duskcap's
        // reason (invariant 5f): JsonUtility drops an unknown field without a word, so a chapter
        // body still carrying one would index, derive a plausible glade and ship as something
        // nobody authored. `content.py` refuses every one of them by name (RETIRED_BLOCKS)
        // rather than ignoring them.

        public float mapX;
        public float mapY;

        /// <summary>
        /// Whether this glade stands on a floating tile rather than on the painting itself.
        ///
        /// <para>
        /// Generated with the position (<c>Tools/make_map_seats.py</c>) and never authored. A
        /// node stands on the ground the map draws; `map1` is an archipelago whose chain
        /// crosses open water, and on the stretch where the painting has only a current to
        /// offer, a node is moored on a tile instead.
        /// </para>
        /// <para>
        /// <b>False is the honest default</b>, which is what makes this safe to add: every
        /// chapter body written before it said nothing, <c>JsonUtility</c> writes <c>false</c>
        /// into a field a file never had, and false means "stands on the map" — which is what
        /// all three road maps do and what every one of those files meant.
        /// </para>
        /// </summary>
        public bool afloat;
        public string accent;
        public string slate;
        public string backdrop;

        /// <summary>
        /// What this level has to say for itself while it is being played.
        ///
        /// <b>Presentation, and deliberately not part of any mode's block.</b> A mode's block is
        /// the board, and every graded number in this game derives from the board — so a field
        /// that could move par has to live there and a field that provably cannot must not. A
        /// story cannot move par, a star line, an allowance or a fail state; it is read by one
        /// screen and by nothing that grades a run. It sits here beside `backdrop` for the same
        /// reason `backdrop` does.
        /// </summary>
        public StoryDto story;

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
        /// What is standing in the well, top row first — one letter per column, with '.' for
        /// empty ground. Spaces are ignored, so a row may be spaced out for reading.
        ///
        /// <para>
        /// Light is upper case (R, G, B, Y, M, C) and glass is lower, so a board says at a glance
        /// what is made of what: <c>O</c> is an empty lens and <c>r</c>, <c>g</c>, <c>b</c>,
        /// <c>y</c>, <c>m</c>, <c>c</c> are a lens already holding that much. How full each pane
        /// starts is the second chapter's whole difficulty ramp.
        /// </para>
        /// <para>
        /// The two slashes are a <b>mirror</b>, leaning the way it turns light. It is the one
        /// cell here light passes through rather than stopping at, it takes no drop and no wash,
        /// and it shatters the first time it turns a shot — so a mirror nothing is ever aimed at
        /// makes a level unwinnable rather than merely dull.
        /// </para>
        /// <para>
        /// Row nought is the brim and must be empty: a mote that comes to rest there floods the
        /// well and ends the run, so a level that begins in that state begins lost.
        /// </para>
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
    /// The board of a prototype mode: a grid, a deal and the room it forgives.
    ///
    /// <para>
    /// <b>One block shape, and no number in it that can be wrong.</b> Par is searched from the
    /// grid (<c>ProtoSearch</c>) and both star lines and the allowance derive from it, so there
    /// is nothing here an author can type that comes to disagree with how the level actually
    /// plays. That is the same bargain every mode in this game strikes, and the reason five
    /// modes could be built at once for the price of a little over one: what a mode brings is
    /// its rules, and what a level brings is a picture of a board. Four of the five were then
    /// withdrawn and this shape outlived them, which is the argument for it made twice.
    /// </para>
    /// <para>
    /// <b>It replaced <c>KeeperDto</c>, whose field name is a spent id.</b> Groovekeeper's ten
    /// levels were withdrawn and the mode with them; a chapter body still carrying a
    /// <c>keeper</c> block is content written for a build that no longer exists, and
    /// <c>ContentValidation</c> says so rather than <c>JsonUtility</c> discarding it in silence
    /// (invariant 5f's rule for a retired token).
    /// </para>
    /// </summary>
    /// <summary>
    /// Thornwatch's level: a field of gems, the colours it refills from, the ward line standing
    /// in front of it, and the waves coming down the hill at it.
    ///
    /// <para>
    /// <b>No numbers at all</b>, which is invariant 20d's rule. How much fuel a match is worth,
    /// how hard a bolt lands, how fast a ward's fuel fades, how long a raider takes to cross the
    /// hill and what a blow costs a ward are all <c>SiegeRules</c> — constants in code, retuned
    /// for the whole mode at once. Par is derived from those and from what is written here, so a
    /// typed one could only ever drift from the level it claims to describe (invariant 5).
    /// </para>
    /// <para>
    /// <b>And no <c>spare</c>, deliberately.</b> Every other mode on this shape is lost by running
    /// out of moves; this one is lost when the last ward falls, so a move allowance would be a
    /// second fail state nobody asked for and the readout of it would count down to an ending
    /// that never happens. What is left of that idea is the ward line itself — the mode's
    /// allowance, drawn on the board rather than in a corner, which is the thing Hollowmarch
    /// found and this one inherits.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SiegeDto
    {
        public int width;
        public int height;

        /// <summary>
        /// The field as it is dealt, one string per row, top first. Spaces are ignored.
        ///
        /// <c>r</c>, <c>g</c>, <c>b</c> and <c>y</c>, and it must be authored <b>settled</b> — no
        /// three alike already touching, or the field would go off before anybody had moved a gem
        /// and the count the run is graded against would have moved with it.
        /// </summary>
        public string[] rows;

        /// <summary>
        /// The colours the field refills from, written as letters.
        ///
        /// An alphabet rather than a queue: a board that refills has no fixed future whichever way
        /// the next gem is chosen, so nothing is bought by making the stream authorable. What it
        /// does buy is a level that can leave a colour out.
        /// </summary>
        public string gems;

        /// <summary>The ward line, left to right, one letter each. No two the same.</summary>
        public string wards;

        /// <summary>
        /// How much health every raider on this hill carries, in <b>tenths</b> of what its kind
        /// ordinarily has. Absent or nought means ten, which is the plain figure.
        ///
        /// <para>
        /// <b>Derived and written down, never invented</b> — the same bargain <c>backdrop</c> and
        /// <c>mapStrips</c> strike (invariant 7c): a chapter's raiders are a step tougher than the
        /// one before it (<c>SiegeTuning.ToughnessFor</c>), the chapter tool computes that from its
        /// ordinal, and the number lands in the body so the board <em>says</em> what it is. A level
        /// still authors no numbers (37d); a generator does.
        /// </para>
        /// <para>
        /// <b>In the body rather than looked up at run time, and that is the load-bearing part.</b>
        /// The hold simulation — this mode's only instrument (37j) — builds its layouts from an
        /// inline table with no catalog anywhere near it, so a surge that came from the chapter
        /// index would be <c>None</c> in every measurement this mode has: ninety runs a chapter,
        /// silently against a hill nobody ships. Carried on the board, it travels into every gate
        /// that reads one.
        /// </para>
        /// </summary>
        public int tough;

        /// <summary>
        /// The waves, in the order they come. One letter per raider — lower case a creeper, upper
        /// case a brute — so a wave's shape is visible in the file. A wave steps out on a clock, or
        /// the moment the hill is empty, whichever comes first.
        /// </summary>
        public string[] waves;

        /// <summary>
        /// The warlord that comes after them, as one colour letter, or empty for a siege that
        /// sends none.
        ///
        /// <para>
        /// <b>Which wave it is in is a rule and not an authoring decision.</b> A warlord is always
        /// the <em>last</em> wave — <c>SiegeLayout</c> appends it — so this field says only whether
        /// there is one and what colour it wears. Anything else would be two places that can
        /// disagree about which wave is the finale, and the finale is the one wave a player
        /// remembers.
        /// </para>
        /// <para>
        /// Its health, how far down the hill it stops, how often it casts and what a spell costs a
        /// ward are all <c>SiegeTuning</c>, exactly as a creeper's are: a level says what is
        /// coming, the mode says what it does.
        /// </para>
        /// </summary>
        public string boss;

        /// <summary>
        /// How often a fresh gem falls in as a <b>cog</b>, per hundred. Absent or nought for a
        /// level that deals none.
        ///
        /// <para>
        /// A cog never matches and is never worth fuel. It is destroyed by a run of gems
        /// <em>beside</em> it, and the colour of that run decides which ward goes up a rank — ten
        /// per cent more damage and ten per cent less fuel a bolt, up to four ranks. So what it
        /// asks the player is the mode's own question about the line instead of about the hill,
        /// and answering carelessly upgrades the wrong turret.
        /// </para>
        /// <para>
        /// <b>A rate rather than a count</b>, because the field refills — see
        /// <c>SiegeLayout.Cogs</c>. A level may also stand cogs on its authored field by writing
        /// <c>*</c> in <see cref="rows"/>, which is how the rung that teaches them puts one where
        /// it will be met.
        /// </para>
        /// </summary>
        public int cogs;

        /// <summary>
        /// Which charms this field's refill may deal, as letters — <c>"p"</c>, <c>"pl"</c>,
        /// <c>"pls"</c> — or absent for a level that deals none.
        ///
        /// <para>
        /// <b>A charm is a power riding on an ordinary gem</b> (<c>SiegeCharm</c>): the cell is
        /// still that colour and still worth that fuel, and what it adds happens at the moment it
        /// goes. A prism joins a run of any colour; a lance takes its whole row and column with
        /// it; a stormglass throws a bolt at every raider on the hill and two at everything wearing
        /// its own colour.
        /// </para>
        /// <para>
        /// <b>Which, and never how often.</b> How rare a charm is belongs to the mode
        /// (<c>SiegeTuning.CharmWithin</c>), because a level that could tune its own rarity
        /// would be a second place this mode's difficulty is decided and the two would drift the
        /// first time either was retuned. What a level says is whether its board has met this
        /// mechanic yet — which is content, and is why the opening rung of the first chapter
        /// authors none at all (invariant 24).
        /// </para>
        /// <para>
        /// <b>Derived from the chapter's ordinal and written down, never invented</b> — the same
        /// bargain <c>tough</c> and <c>backdrop</c> strike (invariants 37by, 7c). A chapter at
        /// ordinal <em>n</em> deals the first <em>n</em> charms of the roster, so a player meets
        /// one new one a chapter; the chapter tool computes that and the body carries the answer,
        /// because the hold simulation builds its layouts from an inline table with no catalog
        /// anywhere near it and a set that came from the index would be empty in every measurement
        /// this mode has.
        /// </para>
        /// <para>
        /// <b>Never written into <see cref="rows"/>.</b> A field is authored settled and dealt with
        /// nothing on it; a charm standing in an authored cell would be a payoff its author placed
        /// rather than one the player was dealt (invariant 20m).
        /// </para>
        /// </summary>
        public string charms;

        /// <summary>
        /// The ramp, for a lane whose waves never stop, or absent for an ordinary siege.
        ///
        /// <para>
        /// <b>A level on an endless track authors no waves and no boss at all.</b> What is coming
        /// at wave <em>n</em> is a rule (<c>SiegeEndless</c>) rather than a list, so the two ways
        /// of saying it are mutually exclusive — a file carrying both would be a file with two
        /// answers to what the second wave is, and the reader refuses it rather than picking one.
        /// </para>
        /// </summary>
        public SiegeEndlessDto endless;

        /// <summary>
        /// Whether this block was authored. <b>Never test the block itself for null</b> —
        /// JsonUtility instantiates a [Serializable] class field on every level in the game, so
        /// absence has to be a value a real block cannot hold.
        /// </summary>
        public bool IsAuthored => width > 0 || height > 0;
    }

    /// <summary>
    /// The ramp of a siege whose waves never stop.
    ///
    /// <b>Four numbers and nothing else.</b> Everything about <em>what</em> arrives — how often a
    /// boss comes, which one, how many raiders, how much tougher each wave is — is a rule in
    /// <c>SiegeEndless</c>, for invariant 20d's reason: a lane that authored its own ramp would be
    /// a second place this mode's difficulty is decided, and the two would drift the first time
    /// either was retuned.
    /// </summary>
    [Serializable]
    public sealed class SiegeEndlessDto
    {
        /// <summary>
        /// The wave a three-star run reaches.
        ///
        /// <b>Authored, because nothing can derive it.</b> Par everywhere else in this game is a
        /// search or an arithmetic floor over what a level sends; an endless lane sends
        /// everything, so how far is far is the one number a designer has to decide — and it is
        /// decided by playing.
        /// </summary>
        public int goldWave;

        /// <summary>
        /// The fraction of <see cref="goldWave"/> a two-star run reaches. Between 0 and 1.
        ///
        /// <b>Below one, and the ordering inverts with it</b> — on a climbing level three stars
        /// asks for <em>more</em> than two (<c>LevelTuning.Climbs</c>). Absent takes the built-in
        /// fraction rather than nought, which would make two stars free.
        /// </summary>
        public float silverFactor;

        /// <summary>
        /// Whether this block was authored. <b>Never test the block itself for null</b> —
        /// JsonUtility instantiates it on every siege in the game (this project's own hard-won
        /// note), so absence has to be a value a real block cannot hold.
        /// </summary>
        public bool IsAuthored => goldWave > 0;
    }

    [Serializable]
    public sealed class ProtoDto
    {
        public int width;
        public int height;

        /// <summary>
        /// The board, one string per row, top first. Spaces are ignored so a row may be spaced
        /// out for reading.
        ///
        /// Which letters mean what is the mode's business — see each mode's <c>Layout</c>, which
        /// is the one place its vocabulary is written down. A letter the mode does not know is
        /// refused by row and column rather than read as bare ground: a mistyped board that
        /// quietly loses a critter validates, derives a plausible par and ships.
        /// </summary>
        public string[] rows;

        /// <summary>
        /// The deal: what the board hands the player, in order, repeating.
        ///
        /// <para>
        /// The third of the three things this block has always claimed to carry — a grid, a deal
        /// and a slack. No mode on this block wants one today: the retired Hollowmarch wrote its
        /// magazine here, and Prismvale leaves it empty, because a board with anything dealt into
        /// it has a future nothing can search. A deal repeats, so one lap is enough, exactly as
        /// <see cref="FallDto.motes"/> does.
        /// </para>
        /// <para>
        /// <b>Ordered and repeating rather than random</b>, which is not a stylistic choice: a
        /// board with no fixed future cannot be searched, so it can author no par, and with no
        /// par there is no star line, no allowance and no fail state (invariant 26). A mode with
        /// no deal simply leaves it out.
        /// </para>
        /// </summary>
        public string cores;

        /// <summary>
        /// Wasted moves this board forgives above par. 0 takes <c>ProtoLevelRules.DefaultSpare</c>.
        ///
        /// A count rather than a factor, for <c>FallDto.spare</c>'s reason: a wrong move costs
        /// about the same wherever it happens, while a fraction of par gives a short board almost
        /// no room at all. See <c>LevelTuning.Slack</c>.
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

        /// <summary>
        /// The utilities a player may hold, and what they cost. Optional: absent means the
        /// built-in catalog stands.
        ///
        /// <para>
        /// Rides here for the reasons every block above it does, and one of its own: a utility
        /// arrives out of a <c>daily</c> chest and is bought with gems, so it is tuned against
        /// the block directly above it and the chest table directly below the rewards. A
        /// consumable priced without the chest odds in front of you is a consumable that is
        /// either never bought or never dropped.
        /// </para>
        /// <para>
        /// What it may <em>not</em> do is invent a <c>kind</c>: what a utility does is a rule
        /// with a fail state and a grade attached, so an entry naming a kind this build has
        /// never heard of is skipped, exactly as a chapter naming an unknown mode is
        /// (invariant 20). See <c>UtilityCatalog</c>.
        /// </para>
        /// </summary>
        public UtilitiesDto utilities;

        /// <summary>
        /// The turret roster: which ones exist, what each does beyond firing, and what it costs.
        ///
        /// <para>
        /// Optional, and absent is not an error — a client that predates it keeps its built-in
        /// roster, and a client that has it reads a file written before it existed and does the
        /// same (invariant 9b).
        /// </para>
        /// <para>
        /// What it may <em>not</em> do is invent an <c>ability</c>: an ability is a rule the board
        /// runs, so an entry naming one this build has never heard of still stands and simply
        /// fires a plain bolt (invariant 20, one level down). And no entry may make a bolt
        /// <em>weaker</em> — there is no field that could, deliberately, because par is computed
        /// against the baseline bolt and a turret that hit softer would put three stars out of
        /// reach of whoever chose it. See <c>WardCatalog</c>.
        /// </para>
        /// </summary>
        public WardsDto wards;

        /// <summary>The task slates and their chest ladder. Optional; see <see cref="TaskTableDto"/>.</summary>
        public TaskTableDto tasks;

        /// <summary>
        /// Refer-a-friend: the milestone an invitee has to reach, the ladder the referrer
        /// climbs and the chest the invitee gets. Optional; see <see cref="ReferralDto"/>.
        /// </summary>
        public ReferralDto referral;

        /// <summary>
        /// Which reminders the phone sends and when. Optional; see <see cref="NotificationsDto"/>.
        ///
        /// <b>The one block in this file that reaches no server.</b> Nothing here is
        /// adjudicated, claimed or paid — a notification is a thing a device says to its own
        /// owner — so <c>seed-config.mjs</c> does not publish it and <c>firestore.rules</c>
        /// has nothing to learn about it. Its push path is the remote-content one, the same
        /// path a chapter body takes.
        /// </summary>
        public NotificationsDto notifications;

        /// <summary>
        /// What the Infinite lane pays per wave cleared, and the ceiling on it. Optional; see
        /// <see cref="Progression.EndlessRewardTable"/>.
        ///
        /// <b>The only block here that decides XP outside the star ledger</b>, which is why it
        /// carries a ceiling at all and why <c>seed-config.mjs</c> publishes it: a keeper level
        /// the server derives differently from the device is a published card that silently
        /// drops whatever that level gated (invariant 19a).
        /// </summary>
        public EndlessRewardDto endless;

        /// <summary>
        /// What an XP boost is worth and how long it runs. Optional; see
        /// <see cref="Progression.XpBoostTable"/>.
        ///
        /// The second block here that decides XP outside the star ledger, and it carries the
        /// same obligation: <c>seed-config.mjs</c> publishes it, because a keeper level the
        /// server derives differently from the device is a published card that silently drops
        /// whatever that level gated (invariant 19a).
        /// </summary>
        public XpBoostDto xpBoost;

        /// <summary>
        /// The rank ladder. Optional; see <see cref="Ranks.RankLadder"/>.
        ///
        /// <para>
        /// <b>Reaches no server</b>, like <see cref="notifications"/> and for a sharper reason: a
        /// rank is <em>derived</em> from records the server already validates and pays nothing at
        /// all, so there is nothing to adjudicate, nothing to clamp and nothing to publish. The
        /// day a rung pays a currency, this block joins <c>seed-config.mjs</c> and invariant 13
        /// starts applying to it.
        /// </para>
        /// </summary>
        public RanksDto ranks;
    }

    /// <summary>
    /// The rank ladder: every rung, humblest first. Read by <c>RankLadder.Resolve</c>.
    ///
    /// <para>
    /// <b>Authored order is the ladder</b>, exactly as it is for <see cref="TaskTableDto.tiers"/>
    /// — a rung carries no number of its own, so reordering the array reorders the ladder and
    /// there is no second place for a position to be written down and disagree.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class RanksDto
    {
        public RankRungDto[] rungs;

        /// <summary>Whether the file wrote this block at all; see <see cref="DailyChestEntryDto.IsAuthored"/>.</summary>
        public bool IsAuthored => rungs != null && rungs.Length > 0;
    }

    /// <summary>
    /// One rung. <c>id</c> is permanent in the way a chest tier's is — it names the badge on disk
    /// (<c>Ui/Rank/{id}</c>) and the loc keys (<c>rank.{id}.name</c>, <c>.blurb</c>), all derived
    /// — and <c>requires</c> is every line that has to be met, all of them.
    /// </summary>
    [Serializable]
    public sealed class RankRungDto
    {
        public string id;
        public RankRequirementDto[] requires;
    }

    /// <summary>
    /// One line of a rung: a <c>RankMeasures</c> id, optionally what it is about, and how much.
    ///
    /// <para>
    /// <c>measure</c> is either one of the derived readings (<c>levels_cleared</c>, <c>stars</c>,
    /// <c>three_stars</c>, <c>keeper_level</c>, <c>best_wave</c>) or any <c>TaskGoals</c> id
    /// counted for ever — so a verb added for a task slate is a rank requirement the same day,
    /// with no code. <c>scope</c> names a chapter for the first three, a level for
    /// <c>best_wave</c>, and must be absent for everything else; empty means the whole account.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class RankRequirementDto
    {
        public string measure;
        public string scope;
        public int target;
    }

    /// <summary>
    /// The XP boost's windows and what they pay.
    ///
    /// <para>
    /// Every field is unwritten as -1 so a partial block inherits rather than zeroing —
    /// <c>HintsDto</c>'s convention, and load-bearing here because nought is a real decision for
    /// all of them: a nought percentage withdraws a window, and a nought cooldown makes one
    /// unmetered. The default has to be distinguishable from the author's choice.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class XpBoostDto
    {
        /// <summary>What a watched window adds, as a percentage. Unwritten inherits; 0 withdraws it.</summary>
        public int watchedPercent = -1;

        /// <summary>How long a watched window runs, in hours.</summary>
        public int watchedHours = -1;

        /// <summary>
        /// How long after a watched window <em>starts</em> before another may be taken, in hours.
        /// The "every N hours" a shop card prints.
        /// </summary>
        public int watchedCooldownHours = -1;

        /// <summary>What a bought window adds, as a percentage. Unwritten inherits; 0 withdraws it.</summary>
        public int boughtPercent = -1;

        /// <summary>How long a bought window runs, in hours.</summary>
        public int boughtHours = -1;

        /// <summary>
        /// The most every running window may add together.
        ///
        /// <b>It is also the factor the stored bonus is clamped against</b>
        /// (<c>XpBoost.BonusFrom</c>), so raising it widens what a forged save may claim as well
        /// as what an honest one may earn. One number for both, so the bound cannot drift away
        /// from the rule it bounds.
        /// </summary>
        public int maxPercent = -1;

        /// <summary>Whether the file wrote this block at all; see <see cref="DailyChestEntryDto.IsAuthored"/>.</summary>
        public bool IsAuthored => watchedPercent >= 0 || watchedHours >= 0
                               || watchedCooldownHours >= 0 || boughtPercent >= 0
                               || boughtHours >= 0 || maxPercent >= 0;
    }

    /// <summary>
    /// What a wave seen off on the Infinite lane is worth, and the most that is ever paid.
    ///
    /// <para>
    /// Two numbers, both unwritten as -1 so a partial block inherits rather than zeroing —
    /// <c>HintsDto</c>'s convention, and important here for the same reason it is there: a field
    /// <c>JsonUtility</c> never saw reads as nought, and nought is a meaningful value for both
    /// of these (it withdraws the payment). The default has to be distinguishable from an
    /// author's decision to stop paying.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class EndlessRewardDto
    {
        /// <summary>XP per wave cleared. Unwritten reads as -1 and inherits; 0 stops paying.</summary>
        public int xpPerWave = -1;

        /// <summary>
        /// The most lifetime waves ever paid for, across every endless level. Unwritten inherits.
        ///
        /// Applied when the XP is derived and never when the count is stored — see
        /// <c>EndlessLimits.HardMaxWaves</c> for why that distinction is the whole safety of a
        /// mergeable count.
        /// </summary>
        public int maxWaves = -1;

        /// <summary>Credits per wave cleared. Unwritten reads as -1 and inherits; 0 stops paying.</summary>
        public int creditsPerWave = -1;

        /// <summary>
        /// The most credits this lane may pay in one day. Unwritten inherits; 0 stops paying.
        ///
        /// <b>A bound rather than a tuning.</b> A wave count cannot be recomputed by the server,
        /// so this ceiling is the entire defence against a forged one (invariant 13's fourth
        /// clause) - see <c>EndlessLimits.MaxDailyCreditCap</c>, which is what this is checked
        /// against, and the server's own copy in the wallet, which is what actually enforces it.
        /// </summary>
        public int dailyCreditCap = -1;

        /// <summary>Whether the file wrote this block at all; see <see cref="DailyChestEntryDto.IsAuthored"/>.</summary>
        public bool IsAuthored => xpPerWave >= 0 || maxWaves >= 0;
    }

    /// <summary>
    /// The notification slate: which kinds are sent, how they rank, and the hours of the
    /// player's own day they are sent at.
    ///
    /// <para>
    /// There is deliberately no text here. A notification's copy is derived from its kind's
    /// permanent id (<c>NotificationKinds.TitleKey</c>) and resolved through the string table,
    /// so a content push can switch a reminder off, reorder the ladder or move the hours — and
    /// cannot write a sentence, because a sentence has to be translated and translations ship
    /// in the build. Invariant 39c's split, said about words instead of pictures.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class NotificationsDto
    {
        /// <summary>The most this game may say in one local day. Unwritten reads as -1 and inherits.</summary>
        public int perDay = -1;

        /// <summary>
        /// How many days ahead the schedule reaches. Unwritten inherits.
        ///
        /// Bounded by <c>NotificationWindow.MaxPending</c> times <see cref="perDay"/>, because
        /// iOS keeps the 64 soonest pending local notifications and drops the rest in silence.
        /// </summary>
        public int horizonDays = -1;

        /// <summary>
        /// How many days keep the full <see cref="perDay"/> before the schedule thins to one a
        /// night. Unwritten reads as -1 and inherits.
        ///
        /// What it really tunes is <em>reach</em>: the ceiling is a count of pending
        /// notifications, so a flat three a day spends the whole allowance inside a week, and
        /// tapering spends it over three. Past the horizon this scheme is silent.
        /// </summary>
        public int taperAfterDays = -1;

        /// <summary>The three times of day. Optional; the built-in hours stand without it.</summary>
        public NotificationHoursDto hours;

        /// <summary>The slate. Order is immaterial; <c>priority</c> is the ranking.</summary>
        public NotificationEntryDto[] entries;

        /// <summary>Whether the file wrote this block at all; see <see cref="DailyChestEntryDto.IsAuthored"/>.</summary>
        public bool IsAuthored => entries != null || hours != null || perDay > 0
                               || horizonDays > 0 || taperAfterDays > 0;
    }

    /// <summary>
    /// The three slot hours, in minutes past <em>local</em> midnight.
    ///
    /// Minutes rather than an hour, so 19:30 is expressible; local rather than UTC, because
    /// 09:00 UTC is three in the morning for a third of the world. Held inside
    /// <c>NotificationWindow</c>'s waking-day bounds, which a content push cannot move.
    /// </summary>
    [Serializable]
    public sealed class NotificationHoursDto
    {
        public int morning = -1;
        public int afternoon = -1;
        public int evening = -1;

        public bool IsAuthored => morning >= 0 || afternoon >= 0 || evening >= 0;
    }

    /// <summary>
    /// One reminder. <c>kind</c> is a <c>NotificationKinds.Id</c> and is permanent — it names
    /// the loc keys the copy is drawn from and the analytics series an open is recorded
    /// against — <c>slot</c> is one of <c>any</c>/<c>morning</c>/<c>afternoon</c>/<c>evening</c>,
    /// <c>priority</c> decides a contested slot, and <c>minDaysBetween</c> is what stops the
    /// same sentence arriving every day for a week.
    /// </summary>
    [Serializable]
    public sealed class NotificationEntryDto
    {
        public string kind;
        public string slot;
        public int priority;
        public int minDaysBetween = 1;

        /// <summary>
        /// Takes this kind out of the slate while keeping its row.
        ///
        /// Written as <c>disabled</c> rather than <c>enabled</c> for <c>ManifestChapterDto</c>'s
        /// reason: <c>JsonUtility</c> reads an absent bool as <c>false</c>, so the unwritten
        /// state has to be the one that means "ordinary".
        /// </summary>
        public bool disabled;
    }

    /// <summary>
    /// The utility catalog, as authored.
    ///
    /// A wrapper around one array rather than a bare array, for the reason every other block in
    /// <see cref="ProgressionDto"/> is an object: <c>JsonUtility</c> gives a class-typed field an
    /// instance even when the JSON has no such key, so a block has to carry a value a real one
    /// cannot hold to be distinguishable from an absent one — here, an empty <see cref="items"/>.
    /// </summary>
    [Serializable]
    public sealed class UtilitiesDto
    {
        /// <summary>In bar order, which is authored on each entry rather than by position.</summary>
        public UtilityDto[] items;
    }

    /// <summary>
    /// One utility. <c>kind</c> is a permanent id — <c>blast</c>, <c>mend</c>, <c>surge</c> —
    /// and what <c>magnitude</c> measures depends on it: damage, health, or fuel in tenths.
    /// </summary>
    [Serializable]
    public sealed class UtilityDto
    {
        /// <summary>Permanent. The save keys on it and a published chest table names it.</summary>
        public string id;

        public string kind;

        /// <summary>Damage, health, or fuel-tenths, depending on <see cref="kind"/>.</summary>
        public int magnitude;

        /// <summary>Gems for one, or nought for a utility only a chest hands out.</summary>
        public int gemPrice;

        /// <summary>The most a player may hold. A grant past it is refused, never clamped away.</summary>
        public int maxHeld;

        /// <summary>Where it sits on the bar: 1 up, no gaps and no ties.</summary>
        public int order;

        /// <summary>
        /// The keeper level before this one may be bought. Absent or nought asks nothing.
        ///
        /// It gates buying and never spending: a utility already in hand is spendable whatever
        /// this says, or a retune would confiscate something bought with gems.
        /// </summary>
        public int minLevel;

        /// <summary>
        /// Whole seconds before another may be used. Absent or nought means no cooldown, which
        /// is exactly how the bar behaved before the field existed — the shape every optional
        /// number here takes, because <c>JsonUtility</c> writes a nought into a field an older
        /// file never had.
        /// </summary>
        public int cooldownSeconds;
    }

    /// <summary>The turret roster. In shelf order, which is authored on each entry.</summary>
    [Serializable]
    public sealed class WardsDto
    {
        public WardModelDto[] models;

        /// <summary>
        /// What each upgrade costs, by band. Absent leaves the built-in ladder standing.
        ///
        /// <b>Named for the upgrades rather than for the stars</b>, because <c>GroveScoreDto</c>
        /// already has a <c>stars</c> that is an <em>array</em> and may legitimately be null —
        /// and <c>compile.py</c>'s DTO check is name-based, so one class-typed <c>stars</c>
        /// anywhere makes every <c>.stars == null</c> test in the project read as the trap
        /// invariant <c>HollowDto.IsAuthored</c> records.
        /// </summary>
        public WardStarLadderDto upgrades;
    }

    /// <summary>
    /// The upgrade ladder: one row of prices per <c>WardTier</c>.
    ///
    /// <b>A row type rather than a jagged array</b>, because <c>JsonUtility</c> cannot serialise
    /// one at all — it reads <c>int[][]</c> as nothing and says so nowhere, which is the class of
    /// silence this project has already paid for twice (a <c>[Serializable]</c> field that is
    /// never null, and an unknown block dropped without a word).
    /// </summary>
    [Serializable]
    public sealed class WardStarLadderDto
    {
        /// <summary>One row per band, lowest first.</summary>
        public WardStarPricesDto[] tiers;
    }

    [Serializable]
    public sealed class WardStarPricesDto
    {
        /// <summary>Credits for the second star, then the third, fourth and fifth.</summary>
        public int[] prices;
    }

    /// <summary>
    /// One turret. <c>ability</c> is a permanent id - <c>splash</c>, <c>chain</c>, <c>frost</c> -
    /// and what <c>magnitude</c> and <c>extent</c> measure depends on it; see <c>WardAbility</c>.
    /// </summary>
    [Serializable]
    public sealed class WardModelDto
    {
        /// <summary>Permanent. The save keys both what is owned and what is chosen on it.</summary>
        public string id;

        public string ability;

        /// <summary>How strong the ability is, in tenths of whatever it measures.</summary>
        public int magnitude;

        /// <summary>The ability's second number: a reach, a duration in tenths, a period.</summary>
        public int extent;

        /// <summary>
        /// What its bolt is worth, in tenths of the baseline. Ten is the floor and nought means
        /// ten. See <c>WardModel.PowerTenths</c> for why it may never be less.
        /// </summary>
        public int power;

        /// <summary>
        /// What it can take before it falls, in tenths of the baseline. Nought means ten, and
        /// below ten is legal — that is the trade. See <c>WardModel.GuardTenths</c>.
        /// </summary>
        public int guard;

        /// <summary>Gems for one. Asks for no keeper level. Nought means it is not sold for gems.</summary>
        public int gemPrice;

        /// <summary>Credits for one. Gated on <see cref="minLevel"/>. Nought means not sold.</summary>
        public int coinPrice;

        /// <summary>The keeper level a credit purchase asks for. Meaningless beside a gem price.</summary>
        public int minLevel;

        /// <summary>Where it sits on the shelf: 1 up, no gaps and no ties.</summary>
        public int order;

        /// <summary>
        /// Whether it wears no colour: bought once rather than once per seat, stands on any seat,
        /// and fires at everything on the hill. See <c>WardModel.Legendary</c>.
        ///
        /// <para>
        /// <b>Authored rather than read off the rung</b>, because <c>WardTier</c> is punctuation
        /// over the shelf's order and a band that silently decided what a turret <em>does</em>
        /// would make a re-rung shelf change four turrets' rules. Both content gates refuse a
        /// legendary outside the legendary band and anything else inside it
        /// (<c>WardCatalog.LadderProblem</c>).
        /// </para>
        /// <para>
        /// <b>False is the honest default and needs no migration</b> - it is what a file written
        /// before the band existed means, and what <c>JsonUtility</c> writes into a field an
        /// older file never had. It is the one place in this DTO where the zero value is also the
        /// answer (compare <c>minLevel</c>, where nought is a real state).
        /// </para>
        /// </summary>
        public bool legendary;
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
        public string eventPassId;
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

        /// <summary>
        /// A flat number of stars, whatever the chapter holds. Unwritten reads as -1 and the
        /// per-level figure decides instead; 0 opens every chapter at once.
        ///
        /// <b>It wins over <see cref="starsPerLevel"/> when both are written</b>, because a
        /// total is the more specific statement — and it is clamped to the stars the chapter
        /// behind actually pays, so a flat figure can never be a gate no play could open.
        /// </summary>
        public int stars = -1;
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
        /// What each continue already taken adds to the next one's price, <em>after</em>
        /// <see cref="gemsFactor"/> has been applied. 0 is what ships, so the escalation is
        /// purely geometric.
        /// </summary>
        public long gemsStep = -1L;

        /// <summary>
        /// What each continue already taken multiplies the next one's price by, in hundredths.
        /// 200 ships, so the price <b>doubles</b> every time: 20, 40, 80, 160, 320 gems on one
        /// run. 100 holds it still, which with a <see cref="gemsStep"/> is the linear ladder
        /// this block shipped with.
        ///
        /// <para>
        /// -1 inherits, which is the doubling. A published table written before this key
        /// existed therefore starts doubling the moment a client that knows about it reads it
        /// — deliberate, because the alternative is a live document silently holding the price
        /// flat on every device until somebody remembers to re-push it.
        /// </para>
        /// </summary>
        public long gemsFactor = -1L;

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

        /// <summary>
        /// <b>Retired.</b> Tiles a Groovekeeper continue handed over. Kept for <c>ink</c>'s
        /// reason: a published table still carries the key, and deleting it would make every one
        /// of them read as malformed for no gain.
        /// </summary>
        public int tiles = -1;

        /// <summary>Moves a prototype board's continue hands over, on the same terms.</summary>
        public int moves = -1;

        /// <summary>
        /// Wards a siege's continue puts back up. <b>Not</b> on the same terms as the others:
        /// this is the whole allowance rather than room above a shortfall, because a siege is
        /// lost when the last ward falls. See <c>ContinueLimits.DefaultWards</c>.
        /// </summary>
        public int wards = -1;
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

        /// <summary>
        /// Whether the file wrote this chest at all. <c>JsonUtility</c> instantiates a class
        /// field the file never mentioned, so a null test says nothing; an entry with neither
        /// array is a value a real chest cannot hold.
        /// </summary>
        public bool IsAuthored => guaranteed != null || options != null;
    }

    /// <summary>
    /// The task rules: the chest ladder, the two slates and how many of each are dealt at a
    /// time. See <c>TaskTable</c>.
    ///
    /// <para>
    /// Every reward here is a chest, and a chest is a <see cref="TaskTierDto"/> — so a task
    /// authors no amounts, only which rung of the ladder it pays. Optional and not its own
    /// schema version, for <see cref="DailyChestDto"/>'s reason: a client that predates it
    /// keeps its built-in slate.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class TaskTableDto
    {
        /// <summary>How many of each slate are dealt per period. Unwritten reads as -1 and inherits.</summary>
        public int activePerPeriod = -1;

        /// <summary>The chest ladder, humblest first. Position is the rank.</summary>
        public TaskTierDto[] tiers;

        /// <summary>Dealt again every day. Order is the rotation.</summary>
        public TaskEntryDto[] daily;

        /// <summary>Dealt again every week. Order is the rotation.</summary>
        public TaskEntryDto[] weekly;

        /// <summary>Whether the file wrote this block at all; see <see cref="DailyChestEntryDto.IsAuthored"/>.</summary>
        public bool IsAuthored => tiers != null || daily != null || weekly != null;
    }

    /// <summary>One rung of the chest ladder. <c>id</c> is permanent: it names art and copy.</summary>
    [Serializable]
    public sealed class TaskTierDto
    {
        public string id;

        /// <summary>What opening one pays, in the daily chest's own shape.</summary>
        public DailyChestEntryDto chest;

        /// <summary>
        /// What claiming one earns toward a season. Absent is nought, which is a legal
        /// answer — see <c>ChestTier.Marks</c>.
        /// </summary>
        public int marks;
    }

    /// <summary>
    /// One task. <c>id</c> is permanent — it is written into save files and claim ids —
    /// <c>goal</c> is a <c>TaskGoals</c> id, <c>target</c> how many, <c>tier</c> a
    /// <see cref="TaskTierDto.id"/>, and <c>retired</c> takes it out of the rotation while
    /// keeping it priced, so a claim already in flight still resolves.
    /// </summary>
    [Serializable]
    public sealed class TaskEntryDto
    {
        public string id;
        public string goal;
        public int target;
        public string tier;
        public bool retired;
    }

    /// <summary>
    /// The refer-a-friend block. Read by <c>ReferralTable.Resolve</c>; published by
    /// <c>seed-config.mjs</c> as <c>config/progression.referral</c>, because every number in
    /// it is adjudicated on the server and the client's copy only ever draws.
    ///
    /// <para>
    /// <c>milestoneChapter</c> is the chapter an invitee has to clear before either side is
    /// paid, named rather than derived so a content push can move it. <c>maxBound</c> is how
    /// many invitees may ever bind to one code — bound, not finished, because a list that
    /// only grows on the invitee's play would be a list with no ceiling. <c>perInvitee</c> is
    /// what the referrer opens for each invitee who clears it; <c>invitee</c> what the
    /// invitee opens on clearing it. Each names a tier of the <c>tasks</c> block (invariant 45)
    /// and a count. <c>withdrawn</c> takes the feature off the screen without deleting the
    /// block.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ReferralDto
    {
        public string milestoneChapter;
        public int maxBound;
        public string shareLink;
        public ReferralPaymentDto invitee;
        public ReferralPaymentDto perInvitee;
        public bool withdrawn;

        /// <summary>Whether the file wrote this block at all; see <see cref="DailyChestEntryDto.IsAuthored"/>.</summary>
        public bool IsAuthored => maxBound > 0 || withdrawn || !string.IsNullOrEmpty(milestoneChapter);
    }

    [Serializable]
    public sealed class ReferralPaymentDto
    {
        public string tier;

        /// <summary>How many chests of that tier. Absent reads as one.</summary>
        public int count;
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

        /// <summary>
        /// Which thing, for a kind that names one — today, the utility a <c>utility</c> band
        /// pays. Ignored by every other kind, and required by the ones that need it.
        ///
        /// It does not reach the generator, so adding one to a shipped table cannot reroll an
        /// unopened chest.
        /// </summary>
        public string item;
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

        /// <summary>Which thing, for a kind that names one. See <see cref="DailyDropDto.item"/>.</summary>
        public string item;

        /// <summary>Relative chance. The reader rejects anything below 1.</summary>
        public int weight;

        public DailyDropDto AsBand()
            => new DailyDropDto { kind = kind, min = min, max = max, item = item };
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
        /// Night one first. An entry with neither a <c>kind</c> nor a <c>tier</c> pays
        /// nothing, which is how a night that only marks time is authored.
        ///
        /// The list is one <em>lap</em> rather than the whole ladder: night eight pays what
        /// night one pays, for ever. So its length is also the length of the board a player
        /// sees, and lengthening it lengthens the week.
        /// </summary>
        public StreakRungDto[] rungs;

        /// <summary>
        /// How many calendar days a bought shield covers, counting the day it was bought.
        /// Absent is <c>StreakRules.DefaultShieldDays</c>.
        ///
        /// Content rather than a constant, which is why no string in this game says "seven":
        /// every sentence about the shield takes this number as an argument.
        /// </summary>
        public int shieldDays;

        /// <summary>
        /// What a shield costs in gems. Absent is <c>StreakRules.DefaultShieldGems</c>; an
        /// explicit <b>zero withdraws the offer</b>, which is the one thing a missing field
        /// must not be able to do by accident.
        ///
        /// It is an ordinary gem debit (invariant 18) rather than anything the server
        /// adjudicates, so this price is not published and is not part of any wire contract —
        /// the shield grants no currency and keeps a streak inside the same one-night-a-day
        /// bound an unprotected one is already held to.
        /// </summary>
        public int shieldGems;
    }

    /// <summary>
    /// One day of the streak ladder: credits, gems, or a chest.
    ///
    /// <para>
    /// <b>A rung names a <c>kind</c> and an <c>amount</c>, or a <c>tier</c>, never both.</b>
    /// The tier is an id out of the <c>tasks</c> block's chest ladder, so a streak night that
    /// pays a royal chest is the <em>same</em> authored chest a weekly task pays — one
    /// published disclosure, one retune (invariant 45).
    /// </para>
    /// <para>
    /// <b>Currency is adjudicated.</b> A rung is claimed as
    /// <c>streak:{day}:{night}:{currency}</c> and paid from the server's own copy of this
    /// ladder — a chest rung out of the same id, re-rolled rather than believed — so retuning
    /// it here and forgetting to re-seed means the server pays the old figure. See
    /// <c>StreakTable</c> for the whole path, and run the seeder after any change. The
    /// per-kind ceilings in <c>StreakRules</c> apply on both sides.
    /// </para>
    /// <para>
    /// <c>hearts</c> and <c>heart_boost</c> are <b>refused by name</b> here: they reach this
    /// ladder through a chest tier now. See <c>StreakRules.IsRetiredKind</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class StreakRungDto
    {
        public string kind;
        public int amount;

        /// <summary>A chest tier id from the <c>tasks</c> block, or empty for a currency rung.</summary>
        public string tier;
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

    /// <summary>One line of dialogue: who says it, and which string.</summary>
    [Serializable]
    public sealed class StoryLineDto
    {
        /// <summary>A <c>StoryCast</c> id. A name nothing recognises is refused by the gate.</summary>
        public string who;

        /// <summary>The loc key. See <c>StoryLine.Key</c> for why it is authored, not derived.</summary>
        public string key;
    }

    /// <summary>Everything said at one moment of a run.</summary>
    [Serializable]
    public sealed class StoryBeatDto
    {
        /// <summary>One of <c>StoryScript.CueNames</c>. Anything else is a build error.</summary>
        public string cue;

        public StoryLineDto[] lines;
    }

    /// <summary>
    /// A level's dialogue.
    ///
    /// <b>Never test this block for null.</b> <c>JsonUtility</c> instantiates a
    /// <c>[Serializable]</c> class field on every level in the game, so <c>dto.story != null</c>
    /// is true for every level ever parsed, including the ninety-one that have never had a word
    /// written for them. Absence has to be a value a real script cannot hold, which is invariant
    /// 11b's shape reached from the parser's side. Arrays are the one thing <c>JsonUtility</c>
    /// does leave null, which is exactly why <see cref="beats"/> is one.
    /// </summary>
    [Serializable]
    public sealed class StoryDto
    {
        public StoryBeatDto[] beats;

        public bool IsAuthored => beats != null && beats.Length > 0;
    }
}
