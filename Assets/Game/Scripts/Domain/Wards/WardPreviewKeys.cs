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

        /// <summary>
        /// Whether the lower key <em>buys</em> rather than equips: every copy the player owns is
        /// already standing somewhere else, so the way onto this seat is another one.
        ///
        /// <para>
        /// <b>The state the copy rule adds, and it is the one the panel could not otherwise
        /// answer.</b> A colourless turret is held on every seat by one purchase
        /// (<c>WardHolding.Row</c>) and may stand in only as many places as have been paid for
        /// (<c>WardLedger.Copies</c>) — so a player owning one Eclipse and tapping a second seat
        /// is holding a turret they cannot equip. EQUIP over that is a button that does nothing,
        /// which is the exact fault <see cref="For"/> exists to have caught once already.
        /// </para>
        /// <para>
        /// <b>It is a price and therefore never <see cref="Equipped"/></b>, and the two can never
        /// both be set: a seat already standing the turret is not asking for another copy.
        /// </para>
        /// </summary>
        public readonly bool Buys;

        WardPreviewKeys(bool upper, bool lower, bool equipped, bool buys = false)
        {
            Upper = upper;
            Lower = lower;
            Equipped = equipped;
            Buys = buys;
        }

        /// <summary>Whether only one key is drawn, and so takes the middle of the band.</summary>
        public bool Alone => Upper ^ Lower;

        /// <summary>
        /// What a turret in this state offers.
        ///
        /// <para>
        /// <paramref name="held"/> is whether the player owns it, <paramref name="standing"/>
        /// whether it is on the line already, <paramref name="rises"/> whether it has a star
        /// left that could be bought, and <paramref name="spare"/> whether a copy of it is free
        /// to be put on this seat (<c>WardLoadout.CanStand</c>).
        /// </para>
        /// <para>
        /// <b>A turret nobody owns has one key</b> — a price, or the wall in front of it — because
        /// there is nothing to stand and nothing to upgrade. <b>A turret somebody owns always has
        /// the lower one</b>, which is the clause that was missing: it is either the way onto the
        /// line, the statement that it is already there, or the price of the copy that would put
        /// it there — and it does not depend on whether a star happens to be for sale.
        /// </para>
        /// <para>
        /// <b>The third of those is what copies cost this rule.</b> Owning a turret stopped
        /// meaning it can be stood anywhere, so "a turret the player owns can always be put on
        /// the line" became "…can always be put on the line, or told what that would cost" — and
        /// the property <c>WardPreviewTests</c> sweeps is the one that still holds: every state
        /// offers a key, and no held state offers a key that does nothing.
        /// </para>
        /// </summary>
        public static WardPreviewKeys For(bool held, bool standing, bool rises, bool spare)
            => !held ? new WardPreviewKeys(true, false, false)
             : standing || spare ? new WardPreviewKeys(rises, true, standing)
             : new WardPreviewKeys(rises, true, false, buys: true);
    }
}
