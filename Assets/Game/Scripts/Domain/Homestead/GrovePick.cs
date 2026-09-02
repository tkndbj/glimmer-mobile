using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// One stand's drawn box, in floor space, and where inside it the picture actually is.
    ///
    /// <para>
    /// A tile is a diamond on the ground and a piece standing on it is a tall rectangle rising
    /// out of that diamond, so the two are nowhere near the same shape. A lantern's post and
    /// flame are drawn several tile-heights above the ground the lantern is standing on, and
    /// they are what the player sees and aims at. The box says where the rectangle is; the mask
    /// says which parts of it are lantern rather than air. <see cref="Col"/> and
    /// <see cref="Row"/> name the stand's <em>anchor</em>, which for a two-wide house is its
    /// back corner — the tile the save file holds.
    /// </para>
    /// <para>
    /// Y grows upward here, the field's own convention, while a mask is read from the top of
    /// the picture down; <see cref="Contains"/> does that one conversion so nobody else has to.
    /// </para>
    /// </summary>
    public readonly struct GroveHit
    {
        public readonly int Col, Row;
        public readonly float CentreX, CentreY, HalfWidth, HalfHeight;
        public readonly int Depth;
        public readonly GroveHitMask Mask;
        public readonly bool Flipped;

        /// <summary>A single-tile box with no mask: what every piece was before masks existed. For tests.</summary>
        public GroveHit(int col, int row, float centreX, float centreY,
                        float halfWidth, float halfHeight)
            : this(col, row, centreX, centreY, halfWidth, halfHeight,
                   GroveFootprint.Single.Depth(col, row), GroveHitMask.None, false)
        {
        }

        public GroveHit(int col, int row, float centreX, float centreY,
                        float halfWidth, float halfHeight, int depth,
                        GroveHitMask mask, bool flipped)
        {
            Col = col;
            Row = row;
            CentreX = centreX;
            CentreY = centreY;
            HalfWidth = halfWidth < 0f ? 0f : halfWidth;
            HalfHeight = halfHeight < 0f ? 0f : halfHeight;
            Depth = depth;
            Mask = mask;
            Flipped = flipped;
        }

        public bool IsDrawn => HalfWidth > 0f && HalfHeight > 0f;

        /// <summary>Whether the box covers the point at all — the cheap half of <see cref="Contains"/>.</summary>
        public bool Boxes(float x, float y)
            => IsDrawn
            && x >= CentreX - HalfWidth && x <= CentreX + HalfWidth
            && y >= CentreY - HalfHeight && y <= CentreY + HalfHeight;

        /// <summary>
        /// Whether the picture, not merely its rectangle, is under the point — within
        /// <see cref="GrovePick.TouchSlop"/> of it, which is a distance on the floor and so the
        /// same forgiveness on a ladder as on a house.
        /// </summary>
        public bool Contains(float x, float y)
        {
            if (!Boxes(x, y)) return false;
            if (!Mask.IsSet) return true;

            float u = (x - (CentreX - HalfWidth)) / (HalfWidth * 2f);
            float v = ((CentreY + HalfHeight) - y) / (HalfHeight * 2f);

            return Mask.Hits(u, v, Flipped,
                             GrovePick.TouchSlop / (HalfWidth * 2f),
                             GrovePick.TouchSlop / (HalfHeight * 2f));
        }
    }

    /// <summary>
    /// Which stand the player meant, when what they touched was a piece rather than the ground.
    ///
    /// <para>
    /// <b>Why this exists.</b> Taps used to resolve by inverting the isometric transform: a
    /// screen point became a point on the ground plane, and the tile containing it was the
    /// answer. That is exactly right for bare ground and wrong for everything standing on it.
    /// Pressing a lantern's body did nothing, because the body is drawn above the diamond it
    /// belongs to — the player had to find the patch of grass under it, which on a floor of
    /// dense props is a patch they often cannot see at all. Reported from play as "it doesn't
    /// detect the object".
    /// </para>
    /// <para>
    /// <b>The rule is: whatever is drawn over the point, frontmost wins.</b> Frontmost by
    /// <see cref="GroveHit.Depth"/> — the same order the field paints in — which is what makes
    /// the answer agree with the picture by construction rather than by coincidence. A piece's
    /// art rises <em>up</em> the screen, which in an isometric projection is <em>backwards</em>,
    /// so a tall tree covers the tiles behind it and its depth is greater than theirs. Both
    /// halves of that follow: the tree wins the tap, and the ground it hides cannot be tapped
    /// through — which is correct, because it cannot be seen either.
    /// </para>
    /// <para>
    /// <b>"Drawn over" means the picture, and the second report is why.</b> The first version
    /// tested boxes, and an oak's box is nine tiles of air around a trunk; the ground beside it
    /// could not be reached at all. Each hit now carries its piece's <see cref="GroveHitMask"/>,
    /// so a tap between the trunk and the canopy's edge lands on the grass that is visibly
    /// there. Boxes rather than pixels still: a mask is 32 bytes a piece where a readable
    /// texture is a megabyte, and the tolerance in <see cref="GroveHitMask.Hits"/> is what
    /// keeps a thin post from being a miss.
    /// </para>
    /// </summary>
    public static class GrovePick
    {
        /// <summary>
        /// How far from the painted part of a piece a touch may land and still be on it, in
        /// floor pixels — about twenty screen pixels at the opening zoom, a fingertip's error.
        ///
        /// A distance rather than a count of mask cells, because a cell is a fixed size in
        /// <em>art</em> pixels and a piece's art is drawn at its own scale: the same number of
        /// cells would forgive a hand-sized error on a boulder and a hair's breadth on a torch.
        /// </summary>
        public const float TouchSlop = 26f;

        /// <summary>
        /// The frontmost stand whose picture covers the point, or false when nothing does — in
        /// which case the caller should fall back to the ground the point lands on. Answers the
        /// stand's <em>anchor</em>.
        /// </summary>
        public static bool Topmost(IReadOnlyList<GroveHit> drawn, float x, float y,
                                   out int col, out int row)
        {
            col = 0;
            row = 0;

            if (drawn == null) return false;

            bool found = false;
            int best = 0;

            for (int i = 0; i < drawn.Count; i++)
            {
                var hit = drawn[i];

                // Strictly greater, so an exact tie keeps the first — a depth is unique per
                // stand, so a tie means the same stand listed twice and either answer is the
                // same answer. Ordered before the mask test, because the mask is the dear half.
                if (found && hit.Depth <= best) continue;
                if (!hit.Contains(x, y)) continue;

                best = hit.Depth;
                col = hit.Col;
                row = hit.Row;
                found = true;
            }

            return found;
        }
    }
}
