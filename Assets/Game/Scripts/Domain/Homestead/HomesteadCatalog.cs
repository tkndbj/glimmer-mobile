using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

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
            new HomesteadCatalog(Array.Empty<HomesteadPlot>(), Array.Empty<HomesteadPiece>());

        readonly HomesteadPlot[] _plots;
        readonly HomesteadPiece[] _pieces;
        readonly Dictionary<string, int> _pieceById;
        readonly Dictionary<string, HomesteadSlot> _slotById;
        readonly Dictionary<string, HomesteadPlot> _plotOfSlot;

        public HomesteadCatalog(IReadOnlyList<HomesteadPlot> plots, IReadOnlyList<HomesteadPiece> pieces)
        {
            _plots = Copy(plots, Array.Empty<HomesteadPlot>());
            _pieces = Copy(pieces, Array.Empty<HomesteadPiece>());

            _pieceById = new Dictionary<string, int>(_pieces.Length, StringComparer.Ordinal);
            for (int i = 0; i < _pieces.Length; i++)
                if (_pieces[i].IsValid) _pieceById[_pieces[i].Id] = i;

            _slotById = new Dictionary<string, HomesteadSlot>(StringComparer.Ordinal);
            _plotOfSlot = new Dictionary<string, HomesteadPlot>(StringComparer.Ordinal);

            foreach (var plot in _plots)
                foreach (var slot in plot.Slots)
                {
                    if (!slot.IsValid) continue;
                    _slotById[slot.Id] = slot;
                    _plotOfSlot[slot.Id] = plot;
                }
        }

        static T[] Copy<T>(IReadOnlyList<T> source, T[] fallback)
        {
            if (source == null || source.Count == 0) return fallback;

            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }

        public bool IsEmpty => _plots.Length == 0 && _pieces.Length == 0;

        // ----------------------------------------------------------------- plots
        /// <summary>Every plot, in the order the file authored them — which is draw order.</summary>
        public IReadOnlyList<HomesteadPlot> Plots => _plots;

        public int PlotCount => _plots.Length;

        /// <summary>Every slot in the grove, across every plot.</summary>
        public int SlotCount
        {
            get
            {
                int total = 0;
                foreach (var plot in _plots) total += plot.SlotCount;
                return total;
            }
        }

        public bool TryFindSlot(string slotId, out HomesteadSlot slot)
            => _slotById.TryGetValue(slotId ?? string.Empty, out slot);

        /// <summary>The plot a slot belongs to, or null for a slot this catalog does not know.</summary>
        public HomesteadPlot PlotOf(string slotId)
            => _plotOfSlot.TryGetValue(slotId ?? string.Empty, out var plot) ? plot : null;

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
        {
            var best = default(HomesteadPiece);
            if (catalog == null) return best;

            foreach (var piece in catalog.Pieces)
            {
                if (piece.IsResident || piece.IsDwelling || piece.Slot != kind) continue;
                if (!best.IsValid || piece.Cost < best.Cost) best = piece;
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
