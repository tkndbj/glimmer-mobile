using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The player's hints: a <see cref="RegenLedger"/> under the published
    /// <see cref="HintRuleTable"/>.
    ///
    /// <para>
    /// <b>An account-wide pool, not a per-glade allowance.</b> A hint used to be three per
    /// board, handed back in full at every board — so it cost nothing, meant nothing, and
    /// the only players who never used one were the ones who had not noticed the button.
    /// It is now a resource on a clock, exactly like a heart: three in the pool, one back
    /// every eight hours, spent wherever the player decides it is worth spending. That
    /// makes using one a decision, which is the whole feature.
    /// </para>
    /// <para>
    /// <b>The arithmetic is not here.</b> Everything about how a regenerating pool refills,
    /// is spent, is granted and is merged lives in <see cref="RegenLedger"/>, which hearts
    /// also run — see its type docs for the merge and the proof that the join preserves it.
    /// This type is the hint-shaped part, and it is thin on purpose: hints differ from
    /// hearts in three ways only. Their numbers come from <see cref="HintRules"/>; nothing
    /// shortens their clock, so the period is <see cref="RegenPeriod.Flat"/>; and there is
    /// no pre-ledger shape to migrate from, because a hint allowance was never written to a
    /// save file at all.
    /// </para>
    /// <para>
    /// <b>Absent means a fresh full pool, and that is the whole migration.</b> A file
    /// written before this existed has <c>hintsProduced</c> of zero, which is unreachable
    /// for a real ledger — an account is seeded at <see cref="Full"/> and
    /// <c>produced</c> only ever rises — so <see cref="WalletDto.hintsProduced"/> carries
    /// the same sentinel <see cref="WalletDto.heartsProduced"/> does, and every existing
    /// player opens the new build holding three. Nothing has to be backfilled because
    /// nothing about a past run implies how many hints it used.
    /// </para>
    /// <para>
    /// <b>The ceiling equals the cap as shipped</b>, so a granted hint at three is refused
    /// rather than half-paid — the opposite of hearts, and deliberate (see
    /// <see cref="HintLimits.DefaultCeiling"/>). It is safe only because nothing offers a
    /// hint without asking <see cref="IsAtCeiling"/> first. Anything that learns to grant
    /// hints later must ask, or it will take thirty seconds of somebody's life in exchange
    /// for nothing.
    /// </para>
    /// <para>
    /// The type is a value with no clock of its own — every method takes <c>now</c> — for
    /// <see cref="Hearts"/>'s reason: it is what lets the whole rule be tested at arbitrary
    /// times without waiting eight hours.
    /// </para>
    /// </summary>
    public readonly struct Hints : IEquatable<Hints>
    {
        readonly RegenLedger _ledger;

        Hints(RegenLedger ledger) { _ledger = ledger; }

        /// <summary>Every hint ever handed to this player. Only ever rises.</summary>
        public long Produced => _ledger.Produced;

        /// <summary>Every hint ever consumed. Only ever rises.</summary>
        public long Spent => _ledger.Spent;

        /// <summary>
        /// When the pending refill lands. Never cleared on reaching the cap — see
        /// <see cref="RegenLedger.DueUnix"/> for why a field that is zeroed cannot be
        /// merged. What a screen should draw is <see cref="NextRefillUnix"/>.
        /// </summary>
        public long DueUnix => _ledger.DueUnix;

        /// <summary>The published rules, as bounds the ledger can work in.</summary>
        static RegenBounds Bounds()
        {
            var rules = HintRules.Table;
            return new RegenBounds(rules.RefillCap, rules.Ceiling,
                                   HintLimits.HardCeiling, rules.Period);
        }

        /// <summary>The ledger, as stored. Clamped into the invariants on the way in.</summary>
        public static Hints Ledger(long produced, long spent, long dueUnix)
            => new Hints(RegenLedger.Of(produced, spent, dueUnix, HintLimits.HardCeiling));

        /// <summary>
        /// A new account's starting set: the refill cap, which is what the clock would have
        /// brought them to anyway. Also what a file written before hints existed reads as.
        /// </summary>
        public static Hints Full => Ledger(HintRules.RefillCap, 0, 0);

        /// <summary>Hints held, never below zero and never above the ceiling.</summary>
        public int Count => (int)_ledger.Count;

        /// <summary>Whether the clock has nothing left to do.</summary>
        public bool IsRefilled => _ledger.IsRefilled(HintRules.RefillCap);

        /// <summary>
        /// Whether another hint would be thrown away — the <em>published</em> ceiling, so
        /// this is the one property here that can change without the ledger changing.
        /// Anything about to offer a hint has to ask this first.
        /// </summary>
        public bool IsAtCeiling => _ledger.IsAtCeiling(HintRules.Ceiling);

        public bool IsEmpty => _ledger.IsEmpty;

        /// <summary>Whether a hint can be spent right now. The gate, in one place.</summary>
        public bool CanSpend => !_ledger.IsEmpty;

        /// <summary>
        /// When the next hint arrives, or 0 when the player is at or above the refill cap
        /// and no timer is running — the number a HUD should draw.
        /// </summary>
        public long NextRefillUnix => _ledger.NextDueUnix(HintRules.RefillCap);

        /// <summary>Seconds until the next hint, or 0 when no timer is running or it is overdue.</summary>
        public long SecondsToNext(long now) => _ledger.SecondsToNext(now, HintRules.RefillCap);

        /// <summary>
        /// Brings the state up to date at <paramref name="now"/>, granting whatever refills
        /// have fallen due.
        /// </summary>
        public Hints At(long now) => new Hints(_ledger.At(now, Bounds()));

        /// <summary>
        /// Spends hints, starting the refill clock if it was not already running. Returns
        /// the state unchanged when there is nothing to spend.
        /// </summary>
        public Hints Spend(int amount, long now) => new Hints(_ledger.Spend(amount, now, Bounds()));

        /// <summary>
        /// Grants hints the player did not wait for — a watched video today, whatever earns
        /// one later. Refused outright at the ceiling rather than partly paid, which is why
        /// the offer asks <see cref="IsAtCeiling"/> before it is made.
        /// </summary>
        public Hints Grant(int amount, long now) => new Hints(_ledger.Grant(amount, now, Bounds()));

        /// <summary>
        /// Joins two devices' hints. Three <c>max</c>es and no special cases — see
        /// <see cref="RegenLedger.Join"/>.
        /// </summary>
        public static Hints Join(Hints a, Hints b)
            => new Hints(RegenLedger.Join(a._ledger, b._ledger, HintLimits.HardCeiling));

        // ------------------------------------------------------------- equality
        /// <summary>
        /// Compares the ledger, not the count — <see cref="Hearts.Equals(Hearts)"/>'s rule,
        /// and it matters for the same reason: reaching the cap advances
        /// <see cref="DueUnix"/> without moving <see cref="Count"/>.
        /// </summary>
        public bool Equals(Hints other) => _ledger.Equals(other._ledger);

        public override bool Equals(object obj) => obj is Hints other && Equals(other);

        public override int GetHashCode() => _ledger.GetHashCode();

        public static bool operator ==(Hints a, Hints b) => a.Equals(b);
        public static bool operator !=(Hints a, Hints b) => !a.Equals(b);

        public override string ToString()
            => $"{Count}/{HintRules.RefillCap} hints (produced {Produced}, spent {Spent}), next at {NextRefillUnix}";
    }
}
