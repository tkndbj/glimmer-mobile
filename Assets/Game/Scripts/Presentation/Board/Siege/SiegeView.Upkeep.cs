using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Tearing the board down, and the small things that outlive a frame.</summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ upkeep
        protected override void Repaint()
        {
            if (_board == null) return;

            // The model is the authority on what is standing where; anything the animation left
            // behind is put right here rather than trusted.
            for (int i = 0; i < _gems.Count && i < Width * Height; i++)
            {
                if (_gems[i] == null) _gems[i] = Mint(Face(i));

                Dress(i);
                Place(i);
            }
        }

        /// <summary>
        /// Reads the verdict and ends the run if it says so.
        ///
        /// <b>Its own rather than <c>ProtoView.Settle</c></b>, and for one reason: the base asks
        /// whether the first move has landed, because everywhere else a run cannot be lost before
        /// it has been played. Here the hill walks whether or not anybody has touched a gem, so a
        /// player who watches the wards fall without moving has genuinely lost — and a run that
        /// simply never ended would be worse than either.
        /// </summary>
        /// <summary>
        /// What a continue buys here, which is the ward line rather than an allowance.
        ///
        /// <para>
        /// This mode has no move meter to top up (invariant 37b), so the base's <c>Run.Grant</c>
        /// would be a no-op on an unbounded budget and the board would come back exactly as lost
        /// as it went in — a charge for nothing. What is bought is
        /// <c>SiegeBoard.Rally</c>: every fallen turret up at full health, keeping the rank its
        /// cogs bought, with the hill standing exactly where it stood.
        /// </para>
        /// <para>
        /// It does not touch the grade. What the purchase owes the graded count is charged by
        /// <c>SiegeScreen.ContinueWith</c> before this runs, because that is a rule about money
        /// rather than about the board.
        /// </para>
        /// </summary>
        protected override void Granted(int wards)
        {
            if (_board == null || _board.Rally(wards) <= 0) return;

            Rallied();
        }

        /// <summary>
        /// A siege re-reads its own verdict after a continue, for the reason it reads its own
        /// verdict at all: the shared one asks whether the first move has landed, and here the
        /// hill walks whether or not anybody has touched a gem.
        /// </summary>
        protected override void Rejudge() => Judge();

        void Judge()
        {
            if (Over || Run == null) return;

            var verdict = Run.Verdict;

            if (verdict.IsWon)
            {
                // **A boss is watched dying before the run is allowed to end.** Reported from
                // play: a cascade big enough to finish the hill killed the boss and the victory
                // panel was up before anything came apart, so the player was told they had won
                // and never saw the thing they beat. The win is already decided — this only
                // holds the *telling* of it, and `Update` re-asks every frame, so nothing can be
                // stranded by it.
                //
                // It is a countdown rather than a callback for the reason `Fall` is five
                // staggered explosions rather than one: the death is a handful of tweens with no
                // single end, and a latch that outlives the last of them is the only version that
                // cannot end early.
                if (_felling > 0f)
                {
                    _felling -= Time.unscaledDeltaTime;
                    return;
                }

                Over = true;
                Finishing?.Invoke();
                StartCoroutine(Triumph());
                return;
            }

            if (verdict.Ending != ProtoEnding.Stuck) return;

            Over = true;
            StartCoroutine(Ruin());
        }

        protected override IEnumerator Ruin()
        {
            Audio.Sfx("shatter", .85f, .7f);
            ShakeBoard(26f);
            Flow.Flash(new Color(1f, .28f, .24f), .5f, .5f);

            yield return base.Ruin();
        }

        /// <summary>
        /// Points at the first match, once. <c>ProtoView</c>'s hand rings a cell; this mode's
        /// first move is a <em>drag</em>, so the hand is put on a gem that has one to make.
        /// </summary>
        public override void CoachTap()
        {
            HideCoach();

            if (_board == null) return;

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int here = y * Width + x;

                    if (x + 1 < Width && _board.Lines(here, here + 1)) { Point(here); return; }
                    if (y + 1 < Height && _board.Lines(here, here + Width)) { Point(here); return; }
                }
        }

        void Point(int cell)
        {
            var node = UIKit.Node("Coach", _fx);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(Cell, Cell);
            node.anchoredPosition = CentreOf(cell);

            CoachHand.Tap(node, Vector2.zero, Pal.Cream, this);
            Tween.After(3.4f, () => { if (node) Destroy(node.gameObject); });
        }
    }
}
