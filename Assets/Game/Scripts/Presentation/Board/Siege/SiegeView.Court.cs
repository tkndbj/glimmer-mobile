using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The sixth chapter's two bosses, drawn: a gorgon's glare and a sunlord's seal.
    ///
    /// <para>
    /// <b>Two verbs, two pictures, and neither may be the other's in a tint</b> (invariant 37z,
    /// and <c>SiegeArtTests.EveryBossSpellIsItsOwnDrawing</c> holds it). A glare is a *ray* - one
    /// straight line of stone light that lands and stays on the machine it hit. A seal is an
    /// *object* - a ring that falls onto a post and sits there with a clock running in it. The
    /// two look nothing alike because they ask the player to do opposite things: walk away from
    /// one, and drop everything for the other.
    /// </para>
    /// <para>
    /// <b>Both lasting states are drawn from the model every frame rather than latched when the
    /// spell lands</b> - the douse's, the chain's and the rubble's rule for their reason: the
    /// frame the mask lifts is the frame the ward is worth feeding again, and the frame a seal is
    /// paid is the frame it has to stop being drawn.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the gorgon
        /// <summary>
        /// The coat a stone-struck ward wears. Not black and not dark - <b>grey</b>, because what
        /// a glare takes is the machine rather than the light (invariant 37m: a light goes up, and
        /// this is not about brightness at all).
        /// </summary>
        static readonly Color StoneCoat = new Color(.62f, .60f, .57f);

        /// <summary>
        /// A shot leaving a stone-struck barrel: it emerges, goes grey a cell out, and shatters.
        ///
        /// <para>
        /// <b>The bolt has to be seen leaving, or the verb reads as a jam.</b> A ward under a
        /// glare is fuelled, is firing and is spending the fuel each shot costs
        /// (<see cref="SiegeWard.Glared"/>); if nothing came out of the barrel a player would read
        /// it as a chain and wait it out, which is exactly the wrong answer. What they have to see
        /// is the fuel going somewhere and arriving nowhere.
        /// </para>
        /// <para>
        /// <b>Drawn one per shot rather than as a state</b>, because the cost is per shot: a ward
        /// masked through the whole window wastes every bolt it takes, and the count of them is
        /// the count of shattering.
        /// </para>
        /// </summary>
        void Stoned(int ward)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var fire = Casting(SiegeKind.Gorgon);
            var from = new Vector2(PostX(ward), _lineY + Cell);
            var to = from + new Vector2(0f, Cell * StoneReach);

            var shot = UIKit.Img("Stoned bolt", _fx, Art.SoftCapsule(48), Pal.Cream,
                                 new Vector2(Cell * .18f, Cell * .52f));
            shot.raycastTarget = false;
            shot.rectTransform.anchoredPosition = from;

            var rt = shot.rectTransform;

            Tween.Run(StoneFlight, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = Vector2.Lerp(from, to, t);

                // It sets as it climbs: cream at the muzzle, stone by the time it stops.
                shot.color = Color.Lerp(Pal.Cream, fire, Mathf.Clamp01(t * 1.6f));
                rt.localScale = new Vector3(1f + t * .5f, 1f - t * .28f, 1f);
            }, shot).OnDone(() =>
            {
                if (!shot) return;
                var died = rt.anchoredPosition;
                Destroy(shot.gameObject);

                if (_fx == null) return;

                // **It shatters rather than fading.** A fade is something the player missed; a
                // break is something that happened to them.
                Burst.Sparks(_fx, died, fire, 8, Cell * 1.5f, Cell * .16f, .40f);
                Pop(died, Pal.A(fire, .85f), 1.2f, .20f);
                Audio.SfxVaried("shatter", .26f, .10f);
            });
        }

        /// <summary>How far a stone-struck bolt gets, in cells, and how long it takes.</summary>
        const float StoneReach = 1.35f, StoneFlight = .22f;

        /// <summary>
        /// A gorgon's glare landing on a post: the mask closing over it.
        ///
        /// <b>Drawn once, on the landing</b> - the lasting state is the grey coat and the pall
        /// `Charge` keeps on the post from the model. What this adds is the moment, because a
        /// state that simply appears is a state nobody saw arrive.
        /// </summary>
        void Masked(int ward, Color fire)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .8f);

            // Two rings closing in from outside, which is what a gaze narrowing looks like from
            // the wrong end of it.
            for (int i = 0; i < 2; i++)
            {
                var ring = UIKit.Img("Glare mask", _fx, Art.Ring(128, 7f), Pal.A(fire, 0f),
                                     new Vector2(Cell * 2.4f, Cell * 2.4f));
                ring.raycastTarget = false;
                ring.rectTransform.anchoredPosition = at;

                var rt = ring.rectTransform;
                float spin = i == 0 ? 120f : -120f;

                Tween.Run(GlareClose, Ease.OutQuad, t =>
                {
                    if (!rt) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(2.8f, .92f, t);
                    rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                    ring.color = Pal.A(fire, t < .6f ? t / .6f : 1f - (t - .6f) / .4f);
                }, ring).Delay(i * .07f).OnDone(() => { if (ring) Destroy(ring.gameObject); });
            }

            Shockwave(at, fire, 2.8f, .32f);
            Audio.Sfx("blocked", .48f, .70f);
        }

        /// <summary>How long a glare's rings take to close.</summary>
        const float GlareClose = .34f;

        // ------------------------------------------------------------------ the sunlord
        /// <summary>
        /// The seal standing on a post while it lasts: a ring with the time left drawn round it
        /// and the toll paid drawn inside it.
        ///
        /// <para>
        /// <b>Two readings on one widget, because the player is asked two questions at once</b> -
        /// how long is left, and how much of the answer is paid. Both come off the model every
        /// frame (<see cref="SiegeWard.Sealed"/>, <see cref="SiegeWard.Paid"/>), so the frame the
        /// toll is met is the frame the seal stops being drawn, whichever door the fuel came
        /// through.
        /// </para>
        /// <para>
        /// <b>It is built when the seal lands and destroyed when it lifts</b>, rather than built
        /// for every post and hidden - invariant 48i's rule about a light on a reward, asked of a
        /// threat: a widget that is always there and usually invisible is a widget that will one
        /// day be drawn when it should not be, and nothing would say so.
        /// </para>
        /// </summary>
        void Warded(Post post, SiegeWard ward, int index)
        {
            bool wanted = ward != null && ward.Doomed;

            if (!wanted)
            {
                if (post.Seal == null) return;

                Destroy(post.Seal.gameObject);
                post.Seal = null;
                post.Toll = null;
                return;
            }

            var fire = Casting(SiegeKind.Sunlord);

            if (post.Seal == null)
            {
                post.Seal = UIKit.Img("Seal", post.Node, Art.Ring(128, 9f), Pal.A(fire, .95f),
                                      new Vector2(Cell * 1.9f, Cell * 1.9f));
                post.Seal.raycastTarget = false;
                post.Seal.rectTransform.anchoredPosition = new Vector2(0f, Cell * 1.5f);

                post.Toll = UIKit.Img("Seal toll", post.Seal.rectTransform, Art.Glow(96, 1.7f),
                                      Pal.A(Pal.Radiance, 0f),
                                      new Vector2(Cell * 1.2f, Cell * 1.2f));
                post.Toll.raycastTarget = false;
                post.Toll.rectTransform.SetAsFirstSibling();
            }

            float left = SiegeTuning.DoomFor <= 0f
                       ? 0f : Mathf.Clamp01(ward.Sealed / SiegeTuning.DoomFor);

            // **It tightens and quickens as it runs out**, which is the one thing a ring can say
            // about a deadline without a number on it: the turn is slow while there is time and
            // frantic when there is not.
            post.Seal.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, Time.unscaledTime * Mathf.Lerp(420f, 90f, left));
            post.Seal.rectTransform.localScale =
                Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * Mathf.Lerp(16f, 4f, left))
                                    * Mathf.Lerp(.12f, .04f, left));
            post.Seal.color = Pal.A(Color.Lerp(Pal.Poppy, fire, left), .95f);

            // And the toll filling it from the middle, which is the half the player controls.
            post.Toll.color = Pal.A(Pal.Radiance, ward.Paid * .8f);
            post.Toll.rectTransform.localScale = Vector3.one * (.35f + ward.Paid * .75f);
        }

        /// <summary>
        /// A seal landing on a post: it falls out of the sky, bites, and starts turning.
        ///
        /// <b>It arrives from above rather than from the caster</b>, which is the one thing that
        /// separates it from every other spell in this mode at a glance: a sentence is passed on
        /// somebody, it is not thrown at them.
        /// </summary>
        void Condemned(int ward, Color fire)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * 1.5f);

            var ring = UIKit.Img("Seal fall", _fx, Art.Ring(128, 11f), Pal.A(fire, 0f),
                                 new Vector2(Cell * 2.6f, Cell * 2.6f));
            ring.raycastTarget = false;

            var rt = ring.rectTransform;
            float above = at.y + Cell * 4f;

            Tween.Run(SealFall, Ease.InQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = new Vector2(at.x, Mathf.Lerp(above, at.y, t));
                rt.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 300f);
                ring.color = Pal.A(fire, Mathf.Min(1f, t * 2f));
            }, ring).OnDone(() =>
            {
                if (!ring) return;
                Destroy(ring.gameObject);

                if (_fx == null) return;

                Shockwave(at, fire, 3.4f, .30f);
                Burst.Sparks(_fx, at, fire, 16, Cell * 2.8f, Cell * .22f, .55f);
                ShakeBoard(Cell * .12f);
                Audio.Sfx("bell", .62f, .74f);
            });

            Audio.Sfx("whoosh", .46f, .64f);
        }

        /// <summary>How long a seal takes to fall onto a post.</summary>
        const float SealFall = .42f;

        /// <summary>
        /// A seal running out: it closes on the post and takes it.
        ///
        /// <para>
        /// <b>It collapses inward where <see cref="Redeemed"/> bursts outward</b>, which is the
        /// whole of what tells the two endings apart at a glance - and they are the only pair of
        /// endings in this mode that share a widget. A ring flying apart is something the player
        /// broke; a ring closing is something that closed on them.
        /// </para>
        /// <para>
        /// <b>It has no caster</b>: the thing that took the ward is the clock, which may well
        /// outlive the boss that started it. So nothing is drawn on the hill, and the whole
        /// picture is at the post.
        /// </para>
        /// </summary>
        void Sentenced(int ward, Color fire)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * 1.5f);

            var ring = UIKit.Img("Seal closing", _fx, Art.Ring(128, 12f), Pal.A(Pal.Poppy, .95f),
                                 new Vector2(Cell * 2.4f, Cell * 2.4f));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            Tween.Run(SealShut, Ease.InQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(1.6f, .06f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 540f);
                ring.color = Pal.A(Pal.Poppy, .95f);
            }, ring).OnDone(() =>
            {
                if (!ring) return;
                Destroy(ring.gameObject);

                if (_fx == null) return;

                Pop(at, Pal.Poppy, 4.2f, .30f);
                Shockwave(at, fire, 5.0f, .42f);
                Burst.Sparks(_fx, at, fire, 22, Cell * 3.6f, Cell * .28f, .65f);
                ShakeBoard(Cell * .22f);
                Flow.Flash(new Color(1f, .32f, .30f), .34f, .3f);
                Audio.Sfx("boom", .82f, .74f);
            });

            Dilate(.25f, .45f);
            Audio.Sfx("bell", .70f, .52f);
        }

        /// <summary>How long a seal takes to close on a post it has taken.</summary>
        const float SealShut = .40f;

        /// <summary>
        /// A seal paid off: it shatters outward and the post is handed back.
        ///
        /// <b>Loud, and deliberately louder than the landing.</b> This is the only thing in six
        /// chapters a player has ever taken back off a boss, and a payoff drawn more quietly than
        /// the threat is a payoff nobody notices they earned (invariant 26f).
        /// </summary>
        void Redeemed(int ward)
        {
            if (_fx == null || _posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * 1.5f);

            for (int i = 0; i < 2; i++)
            {
                var ring = UIKit.Img("Seal broken", _fx, Art.Ring(128, 8f),
                                     Pal.A(i == 0 ? Pal.Radiance : Pal.Gold, .95f),
                                     new Vector2(Cell * 2f, Cell * 2f));
                ring.raycastTarget = false;
                ring.rectTransform.anchoredPosition = at;

                var rt = ring.rectTransform;
                float to = i == 0 ? 3.6f : 5.2f;

                Tween.Run(.44f, Ease.OutQuad, t =>
                {
                    if (!rt) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, to, t);
                    ring.color = Pal.A(i == 0 ? Pal.Radiance : Pal.Gold, 1f - t);
                }, ring).Delay(i * .06f).OnDone(() => { if (ring) Destroy(ring.gameObject); });
            }

            Pop(at, Pal.Radiance, 3.2f, .28f);
            Burst.Sparks(_fx, at, Pal.Gold, 24, Cell * 4f, Cell * .28f, .70f);
            ShakeBoard(Cell * .10f);
            Dilate(.4f, .22f);

            Audio.Sfx("chime", .70f, 1.18f);
            Audio.Sfx("unlock", .54f, 1f);

            Announce(Loc.Get("mode.siege.unsealed"), Pal.Gold, .62f, 1.1f, true);
        }
    }
}
