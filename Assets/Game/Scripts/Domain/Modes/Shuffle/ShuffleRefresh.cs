using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// What a refresh costs (MODES.md 59p): the first refresh of a run and what each one already
    /// bought multiplies the next by. <b>Published in <c>config/shuffle</c></b> (59l) beside the
    /// ramp and the deck, because a price is the number most likely to be wrong on the first guess
    /// and that document is the one place this lane can be retuned without a build; a run prices
    /// its refreshes off the snapshot it opened on.
    ///
    /// <para>
    /// <b>Optional on the device, required of the publisher</b>: a document published before the
    /// section existed is read with <see cref="Shipped"/> here rather than refused, so this build
    /// keeps the rest of the live tuning; the seeder's strict reading refuses a file without it.
    /// <c>firstGems</c> at nought withdraws the key from every hand.
    /// </para>
    /// </summary>
    public sealed class ShuffleRefreshTuning
    {
        // In the order the constructor takes them. A figure is added at the end.
        static readonly ShuffleRampTuning.Figure[] Table =
        {
            // Nought withdraws the offer; a thousand is a misplaced digit (the largest pack's worth).
            new ShuffleRampTuning.Figure("firstGems", 5, 0, 1_000),
            // Hundredths: 200 doubles, 100 holds the price still (never cheaper the more are bought,
            // ContinueLimits.MinGemsFactor's reason), 1,000 is ten times a refresh.
            new ShuffleRampTuning.Figure("factorHundredths", 200, 100, 1_000),
        };

        public static IReadOnlyList<ShuffleRampTuning.Figure> Figures => Table;

        public static readonly ShuffleRefreshTuning Shipped = new ShuffleRefreshTuning(Written(Table));

        readonly int[] _written;

        /// <summary>What the first refresh of a run costs, in gems. Nought: no refresh is sold.</summary>
        public int FirstGems => _written[0];

        /// <summary>What each refresh already bought multiplies the next one's price by, in hundredths.</summary>
        public int FactorHundredths => _written[1];

        /// <summary>Whether a refresh is sold at all.</summary>
        public bool Enabled => FirstGems > 0;

        ShuffleRefreshTuning(int[] written) => _written = written;

        static int[] Written(ShuffleRampTuning.Figure[] table)
        {
            var written = new int[table.Length];
            for (int i = 0; i < table.Length; i++) written[i] = table[i].Shipped;
            return written;
        }

        /// <summary>
        /// The figures a document wrote, in <see cref="Figures"/> order, or null with a fault when
        /// one is outside its range. <see cref="Shipped"/> itself when every figure is the shipped
        /// one, so a published copy of the build's figures is the build's own object.
        /// </summary>
        public static ShuffleRefreshTuning FromWritten(int[] written, out string fault)
        {
            if (!ShuffleTuning.InRange("refresh", Table, written, f => (f.Key, f.Shipped, f.Least, f.Most), out bool shipped, out fault))
                return null;

            return shipped ? Shipped : new ShuffleRefreshTuning((int[])written.Clone());
        }

        /// <summary>The figures as a document writes them, in <see cref="Figures"/> order.</summary>
        public int[] Written() => (int[])_written.Clone();

        /// <summary>
        /// What the next refresh costs after <paramref name="taken"/> on this run: the first price,
        /// multiplied by the factor once per refresh already bought, saturating at
        /// <see cref="ShuffleRefresh.MostGems"/>; nought when no refresh is sold. Integer and
        /// early-returning, the shape of <c>ContinueTable.PriceFor</c>: a count nothing bounds
        /// can neither overflow nor spin.
        /// </summary>
        public long PriceFor(int taken)
        {
            if (!Enabled) return 0L;

            long price = FirstGems;
            long factor = FactorHundredths;

            for (int i = 0; i < taken; i++)
            {
                long next = price * factor / 100L;
                if (next >= ShuffleRefresh.MostGems) return ShuffleRefresh.MostGems;

                // A factor that holds the price, or truncates back to it, climbs no further.
                if (next <= price) return price;

                price = next;
            }

            return price;
        }
    }

    /// <summary>
    /// <b>Refreshing a hand</b> (MODES.md 59p, the owner, 2026-10-15): the open hand is swapped for
    /// a fresh one for gems, as often as the player can pay, at a price that doubles with every
    /// refresh the run has bought - 5, 10, 20, 40 as shipped (<see cref="ShuffleRefreshTuning"/>) -
    /// and starts again on the next run.
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
    /// </summary>
    public static class ShuffleRefresh
    {
        /// <summary>
        /// The most one refresh can cost: a saturation of the arithmetic, not a design figure, and
        /// so code rather than the document. At the shipped figures it is reached on the 28th
        /// refresh of one run, after more than a billion gems spent inside it.
        /// </summary>
        public const long MostGems = 1_000_000_000L;

        /// <summary>Carried on the debit for support and analytics, with the level's id after it.</summary>
        public const string SpendReason = "shuffle_refresh:";

        /// <summary>What the next refresh costs after <paramref name="taken"/>, at the build's own figures.</summary>
        public static long PriceFor(int taken) => ShuffleRefreshTuning.Shipped.PriceFor(taken);

        /// <summary>
        /// Takes the gems for a refresh the player asked for and deals the new hand onto the
        /// table. Answers false, charging nothing, when the run has no hand open, refreshes are
        /// withdrawn, the deck has nothing fresh to deal (<see cref="ShuffleRun.CanRefresh"/>), or
        /// the balance cannot meet the price at the instant of the debit.
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
            if (price <= 0L) return false;

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
