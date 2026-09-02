using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind a locked companion: who they are, the two ways to wake them, and
    /// the button that pays.
    ///
    /// <para>
    /// It replaces a toast. Tapping a padlock used to say "Reach keeper level 24 to wake this
    /// friend" and vanish, which was true and useless — level 24 is unreachable by play for
    /// well over a year on a hundred-glade catalog, so the one thing the game told a player
    /// about most of its roster was a wait they could not serve. The panel exists because
    /// there is now a second answer, and a second answer needs somewhere to be given.
    /// </para>
    /// <para>
    /// It leads with <b>both</b> routes and never just the one that takes money. A companion
    /// is reached by levelling or by paying, both are real, and a panel that mentions only
    /// the price reads as a paywall on something the player was going to be given anyway.
    /// The level line is therefore first and is phrased as a fact, not as a consolation.
    /// </para>
    /// <para>
    /// Every number on it is read from the roster and the live balance rather than written
    /// into the copy — <c>AdOfferOverlay</c> and <c>StreakInfoOverlay</c> are built the same
    /// way for the same reason: a panel that explains the economy is the first thing to rot
    /// when the economy is retuned.
    /// </para>
    /// </summary>
    public sealed class CompanionUnlockOverlay : ModalView
    {
        /// <summary>
        /// Which companion is being offered. Set by the caller before Build runs.
        ///
        /// A property rather than a field, for the reason <c>DefeatOverlay.Run</c> is one:
        /// <see cref="AvatarDefinition"/> is not <c>[Serializable]</c>, so a public field of
        /// that type earns a UAC1001 warning about serialization that will never happen —
        /// this is assigned in code, never through the inspector.
        /// </summary>
        public AvatarDefinition Avatar { get; set; }

        /// <summary>
        /// Whether buying also puts this companion on the player's nameplate. True from the
        /// profile, false from the grove.
        ///
        /// <para>
        /// The profile's roster is a picker: the tap that buys somebody there is the same tap
        /// that chooses them, and making the player choose twice would be friction over a
        /// decision they have already made. The grove's shelf is not — a resident is bought to
        /// <em>stand</em> somewhere, and quietly changing who the player is called after is the
        /// kind of surprise that makes somebody distrust a shop. One panel, one flag, because
        /// two panels would be two places to get the six honest refusals right.
        /// </para>
        /// </summary>
        public bool WearOnBuy { get; set; } = true;

        // ------------------------------------------------------------- geometry
        // A cursor walking down the panel rather than absolute offsets, for the reason
        // AdOfferOverlay's does: the fact list is not a fixed length. A companion with no
        // price loses its cost line, one already reachable loses its level line, and absolute
        // offsets would silently draw the fourth fact over the button.
        const float PanelW = 880f;
        const float ContentW = 700f;
        const float HeadRoom = 148f;
        const float PortraitSize = 300f;
        const float NameH = 62f;
        const float FactStep = 76f;
        const float FactIcon = 48f;
        const float FactGap = 18f;
        const float StatusH = 92f;
        const float ButtonH = 148f;
        const float FootRoom = 44f;

        Btn _buy;
        Text _status;
        Image _portrait;
        RectTransform _disc;
        bool _paid;

        /// <summary>
        /// True while a purchase is in flight, which <see cref="Repaint"/> must not paint over.
        ///
        /// <para>
        /// Not defensive bookkeeping — it closes a real hole. The debit is booked before the
        /// companion is recorded as held (see <c>CompanionLedger.TryBuy</c> for why that order
        /// is the safe one), and booking it raises <c>PlayerProgression.Changed</c>. So there
        /// is a moment inside the purchase where the balance has already fallen and the id has
        /// not yet arrived, and a repaint landing there reads the state as "unheld and now
        /// unaffordable" — it would destroy the buy button from inside that button's own click
        /// handler, replace it with the coin offer, and leave <see cref="Paid"/> stamping
        /// "WEAR" onto the wrong control. <c>AdOfferOverlay</c> holds <c>_watching</c> for the
        /// same reason.
        /// </para>
        /// </summary>
        bool _buying;

        /// <summary>Where the button sits, kept so a state change can rebuild it in place.</summary>
        float _buttonY;

        protected override void Build()
        {
            var offer = Profile.OfferFor(Avatar);

            float y = HeadRoom;
            float discY = y + PortraitSize * .5f;  y += PortraitSize + 18f;
            float nameY = y;                       y += NameH + 14f;
            float factsY = y;                      y += FactCount(offer) * FactStep + 14f;
            float statusY = y + StatusH * .5f;     y += StatusH + 12f;
            float buttonY = y + ButtonH * .5f;     y += ButtonH + FootRoom;

            MakePanel(new Vector2(PanelW, y), Loc.Get("ui.companion.title"));

            BuildPortrait(discY);

            UIKit.Shrinkable(
                UIKit.Titled("Name", Panel, Loc.Get(Avatar.NameKey), 52,
                             new Color(.30f, .20f, .13f), TextAnchor.MiddleCenter,
                             new Vector2(ContentW, NameH), new Vector2(.5f, 1f),
                             new Vector2(0f, -nameY), outline: 0f, shadow: 2f), 34);

            BuildFacts(factsY, offer);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 30, new Color(.36f, .25f, .18f),
                             TextAnchor.UpperCenter, new Vector2(ContentW, StatusH),
                             new Vector2(.5f, 1f), new Vector2(0f, -statusY),
                             outline: 0f, shadow: 0f, wrap: true), 22);

            BuildButton(buttonY, offer);

            // A corner cross rather than a second full-width button, and never no exit at
            // all — the reasoning is AdOfferOverlay's, and it applies harder here: a modal
            // about spending currency whose only exit is the scrim is what store reviewers
            // flag as a dark pattern.
            UIKit.IconButton("Dismiss", Panel, Skins.Nav, "ic_close", new Vector2(84f, 84f),
                             new Vector2(1f, 1f), new Vector2(-58f, -58f), () => Close());

            // A balance that moves while the panel is open — the player watched a video from
            // the ad panel this one opened — has to reach the button, or they come back to
            // the exact screen that sent them away still saying they cannot afford it.
            PlayerProgression.Changed += Repaint;
            Repaint();
        }

        void OnDestroy() => PlayerProgression.Changed -= Repaint;

        // ------------------------------------------------------------- the friend
        /// <summary>
        /// The portrait, drawn full-colour rather than as the grid's grey silhouette.
        ///
        /// Deliberate: the grid greys a locked companion so the eye can skip it, but this
        /// panel is the moment somebody is deciding whether to want one, and a player cannot
        /// want a shadow.
        /// </summary>
        void BuildPortrait(float y)
        {
            var disc = UIKit.Img("Disc", Panel, Art.Disc(300), Pal.A(Pal.Hex("#0B4C55"), .95f),
                                 Vector2.one * PortraitSize, new Vector2(.5f, 1f), new Vector2(0f, -y));
            _disc = (RectTransform)disc.transform;

            var ring = UIKit.Img("Ring", disc.transform, Art.Ring(300, 8f), Pal.A(Pal.Gold, .55f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            UIKit.Halo(disc.transform, Pal.Gold, PortraitSize * 1.12f, .26f);

            _portrait = UIKit.Img("Face", disc.transform, null, Color.white,
                                  new Vector2(228f, 228f), new Vector2(.5f, .5f), new Vector2(0f, 4f));
            _portrait.preserveAspect = true;

            // Companion art loads into a scope, so it may not be resident when this opens
            // from anywhere but the picker. Repaint on arrival — an Image with no sprite is
            // a white disc, not a blank one. See invariant 7b.
            CompanionArt.OpenAsync(() =>
            {
                if (this && _portrait) CompanionArt.Paint(_portrait, Avatar);
            });
            CompanionArt.Paint(_portrait, Avatar);

            Tween.Bob(_portrait.rectTransform, 6f, 3.1f);
        }

        // ------------------------------------------------------------- the facts
        /// <summary>
        /// How many lines the panel will carry, so the height can be decided before anything
        /// is placed in it.
        /// </summary>
        int FactCount(CompanionOffer offer)
        {
            int facts = 1;                                     // always: how to reach it by play
            if (offer.Cost > 0) facts++;                       // what it costs
            if (offer.State == CompanionPurchaseState.TooExpensive) facts++;  // how coins arrive
            return facts;
        }

        void BuildFacts(float top, CompanionOffer offer)
        {
            int i = 0;

            // The gate first, always, because it is the half credits cannot answer. It used
            // to be described as the free route — "wakes on its own at keeper level 40" — and
            // that sentence stopped being true when the rule became level AND purchase: the
            // gate is now permission to pay rather than a second way in.
            Fact(top, i++, Art.S("Ui/ic_star"), ChestDropKind.None, Pal.Gold,
                 Avatar.UnlockLevel > 0
                     ? Loc.Format("ui.companion.by_level", Avatar.UnlockLevel)
                     : Loc.Get("ui.companion.by_level_none"));

            // A still coin, not the spinning one. Credits have no static sprite in this UI —
            // only the Ui/Coin flipbook — and animating it here was a mistake worth recording:
            // the coin turns edge-on for part of every cycle, so a 48-pixel glyph beside a
            // price spent a third of its life as a thin orange bar that reads as a broken
            // image. A reveal can afford a spinning coin because it is 114 pixels and the
            // player is watching it; a caption that has to be understood at a glance cannot.
            if (offer.Cost > 0)
            {
                var coin = Art.CoinFace();
                Fact(top, i++, coin, ChestDropKind.None,
                     coin == null ? RewardArt.Tint(ChestDropKind.Credits) : Color.white,
                     Loc.Format("ui.companion.by_coins", Compact.Number(offer.Cost)));
            }

            // Only when they are short. A player who can afford it does not need to be told
            // where coins come from, and a panel that says so anyway is advertising.
            if (offer.State == CompanionPurchaseState.TooExpensive)
                Fact(top, i, Art.S("Ui/ic_play"), ChestDropKind.None, Pal.Cream,
                     Loc.Get("ui.companion.earn_more"));
        }

        void Fact(float top, int index, Sprite sprite, ChestDropKind kind, Color tint, string line)
        {
            const float left = -ContentW * .5f;
            float textW = ContentW - FactIcon - FactGap;
            float y = top + FactStep * index + FactStep * .5f;

            var glyph = UIKit.Img("F" + index, Panel, sprite ?? Art.Disc(128), tint,
                                  Vector2.one * FactIcon, new Vector2(.5f, 1f),
                                  new Vector2(left + FactIcon * .5f, -y));
            glyph.preserveAspect = true;
            RewardArt.Glyph(glyph, kind, 10f);      // a no-op for anything but credits

            UIKit.Shrinkable(
                UIKit.Titled("L" + index, Panel, line, 27, new Color(.36f, .25f, .18f),
                             TextAnchor.MiddleLeft, new Vector2(textW, FactStep - 12f),
                             new Vector2(.5f, 1f),
                             new Vector2(left + FactIcon + FactGap + textW * .5f, -y),
                             outline: 0f, shadow: 0f, wrap: true), 19);
        }

        // ------------------------------------------------------------- the button
        /// <summary>
        /// One button, whose meaning depends on the state — and which is never dead.
        ///
        /// <para>
        /// The interesting case is <see cref="CompanionPurchaseState.TooExpensive"/>. A greyed
        /// "not enough coins" button is the shape this panel most wanted to be and the one it
        /// must not take: it is the moment a player has decided they want something, which is
        /// the single best moment in the game to offer a video, and a disabled control spends
        /// it on teaching them the feature is broken. So the button stays live and opens the
        /// coin offer instead. That is not a trick — the panel has already said, in the fact
        /// list above, exactly how far short they are.
        /// </para>
        /// <para>
        /// <see cref="CompanionPurchaseState.NotForSale"/> is the one refusal that cannot
        /// resolve, so it gets a plain acknowledgement rather than a green button that can
        /// never work — <c>AdOfferOverlay</c> makes the same call for a placement the content
        /// table does not carry.
        /// </para>
        /// </summary>
        void BuildButton(float y, CompanionOffer offer)
        {
            _buttonY = y;

            var size = new Vector2(600f, ButtonH);
            var anchor = new Vector2(.5f, 1f);
            var at = new Vector2(0f, -y);

            switch (offer.State)
            {
                case CompanionPurchaseState.Ready:
                    // The coin goes after the figure, because it is the unit on the number
                    // rather than a label on the verb — see Btn.IconTrails. It is the still
                    // face rather than the spin, for Art.CoinFace's reason.
                    _buy = UIKit.TextButton("Buy", Panel, "btn_green",
                                            Loc.Format("ui.companion.unlock_for", Compact.Number(offer.Cost)), 44,
                                            size, anchor, at, OnBuy, Art.CoinFace(), iconTrails: true);
                    break;

                case CompanionPurchaseState.TooExpensive:
                    _buy = UIKit.TextButton("Earn", Panel, "btn_blue",
                                            Loc.Get("ui.companion.get_coins"), 44,
                                            size, anchor, at, OnGetCoins, "ic_play");
                    break;

                // A closed gate gets a dead-looking button naming the level rather than the
                // coin offer, and that is the point: credits cannot open this and sending
                // somebody to the coin shelf for a companion they still could not buy is the
                // refusal HintPrompt exists to prevent, one screen over.
                case CompanionPurchaseState.LevelLocked:
                    UIKit.TextButton("Gate", Panel, "btn_blue",
                                     Loc.Format("ui.companion.locked_button", offer.RequiredLevel), 44,
                                     size, anchor, at, () => Close());
                    break;

                default:
                    UIKit.TextButton("Ok", Panel, "btn_blue", Loc.Get("ui.common.got_it"), 46,
                                     size, anchor, at, () => Close());
                    break;
            }
        }

        // ------------------------------------------------------------- painting
        /// <summary>
        /// Re-reads the offer and rebuilds the button when the state changed.
        ///
        /// <para>
        /// The button is replaced rather than relabelled, because the three states differ by
        /// sprite, glyph and action as well as caption — and a player who returns from the ad
        /// panel with enough coins must find a green "unlock" where the blue "get coins" was.
        /// Patching four properties in step is how one of them gets left behind.
        /// </para>
        /// </summary>
        void Repaint()
        {
            if (_paid || _buying || Panel == null) return;

            var offer = Profile.OfferFor(Avatar);

            if (_status) _status.text = Explain(offer);

            bool wants = offer.State == CompanionPurchaseState.Ready
                      || offer.State == CompanionPurchaseState.TooExpensive;

            // Only when the button on screen no longer matches the state. Repaint runs on
            // every balance change in the game, and rebuilding a button because a chest paid
            // out elsewhere would cancel the press the player was halfway through.
            bool showing = _buy != null && _buy;
            if (!wants || !showing) return;

            bool isBuy = string.Equals(_buy.name, "Buy", StringComparison.Ordinal);
            if (isBuy == (offer.State == CompanionPurchaseState.Ready)) return;

            var old = _buy.gameObject;
            old.SetActive(false);              // Destroy only lands at end of frame
            Destroy(old);
            _buy = null;

            BuildButton(_buttonY, offer);
        }

        /// <summary>The sentence under the button. Every branch names the real state.</summary>
        static string Explain(CompanionOffer offer)
        {
            switch (offer.State)
            {
                case CompanionPurchaseState.Ready:
                    return Loc.Format("ui.companion.balance", Compact.Number(offer.Balance));

                case CompanionPurchaseState.TooExpensive:
                    return Loc.Format("ui.companion.short", Compact.Number(offer.Shortfall), Compact.Number(offer.Balance));

                case CompanionPurchaseState.LevelLocked:
                    return Loc.Format("ui.companion.locked_level",
                                      offer.RequiredLevel, PlayerProgression.Level.Level);

                case CompanionPurchaseState.AlreadyHeld:
                    return Loc.Get("ui.companion.already");

                default:
                    return Loc.Get("ui.companion.not_for_sale");
            }
        }

        // ------------------------------------------------------------- buying
        void OnBuy()
        {
            if (_paid || _buying) return;

            _buying = true;
            bool bought;

            try
            {
                // Re-checked here rather than trusted from the button, because the balance can
                // have moved since it was painted — a spend on another screen, or a sync that
                // replaced a claim with the server's smaller figure.
                bought = WearOnBuy
                    ? Profile.TryBuyAvatar(Avatar)
                    : Progression.CompanionLedger.TryBuy(Avatar, Profile.Rank);
            }
            finally
            {
                // Cleared before either branch below, so a throw cannot leave the panel
                // permanently unable to repaint itself.
                _buying = false;
            }

            if (!bought) { Repaint(); return; }

            Paid();
        }

        /// <summary>
        /// Hands the player over to the coin offer, and comes back to a live button.
        ///
        /// The ad panel is opened on top rather than replacing this one, so declining the
        /// video returns to the companion they were looking at instead of dumping them back
        /// on the grid having lost their place.
        /// </summary>
        void OnGetCoins()
        {
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.CoinBonus;
                v.Rewarded = () => { if (this) Repaint(); };
            });
        }

        /// <summary>
        /// The purchase landed. This panel's job is over, so it gets out of the way and hands
        /// the moment to <see cref="CompanionRevealOverlay"/>.
        ///
        /// <para>
        /// It used to do the celebrating itself — the price button became "WEAR" and a few
        /// sparks fired — and that was the whole problem: a transaction panel is the wrong
        /// place for a payoff, because it is still wearing the furniture of a decision the
        /// player has already made. Closing first also means the reveal opens onto a dark
        /// screen rather than on top of a panel showing a price that has been paid.
        /// </para>
        /// <para>
        /// Nothing is reported to the screen behind from here. It repaints on
        /// <c>CompanionLedger.Changed</c>, which the purchase already raised — see
        /// <c>CompanionScreen</c> for why that is an event and not a callback.
        /// </para>
        /// </summary>
        void Paid()
        {
            _paid = true;

            Audio.Sfx("chime", .5f, 1.15f);

            Close(() => Flow.Modal<CompanionRevealOverlay>(v => v.Avatar = Avatar));
        }
    }
}
