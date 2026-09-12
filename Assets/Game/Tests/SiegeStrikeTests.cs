using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where a stormcall's bolt lands.
    ///
    /// <para>
    /// <b>This fixture exists because the answer has been wrong twice, in two different ways, and
    /// a player found it both times.</b> The reel is baked with its flash near the foot of the
    /// frame and the bolt filling the rest, and the board then has to put the *sprite's centre*
    /// somewhere such that the flash ends up on the raider. First the centre went on the raider,
    /// so the strike went off half a frame above it — reported as lightning hitting random spots
    /// rather than enemies. Then the offset had the right shape and the wrong sign, which put the
    /// flash 3.3 cells *below* the raider: the ward line stands about that far down, so what a
    /// player saw was their own turrets being struck.
    /// </para>
    /// <para>
    /// <b>Nothing else in this project can see it.</b> No numeric gate opens a PNG, so par, the
    /// readings, the validators, the content check and the art audit are all green either way —
    /// and <c>Tools/render_siege.py</c>, which is the eye for exactly this class of fault, drew
    /// the *second* version correctly: it mirrors the same expression, but PIL's y runs down the
    /// picture where Unity's runs up it, so the mirror agreed with itself and disagreed with the
    /// game. **A mirror cannot check a sign it has to re-derive in the opposite axis** (invariant
    /// 44d), which is what leaves this to a test.
    /// </para>
    /// <para>
    /// <b>It asserts the consequence rather than the formula.</b> Restating
    /// <c>targetY + tall * (.5f - StrikeAt)</c> here would agree with a wrong sign as happily as
    /// with a right one; what is checked is where the flash comes out, worked forward from how
    /// the sprite is actually laid out — a rectangle of height <c>tall</c> centred on the answer,
    /// with the flash <see cref="SiegeView.StrikeAt"/> of the way up it.
    /// </para>
    /// </summary>
    public sealed class SiegeStrikeTests
    {
        /// <summary>Where the flash comes out, given where the sprite's centre was put.</summary>
        static float FlashY(float targetY, float tall)
        {
            float centre = SiegeView.StrikeCentre(targetY, tall);
            float foot = centre - tall * .5f;
            return foot + tall * SiegeView.StrikeAt;
        }

        [Test]
        public void AStrikeLandsOnTheThingItStruck()
        {
            // Cell sizes and hill positions vary by phone, so this is asked at several, including
            // a raider below the middle of the board — which is where the sign error hid, because
            // at the origin both signs give the same magnitude.
            foreach (float target in new[] { 0f, 120f, -260f, 640f })
                foreach (float tall in new[] { 180f, 390f, 512f })
                    Assert.That(FlashY(target, tall), Is.EqualTo(target).Within(.001f),
                                $"a strike aimed at {target} with a {tall}-tall reel goes off at " +
                                $"{FlashY(target, tall)}");
        }

        [Test]
        public void TheBoltIsAboveTheStrikeAndTheGroundBurstBelowIt()
        {
            // The half that says the sign is right rather than merely consistent: what is over the
            // flash is the bolt and what is under it is the ground burst, so most of the sprite has
            // to be above whatever was hit. Reversed, this fixture's other case still passes at
            // target 0.
            const float Tall = 400f;

            float centre = SiegeView.StrikeCentre(0f, Tall);
            float head = centre + Tall * .5f;
            float foot = centre - Tall * .5f;

            Assert.That(head, Is.GreaterThan(0f), "the bolt has to run up out of the strike");
            Assert.That(foot, Is.LessThan(0f), "the ground burst has to spread below it");
            Assert.That(head, Is.GreaterThan(-foot * 3f),
                        "a strike is nearly all bolt - if the two ends are close to even, the " +
                        "reel is being drawn upside down");
        }

        /// <summary>
        /// The reel is flipped on the way to the screen, so the fraction the bake frames to and
        /// the fraction the board anchors by are complements. Two constants that have to stay
        /// that way, said once.
        /// </summary>
        [Test]
        public void TheFlashSitsLowInItsOwnFrame()
        {
            Assert.That(SiegeView.StrikeAt, Is.GreaterThan(0f).And.LessThan(.5f),
                        "what is above a strike's flash is the whole bolt and what is below it is " +
                        "only the ground burst, so the flash belongs in the lower half of the frame");
        }
    }
}
