using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What the next star costs and what it buys: one price, two figures, and the gain beside
    /// each.
    ///
    /// <para>
    /// <b>Its own panel because an upgrade is a decision with a number on either side of it.</b>
    /// What a star costs is one figure and what it adds is two more, and somebody deciding needs
    /// all three at once — on the preview panel they would be a fourth thing under a stage, a
    /// description and a status line, which is how a shop comes to sell something nobody
    /// understood. It is <c>HomesteadBuyOverlay</c>'s split: browse on one screen, decide on
    /// another.
    /// </para>
    /// <para>
    /// <b>The bars are drawn where the turret <em>is</em>, with the gain marked beside them.</b> A
    /// panel that drew them at the upgraded length would be showing a player something they have
    /// not paid for; a panel that showed only the gain would be asking them to do arithmetic. Both
    /// at once is the one reading that needs neither.
    /// </para>
    /// <para>
    /// <b>It never celebrates in place.</b> A transaction panel is the wrong place for a payoff —
    /// <c>WardPreviewOverlay</c>'s own note about why it stopped doing exactly that — so this one
    /// closes and <see cref="WardUpgradeRevealOverlay"/> is the ceremony.
    /// </para>
    /// </summary>
    public sealed class WardUpgradeOverlay : ModalView
    {
        /// <summary>Which turret. Never null by the time this is raised.</summary>
        public WardModel Model { get; set; }

        /// <summary>Which seat of the line, as a colour index.</summary>
        public int Colour { get; set; }

        /// <summary>Raised after a star lands, so the shelf behind repaints.</summary>
        public Action Changed { get; set; }

        const float PanelW = 880f, PanelH = 980f;

        /// <summary>
        /// The bands, stated as middles because <c>UIKit.Box</c> pivots at centre whatever it is
        /// anchored to — <c>WardPreviewOverlay</c>'s own recorded trap.
        /// </summary>
        const float StarsTop = 132f, StarsH = 86f, StarsMid = StarsTop + StarsH * .5f;
        const float NoteTop = 238f, NoteH = 62f, NoteMid = NoteTop + NoteH * .5f;
        const float BarsTop = 328f, BarsMid = BarsTop + WardStatBars.Height * .5f;
        const float StatusTop = BarsTop + WardStatBars.Height + 24f, StatusH = 44f;
        const float StatusMid = StatusTop + StatusH * .5f;
        const float ActH = 124f, ActTop = 700f, ActMid = ActTop + ActH * .5f;

        // The parchment palette the preview panel writes in, so the two read as one room.
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Short = new Color(.58f, .31f, .06f);

        /// <summary>
        /// What a refused press is written in, and how long it stands before the line goes back
        /// to saying how far short the purse is.
        ///
        /// <b>Red rather than the standing line's amber</b>, because the two are answering
        /// different questions: the amber line is standing information a player can read at their
        /// leisure, and this is an answer to something they just did. Deeper than
        /// <c>Pal.Rose</c> for the parchment's sake, which is the same reading
        /// <see cref="Ink"/> and <see cref="Short"/> are picked against.
        /// </summary>
        static readonly Color Deny = new Color(.72f, .17f, .13f);
        const float DenyFor = 2.2f;

        WardStatBars _bars;
        RectTransform _ladder;
        Text _note, _status, _label;
        Btn _act;
        Image _coin, _pill;
        float _lift;

        char Seat => WardLine.Colours[Colour < 0 || Colour >= WardLine.Colours.Length ? 0 : Colour];

        WardBuild Stood => WardStarLedger.BuildOf(Model, Seat);

        protected override void Build()
        {
            if (Model == null) { Close(); return; }

            var panel = MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Model.NameKey));

            UIKit.IconButton("Close", panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            _ladder = UIKit.Node("Ladder", panel);

            _note = UIKit.Label("Note", panel, string.Empty, 30, Pal.A(Ink, .84f),
                                TextAnchor.MiddleCenter, new Vector2(PanelW - 150f, NoteH),
                                new Vector2(.5f, 1f), new Vector2(0f, -NoteMid));

            _bars = WardStatBars.Build(panel, BarsMid, PanelW - 150f, Ink, gains: true);

            _status = UIKit.Label("Status", panel, string.Empty, 26, Short,
                                  TextAnchor.MiddleCenter, new Vector2(PanelW - 150f, StatusH),
                                  new Vector2(.5f, 1f), new Vector2(0f, -StatusMid));

            BuildButton(panel);
            Paint();

            // A balance can move while this is open — a sync, a reward landing — so the key is
            // repainted from the ledgers rather than latched when the panel was drawn.
            PlayerProgression.Changed += Paint;
            WardStarLedger.Changed += Paint;
        }

        void OnDestroy()
        {
            PlayerProgression.Changed -= Paint;
            WardStarLedger.Changed -= Paint;
        }

        void BuildButton(RectTransform panel)
        {
            _act = UIKit.Button("Act", panel, Art.S("Ui/" + Skins.Affirm), new Vector2(480f, ActH),
                                new Vector2(.5f, 1f), new Vector2(0f, -ActMid), Act);

            _pill = _act.GetComponent<Image>();
            _lift = ActH * UIKit.PillFaceLift;

            _label = UIKit.Titled("Label", _act.transform, string.Empty, 38, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(320f, 62f),
                                  new Vector2(.5f, .5f), new Vector2(18f, _lift), 0f, 3f);
            UIKit.Shrinkable(_label, 22);

            _coin = UIKit.Img("Coin", _act.transform, null, Color.white, new Vector2(46f, 46f),
                              new Vector2(.5f, .5f), new Vector2(-118f, _lift));
            _coin.preserveAspect = true;
        }

        void Paint()
        {
            if (this == null || Model == null || _act == null) return;

            var now = Stood;
            var offer = WardUpgrade.OfferFor(Model, Seat);

            PaintLadder(now.Stars);

            if (!now.CanRise)
            {
                // Topped out: the bars say where it stands and the key closes. Nothing here is a
                // price, so nothing here wears the price pill (invariant 42a's `Skins.Settled`).
                _bars.Set(now, WardLedger.Catalog);

                _note.text = Loc.Get("ui.loadout.upgrade_top");
                _status.text = string.Empty;
                _status.color = Short;

                _label.text = Loc.Get("ui.ok.done");
                _coin.enabled = false;
                _pill.sprite = Art.S("Ui/" + Skins.Settled);

                Place();
                return;
            }

            var next = now.Risen();

            _bars.Offer(now, next, WardLedger.Catalog);

            _note.text = Loc.Format("ui.loadout.upgrade_to", next.Stars);

            // Repainting always restores the standing colour, so a refusal fading back to the
            // shortfall — and a sync landing the credits mid-refusal — both leave one line.
            _status.color = Short;
            _status.text = offer.Shortfall > 0
                ? Loc.Format("ui.shop.short_coins", offer.Shortfall)
                : string.Empty;

            _label.text = offer.Cost.ToString("N0");
            _coin.enabled = true;
            _pill.sprite = Art.S("Ui/" + Skins.Affirm);

            // **The coin is a reel, not a sprite** — credits have no still picture in this UI, only
            // the `Ui/Coin` flipbook, so an `Image` with the sprite cleared is a white rectangle
            // rather than a coin (invariant 7b).
            Flipbook.Attach(_coin, "Ui/Coin", 11f);

            Place();
        }

        /// <summary>Centred unless there is a coin to leave room for.</summary>
        void Place()
            => _label.rectTransform.anchoredPosition =
                   new Vector2(_coin.enabled ? 18f : 0f, _lift);

        /// <summary>
        /// Says no to a press there are not the credits for.
        ///
        /// <para>
        /// <b>A press needs an answer of its own, even when the panel was already saying it.</b>
        /// The status line carries the shortfall from the moment the panel opens, which is
        /// standing information: a player who presses the key anyway has asked a question, and a
        /// sound with nothing moving on screen reads as the button being broken rather than as a
        /// refusal. So the line becomes the blunt sentence, in red, punched — and the key shakes,
        /// because the key is where the finger is and the line is not.
        /// </para>
        /// <para>
        /// <b>It goes back to the shortfall on its own</b>, which is the half that matters: the
        /// number is what a player needs in order to know how much playing is left to do, and a
        /// panel left reading "not enough" for ever would have swapped information for a scolding.
        /// The wait is a tween on a named channel rather than a bare <c>Tween.After</c>, so a
        /// second press restarts it instead of queueing a second repaint behind the first.
        /// </para>
        /// </summary>
        void Refuse()
        {
            Audio.Sfx("blocked", .5f);

            if (_act != null) Tween.Shake((RectTransform)_act.transform, 16f, .34f);

            if (_status == null) return;

            _status.text = Loc.Get("ui.shop.no_coins");
            _status.color = Deny;
            Tween.Punch(_status.transform, .30f, .38f);

            Tween.KillChannel(this, "deny");
            Tween.Run(DenyFor, Ease.Linear, _ => { }, this, "deny").OnDone(Paint);
        }

        void PaintLadder(int stars)
        {
            if (_ladder == null) return;

            for (int i = _ladder.childCount - 1; i >= 0; i--)
                Destroy(_ladder.GetChild(i).gameObject);

            WardStarRow.Build(_ladder, new Vector2(0f, -StarsMid), stars, 58f);
        }

        void Act()
        {
            var offer = WardUpgrade.OfferFor(Model, Seat);

            if (!Stood.CanRise) { Close(); return; }

            if (!offer.CanBuy)
            {
                // A credit shortfall has no shelf to open — credits are earned by playing and
                // there is nothing here to sell — so it says so and refuses in place.
                Refuse();
                return;
            }

            var was = Stood;

            if (!WardUpgrade.TryBuy(Model, Seat)) return;

            // The coin is the money leaving; what the turret getting stronger sounds like belongs
            // to the ceremony about to play it — `WardPreviewOverlay`'s split, for its reason.
            Audio.Sfx("coin", .6f);

            var model = Model;
            int colour = Colour;
            var changed = Changed;
            var risen = Stood;

            changed?.Invoke();

            Close(() => Flow.Modal<WardUpgradeRevealOverlay>(v =>
            {
                v.Model = model;
                v.Colour = colour;
                v.Was = was;
                v.Now = risen;
                v.Changed = changed;
            }), quiet: true);
        }
    }
}
