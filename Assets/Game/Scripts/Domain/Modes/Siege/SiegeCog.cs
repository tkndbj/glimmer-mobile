namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A cog lying on the hill: what a felled raider leaves behind, and what a rank is bought
    /// with.
    ///
    /// <para>
    /// <b>A class rather than a struct, because a cog has a clock.</b> Everything else this hill
    /// carries is a fact that does not change once it exists — a bomb sits where it fell for ever
    /// — and a cog is trampled if it is not taken (<see cref="SiegeTuning.CogLies"/>). A struct in
    /// a list would have to be read out, edited and written back on every step, which is three
    /// places for the copy to be the thing that ages.
    /// </para>
    /// <para>
    /// <b>It names the ward it pays rather than the ward that killed it</b>, and the two are the
    /// same thing said once instead of threaded through four call sites. Under the colour lock a
    /// raider can only ever be brought down by the ward its own colour feeds, so the killer is
    /// derivable from the corpse — which also gives the right answer for the kills no ward made
    /// at all (a firepot, a storm, a bomb). What it says to the player is the loop this whole
    /// mode is about: <em>the colour you fed is the colour that pays you</em>.
    /// </para>
    /// </summary>
    public sealed class SiegeCog
    {
        /// <summary>Its own id, minted from the board's counter so it cannot collide with a bomb's.</summary>
        public readonly int Id;

        /// <summary>Where it lies, in the hill's own targeting grid. See <c>SiegeTuning.RowOf</c>.</summary>
        public readonly int Lane, Row;

        /// <summary>The ward it ranks up, and that ward's colour. Never negative.</summary>
        public readonly int Ward, Colour;

        /// <summary>Seconds before it is trampled. Counted down by <c>SiegeBoard.Age</c>.</summary>
        public float Left;

        public SiegeCog(int id, int lane, int row, int ward, int colour)
        {
            Id = id;
            Lane = lane;
            Row = row;
            Ward = ward;
            Colour = colour;
            Left = SiegeTuning.CogLies;
        }

        /// <summary>How much of its life is left, as a share. What the view fades it by.</summary>
        public float Share
            => SiegeTuning.CogLies <= 0f ? 0f : Left / SiegeTuning.CogLies;

        /// <summary>Whether it has started warning that it is going. See <c>SiegeTuning.CogFading</c>.</summary>
        public bool Fading => Left <= SiegeTuning.CogFading;
    }

    /// <summary>
    /// What taking a cog was worth: the ward it ranked, and the rank it reached.
    ///
    /// <b>A reading rather than a bool</b>, because the view has to say which turret went up and
    /// to what — and a caller handed only "it worked" would go back to the board to find out,
    /// which is a second read of a thing that has already changed.
    /// </summary>
    public readonly struct SiegeTaken
    {
        public readonly bool Landed;
        public readonly int Ward, Rank, Colour, Lane, Row;

        public SiegeTaken(int ward, int rank, int colour, int lane, int row)
        {
            Landed = true;
            Ward = ward;
            Rank = rank;
            Colour = colour;
            Lane = lane;
            Row = row;
        }

        /// <summary>Nothing was there, or the ward it named is already at the top of the ladder.</summary>
        public static SiegeTaken Refused => default;
    }
}
