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
        /// What each cell is carrying, parallel to <see cref="_cells"/> and moved with it.
        ///
        /// <para>
        /// <b>A second array rather than a second alphabet, which is the opposite of what this
        /// field has twice shipped.</b> A cog in a cell and a thief's sack were both <em>glyphs</em>
        /// — things that were not a colour — and both were taken back out, because every rule here
        /// asks either <em>what colour is this</em> or <em>what is standing here</em> and one
        /// alphabet answers the first with a thing that has none. A charm is not a second answer to
        /// the first question: the cell is still a gem, still that colour, still worth that fuel.
        /// So it sits beside the cell exactly as a weaver's web did, and for the same reason — the
        /// player can see both facts at once.
        /// </para>
        /// <para>
        /// <b>The cost of anything parallel is that it has to be carried through
        /// <c>Collapse</c> and <c>Settle</c> in lockstep</b>, and that is the whole of what makes
        /// this shape expensive. It is one array and not two because there is exactly one fact per
        /// cell to carry; a charm that needed to remember anything else would be wrong here.
        /// </para>
        /// </summary>
        readonly SiegeCharm[] _charms;

        /// <summary>
        /// Scratch for <see cref="SiegeLayout.Runs"/>: the colour each cleared cell is paid as.
        ///
        /// <b>Held rather than allocated per beat</b>, because a cascade resolves several of them
        /// inside one swap and this board is stepped sixty times a second. Cleared at the head of
        /// every use, never read outside one.
        /// </summary>
        readonly char[] _paid;

        /// <summary>
        /// Where the next charm falls: how many gems have been dealt since the last one, and which
        /// gem of the current window is the one that carries it.
        ///
        /// <para>
        /// <b>State on the board rather than a chance taken per gem</b>, which is what makes the
        /// gap <em>bounded</em> — see <see cref="SiegeTuning.CharmWithin"/> for the measurement that
        /// bought it. Both are a pure function of the field's own stream, so two devices playing
        /// the same swaps meet the same charms in the same cells, exactly as they meet the same
        /// gems.
        /// </para>
        /// <para>
        /// <b>The first window is picked from the seed and not from nought</b>, or every board in
        /// the mode would deal its first charm on the same gem of the first refill — a tell a
        /// player would find in one session and the one thing a deterministic stream makes easy to
        /// get wrong.
        /// </para>
        /// </summary>
        int _sinceCharm, _charmAt;

        /// <summary>
        /// Which cells a weaver has locked. Parallel to <see cref="_cells"/> and moved with it.
        ///
        /// <para>
        /// <b>A flag rather than a glyph, and a thief's sack is the opposite case.</b> A webbed
        /// gem is still that colour — the player can see exactly what they are being denied, and
        /// the web comes off with the weaver — so it has to sit <em>beside</em> the cell rather
        /// than replace it. A sack is not a colour at all, so it is a glyph
        /// and every rule that walks the field is already
        /// correct about it.
        /// </para>
        /// <para>
        /// <b>One array and not two.</b> Anything parallel to the field has to be carried through
        /// <see cref="Collapse"/> and <see cref="Settle"/> in lockstep, and every extra array is
        /// another chance for one of them to be forgotten — which is why a sack carries no memory
        /// of the colour it took and simply bursts into a freshly dealt gem.
        /// </para>
        /// </summary>

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

            /// <summary>Whether this is the spell its phase's guard stands in front of. See <c>Arrive</c>.</summary>
            public bool Opens;
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

            // **A field opens with no charms on it, and that is the rule rather than the default.**
            // Charms are dealt into a refill (`Deal`), so an authored field is exactly what its
            // author wrote and the settled proof is asked the question it was written for.
            _charms = new SiegeCharm[_cells.Length];
            _paid = new char[_cells.Length];

            // Where the first charm falls, out of the seed rather than out of the stream: taking a
            // draw for it would be a draw, and invariant 41 is that the number of draws is content.
            //
            // **`CharmFirstWithin` and not `CharmWithin`, which is the whole of what "I never see
            // them" turned out to be.** The window bounds the gap between two charms; the gap
            // before the *first* one was drawn from the same window, so a run could be a whole
            // window old before it met one - and on a short rung that is longer than the run.
            _charmAt = (int)(Avalanche(layout.Seed) % (uint)SiegeTuning.CharmFirstWithin);

            _rng = layout.Seed;

            // A different constant so the two streams cannot walk in step, and never nought,
            // because xorshift32 is stuck there.
            _hill = layout.Seed * 2654435761u;
            if (_hill == 0u) _hill = 2463534242u;

            Line = line ?? WardLine.Starter(WardCatalog.Default);

            _wards = new SiegeWard[layout.Wards.Length];
            for (int i = 0; i < _wards.Length; i++)
            {
                int colour = SiegeLayout.Letters.IndexOf(layout.Wards[i]);
                // **The build and not the model**, so the stars a player has bought reach the
                // bolt and the chassis. Asked of the line rather than of a ledger, because a
                // line is resolved once and a ledger can move under a run when a sync lands.
                _wards[i] = new SiegeWard(colour, Line.BuildAt(colour));
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

        /// <summary>
        /// What this run can say about whether the player ever looked at the hill.
        ///
        /// <para>
        /// Recorded, never read by any rule: nothing on this board may branch on it, so a
        /// measurement cannot become a mechanic by accident. See <see cref="SiegeAttention"/>.
        /// </para>
        /// </summary>
        public SiegeAttention Attention { get; } = new SiegeAttention();

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

        /// <summary>
        /// Seconds of the run's own clock left before the first wave steps out, and nought the
        /// instant it has.
        ///
        /// <para>
        /// <b>It exists so the count-in can be <em>derived</em> from the quiet it counts rather
        /// than timed alongside it.</b> The view drew three, two, one and GO! from a coroutine on
        /// the wall clock, on the reasoning that the two would agree because both were
        /// <see cref="SiegeTuning.FirstWaveAfter"/> long — which is arithmetic held in step by
        /// hand, and it came apart the first time anything held the run. A first-timer's tip
        /// holds the board (<c>RunHold.Teaching</c>) and so does the pause menu, the action bar's
        /// shop and the opening transition; none of them holds a wall clock, so the count ran out
        /// behind the panel and the player was handed a hill with no count-in at all. Read off
        /// this, the two cannot disagree on any frame, for any reason, without somebody changing
        /// this line — which is invariant 33g's bargain (a fact derived from a shape can never
        /// come apart from it), and 39j's about a cooldown that must not be payable by opening a
        /// panel.
        /// </para>
        /// <para>
        /// <b>Narrow on purpose: it is not "seconds until the next wave".</b> A hill the player
        /// has cleared musters the next wave at once, whatever <c>_rest</c> says
        /// (<see cref="Muster"/>, invariant 37k), so a general reading would be a number that
        /// lies from the second wave onward. The first wave is the one case the shortcut is
        /// explicitly guarded against, which is what makes this one honest.
        /// </para>
        /// </summary>
        public float BeforeFirstWave => _wave > 0 ? 0f : (_rest > 0f ? _rest : 0f);

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

        /// <summary>
        /// How many raiders this run still has to see off: what the level has not sent yet, plus
        /// everything alive right now.
        ///
        /// <para>
        /// <b>Derived from the state of the hill, never from a tally of kills, and the difference
        /// is a run that could not end.</b> It was <c>Goals - _felled</c> — the authored raider
        /// count less everything that has died — which is exact on every board whose raiders are
        /// all authored, and wrong the day one of them <em>makes</em> raiders. A bonecaller raises
        /// <see cref="SiegeTuning.RaisesInAll"/> creepers that <see cref="SiegeLayout.RaiderCount"/>
        /// has never heard of and that <c>Fell</c> counts like any other death, so on the shipped
        /// rung the kill tally passed the authored 27 <b>while the boss was still standing on
        /// 1,186 health with seven raiders walking</b>, and then went straight past it.
        /// </para>
        /// <para>
        /// <b>Both halves of that are bugs and only the second was reported.</b> The model reads
        /// the equality the frame it happens and so declared the level cleared mid-fight — which
        /// is what the hold simulation had been measuring on that rung, so its whole reading of it
        /// was of a run that stopped when 27 things had died. The <em>view</em> is not allowed to
        /// ask while the field is coming apart or while something is dying (<c>SiegeView.Judge</c>
        /// holds on <c>Busy</c> and <c>_felling</c>), so what a player met was the counter sliding
        /// past nought during the hold and never coming back to it: a cleared hill, a dead boss
        /// and a run that simply never ended. Reported from play in exactly those words.
        /// </para>
        /// <para>
        /// <b>The general rule is that a terminal reading has to be monotone</b> — an equality on
        /// a counter that can overshoot its target is not an ending, it is a coincidence that has
        /// to be observed on the exact frame it happens. This cannot overshoot: nothing may be
        /// raised onto a hill with nothing alive on it, because the only thing that raises is a
        /// raider.
        /// </para>
        /// <para>
        /// <b>The endless lane keeps the tally</b>, and that is not an exception dodging the rule:
        /// it has no ending of this shape at all (<see cref="IsFinished"/> asks the ward line
        /// there), its <see cref="Goals"/> is <c>int.MaxValue</c>, and what this figure is used
        /// for on that lane is the analytics reading <c>Goals - GoalsLeft</c>, which is how many
        /// raiders the run saw off.
        /// </para>
        /// <para>
        /// <b>What it costs is that <c>Goals - GoalsLeft</c> steps backwards when a caster
        /// raises</b>, and that is the truth rather than a defect: the hill really did just get
        /// four raiders longer. It is read in one place — <c>ProtoScreen</c>'s <c>lit</c> on the
        /// defeat record, since the victory record passes <see cref="Goals"/> for both — and it
        /// cannot go negative, because the only board that raises anything raises it from the last
        /// wave, by which point nothing is unsent.
        /// </para>
        /// </summary>
        public int GoalsLeft => Layout.IsEndless ? Goals - _felled : Unsent + Standing;

        /// <summary>How many raiders the level has authored and not yet mustered.</summary>
        int Unsent
        {
            get
            {
                int n = 0;
                for (int wave = _wave; wave < Layout.WaveCount; wave++) n += Layout.SizeOf(wave);

                return n;
            }
        }

        /// <summary>
        /// How many raiders are alive, wherever they are.
        ///
        /// <b>Not <see cref="OnTheHill"/>, which asks whether one has walked on yet.</b> A raider
        /// minted above the top of the hill is still a raider this run has to see off, so an
        /// ending that did not count it would fire in the gap between a wave mustering and its
        /// first body arriving.
        /// </summary>
        int Standing
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive) n++;

                return n;
            }
        }

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
