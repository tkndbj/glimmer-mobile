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

            mob.Shadow = UIKit.Img("Shadow", mob.Node, Art.Glow(64, 3f),
                                   new Color(0f, 0f, 0f, .42f),
                                   new Vector2(tall * .62f, tall * .22f));
            mob.Shadow.raycastTarget = false;
            mob.Shadow.rectTransform.anchoredPosition = new Vector2(0f, -tall * .46f);

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

            // Written out per kind rather than built from a name, for `GemArt`'s reason: a reel
            // whose key is assembled is a reel `Tools/verify/artnames.py` cannot hold to disk.
            mob.Walking = WalkReel(raider.Kind);
            mob.Casting = CastReel(raider.Kind);

            // A warlord comes on *walking* and stands still once it is in place; everything else
            // is walking for its whole life, so its one reel is both.
            mob.Playing = raider.Boss && mob.Walking != null && !raider.InPlace
                        ? mob.Walking : mob.Idle;

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
                float wide = Frame(mob.Playing, tall);

                mob.Body.rectTransform.sizeDelta = new Vector2(wide, tall);

                // The packs draw them facing right; this hill runs top to bottom, so they are
                // turned to face down the way a walk cycle reads best - across, and coming on.
                mob.Body.rectTransform.anchoredPosition = new Vector2(0f, tall * .04f);
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

        /// <summary>
        /// The reel a boss crosses ground in, and null for everything that has only one reel.
        ///
        /// Written out per kind rather than built from a name, for <see cref="GemArt"/>'s reason:
        /// a reel whose key is assembled is a reel <c>Tools/verify/artnames.py</c> cannot hold to
        /// disk, and twelve of the mode's art names are these.
        /// </summary>
        static Sprite[] WalkReel(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Reel("over_walk");
                case SiegeKind.Warbringer: return Reel("bringer_walk");
                case SiegeKind.Boss: return Reel("boss_walk");
                case SiegeKind.Blightcaller: return Reel("blight_walk");
                default: return null;
            }
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
                default: return null;
            }
        }

        /// <summary>How fast a warlord's own frames run. Slow, because it is a heavy thing.</summary>
        const float BossFps = 11f;

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

            // The frame's shape is a fact about the picture (see `Frame`), and a warlord's three
            // reels are cut onto one canvas so that this never actually changes - which is exactly
            // why it is worth setting rather than assuming.
            mob.Body.rectTransform.sizeDelta = new Vector2(Frame(reel, mob.Height), mob.Height);
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

                // Unless a storm has claimed it and not yet struck it. See `_striking`.
                if (_striking.Contains(mob.Id)) continue;

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
