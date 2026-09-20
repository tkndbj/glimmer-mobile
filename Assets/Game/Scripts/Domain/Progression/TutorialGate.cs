using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// Whether this launch owes the player the opening tutorial, and what closes it.
    ///
    /// <para>
    /// <b>Nothing new is stored, and that is the design rather than a saving.</b> A "has seen the
    /// tutorial" flag would be a new field in <c>SaveFileDto</c>, which is a schema bump, a delta,
    /// a Firestore mapper both ways and a line in <c>hasOnly</c> deployed before the client
    /// (invariants 12 and 12a) — for one bit. <c>TipLedger</c> is already a union-joined set of
    /// permanent ids recording <em>what this person has been shown</em>, already on the wire,
    /// already bounded, already merged across devices. The tutorial is two lessons, so the two
    /// lessons are what is written down.
    /// </para>
    /// <para>
    /// <b>They are the level's own two lessons, and that is what removes them from the level.</b>
    /// <see cref="Verb"/> is <c>siege_fuel</c> — a match feeds the turret of its colour — and
    /// <see cref="Charge"/> is <c>siege_brim</c> — a full tube can be tapped. Those are exactly
    /// the two panels the first rung of Thornwatch used to raise, and exactly the two the
    /// tutorial teaches. Teaching them earlier is therefore not a duplicate to suppress: it is
    /// the same lesson, given in a better place, and the ledger already refuses a lesson twice.
    /// No id was minted and none was spent (invariant 5f), and <c>SiegeScreen</c> is untouched —
    /// a player who never meets this screen is still taught both on the board, exactly as before.
    /// </para>
    /// </summary>
    public static class TutorialGate
    {
        /// <summary>What a match is for. The tutorial's first panel.</summary>
        public static Mechanic Verb => Mechanic.SiegeFuel;

        /// <summary>That a full turret is a button. The tutorial's second panel.</summary>
        public static Mechanic Charge => Mechanic.SiegeBrim;

        /// <summary>
        /// Whether the splash should land on the tutorial rather than on the hub.
        ///
        /// <para>
        /// <b>Two clauses, and the second is about people who already own the game.</b> The
        /// ledger alone would put a tutorial in front of any long-standing player who happened
        /// never to have brimmed a tube, on the launch after they updated — so it is also asked
        /// whether they have ever opened a level at all. <c>PlayerProgress</c> writes a record
        /// the moment a run starts (<c>NoteOpened</c>), so an account with none has genuinely
        /// never played.
        /// </para>
        /// <para>
        /// <b>It reads <see cref="Charge"/> and not <see cref="Verb"/>, which is what makes an
        /// interrupted tutorial replay.</b> A player who force-quits between the two panels has
        /// been shown the first and not the second, and what they are owed is the tutorial again
        /// rather than half of one.
        /// </para>
        /// <para>
        /// One case is deliberately accepted: a veteran reinstalling meets this once, because a
        /// fresh install has neither a ledger nor a record until they sign back in. The cost is
        /// a screen with a skip button on it.
        /// </para>
        /// </summary>
        public static bool Owed => !TipLedger.HasSeen(Charge) && PlayerProgress.Records.Count == 0;

        /// <summary>
        /// Closes the gate: the tutorial has been given, finished or skipped.
        ///
        /// <para>
        /// <b>Both, always, however the screen ended.</b> <c>TipOverlay</c> marks a lesson seen
        /// on its OK button and deliberately not on a back-out, so that somebody interrupted
        /// mid-panel is taught next time instead of never — which is right for a tip raised over
        /// a run, and would leave this screen able to finish with neither id written. Marking
        /// both here is what makes "the tutorial is done" a single fact rather than a
        /// consequence of which buttons were pressed.
        /// </para>
        /// <para>
        /// <b>Skipping writes them too</b>, and that is the point of the skip: a player who says
        /// they know this must not then be taught it twice on the first rung.
        /// </para>
        /// </summary>
        public static void Spend()
        {
            TipLedger.MarkSeen(Verb);
            TipLedger.MarkSeen(Charge);
        }
    }
}
