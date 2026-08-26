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
        ChapterGateTable(int starsPerLevel) => StarsPerLevel = starsPerLevel;

        /// <summary>Stars per level of the chapter behind it. 0 opens everything, 3 is perfect play.</summary>
        public int StarsPerLevel { get; }

        /// <summary>True when the gate asks for nothing, so every chapter stands open.</summary>
        public bool IsOpenToAll => StarsPerLevel <= 0;

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
            => levelCount <= 0 ? 0 : StarsPerLevel * levelCount;

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

            int stars = dto.starsPerLevel;
            if (stars < 0) return Default;                    // unset, the file's own convention

            if (stars > ChapterGateLimits.MaxStarsPerLevel)
            {
                problems.Add($"chapterGate starsPerLevel is {stars}, above the " +
                             $"{ChapterGateLimits.MaxStarsPerLevel} a level can ever pay, so no " +
                             "amount of play could open a chapter; clamped");
                stars = ChapterGateLimits.MaxStarsPerLevel;
            }

            return new ChapterGateTable(stars);
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
    /// What stands between a player and the next chapter, as a number they can count towards.
    ///
    /// <para>
    /// Deliberately a plain reading of three integers rather than a call into anything. The map
    /// draws it, the victory panel decides from it whether a chapter just opened, the
    /// information panel explains it and <see cref="LevelUnlock"/> answers with it — one struct
    /// is what stops those four coming to disagree about what the gate is, which is exactly
    /// what happened to the companion unlock rule while it was answered in two places.
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

        public ChapterGate(ChapterId behind, int required, int held, int available)
        {
            Behind = behind;
            Required = required < 0 ? 0 : required;
            Held = held < 0 ? 0 : held;
            Available = available < 0 ? 0 : available;
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

        /// <summary>True when the player may go on.</summary>
        public bool IsOpen => Held >= Required;

        /// <summary>True when there is a requirement worth printing.</summary>
        public bool Exists => Required > 0 && Behind.IsValid;

        /// <summary>Stars still to earn. Zero once the gate is open.</summary>
        public int Remaining => Held >= Required ? 0 : Required - Held;

        /// <summary>
        /// How far along the gate the player is, 0 to 1. One when the gate asks for nothing, so
        /// a bar never divides by zero and never reads as empty at the start of a mode.
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
            => Exists ? $"{Behind}: {Held}/{Required} of {Available}" : "open";
    }
}
