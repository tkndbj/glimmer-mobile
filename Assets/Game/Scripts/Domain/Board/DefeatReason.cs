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
    /// wrong, one draining them on brittle conduits is teaching badly, and one draining
    /// them on the clock is simply too fast. Those need three different fixes, and a
    /// single "defeated" count cannot tell you which.
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
        /// The clock reached zero with the glade unsolved.
        ///
        /// Kept apart from <see cref="OutOfMoves"/> even though both are "you ran out of
        /// something", because they say opposite things about a player: one spent turns
        /// badly and the other did not spend them fast enough, and a glade failing on the
        /// second is retuned through <see cref="Content.LevelTuning.TimeFactor"/> rather
        /// than through its budget.
        /// </summary>
        OutOfTime = 2,
    }
}
