using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The hub. Rank and grove progress are real; hearts, coins and gems come from
    /// <see cref="Profile"/> and are placeholders until the economy is built.
    /// </summary>
    public sealed class HomeScreen : View
    {
        public override string Track => "mus_menu";

        Image _hero;

        protected override void Build()
        {
            BuildBackdrop();
            BuildTopBar();
            BuildResources();
            BuildProgress();
            BuildHero();
            BuildPlay();
            NavBar.Build(Content, NavBar.Tab.Home);
        }

        // ------------------------------------------------------------- backdrop
        void BuildBackdrop()
        {
            Cover("Bg/grove_far", 1f, 0f);
            var light = Art.S("Bg/grove_light");
            if (light != null)
            {
                var img = UIKit.Img("Light", Content, light, new Color(1f, 1f, 1f, .55f));
                Fit(img, light, 1f);
                Tween.Run(3.4f, Ease.InOutSine,
                    t => { if (img) img.color = new Color(1f, 1f, 1f, Mathf.Lerp(.42f, .68f, t)); },
                    img, "pulse").Loop(-1, true);
            }
            Cover("Bg/grove_near", .9f, 18f);
            Fireflies.Spawn(Content, 30, new Color(1f, .93f, .68f), 7f, 26f);
            var vig = UIKit.Img("Vignette", Content, Art.Vignette(256), new Color(.01f, .04f, .06f, .58f));
            vig.type = Image.Type.Simple;
        }

        void Cover(string path, float alpha, float parallax)
        {
            var s = Art.S(path);
            if (s == null) return;
            var img = UIKit.Img(path, Content, s, new Color(1, 1, 1, alpha));
            Fit(img, s, parallax > 0f ? 1.05f : 1f);
            if (parallax > 0f) Parallax.Attach((RectTransform)img.transform, parallax);
        }

        static void Fit(Image img, Sprite s, float scale)
        {
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * scale;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = s.rect.width / s.rect.height;
        }

        // -------------------------------------------------------------- top bar
        void BuildTopBar()
        {
            var bar = UIKit.Box("TopBar", Content, new Vector2(0f, 168f), new Vector2(.5f, 1f), new Vector2(0f, -116f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, 168f);

            var card = UIKit.Img("Card", bar, Art.Round(26), new Color(.05f, .11f, .14f, .74f),
                                 new Vector2(500f, 132f), new Vector2(0f, .5f), new Vector2(292f, 0f));
            var cardEdge = UIKit.Img("Edge", card.transform, Art.RoundOutline(26, 3f), new Color(1, 1, 1, .16f));
            UIKit.StretchTo((RectTransform)cardEdge.transform, 0, 0, 0, 0);

            // avatar
            var frame = UIKit.Img("Avatar", card.transform, Art.S("Ui/sq_dark"), Color.white,
                                  new Vector2(112f, 112f), new Vector2(0f, .5f), new Vector2(66f, 0f));
            var face = UIKit.Img("Face", frame.transform, null, Color.white,
                                 new Vector2(84f, 84f), new Vector2(.5f, .5f), new Vector2(0f, 2f));
            face.preserveAspect = true;

            // The companion the player chose, not a hardcoded critter. Animated when it
            // has frames, which the boot preload has already warmed.
            CompanionArt.Paint(face, Profile.Avatar, animate: true);
            var fb = face.GetComponent<Flipbook>();
            if (fb) fb.Offset = 3;

            var rank = UIKit.Img("RankBadge", frame.transform, Art.Disc(64), Pal.Gold,
                                 new Vector2(50f, 50f), new Vector2(1f, 0f), new Vector2(-4f, 4f));
            UIKit.Titled("N", rank.transform, Profile.Rank.ToString(), 30, new Color(.32f, .21f, .06f),
                         TextAnchor.MiddleCenter, outline: 0f, shadow: 0f);

            UIKit.Titled("Name", card.transform, Profile.Name, 36, Pal.Cream, TextAnchor.LowerLeft,
                         new Vector2(310f, 46f), new Vector2(0f, .5f), new Vector2(287f, 22f), 3f, 3f);

            // rank experience bar, filled by stars toward the next rank
            var track = UIKit.Img("XpTrack", card.transform, Art.Round(12), new Color(.02f, .05f, .07f, .85f),
                                  new Vector2(300f, 26f), new Vector2(0f, .5f), new Vector2(282f, -22f));
            var xp = UIKit.Img("XpFill", track.transform, Art.Round(10), Pal.Mint,
                               new Vector2(0f, 18f), new Vector2(0f, .5f), new Vector2(4f, 0f));
            var xpRT = (RectTransform)xp.transform;
            xpRT.pivot = new Vector2(0f, .5f);
            xpRT.sizeDelta = new Vector2(0f, 18f);
            float w = 292f * Profile.RankProgress;
            Tween.Run(.7f, Ease.OutCubic, t => { if (xpRT) xpRT.sizeDelta = new Vector2(w * t, 18f); }, xp).Delay(.35f);

            // corner buttons
            UIKit.IconButton("Settings", bar, "sq_dark", "ic_gear", new Vector2(104f, 104f),
                             new Vector2(1f, .5f), new Vector2(-92f, 0f), () => Flow.Modal<SettingsOverlay>());
            UIKit.IconButton("Info", bar, "sq_dark", "ic_info", new Vector2(104f, 104f),
                             new Vector2(1f, .5f), new Vector2(-206f, 0f), () => Flow.Modal<HowToOverlay>());
        }

        /// <summary>Either "full" or how long until the next heart, as m:ss.</summary>
        static string HeartsLine()
        {
            if (Profile.Hearts >= Profile.MaxHearts) return Loc.Get("ui.hearts.full");

            return string.Format(Loc.Get("ui.hearts.next"), Profile.HeartCountdown());
        }

        void BuildResources()
        {
            var row = UIKit.Box("Resources", Content, new Vector2(1000f, 92f), new Vector2(.5f, 1f), new Vector2(0f, -250f));

            // Hearts are real now, so this is no longer a "coming soon" — an empty
            // player gets the gate with its live countdown, everyone else gets told
            // when the next one lands.
            ResourcePill(row, -318f, Pal.Rose, "ic_heart", $"{Profile.Hearts}/{Profile.MaxHearts}", false,
                         () =>
                         {
                             if (!Profile.CanPlay) Flow.Modal<OutOfHeartsOverlay>();
                             else Scenery.Toast(Content, HeartsLine(), Pal.Rose, 2.4f);
                         });
            ResourcePill(row, 0f, Pal.Gold, null, Profile.Short(Profile.Coins), true,
                         () => Flow.Modal<ComingSoonOverlay>(v => v.Configure("Coins", "ic_chest",
                             "Earn coins in the glades and spend them in the shop. Coming soon.")));
            ResourcePill(row, 318f, Pal.Bloom, "ic_gem", Profile.Short(Profile.Gems), false,
                         () => Flow.Modal<ComingSoonOverlay>(v => v.Configure("Gems", "ic_gem",
                             "Gems will unlock hints, skins and seasonal glades.")));
        }

        /// <summary>Resource readout with an add button. Coins use the spinning sprite.</summary>
        void ResourcePill(Transform parent, float x, Color tint, string icon, string value,
                          bool animatedCoin, Action onAdd)
        {
            var bg = UIKit.Img("Pill", parent, Art.Round(24), new Color(.04f, .09f, .12f, .78f),
                               new Vector2(268f, 80f), new Vector2(.5f, .5f), new Vector2(x, 0f));
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(24, 3f), Pal.A(tint, .45f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(120f, 120f), new Vector2(0f, .5f), new Vector2(46f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, animatedCoin ? null : Art.S("Ui/" + icon), Color.white,
                               new Vector2(62f, 62f), new Vector2(0f, .5f), new Vector2(46f, 0f));
            ic.preserveAspect = true;
            if (animatedCoin) Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .06f, 2.2f, x * .01f);

            var t = UIKit.Titled("V", bg.transform, value, 36, Pal.Cream, TextAnchor.MiddleCenter,
                                 new Vector2(120f, 50f), new Vector2(.5f, .5f), new Vector2(6f, 0f), 3f, 3f);

            var add = UIKit.Button("Add", bg.transform, Art.S("Ui/sq_green"), new Vector2(58f, 58f),
                                   new Vector2(1f, .5f), new Vector2(-16f, 0f), onAdd);
            var plus = UIKit.Img("P", add.transform, Art.S("Ui/ic_plus"), Pal.Cream,
                                 new Vector2(28f, 28f), new Vector2(.5f, .5f), new Vector2(0f, 1f));
            plus.preserveAspect = true;

            bg.transform.localScale = Vector3.zero;
            Tween.Pop(bg.transform, 0f, .55f, .18f + Mathf.Abs(x) * .0004f);
        }

        // ----------------------------------------------------------- progression
        void BuildProgress()
        {
            int have = Profile.TotalStars, max = Profile.MaxStars;

            var panel = UIKit.Img("Progress", Content, Art.Round(28), new Color(.04f, .09f, .12f, .72f),
                                  new Vector2(900f, 196f), new Vector2(.5f, 1f), new Vector2(0f, -432f));
            var edge = UIKit.Img("Edge", panel.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .14f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Titled("Title", panel.transform, "GROVE AWAKENING", 32, Pal.Gold, TextAnchor.MiddleLeft,
                         new Vector2(460f, 40f), new Vector2(0f, 1f), new Vector2(358f, -36f), 3f, 3f);
            UIKit.Titled("Count", panel.transform, $"{have} / {max}", 32, Pal.Cream, TextAnchor.MiddleRight,
                         new Vector2(200f, 40f), new Vector2(1f, 1f), new Vector2(-142f, -36f), 3f, 3f);

            var track = UIKit.Img("Track", panel.transform, Art.Round(16), new Color(.02f, .05f, .07f, .9f),
                                  new Vector2(816f, 38f), new Vector2(.5f, .5f), new Vector2(0f, -14f));
            var fill = UIKit.Img("Fill", track.transform, Art.Round(14), Pal.Gold,
                                 new Vector2(0f, 28f), new Vector2(0f, .5f), new Vector2(5f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            float full = 806f * Profile.GroveProgress;
            Tween.Run(.9f, Ease.OutCubic, t =>
            {
                if (!fillRT) return;
                fillRT.sizeDelta = new Vector2(full * t, 28f);
                fill.color = Color.Lerp(Pal.Sun, Pal.Gold, t);
            }, fill).Delay(.5f);

            // milestone chests at a third, two thirds and the whole grove
            for (int i = 0; i < 3; i++)
            {
                bool reached = Profile.MilestoneReached(i);
                float px = -408f + 816f * ((i + 1) / 3f);
                var chest = UIKit.Img("M" + i, track.transform,
                                      Art.S(reached ? "Ui/ic_chest_open" : "Ui/ic_chest"),
                                      reached ? Color.white : new Color(.55f, .58f, .62f, .95f),
                                      new Vector2(74f, 74f), new Vector2(.5f, .5f), new Vector2(px, 6f));
                chest.preserveAspect = true;
                if (reached)
                {
                    UIKit.Halo(chest.transform, Pal.Gold, 130f, .5f);
                    Tween.Breathe(chest.transform, .07f, 1.6f, i * .7f);
                }
            }

            UIKit.Titled("Hint", panel.transform,
                         have >= max ? "every glade is awake" : "earn stars to open the next chest",
                         24, new Color(1f, .95f, .84f, .55f), TextAnchor.MiddleCenter,
                         new Vector2(880f, 34f), new Vector2(.5f, 0f), new Vector2(0f, 26f), 0f, 0f);

            var icon = UIKit.Img("Star", panel.transform, Art.S("Ui/star_full"), Color.white,
                                 new Vector2(72f, 72f), new Vector2(0f, 1f), new Vector2(64f, -40f));
            icon.preserveAspect = true;
            Tween.Breathe(icon.transform, .07f, 2.1f);

            panel.transform.localScale = Vector3.zero;
            Tween.Pop(panel.transform, 0f, .6f, .3f);
        }

        // ---------------------------------------------------------------- hero
        void BuildHero()
        {
            var host = UIKit.Box("Hero", Content, new Vector2(600f, 500f), new Vector2(.5f, .5f), new Vector2(0f, 96f));

            var rock = UIKit.Img("Rock", host, Art.S("Map/rock_grass"), Color.white,
                                 new Vector2(420f, 330f), new Vector2(.5f, .5f), new Vector2(0f, -152f));
            rock.preserveAspect = true;
            Tween.Bob((RectTransform)rock.transform, 9f, 3.4f);

            // The companion the player chose, not a fixed critter. This is where the
            // choice actually pays off: the profile is where you pick one, the hub is
            // where you live with it. The screen is rebuilt on every navigation, so
            // coming back from the profile already shows the new one.
            _hero = UIKit.Img("Critter", host, null, Color.white,
                              new Vector2(318f, 318f), new Vector2(.5f, .5f), new Vector2(0f, 30f));
            _hero.preserveAspect = true;
            CompanionArt.Paint(_hero, Profile.Avatar, animate: true);
            Tween.Bob((RectTransform)_hero.transform, 12f, 3.4f, .35f);
            UIKit.Halo(host, new Color(1f, .88f, .55f), 580f, .24f);

            // the hero answers a poke
            var hit = UIKit.Button("Poke", host, Art.Pixel, new Vector2(340f, 340f),
                                   new Vector2(.5f, .5f), new Vector2(0f, 30f), Poke);
            hit.GetComponent<Image>().color = new Color(1, 1, 1, 0);
            hit.ClickSfx = null;
            hit.PressScale = 1f;

            host.localScale = Vector3.zero;
            Tween.Pop(host, 0f, .75f, .42f);
        }

        void Poke()
        {
            if (_hero == null) return;
            Tween.Punch(_hero.transform, .28f, .5f);
            Audio.SfxVaried("pop", .5f, .18f);
            Burst.Sparks(_hero.transform, new Vector2(0f, 40f), Pal.Gold, 8, 150f, 22f, .6f);
        }

        // ----------------------------------------------------------- play + nav
        void BuildPlay()
        {
            var play = UIKit.TextButton("Play", Content, "btn_green", "PLAY", 66,
                                        new Vector2(600f, 172f), new Vector2(.5f, 0f), new Vector2(0f, NavBar.Height + 190f),
                                        () => Flow.Go<LevelsScreen>());
            UIKit.Halo(play.transform, Pal.Mint, 760f, .3f);
            play.transform.localScale = Vector3.zero;
            Tween.Pop(play.transform, 0f, .7f, .62f).OnDone(() =>
            {
                if (!play) return;
                play.Rehome();
                Tween.Breathe(play.transform, .03f, 2.1f);
                Sheen.Attach((RectTransform)play.transform, 3.4f);
            });

            UIKit.Titled("Next", Content, NextGladeLine(), 27, new Color(1f, .96f, .86f, .68f),
                         TextAnchor.MiddleCenter, new Vector2(900f, 38f), new Vector2(.5f, 0f),
                         new Vector2(0f, NavBar.Height + 84f), 3f, 0f);
        }

        static string NextGladeLine()
        {
            var next = LevelUnlock.NextToPlay(GameContent.Index);

            if (!next.IsValid || PlayerProgress.IsCleared(next))
                return Loc.Get("ui.home.all_awake");

            // Named from the id alone, so the home screen never reads a chapter file
            // just to draw one line of text.
            return Loc.Format("ui.home.next_up", Loc.Get(LevelDefinition.DefaultNameKey(next)));
        }

        public override void OnPresented()
        {
            // the line under PLAY already says where to go, so no toast is needed here
        }
    }
}
