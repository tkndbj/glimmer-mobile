using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// <b>Refreshing a hand</b> (MODES.md 59p, the owner, 2026-10-15): the open hand is swapped for
    /// a fresh one for gems, as often as the player can pay, at a price that doubles with every
    /// refresh the run has bought - 5, 10, 20, 40 - and starts again at five on the next run.
    ///
    /// <para>
    /// <b>A debit and nothing else, so the server needs nothing new</b> - the continue's shape
    /// (<c>ContinueTable</c>): the gems go through <see cref="PlayerProgression.TrySpend"/> under a
    /// fresh idempotency key and <c>submitSpends</c> refuses one the server-derived balance cannot
    /// cover; what they buy is a different hand on one run, which mints nothing, is stored nowhere
    /// and is gone when the run ends. No claim, no entitlement, no save field, no rules release.
    /// </para>
    /// <para>
    /// <b>The count lives on the run</b> (<see cref="ShuffleRun.Refreshes"/>), never on the hand
    /// and never in the save: a new hand does not reset the price, a new run does, and a run
    /// carried on by a continue is the same run and keeps its price.
    /// </para>
    /// <para>
    /// <b>Doubling is the ceiling</b> (invariant 5d asked of a price, <c>ContinueLimits</c>'
    /// reasoning): the tenth refresh of one run costs over two and a half thousand gems, so there
    /// is always a number of redeals beyond which the honest answer is the hand on the table.
    /// <see cref="MostGems"/> only keeps the arithmetic finite; nothing real reaches it.
    /// </para>
    /// </summary>
    public static class ShuffleRefresh
    {
        /// <summary>What the first refresh of a run costs, in gems.</summary>
        public const long FirstGems = 5L;

        /// <summary>
        /// The most one refresh can cost: a saturation, not a design figure. Reached on the 28th
        /// refresh of one run, after more than a billion gems spent inside it.
        /// </summary>
        public const long MostGems = 1_000_000_000L;

        /// <summary>Carried on the debit for support and analytics, with the level's id after it.</summary>
        public const string SpendReason = "shuffle_refresh:";

        /// <summary>
        /// What the next refresh costs after <paramref name="taken"/> on this run: five, doubled
        /// once per refresh already bought, saturating at <see cref="MostGems"/>. Integer and
        /// early-returning, so a count nothing bounds can neither overflow nor spin.
        /// </summary>
        public static long PriceFor(int taken)
        {
            long price = FirstGems;

            for (int i = 0; i < taken; i++)
            {
                if (price >= MostGems / 2L) return MostGems;
                price *= 2L;
            }

            return price;
        }

        /// <summary>
        /// Takes the gems for a refresh the player asked for and deals the new hand onto the
        /// table. Answers false, charging nothing, when the run has no hand open, the deck has
        /// nothing fresh to deal (<see cref="ShuffleRun.CanRefresh"/>), or the balance cannot
        /// meet the price at the instant of the debit.
        ///
        /// <para>
        /// <b>Asked before it is charged</b>: <see cref="ShuffleRun.CanRefresh"/> is a pure
        /// reading of the build and the deck, and when it says yes <see cref="ShuffleRun.Refresh"/>
        /// cannot fail - so gems are never taken for a hand that is not dealt.
        /// </para>
        /// </summary>
        public static bool TryBuy(ShuffleRun run, LevelId level)
        {
            if (run == null || !run.CanRefresh) return false;

            long price = run.RefreshPrice;
            if (!PlayerProgression.TrySpend(Currency.Gems, price, SpendReason + level.Value))
                return false;

            run.Refresh();

            ShuffleAnalytics.TrackRefresh(level, run, price);

            // The debit is owed to the server; requested rather than forced, as a continue does,
            // because several of these can land inside a minute.
            CloudSaveService.RequestSync();

            return true;
        }
    }
}
