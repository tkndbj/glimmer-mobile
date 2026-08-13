using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Shared plumbing for modal panels: scrim, springy entrance, tidy exit.</summary>
    public abstract class ModalView : View
    {
        protected RectTransform Panel;
        protected Image Backing;
        bool _closing;

        protected RectTransform MakePanel(Vector2 size, string title, Vector2 offset = default,
                                          bool dismissOnScrim = true)
        {
            UIKit.Scrim(Content, .72f, dismissOnScrim ? (Action)(() => Close()) : null);

            Backing = UIKit.Img("Panel", Content, Art.S("Ui/panel_main"), Color.white,
                                size, new Vector2(.5f, .5f), offset);
            Panel = (RectTransform)Backing.transform;

            if (title != null)
            {
                var ribbon = UIKit.Img("Ribbon", Panel, Art.S("Ui/ribbon_orange"), Color.white,
                                       new Vector2(size.x * .78f, 130f), new Vector2(.5f, 1f), new Vector2(0f, 22f));
                UIKit.Titled("Title", ribbon.transform, title, 54, Pal.Cream, TextAnchor.MiddleCenter,
                             outline: 4f, shadow: 4f);
                ribbon.transform.localRotation = Quaternion.Euler(0, 0, -1.6f);
            }

            Panel.localScale = Vector3.zero;
            Tween.Scale(Panel, 1f, .5f, Ease.OutBack);
            Audio.Sfx("chime", .45f, 1.1f);
            return Panel;
        }

        protected void Close(Action after = null)
        {
            if (_closing) return;
            _closing = true;
            Audio.SfxVaried("back", .5f);
            var cg = UIKit.Group(Content);
            Tween.Fade(cg, 0f, .22f);
            Tween.Scale(Panel, .82f, .24f, Ease.InQuad).OnDone(() =>
            {
                Flow.Dismiss(this);
                after?.Invoke();
            });
        }

        /// <summary>Small square toggle used for the sound switches.</summary>
        protected Btn Toggle(Transform parent, string icon, Vector2 pos, Func<bool> get, Action<bool> set)
        {
            Btn b = null;
            void Paint()
            {
                bool on = get();
                var img = b.GetComponent<Image>();
                img.sprite = Art.S(on ? "Ui/sq_blue" : "Ui/sq_dark");
                if (b.Icon) b.Icon.color = on ? Pal.Cream : new Color(.72f, .78f, .84f, .7f);
            }
            b = UIKit.IconButton("T_" + icon, parent, "sq_blue", icon, new Vector2(124f, 124f),
                                 new Vector2(.5f, .5f), pos, () => { set(!get()); Paint(); });
            Paint();
            return b;
        }
    }

    // ====================================================================== pause
    public sealed class PauseOverlay : ModalView
    {
        public PlayScreen Screen;

        protected override void Build()
        {
            MakePanel(new Vector2(840f, 1040f), "PAUSED");

            UIKit.TextButton("Resume", Panel, "btn_green", "RESUME", 52, new Vector2(600f, 140f),
                             new Vector2(.5f, 1f), new Vector2(0f, -230f), Resume);
            UIKit.TextButton("Restart", Panel, "btn_orange", "RESTART", 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -390f),
                             () => Close(() => { Screen?.RestartLevel(); Screen?.Resume(); }));
            UIKit.TextButton("Glades", Panel, "btn_blue", "GLADES", 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -535f),
                             () => Close(() => Flow.Go<LevelsScreen>()));
            UIKit.TextButton("Home", Panel, "btn_red", "HOME", 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -680f),
                             () => Close(() => Flow.Go<HomeScreen>()));

            var row = UIKit.Box("Toggles", Panel, new Vector2(600f, 150f), new Vector2(.5f, 0f), new Vector2(0f, 128f));
            Toggle(row, "ic_music", new Vector2(-150f, 0f), () => Save.MusicOn, Save.SetMusic);
            Toggle(row, "ic_audio", new Vector2(0f, 0f), () => Save.SfxOn, on => { Save.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });
            Toggle(row, "ic_gear", new Vector2(150f, 0f), () => Save.HapticOn, Save.SetHaptic);

            UIKit.Titled("Hint", Panel, "music   sound   buzz", 26, new Color(.42f, .30f, .22f, .8f),
                         TextAnchor.MiddleCenter, new Vector2(600f, 40f), new Vector2(.5f, 0f), new Vector2(0f, 46f), 0f, 0f);
        }

        void Resume() => Close(() => Screen?.Resume());

        public override bool OnBack() { Resume(); return true; }
    }

    // ==================================================================== victory
    public sealed class WinOverlay : ModalView
    {
        public int Level, Stars, Moves, Par, PreviousBest;
        public bool FirstClear;

        static readonly string[] Ranks = { "SOLVED", "LOVELY!", "PERFECT!" };

        protected override void Build()
        {
            bool last = Level >= Levels.Count - 1;

            UIKit.Scrim(Content, .62f);

            var banner = UIKit.Img("Victory", Content, null, Color.white,
                                   new Vector2(600f, 420f), new Vector2(.5f, .5f), new Vector2(0f, 470f));
            banner.preserveAspect = true;
            Flipbook.Attach(banner, "Fx/Victory", 34f, false);
            banner.transform.localScale = Vector3.one * .9f;
            Tween.Scale(banner.transform, 1f, .8f, Ease.OutBack);

            Backing = UIKit.Img("Panel", Content, Art.S("Ui/panel_main"), Color.white,
                                new Vector2(880f, 820f), new Vector2(.5f, .5f), new Vector2(0f, -140f));
            Panel = (RectTransform)Backing.transform;
            Panel.localScale = Vector3.zero;
            Tween.Scale(Panel, 1f, .55f, Ease.OutBack).Delay(.45f);

            var stars = StarRow.Create(Panel, new Vector2(.5f, 1f), new Vector2(0f, -135f), 144f, 152f, 0, true);
            stars.Reveal(Stars, .95f, .42f);

            var rank = UIKit.Titled("Rank", Panel, Ranks[Mathf.Clamp(Stars - 1, 0, 2)], 62, Pal.Rose,
                                    TextAnchor.MiddleCenter, new Vector2(700f, 80f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -285f), 4f, 4f);
            rank.transform.localScale = Vector3.zero;
            Tween.Pop(rank.transform, 0f, .6f, .95f + .42f * Stars);

            string bestLine = PreviousBest == 0 || Moves < PreviousBest
                ? "a new best for this glade"
                : $"your best here is {PreviousBest}";
            UIKit.Titled("Moves", Panel, $"{Moves} moves  -  {Par} for three stars", 36,
                         new Color(.40f, .28f, .20f), TextAnchor.MiddleCenter, new Vector2(760f, 50f),
                         new Vector2(.5f, 1f), new Vector2(0f, -368f), 0f, 2f);
            UIKit.Titled("Best", Panel, bestLine, 30, new Color(.52f, .38f, .28f, .9f),
                         TextAnchor.MiddleCenter, new Vector2(760f, 44f), new Vector2(.5f, 1f),
                         new Vector2(0f, -416f), 0f, 0f);

            if (last && Save.AllCleared())
                UIKit.Titled("Done", Panel, "the whole grove is awake", 32, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(760f, 44f), new Vector2(.5f, 1f),
                             new Vector2(0f, -462f), 3f, 3f);

            var next = UIKit.TextButton("Next", Panel, "btn_green", last ? "GLADES" : "NEXT GLADE", 48,
                                        new Vector2(560f, 140f), new Vector2(.5f, 0f), new Vector2(0f, 232f),
                                        () => Close(() =>
                                        {
                                            if (last) Flow.Go<LevelsScreen>();
                                            else Flow.Go<PlayScreen>(v => v.LevelIndex = Level + 1);
                                        }));
            UIKit.Halo(next.transform, Pal.Mint, 640f, .3f);

            UIKit.IconButton("Replay", Panel, "sq_orange", "ic_restart", new Vector2(120f, 120f),
                             new Vector2(.5f, 0f), new Vector2(-232f, 96f),
                             () => Close(() => Flow.Go<PlayScreen>(v => v.LevelIndex = Level)));
            UIKit.IconButton("Map", Panel, "sq_blue", "ic_list", new Vector2(120f, 120f),
                             new Vector2(.5f, 0f), new Vector2(232f, 96f),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            Tween.After(.9f, () =>
            {
                if (!this) return;
                Sheen.Attach((RectTransform)next.transform, 3.2f);
                Tween.Breathe(next.transform, .025f, 2f);
            }, this);

            if (FirstClear && !last)
                Tween.After(1.0f + .42f * Stars, () =>
                {
                    if (!this) return;
                    Audio.Sfx("unlock", .6f);
                    Scenery.Toast(Content, $"{Levels.All[Level + 1].Name} has opened", Pal.Gold, 2.2f,
                                  new Vector2(.5f, 0f), 190f);
                }, this);
        }

        public override bool OnBack() { Close(() => Flow.Go<LevelsScreen>()); return true; }
    }

    // ==================================================================== how to
    public sealed class HowToOverlay : ModalView
    {
        protected override void Build()
        {
            MakePanel(new Vector2(880f, 1220f), "HOW TO PLAY");

            string[] lines =
            {
                "Tap a conduit to turn it a quarter step.",
                "Guide light from every heart-crystal to the sleeping critters.",
                "Hearts joined into one network blend their colours.",
                "A critter only wakes to the exact colour it dreams of.",
                "Rooted tiles wear a padlock and will not turn.",
                "Fewer moves, more stars. There is no timer here.",
            };
            for (int i = 0; i < lines.Length; i++)
            {
                float y = -212f - i * 106f;
                var dot = UIKit.Img("dot" + i, Panel, Art.Disc(64), Pal.Energy(1 + (i % 7)),
                                    new Vector2(26f, 26f), new Vector2(0f, 1f), new Vector2(96f, y + 4f));
                var t = UIKit.Titled("l" + i, Panel, lines[i], 32, new Color(.36f, .25f, .18f),
                                     TextAnchor.UpperLeft, new Vector2(620f, 98f), new Vector2(0f, 1f),
                                     new Vector2(452f, y - 16f), 0f, 0f);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                var tr = (RectTransform)t.transform;
                tr.localScale = Vector3.zero;
                Tween.Pop(tr, 0f, .45f, .12f + i * .07f);
                Tween.Pop(dot.transform, 0f, .45f, .12f + i * .07f);
            }

            var mix = UIKit.Box("Mix", Panel, new Vector2(700f, 150f), new Vector2(.5f, 1f), new Vector2(0f, -930f));
            Swatch(mix, Pal.Ember, -230f, "red");
            UIKit.Titled("plus", mix, "+", 46, new Color(.4f, .3f, .22f), TextAnchor.MiddleCenter,
                         new Vector2(60f, 60f), new Vector2(.5f, .5f), new Vector2(-140f, 14f), 0f, 0f);
            Swatch(mix, Pal.Azure, -50f, "blue");
            UIKit.Titled("eq", mix, "=", 46, new Color(.4f, .3f, .22f), TextAnchor.MiddleCenter,
                         new Vector2(60f, 60f), new Vector2(.5f, .5f), new Vector2(45f, 14f), 0f, 0f);
            Swatch(mix, Pal.Bloom, 150f, "blossom");

            UIKit.TextButton("Ok", Panel, "btn_green", "GOT IT", 48, new Vector2(520f, 132f),
                             new Vector2(.5f, 0f), new Vector2(0f, 86f), () => Close());
        }

        void Swatch(Transform parent, Color colour, float x, string label)
        {
            var glow = UIKit.Img("g", parent, Art.Glow(96, 1.9f), Pal.A(colour, .6f),
                                 new Vector2(128f, 128f), new Vector2(.5f, .5f), new Vector2(x, 24f));
            var d = UIKit.Img("d", parent, Art.Disc(96), Pal.Lift(colour, .2f),
                              new Vector2(68f, 68f), new Vector2(.5f, .5f), new Vector2(x, 24f));
            Tween.Breathe(glow.transform, .09f, 1.9f, x * .01f);
            UIKit.Titled("t", parent, label, 24, new Color(.42f, .31f, .23f), TextAnchor.MiddleCenter,
                         new Vector2(200f, 34f), new Vector2(.5f, .5f), new Vector2(x, -32f), 0f, 0f);
        }

        public override bool OnBack() { Close(); return true; }
    }

    // ================================================================= settings
    public sealed class SettingsOverlay : ModalView
    {
        protected override void Build()
        {
            MakePanel(new Vector2(860f, 900f), "SETTINGS");

            var row = UIKit.Box("Toggles", Panel, new Vector2(700f, 200f), new Vector2(.5f, 1f), new Vector2(0f, -260f));
            Toggle(row, "ic_music", new Vector2(-190f, 0f), () => Save.MusicOn, Save.SetMusic);
            Toggle(row, "ic_audio", new Vector2(0f, 0f), () => Save.SfxOn,
                   on => { Save.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });
            Toggle(row, "ic_gear", new Vector2(190f, 0f), () => Save.HapticOn, Save.SetHaptic);

            Caption(row, "music", -190f);
            Caption(row, "sound", 0f);
            Caption(row, "buzz", 190f);

            UIKit.Titled("Ver", Panel, "Glimmer Grove  v1.0", 28, new Color(.44f, .32f, .24f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 40f), new Vector2(.5f, 1f),
                         new Vector2(0f, -430f), 0f, 0f);
            UIKit.Titled("Credit", Panel, "art & audio by CraftPix", 24, new Color(.52f, .40f, .31f, .85f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 36f), new Vector2(.5f, 1f),
                         new Vector2(0f, -474f), 0f, 0f);

            UIKit.TextButton("Wipe", Panel, "btn_red", "RESET PROGRESS", 38, new Vector2(560f, 120f),
                             new Vector2(.5f, 0f), new Vector2(0f, 262f), ConfirmWipe);
            UIKit.TextButton("Close", Panel, "btn_green", "DONE", 46, new Vector2(560f, 132f),
                             new Vector2(.5f, 0f), new Vector2(0f, 118f), () => Close());
        }

        bool _armed;

        void ConfirmWipe()
        {
            var btn = Panel.Find("Wipe");
            var label = btn != null ? btn.Find("Text").GetComponent<Text>() : null;
            if (!_armed)
            {
                _armed = true;
                if (label) label.text = "TAP AGAIN TO ERASE";
                Audio.Sfx("nope", .5f);
                Tween.Shake((RectTransform)btn, 9f, .3f);
                Tween.After(3.2f, () =>
                {
                    if (this == null) return;
                    _armed = false;
                    if (label) label.text = "RESET PROGRESS";
                }, this);
                return;
            }
            Save.WipeProgress();
            Audio.Sfx("shatter", .55f);
            Close(() => Flow.Go<HomeScreen>());
        }

        static void Caption(Transform parent, string text, float x)
            => UIKit.Titled("C_" + text, parent, text, 26, new Color(.44f, .32f, .24f, .9f),
                            TextAnchor.MiddleCenter, new Vector2(200f, 34f), new Vector2(.5f, .5f),
                            new Vector2(x, -84f), 0f, 0f);

        public override bool OnBack() { Close(); return true; }
    }

    // ============================================================== coming soon
    public sealed class ComingSoonOverlay : ModalView
    {
        string _title = "Coming Soon", _icon = "ic_chest", _body = "";

        public void Configure(string title, string icon, string body)
        {
            _title = title; _icon = icon; _body = body;
        }

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 840f), _title.ToUpperInvariant());

            var glow = UIKit.Img("Glow", Panel, Art.Glow(128, 1.9f), Pal.A(Pal.Gold, .45f),
                                 new Vector2(380f, 380f), new Vector2(.5f, 1f), new Vector2(0f, -290f));
            var icon = UIKit.Img("Icon", Panel, Art.S("Ui/" + _icon), Color.white,
                                 new Vector2(230f, 230f), new Vector2(.5f, 1f), new Vector2(0f, -290f));
            icon.preserveAspect = true;
            Tween.Bob((RectTransform)icon.transform, 14f, 2.2f);
            Tween.Run(2.4f, Ease.InOutSine,
                t => { if (glow) glow.transform.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, t); },
                glow, "pulse").Loop(-1, true);
            icon.transform.localScale = Vector3.zero;
            Tween.Pop(icon.transform, 0f, .6f, .18f);

            var ribbon = UIKit.Img("Soon", Panel, Art.S("Ui/ribbon_green"), Color.white,
                                   new Vector2(420f, 96f), new Vector2(.5f, 1f), new Vector2(0f, -458f));
            UIKit.Titled("T", ribbon.transform, "COMING SOON", 38, Pal.Cream, TextAnchor.MiddleCenter,
                         outline: 3f, shadow: 3f);
            ribbon.transform.localRotation = Quaternion.Euler(0, 0, -2.4f);

            var body = UIKit.Titled("Body", Panel, _body, 32, new Color(.40f, .28f, .20f),
                                    TextAnchor.UpperCenter, new Vector2(700f, 120f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -580f), 0f, 0f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            UIKit.TextButton("Ok", Panel, "btn_green", "GOT IT", 46, new Vector2(520f, 128f),
                             new Vector2(.5f, 0f), new Vector2(0f, 96f), () => Close());
        }

        public override bool OnBack() { Close(); return true; }
    }
}
