using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
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
    /// <b>It pays nothing the Infinite lane pays.</b> That lane banks a best wave onto a public
    /// board and a tally that pays XP and credits (<c>EndlessLedger</c>, <c>EndlessCoins</c>);
    /// this one records the level's own record like every other level, pays its stars once
    /// through the star ledger, and banks its best wave in <see cref="ShuffleLedger"/>, which
    /// is the Shuffle board's figure and pays nothing (invariant 19l). A run here is worth
    /// exactly what a level is worth, which is invariant 20a's bargain collected again - and the
    /// reason <see cref="Finished"/> replaces the siege screen's banking rather than extending it.
    /// </para>
    /// </summary>
    public sealed class ShuffleScreen : SiegeScreen
    {
        ShuffleRun _run;

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
        /// </summary>
        protected override ProtoLevelRules Ruled(ProtoLevelRules rules) => rules;

        protected override void Play()
        {
            base.Play();
            Reseat();
        }

        protected override void Rewind()
        {
            base.Rewind();
            Reseat();
        }

        public override void RetryAfterDefeat()
        {
            base.RetryAfterDefeat();
            Reseat();
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

            uint seed = Seed != 0u ? Seed : ShuffleRun.SeedNow();
            Seed = 0u;

            _run = new ShuffleRun(seed);
            board.Boosts = _run.Build.Boosts;
            board.Refit();
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

            if (!running || _run == null || Siege == null || RunOver) return;

            var board = Siege.Siege;
            if (board == null || !_run.Owed(board)) return;

            var hand = _run.Deal();
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
            });
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

            Audio.Sfx("unlock", .6f, 1f);

            int copies = _run.Build.Copies(card);
            string name = Loc.Get(card.NameKey);

            Scenery.Toast(Safe, copies > 1 ? Loc.Format("ui.shuffle.taken_again", name, copies)
                                           : Loc.Format("ui.shuffle.taken", name),
                          ShuffleChoiceOverlay.TintOf(card.Tier), 2.2f);
        }

        /// <summary>
        /// Banks the run's best onto the Shuffle board's ledger and nothing else. See the class
        /// note: the Infinite lane's ledger, tally and claim are that lane's, and a Shuffle wave
        /// pays no currency.
        ///
        /// <para>
        /// <b>Off the board rather than <paramref name="count"/></b>, for the Infinite lane's
        /// reason: the count arrives floored at one (a graded nought is not a grade), so a run
        /// that saw off no wave would publish a wave. A Shuffle run always opens at wave one
        /// (<see cref="Ruled"/> refuses a checkpoint), so <c>WavesCleared</c> is how far it got.
        /// </para>
        /// </summary>
        protected override void Finished(int count)
        {
            var board = Siege != null ? Siege.Siege : null;
            if (board == null || Level == null) return;

            ShuffleLedger.Record(Level.Id, board.WavesCleared);
        }

        /// <summary>
        /// The run's build, for the analytics the base records beside its own and for a fixture.
        /// Null before the first board is dealt.
        /// </summary>
        public ShuffleRun Run => _run;

        protected override void RunEnded(bool won)
        {
            base.RunEnded(won);

            var board = Siege != null ? Siege.Siege : null;
            if (board == null || Level == null || _run == null) return;

            ShuffleAnalytics.TrackRun(Level, board.WavesCleared, _run);
        }
    }
}
