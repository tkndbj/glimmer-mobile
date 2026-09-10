using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where everything is: the three bands, a lane's x, a post's x, and how far down the hill a
    /// march reading sits.
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ geometry
        /// <summary>
        /// The cell, driven by the width and capped by what the hill can spare.
        ///
        /// <b>The width leads.</b> Taking the smaller of the two meant the height always won and
        /// the field was a column in the middle of a full-width plate, which is the one thing on
        /// this screen that had no reason to be inset. See <see cref="MaxGemBand"/> for what caps
        /// it, and `Compose` for how the hill and the line then share what is left.
        /// </summary>
        protected override float Fit(Vector2 room)
        {
            _room = room;

            float wide = (room.x - Margin * 2f) / Width;
            float tall = (room.y - Margin * 2f) * MaxGemBand / Height;
            return Mathf.Min(wide, tall);
        }

        protected override Vector2 Span
            => new Vector2(Mathf.Max(Cell * Width, _room.x - Margin * 2f),
                           Mathf.Max(Cell * Height, _room.y - Margin * 2f));

        protected override Vector2 CentreOf(int index)
        {
            int x = index % Width, y = index / Width;
            return new Vector2((x - (Width - 1) * .5f) * Cell,
                               _gemCentre + ((Height - 1) * .5f - y) * Cell);
        }

        /// <summary>Where a lane sits across the hill.</summary>
        float LaneX(int lane)
        {
            // **Inset the way `PostX` already insets the ward line, and a render is what said so.**
            // Divided by the lane count flat, the outer lanes put a raider's *centre* four tenths
            // of the board from the middle - which is fine for a body drawn in a square and is not
            // what this hill carries: the reels are cut to a fixed height and whatever width the
            // animation's box came out as, so the widest of them is two thirds wider than it is
            // tall. Drawn in lane nought or lane four it hangs over the plate, and the thing that
            // goes over the edge is whatever the pack drew furthest from the body - which on a
            // bulwark is the shield, the one part of it the mechanic is about.
            float wide = Span.x / (SiegeTuning.Lanes + .6f);
            return (lane - (SiegeTuning.Lanes - 1) * .5f) * wide;
        }

        /// <summary>Where a ward stands on the line.</summary>
        float PostX(int index)
        {
            int n = _posts != null ? _posts.Length : 1;
            float wide = Span.x / (n + .6f);
            return (index - (n - 1) * .5f) * wide;
        }

        /// <summary>How far down the hill a raider has come.</summary>
        float MarchY(float march) => Mathf.Lerp(_hillTop, _hillFoot, Mathf.Clamp01(march));

        // ------------------------------------------------------------------ the aiming grid
        /// <summary>
        /// Where the middle of one box of the hill's aiming grid sits.
        ///
        /// <para>
        /// <b>The one arithmetic, and it is written in terms of <see cref="MarchY"/> rather than
        /// beside it.</b> The panes a player taps and the boxes <c>SiegeBoard.Blast</c> reads have
        /// to be the same twenty rectangles — invariant 33g — and the cheapest way to guarantee
        /// that is for the drawing to be a function of the mapping the rule uses, so there is
        /// nothing left to agree about.
        /// </para>
        /// <para>
        /// <b>It was two, and both differences were silent.</b> The panes were laid out from
        /// <c>_hillTop + Cell * .35f</c> downward, where <c>march</c> nought is <c>_hillTop</c>
        /// exactly, so every row boundary on the screen sat up to a third of a cell above the one
        /// the rule read — a raider near a boundary was genuinely in the band above the box it
        /// looked like it was in. And the panes were <c>Span.x / Lanes</c> wide while their centres
        /// were spaced on <see cref="LaneX"/>'s inset pitch of <c>Span.x / (Lanes + .6f)</c>, so
        /// each one overlapped its neighbour by about a tenth of its width and the later sibling —
        /// the higher lane — won every tap in the seam. Reported from play as tapping a raider and
        /// being told nothing was there.
        /// </para>
        /// <para>
        /// <b>The grid is spaced flat and the raiders are inset, deliberately.</b> A raider's x is
        /// pulled in (see <see cref="LaneX"/>) so that a wide reel does not hang off the plate;
        /// the grid is not, because a grid must tile the board it is drawn over — an inset one
        /// would leave a strip down each edge that belongs to no box, and a tap there would fall
        /// through the targeting layer onto the gems underneath. Every lane's inset centre still
        /// lands inside its own flat box, so nothing is misfiled by the difference.
        /// </para>
        /// </summary>
        Vector2 BoxAt(int lane, int row)
            => new Vector2((lane - (SiegeTuning.Lanes - 1) * .5f) * BoxWide,
                           MarchY((row + .5f) / SiegeTuning.BlastRows));

        /// <summary>How wide one box of the aiming grid is. They abut; they do not overlap.</summary>
        float BoxWide => Span.x / SiegeTuning.Lanes;

        /// <summary>How deep one box of the aiming grid is.</summary>
        float BoxTall => (MarchY(0f) - MarchY(1f)) / SiegeTuning.BlastRows;

        /// <summary>
        /// Where a ward's fuel tube sits, as a board coordinate rather than a ward's own.
        ///
        /// <para>
        /// <b>Sitting on the top edge of the field's plate, in the strip under the plinths.</b>
        /// Worked out from the plate rather than typed as an offset from the turret, because that
        /// edge is what a player reads it against — and because both the cell and the way the bands
        /// divide move with the screen (see <c>Compose</c>), so a typed number is right on one
        /// phone and wrong on the next.
        /// </para>
        /// <para>
        /// <b>The plinths and the plate overlap</b>, which is why this cannot be the middle of a
        /// gap: on every screen this mode has been drawn at, the foot of a turret is already behind
        /// the field. What there is instead is the band immediately above the plate's edge, which
        /// is empty on every board and is directly over the gems whose colour fills it — the two
        /// halves of the decision this mode asks, one above the other.
        /// </para>
        /// </summary>
        float TubeY
        {
            get
            {
                float plate = _gemCentre + (Cell * Height + Cell * .34f) * .5f;
                // .15 rather than .22: a device said the bar sat a few pixels high of where it
                // belongs, which is as close to the plate's edge as it can be without the tube's
                // own trough overlapping it.
                return plate + Cell * .15f;
            }
        }
    }
}
