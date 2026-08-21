using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The player's hearts: a <see cref="RegenLedger"/> plus the two things that are true
    /// of hearts and of nothing else.
    ///
    /// <para>
    /// <b>The arithmetic is not here.</b> Everything about how a regenerating pool refills,
    /// is spent, is granted and is merged lives in <see cref="RegenLedger"/>, which hints
    /// also run — see its type docs for the merge and the proof that the join preserves it.
    /// This type is the heart-shaped part: the numbers come from the published
    /// <see cref="HeartRuleTable"/>, the clock can be shortened by a boost, and a file
    /// written before the ledger existed carries a bare count that has to be rebased onto
    /// one. Splitting it that way is invariant 5b's rule applied before the mistake rather
    /// than after it — the walk and the join were about to exist twice.
    /// </para>
    /// <para>
    /// The count is <c>Produced - Spent</c>, derived exactly as XP and credits are
    /// (invariant 9). There is deliberately no way to assign it.
    /// </para>
    /// <para>
    /// <b>The refill cap is a rule about the clock, not about the ledger.</b> The structural
    /// bound is <see cref="HeartLimits.HardCeiling"/> rather than the five a timer stops at,
    /// which is the whole of what changed when rewards were allowed to stack past a full
    /// bar. Nothing else had to: <see cref="At(long)"/> already asked only for the
    /// <em>count</em> before granting, so making it stop at
    /// <see cref="HeartRules.RefillCap"/> while the state is bounded above leaves the join
    /// and its proof untouched. The alternative — a second counter separating waited-for
    /// hearts from collected ones — was built first and buys nothing, because no rule here
    /// ever needs to know which kind a heart was: a spend takes whichever is nearest to
    /// hand, and the timer's question is always "does this player already have enough".
    /// </para>
    /// <para>
    /// <b>The bound is <see cref="HeartLimits.HardCeiling"/> and not the ceiling players
    /// meet</b>, which is the one subtlety in the whole type. The gate is content, so the
    /// ceiling a player experiences can be lowered from a config push — and if that number
    /// were the clamp, the push would cut <c>produced</c> <em>downward</em> on whichever
    /// devices had fetched it. <c>produced</c> only ever rises, and the whole merge proof
    /// rests on that. So the published ceiling is enforced in <see cref="Grant(int, long)"/>,
    /// where it is a decision taken once, and the structural bound is a constant no file can
    /// move.
    /// </para>
    /// <para>
    /// The type is a value with no clock of its own — every method takes <c>now</c>. That is
    /// what lets the whole rule be tested at arbitrary times without waiting eight hours,
    /// and what keeps the question of <em>whose</em> time it is (see <see cref="GameClock"/>)
    /// out of the rule entirely.
    /// </para>
    /// </summary>
    public readonly struct Hearts : IEquatable<Hearts>
    {
        readonly RegenLedger _ledger;

        Hearts(RegenLedger ledger) { _ledger = ledger; }

        /// <summary>Every heart ever handed to this player. Only ever rises.</summary>
        public long Produced => _ledger.Produced;

        /// <summary>Every heart ever consumed. Only ever rises.</summary>
        public long Spent => _ledger.Spent;

        /// <summary>
        /// When the pending refill lands. Never cleared on reaching the cap — see
        /// <see cref="RegenLedger.DueUnix"/> for why a field that is zeroed cannot be
        /// merged. What a screen should draw is <see cref="NextRefillUnix"/>.
        /// </summary>
        public long DueUnix => _ledger.DueUnix;

        /// <summary>
        /// The published rules, as bounds the ledger can work in, with whatever boost is
        /// running folded into the period.
        /// </summary>
        static RegenBounds Bounds(long boostUntilUnix)
        {
            // Taken once. These are published numbers, so each read walks a table reference
            // — cheap, but this is on the path every HUD tick takes.
            var rules = HeartRules.Table;

            return new RegenBounds(
                rules.RefillCap, rules.Ceiling, HeartLimits.HardCeiling,
                new RegenPeriod(rules.RefillSeconds, rules.BoostedRefillSeconds, boostUntilUnix));
        }

        /// <summary>The ledger, as stored. Clamped into the invariants on the way in.</summary>
        public static Hearts Ledger(long produced, long spent, long dueUnix)
            => new Hearts(RegenLedger.Of(produced, spent, dueUnix, HeartLimits.HardCeiling));

        /// <summary>
        /// A device's observation of itself — "I hold this many, next one at this time" —
        /// with nothing known about how it got there.
        ///
        /// The shape a pre-v8 save carries, and the only thing that can be recovered from
        /// one. See <see cref="Observation"/> for how it is folded into a real ledger.
        /// </summary>
        public Hearts(int count, long nextRefillUnix)
            : this(RegenLedger.Of(count < 0 ? 0
                                : count > HeartLimits.HardCeiling ? HeartLimits.HardCeiling : count,
                                  0L, nextRefillUnix, HeartLimits.HardCeiling)) { }

        /// <summary>
        /// A count observed on a device that keeps no ledger, rebased onto one that does.
        ///
        /// <para>
        /// Anchoring on the other side's <paramref name="spentAnchor"/> is what turns "an
        /// old build says 3" into a ledger entry that can be joined rather than compared:
        /// the result carries the same 3 hearts, and a plain <see cref="Join"/> against the
        /// modern side then resolves to the larger of the two counts. Generous, and
        /// deliberately so — the observation is all the evidence that exists, it is bounded
        /// by the ceiling, and the alternative is deleting hearts from whichever device
        /// happened to be running the older build.
        /// </para>
        /// </summary>
        public static Hearts Observation(int count, long dueUnix, long spentAnchor)
        {
            if (spentAnchor < 0) spentAnchor = 0;

            int held = count < 0 ? 0
                     : count > HeartLimits.HardCeiling ? HeartLimits.HardCeiling : count;

            return Ledger(spentAnchor + held, spentAnchor, dueUnix);
        }

        /// <summary>A new account's starting set: the refill cap, which is what the clock
        /// would have brought them to anyway.</summary>
        public static Hearts Full => Ledger(HeartRules.RefillCap, 0, 0);

        /// <summary>Hearts held, never below zero and never above the ceiling.</summary>
        public int Count => (int)_ledger.Count;

        /// <summary>
        /// Whether the clock has nothing left to do — the player holds at least
        /// <see cref="HeartRules.RefillCap"/>, so no refill is pending.
        ///
        /// The question every timer rule asks, and deliberately not "is this player full":
        /// somebody holding eight is not waiting for a ninth, but they are also not at any
        /// kind of maximum. <see cref="IsAtCeiling"/> is the other question, and only one
        /// caller has ever needed it.
        /// </summary>
        public bool IsRefilled => _ledger.IsRefilled(HeartRules.RefillCap);

        /// <summary>
        /// Whether another heart would be thrown away — the <em>published</em> ceiling, so
        /// this is the one property here that can change without the ledger changing. See
        /// <see cref="Grant(int, long)"/>.
        /// </summary>
        public bool IsAtCeiling => _ledger.IsAtCeiling(HeartRules.Ceiling);

        public bool IsEmpty => _ledger.IsEmpty;

        /// <summary>Whether the player may start a run. The gate, in one place.</summary>
        public bool CanPlay => !_ledger.IsEmpty;

        /// <summary>
        /// When the next heart arrives, or 0 when the player is at or above the refill cap
        /// and no timer is running — the number a HUD should draw.
        ///
        /// Derived rather than stored, which is the whole trick: the screen still gets its
        /// "no timer" sentinel, and the merge never sees one.
        /// </summary>
        public long NextRefillUnix => _ledger.NextDueUnix(HeartRules.RefillCap);

        /// <summary>
        /// Brings the state up to date at <paramref name="now"/>, granting whatever refills
        /// have fallen due.
        /// </summary>
        public Hearts At(long now) => At(now, 0L);

        /// <summary>
        /// The same, with a heart boost running until <paramref name="boostUntilUnix"/>.
        ///
        /// <para>
        /// The boost is passed in rather than stored on the struct because it is not a
        /// property of the hearts — it is a fact about the account, it merges separately,
        /// and a value type that carried it would have two states meaning "no boost" and a
        /// merge that had to reconcile them. Keeping it a parameter is also what lets the
        /// whole rule be exercised over arbitrary boost windows in a test.
        /// </para>
        /// </summary>
        public Hearts At(long now, long boostUntilUnix)
            => new Hearts(_ledger.At(now, Bounds(boostUntilUnix)));

        /// <summary>
        /// Spends hearts, starting the refill clock if it was not already running.
        /// Returns the state unchanged when there is nothing to spend.
        /// </summary>
        public Hearts Spend(int amount, long now) => Spend(amount, now, 0L);

        public Hearts Spend(int amount, long now, long boostUntilUnix)
            => new Hearts(_ledger.Spend(amount, now, Bounds(boostUntilUnix)));

        /// <summary>
        /// Grants hearts — a chest, a streak night, a watched video, a server correction.
        ///
        /// <para>
        /// <b>Grants stack past <see cref="HeartRules.RefillCap"/>.</b> The clamp is
        /// <see cref="HeartRules.Ceiling"/>, and the gap between those two numbers is the
        /// feature: a player at a full bar who opens a chest, collects a streak night or
        /// watches a video keeps what they were given instead of watching it evaporate.
        /// </para>
        /// <para>
        /// This is a reversal, and worth stating plainly because the argument against it
        /// used to be written here. It was that a heart is a gate rather than a currency, so
        /// banking them would hand somebody a week of uninterrupted play. That is true of
        /// the <em>timer</em> and false of everything else: the clock still refuses to carry
        /// anyone past five, so the pace of free play is unchanged, and a surplus only ever
        /// arrives through something the player did — which is the moment a game should be
        /// paying out, not the moment it should be quietly confiscating. What remains of the
        /// old argument is the ceiling, which bounds the damage a mistyped drop table can do
        /// without punishing anybody for being engaged.
        /// </para>
        /// </summary>
        public Hearts Grant(int amount, long now) => Grant(amount, now, 0L);

        public Hearts Grant(int amount, long now, long boostUntilUnix)
            => new Hearts(_ledger.Grant(amount, now, Bounds(boostUntilUnix)));

        /// <summary>
        /// Joins two boost deadlines. The later one wins.
        ///
        /// Generous, and it can afford to be for the same reason the count no longer has to
        /// be conservative: the award behind a boost is deduplicated by its own derived id
        /// long before this runs, so a boost cannot be minted by playing on two devices.
        /// Taking the earlier deadline would instead let a second device that has never
        /// opened a chest cut short a boost the player is holding.
        /// </summary>
        public static long JoinBoost(long a, long b) => a > b ? a : b;

        /// <summary>Seconds until the next heart, or 0 when no timer is running or it is overdue.</summary>
        public long SecondsToNext(long now) => _ledger.SecondsToNext(now, HeartRules.RefillCap);

        /// <summary>
        /// Joins two devices' hearts. Three <c>max</c>es and no special cases — see
        /// <see cref="RegenLedger.Join"/> for why every field is a counter of something that
        /// happened rather than a balance, and why that makes the join lossless.
        /// </summary>
        public static Hearts Join(Hearts a, Hearts b)
            => new Hearts(RegenLedger.Join(a._ledger, b._ledger, HeartLimits.HardCeiling));

        // ------------------------------------------------------------- equality
        /// <summary>
        /// Compares the ledger, not the count.
        ///
        /// Two states can show the same number of hearts and differ in what has to be
        /// written — reaching the cap advances <see cref="DueUnix"/> without moving
        /// <see cref="Count"/>, and losing that would leave a device merging against a
        /// deadline it had already passed. Callers deciding whether to save must ask this
        /// rather than comparing what is on screen.
        /// </summary>
        public bool Equals(Hearts other) => _ledger.Equals(other._ledger);

        public override bool Equals(object obj) => obj is Hearts other && Equals(other);

        public override int GetHashCode() => _ledger.GetHashCode();

        public static bool operator ==(Hearts a, Hearts b) => a.Equals(b);
        public static bool operator !=(Hearts a, Hearts b) => !a.Equals(b);

        public override string ToString()
            => $"{Count}/{HeartRules.RefillCap} hearts (produced {Produced}, spent {Spent}), next at {NextRefillUnix}";
    }
}
