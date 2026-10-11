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

        Image _pill, _coin;
        Text _price, _off;

        public static WardDiscountKey Build(Transform parent, Vector2 size, Vector2 anchor,
                                            Vector2 pos, Action onTap)
        {
            var key = new WardDiscountKey();

            key.Button = UIKit.Button("Discount", parent, Art.S("Ui/" + Skins.Alternate), size,
                                      anchor, pos, () => onTap?.Invoke());
            key._pill = key.Button.GetComponent<Image>();

            float lift = size.y * UIKit.PillFaceLift;

            // Play mark, coin, figure, left to right: "watch, and pay this". The figure is
            // left-aligned against the coin so a four-digit price and a six-digit one start in the
            // same place.
            UIKit.Img("Play", key.Button.transform, Art.S("Ui/ic_play"), Pal.Cream,
                      new Vector2(52f, 52f), new Vector2(.5f, .5f), new Vector2(-150f, lift))
                 .preserveAspect = true;

            key._coin = UIKit.Img("Coin", key.Button.transform, Art.CoinFace(), Color.white,
                                  new Vector2(46f, 46f), new Vector2(.5f, .5f),
                                  new Vector2(-88f, lift));
            key._coin.preserveAspect = true;

            key._price = UIKit.Titled("Price", key.Button.transform, string.Empty, 38, Pal.Cream,
                                      TextAnchor.MiddleLeft, new Vector2(250f, 62f),
                                      new Vector2(.5f, .5f), new Vector2(65f, lift), 0f, 3f);
            UIKit.Shrinkable(key._price, 22);

            // The share off, on the kit's burst over the top-right corner - `DealCard`'s seal, at
            // this key's scale. Built last so it draws over the pill.
            var seal = UIKit.Img("Seal", key.Button.transform, Art.S("Ui/" + Skins.Badge), Pal.Rose,
                                 new Vector2(104f, 104f), new Vector2(1f, 1f), new Vector2(-14f, -6f));
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
            _off.text = Loc.Format("ui.loadout.discount_badge", WardDiscount.Percent);

            bool live = !Watching && affordable && WardDiscount.State == WardDiscountState.Ready;

            Button.Interactable = live;
            _pill.sprite = Art.S("Ui/" + (live ? Skins.Alternate : Skins.Shut));
        }

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
        /// Shows the video and answers whether it finished. Never throws - an SDK fault is a
        /// refusal like any other, logged on the way past (<c>RewardedVideo.Watch</c>'s rule).
        /// </summary>
        public async Task<AdShowResult> Watch()
        {
            var impression = AdImpression.New(AdPlacement.TurretDiscount);
            Watching = true;

            try
            {
                return await RewardedAds.Provider.ShowAsync(impression);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return AdShowResult.Failed(AdOutcome.Error, impression, e.Message);
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
