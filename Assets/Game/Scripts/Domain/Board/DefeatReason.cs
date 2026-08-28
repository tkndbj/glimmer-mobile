namespace GlimmerGrove
{
    /// <summary>
    /// Why a run ended badly.
    ///
    /// Worth telling apart even though they cost the same heart. A player who ran out
    /// of turns made a mistake spread over the whole run; one who crumbled a conduit
    /// made a single identifiable one, and the screen should say which.
    ///
    /// It splits the analytics too: a glade draining hearts on the budget is tuned
    /// wrong and one draining them on brittle conduits is teaching badly. Those need
    /// different fixes, and a single "defeated" count cannot tell you which.
    ///
    /// Values are explicit and permanent because analytics keys on them.
    /// </summary>
    public enum DefeatReason
    {
        /// <summary>The move budget ran out with the glade unsolved.</summary>
        OutOfMoves = 0,

        /// <summary>A brittle conduit was turned once too often and crumbled.</summary>
        ConduitLost = 1,

        /// <summary>
        /// <b>Retired.</b> The clock reached zero with the glade unsolved.
        ///
        /// Nothing raises it: the countdown was removed, so the only two ways to lose a
        /// glade are the move budget and a crumbled conduit. The member stays because these
        /// values are permanent — analytics keys on them, so every defeat row ever written
        /// carries a 2 and re-pointing it at some other ending would silently re-label
        /// history. Retired in place, exactly as <c>ChestDropKind.RunTime</c> is.
        /// </summary>
        OutOfTime = 2,

        /// <summary>
        /// A weave ran out of light with the grove unfinished.
        ///
        /// <para>
        /// Its own value rather than <see cref="OutOfMoves"/>, for the reason this enum exists
        /// at all: the two cost the same heart and want different fixes. A glade draining hearts
        /// on its budget is a board asking for too many turns; a weave draining them on ink is
        /// either a grove forcing more detour than it was dealt light for or a mode teaching
        /// badly — and a single count could not tell you which. It covers both ways a weave
        /// ends, running dry and being left with no move it can afford, because from the
        /// player's side those are one thing: there is not enough light left to finish.
        /// </para>
        /// </summary>
        OutOfInk = 3,

        /// <summary>
        /// A Lightfall well flooded: a mote came to rest above the brim.
        ///
        /// <para>
        /// Its own value rather than <see cref="OutOfMoves"/>, for this enum's reason. The two
        /// ways to lose a well want opposite fixes and a single count could not tell them
        /// apart: flooding means the boards are asking for more spatial care than they are
        /// teaching, which is a level design problem, where running dry means the supply is
        /// tight, which is a tuning one. They are also the two halves of the same mistake seen
        /// from different distances, so the ratio between them is the reading that matters.
        /// </para>
        /// </summary>
        WellFlooded = 4,

        /// <summary>
        /// A Lightfall well ran out of motes with light still standing in it.
        ///
        /// Covers both ways the supply ends — the tray emptying, and what is left to come being
        /// unable to finish what is left standing — because from the player's side those are
        /// one thing: there are not enough motes left to clear the well.
        /// </summary>
        OutOfMotes = 5,

        /// <summary>
        /// A grove ran out of tiles with a bed still waiting.
        ///
        /// Its own value rather than <see cref="OutOfMoves"/>, for this enum's reason. The two
        /// ways a grove ends want opposite fixes and a single count could not tell them apart:
        /// running out of tiles means the basket is tight, which is a tuning problem, where
        /// running out of ground means the boards are asking for more care about <em>where</em>
        /// than they are teaching, which is a level design one.
        /// </summary>
        OutOfTiles = 6,

        /// <summary>
        /// A grove had nowhere left to grow with a bed still waiting.
        ///
        /// The spatial half of the pair above, and the one no purchase can rescue: no number of
        /// tiles gives a grove somewhere to plant that it does not have. See
        /// <c>KeeperVerdict</c>.
        /// </summary>
        Overgrown = 7,
    }
}
