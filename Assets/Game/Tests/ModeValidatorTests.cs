using System.Collections.Generic;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every mode the game can play has checks registered for it.
    ///
    /// <para>
    /// <b>This is the guard that pays for moving the validator out of the player build.</b> How a
    /// mode is proved fit to ship used to be a <c>virtual</c> on <c>LevelMode</c>, which made the
    /// question unaskable: a mode either overrode it or inherited a body that did nothing, and
    /// both compiled. The cost of that convenience was six hundred lines of content checks
    /// compiled into every player's install, because the authoring entry point and the runtime
    /// mode referenced each other in a cycle.
    /// </para>
    /// <para>
    /// <see cref="ModeValidator"/> cuts the cycle, and the bill for cutting it is exactly this
    /// file: a registry can be missing an entry where an abstract member cannot. That would be
    /// silent in the worst possible way — <c>Validate Content</c> would print a green tick over a
    /// mode nothing had looked at — so the registration is asserted rather than assumed, and
    /// <c>LevelValidator</c> reports an unregistered mode as an error rather than a pass.
    /// </para>
    /// </summary>
    public sealed class ModeValidatorTests
    {
        [Test]
        public void EveryModeThisBuildCanPlayHasChecksRegisteredForIt()
        {
            foreach (var mode in GameMode.Shipped)
                Assert.IsNotNull(ModeValidators.Of(mode),
                    $"'{mode}' is a mode this build can load and has no ModeValidator, so every "
                    + "level of it would be reported as fine without anything having looked at "
                    + "it — register one in ModeValidators");
        }

        [Test]
        public void NoTwoValidatorsClaimTheSameMode()
        {
            // The lookup returns the first match, so a duplicate is not an error anywhere — it is
            // a set of checks that silently never runs.
            var seen = new HashSet<string>();

            foreach (var validator in ModeValidators.All)
                Assert.IsTrue(seen.Add(validator.Mode.Value),
                    $"two validators claim '{validator.Mode}'; the second one never runs");
        }

        [Test]
        public void TheRegistryCoversTheModeRegistryAndNothingElse()
        {
            // Both directions. A validator for a mode this build cannot load is dead code that
            // reads as coverage, and it is how a mode that was removed leaves its checks behind.
            Assert.AreEqual(GameMode.Shipped.Count, ModeValidators.All.Count,
                "the two registries have drifted apart");

            foreach (var validator in ModeValidators.All)
                Assert.IsNotNull(LevelModes.Find(validator.Mode),
                    $"'{validator.Mode}' has checks but is not a mode this build can play");
        }

        [Test]
        public void AnUnregisteredModeAnswersNothingRatherThanFallingBackToTheClassicOne()
        {
            // The one place this must differ from ModeLooks, which answers an unknown mode with
            // the glade's look on purpose — a map with an odd-looking node beats a map that will
            // not open. There is no equivalent trade here: the fallback would be "checked as a
            // glade", which for a mode that is not a glade means "checked nothing" and looks
            // exactly like a pass.
            Assert.IsNull(ModeValidators.Of(default),
                          "an unregistered mode was answered with somebody else's checks");
        }
    }
}
