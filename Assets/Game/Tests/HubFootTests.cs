using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The hub's foot in both of its shapes (<see cref="HubFoot"/>): the plain stack the hub has
    /// always drawn, and the stack with the welcome door under Daily Challenges (invariant 58).
    /// Both have to clear the squarest canvas the game is drawn on, which is the one thing
    /// <c>render_home.py</c> cannot show at a glance because it draws one canvas.
    /// </summary>
    public sealed class HubFootTests
    {
        [Test]
        public void ThePlainStackIsTheOneTheHubHasAlwaysDrawn()
        {
            var foot = HubFoot.For(false);
            Assert.IsFalse(foot.Welcome);
            Assert.AreEqual(0f, foot.WelcomeH);
            Assert.AreEqual(14f, foot.Gap);
            Assert.AreEqual(280f, foot.ChallengeH);
            Assert.AreEqual(178f, foot.PlayH);
            Assert.AreEqual(168f, foot.LineCell);
            Assert.AreEqual(228f, foot.LineH, "8 + 34 + 8 + 168 + 10");
            Assert.AreEqual(NavBar.Height + 14f + 140f, foot.ChallengeY);
        }

        [Test]
        public void BothShapesClearTheSquarestCanvas()
        {
            foreach (bool welcome in new[] { false, true })
            {
                var foot = HubFoot.For(welcome);
                Assert.GreaterOrEqual(foot.Spare, 0f,
                                      $"the {(welcome ? "welcome" : "plain")} stack is {-foot.Spare} over on 1080x{HubFoot.ShortestCanvas}");
            }
        }

        [Test]
        public void TheWelcomeDoorStandsUnderTheChallengesAndTheStackGrowsForIt()
        {
            var plain = HubFoot.For(false);
            var welcome = HubFoot.For(true);

            Assert.IsTrue(welcome.Welcome);
            Assert.Greater(welcome.WelcomeH, 0f);
            Assert.Greater(welcome.Top, plain.Top, "the door costs height, found by shrinking the rest");

            // The door stands on the nav bar; the challenge slot stands on the door.
            Assert.AreEqual(NavBar.Height + welcome.Gap, welcome.WelcomeY - welcome.WelcomeH * .5f);
            Assert.AreEqual(welcome.WelcomeY + welcome.WelcomeH * .5f + welcome.Gap,
                            welcome.ChallengeY - welcome.ChallengeH * .5f);

            // Every row is above the one under it, in order, in both shapes.
            foreach (var foot in new[] { plain, welcome })
            {
                Assert.Greater(foot.LineY - foot.LineH * .5f, foot.ChallengeY + foot.ChallengeH * .5f - .01f);
                Assert.Greater(foot.PlayY - foot.PlayH * .5f, foot.LineY + foot.LineH * .5f - .01f);
            }
        }

        [Test]
        public void TheShrunkControlsStayLegible()
        {
            var welcome = HubFoot.For(true);

            // A pill drawn far from its own height smears its moulded face (44a); the kit's
            // pills are 166 tall and the door key is drawn at 160.
            Assert.GreaterOrEqual(welcome.WelcomeH, 130f);
            Assert.GreaterOrEqual(welcome.PlayH, 150f);

            // A cell still holds a turret body and a star row under it.
            Assert.GreaterOrEqual(welcome.LineCell, 140f);

            // The challenge slot still has room above its 160 key for the picture to rise.
            Assert.Greater(welcome.ChallengeH, DoorKey.KeyH + 40f);
        }
    }
}
