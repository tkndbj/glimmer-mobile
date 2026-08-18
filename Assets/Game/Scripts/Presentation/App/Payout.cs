using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A reward chip — a glyph, a number, and the flight of tokens that puts the number
    /// there.
    ///
    /// <para>
    /// <b>Why this exists rather than another rolling label.</b> <see cref="Roll"/> already
    /// makes a number climb instead of appearing, and that is most of the effect. What it
    /// cannot do is give the climb a <em>cause</em>. A number that rises on its own is the
    /// game telling the player what they won; a number that rises because seven coins just
    /// flew out of the stars they earned and landed on it is the player watching the
    /// transaction happen. The information is identical and the two do not feel remotely
    /// alike — each landing is a discrete little event with its own bump, click and rising
    /// pitch, and seven small arrivals land harder than one smooth curve.
    /// </para>
    /// <para>
    /// So the tokens are not decoration over a roll: they <em>drive</em> it. The number
    /// only ever moves in <see cref="Land"/>, one step per token, which is what guarantees
    /// the thing on screen and the thing being counted stay the same thing. A roll running
    /// on its own clock beside a particle effect drifts out of sync on a slow frame and
    /// reads as two unrelated animations.
    /// </para>
    /// <para>
    /// The token budget is fixed rather than one per unit, for the reason
    /// <see cref="Roll.Ticks"/> gives: a hundred and fifty coins at one coin each is not a
    /// flourish, it is a swarm, and the celebration would change shape every time the
    /// economy was retuned. The one exception is a prize smaller than the budget — three
    /// coins throw three coins, because throwing seven and landing them in fractions is the
    /// one case where the count on screen and the count in the air visibly disagree.
    /// </para>
    /// <para>
    /// No plate, no pill, no frame: the glyph and the number carry their own contrast, the
    /// same way every other readout in this UI does.
    /// </para>
    /// </summary>
    public sealed class Payout
    {
        /// <summary>How many tokens a payout throws, however much it is worth.</summary>
        public const int Tokens = 7;

        /// <summary>The chip's box. Punched when the last token lands.</summary>
        public RectTransform Root { get; private set; }

        /// <summary>
        /// The icon the tokens fly into, drawn exactly as it was handed over.
        ///
        /// Public because the glyph is the caller's to finish, and this class has no
        /// business knowing what a reward is: credits have no single sprite — they are the
        /// spinning coin flipbook, attached through <c>RewardArt.Glyph</c> — and a generated
        /// shape has no colour of its own and must be tinted or it draws white.
        /// </summary>
        public Image Glyph { get; private set; }

        /// <summary>The number, formatted by whoever built the chip.</summary>
        public Text Number { get; private set; }

        Image _glow;
        CanvasGroup _group;
        Color _tint, _tokenTint;
        Sprite _token;
        float _tokenSize;
        string _sfx;
        Func<long, string> _format;
        long _total;
        int _throwing, _landed;

        /// <summary>Where the glyph sits inside the chip. Everything else hangs off it.</summary>
        const float GlyphX = -94f;

        /// <summary>
        /// The rhythm, and it is the design.
        ///
        /// <para>
        /// <see cref="LeadIn"/> is the beat the chip has to itself before anything is thrown,
        /// so the tokens arrive somewhere that already exists — a chip that pops in carrying
        /// its first coin reads as one event instead of a container being filled. It also
        /// keeps the first landing clear of the glyph's entrance, which owns the same
        /// localScale that <see cref="Land"/> punches.
        /// </para>
        /// <para>
        /// <see cref="LandingGap"/> is the one to be careful with. Spread thin the payout
        /// drags, and packed tight the landings stop being separable — the number appears to
        /// jump and the rising run of ticks turns into a rattle. About nine a second is where
        /// each one still registers as its own small event.
        /// </para>
        /// </summary>
        const float LeadIn = .18f, Flight = .44f, LandingGap = .11f;

        /// <summary>
        /// Builds the chip at rest: glyph dim and small, number showing nothing yet.
        /// Nothing animates until <see cref="Play"/>.
        /// </summary>
        /// <param name="glyph">May be null for a caller that attaches a flipbook after.</param>
        /// <param name="tint">The reward's own colour — the number, the glow and the sparks.</param>
        /// <param name="token">What flies. One sprite, reused; these are transient.</param>
        /// <param name="tokenTint">
        /// White for art that is already coloured (a coin), the reward's colour for a
        /// procedural shape (a spark). Separate from <paramref name="tint"/> because tinting
        /// a gold coin gold is how a coin stops looking like a coin.
        /// </param>
        public static Payout Chip(string name, Transform parent, Vector2 anchor, Vector2 pos,
                                  Sprite glyph, Color tint, Func<long, string> format, long total,
                                  Sprite token, Color tokenTint, string sfx = "tick",
                                  float glyphSize = 88f, float tokenSize = 44f)
        {
            var p = new Payout
            {
                _tint = tint,
                _tokenTint = tokenTint,
                _token = token,
                _tokenSize = tokenSize,
                _sfx = sfx,
                _format = format,
                _total = total < 0 ? 0 : total,
            };

            p.Root = UIKit.Box(name, parent, new Vector2(368f, 112f), anchor, pos);

            p._glow = UIKit.Img("Glow", p.Root, Art.Glow(128, 2.1f), Pal.A(tint, 0f),
                                Vector2.one * glyphSize * 2.2f, new Vector2(.5f, .5f),
                                new Vector2(GlyphX, 0f));

            p.Glyph = UIKit.Img("Glyph", p.Root, glyph, Color.white,
                                Vector2.one * glyphSize, new Vector2(.5f, .5f), new Vector2(GlyphX, 0f));
            p.Glyph.preserveAspect = true;
            p.Glyph.transform.localScale = Vector3.one * .55f;

            // Not Shrinkable, deliberately. Best-fit re-measures on every text change, so a
            // number climbing from 0 to 1,240 would resize itself seven times on the way —
            // and the box is sized for the digits rather than for a translated sentence, so
            // there is nothing here for it to save.
            p.Number = UIKit.Titled("N", p.Root, format != null ? format(0) : "0", 54, tint,
                                    TextAnchor.MiddleCenter, new Vector2(224f, 72f),
                                    new Vector2(.5f, .5f), new Vector2(58f, 0f), 4f, 4f);

            // The unlit look is a group alpha rather than a tint on each piece, because the
            // glyph's colour is not ours to touch: a caller finishing it through
            // RewardArt.Glyph may have fallen back to a tinted disc, and brightening that
            // "back" to white would turn a gold coin into a white one.
            p._group = UIKit.Group(p.Root);
            p._group.alpha = .5f;

            return p;
        }

        /// <summary>
        /// Throws the payout: tokens leave <paramref name="origin"/>, arc into the glyph,
        /// and the number climbs one landing at a time.
        ///
        /// <para>
        /// <paramref name="origin"/> is the whole reason a chip reads as earnings rather
        /// than as a notification. On the victory panel it is the star row, so what the
        /// player sees is the stars they just landed turning into what those stars were
        /// worth — a claim the game is actually making, since the reward is derived from
        /// exactly those stars.
        /// </para>
        /// </summary>
        /// <summary>
        /// How many tokens this chip will throw: the budget, or the prize itself when the
        /// prize is smaller. Three coins throw three coins — throwing seven and landing them
        /// in fractions is the one case where the count on screen and the count in the air
        /// visibly disagree.
        /// </summary>
        public int Count => (int)Mathf.Clamp(_total, 1, Tokens);

        /// <summary>
        /// Exactly how long <see cref="Play"/> takes, so a cue can hold for it rather than
        /// for a number somebody guessed and then had to keep in step.
        /// </summary>
        public float Duration => LeadIn + Flight + (Count - 1) * LandingGap;

        public void Play(Transform origin)
        {
            if (Root == null || Glyph == null) return;

            var space = Root.parent as RectTransform;
            if (space == null || origin == null) { Settle(); return; }

            // Wakes up first — see LeadIn.
            Glyph.transform.localScale = Vector3.one * .55f;
            Tween.Pop(Glyph.transform, .55f, .34f);
            Tween.Fade(_group, 1f, .3f);
            Tween.Run(.34f, Ease.OutQuad, t => { if (_glow) _glow.color = Pal.A(_tint, .42f * t); }, _glow);

            _throwing = Count;
            _landed = 0;

            Vector2 from = TokenFlight.LocalIn(space, origin);
            Vector2 to = TokenFlight.LocalIn(space, Glyph.transform);

            for (int i = 0; i < _throwing; i++) Throw(space, from, to, LeadIn + i * LandingGap, Flight, i);
        }

        // --------------------------------------------------------------- internals
        void Throw(RectTransform space, Vector2 from, Vector2 to, float delay, float flight, int index)
            => TokenFlight.Throw(space, from, to, _token, _tokenTint, _tokenSize,
                                 index, delay, flight, Land);

        /// <summary>
        /// One token has arrived. This is the only place the number moves.
        ///
        /// The last landing writes <see cref="_total"/> itself rather than the interpolation,
        /// so a rounding error can never leave a reward reading one short of what was banked
        /// — the same guarantee <see cref="Roll.Number"/> makes.
        /// </summary>
        void Land()
        {
            _landed++;
            bool last = _landed >= _throwing;

            if (Number)
            {
                long shown = last ? _total
                                  : (long)Mathf.Round(_total * (_landed / (float)_throwing));

                Number.text = _format != null ? _format(shown) : shown.ToString();

                // Reset before punching. Punch shares a channel, and a punch cancelled
                // mid-swing leaves the scale where it stopped — seven of those in a row and
                // the number visibly drifts out of size.
                Number.transform.localScale = Vector3.one;
                Tween.Punch(Number.transform, last ? .3f : .13f, last ? .42f : .22f);
            }

            Glyph.transform.localScale = Vector3.one;
            Tween.Punch(Glyph.transform, last ? .32f : .16f, last ? .44f : .24f);

            // Driven off the landing index rather than a timer, so the ear hears the same
            // rhythm the eye sees however long the flight took on the day.
            float k = _throwing <= 1 ? 1f : (_landed - 1) / (float)(_throwing - 1);
            Audio.Sfx(_sfx, .34f, Mathf.Lerp(.94f, 1.56f, k));

            Ping(last);

            if (!last) return;

            Settle();
            Burst.Sparks(Glyph.transform, Vector2.zero, _tint, 14, 210f, 20f, .5f);

            // No haptic here, and it is a decision rather than an omission. A chip used to buzz
            // once as its last token landed, which on a two-chip payout is two buzzes inside a
            // second — and Handheld.Vibrate is one fixed-length buzz on Android, so there is no
            // way to make the second one lighter than the first. The victory panel is the only
            // caller, and it is a screen the player sees dozens of times a session; a buzz that
            // arrives that often stops being punctuation and becomes a tic.
        }

        /// <summary>Expanding ring at the point of arrival — the impact, not the prize.</summary>
        void Ping(bool strong)
        {
            if (Glyph == null || Root == null) return;

            var ring = UIKit.Img("Ping", Root, Art.Ring(128, 9f),
                                 Pal.A(Pal.Lift(_tint, .4f), strong ? .85f : .5f),
                                 Vector2.one * (strong ? 118f : 86f), new Vector2(.5f, .5f),
                                 new Vector2(GlyphX, 0f));

            var rt = (RectTransform)ring.transform;
            float to = strong ? 2.3f : 1.7f;

            // The starting alpha is captured, not multiplied down each frame: scaling the
            // live colour compounds, so the fade would depend on how many frames it got.
            float alpha = ring.color.a;

            Tween.Run(strong ? .46f : .32f, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.45f, to, t);
                var c = ring.color; c.a = alpha * (1f - t); ring.color = c;
            }, ring).OnDone(() => { if (ring) UnityEngine.Object.Destroy(ring.gameObject); });
        }

        /// <summary>The chip's resting state once it has been paid.</summary>
        void Settle()
        {
            if (Number) Number.text = _format != null ? _format(_total) : _total.ToString();
            if (_group) _group.alpha = 1f;
            if (Root) { Root.localScale = Vector3.one; Tween.Punch(Root, .1f, .3f); }
            if (_glow) Tween.Run(.5f, Ease.OutQuad,
                                 t => { if (_glow) _glow.color = Pal.A(_tint, Mathf.Lerp(.8f, .38f, t)); }, _glow);
        }

    }
}
