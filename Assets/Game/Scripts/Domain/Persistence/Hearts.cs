using System;

namespace GlimmerGrove.Persistence
{
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
    /// merely usually-right. Writing <c>C</c> for <see cref="HeartLimits.HardCeiling"/>:
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
    /// ceiling. A player therefore cannot lose a heart to a sync and cannot gain one
    /// either, for any number of devices merging in any order any number of times.
    /// </para>
    /// <para>
    /// <b>The refill cap is a rule about the clock, not about the ledger.</b> The bound
    /// above is a ceiling rather than the five a timer stops at, and that is the whole of
    /// what changed when rewards were allowed to stack past a full bar. Nothing else had
    /// to: <see cref="At"/> already asked only for the <em>count</em> before granting, so
    /// making it stop at <see cref="HeartRules.RefillCap"/> while the state is bounded
    /// above leaves the join and its proof untouched. The alternative — a second counter
    /// separating waited-for hearts from collected ones — was built first and buys nothing,
    /// because no rule here ever needs to know which kind a heart was: a spend takes
    /// whichever is nearest to hand, and the timer's question is always "does this player
    /// already have enough".
    /// </para>
    /// <para>
    /// <b>The bound is <see cref="HeartLimits.HardCeiling"/> and not the ceiling players
    /// meet</b>, which is the one subtlety in the whole type. The gate is content now, so
    /// the ceiling a player experiences can be lowered from a config push — and if that
    /// number were the clamp here, the push would cut <c>produced</c> downward on whichever
    /// devices had fetched it. <c>produced</c> is a counter that only ever rises; take that
    /// away and the join stops converging, because one device would keep restoring what the
    /// other kept clamping. The published ceiling is therefore enforced in
    /// <see cref="Grant"/>, where it is a decision made once, and the structural bound here
    /// is a constant no file can move.
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
        /// sets it to <c>(the moment it landed) + period</c> and a spend that drops a
        /// player below the cap sets it to <c>now + period</c>, and neither can look
        /// further ahead than that — so taking the larger of two cannot push a player's
        /// next heart more than one period away. What a screen should draw is
        /// <see cref="NextRefillUnix"/>, which hides the idle value.
        /// </para>
        /// </summary>
        public readonly long DueUnix;

        Hearts(long produced, long spent, long dueUnix)
        {
            Spent = spent < 0 ? 0 : spent;

            // Both invariants restated rather than assumed. This is the only door into
            // the type, and the values behind it may have come from a truncated file, a
            // support tool, or a build that has not shipped yet.
            // The permanent bound, never the published one — see the type docs. This clamp
            // runs on every read of every save, so anything it depends on must be something
            // no content file can move underneath it.
            long floor = Spent;
            long ceiling = Spent + HeartLimits.HardCeiling;

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
            : this((long)(count < 0 ? 0
                        : count > HeartLimits.HardCeiling ? HeartLimits.HardCeiling : count),
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
        /// bounded by the ceiling, and the alternative is deleting hearts from whichever
        /// device happened to be running the older build.
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
        public int Count => (int)(Produced - Spent);

        /// <summary>
        /// Whether the clock has nothing left to do — the player holds at least
        /// <see cref="HeartRules.RefillCap"/>, so no refill is pending.
        ///
        /// The question every timer rule asks, and deliberately not "is this player full":
        /// somebody holding eight is not waiting for a ninth, but they are also not at any
        /// kind of maximum. <see cref="IsAtCeiling"/> is the other question, and only one
        /// caller has ever needed it.
        /// </summary>
        public bool IsRefilled => Produced - Spent >= HeartRules.RefillCap;

        /// <summary>
        /// Whether another heart would be thrown away — the <em>published</em> ceiling, so
        /// this is the one property here that can change without the ledger changing. See
        /// <see cref="Grant"/>.
        /// </summary>
        public bool IsAtCeiling => Produced - Spent >= HeartRules.Ceiling;

        public bool IsEmpty => Produced <= Spent;

        /// <summary>Whether the player may start a run. The gate, in one place.</summary>
        public bool CanPlay => Produced > Spent;

        /// <summary>
        /// When the next heart arrives, or 0 when the player is at or above the refill cap
        /// and no timer is running — the number a HUD should draw.
        ///
        /// Derived rather than stored, which is the whole trick: the screen still gets
        /// its "no timer" sentinel, and the merge never sees one.
        /// </summary>
        public long NextRefillUnix => IsRefilled ? 0 : DueUnix;

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
            // Nothing accrues at or above the refill cap, and the deadline is left exactly
            // where it is. That idling value is what a later spend picks up, and leaving it
            // alone is what keeps repeated reads from writing the save file.
            //
            // A player holding more than the cap sits here too, and holds their surplus for
            // as long as they like — the clock is not a drain, it is a floor. Note that the
            // idle deadline is then a stale past timestamp for however long the surplus
            // lasts; that is safe because the only way back under the cap is Spend, which
            // restarts it. See the note there.
            if (IsRefilled) return this;

            // Taken once. These are published numbers now, so each read walks a table
            // reference — cheap, but this is the method every HUD tick calls and the loop
            // below asks for a period per refill.
            var rules = HeartRules.Table;

            // No deadline below the cap means the timer was never started — a pre-v4 save,
            // or one repaired by a merge. Start it now rather than granting a heart
            // immediately, which would pay the player for time they did not wait.
            long due = DueUnix > 0
                ? DueUnix
                : now + rules.PeriodAt(now, boostUntilUnix);

            // Only ever shortens: min() against a boosted wait from this moment cannot
            // move a deadline further away, so repeating this on every read is stable.
            if (boostUntilUnix > now)
            {
                long boosted = now + rules.BoostedRefillSeconds;
                if (boosted < due) due = boosted;
            }

            long produced = Produced;
            while (produced - Spent < rules.RefillCap && now >= due)
            {
                produced++;
                due += rules.PeriodAt(due, boostUntilUnix);
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

            // Dropping *through* the cap is what starts the clock, and asking
            // NextRefillUnix is what makes that the rule rather than "dropping from
            // exactly five": it reads 0 for anybody at or above the cap, so a player
            // spending their way down from eight restarts the timer on the one spend that
            // takes them to four and not on the three before it. That is also what makes
            // the idle deadline safe to leave in the past while a surplus is held — the
            // stale value is never the one a timer resumes from. Already-running timers
            // are left where they are: losing a second heart must not push the first one
            // further away.
            long due = current.NextRefillUnix > 0
                ? current.NextRefillUnix
                : now + HeartRules.PeriodAt(now, boostUntilUnix);

            return Ledger(current.Produced, current.Spent + take, due);
        }

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
        /// used to be written here. It was that a heart is a gate rather than a currency,
        /// so banking them would hand somebody a week of uninterrupted play. That is true
        /// of the <em>timer</em> and false of everything else: the clock still refuses to
        /// carry anyone past five, so the pace of free play is unchanged, and a surplus
        /// only ever arrives through something the player did — which is the moment a game
        /// should be paying out, not the moment it should be quietly confiscating. What
        /// remains of the old argument is the ceiling, which bounds the damage a mistyped
        /// drop table can do without punishing anybody for being engaged.
        /// </para>
        /// </summary>
        public Hearts Grant(int amount, long now) => Grant(amount, now, 0L);

        public Hearts Grant(int amount, long now, long boostUntilUnix)
        {
            var current = At(now, boostUntilUnix);
            if (amount <= 0) return current;

            // The published ceiling is enforced here and nowhere else, which is what makes
            // it safe to lower from a config push: a grant is a decision taken once, so a
            // smaller ceiling refuses new hearts without ever reaching back into a save to
            // take one. Overflow is dropped rather than banked, and RewardedAds is what
            // stops the offer being made into a ledger that is already full.
            int room = HeartRules.Ceiling - current.Count;
            if (room <= 0) return current;
            if (amount > room) amount = room;

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

        /// <summary>Seconds until the next heart, or 0 when no timer is running or it is overdue.</summary>
        public long SecondsToNext(long now)
        {
            if (IsRefilled || DueUnix <= 0) return 0;
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
            => $"{Count}/{HeartRules.RefillCap} hearts (produced {Produced}, spent {Spent}), next at {NextRefillUnix}";
    }
}
