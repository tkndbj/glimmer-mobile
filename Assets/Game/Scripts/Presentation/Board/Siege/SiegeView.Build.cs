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
    /// <summary>
    /// Building the board once: the ground, the ward line, the sockets, the field, the targeting
    /// grid and the countdown that opens a run.
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ building
        protected override void Compose()
        {
            var rules = (SiegeRules)Rules;
            _layout = rules.Layout;
            _board = (SiegeBoard)Run.Board;

            _gems.Clear();
            _mob.Clear();

            _tally.Clear();
            _chain = null;
            _chainAura = null;

            // The pool's widgets hang off `_fx`, which this rebuild replaces — so a spare kept
            // across it is a destroyed node handed out as a live one, and every bolt after the
            // first rebuild would be invisible.
            _spare.Clear();

            _wave = 0;

            // The targeting layer hangs off the layers this rebuild is about to replace, so a
            // board dealt again while something was armed would leave a destroyed node behind a
            // live `Arming` — and the bar would still be showing a ring. Cleared through the
            // field rather than the property, because the layer it would tear down is already
            // gone; the screen re-arms nothing, which is what a fresh board should be.
            _arming = null;
            _aim = null;

            // Hangs off `_wall`, which this rebuild is about to replace - a kept one would be a
            // destroyed node handed to a lesson as a live one.
            _wardAnchor = null;
            _meters = null;

            // **The bands are derived from the cell, not the other way round.** The field is
            // laid out to fill the width (see `Fit`), so how much height its rows need is a fact
            // rather than a share — and the hill and the line then take what is left in the
            // proportion they were authored in. Written as a share of the authored pair rather
            // than as two more constants, so moving `HillBand` still moves only one thing.
            float h = Span.y;
            float gems = Mathf.Clamp(Cell * Height / h, .28f, MaxGemBand);
            float rest = 1f - gems;
            float hill = rest * (HillBand / (HillBand + LineBand));
            float line = rest - hill;

            _hillTop = h * .5f - Cell * .35f;
            _hillFoot = h * (.5f - hill);
            // The wards stand *high* on the line, so their heads break into the grass rather
            // than tucking under the field's plate. A render is why: at the middle of the band
            // they were half-hidden behind the plate and read as small.
            _lineY = _hillFoot - h * line * .30f;
            _gemCentre = (h * (.5f - hill - line) - h * .5f) * .5f;

            _hill = Layer("Hill");
            _mobs = Layer("Raiders");
            _wall = Layer("Line");
            _field = Layer("Field");

            // **The fuel tubes have a layer of their own, and it is above the field.** They sit in
            // the strip between the plinths and the gems, which is where a device said they belong
            // - and that strip is exactly where the field's plate begins, so a tube carried by its
            // own turret would be drawn behind it. That is the fault invariant 37g records, met
            // from the other end: the first fix moved the tube *up* onto the chassis to escape the
            // plate, and what it really needed was to stop being underneath it.
            _meters = Layer("Meters");
            _fx = Layer("Fx");

            Ground();
            Line();
            Sockets();
            Deal();
            Targets();
            Banner();

            StartCoroutine(Countdown());
        }

        RectTransform Layer(string name)
        {
            var rt = UIKit.Node(name, Field);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = Field.sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>The hill: ground the raiders walk over, and the breach they come out of.</summary>
        void Ground()
        {
            float h = _hillTop - _hillFoot + Cell * .35f;

            var ground = UIKit.Img("Ground", _hill, Hill(Rung), Color.white,
                                   new Vector2(Span.x, h + Cell * .5f));
            ground.raycastTarget = false;
            ground.rectTransform.anchoredPosition = new Vector2(0f, (_hillTop + _hillFoot) * .5f);

            // **Nothing over the top of it, and nothing standing at the head of it.** The first
            // cut put a black gradient across the far end (so the hill "got darker the further up
            // it went") and a broken gateway for the waves to come out of. Both were withdrawn by
            // the owner after playing it: the wash read as a hole rather than as distance, and the
            // gateway read as a hut somebody had left on the board. What says a wave has arrived
            // is the wave - it walks on, in front of a field that is plainly a field.
        }

        // **The hill has no lanes drawn on it, and it used to.** Five pale strips at 4.5% white
        // marked where the raiders walk; over grass they were invisible and over the mine floor
        // they read as two seams running the height of the board. They were never load-bearing -
        // a raider's lane is visible from the raider - so they are gone rather than re-tinted:
        // a marking that has to be nearly invisible to be tolerable is a marking nothing needed.

        /// <summary>The rampart and the wards standing on it.</summary>
        void Line()
        {
            float band = Span.y * LineBand;

            var wall = UIKit.Img("Rampart", _wall, Piece("rampart"), Color.white,
                                 new Vector2(Span.x, band * 1.02f));
            wall.raycastTarget = false;
            wall.type = Image.Type.Sliced;
            wall.rectTransform.anchoredPosition = new Vector2(0f, _hillFoot - band * .5f);

            _posts = new Post[_board.Wards.Count];

            for (int i = 0; i < _posts.Length; i++)
            {
                var ward = _board.Wards[i];
                var post = new Post();

                post.Node = UIKit.Node("Ward", _wall);
                post.Node.anchorMin = post.Node.anchorMax = new Vector2(.5f, .5f);
                post.Node.sizeDelta = new Vector2(Cell * 1.8f, Cell * 2.3f);
                post.Node.anchoredPosition = new Vector2(PostX(i), _lineY);

                post.Socket = UIKit.Img("Base", post.Node, Piece("socket"), Color.white,
                                        new Vector2(Cell * 1.7f, Cell * .8f));
                post.Socket.raycastTarget = false;
                post.Socket.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .88f);

                post.Glow = UIKit.Img("Glow", post.Node, Art.Glow(128, 2.1f),
                                      Pal.A(TintOf(ward.Colour), 0f),
                                      new Vector2(Cell * 3.1f, Cell * 3.1f));
                post.Glow.raycastTarget = false;

                // White: a ward's colour is in its sprite, not on top of it.
                post.Coat = Color.white;
                post.Colour = ward.Colour;
                post.Rank = ward.Rank;

                post.Body = UIKit.Img("Post", post.Node, WardArt(ward),
                                      post.Coat, new Vector2(Cell * 1.72f, Cell * 2.15f));
                post.Body.raycastTarget = false;
                post.Body.preserveAspect = true;
                post.Body.rectTransform.anchoredPosition = new Vector2(0f, Cell * .06f);

                post.Fire = FireArt(ward);

                // The fuel tube: the one readout in this mode that is on the board rather than in
                // the header, because it is the thing a player is deciding about on every match.
                //
                // **Hung off the meters layer rather than off the turret**, so it is drawn over the
                // field's plate rather than under it - see `Compose`. Its x is the turret's, so it
                // still reads as belonging to one, and it is the only part of a ward that is not
                // its child.
                post.Tube = UIKit.Node("Tube", _meters);
                post.Tube.anchorMin = post.Tube.anchorMax = new Vector2(.5f, .5f);
                post.Tube.sizeDelta = new Vector2(Cell * 1.06f, Cell * .23f);

                // **In the gap between the plinths and the field**, which is where it belongs and
                // where a device said so. It was over the pillar - a render had put it there,
                // because at the time it was the only place it did not fall behind the field's own
                // plate - and on the pillar it reads as part of the turret's chassis rather than as
                // a meter. The strip of rampart under the line is empty, it is exactly the height
                // of a bar, and it is directly above the gems whose colour fills it: the two halves
                // of every decision this mode asks, one above the other.
                //
                // Derived rather than typed, because the bands are derived (see `Compose`): the top
                // of the field's plate and the foot of a plinth are both known here, and a typed
                // offset would be right on one phone.
                post.Tube.anchoredPosition = new Vector2(PostX(i), TubeY);

                var trough = UIKit.Img("Trough", post.Tube, Art.Round(12),
                                       new Color(0f, 0f, 0f, .62f), post.Tube.sizeDelta);
                trough.raycastTarget = false;
                trough.type = Image.Type.Sliced;

                post.Juice = UIKit.Img("Juice", post.Tube, Art.Round(12), TintOf(ward.Colour),
                                       new Vector2(0f, post.Tube.sizeDelta.y - 4f),
                                       new Vector2(0f, .5f), new Vector2(4f, 0f));
                post.Juice.raycastTarget = false;
                post.Juice.type = Image.Type.Sliced;
                post.Juice.rectTransform.pivot = new Vector2(0f, .5f);
                post.Juice.rectTransform.anchorMin = new Vector2(0f, .5f);
                post.Juice.rectTransform.anchorMax = new Vector2(0f, .5f);
                post.Juice.rectTransform.anchoredPosition = new Vector2(2f, 0f);

                // **A bar rather than a row of pips.** A ward takes ten blows now (see
                // `SiegeTuning.WardHealth`), and ten dots over a turret is something a player
                // reads as texture rather than as a number.
                post.Bar = UIKit.Node("Health", post.Node);
                post.Bar.anchorMin = post.Bar.anchorMax = new Vector2(.5f, .5f);
                post.Bar.sizeDelta = new Vector2(Cell * 1.06f, Cell * .17f);
                post.Bar.anchoredPosition = new Vector2(0f, Cell * 1.26f);

                var kerb = UIKit.Img("Trough", post.Bar, Art.Round(10),
                                     new Color(0f, 0f, 0f, .66f), post.Bar.sizeDelta);
                kerb.raycastTarget = false;

                post.Fill = UIKit.Img("Fill", post.Bar, Art.Round(10), Pal.Cream,
                                      new Vector2(post.Bar.sizeDelta.x - 4f,
                                                  post.Bar.sizeDelta.y - 4f));
                post.Fill.raycastTarget = false;
                post.Fill.rectTransform.pivot = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

                Badge(post, ward);

                _posts[i] = post;
            }
        }

        /// <summary>
        /// The rank badge on a ward's shoulder: the kit's own shield, with a number on it.
        ///
        /// <para>
        /// <b>Always drawn, and it says one before anybody has spent a cog.</b> A badge that only
        /// appeared once a ward had been upgraded would be a reward for knowing about a mechanic
        /// nobody had met — this way the ladder is on the board from the first frame, and what a
        /// cog does is legible the moment it happens rather than the moment it is explained.
        /// </para>
        /// <para>
        /// <b>The number is drawn rather than baked</b>, which is five textures a colour saved and
        /// one place the tier is written down. It sits on the turret's upper-left shoulder, which
        /// is the one corner of a turret nothing else on this board uses: the health bar is above
        /// it, the fuel tube below it, and its own bolts leave from the middle.
        /// </para>
        /// </summary>
        void Badge(Post post, SiegeWard ward)
        {
            post.Crest = UIKit.Node("Crest", post.Node);
            post.Crest.anchorMin = post.Crest.anchorMax = new Vector2(.5f, .5f);
            post.Crest.sizeDelta = new Vector2(Cell * .62f, Cell * .62f);
            post.Crest.anchoredPosition = new Vector2(-Cell * .74f, Cell * .34f);

            // **The badge carries the rank twice: as a number, and as its own colour.**
            //
            // The five-tier turret ladder used to carry it in the *silhouette* (invariant 37w),
            // and twenty player-chosen models cannot - the silhouette belongs to the choice now.
            // A plinth under the turret was tried and thrown away: invariant 37y already records
            // that a turret's foot is behind the field's plate on every screen this mode is drawn
            // at, so what went there was invisible. The shoulder is the one corner of a ward
            // nothing else uses, and a colour is read at a glance where a number has to be read.
            post.Shield = UIKit.Img("Shield", post.Crest, Piece("crest"), RankTint(ward.Rank),
                                    post.Crest.sizeDelta);
            post.Shield.raycastTarget = false;
            post.Shield.preserveAspect = true;

            post.Tier = UIKit.Titled("Tier", post.Crest, ward.Level.ToString(),
                                     Mathf.RoundToInt(Cell * .34f), Pal.Cream,
                                     TextAnchor.MiddleCenter, post.Crest.sizeDelta,
                                     default, default, Cell * .035f, Cell * .02f);

            // The shield's own art hangs its point below its middle, so the number is lifted to sit
            // in the face of it rather than over the tip.
            post.Tier.rectTransform.anchoredPosition = new Vector2(0f, Cell * .06f);
        }

        /// <summary>The dark sockets a gem stands in, drawn once and never taken away.</summary>
        void Sockets()
        {
            var plate = UIKit.Img("Plate", _field, Piece("plate"), Color.white,
                                  new Vector2(Cell * Width + Cell * .34f,
                                              Cell * Height + Cell * .34f));
            plate.raycastTarget = false;
            plate.type = Image.Type.Sliced;
            plate.rectTransform.anchoredPosition = new Vector2(0f, _gemCentre);

            for (int i = 0; i < Width * Height; i++)
            {
                var slot = UIKit.Img("Slot", _field, Art.Round(16), Pal.Slot,
                                     new Vector2(Cell * .92f, Cell * .92f));
                slot.raycastTarget = false;
                slot.type = Image.Type.Sliced;
                slot.rectTransform.anchoredPosition = CentreOf(i);
            }
        }

        /// <summary>The gems. Pictures only - the finger is taken by <see cref="Targets"/>.</summary>
        void Deal()
        {
            for (int i = 0; i < Width * Height; i++)
            {
                var gem = Mint(Face(i));
                gem.Img.rectTransform.anchoredPosition = CentreOf(i);
                _gems.Add(gem);

                // A field may open with nothing on it, but a rebuild after a continue may not:
                // the weaver that locked those cells is still standing there.
                Lock(gem, _board.IsWebbed(i));
            }
        }

        /// <summary>
        /// One hit target per cell, standing still.
        ///
        /// <b>Per cell rather than on the gems</b>, which is <c>EmberView</c>'s idiom and right
        /// here for a second reason on top of its own: a gem on this board is falling most of the
        /// time, so a handler carried by the picture would have to be asked where its picture had
        /// got to. Hit-testing the cell and then asking the board what is standing there keeps the
        /// drawing and the rule from ever disagreeing about what was touched.
        /// </summary>
        void Targets()
        {
            for (int cell = 0; cell < Width * Height; cell++)
            {
                int at = cell;

                var img = UIKit.Img("hit", _field, Art.Pixel, new Color(0f, 0f, 0f, 0f),
                                    new Vector2(Cell, Cell));
                img.raycastTarget = true;
                img.rectTransform.anchoredPosition = CentreOf(at);

                var btn = img.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Poke(at), silent: true);

                var drag = img.gameObject.AddComponent<CellDrag>();
                drag.Threshold = Cell * .30f;
                drag.Dragged = dir => Drag(at, dir);
            }
        }

        /// <summary>
        /// Three, two, one, and they come.
        ///
        /// <para>
        /// <b>Once, at the start, and never per wave.</b> What it is for is the half-second a
        /// player needs to look at the hill before anything is on it - a siege that opens with
        /// something already walking has been going on before they arrived. It is not a *pause*:
        /// the clock runs underneath it, and the first wave is timed to step out as the last
        /// number leaves (<see cref="SiegeTuning.FirstWaveAfter"/>), so nothing is being held up
        /// and the count is telling the truth about when they arrive.
        /// </para>
        /// </summary>
        IEnumerator Countdown()
        {
            // **Four beats inside the quiet, and the quiet was lengthened to fit them.** At a
            // 2.2-second opening the numbers went past in half a second each and read as a
            // flicker; the count now paces itself and `FirstWaveAfter` is what it is so that
            // GO! and the first raider still land together. The count does not hold the game up
            // - the clock runs underneath it - so lengthening it lengthens the quiet, which is
            // the honest thing for it to do.
            float step = SiegeTuning.FirstWaveAfter / 4f;

            for (int i = 3; i >= 0; i--)
            {
                if (_fx == null) yield break;

                string say = i > 0 ? i.ToString() : Loc.Get("mode.siege.go");

                var label = UIKit.Label("Count", _fx, say,
                                        Mathf.RoundToInt(Cell * (i > 0 ? 1.5f : 1.1f)),
                                        i > 0 ? Pal.Cream : Pal.Gold, TextAnchor.MiddleCenter,
                                        new Vector2(Span.x, Cell * 2f));
                label.rectTransform.anchoredPosition =
                    new Vector2(0f, (_hillTop + _hillFoot) * .5f);

                var mark = label;
                mark.transform.localScale = Vector3.one * 2.1f;

                Tween.Scale(mark.transform, 1f, step * .55f, Ease.OutBack);
                Tween.Fade(mark, 0f, step * .95f, Ease.InQuad)
                     .OnDone(() => { if (mark) Destroy(mark.gameObject); });

                Audio.Sfx(i > 0 ? "tick" : "bell", i > 0 ? .5f : .8f, i > 0 ? 1f : 1.2f);

                if (i == 0) Flow.Flash(new Color(1f, .86f, .5f), .3f, .35f);

                yield return new WaitForSecondsRealtime(step);
            }
        }
    }
}
