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

            var panel = MakePanel(new Vector2(880f, 1000f), Loc.Get(Good.NameKey));

            var art = UIKit.Box("Art", panel, new Vector2(300f, 300f), new Vector2(.5f, 1f),
                                new Vector2(0f, -300f));
            ShopArt.PaintGood(art, Good);

            // What arrives, said as a number rather than as a sentence — it is the one thing
            // on the panel the player is actually deciding about.
            UIKit.Shrinkable(
                UIKit.Titled("Amount", panel,
                             boost ? Loc.Format("ui.shop.boost_hours", Good.Amount)
                                   : Loc.Format("ui.shop.hearts_count", Good.Amount),
                             56, boost ? Pal.Sun : Pal.Rose, TextAnchor.MiddleCenter,
                             new Vector2(700f, 76f), new Vector2(.5f, 1f), new Vector2(0f, -486f)), 30);

            // What it does, read from the rules rather than written into the copy. A panel
            // explaining the game is the first thing to rot when the game is retuned — the
            // lesson StreakInfoOverlay and AdOfferOverlay were both rebuilt around.
            UIKit.Shrinkable(
                UIKit.Titled("Note", panel, Explanation(boost), 28,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.UpperCenter,
                             new Vector2(700f, 130f), new Vector2(.5f, 1f), new Vector2(0f, -566f),
                             3f, 0f, wrap: true), 18);

            var held = UIKit.Titled("Held", panel,
                                    boost ? Loc.Format("ui.shop.boost_left",
                                                       Profile.Countdown(Wallet.HeartBoostSecondsLeft))
                                          : Loc.Format("ui.shop.hearts_held", Profile.Hearts),
                                    26, new Color(1f, .96f, .88f, .62f), TextAnchor.MiddleCenter,
                                    new Vector2(700f, 40f), new Vector2(.5f, 1f), new Vector2(0f, -716f),
                                    3f, 0f);
            UIKit.Shrinkable(held, 16);

            UIKit.TextButton("Buy", panel, "btn_violet",
                             Loc.Format("ui.shop.gem_price", Compact.Number(Good.Gems)), 36,
                             new Vector2(560f, 118f), new Vector2(.5f, 0f), new Vector2(0f, 210f),
                             Confirm, "ic_gem");

            UIKit.TextButton("Cancel", panel, Skins.Resting, Loc.Get("ui.common.cancel"), 30,
                             new Vector2(360f, 92f), new Vector2(.5f, 0f), new Vector2(0f, 96f),
                             () => Close());
        }

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

            return Loc.Format("ui.shop.hearts_explain", HeartRules.RefillCap, HeartRules.Ceiling);
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
                Scenery.Toast(Content, Loc.Get(ShopScreen.GoodRefusalKey(state)), Pal.Sun, 2.6f);
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
    /// Deliberately modest next to <c>CompanionRevealOverlay</c>. A companion is a friend
    /// somebody has been saving for over weeks and is seen thirty times in the life of an
    /// account; a coin pack is a transaction, and a player who buys six of them does not
    /// want six choreographies. It is a chime, a stamp and the numbers — no confetti, no
    /// haptic, for the reason the victory panel dropped both: the celebration should be in
    /// proportion to the thing.
    /// </para>
    /// </summary>
    public sealed class ShopGrantOverlay : ModalView
    {
        /// <summary>Set before <c>Init</c>. See <see cref="ShopSupplyOverlay.Good"/>.</summary>
        public StoreGrant Grant { get; set; }

        protected override void Build()
        {
            if (!Grant.IsValid) { Flow.Dismiss(this); return; }

            var panel = MakePanel(new Vector2(860f, 920f), Loc.Get("ui.shop.thanks"));

            var art = UIKit.Box("Art", panel, new Vector2(300f, 300f), new Vector2(.5f, 1f),
                                new Vector2(0f, -300f));
            ShopArt.Paint(art, Grant.Product);

            UIKit.Halo(panel, Pal.Gold, 620f, .22f, new Vector2(0f, 160f));

            float y = -500f;

            if (Grant.Gems > 0)
            {
                Line(panel, y, "ic_gem", Compact.Number(Grant.Gems), Pal.Bloom);
                y -= 96f;
            }

            if (Grant.Credits > 0)
            {
                Line(panel, y, null, Compact.Number(Grant.Credits), Pal.Gold);
                y -= 96f;
            }

            UIKit.Shrinkable(
                UIKit.Titled("Note", panel, Loc.Get("ui.shop.granted_note"), 26,
                             new Color(1f, .96f, .88f, .74f), TextAnchor.UpperCenter,
                             new Vector2(700f, 90f), new Vector2(.5f, 1f), new Vector2(0f, y - 20f),
                             3f, 0f, wrap: true), 17);

            UIKit.TextButton("Done", panel, "btn_green", Loc.Get("ui.common.ok"), 36,
                             new Vector2(480f, 118f), new Vector2(.5f, 0f), new Vector2(0f, 110f),
                             () => Close());

            Audio.Sfx("chest", .6f);
        }

        /// <summary>One currency line: the glyph, then the figure, as one centred block.</summary>
        static void Line(Transform panel, float y, string icon, string amount, Color tint)
        {
            var row = UIKit.Row("R" + y, panel, new Vector2(560f, 80f), new Vector2(.5f, 1f),
                                new Vector2(0f, y), 18f);

            var glyph = UIKit.Img("I", row, icon == null ? null : Art.S("Ui/" + icon), Color.white,
                                  new Vector2(66f, 66f), new Vector2(.5f, .5f), Vector2.zero);
            glyph.preserveAspect = true;
            if (icon == null) Flipbook.Attach(glyph, "Ui/Coin", 11f);

            var text = UIKit.Titled("V", row, "+" + amount, 52, tint, TextAnchor.MiddleLeft,
                                    new Vector2(320f, 66f), new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Shrinkable(text, 26);
        }
    }
}
