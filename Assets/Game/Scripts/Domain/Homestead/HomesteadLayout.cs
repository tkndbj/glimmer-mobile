using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Homestead
{
    /// <summary>How far along a stretch of the floor is, from bare ground to somewhere lived-in.</summary>
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

        /// <summary>Every tile filled. The region earns its plaque and its fireflies.</summary>
        Bloomed,
    }

    /// <summary>
    /// A region's own answer to "did I build this", derived from how full it is.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> Full ground and empty ground would otherwise be the
    /// same picture with different things on it, so nothing about the grove would ever say
    /// <em>you changed this</em> — and a before-and-after is the entire mechanism the feature
    /// runs on. A region brightens, warms and finally blooms as it fills.
    /// </para>
    /// <para>
    /// <b>It stores nothing.</b> Fill is a pure function of the arrangement already in the
    /// save file, which is invariant 14's preferred shape: no counter to merge, no floor to
    /// keep monotonic, no migration, and a retune that widens a region simply moves everybody's
    /// stage down a notch and back up as they fill it.
    /// </para>
    /// <para>
    /// The thresholds are deliberately generous at the bottom and exact at the top. The first
    /// thing a player ever places should visibly change the ground — that is the moment the
    /// habit forms — while <see cref="TendedStage.Bloomed"/> has to mean <em>finished</em>, so
    /// it is the only one that asks for every tile.
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

        public static TendedStage Of(GroveFloor floor, GroveRegion region)
            => Of(HomesteadLayout.FillOf(floor, region));
    }

    /// <summary>What stands in one slot, and when the player decided it.</summary>
    public readonly struct Placement
    {
        /// <summary>The piece's permanent id, or empty for a slot deliberately cleared.</summary>
        public readonly string PieceId;

        /// <summary>When this slot was last set, as a Unix timestamp. 0 is unreachable for a real choice.</summary>
        public readonly long SetUnix;

        /// <summary>
        /// Drawn mirrored, which is the only facing an isometric prop can have.
        ///
        /// <para>
        /// <b>Why this is a flip and not a rotation.</b> Every piece in the catalog is one
        /// drawing from one fixed camera angle — the seventeen packs the art came from ship no
        /// directional variants at all — so there is no second sprite to rotate to. Turning the
        /// transform would rotate the <em>painting</em> rather than the object: a tree leans
        /// over, and a fence's painted side wall goes on facing the old way while its footprint
        /// runs diagonally across the ground plane. A mirror is the one transform that leaves an
        /// isometric drawing still standing on its own tile, so it is the one offered.
        /// </para>
        /// <para>
        /// It rides the row it belongs to rather than getting a stamp of its own, and that is
        /// what keeps invariant 11c satisfied for free: the facing and the piece are one
        /// decision about one slot, so the stamp that dates the placement dates the facing too.
        /// A cleared slot is never flipped — see the constructor — because a facing on nothing
        /// is a bit the merge would have to break a tie on and no player could ever see.
        /// </para>
        /// </summary>
        public readonly bool Flipped;

        public Placement(string pieceId, long setUnix, bool flipped)
        {
            PieceId = pieceId ?? string.Empty;
            SetUnix = setUnix < 0 ? 0 : setUnix;
            Flipped = flipped && PieceId.Length > 0;
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
        /// Whether what stands in a slot is drawn mirrored. An untouched slot answers false,
        /// which is what a slot showing the starter companion wants and what every row written
        /// before this existed meant.
        /// </summary>
        public static bool FlippedAt(string slotId)
            => !string.IsNullOrEmpty(slotId) && _placed.TryGetValue(slotId, out var p) && p.Flipped;

        /// <summary>
        /// What a tile actually <em>shows</em>: whatever the player put there, or the starter
        /// companion on the one tile that has one and has never been touched.
        ///
        /// <para>
        /// <b>The distinction between stored and shown is the whole point of this method.</b> A
        /// new grove is meant to open with one friend already standing next to the hall, and the
        /// obvious way to do that — write the placement at first launch — is exactly what
        /// invariant 11c forbids. A fresh install would stamp that row with <em>now</em>,
        /// outrank a device where the player had moved or cleared that tile, and put the
        /// companion back; a stored default is indistinguishable from a choice. So nothing is
        /// written, the tile simply draws the starter while it has no row, and clearing it is a
        /// real instruction that does get one. <c>Wallet</c> shows the default keeper name the
        /// same way and for the same reason.
        /// </para>
        /// </summary>
        public static string Shown(HomesteadCatalog catalog, string slotId)
        {
            if (catalog == null) return At(slotId);
            if (_placed.ContainsKey(slotId ?? string.Empty)) return At(slotId);

            return catalog.Floor.StarterPieceOn(slotId);
        }

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

        /// <summary>
        /// How many tiles anywhere on the floor hold something.
        ///
        /// Counted over the stored rows rather than by walking the field, because the field is
        /// hundreds of tiles and almost all of them are empty — the save only ever holds a row
        /// for a tile somebody touched, which is the whole reason a big floor costs nothing.
        /// </summary>
        public static int OccupiedCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var pair in _placed)
                if (pair.Value.IsOccupied && catalog.Floor.Contains(pair.Key)) count++;

            return count;
        }

        /// <summary>
        /// How many of one region's buildable tiles hold something.
        ///
        /// The hall's tile is excluded, because it is drawn from what the player owns rather
        /// than placed — counting it would make a region that can never read as finished.
        /// </summary>
        public static int OccupiedCount(GroveFloor floor, GroveRegion region)
        {
            if (floor == null || region == null || !region.IsValid) return 0;

            int count = 0;
            for (int c = region.Col; c < region.Col + region.Cols; c++)
                for (int r = region.Row; r < region.Row + region.Rows; r++)
                {
                    string id = GroveFloor.TileId(c, r);
                    if (!floor.IsHall(id) && IsOccupied(id)) count++;
                }

            return count;
        }

        /// <summary>
        /// How full a region is, 0 to 1. One with nowhere to place answers 0.
        ///
        /// <b>This is the whole "I built this" signal</b> — see <see cref="GroveTending"/>.
        /// </summary>
        public static float FillOf(GroveFloor floor, GroveRegion region)
        {
            if (floor == null || region == null || !region.IsValid) return 0f;

            int total = region.TileCount;
            if (floor.HallIsIn(region)) total--;

            return total <= 0 ? 0f : (float)OccupiedCount(floor, region) / total;
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
            foreach (var pair in _placed)
                if (pair.Value.IsOccupied && catalog.Floor.Contains(pair.Key))
                    seen.Add(pair.Value.PieceId);

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

            // The starter companion is shown rather than stored (see Shown), so it has no row
            // to be found here — and art nobody asked for is art that draws as nothing.
            string starter = HomesteadCatalog.Current.Floor.StarterPiece;
            if (!string.IsNullOrEmpty(starter)
                && !_placed.ContainsKey(HomesteadCatalog.Current.Floor.StarterTile))
                ids.Add(starter);

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

            // A new piece faces the way it was drawn. Carrying the old facing over would make
            // the slot remember a decision about something that is no longer standing in it.
            _placed[slotId] = new Placement(wanted, GameClock.NowUnix(), false);

            Telemetry.Track("grove_placed", "slot", slotId,
                            "piece", string.IsNullOrEmpty(wanted) ? "(cleared)" : wanted);

            SaveService.Save();
            Raise();
            return true;
        }

        /// <summary>Takes whatever is in a slot away. A choice, and stamped as one.</summary>
        public static bool Clear(string slotId) => Place(slotId, string.Empty);

        /// <summary>
        /// Mirrors whatever a tile is showing, and writes the row that says so.
        ///
        /// <para>
        /// It reads <see cref="Shown"/> rather than <see cref="At"/>, which matters on exactly
        /// one tile: the starter companion is shown while its slot has no row (invariant 16f),
        /// so flipping it has to write the piece down as well as the facing — otherwise the row
        /// would say "this tile is mirrored and empty" and the friend would vanish.
        /// </para>
        /// </summary>
        public static bool Flip(HomesteadCatalog catalog, string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return false;

            string piece = Shown(catalog, slotId);
            if (string.IsNullOrEmpty(piece)) return false;

            _placed[slotId] = new Placement(piece, GameClock.NowUnix(), !FlippedAt(slotId));

            Telemetry.Track("grove_flipped", "slot", slotId, "piece", piece);

            SaveService.Save();
            Raise();
            return true;
        }

        /// <summary>
        /// Moves what stands on one tile to another, swapping with whatever was already there.
        ///
        /// <para>
        /// <b>A move and a swap are deliberately the same operation.</b> The destination's
        /// contents — which may be nothing — are written back to the source, so a drop onto an
        /// occupied tile exchanges the two and a drop onto bare ground clears the tile behind
        /// it. One path rather than two means there is no state in which a drag can be refused
        /// for a reason the player has to discover, and every move is undone by making it again.
        /// </para>
        /// <para>
        /// <b>The facing travels with the piece</b>, because it is a fact about the thing rather
        /// than about the ground under it — a mirrored fence dragged two tiles left is still the
        /// same mirrored fence, and having to flip it again after every move would make the two
        /// controls fight each other.
        /// </para>
        /// <para>
        /// Both rows are stamped with the same instant, which is what makes the pair survive a
        /// merge together: a device that sees only one of them would show the piece twice or
        /// not at all, and a shared stamp means the join takes both or neither from whichever
        /// side is newer. The hall is refused at both ends — it is drawn from what the player
        /// owns rather than placed (invariant 16), so it has nothing to give and no room to
        /// take. Like <see cref="Place"/>, this does not re-derive whether the land is owned:
        /// the field only draws tiles the player has, and a second copy of that rule here is a
        /// second answer for a retune to put out of step with the first.
        /// </para>
        /// </summary>
        public static bool Move(HomesteadCatalog catalog, string fromSlot, string toSlot)
        {
            if (catalog == null) return false;
            if (string.IsNullOrEmpty(fromSlot) || string.IsNullOrEmpty(toSlot)) return false;
            if (string.Equals(fromSlot, toSlot, StringComparison.Ordinal)) return false;

            var floor = catalog.Floor;
            if (floor == null) return false;
            if (!floor.Contains(fromSlot) || !floor.Contains(toSlot)) return false;
            if (floor.IsHall(fromSlot) || floor.IsHall(toSlot)) return false;

            string moving = Shown(catalog, fromSlot);
            if (string.IsNullOrEmpty(moving)) return false;

            bool movingFlipped = FlippedAt(fromSlot);
            string displaced = Shown(catalog, toSlot);
            bool displacedFlipped = FlippedAt(toSlot);

            long now = GameClock.NowUnix();
            _placed[toSlot] = new Placement(moving, now, movingFlipped);
            _placed[fromSlot] = new Placement(displaced, now, displacedFlipped);

            Telemetry.Track("grove_moved", "from", fromSlot, "to", toSlot, "piece", moving,
                            "swapped", string.IsNullOrEmpty(displaced) ? "no" : "yes");

            SaveService.Save();
            Raise();
            return true;
        }

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
                    //
                    // The piece id is read through GroveResidents.Rename, which is where the
                    // five creatures the grove used to author of its own become the companions
                    // they now are. It happens here, at the one door every save comes through —
                    // a local file, a cloud document, a merge of the two — rather than at each
                    // reader, so nothing downstream ever sees a retired id and the rewrite is
                    // written back the next time the file is saved.
                    _placed[row.slot] = new Placement(GroveResidents.Rename(row.piece), row.setUnix,
                                                      row.flipped);
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

                var incoming = new Placement(row.piece, row.setUnix, row.flipped);

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

            int byPiece = string.CompareOrdinal(a.PieceId, b.PieceId);
            if (byPiece != 0) return byPiece > 0 ? a : b;

            // Same instant and the same piece, differing only in which way it faces. Falling
            // through to "return a" here is the whole trap: it is not a tie-break at all, it is
            // argument order, so the two devices would each keep their own facing and push it
            // back at the other for ever. Preferring the mirrored one is arbitrary — being
            // arbitrary is fine and depending on who asked is not.
            return a.Flipped ? a : b;
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
                    flipped = placed.Flipped,
                };
            }

            return rows;
        }

        /// <summary>Test seam: an empty grove, as a fresh install would have.</summary>
        internal static void ResetForTests() => _placed.Clear();
    }
}
