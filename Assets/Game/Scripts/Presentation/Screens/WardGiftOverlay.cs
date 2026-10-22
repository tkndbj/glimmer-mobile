using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A turret handed to a new player by a first step (<c>FirstStepsLedger</c>), delivered: it drops
    /// out of the dark into a pool of light and slams into the floor (<see cref="GiftStage"/>), the
    /// dust rolls away, and it stands there glowing with its name and what it does under it - and
    /// one key, EQUIP, which stands it on its seat so the very next board has it.
    ///
    /// <para>
    /// <b>A report, never the grant.</b> The turret was handed over at the clear, in the run's own
    /// save; this panel only shows it. So the back key - or a screen change - loses nothing but
    /// the equip, which the loadout can still do.
    /// </para>
    /// <para>
    /// <b>It resumes what the player tapped.</b> Raised from the victory panel's exits, it carries
    /// the tap it interrupted (<see cref="Then"/>) and runs it exactly once however it ends, which
    /// is <c>AccountOverlay.Then</c>'s contract: a ceremony that ate a NEXT would be a button that
    /// did nothing.
    /// </para>
    /// </summary>
    public sealed class WardGiftOverlay : ModalView
    {
        /// <summary>The turret handed over. A property for <c>WardRevealOverlay.Model</c>'s reason.</summary>
        public WardModel Model { get; set; }

        /// <summary>The seat it was handed over on, as a colour index (0..3).</summary>
        public int Colour { get; set; }

        /// <summary>What to resume when this panel is gone, whichever way it goes. Null from the hub.</summary>
        public Action Then { get; set; }

        /// <summary>Where the floor is, in the room's own units (centre origin).</summary>
        internal const float GroundY = -10f;

        /// <summary>The turret's box: a board's turret at the siege's own body proportions, drawn large.</summary>
        internal const float TurretTall = 440f;
        internal static readonly float TurretWide = TurretTall * SiegeView.BodyWide / SiegeView.BodyTall;

        /// <summary>
        /// Where each band sits, as a <em>middle</em> (<c>UIKit.Box</c> pivots at centre). The room is
        /// centred, so these run up and down from the canvas middle; the shortest canvas the game is
        /// drawn on keeps about 945 units either side, and the lowest edge here is the key's at -665.
        /// Title 646..554, caption 491..449, turret 430..-10 standing on the floor, name -95..-185,
        /// note -230..-330, key -515..-665.
        /// </summary>
        internal const float TitleY = 600f, CaptionY = 470f, NameY = -140f, NoteY = -280f, KeyY = -590f;
        internal const float TitleH = 92f, CaptionH = 42f, NameH = 90f, NoteH = 100f, KeyH = 150f;

        const string Scope = "ward_gift";

        GiftStage _stage;
        RectTransform _body;
        Image _turret, _shadow;
        Text _title, _caption, _name, _note;
        Btn _equip;
        AssetHold _art;
        float _openedAt;
        bool _begun, _equipped, _continued;

        static readonly Color Ink = Pal.Hex("#FFF3DC");

        Vector2 Rest => new Vector2(0f, GroundY + TurretTall * .5f);

        protected override void Build()
        {
            _openedAt = Time.unscaledTime;
            var tint = SiegeView.TintOf(Colour);

            _stage = GiftStage.Build(Content, GroundY, GiftTempo.TurretHush);
            Panel = _stage.Props;   // what ModalView.Close() scales out

            // The words. Built now and hidden, revealed by the beats - never built by them, so a
            // close at any moment has nothing half made to tear down.
            _title = Reveal(UIKit.Titled("Title", Content, Loc.Get("ui.steps.gift_title"), 76, Pal.Gold,
                                         TextAnchor.MiddleCenter, new Vector2(960f, TitleH),
                                         new Vector2(.5f, .5f), new Vector2(0f, TitleY), 4f, 4f), 40);
            _caption = Reveal(UIKit.Titled("Caption", Content, Loc.Get("ui.steps.turret_title"), 36, Pal.A(Ink, .9f),
                                           TextAnchor.MiddleCenter, new Vector2(900f, CaptionH),
                                           new Vector2(.5f, .5f), new Vector2(0f, CaptionY), 2f, 2f), 22);
            _name = Reveal(UIKit.Titled("Name", Content, Loc.Get(Model.NameKey), 72, Pal.Lift(tint, .35f),
                                        TextAnchor.MiddleCenter, new Vector2(900f, NameH),
                                        new Vector2(.5f, .5f), new Vector2(0f, NameY), 4f, 4f), 36);
            _note = Reveal(UIKit.Titled("Note", Content, Loc.Get(Model.NoteKey), 34, Ink,
                                        TextAnchor.MiddleCenter, new Vector2(860f, NoteH),
                                        new Vector2(.5f, .5f), new Vector2(0f, NoteY), 2f, 2f, wrap: true), 22);

            _equip = UIKit.TextButton("Equip", Content, "btn_green", Loc.Get("ui.loadout.stand"), 56,
                                      new Vector2(560f, KeyH), new Vector2(.5f, .5f), new Vector2(0f, KeyY), Equip);
            UIKit.OneLine(_equip, 30);
            _equip.transform.localScale = Vector3.zero;

            // The turret, and the shadow it falls into: a dark spot on the floor that grows and
            // hardens as the turret comes down, which is what says *where* it will land.
            _shadow = UIKit.Img("Shadow", _stage.Props, Art.Glow(64, 2.4f), new Color(0f, 0f, 0f, 0f),
                                new Vector2(TurretWide * 1.25f, 70f), new Vector2(.5f, .5f), new Vector2(0f, GroundY));
            _shadow.raycastTarget = false;

            // Hidden above the screen until its fall; GiftStage.Drop pivots it at the foot.
            _body = UIKit.Box("Turret", _stage.Props, new Vector2(TurretWide, TurretTall),
                              new Vector2(.5f, .5f), new Vector2(0f, GroundY + 1500f));
            UIKit.Group(_body).alpha = 0f;

            _turret = UIKit.Img("Body", _body, null, Color.white);
            _turret.rectTransform.anchorMin = Vector2.zero;
            _turret.rectTransform.anchorMax = Vector2.one;
            _turret.rectTransform.offsetMin = _turret.rectTransform.offsetMax = Vector2.zero;
            _turret.preserveAspect = true;
            _turret.raycastTarget = false;
            _turret.enabled = false;   // an Image with no sprite is a white rectangle (7b)

            Audio.Duck(.4f, 3f);
            Load();
        }

        static Text Reveal(Text text, int floor)
        {
            UIKit.Shrinkable(text, floor);
            text.transform.localScale = Vector3.zero;
            return text;
        }

        // ------------------------------------------------------------- art
        /// <summary>
        /// The turret's body in its seat's colour, held for as long as this panel stands. The
        /// ceremony waits for it - a slam with nothing in it is no slam - but never for long: past
        /// <see cref="LoadPatience"/> it goes ahead, and the name and the key still arrive.
        /// </summary>
        void Load()
        {
            string address = AssetManifest.WardArt(Model, Colour);

            Tween.After(LoadPatience, Begin, this);

            _art = AssetLibrary.Hold(Scope);
            Lifeline.Of(this)?.Run(async token =>
            {
                await _art.LoadAsync(new List<AssetRequest> { AssetRequest.Sprite(address) }, null, token);
                if (this == null || _turret == null) return;

                var sprite = AssetLibrary.Sprite(address);
                _turret.sprite = sprite;
                _turret.enabled = sprite != null;
                Begin();
            }, "WardGiftOverlay.Load");
        }

        /// <summary>The longest the ceremony waits for the turret's art before it goes ahead.</summary>
        const float LoadPatience = 1.2f;

        // ------------------------------------------------------------- the beats
        /// <summary>
        /// Starts the delivery. Every beat is <see cref="GiftTempo"/>'s, counted from the panel's
        /// arrival; art that arrived late pushes the whole sequence back by its lateness, so the
        /// beats keep their spacing and nothing is skipped.
        /// </summary>
        void Begin()
        {
            if (_begun || this == null) return;
            _begun = true;

            var tint = SiegeView.TintOf(Colour);
            float late = Mathf.Max(0f, Time.unscaledTime - _openedAt - GiftTempo.TurretDrop);

            float At(float beat) => Mathf.Max(0f, beat + late - (Time.unscaledTime - _openedAt));

            Tween.After(At(GiftTempo.TurretHush * .6f), () => Tween.Pop(_title.transform, 0f, .45f), this);
            Tween.After(At(GiftTempo.TurretDrop), () => Audio.SfxVaried("whoosh", .7f), this);

            // The shadow gathers under the falling turret.
            Tween.Run(GiftTempo.TurretFall, Ease.InQuad, t =>
            {
                if (_shadow) _shadow.color = new Color(0f, 0f, 0f, .55f * t);
                if (_shadow) _shadow.rectTransform.localScale = new Vector3(Mathf.Lerp(.35f, 1f, t), Mathf.Lerp(.35f, 1f, t), 1f);
            }, this, "shadow").Delay(At(GiftTempo.TurretDrop));

            GiftStage.Drop(_body, new Vector2(0f, GroundY), At(GiftTempo.TurretDrop),
                           GiftTempo.TurretFall, GiftTempo.TurretSettle, this);

            Tween.After(At(GiftTempo.TurretImpact), () =>
            {
                if (_stage == null) return;
                _stage.Impact(new Vector2(0f, GroundY), tint);
            }, this);

            Tween.After(At(GiftTempo.TurretImpact + .30f), () =>
            {
                if (_stage == null) return;
                _stage.Lift();
                _stage.Glow(Rest, tint, TurretTall);
                Tween.Pop(_caption.transform, 0f, .4f);
            }, this);

            Tween.After(At(GiftTempo.TurretName), () =>
            {
                Tween.Pop(_name.transform, 0f, .45f);
                Audio.Sfx("unlock", .7f);
            }, this);

            Tween.After(At(GiftTempo.TurretNote), () => Tween.Pop(_note.transform, 0f, .4f), this);

            Tween.After(At(GiftTempo.TurretKey), () =>
            {
                if (!_equip) return;
                Tween.Pop(_equip.transform, 0f, .45f);
                UIKit.Halo(_equip.transform, Pal.Mint, 680f, .26f);
                Sheen.Attach((RectTransform)_equip.transform, 3.2f);
            }, this);
        }

        // ------------------------------------------------------------- the key
        /// <summary>
        /// Stands the turret on its seat and steps aside. The choice is the loadout's own
        /// (<see cref="WardLoadout.Choose"/>) - stamped, saved and merged by recency like any
        /// arrangement the player makes - so the next board stands it and the loadout shows it.
        /// </summary>
        void Equip()
        {
            if (_equipped || Model == null) return;
            _equipped = true;   // the latch, not Interactable: a key greying out on its own press reads as refused

            char seat = WardLine.Colours[Colour < 0 || Colour >= WardLine.Colours.Length ? 0 : Colour];
            bool changed = WardLoadout.Choose(seat, Model.Id);

            Telemetry.Track("first_step_equipped", "ward", Model.Id, "colour", seat.ToString(), "changed", changed);

            Audio.Sfx("stand", .9f);
            if (_body) Tween.Punch(_body, .16f, .3f);
            _stage?.Light.Shock(Art.Ring(256, 6f), new Vector2(0f, GroundY), Pal.A(Pal.Mint, .8f), 60f, 700f, .5f, 0f, .30f);

            Tween.After(.32f, () => Close(), this);
        }

        public override bool OnBack()
        {
            if (!_equipped) Close();
            return true;
        }

        /// <summary>
        /// Resumes what was interrupted, exactly once, however this panel ended - the key, the back
        /// key, or the screen under it being replaced. <c>AccountOverlay.OnDestroy</c>'s latch.
        /// </summary>
        void OnDestroy()
        {
            _art?.Dispose();
            _art = null;

            if (_continued) return;
            _continued = true;

            var then = Then;
            Then = null;
            then?.Invoke();
        }
    }
}
