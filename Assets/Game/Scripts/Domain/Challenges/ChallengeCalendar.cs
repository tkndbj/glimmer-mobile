using System;
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
    /// sequence, and the sequence is arithmetic over the day, the genre and each row's
    /// permanent id.
    /// </para>
    /// <para>
    /// <b>The shape</b> (invariant 56f). Each day every row of a genre is given a rank — a hash
    /// of the genre, the row's id and the day — and the day's sequence is the rows in rank
    /// order: slot nought is the best-ranked row, slot one the next, and so on round the ring.
    /// The row that <em>yesterday's</em> ranking put first is moved to the end of the ring, so
    /// two days open on the same level about once in <c>n²</c> days rather than once in <c>n</c>
    /// — one day in ten thousand at a hundred levels. That is what gives the three things the
    /// owner asked for at once: everybody plays the same levels in the same order, the order
    /// looks random from day to day, and <b>adding a level re-deals nothing</b> — a new row takes
    /// its own rank on each day and the others keep theirs; the only knock-on is the day after
    /// one the new row opens, whose exclusion moves.
    /// </para>
    /// <para>
    /// <b>What it gives up is exactness.</b> A per-day ranking visits every row in about
    /// <c>n ln n</c> days on average rather than exactly <c>n</c>, and the no-repeat rule is a
    /// rate rather than a guarantee, because a rule with no memory can only look one day back.
    /// The first cut walked a shuffled ring instead, which covered every row once per cycle and
    /// never repeated, but re-dealt the whole cycle whenever a row was added; the owner chose
    /// stability on 2026-09-23. <c>content.py</c> prints the expected days to see every level.
    /// </para>
    /// <para>
    /// <b>The hash is arithmetic the server could repeat</b> — FNV-1a over a spelt subject, all
    /// 32-bit (invariant 9c's shape) — though no server does: a level is content the device
    /// holds, and what the server prices is the count of wins, never which board was won.
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

            var ring = Ring(rows, genre, day);
            return ring[((slot % n) + n) % n];
        }

        /// <summary>Every row of a genre in the order the day deals them, for a gate to walk.</summary>
        public static List<ChallengeDefinition> Sequence(ChallengeTable table, ChallengeGenre genre, int day)
        {
            var list = new List<ChallengeDefinition>();
            if (table == null) return list;

            var rows = table.RowsOf(genre);
            if (rows.Count == 0) return list;
            if (rows.Count == 1) { list.Add(rows[0]); return list; }

            list.AddRange(Ring(rows, genre, day));
            return list;
        }

        /// <summary>
        /// The day's ring: the rows in rank order, with yesterday's opener moved to the end.
        ///
        /// Yesterday's opener is its <em>raw</em> best-ranked row rather than its dealt opener,
        /// so the question stops after one day back instead of recursing to the beginning of
        /// time. The dealt opener differs from the raw one only when yesterday itself had to
        /// move a row, and in that one case the exclusion misses — a repeat about one day in
        /// <c>n²</c> rather than one in <c>n</c>, which is the price of a rule with no memory.
        /// </summary>
        static ChallengeDefinition[] Ring(IReadOnlyList<ChallengeDefinition> rows, ChallengeGenre genre, int day)
        {
            var ranked = Ranked(rows, genre, day);
            var yesterday = Ranked(rows, genre, day - 1)[0];

            int n = ranked.Length;
            var ring = new ChallengeDefinition[n];
            int at = 0;
            for (int i = 0; i < n; i++)
                if (!ReferenceEquals(ranked[i], yesterday)) ring[at++] = ranked[i];
            ring[n - 1] = yesterday;

            return ring;
        }

        /// <summary>
        /// The rows of a genre in rank order for a day. A tie on the hash — one in four
        /// billion — breaks on the id, so the order is total and the same on every device.
        ///
        /// <b>The day goes first in the subject and the hash is finalised</b>, and both are
        /// load-bearing: FNV-1a alone folds each character into the low bits and leaves the high
        /// bits — the ones a comparison is decided by — nearly where the previous character put
        /// them, so with the day appended last, consecutive days ranked the same row first four
        /// days in five. Mixed through <see cref="Mix"/>, the rank is a fresh draw each day.
        /// </summary>
        internal static ChallengeDefinition[] Ranked(IReadOnlyList<ChallengeDefinition> rows, ChallengeGenre genre, int day)
        {
            int n = rows.Count;
            var keys = new uint[n];
            var order = new ChallengeDefinition[n];
            string prefix = "chal:" + ChallengeGenres.NameOf(genre) + ":" + day + ":";

            for (int i = 0; i < n; i++)
            {
                order[i] = rows[i];
                keys[i] = Mix(Fnv1a(prefix + rows[i].Id));
            }

            Array.Sort(order, (a, b) =>
            {
                uint ka = keys[IndexOf(rows, a)], kb = keys[IndexOf(rows, b)];
                if (ka != kb) return ka < kb ? -1 : 1;
                return string.CompareOrdinal(a.Id, b.Id);
            });

            return order;
        }

        static int IndexOf(IReadOnlyList<ChallengeDefinition> rows, ChallengeDefinition row)
        {
            for (int i = 0; i < rows.Count; i++)
                if (ReferenceEquals(rows[i], row)) return i;
            return -1;
        }

        /// <summary>
        /// A 32-bit finaliser (MurmurHash3's <c>fmix32</c>): every input bit reaches every output
        /// bit, which FNV-1a on its own does not promise for the last few characters.
        /// </summary>
        internal static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return h;
        }

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
