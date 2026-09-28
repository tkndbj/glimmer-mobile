using GlimmerGrove.Modes;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The doors a script plays this board through, and the widgets it points at.
    ///
    /// <para>
    /// <b>Every entry here is the handler a finger reaches, called by name.</b> A drag lands in
    /// <c>Drag</c>, a tube tap in <c>Unleashed</c>, a cog in <c>Grabbed</c>, a bomb in
    /// <c>Tapped</c>, a heap in <c>Digging</c> - the same latches, the same refusals, the same
    /// drawing. Nothing below decides anything or draws anything of its own, so a run played by
    /// <c>ShowcaseScreen</c>'s hand is a run played by a hand: what the recording shows is what
    /// a thumb gets.
    /// </para>
    /// <para>
    /// <b>Null on every run.</b> Nothing on a rung, on the Infinite lane or in the tutorial
    /// calls any of this; it is reached from <c>ShowcaseScreen</c> alone, which nothing in the
    /// game opens since the advert's keys were deleted (2026-09-27).
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>Whether the field is at rest: no swap or cascade is being drawn.</summary>
        public bool AtRest => !Busy;

        /// <summary>
        /// Whether the hill says nothing: no count-in, no wave banner, no boss name, no chain
        /// caption and no forecast band.
        ///
        /// <b>For a recording that wants the fight and nothing written over it.</b> Every
        /// caption the board draws goes through one of four methods (<c>Announce</c>,
        /// <c>Chain</c>, <c>CountBeat</c>, <c>Foretell</c>) and each asks this first; the
        /// clocks and rules behind them are untouched, so a muted board plays exactly the run
        /// a spoken one does. False on every run.
        /// </summary>
        public bool Muted { get; set; }

        /// <summary>A drag from one cell onto its neighbour, exactly as a finger makes it.</summary>
        /// <returns>False when the board would refuse the move, in which case nothing happened.</returns>
        public bool Swipe(int cell, int other)
        {
            if (!Playable || _board == null) return false;
            if (!_board.Adjacent(cell, other) || !_board.Lines(cell, other)) return false;

            int dx = other % Width - cell % Width;
            int dy = cell / Width - other / Width;          // screen up is a lower row

            Drag(cell, new Vector2Int(dx, dy));
            return true;
        }

        /// <summary>A tap on a turret's chassis. See <c>Unleashed</c>.</summary>
        public void TapWard(int ward) => Unleashed(ward);

        /// <summary>A tap on a cog lying on the hill. See <c>Grabbed</c>.</summary>
        public void TapCog(int cogId) => Grabbed(cogId);

        /// <summary>A tap on a live bomb. See <c>Tapped</c>.</summary>
        public void TapBomb(int bombId) => Tapped(bombId);

        /// <summary>A tap on the rubble over a turret. See <c>Digging</c>.</summary>
        public void TapHeap(int seat) => Digging(seat);

        /// <summary>The turret's node, or null.</summary>
        public RectTransform WardAt(int seat)
        {
            if (_posts == null || seat < 0 || seat >= _posts.Length) return null;
            var post = _posts[seat];
            return post == null ? null : post.Node;
        }

        /// <summary>The rubble over a turret, or null while it is clear.</summary>
        public RectTransform HeapAt(int seat)
        {
            if (_posts == null || seat < 0 || seat >= _posts.Length) return null;
            var post = _posts[seat];
            if (post == null || post.Heap == null || !post.Heap.gameObject.activeSelf) return null;
            return post.Heap;
        }

        /// <summary>A cog's node, or null.</summary>
        public RectTransform CogAt(int cogId)
        {
            var gear = GearOf(cogId);
            return gear == null ? null : gear.Node;
        }

        /// <summary>A bomb's node, or null.</summary>
        public RectTransform BombAt(int bombId)
        {
            var fuse = FuseOf(bombId);
            return fuse == null ? null : fuse.Node;
        }

        /// <summary>
        /// Re-dresses every gem off the board, without moving one.
        ///
        /// <b>For a charm stood on the field between moves</b> (<c>SiegeShowcase.Plant</c>): the
        /// board knows the cell carries one and the picture does not until it is dressed again,
        /// which is what <c>Repaint</c> does - but <c>Repaint</c> also seats every gem, and a
        /// field mid-cascade has gems in the air. Refused while the field is not settled.
        /// </summary>
        public bool Restyle()
        {
            if (Busy || _board == null) return false;

            for (int i = 0; i < _gems.Count && i < Width * Height; i++)
                if (_gems[i] != null) Dress(i);

            return true;
        }
    }
}
