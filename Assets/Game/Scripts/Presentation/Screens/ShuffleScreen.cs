using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>The Shuffle lane.</b> Thornwatch's board, hill and verb with two things changed: no
    /// boss ever walks on, and every two waves a hand of three upgrade cards is put on the table
    /// and one of them is taken into the build (MODES.md 59). The line is the player's own
    /// loadout, exactly as on a chapter and the Infinite lane.
    ///
    /// <para>
    /// <b>A subclass of the siege screen and not a copy</b>, which is invariant 20b's demand of a
    /// lane: everything about being a run - the heart taken at the gate, the stake, the record,
    /// the chests, the tasks, the lessons, the utilities, the pause and the forfeit - is the
    /// siege screen's and is not restated here. What this screen owns is exactly what the lane
    /// adds: which rules a board is dealt from (<see cref="Ruled"/>), the run's build
    /// (<see cref="ShuffleRun"/>) and when a hand is put on the table (<see cref="Running"/>).
    /// </para>
    /// <para>
    /// Stars still pay through the star ledger. Every run also pays ten XP and ten credits
    /// per cleared wave through its own reward counters. Neither best waves nor grades pay,
    /// and nothing reaches the Infinite lane's ledger or its daily credit allowance.
    /// </para>
    /// <para>
    /// <b>A run outlives the app</b> (MODES.md 59s): at every hand it is suspended on the
    /// device (<see cref="ShuffleSnapshot"/>, <see cref="ShuffleResume"/>), and the next time
    /// this screen opens on the level it picks the run up on the wave after that hand. The
    /// snapshot goes with the run - at its ending, its forfeit, a restart or the line falling -
    /// so only a run that was still going when the app closed is ever resumed.
    /// </para>
    /// </summary>
    public sealed class ShuffleScreen : SiegeScreen
    {
        ShuffleRun _run;
        bool _rewardsBanked;

        /// <summary>The suspended run this screen is opening on, from <see cref="Arm"/> to <see cref="Reseat"/>.</summary>
        ShuffleSnapshot _resume;

        /// <summary>The run folded back from <see cref="_resume"/>, ready before the board is dealt.</summary>
        ShuffleRun _resumed;

        /// <summary>Whether the rules this screen holds open part-way up, for a resumed run.</summary>
        bool _partway;

        /// <summary>
        /// Waves a resumed run cleared before this board opened: the board counts only the waves
        /// it saw itself (<c>SiegeBoard.WavesThisRun</c>), and these were played and never paid.
        /// </summary>
        int _carried;

        /// <summary>The document this run's tuning was read from, null for the build's own; kept in every snapshot.</summary>
        string _tuningBody;

        /// <summary>A resumed run's open hand, owed a panel on its first running frame.</summary>
        bool _offerCarried;

        /// <summary>Whether a snapshot of this run stands on the device, so letting go of it costs nothing when none does.</summary>
        bool _suspended;

        /// <summary>
        /// The seed the next run is dealt from. The clock, unless a fixture or a bench set one -
        /// a seed is the one thing about a run worth being able to repeat.
        /// </summary>
        public uint Seed { get; set; }

        /// <summary>
        /// The level's own rules, untouched: the run opens at wave one on the player's loadout.
        ///
        /// <b>Overridden to refuse the siege screen's checkpoint</b>, which that seam applies to
        /// any layout with a ramp - and this lane's has one (<c>ShuffleRamp</c>), so without this
        /// an Infinite-lane start would reach a Shuffle run. The line is the base screen's
        /// (<c>WardLoadout.Line</c>, read by <see cref="SiegeRules"/> and loaded by
        /// <c>LineToLoad</c>), so the line stood and the line loaded are one answer.
        ///
        /// <para>
        /// <b>A resumed run opens on the wave after its last hand</b> (MODES.md 59s), through the
        /// same <see cref="SiegeStart"/> a checkpoint uses, with no ranks: nothing drops on this
        /// hill, so a walked run's posts are at the rank the snapshot seats them at.
        /// </para>
        /// </summary>
        protected override ProtoLevelRules Ruled(ProtoLevelRules rules)
        {
            _partway = _resume != null && rules is SiegeRules;
            return _partway ? ((SiegeRules)rules).From(new SiegeStart(_resume.Wave, 0)) : rules;
        }

        /// <summary>The first deal on this screen picks up a suspended run, when one is waiting.</summary>
        protected override void Play()
        {
            Arm(resume: true);
            base.Play();
            Reseat();
        }

        /// <summary>
        /// A restart is a new run: whatever was suspended is let go (the forfeit that asked first
        /// already has), and the board opens at wave one.
        /// </summary>
        protected override void Rewind()
        {
            Forget();
            Arm(resume: false);
            base.Rewind();
            Reseat();
        }

        public override void RetryAfterDefeat()
        {
            Forget();
            Arm(resume: false);
            base.RetryAfterDefeat();
            Reseat();
        }

        /// <summary>
        /// The "i" opens this lane's own rules page rather than every siege tip again (the
        /// owner, 2026-10-14). The opening lessons still run; the panel holds the run
        /// (<c>RunHold.Covered</c>) like any modal.
        /// </summary>
        protected internal override bool ExplainsItself => true;

        protected internal override void Explain()
        {
            if (!Flow.HasModal) Flow.Modal<ShuffleInfoOverlay>();
        }

        /// <summary>
        /// The run's one lesson of its own: the pause key, whose menu holds SAVE &amp; QUIT
        /// (MODES.md 59s). Deferred, raised after the first card a run takes (<see cref="Take"/>),
        /// so it never queues at the opening behind the board's own lessons.
        /// </summary>
        protected internal override void Lessons(List<Lesson> into)
        {
            base.Lessons(into);
            into.Add(Lesson.Later(Mechanic.ShuffleSave, RightKeyNode));
        }

        /// <summary>This lane's run can be put away and picked up later (MODES.md 59s).</summary>
        protected internal override bool Suspends => true;

        /// <summary>
        /// The pause menu's SAVE &amp; QUIT: the run is kept as its last snapshot and the player is
        /// taken back to this lane's hub, which then offers to resume it. <b>Not a forfeit</b> -
        /// nothing is abandoned, banked or charged, and the snapshot is not let go.
        ///
        /// <para>
        /// A run nobody has committed to (no move made, no heart paid) has nothing to keep and
        /// simply leaves, as it always could for free. One that has begun is kept from its
        /// first move (<see cref="Running"/>), so there is a snapshot to keep; if a continue has
        /// just stood a fallen line back up and no hand has come since, it is taken now.
        /// </para>
        /// </summary>
        protected internal override void SaveAndQuit()
        {
            if (Level == null) return;

            if (!RunOver && Committed)
            {
                if (!_suspended) Suspend();
                if (_suspended && _run != null)
                    ShuffleAnalytics.TrackSaved(Level.Id, _run, ShuffleSnapshot.WaveAfter(_run.Hands));
            }

            var chapter = Level.Chapter;
            Flow.Go<LevelsScreen>(v => v.ChapterId = chapter);
        }

        /// <summary>The figures the run being dealt plays on. See <see cref="Arm"/>.</summary>
        ShuffleTuning _tuning;

        /// <summary>
        /// Takes the tuning this run will play on (MODES.md 59l) and hands the ramp its figures,
        /// <b>before</b> the base deals the board - the board musters its first wave as it is
        /// built, and a wave dealt off the last run's figures would be the wrong first wave.
        ///
        /// <para>
        /// <b>A snapshot, read from memory and never waited for</b> (<see cref="ShuffleConfig.Current"/>):
        /// the last good published tuning this device holds, else the build's own. Taken for
        /// every deal - the first, a restart and a retry - because each is a new run, and never
        /// again until the next, so a document that lands mid-run changes nothing in it.
        /// Anything going wrong here plays the build's own figures rather than no run at all.
        /// </para>
        /// <para>
        /// <b>A resumed run plays the document it opened on</b> (<see cref="ShuffleSnapshot.Tuning"/>),
        /// read back here, so neither a retune nor an update that landed while it was away
        /// reaches it. The held rules are let go first either way, because they are what decide
        /// where the board opens: part-way up for a resume, wave one for anything after it.
        /// </para>
        /// </summary>
        void Arm(bool resume)
        {
            _resume = null;
            _resumed = null;
            _offerCarried = false;

            if (resume) Pickup();
            if (_resume != null || _partway) Rerule();

            if (_resumed != null)
            {
                RampOf()?.Use(_tuning.Ramp);

                // The held rules are what the board is about to be dealt from, so they are asked
                // here, before there is a board: a resume they would not open part-way is let go
                // now rather than seated on a board opened somewhere else.
                if (Rules is SiegeRules ruled && ruled.Start.Wave == _resume.Wave) return;

                Drop(_resume, $"the rules open on wave {(Rules as SiegeRules)?.Start.Wave ?? 0}, not {_resume.Wave}");
                _resume = null;
                _resumed = null;
                Rerule();
            }

            try
            {
                _tuning = ShuffleConfig.CurrentWithBody(out _tuningBody);
                RampOf()?.Use(_tuning.Ramp);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                _tuning = ShuffleTuning.Shipped;
                _tuningBody = null;
                RampOf()?.Use(ShuffleTuning.Shipped.Ramp);
            }
        }

        /// <summary>
        /// Reads the run waiting on this level, if any, and folds it back (<see cref="ShuffleRun.Resume"/>)
        /// <b>before</b> a board is dealt, so a snapshot that cannot be honoured whole is let go
        /// here rather than half applied to a board already opened part-way up.
        /// </summary>
        void Pickup()
        {
            if (Level == null) return;

            var snapshot = ShuffleResume.Read(Level.Id);
            if (snapshot == null) return;

            string fault;
            var tuning = Reread(snapshot.Tuning, out fault);
            var layout = (Level.RulesAs<ProtoLevelRules>() as SiegeRules)?.Layout;

            ShuffleRun run = null;
            if (tuning != null && snapshot.Fits(layout, out fault))
                run = ShuffleRun.Resume(snapshot, tuning, out fault);

            if (run == null)
            {
                Drop(snapshot, fault);
                return;
            }

            _resume = snapshot;
            _resumed = run;
            _tuning = tuning;
            _tuningBody = snapshot.Tuning;
        }

        /// <summary>The tuning a snapshot's document reads as: the build's own for none, null when it no longer reads.</summary>
        static ShuffleTuning Reread(string body, out string fault)
        {
            fault = null;
            if (string.IsNullOrEmpty(body)) return ShuffleTuning.Shipped;

            return ShuffleTuning.TryRead(body, false, out var tuning, out fault) ? tuning : null;
        }

        /// <summary>
        /// Lets go of a suspended run that cannot be resumed - a card withdrawn by an update, a
        /// tuning this build cannot read, a level whose line has changed - and pays the waves it
        /// had cleared, exactly as a forfeit of it would have. <b>Cleared before it is paid</b>,
        /// so a crash between the two loses those waves rather than paying them twice.
        /// </summary>
        void Drop(ShuffleSnapshot snapshot, string fault)
        {
            Debug.LogWarning($"[Shuffle] a suspended run could not be resumed and was let go: {fault}");

            ShuffleResume.Clear(Level.Id);
            ShuffleAnalytics.TrackDropped(Level.Id, fault);
            XpBoost.Bank(ShuffleRewards.XpFor(ShuffleLedger.Bank(Level.Id, snapshot.Wave - 1)));
        }

        /// <summary>This level's ramp, or null on a layout that has none.</summary>
        ShuffleRamp RampOf()
        {
            if (!(Rules is SiegeRules siege)) return null;

            var plan = siege.Layout;
            return plan != null ? plan.Ramp as ShuffleRamp : null;
        }

        /// <summary>
        /// A fresh run's build on a fresh board. Called after every deal of a board - the first,
        /// a restart and a retry - because a board is built holding the plain line and the
        /// build is a thing that happens to one run.
        /// </summary>
        void Reseat()
        {
            var board = Siege != null ? Siege.Siege : null;
            if (board == null) return;

            if (_resumed != null && SeatResumed(board)) return;

            uint seed = Seed != 0u ? Seed : ShuffleRun.SeedNow();
            Seed = 0u;

            _run = new ShuffleRun(seed, _tuning ?? ShuffleTuning.Shipped);
            _rewardsBanked = false;
            _carried = 0;
            board.Boosts = _run.Build.Boosts;
            board.Refit();
        }

        /// <summary>
        /// The suspended run, seated on a board opened on its wave (MODES.md 59s): its build on
        /// the line, every post as it stood, its continues counted, and the run committed without
        /// a second heart - that was paid at the door the first time. False, changing nothing,
        /// when the board did not open where the snapshot said it would.
        /// </summary>
        bool SeatResumed(SiegeBoard board)
        {
            var snapshot = _resume;
            var run = _resumed;
            _resume = null;
            _resumed = null;

            // Unreachable while `Arm` has asked the rules (the board is dealt from them), and kept
            // so a seam moving under this one can never seat a run on a board it does not fit.
            if (board.StartWave != snapshot.Wave)
            {
                Debug.LogError($"[Shuffle] a resumed run met a board opened on wave {board.StartWave}, not {snapshot.Wave}");
                Drop(snapshot, $"the board opened on wave {board.StartWave}, not {snapshot.Wave}");
                return false;
            }

            _run = run;
            _rewardsBanked = false;
            _carried = snapshot.Wave - 1;
            board.Boosts = _run.Build.Boosts;
            board.Refit();
            snapshot.Seat(board);

            Continue.Carry(snapshot.Continues, snapshot.Watched);
            CommitCarried();

            _suspended = true;
            _offerCarried = _run.Open != null;

            ShuffleAnalytics.TrackResumed(Level.Id, _run, snapshot.Wave);
            return true;
        }

        /// <summary>
        /// Keeps this run on the device as it stands now (<see cref="ShuffleSnapshot.Of"/>), so
        /// closing the app does not end it. Asked at the first move, a hand, a take and a refresh -
        /// the moments a run is fully described by its cards and its line - and only of a run that has begun,
        /// so a run nobody committed to (and paid no heart for) can never be resumed as one.
        /// </summary>
        void Suspend()
        {
            var board = Siege != null ? Siege.Siege : null;
            if (_run == null || board == null || Level == null || RunOver || !Committed) return;

            var snapshot = ShuffleSnapshot.Of(Level.Id, _run, board, _tuningBody, Continue.Taken, Continue.Watched);
            if (snapshot == null) return;

            ShuffleResume.Write(snapshot);
            _suspended = true;
        }

        /// <summary>
        /// Lets go of this run's snapshot: the run has ended, been forfeited or restarted, or its
        /// line has fallen. Free when none stands.
        /// </summary>
        void Forget()
        {
            if (!_suspended || Level == null) return;

            _suspended = false;
            ShuffleResume.Clear(Level.Id);
        }

        /// <summary>
        /// Every running frame: is a hand owed. The siege screen's own half runs first.
        ///
        /// <b>Asked only on a running frame</b>, so a hand is never dealt over a pause, a lesson,
        /// a defeat panel or a board still arriving - and never twice, because a hand on the
        /// table is a hand <see cref="ShuffleRun.Owed"/> answers false for.
        /// </summary>
        protected internal override void Running(bool running)
        {
            base.Running(running);

            if (_run == null || Siege == null) return;

            var board = Siege.Siege;
            if (board == null) return;

            // **A fallen line is not a place to resume from** (MODES.md 59s): the snapshot goes the
            // frame the last post falls, before the continue is offered, so closing the app over
            // the offer is never a free second try. A continue that is bought carries on, and the
            // next hand suspends it again.
            if (board.IsFinished)
            {
                Forget();
                return;
            }

            if (!running || RunOver) return;

            // **A run is kept from its first move**, not only from its first hand, so SAVE & QUIT
            // and a closed app both keep it before a card has been dealt; it reopens on wave one.
            if (!_suspended && Committed && _run.Hands == 0) Suspend();

            // A resumed run's open hand, put back on the table on the first frame it can be.
            if (_offerCarried)
            {
                _offerCarried = false;
                Offer();
                return;
            }

            if (!_run.Owed(board)) return;

            var hand = _run.Deal();

            // Suspended at every hand, empty or not: a deck that has run dry is still a run
            // somebody may want to pick up later.
            Suspend();

            if (hand.Count == 0) return;

            Offer();
        }

        /// <summary>
        /// Puts the open hand in front of the player. The panel holds the run for as long as it
        /// stands (<c>RunHold.Covered</c>), and it cannot be dismissed without a card.
        /// </summary>
        void Offer()
        {
            if (_run == null || _run.Open == null) return;

            var board = Siege != null ? Siege.Siege : null;
            int wave = board != null ? board.Wave : 0;

            Flow.Modal<ShuffleChoiceOverlay>(v =>
            {
                v.Hand = _run.Open;
                v.Held = _run.Build;
                v.Wave = wave;
                v.OnPick = Take;
                v.RefreshPrice = RefreshPrice;
                v.OnRefresh = Refresh;
                v.RefreshOdds = () => ShuffleOdds.ForRefresh(_run);
            });
        }

        /// <summary>
        /// What the next refresh of this run costs (MODES.md 59p), or nought when the deck has
        /// nothing fresh to deal - the panel then draws no key. Read off the run on the panel's
        /// every build, so the doubled price is the price drawn.
        /// </summary>
        long RefreshPrice()
            => _run != null && !RunOver && _run.CanRefresh ? _run.RefreshPrice : 0L;

        /// <summary>
        /// Pays for a refresh and deals it onto the table, answering the new hand, or null when
        /// nothing was bought. The run is held under the panel the whole time, so the hill does
        /// not walk while the player weighs a second hand.
        /// </summary>
        IReadOnlyList<ShuffleCard> Refresh()
        {
            if (_run == null || RunOver || Level == null) return null;

            if (!ShuffleRefresh.TryBuy(_run, Level.Id)) return null;

            // The hand the gems bought is the hand a resume puts back, never the one before it.
            Suspend();
            return _run.Open;
        }

        /// <summary>
        /// A card was tapped: into the build, and the board re-reads the line.
        ///
        /// <b>The build writes the boosts the board already holds</b>, so nothing is re-attached;
        /// what has to be pushed is the ward figures a build changes, through
        /// <c>SiegeBoard.Refit</c>, because a ward's health and capacity are state.
        /// </summary>
        void Take(ShuffleCard card)
        {
            if (_run == null || !_run.Take(card)) return;

            var board = Siege != null ? Siege.Siege : null;
            if (board != null) board.Refit();

            // After the refit, so the posts are suspended at the health the card just gave them.
            Suspend();

            Audio.Sfx("unlock", .6f, 1f);

            // **A taken card says nothing more** (owner, 2026-10-14: no toast on a pick; the next
            // hand's tier line counts the copies). **A card that finished a secret or an evolution is celebrated**
            // (MODES.md 59n) - one panel each, the grandest last.
            var unlocked = _run.Build.Unlocked;
            if (unlocked.Count > 0) Celebrate(new List<ShuffleBond>(unlocked), 0);

            // Once in a player's life, after the first card a run takes: the run now holds
            // something worth keeping, and the lesson waits behind any celebration above.
            Teaching.Teach(Mechanic.ShuffleSave);
        }

        /// <summary>
        /// Raises the unlock panel for <paramref name="bonds"/>[<paramref name="at"/>] and, when
        /// it is put away, the next. Each holds the run while it stands, so the hill never walks
        /// between two of them. Nothing over a run that has already ended.
        /// </summary>
        void Celebrate(List<ShuffleBond> bonds, int at)
        {
            if (bonds == null || at >= bonds.Count || RunOver || _run == null) return;

            var tuning = _run.Tuning;
            Flow.Modal<ShuffleUnlockOverlay>(v =>
            {
                v.Bond = bonds[at];
                v.CardOf = tuning.Find;
                v.Held = _run.Build;
                v.OnDone = () => Celebrate(bonds, at + 1);
            });
        }

        /// <summary>
        /// Banks this run's cleared waves independently of its best wave and grade.
        ///
        /// <para>
        /// <b>Off the board rather than <paramref name="count"/></b>, for the Infinite lane's
        /// reason: the count arrives floored at one (a graded nought is not a grade), so a run
        /// that saw off no wave would publish a wave. A Shuffle run opens at wave one
        /// (<see cref="Ruled"/> refuses a checkpoint) or, resumed, on the wave its last hand left
        /// it at - so either way <c>WavesCleared</c> is how far the run got.
        /// </para>
        /// </summary>
        protected override void Finished(int count)
        {
            _graded = null;
            _gradedBest = false;
            _bestBefore = null;

            var board = Siege != null ? Siege.Siege : null;
            if (board == null || Level == null) return;

            Banked(ShuffleRewards.CreditsFor(BankRewards()));
            ShuffleLedger.Record(Level.Id, board.WavesCleared);

            // **The run's grade, banked beside its wave** (MODES.md 59o): what the build ended on,
            // graded on this build's floors, kept as the level's best-graded run when it is one.
            // Read before it is banked, so the panel can say what the best was.
            if (_run == null) return;

            _graded = ShuffleRecord.Of(_run.Build, board.WavesCleared);
            if (_graded == null) return;

            _bestBefore = ShuffleLedger.TopFor(Level.Id);
            _gradedBest = ShuffleLedger.RecordRun(Level.Id, _graded);
        }

        /// <summary>This run's graded record, and what the level's best was before it. Null when no card was taken.</summary>
        ShuffleRecord _graded, _bestBefore;
        bool _gradedBest;

        long BankRewards()
        {
            var board = Siege != null ? Siege.Siege : null;
            if (_rewardsBanked || board == null || Level == null) return 0L;
            _rewardsBanked = true;

            // A resumed run is paid for the waves it cleared before this board opened as well
            // (MODES.md 59s): nothing paid for them when the app closed, and the snapshot that
            // carried them is already gone, so they are paid here exactly once.
            return ShuffleLedger.Bank(Level.Id, board.WavesThisRun + _carried);
        }

        protected override void NoteAbandoned(string reason)
        {
            Forget();
            XpBoost.Bank(ShuffleRewards.XpFor(BankRewards()));
            base.NoteAbandoned(reason);
        }

        /// <summary>The victory panel carries the graded run as its own section (MODES.md 59o).</summary>
        protected override void Dress(WinOverlay panel)
        {
            if (panel == null || _graded == null) return;

            panel.ShuffleRun = _graded;
            panel.ShuffleNewBest = _gradedBest;
            panel.ShuffleBestBefore = _bestBefore;
        }

        /// <summary>
        /// The run's build, for the analytics the base records beside its own and for a fixture.
        /// Null before the first board is dealt.
        /// </summary>
        public ShuffleRun Run => _run;

        protected override void RunEnded(bool won)
        {
            // Let go before anything is paid, for Drop's reason.
            Forget();

            base.RunEnded(won);

            var board = Siege != null ? Siege.Siege : null;

            // A loss still pays waves already cleared. Wins bank in Finished, where the
            // result measures the delta and RunLedger applies the boost once to all its XP.
            if (!won) XpBoost.Bank(ShuffleRewards.XpFor(BankRewards()));

            if (board == null || Level == null || _run == null) return;

            ShuffleAnalytics.TrackRun(Level, board.WavesCleared, _run);
        }
    }
}
