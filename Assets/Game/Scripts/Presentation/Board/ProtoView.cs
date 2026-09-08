using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The furniture five boards share: the plate they are drawn on, the grid they are laid out
    /// in, the latches that decide whether a finger does anything, and the two endings.
    ///
    /// <para>
    /// <b>Shared by inheritance, exactly as <c>ModeScreen</c> shares the chrome.</b> Each of the
    /// five draws something completely different — a cairn, a network of channels, a wall of
    /// blooms, an arbour, a warren — but every one of them is a rectangle of cells on a dark
    /// plate that takes taps, animates in waves, and finishes with either a celebration or a
    /// board going out. Writing those five times is five places for "a run may only be decided
    /// once" to stop being true.
    /// </para>
    /// <para>
    /// <b>What a subclass owns is everything a player looks at.</b> This class deliberately draws
    /// nothing except the plate: it sizes the grid, holds the run, and offers the spectacle
    /// helpers. A mode that wanted to be drawn some other way could ignore all of it and still
    /// inherit the latches, which is the part that is dangerous to get wrong.
    /// </para>
    /// </summary>
    public abstract class ProtoView : MonoBehaviour
    {
        // ------------------------------------------------------------------ what the screen hears
        /// <summary>Raised whenever anything the readouts count has moved.</summary>
        public Action Changed { get; set; }

        /// <summary>The board is finished. Raised once, after the last animation has played out.</summary>
        public Action Solved { get; set; }

        /// <summary>The run is over and lost. The screen reads <see cref="Run"/>'s verdict for which way.</summary>
        public Action Lost { get; set; }

        /// <summary>The first move has landed, so the run is now owed for.</summary>
        public Action Committed { get; set; }

        /// <summary>
        /// The closing sequence has begun, so nothing else may end this run.
        ///
        /// <c>FallView.Finishing</c>'s rule, and it earns its place here for the same reason: a
        /// board is decided the moment the last goal is met, and the panel arrives a beat later
        /// while the celebration is still playing. Everything that could still end the run has to
        /// stop at the first of those two moments, not the second.
        /// </summary>
        public Action Finishing { get; set; }

        /// <summary>
        /// A tap that could not be honoured, for the one refusal each mode has that the board
        /// itself cannot explain. Rate-limiting is the screen's, not this class's.
        /// </summary>
        public Action Refused { get; set; }

        // ------------------------------------------------------------------ latches
        /// <summary>Held by a lesson, a pause menu or an ending. Nothing takes input while it is set.</summary>
        public bool Locked { get; set; }

        /// <summary>Held by the screen until the run is allowed to advance. Starts true.</summary>
        public bool Held { get; set; } = true;

        public ProtoRun Run { get; private set; }

        protected bool Busy;
        protected bool Over;

        /// <summary>Whether the board is in a state where a tap would mean anything.</summary>
        public bool TakingInput => Run != null && !Locked && !Busy && !Over;

        /// <summary>As <see cref="TakingInput"/>, and the screen has let the run begin.</summary>
        public bool Playable => TakingInput && !Held;

        bool _committed;

        // ------------------------------------------------------------------ geometry
        /// <summary>Air between the board and the edges of the host, in canvas units.</summary>
        protected const float Margin = 18f;

        /// <summary>The plate the board is drawn on, and the parent of everything a subclass adds.</summary>
        protected RectTransform Plate { get; private set; }

        /// <summary>The grid's own node, centred on the plate. Cells are placed inside it.</summary>
        protected RectTransform Field { get; private set; }

        /// <summary>One cell's side, in canvas units. Square, always.</summary>
        protected float Cell { get; private set; }

        protected int Width { get; private set; }
        protected int Height { get; private set; }

        protected ProtoLevelRules Rules { get; private set; }

        /// <summary>
        /// Where the centre of a cell sits inside <see cref="Field"/>.
        ///
        /// <para>
        /// <b>Virtual, because a grid of cells is a drawing decision and not a rule.</b> Every
        /// mode built on this shape until the Iron Quarry laid its board out in squares, so this
        /// was a fact; a quarry floor is drawn in isometry, and the only thing that had to move
        /// for it was where a cell is. Nothing in <see cref="ProtoRun"/>, <c>ProtoSearch</c> or
        /// any mode's own rules knows or could know — a board is a rectangle of characters
        /// whichever way it is painted.
        /// </para>
        /// <para>
        /// It has to be this rather than each mode's private helper, because the lesson anchors
        /// (<see cref="AnchorAt"/>) point at cells: a mode that laid its cells out privately
        /// would have its tips ringing empty air, and a tip pointing at nothing is worse than no
        /// tip at all.
        /// </para>
        /// </summary>
        protected virtual Vector2 CentreOf(int index)
        {
            int x = index % Width, y = index / Width;
            return new Vector2((x - (Width - 1) * .5f) * Cell,
                               ((Height - 1) * .5f - y) * Cell);
        }

        /// <summary>
        /// How wide one cell may be, given the room the host has. Square by default.
        ///
        /// Split out for <see cref="CentreOf"/>'s reason and measured the same way: an
        /// isometric board's footprint is not <c>Width x Height</c> cells, it is a diamond
        /// <c>(Width + Height) / 2</c> tiles across, so a mode that lays out differently has to
        /// be asked how big a cell can be rather than told.
        /// </summary>
        protected virtual float Fit(Vector2 room)
            => Mathf.Min((room.x - Margin * 2f) / Width, (room.y - Margin * 2f) / Height);

        /// <summary>How much room the laid-out board takes, in canvas units.</summary>
        protected virtual Vector2 Span => new Vector2(Width * Cell, Height * Cell);

        protected int IndexOf(int x, int y) => y * Width + x;
        protected int XOf(int index) => index % Width;
        protected int YOf(int index) => index / Width;

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Builds the board into the host and starts a fresh run.
        ///
        /// <b>Idempotent by demolition</b>: a restart calls this again on a view that is already
        /// holding a board, so the first thing it does is take the old one down. Anything a
        /// subclass caches has to be rebuilt in <see cref="Compose"/> rather than kept.
        /// </summary>
        public void Begin(RectTransform host, ProtoLevelRules rules, int budget)
        {
            Tween.KillAll(this);
            StopAllCoroutines();

            if (Plate != null) Destroy(Plate.gameObject);

            // Both hang off the field, which has just gone, so a stale reference would hand a
            // lesson a destroyed node on the restart it is most likely to be shown.
            _verbAnchor = null;
            _friendAnchor = null;

            Rules = rules;
            Run = new ProtoRun(rules.Fresh(), budget);

            Busy = false;
            Over = false;
            _committed = false;

            // **And handed back, which is the half that was missing.** Every way a run ends
            // latches this board — `Settle` latches it, and the screen's `Concede` and `Lose`
            // each latch it again before their panel goes up — so a rebuild that left the flag
            // alone produced a fresh board behind a latch belonging to a run that no longer
            // existed. It is `FallView`'s bug, reported from play there and fixed there, and this
            // class inherited the shape without the fix: run out of moves, decline the offer,
            // press TRY AGAIN, and every tap is ignored for the rest of the screen's life.
            //
            // It belongs here rather than in the caller because there are three callers and only
            // one of them unlatches: `RunScreen.RestartLevel` runs `Rewind(); Resume();` and the
            // Resume does it, while `RetryAfterDefeat` is a mode's own override with no such
            // pairing. A rule that holds only when the caller remembers is one the fourth caller
            // breaks.
            Locked = false;

            var grid = rules.Grid;
            Width = grid.Width;
            Height = grid.Height;

            Cell = Mathf.Floor(Fit(host.rect.size));
            if (Cell < 8f) Cell = 8f;

            var span = Span;

            Plate = UIKit.Node("Plate", host);
            Plate.anchorMin = Plate.anchorMax = new Vector2(.5f, .5f);
            Plate.sizeDelta = new Vector2(span.x + Margin * 2f, span.y + Margin * 2f);
            Plate.anchoredPosition = Vector2.zero;

            var back = Plate.gameObject.AddComponent<Image>();
            back.sprite = Art.Round(34);
            back.type = Image.Type.Sliced;
            back.color = Pal.Board;
            back.raycastTarget = false;

            Field = UIKit.Node("Field", Plate);
            Field.anchorMin = Field.anchorMax = new Vector2(.5f, .5f);
            Field.sizeDelta = span;
            Field.anchoredPosition = Vector2.zero;

            Compose();
            Repaint();
            Enter();

            Changed?.Invoke();
        }

        /// <summary>Builds everything this mode draws. The plate and the field already exist.</summary>
        protected abstract void Compose();

        /// <summary>Puts what is drawn back in step with the board, without animating anything.</summary>
        protected abstract void Repaint();

        /// <summary>
        /// The cell a lesson about the verb should ring, or -1.
        ///
        /// Two anchors rather than a list, because every one of these modes teaches exactly two
        /// things: what a tap does, and what your friend does.
        /// </summary>
        public abstract int VerbCell { get; }

        /// <summary>The cell the companion stands on, or -1.</summary>
        public abstract int FriendCell { get; }

        RectTransform _verbAnchor, _friendAnchor;

        public RectTransform VerbAnchor => AnchorAt(VerbCell, ref _verbAnchor);
        public RectTransform FriendAnchor => AnchorAt(FriendCell, ref _friendAnchor);

        /// <summary>
        /// An empty node over a cell, for a lesson to point at.
        ///
        /// <b>Made once and kept.</b> The lessons are asked again every time the readouts change,
        /// so building one per call is a node per move for the life of the run - and the one it
        /// hands back is the one the tip is already ringing, so replacing it mid-lesson would
        /// leave the ring behind on an orphan.
        /// </summary>
        RectTransform AnchorAt(int cell, ref RectTransform kept)
        {
            if (kept != null) return kept;
            if (Field == null || cell < 0 || cell >= Width * Height) return null;

            kept = UIKit.Node("Anchor", Field);
            kept.anchorMin = kept.anchorMax = new Vector2(.5f, .5f);
            kept.sizeDelta = new Vector2(Cell, Cell);
            kept.anchoredPosition = CentreOf(cell);
            return kept;
        }

        // ------------------------------------------------------------------ arriving
        /// <summary>How long the board takes to arrive. Lessons wait for it.</summary>
        public const float Entrance = .46f;

        void Enter()
        {
            Plate.localScale = Vector3.one * .9f;
            Tween.Scale(Plate, 1f, Entrance, Ease.OutBack);

            var group = UIKit.Group(Plate);
            group.alpha = 0f;
            Tween.Fade(group, 1f, Entrance * .7f);
        }

        // ------------------------------------------------------------------ playing
        /// <summary>
        /// Charges the run for a move that landed, notes the first one, and re-reads the verdict.
        ///
        /// <b>Every mode goes through here and none of them touches <see cref="Run"/> directly.</b>
        /// A run charged for an input that could not be honoured is a run that quietly costs a
        /// player a move for touching stone, and a run charged twice for one input is worse.
        /// </summary>
        protected void Took(int worth)
        {
            Run.Took(worth);
            Commit();
        }

        /// <summary>
        /// Charges the run for a utility that landed, in the unit the mode is graded in.
        ///
        /// <b>Every mode goes through here and none of them touches <see cref="Run"/> directly</b>
        /// — <see cref="Took"/>'s rule, for the resource that costs real money to replace. What
        /// the charge is worth is the mode's (see <c>SiegeUtility</c>); what this owns is that it
        /// happens exactly once and that the run notices.
        /// </summary>
        protected void Charged(int moves)
        {
            Run.Charged(moves);
            Commit();
        }

        /// <summary>
        /// Marks the run as touched and repaints. The half <see cref="Took"/> and
        /// <see cref="Charged"/> share, written once so the two cannot drift about what
        /// committing means — which is the flag a heart is charged against.
        /// </summary>
        void Commit()
        {
            if (!_committed)
            {
                _committed = true;
                Committed?.Invoke();
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Reads the verdict and ends the run if it says so.
        ///
        /// Called at the end of every move's animation and after a continue is granted — never
        /// mid-cascade, because a board halfway through a collapse is not a board anybody should
        /// be judged on.
        /// </summary>
        protected void Settle()
        {
            if (Over || Run == null) return;

            var verdict = Run.Verdict;

            if (verdict.IsWon)
            {
                Over = true;
                Finishing?.Invoke();
                StartCoroutine(Triumph());
                return;
            }

            if (!verdict.EndsTheRun(true, _committed)) return;

            Over = true;
            StartCoroutine(Ruin());
        }

        /// <summary>
        /// Deals more moves, because a continue was paid for.
        ///
        /// The verdict is asked again rather than assumed: if a grant somehow left the run lost,
        /// the fail state fires again and the player is asked again rather than silently left on a
        /// dead board.
        /// </summary>
        public void Grant(int moves)
        {
            if (Run == null) return;

            Run.Grant(moves);
            Over = false;
            Locked = false;

            // Ruin dimmed the field on the way to the defeat panel. A board handed back at 45%
            // is a board that looks lost while it is being played on, and nothing else would ever
            // put it right - Repaint redraws the pieces, not the group they hang in.
            var group = UIKit.Group(Field);
            Tween.KillAll(group);
            group.alpha = 1f;

            Repaint();
            Changed?.Invoke();
            Settle();
        }

        // ------------------------------------------------------------------ the endings
        /// <summary>
        /// The board is finished. One celebration, sounded once.
        ///
        /// <b>No fanfare on the screen's side.</b> This sounds <c>win</c> and then waits a beat
        /// before handing control back, so a second copy on the panel would be the same clip twice
        /// a third of a second apart — a flam and six decibels, not a bigger celebration.
        /// </summary>
        protected virtual IEnumerator Triumph()
        {
            Audio.Sfx("win", .8f);
            Burst.Sparks(Field, Vector2.zero, Pal.Cream, 26, 240f);
            Shockwave(Vector2.zero, Pal.Cream, 3.2f, .55f);

            for (int i = 0; i < 3; i++)
            {
                Burst.Sparks(Field, new Vector2(UnityEngine.Random.Range(-Cell, Cell),
                                                UnityEngine.Random.Range(-Cell, Cell)),
                             i == 0 ? Pal.Gold : i == 1 ? Pal.Bloom : Pal.Aqua, 14, 180f);
                yield return new WaitForSeconds(.12f);
            }

            yield return new WaitForSeconds(.34f);
            Solved?.Invoke();
        }

        /// <summary>The run is lost. The board goes out rather than exploding.</summary>
        protected virtual IEnumerator Ruin()
        {
            Audio.Sfx("blocked", .6f, .82f);
            ShakeBoard(16f);

            var group = UIKit.Group(Field);
            Tween.Value(1f, .45f, .5f, v => group.alpha = v, Ease.OutQuad, this);

            yield return new WaitForSeconds(.42f);
            Lost?.Invoke();
        }

        // ------------------------------------------------------------------ spectacle
        /// <summary>
        /// A ring that races outward and fades. The cheapest impact there is and the one every
        /// mode here uses, so it is written once.
        /// </summary>
        protected void Shockwave(Vector2 at, Color tint, float to, float seconds)
        {
            var ring = UIKit.Img("Wave", Field, Art.Ring(128, 8f), Pal.A(tint, .8f),
                                 new Vector2(Cell, Cell));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            Tween.Scale(ring.transform, to, seconds, Ease.OutCubic);
            Tween.Fade(ring, 0f, seconds, Ease.OutQuad).OnDone(() =>
            {
                if (ring) Destroy(ring.gameObject);
            });
        }

        /// <summary>A soft flash filling one cell, for anything that goes off in place.</summary>
        protected void Pop(Vector2 at, Color tint, float size = 1.8f, float seconds = .34f)
        {
            var flash = UIKit.Img("Pop", Field, Art.Glow(96), Pal.A(tint, .9f),
                                  new Vector2(Cell, Cell));
            flash.raycastTarget = false;
            flash.rectTransform.anchoredPosition = at;
            flash.transform.localScale = Vector3.one * .4f;

            Tween.Scale(flash.transform, size, seconds, Ease.OutCubic);
            Tween.Fade(flash, 0f, seconds, Ease.OutQuad).OnDone(() =>
            {
                if (flash) Destroy(flash.gameObject);
            });
        }

        /// <summary>Knocks the whole board about. Amount is in canvas units.</summary>
        protected void ShakeBoard(float amount)
        {
            if (Plate == null) return;
            Tween.Shake(Plate, amount, .32f);
        }

        /// <summary>
        /// A refusal the board can show for itself: the cell shakes and thumps.
        ///
        /// Every mode has taps that mean nothing, and the difference between a board that feels
        /// broken and one that feels solid is whether it answers them.
        /// </summary>
        protected void Refuse(RectTransform cell)
        {
            Audio.Sfx("blocked", .35f, 1.15f);
            if (cell != null) Tween.Shake(cell, Cell * .1f, .22f);
        }

        // ------------------------------------------------------------------ the first tap
        RectTransform _coach;

        /// <summary>
        /// Points at the first move, once, for a player who has never met this mode.
        ///
        /// Taken away by the first tap anywhere rather than by the right tap: a hand still
        /// pointing after somebody has started playing reads as the game not noticing them.
        /// </summary>
        public virtual void CoachTap()
        {
            HideCoach();

            int cell = VerbCell;
            if (Field == null || cell < 0) return;

            _coach = UIKit.Node("Coach", Field);
            _coach.anchorMin = _coach.anchorMax = new Vector2(.5f, .5f);
            _coach.sizeDelta = new Vector2(Cell, Cell);
            _coach.anchoredPosition = CentreOf(cell);

            CoachHand.Tap(_coach, Vector2.zero, Pal.Cream, this);
        }

        public void HideCoach()
        {
            if (_coach == null) return;

            Tween.KillAll(_coach);
            Destroy(_coach.gameObject);
            _coach = null;
        }

        protected virtual void OnDestroy()
        {
            Tween.KillAll(this);
        }
    }
}
