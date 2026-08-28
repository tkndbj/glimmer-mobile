using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>Grovekeeper.</b> Lay tiles of light so that unlike edges meet. There is no clock and no
    /// fail — only the grove you made with the tiles you were given.
    ///
    /// <para>
    /// Every open spot shows what placing there would be worth <em>before</em> it is placed: the
    /// seams it would light and whether it would bloom. That preview is the game. Without it the
    /// player is guessing, and a cozy builder that punishes guessing is not cozy.
    /// </para>
    /// </summary>
    public sealed class KeeperView : MonoBehaviour
    {
        public Action Changed { get; set; }
        public Action<string> Over { get; set; }

        /// <summary>
        /// The run has not been allowed to begin yet. Written only by the screen, from
        /// <c>RunScreen.Running</c> — see <c>RunHold</c> for why a run has to be let go rather
        /// than simply built.
        /// </summary>
        public bool Held { get; set; } = true;

        KeeperBoard _board;
        RectTransform _host, _grid, _tray;
        Image[] _tiles;
        Image[] _openings;
        Image[] _queue;

        float _cell, _size;
        Vector2 _origin;

        public void Begin(RectTransform host, int width, int height, int tiles, uint seed)
        {
            _host = host;
            Held = true;
            _board = new KeeperBoard(Mathf.Clamp(width, 5, 11),
                                     Mathf.Clamp(height, 5, 11),
                                     tiles > 0 ? tiles : 30, seed);

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var rect = host.rect;
            float trayH = 150f;
            _cell = Mathf.Min(rect.width / _board.Width, (rect.height - trayH) / _board.Height);
            _size = _cell * .92f;

            _grid = UIKit.Node("Grove", host);
            UIKit.StretchTo(_grid, 0, trayH, 0, 0);

            _origin = new Vector2(-(_board.Width - 1) * _cell * .5f,
                                  (_board.Height - 1) * _cell * .5f);

            _tiles = new Image[_board.Width * _board.Height];
            _openings = new Image[_board.Width * _board.Height];

            BuildCells();
            BuildTray(host, trayH);
            Repaint();
        }

        Vector2 Where(int index)
        {
            int x = index % _board.Width, y = index / _board.Width;
            return _origin + new Vector2(x * _cell, -y * _cell);
        }

        void BuildCells()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                int index = i;
                var root = UIKit.Box("Cell" + i, _grid, Vector2.one * _cell,
                                     new Vector2(.5f, .5f), Where(i));

                var hit = root.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                hit.raycastTarget = true;

                // The dashed ghost of an open spot. Drawn under everything, so a placed tile
                // simply covers it rather than needing it hidden.
                _openings[i] = UIKit.Img("Open", root, Art.RoundOutline(18, 3f),
                                         new Color(1, 1, 1, 0f), Vector2.one * _size * .78f,
                                         new Vector2(.5f, .5f), Vector2.zero);

                _tiles[i] = UIKit.Img("Tile", root, Art.Round(18), new Color(1, 1, 1, 0f),
                                      Vector2.one * _size, new Vector2(.5f, .5f), Vector2.zero);

                var btn = root.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Place(index), silent: true);

                var hover = root.gameObject.AddComponent<Hover>();
                hover.Enter = () => Preview(index);
                hover.Exit = ClearPreview;
            }
        }

        void BuildTray(RectTransform host, float trayH)
        {
            _tray = UIKit.Box("Tray", host, new Vector2(0f, trayH), new Vector2(.5f, 0f),
                              new Vector2(0f, trayH * .5f));
            _tray.anchorMin = new Vector2(0f, 0f);
            _tray.anchorMax = new Vector2(1f, 0f);
            _tray.sizeDelta = new Vector2(0f, trayH);

            var plate = UIKit.Img("Plate", _tray, Art.Round(26), new Color(.05f, .09f, .09f, .70f),
                                  new Vector2(430f, 118f), new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Edge", plate.transform, Art.RoundOutline(26, 3f), new Color(1, 1, 1, .12f),
                      new Vector2(430f, 118f), new Vector2(.5f, .5f), Vector2.zero);

            _queue = new Image[KeeperBoard.Lookahead];
            for (int i = 0; i < _queue.Length; i++)
            {
                bool next = i == 0;
                float size = next ? 82f : 48f;
                float x = next ? -140f : -20f + (i - 1) * 74f;

                var seat = UIKit.Img("Seat" + i, plate.transform, Art.RoundOutline(20, 4f),
                                     new Color(1, 1, 1, next ? .24f : .10f),
                                     Vector2.one * (size + 16f), new Vector2(.5f, .5f),
                                     new Vector2(x, 0f));

                _queue[i] = UIKit.Img("Tile" + i, seat.transform, Art.Round(16), Color.white,
                                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
                if (next) Tween.Breathe(_queue[i].transform, .05f, 1.8f);
            }
        }

        // ------------------------------------------------------------------ preview
        readonly List<GameObject> _hints = new List<GameObject>();

        void ClearPreview()
        {
            foreach (var go in _hints) if (go) { go.SetActive(false); Destroy(go); }
            _hints.Clear();
        }

        /// <summary>
        /// Shows what this spot would make: a seam mark toward every unlike neighbour, and a ring
        /// if it would bloom. Nothing numeric — the point is to be read at a glance.
        /// </summary>
        void Preview(int index)
        {
            ClearPreview();
            if (!_board.CanPlace(index)) return;

            var gain = _board.Preview(index);
            int colour = _board.Next;
            var at = Where(index);

            var ghost = UIKit.Img("Ghost", _grid, Art.Round(18), Pal.A(Pal.EnergyColour(colour), .45f),
                                  Vector2.one * _size, new Vector2(.5f, .5f), at);
            _hints.Add(ghost.gameObject);

            int x = index % _board.Width, y = index / _board.Width;
            for (int n = 0; n < Steps.Length; n++)
            {
                int nx = x + Steps[n].dx, ny = y + Steps[n].dy;
                if (!_board.Inside(nx, ny)) continue;

                int mate = _board.At(nx, ny);
                if (mate == Energy.None || mate == colour) continue;

                var mid = (at + Where(_board.Index(nx, ny))) * .5f;
                var seam = UIKit.Img("Seam", _grid, Art.Disc(64),
                                     Pal.A(Pal.EnergyColour(mate | colour), .95f),
                                     Vector2.one * _size * .3f, new Vector2(.5f, .5f), mid);
                _hints.Add(seam.gameObject);
                Tween.Breathe(seam.transform, .16f, .9f);
            }

            if (gain.Bloom)
            {
                var ring = UIKit.Img("Bloom", _grid, Art.Ring(128, 8f), Pal.A(Pal.Radiance, .9f),
                                     Vector2.one * _size * 1.5f, new Vector2(.5f, .5f), at);
                _hints.Add(ring.gameObject);
                Tween.Breathe(ring.transform, .1f, .8f);
            }
        }

        static readonly (int dx, int dy)[] Steps = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        // ------------------------------------------------------------------ placing
        void Place(int index)
        {
            if (Held) return;
            if (!_board.CanPlace(index)) { Audio.Sfx("blocked", .45f); return; }

            int colour = _board.Next;
            var gain = _board.Place(index);
            ClearPreview();
            Repaint();

            var at = Where(index);
            var tile = _tiles[index];
            if (tile)
            {
                tile.transform.localScale = Vector3.zero;
                Tween.Pop(tile.transform, 0f, .32f);
            }

            for (int n = 0; n < Steps.Length; n++)
            {
                int x = index % _board.Width + Steps[n].dx;
                int y = index / _board.Width + Steps[n].dy;
                if (!_board.Inside(x, y)) continue;

                int mate = _board.At(x, y);
                if (mate == Energy.None || mate == colour) continue;

                Seam((at + Where(_board.Index(x, y))) * .5f, mate | colour);
            }

            if (gain.Bloom)
            {
                Burst.Sparks(_grid, at, Pal.Radiance, 16, 210f, 22f, .7f);
                Flow.Flash(Pal.A(Pal.Radiance, .45f), .3f, .34f);
                Audio.Sfx("star", .85f);
            }
            else
            {
                Audio.Sfx(gain.Seams > 0 ? "chime" : "pop", .55f,
                          1f + gain.Seams * .12f);
            }

            Changed?.Invoke();
            if (_board.IsDone) Over?.Invoke(Loc.Format("mode.keeper.over", _board.Score));
        }

        /// <summary>A seam blooming into being between two unlike tiles.</summary>
        void Seam(Vector2 at, int colour)
        {
            var img = UIKit.Img("Seam", _grid, Art.Disc(64), Pal.A(Pal.EnergyColour(colour), 1f),
                                Vector2.one * _size * .34f, new Vector2(.5f, .5f), at);

            Tween.Run(.5f, Ease.OutQuint, t =>
            {
                if (!img) return;
                img.transform.localScale = Vector3.one * Mathf.Lerp(.2f, 1f, t);
                var c = img.color; c.a = 1f - t * .35f; img.color = c;
            }, img);

            var ring = UIKit.Img("SeamRing", _grid, Art.Ring(96, 6f),
                                 Pal.A(Pal.EnergyColour(colour), .8f),
                                 Vector2.one * _size, new Vector2(.5f, .5f), at);
            Tween.Run(.42f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.25f, 1.2f, t);
                var c = ring.color; c.a = .8f * (1f - t); ring.color = c;
            }, ring);
            Tween.After(.6f, () => { if (ring) Destroy(ring.gameObject); });
        }

        void Repaint()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                int colour = _board.At(i);

                if (_tiles[i])
                    _tiles[i].color = colour == Energy.None
                        ? new Color(1, 1, 1, 0f)
                        : Pal.EnergyColour(colour);

                if (_openings[i])
                    _openings[i].color = _board.CanPlace(i)
                        ? new Color(1f, 1f, 1f, .16f)
                        : new Color(1, 1, 1, 0f);

                if (_board.IsBloomed(i) && _tiles[i] != null
                    && _tiles[i].transform.childCount == 0)
                {
                    UIKit.Img("Crown", _tiles[i].transform, Art.Ring(96, 6f),
                              Pal.A(Pal.Radiance, .95f), Vector2.one * _size * 1.14f,
                              new Vector2(.5f, .5f), Vector2.zero);
                }
            }

            for (int i = 0; i < _queue.Length; i++)
                if (_queue[i]) _queue[i].color = Pal.EnergyColour(_board.Ahead(i));
        }

        /// <summary>The three numbers this mode is read by. Captioned by the screen.</summary>
        public void Readouts(out string left, out string middle, out string right)
        {
            left = _board == null ? "0" : _board.Score.ToString("N0");
            middle = _board == null ? "0" : _board.Blooms.ToString();
            right = _board == null ? "0" : _board.Left.ToString();
        }
    }
}
