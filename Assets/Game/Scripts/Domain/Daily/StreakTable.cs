using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// The ceilings that bound whatever a content file asks a streak to pay.
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
        /// Read as hearts on a heart rung and as hours on a boost rung, so it has to sit
        /// above both a full set and a long boost. Seventy-two matches
        /// <see cref="Persistence.HeartRules.MaxBoostHours"/>, which is the larger of the two
        /// and the only one where a typo could do real damage — a boost is the one streak
        /// reward with a duration rather than a clamp.
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
        /// The ceiling a kind is held to. Asked in one place so the four numbers above
        /// cannot be applied inconsistently by the two call sites that clamp.
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
    }

    /// <summary>
    /// One day of the ladder: what the streak pays for reaching this length.
    ///
    /// The kind is a <see cref="ChestDropKind"/> rather than a new enum, for the reason
    /// <c>AdOffer</c> gives — there is one reward vocabulary in this game and one place
    /// that decides which of its members the server has to adjudicate.
    /// </summary>
    public readonly struct StreakRung
    {
        public readonly ChestDropKind Kind;
        public readonly int Amount;

        public StreakRung(ChestDropKind kind, int amount)
        {
            int ceiling = StreakRules.MaxFor(kind);

            Kind = kind;
            Amount = amount < 0 ? 0
                   : amount > ceiling ? ceiling
                   : amount;
        }

        /// <summary>True when this rung is adjudicated by the server rather than applied here.</summary>
        public bool IsCurrency => ChestDropKinds.IsCurrency(Kind);

        public bool IsValid => Kind != ChestDropKind.None && Amount > 0;

        public ChestDrop AsDrop() => new ChestDrop(Kind, Amount);

        public static readonly StreakRung None = new StreakRung(ChestDropKind.None, 0);

        public override string ToString() => AsDrop().ToString();
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
    /// paid. A claim must either advance the night by exactly the days elapsed since — what
    /// an unbroken streak does — or claim no more nights than those days allow, which is
    /// what a restarted one does. A save edited to say "night seven" every morning fails
    /// both. See <c>advances</c> in <c>functions/src/streak.ts</c>.</item>
    /// </list>
    /// <para>
    /// Note what is <em>not</em> in that list: the save file. The streak block does now
    /// travel with the save — it never used to, which is why a player's streak quietly
    /// restarted on their second device — but the server only logs disagreements with it.
    /// A payment rule resting on a client-written number is not a rule.
    /// </para>
    /// <para>
    /// So a forged streak buys nothing an honest one does not: one night per calendar day,
    /// never backfilled, climbing no faster than a calendar climbs. The one thing left
    /// uncapped is which rung a brand-new account's first-ever claim names, which is why
    /// <see cref="StreakRules.MaxGemsPerRung"/> is an economy decision rather than a
    /// defensive one.
    /// </para>
    /// </summary>
    public sealed class StreakTable
    {
        readonly StreakRung[] _rungs;

        StreakTable(StreakRung[] rungs) => _rungs = rungs;

        /// <summary>How many days the ladder is authored for. Always at least one.</summary>
        public int Length => _rungs.Length;

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
        /// The shape is the design. Night one opens the lap with credits, and the reason it
        /// pays at all is the lap: night one is no longer only "the day you started", it is
        /// also the day after every seventh night, so a player meets it once a week for as
        /// long as they keep the flame. Paying it is what makes the first night of a
        /// restarted streak worth having rather than a penalty box.
        /// </para>
        /// <para>
        /// Hearts and boosts fill nights two, four, five and six, because those are the
        /// rewards that make <em>tomorrow's</em> session happen — which is what a streak is
        /// for — and because they cost nothing to hand over while offline.
        /// </para>
        /// <para>
        /// Nights three and seven are the two gem rungs, and seven is the milestone: it is
        /// where a streak stops being a run of days and starts being a thing the player has,
        /// it is the number they describe to themselves — "I'm on a week" — and it is the
        /// last night of the lap, so it is what the crest on the board marks.
        /// </para>
        /// </summary>
        public static readonly StreakTable Default = new StreakTable(new[]
        {
            new StreakRung(ChestDropKind.Credits, 150),             // night 1 — the lap opens
            new StreakRung(ChestDropKind.Hearts, 1),                // 2
            new StreakRung(ChestDropKind.Gems, 5),                  // 3
            new StreakRung(ChestDropKind.Hearts, 2),                // 4
            new StreakRung(ChestDropKind.HeartBoost, 12),           // 5
            new StreakRung(ChestDropKind.Hearts, 3),                // 6
            new StreakRung(ChestDropKind.Gems, 10),                 // 7 — a week, then it laps
        });

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>streak</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in ladder
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static StreakTable Resolve(StreakDto dto, List<string> problems)
        {
            problems ??= new List<string>();
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
                if (!TryReadRung(dto.rungs[i], i + 1, problems, out rungs[i])) return Default;
            }

            return new StreakTable(rungs);
        }

        /// <summary>
        /// One rung, or false when it breaks a rule.
        ///
        /// Stricter than the ads table, which skips a bad entry and carries on. A ladder is
        /// ordered — rung four is only rung four because three rungs precede it — so
        /// dropping one silently renumbers every day above it and quietly changes what the
        /// player is owed. Refusing the whole block is the only safe failure.
        /// </summary>
        static bool TryReadRung(StreakRungDto dto, int day, List<string> problems, out StreakRung rung)
        {
            rung = StreakRung.None;

            // An empty rung is legitimate — it is how day one pays nothing — so a null
            // entry is read as one rather than rejected.
            if (dto == null || string.IsNullOrEmpty(dto.kind)) return true;

            var kind = ChestDropKinds.Parse(dto.kind);
            if (kind == ChestDropKind.None)
            {
                problems.Add($"streak day {day} names unknown reward kind '{dto.kind}'");
                return false;
            }

            if (dto.amount < 1)
            {
                problems.Add($"streak day {day} pays {dto.amount}; leave the kind empty for a " +
                             "day that pays nothing rather than authoring a zero");
                return false;
            }

            // Currency is allowed here now, and the ceiling is the whole of what keeps it
            // safe to author: it is the most a single night can ever hand over, on the
            // server as well as here, and therefore the most a forged streak yields in a
            // day. A rung above it is clamped rather than refused, because the ladder is
            // ordered — refusing renumbers every night above it — but it is said out loud,
            // because a silently clamped reward is one the panel prints and nobody pays.
            int ceiling = StreakRules.MaxFor(kind);

            if (dto.amount > ceiling)
            {
                problems.Add($"streak day {day} pays {dto.amount} {dto.kind}, above the supported " +
                             $"{ceiling}; clamped. The server clamps to the same figure, so raising " +
                             "it means raising StreakRules and firebase/functions/src/streak.ts together");
            }

            rung = new StreakRung(kind, dto.amount);
            return true;
        }
    }
}
