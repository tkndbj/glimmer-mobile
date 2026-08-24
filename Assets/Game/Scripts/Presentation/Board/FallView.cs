using System;
using System.Collections;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>Lightfall.</b> Tap a column, a mote falls into it, and the stack either gets richer or
    /// gets taller.
    ///
    /// <para>
    /// The whole reason this verb works where tapping a cell did not is that the consequence is
    /// visible <em>before</em> you commit: a finger held over a column shows a ghost of where the
    /// mote will land and a ring saying whether it will enrich what is there. So the decision is
    /// made with the eyes rather than by arithmetic, which is what a thumb-driven game needs.
    /// </para>
    /// </summary>
    public sealed class FallView : MonoBehaviour
    {
        public Action Changed { get; set; }
        public Action<string> Over { get; set; }

        FallBoard _board;
        RectTransform _host, _grid, _tray;
        Image[] _motes;
        Image _ghost, _ghostRing;
        Btn[] _columns;
        Image[] _queue;

        float _cell, _size;
        Vector2 _origin;
        bool _busy;

        public void Begin(RectTransform host, int width, int height, uint seed)
        {
            _host = host;
            _board = new FallBoard(Mathf.Clamp(width, 4, 8),
                                   Mathf.Clamp(height, 6, 14), seed);
            _busy = false;

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var rect = host.rect;

            // The tray sits under the well, so the well gets what is left. Measured rather than
            // assumed: a fixed cell size is a board that overflows on somebody's phone.
            float trayH = 150f;
            float usableH = rect.height - trayH;
            _cell = Mathf.Min(rect.width / _board.Width, usableH / _board.Height);
            _size = _cell * .88f;

            _grid = UIKit.Node("Well", host);
            UIKit.StretchTo(_grid, 0, trayH, 0, 0);

            _origin = new Vector2(-(_board.Width - 1) * _cell * .5f,
                                  (_board.Height - 1) * _cell * .5f);

            var plate = UIKit.Img("Plate", _grid, Art.Round(26), new Color(.04f, .06f, .12f, .55f),
                                  new Vector2(_board.Width * _cell + 18f,
                                              _board.Height * _cell + 18f),
                                  new Vector2(.5f, .5f), Vector2.zero);
            plate.transform.SetAsFirstSibling();

            _ghost = UIKit.Img("Ghost", _grid, Art.Disc(96), new Color(1, 1, 1, 0f),
                               Vector2.one * _size, new Vector2(.5f, .5f), Vector2.zero);
            _ghostRing = UIKit.Img("GhostRing", _grid, Art.Ring(96, 7f), new Color(1, 1, 1, 0f),
                                   Vector2.one * _size * 1.22f, new Vector2(.5f, .5f), Vector2.zero);

            _motes = new Image[_board.Width * _board.Height];
            BuildColumns();
            BuildTray(host, trayH);
            Repaint();
        }

        Vector2 Where(int index)
        {
            int x = index % _board.Width, y = index / _board.Width;
            return _origin + new Vector2(x * _cell, -y * _cell);
        }

        /// <summary>
        /// One tall button per column rather than a button per cell. A column is the unit of
        /// decision, so it should be the unit of touch — asking a thumb to hit one cell of a
        /// twelve-row well is asking it to be a mouse.
        /// </summary>
        void BuildColumns()
        {
            _columns = new Btn[_board.Width];

            for (int x = 0; x < _board.Width; x++)
            {
                int column = x;
                var strip = UIKit.Box("Col" + x, _grid, new Vector2(_cell, _board.Height * _cell),
                                      new Vector2(.5f, .5f),
                                      new Vector2(_origin.x + x * _cell, 0f));

                var hit = strip.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                hit.raycastTarget = true;

                var btn = strip.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Drop(column), silent: true);
                _columns[x] = btn;

                var hover = strip.gameObject.AddComponent<Hover>();
                hover.Enter = () => ShowGhost(column);
                hover.Exit = HideGhost;
            }
        }

        void BuildTray(RectTransform host, float trayH)
        {
            _tray = UIKit.Box("Tray", host, new Vector2(0f, trayH), new Vector2(.5f, 0f),
                              new Vector2(0f, trayH * .5f));
            _tray.anchorMin = new Vector2(0f, 0f);
            _tray.anchorMax = new Vector2(1f, 0f);
            _tray.sizeDelta = new Vector2(0f, trayH);

            var plate = UIKit.Img("Plate", _tray, Art.Round(26), new Color(.05f, .07f, .13f, .70f),
                                  new Vector2(430f, 118f), new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Edge", plate.transform, Art.RoundOutline(26, 3f), new Color(1, 1, 1, .12f),
                      new Vector2(430f, 118f), new Vector2(.5f, .5f), Vector2.zero);

            _queue = new Image[FallBoard.Lookahead];
            for (int i = 0; i < _queue.Length; i++)
            {
                bool next = i == 0;
                float size = next ? 84f : 48f;
                float x = next ? -140f : -20f + (i - 1) * 74f;

                var seat = UIKit.Img("Seat" + i, plate.transform, Art.Ring(96, 5f),
                                     new Color(1, 1, 1, next ? .22f : .10f),
                                     Vector2.one * (size + 16f), new Vector2(.5f, .5f),
                                     new Vector2(x, 0f));

                _queue[i] = UIKit.Img("Mote" + i, seat.transform, Art.Disc(96), Color.white,
                                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
                if (next) Tween.Breathe(_queue[i].transform, .05f, 1.8f);
            }
        }

        // ------------------------------------------------------------------ the ghost
        void ShowGhost(int column)
        {
            if (_busy || _board.IsLost || !_board.CanDrop(column)) { HideGhost(); return; }

            int row = _board.Landing(column);
            if (row < 0) { HideGhost(); return; }

            var at = Where(_board.Index(column, row));
            var tint = Pal.EnergyColour(_board.Next);

            ((RectTransform)_ghost.transform).anchoredPosition = at;
            ((RectTransform)_ghostRing.transform).anchoredPosition = at;

            bool enriches = _board.Enriches(column);

            // The ring is the whole preview: cream means this drop makes the stack better, amber
            // means it makes it taller. One glance, no arithmetic.
            _ghost.color = Pal.A(tint, enriches ? .30f : .42f);
            _ghostRing.color = Pal.A(enriches ? Pal.Cream : Pal.Amber, .85f);
        }

        void HideGhost()
        {
            if (_ghost) _ghost.color = new Color(1, 1, 1, 0f);
            if (_ghostRing) _ghostRing.color = new Color(1, 1, 1, 0f);
        }

        // ------------------------------------------------------------------ dropping
        void Drop(int column)
        {
            if (_busy || _board.IsLost || !_board.CanDrop(column)) return;

            int colour = _board.Next;
            int row = _board.Landing(column);
            bool enriches = _board.Enriches(column);

            var result = _board.Drop(column);
            if (result == null) return;

            _busy = true;
            HideGhost();
            Changed?.Invoke();

            StartCoroutine(Play(column, row, colour, enriches, result));
        }

        IEnumerator Play(int column, int row, int colour, bool enriches, FallResolution result)
        {
            // The fall itself. Short, because it sits between every decision and its consequence.
            var mote = UIKit.Img("Falling", _grid, Art.Disc(96), Pal.EnergyColour(colour),
                                 Vector2.one * _size, new Vector2(.5f, .5f),
                                 new Vector2(_origin.x + column * _cell, _origin.y + _cell * 1.4f));

            var rt = (RectTransform)mote.transform;
            var from = rt.anchoredPosition;
            var to = Where(_board.Index(column, row));
            float far = Mathf.Max(.12f, Mathf.Abs(from.y - to.y) / (_board.Height * _cell) * .34f);

            Audio.Sfx("whoosh", .35f, 1.3f);
            Tween.Run(far, Ease.InQuad, t =>
            {
                if (rt) rt.anchoredPosition = Vector2.Lerp(from, to, t);
            }, mote);

            yield return new WaitForSecondsRealtime(far);
            if (mote) Destroy(mote.gameObject);
            if (!this) yield break;

            // Landing. An enriched stack chimes and swells; a heightened one thuds.
            Repaint();
            var landed = _motes[_board.Index(column, row)];
            if (landed) Tween.Punch(landed.transform, enriches ? .34f : .18f, .3f);

            Audio.Sfx(enriches ? "chime" : "pop", .6f, enriches ? 1.25f : .85f);
            if (enriches) Ripple(to, Pal.Cream, _size * 1.9f);

            foreach (var step in result.Steps)
            {
                yield return Detonate(step);
                if (!this) yield break;
            }

            _busy = false;
            Changed?.Invoke();

            if (_board.IsLost) Over?.Invoke(Loc.Get("mode.fall.over"));
        }

        IEnumerator Detonate(FallStep step)
        {
            // The white flash first, so the eye is told what happened before the board changes.
            foreach (int at in step.Taken)
            {
                var img = _motes[at];
                if (!img) continue;

                Tween.Tint(img, Pal.Radiance, .09f);
                Tween.Punch(img.transform, .4f, .26f);
            }

            Audio.Sfx("lit", .7f, Mathf.Min(2.4f, 1f + step.Wave * .18f));
            yield return new WaitForSecondsRealtime(.13f);
            if (!this) yield break;

            foreach (int at in step.Taken)
            {
                var img = _motes[at];
                if (!img) continue;

                Burst.Sparks(_grid, Where(at), Pal.Radiance, 8, 150f, 16f, .5f);
                var going = img;
                Tween.Run(.18f, Ease.OutQuad, t =>
                {
                    if (!going) return;
                    going.transform.localScale = Vector3.one * (1f + t * .5f);
                    var c = going.color; c.a = 1f - t; going.color = c;
                }, going);
            }

            if (step.Wave > 1) Flow.Flash(Pal.A(Pal.Radiance, .5f), .28f, .3f);

            yield return new WaitForSecondsRealtime(.2f);
            if (!this) yield break;

            Repaint();
            yield return new WaitForSecondsRealtime(.12f);
        }

        void Ripple(Vector2 at, Color colour, float size)
        {
            var img = UIKit.Img("Ripple", _grid, Art.Ring(128, 8f), Pal.A(colour, .8f),
                                Vector2.one * size, new Vector2(.5f, .5f), at);
            Tween.Run(.4f, Ease.OutQuint, t =>
            {
                if (!img) return;
                img.transform.localScale = Vector3.one * Mathf.Lerp(.3f, 1.3f, t);
                var c = img.color; c.a = .8f * (1f - t); img.color = c;
            }, img);
            Tween.After(.5f, () => { if (img) Destroy(img.gameObject); });
        }

        // ------------------------------------------------------------------ painting
        void Repaint()
        {
            for (int i = 0; i < _motes.Length; i++)
            {
                int colour = _board.At(i);

                if (colour == Energy.None)
                {
                    if (_motes[i]) { _motes[i].gameObject.SetActive(false); Destroy(_motes[i].gameObject); }
                    _motes[i] = null;
                    continue;
                }

                if (_motes[i] == null)
                {
                    _motes[i] = UIKit.Img("Mote" + i, _grid, Art.Disc(96), Color.white,
                                          Vector2.one * _size, new Vector2(.5f, .5f), Where(i));
                    UIKit.Img("Sheen", _motes[i].transform, Art.Glow(128, 2.4f),
                              new Color(1, 1, 1, .18f), Vector2.one * _size * 1.5f,
                              new Vector2(.5f, .5f), Vector2.zero).transform.SetAsFirstSibling();
                }

                _motes[i].transform.localScale = Vector3.one;
                _motes[i].color = Pal.EnergyColour(colour);
                ((RectTransform)_motes[i].transform).anchoredPosition = Where(i);
            }

            for (int i = 0; i < _queue.Length; i++)
                if (_queue[i]) _queue[i].color = Pal.EnergyColour(_board.Ahead(i));
        }

        /// <summary>The three numbers this mode is read by. Captioned by the screen.</summary>
        public void Readouts(out string left, out string middle, out string right)
        {
            left = _board == null ? "0" : _board.Score.ToString("N0");
            middle = _board == null ? "0" : _board.Best.ToString();
            right = _board == null ? "0" : $"{_board.Tallest}/{_board.Height}";
        }
    }

    /// <summary>
    /// Pointer enter and exit, which <c>Btn</c> does not report.
    ///
    /// It is what lets a column show its ghost while a finger is held over it and take it away
    /// when the finger leaves — on a touch screen that is a drag across the well, which is
    /// exactly how somebody chooses a column.
    /// </summary>
    public sealed class Hover : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler,
        UnityEngine.EventSystems.IPointerDownHandler
    {
        public Action Enter, Exit;

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Exit?.Invoke();
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => Enter?.Invoke();
    }
}
