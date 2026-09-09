using System.Collections.Generic;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// One thing a player has to be taught once.
    ///
    /// <para>
    /// The id is permanent and travels in the save file, exactly like a level id: it
    /// records that a particular person has already been shown a particular idea, and
    /// renaming one would re-teach the whole player base something they know. Add
    /// freely, never rename, never reuse.
    /// </para>
    /// <para>
    /// Its strings are derived from the id — <c>ui.tip.&lt;id&gt;.title</c> and
    /// <c>.body</c> — for the same reason a level's are: anything holding a mechanic
    /// can name it without a lookup table to keep in step.
    /// </para>
    /// </summary>
    public readonly struct Mechanic
    {
        public readonly string Id;

        Mechanic(string id) => Id = id;

        public static readonly Mechanic FragileConduit = new Mechanic("fragile");
        public static readonly Mechanic MoveBudget = new Mechanic("moves");
        public static readonly Mechanic RootedTile = new Mechanic("rooted");

        /// <summary>
        /// A critter that asks for no colour at all, and wakes to whatever light arrives.
        ///
        /// <para>
        /// <b>It is taught because the ring is read as a demand.</b> Every other sleeping
        /// critter wears the colour it is waiting for, so a player learns within a glade or
        /// two that the ring on a critter is an instruction — and an unfussy one wears all
        /// three channels at once (<c>Art.PrismRing</c>), which under that reading says
        /// <em>bring me white</em>. That is the opposite of what it means, and it is the
        /// crossing's fault rather than the taproot's: the player does not fail to learn a
        /// rule, they conclude a wrong one and route light they never needed to.
        /// </para>
        /// <para>
        /// <b>Only where the board can show it.</b> The lesson is a contrast — this one asks
        /// for nothing, those ones ask for something — so it is reported only on a board that
        /// also holds a fussy critter. A glade where every critter is unfussy has no colour
        /// rule on it yet, so there is nothing for "any" to be the absence of, and a lesson is
        /// shown once in a player's life: spent on the opening glade, where all three critters
        /// take any light and no critter has ever asked for a colour, it could never be shown
        /// on the first board that actually mixes the two.
        /// </para>
        /// </summary>
        public static readonly Mechanic AnyLight = new Mechanic("anylight");

        /// <summary>Two heart colours, and a critter that wants them blended.</summary>
        public static readonly Mechanic ColourMixing = new Mechanic("mixing");

        // "duskcap" is a **retired lesson id and must never be reused.** A lesson id
        // travels in the save (`tipsSeen`) exactly as a level id does, so re-pointing one
        // at a different rule would tell a player they have already been taught something
        // they have never seen. Same rule as `weave_fill`, and for the same reason: the
        // mechanic it named was removed because no board could demonstrate it — a glade
        // with every critter awake and a duskcap lit looks precisely like a finished glade
        // that refuses to settle, which is the one thing a board must never look like.

        /// <summary>Conduits sharing a taproot, which turn as one however far apart they are.</summary>
        public static readonly Mechanic BoundConduit = new Mechanic("bound");

        /// <summary>A conduit carrying two flows that pass through one another and never meet.</summary>
        public static readonly Mechanic Crossing = new Mechanic("crossing");

        /// <summary>A conduit with two of its four ways thorned shut, and one tap swaps which.</summary>
        public static readonly Mechanic Briar = new Mechanic("briar");

        // ------------------------------------------------------------- screens
        // Five things a glade board cannot teach, because they are not on one. They ride this type
        // rather than a parallel one because everything about a lesson is already here and
        // already stored: the id is permanent, the strings derive from it, and TipLedger is a
        // union-joined set that reaches the cloud with no new field. A second "thing to teach
        // once" type would be a second ledger, a second merge rule and a second save field,
        // for two strings and a ring.
        //
        // They are deliberately absent from TeachingOrder, which is the *board* scan's queue —
        // see the remarks there. A screen tip is raised by the screen that owns it, because
        // nothing about a board implies the player has opened the Grovement, and nothing about
        // a glade implies they have ever met a second mode.

        // ----------------------------------------------------- retired: Budburst
        // **Six retired lesson ids that must never be reused: `bud_chain`, `bud_cocoon`,
        // `bud_satchel`, `bud_graft`, `bud_bolt` and `bud_sun`**, alongside the eight this mode
        // had already spent (`bud_runner`, `bud_gust`, `bud_firefly`, `bud_puff`, `bud_hive`,
        // `bud_wood`) and the ten Lightweave and Ripplewake spent before it (`weave_join`,
        // `weave_bead`, `weave_ink`, `weave_hedge`, `weave_fill`, `ripple_meet`,
        // `ripple_satchel`, `ripple_reed`, `ripple_deep`, `ripple_lily`). Budburst was withdrawn
        // with Hollowmarch and Emberforge, having shipped two chapters and been played on a
        // device. A lesson id travels in the save (`tipsSeen`) exactly as a level id does, so
        // re-pointing one at a rule it never described would tell a player they have already been
        // shown something they never saw.

        /// <summary>
        /// Lightfall's verb: a mote dropped onto another adds its colour rather than matching
        /// it, and one holding all three bursts.
        ///
        /// <para>
        /// <b>The one rule of this mode a board genuinely cannot show</b>, and it has to be told
        /// before the first drop because the mistake it prevents is the whole of the game.
        /// Everything else here is a matching puzzle - four chapters of joining like to like,
        /// and a mode whose crystals are dragged to critters of their own colour - so a well of
        /// coloured circles reads as "put the reds together" to anybody who has played this game
        /// at all. It is the opposite: a red dropped on a red does nothing except make the stack
        /// taller. One sentence saves a player from spending a whole level being wrong in a way
        /// that looks like being right.
        /// </para>
        /// <para>
        /// What the board <em>can</em> show, and does, is the rest of it: a mote one channel
        /// short wears a halo in the colour it is waiting for, the ghost under a thumb says
        /// whether this drop enriches or heightens, and a burst visibly throws its light into
        /// the motes beside it. So the lesson is two sentences and a ring rather than a
        /// tutorial.
        /// </para>
        /// </summary>
        public static readonly Mechanic FallCook = new Mechanic("fall_cook");

        /// <summary>
        /// A well's supply: the motes it is dealt, and that spending one is permanent.
        ///
        /// <para>
        /// <b>Separate from <see cref="MoveBudget"/>, for the
        /// reason those two are separate from each other.</b> All three are a pot that empties
        /// and ends a run, and everything a player has to be told is in the half that differs. A
        /// glade's budget counts committed turns and hands one back for every undo, without
        /// limit, so exploring a board there costs nothing. A well has no undo at all: a dropped
        /// mote is gone, and a wrong one is gone twice over because it also cost a row of
        /// headroom. Somebody who learned the glade's rule and was never taught this one would
        /// tap about to see what happens, which on this board is how you lose.
        /// </para>
        /// <para>
        /// Only on a well that can actually run dry. The first level of the chapter is authored
        /// without a budget - exactly as the first glade in the game is - and a lesson shown
        /// over a meter that is not there is one that can never be shown again.
        /// </para>
        /// </summary>
        public static readonly Mechanic FallSupply = new Mechanic("fall_supply");

        /// <summary>
        /// The brim: the line at the top of a well that a mote may not come to rest above.
        ///
        /// <para>
        /// <b>Half of this the board shows and half of it it cannot.</b> The line is drawn, it
        /// reddens as the stack climbs into it, and the ghost under a thumb turns red when the
        /// drop would land there - so "this is dangerous" is on the board. What is not is that
        /// it is <em>fatal</em> rather than merely bad, which is the difference between a player
        /// who avoids the brim and one who finds out about it once.
        /// </para>
        /// <para>
        /// Taught only on a well where the brim is in reach. On a board with six rows of
        /// clearance it is scenery, and a modal about scenery spends a lesson that cannot be
        /// spent twice.
        /// </para>
        /// </summary>
        public static readonly Mechanic FallBrim = new Mechanic("fall_brim");

        /// <summary>
        /// The lens: a bead of glass that fills with light and then fires.
        ///
        /// <para>
        /// <b>The board shows the filling and cannot show the rule behind it.</b> Three pips on
        /// the glass say what it holds and what it still wants, and the shot itself is the
        /// loudest thing in the mode — so once a player has seen one go off they understand it
        /// completely. What no board can say is the part that has to be known <em>before</em>
        /// the first one: that a lens takes light from a burst beside it rather than from a
        /// drop. Every other cell here is filled by dropping on it, so the natural thing to do
        /// with a half-charged lens is to drop the colour it wants straight onto it, and that
        /// costs a mote, costs a row of headroom, and does nothing at all.
        /// </para>
        /// <para>
        /// So it is two sentences and a ring, and only what the board genuinely cannot say: that
        /// glass is charged by light that has already travelled, and that a full one fires along
        /// every axis. How far a shot gets, what stops it, and which colour is still missing the
        /// board shows perfectly well.
        /// </para>
        /// <para>
        /// Only on a well that stands one. The Deep Well's ten do not, and a lesson shown over a
        /// board with no glass on it is one that can never be shown again.
        /// </para>
        /// </summary>
        public static readonly Mechanic FallLens = new Mechanic("fall_lens");

        /// <summary>
        /// The whorl: a mouth in the well that draws the motes either side of it together and
        /// mixes them into one.
        ///
        /// <para>
        /// <b>The board shows what it does and cannot show what it is for.</b> Watch one turn and
        /// the mechanism is obvious — two lights slide in and one comes out. What no board can
        /// state is the reason a player should care, and it is the one fact that makes the
        /// mechanic worth having: <em>every other rule in this mode adds a colour to a mote</em>,
        /// so a cyan and a red are two separate drops away from bursting. Put them either side of
        /// a whorl and they are none.
        /// </para>
        /// <para>
        /// The second sentence is the one that costs a run if it is left to be discovered:
        /// <b>anything</b> opens a whorl — a burst beside it, a lens beam, or a drop straight
        /// onto it — and what it gives back is whatever is standing beside it <em>at that
        /// moment</em>. A player who reads it as glass will try to charge it and waste drops; a
        /// player who does not know it fires on any touch will lose the pair they spent four
        /// drops arranging to a chain that reached it early.
        /// </para>
        /// <para>
        /// <b>It replaced two mechanics that had to be withdrawn, and that is why this one is
        /// shaped the way it is.</b> The mirror turned a lens's beam ninety degrees, so it had no
        /// event of its own and did nothing at all on a board with no glass. The wick washed one
        /// authored colour into its four neighbours, which is a burst with the colour changed —
        /// its colour was the author's, its trigger was free, and there was no point at which the
        /// player decided anything. Both were reported as the lens again, correctly. A whorl is
        /// bought with <em>position</em> rather than with drops, which is the one currency this
        /// mode had never charged in.
        /// </para>
        /// <para>
        /// Only on a well that stands one. Two of Lightfall's three chapters do not, and a lesson
        /// shown over a board with no whorl on it is one that can never be shown again.
        /// </para>
        /// </summary>
        public static readonly Mechanic FallWhorl = new Mechanic("fall_whorl");

        /// <summary>
        /// <b>Retired, and the id must never be reused.</b> <c>fall_wick</c> named the mechanic
        /// the whorl replaced. A lesson id travels in <c>tipsSeen</c> exactly as a level id
        /// travels in the ledger, so re-pointing one at a rule it never described would tell a
        /// player they have already been shown something they never saw.
        /// </summary>
        public const string RetiredWick = "fall_wick";

        /// <summary>
        /// The allowance a prototype board is dealt: how many moves it gives you, and that every
        /// one of them is permanent.
        ///
        /// <para>
        /// <b>One lesson for however many modes share the shape, because it really is one
        /// rule.</b> A pull takes exactly one from the meter and cannot be taken back — which is
        /// the half a player arriving from four chapters of turning conduits has to be told,
        /// since a glade hands a turn back for every undo and so rewards tapping about to see
        /// what happens. Separate from <see cref="MoveBudget"/> and <see cref="FallSupply"/> for
        /// the reason those two are separate from each other:
        /// what matters is not that a pot empties, it is what a wrong move costs before it does.
        /// It was written for five modes and kept its wording when four of them went, because
        /// the id is spent either way and the sentence was never about any one of them.
        /// </para>
        /// <para>
        /// Only on a board that can actually run out. The opening board of such a mode is
        /// authored without an allowance — as the first glade, the first well and the first
        /// thicket are (invariant 24) — and a lesson shown over a meter that is not there is one
        /// that can never be shown again.
        /// </para>
        /// </summary>
        public static readonly Mechanic ProtoMoves = new Mechanic("proto_moves");

        // -------------------------------------- retired: Hollowmarch and Emberforge
        // **Four retired lesson ids that must never be reused: `march_fire`, `march_spark`,
        // `ember_fuse` and `ember_star`.** Both modes were withdrawn with Budburst, and both were
        // played on a device, so a real save may hold a `tipsSeen` entry against any of them.

        /// <summary>
        /// Prismvale's verb: drag a gem onto its neighbour and the two change places.
        ///
        /// <para>
        /// One sentence, and it is about what a lantern does rather than about dragging. A
        /// player arriving from four chapters of glades already knows that a critter wants
        /// light; what no board can show before it has happened once is where the light comes
        /// <em>from</em> - a lantern feeds only the gems of its own colour that are touching it,
        /// and that colour runs on through every gem of the same colour beside it.
        /// </para>
        /// </summary>
        public static readonly Mechanic PrismDrag = new Mechanic("prism_drag");

        /// <summary>
        /// That a vein can be <em>broken</em>, and that a gem is never spent.
        ///
        /// <para>
        /// Deliberately two halves of one sentence. A player who does not know the light is read off the
        /// arrangement will pull a gem out of a working vein and think the game took it away;
        /// one who thinks gems are spent will hoard them. Both halves are the same fact - the
        /// board never changes, only where things stand - and it is the one thing that makes a
        /// careless swap cost something, so both have to arrive at once.
        /// </para>
        /// </summary>
        public static readonly Mechanic PrismVein = new Mechanic("prism_vein");

        /// <summary>
        /// Thornwatch's verb: a match is not worth anything by itself, it is worth the
        /// <em>colour</em> it was.
        ///
        /// <para>
        /// Nothing on the field is a goal, which is the one thing a player arriving from any other
        /// jewel board in this game will get wrong. They will look for the biggest match; what
        /// matters is which ward it feeds, and that a bolt is worth double against a raider of its
        /// own colour. A board can show the second half — the bolts visibly go for their own
        /// colour — and cannot show the first, because a match that feeds a full ward looks
        /// exactly like a match that feeds an empty one.
        /// </para>
        /// </summary>
        public static readonly Mechanic SiegeFuel = new Mechanic("siege_fuel");

        /// <summary>
        /// That fuel <em>fades</em>, and that the ward line is the run.
        ///
        /// <para>
        /// Deliberately two halves of one sentence, exactly as <see cref="PrismVein"/> is. A
        /// player who does not know fuel fades will bank a colour
        /// through a quiet moment and find it gone; one who does not know the line is the fail
        /// state will let a wave through to keep matching. Both halves are the same fact — what a
        /// colour is worth depends entirely on when it is spent — and it is the only thing that
        /// makes an unhurried match cost anything, so both have to arrive at once.
        /// </para>
        /// </summary>
        public static readonly Mechanic SiegeLine = new Mechanic("siege_line");

        /// <summary>
        /// The cog: that it is destroyed by the run <em>beside</em> it, and that the colour of
        /// that run decides which turret goes up.
        ///
        /// <para>
        /// Deliberately two halves of one sentence, exactly as <see cref="SiegeLine"/> is. A player
        /// who does not know a cog is taken by an adjacent match will spend the run trying to line
        /// three of them up; one who does not know the colour decides the turret will take whatever
        /// match is nearest and upgrade a ward at random. Both halves are the same fact — a cog is
        /// the mode's own question asked about the <em>line</em> instead of about the hill — and
        /// only the second half can be wrong, which is what makes it a decision at all.
        /// </para>
        /// <para>
        /// <b>Declared per board rather than per mode</b> (see <c>SiegeScreen.Lessons</c>): the
        /// first rung of the chapter deals no cogs, so a lesson about them there would be a lesson
        /// about something that is not on the screen — and a lesson shown once can never be shown
        /// again.
        /// </para>
        /// </summary>
        public static readonly Mechanic SiegeCog = new Mechanic("siege_cog");

        // **Two retired lesson ids that must never be reused: `kindle_join` and `kindle_cross`.**
        // Kindlewake was withdrawn by the owner after play and Prismvale took its slot. A lesson
        // id travels in the save (`tipsSeen`) exactly as a level id travels in the ledger, so
        // re-pointing one at a rule it never described would tell a player they have already been
        // shown something they never saw.

        // **Two retired lesson ids that must never be reused: `quarry_flick` and
        // `quarry_armour`.** The Iron Quarry was withdrawn by the owner and Hollowmarch took its
        // slot. A lesson id travels in the save (`tipsSeen`) exactly as a level id travels in the
        // ledger, so re-pointing one at a rule it never described would tell a player they have
        // already been shown something they never saw. Unlike the eleven below, the quarry was
        // never played on a device - but the ids are spent all the same, because "no save can
        // hold this" is a claim about every device in the world and the cost of being wrong is
        // silent (invariant 26h's own precedent for keeping a level id and moving the string
        // above it).

        // **Six retired lesson ids that must never be reused: `topple_roll`, `topple_burrow`,
        // `nova_drag`, `nova_swap`, `nova_forge` and `nova_armour`.** Toppleglen and Nova Raid
        // were both withdrawn by the owner after play on 2026-09-06 and the Iron Quarry took
        // their slot. A lesson id travels in the save (`tipsSeen`) exactly as a level id travels
        // in the ledger, so re-pointing one at a rule it never described would tell a player
        // they have already been shown something they never saw - and both modes were played on
        // a device, which is precisely when that stops being hypothetical.

        // **Two more retired lesson ids: `orbit_launch` and `orbit_pod`**, Deep Orbit's,
        // withdrawn with the mode on 2026-09-06 after being played on a device.

        // **Eight retired lesson ids that must never be reused: `nectar_pour`, `nectar_hollow`,
        // `ribbon_draw`, `ribbon_sink`, `fling_flick`, `fling_catch`, `warren_cut` and
        // `warren_carry`.** Nectarrun, Ribbonfall, Seedfling and Warrenwake were four of the five
        // prototypes built into Groovekeeper's slot to be judged by playing them (invariant 29);
        // Toppleglen is the one the owner kept. A lesson id travels in the save (`tipsSeen`)
        // exactly as a level id travels in the ledger, so re-pointing one at a rule it never
        // described would tell a player they have already been shown something they never saw —
        // and all four modes were played on a device, which is precisely when that stops being
        // hypothetical. `topple_roll` and `topple_burrow` are the two that survive.

        /// <summary>
        /// That there is more than one way to play, and where the switch between them is.
        ///
        /// <para>
        /// The one lesson here about a <em>control</em> rather than about a rule, and it is the
        /// shape of that control that earns it. It is a closed drop-down (see <c>ModeSwitch</c>)
        /// naming only the mode the player is already in, so nothing about it says there is
        /// anything inside it — and every other mode is reachable through it and through nothing
        /// else, so a player who never presses it never learns the other half of the game exists.
        /// </para>
        /// <para>
        /// It lived in the map's bottom corner, which made the case stronger and not weaker: a
        /// pill in the corner most thumbs rest on, on a screen whose whole job is a vertical
        /// chain of glades running the other way. It is now under the chapter plaque, where the
        /// eye already is. That is a better control and still not a self-evident one, which is
        /// why the lesson stays.
        /// </para>
        /// <para>
        /// It is raised by the map rather than by a board for the reason the two grove lessons
        /// are raised by the grove: nothing about a puzzle implies the player has ever seen the
        /// switcher, and a modal about a menu on another screen is a modal about nothing the
        /// player can look at. It is therefore deliberately absent from
        /// <see cref="TeachingOrder"/>.
        /// </para>
        /// <para>
        /// <b>Taught only while the switcher is actually drawn.</b> <c>ModeSwitch</c> builds
        /// nothing when the catalog holds one mode, so a client whose content has not caught up
        /// — a rolled-back build, an undownloaded drop, or simply the day before a second mode
        /// ships — must not spend this lesson on a control that is not there. The ledger is a
        /// once-in-a-lifetime record, so a tip shown over nothing is a tip that can never be
        /// shown again.
        /// </para>
        /// </summary>
        public static readonly Mechanic ModeSwitch = new Mechanic("mode_switch");

        /// <summary>
        /// That the button under a won glade's reward is a wheel, and that spinning it is free.
        ///
        /// <para>
        /// <b>A lesson about a control, like <see cref="ModeSwitch"/>, and it earns one for the
        /// same reason.</b> The victory panel is the loudest moment in the game and the button
        /// arrives at the end of it, under a reward the player is already reading, on a screen
        /// whose whole purpose is a large green NEXT. A control in that position is not
        /// discovered — it is scrolled past. The wheel is the game's most generous offer and
        /// most players would never learn it exists.
        /// </para>
        /// <para>
        /// It is raised by the victory panel rather than by a board, for the reason the grove's
        /// lessons are raised by the grove: nothing about a puzzle implies the player has an
        /// offer waiting, and a modal about a button on another screen is a modal about nothing
        /// they can look at. It is therefore deliberately absent from
        /// <see cref="TeachingOrder"/>.
        /// </para>
        /// <para>
        /// <b>Taught only while the button is actually drawn</b>, which is <see cref="ModeSwitch"/>'s
        /// rule and not a detail: the offer is withheld on a cooldown, at a spent allowance and
        /// with no account, and the ledger is a once-in-a-lifetime record — so a tip shown over
        /// a corner with nothing in it is a tip that can never be shown again.
        /// </para>
        /// </summary>
        public static readonly Mechanic LuckySpin = new Mechanic("lucky_spin");

        /// <summary>What the Grovement is, shown once on the player's first visit.</summary>
        public static readonly Mechanic Grove = new Mechanic("grove");

        /// <summary>Where the things a grove is built from are bought.</summary>
        public static readonly Mechanic GroveShop = new Mechanic("grove_shop");

        /// <summary>
        /// Teaching order, most disruptive first.
        ///
        /// Only one tip is ever shown on entering a glade — two modal lessons before a
        /// player has touched anything is a tutorial, not a hint. When a glade brings
        /// several ideas at once this decides which gets the moment, and the rest wait
        /// for a later glade that has them.
        ///
        /// A glade may teach more than one thing; they are shown in this order, one
        /// after another, rather than the rest waiting for a later glade that happens
        /// to repeat them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A crossing goes first of the three because it can be <em>misread</em>, which is
        /// worse than not knowing: a tile with four arms is a crossroads everywhere else in
        /// this game, so a player who has not been told does not discover a new rule, they
        /// conclude the board is broken. A taproot goes last for the opposite reason — it
        /// announces itself the first time it is tapped, because two tiles visibly move.
        /// </para>
        /// <para>
        /// A briar sits directly after the crossing, and for the same reason one notch weaker.
        /// It is the other tile here that wears four arms and is not a crossroads, so it is
        /// misread in exactly the way a crossing is — but a briar shows its own rule, because
        /// the thorns are drawn across the ways they have closed and the light stops at them
        /// while the player watches. What it still cannot show is that the thorns *move*, and
        /// that is what the lesson is for.
        /// </para>
        /// </remarks>
        public static readonly Mechanic[] TeachingOrder =
        {
            FragileConduit, MoveBudget, RootedTile, AnyLight, ColourMixing, Crossing, Briar,
            BoundConduit,
        };

        /// <summary>
        /// Every lesson that exists, board and screen alike.
        ///
        /// <para>
        /// It is what the build gate walks to prove each one has its two strings, and that is
        /// the whole reason it exists separately from <see cref="TeachingOrder"/>: a mechanic
        /// added without them compiles, validates and ships, and the first player to reach it
        /// reads <c>ui.tip.grove.title</c> off the screen. That check used to walk the
        /// teaching order, which was the same list until a lesson appeared that no board can
        /// bring — after which the order would have quietly stopped being the set of
        /// everything, and the check with it.
        /// </para>
        /// </summary>
        public static readonly Mechanic[] All =
        {
            FragileConduit, MoveBudget, RootedTile, AnyLight, ColourMixing, Crossing, Briar,
            BoundConduit,
            FallCook, FallSupply, FallBrim, FallLens, FallWhorl,
            ProtoMoves, PrismDrag, PrismVein,
            SiegeFuel, SiegeLine, SiegeCog,
            ModeSwitch, LuckySpin, Grove,
            GroveShop,
        };

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public string TitleKey => "ui.tip." + Id + ".title";
        public string BodyKey => "ui.tip." + Id + ".body";

        public bool Equals(Mechanic other) => string.Equals(Id, other.Id, System.StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is Mechanic m && Equals(m);
        public override int GetHashCode() => Id == null ? 0 : Id.GetHashCode();
        public override string ToString() => Id;
    }

    /// <summary>Where a mechanic can be pointed at on the board. -1 when it has no home.</summary>
    public readonly struct MechanicSighting
    {
        static readonly int[] Alone = new int[0];

        public readonly Mechanic Mechanic;

        /// <summary>The cell to ring, or -1 for a rule that lives off the board.</summary>
        public readonly int CellIndex;

        /// <summary>
        /// The other cells this rule cannot be seen without, in reading order. Empty for
        /// almost every mechanic, because almost every one of them is a fact about a single
        /// tile.
        ///
        /// <para>
        /// Blending is the exception and is the reason this exists: a ring round the gold
        /// critter alone shows the <em>question</em> and none of the answer, so a first-timer
        /// is told two hearts blend while being shown neither of them. The hearts belong to
        /// the lesson exactly as much as the critter does, and which hearts they are is a fact
        /// about the board — derived here rather than authored, so a chapter shipped a year
        /// from now points at its own.
        /// </para>
        /// </summary>
        public readonly int[] Alongside;

        public MechanicSighting(Mechanic mechanic, int cellIndex, int[] alongside = null)
        {
            Mechanic = mechanic;
            CellIndex = cellIndex;
            Alongside = alongside ?? Alone;
        }

        public bool HasCell => CellIndex >= 0;
    }

    /// <summary>
    /// Reads a board and reports which ideas it contains.
    ///
    /// Derived from the board rather than declared per level, which is the whole point:
    /// a chapter shipped a year from now that happens to use brittle conduits gets its
    /// tip with no authoring, no list to update and nothing to forget. It also means a
    /// tip can never point at a mechanic a level does not actually have.
    ///
    /// It reads a built <see cref="Puzzle"/> rather than a definition so it costs
    /// nothing extra — the board is already parsed by the time anybody asks.
    /// </summary>
    public static class MechanicScan
    {
        public static List<MechanicSighting> InBoard(Puzzle board)
        {
            var found = new List<MechanicSighting>();
            if (board == null) return found;

            int fragile = -1, rooted = -1, blended = -1, bound = -1, crossing = -1;
            int briar = -1;

            // The unfussy critter to ring, and whether anything on this board is fussy —
            // without which there is no contrast to teach. See Mechanic.AnyLight.
            int unfussy = -1;
            bool fussy = false;

            for (int i = 0; i < board.C.Length; i++)
            {
                var cell = board.C[i];

                if (cell.fragile > 0 && fragile < 0) fragile = i;
                if (cell.locked && rooted < 0) rooted = i;
                if (cell.kind == Kind.Crossing && crossing < 0) crossing = i;
                if (cell.kind == Kind.Briar && briar < 0) briar = i;

                // Asked of the board rather than of the cell, because a rune only one
                // conduit carries binds nothing — the validator refuses that level, and
                // pointing a lesson at it would teach a rule the board does not follow.
                if (bound < 0 && board.IsBound(i)) bound = i;

                // A critter asking for more than one channel is the only proof that
                // blending is actually required here — two heart colours on their own
                // may just as well mean "keep these apart".
                if (cell.kind == Kind.Lamp && cell.colour != 0 &&
                    (cell.colour & (cell.colour - 1)) != 0 && blended < 0) blended = i;

                if (cell.kind == Kind.Lamp)
                {
                    if (cell.colour == Energy.Any) { if (unfussy < 0) unfussy = i; }
                    else fussy = true;
                }
            }

            if (fragile >= 0) found.Add(new MechanicSighting(Mechanic.FragileConduit, fragile));

            // The budget has no cell to ring — it lives in the counter at the top.
            if (board.HasBudget) found.Add(new MechanicSighting(Mechanic.MoveBudget, -1));

            if (rooted >= 0) found.Add(new MechanicSighting(Mechanic.RootedTile, rooted));

            // Both halves, always: a board of nothing but unfussy critters is the board the
            // player starts on, and "this one is not fussy" says nothing where none of them
            // is. The ring goes round the unfussy critter alone rather than round one of each
            // — unlike blending, whose answer is two hearts elsewhere on the board, the whole
            // of this rule is drawn on the tile being pointed at.
            if (unfussy >= 0 && fussy) found.Add(new MechanicSighting(Mechanic.AnyLight, unfussy));
            if (blended >= 0)
                found.Add(new MechanicSighting(Mechanic.ColourMixing, blended, HeartsBehind(board, blended)));
            if (crossing >= 0) found.Add(new MechanicSighting(Mechanic.Crossing, crossing));
            if (briar >= 0) found.Add(new MechanicSighting(Mechanic.Briar, briar));
            if (bound >= 0) found.Add(new MechanicSighting(Mechanic.BoundConduit, bound));

            return found;
        }

        /// <summary>The three channels a heart can carry, in the order a lesson names them.</summary>
        static readonly int[] Channels = { Energy.R, Energy.G, Energy.B };

        /// <summary>
        /// The hearts a blended critter's light actually comes from: the nearest one carrying
        /// each channel it is asking for, out of those the solution joins it to.
        ///
        /// <para>
        /// <b>Out of those the solution joins it to</b>, rather than out of every heart of the
        /// right colour on the board — a red heart the critter is never joined to is not where
        /// its red comes from, and pointing at one teaches a rule the glade does not follow.
        /// <b>The nearest</b>, because the lesson lights everything it rings and one hole has
        /// to hold the lot: a far heart of a colour that is also standing next door would cut
        /// the tip open across the whole board for no extra teaching.
        /// </para>
        /// </summary>
        static int[] HeartsBehind(Puzzle board, int lamp)
        {
            var feeders = new List<int>();
            board.SolutionFeeders(lamp, feeders);

            var shown = new List<int>(2);

            foreach (int channel in Channels)
            {
                if ((board.C[lamp].colour & channel) == 0) continue;

                int nearest = -1, near = int.MaxValue;

                foreach (int heart in feeders)
                {
                    if ((board.C[heart].colour & channel) == 0) continue;

                    int span = System.Math.Abs(board.X(heart) - board.X(lamp)) +
                               System.Math.Abs(board.Y(heart) - board.Y(lamp));

                    if (span >= near) continue;
                    nearest = heart;
                    near = span;
                }

                // One heart can carry both channels, and then it is the whole answer.
                if (nearest >= 0 && !shown.Contains(nearest)) shown.Add(nearest);
            }

            shown.Sort();
            return shown.ToArray();
        }

        /// <summary>
        /// Every idea on this board that has a lesson, in teaching order.
        ///
        /// <para>
        /// <b>Unfiltered on purpose.</b> This is what the glade <em>teaches</em>, which is a
        /// fact about the board; whether a particular player has met any of it is a fact about
        /// that player, and the two are asked separately because two callers want different
        /// answers. The opening sequence wants what is new (<see cref="Unseen"/>); the button
        /// that says "show me that again" wants the whole list, because the player pressing it
        /// has by definition already seen every one of them.
        /// </para>
        /// <para>
        /// A glade can bring two at once — a rooted tile and a blend, say — and holding the
        /// second back until some later glade repeats it means the player meets it unexplained
        /// in between. Shown one after another instead, which is a short queue rather than a
        /// wall of text: the list is empty on almost every glade after the first few.
        /// </para>
        /// </summary>
        public static List<MechanicSighting> Taught(Puzzle board)
        {
            var present = InBoard(board);
            var queue = new List<MechanicSighting>();

            foreach (var candidate in Mechanic.TeachingOrder)
                foreach (var sighting in present)
                    if (sighting.Mechanic.Equals(candidate)) { queue.Add(sighting); break; }

            return queue;
        }

        /// <summary>
        /// Every idea on this board the player has not met, in teaching order.
        ///
        /// A filter over <see cref="Taught"/> rather than a second walk of the board, so the
        /// two can never come to disagree about what a glade contains or what order it is
        /// taught in.
        /// </summary>
        public static List<MechanicSighting> Unseen(Puzzle board, System.Func<Mechanic, bool> seen)
        {
            var queue = Taught(board);
            if (seen == null) return queue;

            for (int i = queue.Count - 1; i >= 0; i--)
                if (seen(queue[i].Mechanic)) queue.RemoveAt(i);

            return queue;
        }
    }
}
