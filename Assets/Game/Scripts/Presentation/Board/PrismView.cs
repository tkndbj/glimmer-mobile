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
    /// Prismvale drawn: a dark grove floor with coloured gems scattered over it, lanterns
    /// standing among them, critters asleep in their husks, and the vein of light that runs
    /// along a line of gems once they all match.
    ///
    /// <para>
    /// <b>Everything here is in service of one moment.</b> A gem is dragged onto its neighbour,
    /// the two slide past each other, and a line of light <em>runs outward from the lantern
    /// along the gems that now match</em>, cell by cell, until it reaches the critter and wakes
    /// it. That run is the whole animation budget of the mode, and it is drawn as travel rather
    /// than as a state change for the reason invariant 20m gives: the payoff gets the biggest
    /// drawing, and a change of tint is what a player misses.
    /// </para>
    /// <para>
    /// <b>The vein is a permanent highlight and not an effect.</b> A link between two lit gems
    /// is a node that stays on the board for as long as the two of them are lit — so the player
    /// can always read what is connected to what, and a swap that <em>breaks</em> a vein is
    /// visible as the light going out of it rather than as nothing happening. That is the one
    /// thing this mode has instead of a resource meter: light is not stored, it is read off the
    /// arrangement, so what you can see is exactly what the rules think.
    /// </para>
    /// <para>
    /// <b>The board replays a log rather than reconstructing one.</b> <see cref="PrismFlare"/>
    /// comes back naming the two cells that moved, every gem that took light <em>in flood order
    /// out of its lantern</em>, every gem that lost it and every critter that woke — so nothing
    /// here works out what a swap must have done (invariant 30i). That flood order is what makes
    /// the run outward possible at all; recomputing it here would be exactly the arithmetic no
    /// par, no <c>ways</c>, no validator and no content gate can ever see going wrong.
    /// </para>
    /// <para>
    /// <b>What the drag preview may show is geometry and never outcome</b> (invariant 32c): the
    /// gem under the finger lifts, and that is all. Which cells would light is the thing the
    /// player is working out, and printing it is Budburst's withdrawn halo all over again
    /// (invariant 20l).
    /// </para>
    /// </summary>
    public sealed class PrismView : ProtoView
    {
        // ------------------------------------------------------------------ tempo
        /// <summary>Two gems changing places. The one beat the finger is waiting on.</summary>
        const float Slide = .17f;

        /// <summary>How long one cell of the vein takes to arrive, and the gap to the next.</summary>
        const float Spark = .16f, Step = .055f;

        /// <summary>Light going out of a vein that was broken.</summary>
        const float Douse = .22f;

        /// <summary>A critter's climb out of its husk.</summary>
        const float RunHome = .78f;

        // ------------------------------------------------------------------ the field
        PrismLayout _layout;
        PrismBoard _board;

        RectTransform _ground, _veins, _pieces, _fx;

        readonly List<Image> _tiles = new List<Image>(64);
        readonly Dictionary<int, Piece> _live = new Dictionary<int, Piece>(64);

        /// <summary>
        /// The bars drawn between two lit cells, keyed on the pair.
        ///
        /// <b>Kept rather than rebuilt</b>, because a vein is a permanent highlight: a link that
        /// was destroyed and remade on every repaint would flicker on the one thing the player
        /// reads the board by, and the arrival animation would have nothing to animate onto.
        /// </summary>
        readonly Dictionary<int, Image> _links = new Dictionary<int, Image>(96);

        readonly List<int> _around = new List<int>(4);

        /// <summary>
        /// One drawn thing standing on a cell: the node, the body, and the light it is wearing.
        ///
        /// A class rather than a struct because the map is re-keyed as gems change places and
        /// every entry is a live scene node; a struct would be copied into the new slot and the
        /// tween would still be pointing at the old one.
        /// </summary>
        sealed class Piece
        {
            public RectTransform Node;
            public Image Body;
            public Image Halo;
            public Image Crew;
            public char What;
            public int Cell;
            public bool Lit;
        }

        // ------------------------------------------------------------------ art
        /// <summary>
        /// One of this mode's sprites, addressed through <see cref="AssetManifest"/>.
        ///
        /// <b>Every art lookup on this screen goes through here and none of them builds a
        /// path</b>, which is invariant 7 and was learned the expensive way two modes ago:
        /// <c>MarchView.Boom</c> spelt its own folder out, asked for the wrong one, and drew a
        /// white rectangle two cells wide at every burst that a player found before any gate did.
        /// </summary>
        static Sprite Cut(string key) => AssetLibrary.Sprite(AssetManifest.PrismArt(key));

        /// <summary>
        /// One of this mode's flipbooks, as frames.
        ///
        /// A one-line wrapper rather than the call spelt out inside <see cref="Book"/>, so that
        /// <c>Tools/verify/artnames.py</c> can read it: the gate follows a thin wrapper whose
        /// whole body is a manifest lookup, which is what makes every <c>Reel("mon1")</c> below a
        /// name something checks. Centralising a lookup is only safe if the check follows it.
        /// </summary>
        static Sprite[] Reel(string key) => AssetLibrary.Frames(AssetManifest.PrismArt(key));

        /// <summary>This mode's flares, which live under Fx rather than beside the field.</summary>
        static Sprite[] Blaze(string key) => AssetLibrary.Frames(AssetManifest.PrismFx(key));

        /// <summary>
        /// A flipbook widget, or <b>null</b> when its frames are not there.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand.</b> An <c>Image</c> with a
        /// null sprite is a <em>white rectangle</em> and not a blank (invariant 7b), so attaching
        /// a flipbook to an empty folder puts a solid square on the board rather than nothing at
        /// all. Built this way round, missing art costs the thing it draws and never costs a
        /// square of white over the board.
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

        /// <summary>
        /// The four gems, written out rather than indexed, so <c>artnames.py</c> holds all four
        /// to disk.
        /// </summary>
        static Sprite Face(char what)
        {
            switch (what)
            {
                case 'r': return Cut("gem_r");
                case 'g': return Cut("gem_g");
                case 'b': return Cut("gem_b");
                case 'y': return Cut("gem_y");
                default: return Cut("husk");
            }
        }

        /// <summary>
        /// What a colour is painted.
        ///
        /// <b>The four are separated by value as well as by hue</b>, which is the same rule the
        /// art tool cuts them under: the whole verb is "is this gem the same as that one", so a
        /// player who cannot separate red from green has to be able to separate a heart from a
        /// rhombus and a bright one from a dark one.
        /// </summary>
        static Color Tint(int hue)
        {
            switch (hue)
            {
                case 0: return new Color(1f, .34f, .40f);       // ruby
                case 1: return new Color(.42f, .92f, .52f);     // emerald
                case 2: return new Color(.40f, .74f, 1f);       // sapphire
                default: return new Color(1f, .84f, .34f);      // amber
            }
        }

        // ------------------------------------------------------------------ building
        protected override void Compose()
        {
            _layout = ((PrismRules)Rules).Layout;
            _board = (PrismBoard)Run.Board;

            _tiles.Clear();
            _live.Clear();
            _links.Clear();
            _held = -1;

            // **The order is the legibility of the mode.** The veins go *under* the pieces, so a
            // gem sits on its own light rather than being covered by it, and a critter woken by a
            // vein is drawn over the light that woke it.
            _ground = Layer("Ground");
            _veins = Layer("Veins");
            _pieces = Layer("Pieces");
            _fx = Layer("Fx");

            Moss();
            Deal();
            Relink(false);
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
        /// The grove floor, one tile per cell.
        ///
        /// <b>Bare ground gets a tile too, drawn dark.</b> It is the only thing on this board
        /// that shapes a vein, so it has to be visible as a <em>place the light cannot go</em>
        /// rather than as a hole in the picture — and a board of scattered tiles on nothing reads
        /// as a rendering fault.
        /// </summary>
        void Moss()
        {
            for (int i = 0; i < _layout.Count; i++)
            {
                bool bare = _layout.At(i) == PrismLayout.Bare;

                var tile = UIKit.Img("t", _ground, Cut("moss"),
                                     bare ? new Color(.42f, .46f, .48f, .55f) : Color.white,
                                     new Vector2(Cell, Cell));
                tile.raycastTarget = false;
                tile.rectTransform.anchoredPosition = CentreOf(i);
                _tiles.Add(tile);
            }
        }

        void Deal()
        {
            for (int i = 0; i < _layout.Count; i++)
            {
                char c = _board.At(i);
                if (c == PrismLayout.Bare) continue;
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

            if (what == PrismLayout.Asleep)
            {
                // A husk fills its cell and the critter sleeps inside it, which is the render's
                // answer to invariant 32b: the goal has to win a legibility fight against a
                // scatter of bright gems, and it does that by being the biggest thing in the
                // square and the only thing carrying no gem colour at all.
                piece.Body = UIKit.Img("Husk", node, Cut("husk"), Color.white,
                                       new Vector2(Cell, Cell));
                piece.Body.raycastTarget = false;

                piece.Crew = Sleeping(cell, node, Cell * .62f);
                if (piece.Crew != null) piece.Crew.transform.SetAsFirstSibling();

                Asleep(piece);
                return piece;
            }

            if (what == PrismLayout.Awake)
            {
                piece.Body = UIKit.Img("Husk", node, Cut("husk"), Pal.A(Color.white, .3f),
                                       new Vector2(Cell, Cell));
                piece.Body.raycastTarget = false;
                return piece;
            }

            if (PrismLayout.IsLamp(what))
            {
                Lamp(piece, what);
                return piece;
            }

            piece.Body = UIKit.Img("Gem", node, Face(what), Color.white,
                                   new Vector2(Cell * .78f, Cell * .78f));
            piece.Body.raycastTarget = false;

            var halo = UIKit.Halo(piece.Node, Tint(PrismLayout.HueOf(what)), Cell * 1.3f, 0f);
            halo.transform.SetAsFirstSibling();
            piece.Halo = halo;

            Shine(piece, false);
            return piece;
        }

        /// <summary>
        /// A lantern: the one thing on the board a finger can never move, so it is the one thing
        /// that is always drawn at full strength.
        ///
        /// <b>It dims when it is feeding nothing</b>, which is the mode's one free readout: a
        /// lantern with no gem of its own colour beside it is a route that is not open yet, and
        /// saying so costs a sprite rather than a sentence.
        /// </summary>
        void Lamp(Piece piece, char what)
        {
            int hue = PrismLayout.HueOf(what);
            var tint = Tint(hue);

            var halo = UIKit.Halo(piece.Node, tint, Cell * 1.7f, .5f);
            halo.transform.SetAsFirstSibling();
            piece.Halo = halo;

            piece.Body = UIKit.Img("Lamp", piece.Node, Cut("lamp"), tint,
                                   new Vector2(Cell * .92f, Cell * .92f));
            piece.Body.raycastTarget = false;

            var body = piece.Body;
            Tween.Run(1.7f, Ease.InOutSine, t =>
            {
                if (!halo) return;
                float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                halo.color = Pal.A(tint, Mathf.Lerp(.34f, .62f, k));
                if (body) body.transform.localScale = Vector3.one * Mathf.Lerp(.98f, 1.03f, k);
            }, piece.Node).Loop();
        }

        /// <summary>Puts a lantern's own drawing in step with whether it is feeding anything.</summary>
        void Feeding(Piece piece, bool any)
        {
            if (piece == null || piece.Body == null) return;

            piece.Body.sprite = any ? Cut("lamp") : Cut("lamp_dark");
            piece.Lit = any;
        }

        /// <summary>
        /// The ring of light a sleeping critter is drawn under.
        ///
        /// A slow cold breath rather than a colour, because a critter here wants <em>light</em>
        /// and not a particular colour — the lantern carries the colour, and drawing one on the
        /// critter would be promising a rule the board does not have.
        /// </summary>
        void Asleep(Piece piece)
        {
            if (piece.Node == null) return;

            var ring = UIKit.Img("Want", piece.Node, Art.Ring(128, 8f), Pal.A(Pal.Cream, .5f),
                                 new Vector2(Cell * .94f, Cell * .94f));
            ring.raycastTarget = false;
            ring.transform.SetAsFirstSibling();
            piece.Halo = ring;

            Tween.Run(2.1f, Ease.InOutSine, t =>
            {
                if (!ring) return;
                float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                ring.color = Pal.A(Pal.Cream, Mathf.Lerp(.24f, .58f, k));
                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(.96f, 1.04f, k);
            }, ring).Loop();
        }

        /// <summary>
        /// The critter shut in this husk, asleep.
        ///
        /// <para>
        /// <b>Three calls with three literals rather than one call with a name built from the
        /// number</b>, and that is the difference between a name something checks and a name
        /// nothing does. <c>Tools/verify/artnames.py</c> reads the literals in a lookup's first
        /// argument and holds them to disk; a folder reached through a helper that returns a
        /// string is invisible to it, and an art name nothing checks is a white rectangle waiting
        /// to happen (invariant 7b, and the <c>InvalidKeyException</c> a player found in
        /// Hollowmarch).
        /// </para>
        /// </summary>
        Image Sleeping(int cell, RectTransform parent, float size)
        {
            int who = Mathf.Abs(cell) % 3;

            return who == 0 ? Book(Reel("mon1"), "In", parent, size, 7f)
                 : who == 1 ? Book(Reel("mon2"), "In", parent, size, 7f)
                            : Book(Reel("mon3"), "In", parent, size, 7f);
        }

        /// <summary>The same critter, awake and away. See <see cref="Sleeping"/> for why it is three calls.</summary>
        Image Loose(int cell, RectTransform parent, float size)
        {
            int who = Mathf.Abs(cell) % 3;

            return who == 0 ? Book(Reel("mon1_jump"), "Free", parent, size, 16f)
                 : who == 1 ? Book(Reel("mon2_jump"), "Free", parent, size, 16f)
                            : Book(Reel("mon3_jump"), "Free", parent, size, 16f);
        }

        // ------------------------------------------------------------------ the light
        /// <summary>
        /// Puts one gem's own drawing in step with whether it is carrying light.
        ///
        /// <para>
        /// <b>A lit gem is bigger, brighter and wears a halo; an unlit one is none of those.</b>
        /// Three differences rather than one, because the board's whole question is which gems
        /// are on the line — and a difference of tint alone is a difference only some people can
        /// see, which is the same rule the four silhouettes exist for.
        /// </para>
        /// </summary>
        void Shine(Piece piece, bool lit, bool animate = false)
        {
            if (piece == null || piece.Node == null || piece.Body == null) return;

            piece.Lit = lit;

            var tint = Tint(PrismLayout.HueOf(piece.What));
            float scale = lit ? 1.06f : .92f;
            var body = Pal.A(Color.white, lit ? 1f : .74f);
            var halo = Pal.A(tint, lit ? .55f : 0f);

            if (!animate)
            {
                piece.Body.color = body;
                piece.Node.localScale = Vector3.one * scale;
                if (piece.Halo != null) piece.Halo.color = halo;
                return;
            }

            Tween.Scale(piece.Node, scale, lit ? .18f : Douse, lit ? Ease.OutBack : Ease.OutQuad);
            Tween.Tint(piece.Body, body, lit ? .18f : Douse);
            if (piece.Halo != null) Tween.Tint(piece.Halo, halo, lit ? .18f : Douse);
        }

        /// <summary>The key a link between two touching cells is filed under. Order-free.</summary>
        int LinkKey(int a, int b) => a < b ? a * 4096 + b : b * 4096 + a;

        /// <summary>
        /// Whether a bar of light belongs between these two touching cells, and what colour.
        ///
        /// Two lit gems of the same vein, or a lantern and a gem its own colour is lighting.
        /// Everything else is dark — including two gems of a colour no lantern is reaching,
        /// which is the whole point: matching is not enough, the line has to come from somewhere.
        /// </summary>
        bool Wants(int a, int b, out Color tint)
        {
            tint = Color.white;

            int va = _board.Vein(a), vb = _board.Vein(b);
            char ca = _board.At(a), cb = _board.At(b);

            if (va >= 0 && va == vb) { tint = Tint(va); return true; }

            if (PrismLayout.IsLamp(ca) && vb >= 0 && PrismLayout.HueOf(ca) == vb)
            {
                tint = Tint(vb);
                return true;
            }

            if (PrismLayout.IsLamp(cb) && va >= 0 && PrismLayout.HueOf(cb) == va)
            {
                tint = Tint(va);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Brings every link on the board into step with the veins, all at once.
        ///
        /// Used on a repaint — a rebuild after a continue, or the board arriving — where nothing
        /// should animate. A move draws its own links one at a time instead, which is the whole
        /// point of the mode's one animation.
        /// </summary>
        void Relink(bool animate)
        {
            var wanted = new HashSet<int>();

            for (int cell = 0; cell < _layout.Count; cell++)
            {
                _layout.Around(cell, _around);
                for (int i = 0; i < _around.Count; i++)
                {
                    int nb = _around[i];
                    if (nb < cell) continue;

                    if (!Wants(cell, nb, out var tint)) continue;

                    wanted.Add(LinkKey(cell, nb));
                    Link(cell, nb, tint, animate);
                }
            }

            var stale = new List<int>();
            foreach (var pair in _links)
                if (!wanted.Contains(pair.Key)) stale.Add(pair.Key);

            for (int i = 0; i < stale.Count; i++) Unlink(stale[i], animate);
        }

        /// <summary>Draws the bar between two cells, or leaves the one that is already there.</summary>
        void Link(int a, int b, Color tint, bool animate)
        {
            int key = LinkKey(a, b);

            if (_links.TryGetValue(key, out var have) && have != null)
            {
                have.color = Pal.A(tint, .78f);
                return;
            }

            Vector2 from = CentreOf(a), to = CentreOf(b);
            var span = to - from;

            var bar = UIKit.Img("Vein", _veins, Art.Glow(96, 1.35f), Pal.A(tint, animate ? 0f : .78f),
                                new Vector2(span.magnitude + Cell * .52f, Cell * .40f),
                                new Vector2(.5f, .5f), (from + to) * .5f);
            bar.raycastTarget = false;
            bar.rectTransform.anchoredPosition = (from + to) * .5f;
            bar.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);

            _links[key] = bar;

            if (!animate) return;

            // The bar arrives by growing out of the middle of the two cells rather than by
            // fading: a vein is the thing the player just built, so it has to look like it
            // travelled there.
            bar.rectTransform.localScale = new Vector3(.15f, .5f, 1f);
            Tween.Run(Spark, Ease.OutCubic, t =>
            {
                if (!bar) return;
                bar.rectTransform.localScale = new Vector3(Mathf.Lerp(.15f, 1f, t),
                                                           Mathf.Lerp(.5f, 1f, t), 1f);
                bar.color = Pal.A(tint, t * .78f);
            }, bar);
        }

        void Unlink(int key, bool animate)
        {
            if (!_links.TryGetValue(key, out var bar)) return;
            _links.Remove(key);

            if (bar == null) return;

            if (!animate) { Destroy(bar.gameObject); return; }

            Tween.KillAll(bar);
            Tween.Fade(bar, 0f, Douse, Ease.OutQuad)
                 .OnDone(() => { if (bar) Destroy(bar.gameObject); });
        }

        /// <summary>Every link touching this cell, taken away.</summary>
        void UnlinkAround(int cell, bool animate)
        {
            _layout.Around(cell, _around);
            for (int i = 0; i < _around.Count; i++) Unlink(LinkKey(cell, _around[i]), animate);
        }

        // ------------------------------------------------------------------ painting
        protected override void Repaint()
        {
            if (_layout == null) return;

            foreach (var pair in _live)
                if (pair.Value.Node != null) Destroy(pair.Value.Node.gameObject);

            _live.Clear();
            Deal();

            foreach (var pair in _links)
                if (pair.Value != null) Destroy(pair.Value.gameObject);
            _links.Clear();

            Relink(false);
            Lights();
        }

        /// <summary>Puts every gem and every lantern in step with the board, without animating.</summary>
        void Lights()
        {
            foreach (var pair in _live)
            {
                var piece = pair.Value;

                if (PrismLayout.IsGem(piece.What)) Shine(piece, _board.Vein(pair.Key) >= 0);
                else if (PrismLayout.IsLamp(piece.What)) Feeding(piece, Reaching(pair.Key));
            }
        }

        /// <summary>Whether this lantern is lighting anything at all.</summary>
        bool Reaching(int cell)
        {
            int hue = PrismLayout.HueOf(_board.At(cell));
            if (hue < 0) return false;

            _layout.Around(cell, _around);
            for (int i = 0; i < _around.Count; i++)
                if (_board.Vein(_around[i]) == hue) return true;

            return false;
        }

        // ------------------------------------------------------------------ input
        /// <summary>The cell a finger is on, or -1. Only ever used to put it back down.</summary>
        int _held = -1;

        /// <summary>
        /// One hit target per cell.
        ///
        /// <b>Per cell rather than one catcher over the whole field</b>, which is
        /// <c>EmberView</c>'s idiom and right for the same reason: a gem is a rounded shape drawn
        /// inside a square, so hit-testing the <em>cell</em> and then asking the board what is
        /// standing there keeps the drawing and the rule from ever disagreeing about what was
        /// touched. A target sits on bare ground too, so a drag off the edge of the field is
        /// answered rather than swallowed.
        /// </summary>
        void Targets()
        {
            for (int cell = 0; cell < _layout.Count; cell++)
            {
                int at = cell;

                var img = UIKit.Img("hit", Field, Art.Pixel, new Color(0f, 0f, 0f, 0f),
                                    new Vector2(Cell, Cell));
                img.raycastTarget = true;
                img.rectTransform.anchoredPosition = CentreOf(at);

                var btn = img.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Tap(at), silent: true);

                var drag = img.gameObject.AddComponent<CellDrag>();
                drag.Threshold = Cell * .26f;
                drag.Began = () => Lift(at);
                drag.Ended = Drop;
                drag.Dragged = dir => Drag(at, dir);
            }
        }

        /// <summary>
        /// A tap with no drag behind it.
        ///
        /// <b>Not a move</b>, because this mode has exactly one verb and it is a drag. What a tap
        /// is for is answering it: a tap on a gem says "this one moves, take it somewhere", and a
        /// tap on a lantern or a husk is the one rule the board cannot show for itself.
        /// </summary>
        void Tap(int cell)
        {
            Drop();
            if (!Playable) return;

            char what = _board.At(cell);

            if (PrismLayout.IsGem(what))
            {
                Audio.Sfx("tock", .26f, 1.25f);
                if (_live.TryGetValue(cell, out var piece) && piece.Node != null)
                    Tween.Shake(piece.Node, Cell * .05f, .18f);
                return;
            }

            if (PrismLayout.IsLamp(what) || PrismLayout.IsCritter(what))
            {
                Refused?.Invoke();
                if (_live.TryGetValue(cell, out var piece)) Refuse(piece.Node);
                return;
            }

            Audio.Sfx("blocked", .2f, 1.3f);
        }

        /// <summary>The gem under the finger lifts. Geometry and never outcome (invariant 32c).</summary>
        void Lift(int cell)
        {
            Drop();
            if (!Playable || !PrismLayout.IsGem(_board.At(cell))) return;

            _held = cell;

            if (_live.TryGetValue(cell, out var piece) && piece.Node != null)
                Tween.Scale(piece.Node, piece.Lit ? 1.22f : 1.1f, .12f, Ease.OutBack);
        }

        void Drop()
        {
            if (_held < 0) return;

            if (_live.TryGetValue(_held, out var piece) && piece.Node != null)
                Tween.Scale(piece.Node, piece.Lit ? 1.06f : .92f, .12f, Ease.OutQuad);

            _held = -1;
        }

        void Drag(int cell, Vector2Int dir)
        {
            Drop();
            if (!Playable) return;

            int w = _layout.Width;
            int x = cell % w + dir.x, y = cell / w - dir.y;

            if (x < 0 || y < 0 || x >= w || y >= _layout.Height)
            {
                Audio.Sfx("blocked", .24f, 1.2f);
                return;
            }

            int to = y * w + x;
            var move = new PrismMove(cell, to);

            if (!_board.CanSwap(move.From, move.To))
            {
                Nudge(cell, to);

                // Dragging a gem onto a lantern, a husk or bare ground is the one thing that
                // wants a sentence; two gems of one colour refusing to trade places explains
                // itself the moment they lean into each other and come back.
                if (!PrismLayout.IsGem(_board.At(to)) || !PrismLayout.IsGem(_board.At(cell)))
                    Refused?.Invoke();

                return;
            }

            StartCoroutine(Play(move));
        }

        /// <summary>
        /// A swap that cannot happen: both pieces lean into each other and come back.
        ///
        /// Every mode here has inputs that mean nothing, and the difference between a board that
        /// feels broken and one that feels solid is whether it answers them. This one is the
        /// genre's own answer, and it is the reason a refused swap does not need a sentence.
        /// </summary>
        void Nudge(int from, int to)
        {
            Audio.Sfx("blocked", .3f, 1.1f);

            Vector2 home = CentreOf(from), there = CentreOf(to);
            var lean = (there - home) * .26f;

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

        // ------------------------------------------------------------------ the move
        IEnumerator Play(PrismMove move)
        {
            Busy = true;
            HideCoach();

            var log = _board.Play(move);
            if (log == null) { Busy = false; yield break; }

            Took(log.Woke);

            yield return Resolve(log);

            Busy = false;
            Settle();
        }

        /// <summary>
        /// The whole of one swap, and the only animation in this mode that matters.
        ///
        /// <para>
        /// Four beats, in this order and for these reasons. The two gems <b>slide</b> past each
        /// other, which is the beat the finger is waiting on and the only one that is about the
        /// input. Anything the swap <b>put out</b> goes dark next, before the good news, because
        /// a cost shown after a reward is a cost nobody reads. The vein then <b>runs</b> — cell
        /// by cell, outward from the lantern, on a rising note — which is the mode's payoff and
        /// gets the biggest drawing in it. And only then do the <b>critters wake</b>, because
        /// those are consequences and a consequence drawn at the same instant as its cause reads
        /// as one event.
        /// </para>
        /// </summary>
        IEnumerator Resolve(PrismFlare log)
        {
            yield return Exchange(log.From, log.To);

            // What went out. Drawn before what came on, so a swap that traded one vein for
            // another reads as a trade rather than as a win.
            bool doused = false;
            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Deed != PrismDeed.Dark) continue;

                doused = true;
                if (_live.TryGetValue(deed.At, out var piece)) Shine(piece, false, true);
                UnlinkAround(deed.At, true);
            }

            if (doused)
            {
                Audio.Sfx("tock", .3f, .78f);
                yield return new WaitForSeconds(Douse * .5f);
            }

            // The run. One cell at a time, outward from the lantern, because the log is in flood
            // order — which is the whole reason the board hands one back rather than being asked
            // to work it out here (invariant 30i).
            int step = 0;
            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Deed != PrismDeed.Lit) continue;

                Alight(deed.At, deed.Hue, step);
                step++;

                yield return new WaitForSeconds(Step);
            }

            // Anything the one-at-a-time walk could not reach — a link between two cells that
            // were both already lit and have only now become neighbours. Idempotent, so it puts
            // nothing back that the walk already drew.
            Relink(true);
            LampsInStep();

            if (step > 0) yield return new WaitForSeconds(Spark);

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                if (log.Deeds[i].Deed != PrismDeed.Wake) continue;
                yield return Wake(log.Deeds[i]);
            }

            if (step == 0 && !doused && log.Woke == 0) yield return new WaitForSeconds(.08f);
        }

        /// <summary>
        /// Two gems changing places, drawn as two gems changing places.
        ///
        /// They cross rather than teleport, and the one under the finger goes over the top —
        /// which is what makes a swap read as the player's own move rather than as the board
        /// rearranging itself.
        /// </summary>
        IEnumerator Exchange(int from, int to)
        {
            _live.TryGetValue(from, out var a);
            _live.TryGetValue(to, out var b);

            Audio.Sfx("poke", .34f, 1.1f);

            if (a != null && a.Node != null)
            {
                a.Node.SetAsLastSibling();
                Tween.Move(a.Node, CentreOf(to), Slide, Ease.OutQuad);
            }

            if (b != null && b.Node != null)
                Tween.Move(b.Node, CentreOf(from), Slide, Ease.OutQuad);

            yield return new WaitForSeconds(Slide);

            // The map is re-keyed rather than the nodes rebuilt, so the tween that has just run
            // is on the node that is still standing there.
            if (a != null) { a.Cell = to; _live[to] = a; }
            if (b != null) { b.Cell = from; _live[from] = b; }

            if (a == null) _live.Remove(to);
            if (b == null) _live.Remove(from);
        }

        /// <summary>
        /// One cell of the vein arriving: the gem lights, the bar to whatever fed it grows out,
        /// and a note sounds a semitone above the last one.
        ///
        /// <b>The rising pitch is the run.</b> A vein six cells long that sounded the same note
        /// six times would read as six separate events; the same six climbing read as one thing
        /// travelling, which is what it is.
        /// </summary>
        void Alight(int cell, int hue, int step)
        {
            if (_live.TryGetValue(cell, out var piece)) Shine(piece, true, true);

            var tint = Tint(hue < 0 ? 0 : hue);

            // Only the links back toward what fed this cell, so the line grows outward. A link
            // to a neighbour that is not lit yet is drawn when *that* cell's turn comes.
            _layout.Around(cell, _around);
            for (int i = 0; i < _around.Count; i++)
            {
                int nb = _around[i];
                if (!Wants(cell, nb, out var bar)) continue;
                if (_links.ContainsKey(LinkKey(cell, nb))) continue;

                bool fed = PrismLayout.IsLamp(_board.At(nb)) ||
                           (_live.TryGetValue(nb, out var other) && other.Lit);
                if (!fed) continue;

                Link(cell, nb, bar, true);
            }

            Pop(CentreOf(cell), tint, 1.5f, .26f);
            Audio.Sfx("lit", .34f, Mathf.Min(1.9f, .92f + step * .06f));
        }

        /// <summary>Every lantern's own drawing, in step with whether it is feeding anything.</summary>
        void LampsInStep()
        {
            foreach (var pair in _live)
                if (PrismLayout.IsLamp(pair.Value.What)) Feeding(pair.Value, Reaching(pair.Key));
        }

        /// <summary>
        /// A critter waking: the husk breaks open and it goes home over the top of the field.
        ///
        /// The bloom flare rather than the warm one when the light that reached it came from more
        /// than one gem away — which is the one place this mode says out loud that a long vein is
        /// a bigger achievement than a short one. Written out as two calls rather than one
        /// indexed by a flag, so <c>artnames.py</c> holds both to disk.
        /// </summary>
        IEnumerator Wake(PrismDeedRecord deed)
        {
            Vector2 where = CentreOf(deed.At);
            var made = Tint(deed.Hue < 0 ? 0 : deed.Hue);

            if (deed.Hue >= 0) Boom(Blaze("flare_bloom"), where, 2.6f);
            else Boom(Blaze("flare_warm"), where, 2.4f);

            Shockwave(where, made, 3.2f, .5f);
            Burst.Sparks(_fx, where, made, 18, 220f, 26f, .55f);
            Audio.Sfx("free", .85f, 1f);
            ShakeBoard(8f);

            if (_live.TryGetValue(deed.At, out var husk))
            {
                _live.Remove(deed.At);
                var node = husk.Node;
                if (node != null)
                {
                    Tween.KillAll(node);
                    Tween.Scale(node, .05f, .20f, Ease.InBack)
                         .OnDone(() => { if (node) Destroy(node.gameObject); });
                }
            }

            var img = Loose(deed.At, _fx, Cell * 1.1f);
            if (img != null)
            {
                img.rectTransform.anchoredPosition = where;

                // Up and out of the grove: the critter was asleep on the floor, so away and
                // upward is the direction that says what has just happened.
                Vector2 home = where + new Vector2(0f, Span.y * .5f + Cell);

                Tween.Run(RunHome, Ease.OutQuad, t =>
                {
                    if (!img) return;
                    var p = Vector2.Lerp(where, home, t);
                    p.x += Mathf.Sin(t * Mathf.PI * 2.2f) * Cell * .3f;
                    img.rectTransform.anchoredPosition = p;
                    img.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.1f, .66f, t);
                    img.color = Pal.A(Color.white, t > .7f ? (1f - t) / .3f : 1f);
                }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
            }

            _live[deed.At] = Make(PrismLayout.Awake, deed.At);

            yield return new WaitForSeconds(.24f);
        }

        /// <summary>
        /// The flare. Licensed frames rather than a particle spray, because a critter waking is
        /// the largest thing that happens in this mode and a spray reads as dust.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand</b> (invariant 7b): an
        /// <c>Image</c> with a null sprite is a white rectangle, so a flipbook attached to an
        /// empty folder leaves a square two cells wide sitting on the board at every waking.
        /// </para>
        /// <para>
        /// <b>It takes frames rather than a key</b>, so every caller writes
        /// <c>Boom(Blaze("flare_bloom"), …)</c> and the literal lands in a manifest wrapper's own
        /// first argument — the one shape <c>Tools/verify/artnames.py</c> can hold to disk.
        /// </para>
        /// </summary>
        void Boom(Sprite[] frames, Vector2 where, float size)
        {
            if (frames == null || frames.Length == 0)
            {
                Shockwave(where, Pal.Cream, 2.6f, .4f);
                return;
            }

            var img = UIKit.Img("Flare", _fx, frames[0], Color.white,
                                new Vector2(Cell * size, Cell * size));
            img.raycastTarget = false;
            img.rectTransform.anchoredPosition = where;

            var book = Flipbook.Attach(img, frames, 24f, false);
            book.OnFinished = () => { if (img) Destroy(img.gameObject); };

            // A guard as well as the callback: a flipbook whose owner is torn down mid-play never
            // reaches OnFinished, and these are created faster than anything else destroys them.
            Tween.After(1.4f, () => { if (img) Destroy(img.gameObject); }, this);
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>
        /// What the verb lesson rings: a gem standing beside a lantern, because that is where the
        /// first useful drag is.
        /// </summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null) return -1;

                for (int cell = 0; cell < _layout.Count; cell++)
                {
                    if (!PrismLayout.IsLamp(_board.At(cell))) continue;

                    _layout.Around(cell, _around);
                    for (int i = 0; i < _around.Count; i++)
                        if (PrismLayout.IsGem(_board.At(_around[i]))) return _around[i];
                }

                for (int cell = 0; cell < _layout.Count; cell++)
                    if (PrismLayout.IsGem(_board.At(cell))) return cell;

                return -1;
            }
        }

        /// <summary>What the second lesson rings: the first critter still asleep.</summary>
        public override int FriendCell
        {
            get
            {
                if (_board == null) return -1;

                for (int cell = 0; cell < _layout.Count; cell++)
                    if (_board.At(cell) == PrismLayout.Asleep) return cell;

                return -1;
            }
        }

        // ------------------------------------------------------------------ the endings
        protected override IEnumerator Triumph()
        {
            // The grove lights right up rather than merely celebrating: what the player did was
            // bring light back to it, so the thing that has been dark all run is the thing that
            // has to visibly stop being dark.
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] == null) continue;
                if (_layout.At(i) == PrismLayout.Bare) continue;

                _tiles[i].sprite = Cut("moss_lit");
                Tween.Tint(_tiles[i], Pal.Lift(Color.white, .3f), .5f);
            }

            Audio.Sfx("chime2", .6f, 1.1f);

            yield return new WaitForSeconds(.20f);
            yield return base.Triumph();
        }

        protected override void OnDestroy()
        {
            Drop();
            base.OnDestroy();
        }
    }
}
