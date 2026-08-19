using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// One tile's drawn box, in floor space: where its art actually covers the screen.
    ///
    /// <para>
    /// A tile is a diamond on the ground and a piece standing on it is a tall rectangle rising
    /// out of that diamond, so the two are nowhere near the same shape. A lantern's post and
    /// flame are drawn several tile-heights above the ground the lantern is standing on, and
    /// they are what the player sees and aims at.
    /// </para>
    /// </summary>
    public readonly struct GroveHit
    {
        public readonly int Col, Row;
        public readonly float CentreX, CentreY, HalfWidth, HalfHeight;

        public GroveHit(int col, int row, float centreX, float centreY,
                        float halfWidth, float halfHeight)
        {
            Col = col;
            Row = row;
            CentreX = centreX;
            CentreY = centreY;
            HalfWidth = halfWidth < 0f ? 0f : halfWidth;
            HalfHeight = halfHeight < 0f ? 0f : halfHeight;
        }

        public bool IsDrawn => HalfWidth > 0f && HalfHeight > 0f;

        public bool Contains(float x, float y)
            => IsDrawn
            && x >= CentreX - HalfWidth && x <= CentreX + HalfWidth
            && y >= CentreY - HalfHeight && y <= CentreY + HalfHeight;
    }

    /// <summary>
    /// Which tile the player meant, when what they touched was a piece rather than the ground.
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
    /// <see cref="GroveFloor.DrawOrder"/> — the same order the field paints in — which is what
    /// makes the answer agree with the picture by construction rather than by coincidence. A
    /// piece's art rises <em>up</em> the screen, which in an isometric projection is
    /// <em>backwards</em>, so a tall tree covers the tiles behind it and its draw order is
    /// higher than theirs. Both halves of that follow: the tree wins the tap, and the ground it
    /// hides cannot be tapped through — which is correct, because it cannot be seen either.
    /// </para>
    /// <para>
    /// <b>Boxes rather than pixels, deliberately.</b> Testing the sprite's alpha would be more
    /// exact and costs a readable copy of every texture in the grove — a CPU-side duplicate of
    /// a hundred and sixty props, permanently resident, to sharpen the edges of a tap. The art
    /// here is cropped tight by the pack it came from, so a box is close to the silhouette, and
    /// the frontmost rule already resolves the overlaps that matter.
    /// </para>
    /// </summary>
    public static class GrovePick
    {
        /// <summary>
        /// The frontmost tile whose art covers the point, or false when nothing does — in which
        /// case the caller should fall back to the ground the point lands on.
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
                if (!hit.Contains(x, y)) continue;

                int order = GroveFloor.DrawOrder(hit.Col, hit.Row);

                // Strictly greater, so an exact tie keeps the first — DrawOrder is unique per
                // tile (GroveFloor pins that), so a tie means the same tile listed twice and
                // either answer is the same answer.
                if (found && order <= best) continue;

                best = order;
                col = hit.Col;
                row = hit.Row;
                found = true;
            }

            return found;
        }
    }
}
