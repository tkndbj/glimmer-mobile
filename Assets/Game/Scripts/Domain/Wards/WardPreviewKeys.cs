namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which keys the turret preview panel offers, for a turret in a given state.
    ///
    /// <para>
    /// <b>A named rule rather than a branch, because the branch left a state with no answer.</b>
    /// The panel offered one key and decided what it said: for a turret the player owned it sold
    /// the next star, and only fell back to standing it when there was none left to sell. Every
    /// turret starts at one star, so there was always a star left to sell — which meant a turret
    /// somebody owned and had not stood offered <c>UPGRADE</c> and nothing else, and the loadout's
    /// whole purpose was unreachable.
    /// </para>
    /// <para>
    /// <b>Nothing could see it.</b> Both branches are individually correct and every gate reads
    /// one branch at a time; what was wrong is that their union does not cover the states a
    /// turret can be in. That is only visible as a <em>property</em> — <c>WardPreviewTests</c>
    /// sweeps all eight combinations and asserts the two that matter: a key is always offered, and
    /// a turret the player owns can always be put on the line.
    /// </para>
    /// </summary>
    public readonly struct WardPreviewKeys
    {
        /// <summary>Whether the upper key is drawn: the price, the star, or the wall.</summary>
        public readonly bool Upper;

        /// <summary>Whether the lower key is drawn: where this turret stands on the line.</summary>
        public readonly bool Lower;

        /// <summary>
        /// Whether the lower key is the one state that is not an offer.
        ///
        /// EQUIPPED pays nothing, moves nothing and only closes the panel, so it wears the settled
        /// pill rather than the price one — see <c>WardPreviewOverlay.Paint</c>.
        /// </summary>
        public readonly bool Equipped;

        WardPreviewKeys(bool upper, bool lower, bool equipped)
        {
            Upper = upper;
            Lower = lower;
            Equipped = equipped;
        }

        /// <summary>Whether only one key is drawn, and so takes the middle of the band.</summary>
        public bool Alone => Upper ^ Lower;

        /// <summary>
        /// What a turret in this state offers.
        ///
        /// <para>
        /// <paramref name="held"/> is whether the player owns it <em>on this seat</em>,
        /// <paramref name="standing"/> whether it is on the line already, and
        /// <paramref name="rises"/> whether it has a star left that could be bought.
        /// </para>
        /// <para>
        /// <b>A turret nobody owns has one key</b> — a price, or the wall in front of it — because
        /// there is nothing to stand and nothing to upgrade. <b>A turret somebody owns always has
        /// the lower one</b>, which is the clause that was missing: it is either the way onto the
        /// line or the statement that it is already there, and it does not depend on whether a
        /// star happens to be for sale.
        /// </para>
        /// <para>
        /// <b>There was a fourth state for three days and it is gone with the copy rule.</b> While
        /// a legendary was bought outright it was held on seats it could not be stood on, so the
        /// lower key had to be able to sell the copy that would put it there (invariant 42k). A
        /// turret is bought per seat again, so <em>held</em> means <em>standable</em> for the
        /// whole shelf and the panel is back to three answers. The property
        /// <c>WardPreviewTests</c> sweeps is unchanged: every state offers a key, and no held
        /// state offers one that does nothing.
        /// </para>
        /// </summary>
        public static WardPreviewKeys For(bool held, bool standing, bool rises)
            => !held ? new WardPreviewKeys(true, false, false)
             : new WardPreviewKeys(rises, true, standing);
    }
}
