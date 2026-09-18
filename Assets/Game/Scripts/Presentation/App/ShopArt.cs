using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Store;
using GlimmerGrove.Utilities;
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
    /// <b>What replaced it keeps the half that was right.</b> Which picture a card draws is
    /// still a pure function of <see cref="StoreProduct.Tier"/> and its shelf's size (<see
    /// cref="ShopLadder"/>), so a rung inserted in the middle of a shelf still re-draws
    /// everything above it with no art order and no edit anywhere else, and a shelf of four and
    /// a shelf of six both still read as a full ladder. The pictures themselves are cut offline
    /// by <c>Tools/make_shop_art.py</c>, which <c>--check</c>s itself against what is shipped.
    /// </para>
    /// <para>
    /// <b>A ladder is as long as its shelf, and that is a rule rather than a coincidence.</b>
    /// Longer, and <see cref="ShopLadder"/> skips rungs, so painted quantities are shipped that
    /// no card ever draws; shorter, and two adjacent cards draw the same picture. Neither shows
    /// up anywhere, because both ship a shelf that is individually correct on every card.
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
        /// The coin shelf, smallest first: a spilled pile, a basket, a chest, then a vault.
        ///
        /// <para>
        /// <b>Four rungs because the shelf sells four products.</b> It was six for as long as
        /// the pictures came off a sheet that happened to carry six coin tiles, and with four
        /// products <see cref="ShopLadder"/> then picked rungs 0, 2, 3 and 5 — so two of the
        /// four painted quantities were never drawn in the shop at all. A ladder longer than
        /// its shelf hides art and a ladder shorter than its shelf repeats it, and neither is
        /// visible anywhere, because both ship a shelf that is correct on every card.
        /// </para>
        /// </summary>
        static readonly string[] Coins =
        {
            "Shop/coins_1", "Shop/coins_2", "Shop/coins_3", "Shop/coins_4",
        };

        /// <summary>
        /// The gem shelf: one cut stone, then a heap, a sack, a basket, a chest and a vault.
        ///
        /// It opens on a single stone rather than on the smallest heap because the pack
        /// paints five gem quantities against a shelf of six, and a shelf of six over a
        /// ladder of five draws two adjacent cards identically.
        /// </summary>
        static readonly string[] Gems =
        {
            "Shop/gems_1", "Shop/gems_2", "Shop/gems_3",
            "Shop/gems_4", "Shop/gems_5", "Shop/gems_6",
        };

        /// <summary>
        /// The bundle shelf: a gold chest, a gem chest, then the one wearing both.
        ///
        /// <para>
        /// <b>Its own pictures rather than three borrowed from the coin ladder.</b> Borrowing
        /// was right while three of the coin pictures were painted with gems in among the
        /// coins, which is exactly what a bundle sells; the pack that replaced them paints no
        /// mixed pile, so a borrowed picture would say only half of what a bundle grants. The
        /// shelf is therefore told apart by what a chest is made of rather than by how full
        /// it is — there is no quantity to draw when two currencies arrive at once.
        /// </para>
        /// <para>
        /// <b>Three rungs against three products, of which the first is drawn by nothing today</b>,
        /// and that is the one place a ladder is deliberately longer than what it draws: the
        /// starter bundle is one-time, so <see cref="ShopLadder"/> puts it on the top rung whatever
        /// it costs, leaving the bottom one for a cheaper bundle nobody has authored yet.
        /// Everywhere else a ladder is exactly as long as its shelf — invariant 18e.
        /// </para>
        /// </summary>
        static readonly string[] Bundles =
            { "Shop/bundles_1", "Shop/bundles_2", "Shop/bundles_3" };

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
            if (product.IsEventPass)
            {
                // The top bundle chest, shared rather than cut again: a pass is the same
                // kind of purchase, and identical pixels at a second address is memory
                // spent to avoid sharing a string.
                UIKit.Img("SeasonPass", box, Art.S("Ui/Shop/bundles_3"), Color.white,
                          Vector2.one * Mathf.Max(200f, box.rect.width), new Vector2(.5f, .5f), Vector2.zero).preserveAspect = true;
                return;
            }

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

            if (good.Kind == StoreGoodKind.XpBoost)
            {
                // The wordmark rather than the keeper ladder's star. A boost is a rate on
                // something the player already has, so there is no object to draw — and the star
                // this used to borrow is the *level* mark, which reads as "a star" on a shelf
                // where nothing else is one. The letters say what is being multiplied.
                var mark = UIKit.Img("XpBoost", box, Art.S("Ui/ic_xp_boost"), Color.white,
                                     Vector2.one * (size * .74f), new Vector2(.5f, .5f), Vector2.zero);
                mark.preserveAspect = true;

                Tween.Breathe(mark.transform, .035f, 2.6f);
                return;
            }

            if (good.Kind == StoreGoodKind.HeartBoost)
            {
                var boost = UIKit.Img("Boost", box, Art.S("Ui/ic_heart_boost"), Color.white,
                                      Vector2.one * (size * .74f), new Vector2(.5f, .5f), Vector2.zero);
                boost.preserveAspect = true;

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

        }

        /// <summary>
        /// Draws what a rewarded video pays: a small heap of the resource, with the play mark
        /// standing on it.
        ///
        /// <para>
        /// <b>Composed rather than painted, and it has to be.</b> Every other card on these two
        /// shelves draws a rung of a painted ladder (<see cref="ShopLadder"/>), and a video
        /// stands on no ladder — it is one fixed amount that content retunes, so borrowing the
        /// cheapest rung would draw this card and the pack below it identically, which is the
        /// fault invariant 18e is about arriving from the other end. The heap is the same
        /// arrangement <see cref="PaintGood"/> piles hearts in, so the two read as the same
        /// currency in different quantities.
        /// </para>
        /// <para>
        /// <b>The play mark is the picture's whole job.</b> A heap of coins says coins and says
        /// nothing at all about how they are come by, and there is no bought "watch a video"
        /// art in this project — nine icon packs on this machine hold none, which was surveyed
        /// rather than assumed. So the answer is the one invariant 32b prescribes: compose it
        /// out of what the game already draws. <c>ic_play</c> is the mark on the watch button
        /// every rewarded offer in the game is taken through, so a player meets it here and
        /// again on the panel this card opens.
        /// </para>
        /// <para>
        /// It rides the heap rather than sitting beside it, on a disc of its own, because a
        /// glyph floating on a plate is a glyph nobody put anywhere — the same judgement the
        /// streak tile's reward well makes (invariant 48g).
        /// </para>
        /// </summary>
        public static void PaintAd(RectTransform box, ChestDropKind kind)
        {
            if (box == null) return;

            Clear(box);

            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            // **A drawn picture where there is one, and the composed heap where there is not.**
            // Three of the four placements now ship a single illustration that already contains
            // its own play button, so everything below — the heap, the seat, the ring, the mark —
            // would be drawn *over* a picture that has all of it. A placement with no picture
            // (the hint refill) still gets the composition, which is why this is a fallback and
            // not a replacement: taking the old path out would leave that card blank.
            string drawn = AdPicture(kind);
            if (drawn != null)
            {
                var art = UIKit.Img("AdArt", box, Art.S(drawn), Color.white,
                                    Vector2.one * (size * 1.22f), new Vector2(.5f, .5f),
                                    Vector2.zero);
                art.preserveAspect = true;

                // The same breath the composed mark had, moved onto the whole picture — the
                // play button is part of the drawing now, so breathing it alone is not possible
                // and breathing nothing would make the card the one still thing on the shelf.
                Tween.Breathe(art.transform, .04f, 2.4f);
                return;
            }

            RewardArt.Token(kind, null, out var token, out var tint);
            if (token == null) return;

            // **Two, always, and two is the whole of why it is two.** The figure under the
            // picture is the amount — 300 coins is not a pile anybody can draw — so the heap
            // only has to say "some of these". What it must *also* do is not be one of the
            // packs: `PaintGood`'s ladder draws one, three or five, so a heap of three beside a
            // fifteen-heart pack is two cards on one shelf with the same picture, which is
            // exactly the fault invariant 18e names. Two is the one count no ladder here uses,
            // and a single row of two leaves the mark below it clear ground to stand on.
            Heap(box, "A", token, tint, 2, size * .44f, size * .06f);

            // Centred and low, which is a fact about a row of two rather than a taste: two
            // tokens leaning away from each other (`TokenPile.Tilt`) leave a notch under the
            // middle, so the mark stands *in* the heap and covers neither of them. It is why
            // the count above is not three — an odd row puts a token on the centre line, and a
            // mark there is a mark drawn over the picture it is meant to be part of.
            var seat = UIKit.Img("PlaySeat", box, Art.Disc(96), new Color(.05f, .09f, .06f, .88f),
                                 Vector2.one * (size * .34f), new Vector2(.5f, .5f),
                                 new Vector2(0f, -size * .24f));

            var ring = UIKit.Img("PlayRing", seat.transform, Art.Disc(96), Pal.A(Pal.Mint, .85f));
            UIKit.StretchTo((RectTransform)ring.transform, -3, -3, -3, -3);
            ring.transform.SetAsFirstSibling();

            var play = UIKit.Img("Play", seat.transform, Art.S("Ui/ic_play"), Pal.Cream,
                                 Vector2.one * (size * .17f), new Vector2(.5f, .5f),
                                 new Vector2(size * .012f, 0f));
            play.preserveAspect = true;

            Tween.Breathe(seat.transform, .04f, 2.4f);
        }

        /// <summary>
        /// The picture a rewarded video draws, or null when this reward has none.
        ///
        /// <para>
        /// <b>Keyed on what the placement <em>pays</em> rather than on the placement</b>, which is
        /// the same decision <see cref="RewardArt.Address"/> makes and for the same reason: two
        /// placements pay credits (the coin shelf and the victory panel) and both should show the
        /// coins. Keying on the placement would mean two names for one picture and a third to
        /// remember the day a fifth placement pays something that already has one.
        /// </para>
        /// <para>
        /// Null is a real answer and the caller depends on it — see <see cref="PaintAd"/>. Every
        /// name here is resident in <c>AssetManifest</c>, so none of them can arrive late.
        /// </para>
        /// </summary>
        static string AdPicture(ChestDropKind kind)
        {
            switch (kind)
            {
                case ChestDropKind.Credits: return "Ui/ad_coin";
                case ChestDropKind.Hearts: return "Ui/ad_heart";
                case ChestDropKind.XpBoost: return "Ui/ad_xp";
                default: return null;
            }
        }

        /// <summary>
        /// Draws one utility: the same icon the action bar draws, and nothing else.
        ///
        /// <para>
        /// <b>Deliberately not composed and deliberately not a pile.</b> A heart pack is drawn as
        /// a heap because the count is the offer; a utility is one object with one job, and what
        /// a player has to recognise on this shelf is the picture they will be tapping on the bar
        /// half a minute later. Drawing it twice two ways would be teaching a second name for the
        /// same thing — the tab-glyph rule, one card down.
        /// </para>
        /// <para>
        /// <b>Nothing behind it.</b> Every picture on this screen used to stand on a coloured
        /// halo, and the owner had them taken off after playing the restyled shop: against an
        /// opaque kit frame a wash behind an object reads as a smudge on the card rather than
        /// as light under the object. The kind's colour survives on the name above the price
        /// (<see cref="ShopRarity"/>), which is the one place it was saying something the
        /// picture was not.
        /// </para>
        /// </summary>
        public static void PaintUtility(RectTransform box, UtilityItem item)
        {
            if (box == null) return;

            Clear(box);
            if (item == null) return;

            float size = box.rect.width;
            if (size <= 1f) size = 200f;

            var icon = UIKit.Img("Utility", box, Art.S(item.Art), Color.white,
                                 Vector2.one * (size * .78f), new Vector2(.5f, .5f), Vector2.zero);
            icon.preserveAspect = true;

            Tween.Breathe(icon.transform, .03f, 2.8f);
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

            // Ranked, rather than the `ShopLadder.Rung(product, ...)` overload every other
            // shelf uses. That one puts a **one-time** product on the top rung whatever it
            // costs, which is right for the starter bundle — a single offer with nothing to
            // be compared against — and wrong here, because all three vessels are
            // non-consumables and the three of them *are* a ladder. Under it every cap drew
            // the largest bottle with five hearts, so the shelf that sells 10, 20 and 50
            // hearts showed one picture three times: invariant 18e's fault reached through
            // the arithmetic rather than through the length of a list.
            int rung = ShopLadder.Rung(product.Tier, product.ShelfSize, Vessels.Length);

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
            => Heap(box, name, Art.S(sprite), Color.white, count, token, lift);

        /// <summary>
        /// The same heap from a sprite already in hand.
        ///
        /// <para>
        /// The overload above is the one every shelf uses, because a shelf knows the address it
        /// wants. <see cref="PaintAd"/> does not: what a placement pays is content, so the
        /// picture comes out of <see cref="RewardArt.Token"/> — which is the one place that
        /// knows credits have no sprite at all and are a frame of a flipbook, and the one place
        /// that answers a tinted disc rather than <b>null</b> when the art has not arrived
        /// (invariant 7b).
        /// </para>
        /// </summary>
        static void Heap(RectTransform box, string name, Sprite sprite, Color tint,
                         int count, float token, float lift)
        {
            foreach (var spot in TokenPile.Of(count, token))
            {
                var img = UIKit.Img(name + spot.Slot, box, sprite, tint,
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
