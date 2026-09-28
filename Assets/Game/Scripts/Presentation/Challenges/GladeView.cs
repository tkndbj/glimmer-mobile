using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The glade under the hill: the mode's own board, standing over the challenge's puzzle.
    ///
    /// <para>
    /// <b>It is a real <see cref="BoardView"/> over the real <see cref="Puzzle"/></b> - the
    /// tutorial's shape (invariant 53: a real <c>SiegeView</c> over a real <c>SiegeBoard</c>),
    /// asked of the other mode. The conduits, the heart-crystals, the sleeping critters and
    /// their halos, the light walking the network on every turn and the fanfare when the last
    /// one wakes are all the glade's, drawn by the glade's own code, so nothing a player
    /// learns here has to be translated onto a different-looking board if the mode ever
    /// returns to the map. The owner's verdict on the first cut (2026-09-23), which redrew
    /// the board in the challenge's procedural pieces, was that it "doesn't look like my
    /// glade game mode at all" - and that was the whole of what was wrong with it.
    /// </para>
    /// <para>
    /// <b>The one seam is the tap.</b> <see cref="BoardView.Referee"/> hands each tap on a
    /// turnable tile here instead of applying it; it goes through the screen's one door
    /// (<see cref="PuzzleView.Send"/>), the run turns the tile through
    /// <see cref="GladePuzzle.Apply"/>, feeds the line and walks the hill, and the board is
    /// then told to <see cref="BoardView.Follow"/> what the model did - the spin, the light,
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
        protected override float MaxCell => BoardView.MaxPitch;

        /// <summary>
        /// The band this board fills at the pitch the width allows, asked in
        /// <see cref="BoardView.Build"/>'s own terms: its pad on every side of the grid, inside
        /// the frame. The shared answer assumes the shared plate, whose rim is a fraction of a
        /// cell, and on a tall glade that under-asked by a few units, so the board came out
        /// height-bound and a hair smaller than its width allowed. Asked exactly, a glade of any
        /// size is laid out at the widest pitch the phone has room for and the hill takes
        /// precisely what is left (<c>ChallengeScreen.BuildBands</c>) - which is what lets a
        /// hard 7x6 glade stand under a shorter hill than a 7x4 one without either being cramped.
        /// </summary>
        public override float BandWanted(int columns, int rows, float hostWidth)
        {
            float pitch = Mathf.Min((hostWidth - FrameSide * 2f - BoardView.Pad * 2f) / columns, BoardView.MaxPitch);
            return rows * pitch + BoardView.Pad * 2f + FrameSide * 2f;
        }

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

            // And every light is its gem's colour (the owner's instruction, 2026-09-26): red,
            // green, blue and amber exactly as the turrets burn, where the mode's own wheel paints
            // green light yellow. Dark stays the mode's dark.
            _board.Tint = Light;

            // Into the frame's inside, not the band: the board sizes its own pitch off the
            // rect it is given, and the frame has already taken its rim off the band.
            _board.Build(Inner, _glade.Board, Pal.BoardTheme.From(Slate));
        }

        /// <summary>A light's colour on this board: its turret's tint, or the mode's paint for dark.</summary>
        static Color Light(int energy)
        {
            int lane = GladePuzzle.LaneOf(energy);
            return lane >= 0 ? ChallengeArt.Tint(lane) : Pal.EnergyColour(energy);
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
            // tweens and run whether or not the next tap has landed - a tap held back until
            // they finished was the "sometimes it doesn't rotate" the owner reported
            // (2026-09-26): the screen was latched for the spin and then for the hill, and
            // every tap inside that second was thrown away.
        }

        /// <summary>The board has swept in and is taking input: <see cref="BoardView"/>'s own latch.</summary>
        public override bool Landed => _board && !_board.Locked;

        /// <summary>
        /// The most lessons one opening raises. A hard glade can carry a rooted tile, a crossing,
        /// a briar and a taproot at once, and four panels before the first tap is a wall of text
        /// in front of a puzzle; a lesson not raised today is raised the next time a board
        /// carries it, because <c>ScreenLessons.Offer</c> only ever skips what was seen.
        /// </summary>
        const int LessonsAtOnce = 2;

        /// <summary>
        /// The glade's own lessons, asked of the board exactly as the mode asks them
        /// (<see cref="MechanicScan.Taught"/>, in <see cref="Mechanic.TeachingOrder"/>), each
        /// ringing the first tile that shows it (invariant 6b). <b>The glade chapters are
        /// hidden</b>, so for nearly every player this board is the first place a crossing, a
        /// briar, a rooted tile or a taproot is ever met - and a taproot met
        /// untaught reads as a tap that turned the wrong tile. The same ids as the mode's, so
        /// a lesson learnt here is never taught again on the map, and the other way round.
        /// Only the four a challenge row can carry are asked: the move budget and fragile
        /// conduits are refused at read (<see cref="GladePuzzle.Fault"/>) or are not this
        /// screen's, and a challenge critter always wants a colour. <b>Mixing is never taught
        /// here</b>, because this board never mixes (invariant 56l): the mode's lesson says red
        /// and yellow make orange, which on this screen is a sentence about a rule that is off,
        /// and an amber critter is a sighting of that lesson all the same.
        /// </summary>
        public override void Lessons(List<ScreenLesson> into)
        {
            if (!_board) return;

            foreach (var sighting in MechanicScan.Taught(_glade.Board))
            {
                if (into.Count >= LessonsAtOnce) break;
                if (sighting.CellIndex < 0 || !Teaches(sighting.Mechanic)) continue;

                ScreenLessons.Offer(into, sighting.Mechanic, _board.TileAt(sighting.CellIndex));
            }
        }

        /// <summary>
        /// The verb, ringing the board, then every one of the mode's tile lessons this board
        /// carries - all of them rather than <see cref="LessonsAtOnce"/>, because a player who
        /// pressed the key asked for them.
        /// </summary>
        public override void Review(List<ScreenLesson> into)
        {
            if (!_board) return;

            ScreenLessons.Add(into, Mechanic.GladeWake, Inner);

            foreach (var sighting in MechanicScan.Taught(_glade.Board))
            {
                if (sighting.CellIndex < 0 || !Teaches(sighting.Mechanic)) continue;
                ScreenLessons.Add(into, sighting.Mechanic, _board.TileAt(sighting.CellIndex));
            }
        }

        static bool Teaches(Mechanic mechanic)
            => mechanic.Equals(Mechanic.RootedTile)
            || mechanic.Equals(Mechanic.Crossing) || mechanic.Equals(Mechanic.Briar)
            || mechanic.Equals(Mechanic.BoundConduit);

        public override void Refuse()
        {
            // A rooted or inert tile is refused, and drawn refused, by the board itself
            // before the tap ever reaches the run; what reaches here is the run's own
            // refusal, which the shared shake says.
            base.Refuse();
        }
    }
}
