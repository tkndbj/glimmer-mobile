using System.Collections.Generic;
using GlimmerGrove;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That every sprite the storefront asks for is one the game actually loads.
    ///
    /// <para>
    /// <b>It exists because routing the shop's furniture through <see cref="ShopSkins"/>
    /// took thirteen names out of <c>Tools/verify/artnames.py</c>'s sight.</b> That gate reads
    /// <em>literals</em> at a call site — `Art.S("Ui/ic_gem")` — which is exactly what a
    /// constant is not, so a typo in <c>ShopSkins</c> resolves to nothing and draws a
    /// <b>white rectangle</b> on the one screen in the game that takes money (invariant 7b).
    /// The count it prints went from 48 built names to 65 in one change, which is the number
    /// that said this was owed.
    /// </para>
    /// <para>
    /// It is the same shape as the check <c>artnames.py</c> makes and not a second copy of
    /// it: that one holds a literal to what is on <em>disk</em>, this one holds a constant to
    /// what the <em>manifest preloads</em> — and the manifest's own entries are literals, so
    /// they are already held to disk. The two together are what make the chain complete.
    /// </para>
    /// </summary>
    public sealed class ShopSkinsTests
    {
        static IEnumerable<string> Everything()
        {
            foreach (StoreShelf shelf in System.Enum.GetValues(typeof(StoreShelf)))
            {
                yield return ShopSkins.Frame(shelf);
                yield return ShopSkins.Buy(shelf);
            }

            yield return ShopSkins.PlainFrame;
            yield return ShopSkins.Gem;
            yield return ShopSkins.Badge;
            yield return ShopSkins.Ribbon;
            yield return ShopSkins.Pill;
            yield return ShopSkins.Add;
            yield return ShopSkins.TabOn;
            yield return ShopSkins.TabOff;
        }

        [Test]
        public void EverySkinTheShopAsksForIsOneTheGameLoads()
        {
            var loaded = new HashSet<string>();
            foreach (var request in AssetManifest.GlobalAssets()) loaded.Add(request.Address);

            foreach (var skin in Everything())
                // `Art.S` is what these strings are handed to, and it prefixes
                // `AssetManifest.ArtRoot` — so the address is built the way the screen builds
                // it rather than the way this test guesses it.
                Assert.That(loaded, Contains.Item(AssetManifest.ArtRoot + skin),
                            $"ShopSkins asks for '{skin}', which AssetManifest does not load - "
                            + "an address that resolves to nothing draws a white rectangle");
        }

        /// <summary>
        /// And that a shelf's price bar is never the colour of the card it stands on.
        ///
        /// The one thing about this that is a *design* rule rather than a wiring one, and the
        /// only fault a render caught here: a yellow bar on the yellow coin frame is the one
        /// control on a card that cannot be allowed to disappear. Stated as a property rather
        /// than as "coins are blue", so a fourth frame colour cannot quietly reintroduce it.
        /// </summary>
        [Test]
        public void NoShelfsPriceBarIsTheColourOfItsOwnFrame()
        {
            foreach (StoreShelf shelf in System.Enum.GetValues(typeof(StoreShelf)))
            {
                string frame = ShopSkins.Frame(shelf);
                string buy = ShopSkins.Buy(shelf);

                Assert.AreNotEqual(Hue(frame), Hue(buy),
                                   $"{shelf}: a {Hue(buy)} price bar on a {Hue(frame)} frame");
            }
        }

        /// <summary>The colour word at the end of a kit address — "frame_yellow" is yellow.</summary>
        static string Hue(string address) => address.Substring(address.LastIndexOf('_') + 1);
    }
}
