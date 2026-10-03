using System.Collections;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The field dealt again for want of a move (<see cref="SiegeTurn.Shuffled"/>), drawn as the
    /// board turning over: every gem lifts, the field wheels round its own centre as one vortex,
    /// and each gem lands in the cell the model put it in.
    ///
    /// <para>
    /// <b>Why it is drawn at all.</b> The model has always reshuffled a dead field
    /// (<c>SiegeBoard.Settle</c>), and the view used to learn of it only by repainting every cell
    /// at the end of the turn - so the whole board changed in one frame and read as a glitch.
    /// A player has to see the game helping them, and has to see where their gems went.
    /// </para>
    /// <para>
    /// <b>One vortex, one direction.</b> A shuffle drawn as forty gems taking forty straight
    /// lines is a scramble; drawn as the field wheeling one way round its middle it is a single
    /// event the eye can follow. The swirl is computed in the field's own proportions (each axis
    /// over its half extent), so the path is an oval inside the board rather than a circle that
    /// swings the corners out over the ward line, and the radius is pinched in mid-flight so the
    /// board visibly gathers and lets go. The rim leaves first, so it reads as being drawn in.
    /// </para>
    /// <para>
    /// <b>One tween per gem and nothing per gem on the board's canvas.</b> Each gem is moved,
    /// scaled and tilted by a single tween on its <c>move</c> channel, which supersedes the fall
    /// it may still be finishing; whatever else was borrowing its scale or tilt is put back first;
    /// and a tween cut short lands the gem at rest in its new cell (<c>OnAbandon</c>). The light
    /// is one glow, one caption and two rings on the effects layer, never a flash per gem on the
    /// field's canvas - which would rebuild the whole board's mesh forty times
    /// (<c>ui-effects-cost-the-canvas</c>).
    /// </para>
    /// <para>
    /// <b>The run is slowed while it plays</b> (<c>Dilate</c>, invariant 37cq): the board is not
    /// the player's for most of a second, so the hill is handed that second slowly rather than
    /// walking on them while they watch the game do something they did not ask for.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the figures
        /// <summary>
        /// The four parts of the turn-over, in seconds: the lift, the most a gem's start is held
        /// back by its distance from the middle, the flight, and the landing.
        /// </summary>
        internal const float ShuffleLift = .12f, ShuffleStagger = .16f, ShuffleFly = .46f,
                             ShuffleLand = .16f;

        /// <summary>The whole of it. Every gem has landed by this, whatever its stagger.</summary>
        internal const float ShuffleFor = ShuffleLift + ShuffleStagger + ShuffleFly + ShuffleLand;

        /// <summary>How fast the run's clock runs while the board is turning over.</summary>
        const float ShufflePace = .25f;

        /// <summary>How far in the vortex pulls a gem at the middle of its flight, as a share of its radius.</summary>
        const float ShufflePinch = .42f;

        /// <summary>How far a gem leans into the turn at the middle of its flight, in degrees.</summary>
        const float ShuffleTilt = 22f;

        /// <summary>How much a gem grows as it is lifted, and how far it shrinks in flight.</summary>
        const float ShuffleRise = .10f, ShuffleDip = .28f;

        /// <summary>
        /// The longest a refilled gem takes to fall into its cell. Shared with <c>Beat</c>, which
        /// clamps every fall to it, so the turn-over can wait for the last one to land without a
        /// second copy of the figure.
        /// </summary>
        internal const float DropMost = .34f;

        // ------------------------------------------------------------------ the turn-over
        /// <summary>
        /// Draws the field being dealt again: re-files every widget under the cell its gem now
        /// stands in, then flies it there.
        ///
        /// <para>
        /// <b>The widgets are re-filed before anything moves</b>, so that from the first frame
        /// <c>_gems</c> says what the model says. Anything that reads the view mid-flight - a
        /// lesson looking for a gem in its socket, a repaint - then finds the right gem
        /// travelling to the right cell rather than the wrong gem at rest.
        /// </para>
        /// <para>
        /// <b>It waits for the last refill to land first</b>, because a gem still bouncing into
        /// its cell is the start point of its own flight: started early, the swirl would take
        /// over a fall in mid-air.
        /// </para>
        /// </summary>
        IEnumerator Reshuffling(int[] from)
        {
            if (from == null || _gems.Count == 0) yield break;

            // A nudge pointing at two cells is pointing at two gems that are about to leave.
            Stir();

            float settle = DropMost - SiegeTuning.BeatFor * .5f + .05f;
            if (settle > 0f) yield return new WaitForSecondsRealtime(settle);

            int n = Mathf.Min(_gems.Count, from.Length);

            var was = new Gem[n];
            for (int i = 0; i < n; i++) was[i] = _gems[i];

            for (int to = 0; to < n; to++)
            {
                int f = from[to];
                _gems[to] = f >= 0 && f < n ? was[f] : null;
            }

            var centre = new Vector2(0f, _gemCentre);
            var half = new Vector2(Mathf.Max(Cell, Width * Cell * .5f),
                                   Mathf.Max(Cell, Height * Cell * .5f));

            float reach = 0f;
            for (int to = 0; to < n; to++)
            {
                var gem = _gems[to];
                if (gem == null || gem.Img == null) continue;
                reach = Mathf.Max(reach, Scaled(gem.Img.rectTransform.anchoredPosition - centre, half).magnitude);
            }
            if (reach <= 0f) reach = 1f;

            Dilate(ShufflePace, ShuffleFor);

            Banner(Loc.Get("ui.siege.shuffle"), Pal.Cream, .86f,
                   () => Audio.Sfx("whoosh", .55f, .8f));

            if (_fx != null)
            {
                Lightup(Pal.Cream, .10f, ShuffleFor);

                // The gather and the release: one ring closing as it turns, one going out as the
                // gems land. Round, because this is the field and not the ground of the hill.
                float wide = Cell * Width * .95f;
                Ripple(centre, Pal.Sun, wide, .42f, ShuffleLift * .5f, 1f);
                Ripple(centre, Pal.Cream, wide * 1.15f, .45f,
                       ShuffleLift + ShuffleStagger + ShuffleFly * .85f, 1f);
            }

            for (int to = 0; to < n; to++)
            {
                var gem = _gems[to];
                if (gem == null || gem.Img == null) continue;

                var rt = gem.Img.rectTransform;
                var start = rt.anchoredPosition;

                // The rim leaves first, so the board reads as being drawn in.
                float rim = Mathf.Clamp01(Scaled(start - centre, half).magnitude / reach);
                float late = ShuffleStagger * (1f - rim);

                Swirl(rt, start, CentreOf(to), centre, half, late);
            }

            yield return new WaitForSecondsRealtime(ShuffleFor);

            Audio.SfxVaried("settle", .40f);
        }

        /// <summary>
        /// One gem's flight from <paramref name="from"/> to <paramref name="to"/>, wheeling
        /// anticlockwise round <paramref name="centre"/> in the field's own proportions.
        /// </summary>
        void Swirl(RectTransform rt, Vector2 from, Vector2 to, Vector2 centre, Vector2 half,
                   float late)
        {
            // **Hand back whatever else is holding this gem's scale or tilt** before taking them -
            // a charm's arrival pop lands, a punch puts back what it borrowed, a hint stops
            // breathing - so nothing is left fighting the flight frame by frame. The fall is
            // superseded by the tween below, on its own channel.
            Tween.KillChannel(rt, "scale");
            Tween.KillChannel(rt, "punch");
            Tween.KillChannel(rt, "rot");
            Tween.KillChannel(rt, "breathe");

            var a = Scaled(from - centre, half);
            var b = Scaled(to - centre, half);

            float r0 = a.magnitude, r1 = b.magnitude;
            float t0 = Mathf.Atan2(a.y, a.x);
            float t1 = Mathf.Atan2(b.y, b.x);

            // A gem at the very middle has no angle of its own; it takes its destination's, so
            // it rises out rather than spinning on the spot.
            if (r0 < 1e-3f) t0 = t1;
            if (r1 < 1e-3f) t1 = t0;

            // Anticlockwise for every gem, so the field turns as one thing.
            float turn = Mathf.Repeat(t1 - t0, Mathf.PI * 2f);

            Tween.Run(ShuffleFor, Ease.Linear, k =>
            {
                if (!rt) return;

                float time = k * ShuffleFor;

                float lift = Ease.OutCubic(Mathf.Clamp01(time / ShuffleLift));
                float u = Mathf.Clamp01((time - ShuffleLift - late) / ShuffleFly);
                float e = Ease.InOutCubic(u);
                float land = Mathf.Clamp01((time - ShuffleLift - late - ShuffleFly) / ShuffleLand);

                Vector2 at;
                if (u <= 0f) at = from;
                else if (u >= 1f) at = to;
                else
                {
                    float angle = t0 + turn * e;
                    float radius = Mathf.Lerp(r0, r1, e) * (1f - ShufflePinch * Mathf.Sin(Mathf.PI * e));
                    at = centre + Unscaled(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, half);
                }

                float grown = 1f + ShuffleRise * lift - ShuffleDip * Mathf.Sin(Mathf.PI * e);
                float scale = land > 0f
                            ? Mathf.LerpUnclamped(1f + ShuffleRise, 1f, Ease.OutBack(land))
                            : grown;

                rt.anchoredPosition = at;
                rt.localScale = Vector3.one * scale;
                rt.localRotation = Quaternion.Euler(0f, 0f, ShuffleTilt * Mathf.Sin(Mathf.PI * e));
            }, rt, "move").OnDone(() => Reseated(rt, to)).OnAbandon(() => Reseated(rt, to));
        }

        /// <summary>A gem at rest in its cell: where it belongs, its own size, upright.</summary>
        static void Reseated(RectTransform rt, Vector2 at)
        {
            if (!rt) return;

            rt.anchoredPosition = at;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        /// <summary>A field offset in units of the field's own half extents.</summary>
        static Vector2 Scaled(Vector2 v, Vector2 half) => new Vector2(v.x / half.x, v.y / half.y);

        /// <summary>The other way: a unit offset back into the field's own units.</summary>
        static Vector2 Unscaled(Vector2 v, Vector2 half) => new Vector2(v.x * half.x, v.y * half.y);
    }
}
