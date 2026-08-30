using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That a mode is registered <em>whole</em>.
    ///
    /// <para>
    /// A way of playing is declared in two halves — <see cref="LevelMode"/> for its rules and
    /// <c>ModeLook</c> for its screen and its perch — because Domain may never reference
    /// Presentation. Two halves means a mode can be added to one and forgotten in the other, and
    /// the failure is quiet: a mode with no look routes to the classic screen and opens a level
    /// it cannot play; a look with no rules draws a switcher entry for a mode no chapter can
    /// ever belong to.
    /// </para>
    /// <para>
    /// These are the cheapest tests in the suite and they are the ones that make adding a fifth
    /// mode safe, which is the whole point of the registry existing.
    /// </para>
    /// </summary>
    public sealed class ModeRegistryTests
    {
        [Test]
        public void EveryModeHasAPermanentId()
        {
            foreach (var mode in LevelModes.All)
            {
                Assert.IsTrue(mode.Mode.IsValid, $"{mode.GetType().Name} has no id");
                Assert.IsTrue(mode.Mode.IsPlayable,
                              $"{mode.Mode} is registered but does not report as playable");
            }
        }

        [Test]
        public void NoTwoModesShareAnId()
        {
            var seen = new HashSet<string>();
            foreach (var mode in LevelModes.All)
                Assert.IsTrue(seen.Add(mode.Mode.Value),
                              $"two modes both call themselves '{mode.Mode}'");
        }

        [Test]
        public void TheClassicModeIsFirstAndStaysFirst()
        {
            // The switcher offers them in this order, and a player reaches for the first entry
            // without looking. A mode inserted ahead of the glade would move it under them.
            Assert.AreEqual(GameMode.Glade, LevelModes.All[0].Mode);
            Assert.AreEqual(GameMode.Glade, GameMode.Default);
        }

        [Test]
        public void ShippedIsTheRegistryRatherThanASecondList()
        {
            Assert.AreEqual(LevelModes.All.Count, GameMode.Shipped.Count);

            for (int i = 0; i < LevelModes.All.Count; i++)
                Assert.AreEqual(LevelModes.All[i].Mode, GameMode.Shipped[i]);
        }

        [Test]
        public void EveryModeCanBeFoundByItsOwnId()
        {
            foreach (var mode in LevelModes.All)
            {
                Assert.AreSame(mode, LevelModes.Find(mode.Mode));
                Assert.AreSame(mode, LevelModes.Find(mode.Mode.Value));
            }
        }

        [Test]
        public void AModeThisBuildDoesNotKnowIsSimplyNotFound()
        {
            // Content ships ahead of builds, so an unknown mode is content from the future. The
            // honest answer is to lose that chapter, not to guess at how to play it.
            Assert.IsNull(LevelModes.Find("frombeyond"));
            Assert.IsFalse(LevelModes.CanPlay(GameMode.None));
        }

        [Test]
        public void EveryModeClaimsItsOwnLevelAndNobodyElsesLevel()
        {
            // The one property the mapper rests on: exactly one mode answers for a given level.
            foreach (var mode in LevelModes.All)
            {
                var dto = Sample(mode.Mode);

                var claimant = LevelModes.Claimant(dto);
                Assert.IsNotNull(claimant, $"nothing claims a {mode.Mode} level");
                Assert.AreEqual(mode.Mode, claimant.Mode,
                                $"a {mode.Mode} level was claimed by {claimant.Mode}");

                int claims = 0;
                foreach (var other in LevelModes.All) if (other.Claims(dto)) claims++;
                Assert.AreEqual(1, claims, $"a {mode.Mode} level is claimed by {claims} modes");
            }
        }

        [Test]
        public void ALevelWithNothingAuthoredIsClaimedByNobody()
        {
            // It has to be refused rather than defaulted: a level nobody can play must be
            // reported, not silently opened as a glade with no grid.
            Assert.IsNull(LevelModes.Claimant(new LevelDto { id = "empty" }));
        }

        [Test]
        public void EveryModeReadsItsOwnLevelIntoRulesThatNameIt()
        {
            foreach (var mode in LevelModes.All)
            {
                var dto = Sample(mode.Mode);
                var problems = new List<string>();

                Assert.IsTrue(mode.TryRead(dto, LevelId.Parse("t_level"), problems, out var rules),
                              $"{mode.Mode} could not read its own level: "
                              + string.Join("; ", problems));

                Assert.IsNotNull(rules, $"{mode.Mode} read nothing");
                Assert.AreEqual(mode.Mode, rules.Mode,
                                $"{mode.Mode} produced rules that call themselves {rules.Mode}");
                Assert.IsEmpty(problems);
            }
        }

        [Test]
        public void EveryModeTunesItsOwnLevel()
        {
            foreach (var mode in LevelModes.All)
            {
                var dto = Sample(mode.Mode);
                mode.TryRead(dto, LevelId.Parse("t_level"), new List<string>(), out var rules);

                var tuning = mode.Tune(dto, rules);
                Assert.IsNotNull(tuning, $"{mode.Mode} tunes to nothing");
                Assert.GreaterOrEqual(tuning.Par, 1, $"{mode.Mode} tuned to a par below one");
            }
        }

        [Test]
        public void ALevelKnowsWhichModeItBelongsTo()
        {
            foreach (var mode in LevelModes.All)
            {
                var dto = Sample(mode.Mode);
                var problems = new List<string>();
                mode.TryRead(dto, LevelId.Parse("t_level"), problems, out var rules);

                var level = new LevelDefinition(LevelId.Parse("t_level"),
                                                ChapterId.Parse("t_chapter"), rules,
                                                mode.Tune(dto, rules),
                                                new LevelPresentation(default, null, null, "play_0"));

                Assert.AreEqual(mode.Mode, level.Mode);
                Assert.AreEqual(mode.Mode == GameMode.Glade, level.HasBoard,
                                "only the classic mode has a conduit board");
            }
        }

        [Test]
        public void EveryModeSaysWhatItsRecordIsCountedIn()
        {
            // The map badge and the victory panel quote one run in one format, so a mode that
            // named no stem would have them saying "turns" about something that has none.
            foreach (var mode in LevelModes.All)
                Assert.IsNotEmpty(mode.RecordStem, $"{mode.Mode} names no record wording");
        }

        // ------------------------------------------------------------ the other half
        [Test]
        public void EveryModeHasALookOfItsOwn()
        {
            // ModeLooks.Of falls back to the classic look rather than throwing, which is right
            // at run time — a map with an odd-looking node beats a map that will not open — and
            // exactly why it needs a test: without one, a mode registered in Domain and
            // forgotten in Presentation is invisible until somebody notices the wrong rock.
            foreach (var mode in LevelModes.All)
            {
                var look = ModeLooks.Of(mode.Mode);
                Assert.AreEqual(mode.Mode, look.Mode,
                                $"{mode.Mode} has no look of its own and fell back to "
                                + $"{look.Mode}");
            }
        }

        [Test]
        public void EveryLookBelongsToARegisteredMode()
        {
            foreach (var look in ModeLooks.All)
                Assert.IsNotNull(LevelModes.Find(look.Mode),
                                 $"'{look.Mode}' has a look but no rules, so no chapter can "
                                 + "ever belong to it");
        }

        [Test]
        public void EveryLookNamesAScreenThatCanPlayALevel()
        {
            foreach (var look in ModeLooks.All)
            {
                Assert.IsNotNull(look.Screen, $"{look.Mode} names no screen");

                Assert.IsTrue(typeof(View).IsAssignableFrom(look.Screen),
                              $"{look.Mode} names {look.Screen.Name}, which is not a screen");

                Assert.IsTrue(typeof(IPlaysLevel).IsAssignableFrom(look.Screen),
                              $"{look.Mode} names {look.Screen.Name}, which cannot be told "
                              + "which level to play - PlayRoute would open it empty");
            }
        }

        [Test]
        public void NoTwoModesShareAPerch()
        {
            // The floating tile under a node is the single agreed visual difference between
            // modes. Two modes sharing one makes their maps indistinguishable, which is the one
            // thing the design asks this art to prevent.
            var seen = new HashSet<string>();

            foreach (var look in ModeLooks.All)
            {
                Assert.IsNotEmpty(look.Perch, $"{look.Mode} has no perch");
                Assert.IsTrue(seen.Add(look.Perch),
                              $"{look.Mode} stands on '{look.Perch}', which another mode "
                              + "already uses");
            }
        }

        [Test]
        public void EveryPerchIsAnAddressTheGameActuallyLoads()
        {
            // A perch is fetched with Art.S, which answers null for an address nothing asked
            // for - and an Image with no sprite is a white rectangle, not a blank. So the one
            // failure this can have is a mode whose map draws a white square under every glade,
            // on a screen no compile, validator or content check looks at. AssetManifest is
            // Domain and ModeLooks is Presentation, so neither can read the other; this is the
            // only place the two can be asked whether they agree.
            var global = new HashSet<string>();
            foreach (var request in AssetManifest.GlobalAssets()) global.Add(request.Address);

            foreach (var look in ModeLooks.All)
                Assert.IsTrue(global.Contains(AssetManifest.MapArt(look.Perch)),
                              $"{look.Mode} stands on '{look.Perch}', which is not in "
                              + "AssetManifest.MapSprites - it would draw as a white square");
        }

        /// <summary>
        /// The smallest authored level of each mode.
        ///
        /// Written here rather than read from the shipped content on purpose: these tests are
        /// about the registry holding together, and a fixture that came from the catalog would
        /// start passing or failing for reasons about the catalog instead.
        /// </summary>
        static LevelDto Sample(GameMode mode)
        {
            var dto = new LevelDto { id = "t_level" };

            if (mode == GameMode.Glade)
                dto.rows = new[] { "*R -EW", "#R ." };
            else if (mode == GameMode.Fall)
                dto.fall = new FallDto
                {
                    // A well now authors what is standing in it and what it deals, because par
                    // is searched from the two. Small on purpose: this case is about the
                    // registry reading its own block, not about a board being interesting.
                    width = 6,
                    height = 6,
                    rows = new[] { "......", "......", "......", "......", "......", "YY...." },
                    motes = "BGR",
                };
            else if (mode == GameMode.Keeper)
                dto.keeper = new KeeperDto
                {
                    // A grove now authors its ground and what it deals, because par is searched
                    // from the two. Small on purpose: this case is about the registry reading its
                    // own block, not about a board being interesting.
                    width = 6,
                    height = 4,
                    rows = new[] { "......", "..G...", ".R*B..", "......" },
                    tiles = "GRB",
                };
            else if (mode == GameMode.Bud)
                dto.bud = new BudDto
                {
                    // A grove authors its ground and what it deals, because par is searched from
                    // the two. Small on purpose: this case is about the registry reading its own
                    // block, not about a board being interesting.
                    //
                    // The basket used to be missing here and this fixture was the only thing in
                    // the repository that noticed — the mode gained it after this sample was
                    // written, exactly as the well one over did, and a mode that cannot read its
                    // own level is a mode whose chapter would ship as a skipped one.
                    // It also has to be *solvable*, which a grid and a basket alone are not:
                    // this mode searches for par, so an unwinnable sample spends the whole node
                    // budget proving nothing and then logs the refusal the build gate exists to
                    // raise. One tap here — green into the red — makes three touching yellows,
                    // which burst and crack the cocoon beside them. Par 1.
                    width = 4,
                    height = 4,
                    rows = new[] { "....", ".RY.", ".Yo.", "...." },
                    colours = "GRB",
                };
            else
                Assert.Fail($"ModeRegistryTests has no sample level for '{mode}'. A mode was "
                            + "registered without teaching these tests what one of its levels "
                            + "looks like - which is the check, not an oversight in the test.");

            return dto;
        }
    }
}
