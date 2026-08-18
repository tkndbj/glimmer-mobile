using GlimmerGrove.Ads;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind a piece the player does not hold yet: what it is, what it costs, and
    /// the button that pays.
    ///
    /// <para>
    /// Deliberately smaller than <c>CompanionUnlockOverlay</c>, which it otherwise resembles.
    /// That panel has to explain <em>two</em> routes to the same companion, because a
    /// companion is reached by levelling as well as by paying and one that mentioned only the
    /// price would read as a paywall on something the player was going to be given. A shop
    /// piece has one route. Showing an absent second one would be filler.
    /// </para>
    /// <para>
    /// <b>A short balance keeps a live button.</b> It swaps the price for the coin offer
    /// rather than greying out, which is invariant-free good sense the companion panel argues
    /// at length: this is the moment a player has decided they want something, the single best
    /// moment in the game to offer a video, and a disabled control spends it on teaching them
    /// the feature is broken. The panel has already said how far short they are, so nothing is
    /// being hidden.
    /// </para>
    /// <para>
    /// <b>What is bought is permission, not a copy.</b> The caption says so, once, because a
    /// player deciding between a 480-credit fence and a 4,000-credit cottage is making a
    /// completely different decision if the fence can line the whole grove — and it can. See
    /// <see cref="HomesteadPiece"/> for why that is also the only shape the save file permits.
    /// </para>
    /// </summary>
    public sealed class HomesteadBuyOverlay : ModalView
    {
        /// <summary>
        /// The piece being offered. Set by the caller before Build runs.
        ///
        /// A property rather than a field for <c>CompanionUnlockOverlay.Avatar</c>'s reason:
        /// <see cref="HomesteadPiece"/> is not <c>[Serializable]</c>, so a public field of that
        /// type earns a UAC1001 warning about serialisation that will never happen.
        /// </summary>
        public HomesteadPiece Piece { get; set; }

        const float PanelW = 820f;
        const float PanelH = 900f;

        Image _art;
        Text _status, _note;
        Btn _action;
        bool _paid, _buying;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Piece.NameKey));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Sun, 420f, .16f, new Vector2(0f, 86f));

            _art = UIKit.Img("A", Panel, null, Color.white, new Vector2(280f, 280f),
                             new Vector2(.5f, .5f), new Vector2(0f, 86f));
            _art.preserveAspect = true;
            _art.raycastTarget = false;
            HomesteadArt.Paint(_art, Piece);

            // The one sentence about what a purchase actually buys. Read from the rules rather
            // than written into the copy is the house style here, but this is a property of the
            // whole feature rather than of any number, so it is a plain string.
            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get("ui.grove.buy_note"), 26,
                             new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                             new Vector2(640f, 76f), new Vector2(.5f, .5f), new Vector2(0f, -142f), 3f, 0f), 18);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 30, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(660f, 44f), new Vector2(.5f, .5f), new Vector2(0f, -228f), 3f, 3f), 20);

            BuildAction();

            // This piece's own kind, which the shop behind is almost certainly already
            // showing — so this is a no-op that calls back, rather than a second load. The art
            // may still be arriving either way: an Image with no sprite is a white rectangle.
            HomesteadArt.OpenKindAsync(Piece.Slot, () => { if (this) HomesteadArt.Paint(_art, Piece); });

            // A balance can move under an open panel: a chest opened elsewhere, a sync landing
            // the server's figure, an ad paying out through the offer this panel opened.
            PlayerProgression.Changed += Repaint;
        }

        void OnDestroy() => PlayerProgression.Changed -= Repaint;

        public override bool OnBack() { Close(); return true; }

        // ---------------------------------------------------------------- action
        void BuildAction()
        {
            var offer = HomesteadLedger.OfferFor(Piece);

            var size = new Vector2(560f, 122f);
            var anchor = new Vector2(.5f, 0f);
            var at = new Vector2(0f, 108f);

            if (offer.State == HomesteadPurchaseState.TooExpensive)
            {
                _action = UIKit.TextButton("Earn", Panel, "btn_blue", Loc.Get("ui.companion.get_coins"), 40,
                                           size, anchor, at, OnGetCoins, "ic_play");
            }
            else
            {
                _action = UIKit.TextButton("Buy", Panel, "btn_green",
                                           Loc.Format("ui.grove.buy_for", offer.Cost), 40,
                                           size, anchor, at, OnBuy);
                _action.Interactable = offer.CanBuy;
            }

            UIKit.Shrinkable(_action.Label, 22);
            UIKit.FitLabel(_action);

            PaintStatus(offer);
        }

        /// <summary>
        /// Repaints the caption, and rebuilds the button only when the state it renders has
        /// actually moved.
        ///
        /// Rebuilding on every balance change would cancel a press the player is halfway
        /// through, because this runs whenever anything anywhere pays out — the trap
        /// <c>CompanionUnlockOverlay.Repaint</c> documents.
        /// </summary>
        void Repaint()
        {
            if (_paid || this == null) return;

            var offer = HomesteadLedger.OfferFor(Piece);
            PaintStatus(offer);

            bool wantsBuy = offer.State != HomesteadPurchaseState.TooExpensive;
            bool showingBuy = _action != null && _action && _action.name == "Buy";
            if (wantsBuy == showingBuy) return;

            var old = _action.gameObject;
            old.SetActive(false);              // Destroy only lands at end of frame
            Destroy(old);
            _action = null;

            BuildAction();
        }

        void PaintStatus(HomesteadOffer offer)
        {
            if (_status == null) return;

            switch (offer.State)
            {
                case HomesteadPurchaseState.TooExpensive:
                    _status.text = Loc.Format("ui.companion.short", offer.Shortfall, offer.Balance);
                    _status.color = Pal.A(Pal.Sun, .95f);
                    break;

                case HomesteadPurchaseState.AlreadyHeld:
                    _status.text = Loc.Get("ui.grove.yours");
                    _status.color = Pal.A(Pal.Mint, .95f);
                    break;

                default:
                    _status.text = Loc.Format("ui.companion.balance", offer.Balance);
                    _status.color = new Color(1f, .96f, .88f, .78f);
                    break;
            }
        }

        void OnGetCoins()
        {
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.CoinBonus;
                v.Rewarded = () => { if (this) Repaint(); };
            });
        }

        void OnBuy()
        {
            if (_paid || _buying) return;

            _buying = true;
            bool bought;

            try
            {
                // Re-checked inside the ledger rather than trusted from the button, because the
                // balance can have moved since it was painted — a spend on another screen, or a
                // sync that replaced a claim with the server's smaller figure.
                bought = HomesteadLedger.TryBuy(Piece);
            }
            finally
            {
                // Cleared before either branch below, so a throw cannot leave the panel
                // permanently unable to repaint itself.
                _buying = false;
            }

            if (!bought) { Repaint(); return; }

            _paid = true;

            Audio.Sfx("unlock", .8f);
            Burst.Sparks(Panel, Vector2.zero, Pal.Sun, 18);
            Tween.Punch(_art.transform, .18f, .45f);

            if (_note) _note.text = Loc.Get("ui.grove.bought_note");
            if (_action) _action.Interactable = false;

            // Straight back to whatever asked, rather than growing a "place it now" button. The
            // shop is a page of forty cells and a player who has just bought one thing is
            // usually about to look at the next; the grove is one tap away in the nav bar, and
            // the picker they may have come from reopens on the slot they left.
            Tween.Run(.62f, Ease.Linear, _ => { }, this).OnDone(() => { if (this) Close(); });
        }
    }
}
