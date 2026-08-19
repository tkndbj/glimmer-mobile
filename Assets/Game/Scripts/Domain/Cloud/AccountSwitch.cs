namespace GlimmerGrove.Cloud
{
    /// <summary>What became of an attempt to make this device a different account.</summary>
    public enum SwitchOutcome
    {
        /// <summary>
        /// Nothing happened and nothing changed — a bad argument, no backend, or a sync
        /// holding the latch. Safe to offer again immediately.
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
        /// do. Not a failure: it is also how a device recovers from an interrupted switch,
        /// because signing back in as yourself lands exactly here and touches no data.
        /// </summary>
        SameAccount,

        /// <summary>
        /// The credential names an account other than the one this device's save belongs to,
        /// and the caller asked only to be let back in to its own — so nothing was touched.
        ///
        /// <para>
        /// Only reachable while recovering from <see cref="Interrupted"/>, and it exists
        /// because that is the one moment when the safe move and the requested move differ.
        /// A device in that state cannot save its grove anywhere (the server will not take a
        /// write for an account the session is not), so becoming a third party would discard
        /// whatever has been played since the interruption with no way to get it back. Signing
        /// in as the save's own account first costs one tap and loses nothing; if the player
        /// really does want the other grove, that is the destructive adopt prompt's job and it
        /// asks twice.
        /// </para>
        /// </summary>
        DifferentAccount,

        /// <summary>Signed in, and that account's grove was fetched and is now on this device.</summary>
        Adopted,

        /// <summary>
        /// Signed in to an account that has never played. A new grove was started for it; the
        /// previous one is safe on the server and comes back by signing in again.
        /// </summary>
        Started,

        /// <summary>
        /// The session became the other account but its grove could not be fetched, so this
        /// device is authenticated as one player and holding another's save.
        ///
        /// <para>
        /// Nothing is lost and nothing leaks — <see cref="AccountGate"/> refuses every read
        /// and write until the two agree again — but syncing stays stopped until the player
        /// finishes the switch or signs back in as themselves, so it has to be said out loud
        /// rather than logged.
        /// </para>
        /// </summary>
        Interrupted,
    }

    /// <summary>
    /// The result of a switch: what happened, and why if it did not.
    ///
    /// A struct rather than a bare <see cref="CloudResult"/> because six of the outcomes above
    /// need different sentences on screen and three of them are not failures at all. A caller
    /// deciding from an error string is a caller that will one day tell somebody their grove
    /// is gone when it is sitting safely on the server.
    /// </summary>
    public readonly struct SwitchResult
    {
        public readonly SwitchOutcome Outcome;
        public readonly CloudResult Result;

        SwitchResult(SwitchOutcome outcome, CloudResult result)
        {
            Outcome = outcome;
            Result = result;
        }

        /// <summary>True when this device is now the account that was asked for.</summary>
        public bool Ok => Outcome == SwitchOutcome.Adopted
                       || Outcome == SwitchOutcome.Started
                       || Outcome == SwitchOutcome.SameAccount;

        /// <summary>True when the switch did not happen and nothing on this device moved.</summary>
        public bool Untouched => Outcome == SwitchOutcome.Refused
                              || Outcome == SwitchOutcome.NotSecured
                              || Outcome == SwitchOutcome.DifferentAccount;

        public CloudFailure Failure => Result.Failure;

        public static SwitchResult Done(SwitchOutcome outcome)
            => new SwitchResult(outcome, CloudResult.Success);

        public static SwitchResult Failed(SwitchOutcome outcome, CloudFailure failure, string message = null)
            => new SwitchResult(outcome, CloudResult.Failed(failure, message));

        public static SwitchResult Failed(SwitchOutcome outcome, CloudResult result)
            => new SwitchResult(outcome, result);
    }
}
