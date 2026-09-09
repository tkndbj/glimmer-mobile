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
    public sealed partial class SiegeView : ProtoView
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

            /// <summary>
            /// The weaver's lock over it, or null.
            ///
            /// <b>A child of the gem rather than a cell of its own</b>, so it falls with the gem
            /// it is on and nothing has to keep two lists in step — which is the same rule the
            /// board keeps about <c>_webbed</c> travelling through <c>Collapse</c>. Kept once
            /// minted and hidden rather than destroyed, because a field of forty cells locks and
            /// unlocks all run.
            /// </b>
            /// </summary>
            public Image Web;
        }

        sealed class Mob
        {
            public int Id;
            public RectTransform Node;
            public Image Body;
            public Image Shadow;
            public Image Pip;
            public RectTransform Bar;
            public Image Fill;
            public float Height;
            public bool Falling;

            /// <summary>The warlord's, and null for everything else.</summary>
            public bool Boss;

            /// <summary>
            /// Which of the four this is, and so what it throws and how it is drawn.
            ///
            /// <b>It was <c>bool Greater</c>, which was the right shape for exactly two bosses.</b>
            /// A chapter shipped two warlords told apart by their hue and nothing else, and this is
            /// the field that made that inevitable: a bool can only ever answer "the other one",
            /// so every drawing decision downstream was a choice between two. Every one of them is
            /// now a question about a <see cref="SiegeKind"/>, and adding a fifth boss is adding a
            /// branch rather than replacing a flag.
            /// </summary>
            public SiegeKind Kind;

            /// <summary>Whether it is the greatest of the four. Kept for the readouts that scale.</summary>
            public bool Greater => Kind == SiegeKind.Overlord;

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

            /// <summary>
            /// Which rung of the top of the board its <see cref="Crown"/> hangs from.
            ///
            /// <b>Nought until an endless lane sends a pair.</b> A crown is a full-width bar
            /// anchored at one place, so two bosses arriving together drew two of them exactly on
            /// top of each other — invariant 37u's own finding (two readouts overlapping is two
            /// readouts nobody can read), arriving through a door that was safe for as long as a
            /// level could only ever send one. See <see cref="SiegeView.FreeCrown"/>.
            /// </summary>
            public int CrownSlot;
        }

        sealed class Post
        {
            public RectTransform Node;
            public Image Socket;
            public Image Body;

            /// <summary>The badge at its shoulder, tinted by rank. See <c>SiegeView.RankTint</c>.</summary>
            public Image Shield;

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

            /// <summary>Which of <see cref="SiegeLayout.Letters"/> it burns. Its art is keyed on it.</summary>
            public int Colour;

            /// <summary>
            /// The rank its picture is currently drawn at, which is not always the ward's.
            ///
            /// <b>Remembered rather than compared against the sprite</b>, so the one frame a rank
            /// changes on is a frame something can be made to happen on — see
            /// <see cref="SiegeView.Rose"/>. Asking "is the sprite the right one" every frame would
            /// answer the same question and lose the *edge*, which is the only interesting part.
            /// </summary>
            public int Rank = -1;

            /// <summary>The shield on its shoulder, and the number written on it.</summary>
            public RectTransform Crest;
            public Text Tier;
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

        RectTransform _hill, _mobs, _wall, _field, _meters, _fx;

        readonly List<Gem> _gems = new List<Gem>(48);
        readonly List<Mob> _mob = new List<Mob>(24);
        readonly List<Mob> _order = new List<Mob>(24);
        Post[] _posts;
        Text _waveLabel;

        float _hillTop, _hillFoot, _lineY, _gemCentre;
        Vector2 _room;
        int _wave;

        // ------------------------------------------------------------------ aiming
        RectTransform _aim;
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
    }
}
