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
    /// </summary>
    public sealed class WeaveView : MonoBehaviour, IPointerDownHandler, IDragHandler,
                                    IPointerUpHandler
    {
        /// <summary>Raised whenever the board moves, so the screen can repaint its readouts.</summary>
        public Action Changed;

        /// <summary>Every pair joined.</summary>
        public Action Solved;

        /// <summary>The first channel of the run has landed — the moment it is owed for.</summary>
        public Action Committed;

        WeaveRun _run;
        RectTransform _host, _grid;
        Image[] _ground;
        readonly List<GameObject> _ink = new List<GameObject>();
        readonly List<int> _drawing = new List<int>();

        int _pair = -1;
        bool _anyDrawn;
        float _cell, _size;
        Vector2 _origin;

        /// <summary>Refuses input while the run is over or a panel is up.</summary>
        public bool Locked { get; set; }

        public WeaveRun Run => _run;

        static readonly Color Ground = new Color(1f, 1f, 1f, .05f);
        static readonly Color Sleeping = new Color(.44f, .48f, .60f, 1f);

        // ------------------------------------------------------------------ building
        public void Begin(RectTransform host, WeaveLayout layout)
        {
            _host = host;
            _run = new WeaveRun(layout);
            _drawing.Clear();
            _ink.Clear();
            _pair = -1;
            _anyDrawn = false;
            Locked = false;

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

            _ground = new Image[layout.Count];
            for (int i = 0; i < _ground.Length; i++)
                _ground[i] = UIKit.Img("Cell" + i, _grid, Art.Round(16), Ground,
                                       Vector2.one * _size, new Vector2(.5f, .5f), Where(i));

            BuildEndpoints(layout);
            Repaint();
        }

        void BuildEndpoints(WeaveLayout layout)
        {
            for (int p = 0; p < layout.Pairs.Count; p++)
            {
                var pair = layout.Pairs[p];
                var tint = Pal.EnergyColour(pair.Colour);

                UIKit.Img("HeartGlow", _grid, Art.Glow(128, 2.3f), Pal.A(tint, .45f),
                          Vector2.one * _cell * 1.6f, new Vector2(.5f, .5f), Where(pair.Heart));

                var gem = UIKit.Img("Heart" + p, _grid, Art.Gem(96, tint), Color.white,
                                    Vector2.one * _size * .92f, new Vector2(.5f, .5f),
                                    Where(pair.Heart));
                Tween.Breathe(gem.transform, .06f, 2.2f);

                var root = UIKit.Box("Critter" + p, _grid, Vector2.one * _size,
                                     new Vector2(.5f, .5f), Where(pair.Critter));

                var frames = Art.Frames("Critters/c" + (1 + p % 5));
                var body = UIKit.Img("Body", root,
                                     frames != null && frames.Length > 0 ? frames[0] : null,
                                     Sleeping, Vector2.one * _size * .72f,
                                     new Vector2(.5f, .5f), Vector2.zero);
                body.preserveAspect = true;
                if (frames != null && frames.Length > 0) Flipbook.Attach(body, frames, 14f);

                UIKit.Img("Want", root, Art.Ring(128, 9f), tint, Vector2.one * _size * .98f,
                          new Vector2(.5f, .5f), Vector2.zero);
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
                _run.Erase(owner);
                Audio.Sfx("back", .5f);
                Repaint();
                Changed?.Invoke();
                return;
            }

            int pair = _run.Grove.EndpointAt(at);
            if (pair < 0) return;

            // Starting from either end of a joined pair replaces it, so a finished channel can be
            // redrawn without being erased first.
            if (_run.IsJoined(pair)) _run.Erase(pair);

            _pair = pair;
            _drawing.Clear();
            _drawing.Add(at);

            Audio.Sfx("press", .4f, 1.2f);
            Repaint();
        }

        public void OnDrag(PointerEventData e)
        {
            if (Locked || _pair < 0 || _drawing.Count == 0) return;

            int at = CellUnder(e.position);
            if (at < 0 || at == _drawing[_drawing.Count - 1]) return;

            if (_drawing.Count > 1 && at == _drawing[_drawing.Count - 2])
            {
                _drawing.RemoveAt(_drawing.Count - 1);
                Repaint();
                return;
            }

            if (_drawing.Contains(at)) return;
            if (!_run.Grove.Adjacent(_drawing[_drawing.Count - 1], at)) return;

            // The rule the whole puzzle rests on: a channel crosses free ground only, plus the
            // far end of its own pair.
            var ends = _run.Grove.Pairs[_pair];
            bool ownEnd = at == ends.Heart || at == ends.Critter;

            if (!ownEnd && _run.OwnerOf(at) >= 0)
            {
                Audio.Sfx("blocked", .3f);
                return;
            }

            _drawing.Add(at);
            Audio.Sfx("click", .2f, Mathf.Min(2.2f, 1f + _drawing.Count * .04f));
            Repaint();

            if (ownEnd) Commit();
        }

        public void OnPointerUp(PointerEventData e) => Commit();

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

            int pair = _pair;
            var path = new List<int>(_drawing);

            _pair = -1;
            _drawing.Clear();

            if (path.Count >= 2 && _run.Draw(pair, path))
            {
                Land(pair);

                if (!_anyDrawn)
                {
                    _anyDrawn = true;
                    Committed?.Invoke();
                }
            }

            Repaint();
            Changed?.Invoke();

            if (_run.IsSolved) Solved?.Invoke();
        }

        /// <summary>A channel landing: the critter wakes and the light runs down the line.</summary>
        void Land(int pair)
        {
            var ends = _run.Grove.Pairs[pair];
            var tint = Pal.EnergyColour(ends.Colour);

            var critter = _grid.Find("Critter" + pair);
            if (critter != null)
            {
                var body = critter.Find("Body")?.GetComponent<Image>();
                if (body) Tween.Tint(body, Color.white, .22f);
                Tween.Punch(critter, .3f, .36f);
            }

            Burst.Sparks(_grid, Where(ends.Critter), tint, 12, 170f, 18f, .55f);
            Audio.Sfx("lit", .65f, 1f + pair * .09f);
        }

        // ------------------------------------------------------------------ painting
        void Repaint()
        {
            foreach (var go in _ink) if (go) { go.SetActive(false); Destroy(go); }
            _ink.Clear();

            for (int p = 0; p < _run.Pairs; p++)
            {
                var path = _run.PathOf(p);
                if (path.Count >= 2)
                    Draw(path, Pal.EnergyColour(_run.Grove.Pairs[p].Colour), .95f, .34f);
            }

            // The line under the finger is drawn thinner and paler, so it reads as not yet
            // settled — one that looked identical to a finished channel would make the board
            // seem to have more solved than it does.
            if (_drawing.Count >= 2 && _pair >= 0)
                Draw(_drawing, Pal.A(Pal.EnergyColour(_run.Grove.Pairs[_pair].Colour), .7f),
                     1f, .22f);

            for (int i = 0; i < _ground.Length; i++)
                if (_ground[i])
                    _ground[i].color = _run.OwnerOf(i) >= 0 ? new Color(1f, 1f, 1f, .02f) : Ground;

            var critters = _run;
            for (int p = 0; p < _run.Pairs; p++)
            {
                var body = _grid.Find("Critter" + p)?.Find("Body")?.GetComponent<Image>();
                if (body) body.color = _run.IsJoined(p) ? Color.white : Sleeping;
            }
        }

        /// <summary>
        /// A channel as a chain of capsules between cell centres, with a knuckle at each bend.
        ///
        /// Capsules rather than one mesh because a channel turns corners, and a bend drawn as a
        /// straight run is a channel that visibly does not go where the player put it. The
        /// knuckle is what stops a right angle showing a notch on its outside edge.
        /// </summary>
        void Draw(IReadOnlyList<int> path, Color colour, float alpha, float thickness)
        {
            for (int i = 1; i < path.Count; i++)
            {
                var a = Where(path[i - 1]);
                var b = Where(path[i]);
                var delta = b - a;

                var link = UIKit.Img("Link", _grid, Art.Capsule(24, 96), Pal.A(colour, alpha),
                                     new Vector2(_size * thickness,
                                                 delta.magnitude + _size * thickness),
                                     new Vector2(.5f, .5f), (a + b) * .5f);
                ((RectTransform)link.transform).localRotation =
                    Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
                link.transform.SetSiblingIndex(1);
                _ink.Add(link.gameObject);

                var knuckle = UIKit.Img("Knuckle", _grid, Art.Disc(64), Pal.A(colour, alpha),
                                        Vector2.one * _size * thickness,
                                        new Vector2(.5f, .5f), b);
                knuckle.transform.SetSiblingIndex(1);
                _ink.Add(knuckle.gameObject);
            }
        }

        /// <summary>Takes every channel back, for the restart button.</summary>
        public void Clear()
        {
            if (_run == null) return;

            _run.Reset();
            _pair = -1;
            _drawing.Clear();
            Repaint();
            Changed?.Invoke();
        }
    }
}
