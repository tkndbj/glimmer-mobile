namespace GlimmerGrove
{
    /// <summary>
    /// Why a run ended badly.
    ///
    /// Only one way to lose today, and the enum stays anyway: it is what the analytics
    /// event is keyed on, and a second reason — a hazard, a timer — becomes an added
    /// case rather than a changed signature at every call site.
    /// </summary>
    public enum DefeatReason
    {
        /// <summary>The move budget ran out with the glade unsolved.</summary>
        OutOfMoves = 0,
    }
}
