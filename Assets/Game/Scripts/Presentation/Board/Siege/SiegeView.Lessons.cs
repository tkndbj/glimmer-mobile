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
    /// <summary>The one-off lessons this mode shows, and what each of them points at.</summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the lessons
        /// <summary>The gem a lesson about the verb rings: one whose ward is on the line.</summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null) return 0;

                for (int i = 0; i < Width * Height; i++)
                    if (_layout.WardOf(_board.At(i)) == 0) return i;

                return 0;
            }
        }

        /// <summary>
        /// The lesson about the line points at the field's own top row, which is as close as a
        /// cell anchor can get to the wards standing above it — <c>ProtoView</c>'s anchors are
        /// cells, and a tip pointing at nothing is worse than no tip.
        /// </summary>
        public override int FriendCell => Width / 2;

        RectTransform _wardAnchor;

        /// <summary>
        /// The cog, as a picture for a lesson panel to draw.
        ///
        /// <para>
        /// <b>A picture rather than a ring, and the ring was tried first.</b> Pointing at a cog on
        /// the field is the obvious lesson and it cannot be relied on: a cog is dealt at a rate
        /// rather than authored (<see cref="SiegeLayout.Cogs"/>), so a rung can legitimately open
        /// with none standing - and a lesson is offered once in a player's life, so one that waits
        /// for a board that may never come may never be given at all.
        /// </para>
        /// <para>
        /// So the ring stays on a turret, which is always there and is what a cog is <em>for</em>,
        /// and the thing a ring cannot say - what the object actually looks like - is said by
        /// drawing it. See <c>Lesson.Icon</c>.
        /// </para>
        /// </summary>
        public Sprite CogArt => Piece("gem_cog");

        /// <summary>
        /// Something for the cog lesson to ring, and it is the <em>line</em> rather than a cell.
        ///
        /// <para>
        /// A cog is a thing on the field, so the obvious anchor is the cog — and it is wrong,
        /// because a cog is dealt and may not be standing anywhere when the lesson goes up. What
        /// the lesson is about is which turret an upgrade goes to, so the honest thing to point at
        /// is the turret it would go to, and the middle of the line is the one place on it that is
        /// on every board however many wards it holds.
        /// </para>
        /// <para>
        /// Made once and kept, for <c>ProtoView.AnchorAt</c>'s reason: the lessons are asked again
        /// on every readout change, and the node handed back is the one the tip is already ringing.
        /// </para>
        /// </summary>
        public RectTransform WardAnchor
        {
            get
            {
                if (_wardAnchor != null) return _wardAnchor;
                if (_wall == null || _posts == null || _posts.Length == 0) return null;

                _wardAnchor = UIKit.Node("WardAnchor", _wall);
                _wardAnchor.anchorMin = _wardAnchor.anchorMax = new Vector2(.5f, .5f);
                _wardAnchor.sizeDelta = new Vector2(Cell * 1.8f, Cell * 2.3f);
                _wardAnchor.anchoredPosition =
                    new Vector2(PostX(_posts.Length / 2), _lineY);

                return _wardAnchor;
            }
        }
    }
}
