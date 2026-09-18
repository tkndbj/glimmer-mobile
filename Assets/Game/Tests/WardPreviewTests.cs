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
        /// <b>A turret the player owns can always be put on the line.</b> That is the clause that
        /// was missing, and it may not depend on whether a star happens to be for sale.
        /// </summary>
        [Test]
        public void AHeldTurretCanAlwaysBeEquipped()
        {
            foreach (bool standing in new[] { false, true })
                foreach (bool rises in new[] { false, true })
                {
                    var keys = WardPreviewKeys.For(true, standing, rises, spare: true);

                    Assert.IsTrue(keys.Lower,
                                  $"a held turret (standing {standing}, a star to sell {rises}) "
                                  + "offers no way onto the line");

                    Assert.AreEqual(standing, keys.Equipped,
                                    "the lower key says where the turret stands");

                    Assert.IsFalse(keys.Buys,
                                   "a copy is free for this seat, so the lower key equips it "
                                   + "rather than selling another");
                }
        }

        /// <summary>
        /// <b>A turret the player owns whose every copy is standing elsewhere is offered the copy
        /// that would put it here</b> — which is the clause copies cost this rule.
        ///
        /// A colourless turret is held on all four seats by one purchase
        /// (<c>WardHolding.Row</c>), so "held" stopped implying "can be stood": the seat is real,
        /// the entitlement is real, and there is no spare. EQUIP over that is a key that does
        /// nothing, which is this fixture's own bug wearing a different hat.
        /// </summary>
        [Test]
        public void AHeldTurretWithNoSpareCopyIsSoldAnother()
        {
            foreach (bool rises in new[] { false, true })
            {
                var keys = WardPreviewKeys.For(true, standing: false, rises: rises, spare: false);

                Assert.IsTrue(keys.Lower, "no key at all over a turret that cannot be stood");
                Assert.IsTrue(keys.Buys, "the lower key has to be the way onto this seat");
                Assert.IsFalse(keys.Equipped, "a price is not the settled state");
                Assert.AreEqual(rises, keys.Upper, "the star is still for sale above it");
            }
        }

        /// <summary>
        /// <b>A seat already standing the turret never asks for another copy</b>, however the
        /// spare count came out — it is not asking for one.
        /// </summary>
        [Test]
        public void AStandingTurretIsNeverSoldAnother()
        {
            foreach (bool spare in new[] { false, true })
            {
                var keys = WardPreviewKeys.For(true, standing: true, rises: false, spare: spare);

                Assert.IsTrue(keys.Equipped);
                Assert.IsFalse(keys.Buys, "the turret is already on this seat");
            }
        }

        /// <summary>Every state offers something to tap, whatever else it does.</summary>
        [Test]
        public void EveryStateOffersAKey()
        {
            foreach (bool held in new[] { false, true })
                foreach (bool standing in new[] { false, true })
                    foreach (bool rises in new[] { false, true })
                        foreach (bool spare in new[] { false, true })
                        {
                            var keys = WardPreviewKeys.For(held, standing, rises, spare);

                            Assert.IsTrue(keys.Upper || keys.Lower,
                                          $"held {held}, standing {standing}, rises {rises}, "
                                          + $"spare {spare} draws no key at all");

                            Assert.IsFalse(keys.Buys && keys.Equipped,
                                           "the lower key cannot be a price and a settled state "
                                           + "at once");

                            Assert.IsFalse(keys.Buys && !held,
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
                var keys = WardPreviewKeys.For(false, false, rises, spare: false);

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
            var keys = WardPreviewKeys.For(true, false, true, spare: true);

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
            var keys = WardPreviewKeys.For(true, false, false, spare: true);

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
