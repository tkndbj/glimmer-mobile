using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>A bolt that left a ward, for the view to draw.</summary>
    public readonly struct SiegeBolt
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Weak, Killed;

        /// <summary>
        /// Whether this is a turret's ability rather than the bolt it fired: a splash, a chain
        /// hop, a lance down the lane, or a tick of a burn.
        ///
        /// <b>The same record rather than one of its own</b>, because the view already knows how
        /// to draw a bolt arriving from a ward at a raider, and a second kind would be a second
        /// drawing path that could come to disagree with it. The flag exists so an extra can be
        /// drawn smaller than the shot that caused it, and for no other reason.
        /// </summary>
        public readonly bool Extra;

        /// <summary>
        /// What a <em>build</em> drew this hit with (<see cref="SiegeVia"/>), or
        /// <see cref="SiegeVia.None"/> for a turret's own bolt or ability - which is every hit on
        /// every board but the Shuffle lane's, so the drawing a chapter shows is the one it always
        /// showed. A Shuffle card's chain is drawn as lightning and its splash as a ring because
        /// this says so, not because the view guessed from the numbers.
        /// </summary>
        public readonly SiegeVia Via;

        /// <summary>
        /// The raider this hit travelled from - the one a chain hopped off, a ricochet bounced off,
        /// a shatter burst out of - or -1 when it left a ward. Only a <see cref="Via"/> other than
        /// <see cref="SiegeVia.None"/> ever names one.
        /// </summary>
        public readonly int From;

        /// <summary>
        /// The element a build's wild magic rolled onto this primary bolt, as a ward colour
        /// (0-3: fire, venom, ice, lightning), or -1 for the ward's own. The view draws the bolt in
        /// that element's reel; every board but the Shuffle lane's reports -1.
        /// </summary>
        public readonly int Element;

        public SiegeBolt(int ward, int raider, int damage, bool weak, bool killed,
                         bool extra = false, SiegeVia via = SiegeVia.None, int from = -1,
                         int element = -1)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Weak = weak;
            Killed = killed;
            Extra = extra;
            Via = via;
            From = from;
            Element = element;
        }
    }

    /// <summary>
    /// How a hit a Shuffle build granted is drawn. <b>Drawing only</b>: what a hit takes is
    /// decided where it is dealt, and this tells the view which picture says so.
    ///
    /// <para>
    /// <b><see cref="None"/> is every chapter's and the Infinite lane's</b>, and the only value a
    /// board holding <see cref="SiegeBoosts.None"/> can report: nothing but
    /// <c>SiegeBoard.Augment</c> and <c>SiegeBoard.Arsenal</c> - both shut on the plain line -
    /// passes anything else. Appended to, never reordered: a value is only ever compared, but a
    /// reorder is a silent rename for anyone reading a recorded trace.
    /// </para>
    /// </summary>
    public enum SiegeVia
    {
        None = 0,

        // A shipped card's ability, drawn as itself rather than as one more bolt from the ward.
        Splash, Arc, Lance, Twin, Execute, Blast,

        // The attack cards.
        Pellet, Ricochet, Missile, Quake, Toxic, Tesla, Shatter, Wildfire, Meteor, Vortex, Nuke,
        Ray, Hydra,
    }

    /// <summary>
    /// Something a Shuffle build set off that is a <em>place</em> rather than a hit: a shockwave
    /// rolling across a row, a meteor's shadow and its landing, a vortex opening, a nuke, a ray
    /// starting its sweep, a corpse bursting into ice or flame. The hits it dealt are reported as
    /// <see cref="SiegeBolt"/>s with the same <see cref="SiegeVia"/>, so a number is drawn once
    /// and by the one path that draws numbers; this is the picture of the event around them.
    ///
    /// <para>
    /// <b>Where it happened is in the hill's own coordinates</b> - a march down the slope and a
    /// lane across it - so the view places it with the arithmetic it places a raider with.
    /// </para>
    /// </summary>
    public readonly struct SiegeVolley
    {
        public readonly SiegeVia Via;

        /// <summary>Where, down the hill (0 the top, 1 the line) and across it (lanes, may be fractional).</summary>
        public readonly float March, Lane;

        /// <summary>How far it reaches, in boxes (a quake's row band, a meteor's plus, a vortex's pull).</summary>
        public readonly int Reach;

        /// <summary>The ward it came from, or -1 for none (a meteor, a nuke).</summary>
        public readonly int Ward;

        /// <summary>
        /// Seconds until it lands, for an event announced ahead of its impact (a meteor's shadow,
        /// a nuke's fall), or nought for one happening now.
        /// </summary>
        public readonly float In;

        public SiegeVolley(SiegeVia via, float march, float lane, int reach, int ward, float landsIn = 0f)
        {
            Via = via;
            March = march;
            Lane = lane;
            Reach = reach;
            Ward = ward;
            In = landsIn;
        }
    }

    /// <summary>
    /// A tick of a burn: what fire took off a raider this beat, and which ward lit it.
    ///
    /// <para>
    /// <b>Its own record, and that it was a <see cref="SiegeBolt"/> is the whole of what made an
    /// ember turret read as a machine gun.</b> A bolt means <em>something left a barrel</em> - the
    /// view answers one with a recoil, a muzzle flash, a comet crossing the hill and an impact
    /// (<c>SiegeView.Bolt</c>), which is right for a shot and is a lie about a burn. The burn
    /// tick reported one every frame it took a whole point, so a single ember turret drew some
    /// thirty complete shots a second out of one barrel. Reported in exactly those words, and it
    /// was never a rate: the <em>rules</em> were always a damage-over-time, and only the drawing
    /// said otherwise.
    /// </para>
    /// <para>
    /// <b>No <c>Weak</c> and no ward-strength question</b>, because a burn is not aimed: it was
    /// settled when the bolt that lit it landed (<c>SiegeRaider.Kindle</c> keeps the fiercer), and
    /// asking again here would double a rule that has already been applied.
    /// </para>
    /// <para>
    /// <b><see cref="Ward"/> may be -1</b>, which is <c>SiegeRaider.BurnFrom</c>'s own default: a
    /// burn whose ward has since fallen still burns, and the view draws it in the colour it
    /// already wears rather than dropping the tick.
    /// </para>
    /// </summary>
    public readonly struct SiegeBurn
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Killed;

        /// <summary>
        /// <b>The ward first, which is <see cref="SiegeBolt"/>'s order and not a coincidence.</b>
        /// These two are built beside each other and read beside each other, and two records of
        /// four that differ only in which of the first two ints is which is a call site that
        /// compiles perfectly and draws fire coming off the wrong body.
        /// </summary>
        public SiegeBurn(int ward, int raider, int damage, bool killed)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Killed = killed;
        }
    }

    /// <summary>A blow that landed on the line, for the view to draw.</summary>
    /// <summary>
    /// What a utility did to one raider - a hit that came from the player's own hand rather
    /// than out of a ward.
    ///
    /// Its own record and not a <see cref="SiegeBolt"/>, because a bolt names the ward that
    /// fired it and this has no ward: the view draws the two differently and analytics has to
    /// be able to tell them apart.
    /// </summary>
    public readonly struct SiegeStrike
    {
        public readonly int Raider, Damage;
        public readonly bool Killed;

        public SiegeStrike(int raider, int damage, bool killed)
        {
            Raider = raider;
            Damage = damage;
            Killed = killed;
        }
    }

    /// <summary>
    /// A cog going, and what it was worth.
    ///
    /// <para>
    /// <b>Its own record on the beat rather than a flag on the ward, because the view has to draw
    /// a journey.</b> The player's decision was made on the field - <em>which colour do I line up
    /// beside that cog</em> - and it is paid on the line, so what the drawing has to say is that
    /// those two things are one thing. <see cref="Cell"/> is where it stood and
    /// <see cref="Ward"/> is where it went.
    /// </para>
    /// <para>
    /// <see cref="Rank"/> is what the ward came out at, and it is <b>nought when nothing
    /// happened</b> - a cog taken by a colour whose ward has fallen, or is already at the top of
    /// the ladder, is a cog spent for nothing. That is a real mistake with a real cost, which is
    /// what makes choosing the colour a decision (invariant 26h) rather than a formality, and the
    /// view says so by drawing the cog coming apart where it stood and going nowhere.
    /// </para>
    /// </summary>
    public readonly struct SiegeRise
    {
        public readonly int Cell, Ward, Rank;

        /// <summary>Which of <see cref="SiegeLayout.Letters"/> took it, as an index.</summary>
        public readonly int Colour;

        public SiegeRise(int cell, int ward, int rank, int colour)
        {
            Cell = cell;
            Ward = ward;
            Rank = rank;
            Colour = colour;
        }

        /// <summary>Whether a ward really went up. False for a cog that bought nothing.</summary>
        public bool Rose => Ward >= 0 && Rank > 0;
    }

    public readonly struct SiegeBlow
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Felled;

        public SiegeBlow(int ward, int raider, int damage, bool felled)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Felled = felled;
        }
    }

    /// <summary>
    /// A warlord beginning a spell: which ward it has chosen, and how long the player has.
    ///
    /// <b>Its own record and raised the moment the spell is <em>decided</em></b>, which is the
    /// point of it: the wind-up is what the view draws over the ward that is about to be hit, and
    /// it is the window a <c>mending</c> is worth spending in. A cast that was reported only when
    /// it landed would be a fail state arriving with no warning, which is the one thing this mode
    /// already refuses to do with a wave.
    /// </summary>
    public readonly struct SiegeCast
    {
        public readonly int Raider;

        /// <summary>
        /// Which ward it is aimed at, <b>-1</b> for a warbringer's roar at the hill, or - for a
        /// spell aimed at the field rather than at the line - <b>the cell of the field</b>.
        ///
        /// <b>Ask <see cref="SiegeTuning.AimsAtAWard"/> rather than testing the number.</b> Three
        /// things can be in here and only the caster's kind says which, which is exactly why that
        /// predicate exists instead of every reader assuming.
        /// </summary>
        public readonly int Ward;

        /// <summary>Which of the four it is. The tell is drawn differently for each.</summary>
        public readonly SiegeSpell Craft;

        /// <summary>Seconds from now until it lands. <see cref="SiegeTuning.BossTell"/> of that is the tell.</summary>
        public readonly float In;

        /// <summary>Which phase of the fight this spell belongs to. See <c>SiegeRaider.Phase</c>.</summary>
        public readonly int Phase;

        /// <summary>
        /// Whether this is the spell that opens its phase - the one the guard is standing in
        /// front of, and the one the view draws as a roar rather than as a throw.
        /// </summary>
        public readonly bool Opens;

        public SiegeCast(int raider, int ward, SiegeSpell craft, float @in,
                         int phase = 0, bool opens = false)
        {
            Raider = raider;
            Ward = ward;
            Craft = craft;
            In = @in;
            Phase = phase;
            Opens = opens;
        }
    }

    /// <summary>
    /// A spell landing on the line.
    ///
    /// Its own record and not a <see cref="SiegeBlow"/>, for <see cref="SiegeStrike"/>'s reason:
    /// a blow is swung at the line by something standing at it, a spell is thrown from the middle
    /// of the hill, and the view draws the two nothing alike.
    /// </summary>
    public readonly struct SiegeSpellLanded
    {
        public readonly int Raider, Ward, Damage;
        public readonly bool Felled;

        /// <summary>Which of the four it was. The view draws each of them nothing alike.</summary>
        public readonly SiegeSpell Craft;

        /// <summary>Whether a rank really came off. Only ever true of a <see cref="SiegeSpell.Sunder"/>.</summary>
        public readonly bool Sundered;

        /// <summary>
        /// How many banked charges a drain took off the ward, or nought for every other spell.
        ///
        /// Carried on the record rather than read off the ward afterwards, because by the time
        /// the view draws the landing the ward reads nought either way - and what has to be drawn
        /// is the charges leaving.
        /// </summary>
        public readonly int Taken;

        public SiegeSpellLanded(int raider, int ward, SiegeSpell craft, int damage, bool felled,
                                bool sundered = false, int taken = 0)
        {
            Raider = raider;
            Ward = ward;
            Craft = craft;
            Damage = damage;
            Felled = felled;
            Sundered = sundered;
            Taken = taken;
        }
    }

    /// <summary>Everything that happened in one step of the clock.</summary>
    /// <summary>
    /// What an overcharge did: where it landed and what it was worth.
    ///
    /// <b>A reading rather than an int</b>, because the view has to draw the strike where the
    /// model put it - and a caller handed only "it worked" would go back to the board for a
    /// target that is, by then, very likely dead.
    /// </summary>
    public readonly struct SiegeUnleash
    {
        public readonly bool Landed;
        public readonly int Ward, Damage, Lane, Row, Absorbed;

        public SiegeUnleash(int ward, int damage, int lane, int row, int absorbed)
        {
            Landed = true;
            Ward = ward;
            Damage = damage;
            Lane = lane;
            Row = row;
            Absorbed = absorbed;
        }

        /// <summary>The tube was not full, or there was nothing on the hill to throw it at.</summary>
        public static SiegeUnleash Refused => default;
    }

    public sealed class SiegeReport
    {
        public readonly List<SiegeBolt> Bolts = new List<SiegeBolt>(16);

        /// <summary>
        /// Ticks of fire this step. See <see cref="SiegeBurn"/>.
        ///
        /// <b>Its own list rather than <see cref="Bolts"/></b>, for <see cref="Charmed"/>'s
        /// reason said about a lasting state rather than about a volley: the view draws a bolt as
        /// a journey and a burn as a thing the raider is <em>wearing</em>, and one list could only
        /// ever be drawn one of those two ways.
        /// </summary>
        public readonly List<SiegeBurn> Burns = new List<SiegeBurn>(8);

        public readonly List<SiegeBlow> Blows = new List<SiegeBlow>(4);
        public readonly List<SiegeCast> Casts = new List<SiegeCast>(2);
        public readonly List<SiegeSpellLanded> Spells = new List<SiegeSpellLanded>(2);
        public readonly List<int> Arrived = new List<int>(8);

        /// <summary>
        /// Bombs a bomber left standing on the hill this step.
        ///
        /// <b>Its own list rather than a flag on the bomb</b>, because the view has to play the
        /// drop exactly once and a flag would have to be cleared by whoever noticed it first -
        /// which is the class of two-places-hold-one-state bug this report exists to remove.
        /// </summary>
        public readonly List<SiegeBomb> Dropped = new List<SiegeBomb>(2);

        /// <summary>Cogs a kill left on the hill this step. See <c>SiegeBoard.Cog</c>.</summary>
        public readonly List<SiegeCog> Cogs = new List<SiegeCog>(2);

        /// <summary>
        /// Every raider that died inside this step, as the bodies themselves.
        ///
        /// <para>
        /// <b>The one list here that carries a corpse rather than a fact about one, and it exists
        /// because the view cannot go and look.</b> A raider is a <em>state</em> the view reads
        /// off <c>SiegeBoard.Raiders</c> after the step - but <c>Advance</c> musters, walks and
        /// fires in one call and then sweeps the dead out of that list, so a raider that steps
        /// onto the hill and is shot in the same call was never in it at any moment the view could
        /// look. A ward with nothing to shoot at holds its cooldown at nought
        /// (<c>SiegeBoard.Shoot</c>), so a whole line is loaded and waiting the instant a wave
        /// steps out: on a strong line that is not an edge case, it is <em>every</em> raider.
        /// </para>
        /// <para>
        /// What that cost is not one missing body. Everything the view draws about a raider is
        /// keyed on a widget it mints from that list, so a raider it never saw takes its bolt, its
        /// muzzle flash, its impact and its death burst down with it - and the only thing left on
        /// the screen is the cog, which carries its own coordinates. Reported from play as
        /// <em>no raider came in, the hill was empty, but cogs were dropping</em>.
        /// </para>
        /// <para>
        /// <b>A kill made outside <c>Advance</c> never reaches here</b>, and that is deliberate
        /// rather than a gap: <c>Advance</c> clears the report before it does anything, so a
        /// firepot's or a utility's kill is wiped before any frame reads it. Those doors already
        /// have a widget on the screen to take down and call <c>SiegeView.Reap</c> themselves.
        /// </para>
        /// </summary>
        public readonly List<SiegeRaider> Felled = new List<SiegeRaider>(8);

        /// <summary>Cogs that ran out of time this step, by id. See <c>SiegeBoard.Age</c>.</summary>
        public readonly List<int> Trampled = new List<int>(2);

        /// <summary>
        /// Loose things a gravemaw ate this step, by id - cogs and bombs together.
        ///
        /// <b>One list rather than two, because both id spaces are one</b>: a cog, a bomb and a
        /// raider are all minted from <c>SiegeBoard._minted</c>, so an id names exactly one thing
        /// on the hill and the view can look for it in either of its own lists.
        /// </summary>
        public readonly List<int> Devoured = new List<int>(4);

        /// <summary>Tubes that filled this step, by ward. What arms the overcharge.</summary>
        public readonly List<int> Brimmed = new List<int>(2);

        /// <summary>
        /// The volley a stormglass loosed this step. See <c>SiegeBoard.Volley</c>.
        ///
        /// <para>
        /// <b><see cref="SiegeBolt"/>, because that is exactly what these are</b> - a bolt from a
        /// named ward at a named raider, worth double against its own colour. A record of its own
        /// would have been a second way of saying the one thing the view already knows how to draw.
        /// </para>
        /// <para>
        /// <b>Its own list rather than <see cref="Bolts"/>, all the same.</b> A stormglass is drawn
        /// as one volley rather than as nine unrelated shots, so the view has to be handed it
        /// whole - and a funnel that could not separate a free charm from a bought firepot would
        /// price the shelf against something the board hands out.
        /// </para>
        /// </summary>
        public readonly List<SiegeBolt> Charmed = new List<SiegeBolt>(16);

        /// <summary>
        /// A furnace that has landed on the line this step: which ward it reached and whether
        /// the ward could hold what it brought.
        ///
        /// <b>Booked exactly as a stormglass's bolts are</b> (invariant 37s), so it arrives here
        /// a beat after the gem burst rather than on the frame of the swap - the tube is seen to
        /// fill from the stone. A refusal is reported as well as a bank, because a charm that
        /// silently did nothing reads as a broken gem rather than as a wrong choice.
        /// </summary>
        public readonly List<SiegeForged> Forged = new List<SiegeForged>(2);

        /// <summary>
        /// Wards that fired into a gorgon's glare this step and landed nothing
        /// (<see cref="SiegeSpell.Glare"/>), and wards whose sunlord seal was paid off
        /// (<see cref="SiegeSpell.Doom"/>).
        ///
        /// <b>Both are lists of posts rather than events with a payload</b>, because neither
        /// carries a number: a stone-struck shot is worth nothing by definition, and a paid seal
        /// is worth exactly the thing that is no longer going to happen. What the view needs is
        /// which post, and how many times.
        /// </summary>
        public readonly List<int> Stoned = new List<int>(4);

        /// <summary>Wards whose seal was paid off this step. See <see cref="Stoned"/>.</summary>
        public readonly List<int> Redeemed = new List<int>(2);

        /// <summary>
        /// Seconds the hill was just stopped for by an hourglass landing this step, or nought.
        ///
        /// <b>The edge rather than the state</b>, for <see cref="Brimmed"/>'s reason: the view
        /// reads <c>SiegeBoard.Stilled</c> every frame to draw a stopped hill, and what it wants
        /// here is the frame the stop began on, which is the frame the wave is drawn crossing it.
        /// </summary>
        public float Stilled;

        /// <summary>
        /// How far an anvil drove the hill back this step, as a share of the hill
        /// (<see cref="SiegeCharm.Anvil"/>), and how many bodies it moved.
        ///
        /// <b>Two numbers rather than one</b>, because the view needs both answers and they are
        /// different questions: <c>Heaved</c> is how big the shock is drawn, and <c>Shoved</c> is
        /// whether there was anything on the hill for it to throw. An anvil sprung over an empty
        /// hill - or over a boss, which does not move - reports the first and not the second, and
        /// what the player sees is the refusal rather than nothing at all.
        /// </summary>
        public float Heaved;

        /// <summary>How many bodies the anvil moved. See <see cref="Heaved"/>.</summary>
        public int Shoved;

        /// <summary>
        /// Seconds of hex a broken curse just laid on the hill, or nought
        /// (<see cref="SiegeLayout.Obsidian"/>).
        ///
        /// <b>The edge, for <see cref="Stilled"/>'s reason</b>: the view reads
        /// <see cref="SiegeRaider.Hexed"/> every frame to draw who is carrying it, and what it
        /// wants here is the frame the curse fell - which is the frame it is drawn sweeping the
        /// hill. A curse that fell on an empty hill reports its seconds and no bodies, and the
        /// view draws the refusal rather than nothing.
        /// </summary>
        public float Hex;

        /// <summary>
        /// Whether a singularity's beam fired this step (<see cref="SiegeLayout.Singularity"/>),
        /// and what it took off each body it reached.
        ///
        /// <b>The flag is the edge and is true over an empty hill too</b>, so the view draws the
        /// beam firing at nothing rather than nothing at all (the furnace's rule).
        /// </summary>
        public bool Beamed;

        public readonly List<SiegeStrike> Beam = new List<SiegeStrike>(12);

        /// <summary>The raiders the curse marked this step, by id. See <see cref="Hex"/>.</summary>
        public readonly List<int> Hexed = new List<int>(12);

        /// <summary>
        /// What a curse's wither took off each body this step (<c>SiegeRaider.Withering</c>).
        ///
        /// <b>Its own list and not <see cref="Burns"/></b>, for the burn's own reason turned
        /// round: a burn is drawn in the colour of the ward that lit it, and a wither was lit by
        /// no ward, so carried as a burn it would be drawn as fire in a colour nobody chose.
        /// </summary>
        public readonly List<SiegeStrike> Withers = new List<SiegeStrike>(16);

        /// <summary>
        /// Wards a build stood back up this step, by post (<see cref="SiegeBoosts.SecondWinds"/>,
        /// <see cref="SiegeBoosts.Phoenixes"/>). Empty on every board but the Shuffle lane's.
        ///
        /// <b>Reported beside the fall rather than instead of it</b>: the blow that felled the
        /// post is still in <see cref="Blows"/> or <see cref="Spells"/> with <c>Felled</c> true,
        /// so the view draws the fall and then the rise, which is the picture a second wind is.
        /// </summary>
        public readonly List<int> Revived = new List<int>(4);

        /// <summary>
        /// Places a build set something off this step (<see cref="SiegeVolley"/>): empty on every
        /// board but the Shuffle lane's, because only <c>SiegeBoard.Arsenal</c> writes it and it
        /// is shut on the plain line.
        /// </summary>
        public readonly List<SiegeVolley> Volleys = new List<SiegeVolley>(4);

        /// <summary>The wave that has just stepped out, or -1.</summary>
        public int Wave = -1;

        public void Clear()
        {
            Revived.Clear();
            Volleys.Clear();
            Bolts.Clear();
            Burns.Clear();
            Blows.Clear();
            Casts.Clear();
            Spells.Clear();
            Arrived.Clear();
            Dropped.Clear();
            Cogs.Clear();
            Felled.Clear();
            Trampled.Clear();
            Devoured.Clear();
            Brimmed.Clear();
            Charmed.Clear();
            Forged.Clear();
            Stoned.Clear();
            Redeemed.Clear();
            Stilled = 0f;
            Heaved = 0f;
            Shoved = 0;
            Hex = 0f;
            Hexed.Clear();
            Withers.Clear();
            Beamed = false;
            Beam.Clear();
            Wave = -1;
        }

        public bool Any => Bolts.Count > 0 || Burns.Count > 0 || Blows.Count > 0 || Casts.Count > 0
                        || Spells.Count > 0 || Arrived.Count > 0 || Dropped.Count > 0
                        || Cogs.Count > 0 || Felled.Count > 0 || Trampled.Count > 0
                        || Devoured.Count > 0
                        || Brimmed.Count > 0 || Charmed.Count > 0 || Forged.Count > 0
                        || Stoned.Count > 0 || Redeemed.Count > 0 || Withers.Count > 0
                        || Revived.Count > 0 || Volleys.Count > 0
                        || Stilled > 0f || Heaved > 0f || Hex > 0f || Beamed || Wave >= 0;
    }

    /// <summary>Fuel a match has earned that has not reached its ward yet.</summary>
    public struct SiegeCharge
    {
        public int Ward;
        public float Fuel;
        public float In;
    }

    /// <summary>One gem falling, or arriving. <see cref="From"/> below nought is a new gem.</summary>
    public readonly struct SiegeDrop
    {
        public readonly int Column, From, To, Colour;

        /// <summary>
        /// What this gem is carrying, or <see cref="SiegeCharm.None"/>.
        ///
        /// <b>On the drop rather than looked up when it lands</b>, because the view animates a
        /// fall over a third of a second and the board has moved on: a picture that asked the
        /// model what it was carrying would be asking about whatever ended up in that cell after
        /// the next beat.
        /// </summary>
        public readonly SiegeCharm Charm;

        public SiegeDrop(int column, int from, int to, int colour,
                         SiegeCharm charm = SiegeCharm.None)
        {
            Column = column;
            From = from;
            To = to;
            Colour = colour;
            Charm = charm;
        }

        public bool IsNew => From < 0;
    }

    /// <summary>
    /// A charm going off: where it stood, which one it was, and the colour it was paid as.
    ///
    /// <para>
    /// <b>Recorded rather than left for the view to work out, which is invariant 30i's rule.</b>
    /// A lance's cross and the cells an ordinary run took arrive in <see cref="SiegeBeat.Cleared"/>
    /// as one list, so a drawing handed only that could not say which of twenty gems was the one
    /// the player aimed - and the whole of what a charm has to read as is <em>this gem did that</em>.
    /// </para>
    /// <para>
    /// <see cref="Colour"/> is the colour it was <em>paid</em> as and not the letter it was
    /// carrying: a prism is drawn colourless and is worth the run it completed, so the burst that
    /// says what it bought has to be in that colour or it says nothing.
    /// </para>
    /// </summary>
    /// <summary>
    /// A furnace landing on the line: the ward it reached, and whether it banked a charge there.
    ///
    /// <b><see cref="Banked"/> false is a refusal, not a fault</b> - the ward had fallen, or was
    /// already holding <c>SiegeTuning.MostCharges</c> - and the view draws it as one, because the
    /// decision a furnace asks is which colour, and a wrong answer the player cannot see is not a
    /// decision (invariant 26h).
    /// </summary>
    public readonly struct SiegeForged
    {
        public readonly int Ward;
        public readonly bool Banked;

        public SiegeForged(int ward, bool banked)
        {
            Ward = ward;
            Banked = banked;
        }
    }

    public readonly struct SiegeSpark
    {
        public readonly int Cell;
        public readonly SiegeCharm Charm;

        /// <summary>Which of <see cref="SiegeLayout.Letters"/> it paid, or -1.</summary>
        public readonly int Colour;

        public SiegeSpark(int cell, SiegeCharm charm, int colour)
        {
            Cell = cell;
            Charm = charm;
            Colour = colour;
        }
    }

    /// <summary>One beat of a cascade: what went, what it fuelled, and what fell into the gap.</summary>
    public sealed class SiegeBeat
    {
        public readonly List<int> Cleared = new List<int>(12);
        public readonly List<SiegeDrop> Drops = new List<SiegeDrop>(24);

        /// <summary>Every cog this beat took, and what each was worth.</summary>
        public readonly List<SiegeRise> Rises = new List<SiegeRise>(2);

        /// <summary>Every charm this beat set off, in the order they went.</summary>
        public readonly List<SiegeSpark> Sprung = new List<SiegeSpark>(2);

        /// <summary>
        /// Every obsidian a broken curse took this beat, in cell order - the run that broke it
        /// and every stone it drew in from the rest of the field - or empty when no curse broke.
        ///
        /// <b>Recorded rather than read back, for <see cref="Paid"/>'s reason</b>: the cells are
        /// holes by the time the view draws them, and what the drawing has to say is which
        /// stones were pulled across the field into the break and which were the run itself.
        /// </summary>
        public readonly List<int> Broken = new List<int>(6);

        /// <summary>The cells of <see cref="Broken"/> that were in a run, which is where the break is drawn from.</summary>
        public readonly List<int> Breakers = new List<int>(3);

        /// <summary>Whether a curse broke on this beat.</summary>
        public bool Unbound => Broken.Count > 0;

        /// <summary>
        /// Whether a singularity collapsed on this beat and took the whole field with it
        /// (<c>SiegeBoard.Devour</c>), and the void stones whose run it was - where the hole is
        /// drawn opening.
        /// </summary>
        public bool Swallowed;

        public readonly List<int> Eaters = new List<int>(3);

        /// <summary>
        /// What each cell of <see cref="Cleared"/> was paid as, as an index into
        /// <see cref="SiegeLayout.Letters"/>. Parallel to it, and the same length.
        ///
        /// <para>
        /// <b>Recorded rather than read back off the board, which is invariant 30i's rule.</b> The
        /// cell is a hole by the time the view draws it and a fresh gem by the time the animation
        /// ends, so a drawing that asked the model what colour it had just taken would be asking
        /// about whatever fell into it. That was survivable while every cell was worth its own
        /// letter; a prism is worth the colour of the run it joined and nothing on the board
        /// remembers which that was.
        /// </para>
        /// <para>
        /// It is what decides the tint of the burst and which ward a mote crosses to, so a wrong
        /// answer here is a match that visibly pays the wrong turret.
        /// </para>
        /// </summary>
        public readonly List<int> Paid = new List<int>(12);

        /// <summary>Fuel this beat put into each ward, in ward order.</summary>
        public float[] Fuel;

        /// <summary>Which beat of the cascade this is, counting from one.</summary>
        public int Depth;
    }

    /// <summary>What one swap turned into.</summary>
    public sealed class SiegeTurn
    {
        public int A, B;
        public readonly List<SiegeBeat> Beats = new List<SiegeBeat>(4);

        /// <summary>Gems cleared altogether, which is what the flourish readout counts.</summary>
        public int Worth;

        /// <summary>
        /// Where each cell's gem came from when the field was dealt again for want of a move
        /// (<c>SiegeBoard.Settle</c>) - <c>Shuffled[to] == from</c> - or null when it was not.
        ///
        /// <para>
        /// <b>A record of the shuffle the board already made, so it can be drawn.</b> The field
        /// was always dealt again when nothing on it lined up, and the view only ever learnt of
        /// it by repainting every cell from the model at the end of the turn - so the whole board
        /// changed in one frame, which read as a glitch rather than as the game helping. It is a
        /// permutation over the cells and nothing more: the shuffle draws exactly what it always
        /// drew from the stream (invariant 41), and a reader that ignores this field sees exactly
        /// the board it always saw.
        /// </para>
        /// </summary>
        public int[] Shuffled;
    }
}
