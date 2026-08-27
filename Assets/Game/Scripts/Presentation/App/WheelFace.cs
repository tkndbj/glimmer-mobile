using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What colour a slice is drawn in.
    ///
    /// <para>
    /// <b>Derived from where a slice sits in its own wheel, never from its percentage.</b> A
    /// table mapping 500% to gold would be a second place the ladder is written down, and the
    /// day content retunes the top slice to 400 the wheel would quietly lose its best colour
    /// with nothing to show for it. Rank keeps the ramp meaning "this is the good one" whatever
    /// the numbers are — the same reason <c>ModeLook</c> exists rather than a switch on a mode
    /// id, one folder over.
    /// </para>
    /// <para>
    /// The ramp deliberately runs cool to warm rather than through the whole palette. The
    /// wheel is one object the eye has to rank at a glance while it is moving, and six unrelated
    /// hues rank as "six colours" rather than as "worse to better".
    /// </para>
    /// </summary>
    static class WheelPaint
    {
        /// <summary>
        /// Worst to best, in six steps the eye can put in order.
        ///
        /// <para>
        /// <b>The top rung is deliberately not gold.</b> Gold is what the rim, the hub and the
        /// lamps are made of, so a gold jackpot slice is the one prize on the wheel with nothing
        /// to stand against — and <c>Pal.Gold</c> and <c>Pal.Sun</c> differ by seven parts in a
        /// hundred, which wasted a rung of a six-rung ramp on a colour nobody could tell from
        /// its neighbour. <c>Pal.Bloom</c> is the only tint in this palette that cannot be
        /// mistaken for the frame, and "the rare one is the odd colour out" is a convention
        /// every player of anything already knows.
        /// </para>
        /// </summary>
        static readonly Color[] Ramp =
        {
            Pal.Azure,
            Pal.Aqua,
            Pal.Verdant,
            Pal.Sun,
            Pal.Amber,
            Pal.Bloom,
        };

        /// <summary>What a wedge is settled against: the panel's own dark, not black.</summary>
        static readonly Color Deep = new Color(.07f, .11f, .15f);

        /// <summary>
        /// The tint for a slice: its place in the <em>order</em> of this wheel's figures.
        ///
        /// <para>
        /// <b>Ranked among the distinct values, not along the span between worst and best.</b>
        /// The obvious rule is to interpolate the percentage across the ramp, and it collapses
        /// on any ladder with a real top prize: the shipped wheel runs 100 to 500, so five of
        /// its eight slices land inside the first fifth of that span and come out the same
        /// colour, while two rungs of the ramp are never reached at all. A wheel whose slices
        /// cannot be ranked by eye has no reason to be coloured. Counting distinct figures
        /// instead means the <em>n</em>th best prize gets the <em>n</em>th colour, whatever the
        /// numbers are — so a content push that changes the ladder cannot flatten the picture.
        /// </para>
        /// <para>
        /// Quadratic in the slice count, which is at most twelve, and asked once per wedge when
        /// a panel is built.
        /// </para>
        /// </summary>
        public static Color For(BonusWheel wheel, int index)
        {
            if (wheel == null || wheel.Count == 0) return Ramp[0];

            int percent = wheel.SliceAt(index).Percent;
            int distinct = 0, below = 0;

            for (int i = 0; i < wheel.Count; i++)
            {
                int other = wheel.SliceAt(i).Percent;

                bool seen = false;
                for (int j = 0; j < i; j++)
                    if (wheel.SliceAt(j).Percent == other) { seen = true; break; }

                if (seen) continue;

                distinct++;
                if (other < percent) below++;
            }

            // A flat wheel would divide by nothing. It cannot reach here — the reader refuses
            // one — and the fallback is the calm end rather than the loud one anyway.
            if (distinct <= 1) return Ramp[0];

            int rank = below * (Ramp.Length - 1) / (distinct - 1);
            return Ramp[Mathf.Clamp(rank, 0, Ramp.Length - 1)];
        }

        /// <summary>
        /// The wedge's own fill: its tint, alternately deep and deeper.
        ///
        /// <para>
        /// <b>Blended toward the panel's dark, never multiplied.</b> The first cut scaled every
        /// channel by a fifth, which produced eight near-black wedges under a gold rim; raising
        /// the factor fixed the blues and greens and left the two warm rungs — the two <em>best
        /// prizes</em> — as olive mud, because scaling a saturated yellow toward zero is exactly
        /// how olive is made. A lerp keeps the hue and moves only the value, so a gold stays
        /// gold and a pink stays pink at any depth. Both faults were invisible in every check
        /// and obvious in the first rendered frame, which is what <c>Tools/render_wheel.py</c>
        /// is for.
        /// </para>
        /// <para>
        /// <b>The alternation is now slight, because the spokes do that job.</b> It exists
        /// because the ramp cannot promise a different hue for every position — two neighbours
        /// can legitimately land on one colour, and without something between them they read as
        /// one wide slice. Doing it with depth alone meant taking a rung down far enough to
        /// separate it, and far enough is where a yellow becomes khaki. A drawn boundary
        /// separates any two slices at any depth, so the shading is back to a hint of relief.
        /// </para>
        /// </summary>
        public static Color Seat(Color tint, int index)
        {
            float k = index % 2 == 0 ? .14f : .26f;
            return Color.Lerp(tint, Deep, k);
        }
    }

    /// <summary>
    /// A wheel of fortune, drawn and turned. It knows nothing about ads, rewards or panels.
    ///
    /// <para>
    /// <b>A widget rather than part of its panel</b>, for the reason <c>GridView</c> and
    /// <c>ProductCard</c> are: the panel around it is already the offer, the ad, five honest
    /// refusals and a payout, and a screen that has grown a fourth responsibility has grown one
    /// too many. What is here is exactly the part that could be dropped into a second use — an
    /// event wheel, a seasonal one — without carrying an ad table with it.
    /// </para>
    /// <para>
    /// <b>It owns no timing of its own.</b> Every number about how it moves comes from
    /// <see cref="WheelSpin"/> in Domain, which is tested offline: where the wheel must come to
    /// rest, how far it travels, the curve it travels on and how many pegs have gone past at a
    /// given angle. That is not tidiness — the resting angle <em>is</em> the payout, so a wheel
    /// stopping half a degree into its neighbour is the panel disagreeing with what the server
    /// is about to grant, and motion is the one subsystem whose faults show up only in play.
    /// </para>
    /// <para>
    /// <b>Every part of it is generated art.</b> An <c>Image</c> whose sprite has not arrived is
    /// a white rectangle, and this is the most ceremonial object in the game — a white square
    /// where the prize should be is worse here than anywhere else. It also belongs to no chapter,
    /// so delivered art for it would sit in the global group and be loaded by every screen in
    /// the game for the use of one.
    /// </para>
    /// </summary>
    public sealed class WheelFace : MonoBehaviour
    {
        // ------------------------------------------------------------- geometry
        /// <summary>How much of the radius the hub takes, and where a slice's figure sits.</summary>
        const float HubFraction = .24f, LabelRadius = .655f, BadgeRadius = .45f;

        /// <summary>The rim's thickness and the lamps set into it, as fractions of the radius.</summary>
        const float RimThickness = .075f, LampSize = .052f, LampRadius = .945f;

        /// <summary>The pointer, as a fraction of the diameter, and how far it kicks off a peg.</summary>
        const float PointerSize = .21f, PointerKick = 15f;

        BonusWheel _wheel;
        int _baseAmount;
        float _radius;

        RectTransform _rotor, _pointer;
        RectTransform[] _wedges;
        Image[] _faces;
        Image _hubGlow;

        int _pegs;
        float _kick;
        float _lastTick;

        /// <summary>True from the moment a spin starts until it has come to rest.</summary>
        public bool Spinning { get; private set; }

        /// <summary>True once a spin has finished, so a panel cannot sell the same one twice.</summary>
        public bool Landed { get; private set; }

        /// <summary>
        /// Builds a wheel inside <paramref name="host"/>.
        ///
        /// <paramref name="baseAmount"/> is what the placement pays flat; every figure on the
        /// rim is that amount through its slice's multiplier, so the wheel prints real credits
        /// rather than a ratio the player has to do arithmetic on. The multiplier is printed
        /// too, small, under the figure — it is what makes the wheel legible as a ladder.
        /// </summary>
        public static WheelFace Attach(RectTransform host, BonusWheel wheel, int baseAmount,
                                       float diameter)
        {
            var face = host.gameObject.AddComponent<WheelFace>();
            face.Build(host, wheel, baseAmount, diameter);
            return face;
        }

        void Build(RectTransform host, BonusWheel wheel, int baseAmount, float diameter)
        {
            _wheel = wheel;
            _baseAmount = baseAmount;
            _radius = diameter * .5f;

            int count = wheel.Count;
            float step = 360f / count;

            // Light behind the whole thing, so the rim has something to sit against on a dark
            // panel and the wheel does not read as a sticker.
            UIKit.Img("Aura", host, Art.Glow(128, 2.0f), new Color(1f, .86f, .46f, .20f),
                      Vector2.one * (diameter * 1.30f), new Vector2(.5f, .5f), Vector2.zero);

            // The seat: a dark disc a shade wider than the wedges, which is what gives every
            // slice an outline without eight of them each needing one.
            UIKit.Img("Seat", host, Art.Disc(256), new Color(.05f, .09f, .13f, .96f),
                      Vector2.one * (diameter + 14f), new Vector2(.5f, .5f), Vector2.zero);

            _rotor = UIKit.Node("Rotor", host);
            _rotor.sizeDelta = Vector2.one * diameter;

            _wedges = new RectTransform[count];
            _faces = new Image[count];

            var wedgeSprite = Art.Wedge(320, count, HubFraction);

            for (int i = 0; i < count; i++)
            {
                var slice = wheel.SliceAt(i);
                var tint = WheelPaint.For(wheel, i);

                var wedge = UIKit.Img("W" + i, _rotor, wedgeSprite, WheelPaint.Seat(tint, i),
                                      Vector2.one * diameter, new Vector2(.5f, .5f), Vector2.zero);

                // Negative Z is clockwise, and the sprite is drawn pointing straight up — so
                // this puts slice i's own centre line at (i + ½) steps clockwise from the
                // pointer. WheelSpin.Rest is the exact inverse of it, which is what makes the
                // wheel stop where the seed said it would.
                wedge.transform.localRotation = Quaternion.Euler(0f, 0f, -(i + .5f) * step);

                _wedges[i] = (RectTransform)wedge.transform;
                _faces[i] = wedge;

                BuildSliceFace(_wedges[i], slice, tint, step);
            }

            BuildSpokes(_rotor, diameter, count, step);
            BuildRim(host, diameter, count, step);
            BuildHub(host, diameter);
            BuildPointer(host, diameter);
        }

        /// <summary>
        /// One slice's figure and its multiplier badge, set radially inside the wedge.
        ///
        /// <para>
        /// The figure is the money and the badge is the story, and they are that way round on
        /// purpose: "1,000" is what lands in the wallet, and "x5" is why. A wheel printing only
        /// multipliers asks the player to multiply while it is moving; one printing only
        /// figures throws away the whole reason the ladder is exciting.
        /// </para>
        /// <para>
        /// The box is measured from the wedge's own arc so a twelve-slice wheel shrinks its
        /// captions rather than overlapping them, and both are <see cref="UIKit.Shrinkable"/>
        /// on top of that — a translated thousands separator is wider in some languages than in
        /// English, and there is no room here for it to spill into.
        /// </para>
        /// </summary>
        void BuildSliceFace(RectTransform wedge, WheelSlice slice, Color tint, float step)
        {
            float arc = 2f * _radius * LabelRadius * Mathf.Sin(step * .5f * Mathf.Deg2Rad);
            float width = Mathf.Max(70f, arc * .92f);

            var amount = UIKit.Titled("Amount", wedge,
                                      Compact.Number(slice.Pays(_baseAmount)), 46, Pal.Cream,
                                      TextAnchor.MiddleCenter, new Vector2(width, 60f),
                                      new Vector2(.5f, .5f),
                                      new Vector2(0f, _radius * LabelRadius), 4f, 4f);
            UIKit.Shrinkable(amount, 22);

            if (!slice.IsBonus) return;

            // Only on a slice that actually multiplies. Printing "x1" on the ordinary payout
            // would advertise the one outcome that is not a bonus as though it were one.
            //
            // A cream chip with dark text on it, rather than tinted text on the wedge. The
            // wedge is already the slice's own colour, so a badge painted in that colour is the
            // one thing on the wheel guaranteed to have nothing to sit against — which is
            // exactly how the first cut drew it, and it disappeared on every slice at once.
            // Inverting it also stops the badge and the figure reading as the same label twice.
            float chipW = Mathf.Min(width * .72f, 118f);

            var chip = UIKit.Img("Chip", wedge, Art.Round(18), Pal.A(Pal.Cream, .93f),
                                 new Vector2(chipW, 46f), new Vector2(.5f, .5f),
                                 new Vector2(0f, _radius * BadgeRadius));

            var badge = UIKit.Titled("Mult", chip.transform,
                                     Loc.Format("ui.wheel.mult", Multiplier(slice)), 30,
                                     new Color(.20f, .14f, .10f), TextAnchor.MiddleCenter,
                                     new Vector2(chipW - 14f, 38f), new Vector2(.5f, .5f),
                                     Vector2.zero, outline: 0f, shadow: 0f);
            UIKit.Shrinkable(badge, 16);
        }

        /// <summary>
        /// A multiplier as a player would say it: "2" for double, "2.5" for two and a half.
        ///
        /// <para>
        /// Trailing zeroes are trimmed rather than formatted away, so the badge reads "x3"
        /// instead of "x3.0" — and the decimal point comes from the culture, because a comma
        /// is the decimal separator across most of the markets this ships in.
        /// </para>
        /// </summary>
        static string Multiplier(WheelSlice slice)
        {
            int percent = slice.Percent;
            if (percent % 100 == 0) return (percent / 100).ToString();

            return (percent / 100f).ToString("0.##");
        }

        /// <summary>
        /// A dark bar on every boundary, from the hub to the rim.
        ///
        /// <para>
        /// It is what makes a wheel read as a wheel rather than as a pie chart, and it is doing
        /// real work: two neighbouring slices can land on one colour, because the ramp has six
        /// rungs and a wheel may have twelve. Separating those by shading alone means taking one
        /// of them far enough down to tell apart, and far enough is where a saturated yellow
        /// becomes khaki — so the wheel's two best prizes were the two that suffered. A drawn
        /// boundary separates any pair at any depth and costs one image per slice.
        /// </para>
        /// <para>
        /// On the rotor rather than the host, so it turns with the slices it divides. Behind the
        /// hub cap and under the rim at both ends, so neither end needs to be measured exactly.
        /// </para>
        /// </summary>
        void BuildSpokes(RectTransform rotor, float diameter, int count, float step)
        {
            float inner = _radius * HubFraction;
            float length = _radius - inner + 6f;

            for (int i = 0; i < count; i++)
            {
                var bar = UIKit.Img("S" + i, rotor, Art.Pixel, new Color(.05f, .08f, .11f, .85f),
                                    new Vector2(4f, length), new Vector2(.5f, .5f),
                                    new Vector2(0f, inner + length * .5f - 3f));

                bar.transform.localRotation = Quaternion.Euler(0f, 0f, -i * step);

                // Rotated about the wheel's centre rather than its own, which a rotation on an
                // off-centre child does not do by itself.
                var rt = (RectTransform)bar.transform;
                rt.anchoredPosition = new Vector2(
                    Mathf.Sin(i * step * Mathf.Deg2Rad) * (inner + length * .5f - 3f),
                    Mathf.Cos(i * step * Mathf.Deg2Rad) * (inner + length * .5f - 3f));
            }
        }

        /// <summary>
        /// The rim, and the lamps set into it — one on every boundary, so the lamps and the
        /// slice edges are the same thing rather than two decorations that nearly line up.
        /// </summary>
        void BuildRim(RectTransform host, float diameter, int count, float step)
        {
            UIKit.Img("Rim", host, Art.Ring(256, 256f * RimThickness), Pal.Gold,
                      Vector2.one * (diameter + 16f), new Vector2(.5f, .5f), Vector2.zero);

            UIKit.Img("RimInner", host, Art.Ring(256, 5f), new Color(1f, .93f, .70f, .55f),
                      Vector2.one * (diameter - 4f), new Vector2(.5f, .5f), Vector2.zero);

            float lamp = diameter * LampSize;

            for (int i = 0; i < count; i++)
            {
                // On the boundaries rather than over the slice centres, so a lamp is the peg the
                // pointer clicks past and the two are the same thing instead of two decorations
                // that nearly line up.
                float a = i * step * Mathf.Deg2Rad;
                var at = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (_radius * LampRadius);

                var bulb = UIKit.Img("L" + i, host, Art.Disc(64), new Color(1f, .97f, .84f, .95f),
                                     Vector2.one * lamp, new Vector2(.5f, .5f), at);

                // Out of phase around the rim, so the lamps chase rather than blink together —
                // one bounded cycle per lamp rather than a sequencer anybody has to drive.
                Tween.Value(1f, .35f, 1.1f, v => { if (bulb) bulb.color = new Color(1f, .97f, .84f, v); },
                            Ease.InOutSine, bulb.gameObject)
                     .Delay(i * (1.1f / count))
                     .Loop(-1);
            }
        }

        /// <summary>The cap in the middle: what the wedges point at, and where the light comes from.</summary>
        void BuildHub(RectTransform host, float diameter)
        {
            float hub = diameter * HubFraction;

            _hubGlow = UIKit.Img("HubGlow", host, Art.Glow(128, 2.2f), new Color(1f, .88f, .48f, .30f),
                                 Vector2.one * (hub * 2.1f), new Vector2(.5f, .5f), Vector2.zero);

            var rays = UIKit.Img("HubRays", host, Art.Rays(128, 10), new Color(1f, .90f, .55f, .30f),
                                 Vector2.one * (hub * 1.7f), new Vector2(.5f, .5f), Vector2.zero);
            Tween.Run(24f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 360f); },
                      rays.gameObject, "spin").Loop(-1, false);

            UIKit.Img("Hub", host, Art.Disc(128), new Color(.09f, .14f, .19f, 1f),
                      Vector2.one * hub, new Vector2(.5f, .5f), Vector2.zero);

            UIKit.Img("HubRim", host, Art.Ring(128, 9f), Pal.Gold,
                      Vector2.one * hub, new Vector2(.5f, .5f), Vector2.zero);

            var pip = UIKit.Img("HubPip", host, Art.Disc(64), Pal.Gold,
                                Vector2.one * (hub * .26f), new Vector2(.5f, .5f), Vector2.zero);
            Tween.Breathe(pip.transform, .10f, 1.8f);
        }

        /// <summary>
        /// The pointer, hung above the rim and pivoted at its own tip.
        ///
        /// Pivoting at the tip is what makes the kick read as a peg pushing it aside rather than
        /// as the whole marker sliding — the same reason the coaching hand pivots at its
        /// fingertip and not at its centre.
        /// </summary>
        void BuildPointer(RectTransform host, float diameter)
        {
            float size = diameter * PointerSize;

            var img = UIKit.Img("Pointer", host, Art.Pointer(128), Pal.Cream,
                                Vector2.one * size, new Vector2(.5f, .5f),
                                new Vector2(0f, _radius * .99f));

            _pointer = (RectTransform)img.transform;

            // The sprite's tip is at the bottom of its own box, so the pivot goes there and the
            // anchored position above is the tip's own resting place.
            _pointer.pivot = new Vector2(.5f, .12f);

            UIKit.Img("PointerRim", _pointer, Art.Pointer(128), new Color(.10f, .07f, .05f, .55f),
                      Vector2.one * (size * 1.14f), new Vector2(.5f, .5f), new Vector2(0f, size * .02f))
                 .transform.SetAsFirstSibling();
        }

        // -------------------------------------------------------------- turning
        /// <summary>
        /// Winds up, spins, and reports where it stopped.
        ///
        /// <para>
        /// <b>The wind-up returns to exactly zero before the spin begins</b>, which looks like
        /// one movement and is deliberately two. The alternative — starting the spin from the
        /// loaded angle — would put the whole travel nine degrees out, and the resting angle is
        /// not decoration here: it is which slice the server is about to pay for.
        /// </para>
        /// </summary>
        public void Spin(int index, Action onLanded)
        {
            if (Spinning || Landed || _wheel == null || !_wheel.IsUsable) return;

            Spinning = true;
            _pegs = 0;
            _kick = 0f;
            _lastTick = -1f;

            int count = _wheel.Count;

            Audio.Sfx("whoosh", .40f, .85f);

            Tween.Value(0f, WheelSpin.WindUpDegrees, WheelSpin.WindUpSeconds,
                        v => { if (_rotor) _rotor.localRotation = Quaternion.Euler(0f, 0f, v); },
                        Ease.OutQuad, gameObject)
                 .OnDone(() =>
                 {
                     if (this == null) return;

                     Tween.Value(WheelSpin.WindUpDegrees, 0f, WheelSpin.WindUpSeconds * .38f,
                                 v => { if (_rotor) _rotor.localRotation = Quaternion.Euler(0f, 0f, v); },
                                 Ease.InQuad, gameObject)
                          .OnDone(() => Release(count, index, onLanded));
                 });
        }

        void Release(int count, int index, Action onLanded)
        {
            if (this == null) return;

            Tween.Value(0f, 1f, WheelSpin.Seconds, t =>
            {
                if (this == null || _rotor == null) return;

                float angle = WheelSpin.AngleAt(count, index, t);
                _rotor.localRotation = Quaternion.Euler(0f, 0f, angle);

                Peg(WheelSpin.PegsPassed(count, angle));
            }, Ease.Linear, gameObject)
            .OnDone(() =>
            {
                if (this == null) return;

                // Set from the rule rather than left wherever the last frame put it. A tween's
                // final frame is not guaranteed to land on t == 1 exactly, and "nearly on the
                // slice" is the one thing this wheel may not be.
                if (_rotor) _rotor.localRotation = Quaternion.Euler(0f, 0f, WheelSpin.Rest(count, index));

                Spinning = false;
                Landed = true;
                _kick = 0f;
                if (_pointer) _pointer.localRotation = Quaternion.identity;

                onLanded?.Invoke();
            });
        }

        /// <summary>
        /// Kicks the pointer and clicks, once per peg that has gone past.
        ///
        /// <para>
        /// The count comes from <see cref="WheelSpin.PegsPassed"/> rather than from anything
        /// counted here, so the clicks and the slice edges are the same arithmetic. Two copies
        /// would be a wheel whose ticks drift out of step with its own rim, which is the sort of
        /// fault nobody can name and everybody notices.
        /// </para>
        /// <para>
        /// The sound is rate-limited and the kick is not. Early in a spin the pegs go past
        /// faster than a click can be heard as a click, and forty overlapping ones read as
        /// static; the pointer, being one object at one angle, is perfectly happy to be pushed
        /// sixty times a second and is what actually sells the speed.
        /// </para>
        /// </summary>
        void Peg(int passed)
        {
            if (passed <= _pegs) return;

            _pegs = passed;
            _kick = 1f;

            float now = Time.unscaledTime;
            if (now - _lastTick < MinTickGap) return;

            _lastTick = now;

            // Rising as the wheel slows, which is what turns a clatter into a countdown. Read
            // off the kick's own rate rather than off a clock, so it stays right whatever the
            // curve is retuned to.
            float pitch = 1f + Mathf.Clamp01(_pegs / 44f) * .35f;
            Audio.Sfx("tick", .34f, pitch);
        }

        /// <summary>
        /// The shortest gap between two clicks. Below this they overlap into static rather than
        /// reading as separate pegs, and the sample is about this long.
        /// </summary>
        const float MinTickGap = .045f;

        void Update()
        {
            if (_kick <= 0f || _pointer == null) return;

            _kick -= Time.unscaledDeltaTime / WheelSpin.TickSeconds;
            if (_kick < 0f) _kick = 0f;

            _pointer.localRotation = Quaternion.Euler(0f, 0f, PointerKick * _kick);
        }

        // ------------------------------------------------------------ the payoff
        /// <summary>
        /// Lights the slice that won and dims the rest, so the answer is unmistakable on a
        /// still frame — which is what a player takes a screenshot of.
        /// </summary>
        public void Celebrate(int index)
        {
            if (_wheel == null || _faces == null) return;

            var tint = WheelPaint.For(_wheel, index);

            for (int i = 0; i < _faces.Length; i++)
            {
                if (_faces[i] == null) continue;

                if (i == index)
                {
                    Tween.Tint(_faces[i], Pal.A(tint, .95f), .30f);
                    continue;
                }

                var dulled = WheelPaint.Seat(WheelPaint.For(_wheel, i), i);
                Tween.Tint(_faces[i], new Color(dulled.r, dulled.g, dulled.b, .40f), .30f);
            }

            if (_hubGlow) Tween.Tint(_hubGlow, Pal.A(tint, .55f), .3f);

            var won = Won(index);
            if (won != null)
            {
                Tween.Punch(won, .10f, .45f);
                Tween.Breathe(won, .022f, 1.6f);
            }

            if (_pointer) Tween.Punch(_pointer, .22f, .40f);
        }

        /// <summary>
        /// The winning wedge, for whoever wants to throw sparks out of it or fly a reward from
        /// it. Null when the wheel has not been built, which a caller must handle — a payout
        /// that depends on an animation being able to run is a payout that can be lost.
        /// </summary>
        public RectTransform Won(int index)
        {
            if (_wedges == null || index < 0 || index >= _wedges.Length) return null;
            return _wedges[index];
        }
    }
}
