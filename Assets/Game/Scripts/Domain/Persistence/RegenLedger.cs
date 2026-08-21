using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// How long one wait lasts, and whether something is shortening it.
    ///
    /// <para>
    /// A value rather than a delegate, because the period is asked for once per refill
    /// inside a catch-up loop that runs on every HUD tick — a closure there would allocate
    /// per read, and the whole reason this arithmetic is worth extracting is that it is on
    /// the hot path of two resources rather than one.
    /// </para>
    /// <para>
    /// The boost is part of the period rather than part of the ledger for the reason
    /// <see cref="Hearts.At(long, long)"/> already gave: it is a fact about the account, it
    /// merges separately, and a ledger carrying it would have two states meaning "no boost"
    /// and a merge obliged to reconcile them. A pool nothing boosts passes
    /// <see cref="Flat"/>, and the boosted branch is then unreachable.
    /// </para>
    /// </summary>
    public readonly struct RegenPeriod
    {
        /// <summary>The ordinary wait, in seconds.</summary>
        public readonly long Seconds;

        /// <summary>The wait while a boost runs. Equal to <see cref="Seconds"/> when nothing can boost.</summary>
        public readonly long BoostedSeconds;

        /// <summary>When the boost ends; 0 when none is running.</summary>
        public readonly long BoostUntilUnix;

        public RegenPeriod(long seconds, long boostedSeconds, long boostUntilUnix)
        {
            Seconds = seconds < 1 ? 1 : seconds;
            BoostedSeconds = boostedSeconds < 1 ? Seconds : boostedSeconds;
            BoostUntilUnix = boostUntilUnix < 0 ? 0 : boostUntilUnix;
        }

        /// <summary>A period nothing can shorten.</summary>
        public static RegenPeriod Flat(long seconds) => new RegenPeriod(seconds, seconds, 0);

        /// <summary>Whether a boost is running at <paramref name="moment"/>.</summary>
        public bool BoostedAt(long moment) => BoostUntilUnix > moment;

        /// <summary>
        /// How long the wait starting at <paramref name="moment"/> lasts.
        ///
        /// Asked per refill rather than once per catch-up, because a boost can expire in
        /// the middle of a walk: somebody who closes the app with two hours of boost left
        /// and opens it a day later has earned some at the fast rate and the rest at the
        /// slow one, and rounding that either way is a theft or a gift.
        /// </summary>
        public long At(long moment) => BoostedAt(moment) ? BoostedSeconds : Seconds;
    }

    /// <summary>
    /// The numbers a regenerating pool is allowed to hold, and the one no content file may
    /// move.
    /// </summary>
    public readonly struct RegenBounds
    {
        /// <summary>Where the clock stops. Not a maximum — see <see cref="Ceiling"/>.</summary>
        public readonly int RefillCap;

        /// <summary>
        /// The most that may be held once granted extras are stacked on top. Enforced at
        /// the moment of a <see cref="RegenLedger.Grant"/> and never by re-reading a save,
        /// which is what makes it safe to lower from a config push.
        /// </summary>
        public readonly int Ceiling;

        /// <summary>
        /// The structural bound the clamp uses. A compile-time constant on both callers,
        /// and it has to be: see <see cref="HeartLimits.HardCeiling"/> for why using the
        /// published ceiling here would stop the join converging.
        /// </summary>
        public readonly int HardCeiling;

        public readonly RegenPeriod Period;

        public RegenBounds(int refillCap, int ceiling, int hardCeiling, RegenPeriod period)
        {
            HardCeiling = hardCeiling < 1 ? 1 : hardCeiling;
            RefillCap = refillCap < 0 ? 0 : refillCap > HardCeiling ? HardCeiling : refillCap;
            Ceiling = ceiling < RefillCap ? RefillCap : ceiling > HardCeiling ? HardCeiling : ceiling;
            Period = period;
        }
    }

    /// <summary>
    /// A resource that comes back on a clock, as a double-entry ledger: everything ever
    /// produced, everything ever spent, and when the next one is due.
    ///
    /// <para>
    /// <b>This is the arithmetic, and it exists once.</b> Hearts had it first and hints are
    /// the second caller; the two differ in their numbers, in whether anything can shorten
    /// their clock, and in what a file written before either existed looks like — and in
    /// nothing else. Writing the walk, the spend and the join out a second time is exactly
    /// the mistake invariant 5b names: five copies of "is this tile solved" were each
    /// correct until a tile appeared that one of them had not been written for. A merge
    /// that has to stay lossless across an unknown number of devices merging in an unknown
    /// order is a worse thing to hold two copies of than a mask comparison.
    /// </para>
    /// <para>
    /// <b>Why not simply store the count.</b> A count is not mergeable. Two devices holding
    /// 3 and 0 are indistinguishable from one that spent three and one that has not heard
    /// about a refill, so every rule over the pair is wrong somewhere: taking the larger
    /// hands back what somebody spent, so two devices refill each other for free, and
    /// taking the smaller deletes what somebody earned. Hearts shipped with the smaller and
    /// destroyed a refill on every sync — see invariant 11b. The fault is the
    /// representation, not the comparison.
    /// </para>
    /// <para>
    /// <b>What is stored instead.</b> Three numbers that only ever rise, so the merge is
    /// <c>max</c> on each and needs no opinion about which device is right. The count is
    /// <c>Produced - Spent</c>, derived exactly as XP and credits are (invariant 9).
    /// </para>
    /// <para>
    /// Two invariants hold at every moment and — this is the part that matters — are
    /// <em>preserved by the join</em>, which is what makes the merge total rather than
    /// merely usually-right. Writing <c>C</c> for <see cref="RegenBounds.HardCeiling"/>:
    /// </para>
    /// <code>
    ///   spent ≤ produced ≤ spent + C
    /// </code>
    /// <para>
    /// The left half survives because a device cannot spend what it never produced, so
    /// <c>max(spentA, spentB)</c> is some <c>spentX ≤ producedX ≤ max(produced)</c> — the
    /// merged count can never go negative. The right half survives because
    /// <c>producedA ≤ spentA + C</c> and <c>producedB ≤ spentB + C</c> together give
    /// <c>max(produced) ≤ max(spent) + C</c> — the merged count can never exceed the
    /// ceiling. A player therefore cannot lose one to a sync and cannot gain one either,
    /// for any number of devices merging in any order any number of times.
    /// </para>
    /// <para>
    /// The type has no clock of its own — every method takes <c>now</c>. That is what lets
    /// the whole rule be tested at arbitrary times without waiting eight hours, and what
    /// keeps the question of <em>whose</em> time it is (see <see cref="GameClock"/>) out of
    /// the rule entirely.
    /// </para>
    /// </summary>
    public readonly struct RegenLedger : IEquatable<RegenLedger>
    {
        /// <summary>Every one ever handed to this player. Only ever rises.</summary>
        public readonly long Produced;

        /// <summary>Every one ever consumed. Only ever rises.</summary>
        public readonly long Spent;

        /// <summary>
        /// When the pending refill lands.
        ///
        /// <para>
        /// Deliberately <b>not</b> cleared on reaching the cap. A field that is zeroed when
        /// full is not monotonic, so it cannot be merged with <c>max</c>, so the merge would
        /// need a special case for "0 means no timer" — and a special case in a join is a
        /// rule that has stopped being a join. It idles in the past instead, and a spend
        /// moves it forward. Zero survives only as the bottom of the lattice: "this timer
        /// has never started", which any real timestamp beats.
        /// </para>
        /// <para>
        /// It is bounded above by <c>now + one period</c> on any honest device — a refill
        /// sets it to (the moment it landed) + period and a spend that drops the player
        /// below the cap sets it to now + period, and neither looks further ahead than that
        /// — so taking the larger of two cannot push the next one more than one period
        /// away. What a screen should draw is <see cref="NextDueUnix"/>, which hides the
        /// idle value.
        /// </para>
        /// </summary>
        public readonly long DueUnix;

        RegenLedger(long produced, long spent, long dueUnix, int hardCeiling)
        {
            Spent = spent < 0 ? 0 : spent;

            // Both invariants restated rather than assumed. This is the only door into the
            // type, and the values behind it may have come from a truncated file, a support
            // tool, or a build that has not shipped yet. The permanent bound, never the
            // published one — this clamp runs on every read of every save, so anything it
            // depends on must be something no content file can move underneath it.
            long floor = Spent;
            long ceiling = Spent + (hardCeiling < 1 ? 1 : hardCeiling);

            Produced = produced < floor ? floor : produced > ceiling ? ceiling : produced;
            DueUnix = dueUnix < 0 ? 0 : dueUnix;
        }

        /// <summary>The ledger, as stored. Clamped into the invariants on the way in.</summary>
        public static RegenLedger Of(long produced, long spent, long dueUnix, int hardCeiling)
            => new RegenLedger(produced, spent, dueUnix, hardCeiling);

        /// <summary>Held right now, never below zero and never above the hard ceiling.</summary>
        public long Count => Produced - Spent;

        public bool IsEmpty => Produced <= Spent;

        /// <summary>Whether the clock has nothing left to do.</summary>
        public bool IsRefilled(int refillCap) => Produced - Spent >= refillCap;

        /// <summary>Whether another would be thrown away, at the <em>published</em> ceiling.</summary>
        public bool IsAtCeiling(int ceiling) => Produced - Spent >= ceiling;

        /// <summary>
        /// When the next one arrives, or 0 when the player is at or above the refill cap
        /// and no timer is running — the number a HUD should draw.
        ///
        /// Derived rather than stored, which is the whole trick: the screen still gets its
        /// "no timer" sentinel, and the merge never sees one.
        /// </summary>
        public long NextDueUnix(int refillCap) => IsRefilled(refillCap) ? 0 : DueUnix;

        /// <summary>Seconds until the next one, or 0 when no timer runs or it is overdue.</summary>
        public long SecondsToNext(long now, int refillCap)
        {
            if (IsRefilled(refillCap) || DueUnix <= 0) return 0;
            long remaining = DueUnix - now;
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// Brings the state up to date at <paramref name="now"/>, granting whatever refills
        /// have fallen due.
        /// </summary>
        public RegenLedger At(long now, in RegenBounds bounds)
        {
            // Nothing accrues at or above the refill cap, and the deadline is left exactly
            // where it is. That idling value is what a later spend picks up, and leaving it
            // alone is what keeps repeated reads from writing the save file.
            //
            // Somebody holding more than the cap sits here too, and holds their surplus for
            // as long as they like — the clock is a floor, not a drain. The idle deadline is
            // then a stale past timestamp for however long the surplus lasts; that is safe
            // because the only way back under the cap is Spend, which restarts it.
            if (IsRefilled(bounds.RefillCap)) return this;

            var period = bounds.Period;

            // No deadline below the cap means the timer was never started — a file written
            // before this pool existed, or one repaired by a merge. Start it now rather than
            // granting immediately, which would pay for time nobody waited.
            long due = DueUnix > 0 ? DueUnix : now + period.At(now);

            // Only ever shortens: min() against a boosted wait from this moment cannot move
            // a deadline further away, so repeating this on every read is stable. A player
            // granted a boost with six hours left would otherwise get nothing from it until
            // the following refill, which reads as the boost simply not working.
            if (period.BoostedAt(now))
            {
                long boosted = now + period.BoostedSeconds;
                if (boosted < due) due = boosted;
            }

            long produced = Produced;
            while (produced - Spent < bounds.RefillCap && now >= due)
            {
                produced++;
                due += period.At(due);
            }

            return Of(produced, Spent, due, bounds.HardCeiling);
        }

        /// <summary>
        /// Spends, starting the refill clock if it was not already running. Returns the
        /// state unchanged when there is nothing to spend.
        /// </summary>
        public RegenLedger Spend(int amount, long now, in RegenBounds bounds)
        {
            if (amount <= 0) return this;

            var current = At(now, bounds);
            if (current.Count <= 0) return current;

            long take = amount > current.Count ? current.Count : amount;

            // Dropping *through* the cap is what starts the clock, and asking NextDueUnix is
            // what makes that the rule rather than "dropping from exactly the cap": it reads
            // 0 for anybody at or above it, so somebody spending down from eight restarts
            // the timer on the spend that takes them to four and not on the three before it.
            // That is also what makes the idle deadline safe to leave in the past while a
            // surplus is held — the stale value is never the one a timer resumes from. An
            // already-running timer is left where it is: a second spend must not push the
            // first refill further away.
            long pending = current.NextDueUnix(bounds.RefillCap);
            long due = pending > 0 ? pending : now + bounds.Period.At(now);

            return Of(current.Produced, current.Spent + take, due, bounds.HardCeiling);
        }

        /// <summary>
        /// Grants extras the player did not wait for — a chest, a streak night, a watched
        /// video, a server correction.
        ///
        /// <para>
        /// <b>Grants stack past <see cref="RegenBounds.RefillCap"/>,</b> up to
        /// <see cref="RegenBounds.Ceiling"/>. The gap between those two numbers is the
        /// feature: somebody at a full bar who opens a chest keeps what they were given
        /// instead of watching it evaporate. A pool that sets the two equal has opted out of
        /// that, and then a grant at full is refused rather than half-paid — which is why an
        /// offer has to ask <see cref="IsAtCeiling"/> before it is ever made.
        /// </para>
        /// <para>
        /// The published ceiling is enforced here and nowhere else, which is what makes it
        /// safe to lower from a config push: a grant is a decision taken once, so a smaller
        /// ceiling refuses new ones without ever reaching back into a save to take one.
        /// </para>
        /// </summary>
        public RegenLedger Grant(int amount, long now, in RegenBounds bounds)
        {
            var current = At(now, bounds);
            if (amount <= 0) return current;

            long room = bounds.Ceiling - current.Count;
            if (room <= 0) return current;
            if (amount > room) amount = (int)room;

            return Of(current.Produced + amount, current.Spent, current.DueUnix, bounds.HardCeiling);
        }

        /// <summary>
        /// Joins two devices' ledgers. Idempotent, commutative and associative, like every
        /// other merge in this save file — and, unlike a stored count, lossless.
        ///
        /// <para>
        /// Three <c>max</c>es and no special cases, which is the point. Every field is a
        /// counter of something that happened rather than a balance, so the larger value is
        /// always the one that knows more: a device that has produced more has seen a refill
        /// the other missed, one that has spent more has played a run the other missed, and
        /// a later deadline is the one that has already paid out. There is nothing here for
        /// a stale snapshot to overwrite, so it does not matter whether the two sides are
        /// genuine peers or one is simply out of date — which is what the old rule had to
        /// guess at, and got wrong.
        /// </para>
        /// </summary>
        public static RegenLedger Join(RegenLedger a, RegenLedger b, int hardCeiling)
            => Of(a.Produced > b.Produced ? a.Produced : b.Produced,
                  a.Spent > b.Spent ? a.Spent : b.Spent,
                  a.DueUnix > b.DueUnix ? a.DueUnix : b.DueUnix,
                  hardCeiling);

        // ------------------------------------------------------------- equality
        /// <summary>
        /// Compares the ledger, not the count.
        ///
        /// Two states can show the same number and differ in what has to be written —
        /// reaching the cap advances <see cref="DueUnix"/> without moving
        /// <see cref="Count"/>, and losing that would leave a device merging against a
        /// deadline it had already passed. Callers deciding whether to save must ask this
        /// rather than comparing what is on screen.
        /// </summary>
        public bool Equals(RegenLedger other)
            => Produced == other.Produced && Spent == other.Spent && DueUnix == other.DueUnix;

        public override bool Equals(object obj) => obj is RegenLedger other && Equals(other);

        public override int GetHashCode() => (Produced, Spent, DueUnix).GetHashCode();

        public static bool operator ==(RegenLedger a, RegenLedger b) => a.Equals(b);
        public static bool operator !=(RegenLedger a, RegenLedger b) => !a.Equals(b);

        public override string ToString()
            => $"{Count} (produced {Produced}, spent {Spent}), next at {DueUnix}";
    }
}
