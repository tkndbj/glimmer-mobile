using System;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
using GlimmerGrove.Localization;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The turret discount's key: a play mark, the discounted price with its coin, and a burst
    /// in the corner saying how much comes off (<see cref="WardDiscount"/>).
    ///
    /// <para>
    /// <b>One drawing for the two panels that sell something on the shelf</b> - the turret
    /// (<c>WardPreviewOverlay</c>) and its next star (<c>WardUpgradeOverlay</c>) - so the offer
    /// reads as the same offer wherever a price is. Blue (<see cref="Skins.Alternate"/>), because
    /// the green key beside it is the price paid outright and the two must read as two choices
    /// rather than two copies of one.
    /// </para>
    /// <para>
    /// <b>It owns the video and not the purchase.</b> The panel buys after <see cref="Watch"/>
    /// returns, with the model and seat it captured before the video, and buys whether or not the
    /// panel survived the video: a player who watched has paid, and a screen torn down underneath
    /// a video is no reason to keep the discount from them.
    /// </para>
    /// </summary>
    sealed class WardDiscountKey
    {
        public Btn Button { get; private set; }

        /// <summary>True from the tap until the SDK answers. The key is dead for all of it.</summary>
        public bool Watching { get; private set; }

        Image _pill, _play, _coin;
        Text _price, _off;
        float _lift;

        /// <summary>The burst's size, and how far its centre stands in from the key's right edge.</summary>
        public const float SealSize = 104f, SealInset = -6f;

        /// <summary>
        /// The play mark, the coin and the figure's box, and the gaps between them. The three are
        /// one group centred on the key (<see cref="Lay"/>), so these are the only numbers that
        /// place them.
        /// </summary>
        public const float PlaySize = 52f, CoinSize = 46f, PriceW = 250f, PriceH = 62f;
        public const float PlayGap = 13f, CoinGap = 8f;

        public static WardDiscountKey Build(Transform parent, Vector2 size, Vector2 anchor,
                                            Vector2 pos, Action onTap)
        {
            var key = new WardDiscountKey();

            key.Button = UIKit.Button("Discount", parent, Art.S("Ui/" + Skins.Alternate), size,
                                      anchor, pos, () => onTap?.Invoke());
            key._pill = key.Button.GetComponent<Image>();

            float lift = size.y * UIKit.PillFaceLift;
            key._lift = lift;

            // Play mark, coin, figure, left to right: "watch, and pay this". Placed by `Lay` on
            // every paint, because where the group starts depends on how wide the figure is.
            key._play = UIKit.Img("Play", key.Button.transform, Art.S("Ui/ic_play"), Pal.Cream,
                                  new Vector2(PlaySize, PlaySize), new Vector2(.5f, .5f),
                                  new Vector2(0f, lift));
            key._play.preserveAspect = true;

            key._coin = UIKit.Img("Coin", key.Button.transform, Art.CoinFace(), Color.white,
                                  new Vector2(CoinSize, CoinSize), new Vector2(.5f, .5f),
                                  new Vector2(0f, lift));
            key._coin.preserveAspect = true;

            key._price = UIKit.Titled("Price", key.Button.transform, string.Empty, 38, Pal.Cream,
                                      TextAnchor.MiddleLeft, new Vector2(PriceW, PriceH),
                                      new Vector2(.5f, .5f), new Vector2(0f, lift), 0f, 3f);
            UIKit.Shrinkable(key._price, 22);

            // The share off, on the kit's burst - `DealCard`'s seal, at this key's scale. Built last
            // so it draws over the pill. **Straddling the right edge at mid-height, never over the
            // top corner**: the key above is the full price, sixteen units away, and a sticker
            // reaching over its corner read as that price being the discounted one
            // (`render_ad_offers.py`). Shorter than the key, so it stays inside the key's own band.
            var seal = UIKit.Img("Seal", key.Button.transform, Art.S("Ui/" + Skins.Badge), Pal.Rose,
                                 new Vector2(SealSize, SealSize), new Vector2(1f, .5f),
                                 new Vector2(SealInset, lift));
            seal.transform.localRotation = Quaternion.Euler(0f, 0f, -9f);

            key._off = UIKit.Shrinkable(
                UIKit.Titled("Off", seal.transform, string.Empty, 28, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(74f, 44f), new Vector2(.5f, .5f),
                             new Vector2(0f, 2f), 3f, 2f), 16);
            key._off.transform.localRotation = Quaternion.Euler(0f, 0f, 9f);

            return key;
        }

        public void Show(bool on)
        {
            if (Button != null) Button.gameObject.SetActive(on);
        }

        public void Place(Vector2 pos)
        {
            if (Button != null) ((RectTransform)Button.transform).anchoredPosition = pos;
        }

        /// <summary>
        /// Writes every field (44mc): the figure, the burst, and whether the key takes a tap.
        ///
        /// <b>Live only with a video loaded, the discounted price affordable and no video already
        /// up</b>; dead otherwise, and grey, because on these panels grey is the one drawing of a
        /// key that refuses the finger (owner, 2026-09-21).
        /// </summary>
        public void Paint(long price, bool affordable)
        {
            if (Button == null) return;

            _price.text = price.ToString("N0");
            Lay();

            _off.text = Loc.Format("ui.loadout.discount_badge", WardDiscount.Percent);

            bool live = !Watching && affordable && WardDiscount.State == WardDiscountState.Ready;

            Button.Interactable = live;
            _pill.sprite = Art.S("Ui/" + (live ? Skins.Alternate : Skins.Shut));
        }

        /// <summary>
        /// Centres the play mark, the coin and the figure as one group on the key.
        ///
        /// <b>Measured off the figure, never placed at fixed offsets.</b> The three used to stand
        /// at fixed places with the figure left-aligned against the coin, which centres the group
        /// for exactly one width of price: every shorter figure left the whole row sitting left
        /// of the middle of the key (owner, 2026-10-15). The figure's box keeps its width and
        /// left alignment; only where the group starts moves.
        /// </summary>
        void Lay()
        {
            float text = Mathf.Min(_price.preferredWidth, PriceW);
            float wide = PlaySize + PlayGap + CoinSize + CoinGap + text;
            float left = -wide * .5f;

            Place(_play.rectTransform, left + PlaySize * .5f);
            Place(_coin.rectTransform, left + PlaySize + PlayGap + CoinSize * .5f);

            // The box is centre-pivoted (`UIKit.Box`), so its middle is half its width past where
            // the left-aligned figure starts.
            Place(_price.rectTransform, left + PlaySize + PlayGap + CoinSize + CoinGap + PriceW * .5f);
        }

        void Place(RectTransform rt, float x) => rt.anchoredPosition = new Vector2(x, _lift);

        /// <summary>
        /// The sentence the panel's status line carries while this key is the one to read, or
        /// null when the panel's own line should stand.
        /// </summary>
        public string Status(bool affordable)
        {
            if (Watching) return Loc.Get("ui.ads.opening");
            if (!affordable) return null;

            return WardDiscount.State == WardDiscountState.Ready
                ? Loc.Format("ui.loadout.discount_note", WardDiscount.Percent)
                : Loc.Get("ui.ads.loading");
        }

        /// <summary>
        /// Shows the video and answers whether it finished, through
        /// <see cref="RewardedVideo.Show"/> - so it never throws and owns nothing about the SDK
        /// beyond holding the key dead while the video is up.
        /// </summary>
        public async Task<AdShowResult> Watch()
        {
            Watching = true;

            try
            {
                return await RewardedVideo.Show(AdPlacement.TurretDiscount);
            }
            finally
            {
                Watching = false;
            }
        }

        /// <summary>Why a video paid nothing, in the player's terms.</summary>
        public static string Refusal(AdShowResult result) => AdOfferButton.Refusal(result.Outcome);
    }
}
