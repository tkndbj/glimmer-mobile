namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// What deleting an account costs, whether it may be attempted, and how it ended.
    ///
    /// <para>
    /// <b>In Domain and not beside the panel, for the reason every other branching rule here
    /// is.</b> A <c>switch</c> inside a <c>MonoBehaviour</c> is the one place in this project
    /// nothing can be proved, and this is the most destructive branch in the game: getting
    /// <see cref="Verdict.Reauthenticate"/> wrong means either deleting an account without
    /// proving who asked, or refusing a guest the one thing they are entitled to do without a
    /// provider. <c>AccountGate</c>, <c>HintPrompt</c> and <c>AccountPromptPolicy</c> are the
    /// same shape; so is this.
    /// </para>
    /// <para>
    /// <b>It holds no state and reaches nothing.</b> Everything is a pure function of what it
    /// is handed, so the whole rule runs in the offline suite with no Firebase, no save file
    /// and no Editor.
    /// </para>
    /// </summary>
    public static class AccountDeletion
    {
        /// <summary>What the panel must do before it may call the server.</summary>
        public enum Verdict
        {
            /// <summary>
            /// Ask the player to confirm, then delete. A guest account has no provider to
            /// prove anything against, and demanding one would mean the only players who
            /// cannot delete their account are the ones with the least invested in it.
            /// </summary>
            ConfirmOnly,

            /// <summary>
            /// Confirm, then re-authenticate with the linked provider before deleting.
            ///
            /// <para>
            /// Two things at once, and it is worth being clear that the second is a bonus
            /// rather than the reason. The reason is proof: an account with a provider on it
            /// may have been left signed in on somebody else's phone, and deletion is the one
            /// act here that cannot be walked back — so the provider is asked to confirm the
            /// person holding the device is the person who owns the account. What it also
            /// buys, for Apple, is the fresh authorization code that
            /// <c>revokeAppleGrant</c> needs; capturing that at link time instead would mean
            /// storing a live third-party credential for every Apple player for the life of
            /// their account, exercised months later by a path nothing else runs.
            /// </para>
            /// </summary>
            Reauthenticate,

            /// <summary>
            /// Nothing to delete here. No backend is configured, so there is no account and
            /// nothing has ever left the device — offering the control would be
            /// <c>ContinueChoice.Unavailable</c>'s complaint, a button that can never work.
            /// </summary>
            Unavailable,
        }

        /// <summary>Every way a deletion attempt can end.</summary>
        public enum Outcome
        {
            /// <summary>The account is gone and this device is a fresh grove.</summary>
            Deleted,

            /// <summary>The player closed the provider's sheet. Nothing happened.</summary>
            Cancelled,

            /// <summary>No connection. Nothing happened, and trying later will work.</summary>
            Offline,

            /// <summary>
            /// The provider signed in as somebody else.
            ///
            /// <para>
            /// Its own outcome rather than a failure, because it is the one that must never be
            /// reported as "something went wrong": a player who picked the wrong entry out of
            /// an account chooser needs to be told exactly that, and a deletion that silently
            /// did nothing after a provider sheet is indistinguishable from one that worked.
            /// </para>
            /// </summary>
            WrongAccount,

            /// <summary>A sync held the latch and would not let go. Nothing happened.</summary>
            Busy,

            /// <summary>Anything else. Nothing was deleted; the log has the reason.</summary>
            Failed,
        }

        /// <summary>
        /// What has to happen before the server is called.
        /// </summary>
        /// <param name="backendAvailable">Whether a cloud backend is configured at all.</param>
        /// <param name="linked">Whether a permanent provider is attached to the account.</param>
        public static Verdict Required(bool backendAvailable, bool linked)
        {
            if (!backendAvailable) return Verdict.Unavailable;

            return linked ? Verdict.Reauthenticate : Verdict.ConfirmOnly;
        }

        /// <summary>
        /// Whether the control is drawn at all.
        ///
        /// <para>
        /// Drawn in every state a deletion could possibly succeed in — including the mismatched
        /// one, and that is deliberate rather than an oversight. A device caught between two
        /// accounts is the state a player is most likely to want out of, and it is exactly the
        /// state where every *other* control is hedged. Refusing there would leave the one
        /// screen that can resolve it offering nothing but "try signing in again".
        /// </para>
        /// </summary>
        public static bool Offered(bool backendAvailable)
            => Required(backendAvailable, false) != Verdict.Unavailable;

        /// <summary>
        /// Turns a failed cloud call into the sentence the panel shows.
        ///
        /// <para>
        /// A method rather than a conditional at each site, for <c>AccountOverlay.Report</c>'s
        /// reason: closing a provider's sheet is by a wide margin the commonest way this ends
        /// and it is not a failure at all. It used to be reported as "no internet connection"
        /// one screen over, which is the class of wrong sentence this account flow exists to
        /// stop telling.
        /// </para>
        /// </summary>
        public static Outcome Read(CloudFailure failure) => failure switch
        {
            CloudFailure.None => Outcome.Deleted,
            CloudFailure.Cancelled => Outcome.Cancelled,
            CloudFailure.Offline => Outcome.Offline,
            CloudFailure.Busy => Outcome.Busy,

            // The provider signed in as somebody else. Its own sentence, never "something
            // went wrong" — see the outcome.
            CloudFailure.AccountMismatch => Outcome.WrongAccount,

            _ => Outcome.Failed,
        };

        /// <summary>
        /// Whether an outcome left the account exactly as it was.
        ///
        /// <para>
        /// The panel needs this and cannot infer it from "not deleted": a failure that is
        /// certain to have changed nothing may offer the button again, where anything else
        /// must send the player back to a screen that re-reads the account. Every outcome
        /// except <see cref="Outcome.Deleted"/> happens strictly before the server is asked to
        /// remove anything, which is what makes the answer knowable at all — see
        /// <c>CloudSaveService.DeleteAccountAsync</c>, where the local erasure runs only after
        /// the server has confirmed.
        /// </para>
        /// </summary>
        public static bool Untouched(Outcome outcome) => outcome != Outcome.Deleted;
    }

    /// <summary>The result of one deletion attempt.</summary>
    public readonly struct DeleteResult
    {
        public readonly AccountDeletion.Outcome Outcome;

        /// <summary>The underlying failure, for the log. <see cref="CloudFailure.None"/> on success.</summary>
        public readonly CloudFailure Failure;

        /// <summary>What went wrong, in the provider's own words. Never shown to a player.</summary>
        public readonly string Message;

        public DeleteResult(AccountDeletion.Outcome outcome, CloudFailure failure = CloudFailure.None,
                            string message = null)
        {
            Outcome = outcome;
            Failure = failure;
            Message = message ?? string.Empty;
        }

        public bool Ok => Outcome == AccountDeletion.Outcome.Deleted;

        public static DeleteResult Done() => new DeleteResult(AccountDeletion.Outcome.Deleted);

        public static DeleteResult From(CloudResult result)
            => new DeleteResult(AccountDeletion.Read(result.Failure), result.Failure, result.Message);

        public static DeleteResult Failed(AccountDeletion.Outcome outcome, CloudFailure failure,
                                          string message = null)
            => new DeleteResult(outcome, failure, message);
    }
}
