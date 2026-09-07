using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlimmerGrove.Content;
using GlimmerGrove.Events;
using GlimmerGrove.Persistence;
using GlimmerGrove.Store;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    public sealed class EventPassTests
    {
        [Test]
        public void PremiumAmountsCannotLeakIntoTheFreeLedger()
        {
            var id = LevelId.Parse("first");
            var records = new Dictionary<LevelId, LevelRecord>
                { [id] = new LevelRecord(id, 3, 10, 1, 150, 150) };
            var e = new GroveEvent("event", 100, 200, new[] { id },
                new[] { new EventMilestone(1, 60, 1200, 100) }, premiumProductId: "pass");
            Assert.AreEqual(60, EventLedger.ProgressOf(e, records, 1).Credits);
            Assert.AreEqual(60, e.TotalCredits);
            Assert.AreEqual(0, EventLedger.ProgressOf(e, records, 0).Credits);
        }

        [Test]
        public void APassIsAStandaloneNonconsumableEntitlement()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto { products = new[] {
                new StoreProductDto { id = "test_pass", eventPassId = "event", shelf = "event_pass",
                    kind = "nonconsumable", referenceUsdCents = 499 }
            } }, problems);
            Assert.IsEmpty(problems);
            Assert.IsTrue(catalog.Find("test_pass").IsValid);
            Assert.IsTrue(catalog.Find("test_pass").IsEventPass);
            Assert.IsTrue(catalog.Find("test_pass").IsOneTime);
        }

        [TestCase("consumable", 0)]
        [TestCase("nonconsumable", 100)]
        public void APassCannotAlsoBeACurrencyGrantOrAConsumable(string kind, int gems)
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto { products = new[] {
                new StoreProductDto { id = "test_pass", eventPassId = "event", shelf = "event_pass",
                    kind = kind, gems = gems, referenceUsdCents = 499 }
            } }, problems);
            Assert.IsNotEmpty(problems);
            Assert.IsNull(catalog.Find("test_pass"));
        }

        [Test]
        public void ShippedBloomContractPreservesLegacyPayoutsAndEconomyBudget()
        {
            var dto = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "Content/manifest.json")));
            var e = dto.events.Single(x => x.id == "first_bloom");
            Assert.AreEqual(40, e.milestones.Length);
            Assert.AreEqual(40, e.levels.Distinct().Count());
            Assert.AreEqual(6320, e.milestones.Sum(x => x.credits));
            Assert.AreEqual(12000, e.milestones.Sum(x => x.premiumCredits));
            Assert.AreEqual(600, e.milestones.Sum(x => x.premiumGems));
            Assert.AreEqual("first_bloom", StoreCatalog.Default.Find(e.premiumProductId).EventPassId);
            int[] goals = { 1, 2, 4, 6, 8, 10 }, amounts = { 60, 90, 250, 400, 600, 1000 };
            for (int i = 0; i < goals.Length; i++) Assert.AreEqual(amounts[i], e.milestones.Single(x => x.goal == goals[i]).credits);
        }
    }
}
