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
        /// swings into its own attack frames; a violet light gathers on it; and a tether reaches
        /// for the ward it has chosen — so what is about to happen, and to whom, is on the board
        /// for <c>SiegeTuning.BossTell</c> before it happens. That window is not decoration: it is
        /// long enough to pour a <c>mending</c> into the ward that is about to be hit, which is
        /// the one thing on this board a player can do about a warlord other than shoot it.
        ///
        /// <b>The target is said by the tether and by nothing else on the line.</b> A ring used
        /// to close over the chosen ward as well; it was withdrawn because a circle drawn around
        /// the player's own turret reads as something being done *to* the turret rather than as a
        /// warning. If the tell ever stops being read, the thing to strengthen is the reach from
        /// the boss (`SiegeView.Storm`), not a mark on the line.
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
            Vector2 from = mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .10f);

            // **A roar is thrown at the ground it is standing on**, so it has no ward and its
            // "flight" is an expanding ring rather than something crossing the hill.
            //
            // **It asks the rule rather than restating half of it**, which its own comment already
            // claimed and the code did not: this read `Craft != Rally && Ward >= 0`, naming one of
            // the three unaimed crafts and leaving the other two to be caught by an index nobody
            // set. That happened to work and it is the shape invariant 5d warns about — a clause
            // carrying a rule that has moved twice since, under a comment pointing at the rule it
            // was supposed to be. The index test is kept as well because a -1 here would be a lane
            // position on the ward line, not a refusal.
            bool aimed = SiegeTuning.AimsAtAWard(mob.Kind) && cast.Ward >= 0;

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

            // **The spell that opens a phase gathers more**, because it is the one the guard
            // stood in front of and the one the roar a beat earlier promised (`SiegeView.Turned`).
            Gather(mob, cast.Opens ? 1.4f : 1f);

            // The tell. **Nothing is ever drawn on the ward itself** — an aimed spell is announced
            // from the caster's end, by the gather above and the tether below. A roar has no target
            // at all, so what closes is a ring on the warbringer itself, which says "something is
            // about to happen *here*" about the boss and never about the line.
            //
            // **It belongs to the roar, not to "everything that is not aimed", and that
            // distinction was bought by two bosses shipping wearing it.** A ring was drawn for the
            // one spell in the mode that has nothing else to say — a warbringer's rally throws no
            // object, lights no tether and lands on the whole line at once — and `!aimed` then
            // silently collected the two crafts added after it. A devour and a raise both throw
            // something the player can watch (the cogs going, the bodies arriving), both have a
            // fourteen-frame cast reel of their own on top of the gather and the storm, and
            // neither needs a seven-cell circle closing over the hill to say a spell is coming.
            // Reported from play on the bonecaller, in one sentence: *what the hell is that*.
            //
            // It was worse than redundant on those two, in the way invariant 37z predicts: the
            // ring took `Casting(Warbringer)` rather than the caster's own colour, so a boss with
            // a palette of its own was announcing itself in another boss's.
            if (cast.Craft == SiegeSpell.Rally) Brace(mob, from);

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

        /// <summary>
        /// Whether a reel is a comet — a head with a tail behind it — rather than something
        /// round, answered from the art and from nothing else.
        ///
        /// <b>Halfway between the only two shapes the bake cuts</b>, so it cannot be a close
        /// call, and the gap is arithmetic rather than a hope. An orb takes
        /// <c>leanest == longest == 1</c>, which <c>SiegeShotBake.Shape</c> returns outright, so
        /// its frame is square to the pixel. A comet is clamped to <c>LeanestShot</c>..
        /// <c>LongestShot</c> and then quantised by <c>Pixels</c> to a multiple of sixteen inside
        /// <c>NarrowestShot</c>..<c>WidestShot</c> — so the leanest frame that can actually be
        /// written is 384 / 224 = <b>1.71</b>, not 1.6. The realised set is {1.0} and
        /// [1.71, 6.86], with nothing in between, and 1.3 sits three tenths clear of both ends.
        /// That gap is what makes reading the sprite safe where a table of kinds would be a
        /// second opinion — see the call in <see cref="Hurl"/>.
        /// </summary>
        static bool Tailed(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) return false;

            var first = frames[0];
            if (first == null || first.rect.width <= 0f) return false;

            return first.rect.height / first.rect.width > 1.3f;
        }

        /// <summary>How low a boss's magic sounds. Bigger things speak lower.</summary>
        static float Pitch(SiegeKind kind)
            => kind == SiegeKind.Overlord ? .50f
             : kind == SiegeKind.Warbringer ? .55f
             : kind == SiegeKind.Blightcaller ? .92f : .74f;

        /// <summary>The light a warlord gathers before a spell leaves it, at <paramref name="swell"/> of its size.</summary>
        void Gather(Mob mob, float swell = 1f)
        {
            var glow = mob.Charge;
            if (glow == null) return;

            var fire = Casting(mob.Kind);

            Tween.KillChannel(glow, "gather");

            Tween.Run(SiegeTuning.BossTell, Ease.InQuad, t =>
            {
                if (!glow) return;
                glow.color = Pal.A(fire, t * .95f);
                glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.35f, 1.15f * swell, t);
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
        /// The tell a warbringer wears, which is a ring closing on <em>itself</em>.
        ///
        /// A roar has no target, so the thing a player has to read is not "which ward" but "how
        /// long" — and the answer to it is not a mending, it is a firepot into whatever the roar
        /// is about to set running. <b>The one ring left on this board that belongs to a boss</b>,
        /// and it is drawn on the boss: a ring that closes means something is about to happen
        /// *here*, which is only ever honest over the thing doing it.
        ///
        /// <para>
        /// <b>The roar's alone.</b> See the one call site for the two bosses that wore it for a
        /// chapter each by standing on the wrong side of a <c>!aimed</c>.
        /// </para>
        /// </summary>
        void Brace(Mob mob, Vector2 at)
        {
            // The caster's own fire, rather than the warbringer's written out. It is the same
            // colour today, because a warbringer is the only thing that calls this — and a
            // constant that is right only because of where it happens to be called from is what
            // put another boss's colour on the bonecaller.
            var ring = UIKit.Img("Brace", _fx, Art.Ring(128, 12f),
                                 Pal.A(Casting(mob.Kind), .9f),
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
             : kind == SiegeKind.Blightcaller ? 1.35f

             // **An arrow is the smallest thing thrown in this mode and the heaviest slam is the
             // second largest**, and both numbers are the rule above being obeyed rather than a
             // taste. A bind takes no health at all, so it has to read lighter than anything that
             // does — lighter even than a hex, because a hex at least takes the fuel with it. An
             // ironclad's strike takes less health than a warlord's and is drawn bigger than one:
             // that is not a contradiction, because what the drawing has to say is that the axe
             // came down, and the thing it is really announcing is the aegis it stands behind.
             : kind == SiegeKind.Shackler ? 1.15f
             : kind == SiegeKind.Ironclad ? 1.9f : 1.6f;

        static float BurstAt(SiegeKind kind)
            => kind == SiegeKind.Overlord ? 1.2f
             : kind == SiegeKind.Blightcaller ? .85f
             : kind == SiegeKind.Shackler ? .8f
             : kind == SiegeKind.Ironclad ? 1.15f : 1f;

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

            // **Half again the width of a ward's bolt, and anchored wherever its own picture
            // says.** Most of these are orbs — baked square, so the thing *is* the frame and its
            // middle is where it is. Two are not: a shackler looses an arrow and an ironclad
            // brings a blade down, and a comet's frame is mostly the tail behind the head, so
            // anchoring one at its middle would draw it half a length past wherever it had got to.
            //
            // **Read off the sprite rather than switched on the kind**, which is `Lend`'s own rule
            // one step further: the bake decides orb or comet (`SiegeShotBake.Shot.Comet`) and a
            // second table here saying which is which is a second opinion that goes stale the
            // first time a row is re-baked. The aspect cannot be ambiguous — a square frame is 1.0
            // and the leanest comet the bake will ever cut is 1.6.
            var puff = Lend(frames, Color.white, Cell * ThrownAt(kind) * scale, from, angle, 30f,
                            true, Tailed(frames) ? HeadAt : .5f);
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
                //
                // **One pooled ember rather than a general-purpose burst, and that is worth
                // 108 GameObjects a cast** — see `SiegeView.Cinder`, which records what this line
                // used to cost and why the cost landed on the whole board's geometry rather than
                // here. It also reads better: what a wake wants is a thinning line of embers
                // falling away from the head, and what it was getting was a radial scatter with a
                // glow core in the middle of it, fired twenty-seven times over four tenths of a
                // second. A trail is a direction, and a burst has none.
                if (Time.unscaledTime - trail > .05f)
                {
                    trail = Time.unscaledTime;

                    var back = (from - to).normalized * Cell * .55f;
                    back += new Vector2(Random.Range(-Cell * .2f, Cell * .2f),
                                        Random.Range(-Cell * .2f, Cell * .2f));

                    Cinder(at, fire, Cell * .26f, .3f, back);
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
        /// A gravemaw's mouth opening on the ground it is standing on.
        ///
        /// <para>
        /// <b><see cref="Roar"/> read backwards, and that is the design rather than a saving.</b>
        /// The two are the mode's only pair of spells that throw nothing and land nowhere, so the
        /// one thing separating them has to be the thing they are: a roar is pressure going out
        /// and a devour is a pull coming in. Same two reels used the same two ways — one upright
        /// on the caster, one flat over the ground — and then the rings run the other way and the
        /// hill is drawn *up* the board instead of the board being shoved off it.
        /// </para>
        /// <para>
        /// <b>Its own reels now.</b> This wore the warbringer's for two chapters under a green
        /// tint, which is invariant 37z's fault exactly: the picture was a shockwave with shards
        /// in it, which is a thing being pushed away, drawn over a spell whose whole meaning is
        /// that nothing gets away. See <c>SiegeShotBake.Maw</c>.
        /// </para>
        /// </summary>
        void Maw(Vector2 at, SiegeKind kind)
        {
            var fire = Casting(kind);

            // The floor going first, and wider than the mouth — a pull is felt further out than
            // it is seen, which is the reading a roar gets from the same pair the other way up.
            var ground = SpellMuzzleArt(kind);
            if (ground != null && ground.Length > 0)
                Ends(Lend(ground, Pal.A(fire, .85f), Cell * 8.2f,
                          at + new Vector2(0f, Cell * .22f), 0f, 30f, false, .5f), .55f);

            var mouth = SpellHitArt(kind);
            if (mouth != null && mouth.Length > 0)
                Ends(Lend(mouth, Color.white, Cell * 5.4f, at, 0f, 32f, false, .5f), .5f);

            // Three rings **closing**, widest first, a beat apart. The roar's three open and each
            // is wider than the last; these arrive from further out each time and shrink onto the
            // thing doing it, which is the same grammar saying the opposite word.
            for (int i = 0; i < 3; i++)
                Swallow(at, Pal.Lift(fire, .3f), 4.6f + i * 2.4f, .42f, i * .1f);

            // The hill itself starting to go. These are the motes a wind-up drags in
            // (`SiegeView.Drawn`), spent here at four times the count and from twice as far —
            // what a player has to understand in this half-second is that the *ground* is moving,
            // so that when `Swallowed` takes their cogs and firepots a moment later it reads as
            // this having happened rather than as things vanishing.
            for (int i = 0; i < 14; i++)
            {
                float when = i * .022f;
                float turn = Random.Range(0f, Mathf.PI * 2f);
                var away = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn) * .55f)
                           * Cell * Random.Range(3.4f, 6.2f);

                Tween.After(when, () => { if (_fx != null) Drawn(at, away, fire); }, _fx);
            }

            ShakeBoard(16f);
            Audio.Sfx("whoosh", .8f, .4f);
        }

        /// <summary>One ring arriving from outside and closing onto the thing that opened it.</summary>
        void Swallow(Vector2 at, Color tint, float from, float seconds, float delay)
        {
            var ring = UIKit.Img("Swallow", _fx, Art.Ring(128, 10f), Pal.A(tint, 0f),
                                 new Vector2(Cell * 1.4f, Cell * 1.4f));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            Tween.Run(seconds, Ease.InQuad, t =>
            {
                if (!rt) return;

                rt.localScale = Vector3.one * Mathf.Lerp(from, .6f, t);

                // Brightening as it closes rather than fading as it goes, which is the other half
                // of reading as a pull: a shockwave spends itself on the way out and this arrives.
                ring.color = Pal.A(tint, t < .25f ? t / .25f * .85f : .85f * (1f - (t - .25f) / .75f * .35f));
            }, ring, "swallow").Delay(delay)
             .OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// A bonecaller reaching for the top of the hill, drawn the instant the spell leaves it.
        ///
        /// <b>The quieter half of a raise, deliberately.</b> What the player has to watch is the
        /// crest, because that is where the bodies arrive and that is a thing they will have to
        /// answer — so the caster gets a bloom and a reach and the crest gets the rest of it on
        /// the arrival (<see cref="Rise"/>), which is also the beat the raiders are hatched on.
        /// Drawing the loud half here would put the ceremony half a second before the event.
        /// </summary>
        void Crypt(Vector2 at, SiegeKind kind)
        {
            var fire = Casting(kind);

            var bloom = SpellHitArt(kind);
            if (bloom != null && bloom.Length > 0)
                Ends(Lend(bloom, Color.white, Cell * 4.4f, at, 0f, 32f, false, .5f), .45f);

            var crest = new Vector2(0f, MarchY(SiegeTuning.RaiseAt));

            // The reach. Two flickers rather than a held line, for the tether's reason — a line
            // that stays lit is a beam, and the beams on this board come the other way.
            for (int i = 0; i < 2; i++)
                Arc(at, crest, fire, Cell * .05f, .26f, 2, .5f, i * .12f);

            ShakeBoard(9f);
            Audio.Sfx("whoosh", .6f, 1.3f);
        }

        /// <summary>
        /// The aegis, drawn on the ironclad and never on the line.
        ///
        /// <b>A verb with no event is a contradiction, and this is the drawing's half of the
        /// answer.</b> What an aegis does is refuse three quarters of the line — it is a rule
        /// about what may hurt the *boss*, and it takes nothing from any ward — so a drawing at a
        /// post would be the picture inventing a threat that the rules do not contain. A ring
        /// closing over the thing it protects is a sentence this board has already taught with
        /// <see cref="Brace"/>, said here about armour rather than about a shout.
        /// </summary>
        void Guard(Mob mob, Vector2 at, Color fire)
        {
            if (mob == null || mob.Node == null) return;

            var ring = UIKit.Img("Guard", _fx, Art.Ring(128, 16f), Pal.A(fire, 0f),
                                 new Vector2(Cell * 1.6f, Cell * 1.6f));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            Tween.Run(.42f, Ease.OutCubic, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(3.4f, 2.2f, t);
                ring.color = Pal.A(fire, t < .3f ? t / .3f * .8f : .8f * (1f - (t - .3f) / .7f));
            }, ring, "guard").OnDone(() => { if (ring) Destroy(ring.gameObject); });
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

            // **Two spells that land on the hill rather than on the line**, so they are drawn
            // where the boss is standing rather than at a post - and neither has a ward to come
            // back to, which is why they answer here rather than falling through the guard below.
            if (spell.Craft == SiegeSpell.Devour) { Feed(caster, fire); return; }
            if (spell.Craft == SiegeSpell.Raise) { Rise(caster, fire); return; }

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
            // **A chain lands quietly and on the chassis rather than on the tube.** A douse is
            // drawn at the fuel readout because what it took was in there (invariant 37y); a bind
            // takes nothing that is drawn anywhere, so the only honest place to put it is the
            // machine itself, and the state `Charge` keeps on the post is what the player really
            // reads. Iron sparks rather than the boss's own fire, because there is no fire in it.
            if (spell.Craft == SiegeSpell.Bind)
            {
                Chained(spell.Ward, fire);
                return;
            }

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

            // **A beat of slow motion on the blow**, the whole hill together (`Dilate`, invariant
            // 37cq) - short enough that a fight of a dozen spells does not crawl, and longer on
            // the one that takes a ward down, which is the heaviest thing that can happen here.
            Dilate(spell.Felled ? .25f : .45f, spell.Felled ? .45f : .14f);
        }

        /// <summary>
        /// A gravemaw feeding: the ring it opens, drawn where it stands.
        ///
        /// <b>Quiet, for the douse's reason</b> - it takes no ward health at all, so a drawing
        /// that shook the board would be the picture overstating the rule. What has to be noticed
        /// is the loose things on the ground leaving, which <see cref="Swallowed"/> draws and this
        /// only announces.
        /// </summary>
        void Feed(Mob caster, Color fire)
        {
            if (caster == null || caster.Node == null) return;

            var at = caster.Node.anchoredPosition;

            // Inward rather than outward, which is the whole difference between this and a roar:
            // a ring that closes on the thing casting it is a pull, and a ring that opens off it
            // is a push. Nothing here travels anywhere, so the direction is the sentence.
            //
            // **The mouth is `Maw`'s and the swallowing is `Swallowed`'s; this is the shutting.**
            // One last ring closing tight as the loose things reach it, which is the beat that
            // joins the two — before this it was a `Pop` and a scatter of sparks, which is to say
            // that the moment a gravemaw eats the player's cogs was drawn out of two primitives
            // and no art at all (invariant 47i).
            Swallow(at, Pal.Lift(fire, .35f), 3.6f, .3f, 0f);
            Pop(at, fire, 2.4f, .28f);
            Burst.Sparks(_fx, at, fire, 12, Cell * 2.2f, Cell * .2f, .45f);

            Audio.Sfx("boom", .5f, .62f);
        }

        /// <summary>
        /// A bonecaller raising: a pale light at the caster, and one at the top of the hill where
        /// the dead come up.
        ///
        /// <b>Drawn in two places because it happens in two places</b>, and the second is the one
        /// that matters: the raiders themselves appear at the top of the hill and walk down like
        /// any other wave, so what has to link them to the boss is a light in both places at the
        /// same instant. The bodies are hatched by the ordinary raider path a frame later, which
        /// is what keeps a raised creeper identical in every way to a mustered one.
        /// </summary>
        void Rise(Mob caster, Color fire)
        {
            var kind = caster != null ? caster.Kind : SiegeKind.Bonecaller;

            if (caster != null && caster.Node != null)
            {
                var at = caster.Node.anchoredPosition;
                Pop(at, fire, 3.4f, .34f);
                Shockwave(at, fire, 4.2f, .40f);
            }

            // The top of the hill, which is where `SiegeTuning.RaiseAt` puts them.
            var crest = new Vector2(0f, MarchY(SiegeTuning.RaiseAt));

            // **The loud half, and it is the one that had no art in it.** A shockwave and a
            // scatter of sparks is a primitive and a primitive (invariant 47i) — so the moment a
            // bonecaller puts a fresh group at the top of the hill, which is the single biggest
            // thing that boss does to a run, was two circles. The bloom is drawn widest here
            // because this is the beat the bodies are hatched on, and the ground wash under it is
            // what makes them read as coming *up* rather than as having walked on.
            var ground = SpellMuzzleArt(kind);
            if (ground != null && ground.Length > 0)
                Ends(Lend(ground, Pal.A(fire, .8f), Cell * 9f,
                          crest + new Vector2(0f, Cell * .2f), 0f, 30f, false, .5f), .55f);

            var bloom = SpellHitArt(kind);
            if (bloom != null && bloom.Length > 0)
                Ends(Lend(bloom, Color.white, Cell * 6.4f, crest, 0f, 32f, false, .5f), .5f);

            Shockwave(crest, fire, 7.5f, .55f);
            Burst.Sparks(_fx, crest, fire, 22, Cell * 5f, Cell * .28f, .7f);

            // Shards coming up out of the ground on the spot the bodies will stand on. Upward
            // only — a raise is the one thing on this board that arrives from *under* it, and
            // that is the half-second the reading has to happen in.
            for (int i = 0; i < 7; i++)
            {
                var spot = crest + new Vector2(Random.Range(-Cell * 2.6f, Cell * 2.6f), 0f);

                Arc(spot + new Vector2(0f, -Cell * .5f), spot + new Vector2(0f, Cell * 1.5f),
                    fire, Cell * .05f, .28f, 1, .35f, i * .045f);
            }

            ShakeBoard(12f);
            Audio.Sfx("boom", .62f, 1.25f);
        }

        /// <summary>
        /// Loose things a gravemaw ate, taken off the hill toward the thing that ate them.
        ///
        /// <para>
        /// <b>This owns the widgets rather than letting the poll notice them gone.</b> Both
        /// <c>Gears</c> and <c>Fuses</c> reconcile against the board every frame, so a devoured cog
        /// would sink and a devoured bomb would simply vanish - which is what a cog running out of
        /// time and a bomb being tapped already look like. A player whose ranks and firepots
        /// disappeared for no visible reason has met a bug, not a boss (invariant 5f).
        /// </para>
        /// <para>
        /// <b>Called from the clock rather than from <see cref="Smite"/></b>, because what was
        /// eaten is a list on the report and a spell record carries one ward. It runs after the
        /// spell loop, so the ring is already open, and before the two polls, so they find nothing
        /// left to tidy.
        /// </para>
        /// </summary>
        void Swallowed(IReadOnlyList<int> ids)
        {
            var maw = _mob.Count > 0 ? MawNode() : null;

            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];

                var gear = GearOf(id);
                if (gear != null)
                {
                    _gears.Remove(gear);
                    Drawn(gear.Node, maw);
                    continue;
                }

                var fuse = FuseOf(id);
                if (fuse == null) continue;

                _fuses.Remove(fuse);
                Drawn(fuse.Node, maw);
            }
        }

        /// <summary>Where the thing doing the eating is, or null when it is already dead.</summary>
        RectTransform MawNode()
        {
            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Kind == SiegeKind.Gravemaw) return _mob[i].Node;

            return null;
        }

        /// <summary>
        /// One loose thing pulled off the hill and swallowed.
        ///
        /// <b>It goes to the maw rather than simply away</b>, which is the only thing that says
        /// what took it. With nothing to go to - a gravemaw killed on the frame its spell landed -
        /// it sinks where it stands, which is the trampled drawing and is honest: it is gone and
        /// nothing is there to have taken it.
        /// </summary>
        void Drawn(RectTransform node, RectTransform maw)
        {
            if (node == null) return;

            var from = node.anchoredPosition;
            var to = maw != null ? maw.anchoredPosition : from + new Vector2(0f, -Cell * .35f);

            Tween.KillAll(node);

            Tween.Run(.34f, Ease.InQuad, t =>
            {
                if (!node) return;

                node.anchoredPosition = Vector2.Lerp(from, to, t);
                node.localScale = Vector3.one * (1f - t * .85f);
            }, node, "eaten").OnDone(() => { if (node) Destroy(node.gameObject); });
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
        /// A ward being chained by a shackler.
        ///
        /// <b>Deliberately smaller than <see cref="Snuffed"/>, because it takes less.</b> A douse
        /// empties the tube and the drawing follows the loss down to where the loss was; a bind
        /// takes six seconds and leaves everything, so a burst of the same weight would be the
        /// picture overstating the rule — the same argument the douse's own note makes against
        /// flashing the screen. What has to read is that the turret is *held*, and that is the
        /// standing state rather than this instant.
        /// </summary>
        void Chained(int ward, Color fire)
        {
            var post = _posts[ward];
            var at = new Vector2(PostX(ward), _lineY + Cell * .18f);

            Burst.Sparks(_fx, at, fire, 8, Cell * 1.5f, Cell * .14f, .5f);
            Tween.Shake(post.Node, Cell * .06f, .22f);

            // **The chain's own clip, and it is the one sound in the library that is a *fastening*
            // rather than a blow.** A douse is a `whoosh` because a light goes out; a bind is a
            // thing being shut, so `blocked` — which is what this game plays when an input is
            // refused — is the right noise in the right place: what the player is being told is
            // that a turret has stopped answering.
            Audio.Sfx("blocked", .55f, .8f);
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

        /// <summary>
        /// What a boss of this kind is announced as.
        ///
        /// <b>One place, because two things say it now</b>: the banner when it walks on, and the
        /// forecast that warns it is coming. Written out rather than keyed off the kind's own name,
        /// for invariant 6's reason - a loc key built by concatenation is a key the build gate
        /// cannot see.
        ///
        /// <b>Public so a fixture can walk every kind through it</b>, which is what stops the
        /// `default` arm below being a real answer for a boss nobody thought about - see
        /// <c>SiegeCaptionTests.EveryBossIsAnnouncedAsItself</c>.
        /// </summary>
        public static string BossKey(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return "mode.siege.overlord";
                case SiegeKind.Warbringer: return "mode.siege.warbringer";
                case SiegeKind.Blightcaller: return "mode.siege.blightcaller";
                case SiegeKind.Gravemaw: return "mode.siege.gravemaw";
                case SiegeKind.Bonecaller: return "mode.siege.bonecaller";
                case SiegeKind.Shackler: return "mode.siege.shackler";
                case SiegeKind.Ironclad: return "mode.siege.ironclad";

                // **The warlord, and it is the only kind that may fall through here.** Invariant
                // 44e's rule: a `default` that is a real answer hides the case nobody is looking
                // at, and a boss announced under another boss's name is exactly the moment this
                // switch exists to get right. `SiegeCaptionTests` holds every kind to a key of its
                // own, so a seventh boss fails here rather than arriving as a warlord.
                default: return "mode.siege.boss";
            }
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
            string banner = BossKey(kind);

            Announce(boss ? Loc.Get(banner) : Loc.Format("mode.siege.wave", _wave, _board.Waves),
                    boss ? Casting(kind) : Pal.Cream, boss ? .78f : .46f, boss ? 2.4f : 1.5f, boss);

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

                // Its own voice rather than a firepot's boom pitched down: a boss stepping onto
                // the hill is the one arrival in the mode that is *news*, and news is said in a
                // voice nothing else uses (`Tools/sfx.tsv`, `boss`).
                Audio.Sfx("boss", .7f + weight * .3f, Pitch(kind) + .2f);
                ShakeBoard(14f + weight * 14f);
                Flow.Flash(Pal.A(Casting(kind), 1f), .34f + weight * .28f, .45f);

                // **And the hill slows as it steps on**, for a beat: the same dilation a charm
                // buys, spent on the entrance (invariant 37cq - the model is handed the seconds,
                // so the fight is not a function of this number).
                Dilate(.4f, .55f);
            }
            else
            {
                Flow.Flash(new Color(1f, .55f, .45f), .18f, .35f);
            }
        }

        /// <summary>
        /// The hill's one banner, saying <paramref name="text"/> in <paramref name="colour"/> at
        /// <paramref name="size"/> cells, floating up for <paramref name="seconds"/>.
        ///
        /// <b>One label for every piece of news the hill has</b> - a wave, a boss arriving, a
        /// phase turning, a boss falling - so two can never stack, and the newest always wins.
        /// **Stacked clear above the chain banner, from the ward line rather than from the
        /// hill's foot.** Those two anchors sit between .45 and .71 of a cell apart depending
        /// on the display, so measuring one caption from each is what let them share a row on
        /// every shape. See `SiegeView.Captions`.
        /// </summary>
        void Announce(string text, Color colour, float size, float seconds, bool swell)
        {
            if (_waveLabel == null) return;

            _waveLabel.text = text;
            _waveLabel.color = colour;
            _waveLabel.fontSize = Mathf.RoundToInt(Cell * size);

            var ladder = Caption;

            var group = UIKit.Group(_waveLabel.rectTransform);
            var rt = _waveLabel.rectTransform;

            Tween.KillAll(_waveLabel);
            rt.anchoredPosition = new Vector2(0f, ladder.Wave);
            rt.localScale = Vector3.one * (swell ? WaveSwell : 1f);
            group.alpha = 0f;

            Tween.Fade(group, 1f, .22f);
            if (swell) Tween.Scale(rt, 1f, .5f, Ease.OutBack);

            Tween.Move(rt, new Vector2(0f, ladder.Wave + Cell * WaveFloat), seconds, Ease.OutCubic)
                 .OnDone(() => Tween.Fade(group, 0f, .4f));
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
            // Reachable rather than defensive: a board event can land between a screen being
            // handed a board and the first repaint building the layers, and a figure with nowhere
            // to go is not drawn rather than thrown.
            if (_figures == null) return;

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
            //
            // **On `_figures` rather than `_fx`**, so nothing drawn afterwards covers it — see
            // `SiegeView.Build`, where the layer is the last one made for exactly this.
            tally.Label = UIKit.Titled("Hit", _figures, damage.ToString(), 24, Pal.Cream,
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

            // **It lifts, and then it stands still.** The first cut floated a cell and a half
            // over a second and the second floated half a cell over the whole of its life — both
            // of them numbers still travelling at the moment they are being read, which on a
            // board sending the next one 55 milliseconds behind reads as figures climbing the
            // screen. What a floating number owes the player is to be legible on arrival and then
            // get out of the way, so the lift is a quarter of the life and a fifth of a cell: it
            // pops clear of the body it came off, holds where the eye caught it, and fades from
            // there.
            float life = tally.Weak ? .68f : .52f;
            Vector2 from = rt.anchoredPosition;
            Vector2 to = from + new Vector2(tally.Drift * Cell * .10f,
                                            Cell * (tally.Weak ? .26f : .20f));

            Tween.Run(life * .28f, Ease.OutCubic, k =>
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
