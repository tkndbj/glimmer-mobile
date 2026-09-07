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
    /// Kindlewake drawn: a dark hollow with cold embers scattered over it, critters asleep in the
    /// moss, and the light that runs between two embers when they are joined.
    ///
    /// <para>
    /// <b>Everything here is in service of one moment.</b> Two embers of a colour are picked, a
    /// bolt of that colour races out of each of them, the two meet in the middle of the line with
    /// a flash, and what is left behind is a strand that stays lit for the rest of the run. Every
    /// critter it crosses takes the colour — and where the new strand runs over one that was
    /// already there, the crossing flares in a colour <em>neither</em> of them carried, which is
    /// the thing the player arranged and the moment the whole mode is built to sell (invariant
    /// 20m's first rule, and the Tanglewood's lesson: the payoff gets the biggest drawing in the
    /// mode).
    /// </para>
    /// <para>
    /// <b>The hollow replays a log rather than reconstructing one.</b> <c>KindleFlare</c> comes
    /// back naming the strand's two ends, every cell it lit, every crossing it made and every
    /// critter it woke, so nothing here works out what a strand must have done — invariant 30i
    /// taken seriously, because that arithmetic is exactly what no par, no <c>ways</c>, no
    /// validator and no content gate can ever see going wrong.
    /// </para>
    /// <para>
    /// <b>What the aim preview may show is geometry and never outcome</b> (invariant 32c):
    /// holding an ember lights its partners and the lines it could draw to them, which are facts
    /// the player can already read off the hollow drawn faster. What it never draws is which
    /// critters would wake, because that is the arithmetic they are here to do — Budburst's
    /// withdrawn halo (invariant 20l), and the reason it was withdrawn.
    /// </para>
    /// </summary>
    public sealed class KindleView : ProtoView
    {
        // ------------------------------------------------------------------ tempo
        /// <summary>The two bolts racing out of the embers toward each other.</summary>
        const float RunOut = .30f;

        /// <summary>The flash where they meet, and the strand settling out of it.</summary>
        const float Meet = .16f, Settle = .22f;

        /// <summary>A crossing flaring into a colour neither strand carried.</summary>
        const float CrossStep = .30f;

        /// <summary>A critter's climb out of the moss.</summary>
        const float RunHome = .78f;

        // ------------------------------------------------------------------ the hollow
        KindleLayout _layout;
        KindleBoard _board;

        RectTransform _ground, _strands, _pools, _pieces, _fx;

        readonly List<Image> _tiles = new List<Image>(64);
        readonly List<Image> _lit = new List<Image>(64);
        readonly Dictionary<int, Piece> _live = new Dictionary<int, Piece>(64);
        readonly List<KindleMove> _moves = new List<KindleMove>(32);

        /// <summary>The ember a finger is holding, or -1. The mode's whole input state.</summary>
        int _held = -1;

        /// <summary>
        /// One drawn thing standing on a cell: the node, the body, and whatever hangs on it.
        ///
        /// A class rather than a struct because the map is re-keyed as pieces come and go and
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
        /// path</b>, which is invariant 7 and was learned the expensive way two modes ago:
        /// <c>MarchView.Boom</c> spelt its own folder out, asked for the wrong one, and drew a
        /// white rectangle two cells wide at every burst that a player found before any gate did.
        /// </summary>
        static Sprite Cut(string key) => AssetLibrary.Sprite(AssetManifest.KindleArt(key));

        /// <summary>
        /// One of this mode's flipbooks, as frames.
        ///
        /// A one-line wrapper rather than the call spelt out inside <see cref="Book"/>, so that
        /// <c>Tools/verify/artnames.py</c> can read it: the gate follows a thin wrapper whose
        /// whole body is a manifest lookup, which is what makes every <c>Reel("mon1")</c> below a
        /// name something checks. Centralising a lookup is only safe if the check follows it.
        /// </summary>
        static Sprite[] Reel(string key) => AssetLibrary.Frames(AssetManifest.KindleArt(key));

        /// <summary>This mode's flares, which live under Fx rather than beside the hollow.</summary>
        static Sprite[] Blaze(string key) => AssetLibrary.Frames(AssetManifest.KindleFx(key));

        /// <summary>
        /// A flipbook widget, or <b>null</b> when its frames are not there.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand.</b> An <c>Image</c> with a
        /// null sprite is a <em>white rectangle</em> and not a blank (invariant 7b), so attaching
        /// a flipbook to an empty folder puts a solid square on the hollow rather than nothing at
        /// all. Built this way round, missing art costs the thing it draws and never costs a
        /// square of white over the board.
        /// </para>
        /// <para>
        /// <b>It takes frames rather than a key</b>, so every caller writes
        /// <c>Book(Reel("mon1"), …)</c> and the literal lands in a manifest wrapper's own first
        /// argument — the one shape <c>Tools/verify/artnames.py</c> can hold to disk. A key handed
        /// to a method like this one is a name the check cannot follow.
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
        /// The three embers, written out rather than indexed, so <c>artnames.py</c> holds all
        /// three to disk.
        /// </summary>
        static Sprite Face(char what)
        {
            switch (what)
            {
                case 'r': return Cut("ember_r");
                case 'g': return Cut("ember_g");
                case 'b': return Cut("ember_b");
                case KindleLayout.Stone: return Cut("stone");
                case KindleLayout.Spent: return Cut("socket");
                default: return Cut("husk");
            }
        }

        // ------------------------------------------------------------------ building
        protected override void Compose()
        {
            _layout = ((KindleRules)Rules).Layout;
            _board = (KindleBoard)Run.Board;

            _tiles.Clear();
            _lit.Clear();
            _live.Clear();
            _held = -1;

            // **The order is the whole legibility of the mode and a render is what settled
            // it.** Drawn strands-then-pools, a crossing is covered by the two bars that made
            // it - so the one square carrying a colour neither strand had, which is the thing
            // the player arranged, is the one square you cannot see (invariant 20m). The route
            // goes underneath and the *state* goes on top.
            _ground = Layer("Ground");
            _strands = Layer("Strands");
            _pools = Layer("Pools");
            _pieces = Layer("Pieces");
            _fx = Layer("Fx");

            Moss();
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
        /// The hollow's floor, one tile per cell, and over each of them the pool of light that
        /// cell is holding.
        ///
        /// <para>
        /// <b>The pool is a separate image from the tile and is never taken away</b>, because
        /// light is the one thing on this board that only ever accumulates: it is what the player
        /// has already spent the hollow on, so it has to be legible for the rest of the run
        /// rather than only while it is arriving. A cell with no light draws its pool fully
        /// transparent, which costs one quad and is what lets a strand's arrival be a tween on
        /// something that already exists rather than a node built mid-animation.
        /// </para>
        /// </summary>
        void Moss()
        {
            for (int i = 0; i < _layout.Grid.Count; i++)
            {
                var tile = UIKit.Img("t", _ground, Cut("moss"), Color.white,
                                     new Vector2(Cell, Cell));
                tile.raycastTarget = false;
                tile.rectTransform.anchoredPosition = CentreOf(i);
                _tiles.Add(tile);

                var pool = UIKit.Img("lit", _pools, Art.Glow(96, 1.5f),
                                     new Color(1f, 1f, 1f, 0f),
                                     new Vector2(Cell * 1.02f, Cell * 1.02f));
                pool.raycastTarget = false;
                pool.rectTransform.anchoredPosition = CentreOf(i);
                _lit.Add(pool);
            }
        }

        void Deal()
        {
            for (int i = 0; i < _layout.Grid.Count; i++)
            {
                char c = _board.At(i);
                if (c == KindleLayout.Bare) continue;
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

            if (KindleLayout.IsSleeper(what))
            {
                // A husk fills its cell and the critter sleeps inside it, which is the render's
                // answer to invariant 32b: the goal has to win a legibility fight against a
                // scatter of bright gems, and it does that by being the biggest thing in the
                // square and the only thing wearing a colour that is not an ember's.
                piece.Body = UIKit.Img("Husk", node, Cut("husk"), Color.white,
                                       new Vector2(Cell, Cell));
                piece.Body.raycastTarget = false;

                piece.Crew = Sleeping(cell, node, Cell * .66f);
                if (piece.Crew != null) piece.Crew.transform.SetAsFirstSibling();

                Wants(piece, KindleLayout.WantOf(what));
                return piece;
            }

            if (what == KindleLayout.Woken)
            {
                piece.Body = UIKit.Img("Husk", node, Cut("husk"), Pal.A(Color.white, .35f),
                                       new Vector2(Cell, Cell));
                piece.Body.raycastTarget = false;
                return piece;
            }

            float draw = what == KindleLayout.Stone ? 1f : .82f;

            piece.Body = UIKit.Img("Body", node, Face(what), Color.white,
                                   new Vector2(Cell * draw, Cell * draw));
            piece.Body.raycastTarget = false;

            if (KindleLayout.IsEmber(what)) Kindle(piece, KindleLayout.ChannelOf(what));

            return piece;
        }

        /// <summary>
        /// The ring of colour a sleeping critter is asking for.
        ///
        /// <para>
        /// <b>The one readout on this board, and it has to be, because the mode's whole question
        /// is "what is this one short of".</b> It is drawn as the <em>wanted</em> colour rather
        /// than as what it currently holds, because that is the thing the player is aiming at —
        /// and what it holds is already on the floor underneath it as the pool of light, so the
        /// two together read as a gap closing.
        /// </para>
        /// </summary>
        void Wants(Piece piece, int mask)
        {
            if (piece.Node == null) return;

            var tint = Pal.EnergyColour(mask);

            var ring = UIKit.Img("Want", piece.Node, Art.Ring(128, 9f), Pal.A(tint, .95f),
                                 new Vector2(Cell * .92f, Cell * .92f));
            ring.raycastTarget = false;
            ring.transform.SetAsFirstSibling();
            piece.Trim = ring;

            Tween.Run(1.6f, Ease.InOutSine, t =>
            {
                if (!ring) return;
                float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                ring.color = Pal.A(tint, Mathf.Lerp(.55f, 1f, k));
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

        /// <summary>
        /// An ember's own light: a slow breath in the colour it carries.
        ///
        /// An ember is the only thing on this hollow that is <em>moving</em> while nothing is
        /// happening, which is what makes a player look at one and want to touch it — and it is
        /// the only thing a finger can touch, so nothing else on the board may pulse.
        /// </summary>
        void Kindle(Piece piece, int channel)
        {
            if (piece.Node == null) return;

            var tint = Pal.EnergyColour(channel);

            var halo = UIKit.Halo(piece.Node, tint, Cell * 1.25f, .40f);
            halo.transform.SetAsFirstSibling();
            piece.Trim = halo;

            var body = piece.Body;

            Tween.Run(1.1f, Ease.InOutSine, t =>
            {
                float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                if (halo) halo.color = Pal.A(tint, Mathf.Lerp(.20f, .55f, k));
                if (body) body.transform.localScale = Vector3.one * Mathf.Lerp(.97f, 1.05f, k);
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

            // The light is state and is repainted from the board rather than remembered, so a
            // rebuild after a continue puts back exactly what the run has already spent.
            for (int i = 0; i < _lit.Count; i++) Pool(i, _board.Light(i), 0f);

            Unaim();
        }

        /// <summary>
        /// Puts a cell's pool of light where the board says it is.
        ///
        /// <para>
        /// <b>Only where two or more channels meet, and that is the point rather than a saving.</b>
        /// A cell carrying one channel is already drawn — the strand runs straight through it and
        /// is that colour. A cell carrying two is a colour <em>neither</em> strand had, and it is
        /// the only thing on this board the player had to arrange, so it gets a mark of its own
        /// that outlives the flare (invariant 20m). Painting single-channel cells as well turned
        /// every strand into a chain of discs and made the crossing the least distinct thing on
        /// the line.
        /// </para>
        /// <para>
        /// The colour is <c>Pal.EnergyColour</c> and nothing else, which is what makes it legible
        /// without a word: two channels on one cell draw the blend every glade has drawn for four
        /// chapters, so a player arriving from the classic mode already knows what red over blue
        /// makes here.
        /// </para>
        /// </summary>
        void Pool(int cell, int mask, float seconds)
        {
            if (cell < 0 || cell >= _lit.Count) return;

            var img = _lit[cell];
            if (img == null) return;

            int channels = KindleLayout.Channels(mask);

            var want = channels < 2
                     ? new Color(1f, 1f, 1f, 0f)
                     : Pal.A(Pal.EnergyColour(mask), channels > 2 ? .92f : .78f);

            if (seconds <= 0f) { img.color = want; return; }

            Tween.Tint(img, want, seconds, Ease.OutQuad);
        }

        // ------------------------------------------------------------------ input
        /// <summary>
        /// One hit target per cell.
        ///
        /// <b>Per cell rather than one catcher over the whole hollow</b>, which is
        /// <c>BudView</c>'s idiom and right for the same reason: an ember is a rounded shape drawn
        /// inside a square, so hit-testing the <em>cell</em> and then asking the board what is
        /// standing there keeps the drawing and the rule from ever disagreeing about what was
        /// touched. A target sits on bare moss too, so a tap on an empty stretch is answered
        /// rather than swallowed — which is how a held ember is let go.
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
            }
        }

        /// <summary>
        /// A tap: pick an ember up, join it to a second, or put it down.
        ///
        /// <para>
        /// <b>Two taps rather than a drag, and that is a fact about the mode rather than a
        /// preference.</b> The two embers of a strand can be at opposite ends of the hollow, so a
        /// drag would have to be tracked across the whole board and would be indistinguishable
        /// from a scroll; and the pair is the decision, so it is worth a moment of holding one
        /// while the hollow shows what it can reach.
        /// </para>
        /// </summary>
        void Tap(int cell)
        {
            if (!Playable) { Unaim(); return; }

            // Putting the held ember down. Silent, because it is not a refusal - it is the
            // player changing their mind, and a board that thumps at that reads as broken.
            if (_held == cell)
            {
                Audio.Sfx("tock", .25f, 1.3f);
                Unaim();
                return;
            }

            if (_held >= 0)
            {
                var move = new KindleMove(_held, cell);

                if (_board.Join(move.From, move.To, out _))
                {
                    Unaim();
                    StartCoroutine(Play(move));
                    return;
                }

                // A second tap that is not a partner picks the new ember up instead of scolding,
                // which is what a player reaching across a board actually means by it. Only a tap
                // on something that is not an ember at all is a refusal.
                if (KindleLayout.IsEmber(_board.At(cell))) { Aim(cell); return; }

                Unaim();
                Audio.Sfx("blocked", .26f, 1.2f);
                return;
            }

            if (KindleLayout.IsEmber(_board.At(cell))) { Aim(cell); return; }

            // The one rule the hollow cannot show for itself: light comes from embers, so a tap
            // on a sleeping critter is the tap a new player makes first and the one that has to
            // be answered in words.
            if (KindleLayout.IsSleeper(_board.At(cell)))
            {
                Refused?.Invoke();
                if (_live.TryGetValue(cell, out var piece)) Refuse(piece.Node);
                return;
            }

            Audio.Sfx("blocked", .22f, 1.25f);
        }

        // ------------------------------------------------------------------ the aim
        readonly List<Image> _hints = new List<Image>(16);

        /// <summary>
        /// Picks an ember up and shows what it can reach: every partner of its own colour, and
        /// the line to each of them.
        ///
        /// <para>
        /// <b>Geometry and never outcome</b> (invariant 32c). Which embers share a clear row or
        /// column with this one is a fact the player could read off the hollow by squinting along
        /// it, so drawing it is drawing the same fact faster. What is deliberately absent is any
        /// mark saying which critter would wake — that is the arithmetic the mode exists to ask,
        /// and printing it is Budburst's withdrawn halo (invariant 20l).
        /// </para>
        /// </summary>
        void Aim(int cell)
        {
            Unaim();

            _held = cell;
            Audio.Sfx("poke", .40f, 1.15f);

            if (_live.TryGetValue(cell, out var piece) && piece.Node != null)
                Tween.Scale(piece.Node, 1.16f, .14f, Ease.OutBack);

            _board.Joins(_moves);

            var tint = Pal.EnergyColour(KindleLayout.ChannelOf(_board.At(cell)));

            for (int i = 0; i < _moves.Count; i++)
            {
                var move = _moves[i];
                if (move.From != cell && move.To != cell) continue;

                int other = move.From == cell ? move.To : move.From;

                Thread(CentreOf(cell), CentreOf(other), tint, .22f, Cell * .16f);
                Along(cell, other);

                var mark = UIKit.Img("Can", _fx, Art.Ring(96, 7f), Pal.A(tint, .85f),
                                     new Vector2(Cell * .96f, Cell * .96f));
                mark.raycastTarget = false;
                mark.rectTransform.anchoredPosition = CentreOf(other);
                _hints.Add(mark);

                var rt = mark.rectTransform;
                Tween.Run(.9f, Ease.InOutSine, t =>
                {
                    if (!mark) return;
                    float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                    rt.localScale = Vector3.one * Mathf.Lerp(.92f, 1.06f, k);
                }, mark).Loop();
            }
        }

        /// <summary>
        /// Lights the moss under every cell a strand between these two would cross.
        ///
        /// <b>The ground rather than an overlay</b>, because the thing being previewed is which
        /// squares the light would land on - and a marker floating above them would be a second
        /// vocabulary for a fact the floor can state itself. It is put back by
        /// <see cref="Unaim"/> rather than tracked, so nothing has to remember which tiles it lit.
        /// </summary>
        void Along(int from, int to)
        {
            int w = _layout.Width;
            int ax = from % w, ay = from / w, bx = to % w, by = to / w;

            int stepX = ax == bx ? 0 : (bx > ax ? 1 : -1);
            int stepY = ay == by ? 0 : (by > ay ? 1 : -1);

            int x = ax, y = ay;
            while (true)
            {
                int cell = y * w + x;
                if (cell >= 0 && cell < _tiles.Count && _tiles[cell] != null)
                    _tiles[cell].sprite = Cut("moss_lit");

                if (x == bx && y == by) break;

                x += stepX;
                y += stepY;
            }
        }

        void Unaim()
        {
            for (int i = 0; i < _tiles.Count; i++)
                if (_tiles[i] != null) _tiles[i].sprite = Cut("moss");


            if (_held >= 0 && _live.TryGetValue(_held, out var piece) && piece.Node != null)
                Tween.Scale(piece.Node, 1f, .12f, Ease.OutQuad);

            _held = -1;

            for (int i = 0; i < _hints.Count; i++)
                if (_hints[i] != null) Destroy(_hints[i].gameObject);

            _hints.Clear();
        }

        /// <summary>
        /// A faint line between two cells, for the aim preview. The strand's own drawing is
        /// <see cref="Strand"/>; this is the same geometry at a quarter of the weight.
        /// </summary>
        void Thread(Vector2 from, Vector2 to, Color tint, float alpha, float width)
        {
            var span = to - from;
            float length = span.magnitude;
            if (length < 1f) return;

            var bar = UIKit.Img("Aim", _fx, Art.Glow(96, 1.4f), Pal.A(tint, alpha),
                                new Vector2(length, width), new Vector2(.5f, .5f), from);
            bar.raycastTarget = false;
            bar.rectTransform.pivot = new Vector2(0f, .5f);
            bar.rectTransform.anchoredPosition = from;
            bar.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);

            _hints.Add(bar);
        }

        // ------------------------------------------------------------------ the move
        IEnumerator Play(KindleMove move)
        {
            Busy = true;
            HideCoach();

            var log = _board.Draw(move);
            if (log == null) { Busy = false; yield break; }

            Took(log.Woke * 4 + log.Crossings + 1);

            yield return Strike(log);

            Busy = false;
            Settle();
        }

        /// <summary>
        /// The whole of one join, and the only animation in this mode that matters.
        ///
        /// <para>
        /// Four beats, in this order and for these reasons. The <b>bolts</b> race out of both
        /// embers at once, so the player's eye is pulled along the line they chose rather than
        /// told about it. They <b>meet</b> in the middle with a flash, which is the beat that says
        /// the two embers were a pair. The <b>strand</b> then settles and stays — light never goes
        /// out in this mode, so its arrival has to end in something permanent rather than in a
        /// fade. And only then do the <b>crossings and the wakings</b> land, because those are
        /// consequences and a consequence drawn at the same instant as its cause reads as one
        /// event.
        /// </para>
        /// </summary>
        IEnumerator Strike(KindleFlare log)
        {
            var tint = Pal.EnergyColour(log.Channel);

            Vector2 a = CentreOf(log.From), b = CentreOf(log.To);
            Vector2 middle = (a + b) * .5f;

            Spend(log.From);
            Spend(log.To);

            Audio.Sfx("whoosh", .45f, 1.05f + log.Span * .02f);

            Bolt(a, middle, tint);
            Bolt(b, middle, tint);

            yield return new WaitForSeconds(RunOut);

            // The meeting. One flash on the middle of the line, which is the beat that says the
            // two embers were a pair rather than two separate things going off.
            Pop(middle, Pal.Lift(tint, .45f), 2.6f, Meet + .12f);
            Shockwave(middle, tint, 2.2f, .34f);
            Audio.Sfx("chime", .5f, 1.0f + KindleLayout.Channels(log.Channel) * .05f);

            yield return new WaitForSeconds(Meet);

            Strand(a, b, tint);

            // The light itself, laid down the line rather than all at once, so the eye follows it
            // outward from where the two bolts met.
            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Deed != KindleDeed.Light && deed.Deed != KindleDeed.Cross) continue;

                Pool(deed.At, deed.To, Settle);
            }

            yield return new WaitForSeconds(Settle);

            // The crossings. Drawn after the strand has settled, because a crossing is a fact
            // about two strands and only reads as one once the second is standing there.
            int crossings = 0;
            for (int i = 0; i < log.Deeds.Count; i++)
            {
                if (log.Deeds[i].Deed != KindleDeed.Cross) continue;

                crossings++;
                yield return Cross(log.Deeds[i]);
            }

            // The wakings last, and each with a beat of its own: a hollow that frees three
            // critters in one strand should read as three things happening, not one.
            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];

                if (deed.Deed == KindleDeed.Stir) Stir(deed);
                else if (deed.Deed == KindleDeed.Wake) yield return Wake(deed, crossings > 0);
            }

            if (log.Woke == 0 && crossings == 0) yield return new WaitForSeconds(.10f);
        }

        /// <summary>
        /// One bolt racing out of an ember toward the middle of the line.
        ///
        /// Rotated from a pivot at the ember rather than drawn cell by cell, because a bolt that
        /// arrives all at once reads as a bolt where a row of flashes reads as a row of flashes.
        /// Two bars rather than one — a coloured body and a white-hot core half the width — is
        /// what stops it reading as a coloured stripe.
        /// </summary>
        void Bolt(Vector2 from, Vector2 to, Color tint)
        {
            var span = to - from;
            float length = span.magnitude + Cell * .1f;
            if (length < Cell * .2f) return;

            float angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;

            var bar = UIKit.Img("Bolt", _fx, Art.Glow(96, 1.6f), Pal.A(tint, .95f),
                                new Vector2(length, Cell * .58f), new Vector2(.5f, .5f), from);
            bar.raycastTarget = false;
            bar.rectTransform.pivot = new Vector2(0f, .5f);
            bar.rectTransform.anchoredPosition = from;
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            bar.rectTransform.localScale = new Vector3(.04f, .5f, 1f);

            Tween.Run(RunOut, Ease.OutQuint, t =>
            {
                if (!bar) return;
                bar.rectTransform.localScale = new Vector3(t, Mathf.Lerp(.5f, 1.2f, t), 1f);
                bar.color = Pal.A(tint, t < .6f ? .95f : (1f - t) * 2.4f);
            }, bar).OnDone(() => { if (bar) Destroy(bar.gameObject); });

            var core = UIKit.Img("Core", _fx, Art.Pixel, Pal.A(Pal.Cream, .95f),
                                 new Vector2(length, Cell * .14f), new Vector2(.5f, .5f), from);
            core.raycastTarget = false;
            core.rectTransform.pivot = new Vector2(0f, .5f);
            core.rectTransform.anchoredPosition = from;
            core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            core.rectTransform.localScale = new Vector3(.04f, 1f, 1f);

            Tween.Run(RunOut * .85f, Ease.OutQuint, t =>
            {
                if (!core) return;
                core.rectTransform.localScale = new Vector3(t, 1f, 1f);
                core.color = Pal.A(Pal.Cream, 1f - t * .7f);
            }, core).OnDone(() => { if (core) Destroy(core.gameObject); });
        }

        /// <summary>
        /// The strand itself, which is the one thing this mode draws that never goes away.
        ///
        /// It lives on its own layer under the pieces, so a critter woken by it is drawn over the
        /// light that woke it rather than under it, and it breathes very slightly for the rest of
        /// the run — a hollow whose earlier work sat completely still would read as scenery
        /// rather than as something the player put there.
        /// </summary>
        void Strand(Vector2 from, Vector2 to, Color tint)
        {
            var span = to - from;
            float length = span.magnitude + Cell * .5f;

            var bar = UIKit.Img("Strand", _strands, Art.Glow(96, 1.35f), Pal.A(tint, 0f),
                                new Vector2(length, Cell * .34f), new Vector2(.5f, .5f), from);
            bar.raycastTarget = false;
            bar.rectTransform.pivot = new Vector2(0f, .5f);
            bar.rectTransform.anchoredPosition = from - span.normalized * (Cell * .25f);
            bar.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);

            Tween.Run(Settle, Ease.OutCubic, t =>
            {
                if (!bar) return;
                bar.color = Pal.A(tint, t * .55f);
            }, bar).OnDone(() =>
            {
                if (!bar) return;
                Tween.Run(2.3f, Ease.InOutSine, t =>
                {
                    if (!bar) return;
                    float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                    bar.color = Pal.A(tint, Mathf.Lerp(.42f, .62f, k));
                }, bar).Loop();
            });

            var core = UIKit.Img("Vein", _strands, Art.Pixel, Pal.A(Pal.Lift(tint, .5f), 0f),
                                 new Vector2(length, Cell * .09f), new Vector2(.5f, .5f), from);
            core.raycastTarget = false;
            core.rectTransform.pivot = bar.rectTransform.pivot;
            core.rectTransform.anchoredPosition = bar.rectTransform.anchoredPosition;
            core.rectTransform.localRotation = bar.rectTransform.localRotation;

            Tween.Fade(core, .42f, Settle, Ease.OutCubic);
        }

        /// <summary>
        /// A crossing: the biggest thing this mode draws, because it is the only thing on the
        /// board the player had to <em>arrange</em>.
        ///
        /// <para>
        /// It flares in the colour the cell now holds — which is a colour neither strand carried,
        /// and the whole reason the two were drawn through this square. Invariant 20m's first
        /// rule: the payoff gets the biggest drawing in the mode, and it is drawn as an
        /// <em>arrival</em> rather than as a change of tint, because a change of tint is what a
        /// player misses.
        /// </para>
        /// </summary>
        IEnumerator Cross(KindleDeedRecord deed)
        {
            Vector2 where = CentreOf(deed.At);
            var made = Pal.EnergyColour(deed.To);

            Boom(Blaze("flare_bloom"), where, 2.4f);
            Pop(where, made, 3.0f, .30f);
            Shockwave(where, made, 3.0f, .46f);
            Burst.Sparks(_fx, where, made, 16, 210f, 24f, .55f);

            Audio.Sfx("star", .6f, 1.05f + KindleLayout.Channels(deed.To) * .12f);
            ShakeBoard(9f);

            // The cell's own pool jumps and settles, so the eye is left on the square that
            // changed rather than on the flash that left it.
            if (deed.At >= 0 && deed.At < _lit.Count && _lit[deed.At] != null)
            {
                var pool = _lit[deed.At];
                var rt = pool.rectTransform;
                rt.localScale = Vector3.one * 1.5f;
                Tween.Scale(rt, 1f, CrossStep, Ease.OutBack);
            }

            yield return new WaitForSeconds(CrossStep * .62f);
        }

        /// <summary>A sleeping critter that took a channel and is still short: it stirs and settles.</summary>
        void Stir(KindleDeedRecord deed)
        {
            if (!_live.TryGetValue(deed.At, out var piece) || piece.Node == null) return;

            Tween.Shake(piece.Node, Cell * .07f, .26f);
            Burst.Sparks(_fx, CentreOf(deed.At), Pal.EnergyColour(deed.To), 6, 120f, 14f, .35f);
            Audio.Sfx("tock", .28f, 1.35f);
        }

        /// <summary>
        /// A critter waking: the husk breaks open and it goes home over the top of the hollow.
        ///
        /// The flare is warm for a critter woken by one strand and the bloom for one woken by a
        /// crossing, which is the one place this mode says out loud that the two are different
        /// achievements. Written out as two calls rather than one indexed by a flag, so
        /// <c>artnames.py</c> holds both to disk.
        /// </summary>
        IEnumerator Wake(KindleDeedRecord deed, bool blended)
        {
            Vector2 where = CentreOf(deed.At);
            var made = Pal.EnergyColour(deed.To);

            if (blended) Boom(Blaze("flare_bloom"), where, 2.6f);
            else Boom(Blaze("flare_warm"), where, 2.4f);

            Shockwave(where, made, 3.2f, .5f);
            Burst.Sparks(_fx, where, made, 18, 220f, 26f, .55f);
            Audio.Sfx("free", .85f, 1f);

            if (_live.TryGetValue(deed.At, out var husk))
            {
                _live.Remove(deed.At);
                var node = husk.Node;
                if (node != null)
                {
                    Tween.Scale(node, .05f, .20f, Ease.InBack)
                         .OnDone(() => { if (node) Destroy(node.gameObject); });
                }
            }

            var img = Loose(deed.At, _fx, Cell * 1.1f);
            if (img != null)
            {
                img.rectTransform.anchoredPosition = where;

                // Up and out of the hollow: the critter was asleep in the moss, so away and
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

            _live[deed.At] = Make(KindleLayout.Woken, deed.At);

            yield return new WaitForSeconds(.22f);
        }

        /// <summary>An ember going out, having been joined into a strand.</summary>
        void Spend(int cell)
        {
            if (!_live.TryGetValue(cell, out var piece)) return;
            _live.Remove(cell);

            var node = piece.Node;
            if (node != null)
            {
                Tween.KillAll(node);
                Tween.Scale(node, .04f, .16f, Ease.InBack)
                     .OnDone(() => { if (node) Destroy(node.gameObject); });
            }

            // A socket rather than bare moss, so the hollow keeps a record of what has been
            // spent: the material is the whole economy of this mode, and a board that quietly
            // forgot what it had used would be a board nobody could plan on.
            _live[cell] = Make(KindleLayout.Spent, cell);
        }

        /// <summary>
        /// The flare. Licensed frames rather than a particle spray, because a crossing is the
        /// largest thing that happens in this mode and a spray reads as dust.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand</b> (invariant 7b): an
        /// <c>Image</c> with a null sprite is a white rectangle, so a flipbook attached to an
        /// empty folder leaves a square two cells wide sitting on the board at every crossing.
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
        /// What the verb lesson rings: an ember that has a partner, so the tip points at
        /// something a first tap can actually do.
        /// </summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null) return -1;

                _board.Joins(_moves);
                return _moves.Count > 0 ? _moves[0].From : -1;
            }
        }

        /// <summary>
        /// What the second lesson rings: a critter wanting a blend if the hollow holds one, else
        /// any sleeper.
        ///
        /// A blend first, because the second lesson is about the crossing and a tip pointing at a
        /// critter one strand could wake would be teaching the rule against the one board that
        /// does not need it.
        /// </summary>
        public override int FriendCell
        {
            get
            {
                if (_board == null) return -1;

                for (int i = 0; i < _layout.Grid.Count; i++)
                {
                    char c = _board.At(i);
                    if (KindleLayout.IsSleeper(c) && KindleLayout.Channels(KindleLayout.WantOf(c)) > 1)
                        return i;
                }

                for (int i = 0; i < _layout.Grid.Count; i++)
                    if (KindleLayout.IsSleeper(_board.At(i))) return i;

                return -1;
            }
        }

        // ------------------------------------------------------------------ the endings
        protected override IEnumerator Triumph()
        {
            // The hollow lights right up rather than merely celebrating: what the player did was
            // bring light back to it, so the thing that has been dark all run is the thing that
            // has to visibly stop being dark.
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] == null) continue;
                Tween.Tint(_tiles[i], Pal.Lift(Color.white, .35f), .5f);
            }

            Audio.Sfx("chime2", .6f, 1.1f);

            yield return new WaitForSeconds(.20f);
            yield return base.Triumph();
        }

        protected override void OnDestroy()
        {
            Unaim();
            base.OnDestroy();
        }
    }
}
