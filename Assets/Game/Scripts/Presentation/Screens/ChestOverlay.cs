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
        /// <summary>
        /// One key per <see cref="ChestDropKind"/>, indexed by the enum's own value — so the
        /// order here is the enum's order and not a preference. The empty slot at index 5 is
        /// the retired <c>RunTime</c>: its value is frozen because the daily chest vectors key
        /// on it (invariant 9c), nothing can produce it any more, and an empty string is what
        /// <see cref="Name"/> already answers for anything it cannot name. Filling it would
        /// shift every kind after it onto the wrong noun.
        /// </summary>
        static readonly string[] NameKeys =
        {
            string.Empty,
            "ui.reward.credits",
            "ui.reward.gems",
            "ui.reward.hearts",
            "ui.reward.heart_boost",
            string.Empty,                       // ChestDropKind.RunTime, retired
            "ui.reward.hints",
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

                // Radiance rather than a fifth hue. Time is the one prize here that is not a
                // resource with a pill on the hub, so giving it a colour of its own would put
                // a sixth entry in a vocabulary the player has learned has five — and cream-
                // white is what this UI already means by "not one of the coloured things".
                case ChestDropKind.RunTime: return Pal.Radiance;

                // The hint button's own orange, so the prize and the control it fills read as
                // one thing. It is not a sixth hue: sq_orange is already what this UI means by
                // "the hint".
                case ChestDropKind.Hints: return Pal.Amber;

                default: return Pal.Cream;
            }
        }

        /// <summary>
        /// Which readout on the hub a prize belongs in, and whether it has one at all.
        ///
        /// <para>
        /// A heart boost answers <c>Hearts</c> rather than nothing, and that is the whole
        /// judgement in this method. It changes no balance — it is a timer — but it is a
        /// thing that happened *to the hearts*, so flying it into the heart pill says
        /// exactly what it does. Landing it nowhere would leave one prize on the panel
        /// that the collect animation quietly ignored, which is the sort of gap a player
        /// reads as the reward not having been given.
        /// </para>
        /// </summary>
        public static bool Slot(ChestDropKind kind, out ResourceSlots.Kind slot)
        {
            switch (kind)
            {
                case ChestDropKind.Credits: slot = ResourceSlots.Kind.Credits; return true;
                case ChestDropKind.Gems: slot = ResourceSlots.Kind.Gems; return true;
                case ChestDropKind.Hearts:
                case ChestDropKind.HeartBoost: slot = ResourceSlots.Kind.Hearts; return true;

                // RunTime and Hints fall through to false deliberately, and unlike a heart
                // boost neither is a near miss. A boost answers Hearts because it is a thing
                // that happened to the hearts; seconds on the run in progress are not a balance
                // at all, and a hint — though it is banked — has no pill on the hub either,
                // only a badge on a button that lives on the play screen. Neither panel that
                // pays one uses the collect animation, so nothing is left looking ungiven.
                default: slot = ResourceSlots.Kind.Credits; return false;
            }
        }

        /// <summary>
        /// The small thing that flies to the pill. Deliberately not the card's glyph: a
        /// coin in flight is one frame of the spinning flipbook, because attaching a
        /// running <see cref="Flipbook"/> to twenty transient tokens is twenty components
        /// animating themselves for half a second each to say what one still frame says.
        ///
        /// Falls back to a tinted disc by the same argument <see cref="Glyph"/> makes —
        /// an <c>Image</c> with no sprite is a white rectangle, and a handful of white
        /// rectangles crossing the screen is how a player discovers the art had not loaded.
        /// </summary>
        public static void Token(ChestDropKind kind, out Sprite sprite, out Color tint)
        {
            if (kind == ChestDropKind.Credits)
            {
                var frames = Art.Frames("Ui/Coin");
                if (frames != null && frames.Length > 0) { sprite = frames[0]; tint = Color.white; return; }
            }
            else
            {
                sprite = Icon(kind);
                if (sprite != null)
                {
                    tint = kind == ChestDropKind.HeartBoost ? Tint(kind) : Color.white;
                    return;
                }
            }

            sprite = Art.Disc(64);
            tint = Tint(kind);
        }

        /// <summary>Null for credits, which use the spinning coin flipbook instead.</summary>
        public static Sprite Icon(ChestDropKind kind)
        {
            switch (kind)
            {
                case ChestDropKind.Gems: return Art.S("Ui/ic_gem");
                case ChestDropKind.Hearts: return Art.S("Ui/ic_heart");
                case ChestDropKind.HeartBoost: return Art.S("Ui/ic_heart_boost");

                // Generated, not addressed — see Art.Dial. This one is drawn at the instant a
                // run is lost, which is the worst moment in the game to show a white square
                // because a sprite had not finished loading.
                case ChestDropKind.RunTime: return Art.Dial(128);

                case ChestDropKind.Hints: return Art.S("Ui/ic_hint");

                default: return null;
            }
        }

        /// <summary>
        /// The number as the player reads it: hours for a boost, seconds for run time, a
        /// plain count otherwise.
        ///
        /// The units matter more here than they look. A boost's amount is hours and a
        /// continue's is seconds, so both would read as a quantity of *something* without
        /// them — "+24" beside a clock face is a fine way to promise twenty-four minutes.
        /// </summary>
        public static string Amount(ChestDrop drop)
            => drop.Kind == ChestDropKind.HeartBoost ? drop.Amount + "h"
             : drop.Kind == ChestDropKind.RunTime ? "+" + drop.Amount + "s"
             : "+" + Compact.Number(drop.Amount);

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
        Image _scrim;
        RectTransform _rewardRow;
        List<ChestDrop> _drops;
        bool _heartsWereWasted;

        /// <summary>Each prize's card, in drop order, so collecting can throw it.</summary>
        RectTransform[] _cards;

        /// <summary>
        /// Everything that is not a prize: the ribbon, the chest, the odds, the button.
        /// Collected as it is built so the payout can clear the screen in one line without
        /// a second description of what the panel is made of.
        /// </summary>
        readonly List<RectTransform> _chrome = new List<RectTransform>();

        /// <summary>Set the moment Collect is pressed, so it cannot be pressed twice.</summary>
        bool _collecting;

        /// <summary>
        /// The payout, opened before the chest is granted so it can snapshot what each pill
        /// read at the time. See <see cref="RewardFlight.Begin"/>.
        /// </summary>
        RewardFlight _flight;

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
            _scrim = UIKit.Scrim(Content, .86f);

            // Hearts are read before the grant so the panel can say the honest thing when a
            // heart drop lands somewhere it cannot go. Asking afterwards would always say
            // so, because the grant is what put them there.
            //
            // The test is the ceiling rather than the refill cap: a chest opened at a full
            // bar now keeps its hearts, so the old apology would have been a lie told to
            // most of the players who saw it.
            _heartsWereWasted = Profile.HeartState.IsAtCeiling;

            // What each pill reads before the chest is granted, captured rather than derived.
            //
            // Deriving it at collect time — take today's balance, subtract what the chest
            // said — is wrong in exactly the case the line above is about: a heart drop that
            // lands at the ceiling grants nothing, so subtracting its amount would rewind the
            // pill below where it ever was and then count it up to a gain the player did not
            // receive. The same is true of any prize a rule may clamp later. Reading the
            // balance before the grant cannot be wrong about it.
            _flight = RewardFlight.Begin();

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
            _chrome.Add((RectTransform)ribbon.transform);
        }

        void BuildChest()
        {
            var host = UIKit.Box("ChestHost", Content, new Vector2(520f, 520f),
                                 new Vector2(.5f, .5f), new Vector2(0f, 150f));
            _chrome.Add(host);

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
            _cards = new RectTransform[_drops.Count];

            const float step = 250f;
            float left = -(_drops.Count - 1) * step * .5f;

            for (int i = 0; i < _drops.Count; i++)
            {
                var drop = _drops[i];
                int index = i;
                float x = left + i * step;
                float at = i * RevealGap;

                Tween.After(at, () => { if (this != null) RevealOne(index, drop, x); }, this);
            }
        }

        void RevealOne(int index, ChestDrop drop, float x)
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
            _cards[index] = rt;
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
                _chrome.Add((RectTransform)
                    UIKit.Titled("Note", Content, note, 26, Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter,
                                 new Vector2(860f, 40f), new Vector2(.5f, .5f), new Vector2(0f, -400f),
                                 0f, 0f, wrap: true).transform);
            }

            // Shrinkable: this line grows with the number of options and again with
            // translation, and it is a disclosure — a disclosure that runs off the side
            // of the screen is not one.
            _chrome.Add((RectTransform)UIKit.Shrinkable(
                UIKit.Titled("Odds", Content, OddsLine(), 21, new Color(1f, .96f, .86f, .42f),
                             TextAnchor.MiddleCenter, new Vector2(940f, 34f), new Vector2(.5f, 0f),
                             new Vector2(0f, 168f), 0f, 0f), 14).transform);

            var collect = UIKit.TextButton("Collect", Content, "btn_green", Loc.Get("ui.daily.collect"), 52,
                                           new Vector2(560f, 148f), new Vector2(.5f, 0f),
                                           new Vector2(0f, 250f), Collect);
            _chrome.Add((RectTransform)collect.transform);
            UIKit.Halo(collect.transform, Pal.Mint, 640f, .30f);

            collect.transform.localScale = Vector3.zero;
            Tween.Pop(collect.transform, 0f, .55f).OnDone(() =>
            {
                if (!collect) return;
                collect.Rehome();
                Sheen.Attach((RectTransform)collect.transform, 3.2f);
            });
        }

        // ------------------------------------------------------------- collecting
        /// <summary>
        /// Pays the chest into the hub: every prize breaks into a handful of tokens, they
        /// arc into their own resource pill, and each landing steps that pill's number.
        ///
        /// <para>
        /// The cascade itself is <see cref="RewardFlight"/>, which is where the rhythm, the
        /// rewind and the promise that this panel closes all live. It was written here and
        /// lifted out when the rewarded ad needed the same thing — see that class for why a
        /// second copy would have been a second chance to strand a player on a panel with no
        /// button on it.
        /// </para>
        /// <para>
        /// What stays here is what belongs to a chest: the chrome that has to get out of the
        /// way, and the row the tokens fall back to once the cards have gone.
        /// </para>
        /// <para>
        /// If no pill can be resolved — the hub is not the screen underneath, or it has been
        /// torn down — this degrades to the old behaviour and just closes. A reward that has
        /// already been banked must never depend on an animation being able to run.
        /// </para>
        /// </summary>
        void Collect()
        {
            if (_collecting) return;
            _collecting = true;

            if (_drops == null || _cards == null || _flight == null) { Close(); return; }

            for (int i = 0; i < _drops.Count; i++) _flight.Add(_drops[i], _cards[i]);

            // Every prize was a kind with no pill, or the hub is gone. Cannot happen with
            // today's drop table on the screen a chest is opened from, and closing is still
            // the right answer if it ever can.
            if (!_flight.Any) { Close(); return; }

            _flight.Fallback = _rewardRow;

            FadeChrome();
            _flight.Play(Content, () => Flow.Dismiss(this));
        }

        /// <summary>
        /// Clears everything that is not a prize, the scrim included — the hub's pills are
        /// under it, and a token cannot be seen landing on something that is not visible.
        /// </summary>
        void FadeChrome()
        {
            if (_scrim) Tween.Fade(_scrim, 0f, RewardFlight.ClearAt);

            for (int i = 0; i < _chrome.Count; i++)
            {
                var rt = _chrome[i];
                if (!rt) continue;
                Tween.Fade(UIKit.Group(rt), 0f, RewardFlight.ClearAt * .8f);
            }
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

        /// <summary>
        /// Swallowed once the payout has started. Everything is already banked, so leaving
        /// early costs the player nothing — but <see cref="ModalView.Close"/> fades the whole
        /// content group, and the tokens are in it, so the back key would delete the animation
        /// mid-flight and leave the pills rewound to their old figures until the hub next
        /// rebuilt them.
        /// </summary>
        public override bool OnBack()
        {
            if (_collecting) return true;
            Close();
            return true;
        }
    }
}
