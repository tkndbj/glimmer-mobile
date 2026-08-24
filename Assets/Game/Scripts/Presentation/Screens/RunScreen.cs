namespace GlimmerGrove
{
    /// <summary>
    /// The screen a run is played on, whichever mode it belongs to.
    ///
    /// <para>
    /// <b>It exists so the panels around a run do not have to be written twice.</b> The defeat
    /// panel, the pause menu and the forfeit prompt all need to be able to say "try again",
    /// "restart", "back to the map" and "carry on" — and they used to say them to a
    /// <c>PlayScreen</c> specifically, which meant a second mode either duplicated three panels
    /// or went without them. Duplicating was the worse option by some distance: those panels
    /// carry the heart accounting, and two copies of a rule about charging players is exactly
    /// what invariant 9a is about.
    /// </para>
    /// <para>
    /// A base class rather than an interface, and that is a Unity detail worth stating: the
    /// panels hold a reference across frames and test it with <c>if (Screen)</c>, which is
    /// <c>UnityEngine.Object</c>'s lifetime check and the only one that answers correctly for a
    /// screen that has been destroyed underneath them. An interface reference would test as
    /// non-null on a dead object and call into it.
    /// </para>
    /// </summary>
    public abstract class RunScreen : View
    {
        /// <summary>Another go after the run was declared lost.</summary>
        public abstract void RetryAfterDefeat();

        /// <summary>Put the level back as it started. The run continues and is still owed for.</summary>
        public abstract void RestartLevel();

        /// <summary>Leave for the map, confirming first if the run has been paid for.</summary>
        public abstract void LeaveToMap();

        /// <summary>Leave for the hub, on the same terms.</summary>
        public abstract void LeaveToHome();

        /// <summary>Hand the level back after a panel that latched it.</summary>
        public abstract void Resume();
    }
}
