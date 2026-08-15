using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
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

        /// <summary>The run that was lost, decided by the screen. See <see cref="RunOutcome"/>.</summary>
        public RunOutcome Run { get; set; }

        /// <summary>What the streak did. Shown here too — see <see cref="StreakToast.Show"/>.</summary>
        public StreakNote Streak { get; set; }

        /// <summary>False when the player was already at zero — then nothing was taken.</summary>
        public bool HeartWasCharged;

        Btn _watch;
        Text _watchLabel;

        /// <summary>
        /// Keeps the offer button's countdown live while the panel is open.
        ///
        /// A defeat panel is somewhere players sit for a while, deciding. A cooldown that
        /// only updated when the screen was reopened would tick down invisibly and the
        /// button would stay stale until they gave up on it.
        /// </summary>
        void Update() => PaintWatch();

        void PaintWatch()
            => AdOfferButton.Paint(_watch, _watchLabel, AdPlacement.HeartRefill, "ui.ads.hearts_cta");


        /// <summary>
        /// Written out rather than built from the enum name, so the loc gate can see
        /// every key. A concatenated key is invisible to the scanner and ships missing.
        /// </summary>
        static string TitleKey(DefeatReason reason)
            => reason == DefeatReason.ConduitLost ? "ui.defeat.conduit_title" : "ui.defeat.moves_title";

        static string ReasonKey(DefeatReason reason)
            => reason == DefeatReason.ConduitLost ? "ui.defeat.conduit_reason" : "ui.defeat.moves_reason";

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
                                          OfferHearts);
                _watchLabel = _watch.GetComponentInChildren<Text>();
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

            Audio.Sfx("nope", .5f, .8f, .05f);

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
        /// Three cases, and which one appears is decided entirely by
        /// <see cref="RunOutcome"/> rather than by anything read off a board that has since
        /// been restarted. When the turns ran out within one or two of finishing, the panel
        /// says so in words and arrives with a flourish — the board has just pulsed the
        /// conduits in question, so this is the caption to a thing the player watched, not
        /// a claim they have to take on trust. Otherwise the lamp count stands in, which
        /// is honest but flat. After a crumble neither is drawn: the player saw the cause,
        /// and the distance is unknowable anyway because the conduit that broke took its
        /// own owed turns out of the count with it.
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

                return;
            }

            // No number worth quoting after a crumble — see the summary.
            if (Run.Reason != DefeatReason.OutOfMoves || Run.LampCount <= 0) return;

            UIKit.Titled("Score", Panel, $"{Run.LampsLit}/{Run.LampCount}", 44,
                         Run.LampsLit >= Run.LampCount - 1 ? Pal.Gold : Pal.Cream,
                         TextAnchor.MiddleCenter, new Vector2(400f, 70f),
                         new Vector2(.5f, 1f), new Vector2(0f, -300f), outline: 3f, shadow: 3f);
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
        Text _watchLabel;

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
                                          }));
                _watchLabel = _watch.GetComponentInChildren<Text>();

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
            AdOfferButton.Paint(_watch, _watchLabel, AdPlacement.HeartRefill, "ui.ads.hearts_cta");

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
    /// <summary>
    /// The payoff.
    ///
    /// <para>
    /// This panel is where the game either pays a player back for the last few minutes or
    /// does not, and almost all of that is in the <em>timing</em>. Information arriving
    /// all at once is information; the same information arriving in an order, each piece
    /// a beat after the last, is a reward — because at every moment there is still
    /// something about to happen. That is the whole design, and it is why the sequence is
    /// declared as a <see cref="Cue"/> rather than as a scatter of absolute delays: the
    /// beats are relative, so inserting one cannot silently shove two others onto the same
    /// frame. They had been. The rank and the reward were computed from two different
    /// formulae over the star count and collided on a three-star win.
    /// </para>
    /// <para>
    /// The reward line counts up rather than appearing finished, for the same reason.
    /// See <see cref="Roll"/>.
    /// </para>
    /// </summary>
    public sealed class WinOverlay : ModalView
    {
        /// <summary>The run that was won, decided by the screen. See <see cref="RunOutcome"/>.</summary>
        public RunOutcome Run { get; set; }

        /// <summary>What the streak did. See <see cref="StreakToast"/>.</summary>
        public StreakNote Streak { get; set; }

        /// <summary>What this run added, already folded into the ledger. Zero on a worse replay.</summary>
        public long XpGained, CreditsGained;

        /// <summary>
        /// This glade's golden multiplier, as a percentage. 100 is the ordinary reward.
        /// Already inside <see cref="CreditsGained"/> — see <see cref="GoldenTable"/>.
        /// </summary>
        public int GoldenPercent = 100;

        /// <summary>
        /// Written out rather than assembled as "ui.win.rank" + stars, so the keys are
        /// visible to the build's string checker. A key that only exists at runtime is
        /// a key nothing can verify.
        /// </summary>
        internal static readonly string[] RankKeys = { "ui.win.rank1", "ui.win.rank2", "ui.win.rank3" };

        /// <summary>Seconds between one star landing and the next.</summary>
        const float StarGap = .42f;

        /// <summary>Where the payout row sits, and how much of the first chip the second waits for.</summary>
        const float RewardY = -496f, PayoutOverlap = .45f;

        /// <summary>
        /// True when this run completed the last uncleared glade of its chapter. Derived
        /// from the catalog rather than counted, so it stays correct when a chapter gains
        /// levels in a content drop.
        /// </summary>
        bool FinishedAChapter(CatalogIndex index)
        {
            var chapter = index.ChapterOf(Run.Level);
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
            var next = index.Next(Run.Level);
            bool last = !next.IsValid;

            int stars = Mathf.Clamp(Run.Stars, 1, 3);

            // Both decided before anything is built, because between them they set the
            // panel's height. Asking later and growing it afterwards is how a line ends up
            // drawn a few pixels outside the panel it belongs to — which looks fine on the
            // device it was tuned on.
            bool paid = XpGained > 0 || CreditsGained > 0;
            bool goldened = paid && GoldenPercent > 100;

            // The player's own record after this run, against everybody else's. Decided
            // here with the other two because it also grows the panel — see below.
            int record = Run.NewBest ? Run.Moves
                       : Run.PreviousBest > 0 ? Mathf.Min(Run.Moves, Run.PreviousBest)
                       : Run.Moves;
            var population = GroveStats.For(Run.Level);
            bool ranked = population.IsWorthSaying(record);

            UIKit.Scrim(Content, .62f);

            // ------------------------------------------------------ the furniture
            // Everything is built here and animated below, so that the cue sheet reads as
            // the sequence the player experiences rather than as a construction order.
            var banner = UIKit.Img("Victory", Content, null, Color.white,
                                   new Vector2(600f, 420f), new Vector2(.5f, .5f), new Vector2(0f, 470f));
            banner.preserveAspect = true;
            Flipbook.Attach(banner, "Fx/Victory", 34f, false);
            banner.transform.localScale = Vector3.one * .9f;

            // Grown rather than crowded when there is a golden line to fit. The buttons
            // are anchored to the panel's bottom edge and the readouts to its top, so the
            // extra height appears exactly where the new line goes and nothing else moves.
            Backing = UIKit.Img("Panel", Content, Art.S("Ui/panel_main"), Color.white,
                                new Vector2(880f, 820f + (paid ? 56f : 0f) + (goldened ? 76f : 0f)
                                                       + (ranked ? 44f : 0f)),
                                new Vector2(.5f, .5f), new Vector2(0f, -140f));
            Panel = (RectTransform)Backing.transform;
            Panel.localScale = Vector3.zero;

            var starRow = StarRow.Create(Panel, new Vector2(.5f, 1f), new Vector2(0f, -135f), 144f, 152f, 0, true);

            var rank = UIKit.Titled("Rank", Panel, Loc.Get(RankKeys[stars - 1]), 62, Pal.Rose,
                                    TextAnchor.MiddleCenter, new Vector2(700f, 80f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -285f), 4f, 4f);
            rank.transform.localScale = Vector3.zero;

            UIKit.Titled("Moves", Panel, Loc.Format("ui.win.moves", Run.Moves, Run.Target), 36,
                         new Color(.40f, .28f, .20f), TextAnchor.MiddleCenter, new Vector2(760f, 50f),
                         new Vector2(.5f, 1f), new Vector2(0f, -368f), 0f, 2f);

            // A record beaten is a result in its own right, and one a replayer may have
            // come back specifically for. It is drawn in the same slot either way, but
            // only the new one is worth a colour and an entrance.
            var best = UIKit.Titled("Best", Panel,
                                    Run.NewBest ? Loc.Get("ui.win.new_best")
                                                : Loc.Format("ui.win.best_is", Run.PreviousBest),
                                    30,
                                    Run.NewBest ? Pal.Gold : new Color(.52f, .38f, .28f, .9f),
                                    TextAnchor.MiddleCenter, new Vector2(760f, 44f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -416f), 0f, 0f);

            // The payout. Two chips rather than one sentence, and each number is put there
            // by things the player watches leave the stars — see Payout for why the flight
            // is what makes it land rather than being decoration over a rolling label.
            //
            // Built only when the run actually improved the record. A replay that beat
            // nothing earns nothing, and a chip reading "+0 XP" would look like a bug.
            Payout xpChip = null, coinChip = null;
            if (paid)
            {
                float spread = XpGained > 0 && CreditsGained > 0 ? 188f : 0f;

                if (XpGained > 0)
                {
                    // A mint gem, and mint because that is what experience already is
                    // everywhere else here — the bar on the hub and the one on the profile
                    // card. Art.Gem carries its own colours, so like every other reward glyph
                    // it is drawn white rather than tinted, and being generated it cannot
                    // arrive a frame late as a white rectangle (invariant 7b).
                    xpChip = Payout.Chip("Xp", Panel, new Vector2(.5f, 1f), new Vector2(-spread, RewardY),
                                         Art.Gem(128, Pal.Mint), Pal.Mint,
                                         n => Loc.Format("ui.win.xp", n), XpGained,
                                         Art.Gem(64, Pal.Mint), Color.white, "tick");
                }

                if (CreditsGained > 0)
                {
                    // Credits are the spinning coin, which has no single sprite — the glyph
                    // is finished by RewardArt, fallback and all. The tokens use the coin's
                    // first frame flat, untinted: gold art washed in gold stops reading as a
                    // coin, and the disc fallback is the only case that wants a colour.
                    var frames = Art.Frames("Ui/Coin");
                    bool minted = frames != null && frames.Length > 0;

                    coinChip = Payout.Chip("Coins", Panel, new Vector2(.5f, 1f), new Vector2(spread, RewardY),
                                           null, Pal.Gold, n => Loc.Format("ui.win.coins", n), CreditsGained,
                                           minted ? frames[0] : Art.Disc(128),
                                           minted ? Color.white : Pal.Gold, "pop");
                    RewardArt.Glyph(coinChip.Glyph, ChestDropKind.Credits, 14f);
                }

                if (xpChip != null) xpChip.Root.localScale = Vector3.zero;
                if (coinChip != null) coinChip.Root.localScale = Vector3.zero;
            }

            // A glade that paid more than it should have, said out loud. Built here with the
            // rest of the furniture because its beat belongs to the payout's lane rather
            // than to the main sequence — see below.
            var goldenLine = goldened
                ? UIKit.Titled("Golden", Panel, Loc.Format("ui.win.golden", GoldenPercent), 40, Pal.Gold,
                               TextAnchor.MiddleCenter, new Vector2(760f, 54f),
                               new Vector2(.5f, 1f), new Vector2(0f, RewardY - 82f), 3f, 3f)
                : null;
            if (goldenLine) goldenLine.transform.localScale = Vector3.zero;

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

            var replayId = Run.Level;
            UIKit.IconButton("Replay", Panel, "sq_orange", "ic_restart", new Vector2(120f, 120f),
                             new Vector2(.5f, 0f), new Vector2(-232f, 96f),
                             () => Close(() => Flow.Go<PlayScreen>(v => v.LevelId = replayId)));
            UIKit.IconButton("Map", Panel, "sq_blue", "ic_list", new Vector2(120f, 120f),
                             new Vector2(.5f, 0f), new Vector2(232f, 96f),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            // ------------------------------------------------------- the sequence
            // Read this top to bottom: it is the order the player sees, and every number
            // is a gap after the line above rather than a time from the start.
            var cue = new Cue(this);

            cue.With(() => Tween.Scale(banner.transform, 1f, .8f, Ease.OutBack));

            // The panel arrives under a banner that is still settling, which is what makes
            // the two read as one movement rather than two.
            cue.Then(.45f, () => Tween.Scale(Panel, 1f, .55f, Ease.OutBack));

            // Stars land one at a time, each a semitone above the last. The row schedules
            // its own beats, so the playhead is walked over them by hand.
            cue.Then(.50f, () => starRow.Reveal(Run.Stars, 0f, StarGap));

            // ------------------------------------------------------ the payout lane
            // The payout is a second lane, opened at the same instant as the stars rather
            // than queued behind them.
            //
            // <para>
            // It used to run after the rank and the record, which put a gap of three or four
            // seconds between a player landing their last star and seeing what it was worth —
            // long enough that the reward read as a separate announcement instead of as the
            // consequence of the thing they had just watched. The two belong together: the
            // reward genuinely is a function of exactly those stars (see ProgressionLedger),
            // and the tokens are thrown out of the star row to say so. Firing the first
            // handful while the row is still filling is what makes that claim legible.
            // </para>
            // <para>
            // A lane rather than more beats on the main one, because the sequences overlap
            // and a single playhead cannot express that — walking it forward over the payout
            // would push the rank and the record behind it, which is the problem in reverse.
            // The golden line moves in here too: it has to precede the coins, and the coins
            // no longer wait for the main lane.
            // </para>
            // <para>
            // The chips arrive <em>empty</em>, and that is the trick worth keeping. A prize
            // revealed finished is information; a container already on screen with nothing in
            // it is a question, and every token that lands is part of the answer.
            // </para>
            float payoutEnds = cue.Playhead;
            if (paid)
            {
                var payout = new Cue(this, cue.Playhead);
                float lastStart = payout.Playhead, lastRuns = 0f;

                payout.With(() =>
                {
                    if (xpChip != null) Tween.Pop(xpChip.Root, .4f, .44f);
                    if (coinChip != null) Tween.Pop(coinChip.Root, .4f, .44f, .09f);
                    Audio.Sfx("whoosh", .32f, 1.2f);
                });

                if (xpChip != null)
                {
                    // Timed so the first sparks leave as the first star lands, not before it:
                    // tokens thrown out of an empty row would be coming from nothing.
                    payout.Then(StarGap * .8f, () => xpChip.Play(starRow.transform));
                    lastStart = payout.Playhead; lastRuns = xpChip.Duration;

                    // Held for a fraction of the first chip rather than all of it: the coins
                    // leave while the sparks are still landing, so the two read as one
                    // cascade. Played end to end they read as a list being recited.
                    if (coinChip != null) payout.Wait(xpChip.Duration * PayoutOverlap);
                }

                // Lands before the coins so the number the player then watches climb is
                // already explained — "something special happened", then the evidence, which
                // is the way round that makes the count-up a consequence and not a
                // coincidence. Only ever on a paid run: announcing a multiplier on top of no
                // credits would read as the game owing the player money.
                if (goldenLine)
                {
                    payout.Then(xpChip != null ? 0f : StarGap * .8f, () =>
                    {
                        if (!goldenLine) return;
                        Tween.Pop(goldenLine.transform, 0f, .55f);
                        Tween.Breathe(goldenLine.transform, .035f, 1.8f);
                        Audio.Sfx("chime2", .7f, 1.18f);
                        Burst.Sparks(goldenLine.transform, Vector2.zero, Pal.Gold, 18, 300f, 24f, .7f);
                        Flow.Flash(new Color(1f, .93f, .70f), .3f, .5f);
                    });
                    payout.Wait(.28f);
                }

                if (coinChip != null)
                {
                    payout.Then(0f, () => coinChip.Play(starRow.transform));
                    lastStart = payout.Playhead; lastRuns = coinChip.Duration;
                }

                payoutEnds = lastStart + lastRuns;
            }

            // ------------------------------------------------------- the main lane
            cue.Wait(StarGap * stars);

            cue.Then(.34f, () =>
            {
                Tween.Pop(rank.transform, 0f, .6f);
                Audio.Sfx("chime", .55f, 1f + .08f * stars);
            });

            if (Run.NewBest)
            {
                cue.Then(.26f, () =>
                {
                    if (!best) return;
                    Tween.Punch(best.transform, .34f, .45f);
                    Audio.Sfx("star", .5f, 1.42f);
                });
            }

            // The two lanes rejoin here. Everything below is either news in its own right or
            // a call to leave, and none of it should arrive while coins are still in the air
            // — least of all the shine on the Next button, which exists to ask for attention
            // once there is nothing left worth watching.
            cue.Wait(Mathf.Max(0f, payoutEnds - cue.Playhead));

            // A new glade opening is the strongest single line on this panel, so it lands
            // after the run has finished being scored rather than over the top of it.
            if (Run.FirstClear && !last)
            {
                string opened = Loc.Format("ui.win.opened", Loc.Get(LevelDefinition.DefaultNameKey(next)));
                cue.Then(.30f, () =>
                {
                    Audio.Sfx("unlock", .6f);
                    Scenery.Toast(Content, opened, Pal.Gold, 2.2f, new Vector2(.5f, 0f), 190f);
                });
            }

            // How the player did against everybody else, when there is a population large
            // enough to say and the answer is one worth hearing.
            //
            // Drawn upward only, and that is a decision rather than an oversight — see
            // LevelStats.IsWorthSaying. Told they are ahead, a player plays more; told they
            // are behind, a good share stop, and the ones who stop are disproportionately
            // the ones who were already struggling. Every word of it is true either way;
            // this is a choice about which true things belong on a victory screen.
            if (ranked)
            {
                var line = UIKit.Titled("Rank%", Panel,
                                        Loc.Format("ui.win.faster_than", population.PercentSlower(record)),
                                        26, new Color(.44f, .32f, .24f, .9f), TextAnchor.MiddleCenter,
                                        new Vector2(760f, 34f), new Vector2(.5f, 1f),
                                        new Vector2(0f, goldened ? RewardY - 132f
                                                     : paid ? RewardY - 66f : -506f), 0f, 0f);
                line.transform.localScale = Vector3.zero;

                cue.Then(.22f, () =>
                {
                    if (!line) return;
                    Tween.Pop(line.transform, 0f, .45f);
                });
            }

            // The streak, when this run is what moved it. After the glade unlock, because
            // an unlock is about the game and a streak is about the player, and the player
            // should be the last thing said.
            cue.Then(.30f, () => StreakToast.Show(this, Streak, 0f));
            if (Streak.WorthSaying) cue.Wait(.45f);

            // Last, and deliberately so: the button starts asking for attention only once
            // there is nothing left worth watching. Shining it during the celebration
            // teaches players to skip the celebration.
            cue.Then(.35f, () =>
            {
                if (!nextButton) return;
                Sheen.Attach((RectTransform)nextButton.transform, 3.2f);
                Tween.Breathe(nextButton.transform, .025f, 2f);
            });
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
