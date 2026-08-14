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
    /// The player's hearts: how many are held, and when the next one lands.
    ///
    /// <para>
    /// Two numbers, because that is the smallest state that regenerates without drift.
    /// A naive "hearts + last update" loses the remainder every time it is read; an
    /// accruing counter cannot be merged across devices. Storing the <em>deadline</em>
    /// instead means each refill advances it by exactly one period, so a player who
    /// closes the app mid-timer loses nothing and gains nothing.
    /// </para>
    /// <para>
    /// The type is a value with no clock of its own — every method takes <c>now</c>.
    /// That is what lets the whole rule be tested at arbitrary times without waiting
    /// twenty-five minutes, and what keeps the question of <em>whose</em> time it is
    /// (see <see cref="GameClock"/>) out of the rule entirely.
    /// </para>
    /// </summary>
    public readonly struct Hearts
    {
        /// <summary>Hearts held, never above <see cref="HeartRules.Max"/>.</summary>
        public readonly int Count;

        /// <summary>
        /// When the next heart arrives, or 0 when the player is full and no timer is
        /// running. Zero is a real state, not "unknown": a full player has no deadline.
        /// </summary>
        public readonly long NextRefillUnix;

        public Hearts(int count, long nextRefillUnix)
        {
            Count = count < 0 ? 0 : count > HeartRules.Max ? HeartRules.Max : count;
            NextRefillUnix = nextRefillUnix < 0 ? 0 : nextRefillUnix;
        }

        public static Hearts Full => new Hearts(HeartRules.Max, 0);

        public bool IsFull => Count >= HeartRules.Max;
        public bool IsEmpty => Count <= 0;

        /// <summary>Whether the player may start a run. The gate, in one place.</summary>
        public bool CanPlay => Count > 0;

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
            if (IsFull) return new Hearts(HeartRules.Max, 0);

            // Below max with no deadline means the timer was never started — a save
            // written before hearts regenerated, or one repaired by a merge. Start it
            // now rather than granting a heart immediately, which would pay the player
            // for time they did not wait.
            long deadline = NextRefillUnix > 0
                ? NextRefillUnix
                : now + HeartRules.PeriodAt(now, boostUntilUnix);

            // Only ever shortens: min() against a boosted wait from this moment cannot
            // move a deadline further away, so repeating this on every read is stable.
            if (boostUntilUnix > now)
            {
                long boosted = now + HeartRules.BoostedRefillSeconds;
                if (boosted < deadline) deadline = boosted;
            }

            int count = Count;
            while (count < HeartRules.Max && now >= deadline)
            {
                count++;
                deadline += HeartRules.PeriodAt(deadline, boostUntilUnix);
            }

            return count >= HeartRules.Max ? new Hearts(HeartRules.Max, 0) : new Hearts(count, deadline);
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

            int count = current.Count - amount;
            if (count < 0) count = 0;

            // Dropping from full is what starts the clock. Already-running timers are
            // left exactly where they are: losing a second heart must not push the
            // first one further away.
            long deadline = current.NextRefillUnix > 0
                ? current.NextRefillUnix
                : now + HeartRules.PeriodAt(now, boostUntilUnix);

            return new Hearts(count, deadline);
        }

        /// <summary>Grants hearts — a purchase, a gift, a server correction.</summary>
        public Hearts Grant(int amount, long now) => Grant(amount, now, 0L);

        public Hearts Grant(int amount, long now, long boostUntilUnix)
        {
            var current = At(now, boostUntilUnix);
            if (amount <= 0) return current;

            int count = current.Count + amount;

            return count >= HeartRules.Max
                ? new Hearts(HeartRules.Max, 0)
                : new Hearts(count, current.NextRefillUnix);
        }

        /// <summary>
        /// Joins two boost deadlines. The later one wins.
        ///
        /// Generous, where <see cref="Join"/> is conservative, and the difference is that
        /// a boost cannot be minted by playing on two devices — it arrives from a chest,
        /// and that chest's award is deduplicated by its own derived id long before this
        /// runs. Taking the earlier deadline would instead let a second device that has
        /// never opened a chest cut short a boost the player is holding on their phone.
        /// </summary>
        public static long JoinBoost(long a, long b) => a > b ? a : b;

        /// <summary>Seconds until the next heart, or 0 when full or overdue.</summary>
        public long SecondsToNext(long now)
        {
            if (IsFull || NextRefillUnix <= 0) return 0;
            long remaining = NextRefillUnix - now;
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// Joins two devices' hearts. Idempotent, commutative and associative, like
        /// every other merge in this save file.
        ///
        /// The smaller count wins because hearts are consumable: taking the larger
        /// would let two devices refill each other for free. The later deadline wins
        /// for the same reason — it is the one that has granted the least so far. The
        /// pair is conservative rather than fair, which is the right way round when the
        /// alternative is minting a resource that will one day cost money.
        /// </summary>
        public static Hearts Join(Hearts a, Hearts b)
        {
            int count = a.Count < b.Count ? a.Count : b.Count;

            // 0 means "full, no timer", so it must not win a max() against a real
            // deadline — a full device would otherwise erase the other's countdown.
            long deadline = a.NextRefillUnix == 0 ? b.NextRefillUnix
                          : b.NextRefillUnix == 0 ? a.NextRefillUnix
                          : (a.NextRefillUnix > b.NextRefillUnix ? a.NextRefillUnix : b.NextRefillUnix);

            return count >= HeartRules.Max ? new Hearts(HeartRules.Max, 0) : new Hearts(count, deadline);
        }

        public override string ToString() => $"{Count}/{HeartRules.Max} hearts, next at {NextRefillUnix}";
    }
}
