using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// The ceilings that bound whatever a content file asks a streak to pay, and the two
    /// numbers that bound what protecting one costs.
    ///
    /// Same job as <c>AdRules</c> and <c>DailyRules.MaxChests</c>: content may retune the
    /// ladder, it may not redefine what a ladder is.
    /// </summary>
    public static class StreakRules
    {
        /// <summary>
        /// Longest ladder a content file may author.
        ///
        /// Thirty is a month, which is longer than any streak a player will hold often
        /// enough to matter, and the cap exists so a generated file cannot allocate an
        /// arbitrary array on a phone rather than because thirty-one would be wrong.
        /// </summary>
        public const int MaxRungs = 30;

        /// <summary>
        /// Most a single rung may hand over, for the kinds the client applies itself.
        ///
        /// <para>
        /// <b>No authored rung reaches this any more</b> — <see cref="IsPayableKind"/> refuses
        /// every kind the client banks, so a rung is currency or a chest and nothing else. It
        /// is kept because it is <em>wire</em>: <c>maxFor</c> in <c>functions/src/streak.ts</c>
        /// mirrors it, a rolled-back client can still write a heart rung into a published
        /// ladder, and a clamp the two halves disagree about is a number the board draws and
        /// the server refuses.
        /// </para>
        /// </summary>
        public const int MaxRungAmount = 72;

        /// <summary>
        /// Most credits, and most gems, a single rung may pay.
        ///
        /// <para>
        /// These are not the same sort of number as <see cref="MaxRungAmount"/> and it is
        /// worth being clear why. A heart clamps and a boost expires, so their ceiling only
        /// has to stop a typo. A currency rung is money, and the ceiling is also the
        /// <b>per-day cost of a forged streak</b>: the server pays at most one night per
        /// calendar day (see <c>StreakClaim</c> on the server and the type summary below),
        /// so whatever the largest rung pays is the most a save editor can extract in a day.
        /// Choosing them is therefore an economy decision, not a defensive one.
        /// </para>
        /// <para>
        /// The server enforces the same two numbers. They are part of the wire contract
        /// exactly as the chest generator's constants are — a client clamping at one figure
        /// and a server at another would show the player a reward it then refused to pay.
        /// </para>
        /// </summary>
        public const int MaxCreditsPerRung = 2000;

        public const int MaxGemsPerRung = 100;

        /// <summary>
        /// The ceiling a kind is held to. Asked in one place so the numbers above cannot be
        /// applied inconsistently by the two call sites that clamp.
        /// </summary>
        public static int MaxFor(ChestDropKind kind)
        {
            switch (kind)
            {
                case ChestDropKind.Credits: return MaxCreditsPerRung;
                case ChestDropKind.Gems: return MaxGemsPerRung;
                default: return MaxRungAmount;
            }
        }

        /// <summary>
        /// Whether a content file may author this kind on a streak rung.
        ///
        /// <para>
        /// <b>Credits and gems, and nothing else.</b> Everything a chest can hold reaches this
        /// ladder through a <see cref="ChestTier"/> now, which is invariant 45's bargain read
        /// across: naming a tier makes one published disclosure the odds for every night that
        /// pays it, and it retunes every such night at once. A rung that hands over a heart or
        /// a boost <em>directly</em> would be a second vocabulary for rewards on the one
        /// screen that already has the first.
        /// </para>
        /// <para>
        /// The two retired kinds are refused <b>by name</b> rather than ignored, because
        /// <c>JsonUtility</c> drops nothing here and a silently skipped rung renumbers every
        /// night above it (invariant 5f).
        /// </para>
        /// </summary>
        public static bool IsPayableKind(ChestDropKind kind)
            => kind == ChestDropKind.Credits || kind == ChestDropKind.Gems;

        /// <summary>The kinds a streak rung used to be able to name and may not any more.</summary>
        public static bool IsRetiredKind(ChestDropKind kind)
            => kind == ChestDropKind.Hearts || kind == ChestDropKind.HeartBoost;

        // ------------------------------------------------------------------ the shield
        /// <summary>
        /// How many days a bought shield covers by default, and the bounds content may move
        /// it between.
        ///
        /// <para>
        /// <b>Seven, and it is content rather than a constant</b> for invariant 4's reason —
        /// a holiday week may want a longer one — which means no string in this game may say
        /// "seven". Every sentence about the shield takes <see cref="StreakTable.ShieldDays"/>
        /// as an argument, exactly as the streak's own copy takes the ladder's length.
        /// </para>
        /// </summary>
        public const int DefaultShieldDays = 7;
        public const int MinShieldDays = 1;
        public const int MaxShieldDays = 30;

        /// <summary>
        /// What protecting a streak costs in gems, and the ceiling a typo is caught by.
        ///
        /// <para>
        /// A sanity bound rather than tuning: the shield is an <em>ordinary gem spend</em>
        /// (invariant 18), so nothing about it reaches the server as a permission and the
        /// price is not part of any wire contract — which is exactly why a fat-fingered
        /// 12,000 has to be refused here, where it is visible, rather than discovered by a
        /// player being quoted it.
        /// </para>
        /// </summary>
        public const int DefaultShieldGems = 120;
        public const int MaxShieldGems = 5000;
    }

    /// <summary>
    /// One day of the ladder: what the streak pays for reaching this length.
    ///
    /// <para>
    /// <b>Two shapes, and a rung is exactly one of them.</b> A currency rung hands over
    /// credits or gems, adjudicated by the server against its own copy of this ladder. A
    /// chest rung names a <see cref="ChestTier"/> and pays whatever that chest rolls, in the
    /// ceremony every other chest in this game opens in — which is what lets a night be worth
    /// a royal chest without this file learning anything about utilities, hearts or odds.
    /// </para>
    /// <para>
    /// The kind is a <see cref="ChestDropKind"/> rather than a new enum, for the reason
    /// <c>AdOffer</c> gives — there is one reward vocabulary in this game and one place that
    /// decides which of its members the server has to adjudicate.
    /// </para>
    /// </summary>
    public readonly struct StreakRung
    {
        public readonly ChestDropKind Kind;
        public readonly int Amount;

        /// <summary>The chest this night pays, or null when it pays currency.</summary>
        public readonly ChestTier Tier;

        StreakRung(ChestDropKind kind, int amount, ChestTier tier)
        {
            if (tier != null)
            {
                Kind = ChestDropKind.None;
                Amount = 0;
                Tier = tier;
                return;
            }

            int ceiling = StreakRules.MaxFor(kind);

            Kind = kind;
            Amount = amount < 0 ? 0
                   : amount > ceiling ? ceiling
                   : amount;
            Tier = null;
        }

        /// <summary>A night that hands over credits or gems.</summary>
        public static StreakRung Currency(ChestDropKind kind, int amount)
            => new StreakRung(kind, amount, null);

        /// <summary>A night that pays a chest. Null falls back to <see cref="None"/>.</summary>
        public static StreakRung Chest(ChestTier tier)
            => tier == null ? None : new StreakRung(ChestDropKind.None, 0, tier);

        /// <summary>True when this night opens a chest rather than paying a figure.</summary>
        public bool IsChest => Tier != null;

        /// <summary>True when this rung is adjudicated by the server rather than applied here.</summary>
        public bool IsCurrency => !IsChest && ChestDropKinds.IsCurrency(Kind);

        public bool IsValid => IsChest || (Kind != ChestDropKind.None && Amount > 0);

        /// <summary>
        /// What a currency night hands over. <see cref="ChestDrop.None"/> for a chest night,
        /// whose contents are a roll rather than a figure — ask <see cref="Tier"/> instead.
        /// </summary>
        public ChestDrop AsDrop() => IsChest ? ChestDrop.None : new ChestDrop(Kind, Amount);

        public static readonly StreakRung None = new StreakRung(ChestDropKind.None, 0, null);

        public override string ToString()
            => IsChest ? Tier.Id + " chest" : AsDrop().ToString();
    }

    /// <summary>
    /// What a run of consecutive days pays, immutable once built.
    ///
    /// <para>
    /// <b>The ladder is a lap, not a staircase.</b> <see cref="Rung"/> wraps: night eight
    /// pays what night one pays, night nine what night two pays, for ever. That is the only
    /// reading that stays true to what a streak is — it has no end, so a ladder that ran
    /// out would stop rewarding the player on exactly the day their streak became
    /// impressive. It also finally agrees with the board, which has drawn laps since
    /// <c>DailyStreak.CycleStart</c> existed while this table was still repeating its last
    /// rung; a tile that said "night 8" and paid night 7's reward was the ladder and the
    /// board telling the player two different things.
    /// </para>
    /// <para>
    /// <b>Currency on the ladder, and what had to be built for it.</b> Every currency award
    /// in this game reaches the player as a claim the server recomputes — invariant 10a —
    /// and for a long time this ladder refused currency on the grounds that a streak is not
    /// derivable from anything the server observes. That much is true and has not changed.
    /// What it missed is that the server does not need to know the streak — it needs to know
    /// a claim is no <em>better</em> than an honest one, which is arithmetic rather than
    /// gameplay. Two pieces establish it:
    /// </para>
    /// <list type="number">
    /// <item>A night is claimed as <c>streak:{day}:{night}:{currency}</c>. The calendar day
    /// makes it idempotent — one night per day, one grant per id, on any device after any
    /// reinstall — and the night selects the rung, which the server reads from <b>its own</b>
    /// copy of this ladder in <c>config/progression</c>. The client's figure is a
    /// prediction, exactly as a chest's is.</item>
    /// <item>The server keeps a floor no client can write: the day and the night it last
    /// paid. A night may only ever <em>climb</em>, and no faster than the calendar climbs.
    /// A save edited to say "night seven" every morning fails both. See <c>advances</c> in
    /// <c>functions/src/streak.ts</c>.</item>
    /// </list>
    /// <para>
    /// <b>A chest night rides the same id.</b> The rung decides whether the night is a figure
    /// or a roll, and the server reads the rung out of its own ladder — so a chest night is
    /// <em>recomputed</em> (like a task's) on top of being bounded (like a night's), and the
    /// claim id never had to learn anything. Its seed is
    /// <c>ChestSeed.ForSubject(player, "streak", "{day}:{night}")</c>, which is contract.
    /// </para>
    /// <para>
    /// Note what is <em>not</em> in that list: the save file. The streak block does travel
    /// with the save — it never used to, which is why a player's streak quietly restarted on
    /// their second device — but the server only logs disagreements with it. A payment rule
    /// resting on a client-written number is not a rule.
    /// </para>
    /// <para>
    /// So a forged streak buys nothing an honest one does not: one night per calendar day,
    /// never backfilled, climbing no faster than the calendar climbs. The one thing left
    /// uncapped is which rung a brand-new account's first-ever claim names, which is why
    /// <see cref="StreakRules.MaxGemsPerRung"/> is an economy decision rather than a
    /// defensive one.
    /// </para>
    /// </summary>
    public sealed class StreakTable
    {
        readonly StreakRung[] _rungs;

        StreakTable(StreakRung[] rungs, int shieldDays, int shieldGems)
        {
            _rungs = rungs;
            ShieldDays = Clamp(shieldDays, StreakRules.MinShieldDays, StreakRules.MaxShieldDays);
            ShieldGems = shieldGems < 1 ? 0
                       : shieldGems > StreakRules.MaxShieldGems ? StreakRules.MaxShieldGems
                       : shieldGems;
        }

        static int Clamp(int value, int lo, int hi) => value < lo ? lo : value > hi ? hi : value;

        /// <summary>How many days the ladder is authored for. Always at least one.</summary>
        public int Length => _rungs.Length;

        /// <summary>
        /// How many calendar days a bought shield covers, counting the day it was bought.
        ///
        /// Read from here by every sentence that mentions it, so the copy cannot come to
        /// disagree with the rule — see <see cref="StreakRules.DefaultShieldDays"/>.
        /// </summary>
        public int ShieldDays { get; }

        /// <summary>What a shield costs in gems. Zero means this build sells none.</summary>
        public int ShieldGems { get; }

        /// <summary>True when a shield may be offered at all.</summary>
        public bool SellsShield => ShieldGems > 0;

        /// <summary>
        /// What the <paramref name="night"/>th night of a streak pays, counting from 1.
        ///
        /// <para>
        /// Past the end of the ladder it <b>wraps</b>: night eight pays night one's rung.
        /// A player on night forty is the most engaged player the game has, and dropping
        /// them to zero is the one outcome the whole feature is built to avoid — the point
        /// of a streak is that it is worth protecting, and a streak that stops paying stops
        /// being worth protecting on exactly the day it became impressive.
        /// </para>
        /// <para>
        /// Wrapping rather than repeating the last rung, because a repeated tail makes the
        /// most valuable rung the one a player receives for ever, which is the shape that
        /// forces a designer to keep the milestone small. A lap lets night seven be the
        /// week's peak and still be reachable again next week.
        /// </para>
        /// </summary>
        public StreakRung Rung(int night)
            => night < 1 ? StreakRung.None : _rungs[(night - 1) % _rungs.Length];

        /// <summary>
        /// Where a night falls on its lap, counting from 1. Night eight of a seven-night
        /// ladder is night 1 of lap two.
        ///
        /// The number a caption uses when it wants to name the rung rather than the night —
        /// and the number the server derives the same way, which is why it lives on the
        /// table rather than being spelled out at each call site.
        /// </summary>
        public int NightInCycle(int night)
            => night < 1 ? 0 : (night - 1) % _rungs.Length + 1;

        /// <summary>Every rung, in order, for the panel that prints the ladder.</summary>
        public IReadOnlyList<StreakRung> Rungs => _rungs;

        /// <summary>
        /// The ladder that ships inside the build.
        ///
        /// <para>
        /// <b>The shape is the design, and it is coins, gems and chests.</b> Two climbs and a
        /// crest: a figure, then a bigger figure, then the chest that pays for the pair. Night
        /// one opens the lap with credits, and the reason it pays at all is the lap — night
        /// one is no longer only "the day you started", it is also the day after every seventh
        /// night, so a player meets it once a week for as long as they keep the flame. Paying
        /// it is what makes the first night of a restarted streak worth having rather than a
        /// penalty box.
        /// </para>
        /// <para>
        /// <b>No wood chest, and no chest below silver.</b> The humblest tier is what a daily
        /// task pays for two runs; a streak asks for seven consecutive days, which is the
        /// hardest thing this game asks of anybody, so the cheapest thing it may hand over is
        /// the tier above. Night seven is the milestone — it is where a streak stops being a
        /// run of days and starts being a thing the player has, it is the number they describe
        /// to themselves, and it is the last night of the lap — so it is the only royal chest
        /// in the game that is not behind a weekly task.
        /// </para>
        /// <para>
        /// The built-in ladder resolves its tiers against <see cref="TaskTable.Default"/>,
        /// because a fallback that could not name a chest would be a different ladder from the
        /// one the content authors — and the fallback's whole job is to be the same game.
        /// </para>
        /// </summary>
        public static readonly StreakTable Default = BuildDefault();

        static StreakTable BuildDefault()
        {
            var tiers = TaskTable.Default;

            return new StreakTable(new[]
            {
                StreakRung.Currency(ChestDropKind.Credits, 400),   // 1 — the lap opens
                StreakRung.Currency(ChestDropKind.Gems, 8),        // 2
                StreakRung.Chest(tiers.Tier("silver")),            // 3
                StreakRung.Currency(ChestDropKind.Credits, 800),   // 4
                StreakRung.Currency(ChestDropKind.Gems, 16),       // 5
                StreakRung.Chest(tiers.Tier("gold")),              // 6
                StreakRung.Chest(tiers.Tier("royal")),             // 7 — a week, then it laps
            }, StreakRules.DefaultShieldDays, StreakRules.DefaultShieldGems);
        }

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>streak</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in ladder
        /// stands, because a content mistake must fail a build and never a session.
        ///
        /// <para>
        /// <paramref name="tier"/> resolves a chest rung's tier id against the same table the
        /// tasks page draws from, which is what makes "the streak pays a royal chest" one
        /// authored chest rather than a second one nobody would remember to retune. It is a
        /// lookup rather than the table itself so the vectors can exercise this reader against
        /// their own synthetic tiers.
        /// </para>
        /// </summary>
        public static StreakTable Resolve(StreakDto dto, Func<string, ChestTier> tier,
                                          List<string> problems)
        {
            problems = problems ?? new List<string>();
            if (dto == null) return Default;                    // absent is not an error

            if (dto.rungs == null || dto.rungs.Length == 0)
            {
                problems.Add("streak block lists no rungs; using the built-in ladder");
                return Default;
            }

            if (dto.rungs.Length > StreakRules.MaxRungs)
            {
                problems.Add($"streak lists {dto.rungs.Length} rungs, above the supported " +
                             $"{StreakRules.MaxRungs}; using the built-in ladder");
                return Default;
            }

            var rungs = new StreakRung[dto.rungs.Length];

            for (int i = 0; i < dto.rungs.Length; i++)
            {
                if (!TryReadRung(dto.rungs[i], i + 1, tier, problems, out rungs[i])) return Default;
            }

            int days = dto.shieldDays > 0 ? dto.shieldDays : StreakRules.DefaultShieldDays;

            if (days < StreakRules.MinShieldDays || days > StreakRules.MaxShieldDays)
            {
                problems.Add($"streak shieldDays is {days}, outside " +
                             $"{StreakRules.MinShieldDays}..{StreakRules.MaxShieldDays}; clamped");
            }

            // Absent is the built-in price rather than "sells none", because a shield that
            // silently stopped being offered is a feature that disappears from a screen with
            // every gate green. A file that means to withdraw it says so with a zero.
            int gems = dto.shieldGems == 0 ? StreakRules.DefaultShieldGems : dto.shieldGems;

            if (gems < 0 || gems > StreakRules.MaxShieldGems)
            {
                problems.Add($"streak shieldGems is {dto.shieldGems}, outside " +
                             $"0..{StreakRules.MaxShieldGems}; clamped. Zero withdraws the offer");
            }

            return new StreakTable(rungs, days, gems < 0 ? 0 : gems);
        }

        /// <summary>
        /// One rung, or false when it breaks a rule.
        ///
        /// Stricter than the ads table, which skips a bad entry and carries on. A ladder is
        /// ordered — rung four is only rung four because three rungs precede it — so
        /// dropping one silently renumbers every day above it and quietly changes what the
        /// player is owed. Refusing the whole block is the only safe failure.
        /// </summary>
        static bool TryReadRung(StreakRungDto dto, int day, Func<string, ChestTier> tier,
                                List<string> problems, out StreakRung rung)
        {
            rung = StreakRung.None;

            // An empty rung is legitimate — it is how a night that only marks time is
            // authored — so a null entry is read as one rather than rejected.
            if (dto == null) return true;

            bool hasTier = !string.IsNullOrEmpty(dto.tier);
            bool hasKind = !string.IsNullOrEmpty(dto.kind);

            if (hasTier && hasKind)
            {
                problems.Add($"streak night {day} names both a chest tier '{dto.tier}' and a " +
                             $"reward kind '{dto.kind}'; a night pays one or the other");
                return false;
            }

            if (hasTier)
            {
                var chest = tier != null ? tier(dto.tier) : null;
                if (chest == null)
                {
                    problems.Add($"streak night {day} pays chest tier '{dto.tier}', which the tasks " +
                                 "block does not define; a night paying a chest nobody can price is " +
                                 "a claim the server can never confirm");
                    return false;
                }

                rung = StreakRung.Chest(chest);
                return true;
            }

            if (!hasKind) return true;

            var kind = ChestDropKinds.Parse(dto.kind);
            if (kind == ChestDropKind.None)
            {
                problems.Add($"streak day {day} names unknown reward kind '{dto.kind}'");
                return false;
            }

            // Refused by name rather than ignored (invariant 5f). Hearts and boosts used to
            // be authorable here and reach this ladder through a chest tier now, so a file
            // still naming one is a file written against the old vocabulary — and skipping it
            // would renumber every night above it.
            if (StreakRules.IsRetiredKind(kind))
            {
                problems.Add($"streak day {day} pays '{dto.kind}', which a streak rung may no longer " +
                             "name: the streak pays credits, gems and chests. Name a chest tier " +
                             "instead, so one published disclosure covers every night that pays it");
                return false;
            }

            if (!StreakRules.IsPayableKind(kind))
            {
                problems.Add($"streak day {day} pays '{dto.kind}', which a streak rung may not name; " +
                             "a rung pays credits, gems or a chest tier");
                return false;
            }

            if (dto.amount < 1)
            {
                problems.Add($"streak day {day} pays {dto.amount}; leave the rung empty for a " +
                             "day that pays nothing rather than authoring a zero");
                return false;
            }

            // The ceiling is the whole of what keeps currency safe to author: it is the most a
            // single night can ever hand over, on the server as well as here, and therefore the
            // most a forged streak yields in a day. A rung above it is clamped rather than
            // refused, because the ladder is ordered — refusing renumbers every night above it
            // — but it is said out loud, because a silently clamped reward is one the panel
            // prints and nobody pays.
            int ceiling = StreakRules.MaxFor(kind);

            if (dto.amount > ceiling)
            {
                problems.Add($"streak day {day} pays {dto.amount} {dto.kind}, above the supported " +
                             $"{ceiling}; clamped. The server clamps to the same figure, so raising " +
                             "it means raising StreakRules and firebase/functions/src/streak.ts together");
            }

            rung = StreakRung.Currency(kind, dto.amount);
            return true;
        }
    }
}
