using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>One cell of the grove: a turnable conduit, a heart-crystal or a sleeping critter.</summary>
    public sealed class TileView : MonoBehaviour, IPointerClickHandler
    {
        BoardView _board;
        Puzzle _p;
        int _i;
        float _size;
        float _angle;

        Image _slot, _slotEdge;
        RectTransform _rotor, _fixture;
        readonly List<Image> _armBase = new List<Image>(4);
        readonly List<Image> _armLit = new List<Image>(4);
        readonly List<Image> _armGlow = new List<Image>(4);
        Image _hubBase, _hubLit, _hubGlow;

        Image _crystal, _crystalGlow;
        Image _critter, _halo, _haloGlow, _lockBadge;
        Flipbook _book;

        bool _wasLit;
        int _shownEnergy = -1;
        Pal.BoardTheme _theme;

        public int Index => _i;
        public bool IsLamp => _p.C[_i].kind == Kind.Lamp;
        public bool Lit => _p.Lit[_i];

        // ------------------------------------------------------------------ build
        public void Build(BoardView board, Puzzle p, int index, float size, Pal.BoardTheme theme)
        {
            _board = board; _p = p; _i = index; _size = size; _theme = theme;
            var cell = p.C[index];
            var rt = (RectTransform)transform;

            _slot = gameObject.AddComponent<Image>();
            _slot.sprite = Art.Round(22);
            _slot.type = Image.Type.Sliced;
            _slot.color = cell.locked ? new Color(1f, .86f, .55f, .10f) : _theme.Slot;
            _slot.raycastTarget = true;

            _slotEdge = UIKit.Img("Edge", rt, Art.RoundOutline(22, 3f), new Color(1, 1, 1, .075f));
            UIKit.StretchTo((RectTransform)_slotEdge.transform, 0, 0, 0, 0);

            _rotor = UIKit.Node("Rotor", rt);
            _fixture = UIKit.Node("Fixture", rt);

            float thick = Mathf.Round(size * .175f);
            float glowThick = thick * 3.0f;
            float reach = size * .5f + 1f;

            // arms are authored against the solved mask; the rotor maps them to play space
            for (int d = 0; d < 4; d++)
            {
                if ((cell.solved & Puzzle.Bits[d]) == 0) continue;
                float z = -90f * d;

                var glow = MakeArm("ArmGlow", Art.SoftCapsule(40, 120), glowThick, reach * 1.02f, z,
                                   Pal.A(Pal.Dormant, 0f));
                var base_ = MakeArm("ArmBase", Art.Capsule(24, 96), thick, reach, z, _theme.ArmBase);
                var lit = MakeArm("ArmLit", Art.Capsule(24, 96), thick * .74f, reach, z,
                                  Pal.A(Pal.Dormant, 0f));

                _armGlow.Add(glow); _armBase.Add(base_); _armLit.Add(lit);
            }

            float hub = thick * 1.72f;
            _hubGlow = UIKit.Img("HubGlow", _rotor, Art.Glow(96, 1.9f), Pal.A(Pal.Dormant, 0f),
                                 Vector2.one * hub * 3.1f, new Vector2(.5f, .5f), Vector2.zero);
            _hubBase = UIKit.Img("Hub", _rotor, Art.Disc(96), _theme.Hub,
                                 Vector2.one * hub, new Vector2(.5f, .5f), Vector2.zero);
            _hubLit = UIKit.Img("HubLit", _rotor, Art.Disc(96), Pal.A(Pal.Dormant, 0f),
                                Vector2.one * hub * .62f, new Vector2(.5f, .5f), Vector2.zero);

            if (cell.kind == Kind.Source) BuildCrystal(cell);
            else if (cell.kind == Kind.Lamp) BuildCritter(cell);

            if (cell.locked)
            {
                _lockBadge = UIKit.Img("Lock", _fixture, Art.S("Ui/padlock"), new Color(1f, .92f, .72f, .85f),
                                       Vector2.one * size * .3f, new Vector2(1f, 0f),
                                       new Vector2(-size * .19f, size * .19f));
                _lockBadge.preserveAspect = true;
            }

            _angle = -90f * cell.rot;
            _rotor.localRotation = Quaternion.Euler(0, 0, _angle);
            ApplyEnergy(false);
        }

        Image MakeArm(string name, Sprite sprite, float thickness, float length, float z, Color colour)
        {
            var img = UIKit.Img(name, _rotor, sprite, colour);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.pivot = new Vector2(.5f, 0f);
            rt.sizeDelta = new Vector2(thickness, length);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0, 0, z);
            return img;
        }

        void BuildCrystal(Cell cell)
        {
            var col = Pal.EnergyColour(cell.colour);
            _crystalGlow = UIKit.Img("SourceGlow", _fixture, Art.Glow(128, 2f), Pal.A(col, .45f),
                                     Vector2.one * _size * 1.22f, new Vector2(.5f, .5f), Vector2.zero);
            _crystal = UIKit.Img("Crystal", _fixture, Art.Crystal(128), Pal.Lift(col, .45f),
                                 Vector2.one * _size * .46f, new Vector2(.5f, .5f), Vector2.zero);
            var rim = UIKit.Img("Rim", _fixture, Art.Crystal(128), new Color(.09f, .15f, .21f, .85f),
                                Vector2.one * _size * .56f, new Vector2(.5f, .5f), Vector2.zero);
            rim.transform.SetSiblingIndex(_crystal.transform.GetSiblingIndex());
            var inner = UIKit.Img("Core", _crystal.transform, Art.Crystal(128), Color.white);
            UIKit.StretchTo((RectTransform)inner.transform, _size * .12f, _size * .12f, _size * .12f, _size * .12f);
            inner.color = new Color(1f, 1f, 1f, .8f);

            Tween.Breathe(_crystal.transform, .075f, 2.1f, Random.value * 6f);
            Tween.Run(2.6f, Ease.InOutSine, t =>
            {
                if (!_crystalGlow) return;
                _crystalGlow.color = Pal.A(col, Mathf.Lerp(.30f, .60f, t));
                _crystalGlow.transform.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, t);
            }, _crystalGlow, "pulse").Loop(-1, true);
        }

        void BuildCritter(Cell cell)
        {
            var want = cell.colour == Pal.Any ? Pal.Cream : Pal.EnergyColour(cell.colour);

            _haloGlow = UIKit.Img("HaloGlow", _fixture, Art.Glow(128, 1.9f), Pal.A(want, 0f),
                                  Vector2.one * _size * 1.45f, new Vector2(.5f, .5f), Vector2.zero);
            _halo = UIKit.Img("Halo", _fixture, Art.Ring(128, 9f), Pal.A(want, .55f),
                              Vector2.one * _size * .82f, new Vector2(.5f, .5f), new Vector2(0, -_size * .02f));

            var frames = Art.Frames("Critters/c" + (cell.critter + 1));
            _critter = UIKit.Img("Critter", _fixture, frames != null && frames.Length > 0 ? frames[0] : null,
                                 SleepTint, Vector2.one * _size * .68f, new Vector2(.5f, .5f),
                                 new Vector2(0, _size * .03f));
            _critter.preserveAspect = true;
            _book = Flipbook.Attach(_critter, "Critters/c" + (cell.critter + 1), 16f);
            _book.Offset = Random.Range(0, 20);
            _book.enabled = false;
        }

        static readonly Color SleepTint = new Color(.44f, .52f, .60f, .92f);

        // ---------------------------------------------------------------- input
        public void OnPointerClick(PointerEventData e) => _board.OnTileTapped(this);

        // ------------------------------------------------------------ animation
        public void Spin(int direction)
        {
            _angle += -90f * direction;
            Tween.Rotate(_rotor, _angle, .27f, Ease.OutBack);
            Tween.Punch(_fixture, .1f, .3f);
            Ripple(Pal.A(Pal.Cream, .5f), .85f);
        }

        /// <summary>Snap back to a given rotation, used when the level is restarted.</summary>
        public void ResetTo(int rot)
        {
            _angle = -90f * rot;
            Tween.Rotate(_rotor, _angle, .34f, Ease.OutBack);
            Tween.Punch(transform, .12f, .36f);
            _shownEnergy = -1;
        }

        public void Refuse()
        {
            Tween.Shake((RectTransform)transform, 7f, .3f);
            if (_lockBadge) Tween.Punch(_lockBadge.transform, .35f, .35f);
            Ripple(Pal.A(Pal.Rose, .55f), .8f);
        }

        void Ripple(Color colour, float scale)
        {
            var img = UIKit.Img("Ripple", transform, Art.RoundOutline(22, 5f), colour);
            UIKit.StretchTo((RectTransform)img.transform, 0, 0, 0, 0);
            var rt = (RectTransform)img.transform;
            Tween.Run(.42f, Ease.OutCubic, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.86f, 1f + scale * .28f, t);
                var c = img.color; c.a = colour.a * (1f - t); img.color = c;
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>Hint pulse: a bright ring that draws the eye to a tile.</summary>
        public void Beckon(Color colour)
        {
            for (int k = 0; k < 3; k++)
            {
                float delay = k * .16f;
                Tween.After(delay, () => { if (this) Ripple(Pal.A(colour, .9f), 1.5f); }, this);
            }
            Tween.Punch(transform, .14f, .45f);
        }

        // --------------------------------------------------------------- energy
        public void ApplyEnergy(bool animate, float extraDelay = 0f)
        {
            int energy = _p.Energy(_i);
            int depth = Mathf.Max(0, _p.Depth[_i]);
            bool lit = energy != 0;
            var col = Pal.EnergyColour(energy);

            float delay = animate ? extraDelay + depth * .028f : 0f;
            float dur = animate ? .3f : .001f;

            if (_shownEnergy != energy)
            {
                _shownEnergy = energy;
                for (int k = 0; k < _armLit.Count; k++)
                {
                    var lay = _armLit[k]; var glow = _armGlow[k];
                    Tween.Tint(lay, lit ? Pal.A(Pal.Lift(col, .35f), 1f) : Pal.A(col, 0f), dur, Ease.OutQuad).Delay(delay);
                    Tween.Tint(glow, lit ? Pal.A(col, .5f) : Pal.A(col, 0f), dur * 1.4f, Ease.OutQuad).Delay(delay);
                }
                Tween.Tint(_hubLit, lit ? Pal.A(Pal.Lift(col, .55f), 1f) : Pal.A(col, 0f), dur, Ease.OutQuad).Delay(delay);
                Tween.Tint(_hubGlow, lit ? Pal.A(col, .62f) : Pal.A(col, 0f), dur * 1.4f, Ease.OutQuad).Delay(delay);
            }

            if (_p.C[_i].kind == Kind.Lamp) ApplyLamp(animate, delay);
        }

        void ApplyLamp(bool animate, float delay)
        {
            bool lit = _p.Lit[_i];
            if (lit == _wasLit && animate) return;
            _wasLit = lit;

            var want = _p.C[_i].colour == Pal.Any ? Pal.Cream : Pal.EnergyColour(_p.C[_i].colour);

            void Apply()
            {
                if (this == null || _critter == null) return;
                if (_book) _book.enabled = lit;
                if (!lit && _book) _critter.sprite = Art.Frames("Critters/c" + (_p.C[_i].critter + 1))[0];

                Tween.Tint(_critter, lit ? Color.white : SleepTint, .28f);
                Tween.Tint(_halo, Pal.A(want, lit ? 1f : .5f), .28f);
                Tween.Tint(_haloGlow, Pal.A(want, lit ? .78f : 0f), .34f);

                if (lit)
                {
                    Tween.Punch(_critter.transform, .3f, .5f);
                    Tween.Punch(_halo.transform, .22f, .45f);
                    Burst.Sparks(_fixture, Vector2.zero, want, 10, _size * 1.15f, _size * .2f, .55f);
                    Tween.Run(2.0f, Ease.InOutSine, t =>
                    {
                        if (!_haloGlow) return;
                        _haloGlow.color = Pal.A(want, Mathf.Lerp(.42f, .85f, t));
                    }, _haloGlow, "lampglow").Loop(-1, true);
                }
                else
                {
                    Tween.KillChannel(_haloGlow, "lampglow");
                }
            }

            if (animate && delay > 0f) Tween.After(delay, Apply, this);
            else Apply();
        }

        /// <summary>Slow travelling shimmer so live conduits feel like flowing light.</summary>
        void Update()
        {
            if (_shownEnergy <= 0 || _armLit.Count == 0) return;
            float phase = Time.unscaledTime * 2.6f - Mathf.Max(0, _p.Depth[_i]) * .55f;
            float k = .84f + .16f * Mathf.Sin(phase);
            for (int i = 0; i < _armLit.Count; i++)
            {
                if (!_armLit[i]) continue;
                var rt = (RectTransform)_armLit[i].transform;
                var s = rt.localScale; s.x = k; rt.localScale = s;
            }
            if (_hubLit) _hubLit.transform.localScale = Vector3.one * (.92f + .12f * Mathf.Sin(phase));
        }

        /// <summary>Victory sweep: everything flares once, ordered by distance.</summary>
        public void Flare(float delay)
        {
            var col = Pal.EnergyColour(Mathf.Max(1, _p.Energy(_i)));
            Tween.After(delay, () =>
            {
                if (this == null) return;
                Ripple(Pal.A(Pal.Lift(col, .5f), .95f), 1.1f);
                Tween.Punch(_rotor, .16f, .4f);
                for (int i = 0; i < _armGlow.Count; i++)
                {
                    var g = _armGlow[i];
                    if (!g) continue;
                    var home = g.color;
                    Tween.Run(.55f, Ease.OutCubic, t => { if (g) g.color = Color.Lerp(Pal.A(Pal.Lift(col, .6f), .95f), home, t); }, g);
                }
            }, this);
        }
    }
}
