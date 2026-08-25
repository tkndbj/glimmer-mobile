using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Lightweave's grove: press a crystal, drag a channel of light to the critter that wants its
    /// colour, and do it for every pair without any two channels crossing.
    ///
    /// <para>
    /// <b>Only a finished channel is kept.</b> A drag let go anywhere but on its own critter
    /// leaves nothing behind — because a half-drawn channel would have to either hold ground
    /// (blocking a route the player never meant to block) or not hold it (a line that lies about
    /// what is free). Neither is worth the state, and "it counts when it lands" is one sentence.
    /// </para>
    /// <para>
    /// Dragging back over the previous cell rubs that step out; a tap on a finished channel takes
    /// the whole thing back. Both matter more than they look: on a grid this size a finger is
    /// wrong constantly, and a puzzle that punishes the correction rather than the mistake is one
    /// people put down.
    /// </para>
    ///
    /// <para>
    /// <b>The ink is built once and edited, never rebuilt.</b> This used to destroy every segment
    /// of every channel and make them all again on each repaint — and a repaint is what a drag
    /// <em>is</em>, so a finger crossing a full grove was destroying and reallocating well over a
    /// hundred <c>Image</c>s per input event. A drag step now appends exactly one link and one
    /// knuckle, and rubbing one out removes exactly those two. That is <c>GridView</c>'s bargain
    /// in the one place on the board where it was not being kept, and it is a correctness rule as
    /// much as a cost one: an object destroyed and remade cannot carry an animation, so nothing
    /// here could have been made to move until this was fixed.
    /// </para>
    /// <para>
    /// <b>Drawn in four layers</b> — ground, ink, ends, effects — so what is in front of what is a
    /// property of where a thing is built rather than of the order it happened to be built in.
    /// Every one of these used to be a sibling of every other with the ink shuffled to index 1 by
    /// hand, which is <c>GroveFieldView</c>'s insertion bug waiting to happen.
    /// </para>
    /// <para>
    /// The timing of all of it — how fast light runs a channel, what note a critter rings, how the
    /// closing cascade paces itself — is <see cref="WeaveTempo"/>, in Domain, because none of it
    /// can be seen to be wrong in a screenshot.
    /// </para>
    /// </summary>
    public sealed class WeaveView : MonoBehaviour, IPointerDownHandler, IDragHandler,
                                    IPointerUpHandler
    {
        /// <summary>Raised whenever the board moves, so the screen can repaint its readouts.</summary>
        public Action Changed;

        /// <summary>Every pair joined — raised after the closing cascade, not before it.</summary>
        public Action Solved;

        /// <summary>The first channel of the run has landed — the moment it is owed for.</summary>
        public Action Committed;

        /// <summary>
        /// The last channel has landed and the closing cascade has begun — raised about a second
        /// and a half before <see cref="Solved"/>.
        ///
        /// <para>
        /// It exists because that second and a half is a window in which the run is decided and
        /// the screen does not know it yet. The board is latched, but the header's back and
        /// restart buttons are not part of the board: a restart tapped during the cascade would
        /// clear the grove and then be handed a win for it, and a forfeit would charge a heart
        /// for a run that was already won. The safe outcome has to be what every exit does, so
        /// the screen is told when the run stops being live rather than each exit being asked to
        /// remember.
        /// </para>
        /// </summary>
        public Action Finishing;

        WeaveRun _run;
        RectTransform _host, _grid;
        RectTransform _groundLayer, _inkLayer, _beadLayer, _endLayer, _fxLayer;

        Image[] _ground;
        Image _wash, _vignette;

        RectTransform[] _channel;          // one ink container per pair
        List<Image>[] _parts;              // its segments, link and knuckle interleaved
        RectTransform[] _crystal, _critter;
        Image[] _body, _want, _halo;

        RectTransform[] _bead;             // one per bead, in the order the layout lists them
        Image[] _beadRing, _beadGlow;
        readonly List<int> _waiting = new List<int>();

        /// <summary>
        /// Whether each pair's light has actually finished travelling.
        ///
        /// <para>
        /// Not the same question as <c>WeaveRun.IsJoined</c>, and the difference is a whole
        /// second of screen time. A channel is joined the instant the finger lifts; its light
        /// then runs the length of it, and the critter wakes when the light <em>arrives</em>,
        /// because cause is what the player is being paid for. A bead lights on the same
        /// schedule for the same reason — read straight off the model instead, every bead on a
        /// channel would flash before the light had left the crystal.
        /// </para>
        /// </summary>
        bool[] _arrived;

        RectTransform _liveInk;
        readonly List<Image> _liveParts = new List<Image>();
        readonly List<int> _drawing = new List<int>();

        int _pair = -1;
        bool _anyDrawn, _closing;
        int _blockedAt = -1;
        float _blockedWhen = -99f, _urgency;
        bool _pressed, _nagging;
        float _cell, _size;
        Vector2 _origin;

        /// <summary>Refuses input while the run is over or a panel is up.</summary>
        public bool Locked { get; set; }

        public WeaveRun Run => _run;

        /// <summary>
        /// How hard the clock is pressing, 0..1. Pushed by the screen every frame rather than
        /// read from a clock here — <see cref="WeaveTempo.Urgency"/> has the argument for why
        /// this is not derived from the time remaining.
        /// </summary>
        public float Urgency { set { _urgency = value < 0f ? 0f : value > 1f ? 1f : value; } }

        // Free ground is the thing the player is hunting, so it is the brighter of the two.
        static readonly Color Ground = new Color(1f, 1f, 1f, .085f);
        static readonly Color Taken = new Color(1f, 1f, 1f, .02f);
        static readonly Color Sleeping = new Color(.44f, .48f, .60f, 1f);

        /// <summary>How pale a channel is before its light has reached that far.</summary>
        const float Unlit = .30f, LitAlpha = .98f, LiveAlpha = .62f;

        /// <summary>Thickness of a channel and of the line still under the finger.</summary>
        const float Thick = .34f, LiveThick = .24f;

        /// <summary>How solid a bead's ring is before and after its own light has come through.</summary>
        const float WaitingRing = .62f, ThreadedRing = 1f;

        // ------------------------------------------------------------------ building
        public void Begin(RectTransform host, WeaveLayout layout)
        {
            _host = host;
            _run = new WeaveRun(layout);
            _drawing.Clear();
            _liveParts.Clear();
            _pair = -1;
            _anyDrawn = false;
            _closing = false;
            _pressed = false;
            _urgency = 0f;
            Locked = false;

            Tween.KillAll(this);

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var rect = host.rect;
            _cell = Mathf.Min(rect.width / layout.Width, rect.height / layout.Height);
            _size = _cell * .88f;

            _grid = UIKit.Node("Grove", host);
            UIKit.StretchTo(_grid, 0, 0, 0, 0);

            // One catcher over the whole grove rather than a widget per cell: this is a drag, and
            // a drag handed between sixty-three separate widgets is a drag that drops.
            var catcher = _grid.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;

            _origin = new Vector2(-(layout.Width - 1) * _cell * .5f,
                                  (layout.Height - 1) * _cell * .5f);

            _groundLayer = UIKit.Node("Ground", _grid);
            _inkLayer = UIKit.Node("Ink", _grid);

            // Above the ink and below the ends, deliberately. A bead has to stay readable once
            // its own channel has been drawn over it — "have I been through here" is the whole
            // question it asks — and it must never sit on top of a crystal or a critter, which
            // are what the player aims at.
            _beadLayer = UIKit.Node("Beads", _grid);

            _endLayer = UIKit.Node("Ends", _grid);
            _fxLayer = UIKit.Node("Fx", _grid);

            // Behind everything: the grove's own light, which comes up as pairs are joined. It is
            // the only thing on screen that says "this is going well" while a run is in progress.
            _wash = UIKit.Img("Wash", _groundLayer, Art.Glow(256, 1.5f),
                              new Color(1f, 1f, 1f, 0f),
                              Vector2.one * _cell * Mathf.Max(layout.Width, layout.Height) * 1.5f,
                              new Vector2(.5f, .5f), Vector2.zero);

            _ground = new Image[layout.Count];
            for (int i = 0; i < _ground.Length; i++)
                _ground[i] = UIKit.Img("Cell" + i, _groundLayer, Art.Round(16), Ground,
                                       Vector2.one * _size, new Vector2(.5f, .5f), Where(i));

            _channel = new RectTransform[layout.Pairs.Count];
            _parts = new List<Image>[layout.Pairs.Count];
            _arrived = new bool[layout.Pairs.Count];
            _crystal = new RectTransform[layout.Pairs.Count];
            _critter = new RectTransform[layout.Pairs.Count];
            _body = new Image[layout.Pairs.Count];
            _want = new Image[layout.Pairs.Count];
            _halo = new Image[layout.Pairs.Count];

            BuildEndpoints(layout);
            BuildBeads(layout);

            _liveInk = UIKit.Node("Live", _inkLayer);

            // On top of the grove and nothing else. Alpha is driven from Urgency, so on a grove
            // with no clock it is simply never drawn.
            _vignette = UIKit.Img("Press", _fxLayer, Art.Vignette(256),
                                  new Color(1f, .32f, .28f, 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, -_cell, -_cell, -_cell, -_cell);

            RefreshCells();
        }

        void BuildEndpoints(WeaveLayout layout)
        {
            for (int p = 0; p < layout.Pairs.Count; p++)
            {
                var pair = layout.Pairs[p];
                var tint = Pal.EnergyColour(pair.Colour);

                var crystal = UIKit.Box("Crystal" + p, _endLayer, Vector2.one * _size,
                                        new Vector2(.5f, .5f), Where(pair.Heart));
                _crystal[p] = crystal;

                UIKit.Img("Glow", crystal, Art.Glow(128, 2.3f), Pal.A(tint, .45f),
                          Vector2.one * _cell * 1.6f, new Vector2(.5f, .5f), Vector2.zero);

                var gem = UIKit.Img("Gem", crystal, Art.Gem(96, tint), Color.white,
                                    Vector2.one * _size * .92f, new Vector2(.5f, .5f),
                                    Vector2.zero);
                Tween.Breathe(gem.transform, .06f, 2.2f, p * .35f);

                var root = UIKit.Box("Critter" + p, _endLayer, Vector2.one * _size,
                                     new Vector2(.5f, .5f), Where(pair.Critter));
                _critter[p] = root;

                // Behind the critter and dark until it wakes, so waking is something arriving
                // rather than only a tint changing.
                _halo[p] = UIKit.Img("Halo", root, Art.Glow(128, 2.1f), Pal.A(tint, 0f),
                                     Vector2.one * _cell * 1.7f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                var frames = Art.Frames("Critters/c" + (1 + p % 5));
                var body = UIKit.Img("Body", root,
                                     frames != null && frames.Length > 0 ? frames[0] : null,
                                     Sleeping, Vector2.one * _size * .72f,
                                     new Vector2(.5f, .5f), Vector2.zero);
                body.preserveAspect = true;
                if (frames != null && frames.Length > 0) Flipbook.Attach(body, frames, 14f);
                _body[p] = body;

                _want[p] = UIKit.Img("Want", root, Art.Ring(128, 9f), tint,
                                     Vector2.one * _size * .98f, new Vector2(.5f, .5f),
                                     Vector2.zero);
            }
        }

        /// <summary>
        /// Draws the beads: a ring of the colour that owes each one, standing on the ground the
        /// player has to come through.
        ///
        /// <para>
        /// Hollow rather than filled, because a bead is a place to pass <em>through</em> and a
        /// solid shape reads as a thing in the way — which is exactly the wrong half of what it
        /// means. Its own channel is drawn through the hole.
        /// </para>
        /// <para>
        /// A <em>hexagon</em> rather than a circle, and that is not decoration. A sleeping
        /// critter already wears a ring of its own colour to say what it wants, so drawing beads
        /// as rings put eleven circles in six colours on the finale and left the player working
        /// out which of them were places and which were creatures. See <see cref="Art.HexRing"/>.
        /// </para>
        /// </summary>
        void BuildBeads(WeaveLayout layout)
        {
            int count = layout.Beads.Count;
            _bead = new RectTransform[count];
            _beadRing = new Image[count];
            _beadGlow = new Image[count];

            for (int b = 0; b < count; b++)
            {
                var bead = layout.Beads[b];
                var tint = Pal.EnergyColour(layout.Pairs[bead.Pair].Colour);

                var root = UIKit.Box("Bead" + b, _beadLayer, Vector2.one * _size,
                                     new Vector2(.5f, .5f), Where(bead.Cell));
                _bead[b] = root;

                _beadGlow[b] = UIKit.Img("Glow", root, Art.Glow(128, 2.2f), Pal.A(tint, 0f),
                                         Vector2.one * _cell * 1.35f, new Vector2(.5f, .5f),
                                         Vector2.zero);

                // A hexagon rather than a circle, because a circle is what a sleeping critter
                // already wears to name its colour — see Art.HexRing for what that cost.
                _beadRing[b] = UIKit.Img("Ring", root, Art.HexRing(128, 11f),
                                         Pal.A(tint, WaitingRing),
                                         Vector2.one * _size * .80f, new Vector2(.5f, .5f),
                                         Vector2.zero);
                _beadRing[b].raycastTarget = false;
            }
        }

        Vector2 Where(int index)
            => _origin + new Vector2((index % _run.Grove.Width) * _cell,
                                     -(index / _run.Grove.Width) * _cell);

        int CellUnder(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _grid, screen, Flow.Canvas.worldCamera, out var local))
                return -1;

            var layout = _run.Grove;
            int x = Mathf.RoundToInt((local.x - _origin.x) / _cell);
            int y = Mathf.RoundToInt((_origin.y - local.y) / _cell);
            return layout.Inside(x, y) ? layout.Index(x, y) : -1;
        }

        // ------------------------------------------------------------------ drawing
        public void OnPointerDown(PointerEventData e)
        {
            if (Locked || _run == null) return;

            int at = CellUnder(e.position);
            if (at < 0) return;

            int owner = _run.OwnerOf(at);

            // A tap on a finished channel takes it back — which is also how a route is changed,
            // so there is no separate erase control to find.
            if (owner >= 0 && _run.IsJoined(owner) && _run.Grove.EndpointAt(at) < 0)
            {
                Unwind(owner);
                return;
            }

            int pair = _run.Grove.EndpointAt(at);
            if (pair < 0) return;

            // Starting from either end of a joined pair replaces it, so a finished channel can be
            // redrawn without being erased first.
            if (_run.IsJoined(pair)) Unwind(pair, quiet: true);

            _pair = pair;
            _drawing.Clear();
            _drawing.Add(at);
            ClearLive();

            // The far end answers. On a grove wearing six colours, three of which are blends of
            // the other three, "which critter is this crystal for" is a real question — and the
            // cheapest honest answer is to make the one that wants it move.
            Signal(pair, at);

            Audio.Sfx("press", .4f, 1.2f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (Locked || _pair < 0 || _drawing.Count == 0) return;

            int at = CellUnder(e.position);
            if (at < 0 || at == _drawing[_drawing.Count - 1]) return;

            if (_drawing.Count > 1 && at == _drawing[_drawing.Count - 2])
            {
                _drawing.RemoveAt(_drawing.Count - 1);
                PopLive();
                Audio.Sfx("click", .13f, .8f);
                return;
            }

            if (_drawing.Contains(at)) return;
            if (!_run.Grove.Adjacent(_drawing[_drawing.Count - 1], at)) return;

            // The rule the whole puzzle rests on, asked of the run rather than re-derived here:
            // a channel crosses free ground, its own two ends, and its own beads. Working that
            // out in the view is how the bead rule would end up stated in two places and
            // enforced in one — WeaveRun.Free is the one that also holds for every future input.
            var ends = _run.Grove.Pairs[_pair];
            bool ownEnd = at == ends.Heart || at == ends.Critter;

            if (!ownEnd && !_run.Free(_pair, at))
            {
                Blocked(at);
                return;
            }

            int from = _drawing[_drawing.Count - 1];
            _drawing.Add(at);
            PushLive(from, at);

            Audio.Sfx("click", .2f, Mathf.Min(2.2f, 1f + _drawing.Count * .04f));

            if (ownEnd) Commit();
        }

        public void OnPointerUp(PointerEventData e) => Commit();

        /// <summary>
        /// Says who is in the way, in their colour, at the cell the finger was refused.
        ///
        /// <para>
        /// A sound alone says only "no". On a six-colour grove the useful question is
        /// <em>whose</em> ground that is, and the answer is already on screen — so the refusal is
        /// drawn in the blocking channel's own tint. Throttled by cell and by time, because a
        /// finger held against a wall re-enters the same cell many times a second and a refusal
        /// that machine-guns is worse than no feedback at all.
        /// </para>
        /// </summary>
        void Blocked(int at)
        {
            if (at == _blockedAt && Time.unscaledTime - _blockedWhen < .45f) return;

            _blockedAt = at;
            _blockedWhen = Time.unscaledTime;

            int owner = _run.OwnerOf(at);
            var tint = owner >= 0 ? Pal.EnergyColour(_run.Grove.Pairs[owner].Colour) : Pal.Rose;

            Ripple(Where(at), tint, _cell * 1.15f, .34f);
            if (owner >= 0 && _ground[at]) Flare(_ground[at], tint);

            Audio.Sfx("blocked", .3f);
        }

        void Signal(int pair, int from)
        {
            var ends = _run.Grove.Pairs[pair];
            int far = from == ends.Heart ? ends.Critter : ends.Heart;
            var tint = Pal.EnergyColour(ends.Colour);

            Ripple(Where(far), tint, _cell * 1.5f, .5f);

            var target = from == ends.Heart ? _critter[pair] : _crystal[pair];
            if (target) Tween.Punch(target, .2f, .34f);
        }

        /// <summary>
        /// Takes the drawn path if it reaches, and drops it if it does not.
        ///
        /// Called both when the finger lifts and the instant a path touches its own far end, so
        /// dragging straight onto the critter lands the channel under the finger — which is where
        /// the player is looking — rather than making them let go first.
        /// </summary>
        void Commit()
        {
            if (_pair < 0) return;

            // Guarded, because this is the one input path that did not check. A run lost on the
            // clock mid-drag locks the board, and the finger coming up afterwards would otherwise
            // still lay a channel on a board whose run is already over.
            if (Locked) { _pair = -1; _drawing.Clear(); ClearLive(); return; }

            int pair = _pair;
            var path = new List<int>(_drawing);

            _pair = -1;
            _drawing.Clear();
            ClearLive();

            if (path.Count >= 2 && _run.Draw(pair, path))
            {
                Land(pair);

                if (!_anyDrawn)
                {
                    _anyDrawn = true;
                    Committed?.Invoke();
                }
            }

            RefreshCells();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ the ink
        /// <summary>
        /// Builds a pair's channel, every segment dark. <see cref="Land"/> is what lights it.
        /// </summary>
        void BuildChannel(int pair)
        {
            DropChannel(pair, drain: false);

            var path = _run.PathOf(pair);
            if (path.Count < 2) return;

            var root = UIKit.Node("Channel" + pair, _inkLayer);
            var tint = Pal.EnergyColour(_run.Grove.Pairs[pair].Colour);
            var parts = new List<Image>(Mathf.Max(2, (path.Count - 1) * 2));

            for (int i = 1; i < path.Count; i++)
            {
                parts.Add(Link(root, Where(path[i - 1]), Where(path[i]),
                               Pal.A(tint, Unlit), Thick));
                parts.Add(Knuckle(root, Where(path[i]), Pal.A(tint, Unlit), Thick));
            }

            _channel[pair] = root;
            _parts[pair] = parts;
        }

        void DropChannel(int pair, bool drain)
        {
            var root = _channel[pair];
            _channel[pair] = null;
            _parts[pair] = null;
            if (!root) return;

            if (!drain)
            {
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
                return;
            }

            // Light draining back rather than a line vanishing. Short — taking a channel back is
            // something the player does constantly, and every millisecond of it is friction on
            // the correction rather than on the mistake.
            var group = root.gameObject.AddComponent<CanvasGroup>();
            Tween.Run(.16f, Ease.OutQuad, t =>
            {
                if (!group) return;
                group.alpha = 1f - t;
                root.localScale = Vector3.one * (1f - .10f * t);
            }, group);
            Tween.After(.2f, () => { if (root) { root.gameObject.SetActive(false); Destroy(root.gameObject); } });
        }

        Image Link(Transform parent, Vector2 a, Vector2 b, Color colour, float thickness)
        {
            var delta = b - a;
            var link = UIKit.Img("Link", parent, Art.Capsule(24, 96), colour,
                                 new Vector2(_size * thickness,
                                             delta.magnitude + _size * thickness),
                                 new Vector2(.5f, .5f), (a + b) * .5f);
            ((RectTransform)link.transform).localRotation =
                Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
            return link;
        }

        Image Knuckle(Transform parent, Vector2 at, Color colour, float thickness)
            => UIKit.Img("Knuckle", parent, Art.Disc(64), colour,
                         Vector2.one * _size * thickness, new Vector2(.5f, .5f), at);

        void PushLive(int from, int to)
        {
            var tint = Pal.A(Pal.EnergyColour(_run.Grove.Pairs[_pair].Colour), LiveAlpha);
            _liveParts.Add(Link(_liveInk, Where(from), Where(to), tint, LiveThick));
            _liveParts.Add(Knuckle(_liveInk, Where(to), tint, LiveThick));
        }

        void PopLive()
        {
            for (int i = 0; i < 2 && _liveParts.Count > 0; i++)
            {
                var img = _liveParts[_liveParts.Count - 1];
                _liveParts.RemoveAt(_liveParts.Count - 1);
                if (img) { img.gameObject.SetActive(false); Destroy(img.gameObject); }
            }
        }

        void ClearLive()
        {
            for (int i = 0; i < _liveParts.Count; i++)
            {
                var img = _liveParts[i];
                if (img) { img.gameObject.SetActive(false); Destroy(img.gameObject); }
            }
            _liveParts.Clear();
        }

        // ------------------------------------------------------------------ landing
        /// <summary>
        /// A channel landing: light runs it end to end, and the critter wakes when the light
        /// actually arrives.
        ///
        /// <para>
        /// <b>The arrival is the whole effect.</b> What this replaces put the critter's flash and
        /// the channel's colour on screen in the same frame, so the line and the waking were two
        /// things that happened rather than one thing causing the other — and cause is what the
        /// player is being paid for. The wake is therefore scheduled off the same duration the
        /// light is travelling for, and both come from <see cref="WeaveTempo"/> so a long channel
        /// cannot make the wait proportionally long.
        /// </para>
        /// </summary>
        void Land(int pair)
        {
            BuildChannel(pair);

            var path = _run.PathOf(pair);
            var parts = _parts[pair];
            if (parts == null || path.Count < 2) { Wake(pair); Settle(); return; }

            var tint = Pal.EnergyColour(_run.Grove.Pairs[pair].Colour);
            var lit = Pal.A(tint, LitAlpha);
            var dark = Pal.A(tint, Unlit);

            int steps = path.Count - 1;
            float travel = WeaveTempo.TravelSeconds(path.Count);

            var head = UIKit.Img("Spark", _fxLayer, Art.Glow(128, 2.0f),
                                 Pal.A(Pal.Lift(tint, .55f), .95f),
                                 Vector2.one * _cell * 1.3f, new Vector2(.5f, .5f),
                                 Where(path[0]));

            // One tween walking every segment rather than a tween per segment: a thirty-cell
            // channel would otherwise start sixty of them at once, and the swell reads better as
            // a wave behind the light than as a row of separate pops.
            Tween.Run(travel, Ease.Linear, t =>
            {
                if (!head) return;

                float front = t * steps;
                head.rectTransform.anchoredPosition = Along(path, t);
                head.transform.localScale = Vector3.one * (.85f + .35f * Mathf.Sin(t * Mathf.PI));

                for (int s = 0; s < steps; s++)
                {
                    float behind = front - s;
                    bool reached = behind >= 0f;
                    float swell = reached && behind < 1.8f ? 1f + .42f * (1f - behind / 1.8f) : 1f;
                    Paint(parts, s, reached ? lit : dark, swell);
                }
            }, head);

            Tween.After(travel, () =>
            {
                if (!this) return;
                if (parts != null)
                    for (int s = 0; s < steps; s++) Paint(parts, s, lit, 1f);
                if (head) { head.gameObject.SetActive(false); Destroy(head.gameObject); }

                Wake(pair);
                Settle();
            }, this);
        }

        /// <summary>Sets one segment's link and knuckle, both at once.</summary>
        static void Paint(List<Image> parts, int segment, Color colour, float swell)
        {
            int a = segment * 2;
            for (int k = a; k < a + 2 && k < parts.Count; k++)
            {
                var img = parts[k];
                if (!img) continue;
                img.color = colour;
                img.transform.localScale = new Vector3(swell, swell, 1f);
            }
        }

        Vector2 Along(IReadOnlyList<int> path, float t)
        {
            if (path.Count < 2) return Where(path[0]);

            float f = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Min(path.Count - 2, Mathf.FloorToInt(f));
            return Vector2.Lerp(Where(path[i]), Where(path[i + 1]), f - i);
        }

        /// <summary>
        /// A critter waking: the one moment this mode is played for, and it is deliberately
        /// louder every time.
        ///
        /// <para>
        /// The note, the spark count and the flash all climb with how many are already awake, so
        /// the sixth critter of a hard grove is unmistakably a bigger event than the first of an
        /// easy one. The ladder is pentatonic and rises exactly once per pair — see
        /// <see cref="WeaveTempo.Pitch"/>, which is where the six-note ceiling and the reason for
        /// it live.
        /// </para>
        /// </summary>
        void Wake(int pair)
        {
            if (_run == null) return;

            _arrived[pair] = true;

            var ends = _run.Grove.Pairs[pair];
            var tint = Pal.EnergyColour(ends.Colour);

            int joined = _run.Joined;
            float share = _run.Pairs <= 0 ? 1f : joined / (float)_run.Pairs;

            var body = _body[pair];
            if (body) Tween.Tint(body, Color.white, .2f);

            var root = _critter[pair];
            if (root) Tween.Punch(root, .34f + .12f * share, .4f);

            var halo = _halo[pair];
            if (halo) Tween.Fade(halo, .55f, .35f);

            // The ring the critter has been wearing all along is what breaks outward, so the
            // thing that said "this is what I want" is the thing that says "I have it".
            var want = _want[pair];
            if (want)
            {
                want.color = Pal.Lift(tint, .5f);
                Tween.Tint(want, tint, .45f);
            }

            // Each bead this channel has just been threaded through answers as the light
            // passes it, so a route that collected one reads as having collected it.
            var beads = _run.Grove.Beads;
            for (int b = 0; b < beads.Count; b++)
                if (beads[b].Pair == pair && _run.IsThreaded(b))
                    Ripple(Where(beads[b].Cell), tint, _cell * 1.25f, .45f);

            Ripple(Where(ends.Critter), tint, _cell * 2.1f, .55f);
            Burst.Sparks(_fxLayer, Where(ends.Critter), tint,
                         14 + Mathf.RoundToInt(14f * share),
                         190f + 90f * share, 20f, .6f);

            // The crystal it came from answers, so the pair reads as a pair.
            if (_crystal[pair]) Tween.Punch(_crystal[pair], .22f, .3f);

            Audio.Sfx("lit", .62f, WeaveTempo.Pitch(joined));
            if (joined >= 2) Audio.Sfx("chime", .22f + .1f * share, WeaveTempo.Pitch(joined));

            Brighten();
            RefreshCells();
        }

        /// <summary>
        /// Ends the run if the grove is finished. Called once for every channel that lands, which
        /// is the only thing that can ever finish one.
        ///
        /// <para>
        /// <b>Deliberately not inside <see cref="Wake"/>, and that is the point of it existing.</b>
        /// It used to be, and the two were the same thing right up until they were not: a weave is
        /// won when every critter is awake <em>and</em> no bare ground is left, so the last thing a
        /// player does on most groves is re-route a channel whose critter is already up. Waking is
        /// no longer what wins, so hanging the win on it was a coincidence rather than a rule —
        /// today <c>Wake</c> happens to run on every landing, and the first future caller that
        /// wakes a critter for any other reason silently moves the ending.
        /// </para>
        /// </summary>
        void Settle()
        {
            if (_run != null && _run.IsSolved) Close();
        }

        /// <summary>The grove's own light, coming up as the weave is finished.</summary>
        void Brighten()
        {
            if (!_wash || _run.Pairs <= 0) return;

            float share = _run.Joined / (float)_run.Pairs;
            Tween.Fade(_wash, .05f + .13f * share, .5f);
        }

        // ------------------------------------------------------------------ endings
        /// <summary>
        /// The weave is finished. Every channel lights again in turn, and only then is the run
        /// reported as won.
        ///
        /// <para>
        /// <b>The board is latched first, and that is a correctness step rather than a polish
        /// one.</b> The screen's clock only accrues while this is unlocked, so without it a grove
        /// solved with two seconds left would run its own celebration into a timeout and lose a
        /// run the player had already won. Latching also makes the recorded time the moment of
        /// solving rather than the moment the panel opens.
        /// </para>
        /// </summary>
        void Close()
        {
            if (_closing) return;

            _closing = true;
            Locked = true;
            Finishing?.Invoke();

            int channels = _run.Pairs;
            for (int p = 0; p < channels; p++)
            {
                int pair = p;
                Tween.After(WeaveTempo.FinaleAt(p, channels),
                            () => { if (this) Sweep(pair, p + 1); }, this);
            }

            Tween.After(WeaveTempo.FinaleSeconds(channels) + .2f,
                        () => { if (this) Solved?.Invoke(); }, this);
        }

        /// <summary>One channel of the closing cascade: light runs it again, brighter and faster.</summary>
        void Sweep(int pair, int note)
        {
            var parts = _parts[pair];
            var path = _run.PathOf(pair);
            if (parts == null || path.Count < 2) return;

            var tint = Pal.EnergyColour(_run.Grove.Pairs[pair].Colour);
            var lit = Pal.A(tint, LitAlpha);
            var flare = Pal.A(Pal.Lift(tint, .6f), 1f);
            int steps = path.Count - 1;

            Tween.Run(WeaveTempo.MinTravel * 1.4f, Ease.OutQuad, t =>
            {
                float front = t * steps;
                for (int s = 0; s < steps; s++)
                {
                    float behind = front - s;
                    bool hot = behind >= 0f && behind < 2.2f;
                    Paint(parts, s, hot ? flare : lit,
                          hot ? 1f + .5f * (1f - behind / 2.2f) : 1f);
                }
            }, this);

            Tween.After(WeaveTempo.MinTravel * 1.6f, () =>
            {
                if (!this || parts == null) return;
                for (int s = 0; s < steps; s++) Paint(parts, s, lit, 1f);
            }, this);

            if (_critter[pair]) Tween.Punch(_critter[pair], .26f, .3f);
            Audio.Sfx("chime", .5f, WeaveTempo.Pitch(note));
        }

        void Unwind(int pair, bool quiet = false)
        {
            _arrived[pair] = false;
            _run.Erase(pair);
            DropChannel(pair, drain: true);

            var body = _body[pair];
            if (body) Tween.Tint(body, Sleeping, .18f);

            var halo = _halo[pair];
            if (halo) Tween.Fade(halo, 0f, .18f);

            if (!quiet) Audio.Sfx("back", .5f);

            Brighten();
            RefreshCells();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Re-reads the cells and the critters. Note what it no longer does: it does not touch
        /// the ink, which is owned by the pair it belongs to and edited in place.
        /// </summary>
        void RefreshCells()
        {
            if (_run == null) return;

            for (int i = 0; i < _ground.Length; i++)
            {
                bool free = _run.OwnerOf(i) < 0;
                if (_ground[i]) _ground[i].color = free ? Ground : Taken;
            }

            for (int p = 0; p < _run.Pairs; p++)
            {
                var body = _body[p];
                if (body && !Tween.Orphaned(body))
                    body.color = _run.IsJoined(p) ? Color.white : Sleeping;
            }

            PaintBeads();

            // Every critter awake with a bead still waiting is the one state of this board that
            // looks finished and is not. It is much rarer than the state it replaces — a bead is
            // on the board and a missing cell was not — but it is the same trap, so it gets the
            // same answer: the beads still waiting are made to breathe until they are threaded.
            bool wasNagging = _nagging;
            _nagging = !_closing && _run.Joined >= _run.Pairs && _waiting.Count > 0;

            if (_nagging && !wasNagging) Audio.Sfx("tick", .35f, 1.4f);
        }

        /// <summary>
        /// Re-reads which beads have had their light through them.
        ///
        /// <para>
        /// Edited in place rather than rebuilt, and only where the state actually moved — a
        /// bead that was threaded and still is must not replay its arrival, or every drag
        /// elsewhere on the board sets the whole set of them off again. <c>GridView</c>'s
        /// Show/Refresh rule, on the smallest thing on the screen that has an entrance.
        /// </para>
        /// </summary>
        void PaintBeads()
        {
            _waiting.Clear();
            if (_bead == null) return;

            var beads = _run.Grove.Beads;
            for (int b = 0; b < _bead.Length; b++)
            {
                // Threaded in the model and lit on the screen are different moments — see
                // _arrived. A bead waits for its own light rather than for the finger.
                bool threaded = _run.IsThreaded(b) && _arrived[beads[b].Pair];
                if (!_run.IsThreaded(b)) _waiting.Add(b);

                var ring = _beadRing[b];
                if (!ring) continue;

                var tint = Pal.EnergyColour(_run.Grove.Pairs[beads[b].Pair].Colour);
                var wanted = threaded ? Pal.A(Pal.Lift(tint, .45f), ThreadedRing)
                                      : Pal.A(tint, WaitingRing);

                bool moved = ring.color != wanted;
                ring.color = wanted;

                if (_beadGlow[b]) _beadGlow[b].color = Pal.A(tint, threaded ? .5f : 0f);

                if (moved && threaded && _bead[b])
                {
                    _bead[b].localScale = Vector3.one;
                    Tween.Pop(_bead[b], .74f, .26f);
                }
                else if (!threaded && _bead[b] && !Tween.Orphaned(ring))
                {
                    _bead[b].localScale = Vector3.one;
                }
            }
        }

        void Ripple(Vector2 at, Color colour, float size, float strength)
        {
            var img = UIKit.Img("Ripple", _fxLayer, Art.Ring(128, 8f), Pal.A(colour, strength),
                                Vector2.one * size, new Vector2(.5f, .5f), at);
            Tween.Run(.42f, Ease.OutQuint, t =>
            {
                if (!img) return;
                img.transform.localScale = Vector3.one * Mathf.Lerp(.35f, 1.35f, t);
                var c = img.color; c.a = strength * (1f - t); img.color = c;
            }, img);
            Tween.After(.5f, () => { if (img) { img.gameObject.SetActive(false); Destroy(img.gameObject); } });
        }

        void Flare(Image target, Color colour)
        {
            if (!target) return;

            var from = target.color;
            Tween.Run(.3f, Ease.OutQuint, t =>
            {
                if (!target) return;
                target.color = Color.Lerp(Pal.A(colour, .5f), from, t);
            }, target);
        }

        /// <summary>
        /// The grove pressing in as the light runs down.
        ///
        /// Drawn from <see cref="Urgency"/>, which is zero on a grove with no clock — so this is
        /// simply never seen there rather than needing a branch. It breathes rather than holding
        /// steady, because a static tint at the edge of the screen stops being read within a few
        /// seconds and the point of it is to still be saying something at ten.
        /// </summary>
        void Update()
        {
            Nag();

            if (!_vignette) return;

            if (_urgency <= 0f)
            {
                if (_pressed) _pressed = false;
                var clear = _vignette.color; clear.a = 0f; _vignette.color = clear;
                return;
            }

            if (!_pressed)
            {
                _pressed = true;
                Audio.Sfx("tock", .45f, .9f);
            }

            float beat = .5f + .5f * Mathf.Sin(Time.unscaledTime * (5f + 5f * _urgency));
            var c = _vignette.color;
            c.a = _urgency * (.16f + .22f * beat);
            _vignette.color = c;
        }

        /// <summary>
        /// The beads still waiting, breathing, while every critter is already awake.
        ///
        /// Driven here rather than as a tween per bead: the set changes every time a channel is
        /// drawn or taken back, and a tween owned by a bead would have to be found and killed
        /// each time. One pass over a handful of rings is cheaper than the bookkeeping.
        /// </summary>
        void Nag()
        {
            if (!_nagging || _bead == null) return;

            float beat = .5f + .5f * Mathf.Sin(Time.unscaledTime * 4.4f);

            for (int i = 0; i < _waiting.Count; i++)
            {
                var root = _bead[_waiting[i]];
                if (root) root.localScale = Vector3.one * (1f + .16f * beat);
            }
        }

        /// <summary>Takes every channel back, for the restart button.</summary>
        public void Clear()
        {
            if (_run == null) return;

            for (int p = 0; p < _run.Pairs; p++)
            {
                _arrived[p] = false;
                if (_run.IsJoined(p)) DropChannel(p, drain: true);
                else DropChannel(p, drain: false);

                var body = _body[p];
                if (body) Tween.Tint(body, Sleeping, .18f);
                var halo = _halo[p];
                if (halo) Tween.Fade(halo, 0f, .18f);
            }

            _run.Reset();
            _pair = -1;
            _drawing.Clear();
            ClearLive();

            Brighten();
            RefreshCells();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ pointing at it
        /// <summary>
        /// Where the things on this board are, for a lesson that has to point at one.
        ///
        /// <para>
        /// Transforms rather than positions, because whatever is pointing lives on the overlay
        /// canvas and has to convert into its own space — and because a rectangle is what a ring
        /// and a hole are cut from. A crystal, a critter and a bead are all one cell across, and
        /// a plain cell is handed back for the ground between them.
        /// </para>
        /// <para>
        /// Read-only and deliberately narrow: nothing outside may move, hide or restyle a piece
        /// of the board. A lesson asks where something is; the board still owns what it looks
        /// like.
        /// </para>
        /// </summary>
        public RectTransform CrystalOf(int pair)
            => _crystal != null && pair >= 0 && pair < _crystal.Length ? _crystal[pair] : null;

        public RectTransform CritterOf(int pair)
            => _critter != null && pair >= 0 && pair < _critter.Length ? _critter[pair] : null;

        public RectTransform BeadAt(int bead)
            => _bead != null && bead >= 0 && bead < _bead.Length ? _bead[bead] : null;

        public RectTransform CellAt(int index)
            => _ground != null && index >= 0 && index < _ground.Length && _ground[index]
                ? _ground[index].rectTransform : null;

        void OnDestroy() => Tween.KillAll(this);
    }
}
