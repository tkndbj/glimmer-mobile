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

        /// <summary>
        /// Which of the cell's strands each arm belongs to, in the order the arms were built.
        ///
        /// Always 0 off a crossing, which is what lets one painting routine serve both kinds
        /// of tile — a crossing is the only tile that can be answering two colours at once,
        /// and it answers them along arms that were already being tinted one at a time.
        /// </summary>
        readonly List<int> _armStrand = new List<int>(4);

        /// <summary>The dark backing under a crossing's over-strand. Empty on every other tile.</summary>
        readonly List<Image> _armShade = new List<Image>(2);

        /// <summary>
        /// Each arm's resting base colour, in the order the arms were built.
        ///
        /// One per arm rather than one per tile because a briar's thorned arms rest darker
        /// than its open ones — and <see cref="RestoreFragility"/> puts the base colours back
        /// after a restart, which without this would quietly repaint a closed way as an open
        /// one and leave the tile drawing a rule it does not follow.
        /// </summary>
        readonly List<Color> _armRest = new List<Color>(4);

        /// <summary>The thorns across a briar's two closed ways. Empty on every other tile.</summary>
        readonly List<Image> _thorns = new List<Image>(2);

        Image _hubBase, _hubLit, _hubGlow;

        Image _crystal, _crystalGlow;
        Image _critter, _halo, _haloGlow, _lockBadge;
        Image _wearRing;
        Text _wearCount;
        int _shownWear = -1;
        Flipbook _book;

        Image _rootRing;

        bool _wasLit;
        int _shownEnergy = -1;

        /// <summary>Energy the halo was last painted for. -1 until it has been.</summary>
        int _shownHalo = -1;

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

            // Arms are authored against the solved mask; the rotor maps them to play space.
            //
            // On a crossing they are built under-strand first, so the other pair draws across
            // it — and the tile deliberately gets no hub. The hub disc is what this board means
            // by "these arms are joined", so leaving it off is not a decoration missing, it is
            // the rule stated in the vocabulary the player already reads.
            float hub = thick * 1.72f;
            bool crossing = cell.kind == Kind.Crossing;
            bool briar = cell.kind == Kind.Briar;

            foreach (int d in ArmOrder(cell))
            {
                float z = -90f * d;

                // Strand 1 means two different things on the two four-armed tiles, and one
                // line serves both because the painting only ever asks it one question: what
                // energy does this arm carry? On a crossing it is the second flow. On a briar
                // there is no second flow, so a closed way is put on a strand the tile does
                // not have — and Puzzle.EnergyOn answers 0 for a strand that does not exist,
                // which is exactly the right answer for a way with thorns across it. Nothing
                // below needs to know which of the two it is drawing.
                int shutBy = crossing ? cell.cross : briar ? cell.gate : 0;
                int strand = shutBy != 0 && (shutBy & Puzzle.Bits[d]) == 0 ? 1 : 0;

                // The strand that crosses on top is drawn a little thicker and rimmed in
                // shadow along its whole length. A dark patch at the junction alone is not
                // enough: it says "not a junction", which the missing hub already says, and
                // leaves the two pairs looking equal. A rim that runs the length of the arm
                // is what makes one of them read as being in front, and it is the only part
                // of this tile that survives being glanced at.
                if (crossing && strand == 0)
                    _armShade.Add(MakeArm("ArmShade", Art.Capsule(24, 96), thick * 1.62f, reach, z,
                                          ShadeTint));

                float armThick = crossing && strand == 0 ? thick * 1.1f : thick;

                // A thorned way rests darker than an open one, and the thorns are drawn on it
                // besides. Two statements rather than one because either alone is ambiguous:
                // an unlit arm is already dark on every tile on the board, and a mark on a
                // fully bright arm reads as decoration hung on a working conduit.
                var rest = briar && strand == 1
                    ? Color.Lerp(_theme.ArmBase, Pal.Slate, .55f)
                    : _theme.ArmBase;

                var glow = MakeArm("ArmGlow", Art.SoftCapsule(40, 120), glowThick, reach * 1.02f, z,
                                   Pal.A(Pal.Dormant, 0f));
                var base_ = MakeArm("ArmBase", Art.Capsule(24, 96), armThick, reach, z, rest);
                var lit = MakeArm("ArmLit", Art.Capsule(24, 96), armThick * .74f, reach, z,
                                  Pal.A(Pal.Dormant, 0f));

                _armGlow.Add(glow); _armBase.Add(base_); _armLit.Add(lit); _armStrand.Add(strand);
                _armRest.Add(rest);

                if (briar && strand == 1) BuildThorn(d, thick);
            }

            if (!crossing)
            {
                _hubGlow = UIKit.Img("HubGlow", _rotor, Art.Glow(96, 1.9f), Pal.A(Pal.Dormant, 0f),
                                     Vector2.one * hub * 3.1f, new Vector2(.5f, .5f), Vector2.zero);
                _hubBase = UIKit.Img("Hub", _rotor, Art.Disc(96), _theme.Hub,
                                     Vector2.one * hub, new Vector2(.5f, .5f), Vector2.zero);
                _hubLit = UIKit.Img("HubLit", _rotor, Art.Disc(96), Pal.A(Pal.Dormant, 0f),
                                    Vector2.one * hub * .62f, new Vector2(.5f, .5f), Vector2.zero);
            }

            if (cell.kind == Kind.Source) BuildCrystal(cell);
            else if (cell.kind == Kind.Lamp) BuildCritter(cell);
            if (cell.fragile > 0) BuildFragility();
            if (_p.IsBound(_i)) BuildTaproot(cell);

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

        /// <summary>
        /// The directions this tile carries an arm in, in the order they should be drawn.
        ///
        /// Only a crossing has an opinion: its under-strand goes down first so the other pair
        /// draws over the top of it. Everything else is simply north to west, exactly as it
        /// always was — the ordering is a fact about one tile, so it lives in one method
        /// rather than as a branch inside the build loop.
        /// </summary>
        static IEnumerable<int> ArmOrder(Cell cell)
        {
            // The two four-armed tiles both name one pair and draw it last, for opposite
            // reasons that happen to want the same order: a crossing's named strand is the one
            // passing over, and a briar's named pair is the one still open, which has to draw
            // across the thorns rather than under them.
            int last = cell.kind == Kind.Crossing ? cell.cross
                     : cell.kind == Kind.Briar ? cell.gate
                     : 0;

            if (last != 0)
            {
                for (int d = 0; d < 4; d++)
                    if ((cell.solved & Puzzle.Bits[d]) != 0 && (last & Puzzle.Bits[d]) == 0) yield return d;

                for (int d = 0; d < 4; d++)
                    if ((last & Puzzle.Bits[d]) != 0) yield return d;

                yield break;
            }

            for (int d = 0; d < 4; d++)
                if ((cell.solved & Puzzle.Bits[d]) != 0) yield return d;
        }

        /// <summary>
        /// The thorns laid across one of a briar's closed ways.
        ///
        /// <para>
        /// On the rotor rather than the fixture, which is the whole drawing: a tap turns the
        /// tile and the thorns visibly sweep off the two ways they were closing and onto the
        /// other two. Nothing else on this board moves an obstacle, so the rule shows itself
        /// on the first tile the player tries — which is what the lesson can only describe.
        /// </para>
        /// <para>
        /// Set out along the arm rather than at the hub. A mark at the centre would sit where
        /// four ways meet and say nothing about which of them it closed; out on the arm it is
        /// unambiguously across <em>that</em> way, and it leaves the hub free to light, which
        /// it does, because a briar is a junction on the pair that is open.
        /// </para>
        /// </summary>
        void BuildThorn(int d, float thick)
        {
            var dir = Quaternion.Euler(0, 0, -90f * d) * Vector3.up;

            var img = UIKit.Img($"Thorn{d}", _rotor, Art.Thorn(64), Pal.A(Pal.Thorn, .95f),
                                Vector2.one * thick * 1.85f, new Vector2(.5f, .5f),
                                new Vector2(dir.x, dir.y) * _size * .33f);
            img.transform.localRotation = Quaternion.Euler(0, 0, -90f * d);
            img.raycastTarget = false;
            _thorns.Add(img);
        }

        /// <summary>The overpass shadow: dark enough to read as depth, not as a second colour.</summary>
        static readonly Color ShadeTint = new Color(.04f, .06f, .09f, .72f);

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
            var want = HaloColour();

            _haloGlow = UIKit.Img("HaloGlow", _fixture, Art.Glow(128, 1.9f), Pal.A(want, 0f),
                                  Vector2.one * _size * 1.45f, new Vector2(.5f, .5f), Vector2.zero);
            _halo = UIKit.Img("Halo", _fixture, HaloSprite(), Pal.A(want, SleepingHalo),
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

        /// <summary>
        /// How strongly a sleeping critter's ring states the colour it is waiting for.
        ///
        /// <para>
        /// It is the board's only standing instruction — a player reads every demand off these
        /// rings before touching a conduit — and at the .55 it shipped at, it was reported as
        /// hard to pick out against a graded backdrop. Lifted rather than recoloured: the hue
        /// is the answer and must not drift, so the only thing that may move is how loudly it
        /// is said. It stays well under the lit ring's full alpha, because the gap between the
        /// two is what separates "wanted" from "fed" at a glance.
        /// </para>
        /// <para>
        /// Named here because it is asserted twice — once when the ring is built and once
        /// every time the lamp is repainted — and two numbers that have to agree is the shape
        /// this project keeps paying for.
        /// </para>
        /// </summary>
        const float SleepingHalo = .72f;

        /// <summary>
        /// What a critter's halo says, which is two different things.
        ///
        /// <para>
        /// A fussy critter wears the colour it wants, lit or not, so its ring is a standing
        /// demand the player can read off the board before touching anything. A critter that
        /// accepts <see cref="Pal.Any"/> has no demand to state, so while it sleeps it wears
        /// the three channels at once (<see cref="Art.PrismRing"/>) and once it wakes it
        /// takes <em>the colour that actually reached it</em>.
        /// </para>
        /// <para>
        /// That second half is the part that teaches. The ring used to stay cream whatever
        /// fed it, which made cream look like a fifth colour rather than the absence of one
        /// — invisible while every critter on a board was unfussy, and confusing on the
        /// first board where one sat beside a fussy neighbour. Letting it take the colour it
        /// was given means the rule is demonstrated by the first conduit the player turns,
        /// in any language, with nothing to read.
        /// </para>
        /// </summary>
        bool AcceptsAnyColour => _p.C[_i].colour == Pal.Any;

        Color HaloColour()
        {
            if (!AcceptsAnyColour) return Pal.EnergyColour(_p.C[_i].colour);

            int reaching = _p.Energy(_i);
            return reaching != 0 ? Pal.EnergyColour(reaching) : Pal.Cream;
        }

        Sprite HaloSprite()
            => AcceptsAnyColour && _p.Energy(_i) == 0 ? Art.PrismRing(128, 9f) : Art.Ring(128, 9f);

        // -------------------------------------------------------------- taproot
        /// <summary>
        /// The mark on a conduit that shares a taproot: a rope-coloured rim around the whole
        /// tile, and one pip per root along the bottom of it.
        ///
        /// <para>
        /// Rope rather than a hue, and the same rope for every root — see <see cref="Pal.Rope"/>.
        /// Identity is carried by the pips instead, which is slower to read than a colour
        /// and is meant to be: the fast answer is tapping the tile and watching its partners
        /// move, and the pips are for planning the turn before spending it.
        /// </para>
        /// </summary>
        void BuildTaproot(Cell cell)
        {
            _rootRing = UIKit.Img("Root", transform, Art.RoundOutline(22, 5f), Pal.A(Pal.Rope, .62f));
            UIKit.StretchTo((RectTransform)_rootRing.transform, -3, -3, -3, -3);
            _rootRing.raycastTarget = false;
            _rootRing.transform.SetSiblingIndex(1);

            int pips = Mathf.Clamp(cell.link, 1, Puzzle.MaxReadableRunes);
            float pip = Mathf.Round(_size * .085f);
            float step = pip * 1.7f;

            for (int k = 0; k < pips; k++)
            {
                float x = (k - (pips - 1) * .5f) * step;
                var dot = UIKit.Img($"Pip{k}", _fixture, Art.Disc(32), Pal.A(Pal.Rope, .95f),
                                    Vector2.one * pip, new Vector2(.5f, 0f),
                                    new Vector2(x, _size * .10f));
                dot.raycastTarget = false;
            }
        }

        /// <summary>
        /// Called on every conduit sharing a taproot with one the player just tapped.
        /// Nothing else on the board answers a tap somewhere else, which is exactly why it
        /// reads immediately as "these are the same tile".
        /// </summary>
        public void RootPulse()
        {
            if (_rootRing) Tween.Punch(_rootRing.transform, .09f, .34f);
            Ripple(Pal.A(Pal.Rope, .70f), 1.15f);
        }

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

            foreach (var shade in _armShade) Tween.Tint(shade, Pal.A(dust, 0f), .3f, Ease.InQuad);
            foreach (var thorn in _thorns) Tween.Tint(thorn, Pal.A(dust, 0f), .3f, Ease.InQuad);
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

            foreach (var shade in _armShade) shade.color = ShadeTint;
            foreach (var thorn in _thorns) thorn.color = Pal.A(Pal.Thorn, .95f);
            for (int k = 0; k < _armBase.Count; k++) _armBase[k].color = _armRest[k];
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

        /// <summary>
        /// Undoes <see cref="Gutter"/>: paints this tile the way the model says it stands.
        ///
        /// <para>
        /// <b>It has to clear the caches, and that is the whole of it.</b>
        /// <see cref="ApplyEnergy"/> is guarded on <c>_shownEnergy</c> and <c>_shownHalo</c>
        /// precisely so that repainting an unchanged board costs nothing — and
        /// <see cref="Gutter"/> tints every layer to dead without touching either, because it
        /// is a farewell rather than a state. So the model and the caches agree, the picture
        /// does not, and a plain <c>ApplyEnergy</c> here would return having done nothing at
        /// all. <see cref="ResetTo"/> clears them for the same reason after a restart.
        /// </para>
        /// <para>
        /// Animated rather than snapped: the grove went to sleep over half a second and it
        /// should wake the same way, ordered by depth, which is what <c>ApplyEnergy</c>'s own
        /// delay already does.
        /// </para>
        /// </summary>
        public void Relight()
        {
            _shownEnergy = -1;
            _shownHalo = -1;
            ApplyEnergy(true);
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
            _shownHalo = -1;
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
            // Per strand, because a crossing can be carrying two different colours through one
            // tile and the whole mechanic is invisible if both pairs of arms are painted the
            // same. Packed into one number so the "has anything changed" guard still holds:
            // repainting a board on every evaluation is what the shown-value cache exists to
            // stop, and two fields would need two of them.
            int onStrand0 = _p.EnergyOn(_i, 0), onStrand1 = _p.EnergyOn(_i, 1);
            int energy = onStrand0 | (onStrand1 << 3);
            int depth = Mathf.Max(0, _p.Depth[_i]);

            float delay = animate ? extraDelay + depth * .028f : 0f;
            float dur = animate ? .3f : .001f;

            if (_shownEnergy != energy)
            {
                _shownEnergy = energy;
                for (int k = 0; k < _armLit.Count; k++)
                {
                    int flow = _armStrand[k] == 0 ? onStrand0 : onStrand1;
                    bool armLit = flow != 0;
                    var armCol = Pal.EnergyColour(flow);

                    var lay = _armLit[k]; var glow = _armGlow[k];
                    Tween.Tint(lay, armLit ? Pal.A(Pal.Lift(armCol, .35f), 1f) : Pal.A(armCol, 0f), dur, Ease.OutQuad).Delay(delay);
                    Tween.Tint(glow, armLit ? Pal.A(armCol, .5f) : Pal.A(armCol, 0f), dur * 1.4f, Ease.OutQuad).Delay(delay);
                }

                // A crossing has no hub, because a hub is this board's word for a junction.
                if (_hubLit)
                {
                    bool lit = onStrand0 != 0;
                    var col = Pal.EnergyColour(onStrand0);
                    Tween.Tint(_hubLit, lit ? Pal.A(Pal.Lift(col, .55f), 1f) : Pal.A(col, 0f), dur, Ease.OutQuad).Delay(delay);
                    Tween.Tint(_hubGlow, lit ? Pal.A(col, .62f) : Pal.A(col, 0f), dur * 1.4f, Ease.OutQuad).Delay(delay);
                }
            }

            if (_p.C[_i].kind == Kind.Lamp) ApplyLamp(animate, delay);

            PaintFragility(animate);
        }

        void ApplyLamp(bool animate, float delay)
        {
            bool lit = _p.Lit[_i];

            // The halo tracks the energy reaching the tile, not only whether it is lit:
            // an unfussy critter wears the colour it was given, and blending a second
            // heart into its network changes that colour without waking it twice.
            int reaching = _p.Energy(_i);
            if (lit == _wasLit && reaching == _shownHalo && animate) return;
            _wasLit = lit;
            _shownHalo = reaching;

            var want = HaloColour();
            var ring = HaloSprite();

            void Apply()
            {
                if (this == null || _critter == null) return;
                if (_book) _book.enabled = lit;
                // Guarded: AssetLibrary.Frames answers an *empty array* and a warning when
                // art is missing, deliberately, so that a failed bundle draws blank rather
                // than throwing. Indexing it unchecked turned that into
                // an IndexOutOfRangeException raised out of a tween callback in the middle
                // of BoardView.Build — a half-drawn, unplayable board instead of a critter
                // nobody can see. Found by building a board with the art unloaded.
                if (!lit && _book)
                {
                    var sleeping = Art.Frames("Critters/c" + (_p.C[_i].critter + 1));
                    if (sleeping != null && sleeping.Length > 0) _critter.sprite = sleeping[0];
                }

                // Swapped rather than cross-faded. The three arcs and the single colour are
                // two different statements, and a blend between them would read as a fourth.
                if (_halo && _halo.sprite != ring) _halo.sprite = ring;

                Tween.Tint(_critter, lit ? Color.white : SleepTint, .28f);
                Tween.Tint(_halo, Pal.A(want, lit ? 1f : SleepingHalo), .28f);
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
