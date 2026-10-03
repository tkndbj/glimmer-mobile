using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The gravity well (<c>UtilityKind.Gravity</c>): a black hole opened on one of four places
    /// on the hill, the hill falling into it, and the collapse that lets it go.
    ///
    /// <para>
    /// <b>The bodies are not animated by this file</b>, for the anvil's reason
    /// (<c>SiegeView.Anvil</c>): the pull, the hold and the release are the model's
    /// (<c>SiegeBoard.Gravity</c>), so <c>Follow</c> is already dragging every raider in on its
    /// own. What is added here is the thing doing the dragging, and the small circle each held
    /// body turns in so a crowd in one place reads as a crowd in orbit rather than as a pile.
    /// </para>
    /// <para>
    /// <b>Drawn off the model's state every frame and never off the tap</b> (invariant 48l): a
    /// well is open exactly while <c>SiegeBoard.Sinking</c> says so, so a board dealt again, a
    /// run that ends with one open and a panel raised over one all come out right without any
    /// of them being a case.
    /// </para>
    /// <para>
    /// <b>Stacked and spun, never baked</b> (MODES.md 37eu). Four pieces
    /// (<see cref="GravityFx"/>): the disc twice at two speeds and the arms faster than both,
    /// squashed into a tilted ellipse; the horizon over them, which is the one piece that is
    /// not additive because it is the one thing here that takes light away; and the ring. The
    /// near half of the disc is drawn <em>over</em> the raiders and the far half under them, so
    /// a body held in the well is standing inside it.
    /// </para>
    /// <para>
    /// <b>Nothing here is drawn from a sprite that has not arrived</b> (invariant 7b): every
    /// piece goes through <see cref="WellPiece"/>, which answers null, and a well short of a piece
    /// is a well.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the figures
        /// <summary>How the disc is squashed to read as tilted toward the eye, and how far it leans.</summary>
        const float WellTilt = .56f, WellLean = -16f;

        /// <summary>The disc's width and the horizon's, in cells.</summary>
        const float WellWide = 5.4f, WellCore = 1.5f;

        /// <summary>Seconds a well takes to open, and to fall in on itself.</summary>
        const float WellOpens = .40f, WellShuts = .26f;

        /// <summary>Degrees a second the disc turns once the hill is in it; faster while it is dragging.</summary>
        const float WellSpin = 115f, WellHaste = 2.6f;

        /// <summary>Seconds between two motes falling in, and between two rings closing on it.</summary>
        const float WellMote = .065f, WellPulse = .40f;

        /// <summary>How far from the well's middle a held body circles, in cells: inside its own box (39k).</summary>
        const float OrbitNear = .24f, OrbitFar = .56f;

        /// <summary>Radians a second a held body goes round.</summary>
        const float OrbitSpeed = 5.2f;

        static readonly Color WellViolet = new Color(.56f, .34f, 1f);
        static readonly Color WellDark = new Color(.05f, .02f, .13f);

        /// <summary>What a body in the well is washed in, and what one walking off its slow is.</summary>
        static readonly Color Sunken = new Color(.66f, .54f, 1f);
        static readonly Color Heavy = new Color(.84f, .76f, 1f);

        sealed class Pit
        {
            public Vector2 At;

            /// <summary>The far half, under the raiders; and the near half, over them.</summary>
            public RectTransform Under, Over;

            public Image Veil, Inner, Core, Ring;
            public Image DiscBack, DiscFront, ArmsBack, ArmsFront;

            /// <summary>Seconds it has been open, and seconds it has been shutting (or -1).</summary>
            public float Age, Shut = -1f;

            /// <summary>The disc's turn so far, in degrees.</summary>
            public float Spin;

            /// <summary>The two emitters' clocks.</summary>
            public float Mote, Pulse;
        }

        Pit _pit;

        /// <summary>The clock every held body's circle is read off. Real seconds.</summary>
        float _orbit;

        // ------------------------------------------------------------------ the pieces
        static Sprite Well(string key) => AssetLibrary.Sprite(AssetManifest.GravityFx(key));

        /// <summary>One piece of the well, or null when the hold has not delivered it.</summary>
        static Image WellPiece(string name, Transform parent, string key, float size, bool lit)
        {
            var sprite = Well(key);
            if (sprite == null) return null;

            var img = UIKit.Img(name, parent, sprite, Color.white, Vector2.one * size);
            img.raycastTarget = false;
            return lit ? Additive.Lit(img) : img;
        }

        /// <summary>
        /// Half of the well's own box, as a stencil, with a squashed node inside it for the
        /// pieces that turn.
        ///
        /// <b>Two halves that meet on the disc's long axis</b>: the far one is drawn under the
        /// horizon and under the raiders, the near one over both, and because the two stencils
        /// share the edge nothing is drawn twice and nothing is missed.
        /// </summary>
        static RectTransform Half(string name, RectTransform root, float wide, bool far)
        {
            var pane = UIKit.Img(name, root, Art.Pixel, Color.white, new Vector2(wide, wide * .5f));
            pane.rectTransform.anchoredPosition = new Vector2(0f, far ? wide * .25f : -wide * .25f);

            var mask = pane.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var tilt = UIKit.Box("Tilt", pane.transform, Vector2.zero, new Vector2(.5f, .5f),
                                 new Vector2(0f, far ? -wide * .25f : wide * .25f));
            tilt.localScale = new Vector3(1f, WellTilt, 1f);
            return tilt;
        }

        /// <summary>Where a well stands on the hill.</summary>
        Vector2 WellAt(int well)
            => new Vector2(LaneX(SiegeTuning.WellLane(well)), MarchY(SiegeTuning.WellMarch(well)));

        // ------------------------------------------------------------------ the frame
        /// <summary>
        /// Puts the well up, turns it and takes it down, off <c>SiegeBoard.Sinking</c>.
        ///
        /// <b>Run before the gate that holds a run</b>, with <c>Breathe</c> and for its reason: a
        /// well that stopped turning the moment a panel opened would tell a player the board had
        /// gone dead. It reads the board and decides nothing.
        /// </summary>
        void Welling(float dt)
        {
            if (_board == null) return;

            _orbit += dt;

            // **A run that ended with a well open shuts it.** The model's clock stops with the
            // run, so `Sinking` would stay true under the victory panel for ever.
            bool open = _board.Sinking && !Over;

            // A board dealt again took the layers this was drawn in with it.
            if (_pit != null && (!_pit.Under || !_pit.Over)) _pit = null;

            if (open && _pit != null && _pit.Shut >= 0f) Scrap();
            if (open && _pit == null) Opened(_board.Well);
            if (_pit == null) return;

            var pit = _pit;
            pit.Age += dt;

            if (!open && pit.Shut < 0f) Shutting(pit);

            if (pit.Shut >= 0f)
            {
                pit.Shut += dt;
                if (pit.Shut >= WellShuts) { Scrap(); return; }
            }

            bool dragging = open && !_board.Gathered;

            float grown = Ease.OutBack(Mathf.Clamp01(pit.Age / WellOpens));
            float gone = pit.Shut < 0f ? 0f : Ease.InCubic(Mathf.Clamp01(pit.Shut / WellShuts));
            float size = grown * (1f - gone) * (1f + .035f * Mathf.Sin(pit.Age * 7f));
            float light = Mathf.Clamp01(pit.Age / (WellOpens * .6f)) * (1f - gone);

            pit.Under.localScale = pit.Over.localScale = Vector3.one * Mathf.Max(0f, size);

            // Faster while it is dragging the hill in and as it falls in on itself: the two
            // moments something is visibly being done.
            pit.Spin -= dt * WellSpin * (dragging || pit.Shut >= 0f ? WellHaste : 1f);

            Turn(pit.DiscBack, pit.Spin, light);
            Turn(pit.DiscFront, pit.Spin, light);
            Turn(pit.ArmsBack, pit.Spin * 2.3f, light * .9f);
            Turn(pit.ArmsFront, pit.Spin * 2.3f, light * .9f);
            Turn(pit.Inner, -pit.Spin * .62f + 40f, light * .72f);

            if (pit.Ring != null)
            {
                float beat = .82f + .18f * Mathf.Sin(pit.Age * 11f);
                pit.Ring.color = new Color(1f, 1f, 1f, light * beat);
                pit.Ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, pit.Age * -38f);
            }

            if (pit.Veil != null) pit.Veil.color = Pal.A(WellDark, .62f * light);

            if (pit.Shut >= 0f) return;

            // The light falling in, and the rings closing on it. Emitted for as long as the
            // well is open and never after: a mote set off as it shuts would arrive at nothing.
            pit.Mote += dt;
            while (pit.Mote >= WellMote) { pit.Mote -= WellMote; Infall(pit); }

            pit.Pulse += dt;
            if (pit.Pulse >= WellPulse) { pit.Pulse -= WellPulse; Closing(pit, 4.8f, .52f); }
        }

        static void Turn(Image piece, float degrees, float alpha)
        {
            if (piece == null) return;

            piece.rectTransform.localRotation = Quaternion.Euler(0f, 0f, degrees);
            piece.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        // ------------------------------------------------------------------ opening
        void Opened(int well)
        {
            if (well < 0 || _hill == null || _fx == null) return;

            var pit = Raise(WellAt(well), _hill, _fx, Cell * WellWide, Cell * WellCore);
            pit.Over.SetAsFirstSibling();

            _pit = pit;

            // The moment itself: the sky dims violet, one great ring closes on the place, and
            // every body it took is drawn a thread of light to it.
            Lightup(WellViolet, .20f, .50f);
            Closing(pit, 8.5f, WellOpens + .12f);
            ShakeBoard(Cell * .08f);

            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || !mob.Node || mob.Falling || mob.Boss) continue;

                Taken(mob.Node.anchoredPosition, pit.At, i);
            }

            // A rising whine pitched down an octave is a thing winding up under the floor; the
            // sweep over it is the air going in.
            Audio.Sfx("charge", .85f, .52f);
            Audio.Sfx("whoosh", .60f, .62f);
        }

        /// <summary>
        /// Stacks a hole at <paramref name="at"/>: the far half of the disc, the horizon and the
        /// ring under <paramref name="under"/>, the near half under <paramref name="over"/>.
        /// Built closed; whoever raised it turns it and takes it down.
        ///
        /// <b>One stack for both holes this mode draws</b> - the well a Gravity Hole opens on the
        /// hill and the one a singularity opens on the field (<c>SiegeView.Singularity</c>) - so
        /// the two cannot come to look like different things.
        /// </summary>
        Pit Raise(Vector2 at, RectTransform under, RectTransform over, float wide, float core)
        {
            var pit = new Pit { At = at };

            pit.Under = UIKit.Box("Well under", under, Vector2.zero, new Vector2(.5f, .5f), pit.At);
            pit.Over = UIKit.Box("Well over", over, Vector2.zero, new Vector2(.5f, .5f), pit.At);

            pit.Under.localRotation = pit.Over.localRotation = Quaternion.Euler(0f, 0f, WellLean);
            pit.Under.localScale = pit.Over.localScale = Vector3.zero;

            // **The dark first.** A black hole on a lit hill is a dark disc among bright things;
            // what makes it the brightest thing on the board is the ground round it going out.
            pit.Veil = UIKit.Img("Veil", pit.Under, Art.Glow(128, 1.5f), Pal.A(WellDark, 0f),
                                 new Vector2(wide * 2.5f, wide * 1.9f));
            pit.Veil.raycastTarget = false;

            // The second disc: smaller, turning the other way, whole. It is what makes the
            // bands cross rather than slide, which is the difference between a disc and a wheel.
            var slow = UIKit.Box("Tilt", pit.Under, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
            slow.localScale = new Vector3(1f, WellTilt, 1f);
            pit.Inner = WellPiece("Inner", slow, GravityFx.Disc, wide * .78f, true);

            var far = Half("Far", pit.Under, wide * 1.06f, far: true);
            pit.DiscBack = WellPiece("Disc", far, GravityFx.Disc, wide, true);
            pit.ArmsBack = WellPiece("Arms", far, GravityFx.Arms, wide * .94f, true);

            pit.Core = WellPiece("Core", pit.Under, GravityFx.Core, core, false);
            pit.Ring = WellPiece("Ring", pit.Under, GravityFx.Ring, core * 1.36f, true);

            var near = Half("Near", pit.Over, wide * 1.06f, far: false);
            pit.DiscFront = WellPiece("Disc", near, GravityFx.Disc, wide, true);
            pit.ArmsFront = WellPiece("Arms", near, GravityFx.Arms, wide * .94f, true);

            return pit;
        }

        /// <summary>A thread of light from a body to the well, as it is taken.</summary>
        void Taken(Vector2 from, Vector2 to, int ordinal)
        {
            for (int n = 0; n < 2; n++)
            {
                var mote = Lit("Taken", _fx, Art.Spark(64), Pal.A(Pal.Bloom, 0f),
                               Vector2.one * Cell * (.34f - n * .10f));
                var rt = mote.rectTransform;
                rt.anchoredPosition = from;

                var span = to - from;
                float lead = n * .07f + (ordinal % 4) * .02f;

                Tween.Run(SiegeTuning.GravityGather, Ease.InQuad, t =>
                {
                    if (!mote) return;

                    rt.anchoredPosition = from + span * t;
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f);
                    rt.localScale = new Vector3(1f - t * .5f, 1f + t * 2.4f, 1f);
                    mote.color = Pal.A(Color.Lerp(Color.white, Pal.Bloom, t), Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI));
                }, mote).Delay(lead).OnDone(() => { if (mote) Destroy(mote.gameObject); });
            }
        }

        /// <summary>One mote of light spiralling into the well from outside the disc.</summary>
        void Infall(Pit pit)
        {
            if (!pit.Over) return;

            float from = Random.Range(0f, Mathf.PI * 2f);
            float reach = Cell * Random.Range(2.3f, 3.5f);
            float turns = Random.Range(1.1f, 1.7f) * Mathf.PI * 2f;
            float big = Cell * Random.Range(.16f, .30f);
            var tint = Random.value < .35f ? Pal.Sun : Random.value < .5f ? Pal.Bloom : WellViolet;

            var mote = Lit("Mote", pit.Over, Art.Spark(64), Pal.A(tint, 0f), Vector2.one * big);
            var rt = mote.rectTransform;

            Tween.Run(Random.Range(.50f, .72f), Ease.Linear, t =>
            {
                if (!mote) return;

                float fall = t * t;
                float at = from - turns * fall;
                float out_ = reach * (1f - fall);

                rt.anchoredPosition = new Vector2(Mathf.Cos(at) * out_, Mathf.Sin(at) * out_ * WellTilt);
                rt.localRotation = Quaternion.Euler(0f, 0f, at * Mathf.Rad2Deg);
                rt.localScale = new Vector3(1f + fall * 1.8f, 1f - fall * .55f, 1f);
                mote.color = Pal.A(Color.Lerp(tint, Color.white, fall),
                                   Mathf.Clamp01(t * 5f) * (1f - fall * fall));
            }, mote).OnDone(() => { if (mote) Destroy(mote.gameObject); });
        }

        /// <summary>
        /// A ring closing on the well rather than leaving it: everything else on this hill
        /// throws its rings outward, and the direction is what says <em>in</em>.
        /// </summary>
        void Closing(Pit pit, float cells, float life)
        {
            if (!pit.Over) return;

            var ring = Lit("Closing", pit.Over, StrikeFx.Ring, Pal.A(WellViolet, 0f),
                           new Vector2(Cell * cells, Cell * cells * WellTilt));
            if (ring == null) return;

            var rt = ring.rectTransform;
            rt.SetAsFirstSibling();

            Tween.Run(life, Ease.InQuad, t =>
            {
                if (!ring) return;

                rt.localScale = Vector3.one * Mathf.Lerp(1f, .14f, t);
                ring.color = Pal.A(Color.Lerp(WellViolet, Pal.Bloom, t), Mathf.Sin(t * Mathf.PI) * .85f);
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        // ------------------------------------------------------------------ shutting
        /// <summary>
        /// The well falling in on itself and letting go: the disc is swallowed, and what comes
        /// back out is light.
        /// </summary>
        void Shutting(Pit pit)
        {
            pit.Shut = 0f;

            // Nothing is thrown over a finished run: the panel is on its way.
            if (Over || _fx == null) return;

            var at = pit.At;

            Tween.After(WellShuts * .8f, () =>
            {
                if (_fx == null) return;

                Flash(at, Pal.Bloom, 3.6f);
                Ripple(at, WellViolet, Cell * 6.4f, .55f, 0f);
                Ripple(at, Pal.Sun, Cell * 4.2f, .45f, .07f);
                Burst.Sparks(_fx, at, Pal.Bloom, 22, Cell * 3.2f, Cell * .24f, .55f);
                Twinkles(at, WellViolet, 9, Cell * 1.7f);
                Lightup(Pal.Cream, .20f, .26f);
                ShakeBoard(Cell * .15f);

                Audio.Sfx("boom", .70f, .46f);
                Audio.Sfx("whoosh", .50f, 1.25f);
            }, _fx);
        }

        void Scrap()
        {
            if (_pit == null) return;

            if (_pit.Under) Destroy(_pit.Under.gameObject);
            if (_pit.Over) Destroy(_pit.Over.gameObject);
            _pit = null;
        }

        // ------------------------------------------------------------------ the bodies
        /// <summary>
        /// Where a raider is drawn: where the model says it stands, and - while a well has hold
        /// of it - a small circle round that.
        ///
        /// <para>
        /// <b>The circle is the drawing's and stays inside the well's own box</b>
        /// (<see cref="OrbitFar"/>), so a firepot thrown at the clump finds every body in the
        /// box it is drawn in (invariant 39k). Its phase and its radius are read off the
        /// raider's id, so the same hill circles the same way on two devices.
        /// </para>
        /// </summary>
        Vector2 Standing(Mob mob, SiegeRaider raider)
        {
            var at = new Vector2(LaneX(raider.Lane + raider.Drift), MarchY(raider.March));
            if (mob.Orbit <= 0f) return at;

            float turn = mob.Id * 2.39996f - _orbit * OrbitSpeed;
            float share = mob.Id * .618034f;
            share -= Mathf.Floor(share);

            float reach = Cell * Mathf.Lerp(OrbitNear, OrbitFar, share) * mob.Orbit;

            return at + new Vector2(Mathf.Cos(turn) * reach, Mathf.Sin(turn) * reach * WellTilt);
        }

        /// <summary>
        /// How far into its circle a body is, eased toward what the model says: one while a
        /// well has hold of it, nought otherwise. Answers what it was <em>before</em> this
        /// frame, which is what lets the caller write the body upright one last time.
        /// </summary>
        float Orbiting(Mob mob, SiegeRaider raider)
        {
            float was = mob.Orbit;
            float want = raider != null && raider.Sunk ? 1f : 0f;

            mob.Orbit = Mathf.MoveTowards(mob.Orbit, want, Time.unscaledDeltaTime * 2.6f);
            return was;
        }

        /// <summary>Degrees a held body is rocked as it goes round.</summary>
        float Tumble(Mob mob) => Mathf.Sin(_orbit * 7.5f + mob.Id * 1.7f) * 16f * mob.Orbit;

        // ------------------------------------------------------------------ aiming
        /// <summary>
        /// The hill as the four places a well may be opened: a pane to each quarter, and the
        /// item's own picture turning where the well would stand.
        ///
        /// <b>Geometry and never outcome</b> (invariant 32c): the panes say where the four
        /// places are and nothing about which is worth it.
        /// </summary>
        void AimWells()
        {
            float wide = Span.x * .5f;
            float tall = (MarchY(0f) - MarchY(1f)) * .5f;
            var mark = _arming != null ? Art.S(_arming.Art) : null;

            for (int i = 0; i < SiegeTuning.GravityWells; i++)
            {
                int well = i;
                var centre = new Vector2((i & 1) == 0 ? -wide * .5f : wide * .5f,
                                         MarchY(SiegeTuning.WellMarch(i)));

                var pane = UIKit.Button("AimWell" + i, _aim, Art.Round(18), new Vector2(wide, tall),
                                        new Vector2(.5f, .5f), centre,
                                        () => Loose(SiegeAim.AtWell(well)));
                pane.PressScale = .97f;
                pane.ClickSfx = null;

                // The whole quarter catches the tap; the gutter is painted, as on the firepot's grid.
                var catcher = pane.GetComponent<Image>();
                if (catcher != null) catcher.color = new Color(0f, 0f, 0f, 0f);

                var face = UIKit.Img("Face", pane.transform, Art.Round(18), Pal.A(WellViolet, .20f),
                                     new Vector2(wide - Gutter, tall - Gutter));
                face.type = Image.Type.Sliced;

                // Where the well would open, which is not the middle of the quarter: it stands
                // on a lane (`SiegeTuning.WellLane`).
                var stand = new Vector2(WellAt(i).x - centre.x, 0f);
                float eye = Mathf.Min(wide, tall) * .62f;

                var ring = UIKit.Img("Eye", pane.transform, Art.Ring(96, 6f), Pal.A(Pal.Bloom, .80f),
                                     Vector2.one * eye, new Vector2(.5f, .5f), stand);
                Tween.Breathe(ring.transform, .07f, 1.5f, i * .21f);

                if (mark == null) continue;

                var picture = UIKit.Img("Mark", pane.transform, mark, Color.white,
                                        Vector2.one * eye * .74f, new Vector2(.5f, .5f), stand);
                picture.preserveAspect = true;
                Tween.Breathe(picture.transform, .05f, 1.5f, i * .21f + .4f);
            }
        }
    }
}
