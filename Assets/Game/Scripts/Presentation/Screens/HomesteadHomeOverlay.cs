using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Ads;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The player's own front door: the home they have, the one above it, and what it costs.
    ///
    /// <para>
    /// <b>Why the home has a panel of its own rather than a cell in the shop.</b> Everything
    /// else in the grove is a thing among forty things; this is <em>the</em> thing, the one
    /// the whole island is composed around and the only purchase in the game that changes the
    /// screen the moment it is made. A cell in a grid says "another item"; a panel opened by
    /// tapping your own house says "this is yours and it can be better", which is the whole
    /// sentence the feature is trying to say.
    /// </para>
    /// <para>
    /// <b>The ladder is drawn as pips, and that is the pitch.</b> A player who can see there
    /// are five rungs and that they are on the first knows there is somewhere to get to — the
    /// same reason the streak board draws a whole lap rather than only the night you are on.
    /// Hiding it behind a shop scroll would make the best long-term goal in the game the least
    /// visible thing in it.
    /// </para>
    /// <para>
    /// A short balance opens the coin offer instead of greying the button, which is the call
    /// <c>CompanionUnlockOverlay</c> and <see cref="HomesteadBuyOverlay"/> both make and for
    /// their reason: this is the moment a player has decided they want something.
    /// </para>
    /// </summary>
    public sealed class HomesteadHomeOverlay : ModalView
    {

        /// <summary>The art this panel draws, held for exactly as long as it is up.</summary>
        AssetHold _held;
        const float PanelW = 860f;
        const float PanelH = 980f;

        Image _art;
        Text _name, _status;
        Btn _action;
        bool _buying;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.grove.town_hall"));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Sun, 460f, .18f, new Vector2(0f, 116f));

            _art = UIKit.Img("A", Panel, null, Color.white, new Vector2(320f, 320f),
                             new Vector2(.5f, .5f), new Vector2(0f, 116f));
            _art.preserveAspect = true;
            _art.raycastTarget = false;

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", Panel, string.Empty, 36, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(660f, 48f), new Vector2(.5f, .5f), new Vector2(0f, -84f), 3f, 3f), 24);

            // Plain text, deliberately. `Titled` hangs an `Outline` on anything given a width,
            // which is right for a caption over a busy board and wrong here: this sits on a flat
            // panel, where a three-pixel outline on two-and-a-half-line type reads as a fringe
            // rather than as weight, and it was reported as hard to read. Nought is the whole
            // fix — the colour already carries it against the plate.
            //
            // It sits where the rung pips used to, because taking them out left a hole between
            // the name and this line rather than tightening the panel.
            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 31,
                             Pal.A(Pal.Amber, .95f), TextAnchor.MiddleCenter,
                             new Vector2(680f, 88f), new Vector2(.5f, .5f), new Vector2(0f, -170f), 0f, 0f), 21);

            // **Where the long press is taught, and the only place it could be.** A home can be
            // picked up and turned now that its seat is the player's rather than the floor's —
            // and a gesture nobody is told about is a feature nobody has. This panel is what a
            // tap on the hall raises, so it is exactly where somebody who has just tried to move
            // their house by tapping it is looking. Quiet type: it is an aside, not the news this
            // panel is for, and the ladder above it is what a player came here to read.
            UIKit.Shrinkable(
                UIKit.Titled("Hint", Panel, Loc.Get("ui.grove.home_hold"), 22,
                             Pal.A(Pal.Cream, .60f), TextAnchor.MiddleCenter,
                             new Vector2(680f, 44f), new Vector2(.5f, .5f),
                             new Vector2(0f, -262f), 0f, 0f), 17);

            Paint();

            // The catalog is a body and the art is a scope, so both can still be arriving when
            // this is opened from a grove that was itself opened a moment ago.
            // Just the home ladder, not the whole grove. This used to open the Grovement's
            // entire set — every piece the player had placed — to draw one house, because a
            // named scope was all or nothing and a second asker wanting less would have
            // replaced the first.
            _held = GroveArtLoader.Open("grove_homes", GroveArtLoader.Homes(), this, Paint);

            HomesteadLedger.Changed += Paint;
            HomesteadCatalog.Changed += Paint;
            PlayerProgression.Changed += Paint;
        }

        void OnDestroy()
        {
            HomesteadLedger.Changed -= Paint;
            HomesteadCatalog.Changed -= Paint;
            PlayerProgression.Changed -= Paint;

            _held?.Dispose();
        }

        public override bool OnBack() { Close(); return true; }

        // ----------------------------------------------------------------- paint
        /// <summary>
        /// Redraws everything but the button, then rebuilds the button only when what it says
        /// has changed — <c>HomesteadBuyOverlay.Repaint</c>'s bargain, for its reason: this
        /// fires whenever anything anywhere pays out, and rebuilding a control under a thumb
        /// that is halfway through pressing it cancels the press.
        /// </summary>
        void Paint()
        {
            if (this == null || Panel == null) return;

            var catalog = HomesteadCatalog.Current;
            var home = HomesteadLedger.BestDwelling(catalog);
            var next = HomesteadLedger.NextDwelling(catalog);

            HomesteadArt.Paint(_art, home.IsValid ? home : next);

            if (_name)
                _name.text = home.IsValid ? Loc.Get(home.NameKey) : Loc.Get("ui.grove.home");

            var offer = next.IsValid ? HomesteadLedger.OfferFor(next) : default;

            // One colour for all three states, which is the owner's call and worth stating
            // so nobody "fixes" it back. This line is the panel's own accent rather than a
            // signal: what the three states differ in is what they *say*, and the words are
            // unambiguous without a second channel carrying the same news in hue.
            //
            // What it replaced could not have carried that news anyway: the "cannot afford it"
            // line was `Pal.Sun` and the "finest home" line `Pal.Gold`, which differ by six in
            // one channel — two states drawn in the same colour, and a third drawn in cream.
            if (_status)
            {
                if (!next.IsValid)
                {
                    _status.text = Loc.Get("ui.grove.home_best");
                    _status.color = Pal.A(Pal.Amber, .95f);
                }
                else if (offer.State == HomesteadPurchaseState.LevelLocked)
                {
                    // The gate leads whenever it binds, and the shortfall is not mentioned at
                    // all — quoting a price a player cannot act on yet is the refusal invariant
                    // 15a puts last. The rung is still named, because what a player is working
                    // toward is the whole reason this panel exists.
                    _status.text = Loc.Format("ui.grove.home_locked", Loc.Get(next.NameKey),
                                              offer.RequiredLevel);
                    _status.color = Pal.A(Pal.Amber, .95f);
                }
                else if (offer.State == HomesteadPurchaseState.TooExpensive)
                {
                    // The next rung is named even when it cannot be afforded, which the shared
                    // "short by N" line on its own does not do. A player saving for something
                    // should be able to see what it is called.
                    _status.text = Loc.Format("ui.grove.home_short", Loc.Get(next.NameKey),
                                              Compact.Number(offer.Shortfall));
                    _status.color = Pal.A(Pal.Amber, .95f);
                }
                else
                {
                    _status.text = Loc.Format("ui.grove.home_next", Loc.Get(next.NameKey));
                    _status.color = Pal.A(Pal.Amber, .95f);
                }
            }

            string wanted = !next.IsValid ? "None"
                          : offer.State == HomesteadPurchaseState.LevelLocked ? "Gate"
                          : offer.State == HomesteadPurchaseState.TooExpensive ? "Earn"
                          : "Buy";

            if (_action != null && _action && _action.name == wanted) return;

            if (_action)
            {
                var old = _action.gameObject;
                old.SetActive(false);              // Destroy only lands at end of frame
                Destroy(old);
            }

            _action = null;
            BuildAction(wanted, next, offer);
        }

        void BuildAction(string wanted, HomesteadPiece next, HomesteadOffer offer)
        {
            var size = new Vector2(560f, 122f);
            var anchor = new Vector2(.5f, 0f);
            var at = new Vector2(0f, 104f);

            switch (wanted)
            {
                case "None":
                    return;

                // A closed gate names the level rather than offering the coin shelf, which is
                // `CompanionUnlockOverlay`'s call and for its reason: credits cannot open this,
                // and sending somebody to buy coins for a home they still could not buy is the
                // refusal one screen over exists to prevent. It closes rather than doing
                // nothing, so the control is honest about being a way out and not a purchase.
                case "Gate":
                    _action = UIKit.TextButton("Gate", Panel, "btn_blue",
                                               Loc.Format("ui.companion.locked_button",
                                                          offer.RequiredLevel), 40,
                                               size, anchor, at, () => Close());
                    break;

                case "Earn":
                    _action = UIKit.TextButton("Earn", Panel, "btn_blue",
                                               Loc.Get("ui.companion.get_coins"), 40,
                                               size, anchor, at, OnGetCoins, "ic_play");
                    break;

                default:
                    _action = UIKit.TextButton("Buy", Panel, "btn_green",
                                               Loc.Format("ui.grove.upgrade_for", Compact.Number(offer.Cost)), 40,
                                               size, anchor, at, () => OnBuy(next),
                                               Art.CoinFace(), iconTrails: true);
                    _action.Interactable = offer.CanBuy;
                    break;
            }

            UIKit.Shrinkable(_action.Label, 22);
            UIKit.FitLabel(_action);
        }

        void OnGetCoins()
        {
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.CoinBonus;
                v.Rewarded = () => { if (this) Paint(); };
            });
        }

        /// <summary>
        /// Buys the next rung and stays open, which is the opposite of what the shop's panel
        /// does and deliberate: the grove behind this one has just redrawn the house, so
        /// closing would hide the thing the player paid for at the instant they paid for it.
        /// The panel repaints onto the new rung and offers the one after it.
        /// </summary>
        void OnBuy(HomesteadPiece next)
        {
            if (_buying) return;

            _buying = true;
            bool bought;

            try { bought = HomesteadLedger.TryBuy(next); }
            finally { _buying = false; }

            if (!bought) { Paint(); return; }

            Audio.Sfx("coin", .6f);
            Tween.Punch(_art.transform, .22f, .5f);

            // Repainted first, so the panel underneath is already showing the new house and
            // offering the rung above it by the time the ceremony clears — the same reason this
            // panel stays open at all.
            Paint();

            // A home is the loudest thing the grove sells and the only purchase that changes
            // the whole island, so it gets the full unveiling rather than a spark burst. It
            // lands in the top two tiers on price alone (see GroveUnveil), which is where the
            // confetti and the struck seal live.
            var bring = next;
            Flow.Modal<GroveUnveilOverlay>(v => v.Piece = bring);
        }
    }
}
