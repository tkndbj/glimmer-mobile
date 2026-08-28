using GlimmerGrove.Layout;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A product's picture: one painted rung of the shelf it sells on.
    ///
    /// <para>
    /// <b>This used to compose a card out of a container and a heap of tokens</b>, and the
    /// argument for that was a good one — thirteen near-identical piles of coins is a texture
    /// budget spent on the difference between four coins and six, and a picture derived from
    /// the ladder cannot drift from it. What it could not do is look like money. Every rung of
    /// a shelf was the same two tokens in slightly different quantities, so a shelf read as one
    /// product listed six times, which is the single thing a storefront must not read as.
    /// </para>
    /// <para>
    /// <b>What replaced it keeps the half that was right.</b> Which of the six pictures a card
    /// draws is still a pure function of <see cref="StoreProduct.Tier"/> and its shelf's size
    /// (<see cref="ShopLadder"/>), so a rung inserted in the middle of a shelf still re-draws
    /// everything above it with no art order and no edit anywhere else, and a shelf of four and
    /// a shelf of six both still read as a full ladder. The pictures themselves are cut offline
    /// by <c>Tools/make_shop_art.py</c>, which <c>--check</c>s itself against what is shipped.
    /// </para>
    /// <para>
    /// <b>A bundle borrows the coin ladder rather than owning art of its own.</b> Three of the
    /// six coin pictures are painted with gems in among the coins, which is exactly what a
    /// bundle sells; cutting those a second time under a bundle name would put identical pixels
    /// at two addresses in one bundle, which is memory spent to avoid sharing a string.
    /// </para>
    /// <para>
    /// <b>Hearts are still composed, and that is not an inconsistency.</b> A heart pack sells a
    /// number of the thing the hub already counts, so the pile <em>is</em> the amount and a
    /// painted picture would put something between the player and the figure. The rule is that
    /// a picture is painted when the ladder is the message and composed when the count is —
    /// which is <c>CompanionRevealOverlay</c>'s argument about what may wait on an art order.
    /// </para>
    /// </summary>
    public static class ShopArt
    {
        /// <summary>
        /// The coin shelf, smallest first: a stack, a pile, a sack, then three chests.
        /// </summary>
        static readonly string[] Coins =
        {
            "Shop/coins_1", "Shop/coins_2", "Shop/coins_3",
            "Shop/coins_4", "Shop/coins_5", "Shop/coins_6",
        };

        /// <summary>The gem shelf: three loose piles, a sack, and two chests.</summary>
        static readonly string[] Gems =
        {
            "Shop/gems_1", "Shop/gems_2", "Shop/gems_3",
            "Shop/gems_4", "Shop/gems_5", "Shop/gems_6",
        };

        /// <summary>
        /// The bundle shelf, which is the three coin pictures painted with gems in among the
        /// coins. Deliberately the same three addresses rather than three more files — see
        /// the class summary.
        /// </summary>
        static readonly string[] Bundles = { "Shop/coins_2", "Shop/coins_5", "Shop/coins_6" };

        /// <summary>
        /// Which ladder a product's picture comes from, by what it grants rather than by what
        /// shelf it was authored on. A shelf is a browsing decision and could be re-cut; what
        /// arrives in the wallet cannot, so the picture keys on that.
        /// </summary>
        static string[] LadderFor(StoreProduct product)
            => product.Gems > 0 && product.Credits > 0 ? Bundles
             : product.Gems > 0 ? Gems
             : Coins;

        /// <summary>
        /// Draws one product into <paramref name="box"/>, replacing whatever was there.
        ///
        /// <para>
        /// Clears first, because a grid cell is rebound rather than rebuilt — see
        /// <c>GridView</c> — so the picture from the row this cell used to be is still
        /// hanging in it. And it clears <em>immediately</em> rather than by
        /// <c>Destroy</c>, which lands at the end of the frame: a cell rebound during a
        /// flick would otherwise draw two products on top of each other for a frame, which
        /// is the house rule five screens have each had to learn separately.
        /// </para>
        /// </summary>
        public static void Paint(RectTransform box, StoreProduct product)
        {
            if (box == null) return;

            Clear(box);
            if (product == null || !product.IsValid) return;

            // A heart container is not a pile of currency, so it is not composed like one.
            if (product.IsContainer) { PaintContainer(box, product); return; }

            var ladder = LadderFor(product);

            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            // Drawn to the box rather than inside it: every sprite is square with a hair of
            // air baked in, and the box is already measured against the figure underneath it.
            var picture = UIKit.Img("Rung", box,
                                    Art.S("Ui/" + ladder[ShopLadder.Rung(product, ladder.Length)]),
                                    Color.white, Vector2.one * size,
                                    new Vector2(.5f, .5f), Vector2.zero);
            picture.preserveAspect = true;
        }

        /// <summary>
        /// Draws one gem-priced good: a heart, or a heart wearing the boost's mark.
        ///
        /// Simpler than a product on purpose. Hearts do not come in chests and never will,
        /// because the pile <em>is</em> the amount here — a player buying five hearts is
        /// buying five of a thing they already count on the hub, and a container would put
        /// something between them and the number.
        /// </summary>
        public static void PaintGood(RectTransform box, StoreGood good)
        {
            if (box == null) return;

            Clear(box);
            if (good == null || !good.IsValid) return;

            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            if (good.Kind == StoreGoodKind.HeartBoost)
            {
                var boost = UIKit.Img("Boost", box, Art.S("Ui/ic_heart_boost"), Color.white,
                                      Vector2.one * (size * .74f), new Vector2(.5f, .5f), Vector2.zero);
                boost.preserveAspect = true;

                UIKit.Halo(box, Pal.Sun, size * 1.05f, .34f);
                Tween.Breathe(boost.transform, .035f, 2.6f);
                return;
            }

            // One, three or five hearts — the shape of the pile says "more" faster than the
            // number under it does, and the number is there for the exact figure. The sizes
            // step down as the count goes up so a heap of five is no wider than the picture it
            // is drawn in, and TokenPile.Width is what says whether it is.
            int shown = good.Amount <= 5 ? 1 : good.Amount <= 20 ? 3 : 5;
            float heart = size * (shown == 1 ? .68f : shown == 3 ? .44f : .36f);

            Heap(box, "H", "Ui/ic_heart", shown, heart, 0f);

            UIKit.Halo(box, Pal.Rose, size * .96f, .30f);
        }

        /// <summary>
        /// The vessels, smallest first, for a container's tier. Three rungs and three
        /// bottles already in the build.
        /// </summary>
        static readonly string[] Vessels = { "potion2", "potion4", "potion6" };

        /// <summary>
        /// Draws a heart container: a vessel with hearts spilling over its lip.
        ///
        /// <para>
        /// A vessel plus a pile, with the pile riding the lip so the thing reads as
        /// <em>full</em> rather than as a bottle standing next to some hearts. What it sells
        /// is a bigger vessel, and that is the one idea the picture has to carry before a
        /// word is read.
        /// </para>
        /// <para>
        /// The hearts are the game's own <c>ic_heart</c>, for the reason the pile is the
        /// game's own coin: a prettier heart drawn only in the shop would be a different
        /// resource as far as a player is concerned. The vessels are three of the six potion
        /// bottles already in the global set, so this needed no art order — which is what the
        /// class summary means by composing where the count is the message.
        /// </para>
        /// </summary>
        static void PaintContainer(RectTransform box, StoreProduct product)
        {
            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            int rung = ShopLadder.Rung(product, Vessels.Length);

            // Bigger vessels for bigger caps, and the step is small on purpose: the ladder is
            // carried by the hearts over the lip, and three bottles at wildly different sizes
            // would make the entry rung look like a mistake rather than a rung.
            float vessel = size * (.62f + rung * .07f);

            var bottle = UIKit.Img("Vessel", box, Art.S("Ui/" + Vessels[rung]), Color.white,
                                   new Vector2(vessel * .72f, vessel), new Vector2(.5f, .5f),
                                   new Vector2(0f, -size * .10f));
            bottle.preserveAspect = true;

            // Three, four or five hearts. PaintGood's ladder, so a container and a heart pack
            // on the same shelf read as the same currency in different quantities.
            Heap(box, "H", "Ui/ic_heart", 3 + rung, size * .26f, size * .20f);

            UIKit.Halo(box, Pal.Rose, size * 1.02f, .32f);
        }

        /// <summary>
        /// A heap of one repeated sprite — the hearts, in both of the places they are piled.
        ///
        /// <para>
        /// The arrangement is <see cref="TokenPile"/>'s, and it is the same arrangement the
        /// coins take, which is the whole reason it left this file: the three heaps here were
        /// three copies of one shallow arc with every second token dropped a little, and that
        /// alternation is only symmetric on an odd count — so a heap of four came out
        /// visibly heavier on one side and a heap of five did not, from the same three lines.
        /// </para>
        /// </summary>
        static void Heap(RectTransform box, string name, string sprite,
                         int count, float token, float lift)
        {
            foreach (var spot in TokenPile.Of(count, token))
            {
                var img = UIKit.Img(name + spot.Slot, box, Art.S(sprite), Color.white,
                                    Vector2.one * token, new Vector2(.5f, .5f),
                                    new Vector2(spot.X, lift + spot.Y));
                img.preserveAspect = true;
                img.transform.localRotation = Quaternion.Euler(0f, 0f, spot.Tilt);
            }
        }

        /// <summary>
        /// Empties a box now rather than at the end of the frame.
        ///
        /// <c>Destroy</c> is deferred, so a rebound cell would draw the outgoing product
        /// over the incoming one for a frame. <c>DestroyImmediate</c> is not available at
        /// runtime, so the children are hidden as they are marked — which is the same
        /// two-line rule the hub, the profile and the grove each arrived at separately.
        /// </summary>
        static void Clear(RectTransform box)
        {
            for (int i = box.childCount - 1; i >= 0; i--)
            {
                var child = box.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }
    }
}
