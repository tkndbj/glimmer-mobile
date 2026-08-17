using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The bounds that survive any content file, and the numbers used when there is none.
    ///
    /// <para>
    /// Same job <c>AdRules</c> does for the ad table: content is allowed to retune the
    /// gate, it is not allowed to redefine what the gate is. Everything here is a
    /// compile-time constant precisely because it is what a published file is checked
    /// <em>against</em> — a limit that could itself be published would not be a limit.
    /// </para>
    /// </summary>
    public static class HeartLimits
    {
        // ------------------------------------------------------- the structural bound
        /// <summary>
        /// The most hearts a <see cref="Hearts"/> ledger can represent, ever.
        ///
        /// <para>
        /// <b>This is not the ceiling a player experiences</b> — that is
        /// <see cref="HeartRuleTable.Ceiling"/>, and it is content. This one is the bound
        /// the ledger's own clamp uses, and the distinction is the single most important
        /// thing in this file.
        /// </para>
        /// <para>
        /// The merge proof needs <c>produced ≤ spent + something finite</c> to hold on
        /// every device at every moment. If that "something" were the tunable ceiling, then
        /// lowering it from a config push would clamp <c>produced</c> <em>downward</em> on
        /// the next read — and <c>produced</c> is a counter that only ever rises. Breaking
        /// that breaks everything built on it: the value would fall on one device and not
        /// on another that had not fetched the new table yet, the join would restore it,
        /// the clamp would cut it again, and two devices would never agree. It would also
        /// mean a tuning change silently confiscated hearts people had collected, which is
        /// the exact class of bug invariant 11b exists to stop.
        /// </para>
        /// <para>
        /// So the structural bound is fixed in code and generous, and the ceiling players
        /// meet is enforced where it belongs — at the moment of a <see cref="Hearts.Grant"/>,
        /// which is a decision rather than a re-reading. Lowering the published ceiling
        /// therefore stops new grants and never takes a heart away from anybody.
        /// </para>
        /// </summary>
        public const int HardCeiling = 999;

        // ------------------------------------------------------------ published bounds
        /// <summary>Highest refill cap a content file may ask for.</summary>
        public const int MaxRefillCap = 50;

        /// <summary>
        /// Shortest refill a content file may ask for, in seconds.
        ///
        /// Five minutes. Below that the gate stops being a gate and the timer becomes a
        /// busy loop writing the save file; a typo that dropped a zero would otherwise turn
        /// eight hours into forty-eight minutes and nobody would notice until the retention
        /// numbers moved.
        /// </summary>
        public const int MinRefillSeconds = 300;

        /// <summary>Longest refill a content file may ask for. Two days.</summary>
        public const int MaxRefillSeconds = 48 * 60 * 60;

        /// <summary>Longest boost window a content file may allow a chest to grant. A week.</summary>
        public const int MaxBoostHoursLimit = 168;

        /// <summary>Most hearts a single lost run may cost.</summary>
        public const int MaxDefeatCost = 5;

        // ------------------------------------------------------------------ defaults
        public const int DefaultRefillCap = 5;
        public const int DefaultCeiling = 50;
        public const int DefaultRefillSeconds = 8 * 60 * 60;
        public const int DefaultBoostedRefillSeconds = 4 * 60 * 60;
        public const int DefaultMaxBoostHours = 72;
        public const int DefaultDefeatCost = 1;
    }

    /// <summary>
    /// How many hearts a player may hold and how fast they come back — content, not code.
    ///
    /// <para>
    /// This is the gate that sits between a player and the game, so it is the single
    /// number in the economy most likely to be wrong on launch day and most expensive to
    /// leave wrong. Eight hours is a guess made before anybody has a retention curve; the
    /// right value is discovered from live data in the first fortnight, and a value that
    /// needs a store review to change is a value that gets set once, badly, from that
    /// guess. The same argument the ad payouts and the chest odds already won.
    /// </para>
    /// <para>
    /// Like every other optional block, this is deliberately <b>not</b> a schema bump. A
    /// client that predates it ignores it and keeps the built-in numbers; a client that has
    /// it reads a file written before it existed and does the same. Bumping
    /// <see cref="ProgressionSchema"/> for an added optional field would invalidate the
    /// whole reward table for every client that has not updated.
    /// </para>
    /// <para>
    /// <b>Every number here is safe to lower.</b> That is a property worth stating, because
    /// it is what makes the block safe to push at all. Lowering the refill cap stops the
    /// clock earlier and leaves anybody above it holding what they had; lowering the
    /// ceiling stops new grants and confiscates nothing — see
    /// <see cref="HeartLimits.HardCeiling"/>. Nothing published here can reach into a save
    /// file and take something out of it.
    /// </para>
    /// </summary>
    public sealed class HeartRuleTable
    {
        HeartRuleTable(int refillCap, int ceiling, int refillSeconds, int boostedRefillSeconds,
                       int maxBoostHours, int defeatCost)
        {
            RefillCap = refillCap;
            Ceiling = ceiling;
            RefillSeconds = refillSeconds;
            BoostedRefillSeconds = boostedRefillSeconds;
            MaxBoostHours = maxBoostHours;
            DefeatCost = defeatCost;
        }

        /// <summary>
        /// Where the clock stops. The timer refills to this and no further, so this — not
        /// <see cref="Ceiling"/> — is the number that sets the pace of free play.
        /// </summary>
        public int RefillCap { get; }

        /// <summary>
        /// The most a player may hold once collected hearts are stacked on top. Enforced at
        /// the moment of a grant, never by re-reading a save.
        /// </summary>
        public int Ceiling { get; }

        /// <summary>Seconds between refills.</summary>
        public long RefillSeconds { get; }

        /// <summary>Seconds between refills while a heart boost is running.</summary>
        public long BoostedRefillSeconds { get; }

        /// <summary>Longest boost a chest may award, so a bad drop table cannot grant a year.</summary>
        public long MaxBoostHours { get; }

        /// <summary>What one lost run costs.</summary>
        public int DefeatCost { get; }

        /// <summary>
        /// How long the wait starting at <paramref name="at"/> lasts.
        ///
        /// Asked per refill rather than once per catch-up, because a boost can expire in
        /// the middle of a walk: a player who closes the app with two hours of boost left
        /// and opens it a day later has earned some hearts at the fast rate and the rest at
        /// the slow one, and rounding that either way is either a theft or a gift.
        /// </summary>
        public long PeriodAt(long at, long boostUntilUnix)
            => boostUntilUnix > at ? BoostedRefillSeconds : RefillSeconds;

        /// <summary>
        /// The numbers that ship inside the build, and the floor under any content mistake.
        ///
        /// Five hearts at eight hours apart is a full set in a day and a half — long enough
        /// that the gate is real rather than decorative, which is what makes it worth
        /// building a server clock for. At twenty-five minutes nobody bothers cheating; at
        /// eight hours they will.
        /// </summary>
        public static readonly HeartRuleTable Default = new HeartRuleTable(
            HeartLimits.DefaultRefillCap,
            HeartLimits.DefaultCeiling,
            HeartLimits.DefaultRefillSeconds,
            HeartLimits.DefaultBoostedRefillSeconds,
            HeartLimits.DefaultMaxBoostHours,
            HeartLimits.DefaultDefeatCost);

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>hearts</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in numbers
        /// stand, because a content mistake must fail a build and never a session.
        /// </summary>
        public static HeartRuleTable Resolve(HeartsDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            int refillCap = Read(dto.refillCap, HeartLimits.DefaultRefillCap, 1,
                                 HeartLimits.MaxRefillCap, "hearts refillCap", problems);

            int ceiling = Read(dto.ceiling, HeartLimits.DefaultCeiling, 1,
                               HeartLimits.HardCeiling, "hearts ceiling", problems);

            int refill = Read(dto.refillSeconds, HeartLimits.DefaultRefillSeconds,
                              HeartLimits.MinRefillSeconds, HeartLimits.MaxRefillSeconds,
                              "hearts refillSeconds", problems);

            int boosted = Read(dto.boostedRefillSeconds, HeartLimits.DefaultBoostedRefillSeconds,
                               HeartLimits.MinRefillSeconds, HeartLimits.MaxRefillSeconds,
                               "hearts boostedRefillSeconds", problems);

            int boostHours = Read(dto.maxBoostHours, HeartLimits.DefaultMaxBoostHours, 1,
                                  HeartLimits.MaxBoostHoursLimit, "hearts maxBoostHours", problems);

            int defeatCost = Read(dto.defeatCost, HeartLimits.DefaultDefeatCost, 1,
                                  HeartLimits.MaxDefeatCost, "hearts defeatCost", problems);

            // A ceiling under the refill cap is not a smaller ceiling, it is a contradiction:
            // the clock would carry a player past the most they are allowed to hold, so every
            // grant would be refused while the timer kept paying. Raised to the cap rather
            // than rejected, because the author's intent is unambiguous and a session must
            // not lose the rest of a good block over it.
            if (ceiling < refillCap)
            {
                problems.Add($"hearts ceiling is {ceiling}, below the refill cap of {refillCap}; " +
                             "the clock would carry a player past what they may hold, so the " +
                             "ceiling is raised to the cap");
                ceiling = refillCap;
            }

            // A "boost" slower than the ordinary rate is the feature working backwards: the
            // player is told hearts return faster and they return more slowly. It reads as
            // the reward being broken, and nothing else in the build would catch it.
            if (boosted > refill)
            {
                problems.Add($"hearts boostedRefillSeconds is {boosted}, longer than the ordinary " +
                             $"{refill}; a boost that slows hearts down is the feature inverted, " +
                             "so the boosted rate is held at the ordinary one");
                boosted = refill;
            }

            return new HeartRuleTable(refillCap, ceiling, refill, boosted, boostHours, defeatCost);
        }

        /// <summary>
        /// One authored number: unwritten inherits, out of range is clamped and named.
        ///
        /// Clamped rather than rejected because these are scalars with no sensible partial
        /// state — refusing one would mean discarding the whole block, and a gate running on
        /// five of six published numbers is closer to what the author meant than one running
        /// on none of them.
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
    /// The live heart rules, read synchronously from anywhere.
    ///
    /// <para>
    /// A facade over <see cref="ProgressionRules"/> rather than a table anybody has to be
    /// handed, and shaped exactly like <c>RewardedAds.Table</c> for the same reason: the
    /// alternative is an install step, and a step someone has to remember is a step that
    /// gets forgotten. Until the content pack loads, <see cref="HeartRuleTable.Default"/>
    /// is in force, so nothing has to null-check and a failed read costs a retune rather
    /// than a session.
    /// </para>
    /// <para>
    /// The names are unchanged from when these were constants, which is deliberate — every
    /// call site reads identically and the diff that made the gate tunable is confined to
    /// this file. Note that they are properties now, so a hot loop should take a local
    /// copy rather than re-reading through three dereferences per iteration.
    /// </para>
    /// </summary>
    public static class HeartRules
    {
        public static HeartRuleTable Table => ProgressionRules.Table.Hearts;

        /// <summary>Where the refill timer stops. See <see cref="HeartRuleTable.RefillCap"/>.</summary>
        public static int RefillCap => Table.RefillCap;

        /// <summary>The most a player may hold. See <see cref="HeartRuleTable.Ceiling"/>.</summary>
        public static int Ceiling => Table.Ceiling;

        public static long RefillSeconds => Table.RefillSeconds;

        public static long BoostedRefillSeconds => Table.BoostedRefillSeconds;

        public static long MaxBoostHours => Table.MaxBoostHours;

        public static int DefeatCost => Table.DefeatCost;

        /// <summary>How long the wait starting at <paramref name="at"/> lasts.</summary>
        public static long PeriodAt(long at, long boostUntilUnix)
            => Table.PeriodAt(at, boostUntilUnix);
    }
}
