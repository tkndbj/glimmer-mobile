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
        /// <summary>
        /// The most lesson ids a save may carry on the wire.
        ///
        /// <para>
        /// <b>Matches the bound in <c>firestore.rules</c>, and has to.</b> <c>hasOnly</c> is an
        /// allow-list over the whole document, so a list one entry longer than the rules permit
        /// does not lose that entry — it loses <em>every</em> save write for that account, for
        /// ever, with nothing on any screen saying so (invariant 12a). That is exactly what
        /// happened on 2026-09-16: the ledger keeps every id it has ever seen, including the
        /// lessons of every withdrawn mode, so the longest-played account crossed the old bound
        /// of 64 first, and from that write on every push was refused by the security rules and
        /// every account switch away from it failed to secure the grove. The bound is
        /// generous against anything the game can teach, the writer prunes to it rather than
        /// trusting it, and <c>CloudWireTests</c> holds this constant to the rules file.
        /// </para>
        /// </summary>
        public const int MaxIds = 256;

        static readonly HashSet<string> _seen = new HashSet<string>(System.StringComparer.Ordinal);

        static HashSet<string> _live, _retired;

        static HashSet<string> Live => _live ??= IdsOf(Mechanic.All);
        static HashSet<string> Retired => _retired ??= IdsOf(Mechanic.Retired);

        static HashSet<string> IdsOf(Mechanic[] mechanics)
        {
            var ids = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var mechanic in mechanics)
                if (mechanic.IsValid) ids.Add(mechanic.Id);
            return ids;
        }

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
            dto.tipsSeen = Bounded(ids);
        }

        /// <summary>
        /// The union of two devices' lessons. Seeing something cannot be undone.
        ///
        /// <para>
        /// Bounded here as well as in <see cref="WriteInto"/>, and it is not belt and braces:
        /// the merged file the sync pushes is built by <c>SaveMerge.Join</c> straight from this
        /// answer and never passes through the ledger, so a cap applied only on the ledger's
        /// own write would leave the one document that reaches the server unbounded.
        /// </para>
        /// </summary>
        internal static string[] Join(string[] mine, string[] other)
        {
            var union = new SortedSet<string>(System.StringComparer.Ordinal);

            if (mine != null)
                foreach (var id in mine) if (!string.IsNullOrEmpty(id)) union.Add(id);

            if (other != null)
                foreach (var id in other) if (!string.IsNullOrEmpty(id)) union.Add(id);

            return Bounded(new List<string>(union));
        }

        /// <summary>
        /// At most <see cref="MaxIds"/> of the ids given, which must arrive sorted.
        ///
        /// <para>
        /// Under the cap nothing is touched, so no player who has ever existed sees a change
        /// from this. Over it, ids go in the order that costs the least: every retired lesson
        /// first, because no build will teach one again; then every id this build does not
        /// recognise, which is a withdrawn mode's lesson or a newer build's — the second is
        /// re-taught once on that build, and that is the whole price of a cap. A live lesson
        /// is never dropped, and <c>TipLedgerTests</c> proves the live set fits with room.
        /// </para>
        /// </summary>
        internal static string[] Bounded(List<string> sorted)
        {
            if (sorted.Count <= MaxIds) return sorted.ToArray();

            var kept = new List<string>(sorted);
            kept.RemoveAll(id => Retired.Contains(id));

            if (kept.Count > MaxIds) kept.RemoveAll(id => !Live.Contains(id));

            // Unreachable while the live set fits under the cap, which the test holds; kept so
            // the promise this method makes to the security rules cannot depend on a test.
            if (kept.Count > MaxIds) kept.RemoveRange(MaxIds, kept.Count - MaxIds);

            return kept.ToArray();
        }
    }
}
