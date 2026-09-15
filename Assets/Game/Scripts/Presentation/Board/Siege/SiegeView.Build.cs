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

            // A storm still falling over a board that has just been dealt again owns nothing:
            // the widgets it claimed went with `_mob`. See `_striking`.
            _striking.Clear();
            _volleying.Clear();

            // A board dealt again mid-charm must not hand the next beat a hold taken for a stone
            // that no longer exists.
            _holdUntil = 0f;

            _tally.Clear();
            _chain = null;
            _chainAura = null;

            // The pool's widgets hang off `_fx`, which this rebuild replaces — so a spare kept
            // across it is a destroyed node handed out as a live one, and every bolt after the
            // first rebuild would be invisible.
            _spare.Clear();

            _wave = 0;

            // The idle nudge is per run and its widgets hang off gems this rebuild is about to
            // replace — a kept one is a destroyed node held as a live one. See `SiegeView.Hint`.
            Unhinted();

            // The targeting layer hangs off the layers this rebuild is about to replace, so a
            // board dealt again while something was armed would leave a destroyed node behind a
            // live `Arming` — and the bar would still be showing a ring. Cleared through the
            // field rather than the property, because the layer it would tear down is already
            // gone; the screen re-arms nothing, which is what a fresh board should be.
            _arming = null;
            _aim = null;

            _meters = null;
            _fuseLayer = null;
            _cogLayer = null;
            _forecast = null;
            _forecastGroup = null;

            // **The bands are derived from the cell, not the other way round**, and the
            // arithmetic lives in `Bands.Of` rather than here so a test can sweep the screen
            // shapes a render can only look at one at a time (`SiegeBandTests`).
            float h = Span.y;
            var bands = Bands.Of(h, Cell, Height);
            float hill = bands.Hill, line = bands.Line;

            _lineBand = line;

            _hillTop = bands.HillTop;
            _hillFoot = bands.HillFoot;
            _lineY = bands.LineY;
            _gemCentre = (h * (.5f - hill - line) - h * .5f) * .5f;

            _hill = Layer("Hill");
            _mobs = Layer("Raiders");

            // **Bombs sit above the raiders, and that is about the finger rather than the eye.**
            // A bomb is the one thing on this hill that is tapped, and it is left exactly where
            // something died - which is ground other raiders go on walking over. Sharing the
            // raiders' layer would put a body between the finger and the only control on that half
            // of the screen, and the failure reads as a bomb that does not answer.
            _fuseLayer = Layer("Bombs");

            // **Cogs above the bombs, for the same reason bombs are above the raiders.** Both are
            // tapped, both are left where something died, and a bomber drops both - so they are
            // given a stacking order rather than left to whichever widget happened to be written
            // last. See `SiegeView.CogNudge` for the other half of not stacking two taps on one
            // point.
            _cogLayer = Layer("Cogs");

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

            // **A layer that is clipped to the board, for the one thing that comes from outside
            // it.** Everything else drawn here starts somewhere on the hill and stays there, which
            // is why `_fx` carries no mask and the procedural bolts clamp their own endpoints
            // instead (`OnBoard`, invariant 37ac). A stormcall's bolt cannot: it falls out of the
            // sky onto a raider, so it is longer than the room above whatever it hit and the top
            // of it belongs off the top of the picture. The two ways to spend that are to *shrink*
            // the bolt until it fits — which on a hill four cells deep is a spark rather than
            // lightning — or to let it run off and cut it at the frame, which is what every game
            // that has ever drawn a lightning strike does. `RectMask2D` rather than `Mask`: it is
            // a clip rectangle handed to the shader, so it costs no stencil buffer and no extra
            // draw call.
            _sky = Layer("Sky");
            _sky.gameObject.AddComponent<RectMask2D>();

            // **A damage figure is a readout, not an effect, so it is the last thing built and
            // nothing on this board is ever drawn over one.** Sharing `_fx` was right for as long
            // as the busiest thing here was a lit line — hits land 55ms apart at one raider and a
            // `Pop` is a cell wide, so a number was covered by very little for very little time.
            // A stormglass is not that: it throws every standing ward at every raider inside half
            // a second, so each beam, each two-cell scorch and each landing reel is a new sibling
            // over the top of every figure already standing. The number was drawn, was correct,
            // and was buried about thirty milliseconds after it appeared — which on a screen the
            // whole game stops for reads as a payoff that does not say what it did.
            //
            // Above `_sky` as well as `_fx`, because a stormcall's lightning is the other thing
            // big enough to cover one. The layer holds nothing but figures, so it costs one node.
            _figures = Layer("Figures");

            Ground();
            Line();
            Sockets();
            Deal();
            Targets();
            Banner();

            // Built last of the hill's furniture and hidden: it is only ever shown in a breather,
            // and a breather cannot happen before the first wave has been cleared.
            Foresight();

            // A fresh board is a fresh count-in. It is not started here: it is read off the
            // board's own quiet every frame the run is allowed to advance. See `CountIn`.
            _counted = 0;
        }

        /// <summary>
        /// How big the ground is drawn, given the band it has to cover and the shape it was drawn
        /// at: the smallest rectangle of the art's own aspect that covers the band.
        ///
        /// <para>
        /// <b>Worked out here rather than handed to an <see cref="AspectRatioFitter"/>, and the
        /// reason is that this has to be testable.</b> <see cref="Scenery.Cover"/> uses the fitter
        /// because a backdrop's parent resizes under it; the hill band does not — it is one
        /// rectangle, computed once — so the fitter would buy nothing but a dependency on when
        /// layout runs. This is <c>SiegeView.StrikeCentre</c>'s bargain for the same reason: the
        /// arithmetic is a static a fixture can sweep, and the fixture asserts <em>the
        /// consequence</em> (it covers, and its aspect is the art's) rather than restating the
        /// formula, which would agree with a wrong one as happily as with a right one.
        /// </para>
        /// </summary>
        /// <summary>
        /// How wide the hill and the rampart are drawn: the <em>plate's</em> width, not the field's.
        ///
        /// <para>
        /// <b>They were drawn at <c>Span.x</c>, which is a strip of bare plate down each side.</b>
        /// <c>ProtoView</c> sizes the plate to the field plus <c>Margin</c> on every edge, so a
        /// ground drawn at the field's own width leaves 18 units showing left and right — reported
        /// as tiny side gaps, and correct in the sense that every widget on a board is inset by
        /// that margin. A floor is not a widget on the board; it <em>is</em> the board, and so is
        /// the rampart the turrets stand on. Both run to the edge.
        /// </para>
        /// </summary>
        protected float PlateWide => Span.x + Margin * 2f;

        public static Vector2 GroundSize(Vector2 band, float aspect)
        {
            if (aspect <= 0f) return band;

            float tall = Mathf.Max(band.y, band.x / aspect);
            return new Vector2(tall * aspect, tall);
        }

        RectTransform Layer(string name)
        {
            var rt = UIKit.Node(name, Field);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = Field.sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// The hill: ground the raiders walk over, and the breach they come out of.
        ///
        /// <para>
        /// <b>Enveloped, never stretched — and it was stretched on every device this game runs
        /// on.</b> The ground was drawn as a plain <c>Image</c> sized to the band, which scales a
        /// sprite independently in x and y; the band's aspect is <b>0.99</b> on a 21:9 phone,
        /// <b>1.14</b> on an iPhone, <b>1.77</b> on a 16:9 screen and <b>2.85</b> on an iPad,
        /// against art authored at 0.80. So every square rock plate on the hill arrived as a wide
        /// rectangle and every rounded corner as an ellipse, by 1.24x at the kindest and 3.57x at
        /// the worst. <see cref="AspectRatioFitter.AspectMode.EnvelopeParent"/> is what
        /// <see cref="Scenery.Cover"/> has always used for a backdrop, and a floor wants it for
        /// exactly the same reason: what may vary between devices is <em>how much of the edges is
        /// cropped</em>, and never the shape of anything drawn.
        /// </para>
        ///
        /// <para>
        /// <b>Which is why the ground needs a clip of its own.</b> Enveloping means the sprite is
        /// deliberately larger than the band on one axis, so without a mask it would draw over the
        /// ward line and the field below it. <c>RectMask2D</c> rather than <c>Mask</c>, for the
        /// reason <c>_sky</c> gives: a clip rectangle handed to the shader costs no stencil buffer
        /// and no extra draw call. The mask wraps the ground alone — the raiders are in
        /// <c>_mobs</c> and are untouched.
        /// </para>
        ///
        /// <para>
        /// <b>Nothing offline could see any of this.</b> Every gate here reads the model, and
        /// <c>Tools/render_siege.py</c> — the one instrument built to catch a widget in the wrong
        /// place — stretched it in exactly the same way, so the mirror reproduced the fault
        /// faithfully and drew a picture that looked composed (44d). What saw it is the owner.
        /// </para>
        /// </summary>
        void Ground()
        {
            float h = _hillTop - _hillFoot + Cell * .35f;
            var band = new Vector2(PlateWide, h + Cell * .5f);

            var host = UIKit.Node("Ground", _hill);
            host.anchorMin = host.anchorMax = new Vector2(.5f, .5f);
            host.sizeDelta = band;
            host.anchoredPosition = new Vector2(0f, (_hillTop + _hillFoot) * .5f);
            host.gameObject.AddComponent<RectMask2D>();

            var rock = Hill(Rung);

            // An absent sprite keeps the plain fit: a white rectangle the size of the band is a
            // missing address (invariant 7b) and has to look like one, where enveloping an aspect
            // of nought would leave nothing on the screen at all.
            var ground = UIKit.Img("Rock", host, rock, Color.white,
                                   rock != null && rock.rect.height > 0f
                                       ? GroundSize(band, rock.rect.width / rock.rect.height)
                                       : band);
            ground.raycastTarget = false;

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
            // **The band `Compose` derived, never `LineBand` again.** The two are only the same
            // number when the field happens to take exactly its authored share: `LineBand` is one
            // half of a *ratio* that divides whatever the field leaves, so reading it directly
            // draws a rampart of one height under a line standing at another. It was out by a
            // sixth on a 16:9 canvas before anything moved it.
            float band = Span.y * _lineBand;

            var wall = UIKit.Img("Rampart", _wall, Piece("rampart"), Color.white,
                                 new Vector2(PlateWide, band * 1.02f));
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

                // Sized through the constants rather than by two numbers typed here, because
                // `BarrelGap` is a fraction of this picture and has to be converted into board
                // units by exactly the width it is drawn at.
                post.Body = UIKit.Img("Post", post.Node, WardArt(ward),
                                      post.Coat,
                                      new Vector2(Cell * BodyWide, Cell * BodyTall));
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

                // **The demand light, behind everything else on the tube.** It is what says
                // which colour the hill is asking for, and it is here rather than on the chassis
                // because this is the one widget in this mode a player's eye passes on the way
                // back to the gems - see `SiegeView.Wanted`.
                post.Want = UIKit.Img("Want", post.Tube, Art.Glow(96, 1.9f),
                                      Pal.A(TintOf(ward.Colour), 0f),
                                      new Vector2(Cell * 1.42f, Cell * .52f));
                post.Want.raycastTarget = false;

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

                // **The overcharge key, on the turret's own chassis.** It was a plate over the
                // fuel bar, which is a *meter*: it says how much, continuously, and it already
                // carries the demand light - so a control living on it was a button hidden inside
                // a readout. The chassis is the one part of a ward nothing else uses, and it is
                // what a finger goes for when it means "this turret". It is invisible and inert
                // until a charge is banked - see `SiegeView.Ready`, which switches its raycast
                // with its light, so a turret with nothing to throw cannot be tapped either.
                int seat = i;

                post.Halo = UIKit.Img("Halo", post.Node, Art.Glow(96, 2.1f),
                                      Pal.A(TintOf(ward.Colour), 0f),
                                      new Vector2(Cell * 1.3f, Cell * 1.3f));
                post.Halo.raycastTarget = false;
                post.Halo.rectTransform.anchoredPosition = new Vector2(0f, ChargeY);

                post.Dump = UIKit.Img("Dump", post.Node, Piece("charge"),
                                      Pal.A(Color.white, 0f),
                                      new Vector2(Cell * ChargeSize, Cell * ChargeSize));
                post.Dump.preserveAspect = true;
                post.Dump.raycastTarget = false;
                post.Dump.rectTransform.anchoredPosition = new Vector2(0f, ChargeY);

                var key = post.Dump.gameObject.AddComponent<Btn>();
                key.PressScale = .88f;
                key.Setup(() => Unleashed(seat), silent: true);

                // Hung off the turret rather than off the glyph, so it is not scaled by the
                // glyph's own pulse - a number that breathes is a number that is hard to read.
                var pipAt = new Vector2(Cell * ChargeSize * .54f,
                                        ChargeY + Cell * ChargeSize * .48f);

                post.Pip = UIKit.Img("Pip", post.Node, Art.Disc(64), new Color(.05f, .09f, .16f, 1f),
                                     new Vector2(Cell * .36f, Cell * .36f));
                post.Pip.raycastTarget = false;
                post.Pip.rectTransform.anchoredPosition = pipAt;
                post.Pip.enabled = false;

                post.Held = UIKit.Label("Held", post.Node, string.Empty,
                                        Mathf.RoundToInt(Cell * .27f), Pal.Cream,
                                        TextAnchor.MiddleCenter,
                                        new Vector2(Cell * .36f, Cell * .36f));
                post.Held.rectTransform.anchoredPosition = pipAt;
                post.Held.enabled = false;

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
        /// <summary>
        /// Where the overcharge glyph sits on a turret, and how big it is drawn.
        ///
        /// <b>The middle of the chassis, measured off the picture rather than guessed.</b> The body
        /// is <c>BodyTall</c> cells tall and hung a little above the node, so its own middle is a
        /// hair under the node's - which is the flat panel every turret in this pack carries, and
        /// the spot a device circled.
        ///
        /// <b>It is a tile rather than a line glyph</b>, because the first cut was `Ui/ic_power` in
        /// cream and came back from a device as simply not visible: a thin monochrome outline over
        /// a saturated chassis has nothing to separate it from what it is drawn on. A colourful
        /// badge with its own dark ground reads on all four ward colours at once - see
        /// <c>make_siege_art.charge</c>, which is where six candidates were compared at this size.
        /// </summary>
        const float ChargeY = 0f, ChargeSize = .62f;

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

        /// <summary>
        /// The dark sockets a gem stands in, drawn once and never taken away.
        ///
        /// <para>
        /// <b>The plate under them runs to the edge of the board, exactly as the ground and the
        /// rampart do</b> (see <see cref="PlateWide"/>). On a phone it always did, by arithmetic
        /// rather than by rule — the field is laid out to the width, so eight cells and a
        /// third of one is the plate's own width to within six units. On a tablet the field is
        /// laid out to the width a <em>phone</em> would have given it (invariant 37cc,
        /// <see cref="CellFor"/>), so the two part by a couple of hundred units and a plate cut
        /// to the cells would leave a strip of bare board down each side of the one band that is
        /// meant to read as a surface. A floor is not a widget on the board; it is the board.
        /// </para>
        /// </summary>
        void Sockets()
        {
            var plate = UIKit.Img("Plate", _field, Piece("plate"), Color.white,
                                  new Vector2(Mathf.Max(Cell * Width + Cell * .34f, PlateWide),
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
                var gem = Mint(Face(i), _board.CharmAt(i));
                gem.Img.rectTransform.anchoredPosition = CentreOf(i);
                _gems.Add(gem);

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
        /// How many beats the count-in draws: three numbers and then GO!.
        ///
        /// Public because <see cref="BeatsBy"/> is, and a fixture that typed a four of its own
        /// would go on passing the day this became five.
        /// </summary>
        public const int Beats = 4;

        /// <summary>How many of them this board has drawn. Reset by every <see cref="Compose"/>.</summary>
        int _counted;

        /// <summary>
        /// Three, two, one, and they come.
        ///
        /// <para>
        /// <b>Once, at the start, and never per wave.</b> What it is for is the half-second a
        /// player needs to look at the hill before anything is on it - a siege that opens with
        /// something already walking has been going on before they arrived. It is not a *pause*:
        /// the clock runs underneath it and the board is <c>Playable</c> throughout, so nothing
        /// is being held up.
        /// </para>
        /// <para>
        /// <b>Paced by the run's own clock rather than by a wall clock, and it was a coroutine on
        /// <c>WaitForSecondsRealtime</c>.</b> That drew the same four beats over the same 3.4
        /// seconds and agreed with the first wave only because both numbers were
        /// <see cref="SiegeTuning.FirstWaveAfter"/> - two clocks kept in step by arithmetic, which
        /// is fine until something stops one of them. Everything that holds a run stops the
        /// board's: a first-timer's tip (<c>RunHold.Teaching</c>), the pause menu, a panel over
        /// the board (<c>RunHold.Covered</c>) and the transition that is still hiding the screen
        /// (<c>RunHold.Opening</c>). None of them stops a wall clock, so the count-in burned
        /// through the tip boxes a first-timer was reading and they were handed a hill with no
        /// count-in and a wave already due. Read off <see cref="SiegeBoard.BeforeFirstWave"/> the
        /// two cannot disagree on any frame for any reason, and GO! landing with the first raider
        /// stops being an arrangement and becomes a fact - which is also why this needs no flag
        /// of its own and has no coroutine to strand (invariant 30g).
        /// </para>
        /// <para>
        /// <b>One beat a frame, and a missed one is skipped rather than queued.</b> A hitch that
        /// swallows a whole step should cost the beat it swallowed, not print two numbers on top
        /// of each other a frame apart - so the count jumps to where the quiet actually is and
        /// draws only the newest. The board clamps its own step (<c>SiegeBoard.Advance</c>), so
        /// on any frame rate anybody plays at nothing is ever missed.
        /// </para>
        /// </summary>
        void CountIn()
        {
            if (_counted >= Beats || _board == null || _fx == null) return;

            // The quiet the board says is left, not the seconds this screen has been up. It does
            // not move at all until the run is allowed to advance, which is the whole fix.
            int want = BeatsBy(_board.BeforeFirstWave);
            if (want <= _counted) return;

            _counted = want;
            CountBeat(Beats - want, SiegeTuning.FirstWaveAfter / Beats);
        }

        /// <summary>
        /// How many beats of the count-in are owed, given the seconds of opening quiet left.
        ///
        /// <para>
        /// <b>A static, so a fixture can sweep it</b> — <see cref="GroundSize"/>'s bargain and
        /// <c>SiegeView.StrikeCentre</c>'s: what a render can only look at one frame at a time, a
        /// test can walk end to end. It answers nought at the top of the quiet, climbs one beat a
        /// step, and reaches <see cref="Beats"/> no later than the quiet runs out — which is the
        /// property that makes GO! and the first raider land together
        /// (<c>SiegeCountInTests</c>).
        /// </para>
        /// </summary>
        /// <param name="left">
        /// <see cref="SiegeBoard.BeforeFirstWave"/>: seconds until the first wave steps out, and
        /// nought once it has.
        /// </param>
        public static int BeatsBy(float left)
        {
            float quiet = SiegeTuning.FirstWaveAfter;
            if (quiet <= 0f) return Beats;

            float step = quiet / Beats;
            float gone = quiet - Mathf.Clamp(left, 0f, quiet);

            // Nought only at the very top of the quiet: the first beat is owed the instant the
            // board's clock has moved at all, which is the frame the hill starts walking.
            if (gone <= 0f) return 0;

            return Mathf.Clamp(Mathf.FloorToInt(gone / step) + 1, 1, Beats);
        }

        /// <summary>
        /// One beat of the count-in: the number, its sound, and the flash the last one earns.
        ///
        /// <para>
        /// <b>Not called <c>Beat</c>, which is what it wants to be called.</b> There is already a
        /// <c>Beat(SiegeBeat)</c> on this view - the coroutine that draws one beat of a cascade -
        /// and the two would be an overload pair that differ only in their arguments. This
        /// project has paid once for two members sharing a name (<c>ModeScreen</c>'s coroutine
        /// hiding <c>RunScreen.Resolve</c>, and a won grove charged for at the next launch), and
        /// a compiler is happy with both.
        /// </para>
        ///
        /// <para>
        /// The label fades on the ordinary unscaled clock rather than on the run's, which is the
        /// one thing here deliberately left on a wall clock. A number is a three-quarter-second
        /// mark with nothing depending on it; holding one still behind a pause menu would mean a
        /// widget that has to be found and released on every way out of that menu, and what it
        /// would buy is a "2" that survives being paused. What the run's clock owns is *when a
        /// beat is drawn*, which is the half that decides anything.
        /// </para>
        /// </summary>
        void CountBeat(int i, float step)
        {
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
        }
    }
}
