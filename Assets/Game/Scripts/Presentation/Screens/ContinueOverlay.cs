using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The offer to carry a lost run on, for a handful of gems.
    ///
    /// <para>
    /// <b>It sits in front of the defeat panel rather than on it.</b> A defeat has already
    /// taken a heart, written the run down and told the player how close they came; by then
    /// the run is over and an offer to continue would be an offer to undo an accounting entry.
    /// So this comes first, and <c>RunContinueFlow</c> is what makes that ordering a
    /// property of every mode rather than a habit each one keeps.
    /// </para>
    /// <para>
    /// <b>The short-of-gems branch never navigates, and that is the whole reason this panel is
    /// not simply a re-skin of <c>ShopSupplyOverlay</c>.</b> Every other short balance in this
    /// game opens the shop, which is right everywhere else and catastrophic here: the screen
    /// underneath is a run in progress, and leaving it forfeits a heart — so a player who
    /// tapped "get gems" to <em>save</em> their run would lose it on the way to paying for it.
    /// The gems are brought to the player instead (<see cref="GemShopOverlay"/>), stacked on
    /// top of this panel, and when they land this one is still standing with the price now
    /// affordable.
    /// </para>
    /// <para>
    /// <b>Exactly one of <see cref="Bought"/> and <see cref="Declined"/> fires, always</b>, for
    /// every way this panel can end — the two buttons, the hardware back key, and the screen
    /// underneath being destroyed while it is open. It is reported from <c>OnDestroy</c> for
    /// <c>AdOfferOverlay</c>'s reason, and it matters more here than it did there: the run
    /// behind this is frozen mid-defeat with its heart uncharged and its record unwritten, so
    /// a caller that never hears leaves the player on a dead board with no way forward and no
    /// way to lose either.
    /// </para>
    /// </summary>
    public sealed class ContinueOverlay : ModalView
    {
        /// <summary>
        /// What is being sold. Set by the caller's configure callback before <c>Init</c> runs,
        /// which is why it is a property rather than a field — the shape <c>WinOverlay.Run</c>
        /// uses, and what keeps Unity's serialization analyser off a type never meant to
        /// survive a domain reload.
        /// </summary>
        public ContinueOffer Offer { get; set; }

        /// <summary>The run's level, for the debit's reason string and the analytics event.</summary>
        public LevelId Level { get; set; }

        /// <summary>
        /// The gems were taken. Carries the allowance to hand over, which is the offer's
        /// figure rather than the table's — see <see cref="ContinueOffer.Amount"/>.
        /// </summary>
        public Action<int> Bought;

        /// <summary>The offer was turned down, however that happened. The run is now lost.</summary>
        public Action Declined;

        // ------------------------------------------------------------------ geometry
        // A cursor walking down the panel rather than absolute offsets, because the panel has
        // two heights: the short-of-gems state carries a line the affordable one does not.
        // Absolute offsets would mean that line drawn through a button on exactly one of the
        // two branches — which is the failure AdOfferOverlay's layout was rewritten to avoid.
        const float PanelW = 880f;
        const float ContentW = 700f;
        const float HeadRoom = 150f;
        const float AmountH = 190f;
        const float NoteH = 80f;
        const float HeldH = 44f;
        const float ShortH = 58f;
        const float ButtonH = 148f;
        const float GiveUpH = 96f;
        const float FootRoom = 46f;

        /// <summary>What the offer's price can be met with right now. Re-read on every repaint.</summary>
        GemChoice _choice;

        bool _reported, _paid;

        /// <summary>
        /// True from the moment the spend button is pressed until the panel closes.
        ///
        /// <para>
        /// It exists because the debit raises <c>PlayerProgression.Changed</c> <em>from inside
        /// itself</em>, so the balance handler runs while <see cref="Spend"/> is still on the
        /// stack — and by then the gems are gone, which reads as "short of gems" and would
        /// rebuild the panel into the buy-gems state a frame before it closes. Guarding the
        /// repaint is cheaper and clearer than making the debit quiet.
        /// </para>
        /// </summary>
        bool _spending;

        protected override void Build()
        {
            // An offer that has evaporated between being built and being drawn — a content
            // push withdrawing the feature, a balance that moved — is not a panel worth
            // showing. Closing reports a decline, which loses the run exactly as it would have
            // been lost had the offer never been made.
            if (!Offer.Exists) { Flow.Dismiss(this); Report(false); return; }

            _choice = Choice();

            bool buying = _choice == GemChoice.BuyGems;

            // Every row's offset is the centre of its slot, because UIKit.Box always pivots
            // centre whatever it is anchored to. The note alone used to be placed at the top
            // of its slot, which drew it half its own height too high — straight through the
            // "more turns" line above it. The house rule the map nodes and the weave band
            // both landed on: whether two things overlap is arithmetic, so do the arithmetic.
            float y = HeadRoom;
            float amountY = y + AmountH * .5f;   y += AmountH + 18f;
            float noteY = y + NoteH * .5f;       y += NoteH + 10f;
            float heldY = y + HeldH * .5f;       y += HeldH + 12f;

            float shortY = 0f;
            if (buying) { shortY = y + ShortH * .5f; y += ShortH + 8f; }

            float buyY = y + ButtonH * .5f;      y += ButtonH + 16f;
            float giveY = y + GiveUpH * .5f;     y += GiveUpH + FootRoom;

            // Never dismissed by a stray tap on the scrim. The same judgement DefeatOverlay
            // makes and for a sharper reason: an accidental dismissal here does not close a
            // panel, it ends the run.
            MakePanel(new Vector2(PanelW, y), Loc.Get(TitleKey(Offer.Unit)), dismissOnScrim: false);

            BuildAmount(amountY);

            // What the purchase does, in the player's terms and read from the offer rather
            // than written into the copy. The rule StreakInfoOverlay and AdOfferOverlay were
            // both rebuilt around: a panel explaining the game is the first thing to rot when
            // the game is retuned.
            UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get(NoteKey(Offer.Unit)), 30,
                             new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                             new Vector2(ContentW, NoteH), new Vector2(.5f, 1f),
                             new Vector2(0f, -noteY), outline: 0f, shadow: 0f, wrap: true), 20);

            UIKit.Shrinkable(
                UIKit.Titled("Held", Panel, Loc.Format("ui.continue.held", Compact.Number(Profile.Gems)),
                             26, new Color(.36f, .25f, .18f, .74f), TextAnchor.MiddleCenter,
                             new Vector2(ContentW, HeldH), new Vector2(.5f, 1f),
                             new Vector2(0f, -heldY), outline: 0f, shadow: 0f), 17);

            if (buying)
                UIKit.Shrinkable(
                    UIKit.Titled("Short", Panel, Loc.Format("ui.continue.short",
                                                            Compact.Number(Offer.Gems)),
                                 30, Pal.Ember, TextAnchor.MiddleCenter,
                                 new Vector2(ContentW, ShortH), new Vector2(.5f, 1f),
                                 new Vector2(0f, -shortY), outline: 0f, shadow: 0f), 19);

            // One button, two jobs, and it always does what its own label says. Asking about
            // the blocking condition before the price is the house rule HintPrompt follows;
            // what is different here is that the answer stays on this screen.
            var buy = UIKit.TextButton("Buy", Panel, "btn_violet",
                                       buying ? Loc.Get("ui.continue.get_gems")
                                              : Loc.Format("ui.shop.gem_price",
                                                           Compact.Number(Offer.Gems)),
                                       42, new Vector2(600f, ButtonH), new Vector2(.5f, 1f),
                                       new Vector2(0f, -buyY),
                                       buying ? (Action)OpenGems : Spend, "ic_gem");
            UIKit.Shrinkable(buy.Label, 24);
            UIKit.FitLabel(buy);

            // A whole button rather than a corner cross, which is where this parts company
            // with AdOfferOverlay. There, declining costs nothing and a button spent on it
            // reads as a panel expecting to be declined. Here declining ends the run and takes
            // a heart, so it is a decision the player is entitled to make deliberately — and a
            // panel whose only visible exit is the one that charges them is the shape a store
            // reviewer is right to call a dark pattern.
            var give = UIKit.TextButton("GiveUp", Panel, "btn_red",
                                        Loc.Get("ui.continue.give_up"), 32,
                                        new Vector2(420f, GiveUpH), new Vector2(.5f, 1f),
                                        new Vector2(0f, -giveY), Decline);
            UIKit.Shrinkable(give.Label, 20);

            // Unsubscribed first, because Rebuild runs Build again on the same component and
            // a second subscription would repaint twice for every balance change — and then
            // four times, and then eight.
            PlayerProgression.Changed -= OnBalanceChanged;
            PlayerProgression.Changed += OnBalanceChanged;
        }

        void OnDestroy()
        {
            PlayerProgression.Changed -= OnBalanceChanged;

            // The backstop, not the normal path. Reporting from here as well as from the two
            // buttons means the caller hears exactly once however the panel ended, including
            // the ending no button knows about: this screen being torn down with the offer
            // still standing.
            Report(_paid);
        }

        // ------------------------------------------------------------------ the amount
        /// <summary>
        /// What is being bought, said as a number, because it is the one thing on the panel
        /// the player is actually deciding about.
        ///
        /// <para>
        /// Drawn from generated art rather than an address, the house rule for anything a
        /// ceremonial screen cannot afford to be missing: an <c>Image</c> whose sprite has not
        /// arrived is a white rectangle, and this one sits behind the figure a purchase is
        /// being made against.
        /// </para>
        /// </summary>
        void BuildAmount(float y)
        {
            var box = UIKit.Box("Amount", Panel, new Vector2(ContentW, AmountH),
                                new Vector2(.5f, 1f), new Vector2(0f, -y));

            var glow = UIKit.Img("Glow", box, Art.Glow(160, 2.2f), Pal.A(Pal.Gold, .26f),
                                 new Vector2(AmountH * 2.1f, AmountH * 1.5f),
                                 new Vector2(.5f, .5f), Vector2.zero);
            glow.raycastTarget = false;

            UIKit.Shrinkable(
                UIKit.Titled("Figure", box, "+" + Offer.Amount, 96, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(ContentW, 118f),
                             new Vector2(.5f, 1f), new Vector2(0f, -18f), 5f, 5f), 48);

            UIKit.Shrinkable(
                UIKit.Titled("Unit", box, Loc.Get(UnitKey(Offer.Unit)), 34, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(ContentW, 46f),
                             new Vector2(.5f, 1f), new Vector2(0f, -134f), 3f, 3f), 22);
        }

        // ------------------------------------------------------------------ the two answers
        /// <summary>
        /// Takes the gems and hands the allowance back.
        ///
        /// <para>
        /// The debit is asked for again here rather than trusted from the state the panel was
        /// built in, and that is reachable rather than defensive — the balance moves while a
        /// panel is open, and a sync landing between the build and the tap is the ordinary
        /// case. <c>RunContinue.TryBuy</c> decides against the balance at the instant of the
        /// charge, which is the only instant that means anything.
        /// </para>
        /// </summary>
        void Spend()
        {
            if (_reported || _spending) return;
            _spending = true;

            if (!RunContinue.TryBuy(Offer, Level))
            {
                _spending = false;
                // Reachable exactly as it is on the shop's own supply panel: another device
                // spent, or a server sync revised the balance down. Repaint rather than close
                // — the offer is still good, the player simply cannot meet it yet, and this
                // panel already knows how to say that.
                Audio.SfxVaried("back", .5f);
                Repaint();
                return;
            }

            _paid = true;

            // A sound and no haptic, which is the rule the victory panel and the shop both
            // arrived at: Handheld.Vibrate is one fixed-length buzz on Android, so a small
            // purchase cannot be made to feel lighter than a big one.
            Audio.Sfx("coin", .6f);

            // Quiet, because what the player hears next is their board coming back to life and
            // a backing-out whoosh underneath it is one sound too many.
            Close(null, quiet: true);
        }

        void Decline() => Close();

        /// <summary>
        /// Brings the gem shelf to the player instead of taking the player to it.
        ///
        /// <para>
        /// Stacked on top of this panel rather than replacing it, so the thing they are buying
        /// gems <em>for</em> is still there when they come back — and so that nothing about
        /// this run's frozen board is disturbed by a purchase that may take the app into the
        /// background for a minute. See <see cref="GemShopOverlay"/>.
        /// </para>
        /// </summary>
        void OpenGems()
        {
            if (_reported) return;

            Flow.Modal<GemShopOverlay>(v => v.Bought = Repaint);
        }

        // ------------------------------------------------------------------ repainting
        /// <summary>
        /// What the price can be met with right now.
        ///
        /// Asked of Domain rather than decided here, so the one branch that governs whether
        /// somebody is shown a purchase or a dead end is pinned by a test. See
        /// <c>GemPrice.ChoiceFor</c>.
        /// </summary>
        GemChoice Choice()
            => GemPrice.ChoiceFor(Profile.Gems, Offer.Gems,
                                     StoreService.IsAvailable && StoreRules.Catalog.HasGems);

        void OnBalanceChanged() => Repaint();

        /// <summary>
        /// Redraws the panel when what the player can do about the price has changed.
        ///
        /// <para>
        /// A full <see cref="ModalView.Rebuild"/> rather than a set of label writes, because
        /// the two states are different heights — the short one carries a line the affordable
        /// one does not — and this project has already recorded what maintaining two sets of
        /// coordinates costs. <c>Rebuild</c> is the redraw that does not replay the entrance,
        /// so the panel does not pop and chime at somebody who just bought gems on top of it.
        /// </para>
        /// <para>
        /// Guarded on the choice rather than on the balance: gems arriving that still do not
        /// cover the price change nothing the player can act on, and a panel that flickers
        /// every time a sync lands is a panel that looks broken.
        /// </para>
        /// </summary>
        void Repaint()
        {
            if (!this || _reported || _spending) return;

            var choice = Choice();
            if (choice == _choice) return;

            // An offer that has become unmeetable — the store went away while the panel was
            // open — is left exactly as it is rather than rebuilt into a dead end. The player
            // still has a way out, and the give-up button is it.
            if (choice == GemChoice.Unavailable) return;

            _choice = choice;
            Rebuild();

            if (choice == GemChoice.Spend) Audio.Sfx("star", .55f, 1.15f);
        }

        // ------------------------------------------------------------------ reporting
        /// <summary>
        /// Tells the caller how this ended, exactly once. See the class remarks for why the
        /// latch is the substance rather than the bookkeeping.
        /// </summary>
        void Report(bool paid)
        {
            if (_reported) return;
            _reported = true;

            var bought = Bought;
            var declined = Declined;
            Bought = null;
            Declined = null;

            // Swallowed, because this can run during teardown: a caller that throws would
            // leave the rest of the destroy chain unrun.
            try
            {
                if (paid) bought?.Invoke(Offer.Amount);
                else declined?.Invoke();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// The hardware key declines rather than doing nothing.
        ///
        /// <para>
        /// Doing nothing was considered and is worse: the scrim is deaf on this panel, so a
        /// back key that also refused would leave the only exits behind two buttons, and a
        /// player who reflexively pressed back would conclude the game had hung. Declining is
        /// what every other panel's back key does, and here it means what the give-up button
        /// means.
        /// </para>
        /// </summary>
        public override bool OnBack()
        {
            Decline();
            return true;
        }

        // ------------------------------------------------------------------ wording
        /// <summary>
        /// Written out per unit rather than built from the enum name, so the build gate's
        /// string scanner can see every key. A concatenated key is invisible to it and ships
        /// missing in whichever language nobody tested — the rule <c>WinOverlay.RankKeys</c>
        /// states and <c>DefeatOverlay</c> follows.
        /// </summary>
        static string TitleKey(ContinueUnit unit)
            => unit == ContinueUnit.Ink ? "ui.continue.ink_title" : "ui.continue.turns_title";

        static string UnitKey(ContinueUnit unit)
            => unit == ContinueUnit.Ink ? "ui.continue.ink_unit" : "ui.continue.turns_unit";

        static string NoteKey(ContinueUnit unit)
            => unit == ContinueUnit.Ink ? "ui.continue.ink_note" : "ui.continue.turns_note";
    }
}
