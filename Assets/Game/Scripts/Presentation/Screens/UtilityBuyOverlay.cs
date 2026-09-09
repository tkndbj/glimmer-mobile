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
    /// <b>It carries a stepper now, and the reason it did not is the reason it does.</b> The
    /// argument against one was that a utility was bought one at a time up to a ceiling of
    /// <em>nine</em>, so the control would have had two stops on a decision worth eight gems.
    /// The ceiling is a hundred (<c>UtilityCatalog.Default</c>), which makes that argument say
    /// the opposite: a shelf that sold a hundred one tap at a time would be a hundred taps, and
    /// a player stocking up before a chapter is doing exactly what the ceiling was raised for.
    /// It counts out loud for <see cref="HomesteadBuyOverlay"/>'s reason — the total is what is
    /// being agreed to, so the button says the total rather than the unit price.
    /// </para>
    /// <para>
    /// <b>It opens from two places and is one panel on purpose.</b> The empty slot on the action
    /// bar raises it over a live siege; the shop's kit shelf raises it over a page of cards. Two
    /// panels would be two prices, two ceilings and two chances to disagree about what somebody
    /// is carrying — <c>RunContinueFlow</c>'s argument, one screen along. The order starts at one,
    /// so nothing about the in-run route costs a tap more than it did.
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

        // The panel grew by the stepper's block rather than by squeezing what was there —
        // HomesteadBuyOverlay's `StepperRoom`, and its lesson, which is that a control counting
        // out loud has to be clear of both the sentence above it and the button below. Every
        // measurement above the stepper is lifted by half the growth, so the picture, the note
        // and the status line sit exactly where they did relative to the panel's own top.
        const float PanelW = 780f, PanelH = 1000f;
        const float StepperRoom = 140f, Lift = StepperRoom * .5f;

        // The panel is parchment, so it is written in ink rather than in the cream the board
        // uses — HomesteadBuyOverlay's note, and the same measured accents.
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Short = new Color(.58f, .31f, .06f);
        static readonly Color Held = new Color(.18f, .42f, .21f);

        Image _art;
        Text _note, _status, _count;
        Btn _action, _less, _more;
        bool _paid, _buying;

        /// <summary>
        /// How many this order is for. Starts at one and is clamped to
        /// <c>UtilityLedger.MaxQuantity</c> on every repaint, because the balance and the room
        /// both move under an open panel — a stepper left reading twelve over a button that will
        /// only sell two is the panel lying about the one thing it exists to be exact about.
        /// </summary>
        int _quantity = 1;

        protected override void Build()
        {
            if (Item == null) { Close(); return; }

            MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Item.NameKey));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Sun, 380f, .16f, new Vector2(0f, 96f + Lift));

            _art = UIKit.Img("A", Panel, Art.S(Item.Art), Color.white, new Vector2(250f, 250f),
                             new Vector2(.5f, .5f), new Vector2(0f, 96f + Lift));
            _art.preserveAspect = true;
            _art.raycastTarget = false;

            // What it does, authored per utility rather than written here: the sentence differs
            // across the catalog, and one that is true of a firepot and false of a mending is
            // how a player stops believing the panel.
            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get(Item.NoteKey), 27, Ink,
                             TextAnchor.MiddleCenter, new Vector2(620f, 96f),
                             new Vector2(.5f, .5f), new Vector2(0f, -108f + Lift),
                             outline: 0f, shadow: 0f, wrap: true), 19);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 29, Ink, TextAnchor.MiddleCenter,
                             new Vector2(640f, 52f), new Vector2(.5f, .5f),
                             new Vector2(0f, -206f + Lift), outline: 0f, shadow: 0f, wrap: true), 20);

            BuildStepper();
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

        // -------------------------------------------------------------- the stepper
        /// <summary>
        /// Minus, the count, plus — and nothing else, because the total is on the button.
        ///
        /// <para>
        /// Built even for a chest-only utility, greyed at both stops rather than absent: a
        /// control that disappears takes the layout with it, and the panel would then be two
        /// different heights for two utilities on one shelf.
        /// </para>
        /// </summary>
        void BuildStepper()
        {
            // Centred in the room it actually has rather than at a typed offset: the status
            // line's foot is at -162 and the buy button's head at -331, so a 96-tall control
            // sits at -246 with about 36 units clear either side.
            const float Y = -246f;

            _less = Step("Less", "−", new Vector2(-214f, Y), -1);
            _more = Step("More", "+", new Vector2(214f, Y), +1);

            _count = UIKit.Titled("Count", Panel, string.Empty, 52, Ink,
                                  TextAnchor.MiddleCenter, new Vector2(300f, 62f),
                                  new Vector2(.5f, .5f), new Vector2(0f, Y),
                                  outline: 0f, shadow: 2f);
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
        /// The upper stop is re-read on every tap rather than cached at build, because both
        /// halves of it move under this panel — gems landing from the stacked shelf raise it, a
        /// chest opened elsewhere lowers it. <c>HomesteadBuyOverlay.Nudge</c>'s rule.
        /// </summary>
        void Nudge(int delta)
        {
            if (_paid) return;

            int most = UtilityLedger.MaxQuantity(Item);
            int wanted = _quantity + delta;

            if (wanted < 1) wanted = 1;
            if (most >= 1 && wanted > most) wanted = most;
            if (wanted == _quantity) return;

            _quantity = wanted;
            Audio.Sfx("click", .5f);
            Repaint();
        }

        void PaintStepper()
        {
            if (_count == null || !_count) return;

            int most = UtilityLedger.MaxQuantity(Item);

            _count.text = "×" + _quantity;

            // Greyed rather than hidden at the stops — a player who has just pressed + four
            // times needs to see why the fifth did nothing.
            if (_less) _less.Interactable = _quantity > 1;
            if (_more) _more.Interactable = _quantity < most;
        }

        // --------------------------------------------------------------- the button
        void BuildAction()
        {
            var size = new Vector2(560f, 122f);
            var anchor = new Vector2(.5f, 0f);
            var at = new Vector2(0f, 108f);

            if (UtilityLedger.WhyNotBuy(Item, _quantity) == UtilityRefusal.Poor)
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

        // The *total*, never the unit price. A control labelled with a price has to charge
        // that price — HomesteadBuyOverlay's rule, and the one complaint every shop with a
        // stepper gets is from somebody who did not know what they were agreeing to.
        string BuyLabel()
            => Loc.Format("ui.utility.buy", Compact.Number(UtilityLedger.Quote(Item, _quantity)));

        void Repaint()
        {
            if (_paid || Item == null || _status == null || !_status) return;

            // The order can stop being affordable, or stop fitting, while the panel is open.
            // Clamped before anything is asked about it, so every line below is about the order
            // the button would actually place.
            int most = UtilityLedger.MaxQuantity(Item);
            if (most >= 1 && _quantity > most) _quantity = most;
            if (_quantity < 1) _quantity = 1;

            var refusal = UtilityLedger.WhyNotBuy(Item, _quantity);

            PaintStepper();

            switch (refusal)
            {
                case UtilityRefusal.Poor:
                    _status.text = Loc.Format("ui.utility.short",
                                              Compact.Number(UtilityLedger.Quote(Item, _quantity)
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
                bought = UtilityLedger.TryBuy(Item, _quantity, out _);
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
