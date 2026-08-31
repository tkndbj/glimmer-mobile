using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one way back onto a lost board that takes money instead of a heart: the offer, the
    /// price, the debit, and keeping the panel honest while a balance moves underneath it.
    ///
    /// <para>
    /// <b>It is named for the offer and not for a panel, because it now has two.</b> It was
    /// <c>DefeatRescueFlow</c> while the defeat screen was the only place a player could be out
    /// of hearts with something at stake; <see cref="RestartGateOverlay"/> is the second, and a
    /// class called after one of its two callers is how the next reader concludes the other one
    /// is doing something different. Nothing about the offer moved with the name: one price, one
    /// amount, one debit, and <see cref="HeartRescueWhere"/> to keep the two funnels apart in
    /// the analytics rather than in the rule.
    /// </para>
    /// <para>
    /// <b>It is a collaborator rather than more of the panel that draws it, for
    /// <see cref="RunContinueFlow"/>'s reason.</b> That panel already carried the reason copy,
    /// the near-miss line, the heart row, the rewarded-video offer and the way back to the map;
    /// a sixth responsibility with its own latch, its own subscription and its own purchase is
    /// how a class becomes the one nobody dares change. The test to apply is the one
    /// <c>RippleRun</c> was split against — <em>could this rule be proved without building the
    /// other five</em> — and the answer here is now yes.
    /// </para>
    /// <para>
    /// <b>The split is by what each half knows.</b> This owns what the offer is, what it costs,
    /// whether it has been paid for and whether the panel needs redrawing. The panel owns where
    /// the button goes and what "back onto the board" means, because only it holds the screen.
    /// Neither reaches into the other: the panel gets a <see cref="Draw"/> and two callbacks,
    /// and that is the whole surface.
    /// </para>
    /// <para>
    /// <b>The offer is decided once, at construction.</b> That is not a detail — it is what
    /// makes the analytics honest without a latch. A defeat panel is rebuilt whenever the gem
    /// balance changes the answer, and an offer recomputed inside <c>Build</c> would count a
    /// second impression every time somebody came back from the gem shelf, which is the
    /// direction that argues for dropping the price. One construction, one impression.
    /// </para>
    /// <para>
    /// It holds the panel, which is a <c>MonoBehaviour</c>, so <c>if (_panel)</c> is Unity's own
    /// lifetime check — the same bargain <see cref="RunContinueFlow"/> makes with its screen.
    /// </para>
    /// </summary>
    public sealed class HeartRescueFlow
    {
        readonly ModalView _panel;
        readonly LevelId _level;
        readonly int _heartsLeft;

        /// <summary>Which panel this is standing on. Labels the analytics and nothing else.</summary>
        readonly HeartRescueWhere _where;

        /// <summary>Redraws the panel around a changed offer. <c>ModalView.Rebuild</c>.</summary>
        readonly Action _redraw;

        /// <summary>Closes the panel and puts the player back on a fresh board.</summary>
        readonly Action _backToTheBoard;

        HeartRescueOffer _offer;

        /// <summary>
        /// True from the moment the price is tapped until the panel closes.
        ///
        /// <para>
        /// It exists because the debit raises <c>PlayerProgression.Changed</c> <em>from inside
        /// itself</em>, so <see cref="OnBalanceChanged"/> runs while <see cref="Buy"/> is still
        /// on the stack — and by then the gems are gone, which reads as "short of gems" and
        /// would redraw the panel into the buy-gems state a frame before it closes.
        /// </para>
        /// </summary>
        bool _buying;

        /// <param name="panel">The defeat panel this belongs to.</param>
        /// <param name="level">The run's level, for the debit's reason and the analytics.</param>
        /// <param name="heartsLeft">What the player holds now, after the loss was charged.</param>
        /// <param name="canRetry">
        /// True when there is still a heart to spend. Then there is no offer at all and nothing
        /// is subscribed to — a player who can already play is never sold a way to play, which
        /// is the rule that keeps a defeat from being an advertisement. Answered by the panel
        /// because a free opening can be retried whatever the wallet says.
        /// </param>
        /// <param name="where">
        /// Which panel is drawing this. It labels the two events and reaches nothing else — a
        /// per-panel price would be the haggling invariant 23a refuses, and the two are met a
        /// minute apart on the same screen.
        /// </param>
        /// <param name="redraw">Redraws the panel. Called only when the offer's state changes.</param>
        /// <param name="backToTheBoard">Closes the panel and puts the player back in play.</param>
        public HeartRescueFlow(ModalView panel, LevelId level, int heartsLeft, bool canRetry,
                               HeartRescueWhere where, Action redraw, Action backToTheBoard)
        {
            _panel = panel;
            _level = level;
            _heartsLeft = heartsLeft;
            _where = where;
            _redraw = redraw;
            _backToTheBoard = backToTheBoard;

            if (canRetry) { _offer = HeartRescueOffer.None; return; }

            _offer = Read();
            if (!_offer.Exists) return;

            // Subscribed only when there is something to keep honest. A panel with no offer on
            // it has nothing a balance change could alter, and an event handler that can only
            // ever decide to do nothing is a handler somebody will later make do something.
            PlayerProgression.Changed += OnBalanceChanged;

            LevelAnalytics.TrackHeartRescueOffered(_level, _offer, _where);
        }

        /// <summary>What is being sold, or <c>None</c>. The panel sizes itself against this.</summary>
        public HeartRescueOffer Offer => _offer;

        /// <summary>True when there is a button worth drawing.</summary>
        public bool Exists => _offer.Exists;

        /// <summary>Lets go of the balance. The panel calls it from <c>OnDestroy</c>.</summary>
        public void Dispose() => PlayerProgression.Changed -= OnBalanceChanged;

        /// <summary>
        /// Whether a store is reachable that could sell gems right now.
        ///
        /// <c>RunContinueFlow</c>'s reading, and it has to be the same one: the two offers are
        /// met on one screen a minute apart, and a build where one of them can send somebody to
        /// a shop and the other cannot is a build where the answer depends on which panel asked.
        /// </summary>
        static bool GemsForSale => StoreService.IsAvailable && StoreRules.Catalog.HasGems;

        HeartRescueOffer Read() => HeartRescue.Offer(_heartsLeft, Profile.Gems, GemsForSale);

        // ------------------------------------------------------------------ the button
        /// <summary>
        /// Draws the offer as one button doing two jobs, which always does what its own label
        /// says.
        ///
        /// <para>
        /// <b>It is not a continue and must not read as one.</b> A continue sells the run where
        /// it stood; this sells a heart, so the board is rebuilt and the attempt is a fresh one
        /// graded like any other (<c>HeartRescue</c>). The label therefore names hearts rather
        /// than the board, and leads with what arrives — <c>ShopSupplyOverlay</c>'s order,
        /// because a control that opens with a price reads as a demand.
        /// </para>
        /// <para>
        /// There is no third, disabled state: an offer that cannot be met at all is never
        /// drawn, because a control that can never work is worse than no control — and on this
        /// panel of all panels, where the player has just been told they cannot play.
        /// </para>
        /// </summary>
        public void Draw(Transform parent, Vector2 size, Vector2 anchor, Vector2 pos)
        {
            if (!_offer.Exists) return;

            bool buying = _offer.Choice == GemChoice.BuyGems;

            var button = UIKit.TextButton(
                "Rescue", parent, "btn_violet",
                buying ? Loc.Get("ui.defeat.get_gems")
                       : Loc.Format("ui.defeat.buy_hearts", _offer.Hearts,
                                    Compact.Number(_offer.Gems)),
                40, size, anchor, pos, buying ? (Action)OpenGems : Buy,
                buying ? "ic_gem" : "ic_heart");

            UIKit.Shrinkable(button.Label, 22);
            UIKit.FitLabel(button);
        }

        // ------------------------------------------------------------------ the purchase
        /// <summary>
        /// Takes the gems, and goes straight back onto the board.
        ///
        /// <para>
        /// Straight back in rather than to a panel that has quietly grown a retry button, which
        /// is what the rewarded video already decided and is the stronger argument here:
        /// somebody who has just paid for another go has said what they want, and making them
        /// find one more button is a tax on the thing they paid for.
        /// </para>
        /// <para>
        /// The debit is asked for again rather than trusted from the state the panel was built
        /// in, and that is reachable rather than defensive — a sync landing between the build
        /// and the tap is the ordinary case. <c>HeartRescue.TryBuy</c> decides against the
        /// balance at the instant of the charge, which is the only instant that means anything.
        /// </para>
        /// </summary>
        void Buy()
        {
            if (_buying || !_panel) return;
            _buying = true;

            if (!HeartRescue.TryBuy(_offer, _level, _where))
            {
                _buying = false;

                // Reachable exactly as it is on the shop's own supply panel: another device
                // spent, or a server sync revised the balance down. Redraw rather than close —
                // the offer is still good, the player simply cannot meet it yet, and the panel
                // already knows how to say that.
                Audio.SfxVaried("back", .5f);
                Refresh();
                return;
            }

            // A sound and no haptic, the rule the victory panel and the shop both arrived at:
            // Handheld.Vibrate is one fixed-length buzz on Android, so a small purchase cannot
            // be made to feel lighter than a big one.
            Audio.Sfx("coin", .6f);

            _backToTheBoard?.Invoke();
        }

        /// <summary>
        /// Brings the gem shelf to the player instead of taking the player to it.
        ///
        /// <para>
        /// Stacked on top of the defeat panel rather than replacing it, so the thing they are
        /// buying gems <em>for</em> is still there when they come back — and so that a purchase
        /// which takes the app into the background for a minute disturbs nothing about the run
        /// that has just been written down. See <see cref="GemShopOverlay"/>.
        /// </para>
        /// <para>
        /// Raising it twice is impossible rather than merely unlikely: <c>Flow.Modal</c> hands
        /// back the shelf that is already up rather than building a second one, so a double tap
        /// is one panel with one <c>Bought</c> callback.
        /// </para>
        /// </summary>
        void OpenGems()
        {
            if (_buying || !_panel) return;

            Flow.Modal<GemShopOverlay>(v => v.Bought = Refresh);
        }

        // ------------------------------------------------------------------ keeping it honest
        void OnBalanceChanged() => Refresh();

        /// <summary>
        /// Redraws the panel when what the player can do about the price has changed, and
        /// leaves it alone otherwise.
        ///
        /// <para>
        /// The decision is <c>HeartRescue.WorthRedrawing</c> rather than a chain of conditions
        /// here, because it is a rule about two offers and this is a place nothing can prove
        /// one. What stays is the plumbing it cannot see: a panel that has been destroyed, and
        /// a debit that is still on the stack.
        /// </para>
        /// <para>
        /// A full rebuild rather than a label write, because the panel is a different height
        /// with the button in a different state — and this project has already recorded what
        /// maintaining two sets of coordinates costs.
        /// </para>
        /// </summary>
        void Refresh()
        {
            if (!_panel || _buying) return;

            var now = Read();
            if (!HeartRescue.WorthRedrawing(_offer, now)) return;

            _offer = now;
            _redraw?.Invoke();

            if (now.Choice == GemChoice.Spend) Audio.Sfx("star", .55f, 1.15f);
        }
    }
}
