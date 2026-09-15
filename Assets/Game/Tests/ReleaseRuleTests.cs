using GlimmerGrove.Release;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The forced-update rules that are pure arithmetic: which builds a requirement walls out,
    /// what makes one enforceable at all, how a version string becomes a number, and how often
    /// the deployment is asked.
    ///
    /// <para>
    /// <b>Split from <see cref="ReleaseGateTests"/> so that it runs.</b> That fixture reaches
    /// <c>PlayerPrefs</c>, which is a native call, so the offline runner reports every case in
    /// it as "needs the Editor" — and a single shared <c>[SetUp]</c> would drag these in with
    /// them, leaving the most consequential comparison in the game behind the one gate nobody
    /// runs on the way past (invariant 29e, which this project has already paid for once).
    /// Nothing here touches the device, so all of it is checked on every offline run.
    /// </para>
    /// <para>
    /// <b>Every case is about a failure with no undo.</b> Off by one and the release that was
    /// just shipped is walled out; a wall with no door and an installed base is pointed at
    /// nothing; a version string read generously and a stale build walks straight through. None
    /// of them is visible in the Editor, in a content gate or in a render.
    /// </para>
    /// </summary>
    public sealed class ReleaseRuleTests
    {
        const string Store = "https://play.google.com/store/apps/details?id=com.example.game";

        static ReleaseRequirement Wall(int minimum, string store = Store)
            => new ReleaseRequirement(minimum, store);

        // ------------------------------------------------------------------ the comparison
        [Test]
        public void ABuildBelowTheMinimumIsWalledOut()
        {
            Assert.IsTrue(Wall(10300).Shuts(10204));
        }

        [Test]
        public void TheMinimumBuildItselfIsNotWalledOut()
        {
            // The published number is the oldest build still *allowed*, not the first one
            // required. Off by one here walls out the release that was just shipped, which is
            // the one everybody is about to be on.
            Assert.IsFalse(Wall(10300).Shuts(10300));
            Assert.IsFalse(Wall(10300).Shuts(10301));
        }

        [Test]
        public void AMinimumOfNoughtWallsNobodyOut()
        {
            // The ordinary state of a release, and what an unseeded deployment answers.
            Assert.IsFalse(Wall(0).Shuts(1));
            Assert.IsFalse(ReleaseRequirement.None.Shuts(1));
        }

        [Test]
        public void ABuildThatCannotBeReadIsNeverWalledOut()
        {
            // AppVersion.Running answers nought when Application.version is not a version. A
            // comparison against an unknown build would wall out every install in the world
            // rather than the old ones — the failure direction with no recovery at all.
            Assert.IsFalse(Wall(10300).Shuts(0));
        }

        // ---------------------------------------------------------------- a wall needs a door
        [Test]
        public void AWallWithNoStoreLinkIsNotEnforced()
        {
            // The iOS case, and it arrives by doing the normal thing: Apple review runs days
            // behind a Play rollout, so a release that forces an update before the App Store has
            // published it would point every iPhone at nothing. Refused outright rather than
            // half-applied, because the half that survives is the wall.
            Assert.IsFalse(Wall(10300, string.Empty).IsEnforceable);
            Assert.IsFalse(Wall(10300, string.Empty).Shuts(10204));
        }

        [Test]
        public void AWallWithAnUnusableStoreLinkIsNotEnforced()
        {
            // Whatever the platform does with a malformed URL, it is not "open the store" — and
            // on a device doing nothing is indistinguishable from a dead button, on the one
            // panel in this game that has only the one button.
            Assert.IsFalse(Wall(10300, "play.google.com/store").IsEnforceable);
            Assert.IsFalse(Wall(10300, "http://insecure.example").IsEnforceable);
            Assert.IsFalse(Wall(10300, "https://store.example/a b").IsEnforceable);
        }

        [Test]
        public void ADoorWithNoWallAsksForNothing()
        {
            // The ordinary published state between forced releases: a store link is named and
            // no minimum is. It must not read as a wall of height nought.
            Assert.IsFalse(Wall(0).IsEnforceable);
            Assert.IsFalse(Wall(0).Demands);
        }

        // ---------------------------------------------------------------- the stored form
        [Test]
        public void TheStoredFormRoundTrips()
        {
            var requirement = Wall(10300);
            var read = ReleaseGate.Parse(ReleaseGate.Compose(requirement));

            Assert.AreEqual(requirement.MinimumBuild, read.MinimumBuild);
            Assert.AreEqual(requirement.StoreUrl, read.StoreUrl);
        }

        [Test]
        public void ADoorlessRequirementComposesToNothing()
        {
            // The device may not hold a wall it cannot open — see
            // ReleaseGateTests.ADoorlessWallIsNotEvenRemembered for the same claim through the
            // store itself. Pinned here too because this is the half a future refactor would
            // reach for first.
            Assert.AreEqual(string.Empty, ReleaseGate.Compose(Wall(10300, string.Empty)));
            Assert.AreEqual(string.Empty, ReleaseGate.Compose(ReleaseRequirement.None));
        }

        [Test]
        public void AMalformedStoredValueReadsAsNoWall()
        {
            // A hand edit, a half-written store, a key some future version wrote differently.
            // Every refusal in this feature has to fail open: it may fail to wall somebody out,
            // and may never wall somebody out by accident.
            foreach (string stored in new[]
                     { "", "   ", "10300", "abc " + Store, "10300 ", " " + Store, "10300 nonsense" })
            {
                Assert.AreEqual(0, ReleaseGate.Parse(stored).MinimumBuild, $"'{stored}'");
            }
        }

        // ------------------------------------------------------------------ the build number
        [Test]
        public void AVersionStringBecomesOneComparableNumber()
        {
            Assert.IsTrue(AppVersion.TryParse("1.4.2", out int build));
            Assert.AreEqual(10402, build);

            // A missing segment is nought, so these name the same build.
            Assert.IsTrue(AppVersion.TryParse("1", out int bare));
            Assert.IsTrue(AppVersion.TryParse("1.0.0", out int full));
            Assert.AreEqual(full, bare);
        }

        [Test]
        public void VersionsOrderTheWayReleasesDo()
        {
            AppVersion.TryParse("1.9.0", out int older);
            AppVersion.TryParse("1.10.0", out int newer);
            AppVersion.TryParse("2.0.0", out int newest);

            Assert.Less(older, newer);
            Assert.Less(newer, newest);
        }

        [Test]
        public void AVersionThatIsNotOneIsRefusedRatherThanGuessedAt()
        {
            // Refused, so AppVersion.Running answers nought, so nothing is walled out. The
            // alternative — salvaging a number out of "1.4.2-beta" — is this file inventing a
            // convention nobody wrote down, on the input that decides who gets locked out.
            Assert.IsFalse(AppVersion.TryParse(null, out _));
            Assert.IsFalse(AppVersion.TryParse("", out _));
            Assert.IsFalse(AppVersion.TryParse("beta", out _));
            Assert.IsFalse(AppVersion.TryParse("1.4.2-beta", out _));
        }

        [Test]
        public void ASegmentThatWouldCollideWithTheOneAboveIsRefused()
        {
            // 1.100.0 and 2.0.0 are the same integer under this scheme, and the direction that
            // fails in is the bad one: a build that reads as newer than it is walks through the
            // gate. Refused rather than clamped — a clamp makes two real versions equal, which
            // is the same fault wearing a check's clothes.
            Assert.IsFalse(AppVersion.TryParse("1.100.0", out _));
            Assert.IsFalse(AppVersion.TryParse("1.0.100", out _));
            Assert.IsTrue(AppVersion.TryParse("1.99.99", out _));
        }

        [Test]
        public void ContentKeepsItsForgivingDefault()
        {
            // A chapter's minAppVersion compares against this one, where the worst outcome of
            // being wrong is a chapter shown that could have been hidden. The gate's default is
            // deliberately the opposite (nought, wall nobody) — two callers, two directions, one
            // parser.
            Assert.AreEqual(1, AppVersion.Parse("nonsense"));
            Assert.AreEqual(10402, AppVersion.Parse("1.4.2"));
        }

        // ------------------------------------------------------------------------- cadence
        [Test]
        public void NothingIsAskedWhileTheDeviceIsOffline()
        {
            var watch = new ReleaseWatch();
            watch.Claim();
            watch.Answered(ok: true);

            // An hour of frames with no network must not produce a single attempt — and must not
            // spend the cadence either, so that regaining a signal is what asks rather than the
            // quarter of an hour that happened to pass in a tunnel.
            for (int i = 0; i < 3600; i++) Assert.IsFalse(watch.Tick(1f, reachable: false));

            // Regaining it brings the next attempt forward to the reconnect pause rather than
            // firing on the frame the interface came up — NetworkReachability flips somewhat
            // before the interface carries traffic, and asking on that frame buys one guaranteed
            // failure and nothing else.
            Assert.IsFalse(watch.Tick(1f, reachable: true), "asked on the frame the signal returned");
            Assert.IsTrue(watch.Tick(ReleaseWatch.ResumeSeconds, reachable: true));
        }

        [Test]
        public void ALaunchWithNoSignalAsksTheMomentOneArrives()
        {
            // The device that has never been told anything. Nothing has been spent, so there is
            // no pause to serve — this is the launch in a tunnel, and the first moment it can be
            // answered is the first moment it asks.
            var watch = new ReleaseWatch();

            for (int i = 0; i < 600; i++) Assert.IsFalse(watch.Tick(1f, reachable: false));

            Assert.IsTrue(watch.Tick(1f, reachable: true));
        }

        [Test]
        public void OnlyOneReadIsEverOut()
        {
            var watch = new ReleaseWatch();

            Assert.IsTrue(watch.Claim(), "the launch takes the first claim");
            Assert.IsFalse(watch.Claim());

            // The first frame after the splash must not fire a second read of the same document.
            Assert.IsFalse(watch.Tick(600f, reachable: true));

            watch.Answered(ok: true);
            Assert.IsTrue(watch.Tick(ReleaseWatch.RecheckSeconds, reachable: true));
        }

        [Test]
        public void AFailureIsRetriedSoonerThanASuccessIsRechecked()
        {
            // A device that has not been answered might be running something it should not be,
            // so it asks again in a minute rather than a quarter of an hour. Pinned as a relation
            // rather than as two numbers, so retuning either keeps the claim honest.
            Assert.Less(ReleaseWatch.RetrySeconds, ReleaseWatch.RecheckSeconds);

            var watch = new ReleaseWatch();
            watch.Claim();
            watch.Answered(ok: false);

            Assert.IsFalse(watch.Tick(ReleaseWatch.RetrySeconds - 1f, reachable: true));
            Assert.IsTrue(watch.Tick(2f, reachable: true));
        }

        [Test]
        public void AStandingWallIsRecheckedOnTheShortCadence()
        {
            // The path a rolled-back requirement reaches a stuck player by, and the only one.
            // A walled device has no gameplay to interrupt, so it asks on the retry cadence
            // however successful the read was.
            var watch = new ReleaseWatch();
            watch.Claim();
            watch.Answered(ok: true, urgent: true);

            Assert.IsTrue(watch.Tick(ReleaseWatch.RetrySeconds, reachable: true));
        }

        [Test]
        public void FlickingBetweenAppsDoesNotCostAReadPerFlick()
        {
            var watch = new ReleaseWatch();
            watch.Claim();
            watch.Answered(ok: true);

            for (int i = 0; i < 40; i++)
            {
                watch.Resumed();
                Assert.IsFalse(watch.Tick(ReleaseWatch.ResumeSeconds, reachable: true),
                               "resuming asked again within the gap");
            }
        }

        [Test]
        public void ComingBackAfterAWhileDoesAskAgain()
        {
            var watch = new ReleaseWatch();
            watch.Claim();
            watch.Answered(ok: true);
            watch.Tick(ReleaseWatch.ResumeGapSeconds, reachable: true);

            watch.Resumed();
            Assert.IsTrue(watch.Tick(ReleaseWatch.ResumeSeconds, reachable: true));
        }
    }
}
