using GlimmerGrove.Cloud;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rule that keeps one player's grove out of another player's account.
    ///
    /// <para>
    /// Five lines of production code, and every one of the failures they prevent is
    /// unrecoverable and invisible in the Editor — the Editor never authenticates, so a device
    /// holding the wrong account is a state that only exists on somebody's phone. That is the
    /// same argument <c>TweenCycle</c> made, and it is why <see cref="AccountGate"/> is a pure
    /// function with no Unity types in it: the table below can be walked offline, in full,
    /// rather than reasoned about.
    /// </para>
    /// <para>
    /// The one to read first is <see cref="TwoDifferentAccountsAreRefused"/>. A sync is pull,
    /// join, push, and the join is monotonic — so addressed to the wrong account it takes the
    /// better half of two strangers' saves and writes it over one of them.
    /// </para>
    /// </summary>
    public sealed class AccountGateTests
    {
        const string Mine = "uid-mine";
        const string Theirs = "uid-theirs";

        // ------------------------------------------------------------ the refusal
        [Test]
        public void TwoDifferentAccountsAreRefused()
            => Assert.AreEqual(AccountGateVerdict.Refuse, AccountGate.Decide(Mine, Theirs),
                               "a save may only ever be pushed to the account it belongs to");

        [Test]
        public void TheSameAccountProceeds()
            => Assert.AreEqual(AccountGateVerdict.Proceed, AccountGate.Decide(Mine, Mine));

        /// <summary>
        /// A uid is an opaque token, so the only safe comparison is byte equality. Anything
        /// cleverer is a comparison that can answer "yes" for two different people.
        /// </summary>
        [Test]
        public void AccountIdsAreComparedExactly()
        {
            Assert.AreEqual(AccountGateVerdict.Refuse, AccountGate.Decide("uid-abc", "UID-ABC"));
            Assert.AreEqual(AccountGateVerdict.Refuse, AccountGate.Decide("uid-abc", "uid-abc "));
        }

        // ----------------------------------------------------------- the first launch
        [Test]
        public void AnUnownedSaveAdoptsWhoeverIsSignedIn()
            => Assert.AreEqual(AccountGateVerdict.Adopt, AccountGate.Decide("", Mine),
                               "a first launch is the one moment an account is written onto a save");

        [Test]
        public void KnowingNothingAtAllSignsIn()
            => Assert.AreEqual(AccountGateVerdict.SignIn, AccountGate.Decide("", ""));

        [Test]
        public void AnUnownedSaveTreatsNullTheSameAsEmpty()
        {
            Assert.AreEqual(AccountGateVerdict.SignIn, AccountGate.Decide(null, null));
            Assert.AreEqual(AccountGateVerdict.Adopt, AccountGate.Decide(null, Mine));
        }

        // ------------------------------------------------------- resume, never sign in
        /// <summary>
        /// The distinction the whole class exists for, and the one that used to be missing.
        ///
        /// <para>
        /// Signing in with no session <em>creates an anonymous account</em>. Doing that on
        /// behalf of a save that already names somebody produces an account that can never
        /// match it, so the device is refused for ever afterwards — silently abandoning a grove
        /// the player believes is backed up. A cancelled consent sheet was enough to reach it.
        /// </para>
        /// </summary>
        [Test]
        public void AnOwnedSaveWithNoSessionResumesRatherThanSigningIn()
        {
            Assert.AreEqual(AccountGateVerdict.Resume, AccountGate.Decide(Mine, ""));
            Assert.AreEqual(AccountGateVerdict.Resume, AccountGate.Decide(Mine, null));

            Assert.AreNotEqual(AccountGateVerdict.SignIn, AccountGate.Decide(Mine, ""),
                               "signing in here would mint an account the save can never match");
        }

        /// <summary>
        /// Resuming is what the SDK's start-up looks like, and it has to end somewhere. Whatever
        /// comes back is put through the same table again, so the second answer is always one of
        /// the two terminal ones and there is no way to loop.
        /// </summary>
        [Test]
        public void ResumingSettlesOnTheSecondAsk()
        {
            Assert.AreEqual(AccountGateVerdict.Proceed, AccountGate.Decide(Mine, Mine));
            Assert.AreEqual(AccountGateVerdict.Refuse, AccountGate.Decide(Mine, Theirs));
        }
    }
}
