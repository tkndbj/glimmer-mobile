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
    /// <summary>The cast: minting a raider's widget, wearing the right reel, and taking it away.</summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ raiders
        Mob Widget(SiegeRaider raider)
        {
            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == raider.Id) return _mob[i];

            return Hatch(raider);
        }

        Mob Hatch(SiegeRaider raider)
        {
            var mob = new Mob { Id = raider.Id };

            mob.Node = UIKit.Node("Raider", _mobs);
            mob.Node.anchorMin = mob.Node.anchorMax = new Vector2(.5f, .5f);
            mob.Node.sizeDelta = new Vector2(Cell, Cell);
            mob.Node.anchoredPosition = new Vector2(LaneX(raider.Lane), MarchY(raider.March));

            float tall = Cell * TallOf(raider);
            mob.Height = tall;
            mob.Boss = raider.Boss;
            mob.Kind = raider.Kind;

            // **A shadow belongs under the body, and where that is depends on what the body is.**
            //
            // This sat at 0.46 of the drawn height below the node, which is right for a *biped*
            // seen from the side: the node is the middle of the picture, the feet are near the
            // bottom of it, and the shadow goes under the feet. The cast are top-down insects now,
            // whose picture *is* their footprint - so the same number puts the shadow the better
            // part of a body-length clear of the thing casting it, and what that reads as on a
            // device is a hill of raiders floating. Reported in exactly those words.
            //
            // The numbers are the pack's own, measured off the shadows it bakes under these
            // insects and which `make_siege_art.deshadow` takes off (it has to: they are wider
            // than the body and inflate every frame). A beetle's sits about a fifth of its own
            // height below its middle and is about as wide as it is; `ShadowDrop`, `ShadowWide`
            // and `ShadowTall` are that, against the *body* rather than against the frame.
            // **And the baked cast is upright, so it takes the numbers the insects replaced.**
            // The paragraph above is about a body whose picture *is* its footprint; a KayKit
            // humanoid rendered at this board's 22° is the other thing entirely — the feet are at
            // the bottom of the picture and the shadow goes under them, which is precisely the
            // case the 0.46 figure was right for. Left on the insects' numbers it sat inside the
            // hips, which on a hill reads as a man hovering an inch above the grass. `Standing`
            // is that, and it is chosen by which cast is drawn rather than by kind, because it is
            // a fact about how the art was made.
            float wide = Frame(mob.Playing, tall);
            float body = tall * BodyFill(raider.Boss);
            bool upright = CastSet == SiegeMode.Baked && !raider.Boss;

            float drop = upright ? StandingDrop : ShadowDrop;
            float across = upright ? StandingWide : ShadowWide;
            float deep = upright ? StandingTall : ShadowTall;

            mob.Shadow = UIKit.Img("Shadow", mob.Node, Art.Glow(64, ShadowFalloff),
                                   new Color(0f, 0f, 0f, upright ? StandingInk : ShadowInk),
                                   new Vector2(wide * across, body * deep));
            mob.Shadow.raycastTarget = false;
            mob.Shadow.rectTransform.anchoredPosition =
                new Vector2(0f, BodyLift * tall - body * drop);

            // **A raider is drawn in its own paint, and the wash and the coat that used to
            // say its colour are gone.** They were a wash behind the body and a 62% multiply over
            // it, on the argument that the packs draw monsters in colours that have nothing to do
            // with this board's four — which was true and was fixed at the wrong end. `Image.color`
            // is a multiply, so a coat can only ever *darken*: what it produced was four
            // silhouettes of the same value with a hint of hue, and the drawing every one of these
            // packs is actually good at — highlights, shading, a face — was thrown away to say one
            // bit of information. Reported from play as exactly that, and it is the same finding
            // the ward line already made one folder over.
            //
            // The colour is now said three ways that cost the picture nothing: the raider is
            // **hue-rotated in the bake**, so it is genuinely that colour with all its shading
            // intact; it has a **body of its own per colour**, so silhouette carries it for anybody
            // who cannot separate two hues; and it still wears the **gem over its head**, which is
            // the one readout that survives two raiders overlapping.

            // **The warlord gathers its spell in a light of its own, under everything else.**
            // Built here and left dark rather than made when a cast starts: a widget that appears
            // at the moment something happens has to be faded in before it can be seen growing,
            // and what this is for is the growing.
            if (raider.Boss)
            {
                mob.Charge = UIKit.Img("Charge", mob.Node, Art.Glow(128, 2.0f),
                                       Pal.A(Casting(raider.Kind), 0f),
                                       new Vector2(tall * 1.5f, tall * 1.5f));
                mob.Charge.raycastTarget = false;
            }

            mob.Idle = Skin(raider);
            mob.Swinging = Swing(raider);

            // Written out per kind rather than built from a name, for `GemArt`'s reason: a reel
            // whose key is assembled is a reel `Tools/verify/artnames.py` cannot hold to disk.
            mob.Casting = CastReel(raider.Kind);
            mob.Walking = WalkReel(raider.Kind);

            // A warlord comes on *walking* and stands still once it is in place; everything else
            // is walking for its whole life, so its one reel is both. **A boss that has a walk of
            // its own opens in it**, because the first thing it ever does is the walk on — see
            // `Mob.Walking` for the three seconds this was drawing standing still.
            mob.Playing = mob.Walking ?? mob.Idle;

            mob.Body = Book(mob.Playing, "Body", mob.Node, new Vector2(tall, tall),
                            raider.Boss ? BossFps : raider.Brute ? 10f : 13f);

            if (mob.Body != null)
            {
                mob.Body.color = Color.white;

                // **Sized by its own height, with the width following the picture** — never fitted
                // into a square. Every cast reel is cut to a fixed height and whatever width the
                // animation's box came out as (`make_siege_art.cast_frames`), so a square box with
                // `preserveAspect` fits the *wider* ones by width and draws them short: measured
                // on the shipped art, `mon2` is 300 x 180 and was being drawn at 60% of the height
                // `mon1` gets, which is why one of the three creepers has always looked squat. The
                // warlord is the same fault at three times the size and would have been obvious,
                // which is how this was found.
                mob.Body.rectTransform.sizeDelta = new Vector2(wide, tall);

                // This pack draws its insects head-down already, which is the way this hill runs,
                // so nothing here is turned.
                mob.Body.rectTransform.anchoredPosition = new Vector2(0f, BodyLift * tall);

                // **The bob is a gait, so it belongs to a body that has none of its own.**
                //
                // Every other cast in this mode is a pack's single cycle, and the sine is what
                // stops a row of them reading as stickers sliding down a hill. A boss that walks
                // on in a real walk cycle and then *stops* is the one body here for which both
                // halves of that are wrong: while it walks the sine is a second gait fighting the
                // baked one, and while it holds the middle of the hill it is a three-cell figure
                // standing perfectly still and bouncing — which is what "when he is standing
                // still, it moves up and down" was, and it was the only vertical motion in the
                // picture. Measured on the shipped reels: the stand's frames differ from each
                // other by 2.2 mean pixel levels against 30-37 for every other body reel, so the
                // art was holding still and the tween was doing all of it.
                //
                // Keyed on having a walk reel rather than on being a boss, because that is the
                // property the argument actually rests on: the four 2D bosses have one reel that
                // is their walk and their stand together, they never hold still in it, and they
                // keep the bob they have always had.
                if (mob.Walking == null)
                    Tween.Bob(mob.Body.rectTransform, tall * .035f,
                              raider.Boss ? 1.6f : raider.Brute ? 1.1f : .72f,
                              raider.Id * .37f);
            }

            // A warlord's readouts hang off the top of the board rather than off its body.
            if (raider.Boss)
            {
                mob.CrownSlot = FreeCrown();
                mob.Crown = Crown(mob.CrownSlot);
            }

            var perch = mob.Crown != null ? mob.Crown : mob.Node;

            mob.Bar = UIKit.Node("Bar", perch);
            mob.Bar.anchorMin = mob.Bar.anchorMax = new Vector2(.5f, .5f);

            // A warlord's bar is thicker as well as wider, because it is the one health bar in
            // this mode a player watches for half a minute rather than glances at.
            mob.Bar.sizeDelta = raider.Boss
                ? new Vector2(Span.x * .60f, Cell * .26f)
                : new Vector2(tall * .72f, Cell * .13f);

            mob.Bar.anchoredPosition = raider.Boss
                ? new Vector2(Cell * .34f, 0f)
                : new Vector2(0f, tall * ReadoutAt(raider));

            var trough = UIKit.Img("Trough", mob.Bar, Art.Round(10), new Color(0f, 0f, 0f, .66f),
                                   mob.Bar.sizeDelta);
            trough.raycastTarget = false;
            trough.type = Image.Type.Sliced;

            mob.Fill = UIKit.Img("Fill", mob.Bar, Art.Round(10),
                                 raider.Boss ? Pal.Ember : raider.Brute ? Pal.Foxglove : Pal.Rose,
                                 new Vector2(mob.Bar.sizeDelta.x - 4f, mob.Bar.sizeDelta.y - 4f));
            mob.Fill.raycastTarget = false;
            mob.Fill.type = Image.Type.Sliced;
            mob.Fill.rectTransform.pivot = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

            // The gem over its head is the third thing that says its colour, and on the warlord
            // it is the one that has to carry: the body is so large that a 62% coat reads as
            // "purple alien with a red wash" rather than as red.
            float pip = Cell * (raider.Boss ? .62f : .34f);

            mob.Pip = UIKit.Img("Pip", perch, GemArt(raider.Colour), Color.white,
                                new Vector2(pip, pip));
            mob.Pip.raycastTarget = false;
            mob.Pip.preserveAspect = true;

            // At the head of the bar for a warlord, over the shoulder for everything else.
            mob.Pip.rectTransform.anchoredPosition = raider.Boss
                ? new Vector2(Cell * .34f - Span.x * .30f - pip * .72f, 0f)
                : new Vector2(-tall * .46f, tall * ReadoutAt(raider));

            mob.Node.localScale = Vector3.one * .5f;
            Tween.Scale(mob.Node, 1f, raider.Boss ? .6f : .3f, Ease.OutBack);

            var group = UIKit.Group(mob.Node);
            group.alpha = 0f;
            Tween.Fade(group, 1f, .26f);

            _mob.Add(mob);
            return mob;
        }

        /// <summary>The reel a boss throws in, and null for everything that never throws.</summary>
        static Sprite[] CastReel(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Reel("over_cast");
                case SiegeKind.Warbringer: return Reel("bringer_cast");
                case SiegeKind.Boss: return Reel("boss_cast");
                case SiegeKind.Blightcaller: return Reel("blight_cast");
                case SiegeKind.Gravemaw: return Reel("maw_cast");

                // **The one bought gesture in this mode.** The other four rear up on a sine the
                // bake synthesises, because their pack drew them one animation each; this one
                // raises its staff and lowers it again, which is why `make_siege_art.boss_reels`
                // grew a `whole` flag rather than cutting it to a rear-up.
                case SiegeKind.Bonecaller: return Reel("caller_cast");
                case SiegeKind.Shackler: return Reel("snare_cast");
                case SiegeKind.Ironclad: return Reel("clad_cast");

                default: return null;
            }
        }

        /// <summary>
        /// The reel a boss walks on in, and <b>null</b> for a boss whose cast drew only one.
        ///
        /// <para>
        /// <b>Three cases now, and the split is exactly the 2D/3D one.</b> The five bosses cut
        /// from flat packs have a single reel that is their walk and their stand at once — they
        /// never stop cycling, so nothing about them wants a second — where a boss rendered out of
        /// rigged 3D (invariant 37bx) genuinely stands still when it arrives and therefore needs
        /// both. Answering null for the rest is what keeps this a fact about the art rather than a
        /// rule everything has to satisfy.
        /// </para>
        /// <para>
        /// Written out per kind rather than assembled from <see cref="CastReel"/>'s key plus a
        /// suffix, for the reason every reel name in this file is: a name built at its call site
        /// is a name <c>Tools/verify/artnames.py</c> cannot hold to disk, and an <c>Image</c> with
        /// no sprite is a white rectangle three cells tall over the hill (invariant 7b).
        /// </para>
        /// </summary>
        static Sprite[] WalkReel(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Bonecaller: return Reel("caller_walk");
                case SiegeKind.Shackler: return Reel("snare_walk");
                case SiegeKind.Ironclad: return Reel("clad_walk");
                default: return null;
            }
        }

        /// <summary>How fast a warlord's own frames run. Slow, because it is a heavy thing.</summary>
        ///
        /// <para>
        /// <b>And it is the walk's cadence too, which is a decision rather than an oversight.</b>
        /// The obvious thing is to derive the step rate from the ground speed so the feet cannot
        /// slip — and the arithmetic says not to: this hill is about seven cells deep and a boss
        /// crosses the whole of it in <c>SiegeTuning.BossMarch</c> seconds, which is a shade under
        /// one cell a second, while the body walking it is drawn <em>three and a half cells
        /// tall</em>. A human stride carries about nine tenths of its own height, so a foot-locked
        /// cadence here is one cycle every three and a third seconds — <b>3.6 frames a second</b>,
        /// which does not read as a heavy walk, it reads as slow motion. The board draws its cast
        /// far larger than their speed implies and every game of this shape does; the genre's own
        /// answer is a natural cadence and some slip, and the complaint this is fixing was that
        /// the legs were not moving <em>at all</em>.
        /// </para>
        const float BossFps = 11f;

        /// <summary>How far a body is lifted off its node, as a share of the drawn height.</summary>
        const float BodyLift = .04f;

        /// <summary>
        /// Where a raider's shadow sits and how big it is, against the <b>body</b> rather than
        /// against the frame.
        ///
        /// Measured off the shadows this pack bakes under its own insects and which the bake
        /// strips (<c>make_siege_art.deshadow</c>): a crawler's sits 0.17 to 0.24 of its own
        /// height below its middle and runs 1.02 to 1.15 of its width. One number for the whole
        /// cast rather than one per family, and the family it is taken from is the crawlers,
        /// because a raider is a thing coming down a hill - a fly drawn with a ground-hugging
        /// shadow reads as skimming, which is true, where a beetle drawn with a flier's reads as
        /// floating, which is what was reported.
        /// </summary>
        const float ShadowDrop = .21f, ShadowWide = .78f, ShadowTall = .37f;

        /// <summary>
        /// The same three, for a body that is standing on the ground rather than lying on it.
        ///
        /// <para>
        /// <b>An offset is a fact about what the body is</b> — which is this invariant's own rule
        /// (37as), and the baked cast is the case it was written *against*. A top-down insect's
        /// picture is its footprint, so its shadow sits a fifth of its height below its middle; a
        /// humanoid rendered at 22° has its feet at the bottom edge of the picture, so its shadow
        /// sits most of a half-height below the middle or it is drawn inside the knees. Measured
        /// off the baked reels: the feet occupy the bottom eighth of a trimmed frame, which puts
        /// the contact point at 0.44 of the body below its middle.
        /// </para>
        /// <para>
        /// <b>Wider than it is deep, because it is an ellipse seen at a rake.</b> A shadow on the
        /// ground under a near-side-on camera is foreshortened in depth and not in width — the
        /// insects' near-circle is what a plan view gives you and is wrong here. And it is
        /// <b>darker</b>: a standing body's contact shadow is the only thing saying it is touching
        /// the hill at all, where a prone insect has its whole silhouette doing that job.
        /// </para>
        /// <para>
        /// A boss keeps the insect numbers, because the four bosses are still insects and are
        /// drawn from the other cast whatever <see cref="CastSet"/> says.
        /// </para>
        /// </summary>
        const float StandingDrop = .44f, StandingWide = .86f, StandingTall = .26f;

        /// <summary>How dark a standing body's contact shadow is. See <see cref="StandingDrop"/>.</summary>
        const float StandingInk = .62f;

        /// <summary>
        /// How dark the shadow is at its middle, and how fast it falls off to nothing.
        ///
        /// <para>
        /// <b>The power is what decides whether this reads as a shadow or as a smudge, and it is
        /// the number that has been wrong twice.</b> <c>Art.Glow</c> is <c>(1 - distance)</c>
        /// raised to a power, so a <em>high</em> power fades from its own middle outward — at the
        /// cubic it was first asked for, an eighth of its peak half way out, which at this size is
        /// nothing anybody can see. Dropping it to 1.4 made it visible and came straight back from
        /// play as <b>cloudy</b>, which is the same reading one step on: a profile that ramps the
        /// whole way is a haze, where a shadow is flat in the middle and soft only at its rim. At
        /// a quarter it holds near its peak most of the way out and falls off at the edge, which
        /// is what the pack's own baked shadows are (a near-flat ellipse at 34% alpha, about as
        /// wide as the insect).
        /// </para>
        /// <para>
        /// <b>The width moves with it or the footprint does not stay put</b>: a flatter profile
        /// reaches visibly almost to the edge of its rect where a steep one dies two thirds of the
        /// way, so going from 1.4 to 0.25 means shrinking the rect by about a third to put the
        /// same amount of dark on the ground. Picked by drawing the candidates under a real insect
        /// on the palest floor this mode has and on the brightest — the step darker than this
        /// reads as a hole in the grass.
        /// </para>
        /// </summary>
        const float ShadowInk = .55f, ShadowFalloff = .25f;

        /// <summary>
        /// How much of its frame a body actually fills, measured off the shipped reels.
        ///
        /// <b>A raider's frame is trimmed to its own animation and a boss's is not</b>: a boss
        /// shares one canvas with its cast reel so that it cannot change size when it throws
        /// (<c>make_siege_art.one_canvas</c>), and that reel rises, so the frame is taller than the
        /// body standing in it. Everything the view places is placed against the <em>frame</em>, so
        /// without this a boss's shadow sits a third further out than its raiders' do — which is
        /// the same fault this whole rule exists to fix, one kind further in.
        /// </summary>
        static float BodyFill(bool boss) => boss ? .72f : .94f;

        /// <summary>
        /// Puts one of a raider's reels on its body, and remembers which.
        ///
        /// <para>
        /// <b>Remembering is the whole job.</b> <c>Flipbook.Attach</c> re-starts a reel from frame
        /// nought, so a caller that attached the wanted reel every frame would draw frame nought
        /// for ever — a walk cycle that never takes a step, which is the fault this method exists
        /// to fix, arrived at from the other side. <see cref="Mob.Playing"/> is what lets
        /// <see cref="Follow"/> ask the question sixty times a second and answer it once.
        /// </para>
        /// </summary>
        void Wear(Mob mob, Sprite[] reel, bool loop = true)
        {
            if (mob == null || mob.Body == null || reel == null || reel.Length == 0) return;
            if (mob.Playing == reel && loop) return;

            mob.Playing = reel;
            Flipbook.Attach(mob.Body, reel, BossFps, loop);

            // **A reel may be cut on a bigger canvas than the one this body walks in, and the body
            // must not change size when it is.** A boss's reels share one canvas, so this has
            // always been a no-op for them and was worth setting rather than assuming. A skeleton's
            // swing does not: the attack throws the weapon so far outside the walk's box that a
            // shared canvas fitted to the walk's height would draw every raider in the chapter at
            // 59-73% of its size for the whole run, for the sake of six frames at the line
            // (`make_siege_art.walk_and_swing`). So the bake keeps the *body* at one scale and
            // lets the swing's frame be bigger, and this reads the ratio straight off the two
            // sprites - which needs no number written down anywhere and cannot drift from the art.
            float tall = mob.Height * Grown(mob.Idle, reel);
            mob.Body.rectTransform.sizeDelta = new Vector2(Frame(reel, tall), tall);
        }

        /// <summary>
        /// Whether a raider's body is showing something it must not be interrupted in.
        ///
        /// Only a warlord has one, and it is the cast: <see cref="Follow"/> runs every frame and
        /// would otherwise put the idle back on the frame after a spell started.
        ///
        /// <b>Named away from <c>Busy</c> deliberately</b> — that is <c>ProtoView</c>'s latch for a
        /// cascade still falling, and a second member of the same name in one hierarchy is what
        /// <c>ModeScreen.Prepare</c> was renamed to avoid.
        /// </summary>
        static bool Throwing(Mob mob) => mob.Casting != null && mob.Playing == mob.Casting;

        /// <summary>
        /// How much bigger this reel's frame is than the one the body walks in.
        ///
        /// <b>Read off the sprites rather than written down</b>, which is the whole point: the
        /// bake decides how much room a swing needs and this cannot come to disagree with it. One
        /// for every reel cut on the body's own canvas, which is every reel of every other cast.
        /// </summary>
        static float Grown(Sprite[] home, Sprite[] reel)
        {
            if (home == null || home.Length == 0 || home[0] == null) return 1f;
            if (reel == null || reel.Length == 0 || reel[0] == null) return 1f;

            float walk = home[0].rect.height;
            return walk > 0f ? reel[0].rect.height / walk : 1f;
        }

        /// <summary>
        /// Takes down the widgets of raiders that are dead.
        ///
        /// <para>
        /// <b>Dead, not <em>forgotten</em> — and the difference was a bug the player met.</b> It
        /// used to reap a widget whose raider the model had swept out of its list, which is the
        /// same question only while every kill happens inside <c>SiegeBoard.Advance</c>: the
        /// sweep is the last thing that method does, so a bolt's kill is gone by the time this
        /// runs. A <em>utility</em> kills outside <c>Advance</c> — no sweep has run, the raider
        /// is still in the list with <c>Alive</c> false, so this skipped it. Ordinarily the next
        /// frame put it right; on the killing blow it never came, because <see cref="Judge"/>
        /// ends the run in the same breath and <c>Update</c> stops with it. What shipped was a
        /// firepot or a storm finishing a hill and leaving every raider it had just killed
        /// standing there under the victory panel.
        /// </para>
        /// <para>
        /// Asking <c>Alive</c> is the question that was always meant, and it is true one step
        /// earlier than "swept" on every path, so both agree without either having to know when
        /// the model tidies up.
        /// </para>
        /// </summary>
        void Reap()
        {
            for (int i = _mob.Count - 1; i >= 0; i--)
            {
                var mob = _mob[i];
                if (mob.Falling) continue;

                var raider = _board.Find(mob.Id);
                if (raider != null && raider.Alive) continue;

                // Unless a storm or a stormglass has claimed it and not yet struck it. See
                // `_striking` and `_volleying`.
                if (_striking.Contains(mob.Id)) continue;
                if (_volleying.Contains(mob.Id)) continue;

                mob.Falling = true;
                Die(mob);
                _mob.RemoveAt(i);
            }
        }

        void Die(Mob mob)
        {
            var at = mob.Node.anchoredPosition;

            if (mob.Boss) { Fall(mob, at); return; }

            // The run may not be told until this has been watched. See `_felling`.
            Felling(DyingFor);

            Boom(at, Blast("boom_fire"), mob.Height * 2f);
            Burst.Sparks(_fx, at, Pal.Ember, 14, mob.Height * 1.6f, mob.Height * .2f);
            Audio.SfxVaried("burst", .38f);

            var node = mob.Node;
            var group = UIKit.Group(node);

            Tween.Run(.32f, Ease.OutQuad, t =>
            {
                if (!node) return;
                node.localScale = new Vector3(1f + t * .35f, 1f - t * .55f, 1f);
                if (group) group.alpha = 1f - t;
            }, node).OnDone(() => { if (node) Destroy(node.gameObject); });
        }

        /// <summary>
        /// The warlord coming apart.
        ///
        /// <b>The largest thing that happens in this mode, and it is drawn as a run rather than as
        /// one bang.</b> A boss that vanished in the same puff a creeper does would be the whole
        /// fight paying out in a tenth of a second — which is invariant 20m's rule about a payoff
        /// asked of the one moment the player has been working toward for half a minute. Five
        /// explosions walking outward, then the shape going down.
        /// </summary>
        /// <summary>
        /// Whether this frame's report has already drawn a roar taking hold.
        ///
        /// A warbringer's spell lands on every standing ward and so arrives as one record per
        /// ward; the hill charging is one event and is drawn once. Cleared per frame rather than
        /// latched for the run, because a warbringer roars every few seconds for the whole fight.
        /// </summary>
        bool _roared;

        /// <summary>
        /// Seconds left of <em>something</em> coming apart, or nought.
        ///
        /// <para>
        /// <b>The one thing in this mode allowed to hold the ending up</b> — see
        /// <see cref="Judge"/>. It was a boss's alone, on the reasoning that everything else a
        /// run ends on is already on the screen when the verdict lands. That is true of a ward
        /// falling and false of a raider: a killing blow decides the run in the frame it lands,
        /// and the death it caused is a burst and a third of a second of tween that has not
        /// started yet. Reported from play about a firepot and a storm, which are the two ways a
        /// player can land that blow themselves — so it is the two moments in this mode most
        /// worth watching, and both of them were being covered by a panel.
        /// </para>
        /// <para>
        /// <b>It is decremented by <c>Update</c> rather than by <see cref="Judge"/>, and that is
        /// load-bearing now that ordinary deaths arm it.</b> A latch only ticked down inside the
        /// won branch would be armed by the first creeper of the run and still standing at full
        /// when the last one died, holding the panel for a boss's worth of seconds over a hill
        /// that came apart a minute ago.
        /// </para>
        /// </summary>
        float _felling;

        /// <summary>How long <see cref="Fall"/> takes end to end. The ending waits this out.</summary>
        const float FellingFor = 1.45f;

        /// <summary>
        /// How long an ordinary raider's death is watched before the run may be told.
        ///
        /// Comfortably past <see cref="Die"/>'s own tween rather than exactly it: what the player
        /// is owed is the burst reading as a burst, not the last frame of an alpha ramp.
        /// </summary>
        const float DyingFor = .50f;

        /// <summary>
        /// Holds the ending for at least this long, and never shortens a hold already running.
        ///
        /// <b>The larger of the two, always</b> — a boss and three creepers going off together is
        /// one event that lasts as long as its longest part, and taking the newer figure would
        /// let a creeper dying a frame after the warlord cut the warlord's death short.
        /// </summary>
        void Felling(float seconds)
        {
            if (seconds > _felling) _felling = seconds;
        }

        /// <summary>
        /// Counts the hold down. Called once a frame by the clock, whether or not the run is won.
        ///
        /// Named away from <c>Fade</c> deliberately — this file fades half a dozen widgets and a
        /// method of that name here would read as one more of them.
        /// </summary>
        void Watching(float dt)
        {
            if (_felling > 0f) _felling -= dt;
        }

        void Fall(Mob mob, Vector2 at)
        {
            Felling(FellingFor);

            var node = mob.Node;
            var group = UIKit.Group(node);
            var crown = mob.Crown;

            // The bar goes with it, and by hand: it hangs off the effects layer rather than off
            // the body, so destroying the body would leave an empty warlord's health bar pinned
            // across the top of a board with no warlord on it.
            if (crown != null)
            {
                var over = UIKit.Group(crown);
                Tween.Fade(over, 0f, .5f).Delay(.5f)
                     .OnDone(() => { if (crown) Destroy(crown.gameObject); });
            }

            Audio.Sfx("boom", .9f, .72f);
            Flow.Flash(new Color(1f, .82f, .55f), .55f, .5f);
            ShakeBoard(30f);

            for (int i = 0; i < 5; i++)
            {
                float wait = i * .11f;
                var spot = at + new Vector2(Random.Range(-1f, 1f) * mob.Height * .34f,
                                            Random.Range(-1f, 1f) * mob.Height * .30f);

                Tween.After(wait, () =>
                {
                    Boom(spot, Blast("boom_fire"), mob.Height * 1.5f);
                    Burst.Sparks(_fx, spot, Pal.Ember, 12, mob.Height * 1.2f, mob.Height * .16f);
                    Audio.SfxVaried("burst", .42f);
                });
            }

            Tween.Shake(node, Cell * .3f, .55f);

            Tween.Run(.95f, Ease.InQuad, t =>
            {
                if (!node) return;
                node.localScale = new Vector3(1f + t * .18f, 1f - t * .68f, 1f);
                if (group) group.alpha = 1f - t * t;
            }, node).Delay(.35f).OnDone(() =>
            {
                if (!node) return;

                Boom(at, Blast("boom_smoke"), mob.Height * 2.6f);
                Shockwave(at, Pal.Gold, 7f, .55f);
                Destroy(node.gameObject);
            });
        }
    }
}
