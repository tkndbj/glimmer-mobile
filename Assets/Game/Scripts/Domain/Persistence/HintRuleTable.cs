using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The bounds that survive any content file, and the numbers used when there is none.
    ///
    /// <para>
    /// <see cref="HeartLimits"/>'s job for the hint pool, and the reasoning transfers whole:
    /// content is allowed to retune the pool, it is not allowed to redefine what the pool
    /// is. Everything here is a compile-time constant precisely because it is what a
    /// published file is checked <em>against</em> — a limit that could itself be published
    /// would not be a limit.
    /// </para>
    /// </summary>
    public static class HintLimits
    {
        // ------------------------------------------------------- the structural bound
        /// <summary>
        /// The most hints a <see cref="Hints"/> ledger can represent, ever.
        ///
        /// <para>
        /// <b>Not the ceiling a player experiences</b> — that is
        /// <see cref="HintRuleTable.Ceiling"/>, and it is content. This is the bound the
        /// ledger's own clamp uses, and the distinction is the single most important thing
        /// in this file. <see cref="HeartLimits.HardCeiling"/> carries the full argument;
        /// the short version is that clamping against a published number would cut
        /// <c>produced</c> downward on whichever devices had fetched a lowered table, and
        /// <c>produced</c> only ever rising is what the entire merge proof rests on.
        /// </para>
        /// <para>
        /// Deliberately the same generous 999 hearts use rather than something snug around
        /// the shipped 3. This number can never be <em>lowered</em> without reintroducing
        /// exactly the bug it exists to prevent, so it is chosen once and chosen wide.
        /// </para>
        /// </summary>
        public const int HardCeiling = 999;

        // ------------------------------------------------------------ published bounds
        /// <summary>Highest refill cap a content file may ask for.</summary>
        public const int MaxRefillCap = 20;

        /// <summary>
        /// Shortest refill a content file may ask for, in seconds. Five minutes, for
        /// <see cref="HeartLimits.MinRefillSeconds"/>'s reason — below that the pool stops
        /// being scarce and the timer becomes a busy loop writing the save file.
        /// </summary>
        public const int MinRefillSeconds = 300;

        /// <summary>Longest refill a content file may ask for. Two days.</summary>
        public const int MaxRefillSeconds = 48 * 60 * 60;

        // ------------------------------------------------------------------ defaults
        /// <summary>
        /// Three, which is what a single glade used to allow on its own.
        ///
        /// The number did not change; what changed is that it is now the player's whole
        /// account rather than their whole glade, so it is spent across a session instead of
        /// being handed back at every board.
        /// </summary>
        public const int DefaultRefillCap = 3;

        /// <summary>
        /// Equal to the cap, which is a deliberate choice rather than an oversight.
        ///
        /// Hearts keep a ceiling well above their cap so a chest opened at a full bar still
        /// pays; hints do not, so a hint granted at three is refused outright. That is only
        /// safe because nothing offers one without asking <see cref="Hints.IsAtCeiling"/>
        /// first — see <c>RewardedAds.WouldBenefit</c>, which is what stops the video being
        /// offered for a reward that would evaporate. Raising this later is a config push
        /// and needs no build; lowering it takes nothing from anybody.
        /// </summary>
        public const int DefaultCeiling = 3;

        /// <summary>Eight hours, so a spent pool is whole again in a day.</summary>
        public const int DefaultRefillSeconds = 8 * 60 * 60;
    }

    /// <summary>
    /// How many hints a player may hold and how fast they come back — content, not code.
    ///
    /// <para>
    /// The same argument <see cref="HeartRuleTable"/> makes, one resource over. A hint pool
    /// decides how much help a struggling player can buy themselves before they are stuck,
    /// which is a retention lever pointing the opposite way from the heart gate: hearts
    /// decide how many attempts somebody gets, hints decide how likely an attempt is to end
    /// in a clear. Both numbers are guesses made before anybody has a retention curve, and a
    /// number that needs a store review to change is a number that gets set once, badly,
    /// from that guess.
    /// </para>
    /// <para>
    /// Like every other optional block, this is deliberately <b>not</b> a schema bump. A
    /// client that predates it ignores it and keeps the built-in numbers; a client that has
    /// it reads a file written before it existed and does the same. Bumping
    /// <see cref="ProgressionSchema"/> for an added optional field would invalidate the
    /// whole reward table for every client that has not updated.
    /// </para>
    /// <para>
    /// <b>Every number here is safe to lower</b>, for <see cref="HeartRuleTable"/>'s reason.
    /// Lowering the cap stops the clock earlier and leaves anybody above it holding what
    /// they had; lowering the ceiling refuses new grants and confiscates nothing. Nothing
    /// published here can reach into a save file and take something out of it.
    /// </para>
    /// </summary>
    public sealed class HintRuleTable
    {
        HintRuleTable(int refillCap, int ceiling, int refillSeconds)
        {
            RefillCap = refillCap;
            Ceiling = ceiling;
            RefillSeconds = refillSeconds;
        }

        /// <summary>
        /// Where the clock stops. The timer refills to this and no further, so this — not
        /// <see cref="Ceiling"/> — is the number that sets how much help free play buys.
        /// </summary>
        public int RefillCap { get; }

        /// <summary>
        /// The most a player may hold once granted hints are stacked on top. Enforced at the
        /// moment of a grant, never by re-reading a save. Equal to
        /// <see cref="RefillCap"/> as shipped — see <see cref="HintLimits.DefaultCeiling"/>.
        /// </summary>
        public int Ceiling { get; }

        /// <summary>Seconds between refills.</summary>
        public long RefillSeconds { get; }

        /// <summary>
        /// How long one wait lasts. Flat, because nothing boosts hints: the heart boost is
        /// named for hearts, is sold and dropped as such, and quietly speeding up a second
        /// resource with it would make a published number mean two things.
        /// </summary>
        public RegenPeriod Period => RegenPeriod.Flat(RefillSeconds);

        /// <summary>The numbers that ship inside the build, and the floor under any content mistake.</summary>
        public static readonly HintRuleTable Default = new HintRuleTable(
            HintLimits.DefaultRefillCap,
            HintLimits.DefaultCeiling,
            HintLimits.DefaultRefillSeconds);

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>hints</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in numbers
        /// stand, because a content mistake must fail a build and never a session.
        /// </summary>
        public static HintRuleTable Resolve(HintsDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            int refillCap = Read(dto.refillCap, HintLimits.DefaultRefillCap, 1,
                                 HintLimits.MaxRefillCap, "hints refillCap", problems);

            int ceiling = Read(dto.ceiling, HintLimits.DefaultCeiling, 1,
                               HintLimits.HardCeiling, "hints ceiling", problems);

            int refill = Read(dto.refillSeconds, HintLimits.DefaultRefillSeconds,
                              HintLimits.MinRefillSeconds, HintLimits.MaxRefillSeconds,
                              "hints refillSeconds", problems);

            // A ceiling under the refill cap is not a smaller ceiling, it is a contradiction:
            // the clock would carry a player past the most they are allowed to hold, so every
            // grant would be refused while the timer kept paying. Raised to the cap rather
            // than rejected, because the author's intent is unambiguous and a session must
            // not lose the rest of a good block over it. A ceiling *equal* to the cap is not
            // a mistake and is not reported — that is the shipped shape.
            if (ceiling < refillCap)
            {
                problems.Add($"hints ceiling is {ceiling}, below the refill cap of {refillCap}; " +
                             "the clock would carry a player past what they may hold, so the " +
                             "ceiling is raised to the cap");
                ceiling = refillCap;
            }

            return new HintRuleTable(refillCap, ceiling, refill);
        }

        /// <summary>
        /// One authored number: unwritten inherits, out of range is clamped and named.
        /// <see cref="HeartRuleTable"/>'s reader, and clamped for its reason — refusing one
        /// scalar would mean discarding the whole block.
        /// </summary>
        static int Read(int authored, int fallback, int min, int max, string name, List<string> problems)
        {
            if (authored < 0) return fallback;                   // -1 is "not written"

            if (authored < min)
            {
                problems.Add($"{name} is {authored}, below the supported minimum {min}; clamped");
                return min;
            }

            if (authored > max)
            {
                problems.Add($"{name} is {authored}, above the supported maximum {max}; clamped");
                return max;
            }

            return authored;
        }
    }

    /// <summary>
    /// The live hint rules, read synchronously from anywhere.
    ///
    /// A facade over <see cref="ProgressionRules"/>, shaped exactly like
    /// <see cref="HeartRules"/> and for its reason: the alternative is an install step, and
    /// a step someone has to remember is a step that gets forgotten. Until the content pack
    /// loads, <see cref="HintRuleTable.Default"/> is in force, so nothing has to null-check
    /// and a failed read costs a retune rather than a session.
    /// </summary>
    public static class HintRules
    {
        public static HintRuleTable Table => ProgressionRules.Table.Hints;

        /// <summary>Where the refill timer stops. See <see cref="HintRuleTable.RefillCap"/>.</summary>
        public static int RefillCap => Table.RefillCap;

        /// <summary>The most a player may hold. See <see cref="HintRuleTable.Ceiling"/>.</summary>
        public static int Ceiling => Table.Ceiling;

        public static long RefillSeconds => Table.RefillSeconds;
    }
}
