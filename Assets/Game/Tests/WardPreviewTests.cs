using GlimmerGrove.Layout;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What the turret preview panel offers, and that it fits.
    ///
    /// <para>
    /// <b>This fixture exists because of a bug no gate could have caught.</b> The panel offered one
    /// key and chose what it said: for a turret the player owned it sold the next star, and only
    /// fell back to standing it when there was none left. A turret starts at one star, so there was
    /// always one left — and every turret a player owned and had not stood offered <c>UPGRADE</c>
    /// and nothing else. There was no way to equip a second turret at all, and the compile, the
    /// content gates, the art gates and 1,757 tests were green throughout, because every branch is
    /// correct and nothing asks whether their union covers the states.
    /// </para>
    /// <para>
    /// So what is asserted is the <em>property</em> rather than the branches — see
    /// <see cref="WardPreviewKeys"/>.
    /// </para>
    /// </summary>
    public sealed class WardPreviewTests
    {
        /// <summary>
        /// <b>A turret the player owns on this seat can always be put on the line.</b> That is the
        /// clause that was missing, and it may not depend on whether a star happens to be for
        /// sale.
        ///
        /// <b>It is unconditional again.</b> While the legendary band was bought outright, held
        /// stopped implying standable — a turret could be owned on a seat with no copy free for
        /// it — and the lower key had to be able to sell that copy (invariant 42k). A turret is
        /// bought per seat again, so owning one here <em>is</em> owning this seat.
        /// </summary>
        [Test]
        public void AHeldTurretCanAlwaysBeEquipped()
        {
            foreach (bool standing in new[] { false, true })
                foreach (bool rises in new[] { false, true })
                {
                    var keys = WardPreviewKeys.For(true, standing, rises);

                    Assert.IsTrue(keys.Lower,
                                  $"a held turret (standing {standing}, a star to sell {rises}) "
                                  + "offers no way onto the line");

                    Assert.AreEqual(standing, keys.Equipped,
                                    "the lower key says where the turret stands");
                }
        }

        /// <summary>Every state offers something to tap, whatever else it does.</summary>
        [Test]
        public void EveryStateOffersAKey()
        {
            foreach (bool held in new[] { false, true })
                foreach (bool standing in new[] { false, true })
                    foreach (bool rises in new[] { false, true })
                    {
                        var keys = WardPreviewKeys.For(held, standing, rises);

                        Assert.IsTrue(keys.Upper || keys.Lower,
                                      $"held {held}, standing {standing}, rises {rises} "
                                      + "draws no key at all");

                        Assert.IsFalse(keys.Equipped && !held,
                                       "a turret nobody owns cannot be the settled state");

                        Assert.IsFalse(keys.Lower && !held,
                                       "a turret nobody owns is sold by the upper key");
                    }
        }

        /// <summary>
        /// A turret nobody owns has one key and it is the upper one: there is nothing to stand and
        /// nothing to upgrade, so the panel is a price or the wall in front of one.
        /// </summary>
        [Test]
        public void AnUnheldTurretHasOneKeyAndItIsThePrice()
        {
            foreach (bool rises in new[] { false, true })
            {
                var keys = WardPreviewKeys.For(false, false, rises);

                Assert.IsTrue(keys.Upper);
                Assert.IsFalse(keys.Lower, "a turret nobody owns was offered a way onto the line");
                Assert.IsTrue(keys.Alone, "a lone key takes the middle of the band");
            }
        }

        /// <summary>
        /// <b>Both keys, and that is the state the whole panel was rebuilt for:</b> a turret the
        /// player owns, has not stood, and could still buy a star for.
        /// </summary>
        [Test]
        public void AHeldTurretWithAStarLeftOffersBoth()
        {
            var keys = WardPreviewKeys.For(true, false, true);

            Assert.IsTrue(keys.Upper, "no upgrade key");
            Assert.IsTrue(keys.Lower, "no equip key - this is the bug this fixture is named for");
            Assert.IsFalse(keys.Alone, "two keys are not alone");
            Assert.IsFalse(keys.Equipped);
        }

        /// <summary>
        /// A held turret at the top of the ladder has the equip key alone, centred in the band.
        /// </summary>
        [Test]
        public void AMaxedHeldTurretOffersTheEquipKeyAlone()
        {
            var keys = WardPreviewKeys.For(true, false, false);

            Assert.IsFalse(keys.Upper, "a turret with no star left was offered one");
            Assert.IsTrue(keys.Lower);
            Assert.IsTrue(keys.Alone);
        }

        /// <summary>
        /// The panel fits the shortest canvas this game is drawn on, title and all.
        ///
        /// <b>Asserted because it just grew.</b> The key band is two keys tall now whether or not
        /// both are shown, which is what stops the panel resizing under a finger — and a panel
        /// measured against nothing is a panel that draws its own button off the bottom edge, which
        /// is the mistake <c>WardPreviewOverlay.PanelH</c>'s own remark names.
        /// </summary>
        [Test]
        public void ThePanelFitsTheShortestCanvas()
        {
            Assert.LessOrEqual(WardPreviewOverlay.PanelH, PanelStack.TallestPanel,
                               $"the turret preview reaches {WardPreviewOverlay.PanelH} and the "
                               + $"shortest canvas holds {PanelStack.TallestPanel}. The stage is "
                               + "640 of it - shrink that before anything else");
        }
    }
}
