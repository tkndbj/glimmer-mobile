using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The overcharge: a full tube, tapped and thrown.
    ///
    /// <para>
    /// <b>The one control this mode has in the middle band</b>, which is the band that used to
    /// exist only to be read. A tube fills, it starts pulsing, and the player has a decision — let
    /// it burn down as ordinary bolts, or dump the lot now at whatever is furthest down the hill.
    /// It can be wrong, which is what makes it a decision rather than a button: a tube spent on a
    /// creeper is a tube not standing ready for the brute three beats behind it.
    /// </para>
    /// <para>
    /// <b>The blast is drawn as a firepot's, deliberately.</b> A player who has ever thrown one
    /// knows exactly what they are looking at, and a second explosion of our own would be a second
    /// vocabulary for the same event. What says this one is <em>theirs</em> is where it comes
    /// from: a beam leaves the turret they tapped.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>
        /// The overcharge glyph's beat: how long the bolt takes to spring up, how often it swells
        /// once it is up, and by how much. About a beat and a half a second and a fifth of its
        /// size, which is loud enough to find from the gems without reading as an alarm.
        /// </summary>
        const float ArriveSeconds = .32f, PulseHz = 1.5f, PulseReach = .2f;

        /// <summary>
        /// Paints every tube's readiness: the pulse that says a tube may be spent.
        ///
        /// <para>
        /// <b>The raycast is switched with it</b>, so a tube that cannot be spent cannot be tapped
        /// at all — a control that is live and silently refuses is one nobody learns.
        /// </para>
        /// <para>
        /// <b>And "can be spent" is the board's answer, not this file's guess.</b> It read
        /// <c>ward.Armed</c> — a charge banked, not chained, not buried — which is only half the
        /// question: <c>SiegeBoard.Overcharge</c> also needs something on the hill it could hurt,
        /// and against a boss there are frames where there is not (the walk in, and a stand
        /// already resting on its floor). So the button pulsed, invited a tap and shook it off,
        /// which is what a player meets as <em>sometimes I can use it and sometimes I cannot</em>.
        /// One reading, asked here and answered there: <c>SiegeBoard.CanOvercharge</c>.
        /// </para>
        /// </summary>
        void Ready()
        {
            if (_posts == null || _board == null) return;

            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                if (post == null || post.Dump == null) continue;

                var ward = _board.Wards[i];
                bool armed = _board.CanOvercharge(i);

                post.Dump.raycastTarget = armed;

                float now = Time.unscaledTime;

                if (!armed) post.LitAt = -1f;
                else if (post.LitAt < 0f) post.LitAt = now;

                float age = armed ? now - post.LitAt : 0f;

                // **It arrives, then it beats.** The bolt springs up from nothing with an
                // overshoot, and from then on swells by a fifth and settles, never dimming: the
                // owner's note on the glyph before this one was that it could not be seen, and an
                // alpha pulse spends half of every beat being harder to see. What breathes is the
                // size and the light behind it, on a cosine timed from the arrival so every bolt
                // opens its beat at rest rather than mid-swell.
                float arrive = armed ? Ease.OutBack(Mathf.Clamp01(age / ArriveSeconds)) : 0f;
                float swell = armed ? .5f - .5f * Mathf.Cos(age * PulseHz * 2f * Mathf.PI) : 0f;

                // **White, because the glyph carries its own colour.** Tinting it would be the
                // multiply invariant 37l records - `Image.color` can only ever darken, so a
                // coloured badge asked to look *lit* comes out muddy.
                post.Dump.color = Pal.A(Color.white, armed ? 1f : 0f);
                post.Dump.rectTransform.localScale = Vector3.one * (arrive * (1f + swell * PulseReach));

                if (post.Halo != null)
                {
                    post.Halo.color = Pal.A(Pal.Lift(TintOf(ward.Colour), .35f),
                                            Mathf.Clamp01(arrive) * (.45f + swell * .5f));
                    post.Halo.rectTransform.localScale = Vector3.one * (arrive * (.9f + swell * .45f));
                }

                // **How many are held, and only once there is more than one.** A badge saying "1"
                // on every armed tube is a number nobody reads; a "2" is the one moment the count
                // is news, because it is the moment a third would be thrown away.
                if (post.Held == null) continue;

                bool many = ward.Charges > 1;

                post.Held.enabled = many;
                if (post.Pip != null) post.Pip.enabled = many;

                if (many) post.Held.text = ward.Charges.ToString();
            }
        }

        /// <summary>
        /// A tube tapped: one banked charge goes at once.
        ///
        /// <para>
        /// <b>It charges the run nothing</b>, and that is arithmetic rather than generosity. What
        /// an overcharge delivers is exactly what the tube would have delivered as ordinary bolts
        /// — the same fuel, the same weight, landing as an own-colour hit does — so the player has
        /// moved damage they had already matched for rather than conjured any, and invariant 39's
        /// exchange rate has nothing to price. See <c>SiegeBoard.Overcharge</c>.
        /// </para>
        /// </summary>
        void Unleashed(int ward)
        {
            if (!Tappable || _board == null) return;

            HideCoach();
            Stir();

            _strikes.Clear();
            var blast = _board.Overcharge(ward, _strikes);

            if (!blast.Landed)
            {
                var post = ward >= 0 && ward < _posts.Length ? _posts[ward] : null;
                if (post != null && post.Dump != null) Refuse(post.Dump.rectTransform);

                Rejected?.Invoke();
                return;
            }

            Beam(blast);
            Firepot(SiegeAim.OnTheHill(blast.Lane, blast.Row));

            for (int i = 0; i < _strikes.Count; i++) Hurt(_strikes[i]);

            Reap();
            Changed?.Invoke();
            Judge();
        }

        /// <summary>
        /// The beam out of the turret that spent itself.
        ///
        /// <b>Drawn from the muzzle to the box rather than to the raider</b>, because the raider it
        /// was aimed at is very often dead by the time this runs — and a beam that ends where the
        /// blast is is the same fact said twice, which is what makes the pair read as one event.
        /// </b>
        /// </summary>
        void Beam(SiegeUnleash blast)
        {
            var post = blast.Ward >= 0 && blast.Ward < _posts.Length ? _posts[blast.Ward] : null;
            if (post == null) return;

            var tint = TintOf(_board.Wards[blast.Ward].Colour);

            var from = new Vector2(PostX(blast.Ward), _lineY + Cell * 1.0f);
            var to = BoxAt(blast.Lane, blast.Row);

            var span = to - from;
            float length = span.magnitude;
            if (length <= 1f) return;

            var bar = UIKit.Img("Beam", _fx, Art.SoftCapsule(64), Pal.A(tint, .92f),
                                new Vector2(length, Cell * .5f));
            bar.raycastTarget = false;

            var rt = bar.rectTransform;
            rt.anchoredPosition = from + span * .5f;
            rt.localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);

            Tween.Run(.26f, Ease.OutQuad, t =>
            {
                if (!bar) return;

                bar.color = Pal.A(tint, .92f * (1f - t));
                rt.sizeDelta = new Vector2(length, Cell * .5f * (1f - t * .7f));
            }, bar, "beam").OnDone(() =>
            {
                if (bar) Destroy(bar.gameObject);
            });

            Shockwave(from, Pal.Lift(tint, .4f), 3.4f, .4f);
            Burst.Sparks(_fx, from, tint, 14, Cell * 3f, Cell * .24f, .5f);

            Tween.Punch(post.Node, .2f, .3f);

            Audio.Sfx("boom", .7f, 1.12f);
            ShakeBoard(14f);
        }
    }
}
