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

        /// <summary>What one lost run costs. Named so the rule is not a bare 1 in the flow.</summary>
        public const int DefeatCost = 1;
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
        public Hearts At(long now)
        {
            if (IsFull) return new Hearts(HeartRules.Max, 0);

            // Below max with no deadline means the timer was never started — a save
            // written before hearts regenerated, or one repaired by a merge. Start it
            // now rather than granting a heart immediately, which would pay the player
            // for time they did not wait.
            long deadline = NextRefillUnix > 0 ? NextRefillUnix : now + HeartRules.RefillSeconds;

            int count = Count;
            while (count < HeartRules.Max && now >= deadline)
            {
                count++;
                deadline += HeartRules.RefillSeconds;
            }

            return count >= HeartRules.Max ? new Hearts(HeartRules.Max, 0) : new Hearts(count, deadline);
        }

        /// <summary>
        /// Spends hearts, starting the refill clock if it was not already running.
        /// Returns the state unchanged when there is nothing to spend.
        /// </summary>
        public Hearts Spend(int amount, long now)
        {
            if (amount <= 0) return this;

            var current = At(now);
            if (current.Count <= 0) return current;

            int count = current.Count - amount;
            if (count < 0) count = 0;

            // Dropping from full is what starts the clock. Already-running timers are
            // left exactly where they are: losing a second heart must not push the
            // first one further away.
            long deadline = current.NextRefillUnix > 0
                ? current.NextRefillUnix
                : now + HeartRules.RefillSeconds;

            return new Hearts(count, deadline);
        }

        /// <summary>Grants hearts — a purchase, a gift, a server correction.</summary>
        public Hearts Grant(int amount, long now)
        {
            if (amount <= 0) return At(now);

            var current = At(now);
            int count = current.Count + amount;

            return count >= HeartRules.Max
                ? new Hearts(HeartRules.Max, 0)
                : new Hearts(count, current.NextRefillUnix);
        }

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
