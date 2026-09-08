using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind an empty slot on the action bar: what the utility is, what it does, what
    /// it costs in gems, and the button that pays.
    ///
    /// <para>
    /// <b>Deliberately smaller than <see cref="HomesteadBuyOverlay"/>, and the difference is the
    /// stepper.</b> Grove decor is bought by the bundle and a player ordering three of something
    /// sold in tens is agreeing to thirty, so that panel has to count out loud. A utility is
    /// bought one at a time up to a ceiling of nine — the stepper would be a control with two
    /// stops on a decision worth eight gems, which is a lot of panel for very little question.
    /// </para>
    /// <para>
    /// <b>It opens over a live board and must not disturb it.</b> A siege's clock does not stop
    /// for a modal (<c>SiegeScreen.Runnable</c>), so this is the one shop in the game a player
    /// can open while something is walking down a hill at them — which is why it says its price
    /// and closes, and why the ceremony a grove piece gets would be wrong here.
    /// </para>
    /// <para>
    /// <b>A short balance keeps a live button</b>, which is <see cref="HomesteadBuyOverlay"/>'s
    /// rule and its reason: this is the moment a player has decided they want something, and a
    /// greyed control spends it on teaching them the feature is broken. The gem shelf is stacked
    /// on top rather than navigated to, because the board behind is still running — the same
    /// argument <c>ContinueOverlay</c> makes about a frozen board, with a clock instead of a
    /// freeze.
    /// </para>
    /// </summary>
    public sealed class UtilityBuyOverlay : ModalView
    {
        /// <summary>
        /// The utility being offered. A property rather than a field for
        /// <c>HomesteadBuyOverlay.Piece</c>'s reason: <see cref="UtilityItem"/> is not
        /// <c>[Serializable]</c>, so a public field earns a warning about serialisation that will
        /// never happen.
        /// </summary>
        public UtilityItem Item { get; set; }

        /// <summary>Raised after a purchase lands, so the bar behind can repaint.</summary>
        public System.Action Bought { get; set; }

        const float PanelW = 780f, PanelH = 860f;

        // The panel is parchment, so it is written in ink rather than in the cream the board
        // uses — HomesteadBuyOverlay's note, and the same measured accents.
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Short = new Color(.58f, .31f, .06f);
        static readonly Color Held = new Color(.18f, .42f, .21f);

        Image _art;
        Text _note, _status;
        Btn _action;
        bool _paid, _buying;

        protected override void Build()
        {
            if (Item == null) { Close(); return; }

            MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Item.NameKey));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Sun, 380f, .16f, new Vector2(0f, 96f));

            _art = UIKit.Img("A", Panel, Art.S(Item.Art), Color.white, new Vector2(250f, 250f),
                             new Vector2(.5f, .5f), new Vector2(0f, 96f));
            _art.preserveAspect = true;
            _art.raycastTarget = false;

            // What it does, authored per utility rather than written here: the sentence differs
            // across the catalog, and one that is true of a firepot and false of a mending is
            // how a player stops believing the panel.
            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get(Item.NoteKey), 27, Ink,
                             TextAnchor.MiddleCenter, new Vector2(620f, 96f),
                             new Vector2(.5f, .5f), new Vector2(0f, -108f),
                             outline: 0f, shadow: 0f, wrap: true), 19);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 29, Ink, TextAnchor.MiddleCenter,
                             new Vector2(640f, 52f), new Vector2(.5f, .5f),
                             new Vector2(0f, -206f), outline: 0f, shadow: 0f, wrap: true), 20);

            BuildAction();

            // A balance can move under an open panel: the gem shelf stacked on this one, a sync
            // landing the server's figure, a chest opened elsewhere.
            PlayerProgression.Changed += Repaint;
            UtilityLedger.Changed += Repaint;

            Repaint();
        }

        void OnDestroy()
        {
            PlayerProgression.Changed -= Repaint;
            UtilityLedger.Changed -= Repaint;
        }

        public override bool OnBack() { Close(); return true; }

        // --------------------------------------------------------------- the button
        void BuildAction()
        {
            var size = new Vector2(560f, 122f);
            var anchor = new Vector2(.5f, 0f);
            var at = new Vector2(0f, 108f);

            if (UtilityLedger.WhyNotBuy(Item, 1) == UtilityRefusal.Poor)
            {
                _action = UIKit.TextButton("Gems", Panel, "btn_blue",
                                           Loc.Get("ui.utility.get_gems"), 40,
                                           size, anchor, at, OnGetGems, "ic_gem");
                return;
            }

            _action = UIKit.TextButton("Buy", Panel, "btn_green", BuyLabel(), 40,
                                       size, anchor, at, OnBuy, "ic_gem");
            _action.IconTrails = true;
            UIKit.FitLabel(_action);
        }

        string BuyLabel()
            => Loc.Format("ui.utility.buy", Compact.Number(UtilityLedger.Quote(Item, 1)));

        void Repaint()
        {
            if (_paid || Item == null || _status == null || !_status) return;

            var refusal = UtilityLedger.WhyNotBuy(Item, 1);

            switch (refusal)
            {
                case UtilityRefusal.Poor:
                    _status.text = Loc.Format("ui.utility.short",
                                              Compact.Number(UtilityLedger.Quote(Item, 1)
                                                             - PlayerProgression.Gems),
                                              Compact.Number(PlayerProgression.Gems));
                    _status.color = Short;
                    break;

                case UtilityRefusal.Full:
                    _status.text = Loc.Format("ui.utility.full", Item.MaxHeld);
                    _status.color = Held;
                    break;

                case UtilityRefusal.NotForSale:
                    _status.text = Loc.Get("ui.utility.chest_only");
                    _status.color = Held;
                    break;

                default:
                    _status.text = Loc.Format("ui.utility.holding",
                                              UtilityLedger.Held(Item), Item.MaxHeld);
                    _status.color = Ink;
                    break;
            }

            // The button swaps between paying and earning, so it is rebuilt rather than
            // relabelled when the answer changes — HomesteadBuyOverlay's shape, minus the
            // stepper it has to keep in step.
            bool wantsGems = refusal == UtilityRefusal.Poor;
            bool showingGems = _action != null && _action && _action.name == "Gems";

            if (wantsGems == showingGems)
            {
                if (_action != null && _action && !showingGems)
                {
                    _action.Label.text = BuyLabel();
                    _action.Interactable = refusal == UtilityRefusal.None;
                    UIKit.FitLabel(_action);
                }

                return;
            }

            var old = _action.gameObject;
            old.SetActive(false);                 // Destroy only lands at end of frame
            Destroy(old);
            _action = null;

            BuildAction();
        }

        void OnGetGems()
        {
            // Stacked on this panel rather than navigated to, because the board behind is a
            // siege and its clock does not stop — leaving the screen to buy gems would be
            // leaving a run that is still being lost. ContinueOverlay's rule, one screen along.
            Flow.Modal<GemShopOverlay>(v => v.Bought = () => { if (this) Repaint(); });
        }

        void OnBuy()
        {
            if (_paid || _buying || Item == null) return;

            _buying = true;
            bool bought;

            try
            {
                // Re-checked inside the ledger rather than trusted from the button: the balance
                // can have moved since it was painted, and the ledger is the only thing that
                // takes the gems and hands over the item in one step.
                bought = UtilityLedger.TryBuy(Item, 1, out _);
            }
            finally
            {
                _buying = false;
            }

            if (!bought) { Repaint(); return; }

            _paid = true;

            Audio.Sfx("coin", .6f);
            Tween.Punch(_art.transform, .2f, .45f);

            if (_action) _action.Interactable = false;
            if (_note) _note.text = Loc.Get("ui.utility.bought");

            Bought?.Invoke();

            // Closed on a short delay rather than at once, so the player sees the panel confirm
            // before it goes — and with no unveiling, because the board behind is still running.
            Tween.After(.55f, () => { if (this) Close(); }, this);
        }
    }
}
