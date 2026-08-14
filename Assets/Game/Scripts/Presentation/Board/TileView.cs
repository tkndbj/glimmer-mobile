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
        Image _wearRing;
        Text _wearCount;
        int _shownWear = -1;
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
            if (cell.fragile > 0) BuildFragility();

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


        // -------------------------------------------------------------- fragility
        /// <summary>
        /// The turns a fragile conduit has left, shown as a small count on the tile.
        ///
        /// A number rather than a crack texture, because the player has to be able to
        /// <em>plan</em> against it. "This one has two left" is a decision; "this one
        /// looks a bit cracked" is a guess, and a mechanic that costs a heart must never
        /// be a guess.
        /// </summary>
        void BuildFragility()
        {
            _wearRing = UIKit.Img("WearRing", _fixture, Art.Ring(96, 7f), Pal.A(Pal.Ember, .55f),
                                  Vector2.one * _size * .40f, new Vector2(1f, 1f),
                                  new Vector2(-_size * .16f, -_size * .16f));

            _wearCount = UIKit.Titled("Wear", _wearRing.transform, string.Empty, 30, Pal.Cream,
                                      TextAnchor.MiddleCenter, Vector2.one * _size * .34f,
                                      new Vector2(.5f, .5f), Vector2.zero, outline: 0f, shadow: 2f);
            PaintFragility(false);
        }

        void PaintFragility(bool animate)
        {
            if (!_wearRing) return;

            int left = _p.FragileLeft(_i);
            if (_shownWear == left) return;
            _shownWear = left;

            _wearCount.text = left.ToString();

            var tint = left <= 1 ? Pal.Ember : left <= 2 ? Pal.Gold : Pal.Mint;
            _wearRing.color = Pal.A(tint, .75f);
            _wearCount.color = Pal.Lift(tint, .55f);

            if (!animate) return;

            Tween.Punch(_wearRing.transform, .30f, .34f);
            if (left <= 1) Tween.Shake((RectTransform)_wearRing.transform, 4f, .3f);
        }

        /// <summary>
        /// The conduit gives way: it drops out of the board and leaves a gap.
        ///
        /// The tile is not destroyed, only emptied — <see cref="Puzzle.Used"/> already
        /// reports it gone, so nothing downstream needs to know, and keeping the object
        /// means a restart can put it back without rebuilding the grid.
        /// </summary>
        public void Crumble()
        {
            var dust = new Color(.72f, .66f, .58f);

            if (_wearRing) { Tween.Tint(_wearRing, Pal.A(dust, 0f), .25f); }
            if (_wearCount) { Tween.Tint(_wearCount, Pal.A(dust, 0f), .2f); }

            foreach (var arm in _armBase) Tween.Tint(arm, Pal.A(dust, 0f), .34f, Ease.InQuad);
            foreach (var arm in _armLit) Tween.Tint(arm, Pal.A(dust, 0f), .22f, Ease.InQuad);
            foreach (var arm in _armGlow) Tween.Tint(arm, Pal.A(dust, 0f), .22f, Ease.InQuad);

            if (_hubBase) Tween.Tint(_hubBase, Pal.A(dust, 0f), .34f, Ease.InQuad);
            if (_hubLit) Tween.Tint(_hubLit, Pal.A(dust, 0f), .2f, Ease.InQuad);
            if (_hubGlow) Tween.Tint(_hubGlow, Pal.A(dust, 0f), .2f, Ease.InQuad);

            if (_slot) Tween.Tint(_slot, new Color(.06f, .07f, .09f, .30f), .3f);
            if (_slotEdge) Tween.Tint(_slotEdge, new Color(1f, 1f, 1f, .03f), .3f);

            Tween.Shake((RectTransform)transform, 9f, .32f);
            Burst.Sparks(Flow.Effects, WorldCentre(), dust, 16, 190f, 22f, .6f);
        }

        /// <summary>Puts a crumbled conduit back, for a restart.</summary>
        void RestoreFragility()
        {
            if (!_wearRing) return;

            _shownWear = -1;
            _wearRing.color = Pal.A(Pal.Mint, .75f);
            _wearCount.color = Pal.Cream;
            PaintFragility(false);

            if (_slot) _slot.color = _theme.Slot;
            if (_slotEdge) _slotEdge.color = new Color(1, 1, 1, .075f);

            foreach (var arm in _armBase) arm.color = _theme.ArmBase;
            if (_hubBase) _hubBase.color = _theme.Hub;
        }

        /// <summary>
        /// The light going out because the turns ran out.
        ///
        /// Deliberately gentle, and deliberately not the same as a blast: nothing here
        /// was the player's mistake in particular, so the grove goes to sleep rather
        /// than being destroyed. Depth staggers it, so the dark spreads outward from
        /// wherever the light was weakest — the same choreography as waking, run
        /// backwards.
        /// </summary>
        public void Gutter()
        {
            float delay = Mathf.Max(0, _p.Depth[_i]) * .035f;
            var dead = new Color(.34f, .40f, .50f);

            for (int k = 0; k < _armLit.Count; k++)
            {
                Tween.Tint(_armLit[k], Pal.A(dead, 0f), .5f, Ease.InQuad).Delay(delay);
                Tween.Tint(_armGlow[k], Pal.A(dead, 0f), .55f, Ease.InQuad).Delay(delay);
            }

            if (_hubLit) Tween.Tint(_hubLit, Pal.A(dead, 0f), .5f, Ease.InQuad).Delay(delay);
            if (_hubGlow) Tween.Tint(_hubGlow, Pal.A(dead, 0f), .55f, Ease.InQuad).Delay(delay);

            if (_critter) Tween.Tint(_critter, SleepTint, .5f, Ease.InQuad).Delay(delay);
            if (_haloGlow) Tween.Tint(_haloGlow, Pal.A(dead, 0f), .5f, Ease.InQuad).Delay(delay);
            if (_book) _book.enabled = false;

            Tween.Punch(_fixture, .05f, .35f).Delay(delay);
        }



        /// <summary>This tile's centre in the effects layer's space, for a burst.</summary>
        Vector2 WorldCentre()
        {
            var rt = (RectTransform)transform;
            var world = rt.TransformPoint(rt.rect.center);
            return Flow.Effects.InverseTransformPoint(world);
        }

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

        /// <summary>
        /// Snap back to a given rotation, used when the level is restarted. Also mends
        /// a crumbled conduit — a retry rewinds the model, and the view has to follow
        /// it all the way.
        /// </summary>
        public void ResetTo(int rot)
        {
            _angle = -90f * rot;
            Tween.Rotate(_rotor, _angle, .34f, Ease.OutBack);
            Tween.Punch(transform, .12f, .36f);
            _shownEnergy = -1;
            RestoreFragility();
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

            PaintFragility(animate);
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
