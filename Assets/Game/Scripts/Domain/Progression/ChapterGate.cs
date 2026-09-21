using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds that survive any content file, and the number used when there is none.
    ///
    /// <para>
    /// <c>AccountPromptLimits</c>' job for the chapter gate: content may retune how much of a
    /// chapter opens the next one, it may not redefine what a star is. Everything here is a
    /// compile-time constant precisely because it is what a published file is checked
    /// <em>against</em> — a limit that could itself be published would not be a limit.
    /// </para>
    /// </summary>
    public static class ChapterGateLimits
    {
        /// <summary>
        /// Two of a level's three stars, so a ten-glade chapter opens the next one at 20 of 30.
        ///
        /// <para>
        /// Written per level rather than as a total because a chapter is not a fixed size. A
        /// total of 20 is two thirds of a ten-glade chapter and a fifth of a fifty-glade one,
        /// so the rule a player learned on their first chapter would quietly stop being the
        /// rule — while "two stars a level" is the same sentence whatever the chapter holds,
        /// and it is the sentence the information panel prints.
        /// </para>
        /// </summary>
        public const int DefaultStarsPerLevel = 2;

        /// <summary>
        /// Zero is legal and is the point of the lever.
        ///
        /// <para>
        /// If the gate turns out to be a wall — and a gate is the one kind of tuning whose
        /// damage is players who stop playing rather than an economy that drifts — the fix has
        /// to be available in minutes rather than in a store review. Zero opens every chapter
        /// at once and leaves the level-by-level chain inside each chapter exactly as it was,
        /// which is a working game and a coherent one.
        /// </para>
        /// </summary>
        public const int MinStarsPerLevel = 0;

        /// <summary>
        /// A level cannot pay more than three stars, so more than three per level would be a
        /// gate no amount of play could open. Three itself is legal and means perfect play.
        /// </summary>
        public const int MaxStarsPerLevel = LevelRecord.MaxStars;

        /// <summary>
        /// The most a flat gate may ask for, before it is cut down to what the chapter behind
        /// can actually pay.
        ///
        /// <para>
        /// <b>A typo guard rather than a design opinion</b>, and it is the loose half of the
        /// rule: the tight half is the clamp in <see cref="ChapterGateTable.RequiredStars"/>,
        /// which knows how many stars are really on offer and is the thing that makes a flat
        /// figure safe. This only catches a number that was never meant.
        /// </para>
        /// </summary>
        public const int MaxStars = 999;
    }

    /// <summary>
    /// How much of a chapter opens the chapter after it — content, not code.
    ///
    /// <para>
    /// It is here for the reason the heart gate and the ad caps are here. This paces the
    /// content itself: it decides how much of a chapter a player has to master before the next
    /// one opens, which decides how long a drop lasts and how often somebody is sent back to a
    /// glade they have already finished. The right value is discovered from live completion
    /// rates rather than known in advance, and shipping it as a <c>const</c> would mean that
    /// finding out the gate is too tight costs a store review — the mistake this project has
    /// already recorded against the heart gate, the chest odds and the clock.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> published to <c>config/progression</c> by the seeder, for
    /// <c>difficulty</c>'s and <c>prompts</c>' reason: nothing about unlocking is adjudicated.
    /// A chapter opening pays nothing, mints nothing and is written nowhere — it is a pure
    /// function of the star ledger the server already validates for currency, so there is no
    /// second answer for a retune to put out of step with the first.
    /// </para>
    /// <para>
    /// Like every other optional block this is not a schema bump — a client that predates it
    /// keeps the built-in gate.
    /// </para>
    /// </summary>
    public sealed class ChapterGateTable
    {
        ChapterGateTable(int starsPerLevel, int stars = 0)
        {
            StarsPerLevel = starsPerLevel;
            Stars = stars;
        }

        /// <summary>Stars per level of the chapter behind it. 0 opens everything, 3 is perfect play.</summary>
        public int StarsPerLevel { get; }

        /// <summary>
        /// True when the gate asks for nothing, so every chapter stands open.
        ///
        /// <b>Both halves, for invariant 15a's reason</b>: a flat figure standing over a rate
        /// of nought is still a gate, and a reading that asked only the rate would report every
        /// chapter open while the map kept them shut.
        /// </summary>
        public bool IsOpenToAll => Stars <= 0 && StarsPerLevel <= 0;

        /// <summary>
        /// A flat number of stars, or 0 to use <see cref="StarsPerLevel"/> instead.
        ///
        /// <para>
        /// <b>Added because the per-level shape could not express the figure that was
        /// wanted.</b> Two stars a level is the same sentence whatever a chapter holds, which
        /// is why it was the only shape for a year - but it can only ever ask a ten-glade
        /// chapter for 10, 20 or 30, and the number the owner wanted was 16. A total says less
        /// about a chapter of another size, and it says exactly what was meant about this one.
        /// </para>
        /// <para>
        /// <b>It is always cut down to the stars really on offer</b>
        /// (<see cref="RequiredStars"/>), so the danger a total carries and a rate does not -
        /// asking a five-glade chapter for sixteen of its fifteen stars - is unreachable
        /// rather than merely unlikely.
        /// </para>
        /// </summary>
        public int Stars { get; }

        /// <summary>The gate that ships inside the build.</summary>
        public static readonly ChapterGateTable Default =
            new ChapterGateTable(ChapterGateLimits.DefaultStarsPerLevel);

        /// <summary>
        /// Stars needed to open the chapter after one holding <paramref name="levelCount"/>
        /// levels.
        ///
        /// <para>
        /// Integer arithmetic, and never a fraction of the chapter's maximum. A fraction is a
        /// float, two runtimes round one differently — see what that cost the weave generator —
        /// and the one thing a player has to be able to do with a gate is count towards it.
        /// </para>
        /// </summary>
        public int RequiredStars(int levelCount)
        {
            if (levelCount <= 0) return 0;

            // Everything a chapter of this size could ever pay. The clamp below is what lets a
            // flat figure be authored at all: without it, a total larger than the chapter
            // behind can give is a chapter nobody opens, and the symptom is a player sent back
            // to glades they have already three-starred.
            int available = levelCount * LevelRecord.MaxStars;

            int wanted = Stars > 0 ? Stars : StarsPerLevel * levelCount;
            return wanted > available ? available : wanted;
        }

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>chapterGate</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in gate stands,
        /// because a content mistake must fail a build and never a session.
        /// </summary>
        public static ChapterGateTable Resolve(ChapterGateDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                  // absent is not an error

            int flat = dto.stars;

            if (flat > ChapterGateLimits.MaxStars)
            {
                problems.Add($"chapterGate stars is {flat}, above the " +
                             $"{ChapterGateLimits.MaxStars} this reader will carry; clamped");
                flat = ChapterGateLimits.MaxStars;
            }

            if (flat < 0) flat = 0;                           // unset, the file's own convention

            int stars = dto.starsPerLevel;

            // **Unset means the built-in rate, and that has to survive a flat figure being
            // written beside it.** The first version returned `Default` here whenever the rate
            // was unwritten, which threw the flat figure away in the one file that would ever
            // author it alone - a gate silently back at two a level, and nothing anywhere
            // saying so.
            if (stars < 0) stars = ChapterGateLimits.DefaultStarsPerLevel;

            if (stars > ChapterGateLimits.MaxStarsPerLevel)
            {
                problems.Add($"chapterGate starsPerLevel is {stars}, above the " +
                             $"{ChapterGateLimits.MaxStarsPerLevel} a level can ever pay, so no " +
                             "amount of play could open a chapter; clamped");
                stars = ChapterGateLimits.MaxStarsPerLevel;
            }

            return new ChapterGateTable(stars, flat);
        }
    }

    /// <summary>
    /// The live gate, read the way <c>HeartRules</c> and <c>AccountPromptRules</c> are — a
    /// facade over the published table, so a call site reads as it did when this was a rule
    /// nobody could tune.
    /// </summary>
    public static class ChapterGateRules
    {
        public static ChapterGateTable Table => ProgressionRules.Table.ChapterGate;
    }

    /// <summary>
    /// What stands between a player and a chapter, as numbers they can count towards.
    ///
    /// <para>
    /// Deliberately a plain reading of integers rather than a call into anything. The map
    /// draws it, the victory panel decides from it whether a chapter just opened, the
    /// information panel explains it and <see cref="LevelUnlock"/> answers with it — one struct
    /// is what stops those four coming to disagree about what the gate is, which is exactly
    /// what happened to the companion unlock rule while it was answered in two places.
    /// </para>
    /// <para>
    /// <b>Two halves, and one struct is what keeps them from being checked one at a time.</b>
    /// A chapter can ask for stars in the chapter behind it (<see cref="Required"/>) and for a
    /// keeper level of its own (<see cref="RequiredLevel"/>), and invariant 15a is the whole
    /// reason they live together: a companion's unlock was <em>two</em> predicates for a year,
    /// and a call site checking half a rule under a name promising all of it is how somebody's
    /// purchase stayed behind a padlock. <see cref="IsOpen"/> is both halves, so no caller can
    /// ask for less than the rule.
    /// </para>
    /// <para>
    /// <b>The level half is the one reported when both apply</b>, which is
    /// <c>HomesteadLedger</c>'s rule (invariant 16s) read across: when two refusals stand, the
    /// one worth saying is the one the nearer currency cannot answer. A player told "20 of 30
    /// stars to go on" who then meets a second wall has been given a number that was never the
    /// whole price.
    /// </para>
    /// <para>
    /// It is filled from <see cref="PlayerProgress"/> by <see cref="LevelUnlock.GateFor"/> and
    /// holds no live state of its own, so every decision in it is proved offline against plain
    /// integers — the house rule that the deciding lives in Domain and only the drawing does not.
    /// </para>
    /// </summary>
    public readonly struct ChapterGate
    {
        /// <summary>The chapter whose stars are counted — the one <em>before</em> the gate.</summary>
        public readonly ChapterId Behind;

        /// <summary>Stars the gate asks for. Zero when there is nothing to ask.</summary>
        public readonly int Required;

        /// <summary>Stars the player holds in <see cref="Behind"/>.</summary>
        public readonly int Held;

        /// <summary>The most <see cref="Behind"/> can hold, so a readout can say "18 of 30".</summary>
        public readonly int Available;

        /// <summary>
        /// The keeper level this chapter asks for, or nought when it asks for none.
        ///
        /// <b>A fact about <em>this</em> chapter, where the star half is about the one behind
        /// it.</b> That asymmetry is not an accident: stars are how well the previous chapter
        /// was played, and a keeper level is how much of the game has been played at all — so a
        /// lane's <em>first</em> chapter, which has nothing behind it and therefore no star
        /// gate, can still carry a wall. That is what the Infinite lane is.
        /// </summary>
        public readonly int RequiredLevel;

        /// <summary>The keeper level the player is at, so a readout can say "level 7 of 10".</summary>
        public readonly int KeeperLevel;

        public ChapterGate(ChapterId behind, int required, int held, int available)
            : this(behind, required, held, available, 0, 0) { }

        public ChapterGate(ChapterId behind, int required, int held, int available,
                           int requiredLevel, int keeperLevel)
        {
            Behind = behind;
            Required = required < 0 ? 0 : required;
            Held = held < 0 ? 0 : held;
            Available = available < 0 ? 0 : available;
            RequiredLevel = requiredLevel < 0 ? 0 : requiredLevel;
            KeeperLevel = keeperLevel < 0 ? 0 : keeperLevel;
        }

        /// <summary>
        /// Nothing stands in the way: the first chapter of a mode, or a gate asking for zero.
        ///
        /// <para>
        /// Note that this is <em>open</em> rather than absent. A caller asking "may I go on"
        /// gets a yes with no special case of its own, and a caller that wants to draw the gate
        /// asks <see cref="Exists"/> first.
        /// </para>
        /// </summary>
        public static readonly ChapterGate Open = new ChapterGate(ChapterId.None, 0, 0, 0);

        /// <summary>
        /// A chapter the catalog does not carry.
        ///
        /// Closed rather than open, because the alternative is that a typo in a manifest opens
        /// everything. It is deliberately not drawable — <see cref="Exists"/> is false, since
        /// it names no chapter — so nothing can print a requirement nobody can work towards.
        /// </summary>
        public static readonly ChapterGate Missing = new ChapterGate(ChapterId.None, 1, 0, 0);

        /// <summary>
        /// True when the player may go on — <b>both</b> halves met, never one.
        ///
        /// This is the whole rule and the only thing entitled to answer it. See the type's own
        /// note on invariant 15a for why the halves are not separately askable.
        /// </summary>
        public bool IsOpen => Held >= Required && KeeperLevel >= RequiredLevel;

        /// <summary>True when there is a requirement worth printing.</summary>
        public bool Exists => (Required > 0 && Behind.IsValid) || RequiredLevel > 0;

        /// <summary>
        /// True when the keeper wall is what is shutting this gate, and so the refusal to
        /// print.
        ///
        /// <b>Asked before the star line</b>, for the reason in the type's own note: when both
        /// stand, a star count is a number that was never the whole price.
        /// </summary>
        public bool NeedsLevel => KeeperLevel < RequiredLevel;

        /// <summary>Stars still to earn. Zero once the star half is met.</summary>
        public int Remaining => Held >= Required ? 0 : Required - Held;

        /// <summary>Keeper levels still to climb. Zero once the level half is met.</summary>
        public int LevelsRemaining
            => KeeperLevel >= RequiredLevel ? 0 : RequiredLevel - KeeperLevel;

        /// <summary>
        /// How far along the <em>star</em> half the player is, 0 to 1. One when that half asks
        /// for nothing, so a bar never divides by zero and never reads as empty at the start of
        /// a mode.
        ///
        /// <b>Deliberately not a reading of both halves.</b> Stars and keeper levels are
        /// different units, and averaging them would draw a bar reading nearly full with a wall
        /// still standing. The one place that draws this is the map's signpost, which is about
        /// the chapter behind; a caller wanting the other half asks
        /// <see cref="LevelsRemaining"/>.
        /// </summary>
        public float Fraction
        {
            get
            {
                if (Required <= 0) return 1f;
                float f = (float)Held / Required;
                return f > 1f ? 1f : f;
            }
        }

        public override string ToString()
        {
            if (!Exists) return "open";

            string stars = Required > 0 && Behind.IsValid
                ? $"{Behind}: {Held}/{Required} of {Available}"
                : null;
            string keeper = RequiredLevel > 0 ? $"keeper {KeeperLevel}/{RequiredLevel}" : null;

            return stars == null ? keeper
                 : keeper == null ? stars
                 : stars + ", " + keeper;
        }
    }
}
