using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Pairs: a hand of face-down cards, each hiding one of twenty-four stones. Turn two; the
    /// same stone twice stays up and fires the turret of its colour.
    ///
    /// <para>
    /// <b>A card is a card</b> (the owner's own picture of this genre, <c>challenge_pairs</c>): a
    /// stone frame, a royal-blue back with a crown, a cream face with the gem standing on it
    /// (<c>Tools/make_pairs_art.py</c>). The first cut drew a navy square that blinked into a
    /// gem and back, and was rejected as primitive - "no animation, nothing" (2026-09-27).
    /// </para>
    /// <para>
    /// <b>Every beat is a gesture the player can follow</b>, in the order the rules resolved
    /// it (the moving-board rules, <c>CRAFT.md</c>): the hand is <em>dealt</em> out of one pile
    /// onto its cells; a card <em>turns</em> - its width closing to an edge and opening on the
    /// other side, lifted off the table with its shadow dropping away - rather than swapping
    /// pictures; a card waiting for its partner breathes a halo; a pair is joined by a thread of
    /// light, rings out in its colour and throws two motes at the post it fed
    /// (<see cref="PuzzleView.FlyFeed"/>), and a pair made straight after another says
    /// <c>COMBO</c> on a rising note; a miss is held up to be read, shaken, and turned back; a
    /// cursed stone shudders the board and says so. At rest, a glint crosses the unturned
    /// hand now and then, so a board waiting on a thought is still alive.
    /// </para>
    /// <para>
    /// <b>The game never waits on a look.</b> A miss is held long enough to be remembered and
    /// no longer than the player wants: the screen holds the next taps
    /// (<see cref="InputsHeld"/>) and asks the board to <see cref="Hurry"/>, which ends the
    /// look at <see cref="PeekLeast"/>. Every celebration is a tween the board does not wait
    /// for, except the flight of a feed, which the hill's bolt must follow.
    /// </para>
    /// <para>
    /// <b>A thing that turns and a thing that moves never share a transform</b>: the card's
    /// node carries its place, the deal, a punch, a shake and a wiggle; its <c>Turn</c> child
    /// carries the flip and the lift alone. A repaint writes the side a card shows and never its
    /// position, so it cannot land a card that is still being dealt.
    /// </para>
    /// </summary>
    public sealed class PairsView : PuzzleView
    {
        sealed class Card
        {
            public RectTransform Node;
            public RectTransform Turn;
            public Image Shadow, Back, Shine, Face, Glow, Gem, Rim, Halo;
        }

        /// <summary>A card's share of its cell, and its gem's.</summary>
        const float CardShare = .92f, GemShare = .56f;

        /// <summary>The beats: a flip, a deal, a feed's flight.</summary>
        const float FlipFor = .24f, DealFor = .36f, FeedFor = .30f;

        /// <summary>
        /// How long a miss is held up, and the least it is held when the player has already
        /// moved on. The first is a look; the second is enough to see which stone it was.
        /// </summary>
        const float MissHold = .72f, PeekLeast = .26f;

        /// <summary>How long a cursed stone is held up, and the least when hurried.</summary>
        const float CurseHold = .80f, CurseLeast = .40f;

        /// <summary>Seconds between the glints that cross an idle hand.</summary>
        const float ShimmerEvery = 6f;

        /// <summary>The cursed stone's light: a violet no turret fires, so it cannot be read as a colour.</summary>
        static readonly Color CurseTint = new Color(.62f, .32f, .96f, 1f);

        PairsPuzzle _pairs;
        Card[] _card;
        RectTransform _deck, _fx;
        bool _landed, _hurry, _moving;
        int _pressed = -1;
        float _lastMove;

        /// <summary>
        /// Cards pressed while the hand was still being dealt, played the moment it lands. A
        /// card in flight is still being scaled by the deal, and a flip or a punch on it would
        /// write the same value (the two-tweens-one-value rule, <c>CRAFT.md</c>); dropping the
        /// press instead would be a tap that did not register.
        /// </summary>
        readonly List<int> _early = new List<int>(2);

        public override bool Landed => _landed;

        /// <summary>Two: a quick player turns two cards while a pair is still landing.</summary>
        public override int InputsHeld => 2;

        // ------------------------------------------------------------------ building
        protected override void Build()
        {
            _pairs = (PairsPuzzle)Run.Puzzle;
            int n = Columns * Rows;

            Sockets(.94f);

            _deck = UIKit.Node("Deck", Field);
            _card = new Card[n];
            for (int i = 0; i < n; i++) _card[i] = MakeCard(i);

            // Above every card, so a ring, a thread or a word is never drawn under a neighbour.
            _fx = UIKit.Node("Fx", Field);

            Targets(cell =>
            {
                if (!_landed)
                {
                    if (_early.Count < 2 && !_early.Contains(cell)) _early.Add(cell);
                    return;
                }

                _pressed = cell;
                Send(ChallengeInput.Tap(cell));
            });

            StartCoroutine(Deal());
        }

        Card MakeCard(int i)
        {
            var size = Vector2.one * Cell * CardShare;
            int kind = _pairs.KindAt(i);
            var tint = TintOf(i);

            var c = new Card();
            c.Node = UIKit.Box("Card", _deck, size, new Vector2(.5f, .5f), CentreOf(i));

            c.Shadow = UIKit.Img("Shadow", c.Node, Art.Round(22), new Color(0f, .02f, .08f, .45f), size,
                                 new Vector2(.5f, .5f), new Vector2(0f, -Cell * .04f));
            c.Shadow.type = Image.Type.Sliced;

            c.Turn = UIKit.Box("Turn", c.Node, size, new Vector2(.5f, .5f), Vector2.zero);

            var back = ChallengeArt.CardBack();
            c.Back = UIKit.Img("Back", c.Turn, back != null ? back : Art.Round(22),
                               back != null ? Color.white : new Color(.15f, .38f, .86f, 1f), size);
            if (back == null) c.Back.type = Image.Type.Sliced;

            c.Shine = UIKit.Img("Shine", c.Back.transform, Art.Glint(96, 4), Pal.A(Pal.Cream, 0f), size * .62f);

            var face = ChallengeArt.CardFace();
            c.Face = UIKit.Img("Face", c.Turn, face != null ? face : Art.Round(22),
                               face != null ? Color.white : Pal.Cream, size);
            if (face == null) c.Face.type = Image.Type.Sliced;

            c.Glow = UIKit.Img("Glow", c.Turn, Art.Glow(128, 1.8f), Pal.A(tint, .5f), size * .98f);

            c.Gem = UIKit.Img("Gem", c.Turn, ChallengeArt.PairGem(kind), Color.white, Vector2.one * Cell * GemShare);
            c.Gem.preserveAspect = true;

            c.Rim = UIKit.Img("Rim", c.Turn, Art.RoundOutline(22, 6f), Pal.A(tint, 0f), size * 1.03f);

            c.Halo = UIKit.Img("Halo", c.Node, Art.RoundOutline(26, 5f), Pal.A(Pal.Cream, 0f), size * 1.12f);

            Side(c, false, i);
            return c;
        }

        /// <summary>The light a card is lit in: its turret's colour, or the curse's violet.</summary>
        Color TintOf(int cell)
        {
            int colour = _pairs.ColourAt(cell);
            return colour < 0 ? CurseTint : ChallengeArt.Tint(colour);
        }

        /// <summary>Draws one side of a card. The gem is drawn only once its picture has landed (7b).</summary>
        void Side(Card c, bool up, int cell)
        {
            c.Back.enabled = !up;
            c.Shine.enabled = !up;
            c.Face.enabled = up;
            c.Glow.enabled = up;
            c.Rim.enabled = up;

            if (up && c.Gem.sprite == null) c.Gem.sprite = ChallengeArt.PairGem(_pairs.KindAt(cell));
            c.Gem.enabled = up && c.Gem.sprite != null;
        }

        // ------------------------------------------------------------------ painting
        public override void Repaint()
        {
            for (int i = 0; i < _card.Length; i++) Paint(i);
        }

        /// <summary>
        /// One card put back exactly as the model says, with nothing borrowed: its side, its
        /// flip, its look and its halo. Never its position, which belongs to the deal.
        /// </summary>
        void Paint(int i)
        {
            var c = _card[i];
            var face = _pairs.FaceAt(i);
            var tint = TintOf(i);

            Tween.KillChannel(c.Turn, "flip");
            c.Turn.localScale = Vector3.one;
            c.Shadow.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .04f);

            Side(c, face != PairsPuzzle.Face.Hidden, i);

            bool matched = face == PairsPuzzle.Face.Matched;
            Tween.KillChannel(c.Face, "tint");
            Tween.KillChannel(c.Rim, "fade");
            c.Face.color = matched ? Settled(tint) : Color.white;
            c.Rim.color = Pal.A(tint, matched ? .95f : 0f);
            c.Glow.color = Pal.A(tint, matched ? .38f : .5f);

            Halo(i, i == _pairs.First);
        }

        /// <summary>A matched card's face: the cream taken a fifth of the way to its colour, so a claimed card reads as its turret's.</summary>
        static Color Settled(Color tint) => Color.Lerp(Color.white, tint, .22f);

        // ------------------------------------------------------------------ the deal
        /// <summary>
        /// The hand is dealt out of one pile in the middle of the board, card by card in
        /// reading order, each spinning straight as it lands. A stagger and a duration are one
        /// bound (<c>CRAFT.md</c>): the step shortens with the hand so a big board is dealt in
        /// the same second as a small one, and at most six of the cards are heard.
        /// </summary>
        IEnumerator Deal()
        {
            _landed = false;
            int n = _card.Length;
            float step = Mathf.Min(.045f, .95f / n);
            const float lead = .20f;
            int beat = Mathf.Max(1, n / 6);

            for (int i = 0; i < n; i++)
            {
                var node = _card[i].Node;
                float d = lead + i * step;

                node.anchoredPosition = new Vector2((i % 3 - 1) * Cell * .04f, (i % 2) * Cell * .03f);
                node.localRotation = Quaternion.Euler(0f, 0f, (i * 47 % 31) - 15f);
                node.localScale = Vector3.one * .62f;

                Tween.Move(node, CentreOf(i), DealFor, Ease.OutCubic).Delay(d);
                Tween.Rotate(node, 0f, DealFor, Ease.OutCubic).Delay(d);
                Tween.Scale(node, 1f, DealFor, Ease.OutBack).Delay(d);

                if (i % beat == 0) Audio.Sfx("tick", .26f, .92f + .04f * (i / beat), d);
            }

            yield return new WaitForSecondsRealtime(lead + n * step + DealFor);
            if (!this) yield break;

            _landed = true;
            _lastMove = Time.unscaledTime;
            Audio.Sfx("settle", .3f);
            Shimmer();

            foreach (int cell in _early)
            {
                _pressed = cell;
                Send(ChallengeInput.Tap(cell));
            }
            _early.Clear();

            while (this)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (!this) yield break;
                if (_moving || Time.unscaledTime - _lastMove < ShimmerEvery) continue;
                _lastMove = Time.unscaledTime;
                Shimmer();
            }
        }

        /// <summary>A glint crossing every unturned card, top left to bottom right.</summary>
        void Shimmer()
        {
            for (int i = 0; i < _card.Length; i++)
            {
                if (_pairs.FaceAt(i) != PairsPuzzle.Face.Hidden || i == _pairs.First) continue;

                var shine = _card[i].Shine;
                var rt = shine.rectTransform;
                float delay = (i % Columns + i / Columns) * .05f;

                Tween.Run(.46f, Ease.Linear, t =>
                {
                    if (!shine) return;
                    float k = Mathf.Sin(t * Mathf.PI);
                    shine.color = Pal.A(Pal.Cream, .75f * k);
                    rt.localScale = Vector3.one * (.55f + .6f * k);
                    rt.localRotation = Quaternion.Euler(0f, 0f, 45f * t);
                }, shine, "shine").Delay(delay).OnAbandon(() => { if (shine) shine.color = Pal.A(Pal.Cream, 0f); });
            }
        }

        // ------------------------------------------------------------------ gestures
        /// <summary>
        /// A card turns over: its width closes to an edge and opens on the other side, lifted
        /// off the table while it turns, the shadow dropping away beneath it. The side is
        /// swapped at the edge, which is the one frame nobody can see it.
        /// </summary>
        void Flip(int i, bool up, float delay = 0f)
        {
            var c = _card[i];
            var turn = c.Turn;
            var shadow = c.Shadow.rectTransform;
            bool swapped = false;
            float rest = -Cell * .04f;

            c.Node.SetAsLastSibling();

            Tween.Run(FlipFor, Ease.InOutSine, t =>
            {
                if (!turn) return;
                float lift = Mathf.Sin(t * Mathf.PI);
                float width = Mathf.Max(.02f, Mathf.Abs(Mathf.Cos(t * Mathf.PI)));
                turn.localScale = new Vector3(width * (1f + .10f * lift), 1f + .10f * lift, 1f);
                shadow.anchoredPosition = new Vector2(Cell * .03f * lift, rest - Cell * .08f * lift);
                if (!swapped && t >= .5f)
                {
                    swapped = true;
                    Side(c, up, i);
                }
            }, turn, "flip").Delay(delay).OnDone(() =>
            {
                if (!turn) return;
                turn.localScale = Vector3.one;
                shadow.anchoredPosition = new Vector2(0f, rest);
                if (!swapped) Side(c, up, i);
                if (up) Tween.Punch(c.Gem.transform, .16f, .26f);
            }).OnAbandon(() =>
            {
                if (turn) turn.localScale = Vector3.one;
                if (shadow) shadow.anchoredPosition = new Vector2(0f, rest);
            });

            Audio.Sfx(up ? "tock" : "tick", up ? .34f : .22f, up ? 1.05f : .9f, delay);
        }

        /// <summary>The halo round a card that is waiting for its partner: breathing while it waits.</summary>
        void Halo(int i, bool on)
        {
            if (i < 0 || i >= _card.Length) return;
            var halo = _card[i].Halo;
            Tween.KillChannel(halo, "halo");

            if (!on)
            {
                halo.color = Pal.A(Pal.Cream, 0f);
                return;
            }

            Tween.Run(.9f, Ease.InOutSine, t =>
            {
                if (halo) halo.color = Pal.A(Pal.Cream, .30f + .55f * t);
            }, halo, "halo").Loop(-1, true);
        }

        /// <summary>A card shaken on its own axis: "no".</summary>
        void Wiggle(int i, float degrees)
        {
            var node = _card[i].Node;
            Tween.Run(.38f, Ease.Linear, t =>
            {
                if (node) node.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 4f) * degrees * (1f - t));
            }, node, "wiggle").OnDone(() => { if (node) node.localRotation = Quaternion.identity; })
                              .OnAbandon(() => { if (node) node.localRotation = Quaternion.identity; });
        }

        /// <summary>A ring thrown off a card in a colour.</summary>
        void Ring(int cell, Color tint, float size = .8f, float to = 1.9f)
        {
            var ring = UIKit.Img("Ring", _fx, Art.Ring(128, 9f), Pal.A(tint, .95f), Vector2.one * Cell * size,
                                 new Vector2(.5f, .5f), CentreOf(cell));
            var rt = ring.rectTransform;
            Tween.Scale(rt, to, .36f, Ease.OutCubic);
            Tween.Fade(ring, 0f, .36f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });
        }

        /// <summary>A thread of light joining a pair, drawn out from the middle and let go.</summary>
        void Thread(int a, int b, Color tint)
        {
            var from = CentreOf(a);
            var to = CentreOf(b);
            var dir = to - from;
            float length = dir.magnitude;
            if (length < 1f) return;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            var beam = UIKit.Img("Thread", _fx, Art.SoftCapsule(40, 120), Pal.A(Pal.Lift(tint, .35f), .95f),
                                 new Vector2(Cell * .26f, length + Cell * .3f), new Vector2(.5f, .5f), (from + to) * .5f);
            var rt = beam.rectTransform;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            rt.localScale = new Vector3(1f, 0f, 1f);

            Tween.Run(.18f, Ease.OutCubic, t => { if (rt) rt.localScale = new Vector3(1f, t, 1f); }, rt);
            Tween.Fade(beam, 0f, .30f, Ease.InQuad).Delay(.16f).OnDone(() => { if (rt) Destroy(rt.gameObject); });
        }

        /// <summary>A word that pops over a card, rises and goes - the combo, the curse.</summary>
        void Say(int cell, string text, Color colour, float size)
        {
            var at = CentreOf(cell) + new Vector2(0f, Cell * .42f);
            var word = UIKit.Titled("Say", _fx, text, Mathf.RoundToInt(Cell * size), colour, TextAnchor.MiddleCenter,
                                    new Vector2(Cell * 3.2f, Cell * .7f), new Vector2(.5f, .5f), at, 3f, 3f);
            var rt = word.rectTransform;
            rt.localScale = Vector3.zero;

            Tween.Pop(rt, 0f, .30f);
            Tween.Move(rt, at + new Vector2(0f, Cell * .55f), .85f, Ease.OutCubic);
            Tween.Fade(word, 0f, .30f, Ease.InQuad).Delay(.58f).OnDone(() => { if (rt) Destroy(rt.gameObject); });
        }

        /// <summary>A card taking the look of a claimed pair: its face warmed toward its colour, its rim lit.</summary>
        void Claim(int cell)
        {
            var c = _card[cell];
            var tint = TintOf(cell);
            c.Rim.enabled = true;
            Tween.Tint(c.Face, Settled(tint), .34f);
            Tween.Fade(c.Rim, .95f, .34f);
            Tween.Run(.34f, Ease.OutQuad, t => { if (c.Glow) c.Glow.color = Pal.A(tint, Mathf.Lerp(.9f, .38f, t)); }, c.Glow);
        }

        // ------------------------------------------------------------------ a move landing
        public override void Hurry() => _hurry = true;

        /// <summary>Waits up to <paramref name="most"/>, or <paramref name="least"/> once the player has moved on.</summary>
        IEnumerator Hold(float most, float least)
        {
            float start = Time.unscaledTime;
            while (this)
            {
                float held = Time.unscaledTime - start;
                if (held >= most || (_hurry && held >= least)) yield break;
                yield return null;
            }
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            _hurry = false;
            _moving = true;
            _lastMove = Time.unscaledTime;

            if (!move.Turn)
            {
                // The first flip: turned, and left breathing until its partner is chosen. The
                // board does not wait for it - the second tap may land mid-turn.
                int first = _pairs.First;
                if (first >= 0)
                {
                    Flip(first, true);
                    Halo(first, true);
                }
                _moving = false;
                yield break;
            }

            if (_pairs.LastCursed) yield return Cursed(_pairs.LastA, _pairs.LastB);
            else if (_pairs.LastMatched) yield return Matched(_pairs.LastA, _pairs.LastB);
            else yield return Missed(_pairs.LastA, _pairs.LastB);

            _moving = false;
            _lastMove = Time.unscaledTime;
        }

        IEnumerator Matched(int a, int b)
        {
            Halo(a, false);
            Flip(b, true);
            yield return new WaitForSecondsRealtime(FlipFor);
            if (!this) yield break;

            int colour = _pairs.ColourAt(b);
            var tint = TintOf(b);
            int combo = _pairs.LastCombo;

            // The pair rings out: both cards jump, a thread of light joins them, and each throws
            // a ring and sparks in the colour it will feed. The note climbs with the combo.
            Tween.Punch(_card[a].Node, .22f, .34f);
            Tween.Punch(_card[b].Node, .22f, .34f);
            Thread(a, b, tint);
            Ring(a, tint);
            Ring(b, tint);
            Burst.Sparks(_fx, CentreOf(a), tint, 7, Cell * 1.1f, Cell * .1f, .4f);
            Burst.Sparks(_fx, CentreOf(b), tint, 7, Cell * 1.1f, Cell * .1f, .4f);
            Audio.Sfx("chime", .55f, 1f + .12f * (combo - 1));

            if (combo >= 2)
            {
                // A second ring chasing the first reads as *more*, which a combo is.
                Ring(b, Pal.Gold, .9f, 2.4f);
                Say(b, Loc.Format("ui.challenges.combo", combo), combo >= PairsPuzzle.ComboCap ? Pal.Gold : Pal.Cream, .36f);
                Audio.Sfx("chime2", .42f, 1f + .08f * combo, .07f);
            }

            Claim(a);
            Claim(b);

            yield return new WaitForSecondsRealtime(.10f);
            if (!this) yield break;

            // The feed: a mote from each card to the post of its colour, waited on, so the
            // bolt it bought leaves the post after it has arrived.
            float flight = FlyFeed(a, colour, FeedFor);
            yield return new WaitForSecondsRealtime(.05f);
            if (!this) yield break;
            flight = Mathf.Max(flight - .05f, FlyFeed(b, colour, FeedFor));
            if (flight > 0f) yield return new WaitForSecondsRealtime(flight + .04f);
            if (!this) yield break;

            if (_pairs.Solved) yield return Finale();
        }

        IEnumerator Missed(int a, int b)
        {
            Halo(a, false);
            Flip(b, true);
            yield return new WaitForSecondsRealtime(FlipFor);
            if (!this) yield break;

            // Held up to be read; halfway through the look both cards shake their heads. A
            // player who has already tapped on ends the look early (Hurry).
            float start = Time.unscaledTime;
            yield return Hold(MissHold * .4f, PeekLeast);
            if (!this) yield break;

            Wiggle(a, 5f);
            Wiggle(b, 5f);
            Audio.Sfx("blocked", .28f);

            float spent = Time.unscaledTime - start;
            yield return Hold(Mathf.Max(0f, MissHold - spent), Mathf.Max(0f, PeekLeast - spent));
            if (!this) yield break;

            // Turned back together, and not waited on: the model already has them down, so the
            // next card may be turned while these two are still closing.
            Flip(a, false);
            Flip(b, false, .03f);
        }

        IEnumerator Cursed(int first, int cursed)
        {
            Halo(first, false);
            Flip(cursed, true);
            yield return new WaitForSecondsRealtime(FlipFor);
            if (!this) yield break;

            // The board flinches: a violet burst off the stone, the card shudders, the plate
            // shakes, and the word says what happened. The hill's extra step follows in the
            // replay, which is the cost made visible.
            var at = CentreOf(cursed);
            var cloud = UIKit.Img("Curse", _fx, Art.Glow(128, 1.6f), Pal.A(CurseTint, .85f), Vector2.one * Cell * 1.1f,
                                  new Vector2(.5f, .5f), at);
            var rt = cloud.rectTransform;
            Tween.Scale(rt, 2.6f, .6f, Ease.OutCubic);
            Tween.Fade(cloud, 0f, .6f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });
            Ring(cursed, CurseTint, .9f, 2.3f);
            Burst.Sparks(_fx, at, CurseTint, 12, Cell * 1.5f, Cell * .12f, .55f);
            Wiggle(cursed, 10f);
            Tween.Shake(Field, Cell * .07f, .38f);
            Say(cursed, Loc.Get("ui.challenges.cursed"), Pal.Lift(CurseTint, .45f), .34f);
            Audio.Sfx("boom", .38f, .72f);
            Audio.Sfx("zap", .22f, .6f, .05f);

            yield return Hold(CurseHold, CurseLeast);
            if (!this) yield break;

            Flip(cursed, false);
            if (first >= 0) Flip(first, false, .03f);
        }

        /// <summary>
        /// The last pair made: a wave of light across the whole hand, corner to corner, every
        /// card jumping as it passes. Short, because the screen's curtain follows it.
        /// </summary>
        IEnumerator Finale()
        {
            float last = 0f;
            for (int i = 0; i < _card.Length; i++)
            {
                if (_pairs.FaceAt(i) != PairsPuzzle.Face.Matched) continue;

                int cell = i;
                float delay = (i % Columns + i / Columns) * .05f;
                last = Mathf.Max(last, delay);
                var tint = TintOf(cell);

                Tween.After(delay, () =>
                {
                    if (!this) return;
                    Tween.Punch(_card[cell].Node, .2f, .3f);
                    Ring(cell, tint, .7f, 1.6f);
                }, this);
            }

            Audio.Sfx("lit", .45f);
            yield return new WaitForSecondsRealtime(last + .34f);
        }

        public override void Refuse()
        {
            if (_landed && _pressed >= 0 && _pressed < _card.Length)
            {
                Tween.Shake(_card[_pressed].Node, Cell * .05f, .22f);
                Audio.Sfx("blocked", .3f);
                return;
            }

            base.Refuse();
        }

        // ------------------------------------------------------------------ the lessons
        public override void Lessons(List<ScreenLesson> into)
            => ScreenLessons.Offer(into, Mechanic.PairsFlip, Field);

        public override void LessonsAfter(ChallengeMove move, List<ScreenLesson> into)
        {
            if (move == null || !move.Turn) return;

            int b = _pairs.LastB;
            if (b < 0 || b >= _card.Length) return;

            // Each at the event: a ring round the card that just did the thing.
            if (_pairs.LastCursed) ScreenLessons.Offer(into, Mechanic.PairsCurse, _card[b].Node);
            else if (_pairs.LastMatched && _pairs.LastCombo == 2) ScreenLessons.Offer(into, Mechanic.PairsCombo, _card[b].Node);
        }

        public override void Review(List<ScreenLesson> into)
        {
            ScreenLessons.Add(into, Mechanic.PairsFlip, Field);
            ScreenLessons.Add(into, Mechanic.PairsCombo, Field);
            if (_pairs.Curses > 0) ScreenLessons.Add(into, Mechanic.PairsCurse, Field);
        }
    }
}
