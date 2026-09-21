using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The seventh chapter's two bosses, drawn: a harrower's claw and a hollowking's wane.
    ///
    /// <para>
    /// <b>Two verbs, two pictures, and neither may be the other's in a tint</b> (invariant 37z,
    /// held by <c>SiegeArtTests.EveryBossSpellIsItsOwnDrawing</c>). A harrow is a *theft* - a
    /// thing comes off the machine and lands on the ground where the player can go and get it -
    /// so the drawing has a direction and an object in it. A wane is an *absence*: nothing
    /// arrives at all, the post simply gutters, and the posts that were working are not drawn
    /// on. The two look nothing alike because they ask the player for opposite things: reach out
    /// for one, and have already been busy for the other.
    /// </para>
    /// <para>
    /// <b>Both are moments rather than states, which is what separates this file from
    /// <c>SiegeView.Court</c>.</b> A glare and a seal leave something standing on the post that
    /// has to be drawn from the model every frame; a harrow leaves an ordinary
    /// <c>SiegeCog</c> on the hill and a wane leaves nothing but health taken, so neither has a
    /// lasting widget and neither may grow one - what a player reads afterwards is the cog lying
    /// there and the health bar that moved.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the harrower
        /// <summary>How long the torn badge takes to fall off the post.</summary>
        const float TornFor = .40f;

        /// <summary>
        /// A harrow landing on a post: the rank coming off it.
        ///
        /// <para>
        /// <b>It falls rather than bursts, and that is the whole of what tells it apart from an
        /// overlord's sunder.</b> A sunder is a rank destroyed and is drawn as a fan of lightning
        /// to every turret; a harrow is a rank <em>taken</em>, so what the post shows is a badge
        /// coming loose and dropping out of frame toward the hill - where the cog it became is
        /// already lying (<c>SiegeBoard.Scatter</c>). The eye is carried from the line down onto
        /// the ground, which is exactly where the answer to this boss is.
        /// </para>
        /// <para>
        /// <b>Drawn on the landing only.</b> There is no state to keep: the rank is off the ward
        /// the moment the spell lands and the rank strip already draws what is left, so a widget
        /// that lingered here would be a second opinion about a number the post is showing.
        /// </para>
        /// </summary>
        void Torn(int ward, Color fire)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .95f);

            // The badge itself: a small hard shape rather than a glow, because what came off is
            // a *thing*. It tumbles as it falls, which is what stops it reading as a spark.
            var chip = UIKit.Img("Torn rank", _fx, Art.Round(10), Pal.A(Pal.Gold, .95f),
                                 new Vector2(Cell * .34f, Cell * .34f));
            chip.raycastTarget = false;
            chip.rectTransform.anchoredPosition = at;

            var rt = chip.rectTransform;
            float spin = Random.value < .5f ? -320f : 320f;
            float drift = Random.Range(-Cell * .5f, Cell * .5f);

            Tween.Run(TornFor, Ease.InQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = at + new Vector2(drift * t, Cell * (1.1f * t - .9f * t * t));
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                chip.color = Pal.A(Pal.Gold, 1f - t * t);
            }, chip).OnDone(() => { if (chip) Destroy(chip.gameObject); });

            // Three claw scores across the chassis, which is what took it. Short, parallel and
            // all in one direction - a fan in every direction is the overlord's drawing.
            for (int i = 0; i < 3; i++)
            {
                var a = at + new Vector2(-Cell * .55f + i * Cell * .12f, Cell * .35f);
                var b = a + new Vector2(Cell * .95f, -Cell * .7f);

                Arc(a, b, fire, Cell * .045f, .22f, 0, .04f, i * .045f);
            }

            Shockwave(at, Pal.Lift(fire, .25f), 2.2f, .26f);
            Audio.Sfx("blocked", .44f, 1.18f);
        }

        // ----------------------------------------------------------------- the hollowking
        /// <summary>How long a struck post gutters for.</summary>
        const float HollowFor = .46f;

        /// <summary>
        /// A wane landing on a post that had gone quiet: the light guttering out of it.
        ///
        /// <para>
        /// <b>It goes down, and that is the one drawing in this mode allowed to.</b> Invariant
        /// 37m says a light goes <em>up</em> - "has fuel" reads as brighter, never "no fuel" as
        /// dimmer - and that rule is about a <em>standing</em> state on a post the player is
        /// reading at a glance. This is an event: a pall drops over the chassis, the glow under
        /// it collapses inward, and both are gone inside half a second. Nothing about the post
        /// is dimmer afterwards than it was before.
        /// </para>
        /// <para>
        /// <b>What makes it the verb is where it is <em>not</em> drawn.</b> A rally puts one of
        /// these on every post whatever the player did; a wane is reported per ward and only for
        /// the ones that landed nothing since the last cast (<c>SiegeBoard.Advance</c>), so a
        /// line that has been kept working sees an empty ward line and hears the boss cast at
        /// nothing. That silence is the rule being read off the board rather than off a caption,
        /// which is what invariant 20g asks of a mechanic the player has to learn.
        /// </para>
        /// </summary>
        void Hollowed(int ward, Color fire)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .8f);

            // The pall: a soft disc that falls onto the post and shrinks into it, so the motion
            // is inward and downward rather than outward - the opposite of every burst here.
            var pall = UIKit.Img("Wane pall", _fx, Art.Glow(128, 1.5f), Pal.A(fire, 0f),
                                 new Vector2(Cell * 2.6f, Cell * 2.6f));
            pall.raycastTarget = false;
            pall.rectTransform.anchoredPosition = at + new Vector2(0f, Cell * .9f);

            var rt = pall.rectTransform;

            Tween.Run(HollowFor, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = at + new Vector2(0f, Cell * .9f * (1f - t));
                rt.localScale = Vector3.one * Mathf.Lerp(1.15f, .38f, t);
                pall.color = Pal.A(fire, t < .35f ? t / .35f : 1f - (t - .35f) / .65f);
            }, pall).OnDone(() => { if (pall) Destroy(pall.gameObject); });

            // Two motes drifting off the chassis, which is the light leaving rather than
            // arriving. Slow, few and short - anything busier would read as an explosion.
            for (int i = 0; i < 2; i++)
            {
                var a = at + new Vector2(Random.Range(-Cell * .3f, Cell * .3f), Cell * .1f);
                var b = a + new Vector2(Random.Range(-Cell * .35f, Cell * .35f), Cell * .8f);

                Arc(a, b, Pal.Lift(fire, .3f), Cell * .035f, .26f, 0, .10f, .05f + i * .08f);
            }

            Audio.Sfx("blocked", .40f, .62f);
        }
    }
}
