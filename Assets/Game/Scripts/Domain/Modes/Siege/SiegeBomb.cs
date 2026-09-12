namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A live bomb standing on the hill, left where a bomber died.
    ///
    /// <para>
    /// <b>A box rather than a point, and that is the whole of what makes it tappable.</b> The hill
    /// is already divided into <c>SiegeTuning.Lanes</c> by <c>SiegeTuning.BlastRows</c> boxes for a
    /// firepot's targeting, and a bomb is aimed at by the same finger against the same grid — so
    /// it is stored as one of those boxes and every reader agrees about where it is. A raw march
    /// would leave the drawing and the tap test each doing their own arithmetic on it, which is
    /// exactly the disagreement invariant 39k records.
    /// </para>
    /// <para>
    /// <b>It carries the colour of the raider that dropped it</b>, and that is for the drawing
    /// alone: a bomb takes the same off everything whatever colour it wears, because it is a
    /// firepot rather than a bolt. What the colour buys is a player being able to see which of the
    /// things that just died left it.
    /// </para>
    /// </summary>
    public readonly struct SiegeBomb
    {
        public readonly int Id, Lane, Row, Colour;

        public SiegeBomb(int id, int lane, int row, int colour)
        {
            Id = id;
            Lane = lane;
            Row = row;
            Colour = colour;
        }
    }
}
