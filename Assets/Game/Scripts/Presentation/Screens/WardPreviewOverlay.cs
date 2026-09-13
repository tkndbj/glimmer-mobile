using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a turret is, and what it looks like firing — the panel behind every tap on the loadout
    /// shelf, held or not.
    ///
    /// <para>
    /// <b>One panel for both, which is the change.</b> Tapping a held turret used to stand it on
    /// the line immediately and tapping an unheld one opened a price; so the only turrets a player
    /// could ever *see* were the ones they had already bought, and the decision the shop is asking
    /// them to make — is this worth nine thousand credits — was being made from a thumbnail. A
    /// preview costs the held case one extra tap and is worth it: standing a turret is still one
    /// tap from here, and what it buys is that the nineteen effects are visible before they are
    /// paid for rather than after.
    /// </para>
    /// <para>
    /// <b>The stage is the shipped one</b> (<see cref="WardFiringStage"/>), drawn at the sizes,
    /// anchors and per-turret scale the board uses. A preview that flattered a turret would be
    /// worse than none.
    /// </para>
    /// <para>
    /// <b>Everything here is asked about the seat that raised it.</b> A turret is bought for one
    /// colour rather than for the line (<c>WardHolding</c>), so the same panel over the same
    /// turret is a purchase on blue and an EQUIP on red — which is why <see cref="Colour"/> is
    /// handed in rather than looked up, and why the stage behind the button fires in it.
    /// </para>
    /// <para>
    /// <b>The button says the game's own words rather than a new set.</b> This panel is one tap in
    /// front of a shelf whose cells already read "Stand here" and "On the line", so a second
    /// vocabulary for the same two actions would be the panel disagreeing with what raised it. An
    /// unheld turret shows its <em>price</em> rather than the word for buying, which is
    /// <c>WardBuyOverlay</c>'s rule kept: the number is the thing the player is deciding about.
    /// </para>
    /// </summary>
    public sealed class WardPreviewOverlay : ModalView
    {
        /// <summary>
        /// The turret being shown.
        ///
        /// A property rather than a field, because <see cref="WardModel"/> is not
        /// <c>[Serializable]</c> and a public field earns a warning about serialisation that will
        /// never happen.
        /// </summary>
        public WardModel Model { get; set; }

        /// <summary>
        /// Which of the line's four colours the shelf was filling when this was raised.
        ///
        /// <b>Handed in rather than looked up</b>, because the panel has no business knowing which
        /// screen opened it — and it is why the turret here wears the colour of the cell the player
        /// just tapped, and fires in it.
        /// </summary>
        public int Colour { get; set; }

        /// <summary>Raised after anything lands, so the shelf behind can repaint.</summary>
        public Action Changed { get; set; }

        /// <summary>
        /// <b>Its height is derived from the last band rather than typed</b>, so adding one is a
        /// band and not two numbers that have to be kept in step — which is how a panel comes to
        /// draw its own button off the bottom edge.
        /// </summary>
        public const float PanelW = 880f, PanelH = ActTop + ActBand + 118f;

        /// <summary>
        /// The stage's own box, and the cell its contents are multiples of.
        ///
        /// <b>The cell is a real board's</b>, so a bolt, a flash and an impact are drawn here at
        /// the size a phone draws them on the hill — which is the whole point of showing them.
        /// </summary>
        const float StageW = 800f, StageH = 640f, StageCell = 104f;

        /// <summary>Its own scope, never the line's — see <see cref="WardFiringStage"/>.</summary>
        const string PreviewScope = "ward_preview";

        /// <summary>
        /// The four bands, stacked down the panel and stated as <em>middles</em>.
        ///
        /// <para>
        /// <b>Middles, because <c>UIKit.Box</c> pivots at centre whatever it is anchored to.</b>
        /// Written as top edges the first time, the stage's six hundred and forty units were
        /// centred where its top was meant to be and it drew straight through the description and
        /// the status line above it — measured on the built panel, which is the only thing that
        /// could have said so. It is the same arithmetic <c>render_home.py</c> had to learn about
        /// its own mirror (invariant 44d).
        /// </para>
        /// </summary>
        const float NoteTop = 120f, NoteH = 110f, NoteMid = NoteTop + NoteH * .5f;
        const float StatusTop = 240f, StatusH = 44f, StatusMid = StatusTop + StatusH * .5f;
        const float StageTop = 300f, StageMid = StageTop + StageH * .5f;

        /// <summary>
        /// What it hits for and what it can take, under the stage.
        ///
        /// <b>Below the thing firing rather than above it</b>, because the order a player reads
        /// this panel in is what it looks like, then what it does, then what it costs — and the
        /// figures are the last question, asked once the effect has been watched. The band is
        /// <see cref="WardStatBars.Height"/> and never a number typed twice.
        /// </summary>
        const float StatTop = StageTop + StageH + 24f;
        const float StatMid = StatTop + WardStatBars.Height * .5f;

        /// <summary>
        /// The upgrade ladder, under the figures it moves.
        ///
        /// <b>Directly under the bars on purpose.</b> A star is bought for what it does to those
        /// two numbers, so the thing being paid for and the thing it changes are read in one
        /// glance — a ladder above the description would be a decoration on a card instead.
        /// </summary>
        const float StarsH = 62f;
        const float StarsTop = StatTop + WardStatBars.Height + 10f;
        const float StarsMid = StarsTop + StarsH * .5f;

        /// <summary>
        /// The keys, and the band is <b>always</b> two of them tall.
        ///
        /// <para>
        /// <b>A held turret needs two answers and the panel only ever offered one</b>, which is how
        /// it came to be impossible to equip anything. A turret starts at one star, so there is
        /// always a next star to sell — and the held branch offered that star and stopped, so every
        /// turret a player owned and had not stood showed <c>UPGRADE</c> and nothing else. The
        /// loadout's whole purpose was unreachable, and every gate was green, because each of the
        /// two branches is correct and nothing anywhere asks whether their union covers the state.
        /// </para>
        /// <para>
        /// <b>The band is reserved for two and a single key is centred in it</b>, rather than the
        /// panel growing when a second is wanted. What a held turret offers changes while the panel
        /// is open — buying it makes it held, upgrading it to the top takes the star away — so a
        /// height derived from the state would be a modal that resizes under the finger.
        /// </para>
        /// </summary>
        const float ActH = 124f, ActGap = 16f, ActTop = StarsTop + StarsH + 22f;
        const float ActBand = ActH * 2f + ActGap;

        /// <summary>Where one key sits when it is the only one: the middle of the band.</summary>
        const float ActMid = ActTop + ActBand * .5f;

        /// <summary>Where the two sit when both are shown.</summary>
        const float ActUpper = ActTop + ActH * .5f;
        const float ActLower = ActTop + ActH + ActGap + ActH * .5f;

        // The panel is parchment, so it is written in ink rather than in the cream the board uses
        // — `WardBuyOverlay`'s note, and the same measured accents.
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Short = new Color(.58f, .31f, .06f);
        static readonly Color Held = new Color(.18f, .42f, .21f);

        Text _status;
        WardStatBars _bars;
        RectTransform _ladder;
        Btn _act;

        /// <summary>
        /// The equip key: the second answer a held turret owes, and the one that was missing.
        ///
        /// <b>Its own button rather than a caption the first one switches to</b>, because the two
        /// are not alternatives — a turret a player owns and has not stood can be upgraded *and*
        /// equipped, and offering one of those at a time is offering neither.
        /// </summary>
        Btn _stand;

        Image _standPill;
        Text _standLabel;
        Text _label;
        Image _coin;
        Image _pill;
        float _lift;


        protected override void Build()
        {
            if (Model == null) { Close(); return; }

            var panel = MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Model.NameKey));

            UIKit.IconButton("Close", panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            // What it does, in the roster's own words. Above the stage, because a player reads the
            // sentence once and then watches the thing fire.
            var note = UIKit.Label("Note", panel, Loc.Get(Model.NoteKey), 30, Pal.A(Ink, .84f),
                                   TextAnchor.UpperCenter, new Vector2(PanelW - 150f, NoteH),
                                   new Vector2(.5f, 1f), new Vector2(0f, -NoteMid), wrap: true);
            UIKit.Shrinkable(note, 22);

            _status = UIKit.Label("Status", panel, string.Empty, 26, Short, TextAnchor.MiddleCenter,
                                  new Vector2(PanelW - 150f, StatusH), new Vector2(.5f, 1f),
                                  new Vector2(0f, -StatusMid));

            BuildStage(panel);

            // What it hits for and what it can take. Built once: a turret's figures are its
            // model's and do not move when a balance does, so this is deliberately not in
            // `Paint`.
            // **Kept rather than drawn and forgotten.** It was a one-shot builder, so the bars
            // never carried a turret's stars and never moved when one was bought — `Paint` writes
            // them now, which is also what makes an upgrade visible the instant it lands.
            _bars = WardStatBars.Build(panel, StatMid, PanelW - 150f, Ink);

            _ladder = UIKit.Node("Ladder", panel);
            BuildButton(panel);
            Paint();

            // The ledgers' own events rather than a callback, which is `WardBuyOverlay`'s rule and
            // its reason: a balance can move while this is open (a gem shelf stacked on top), and a
            // panel that only repainted on its own taps would keep a dead button over a purse that
            // could now pay for it.
            PlayerProgression.Changed += Paint;
            WardLedger.Changed += Paint;
            WardLoadout.Changed += Paint;
            WardStarLedger.Changed += Paint;
        }

        void OnDestroy()
        {
            PlayerProgression.Changed -= Paint;
            WardLedger.Changed -= Paint;
            WardLoadout.Changed -= Paint;
            WardStarLedger.Changed -= Paint;
        }

        void BuildStage(RectTransform panel)
        {
            // A well for the stage to sit in, so the turret reads as standing somewhere rather than
            // floating on the parchment. Drawn before the stage, so the stage is over it.
            var well = UIKit.Img("Well", panel, Art.Round(30), new Color(.09f, .12f, .16f, .96f),
                                 new Vector2(StageW, StageH), new Vector2(.5f, 1f),
                                 new Vector2(0f, -StageMid));

            var edge = UIKit.Img("Edge", well.transform, Art.RoundOutline(30, 3f),
                                 Pal.A(SiegeView.TintOf(Colour), .40f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var stage = WardFiringStage.Attach(panel, new Vector2(StageW, StageH),
                                               new Vector2(.5f, 1f), new Vector2(0f, -StageMid),
                                               StageCell, PreviewScope);
            stage.Show(Model, Colour);
        }

        void BuildButton(RectTransform panel)
        {
            // Orange, which is this kit's price pill - the gold one it wore is the hub's battle
            // key and reads as a different kind of control. `Paint` swaps it for `Skins.Settled`
            // on the one state that is not an offer, so this is only the starting skin.
            _act = UIKit.Button("Act", panel, Art.S("Ui/" + Skins.Affirm), new Vector2(480f, ActH),
                                new Vector2(.5f, 1f), new Vector2(0f, -ActMid), Act);

            _pill = _act.GetComponent<Image>();

            _lift = ActH * UIKit.PillFaceLift;

            // **Orange, which is this panel's own convention for a key that is not a price.** The
            // upgrade key above it is the affirmative green, so the two read as *the thing that
            // costs* and *the thing that does not* rather than as two of the same offer.
            _stand = UIKit.Button("Stand", panel, Art.S("Ui/" + Skins.Buy),
                                  new Vector2(480f, ActH), new Vector2(.5f, 1f),
                                  new Vector2(0f, -ActLower), Stand);

            _standPill = _stand.GetComponent<Image>();

            _standLabel = UIKit.Titled("Label", _stand.transform, string.Empty, 38, Pal.Cream,
                                       TextAnchor.MiddleCenter, new Vector2(320f, 62f),
                                       new Vector2(.5f, .5f), new Vector2(0f, _lift), 0f, 3f);
            UIKit.Shrinkable(_standLabel, 22);

            _label = UIKit.Titled("Label", _act.transform, string.Empty, 38, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(320f, 62f),
                                  new Vector2(.5f, .5f), new Vector2(PriceShift, _lift), 0f, 3f);
            UIKit.Shrinkable(_label, 22);

            _coin = UIKit.Img("Coin", _act.transform, null, Color.white, new Vector2(46f, 46f),
                              new Vector2(.5f, .5f), new Vector2(-118f, _lift));
            _coin.preserveAspect = true;
        }

        /// <summary>
        /// How far the caption sits off centre to leave room for the coin beside it.
        ///
        /// <b>Only when there is a coin.</b> It was a constant, so EQUIP - which has no price and
        /// no glyph - sat eighteen units right of the middle of its own button, off centre with
        /// nothing to explain why.
        /// </summary>
        const float PriceShift = 18f;

        /// <summary>
        /// Repaints the pill.
        ///
        /// <b><c>"Ui/"</c> because a skin name is a name, not an address.</b> `UIKit.Button` adds
        /// the folder for its callers, so the bare names in <see cref="Skins"/> normally only ever
        /// reach <c>Art.S</c> through it - and a name handed over without the prefix has no
        /// location, so it resolves to nothing and the button draws as a **white rectangle**
        /// rather than as a missing decoration (invariant 7b). `LeaderboardScreen.Skin` and
        /// `StreakScreen.Skin` write it the same way, for the same reason and after the same bug.
        /// </summary>
        void Skin(string skin)
        {
            if (_pill != null) _pill.sprite = Art.S("Ui/" + skin);
        }

        /// <summary>
        /// The glyph beside a price.
        ///
        /// <b>The coin is a reel, not a sprite</b> — credits have no still picture in this UI,
        /// only the <c>Ui/Coin</c> flipbook, so clearing the sprite for a credit price would leave
        /// an <c>Image</c> with none, which is a white rectangle rather than a coin (invariant 7b).
        /// Said once because two branches draw a price now: buying the turret, and buying its next
        /// star.
        /// </summary>
        void Coin(bool gems = false)
        {
            if (_coin == null) return;

            if (gems) _coin.sprite = Art.S("Ui/ic_gem");
            else Flipbook.Attach(_coin, "Ui/Coin", 11f);
        }

        /// <summary>
        /// The upgrade ladder, rebuilt on every repaint.
        ///
        /// <b>Rebuilt rather than rebound</b>, which is the exception invariant 16k allows: it is
        /// five images with no animation and no state of its own, so there is nothing for a bind
        /// to restart and nothing for a player to see snap back. Anything that breathed or played
        /// a reel would have to be adopted instead.
        /// </summary>
        /// <summary>The turret this panel is about, as it stands on this seat.</summary>
        WardBuild Stood => WardStarLedger.BuildOf(Model, WardLine.Colours[Colour]);

        void PaintLadder()
        {
            if (_ladder == null) return;

            for (int i = _ladder.childCount - 1; i >= 0; i--)
                Destroy(_ladder.GetChild(i).gameObject);

            // Only for a turret this seat holds: a ladder over one that is still for sale reads as
            // a promise about what buying it gives you.
            if (!WardLedger.IsHeld(Model, WardLine.Colours[Colour])) return;

            WardStarRow.Build(_ladder, new Vector2(0f, -StarsMid),
                              WardStarLedger.StarsOf(Model, WardLine.Colours[Colour]), 44f);
        }

        /// <summary>
        /// A turret's own name, from its id.
        ///
        /// Only ever asked about <see cref="WardOffer.Needs"/>, which carries an id because a name
        /// is a loc key and Domain may not reach for one. An id the roster no longer knows draws
        /// an empty name rather than the id itself, because an id is not player-facing text
        /// (invariant 6).
        /// </summary>
        static string NameOf(string id)
        {
            var model = WardLedger.Catalog.Find(id);
            return model == null ? string.Empty : Loc.Get(model.NameKey);
        }

        /// <summary>Whether this turret is the one already standing on the colour that raised this.</summary>
        bool Standing
        {
            get
            {
                var stood = WardLoadout.Line.At(Colour);
                return stood != null && stood.Id == Model.Id;
            }
        }

        void Paint()
        {
            if (this == null || Model == null || _act == null) return;

            var offer = WardLedger.OfferFor(Model, Colour, PlayerProgression.Level.Level);
            var rise = WardUpgrade.OfferFor(Model, Colour);

            PaintLadder();

            // The figures a player actually plays with, stars and all — and rewritten on every
            // repaint, so buying a star moves them where the player is looking.
            _bars?.Set(Stood, WardLedger.Catalog);

            _coin.enabled = false;
            Flipbook.Detach(_coin);

            // Reset before the switch rather than in three of its four branches: `Paint` runs
            // again after a purchase and after a turret is stood, so a skin left from the last
            // pass is a live-looking key on the next turret opened.
            //
            // **The reset is the refusals' colour, not the affirmative's.** The three walls and
            // the EQUIP key fall through to it, and a green wall reads as a key that will do
            // something — so the branches that really do something say so (`Skins.Affirm`) and
            // everything else keeps the orange it already had.
            Skin(Skins.Buy);

            bool held = offer.State == WardPurchaseState.AlreadyHeld;

            bool rises = held && (rise.State == WardUpgradeState.Ready
                                  || rise.State == WardUpgradeState.Short);

            // **Which keys are offered is a rule with a name**, swept over every state a turret can
            // be in (`WardPreviewTests`) — because what went wrong here was not a wrong branch, it
            // was two correct branches whose union left a state with no answer at all.
            var keys = WardPreviewKeys.For(held, Standing, rises);

            // **The equip key is the second answer, and it is shown for every turret the player
            // owns.** It is what says where this turret already stands, and it is the only way to
            // move it — a held turret with a star left to sell used to offer the star and nothing
            // else, which made the loadout unreachable.
            //
            // EQUIPPED pays nothing, moves nothing and only closes the panel, so it wears the one
            // pill on this panel that is not an offer; EQUIP wears the orange, which keeps the
            // affirmative green for the thing that costs.
            if (_stand != null)
            {
                _stand.gameObject.SetActive(keys.Lower);

                if (keys.Lower)
                {
                    _standLabel.text = Loc.Get(keys.Equipped ? "ui.loadout.standing"
                                                             : "ui.loadout.stand");

                    if (_standPill != null)
                        _standPill.sprite =
                            Art.S("Ui/" + (keys.Equipped ? Skins.Settled : Skins.Buy));
                }
            }

            _act.gameObject.SetActive(keys.Upper);

            // A lone key takes the middle of the band rather than sitting over a gap - see
            // `ActBand`, which is always two keys tall so the panel never resizes under a finger.
            if (keys.Upper)
                _act.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(0f, -(keys.Alone ? ActMid : ActUpper));

            if (_stand != null)
                _stand.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(0f, -(keys.Alone ? ActMid : ActLower));

            switch (offer.State)
            {
                case WardPurchaseState.AlreadyHeld:
                    // **A held turret's upper key sells the next star**, and the equip key under it
                    // says where the turret stands. The panel is one tap from the shelf and the
                    // shelf already says which turret is on the line, so the useful thing to offer
                    // somebody looking at a turret they own is both of the things they can do to
                    // it.
                    if (rises)
                    {
                        // **The word rather than the price**, because this button no longer buys
                        // anything: it opens the panel that shows what the star is worth, and a
                        // key that reads as a price while it only opens a door is a key that has
                        // charged somebody by the time they find out.
                        _status.text = Loc.Format("ui.loadout.upgrade_note", rise.Star);
                        _status.color = Held;

                        _label.text = Loc.Get("ui.loadout.upgrade");
                        Skin(Skins.Affirm);
                        break;
                    }

                    _status.text = Standing ? Loc.Get("ui.loadout.held_one") : string.Empty;
                    _status.color = Held;
                    break;

                case WardPurchaseState.Sealed:
                    // **The rung the shelf is standing on, named**, because it is the one refusal
                    // of the three that a player can act on this minute — and the button says the
                    // same thing rather than a price they cannot pay yet, since a live-looking key
                    // over a wall is worse than a plain one.
                    _status.text = Loc.Format("ui.loadout.sealed_note", NameOf(offer.Needs));
                    _status.color = Short;
                    _label.text = Loc.Get("ui.loadout.sealed");
                    break;

                case WardPurchaseState.LevelLocked:
                    _status.text = Loc.Format("ui.loadout.level_note", offer.RequiredLevel);
                    _status.color = Short;
                    _label.text = Loc.Format("ui.loadout.level", offer.RequiredLevel);
                    break;

                case WardPurchaseState.NotForSale:
                    _status.text = Loc.Get("ui.loadout.chest_only");
                    _status.color = Short;
                    _label.text = Loc.Get("ui.loadout.locked");
                    break;

                default:
                    bool gems = offer.Currency == Currency.Gems;

                    _status.text = offer.Shortfall > 0
                        ? Loc.Format(gems ? "ui.shop.short_gems" : "ui.shop.short_coins",
                                     offer.Shortfall)
                        : string.Empty;

                    _status.color = Short;
                    _label.text = offer.Cost.ToString("N0");
                    _coin.enabled = true;

                    Skin(Skins.Affirm);
                    Coin(gems);
                    break;
            }

            // Centred unless there is a coin to leave room for - see `PriceShift`.
            _label.rectTransform.anchoredPosition =
                new Vector2(_coin.enabled ? PriceShift : 0f, _lift);
        }

        /// <summary>
        /// Stands this turret on its colour's seat, or closes when it is already standing.
        ///
        /// <b>Its own handler rather than a branch of the other one.</b> Equipping and upgrading
        /// are two things a player can do to one turret, not two readings of one button — which is
        /// the whole of what went wrong: the upgrade branch returned first, so a turret with a star
        /// left to sell could never be stood, and every turret starts with one.
        /// </summary>
        void Stand()
        {
            if (Standing) { Close(); return; }

            // **A mechanism rather than a bell**, which is the shelf's own note: standing a turret
            // is an action a player takes several times in a row and one tap to undo, where
            // `unlock` is what an earning sounds like.
            if (WardLoadout.Choose(WardLine.Colours[Colour], Model.Id))
            {
                Audio.Sfx("stand", .5f);
                Changed?.Invoke();
                Close(quiet: true);
            }
            else Audio.Sfx("blocked", .4f);
        }

        /// <summary>
        /// The upper button: buy the next star, pay for the turret, or say why neither is on offer.
        ///
        /// <b>A short balance is answered rather than refused</b> — the gem shelf stacks over this
        /// panel and steps out when the gems land, which is <c>GemShopOverlay</c>'s own rule. A
        /// credit shortfall has no shelf to open, so it says the number and nothing else: credits
        /// are earned by playing and there is nothing here to sell.
        /// </summary>
        void Act()
        {
            var offer = WardLedger.OfferFor(Model, Colour, PlayerProgression.Level.Level);

            if (offer.State == WardPurchaseState.AlreadyHeld)
            {
                // **This key only ever sells the star**, because that is the only thing it is ever
                // drawn as: standing the turret is the key underneath it now (`Stand`), and a
                // button that did one thing or the other depending on state is how the equip path
                // came to be unreachable in the first place.
                var rise = WardUpgrade.OfferFor(Model, Colour);

                if (rise.State == WardUpgradeState.Ready || rise.State == WardUpgradeState.Short)
                {
                    // **A panel of its own, because an upgrade is a decision with a number on
                    // either side of it.** What a star costs is one figure and what it buys is
                    // two more, and a player deciding needs all three at once — on this panel they
                    // would be a fourth thing under a stage, a description and a status line.
                    var turret = Model;
                    int seat = Colour;
                    var after = Changed;

                    Flow.Modal<WardUpgradeOverlay>(v =>
                    {
                        v.Model = turret;
                        v.Colour = seat;
                        v.Changed = after;
                    });

                    return;
                }

                // Nothing left to sell, so this key is not drawn at all - see `Paint`. Closing is
                // the honest answer to a tap that reached it anyway.
                Close();
                return;
            }

            if (offer.State == WardPurchaseState.NotForSale
                || offer.State == WardPurchaseState.LevelLocked
                || offer.State == WardPurchaseState.Sealed)
            {
                Close();
                return;
            }

            if (offer.Shortfall > 0)
            {
                if (offer.Currency == Currency.Gems) Flow.Modal<GemShopOverlay>();
                else Audio.Sfx("blocked", .45f);

                return;
            }

            if (!WardLedger.TryBuy(Model, Colour, PlayerProgression.Level.Level))
            {
                Audio.Sfx("blocked", .45f);
                return;
            }

            // **Bought, and then it gets out of the way.** This panel used to stay open and
            // celebrate in place — the price pill became EQUIP, waves left the turret, confetti
            // fell — on the argument that what was paid for is the thing firing behind the
            // button. Played, that read as a flourish on a shop panel rather than as an unlock,
            // which is the note `CompanionUnlockOverlay` already carries about its own history:
            // a transaction panel is the wrong place for a payoff, because it is still wearing
            // the furniture of a decision the player has already made. `WardRevealOverlay` is
            // the payoff, and it ends on EQUIP so nobody has to come back here for it.
            //
            // The coin is the money leaving; what the turret arriving sounds like belongs to the
            // ceremony about to play it — `HomesteadBuyOverlay`'s split, for its reason.
            Audio.Sfx("coin", .6f);

            Changed?.Invoke();

            var model = Model;
            int colour = Colour;
            var changed = Changed;

            Close(() =>
            {
                // Nothing to give up by hand any more, and the deletion is the point. This
                // used to release the preview's scope here, by name, because `Object.Destroy`
                // lands at the end of the frame — so this panel's own release ran *after* the
                // reveal below had already asked for the same turret, and a scope claimed
                // nothing another scope owned, so the reveal watched its art freed underneath
                // it. Counting makes the ordering irrelevant: the reveal takes its own hold
                // before this panel lets go, so the count never reaches nought.
                Flow.Modal<WardRevealOverlay>(v =>
                {
                    v.Model = model;
                    v.Colour = colour;
                    v.Changed = changed;
                });
            }, quiet: true);
        }
    }
}
