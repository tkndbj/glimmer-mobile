namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The revive (<c>UtilityKind.Revive</c>): one fallen turret stood back up at full health,
    /// inside a barrier that turns away every hit for a moment.
    ///
    /// <para>
    /// <b>The phoenix's rise (<see cref="Rise"/>) cut down to one post.</b> The post stands up
    /// exactly as a continue raises one (<see cref="StandUp"/>), and the cover is the mantle's
    /// own rule on a clock the post carries (<see cref="SiegeWard.Barrier"/>), asked through the
    /// door every hit already asks (<see cref="Turned"/>) - so a shielded post is never hurt,
    /// doused, chained, sealed or sundered, and the view draws the same shell.
    /// </para>
    /// <para>
    /// <b>It never raises a line that has fallen whole</b>: a run with nothing standing is over,
    /// and only a continue answers that (invariant 23). So it is refused unless the board still
    /// has a move (<see cref="AnyMove"/>) and the post it is aimed at is down. It hurts nothing
    /// and draws from neither random stream (41), so it is charged no matches (39).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>Whether post <paramref name="ward"/> is down, which is all a revive may be aimed at.</summary>
        public bool Fallen(int ward)
            => ward >= 0 && ward < _wards.Length && !_wards[ward].Alive;

        /// <summary>Whether any post on the line is down. Asked before a revive is armed.</summary>
        public bool AnyFallen
        {
            get
            {
                for (int w = 0; w < _wards.Length; w++)
                    if (!_wards[w].Alive) return true;

                return false;
            }
        }

        /// <summary>
        /// Stands fallen post <paramref name="ward"/> back up at full health with a barrier of
        /// <paramref name="tenths"/> tenths of a second, and answers whether it did. Refused for
        /// a standing post, a run already over, or a barrier of nothing.
        /// </summary>
        public bool Restore(int ward, int tenths)
        {
            if (tenths <= 0 || !AnyMove || !Fallen(ward)) return false;

            var post = _wards[ward];

            StandUp(post);
            post.Barrier = tenths / 10f;

            return true;
        }
    }
}
