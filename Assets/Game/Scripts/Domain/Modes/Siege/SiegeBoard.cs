using System;
using System.Collections.Generic;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A siege: a field of gems, a line of wards and a hill with raiders coming down it.
    ///
    /// <para>
    /// <b>The clock is passed in.</b> Nothing here reads <c>Time</c> — <see cref="Advance"/> takes
    /// the seconds that have gone by, so the whole mode is Domain and can be stepped by a test at
    /// whatever rate a test likes. That is the same bargain every board here makes and the reason
    /// this one is not simply written inside the view.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard : IProtoBoard
    {
        /// <summary>A cell with nothing in it, which only ever exists mid-resolve.</summary>
        public const char Hole = '.';

        public readonly SiegeLayout Layout;

        readonly char[] _cells;

        /// <summary>
        /// Which cells a weaver has locked. Parallel to <see cref="_cells"/> and moved with it.
        ///
        /// <para>
        /// <b>A flag rather than a glyph, and a thief's sack is the opposite case.</b> A webbed
        /// gem is still that colour — the player can see exactly what they are being denied, and
        /// the web comes off with the weaver — so it has to sit <em>beside</em> the cell rather
        /// than replace it. A sack is not a colour at all, so it is a glyph
        /// (<see cref="SiegeLayout.Sack"/>) and every rule that walks the field is already
        /// correct about it.
        /// </para>
        /// <para>
        /// <b>One array and not two.</b> Anything parallel to the field has to be carried through
        /// <see cref="Collapse"/> and <see cref="Settle"/> in lockstep, and every extra array is
        /// another chance for one of them to be forgotten — which is why a sack carries no memory
        /// of the colour it took and simply bursts into a freshly dealt gem.
        /// </para>
        /// </summary>
        readonly bool[] _webbed;

        readonly SiegeWard[] _wards;
        readonly List<SiegeRaider> _raiders = new List<SiegeRaider>(24);
        readonly SiegeReport _report = new SiegeReport();
        readonly List<SiegeCharge> _flying = new List<SiegeCharge>(16);

        /// <summary>A spell that has been cast and has not arrived. See <see cref="Conjure"/>.</summary>
        struct Flight
        {
            public int Raider, Ward;
            public SiegeSpell Craft;
            public float In;
        }

        readonly List<Flight> _spells = new List<Flight>(4);

        /// <summary>
        /// Seconds left on a warbringer's roar, or nought.
        ///
        /// <b>One number for the whole hill rather than one per raider</b>, because a roar is a
        /// fact about the ground and not about who is standing on it — a raider that steps out
        /// mid-roar charges with the rest, which is what a player watching the hill expects and
        /// what a per-raider timer would quietly get wrong.
        /// </summary>
        float _roar;

        uint _rng;

        /// <summary>
        /// The hill's own random stream: which cell a weaver or a thief reaches for.
        ///
        /// <para>
        /// <b>Separate from <see cref="_rng"/>, and only for the draws a player's taps do not
        /// order.</b> The field's stream must be a pure function of the swaps that were made, or
        /// two devices playing the same board deal different gems. A wave's lanes are safe to draw
        /// from it — a lane is drawn once per raider, in a sequence fixed by play rather than by
        /// frame rate — but a weaver reaching for a cell every few seconds is not: how many times
        /// it has reached by the time a gem is dealt depends on wall-clock time, which is a frame
        /// rate, a background-and-resume and a slower phone.
        /// </para>
        /// <para>
        /// <b>Narrow on purpose.</b> Moving the lanes here as well was tried and reverted: it does
        /// not merely change which lane a raider walks in, it stops the muster consuming the
        /// field's stream at all — so every gem dealt after the first wave changes, and the ten
        /// rungs of a tuned chapter are re-rolled. Three of them became unholdable and two became
        /// trivial, with nothing wrong in any file. <b>A random stream is part of a level's
        /// content</b>, and anything that changes how often it is drawn from is a content change.
        /// </para>
        /// </summary>
        uint _hill;

        int _wave;
        int _minted;
        int _felled;
        float _rest;

        /// <summary>Whether the authored field stood a cog on it. See <see cref="Upgrades"/>.</summary>
        readonly bool _seeded;

        /// <summary>
        /// The turrets the player stood on the line.
        ///
        /// <b>Handed in rather than read out of a static</b>, so a fixture can play the same hill
        /// against the weakest line a player could bring and against a decked-out one without
        /// touching a save — which is what <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>
        /// needs in order to prove a rung is holdable rather than merely holdable by somebody who
        /// has been shopping.
        /// </b>
        /// </summary>
        public readonly WardLine Line;

        SiegeBoard(SiegeLayout layout, WardLine line)
        {
            Layout = layout;
            _cells = layout.Grid.Copy();
            _webbed = new bool[_cells.Length];
            _rng = layout.Seed;

            // A different constant so the two streams cannot walk in step, and never nought,
            // because xorshift32 is stuck there.
            _hill = layout.Seed * 2654435761u;
            if (_hill == 0u) _hill = 2463534242u;

            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] == SiegeLayout.Cog) { _seeded = true; break; }

            Line = line ?? WardLine.Starter(WardCatalog.Default);

            _wards = new SiegeWard[layout.Wards.Length];
            for (int i = 0; i < _wards.Length; i++)
            {
                int colour = SiegeLayout.Letters.IndexOf(layout.Wards[i]);
                _wards[i] = new SiegeWard(colour, Line.At(colour));
            }

            _rest = SiegeTuning.FirstWaveAfter;
        }

        /// <summary>
        /// Builds a board for this level, standing the player's chosen turrets on it.
        ///
        /// <b>Null is the starter line</b>, which is what every content gate, every offline
        /// mirror and every rule test plays against — the weakest line a player could bring.
        /// </summary>
        public static SiegeBoard Build(SiegeLayout layout, WardLine line = null)
            => new SiegeBoard(layout, line);

        // ------------------------------------------------------------------ the field
        public int Width => Layout.Grid.Width;
        public int Height => Layout.Grid.Height;
        public int Count => _cells.Length;

        public char At(int index) => index >= 0 && index < _cells.Length ? _cells[index] : Hole;

        public int ColourAt(int index) => SiegeLayout.Letters.IndexOf(At(index));

        public int IndexOf(int x, int y) => y * Width + x;

        public IReadOnlyList<SiegeWard> Wards => _wards;

        /// <summary>Fuel booked and still crossing the field. Nothing grades on it.</summary>
        public IReadOnlyList<SiegeCharge> Flying => _flying;
        public IReadOnlyList<SiegeRaider> Raiders => _raiders;

        public int WardsStanding
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _wards.Length; i++) if (_wards[i].Alive) n++;
                return n;
            }
        }

        /// <summary>The wave now on the hill, counting from one. Nought before the first.</summary>
        public int Wave => _wave;

        /// <summary>How many waves this siege sends, the warlord's included.</summary>
        public int Waves => Layout.Waves.Length;

        /// <summary>Whether this lane's waves never stop. <see cref="SiegeLayout.IsEndless"/>.</summary>
        public bool IsEndless => Layout.IsEndless;

        /// <summary>Whether the wave now on the hill is the warlord's.</summary>
        public bool BossWave => Layout.HasBoss && _wave == Layout.BossWave + 1;

        /// <summary>The warlord, while it is standing, or null.</summary>
        public SiegeRaider Warlord
        {
            get
            {
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Boss && _raiders[i].Alive) return _raiders[i];

                return null;
            }
        }

        // ------------------------------------------------------------------ IProtoBoard
        /// <summary>
        /// Whether this run has produced its answer.
        ///
        /// <para>
        /// <b>An endless lane is finished when the line falls, and that is not a euphemism.</b> A
        /// run that can never be won still has to <em>end</em>, and the ending it has is the only
        /// one it has: the hill got through. Routing it here rather than through the defeat path
        /// is what makes an endless run an ordinary run in every way that matters — it is graded
        /// (on how far it got, <c>LevelTuning.Climbs</c>), it promotes stars, it pays the credits
        /// and the XP those stars derive, and it costs the save file nothing at all beyond the
        /// high-water wave (invariant 20a, one more time).
        /// </para>
        /// <para>
        /// It is asked before <c>AnyMove</c> by <c>ProtoVerdict.Read</c>, which is what stops the
        /// same state being read as "nothing left to do" a line later.
        /// </para>
        /// </summary>
        public bool IsFinished => Layout.IsEndless ? WardsStanding == 0 : GoalsLeft == 0;

        /// <summary>How many waves this run has seen off. The score, on an endless lane.</summary>
        public int WavesCleared => _wave > 0 ? _wave - 1 : 0;

        public int Goals => Layout.RaiderCount;

        public int GoalsLeft => Goals - _felled;

        /// <summary>
        /// How many raiders are standing on the hill right now.
        ///
        /// <b>Not <see cref="GoalsLeft"/>, which counts everything the level has left to send.</b>
        /// A utility that lands on the hill can only reach what is already walking, so this is what
        /// decides whether one would do anything at all.
        /// </summary>
        public int OnTheHill
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive && _raiders[i].OnTheHill) n++;

                return n;
            }
        }

        /// <summary>
        /// Whether a move on this board could still mean anything.
        ///
        /// <b>A line with nothing standing on it is where a run of this mode ends</b>, and it is
        /// honestly this question rather than a special case: with every ward down, a match feeds
        /// nothing, so there is no legal move left however many gems are on the field. The gem
        /// field itself always has a swap — <see cref="Settle"/> deals it again rather than
        /// letting it lock (invariant 20j's second test: every input has to move something).
        /// </summary>
        public bool AnyMove => WardsStanding > 0;

        /// <summary>
        /// <b>Never stranded, and that is arithmetic rather than generosity.</b> A run here ends
        /// when the last ward falls — <see cref="AnyMove"/> — and what a continue sells is the
        /// line itself: <see cref="Rally"/> puts every turret back up at full health with the
        /// hill exactly where it stood. So a fallen line is a shortage that a purchase really
        /// does fix, which is the only thing this predicate is asked (invariant 28f), and
        /// answering <c>true</c> would refuse a rescue to somebody who could still win.
        ///
        /// <para>
        /// <b>Note what does not make it true either.</b> A pod carrying a cage jams the line
        /// rather than passing through it (invariant 33b), so every goal a board opened with is
        /// still standing on it however long the run goes on — more cores always help. And the
        /// gem field is dealt again rather than allowed to lock (invariant 20j), so there is
        /// always a swap. There is no siege state no purchase rescues.
        /// </para>
        /// <para>
        /// It is <em>this</em> rather than the run's own gate that decides whether an offer is
        /// honest. Whether one is actually made is <c>RunContinueFlow</c>'s — a run nobody is
        /// charged for is a run nobody is sold, so the opening rungs of the chapter never see it.
        /// </para>
        /// </summary>
        public bool Stranded => false;
    }
}
