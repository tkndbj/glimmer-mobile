using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The ways back to playing, brought to a player standing on a board they have just been
    /// refused a restart of.
    ///
    /// <para>
    /// <b>It exists because <c>OutOfHeartsOverlay</c> cannot be raised over a run.</b> That
    /// panel is right everywhere it is raised today — a refused map node, an event tile, the
    /// victory panel's next — precisely because nothing is standing behind it: it navigates to
    /// the shop, and it closes itself the moment <c>Profile.CanPlay</c> reads true. Both of
    /// those are wrong here. Leaving this screen through <c>Flow.Go</c> abandons a committed run
    /// <em>without resolving it</em>, so <c>RunGuard</c>'s marker survives on disk and charges a
    /// heart at the next launch for a run nobody finished; and the commonest refusal on this
    /// panel is a player holding one heart, which is a state that panel would close itself on
    /// while the restart was still refused. So the shelf comes to them and the panel decides for
    /// itself when the gate has lifted — invariant 23's rule, which <c>GemShopOverlay</c>
    /// already answers for a lost run one panel over.
    /// </para>
    /// <para>
    /// <b>It is composition rather than a fourth copy of anything.</b> The free way is
    /// <see cref="HeartVideoFlow"/> and the paid way is <see cref="HeartRescueFlow"/> — both
    /// were lifted out of the defeat panel as collaborators taking a panel and two callbacks,
    /// which is exactly what a second caller needs — and the column they sit in is
    /// <see cref="HeartGatePanel"/>, shared with the panel above. What is left here is the two
    /// things only this panel knows: which sentence explains the refusal, and what onward means.
    /// </para>
    /// <para>
    /// <b>Onward is <c>RunScreen.RestartLevel</c> again, never <c>Rewind</c>, and that is the
    /// clause with teeth.</b> Hearts arriving do not imply the gate has lifted — a rescue of one
    /// heart to a player holding none leaves a charged restart still refused — so calling the
    /// mode's rewind directly would be the very bug this panel was built for, reintroduced by
    /// its own fix. Re-entering the door instead re-asks the gate, and when it does pass the
    /// player gets the ordinary forfeit confirmation they would have got with hearts in hand:
    /// abandoning a committed run is one of the three confirmations in this game, and skipping
    /// it because money changed hands would make a paid restart less guarded than a free one.
    /// </para>
    /// </summary>
    public sealed class RestartGateOverlay : ModalView
    {
        /// <summary>The run this is standing over. Held for its lifetime and for the way onward.</summary>
        public RunScreen Screen;

        /// <summary>The level, for the rescue's debit reason and its analytics.</summary>
        public LevelId Level;

        /// <summary>
        /// Hearts held when the restart was refused.
        ///
        /// <para>
        /// Told at build time rather than read here, because which of the two refusals this is
        /// cannot be worked out afterwards from a wallet that has since moved — and because it
        /// is also what the rescue prices itself against. See <see cref="RefusalKey"/>.
        /// </para>
        /// </summary>
        public int HeartsHeld;

        /// <summary>
        /// Set by the one exit that hands the run straight on to something which takes the board
        /// over again.
        ///
        /// <para>
        /// <c>PauseOverlay</c>'s flag exactly, and for its reason: this panel has four ways out
        /// — two offers, a dismiss button and the scrim — and a panel with several exits reports
        /// through none of them reliably, so the board is handed back by <see cref="OnDestroy"/>
        /// and the exception is the thing somebody declares. Forgetting to declare a hand-off
        /// costs a board that thaws a moment before the question over it is answered; forgetting
        /// to unlatch costs the player the run.
        /// </para>
        /// </summary>
        bool _handedOn;

        Text _countdown;
        bool _offering;

        /// <summary>The video, and what it pays into. See <see cref="HeartVideoFlow"/>.</summary>
        HeartVideoFlow _video;

        /// <summary>Gems for hearts, without leaving the board. See <see cref="HeartRescueFlow"/>.</summary>
        HeartRescueFlow _rescue;

        protected override void Build()
        {
            // Resolved once, at the top, because both decide the panel's height as well as its
            // buttons — and asking twice risks the two disagreeing if fill arrives in between,
            // leaving a button drawn outside the panel it belongs to.
            _offering = RewardedAds.ShouldOffer(AdPlacement.HeartRefill);

            // Once per visit, never per paint: it decides the offer, counts the impression and
            // subscribes to the balance, none of which a redraw may do again. canRetry is false
            // because this panel only exists when the answer was no — the gate has already been
            // asked, by the door, of the whole rule rather than of a heart count.
            if (_rescue == null)
                _rescue = new HeartRescueFlow(this, Level, HeartsHeld, canRetry: false,
                                              HeartRescueWhere.Restart, Rebuild, Proceed);

            var stack = HeartGatePanel.Of(_offering, _rescue.Exists);

            MakePanel(new Vector2(HeartGatePanel.Width, stack.Height),
                      Loc.Get("ui.hearts.restart_title"));

            // Wrapped and unadorned, matching every other body paragraph in the game — and
            // shrinkable, which the panel this borrows its column from does not need to be.
            // Both sentences here are about twice the length of that one (measured: two lines
            // against one at the same size in the same box), so the headroom a translation has
            // before it runs into the heart below it is halved. UIKit.Label defaults to
            // Overflow with no clipping, so the failure is a paragraph drawn through the art
            // rather than a truncated one — the house rule, and DefeatPanel's own body copy
            // takes the same guard at the same floor.
            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel, Loc.Get(RefusalKey(HeartsHeld)), 32,
                             new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                             new Vector2(680f, 150f), new Vector2(.5f, 1f), new Vector2(0f, -190f),
                             outline: 0f, shadow: 0f, wrap: true), 22);

            var empty = UIKit.Img("Heart", Panel, Art.S("Ui/ic_heart"),
                                  new Color(.62f, .58f, .60f, .45f), Vector2.one * 138f,
                                  new Vector2(.5f, 1f), new Vector2(0f, -380f));
            empty.preserveAspect = true;
            Tween.Breathe(empty.transform, .05f, 2.2f);

            // The countdown is a heading, not prose, so it keeps its outline — it is the one
            // number on this panel the player actually came to read.
            _countdown = UIKit.Titled("Clock", Panel, string.Empty, 52, Pal.Rose,
                                      TextAnchor.MiddleCenter, new Vector2(640f, 84f),
                                      new Vector2(.5f, 1f), new Vector2(0f, -500f),
                                      outline: 3f, shadow: 3f);
            Paint();

            if (stack.HasWatch)
            {
                // Guarded for its own lifetime because the celebration outlives this panel: a
                // player who backgrounds the app during the video can come back somewhere else
                // entirely, and the hearts are theirs either way.
                if (_video == null)
                    _video = new HeartVideoFlow(this, () => { if (this) Proceed(); });

                _video.Draw(Panel, new Vector2(560f, HeartGatePanel.ActionHeight),
                            new Vector2(.5f, 1f), new Vector2(0f, -stack.Watch));
            }

            if (stack.HasPaid)
                _rescue.Draw(Panel, new Vector2(560f, HeartGatePanel.ActionHeight),
                             new Vector2(.5f, 1f), new Vector2(0f, -stack.Paid));

            // KEEP PLAYING rather than GOT IT, because there is a board behind this one and
            // carrying on with it is a real answer rather than a dismissal. It is also the
            // honest default: the run they were refused a restart of is still winnable.
            UIKit.OneLine(
                UIKit.TextButton("Stay", Panel, "btn_blue", Loc.Get("ui.hearts.keep_playing"), 44,
                                 new Vector2(560f, HeartGatePanel.OkHeight), new Vector2(.5f, 1f),
                                 new Vector2(0f, -stack.Ok), () => Close()), 24);
        }

        /// <summary>
        /// Which refusal this is, in one sentence.
        ///
        /// <para>
        /// Two keys written out rather than one built from a count, for
        /// <c>WinOverlay.RankKeys</c> reason: a key assembled at runtime is invisible to the
        /// build's string scanner and ships missing in whichever language nobody tested. They
        /// are genuinely different news and a player can act on the difference — an empty bar
        /// is a wait, and a last heart is a choice they still have, spent on this board or on a
        /// fresh one.
        /// </para>
        /// </summary>
        static string RefusalKey(int heartsHeld)
            => heartsHeld > 0 ? "ui.hearts.restart_last" : "ui.hearts.restart_none";

        // ------------------------------------------------------------------ the way onward
        /// <summary>
        /// Closes, and asks the door again.
        ///
        /// <para>
        /// Raised by both offers and by the clock, so there is one way onward however the hearts
        /// arrived. It hands the run on — see <see cref="_handedOn"/> — because
        /// <c>RestartLevel</c> takes the board over itself, either with the forfeit confirmation
        /// or by raising this panel again on an offer that turned out not to be enough.
        /// </para>
        /// <para>
        /// Quiet, because what the player hears next is either a celebration they have just
        /// collected or the question they asked for, and a backing-out whoosh underneath either
        /// is one sound too many.
        /// </para>
        /// </summary>
        void Proceed()
        {
            if (IsLeaving) return;

            _handedOn = true;
            Close(() => { if (Screen) Screen.RestartLevel(); }, quiet: true);
        }

        /// <summary>
        /// The board comes off its latch however this panel went away, unless it was handed on.
        ///
        /// <c>PauseOverlay.OnDestroy</c> exactly, and deliberately unconditional apart from the
        /// flag: the screen underneath being torn down with this still open has to leave nothing
        /// frozen either.
        /// </summary>
        void OnDestroy()
        {
            if (_rescue != null) _rescue.Dispose();

            if (_handedOn) return;
            if (Screen) Screen.Resume();
        }

        void Update() => Paint();

        void Paint()
        {
            // The offer's own cooldown runs independently of the heart clock, so it is
            // repainted here too rather than only when the panel is built.
            if (_video != null) _video.Paint();

            if (!_countdown) return;

            // The gate rather than Profile.CanPlay, which is the whole reason this is not
            // OutOfHeartsOverlay: a restart is an abandonment and an entry, so one heart landing
            // is not necessarily enough. Asked of the screen, so there is no second copy of the
            // rule — and guarded, because the screen can be torn down under this panel.
            //
            // Only while nothing is stacked on this one, and that clause is not defensive. The
            // hearts land the instant a video finishes, which is several seconds before the
            // celebration over this panel has been collected — so without it this would close
            // out from under PrizeOverlay and put a forfeit confirmation up behind somebody's
            // confetti. Same for a heart arriving off the clock while the gem shelf is open.
            // The offers each raise Proceed themselves when they are done, so nothing is lost
            // by waiting: this branch is only ever the clock's way in.
            if (Screen && Screen.MayRestart && Flow.IsTopModal(this)) { Proceed(); return; }

            long seconds = Profile.SecondsToNextHeart;

            _countdown.text = seconds <= 0
                ? Loc.Get("ui.hearts.full")
                : string.Format(Loc.Get("ui.hearts.next"), Profile.Countdown(seconds));
        }
    }
}
