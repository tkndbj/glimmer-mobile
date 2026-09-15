using GlimmerGrove.Release;

namespace GlimmerGrove
{
    /// <summary>
    /// Keeps the update wall standing for exactly as long as the deployment says it should.
    ///
    /// <para>
    /// <b>This is a poll and it has to be.</b> The obvious shape — raise the panel when the
    /// check comes back — is wrong three ways over, and each of the three is a way the wall gets
    /// quietly dismissed by something that is not the player agreeing to anything.
    /// <c>Flow.Go</c> destroys every modal in the stack on a screen change, so a panel raised
    /// once lives only until the next navigation any timer, receipt or callback happens to
    /// perform. A device that was told <em>last</em> launch and cannot reach the network this
    /// one gets no event at all, and that is the ordinary case rather than an edge — it is
    /// precisely the player who force-quits to get rid of the panel. And a requirement that is
    /// rolled back has to take the wall down again, which no "raise" ever announces.
    /// </para>
    /// <para>
    /// So the panel is a drawing of <see cref="ReleaseGate.IsShut"/>, re-asserted every frame,
    /// and the cost of that is an integer comparison against a cached field. Nothing has to be
    /// remembered, nothing has to be undone, and there is no sequence of taps, back presses,
    /// restarts or app switches that leaves the two out of step — because there is no state here
    /// to get out of step with.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not do is stop the game underneath.</b> The save still
    /// loads, the sync still runs, a purchase interrupted by a crash is still honoured. The
    /// scrim takes the input and that is the whole of the enforcement: every write this game
    /// makes is an idempotent monotonic join, so there is nothing to protect the server from,
    /// and refusing to push would strand whatever the player did in the session before the wall
    /// went up.
    /// </para>
    /// </summary>
    public static class UpdateGate
    {
        /// <summary>
        /// One frame. Driven from <c>Boot.Pump</c>, beside the other things that outlive any
        /// particular screen.
        /// </summary>
        public static void Tick()
        {
            var live = Flow.LiveModal<UpdateRequiredOverlay>();

            if (!ReleaseGate.IsShut)
            {
                // The requirement was rolled back, and the wall comes down where it stands.
                // This is the path that makes a mis-seeded minimum a bad afternoon rather than
                // a dead installed base, so it is worth as much as the raise below.
                live?.Lift();
                return;
            }

            if (live != null) return;

            // Not over the launch screen. The wall belongs on the hub, where the player has
            // arrived somewhere and the game has stopped loading — a panel over a progress bar
            // reads as a load that failed, and this one would be read that way by somebody
            // already being told their game is out of date. It costs nothing: the splash is a
            // second or two and there is nothing on it to play.
            if (Flow.Current == null || Flow.Current is SplashScreen) return;

            // Not during a transition either, and this one is mechanical rather than a matter of
            // taste: `Flow.Go` destroys the stack when it swaps, so a panel raised while the
            // iris is closing is one built, animated and thrown away inside a third of a second.
            // The frame after the swap raises it properly.
            if (Flow.Busy) return;

            // Counted here rather than when the requirement arrived, so that a device enforcing
            // a wall it recovered off its own storage — the offline force-quit, which is the
            // session this feature exists for — is in the figures too. Latched to once per
            // process inside, because the line below runs again after every screen change.
            ReleaseGate.NoteShown();

            Flow.Modal<UpdateRequiredOverlay>();
        }
    }
}
