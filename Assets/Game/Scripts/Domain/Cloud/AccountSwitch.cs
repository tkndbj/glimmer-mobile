namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// What became of an attempt to make this device a different account.
    ///
    /// <para>
    /// <b>There is no outcome here for "signed in but the grove could not be loaded", and its
    /// absence is the point.</b> That used to be the most likely way a switch ended: the
    /// download was part of the switch, so a dropped connection in the seconds after an OAuth
    /// consent screen — which is the moment the process has just been foregrounded and the
    /// database stream has just been re-authenticated, so by some distance the most fragile
    /// moment in the whole app — left the device authenticated as one player and holding
    /// another's save, with nothing to do about it but read a warning. Since
    /// <c>SaveService.SwitchTo</c> the switch is finished locally before the network is asked
    /// for anything, so it cannot stop halfway. What is left is three ways of arriving and two
    /// refusals that happen before anything moves.
    /// </para>
    /// </summary>
    public enum SwitchOutcome
    {
        /// <summary>
        /// Nothing happened and nothing changed — a bad argument, no backend, a sync holding
        /// the latch, or a consent screen the player closed. Safe to offer again immediately.
        /// </summary>
        Refused,

        /// <summary>
        /// The outgoing grove could not be saved to the server, so the switch was abandoned
        /// before it began. <b>This device is untouched and still signed in as it was.</b>
        /// The one refusal a player will actually meet, and it resolves by waiting for a
        /// connection rather than by anything they have to understand.
        /// </summary>
        NotSecured,

        /// <summary>
        /// The credential names the account already signed in here, so there was nothing to
        /// do. Not a failure.
        /// </summary>
        SameAccount,

        /// <summary>
        /// Signed in, and that account's grove is on the screen — restored from this device if
        /// it had been played here before, fetched if not.
        /// </summary>
        Adopted,

        /// <summary>
        /// Signed in to an account that has never played, and the server confirmed it. A new
        /// grove was started; the previous one is safe and comes back by signing in again.
        /// </summary>
        Started,

        /// <summary>
        /// Signed in, and this device has nothing of theirs yet — but the server has not been
        /// reached, so it cannot say whether they have a grove elsewhere.
        ///
        /// <para>
        /// A success, not a failure: the account changed, the previous grove is archived here
        /// and on the server, and the next sync fills this one in. It exists so the screen can
        /// avoid the one sentence that would be a lie — telling somebody with three chapters
        /// behind them that they are starting fresh, because a train went into a tunnel.
        /// </para>
        /// </summary>
        Pending,
    }

    /// <summary>
    /// The result of a switch: what happened, and why if it did not.
    ///
    /// A struct rather than a bare <see cref="CloudResult"/> because the outcomes above need
    /// different sentences on screen and four of them are not failures at all. A caller
    /// deciding from an error string is a caller that will one day tell somebody their grove is
    /// gone when it is sitting safely on a server.
    /// </summary>
    public readonly struct SwitchResult
    {
        public readonly SwitchOutcome Outcome;
        public readonly CloudResult Result;

        /// <summary>
        /// How much progress is on the device now, for a screen that wants to say so.
        ///
        /// Read after the switch settled rather than out of a server reply, because the two can
        /// differ — a grove restored from this device's own archive never came from a reply at
        /// all — and what a player wants confirmed is what they are about to be looking at.
        /// </summary>
        public readonly int ClearedGlades;

        SwitchResult(SwitchOutcome outcome, CloudResult result, int clearedGlades)
        {
            Outcome = outcome;
            Result = result;
            ClearedGlades = clearedGlades;
        }

        /// <summary>True when this device is now the account that was asked for.</summary>
        public bool Ok => Outcome == SwitchOutcome.Adopted
                       || Outcome == SwitchOutcome.Started
                       || Outcome == SwitchOutcome.Pending
                       || Outcome == SwitchOutcome.SameAccount;

        /// <summary>True when the switch did not happen and nothing on this device moved.</summary>
        public bool Untouched => Outcome == SwitchOutcome.Refused
                              || Outcome == SwitchOutcome.NotSecured;

        public CloudFailure Failure => Result.Failure;

        public static SwitchResult Done(SwitchOutcome outcome, int clearedGlades = 0)
            => new SwitchResult(outcome, CloudResult.Success, clearedGlades);

        public static SwitchResult Failed(SwitchOutcome outcome, CloudFailure failure, string message = null)
            => new SwitchResult(outcome, CloudResult.Failed(failure, message), 0);

        public static SwitchResult Failed(SwitchOutcome outcome, CloudResult result)
            => new SwitchResult(outcome, result, 0);
    }
}
