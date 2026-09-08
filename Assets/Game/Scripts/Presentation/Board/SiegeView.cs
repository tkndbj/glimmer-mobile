using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>Thornwatch.</b> A hill with raiders coming down it, a line of wards holding it, and a
    /// field of gems underneath that feeds them.
    ///
    /// <para>
    /// <b>It inherits the latches and lays itself out.</b> <see cref="ProtoView"/> draws nothing
    /// but the plate and is explicit that a mode wanting a different shape may ignore every
    /// drawing helper and still take the part that is dangerous to get wrong — whether a finger
    /// does anything, and how a run ends. This board is three bands rather than one grid, so
    /// <see cref="Fit"/>, <see cref="Span"/> and <see cref="CentreOf"/> are all overridden and
    /// nothing else about being a board had to move.
    /// </para>
    /// <para>
    /// <b>The clock runs here and the rules are in Domain.</b> Nothing below decides anything: it
    /// hands <c>SiegeBoard.Advance</c> the seconds that have gone by and draws what it is told
    /// happened. That split is what lets the whole mode be stepped by a test at whatever rate a
    /// test likes, and it is why the one thing this class must never do is work out for itself
    /// whether a run is over.
    /// </para>
    /// <para>
    /// <b>The clock is not stopped by an animation, and that is deliberate.</b> <c>Busy</c> stops
    /// a second swap landing while the first is still falling; if it also stopped the hill, a
    /// player could hold time still by swapping. What stops the hill is exactly what stops a run —
    /// a lesson, the pause menu, the ending — which is <see cref="Live"/> below.
    /// </para>
    /// </summary>
    public sealed class SiegeView : ProtoView
    {
        // ------------------------------------------------------------------ shape
        /// <summary>
        /// How the height is split: the hill, the ward line, and the field.
        ///
        /// <para>
        /// Roughly forty / fifteen / forty, which is the arrangement the mode was commissioned as
        /// with the middle band widened once. <b>A render is why it was widened.</b> At a tenth of
        /// the height a ward's own furniture — its plinth, its fuel tube and its four health pips
        /// — is nearly two cells tall against a band of one and a half, so the tube fell behind
        /// the field's plate and the one readout this mode is decided on was invisible. Nothing
        /// but a picture at the size a phone draws it could have said so (invariant 33h).
        /// </para>
        /// </summary>
        const float HillBand = .44f, LineBand = .16f, GemBand = .40f;

        /// <summary>Air between a gem and its socket, as a fraction of a cell.</summary>
        const float GemInset = .84f;

        sealed class Gem
        {
            public Image Img;
            public int Colour;
        }

        sealed class Mob
        {
            public int Id;
            public RectTransform Node;
            public Image Body;
            public Image Shadow;
            public Image Aura;
            public Image Pip;
            public RectTransform Bar;
            public Image Fill;
            public Color Coat;
            public float Height;
            public bool Falling;
        }

        sealed class Post
        {
            public RectTransform Node;
            public Image Socket;
            public Image Body;
            public Image Glow;
            public RectTransform Tube;
            public Image Juice;
            public RectTransform Bar;
            public Image Fill;
            public Color Coat;
            public Sprite[] Fire;
            public float Recoil;
            public bool Down;
            public float Lit;
        }

        SiegeLayout _layout;
        SiegeBoard _board;

        /// <summary>
        /// The siege this view is drawing, for the one readout the shared run cannot give.
        ///
        /// Every other mode on this shape counts a move allowance, so <c>ProtoScreen</c>'s
        /// readouts are enough for them; this one counts a ward line, which lives on the board.
        /// </summary>
        public SiegeBoard Siege => _board;

        RectTransform _hill, _lane, _mobs, _wall, _field, _fx;

        readonly List<Gem> _gems = new List<Gem>(48);
        readonly List<Mob> _mob = new List<Mob>(24);
        readonly List<Mob> _order = new List<Mob>(24);
        Post[] _posts;
        Text _waveLabel;

        float _hillTop, _hillFoot, _lineY, _gemCentre;
        Vector2 _room;
        int _held = -1;
        int _wave;

        // ------------------------------------------------------------------ art
        /// <summary>
        /// One of this mode's sprites, addressed through <see cref="AssetManifest"/>.
        ///
        /// <b>Every art lookup here goes through this and none of them builds a path</b>
        /// (invariant 7). The key is the first argument because <c>Tools/verify/artnames.py</c>
        /// reads the literals in a lookup's first argument, and a widget name in front of it would
        /// have the gate checking the wrong string and saying so confidently.
        /// </summary>
        static Sprite Piece(string key) => AssetLibrary.Sprite(AssetManifest.SiegeArt(key));

        static Sprite[] Reel(string key) => AssetLibrary.Frames(AssetManifest.SiegeArt(key));

        static Sprite[] Blast(string key) => AssetLibrary.Frames(AssetManifest.SiegeFx(key));

        /// <summary>
        /// A flipbook widget, or <b>null</b> when its frames are not there.
        ///
        /// An <c>Image</c> with a null sprite is a white rectangle rather than a blank (invariant
        /// 7b), so the widget is not built until the frames are in hand — missing art then costs
        /// the thing it draws and never costs a white square over the board.
        /// </summary>
        static Image Book(Sprite[] frames, string name, RectTransform parent, Vector2 size,
                          float fps, bool loop = true)
        {
            if (frames == null || frames.Length == 0) return null;

            var img = UIKit.Img(name, parent, frames[0], Color.white, size);
            img.raycastTarget = false;
            img.preserveAspect = true;
            Flipbook.Attach(img, frames, fps, loop);
            return img;
        }

        static readonly Color[] Tints =
        {
            Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Sun,
        };

        static Color TintOf(int colour)
            => colour >= 0 && colour < Tints.Length ? Tints[colour] : Pal.Cream;

        /// <summary>
        /// A gem's picture.
        ///
        /// <b>The name is written out at the point it is looked up rather than returned as a
        /// string</b>, which is invariant 6's rule for loc keys read across to art. A helper that
        /// answers <c>"gem_r"</c> and is then passed to <see cref="Piece"/> puts the literal one
        /// call away from the lookup, and <c>Tools/verify/artnames.py</c> counts that as a name
        /// nothing checks. Written this way every one of them is held to what is on disk.
        /// </summary>
        static Sprite GemArt(int colour)
        {
            switch (colour)
            {
                case 0: return Piece("gem_r");
                case 1: return Piece("gem_g");
                case 2: return Piece("gem_b");
                default: return Piece("gem_y");
            }
        }

        /// <summary>
        /// A ward's body. Four models rather than four paint jobs.
        ///
        /// <para>
        /// <b>No colour is baked into the art</b> — the sprite is a bare turret and the colour it
        /// burns is <see cref="Coat"/>, applied here to the same <c>Pal</c> entry the gems and the
        /// raiders take theirs from. So the four cannot drift apart, and a ward, its bullets, its
        /// muzzle flash and the gems that feed it are one colour by construction rather than by
        /// four people agreeing.
        /// </para>
        /// <para>
        /// Four <em>models</em> because a tint alone is a difference only some people can see
        /// (CRAFT.md's rule about the board's vocabulary), and a line of four identical turrets
        /// told apart only by hue is exactly that.
        /// </para>
        /// </summary>
        static Sprite WardArt(int colour)
        {
            switch (colour)
            {
                case 0: return Piece("ward1");
                case 1: return Piece("ward2");
                case 2: return Piece("ward3");
                default: return Piece("ward4");
            }
        }

        /// <summary>A ward's recoil, as frames. See <see cref="WardArt"/>.</summary>
        static Sprite[] FireArt(int colour)
        {
            switch (colour)
            {
                case 0: return Reel("fire1");
                case 1: return Reel("fire2");
                case 2: return Reel("fire3");
                default: return Reel("fire4");
            }
        }

        /// <summary>
        /// What a ward <em>fires</em>, as frames.
        ///
        /// <para>
        /// <b>Four elements rather than four tints of one round</b> — a fireball, a venom dart, an
        /// icicle and a lightning bolt, baked out of the bought projectile pack by
        /// <c>SiegeShotBake</c>. The argument is <see cref="WardArt"/>'s one step on: four turret
        /// models exist because a tint alone is a difference only some people can see, and the
        /// thing crossing the hill is on screen far more often than the turret that let it go. At
        /// the size a bolt is drawn, silhouette is the only difference that survives.
        /// </para>
        /// <para>
        /// <b>Keyed by colour and never by ward index</b>, because which colour a ward burns is
        /// content — a table by post would put a fireball on whichever ward happened to stand
        /// first and disagree with the turret beside it.
        /// </para>
        /// <para>
        /// <b>The hue is baked and nothing here tints it</b>, which is <see cref="WardArt"/>'s
        /// lesson taken at face value: pulled the whole way to a saturated <c>Pal</c> entry these
        /// come back dark, and lifted toward white they come back pastel. The bake reads
        /// <c>Pal</c> itself, so the colour cannot drift from the gems and the turrets however
        /// they are retuned — it simply has to be re-run.
        /// </para>
        /// </summary>
        static Sprite[] ShotArt(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("shot_r");
                case 1: return Blast("shot_g");
                case 2: return Blast("shot_b");
                default: return Blast("shot_y");
            }
        }

        /// <summary>The flash a ward throws as it lets one go. See <see cref="ShotArt"/>.</summary>
        static Sprite[] MuzzleArt(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("muzzle_r");
                case 1: return Blast("muzzle_g");
                case 2: return Blast("muzzle_b");
                default: return Blast("muzzle_y");
            }
        }

        /// <summary>What a bolt does when it arrives. See <see cref="ShotArt"/>.</summary>
        static Sprite[] HitArt(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("hit_r");
                case 1: return Blast("hit_g");
                case 2: return Blast("hit_b");
                default: return Blast("hit_y");
            }
        }

        /// <summary>
        /// How far a thing is pulled toward the colour it wears.
        ///
        /// <para>
        /// One number for the raiders, the wards, their bullets and their muzzle flashes, because
        /// all four are answering the same question — <em>which of the board's colours is this</em>
        /// — and four numbers would be four answers.
        /// </para>
        /// <para>
        /// <b>It is a multiply, so it can only ever darken</b> — which is fine for the cast,
        /// whose art is bright and whose colour only has to be legible, and was not fine for the
        /// wards. Pulled the whole way to a saturated <c>Pal</c> entry a turret came back dark
        /// ("too dim"); lifted toward white first it came back pastel. The wards therefore carry a
        /// real hue baked into the sprite and are drawn at full brightness (see
        /// <c>Tools/make_siege_art.py</c>), and nothing here tints them.
        /// </para>
        /// </summary>
        static Color Coat(int colour) => Color.Lerp(Color.white, TintOf(colour), .62f);

        /// <summary>Which of the cast a raider is drawn as, as frames. See <see cref="GemArt"/>.</summary>
        static Sprite[] Skin(SiegeRaider raider)
        {
            if (raider.Brute) return Reel("brute");

            switch (raider.Colour % 3)
            {
                case 0: return Reel("mon1");
                case 1: return Reel("mon2");
                default: return Reel("mon3");
            }
        }

        // ------------------------------------------------------------------ geometry
        protected override float Fit(Vector2 room)
        {
            _room = room;

            float wide = (room.x - Margin * 2f) / Width;
            float tall = (room.y - Margin * 2f) * GemBand / Height;
            return Mathf.Min(wide, tall);
        }

        protected override Vector2 Span
            => new Vector2(Mathf.Max(Cell * Width, _room.x - Margin * 2f),
                           Mathf.Max(Cell * Height, _room.y - Margin * 2f));

        protected override Vector2 CentreOf(int index)
        {
            int x = index % Width, y = index / Width;
            return new Vector2((x - (Width - 1) * .5f) * Cell,
                               _gemCentre + ((Height - 1) * .5f - y) * Cell);
        }

        /// <summary>Where a lane sits across the hill.</summary>
        float LaneX(int lane)
        {
            float wide = Span.x / SiegeTuning.Lanes;
            return (lane - (SiegeTuning.Lanes - 1) * .5f) * wide;
        }

        /// <summary>Where a ward stands on the line.</summary>
        float PostX(int index)
        {
            int n = _posts != null ? _posts.Length : 1;
            float wide = Span.x / (n + .6f);
            return (index - (n - 1) * .5f) * wide;
        }

        /// <summary>How far down the hill a raider has come.</summary>
        float MarchY(float march) => Mathf.Lerp(_hillTop, _hillFoot, Mathf.Clamp01(march));

        // ------------------------------------------------------------------ building
        protected override void Compose()
        {
            var rules = (SiegeRules)Rules;
            _layout = rules.Layout;
            _board = (SiegeBoard)Run.Board;

            _gems.Clear();
            _mob.Clear();

            // The pool's widgets hang off `_fx`, which this rebuild replaces — so a spare kept
            // across it is a destroyed node handed out as a live one, and every bolt after the
            // first rebuild would be invisible.
            _spare.Clear();

            _held = -1;
            _wave = 0;

            float h = Span.y;
            _hillTop = h * .5f - Cell * .35f;
            _hillFoot = h * (.5f - HillBand);
            // The wards stand *high* on the line, so their heads break into the grass rather
            // than tucking under the field's plate. A render is why: at the middle of the band
            // they were half-hidden behind the plate and read as small.
            _lineY = _hillFoot - h * LineBand * .30f;
            _gemCentre = (h * (.5f - HillBand - LineBand) - h * .5f) * .5f;

            _hill = Layer("Hill");
            _lane = Layer("Lanes");
            _mobs = Layer("Raiders");
            _wall = Layer("Line");
            _field = Layer("Field");
            _fx = Layer("Fx");

            Ground();
            Lanes();
            Line();
            Sockets();
            Deal();
            Targets();
            Banner();

            StartCoroutine(Countdown());
        }

        RectTransform Layer(string name)
        {
            var rt = UIKit.Node(name, Field);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = Field.sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>The hill: ground the raiders walk over, and the breach they come out of.</summary>
        void Ground()
        {
            float h = _hillTop - _hillFoot + Cell * .35f;

            var ground = UIKit.Img("Ground", _hill, Piece("hill"), Color.white,
                                   new Vector2(Span.x, h + Cell * .5f));
            ground.raycastTarget = false;
            ground.rectTransform.anchoredPosition = new Vector2(0f, (_hillTop + _hillFoot) * .5f);

            // **Nothing over the top of it, and nothing standing at the head of it.** The first
            // cut put a black gradient across the far end (so the hill "got darker the further up
            // it went") and a broken gateway for the waves to come out of. Both were withdrawn by
            // the owner after playing it: the wash read as a hole rather than as distance, and the
            // gateway read as a hut somebody had left on the board. What says a wave has arrived
            // is the wave - it walks on, in front of a field that is plainly a field.
        }

        /// <summary>Faint tracks, so a raider reads as walking down something.</summary>
        void Lanes()
        {
            float h = _hillTop - _hillFoot;

            for (int i = 0; i < SiegeTuning.Lanes; i++)
            {
                var strip = UIKit.Img("Lane", _lane, Art.Round(6), new Color(1f, 1f, 1f, .045f),
                                      new Vector2(Span.x / SiegeTuning.Lanes * .82f, h));
                strip.raycastTarget = false;
                strip.type = Image.Type.Sliced;
                strip.rectTransform.anchoredPosition =
                    new Vector2(LaneX(i), (_hillTop + _hillFoot) * .5f);
            }
        }

        /// <summary>The rampart and the wards standing on it.</summary>
        void Line()
        {
            float band = Span.y * LineBand;

            var wall = UIKit.Img("Rampart", _wall, Piece("rampart"), Color.white,
                                 new Vector2(Span.x, band * 1.02f));
            wall.raycastTarget = false;
            wall.type = Image.Type.Sliced;
            wall.rectTransform.anchoredPosition = new Vector2(0f, _hillFoot - band * .5f);

            _posts = new Post[_board.Wards.Count];

            for (int i = 0; i < _posts.Length; i++)
            {
                var ward = _board.Wards[i];
                var post = new Post();

                post.Node = UIKit.Node("Ward", _wall);
                post.Node.anchorMin = post.Node.anchorMax = new Vector2(.5f, .5f);
                post.Node.sizeDelta = new Vector2(Cell * 1.8f, Cell * 2.3f);
                post.Node.anchoredPosition = new Vector2(PostX(i), _lineY);

                post.Socket = UIKit.Img("Base", post.Node, Piece("socket"), Color.white,
                                        new Vector2(Cell * 1.7f, Cell * .8f));
                post.Socket.raycastTarget = false;
                post.Socket.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .88f);

                post.Glow = UIKit.Img("Glow", post.Node, Art.Glow(128, 2.1f),
                                      Pal.A(TintOf(ward.Colour), 0f),
                                      new Vector2(Cell * 3.1f, Cell * 3.1f));
                post.Glow.raycastTarget = false;

                // White: a ward's colour is in its sprite, not on top of it.
                post.Coat = Color.white;

                post.Body = UIKit.Img("Post", post.Node, WardArt(ward.Colour), post.Coat,
                                      new Vector2(Cell * 1.72f, Cell * 2.15f));
                post.Body.raycastTarget = false;
                post.Body.preserveAspect = true;
                post.Body.rectTransform.anchoredPosition = new Vector2(0f, Cell * .06f);

                post.Fire = FireArt(ward.Colour);

                // The fuel tube: the one readout in this mode that is on the board rather than in
                // the header, because it is the thing a player is deciding about on every match.
                post.Tube = UIKit.Node("Tube", post.Node);
                post.Tube.anchorMin = post.Tube.anchorMax = new Vector2(.5f, .5f);
                post.Tube.sizeDelta = new Vector2(Cell * 1.06f, Cell * .23f);

                // **Over the pillar rather than under the plinth**, which is where a render put
                // it: below the plinth it fell behind the field's own plate and the one number
                // every decision in this mode rests on could not be seen at all.
                post.Tube.anchoredPosition = new Vector2(0f, -Cell * .62f);

                var trough = UIKit.Img("Trough", post.Tube, Art.Round(12),
                                       new Color(0f, 0f, 0f, .62f), post.Tube.sizeDelta);
                trough.raycastTarget = false;
                trough.type = Image.Type.Sliced;

                post.Juice = UIKit.Img("Juice", post.Tube, Art.Round(12), TintOf(ward.Colour),
                                       new Vector2(0f, post.Tube.sizeDelta.y - 4f),
                                       new Vector2(0f, .5f), new Vector2(4f, 0f));
                post.Juice.raycastTarget = false;
                post.Juice.type = Image.Type.Sliced;
                post.Juice.rectTransform.pivot = new Vector2(0f, .5f);
                post.Juice.rectTransform.anchorMin = new Vector2(0f, .5f);
                post.Juice.rectTransform.anchorMax = new Vector2(0f, .5f);
                post.Juice.rectTransform.anchoredPosition = new Vector2(2f, 0f);

                // **A bar rather than a row of pips.** A ward takes ten blows now (see
                // `SiegeTuning.WardHealth`), and ten dots over a turret is something a player
                // reads as texture rather than as a number.
                post.Bar = UIKit.Node("Health", post.Node);
                post.Bar.anchorMin = post.Bar.anchorMax = new Vector2(.5f, .5f);
                post.Bar.sizeDelta = new Vector2(Cell * 1.06f, Cell * .17f);
                post.Bar.anchoredPosition = new Vector2(0f, Cell * 1.26f);

                var kerb = UIKit.Img("Trough", post.Bar, Art.Round(10),
                                     new Color(0f, 0f, 0f, .66f), post.Bar.sizeDelta);
                kerb.raycastTarget = false;

                post.Fill = UIKit.Img("Fill", post.Bar, Art.Round(10), Pal.Cream,
                                      new Vector2(post.Bar.sizeDelta.x - 4f,
                                                  post.Bar.sizeDelta.y - 4f));
                post.Fill.raycastTarget = false;
                post.Fill.rectTransform.pivot = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

                _posts[i] = post;
            }
        }

        /// <summary>The dark sockets a gem stands in, drawn once and never taken away.</summary>
        void Sockets()
        {
            var plate = UIKit.Img("Plate", _field, Piece("plate"), Color.white,
                                  new Vector2(Cell * Width + Cell * .34f,
                                              Cell * Height + Cell * .34f));
            plate.raycastTarget = false;
            plate.type = Image.Type.Sliced;
            plate.rectTransform.anchoredPosition = new Vector2(0f, _gemCentre);

            for (int i = 0; i < Width * Height; i++)
            {
                var slot = UIKit.Img("Slot", _field, Art.Round(16), Pal.Slot,
                                     new Vector2(Cell * .92f, Cell * .92f));
                slot.raycastTarget = false;
                slot.type = Image.Type.Sliced;
                slot.rectTransform.anchoredPosition = CentreOf(i);
            }
        }

        /// <summary>The gems. Pictures only - the finger is taken by <see cref="Targets"/>.</summary>
        void Deal()
        {
            for (int i = 0; i < Width * Height; i++)
            {
                var gem = Mint(_board.ColourAt(i));
                gem.Img.rectTransform.anchoredPosition = CentreOf(i);
                _gems.Add(gem);
            }
        }

        /// <summary>
        /// One hit target per cell, standing still.
        ///
        /// <b>Per cell rather than on the gems</b>, which is <c>EmberView</c>'s idiom and right
        /// here for a second reason on top of its own: a gem on this board is falling most of the
        /// time, so a handler carried by the picture would have to be asked where its picture had
        /// got to. Hit-testing the cell and then asking the board what is standing there keeps the
        /// drawing and the rule from ever disagreeing about what was touched.
        /// </summary>
        void Targets()
        {
            for (int cell = 0; cell < Width * Height; cell++)
            {
                int at = cell;

                var img = UIKit.Img("hit", _field, Art.Pixel, new Color(0f, 0f, 0f, 0f),
                                    new Vector2(Cell, Cell));
                img.raycastTarget = true;
                img.rectTransform.anchoredPosition = CentreOf(at);

                var btn = img.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Poke(at), silent: true);

                var drag = img.gameObject.AddComponent<CellDrag>();
                drag.Threshold = Cell * .30f;
                drag.Dragged = dir => Drag(at, dir);
            }
        }

        /// <summary>
        /// Three, two, one, and they come.
        ///
        /// <para>
        /// <b>Once, at the start, and never per wave.</b> What it is for is the half-second a
        /// player needs to look at the hill before anything is on it - a siege that opens with
        /// something already walking has been going on before they arrived. It is not a *pause*:
        /// the clock runs underneath it, and the first wave is timed to step out as the last
        /// number leaves (<see cref="SiegeTuning.FirstWaveAfter"/>), so nothing is being held up
        /// and the count is telling the truth about when they arrive.
        /// </para>
        /// </summary>
        IEnumerator Countdown()
        {
            float step = SiegeTuning.FirstWaveAfter / 4f;

            for (int i = 3; i >= 0; i--)
            {
                if (_fx == null) yield break;

                string say = i > 0 ? i.ToString() : Loc.Get("mode.siege.go");

                var label = UIKit.Label("Count", _fx, say,
                                        Mathf.RoundToInt(Cell * (i > 0 ? 1.5f : 1.1f)),
                                        i > 0 ? Pal.Cream : Pal.Gold, TextAnchor.MiddleCenter,
                                        new Vector2(Span.x, Cell * 2f));
                label.rectTransform.anchoredPosition =
                    new Vector2(0f, (_hillTop + _hillFoot) * .5f);

                var mark = label;
                mark.transform.localScale = Vector3.one * 2.1f;

                Tween.Scale(mark.transform, 1f, step * .55f, Ease.OutBack);
                Tween.Fade(mark, 0f, step * .95f, Ease.InQuad)
                     .OnDone(() => { if (mark) Destroy(mark.gameObject); });

                Audio.Sfx(i > 0 ? "tick" : "bell", i > 0 ? .5f : .8f, i > 0 ? 1f : 1.2f);

                if (i == 0) Flow.Flash(new Color(1f, .86f, .5f), .3f, .35f);

                yield return new WaitForSecondsRealtime(step);
            }
        }

        void Banner()
        {
            _waveLabel = UIKit.Label("Wave", _fx, string.Empty, Mathf.RoundToInt(Cell * .46f),
                                     Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(Span.x, Cell * .9f));
            _waveLabel.rectTransform.anchoredPosition = new Vector2(0f, _hillFoot + Cell * 1.5f);

            var group = UIKit.Group(_waveLabel.rectTransform);
            group.alpha = 0f;
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>The gem a lesson about the verb rings: one whose ward is on the line.</summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null) return 0;

                for (int i = 0; i < Width * Height; i++)
                    if (_layout.WardOf(_board.At(i)) == 0) return i;

                return 0;
            }
        }

        /// <summary>
        /// The lesson about the line points at the field's own top row, which is as close as a
        /// cell anchor can get to the wards standing above it — <c>ProtoView</c>'s anchors are
        /// cells, and a tip pointing at nothing is worse than no tip.
        /// </summary>
        public override int FriendCell => Width / 2;

        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// Whether this run is under way at all, ignoring whether an animation is playing.
        ///
        /// <para>
        /// <b>It exists because <c>ProtoView.TakingInput</c> is the wrong question for this
        /// mode, and asking the wrong one stopped the clock.</b> Every other board on this shape
        /// is turn-based, so "is the board taking input" and "is the run advancing" are the same
        /// question and <c>ProtoScreen.Runnable</c> answers both with one. Here they are not:
        /// <c>Busy</c> is the latch that stops a second swap landing while the first is still
        /// falling, and a cascade is half a second long - so a run whose clock was gated on
        /// <c>TakingInput</c> would freeze the hill on every match, and a player could hold time
        /// still by swapping. <c>SiegeScreen.Runnable</c> reads this instead, which also means
        /// <c>Played</c> keeps counting through a cascade, which it should: a siege is running the
        /// whole time whether or not a finger would do anything.
        /// </para>
        /// </summary>
        public bool Advancing => Run != null && !Locked && !Over;

        /// <summary>As <see cref="Advancing"/>, and the screen has let the run begin.</summary>
        bool Live => Advancing && !Held;

        void Update()
        {
            if (!Live) return;

            var report = _board.Advance(Time.unscaledDeltaTime);

            Follow();
            Depth();
            Charge();

            if (report.Wave >= 0) Arrival(report.Wave);
            for (int i = 0; i < report.Bolts.Count; i++) Bolt(report.Bolts[i]);
            for (int i = 0; i < report.Blows.Count; i++) Blow(report.Blows[i]);

            Reap();

            if (report.Bolts.Count > 0 || report.Blows.Count > 0 || report.Wave >= 0)
                Changed?.Invoke();

            Judge();
        }

        /// <summary>Puts every raider widget where the model says it is.</summary>
        void Follow()
        {
            var raiders = _board.Raiders;

            for (int i = 0; i < raiders.Count; i++)
            {
                var raider = raiders[i];
                if (!raider.OnTheHill) continue;

                var mob = Widget(raider);
                if (mob == null) continue;

                mob.Node.anchoredPosition =
                    new Vector2(LaneX(raider.Lane), MarchY(raider.March));

                if (mob.Fill != null)
                {
                    float share = Mathf.Clamp01(raider.Health / (float)raider.MaxHealth);
                    mob.Fill.rectTransform.sizeDelta =
                        new Vector2(mob.Bar.sizeDelta.x * share - 4f, mob.Bar.sizeDelta.y - 4f);
                }

                if (mob.Body != null)
                    mob.Body.color = raider.Flash > 0f
                                   ? Color.Lerp(mob.Coat, Pal.Cream, raider.Flash * 5f)
                                   : mob.Coat;
            }
        }

        /// <summary>
        /// Puts the raiders in front of and behind each other by how far down the hill they are.
        ///
        /// <para>
        /// <b>Sorted, then indexed one at a time.</b> The first cut handed each widget
        /// <c>SetSiblingIndex(march * 1000)</c>, which reads as depth and is not: Unity clamps a
        /// sibling index to the number of children, so with eight raiders on the hill every one of
        /// those numbers clamped to the last slot and the order became whichever widget was
        /// written most recently. Reported from play as a raider walking down *behind* another and
        /// drawing on top of it. There is no way to say "put this one at depth 0.42" — the only
        /// thing a UI hierarchy understands is a run of positions, so the list has to be ordered
        /// and then laid out.
        /// </para>
        /// </summary>
        void Depth()
        {
            _order.Clear();

            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Node != null) _order.Add(_mob[i]);

            // Furthest up the hill first, so it ends up at the back. An insertion sort, because
            // the list is nearly ordered every frame and never longer than a wave.
            for (int i = 1; i < _order.Count; i++)
            {
                var held = _order[i];
                float depth = DepthOf(held);

                int j = i - 1;
                while (j >= 0 && DepthOf(_order[j]) > depth)
                {
                    _order[j + 1] = _order[j];
                    j--;
                }

                _order[j + 1] = held;
            }

            for (int i = 0; i < _order.Count; i++) _order[i].Node.SetSiblingIndex(i);
        }

        float DepthOf(Mob mob)
        {
            var raider = _board.Find(mob.Id);
            return raider == null ? 2f : raider.March;
        }

        /// <summary>Paints every ward's fuel, its light and its health.</summary>
        void Charge()
        {
            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                var ward = _board.Wards[i];

                float wide = post.Tube.sizeDelta.x - 4f;
                post.Juice.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(0f, wide * ward.Charge), post.Tube.sizeDelta.y - 4f);

                // The light is what says a ward is working, and it is the thing a player watches
                // out of the corner of an eye while looking at the field.
                float want = ward.Alive ? Mathf.Clamp01(ward.Charge) : 0f;
                post.Lit = Mathf.Lerp(post.Lit, want, Time.unscaledDeltaTime * 6f);

                float beat = ward.Fuelled
                           ? 1f + Mathf.Sin(Time.unscaledTime * 14f) * .06f : 1f;

                post.Glow.color = Pal.A(TintOf(ward.Colour), post.Lit * .62f);
                post.Glow.rectTransform.localScale = Vector3.one * (.7f + post.Lit * .55f) * beat;

                // The recoil: the turret's own shoot frames, walked while a shot is in flight.
                if (post.Recoil > 0f)
                {
                    post.Recoil = Mathf.Max(0f, post.Recoil - Time.unscaledDeltaTime);

                    if (post.Body != null && post.Fire != null && post.Fire.Length > 0)
                    {
                        int frame = Mathf.Clamp(
                            (int)((1f - post.Recoil / RecoilFor) * post.Fire.Length),
                            0, post.Fire.Length - 1);
                        post.Body.sprite = post.Fire[frame];
                    }
                }
                else if (post.Body != null && !post.Down)
                {
                    post.Body.sprite = WardArt(ward.Colour);
                }

                // **A ward is never drawn darker than its own colour.** It used to fade toward
                // 72% of its coat when it had no fuel, which meant it dimmed on the first frame of
                // the run and stayed dim - reported from play as "they are bright when the match
                // starts and immediately dim down". Fuel now reads as a ward getting *brighter*,
                // which is the direction a light should move in.
                if (post.Body != null && !post.Down)
                    post.Body.color = Color.Lerp(post.Coat, Color.white, post.Lit * .30f);

                // Cream, then gold, then ember: the line says how close it is to going in the
                // one place a player is already looking.
                float held = Mathf.Clamp01(ward.Health / (float)SiegeTuning.WardHealth);

                post.Fill.rectTransform.sizeDelta =
                    new Vector2((post.Bar.sizeDelta.x - 4f) * held, post.Bar.sizeDelta.y - 4f);

                post.Fill.color = held <= .34f ? Pal.Ember : held <= .67f ? Pal.Gold : Pal.Cream;

                if (ward.Alive || post.Down) continue;

                post.Down = true;
                Fell(post);
            }
        }

        // ------------------------------------------------------------------ raiders
        Mob Widget(SiegeRaider raider)
        {
            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == raider.Id) return _mob[i];

            return Hatch(raider);
        }

        Mob Hatch(SiegeRaider raider)
        {
            var mob = new Mob { Id = raider.Id };

            mob.Node = UIKit.Node("Raider", _mobs);
            mob.Node.anchorMin = mob.Node.anchorMax = new Vector2(.5f, .5f);
            mob.Node.sizeDelta = new Vector2(Cell, Cell);
            mob.Node.anchoredPosition = new Vector2(LaneX(raider.Lane), MarchY(raider.March));

            float tall = Cell * (raider.Brute ? 1.55f : 1.15f);
            mob.Height = tall;

            mob.Shadow = UIKit.Img("Shadow", mob.Node, Art.Glow(64, 3f),
                                   new Color(0f, 0f, 0f, .42f),
                                   new Vector2(tall * .62f, tall * .22f));
            mob.Shadow.raycastTarget = false;
            mob.Shadow.rectTransform.anchoredPosition = new Vector2(0f, -tall * .46f);

            // The colour a raider wears is what decides which ward answers it, so it is said
            // three times: a wash behind the body, the body's own coat, and a gem over its head.
            // <b>Three rather than one, and none of them is enough on its own.</b> The packs draw
            // four monsters in four colours of their own that have nothing to do with this
            // board's four, so an untinted raider would wear a colour the player has to learn; a
            // tint alone is a difference only some people can see (CRAFT.md's rule about the
            // board's vocabulary); and an aura alone is lost the moment two raiders overlap.
            mob.Aura = UIKit.Img("Aura", mob.Node, Art.Glow(96, 2.2f),
                                 Pal.A(TintOf(raider.Colour), .40f),
                                 new Vector2(tall * 1.25f, tall * 1.25f));
            mob.Aura.raycastTarget = false;

            mob.Coat = Color.Lerp(Color.white, TintOf(raider.Colour), .62f);

            mob.Body = Book(Skin(raider), "Body", mob.Node, new Vector2(tall, tall),
                            raider.Brute ? 10f : 13f);

            if (mob.Body != null)
            {
                mob.Body.color = mob.Coat;
                // The packs draw them facing right; this hill runs top to bottom, so they are
                // turned to face down the way a walk cycle reads best - across, and coming on.
                mob.Body.rectTransform.anchoredPosition = new Vector2(0f, tall * .04f);
                Tween.Bob(mob.Body.rectTransform, tall * .035f, raider.Brute ? 1.1f : .72f,
                          raider.Id * .37f);
            }

            mob.Bar = UIKit.Node("Bar", mob.Node);
            mob.Bar.anchorMin = mob.Bar.anchorMax = new Vector2(.5f, .5f);
            mob.Bar.sizeDelta = new Vector2(tall * .72f, Cell * .13f);
            mob.Bar.anchoredPosition = new Vector2(0f, tall * .58f);

            var trough = UIKit.Img("Trough", mob.Bar, Art.Round(10), new Color(0f, 0f, 0f, .66f),
                                   mob.Bar.sizeDelta);
            trough.raycastTarget = false;
            trough.type = Image.Type.Sliced;

            mob.Fill = UIKit.Img("Fill", mob.Bar, Art.Round(10),
                                 raider.Brute ? Pal.Foxglove : Pal.Rose,
                                 new Vector2(mob.Bar.sizeDelta.x - 4f, mob.Bar.sizeDelta.y - 4f));
            mob.Fill.raycastTarget = false;
            mob.Fill.type = Image.Type.Sliced;
            mob.Fill.rectTransform.pivot = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

            mob.Pip = UIKit.Img("Pip", mob.Node, GemArt(raider.Colour), Color.white,
                                new Vector2(Cell * .34f, Cell * .34f));
            mob.Pip.raycastTarget = false;
            mob.Pip.preserveAspect = true;
            mob.Pip.rectTransform.anchoredPosition = new Vector2(-tall * .46f, tall * .58f);

            mob.Node.localScale = Vector3.one * .5f;
            Tween.Scale(mob.Node, 1f, .3f, Ease.OutBack);

            var group = UIKit.Group(mob.Node);
            group.alpha = 0f;
            Tween.Fade(group, 1f, .26f);

            _mob.Add(mob);
            return mob;
        }

        /// <summary>Takes down the widgets of raiders the model has already forgotten.</summary>
        void Reap()
        {
            for (int i = _mob.Count - 1; i >= 0; i--)
            {
                var mob = _mob[i];
                if (mob.Falling) continue;
                if (_board.Find(mob.Id) != null) continue;

                mob.Falling = true;
                Die(mob);
                _mob.RemoveAt(i);
            }
        }

        void Die(Mob mob)
        {
            var at = mob.Node.anchoredPosition;

            Boom(at, Blast("boom_fire"), mob.Height * 2f);
            Burst.Sparks(_fx, at, Pal.Ember, 14, mob.Height * 1.6f, mob.Height * .2f);
            Audio.SfxVaried("burst", .38f);

            var node = mob.Node;
            var group = UIKit.Group(node);

            Tween.Run(.32f, Ease.OutQuad, t =>
            {
                if (!node) return;
                node.localScale = new Vector3(1f + t * .35f, 1f - t * .55f, 1f);
                if (group) group.alpha = 1f - t;
            }, node).OnDone(() => { if (node) Destroy(node.gameObject); });
        }

        // ------------------------------------------------------------------ the widget a shot is
        /// <summary>
        /// One drawn beat of a shot: a node that carries the aim, a reel, and a halo under it.
        ///
        /// <para>
        /// <b>Pooled, and this is the one board in the game where that is not premature.</b> A lit
        /// ward fires every <c>SiegeTuning.FireEvery</c> — seven a second — and a full line is
        /// four of those, each of which is a muzzle flash, a comet and an impact. Building and
        /// destroying twenty-odd of these a second is churn the rest of this project never asks
        /// for, and the pool is what makes the three-part shot affordable rather than something to
        /// trim back to one part.
        /// </para>
        /// <para>
        /// <b>The node carries the rotation and the reel hangs off it</b>, rather than the reel
        /// being turned about a shifted pivot. A comet's head is at <see cref="HeadAt"/> of its
        /// frame, not the middle, so the image is offset until its head sits on the node's origin
        /// — and then aiming the node aims the shot, the halo stays on the head, and the growth
        /// tween on the way in is one scale on one transform.
        /// </para>
        /// </summary>
        sealed class Puff
        {
            public RectTransform Node;
            public Image Reel, Halo;
            public Flipbook Film;
            public bool Out;
        }

        readonly Stack<Puff> _spare = new Stack<Puff>(24);

        /// <summary>
        /// A widget playing <paramref name="frames"/>, aimed, sized to the reel's own shape.
        ///
        /// <b>The shape comes off the sprite and is never a constant here.</b> A bolt's frame is
        /// three times as tall as it is wide and a burst's is square, and both of those are
        /// decisions <c>SiegeShotBake</c> makes; reading them back off the art is what stops this
        /// class holding a second opinion that can go stale when a reel is re-baked.
        /// </summary>
        Puff Lend(Sprite[] frames, Color tint, float wide, Vector2 at, float angle, float fps,
                  bool loop, float head)
        {
            var puff = _spare.Count > 0 ? _spare.Pop() : Build();
            puff.Out = true;

            var first = frames[0];
            float aspect = first.rect.width > 0f ? first.rect.height / first.rect.width : 1f;
            float tall = wide * aspect;

            puff.Node.gameObject.SetActive(true);
            puff.Node.anchoredPosition = at;
            puff.Node.localRotation = Quaternion.Euler(0f, 0f, angle);
            puff.Node.localScale = Vector3.one;

            puff.Reel.rectTransform.sizeDelta = new Vector2(wide, tall);
            puff.Reel.rectTransform.anchoredPosition = new Vector2(0f, -(head - .5f) * tall);
            puff.Reel.color = tint;

            puff.Film = Flipbook.Attach(puff.Reel, frames, fps, loop);
            return puff;
        }

        Puff Build()
        {
            var node = UIKit.Node("Shot", _fx);

            // Under the reel, because it is a light the effect sits in rather than a ring round
            // it: a bolt's own colour on a bright green hill is the one thing this board cannot
            // afford to lose, and the baked art is bright but small.
            var halo = UIKit.Img("Halo", node, Art.Glow(96, 2.0f), Color.clear,
                                 new Vector2(1f, 1f));
            halo.raycastTarget = false;

            // Built with the sprite it will be given rather than with none: an `Image` with a null
            // sprite is a white rectangle, not a blank (invariant 7b), and a pooled widget spends
            // its whole life one frame away from being shown.
            var reel = UIKit.Img("Reel", node, Art.Pixel, Color.clear, new Vector2(1f, 1f));
            reel.raycastTarget = false;
            reel.preserveAspect = true;

            return new Puff { Node = node, Reel = reel, Halo = halo };
        }

        /// <summary>Lights the halo under a widget already lent. Only the bolt in flight wants one.</summary>
        static void Glow(Puff puff, Color tint, float size)
        {
            puff.Halo.rectTransform.sizeDelta = new Vector2(size, size);
            puff.Halo.rectTransform.anchoredPosition = Vector2.zero;
            puff.Halo.color = Pal.A(Pal.Lift(tint, .5f), .8f);
        }

        /// <summary>
        /// Hands a widget back when its reel finishes, and on a timer if the reel cannot say so.
        ///
        /// <b>The timer is not belt and braces, it is the only guarantee</b>: a <c>Flipbook</c>
        /// whose image is destroyed or whose frames are empty never raises <c>OnFinished</c>, and
        /// a widget that is never given back is a leak on a board that asks for twenty a second.
        /// <see cref="Give"/> is idempotent so the two cannot double up.
        /// </summary>
        void Ends(Puff puff, float after)
        {
            if (puff.Film != null) puff.Film.OnFinished = () => Give(puff);

            Tween.After(after + .08f, () => Give(puff), puff.Node);
        }

        /// <summary>
        /// Puts a widget back, once.
        ///
        /// Every tween on the node is killed first: a widget handed back while its flight is still
        /// running would be moved across the board by a shot that has already landed, which is the
        /// pooling bug this project has met before under another name (a flipbook left painting
        /// into an image that had been re-sized for something else).
        /// </summary>
        void Give(Puff puff)
        {
            if (puff == null || !puff.Out) return;
            puff.Out = false;

            if (!puff.Node) return;

            Tween.KillAll(puff.Node);
            Flipbook.Detach(puff.Reel);
            puff.Film = null;

            puff.Reel.color = Color.clear;
            puff.Halo.color = Color.clear;
            puff.Node.gameObject.SetActive(false);

            _spare.Push(puff);
        }

        /// <summary>The bolt itself: the ward's own comet, aimed, with a light under its head.</summary>
        Puff Round(int colour, Color tint, Vector2 at, float angle)
        {
            var frames = ShotArt(colour);

            // The shared round, drained of colour so it can take the ward's, is what is drawn when
            // the projectile pack is not in this checkout. It is one sprite rather than a reel, so
            // it is wrapped as one - a missing bake costs the animation and never the bolt.
            if (frames == null || frames.Length == 0)
            {
                var round = Piece("bullet");
                frames = round != null ? new[] { round } : null;
            }

            if (frames == null || frames.Length == 0)
            {
                return Lend(new[] { Art.Glow(96, 2.0f) }, Pal.A(Pal.Lift(tint, .5f), 1f),
                            Cell * .5f, at, angle, 1f, true, .5f);
            }

            // Sized by the frame's *width*, which is the comet's own width because the bake frames
            // it that tightly — so this number means "a bolt is two thirds of a gem across" and
            // stays true when a reel is re-baked into a different shape.
            var puff = Lend(frames, Color.white, Cell * .62f, at, angle, 30f, true, HeadAt);
            Glow(puff, tint, Cell * 1.15f);
            return puff;
        }

        // ------------------------------------------------------------------ the exchange
        /// <summary>How long a ward's recoil frames take to play out.</summary>
        const float RecoilFor = .18f;

        /// <summary>
        /// Where a bolt's head sits in its own frame, measured from the bottom.
        ///
        /// <para>
        /// <b>Declared here and read by the bake</b> (<c>SiegeShotBake</c> references this
        /// constant), so the number that frames the render and the number that positions the
        /// sprite are one number. Two would be two, and the failure is a comet whose head is not
        /// where the shot is — visible only as a bolt that seems to land slightly early, which is
        /// exactly the kind of wrongness nobody can name.
        /// </para>
        /// <para>
        /// Above a half because a comet is nearly all tail: the head leads and the trail has the
        /// rest of the frame to lie in.
        /// </para>
        /// </summary>
        public const float HeadAt = .82f;

        /// <summary>
        /// Where the barrel sits in a muzzle flash's own frame, measured from the bottom.
        ///
        /// <b>Low, because a flash is all in front of the gun.</b> Anchored in the middle like an
        /// impact, half of every flash was drawn behind the turret that threw it — and half of
        /// every reel was empty air, which came off the size of the thing on the board. Read by
        /// <c>SiegeShotBake</c>, which frames the render around it.
        /// </summary>
        public const float MuzzleAt = .22f;

        /// <summary>
        /// How long a bolt is in the air.
        ///
        /// <para>
        /// <b>Longer than it was, and the art is the reason.</b> A round crossing the hill in six
        /// hundredths of a second is a dot teleporting whatever is drawn on it — which was fine
        /// while it was a dot, and throws away a fourteen-frame comet. The ceiling is what keeps
        /// it honest: a ward fires every <c>SiegeTuning.FireEvery</c>, so a flight much past that
        /// puts two of a ward's own bolts in the air at once and the line reads as a hose rather
        /// than as a gun.
        /// </para>
        /// </summary>
        const float ShortestFlight = .09f, LongestFlight = .20f;

        void Bolt(SiegeBolt shot)
        {
            var post = _posts[shot.Ward];
            var ward = _board.Wards[shot.Ward];
            var tint = TintOf(ward.Colour);

            Vector2 muzzle = new Vector2(PostX(shot.Ward), _lineY + Cell * 1.0f);

            Mob mob = null;
            for (int i = 0; i < _mob.Count; i++) if (_mob[i].Id == shot.Raider) mob = _mob[i];
            if (mob == null) return;

            Vector2 to = mob.Node.anchoredPosition;

            // The turret's own recoil frames, walked by `Charge`. Frames rather than a tween,
            // because the pack drew the barrels moving and a scale-punch over a still turret is
            // the cheaper lie - which is what the first cut was, and what came back as "I didn't
            // like the firing animation".
            post.Recoil = RecoilFor;

            var dir = to - muzzle;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            // **The whole shot, and it is deliberately more than a dot crossing a gap.** What
            // came back from play was that the firing was boring, and the fix is not one bigger
            // thing - it is that a shot has *four* beats a player can see: the barrel kicks, the
            // muzzle throws light, something with a tail crosses the hill, and it arrives.
            Flash(muzzle, angle, ward.Colour, tint);

            // The kick, on top of the pack's own frames. A turret that only cycles frames stays
            // put; one that is shoved backwards and springs forward has weight.
            var body = post.Body;
            if (body != null)
            {
                Tween.KillChannel(body, "kick");
                Tween.Run(RecoilFor, Ease.OutQuad, t =>
                {
                    if (!body) return;
                    float k = t < .3f ? t / .3f : 1f - (t - .3f) / .7f;
                    body.rectTransform.anchoredPosition =
                        new Vector2(0f, Cell * .06f - Cell * .13f * k);
                    body.rectTransform.localScale =
                        new Vector3(1f + k * .07f, 1f - k * .06f, 1f);
                }, body, "kick");
            }

            var round = Round(ward.Colour, tint, muzzle, angle);
            float flight = Mathf.Clamp(dir.magnitude / (Cell * 26f), ShortestFlight, LongestFlight);

            var node = round.Node;
            Tween.Run(flight, Ease.Linear, t =>
            {
                if (!node) return;
                node.anchoredPosition = Vector2.Lerp(muzzle, to, t);

                // It grows a little on the way in, which is a cheap read of "coming toward you"
                // on a board with no depth - and it is the only thing about the bolt this class
                // animates, because the fourteen frames under it are doing the rest.
                node.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, t);
            }, node).OnDone(() =>
            {
                Give(round);
                Land(to, tint, ward.Colour, angle, shot);
            });

            // **A ward is audible.** Each of the four is pitched differently, so a player can
            // hear which one they just fed without looking away from the field - and a hit that
            // lands double is a brighter note on top, which is the rule the mode is about said in
            // the one channel that was silent.
            Audio.Sfx("poke", .32f, 1.18f - shot.Ward * .11f);
            if (shot.Weak) Audio.SfxVaried("chime2", .2f);
        }

        /// <summary>
        /// The muzzle flash: the projectile's own, from the pack that drew the projectile.
        ///
        /// <para>
        /// <b>The pack fires as three parts and all three are played</b>, which is what its own
        /// demo does — a flash, a comet, an impact. Showing the middle one alone is judging a
        /// sentence by its verb, and it is also the difference between a turret that emits
        /// something and a turret that <em>fires</em>.
        /// </para>
        /// <para>
        /// It falls back to the shared white <c>flash</c> reel, and then to a plain pop, because a
        /// missing reel must cost the thing it draws and never a white rectangle (invariant 7b).
        /// </para>
        /// </summary>
        void Flash(Vector2 at, float angle, int colour, Color tint)
        {
            var frames = MuzzleArt(colour);
            bool own = frames != null && frames.Length > 0;

            if (!own) frames = Blast("flash");

            if (frames == null || frames.Length == 0)
            {
                Pop(at, tint, .9f, .16f);
                return;
            }

            // The pack's own flash is drawn pointing along the shot; the shared one is a radial
            // burst with no direction in it, so it is spun instead of aimed.
            var puff = Lend(frames, own ? Color.white : Pal.A(Pal.Lift(tint, .55f), 1f),
                            Cell * (own ? 1.9f : 1.7f), at,
                            own ? angle : Random.Range(0f, 360f), 33f, false,
                            own ? MuzzleAt : .5f);

            // A ring of light off the muzzle as well as the frames, because this is the moment the
            // player's own move pays out.
            Shockwave(at, Pal.Lift(tint, .5f), 2.2f, .22f);

            Ends(puff, own ? .32f : .3f);
        }

        void Land(Vector2 at, Color tint, int colour, float angle, SiegeBolt shot)
        {
            var frames = HitArt(colour);

            if (frames != null && frames.Length > 0)
                Ends(Lend(frames, Color.white, Cell * (shot.Killed ? 3.0f : 2.35f), at, angle,
                          35f, false, .5f), .35f);

            Pop(at, shot.Weak ? Pal.Gold : tint, shot.Weak ? 1.9f : 1.2f, .24f);

            // A double is drawn as a *different kind* of hit rather than a bigger one: gold, a
            // ring, and sparks. It is the mode's one rule and the board is where it is said.
            if (shot.Weak)
            {
                Burst.Sparks(_fx, at, Pal.Gold, 8, Cell * 2.4f, Cell * .2f, .38f);
                Shockwave(at, Pal.Gold, 1.7f, .26f);
            }

            Number(at, shot.Damage, shot.Weak);

            if (shot.Killed) Audio.SfxVaried("burst", .42f);

            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == shot.Raider && _mob[i].Body != null)
                    Tween.Punch(_mob[i].Body.transform, .1f, .14f);
        }

        void Blow(SiegeBlow hit)
        {
            var post = _posts[hit.Ward];

            Tween.Shake(post.Node, Cell * .16f, .3f);
            ShakeBoard(hit.Felled ? 22f : 9f);

            Burst.Sparks(_fx, new Vector2(PostX(hit.Ward), _lineY), Pal.Rose, 9, Cell * 2f,
                         Cell * .2f, .4f);

            Audio.SfxVaried("blocked", hit.Felled ? .7f : .34f);

            // Only a ward coming down flashes the whole screen. A flash on every blow is five a
            // second once a wave is at the line, at which point it stops reading as damage taken
            // and starts reading as a fault.
            if (hit.Felled) Flow.Flash(new Color(1f, .32f, .30f), .34f, .3f);
        }

        void Fell(Post post)
        {
            var at = new Vector2(PostX(System.Array.IndexOf(_posts, post)), _lineY);

            Boom(at, Blast("boom_smoke"), Cell * 3.4f);
            Burst.Sparks(_fx, at, Pal.Ember, 20, Cell * 3f, Cell * .28f, .7f);
            Audio.Sfx("shatter", .8f, .78f);

            post.Body.sprite = Piece("ward_dead");
            post.Body.color = new Color(.52f, .52f, .56f, 1f);

            Tween.Rotate(post.Body.rectTransform, -16f, .5f, Ease.OutBounce);
            Tween.Fade(post.Glow, 0f, .3f);
            Tween.Fade(post.Juice, .25f, .3f);
        }

        void Arrival(int wave)
        {
            _wave = wave + 1;

            if (_waveLabel == null) return;

            _waveLabel.text = Loc.Format("mode.siege.wave", _wave, _board.Waves);

            var group = UIKit.Group(_waveLabel.rectTransform);
            var rt = _waveLabel.rectTransform;

            Tween.KillAll(_waveLabel);
            rt.anchoredPosition = new Vector2(0f, _hillFoot + Cell * 1.1f);
            group.alpha = 0f;

            Tween.Fade(group, 1f, .22f);
            Tween.Move(rt, new Vector2(0f, _hillFoot + Cell * 1.9f), 1.5f, Ease.OutCubic)
                 .OnDone(() => Tween.Fade(group, 0f, .4f));

            Audio.Sfx("bell", .5f, wave == 0 ? 1f : 1.08f);
            Flow.Flash(new Color(1f, .55f, .45f), .18f, .35f);
        }

        void Boom(Vector2 at, Sprite[] frames, float size)
        {
            if (frames == null || frames.Length == 0) return;

            var img = UIKit.Img("Boom", _fx, frames[0], Color.white, new Vector2(size, size));
            img.raycastTarget = false;
            img.rectTransform.anchoredPosition = at;

            var book = Flipbook.Attach(img, frames, 26f, false);
            if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
            else Tween.After(.6f, () => { if (img) Destroy(img.gameObject); });
        }

        void Number(Vector2 at, int damage, bool weak)
        {
            var label = UIKit.Label("Hit", _fx, damage.ToString(),
                                    Mathf.RoundToInt(Cell * (weak ? .42f : .32f)),
                                    weak ? Pal.Gold : Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(Cell * 2f, Cell * .6f));

            var rt = label.rectTransform;
            rt.anchoredPosition = at + new Vector2(Random.Range(-Cell * .2f, Cell * .2f), 0f);

            Tween.Move(rt, rt.anchoredPosition + new Vector2(0f, Cell * .8f), .55f, Ease.OutCubic);
            Tween.Fade(label, 0f, .55f, Ease.InQuad)
                 .OnDone(() => { if (label) Destroy(label.gameObject); });
        }

        // ------------------------------------------------------------------ the field
        void Poke(int cell)
        {
            if (!Playable) return;

            // A tap is not this mode's verb. The gem leans and comes back, which is the genre's
            // own answer to a tap on a jewel - and the screen says the sentence, rate-limited, so
            // a player poking about is answered once rather than shouted at.
            HideCoach();

            var gem = _gems[cell];
            if (gem != null && gem.Img != null) Refuse(gem.Img.rectTransform);

            Refused?.Invoke();
        }

        void Drag(int cell, Vector2Int dir)
        {
            if (!Playable) return;

            HideCoach();

            int x = cell % Width + dir.x;
            int y = cell / Width - dir.y;      // screen up is a lower row

            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                Refuse(_gems[cell].Img.rectTransform);
                return;
            }

            int other = y * Width + x;

            if (!_board.Lines(cell, other))
            {
                StartCoroutine(Rebuff(cell, other));
                return;
            }

            var turn = _board.Swap(cell, other);
            if (turn == null) return;

            Took(turn.Worth);
            _held = -1;

            Busy = true;
            StartCoroutine(Resolve(turn));
        }

        /// <summary>A swap that lines nothing up: the two lean into each other and come back.</summary>
        IEnumerator Rebuff(int a, int b)
        {
            Busy = true;

            var ga = _gems[a].Img.rectTransform;
            var gb = _gems[b].Img.rectTransform;
            Vector2 pa = CentreOf(a), pb = CentreOf(b);

            Audio.Sfx("blocked", .3f, 1.2f);

            Tween.Move(ga, Vector2.Lerp(pa, pb, .38f), .11f, Ease.OutQuad);
            Tween.Move(gb, Vector2.Lerp(pb, pa, .38f), .11f, Ease.OutQuad);

            yield return new WaitForSecondsRealtime(.12f);

            Tween.Move(ga, pa, .13f, Ease.OutBack);
            Tween.Move(gb, pb, .13f, Ease.OutBack);

            yield return new WaitForSecondsRealtime(.14f);

            Busy = false;
        }

        IEnumerator Resolve(SiegeTurn turn)
        {
            var ga = _gems[turn.A];
            var gb = _gems[turn.B];

            Tween.Move(ga.Img.rectTransform, CentreOf(turn.B), .15f, Ease.OutQuad);
            Tween.Move(gb.Img.rectTransform, CentreOf(turn.A), .15f, Ease.OutQuad);
            Audio.SfxVaried("rotate_a", .3f);

            yield return new WaitForSecondsRealtime(.16f);

            var keep = _gems[turn.A];
            _gems[turn.A] = _gems[turn.B];
            _gems[turn.B] = keep;
            Place(turn.A);
            Place(turn.B);

            for (int i = 0; i < turn.Beats.Count; i++)
                yield return Beat(turn.Beats[i]);

            Busy = false;
            Repaint();
            Changed?.Invoke();
            Judge();
        }

        IEnumerator Beat(SiegeBeat beat)
        {
            // What went, and where its fuel is going. The motes are the point of the whole
            // animation: a match is only ever worth the colour it was, so the colour has to be
            // seen leaving the field and arriving at a ward.
            for (int i = 0; i < beat.Cleared.Count; i++)
            {
                int cell = beat.Cleared[i];
                var gem = _gems[cell];
                if (gem == null || gem.Img == null) continue;

                var at = CentreOf(cell);
                var tint = TintOf(gem.Colour);

                // **A gem comes apart rather than switching off**, which is what came back from
                // play as "gems only disappear when they are matched". Three things at once and
                // none of them is the gem: a ring of debris in the gem's own colour, a flash under
                // it, and shards thrown outward. The gem itself does the smallest part - one
                // frame of swelling and then it is behind all of that.
                Shatter(at, tint, i * .012f);

                int ward = _layout.WardOf(SiegeLayout.Letters[gem.Colour]);
                if (ward >= 0) Mote(at, ward, tint, i * .012f);

                var img = gem.Img;
                Tween.Scale(img.transform, 1.45f, .07f, Ease.OutQuad).OnDone(() =>
                {
                    if (img) Tween.Scale(img.transform, 0f, .10f, Ease.InBack);
                });
                Tween.RotateBy(img.rectTransform, Random.Range(-70f, 70f), .17f, Ease.OutQuad);

                _gems[cell] = null;
                Tween.After(.23f, () => { if (img) Destroy(img.gameObject); });
            }

            Audio.SfxVaried("pop", .42f + Mathf.Min(.3f, beat.Depth * .08f));

            if (beat.Depth > 1) Chain(beat.Depth);

            yield return new WaitForSecondsRealtime(.2f);

            // The fall. New gems come in from above the field so a refill reads as a refill and
            // not as a board being redrawn.
            for (int i = 0; i < beat.Drops.Count; i++)
            {
                var drop = beat.Drops[i];
                int to = drop.To * Width + drop.Column;

                Gem gem;

                if (drop.IsNew)
                {
                    gem = Mint(drop.Colour);
                    gem.Img.rectTransform.anchoredPosition =
                        CentreOf(drop.Column) + new Vector2(0f, Cell * (1.1f - drop.From));
                }
                else
                {
                    gem = _gems[drop.From * Width + drop.Column];
                    if (gem == null) continue;
                    _gems[drop.From * Width + drop.Column] = null;
                }

                _gems[to] = gem;

                float far = Mathf.Abs(gem.Img.rectTransform.anchoredPosition.y - CentreOf(to).y);
                float fall = Mathf.Clamp(.09f + far / (Cell * 22f), .12f, .34f);

                Tween.Move(gem.Img.rectTransform, CentreOf(to), fall, Ease.OutBounce);
            }

            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        /// What a matched gem comes apart into.
        ///
        /// <para>
        /// One white reel tinted to the gem's colour rather than four painted ones — eighty
        /// textures against twenty, and four chances for one of them to stop matching <c>Pal</c>
        /// against none. It is the same reel every colour uses and the same <see cref="TintOf"/>
        /// the ward it feeds is painted from, which is what makes a match, its fuel and the bolt
        /// that fuel becomes visibly one colour all the way through.
        /// </para>
        /// </summary>
        void Shatter(Vector2 at, Color tint, float delay)
        {
            var frames = Blast("pop");

            if (frames != null && frames.Length > 0)
            {
                var img = UIKit.Img("Pop", _fx, frames[0], Pal.A(Pal.Lift(tint, .3f), 1f),
                                    new Vector2(Cell * 1.9f, Cell * 1.9f));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition = at;
                img.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var book = Flipbook.Attach(img, frames, 30f, false);
                if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
                else Tween.After(.5f, () => { if (img) Destroy(img.gameObject); });
            }

            Pop(at, tint, 1.7f, .3f);
            Burst.Sparks(_fx, at, tint, 8, Cell * 3.2f, Cell * .22f, .42f);
        }

        /// <summary>A gem's worth of fuel, flying from the field to the ward it feeds.</summary>
        void Mote(Vector2 from, int ward, Color tint, float delay)
        {
            var to = new Vector2(PostX(ward), _lineY - Cell * .62f);

            var img = UIKit.Img("Mote", _fx, Art.Spark(64), Pal.A(Pal.Lift(tint, .4f), 1f),
                                new Vector2(Cell * .3f, Cell * .3f));
            img.raycastTarget = false;

            var rt = img.rectTransform;
            rt.anchoredPosition = from;

            // Arced sideways, so a stream of them from one match reads as several things
            // travelling rather than as one line being drawn.
            float bow = Random.Range(-Cell * 1.1f, Cell * 1.1f);

            Tween.Run(.42f, Ease.InOutSine, t =>
            {
                if (!rt) return;
                var p = Vector2.Lerp(from, to, t);
                p.x += Mathf.Sin(t * Mathf.PI) * bow;
                rt.anchoredPosition = p;
                rt.localScale = Vector3.one * Mathf.Lerp(.7f, 1.25f, t);
            }, img).Delay(delay).OnDone(() =>
            {
                if (img) Destroy(img.gameObject);
                if (ward < _posts.Length) Fed(ward, tint);
            });
        }

        void Fed(int ward, Color tint)
        {
            var post = _posts[ward];
            if (post == null || post.Node == null) return;

            Pop(new Vector2(PostX(ward), _lineY - Cell * .62f), tint, .7f, .2f);
            Tween.Punch(post.Node, .1f, .2f);
            Audio.SfxVaried("lit", .18f);
        }

        void Chain(int depth)
        {
            var label = UIKit.Label("Chain", _fx, Loc.Format("mode.siege.chain", depth),
                                    Mathf.RoundToInt(Cell * .5f), Pal.Gold,
                                    TextAnchor.MiddleCenter, new Vector2(Span.x, Cell));
            label.rectTransform.anchoredPosition = new Vector2(0f, _gemCentre + Cell * .4f);

            label.transform.localScale = Vector3.one * .5f;
            Tween.Scale(label.transform, 1.1f, .2f, Ease.OutBack);
            Tween.Fade(label, 0f, .7f, Ease.InQuad)
                 .OnDone(() => { if (label) Destroy(label.gameObject); });

            Audio.Sfx("chime", .35f, Mathf.Min(1.6f, .9f + depth * .12f));
        }

        Gem Mint(int colour)
        {
            var img = UIKit.Img("Gem", _field, GemArt(colour), Color.white,
                                new Vector2(Cell * GemInset, Cell * GemInset));
            img.preserveAspect = true;

            return new Gem { Img = img, Colour = colour };
        }

        void Place(int cell)
        {
            var gem = _gems[cell];
            if (gem != null && gem.Img != null)
                gem.Img.rectTransform.anchoredPosition = CentreOf(cell);
        }

        // ------------------------------------------------------------------ upkeep
        protected override void Repaint()
        {
            if (_board == null) return;

            // The model is the authority on what is standing where; anything the animation left
            // behind is put right here rather than trusted.
            for (int i = 0; i < _gems.Count && i < Width * Height; i++)
            {
                var gem = _gems[i];
                int colour = _board.ColourAt(i);

                if (gem == null)
                {
                    _gems[i] = Mint(colour);
                    Place(i);
                    continue;
                }

                if (gem.Colour != colour)
                {
                    gem.Colour = colour;
                    gem.Img.sprite = GemArt(colour);
                }

                Place(i);
            }
        }

        /// <summary>
        /// Reads the verdict and ends the run if it says so.
        ///
        /// <b>Its own rather than <c>ProtoView.Settle</c></b>, and for one reason: the base asks
        /// whether the first move has landed, because everywhere else a run cannot be lost before
        /// it has been played. Here the hill walks whether or not anybody has touched a gem, so a
        /// player who watches the wards fall without moving has genuinely lost — and a run that
        /// simply never ended would be worse than either.
        /// </summary>
        void Judge()
        {
            if (Over || Run == null) return;

            var verdict = Run.Verdict;

            if (verdict.IsWon)
            {
                Over = true;
                Finishing?.Invoke();
                StartCoroutine(Triumph());
                return;
            }

            if (verdict.Ending != ProtoEnding.Stuck) return;

            Over = true;
            StartCoroutine(Ruin());
        }

        protected override IEnumerator Ruin()
        {
            Audio.Sfx("shatter", .85f, .7f);
            ShakeBoard(26f);
            Flow.Flash(new Color(1f, .28f, .24f), .5f, .5f);

            Fallen();

            yield return base.Ruin();
        }

        /// <summary>
        /// The word, over the board, before the panel.
        ///
        /// <para>
        /// <b>A run has to be told it is over on the board it was lost on.</b> Every other mode
        /// here ends with a modal a beat later and that is enough, because their boards visibly
        /// stop - a glade goes dark, a wall stops coming apart. A siege does not: the hill is
        /// still walking and the field is still full, so without this the half-second between the
        /// last ward falling and the panel arriving reads as nothing having happened.
        /// </para>
        /// </summary>
        void Fallen()
        {
            if (_fx == null) return;

            var label = UIKit.Label("Fallen", _fx, Loc.Get("mode.siege.fallen"),
                                    Mathf.RoundToInt(Cell * 1.05f), Pal.Rose,
                                    TextAnchor.MiddleCenter, new Vector2(Span.x, Cell * 2f));
            label.rectTransform.anchoredPosition = new Vector2(0f, _lineY + Cell * 2.2f);

            label.transform.localScale = new Vector3(2.4f, 2.4f, 1f);
            Tween.Scale(label.transform, 1f, .3f, Ease.OutBack);

            var group = UIKit.Group(label.rectTransform);
            group.alpha = 0f;
            Tween.Fade(group, 1f, .18f);
        }

        /// <summary>
        /// Points at the first match, once. <c>ProtoView</c>'s hand rings a cell; this mode's
        /// first move is a <em>drag</em>, so the hand is put on a gem that has one to make.
        /// </summary>
        public override void CoachTap()
        {
            HideCoach();

            if (_board == null) return;

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int here = y * Width + x;

                    if (x + 1 < Width && _board.Lines(here, here + 1)) { Point(here); return; }
                    if (y + 1 < Height && _board.Lines(here, here + Width)) { Point(here); return; }
                }
        }

        void Point(int cell)
        {
            var node = UIKit.Node("Coach", _fx);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(Cell, Cell);
            node.anchoredPosition = CentreOf(cell);

            CoachHand.Tap(node, Vector2.zero, Pal.Cream, this);
            Tween.After(3.4f, () => { if (node) Destroy(node.gameObject); });
        }
    }
}
