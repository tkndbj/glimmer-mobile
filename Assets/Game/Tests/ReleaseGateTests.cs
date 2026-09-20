using System.Text.RegularExpressions;
using GlimmerGrove.Release;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What the update wall remembers about itself between processes — the half of the feature
    /// that makes it unavoidable rather than merely usual.
    ///
    /// <para>
    /// <b>A wall that lived only in memory would be dismissed by the one gesture every player
    /// already knows</b>: force-quit and reopen, in flight mode if they like, so that the read
    /// which raised it simply fails on the way back. So the requirement is written device-locally
    /// the moment the deployment names it, and these cases are about that write — that it
    /// happens, that it survives, that it can be taken back, and that it is refused for a
    /// requirement nobody could satisfy.
    /// </para>
    /// <para>
    /// These reach <c>PlayerPrefs</c>, so they run in the Editor's Test Runner rather than
    /// offline. There is no way round that and no point faking it: the whole claim is one value
    /// outliving the process (<c>ChapterChoiceTests</c> makes the same trade for the same
    /// reason). The rules that are pure arithmetic are deliberately <b>not</b> here — see
    /// <see cref="ReleaseRuleTests"/>, which runs on every offline gate.
    /// </para>
    /// </summary>
    public sealed class ReleaseGateTests
    {
        const string Store = "https://play.google.com/store/apps/details?id=com.example.game";

        static ReleaseRequirement Wall(int minimum, string store = Store)
            => new ReleaseRequirement(minimum, store);

        /// <summary>
        /// Both ends, for <c>ChapterChoiceTests</c>' reason: the Editor's own
        /// <see cref="PlayerPrefs"/> survive between runs, so without the setup a case about
        /// "this device has never been told anything" would quietly test whatever the last run
        /// wrote — and the whole point of this class is that what it wrote outlives the process.
        /// </summary>
        [SetUp]
        public void Clear() => ReleaseGate.Forget();

        [TearDown]
        public void Tidy() => ReleaseGate.Forget();

        // -------------------------------------------------------------- surviving a restart
        [Test]
        public void TheRequirementSurvivesTheProcess()
        {
            ReleaseGate.Apply(Wall(10300));

            Rebooted();

            Assert.AreEqual(10300, ReleaseGate.Held.MinimumBuild);
            Assert.AreEqual(Store, ReleaseGate.Held.StoreUrl);
        }

        [Test]
        public void ADeviceThatCannotReachAnybodyStillEnforcesWhatItWasTold()
        {
            // Not a call this fixture can make directly, and that is the point: CloudSaveService
            // only ever calls Apply for a read that *succeeded*, so a launch with no signal
            // changes nothing here. What is pinned is the half that makes that contract safe —
            // the wall is on the device, so the failed read has a standing wall to leave alone.
            ReleaseGate.Apply(Wall(10300));
            Rebooted();

            Assert.IsTrue(ReleaseGate.Held.Shuts(10204));
        }

        [Test]
        public void TheDoorSurvivesWithTheWall()
        {
            // Both halves or neither. A restart that recovered the minimum and lost the link
            // would produce a requirement that is not enforceable, so the wall would silently
            // lift — the failure is safe and it is still a feature that stops working the first
            // time anybody force-quits.
            ReleaseGate.Apply(Wall(10300));
            Rebooted();

            Assert.IsTrue(ReleaseGate.Held.IsEnforceable);
            Assert.AreEqual(Store, ReleaseGate.StoreUrl);
        }

        // ------------------------------------------------------------------ and coming down
        [Test]
        public void TheServersAnswerGovernsDownwardsAsWellAsUp()
        {
            // The one that keeps a typo from being permanent. A ratchet that only ever rose
            // would be safer against a player and catastrophic against a mis-seeded minimum: the
            // document that would fix it is the one the client has stopped believing.
            ReleaseGate.Apply(Wall(999999));
            Assert.IsTrue(ReleaseGate.Held.Shuts(10300));

            ReleaseGate.Apply(Wall(10300));
            Assert.IsFalse(ReleaseGate.Held.Shuts(10300));

            Rebooted();
            Assert.IsFalse(ReleaseGate.Held.Shuts(10300), "the rollback did not reach the device");
        }

        [Test]
        public void ADeploymentThatRequiresNothingLiftsEveryWall()
        {
            // Deleting config/release is the emergency stop, so "nothing is required" has to
            // clear what is held on the device rather than merely fail to add to it.
            ReleaseGate.Apply(Wall(10300));
            ReleaseGate.Apply(ReleaseRequirement.None);

            Assert.AreEqual(0, ReleaseGate.Held.MinimumBuild);

            Rebooted();
            Assert.AreEqual(0, ReleaseGate.Held.MinimumBuild);
        }

        // ----------------------------------------------------------- a wall must have a door
        [Test]
        public void ADoorlessWallIsNotEvenRemembered()
        {
            // Stronger than "not enforced now": storing it would leave a wall on the device that
            // no later build and no later seed could be proved to open from the player's side,
            // which is the one failure in this feature with no recovery short of a reinstall.
            // Applying it has to be the same as applying nothing.
            ExpectTheDoorlessAlarm();
            ReleaseGate.Apply(Wall(10300, string.Empty));

            Assert.AreEqual(0, ReleaseGate.Held.MinimumBuild);
            Assert.AreEqual(string.Empty, PlayerPrefs.GetString(ReleaseGate.PrefsKey, string.Empty));
        }

        [Test]
        public void ADoorlessRequirementLiftsTheWallRatherThanKeepingAStaleOne()
        {
            // A re-seed that drops the store link while keeping the minimum is a real mistake
            // with a real shape, and there are two defensible answers to it: lift the wall, or
            // keep enforcing the last one that had a door. This is the deliberate choice of the
            // first. Keeping the old one would mean a device enforcing a requirement the server
            // no longer states, pointed at a link the server no longer publishes — which is the
            // shape of every unfixable outage in this feature. The cost is that the mistake
            // turns the wall off rather than making it stale, which is loud in the log and is
            // repaired by the same re-seed that caused it.
            ReleaseGate.Apply(Wall(10300));

            ExpectTheDoorlessAlarm();
            ReleaseGate.Apply(Wall(10400, string.Empty));

            Assert.IsFalse(ReleaseGate.IsShut);
            Assert.AreEqual(0, ReleaseGate.Held.MinimumBuild);
        }

        // ------------------------------------------------------------------------- helpers
        /// <summary>
        /// Declares the error a doorless requirement is supposed to raise.
        ///
        /// <para>
        /// <b>The log line is part of the rule rather than noise beside it</b> — 49a says a
        /// requirement naming no usable link is neither enforced nor cached, and the whole
        /// reason that is safe is that it is <em>loud</em>: the mistake is a re-seed, and a
        /// wall that silently switched itself off would be indistinguishable from a wall
        /// nobody asked for. So it is declared with <c>LogAssert.Expect</c> rather than
        /// waved through with <c>ignoreFailingMessages</c>, which would also swallow any
        /// other error these cases provoke.
        /// </para>
        /// <para>
        /// Matched on a fragment, because the sentence carries an em dash that does not
        /// survive every console encoding — and these two cases had been red in the Editor
        /// ever since, on a suite where a red test hides the next real one.
        /// </para>
        /// </summary>
        static void ExpectTheDoorlessAlarm()
            => LogAssert.Expect(LogType.Error, new Regex("no usable store link"));
        /// <summary>
        /// Everything this device remembers, read back the way a cold start reads it.
        ///
        /// <see cref="ReleaseGate.Forget"/> clears the stored value as well, which is the
        /// opposite of what these cases need — so the in-memory copy is dropped by putting the
        /// stored one back over a forgotten gate.
        /// </summary>
        static void Rebooted()
        {
            string stored = PlayerPrefs.GetString(ReleaseGate.PrefsKey, string.Empty);
            ReleaseGate.Forget();
            PlayerPrefs.SetString(ReleaseGate.PrefsKey, stored);
            PlayerPrefs.Save();
        }
    }
}
