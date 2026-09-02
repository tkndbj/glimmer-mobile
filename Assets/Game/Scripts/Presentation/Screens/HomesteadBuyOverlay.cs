using GlimmerGrove.Ads;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind a piece in the shop: what it is, what it costs, how many a purchase
    /// gives you, and the button that pays.
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
    /// <b>What is bought depends on what it is, and the panel says which.</b> A resident, a
    /// home rung and anything earned by playing are bought once and drawn anywhere. Priced
    /// decor is bought <em>by the copy</em> — usually ten at a time — so this panel grows a
    /// stepper and its caption counts. See <see cref="HomesteadPiece.Bundle"/>.
    /// </para>
    /// <para>
    /// <b>The stepper's stops are the ledger's, never this panel's.</b> Every bound that
    /// decides how many a player may order — what they can afford, the per-purchase ceiling,
    /// the room left in their stock — lives in <c>HomesteadLedger.MaxQuantity</c>, and the
    /// price of an order comes back on the offer rather than being multiplied here. A panel
    /// that did its own arithmetic would be a second answer for the ledger to disagree with,
    /// on the screen where disagreeing means charging somebody the wrong number.
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

        /// <summary>
        /// Body copy on this panel, and it is a dark ink rather than the cream the rest of
        /// the game writes in.
        ///
        /// <para>
        /// <c>panel_main</c> is a light parchment, so a cream label on it is a pale thing on a
        /// pale ground held apart only by <c>Titled</c>'s outline — which at 26 point is a
        /// dark rim thicker than the strokes it surrounds, and reads as a smudge rather than
        /// as a sentence. Every other panel on this skin already writes in ink with no outline
        /// at all (<c>CompanionUnlockOverlay</c>, <c>AccountOverlay</c>); this one did not, and
        /// it was reported as exactly that.
        /// </para>
        /// <para>
        /// The accents are darkened for the same reason. <c>Pal.Mint</c> and <c>Pal.Sun</c> are
        /// chosen to sit on the board's near-black plate; on parchment they are two shades of
        /// nothing. <b>A palette colour is a colour against a named ground</b> — carrying one
        /// onto a different ground is a decision, not a default.
        /// </para>
        /// </summary>
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Deep = new Color(.30f, .20f, .13f);
        // 4.7:1 on the parchment, measured. The obvious .62/.34/.08 reads warmer and
        // lands at 4.17, which clears the bar for a heading and misses it for the
        // 29pt line this actually is.
        static readonly Color Short = new Color(.58f, .31f, .06f);
        static readonly Color Held = new Color(.18f, .42f, .21f);

        /// <summary>
        /// The panel is measured rather than fixed, because the stepper is a row that only
        /// half the catalog has. A constant tall enough for both would leave a hole under
        /// every resident and every home rung — <c>WinOverlay</c>'s lesson, in miniature.
        /// </summary>
        const float PanelBase = 900f;
        const float StepperRoom = 200f;

        /// <summary>
        /// How far the art, the halo and the note ride up when the stepper is present.
        ///
        /// <para>
        /// <c>MakePanel</c> centres the panel, so growing it puts <em>half</em> the new room
        /// above the art as well as half below — which reads as a hole under the header rather
        /// than as space for the stepper. Lifting the block above the stepper by the same half
        /// keeps every gap at the top exactly what it is on a panel with no stepper, and spends
        /// all the new room where it was asked for.
        /// </para>
        /// <para>
        /// Caught on a device rather than reasoned about: the first version left the status
        /// line drawn <em>underneath</em> the buy button, which compiles, validates and reads
        /// perfectly in the source.
        /// </para>
        /// </summary>
        float Lift => Stocked ? StepperRoom * .5f : 0f;

        Image _art;
        Text _status, _note, _count, _copies;
        Btn _action, _less, _more;
        bool _paid, _buying;

        /// <summary>How many bundles the buy button is currently offering. Always at least 1.</summary>
        int _quantity = 1;

        bool Stocked => Piece.IsStocked;

        protected override void Build()
        {
            float height = PanelBase + (Stocked ? StepperRoom : 0f);
            MakePanel(new Vector2(PanelW, height), Loc.Get(Piece.NameKey));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Sun, 420f, .16f, new Vector2(0f, 86f + Lift));

            _art = UIKit.Img("A", Panel, null, Color.white, new Vector2(280f, 280f),
                             new Vector2(.5f, .5f), new Vector2(0f, 86f + Lift));
            _art.preserveAspect = true;
            _art.raycastTarget = false;
            HomesteadArt.PaintThumb(_art, Piece);

            // What a purchase actually buys. Read from the piece rather than written into the
            // copy, because the answer differs across the catalog and a sentence that is true
            // of a fence and false of a friend is how a player stops believing the panel.
            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, NoteText(), 27, Ink, TextAnchor.MiddleCenter,
                             new Vector2(640f, 76f), new Vector2(.5f, .5f),
                             new Vector2(0f, -142f + Lift), outline: 0f, shadow: 0f, wrap: true), 19);

            if (Stocked) BuildStepper();

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 29, Ink, TextAnchor.MiddleCenter,
                             new Vector2(660f, 52f), new Vector2(.5f, .5f),
                             new Vector2(0f, Stocked ? StatusY : -228f),
                             outline: 0f, shadow: 0f, wrap: true), 20);

            BuildAction();

            // This piece's own shelf, which the shop behind is almost certainly already
            // showing — so this is a no-op that calls back, rather than a second load. The art
            // may still be arriving either way: an Image with no sprite is a white rectangle.
            //
            // Drawn from the shelf's browse atlas, like the cell that opened this panel. A
            // thumbnail is cut at 256 and this frame is 280, so the difference is a hair of
            // softness — against loading a 512-pixel texture, and its whole bundle, for one
            // picture on a panel that is dismissed in two seconds.
            HomesteadArt.OpenShelfAsync(GroveShelves.Of(Piece),
                                        () => { if (this) HomesteadArt.PaintThumb(_art, Piece); });

            // A balance can move under an open panel: a chest opened elsewhere, a sync landing
            // the server's figure, an ad paying out through the offer this panel opened.
            PlayerProgression.Changed += Repaint;
        }

        void OnDestroy() => PlayerProgression.Changed -= Repaint;

        public override bool OnBack() { Close(); return true; }

        // --------------------------------------------------------------- stepper
        /// <summary>
        /// Minus, a count, plus — and under the count, what the order actually delivers.
        ///
        /// <para>
        /// The second line is the one that matters and it is why this is not a bare number
        /// field: a player ordering three of something that comes in tens is buying thirty
        /// fences, and a stepper reading "3" with the price beside it does not say so. Every
        /// complaint a shop gets about quantity is a player who did not know what they were
        /// agreeing to.
        /// </para>
        /// </summary>
        /// <summary>
        /// Where the status line sits on a panel with a stepper: below the stepper block and
        /// clear of the buy button, which is bottom-anchored and 122 tall.
        /// </summary>
        const float StatusY = -320f;

        void BuildStepper()
        {
            const float Y = -190f;

            _less = Step("Less", "−", new Vector2(-214f, Y), -1);
            _more = Step("More", "+", new Vector2(214f, Y), +1);

            _count = UIKit.Titled("Count", Panel, string.Empty, 52, Deep,
                                  TextAnchor.MiddleCenter, new Vector2(300f, 62f),
                                  new Vector2(.5f, .5f), new Vector2(0f, Y + 20f),
                                  outline: 0f, shadow: 2f);

            _copies = UIKit.Shrinkable(
                UIKit.Titled("Copies", Panel, string.Empty, 26, Held,
                             TextAnchor.MiddleCenter, new Vector2(360f, 40f),
                             new Vector2(.5f, .5f), new Vector2(0f, Y - 34f),
                             outline: 0f, shadow: 0f), 18);

        }

        Btn Step(string name, string glyph, Vector2 at, int delta)
        {
            var size = new Vector2(96f, 96f);
            var b = UIKit.Button(name, Panel, Art.S("Ui/" + Skins.Nav), size,
                                 new Vector2(.5f, .5f), at, () => Nudge(delta));

            UIKit.Titled("G", b.transform, glyph, 54, Pal.Cream, TextAnchor.MiddleCenter,
                         size, new Vector2(.5f, .5f),
                         new Vector2(0f, size.y * UIKit.SquareFaceLift), 0f, 0f);

            return b;
        }

        /// <summary>
        /// Moves the order by one, clamped to what the ledger will actually sell.
        ///
        /// The upper stop is re-read on every tap rather than cached at build, because the
        /// balance moves under this panel — an ad paying out mid-decision should raise the
        /// stop, and a sync landing the server's smaller figure should lower it.
        /// </summary>
        void Nudge(int delta)
        {
            if (_paid) return;

            int most = HomesteadLedger.MaxQuantity(Piece);
            int wanted = _quantity + delta;

            if (wanted < 1) wanted = 1;
            if (wanted > most) wanted = most;
            if (wanted == _quantity) return;

            _quantity = wanted;
            Audio.Sfx("click", .5f);
            Repaint();
        }

        // ---------------------------------------------------------------- action
        void BuildAction()
        {
            var offer = HomesteadLedger.OfferFor(Piece, _quantity);

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
                // The coin goes after the figure, because it is the unit on the number and
                // not a label on the verb — "BUY FOR 4,500 ⬤" rather than "⬤ BUY FOR 4,500".
                // See Btn.IconTrails.
                _action = UIKit.TextButton("Buy", Panel, "btn_green", BuyLabel(offer), 40,
                                           size, anchor, at, OnBuy, Art.CoinFace(), iconTrails: true);
                _action.Interactable = offer.CanBuy;
            }

            UIKit.Shrinkable(_action.Label, 22);
            UIKit.FitLabel(_action);

            PaintAll(offer);
        }

        /// <summary>
        /// The button's caption: the plain price for a one-off, the count and the price for an
        /// order. Never a unit price — what the button takes is what the button says.
        /// </summary>
        string BuyLabel(HomesteadOffer offer)
            => Stocked && offer.Quantity > 1
                ? Loc.Format("ui.grove.buy_bundle", offer.Quantity, Compact.Number(offer.Cost))
                : Loc.Format("ui.grove.buy_for", Compact.Number(offer.Cost));

        /// <summary>
        /// Repaints the captions, and rebuilds the button only when the state it renders has
        /// actually moved.
        ///
        /// Rebuilding on every balance change would cancel a press the player is halfway
        /// through, because this runs whenever anything anywhere pays out — the trap
        /// <c>CompanionUnlockOverlay.Repaint</c> documents.
        /// </summary>
        void Repaint()
        {
            if (_paid || this == null) return;

            // The order can stop being affordable while the panel is open, and a stepper left
            // reading 5 over a button that will only sell 2 is the panel lying about the thing
            // it exists to be exact about.
            int most = HomesteadLedger.MaxQuantity(Piece);
            if (_quantity > most) _quantity = most < 1 ? 1 : most;

            var offer = HomesteadLedger.OfferFor(Piece, _quantity);
            PaintAll(offer);

            bool wantsBuy = offer.State != HomesteadPurchaseState.TooExpensive;
            bool showingBuy = _action != null && _action && _action.name == "Buy";

            if (wantsBuy == showingBuy)
            {
                if (showingBuy && _action != null && _action)
                {
                    _action.Label.text = BuyLabel(offer);
                    _action.Interactable = offer.CanBuy;
                    UIKit.FitLabel(_action);
                }

                return;
            }

            var old = _action.gameObject;
            old.SetActive(false);              // Destroy only lands at end of frame
            Destroy(old);
            _action = null;

            BuildAction();
        }

        void PaintAll(HomesteadOffer offer)
        {
            PaintStatus(offer);
            PaintStepper(offer);
        }

        void PaintStepper(HomesteadOffer offer)
        {
            if (_count == null || !_count) return;

            int most = HomesteadLedger.MaxQuantity(Piece);

            _count.text = "×" + offer.Quantity;
            // What this order delivers, which is not the same sentence as what is left to
            // place — the first version reused the picker's "N left" here and it read as a
            // stock line under a quantity, on the one control whose whole job is to be exact
            // about how many a player is agreeing to.
            _copies.text = Loc.Format("ui.grove.order_copies", offer.Copies);

            // Greyed rather than hidden at the stops: a control that disappears takes the
            // layout with it, and a player who has just pressed + four times needs to see why
            // the fifth did nothing.
            if (_less) _less.Interactable = offer.Quantity > 1;
            if (_more) _more.Interactable = offer.Quantity < most;
        }


        string NoteText()
        {
            if (!Stocked) return Loc.Get("ui.grove.buy_note");

            return Piece.Bundle > 1
                ? Loc.Format("ui.grove.bundle_note", Piece.Bundle)
                : Loc.Get("ui.grove.single_note");
        }

        void PaintStatus(HomesteadOffer offer)
        {
            if (_status == null) return;

            switch (offer.State)
            {
                case HomesteadPurchaseState.TooExpensive:
                    _status.text = Loc.Format("ui.companion.short", Compact.Number(offer.Shortfall), Compact.Number(offer.Balance));
                    _status.color = Short;
                    break;

                case HomesteadPurchaseState.AlreadyHeld:
                    // For stock this is the ceiling rather than "you own it", because a stocked
                    // piece is never done being sold — the two readings are one enum member and
                    // only the piece can tell them apart.
                    _status.text = Stocked ? Loc.Get("ui.grove.stock_full") : Loc.Get("ui.grove.yours");
                    _status.color = Held;
                    break;

                default:
                    _status.text = Stocked ? StockLine() : Loc.Format("ui.companion.balance", Compact.Number(offer.Balance));
                    _status.color = Ink;
                    break;
            }
        }

        /// <summary>
        /// What the player is already holding: how many are free to place, and how many are
        /// standing in the grove.
        ///
        /// Both halves, because either alone is misleading. "You have 4" reads as four to
        /// place when six of them are already out; "6 placed" says nothing about whether
        /// buying more is needed.
        /// </summary>
        string StockLine()
        {
            int bought = HomesteadLedger.Copies(Piece);
            if (bought <= 0) return Loc.Get("ui.grove.stock_first");

            return Loc.Format("ui.grove.stock_have",
                              HomesteadLedger.Available(Piece), HomesteadLayout.CountOf(Piece.Id));
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
            int ordered = _quantity;

            try
            {
                // Re-checked inside the ledger rather than trusted from the button, because the
                // balance can have moved since it was painted — a spend on another screen, or a
                // sync that replaced a claim with the server's smaller figure.
                bought = HomesteadLedger.TryBuy(Piece, ordered);
            }
            finally
            {
                // Cleared before either branch below, so a throw cannot leave the panel
                // permanently unable to repaint itself.
                _buying = false;
            }

            if (!bought) { Repaint(); return; }

            _paid = true;

            // The coin is the money leaving. What the piece arriving sounds like belongs to the
            // ceremony that is about to play it — GroveLandOverlay's split, for its reason.
            Audio.Sfx("coin", .6f);
            Tween.Punch(_art.transform, .18f, .45f);

            if (_note)
                _note.text = Stocked
                    ? Loc.Format("ui.grove.bought_copies", ordered * (Piece.Bundle < 1 ? 1 : Piece.Bundle))
                    : Loc.Get("ui.grove.bought_note");

            if (_action) _action.Interactable = false;
            if (_less) _less.Interactable = false;
            if (_more) _more.Interactable = false;

            // Hands over to the unveiling rather than simply closing. A piece bought out of a
            // grid of a hundred and fifty is the same shape of moment as a companion bought out
            // of the roster, and that one has had a reveal since the day it shipped; this panel
            // had a spark burst and a fade. See GroveUnveilOverlay — and note the sequence is
            // raised *after* the close, so the shop is what it lands over and the cell behind it
            // has already repainted.
            var piece = Piece;
            Close(() => Flow.Modal<GroveUnveilOverlay>(v => v.Piece = piece), quiet: true);
        }
    }
}
