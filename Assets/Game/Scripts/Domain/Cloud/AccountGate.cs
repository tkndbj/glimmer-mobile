using System;

namespace GlimmerGrove.Cloud
{
    /// <summary>What a sync is allowed to do, given who the save belongs to.</summary>
    public enum AccountGateVerdict
    {
        /// <summary>The save and the session agree. Read and write freely.</summary>
        Proceed,

        /// <summary>
        /// The save belongs to nobody yet and a session exists. Record the session's account
        /// on the save, then proceed. This is a first launch, and the only way an account is
        /// ever written onto a save that did not name one.
        /// </summary>
        Adopt,

        /// <summary>
        /// Neither side knows anything. Sign in — anonymously if need be — and decide again
        /// with the answer. Reachable only from a save that names no account, so a session
        /// minted here can never collide with one.
        /// </summary>
        SignIn,

        /// <summary>
        /// The save names an account and no session is in hand. Bring the SDK up and ask
        /// again; <b>never</b> create an account here. See the class comment.
        /// </summary>
        Resume,

        /// <summary>
        /// The save belongs to one account and the session is a different one. Nothing may be
        /// read and, far more importantly, nothing may be written. See the class comment.
        /// </summary>
        Refuse,
    }

    /// <summary>
    /// The one rule that keeps one player's grove out of another player's account:
    /// <b>a save may only ever be pushed to the account it says it belongs to.</b>
    ///
    /// <para>
    /// <b>Why this is not paranoia.</b> A sync is pull, join, push, and <c>SaveMerge.Join</c>
    /// is monotonic — it takes the larger of everything. Join two <em>different people's</em>
    /// saves and the result is a grove holding the better half of each, pushed over one of
    /// them. There is no undo for that and no support answer for it. The window where it
    /// becomes possible is short but entirely ordinary: switching accounts signs the session
    /// out of one and into another while the file on disk still describes the first, and the
    /// OAuth consent screen backgrounds the app in the middle of it — so a process death, a
    /// cancelled sheet or a dropped network lands squarely inside it.
    /// </para>
    /// <para>
    /// <b>Why it also protects the economy.</b> Earned credits are derived from the star
    /// ledger (invariant 9) and a glade's golden multiplier is a function of the account id,
    /// so the same ledger under a fresh uid is a fresh, differently-rolled, fully funded
    /// wallet — and chests and the grant log are keyed per uid too. Copying a save into any
    /// account that did not earn it is therefore a faucet, not merely a mix-up. The rule
    /// below is what makes that unreachable rather than merely unlikely.
    /// </para>
    /// <para>
    /// <b>Why <see cref="AccountGateVerdict.Resume"/> is a separate answer from
    /// <see cref="AccountGateVerdict.SignIn"/>.</b> They look like the same question — "there
    /// is no session, get one" — and answering them the same way is how a cancelled sign-in
    /// sheet used to cost a player their sync. Signing in with no session <em>creates an
    /// anonymous account</em>. That is right for a save nobody owns, and catastrophic for a
    /// save that names one: the new account would never match, so the device would sit in
    /// <see cref="AccountGateVerdict.Refuse"/> for ever, having quietly abandoned a grove the
    /// player believes is backed up. A save that names an account must have that account
    /// restored or nothing at all — the failure is then a retry, which the next launch or the
    /// next tap of a provider button fixes.
    /// </para>
    /// <para>
    /// Pure, static-free and free of Unity types on purpose, for the reason <c>RunClock</c>
    /// and <c>TweenCycle</c> are: this is a five-line rule guarding an unrecoverable failure
    /// that is invisible in the Editor, so it has to be provable offline rather than reasoned
    /// about. <c>AccountGateTests</c> walks every cell of the table.
    /// </para>
    /// </summary>
    public static class AccountGate
    {
        /// <summary>
        /// Decides what may happen next.
        /// </summary>
        /// <param name="saveOwnerId">The account the local save names, empty if none.</param>
        /// <param name="sessionUserId">The account the backend is authenticated as, empty if
        /// none — which includes an SDK that has not finished starting up.</param>
        public static AccountGateVerdict Decide(string saveOwnerId, string sessionUserId)
        {
            bool owned = !string.IsNullOrEmpty(saveOwnerId);
            bool session = !string.IsNullOrEmpty(sessionUserId);

            if (!owned) return session ? AccountGateVerdict.Adopt : AccountGateVerdict.SignIn;
            if (!session) return AccountGateVerdict.Resume;

            // Ordinal, never a culture-aware or case-insensitive comparison. A uid is an
            // opaque token from the provider, and any comparison cleverer than "the same
            // bytes" is one that can answer "yes" for two different people.
            return string.Equals(saveOwnerId, sessionUserId, StringComparison.Ordinal)
                ? AccountGateVerdict.Proceed
                : AccountGateVerdict.Refuse;
        }
    }
}
