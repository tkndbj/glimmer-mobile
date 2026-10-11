using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A limited-time deal's card (invariant 60), in two shapes built in one place (44d, said of
    /// code): the shop's band wears the boxed card (<see cref="Build"/>), and every row of the deals
    /// panel the open one (<see cref="BuildOpen"/>). Both write the same amount and value seal
    /// through <see cref="Paint"/>.
    ///
    /// <para>
    /// <b>The look</b> (the owner, 2026-10-09): one flat emerald box - <c>Hud/plate_flat</c>, the
    /// kit's plate with its two-tone face flattened to white and tinted here, so the box is exactly
    /// one colour with the kit's own keyline round it; the coin coffer at the left, breathing gently;
    /// the amount beside it in full; and a red seal over the top-left corner saying how much more
    /// the deal is worth than the shop's ordinary prices (<see cref="DealValue"/>). The seal is
    /// still, not turning. What sits on the right is the caller's: the shop puts the clock there,
    /// the panel the clock over the price key.
    /// </para>
    /// <para>
    /// <b>The panel's shape</b> (the owner, 2026-10-11: the green box "looks too thin" on the
    /// panel, which is already a green window): no box at all. The coffer is drawn big on a warm
    /// glow at the left with the seal over its corner, and a column on the right carries the
    /// amount, COINS, and the caller's clock and price key. The shop keeps its box.
    /// </para>
    /// </summary>
    public static class DealCard
    {
        /// <summary>The box's colour: an emerald, deeper and cooler than the kit's lime green.</summary>
        public static readonly Color Face = new Color(.12f, .72f, .42f);

        /// <summary>The seal's colour: the kit's alarm red, which no other card on the shop wears.</summary>
        static readonly Color SealInk = Pal.Poppy;

        /// <summary>How big the seal is drawn, and how far inside the corner its middle sits.</summary>
        public const float SealSize = 140f, SealIn = 44f, SealDrop = 8f;

        /// <summary>How far the seal reaches above the card's top edge - the room a caller leaves for it.</summary>
        public const float SealRise = SealSize * .5f - SealDrop;

        /// <summary>The coffer's margin top and bottom, and from the left - wider, so the seal over the corner leaves it in view.</summary>
        const float ArtPad = 14f, ArtLeft = 40f, AmountGap = 16f;
        const int AmountSize = 76, UnitSize = 32;

        /// <summary>
        /// The open shape (<see cref="BuildOpen"/>): the coffer's size and left margin, the glow under
        /// it, the seal's middle from the top-left corner, and the amount and COINS above the column's
        /// middle. Mirrored by <c>Tools/render_deals.py</c>.
        /// </summary>
        const float OpenArt = 360f, OpenArtLeft = 20f, OpenGlow = 520f;
        const float OpenSealX = 74f, OpenSealY = 70f, OpenAmountY = 108f, OpenUnitY = 44f;
        const int OpenAmountSize = 100, OpenUnitSize = 34;
        static readonly Color OpenGlowInk = new Color(1f, .82f, .38f, .50f);

        public sealed class Parts
        {
            public RectTransform Root;
            public Btn Button;
            public Text Amount;
            public RectTransform Seal;
            public Text SealWord;

            /// <summary>Where the amount column starts, from the card's left - so a caller lays its right side clear of it.</summary>
            public float AmountLeft;

            /// <summary>The open shape's right column: its middle, from the card's left - where a caller centres its clock and key.</summary>
            public float ColumnX;
        }

        /// <summary>
        /// Builds a card <paramref name="size"/> big. With <paramref name="tap"/> the whole card is
        /// the button (the shop's band); without, it is a plate (a panel row, which has its own key).
        /// Call <see cref="Paint"/> to write a deal into it.
        /// </summary>
        public static Parts Build(string name, Transform parent, Vector2 size, Vector2 anchor, Vector2 pos,
                                  float amountWidth, Action tap)
        {
            var parts = new Parts();
            var plate = Art.S("Ui/" + Skins.PlateFlat);

            if (tap != null)
            {
                parts.Button = UIKit.Button(name, parent, plate, size, anchor, pos, tap);
                parts.Button.PressScale = .985f;
                parts.Root = (RectTransform)parts.Button.transform;
                parts.Button.GetComponent<Image>().color = Face;
            }
            else
            {
                var img = UIKit.Img(name, parent, plate, Face, size, anchor, pos);
                parts.Root = (RectTransform)img.transform;
            }

            // The coffer, inside the box at its left, breathing - gently, so it reads as alive rather
            // than as something asking to be tapped.
            float art = size.y - 2f * ArtPad;
            var coffer = UIKit.Img("Art", parts.Root, Art.S("Ui/Shop/coins_3"), Color.white,
                                   new Vector2(art, art), new Vector2(0f, .5f), new Vector2(ArtLeft + art * .5f, 0f));
            coffer.preserveAspect = true;
            coffer.enabled = coffer.sprite != null;
            Tween.Breathe(coffer.transform, .035f, 2.4f);

            // The amount, in full, on the box's vertical middle with COINS tucked under it.
            parts.AmountLeft = ArtLeft + art + AmountGap;
            parts.Amount = UIKit.Shrinkable(
                UIKit.Titled("Amount", parts.Root, string.Empty, AmountSize, Pal.Sun, TextAnchor.MiddleLeft,
                             new Vector2(amountWidth, 90f), new Vector2(0f, .5f),
                             new Vector2(parts.AmountLeft + amountWidth * .5f, 14f + DealClock.DigitLift(AmountSize)),
                             4f, 3f), 40);
            UIKit.Shrinkable(
                UIKit.Titled("Unit", parts.Root, Loc.Get("ui.endless.coins").Upper(), UnitSize, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(amountWidth, 42f), new Vector2(0f, .5f),
                             new Vector2(parts.AmountLeft + amountWidth * .5f, -46f), 3f, 2f), 20);

            // The seal, over the top-left corner, built last so it draws over the coffer; still.
            BuildSeal(parts, new Vector2(SealIn, -SealDrop));

            return parts;
        }

        /// <summary>
        /// Builds the deals panel's open card <paramref name="size"/> big: no box, the coffer big on a
        /// warm glow at the left, the seal over its corner, and the amount over COINS in a column
        /// <paramref name="columnW"/> wide against the right edge. The caller lays its clock and key
        /// under them on <see cref="Parts.ColumnX"/>. Call <see cref="Paint"/> to write a deal into it.
        /// </summary>
        public static Parts BuildOpen(string name, Transform parent, Vector2 size, Vector2 anchor, Vector2 pos,
                                      float columnW)
        {
            var parts = new Parts { Root = UIKit.Box(name, parent, size, anchor, pos) };

            // The coffer on its own light, breathing - the picture is the card now.
            float artX = OpenArtLeft + OpenArt * .5f;
            UIKit.Img("Glow", parts.Root, Art.Glow(128, 2.4f), OpenGlowInk,
                      new Vector2(OpenGlow, OpenGlow), new Vector2(0f, .5f), new Vector2(artX, 0f));
            var coffer = UIKit.Img("Art", parts.Root, Art.S("Ui/Shop/coins_3"), Color.white,
                                   new Vector2(OpenArt, OpenArt), new Vector2(0f, .5f), new Vector2(artX, 0f));
            coffer.preserveAspect = true;
            coffer.enabled = coffer.sprite != null;
            Tween.Breathe(coffer.transform, .035f, 2.4f);

            // The amount, big and centred on the column, with COINS under it.
            parts.ColumnX = size.x - columnW * .5f;
            parts.AmountLeft = parts.ColumnX - columnW * .5f;
            parts.Amount = UIKit.Shrinkable(
                UIKit.Titled("Amount", parts.Root, string.Empty, OpenAmountSize, Pal.Sun, TextAnchor.MiddleCenter,
                             new Vector2(columnW, 110f), new Vector2(0f, .5f),
                             new Vector2(parts.ColumnX, OpenAmountY + DealClock.DigitLift(OpenAmountSize)),
                             5f, 3f), 48);
            UIKit.Shrinkable(
                UIKit.Titled("Unit", parts.Root, Loc.Get("ui.endless.coins").Upper(), OpenUnitSize, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(columnW, 44f), new Vector2(0f, .5f),
                             new Vector2(parts.ColumnX, OpenUnitY), 3f, 2f), 22);

            // The seal over the coffer's top-left, built last so it draws over it.
            BuildSeal(parts, new Vector2(OpenSealX, -OpenSealY));

            return parts;
        }

        /// <summary>The red value seal, its middle at <paramref name="pos"/> from the card's top-left; hidden until painted.</summary>
        static void BuildSeal(Parts parts, Vector2 pos)
        {
            var seal = UIKit.Img("Seal", parts.Root, Art.S("Ui/" + Skins.Badge), SealInk,
                                 new Vector2(SealSize, SealSize), new Vector2(0f, 1f), pos);
            seal.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            parts.Seal = (RectTransform)seal.transform;
            parts.SealWord = UIKit.Shrinkable(
                UIKit.Titled("Word", seal.transform, string.Empty, 30, Color.white, TextAnchor.MiddleCenter,
                             new Vector2(SealSize * .70f, SealSize * .62f), new Vector2(.5f, .5f), new Vector2(0f, 2f),
                             3f, 2f, wrap: true), 16);
            parts.SealWord.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);   // upright, whatever the seal's tilt
            parts.Seal.gameObject.SetActive(false);
        }

        /// <summary>
        /// Writes <paramref name="deal"/> into a card: the amount, and the seal's figure or no seal.
        /// Every field on every call (44mc) - a panel row is rebound as the list recycles.
        /// </summary>
        public static void Paint(Parts parts, ShopDeal deal)
        {
            if (parts == null) return;

            parts.Amount.text = deal != null ? deal.Credits.ToString("N0") : string.Empty;

            int percent = DealValue.Percent(deal, StoreRules.Catalog);
            bool show = percent > 0;
            if (parts.Seal.gameObject.activeSelf != show) parts.Seal.gameObject.SetActive(show);
            parts.SealWord.text = show ? Loc.Format("ui.deal.value", percent).Upper() : string.Empty;
        }
    }
}
