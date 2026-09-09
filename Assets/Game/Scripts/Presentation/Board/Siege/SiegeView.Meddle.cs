using System.Collections.Generic;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The half of the hill that reaches into the field: a weaver throwing a lock at a gem, a thief
    /// taking one away, and the beat where killing either gives it all back.
    ///
    /// <para>
    /// <b>It is drawn as something crossing, and that is the whole of what makes it legible.</b>
    /// The rules book a flight and land it a second later (invariant 37s), so the player watches a
    /// thread leave a beetle, cross the hill and land on one gem — which says <em>that</em> beetle
    /// did <em>this</em> to <em>that cell</em> without a word being written about any of it. A
    /// cell that simply changed would be the field misbehaving.
    /// </para>
    /// <para>
    /// <b>Its own file for <c>SiegeBoard.Meddle</c>'s reason</b>: everything else on this hill
    /// costs a ward, so it is drawn beside the wards. These two cost the player cells.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>
        /// A weaver's or a thief's cast: a mote out of the raider, and a ring closing on the cell
        /// it has chosen.
        ///
        /// <b>The ring comes first and the mote follows</b>, so a player who is looking at the
        /// field rather than at the hill still gets a warning — which is the only warning there
        /// is, since the cell it takes is not one they could have predicted.
        /// </b>
        /// </summary>
        void Snare(SiegeCast cast, Mob mob)
        {
            int cell = cast.Ward;
            if (cell < 0 || cell >= _gems.Count) return;

            bool stealing = cast.Craft == SiegeSpell.Snatch;
            var ink = stealing ? new Color(.78f, .66f, .48f) : new Color(.92f, .94f, .88f);

            Vector2 from = mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .16f);
            Vector2 to = _field.anchoredPosition + CentreOf(cell);

            // The ring on the cell, closing over the whole tell and the flight together. Drawn on
            // the field so it moves with a gem that falls under it mid-cast, which is exactly what
            // the board does about a cell that stops being takeable (`SiegeBoard.Landed` drops the
            // throw rather than landing it somewhere else).
            var ring = UIKit.Img("Snare", _field, Art.Ring(96, 8f), Pal.A(ink, .0f),
                                 new Vector2(Cell * 1.5f, Cell * 1.5f));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = CentreOf(cell);

            float lands = SiegeTuning.MeddleTell + SiegeTuning.MeddleFlight;

            Tween.Tint(ring, Pal.A(ink, .9f), SiegeTuning.MeddleTell * .5f);
            Tween.Scale(ring.transform, Vector3.one * .62f, lands, Ease.OutCubic)
                 .OnDone(() => { if (ring) Destroy(ring.gameObject); });

            Audio.Sfx("poke", .32f, stealing ? .78f : 1.18f);

            // The mote. It leaves when the wind-up ends and crosses in `MeddleFlight`, which is
            // the rules' own schedule rather than a duration invented here — the same split every
            // boss spell keeps, and the reason a gem never locks before something has visibly been
            // thrown at it.
            Tween.After(SiegeTuning.MeddleTell, () =>
            {
                if (_fx == null) return;

                var mote = UIKit.Img("Thread", _fx, Art.Glow(80, 2.2f), ink,
                                     new Vector2(Cell * .58f, Cell * .58f));
                mote.raycastTarget = false;

                var node = mote.rectTransform;
                node.anchoredPosition = from;

                Tween.Run(SiegeTuning.MeddleFlight, Ease.InQuad, t =>
                {
                    if (!node) return;
                    node.anchoredPosition = Vector2.Lerp(from, to, t);
                    node.localScale = Vector3.one * Mathf.Lerp(1.15f, .7f, t);
                }, node).OnDone(() => { if (mote) Destroy(mote.gameObject); });
            }, mob.Node);
        }

        /// <summary>
        /// One mark landing on the field, or coming off it.
        ///
        /// <para>
        /// <b>The repaint is <see cref="Dress"/>'s job and this is only the ceremony</b>, so a
        /// board rebuilt after a continue is correct without any of this having run — which is
        /// what stops a lock that arrived while the player was in a menu from being invisible for
        /// the rest of the run.
        /// </para>
        /// <para>
        /// <b>A release is the payoff and gets the louder drawing</b> (invariant 20m): a lock
        /// landing is one small thud, and a weaver dying is six of them burning off at once.
        /// </para>
        /// </summary>
        void Meddled(SiegeMeddle mark)
        {
            int cell = mark.Cell;
            if (cell < 0 || cell >= _gems.Count) return;

            Dress(cell);

            var at = CentreOf(cell);
            bool stealing = mark.Craft == SiegeSpell.Snatch;

            if (mark.Placed)
            {
                ShakeBoard(.10f);
                Audio.Sfx(stealing ? "pilfer" : "snare", .5f);
                return;
            }

            // Coming off. A burst per cell, all in the same beat, which is what makes a field
            // handed back at once read as one event rather than as six.
            var pop = Blast("pop");
            var tint = mark.Colour >= 0 ? TintOf(mark.Colour) : new Color(.86f, .78f, .58f);

            if (pop != null && pop.Length > 0)
            {
                var puff = Book(pop, "Freed", _field, new Vector2(Cell * 1.5f, Cell * 1.5f),
                                pop.Length / .34f, false);
                if (puff != null) puff.color = Pal.A(tint, .95f);
                if (puff != null)
                {
                    puff.rectTransform.anchoredPosition = at;
                    Tween.After(.42f, () => { if (puff) Destroy(puff.gameObject); }, puff.transform);
                }
            }

            var gem = _gems[cell];
            if (gem != null && gem.Img != null) Tween.Pop(gem.Img.transform, 0f, .3f);
        }

        /// <summary>
        /// Everything the hill has taken from the field, given back at once.
        ///
        /// One sound for the lot rather than one each, because six pops a frame apart is a rattle
        /// and the thing being said is a single piece of news: that raider is dead and the board
        /// is yours again.
        /// </summary>
        void Freed(IReadOnlyList<SiegeMeddle> marks)
        {
            bool any = false;

            for (int i = 0; i < marks.Count; i++)
                if (!marks[i].Placed) { any = true; break; }

            if (!any) return;

            Audio.Sfx("chime", .55f, 1.05f);
            Flow.Flash(new Color(1f, .98f, .90f), .16f, .22f);
        }
    }
}
