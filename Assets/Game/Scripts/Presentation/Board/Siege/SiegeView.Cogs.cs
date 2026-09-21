using System.Collections.Generic;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Cogs lying on the hill: the reward that lands in the half of the screen nobody was
    /// watching.
    ///
    /// <para>
    /// <b>Drawn the way the bomber's bomb is drawn, deliberately.</b> They are the only two things
    /// on that hill a finger does anything to, and a player who has learnt one has learnt the
    /// other: a thing that arrives with a thump, sits in a box of the aiming grid, breathes, and
    /// goes away when it is tapped. What separates them is colour and a clock — a cog wears the
    /// turret it will rank, and it starts blinking when its time is nearly up.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        sealed class Gear
        {
            public int Id;
            public RectTransform Node;
            public Image Body;
            public Image Glow;
            public Image Ring;
        }

        readonly List<Gear> _gears = new List<Gear>(4);

        /// <summary>
        /// How far a cog sits from the middle of its box.
        ///
        /// <b>Offset rather than centred, because a bomber drops both.</b> A felled bomber leaves
        /// a bomb and may leave a cog in the same instant and the same box, and two taps stacked
        /// on one point is one of them the player cannot reach. A quarter of a cell up and across
        /// is enough to separate them and small enough that the cog is still obviously in that
        /// box.
        /// </summary>
        const float CogNudge = .26f;

        void Dropped(SiegeCog cog)
        {
            var gear = new Gear { Id = cog.Id };

            gear.Node = UIKit.Node("Cog", _cogLayer);
            gear.Node.anchorMin = gear.Node.anchorMax = new Vector2(.5f, .5f);
            gear.Node.sizeDelta = new Vector2(Cell, Cell);
            gear.Node.anchoredPosition =
                BoxAt(cog.Lane, cog.Row) + new Vector2(Cell * CogNudge, Cell * CogNudge);

            var tint = TintOf(cog.Colour);

            gear.Glow = UIKit.Img("Glow", gear.Node, Art.Glow(96, 2.1f), Pal.A(tint, 0f),
                                  new Vector2(Cell * 2.1f, Cell * 2.1f));
            gear.Glow.raycastTarget = false;

            // The ring is the clock. It is the ward's own colour rather than a warning red,
            // because what it is counting down is a *prize* — an alarm colour on a thing the
            // player wants would read as one more threat on a hill already full of them.
            gear.Ring = UIKit.Img("Ring", gear.Node, Art.Ring(96, 9f), Pal.A(tint, .85f),
                                  new Vector2(Cell * 1.15f, Cell * 1.15f));
            gear.Ring.raycastTarget = false;

            gear.Body = UIKit.Img("Cog", gear.Node, CogArt, Color.white,
                                  new Vector2(Cell * .86f, Cell * .86f));
            gear.Body.preserveAspect = true;
            gear.Body.raycastTarget = true;

            var btn = gear.Body.gameObject.AddComponent<Btn>();
            btn.PressScale = .9f;
            btn.Setup(() => Grabbed(cog.Id), silent: true);

            _gears.Add(gear);

            gear.Node.localScale = new Vector3(.2f, .2f, 1f);
            Tween.Scale(gear.Node.transform, 1f, .38f, Ease.OutBack);

            Burst.Sparks(_fx, gear.Node.anchoredPosition, tint, 10, Cell * 1.7f, .45f);
            Audio.Sfx("land", .42f, 1.25f);

            Tween.After(.4f, () =>
            {
                if (gear.Body == null) return;
                Tween.Breathe(gear.Body.transform, .08f, 1.3f);
                if (gear.Glow != null) Tween.Fade(gear.Glow, .5f, .5f);
            }, this);
        }

        /// <summary>
        /// A cog tapped: the ward it names goes up a rank, and the cog flies to it.
        ///
        /// <para>
        /// <b>The fanfare is not raised here.</b> <c>Charge</c> reads every ward's rank off the
        /// board each frame and turns a change into an edge exactly once (<c>Post.Rank</c>), so a
        /// rank bought by a cog is celebrated by the same code that celebrates one bought any
        /// other way. What is left for this to draw is the *journey* — which turret the cog went
        /// to, which is the only part a player could otherwise miss.
        /// </para>
        /// </summary>
        void Grabbed(int id)
        {
            if (!Tappable || _board == null) return;

            HideCoach();
            Stir();

            var gear = GearOf(id);
            var took = _board.Take(id);

            if (!took.Landed)
            {
                if (gear != null && gear.Body != null) Refuse(gear.Body.rectTransform);
                Rejected?.Invoke();
                return;
            }

            if (gear != null)
            {
                _gears.Remove(gear);
                Carry(gear, took);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Flies a taken cog down to the turret it ranked, and leaves nothing behind.
        ///
        /// <b>The widget is re-parented to the effects layer first</b>, because the layer it was
        /// drawn on is clipped to the hill and the turret it is travelling to is not on the hill.
        /// </summary>
        void Carry(Gear gear, SiegeTaken took)
        {
            if (gear == null || gear.Node == null) return;

            var node = gear.Node;
            var from = node.anchoredPosition;
            var to = new Vector2(PostX(took.Ward), _lineY + Cell * .5f);

            node.SetParent(_fx, false);
            node.anchoredPosition = from;

            if (gear.Ring != null) Tween.Fade(gear.Ring, 0f, .14f);
            if (gear.Body != null) Tween.KillAll(gear.Body.transform);

            Tween.Run(.34f, Ease.InQuad, t =>
            {
                if (!node) return;

                node.anchoredPosition = Vector2.Lerp(from, to, t);
                node.localScale = Vector3.one * (1f - t * .45f);
            }, node, "cog").OnDone(() =>
            {
                if (node) Destroy(node.gameObject);
            });
        }

        /// <summary>
        /// Keeps the drawn cogs in step with the board's, and fades the ones that ran out.
        ///
        /// <b>Polled rather than driven by the report, for the reason <c>Fuses</c> is.</b> A cog
        /// can leave the board three ways — taken, trampled, or the board rebuilt under a
        /// continue — and a widget that only knew about the first two would be a cog standing on a
        /// hill that no longer has one.
        /// </summary>
        void Gears()
        {
            var cogs = _board.Cogs;

            // **Anything lying on the hill with no widget is drawn here, and that is a repair
            // rather than tidiness.** A cog is minted by `SiegeBoard.Cog` at the one kill door and
            // announced in the step's report — but a firepot, a utility and an overcharge all kill
            // from a tap, *outside* `Advance`, and `Advance` clears the report before it does
            // anything. So those cogs were booked into a report nothing ever read, and what they
            // were on the board was a real, tappable prize that drew nothing at all. Reconciling
            // against the board's own list is the same shape the loop below already uses to take
            // a trampled one down, and it cannot double-mint: a cog announced in the report was
            // added to `_gears` by `Dropped` a few lines earlier in the same frame.
            for (int c = 0; c < cogs.Count; c++)
            {
                bool drawn = false;
                for (int g = 0; g < _gears.Count; g++)
                    if (_gears[g].Id == cogs[c].Id) { drawn = true; break; }

                if (!drawn) Dropped(cogs[c]);
            }

            for (int i = _gears.Count - 1; i >= 0; i--)
            {
                var gear = _gears[i];

                SiegeCog held = null;
                for (int c = 0; c < cogs.Count; c++)
                    if (cogs[c].Id == gear.Id) { held = cogs[c]; break; }

                if (held == null)
                {
                    _gears.RemoveAt(i);
                    Trampled(gear);
                    continue;
                }

                if (gear.Ring != null)
                {
                    // The ring shrinks with the clock, so how long is left is a size rather than a
                    // number - and it blinks over the last third, which is the one thing that can
                    // pull an eye that is looking somewhere else.
                    float left = Mathf.Clamp01(held.Share);
                    float wide = Cell * (.7f + left * .45f);

                    gear.Ring.rectTransform.sizeDelta = new Vector2(wide, wide);

                    float lit = held.Fading
                              ? .35f + Mathf.PingPong(Time.unscaledTime * 4.5f, 1f) * .6f
                              : .85f;

                    gear.Ring.color = Pal.A(TintOf(held.Colour), lit);
                }
            }
        }

        /// <summary>A cog nobody reached: it sinks rather than vanishing, so the loss is visible.</summary>
        void Trampled(Gear gear)
        {
            if (gear == null || gear.Node == null) return;

            var node = gear.Node;
            var from = node.anchoredPosition;

            Tween.KillAll(node);
            if (gear.Body != null) Tween.KillAll(gear.Body.transform);

            Tween.Run(.3f, Ease.InQuad, t =>
            {
                if (!node) return;

                node.anchoredPosition = from + new Vector2(0f, -Cell * .35f * t);
                node.localScale = Vector3.one * (1f - t);
            }, node, "sink").OnDone(() =>
            {
                if (node) Destroy(node.gameObject);
            });
        }

        Gear GearOf(int id)
        {
            for (int i = 0; i < _gears.Count; i++)
                if (_gears[i].Id == id) return _gears[i];

            return null;
        }
    }
}
