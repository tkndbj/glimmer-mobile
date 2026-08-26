using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
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

        // ------------------------------------------------------- the feature row
        /// <summary>
        /// The two boxes under the daily panel: the streak, and whatever the player is
        /// working toward.
        ///
        /// <para>
        /// They are equals on purpose. Both were smaller than the thing they carried — the
        /// streak was a chip squeezed into the top bar beside the settings gear, and the
        /// event was a 56-high strip that had to fit a name, a clock, a bar and a count.
        /// Between them they are the two reasons a player opens the game on a day they had
        /// not planned to, which is a strange thing to draw at the size of chrome.
        /// </para>
        /// <para>
        /// The right box holds the event when one is running and the next companion when
        /// one is not. That is not a fallback bolted on: the strip already chose between
        /// exactly those two and simply hid the loser, so the only change is that the loser
        /// now gets the slot when the winner is absent. It also means the row never draws
        /// one box and a hole.
        /// </para>
        /// </summary>
        const float RowTop = 556f;
        const float RowHeight = 300f;
        const float RowWidth = 900f;    // the daily panel's width, so the column lines up
        const float RowGap = 24f;

        Image _hero;
        RectTransform _dailyPanel;
        RectTransform _resourceRow;
        RectTransform _streakBox;
        RectTransform _focusBox;
        Text _resetClock;
        float _clockTick;

        // The shortest gap between two pokes that both get a spray. Above the rate a poke is
        // deliberately given at, below the rate a held finger produces. See Poke.
        const float SparkGap = .18f;
        float _pokedAt = float.NegativeInfinity;

        protected override void Build()
        {
            BuildBackdrop();
            BuildTopBar();
            BuildResources();
            BuildDaily();
            BuildFeature();
            BuildHero();
            BuildPlay();
            NavBar.Build(Content, NavBar.Tab.Home);

            // Midnight arrives while a screen is open exactly as often as it arrives while
            // it is not, and opening a chest changes both panels from under themselves.
            DailyChests.Changed += OnDailyChanged;
            DailyStreak.Changed += OnStreakChanged;
            PlayerProgression.Changed += OnWalletChanged;
            Wallet.HeartsChanged += OnHeartsChanged;
        }

        void OnDestroy()
        {
            DailyChests.Changed -= OnDailyChanged;
            DailyStreak.Changed -= OnStreakChanged;
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

        void OnStreakChanged()
        {
            if (this == null || !_streakBox) return;
            BuildStreakBox();
        }

        void OnHeartsChanged(Hearts hearts) => OnWalletChanged();

        /// <summary>
        /// Three numbers changed, so three numbers are written. It used to rebuild the row.
        ///
        /// <para>
        /// Nothing about the row is a function of the wallet except the readouts — same
        /// three pills, same icons, same buttons — so a rebuild was a way of setting a
        /// string that also replayed the pills' entrance, and the entrance starts at scale
        /// zero behind a delay. One of those is a flourish; several in a second is the row
        /// flashing in and out. Returning to the game fires the events several times over
        /// (a sync applies another device's work, and the first read of the hearts catches
        /// up whatever refilled while the app was away), which is where a player sees it.
        /// </para>
        /// <para>
        /// It also removes a re-entrancy that was quietly worse than the flashing. Reading
        /// <c>Wallet.Hearts</c> commits a refill and raises <c>HeartsChanged</c> from inside
        /// the getter, so <c>BuildResources</c> — which reads it while placing the first
        /// pill — could be re-entered halfway through itself. The inner call built the row
        /// the player ends up with; the outer one carried on and registered its own pills
        /// with <see cref="ResourceSlots"/>, which are on the row destroyed at the end of
        /// the frame. The chest's prizes then had nowhere to fly to.
        /// </para>
        /// <para>
        /// The row is still rebuilt on navigation, which is the one place its structure can
        /// actually differ, and <see cref="ResourceSlots"/> registration stays in the
        /// builder — it is still the one thing that runs exactly once per row.
        /// </para>
        /// </summary>
        void OnWalletChanged()
        {
            if (this == null || !_resourceRow) return;
            PaintResources();
        }

        /// <summary>
        /// Takes something off the screen and then destroys it, in that order.
        ///
        /// <para>
        /// <c>Destroy</c> only lands at the end of the frame, so a panel replaced in place is
        /// drawn <em>over</em> its own replacement for the rest of the frame it was replaced
        /// in — and since everything here enters from <c>Tween.Pop</c> at scale zero, what
        /// the player sees is the old panel, then a gap, then the new one springing in. One
        /// of those reads as a flourish; two in a second reads as a fault.
        /// </para>
        /// <para>
        /// The other five screens that rebuild a region already do this (<c>ProfileScreen</c>,
        /// <c>EventScreen</c>, <c>StreakScreen</c>, <c>CompanionScreen</c>,
        /// <c>CompanionUnlockOverlay</c>); this was the screen that did not, which is the
        /// screen the doubling was reported from.
        /// </para>
        /// </summary>
        static void Hide(GameObject go)
        {
            if (!go) return;
            go.SetActive(false);
            Destroy(go);
        }

        /// <summary>
        /// Writes today's figures onto the three pills.
        ///
        /// <para>
        /// Through <see cref="ResourceSlots.Repaint"/> rather than onto the labels directly,
        /// which makes the registry the one writer of those three readouts. That is what lets
        /// a reward cascade own a pill while it walks it forward: a chest or a rewarded ad
        /// rewinds the number to what it said before the grant, and a wallet change landing
        /// mid-flight — an ad's credits arriving from the server is exactly one — would
        /// otherwise jump it to the true figure and have the next token drag it back down.
        /// See <see cref="ResourceSlots.Claim"/>.
        /// </para>
        /// </summary>
        void PaintResources()
        {
            ResourceSlots.Repaint(ResourceSlots.Kind.Hearts, Profile.Hearts);
            ResourceSlots.Repaint(ResourceSlots.Kind.Credits, Profile.Coins);
            ResourceSlots.Repaint(ResourceSlots.Kind.Gems, Profile.Gems);
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
            var bar = UIKit.Box("TopBar", Safe, new Vector2(0f, 168f), new Vector2(.5f, 1f), new Vector2(0f, -116f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, 168f);

            // 620 wide rather than 500: the streak chip used to sit at the far end of this
            // bar and the card was cut back to make room for it. With the chip gone the
            // space belongs to the name and the rank bar, which were both short of it —
            // a name is the one string here whose length the game does not choose.
            var card = UIKit.Img("Card", bar, Art.Round(26), new Color(.05f, .11f, .14f, .74f),
                                 new Vector2(620f, 132f), new Vector2(0f, .5f), new Vector2(352f, 0f));
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

            UIKit.Shrinkable(
                UIKit.Titled("Name", card.transform, Profile.Name, 36, Pal.Cream, TextAnchor.LowerLeft,
                             new Vector2(460f, 46f), new Vector2(0f, .5f), new Vector2(362f, 22f),
                             3f, 3f), 24);

            // rank experience bar, filled by stars toward the next rank
            var track = UIKit.Img("XpTrack", card.transform, Art.Round(12), new Color(.02f, .05f, .07f, .85f),
                                  new Vector2(440f, 26f), new Vector2(0f, .5f), new Vector2(352f, -22f));
            var xp = UIKit.Img("XpFill", track.transform, Art.Round(10), Pal.Mint,
                               new Vector2(0f, 18f), new Vector2(0f, .5f), new Vector2(4f, 0f));
            var xpRT = (RectTransform)xp.transform;
            xpRT.pivot = new Vector2(0f, .5f);
            xpRT.sizeDelta = new Vector2(0f, 18f);
            float w = 432f * Profile.RankProgress;
            Tween.Run(.7f, Ease.OutCubic, t => { if (xpRT) xpRT.sizeDelta = new Vector2(w * t, 18f); }, xp).Delay(.35f);

            // corner buttons
            UIKit.IconButton("Settings", bar, Skins.Aside, "ic_gear", new Vector2(104f, 104f),
                             new Vector2(1f, .5f), new Vector2(-92f, 0f), () => Flow.Modal<SettingsOverlay>());
            UIKit.IconButton("Info", bar, Skins.Aside, "ic_info", new Vector2(104f, 104f),
                             new Vector2(1f, .5f), new Vector2(-206f, 0f), () => Flow.Modal<HowToOverlay>());
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
            if (_resourceRow) Hide(_resourceRow.gameObject);

            var row = UIKit.Box("Resources", Safe, new Vector2(1000f, 92f), new Vector2(.5f, 1f), new Vector2(0f, -250f));
            _resourceRow = row;
            if (sibling >= 0) row.SetSiblingIndex(sibling);

            // The plus always opens the same panel, and that is the change worth explaining.
            // It used to choose between three destinations by asking whether an ad happened
            // to be loaded — the offer panel, the out-of-hearts gate, or a toast — which
            // meant the one control on this screen that looks like a question mark answered
            // a different question depending on the state of an ad network. A player who
            // tapped it twice got two different screens and learned nothing from either.
            //
            // It now opens the resource's own panel, which explains how the resource works
            // whatever the network is doing and offers the video when there is one. The
            // states that used to hide it are states the panel renders honestly: at the
            // ceiling it says so, with nothing loaded it says it is looking.
            ResourcePill(row, -318f, Pal.Rose, "ic_heart", Profile.HeartsLabel(), false,
                         () => Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.HeartRefill),
                         ResourceSlots.Kind.Hearts, n => Profile.HeartsLabel((int)n));
            ResourcePill(row, 0f, Pal.Gold, null, Compact.Number(Profile.Coins), true,
                         () => Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.CoinBonus),
                         ResourceSlots.Kind.Credits, Compact.Number);
            ResourcePill(row, 318f, Pal.Bloom, "ic_gem", Compact.Number(Profile.Gems), false,
                         () => Flow.Modal<ComingSoonOverlay>(v => v.Configure("Gems", "ic_gem",
                             "Gems will unlock hints, skins and seasonal glades.")),
                         ResourceSlots.Kind.Gems, Compact.Number);
        }

        /// <summary>
        /// Resource readout with an add button. Coins use the spinning sprite.
        /// </summary>
        /// <remarks>
        /// Each pill registers itself with <see cref="ResourceSlots"/> as it is built, which
        /// is what lets the daily chest fly its prizes into this row from an overlay drawn on
        /// top of it. Registration happens here rather than at the call site precisely because
        /// this row is destroyed and rebuilt whenever the wallet moves — anything holding a
        /// reference from the last build would be pointing at a dead object, and the one place
        /// guaranteed to run on every rebuild is the builder itself.
        /// </remarks>
        void ResourcePill(Transform parent, float x, Color tint, string icon, string value,
                          bool animatedCoin, Action onAdd,
                          ResourceSlots.Kind kind, Func<long, string> format)
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

            ResourceSlots.Register(kind, (RectTransform)ic.transform, t, glow, tint, format);

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
            if (_dailyPanel) Hide(_dailyPanel.gameObject);

            int runs = DailyChests.Runs, target = DailyChests.RunsForAll;

            // Sits straight under the resources row. It used to leave a gap for the 56-high
            // focus strip, which has become the second box on the row below; the panel takes
            // that space rather than leaving it, because everything under it is now taller.
            // Moved rather than shrunk: the panel's internals are laid out from its own
            // edges, so its height is load-bearing and its position is not.
            var panel = UIKit.Img("Daily", Safe, Art.Round(28), new Color(.04f, .09f, .12f, .72f),
                                  new Vector2(900f, 208f), new Vector2(.5f, 1f), new Vector2(0f, -424f));
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

        // ------------------------------------------------------- the feature row
        /// <summary>
        /// Builds both boxes and decides how wide each of them is.
        ///
        /// <para>
        /// The streak is always drawn, including for a player who has never had one — that
        /// is how they learn the thing exists before they have one to lose. So the only
        /// question here is whether there is a second box: when no event is running and the
        /// roster holds nothing further, the streak takes the whole row rather than sitting
        /// beside a hole.
        /// </para>
        /// <para>
        /// The boxes are built from their own left edge rather than from the centre, which
        /// is what lets the same code lay out a half-row and a full one.
        /// </para>
        /// </summary>
        void BuildFeature()
        {
            float half = (RowWidth - RowGap) * .5f;
            float y = -(RowTop + RowHeight * .5f);
            float x = (RowWidth - half) * .5f;

            _focusBox = UIKit.Box("Focus", Safe, new Vector2(half, RowHeight),
                                  new Vector2(.5f, 1f), new Vector2(x, y));

            if (!BuildFocusBox(half))
            {
                Destroy(_focusBox.gameObject);
                _focusBox = null;
            }

            bool paired = _focusBox != null;

            _streakBox = UIKit.Box("Streak", Safe,
                                   new Vector2(paired ? half : RowWidth, RowHeight),
                                   new Vector2(.5f, 1f), new Vector2(paired ? -x : 0f, y));
            BuildStreakBox();

            _streakBox.localScale = Vector3.zero;
            Tween.Pop(_streakBox, 0f, .55f, .28f);

            if (!paired) return;
            _focusBox.localScale = Vector3.zero;
            Tween.Pop(_focusBox, 0f, .55f, .34f);
        }

        /// <summary>
        /// The shell every box on this row shares: one radius, one fill, and a rim in the
        /// box's own colour.
        ///
        /// <para>
        /// The card is the button rather than carrying an invisible one, so a press squashes
        /// the whole box — glyph, number and strip together — instead of an overlay nobody
        /// can see. There is no plate behind the row and no rule between the two: each box
        /// earns its own contrast from its fill and rim, which it has to, because
        /// <c>grove_near</c> runs from near-white inside the light shaft to dark teal beside
        /// it and a flat shape would vanish over half of it.
        /// </para>
        /// </summary>
        RectTransform FeatureCard(Transform host, float w, Color tint, float edge, Action onTap)
        {
            var btn = UIKit.Button("Card", host, Art.Round(28), new Vector2(w, RowHeight),
                                   new Vector2(.5f, .5f), Vector2.zero, onTap);
            btn.GetComponent<Image>().color = new Color(.04f, .09f, .12f, .76f);
            btn.PressScale = .985f;

            var rim = UIKit.Img("Edge", btn.transform, Art.RoundOutline(28, 3f), Pal.A(tint, edge));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            return (RectTransform)btn.transform;
        }

        /// <summary>
        /// The box's name, and the one fact that has to sit beside it rather than under it —
        /// a countdown, or that a companion is still locked.
        ///
        /// Both are placed from the card's own edges rather than by eye, and both shrink:
        /// the title is a translated name and the meta is a translated sentence with a clock
        /// in it, so neither width is under our control.
        /// </summary>
        void FeatureHeader(Transform card, float w, string title, Color tint, string meta,
                           bool badged = false)
        {
            // The meta is 200 wide inset 26 from the right, so the title gets everything left
            // of it less a gap. Worked out from the edges rather than by eye: both strings
            // are translated, both are right up against their box, and nothing clips them.
            //
            // A box with no meta still does not get the full width — the top-right corner is
            // where a count badge hangs, and a title long enough to reach it would run under
            // one rather than being clipped by it.
            //
            // `badged` moves the meta out of that corner as well. The streak box never needed
            // it because it carries no meta, so the collision only appeared when the event box
            // gained a badge and its countdown ran straight under the disc. The corner is 66
            // wide and hangs 30 out, so 70 of clearance puts the text clear of it whatever the
            // translation is.
            bool hasMeta = !string.IsNullOrEmpty(meta);
            float inset = badged ? 70f : 0f;
            float tw = hasMeta ? w - 262f - inset : w - 110f;

            UIKit.Shrinkable(
                UIKit.Titled("Title", card, title.ToUpperInvariant(), 25, tint, TextAnchor.MiddleLeft,
                             new Vector2(tw, 34f), new Vector2(0f, 1f),
                             new Vector2(30f + tw * .5f, -36f), 0f, 2f), 16);

            if (!hasMeta) return;

            UIKit.Shrinkable(
                UIKit.Titled("Meta", card, meta, 23, new Color(1f, .95f, .84f, .66f),
                             TextAnchor.MiddleRight, new Vector2(200f, 32f), new Vector2(1f, 1f),
                             new Vector2(-126f - inset, -36f), 0f, 0f), 15);
        }

        /// <summary>
        /// The number the box is about, and what it counts.
        ///
        /// Both are shrinkable and neither is a two-digit field: a streak does not stop at
        /// the end of the ladder, and the caption under it is the shortest string on the
        /// screen in English and one of the longest in German.
        /// </summary>
        void FeatureValue(Transform card, float w, string value, Color colour,
                          string caption, Color tint)
        {
            float vx = -w * .5f + 299f;

            UIKit.Shrinkable(
                UIKit.Titled("V", card, value, 62, colour, TextAnchor.MiddleCenter,
                             new Vector2(200f, 74f), new Vector2(.5f, .5f), new Vector2(vx, 20f),
                             3f, 4f), 30);

            UIKit.Shrinkable(
                UIKit.Titled("Cap", card, caption, 22, Pal.A(tint, .95f), TextAnchor.MiddleCenter,
                             new Vector2(210f, 30f), new Vector2(.5f, .5f), new Vector2(vx, -32f),
                             0f, 0f), 15);
        }

        /// <summary>The inset that runs along the bottom of a box, holding its one live detail.</summary>
        RectTransform FeatureStrip(Transform card, float w)
        {
            var strip = UIKit.Img("Strip", card, Art.Round(18), new Color(.01f, .04f, .05f, .62f),
                                  new Vector2(w - 72f, 54f), new Vector2(.5f, .5f),
                                  new Vector2(0f, -(RowHeight * .5f) + 46f));
            return (RectTransform)strip.transform;
        }

        /// <summary>
        /// Lights a box that is holding something the player can take right now.
        ///
        /// <para>
        /// The corner badge says <em>that</em> there is a reward and how many; this says
        /// <em>which box</em> from across the screen. They are not redundant — a 54px disc is
        /// something you find once you are already looking at the row, and the whole problem
        /// with a collect-by-hand reward is getting somebody to look at the row at all. A
        /// player who opens the hub for the daily chests has no reason to read the box beside
        /// them.
        /// </para>
        /// <para>
        /// Three layers, because each does a job the others cannot. A soft gold light sits
        /// <em>behind</em> the box and breathes — the same trick the nav caps use to hold
        /// their own contrast, run in reverse: a seat of light instead of a seat of shadow,
        /// and the only part of this visible from the far corner of the screen. Over the
        /// resting rim, a gold edge brightens with it, which is what says the border is lit
        /// rather than merely coloured. And a pale ring steps out of the border and fades,
        /// which is the "tap me": motion that <em>leaves</em> the shape is what the eye
        /// catches peripherally, and a rim that only brightens does not.
        /// </para>
        /// <para>
        /// The ring is cream rather than gold, and that is not a taste call — the first
        /// version was gold, travelling out of a gold rim into a gold glow, and it was
        /// invisible on the screen while looking perfectly reasonable in the code.
        /// </para>
        /// <para>
        /// It grows by a fixed number of pixels rather than by a scale factor, because a
        /// percentage would drift the moment a box changes width — which it does: the streak
        /// takes the whole row when there is no second box. Eighteen is further than the 24px
        /// gap between the boxes would allow a solid shape, and it can be, because the ring is
        /// down to a tenth of its alpha before it gets there.
        /// </para>
        /// </summary>
        static void FeatureBeacon(RectTransform card)
        {
            const float Reach = 18f;

            // Behind the card, so it lights the box rather than washing its contents:
            // a child would draw over the card's own fill, which is what everything
            // inside one of these boxes relies on not happening.
            var seat = UIKit.Img("Beacon", card.parent, Art.Glow(128, 1.7f), Pal.A(Pal.Gold, 0f),
                                 card.sizeDelta + new Vector2(96f, 96f),
                                 new Vector2(.5f, .5f), Vector2.zero);
            seat.transform.SetAsFirstSibling();

            var lit = UIKit.Img("Lit", card, Art.RoundOutline(28, 4f), Pal.A(Pal.Gold, 0f));
            UIKit.StretchTo((RectTransform)lit.transform, 0, 0, 0, 0);

            Tween.Run(1.5f, Ease.InOutSine, t =>
            {
                if (lit) lit.color = Pal.A(Pal.Gold, Mathf.Lerp(.22f, .95f, t));
                if (seat) seat.color = Pal.A(Pal.Gold, Mathf.Lerp(.10f, .34f, t));
            }, lit, "lit").Loop(-1, true);

            var ring = UIKit.Img("Ring", card, Art.RoundOutline(28, 5f), Pal.A(Pal.Cream, 0f));
            var ringRT = (RectTransform)ring.transform;

            // Not ping-ponged: a ring that steps out and then walks back in reads as
            // something breathing rather than as something leaving. It also rests for the
            // back half of the cycle — a pulse that never stops is a pulse nobody sees.
            Tween.Run(2.4f, Ease.Linear, t =>
            {
                if (!ringRT) return;

                float k = Mathf.Clamp01(t / .55f);
                float out01 = Ease.OutCubic(k);
                UIKit.StretchTo(ringRT, -Reach * out01, -Reach * out01, -Reach * out01, -Reach * out01);

                // Held bright for the first third of the step, then gone. Fading from the
                // first frame is what made the previous ring read as a flicker.
                ring.color = Pal.A(Pal.Cream, .75f * Mathf.Clamp01((1f - k) * 1.6f) * (k < 1f ? 1f : 0f));
            }, ring, "ring").Loop(-1, false);
        }

        /// <summary>A bar inside a strip. Returns the track, so a caller can pin things to it.</summary>
        RectTransform FeatureBar(Transform strip, float w, float fill01, Color tint)
        {
            float track = w - 132f;

            var bar = UIKit.Img("Track", strip, Art.Round(9), new Color(.01f, .02f, .04f, .85f),
                                new Vector2(track, 18f), new Vector2(.5f, .5f), Vector2.zero);
            var fill = UIKit.Img("Fill", bar.transform, Art.Round(6), tint,
                                 new Vector2(0f, 12f), new Vector2(0f, .5f), new Vector2(3f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            fillRT.sizeDelta = new Vector2(0f, 12f);

            float full = (track - 6f) * Mathf.Clamp01(fill01);
            Tween.Run(.85f, Ease.OutCubic,
                      t => { if (fillRT) fillRT.sizeDelta = new Vector2(full * t, 12f); },
                      fill).Delay(.5f);

            return (RectTransform)bar.transform;
        }

        // -------------------------------------------------------------- streak
        /// <summary>
        /// The flame, the number of days behind it, and what tomorrow is worth.
        ///
        /// <para>
        /// This is the one thing on the screen the player already owns, and the only one
        /// they can lose by doing nothing — which is exactly why it is the strongest reason
        /// on the screen to come back tomorrow, and why it is no longer a chip beside the
        /// settings gear.
        /// </para>
        /// <para>
        /// Three states, drawn differently on purpose. Held and fed today: a lit flame,
        /// flickering. Held but not yet fed: the same flame, dimmed and pulsing, with the
        /// hours left in place of the caption — the only moment in the game where the screen
        /// says "this is going to be taken away", and it earns that by being true. Not held
        /// at all: a grey flame and an invitation, so a new player learns the thing exists
        /// before they have one to lose.
        /// </para>
        /// <para>
        /// Rebuilt rather than repainted, like the panels around it: the three states differ
        /// in what exists, not just in what colour it is, and a repaint path would be a
        /// second description of the same box to keep in step with this one.
        /// </para>
        /// </summary>
        void BuildStreakBox()
        {
            if (!_streakBox) return;
            for (int i = _streakBox.childCount - 1; i >= 0; i--)
                Hide(_streakBox.GetChild(i).gameObject);

            float w = _streakBox.sizeDelta.x;
            int days = DailyStreak.Days;
            int pending = DailyStreak.Pending;
            bool atRisk = DailyStreak.AtRisk;
            bool lit = days > 0 && !atRisk;

            var tint = lit ? Pal.Sun : atRisk ? Pal.Ember : new Color(.52f, .56f, .60f);
            float gx = -w * .5f + 115f;

            var card = FeatureCard(_streakBox, w, tint, lit ? .52f : .30f, OpenStreak);

            // Straight after the card, so the lit rim and the ring sit under everything the
            // box draws. They live at the border, where nothing else does.
            if (pending > 0) FeatureBeacon(card);

            FeatureHeader(card, w, Loc.Get("ui.home.streak"), tint, null);

            if (lit) UIKit.Img("Glow", card, Art.Glow(128, 2f), Pal.A(Pal.Sun, .32f),
                               new Vector2(200f, 200f), new Vector2(.5f, .5f), new Vector2(gx, 4f));

            var flame = UIKit.Img("Flame", card, null,
                                  days > 0 ? Color.white : new Color(.62f, .66f, .70f, .55f),
                                  new Vector2(138f, 138f), new Vector2(.5f, .5f), new Vector2(gx, 4f));
            flame.preserveAspect = true;

            // Animated only while the streak is actually alive. A flame that flickers on a
            // player who has no streak is decoration claiming to be a state.
            if (days > 0)
            {
                Flipbook.Attach(flame, "Ui/Flame", 9f);
            }
            else
            {
                var frames = Art.Frames("Ui/Flame");
                flame.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            }

            if (atRisk) Tween.Run(1.1f, Ease.InOutSine,
                                  t => { if (flame) flame.color = Color.Lerp(new Color(1f, 1f, 1f, .45f), Color.white, t); },
                                  flame, "risk").Loop(-1, true);

            FeatureValue(card, w, days > 0 ? days.ToString() : "—",
                         days > 0 ? Pal.Cream : new Color(1f, .96f, .86f, .5f),
                         StreakCaption(days, atRisk), tint);

            StreakStrip(FeatureStrip(card, w), w, pending);
            StreakBadge(card, pending);
        }

        /// <summary>
        /// What keeping the flame another night is actually worth.
        ///
        /// <para>
        /// It is most of the reason the box earns its size. The count above says how long
        /// the player has kept the streak, which is a record; this says what tomorrow pays,
        /// which is an argument. The old chip had room for one of the two and kept the
        /// record.
        /// </para>
        /// <para>
        /// A reward already waiting outranks tomorrow's, because it is something to do now
        /// rather than a reason to return. Neither is a number when the next rung pays
        /// nothing, which the shipped ladder never does but a retuned one could.
        /// </para>
        /// </summary>
        void StreakStrip(Transform strip, float w, int pending)
        {
            float sw = w - 72f;
            float lw = sw - 90f;

            var drop = DailyStreak.NextReward;
            bool plain = pending > 0 || drop.Kind == ChestDropKind.None;

            var icon = UIKit.Img("M", strip,
                                 plain ? Art.S("Ui/ic_gift") : RewardArt.Icon(drop.Kind),
                                 Color.white, new Vector2(40f, 40f), new Vector2(0f, .5f),
                                 new Vector2(38f, 0f));
            icon.preserveAspect = true;
            if (!plain) RewardArt.Glyph(icon, drop.Kind, 10f);

            string line = pending > 0 ? Loc.Get("ui.home.streak_waiting")
                        : plain ? Loc.Get("ui.home.streak_keep")
                        : Loc.Format("ui.home.streak_next", RewardArt.Amount(drop));

            UIKit.Shrinkable(
                UIKit.Titled("L", strip, line, 24, new Color(1f, .95f, .84f, .82f),
                             TextAnchor.MiddleLeft, new Vector2(lw, 34f), new Vector2(0f, .5f),
                             new Vector2(70f + lw * .5f, 0f), 0f, 0f), 15);
        }

        /// <summary>
        /// The count of streak nights with a reward still on them, on the corner of the box.
        ///
        /// <para>
        /// Load-bearing rather than decorative. A rung used to be applied silently when a
        /// run ended; it now waits on the streak page to be tapped, which is a better moment
        /// but only if the player knows it is there. The hub is the screen they return to,
        /// so this is the one place that can tell them — without it the change trades a
        /// reward they did not notice for a reward they never take.
        /// </para>
        /// <para>
        /// Built last so it sits over the card's own tap area, and drawn as a number rather
        /// than a dot because "3" is a reason to go and a dot is only a hint that there
        /// might be one.
        /// </para>
        /// <para>
        /// 66px, sized off the corner rather than off the number: it hangs 3px past the right
        /// edge and 5px past the top, which is what makes it read as pinned <em>to</em> the
        /// box instead of drawn inside it. <see cref="FeatureHeader"/> keeps its title clear
        /// of that corner, so the two cannot collide however long a translation runs.
        /// </para>
        /// </summary>
        void StreakBadge(Transform card, int pending)
        {
            if (pending <= 0) return;

            var badge = UIKit.Img("Waiting", card, Art.Disc(64), Pal.Gold,
                                  new Vector2(66f, 66f), new Vector2(1f, 1f), new Vector2(-30f, -28f));

            var rim = UIKit.Img("Rim", badge.transform, Art.Ring(64, 7f), new Color(.16f, .12f, .04f, .95f));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);

            // Shrinkable, because this is not a one-digit field: a player who is away for a
            // fortnight comes back to two figures, and the count is over *nights*, which the
            // ladder no longer caps.
            UIKit.Shrinkable(
                UIKit.Titled("N", badge.transform, pending.ToString(), 36, new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(50f, 50f),
                             new Vector2(.5f, .5f), Vector2.zero, 0f, 0f), 22);

            UIKit.Halo(badge.transform, Pal.Gold, 146f, .45f);

            badge.transform.localScale = Vector3.zero;
            Tween.Pop(badge.transform, 0f, .5f, .18f)
                 .OnDone(() => { if (badge) Tween.Breathe(badge.transform, .10f, 1.3f); });
        }

        static string StreakCaption(int days, bool atRisk)
        {
            if (days <= 0) return Loc.Get("ui.streak.none");
            if (atRisk) return Loc.Format("ui.streak.at_risk",
                                          Profile.Countdown(DailyStreak.SecondsUntilLost));
            return Loc.Get(days == 1 ? "ui.streak.day" : "ui.streak.days");
        }

        /// <summary>
        /// Opens the streak page.
        ///
        /// A page rather than the toast this used to raise. The box answers "how many days";
        /// the question a player actually has is "and then what", and a reward two nights
        /// away is the thing that makes them open the game tomorrow. A line of text that
        /// fades after three seconds cannot carry that.
        /// </summary>
        void OpenStreak()
        {
            if (Flow.HasModal) return;
            Flow.Go<StreakScreen>();
        }

        // ------------------------------------------------------------- the focus
        /// <summary>
        /// The right-hand box, and whichever of two things most deserves it.
        ///
        /// <para>
        /// A live event wins, and the rule is worth stating plainly: an event is the only
        /// thing on this screen with a deadline. The unlock goal will still be there next
        /// week; the event will not.
        /// </para>
        /// <para>
        /// This is the choice the old 56-high strip already made — it simply hid the loser.
        /// The difference now is that the loser takes the slot when the winner is absent,
        /// which is why there is no state of this screen with one box and a gap.
        /// </para>
        /// <para>
        /// Drawn once per navigation and not repainted. An event's countdown is measured in
        /// days and the keeper level cannot move while this screen is up, so a live path
        /// would be code that never runs.
        /// </para>
        /// </summary>
        bool BuildFocusBox(float w)
        {
            // Featured, not Live. Rewards are collected by hand now, so a window closing must
            // not take a bloom the player grew and never took — and the box is the only way
            // back to the page holding it. A closed event with nothing waiting stops being
            // featured, so the goal box gets the slot back the moment the track is settled.
            var featured = GroveEvents.Featured;
            if (featured == null) return BuildGoalBox(w);

            BuildEventBox(featured, w);
            return true;
        }

        /// <summary>
        /// The live event: its name, its clock, how much of the track is done, and the mark
        /// it wears.
        ///
        /// The mark is the interesting part. It is <see cref="EventMark"/> rather than a
        /// fixed glyph, so the event picks it from the manifest; and for the bloom it opens
        /// as the track fills, which means the picture and the bar under it say the same
        /// thing and a glance is enough.
        /// </summary>
        void BuildEventBox(GroveEvent live, float w)
        {
            var progress = GroveEvents.ProgressOf(live);
            int goal = Mathf.Max(1, live.FinalGoal);
            float done = Mathf.Clamp01(progress.Finished / (float)goal);
            float gx = -w * .5f + 115f;
            long left = live.SecondsLeftAt(GameClock.NowUnix());

            var card = FeatureCard(_focusBox, w, Pal.Bloom, .50f, OpenEvent);

            // Straight after the card, for the reason the streak's is: the lit rim and the
            // ring live at the border, under everything else the box draws.
            if (progress.AnyWaiting) FeatureBeacon(card);

            FeatureHeader(card, w, Loc.Get(live.NameKey), Pal.Bloom,
                          left > 0 ? Loc.Format("ui.event.ends_in", Profile.LongCountdown(left))
                                   : Loc.Get("ui.event.ended"),
                          progress.AnyWaiting);

            UIKit.Img("Glow", card, Art.Glow(128, 2f), Pal.A(Pal.Bloom, .28f),
                      new Vector2(200f, 200f), new Vector2(.5f, .5f), new Vector2(gx, 4f));

            var mark = UIKit.Box("Mark", card, new Vector2(148f, 148f),
                                 new Vector2(.5f, .5f), new Vector2(gx, 4f));
            EventMark.Paint(mark, live.Icon, Pal.Bloom, done);
            Tween.Breathe(mark, .055f, 2.6f);

            // The caption gives way to the instruction when there is something to take. The
            // fraction is still drawn right above it, so nothing is lost — and a box whose
            // border is lit and whose corner carries a count should say what to do about it.
            FeatureValue(card, w, Loc.Format("ui.home.fraction", progress.Finished, goal),
                         Pal.Cream,
                         progress.AnyWaiting ? Loc.Get("ui.home.event_waiting")
                                             : Loc.Get("ui.home.glades"),
                         progress.AnyWaiting ? Pal.Gold : Pal.Bloom);

            Milestones(FeatureBar(FeatureStrip(card, w), w, done, Pal.Bloom),
                       w, live, progress.Finished, goal);

            EventBadge(card, progress.Waiting);

            Sheen.Attach(card, 4.6f);
        }

        /// <summary>
        /// The count of blooms waiting, pinned to the corner.
        ///
        /// The same disc the streak wears, and deliberately the same: a gold badge on a
        /// feature box means one thing across this screen. Not redundant with the beacon
        /// either, for the reason stated there — the lit border is what is visible from
        /// across the room, the badge is what says how much once you are looking.
        /// </summary>
        void EventBadge(Transform card, int waiting)
        {
            if (waiting <= 0) return;

            var badge = UIKit.Img("Waiting", card, Art.Disc(64), Pal.Gold,
                                  new Vector2(66f, 66f), new Vector2(1f, 1f), new Vector2(-30f, -28f));

            var rim = UIKit.Img("Rim", badge.transform, Art.Ring(64, 7f), new Color(.16f, .12f, .04f, .95f));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);

            UIKit.Shrinkable(
                UIKit.Titled("N", badge.transform, waiting.ToString(), 36, new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(50f, 50f),
                             new Vector2(.5f, .5f), Vector2.zero, 0f, 0f), 22);

            UIKit.Halo(badge.transform, Pal.Gold, 146f, .45f);

            badge.transform.localScale = Vector3.zero;
            Tween.Pop(badge.transform, 0f, .5f, .22f)
                 .OnDone(() => { if (badge) Tween.Breathe(badge.transform, .10f, 1.3f); });
        }

        /// <summary>
        /// Opens the event page.
        ///
        /// A page rather than the panel this used to raise, and the reason is the same one
        /// that turned the streak's toast into a screen: the box reports, and there is now
        /// something on the other side of it to <em>do</em>.
        /// </summary>
        void OpenEvent()
        {
            if (Flow.HasModal) return;
            Flow.Go<EventScreen>();
        }

        /// <summary>
        /// The track's rungs, as pips on the bar.
        ///
        /// A bar says how far along; the pips say how far to the next thing worth having,
        /// which is the question that actually moves somebody. Placed from the event's own
        /// milestones rather than at even spacing, because the rungs are not evenly spaced —
        /// the shipped track pays at one, two and four of four.
        /// </summary>
        static void Milestones(RectTransform bar, float w, GroveEvent live, int finished, int goal)
        {
            float track = w - 132f;

            for (int i = 0; i < live.Milestones.Count; i++)
            {
                int rung = live.Milestones[i].Goal;
                float x = -track * .5f + 3f + (track - 6f) * Mathf.Clamp01(rung / (float)goal);

                var pip = UIKit.Img("P" + i, bar, Art.Disc(32),
                                    finished >= rung ? Pal.Gold : new Color(.47f, .51f, .55f, .95f),
                                    new Vector2(18f, 18f), new Vector2(.5f, .5f), new Vector2(x, 0f));

                var rim = UIKit.Img("Rim", pip.transform, Art.Ring(32, 7f),
                                    new Color(.01f, .02f, .04f, .9f));
                UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// The next companion the keeper level will unlock, and how far along the way the
        /// player already is. False when the roster holds nothing further, which is what
        /// hands the whole row to the streak.
        ///
        /// <para>
        /// The rank bar in the card at the top measures progress <em>through</em> the current
        /// rank, which has a specific weakness: it empties every time it fills, and it never
        /// says what any of it is for. This measures the span between the unlock just earned
        /// and the one coming, and names it. A player four ranks into a five-rank span sees a
        /// bar four fifths full because it is four fifths full — the endowed-progress effect
        /// used honestly, as a change of framing rather than a fabricated head start. See
        /// <see cref="UnlockGoal"/>.
        /// </para>
        /// </summary>
        bool BuildGoalBox(float w)
        {
            var goal = UnlockGoal.Next(Profile.Level);

            // Nothing left to unlock. Drawing an empty box would be worse than drawing none:
            // a goal bar with no goal reads as something the game forgot to fill in.
            if (!goal.IsValid) return false;

            float gx = -w * .5f + 115f;

            // The meta says the condition rather than the state — "at rank 8" rather than
            // "locked" — which is the same job the event's countdown does on the other box:
            // both name what the player is waiting on, and only one of the two is useful.
            var card = FeatureCard(_focusBox, w, Pal.Bloom, .38f, () => Flow.Go<ProfileScreen>());
            FeatureHeader(card, w, Loc.Get(goal.NameKey), Pal.Bloom,
                          Loc.Format("ui.home.at_rank", goal.AtLevel));

            var portrait = UIKit.Img("Face", card, null, Color.white,
                                     new Vector2(132f, 132f), new Vector2(.5f, .5f),
                                     new Vector2(gx, 4f));
            portrait.preserveAspect = true;
            CompanionArt.Paint(portrait, AvatarCatalog.Find(goal.AvatarId), animate: false);

            // Locked, and shown as such. The picture is the reason to care; dimming it is
            // what makes it a thing to earn rather than a thing already owned.
            //
            // Only once the art has actually arrived, though: CompanionArt hides the frame
            // by dropping alpha to zero when a portrait is missing, and an Image with no
            // sprite draws a white rectangle rather than a blank. Overwriting that alpha
            // unconditionally is how a locked companion becomes a grey slab on a slow load.
            if (portrait.sprite != null) portrait.color = new Color(.42f, .48f, .54f, .95f);

            FeatureValue(card, w, goal.RanksToGo.ToString(), Pal.Cream,
                         Loc.Get(goal.RanksToGo == 1 ? "ui.home.rank_left" : "ui.home.ranks_left"),
                         Pal.Bloom);

            FeatureBar(FeatureStrip(card, w), w, goal.Progress01, Pal.Bloom);
            return true;
        }

        // ---------------------------------------------------------------- hero
        /// <summary>
        /// The companion, on its rock, between the feature row and the play button.
        ///
        /// It sits lower and a tenth smaller than it used to. The two boxes above take the
        /// space the hero had, and the numbers here are what is left: the rock's foot clears
        /// the play button and the critter's ears clear the boxes, with about the same margin
        /// at each end. Everything inside the host is scaled together rather than re-tuned
        /// one image at a time, so the rock and the critter cannot drift apart.
        /// </summary>
        void BuildHero()
        {
            var host = UIKit.Box("Hero", Content, new Vector2(600f, 500f), new Vector2(.5f, .5f), new Vector2(0f, -130f));

            var rock = UIKit.Img("Rock", host, Art.S("Map/rock_grass"), Color.white,
                                 new Vector2(378f, 297f), new Vector2(.5f, .5f), new Vector2(0f, -137f));
            rock.preserveAspect = true;
            Tween.Bob((RectTransform)rock.transform, 9f, 3.4f);

            // The companion the player chose, not a fixed critter. This is where the
            // choice actually pays off: the profile is where you pick one, the hub is
            // where you live with it. The screen is rebuilt on every navigation, so
            // coming back from the profile already shows the new one.
            _hero = UIKit.Img("Critter", host, null, Color.white,
                              new Vector2(286f, 286f), new Vector2(.5f, .5f), new Vector2(0f, 27f));
            _hero.preserveAspect = true;
            CompanionArt.Paint(_hero, Profile.Avatar, animate: true);
            Tween.Bob((RectTransform)_hero.transform, 12f, 3.4f, .35f);
            UIKit.Halo(host, new Color(1f, .88f, .55f), 522f, .24f);

            // the hero answers a poke
            var hit = UIKit.Button("Poke", host, Art.Pixel, new Vector2(306f, 306f),
                                   new Vector2(.5f, .5f), new Vector2(0f, 27f), Poke);
            hit.GetComponent<Image>().color = new Color(1, 1, 1, 0);
            hit.ClickSfx = null;
            hit.PressScale = 1f;

            host.localScale = Vector3.zero;
            Tween.Pop(host, 0f, .75f, .42f);
        }

        /// <summary>
        /// The companion answers a poke - and answers a held-down finger once per squash
        /// rather than once per tap.
        ///
        /// <para>
        /// The deformation a spammed poke used to produce was <see cref="Tween.Punch"/>'s and
        /// is fixed there, where every re-punchable control in the game gets it. What is left
        /// here is the other half of a spammed one: eight sparks and a pop per tap is a dozen
        /// bursts a second, and <em>celebrate once</em> applies to a poke as much as to a win.
        /// The squash itself still restarts on every tap, so the critter stays exactly as
        /// answerable as it looks; only the fanfare has a floor under it, and the floor sits
        /// above any rate a poke is deliberately given at.
        /// </para>
        /// </summary>
        void Poke()
        {
            if (_hero == null) return;

            Tween.Punch(_hero.transform, .28f, .5f);

            if (Time.unscaledTime - _pokedAt < SparkGap) return;
            _pokedAt = Time.unscaledTime;

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
            var index = GameContent.Index;
            var next = LevelUnlock.NextToPlay(index);

            if (!next.IsValid) return Loc.Get("ui.home.all_awake");

            // Named from the id alone, so the home screen never reads a chapter file
            // just to draw one line of text.
            if (!PlayerProgress.IsCleared(next))
                return Loc.Format("ui.home.next_up", Loc.Get(LevelDefinition.DefaultNameKey(next)));

            // Everything the player may open is finished, and since the chapter boundary
            // became a star gate that is two different situations. One is a finished game and
            // the other is a player two stars short of the next chapter — telling the second
            // one that every level is awake would be the game congratulating them for being
            // stuck, on the screen whose whole job is to say what to do next. The gate names
            // itself instead, in the number that moves.
            var gate = LevelUnlock.GateAfter(index, index.ChapterOf(next));

            return gate.Exists && !gate.IsOpen
                ? Loc.Format("ui.home.next_chapter", gate.Held, gate.Required)
                : Loc.Get("ui.home.all_awake");
        }

        /// <summary>
        /// Says so when a heart went on a run the last launch never finished — a force-quit, a
        /// crash, a flat battery. See <see cref="RunGuard"/>.
        ///
        /// <para>
        /// Reported rather than left to be noticed. A resource that quietly decrements is a
        /// resource players feel cheated by later, which is the rule the defeat panel's heart
        /// row is built on, and it applies twice as hard here: this is the one charge in the
        /// game the player did not watch happen.
        /// </para>
        /// <para>
        /// Here rather than on the splash because a toast over a loading screen is a toast
        /// nobody reads, and it is <see cref="OnPresented"/> rather than <c>Build</c> so it
        /// lands after the iris has opened rather than under it. <c>NoteReported</c> makes it
        /// once per launch, not once per visit to the hub.
        /// </para>
        /// </summary>
        public override void OnPresented()
        {
            // the line under PLAY already says where to go, so no toast is needed for that

            if (!RunGuard.Unfinished.IsValid) return;

            string glade = Loc.Get(LevelDefinition.DefaultNameKey(RunGuard.Unfinished));
            bool charged = RunGuard.UnfinishedWasCharged;
            RunGuard.NoteReported();

            // Only when a heart was actually taken. A player who was already at zero owes
            // nothing, and telling them about a charge that did not happen is worse than
            // saying nothing at all.
            if (!charged) return;

            Tween.After(.5f, () =>
            {
                if (!this) return;
                Scenery.Toast(Content, Loc.Format("ui.forfeit.unfinished", glade),
                              Pal.Ember, 3.2f, new Vector2(.5f, 1f), -300f);
            }, this);
        }
    }
}
