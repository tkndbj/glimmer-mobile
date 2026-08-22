using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind every "watch a video for this" offer in the game.
    ///
    /// <para>
    /// One overlay for both entry points — the defeat screen's heart refill and the home
    /// screen's coin pill — because they are the same transaction seen from two moods, and
    /// the honest states below are the whole substance of it. Two panels would be two
    /// places to get "no fill" wrong.
    /// </para>
    /// <para>
    /// It is built around saying the true thing. A rewarded ad has five ways of not
    /// happening and a player meets all of them: the network has nothing loaded, the day's
    /// allowance is spent, another ad was watched a minute ago, hearts are already full, or
    /// there is no account to pay coins into yet. A panel that renders those as one greyed
    /// button teaches people the feature is broken, and they stop looking at it — which
    /// costs far more than the ad it failed to show.
    /// </para>
    /// </summary>
    /// <summary>
    /// The caption and enabled state of an offer button, wherever one is drawn.
    ///
    /// Shared by the three entry points so a cooldown reads identically on the defeat
    /// panel, the out-of-hearts gate and the home screen. Three copies of this would be
    /// three chances for one of them to keep saying "WATCH" while the button does nothing.
    /// </summary>
    static class AdOfferButton
    {
        /// <summary>What the button should say, given the placement's current state.</summary>
        public static string Caption(AdOfferStatus status, string readyKey)
        {
            switch (status.State)
            {
                case AdOfferState.Ready:
                    return Loc.Get(readyKey);

                case AdOfferState.CoolingDown:
                    return string.Format(Loc.Get("ui.ads.btn_cooling"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.CapReached:
                    return Loc.Get("ui.ads.btn_cap");

                default:
                    return Loc.Get("ui.ads.btn_loading");
            }
        }

        /// <summary>
        /// Repaints a button in place. Safe to call on a timer — the caption is only
        /// assigned when it actually changed, and the glyph beside it is only re-measured
        /// on the ticks where it really moved.
        /// </summary>
        public static void Paint(Btn button, string placementId, string readyKey)
        {
            if (button == null) return;

            var status = RewardedAds.Status(placementId);

            button.Interactable = status.CanShow;
            button.SetCaption(Caption(status, readyKey));
        }
    }

    /// <summary>
    /// One line of "how this actually works", with the glyph that labels it.
    ///
    /// <para>
    /// The text is a delegate rather than a string because most of these lines move while
    /// the panel is open — a heart countdown ticks, a boost expires, the day's allowance
    /// drops as videos are watched. Building them as live readings means the panel has one
    /// description of each fact rather than one for the first paint and another for the
    /// repaint, which is how a screen ends up telling a player two different things.
    /// </para>
    /// </summary>
    readonly struct AdFact
    {
        public readonly string Icon;
        public readonly Color Tint;
        public readonly Func<string> Line;

        public AdFact(string icon, Color tint, Func<string> line)
        {
            Icon = icon;
            Tint = tint;
            Line = line;
        }
    }

    public sealed class AdOfferOverlay : ModalView
    {
        /// <summary>Which placement is being offered. Set by the caller before Build runs.</summary>
        public string PlacementId = AdPlacement.HeartRefill;

        /// <summary>Raised after a reward actually lands, so the screen behind can repaint.</summary>
        public Action Rewarded;

        /// <summary>
        /// Raised when the panel goes away having paid nothing.
        ///
        /// <para>
        /// Exactly one of this and <see cref="Rewarded"/> fires, always, for every way this
        /// panel can end — the watch button, the decline button, the corner cross, a tap on
        /// the scrim, the hardware back key, and the screen underneath being destroyed while
        /// the panel is open. That completeness is the whole point of it, and it is why it is
        /// raised from <c>OnDestroy</c> rather than from the three or four exits: this panel
        /// has more ways out than any other in the game, and <c>CompanionRevealOverlay</c>
        /// already learned here that a callback wired to the buttons is a callback that does
        /// not fire when somebody dismisses with the cross.
        /// </para>
        /// <para>
        /// Most callers do not need it. <see cref="AdPlacement.RunContinue"/> does, and needs
        /// it absolutely: the run behind the panel is frozen mid-defeat, so a dismissal that
        /// reported nothing would leave a player on a dead board with a spent clock and no
        /// way forward.
        /// </para>
        /// </summary>
        public Action Dismissed;

        // ------------------------------------------------------------- geometry
        // Laid out by a cursor walking down the panel rather than by absolute offsets,
        // because the fact list is not a fixed length: a placement the content table does
        // not carry loses its allowance line, and the two placements do not explain
        // themselves in the same number of sentences. Absolute offsets would mean a fourth
        // fact silently drawn over the button — the failure this file's sibling Cue was
        // rewritten to avoid.
        const float PanelW = 880f;
        const float ContentW = 700f;
        const float HeadRoom = 148f;      // under the ribbon
        const float CardSize = 264f;
        const float FactStep = 76f;
        const float FactIcon = 48f;
        const float FactGap = 18f;
        const float StatusH = 92f;
        const float ButtonH = 148f;
        const float DeclineH = 108f;
        const float ShopH = 96f;
        const float FootRoom = 44f;

        Btn _watch;
        Text _status;
        Text _reward;
        RectTransform _card;
        AdFact[] _facts;
        Text[] _factLines;
        bool _watching;
        bool _paid;

        /// <summary>
        /// Whether this panel offers a way through to the shop.
        ///
        /// <para>
        /// Only the two placements a player can safely walk away from. The run continue's
        /// board is frozen mid-defeat, so leaving forfeits the run; the win bonus sits inside
        /// the victory sequence, and a shop button there would take somebody off the panel
        /// that is still telling them how the run went.
        /// </para>
        /// <para>
        /// It also needs the shop to actually work. <c>StoreService.IsAvailable</c> is false
        /// in a build with no store SDK and in every Editor session, and a button leading to
        /// a screen with nothing on it is worse than no button — the same judgement the watch
        /// button makes about a placement the content table does not carry.
        /// </para>
        /// </summary>
        bool ShowsShop
            => StoreService.IsAvailable &&
               (PlacementId == AdPlacement.HeartRefill || PlacementId == AdPlacement.CoinBonus);

        protected override void Build()
        {
            var offer = RewardedAds.Table.Offer(PlacementId);
            _facts = Facts(PlacementId, offer);

            // Height first, because MakePanel needs it before anything can be placed in it.
            float y = HeadRoom;
            float cardY = y + CardSize * .5f;   y += CardSize + 30f;
            float factsY = y;                   y += _facts.Length * FactStep + 14f;
            float statusY = y + StatusH * .5f;  y += StatusH + 12f;
            float buttonY = y + ButtonH * .5f;  y += ButtonH;

            // A spelled-out way to decline, on the one placement where declining is not
            // "not now". Everywhere else the corner cross is right and a whole button spent
            // on refusing reads as a panel that expects to be refused — but here the cross
            // ends a run, and an unlabelled cross that ends a run is exactly the ambiguity
            // ForfeitOverlay exists to remove. So the continue says what closing costs.
            bool decline = PlacementId == AdPlacement.RunContinue;

            float declineY = 0f;
            if (decline) { y += 14f; declineY = y + DeclineH * .5f; y += DeclineH; }

            // A quiet way through to the shop, on the two placements opened from somewhere
            // a player can leave. Not on the run continue — the board behind it is frozen
            // mid-defeat and navigating away would forfeit the run — and not on the win
            // bonus, which sits inside the victory sequence.
            //
            // It is here rather than on the hub because this panel is already the answer to
            // "how do I get more of this", and a resource panel that lists every way except
            // the one that works instantly is a panel being coy. A slim button under the
            // watch button, in the gem colour, so it reads as the other kind of answer.
            bool shop = ShowsShop;

            float shopY = 0f;
            if (shop) { y += 12f; shopY = y + ShopH * .5f; y += ShopH; }

            y += FootRoom;

            // Never dismissed by a stray tap on the scrim when the run behind it is frozen
            // and closing forfeits it. The same judgement DefeatOverlay makes, for the same
            // reason: an accidental dismissal here costs the player the run.
            MakePanel(new Vector2(PanelW, y), Loc.Get(TitleKey(PlacementId)),
                      dismissOnScrim: !decline);

            BuildRewardCard(offer, cardY);
            BuildFacts(factsY);

            _status = UIKit.Titled("Status", Panel, string.Empty, 30,
                                   new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                                   new Vector2(ContentW, StatusH), new Vector2(.5f, 1f),
                                   new Vector2(0f, -statusY), outline: 0f, shadow: 0f, wrap: true);
            UIKit.Shrinkable(_status, 22);

            // No watch button at all when the content table does not carry this placement.
            // It is the one refusal on this panel that cannot resolve by waiting, and a
            // green button that can never work is worse than no button — the facts above
            // are still worth the player's trip, so the panel closes rather than teasing.
            if (offer.IsValid)
                _watch = UIKit.TextButton("Watch", Panel, "btn_green", Loc.Get(WatchKey(PlacementId)), 46,
                                          new Vector2(600f, ButtonH), new Vector2(.5f, 1f),
                                          new Vector2(0f, -buttonY), OnWatch, "ic_play");
            else
                UIKit.TextButton("Ok", Panel, "btn_blue", Loc.Get("ui.common.got_it"), 46,
                                 new Vector2(600f, ButtonH), new Vector2(.5f, 1f),
                                 new Vector2(0f, -buttonY), () => Close());

            if (decline)
                UIKit.TextButton("Decline", Panel, "btn_gray", Loc.Get("ui.ads.continue_decline"), 38,
                                 new Vector2(600f, DeclineH), new Vector2(.5f, 1f),
                                 new Vector2(0f, -declineY), () => Close());

            if (shop)
            {
                var button = UIKit.TextButton("Shop", Panel, "btn_violet", Loc.Get("ui.ads.to_shop"), 32,
                                              new Vector2(600f, ShopH), new Vector2(.5f, 1f),
                                              new Vector2(0f, -shopY),
                                              () => Close(() => Flow.Go<ShopScreen>()), "ic_gem");
                UIKit.Shrinkable(button.Label, 18);
                UIKit.FitLabel(button);
            }

            // A corner cross rather than a second full-width button. Nobody taps "not now",
            // and a whole button spent on the option to decline reads as a panel that
            // expects to be declined.
            //
            // It is not removed altogether, though, and that is deliberate: a modal whose
            // only exit is tapping the scrim is a modal that a fair number of players will
            // experience as being trapped by an advert. That is a dark pattern, it is the
            // kind of thing store reviewers flag, and it earns one-star reviews from people
            // who would otherwise have watched the video tomorrow.
            UIKit.IconButton("Dismiss", Panel, Skins.Nav, "ic_close", new Vector2(84f, 84f),
                             new Vector2(1f, 1f), new Vector2(-58f, -58f), () => Close());

            RewardedAds.Changed += Repaint;
            Repaint();
        }

        void OnDestroy()
        {
            RewardedAds.Changed -= Repaint;

            // The backstop, not the normal path. Reporting from here as well as from the
            // close means the caller hears exactly once however the panel ended — including
            // the two endings no button knows about: the hardware back key pressed during the
            // reward beat, and the screen underneath being torn down with this still open.
            Report(_paid);
        }

        bool _reported;

        /// <summary>
        /// Tells the caller how this ended, exactly once.
        ///
        /// <para>
        /// The latch is the substance. This panel has six exits — watch, decline, the corner
        /// cross, the scrim, the back key and the screen dying underneath it — and a caller
        /// that hears twice is as broken as one that never hears: <c>PlayScreen</c> would
        /// extend a clock it had already lost the run on. Reporting through one method with
        /// one guard is what makes "exactly one of Rewarded and Dismissed fires, always"
        /// something the type enforces rather than something six call sites agree about.
        /// </para>
        /// <para>
        /// <paramref name="paid"/> is read from <see cref="_paid"/> rather than from how the
        /// panel closed, so a player who backs out during the reward beat still gets what the
        /// video bought them. <c>RewardedAds.Redeem</c> has already banked it by then; the
        /// callback is only how the screen finds out.
        /// </para>
        /// </summary>
        void Report(bool paid)
        {
            if (_reported) return;
            _reported = true;

            var callback = paid ? Rewarded : Dismissed;
            Rewarded = null;
            Dismissed = null;

            // Swallowed, because this can run during teardown: a caller that throws would
            // leave the rest of the destroy chain unrun, and one of the things it unruns is
            // the event unsubscription above.
            try { callback?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Written out rather than built from the placement id, so the loc gate can see
        /// every key. A concatenated key is invisible to the scanner and ships missing.
        /// </summary>
        static string TitleKey(string placementId)
            => placementId == AdPlacement.CoinBonus ? "ui.ads.coins_title"
             : placementId == AdPlacement.RunContinue ? "ui.ads.continue_title"
             : placementId == AdPlacement.WinBonus ? "ui.ads.bonus_title"
             : placementId == AdPlacement.HintRefill
                 ? (Wallet.Hints.CanSpend ? "ui.ads.hints_title" : "ui.ads.hints_empty_title")
             : "ui.ads.hearts_title";

        /// <summary>
        /// What the watch button says when it can be pressed, written out for
        /// <see cref="TitleKey"/>'s reason.
        ///
        /// The continue gets its own verb because the transaction is not the same one. Every
        /// other placement here adds to a balance the player will spend later; this one
        /// decides whether the run they are in the middle of is over, and a button reading
        /// "watch" beside a lost board does not say that.
        /// </summary>
        static string WatchKey(string placementId)
            => placementId == AdPlacement.RunContinue ? "ui.ads.continue_cta"
             : placementId == AdPlacement.HintRefill ? "ui.ads.hints_cta"
             : "ui.ads.watch";

        /// <summary>
        /// What this placement pays into, when the table has no offer to describe. The card
        /// still shows the player's own resource rather than falling back to coins on a
        /// hearts panel, which would be a small lie told at the worst moment.
        /// </summary>
        static ChestDropKind ResourceOf(string placementId)
            => placementId == AdPlacement.CoinBonus || placementId == AdPlacement.WinBonus
                 ? ChestDropKind.Credits
             : placementId == AdPlacement.RunContinue ? ChestDropKind.RunTime
             : placementId == AdPlacement.HintRefill ? ChestDropKind.Hints
             : ChestDropKind.Hearts;

        // ------------------------------------------------------------- the prize
        /// <summary>
        /// What the ad pays, drawn the way a chest draws a reward so the two read as the
        /// same kind of thing — which they are.
        /// </summary>
        void BuildRewardCard(AdOffer offer, float y)
        {
            var kind = offer.IsValid ? offer.Kind : ResourceOf(PlacementId);
            var tint = RewardArt.Tint(kind);

            var card = UIKit.Img("Card", Panel, Art.Round(26), new Color(.04f, .09f, .12f, .82f),
                                 Vector2.one * CardSize, new Vector2(.5f, 1f), new Vector2(0f, -y));
            _card = (RectTransform)card.transform;

            var edge = UIKit.Img("Edge", card.transform, Art.RoundOutline(26, 3f), Pal.A(tint, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Halo(card.transform, tint, CardSize * 1.1f, .30f);

            var icon = UIKit.Img("Icon", card.transform, RewardArt.Icon(kind), Color.white,
                                 new Vector2(114f, 114f), new Vector2(.5f, 1f), new Vector2(0f, -68f));
            icon.preserveAspect = true;
            RewardArt.Glyph(icon, kind, 11f);
            Tween.Breathe(icon.transform, .05f, 2.2f);

            // Through RewardArt so the units come from the same place the chest and the
            // streak board read them from: "+30" beside a clock face promises minutes.
            _reward = UIKit.Titled("Amount", card.transform,
                                   offer.IsValid ? RewardArt.Amount(offer.AsDrop()) : string.Empty, 52, Pal.Cream,
                                   TextAnchor.MiddleCenter, new Vector2(230f, 62f),
                                   new Vector2(.5f, 1f), new Vector2(0f, -166f), outline: 3f, shadow: 3f);

            UIKit.Titled("Kind", card.transform, RewardArt.Name(kind), 28, Pal.A(tint, .95f),
                         TextAnchor.MiddleCenter, new Vector2(230f, 44f),
                         new Vector2(.5f, 1f), new Vector2(0f, -218f), outline: 2f, shadow: 2f);
        }

        // ------------------------------------------------------------- the facts
        /// <summary>
        /// What the player came to find out.
        ///
        /// <para>
        /// The "+" beside a resource is the only question mark on the home screen, and
        /// until now it answered exactly one question — "will you watch a video?" — while
        /// leaving the three a player actually has unanswered: how the resource comes back
        /// on its own, when the next one lands, and whether collecting past a full bar is
        /// worth anything. Those are the facts below, and they are read from
        /// <see cref="HeartRules"/> and the live offer rather than written into the copy,
        /// because a panel that explains the game is the first thing to rot when the game
        /// is retuned. <c>StreakInfoOverlay</c> is built the same way for the same reason.
        /// </para>
        /// </summary>
        AdFact[] Facts(string placementId, AdOffer offer)
        {
            var facts = new List<AdFact>(4);

            if (placementId == AdPlacement.RunContinue)
            {
                // The second line is the one that has to be here, and it is the line a panel
                // selling something would leave out. A player deciding whether thirty seconds
                // is worth a video is entitled to know that the seconds they have already
                // spent still count against their stars — otherwise the first continued run
                // teaches them the offer was a trick, and they never take the second.
                facts.Add(new AdFact("ic_play", Pal.Radiance,
                                     () => Loc.Format("ui.time.adds", offer.IsValid ? offer.Amount : 0)));
                facts.Add(new AdFact("ic_star", Pal.Gold, () => Loc.Get("ui.time.stars_still_run")));
                facts.Add(new AdFact("ic_heart", Pal.Rose, () => Loc.Get("ui.time.keeps_heart")));
            }
            else if (placementId == AdPlacement.WinBonus)
            {
                facts.Add(new AdFact("ic_star", Pal.Gold, () => Loc.Get("ui.bonus.on_top")));
                facts.Add(new AdFact("ic_chest", Pal.Gold, () => Loc.Get("ui.coins.shop_soon")));
            }
            else if (placementId == AdPlacement.CoinBonus)
            {
                facts.Add(new AdFact("ic_star", Pal.Gold, () => Loc.Get("ui.coins.from_glades")));
                facts.Add(new AdFact("ic_chest", Pal.Gold, () => Loc.Get("ui.coins.shop_soon")));
            }
            else if (placementId == AdPlacement.HintRefill)
            {
                // Read from HintRules rather than written into the copy, for the reason the
                // heart facts are. Nothing here says "collected hints stack", which the heart
                // panel's third line does, because they do not — the pool has no headroom
                // above its cap. That is not a fact worth printing, because a player can
                // never meet it: WouldBenefit hides the whole offer at a full pool rather
                // than letting somebody watch a video for a grant that would be refused.
                facts.Add(new AdFact("ic_hint", Pal.Amber,
                                     () => Loc.Format("ui.hints.regen",
                                                      HintRules.RefillSeconds / 3600L,
                                                      HintRules.RefillCap)));
                facts.Add(new AdFact("ic_plus", Pal.Cream, NextHintLine));
                facts.Add(new AdFact("ic_hint", Pal.Gold, () => Loc.Get("ui.hints.one_conduit")));
            }
            else
            {
                facts.Add(new AdFact("ic_heart_boost", Pal.Mint, RegenLine));
                facts.Add(new AdFact("ic_heart", Pal.Rose, NextHeartLine));
                facts.Add(new AdFact("ic_plus", Pal.Gold,
                                     () => Loc.Format("ui.hearts.stack",
                                                      Profile.MaxHearts, Profile.HeartCeiling)));
            }

            // Only when there is something to be allowed. A count of videos left is
            // meaningless for a placement the table does not carry.
            if (offer.IsValid) facts.Add(new AdFact("ic_play", Pal.Cream, AllowanceLine));

            return facts.ToArray();
        }

        /// <summary>When the next hint lands, or that the pool is already full.</summary>
        static string NextHintLine()
            => Profile.HintsRefilled
                ? Loc.Format("ui.hints.full", Profile.MaxHints)
                : Loc.Format("ui.hints.next", Profile.HintCountdown());

        /// <summary>How fast hearts come back, and for how much longer if that is boosted.</summary>
        static string RegenLine()
        {
            long boost = Wallet.HeartBoostSecondsLeft;

            return boost > 0
                ? Loc.Format("ui.hearts.regen_fast",
                             HeartRules.BoostedRefillSeconds / 3600L, Profile.Countdown(boost))
                : Loc.Format("ui.hearts.regen", HeartRules.RefillSeconds / 3600L);
        }

        /// <summary>When the next one lands, or that the clock has nothing left to do.</summary>
        static string NextHeartLine()
            => Profile.HeartsRefilled
                ? Loc.Get("ui.hearts.full")
                : Loc.Format("ui.hearts.next", Profile.HeartCountdown());

        /// <summary>How many more videos today's allowance has room for.</summary>
        string AllowanceLine()
        {
            int left = RewardedAds.RemainingToday(PlacementId);
            return left > 0 ? Loc.Format("ui.ads.left_today", left) : Loc.Get("ui.ads.none_left_today");
        }

        void BuildFacts(float top)
        {
            _factLines = new Text[_facts.Length];

            const float left = -ContentW * .5f;
            float textW = ContentW - FactIcon - FactGap;
            float textX = left + FactIcon + FactGap + textW * .5f;

            for (int i = 0; i < _facts.Length; i++)
            {
                var fact = _facts[i];
                float y = top + FactStep * i + FactStep * .5f;

                var icon = UIKit.Img("F" + i, Panel, Art.S("Ui/" + fact.Icon), fact.Tint,
                                     Vector2.one * FactIcon, new Vector2(.5f, 1f),
                                     new Vector2(left + FactIcon * .5f, -y));
                icon.preserveAspect = true;

                _factLines[i] = UIKit.Shrinkable(
                    UIKit.Titled("L" + i, Panel, fact.Line(), 27, new Color(.36f, .25f, .18f),
                                 TextAnchor.MiddleLeft, new Vector2(textW, FactStep - 12f),
                                 new Vector2(.5f, 1f), new Vector2(textX, -y),
                                 outline: 0f, shadow: 0f, wrap: true), 19);
            }
        }

        // ------------------------------------------------------------- painting
        /// <summary>
        /// Kept live rather than painted once, because two of the reasons an offer is
        /// unavailable resolve by themselves — a cooldown runs out, and fill arrives — and
        /// a panel that had to be closed and reopened to notice is a panel players learn
        /// to distrust.
        /// </summary>
        /// <summary>
        /// Ticks the panel four times a second rather than sixty.
        ///
        /// <para>
        /// Every live thing on it — a heart countdown, a boost, a cooldown, the day's
        /// allowance — has one-second granularity, so a per-frame repaint was rebuilding
        /// the same five strings fifty-nine times out of sixty. That is a few hundred
        /// bytes of garbage a second on a phone, spent to render pixels nobody could tell
        /// apart. A quarter second still lands inside every second boundary, and anything
        /// that changes for a reason other than the clock arrives on
        /// <see cref="RewardedAds.Changed"/> immediately regardless of this.
        /// </para>
        /// </summary>
        const float TickSeconds = .25f;
        float _tick;

        void Update()
        {
            if (_paid || _watching) return;

            _tick += Time.unscaledDeltaTime;
            if (_tick < TickSeconds) return;

            _tick = 0f;
            Repaint();
        }

        void Repaint()
        {
            if (_paid || _watching) return;

            // The facts keep ticking even when there is no offer to make — a player who
            // opened this panel to find out when their next heart lands is owed a clock
            // that moves, whether or not the network has anything to show them.
            PaintFacts();

            if (_status == null) return;

            var status = RewardedAds.Status(PlacementId);

            if (_watch != null) _watch.Interactable = status.CanShow;
            _status.text = Explain(status);
        }

        /// <summary>
        /// Re-reads every fact, assigning only what actually moved. A heart countdown
        /// changes once a second and the three lines around it change almost never, so
        /// this is the same bargain <see cref="AdOfferButton.Paint"/> makes: no text mesh
        /// is rebuilt on a frame that would have rendered identically.
        /// </summary>
        void PaintFacts()
        {
            if (_factLines == null) return;

            for (int i = 0; i < _factLines.Length; i++)
            {
                var line = _factLines[i];
                if (line == null) continue;

                string text = _facts[i].Line();
                if (!string.Equals(line.text, text, StringComparison.Ordinal)) line.text = text;
            }
        }

        /// <summary>
        /// The sentence under the button. Every branch names the real reason and, where
        /// there is one, when it stops being true.
        /// </summary>
        static string Explain(AdOfferStatus status)
        {
            switch (status.State)
            {
                case AdOfferState.Ready:
                    return Loc.Get("ui.ads.ready");

                case AdOfferState.NotLoaded:
                    return Loc.Get("ui.ads.loading");

                case AdOfferState.CoolingDown:
                    return string.Format(Loc.Get("ui.ads.cooling"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.CapReached:
                    return string.Format(Loc.Get("ui.ads.cap_reached"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.NothingToGain:
                    return Loc.Format("ui.ads.hearts_ceiling", Profile.HeartCeiling);

                case AdOfferState.NeedsAccount:
                    return Loc.Get("ui.ads.needs_account");

                default:
                    return Loc.Get("ui.ads.unavailable");
            }
        }

        // ------------------------------------------------------------- watching
        void OnWatch()
        {
            if (_watching || _paid) return;
            if (!RewardedAds.CanOffer(PlacementId)) { Repaint(); return; }

            _watching = true;
            _watch.Interactable = false;
            _status.text = Loc.Get("ui.ads.opening");

            Show();
        }

        /// <summary>
        /// Shows the ad and pays for it.
        ///
        /// <para>
        /// The impression is generated here, before the SDK is asked for anything, because
        /// the nonce inside it has to reach the ad network as a custom parameter — it is
        /// what the network's verification callback will name, and therefore what the
        /// server will match a claim against. See <see cref="AdImpression"/>.
        /// </para>
        /// </summary>
        async void Show()
        {
            var impression = AdImpression.New(PlacementId);

            try
            {
                var result = await RewardedAds.Provider.ShowAsync(impression);

                // The overlay can be gone by now — a player who backgrounds the app during
                // a video may come back to a different screen entirely. The reward is still
                // banked, because Redeem does not touch the UI.
                var drop = RewardedAds.Redeem(result);

                if (this == null) return;

                if (drop.IsValid) { Paid(drop); return; }

                _watching = false;
                _status.text = Refusal(result.Outcome);
                Repaint();
            }
            catch (Exception e)
            {
                // async void swallows exceptions, and an ad that throws must not leave the
                // panel stuck on "opening" with a dead button.
                Debug.LogException(e);

                if (this == null) return;

                _watching = false;
                _status.text = Loc.Get("ui.ads.failed");
                Repaint();
            }
        }

        /// <summary>Why nothing was paid, in the player's terms rather than the SDK's.</summary>
        static string Refusal(AdOutcome outcome)
        {
            switch (outcome)
            {
                case AdOutcome.Dismissed: return Loc.Get("ui.ads.dismissed");
                case AdOutcome.NoFill: return Loc.Get("ui.ads.no_fill");
                case AdOutcome.Unavailable: return Loc.Get("ui.ads.unavailable");
                default: return Loc.Get("ui.ads.failed");
            }
        }

        /// <summary>
        /// The reward landed. Small ceremony, then the button becomes the way out.
        ///
        /// Deliberately quieter than a chest opening. This is a transaction the player will
        /// make many times, and a five-second animation that was delightful the first time
        /// is an obstacle by the fourth.
        /// </summary>
        void Paid(ChestDrop drop)
        {
            _paid = true;
            _watching = false;

            if (_reward) _reward.text = RewardArt.Amount(drop);
            if (_card) Tween.Pop(_card, 0f, .34f);

            if (_status) _status.text = Loc.Get("ui.ads.thanks");

            // A continue lets itself out. Every other placement here pays into a balance the
            // player will spend later, so a collect button is a fine last beat; this one pays
            // seconds into a run that is frozen on the other side of the panel, and the thing
            // the player agreed to watch a video for is *being back in it*. Making them find
            // one more button first is a tax on exactly the action we just persuaded them to
            // take — the argument DefeatOverlay.OfferHearts already makes about going straight
            // back into the run rather than back to a panel that has grown a retry button.
            if (PlacementId == AdPlacement.RunContinue)
            {
                if (_watch) _watch.Interactable = false;

                Tween.After(PaidBeat, () => { if (this) Close(() => Report(true)); }, this);

                Audio.Sfx("win", .6f);
                Burst.Sparks(_card, Vector2.zero, RewardArt.Tint(drop.Kind), 20, 420f, 30f, .8f);
                return;
            }

            if (_watch)
            {
                _watch.Interactable = true;
                _watch.Setup(() => Close(() => Report(true)));

                // The play glyph goes with the offer it belonged to. Leaving it in front of
                // "COLLECT" would advertise a second video on a button that closes a panel.
                if (_watch.Icon)
                {
                    Destroy(_watch.Icon.gameObject);
                    _watch.Icon = null;
                }

                if (_watch.Label)
                {
                    _watch.Label.text = Loc.Get("ui.daily.collect");

                    // Back to a plain centred caption. FitLabel is a no-op without a glyph,
                    // so the box it left behind has to be restored by hand.
                    var rt = _watch.Label.rectTransform;
                    rt.sizeDelta = new Vector2(_watch.LabelWidth, rt.sizeDelta.y);
                    rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
                }
            }

            Audio.Sfx("win", .6f);
            Haptic.Tap();
            Burst.Sparks(_card, Vector2.zero, RewardArt.Tint(drop.Kind), 20, 420f, 30f, .8f);
        }

        /// <summary>
        /// How long a self-closing offer holds on the reward before letting go.
        ///
        /// Long enough that the card's pop and its sparks are seen rather than glimpsed, and
        /// short enough that it never feels like a wait — this is on the path back into a run
        /// the player is in the middle of, and they have already watched thirty seconds of
        /// video to get here.
        /// </summary>
        const float PaidBeat = .85f;
    }
}
