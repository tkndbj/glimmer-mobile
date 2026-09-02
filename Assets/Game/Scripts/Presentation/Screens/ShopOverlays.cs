using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Confirms spending gems on hearts or on a boost.
    ///
    /// <para>
    /// <b>Why this exists when buying gems has no confirmation at all.</b> A money purchase
    /// is confirmed by the store's own sheet, which names the product, states the price and
    /// asks for a fingerprint; putting a panel in front of that would be a tap for a
    /// question about to be asked properly. A gem purchase has no sheet and no
    /// authentication — a mistap on a 280-gem card is two months of free gems gone with no
    /// way back — so this is the only thing standing between a thumb and that.
    /// </para>
    /// <para>
    /// It leads with what the player gets and puts the cost second, which is the same order
    /// <c>CompanionUnlockOverlay</c> uses and for the same reason: a panel that opens with a
    /// price reads as a demand rather than as an offer.
    /// </para>
    /// </summary>
    public sealed class ShopSupplyOverlay : ModalView
    {
        /// <summary>
        /// Set by the caller's configure callback before <c>Init</c> runs, which is why it
        /// is a property rather than a field — the same shape <c>WinOverlay.Run</c> uses.
        /// A public field of a non-serialisable type is flagged by Unity's serialization
        /// analyser, correctly: nothing here is meant to survive a domain reload.
        /// </summary>
        public StoreGood Good { get; set; }

        protected override void Build()
        {
            if (Good == null || !Good.IsValid) { Flow.Dismiss(this); return; }

            bool boost = Good.Kind == StoreGoodKind.HeartBoost;

            var panel = MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Good.NameKey));

            var art = UIKit.Box("Art", panel, new Vector2(300f, 300f), new Vector2(.5f, 1f),
                                new Vector2(0f, -300f));
            ShopArt.PaintGood(art, Good);

            // What arrives, said as a number rather than as a sentence — it is the one thing
            // on the panel the player is actually deciding about.
            UIKit.Shrinkable(
                UIKit.Titled("Amount", panel,
                             boost ? Loc.Format("ui.shop.boost_hours", Good.Amount)
                                   : Loc.Format("ui.shop.hearts_count", Good.Amount),
                             56, boost ? Amber : Rose, TextAnchor.MiddleCenter,
                             new Vector2(700f, 76f), new Vector2(.5f, 1f), new Vector2(0f, -AmountY),
                             outline: 0f, shadow: 2f), 30);

            // What it does, read from the rules rather than written into the copy. A panel
            // explaining the game is the first thing to rot when the game is retuned — the
            // lesson StreakInfoOverlay and AdOfferOverlay were both rebuilt around.
            UIKit.Shrinkable(
                UIKit.Titled("Note", panel, Explanation(boost), 28, Ink, TextAnchor.UpperCenter,
                             new Vector2(700f, NoteH), new Vector2(.5f, 1f), new Vector2(0f, -NoteY),
                             outline: 0f, shadow: 0f, wrap: true), 19);

            var held = UIKit.Titled("Held", panel,
                                    boost ? Loc.Format("ui.shop.boost_left",
                                                       Profile.Countdown(Wallet.HeartBoostSecondsLeft))
                                          : Loc.Format("ui.shop.hearts_held", Profile.Hearts),
                                    26, Pal.A(Ink, .88f), TextAnchor.MiddleCenter,
                                    new Vector2(700f, 46f), new Vector2(.5f, 1f), new Vector2(0f, -HeldY),
                                    outline: 0f, shadow: 0f);
            UIKit.Shrinkable(held, 16);

            UIKit.TextButton("Buy", panel, "btn_violet",
                             Loc.Format("ui.shop.gem_price", Compact.Number(Good.Gems)), 36,
                             new Vector2(560f, 118f), new Vector2(.5f, 0f), new Vector2(0f, 210f),
                             Confirm, "ic_gem");

            // Red rather than the resting grey. Grey means "not a control right now"
            // (see Skins), and this is a live way out of a panel that is asking for gems —
            // the same job btn_red already does for leaving and for wiping.
            UIKit.TextButton("Cancel", panel, "btn_red", Loc.Get("ui.common.cancel"), 30,
                             new Vector2(360f, 92f), new Vector2(.5f, 0f), new Vector2(0f, 96f),
                             () => Close());
        }

        // ------------------------------------------------------------- the layout
        /// <summary>
        /// Where each row sits, measured from the top of the panel to the row's <em>centre</em>
        /// — <c>UIKit.Box</c> always pivots at the middle, whatever it is anchored to.
        ///
        /// <para>
        /// Written down as constants because they were not, and the panel shipped with three
        /// rows printed through one another: the amount ran into the first line of the
        /// explanation, and "You are holding 3" sat under the top edge of the purple button.
        /// Absolute offsets scattered through a build method are the failure
        /// <c>ShopGrantOverlay</c> two classes down uses a cursor to avoid; this panel is short
        /// and fixed enough not to need one, but it does need the numbers somewhere the next
        /// person can add them up.
        /// </para>
        /// </summary>
        const float PanelW = 880f;
        const float PanelH = 1060f;
        const float AmountY = 496f;   // 700x76  -> 458..534
        const float NoteY = 628f;     // 700x150 -> 553..703
        const float NoteH = 150f;
        const float HeldY = 750f;     // 700x46  -> 727..773, clear of the button at 791

        /// <summary>
        /// Ink and two darkened accents, for <c>HomesteadBuyOverlay.Ink</c>'s reason:
        /// <c>panel_main</c> is a light parchment, so cream copy on it is held apart from its
        /// ground only by an outline, and <c>Pal.Sun</c> and <c>Pal.Rose</c> are colours chosen
        /// against the board's near-black plate. Both were reported here as unreadable.
        /// </summary>
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Rose = new Color(.70f, .19f, .17f);
        static readonly Color Amber = new Color(.62f, .34f, .08f);

        /// <summary>
        /// What the purchase actually does, in the player's terms, derived from the live
        /// heart rules rather than restated. Retuning the gate rewrites this line.
        /// </summary>
        string Explanation(bool boost)
        {
            if (boost)
            {
                long normal = HeartRules.RefillSeconds / 3600L;
                long fast = HeartRules.BoostedRefillSeconds / 3600L;
                return Loc.Format("ui.shop.boost_explain", fast, normal);
            }

            // This player's cap rather than the published one — a keeper who bought a
            // container is told their hearts refill to twenty, which is the number they
            // paid for and the only one this sentence can honestly print.
            return Loc.Format("ui.shop.hearts_explain", Wallet.MaxHearts, HeartRules.Ceiling);
        }

        void Confirm()
        {
            var state = StoreService.TryBuyGood(Good);

            if (state != GoodOfferState.Ready)
            {
                // Reachable: the balance can move while the panel is open — a sync landing,
                // another device spending. Refusing here rather than trusting the state the
                // panel was built from is what stops a debit going through on a balance that
                // no longer covers it.
                Scenery.Toast(Content, Loc.Get(StoreWording.GoodRefusal(state)), Pal.Sun, 2.6f);
                return;
            }

            // A sound and no haptic, which is the rule the victory panel arrived at:
            // Handheld.Vibrate is one fixed-length buzz on Android, so there is no way to
            // make a small purchase feel lighter than a big one, and a shop is somewhere a
            // player taps repeatedly.
            Audio.Sfx("coin", .6f);

            Close(() =>
            {
                var screen = Flow.Current;
                if (screen == null) return;

                Scenery.Toast(screen.Content,
                              Good.Kind == StoreGoodKind.HeartBoost
                                  ? Loc.Format("ui.shop.boost_added", Good.Amount)
                                  : Loc.Format("ui.shop.hearts_added", Good.Amount),
                              Good.Kind == StoreGoodKind.HeartBoost ? Pal.Sun : Pal.Rose, 2.4f);
            });
        }
    }

    /// <summary>
    /// What a purchase bought, once the server has actually granted it.
    ///
    /// <para>
    /// Raised by <c>StoreService.Granted</c> and therefore <b>only after the money has
    /// become currency</b> — never when the payment sheet closes, and never on a retry that
    /// granted nothing. That ordering is the whole point of the panel: it is the receipt,
    /// and a receipt that appears before the goods arrive is the thing that makes a player
    /// distrust a shop.
    /// </para>
    /// <para>
    /// <b>It used to be a chime, a stamp and two static numbers, and that was wrong.</b> The
    /// argument for keeping it modest was that a coin pack is a transaction rather than a
    /// friend somebody saved for over weeks, so it should not get
    /// <c>CompanionRevealOverlay</c>'s choreography. That is true, and it answered the wrong
    /// question: the fault was never the length, it was that <em>nothing happened</em>. A
    /// number that is simply printed is the game telling somebody what they bought; a number
    /// that climbs because coins are pouring out of the chest they just paid for is that
    /// person watching the transaction happen. <see cref="Payout"/> was written for exactly
    /// that distinction on the victory panel, and the one screen in the game where real money
    /// changes hands was the one screen not using it.
    /// </para>
    /// <para>
    /// <b>The arc is money → goods → your purse, and it is three mechanisms that already
    /// existed.</b> The product lands with a shockwave; <see cref="Payout"/> throws its
    /// contents out of it and rolls each figure up one landing at a time; and COLLECT hands
    /// the whole thing to <see cref="RewardFlight"/>, which empties the chips into the
    /// balance row of whatever screen is underneath — the shop's own purse, or the hub's
    /// pills when the grant arrives while the player is standing somewhere else. That last
    /// step is why the panel is worth a button at all: without it a player is told a number
    /// and left to go and check.
    /// </para>
    /// <para>
    /// <b>What keeps it from becoming an obstacle by the fourth purchase.</b> There is a way
    /// out about half a second in — the button appears when the tokens start flying, not when
    /// they land, and tapping it early is safe because the flight's snapshot was taken at
    /// build time and owes nothing to the ceremony. The confetti and the single haptic are on
    /// the last landing and nowhere else: <c>Handheld.Vibrate</c> is one fixed-length pulse on
    /// Android, so several inside a second overlap into a rumble rather than reading as
    /// several taps, which is the mistake the chest made eight times over. One buzz, at the
    /// payoff, on the rarest and most expensive event in the game.
    /// </para>
    /// </summary>
    public sealed class ShopGrantOverlay : ModalView
    {
        /// <summary>Set before <c>Init</c>. See <see cref="ShopSupplyOverlay.Good"/>.</summary>
        public StoreGrant Grant { get; set; }

        /// <summary>
        /// Raised once, whatever closed the panel, so something can be chained behind it.
        ///
        /// <para>
        /// From <c>OnDestroy</c> and never from the button, which is the rule
        /// <c>AdOfferOverlay.Dismissed</c> and <c>PauseOverlay</c> both arrived at the hard
        /// way: this panel has four exits — the button, the back key, the cascade finishing,
        /// and the screen dying underneath it — so anything wired to one of them fires from
        /// one of them. The safe outcome has to be the default and the exception has to be the
        /// thing somebody declares.
        /// </para>
        /// </summary>
        public System.Action Dismissed;

        // ------------------------------------------------------------- geometry
        // Laid out by a cursor walking down the panel rather than by absolute offsets,
        // because the panel is not a fixed length: a coin pack has one currency line and a
        // bundle has two. Absolute offsets would mean the note printed through the button on
        // whichever shelf nobody checked — the failure AdOfferOverlay's layout was rewritten
        // to remove.
        const float PanelW = 880f;
        const float HeadRoom = 150f;      // under the ribbon
        const float ArtSize = 300f;
        const float NameH = 52f;
        const float ChipH = 112f;
        const float ChipStep = 120f;
        const float NoteH = 84f;
        const float ButtonH = 118f;
        const float FootRoom = 52f;

        /// <summary>
        /// The rhythm, and it is the design.
        ///
        /// <para>
        /// <see cref="ArriveAt"/> is the beat the panel has to itself before the product lands
        /// in it, so the shockwave breaks against something that already exists rather than
        /// arriving with it — the same reason <c>Payout.LeadIn</c> exists one level down.
        /// <see cref="PayoutAt"/> is late enough that the impact has been seen and early
        /// enough that nobody is waiting; it is also when the button appears, so there is
        /// never more than about half a second in which this panel cannot be left.
        /// </para>
        /// </summary>
        const float ArriveAt = .14f, PayoutAt = .58f, ChipGap = .18f;

        RectTransform _art;
        Btn _done;
        Payout _gems, _credits;

        /// <summary>
        /// The one row a heart container's receipt has, and it is not a <see cref="Payout"/>.
        ///
        /// A payout exists to walk a figure up and empty it into a pill on the screen
        /// underneath; a capacity is not a balance and has no pill, so the honest shape is a
        /// line that states what changed and lands with the rest of the ceremony.
        /// </summary>
        RectTransform _capacity;

        RewardFlight _flight;
        bool _collecting;

        void OnDestroy()
        {
            var dismissed = Dismissed;
            Dismissed = null;
            dismissed?.Invoke();
        }

        protected override void Build()
        {
            if (!Grant.IsValid) { Flow.Dismiss(this); return; }

            // Snapshotted here rather than when COLLECT is pressed, and it is the one thing
            // about this panel that has to happen in Build. The grant is what raised the
            // event, so the balance has already moved; AfterGrant takes it back off to find
            // what the purse read a moment ago. Doing it later would usually be identical —
            // doing it here means the figure cannot drift if the player leaves the panel
            // standing while something else lands.
            _flight = RewardFlight.AfterGrant(Grant.Credits, Grant.Gems);

            // A container has exactly one line — what the cap is now — and every currency
            // product has one per currency it granted.
            int lines = Grant.IsContainer
                ? 1
                : (Grant.Gems > 0 ? 1 : 0) + (Grant.Credits > 0 ? 1 : 0);

            // Only a container's receipt has a line under the chips, and the room for it is
            // reserved only when it does. A currency product's chips already say the whole
            // of what arrived, so a sentence under them was words for the sake of the shape
            // of the panel — and a panel that keeps the gap after losing the sentence is a
            // band of empty plate above its button.
            bool note = Grant.IsContainer;

            float y = HeadRoom;
            float artY = y + ArtSize * .5f;    y += ArtSize + 8f;
            float nameY = y;                   y += NameH + 16f;
            float chipsY = y;                  y += lines * ChipStep + 10f;
            float noteY = y;                   if (note) y += NoteH + 20f;
            float buttonY = y + ButtonH * .5f; y += ButtonH + FootRoom;

            // Never dismissed by a stray tap on the scrim. This is the receipt for a real
            // payment, and one that can be flicked away by a thumb landing anywhere is one a
            // player can miss entirely and then wonder what they were charged for. The back
            // key still works throughout — see OnBack.
            var panel = MakePanel(new Vector2(PanelW, y), Loc.Get("ui.shop.thanks"),
                                  dismissOnScrim: false);

            BuildRays(panel, artY);
            UIKit.Halo(panel, Pal.Gold, 660f, .24f, new Vector2(0f, y * .5f - artY));

            _art = UIKit.Box("Art", panel, Vector2.one * ArtSize, new Vector2(.5f, 1f),
                             new Vector2(0f, -artY));
            ShopArt.Paint(_art, Grant.Product);

            // Enters after the panel is already standing, so it reads as the goods being
            // handed over rather than as part of the furniture.
            _art.localScale = Vector3.zero;

            UIKit.Shrinkable(
                UIKit.Titled("Name", panel, Loc.Get(Grant.Product.NameKey), 34, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(700f, NameH),
                             new Vector2(.5f, 1f), new Vector2(0f, -nameY - NameH * .5f), 3f, 3f), 20);

            BuildChips(panel, chipsY);

            if (note)
                UIKit.Shrinkable(
                    UIKit.Titled("Note", panel,
                                 Loc.Format("ui.shop.capacity_note", Wallet.MaxHearts), 26,
                                 new Color(1f, .96f, .88f, .74f), TextAnchor.UpperCenter,
                                 new Vector2(700f, NoteH), new Vector2(.5f, 1f),
                                 new Vector2(0f, -noteY - NoteH * .5f), 3f, 0f, wrap: true), 17);

            // The label tells the truth about what the button does. Where there is a balance
            // row underneath, this empties the panel into it and COLLECT is the honest verb;
            // where there is not — the map, the grove, a purchase credited on the next launch
            // — it simply closes, and calling that "collect" would promise something the
            // player then does not see happen.
            _done = UIKit.TextButton("Done", panel, "btn_green",
                                     Loc.Get(CanFly() ? "ui.daily.collect" : "ui.common.ok"), 36,
                                     new Vector2(480f, ButtonH), new Vector2(.5f, 1f),
                                     new Vector2(0f, -buttonY), OnCollect);

            _done.transform.localScale = Vector3.zero;

            Schedule();
        }

        /// <summary>
        /// Whether the currency has anywhere to land, asked before the chips exist.
        ///
        /// <see cref="RewardFlight.Add"/> is the authority and is asked again at collect time
        /// with the real sources; this is only the button's caption, which has to be decided
        /// while the panel is being built.
        /// </summary>
        bool CanFly()
            => (Grant.Credits > 0 && ResourceSlots.TryGet(ResourceSlots.Kind.Credits, out _))
            || (Grant.Gems > 0 && ResourceSlots.TryGet(ResourceSlots.Kind.Gems, out _));

        /// <summary>
        /// Light behind the goods, turning slowly. The chest opens on the same figure, which
        /// is the point: this is the other moment in the game where something is handed over,
        /// and a player should recognise it as one.
        /// </summary>
        static void BuildRays(RectTransform panel, float artY)
        {
            var host = UIKit.Box("Rays", panel, Vector2.one * 520f, new Vector2(.5f, 1f),
                                 new Vector2(0f, -artY));
            host.SetAsFirstSibling();

            for (int i = 0; i < 8; i++)
            {
                var ray = UIKit.Img("r" + i, host, Art.SoftCapsule(40, 200), Pal.A(Pal.Sun, .13f),
                                    new Vector2(40f, 620f), new Vector2(.5f, .5f), Vector2.zero);
                ray.raycastTarget = false;
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 22.5f);
            }

            Tween.Run(18f, Ease.Linear,
                      t => { if (host) host.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      host.gameObject, "spin").Loop(-1, false);
        }

        /// <summary>
        /// One <see cref="Payout"/> per currency the product granted.
        ///
        /// Gems lead where a bundle has both, which is the order the shelf uses and the order
        /// the card printed — a receipt that reorders what was bought reads as a different
        /// purchase.
        /// </summary>
        void BuildChips(RectTransform panel, float top)
        {
            float y = top + ChipH * .5f;

            if (Grant.IsContainer) { _capacity = CapacityRow(panel, y); return; }

            if (Grant.Gems > 0)
            {
                _gems = Chip(panel, y, ChestDropKind.Gems, Grant.Gems);
                y += ChipStep;
            }

            if (Grant.Credits > 0) _credits = Chip(panel, y, ChestDropKind.Credits, Grant.Credits);
        }

        /// <summary>
        /// The heart container's line: the old limit, an arrow, and the new one.
        ///
        /// <para>
        /// It says what <em>changed</em> rather than what was bought, because the card the
        /// player tapped already said "20" and the number they need is the one they had
        /// before it. <see cref="StoreGrant.CapacityWas"/> is read at redemption for exactly
        /// this — after the entitlement lands the old figure is gone.
        /// </para>
        /// <para>
        /// A plate rather than a <c>Payout</c> chip, and the difference is not decoration: a
        /// chip exists to throw tokens into a balance pill, and a permanent limit has no pill
        /// and no balance. Drawing one would promise a flight that could never happen.
        /// </para>
        /// </summary>
        RectTransform CapacityRow(RectTransform panel, float y)
        {
            var row = UIKit.Img("Capacity", panel, Art.Round(26), new Color(.24f, .10f, .16f, .82f),
                                new Vector2(560f, ChipH), new Vector2(.5f, 1f), new Vector2(0f, -y));

            var edge = UIKit.Img("Edge", row.transform, Art.RoundOutline(26, 2.5f),
                                 Pal.A(Pal.Rose, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var glyph = UIKit.Img("Icon", row.transform, Art.S("Ui/ic_heart"), Pal.Rose,
                                  Vector2.one * 60f, new Vector2(0f, .5f), new Vector2(64f, 0f));
            glyph.preserveAspect = true;

            // The label sits to the right of the glyph, and the offset has to carry half its
            // own width because UIKit.Box *always* pivots at centre however it is anchored —
            // the trap Corner() exists for, which cannot be used here because it reads an
            // anchor y of .5 as "top" and would drop the line 30 units. Passing the margin
            // straight through put this 400-wide box at -84..316 on a plate starting at 0, so
            // the line hung off the left edge and ran underneath the heart. Shipped that way.
            const float labelLeft = 116f, labelWidth = 400f;

            UIKit.Shrinkable(
                UIKit.Titled("Was", row.transform,
                             Loc.Format("ui.shop.capacity_upgrade",
                                        Grant.CapacityWas, Wallet.MaxHearts),
                             34, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(labelWidth, 60f), new Vector2(0f, .5f),
                             new Vector2(labelLeft + labelWidth * .5f, 0f),
                             3f, 3f), 20);

            var rt = (RectTransform)row.transform;
            rt.localScale = Vector3.zero;
            return rt;
        }

        /// <summary>
        /// One chip, drawn through <c>RewardArt</c> so the glyph, the colour and the token are
        /// the ones the chest and the rewarded ad already use for the same currency.
        /// </summary>
        static Payout Chip(RectTransform panel, float y, ChestDropKind kind, long amount)
        {
            RewardArt.Token(kind, out var token, out var tokenTint);

            var chip = Payout.Chip("Chip" + kind, panel, new Vector2(.5f, 1f), new Vector2(0f, -y),
                                   RewardArt.Icon(kind), RewardArt.Tint(kind),
                                   n => "+" + Compact.Number(n), amount,
                                   token, tokenTint, sfx: "coin");

            // Credits have no sprite of their own — they are the spinning flipbook — and this
            // is the call that also covers the frames not having arrived. See RewardArt.Glyph.
            RewardArt.Glyph(chip.Glyph, kind, 11f);

            return chip;
        }

        // ------------------------------------------------------------- the beats
        /// <summary>
        /// The ceremony, scheduled off the panel rather than chained through each other's
        /// callbacks, so retiming the middle does not mean re-deriving every delay after it.
        /// </summary>
        void Schedule()
        {
            // A warm wash across the whole screen at the instant the receipt lands. Peaked low
            // on purpose: this sits on top of whatever the player was doing, and a white-out
            // over a board or a map is a transition rather than a flourish.
            Flow.Flash(Pal.Gold, .26f, .55f);

            Tween.After(ArriveAt, Arrive, this);

            float last = PayoutAt;

            if (_gems != null)
            {
                Tween.After(PayoutAt, () => { if (this) _gems.Play(_art); }, this);
                last = PayoutAt + _gems.Duration;
            }

            if (_credits != null)
            {
                float at = _gems != null ? PayoutAt + ChipGap : PayoutAt;
                Tween.After(at, () => { if (this) _credits.Play(_art); }, this);
                last = Mathf.Max(last, at + _credits.Duration);
            }

            // The container's one line, on the same beat a payout would have started on, so
            // the receipt has the same rhythm whichever kind of thing was bought.
            if (_capacity != null)
            {
                Tween.After(PayoutAt, () =>
                {
                    if (this == null || !_capacity) return;
                    Tween.Pop(_capacity, 0f, .5f);
                    Burst.Sparks(_capacity, Vector2.zero, Pal.Rose, 14, 300f, 26f, .66f);
                }, this);

                last = Mathf.Max(last, PayoutAt + .5f);
            }

            // With the tokens, not with their landing: a button that arrives at the end of the
            // ceremony is a ceremony the player has to sit through.
            Tween.After(PayoutAt, () =>
            {
                if (this == null || !_done) return;
                Tween.Pop(_done.transform, 0f, .5f).OnDone(() =>
                {
                    if (!_done) return;
                    _done.Rehome();
                    Sheen.Attach((RectTransform)_done.transform, 3.2f);
                });
            }, this);

            Tween.After(last, Payoff, this);
        }

        /// <summary>
        /// The goods land: a spring, two shockwaves and a burst — and no sound of its own.
        ///
        /// The clunk that used to ride the entrance was withdrawn by the owner. What is left
        /// still carries the beat, because the tokens start half a second later and the tune
        /// lands under the confetti; adding a different noise here would be answering a
        /// removal with a substitution.
        /// </summary>
        void Arrive()
        {
            if (_art == null) return;

            Tween.Scale(_art, 1f, .42f, Ease.OutBack).OnDone(() =>
            {
                // Started only once the entrance is over, because both write localScale and
                // two tweens on one value fight for it every frame they share.
                if (_art) Tween.Breathe(_art, .035f, 3.1f);
            });

            Shockwave(0f, 1.9f, .52f, .80f);
            Shockwave(.10f, 2.6f, .62f, .42f);

            Burst.Sparks(_art, Vector2.zero, Pal.Gold, 20, 380f, 30f, .72f);
        }

        /// <summary>
        /// A ring breaking outwards from the goods.
        ///
        /// The starting alpha is captured rather than the live colour being scaled down each
        /// frame — scaling the live value compounds, so the fade would depend on how many
        /// frames it got. <c>Payout.Ping</c> records the same trap.
        /// </summary>
        void Shockwave(float delay, float to, float dur, float alpha)
        {
            if (_art == null) return;

            var ring = UIKit.Img("Wave", _art, Art.Ring(128, 8f),
                                 Pal.A(Pal.Lift(Pal.Gold, .4f), alpha),
                                 Vector2.one * ArtSize * .9f, new Vector2(.5f, .5f), Vector2.zero);
            ring.raycastTarget = false;

            var rt = (RectTransform)ring.transform;

            Tween.Run(dur, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.35f, to, t);
                var c = ring.color; c.a = alpha * (1f - t); ring.color = c;
            }, ring).Delay(delay)
             .OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// The last figure has landed. Everything loud is here and nowhere else.
        ///
        /// One haptic, on the rarest event in the game, at the moment the numbers finish
        /// rather than at any of the four beats before it — see the class remarks for why
        /// several would be one rumble instead of several taps.
        /// </summary>
        void Payoff()
        {
            if (this == null || _collecting) return;

            Burst.Confetti(Content, 44);
            Audio.Sfx("win", .55f);

            if (_art) { _art.localScale = Vector3.one; Tween.Punch(_art, .12f, .38f); }
        }

        // ------------------------------------------------------------- collecting
        /// <summary>
        /// The player took the receipt. Where there is a balance row underneath, the chips
        /// empty into it — the same cascade the daily chest and the rewarded ad use, so the
        /// three places money turns into currency all read as one gesture.
        /// </summary>
        void OnCollect()
        {
            if (_collecting) return;

            bool any = false;

            if (_gems != null)
                any |= _flight.Add(new ChestDrop(ChestDropKind.Gems, Amount(Grant.Gems)), _gems.Root);

            if (_credits != null)
                any |= _flight.Add(new ChestDrop(ChestDropKind.Credits, Amount(Grant.Credits)), _credits.Root);

            // Asked before the latch, so a panel with nowhere to fly to is still an ordinary
            // close — including its sound and its scale-out, which the cascade does not use.
            if (!any) { Close(); return; }

            _collecting = true;
            Fly();
        }

        /// <summary>
        /// A grant as a drop's amount.
        ///
        /// Only ever read for its token count, which is clamped to a handful — so the
        /// saturating cast is a fact about the throw rather than a truncation of the payment.
        /// A product granting more than two billion of anything is not a rounding question.
        /// </summary>
        static int Amount(long value)
            => value >= int.MaxValue ? int.MaxValue : value <= 0 ? 0 : (int)value;

        /// <summary>
        /// Clears the panel out of the way and throws the purchase at the balance row.
        ///
        /// The chips are lifted out of the panel first: they are the things the tokens come
        /// out of, so they have to outlive their own parent by a beat, while everything else
        /// here is chrome the moment the receipt has been read. <c>SetParent</c> keeps their
        /// world position, so nothing moves as it changes hands. The scrim stops taking taps
        /// as well as fading — one at zero alpha still swallows everything aimed at what is
        /// now visible through it.
        /// </summary>
        void Fly()
        {
            if (_done) _done.Interactable = false;

            if (_gems != null && _gems.Root) _gems.Root.SetParent(Content, true);
            if (_credits != null && _credits.Root) _credits.Root.SetParent(Content, true);

            if (Scrim)
            {
                Scrim.raycastTarget = false;
                Tween.Fade(Scrim, 0f, RewardFlight.ClearAt);
            }

            if (Panel)
            {
                var group = UIKit.Group(Panel);
                group.interactable = false;
                group.blocksRaycasts = false;
                Tween.Fade(group, 0f, RewardFlight.ClearAt * .8f);
            }

            _flight.Play(Content, () => { if (this) Flow.Dismiss(this); });
        }

        /// <summary>
        /// Swallowed once the payout has started, for <c>ChestOverlay.OnBack</c>'s reason: the
        /// purchase is banked either way, but <see cref="ModalView.Close"/> fades the whole
        /// content group and the tokens are in it, so the back key would delete the animation
        /// mid-flight and leave the balance row rewound to its old figures.
        ///
        /// Before that it closes, which is what gives this panel a way out from the first
        /// frame — the scrim does not dismiss it and the button takes half a second to arrive.
        /// </summary>
        public override bool OnBack()
        {
            if (_collecting) return true;
            Close();
            return true;
        }
    }
}
