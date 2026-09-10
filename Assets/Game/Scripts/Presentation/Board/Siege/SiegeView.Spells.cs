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
    /// <summary>The bosses' spells: the tell, the flight and what lands.</summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the warlord's spells
        /// <summary>
        /// A spell, from the moment the warlord decides on it to the moment it leaves.
        ///
        /// <para>
        /// <b>Four beats a player can read, and the first three are the point.</b> The warlord
        /// swings into its own attack frames; a violet light gathers on it; and a ring closes over
        /// the ward it has chosen — so what is about to happen, and to whom, is on the board for
        /// <c>SiegeTuning.BossTell</c> before it happens. That window is not decoration: it is
        /// long enough to pour a <c>mending</c> into the ward that is about to be hit, which is
        /// the one thing on this board a player can do about a warlord other than shoot it.
        /// </para>
        /// <para>
        /// <b>The schedule comes out of the rules and is never invented here</b> (invariant 37s).
        /// The board takes the ward's health exactly <c>BossTell + BossFlight</c> after this, so a
        /// bolt drawn on any other clock would arrive before or after the damage it is meant to be.
        /// </para>
        /// </summary>
        void Cast(SiegeCast cast)
        {
            var mob = MobOf(cast.Raider);
            if (mob == null || mob.Node == null) return;

            // **A spell aimed at the field is a different drawing entirely**, and it is settled
            // before anything below reads `cast.Ward` as a post index — which for these carries a
            // *cell*. `SiegeTuning.AimsAtAWard` is the question, for the reason it exists: three
            // things can be in that field and only the caster's kind says which.
            if (!SiegeTuning.AimsAtAWard(mob.Kind)
                && cast.Craft != SiegeSpell.Rally) { Snare(cast, mob); return; }

            Vector2 from = mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .10f);

            // **A roar is thrown at the ground it is standing on**, so it has no ward and its
            // "flight" is an expanding ring rather than something crossing the hill. Everything
            // below asks the craft rather than testing the ward index, which is the rule the board
            // keeps too (`SiegeTuning.AimsAtAWard`).
            bool aimed = cast.Craft != SiegeSpell.Rally && cast.Ward >= 0;

            Vector2 to = aimed ? new Vector2(PostX(cast.Ward), _lineY + Cell * .3f) : from;

            // The alien's own attack frames, once, and back to standing. A warlord that only ever
            // cycled its idle would have no way to say it had done anything, which is the fault
            // the ward line's recoil frames were added for.
            if (mob.Body != null && mob.Casting != null && mob.Casting.Length > 0)
            {
                mob.Playing = mob.Casting;

                var book = Flipbook.Attach(mob.Body, mob.Casting,
                                           mob.Casting.Length / SiegeTuning.BossTell, false);

                // Handed straight back to `Follow`, which is the only thing that decides what a
                // warlord wears: clearing the latch is all that is needed, and a callback that
                // put a *particular* reel back would be a second opinion about a question already
                // answered above.
                if (book != null) book.OnFinished = () => { if (mob.Body) mob.Playing = null; };
                else mob.Playing = null;
            }

            Gather(mob);

            // The tell. A ward that is about to be hit gets a ring closing on it; a roar has no
            // target, so what closes is a ring on the warbringer itself — the same grammar saying
            // "something is about to happen *here*" rather than "*to that*".
            if (aimed) Sigil(cast.Ward, mob.Kind);
            else Brace(mob, from);

            // **The storm the wind-up is actually made of** (see `SiegeView.Storm`): crackle
            // accelerating over the whole window, motes dragged in off the hill, and — for a boss
            // that has chosen a ward — a tether flickering between it and its target. None of it
            // moves the schedule; all of it happens inside `BossTell`.
            Winding(mob, from, to, aimed);

            Audio.Sfx("whoosh", .5f, Pitch(mob.Kind));

            // The bolt itself leaves when the wind-up ends, and crosses in `BossFlight` — but only
            // if the warlord is still standing when it does. The board already fizzles a spell
            // whose caster has been destroyed (`SiegeBoard.Arrive`), and a spell drawn crossing the
            // hill that then does nothing is worse than one that was never thrown: it reads as the
            // hit having been missed rather than as the cast having been interrupted.
            int caster = cast.Raider;

            int ward = aimed ? cast.Ward : -1;

            Tween.After(SiegeTuning.BossTell, () =>
            {
                if (_board == null || _board.Find(caster) == null) return;

                Unleash(mob, from, to, ward);
            }, mob.Node);
        }

        /// <summary>How low a boss's magic sounds. Bigger things speak lower.</summary>
        static float Pitch(SiegeKind kind)
            => kind == SiegeKind.Overlord ? .50f
             : kind == SiegeKind.Warbringer ? .55f
             : kind == SiegeKind.Blightcaller ? .92f : .74f;

        /// <summary>The light a warlord gathers before a spell leaves it.</summary>
        void Gather(Mob mob)
        {
            var glow = mob.Charge;
            if (glow == null) return;

            var fire = Casting(mob.Kind);

            Tween.KillChannel(glow, "gather");

            Tween.Run(SiegeTuning.BossTell, Ease.InQuad, t =>
            {
                if (!glow) return;
                glow.color = Pal.A(fire, t * .95f);
                glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.35f, 1.15f, t);
            }, glow, "gather").OnDone(() =>
            {
                if (!glow) return;
                Tween.Run(.22f, Ease.OutQuad, t =>
                {
                    if (!glow) return;
                    glow.color = Pal.A(fire, (1f - t) * .95f);
                }, glow, "gather");
            });
        }

        /// <summary>
        /// The ring that closes over the ward a spell is coming for.
        ///
        /// <b>It closes rather than expanding</b>, which is the opposite of every other ring on
        /// this board and is why: <c>Shockwave</c> grows outward and means <em>something has
        /// happened here</em>, and this means <em>something is about to</em>. A countdown that
        /// looks like an explosion is a warning nobody reads as one.
        /// </summary>
        void Sigil(int ward, SiegeKind kind)
        {
            var at = new Vector2(PostX(ward), _lineY + Cell * .3f);
            bool greater = kind == SiegeKind.Overlord;

            var ring = UIKit.Img("Sigil", _fx, Art.Ring(128, greater ? 13f : 10f),
                                 Pal.A(Casting(kind), .95f),
                                 new Vector2(Cell * (greater ? 1.25f : 1f), Cell));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            // **A hex turns the other way.** The two spells that take a ward's *health* close a
            // ring clockwise; the one that takes its *fire* closes anticlockwise, so the two
            // warnings are told apart at a glance by a player who has met both — which is the
            // whole of what a tell is for, and the cheapest possible way to say it.
            float spin = SiegeTuning.SpellOf(kind) == SiegeSpell.Douse ? -260f : 220f;

            Tween.Run(SiegeTuning.BossTell, Ease.Linear, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(4.4f, 1.5f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, t * spin);
            }, ring).OnDone(() =>
            {
                if (!ring) return;
                Tween.Fade(ring, 0f, SiegeTuning.BossFlight)
                     .OnDone(() => { if (ring) Destroy(ring.gameObject); });
            });
        }

        /// <summary>
        /// The tell a warbringer wears, which is a ring closing on <em>itself</em>.
        ///
        /// A roar has no target, so the thing a player has to read is not "which ward" but "how
        /// long" — and the answer to it is not a mending, it is a firepot into whatever the roar
        /// is about to set running. Same grammar as <see cref="Sigil"/> (a ring that closes means
        /// something is about to happen), aimed at the boss rather than at the line.
        /// </summary>
        void Brace(Mob mob, Vector2 at)
        {
            var ring = UIKit.Img("Brace", _fx, Art.Ring(128, 12f),
                                 Pal.A(Casting(SiegeKind.Warbringer), .9f),
                                 new Vector2(Cell * 1.4f, Cell * 1.4f));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            Tween.Run(SiegeTuning.BossTell, Ease.Linear, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(5.2f, 1.8f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 160f);
            }, ring).OnDone(() =>
            {
                if (!ring) return;
                Tween.Fade(ring, 0f, SiegeTuning.BossFlight)
                     .OnDone(() => { if (ring) Destroy(ring.gameObject); });
            });

            // It braces itself before it goes: the body squashes down into the roar, which is the
            // one bit of anticipation a boss that never throws anything has to work with.
            if (mob.Body != null)
                Tween.Punch(mob.Body.transform, .09f, SiegeTuning.BossTell * .6f);
        }

        /// <summary>
        /// How big each boss's thrown thing is drawn, in cells: the object, its muzzle and its
        /// impact.
        ///
        /// <b>Size is the second of the three things that tell the four apart</b>, after what they
        /// do to the line and before their colour. An omen is the largest thing this mode ever
        /// draws; a hex is the smallest, because it takes no health and reading as heavy would be
        /// the drawing telling a lie about the rule.
        /// </summary>
        static float ThrownAt(SiegeKind kind)
            => kind == SiegeKind.Overlord ? 2.1f
             : kind == SiegeKind.Blightcaller ? 1.35f : 1.6f;

        static float BurstAt(SiegeKind kind)
            => kind == SiegeKind.Overlord ? 1.2f
             : kind == SiegeKind.Blightcaller ? .85f : 1f;

        /// <summary>
        /// One arm of a volley: an orb crossing the hill on a bowed path.
        ///
        /// <para>
        /// <b>It draws the projectile and nothing else now.</b> The flash, the ring, the starburst
        /// and the bolts thrown off the caster all moved to <see cref="Leaving"/>, because every
        /// boss throws two or three of these at once and a muzzle drawn per orb is three muzzles
        /// on one frame. What is left here is exactly "a thing flying from A to B".
        /// </para>
        /// <para>
        /// <b><paramref name="bow"/> is what makes a volley read as a volley.</b> Three orbs on
        /// the same straight line are one orb drawn three times; pushed sideways by a sine that is
        /// nought at both ends, they leave together, spread across the hill and converge on the
        /// ward — and the two halves of an overlord's pair bow in opposite directions, which is
        /// the whole of why it reads as a launch rather than as a bigger smite.
        /// </para>
        /// <para>
        /// <b>Every arm still lands on the rules' clock</b> (invariant 37s): the flight is
        /// shortened by exactly whatever <paramref name="delay"/> holds it back, so a stagger
        /// spreads the departures and never moves the arrival.
        /// </para>
        /// </summary>
        void Hurl(Vector2 from, Vector2 to, SiegeKind kind, float flight, float bow, float scale,
                  float delay)
        {
            var dir = to - from;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            var fire = Casting(kind);

            var frames = SpellArt(kind);

            if (frames == null || frames.Length == 0)
                frames = new[] { Art.Glow(96, 2.0f) };

            // **Half again the width of a ward's bolt, and anchored at its middle rather than at
            // `HeadAt`.** A boss's spell is an orb rather than a comet — it is baked square
            // (`SiegeShotBake.BakeSpell`), so there is no head leading a trail to step back from.
            var puff = Lend(frames, Color.white, Cell * ThrownAt(kind) * scale, from, angle, 30f,
                            true, .5f);
            Glow(puff, fire, Cell * 3f * BurstAt(kind) * scale);

            var node = puff.Node;

            node.gameObject.SetActive(delay <= 0f);

            // **A hex drifts rather than flies**, which is the one thing about its motion a player
            // can read before it lands: it wobbles across the hill and swells, where a smite and
            // an omen go straight. The arrival is on the rules' clock either way (invariant 37s) —
            // what changes is the path, never the time.
            bool wafts = SiegeTuning.SpellOf(kind) == SiegeSpell.Douse;
            var side = new Vector2(-dir.y, dir.x).normalized * Cell;

            float crosses = Mathf.Max(.05f, flight - delay);

            var trail = Time.unscaledTime;

            Tween.Run(crosses, Ease.Linear, t =>
            {
                if (!node) return;

                node.gameObject.SetActive(true);

                var at = Vector2.Lerp(from, to, t);

                // **More than a cell at full lean, and it has to be.** At half a cell three orbs
                // on spread arcs are one orb drawn three times and an overlord's pair is a single
                // sun; what makes a volley read as one is that the arms are visibly *apart* in the
                // middle of the flight and visibly *together* at the end of it.
                at += side * bow * 1.4f * Mathf.Sin(t * Mathf.PI);
                if (wafts) at += side * .55f * Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);

                node.anchoredPosition = at;
                node.localScale = Vector3.one * Mathf.Lerp(.8f, 1.25f, t);

                // A wake of embers behind it, thinned to about twenty a second so a three-orb
                // volley is a trail of sparks rather than a wall of them.
                if (_fx != null && Time.unscaledTime - trail > .05f)
                {
                    trail = Time.unscaledTime;
                    Burst.Sparks(_fx, at, fire, 2, Cell * .7f, Cell * .12f, .24f);
                }
            }, node).Delay(delay).OnDone(() => Give(puff));
        }

        /// <summary>
        /// A warbringer's roar leaving it: a white ring over the whole hill, and nothing crossing
        /// it.
        ///
        /// <b>The one boss effect in this mode that is not aimed</b>, so it is drawn as the thing
        /// a roar is — pressure going outward from a point — rather than as something travelling
        /// to a place. It is the loudest single drawing on the board and it is meant to be: what
        /// it announces is every raider on the hill breaking into a run, which a player has about
        /// five seconds to do something about.
        /// </summary>
        void Roar(Vector2 at, SiegeKind kind)
        {
            var fire = Casting(kind);

            // **Two reels, one upright and one flat, and that pair is the whole drawing.** The
            // pack's impact for this is a ring opening outward with shards in it, which is what a
            // roar looks like head on; its muzzle is a flat ellipse spreading, which over a hill
            // drawn in perspective is the same pressure crossing the ground. Neither alone reads
            // as more than an effect; together they read as something going *out over the hill*.
            var ring = SpellHitArt(kind);
            if (ring != null && ring.Length > 0)
                Ends(Lend(ring, Color.white, Cell * 7f, at, 0f, 34f, false, .5f), .5f);

            var ground = SpellMuzzleArt(kind);
            if (ground != null && ground.Length > 0)
                Ends(Lend(ground, Pal.A(fire, .8f), Cell * 9f, at + new Vector2(0f, Cell * .3f),
                          0f, 30f, false, .5f), .55f);

            // Three rings rather than one, a beat apart, so it reads as a shout rather than as a
            // single burst - and each is wider than the last, which is the shape of something
            // spreading over ground rather than exploding on it.
            for (int i = 0; i < 3; i++)
            {
                float wait = i * .11f;
                float size = 5.4f + i * 2.6f;

                Tween.After(wait, () => Shockwave(at, Pal.Lift(fire, .35f), size, .46f), _fx);
            }

            Burst.Sparks(_fx, at, fire, 18, Cell * 3.4f, Cell * .26f, .55f);

            ShakeBoard(24f);
            Audio.Sfx("boom", .75f, .52f);
        }

        /// <summary>
        /// A spell arriving on the line.
        ///
        /// <b>Drawn nothing like a blow</b>, which is why it is its own record: a blow is
        /// something swung by a raider standing at the line, and this comes out of the middle of
        /// the hill. It is also the heaviest single hit in the mode, so it takes the shake a felled
        /// ward used to have to itself.
        /// </summary>
        void Smite(SiegeSpellLanded spell)
        {
            // Read off the widget rather than off the board: a spell that landed on the frame its
            // caster was destroyed still has to be drawn in the colour it was thrown in, and by
            // then `SiegeBoard.Find` may already have answered null.
            // Read off the widget where there is one, and off the layout where there is not: a
            // spell that landed on the frame its caster was destroyed still has to be drawn in the
            // colour it was thrown in, and a siege sends exactly one boss (invariant 37t), so the
            // level itself is the honest fallback rather than a guess at the commonest kind.
            var caster = MobOf(spell.Raider);
            var kind = caster != null ? caster.Kind : _layout.BossKind;
            var fire = Casting(kind);

            // **A roar arrives on every ward at once, so it is four records and one stampede.**
            // The hill breaking into a run is drawn once, on the first of them — `_roared` is
            // cleared at the top of every frame's report, so "first" means first *this frame*
            // rather than first ever.
            if (spell.Craft == SiegeSpell.Rally && !_roared)
            {
                _roared = true;
                Stampede(fire);

                // The line's own answer to a roar, drawn once for all four records for the reason
                // the stampede is: lightning running post to post along the wards it just shook.
                Aftermath(SiegeKind.Warbringer, -1, default, fire);
            }

            if (spell.Ward < 0) return;

            var at = new Vector2(PostX(spell.Ward), _lineY + Cell * .3f);
            bool greater = kind == SiegeKind.Overlord;

            var frames = SpellHitArt(kind);
            if (frames != null && frames.Length > 0)
                Ends(Lend(frames, Color.white,
                          Cell * (spell.Felled ? 5.2f : 4.2f) * BurstAt(kind), at, 0f, 32f,
                          false, .5f), .4f);

            Pop(at, fire, 2.6f * BurstAt(kind), .3f);
            Shockwave(at, fire, 3.6f * BurstAt(kind), .34f);
            Burst.Sparks(_fx, at, fire, greater ? 20 : 14, Cell * 3f, Cell * .24f, .5f);

            // What the boss's own verb leaves behind on the post, over and above the burst — see
            // `SiegeView.Storm`. A roar has already had its own above, because it lands on four
            // wards at once and this is one drawing.
            if (spell.Craft != SiegeSpell.Rally) Aftermath(kind, spell.Ward, at, fire);

            // **A douse is drawn as a light going out, and everything about it is quieter.** It
            // takes no health, so a hit that shook the board and flashed the screen would be the
            // drawing overstating the rule — and the thing a player has to notice is the ward
            // itself going dark, which `Post` keeps drawn for as long as it lasts.
            if (spell.Craft == SiegeSpell.Douse)
            {
                Snuffed(spell.Ward, fire);
                return;
            }

            // A rank coming off is its own piece of news and gets its own drawing, because it is
            // the only damage in this mode that cannot be mended back.
            if (spell.Sundered) Sundered(spell.Ward, fire);

            Tween.Shake(_posts[spell.Ward].Node, Cell * .26f, .38f);
            ShakeBoard(spell.Felled ? 26f : greater ? 20f : 15f);

            Audio.Sfx("boom", spell.Felled ? .8f : .6f, spell.Felled ? .8f : greater ? .82f : 1f);

            if (spell.Felled) Flow.Flash(new Color(1f, .32f, .30f), .34f, .3f);
        }

        /// <summary>
        /// A ward going out under a hex.
        ///
        /// <b>The fuel tube empties in front of the player rather than simply being empty next
        /// frame</b>, because the tube is the one readout every decision in this mode rests on
        /// (invariant 37y) and what has been taken is exactly what was in it. The pall over the
        /// post is drawn by <see cref="Charge"/> for as long as the dark lasts, so this is the
        /// moment and that is the state.
        /// </summary>
        void Snuffed(int ward, Color fire)
        {
            var post = _posts[ward];
            var at = new Vector2(PostX(ward), _lineY + Cell * .3f);

            Burst.Sparks(_fx, at, fire, 10, Cell * 2.2f, Cell * .18f, .6f);
            Tween.Shake(post.Node, Cell * .1f, .26f);

            // A second scatter down at the tube, because what a douse really took is what was in
            // it — the fuel readout is where every decision in this mode is read (invariant 37y),
            // so that is where the loss has to be seen happening.
            Burst.Sparks(_fx, new Vector2(PostX(ward), _lineY - Cell * .1f), fire, 8,
                         Cell * 1.4f, Cell * .12f, .45f);

            Audio.Sfx("whoosh", .55f, 1.25f);
        }

        /// <summary>
        /// What a doused ward is drawn in: standing, cold, and unmistakably not firing.
        ///
        /// <b>Slate rather than dark</b>, so it cannot be read as the <c>ward_dead</c> a fallen
        /// one wears — one of these is over in five seconds and the other is over for the run,
        /// and a player who confuses them stops feeding a colour that is coming back.
        /// </summary>
        static readonly Color DarkCoat = new Color(.46f, .53f, .60f, 1f);

        /// <summary>
        /// A rank coming off a ward under an omen.
        ///
        /// <b>The badge falls, which is the only place in this mode a tier goes down.</b> A cog
        /// climbing the ladder is drawn as an arrival (invariant 20m's rule about the event being
        /// the reward); this is that run backwards, so a player who spent four cogs on one turret
        /// sees the thing they spent them on come apart rather than simply finding a smaller
        /// number there later.
        /// </summary>
        void Sundered(int ward, Color fire)
        {
            var post = _posts[ward];
            var rt = post.Crest;
            if (rt == null) return;

            var home = rt.anchoredPosition;

            Tween.KillChannel(rt, "sunder");
            Tween.Run(.42f, Ease.InQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = home + new Vector2(0f, -Cell * .5f * t);
                rt.localRotation = Quaternion.Euler(0f, 0f, -70f * t);
                rt.localScale = Vector3.one * Mathf.Lerp(1.35f, .7f, t);
            }, rt, "sunder").OnDone(() =>
            {
                if (!rt) return;
                rt.anchoredPosition = home;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            });

            Burst.Sparks(_fx, new Vector2(PostX(ward), _lineY + Cell * .55f), fire, 12,
                         Cell * 1.8f, Cell * .16f, .5f);

            Audio.Sfx("shatter", .5f, 1.15f);
        }

        /// <summary>
        /// The hill breaking into a run.
        ///
        /// One flash across every raider standing on it, so the moment a roar takes hold is
        /// something the player <em>sees on the raiders</em> rather than something they have to
        /// infer from them arriving sooner than expected.
        /// </summary>
        void Stampede(Color fire)
        {
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob.Boss || mob.Body == null) continue;

                Tween.Punch(mob.Body.transform, .16f, .3f);
                Burst.Sparks(_fx, mob.Node.anchoredPosition, fire, 5, Cell * 1.1f, Cell * .12f,
                             .35f);
            }

            Flow.Flash(Pal.A(fire, .5f), .22f, .22f);
        }

        void Blow(SiegeBlow hit)
        {
            var post = _posts[hit.Ward];

            Tween.Shake(post.Node, Cell * .16f, .3f);
            ShakeBoard(hit.Felled ? 22f : 9f);

            Burst.Sparks(_fx, new Vector2(PostX(hit.Ward), _lineY), Pal.Rose, 9, Cell * 2f,
                         Cell * .2f, .4f);

            Audio.SfxVaried("blocked", hit.Felled ? .7f : .34f);

            // Only a ward coming down flashes the whole screen. A flash on every blow is five a
            // second once a wave is at the line, at which point it stops reading as damage taken
            // and starts reading as a fault.
            if (hit.Felled) Flow.Flash(new Color(1f, .32f, .30f), .34f, .3f);
        }

        void Fell(Post post)
        {
            var at = new Vector2(PostX(System.Array.IndexOf(_posts, post)), _lineY);

            Boom(at, Blast("boom_smoke"), Cell * 3.4f);
            Burst.Sparks(_fx, at, Pal.Ember, 20, Cell * 3f, Cell * .28f, .7f);
            Audio.Sfx("shatter", .8f, .78f);

            post.Body.sprite = Piece("ward_dead");
            post.Body.color = new Color(.52f, .52f, .56f, 1f);

            Tween.Rotate(post.Body.rectTransform, -16f, .5f, Ease.OutBounce);
            Tween.Fade(post.Glow, 0f, .3f);
            Tween.Fade(post.Juice, .25f, .3f);
        }

        /// <summary>
        /// A ward standing up again, because a continue was paid for. <see cref="Fell"/> read
        /// backwards, and it has to be written out rather than left to <see cref="Charge"/>.
        ///
        /// <para>
        /// Three of the four things felling a ward does are outside anything the per-frame paint
        /// touches — the wreck sprite, the sixteen degrees it topples through and the fuel tube
        /// faded to a quarter — so a raised ward left to <see cref="Charge"/> would come back
        /// upright in the model and lying on its side on the screen. Only the body's tint heals
        /// itself, and that is the one that would have looked fine.
        /// </para>
        /// <para>
        /// <b>No sound of its own.</b> Four turrets come back at once and four shatters played
        /// backwards is a flam rather than a fanfare (invariant 37q); <see cref="Rally"/> sounds
        /// the moment once, for the line.
        /// </para>
        /// </summary>
        void Raise(Post post)
        {
            if (post == null || post.Body == null) return;

            int i = System.Array.IndexOf(_posts, post);
            if (i < 0) return;

            var ward = _board.Wards[i];
            var tint = TintOf(post.Colour);
            var at = new Vector2(PostX(i), _lineY);

            post.Down = false;

            // The two fades are killed rather than overwritten, because setting a colour under a
            // running fade is a value the next frame throws away. The topple needs no kill: a
            // second `Tween.Rotate` on one transform takes the same channel and ends the first.
            Tween.KillAll(post.Glow);
            Tween.KillAll(post.Juice);

            post.Body.sprite = WardArt(ward);
                    if (post.Shield != null) post.Shield.color = RankTint(ward.Rank);
            post.Body.color = post.Coat;

            // The tube goes back to full opacity and stays empty, which is the honest picture: a
            // raised ward has its health and none of its fuel (see `SiegeBoard.Rally`).
            post.Juice.color = tint;
            post.Glow.color = Pal.A(tint, 0f);

            Tween.Rotate(post.Body.rectTransform, 0f, .34f, Ease.OutBack);

            Shockwave(at, Pal.Lift(tint, .45f), 3.4f, .46f);
            Pop(at, Pal.Lift(tint, .3f), 2.6f, .32f);
            Burst.Sparks(_fx, at + new Vector2(0f, Cell * .4f), tint, 18, Cell * 3.2f,
                         Cell * .26f, .6f);

            Tween.Punch(post.Node, .2f, .36f);
        }

        /// <summary>
        /// The whole line coming back, because a continue was paid for.
        ///
        /// <para>
        /// <b>Staggered left to right and sounded once.</b> Four turrets standing up on the same
        /// frame reads as a repaint; a fifteenth of a second between them reads as a line being
        /// rallied, which is what was bought. The order is <c>SiegeBoard.Rally</c>'s own, so what
        /// the board did and what the screen draws cannot come apart.
        /// </para>
        /// </summary>
        void Rallied()
        {
            if (_posts == null) return;

            int nth = 0;

            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                if (post == null || !post.Down || !_board.Wards[i].Alive) continue;

                int at = nth++;
                Tween.After(at * .07f, () => { if (this) Raise(post); }, this);
            }

            if (nth == 0) return;

            // Cream and gentle, against the ember flash `Ruin` threw a moment ago: the same
            // gesture in the opposite colour, at a third of the strength, because this is relief
            // rather than another blow. The peak is the parameter, not the colour's own alpha —
            // `Flow.Flash` zeroes that before it starts.
            Audio.Sfx("mend", .85f, .92f);
            Flow.Flash(Pal.Cream, .34f, .34f);
            ShakeBoard(12f);
        }

        void Arrival(int wave)
        {
            _wave = wave + 1;

            if (_waveLabel == null) return;

            // **The warlord's wave is announced as itself rather than as a number.** "WAVE 4 OF 4"
            // is true and is the wrong thing to say about the one wave that is not like the others
            // — the header carries the count for anybody who wants it (`SiegeScreen.Readouts`), and
            // what the board owes this moment is the news.
            bool boss = _board.BossWave;
            var kind = _layout.BossKind;

            // **Each boss is announced as itself.** Two of them shared one banner while the mode
            // had two, which is the same fault as sharing a body: the one moment the game has to
            // say "this is not the thing you fought last time" was spent saying "a boss".
            // Written out rather than keyed off the kind's name, for invariant 6's reason — a loc
            // key built by concatenation is a key the build gate cannot see.
            string banner;
            switch (kind)
            {
                case SiegeKind.Overlord: banner = "mode.siege.overlord"; break;
                case SiegeKind.Warbringer: banner = "mode.siege.warbringer"; break;
                case SiegeKind.Blightcaller: banner = "mode.siege.blightcaller"; break;
                default: banner = "mode.siege.boss"; break;
            }

            _waveLabel.text = boss ? Loc.Get(banner)
                                   : Loc.Format("mode.siege.wave", _wave, _board.Waves);

            _waveLabel.color = boss ? Casting(kind) : Pal.Cream;
            _waveLabel.fontSize = Mathf.RoundToInt(Cell * (boss ? .78f : .46f));

            var group = UIKit.Group(_waveLabel.rectTransform);
            var rt = _waveLabel.rectTransform;

            Tween.KillAll(_waveLabel);
            rt.anchoredPosition = new Vector2(0f, _hillFoot + Cell * 1.1f);
            rt.localScale = Vector3.one * (boss ? 1.6f : 1f);
            group.alpha = 0f;

            Tween.Fade(group, 1f, .22f);
            if (boss) Tween.Scale(rt, 1f, .5f, Ease.OutBack);

            Tween.Move(rt, new Vector2(0f, _hillFoot + Cell * 1.9f), boss ? 2.4f : 1.5f,
                       Ease.OutCubic)
                 .OnDone(() => Tween.Fade(group, 0f, .4f));

            // **No sound of its own, except for the warlord.** The first wave steps out on the
            // same frame the countdown says GO!, so a bell there was the same bell twice a frame
            // apart - which is a flam rather than emphasis. Nothing lands with the warlord, so it
            // is the one arrival that can be heard as well as seen.
            if (boss)
            {
                // How hard each one lands is how big it is, which is the same ladder its
                // silhouette, its health bar and its spell are drawn on — the blightcaller arrives
                // as the lightest of the four because it is the first one a chapter shows.
                float weight = kind == SiegeKind.Overlord ? 1f
                             : kind == SiegeKind.Warbringer ? .88f
                             : kind == SiegeKind.Blightcaller ? .62f : .78f;

                Audio.Sfx("boom", .6f + weight * .35f, Pitch(kind) - .1f);
                ShakeBoard(14f + weight * 14f);
                Flow.Flash(Pal.A(Casting(kind), 1f), .34f + weight * .28f, .45f);
            }
            else
            {
                Flow.Flash(new Color(1f, .55f, .45f), .18f, .35f);
            }
        }

        void Boom(Vector2 at, Sprite[] frames, float size)
        {
            if (frames == null || frames.Length == 0) return;

            var img = UIKit.Img("Boom", _fx, frames[0], Color.white, new Vector2(size, size));
            img.raycastTarget = false;
            img.rectTransform.anchoredPosition = at;

            var book = Flipbook.Attach(img, frames, 26f, false);
            if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
            else Tween.After(.6f, () => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// One raider's damage as it lands, floating off it.
        ///
        /// <para>
        /// <b>Numbers are tallied per raider rather than one to a bolt, and that is what makes
        /// them readable here at all.</b> A lit line fires every <c>SiegeTuning.FireEvery</c>, so
        /// four wards on one target is about eighteen hits a second — eighteen separate figures a
        /// second is a wall of text nobody can read one number out of, and drawing each of them
        /// bigger makes that worse rather than better. So a hit landing on a raider that is
        /// already showing a number <em>adds to it</em>: the figure climbs, grows, punches again
        /// and its float restarts, and what the player watches is one number running up while they
        /// hold fire on something. The next one starts its own after <see cref="TallyFor"/> of
        /// quiet.
        /// </para>
        /// <para>
        /// <b>A double reads as a different kind of number rather than a bigger one</b> — gold, a
        /// much harder punch, a longer and higher float — because the elemental double is the one
        /// rule this mode is about, and this is the only place it is ever said in figures.
        /// </para>
        /// </summary>
        const float TallyFor = .34f;

        sealed class Tally
        {
            public int Raider, Total;
            public bool Weak;

            /// <summary>Caused by the player rather than by a ward. Drawn bigger and hotter.</summary>
            public bool Mine;
            public float Until, Drift;
            public Text Label;
            public RectTransform Rt;
        }

        readonly Dictionary<int, Tally> _tally = new Dictionary<int, Tally>();

        /// <summary>Which way the next number leans, so a run of them fans out instead of stacking.</summary>
        int _fan;

        void Number(int raider, Vector2 at, int damage, bool weak, bool mine = false)
        {
            if (_tally.TryGetValue(raider, out var running) && running.Label &&
                Time.unscaledTime < running.Until)
            {
                running.Total += damage;
                running.Weak |= weak;
                running.Mine |= mine;
                running.Until = Time.unscaledTime + TallyFor;

                Paint(running);
                Punch(running);
                Rise(running);
                return;
            }

            var tally = new Tally
            {
                Raider = raider,
                Total = damage,
                Weak = weak,
                Mine = mine,
                Until = Time.unscaledTime + TallyFor,
                Drift = (_fan++ & 1) == 0 ? -1f : 1f,
            };

            // Built through `Titled` rather than `Label`: a bare figure over a lit hill and a
            // bright cast is unreadable, and the outline is most of what a floating number is.
            tally.Label = UIKit.Titled("Hit", _fx, damage.ToString(), 24, Pal.Cream,
                                       TextAnchor.MiddleCenter,
                                       new Vector2(Cell * 5f, Cell * 2f), default, default,
                                       Cell * .055f, Cell * .06f);

            tally.Rt = tally.Label.rectTransform;
            tally.Rt.anchoredPosition =
                at + new Vector2(Random.Range(-Cell * .3f, Cell * .3f), Cell * .45f);

            _tally[raider] = tally;

            Paint(tally);
            Punch(tally);
            Rise(tally);
        }

        /// <summary>The figure, sized by what it has come to. A big number is a big number.</summary>
        void Paint(Tally tally)
        {
            if (!tally.Label) return;

            float grown = Mathf.Min(tally.Total, 30) / 30f;
            float step = tally.Mine ? .82f : tally.Weak ? .58f : .40f;
            float size = Cell * step * (1f + grown * .5f);

            tally.Label.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            tally.Label.text = tally.Total.ToString();
            tally.Label.color = tally.Mine ? Pal.Ember : tally.Weak ? Pal.Gold : Pal.Cream;
        }

        /// <summary>The arrival: overshoot and settle, once per hit that lands on it.</summary>
        void Punch(Tally tally)
        {
            var rt = tally.Rt;
            if (!rt) return;

            float from = tally.Weak ? 1.85f : 1.4f;

            Tween.KillChannel(tally.Label, "pop");
            Tween.Run(.17f, Ease.OutBack, k =>
            {
                if (rt) rt.localScale = Vector3.one * Mathf.Lerp(from, 1f, k);
            }, tally.Label, "pop");
        }

        /// <summary>
        /// The float, restarted from wherever the number has got to every time it takes another
        /// hit — so a figure still climbing does not drift off in the middle of its own tally.
        ///
        /// Every channel is owned by the <c>Text</c> rather than by its transform, because they
        /// are two different Unity objects and a channel killed on one is not killed on the other.
        /// </summary>
        void Rise(Tally tally)
        {
            var rt = tally.Rt;
            var label = tally.Label;
            if (!rt || !label) return;

            Tween.KillChannel(label, "rise");
            Tween.KillChannel(label, "fade");

            var opaque = label.color;
            opaque.a = 1f;
            label.color = opaque;

            // **Lower and shorter than it was.** The first cut floated a cell and a half over a
            // second, which is a long time for a figure to be over the hill when the next one is
            // 55 milliseconds behind it — the numbers stacked up the screen and stayed there. What
            // a floating number owes the player is to be legible on arrival and then get out of
            // the way, so it lifts about half a cell and is gone inside two thirds of a second.
            float life = tally.Weak ? .68f : .52f;
            Vector2 from = rt.anchoredPosition;
            Vector2 to = from + new Vector2(tally.Drift * Cell * .22f,
                                            Cell * (tally.Weak ? .75f : .55f));

            Tween.Run(life, Ease.OutCubic, k =>
            {
                if (rt) rt.anchoredPosition = Vector2.Lerp(from, to, k);
            }, label, "rise");

            // Held at full for the first half and only then let go, because a number that starts
            // fading the instant it appears is one nobody has finished reading.
            Tween.Run(life, Ease.Linear, k =>
            {
                if (!label) return;
                var colour = label.color;
                colour.a = k < .45f ? 1f : 1f - (k - .45f) / .55f;
                label.color = colour;
            }, label, "fade").OnDone(() => Retire(tally));
        }

        void Retire(Tally tally)
        {
            if (_tally.TryGetValue(tally.Raider, out var held) && held == tally)
                _tally.Remove(tally.Raider);

            if (tally.Label) Destroy(tally.Label.gameObject);
        }
    }
}
