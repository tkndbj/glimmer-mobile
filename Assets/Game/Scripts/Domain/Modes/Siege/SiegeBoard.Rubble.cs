using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The player's half of a burial: digging a ward out from under a colossus's boulder.
    ///
    /// <para>
    /// <b>Its own file because it is the one thing on the line the player does with their
    /// hands.</b> Everything else the line does is a consequence of the field — fuel arrives, a
    /// bolt leaves, a charge is tapped — and a tap on rubble is none of those: it moves no fuel,
    /// throws nothing and costs the run no match. What it costs is the beat it took, which is
    /// the resource a colossus is built to take (<see cref="SiegeKind.Colossus"/>).
    /// </para>
    /// <para>
    /// <b>Refused rather than counted on a clear post</b>, for <see cref="Overcharge"/>'s reason:
    /// a tap that does nothing has to say so, or the view draws a dig on a post that was never
    /// buried and the player learns that tapping turrets is a thing.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Takes one piece of rubble off <paramref name="ward"/>, answering whether one came off.
        ///
        /// <b>Answered on the call and never on the next report</b>, which is
        /// <see cref="Overcharge"/>'s shape and had to be: a tap arrives between two steps of the
        /// clock, and <c>Advance</c> clears the report on its way in, so a record written here
        /// would be gone before the view read it. The view draws the piece leaving off this
        /// answer, and a tap on a post whose last piece went a frame earlier is refused here and
        /// drawn as a refusal there.
        /// </summary>
        public bool Dig(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return false;

            var post = _wards[ward];
            if (!post.Alive || !post.Dig()) return false;

            Attention.Dug(post.Rubble == 0);
            return true;
        }

        /// <summary>Whether any ward on the line is under rubble right now.</summary>
        public bool AnyBuried
        {
            get
            {
                for (int w = 0; w < _wards.Length; w++)
                    if (_wards[w].Buried) return true;

                return false;
            }
        }
    }
}
