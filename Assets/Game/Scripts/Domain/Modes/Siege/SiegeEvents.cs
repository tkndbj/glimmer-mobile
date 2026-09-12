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

        public SiegeBolt(int ward, int raider, int damage, bool weak, bool killed,
                         bool extra = false)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Weak = weak;
            Killed = killed;
            Extra = extra;
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
    /// a journey.</b> The player's decision was made on the field — <em>which colour do I line up
    /// beside that cog</em> — and it is paid on the line, so what the drawing has to say is that
    /// those two things are one thing. <see cref="Cell"/> is where it stood and
    /// <see cref="Ward"/> is where it went.
    /// </para>
    /// <para>
    /// <see cref="Rank"/> is what the ward came out at, and it is <b>nought when nothing
    /// happened</b> — a cog taken by a colour whose ward has fallen, or is already at the top of
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
        /// Which ward it is aimed at, <b>-1</b> for a warbringer's roar at the hill, or — for a
        /// spell aimed at the field rather than at the line — <b>the cell of the field</b>.
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

        public SiegeCast(int raider, int ward, SiegeSpell craft, float @in)
        {
            Raider = raider;
            Ward = ward;
            Craft = craft;
            In = @in;
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

        public SiegeSpellLanded(int raider, int ward, SiegeSpell craft, int damage, bool felled,
                                bool sundered = false)
        {
            Raider = raider;
            Ward = ward;
            Craft = craft;
            Damage = damage;
            Felled = felled;
            Sundered = sundered;
        }
    }

    /// <summary>Everything that happened in one step of the clock.</summary>
    /// <summary>
    /// What an overcharge did: where it landed and what it was worth.
    ///
    /// <b>A reading rather than an int</b>, because the view has to draw the strike where the
    /// model put it — and a caller handed only "it worked" would go back to the board for a
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
        public readonly List<SiegeBlow> Blows = new List<SiegeBlow>(4);
        public readonly List<SiegeCast> Casts = new List<SiegeCast>(2);
        public readonly List<SiegeSpellLanded> Spells = new List<SiegeSpellLanded>(2);
        public readonly List<int> Arrived = new List<int>(8);

        /// <summary>
        /// Bombs a bomber left standing on the hill this step.
        ///
        /// <b>Its own list rather than a flag on the bomb</b>, because the view has to play the
        /// drop exactly once and a flag would have to be cleared by whoever noticed it first —
        /// which is the class of two-places-hold-one-state bug this report exists to remove.
        /// </summary>
        public readonly List<SiegeBomb> Dropped = new List<SiegeBomb>(2);

        /// <summary>Cogs a kill left on the hill this step. See <c>SiegeBoard.Cog</c>.</summary>
        public readonly List<SiegeCog> Cogs = new List<SiegeCog>(2);

        /// <summary>Cogs that ran out of time this step, by id. See <c>SiegeBoard.Age</c>.</summary>
        public readonly List<int> Trampled = new List<int>(2);

        /// <summary>Tubes that filled this step, by ward. What arms the overcharge.</summary>
        public readonly List<int> Brimmed = new List<int>(2);

        /// <summary>The wave that has just stepped out, or -1.</summary>
        public int Wave = -1;

        public void Clear()
        {
            Bolts.Clear();
            Blows.Clear();
            Casts.Clear();
            Spells.Clear();
            Arrived.Clear();
            Dropped.Clear();
            Cogs.Clear();
            Trampled.Clear();
            Brimmed.Clear();
            Wave = -1;
        }

        public bool Any => Bolts.Count > 0 || Blows.Count > 0 || Casts.Count > 0
                        || Spells.Count > 0 || Arrived.Count > 0 || Dropped.Count > 0
                        || Cogs.Count > 0 || Trampled.Count > 0 || Brimmed.Count > 0
                        || Wave >= 0;
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

        public SiegeDrop(int column, int from, int to, int colour)
        {
            Column = column;
            From = from;
            To = to;
            Colour = colour;
        }

        public bool IsNew => From < 0;
    }

    /// <summary>One beat of a cascade: what went, what it fuelled, and what fell into the gap.</summary>
    public sealed class SiegeBeat
    {
        public readonly List<int> Cleared = new List<int>(12);
        public readonly List<SiegeDrop> Drops = new List<SiegeDrop>(24);

        /// <summary>Every cog this beat took, and what each was worth.</summary>
        public readonly List<SiegeRise> Rises = new List<SiegeRise>(2);

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
    }
}
