using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// How many hearts a player may hold and how fast they come back.
    ///
    /// A deliberate placeholder, in the same sense as the companion roster: the numbers
    /// are here rather than in <c>progression.json</c> because nothing has needed to
    /// retune them live yet. When that day comes — and for a gate that sits between a
    /// player and the game, it will — this moves into the reward table beside the XP
    /// curve and every call site below is unchanged.
    /// </summary>
    public static class HeartRules
    {
        public const int Max = 5;

        /// <summary>
        /// Seconds between refills. Eight hours — a full set is a day and a half.
        ///
        /// Long enough that the gate is real rather than decorative, which is what
        /// makes it worth building the server clock for: at twenty-five minutes nobody
        /// bothers cheating, at eight hours they will.
        /// </summary>
        public const long RefillSeconds = 8 * 60 * 60;

        /// <summary>
        /// Seconds between refills while a heart boost is running. Half the normal wait,
        /// which is the smallest multiple a player actually feels — a boost that shaves
        /// twenty minutes off eight hours is a boost nobody notices they have.
        /// </summary>
        public const long BoostedRefillSeconds = 4 * 60 * 60;

        /// <summary>Longest boost a chest may award, so a bad drop table cannot grant a year.</summary>
        public const long MaxBoostHours = 72;

        /// <summary>What one lost run costs. Named so the rule is not a bare 1 in the flow.</summary>
        public const int DefeatCost = 1;

        /// <summary>
        /// How long the wait starting at <paramref name="at"/> lasts.
        ///
        /// Asked per refill rather than once per catch-up, because a boost can expire in
        /// the middle of a walk: a player who closes the app with two hours of boost left
        /// and opens it a day later has earned some hearts at the fast rate and the rest
        /// at the slow one, and rounding that either way is either a theft or a gift.
        /// </summary>
        public static long PeriodAt(long at, long boostUntilUnix)
            => boostUntilUnix > at ? BoostedRefillSeconds : RefillSeconds;
    }

    /// <summary>
    /// The player's hearts, as a double-entry ledger: everything ever produced,
    /// everything ever spent, and when the next one is due.
    ///
    /// <para>
    /// <b>Why not simply store the count.</b> A count is not mergeable. Two devices
    /// holding 3 and 0 are indistinguishable from one device that spent three hearts and
    /// one that has not heard about it yet, so any rule over the pair has to guess — and
    /// every guess is wrong somewhere. Taking the larger hands back hearts somebody
    /// spent, so two devices refill each other for free. Taking the smaller deletes
    /// hearts somebody earned, which is what this file used to do: because a sync is
    /// pull → join → push, a stale cloud snapshot of 0 destroyed a local 3 and then
    /// wrote the 0 back, so both sides agreed and the hearts were gone. A timer refill
    /// could not survive being backgrounded (observed live, 2026-08-14). Neither rule is
    /// salvageable, because the fault is the representation, not the comparison.
    /// </para>
    /// <para>
    /// <b>What is stored instead.</b> Three numbers that only ever rise, so the merge is
    /// <c>max</c> on each and needs no opinion about which device is "right":
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Produced"/> — every heart ever handed over, by the timer, by a
    /// chest, by an ad, by the starting set.</item>
    /// <item><see cref="Spent"/> — every heart ever consumed.</item>
    /// <item><see cref="DueUnix"/> — when the pending refill lands. It advances by one
    /// period per refill and is set forward when a spend restarts an idle timer; it is
    /// never rewound and never cleared.</item>
    /// </list>
    /// <para>
    /// The count is <c>Produced - Spent</c>, derived exactly as XP and credits are
    /// derived (invariant 9), and the same two properties that make those safe make this
    /// safe: a grant survives a merge because <c>max</c> keeps it, a spend survives
    /// because <c>max</c> keeps that too, and neither device can mint because both
    /// numbers are counters of things that happened rather than a balance to be
    /// negotiated.
    /// </para>
    /// <para>
    /// Two invariants hold at every moment and — this is the part that matters — are
    /// <em>preserved by the join</em>, which is what makes the merge total rather than
    /// merely usually-right. Writing <c>M</c> for <see cref="HeartRules.Max"/>:
    /// </para>
    /// <code>
    ///   spent ≤ produced ≤ spent + M
    /// </code>
    /// <para>
    /// The left half survives because a device cannot spend what it never produced, so
    /// <c>max(spentA, spentB)</c> is some <c>spentX ≤ producedX ≤ max(produced)</c> — the
    /// merged count can never go negative. The right half survives because
    /// <c>producedA ≤ spentA + M</c> and <c>producedB ≤ spentB + M</c> together give
    /// <c>max(produced) ≤ max(spent) + M</c> — the merged count can never exceed the cap.
    /// A player therefore cannot lose a heart to a sync and cannot gain one either, for
    /// any number of devices merging in any order any number of times.
    /// </para>
    /// <para>
    /// The type is a value with no clock of its own — every method takes <c>now</c>.
    /// That is what lets the whole rule be tested at arbitrary times without waiting
    /// eight hours, and what keeps the question of <em>whose</em> time it is (see
    /// <see cref="GameClock"/>) out of the rule entirely.
    /// </para>
    /// </summary>
    public readonly struct Hearts : IEquatable<Hearts>
    {
        /// <summary>Every heart ever handed to this player. Only ever rises.</summary>
        public readonly long Produced;

        /// <summary>Every heart ever consumed. Only ever rises.</summary>
        public readonly long Spent;

        /// <summary>
        /// When the pending refill lands.
        ///
        /// <para>
        /// Deliberately <b>not</b> cleared when the player reaches the cap, which is the
        /// one thing the old shape got wrong beyond the count: a field that is zeroed on
        /// reaching full is not monotonic, so it cannot be merged with <c>max</c>, so the
        /// merge needed a special case for "0 means no timer" — and a special case in a
        /// join is a rule that stops being a join. It idles in the past instead, and a
        /// spend from full moves it forward. Zero survives only as the bottom of the
        /// lattice: "this timer has never started", which any real timestamp beats.
        /// </para>
        /// <para>
        /// It is bounded above by <c>now + one period</c> on any honest device — a refill
        /// sets it to <c>(the moment it landed) + period</c> and a spend from full sets it
        /// to <c>now + period</c>, and neither can look further ahead than that — so
        /// taking the larger of two cannot push a player's next heart more than one
        /// period away. What a screen should draw is <see cref="NextRefillUnix"/>, which
        /// hides the idle value.
        /// </para>
        /// </summary>
        public readonly long DueUnix;

        Hearts(long produced, long spent, long dueUnix)
        {
            Spent = spent < 0 ? 0 : spent;

            // Both invariants restated rather than assumed. This is the only door into
            // the type, and the values behind it may have come from a truncated file, a
            // support tool, or a build that has not shipped yet.
            long floor = Spent;
            long ceiling = Spent + HeartRules.Max;

            Produced = produced < floor ? floor : produced > ceiling ? ceiling : produced;
            DueUnix = dueUnix < 0 ? 0 : dueUnix;
        }

        /// <summary>The ledger, as stored. Clamped into the invariants on the way in.</summary>
        public static Hearts Ledger(long produced, long spent, long dueUnix)
            => new Hearts(produced, spent, dueUnix);

        /// <summary>
        /// A device's observation of itself — "I hold this many, next one at this time" —
        /// with nothing known about how it got there.
        ///
        /// The shape a pre-v8 save carries, and the only thing that can be recovered from
        /// one. See <see cref="Observation"/> for how it is folded into a real ledger.
        /// </summary>
        public Hearts(int count, long nextRefillUnix)
            : this((long)(count < 0 ? 0 : count > HeartRules.Max ? HeartRules.Max : count),
                   0L, nextRefillUnix) { }

        /// <summary>
        /// A count observed on a device that keeps no ledger, rebased onto one that does.
        ///
        /// <para>
        /// Anchoring on the other side's <paramref name="spentAnchor"/> is what turns "an
        /// old build says 3" into a ledger entry that can be joined rather than compared:
        /// the result carries the same 3 hearts, and a plain <see cref="Join"/> against
        /// the modern side then resolves to the larger of the two counts. Generous, and
        /// deliberately so — the observation is all the evidence that exists, it is
        /// bounded by the cap, and the alternative is deleting hearts from whichever
        /// device happened to be running the older build.
        /// </para>
        /// </summary>
        public static Hearts Observation(int count, long dueUnix, long spentAnchor)
        {
            if (spentAnchor < 0) spentAnchor = 0;
            int held = count < 0 ? 0 : count > HeartRules.Max ? HeartRules.Max : count;

            return Ledger(spentAnchor + held, spentAnchor, dueUnix);
        }

        public static Hearts Full => Ledger(HeartRules.Max, 0, 0);

        /// <summary>Hearts held, never below zero and never above the cap.</summary>
        public int Count => (int)(Produced - Spent);

        public bool IsFull => Produced - Spent >= HeartRules.Max;
        public bool IsEmpty => Produced <= Spent;

        /// <summary>Whether the player may start a run. The gate, in one place.</summary>
        public bool CanPlay => Produced > Spent;

        /// <summary>
        /// When the next heart arrives, or 0 when the player is full and no timer is
        /// running — the number a HUD should draw.
        ///
        /// Derived rather than stored, which is the whole trick: the screen still gets
        /// its "no timer" sentinel, and the merge never sees one.
        /// </summary>
        public long NextRefillUnix => IsFull ? 0 : DueUnix;

        /// <summary>
        /// Brings the state up to date at <paramref name="now"/>, granting whatever
        /// refills have fallen due.
        /// </summary>
        public Hearts At(long now) => At(now, 0L);

        /// <summary>
        /// The same, with a heart boost running until <paramref name="boostUntilUnix"/>.
        ///
        /// <para>
        /// The boost is passed in rather than stored on the struct because it is not a
        /// property of the hearts — it is a fact about the account, it merges separately,
        /// and a value type that carried it would have two states meaning "no boost" and
        /// a merge that had to reconcile them. Keeping it a parameter is also what lets
        /// the whole rule be exercised over arbitrary boost windows in a test.
        /// </para>
        /// <para>
        /// An already-running timer is pulled forward when a boost is active, never
        /// pushed back. A player who is granted a boost with six hours left on the clock
        /// would otherwise get nothing from it until the following heart, which reads as
        /// the boost simply not working.
        /// </para>
        /// </summary>
        public Hearts At(long now, long boostUntilUnix)
        {
            // Nothing accrues at the cap, and the deadline is left exactly where it is.
            // That idling value is what a later spend picks up, and leaving it alone is
            // what keeps repeated reads from writing the save file.
            if (IsFull) return this;

            // No deadline below the cap means the timer was never started — a pre-v4 save,
            // or one repaired by a merge. Start it now rather than granting a heart
            // immediately, which would pay the player for time they did not wait.
            long due = DueUnix > 0
                ? DueUnix
                : now + HeartRules.PeriodAt(now, boostUntilUnix);

            // Only ever shortens: min() against a boosted wait from this moment cannot
            // move a deadline further away, so repeating this on every read is stable.
            if (boostUntilUnix > now)
            {
                long boosted = now + HeartRules.BoostedRefillSeconds;
                if (boosted < due) due = boosted;
            }

            long produced = Produced;
            while (produced - Spent < HeartRules.Max && now >= due)
            {
                produced++;
                due += HeartRules.PeriodAt(due, boostUntilUnix);
            }

            return Ledger(produced, Spent, due);
        }

        /// <summary>
        /// Spends hearts, starting the refill clock if it was not already running.
        /// Returns the state unchanged when there is nothing to spend.
        /// </summary>
        public Hearts Spend(int amount, long now) => Spend(amount, now, 0L);

        public Hearts Spend(int amount, long now, long boostUntilUnix)
        {
            if (amount <= 0) return this;

            var current = At(now, boostUntilUnix);
            if (current.Count <= 0) return current;

            int take = amount > current.Count ? current.Count : amount;

            // Dropping from full is what starts the clock — NextRefillUnix reads 0 at the
            // cap, which is exactly the "no timer running" this needs. Already-running
            // timers are left where they are: losing a second heart must not push the
            // first one further away.
            long due = current.NextRefillUnix > 0
                ? current.NextRefillUnix
                : now + HeartRules.PeriodAt(now, boostUntilUnix);

            return Ledger(current.Produced, current.Spent + take, due);
        }

        /// <summary>Grants hearts — a chest, an ad, a purchase, a server correction.</summary>
        public Hearts Grant(int amount, long now) => Grant(amount, now, 0L);

        public Hearts Grant(int amount, long now, long boostUntilUnix)
        {
            var current = At(now, boostUntilUnix);
            if (amount <= 0) return current;

            // Overflow past the cap is dropped by the clamp rather than banked. A heart
            // is a gate, not a currency: banking them would let a chest run hand somebody
            // a week of uninterrupted play, which is the one thing the gate exists to
            // prevent. RewardedAds.WouldBenefit is what stops the offer being made.
            return Ledger(current.Produced + amount, current.Spent, current.DueUnix);
        }

        /// <summary>
        /// Joins two boost deadlines. The later one wins.
        ///
        /// Generous, and it can afford to be for the same reason the count no longer has
        /// to be conservative: the award behind a boost is deduplicated by its own
        /// derived id long before this runs, so a boost cannot be minted by playing on
        /// two devices. Taking the earlier deadline would instead let a second device
        /// that has never opened a chest cut short a boost the player is holding.
        /// </summary>
        public static long JoinBoost(long a, long b) => a > b ? a : b;

        /// <summary>Seconds until the next heart, or 0 when full or overdue.</summary>
        public long SecondsToNext(long now)
        {
            if (IsFull || DueUnix <= 0) return 0;
            long remaining = DueUnix - now;
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// Joins two devices' hearts. Idempotent, commutative and associative, like every
        /// other merge in this save file — and, unlike the rule it replaces, lossless.
        ///
        /// <para>
        /// Three <c>max</c>es and no special cases, which is the point. Every field is a
        /// counter of something that happened rather than a balance, so the larger value
        /// is always the one that knows more: a device that has produced more has seen a
        /// refill the other missed, a device that has spent more has played a run the
        /// other missed, and a later deadline is the one that has already paid out. There
        /// is nothing here for a stale snapshot to overwrite, so it does not matter
        /// whether the two sides are genuine peers or one is simply out of date — which
        /// is what the old rule had to guess at, and got wrong.
        /// </para>
        /// <para>
        /// Both invariants are closed under this, so the result is always a state the
        /// game could have reached on its own. See the type docs for the proof; the
        /// clamps in <see cref="Ledger"/> are belt to those braces, for files this build
        /// did not write.
        /// </para>
        /// </summary>
        public static Hearts Join(Hearts a, Hearts b)
            => Ledger(a.Produced > b.Produced ? a.Produced : b.Produced,
                      a.Spent > b.Spent ? a.Spent : b.Spent,
                      a.DueUnix > b.DueUnix ? a.DueUnix : b.DueUnix);

        // ------------------------------------------------------------- equality
        /// <summary>
        /// Compares the ledger, not the count.
        ///
        /// Two states can show the same number of hearts and differ in what has to be
        /// written — reaching the cap advances <see cref="DueUnix"/> without moving
        /// <see cref="Count"/>, and losing that would leave a device merging against a
        /// deadline it had already passed. Callers deciding whether to save must ask
        /// this rather than comparing what is on screen.
        /// </summary>
        public bool Equals(Hearts other)
            => Produced == other.Produced && Spent == other.Spent && DueUnix == other.DueUnix;

        public override bool Equals(object obj) => obj is Hearts other && Equals(other);

        public override int GetHashCode()
            => (Produced, Spent, DueUnix).GetHashCode();

        public static bool operator ==(Hearts a, Hearts b) => a.Equals(b);
        public static bool operator !=(Hearts a, Hearts b) => !a.Equals(b);

        public override string ToString()
            => $"{Count}/{HeartRules.Max} hearts (produced {Produced}, spent {Spent}), next at {NextRefillUnix}";
    }
}
