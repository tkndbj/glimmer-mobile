using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Offers;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The offer engine's tuning (invariant 64): the built-in text reads, the seed file the publisher
    /// writes is the game's own reader's to accept, revision 1 is the build's own, and every way a
    /// published document can be wrong is refused by name rather than half-read.
    /// </summary>
    public sealed class OfferTuningTests
    {
        static string SeedPath() => Path.Combine(TestJson.RepoRoot(), "firebase", "seed", "offers.json");
        static string ProgressionPath()
            => Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json");

        static OfferTuning Read(string json, bool strict = true)
        {
            Assert.IsTrue(OfferTuning.TryRead(json, strict, out var tuning, out string fault), fault);
            return tuning;
        }

        static string Refused(string json, bool strict = true)
        {
            Assert.IsFalse(OfferTuning.TryRead(json, strict, out var tuning, out string fault),
                           "the document was accepted");
            Assert.IsNull(tuning);
            Assert.IsNotEmpty(fault);
            return fault;
        }

        /// <summary>The built-in text with one replacement, for the refusals below.</summary>
        static string Shipped(string from, string to)
        {
            string json = OfferTuning.ShippedJson;
            Assert.AreEqual(1, Count(json, from), "the fixture's anchor is not unique: " + from);
            return json.Replace(from, to);
        }

        static int Count(string text, string part)
        {
            int n = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>The ids of the products on the offers shelf of the shipped content, read without the engine.</summary>
        static HashSet<string> ShippedOfferProducts()
        {
            Assert.IsTrue(StrictJson.TryParse(File.ReadAllText(ProgressionPath()), out object parsed, out string fault), fault);
            var store = (Dictionary<string, object>)((Dictionary<string, object>)parsed)["store"];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in (List<object>)store["products"])
            {
                var product = (Dictionary<string, object>)row;
                if ((string)product["shelf"] == "offers") ids.Add((string)product["id"]);
            }
            return ids;
        }

        // ------------------------------------------------------------------ the build's own
        [Test]
        public void TheBuiltInTuningReadsStrictly()
        {
            var tuning = OfferTuning.Shipped;
            Assert.AreEqual(1, tuning.Revision);
            Assert.IsTrue(tuning.Enabled);
            Assert.Greater(tuning.Offers.Count, 0);
            Assert.AreEqual(tuning.Describe(), Read(OfferTuning.ShippedJson).Describe());
        }

        [Test]
        public void TheSeedFileReadsAndNamesOnlyOfferProducts()
        {
            string path = SeedPath();
            Assert.IsTrue(File.Exists(path), "missing " + path);

            var published = Read(File.ReadAllText(path), strict: true);
            var shelf = ShippedOfferProducts();
            Assert.Greater(shelf.Count, 0, "progression.json has no offers shelf");

            foreach (var offer in published.Offers)
                foreach (string product in offer.Products)
                    Assert.IsTrue(shelf.Contains(product),
                                  $"offer '{offer.Id}' sells '{product}', which is not on the offers shelf of " +
                                  "progression.json - it would never be dealt, and the server would not know it");

            Console.WriteLine($"firebase/seed/offers.json is revision {published.Revision}, " +
                              $"{(published.Enabled ? "enabled" : "DISABLED")}, {published.Offers.Count} offer(s), " +
                              $"{published.Segments.Count} segment(s), holdout {published.HoldoutPercent}%");
        }

        [Test]
        public void TheSeedFileAtRevisionOneIsTheBuiltInTuning()
        {
            var published = Read(File.ReadAllText(SeedPath()));
            if (published.Revision != 1) Assert.Pass($"the seed file is revision {published.Revision}; only revision 1 is pinned");
            Assert.AreEqual(OfferTuning.Shipped.Describe(), published.Describe(),
                            "revision 1 of offers.json is not OfferTuning.ShippedJson - change both together");
        }

        [Test]
        public void EveryBuiltInOfferSellsAProductOnTheBuiltInOffersShelf()
        {
            foreach (var offer in OfferTuning.Shipped.Offers)
                foreach (string id in offer.Products)
                {
                    var product = StoreCatalog.Default.Find(id);
                    Assert.IsNotNull(product, $"offer '{offer.Id}' sells '{id}', which the built-in catalog lacks");
                    Assert.AreEqual(StoreShelf.Offers, product.Shelf, $"'{id}' is not on the offers shelf");
                    Assert.IsFalse(product.IsOneTime, $"'{id}' is a one-time product; an offer is dealt again");
                }
        }

        [Test]
        public void EveryLookHasWords()
        {
            var en = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "loc", "en.json");
            string text = File.ReadAllText(en);
            foreach (var look in OfferLooks.All)
            {
                StringAssert.Contains("\"" + OfferLooks.TitleKey(look) + "\"", text);
                StringAssert.Contains("\"" + OfferLooks.LineKey(look) + "\"", text);
            }
        }

        [Test]
        public void EverySpellingRoundTrips()
        {
            foreach (var m in OfferTriggers.All) Assert.AreEqual(m, OfferTriggers.Parse(OfferTriggers.Id(m)));
            foreach (var m in OfferMeasures.All) Assert.AreEqual(m, OfferMeasures.Parse(OfferMeasures.Id(m)));
            foreach (var l in OfferLooks.All) Assert.AreEqual(l, OfferLooks.Parse(OfferLooks.Id(l)));
            Assert.AreEqual(Enum.GetValues(typeof(OfferMeasure)).Length - 1, OfferMeasures.All.Length, "a measure has no spelling");
            Assert.AreEqual(Enum.GetValues(typeof(OfferTrigger)).Length - 1, OfferTriggers.All.Length, "a moment has no spelling");
        }

        // ------------------------------------------------------------------ refusals
        [Test]
        public void AnUnknownKeyIsRefusedStrictlyAndIgnoredOnADevice()
        {
            string json = Shipped("\"perDay\": 2,", "\"perDay\": 2, \"perWeek\": 9,");
            StringAssert.Contains("perWeek", Refused(json, strict: true));
            Assert.AreEqual(2, Read(json, strict: false).PerDay);
        }

        [Test]
        public void ACommentIsNeverAFigure()
            => Read(Shipped("\"schema\": 1,", "\"schema\": 1, \"_comment\": \"hello\","));

        [Test]
        public void TheWrongSchemaIsRefused()
            => StringAssert.Contains("schema", Refused(Shipped("\"schema\": 1,", "\"schema\": 2,")));

        [Test]
        public void AnUnknownMeasureIsRefusedByName()
            => StringAssert.Contains("wealth", Refused(Shipped("\"measure\": \"loss_streak\"", "\"measure\": \"wealth\"")));

        [Test]
        public void AnUnknownMomentIsRefusedByName()
            => StringAssert.Contains("payday", Refused(Shipped("\"moments\": [\"near_miss\"]", "\"moments\": [\"payday\"]")));

        [Test]
        public void AnUnknownLookIsRefusedByName()
            => StringAssert.Contains("gold_rush", Refused(Shipped("\"look\": \"starter\"", "\"look\": \"gold_rush\"")));

        [Test]
        public void AGapOfferMustAnswerOnlyAShortfall()
            => StringAssert.Contains("shortfall", Refused(Shipped(
                "\"pick\": \"gap\",\n      \"moments\": [\"shortfall\"], \"segments\": [\"never_paid\"]",
                "\"pick\": \"gap\",\n      \"moments\": [\"shortfall\", \"win\"], \"segments\": [\"never_paid\"]")));

        [Test]
        public void AnOfferNamingNoSuchSegmentIsRefused()
            => StringAssert.Contains("ghosts", Refused(Shipped("\"segments\": [\"new_player\"]", "\"segments\": [\"ghosts\"]")));

        [Test]
        public void ARuleNobodyCanBeInIsRefused()
            => StringAssert.Contains("min", Refused(Shipped("{ \"measure\": \"loss_streak\", \"min\": 3 }",
                                                            "{ \"measure\": \"loss_streak\", \"min\": 3, \"max\": 1 }")));

        [Test]
        public void ARuleAskingNothingIsRefused()
            => StringAssert.Contains("neither", Refused(Shipped("{ \"measure\": \"loss_streak\", \"min\": 3 }",
                                                                "{ \"measure\": \"loss_streak\" }")));

        [Test]
        public void ABackoffThatShortensIsRefused()
            => StringAssert.Contains("backoff", Refused(Shipped("\"backoffHours\": [6, 24, 72]", "\"backoffHours\": [6, 2, 72]")));

        [Test]
        public void MoreASessionThanADayIsRefused()
            => StringAssert.Contains("perSession", Refused(Shipped("\"perSession\": 1,", "\"perSession\": 3,")));

        [Test]
        public void AFigureOutOfRangeIsRefused()
            => StringAssert.Contains("holdoutPercent", Refused(Shipped("\"holdoutPercent\": 10,", "\"holdoutPercent\": 90,")));

        [Test]
        public void ATwiceListedOfferIsRefused()
            => StringAssert.Contains("twice", Refused(Shipped("{ \"id\": \"stuck_help\"", "{ \"id\": \"near_miss\"")));

        [Test]
        public void AnythingThatIsNotJsonIsRefusedWithoutThrowing()
        {
            Refused("");
            Refused("not json");
            Refused("[]");
            Refused(OfferTuning.ShippedJson.Substring(0, 200));
        }

        [Test]
        public void TheStopIsAPublishedSwitch()
        {
            var off = Read(Shipped("\"enabled\": true,", "\"enabled\": false,"));
            Assert.IsFalse(off.Enabled);
            var deal = OfferRules.Choose(off, new OfferProfile().Set(OfferMeasure.ClearedLevels, 50), OfferTrigger.Hub,
                                         new OfferHistory(null), OfferRulesTests.Everything, "acct", 1_800_000_000L);
            Assert.AreEqual(OfferRefusal.Off, deal.Refusal);
        }
    }
}
