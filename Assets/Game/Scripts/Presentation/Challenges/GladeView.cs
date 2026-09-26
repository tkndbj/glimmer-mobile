using System.Collections;
using GlimmerGrove.Challenges;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The glade under the hill: the mode's own board, standing over the challenge's puzzle.
    ///
    /// <para>
    /// <b>It is a real <see cref="BoardView"/> over the real <see cref="Puzzle"/></b> — the
    /// tutorial's shape (invariant 53: a real <c>SiegeView</c> over a real <c>SiegeBoard</c>),
    /// asked of the other mode. The conduits, the heart-crystals, the sleeping critters and
    /// their halos, the light walking the network on every turn and the fanfare when the last
    /// one wakes are all the glade's, drawn by the glade's own code, so nothing a player
    /// learns here has to be translated onto a different-looking board if the mode ever
    /// returns to the map. The owner's verdict on the first cut (2026-09-23), which redrew
    /// the board in the challenge's procedural pieces, was that it "doesn't look like my
    /// glade game mode at all" — and that was the whole of what was wrong with it.
    /// </para>
    /// <para>
    /// <b>The one seam is the tap.</b> <see cref="BoardView.Referee"/> hands each tap on a
    /// turnable tile here instead of applying it; it goes through the screen's one door
    /// (<see cref="PuzzleView.Send"/>), the run turns the tile through
    /// <see cref="GladePuzzle.Apply"/>, feeds the line and walks the hill, and the board is
    /// then told to <see cref="BoardView.Follow"/> what the model did — the spin, the light,
    /// the wake sounds, all its own. A latched screen simply does not send, so a tap while
    /// the hill is walking costs nothing and draws nothing.
    /// </para>
    /// <para>
    /// <b>The win is the glade's fanfare, and the curtain waits for it.</b> The board
    /// celebrates itself the moment the last critter wakes; <see cref="Animate"/> yields until
    /// <see cref="BoardView.OnSolved"/> says the fanfare has settled (bounded, so a board that
    /// never says so cannot strand the run), and the screen's curtain then arrives without
    /// its own flash and confetti (<see cref="CelebratesItself"/>).
    /// </para>
    /// </summary>
    public sealed class GladeView : PuzzleView
    {
        /// <summary>
        /// The floor's tint. It was the first chapter's slate, which is what the glade was
        /// authored on - and, once the band became a framed panel of nearly that blue
        /// (2026-09-26), a floor nobody could see. Sunk to a deeper navy so it reads as the
        /// well inside the frame, as the other three boards' dark plate does; the arms and the
        /// hub are derived from it by <c>Pal.BoardTheme.From</c> and stay blue-grey.
        /// </summary>
        static readonly Color Slate = new Color(.035f, .086f, .184f, 1f);

        /// <summary>The most a fanfare is waited on, in seconds, whatever the board says.</summary>
        const float FanfareMost = 9f;

        GladePuzzle _glade;
        BoardView _board;
        bool[] _before;
        int _tapped = -1;
        bool _settled;

        /// <summary><see cref="BoardView"/>'s own pitch cap, so the band arithmetic and the board agree.</summary>
        protected override float MaxCell => 190f;

        protected override bool DrawsPlate => false;
        public override bool CelebratesItself => true;

        protected override void Build()
        {
            _glade = (GladePuzzle)Run.Puzzle;

            _board = Host.gameObject.AddComponent<BoardView>();
            _board.Referee = Tapped;
            _board.OnSolved = () => _settled = true;

            // A critter is a gem here (the owner's instruction, 2026-09-23): the gem the turret
            // it feeds fires, dimmed while it sleeps and lit when its light reaches it.
            _board.LampFace = energy => ChallengeArt.Gem(GladePuzzle.LaneOf(energy));

            // Into the frame's inside, not the band: the board sizes its own pitch off the
            // rect it is given, and the frame has already taken its rim off the band.
            _board.Build(Inner, _glade.Board, Pal.BoardTheme.From(Slate));
        }

        void Tapped(int cell)
        {
            _before = _board.CaptureLit();
            _tapped = cell;
            Send(ChallengeInput.Tap(cell));
        }

        public override void Repaint()
        {
            if (_board) _board.SyncViews();
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            if (!_board) yield break;

            if (_tapped >= 0)
            {
                _board.Follow(_tapped, _before);
                _tapped = -1;
                _before = null;
            }
            else
            {
                _board.SyncViews();
            }

            if (_glade.Solved)
            {
                // The fanfare owns the board from here; the curtain is raised when it settles.
                float until = Time.unscaledTime + FanfareMost;
                while (!_settled && Time.unscaledTime < until) yield return null;
            }

            // Nothing else is waited on. The spin and the light walk are the board's own
            // tweens and run whether or not the next tap has landed — a tap held back until
            // they finished was the "sometimes it doesn't rotate" the owner reported
            // (2026-09-26): the screen was latched for the spin and then for the hill, and
            // every tap inside that second was thrown away.
        }

        public override void Refuse()
        {
            // A rooted or inert tile is refused, and drawn refused, by the board itself
            // before the tap ever reaches the run; what reaches here is the run's own
            // refusal, which the shared shake says.
            base.Refuse();
        }
    }
}
