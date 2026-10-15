using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Shuffle lane's live tuning (MODES.md 59l): the build's own figures are the code, a
    /// published document is read whole or refused whole, a run plays the figures it opened on,
    /// a card's text follows its figures, and the device keeps what it last read.
    ///
    /// <para>
    /// <b>The first two fixtures are the ones that matter to every player today</b>: the shipped
    /// tuning <em>is</em> the constants - the same card objects, the same arrays, the same floats
    /// - and the file the seeder publishes is accepted and, at revision 1, names exactly those
    /// figures. Together they are the proof that turning tuning on changed nothing about the lane.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ShuffleTuningTests
    {
        static string PublishedPath()
            => Path.Combine(TestJson.RepoRoot(), "firebase", "seed", "shuffle.json");

        static ShuffleTuning Read(string json, bool strict = true)
        {
            Assert.IsTrue(ShuffleTuning.TryRead(json, strict, out var tuning, out string fault), fault);
            return tuning;
        }

        static string Refused(string json, bool strict = true)
        {
            Assert.IsFalse(ShuffleTuning.TryRead(json, strict, out var tuning, out string fault),
                           "a document that should have been refused was read");
            Assert.IsNull(tuning);
            Assert.IsFalse(string.IsNullOrEmpty(fault), "a refusal named no fault");
            return fault;
        }

        /// <summary>The build's own figures as a document, at <paramref name="revision"/>.</summary>
        static string Doc(int revision = 2) => ShuffleTuning.Shipped.ToJson(revision);

        /// <summary>A document with one section figure changed (the first <c>"key": n</c>).</summary>
        static string With(string json, string key, string value)
        {
            var pattern = new Regex("\"" + Regex.Escape(key) + "\": (\\[[^\\]]*\\]|-?\\d+)");
            Assert.IsTrue(pattern.IsMatch(json), $"no '{key}' in the document");
            return pattern.Replace(json, "\"" + key + "\": " + value, 1);
        }

        /// <summary>A document with one card's field changed.</summary>
        static string Card(string json, string id, string field, string value)
        {
            var row = new Regex("\\{ \"id\": \"" + Regex.Escape(id) + "\"[^}]*\\}");
            var m = row.Match(json);
            Assert.IsTrue(m.Success, $"no card '{id}' in the document");

            var cell = new Regex("\"" + field + "\": (true|false|-?\\d+)");
            Assert.IsTrue(cell.IsMatch(m.Value), $"card '{id}' has no '{field}'");

            return json.Substring(0, m.Index) + cell.Replace(m.Value, "\"" + field + "\": " + value, 1)
                 + json.Substring(m.Index + m.Length);
        }

        static string Without(string json, string id)
        {
            var row = new Regex("\\s*\\{ \"id\": \"" + Regex.Escape(id) + "\"[^}]*\\},");
            Assert.IsTrue(row.IsMatch(json), $"no card '{id}' to remove");
            return row.Replace(json, "", 1);
        }

        // ------------------------------------------------------------------ nothing changed
        [Test]
        public void TheShippedTuningIsTheCodeItself()
        {
            var s = ShuffleTuning.Shipped;

            Assert.AreEqual(0, s.Revision);
            Assert.AreSame(ShuffleCards.All, s.Cards, "the shipped tuning deals a copy of the catalog, not the catalog");

            var r = s.Ramp;
            Assert.AreEqual(ShuffleRamp.FirstWave, r.FirstWave);
            Assert.AreEqual(ShuffleRamp.MostRaiders, r.MostRaiders);
            Assert.AreEqual(ShuffleRamp.HealthStepTwentieths, r.HealthStepTwentieths);
            Assert.AreEqual(ShuffleRamp.BlowEveryWaves, r.BlowEveryWaves);
            Assert.AreEqual(ShuffleRamp.BrutesFrom, r.BrutesFrom);
            Assert.AreEqual(ShuffleRamp.BulwarksFrom, r.BulwarksFrom);
            Assert.AreEqual(ShuffleRamp.BruteShareMost, r.BruteShareMost);
            Assert.AreEqual(ShuffleRamp.BulwarkShareMost, r.BulwarkShareMost);
            Assert.AreEqual(ShuffleRamp.ShareStep, r.ShareStep);
            Assert.AreEqual(ShuffleRamp.CursedWave, r.CursedWave);
            Assert.AreEqual(ShuffleRamp.VoidWave, r.VoidWave);
            Assert.AreEqual(ShuffleRamp.SurgeFrom, r.SurgeFrom);
            Assert.AreEqual(ShuffleRamp.SurgeEvery, r.SurgeEvery);
            Assert.AreEqual(ShuffleRamp.SurgeHealthPercent, r.SurgeHealthPercent);
            Assert.AreEqual(ShuffleRamp.SurgeHealthPercentStep, r.SurgeHealthPercentStep);
            Assert.AreEqual(ShuffleRamp.SurgeBlowPercent, r.SurgeBlowPercent);
            Assert.AreEqual(ShuffleRamp.SurgeBlowPercentStep, r.SurgeBlowPercentStep);
            Assert.AreEqual(ShuffleRamp.SurgePacePercent, r.SurgePacePercent);
            Assert.AreEqual(ShuffleRamp.SurgePacePercentStep, r.SurgePacePercentStep);
            Assert.AreEqual(ShuffleRamp.SurgePaceMostHundredths, r.SurgePaceMostHundredths);

            var d = s.Deck;
            CollectionAssert.AreEqual(ShuffleDeck.Weights, d.Opening);
            CollectionAssert.AreEqual(ShuffleDeck.Heated, d.Heated);
            CollectionAssert.AreEqual(ShuffleDeck.Wild, d.Wild);
            Assert.AreEqual(ShuffleDeck.HeatedFrom, d.HeatedFrom);
            Assert.AreEqual(ShuffleDeck.WildFrom, d.WildFrom);
            Assert.AreEqual(ShuffleDeck.RarePity, d.RarePity);
            Assert.AreEqual(ShuffleDeck.EpicPity, d.EpicPity);

            for (int hand = 1; hand <= 30; hand++)
                Assert.AreSame(ShuffleDeck.WeightsFor(hand), d.TableFor(hand), $"hand {hand} deals off another table");

            var a = s.Arsenal;
            Assert.AreSame(SiegeArsenalTuning.Shipped, a);
            Assert.AreEqual(SiegeArsenal.TeslaEvery, a.TeslaEvery);
            Assert.AreEqual(SiegeArsenal.TeslaFloor, a.TeslaFloor);
            Assert.AreEqual(SiegeArsenal.TeslaSpan, a.TeslaSpan);
            Assert.AreEqual(SiegeArsenal.ToxicTick, a.ToxicTick);
            Assert.AreEqual(SiegeArsenal.QuakeShove, a.QuakeShove);
            Assert.AreEqual(SiegeArsenal.ShatterChillTenths, a.ShatterChillTenths);
            Assert.AreEqual(SiegeArsenal.ShatterChillFor, a.ShatterChillFor);
            Assert.AreEqual(SiegeArsenal.WildfireFor, a.WildfireFor);
            Assert.AreEqual(SiegeArsenal.MeteorFall, a.MeteorFall);
            Assert.AreEqual(SiegeArsenal.MeteorBurnTenths, a.MeteorBurnTenths);
            Assert.AreEqual(SiegeArsenal.MeteorBurnFor, a.MeteorBurnFor);
            Assert.AreEqual(SiegeArsenal.VortexFor, a.VortexFor);
            Assert.AreEqual(SiegeArsenal.VortexTick, a.VortexTick);
            Assert.AreEqual(SiegeArsenal.VortexDrag, a.VortexDrag);
            Assert.AreEqual(SiegeArsenal.VortexGather, a.VortexGather);
            Assert.AreEqual(SiegeArsenal.RayFor, a.RayFor);
            Assert.AreEqual(SiegeArsenal.RayTick, a.RayTick);
            Assert.AreEqual(SiegeArsenal.RayWidth, a.RayWidth);
            Assert.AreEqual(SiegeArsenal.NukeFall, a.NukeFall);
            Assert.AreEqual(SiegeArsenal.WildBurnTenths, a.WildBurnTenths);
            Assert.AreEqual(SiegeArsenal.WildVenomTenths, a.WildVenomTenths);
            Assert.AreEqual(SiegeArsenal.WildChillTenths, a.WildChillTenths);
            Assert.AreEqual(SiegeArsenal.WildArcTenths, a.WildArcTenths);
            Assert.AreEqual(SiegeArsenal.WildArcHops, a.WildArcHops);
            Assert.AreEqual(SiegeArsenal.WildBurnFor, a.WildBurnFor);
            Assert.AreEqual(SiegeArsenal.WildVenomFor, a.WildVenomFor);
            Assert.AreEqual(SiegeArsenal.WildChillFor, a.WildChillFor);

            // The plain line carries the shipped arsenal and never reaches it.
            Assert.AreSame(SiegeArsenalTuning.Shipped, SiegeBoosts.None.Arsenal);
            Assert.AreSame(SiegeArsenalTuning.Shipped, new SiegeBoosts(7u).Arsenal);
        }

        [Test]
        public void ThePublishedFileIsAcceptedAndRevisionOneIsTheBuildsOwnFigures()
        {
            string path = PublishedPath();
            Assert.IsTrue(File.Exists(path), "missing " + path);

            var published = Read(File.ReadAllText(path), strict: true);
            Assert.GreaterOrEqual(published.Revision, 1);

            // What the file moves away from the build, printed for whoever publishes it.
            var moved = Differences(ShuffleTuning.Shipped, published);
            Console.WriteLine($"firebase/seed/shuffle.json is revision {published.Revision}; " +
                                      $"{moved.Count} figure(s) differ from this build's own:");
            foreach (var line in moved) Console.WriteLine("  " + line);

            // Revision 1 is defined as the build's own figures, published: the first document out
            // must play the lane exactly as it played before tuning existed.
            if (published.Revision == 1)
            {
                CollectionAssert.IsEmpty(moved, "revision 1 is not the build's own figures");
                Assert.AreSame(ShuffleCards.All, published.Cards);
                Assert.AreSame(ShuffleRampTuning.Shipped, published.Ramp);
                Assert.AreSame(ShuffleDeckTuning.Shipped, published.Deck);
                Assert.AreSame(SiegeArsenalTuning.Shipped, published.Arsenal);
            }
        }

        [Test]
        public void TheShippedTuningRoundTripsToItsOwnObjects()
        {
            var back = Read(Doc(5));

            Assert.AreEqual(5, back.Revision);
            Assert.AreSame(ShuffleCards.All, back.Cards);
            Assert.AreSame(ShuffleRampTuning.Shipped, back.Ramp);
            Assert.AreSame(ShuffleDeckTuning.Shipped, back.Deck);
            Assert.AreSame(SiegeArsenalTuning.Shipped, back.Arsenal);
        }

        [Test]
        public void AHundredthIsTheFloatTheConstantWas()
        {
            // One figure moved, so every other figure goes through the hundredths path rather than
            // the shipped object, and must land on the constant's exact float.
            var tuned = Read(With(Doc(), "teslaEveryHundredths", "200")).Arsenal;

            Assert.AreNotSame(SiegeArsenalTuning.Shipped, tuned);
            Assert.AreEqual(2f, tuned.TeslaEvery);
            Assert.AreEqual(SiegeArsenal.TeslaFloor, tuned.TeslaFloor);
            Assert.AreEqual(SiegeArsenal.QuakeShove, tuned.QuakeShove);
            Assert.AreEqual(SiegeArsenal.VortexDrag, tuned.VortexDrag);
            Assert.AreEqual(SiegeArsenal.RayTick, tuned.RayTick);
            Assert.AreEqual(SiegeArsenal.MeteorFall, tuned.MeteorFall);
            Assert.AreEqual(SiegeArsenal.NukeFall, tuned.NukeFall);
            Assert.AreEqual(SiegeArsenal.ShatterChillFor, tuned.ShatterChillFor);
            Assert.AreEqual(SiegeArsenal.VortexGather, tuned.VortexGather);
        }

        // ------------------------------------------------------------------ refused whole
        [Test]
        public void AMalformedOrWrongShapedDocumentIsRefused()
        {
            Refused("");
            Refused("not json");
            Refused("[1, 2]");
            Refused(Doc() + "x");
            Refused(Doc().Replace("\"schema\": 1", "\"schema\": 2"));
            Refused(Doc().Replace("\"revision\": 2", "\"revision\": 0"));
            Refused(With(Doc(), "firstWave", "4.5"));
            Refused(Doc().Replace("\"firstWave\": 4,", "\"firstWave\": 4, \"firstWave\": 5,"));
            Refused(Doc().Replace("\"firstWave\": 4,", ""));            // a missing figure is not a nought
            Refused(new string(' ', ShuffleTuning.MostLength + 1));
        }

        [Test]
        public void AFigureOutsideItsRangeIsRefusedAndNamed()
        {
            StringAssert.Contains("ramp.firstWave", Refused(With(Doc(), "firstWave", "0")));
            StringAssert.Contains("ramp.mostRaiders", Refused(With(Doc(), "mostRaiders", "3")));
            StringAssert.Contains("ramp.healthStepTwentieths", Refused(With(Doc(), "healthStepTwentieths", "0")));
            StringAssert.Contains("ramp.surgeFromWave", Refused(With(Doc(), "surgeFromWave", "0")));
            StringAssert.Contains("ramp.surgeEveryWaves", Refused(With(Doc(), "surgeEveryWaves", "0")));
            StringAssert.Contains("ramp.surgeHealthPercent", Refused(With(Doc(), "surgeHealthPercent", "51")));
            StringAssert.Contains("ramp.surgeBlowPercentStep", Refused(With(Doc(), "surgeBlowPercentStep", "-1")));
            StringAssert.Contains("deck.opening", Refused(With(Doc(), "opening", "[56, 30, 11, 4]")));
            StringAssert.Contains("deck.heated", Refused(With(Doc(), "heated", "[50, 50, 0]")));
            StringAssert.Contains("deck.wildFromHand", Refused(With(Doc(), "wildFromHand", "6")));
            StringAssert.Contains("rareEveryHands", Refused(With(Doc(), "rareEveryHands", "1")));
            StringAssert.Contains("arsenal.teslaFloorHundredths", Refused(With(Doc(), "teslaFloorHundredths", "95")));
            StringAssert.Contains("arsenal.wildArcHops", Refused(With(Doc(), "wildArcHops", "5")));
            StringAssert.Contains("cards.heavy_bolts.magnitude", Refused(Card(Doc(), "heavy_bolts", "magnitude", "61")));
            StringAssert.Contains("cards.heavy_bolts.extent", Refused(Card(Doc(), "heavy_bolts", "extent", "3")));
            StringAssert.Contains("cards.phoenix.most", Refused(Card(Doc(), "phoenix", "most", "2")));
            StringAssert.Contains("cards.heavy_bolts.most", Refused(Card(Doc(), "heavy_bolts", "most", "11")));
            StringAssert.Contains("cards.frostbite.magnitude", Refused(Card(Doc(), "frostbite", "magnitude", "10")));
        }

        [Test]
        public void AFigureTheCardsTextSaysInWordsCannotMove()
        {
            // "Every 15 kills, a nuke ..." - fifteen kills is a word in Polish and the Arabic plural.
            StringAssert.Contains("in words", Refused(Card(Doc(), "doomsday", "extent", "20")));
            StringAssert.Contains("in words", Refused(Card(Doc(), "brace", "magnitude", "2")));
            StringAssert.Contains("in words", Refused(Card(Doc(), "bullet_hell", "magnitude", "120")));

            // A per cent the text prints is a slot, so it moves.
            Assert.AreEqual(30, Read(Card(Doc(), "heavy_bolts", "magnitude", "30")).Find("heavy_bolts").Magnitude);
            // A figure the text never mentions moves too.
            Assert.AreEqual(150, Read(Card(Doc(), "thorns", "magnitude", "150")).Find("thorns").Magnitude);
        }

        [Test]
        public void TheWidestCardTheRangesAllowStillLeavesABoardThatRuns()
        {
            // Execution's line climbs ten a copy, so the ranges are what keep it short of every
            // raider: the most magnitude at the most copies folds to exactly the 90% ceiling.
            var tuning = Read(Card(Card(Doc(), "execution", "magnitude", "60"), "execution", "most", "4"));
            var run = new ShuffleRun(5u, tuning);
            var card = tuning.Find("execution");

            for (int n = 0; n < 4; n++) Assert.IsTrue(run.Build.Take(card));
            Assert.AreEqual(90, run.Build.Boosts.ExecutePercent);

            // And the ceiling itself is held at the range as well as the fold.
            StringAssert.Contains("cards.execution.magnitude", Refused(Card(Doc(), "execution", "magnitude", "61")));
            StringAssert.Contains("cards.doomsday.magnitude", Refused(Card(Doc(), "doomsday", "magnitude", "91")));
            StringAssert.Contains("cards.last_stand.extent", Refused(Card(Doc(), "last_stand", "extent", "91")));
        }

        [Test]
        public void TheStrictReadingRefusesWhatTheDeviceForgives()
        {
            string unknownKey = With(Doc(), "firstWave", "4, \"firstWaev\": 6");
            StringAssert.Contains("firstWaev", Refused(unknownKey, strict: true));
            Read(unknownKey, strict: false);

            string unknownCard = Doc().Replace("\"cards\": [", "\"cards\": [\n    { \"id\": \"comet_tail\", \"magnitude\": 5, \"extent\": 0, \"most\": 2, \"enabled\": true },");
            StringAssert.Contains("comet_tail", Refused(unknownCard, strict: true));
            Assert.IsNull(Read(unknownCard, strict: false).Find("comet_tail"));

            string retired = Doc().Replace("\"cards\": [", "\"cards\": [\n    { \"id\": \"charm_magnet\", \"magnitude\": 5, \"extent\": 0, \"most\": 2, \"enabled\": true },");
            StringAssert.Contains("retired", Refused(retired, strict: true));

            string leftOut = Without(Doc(), "heavy_bolts");
            StringAssert.Contains("heavy_bolts", Refused(leftOut, strict: true));
            Assert.AreSame(ShuffleCards.Find("heavy_bolts"), Read(leftOut, strict: false).Find("heavy_bolts"),
                           "a device keeps a card the document left out at its shipped figures");

            string twice = Doc().Replace("\"cards\": [", "\"cards\": [\n    { \"id\": \"heavy_bolts\", \"magnitude\": 15, \"extent\": 0, \"most\": 5, \"enabled\": true },");
            StringAssert.Contains("twice", Refused(twice, strict: false));

            // A comment is never a figure.
            Read(Doc().Replace("\"schema\": 1,", "\"_note\": \"why\", \"schema\": 1,"), strict: true);
        }

        [Test]
        public void AHandNeedsThreeCardsSwitchedOn()
        {
            string json = Doc();
            foreach (var card in ShuffleCards.All) json = Card(json, card.Id, "enabled", "false");

            json = Card(Card(json, "heavy_bolts", "enabled", "true"), "phoenix", "enabled", "true");
            StringAssert.Contains("switched on", Refused(json));

            Assert.AreEqual(3, Read(Card(json, "thorns", "enabled", "true")).Cards.Count);
        }

        // ------------------------------------------------------------------ a run plays its tuning
        [Test]
        public void ATunedCardFoldsAtItsTunedFigure()
        {
            var tuning = Read(Card(Doc(), "heavy_bolts", "magnitude", "30"));
            var run = new ShuffleRun(11u, tuning);
            var card = tuning.Find("heavy_bolts");

            Assert.AreNotSame(ShuffleCards.Find("heavy_bolts"), card);
            Assert.AreEqual(ShuffleTier.Common, card.Tier);
            Assert.AreEqual(ShuffleCards.Find("heavy_bolts").Picture, card.Picture);

            Assert.IsTrue(run.Build.Take(card));
            Assert.AreEqual(130, run.Build.Boosts.DamagePercent);
            Assert.AreSame(tuning.Arsenal, run.Build.Boosts.Arsenal);
        }

        [Test]
        public void ASwitchedOffCardIsNeverDealt()
        {
            var tuning = Read(Card(Doc(), "phoenix", "enabled", "false"));
            Assert.IsNull(tuning.Find("phoenix"));

            for (uint seed = 1; seed <= 40; seed++)
            {
                var run = new ShuffleRun(seed, tuning);

                for (int hand = 0; hand < 30; hand++)
                {
                    var dealt = run.Deal();
                    foreach (var card in dealt) Assert.AreNotEqual("phoenix", card.Id, $"seed {seed} dealt a switched-off card");
                    if (dealt.Count > 0) run.Take(dealt[0]);
                }
            }
        }

        [Test]
        public void TheShippedFileDealsTheShippedDeckCardForCard()
        {
            var published = Read(Doc(1));

            for (uint seed = 1; seed <= 25; seed++)
            {
                var plain = new ShuffleRun(seed);
                var tuned = new ShuffleRun(seed, published);

                for (int hand = 0; hand < 25; hand++)
                {
                    var a = plain.Deal();
                    var b = tuned.Deal();

                    Assert.AreEqual(a.Count, b.Count, $"seed {seed} hand {hand}");
                    for (int i = 0; i < a.Count; i++) Assert.AreSame(a[i], b[i], $"seed {seed} hand {hand} slot {i}");
                    if (a.Count == 0) break;

                    Assert.IsTrue(plain.Take(a[a.Count - 1]));
                    Assert.IsTrue(tuned.Take(b[b.Count - 1]));
                }
            }
        }

        [Test]
        public void TheTunedDeckDealsOffItsOwnTables()
        {
            // Every hand opens on a legendary when the opening table is all legendary.
            var tuning = Read(With(Doc(), "opening", "[0, 0, 0, 100]"));
            var run = new ShuffleRun(3u, tuning);

            var hand = run.Deal();
            Assert.AreEqual(ShuffleTier.Legendary, hand[0].Tier);
        }

        [Test]
        public void TheRampAnswersWithWhatItWasLastHandedAndTheLayoutDealsAgain()
        {
            var ramp = new ShuffleRamp("rgby", 24, .5f);
            Assert.IsTrue(ProtoGrid.TryRead(new[] { "gbryrrgb", "ggyrbgry", "rybbgbyy", "brgbyygg", "bryrbrgr" },
                                            8, 5, SiegeLayout.Cells, out var grid, out string error), error);
            var layout = new SiegeLayout(grid, "rgby", "rgby", new string[0], "", 0, ramp, 0, "plsfha", true, true);
            Assert.IsNull(layout.Fault, layout.Fault);

            Assert.AreEqual(ShuffleRamp.FirstWave, layout.SizeOf(0));
            Assert.AreEqual(ShuffleRamp.CursedWave, ramp.CursesFrom);
            int edition = ramp.Edition;

            var tuned = Read(With(With(Doc(), "firstWave", "7"), "cursedWave", "3"));
            ramp.Use(tuned.Ramp);

            Assert.AreNotEqual(edition, ramp.Edition);
            Assert.AreEqual(7, layout.SizeOf(0), "the layout kept a wave it memoised under the last run's figures");
            Assert.AreEqual(3, ramp.CursesFrom);
            Assert.AreEqual(ShuffleRamp.SurgeAt(5).HealthTenths, ramp.SurgeFor(5).HealthTenths);

            edition = ramp.Edition;
            ramp.Use(tuned.Ramp);
            Assert.AreEqual(edition, ramp.Edition, "the same figures handed twice are a new edition");

            ramp.Use(null);
            Assert.AreSame(ShuffleRampTuning.Shipped, ramp.Tuning);
            Assert.AreEqual(ShuffleRamp.FirstWave, layout.SizeOf(0));
        }

        [Test]
        public void ARampFigureMovesEveryWaveTheShippedRampDeals()
        {
            var tuned = Read(With(With(With(Doc(), "healthStepTwentieths", "6"), "brutesFrom", "1"), "shareStep", "50")).Ramp;

            Assert.AreEqual(ShuffleRamp.SurgeAt(1).HealthTenths, ShuffleRamp.SurgeAt(1, tuned).HealthTenths, "wave one is the plain figure either way");
            Assert.Greater(ShuffleRamp.SurgeAt(10, tuned).HealthTenths, ShuffleRamp.SurgeAt(10).HealthTenths);

            int brutes = 0;
            for (int i = 0; i < 40; i++)
                if (ShuffleRamp.KindAt(2, i, 99u, tuned) == SiegeKind.Brute) brutes++;

            Assert.Greater(brutes, 0, "brutes from wave one at fifty a wave sent none on wave two");
        }

        [Test]
        public void TheDeepSurgeIsTheDocumentsAndNoughtSwitchesItOff()
        {
            // Earlier and steeper: wave 25 surges where the shipped ramp is still plain.
            var sooner = Read(With(With(Doc(), "surgeFromWave", "20"), "surgeHealthPercent", "10")).Ramp;
            Assert.AreEqual(ShuffleRamp.SurgeAt(19).HealthTenths, ShuffleRamp.SurgeAt(19, sooner).HealthTenths, "before the surge nothing moved");
            Assert.Greater(ShuffleRamp.SurgeAt(25, sooner).HealthTenths, ShuffleRamp.SurgeAt(25).HealthTenths);
            Assert.Greater(ShuffleRamp.SurgeAt(60, sooner).HealthTenths, ShuffleRamp.SurgeAt(60).HealthTenths);

            // A steeper health surge reaches its ceiling sooner, and the pace follows it there
            // with no figure of its own moved.
            Assert.Less(sooner.HealthCeilingWave, ShuffleRampTuning.Shipped.HealthCeilingWave);
            Assert.AreEqual(100, ShuffleRamp.SurgeAt(sooner.HealthCeilingWave, sooner).PaceHundredths, "the pace moved on the ceiling's own wave");
            Assert.Greater(ShuffleRamp.SurgeAt(sooner.HealthCeilingWave + 1, sooner).PaceHundredths, 100, "the pace never followed the ceiling");

            // Every per cent at nought: the plain ramp at every depth, at its own pace, as before
            // the surge existed.
            var off = Read(With(With(With(With(With(With(Doc(), "surgeHealthPercent", "0"), "surgeHealthPercentStep", "0"),
                                               "surgeBlowPercent", "0"), "surgeBlowPercentStep", "0"),
                                     "surgePacePercent", "0"), "surgePacePercentStep", "0")).Ramp;

            foreach (int wave in new[] { 1, 30, 52, 90, 400, 9999 })
            {
                var s = ShuffleRamp.SurgeAt(wave, off);
                Assert.AreEqual((20 + (wave - 1) * ShuffleRamp.HealthStepTwentieths) / 2, s.HealthTenths, $"wave {wave}");
                Assert.AreEqual(10 + (wave - 1) / ShuffleRamp.BlowEveryWaves, s.BlowTenths, $"wave {wave}");
                Assert.AreEqual(100, s.PaceHundredths, $"wave {wave}");
            }

            // The pace alone switched off: health and blow surge as shipped, and nothing walks faster.
            var steady = Read(With(With(Doc(), "surgePacePercent", "0"), "surgePacePercentStep", "0")).Ramp;
            Assert.AreEqual(ShuffleRamp.SurgeAt(120).HealthTenths, ShuffleRamp.SurgeAt(120, steady).HealthTenths);
            Assert.AreEqual(100, ShuffleRamp.SurgeAt(9999, steady).PaceHundredths);

            // A lower top speed is the top speed.
            var gentle = Read(With(Doc(), "surgePaceMostHundredths", "200")).Ramp;
            Assert.AreEqual(200, ShuffleRamp.SurgeAt(9999, gentle).PaceHundredths);

            // The steepest a document can write still stops at every ceiling and never wraps.
            var steepest = Read(With(With(With(With(With(With(With(With(Doc(), "surgeFromWave", "1"), "surgeEveryWaves", "1"),
                                                                "surgeHealthPercent", "50"), "surgeHealthPercentStep", "20"),
                                                      "surgeBlowPercentStep", "20"), "surgePacePercent", "50"),
                                           "surgePacePercentStep", "20"), "surgePaceMostHundredths", ShuffleRamp.MostPaceHundredths.ToString())).Ramp;

            for (int wave = 2; wave <= 200; wave++)
            {
                Assert.GreaterOrEqual(ShuffleRamp.SurgeAt(wave, steepest).HealthTenths, ShuffleRamp.SurgeAt(wave - 1, steepest).HealthTenths, $"wave {wave}");
                Assert.GreaterOrEqual(ShuffleRamp.SurgeAt(wave, steepest).PaceHundredths, ShuffleRamp.SurgeAt(wave - 1, steepest).PaceHundredths, $"wave {wave}");
            }

            var deepest = ShuffleRamp.SurgeAt(int.MaxValue, steepest);
            Assert.AreEqual(ShuffleRamp.MostHealthTenths, deepest.HealthTenths);
            Assert.AreEqual(ShuffleRamp.MostBlowTenths, deepest.BlowTenths);
            Assert.AreEqual(ShuffleRamp.MostPaceHundredths, deepest.PaceHundredths);

            // And no document can walk a raider faster than the board's own ceiling.
            StringAssert.Contains("ramp.surgePaceMostHundredths", Refused(With(Doc(), "surgePaceMostHundredths", (ShuffleRamp.MostPaceHundredths + 1).ToString())));
            StringAssert.Contains("ramp.surgePaceMostHundredths", Refused(With(Doc(), "surgePaceMostHundredths", "99")));
        }

        /// <summary>
        /// <b>The health ceiling against the board's integer arithmetic</b>, walked rather than
        /// asserted (<see cref="ShuffleRamp.MostHealthTenths"/>). The heaviest raider this lane
        /// sends at the ceiling must survive every read of a whole body - the execute test
        /// (health x 100) and the hill's demand (four of the largest waves at full weight) - and
        /// every death derived from a body's full health: each blast, shatter and nuke card at the
        /// widest figures the strict reader accepts, folded by the build itself, landing on a
        /// hexed body (x <see cref="SiegeTuning.HexPercent"/>). A new card of one of those
        /// effects, or a wider range, that breaks this is a red here rather than a number that
        /// wraps on a phone.
        /// </summary>
        [Test]
        public void TheDeepestSurgeFitsTheBoardsArithmetic()
        {
            var deepest = new SiegeSurge(ShuffleRamp.MostHealthTenths, ShuffleRamp.MostBlowTenths, ShuffleRamp.MostPaceHundredths);
            long heaviest = 0;

            foreach (var kind in new[] { SiegeKind.Creeper, SiegeKind.Brute, SiegeKind.Bulwark })
            {
                heaviest = Math.Max(heaviest, (long)SiegeTuning.HealthOf(kind) * ShuffleRamp.MostHealthTenths / 10);
                Assert.Less((long)SiegeTuning.BlowOf(kind) * ShuffleRamp.MostBlowTenths, int.MaxValue, $"a {kind}'s blow");
            }

            Assert.AreEqual(heaviest, deepest.Health(SiegeTuning.BulwarkHealth), "the surge's own multiply");
            Assert.Less(heaviest * 100, int.MaxValue, "the execute test");
            Assert.Less(heaviest * 4 * SiegeLayout.MaxRaiders, int.MaxValue, "the hill's demand");

            int chains = 0;
            foreach (var shipped in ShuffleCards.All)
            {
                if (shipped.Effect != ShuffleEffect.Blast && shipped.Effect != ShuffleEffect.Shatter &&
                    shipped.Effect != ShuffleEffect.Nuke)
                    continue;

                // Each figure at its widest on its own: a superset of what one document can reach,
                // which is the safe direction for a bound.
                int magnitude = Widest(shipped, "magnitude", shipped.Magnitude * 4);
                int most = Widest(shipped, "most", shipped.Most * 2);
                var card = new ShuffleCard(shipped.Id, shipped.Tier, shipped.Effect, magnitude, shipped.Extent, most, shipped.Picture);

                var build = new ShuffleBuild(1u);
                for (int n = 0; n < most; n++) Assert.IsTrue(build.Take(card), $"{card.Id} refused copy {n + 1}");

                // In hundredths of the body's full health, whichever unit the effect keeps.
                var b = build.Boosts;
                long share = shipped.Effect == ShuffleEffect.Blast ? b.BlastTenths * 10L
                           : shipped.Effect == ShuffleEffect.Shatter ? b.ShatterTenths * 10L
                           : b.NukePercent;
                Assert.Greater(share, 0, $"{card.Id} folded to nothing");

                long derived = heaviest * share / 100;
                Assert.Less(heaviest * share, int.MaxValue, $"{card.Id}: full health x its share");
                Assert.Less(derived * SiegeTuning.HexPercent + 50, int.MaxValue, $"{card.Id}: its share landing on a hexed body");
                chains++;
            }

            Assert.Greater(chains, 0, "no card derives a hit from a body's health, so this walked nothing");
        }

        /// <summary>The widest value of one card field the strict reader accepts, searched down from <paramref name="from"/>.</summary>
        static int Widest(ShuffleCard card, string field, int from)
        {
            for (int value = from; value >= 1; value--)
                if (ShuffleTuning.TryRead(Card(Doc(), card.Id, field, value.ToString()), true, out _, out _))
                    return value;

            Assert.Fail($"no {field} for {card.Id} is accepted");
            return 0;
        }

        // ------------------------------------------------------------------ the text follows
        [Test]
        public void EveryNoteSlotIsAFigureAndEveryFigureIsASlot()
        {
            string root = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "loc");
            var slot = new Regex(@"\{(\d+)\}");

            foreach (string file in Directory.GetFiles(root, "*.json"))
            {
                var doc = TestJson.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                Assert.IsNotNull(doc, file);

                var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var row in (List<object>)doc["entries"])
                {
                    var entry = (Dictionary<string, object>)row;
                    texts[(string)entry["key"]] = (string)entry["text"];
                }

                foreach (var card in ShuffleCards.All)
                {
                    Assert.IsTrue(texts.TryGetValue(card.NoteKey, out string note), $"{Path.GetFileName(file)} has no {card.NoteKey}");

                    var found = new HashSet<int>();
                    foreach (Match m in slot.Matches(note)) found.Add(int.Parse(m.Groups[1].Value));

                    int slots = ShuffleNotes.Slots(card).Count;
                    for (int i = 0; i < slots; i++)
                        Assert.IsTrue(found.Contains(i), $"{Path.GetFileName(file)} {card.NoteKey} never prints {{{i}}}: '{note}'");
                    foreach (int i in found)
                        Assert.Less(i, slots, $"{Path.GetFileName(file)} {card.NoteKey} prints {{{i}}}, which nothing fills: '{note}'");

                    string printed = string.Format(note, ShuffleNotes.Args(card));
                    StringAssert.DoesNotContain("{", printed, $"{Path.GetFileName(file)} {card.NoteKey}");
                }
            }
        }

        [Test]
        public void TheShippedCardsPrintTheSentencesTheyAlwaysPrinted()
        {
            string Note(string id, string pattern) => string.Format(pattern, ShuffleNotes.Args(ShuffleCards.Find(id)));

            Assert.AreEqual("Shots deal 15% more damage.", Note("heavy_bolts", "Shots deal {0}% more damage."));
            Assert.AreEqual("Shots also hit nearby raiders for 40% damage.",
                            Note("splash_shot", "Shots also hit nearby raiders for {0}% damage."));
            Assert.AreEqual("Turrets shoot faster the longer they keep shooting, up to 150%.",
                            Note("spin_up", "Turrets shoot faster the longer they keep shooting, up to {0}%."));
            Assert.AreEqual("Turrets under 30% health deal 60% more damage and shoot 60% faster.",
                            Note("last_stand", "Turrets under {0}% health deal {1}% more damage and shoot {1}% faster."));
            Assert.AreEqual("Shots deal 40% more damage and turrets shoot 25% faster.",
                            Note("overdrive", "Shots deal {0}% more damage and turrets shoot {1}% faster."));
        }

        [Test]
        public void AStackedNotePrintsTheLevelTheBuildReaches()
        {
            // What the note prints on a hand dealt to a build already holding `held` copies.
            int Printed(string id, int held) => (int)ShuffleNotes.Args(ShuffleCards.Find(id), held)[0];

            // A level climbs with the copies: the total this copy reaches.
            Assert.AreEqual(15, Printed("execution", 0));
            Assert.AreEqual(25, Printed("execution", 1));
            Assert.AreEqual(new[] { 12, 24, 36 }, new[] { Printed("stunning_blows", 0), Printed("stunning_blows", 1), Printed("stunning_blows", 2) });
            Assert.AreEqual(new[] { 40, 60, 80 }, new[] { Printed("splash_shot", 0), Printed("splash_shot", 1), Printed("splash_shot", 2) });
            Assert.AreEqual(new[] { 30, 50, 70 }, new[] { Printed("frostbite", 0), Printed("frostbite", 1), Printed("frostbite", 2) });
            Assert.AreEqual(new[] { 35, 70 }, new[] { Printed("repelling_wards", 0), Printed("repelling_wards", 1) });
            Assert.AreEqual(new[] { 150, 300 }, new[] { Printed("spin_up", 0), Printed("spin_up", 1) });

            // A gain is what each copy adds, so it prints the same every time.
            Assert.AreEqual(15, Printed("heavy_bolts", 3));
            Assert.AreEqual(50, Printed("charge_master", 1));

            // The owner's example.
            Assert.AreEqual("Shots instantly kill raiders below 25% health.",
                            string.Format("Shots instantly kill raiders below {0}% health.",
                                          ShuffleNotes.Args(ShuffleCards.Find("execution"), 1)));
        }

        [Test]
        public void EveryNoteRowNamesALiveCard()
        {
            foreach (var card in ShuffleCards.All)
            {
                // A slot and a word may never name the same figure.
                var worded = ShuffleNotes.Worded(card);
                foreach (var figure in ShuffleNotes.Slots(card))
                {
                    if (figure == ShuffleFigure.Magnitude || figure == ShuffleFigure.MagnitudeTens || figure == ShuffleFigure.MagnitudeByExtent)
                        Assert.AreEqual(ShuffleWorded.None, worded & ShuffleWorded.Magnitude, card.Id);
                    if (figure == ShuffleFigure.Extent || figure == ShuffleFigure.MagnitudeByExtent)
                        Assert.AreEqual(ShuffleWorded.None, worded & ShuffleWorded.Extent, card.Id);
                }
            }

            // And the table names nothing the catalog does not deal.
            var notes = File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Scripts", "Domain",
                                                      "Modes", "Shuffle", "ShuffleNotes.cs"));
            foreach (Match m in new Regex("\\{ \"([a-z_]+)\", new Note").Matches(notes))
                Assert.IsNotNull(ShuffleCards.Find(m.Groups[1].Value), $"ShuffleNotes names '{m.Groups[1].Value}', which is not a card");
        }

        // ------------------------------------------------------------------ the device
        [Test]
        public void TheDeviceKeepsTheLastGoodTuningAndFallsBackToItsOwn()
        {
            var store = new EndlessCoins.MemoryStore();

            try
            {
                ShuffleConfig.UseStore(store);
                Assert.AreSame(ShuffleTuning.Shipped, ShuffleConfig.Current, "a fresh device does not play its own figures");

                string good = Card(Doc(3), "heavy_bolts", "magnitude", "30");
                Assert.IsTrue(ShuffleConfig.Adopt(good));
                Assert.AreEqual(3, ShuffleConfig.Current.Revision);
                Assert.AreEqual(good, store.Read(ShuffleConfig.CacheKey));
                Assert.IsFalse(ShuffleConfig.Adopt(good), "the same document read twice is a change");

                // A relaunch with no network plays what it last read.
                ShuffleConfig.UseStore(store);
                Assert.AreEqual(3, ShuffleConfig.Current.Revision);
                Assert.AreEqual(30, ShuffleConfig.Current.Find("heavy_bolts").Magnitude);

                // A bad document changes nothing, and is not cached.
                Assert.IsFalse(ShuffleConfig.Adopt(Card(Doc(4), "heavy_bolts", "magnitude", "999")));
                Assert.AreEqual(3, ShuffleConfig.Current.Revision);
                Assert.AreEqual(good, store.Read(ShuffleConfig.CacheKey));
                Assert.IsFalse(ShuffleConfig.Adopt("{ broken"));
                Assert.AreEqual(3, ShuffleConfig.Current.Revision);

                // Nothing published is the stop: back to the build's own, cache cleared.
                Assert.IsTrue(ShuffleConfig.Adopt(null));
                Assert.AreSame(ShuffleTuning.Shipped, ShuffleConfig.Current);
                Assert.AreEqual(string.Empty, store.Read(ShuffleConfig.CacheKey));

                // A cache this build cannot read is dropped, not played.
                store.Write(ShuffleConfig.CacheKey, "{ \"schema\": 9 }");
                ShuffleConfig.UseStore(store);
                Assert.AreSame(ShuffleTuning.Shipped, ShuffleConfig.Current);
                Assert.AreEqual(string.Empty, store.Read(ShuffleConfig.CacheKey));
            }
            finally
            {
                ShuffleConfig.UseStore(new EndlessCoins.MemoryStore());
            }
        }

        [Test]
        public void StrictJsonSaysWhatIsAbsentAndWhatIsMalformed()
        {
            Assert.IsTrue(StrictJson.TryParse("{\"a\": [1, -2, 3.5, \"x\\u0041\", true, null]}", out object value, out string fault), fault);
            var list = (List<object>)((Dictionary<string, object>)value)["a"];
            Assert.AreEqual(1L, list[0]);
            Assert.AreEqual(-2L, list[1]);
            Assert.AreEqual(3.5, list[2]);
            Assert.AreEqual("xA", list[3]);
            Assert.AreEqual(true, list[4]);
            Assert.IsNull(list[5]);

            foreach (string bad in new[] { "", "{", "{\"a\": 1,}", "{\"a\": 01}", "{\"a\": 1} 2", "{\"a\": 1, \"a\": 2}", "[1 2]", "\"\\q\"" })
                Assert.IsFalse(StrictJson.TryParse(bad, out _, out _), $"'{bad}' was read");

            Assert.IsFalse(StrictJson.TryParse(new string('[', StrictJson.MostDepth + 5), out _, out _));
        }

        // ------------------------------------------------------------------ helpers
        static List<string> Differences(ShuffleTuning a, ShuffleTuning b)
        {
            var moved = new List<string>();

            var ra = a.Ramp.Written(); var rb = b.Ramp.Written();
            for (int i = 0; i < ra.Length; i++)
                if (ra[i] != rb[i]) moved.Add($"ramp.{ShuffleRampTuning.Figures[i].Key}: {ra[i]} -> {rb[i]}");

            void Table(string name, IReadOnlyList<int> x, IReadOnlyList<int> y)
            {
                for (int i = 0; i < x.Count; i++)
                    if (x[i] != y[i]) { moved.Add($"deck.{name}: [{string.Join(", ", x)}] -> [{string.Join(", ", y)}]"); return; }
            }

            Table("opening", a.Deck.Opening, b.Deck.Opening);
            Table("heated", a.Deck.Heated, b.Deck.Heated);
            Table("wild", a.Deck.Wild, b.Deck.Wild);
            if (a.Deck.HeatedFrom != b.Deck.HeatedFrom) moved.Add($"deck.heatedFromHand: {a.Deck.HeatedFrom} -> {b.Deck.HeatedFrom}");
            if (a.Deck.WildFrom != b.Deck.WildFrom) moved.Add($"deck.wildFromHand: {a.Deck.WildFrom} -> {b.Deck.WildFrom}");
            if (a.Deck.RarePity != b.Deck.RarePity) moved.Add($"deck.rareEveryHands: {a.Deck.RarePity} -> {b.Deck.RarePity}");
            if (a.Deck.EpicPity != b.Deck.EpicPity) moved.Add($"deck.epicEveryHands: {a.Deck.EpicPity} -> {b.Deck.EpicPity}");

            var aa = a.Arsenal.Written(); var ab = b.Arsenal.Written();
            for (int i = 0; i < aa.Length; i++)
                if (aa[i] != ab[i]) moved.Add($"arsenal.{SiegeArsenalTuning.Figures[i].Key}: {aa[i]} -> {ab[i]}");

            foreach (var card in ShuffleCards.All)
            {
                var x = a.Find(card.Id);
                var y = b.Find(card.Id);

                if (x == null && y == null) continue;
                if (x == null || y == null) { moved.Add($"cards.{card.Id}: {(y == null ? "switched off" : "switched on")}"); continue; }

                if (x.Magnitude != y.Magnitude) moved.Add($"cards.{card.Id}.magnitude: {x.Magnitude} -> {y.Magnitude}");
                if (x.Extent != y.Extent) moved.Add($"cards.{card.Id}.extent: {x.Extent} -> {y.Extent}");
                if (x.Most != y.Most) moved.Add($"cards.{card.Id}.most: {x.Most} -> {y.Most}");
            }

            return moved;
        }
    }
}
