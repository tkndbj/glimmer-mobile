using System.Collections.Generic;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Which lessons this player has already been shown.
    ///
    /// <para>
    /// A set of permanent mechanic ids, and the only stored thing in the game that is
    /// purely about what someone has <em>seen</em> rather than what they have done.
    /// That makes it the easiest kind of state to merge: seeing something is
    /// irreversible, so two devices can only ever have seen more between them, and the
    /// join is a union — idempotent, commutative and associative without trying.
    /// </para>
    /// <para>
    /// Unknown ids are kept rather than dropped. A player who meets a mechanic on a
    /// newer build and then opens an older one must not be taught it again when they
    /// come back, and an id this build does not recognise costs one short string.
    /// </para>
    /// </summary>
    public static class TipLedger
    {
        static readonly HashSet<string> _seen = new HashSet<string>(System.StringComparer.Ordinal);

        public static bool HasSeen(Mechanic mechanic)
            => mechanic.IsValid && _seen.Contains(mechanic.Id);

        /// <summary>
        /// Records a lesson as taught. Returns false when it already was, so a caller
        /// can tell a first showing from a repeat without asking twice.
        /// </summary>
        public static bool MarkSeen(Mechanic mechanic)
        {
            if (!mechanic.IsValid || !_seen.Add(mechanic.Id)) return false;

            SaveService.Save();
            return true;
        }

        public static int Count => _seen.Count;

        /// <summary>Forgets everything, so the next run teaches from scratch. Dev only.</summary>
        public static void ForgetAll()
        {
            if (_seen.Count == 0) return;

            _seen.Clear();
            SaveService.Save();
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _seen.Clear();

            var ids = dto?.tipsSeen;
            if (ids == null) return;

            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) _seen.Add(id);
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            // Sorted, so two saves holding the same lessons produce the same bytes and
            // the delta check does not report a change that is only ordering.
            var ids = new List<string>(_seen);
            ids.Sort(System.StringComparer.Ordinal);
            dto.tipsSeen = ids.ToArray();
        }

        /// <summary>The union of two devices' lessons. Seeing something cannot be undone.</summary>
        internal static string[] Join(string[] mine, string[] other)
        {
            var union = new SortedSet<string>(System.StringComparer.Ordinal);

            if (mine != null)
                foreach (var id in mine) if (!string.IsNullOrEmpty(id)) union.Add(id);

            if (other != null)
                foreach (var id in other) if (!string.IsNullOrEmpty(id)) union.Add(id);

            var result = new string[union.Count];
            union.CopyTo(result);
            return result;
        }
    }
}
