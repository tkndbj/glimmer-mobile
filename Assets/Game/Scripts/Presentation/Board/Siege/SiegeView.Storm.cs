using System.Collections.Generic;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The bosses' <b>storm</b>: the lightning, the volleys and the idle crackle that fill the
    /// seconds a boss used to spend standing still.
    ///
    /// <para>
    /// <b>Every line of this is drawing and none of it is a rule.</b> A spell is still telegraphed
    /// for <c>SiegeTuning.BossTell</c> and still lands <c>BossFlight</c> after that, it still takes
    /// exactly what <c>SiegeBoard</c> says it takes, and a cast still comes round on
    /// <c>SiegeTuning.CastEveryFor</c>. What changed is what a player <em>watches</em> across that
    /// window — reported from a device as bosses that "do their attack every 3-4 seconds with 1
    /// simple vfx animation", which is a complaint about the drawing and not about the fight.
    /// Invariant 37s is the reason the fix could not be "make it quicker": the schedule is a rule
    /// the player plays a mending against, so the honest answer is to spend the window rather than
    /// shorten it.
    /// </para>
    /// <para>
    /// <b>Procedural rather than baked, and that is a decision rather than a shortcut.</b> Every
    /// other effect in this mode is a sprite reel out of a bought pack
    /// (<c>SiegeShotBake</c>) — which is right for a thing that always looks the same, and wrong
    /// for lightning: a bolt that is the <em>same</em> bolt twice reads as a stamp rather than as
    /// electricity, and a chain has to reach two points a board decides at run time. These are
    /// polylines of <see cref="Art.Capsule"/> and <see cref="Art.SoftCapsule"/>, generated at run
    /// time, so there is no address to register, no group to belong to, no scope to load and no
    /// frame where a strike is a white rectangle (invariant 7b). It also means this works on a
    /// checkout with no licensed pack in it, which is the one thing the baked reels cannot say.
    /// </para>
    /// <para>
    /// <b>The cost is bounded by the events rather than by the board.</b> A crackle is a couple of
    /// dozen images alive for a fifth of a second and a boss makes one about twice a second; a
    /// volley is at most a few hundred over a second and a half and happens once every four to
    /// nine. Nothing here is per-frame, and nothing here is pooled for that reason — the pool
    /// (<see cref="Lend"/>) exists for the twenty-eight bolts a second the ward line fires, which
    /// is a different order of thing entirely.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ one bolt of lightning
        /// <summary>
        /// How many pieces a bolt is broken into, at most.
        ///
        /// <b>A ceiling rather than a count</b>, because the length is what decides it: a hop
        /// between two neighbouring wards and a strike out of the sky are the same call, and a
        /// fixed segment count would make the short one a scribble and the long one a smooth
        /// curve. Segments are about half a cell each, capped here so a bolt drawn across the whole
        /// board is still two dozen images rather than a hundred.
        /// </summary>
        /// <b>Ten rather than fourteen, and the segments a little longer than half a cell.</b>
        /// Each one is three <c>Image</c>s and a storm is nine bolts with forks on them, so this
        /// number is multiplied by about sixty before it reaches a frame — and a bolt with ten
        /// bends in it is not visibly straighter than one with fourteen.
        const int ArcSegments = 10;

        /// <summary>
        /// A single jagged bolt from <paramref name="a"/> to <paramref name="b"/>.
        ///
        /// <para>
        /// <b>Two bars per segment, and both are needed.</b> The core is drawn nearly white — real
        /// lightning is white with a coloured sheath, and a bolt drawn in a flat hue reads as a
        /// painted stick — and the halo under it is the boss's own casting colour, which is what
        /// carries <em>whose</em> lightning this is on a hill already carrying four ward colours
        /// (the same argument <see cref="Casting"/> makes about the orbs).
        /// </para>
        /// <para>
        /// <b>The jag is pinched at both ends.</b> A polyline whose first joint can wander is a
        /// bolt that does not leave the hand that threw it, and one whose last joint can wander is
        /// a bolt that misses what it hit — so the offset is scaled by <c>sin(pi*t)</c>, which is
        /// nought at both ends and widest in the middle. That single term is the difference
        /// between "lightning" and "a broken line".
        /// </para>
        /// <para>
        /// <b>It flickers rather than fading.</b> A bolt that ramps smoothly to nothing reads as a
        /// beam being switched off; what it does instead is stutter on a fast sine against a
        /// falling envelope, which is the one thing everybody's eye already knows about
        /// electricity.
        /// </para>
        /// </summary>
        /// <summary>
        /// A point pulled back inside the board.
        ///
        /// <b>A bolt that leaves the plate is a bolt drawn on the app's background</b>, and
        /// nothing about the effects layer stops one: <c>_fx</c> is sized to the field and carries
        /// no mask, so a strike out of the sky and a bolt thrown off a boss's hand both draw
        /// happily over the status bar. <c>Tools/render_siege.py</c> is what said so, on the first
        /// frame it ever drew of this — a placement, which is exactly what no numeric gate in this
        /// project can look at (invariants 37g, 37u).
        /// </summary>
        Vector2 OnBoard(Vector2 p)
        {
            var half = Span * .5f;
            return new Vector2(Mathf.Clamp(p.x, -half.x, half.x),
                               Mathf.Clamp(p.y, -half.y, half.y));
        }

        void Arc(Vector2 a, Vector2 b, Color tint, float wide, float life,
                 int forks = 0, float jag = .34f, float delay = 0f)
        {
            if (_fx == null) return;

            a = OnBoard(a);
            b = OnBoard(b);

            // **A delayed bolt is *built* late as well as shown late, and that is the difference
            // between a storm and a hitch.** A warbringer throws nine strikes staggered across
            // half a second; built up front that is several hundred `Image`s created — and a
            // canvas rebuilt — inside one frame, with the stagger only deciding when they are
            // faded up. Deferring the construction spreads the same work over the frames it is
            // spread across on screen. The delay is the only reason this branch exists: an
            // undelayed bolt is built here and now, as it always was.
            if (delay > 0f)
            {
                Tween.After(delay, () => Struck(a, b, tint, wide, life, forks, jag), _fx);
                return;
            }

            Struck(a, b, tint, wide, life, forks, jag);
        }

        /// <summary>One bolt, built and lit now. See <see cref="Arc"/>, which owns the delay.</summary>
        void Struck(Vector2 a, Vector2 b, Color tint, float wide, float life, int forks, float jag)
        {
            if (_fx == null) return;

            var host = UIKit.Node("Arc", _fx);
            var group = UIKit.Group(host);
            group.alpha = 0f;

            Fork(host, a, b, tint, wide, jag, forks);

            float seed = Random.Range(0f, 40f);

            Tween.Run(life, Ease.Linear, t =>
            {
                if (!group) return;

                // Held bright for the first fifth and only then let go, because a bolt that starts
                // dying on its first frame is one nobody saw arrive.
                float envelope = t < .2f ? 1f : 1f - (t - .2f) / .8f;
                float flicker = .58f + .42f * Mathf.Abs(Mathf.Sin(t * 34f + seed));

                group.alpha = envelope * flicker;
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });
        }

        /// <summary>One bolt and the branches that come off it.</summary>
        void Fork(RectTransform host, Vector2 a, Vector2 b, Color tint, float wide, float jag,
                  int forks)
        {
            var joints = Joints(a, b, jag);
            Trace(host, joints, tint, wide);

            for (int f = 0; f < forks; f++)
            {
                // A branch leaves from somewhere along the trunk and goes a third of the way again
                // in a direction of its own, which is what a fork is. It never reaches the target,
                // deliberately: a second line arriving where the first one did would read as two
                // bolts rather than as one splitting.
                int at = Random.Range(1, Mathf.Max(2, joints.Count - 1));
                var root = joints[at];

                var away = (b - a).normalized;
                float turn = Random.Range(.5f, 1.3f) * (Random.value < .5f ? -1f : 1f);
                var side = new Vector2(-away.y, away.x);

                var tip = root + (away + side * turn).normalized
                                 * (b - a).magnitude * Random.Range(.22f, .42f);

                Trace(host, Joints(root, tip, jag * 1.4f), tint, wide * .58f);
            }
        }

        /// <summary>The joints of one bolt: a straight run pushed sideways, pinched at both ends.</summary>
        List<Vector2> Joints(Vector2 a, Vector2 b, float jag)
        {
            var dir = b - a;
            float len = dir.magnitude;

            int steps = Mathf.Clamp(Mathf.RoundToInt(len / (Cell * .7f)), 3, ArcSegments);
            var side = len > .001f ? new Vector2(-dir.y, dir.x) / len : Vector2.right;

            var joints = new List<Vector2>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float pinch = Mathf.Sin(t * Mathf.PI);
                float push = Random.Range(-1f, 1f) * jag * Cell * pinch;

                joints.Add(Vector2.Lerp(a, b, t) + side * push);
            }

            return joints;
        }

        /// <summary>Lays a bar along every joint of a polyline.</summary>
        void Trace(RectTransform host, List<Vector2> joints, Color tint, float wide)
        {
            for (int i = 0; i + 1 < joints.Count; i++) Bar(host, joints[i], joints[i + 1], tint, wide);
        }

        /// <summary>One straight piece of a bolt: a coloured sheath with a white filament in it.</summary>
        void Bar(RectTransform host, Vector2 a, Vector2 b, Color tint, float wide)
        {
            var dir = b - a;
            float len = dir.magnitude;
            if (len < .5f) return;

            // The capsule sprites are drawn along +Y and sliced, so a bar is the segment's own
            // length in the Y of a node turned to face along it. Round caps are what make the
            // joints of a polyline meet without a notch at every bend.
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            var mid = (a + b) * .5f;

            // **Three bars rather than two, and the middle one is why the colour survives.** A
            // white filament inside a soft glow is what lightning looks like in isolation and not
            // what it looks like over a lit hill: at the size a phone draws it the glow is thin
            // enough to read as an edge, so the bolt comes out white and a magenta overlord throws
            // the same lightning a teal blightcaller does. `Tools/render_siege.py` said so at the
            // first look. The sheath is drawn at full strength in the boss's own colour at about
            // twice the filament's width, so the bolt carries its colour even where the glow is
            // lost against bright ground.
            var halo = UIKit.Img("h", host, Art.SoftCapsule(40, 120), Pal.A(tint, .55f),
                                 new Vector2(wide * 4.2f, len + wide * 3.2f));
            halo.rectTransform.anchoredPosition = mid;
            halo.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

            var sheath = UIKit.Img("s", host, Art.Capsule(24, 96), Pal.A(tint, .95f),
                                   new Vector2(wide * 2f, len + wide * 1.2f));
            sheath.rectTransform.anchoredPosition = mid;
            sheath.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

            var core = UIKit.Img("c", host, Art.Capsule(24, 96),
                                 Pal.A(Color.Lerp(tint, Color.white, .62f), 1f),
                                 new Vector2(wide, len + wide * .6f));
            core.rectTransform.anchoredPosition = mid;
            core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // ------------------------------------------------------------------ what bolts are used for
        /// <summary>
        /// Lightning hopping from one place to the next, each hop a beat after the last.
        ///
        /// <b>The hops are what make it a chain rather than a fan</b>: a set of bolts leaving one
        /// point at once is a starburst, and what says "this is travelling through things" is that
        /// each one starts where the last one ended and a moment later. The whole walk is fitted
        /// into <paramref name="over"/>, so a chain can be given exactly the window a spell has in
        /// the air and cannot arrive early or late (invariant 37s).
        /// </summary>
        void Chain(IList<Vector2> path, Color tint, float wide, float over, float life,
                   float delay = 0f, int forks = 1)
        {
            if (path == null || path.Count < 2) return;

            int hops = path.Count - 1;
            float step = hops > 1 ? over / (hops - 1) : 0f;

            for (int i = 0; i < hops; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                float when = delay + i * step;

                Arc(a, b, tint, wide, life, forks, .34f, when);

                // A flash where each hop lands, so the eye is told the bolt arrived somewhere
                // rather than merely passed through. The last one is left to whatever the spell
                // itself draws on landing, which is louder than this.
                float at = when;
                if (i + 1 < hops)
                    Tween.After(at + life * .25f, () =>
                    {
                        if (_fx == null) return;
                        Pop(b, tint, 1.5f, .22f);
                        Burst.Sparks(_fx, b, tint, 5, Cell * 1.1f, Cell * .13f, .3f);
                    }, _fx);
            }
        }

        /// <summary>
        /// A bolt out of the sky onto one point of the board, with the ground answering it.
        ///
        /// <b>It comes down from above the hill rather than from the boss</b>, which is what makes
        /// a volley read as weather rather than as more throwing: a boss that only ever emits
        /// things has one direction, and a strike gives the board a second one. The start is
        /// jittered sideways so four of them onto one ward are four bolts and not one bolt drawn
        /// four times.
        /// </summary>
        void Strike(Vector2 at, Color tint, float wide, float delay = 0f, float life = .30f)
        {
            // **The top of the hill, not the sky above it**, because there is no sky: the board
            // ends at the plate and a bolt started above it is drawn over whatever the phone has
            // up there. `_hillTop` is the top edge of the field, so a strike falls the whole depth
            // of the hill, which is as far as a bolt on this board can travel downward anyway.
            float sky = _hillTop - Cell * Random.Range(0f, .5f);
            var from = new Vector2(at.x + Random.Range(-Cell * .8f, Cell * .8f), sky);

            Arc(from, at, tint, wide, life, 1, .26f, delay);

            Tween.After(delay + life * .18f, () =>
            {
                if (_fx == null) return;

                Pop(at, tint, 2.4f, .26f);
                Shockwave(at, Pal.Lift(tint, .4f), 2.6f, .3f);
                Burst.Sparks(_fx, at, tint, 8, Cell * 1.8f, Cell * .18f, .38f);
            }, _fx);
        }

        /// <summary>
        /// Short bolts crawling over a body.
        ///
        /// <b>The one drawing here that is used both as decoration and as a warning</b>, and that
        /// is deliberate rather than lazy: a boss crackles gently the whole time it is standing
        /// there, and the same crackle louder and faster is what a wind-up looks like. A player
        /// learns to read it without ever being told, because they have been watching the quiet
        /// version since the thing walked on.
        /// </summary>
        void Crackle(Mob mob, Color tint, float strength = 1f, int bolts = 2)
        {
            if (mob == null || mob.Node == null) return;

            var centre = mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .12f);
            float reach = mob.Height * .38f * strength;

            for (int i = 0; i < bolts; i++)
            {
                float turn = Random.Range(0f, Mathf.PI * 2f);
                var axis = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn));

                Arc(centre - axis * reach, centre + axis * reach, tint,
                    Cell * .05f * strength, .16f + .06f * strength, 1, .30f);
            }
        }

        // ------------------------------------------------------------------ the idle
        /// <summary>
        /// How often a boss crackles while it is doing nothing.
        ///
        /// <b>This is the half of the complaint that is not about the spell at all.</b> A boss
        /// casts every four to nine seconds, so most of the time a player spends looking at one it
        /// is standing still playing a two-second idle loop — which is what "I don't want to wait
        /// 5 seconds" is really about. Something has to be happening on it in between, and it has
        /// to be small enough that the cast is still unmistakably the event (invariant 26f: before
        /// animating anything, ask how often it fires).
        /// </summary>
        const float CrackleEvery = .42f, CrackleSpread = .5f;

        /// <summary>
        /// Keeps every boss on the hill alive between casts.
        ///
        /// <b>Driven off <see cref="Time.unscaledDeltaTime"/> like everything else this mode
        /// draws</b>, because a modal sets <c>Time.timeScale</c> to nought and a boss frozen
        /// mid-crackle under a lesson is the fault this project has already met twice
        /// (invariant 30h).
        /// </summary>
        void Ambient()
        {
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || !mob.Boss || mob.Falling || mob.Node == null) continue;

                mob.Crackle -= Time.unscaledDeltaTime;
                if (mob.Crackle > 0f) continue;

                mob.Crackle = CrackleEvery + Random.Range(0f, CrackleSpread);

                // Quiet, and one bolt: this is a thing breathing rather than a thing casting, and
                // the wind-up has to stay louder than it or the tell stops being a tell. **Behind
                // its guard it breathes harder** - two bolts, and still under the wind-up's five
                // - because a boss that cannot be hurt has to look like it is *doing* something
                // with the seconds it is being given.
                Crackle(mob, Casting(mob.Kind), mob.Guarded ? .85f : .55f, mob.Guarded ? 2 : 1);
            }
        }

        // ------------------------------------------------------------------ the wind-up
        /// <summary>
        /// What a boss does across <c>SiegeTuning.BossTell</c>, over and above the light it
        /// gathers and the ring that closes on its target.
        ///
        /// <para>
        /// <b>It accelerates, which is the whole of what a wind-up owes the player.</b> Five
        /// crackles at rising strength over the window say "this is about to go off" in a way a
        /// steady glow cannot, and the last of them lands close enough to the release to read as
        /// the same event.
        /// </para>
        /// <para>
        /// <b>And an aimed boss reaches for its target before it throws.</b> A tether — a thin
        /// bolt flickering between the caster and the ward it has chosen — is drawn over the last
        /// two fifths of the tell, which says <em>who</em> as well as <em>when</em>. <b>It is the
        /// only thing that says which ward</b> — a ring used to close over the chosen post and was
        /// withdrawn, because a circle drawn around the player's own turret reads as something
        /// being done to it. This says the same thing in the boss's own colour and from the boss's
        /// own hand, which is where the threat actually is.
        /// </para>
        /// </summary>
        void Winding(Mob mob, Vector2 from, Vector2 to, bool aimed)
        {
            var fire = Casting(mob.Kind);
            float tell = SiegeTuning.BossTell;

            const int Beats = 5;

            for (int i = 0; i < Beats; i++)
            {
                // Bunched toward the end rather than evenly spaced: the square puts the first two
                // early and the last three almost on top of the release.
                float k = (i + 1) / (float)Beats;
                float when = tell * k * k;
                float strength = .5f + k * 1.1f;
                int bolts = 1 + i / 2;

                Tween.After(when, () =>
                {
                    if (mob.Node == null) return;
                    Crackle(mob, fire, strength, bolts);
                }, mob.Node);
            }

            // Motes dragged in off the hill, so the light it gathers visibly comes from somewhere.
            for (int i = 0; i < 7; i++)
            {
                float when = tell * Random.Range(.05f, .82f);
                float turn = Random.Range(0f, Mathf.PI * 2f);
                var away = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)) * Cell * Random.Range(2f, 3.6f);

                Tween.After(when, () =>
                {
                    if (_fx == null || mob.Node == null) return;
                    Drawn(mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .12f), away, fire);
                }, mob.Node);
            }

            if (!aimed) return;

            // The tether. Three flickers rather than one continuous line, because a line that
            // stays lit is a beam — and this mode already has beams coming the other way.
            for (int i = 0; i < 3; i++)
            {
                float when = tell * (.58f + i * .13f);

                Tween.After(when, () =>
                {
                    if (mob.Node == null) return;
                    Arc(from, to, fire, Cell * .045f, .13f, 0, .22f);
                }, mob.Node);
            }
        }

        /// <summary>A mote falling inward to a gathering spell.</summary>
        void Drawn(Vector2 at, Vector2 away, Color tint)
        {
            var mote = UIKit.Img("Draw", _fx, Art.Glow(64, 2.4f), Pal.A(tint, 0f),
                                 new Vector2(Cell * .3f, Cell * .3f));
            mote.raycastTarget = false;

            var rt = mote.rectTransform;
            var from = at + away;

            Tween.Run(.34f, Ease.InQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = Vector2.Lerp(from, at, t);
                rt.localScale = Vector3.one * Mathf.Lerp(.5f, 1.3f, t);
                mote.color = Pal.A(tint, t < .3f ? t / .3f : 1f);
            }, mote).OnDone(() => { if (mote) Destroy(mote.gameObject); });
        }

        // ------------------------------------------------------------------ the release
        /// <summary>
        /// Everything a boss throws, drawn as the thing it is rather than as one orb.
        ///
        /// <para>
        /// <b>Every one of the four is a volley now, and none of them takes any more or any less
        /// than it did.</b> The board books one hit; what crosses the hill is several things
        /// arriving together, which is the difference between a spell and a projectile. The last
        /// arrival is always on <c>BossFlight</c> exactly, because that is when the damage is
        /// (invariant 37s) — everything else is early.
        /// </para>
        /// <para>
        /// <b>They are told apart by <em>shape of attack</em> and not by colour</b>, which is
        /// invariant 37z's rule about four bosses being four fights carried into the drawing: a
        /// blightcaller chains, a warlord rains, a warbringer storms and an overlord launches a
        /// pair. A player who has met two of them can name the third from across the room.
        /// </para>
        /// </summary>
        void Unleash(Mob mob, Vector2 from, Vector2 to, int ward)
        {
            var kind = mob.Kind;
            var fire = Casting(kind);
            float flight = SiegeTuning.BossFlight;

            // The moment it leaves, in every case: a starburst on the caster, its own muzzle reel,
            // and a snap of bolts thrown outward. This used to be a shockwave and a pop.
            Leaving(from, kind, fire);

            switch (kind)
            {
                // ---------------------------------------------------------- the blightcaller
                // **Chain lightning**, which is the only one of the four whose bolt visits the
                // wards it is not aimed at. That is exactly what a douse is — something spreading
                // through the line and settling on one of them — and it is the reading a player
                // needs, because the ward it puts out is not the one nearest the caster.
                case SiegeKind.Blightcaller:
                {
                    var path = new List<Vector2> { from };

                    // Hops through two other posts on the way. In post order rather than by
                    // distance, so the walk is the same shape every time and can be learned.
                    if (_posts != null)
                    {
                        int taken = 0;

                        for (int i = 0; i < _posts.Length && taken < 2; i++)
                        {
                            if (i == ward) continue;
                            path.Add(new Vector2(PostX(i), _lineY + Cell * .5f));
                            taken++;
                        }
                    }

                    path.Add(to);

                    Chain(path, fire, Cell * .075f, flight * .74f, .26f);
                    Hurl(from, to, kind, flight, .55f, 1f, 0f);
                    break;
                }

                // ---------------------------------------------------------- the warlord
                // **Four strikes and a triple volley.** A smite takes health, so what it looks
                // like is a bombardment: three orbs on spread arcs and four bolts out of the sky
                // onto the ward, the last of each landing on the beat the damage does.
                case SiegeKind.Boss:
                {
                    for (int i = 0; i < 3; i++)
                        Hurl(from, to, kind, flight, i == 1 ? 0f : (i == 0 ? -.7f : .7f),
                             i == 1 ? 1f : .62f, i * .045f);

                    for (int i = 0; i < 4; i++)
                        Strike(to + new Vector2(Random.Range(-Cell * .5f, Cell * .5f), 0f),
                               fire, Cell * .07f, flight * (.18f + i * .21f), .26f);
                    break;
                }

                // ---------------------------------------------------------- the warbringer
                // **A storm over the whole hill**, because a roar is aimed at nothing and the one
                // thing it has to say is "everything out there is about to move". Bolts land
                // scattered across the hill rather than on the line, which is where the raiders
                // the roar is about are standing.
                case SiegeKind.Warbringer:
                {
                    Roar(from, kind);

                    // Six rather than nine, spread a little wider apart. Nine read as noise over
                    // the hill rather than as six things being struck, and cost half as much again.
                    for (int i = 0; i < 6; i++)
                    {
                        var spot = new Vector2(Random.Range(-Span.x * .44f, Span.x * .44f),
                                               Random.Range(_hillFoot, _hillTop));

                        Strike(spot, fire, Cell * .07f, .04f + i * .075f, .28f);
                    }

                    // Two bolts thrown flat across the hill under the rings, which is the same
                    // pressure the flat reel draws said in the storm's own vocabulary.
                    for (int i = 0; i < 2; i++)
                    {
                        float y = Mathf.Lerp(_hillFoot, _hillTop, .3f + i * .35f);

                        Arc(new Vector2(-Span.x * .5f, y), new Vector2(Span.x * .5f, y),
                            fire, Cell * .05f, .3f, 3, .5f, .1f + i * .12f);
                    }
                    break;
                }

                // ---------------------------------------------------------- the gravemaw
                // **A ring that closes, which is the entire difference between this and a roar.**
                // A roar pushes outward off the thing casting it; a devour pulls the hill into it.
                // Nothing crosses the board either way, so the only thing a player can read is
                // *direction* — and for two chapters this drew the overlord's double launch, at a
                // boss that throws nothing, so the two orbs were built with no art behind them
                // (`SpellArt` answers null for a grounded boss), fell back to `Art.Glow`, and
                // were flown from the boss to the boss. Two grey blobs pulsing in place.
                case SiegeKind.Gravemaw:
                {
                    Maw(from, kind);
                    break;
                }

                // ---------------------------------------------------------- the bonecaller
                // **Two places at once, because that is what a raise is**: a light at the caster
                // and a light at the top of the hill where the bodies come up, reaching for each
                // other across the board. The crest's half is the louder and it is drawn on the
                // arrival rather than here (<see cref="Rise"/>), because that is the beat the
                // raiders are actually hatched on.
                case SiegeKind.Bonecaller:
                {
                    Crypt(from, kind);
                    break;
                }

                // ---------------------------------------------------------- the shackler
                // **One shot, straight, and a chain paying out behind it.** Everything else on
                // this board that is thrown at a ward is a *volley* — three orbs, a pair of
                // rockets, a chain of lightning — because everything else is a bombardment. A
                // bind is one arrow finding one turret and holding it, so drawing it as a spread
                // would be the picture saying the opposite of the rule. The straightness *is* the
                // reading, which is why it takes no bow at all.
                case SiegeKind.Shackler:
                {
                    Hurl(from, to, kind, flight, 0f, 1f, 0f);

                    // The chain: a taut line snapping tighter behind the arrow three times over
                    // the flight, in iron rather than in fire. `Arc` with no forks is a straight
                    // line, which is the one time in this file that is what is wanted — a bolt
                    // of lightning jags and a chain does not.
                    for (int i = 0; i < 3; i++)
                        Arc(from, to, fire, Cell * .05f * (1f + i * .35f), .2f, 0, .02f,
                            flight * (.18f + i * .26f));

                    break;
                }

                // ---------------------------------------------------------- the ironclad
                // **One heavy thing, thrown high and arriving downward.** An axe is the only
                // thing in the mode that has to read as *falling*, so it leaves from above the
                // boss's head and bows hard: what the eye follows is a rise and a drop rather
                // than a crossing, and at the ward it is the one impact here that throws rubble.
                case SiegeKind.Ironclad:
                {
                    Hurl(from + new Vector2(0f, Cell * 2.1f), to, kind, flight, -1.1f, 1f, 0f);

                    // The aegis, said on the boss and never on the line. The spell takes no
                    // health from a ward and its rule is about what may hurt *it* — so the one
                    // honest place to draw it is round the thing it protects, and a ring closing
                    // on a boss is a sentence this board has already taught (`Brace`).
                    Guard(mob, from, fire);

                    for (int i = 0; i < 3; i++)
                        Strike(to + new Vector2(Random.Range(-Cell * .5f, Cell * .5f), 0f),
                               fire, Cell * .08f, flight * (.52f + i * .16f), .26f);
                    break;
                }

                // ---------------------------------------------------------- the overlord
                // **Double rockets, and they are the reason it is drawn last.** The finale's spell
                // takes a rank as well as health, so it is the one that has to look like more than
                // a bigger smite: two orbs launch sideways out of it, bow hard in opposite
                // directions and converge on the ward together, with a bolt riding down between
                // them.
                //
                // **`default` is the overlord's and that is a fault this switch has already
                // paid for** (invariant 44e). Four bosses added after it were drawn as an
                // overlord for two chapters because nobody had to write a case to get one. It
                // stays `default` rather than becoming `case Overlord:` for one reason only:
                // a ninth boss with no arm must draw *something*, and an overlord's launch is
                // the most generic thing here. What stops that being the quiet answer again is
                // `SiegeArtTests.EveryBossSpellIsItsOwnDrawing`, which fails on a boss wearing
                // another boss's reels — the drawing half of invariant 37z, held by a fixture
                // rather than by whoever next reads this file.
                default:
                {
                    Hurl(from, to, kind, flight, -1.35f, .85f, 0f);
                    Hurl(from, to, kind, flight, 1.35f, .85f, .05f);

                    for (int i = 0; i < 3; i++)
                        Strike(to + new Vector2(Random.Range(-Cell * .7f, Cell * .7f), 0f),
                               fire, Cell * .085f, flight * (.3f + i * .24f), .3f);

                    Arc(from, to, fire, Cell * .06f, .34f, 2, .3f, flight * .1f);
                    break;
                }
            }
        }

        /// <summary>
        /// The instant a spell leaves its caster.
        ///
        /// <b>Loud enough to be the second-biggest thing in the cast</b>, after the landing. It is
        /// the pack's own muzzle reel, a starburst, a ring and six bolts thrown outward — where it
        /// used to be a ring and a flash, which on a lit hill is very nearly nothing.
        /// </summary>
        void Leaving(Vector2 from, SiegeKind kind, Color fire)
        {
            float scale = BurstAt(kind);

            // **The three grounded bosses' muzzle reels are spoken for**, and this is the one
            // branch in the file that has to know it. Each of the three lays its own reel *flat
            // over the ground* — a roar as pressure crossing the hill (<see cref="Roar"/>), a
            // devour as the floor going (<see cref="Maw"/>), a raise as the ground opening at the
            // crest (<see cref="Rise"/>) — and flat is the whole of why any of them reads as
            // happening *on* the hill. Drawing the same reel upright here as well would be the
            // same picture twice, once wrong.
            //
            // **Asked of the rule rather than listed**, which is the fix `SiegeView.Cast` already
            // had to make on the line beside it: a list of the kinds that happen to be grounded
            // today is a clause that goes stale the next time one is added, and this one had
            // already gone stale once — it named the warbringer alone, so the two bosses added
            // after it drew a flat ground wash standing upright in the air.
            if (SiegeTuning.AimsAtAWard(kind))
            {
                var muzzle = SpellMuzzleArt(kind);
                if (muzzle != null && muzzle.Length > 0)
                    Ends(Lend(muzzle, Color.white, Cell * 4.6f * scale, from, 0f, 30f,
                              false, MuzzleAt), .38f);

                // A roar has a boom of its own a beat later (invariant 37q: two sounds a frame
                // apart are a flam, not emphasis), so only the thrown three snap.
                Audio.Sfx("poke", .5f, Pitch(kind) - .12f);
            }

            var star = UIKit.Img("Snap", _fx, Art.Flash(256, 14), Pal.A(Pal.Lift(fire, .55f), .95f),
                                 new Vector2(Cell * 5f * scale, Cell * 5f * scale));
            star.raycastTarget = false;
            star.rectTransform.anchoredPosition = from;
            star.rectTransform.localScale = Vector3.one * .3f;

            Tween.Scale(star.transform, 1.5f, .3f, Ease.OutCubic);
            Tween.Rotate(star.rectTransform, 45f, .3f);
            Tween.Fade(star, 0f, .3f).OnDone(() => { if (star) Destroy(star.gameObject); });

            Shockwave(from, Pal.Lift(fire, .5f), 4.2f * scale, .32f);
            Pop(from, fire, 3.2f * scale, .28f);

            // Bolts thrown outward off the hand, which is what says the thing left rather than
            // simply appeared somewhere else.
            for (int i = 0; i < 5; i++)
            {
                float turn = i / 5f * Mathf.PI * 2f + Random.Range(-.3f, .3f);
                var away = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn))
                           * Cell * Random.Range(1.2f, 2f) * scale;

                Arc(from, from + away, fire, Cell * .055f, .2f, 1, .4f);
            }

            Burst.Sparks(_fx, from, fire, 14, Cell * 2.6f * scale, Cell * .2f, .45f);
        }

        // ------------------------------------------------------------------ the landing
        /// <summary>
        /// What a spell leaves behind on the line, on top of the burst
        /// <see cref="Smite"/> already draws.
        ///
        /// <b>Each one is the boss's own verb said one more time</b> (invariant 37z): a douse
        /// crawls over the ward it put out, a smite hammers it again, a roar throws the line's own
        /// lightning between the posts, and a sunder fans out to every turret the finale is about
        /// to start taking apart. None of it touches a number.
        /// </summary>
        void Aftermath(SiegeKind kind, int ward, Vector2 at, Color fire)
        {
            // **Asked once, at the top, rather than carried as two arms that break on nothing.**
            // Everything below paints a *post*; a devour and a raise land on the hill and never
            // touch the line, so there is nothing here for them to be drawn on. `Smite` already
            // hands both off to `Feed` and `Rise` before this is reached — this is what stops
            // that being the only thing standing between them and the `default` arm, which is the
            // overlord's fan and is what they really drew for two chapters (invariant 44e).
            if (!SiegeTuning.ReachesTheLine(kind)) return;

            switch (kind)
            {
                case SiegeKind.Blightcaller:
                {
                    // The light crawling over a ward as it goes out: four short bolts down the
                    // post, a beat apart, getting weaker. It is the only aftermath drawn *down*
                    // rather than outward, because what a douse takes is in the tube below.
                    for (int i = 0; i < 4; i++)
                    {
                        float k = i / 3f;
                        var a = at + new Vector2(Random.Range(-Cell * .3f, Cell * .3f),
                                                 Cell * (.5f - k * 1.1f));
                        var b = a + new Vector2(Random.Range(-Cell * .4f, Cell * .4f), -Cell * .5f);

                        Arc(a, b, fire, Cell * .04f * (1f - k * .5f), .22f, 1, .5f, i * .07f);
                    }
                    break;
                }

                case SiegeKind.Boss:
                {
                    for (int i = 0; i < 3; i++)
                        Strike(at + new Vector2(Random.Range(-Cell * .6f, Cell * .6f), 0f),
                               fire, Cell * .06f, .06f + i * .08f, .24f);
                    break;
                }

                case SiegeKind.Warbringer:
                {
                    // A roar arrives on every ward at once, so what it leaves is a line of
                    // lightning running along the posts — one drawing for four records, which is
                    // why it is gated on the same latch the stampede is.
                    if (_posts == null || _posts.Length < 2) break;

                    var along = new List<Vector2>();
                    for (int i = 0; i < _posts.Length; i++)
                        along.Add(new Vector2(PostX(i), _lineY + Cell * .45f));

                    Chain(along, fire, Cell * .055f, .24f, .26f, 0f, 1);
                    break;
                }

                case SiegeKind.Shackler:
                {
                    // **Iron closing round the post, and it is the only aftermath here drawn as a
                    // shape rather than as light.** A bind takes nothing — no health, no fuel, no
                    // rank — so a burst of any weight would be the drawing overstating the rule
                    // (the same argument `Snuffed` and `Chained` already make). Four short links
                    // laid in a ring round the chassis say *held*, which is the whole verb, and
                    // the standing state `Charge` keeps on the post is what the player really
                    // reads afterwards.
                    for (int i = 0; i < 4; i++)
                    {
                        float turn = i / 4f * Mathf.PI * 2f + Mathf.PI * .25f;
                        float next = (i + 1) / 4f * Mathf.PI * 2f + Mathf.PI * .25f;

                        var a = at + new Vector2(Mathf.Cos(turn), Mathf.Sin(turn) * .5f) * Cell * .5f;
                        var b = at + new Vector2(Mathf.Cos(next), Mathf.Sin(next) * .5f) * Cell * .5f;

                        Arc(a, b, fire, Cell * .05f, .34f, 0, .02f, i * .05f);
                    }
                    break;
                }

                case SiegeKind.Ironclad:
                {
                    // **Dust along the ground, because what landed was mass.** Every other
                    // aftermath in this switch goes up or outward; a slam's goes *sideways at the
                    // foot of the post*, which is the one direction that says weight. Nothing
                    // reaches the other turrets — an aegis is a rule about what may hurt the
                    // ironclad and it does nothing to the line, so a fan to every ward (which is
                    // what this drew as an overlord) was the picture inventing a threat.
                    for (int i = 0; i < 2; i++)
                    {
                        float away = (i == 0 ? -1f : 1f) * Cell * 1.9f;

                        Arc(at + new Vector2(0f, -Cell * .35f),
                            at + new Vector2(away, -Cell * .3f),
                            fire, Cell * .06f, .3f, 1, .4f, .04f);
                    }

                    Shockwave(at + new Vector2(0f, -Cell * .35f), Pal.Lift(fire, .3f), 4.4f, .42f);
                    break;
                }

                default:
                {
                    // The finale fans to every other turret, which is the drawing saying what the
                    // rule is about to do: an overlord takes a rank off the best of them, and the
                    // player has four to worry about rather than one.
                    if (_posts == null) break;

                    for (int i = 0; i < _posts.Length; i++)
                    {
                        if (i == ward) continue;

                        var post = new Vector2(PostX(i), _lineY + Cell * .45f);
                        Arc(at, post, fire, Cell * .06f, .3f, 1, .28f, .05f + i * .035f);
                    }

                    for (int i = 0; i < 4; i++)
                        Strike(at + new Vector2(Random.Range(-Cell * .8f, Cell * .8f), 0f),
                               fire, Cell * .075f, .05f + i * .07f, .28f);
                    break;
                }
            }
        }
    }
}
