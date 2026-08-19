using GlimmerGrove.Ads;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind a locked stretch of ground: how big it is, what it costs, and the
    /// button that pays.
    ///
    /// <para>
    /// Built like <c>HomesteadBuyOverlay</c> because it is the same transaction wearing
    /// different words, and deliberately not merged with it: what a player is judging here is
    /// <em>room</em> rather than an object, so the panel leads with how many tiles they are
    /// getting and shows no picture at all. A thumbnail of a patch of grass would be a picture
    /// of nothing.
    /// </para>
    /// <para>
    /// A player who is short opens the coin offer rather than meeting a dead button, which is
    /// <c>CompanionUnlockOverlay</c>'s call and for its reason: this is the moment somebody has
    /// decided they want something, which is the best moment in the game to offer a video and
    /// the worst to teach them a control does nothing.
    /// </para>
    /// </summary>
    public sealed class GroveLandOverlay : ModalView
    {
        /// <summary>
        /// The land being offered. Set by the caller before Build runs.
        ///
        /// A property rather than a field for <c>CompanionUnlockOverlay.Avatar</c>'s reason:
        /// nothing here is assigned through the inspector, and a public field of a type Unity
        /// cannot serialise earns a warning about serialisation that will never happen.
        /// </summary>
        public GroveRegion Region { get; set; }

        const float PanelW = 860f;
        const float PanelH = 720f;

        Btn _action;
        Text _status, _size;
        bool _paid, _buying;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH),
                      Region == null ? Loc.Get("ui.land.title") : Loc.Get(Region.NameKey));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Verdant, 380f, .16f, new Vector2(0f, 96f));

            // How much room, in the unit the player is actually buying. A region's dimensions
            // rather than its area alone, because "6 x 5" is a shape somebody can picture and
            // "30 tiles" is a number.
            _size = UIKit.Shrinkable(
                UIKit.Titled("Size", Panel, SizeText(), 52, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(640f, 72f), new Vector2(.5f, .5f), new Vector2(0f, 110f), 3f, 3f), 32);

            UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get("ui.land.note"), 26,
                             new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                             new Vector2(640f, 76f), new Vector2(.5f, .5f), new Vector2(0f, 22f), 3f, 0f), 18);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 30, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(660f, 44f), new Vector2(.5f, .5f), new Vector2(0f, -66f), 3f, 3f), 20);

            BuildAction();

            // A balance can move under an open panel: a chest opened elsewhere, a sync landing
            // the server's figure, an ad paying out through the offer this panel opened.
            PlayerProgression.Changed += Repaint;
        }

        void OnDestroy() => PlayerProgression.Changed -= Repaint;

        public override bool OnBack() { Close(); return true; }

        string SizeText()
            => Region == null ? string.Empty : Loc.Format("ui.land.size", Region.Cols, Region.Rows);

        // ---------------------------------------------------------------- action
        void BuildAction()
        {
            var offer = GroveLand.OfferFor(Region);

            var size = new Vector2(560f, 122f);
            var anchor = new Vector2(.5f, 0f);
            var at = new Vector2(0f, 104f);

            if (offer.State == HomesteadPurchaseState.TooExpensive)
            {
                _action = UIKit.TextButton("Earn", Panel, "btn_blue", Loc.Get("ui.companion.get_coins"), 40,
                                           size, anchor, at, OnGetCoins, "ic_play");
            }
            else
            {
                _action = UIKit.TextButton("Buy", Panel, "btn_green",
                                           Loc.Format("ui.grove.buy_for", Compact.Number(offer.Cost)), 40,
                                           size, anchor, at, OnBuy);
                _action.Interactable = offer.CanBuy;
            }

            UIKit.Shrinkable(_action.Label, 24);
            UIKit.FitLabel(_action);

            Repaint();
        }

        /// <summary>
        /// Redraws the status line and swaps the button when the balance moves under the panel.
        ///
        /// Guarded on <see cref="_buying"/> for <c>CompanionUnlockOverlay</c>'s reason: the
        /// debit is booked before the region is recorded as owned, and booking it raises
        /// <c>PlayerProgression.Changed</c> — so there is a moment inside the purchase where the
        /// balance has fallen and the id has not arrived, and a repaint landing there would
        /// destroy the buy button from inside that button's own click handler.
        /// </summary>
        void Repaint()
        {
            if (!this || _paid || _buying) return;

            var offer = GroveLand.OfferFor(Region);

            if (_status)
            {
                _status.text = offer.State == HomesteadPurchaseState.TooExpensive
                    ? Loc.Format("ui.grove.price", Compact.Number(offer.Cost))
                    : Loc.Format("ui.land.balance", Compact.Number(offer.Balance));

                _status.color = offer.State == HomesteadPurchaseState.TooExpensive
                    ? Pal.A(Pal.Sun, .90f)
                    : new Color(1f, .96f, .88f, .72f);
            }

            if (_action) _action.Interactable = offer.CanBuy || offer.State == HomesteadPurchaseState.TooExpensive;
        }

        void OnBuy()
        {
            if (_paid || _buying) return;

            _buying = true;
            bool bought;

            try
            {
                // Re-checked here rather than trusted from the button, because the balance can
                // have moved since it was painted.
                bought = GroveLand.TryBuy(Region);
            }
            finally
            {
                _buying = false;
            }

            if (!bought) { Repaint(); return; }

            _paid = true;
            Audio.Sfx("unlock", .6f);

            // Nothing is reported to the screen behind: it repaints on GroveLand.Changed, which
            // the purchase already raised. An event cannot be forgotten by a new call site and a
            // callback threaded through each of them will be.
            Close();
        }

        void OnGetCoins()
        {
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.CoinBonus;
                v.Rewarded = () => { if (this) Repaint(); };
            });
        }
    }
}
