using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Homestead
{
    /// <summary>How far along an island is, from bare rock to a place somebody lives.</summary>
    public enum TendedStage
    {
        /// <summary>Nothing placed. Owned land, and that is all.</summary>
        Bare,

        /// <summary>Somebody has started.</summary>
        Started,

        /// <summary>Taking shape.</summary>
        Growing,

        /// <summary>Nearly finished.</summary>
        Lush,

        /// <summary>Every slot filled. The island earns its plaque and its fireflies.</summary>
        Bloomed,
    }

    /// <summary>
    /// The island's own answer to "did I build this", derived from how full it is.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> A full island and an empty one used to be the same
    /// picture with different dots on it, so nothing about the grove ever said <em>you
    /// changed this</em> — and a before-and-after is the entire mechanism the feature runs
    /// on. The island now brightens, warms and finally blooms as it fills.
    /// </para>
    /// <para>
    /// <b>It stores nothing.</b> Fill is a pure function of the arrangement already in the
    /// save file, which is invariant 14's preferred shape: no counter to merge, no floor to
    /// keep monotonic, no migration, and a retune that adds a slot to an island simply moves
    /// everybody's stage down a notch and back up as they fill it.
    /// </para>
    /// <para>
    /// The thresholds are deliberately generous at the bottom and exact at the top. The first
    /// thing a player ever places should visibly change the island — that is the moment the
    /// habit forms — while <see cref="TendedStage.Bloomed"/> has to mean <em>finished</em>, so
    /// it is the only one that asks for every slot.
    /// </para>
    /// </summary>
    public static class GroveTending
    {
        public const float GrowingAt = .40f;
        public const float LushAt = .75f;

        public static TendedStage Of(float fill)
        {
            if (fill >= 1f) return TendedStage.Bloomed;
            if (fill >= LushAt) return TendedStage.Lush;
            if (fill >= GrowingAt) return TendedStage.Growing;
            return fill > 0f ? TendedStage.Started : TendedStage.Bare;
        }

        public static TendedStage Of(HomesteadPlot plot) => Of(HomesteadLayout.FillOf(plot));
    }

    /// <summary>What stands in one slot, and when the player decided it.</summary>
    public readonly struct Placement
    {
        /// <summary>The piece's permanent id, or empty for a slot deliberately cleared.</summary>
        public readonly string PieceId;

        /// <summary>When this slot was last set, as a Unix timestamp. 0 is unreachable for a real choice.</summary>
        public readonly long SetUnix;

        public Placement(string pieceId, long setUnix)
        {
            PieceId = pieceId ?? string.Empty;
            SetUnix = setUnix < 0 ? 0 : setUnix;
        }

        public bool IsOccupied => !string.IsNullOrEmpty(PieceId);
    }

    /// <summary>
    /// Where the player has put things: a map from slot id to what stands there.
    ///
    /// <para>
    /// <b>This is the one part of the grove that is stored, and the only part merged by
    /// recency.</b> Everything else here is derived — the land from chapters finished, the
    /// residents from glades cleared — or a union-joined entitlement set. An arrangement is
    /// neither: it is an <em>instruction</em>, like the keeper's name and their worn
    /// companion, and the most recent one is the one the player meant. Invariant 11c governs
    /// it, and this is the third thing in the save file under that rule.
    /// </para>
    /// <para>
    /// <b>Two mistakes 11c exists to prevent, both of which cost a year of lost names.</b>
    /// The stamp travels with the value rather than being read off the file's
    /// <c>updatedUnix</c> — which <c>SaveService.Snapshot</c> writes as <em>now</em> every
    /// time a sync asks for one, so "the newer file wins" would once again mean "this device
    /// wins", and a grove arranged on a phone would be flattened by a tablet that had never
    /// been opened. And an untouched slot <b>stores nothing</b>: a row is written when the
    /// player puts something down or takes it away, never to record that a slot is empty. A
    /// stored default is what makes a device with no opinion indistinguishable from one that
    /// chose, and that is precisely how a fresh install erases a grove.
    /// </para>
    /// <para>
    /// <b>Clearing a slot is a choice and carries a stamp.</b> So "absent" and "emptied" are
    /// different facts, and taking a tree down on one device survives a sync with another
    /// that still has it. That is why <see cref="Placement.PieceId"/> may be empty rather
    /// than the row simply being deleted — a deletion would read as "never touched" and the
    /// stale side would put the tree back.
    /// </para>
    /// <para>
    /// <see cref="Join"/> is a maximum over (has a stamp, then the stamp, then ordinal
    /// order), taken independently per slot — so it is idempotent, commutative and
    /// associative, which is what invariant 11 promises about every merge in this file.
    /// </para>
    /// </summary>
    public static class HomesteadLayout
    {
        /// <summary>
        /// Slot id to what stands in it. A slot with no entry has never been touched, which
        /// is a different fact from one holding an empty <see cref="Placement"/>.
        /// </summary>
        static readonly Dictionary<string, Placement> _placed =
            new Dictionary<string, Placement>(StringComparer.Ordinal);

        /// <summary>Raised when anything moved, so an open screen can redraw.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------- reading
        /// <summary>The piece id in a slot, or empty. Unknown slots answer empty.</summary>
        public static string At(string slotId)
            => !string.IsNullOrEmpty(slotId) && _placed.TryGetValue(slotId, out var p)
                ? p.PieceId
                : string.Empty;

        public static bool IsOccupied(string slotId) => !string.IsNullOrEmpty(At(slotId));

        /// <summary>
        /// The piece standing in a slot, resolved against a catalog.
        ///
        /// An id the catalog does not know answers invalid rather than throwing, and the row
        /// is left alone: a save written by a newer build may name a piece this one has never
        /// heard of, and the right response is to draw an empty slot for now, not to erase
        /// somebody's arrangement on a rollback.
        /// </summary>
        public static HomesteadPiece PieceAt(HomesteadCatalog catalog, string slotId)
            => catalog == null ? default : catalog.Find(At(slotId));

        /// <summary>How many slots of this catalog currently hold something.</summary>
        public static int OccupiedCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var plot in catalog.Plots)
                foreach (var slot in plot.Slots)
                    if (slot.IsValid && IsOccupied(slot.Id)) count++;

            return count;
        }

        /// <summary>
        /// How many of one island's placeable slots hold something.
        ///
        /// The hearth is excluded, because it is drawn from what the player owns rather than
        /// placed — counting it would make an island that can never read as finished.
        /// </summary>
        public static int OccupiedCount(HomesteadPlot plot)
        {
            if (plot == null) return 0;

            int count = 0;
            foreach (var slot in plot.Slots)
                if (slot.IsValid && !slot.IsHearth && IsOccupied(slot.Id)) count++;

            return count;
        }

        /// <summary>
        /// How full an island is, 0 to 1. An island with nowhere to place answers 0.
        ///
        /// <b>This is the whole "I built this" signal</b> — see <see cref="GroveTending"/>.
        /// </summary>
        public static float FillOf(HomesteadPlot plot)
        {
            if (plot == null) return 0f;

            int total = plot.PlaceableCount;
            return total <= 0 ? 0f : (float)OccupiedCount(plot) / total;
        }

        /// <summary>
        /// How many distinct pieces the grove is currently showing.
        ///
        /// The number worth putting on the screen: a grove of forty benches is one idea, and
        /// a player comparing theirs to somebody else's is comparing variety, not slot count.
        /// </summary>
        public static int VarietyCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var plot in catalog.Plots)
                foreach (var slot in plot.Slots)
                {
                    if (!slot.IsValid) continue;
                    string id = At(slot.Id);
                    if (!string.IsNullOrEmpty(id)) seen.Add(id);
                }

            return seen.Count;
        }

        /// <summary>
        /// Every distinct piece id standing somewhere in the grove.
        ///
        /// What the grove screen's art scope is built from, which is why it is a set rather
        /// than a list: a fence placed in nine slots is one texture. Slots the catalog does
        /// not know are included — the address simply misses, and dropping them here would
        /// mean a rollback silently stopped loading art it might still have.
        /// </summary>
        public static IReadOnlyCollection<string> PlacedIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var placed in _placed.Values)
                if (placed.IsOccupied) ids.Add(placed.PieceId);

            return ids;
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Puts a piece in a slot, or clears it when <paramref name="pieceId"/> is empty.
        ///
        /// <para>
        /// Returns false when nothing changed, which is what stops a repaint and a save from
        /// firing on a tap that re-chose what was already there. It deliberately does
        /// <em>not</em> check that the piece is held: the caller is a picker that only ever
        /// lists held pieces, and a second copy of the unlock rule here would be a second
        /// answer for a retune to put out of step with the first — invariant 15a's argument,
        /// which this file is not going to lose by re-deriving it. A forged row costs a
        /// picture and is bounded exactly as <see cref="HomesteadLedger"/> describes.
        /// </para>
        /// </summary>
        public static bool Place(string slotId, string pieceId)
        {
            if (string.IsNullOrEmpty(slotId)) return false;

            string wanted = pieceId ?? string.Empty;
            if (string.Equals(At(slotId), wanted, StringComparison.Ordinal)) return false;

            _placed[slotId] = new Placement(wanted, GameClock.NowUnix());

            Telemetry.Track("grove_placed", "slot", slotId,
                            "piece", string.IsNullOrEmpty(wanted) ? "(cleared)" : wanted);

            SaveService.Save();
            Raise();
            return true;
        }

        /// <summary>Takes whatever is in a slot away. A choice, and stamped as one.</summary>
        public static bool Clear(string slotId) => Place(slotId, string.Empty);

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _placed.Clear();

            var rows = dto?.homesteadPlaced;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.slot)) continue;

                    // A duplicated slot is a malformed file rather than a second placement —
                    // invariant 11a's whole point. The later row wins, which is arbitrary but
                    // stable, and the row that lost is dropped rather than kept as a second
                    // answer.
                    _placed[row.slot] = new Placement(row.piece, row.setUnix);
                }

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.homesteadPlaced = Rows(_placed);
        }

        /// <summary>
        /// The two devices' arrangements, joined slot by slot.
        ///
        /// <para>
        /// No early return for an empty side, for <c>EventCollection.Join</c>'s reason:
        /// handing one array straight back would skip the sort and the deduplication, so a
        /// malformed file joined against nothing would come out still malformed — and
        /// <see cref="SaveDelta"/> walks these in order, so it would read as changed on every
        /// sync for ever.
        /// </para>
        /// <para>
        /// Rows naming slots this build does not know are carried through untouched. A grove
        /// arranged on a device a content drop ahead must not be flattened by a trip through
        /// an older one; a row costs two short strings and a long.
        /// </para>
        /// </summary>
        public static HomesteadPlacementDto[] Join(HomesteadPlacementDto[] mine,
                                                   HomesteadPlacementDto[] other)
        {
            var byId = new Dictionary<string, Placement>(StringComparer.Ordinal);

            Absorb(byId, mine);
            Absorb(byId, other);

            return Rows(byId);
        }

        static void Absorb(Dictionary<string, Placement> into, HomesteadPlacementDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.slot)) continue;

                var incoming = new Placement(row.piece, row.setUnix);

                into[row.slot] = into.TryGetValue(row.slot, out var held)
                    ? Later(held, incoming)
                    : incoming;
            }
        }

        /// <summary>
        /// The winner between two versions of one slot: the later decision.
        ///
        /// <para>
        /// A tie is broken by ordinal order on the piece id, which is not a better answer
        /// than the alternative — only a <em>stable</em> one, and stability is the whole
        /// point. An arbitrary choice that depended on argument order would leave two devices
        /// pushing over each other for ever, which is the property <c>SaveMerge.Chosen</c>
        /// documents and the reason this join can be called in any order.
        /// </para>
        /// </summary>
        static Placement Later(Placement a, Placement b)
        {
            if (a.SetUnix != b.SetUnix) return a.SetUnix > b.SetUnix ? a : b;

            return string.CompareOrdinal(a.PieceId, b.PieceId) >= 0 ? a : b;
        }

        /// <summary>
        /// The map as sorted rows.
        ///
        /// Sorted for <c>CompanionLedger.Sorted</c>'s reason and not for tidiness:
        /// <see cref="SaveChecksum"/> hashes the serialised file and <see cref="SaveDelta"/>
        /// walks these in order, so dictionary order would make an unchanged grove look
        /// changed on every launch and push a write for nothing.
        /// </summary>
        static HomesteadPlacementDto[] Rows(Dictionary<string, Placement> map)
        {
            if (map.Count == 0) return Array.Empty<HomesteadPlacementDto>();

            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);

            var rows = new HomesteadPlacementDto[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                var placed = map[keys[i]];
                rows[i] = new HomesteadPlacementDto
                {
                    slot = keys[i],
                    piece = placed.PieceId,
                    setUnix = placed.SetUnix,
                };
            }

            return rows;
        }

        /// <summary>Test seam: an empty grove, as a fresh install would have.</summary>
        internal static void ResetForTests() => _placed.Clear();
    }
}
