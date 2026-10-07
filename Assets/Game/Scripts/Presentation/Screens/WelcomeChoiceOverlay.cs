using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A finished welcome quest's two prizes, side by side: the turret on every seat, or its
    /// shelf price in coins (the owner, 2026-10-07: some players already hold the turret).
    ///
    /// <para>
    /// <b>It wears the Deals sheet's frame</b> (<see cref="ChallengeTierOverlay"/>, on
    /// <see cref="VictoryFrame"/>), at the owner's instruction: the green window, the fan behind
    /// it and the crown over a banner carrying one word, so the two sheets that offer a player
    /// something to take cannot drift. Two rows in the deals' own shape - a picture, a name over
    /// a sentence, a key - and NOT NOW at the foot.
    /// </para>
    /// <para>
    /// <b>The choice is made here and recorded by the ledger</b>
    /// (<see cref="WelcomeLedger.TryClaim(WelcomeQuest, WelcomeReward)"/>), which writes the
    /// claim, the choice and the prize in one save. This sheet tells its opener which was taken
    /// (<see cref="Taken"/>) and gets out of the way: the turret's payoff is the loadout's reveal
    /// and the coins' payoff is the page's flight to the wallet, and neither belongs on a sheet
    /// still wearing the furniture of a decision (<c>WardPreviewOverlay</c>'s note).
    /// </para>
    /// <para>
    /// <b>A quest that stops being ready under the sheet is answered by the sheet closing</b> -
    /// a sync landing another device's claim - rather than by a second prize.
    /// </para>
    /// </summary>
    public sealed class WelcomeChoiceOverlay : ModalView
    {
        /// <summary>The quest being taken. A property, for <c>WardRevealOverlay.Model</c>'s reason.</summary>
        public WelcomeQuest Quest { get; set; }

        /// <summary>Called after the ledger took the quest, with what was taken. The opener plays the payoff.</summary>
        public Action<WelcomeReward> Taken { get; set; }

        // The Deals sheet's stack, field for field (`ChallengeTierOverlay`).
        const float LineY = 150f, RowsTop = 200f, RowH = 230f, RowGap = 14f, Tail = 30f, FootH = 170f;
        const float PanelW = 1000f, RowW = 880f, StoneSize = 160f, StoneX = 104f, TextX = 204f, TextW = 370f;
        const float LineTop = 22f, LineH = 124f;
        static readonly Vector2 KeySize = new Vector2(280f, 116f);
        const float KeyInset = 16f;

        /// <summary>Its own hold on the turret's picture, never the page's (7b).</summary>
        AssetHold _art;
        Image _turret;
        bool _taking;

        protected override void Build()
        {
            if (Quest == null || Quest.Ward == null) { Close(); return; }

            const int rowCount = 2;
            float rows = rowCount * (RowH + RowGap) - RowGap;
            float panelH = RowsTop + rows + Tail + FootH;

            Scrim = UIKit.Scrim(Content, .72f, () => Close());

            var frame = VictoryFrame.Build(Content, panelH, Loc.Get("ui.welcome.choose_word"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            UIKit.Shrinkable(
                UIKit.Titled("Line", Panel, Loc.Get("ui.welcome.choose_line"), 32,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter, new Vector2(860f, 60f),
                             new Vector2(.5f, 1f), new Vector2(0f, -LineY), 2f, 2f, wrap: true),
                20);

            float y = -(RowsTop + RowH * .5f);
            BuildTurretRow(y);
            y -= RowH + RowGap;
            BuildPriceRow(y);

            UIKit.TextButton("Close", Panel, Skins.Alternate, Loc.Get("ui.common.cancel").Upper(), 36,
                             new Vector2(400f, 110f), new Vector2(.5f, 0f), new Vector2(0f, 30f + 55f),
                             () => Close());

            Run(async token =>
            {
                _art = _art ?? AssetLibrary.Hold("welcome_choice");
                await _art.LoadAsync(new System.Collections.Generic.List<AssetRequest>
                {
                    AssetRequest.Sprite(AssetManifest.WardArt(Quest.Ward, Quest.Colour)),
                }, null, token);
                if (!Living || !_turret) return;
                var sprite = AssetLibrary.Sprite(AssetManifest.WardArt(Quest.Ward, Quest.Colour));
                _turret.sprite = sprite;
                _turret.enabled = sprite != null;
            });

            if (Rebuilding)
            {
                frame.Settle();
                return;
            }

            // The Deals sheet's entrance, in its order.
            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => Tween.Scale(Panel, 1f, .5f, Ease.OutBack));
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
        }

        void OnDestroy()
        {
            _art?.Dispose();
            _art = null;
        }

        /// <summary>The deals' row plate: a dark inset with a faint edge, and the picture's halo.</summary>
        Transform Row(string name, float y, Color edge, Color halo, out Image picture, Vector2 pictureSize)
        {
            var plate = UIKit.Img(name, Panel, Art.Round(28), new Color(0f, 0f, 0f, .32f),
                                  new Vector2(RowW, RowH), new Vector2(.5f, 1f), new Vector2(0f, y));
            var rim = UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f), edge);
            UIKit.StretchTo((RectTransform)rim.transform, 0f, 0f, 0f, 0f);
            rim.raycastTarget = false;

            picture = UIKit.Img("Picture", plate.transform, null, Color.white, pictureSize,
                                new Vector2(0f, .5f), new Vector2(StoneX, 0f));
            picture.preserveAspect = true;
            picture.raycastTarget = false;
            picture.enabled = false;
            UIKit.Halo(picture.transform, halo, StoneSize * 1.9f, .22f);

            return plate.transform;
        }

        void Caption(Transform t, string name, string line, Color nameTint)
        {
            UIKit.Shrinkable(
                UIKit.Titled("Name", t, name, 52, nameTint, TextAnchor.MiddleLeft,
                             new Vector2(TextW, 66f), new Vector2(0f, .5f), new Vector2(TextX + TextW * .5f, 60f), 3f, 3f),
                28);

            UIKit.Shrinkable(
                UIKit.Titled("Line", t, line, 36, new Color(1f, .96f, .88f, .88f), TextAnchor.UpperLeft,
                             new Vector2(TextW, LineH), new Vector2(0f, .5f),
                             new Vector2(TextX + TextW * .5f, LineTop - LineH * .5f), 2f, 0f, wrap: true),
                20);
        }

        /// <summary>The turret, on every seat, for ever. Green key: it costs nothing.</summary>
        void BuildTurretRow(float y)
        {
            var tint = SiegeView.TintOf(Quest.Colour);
            var t = Row("Row_turret", y, Pal.A(tint, .55f), tint, out _turret,
                        new Vector2(StoneSize * .8f, StoneSize));

            Caption(t, Loc.Get(Quest.Ward.NameKey), Loc.Get("ui.welcome.choose_turret"), Pal.Cream);

            var key = UIKit.TextButton("Take", t, Skins.Affirm, Loc.Get("ui.welcome.take").Upper(), 36, KeySize,
                                       new Vector2(1f, .5f), new Vector2(-(KeyInset + KeySize.x * .5f), 0f),
                                       () => Take(WelcomeReward.Turret));
            UIKit.OneLine(key, 20);
        }

        /// <summary>The turret's shelf price in its currency. The coin is the pills' own reel.</summary>
        void BuildPriceRow(float y)
        {
            bool gems = Quest.PriceCurrency == Currency.Gems;
            var tint = gems ? Pal.Bloom : Pal.Gold;
            var t = Row("Row_price", y, Pal.A(tint, .55f), tint, out var picture, Vector2.one * (StoneSize * .78f));

            if (gems)
            {
                picture.sprite = Art.S("Ui/ic_gem");
                picture.enabled = picture.sprite != null;
            }
            else
            {
                picture.enabled = true;
                Flipbook.Attach(picture, "Ui/Coin", 11f);
            }

            string amount = Compact.Number(Quest.PriceAmount);
            Caption(t, Loc.Format(gems ? "ui.welcome.gems" : "ui.welcome.coins", amount),
                    Loc.Get("ui.welcome.choose_price"), tint);

            // Orange - the BUY key's colour - because this row is about money, and the caption
            // carries the figure with the currency's glyph trailing it, which is how every price
            // in this game says which currency it is.
            var key = UIKit.TextButton("Take", t, Skins.Buy, Loc.Get("ui.welcome.take").Upper(), 36, KeySize,
                                       new Vector2(1f, .5f), new Vector2(-(KeyInset + KeySize.x * .5f), 0f),
                                       () => Take(WelcomeReward.Price));
            UIKit.OneLine(key, 20);
        }

        void Take(WelcomeReward reward)
        {
            if (_taking) return;
            _taking = true;

            if (!WelcomeLedger.TryClaim(Quest, reward))
            {
                // Taken on another device meanwhile, or the block withdrawn: nothing to give, so
                // the sheet closes and the page behind repaints off the ledger's Changed.
                Audio.Sfx("blocked", .45f);
                Close();
                return;
            }

            Audio.Sfx(reward == WelcomeReward.Price ? "coin" : "collect", .6f);

            var taken = Taken;
            Close(() => taken?.Invoke(reward), quiet: true);
        }

        public override bool OnBack()
        {
            Close();
            return true;
        }
    }
}
