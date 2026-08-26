using System.Collections.Generic;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A product's picture, composed rather than drawn.
    ///
    /// <para>
    /// Thirteen products and five sprites, and the ratio is the point. A storefront that
    /// carried a picture per product would need an art order every time a price was retuned
    /// or a rung inserted, and thirteen near-identical piles of coins would be a texture
    /// budget spent on the difference between four coins and six. So a card is a
    /// <b>container</b> plus a <b>pile</b>, both chosen from where the product sits on its
    /// shelf — which is derived (<see cref="StoreProduct.Tier"/>), so the ladder and the
    /// picture cannot drift apart.
    /// </para>
    /// <para>
    /// <b>The coin and the gem are the game's own.</b> That is not a saving, it is the
    /// whole readability of the shop: the pile on a card is made of the same coin the hub's
    /// pill shows and the same gem the profile counts, so what a card sells is legible
    /// before a single word is read. A second, prettier coin drawn only in the shop would
    /// be a different currency as far as a player is concerned.
    /// </para>
    /// <para>
    /// This is <c>CompanionRevealOverlay</c>'s argument in a quieter place — a reveal that
    /// scales with the roster cannot wait on a sprite per companion — and it is why the
    /// only new art the feature needed was a pouch and four chests.
    /// </para>
    /// </summary>
    public static class ShopArt
    {
        /// <summary>
        /// The containers, smallest first. A product's tier picks one by fraction, so a
        /// shelf of four and a shelf of six both read as a full ladder rather than the
        /// short one stopping halfway up.
        /// </summary>
        static readonly string[] Containers =
        {
            null,                    // the smallest rung has no container: it is just the coins
            "Shop/pouch",
            "Shop/chest_wood",
            "Shop/chest_iron",
            "Shop/chest_silver",
            "Shop/chest_gold",
        };

        /// <summary>How many tokens are piled on each rung, by the same fraction.</summary>
        static readonly int[] Tokens = { 2, 3, 4, 5, 6, 8 };

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

            int rung = RungFor(product);

            // A one-time offer wears the best container on the shelf whatever it costs.
            // Its tier is honest — the starter pack is the cheapest thing in the shop — and
            // drawing it as the cheapest thing in the shop would be telling the truth about
            // the price and a lie about the offer.
            if (product.IsOneTime) rung = Containers.Length - 1;

            var container = Containers[rung];
            int tokens = Tokens[rung];

            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            // The container sits low and the pile rides over its lip, which is what makes a
            // chest read as *full* rather than as a chest with coins beside it.
            if (container != null)
            {
                var chest = UIKit.Img("Container", box, Art.S("Ui/" + container), Color.white,
                                      Vector2.one * (size * .78f), new Vector2(.5f, .5f),
                                      new Vector2(0f, -size * .12f));
                chest.preserveAspect = true;
            }

            // Bundles pile both currencies, in proportion to which one the product leans on,
            // so a gem-heavy bundle looks gem-heavy. Rounded so a bundle with a token of one
            // currency still shows at least one of it.
            bool hasGems = product.Gems > 0;
            bool hasCredits = product.Credits > 0;

            int gemTokens = hasGems && hasCredits ? Mathf.Max(1, tokens / 2)
                          : hasGems ? tokens : 0;
            int coinTokens = hasGems && hasCredits ? tokens - gemTokens
                           : hasCredits ? tokens : 0;

            Pile(box, size, container != null, gemTokens, coinTokens);
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
            // number under it does, and the number is there for the exact figure.
            int shown = good.Amount <= 5 ? 1 : good.Amount <= 20 ? 3 : 5;
            float heart = size * (shown == 1 ? .68f : shown == 3 ? .46f : .40f);

            for (int i = 0; i < shown; i++)
            {
                float t = shown == 1 ? 0f : (i / (float)(shown - 1)) - .5f;

                var img = UIKit.Img("H" + i, box, Art.S("Ui/ic_heart"), Color.white,
                                    Vector2.one * heart, new Vector2(.5f, .5f),
                                    new Vector2(t * size * .44f, Mathf.Abs(t) * size * -.16f));
                img.preserveAspect = true;
                img.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 22f);
            }

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
        /// The composition is the coin chest's, deliberately — a vessel plus a pile, with
        /// the pile riding the lip so the thing reads as <em>full</em> rather than as a
        /// bottle standing next to some hearts. What it sells is a bigger vessel, and that
        /// is the one idea the picture has to carry before a word is read.
        /// </para>
        /// <para>
        /// The hearts are the game's own <c>ic_heart</c>, for the reason the pile is the
        /// game's own coin: a prettier heart drawn only in the shop would be a different
        /// resource as far as a player is concerned. The vessels are three of the six potion
        /// bottles already in the global set, so this needed no art order — which is the
        /// whole argument of this class, applied to its fourth kind of card.
        /// </para>
        /// </summary>
        static void PaintContainer(RectTransform box, StoreProduct product)
        {
            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            int rung = product.ShelfSize <= 1
                ? Vessels.Length - 1
                : Mathf.Clamp(Mathf.RoundToInt(product.TierFraction * (Vessels.Length - 1)),
                              0, Vessels.Length - 1);

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
            int shown = 3 + rung;
            float heart = size * .26f;

            for (int i = 0; i < shown; i++)
            {
                float t = (i / (float)(shown - 1)) - .5f;
                float arc = 1f - 4f * t * t;

                var img = UIKit.Img("H" + i, box, Art.S("Ui/ic_heart"), Color.white,
                                    Vector2.one * heart, new Vector2(.5f, .5f),
                                    new Vector2(t * size * .40f,
                                                size * .17f + arc * size * .09f - (i % 2) * size * .04f));
                img.preserveAspect = true;
                img.transform.localRotation = Quaternion.Euler(0f, 0f, t * -20f);
            }

            UIKit.Halo(box, Pal.Rose, size * 1.02f, .32f);
        }

        // ------------------------------------------------------------------ the pile
        /// <summary>
        /// Scatters tokens over the container in a shallow arc.
        ///
        /// <para>
        /// The positions come from the index rather than from a random number, and that is
        /// deliberate rather than lazy: a grid cell is rebound as it scrolls, so a random
        /// scatter would re-scatter every time a card came back on screen — the same pack of
        /// gems shuffling itself while the player flicks past. Derived positions mean a card
        /// looks the same every time it is seen, which is the difference between a
        /// composition and a mess.
        /// </para>
        /// </summary>
        static void Pile(RectTransform box, float size, bool overContainer, int gems, int coins)
        {
            int total = gems + coins;
            if (total <= 0) return;

            float token = size * (total <= 3 ? .34f : total <= 5 ? .27f : .23f);

            // With no container the pile owns the middle of the cell; with one it rides the
            // lip, which is what makes a full chest read as full.
            float lift = overContainer ? size * .17f : 0f;
            float spread = overContainer ? size * .34f : size * .30f;

            for (int i = 0; i < total; i++)
            {
                float t = total == 1 ? 0f : (i / (float)(total - 1)) - .5f;
                float arc = 1f - 4f * t * t;                      // 0 at the ends, 1 in the middle

                var pos = new Vector2(t * spread * 2f, lift + arc * size * .10f - (i % 2) * size * .045f);

                bool isGem = i < gems;

                var img = UIKit.Img(isGem ? "G" + i : "C" + i, box,
                                    isGem ? Art.S("Ui/ic_gem") : null, Color.white,
                                    Vector2.one * token, new Vector2(.5f, .5f), pos);
                img.preserveAspect = true;

                // The coin is a flipbook, and every coin on a card runs it. They are
                // deliberately *not* phase-offset: the hub's pill spins one coin and a shelf
                // of them spinning in step reads as one object catching the light, where a
                // scatter of phases reads as noise. Cheap either way — six Images sharing
                // one already-global sprite set.
                if (!isGem) Flipbook.Attach(img, "Ui/Coin", 11f);

                img.transform.localRotation = Quaternion.Euler(0f, 0f, t * -16f);
            }
        }

        /// <summary>Which rung of the container ladder a product's tier lands on.</summary>
        static int RungFor(StoreProduct product)
        {
            if (product.ShelfSize <= 1) return Containers.Length - 1;

            int rung = Mathf.RoundToInt(product.TierFraction * (Containers.Length - 1));
            return Mathf.Clamp(rung, 0, Containers.Length - 1);
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
