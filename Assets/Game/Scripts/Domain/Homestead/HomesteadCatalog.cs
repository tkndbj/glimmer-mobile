using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// Everything that exists in the grove: which plots there are and what can stand on
    /// them. Immutable, and swapped whole.
    ///
    /// <para>
    /// <b>This is a body, not an index.</b> The manifest carries one integer for it — a
    /// version — and the file itself is read when the player opens the Grovement and
    /// dropped when they leave, exactly like a chapter body and for exactly invariant 4a's
    /// reason. A shop that grows to two hundred pieces and a grove that grows to twenty
    /// plots would otherwise be tens of kilobytes parsed at every launch, on every device,
    /// forever, to answer a question nothing on the boot path asks. Nothing outside the
    /// Grovement screens needs to know what a fence costs.
    /// </para>
    /// <para>
    /// There is deliberately <b>no built-in fallback roster</b>, which is where this parts
    /// company with <c>AvatarCatalog</c>. A companion has to be drawable on the hub the
    /// instant the game starts, so a client whose content failed still needs an answer. The
    /// grove is a screen the player navigates to, so "not loaded yet" is its ordinary state
    /// for most of a session, and a built-in list would mean drawing one grove and then
    /// replacing it with another a frame later. A failure here is reported, the same way a
    /// chapter body that cannot be read is.
    /// </para>
    /// </summary>
    public sealed class HomesteadCatalog
    {
        public static readonly HomesteadCatalog Empty =
            new HomesteadCatalog(GroveFloor.Empty, Array.Empty<HomesteadPiece>());

        readonly GroveFloor _floor;
        readonly HomesteadPiece[] _authored;
        readonly HomesteadPiece[] _pieces;
        readonly Dictionary<string, int> _pieceById;
        readonly GroveScoreTable _scores;

        /// <summary>
        /// A catalog of what the file authored, plus the residents the roster projects.
        ///
        /// <para>
        /// The two halves are kept apart in <see cref="Authored"/> so the roster can be
        /// swapped without re-reading the file — see <see cref="WithResidents"/>. Residents
        /// come last, which is the order the shop and the picker sort against, and they are
        /// dropped rather than merged when an id collides with an authored piece: the file's
        /// row wins, because that is the one written into <c>homesteadOwned</c> as an
        /// entitlement, and the build gate fails on the collision so it never reaches a player.
        /// </para>
        /// </summary>
        public HomesteadCatalog(GroveFloor floor,
                                IReadOnlyList<HomesteadPiece> pieces,
                                IReadOnlyList<AvatarDefinition> residents,
                                GroveScoreTable scores = null)
            : this(floor, Compose(pieces, residents), pieces, scores)
        {
        }

        public HomesteadCatalog(GroveFloor floor, IReadOnlyList<HomesteadPiece> pieces,
                                GroveScoreTable scores = null)
            : this(floor, pieces, pieces, scores)
        {
        }

        HomesteadCatalog(GroveFloor floor,
                         IReadOnlyList<HomesteadPiece> pieces,
                         IReadOnlyList<HomesteadPiece> authored,
                         GroveScoreTable scores)
        {
            _floor = floor ?? GroveFloor.Empty;
            _scores = scores ?? GroveScoreTable.Default;
            _pieces = Copy(pieces, Array.Empty<HomesteadPiece>());
            _authored = ReferenceEquals(authored, pieces) ? _pieces : Copy(authored, Array.Empty<HomesteadPiece>());

            _pieceById = new Dictionary<string, int>(_pieces.Length, StringComparer.Ordinal);
            for (int i = 0; i < _pieces.Length; i++)
                if (_pieces[i].IsValid) _pieceById[_pieces[i].Id] = i;
        }

        static T[] Copy<T>(IReadOnlyList<T> source, T[] fallback)
        {
            if (source == null || source.Count == 0) return fallback;

            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }

        static List<HomesteadPiece> Compose(IReadOnlyList<HomesteadPiece> authored,
                                            IReadOnlyList<AvatarDefinition> residents)
        {
            var composed = new List<HomesteadPiece>((authored?.Count ?? 0) + (residents?.Count ?? 0));
            var taken = new HashSet<string>(StringComparer.Ordinal);

            if (authored != null)
                foreach (var piece in authored)
                {
                    if (!piece.IsValid) continue;
                    if (taken.Add(piece.Id)) composed.Add(piece);
                }

            foreach (var resident in GroveResidents.From(residents))
                if (taken.Add(resident.Id)) composed.Add(resident);

            return composed;
        }

        /// <summary>
        /// The same catalog with a different roster projected into it.
        ///
        /// Content publishes the roster and the grove body on separate paths — the roster
        /// rides the manifest and is in hand at boot, the body is read when the Grovement is
        /// opened — so either can arrive second, and a content refresh can replace the roster
        /// under an open screen. Rebuilding from <see cref="Authored"/> rather than re-reading
        /// the file is what makes that a swap of one immutable object rather than an I/O
        /// round trip in front of a screen the player is already looking at.
        /// </summary>
        public HomesteadCatalog WithResidents(IReadOnlyList<AvatarDefinition> residents)
            => new HomesteadCatalog(_floor, _authored, residents, _scores);

        /// <summary>
        /// The star ladder this grove's score is read against. See <see cref="GroveScoreTable"/>.
        ///
        /// Never null: a body that does not carry one — or predates the field — falls back to
        /// the built-in ladder, because a grove a version behind must still be able to draw
        /// its own standing.
        /// </summary>
        public GroveScoreTable Scores => _scores ?? GroveScoreTable.Default;

        /// <summary>What the file said, before any resident was projected in.</summary>
        public IReadOnlyList<HomesteadPiece> Authored => _authored;

        public bool IsEmpty => _floor.IsEmpty && _pieces.Length == 0;

        // ----------------------------------------------------------------- floor
        /// <summary>The ground everything stands on. See <see cref="GroveFloor"/>.</summary>
        public GroveFloor Floor => _floor;

        /// <summary>Every tile of the field, whether or not the player owns it yet.</summary>
        public int SlotCount => _floor.TileCount;

        /// <summary>
        /// The tile an id names, or an invalid slot for an id off the floor.
        ///
        /// Derived rather than looked up: a floor has a slot at every tile, so there is no
        /// table to consult and nothing to keep in step. An id the floor does not contain
        /// answers invalid, which is what a save written by a newer build with a bigger floor
        /// produces — and the right response there is to draw nothing and leave the row alone,
        /// never to erase somebody's arrangement on a rollback.
        /// </summary>
        public bool TryFindSlot(string slotId, out HomesteadSlot slot)
        {
            slot = default;
            if (!GroveFloor.TryParse(slotId, out int col, out int row)) return false;
            if (!_floor.Contains(col, row)) return false;

            slot = new HomesteadSlot(slotId, col, row);
            return true;
        }

        /// <summary>The region a tile is sold in, or null for ground nobody sells.</summary>
        public GroveRegion RegionOf(string slotId) => _floor.RegionOf(slotId);

        // ---------------------------------------------------------------- pieces
        /// <summary>Every piece, in authored order.</summary>
        public IReadOnlyList<HomesteadPiece> Pieces => _pieces;

        public int PieceCount => _pieces.Length;

        public bool Exists(string id) => !string.IsNullOrEmpty(id) && _pieceById.ContainsKey(id);

        /// <summary>
        /// The piece with this id, or an invalid one.
        ///
        /// An unknown id is not an error and must never be treated as one: a save written by
        /// a newer build can name a piece this catalog has not heard of, and the right
        /// answer is to draw nothing in that slot while leaving the row untouched — see
        /// <see cref="HomesteadLayout"/>.
        /// </summary>
        public HomesteadPiece Find(string id)
            => !string.IsNullOrEmpty(id) && _pieceById.TryGetValue(id, out int i) ? _pieces[i] : default;

        /// <summary>
        /// The piece a kind of slot labels itself with: the cheapest decor that belongs there.
        ///
        /// <para>
        /// In Domain rather than beside the shop because two things have to agree about it —
        /// the tab that draws it and the asset scope that has to have loaded it — and a second
        /// copy of "cheapest of this kind" is a second answer for a retune to put out of step
        /// with the first. Residents and dwellings are excluded: a resident fits every kind,
        /// so it would label all six tabs identically.
        /// </para>
        /// </summary>
        public static HomesteadPiece Emblem(HomesteadCatalog catalog, HomesteadSlotKind kind)
            => Emblem(catalog, GroveShelves.Of(kind));

        /// <summary>
        /// The piece a shelf labels itself with: the cheapest thing on it.
        ///
        /// <para>
        /// One rule for all eight shelves rather than a special case for the two that are not
        /// decor. The residents' tab wears whichever companion is cheapest — which is the one
        /// a new player is closest to, and therefore the right face for the shelf — and the
        /// home's wears the first rung of the ladder. Ties go to catalog order, which is
        /// arbitrary and <em>stable</em>, and stability is the property that matters: two
        /// devices must draw the same row of tabs.
        /// </para>
        /// </summary>
        public static HomesteadPiece Emblem(HomesteadCatalog catalog, GroveShelf shelf)
        {
            var best = default(HomesteadPiece);
            if (catalog == null) return best;

            foreach (var piece in catalog.Pieces)
            {
                if (GroveShelves.Of(piece) != shelf) continue;

                // The home ladder orders by tier, not by price — the cheapest rung is the
                // first one, and it is the only one that is free.
                bool better = shelf == GroveShelf.Home
                    ? piece.Tier < best.Tier
                    : piece.Cost < best.Cost;

                if (!best.IsValid || better) best = piece;
            }

            return best;
        }

        // ------------------------------------------------------------- publishing
        /// <summary>The catalog in force. <see cref="Empty"/> until the body has been read.</summary>
        public static HomesteadCatalog Current { get; private set; } = Empty;

        /// <summary>True once a body has been read, however small it turned out to be.</summary>
        public static bool IsLoaded { get; private set; }

        /// <summary>Raised after a new catalog is published, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>
        /// Installs a catalog. Publishing swaps one immutable object in a single assignment,
        /// so no screen can observe a half-updated grove.
        /// </summary>
        public static void Publish(HomesteadCatalog catalog)
        {
            Current = catalog ?? Empty;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>Test seam: back to nothing loaded, as a fresh launch would be.</summary>
        internal static void ResetForTests()
        {
            Current = Empty;
            IsLoaded = false;
        }
    }
}
