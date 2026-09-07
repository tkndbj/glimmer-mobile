using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Emberforge drawn: a wall of jewels bolted into a raider hull, the fittings holding it up,
    /// and the light that comes out when three of them are forced together.
    ///
    /// <para>
    /// <b>Everything here is in service of one moment.</b> An ember is tapped, a cross of light
    /// goes down its row and its column, and every ember that light touches goes off too — each
    /// beat louder, a semitone higher and shaking harder, with a multiplier climbing over the
    /// blast. That chain is the whole reason this mode exists, so it gets the largest drawing in
    /// it: invariant 20m's first rule, which the Tanglewood learned by shipping five mechanics
    /// whose payoff was drawn the same size as everything else and hearing "all I see is flowers
    /// popping".
    /// </para>
    /// <para>
    /// <b>The wall replays snapshots rather than reconstructing them.</b> <c>EmberBlast</c> comes
    /// back with one <c>EmberFrame</c> per beat and every deed stamped with the cell it happened
    /// on, so nothing here works out where a shard ended up after a collapse — which is invariant
    /// 30i taken seriously, because that arithmetic is exactly what no par, no <c>ways</c>, no
    /// validator and no content gate can ever see going wrong.
    /// </para>
    /// <para>
    /// <b>What the press preview may show is geometry and never outcome</b> (invariant 32c):
    /// holding an ember lights the cells its cross would reach, which is a fact the player can
    /// already read off the wall drawn faster. What it never draws is which shards would line up
    /// after a swap, because that is the thing they are working out — Budburst's withdrawn halo,
    /// and the reason it was withdrawn.
    /// </para>
    /// </summary>
    public sealed class EmberView : ProtoView
    {
        // ------------------------------------------------------------------ tempo
        /// <summary>The swap itself: two pieces changing places under a finger.</summary>
        const float SwapStep = .17f;

        /// <summary>The fuse: shards flying together, then the ember standing up out of them.</summary>
        const float DrawIn = .20f, StandUp = .26f;

        /// <summary>A blast: the flash, the beams racing out, then what they took.</summary>
        const float FlashStep = .11f, BeamStep = .26f, BlastStep = .24f;

        /// <summary>The collapse. Slower than the blast, because it is the thing to read.</summary>
        const float DropStep = .20f;

        /// <summary>A critter's climb out of a broken cage.</summary>
        const float RunHome = .78f;

        // ------------------------------------------------------------------ what the screen hears
        /// <summary>A story beat the level may want to answer. Raised once per move at most.</summary>
        public Action<StoryCue> Say { get; set; }

        // ------------------------------------------------------------------ the wall
        EmberLayout _layout;
        EmberBoard _board;

        RectTransform _back, _pieces, _fx;

        readonly List<Image> _tiles = new List<Image>(96);
        readonly Dictionary<int, Piece> _live = new Dictionary<int, Piece>(96);
        readonly List<EmberMove> _moves = new List<EmberMove>(48);

        int _held = -1;

        /// <summary>
        /// One drawn piece: a node, the body in it, and whatever hangs on top.
        ///
        /// A class rather than a struct because gravity re-keys the map on every collapse and
        /// every entry is a live scene node; a struct would be copied into the new slot and the
        /// tween would still be pointing at the old one.
        /// </summary>
        sealed class Piece
        {
            public RectTransform Node;
            public Image Body;
            public Image Trim;
            public Image Crew;
            public char What;
            public int Cell;
        }

        // ------------------------------------------------------------------ art
        /// <summary>
        /// One of this mode's sprites, addressed through <see cref="AssetManifest"/>.
        ///
        /// <b>Every art lookup on this screen goes through here and none of them builds a
        /// path</b>, which is invariant 7 and was learned the expensive way one mode ago:
        /// <c>MarchView.Boom</c> spelt its own folder out, asked for the wrong one, and drew a
        /// white rectangle two cells wide at every burst. A path built by hand in two places is
        /// two paths, and one of them is wrong.
        /// </summary>
        static Sprite Cut(string key) => AssetLibrary.Sprite(AssetManifest.EmberArt(key));

        /// <summary>
        /// One of this mode's flipbooks, as frames.
        ///
        /// A one-line wrapper rather than the call spelt out inside <see cref="Book"/>, so that
        /// <c>Tools/verify/artnames.py</c> can read it: the gate follows a thin wrapper whose
        /// whole body is a manifest lookup, which is what makes every <c>Reel("warden")</c> below
        /// a name something checks. Centralising a lookup is only safe if the check follows it.
        /// </summary>
        static Sprite[] Reel(string key) => AssetLibrary.Frames(AssetManifest.EmberArt(key));

        /// <summary>This mode's explosions, which live under Fx rather than beside the wall.</summary>
        static Sprite[] Blaze(string key) => AssetLibrary.Frames(AssetManifest.EmberFx(key));

        /// <summary>
        /// A flipbook widget, or <b>null</b> when its frames are not there.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand.</b> An <c>Image</c> with a
        /// null sprite is a <em>white rectangle</em> and not a blank (invariant 7b), so attaching
        /// a flipbook to an empty folder puts a solid square on the wall rather than nothing at
        /// all. Built this way round, missing art costs the thing it draws and never costs a
        /// square of white over the board.
        /// </para>
        /// <para>
        /// <b>It takes frames rather than a key, and that is not a style choice.</b>
        /// <c>Tools/verify/artnames.py</c> follows a thin wrapper whose whole body is an
        /// <see cref="AssetManifest"/> lookup and reads the literals in <em>its</em> first
        /// argument. A key handed to a method like this one — two statements and a branch — is a
        /// name the check cannot follow, so it is counted as "built" and nothing holds it to
        /// disk. Every caller below therefore writes <c>Book(Reel("warden"), …)</c>, which puts
        /// the literal exactly where the gate is looking.
        /// </para>
        /// </summary>
        Image Book(Sprite[] frames, string name, RectTransform parent, float size, float fps,
                   bool loop = true)
        {
            if (frames == null || frames.Length == 0) return null;

            var img = UIKit.Img(name, parent, frames[0], Color.white, new Vector2(size, size));
            img.raycastTarget = false;
            Flipbook.Attach(img, frames, fps, loop);
            return img;
        }

        static Sprite Face(char what)
        {
            switch (what)
            {
                case 'r': return Cut("shard_r");
                case 'g': return Cut("shard_g");
                case 'b': return Cut("shard_b");
                case 'y': return Cut("shard_y");
                case EmberLayout.Ember: return Cut("ember");
                case EmberLayout.Stone: return Cut("stone");
                case EmberLayout.Frost: return Cut("frost");
                case EmberLayout.Cage: return Cut("cage");
                default: return null;
            }
        }

        static Color Tint(char what)
        {
            switch (what)
            {
                case 'r': return Pal.Poppy;
                case 'g': return Pal.Mint;
                case 'b': return Pal.Azure;
                case 'y': return Pal.Sun;
                case EmberLayout.Ember: return Pal.Ember;
                case EmberLayout.Frost: return Pal.Glass;
                case EmberLayout.Cage: return Pal.Gold;
                default: return Pal.Cream;
            }
        }

        // ------------------------------------------------------------------ building
        protected override void Compose()
        {
            _layout = ((EmberRules)Rules).Layout;
            _board = (EmberBoard)Run.Board;

            _tiles.Clear();
            _live.Clear();
            _held = -1;

            _back = Layer("Back");
            _pieces = Layer("Pieces");
            _fx = Layer("Fx");

            Tiles();
            Deal();
            Targets();
        }

        RectTransform Layer(string name)
        {
            var rt = UIKit.Node(name, Field);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = Field.sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// The wall's backing, one tile per cell.
        ///
        /// Drawn under everything and never taken away, so a breach is a hole with the hull
        /// behind it rather than a gap in the picture — which is what makes progress legible on
        /// a board that only ever empties.
        /// </summary>
        void Tiles()
        {
            for (int i = 0; i < _layout.Grid.Count; i++)
            {
                var img = UIKit.Img("t", _back, Cut("wall"), Color.white,
                                    new Vector2(Cell, Cell));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition = CentreOf(i);
                _tiles.Add(img);
            }
        }

        void Deal()
        {
            for (int i = 0; i < _layout.Grid.Count; i++)
            {
                char c = _board.At(i);
                if (c == EmberLayout.Breach) continue;
                _live[i] = Make(c, i);
            }
        }

        Piece Make(char what, int cell)
        {
            var node = UIKit.Node("p", _pieces);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(Cell, Cell);
            node.anchoredPosition = CentreOf(cell);

            var piece = new Piece { Node = node, What = what, Cell = cell };

            if (what == EmberLayout.Warden || what == EmberLayout.Scarred)
            {
                // A warden is drawn as itself rather than as a jewel with a face on it: the whole
                // point of one is that it wears no colour, so it must never look like something
                // a match could take.
                piece.Crew = Book(Reel("warden"), "Crew", node, Cell * 1.22f, 11f);

                if (what == EmberLayout.Warden)
                {
                    piece.Trim = UIKit.Img("Plate", node, Cut("plate"), Color.white,
                                           new Vector2(Cell * 1.02f, Cell * 1.02f));
                    piece.Trim.raycastTarget = false;
                }

                return piece;
            }

            // A cage fills its cell and everything else sits inside one, which is the render's
            // answer to invariant 32b: the goal has to win a legibility fight against four
            // jewels, and it does that by being the biggest thing in the square.
            float draw = what == EmberLayout.Cage ? 1f : .88f;

            piece.Body = UIKit.Img("Body", node, Face(what), Color.white,
                                   new Vector2(Cell * draw, Cell * draw));
            piece.Body.raycastTarget = false;

            if (what == EmberLayout.Cage)
            {
                // The thing the level is about, so it gets the one halo on the wall that is not
                // an explosion — and a critter inside it, because a cage with nothing in it is a
                // prop rather than a reason.
                piece.Crew = Caged(cell, node, Cell * .72f);
                if (piece.Crew != null) piece.Crew.transform.SetAsFirstSibling();

                var halo = UIKit.Halo(node, Pal.Gold, Cell * 1.5f, .30f);
                halo.transform.SetAsFirstSibling();
                Tween.Run(1.7f, Ease.InOutSine, t =>
                {
                    if (!halo) return;
                    float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                    halo.color = Pal.A(Pal.Gold, Mathf.Lerp(.14f, .38f, k));
                }, halo).Loop();
            }
            else if (what == EmberLayout.Ember)
            {
                Prime(piece);
            }

            return piece;
        }

        /// <summary>
        /// The critter shut in this cage, standing still.
        ///
        /// <para>
        /// <b>Three calls with three literals rather than one call with a name built from the
        /// number</b>, and that is the difference between a name something checks and a name
        /// nothing does. <c>Tools/verify/artnames.py</c> reads the literals in a lookup's first
        /// argument and holds them to disk; a folder reached through a helper that returns a
        /// string is invisible to it, and an art name nothing checks is a white rectangle
        /// waiting to happen (invariant 7b, and the <c>InvalidKeyException</c> a player found in
        /// Hollowmarch).
        /// </para>
        /// </summary>
        Image Caged(int cell, RectTransform parent, float size)
        {
            int who = Mathf.Abs(cell) % 3;

            return who == 0 ? Book(Reel("mon1"), "In", parent, size, 9f)
                 : who == 1 ? Book(Reel("mon2"), "In", parent, size, 9f)
                            : Book(Reel("mon3"), "In", parent, size, 9f);
        }

        /// <summary>The same critter, out and climbing. See <see cref="Caged"/> for why it is three calls.</summary>
        Image Loose(int cell, RectTransform parent, float size)
        {
            int who = Mathf.Abs(cell) % 3;

            return who == 0 ? Book(Reel("mon1_jump"), "Free", parent, size, 16f)
                 : who == 1 ? Book(Reel("mon2_jump"), "Free", parent, size, 16f)
                            : Book(Reel("mon3_jump"), "Free", parent, size, 16f);
        }

        /// <summary>
        /// The lit fuse. An ember is the one thing on this wall that is <em>moving</em> while
        /// nothing is happening, which is what makes a player look at it and want to tap it.
        /// </summary>
        void Prime(Piece piece)
        {
            if (piece.Node == null) return;

            var halo = UIKit.Halo(piece.Node, Pal.Ember, Cell * 1.35f, .45f);
            halo.transform.SetAsFirstSibling();
            piece.Trim = halo;

            var body = piece.Body;

            Tween.Run(.85f, Ease.InOutSine, t =>
            {
                float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                if (halo) halo.color = Pal.A(Pal.Ember, Mathf.Lerp(.24f, .62f, k));
                if (body) body.transform.localScale = Vector3.one * Mathf.Lerp(.96f, 1.06f, k);
            }, piece.Node).Loop();
        }

        // ------------------------------------------------------------------ painting
        protected override void Repaint()
        {
            if (_layout == null) return;

            foreach (var pair in _live)
                if (pair.Value.Node != null) Destroy(pair.Value.Node.gameObject);

            _live.Clear();
            Deal();
            Unlight();
        }

        // ------------------------------------------------------------------ input
        /// <summary>
        /// One hit target per cell.
        ///
        /// <b>Per cell rather than one catcher over the whole wall</b>, which is <c>BudView</c>'s
        /// idiom and right for the same reason: a jewel is a rounded shape drawn inside a square,
        /// so hit-testing the <em>cell</em> and then asking the board what is standing there keeps
        /// the drawing and the rule from ever disagreeing about what was touched. A target sits
        /// on a breach too, so a tap on an empty stretch is answered rather than swallowed.
        /// </summary>
        void Targets()
        {
            for (int cell = 0; cell < _layout.Grid.Count; cell++)
            {
                int at = cell;

                var img = UIKit.Img("hit", Field, Art.Pixel, new Color(0f, 0f, 0f, 0f),
                                    new Vector2(Cell, Cell));
                img.raycastTarget = true;
                img.rectTransform.anchoredPosition = CentreOf(at);

                var btn = img.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Tap(at), silent: true);

                var hover = img.gameObject.AddComponent<Hover>();
                hover.Enter = () => Aiming(at);
                hover.Exit = Unlight;

                var drag = img.gameObject.AddComponent<CellDrag>();
                drag.Threshold = Cell * .28f;
                drag.Began = () => Aiming(at);
                drag.Ended = Unlight;
                drag.Dragged = dir => Drag(at, dir);
            }
        }

        void Tap(int cell)
        {
            Unlight();
            if (!Playable) return;

            var move = EmberMove.Tap(cell);
            if (!_board.Aim(move.From, move.To, out _))
            {
                // A tap on bare wall or on a fitting is not worth a sentence; a tap on a shard is
                // the mode's single rule the board cannot show for itself — you drag those.
                if (EmberLayout.IsShard(_board.At(cell))) Refused?.Invoke();
                else Audio.Sfx("blocked", .26f, 1.2f);
                return;
            }

            StartCoroutine(Play(move));
        }

        void Drag(int cell, Vector2Int dir)
        {
            Unlight();
            if (!Playable) return;

            int w = _layout.Width;
            int x = cell % w + dir.x, y = cell / w - dir.y;

            if (x < 0 || y < 0 || x >= w || y >= _layout.Height)
            {
                Audio.Sfx("blocked", .26f, 1.2f);
                return;
            }

            int to = y * w + x;
            var move = new EmberMove(cell, to);

            if (!_board.Aim(move.From, move.To, out _))
            {
                Nudge(cell, to);
                Refused?.Invoke();
                return;
            }

            StartCoroutine(Play(move));
        }

        /// <summary>
        /// A swap that lines nothing up: both pieces lean into each other and come back.
        ///
        /// Every mode here has inputs that mean nothing, and the difference between a board that
        /// feels broken and one that feels solid is whether it answers them. This one is the
        /// genre's own answer, and it is the reason a refused swap does not need a sentence.
        /// </summary>
        void Nudge(int from, int to)
        {
            Audio.Sfx("blocked", .3f, 1.1f);

            Vector2 home = CentreOf(from), there = CentreOf(to);
            var lean = (there - home) * .28f;

            Lean(from, home + lean);
            Lean(to, there - lean);
        }

        void Lean(int cell, Vector2 towards)
        {
            if (!_live.TryGetValue(cell, out var piece) || piece.Node == null) return;

            Vector2 home = CentreOf(cell);
            var node = piece.Node;

            Tween.Move(node, towards, .09f, Ease.OutQuad)
                 .OnDone(() => { if (node) Tween.Move(node, home, .13f, Ease.OutBack); });
        }

        // ------------------------------------------------------------------ the aim
        /// <summary>
        /// Lights the cells an ember's cross would reach. Geometry and never outcome
        /// (invariant 32c).
        /// </summary>
        void Aiming(int cell)
        {
            Unlight();
            if (!Playable) return;

            _held = cell;
            if (!EmberLayout.IsEmber(_board.At(cell))) return;

            int w = _layout.Width, h = _layout.Height;
            Glow(cell);

            for (int d = 0; d < EmberLayout.CrossRays; d++)
            {
                int x = cell % w, y = cell / w;

                while (true)
                {
                    x += EmberLayout.StepX[d];
                    y += EmberLayout.StepY[d];
                    if (x < 0 || y < 0 || x >= w || y >= h) break;

                    Glow(y * w + x);
                    if (EmberLayout.Stops(_board.At(y * w + x))) break;
                }
            }
        }

        void Glow(int cell)
        {
            if (cell < 0 || cell >= _tiles.Count) return;
            if (_tiles[cell] != null) _tiles[cell].sprite = Cut("wall_lit");
        }

        void Unlight()
        {
            _held = -1;
            for (int i = 0; i < _tiles.Count; i++)
                if (_tiles[i] != null) _tiles[i].sprite = Cut("wall");
        }

        // ------------------------------------------------------------------ the move
        IEnumerator Play(EmberMove move)
        {
            Busy = true;
            HideCoach();
            Unlight();

            bool tap = move.IsTap;
            _board.Aim(move.From, move.To, out var aim);

            var log = _board.Fire(move);
            if (log == null) { Busy = false; yield break; }

            Took(log.Took + log.Goals * 4);

            yield return Touch(log, move, aim);

            for (int beat = 1; beat <= log.Beats; beat++) yield return Beat(log, beat);

            Voice(log);

            Busy = false;
            Settle();
        }

        /// <summary>The touch itself: the swap, the tap or the two embers coming together.</summary>
        IEnumerator Touch(EmberBlast log, EmberMove move, EmberAim aim)
        {
            switch (aim)
            {
                case EmberAim.Fire:
                    Audio.Sfx("poke", .5f, 1.1f);
                    Vanish(move.From, .12f);
                    yield return new WaitForSeconds(.10f);
                    break;

                case EmberAim.Merge:
                    // The one move in the mode where the two things the player made are pushed
                    // into each other, so both fly to the same cell and the star comes out of it.
                    Audio.Sfx("unlock", .75f, 1.05f);
                    Slide(move.From, CentreOf(move.To), DrawIn);
                    yield return new WaitForSeconds(DrawIn);
                    Vanish(move.From, .01f);
                    Vanish(move.To, .01f);
                    break;

                default:
                    Audio.Sfx("rotate_a", .40f, 1.15f);
                    Swap(move.From, move.To);
                    yield return new WaitForSeconds(SwapStep);
                    break;
            }
        }

        void Swap(int from, int to)
        {
            _live.TryGetValue(from, out var a);
            _live.TryGetValue(to, out var b);

            if (a != null)
            {
                Tween.Move(a.Node, CentreOf(to), SwapStep, Ease.OutCubic);
                a.Cell = to;
            }

            if (b != null)
            {
                Tween.Move(b.Node, CentreOf(from), SwapStep, Ease.OutCubic);
                b.Cell = from;
            }

            _live.Remove(from);
            _live.Remove(to);

            if (a != null) _live[to] = a;
            if (b != null) _live[from] = b;
        }

        void Slide(int cell, Vector2 to, float seconds)
        {
            if (!_live.TryGetValue(cell, out var piece) || piece.Node == null) return;
            Tween.Move(piece.Node, to, seconds, Ease.InQuad);
        }

        void Vanish(int cell, float seconds)
        {
            if (!_live.TryGetValue(cell, out var piece)) return;
            _live.Remove(cell);

            var node = piece.Node;
            if (node == null) return;

            Tween.Scale(node, .05f, seconds, Ease.InBack)
                 .OnDone(() => { if (node) Destroy(node.gameObject); });
        }

        // ------------------------------------------------------------------ one beat
        /// <summary>
        /// One beat of the cascade: either everything fusing, or everything going off — and then
        /// the wall collapsing into what is left.
        ///
        /// The two never happen in the same beat, because the board does not let them: a beam
        /// that set off an ember names the cell it stood on, so a collapse in between would fire
        /// it from wherever the wall had put something else.
        /// </summary>
        IEnumerator Beat(EmberBlast log, int beat)
        {
            bool blew = false;

            for (int i = 0; i < log.Deeds.Count && !blew; i++)
                blew = log.Deeds[i].Beat == beat && log.Deeds[i].Deed == EmberDeed.Blast;

            if (blew) yield return Blast(log, beat);
            else yield return Fuse(log, beat);

            yield return Drop(log, beat);
        }

        /// <summary>
        /// Shards flying together and an ember standing up out of them.
        ///
        /// <b>Drawn as an arrival rather than as a swap of sprites</b> — the cell is bare for a
        /// beat, the shards gather into it, and the ember comes up under a ring with the one "you
        /// made something" sound in the mode. That is Budburst's forge (invariant 20m's first
        /// rule) and it is the moment this whole mode is built to sell.
        /// </summary>
        IEnumerator Fuse(EmberBlast log, int beat)
        {
            var drawn = new List<int>(8);
            var made = new List<int>(4);
            int most = 0;

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Beat != beat) continue;

                if (deed.Deed == EmberDeed.Fuse)
                {
                    drawn.Add(deed.At);
                    Slide(deed.At, CentreOf(deed.To), DrawIn);
                }
                else if (deed.Deed == EmberDeed.Forge)
                {
                    made.Add(deed.At);
                    if (deed.To > most) most = deed.To;
                }
            }

            if (drawn.Count == 0) yield break;

            Audio.Sfx("chime", .45f, Mathf.Min(1.6f, .95f + most * .06f));

            yield return new WaitForSeconds(DrawIn);

            for (int i = 0; i < drawn.Count; i++) Vanish(drawn[i], .06f);

            for (int i = 0; i < made.Count; i++)
            {
                Vector2 where = CentreOf(made[i]);

                Shockwave(where, Pal.Ember, 2.4f, .40f);
                Burst.Sparks(_fx, where, Pal.Sun, 14, 190f, 22f, .5f);
                Audio.Sfx("unlock", .55f, 1.15f);

                var piece = Make(EmberLayout.Ember, made[i]);
                _live[made[i]] = piece;

                piece.Node.localScale = Vector3.one * .1f;
                Tween.Scale(piece.Node, 1f, StandUp, Ease.OutElastic);
            }

            yield return new WaitForSeconds(StandUp * .7f);
        }

        /// <summary>
        /// The blast: a flash, beams racing out to the last cell each one reaches, and everything
        /// they caught coming apart.
        ///
        /// Every beat is louder, higher and shakes harder than the one before it, which is the
        /// whole of what makes a chain feel like it is building rather than repeating.
        /// </summary>
        IEnumerator Blast(EmberBlast log, int beat)
        {
            var rays = new List<EmberDeedRecord>(8);
            int crosses = 0, stars = 0;
            Vector2 middle = Vector2.zero;

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Beat != beat) continue;

                if (deed.Deed == EmberDeed.Blast)
                {
                    if ((EmberBlow)deed.To == EmberBlow.Star) stars++; else crosses++;
                    middle = CentreOf(deed.At);
                    Pop(middle, Pal.Radiance, 3.4f, .30f);

                    // **A different explosion per beat, not the same one louder.** A chain that
                    // escalates in volume and shake alone reads as a repeat; five licensed
                    // flipbooks is what makes the fourth beat feel like a different event from
                    // the first, which is invariant 20m's first rule about the payoff getting
                    // the biggest drawing in the mode. Written out rather than indexed, so
                    // `artnames.py` holds all five to disk.
                    Boom((EmberBlow)deed.To == EmberBlow.Star ? Blaze("boom_violet")
                         : beat > 2 ? Blaze("boom_blue")
                         : Blaze("boom_fire"),
                         middle, (EmberBlow)deed.To == EmberBlow.Star ? 3.1f : 2.4f);
                }
                else if (deed.Deed == EmberDeed.Ray)
                {
                    rays.Add(deed);
                }
            }

            float pitch = Mathf.Min(1.9f, .92f + (beat - 1) * .14f);
            Audio.Sfx(stars > 0 ? "shatter" : "burst", Mathf.Min(1f, .60f + beat * .11f), pitch);
            ShakeBoard(Mathf.Min(38f, (stars > 0 ? 20f : 11f) + beat * 6f));

            for (int i = 0; i < rays.Count; i++)
                Beam(CentreOf(rays[i].At), CentreOf(rays[i].To),
                     stars > 0 ? Pal.Radiance : Pal.Sun);

            yield return new WaitForSeconds(FlashStep);

            // What the beams caught. Read from the log rather than from the wall, so nothing here
            // has to work out what a beam must have reached (invariant 30i).
            int freed = 0, wrecked = 0, lit = 0;

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Beat != beat) continue;

                Vector2 where = CentreOf(deed.At);

                switch (deed.Deed)
                {
                    case EmberDeed.Break:
                        Burst.Sparks(_fx, where, Tint(deed.What), 7, 150f, 16f, .40f);
                        Vanish(deed.At, .14f);
                        break;

                    case EmberDeed.Melt:
                        Audio.Sfx("chime2", .35f, 1.35f);
                        Burst.Sparks(_fx, where, Pal.Glass, 12, 170f, 20f, .5f);
                        Pop(where, Pal.Glass, 2.0f, .32f);
                        Vanish(deed.At, .18f);
                        break;

                    case EmberDeed.Ignite:
                        lit++;
                        Pop(where, Pal.Ember, 2.6f, .26f);
                        Vanish(deed.At, .10f);
                        break;

                    case EmberDeed.Free:
                        freed++;
                        Rescue(deed.At, where);
                        break;

                    case EmberDeed.Crack:
                        Crack(deed.At);
                        break;

                    case EmberDeed.Wreck:
                        wrecked++;
                        Wreck(deed.At, where);
                        break;
                }
            }

            if (freed > 0) Audio.Sfx("free", .85f, 1f);
            if (wrecked > 0) Audio.Sfx("shatter", .6f, .85f);
            if (beat > 1) Multiplier(middle, beat);

            yield return new WaitForSeconds(BlastStep);
        }

        /// <summary>
        /// One ray, drawn as a bar of light racing out from the blast to the last cell it reached.
        ///
        /// <para>
        /// Rotated from a pivot at the blast rather than drawn cell by cell, because a star
        /// throws four of these along the diagonals and a chain of stars throws dozens — and
        /// because a beam that arrives all at once reads as a beam where a row of flashes reads
        /// as a row of flashes.
        /// </para>
        /// </summary>
        void Beam(Vector2 from, Vector2 to, Color tint)
        {
            var span = to - from;
            float length = span.magnitude + Cell * .5f;
            if (length < Cell * .3f) return;

            var bar = UIKit.Img("Beam", _fx, Art.Glow(96, 1.6f), Pal.A(tint, .95f),
                                new Vector2(length, Cell * .62f), new Vector2(.5f, .5f), from);

            bar.raycastTarget = false;
            bar.rectTransform.pivot = new Vector2(0f, .5f);
            bar.rectTransform.anchoredPosition = from;
            bar.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);

            bar.rectTransform.localScale = new Vector3(.05f, .4f, 1f);

            Tween.Run(BeamStep, Ease.OutQuint, t =>
            {
                if (!bar) return;
                bar.rectTransform.localScale = new Vector3(t, Mathf.Lerp(.4f, 1.25f, t), 1f);
                bar.color = Pal.A(tint, t < .5f ? .95f : (1f - t) * 1.9f);
            }, bar).OnDone(() => { if (bar) Destroy(bar.gameObject); });

            // The white-hot core, half the width and gone twice as fast. Two bars rather than one
            // is what stops a beam reading as a coloured stripe.
            var core = UIKit.Img("Core", _fx, Art.Pixel, Pal.A(Pal.Cream, .9f),
                                 new Vector2(length, Cell * .16f), new Vector2(.5f, .5f), from);

            core.raycastTarget = false;
            core.rectTransform.pivot = new Vector2(0f, .5f);
            core.rectTransform.anchoredPosition = from;
            core.rectTransform.localRotation = bar.rectTransform.localRotation;
            core.rectTransform.localScale = new Vector3(.05f, 1f, 1f);

            Tween.Run(BeamStep * .8f, Ease.OutQuint, t =>
            {
                if (!core) return;
                core.rectTransform.localScale = new Vector3(t, 1f, 1f);
                core.color = Pal.A(Pal.Cream, 1f - t);
            }, core).OnDone(() => { if (core) Destroy(core.gameObject); });
        }

        /// <summary>
        /// The explosion. Licensed frames rather than a particle spray, because a chain of these
        /// is the largest thing that happens in this mode and a spray reads as dust.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand</b> (invariant 7b): an
        /// <c>Image</c> with a null sprite is a white rectangle, so a flipbook attached to an
        /// empty folder leaves a square two cells wide sitting on the wall at every burst.
        /// </para>
        /// <para>
        /// <b>It takes frames rather than a key</b>, so that every caller writes
        /// <c>Boom(Blaze("boom_gold"), …)</c> and the literal lands in a manifest wrapper's own
        /// first argument — the one shape <c>Tools/verify/artnames.py</c> can hold to disk. A key
        /// passed through a method like this one goes unchecked, which is exactly how a player
        /// came to meet five <c>InvalidKeyException</c>s a level in the mode before this one.
        /// </para>
        /// </summary>
        void Boom(Sprite[] frames, Vector2 where, float size)
        {
            if (frames == null || frames.Length == 0)
            {
                Shockwave(where, Pal.Sun, 2.6f, .4f);
                return;
            }

            var img = UIKit.Img("Boom", _fx, frames[0], Color.white,
                                new Vector2(Cell * size, Cell * size));
            img.raycastTarget = false;
            img.rectTransform.anchoredPosition = where;

            var book = Flipbook.Attach(img, frames, 26f, false);
            book.OnFinished = () => { if (img) Destroy(img.gameObject); };

            // A guard as well as the callback: a flipbook whose owner is torn down mid-play never
            // reaches OnFinished, and these are created faster than anything else destroys them.
            Tween.After(1.4f, () => { if (img) Destroy(img.gameObject); }, this);
        }

        /// <summary>A critter out of a broken cage, climbing out and away over the wall.</summary>
        void Rescue(int cell, Vector2 where)
        {
            Boom(Blaze("boom_gold"), where, 2.2f);

            if (_live.TryGetValue(cell, out var caged))
            {
                _live.Remove(cell);
                var node = caged.Node;
                if (node != null)
                {
                    Tween.Scale(node, .05f, .18f, Ease.InBack)
                         .OnDone(() => { if (node) Destroy(node.gameObject); });
                }
            }

            var img = Loose(cell, _fx, Cell * 1.15f);
            if (img == null) { Shockwave(where, Pal.Gold, 2.8f, .5f); return; }

            img.rectTransform.anchoredPosition = where;

            // Out of the top of the wall rather than off to one side: the cage was buried in it,
            // so up and away is the direction that says what just happened.
            Vector2 home = where + new Vector2(0f, Span.y * .5f + Cell);

            Tween.Run(RunHome, Ease.OutQuad, t =>
            {
                if (!img) return;
                var p = Vector2.Lerp(where, home, t);
                p.x += Mathf.Sin(t * Mathf.PI * 2.4f) * Cell * .35f;
                img.rectTransform.anchoredPosition = p;
                img.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.15f, .68f, t);
                img.color = Pal.A(Color.white, t > .7f ? (1f - t) / .3f : 1f);
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });

            Shockwave(where, Pal.Gold, 2.8f, .5f);
        }

        /// <summary>The armour turning a beam away: the one refusal this wall shows for itself.</summary>
        void Crack(int cell)
        {
            if (!_live.TryGetValue(cell, out var piece)) return;

            piece.What = EmberLayout.Scarred;

            if (piece.Trim != null)
            {
                var plate = piece.Trim;
                piece.Trim = null;
                Tween.Tint(plate, Color.white, .06f).OnDone(() =>
                {
                    if (plate) Tween.Fade(plate, 0f, .32f, Ease.OutQuad);
                });
            }

            if (piece.Crew != null)
            {
                var frames = Reel("warden_hit");
                if (frames != null && frames.Length > 0)
                {
                    Flipbook.Detach(piece.Crew);
                    Flipbook.Attach(piece.Crew, frames, 14f, true);
                }
            }

            if (piece.Node != null) Tween.Shake(piece.Node, Cell * .14f, .28f);
            Audio.Sfx("blocked", .5f, .9f);
            Burst.Sparks(_fx, CentreOf(cell), Pal.Glass, 9, 130f, 18f, .4f);
        }

        void Wreck(int cell, Vector2 where)
        {
            Boom(Blaze("boom_smoke"), where, 2.4f);

            if (!_live.TryGetValue(cell, out var piece)) { ShakeBoard(14f); return; }
            _live.Remove(cell);

            var node = piece.Node;

            if (piece.Crew != null)
            {
                var frames = Reel("warden_dead");
                if (frames != null && frames.Length > 0)
                {
                    Flipbook.Detach(piece.Crew);
                    var book = Flipbook.Attach(piece.Crew, frames, 16f, false);
                    book.OnFinished = () => { if (node) Destroy(node.gameObject); };
                    Tween.After(1.2f, () => { if (node) Destroy(node.gameObject); }, this);
                    return;
                }
            }

            if (node != null)
                Tween.Scale(node, .05f, .24f, Ease.InBack)
                     .OnDone(() => { if (node) Destroy(node.gameObject); });
        }

        /// <summary>
        /// The chain count, over the blast that caused it.
        ///
        /// The one number this mode puts on the wall, and deliberately not a score: what it says
        /// is <em>that this went off because the last one did</em>, which is the thing the player
        /// did and the thing they will try to do again.
        /// </summary>
        void Multiplier(Vector2 where, int beat)
        {
            var text = UIKit.Titled("Chain", _fx, "x" + beat, Mathf.RoundToInt(Cell * .64f),
                                    Pal.Sun, TextAnchor.MiddleCenter,
                                    new Vector2(Cell * 3f, Cell), new Vector2(.5f, .5f), where);
            text.raycastTarget = false;

            var rt = text.rectTransform;
            rt.localScale = Vector3.one * .4f;

            Tween.Scale(rt, 1.18f, .26f, Ease.OutBack);
            Tween.Run(.9f, Ease.OutCubic, t =>
            {
                if (!text) return;
                rt.anchoredPosition = where + new Vector2(0f, Cell * 1.1f * t);
                text.color = Pal.A(Pal.Sun, t > .55f ? (1f - t) / .45f : 1f);
            }, text).OnDone(() => { if (text) Destroy(text.gameObject); });

            Audio.Sfx("star", .55f, Mathf.Min(1.9f, 1f + beat * .13f));
        }

        // ------------------------------------------------------------------ the collapse
        /// <summary>
        /// The wall settling into what is left of it.
        ///
        /// Every move is applied at once — the whole list is lifted out of the map before any of
        /// it goes back in — because two shards can swap places down a column across one beat and
        /// re-keying them one at a time would lose whichever went second.
        /// </summary>
        IEnumerator Drop(EmberBlast log, int beat)
        {
            var moved = new List<EmberDeedRecord>(16);

            for (int i = 0; i < log.Deeds.Count; i++)
                if (log.Deeds[i].Beat == beat && log.Deeds[i].Deed == EmberDeed.Drop)
                    moved.Add(log.Deeds[i]);

            if (moved.Count == 0) yield break;

            var carried = new List<Piece>(moved.Count);

            for (int i = 0; i < moved.Count; i++)
            {
                if (!_live.TryGetValue(moved[i].At, out var piece)) { carried.Add(null); continue; }
                _live.Remove(moved[i].At);
                carried.Add(piece);
            }

            for (int i = 0; i < moved.Count; i++)
            {
                var piece = carried[i];
                if (piece == null) continue;

                int to = moved[i].To;
                piece.Cell = to;
                _live[to] = piece;

                if (piece.Node != null) Tween.Move(piece.Node, CentreOf(to), DropStep, Ease.InQuad);
            }

            Audio.Sfx("tock", .28f, 1.25f);
            yield return new WaitForSeconds(DropStep);
        }

        // ------------------------------------------------------------------ the voice
        /// <summary>
        /// One story beat per move at most, chosen by what the move was worth.
        ///
        /// Ordered rather than raised for everything that happened, because a wall that frees two
        /// critters and wrecks a warden in one chain would otherwise say three sentences on top
        /// of each other — which is how a story stops being read (invariant 30d).
        /// </summary>
        void Voice(EmberBlast log)
        {
            if (Say == null) return;

            if (Run.Board.IsFinished) { Say(StoryCue.Won); return; }

            if (log.Freed > 0) { Say(StoryCue.Freed); return; }
            if (log.Wrecked > 0) { Say(StoryCue.Kill); return; }
            if (log.Beats > 2) { Say(StoryCue.Fired); return; }
            if (log.Forged > 0 && log.Crosses == 0 && log.Stars == 0) { Say(StoryCue.Forged); return; }

            if (Run.Budget.Bounded && Run.Budget.Left <= Run.Board.GoalsLeft) Say(StoryCue.Tight);
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>
        /// What the verb lesson rings: the first swap on the wall that would fuse something.
        ///
        /// An ember if the level deals one, because on that board the thing to be told about is
        /// the thing already standing there.
        /// </summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null) return -1;

                _board.Moves(_moves);

                for (int i = 0; i < _moves.Count; i++)
                    if (!_moves[i].IsTap) return _moves[i].From;

                return _moves.Count > 0 ? _moves[0].From : -1;
            }
        }

        /// <summary>What the second lesson rings: an ember if there is one, else the first cage.</summary>
        public override int FriendCell
        {
            get
            {
                if (_board == null) return -1;

                for (int i = 0; i < _layout.Grid.Count; i++)
                    if (EmberLayout.IsEmber(_board.At(i))) return i;

                for (int i = 0; i < _layout.Grid.Count; i++)
                    if (EmberLayout.IsGoal(_board.At(i))) return i;

                return -1;
            }
        }

        // ------------------------------------------------------------------ the endings
        protected override IEnumerator Triumph()
        {
            // The hull comes apart rather than the board merely celebrating: what the player did
            // was break the smelter open, so the thing that was holding the critters is the thing
            // that has to visibly stop.
            for (int i = 0; i < _tiles.Count; i += 3)
            {
                if (_tiles[i] == null) continue;
                Tween.Fade(_tiles[i], .25f, .45f, Ease.OutQuad);
            }

            ShakeBoard(26f);
            Audio.Sfx("shatter", .7f, .8f);

            yield return new WaitForSeconds(.22f);
            yield return base.Triumph();
        }
    }
}
