namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The two things a <em>scripted</em> board may do to its own line, and nothing else.
    ///
    /// <para>
    /// <b>They live on the board because the board owns the line.</b> Both of these began as a
    /// screen reaching across and writing <c>SiegeWard</c>'s fields — which compiled, worked, and
    /// put the one type that decides what a turret is at the mercy of anything holding a
    /// reference to it. Everything a ward can have done to it goes through a method on the ward
    /// (<c>Fill</c>, <c>Snuff</c>, <c>Stoke</c>, <c>Drain</c>, <c>Shackle</c>), and everything
    /// done to the <em>line</em> goes through one here (<c>Rally</c>, and these).
    /// </para>
    /// <para>
    /// <b>Neither is reachable from a graded run.</b> The tutorial is the only caller, the
    /// guarantee that it cannot be lost is <see cref="Sheltered"/> rather than anything below,
    /// and nothing here is wired to a utility, a charm or a chest — so no board a player is
    /// scored on can reach either of them (invariant 39's rule about what may move a run).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Pours fuel into one ward, exactly as a match landing does. Answers whether that
        /// banked a charge.
        ///
        /// <para>
        /// <b>Through <c>SiegeWard.Fill</c> and never around it</b>, so a tube filled by a script
        /// is filled by the same door a match fills it through: the cap on held charges, the
        /// carried overflow and the toll a sunlord's seal counts are all that method's, and a
        /// caller that set <c>Fuel</c> itself would silently miss every one of them.
        /// </para>
        /// <para>
        /// <b>It is a pour rather than a set, so a caller can drip it.</b> The whole point of the
        /// tutorial's tube filling over half a second rather than in a frame is that the player
        /// sees their own match arrive as fuel, and a method that could only say "full" would
        /// make that impossible to draw.
        /// </para>
        /// </summary>
        public bool Pour(int ward, float fuel)
        {
            if (ward < 0 || ward >= _wards.Length || fuel <= 0f) return false;

            var post = _wards[ward];
            return post.Alive && post.Fill(fuel);
        }

        /// <summary>
        /// How much more fuel this ward needs before its tube brims, or nought when it cannot
        /// take any.
        ///
        /// <b>Not <see cref="RoomForFuel"/>, which is a different question.</b> That one answers
        /// what a surge may be sold — every charge this ward has not banked yet, so a nearly-full
        /// tube is still worth buying fuel for. This answers what it takes to bank the
        /// <em>next</em> one, which is what something drawing a tube filling has to know.
        /// </summary>
        public float ToBrim(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0f;

            var post = _wards[ward];
            if (!post.Alive) return 0f;

            float room = post.Capacity - post.Fuel;
            return room > 0f ? room : 0f;
        }

        /// <summary>
        /// Fills every standing ward's tube and banks nothing: the tutorial's closing sweep.
        ///
        /// <para>
        /// <b>It is what makes that ending certain rather than likely.</b> An overcharge throws a
        /// blast, and a blast is a shape — whatever it catches dies and whatever it misses walks
        /// on — so a tutorial that ended when the hill happened to be empty would be one that
        /// sometimes never ended. Once the charge has been thrown the grove answers with
        /// everything it has, and the run then finishes on <see cref="IsFinished"/> exactly as
        /// every siege does.
        /// </para>
        /// <para>
        /// Through <c>SiegeWard.Stoke</c>, which is fuel and never a charge — see there.
        /// </para>
        /// </summary>
        public void Kindle()
        {
            for (int i = 0; i < _wards.Length; i++) _wards[i].Stoke();
        }
    }
}
