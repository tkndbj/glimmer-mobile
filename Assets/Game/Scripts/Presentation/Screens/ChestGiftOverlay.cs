using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The chests a first step hands a new player (<c>FirstStepsLedger</c>), delivered: they drop
    /// out of the dark one after another and slam into the floor (<see cref="GiftStage"/>), the dust
    /// rolls away, and they stand in a pool of light under their name with one key - OPEN - which
    /// opens them one at a time through the game's one chest ceremony (<see cref="ChestOverlay"/>).
    ///
    /// <para>
    /// <b>The purse rides on this panel.</b> A chest pays into the readouts of the screen under it,
    /// and over a run's board there are none - so this panel draws the hub's three and registers
    /// them over the screen's (<see cref="ResourceSlots.RegisterOver"/>, watched by
    /// <see cref="WalletWatch"/>, 44j): every coin a chest throws lands somewhere the player is
    /// looking, and the screen gets its own row back the moment this goes.
    /// </para>
    /// <para>
    /// <b>The chest is opened by its claim, never by this panel</b> (<see cref="ChestClaim.ForFirstStep"/>):
    /// the ledger takes it before it pays, so a process killed mid-ceremony comes back with that
    /// chest gone and its currency still claimed. A chest left shut - the back key, a screen change
    /// - waits, and a calm hub raises this again for it (<see cref="FirstStepsMoment.Tick"/>).
    /// </para>
    /// </summary>
    public sealed class ChestGiftOverlay : ModalView
    {
        /// <summary>The chest step being delivered. A property, for <c>WardRevealOverlay.Model</c>'s reason.</summary>
        public FirstStep Step { get; set; }

        /// <summary>What to resume when this panel is gone, whichever way it goes. Null from the hub.</summary>
        public Action Then { get; set; }

        internal const float GroundY = -30f;

        /// <summary>
        /// A closed chest's box, at its sprite's own aspect (176 x 244, the chest in the lower 156
        /// rows - the lid's room above it), so the drawn chest stands on the floor.
        /// </summary>
        internal const float ChestH = 470f, ChestW = ChestH * 176f / 244f;

        /// <summary>
        /// Where each band sits, as a middle (<c>UIKit.Box</c> pivots at centre): purse 698..602,
        /// title 556..464, chests standing on the floor at -30 (their drawn tops near +215), name
        /// -95..-165, line -205..-295, key -435..-585. The shortest canvas keeps about 945 either side.
        /// </summary>
        internal const float PurseY = 650f, TitleY = 510f, NameY = -130f, LineY = -250f, KeyY = -510f;
        internal const float PurseH = 96f, TitleH = 92f, NameH = 70f, LineH = 90f, KeyH = 150f;

        /// <summary>How far apart two chests stand, centre to centre.</summary>
        const float Pitch = 360f;

        static readonly Color Ink = Pal.Hex("#FFF3DC");
        static readonly Color Royal = Pal.Hex("#FFC23C");

        GiftStage _stage;
        RectTransform[] _chests;
        RectTransform _purse;
        Text _title, _name, _line;
        Btn _open, _done;
        bool _awaiting, _closing, _continued;
        int _count;

        protected override void Build()
        {
            _count = Step != null ? Math.Max(1, Math.Min(Step.Count, FirstStepTable.MaxChests)) : 1;

            _stage = GiftStage.Build(Content, GroundY, GiftTempo.ChestHush);
            Panel = _stage.Props;   // what ModalView.Close() scales out

            var tier = Step?.Tier;

            _title = Reveal(UIKit.Titled("Title", Content, Loc.Get("ui.steps.gift_title"), 76, Pal.Gold,
                                         TextAnchor.MiddleCenter, new Vector2(960f, TitleH),
                                         new Vector2(.5f, .5f), new Vector2(0f, TitleY), 4f, 4f), 40);
            _name = Reveal(UIKit.Titled("Name", Content,
                                        tier != null ? Loc.Format("ui.steps.chest_count", Loc.Get(tier.NameKey), _count) : string.Empty,
                                        58, Royal, TextAnchor.MiddleCenter, new Vector2(900f, NameH),
                                        new Vector2(.5f, .5f), new Vector2(0f, NameY), 4f, 4f), 30);
            _line = Reveal(UIKit.Titled("Line", Content, Loc.Get("ui.steps.chests_line"), 36, Ink,
                                        TextAnchor.MiddleCenter, new Vector2(860f, LineH),
                                        new Vector2(.5f, .5f), new Vector2(0f, LineY), 2f, 2f, wrap: true), 22);

            _open = UIKit.TextButton("Open", Content, "btn_green", Loc.Get("ui.steps.open"), 56,
                                     new Vector2(560f, KeyH), new Vector2(.5f, .5f), new Vector2(0f, KeyY), Open);
            UIKit.OneLine(_open, 30);
            _open.transform.localScale = Vector3.zero;

            _done = UIKit.TextButton("Done", Content, Skins.Alternate, Loc.Get("ui.common.done"), 50,
                                     new Vector2(480f, KeyH - 10f), new Vector2(.5f, .5f), new Vector2(0f, KeyY), () => Close());
            UIKit.OneLine(_done, 28);
            _done.transform.localScale = Vector3.zero;

            BuildPurse();
            BuildChests(tier);
            Play();

            Audio.Duck(.4f, 3.2f);
        }

        static Text Reveal(Text text, int floor)
        {
            UIKit.Shrinkable(text, floor);
            text.transform.localScale = Vector3.zero;
            return text;
        }

        // ------------------------------------------------------------- building
        void BuildChests(ChestTier tier)
        {
            // The closed chest is resident and global (45f), so it is drawn in the frame it is asked
            // for; a tier with no picture costs the picture and never the panel.
            var sprite = tier != null ? Art.S(tier.Icon) : null;

            _chests = new RectTransform[_count];
            for (int i = 0; i < _count; i++)
            {
                float x = (i - (_count - 1) * .5f) * Pitch;

                var body = UIKit.Box("Chest" + i, _stage.Props, new Vector2(ChestW, ChestH),
                                     new Vector2(.5f, .5f), new Vector2(x, GroundY + 1500f));
                UIKit.Group(body).alpha = 0f;

                var img = UIKit.Img("Img", body, sprite, Color.white);
                img.rectTransform.anchorMin = Vector2.zero;
                img.rectTransform.anchorMax = Vector2.one;
                img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.enabled = sprite != null;

                _chests[i] = body;
            }
        }

        /// <summary>
        /// The hub's three readouts, drawn over this panel and registered over the screen's: the
        /// kit's trough, the icon and the number, and no <c>+</c> - <c>HomeScreen.ResourcePill</c>'s
        /// measurements and <c>GemShopOverlay.BuildPurse</c>'s registration.
        /// </summary>
        void BuildPurse()
        {
            _purse = UIKit.Box("Purse", Content, new Vector2(1000f, PurseH), new Vector2(.5f, .5f), new Vector2(0f, PurseY));
            _purse.localScale = Vector3.zero;

            Pill(-322f, Pal.Rose, "ic_heart", false, ResourceSlots.Kind.Hearts, n => Profile.HeartsLabel((int)n));
            Pill(0f, Pal.Gold, null, true, ResourceSlots.Kind.Credits, Compact.Number);
            Pill(322f, Pal.Bloom, "ic_gem", false, ResourceSlots.Kind.Gems, Compact.Number);

            WalletWatch.Attach(this, ResourceSlots.Kind.Hearts, ResourceSlots.Kind.Credits, ResourceSlots.Kind.Gems);
        }

        void Pill(float x, Color tint, string icon, bool coin, ResourceSlots.Kind kind, Func<long, string> format)
        {
            var bg = UIKit.Img("Pill", _purse, Art.S("Ui/" + Skins.Trough), Color.white,
                               new Vector2(304f, PurseH), new Vector2(.5f, .5f), new Vector2(x, 0f));
            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(120f, 120f), new Vector2(0f, .5f), new Vector2(66f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, coin ? null : Art.S("Ui/" + icon), Color.white,
                               new Vector2(62f, 62f), new Vector2(0f, .5f), new Vector2(66f, 0f));
            ic.preserveAspect = true;
            if (coin) Flipbook.Attach(ic, "Ui/Coin", 11f);

            var number = UIKit.Shrinkable(
                UIKit.Titled("V", bg.transform, format(ResourceSlots.Walked(kind)), 37, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(150f, 52f), new Vector2(.5f, .5f), new Vector2(34f, 0f), 3f, 3f), 22);

            ResourceSlots.RegisterOver(this, kind, (RectTransform)ic.transform, number, glow, tint, format);
        }

        // ------------------------------------------------------------- the beats
        void Play()
        {
            Tween.After(GiftTempo.ChestHush * .6f, () => { if (_title) Tween.Pop(_title.transform, 0f, .45f); }, this);
            Tween.After(GiftTempo.ChestDrop, () => Audio.SfxVaried("whoosh", .7f), this);

            for (int i = 0; i < _count; i++)
            {
                int index = i;
                float x = _chests[i].anchoredPosition.x;
                float lands = GiftTempo.ChestImpact(i);

                GiftStage.Drop(_chests[i], new Vector2(x, GroundY), lands - GiftTempo.ChestFall,
                               GiftTempo.ChestFall, GiftTempo.TurretSettle, this);

                // Each later chest a lighter thump: the first is the arrival, the rest join it.
                Tween.After(lands, () => _stage?.Impact(new Vector2(x, GroundY), Royal, index == 0 ? 1f : .7f), this);
            }

            float caption = GiftTempo.ChestCaption(_count);

            Tween.After(caption - .15f, () =>
            {
                if (_stage == null) return;
                _stage.Lift();
                _stage.Glow(new Vector2(0f, GroundY + ChestH * .4f), Royal, ChestH * 1.15f);
            }, this);

            Tween.After(caption, () =>
            {
                if (_name) Tween.Pop(_name.transform, 0f, .45f);
                if (_purse) Tween.Pop(_purse, 0f, .45f);
                Audio.Sfx("unlock", .7f);
            }, this);

            Tween.After(caption + .15f, () => { if (_line) Tween.Pop(_line.transform, 0f, .4f); }, this);
            Tween.After(GiftTempo.ChestKey(_count), ShowOpen, this);
        }

        void ShowOpen()
        {
            if (!_open || _closing) return;
            Tween.Pop(_open.transform, 0f, .45f);
            Sheen.Attach((RectTransform)_open.transform, 3.2f);
        }

        // ------------------------------------------------------------- opening
        /// <summary>
        /// Opens the next shut chest through the chest ceremony, stacked over this panel. The chest
        /// that is opening leaves the floor, so the one left standing is visibly the one still to
        /// come. This panel waits, and <see cref="Update"/> hears the ceremony step aside.
        /// </summary>
        void Open()
        {
            if (_awaiting || _closing || Step == null) return;

            int n = FirstStepsLedger.NextChest(Step);
            var claim = ChestClaim.ForFirstStep(Step, n);
            if (n < 0 || !claim.IsValid) { Settle(); return; }

            _awaiting = true;
            if (_open) _open.transform.localScale = Vector3.zero;

            if (n < _chests.Length && _chests[n])
            {
                var chest = _chests[n];
                Tween.Punch(chest, .2f, .26f);
                Tween.Fade(UIKit.Group(chest), 0f, .3f).Delay(.2f);
            }

            Telemetry.Track("first_step_chest_open_tapped", "level", Step.Level.Value, "chest", n);
            Flow.Modal<ChestOverlay>(v => v.Claim = claim);
        }

        /// <summary>
        /// Hears the chest ceremony end - it departs at COLLECT and is dismissed on any other ending -
        /// by this panel being the top one again, then offers the next chest or the way onward.
        /// A poll for <c>DealMoment</c>'s reason: "the ceremony is over" has several endings and this
        /// reads the one fact they share.
        /// </summary>
        void Update()
        {
            if (!_awaiting || _closing || !Flow.IsTopModal(this)) return;
            _awaiting = false;

            if (Step != null && FirstStepsLedger.ChestsWaiting(Step) > 0) ShowOpen();
            else Settle();
        }

        /// <summary>Every chest is open: the room says so and offers the way onward.</summary>
        void Settle()
        {
            if (_closing) return;
            if (_open) _open.transform.localScale = Vector3.zero;
            if (_done) Tween.Pop(_done.transform, 0f, .45f, .35f);
        }

        public override bool OnBack()
        {
            if (!_awaiting) Close();
            return true;
        }

        /// <summary>
        /// Resumes what was interrupted, exactly once, however this panel ended. A chest left shut
        /// is not lost: it is still owed, and the hub raises this again for it.
        /// </summary>
        void OnDestroy()
        {
            _closing = true;
            if (_continued) return;
            _continued = true;

            var then = Then;
            Then = null;
            then?.Invoke();
        }
    }
}
