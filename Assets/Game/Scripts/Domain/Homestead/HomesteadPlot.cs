using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// What a slot is <em>for</em>, and therefore what may stand in it.
    ///
    /// <para>
    /// <b>This is what makes placing a composition rather than a sprinkle.</b> Every slot
    /// used to accept everything, so the only decision a player could make was which of seven
    /// interchangeable dots got which sticker — and every arrangement came out looking equally
    /// accidental. A fence standing in the middle of a lawn is not a fence; a path segment
    /// that leads nowhere is not a path. One field per slot fixes both, because the author can
    /// now say <em>the rim is where fences go</em> and the game can hold the player to it.
    /// </para>
    /// <para>
    /// The rule is a plain equality — a decor piece declares the one kind of slot it belongs
    /// in and fits only that — with two deliberate exceptions in
    /// <see cref="HomesteadPieceKind"/>. A set of accepted kinds per piece was the obvious
    /// alternative and buys nothing: a bench that fits three places is a bench with no place,
    /// and every extra degree of freedom here is another way for a grove to look accidental.
    /// </para>
    /// <para>
    /// <see cref="Ground"/> is the default for slots and for pieces alike, so a catalog
    /// written before this field existed keeps working exactly as it did — which is the same
    /// bargain every other optional content field here makes.
    /// </para>
    /// </summary>
    public enum HomesteadSlotKind
    {
        /// <summary>Open ground: rocks, logs, crates, anything that just sits there.</summary>
        Ground,

        /// <summary>
        /// The one place a home stands. Never placed by hand — see
        /// <see cref="HomesteadLedger.BestDwelling"/>, which draws the best one the player owns.
        /// </summary>
        Hearth,

        /// <summary>A built thing that anchors an island: a well, a cave mouth, a spire.</summary>
        Structure,

        /// <summary>Planted ground: flowers, sprouts, bushes.</summary>
        Bed,

        /// <summary>A step of a route. Authored in short chains so a path leads somewhere.</summary>
        Path,

        /// <summary>The island's rim: fences, signs, lanterns.</summary>
        Edge,

        /// <summary>Back of the island, drawn tall: trees and anything that towers.</summary>
        Canopy,
    }

    /// <summary>
    /// One place on a plot where a piece can stand.
    ///
    /// <para>
    /// <b>Slots rather than free placement, and the save file is why.</b> Letting a player
    /// drop a piece at any (x, y) means storing a list of positions — which grows without
    /// bound, has no natural key, and cannot be merged: two devices that each added three
    /// things produce two lists with no way to tell a moved item from a new one. A slot is
    /// a permanent id, so the layout is a <em>map</em> keyed by it, which is invariant 11a's
    /// shape for the same reason the star ledger has it: a duplicate is unrepresentable and
    /// a sync can write one slot without re-uploading the grove.
    /// </para>
    /// <para>
    /// It is also the better screen. Every arrangement is composed by whoever authored the
    /// plot, so nobody's grove is a pile of overlapping trees, and depth sorting is a fact
    /// about the slot rather than a physics problem. Forty slots and sixty pieces is more
    /// arrangements than there are atoms in the observable universe, so the freedom that
    /// was given up is freedom nobody would have used.
    /// </para>
    /// </summary>
    public readonly struct HomesteadSlot
    {
        /// <summary>
        /// Permanent id, unique across the whole grove. Written into the save file, so it is
        /// under invariant 1: never renamed, never reused, and never derived from the slot's
        /// position in its plot's list — inserting a slot must not move what stands in the
        /// ones after it.
        /// </summary>
        public readonly string Id;

        /// <summary>Position within the plot, as fractions of the plot's own box.</summary>
        public readonly float X, Y;

        /// <summary>
        /// What may stand here. See <see cref="HomesteadSlotKind"/> for why this exists at all.
        /// </summary>
        public readonly HomesteadSlotKind Kind;

        /// <summary>
        /// How large a piece standing here draws, before the piece's own
        /// <see cref="HomesteadPiece.Scale"/>. This is the composition half — front and
        /// centre is bigger than back and left.
        /// </summary>
        public readonly float Scale;

        public HomesteadSlot(string id, float x, float y, float scale,
                             HomesteadSlotKind kind = HomesteadSlotKind.Ground)
        {
            Id = id;
            X = x;
            Y = y;
            Scale = scale > 0f ? scale : 1f;
            Kind = kind;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        /// <summary>
        /// True for the slot a home stands on, which is the one slot nothing is placed into.
        /// </summary>
        public bool IsHearth => Kind == HomesteadSlotKind.Hearth;

        public override string ToString() => Id ?? "(none)";
    }

    /// <summary>
    /// One island of the grove: a piece of land, where it floats, and the slots on it.
    ///
    /// <para>
    /// <b>Land is derived, never stored.</b> A plot is held when its
    /// <see cref="RequiresChapter"/> is finished, which is a question about the star ledger
    /// — so it recomputes on every device, survives every merge, cannot be lost and can be
    /// retuned for players who already have it. That is invariant 14 applied to the thing
    /// the whole screen is made of: the grove grows because the player played, and not one
    /// byte records that it did.
    /// </para>
    /// <para>
    /// The consequence worth naming: a chapter drop ships a plot, so the grove gets visibly
    /// bigger on the day new glades arrive, for everybody, with no migration and no
    /// backfill. That is the cheap-today-cheap-later shape this project keeps choosing.
    /// </para>
    /// </summary>
    public sealed class HomesteadPlot
    {
        /// <summary>Permanent id. Not in the save file — slot ids are — but analytics keys on it.</summary>
        public readonly string Id;

        /// <summary>Sprite key relative to <c>Art/</c>, the island this plot is drawn as.</summary>
        public readonly string Art;

        /// <summary>
        /// Where it floats horizontally, as a fraction of the grove canvas width.
        ///
        /// The only position an author writes. Vertical position is <em>derived</em> by
        /// <see cref="HomesteadMap"/> from the real height of each island's art, because the
        /// number an authored <c>y</c> would have to agree with lives in a PNG the author
        /// cannot see from the JSON — and when it was authored, every consecutive pair of
        /// islands overlapped and the starter plot fell outside the scrollable area entirely.
        /// </summary>
        public readonly float X;

        /// <summary>Size of the island art, as a fraction of the grove canvas width.</summary>
        public readonly float Width;

        /// <summary>
        /// The chapter that must be finished for this plot to be held, or
        /// <see cref="ChapterId.None"/> for the one every account starts with.
        /// </summary>
        public readonly ChapterId RequiresChapter;

        readonly HomesteadSlot[] _slots;

        public HomesteadPlot(string id, string art, float x, float width,
                             ChapterId requiresChapter, IReadOnlyList<HomesteadSlot> slots)
        {
            Id = id;
            Art = art ?? string.Empty;
            X = x;
            Width = width > 0f ? width : .5f;
            RequiresChapter = requiresChapter;

            if (slots == null || slots.Count == 0)
            {
                _slots = Array.Empty<HomesteadSlot>();
            }
            else
            {
                _slots = new HomesteadSlot[slots.Count];
                for (int i = 0; i < slots.Count; i++) _slots[i] = slots[i];
            }
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public IReadOnlyList<HomesteadSlot> Slots => _slots;

        public int SlotCount => _slots.Length;

        /// <summary>
        /// This island's hearth, or an invalid slot when it has none.
        ///
        /// Only the starter plot carries one, and that is the design rather than a limitation:
        /// a player has one home. Every other island anchors on a
        /// <see cref="HomesteadSlotKind.Structure"/> slot instead, which is placed by hand like
        /// anything else.
        /// </summary>
        public HomesteadSlot Hearth
        {
            get
            {
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i].IsHearth) return _slots[i];

                return default;
            }
        }

        /// <summary>
        /// How many slots a player can actually put something in — every slot but the hearth.
        ///
        /// The denominator of "how tended is this island", which is why it is here rather than
        /// counted at each call site: the hearth draws itself from what the player owns, so
        /// counting it would make an island that can never read as finished.
        /// </summary>
        public int PlaceableCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i].IsValid && !_slots[i].IsHearth) count++;

                return count;
            }
        }

        /// <summary>True when this plot needs no chapter — the ground a new grove starts on.</summary>
        public bool IsStarter => !RequiresChapter.IsValid;

        /// <summary>
        /// A plot's name is a pure function of its id, for
        /// <see cref="HomesteadPiece.NameKey"/>'s reason.
        /// </summary>
        public string NameKey => "ui.plot." + Id;

        public override string ToString() => Id ?? "(none)";
    }
}
