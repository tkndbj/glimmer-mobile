using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which five utilities ride on the bar (<see cref="UtilityKit"/>): the player's own choice,
    /// the catalog's first five when there is none, and the two refusals.
    ///
    /// <para>
    /// Pure over a hand-built catalog in the shipped shape: no save, no Unity.
    /// </para>
    /// </summary>
    public sealed class UtilityKitTests
    {
        static readonly string[] Shipped =
            { "firepot", "mending", "surge", "stormcall", "gravityhole", "revive", "wall" };

        static UtilityCatalog Catalog()
        {
            string[] kinds = { "blast", "mend", "surge", "storm", "gravity", "revive", "wall" };
            var items = new UtilityDto[Shipped.Length];

            for (int i = 0; i < items.Length; i++)
                items[i] = new UtilityDto
                {
                    id = Shipped[i], kind = kinds[i], magnitude = 10, gemPrice = 10,
                    maxHeld = 100, order = i + 1,
                };

            var problems = new List<string>();
            var catalog = UtilityCatalog.Resolve(new UtilitiesDto { items = items }, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.AreEqual(Shipped.Length, catalog.Count);
            return catalog;
        }

        static string[] Ids(IReadOnlyList<UtilityItem> kit)
        {
            var ids = new string[kit.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = kit[i].Id;
            return ids;
        }

        [Test]
        public void NeverChosenIsTheCatalogsFirstFiveWhichIsTheBarEveryoneAlreadyHad()
        {
            var catalog = Catalog();

            CollectionAssert.AreEqual(
                new[] { "firepot", "mending", "surge", "stormcall", "gravityhole" },
                Ids(UtilityKit.Resolve(catalog, new string[0])));

            CollectionAssert.AreEqual(Ids(UtilityKit.Resolve(catalog, new string[0])),
                                      Ids(UtilityKit.Resolve(catalog, null)));
        }

        [Test]
        public void AChoiceIsDrawnInCatalogOrderWhateverOrderItWasMadeIn()
        {
            var kit = UtilityKit.Resolve(Catalog(), new[] { "wall", "firepot", "revive" });
            CollectionAssert.AreEqual(new[] { "firepot", "revive", "wall" }, Ids(kit));
        }

        [Test]
        public void UnknownIdsAndDuplicatesAreSkippedAndTheBarIsNeverLongerThanFive()
        {
            var kit = UtilityKit.Resolve(Catalog(),
                new[] { "retired", "wall", "wall", "firepot", "mending", "surge", "stormcall", "revive" });

            Assert.AreEqual(UtilityKit.Size, kit.Count);
            CollectionAssert.AllItemsAreUnique(kit);

            // A choice that names only things the catalog withdrew falls back to the default
            // rather than leaving the bar empty for a reason the player did not choose.
            Assert.AreEqual(UtilityKit.Size, UtilityKit.Resolve(Catalog(), new[] { "retired" }).Count);
        }

        [Test]
        public void ASixthChoiceIsRefusedAndTakingOneOffMakesRoom()
        {
            var catalog = Catalog();
            var wall = catalog.Find("wall");
            var surge = catalog.Find("surge");

            // Never chosen: the bar is full with the default five.
            Assert.AreEqual(KitRefusal.Full, UtilityKit.WhyNotToggle(catalog, new string[0], wall));
            Assert.IsNull(UtilityKit.Toggled(catalog, new string[0], wall));

            var less = UtilityKit.Toggled(catalog, new string[0], surge);
            CollectionAssert.AreEqual(new[] { "firepot", "mending", "stormcall", "gravityhole" }, less);

            var more = UtilityKit.Toggled(catalog, less, wall);
            CollectionAssert.AreEqual(new[] { "firepot", "mending", "stormcall", "gravityhole", "wall" }, more);
        }

        [Test]
        public void TheLastItemCannotComeOff()
        {
            var catalog = Catalog();
            var wall = catalog.Find("wall");

            Assert.AreEqual(KitRefusal.Last, UtilityKit.WhyNotToggle(catalog, new[] { "wall" }, wall));
            Assert.IsNull(UtilityKit.Toggled(catalog, new[] { "wall" }, wall));
        }

        [Test]
        public void AnItemFromAnotherCatalogIsUnknown()
        {
            var stray = new UtilityItem("wall", UtilityKind.Wall, 50, 40, 100, 7);
            Assert.AreEqual(KitRefusal.Unknown, UtilityKit.WhyNotToggle(Catalog(), new string[0], stray));
            Assert.AreEqual(KitRefusal.Unknown, UtilityKit.WhyNotToggle(Catalog(), new string[0], null));
        }

        [Test]
        public void TheBarIsAsWideAsTheKit()
            => Assert.AreEqual(5, UtilityKit.Size, "the tray sprite is cut for five cells");
    }
}
