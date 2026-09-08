using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Utilities
{
    /// <summary>
    /// How many of each utility a player has ever been given, and how many they have ever used.
    ///
    /// <para>
    /// <b>Two counters per id and never one</b>, which is invariant 11b for the fourth time in
    /// this file after hearts, hints and grove decor. A count of utilities <em>remaining</em>
    /// cannot be merged: two devices showing 3 and 1 are equally consistent with "one opened a
    /// chest" and "one spent two on a siege", so every rule over the pair is wrong somewhere —
    /// taking the larger hands back what was spent, taking the smaller destroys what was earned.
    /// Both halves of a double-entry ledger only ever rise, so the join is a per-id <c>max</c> on
    /// each and the larger value is always the one that knows more.
    /// </para>
    /// <para>
    /// <b>Why not <see cref="RegenLedger"/>, which is the same idea.</b> That type is three
    /// counters and a clock answering "how many are there now" for <em>one</em> pool; this is two
    /// counters per id for an open-ended set of them, with no clock at all because nothing here
    /// comes back on its own. Grove decor made the same choice for the same reason and
    /// <c>GroveStock</c> says so: what the three share is the <em>idea</em>, and the idea is
    /// written down in invariant 11b rather than in a base class. What they must not share is a
    /// second copy of the arithmetic, which is why the walk, the clamp and the join live here
    /// once and <see cref="UtilityLedger"/> holds no counting of its own.
    /// </para>
    /// <para>
    /// <b>It holds no policy.</b> Which utilities exist, what one costs, how many a player may
    /// hold and whether a use is legal are all <see cref="UtilityLedger"/>'s, because those need
    /// the catalog and this needs nothing. That is what lets every rule about merging, bounding
    /// and writing this section be proved offline against plain integers.
    /// </para>
    /// <para>
    /// <b>A forged row buys an easier run and never a better one.</b> Utilities are not currency,
    /// so nothing here is adjudicated (invariant 13) — and the reason that is safe rather than
    /// merely cheap is invariant 39: a utility that delivers damage is charged against the graded
    /// count at the most a match could ever have delivered, so no number written here can improve
    /// a star, a credit or a grove's worth on a public board.
    /// </para>
    /// </summary>
    public sealed class UtilityStock
    {
        /// <summary>
        /// The most either counter will ever record, and a permanent <c>const</c> rather than
        /// anything a content push can move.
        ///
        /// <para>
        /// <c>GroveStock.MaxCopies</c>' argument exactly. The clamp is <em>structural</em> — it
        /// exists so a corrupt or hostile file cannot put a number here that overflows the
        /// subtraction or the wire's size guard — and a structural clamp may never be a published
        /// number, because lowering a published one would cut a counter the merge proof requires
        /// to be monotonic. How many a player may <em>hold</em> is an economy question and lives
        /// on <see cref="UtilityItem.MaxHeld"/>.
        /// </para>
        /// </summary>
        public const int MaxHeld = 9_999;

        /// <summary>
        /// Most distinct ids this will record, which is what bounds the wire.
        ///
        /// It matches the <c>utilityStock</c> size guard in <c>firestore.rules</c>, and it has to:
        /// a save the client is willing to write and the rules refuse loses the whole document
        /// write rather than the extra rows (invariant 12a).
        /// </summary>
        public const int MaxIds = 64;

        struct Row
        {
            public int Earned;
            public int Spent;
        }

        readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>(StringComparer.Ordinal);

        /// <summary>How many of this id were ever handed over. Zero for anything never granted.</summary>
        public int EarnedOf(string id)
            => !string.IsNullOrEmpty(id) && _rows.TryGetValue(id, out var row) ? row.Earned : 0;

        /// <summary>How many of this id were ever used.</summary>
        public int SpentOf(string id)
            => !string.IsNullOrEmpty(id) && _rows.TryGetValue(id, out var row) ? row.Spent : 0;

        /// <summary>
        /// How many are in hand: earned minus spent, never below nought.
        ///
        /// <b>The clamp is not decoration.</b> Two devices can each spend the last one before
        /// either has synced, so the merged file can legitimately hold more spent than earned for
        /// as long as it takes the next grant to land — exactly the state <c>GroveStock</c>
        /// documents for a fence placed twice. Answering "none left" costs nothing; reaching back
        /// into either counter to balance the identity would break the monotonicity the join
        /// rests on.
        /// </summary>
        public int Held(string id)
        {
            int left = EarnedOf(id) - SpentOf(id);
            return left < 0 ? 0 : left;
        }

        /// <summary>
        /// Records some granted, and answers how many were actually added.
        ///
        /// <b>No ceiling is applied here</b>, deliberately: the published ceiling is a decision
        /// taken at the moment of a grant and belongs to <see cref="UtilityLedger"/>, which has
        /// the catalog. All that happens here is the structural clamp, so a caller that has
        /// already charged a player for something can never be refused after the fact — the trap
        /// <c>GroveStock.Add</c> names.
        /// </summary>
        public int Earn(string id, int count)
        {
            if (string.IsNullOrEmpty(id) || count <= 0) return 0;
            if (!_rows.ContainsKey(id) && _rows.Count >= MaxIds) return 0;

            _rows.TryGetValue(id, out var row);

            int had = row.Earned;
            row.Earned = Clamp((long)had + count);
            _rows[id] = row;

            return row.Earned - had;
        }

        /// <summary>
        /// Records some used, and answers how many were actually taken.
        ///
        /// Never takes more than are in hand, so <see cref="Held"/> can only be driven to nought
        /// and never through it by an honest caller.
        /// </summary>
        public int Use(string id, int count)
        {
            if (string.IsNullOrEmpty(id) || count <= 0) return 0;

            int held = Held(id);
            if (held <= 0) return 0;
            if (count > held) count = held;

            _rows.TryGetValue(id, out var row);
            row.Spent = Clamp((long)row.Spent + count);
            _rows[id] = row;

            return count;
        }

        /// <summary>Forgets everything, as a fresh install would.</summary>
        public void Clear() => _rows.Clear();

        // --------------------------------------------------------- file bridge
        /// <summary>
        /// Reads the rows of a save file, ignoring anything malformed rather than throwing.
        ///
        /// A duplicated id takes the larger of each counter, which is the rule <see cref="Join"/>
        /// uses — so a file that should have been impossible (invariant 11a) is read the way two
        /// devices holding it would have been merged, rather than by whichever row came last.
        /// </summary>
        public void LoadFrom(UtilityStockDto[] rows)
        {
            _rows.Clear();
            Absorb(rows);
        }

        /// <summary>
        /// The rows to write, sorted by id.
        ///
        /// Not tidiness: <c>SaveChecksum</c> hashes the serialised file and <c>SaveDelta</c> walks
        /// these in order, so dictionary order would make an unchanged save look changed on every
        /// launch and push a write for nothing, for ever.
        ///
        /// <b>A row whose counters are both nought is dropped</b>, so "granted nothing" and
        /// "written before utilities existed" stay the same fact and no sentinel is needed — the
        /// property that makes every other id-keyed section here mergeable.
        /// </summary>
        public UtilityStockDto[] Write()
        {
            if (_rows.Count == 0) return Array.Empty<UtilityStockDto>();

            var ids = new List<string>(_rows.Count);
            foreach (var pair in _rows)
                if (pair.Value.Earned > 0 || pair.Value.Spent > 0) ids.Add(pair.Key);

            if (ids.Count == 0) return Array.Empty<UtilityStockDto>();

            ids.Sort(StringComparer.Ordinal);

            var rows = new UtilityStockDto[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                var row = _rows[ids[i]];
                rows[i] = new UtilityStockDto { id = ids[i], earned = row.Earned, spent = row.Spent };
            }

            return rows;
        }

        /// <summary>
        /// The two devices' ledgers, joined: a per-id maximum on each counter.
        ///
        /// <para>
        /// Idempotent, commutative and associative, which is the whole reason this section is
        /// allowed to hold numbers at all. Both counters only ever rise, so the larger side is
        /// always the one that has heard about more: more earned means a chest the other missed,
        /// more spent means a siege the other missed, and there is nothing here for a stale
        /// snapshot to overwrite.
        /// </para>
        /// <para>
        /// <b>What it is not is exact, and the inexactness is deliberate and in the player's
        /// favour.</b> Two devices that both go offline holding five and spend two and three
        /// merge to <c>spent = 3</c>, not five — so two of those uses were free. The alternative
        /// is adding the two, and adding is not idempotent: a device that re-uploads the same
        /// save, or a sync retried after a dropped reply, would charge those spends again, which
        /// is the failure mode that actually loses a player something they paid gems for.
        /// <see cref="RegenLedger"/> makes the same trade for hearts and hints and has since v8;
        /// this is that decision inherited rather than re-taken, and the reason it is safe is
        /// invariant 39 — a forgiven use buys an easier run and never a better one.
        /// </para>
        /// <para>
        /// No early return for an empty side, deliberately — the trap <c>GroveStock.Join</c>
        /// documents. Handing one array straight back would skip the sort, so an unsorted file
        /// joined against nothing would come out still unsorted and <c>SaveDelta</c> would read
        /// every launch as changed.
        /// </para>
        /// <para>
        /// Unknown ids are kept, exactly as <c>tipsSeen</c> and <c>companionsOwned</c> keep theirs:
        /// a utility granted on a newer build must not be confiscated by a trip through an older
        /// one — and here that would be taking back something a player may have paid gems for.
        /// </para>
        /// </summary>
        public static UtilityStockDto[] Join(UtilityStockDto[] mine, UtilityStockDto[] other)
        {
            var stock = new UtilityStock();
            stock.Absorb(mine);
            stock.Absorb(other);

            return stock.Write();
        }

        /// <summary>
        /// Folds rows into what is already held, keeping the larger of each counter.
        ///
        /// The one place a row is judged, so <see cref="LoadFrom"/> and <see cref="Join"/> cannot
        /// come to disagree about what a malformed row means — invariant 5b's rule in a file with
        /// no reason to have two copies of it.
        /// </summary>
        void Absorb(UtilityStockDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.id)) continue;
                if (row.earned <= 0 && row.spent <= 0) continue;
                if (!_rows.ContainsKey(row.id) && _rows.Count >= MaxIds) continue;

                _rows.TryGetValue(row.id, out var held);

                int earned = Clamp(row.earned);
                int spent = Clamp(row.spent);

                if (earned > held.Earned) held.Earned = earned;
                if (spent > held.Spent) held.Spent = spent;

                _rows[row.id] = held;
            }
        }

        static int Clamp(long value)
            => value < 0L ? 0 : value > MaxHeld ? MaxHeld : (int)value;
    }
}
