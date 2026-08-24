using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Privacy;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The line that says the streak grew, shown on whichever panel ends the run.
    ///
    /// <para>
    /// It appears after a defeat as well as after a win, and that is the whole reason it
    /// exists as a shared thing rather than as another beat in the victory sequence. A
    /// player who has just lost is being told, in the same breath, that the run still
    /// counted — which is true (the streak counts finished runs, not won ones) and is the
    /// single most useful thing a defeat screen can say. A streak that only survived wins
    /// would make the hardest glade in a chapter the place streaks go to die.
    /// </para>
    /// <para>
    /// A toast rather than a panel element, so it can be dropped into either sequence
    /// without either panel having to make room for it, and so it costs nothing on the
    /// runs — most of them — where the streak did not move.
    /// </para>
    /// </summary>
    static class StreakToast
    {
        public static void Show(View host, StreakNote streak, float delay)
        {
            if (host == null || !streak.WorthSaying) return;

            Tween.After(delay, () =>
            {
                if (host == null) return;
                Audio.Sfx("bell", .5f, 1.05f);

                // Says so when the night put something aside. Rungs are collected by hand
                // now, so a player who is only told "six days running" has no way to know
                // there is a reward sitting on the streak page — and a reward nobody is
                // told about is a reward that is never taken.
                string line = Daily.DailyStreak.AnyPending
                    ? Loc.Format("ui.streak.kept_waiting", streak.Days)
                    : Loc.Format("ui.streak.kept", streak.Days);

                Scenery.Toast(host.Content, line, Pal.Sun, 2.6f, new Vector2(.5f, 1f), -250f);
            }, host);
        }
    }

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

            // A rebuild is the same panel in a new state, not a new panel: replaying the
            // entrance would pop and chime at a player who tapped a button on the panel that
            // is already in front of them. Same distinction the grove's grids draw between
            // Show and Refresh, and for the same reason.
            if (!Rebuilding)
            {
                Panel.localScale = Vector3.zero;
                Tween.Scale(Panel, 1f, .5f, Ease.OutBack);
                Audio.Sfx("chime", .45f, 1.1f);
            }

            return Panel;
        }

        /// <summary>True while <see cref="Rebuild"/> is replacing the panel.</summary>
        protected bool Rebuilding { get; private set; }

        /// <summary>
        /// Throws the panel away and builds it again for whatever state the overlay is now in.
        ///
        /// <para>
        /// For a panel whose height depends on what it is saying — an account screen that grows
        /// two provider buttons, say. The alternative is reserving room for the tallest state
        /// and living with a hole in the others, or writing a second set of coordinates for the
        /// expanded layout, and this file has already recorded what the second one costs: the
        /// two layouts drift and a line of text ends up printed through a button.
        /// </para>
        /// <para>
        /// Hides each child before destroying it. <c>Destroy</c> lands at the end of the frame,
        /// so without that the outgoing panel is drawn over its replacement for one frame — the
        /// house rule five screens had to learn one at a time.
        /// </para>
        /// </summary>
        protected void Rebuild()
        {
            if (_closing) return;

            Rebuilding = true;
            try
            {
                for (int i = Content.childCount - 1; i >= 0; i--)
                {
                    var child = Content.GetChild(i).gameObject;
                    child.SetActive(false);
                    Destroy(child);
                }

                Build();
            }
            finally
            {
                Rebuilding = false;
            }
        }

        /// <summary>
        /// Puts the panel away, and optionally runs something once it is gone.
        ///
        /// <para>
        /// <paramref name="quiet"/> suppresses the dismissal sound, and it is for exactly one
        /// case: a panel that is closing because the player <em>bought</em> something, where
        /// the next thing they hear is the celebration and a backing-out whoosh underneath it
        /// is one sound too many. It is not a general volume control — an ordinary close makes
        /// a noise because an ordinary close is the player leaving.
        /// </para>
        /// </summary>
        protected void Close(Action after = null, bool quiet = false)
        {
            if (_closing) return;
            _closing = true;
            if (!quiet) Audio.SfxVaried("back", .5f);
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
                img.sprite = Art.S("Ui/" + (on ? Skins.Nav : Skins.Resting));
                if (b.Icon) b.Icon.color = on ? Pal.Cream : new Color(.72f, .78f, .84f, .7f);
            }
            b = UIKit.IconButton("T_" + icon, parent, Skins.Nav, icon, new Vector2(124f, 124f),
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
        public RunScreen Screen;
        public int HeartsLeft;

        /// <summary>The run that was lost, decided by the screen. See <see cref="RunOutcome"/>.</summary>
        public RunOutcome Run { get; set; }

        /// <summary>What the streak did. Shown here too — see <see cref="StreakToast.Show"/>.</summary>
        public StreakNote Streak { get; set; }

        /// <summary>False when the player was already at zero — then nothing was taken.</summary>
        public bool HeartWasCharged;

        Btn _watch;

        /// <summary>
        /// Keeps the offer button's countdown live while the panel is open.
        ///
        /// A defeat panel is somewhere players sit for a while, deciding. A cooldown that
        /// only updated when the screen was reopened would tick down invisibly and the
        /// button would stay stale until they gave up on it.
        /// </summary>
        void Update() => PaintWatch();

        void PaintWatch()
            => AdOfferButton.Paint(_watch, AdPlacement.HeartRefill, "ui.ads.hearts_cta");


        /// <summary>
        /// Written out rather than built from the enum name, so the loc gate can see
        /// every key. A concatenated key is invisible to the scanner and ships missing.
        /// </summary>
        static string TitleKey(DefeatReason reason)
        {
            switch (reason)
            {
                case DefeatReason.ConduitLost: return "ui.defeat.conduit_title";
                case DefeatReason.OutOfTime: return "ui.defeat.time_title";
                default: return "ui.defeat.moves_title";
            }
        }

        static string ReasonKey(DefeatReason reason)
        {
            switch (reason)
            {
                case DefeatReason.ConduitLost: return "ui.defeat.conduit_reason";
                case DefeatReason.OutOfTime: return "ui.defeat.time_reason";
                default: return "ui.defeat.moves_reason";
            }
        }

        /// <summary>
        /// One sentence per distance, written out for the same reason
        /// <see cref="WinOverlay.RankKeys"/> is: a key assembled at runtime is a key the
        /// build's string checker cannot see, and it ships missing in whichever language
        /// nobody tested.
        ///
        /// Indexed by turns short, minus one. The array's length is therefore the honest
        /// statement of how far "near" reaches — widen <see cref="RunOutcome.NearMissTurns"/>
        /// and the compiler does not care, but the sentence has to be written before a
        /// player can be told it.
        /// </summary>
        static readonly string[] NearMissKeys = { "ui.defeat.near_one", "ui.defeat.near_two" };

        protected override void Build()
        {
            bool canRetry = HeartsLeft > 0;

            // The offer only belongs on the branch that has no retry button. A player who
            // can still play does not need to be sold a video, and putting one there would
            // turn every defeat into an advertisement.
            //
            // ShouldOffer, not CanOffer: a cooldown draws the button disabled with its own
            // countdown rather than hiding it, so a player who watched one a minute ago can
            // see when the next is due instead of concluding the offer was a fluke.
            bool offering = !canRetry && RewardedAds.ShouldOffer(AdPlacement.HeartRefill);

            // Grown rather than crowded. The alternative — squeezing a third button into
            // the same 880 — leaves the last one a few pixels off the panel edge, which is
            // the sort of thing that looks fine on the device it was tuned on.
            MakePanel(new Vector2(880f, offering ? 1010f : 880f),
                      Loc.Get(TitleKey(Run.Reason)), dismissOnScrim: false);

            // Body copy, drawn the way every other panel here draws it: wrapped, and
            // with no outline or shadow. Those two are for headings sitting on a ribbon;
            // on a 32pt sentence they smear the strokes together and it stops reading.
            Body("Why", Loc.Get(ReasonKey(Run.Reason)), -186f, 150f);

            BuildHowClose();
            BuildHearts();

            if (canRetry)
            {
                UIKit.TextButton("Retry", Panel, "btn_green", Loc.Get("ui.defeat.try_again"), 52,
                                 new Vector2(620f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -560f),
                                 () => Close(() => { if (Screen) Screen.RetryAfterDefeat(); }));
            }
            else if (offering)
            {
                // Out of hearts, but there is a way back in. The sentence changes with it:
                // telling somebody to wait eight hours directly above a button that skips
                // the wait is how a panel reads as a trick.
                Body("Wait", Loc.Get("ui.defeat.watch_for_hearts"), -520f, 96f, Pal.Ember);

                _watch = UIKit.TextButton("WatchAd", Panel, "btn_green", Loc.Get("ui.ads.hearts_cta"), 44,
                                          new Vector2(620f, 140f), new Vector2(.5f, 1f), new Vector2(0f, -644f),
                                          OfferHearts, "ic_play");
                PaintWatch();
            }
            else
            {
                // Out of hearts and no ad to be had: a retry button would be a lie, so it
                // is not offered and the honest wait is all there is to say.
                Body("Wait", Loc.Get("ui.defeat.out_of_hearts"), -540f, 130f, Pal.Ember);
            }

            UIKit.TextButton("Glades", Panel, "btn_blue", Loc.Get("ui.pause.glades"), 46,
                             new Vector2(620f, 132f), new Vector2(.5f, 1f),
                             new Vector2(0f, canRetry ? -722f : offering ? -816f : -700f),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            // Last, and after the near-miss line has had its moment: a lost run still fed
            // the streak, which is the one piece of good news this panel has.
            StreakToast.Show(this, Streak, 1.05f);
        }

        /// <summary>
        /// Opens the offer, and goes straight back into the run if it pays.
        ///
        /// Straight back in, rather than returning to a panel that has quietly grown a
        /// retry button, because returning to the run is what the player agreed to watch a
        /// video for. Making them find one more button afterwards is a tax on the thing we
        /// just persuaded them to do.
        /// </summary>
        void OfferHearts()
        {
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.HeartRefill;
                v.Rewarded = () => Close(() => { if (Screen) Screen.RetryAfterDefeat(); });
            });
        }

        /// <summary>
        /// How close the run came, in the strongest form the evidence supports.
        ///
        /// <para>
        /// One case now, and whether it appears is decided entirely by
        /// <see cref="RunOutcome"/> rather than by anything read off a board that has since
        /// been restarted. When the run ended within one or two turns of finishing, the panel
        /// says so in words and arrives with a flourish — the board has just pulsed the
        /// conduits in question, so this is the caption to a thing the player watched, not a
        /// claim they have to take on trust. Otherwise nothing is drawn.
        /// </para>
        /// <para>
        /// There used to be a fallback: a bare <c>lit/total</c> critter count when the run was
        /// not close. It is gone. It was honest but flat, and sitting directly above a row of
        /// five hearts it read as a <em>heart</em> count — "0/5" on a five-critter glade
        /// against a five-heart cap — which is the one misreading a defeat panel cannot
        /// afford, because a player who believes they are out of hearts stops playing. The
        /// timeout ending did not introduce that, it made it common: a run that runs out of
        /// clock has usually lit nothing, so the flat case became the usual one and it almost
        /// always said zero.
        /// </para>
        /// <para>
        /// The near-miss line is the single highest-value sentence on this panel. A defeat
        /// that reads as nearly a win is retried; one that reads as a wall is not. It earns
        /// that only by being true, which is why it is gated on an upper bound the player
        /// could check by restarting — see <see cref="Puzzle.TurnsToSolution"/>.
        /// </para>
        /// </summary>
        void BuildHowClose()
        {
            if (Run.NearMiss)
            {
                int index = Mathf.Clamp(Run.TurnsShort - 1, 0, NearMissKeys.Length - 1);

                var line = UIKit.Titled("Close", Panel, Loc.Get(NearMissKeys[index]), 46, Pal.Gold,
                                        TextAnchor.MiddleCenter, new Vector2(720f, 74f),
                                        new Vector2(.5f, 1f), new Vector2(0f, -300f),
                                        outline: 3f, shadow: 3f);

                // Landed rather than present. The panel springs in over half a second, so a
                // line that is simply there from the first frame is read as part of the
                // furniture; one that arrives after everything has settled is read as news.
                line.transform.localScale = Vector3.zero;

                new Cue(this)
                    .Then(.42f, () =>
                    {
                        if (!line) return;
                        Tween.Pop(line.transform, 0f, .5f);
                        Audio.Sfx("star", .6f, 1.3f);
                        Burst.Sparks(line.transform, Vector2.zero, Pal.Gold, 12, 220f, 20f, .55f);
                    });
            }
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
        ///
        /// <para>
        /// The row is <see cref="HeartRules.RefillCap"/> wide and stays that width however
        /// many hearts are held, because it is a picture of the gate rather than of the
        /// balance — fifty icons would be a wall, and the fifth is where the timer stops
        /// regardless. A surplus collected from chests, streaks or videos is drawn as a
        /// "+n" beside the row: still visible, still honest, and it does not turn a panel
        /// about a lost run into a shelf of trophies.
        /// </para>
        /// </summary>
        void BuildHearts()
        {
            var row = UIKit.Node("Hearts", Panel);
            row.anchorMin = row.anchorMax = new Vector2(.5f, 1f);
            row.pivot = new Vector2(.5f, .5f);
            row.sizeDelta = new Vector2(600f, 120f);
            row.anchoredPosition = new Vector2(0f, -400f);

            const float step = 96f;
            float left = -(HeartRules.RefillCap - 1) * step * .5f;

            int drawn = HeartsLeft > HeartRules.RefillCap ? HeartRules.RefillCap : HeartsLeft;
            int surplus = HeartsLeft - drawn;

            if (surplus > 0)
                UIKit.Titled("Surplus", row, $"+{surplus}", 40, Pal.Rose, TextAnchor.MiddleLeft,
                             new Vector2(120f, 60f), new Vector2(.5f, .5f),
                             new Vector2(left + HeartRules.RefillCap * step - 24f, 0f), 3f, 3f);

            for (int k = 0; k < HeartRules.RefillCap; k++)
            {
                bool held = k < drawn;

                // The struck-through heart is only drawn when the loss actually shows in
                // the row. A player who was over the cap still paid, but the picture would
                // be a lie: nothing in the five went out.
                bool justLost = HeartWasCharged && surplus == 0 && k == drawn;

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
    /// There is still no "buy hearts" button, and that is deliberate: the store secrets
    /// hold UNSET, so an offer here would be a button that cannot work. There is now a
    /// <em>watch</em> button, which is a different thing entirely — it costs the player
    /// attention rather than money, needs no store product to exist, and is shown only
    /// when an ad is actually loaded and the day's allowance has room. When a purchase
    /// exists it goes beside it, not instead of it.
    /// </summary>
    public sealed class OutOfHeartsOverlay : ModalView
    {
        Text _countdown;
        bool _offering;
        Btn _watch;

        protected override void Build()
        {
            // Resolved once, at the top, because it decides the panel's height as well as
            // its buttons — and asking twice risks the two disagreeing if fill arrives in
            // between, leaving a button drawn outside the panel it belongs to.
            _offering = RewardedAds.ShouldOffer(AdPlacement.HeartRefill);

            MakePanel(new Vector2(860f, _offering ? 900f : 780f), Loc.Get("ui.hearts.empty"));

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

            if (_offering)
            {
                _watch = UIKit.TextButton("WatchAd", Panel, "btn_green", Loc.Get("ui.ads.hearts_cta"), 44,
                                          new Vector2(560f, 136f), new Vector2(.5f, 1f), new Vector2(0f, -616f),
                                          () => Flow.Modal<AdOfferOverlay>(v =>
                                          {
                                              v.PlacementId = AdPlacement.HeartRefill;

                                              // Closing on reward rather than repainting: this
                                              // panel exists to explain an empty heart bar, and
                                              // once it is no longer empty it has nothing to say.
                                              v.Rewarded = () => Close();
                                          }), "ic_play");

                UIKit.TextButton("Ok", Panel, "btn_blue", Loc.Get("ui.common.got_it"), 44,
                                 new Vector2(560f, 120f), new Vector2(.5f, 1f), new Vector2(0f, -744f),
                                 () => Close());
            }
            else
            {
                UIKit.TextButton("Ok", Panel, "btn_green", Loc.Get("ui.common.got_it"), 48,
                                 new Vector2(560f, 136f), new Vector2(.5f, 1f), new Vector2(0f, -630f),
                                 () => Close());
            }
        }

        void Update() => Paint();

        void Paint()
        {
            // The offer's own cooldown runs independently of the heart clock, so it is
            // repainted here too rather than only when the panel is built.
            AdOfferButton.Paint(_watch, AdPlacement.HeartRefill, "ui.ads.hearts_cta");

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
        public RunScreen Screen;

        /// <summary>
        /// Set by the three exits that hand the run straight to something which latches the
        /// board again — restarting it and the two ways of walking away from it, all of which
        /// go through <c>PlayScreen.ConfirmForfeit</c> and want it kept frozen behind the
        /// question that follows.
        ///
        /// <para>
        /// Everything else lets go of the latch on the way out, and it is
        /// <see cref="OnDestroy"/> that does the letting go rather than the buttons, because
        /// this panel has five ways out and only four of them are buttons. The fifth is the
        /// scrim, which closes through <see cref="ModalView.Close"/> with no continuation of
        /// its own — so a tap outside the panel dismissed the pause menu and left the board
        /// latched, the clock stopped and nothing on screen able to release either. Exactly
        /// the lesson <c>AdOfferOverlay.Dismissed</c> is written from: a panel with five exits
        /// reports through none of them reliably, so the safe outcome has to be the default
        /// and the exception has to be the thing somebody declares.
        /// </para>
        /// </summary>
        bool _handedOn;

        protected override void Build()
        {
            MakePanel(new Vector2(840f, 1040f), Loc.Get("ui.pause.title"));

            UIKit.TextButton("Resume", Panel, "btn_green", Loc.Get("ui.pause.resume"), 52, new Vector2(600f, 140f),
                             new Vector2(.5f, 1f), new Vector2(0f, -230f), Resume);
            UIKit.TextButton("Restart", Panel, "btn_orange", Loc.Get("ui.pause.restart"), 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -390f),
                             () => { _handedOn = true; Close(() => Screen?.RestartLevel()); });

            // Both exits go through the screen rather than straight at Flow, because leaving a
            // run that has begun costs a heart and the screen is what knows whether this one
            // has. Navigating from here directly was the third of five ways to walk away from a
            // countdown for free — see RunGuard.
            UIKit.TextButton("Glades", Panel, "btn_blue", Loc.Get("ui.pause.glades"), 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -535f),
                             () =>
                             {
                                 _handedOn = true;
                                 Close(() =>
                                 {
                                     if (Screen) Screen.LeaveToMap();
                                     else Flow.Go<LevelsScreen>();
                                 });
                             });
            UIKit.TextButton("Home", Panel, "btn_red", Loc.Get("ui.pause.home"), 48, new Vector2(600f, 130f),
                             new Vector2(.5f, 1f), new Vector2(0f, -680f),
                             () =>
                             {
                                 _handedOn = true;
                                 Close(() =>
                                 {
                                     if (Screen) Screen.LeaveToHome();
                                     else Flow.Go<HomeScreen>();
                                 });
                             });

            var row = UIKit.Box("Toggles", Panel, new Vector2(600f, 150f), new Vector2(.5f, 0f), new Vector2(0f, 128f));
            Toggle(row, "ic_music", new Vector2(-150f, 0f), () => GameSettings.MusicOn, GameSettings.SetMusic);
            Toggle(row, "ic_audio", new Vector2(0f, 0f), () => GameSettings.SfxOn, on => { GameSettings.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });
            Toggle(row, "ic_gear", new Vector2(150f, 0f), () => GameSettings.HapticsOn, GameSettings.SetHaptics);

            UIKit.Titled("Hint", Panel, Loc.Get("ui.settings.toggle_row"), 26, new Color(.42f, .30f, .22f, .8f),
                         TextAnchor.MiddleCenter, new Vector2(600f, 40f), new Vector2(.5f, 0f), new Vector2(0f, 46f), 0f, 0f);
        }

        // Only the way out. Handing the board back is OnDestroy's job, so that every way
        // out gets it and not just this one.
        void Resume() => Close();

        /// <summary>
        /// The board comes off its latch however this panel went away — the resume button, the
        /// hardware back key, a tap on the scrim, or the screen underneath being torn down with
        /// the menu still open.
        ///
        /// Deliberately unconditional apart from the hand-off flag: forgetting to declare a
        /// hand-off costs a board that thaws a moment before the question over it is answered,
        /// while forgetting to unlatch costs the player the run.
        /// </summary>
        void OnDestroy()
        {
            if (_handedOn) return;
            if (Screen) Screen.Resume();
        }

        public override bool OnBack() { Resume(); return true; }
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
            // 700 rather than 800: the reset button and the gap above it were 146px of the
            // old height. Everything else here hangs off the top edge and only Close hangs
            // off the bottom, so shrinking the panel is what closes the hole — moving Close
            // up instead would have left the panel the same size with dead space in it.
            // Only players whose jurisdiction requires an ongoing privacy control get the
            // row, and the CMP is what decides that — not a locale, not a guess. Drawing it
            // for everybody would put a button in front of people it does nothing for;
            // hiding it from somebody in the EEA who consented is a compliance failure,
            // because withdrawing has to be as easy as agreeing. So the panel is measured
            // to the state it is in, exactly as the account panel is.
            bool privacy = AdPrivacy.CanRevisit;

            MakePanel(new Vector2(860f, privacy ? 830f : 700f), Loc.Get("ui.settings.title"));

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

            if (privacy)
            {
                // Reopens the consent form. Deliberately not a toggle of our own: the answer
                // has to be recorded by the CMP in the form the ad networks read, so a switch
                // here would either lie about what it changed or need us to write a consent
                // string we do not own. The panel closes first, because the form is a native
                // dialog and stacking one over a Unity modal leaves the modal drawn behind it
                // for as long as it is up.
                UIKit.TextButton("Privacy", Panel, "btn_blue", Loc.Get("ui.settings.privacy"), 40,
                                 new Vector2(600f, 116f), new Vector2(.5f, 1f), new Vector2(0f, -560f),
                                 () => Close(() => _ = AdPrivacy.RevisitAsync()));
            }

            // Account lives on the profile screen, not here. It is the one part of
            // settings that is about *who the player is* rather than how the game
            // behaves, and burying the thing that protects a grove three taps deep in
            // a preferences panel is how it stayed unfound.
            //
            // There is deliberately no "reset progress" here, and the reason is stronger
            // than distaste for the button. It called SaveService.Wipe, which keeps the
            // cloud identity on purpose — so on a signed-in device the next sync pulled the
            // old save straight back, because SaveMerge.Join is monotonic and the cloud copy
            // knows more than a freshly zeroed one about every field it joins. The control
            // therefore promised something it could no longer deliver: the grove vanished,
            // the player believed it, and then it came back. That is worse than not offering
            // it. Wiping a device is `adb shell pm clear` or a reinstall, and starting a
            // genuinely new grove is what linking a different account already does.
            //
            // Wipe itself stays. CloudSaveService.AdoptLinkedAccountAsync needs it, and it
            // is safe there for exactly the reason it was unsafe here: it is followed by a
            // sign-in to a *different* uid, so there is no old cloud document to merge back.
            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46, new Vector2(560f, 132f),
                             new Vector2(.5f, 0f), new Vector2(0f, 108f), () => Close());
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
