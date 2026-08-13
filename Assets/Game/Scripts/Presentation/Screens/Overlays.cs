using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
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
            MakePanel(new Vector2(840f, 1040f), Loc.Get("ui.pause.title"));

            UIKit.TextButton("Resume", Panel, "btn_green", Loc.Get("ui.pause.resume"), 52, new Vector2(600f, 140f),
                             new Vector2(.5f, 1f), new Vector2(0f, -230f), Resume);
            UIKit.TextButton("Restart", Panel, "btn_orange", Loc.Get("ui.pause.restart"), 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -390f),
                             () => Close(() => { Screen?.RestartLevel(); Screen?.Resume(); }));
            UIKit.TextButton("Glades", Panel, "btn_blue", Loc.Get("ui.pause.glades"), 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -535f),
                             () => Close(() => Flow.Go<LevelsScreen>()));
            UIKit.TextButton("Home", Panel, "btn_red", Loc.Get("ui.pause.home"), 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -680f),
                             () => Close(() => Flow.Go<HomeScreen>()));

            var row = UIKit.Box("Toggles", Panel, new Vector2(600f, 150f), new Vector2(.5f, 0f), new Vector2(0f, 128f));
            Toggle(row, "ic_music", new Vector2(-150f, 0f), () => GameSettings.MusicOn, GameSettings.SetMusic);
            Toggle(row, "ic_audio", new Vector2(0f, 0f), () => GameSettings.SfxOn, on => { GameSettings.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });
            Toggle(row, "ic_gear", new Vector2(150f, 0f), () => GameSettings.HapticsOn, GameSettings.SetHaptics);

            UIKit.Titled("Hint", Panel, Loc.Get("ui.settings.toggle_row"), 26, new Color(.42f, .30f, .22f, .8f),
                         TextAnchor.MiddleCenter, new Vector2(600f, 40f), new Vector2(.5f, 0f), new Vector2(0f, 46f), 0f, 0f);
        }

        void Resume() => Close(() => Screen?.Resume());

        public override bool OnBack() { Resume(); return true; }
    }

    // ==================================================================== victory
    public sealed class WinOverlay : ModalView
    {
        public LevelId LevelId;
        public int Stars, Moves, Par, PreviousBest;
        public bool FirstClear;

        /// <summary>What this run added, already folded into the ledger. Zero on a worse replay.</summary>
        public long XpGained, CreditsGained;

        /// <summary>The level just reached, or 0 when this run did not raise it.</summary>
        public int LevelledUpTo;

        /// <summary>
        /// Written out rather than assembled as "ui.win.rank" + stars, so the keys are
        /// visible to the build's string checker. A key that only exists at runtime is
        /// a key nothing can verify.
        /// </summary>
        static readonly string[] RankKeys = { "ui.win.rank1", "ui.win.rank2", "ui.win.rank3" };

        /// <summary>
        /// True when this run completed the last uncleared glade of its chapter. Derived
        /// from the catalog rather than counted, so it stays correct when a chapter gains
        /// levels in a content drop.
        /// </summary>
        bool FinishedAChapter(LevelCatalog catalog)
        {
            var level = catalog.Find(LevelId);
            if (level == null) return false;

            foreach (var sibling in catalog.LevelsOf(level.Chapter))
                if (!PlayerProgress.IsCleared(sibling.Id)) return false;

            return true;
        }

        protected override void Build()
        {
            // "Last" means the catalog has nothing after this level, not a fixed count,
            // so publishing a new chapter turns the end screen back into a next button
            // without any code here changing.
            var catalog = GameContent.Catalog;
            var next = catalog.Next(LevelId);
            bool last = next == null;

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

            var rank = UIKit.Titled("Rank", Panel, Loc.Get(RankKeys[Mathf.Clamp(Stars, 1, 3) - 1]), 62, Pal.Rose,
                                    TextAnchor.MiddleCenter, new Vector2(700f, 80f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -285f), 4f, 4f);
            rank.transform.localScale = Vector3.zero;
            Tween.Pop(rank.transform, 0f, .6f, .95f + .42f * Stars);

            string bestLine = PreviousBest == 0 || Moves < PreviousBest
                ? Loc.Get("ui.win.new_best")
                : Loc.Format("ui.win.best_is", PreviousBest);
            UIKit.Titled("Moves", Panel, Loc.Format("ui.win.moves", Moves, Par), 36,
                         new Color(.40f, .28f, .20f), TextAnchor.MiddleCenter, new Vector2(760f, 50f),
                         new Vector2(.5f, 1f), new Vector2(0f, -368f), 0f, 2f);
            UIKit.Titled("Best", Panel, bestLine, 30, new Color(.52f, .38f, .28f, .9f),
                         TextAnchor.MiddleCenter, new Vector2(760f, 44f), new Vector2(.5f, 1f),
                         new Vector2(0f, -416f), 0f, 0f);

            // Only shown when the run actually improved the record. A replay that beat
            // nothing earns nothing, and saying "+0 XP" would read as a bug.
            float rewardY = -462f;
            if (XpGained > 0 || CreditsGained > 0)
            {
                var reward = UIKit.Titled("Reward", Panel,
                                          Loc.Format("ui.win.reward", XpGained, CreditsGained), 32, Pal.Mint,
                                          TextAnchor.MiddleCenter, new Vector2(760f, 44f),
                                          new Vector2(.5f, 1f), new Vector2(0f, rewardY), 2f, 2f);
                reward.transform.localScale = Vector3.zero;
                Tween.Pop(reward.transform, 0f, .55f, 1.15f + .42f * Stars);
                rewardY -= 46f;
            }

            if (LevelledUpTo > 0)
            {
                var levelUp = UIKit.Titled("LevelUp", Panel,
                                           Loc.Format("ui.win.level_up", LevelledUpTo), 40, Pal.Gold,
                                           TextAnchor.MiddleCenter, new Vector2(760f, 52f),
                                           new Vector2(.5f, 1f), new Vector2(0f, rewardY), 4f, 4f);
                levelUp.transform.localScale = Vector3.zero;
                Tween.Pop(levelUp.transform, 0f, .7f, 1.45f + .42f * Stars);
                rewardY -= 54f;
            }

            if (last && PlayerProgress.AllCleared(catalog))
                UIKit.Titled("Done", Panel, Loc.Get("ui.win.all_done"), 32, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(760f, 44f), new Vector2(.5f, 1f),
                             new Vector2(0f, rewardY), 3f, 3f);

            var nextId = last ? LevelId.None : next.Id;

            // Offered here and nowhere else. A player who has just finished a chapter has
            // something worth keeping, which is exactly when asking them to protect it is
            // a service rather than an obstacle — and the answer costs nothing either way.
            bool offerAccount = FinishedAChapter(catalog) && AccountOverlay.ShouldOffer();

            var nextButton = UIKit.TextButton("Next", Panel, "btn_green",
                                        Loc.Get(last ? "ui.win.glades" : "ui.win.next"), 48,
                                        new Vector2(560f, 140f), new Vector2(.5f, 0f), new Vector2(0f, 232f),
                                        () => Close(() =>
                                        {
                                            if (offerAccount)
                                            {
                                                AccountOverlay.NoteOffered();
                                                Flow.Modal<AccountOverlay>();
                                                return;
                                            }
                                            if (last) Flow.Go<LevelsScreen>();
                                            else Flow.Go<PlayScreen>(v => v.LevelId = nextId);
                                        }));
            UIKit.Halo(nextButton.transform, Pal.Mint, 640f, .3f);

            var replayId = LevelId;
            UIKit.IconButton("Replay", Panel, "sq_orange", "ic_restart", new Vector2(120f, 120f),
                             new Vector2(.5f, 0f), new Vector2(-232f, 96f),
                             () => Close(() => Flow.Go<PlayScreen>(v => v.LevelId = replayId)));
            UIKit.IconButton("Map", Panel, "sq_blue", "ic_list", new Vector2(120f, 120f),
                             new Vector2(.5f, 0f), new Vector2(232f, 96f),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            Tween.After(.9f, () =>
            {
                if (!this) return;
                Sheen.Attach((RectTransform)nextButton.transform, 3.2f);
                Tween.Breathe(nextButton.transform, .025f, 2f);
            }, this);

            if (FirstClear && !last)
            {
                string opened = Loc.Format("ui.win.opened", Loc.Get(next.NameKey));
                Tween.After(1.0f + .42f * Stars, () =>
                {
                    if (!this) return;
                    Audio.Sfx("unlock", .6f);
                    Scenery.Toast(Content, opened, Pal.Gold, 2.2f, new Vector2(.5f, 0f), 190f);
                }, this);
            }
        }

        public override bool OnBack() { Close(() => Flow.Go<LevelsScreen>()); return true; }
    }

    // ==================================================================== how to
    public sealed class HowToOverlay : ModalView
    {
        protected override void Build()
        {
            MakePanel(new Vector2(880f, 1220f), Loc.Get("ui.howto.title"));

            // Body copy lives in the string table like everything else the player reads.
            string[] lineKeys =
            {
                "ui.howto.line1", "ui.howto.line2", "ui.howto.line3",
                "ui.howto.line4", "ui.howto.line5", "ui.howto.line6",
            };
            for (int i = 0; i < lineKeys.Length; i++)
            {
                float y = -212f - i * 106f;
                var dot = UIKit.Img("dot" + i, Panel, Art.Disc(64), Pal.EnergyColour(1 + (i % 7)),
                                    new Vector2(26f, 26f), new Vector2(0f, 1f), new Vector2(96f, y + 4f));
                var t = UIKit.Titled("l" + i, Panel, Loc.Get(lineKeys[i]), 32, new Color(.36f, .25f, .18f),
                                     TextAnchor.UpperLeft, new Vector2(620f, 98f), new Vector2(0f, 1f),
                                     new Vector2(452f, y - 16f), 0f, 0f);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                var tr = (RectTransform)t.transform;
                tr.localScale = Vector3.zero;
                Tween.Pop(tr, 0f, .45f, .12f + i * .07f);
                Tween.Pop(dot.transform, 0f, .45f, .12f + i * .07f);
            }

            var mix = UIKit.Box("Mix", Panel, new Vector2(700f, 150f), new Vector2(.5f, 1f), new Vector2(0f, -930f));
            Swatch(mix, Pal.Ember, -230f, "ui.howto.red");
            UIKit.Titled("plus", mix, "+", 46, new Color(.4f, .3f, .22f), TextAnchor.MiddleCenter,
                         new Vector2(60f, 60f), new Vector2(.5f, .5f), new Vector2(-140f, 14f), 0f, 0f);
            Swatch(mix, Pal.Azure, -50f, "ui.howto.blue");
            UIKit.Titled("eq", mix, "=", 46, new Color(.4f, .3f, .22f), TextAnchor.MiddleCenter,
                         new Vector2(60f, 60f), new Vector2(.5f, .5f), new Vector2(45f, 14f), 0f, 0f);
            Swatch(mix, Pal.Bloom, 150f, "ui.howto.blossom");

            UIKit.TextButton("Ok", Panel, "btn_green", Loc.Get("ui.common.got_it"), 48, new Vector2(520f, 132f),
                             new Vector2(.5f, 0f), new Vector2(0f, 86f), () => Close());
        }

        void Swatch(Transform parent, Color colour, float x, string labelKey)
        {
            var glow = UIKit.Img("g", parent, Art.Glow(96, 1.9f), Pal.A(colour, .6f),
                                 new Vector2(128f, 128f), new Vector2(.5f, .5f), new Vector2(x, 24f));
            var d = UIKit.Img("d", parent, Art.Disc(96), Pal.Lift(colour, .2f),
                              new Vector2(68f, 68f), new Vector2(.5f, .5f), new Vector2(x, 24f));
            Tween.Breathe(glow.transform, .09f, 1.9f, x * .01f);
            UIKit.Titled("t", parent, Loc.Get(labelKey), 24, new Color(.42f, .31f, .23f), TextAnchor.MiddleCenter,
                         new Vector2(200f, 34f), new Vector2(.5f, .5f), new Vector2(x, -32f), 0f, 0f);
        }

        public override bool OnBack() { Close(); return true; }
    }

    // ================================================================= settings
    public sealed class SettingsOverlay : ModalView
    {
        protected override void Build()
        {
            MakePanel(new Vector2(860f, 900f), Loc.Get("ui.settings.title"));

            var row = UIKit.Box("Toggles", Panel, new Vector2(700f, 200f), new Vector2(.5f, 1f), new Vector2(0f, -260f));
            Toggle(row, "ic_music", new Vector2(-190f, 0f), () => GameSettings.MusicOn, GameSettings.SetMusic);
            Toggle(row, "ic_audio", new Vector2(0f, 0f), () => GameSettings.SfxOn,
                   on => { GameSettings.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });
            Toggle(row, "ic_gear", new Vector2(190f, 0f), () => GameSettings.HapticsOn, GameSettings.SetHaptics);

            Caption(row, "ui.settings.music", -190f);
            Caption(row, "ui.settings.sound", 0f);
            Caption(row, "ui.settings.buzz", 190f);

            UIKit.Titled("Ver", Panel, Loc.Format("ui.settings.version", Application.version), 28, new Color(.44f, .32f, .24f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 40f), new Vector2(.5f, 1f),
                         new Vector2(0f, -430f), 0f, 0f);
            UIKit.Titled("Credit", Panel, Loc.Get("ui.settings.credit"), 24, new Color(.52f, .40f, .31f, .85f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 36f), new Vector2(.5f, 1f),
                         new Vector2(0f, -474f), 0f, 0f);

            UIKit.TextButton("Account", Panel, "btn_blue", Loc.Get("ui.settings.account"), 38,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f), new Vector2(0f, 406f),
                             () => Close(() => Flow.Modal<AccountOverlay>()));

            UIKit.TextButton("Wipe", Panel, "btn_red", Loc.Get("ui.settings.reset"), 38, new Vector2(560f, 120f),
                             new Vector2(.5f, 0f), new Vector2(0f, 262f), ConfirmWipe);
            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46, new Vector2(560f, 132f),
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
                if (label) label.text = Loc.Get("ui.settings.reset_confirm");
                Audio.Sfx("nope", .5f);
                Tween.Shake((RectTransform)btn, 9f, .3f);
                Tween.After(3.2f, () =>
                {
                    if (this == null) return;
                    _armed = false;
                    if (label) label.text = Loc.Get("ui.settings.reset");
                }, this);
                return;
            }
            SaveService.Wipe();
            Audio.Sfx("shatter", .55f);
            Close(() => Flow.Go<HomeScreen>());
        }

        static void Caption(Transform parent, string key, float x)
            => UIKit.Titled("C_" + key, parent, Loc.Get(key), 26, new Color(.44f, .32f, .24f, .9f),
                            TextAnchor.MiddleCenter, new Vector2(200f, 34f), new Vector2(.5f, .5f),
                            new Vector2(x, -84f), 0f, 0f);

        public override bool OnBack() { Close(); return true; }
    }

    // ============================================================== coming soon
    public sealed class ComingSoonOverlay : ModalView
    {
        string _titleKey = "ui.common.coming_soon", _icon = "ic_chest", _bodyKey = "";

        /// <summary>Titles and body arrive as localisation keys, never as text.</summary>
        public void Configure(string titleKey, string icon, string bodyKey)
        {
            _titleKey = titleKey; _icon = icon; _bodyKey = bodyKey;
        }

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 840f), Loc.Get(_titleKey).ToUpperInvariant());

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
            UIKit.Titled("T", ribbon.transform, Loc.Get("ui.common.coming_soon"), 38, Pal.Cream, TextAnchor.MiddleCenter,
                         outline: 3f, shadow: 3f);
            ribbon.transform.localRotation = Quaternion.Euler(0, 0, -2.4f);

            var body = UIKit.Titled("Body", Panel, Loc.Get(_bodyKey), 32, new Color(.40f, .28f, .20f),
                                    TextAnchor.UpperCenter, new Vector2(700f, 120f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -580f), 0f, 0f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            UIKit.TextButton("Ok", Panel, "btn_green", Loc.Get("ui.common.got_it"), 46, new Vector2(520f, 128f),
                             new Vector2(.5f, 0f), new Vector2(0f, 96f), () => Close());
        }

        public override bool OnBack() { Close(); return true; }
    }
}
