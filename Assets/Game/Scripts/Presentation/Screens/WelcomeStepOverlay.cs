using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Notifications;
using GlimmerGrove.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// "Well done!" - the welcome bonus's progress, shown at the moment a new player has just earned
    /// a piece of it (a welcome step, <c>FirstStepTable</c>): the turret the quest pays, standing in
    /// its seat; the row of day boxes, today's box stamped with a tick that slams down onto it; where
    /// the quest stands; and the reason to come back - tomorrow is the next box.
    ///
    /// <para>
    /// <b>It carries the reminder ask</b> (invariant 50p) when the cadence allows one, because there
    /// is no better moment to offer a nudge than the moment the player has been shown what tomorrow
    /// is for: REMIND ME raises the OS's own dialog (or its settings page, once the OS will not
    /// draw one again) and NOT NOW costs nothing. <see cref="FirstStepsMoment"/> decided the route
    /// and marked the ask shown, so the cadence keeps the next one days away. With nothing to ask,
    /// the panel has one key.
    /// </para>
    /// <para>
    /// <b>The next board waits for the OS to answer.</b> The dialog is the OS's, drawn over the
    /// game, and a run started underneath it would be a run the player cannot see begin. So after
    /// REMIND ME this panel stays up until the app has lost focus to the dialog and got it back - or
    /// <see cref="OsGrace"/> has passed with no dialog at all - and only then resumes what the
    /// player tapped on the victory panel (<see cref="Then"/>, <c>AccountOverlay</c>'s contract).
    /// </para>
    /// </summary>
    public sealed class WelcomeStepOverlay : ModalView
    {
        /// <summary>The quest shown. Chosen by <c>WelcomeStep.QuestToShow</c>.</summary>
        public WelcomeQuest Quest { get; set; }

        /// <summary>What REMIND ME does, or <see cref="AskRoute.None"/> for a panel with nothing to ask.</summary>
        public AskRoute Route { get; set; } = AskRoute.None;

        /// <summary>What to resume when this panel is gone, whichever way it goes.</summary>
        public Action Then { get; set; }

        internal const float PanelW = 880f, PanelH = 1220f;

        /// <summary>
        /// Where each band sits below the panel's top edge, as a middle: the seat -100..-320, the
        /// name -337..-393, the sentence -403..-447, the boxes -480..-600, "tomorrow" -606..-638, the
        /// day line -665..-735, the reason -752..-852 (two lines at 32). The keys stand on the foot
        /// where <c>ReminderAskOverlay</c> puts its own, the upper one's top edge at -919. Measured
        /// in every Latin table by <c>render_first_steps.py --measure</c>.
        /// </summary>
        internal const float SeatY = -210f, NameY = -365f, SentenceY = -425f, BoxesY = -540f,
                             TomorrowY = -622f, DayY = -700f, ReasonY = -802f;

        internal const float SeatSize = 220f, BoxMost = 120f, BoxGap = 22f, BoxRow = 760f;

        /// <summary>How long REMIND ME waits for the OS's dialog to take focus before going on without it.</summary>
        internal const float OsGrace = 1.5f;

        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color BoxDim = new Color(1f, 1f, 1f, .55f);

        CeremonyFx _matter, _light;
        Image[] _boxes, _ticks;
        Image _body;
        Text _day, _reason, _tomorrow;
        Btn _yes, _later, _ok;
        AssetHold _art;
        bool _answered, _waitingOs, _sawBlur, _continued;
        float _askedAt;

        int Done => Quest != null ? WelcomeLedger.DaysDone(Quest) : 0;
        int Target => Quest != null ? Quest.Days : 0;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.steps.well_done"), dismissOnScrim: false);
            if (Quest == null) return;

            var tint = SiegeView.TintOf(Quest.Colour);

            // The turret the quest pays, in its seat with its colour on the rim: the welcome
            // page's own cell, one size up, so the panel points at the page it belongs to.
            var seat = UIKit.Img("Seat", Panel, Art.S("Ui/" + Skins.Slot), Color.white,
                                 Vector2.one * SeatSize, new Vector2(.5f, 1f), new Vector2(0f, SeatY));
            var halo = UIKit.Img("Halo", seat.transform, Art.Glow(128, 2f), Pal.A(tint, .45f),
                                 Vector2.one * SeatSize * 1.8f, new Vector2(.5f, .5f), Vector2.zero);
            halo.raycastTarget = false;
            halo.transform.SetAsFirstSibling();
            Tween.Breathe(halo.transform, .06f, 2.4f);
            UIKit.Img("Rim", seat.transform, Art.RoundOutline(26, 5f), Pal.A(tint, .85f),
                      Vector2.one * (SeatSize - 10f)).raycastTarget = false;

            _body = UIKit.Img("Body", seat.transform, null, Color.white,
                              new Vector2(SeatSize * .62f, SeatSize * .78f), new Vector2(.5f, .5f), new Vector2(0f, 6f));
            _body.preserveAspect = true;
            _body.raycastTarget = false;
            _body.enabled = false;   // a sprite-less Image is a white rectangle (7b)

            UIKit.Shrinkable(UIKit.Titled("Name", Panel, Loc.Get(Quest.Ward.NameKey), 50, Pal.Cream,
                                          TextAnchor.MiddleCenter, new Vector2(760f, 56f),
                                          new Vector2(.5f, 1f), new Vector2(0f, NameY), 3f, 3f), 28);
            UIKit.Shrinkable(UIKit.Titled("Sentence", Panel, Loc.Format(Quest.SentenceKey, Quest.Days), 30, Ink,
                                          TextAnchor.MiddleCenter, new Vector2(760f, 44f),
                                          new Vector2(.5f, 1f), new Vector2(0f, SentenceY), 0f, 0f), 18);

            BuildBoxes();

            _day = UIKit.Shrinkable(UIKit.Titled("Day", Panel, Loc.Format("ui.steps.day_of", Math.Max(1, Done), Target), 54,
                                                 Pal.Gold, TextAnchor.MiddleCenter, new Vector2(760f, 70f),
                                                 new Vector2(.5f, 1f), new Vector2(0f, DayY), 4f, 4f), 30);
            _day.transform.localScale = Vector3.zero;

            _reason = UIKit.Shrinkable(UIKit.Titled("Reason", Panel, Loc.Get(ReasonKey()), 32, Ink,
                                                    TextAnchor.MiddleCenter, new Vector2(760f, 100f),
                                                    new Vector2(.5f, 1f), new Vector2(0f, ReasonY), 0f, 0f, wrap: true), 20);
            _reason.transform.localScale = Vector3.zero;

            BuildKeys();

            // Matter under light, over the panel: the stamp's dust and its sparks.
            _matter = CeremonyFx.AttachSoft(Panel, "Dust", (GiftStage.SmokeArt[0], 12), (GiftStage.SmokeArt[1], 12));
            _light = CeremonyFx.Attach(Panel, "Light", (Art.Spark(64), 40), (Art.Ring(256, 6f), 3));

            Stamp();
            Load();
        }

        /// <summary>
        /// Written out rather than assembled, so the build's string checker sees every key.
        /// A quest already full says where to collect it; otherwise tomorrow is the reason, with
        /// the reminder question when there is one to ask.
        /// </summary>
        string ReasonKey()
        {
            if (Done >= Target) return "ui.steps.welcome_ready";
            return Route != AskRoute.None ? "ui.steps.come_back_remind" : "ui.steps.come_back";
        }

        void BuildBoxes()
        {
            int n = Math.Max(1, Target);
            float size = Mathf.Min(BoxMost, (BoxRow - BoxGap * (n - 1)) / n);
            float width = n * size + (n - 1) * BoxGap;

            _boxes = new Image[n];
            _ticks = new Image[n];

            int stamped = Done - 1;
            for (int i = 0; i < n; i++)
            {
                float x = -width * .5f + size * .5f + i * (size + BoxGap);
                bool lit = i < Done && i != stamped;

                var box = UIKit.Img("B" + i, Panel, Art.S("Ui/" + Skins.Slot), lit ? Color.white : BoxDim,
                                    Vector2.one * size, new Vector2(.5f, 1f), new Vector2(x, BoxesY));
                box.raycastTarget = false;

                var tick = UIKit.Img("Tick", box.transform, Art.S("Ui/ic_check"), Pal.Mint,
                                     Vector2.one * size * .66f, new Vector2(.5f, .5f), new Vector2(0f, 2f));
                tick.preserveAspect = true;
                tick.raycastTarget = false;
                tick.enabled = lit;

                _boxes[i] = box;
                _ticks[i] = tick;
            }

            // The box tomorrow fills: breathing, with the word under it.
            if (Done < n)
            {
                var next = _boxes[Done];
                Tween.Breathe(next.transform, .05f, 1.6f);

                _tomorrow = UIKit.Shrinkable(UIKit.Titled("Tomorrow", Panel, Loc.Get("ui.shop.pass_tomorrow"), 26, Ink,
                                                          TextAnchor.MiddleCenter, new Vector2(Mathf.Max(size + 60f, 180f), 32f),
                                                          new Vector2(.5f, 1f),
                                                          new Vector2(next.rectTransform.anchoredPosition.x, TomorrowY), 0f, 0f), 16);
                _tomorrow.transform.localScale = Vector3.zero;
            }
        }

        void BuildKeys()
        {
            if (Route == AskRoute.None)
            {
                _ok = UIKit.TextButton("Ok", Panel, "btn_green", Loc.Get("ui.common.got_it"), 46,
                                       new Vector2(560f, 138f), new Vector2(.5f, 0f), new Vector2(0f, 150f), Ok);
                UIKit.OneLine(_ok, 26);
                _ok.transform.localScale = Vector3.zero;
                return;
            }

            bool settings = Route == AskRoute.Settings;
            _yes = UIKit.TextButton("Yes", Panel, "btn_green",
                                    Loc.Get(settings ? "ui.reminders.open_settings" : "ui.reminders.ask_yes"), 46,
                                    new Vector2(620f, 138f), new Vector2(.5f, 0f), new Vector2(0f, 232f), Yes);
            UIKit.Shrinkable(_yes.Label, 26);
            _yes.transform.localScale = Vector3.zero;

            _later = UIKit.TextButton("Later", Panel, "btn_blue", Loc.Get("ui.reminders.not_now"), 42,
                                      new Vector2(620f, 126f), new Vector2(.5f, 0f), new Vector2(0f, 92f), Later);
            UIKit.Shrinkable(_later.Label, 24);
            _later.transform.localScale = Vector3.zero;
        }

        // ------------------------------------------------------------- the stamp
        /// <summary>
        /// Today's tick comes down on today's box: it starts large and tilted above the box, drops
        /// on it, and lands with a thump - the box lit, a ring, a puff of dust round it, sparks - and
        /// then the line saying where the quest stands and the keys. <see cref="GiftTempo"/>'s beats.
        /// </summary>
        void Stamp()
        {
            int k = Done - 1;
            if (k < 0 || k >= _ticks.Length)
            {
                Tween.After(GiftTempo.StampLine, RevealText, this);
                Tween.After(GiftTempo.StampKeys, RevealKeys, this);
                return;
            }

            var tick = _ticks[k];
            var box = _boxes[k];
            var trt = tick.rectTransform;
            var rest = trt.anchoredPosition;
            var lifted = rest + new Vector2(0f, 190f);

            Tween.After(GiftTempo.StampDrop, () =>
            {
                if (!tick) return;
                tick.enabled = true;
                trt.anchoredPosition = lifted;
                trt.localScale = Vector3.one * 2.8f;
                trt.localRotation = Quaternion.Euler(0f, 0f, -18f);
                tick.color = Pal.A(Pal.Mint, 0f);

                Tween.Run(GiftTempo.StampFall, Ease.InQuad, t =>
                {
                    if (!tick) return;
                    trt.anchoredPosition = Vector2.LerpUnclamped(lifted, rest, t);
                    trt.localScale = Vector3.one * Mathf.Lerp(2.8f, .86f, t);
                    trt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-18f, 0f, t));
                    tick.color = Pal.A(Pal.Mint, Mathf.Clamp01(t * 3f));
                }, this, "stamp");
            }, this);

            Tween.After(GiftTempo.StampImpact, () =>
            {
                if (!box || !tick) return;

                // The tick springs out of its squash to its own size; the box takes the hit.
                Tween.Run(.36f, Ease.OutBack, t =>
                {
                    if (trt) trt.localScale = Vector3.one * Mathf.LerpUnclamped(.86f, 1f, t);
                }, this, "stamp");
                box.color = Color.white;
                Tween.Punch(box.transform, .24f, .32f);

                var at = Panel.InverseTransformPoint(box.rectTransform.position);
                var centre = new Vector2(at.x, at.y);
                float size = box.rectTransform.sizeDelta.x;

                _light?.Shock(Art.Ring(256, 6f), centre, Pal.A(Pal.Mint, .9f), size * .6f, size * 3.2f, .5f);
                _light?.Spray(Art.Spark(64), centre, new[] { Pal.Hex("#FFF4CE"), Pal.Gold, Pal.Mint }, 26,
                              260f, 760f, 18f, .65f, 900f, 2.6f, .05f, 160f);
                _matter?.Billow(GiftStage.SmokeArt, centre - new Vector2(0f, size * .4f), Pal.A(Pal.Hex("#E9DCC6"), .85f),
                                12, 160f, 420f, size * .55f, .9f, 8f, 40f);

                Audio.Sfx("star", .8f);
                Audio.SfxVaried("land", .6f);
            }, this);

            Tween.After(GiftTempo.StampLine, RevealText, this);
            Tween.After(GiftTempo.StampKeys, RevealKeys, this);
        }

        void RevealText()
        {
            if (_day) Tween.Pop(_day.transform, 0f, .45f);
            if (_reason) Tween.Pop(_reason.transform, 0f, .4f, .12f);
            if (_tomorrow) Tween.Pop(_tomorrow.transform, 0f, .4f, .2f);
        }

        void RevealKeys()
        {
            if (_ok) { Tween.Pop(_ok.transform, 0f, .45f); Sheen.Attach((RectTransform)_ok.transform, 3.2f); }
            if (_yes) { Tween.Pop(_yes.transform, 0f, .45f); Sheen.Attach((RectTransform)_yes.transform, 3.2f); }
            if (_later) Tween.Pop(_later.transform, 0f, .45f, .08f);
        }

        // ------------------------------------------------------------- art
        void Load()
        {
            if (Quest?.Ward == null) return;
            string address = AssetManifest.WardArt(Quest.Ward, Quest.Colour);

            _art = AssetLibrary.Hold("welcome_step");
            Lifeline.Of(this)?.Run(async token =>
            {
                await _art.LoadAsync(new List<AssetRequest> { AssetRequest.Sprite(address) }, null, token);
                if (this == null || _body == null) return;

                var sprite = AssetLibrary.Sprite(address);
                _body.sprite = sprite;
                _body.enabled = sprite != null;
                if (sprite != null) Tween.Pop(_body.transform, .6f, .4f);
            }, "WelcomeStepOverlay.Load");
        }

        // ------------------------------------------------------------- the answers
        void Ok()
        {
            if (_answered) return;
            _answered = true;
            Close();
        }

        void Yes()
        {
            if (_answered) return;
            _answered = true;

            Telemetry.Track("notification_ask_answered", "route", NotificationAsk.Id(Route),
                            "trigger", WelcomeStep.AskTrigger, "answer", "yes");

            // Yes is also an answer to our own switch, so a player who never touched it is recorded
            // as wanting reminders before the OS is asked - ReminderAskOverlay's order.
            if (Route == AskRoute.Settings) Notify.OpenSettings();
            else
            {
                NotificationOptIn.SetWanted(true);
                Notify.Ask();
            }

            _waitingOs = true;
            _sawBlur = false;
            _askedAt = Time.unscaledTime;
        }

        void Later()
        {
            if (_answered) return;
            _answered = true;

            Telemetry.Track("notification_ask_answered", "route", NotificationAsk.Id(Route),
                            "trigger", WelcomeStep.AskTrigger, "answer", "later");
            Close();
        }

        /// <summary>
        /// Waits out the OS's dialog after REMIND ME: focus leaves the app while the dialog is up and
        /// comes back when it is answered. No dialog by <see cref="OsGrace"/> - the OS chose not to
        /// draw one - is the same answer. A poll, because the OS reports nothing to the game.
        /// </summary>
        void Update()
        {
            if (!_waitingOs) return;

            if (!Application.isFocused)
            {
                _sawBlur = true;
                return;
            }

            if (_sawBlur || Time.unscaledTime - _askedAt >= OsGrace)
            {
                _waitingOs = false;
                Close();
            }
        }

        /// <summary>Back is "not now" - or, with nothing asked, the one key.</summary>
        public override bool OnBack()
        {
            if (_waitingOs) return true;
            if (Route == AskRoute.None) Ok();
            else Later();
            return true;
        }

        /// <summary>Resumes what was interrupted, exactly once, however this panel ended.</summary>
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
