using System;
using System.Collections.Generic;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// A set of taken things stored as a floor and the short list taken above it - the shape
    /// that lets a ladder's chests be opened in any order and still be merged across devices.
    ///
    /// <para>
    /// <b>A floor says everything at or under it is taken; the list says which above it are.</b>
    /// Together they are a set that only ever grows: the floor joins by <c>max</c>, the list by
    /// union, and the join of two such pairs is exactly the union of the two sets they mean
    /// (invariant 11b). The list holds nothing at or under the floor - redundant there, and
    /// pruned by the same function on every side - so a player who opens chests from the bottom
    /// up writes a floor and an empty list, which is what every build before this shape wrote.
    /// </para>
    /// <para>
    /// <b>One canonical form, produced by every writer</b> (<see cref="Normal"/>): a load, a
    /// join and a write agree byte for byte, or every write would owe a sync and every sync
    /// would re-learn it (invariant 11f). The keeper milestones (<c>KeeperMilestoneSet</c>), the
    /// season's two tracks and the streak's nights all keep their records through it, each with
    /// its own bounds.
    /// </para>
    /// </summary>
    public static class FloorSet
    {
        /// <summary>
        /// The most entries a list carries. The season's ladder is forty rungs at most and a
        /// streak's backlog past this is taken oldest first (<c>DailyStreak.CollectableAt</c>),
        /// so a longer list is a forged file; the lowest are kept, deterministically, so a join
        /// and a load cannot disagree about which.
        /// </summary>
        public const int MaxTaken = 64;

        /// <summary>
        /// The canonical list: distinct, ascending, every entry above <paramref name="floor"/>
        /// and at or under <paramref name="ceiling"/>, at most <paramref name="cap"/> of them -
        /// the lowest, so a join and a load cannot disagree about which.
        /// </summary>
        public static int[] Normal(int floor, int[] taken, int ceiling, int cap = MaxTaken)
        {
            if (taken == null || taken.Length == 0) return Array.Empty<int>();

            var set = new SortedSet<int>();
            foreach (int entry in taken)
                if (entry > floor && entry <= ceiling) set.Add(entry);

            if (set.Count == 0) return Array.Empty<int>();

            var result = new int[Math.Min(set.Count, cap < 0 ? 0 : cap)];
            int i = 0;
            foreach (int entry in set)
            {
                if (i == result.Length) break;
                result[i++] = entry;
            }
            return result;
        }

        /// <summary>The canonical union of two lists over one floor and one ceiling.</summary>
        public static int[] Union(int floor, int[] a, int[] b, int ceiling, int cap = MaxTaken)
        {
            int la = a?.Length ?? 0, lb = b?.Length ?? 0;
            if (la == 0) return Normal(floor, b, ceiling, cap);
            if (lb == 0) return Normal(floor, a, ceiling, cap);

            var both = new int[la + lb];
            Array.Copy(a, 0, both, 0, la);
            Array.Copy(b, 0, both, la, lb);
            return Normal(floor, both, ceiling, cap);
        }

        /// <summary>The canonical list with one more entry in it.</summary>
        public static int[] With(int floor, int[] taken, int entry, int ceiling)
            => Union(floor, taken, new[] { entry }, ceiling);

        /// <summary>Whether <paramref name="entry"/> is taken: at or under the floor, or in the list.</summary>
        public static bool Holds(int floor, int[] taken, int entry)
        {
            if (entry <= floor) return true;
            if (taken == null) return false;
            for (int i = 0; i < taken.Length; i++)
                if (taken[i] == entry) return true;
            return false;
        }

        /// <summary>Whether the list has room for an entry it does not already hold.</summary>
        public static bool HasRoom(int[] taken) => (taken?.Length ?? 0) < MaxTaken;

        /// <summary>Whether two lists hold the same entries in the same order.</summary>
        public static bool Same(int[] a, int[] b)
        {
            int la = a?.Length ?? 0, lb = b?.Length ?? 0;
            if (la != lb) return false;
            for (int i = 0; i < la; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
