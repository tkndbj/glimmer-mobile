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

    // ===================================================================== defeat
    /// <summary>
    /// Shown when a run is lost.
    ///
    /// Built from the same panel furniture as every other overlay rather than from a
    /// painted banner, for one reason above the rest: a banner with the word "Defeat"
    /// baked into it cannot be translated, and this game ships everywhere. Every string
    /// here is a loc key, so a new language is a file rather than an art order.
    ///
    /// The tone is deliberately gentle. A defeat already costs a heart; a screen that
    /// also scolds is how a player decides the game is not for them. It names what went
    /// wrong, shows what it cost, and puts "try again" under their thumb.
    /// </summary>
    public sealed class DefeatOverlay : ModalView
    {
        public PlayScreen Screen;
        public int HeartsLeft;
        public DefeatReason Reason;
        public int LampsLit, LampCount;

        /// <summary>False when the player was already at zero — then nothing was taken.</summary>
        public bool HeartWasCharged;


        /// <summary>
        /// Written out rather than built from the enum name, so the loc gate can see
        /// every key. A concatenated key is invisible to the scanner and ships missing.
        /// </summary>
        static string TitleKey(DefeatReason reason)
            => reason == DefeatReason.ConduitLost ? "ui.defeat.conduit_title" : "ui.defeat.moves_title";

        static string ReasonKey(DefeatReason reason)
            => reason == DefeatReason.ConduitLost ? "ui.defeat.conduit_reason" : "ui.defeat.moves_reason";

        protected override void Build()
        {
            bool canRetry = HeartsLeft > 0;

            MakePanel(new Vector2(880f, 880f), Loc.Get(TitleKey(Reason)), dismissOnScrim: false);

            // Body copy, drawn the way every other panel here draws it: wrapped, and
            // with no outline or shadow. Those two are for headings sitting on a ribbon;
            // on a 32pt sentence they smear the strokes together and it stops reading.
            Body("Why", Loc.Get(ReasonKey(Reason)), -186f, 150f);

            // Running out of turns gives no clue how close you were, so say it. After a
            // crumble the player watched the cause, and a score would only distract.
            if (Reason == DefeatReason.OutOfMoves && LampCount > 0)
                UIKit.Titled("Score", Panel, $"{LampsLit}/{LampCount}", 44,
                             LampsLit >= LampCount - 1 ? Pal.Gold : Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(400f, 70f),
                             new Vector2(.5f, 1f), new Vector2(0f, -300f), outline: 3f, shadow: 3f);

            BuildHearts();

            if (canRetry)
            {
                UIKit.TextButton("Retry", Panel, "btn_green", Loc.Get("ui.defeat.try_again"), 52,
                                 new Vector2(620f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -560f),
                                 () => Close(() => { if (Screen) Screen.RetryAfterDefeat(); }));
            }
            else
            {
                // Out of hearts: a retry button would be a lie, so it is not offered.
                Body("Wait", Loc.Get("ui.defeat.out_of_hearts"), -540f, 130f, Pal.Ember);
            }

            UIKit.TextButton("Glades", Panel, "btn_blue", Loc.Get("ui.pause.glades"), 46,
                             new Vector2(620f, 132f), new Vector2(.5f, 1f),
                             new Vector2(0f, canRetry ? -722f : -700f),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            Audio.Sfx("nope", .5f, .8f, .05f);
        }

        /// <summary>Wrapped, unadorned panel prose. Shared so both states line up.</summary>
        Text Body(string name, string text, float y, float height, Color? colour = null)
            => UIKit.Titled(name, Panel, text, 32, colour ?? new Color(.36f, .25f, .18f),
                            TextAnchor.UpperCenter, new Vector2(680f, height),
                            new Vector2(.5f, 1f), new Vector2(0f, y),
                            outline: 0f, shadow: 0f, wrap: true);

        /// <summary>
        /// The heart row, with the one just lost drawn empty and struck through by a
        /// short animation. Showing the cost is the point — a resource that quietly
        /// decrements is a resource players feel cheated by later.
        /// </summary>
        void BuildHearts()
        {
            var row = UIKit.Node("Hearts", Panel);
            row.anchorMin = row.anchorMax = new Vector2(.5f, 1f);
            row.pivot = new Vector2(.5f, .5f);
            row.sizeDelta = new Vector2(600f, 120f);
            row.anchoredPosition = new Vector2(0f, -400f);

            const float step = 96f;
            float left = -(HeartRules.Max - 1) * step * .5f;

            for (int k = 0; k < HeartRules.Max; k++)
            {
                bool held = k < HeartsLeft;
                bool justLost = HeartWasCharged && k == HeartsLeft;

                var heart = UIKit.Img("H" + k, row, Art.S("Ui/ic_heart"),
                                      held ? Pal.Rose : new Color(.62f, .58f, .60f, .38f),
                                      Vector2.one * 78f, new Vector2(.5f, .5f),
                                      new Vector2(left + k * step, 0f));
                heart.preserveAspect = true;

                if (!justLost) continue;

                // the one that was taken: full for a beat, then drained
                heart.color = Pal.Rose;
                Tween.Punch(heart.transform, .3f, .4f).Delay(.18f);
                Tween.Tint(heart, new Color(.62f, .58f, .60f, .38f), .45f, Ease.InQuad).Delay(.30f);
            }
        }
    }

    // ============================================================= out of hearts
    /// <summary>
    /// The door, when the player has no hearts to spend.
    ///
    /// It counts down live rather than showing a static number, because a wait you can
    /// watch shrink is a wait; a wait you have to re-open a screen to measure is a
    /// wall. The countdown reads <see cref="Profile.SecondsToNextHeart"/> each frame —
    /// the heart state catches itself up on read, so this stays correct across a
    /// backgrounded app without any resume plumbing.
    ///
    /// There is no "buy hearts" button yet, and that is deliberate: the store secrets
    /// still hold UNSET, so an offer here would be a button that cannot work. This is
    /// where it goes when it exists.
    /// </summary>
    public sealed class OutOfHeartsOverlay : ModalView
    {
        Text _countdown;

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 780f), Loc.Get("ui.hearts.empty"));

            // Wrapped and unadorned, matching every other body paragraph in the game.
            UIKit.Titled("Why", Panel, Loc.Get("ui.hearts.wait_to_play"), 32,
                         new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                         new Vector2(680f, 150f), new Vector2(.5f, 1f), new Vector2(0f, -190f),
                         outline: 0f, shadow: 0f, wrap: true);

            var empty = UIKit.Img("Heart", Panel, Art.S("Ui/ic_heart"),
                                  new Color(.62f, .58f, .60f, .45f), Vector2.one * 138f,
                                  new Vector2(.5f, 1f), new Vector2(0f, -380f));
            empty.preserveAspect = true;
            Tween.Breathe(empty.transform, .05f, 2.2f);

            // The countdown is a heading, not prose, so it keeps its outline — it is
            // the one number on this panel the player actually came to read.
            _countdown = UIKit.Titled("Clock", Panel, string.Empty, 52, Pal.Rose,
                                      TextAnchor.MiddleCenter, new Vector2(640f, 84f),
                                      new Vector2(.5f, 1f), new Vector2(0f, -500f),
                                      outline: 3f, shadow: 3f);
            Paint();

            UIKit.TextButton("Ok", Panel, "btn_green", Loc.Get("ui.common.got_it"), 48,
                             new Vector2(560f, 136f), new Vector2(.5f, 1f), new Vector2(0f, -630f),
                             () => Close());
        }

        void Update() => Paint();

        void Paint()
        {
            if (!_countdown) return;

            long seconds = Profile.SecondsToNextHeart;

            // The clock ran out while they were looking at it — let them straight in.
            if (Profile.CanPlay) { Close(); return; }

            _countdown.text = seconds <= 0
                ? Loc.Get("ui.hearts.full")
                : string.Format(Loc.Get("ui.hearts.next"), Profile.Countdown(seconds));
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
        bool FinishedAChapter(CatalogIndex index)
        {
            var chapter = index.ChapterOf(LevelId);
            if (!chapter.IsValid) return false;

            foreach (var sibling in index.LevelsOf(chapter))
                if (!PlayerProgress.IsCleared(sibling)) return false;

            return true;
        }

        protected override void Build()
        {
            // "Last" means the catalog has nothing after this level, not a fixed count,
            // so publishing a new chapter turns the end screen back into a next button
            // without any code here changing.
            var index = GameContent.Index;
            var next = index.Next(LevelId);
            bool last = !next.IsValid;

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
            if (XpGained > 0 || CreditsGained > 0)
            {
                var reward = UIKit.Titled("Reward", Panel,
                                          Loc.Format("ui.win.reward", XpGained, CreditsGained), 32, Pal.Mint,
                                          TextAnchor.MiddleCenter, new Vector2(760f, 44f),
                                          new Vector2(.5f, 1f), new Vector2(0f, -462f), 2f, 2f);
                reward.transform.localScale = Vector3.zero;
                Tween.Pop(reward.transform, 0f, .55f, 1.15f + .42f * Stars);
            }

            var nextId = last ? LevelId.None : next;

            // Offered here and nowhere else. A player who has just finished a chapter has
            // something worth keeping, which is exactly when asking them to protect it is
            // a service rather than an obstacle — and the answer costs nothing either way.
            bool offerAccount = FinishedAChapter(index) && AccountOverlay.ShouldOffer();

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
                string opened = Loc.Format("ui.win.opened", Loc.Get(LevelDefinition.DefaultNameKey(next)));
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
                                     new Vector2(452f, y - 16f), 0f, 0f, wrap: true);
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
            MakePanel(new Vector2(860f, 800f), Loc.Get("ui.settings.title"));

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
                         new Vector2(0f, -420f), 0f, 0f);
            UIKit.Titled("Credit", Panel, Loc.Get("ui.settings.credit"), 24, new Color(.52f, .40f, .31f, .85f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 36f), new Vector2(.5f, 1f),
                         new Vector2(0f, -462f), 0f, 0f);

            // Account lives on the profile screen, not here. It is the one part of
            // settings that is about *who the player is* rather than how the game
            // behaves, and burying the thing that protects a grove three taps deep in
            // a preferences panel is how it stayed unfound.
            UIKit.TextButton("Wipe", Panel, "btn_red", Loc.Get("ui.settings.reset"), 38, new Vector2(560f, 120f),
                             new Vector2(.5f, 0f), new Vector2(0f, 250f), ConfirmWipe);
            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46, new Vector2(560f, 132f),
                             new Vector2(.5f, 0f), new Vector2(0f, 108f), () => Close());
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
        string _titleKey = "ui.common.coming_soon", _bodyKey = "";
        Sprite _icon;

        /// <summary>Titles and body arrive as localisation keys, never as text.</summary>
        public void Configure(string titleKey, string icon, string bodyKey)
            => Configure(titleKey, Art.S("Ui/" + icon), bodyKey);

        /// <summary>Takes the glyph itself, for callers that already hold one.</summary>
        public void Configure(string titleKey, Sprite icon, string bodyKey)
        {
            _titleKey = titleKey; _icon = icon; _bodyKey = bodyKey;
        }

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 840f), Loc.Get(_titleKey).ToUpperInvariant());

            var glow = UIKit.Img("Glow", Panel, Art.Glow(128, 1.9f), Pal.A(Pal.Gold, .45f),
                                 new Vector2(380f, 380f), new Vector2(.5f, 1f), new Vector2(0f, -290f));

            // Dark medallion under the glyph. Half these icons are white silhouettes,
            // which are all but invisible on the parchment panel; the ones painted in
            // full colour lose nothing by sitting on it.
            var disc = UIKit.Img("Disc", Panel, Art.Disc(256), Pal.A(Pal.Hex("#08333C"), .92f),
                                 new Vector2(298f, 298f), new Vector2(.5f, 1f), new Vector2(0f, -290f));
            var ring = UIKit.Img("Ring", disc.transform, Art.Ring(256, 14f), Pal.A(Pal.Gold, .90f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            var icon = UIKit.Img("Icon", Panel, _icon != null ? _icon : Art.S("Ui/ic_chest"), Color.white,
                                 new Vector2(200f, 200f), new Vector2(.5f, 1f), new Vector2(0f, -290f));
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
