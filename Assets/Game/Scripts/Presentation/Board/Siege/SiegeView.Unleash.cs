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
        /// Paints every tube's readiness: the pulse that says a tube may be spent.
        ///
        /// <b>The raycast is switched with it</b>, so a tube that is not full cannot be tapped at
        /// all — a control that is live and silently refuses is one nobody learns.
        /// </summary>
        void Ready()
        {
            if (_posts == null || _board == null) return;

            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                if (post == null || post.Dump == null) continue;

                var ward = _board.Wards[i];
                bool armed = ward.Armed;

                post.Dump.raycastTarget = armed;

                float lit = armed
                          ? .6f + Mathf.PingPong(Time.unscaledTime * 2.6f, 1f) * .4f
                          : 0f;

                float beat = armed ? 1f + Mathf.Sin(Time.unscaledTime * 6f) * .09f : 1f;

                // **White, because the glyph carries its own colour.** Tinting it would be the
                // multiply invariant 37l records - `Image.color` can only ever darken, so a
                // coloured badge asked to look *lit* comes out muddy. What pulses is its alpha and
                // the halo behind it.
                post.Dump.color = Pal.A(Color.white, lit);
                post.Dump.rectTransform.localScale = Vector3.one * beat;

                if (post.Halo != null)
                {
                    post.Halo.color = Pal.A(TintOf(ward.Colour), lit * .7f);
                    post.Halo.rectTransform.localScale = Vector3.one * (.85f + lit * .3f) * beat;
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
