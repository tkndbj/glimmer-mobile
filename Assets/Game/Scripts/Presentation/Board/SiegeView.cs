using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
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
        const float HillBand = .44f, LineBand = .16f;

        /// <summary>
        /// The most of the board's height the gem field may take.
        ///
        /// <para>
        /// <b>A ceiling rather than a share, because the field is now laid out to the width.</b>
        /// The three bands were 44 / 16 / 40 of the height and the cell was whichever of width and
        /// height bound first — which on every phone was the height, so the gems sat in a column
        /// with a hand's width of empty plate either side of them. They fill the width now, and
        /// what that costs comes out of the hill: this is the line past which it stops costing the
        /// hill anything, because a hill with no room to walk down is the one band this mode
        /// cannot spend (invariant 37g).
        /// </para>
        /// </summary>
        const float MaxGemBand = .52f;

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

            /// <summary>The warlord's, and null for everything else.</summary>
            public bool Boss;

            /// <summary>What it stands in, what it comes on in, and what it throws with.</summary>
            public Sprite[] Idle, Walking, Casting;

            /// <summary>Which of the three its body is wearing now. See <see cref="SiegeView.Wear"/>.</summary>
            public Sprite[] Playing;

            /// <summary>The light it gathers before a spell leaves. Only a warlord has one.</summary>
            public Image Charge;

            /// <summary>
            /// A warlord's health, pinned across the top of the board rather than carried.
            ///
            /// Its own node under the effects layer, so it is not moved by the raider and has to
            /// be taken down by hand when one falls — see <see cref="SiegeView.Fall"/>.
            /// </summary>
            public RectTransform Crown;
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

        RectTransform _hill, _mobs, _wall, _field, _fx;

        readonly List<Gem> _gems = new List<Gem>(48);
        readonly List<Mob> _mob = new List<Mob>(24);
        readonly List<Mob> _order = new List<Mob>(24);
        Post[] _posts;
        Text _waveLabel;

        float _hillTop, _hillFoot, _lineY, _gemCentre;
        Vector2 _room;
        int _held = -1;
        int _wave;

        // ------------------------------------------------------------------ aiming
        RectTransform _aim;
        Image _marker;
        UtilityItem _arming;
        readonly List<SiegeStrike> _strikes = new List<SiegeStrike>(16);

        /// <summary>
        /// What the screen does when a target is chosen: apply it, charge for it and spend it.
        ///
        /// <para>
        /// <b>The view never spends and never charges by itself, and the split is deliberate.</b>
        /// What a utility does to a board is a rule (<c>SiegeUtility</c>); whether the player has
        /// one and whether they lose it is an account question the screen already owns; and what
        /// it costs the run is the shared allowance, which only a <c>ProtoView</c> may touch. So
        /// the screen owns the transaction and hands back what happened, and this class draws it.
        /// A view that took the item itself would be a second place that could charge for a use
        /// that did not land.
        /// </para>
        /// </summary>
        public System.Func<UtilityItem, SiegeAim, List<SiegeStrike>, SiegeUse> Fire { get; set; }

        /// <summary>Raised when a target was chosen and refused, so the screen can say why.</summary>
        public System.Action Rejected { get; set; }

        /// <summary>
        /// Raised once a chosen target has resolved, however it resolved.
        ///
        /// <b>The bar owns what is armed, and this is how it finds out.</b> Without it the view
        /// disarmed itself and the slot kept its ring — a highlight sitting on an item the player
        /// had already spent, which is what it looked like: an item stuck on. Two places holding
        /// one piece of state is the fault; one of them telling the other is the fix.
        /// </summary>
        public System.Action Done { get; set; }

        /// <summary>
        /// Which utility is being aimed, or null.
        ///
        /// Setting it builds or tears down the targeting layer, so nothing outside has to
        /// remember to do either — and disarming is what a paused board, a finished run and a
        /// tapped slot all do.
        /// </summary>
        public UtilityItem Arming
        {
            get => _arming;
            set
            {
                if (_arming == value) return;
                _arming = value;

                Aiming();
            }
        }

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

        /// <summary>
        /// How wide a reel drawn <paramref name="tall"/> high comes out, read off its own art.
        ///
        /// <b>Read rather than assumed, and it is the only thing here that is.</b> Invariant 16i
        /// says a piece's drawn <em>size</em> is authored and never measured off the loaded sprite,
        /// so a tile laid out before its art arrives is not laid out around a placeholder — and
        /// that is exactly what <paramref name="tall"/> is. What cannot be authored is the shape a
        /// cutting tool happened to give an animation's bounding box, which is a fact about the
        /// picture. Answers a square when the frames are not in hand, so a missing reel costs the
        /// picture and never the layout.
        /// </summary>
        static float Frame(Sprite[] frames, float tall)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return tall;

            var rect = frames[0].rect;
            return rect.height > 0f ? tall * rect.width / rect.height : tall;
        }

        /// <summary>Which of the cast a raider is drawn as, as frames. See <see cref="GemArt"/>.</summary>
        static Sprite[] Skin(SiegeRaider raider)
        {
            if (raider.Boss) return Reel("boss");
            if (raider.Brute) return Reel("brute");


            switch (raider.Colour % 3)
            {
                case 0: return Reel("mon1");
                case 1: return Reel("mon2");
                default: return Reel("mon3");
            }
        }

        /// <summary>
        /// How tall a raider is drawn, in cells.
        ///
        /// <para>
        /// <b>The warlord is nearly three times a creeper, and that is the whole of what makes it
        /// read as a boss before anything about it has happened.</b> Its health bar, its damage
        /// numbers and its silhouette all follow from this one number, and it is bounded by the
        /// hill rather than by taste: the band a raider walks down is about four cells deep, so a
        /// warlord at three fills three quarters of it and anything larger would stand in the
        /// ward line's own space.
        /// </para>
        /// </summary>
        static float TallOf(SiegeRaider raider)
            => raider.Boss ? 3.1f : raider.Brute ? 1.55f : 1.15f;

        /// <summary>
        /// Where a raider carries its health bar and its colour, as a fraction of its own height.
        ///
        /// <b>Over the head for a raider, and nowhere near a warlord</b> — see
        /// <see cref="Crown"/> for where a warlord's goes and why it had to leave its body.
        /// </summary>
        static float ReadoutAt(SiegeRaider raider) => .58f;

        /// <summary>
        /// A warlord's health bar, pinned across the top of the board.
        ///
        /// <para>
        /// <b>Off its body, and that is what let the warlord be big.</b> A carried bar has to sit
        /// somewhere, and on a hill four cells deep there is nowhere for one that belongs to
        /// something three cells tall: three renders put it outside the board's top edge (two
        /// widgets that had come loose), then on the ward line's own health bars (two readouts
        /// overlapping, which is two readouts nobody can read), and each time the answer was to
        /// make the warlord smaller — until it was barely taller than the turrets it was supposed
        /// to be looming over.
        /// </para>
        /// <para>
        /// A bar across the top is the genre's own answer and it is better for a second reason:
        /// this is the one health bar in the mode a player watches for half a minute rather than
        /// glances at, and it is the only one whose *place* can be learned. Nothing else on this
        /// board lives up there.
        /// </para>
        /// </summary>
        RectTransform Crown()
        {
            var node = UIKit.Node("Warlord", _fx);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(Span.x, Cell * .5f);
            node.anchoredPosition = new Vector2(0f, _hillTop + Cell * .12f);
            return node;
        }

        /// <summary>
        /// What a warlord throws, as frames.
        ///
        /// <b>One set rather than four, graded to a colour no ward and no gem wears.</b> The
        /// elemental double is a rule about bolts going <em>into</em> a raider, so a spell coming
        /// out of one that wore one of the board's four colours would be saying something the
        /// rules do not mean — a player would reasonably read it as "this hurts the blue ward
        /// more". See <c>SiegeShotBake</c>.
        /// </summary>
        static Sprite[] SpellArt() => Blast("spell");

        static Sprite[] SpellMuzzleArt() => Blast("spell_muzzle");

        static Sprite[] SpellHitArt() => Blast("spell_hit");

        /// <summary>
        /// The colour the warlord's magic is drawn in.
        ///
        /// Violet, which is the one entry in <c>Pal</c>'s board set that is not one of this mode's
        /// four gems — so nothing it lights can be mistaken for a colour rule.
        /// </summary>
        static readonly Color Spellfire = Pal.Foxglove;

        /// <summary>
        /// Rounded at the top and square at the foot, because the action bar is stacked directly
        /// under it.
        ///
        /// A fully rounded plate over a square shelf leaves two notches at the join, and at the
        /// bottom of a board they read as a gap rather than as two things meeting — which is what
        /// they are. This is the only mode with something under its board.
        /// </summary>
        protected override Sprite PlateSkin => Art.RoundTop(34);

        // ------------------------------------------------------------------ geometry
        /// <summary>
        /// The cell, driven by the width and capped by what the hill can spare.
        ///
        /// <b>The width leads.</b> Taking the smaller of the two meant the height always won and
        /// the field was a column in the middle of a full-width plate, which is the one thing on
        /// this screen that had no reason to be inset. See <see cref="MaxGemBand"/> for what caps
        /// it, and `Compose` for how the hill and the line then share what is left.
        /// </summary>
        protected override float Fit(Vector2 room)
        {
            _room = room;

            float wide = (room.x - Margin * 2f) / Width;
            float tall = (room.y - Margin * 2f) * MaxGemBand / Height;
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

            _tally.Clear();
            _chain = null;
            _chainAura = null;

            // The pool's widgets hang off `_fx`, which this rebuild replaces — so a spare kept
            // across it is a destroyed node handed out as a live one, and every bolt after the
            // first rebuild would be invisible.
            _spare.Clear();

            _held = -1;
            _wave = 0;

            // The targeting layer hangs off the layers this rebuild is about to replace, so a
            // board dealt again while something was armed would leave a destroyed node behind a
            // live `Arming` — and the bar would still be showing a ring. Cleared through the
            // field rather than the property, because the layer it would tear down is already
            // gone; the screen re-arms nothing, which is what a fresh board should be.
            _arming = null;
            _aim = null;
            _marker = null;

            // **The bands are derived from the cell, not the other way round.** The field is
            // laid out to fill the width (see `Fit`), so how much height its rows need is a fact
            // rather than a share — and the hill and the line then take what is left in the
            // proportion they were authored in. Written as a share of the authored pair rather
            // than as two more constants, so moving `HillBand` still moves only one thing.
            float h = Span.y;
            float gems = Mathf.Clamp(Cell * Height / h, .28f, MaxGemBand);
            float rest = 1f - gems;
            float hill = rest * (HillBand / (HillBand + LineBand));
            float line = rest - hill;

            _hillTop = h * .5f - Cell * .35f;
            _hillFoot = h * (.5f - hill);
            // The wards stand *high* on the line, so their heads break into the grass rather
            // than tucking under the field's plate. A render is why: at the middle of the band
            // they were half-hidden behind the plate and read as small.
            _lineY = _hillFoot - h * line * .30f;
            _gemCentre = (h * (.5f - hill - line) - h * .5f) * .5f;

            _hill = Layer("Hill");
            _mobs = Layer("Raiders");
            _wall = Layer("Line");
            _field = Layer("Field");
            _fx = Layer("Fx");

            Ground();
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

        // **The hill has no lanes drawn on it, and it used to.** Five pale strips at 4.5% white
        // marked where the raiders walk; over grass they were invisible and over the mine floor
        // they read as two seams running the height of the board. They were never load-bearing -
        // a raider's lane is visible from the raider - so they are gone rather than re-tinted:
        // a marking that has to be nearly invisible to be tolerable is a marking nothing needed.

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
            // **Four beats inside the quiet, and the quiet was lengthened to fit them.** At a
            // 2.2-second opening the numbers went past in half a second each and read as a
            // flicker; the count now paces itself and `FirstWaveAfter` is what it is so that
            // GO! and the first raider still land together. The count does not hold the game up
            // - the clock runs underneath it - so lengthening it lengthens the quiet, which is
            // the honest thing for it to do.
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

        // ------------------------------------------------------------------ aiming
        /// <summary>
        /// Builds or tears down the targeting layer for whatever is armed.
        ///
        /// <para>
        /// Built on arming rather than kept and hidden, because it is a <em>layer over the
        /// board</em>: a pad that stayed alive would sit in front of the gems for the whole run
        /// and swallow every swap, which is the class of fault a hidden raycast target always is.
        /// </para>
        /// </summary>
        void Aiming()
        {
            if (_aim != null)
            {
                Destroy(_aim.gameObject);
                _aim = null;
            }

            if (_arming == null || _fx == null) return;

            _aim = Layer("Aim");
            _aim.SetAsLastSibling();

            if (_arming.Target == UtilityTarget.Ward) AimWards();
            else AimHill();
        }

        /// <summary>
        /// The hill, drawn as the grid the rule reads: one box per lane per band, each a pane of
        /// blue with a ring in the middle of it.
        ///
        /// <para>
        /// <b>Boxes rather than a ring that follows a finger, and that is the whole change.</b> A
        /// radius round wherever a drag ended is exact in the rule and unreadable on the board:
        /// what a player had to do was judge a distance against raiders that were walking. A box
        /// is a place. It is tapped, it is the thing that lights, and it is exactly what burns —
        /// invariant 33g at its strongest, because <c>SiegeBoard.Blast</c> and this loop now share
        /// their integers rather than agreeing about a mapping.
        /// </para>
        /// <para>
        /// <b>The panes are geometry and never outcome</b> (invariant 32c). They say where the
        /// boxes are, which is a fact about the board; nothing here counts what is standing in one
        /// or marks the ones worth throwing at. That is the question the player is being asked.
        /// </para>
        /// </summary>
        void AimHill()
        {
            float top = _hillTop + Cell * .35f;
            float wide = Span.x / SiegeTuning.Lanes;
            float tall = (top - _hillFoot) / SiegeTuning.BlastRows;

            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                for (int lane = 0; lane < SiegeTuning.Lanes; lane++)
                {
                    int atLane = lane, atRow = row;

                    // Boxes are laid out from the top of the hill down, which is the direction
                    // `march` runs: row 0 is where a wave walks on.
                    var at = new Vector2(LaneX(lane), top - (row + .5f) * tall);

                    var pane = UIKit.Button("Aim" + lane + "_" + row, _aim, Art.Round(18),
                                            new Vector2(wide - 6f, tall - 6f),
                                            new Vector2(.5f, .5f), at,
                                            () => Loose(SiegeAim.OnTheHill(atLane, atRow)));

                    pane.PressScale = .96f;
                    pane.ClickSfx = null;

                    var face = pane.GetComponent<Image>();
                    if (face != null)
                    {
                        face.type = Image.Type.Sliced;
                        face.color = Pal.A(Pal.Azure, .18f);
                    }

                    // The ring in the middle: what says the box is a target rather than a tile,
                    // and where the burst will be centred.
                    var eye = UIKit.Img("Eye", pane.transform, Art.Ring(96, 6f),
                                        Pal.A(Pal.Azure, .78f),
                                        Vector2.one * Mathf.Min(wide, tall) * .46f);
                    eye.raycastTarget = false;

                    Tween.Breathe(eye.transform, .07f, 1.6f, row * .12f + lane * .05f);
                }
            }
        }

        /// <summary>A target over each standing ward. A fallen one is not a target.</summary>
        void AimWards()
        {
            if (_posts == null) return;

            for (int i = 0; i < _posts.Length; i++)
            {
                if (_board.Wards[i] == null || !_board.Wards[i].Alive) continue;

                int ward = i;

                // **Sized to the post it is round, not to a guess.** A ward's node is 1.8 by 2.3
                // cells with its body standing a hair above the middle of it, so a circle of 1.6
                // sat high and covered the barrel rather than the turret — reported as exactly
                // that. This is an ellipse round the whole of it, which is also what makes it a
                // target big enough to hit with a thumb.
                var size = new Vector2(Cell * 2.05f, Cell * 2.6f);

                var hit = UIKit.Button("AimWard" + i, _aim, Art.Ring(128, 7f),
                                       size, new Vector2(.5f, .5f),
                                       new Vector2(PostX(i), _lineY + Cell * .06f),
                                       () => Loose(SiegeAim.AtWard(ward)));

                hit.PressScale = .92f;
                hit.ClickSfx = null;

                var img = hit.GetComponent<Image>();
                if (img != null) img.color = Pal.A(Pal.Sun, .9f);

                Tween.Breathe(hit.transform, .06f, .9f);
            }
        }

        /// <summary>
        /// Hands a chosen target to the screen and draws whatever came back.
        ///
        /// <b>Disarmed first, whatever happens.</b> A refusal that left the layer up would put
        /// the player back in a targeting mode they had just been told they could not use, and a
        /// success that left it up would arm a second use of an item they may no longer hold.
        /// </summary>
        void Loose(SiegeAim aim)
        {
            var item = _arming;
            if (item == null || Fire == null) { Arming = null; return; }

            _strikes.Clear();
            var use = Fire(item, aim, _strikes);

            Arming = null;
            Done?.Invoke();

            if (!use.Landed)
            {
                Rejected?.Invoke();
                return;
            }

            Charged(use.Matches);
            Struck(item, aim, use);
            Judge();
        }

        /// <summary>What a landed utility looks like.</summary>
        void Struck(UtilityItem item, SiegeAim aim, SiegeUse use)
        {
            switch (item.Kind)
            {
                case UtilityKind.Blast:
                    Firepot(aim);
                    break;

                case UtilityKind.Mend:
                    Mended(use.Ward);
                    break;

                case UtilityKind.Surge:
                    Surged(use.Ward);
                    break;
            }

            for (int i = 0; i < _strikes.Count; i++) Hurt(_strikes[i]);

            Reap();
            Changed?.Invoke();
        }

        void Firepot(SiegeAim aim)
        {
            // The middle of the box that was tapped, worked out the way the panes were laid out —
            // one arithmetic, so the burst lands where the ring the player aimed at was.
            float top = _hillTop + Cell * .35f;
            float tall = (top - _hillFoot) / SiegeTuning.BlastRows;
            var at = new Vector2(LaneX(aim.Lane), top - (aim.Row + .5f) * tall);

            Boom(at, Blast("boom_fire"), Cell * 3.4f);
            Burst.Sparks(_fx, at, Pal.Ember, 22, Cell * 3f, Cell * .3f);
            Shockwave(at, Pal.Sun, Cell * 4.5f, .34f);
            ShakeBoard(26f);

            // Its own clip rather than the `burst` a raider's death plays. That one is struck
            // thirteen times in a wave and is tuned to be the shortest, brightest thing in the
            // set; a firepot is one event a run and the loudest thing a player can cause, so
            // sharing a sound would tune the big moment by the small one.
            Audio.SfxVaried("boom", .8f);
        }

        void Mended(int ward)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .5f);

            Burst.Sparks(_fx, at, Pal.Mint, 16, Cell * 2f, Cell * .2f, .5f);
            Shockwave(at, Pal.Mint, Cell * 2.6f, .30f);
            Tween.Pop(_posts[ward].Node, 1.12f, .28f);

            // A spell rather than an object. `chime` read as a coin landing here, which is the
            // wrong news about a ward being put back together.
            Audio.Sfx("mend", .75f);
        }

        void Surged(int ward)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length) return;

            var post = _posts[ward];
            var at = new Vector2(PostX(ward), _lineY + Cell * .5f);
            var tint = TintOf(_board.Wards[ward].Colour);

            Burst.Sparks(_fx, at, tint, 20, Cell * 2.2f, Cell * .2f, .45f);
            Shockwave(at, tint, Cell * 3f, .30f);
            Tween.Shake(post.Node, Cell * .1f, .3f);

            Audio.Sfx("lit", .7f, .85f);
        }

        /// <summary>One raider taking a hit from the player's own hand.</summary>
        void Hurt(SiegeStrike hit)
        {
            var raider = _board.Find(hit.Raider);
            var mob = raider == null ? MobOf(hit.Raider) : Widget(raider);
            if (mob == null || mob.Node == null) return;

            // Drawn as the player's own, which is bigger and hotter than a bolt's: a firepot
            // is one event a run and the loudest thing they can cause, and a figure the same size
            // as the eighteen a lit line throws every second is a figure nobody reads as theirs.
            Number(hit.Raider, mob.Node.anchoredPosition, hit.Damage, false, mine: true);

            if (!hit.Killed) Tween.Shake(mob.Node, Cell * .12f, .22f);
        }

        /// <summary>The widget for a raider the board has already taken off the hill.</summary>
        Mob MobOf(int id)
        {
            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == id) return _mob[i];

            return null;
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
            for (int i = 0; i < report.Casts.Count; i++) Cast(report.Casts[i]);
            for (int i = 0; i < report.Spells.Count; i++) Smite(report.Spells[i]);
            for (int i = 0; i < report.Blows.Count; i++) Blow(report.Blows[i]);

            Reap();

            if (report.Any) Changed?.Invoke();

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

                // **A warlord walks on and then stands, and which of the two it wears is read off
                // the board rather than latched at spawn.** It was drawn in its idle for the whole
                // walk-in, which came back from play in one word — *floating* — and is exactly the
                // fault this mode's cast reels are walks to avoid (`make_siege_art.CAST_SET`). The
                // question is asked every frame and `Wear` answers it once.
                if (mob.Boss && !Throwing(mob))
                    Wear(mob, raider.InPlace || mob.Walking == null ? mob.Idle : mob.Walking);
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

            float tall = Cell * TallOf(raider);
            mob.Height = tall;
            mob.Boss = raider.Boss;

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

            // **The warlord gathers its spell in a light of its own, under everything else.**
            // Built here and left dark rather than made when a cast starts: a widget that appears
            // at the moment something happens has to be faded in before it can be seen growing,
            // and what this is for is the growing.
            if (raider.Boss)
            {
                mob.Charge = UIKit.Img("Charge", mob.Node, Art.Glow(128, 2.0f),
                                       Pal.A(Spellfire, 0f),
                                       new Vector2(tall * 1.5f, tall * 1.5f));
                mob.Charge.raycastTarget = false;
            }

            mob.Idle = Skin(raider);
            mob.Walking = raider.Boss ? Reel("boss_walk") : null;
            mob.Casting = raider.Boss ? Reel("boss_cast") : null;

            // A warlord comes on *walking* and stands still once it is in place; everything else
            // is walking for its whole life, so its one reel is both.
            mob.Playing = raider.Boss && mob.Walking != null && !raider.InPlace
                        ? mob.Walking : mob.Idle;

            mob.Body = Book(mob.Playing, "Body", mob.Node, new Vector2(tall, tall),
                            raider.Boss ? BossFps : raider.Brute ? 10f : 13f);

            if (mob.Body != null)
            {
                mob.Body.color = mob.Coat;

                // **Sized by its own height, with the width following the picture** — never fitted
                // into a square. Every cast reel is cut to a fixed height and whatever width the
                // animation's box came out as (`make_siege_art.cast_frames`), so a square box with
                // `preserveAspect` fits the *wider* ones by width and draws them short: measured
                // on the shipped art, `mon2` is 300 x 180 and was being drawn at 60% of the height
                // `mon1` gets, which is why one of the three creepers has always looked squat. The
                // warlord is the same fault at three times the size and would have been obvious,
                // which is how this was found.
                float wide = Frame(mob.Playing, tall);

                mob.Body.rectTransform.sizeDelta = new Vector2(wide, tall);

                // The packs draw them facing right; this hill runs top to bottom, so they are
                // turned to face down the way a walk cycle reads best - across, and coming on.
                mob.Body.rectTransform.anchoredPosition = new Vector2(0f, tall * .04f);
                Tween.Bob(mob.Body.rectTransform, tall * .035f,
                          raider.Boss ? 1.6f : raider.Brute ? 1.1f : .72f,
                          raider.Id * .37f);
            }

            // A warlord's readouts hang off the top of the board rather than off its body.
            if (raider.Boss) mob.Crown = Crown();

            var perch = mob.Crown != null ? mob.Crown : mob.Node;

            mob.Bar = UIKit.Node("Bar", perch);
            mob.Bar.anchorMin = mob.Bar.anchorMax = new Vector2(.5f, .5f);

            // A warlord's bar is thicker as well as wider, because it is the one health bar in
            // this mode a player watches for half a minute rather than glances at.
            mob.Bar.sizeDelta = raider.Boss
                ? new Vector2(Span.x * .60f, Cell * .26f)
                : new Vector2(tall * .72f, Cell * .13f);

            mob.Bar.anchoredPosition = raider.Boss
                ? new Vector2(Cell * .34f, 0f)
                : new Vector2(0f, tall * ReadoutAt(raider));

            var trough = UIKit.Img("Trough", mob.Bar, Art.Round(10), new Color(0f, 0f, 0f, .66f),
                                   mob.Bar.sizeDelta);
            trough.raycastTarget = false;
            trough.type = Image.Type.Sliced;

            mob.Fill = UIKit.Img("Fill", mob.Bar, Art.Round(10),
                                 raider.Boss ? Pal.Ember : raider.Brute ? Pal.Foxglove : Pal.Rose,
                                 new Vector2(mob.Bar.sizeDelta.x - 4f, mob.Bar.sizeDelta.y - 4f));
            mob.Fill.raycastTarget = false;
            mob.Fill.type = Image.Type.Sliced;
            mob.Fill.rectTransform.pivot = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

            // The gem over its head is the third thing that says its colour, and on the warlord
            // it is the one that has to carry: the body is so large that a 62% coat reads as
            // "purple alien with a red wash" rather than as red.
            float pip = Cell * (raider.Boss ? .62f : .34f);

            mob.Pip = UIKit.Img("Pip", perch, GemArt(raider.Colour), Color.white,
                                new Vector2(pip, pip));
            mob.Pip.raycastTarget = false;
            mob.Pip.preserveAspect = true;

            // At the head of the bar for a warlord, over the shoulder for everything else.
            mob.Pip.rectTransform.anchoredPosition = raider.Boss
                ? new Vector2(Cell * .34f - Span.x * .30f - pip * .72f, 0f)
                : new Vector2(-tall * .46f, tall * ReadoutAt(raider));

            mob.Node.localScale = Vector3.one * .5f;
            Tween.Scale(mob.Node, 1f, raider.Boss ? .6f : .3f, Ease.OutBack);

            var group = UIKit.Group(mob.Node);
            group.alpha = 0f;
            Tween.Fade(group, 1f, .26f);

            _mob.Add(mob);
            return mob;
        }

        /// <summary>How fast a warlord's own frames run. Slow, because it is a heavy thing.</summary>
        const float BossFps = 11f;

        /// <summary>
        /// Puts one of a raider's reels on its body, and remembers which.
        ///
        /// <para>
        /// <b>Remembering is the whole job.</b> <c>Flipbook.Attach</c> re-starts a reel from frame
        /// nought, so a caller that attached the wanted reel every frame would draw frame nought
        /// for ever — a walk cycle that never takes a step, which is the fault this method exists
        /// to fix, arrived at from the other side. <see cref="Mob.Playing"/> is what lets
        /// <see cref="Follow"/> ask the question sixty times a second and answer it once.
        /// </para>
        /// </summary>
        void Wear(Mob mob, Sprite[] reel, bool loop = true)
        {
            if (mob == null || mob.Body == null || reel == null || reel.Length == 0) return;
            if (mob.Playing == reel && loop) return;

            mob.Playing = reel;
            Flipbook.Attach(mob.Body, reel, BossFps, loop);

            // The frame's shape is a fact about the picture (see `Frame`), and a warlord's three
            // reels are cut onto one canvas so that this never actually changes - which is exactly
            // why it is worth setting rather than assuming.
            mob.Body.rectTransform.sizeDelta = new Vector2(Frame(reel, mob.Height), mob.Height);
        }

        /// <summary>
        /// Whether a raider's body is showing something it must not be interrupted in.
        ///
        /// Only a warlord has one, and it is the cast: <see cref="Follow"/> runs every frame and
        /// would otherwise put the idle back on the frame after a spell started.
        ///
        /// <b>Named away from <c>Busy</c> deliberately</b> — that is <c>ProtoView</c>'s latch for a
        /// cascade still falling, and a second member of the same name in one hierarchy is what
        /// <c>ModeScreen.Prepare</c> was renamed to avoid.
        /// </summary>
        static bool Throwing(Mob mob) => mob.Casting != null && mob.Playing == mob.Casting;

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

            if (mob.Boss) { Fall(mob, at); return; }

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

        /// <summary>
        /// The warlord coming apart.
        ///
        /// <b>The largest thing that happens in this mode, and it is drawn as a run rather than as
        /// one bang.</b> A boss that vanished in the same puff a creeper does would be the whole
        /// fight paying out in a tenth of a second — which is invariant 20m's rule about a payoff
        /// asked of the one moment the player has been working toward for half a minute. Five
        /// explosions walking outward, then the shape going down.
        /// </summary>
        void Fall(Mob mob, Vector2 at)
        {
            var node = mob.Node;
            var group = UIKit.Group(node);
            var crown = mob.Crown;

            // The bar goes with it, and by hand: it hangs off the effects layer rather than off
            // the body, so destroying the body would leave an empty warlord's health bar pinned
            // across the top of a board with no warlord on it.
            if (crown != null)
            {
                var over = UIKit.Group(crown);
                Tween.Fade(over, 0f, .5f).Delay(.5f)
                     .OnDone(() => { if (crown) Destroy(crown.gameObject); });
            }

            Audio.Sfx("boom", .9f, .72f);
            Flow.Flash(new Color(1f, .82f, .55f), .55f, .5f);
            ShakeBoard(30f);

            for (int i = 0; i < 5; i++)
            {
                float wait = i * .11f;
                var spot = at + new Vector2(Random.Range(-1f, 1f) * mob.Height * .34f,
                                            Random.Range(-1f, 1f) * mob.Height * .30f);

                Tween.After(wait, () =>
                {
                    Boom(spot, Blast("boom_fire"), mob.Height * 1.5f);
                    Burst.Sparks(_fx, spot, Pal.Ember, 12, mob.Height * 1.2f, mob.Height * .16f);
                    Audio.SfxVaried("burst", .42f);
                });
            }

            Tween.Shake(node, Cell * .3f, .55f);

            Tween.Run(.95f, Ease.InQuad, t =>
            {
                if (!node) return;
                node.localScale = new Vector3(1f + t * .18f, 1f - t * .68f, 1f);
                if (group) group.alpha = 1f - t * t;
            }, node).Delay(.35f).OnDone(() =>
            {
                if (!node) return;

                Boom(at, Blast("boom_smoke"), mob.Height * 2.6f);
                Shockwave(at, Pal.Gold, 7f, .55f);
                Destroy(node.gameObject);
            });
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
            var puff = Lend(frames, Color.white, Cell * 1.0f, at, angle, 30f, true, HeadAt);
            Glow(puff, tint, Cell * 1.7f);
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
        /// <b>Longer than it was, twice, and the art is the reason both times.</b> A round
        /// crossing the hill in six hundredths of a second is a dot teleporting whatever is drawn
        /// on it — fine while it was a dot, and it throws away a fourteen-frame comet. Doubled
        /// again after play: at a fifth of a second the animation was still over before it could
        /// be looked at, which was reported as not being able to see it at all.
        ///
        /// <para>
        /// <b>It is deliberately longer than the cadence now, and that is a change of shape rather
        /// than of degree.</b> A ward fires every <c>SiegeTuning.FireEvery</c> (.22), so a flight
        /// of up to .40 puts two of a ward's own bolts in the air at once — the line reads as a
        /// stream of comets rather than as one thing at a time, which is what makes a trail
        /// visible at all. What stops that being a hose is the cadence, which was slowed for the
        /// same verdict and cannot go further without losing the level.
        /// </para>
        /// </para>
        /// </summary>
        const float ShortestFlight = .20f, LongestFlight = .40f;

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
                            Cell * (own ? 2.7f : 1.7f), at,
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
                Ends(Lend(frames, Color.white, Cell * (shot.Killed ? 4.1f : 3.2f), at, angle,
                          35f, false, .5f), .35f);

            Pop(at, shot.Weak ? Pal.Gold : tint, shot.Weak ? 1.9f : 1.2f, .24f);

            // A double is drawn as a *different kind* of hit rather than a bigger one: gold, a
            // ring, and sparks. It is the mode's one rule and the board is where it is said.
            if (shot.Weak)
            {
                Burst.Sparks(_fx, at, Pal.Gold, 8, Cell * 2.4f, Cell * .2f, .38f);
                Shockwave(at, Pal.Gold, 1.7f, .26f);
            }

            Number(shot.Raider, at, shot.Damage, shot.Weak);

            if (shot.Killed) Audio.SfxVaried("burst", .42f);

            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == shot.Raider && _mob[i].Body != null)
                    Tween.Punch(_mob[i].Body.transform, .1f, .14f);
        }

        // ------------------------------------------------------------------ the warlord's spells
        /// <summary>
        /// A spell, from the moment the warlord decides on it to the moment it leaves.
        ///
        /// <para>
        /// <b>Four beats a player can read, and the first three are the point.</b> The warlord
        /// swings into its own attack frames; a violet light gathers on it; and a ring closes over
        /// the ward it has chosen — so what is about to happen, and to whom, is on the board for
        /// <c>SiegeTuning.BossTell</c> before it happens. That window is not decoration: it is
        /// long enough to pour a <c>mending</c> into the ward that is about to be hit, which is
        /// the one thing on this board a player can do about a warlord other than shoot it.
        /// </para>
        /// <para>
        /// <b>The schedule comes out of the rules and is never invented here</b> (invariant 37s).
        /// The board takes the ward's health exactly <c>BossTell + BossFlight</c> after this, so a
        /// bolt drawn on any other clock would arrive before or after the damage it is meant to be.
        /// </para>
        /// </summary>
        void Cast(SiegeCast cast)
        {
            var mob = MobOf(cast.Raider);
            if (mob == null || mob.Node == null) return;

            Vector2 from = mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .10f);
            Vector2 to = new Vector2(PostX(cast.Ward), _lineY + Cell * .3f);

            // The alien's own attack frames, once, and back to standing. A warlord that only ever
            // cycled its idle would have no way to say it had done anything, which is the fault
            // the ward line's recoil frames were added for.
            if (mob.Body != null && mob.Casting != null && mob.Casting.Length > 0)
            {
                mob.Playing = mob.Casting;

                var book = Flipbook.Attach(mob.Body, mob.Casting,
                                           mob.Casting.Length / SiegeTuning.BossTell, false);

                // Handed straight back to `Follow`, which is the only thing that decides what a
                // warlord wears: clearing the latch is all that is needed, and a callback that
                // put a *particular* reel back would be a second opinion about a question already
                // answered above.
                if (book != null) book.OnFinished = () => { if (mob.Body) mob.Playing = null; };
                else mob.Playing = null;
            }

            Gather(mob);
            Sigil(cast.Ward);

            Audio.Sfx("whoosh", .5f, .74f);

            // The bolt itself leaves when the wind-up ends, and crosses in `BossFlight` — but only
            // if the warlord is still standing when it does. The board already fizzles a spell
            // whose caster has been destroyed (`SiegeBoard.Arrive`), and a spell drawn crossing the
            // hill that then does nothing is worse than one that was never thrown: it reads as the
            // hit having been missed rather than as the cast having been interrupted.
            int caster = cast.Raider;

            Tween.After(SiegeTuning.BossTell, () =>
            {
                if (_board != null && _board.Find(caster) != null) Hurl(from, to);
            }, mob.Node);
        }

        /// <summary>The light a warlord gathers before a spell leaves it.</summary>
        void Gather(Mob mob)
        {
            var glow = mob.Charge;
            if (glow == null) return;

            Tween.KillChannel(glow, "gather");

            Tween.Run(SiegeTuning.BossTell, Ease.InQuad, t =>
            {
                if (!glow) return;
                glow.color = Pal.A(Spellfire, t * .95f);
                glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.35f, 1.15f, t);
            }, glow, "gather").OnDone(() =>
            {
                if (!glow) return;
                Tween.Run(.22f, Ease.OutQuad, t =>
                {
                    if (!glow) return;
                    glow.color = Pal.A(Spellfire, (1f - t) * .95f);
                }, glow, "gather");
            });
        }

        /// <summary>
        /// The ring that closes over the ward a spell is coming for.
        ///
        /// <b>It closes rather than expanding</b>, which is the opposite of every other ring on
        /// this board and is why: <c>Shockwave</c> grows outward and means <em>something has
        /// happened here</em>, and this means <em>something is about to</em>. A countdown that
        /// looks like an explosion is a warning nobody reads as one.
        /// </summary>
        void Sigil(int ward)
        {
            var at = new Vector2(PostX(ward), _lineY + Cell * .3f);

            var ring = UIKit.Img("Sigil", _fx, Art.Ring(128, 10f), Pal.A(Spellfire, .95f),
                                 new Vector2(Cell, Cell));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            Tween.Run(SiegeTuning.BossTell, Ease.Linear, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(4.4f, 1.5f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 220f);
            }, ring).OnDone(() =>
            {
                if (!ring) return;
                Tween.Fade(ring, 0f, SiegeTuning.BossFlight)
                     .OnDone(() => { if (ring) Destroy(ring.gameObject); });
            });
        }

        /// <summary>The spell crossing the hill, and the flash it leaves the warlord with.</summary>
        void Hurl(Vector2 from, Vector2 to)
        {
            var dir = to - from;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            var muzzle = SpellMuzzleArt();
            if (muzzle != null && muzzle.Length > 0)
                Ends(Lend(muzzle, Color.white, Cell * 4.2f, from, angle, 30f, false, MuzzleAt),
                     .38f);

            // The pack's own flash for this one is faint - a thin ring and a few sparks - so the
            // moment the spell *leaves* is carried by these two rather than by it. Cheaper than
            // swapping a whole three-part set for its weakest part, which is what the alternative
            // was: its orb and its impact are the best in the pack.
            Shockwave(from, Pal.Lift(Spellfire, .5f), 3.4f, .3f);
            Pop(from, Spellfire, 2.6f, .26f);

            Audio.Sfx("poke", .5f, .62f);

            var frames = SpellArt();

            if (frames == null || frames.Length == 0)
                frames = new[] { Art.Glow(96, 2.0f) };

            // **Half again the width of a ward's bolt, and anchored at its middle rather than at
            // `HeadAt`.** A warlord's spell is an orb rather than a comet — it is baked square
            // (`SiegeShotBake.BakeSpell`), so there is no head leading a trail to step back from.
            var puff = Lend(frames, Color.white, Cell * 1.6f, from, angle, 30f, true, .5f);
            Glow(puff, Spellfire, Cell * 3f);

            var node = puff.Node;

            Tween.Run(SiegeTuning.BossFlight, Ease.Linear, t =>
            {
                if (!node) return;
                node.anchoredPosition = Vector2.Lerp(from, to, t);
                node.localScale = Vector3.one * Mathf.Lerp(.8f, 1.25f, t);
            }, node).OnDone(() => Give(puff));
        }

        /// <summary>
        /// A spell arriving on the line.
        ///
        /// <b>Drawn nothing like a blow</b>, which is why it is its own record: a blow is
        /// something swung by a raider standing at the line, and this comes out of the middle of
        /// the hill. It is also the heaviest single hit in the mode, so it takes the shake a felled
        /// ward used to have to itself.
        /// </summary>
        void Smite(SiegeSpell spell)
        {
            var at = new Vector2(PostX(spell.Ward), _lineY + Cell * .3f);

            var frames = SpellHitArt();
            if (frames != null && frames.Length > 0)
                Ends(Lend(frames, Color.white, Cell * (spell.Felled ? 5.2f : 4.2f), at, 0f, 32f,
                          false, .5f), .4f);

            Pop(at, Spellfire, 2.6f, .3f);
            Shockwave(at, Spellfire, 3.6f, .34f);
            Burst.Sparks(_fx, at, Spellfire, 14, Cell * 3f, Cell * .24f, .5f);

            Tween.Shake(_posts[spell.Ward].Node, Cell * .26f, .38f);
            ShakeBoard(spell.Felled ? 26f : 15f);

            Audio.Sfx("boom", spell.Felled ? .8f : .5f, spell.Felled ? .8f : 1f);

            if (spell.Felled) Flow.Flash(new Color(1f, .32f, .30f), .34f, .3f);
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

            // **The warlord's wave is announced as itself rather than as a number.** "WAVE 4 OF 4"
            // is true and is the wrong thing to say about the one wave that is not like the others
            // — the header carries the count for anybody who wants it (`SiegeScreen.Readouts`), and
            // what the board owes this moment is the news.
            bool boss = _board.BossWave;

            _waveLabel.text = boss ? Loc.Get("mode.siege.boss")
                                   : Loc.Format("mode.siege.wave", _wave, _board.Waves);

            _waveLabel.color = boss ? Pal.Foxglove : Pal.Cream;
            _waveLabel.fontSize = Mathf.RoundToInt(Cell * (boss ? .78f : .46f));

            var group = UIKit.Group(_waveLabel.rectTransform);
            var rt = _waveLabel.rectTransform;

            Tween.KillAll(_waveLabel);
            rt.anchoredPosition = new Vector2(0f, _hillFoot + Cell * 1.1f);
            rt.localScale = Vector3.one * (boss ? 1.6f : 1f);
            group.alpha = 0f;

            Tween.Fade(group, 1f, .22f);
            if (boss) Tween.Scale(rt, 1f, .5f, Ease.OutBack);

            Tween.Move(rt, new Vector2(0f, _hillFoot + Cell * 1.9f), boss ? 2.4f : 1.5f,
                       Ease.OutCubic)
                 .OnDone(() => Tween.Fade(group, 0f, .4f));

            // **No sound of its own, except for the warlord.** The first wave steps out on the
            // same frame the countdown says GO!, so a bell there was the same bell twice a frame
            // apart - which is a flam rather than emphasis. Nothing lands with the warlord, so it
            // is the one arrival that can be heard as well as seen.
            if (boss)
            {
                Audio.Sfx("boom", .85f, .62f);
                ShakeBoard(20f);
                Flow.Flash(Pal.A(Spellfire, 1f), .5f, .45f);
            }
            else
            {
                Flow.Flash(new Color(1f, .55f, .45f), .18f, .35f);
            }
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

        /// <summary>
        /// One raider's damage as it lands, floating off it.
        ///
        /// <para>
        /// <b>Numbers are tallied per raider rather than one to a bolt, and that is what makes
        /// them readable here at all.</b> A lit line fires every <c>SiegeTuning.FireEvery</c>, so
        /// four wards on one target is about eighteen hits a second — eighteen separate figures a
        /// second is a wall of text nobody can read one number out of, and drawing each of them
        /// bigger makes that worse rather than better. So a hit landing on a raider that is
        /// already showing a number <em>adds to it</em>: the figure climbs, grows, punches again
        /// and its float restarts, and what the player watches is one number running up while they
        /// hold fire on something. The next one starts its own after <see cref="TallyFor"/> of
        /// quiet.
        /// </para>
        /// <para>
        /// <b>A double reads as a different kind of number rather than a bigger one</b> — gold, a
        /// much harder punch, a longer and higher float — because the elemental double is the one
        /// rule this mode is about, and this is the only place it is ever said in figures.
        /// </para>
        /// </summary>
        const float TallyFor = .34f;

        sealed class Tally
        {
            public int Raider, Total;
            public bool Weak;

            /// <summary>Caused by the player rather than by a ward. Drawn bigger and hotter.</summary>
            public bool Mine;
            public float Until, Drift;
            public Text Label;
            public RectTransform Rt;
        }

        readonly Dictionary<int, Tally> _tally = new Dictionary<int, Tally>();

        /// <summary>Which way the next number leans, so a run of them fans out instead of stacking.</summary>
        int _fan;

        void Number(int raider, Vector2 at, int damage, bool weak, bool mine = false)
        {
            if (_tally.TryGetValue(raider, out var running) && running.Label &&
                Time.unscaledTime < running.Until)
            {
                running.Total += damage;
                running.Weak |= weak;
                running.Mine |= mine;
                running.Until = Time.unscaledTime + TallyFor;

                Paint(running);
                Punch(running);
                Rise(running);
                return;
            }

            var tally = new Tally
            {
                Raider = raider,
                Total = damage,
                Weak = weak,
                Mine = mine,
                Until = Time.unscaledTime + TallyFor,
                Drift = (_fan++ & 1) == 0 ? -1f : 1f,
            };

            // Built through `Titled` rather than `Label`: a bare figure over a lit hill and a
            // bright cast is unreadable, and the outline is most of what a floating number is.
            tally.Label = UIKit.Titled("Hit", _fx, damage.ToString(), 24, Pal.Cream,
                                       TextAnchor.MiddleCenter,
                                       new Vector2(Cell * 5f, Cell * 2f), default, default,
                                       Cell * .055f, Cell * .06f);

            tally.Rt = tally.Label.rectTransform;
            tally.Rt.anchoredPosition =
                at + new Vector2(Random.Range(-Cell * .3f, Cell * .3f), Cell * .45f);

            _tally[raider] = tally;

            Paint(tally);
            Punch(tally);
            Rise(tally);
        }

        /// <summary>The figure, sized by what it has come to. A big number is a big number.</summary>
        void Paint(Tally tally)
        {
            if (!tally.Label) return;

            float grown = Mathf.Min(tally.Total, 30) / 30f;
            float step = tally.Mine ? .82f : tally.Weak ? .58f : .40f;
            float size = Cell * step * (1f + grown * .5f);

            tally.Label.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            tally.Label.text = tally.Total.ToString();
            tally.Label.color = tally.Mine ? Pal.Ember : tally.Weak ? Pal.Gold : Pal.Cream;
        }

        /// <summary>The arrival: overshoot and settle, once per hit that lands on it.</summary>
        void Punch(Tally tally)
        {
            var rt = tally.Rt;
            if (!rt) return;

            float from = tally.Weak ? 1.85f : 1.4f;

            Tween.KillChannel(tally.Label, "pop");
            Tween.Run(.17f, Ease.OutBack, k =>
            {
                if (rt) rt.localScale = Vector3.one * Mathf.Lerp(from, 1f, k);
            }, tally.Label, "pop");
        }

        /// <summary>
        /// The float, restarted from wherever the number has got to every time it takes another
        /// hit — so a figure still climbing does not drift off in the middle of its own tally.
        ///
        /// Every channel is owned by the <c>Text</c> rather than by its transform, because they
        /// are two different Unity objects and a channel killed on one is not killed on the other.
        /// </summary>
        void Rise(Tally tally)
        {
            var rt = tally.Rt;
            var label = tally.Label;
            if (!rt || !label) return;

            Tween.KillChannel(label, "rise");
            Tween.KillChannel(label, "fade");

            var opaque = label.color;
            opaque.a = 1f;
            label.color = opaque;

            // **Lower and shorter than it was.** The first cut floated a cell and a half over a
            // second, which is a long time for a figure to be over the hill when the next one is
            // 55 milliseconds behind it — the numbers stacked up the screen and stayed there. What
            // a floating number owes the player is to be legible on arrival and then get out of
            // the way, so it lifts about half a cell and is gone inside two thirds of a second.
            float life = tally.Weak ? .68f : .52f;
            Vector2 from = rt.anchoredPosition;
            Vector2 to = from + new Vector2(tally.Drift * Cell * .22f,
                                            Cell * (tally.Weak ? .75f : .55f));

            Tween.Run(life, Ease.OutCubic, k =>
            {
                if (rt) rt.anchoredPosition = Vector2.Lerp(from, to, k);
            }, label, "rise");

            // Held at full for the first half and only then let go, because a number that starts
            // fading the instant it appears is one nobody has finished reading.
            Tween.Run(life, Ease.Linear, k =>
            {
                if (!label) return;
                var colour = label.color;
                colour.a = k < .45f ? 1f : 1f - (k - .45f) / .55f;
                label.color = colour;
            }, label, "fade").OnDone(() => Retire(tally));
        }

        void Retire(Tally tally)
        {
            if (_tally.TryGetValue(tally.Raider, out var held) && held == tally)
                _tally.Remove(tally.Raider);

            if (tally.Label) Destroy(tally.Label.gameObject);
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

            Tween.Move(ga.Img.rectTransform, CentreOf(turn.B), SiegeTuning.SwapFor * .94f,
                       Ease.OutQuad);
            Tween.Move(gb.Img.rectTransform, CentreOf(turn.A), SiegeTuning.SwapFor * .94f,
                       Ease.OutQuad);
            Audio.SfxVaried("rotate_a", .3f);

            yield return new WaitForSecondsRealtime(SiegeTuning.SwapFor);

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

            // Halves of `BeatFor`, because the board books this beat's fuel to land a whole
            // `BeatFor` after the last one - see `SiegeTuning.FuelLands`. Typed here they would
            // drift, and a mote that arrives after its fuel does is the bug this schedule exists
            // to stop.
            yield return new WaitForSecondsRealtime(SiegeTuning.BeatFor * .5f);

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

            yield return new WaitForSecondsRealtime(SiegeTuning.BeatFor * .5f);
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

            Tween.Run(SiegeTuning.FuelFlight, Ease.InOutSine, t =>
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

        /// <summary>
        /// How hot a chain reads. Yellow, gold, ember, rose — a heat ladder rather than one
        /// colour at four sizes, because the thing being said is <em>how big</em>.
        /// </summary>
        /// <summary>The dark behind the banner, which a heavy outline alone cannot replace here.</summary>
        static Color Under(float alpha) => new Color(.04f, .06f, .09f, .62f * alpha);

        static Color ChainHeat(int depth)
        {
            switch (depth)
            {
                case 2: return Pal.Sun;
                case 3: return Pal.Gold;
                case 4: return Pal.Ember;
                default: return Pal.Rose;
            }
        }

        Text _chain;
        Image _chainAura;

        /// <summary>
        /// A cascade, announced over the ward line.
        ///
        /// <para>
        /// <b>Over the turrets rather than over the field, which is where it was and where nobody
        /// saw it.</b> It sat just above the gems in a plain label at half the size it is now — on
        /// top of the one part of the board the player is already staring at, in the same band as
        /// forty gems, with no outline to separate it from any of them. A chain is the loudest
        /// thing that can happen on this board and it read as a caption.
        /// </para>
        /// <para>
        /// It is drawn on the empty run of hill just above the line: nothing else lives there, the
        /// eye is already going that way to see what the wards are shooting, and it is far enough
        /// from the field that it never covers the move that earned it. A soft dark aura sits under
        /// it, because a heavy outline alone is not enough over a lit hill.
        /// </para>
        /// <para>
        /// <b>One banner, reused.</b> A cascade raises this once per wave of it, so two arriving in
        /// a quarter of a second would otherwise be two labels in one place — the second one
        /// re-punches the first instead, which is also what makes a long chain read as one thing
        /// getting louder.
        /// </para>
        /// </summary>
        void Chain(int depth)
        {
            var heat = ChainHeat(depth);
            float y = _lineY + Cell * 2.35f;

            if (_chain == null)
            {
                var host = UIKit.Node("Chain", _fx);
                host.anchoredPosition = new Vector2(0f, y);

                _chainAura = UIKit.Img("Aura", host, Art.Glow(128, 1.7f), Under(1f),
                                       new Vector2(Cell * 7f, Cell * 2.6f));
                _chainAura.raycastTarget = false;

                _chain = UIKit.Titled("Text", host, "", 24, heat, TextAnchor.MiddleCenter,
                                      new Vector2(Span.x, Cell * 1.6f), default, default,
                                      Cell * .07f, Cell * .07f);
            }

            var rt = (RectTransform)_chain.transform.parent;
            rt.anchoredPosition = new Vector2(0f, y);

            // Bigger with depth as well as hotter, so a five reads as more than a two across the
            // room rather than only up close.
            _chain.fontSize = Mathf.RoundToInt(Cell * (.72f + Mathf.Min(depth, 6) * .05f));
            _chain.text = Loc.Format("mode.siege.chain", depth);
            _chain.color = heat;

            var solid = _chain.color;
            solid.a = 1f;
            _chain.color = solid;

            if (_chainAura) _chainAura.color = Under(1f);

            Tween.KillAll(rt);
            Tween.KillAll(_chain);

            Tween.Run(.22f, Ease.OutBack, k =>
            {
                if (rt) rt.localScale = Vector3.one * Mathf.Lerp(.55f, 1f, k);
            }, rt, "pop");

            var banner = _chain;
            var group = rt;
            var aura = _chainAura;

            // Held solid for most of its life and then let go quickly: a banner that starts fading
            // as it arrives is one nobody reads, and this one has a number in it.
            Tween.Run(.95f, Ease.Linear, k =>
            {
                if (!banner || !group) return;

                float fade = k < .62f ? 1f : 1f - (k - .62f) / .38f;

                var lit = banner.color;
                lit.a = fade;
                banner.color = lit;

                if (aura) aura.color = Under(fade);

                group.anchoredPosition = new Vector2(0f, y + Cell * .3f * k);
            }, banner, "fade").OnDone(() =>
            {
                if (group) Destroy(group.gameObject);
                if (_chain == banner) { _chain = null; _chainAura = null; }
            });

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

            yield return base.Ruin();
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
