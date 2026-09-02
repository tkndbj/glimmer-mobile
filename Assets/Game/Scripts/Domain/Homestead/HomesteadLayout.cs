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
    /// stage down a notch and back up as they fill it. A two-tile piece fills two tiles, which
    /// is why fill is asked of the catalog rather than of the floor alone.
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

        public static TendedStage Of(HomesteadCatalog catalog, GroveRegion region)
            => Of(HomesteadLayout.FillOf(catalog, region));
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
        /// isometric drawing still standing on its own tile, so it is the one offered — and a
        /// mirrored footprint swaps its columns for its rows, see <see cref="GroveFootprint"/>.
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
    /// What a move would do, worked out before anything is written — so a drag can show its
    /// answer under the finger and the drop can execute exactly what was shown.
    /// </summary>
    public readonly struct GroveMovePlan
    {
        public readonly GrovePlaceResult Result;

        /// <summary>Where the moving piece's anchor would land.</summary>
        public readonly int AnchorCol, AnchorRow;

        /// <summary>The moving piece's footprint, facing as it does.</summary>
        public readonly GroveFootprint Footprint;

        /// <summary>The stand that would move the other way, or invalid for a move onto clear ground.</summary>
        public readonly GroveStand Swapped;

        public GroveMovePlan(GrovePlaceResult result, int anchorCol, int anchorRow,
                             GroveFootprint footprint, GroveStand swapped = default)
        {
            Result = result;
            AnchorCol = anchorCol;
            AnchorRow = anchorRow;
            Footprint = footprint;
            Swapped = swapped;
        }

        public bool Ok => Result == GrovePlaceResult.Placed;

        public bool IsSwap => Ok && Swapped.IsValid;
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
    /// <b>A row names an anchor, and a piece may cover more than its anchor.</b> The tiles a
    /// piece occupies are derived through the catalog (<see cref="GroveOccupancy"/>), so
    /// footprints cost the file nothing and can be retuned in a drop. The rules that keep the
    /// floor consistent — nothing placed where something stands, nothing under the hall — are
    /// applied when the player acts, through <see cref="TryPlace"/>, <see cref="PlanMove"/>
    /// and <see cref="Flip"/>, never to the stored rows: a merge that lands two pieces on one
    /// tile keeps both, because losing either is the failure invariant 11 refuses.
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

        /// <summary>Bumped on every write, so a derived index knows when it is stale.</summary>
        static int _version;

        /// <summary>Raised when anything moved, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when the <em>player</em> placed, moved, flipped or cleared something —
        /// never when a save was loaded or a merge adopted, which is the difference from
        /// <see cref="Changed"/> and the whole reason this exists. See <c>SyncTriggers</c>.
        /// </summary>
        public static event Action Edited;

        // ------------------------------------------------------------- reading
        /// <summary>The piece id in a slot, or empty. Unknown slots answer empty.</summary>
        public static string At(string slotId)
            => !string.IsNullOrEmpty(slotId) && _placed.TryGetValue(slotId, out var p)
                ? p.PieceId
                : string.Empty;

        /// <summary>Whether a row with a piece in it is anchored on this slot.</summary>
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

        // ----------------------------------------------------------- occupancy
        static GroveOccupancy _index = GroveOccupancy.Empty;
        static HomesteadCatalog _indexCatalog;
        static int _indexVersion = -1;

        /// <summary>
        /// The index depends on one thing this class does not write: which home the player
        /// holds, which the hall stand is drawn from. So the ledger's event bumps the version
        /// exactly as a row write does, and the index never has to ask the ledger whether it
        /// is stale — a question that used to be asked on every tile bind.
        /// </summary>
        static HomesteadLayout()
        {
            HomesteadLedger.Changed += () => _version++;
        }

        /// <summary>
        /// Every tile covered by what, for this catalog — rebuilt only when a row, the catalog
        /// or the held homes change, and otherwise the same object on every call.
        ///
        /// <para>
        /// The hall is in it, as a stand that is never placed and never picked up, so a rule
        /// asking "is there room here" gets one answer from one index rather than a hall check
        /// and an occupancy check that could disagree. The starter companion is in it while
        /// its tile is untouched, exactly as <see cref="Shown"/> draws it.
        /// </para>
        /// </summary>
        public static GroveOccupancy Occupancy(HomesteadCatalog catalog)
        {
            if (catalog == null) return GroveOccupancy.Empty;

            if (ReferenceEquals(catalog, _indexCatalog) && _indexVersion == _version) return _index;

            _index = new GroveOccupancy(Stands(catalog, HomesteadLedger.BestDwelling(catalog).Id));
            _indexCatalog = catalog;
            _indexVersion = _version;
            return _index;
        }

        static IEnumerable<GroveStand> Stands(HomesteadCatalog catalog, string dwelling)
        {
            var floor = catalog.Floor;

            var hall = floor.HallStand(dwelling);
            if (hall.IsValid) yield return hall;

            foreach (var pair in _placed)
            {
                if (!pair.Value.IsOccupied || !floor.Contains(pair.Key)) continue;

                var stand = GroveOccupancy.Of(catalog, pair.Key, pair.Value.PieceId, pair.Value.Flipped);
                if (stand.IsValid) yield return stand;
            }

            string starter = floor.StarterPiece;
            if (!string.IsNullOrEmpty(starter) && !_placed.ContainsKey(floor.StarterTile))
            {
                var stand = GroveOccupancy.Of(catalog, floor.StarterTile, starter, false);
                if (stand.IsValid) yield return stand;
            }
        }

        /// <summary>
        /// The stand a tile belongs to — anchored on it or reaching over it — if any. The one
        /// question a tap, a hold and a drop all ask.
        /// </summary>
        public static bool TryStandAt(HomesteadCatalog catalog, int col, int row, out GroveStand stand)
            => Occupancy(catalog).TryStandAt(col, row, out stand);

        /// <summary>Whether the player may build on a tile: owned land that the hall does not cover.</summary>
        static Func<int, int, bool> Buildable(HomesteadCatalog catalog)
        {
            var floor = catalog.Floor;
            if (!ReferenceEquals(floor, _buildableFloor))
            {
                _buildableFloor = floor;
                _buildable = (c, r) => GroveLand.IsOwned(floor, c, r);
            }

            return _buildable;
        }

        static GroveFloor _buildableFloor;
        static Func<int, int, bool> _buildable;

        // --------------------------------------------------------------- counts
        /// <summary>
        /// How many things stand anywhere on the floor.
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
        /// How many of one region's buildable tiles hold something, counting every tile a
        /// footprint covers. The hall's tiles are excluded — see <see cref="GroveOccupancy.CoveredCount"/>.
        /// </summary>
        public static int CoveredCount(HomesteadCatalog catalog, GroveRegion region)
            => catalog == null ? 0 : Occupancy(catalog).CoveredCount(region);

        /// <summary>
        /// How full a region is, 0 to 1. One with nowhere to place answers 0.
        ///
        /// <b>This is the whole "I built this" signal</b> — see <see cref="GroveTending"/>.
        /// </summary>
        public static float FillOf(HomesteadCatalog catalog, GroveRegion region)
        {
            if (catalog == null || region == null || !region.IsValid) return 0f;

            int total = region.TileCount - catalog.Floor.HallTilesIn(region);
            return total <= 0 ? 0f : Math.Min(1f, (float)CoveredCount(catalog, region) / total);
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
        /// How many copies of one piece are standing in the grove.
        ///
        /// <para>
        /// The other half of the stock subtraction: what a player may still place is what they
        /// bought minus this (see <see cref="HomesteadLedger.Available(HomesteadPiece)"/>).
        /// Deriving it rather than keeping a running total is what makes a copy count safe to
        /// store at all — a stored "how many are left" would go up and down, which is precisely
        /// the shape invariant 11b refuses, while this is recomputed from rows that are already
        /// merged and can never disagree with what the grove is drawing.
        /// </para>
        /// <para>
        /// Counted over <em>every</em> row rather than only over tiles the floor still contains,
        /// deliberately. Land the player has not bought back, a region a content drop moved and
        /// a tile id from a newer build all hold a piece the player put there and will get back;
        /// counting only visible tiles would hand them a free copy of everything parked outside
        /// the current floor, and the id is what was paid for rather than the tile.
        /// </para>
        /// <para>
        /// The starter companion is deliberately absent, for the reason
        /// <see cref="PlacedIds"/> gives: it is shown rather than stored, it is a resident, and
        /// a resident is an entitlement that is never counted against stock.
        /// </para>
        /// </summary>
        public static int CountOf(string pieceId)
        {
            if (string.IsNullOrEmpty(pieceId)) return 0;

            int count = 0;
            foreach (var placed in _placed.Values)
                if (placed.IsOccupied && string.Equals(placed.PieceId, pieceId, StringComparison.Ordinal))
                    count++;

            return count;
        }

        /// <summary>
        /// Every distinct piece id standing somewhere in the grove.
        ///
        /// What the grove screen's art scope is built from, which is why it is a set rather
        /// than a list: a fence placed in nine slots is one texture. Slots the catalog does
        /// not know are included — the address simply misses, and dropping them here would
        /// mean a rollback silently stopped loading art it might still have.
        /// </summary>
        /// <summary>
        /// The slots with something standing in them, by slot id.
        ///
        /// <para>
        /// Distinct from <see cref="PlacedIds"/>, which answers in <em>piece</em> ids for the
        /// art loader, and the distinction cost a card: <c>GroveCard.OfPlayer</c> walked
        /// <see cref="PlacedIds"/> as though it were slots, asked <see cref="At"/> about each
        /// piece id, got nothing back, and so the locally built card carried no placements at
        /// all — which meant its fingerprint never changed when the player rearranged, and a
        /// rearranged grove never asked to be republished. An emptied slot has a row and is
        /// not listed here; the starter companion has no row and is not either.
        /// </para>
        /// </summary>
        public static IReadOnlyCollection<string> OccupiedSlots()
        {
            var slots = new List<string>(_placed.Count);

            foreach (var pair in _placed)
                if (pair.Value.IsOccupied) slots.Add(pair.Key);

            return slots;
        }

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
        /// Puts a piece in a slot, or clears it when <paramref name="pieceId"/> is empty —
        /// <b>the raw write, with no room check.</b> A caller that has a tile and a piece from
        /// the player wants <see cref="TryPlace"/>, which fits the footprint; this is what
        /// that calls once it has decided, and what a clear uses, because clearing an anchor
        /// never needs room.
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
            Write(slotId, new Placement(wanted, GameClock.NowUnix(), false));

            Telemetry.Track("grove_placed", "slot", slotId,
                            "piece", string.IsNullOrEmpty(wanted) ? "(cleared)" : wanted);

            Commit();
            return true;
        }

        /// <summary>Takes whatever is anchored on a slot away. A choice, and stamped as one.</summary>
        public static bool Clear(string slotId) => Place(slotId, string.Empty);

        /// <summary>
        /// Puts a piece down on the tile the player touched, laying its footprint wherever it
        /// fits around that tile — or clears whatever stands there when the piece is empty.
        ///
        /// <para>
        /// <b>The touched tile is the request, the anchor is the answer.</b> A player tapping a
        /// gap and choosing a two-wide bench is asking for the bench to be <em>there</em>, not
        /// for that tile to be its back corner, so the footprint is fitted around the touch
        /// (<see cref="GroveOccupancy.TryFit"/>) and the row is written at whatever anchor
        /// came of it. Whatever was standing on the touched tile is replaced — its anchor is
        /// cleared, in the same instant, so the pair survives a merge together — which is what
        /// a tap on an occupied tile has always meant here.
        /// </para>
        /// <para>
        /// <see cref="GrovePlaceResult.NoRoom"/> is the one answer the caller has to say out
        /// loud: a picker that closes over a grove that did not change teaches the player that
        /// the control is broken.
        /// </para>
        /// </summary>
        public static GrovePlaceResult TryPlace(HomesteadCatalog catalog, int col, int row, string pieceId,
                                                out int anchorCol, out int anchorRow)
        {
            anchorCol = col;
            anchorRow = row;

            if (catalog == null) return GrovePlaceResult.Refused;

            var floor = catalog.Floor;
            if (!floor.Contains(col, row) || floor.IsHall(col, row)) return GrovePlaceResult.Refused;

            var index = Occupancy(catalog);
            bool standing = index.TryStandAt(col, row, out var was);
            if (standing && was.IsHall) return GrovePlaceResult.Refused;

            string wanted = pieceId ?? string.Empty;

            if (wanted.Length == 0)
            {
                if (!standing) return GrovePlaceResult.Unchanged;

                anchorCol = was.AnchorCol;
                anchorRow = was.AnchorRow;
                return Clear(was.AnchorId) ? GrovePlaceResult.Placed : GrovePlaceResult.Unchanged;
            }

            var piece = catalog.Find(wanted);
            var footprint = piece.IsValid ? piece.Footprint : GroveFootprint.Single;

            // A dwelling is drawn from what the player owns, never placed (invariant 16). The
            // picker never offers one; the rule is here as well because a picker is not where
            // a rule lives.
            if (piece.IsValid && !piece.CanBePlaced) return GrovePlaceResult.Refused;

            // Re-choosing what is already there, at the same anchor and the same size, is the
            // no-op it has always been. A different footprint (a retune since it was placed) is
            // a real change and falls through to be re-fitted.
            if (standing && string.Equals(was.PieceId, wanted, StringComparison.Ordinal)
                && !was.Flipped && was.Footprint == footprint)
            {
                anchorCol = was.AnchorCol;
                anchorRow = was.AnchorRow;
                return GrovePlaceResult.Unchanged;
            }

            long ignore = standing ? was.AnchorKey : GroveOccupancy.NoKey;

            if (!index.TryFit(floor, footprint, col, row, Buildable(catalog),
                              out anchorCol, out anchorRow, ignore))
                return GrovePlaceResult.NoRoom;

            long now = GameClock.NowUnix();
            string anchorId = GroveFloor.TileId(anchorCol, anchorRow);

            if (standing && !string.Equals(was.AnchorId, anchorId, StringComparison.Ordinal))
                Write(was.AnchorId, new Placement(string.Empty, now, false));

            Write(anchorId, new Placement(wanted, now, false));

            Telemetry.Track("grove_placed", "slot", anchorId, "piece", wanted);

            Commit();
            return GrovePlaceResult.Placed;
        }

        /// <summary>
        /// Mirrors whatever a tile is showing, and writes the row that says so.
        ///
        /// <para>
        /// It reads <see cref="Shown"/> rather than <see cref="At"/>, which matters on exactly
        /// one tile: the starter companion is shown while its slot has no row (invariant 16f),
        /// so flipping it has to write the piece down as well as the facing — otherwise the row
        /// would say "this tile is mirrored and empty" and the friend would vanish.
        /// </para>
        /// <para>
        /// A mirrored footprint runs along the other diagonal, so a piece longer than it is
        /// wide is asked whether it still fits before it turns. A single tile always does.
        /// </para>
        /// </summary>
        public static GrovePlaceResult Flip(HomesteadCatalog catalog, string slotId)
        {
            if (catalog == null || string.IsNullOrEmpty(slotId)) return GrovePlaceResult.Refused;

            string piece = Shown(catalog, slotId);
            if (string.IsNullOrEmpty(piece)) return GrovePlaceResult.Refused;

            if (!GroveFloor.TryParse(slotId, out int col, out int row)) return GrovePlaceResult.Refused;

            var index = Occupancy(catalog);
            if (!index.TryAnchored(col, row, out var stand) || stand.IsHall)
                return GrovePlaceResult.Refused;

            bool flipped = !stand.Flipped;
            var turned = stand.Footprint.Mirrored;

            if (turned != stand.Footprint
                && !index.Fits(catalog.Floor, turned, col, row, Buildable(catalog), stand.AnchorKey))
                return GrovePlaceResult.NoRoom;

            Write(slotId, new Placement(piece, GameClock.NowUnix(), flipped));

            Telemetry.Track("grove_flipped", "slot", slotId, "piece", piece);

            Commit();
            return GrovePlaceResult.Placed;
        }

        /// <summary>
        /// What moving the stand anchored on one tile onto another tile would do.
        ///
        /// <para>
        /// <b>A move and a swap are deliberately the same operation.</b> Ground with room takes
        /// the piece; ground held by exactly one other stand exchanges the two, provided each
        /// fits where the other was. One path rather than two means there is no state in which
        /// a drag can be refused for a reason the player has to discover, and every move is
        /// undone by making it again. A footprint that would land on two different stands at
        /// once is <see cref="GrovePlaceResult.NoRoom"/>, because "swap with both" has no
        /// answer a player could predict.
        /// </para>
        /// <para>
        /// Planned apart from being done because the drag shows its answer under the finger on
        /// every frame — the footprint lit green where it would land, red where it would not —
        /// and the drop must do exactly what was shown, so both read one function.
        /// </para>
        /// </summary>
        public static GroveMovePlan PlanMove(HomesteadCatalog catalog, string fromSlot, int toCol, int toRow)
        {
            var refused = new GroveMovePlan(GrovePlaceResult.Refused, toCol, toRow, GroveFootprint.Single);
            if (catalog == null || string.IsNullOrEmpty(fromSlot)) return refused;

            var floor = catalog.Floor;
            if (!GroveFloor.TryParse(fromSlot, out int fromCol, out int fromRow)) return refused;
            if (!floor.Contains(fromCol, fromRow) || !floor.Contains(toCol, toRow)) return refused;
            if (floor.IsHall(toCol, toRow)) return refused;

            var index = Occupancy(catalog);
            if (!index.TryAnchored(fromCol, fromRow, out var moving) || moving.IsHall) return refused;

            var footprint = moving.Footprint;
            var buildable = Buildable(catalog);

            // Room, first: the anchor that keeps the touched tile inside the footprint.
            if (index.TryFit(floor, footprint, toCol, toRow, buildable,
                             out int anchorCol, out int anchorRow, moving.AnchorKey))
            {
                if (anchorCol == fromCol && anchorRow == fromRow)
                    return new GroveMovePlan(GrovePlaceResult.Unchanged, anchorCol, anchorRow, footprint);

                return new GroveMovePlan(GrovePlaceResult.Placed, anchorCol, anchorRow, footprint);
            }

            // Then a swap: anchored on the touched tile itself, landing on exactly one other
            // stand that in turn fits where this one was.
            if (index.Overlapping(footprint, toCol, toRow, out var other, moving.AnchorKey) != 1 || other.IsHall)
                return new GroveMovePlan(GrovePlaceResult.NoRoom, toCol, toRow, footprint);

            bool ok = index.Fits(floor, footprint, toCol, toRow, buildable, moving.AnchorKey, other.AnchorKey)
                   && index.Fits(floor, other.Footprint, fromCol, fromRow, buildable, moving.AnchorKey, other.AnchorKey);

            return ok
                ? new GroveMovePlan(GrovePlaceResult.Placed, toCol, toRow, footprint, other)
                : new GroveMovePlan(GrovePlaceResult.NoRoom, toCol, toRow, footprint);
        }

        /// <summary>
        /// Moves what stands on one tile to another, swapping with whatever was already there.
        /// See <see cref="PlanMove"/> for the rule; this writes what it planned.
        ///
        /// <para>
        /// <b>The facing travels with the piece</b>, because it is a fact about the thing rather
        /// than about the ground under it — a mirrored fence dragged two tiles left is still the
        /// same mirrored fence, and having to flip it again after every move would make the two
        /// controls fight each other.
        /// </para>
        /// <para>
        /// Every row a move touches is stamped with the same instant, which is what makes the
        /// set survive a merge together: a device that sees only one of them would show the
        /// piece twice or not at all, and a shared stamp means the join takes all or none from
        /// whichever side is newer. Like <see cref="Place"/>, this does not re-derive whether
        /// the piece is held: the rows already say it is standing here.
        /// </para>
        /// </summary>
        public static GrovePlaceResult Move(HomesteadCatalog catalog, string fromSlot, int toCol, int toRow)
        {
            var plan = PlanMove(catalog, fromSlot, toCol, toRow);
            if (!plan.Ok) return plan.Result;

            var index = Occupancy(catalog);
            GroveFloor.TryParse(fromSlot, out int fromCol, out int fromRow);
            index.TryAnchored(fromCol, fromRow, out var moving);

            long now = GameClock.NowUnix();
            string toAnchor = GroveFloor.TileId(plan.AnchorCol, plan.AnchorRow);

            // Everything the move vacates is cleared first, then everything it fills is
            // written, so a row that is both (a swap partner's anchor, a piece nudged one tile
            // within its own old footprint) ends up holding what lands there.
            Write(fromSlot, new Placement(string.Empty, now, false));
            if (plan.IsSwap) Write(plan.Swapped.AnchorId, new Placement(string.Empty, now, false));

            Write(toAnchor, new Placement(moving.PieceId, now, moving.Flipped));
            if (plan.IsSwap) Write(fromSlot, new Placement(plan.Swapped.PieceId, now, plan.Swapped.Flipped));

            Telemetry.Track("grove_moved", "from", fromSlot, "to", toAnchor, "piece", moving.PieceId,
                            "swapped", plan.IsSwap ? "yes" : "no");

            Commit();
            return GrovePlaceResult.Placed;
        }

        /// <summary>The same move, named by the tile dropped on.</summary>
        public static GrovePlaceResult Move(HomesteadCatalog catalog, string fromSlot, string toSlot)
            => GroveFloor.TryParse(toSlot, out int col, out int row)
                ? Move(catalog, fromSlot, col, row)
                : GrovePlaceResult.Refused;

        static void Write(string slotId, Placement placement)
        {
            _placed[slotId] = placement;
            _version++;
        }

        static void Commit()
        {
            SaveService.Save();
            Raise();

            // After Changed, and only here: LoadFrom raises Changed too, and anything hung on
            // this event — a sync request — must never fire for a save being read.
            try { Edited?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
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
            _version++;

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
        internal static void ResetForTests()
        {
            _placed.Clear();
            _version++;
        }
    }
}
