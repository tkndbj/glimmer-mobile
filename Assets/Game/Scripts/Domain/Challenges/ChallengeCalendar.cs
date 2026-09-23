using System.Collections.Generic;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Which level a genre deals on a given day, and in what order.
    ///
    /// <para>
    /// <b>Rotation is global and pure</b> (invariant 45b's rule, said of a ladder of levels):
    /// nothing is stored, every device on the same day and the same file deals the same
    /// sequence, and a re-authored slate re-deals every day after it — accepted rather than
    /// refused, because a slate that cannot change is a slate nobody will tune.
    /// </para>
    /// <para>
    /// <b>The shape</b> (invariant 56f). A genre with <c>n</c> rows is walked in <em>cycles</em>
    /// of <c>n</c> days. Each cycle shuffles the rows once, seeded by the genre and the cycle
    /// number, and day <c>d</c> of the cycle starts at position <c>d</c> of that shuffle: slot
    /// nought of the day is that row, slot one the row after it, and so on round the ring. So
    /// the first level of every day is different from the day before's, every row is the first
    /// level exactly once per cycle, and the levels a player who wins <c>k</c> times sees are
    /// the <c>k</c> that follow — the same <c>k</c> for everybody, which is what makes it a
    /// fair challenge. When a day asks for more slots than there are rows the ring wraps, which
    /// with one row is that row every time and with a hundred is nothing anybody reaches.
    /// </para>
    /// <para>
    /// <b>The shuffle is arithmetic the server could repeat</b> — FNV-1a over a spelt subject
    /// into xorshift32 and a Fisher-Yates walk, all 32-bit (invariant 9c's shape) — though no
    /// server does: a level is content the device holds, and what the server prices is the
    /// count of wins, never which board was won.
    /// </para>
    /// <para>
    /// <b>UTC</b>, off the trusted clock and the same day key every adjudicated day in this
    /// game uses (<see cref="DailyRules.DayKeyFor"/>), so two phones in two time zones are on
    /// the same level and the day a claim names is the day the server counts.
    /// </para>
    /// </summary>
    public static class ChallengeCalendar
    {
        /// <summary>Today's key, off the trusted clock.</summary>
        public static int Today() => DailyRules.DayKeyFor(GameClock.NowUnix());

        /// <summary>The day key an instant falls on.</summary>
        public static int DayOf(long unix) => DailyRules.DayKeyFor(unix);

        /// <summary>
        /// The row a genre deals as the <paramref name="slot"/>-th level of <paramref name="day"/>,
        /// or null when the genre has no rows. Slot nought is the first level of the day.
        /// </summary>
        public static ChallengeDefinition Slot(ChallengeTable table, ChallengeGenre genre, int day, int slot)
        {
            if (table == null) return null;

            var rows = table.RowsOf(genre);
            int n = rows.Count;
            if (n == 0) return null;
            if (n == 1) return rows[0];

            int position = Position(genre, day, n);
            int at = Mod(position + Mod(slot, n), n);
            return rows[Order(genre, day, n)[at]];
        }

        /// <summary>Where in the cycle's shuffle a day starts.</summary>
        static int Position(ChallengeGenre genre, int day, int n) => Mod(day, n);

        /// <summary>Which cycle a day belongs to. Negative days floor rather than truncate.</summary>
        static int Cycle(int day, int n) => day >= 0 ? day / n : -((-day + n - 1) / n);

        /// <summary>
        /// The cycle's shuffle: a permutation of <c>0..n-1</c>, the same on every device.
        ///
        /// Fisher-Yates over a xorshift stream seeded from <c>chal:{genre}:{cycle}</c>, so two
        /// genres with the same count still shuffle differently and cycle <c>c+1</c> is not a
        /// rotation of cycle <c>c</c>.
        /// </summary>
        internal static int[] Order(ChallengeGenre genre, int day, int n)
        {
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            var rng = new ChallengeRng(Fnv1a("chal:" + ChallengeGenres.NameOf(genre) + ":" + Cycle(day, n)));
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Below(i + 1);
                int t = order[i];
                order[i] = order[j];
                order[j] = t;
            }

            return order;
        }

        /// <summary>Every row of a genre in the order the day deals them, for a gate to walk.</summary>
        public static List<ChallengeDefinition> Sequence(ChallengeTable table, ChallengeGenre genre, int day)
        {
            var list = new List<ChallengeDefinition>();
            if (table == null) return list;

            int n = table.RowsOf(genre).Count;
            for (int slot = 0; slot < n; slot++) list.Add(Slot(table, genre, day, slot));
            return list;
        }

        static int Mod(int value, int n) => ((value % n) + n) % n;

        /// <summary>32-bit FNV-1a over the UTF-16 code units of a string, the chest seed's arithmetic.</summary>
        internal static uint Fnv1a(string text)
        {
            const uint offset = 2166136261u, prime = 16777619u;
            uint hash = offset;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= prime;
            }
            return hash == 0u ? offset : hash;
        }
    }
}
