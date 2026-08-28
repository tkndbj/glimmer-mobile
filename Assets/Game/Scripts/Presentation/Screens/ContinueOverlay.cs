using System;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Persistence;
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

        // A "restart level" button lived here for one revision and was taken out again. It
        // charged the same heart declining charges, wrote the same record, and differed only in
        // skipping the panel that follows — whose own first button then did exactly what it had
        // just done. Two buttons with one outcome is the confusion this panel was being fixed
        // for, one layer further down. The panel asks whether to pay; what to do instead is the
        // defeat panel's whole job.

        // ------------------------------------------------------------------ geometry
        // A cursor walking down the panel rather than absolute offsets, because the panel now
        // has four heights: the short-of-gems line and the restart button are each optional and
        // they combine. Absolute offsets would mean a line drawn through a button on exactly one
        // of the branches — which is the failure AdOfferOverlay's layout was rewritten to avoid.
        //
        // The numbers themselves live in ContinuePanel, in Domain, with the fit check that
        // refused the first version of the banner for being ten units off the top of a 4:3
        // canvas. See there.
        const float PanelW = ContinuePanel.Width;
        const float ContentW = ContinuePanel.ContentWidth;
        const float HeadRoom = ContinuePanel.HeadRoom;
        const float OfferH = ContinuePanel.OfferH;
        const float ShortH = ContinuePanel.ShortH;
        const float ButtonH = ContinuePanel.ButtonH;
        const float GiveUpH = ContinuePanel.GiveUpH;

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
            float offerY = y + OfferH * .5f;     y += OfferH + ContinuePanel.OfferGap;

            float shortY = 0f;
            if (buying) { shortY = y + ShortH * .5f; y += ShortH + ContinuePanel.ShortGap; }

            float buyY = y + ButtonH * .5f;      y += ButtonH + ContinuePanel.ButtonGap;
            float giveY = y + GiveUpH * .5f;     y += GiveUpH + ContinuePanel.FootRoom;

            // Never dismissed by a stray tap on the scrim. The same judgement DefeatOverlay
            // makes and for a sharper reason: an accidental dismissal here does not close a
            // panel, it ends the run.
            MakePanel(new Vector2(PanelW, y), Loc.Get("ui.continue.title"), dismissOnScrim: false);

            // Held back until the word above has landed and risen. Sequencing them is what makes
            // the two read as one sentence — this happened, so: this question — and it is the
            // whole reason the banner is worth animating at all.
            var entrance = Panel.gameObject.AddComponent<CanvasGroup>();
            entrance.alpha = 0f;

            Tween.Run(ContinuePanel.PanelEnter, Ease.OutCubic, t =>
            {
                if (entrance) entrance.alpha = t;
            }, entrance).Delay(ContinuePanel.PanelDelay)
               .OnAbandon(() => { if (entrance) entrance.alpha = 1f; });

            // The word first, so nobody has to infer from a price that they have lost. It was
            // reported exactly that way — a panel offering gems, read as the cost of *finishing*
            // the level rather than of undoing a defeat. The panel below it asks a question; this
            // says what happened, and it is the one thing on screen that is not a choice.
            BuildBanner(y);

            // One sentence with the number in it, set large enough to be read at the moment
            // somebody has just lost — which is not a moment anybody spends on a paragraph. It
            // is built from the offer rather than written into the copy, so a retune of what a
            // continue hands over cannot leave this saying something else.
            UIKit.Shrinkable(
                UIKit.Titled("Offer", Panel,
                             Loc.Format("ui.continue.offer", Offer.Amount,
                                        Loc.Get(UnitKey(Offer.Unit))),
                             38, new Color(.30f, .21f, .15f), TextAnchor.UpperCenter,
                             new Vector2(ContentW, OfferH), new Vector2(.5f, 1f),
                             new Vector2(0f, -offerY), outline: 0f, shadow: 0f, wrap: true), 26);

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

        /// <summary>
        /// The word, and the one animation on this panel.
        ///
        /// <para>
        /// It arrives at the middle of the screen — where the eye already is, because that is
        /// where the board was — holds for a beat, and glides up to sit above the panel that is
        /// arriving underneath it. Landing it in place immediately was tried and reads as a
        /// header; travelling from where the run ended to where the question is being asked is
        /// what makes the two feel like one sentence rather than two panels.
        /// </para>
        /// <para>
        /// Where it comes to rest is <c>ContinuePanel.BannerCentre</c>'s, which counts the
        /// panel's half-height, the ribbon standing proud of it and the gap — a modal is
        /// centred, so all three are above the middle and all three have to be paid for.
        /// </para>
        /// </summary>
        void BuildBanner(float panelHeight)
        {
            float rest = ContinuePanel.BannerCentre(panelHeight);

            var word = UIKit.Titled("Defeat", Content, Loc.Get("ui.continue.defeat"), 84,
                                    Pal.Rose, TextAnchor.MiddleCenter,
                                    new Vector2(PanelW, ContinuePanel.BannerHeight),
                                    new Vector2(.5f, .5f), Vector2.zero, 9f, 8f);
            UIKit.Shrinkable(word, 44);
            word.raycastTarget = false;

            var rt = (RectTransform)word.transform;

            // Under the reason, so the player is told what happened *and* why in one glance —
            // and the why is the mode's own word for it, not a generic one.
            var why = UIKit.Titled("Why", Content, Loc.Get(ReasonKey(Offer.Unit)), 30,
                                   Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter,
                                   new Vector2(PanelW, 40f), new Vector2(.5f, .5f),
                                   new Vector2(0f, -ContinuePanel.BannerHeight * .5f - 4f),
                                   4f, 3f);
            UIKit.Shrinkable(why, 20);
            why.raycastTarget = false;
            why.transform.SetParent(rt, false);

            Tween.Run(ContinuePanel.BannerPop, Ease.OutBack, t =>
            {
                if (!word) return;
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(.4f, 1f, t);
            }, word);

            // Held where it landed before it moves, and taken up unhurriedly. Both were about a
            // third quicker to begin with and were reported as too fast to register.
            Tween.Run(ContinuePanel.BannerRise, Ease.InOutCubic, t =>
            {
                if (!word) return;
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, rest, t));
            }, word).Delay(ContinuePanel.BannerPop + ContinuePanel.BannerHold)
               .OnAbandon(() => { if (word) rt.anchoredPosition = new Vector2(0f, rest); });

            // No sound. Breaking glass over a lost run is a punishment noise, and this panel is
            // an offer — it was the one thing on screen saying "you have been told off" while
            // everything else was asking a question.
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
                Rebuild();
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

            Flow.Modal<GemShopOverlay>(v => v.Bought = Rebuild);
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

        void OnBalanceChanged() => Rebuild();

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
        /// <summary>
        /// Why the run ended, in the unit the mode is measured in.
        ///
        /// This was the panel's <em>title</em> until the word above it took that job. It says the
        /// same thing and now says it under "DEFEAT", where it reads as the reason rather than as
        /// the subject.
        /// </summary>
        static string ReasonKey(ContinueUnit unit)
        {
            switch (unit)
            {
                case ContinueUnit.Ink: return "ui.continue.ink_title";
                case ContinueUnit.Motes: return "ui.continue.motes_title";
                case ContinueUnit.Tiles: return "ui.continue.tiles_title";
                default: return "ui.continue.turns_title";
            }
        }

        static string UnitKey(ContinueUnit unit)
        {
            switch (unit)
            {
                case ContinueUnit.Ink: return "ui.continue.ink_unit";
                case ContinueUnit.Motes: return "ui.continue.motes_unit";
                case ContinueUnit.Tiles: return "ui.continue.tiles_unit";
                default: return "ui.continue.turns_unit";
            }
        }
    }
}
