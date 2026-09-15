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
        /// <para>
        /// <b>They are a ratio and not two shares, which is what makes moving one safe.</b> The
        /// field's height is a fact rather than a share — it is laid out to the width (see
        /// <see cref="MaxGemBand"/>) — so these two only ever divide what is left. The hill's half
        /// went from .44 to .54 after a device reported the enemy ground as too small: on a
        /// 19.5:9 phone that is about seventy points of hill, and it costs the line a tenth of
        /// itself, leaving it just under two cells — which is the bar the render bought.
        /// <b>Check it with <c>Tools/render_siege.py --phone</c> before moving either.</b>
        /// </para>
        /// </summary>
        const float HillBand = .54f, LineBand = .16f;

        /// <summary>
        /// The least the ward line may be, in cells, whatever the ratio above works out to.
        ///
        /// <b>A floor rather than a share, because the line holds furniture and not a picture.</b>
        /// Everything standing on it is sized off <c>Cell</c> — plinth, tube, health bar, rank
        /// badge — so on a short display a band that is only a fraction of what the field leaves
        /// gets squeezed under them, and what a player sees is a tube drawn across a turret's own
        /// chassis (invariant 37y) and a plinth behind the field's plate (37g). One and a half
        /// cells is where the render stops showing either.
        /// </summary>
        const float LineFloor = 1.5f;

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

            /// <summary>
            /// What this gem is carrying.
            ///
            /// <b>The picture that says so is the gem's own face</b> — a lance is a stellated star
            /// and a stormglass a vortex orb, each cut in all four gem colours (<c>CharmFace</c>),
            /// so a charmed cell is a <em>different stone</em> rather than one of the four with a
            /// glyph printed on it. That is a correction: the glyph was the first cut and the
            /// owner's verdict on it was that the charms were the existing gems with an icon put
            /// on them. See <c>SiegeMode.Cast</c> for the whole argument.
            /// </summary>
            public SiegeCharm Charm;

            /// <summary>
            /// The halo behind it, or null.
            ///
            /// <b>A child of the gem for <see cref="Web"/>'s reason</b>: it falls with the gem,
            /// swaps with it and is destroyed with it, so nothing has to keep a second list of
            /// forty cells in step with this one.
            ///
            /// <para>
            /// <b>It survived the face going in because it is the reading at a distance.</b> A
            /// silhouette is what separates a charm from the gem beside it; a halo is what makes
            /// the eye go there at all on a board that also has a hill walking down it (invariant
            /// 37f, asked of a gem). Every charm wears one, the prism included.
            /// </para>
            /// </summary>
            public Image Ring;
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

            /// <summary>What it stands and walks in, and what it throws with.</summary>
            ///
            /// <b>One reel for standing and walking, which is a fact about the cast rather than a
            /// saving.</b> Every body in this mode is a top-down insect whose legs and wings cycle
            /// <em>in place</em> - measured at 2.3 pixels of drift across a 137-pixel frame - so
            /// the picture of one standing and the picture of one walking are the same picture,
            /// and the board is the only thing that moves it. A boss that really strode would want
            /// a walk reel and something choosing between them every frame, which is what this
            /// carried while the cast were bipeds (invariant 37u, reported in one word:
            /// <em>floating</em>).
            public Sprite[] Idle, Casting;

            /// <summary>
            /// What a <b>boss</b> walks on in, and <b>null</b> for everything that walks for its
            /// whole life.
            ///
            /// <para>
            /// <b>A boss is the only body on this hill that ever stops, so it is the only one for
            /// which "standing" and "walking" are two pictures.</b> <see cref="Idle"/>'s note
            /// above is about an insect, whose legs cycle in place — the picture of one standing
            /// and one walking really are the same picture. A boss rendered out of 3D is the case
            /// that note says would want two, arriving: it walks to
            /// <c>SiegeTuning.HoldOf</c> and holds the middle of the hill from there (37t), and
            /// the reel it shipped with was the <em>stand</em>, so for the three seconds of its
            /// entrance a standing figure was translated down the hill. Reported from play as
            /// sliding rather than walking, which is what it was.
            /// </para>
            /// <para>
            /// <b>Null is an answer and not a failure</b>, exactly as it is for
            /// <see cref="Swinging"/>: the four bosses cut from 2D packs have one reel that is
            /// their walk and their stand together (<c>make_siege_art.boss_reels</c>), so they
            /// keep every frame of the behaviour they have always had. Asking for the reel and
            /// falling back to <see cref="Idle"/> is also what keeps a boss whose walk failed to
            /// load off an <c>Image</c> with no sprite, which is a white rectangle three cells
            /// tall rather than a blank (invariant 7b).
            /// </para>
            /// </summary>
            public Sprite[] Walking;

            /// <summary>
            /// What it swings at the ward line, and <b>null for every cast whose pack drew no
            /// attack</b> — which is the insects and the brood, so nothing about either of them
            /// changes.
            ///
            /// <b>Its frame is bigger than the walk's and its body is not</b>, which is
            /// <see cref="SiegeView.Wear"/>'s whole job: a swing throws the weapon far outside the
            /// box the body walks in, and paying for that on the walk reel would draw every raider
            /// in the chapter two-thirds size for the whole run.
            /// </summary>
            public Sprite[] Swinging;

            /// <summary>Which of them its body is wearing now. See <see cref="SiegeView.Wear"/>.</summary>
            public Sprite[] Playing;

            /// <summary>The light it gathers before a spell leaves. Only a warlord has one.</summary>
            public Image Charge;

            /// <summary>
            /// Seconds until this boss crackles again. See <c>SiegeView.Ambient</c>.
            ///
            /// <b>A countdown per boss rather than one clock for the hill</b>, because a pair of
            /// them striking on the same frame reads as one flash rather than as two creatures —
            /// and it is jittered on every reset for the same reason.
            /// </summary>
            public float Crackle;

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

            /// <summary>
            /// The demand light: how much of what is on the hill this turret is the answer to.
            ///
            /// <b>It sits behind the tube rather than on the chassis</b>, because the tube is
            /// already the one thing on this line a player's eye passes on its way back to the
            /// gems — see <c>SiegeView.Wanted</c>.
            /// </summary>
            public Image Want;

            /// <summary>
            /// The overcharge key: the glyph on the turret's own chassis, live only while a charge
            /// is banked.
            ///
            /// <para>
            /// <b>On the turret rather than on the fuel bar, which is where it started.</b> The bar
            /// is a *meter* — it says how much, continuously, and it is already carrying the demand
            /// light — so a control living on it was a button hidden inside a readout. The chassis
            /// is the one part of a ward nothing else uses (the badge has the shoulder, the health
            /// bar is above, the bar below) and it is what a finger goes for when it means "this
            /// turret".
            /// </para>
            /// </summary>
            public Image Dump;

            /// <summary>The light behind the glyph, in the ward's own colour.</summary>
            public Image Halo;

            /// <summary>How many overcharges are banked, drawn only when it is more than one.</summary>
            public Text Held;

            /// <summary>
            /// The disc behind that number.
            ///
            /// <b>A bare digit over a saturated chassis is a digit nobody reads</b>, which is the
            /// same finding the glyph itself cost: a player who had banked two charges asked
            /// whether they stacked at all. A count needs its own ground.
            /// </summary>
            public Image Pip;
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

        RectTransform _hill, _mobs, _fuseLayer, _cogLayer, _wall, _field, _meters, _fx;

        /// <summary>The effect layer that is clipped to the board — see <c>SiegeView.Build</c>.</summary>
        RectTransform _sky;

        /// <summary>
        /// The damage figures, above every effect on this board — see <c>SiegeView.Build</c> for
        /// why they are not on <c>_fx</c> and <c>SiegeView.Number</c> for what they are.
        /// </summary>
        RectTransform _figures;

        readonly List<Gem> _gems = new List<Gem>(48);
        readonly List<Mob> _mob = new List<Mob>(24);
        readonly List<Mob> _order = new List<Mob>(24);
        Post[] _posts;
        Text _waveLabel;

        float _hillTop, _hillFoot, _lineY, _gemCentre;

        /// <summary>
        /// The ward line's share of the board, as <c>Compose</c> derived it — not
        /// <see cref="LineBand"/>, which is one half of the ratio that produced it.
        /// </summary>
        float _lineBand;
        Vector2 _room;
        int _wave;

        // ------------------------------------------------------------------ aiming
        RectTransform _aim;
        UtilityItem _arming;
        readonly List<SiegeStrike> _strikes = new List<SiegeStrike>(16);

        /// <summary>
        /// Raiders a storm has claimed and not yet dropped a bolt on.
        ///
        /// <para>
        /// <b>A storm resolves in the rules in one instant and is drawn over a second or more</b>
        /// (see <c>Stormcall</c>), so between the two there is a stretch where the model holds a
        /// dozen dead raiders that are still standing on the screen on purpose. <c>Reap</c> runs
        /// every frame and takes down the widget of anything dead — correctly, and it would take
        /// the whole hill down one frame in, leaving the rest of the bolts falling on empty
        /// ground and the staggering pointless.
        /// </para>
        /// <para>
        /// So the storm says which raiders are its, and <c>Reap</c> leaves them alone until it
        /// has had its bolt. Cleared by <c>Compose</c>, because a board dealt again mid-storm
        /// must not leave a claim standing over widgets that no longer exist.
        /// </para>
        /// </summary>
        readonly List<int> _striking = new List<int>(16);

        /// <summary>
        /// Raiders a <b>stormglass</b>'s volley has claimed and not yet landed a bolt on.
        ///
        /// <para>
        /// <b>The same problem as <see cref="_striking"/> and a list of its own rather than a
        /// share of it.</b> Both are "the model killed a dozen things in one instant and the
        /// drawing runs on for a second and a half", and both need <c>Reap</c> to leave the bodies
        /// standing until their bolt arrives — but a stormcall clears its whole claim when its
        /// sequence ends, so sharing one list would let either of them drop the other's corpses
        /// mid-flight. Two lists cost a second <c>Contains</c> per mob per frame and cannot
        /// interfere.
        /// </para>
        /// <para>
        /// Cleared by <c>Compose</c> with the other, because a board dealt again mid-volley must
        /// not leave a claim standing over widgets that no longer exist.
        /// </para>
        /// </summary>
        readonly List<int> _volleying = new List<int>(16);

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

        /// <summary>Raised once, the first time any tube on the line fills.</summary>
        public System.Action Brimmed { get; set; }

        /// <summary>Raised once, the first time a felled raider leaves a cog on the hill.</summary>
        public System.Action Salvaged { get; set; }

        /// <summary>
        /// Raised once, the first time a felled bomber leaves a live bomb on the hill.
        ///
        /// <para>
        /// <b>It replaced a hook that fired when a bomber <em>walked on</em>, and the difference
        /// is the whole lesson.</b> What the tip has to say is "tap this" — so it was arriving
        /// while the thing to tap did not exist, ringing the raider instead and being long gone by
        /// the time a bomb landed. Reported from a device as the tip highlighting the wrong thing,
        /// and it is invariant 20m's rule about a payoff being something the player <em>made</em>,
        /// asked of a lesson: the bomb is theirs because they killed for it, and the panel belongs
        /// over it.
        /// </para>
        /// <para>
        /// <b>The view raises it and the screen decides whether to teach</b>, because whether a
        /// player has seen a lesson before is a fact about the save (<c>TipLedger</c>) and a board
        /// has no business reading one. Raised on every drop rather than latched here, which is
        /// <see cref="Salvaged"/>'s own hard-won rule: <c>RunLessons.Teach</c> refuses a lesson
        /// already seen or mid-chain, and a latch that decided it first threw the tip away for ever
        /// when it happened to arrive while another panel was up.
        /// </para>
        /// </summary>
        public System.Action Bombed { get; set; }

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

                // Arming is the player doing something, so the idle nudge starts over — and a
                // targeting layer going up over a ringed gem would be two things at once asking
                // to be looked at. See `SiegeView.Hint`.
                Stir();

                Aiming();
            }
        }
    }
}
