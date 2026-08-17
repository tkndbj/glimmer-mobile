using System.Collections.Generic;
using System.Text;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// How a reward is drawn: its glyph, its colour and what it is called.
    ///
    /// Kept beside the overlay rather than on <see cref="ChestDropKind"/> because a
    /// sprite path is a Presentation fact and Domain is not allowed to hold one. The
    /// loc keys are written out in an array rather than assembled from the kind's id,
    /// so the build's string checker can see every one of them — a key that only exists
    /// at runtime is a key nothing can verify.
    ///
    /// <para>
    /// Every glyph here carries its own colour and is drawn white — never tinted. The
    /// heart boost used to break that rule: it was <c>ic_power</c>, the standard on/off
    /// symbol, washed in aqua, which named the wrong thing twice. The glyph said "power
    /// button" and the colour said nothing, so the one reward on the streak ladder a
    /// player has never met before was also the only one they could not read. It is now
    /// a heart with a bolt through it, built from the two glyphs this UI already ships.
    /// </para>
    /// </summary>
    static class RewardArt
    {
        static readonly string[] NameKeys =
        {
            string.Empty,
            "ui.reward.credits",
            "ui.reward.gems",
            "ui.reward.hearts",
            "ui.reward.heart_boost",
        };

        public static string Name(ChestDropKind kind)
        {
            int i = (int)kind;
            return i > 0 && i < NameKeys.Length ? Loc.Get(NameKeys[i]) : string.Empty;
        }

        public static Color Tint(ChestDropKind kind)
        {
            switch (kind)
            {
                case ChestDropKind.Credits: return Pal.Gold;
                case ChestDropKind.Gems: return Pal.Bloom;
                case ChestDropKind.Hearts: return Pal.Rose;
                case ChestDropKind.HeartBoost: return Pal.Aqua;
                default: return Pal.Cream;
            }
        }

        /// <summary>Null for credits, which use the spinning coin flipbook instead.</summary>
        public static Sprite Icon(ChestDropKind kind)
        {
            switch (kind)
            {
                case ChestDropKind.Gems: return Art.S("Ui/ic_gem");
                case ChestDropKind.Hearts: return Art.S("Ui/ic_heart");
                case ChestDropKind.HeartBoost: return Art.S("Ui/ic_heart_boost");
                default: return null;
            }
        }

        /// <summary>The number as the player reads it: hours for a boost, a count otherwise.</summary>
        public static string Amount(ChestDrop drop)
            => drop.Kind == ChestDropKind.HeartBoost ? drop.Amount + "h" : "+" + drop.Amount;

        /// <summary>
        /// Finishes a reward glyph that <see cref="Icon"/> could not supply on its own.
        ///
        /// <para>
        /// Credits are the one kind with no sprite: they are a spinning flipbook, and
        /// <see cref="Icon"/> returns null for them. That makes every caller that draws a
        /// reward responsible for the same three lines, and the moment a third one appeared
        /// — the streak board, once its ladder started paying credits — the second one had
        /// already drifted: <c>AdOfferOverlay</c> attached the flipbook without checking
        /// whether its frames were resident.
        /// </para>
        /// <para>
        /// That check is the whole reason this is worth centralising. Art loads
        /// asynchronously by scope (invariant 7b), and an <c>Image</c> with no sprite is a
        /// white rectangle rather than a blank — so a hundred-pixel white square in the
        /// middle of a reward reveal is how a caller discovers the frames had not arrived.
        /// The fallback is a tinted disc, which reads as a coin at a glance and never as a
        /// bug.
        /// </para>
        /// </summary>
        public static void Glyph(Image icon, ChestDropKind kind, float fps = 12f)
        {
            if (icon == null || kind != ChestDropKind.Credits) return;

            var frames = Art.Frames("Ui/Coin");

            if (frames != null && frames.Length > 0) Flipbook.Attach(icon, "Ui/Coin", fps);
            else { icon.sprite = Art.Disc(128); icon.color = Tint(kind); }
        }
    }

    /// <summary>
    /// Opening a daily chest.
    ///
    /// <para>
    /// The whole overlay exists for about four seconds and does one job: make a number
    /// arriving in a ledger feel like something happening. It is built as a sequence
    /// rather than a state machine because it is one — anticipation, release, reveal —
    /// and each beat is scheduled off the last so retiming the middle does not require
    /// re-deriving every delay after it.
    /// </para>
    /// <para>
    /// The reward is applied to the save at the <em>start</em>, not when the animation
    /// finishes. A player who kills the app mid-burst has still opened the chest, and a
    /// grant that depended on an animation completing would be a grant that a slow phone
    /// could lose. What is on screen is a report of something that has already happened.
    /// </para>
    /// </summary>
    public sealed class ChestOverlay : ModalView
    {
        public int ChestIndex;

        Image _chest;
        Image _seam;
        RectTransform _rewardRow;
        List<ChestDrop> _drops;
        bool _heartsWereWasted;

        // Beat timings, in seconds from the overlay appearing. Named because the numbers
        // are the design: three thumps under two seconds is a tease, and over three is a
        // player wondering whether the game has hung.
        const float ThumpFirst = 0.55f;
        const float ThumpGap = 0.34f;
        const int Thumps = 3;
        const float BurstAt = ThumpFirst + Thumps * ThumpGap;
        const float RevealAt = BurstAt + 0.30f;
        const float RevealGap = 0.34f;

        protected override void Build()
        {
            UIKit.Scrim(Content, .86f);

            // Hearts are read before the grant so the panel can say the honest thing when a
            // heart drop lands somewhere it cannot go. Asking afterwards would always say
            // so, because the grant is what put them there.
            //
            // The test is the ceiling rather than the refill cap: a chest opened at a full
            // bar now keeps its hearts, so the old apology would have been a lie told to
            // most of the players who saw it.
            _heartsWereWasted = Profile.HeartState.IsAtCeiling;

            if (!DailyChests.TryOpen(ChestIndex, out _drops))
            {
                // Beaten to it — another device synced the claim in, or the day rolled
                // over between the tap and this frame. Nothing to show and nothing lost.
                //
                // Dismissed next frame rather than here: Build() runs inside Flow.Modal
                // before the view has been added to the modal stack, so tearing down now
                // would leave a destroyed object in it.
                Tween.After(0f, () => Flow.Dismiss(this), this);
                return;
            }

            BuildTitle();
            BuildChest();

            Panel = (RectTransform)_chest.transform;   // what ModalView.Close() scales out

            ScheduleThumps();
            Tween.After(BurstAt, OpenLid, this);
            Tween.After(RevealAt, RevealRewards, this);
            Tween.After(RevealAt + _drops.Count * RevealGap + 0.25f, BuildFooter, this);

            Audio.Duck(.4f, 3.2f);
        }

        void BuildTitle()
        {
            var ribbon = UIKit.Img("Ribbon", Content, Art.S("Ui/ribbon_orange"), Color.white,
                                   new Vector2(620f, 132f), new Vector2(.5f, 1f), new Vector2(0f, -300f));
            UIKit.Titled("T", ribbon.transform, Loc.Get("ui.daily.chest_title"), 50, Pal.Cream,
                         TextAnchor.MiddleCenter, outline: 4f, shadow: 4f);
            ribbon.transform.localRotation = Quaternion.Euler(0, 0, -1.6f);

            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .08f);
        }

        void BuildChest()
        {
            var host = UIKit.Box("ChestHost", Content, new Vector2(520f, 520f),
                                 new Vector2(.5f, .5f), new Vector2(0f, 150f));

            // Rays first so everything else sits on top of them.
            var rays = UIKit.Box("Rays", host, Vector2.one * 620f, new Vector2(.5f, .5f), Vector2.zero);
            for (int i = 0; i < 8; i++)
            {
                var ray = UIKit.Img("r" + i, rays, Art.SoftCapsule(40, 200), Pal.A(Pal.Sun, .16f),
                                    new Vector2(46f, 700f), new Vector2(.5f, .5f), Vector2.zero);
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 22.5f);
            }
            Tween.Run(14f, Ease.Linear,
                      t => { if (rays) rays.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      rays.gameObject, "spin").Loop(-1, false);

            UIKit.Halo(host, Pal.Gold, 520f, .40f);

            // The light escaping the lid. Dim now; the thumps brighten it, which is what
            // sells the idea that something inside is trying to get out.
            var seam = UIKit.Img("Seam", host, Art.Glow(128, 2.4f), Pal.A(Pal.Radiance, 0f),
                                 new Vector2(300f, 300f), new Vector2(.5f, .5f), new Vector2(0f, 20f));
            seam.name = "Seam";

            _chest = UIKit.Img("Chest", host, Art.S("Ui/ic_chest"), Color.white,
                               new Vector2(340f, 340f), new Vector2(.5f, .5f), Vector2.zero);
            _chest.preserveAspect = true;
            _chest.transform.localScale = Vector3.zero;
            Tween.Scale(_chest.transform, 1f, .55f, Ease.OutBack).Delay(.05f);

            _seam = seam;
        }

        /// <summary>
        /// Three escalating knocks from inside the lid.
        ///
        /// Each one is louder, higher, harder and brighter than the last. The escalation
        /// is doing all of the work — three identical thumps read as a loading spinner.
        /// </summary>
        void ScheduleThumps()
        {
            for (int i = 0; i < Thumps; i++)
            {
                int step = i;
                float at = ThumpFirst + i * ThumpGap;

                Tween.After(at, () =>
                {
                    if (this == null || !_chest) return;

                    float force = .12f + .09f * step;

                    Tween.Punch(_chest.transform, force, ThumpGap * .92f);
                    Tween.Shake((RectTransform)_chest.transform, 8f + 7f * step, ThumpGap * .8f);

                    Audio.Sfx("tock", .42f + .12f * step, .82f + .16f * step);
                    Haptic.Tap();

                    if (_seam)
                    {
                        float glow = .22f + .26f * step;
                        Tween.Tint(_seam, Pal.A(Pal.Radiance, glow), ThumpGap * .5f);
                    }

                    Burst.Sparks(_chest.transform, new Vector2(0f, 40f), Pal.Sun,
                                 3 + step * 3, 90f + 40f * step, 14f, .4f);
                }, this);
            }
        }

        /// <summary>The lid gives. Everything happens at once, which is the point.</summary>
        void OpenLid()
        {
            if (this == null || !_chest) return;

            _chest.sprite = Art.S("Ui/ic_chest_open");

            Flow.Flash(Pal.Radiance, .85f, .55f);
            Audio.Sfx("chest", .85f);
            Audio.Sfx("win", .5f, 1f, .06f);
            Haptic.Tap();

            Tween.Scale(_chest.transform, 1.22f, .16f, Ease.OutQuad)
                 .OnDone(() => { if (_chest) Tween.Scale(_chest.transform, 1f, .5f, Ease.OutBack); });

            if (_seam)
            {
                Tween.Tint(_seam, Pal.A(Pal.Radiance, .9f), .12f)
                     .OnDone(() => { if (_seam) Tween.Tint(_seam, Pal.A(Pal.Radiance, .30f), .7f); });
            }

            // An expanding ring, which is the cheapest possible shockwave and reads as
            // one on every phone this will ever run on.
            var ring = UIKit.Img("Shock", _chest.transform.parent, Art.Ring(256, 18f),
                                 Pal.A(Pal.Radiance, .85f), Vector2.one * 260f,
                                 new Vector2(.5f, .5f), Vector2.zero);
            Tween.Run(.75f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.5f, 3.4f, t);
                ring.color = Pal.A(Pal.Radiance, .85f * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });

            Burst.Sparks(_chest.transform, new Vector2(0f, 30f), Pal.Gold, 26, 460f, 34f, .85f);
            Burst.Confetti(Content, 60);
        }

        /// <summary>
        /// The prizes, arcing out of the chest one at a time and settling into a row.
        ///
        /// One at a time on purpose: two rewards that appear together are one event, and
        /// three-tenths of a second apart is the difference between "I got some stuff"
        /// and counting them.
        /// </summary>
        void RevealRewards()
        {
            if (this == null) return;

            _rewardRow = UIKit.Box("Rewards", Content, new Vector2(900f, 260f),
                                   new Vector2(.5f, .5f), new Vector2(0f, -230f));

            const float step = 250f;
            float left = -(_drops.Count - 1) * step * .5f;

            for (int i = 0; i < _drops.Count; i++)
            {
                var drop = _drops[i];
                float x = left + i * step;
                float at = i * RevealGap;

                Tween.After(at, () => { if (this != null) RevealOne(drop, x); }, this);
            }
        }

        void RevealOne(ChestDrop drop, float x)
        {
            if (!_rewardRow) return;

            var tint = RewardArt.Tint(drop.Kind);

            var card = UIKit.Img("R", _rewardRow, Art.Round(26), new Color(.04f, .09f, .12f, .82f),
                                 new Vector2(206f, 236f), new Vector2(.5f, .5f), new Vector2(x, 0f));
            var edge = UIKit.Img("Edge", card.transform, Art.RoundOutline(26, 3f), Pal.A(tint, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Halo(card.transform, tint, 260f, .30f);

            var icon = UIKit.Img("Icon", card.transform, RewardArt.Icon(drop.Kind), Color.white,
                                 new Vector2(104f, 104f), new Vector2(.5f, 1f), new Vector2(0f, -70f));
            icon.preserveAspect = true;

            RewardArt.Glyph(icon, drop.Kind);

            if (drop.Kind == ChestDropKind.HeartBoost) icon.color = tint;

            UIKit.Titled("Amount", card.transform, RewardArt.Amount(drop), 46, Pal.Cream,
                         TextAnchor.MiddleCenter, new Vector2(190f, 56f), new Vector2(.5f, 1f),
                         new Vector2(0f, -148f), 3f, 3f);
            UIKit.Titled("Name", card.transform, RewardArt.Name(drop.Kind), 24, Pal.A(tint, .92f),
                         TextAnchor.MiddleCenter, new Vector2(190f, 34f), new Vector2(.5f, 1f),
                         new Vector2(0f, -198f), 0f, 0f);

            // Out of the chest, over the top, and down into place.
            var rt = (RectTransform)card.transform;
            var home = rt.anchoredPosition;
            var from = new Vector2(x * .18f, 300f);

            rt.anchoredPosition = from;
            rt.localScale = Vector3.zero;

            Tween.Run(.52f, Ease.OutCubic, t =>
            {
                if (!rt) return;
                var p = Vector2.Lerp(from, home, t);
                p.y += Mathf.Sin(t * Mathf.PI) * 90f;      // the arc
                rt.anchoredPosition = p;
                rt.localScale = Vector3.one * Mathf.Lerp(.2f, 1f, Ease.OutBack(t));
            }, card).OnDone(() =>
            {
                if (!card) return;
                Tween.Punch(card.transform, .16f, .3f);
                Burst.Sparks(card.transform, Vector2.zero, tint, 10, 180f, 20f, .5f);
            });

            Audio.Sfx("star", .55f, 1f);
            Audio.Sfx("chime2", .4f, 1.08f, .05f);
            Haptic.Tap();
        }

        /// <summary>
        /// The collect button, the odds, and the two notes a player may need.
        ///
        /// The odds are here rather than buried in a settings screen because a randomised
        /// reward should be able to explain itself where it is given. It costs one line
        /// and it is the difference between a feature that is obviously fair and one that
        /// has to be argued about later.
        /// </summary>
        void BuildFooter()
        {
            if (this == null) return;

            string note = FootNote();
            if (note.Length > 0)
            {
                UIKit.Titled("Note", Content, note, 26, Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter,
                             new Vector2(860f, 40f), new Vector2(.5f, .5f), new Vector2(0f, -400f),
                             0f, 0f, wrap: true);
            }

            // Shrinkable: this line grows with the number of options and again with
            // translation, and it is a disclosure — a disclosure that runs off the side
            // of the screen is not one.
            UIKit.Shrinkable(
                UIKit.Titled("Odds", Content, OddsLine(), 21, new Color(1f, .96f, .86f, .42f),
                             TextAnchor.MiddleCenter, new Vector2(940f, 34f), new Vector2(.5f, 0f),
                             new Vector2(0f, 168f), 0f, 0f), 14);

            var collect = UIKit.TextButton("Collect", Content, "btn_green", Loc.Get("ui.daily.collect"), 52,
                                           new Vector2(560f, 148f), new Vector2(.5f, 0f),
                                           new Vector2(0f, 250f), () => Close());
            UIKit.Halo(collect.transform, Pal.Mint, 640f, .30f);

            collect.transform.localScale = Vector3.zero;
            Tween.Pop(collect.transform, 0f, .55f).OnDone(() =>
            {
                if (!collect) return;
                collect.Rehome();
                Sheen.Attach((RectTransform)collect.transform, 3.2f);
            });
        }

        /// <summary>
        /// The one thing a player might otherwise be confused by: a heart drop that hit
        /// the cap, or a boost that is now running.
        /// </summary>
        string FootNote()
        {
            for (int i = 0; i < _drops.Count; i++)
            {
                if (_drops[i].Kind == ChestDropKind.HeartBoost)
                    return Loc.Format("ui.daily.boost_on",
                                      Profile.Countdown(Wallet.HeartBoostSecondsLeft));

                if (_drops[i].Kind == ChestDropKind.Hearts && _heartsWereWasted)
                    return Loc.Format("ui.daily.hearts_ceiling", Profile.HeartCeiling);
            }
            return string.Empty;
        }

        /// <summary>
        /// The published odds for this chest's variable slot, read from the same table the
        /// roll used. Generated rather than written, so it cannot drift from the weights.
        /// </summary>
        string OddsLine()
        {
            var definition = DailyChests.Definition(ChestIndex);
            if (definition == null || definition.Options.Count == 0) return string.Empty;

            var text = new StringBuilder(Loc.Get("ui.daily.odds"));

            for (int i = 0; i < definition.Options.Count; i++)
            {
                text.Append("  ·  ")
                    .Append(RewardArt.Name(definition.Options[i].Band.Kind))
                    .Append(' ')
                    .Append(Mathf.RoundToInt(definition.ChanceOf(i)))
                    .Append('%');
            }

            return text.ToString();
        }

        public override bool OnBack() { Close(); return true; }
    }
}
