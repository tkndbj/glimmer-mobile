using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What a build does that the board has to be seen doing: a post standing back up.
    ///
    /// <para>
    /// <b>One drawing, because a build changes numbers and numbers are already drawn.</b> A
    /// heavier bolt is a bigger figure, a granted splash is the splash the shelf's turret
    /// already draws, a wave's stop is the hourglass's own wave, a banked charge is the glyph
    /// lighting - every one of those reaches the screen through a report the view already
    /// reads. The one thing a build does that no shipped turret does is stand a fallen post
    /// back up (<c>SiegeReport.Revived</c>), and that is drawn here with the gesture a continue
    /// already uses (<see cref="Rallied"/>), a beat after the fall so the fall is seen.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>Seconds between a post's fall being drawn and its rise. Long enough to read as two events.</summary>
        const float RiseAfter = .55f;

        /// <summary>
        /// Draws a build's revival: the fall has just been drawn off the blow or spell that did
        /// it, so the rise waits a beat and then raises every post the model says is standing
        /// again. <see cref="Rallied"/> reads the board at the moment it runs, which is what
        /// makes the wait safe - a post the board has since taken down again is left down.
        /// </summary>
        void Revived()
        {
            Tween.After(RiseAfter, () => { if (this) Rallied(); }, this);
        }
    }
}
