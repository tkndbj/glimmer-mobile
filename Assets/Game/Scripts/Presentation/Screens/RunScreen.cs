using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The screen a run is played on, whichever mode it belongs to.
    ///
    /// <para>
    /// <b>It exists so the panels around a run do not have to be written twice.</b> The defeat
    /// panel, the pause menu and the forfeit prompt all need to be able to say "try again",
    /// "restart", "back to the map" and "carry on" - and they used to say them to a
    /// <c>PlayScreen</c> specifically, which meant a second mode either duplicated three panels
    /// or went without them. Duplicating was the worse option by some distance: those panels
    /// carry the heart accounting, and two copies of a rule about charging players is exactly
    /// what invariant 9a is about.
    /// </para>
    /// <para>
    /// A base class rather than an interface, and that is a Unity detail worth stating: the
    /// panels - and the two collaborators below - hold a reference across frames and test it
    /// with <c>if (Screen)</c>, which is <c>UnityEngine.Object</c>'s lifetime check and the
    /// only one that answers correctly for a screen that has been destroyed underneath them.
    /// An interface reference would test as non-null on a dead object and call into it.
    /// </para>
    /// <para>
    /// <b>What is left here is two things and only two</b>: what a way out of a run costs
    /// (<see cref="Committed"/>) and when a run is allowed to run (<see cref="Hold"/>). Both
    /// are rules a mode must never own, and both are small. Everything else a run needs that is
    /// also not a mode's business has been lifted out - <see cref="Teaching"/> owns the lesson
    /// sequence and the review key, <see cref="Continue"/> owns the offer to buy one more go.
    /// That split is deliberate rather than tidy: this class had grown five responsibilities,
    /// which is the point at which a base class becomes the type nobody dares change, and each
    /// of the two that left was bigger than what remains. <c>WeaveRun</c> was taken apart into
    /// five for the same reason and against the same test - could any one of these rules be
    /// proved without building the other four.
    /// </para>
    /// <para>
    /// A mode contributes <em>declarations</em> - what it teaches, what its board is measured
    /// in, how to put it back, how to hand it more allowance - and never sequences any of it.
    /// </para>
    /// </summary>
    public abstract class RunScreen : View
    {
        /// <summary>
        /// A run screen's chrome runs to the top edge of the display: the safe layer holds the
        /// sides and the bottom and gives up the top.
        ///
        /// <para>
        /// <b>The board is the reason.</b> It is the largest control in the game and it is sized
        /// from what the header leaves, so every canvas unit the top inset takes is taken off the
        /// puzzle — on a phone with a deep cutout that is the difference between a comfortable
        /// board and a cramped one, on the one screen a player spends their whole session on.
        /// The home indicator is left alone, because the bottom of a board is board.
        /// </para>
        /// <para>
        /// <b>What it costs is stated plainly rather than argued away.</b> The top inset on a
        /// deep-cutout phone is around 120 canvas units (141 device pixels over a scale of
        /// 1.19 on an iPhone 13 Pro Max), and the header's keys sit 50 units below the top of
        /// their bar — so they do move up into the status strip. They stay on visible,
        /// pressable screen: a sensor housing is centred and the keys are at the two ears,
        /// which is where a phone's own clock and battery live. If a future device puts
        /// something across the whole width up there, this is the one line to take back.
        /// </para>
        /// <para>
        /// Declared here rather than per mode for this class's usual reason — a rule about what
        /// a run screen looks like, written once, is one that cannot come to differ between the
        /// glade and the weave.
        /// </para>
        /// </summary>
        protected override SafeArea.Edges SafeEdges => SafeArea.Edges.SidesAndBottom;

        /// <summary>Another go after the run was declared lost.</summary>
        public abstract void RetryAfterDefeat();

        /// <summary>
        /// Hands the level back after a panel that latched it — unless something else is still
        /// holding it.
        ///
        /// <para>
        /// <b>Concrete, and it is the third time this rule has been consolidated.</b> Every mode
        /// used to answer it, and every answer was the same two lines with the mode's own name
        /// for its board: "unlock it, unless the run is already over". A copy per mode is what
        /// the stake above was taken apart for, and this is a smaller version of the same
        /// hazard — the mode already declares <see cref="Latch"/>, which is that sentence
        /// exactly, so the second copy was only ever an opportunity for the two to drift.
        /// </para>
        /// <para>
        /// <b>A board a lesson is holding is not handed back.</b> That clause is what this
        /// method was made concrete for. A lesson is scheduled on a timer, so a player can open
        /// the pause menu in the beat before the first tip appears — and the menu's every exit
        /// runs through here (<c>PauseOverlay.OnDestroy</c>, deliberately, so no exit can forget
        /// to). Without the clause, closing that menu unlocks a board a tip is about to be drawn
        /// over, with a hole cut in its dim around the very tile it is pointing at. The rule the
        /// hole relies on is that whoever latched a board is what hands it back:
        /// <see cref="RunLessons"/> took this one and releases it when the last lesson closes.
        /// </para>
        /// </summary>
        public void Resume()
        {
            if (Teaching.Teaching) return;

            Latch(false);
        }

        // ------------------------------------------------------------ what a run is owed for
        /// <summary>
        /// The stake: whether this run has been paid for, and what every way out of it costs.
        ///
        /// <para>
        /// <b>It is here because it was written twice and the two copies drifted.</b> Each mode
        /// used to carry its own <c>Commit</c>, <c>Resolve</c>, <c>Forfeit</c> and
        /// <c>ConfirmForfeit</c> — four near-identical methods about charging players a heart,
        /// which is precisely what the remarks on this class already said must not happen. They
        /// did not stay identical: one guarded a closing cascade and the other did not, and,
        /// worse, <b>Lightweave's restart never called its copy at all</b>. A restart there was
        /// free, which on a mode whose fail state is a pot of ink that a restart refills meant
        /// the fail state could be walked out of for nothing. It was reported from play, and no
        /// compile, validator or test could have said so — because the rule was not anywhere, it
        /// was in two places and missing from a third.
        /// </para>
        /// <para>
        /// So a mode no longer decides what an exit costs. It says how to put its board back
        /// (<see cref="Rewind"/>), when its run is over (<see cref="RunOver"/>) and how to write
        /// an abandonment down (<see cref="NoteAbandoned"/>); everything about hearts, the
        /// confirmation and <c>RunGuard</c> is here. A mode that forgets to price an exit is
        /// now unrepresentable rather than merely unlikely.
        /// </para>
        /// </summary>
        protected bool Committed { get; private set; }

        HeartPrice? _price;

        /// <summary>
        /// What this screen's level costs, and if it costs nothing, why — a mode's free
        /// opening, or a glade this player has already finished. See <see cref="HeartStake"/>.
        ///
        /// <para>
        /// <b>A fact about the level, resolved once and cached for the life of the screen</b>,
        /// which is what makes it safe to read from anywhere in an ending. It is deliberately
        /// <em>not</em> tied to <see cref="Commit"/> and <see cref="Resolve"/>: a screen plays
        /// one level, a restart and a retry play it again, and the price cannot move between
        /// those. Tying it to the run's own lifecycle was tried and is wrong in a way nothing
        /// would have caught — both modes call <c>Resolve</c> a few lines <em>before</em>
        /// <c>RunLedger.Loss</c>, on purpose, so that a crash mid-defeat cannot charge twice.
        /// A stake cleared by <c>Resolve</c> therefore reads "free" at the exact moment the
        /// heart is taken, and every lost glade in the game becomes free. It compiles, it
        /// validates, and only playing would show it.
        /// </para>
        /// <para>
        /// <b>The latch is one-way: free is remembered, charged is not.</b> Both clauses can
        /// move underneath a screen — the window is content and a push can land mid-run, and the
        /// replay clause turns over the instant a glade is first cleared, which is something
        /// that happens in the middle of this screen's own life. Only one of those directions is
        /// dangerous. A board the player was told was free must never become one they are
        /// charged for on the way out of it, so a free answer is kept for ever; a charged one
        /// becoming free costs nobody anything and is the honest reading of a rule that has just
        /// changed in the player's favour — which is what makes a first clear followed by a
        /// restart on the same screen free, rather than charged by a value latched before the
        /// win. And it is still one answer rather than several: the ledger is <em>told</em> this
        /// (<c>RunLedger.Loss</c>'s <c>price</c>) instead of working it out again later, which
        /// is what stopped one run having two prices.
        /// </para>
        /// <para>
        /// An unresolved level answers <see cref="HeartPrice.Charged"/> and is never latched,
        /// which is <see cref="HeartStake"/>'s safe direction in both places: a glade nothing
        /// can name is priced like every other glade rather than handed out free.
        /// </para>
        /// </summary>
        protected HeartPrice Price
        {
            get
            {
                if (_price.HasValue && _price.Value != HeartPrice.Charged) return _price.Value;
                if (!StakeLevel.IsValid) return HeartPrice.Charged;

                _price = HeartStake.PriceOf(GameContent.Index, StakeLevel);
                return _price.Value;
            }
        }

        /// <summary>
        /// Whether this screen's level is one somebody pays a heart for — the bool half of
        /// <see cref="Price"/>, and what every exit is priced from.
        /// </summary>
        protected bool Staked => Price == HeartPrice.Charged;

        /// <summary>The level this run is staked on, for <c>RunGuard</c>'s marker.</summary>
        protected internal abstract LevelId StakeLevel { get; }

        /// <summary>
        /// Whether this run has already reached an ending, however it is spelled by the mode —
        /// a glade calls it finished, a weave counts the closing cascade too.
        ///
        /// A run that is over is never forfeited on the way out of it: leaving a grove that has
        /// already been won costs nothing and asks nothing.
        /// </summary>
        protected abstract bool RunOver { get; }

        /// <summary>Puts the board back as it started. What it costs is not this method's business.</summary>
        protected abstract void Rewind();

        /// <summary>
        /// Writes an abandonment down, with whatever numbers this mode measures progress in —
        /// a glade counts turns, a weave counts critters woken.
        /// </summary>
        protected abstract void NoteAbandoned(string reason);

        /// <summary>
        /// Notes on disk that the run is now owed for, so the process dying does not make it
        /// free. See <c>RunGuard</c> — <c>Boot</c> charges anything still written down at the
        /// next launch.
        /// </summary>
        protected void Commit()
        {
            // Guarded rather than assumed. Everything downstream takes a heart off a player, so
            // the one path where the level never resolved is worth a line of insurance.
            if (Committed || !StakeLevel.IsValid) return;

            Committed = true;

            // No marker for a free run, and that is the whole of what makes the crash path
            // right. The marker exists so a process that dies mid-run is charged at the next
            // launch, and Boot claims it before any content has loaded — so nothing there
            // could ask whether the glade was free. Not writing one says it in the only place
            // that still knows.
            if (Staked) RunGuard.Begin(StakeLevel);
        }

        /// <summary>
        /// The run reached an ending and has been accounted for. Every path that finishes one
        /// calls it — a win, a defeat, or an abandonment the player agreed to.
        ///
        /// Missing one costs a player a heart they did not owe on their next launch, so it is
        /// deliberately cheap and idempotent rather than conditional.
        /// </summary>
        protected void Resolve()
        {
            Committed = false;
            RunGuard.Resolve();
        }

        /// <summary>
        /// The player walked away from a run that had begun. It costs exactly what losing it
        /// costs, because that is what it is.
        ///
        /// <para>
        /// Note what it does <em>not</em> do. A defeat also counts a run towards the daily
        /// chests and feeds the streak; a forfeit counts towards neither. Those are for runs
        /// that were <em>finished</em>, won or lost, and a withdrawn run was not — which is also
        /// what keeps the restart button from being the fastest way to bank three chests.
        /// </para>
        /// </summary>
        void Forfeit(string reason)
        {
            if (!Committed) return;

            // A free run — a mode's opening, or a glade already finished — is walked away from
            // for nothing, and it still reaches NoteAbandoned: what a run cost is an economy
            // question, and whether somebody left one is a fact worth counting whatever it cost.

            NoteAbandoned(reason);
            if (Staked) Wallet.TrySpendHeart();
            Resolve();
        }

        /// <summary>
        /// Asks before charging, then does the thing.
        ///
        /// On an uncommitted run, an already-finished one, one of a mode's free openings or a
        /// glade the player has already cleared there is nothing to charge, so it does the thing
        /// immediately — a confirmation over a free action is friction that teaches players to
        /// dismiss the one that is not free.
        /// </summary>
        protected void ConfirmForfeit(ForfeitOverlay.Kind kind, string reason, Action then)
        {
            if (!Committed || RunOver) { then(); return; }

            // A free run is walked out of without being asked about, for the same reason an
            // uncommitted one is: a confirmation over an action that costs nothing is friction
            // that teaches players to dismiss the one that does. That covers a mode's opening
            // glades and every glade this player has already finished — going back to a board
            // you beat and thinking better of it is the commonest free exit there is, and being
            // stopped by a panel warning about a heart nobody is taking is how a player learns
            // the warning means nothing. It is still forfeited, so the abandonment is written
            // down and the run stops being owed for.
            if (!Staked) { Forfeit(reason); then(); return; }

            Latch(true);

            Flow.Modal<ForfeitOverlay>(v =>
            {
                v.Choice = kind;
                v.OnConfirm = () => { Forfeit(reason); then(); };
                v.OnCancel = Resume;
            });
        }

        // ------------------------------------------------------------ the ways out
        /// <summary>
        /// Put the level back as it started, at the price of the run in progress.
        ///
        /// <para>
        /// <b>A restart abandons a run and begins another, so it is priced like any other
        /// abandonment</b> and asked about the same way. Not sealed by the language but by
        /// having nothing to override: a mode supplies <see cref="Rewind"/> and cannot get at
        /// the decision. That is deliberate — this is the exact method Lightweave shipped
        /// unpriced.
        /// </para>
        /// </summary>
        public void RestartLevel()
            => ConfirmForfeit(ForfeitOverlay.Kind.Restart, "restart",
                              () => { Rewind(); Resume(); });

        /// <summary>Leaving without solving is a data point, not just a navigation.</summary>
        public virtual void LeaveToMap()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "back", () => Flow.Go<LevelsScreen>());

        /// <summary>The pause menu's way out to the hub, guarded like every other.</summary>
        public virtual void LeaveToHome()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "home", () => Flow.Go<HomeScreen>());

        // ------------------------------------------------------------ the two collaborators
        /// <summary>
        /// This run's lesson sequence and the "show me that again" key in its header.
        ///
        /// <para>
        /// Exposed rather than forwarded through half a dozen one-line methods, so that what a
        /// mode is talking to is visible at the call site. A mode asks it to build its key and
        /// to repaint; it never asks it to teach, because when a lesson goes up is
        /// <see cref="OnPresented"/>'s business and the key's.
        /// </para>
        /// </summary>
        protected RunLessons Teaching { get; }

        /// <summary>
        /// The offer to carry this run on rather than lose it. See <see cref="RunContinueFlow"/>.
        ///
        /// <para>
        /// A mode reaches it exactly once, from its own fail state, with
        /// <c>Continue.OfferOrLose(...)</c> - handing over what it would otherwise have done
        /// outright. It cannot get at the price.
        /// </para>
        /// </summary>
        protected RunContinueFlow Continue { get; }

        protected RunScreen()
        {
            Teaching = new RunLessons(this, Hold);
            Continue = new RunContinueFlow(this, Hold);
        }

        // ------------------------------------------------------------ one more go
        /// <summary>
        /// The unit this mode's allowance is measured in, which is what the offer is priced and
        /// worded in. Turns unless a mode says otherwise.
        /// </summary>
        protected internal virtual ContinueUnit MeasuredIn => ContinueUnit.Turns;

        /// <summary>
        /// How much allowance has to be restored before a grant would be usable room, or
        /// <see cref="RunContinue.NoContinue"/> when this run cannot be rescued at any price.
        ///
        /// <para>
        /// <b>The one thing about a continue that only the mode knows.</b> A glade is lost when
        /// its counter reaches the budget, and any turn at all makes it playable again, so its
        /// answer is nought. A weave is lost when the light left cannot cover the cheapest
        /// possible finish, which usually leaves cells in the pot that cannot be spent - so its
        /// answer is that shortfall, and handing over the authored figure alone would put the
        /// player back on a board that ends again in the same frame, having taken their gems.
        /// </para>
        /// <para>
        /// The default is <see cref="RunContinue.NoContinue"/> rather than nought, so a mode
        /// with no fail state - or one whose fail state has not been thought about - is never
        /// offered a purchase by accident. Silence means no, which is the safe direction for
        /// the one seam here that charges money.
        /// </para>
        /// </summary>
        protected internal virtual int ContinueDeficit => RunContinue.NoContinue;

        /// <summary>
        /// Hands the run the allowance it has just paid for and puts the board back in play.
        ///
        /// <para>
        /// Called only after the debit has landed, so an implementation may assume it is owed.
        /// It has to do both halves - the meter <em>and</em> the board - because a mode's fail
        /// state locks its own view on the way out and nothing else knows how to unlock it.
        /// </para>
        /// </summary>
        protected internal virtual void ContinueWith(int amount) { }

        // ------------------------------------------------------------ when a run may begin
        /// <summary>
        /// Why this run may not be under way yet. A mode's clock asks this before it starts
        /// and before it advances.
        ///
        /// <para>
        /// <b>It is held from construction and released here, never by a mode.</b> Both modes
        /// used to find the start edge by polling their own board's latch, which is a boolean
        /// several things write — and the one that wrote last was an animation. A first-timer's
        /// tip latched the board at the moment the screen was presented; the board's intro
        /// sweep, scheduled earlier from a different object, unlatched it a beat later; and the
        /// countdown then ran for as long as the player took to read a lesson they are only
        /// ever shown once. On the weave the leak was smaller and had the same cause — a grove
        /// is playable from the frame it is built, so the clock ran for the whole of the iris
        /// opening over it, before the player had seen anything.
        /// </para>
        /// <para>
        /// So the answer is a latch nothing else writes, and the modes ask it <em>in addition
        /// to</em> their own — a board that is still animating is not playable either, and that
        /// stays their business. See <see cref="RunHold"/> for why a leak here is the safe
        /// direction.
        /// </para>
        /// </summary>
        protected RunHold Hold { get; } = new RunHold(RunHold.Opening);

        /// <summary>
        /// Seconds of actual play this run has been given, accumulated a frame at a time.
        ///
        /// <para>
        /// <b>Not elapsed real time.</b> It only advances on frames <see cref="Tick"/> allowed,
        /// so a paused run, a panel over the board, a lesson still being read and a
        /// backgrounded app all contribute nothing. Nothing grades a run on this and nothing
        /// stores it — the countdown is gone and a glade is scored on turns alone
        /// (<c>LevelTuning.StarsFor</c>). What is left is the one question a mode still has to
        /// ask of it: has the player been sitting on this board long enough that leaving it is
        /// walking away from a run rather than glancing at one. See <c>PlayScreen.Commit</c>.
        /// </para>
        /// </summary>
        protected float Played { get; private set; }

        /// <summary>
        /// The most any single frame may contribute, in seconds.
        ///
        /// A quarter second is four frames at 15fps — longer than any hitch a running game
        /// produces, and short enough that a resume, a long asset load or a breakpoint in the
        /// Editor cannot arrive as one enormous <c>deltaTime</c> and commit a run the player
        /// never touched.
        /// </summary>
        protected const float MaxTick = .25f;

        /// <summary>Back to nothing played. Every path that hands out a fresh board calls it.</summary>
        protected void ResetPlayed() => Played = 0f;

        /// <summary>
        /// Gives a run one frame of play, if it is allowed one. Returns whether it got it, so a
        /// caller can skip everything else it does per running frame.
        ///
        /// <para>
        /// <b>A funnel rather than a convention.</b> Whether a run may advance at all is asked
        /// in one place, so a mode cannot let one run without the question being put. The
        /// alternative is each mode remembering to consult a latch in its own <c>Update</c>,
        /// which is exactly the shape of rule this project has paid for twice: the pause menu
        /// that only unlatched from its buttons, and the asset scope only one of two screens
        /// released.
        /// </para>
        /// <para>
        /// <paramref name="playable"/> is the mode's own half of the answer, and it stays the
        /// mode's business: a board still flying in, a cascade playing out, a panel over the
        /// top. This one adds the half no mode can see — whether the run has been allowed to
        /// begin at all.
        /// </para>
        /// </summary>
        protected bool Tick(bool playable)
        {
            if (!playable || Hold.Held) return false;

            // Unscaled, like every clock here: a run must not stretch because something paused
            // the game underneath it.
            float delta = Time.unscaledDeltaTime;
            if (delta > 0f && !float.IsNaN(delta) && !float.IsInfinity(delta))
                Played += delta > MaxTick ? MaxTick : delta;

            return true;
        }

        /// <summary>
        /// Everything this run teaches, in the order it should be taught. Empty for the
        /// overwhelming majority of runs, which teach nothing.
        ///
        /// <para>
        /// <b>Everything, not only what is new.</b> A mode used to filter this against
        /// <see cref="TipLedger"/> itself, which was correct while the opening sequence was the
        /// only reader — and stopped being correct the moment a second one arrived. The review
        /// button asks the same question and wants the opposite answer: the player pressing it
        /// has by definition already seen every lesson on the board, so a list filtered by
        /// "never met" is empty exactly when they are asking. So a mode declares what its board
        /// <em>contains</em>, which is a fact about the board, and this class asks
        /// <see cref="TipLedger"/> the question about the player. One declaration, two readings,
        /// and nothing for the two to disagree about.
        /// </para>
        /// <para>
        /// Filled rather than returned so a mode that teaches nothing allocates nothing, and
        /// asked at moments when the board exists and can be pointed at — once when the screen
        /// is presented, and again each time the review is opened, because a restart rebuilds
        /// the very tiles a lesson rings and a list cached across one would point at destroyed
        /// transforms. A mode resolves its own targets here: the scan lives in Domain and knows
        /// nothing about tiles or pills.
        /// </para>
        /// </summary>
        protected internal virtual void Lessons(List<Lesson> into) { }

        /// <summary>
        /// Whether this run could take a lesson panel over it right now.
        ///
        /// <para>
        /// The opening sequence never asks — nothing else can be happening at the moment a
        /// screen is presented — but the review button is a live control sitting in the header
        /// for the whole run, and a board mid-celebration or mid-cascade is latched by something
        /// that will hand it back itself. Teaching over the top of that would end with
        /// <see cref="Latch"/> unlatching a board its own animation still owns. A mode answers
        /// with the predicate it already has for "is this taking input".
        /// </para>
        /// </summary>
        protected internal virtual bool Teachable => true;

        /// <summary>
        /// How long to wait before the first lesson appears, so the board it points at has
        /// finished arriving. A mode whose entrance is longer overrides it.
        /// </summary>
        protected internal virtual float LessonDelay => .6f;

        /// <summary>
        /// Latches this mode's board while a lesson is up, and hands it back afterwards.
        ///
        /// <para>
        /// Called exactly once each way, from the two edges of the teaching sequence, so no
        /// mode has to pair them itself. An implementation must refuse to hand back a board
        /// whose run has already ended — a lesson dismissed over a finished board must not
        /// make it live again.
        /// </para>
        /// </summary>
        protected internal virtual void Latch(bool latched) { }

        // ------------------------------------------------------------ the opening
        /// <summary>
        /// Teaches whatever this run brings that the player has never met, then lets the run
        /// begin - in that order, for every mode, without any of them arranging it.
        ///
        /// <para>
        /// <b>Sealed.</b> The ordering is the whole point of it, and an override that forgot to
        /// chain would put back a run advancing behind a modal. A mode contributes through
        /// <see cref="Lessons"/> instead, which is a declaration and cannot be got in the
        /// wrong order.
        /// </para>
        /// <para>
        /// <see cref="RunLessons.Open"/> takes the teaching hold before it returns, so
        /// releasing the opening one immediately afterwards can never leave a frame in which
        /// the run is free between the two. One free frame is one frame of a run the player has
        /// not been shown.
        /// </para>
        /// </summary>
        public sealed override void OnPresented()
        {
            Teaching.Open();
            Hold.Release(RunHold.Opening);
        }
    }
}
