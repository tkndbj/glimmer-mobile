using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a turret is, and what it looks like firing — the panel behind every tap on the loadout
    /// shelf, held or not.
    ///
    /// <para>
    /// <b>One panel for both, which is the change.</b> Tapping a held turret used to stand it on
    /// the line immediately and tapping an unheld one opened a price; so the only turrets a player
    /// could ever *see* were the ones they had already bought, and the decision the shop is asking
    /// them to make — is this worth nine thousand credits — was being made from a thumbnail. A
    /// preview costs the held case one extra tap and is worth it: standing a turret is still one
    /// tap from here, and what it buys is that the nineteen effects are visible before they are
    /// paid for rather than after.
    /// </para>
    /// <para>
    /// <b>The stage is the shipped one</b> (<see cref="WardFiringStage"/>), drawn at the sizes,
    /// anchors and per-turret scale the board uses. A preview that flattered a turret would be
    /// worse than none.
    /// </para>
    /// <para>
    /// <b>The button says the game's own words rather than a new set.</b> This panel is one tap in
    /// front of a shelf whose cells already read "Stand here" and "On the line", so a second
    /// vocabulary for the same two actions would be the panel disagreeing with what raised it. An
    /// unheld turret shows its <em>price</em> rather than the word for buying, which is
    /// <c>WardBuyOverlay</c>'s rule kept: the number is the thing the player is deciding about.
    /// </para>
    /// </summary>
    public sealed class WardPreviewOverlay : ModalView
    {
        /// <summary>
        /// The turret being shown.
        ///
        /// A property rather than a field, because <see cref="WardModel"/> is not
        /// <c>[Serializable]</c> and a public field earns a warning about serialisation that will
        /// never happen.
        /// </summary>
        public WardModel Model { get; set; }

        /// <summary>
        /// Which of the line's four colours the shelf was filling when this was raised.
        ///
        /// <b>Handed in rather than looked up</b>, because the panel has no business knowing which
        /// screen opened it — and it is why the turret here wears the colour of the cell the player
        /// just tapped, and fires in it.
        /// </summary>
        public int Colour { get; set; }

        /// <summary>Raised after anything lands, so the shelf behind can repaint.</summary>
        public Action Changed { get; set; }

        const float PanelW = 880f, PanelH = 1240f;

        /// <summary>
        /// The stage's own box, and the cell its contents are multiples of.
        ///
        /// <b>The cell is a real board's</b>, so a bolt, a flash and an impact are drawn here at
        /// the size a phone draws them on the hill — which is the whole point of showing them.
        /// </summary>
        const float StageW = 800f, StageH = 640f, StageCell = 104f;

        /// <summary>Its own scope, never the line's — see <see cref="WardFiringStage"/>.</summary>
        const string PreviewScope = "ward_preview";

        /// <summary>
        /// The four bands, stacked down the panel and stated as <em>middles</em>.
        ///
        /// <para>
        /// <b>Middles, because <c>UIKit.Box</c> pivots at centre whatever it is anchored to.</b>
        /// Written as top edges the first time, the stage's six hundred and forty units were
        /// centred where its top was meant to be and it drew straight through the description and
        /// the status line above it — measured on the built panel, which is the only thing that
        /// could have said so. It is the same arithmetic <c>render_home.py</c> had to learn about
        /// its own mirror (invariant 44d).
        /// </para>
        /// </summary>
        const float NoteTop = 120f, NoteH = 110f, NoteMid = NoteTop + NoteH * .5f;
        const float StatusTop = 240f, StatusH = 44f, StatusMid = StatusTop + StatusH * .5f;
        const float StageTop = 300f, StageMid = StageTop + StageH * .5f;
        const float ActH = 124f, ActTop = 998f, ActMid = ActTop + ActH * .5f;

        // The panel is parchment, so it is written in ink rather than in the cream the board uses
        // — `WardBuyOverlay`'s note, and the same measured accents.
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Short = new Color(.58f, .31f, .06f);
        static readonly Color Held = new Color(.18f, .42f, .21f);

        Text _status;
        Btn _act;
        Text _label;
        Image _coin;
        float _lift;

        protected override void Build()
        {
            if (Model == null) { Close(); return; }

            var panel = MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Model.NameKey));

            UIKit.IconButton("Close", panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            // What it does, in the roster's own words. Above the stage, because a player reads the
            // sentence once and then watches the thing fire.
            var note = UIKit.Label("Note", panel, Loc.Get(Model.NoteKey), 30, Pal.A(Ink, .84f),
                                   TextAnchor.UpperCenter, new Vector2(PanelW - 150f, NoteH),
                                   new Vector2(.5f, 1f), new Vector2(0f, -NoteMid), wrap: true);
            UIKit.Shrinkable(note, 22);

            _status = UIKit.Label("Status", panel, string.Empty, 26, Short, TextAnchor.MiddleCenter,
                                  new Vector2(PanelW - 150f, StatusH), new Vector2(.5f, 1f),
                                  new Vector2(0f, -StatusMid));

            BuildStage(panel);
            BuildButton(panel);
            Paint();

            // The ledgers' own events rather than a callback, which is `WardBuyOverlay`'s rule and
            // its reason: a balance can move while this is open (a gem shelf stacked on top), and a
            // panel that only repainted on its own taps would keep a dead button over a purse that
            // could now pay for it.
            PlayerProgression.Changed += Paint;
            WardLedger.Changed += Paint;
            WardLoadout.Changed += Paint;
        }

        void OnDestroy()
        {
            PlayerProgression.Changed -= Paint;
            WardLedger.Changed -= Paint;
            WardLoadout.Changed -= Paint;
        }

        void BuildStage(RectTransform panel)
        {
            // A well for the stage to sit in, so the turret reads as standing somewhere rather than
            // floating on the parchment. Drawn before the stage, so the stage is over it.
            var well = UIKit.Img("Well", panel, Art.Round(30), new Color(.09f, .12f, .16f, .96f),
                                 new Vector2(StageW, StageH), new Vector2(.5f, 1f),
                                 new Vector2(0f, -StageMid));

            var edge = UIKit.Img("Edge", well.transform, Art.RoundOutline(30, 3f),
                                 Pal.A(SiegeView.TintOf(Colour), .40f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var stage = WardFiringStage.Attach(panel, new Vector2(StageW, StageH),
                                               new Vector2(.5f, 1f), new Vector2(0f, -StageMid),
                                               StageCell, PreviewScope);
            stage.Show(Model, Colour);
        }

        void BuildButton(RectTransform panel)
        {
            // Orange, which is this kit's "do the thing" pill (`Skins.Buy`) - the gold one it
            // wore is the hub's battle key and reads as a different kind of control.
            _act = UIKit.Button("Act", panel, Art.S("Ui/" + Skins.Buy), new Vector2(480f, ActH),
                                new Vector2(.5f, 1f), new Vector2(0f, -ActMid), Act);

            _lift = ActH * UIKit.PillFaceLift;

            _label = UIKit.Titled("Label", _act.transform, string.Empty, 38, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(320f, 62f),
                                  new Vector2(.5f, .5f), new Vector2(PriceShift, _lift), 0f, 3f);
            UIKit.Shrinkable(_label, 22);

            _coin = UIKit.Img("Coin", _act.transform, null, Color.white, new Vector2(46f, 46f),
                              new Vector2(.5f, .5f), new Vector2(-118f, _lift));
            _coin.preserveAspect = true;
        }

        /// <summary>
        /// How far the caption sits off centre to leave room for the coin beside it.
        ///
        /// <b>Only when there is a coin.</b> It was a constant, so EQUIP - which has no price and
        /// no glyph - sat eighteen units right of the middle of its own button, off centre with
        /// nothing to explain why.
        /// </summary>
        const float PriceShift = 18f;

        /// <summary>Whether this turret is the one already standing on the colour that raised this.</summary>
        bool Standing
        {
            get
            {
                var stood = WardLoadout.Line.At(Colour);
                return stood != null && stood.Id == Model.Id;
            }
        }

        void Paint()
        {
            if (this == null || Model == null || _act == null) return;

            var offer = WardLedger.OfferFor(Model, PlayerProgression.Level.Level);

            _coin.enabled = false;
            Flipbook.Detach(_coin);

            switch (offer.State)
            {
                case WardPurchaseState.AlreadyHeld:
                    // **Two different answers, and the difference is the whole reason a held turret
                    // now opens a panel at all.** One is an action and one is a statement of where
                    // things already stand.
                    _status.text = Standing
                        ? Loc.Get("ui.loadout.held_one")
                        : string.Empty;
                    _status.color = Held;

                    _label.text = Loc.Get(Standing ? "ui.loadout.standing" : "ui.loadout.stand");
                    break;

                case WardPurchaseState.LevelLocked:
                    _status.text = Loc.Format("ui.loadout.level_note", offer.RequiredLevel);
                    _status.color = Short;
                    _label.text = Loc.Format("ui.loadout.level", offer.RequiredLevel);
                    break;

                case WardPurchaseState.NotForSale:
                    _status.text = Loc.Get("ui.loadout.chest_only");
                    _status.color = Short;
                    _label.text = Loc.Get("ui.loadout.locked");
                    break;

                default:
                    bool gems = offer.Currency == Currency.Gems;

                    _status.text = offer.Shortfall > 0
                        ? Loc.Format(gems ? "ui.shop.short_gems" : "ui.shop.short_coins",
                                     offer.Shortfall)
                        : string.Empty;

                    _status.color = Short;
                    _label.text = offer.Cost.ToString("N0");
                    _coin.enabled = true;

                    // **The coin is a reel, not a sprite** — credits have no still picture in this
                    // UI, only the `Ui/Coin` flipbook, so clearing the sprite for a credit price
                    // would leave an `Image` with none, which is a white rectangle rather than a
                    // coin (invariant 7b).
                    if (gems) _coin.sprite = Art.S("Ui/ic_gem");
                    else Flipbook.Attach(_coin, "Ui/Coin", 11f);
                    break;
            }

            // Centred unless there is a coin to leave room for - see `PriceShift`.
            _label.rectTransform.anchoredPosition =
                new Vector2(_coin.enabled ? PriceShift : 0f, _lift);
        }

        /// <summary>
        /// The one button: stand it, pay for it, or say why neither is on offer.
        ///
        /// <b>A short balance is answered rather than refused</b> — the gem shelf stacks over this
        /// panel and steps out when the gems land, which is <c>GemShopOverlay</c>'s own rule. A
        /// credit shortfall has no shelf to open, so it says the number and nothing else: credits
        /// are earned by playing and there is nothing here to sell.
        /// </summary>
        void Act()
        {
            var offer = WardLedger.OfferFor(Model, PlayerProgression.Level.Level);

            if (offer.State == WardPurchaseState.AlreadyHeld)
            {
                if (Standing) { Close(); return; }

                // **A mechanism rather than a bell**, which is the shelf's own note: standing a
                // turret is an action a player takes several times in a row and one tap to undo,
                // where `unlock` is what an earning sounds like.
                if (WardLoadout.Choose(WardLine.Colours[Colour], Model.Id))
                {
                    Audio.Sfx("stand", .5f);
                    Changed?.Invoke();
                    Close(quiet: true);
                }
                else Audio.Sfx("blocked", .4f);

                return;
            }

            if (offer.State == WardPurchaseState.NotForSale
                || offer.State == WardPurchaseState.LevelLocked)
            {
                Close();
                return;
            }

            if (offer.Shortfall > 0)
            {
                if (offer.Currency == Currency.Gems) Flow.Modal<GemShopOverlay>();
                else Audio.Sfx("blocked", .45f);

                return;
            }

            if (!WardLedger.TryBuy(Model, PlayerProgression.Level.Level))
            {
                Audio.Sfx("blocked", .45f);
                return;
            }

            // **Bought, and then still here.** The panel does not close on a purchase: what the
            // player just paid for is the thing on the stage behind this button, and closing over
            // it would hide the one moment it is worth watching. The button becomes STAND HERE,
            // which is the next thing they want anyway.
            Audio.Sfx("unlock", .6f);
            Changed?.Invoke();
            Paint();
        }
    }
}
