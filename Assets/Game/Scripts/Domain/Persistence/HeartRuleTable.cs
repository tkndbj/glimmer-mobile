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

        /// <summary>
        /// Dearest a way back onto a lost board may be published at, in gems.
        ///
        /// A sanity bound rather than a design one, and deliberately far above anything
        /// sensible for <c>ContinueLimits.MaxGems</c>' reason: what it guards is a misplaced
        /// zero in a content push, which would otherwise put a price on the defeat panel that
        /// no player could ever meet and turn every empty heart bar into a dead end.
        /// </summary>
        public const long MaxRescueGems = 5_000L;

        /// <summary>Most hearts one purchase may hand over. Above any sensible tuning.</summary>
        public const int MaxRescueHearts = 50;

        /// <summary>
        /// The longest free opening a content file may ask for, in glades.
        ///
        /// <para>
        /// Twenty rather than something tighter because the window is bounded again by the
        /// content itself — <c>HeartStake</c> counts it inside the first chapter of a mode and
        /// stops at the chapter's end, so a published twenty on a ten-glade chapter is ten. The
        /// limit is here to catch the typo that drops a zero, not to express a design view: a
        /// whole free first chapter is a decision somebody may legitimately want to make from a
        /// config push after a bad first-session retention week.
        /// </para>
        /// </summary>
        public const int MaxGraceLevels = 20;

        // ------------------------------------------------------------------ defaults
        public const int DefaultRefillCap = 5;
        public const int DefaultCeiling = 50;
        public const int DefaultRefillSeconds = 8 * 60 * 60;
        public const int DefaultBoostedRefillSeconds = 4 * 60 * 60;
        public const int DefaultMaxBoostHours = 72;
        public const int DefaultDefeatCost = 1;

        /// <summary>
        /// What a way back onto a lost board costs, in gems.
        ///
        /// <para>
        /// The same twenty a continue costs, and that is a decision rather than a coincidence:
        /// the two offers can be met on one screen a minute apart, and a player who declined
        /// one price and is then shown a different one for the other reads the pair as
        /// haggling. It is the number most likely to be wrong on the first guess, which is
        /// exactly why it is content.
        /// </para>
        /// </summary>
        public const long DefaultRescueGems = 20L;

        /// <summary>
        /// Hearts that purchase hands over.
        ///
        /// <para>
        /// Two rather than one, because one is a purchase that has to be made again the moment
        /// it fails — and the board it buys is a board the player has just lost, so a second
        /// loss is the likely outcome rather than a rare one. Two is one attempt and one
        /// recovery; a full bar of five is <c>hearts_five</c> in the shop and belongs there,
        /// where somebody is choosing rather than reacting.
        /// </para>
        /// <para>
        /// Nought is legal and withdraws the offer entirely. That is the lever that turns the
        /// whole feature off from a config push — a store review objection, a market where
        /// paying past a play gate is regulated, a price that turned out to read as a trap.
        /// </para>
        /// </summary>
        public const int DefaultRescueHearts = 2;

        /// <summary>
        /// The first three glades of a mode are free to fail.
        ///
        /// Three because that is the shortest run of boards that can teach a verb, let the
        /// player use it badly, and let them use it again — one is a demonstration and two is a
        /// coincidence. The cost of getting this wrong is asymmetric in a way worth stating:
        /// too generous and a player spends three glades' worth of nothing, too mean and the
        /// heart gate meets somebody who has not yet decided they like the game.
        /// </summary>
        public const int DefaultGraceLevels = 3;
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
                       int maxBoostHours, int defeatCost, int graceLevels,
                       long rescueGems, int rescueHearts)
        {
            RefillCap = refillCap;
            Ceiling = ceiling;
            RefillSeconds = refillSeconds;
            BoostedRefillSeconds = boostedRefillSeconds;
            MaxBoostHours = maxBoostHours;
            DefeatCost = defeatCost;
            GraceLevels = graceLevels;
            RescueGems = rescueGems;
            RescueHearts = rescueHearts;
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
        /// How many glades at the head of a mode are free to fail. Nought turns the window off.
        ///
        /// The number alone; <see cref="Progression.HeartStake"/> owns what it is counted over,
        /// because that needs the catalog and this table must stay readable without one.
        /// </summary>
        public int GraceLevels { get; }

        /// <summary>What buying a way back onto a lost board costs, in gems. See <c>HeartRescue</c>.</summary>
        public long RescueGems { get; }

        /// <summary>
        /// Hearts that purchase hands over. Nought withdraws the offer.
        ///
        /// The number alone; <see cref="Progression.HeartRescue"/> owns when it may be shown,
        /// because that needs a balance and a store and this table must stay readable without
        /// either — the same split <see cref="GraceLevels"/> makes with <c>HeartStake</c>.
        /// </summary>
        public int RescueHearts { get; }

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
            HeartLimits.DefaultDefeatCost,
            HeartLimits.DefaultGraceLevels,
            HeartLimits.DefaultRescueGems,
            HeartLimits.DefaultRescueHearts);

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

            // Nought is a legal minimum here and nowhere else in this block: it means "no free
            // opening", which is a decision rather than a mistake, so it must not be clamped
            // up to one or reported as a problem.
            int graceLevels = Read(dto.graceLevels, HeartLimits.DefaultGraceLevels, 0,
                                   HeartLimits.MaxGraceLevels, "hearts graceLevels", problems);

            // Nought is legal here too, and it is the switch that withdraws the whole offer.
            // See HeartLimits.DefaultRescueHearts.
            int rescueHearts = Read(dto.rescueHearts, HeartLimits.DefaultRescueHearts, 0,
                                    HeartLimits.MaxRescueHearts, "hearts rescueHearts", problems);

            // The price refuses nought rather than obeying it. A rescue that costs nothing is
            // not a cheap rescue, it is a heart gate that has stopped gating — invariant 5d's
            // complaint about a rule that rejects nothing, applied to the one thing in this
            // game that can stop somebody playing. The field above says "no offer" properly,
            // so there is no reading of a zero here that is a design decision.
            long rescueGems = ReadPrice(dto.rescueGems, problems);

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

            // A purchase that could never be granted is a button that takes gems and hands
            // back nothing anybody can see, so it is held at what the ceiling can accept.
            // Reachable from an honest push: lowering the ceiling is documented as safe, and
            // this is the one number that has to move down with it.
            if (rescueHearts > ceiling)
            {
                problems.Add($"hearts rescueHearts is {rescueHearts}, above the ceiling of " +
                             $"{ceiling}; the purchase could never be granted, so it is held " +
                             "at the ceiling");
                rescueHearts = ceiling;
            }

            return new HeartRuleTable(refillCap, ceiling, refill, boosted, boostHours, defeatCost,
                                      graceLevels, rescueGems, rescueHearts);
        }

        /// <summary>
        /// The rescue price: unwritten inherits, out of range is clamped and named, and nought
        /// is refused rather than obeyed. See the call site for why zero is not a tuning.
        /// </summary>
        static long ReadPrice(long authored, List<string> problems)
        {
            if (authored < 0L) return HeartLimits.DefaultRescueGems;   // -1 is "not written"

            if (authored == 0L)
            {
                problems.Add("hearts rescueGems is 0, which would hand a heart to anybody who " +
                             "lost a run and stop the gate gating; set rescueHearts to 0 to " +
                             "withdraw the offer instead");
                return HeartLimits.DefaultRescueGems;
            }

            if (authored > HeartLimits.MaxRescueGems)
            {
                problems.Add($"hearts rescueGems is {authored}, above the " +
                             $"{HeartLimits.MaxRescueGems} a rescue may be priced at; clamped");
                return HeartLimits.MaxRescueGems;
            }

            return authored;
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

        /// <summary>What a way back onto a lost board costs. See <c>HeartRescue</c>.</summary>
        public static long RescueGems => Table.RescueGems;

        /// <summary>Hearts that purchase hands over. Nought withdraws the offer.</summary>
        public static int RescueHearts => Table.RescueHearts;

        /// <summary>Glades at the head of a mode that cost no heart. See <c>HeartStake</c>.</summary>
        public static int GraceLevels => Table.GraceLevels;

        /// <summary>How long the wait starting at <paramref name="at"/> lasts.</summary>
        public static long PeriodAt(long at, long boostUntilUnix)
            => Table.PeriodAt(at, boostUntilUnix);
    }
}
