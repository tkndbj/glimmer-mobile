using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
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
        RectTransform _dailyPanel;
        RectTransform _resourceRow;
        Text _resetClock;
        float _clockTick;

        protected override void Build()
        {
            BuildBackdrop();
            BuildTopBar();
            BuildResources();
            BuildDaily();
            BuildHero();
            BuildPlay();
            NavBar.Build(Content, NavBar.Tab.Home);

            // Midnight arrives while a screen is open exactly as often as it arrives while
            // it is not, and opening a chest changes both panels from under themselves.
            DailyChests.Changed += OnDailyChanged;
            PlayerProgression.Changed += OnWalletChanged;
            Wallet.HeartsChanged += OnHeartsChanged;
        }

        void OnDestroy()
        {
            DailyChests.Changed -= OnDailyChanged;
            PlayerProgression.Changed -= OnWalletChanged;
            Wallet.HeartsChanged -= OnHeartsChanged;
        }

        void OnDailyChanged()
        {
            // Guarded because the event can arrive from a save load during teardown, and
            // rebuilding a panel onto a destroyed screen throws where nobody is looking.
            if (this == null || !_dailyPanel) return;
            BuildDaily();
        }

        void OnHeartsChanged(Hearts hearts) => OnWalletChanged();

        void OnWalletChanged()
        {
            if (this == null || !_resourceRow) return;
            BuildResources();
        }

        /// <summary>
        /// Ticks the reset clock once a second.
        ///
        /// Only the one label, and only when it would actually change. Rebuilding the
        /// panel every second would restart every tween on it, which is how a shine
        /// becomes a stutter.
        /// </summary>
        void Update()
        {
            _clockTick += Time.unscaledDeltaTime;
            if (_clockTick < 1f) return;

            _clockTick = 0f;
            if (_resetClock) _resetClock.text = ResetLine();
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

        /// <summary>
        /// Hearts, coins and gems.
        ///
        /// Rebuilt on change rather than painted once. All three move while this screen is
        /// open — a heart lands on a timer, and a chest pays out into an overlay drawn on
        /// top of it — and a pill still showing the number from thirty seconds ago is how
        /// a player concludes the reward did not arrive.
        /// </summary>
        void BuildResources()
        {
            int sibling = _resourceRow ? _resourceRow.GetSiblingIndex() : -1;
            if (_resourceRow) Destroy(_resourceRow.gameObject);

            var row = UIKit.Box("Resources", Content, new Vector2(1000f, 92f), new Vector2(.5f, 1f), new Vector2(0f, -250f));
            _resourceRow = row;
            if (sibling >= 0) row.SetSiblingIndex(sibling);

            // Hearts are real now, so this is no longer a "coming soon" — an empty
            // player gets the gate with its live countdown, everyone else gets told
            // when the next one lands.
            // The plus goes to the best thing available, which is not the same thing every
            // time. An ad if one is loaded and would help; otherwise the gate's countdown
            // when the bar is empty; otherwise the honest "you are full" toast. Sending
            // every tap to the same panel would mean showing a full player a way to get
            // hearts they cannot hold.
            ResourcePill(row, -318f, Pal.Rose, "ic_heart", $"{Profile.Hearts}/{Profile.MaxHearts}", false,
                         () =>
                         {
                             if (RewardedAds.ShouldOffer(AdPlacement.HeartRefill))
                                 Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.HeartRefill);
                             else if (!Profile.CanPlay) Flow.Modal<OutOfHeartsOverlay>();
                             else Scenery.Toast(Content, HeartsLine(), Pal.Rose, 2.4f);
                         });
            ResourcePill(row, 0f, Pal.Gold, null, Profile.Short(Profile.Coins), true,
                         () =>
                         {
                             if (RewardedAds.ShouldOffer(AdPlacement.CoinBonus))
                                 Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.CoinBonus);
                             else
                                 Flow.Modal<ComingSoonOverlay>(v => v.Configure("Coins", "ic_chest",
                                     "Earn coins in the glades and spend them in the shop. Coming soon."));
                         });
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

        // -------------------------------------------------------- daily bonuses
        /// <summary>
        /// The daily chests: how much has been played today, and the chests that has
        /// earned.
        ///
        /// <para>
        /// This panel used to show lifetime stars, which was a fine thing to show and the
        /// wrong thing to put chests on — a bar that moves three times in a player's whole
        /// history cannot carry a daily loop, and the chests on it opened themselves. The
        /// grove total still lives on the profile screen, where a record belongs.
        /// </para>
        /// <para>
        /// Rebuilt rather than repainted when the day rolls over or a chest is opened.
        /// It is a dozen images built once a navigation, and a repaint path would be a
        /// second description of the same panel that has to be kept in step with this one.
        /// </para>
        /// </summary>
        void BuildDaily()
        {
            int sibling = _dailyPanel ? _dailyPanel.GetSiblingIndex() : -1;
            if (_dailyPanel) Destroy(_dailyPanel.gameObject);

            int runs = DailyChests.Runs, target = DailyChests.RunsForAll;

            var panel = UIKit.Img("Daily", Content, Art.Round(28), new Color(.04f, .09f, .12f, .72f),
                                  new Vector2(900f, 208f), new Vector2(.5f, 1f), new Vector2(0f, -438f));
            _dailyPanel = (RectTransform)panel.transform;
            if (sibling >= 0) _dailyPanel.SetSiblingIndex(sibling);

            var edge = UIKit.Img("Edge", panel.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .14f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            // The header is two labels sharing one 900-wide row, so both are placed from
            // the panel edges rather than by eye. A box anchored to the right edge has its
            // *centre* at the anchored position, so the offset has to be
            // -(margin + width/2) — anything less hangs the box off the end of the panel,
            // and nothing clips it, so it simply draws over the screen edge.
            const float Margin = 40f, TitleW = 370f, ClockW = 320f;

            UIKit.Shrinkable(
                UIKit.Titled("Title", panel.transform, Loc.Get("ui.home.bonuses"), 32, Pal.Gold,
                             TextAnchor.MiddleLeft, new Vector2(TitleW, 40f), new Vector2(0f, 1f),
                             new Vector2(148f + TitleW * .5f, -38f), 3f, 3f), 20);

            // The reset clock, ticking. An expiry a player cannot see is an expiry that
            // reads as the game having eaten their chest.
            //
            // Shrinkable because this is the one label here whose width is not under our
            // control: "resets in 10h 21m" is the short case, and a translation of it
            // plus a two-digit hour is the long one.
            _resetClock = UIKit.Shrinkable(
                UIKit.Titled("Reset", panel.transform, ResetLine(), 26,
                             new Color(1f, .95f, .84f, .62f), TextAnchor.MiddleRight,
                             new Vector2(ClockW, 36f), new Vector2(1f, 1f),
                             new Vector2(-(Margin + ClockW * .5f), -38f), 0f, 0f), 17);

            var track = UIKit.Img("Track", panel.transform, Art.Round(16), new Color(.02f, .05f, .07f, .9f),
                                  new Vector2(816f, 38f), new Vector2(.5f, .5f), new Vector2(0f, -16f));
            var fill = UIKit.Img("Fill", track.transform, Art.Round(14), Pal.Gold,
                                 new Vector2(0f, 28f), new Vector2(0f, .5f), new Vector2(5f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);

            float full = 806f * (target <= 0 ? 0f : Mathf.Clamp01(runs / (float)target));
            Tween.Run(.9f, Ease.OutCubic, t =>
            {
                if (!fillRT) return;
                fillRT.sizeDelta = new Vector2(full * t, 28f);
                fill.color = Color.Lerp(Pal.Sun, Pal.Gold, t);
            }, fill).Delay(.35f);

            int count = Mathf.Max(1, DailyChests.ChestCount);
            for (int i = 0; i < count; i++) BuildChest(track.transform, i, count);

            // Inset from the panel rather than filling it, and allowed to shrink: this is
            // a whole sentence, and it is the line most likely to grow in translation.
            UIKit.Shrinkable(
                UIKit.Titled("Hint", panel.transform, HintLine(), 24, new Color(1f, .95f, .84f, .58f),
                             TextAnchor.MiddleCenter, new Vector2(820f, 34f), new Vector2(.5f, 0f),
                             new Vector2(0f, 24f), 0f, 0f), 16);

            var icon = UIKit.Img("Gift", panel.transform, Art.S("Ui/ic_gift"), Color.white,
                                 new Vector2(72f, 72f), new Vector2(0f, 1f), new Vector2(62f, -42f));
            icon.preserveAspect = true;
            Tween.Breathe(icon.transform, .07f, 2.1f);

            if (sibling < 0)
            {
                panel.transform.localScale = Vector3.zero;
                Tween.Pop(panel.transform, 0f, .6f, .3f);
            }
        }

        /// <summary>
        /// One chest on the bar.
        ///
        /// A ready chest is a button and shines; the other two are images and do not.
        /// That distinction is the entire interaction, so it is drawn as loudly as the
        /// panel can afford — a chest that is tappable and looks like scenery is a reward
        /// most players never collect.
        /// </summary>
        void BuildChest(Transform track, int index, int count)
        {
            var state = DailyChests.StateOf(index);
            float px = -408f + 816f * ((index + 1) / (float)count);

            if (state != ChestState.Ready)
            {
                bool opened = state == ChestState.Opened;
                var img = UIKit.Img("C" + index, track,
                                    Art.S(opened ? "Ui/ic_chest_open" : "Ui/ic_chest"),
                                    opened ? new Color(1f, 1f, 1f, .72f) : new Color(.5f, .54f, .58f, .92f),
                                    new Vector2(78f, 78f), new Vector2(.5f, .5f), new Vector2(px, 8f));
                img.preserveAspect = true;

                if (!opened)
                {
                    // How many more runs this one needs, so the bar reads as a plan
                    // rather than as three identical grey boxes.
                    int needed = DailyChests.RunsFor(index) - DailyChests.Runs;
                    if (needed > 0)
                        UIKit.Titled("N" + index, img.transform, needed.ToString(), 24, Pal.Cream,
                                     TextAnchor.MiddleCenter, new Vector2(40f, 30f),
                                     new Vector2(.5f, 0f), new Vector2(0f, -12f), 3f, 0f);
                }
                return;
            }

            int chestIndex = index;
            var btn = UIKit.Button("C" + index, track, Art.S("Ui/ic_chest"),
                                   new Vector2(96f, 96f), new Vector2(.5f, .5f), new Vector2(px, 8f),
                                   () => OpenChest(chestIndex));

            var face = btn.GetComponent<Image>();
            face.preserveAspect = true;

            UIKit.Halo(btn.transform, Pal.Gold, 168f, .55f);
            Shine(btn.transform, 150f, index * .6f);
            Tween.Breathe(btn.transform, .075f, 1.5f, index * .4f);
            btn.Rehome();
        }

        /// <summary>
        /// The rotating star of light behind a chest that is ready to open.
        ///
        /// Four soft capsules turning slowly. Cheaper than a particle system, works in a
        /// uGUI hierarchy without a second canvas, and reads at a glance on a phone in
        /// daylight — which is the only test that matters for a call to action.
        /// </summary>
        static void Shine(Transform parent, float size, float phase)
        {
            var host = UIKit.Box("Shine", parent, Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            host.SetAsFirstSibling();

            for (int i = 0; i < 4; i++)
            {
                var ray = UIKit.Img("r" + i, host, Art.SoftCapsule(28, 160), Pal.A(Pal.Sun, .34f),
                                    new Vector2(20f, size * 1.28f), new Vector2(.5f, .5f), Vector2.zero);
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 45f);
            }

            Tween.Run(7f, Ease.Linear,
                      t => { if (host) host.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      host.gameObject, "spin").Loop(-1, false).Delay(phase);

            Tween.Run(1.7f, Ease.InOutSine,
                      t => { if (host) host.localScale = Vector3.one * Mathf.Lerp(.86f, 1.06f, t); },
                      host.gameObject, "pulse").Loop(-1, true).Delay(phase);
        }

        void OpenChest(int index)
        {
            if (Flow.HasModal) return;

            // Only reachable on a first launch that has never had a connection: a chest
            // rolled before the account exists is one the server would recompute
            // differently, so it waits rather than showing a reward it cannot honour.
            // One connection lifts this permanently — see DailyChests.CanOpen.
            if (!DailyChests.CanOpen)
            {
                Audio.Sfx("nope", .5f);
                Scenery.Toast(Content, Loc.Get("ui.daily.needs_connection"), Pal.Rose, 3f);
                return;
            }

            Flow.Modal<ChestOverlay>(v => v.ChestIndex = index);
        }

        string ResetLine() => Loc.Format("ui.daily.resets_in",
                                         Profile.Countdown(DailyChests.SecondsUntilReset));

        string HintLine()
        {
            // Said on the panel rather than only in a toast, so a player who has never
            // been online understands the chest is waiting for them and not broken.
            if (DailyChests.HasReadyChest && !DailyChests.CanOpen)
                return Loc.Get("ui.daily.needs_connection");

            if (DailyChests.HasReadyChest) return Loc.Get("ui.daily.ready");
            if (DailyChests.DayComplete) return Loc.Get("ui.daily.all_open");

            int needed = DailyChests.RunsToNextChest;
            return needed > 0
                ? Loc.Format("ui.daily.next_chest", needed)
                : Loc.Format("ui.daily.played", DailyChests.Runs, DailyChests.RunsForAll);
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
