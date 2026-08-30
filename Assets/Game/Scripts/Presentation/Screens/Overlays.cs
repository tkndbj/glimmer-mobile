using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Privacy;
using GlimmerGrove.Progression;
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
                // Silent. This lands on top of whichever panel ended the run, a beat after
                // that panel has already had its say - so its chime arrived as an extra
                // ending on a screen that had finished. The toast slides in and is read.

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

        /// <summary>
        /// The dimmed sheet behind the panel.
        ///
        /// Kept rather than discarded because a panel that pays a reward into the screen
        /// underneath has to take it away — a token cannot be seen landing on a readout the
        /// player cannot see, and a scrim faded to nothing still swallows the taps aimed at
        /// what is now visible through it. See <c>AdOfferOverlay</c>'s collect.
        /// </summary>
        protected Image Scrim;

        bool _closing;

        /// <summary>
        /// True from the first frame of the exit animation. See <see cref="View.IsLeaving"/>
        /// for what reads it and why a closing panel must not block its own successor.
        /// </summary>
        public override bool IsLeaving => _closing;

        protected RectTransform MakePanel(Vector2 size, string title, Vector2 offset = default,
                                          bool dismissOnScrim = true)
        {
            Scrim = UIKit.Scrim(Content, .72f, dismissOnScrim ? (Action)(() => Close()) : null);

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

                // One sound for one tap. The button that opened this panel spoke on pointer
                // down and this fires on pointer up, so without the hush a menu arrives as
                // two noises a tenth of a second apart — reported as exactly that. See
                // Audio.Hush for why the rule lives here rather than on every button.
                Audio.Hush("click");
                Audio.Sfx("menu", .55f);
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

        /// <summary>
        /// What the run was priced at, and if it cost nothing, why — one of a mode's free
        /// openings, or a glade this player had already finished.
        ///
        /// <para>
        /// Distinct from <see cref="HeartWasCharged"/> being false, which is the opposite
        /// news: that one means there was nothing left to take. Told apart because the panel
        /// says opposite things about them — a free run always offers another go, and an empty
        /// wallet cannot. See <c>HeartStake</c>.
        /// </para>
        /// <para>
        /// The reason is carried rather than the bare fact because the panel prints a sentence
        /// about it and the two sentences are not interchangeable: "one of the free levels" over
        /// the fortieth glade of a chapter reads as a bug, and "you have already finished this
        /// one" over somebody's second board is a fact they have no way of knowing yet.
        /// </para>
        /// </summary>
        public HeartPrice Price;

        /// <summary>Whether anything was owed for the run at all, whichever clause said so.</summary>
        bool WasFree => Price != HeartPrice.Charged;

        /// <summary>
        /// The most heart icons this panel will ever draw in a row.
        ///
        /// <para>
        /// The row is 600 units across at a 96-unit step, so six is what fits and five is what
        /// the free gate has always been. It exists as a bound because the cap stopped being a
        /// single number when heart containers shipped: <c>Wallet.MaxHearts</c> is per player
        /// and can be fifty, and a row that grew with it would draw a wall off both edges of
        /// the panel. Everything above it is the "+n" the surplus already used.
        /// </para>
        /// </summary>
        const int RowPips = 5;

        /// <summary>
        /// Hearts for a video: the free way out, and always drawn above the paid one.
        ///
        /// <para>
        /// A collaborator for <see cref="_rescue"/>'s reason and beside it — see
        /// <see cref="HeartVideoFlow"/>, which is also where the argument lives for why tapping
        /// it shows the video rather than an explanatory panel. What stays here is what only a
        /// panel can answer: where the button goes, and what "back onto the board" means.
        /// </para>
        /// <para>
        /// Built once per defeat and kept across rebuilds, exactly as the rescue is, because it
        /// holds the one piece of state a redraw must not reset: whether a video is already on
        /// its way up. This panel rebuilds on a gem balance the rescue cares about, a cloud sync
        /// can move one at any moment, and a fresh flow would hand back an armed WATCH button
        /// over a video already playing.
        /// </para>
        /// </summary>
        HeartVideoFlow _video;

        /// <summary>
        /// Hearts for gems: the third way out, and the only one that costs money.
        ///
        /// <para>
        /// A collaborator rather than more of this panel, which had already reached five
        /// responsibilities — <see cref="DefeatRescueFlow"/> says why, and <c>RunContinueFlow</c>
        /// is the precedent. What is left here is what only a panel can answer: whether there is
        /// still a heart to spend, where the button goes, and what "back onto the board" means.
        /// </para>
        /// <para>
        /// Built once and kept across rebuilds, which is what makes the offer's impression count
        /// once. A rebuild is the same panel in a new state, so anything decided per <em>defeat</em>
        /// rather than per <em>paint</em> has to outlive <c>Build</c>.
        /// </para>
        /// </summary>
        DefeatRescueFlow _rescue;

        /// <summary>
        /// Keeps the offer button's countdown live while the panel is open.
        ///
        /// A defeat panel is somewhere players sit for a while, deciding. A cooldown that
        /// only updated when the screen was reopened would tick down invisibly and the
        /// button would stay stale until they gave up on it.
        /// </summary>
        void Update() => _video?.Paint();


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
                // DefeatReason.OutOfInk is retired with Lightweave and deliberately absent: the
                // ordinal stays so every defeat row ever written keeps meaning what it meant,
                // and a mode nothing can play never reaches here.
                case DefeatReason.WellFlooded: return "ui.defeat.flood_title";
                case DefeatReason.OutOfMotes: return "ui.defeat.motes_title";
                case DefeatReason.Overgrown: return "ui.defeat.overgrown_title";
                case DefeatReason.OutOfTiles: return "ui.defeat.tiles_title";
                case DefeatReason.OutOfTaps: return "ui.defeat.taps_title";
                case DefeatReason.Barren: return "ui.defeat.barren_title";
                default: return "ui.defeat.moves_title";
            }
        }

        /// <summary>
        /// Which sentence explains a run that cost nothing.
        ///
        /// Written out rather than built from the enum name, for <see cref="TitleKey"/>'s
        /// reason: a concatenated key is invisible to the build's string scanner and ships
        /// missing in whichever language nobody tested. <see cref="HeartPrice.Charged"/> cannot
        /// reach here — the caller asks only when the run was free — but it answers the free
        /// opening rather than throwing, because the worst a wrong sentence does on this panel
        /// is read oddly, and the worst an exception does is eat the defeat screen.
        /// </summary>
        static string FreeKey(HeartPrice price)
            => price == HeartPrice.Replay ? "ui.defeat.free_replay" : "ui.defeat.free_glade";

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
            // A free opening can always be tried again, whatever the wallet says. That is the
            // whole point of it, and it is why this is not simply a heart count.
            bool canRetry = WasFree || HeartsLeft > 0;

            // Neither offer belongs on the branch that has a retry button. A player who can
            // still play does not need to be sold a way to play, and putting either there
            // would turn every defeat into an advertisement.
            //
            // ShouldOffer, not CanOffer: a cooldown draws the button disabled with its own
            // countdown rather than hiding it, so a player who watched one a minute ago can
            // see when the next is due instead of concluding the offer was a fluke.
            bool offering = !canRetry && RewardedAds.ShouldOffer(AdPlacement.HeartRefill);

            // Once per defeat, never per paint: it decides the offer, counts the impression and
            // subscribes to the balance, none of which a redraw may do again.
            _rescue ??= new DefeatRescueFlow(this, Run.Level, HeartsLeft, canRetry,
                                             Rebuild, BackToTheBoard);

            // Derived rather than typed, so the five shapes this panel can take cannot come to
            // disagree with the buttons drawn into them. See DefeatPanel — the two constants
            // this replaced were 880 and 1010, written before there was a third way out.
            var stack = DefeatPanel.Of(canRetry, offering, _rescue.Exists);

            MakePanel(new Vector2(DefeatPanel.Width, stack.Height),
                      Loc.Get(TitleKey(Run.Reason)), dismissOnScrim: false);

            // The sentence explaining the defeat used to sit here and no longer does. It
            // restated the title directly above it — "OUT OF TURNS" over "the groove grew tired
            // before the glade woke" — and was reported from play as noise at the one moment
            // nobody is reading prose. What is left is the title, how close it was, and what to
            // do about it. The offer panel that comes first already says why, as a subtitle
            // under DEFEAT, where it reads as a reason rather than as the subject.
            BuildHowClose();

            // Five empty hearts under a run that cost none of them is a picture of a charge
            // that did not happen, and directly above a retry button it reads as the panel
            // contradicting itself. The row is replaced by the reason instead — the reason,
            // not a reason: which of the two clauses spared this run is what the player needs
            // to know, since one of them runs out after three boards and the other never does.
            //
            // Centred in the room it actually has rather than at a typed offset, because the
            // near-miss slot above it is reserved on every defeat and filled on few — and in
            // Pal.Moss rather than Pal.Mint, which is a board colour and was being asked to
            // carry a whole sentence of unoutlined body copy across cream paper. See
            // DefeatPanel.FreeCentre and Pal.Moss; both were reported from play.
            if (WasFree)
                UIKit.Shrinkable(Body("Free", Loc.Get(FreeKey(Price)),
                                      -DefeatPanel.FreeCentre(Run.NearMiss),
                                      DefeatPanel.FreeHeight, Pal.Moss), 22);
            else BuildHearts();

            if (stack.HasRetry)
            {
                UIKit.TextButton("Retry", Panel, "btn_green", Loc.Get("ui.defeat.try_again"), 52,
                                 new Vector2(620f, DefeatPanel.RetryHeight), new Vector2(.5f, 1f),
                                 new Vector2(0f, -stack.Retry),
                                 () => Close(() => { if (Screen) Screen.RetryAfterDefeat(); }));
            }

            // Out of hearts. Which sentence depends on whether there is a way back in at all:
            // telling somebody to wait eight hours directly above a button that skips the wait
            // is how a panel reads as a trick.
            //
            // Drawn in the panel's own body colour rather than in red. Red is an alarm, and this
            // is an instruction sitting directly above the two buttons that carry it out — the
            // colour was saying "something is wrong" about the one part of the panel that is
            // actually a way forward.
            if (stack.HasNote)
                UIKit.Shrinkable(
                    Body("Wait", Loc.Get(stack.HasWatch || stack.HasRescue
                                             ? "ui.defeat.watch_for_hearts"
                                             : "ui.defeat.out_of_hearts"),
                         -stack.Note, DefeatPanel.NoteHeight), 22);

            if (stack.HasWatch)
            {
                // Straight back onto the board once the prize is taken, exactly as the rescue's
                // purchase is — and through the same method, so the two ways back cannot come to
                // mean different things. Guarded for its own lifetime because the celebration
                // outlives this panel: a player who backgrounds the app during the video can come
                // back somewhere else entirely, and the hearts are theirs either way.
                _video ??= new HeartVideoFlow(this, () => { if (this) BackToTheBoard(); });

                _video.Draw(Panel, new Vector2(620f, DefeatPanel.ActionHeight),
                            new Vector2(.5f, 1f), new Vector2(0f, -stack.Watch));
            }

            if (stack.HasRescue)
                _rescue.Draw(Panel, new Vector2(620f, DefeatPanel.ActionHeight),
                             new Vector2(.5f, 1f), new Vector2(0f, -stack.Rescue));

            UIKit.TextButton("Glades", Panel, "btn_blue", Loc.Get("ui.pause.glades"), 46,
                             new Vector2(620f, DefeatPanel.GladesHeight), new Vector2(.5f, 1f),
                             new Vector2(0f, -stack.Glades),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            // Last, and after the near-miss line has had its moment: a lost run still fed
            // the streak, which is the one piece of good news this panel has. Not replayed on
            // a repaint — the house rule is that Show animates and Refresh does not, and a
            // streak toast that flew past again because gems landed would read as a second
            // night collected.
            if (!Rebuilding) StreakToast.Show(this, Streak, 1.05f);
        }

        void OnDestroy() => _rescue?.Dispose();

        /// <summary>
        /// Closes this panel and starts a fresh attempt at the same board.
        ///
        /// <para>
        /// The panel's, not the rescue's: only a screen knows what a way back onto the board
        /// means, and the collaborator that sells one must not also be the thing that decides
        /// what was sold. Quiet, because what the player hears next is the board coming back and
        /// a backing-out whoosh underneath it is one sound too many.
        /// </para>
        /// </summary>
        void BackToTheBoard()
            => Close(() => { if (Screen) Screen.RetryAfterDefeat(); }, quiet: true);

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
                // A repaint is not news. The line still belongs on the panel, so it is drawn
                // at rest rather than skipped — what must not happen twice is the arrival.
                if (Rebuilding)
                {
                    int at = Mathf.Clamp(Run.TurnsShort - 1, 0, NearMissKeys.Length - 1);

                    UIKit.Titled("Close", Panel, Loc.Get(NearMissKeys[at]), 46, Pal.Gold,
                                 TextAnchor.MiddleCenter,
                                 new Vector2(720f, DefeatPanel.CloseHeight),
                                 new Vector2(.5f, 1f), new Vector2(0f, -DefeatPanel.CloseCentre),
                                 outline: 3f, shadow: 3f);
                    return;
                }

                int index = Mathf.Clamp(Run.TurnsShort - 1, 0, NearMissKeys.Length - 1);

                var line = UIKit.Titled("Close", Panel, Loc.Get(NearMissKeys[index]), 46, Pal.Gold,
                                        TextAnchor.MiddleCenter,
                                        new Vector2(720f, DefeatPanel.CloseHeight),
                                        new Vector2(.5f, 1f),
                                        new Vector2(0f, -DefeatPanel.CloseCentre),
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
        /// <summary>
        /// A paragraph on the panel's paper: wrapped, and with no outline or shadow. Those two
        /// are for headings sitting on a ribbon; on a 32pt sentence they smear the strokes
        /// together and it stops reading.
        ///
        /// <para>
        /// <b>Middle-aligned, because <paramref name="y"/> is a centre.</b> Every number
        /// <c>DefeatPanel</c> hands out is the centre of the room reserved for a row, and a
        /// paragraph anchored to the top of that room sits high in it by however much shorter
        /// than the room it happens to be — so the air above it and the air below it are never
        /// the two halves of one gap, and both rows drawn through here were visibly closer to
        /// what follows them than to what precedes them. Centring makes the reserved room mean
        /// what it says whatever the line count and whatever the translation.
        /// </para>
        /// </summary>
        Text Body(string name, string text, float y, float height, Color? colour = null)
            => UIKit.Titled(name, Panel, text, 32, colour ?? new Color(.36f, .25f, .18f),
                            TextAnchor.MiddleCenter, new Vector2(680f, height),
                            new Vector2(.5f, 1f), new Vector2(0f, y),
                            outline: 0f, shadow: 0f, wrap: true);

        /// <summary>
        /// The heart row, with the one just lost drawn empty and struck through by a
        /// short animation. Showing the cost is the point — a resource that quietly
        /// decrements is a resource players feel cheated by later.
        ///
        /// <para>
        /// The row is a fixed handful of icons wide and stays that width however many hearts
        /// are held, because it is a picture of the gate rather than of the balance — twenty
        /// icons would be a wall, and the row is 600 units across. A surplus, whether it came
        /// from a chest, a streak, a video or a bought container, is drawn as a "+n" beside
        /// the row: still visible, still honest, and it does not turn a panel about a lost run
        /// into a shelf of trophies.
        /// </para>
        /// <para>
        /// <see cref="RowPips"/> is a bound rather than the cap itself, and it became one when
        /// heart containers shipped: <c>Wallet.MaxHearts</c> is per player now and can be
        /// fifty, which no row of icons can draw. It is still held to the cap as well, so a
        /// content push that lowers the free tuning to three draws three.
        /// </para>
        /// </summary>
        void BuildHearts()
        {
            var row = UIKit.Node("Hearts", Panel);
            row.anchorMin = row.anchorMax = new Vector2(.5f, 1f);
            row.pivot = new Vector2(.5f, .5f);
            row.sizeDelta = new Vector2(600f, 120f);
            row.anchoredPosition = new Vector2(0f, -DefeatPanel.HeartsCentre);

            const float step = 96f;
            int pips = Wallet.MaxHearts < RowPips ? Wallet.MaxHearts : RowPips;
            if (pips < 1) pips = 1;

            float left = -(pips - 1) * step * .5f;

            int drawn = HeartsLeft > pips ? pips : HeartsLeft;
            int surplus = HeartsLeft - drawn;

            if (surplus > 0)
                UIKit.Titled("Surplus", row, $"+{surplus}", 40, Pal.Rose, TextAnchor.MiddleLeft,
                             new Vector2(120f, 60f), new Vector2(.5f, .5f),
                             new Vector2(left + pips * step - 24f, 0f), 3f, 3f);

            for (int k = 0; k < pips; k++)
            {
                bool held = k < drawn;

                // The struck-through heart is only drawn when the loss actually shows in
                // the row. A player who was over the drawn row still paid, but the picture
                // would be a lie: nothing in the icons went out.
                // Not on a repaint: the heart went out a minute ago, and draining it again
                // because gems landed is the panel reporting a charge that did not happen.
                bool justLost = HeartWasCharged && surplus == 0 && k == drawn && !Rebuilding;

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
    /// Three ways out, in the order they cost the player: a video, the shop, and away. The
    /// video is shown only when one is actually loaded and the day's allowance has room, so
    /// it is the shop that is always there — hearts sell for <em>gems</em>, which need no
    /// store connection and may already be in hand, so that button works in a build with no
    /// IAP and on a plane. Free above paid, for <c>DefeatPanel</c>'s reason.
    ///
    /// It <b>navigates</b> rather than raising a shelf, which is the opposite of what the
    /// defeat panel does and is right for the same reason: nothing is frozen behind this one.
    /// A run has not started, no heart is at stake, and the map this was raised from is one
    /// tap away again. <c>GemShopOverlay</c> exists for the case where leaving would cost
    /// something, and this is not it.
    /// </summary>
    public sealed class OutOfHeartsOverlay : ModalView
    {
        Text _countdown;
        bool _offering;

        /// <summary>
        /// The video, and what it pays into. See <see cref="HeartVideoFlow"/> — the same free
        /// way back the defeat panel draws, because a player who meets the explanatory panel in
        /// one of the two places hearts are asked for and the celebration in the other has met
        /// two features rather than one.
        /// </summary>
        HeartVideoFlow _video;

        protected override void Build()
        {
            // Resolved once, at the top, because it decides the panel's height as well as
            // its buttons — and asking twice risks the two disagreeing if fill arrives in
            // between, leaving a button drawn outside the panel it belongs to.
            _offering = RewardedAds.ShouldOffer(AdPlacement.HeartRefill);

            // Derived rather than typed, so the panel cannot come to disagree with the buttons
            // drawn into it. See HeartGatePanel — the two constants this replaced were 900 and
            // 780, written when there were two ways out rather than three.
            var stack = HeartGatePanel.Of(_offering);

            MakePanel(new Vector2(HeartGatePanel.Width, stack.Height), Loc.Get("ui.hearts.empty"));

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

            if (stack.HasWatch)
            {
                // Closing once the prize has been taken rather than repainting: this panel
                // exists to explain an empty heart bar, and once it is no longer empty it has
                // nothing left to say. Guarded because the celebration outlives it — and it
                // usually does, since Paint below closes this the moment the hearts land.
                _video = new HeartVideoFlow(this, () => { if (this) Close(); });

                _video.Draw(Panel, new Vector2(560f, HeartGatePanel.ActionHeight),
                            new Vector2(.5f, 1f), new Vector2(0f, -stack.Watch));
            }

            // Navigates rather than raising a shelf. See the class remarks: nothing is frozen
            // behind this panel, so leaving it costs nothing and the shop is the whole answer
            // rather than a corner of it.
            var shop = UIKit.TextButton("Shop", Panel, "btn_violet", Loc.Get("ui.hearts.to_shop"), 44,
                                        new Vector2(560f, HeartGatePanel.ActionHeight),
                                        new Vector2(.5f, 1f), new Vector2(0f, -stack.Shop),
                                        () => Close(() => Flow.Go<ShopScreen>()), "ic_gem");
            UIKit.OneLine(shop, 24);

            UIKit.OneLine(
                UIKit.TextButton("Ok", Panel, "btn_blue", Loc.Get("ui.common.got_it"), 44,
                                 new Vector2(560f, HeartGatePanel.OkHeight), new Vector2(.5f, 1f),
                                 new Vector2(0f, -stack.Ok), () => Close()), 24);
        }

        void Update() => Paint();

        void Paint()
        {
            // The offer's own cooldown runs independently of the heart clock, so it is
            // repainted here too rather than only when the panel is built.
            _video?.Paint();

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
            // Two, straddling the middle rather than taking the outer thirds — ReadoutRow's
            // rule, because a gap in the centre of a row of two reads as a third control that
            // failed to draw. The buzz used to be the third and is gone: see Haptic's removal.
            Toggle(row, "ic_music", new Vector2(-110f, 0f), () => GameSettings.MusicOn, GameSettings.SetMusic);
            Toggle(row, "ic_audio", new Vector2(110f, 0f), () => GameSettings.SfxOn, on => { GameSettings.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });

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

            // Derived rather than typed, for GladeRewardsOverlay's reason: this panel now has
            // two optional rows, and a height somebody has to remember to move is a height that
            // ends up drawing a paragraph through a button. Both numbers below are what the
            // rows actually occupy, so adding a third row is one line here and one there.
            float height = BaseHeight + (privacy ? ConsentRow : 0f) + LegalRow;

            MakePanel(new Vector2(860f, height), Loc.Get("ui.settings.title"));

            var row = UIKit.Box("Toggles", Panel, new Vector2(700f, 200f), new Vector2(.5f, 1f), new Vector2(0f, -260f));
            Toggle(row, "ic_music", new Vector2(-140f, 0f), () => GameSettings.MusicOn, GameSettings.SetMusic);
            Toggle(row, "ic_audio", new Vector2(140f, 0f), () => GameSettings.SfxOn,
                   on => { GameSettings.SetSfx(on); if (on) Audio.Sfx("chime", .5f); });

            Caption(row, "ui.settings.music", -140f);
            Caption(row, "ui.settings.sound", 140f);

            UIKit.Titled("Ver", Panel, Loc.Format("ui.settings.version", Application.version), 28, new Color(.44f, .32f, .24f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 40f), new Vector2(.5f, 1f),
                         new Vector2(0f, -420f), 0f, 0f);
            // The art licences, and the one place in the game that discharges them. Shrinkable
            // and nearly the full width of the panel because it is a *list* that grows with the
            // packs the build draws from — a fixed box would let the next one run off the paper
            // (UIKit.Label overflows rather than truncating), in whichever language is longest.
            UIKit.Shrinkable(
                UIKit.Titled("Credit", Panel, Loc.Get("ui.settings.credit"), 24,
                             new Color(.52f, .40f, .31f, .85f),
                             TextAnchor.MiddleCenter, new Vector2(800f, 36f), new Vector2(.5f, 1f),
                             new Vector2(0f, -462f), 0f, 0f), 16);

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
            // ----------------------------------------------------------------- the law
            // Required in the app rather than only on the store listing: App Store Review
            // 5.1.1(i) wants the privacy policy reachable from inside the app, and a link only
            // in App Store Connect is a documented rejection. Support is here for Guideline
            // 1.2's other half — this game publishes keeper names to a public board, and a way
            // to report somebody is not a way to reach us.
            //
            // Blue, which is this UI's colour for a secondary action — the undo key, the map
            // key, an overlay's dismiss. They were the shop's grey Restore skin first and read
            // as disabled, which on the one control a reviewer is told to look for is the worst
            // possible reading. Small rather than grey is how they stay quieter than Close
            // without looking switched off.
            //
            // Anchored to the bottom edge above Close, so the optional consent button above
            // cannot push them off: everything below the toggles hangs from the foot.
            var law = UIKit.Box("Legal", Panel, new Vector2(760f, LegalRow), new Vector2(.5f, 0f),
                                new Vector2(0f, 108f + 132f * .5f + LegalRow * .5f + 10f));

            Link(law, "ui.settings.privacy_policy", -250f, LegalLinks.Privacy);
            Link(law, "ui.settings.terms", 0f, LegalLinks.Terms);
            Link(law, "ui.settings.support", 250f, LegalLinks.Support);

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46, new Vector2(560f, 132f),
                             new Vector2(.5f, 0f), new Vector2(0f, 108f), () => Close());
        }

        /// <summary>
        /// What the panel is made of, so its height is arithmetic rather than a typed number
        /// somebody has to remember to move.
        ///
        /// <para>
        /// Internal so <c>LegalLinkTests</c> can prove the tallest arrangement still fits inside
        /// <see cref="PanelStack.TallestPanel"/> — a modal is centred and its ribbon stands proud
        /// of the top edge, so a panel that grows past that is drawn off the top of a 4:3 tablet
        /// and off nothing else. That is the failure this file has already had once.
        /// </para>
        /// </summary>
        internal const float BaseHeight = 700f, ConsentRow = 130f, LegalRow = 96f;

        /// <summary>
        /// One link out to the public site.
        ///
        /// <para>
        /// <see cref="Application.OpenURL"/> leaves the game, so the panel is closed first — on
        /// iOS the browser is a separate app and coming back to a modal that was never dismissed
        /// is how a player ends up tapping Close twice. A malformed URL is refused rather than
        /// handed over, because the platform's answer to one is to do nothing at all, which on a
        /// device is indistinguishable from a dead button.
        /// </para>
        /// </summary>
        void Link(Transform parent, string key, float x, string url)
        {
            var button = UIKit.TextButton("L_" + key, parent, "btn_blue", Loc.Get(key), 24,
                                          new Vector2(240f, 72f), new Vector2(.5f, .5f),
                                          new Vector2(x, 0f), () =>
            {
                if (!LegalLinks.Usable(url))
                {
                    Debug.LogError($"[Settings] refused to open a malformed link: '{url}'");
                    return;
                }

                Close(() => Application.OpenURL(url));
            });

            UIKit.Shrinkable(button.Label, 16);
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
