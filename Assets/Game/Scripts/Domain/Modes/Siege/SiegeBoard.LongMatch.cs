using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>The ladder's and the Infinite lane's long matches</b> (MODES.md 37fk,
    /// <see cref="SiegeLongMatch"/>): a run of exactly four merges into a <b>gem bomb</b> the player
    /// taps to blow up the square round it, and a run of five or more fires a <b>salvo</b> of
    /// rockets from the field at the hill.
    ///
    /// <para>
    /// <b>Shut on every board that does not forge, and that is one line.</b>
    /// <see cref="Lengthen"/> returns before it reads anything unless the rules switched the board
    /// on (<see cref="LongMatches"/>) and it holds the plain line - so the Shuffle lane (whose
    /// <c>power_gems</c> card is its own long-match payoff), the tutorial, the showcase and every
    /// gate and offline mirror resolve every swap exactly as they did before this file existed.
    /// </para>
    /// <para>
    /// <b>The bomb is a charm the field makes</b> (<see cref="SiegeCharm.Bomb"/>), so it rides its
    /// gem, falls and swaps with it through the parallel array, and goes off through
    /// <c>Spring</c> whatever takes it - a match it lines up in, a lance, a nova, another bomb, or
    /// the player's own tap (<see cref="Detonate"/>). Its blast is folded into the beat that set it
    /// off, for the lance's reason: the gems it takes clear, pay their own colours and are drawn
    /// going in one picture.
    /// </para>
    /// <para>
    /// <b>The salvo is booked, never applied on the swap</b> (invariant 37s): the model resolves a
    /// turn in an instant and the view spends most of a second drawing it, so each rocket carries
    /// the raider it was aimed at and lands <see cref="SiegeLongMatch.RocketLands"/> later, through
    /// the one door every hurt goes through (<c>Wound</c>, <c>Fell</c>).
    /// </para>
    /// <para>
    /// <b>Nothing here draws from any stream</b> (invariant 41): which run forges, where, what a
    /// blast takes and whom a rocket is aimed at are all read off the field and the hill, so the
    /// refill deals exactly the gems it always would have.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Whether a long match pays off on this board. <b>Set by the rules and by nothing else</b>
        /// (<c>SiegeRules.Fresh</c>, true on the ladder and the Infinite lane); false on every
        /// board built directly, which is every gate, mirror and fixture that does not ask for it.
        /// </summary>
        public bool LongMatches { get; internal set; }

        /// <summary>
        /// Whether this board forges at all: switched on, and holding the plain line - the second
        /// half is the Shuffle lane's own lock, independent of the first.
        /// </summary>
        bool Lengthens => LongMatches && _boosts.IsIdentity;

        /// <summary>A salvo rocket in the air: the id its beat reported, whom it was aimed at, and how long until it lands.</summary>
        struct Salvo
        {
            public int Id, Raider;
            public float In;
        }

        /// <summary>Rockets in the air, in the order they were fired - which is the order they land in.</summary>
        readonly List<Salvo> _salvos = new List<Salvo>(SiegeLongMatch.MostRockets);

        /// <summary>The last id a rocket was fired under. Only ever climbs, so a landing names exactly one flight.</summary>
        int _rocketIds;

        /// <summary>Scratch for <see cref="Aims"/>: the bodies a salvo may be aimed at, nearest the line first.</summary>
        readonly List<SiegeRaider> _aims = new List<SiegeRaider>(16);

        // ------------------------------------------------------------------ on the beat
        /// <summary>
        /// Folds this beat's long matches into it: every run of exactly
        /// <see cref="SiegeLongMatch.BombRun"/> merges into a gem bomb, and every run of
        /// <see cref="SiegeLongMatch.SalvoRun"/> or more fires a salvo. Before <c>Spring</c>, so the
        /// cell a bomb is forming in is spared by every charm this beat sets off.
        /// </summary>
        void Lengthen(HashSet<int> hit, SiegeBeat beat)
        {
            if (!Lengthens) return;

            // A collapsed singularity has taken the whole field: nothing is left to forge.
            if (beat.Swallowed) return;

            // The ordinary beat is a three and nothing else; asked before the field is scanned.
            if (hit.Count < SiegeLongMatch.BombRun) return;

            var strokes = Strokes();

            for (int s = 0; s < strokes.Count; s++)
            {
                var stroke = strokes[s];
                int length = stroke.Cells.Length;

                if (length >= SiegeLongMatch.SalvoRun) Fire(stroke, beat);
                else if (length == SiegeLongMatch.BombRun) Merge(stroke, hit, beat);
            }
        }

        /// <summary>
        /// One run of four merging into a gem bomb: the bomb stands where the player moved when the
        /// swap made the run, else in the run's middle (<c>StandPower</c>, the forge every power
        /// gem goes through), the other three clear and pay as any match does, and the merge pays
        /// <see cref="SiegeLongMatch.MergeGems"/> more of the run's colour on top - the owner's
        /// rule that a merge is never free.
        /// </summary>
        void Merge(Stroke stroke, HashSet<int> hit, SiegeBeat beat)
        {
            if (!StandPower(stroke.Cells, stroke.Colour, SiegeCharm.Bomb, -1, hit, beat)) return;

            int ward = Layout.WardOf(SiegeLayout.Letters[stroke.Colour]);
            if (ward < 0) return;

            // Booked with the beat's own fuel, so it lands when the run's motes do (37s).
            beat.Fuel[ward] += SiegeLongMatch.MergeGems * _boosts.FuelGem(SiegeTuning.FuelPerGem, beat.Depth);
        }

        /// <summary>
        /// One run of five or more firing its salvo: <see cref="SiegeLongMatch.RocketsFor"/> rockets,
        /// each leaving from a cell spread along the run and aimed at the hill (<see cref="Aims"/>).
        /// The run itself clears and pays as any match does - the rockets are on top.
        /// </summary>
        void Fire(Stroke stroke, SiegeBeat beat)
        {
            int rockets = SiegeLongMatch.RocketsFor(stroke.Cells.Length);
            if (rockets <= 0) return;

            Aims();

            float lands = SiegeLongMatch.RocketLands(beat.Depth);
            int last = stroke.Cells.Length - 1;

            for (int i = 0; i < rockets; i++)
            {
                // Spread evenly from one end of the run to the other, so a salvo of three leaves
                // from both ends and the middle.
                int cell = stroke.Cells[rockets == 1 ? last / 2 : i * last / (rockets - 1)];

                // **Nearest the line first, one body each while there are enough**, then round
                // again: what a rocket answers is the raider about to swing. An empty hill aims at
                // nothing and the rocket picks whoever is there when it lands.
                int aim = _aims.Count > 0 ? _aims[i % _aims.Count].Id : -1;

                int id = ++_rocketIds;
                _salvos.Add(new Salvo { Id = id, Raider = aim, In = lands });
                beat.Rockets.Add(new SiegeRocket(id, cell, aim, stroke.Colour));
            }
        }

        /// <summary>
        /// Fills <see cref="_aims"/> with every raider a rocket may strike, nearest the line first
        /// and by id on a tie - a stated order, so two runtimes aim the same salvo the same way.
        /// </summary>
        void Aims()
        {
            _aims.Clear();

            for (int i = 0; i < _raiders.Count; i++)
                if (Touchable(_raiders[i])) _aims.Add(_raiders[i]);

            _aims.Sort((a, b) =>
            {
                int by = b.March.CompareTo(a.March);
                return by != 0 ? by : a.Id.CompareTo(b.Id);
            });
        }

        // ------------------------------------------------------------------ the tap
        /// <summary>
        /// Whether tapping <paramref name="cell"/> would set off a gem bomb: one is standing there
        /// and the run can still be played.
        /// </summary>
        public bool Detonates(int cell)
            => cell >= 0 && cell < _charms.Length && SiegeCharms.IsBomb(_charms[cell]) && AnyMove;

        /// <summary>
        /// Sets off the gem bomb in <paramref name="cell"/> and resolves everything that follows,
        /// fuelling the wards as it goes - or answers null when there is no bomb there.
        ///
        /// <para>
        /// <b>A turn like a swap, and charged like one</b> (the view charges the run a move for
        /// it, as it does a swap): the bomb is the first beat, its blast clears the square round it
        /// and every gem that goes pays its own colour, and whatever falls into line afterwards is
        /// the ordinary cascade. Nothing is exchanged, so <see cref="SiegeTurn.A"/> and
        /// <see cref="SiegeTurn.B"/> are both the bomb's cell and <see cref="SiegeTurn.Tapped"/> says
        /// so.
        /// </para>
        /// </summary>
        public SiegeTurn Detonate(int cell)
        {
            if (!Detonates(cell)) return null;

            var turn = new SiegeTurn { A = cell, B = cell, Tapped = true };

            // No swap made this turn's runs, so a bomb a cascade forges stands mid-run.
            _swapA = -1;
            _swapB = -1;

            Cascade(turn, cell);

            turn.Shuffled = Settle();
            return turn;
        }

        // ------------------------------------------------------------------ the blast
        /// <summary>
        /// A gem bomb going off: every cell within <see cref="SiegeLongMatch.BombReach"/> of it,
        /// added to what this beat takes. Through <c>Taken</c>, the door a rocket's line goes
        /// through, so a hole is skipped, a forming gem is spared, a prism is paid as the bomb's
        /// colour and a charm the blast reaches goes off in turn.
        /// </summary>
        void Burst(int cell, int colour, HashSet<int> hit, List<int> queue)
        {
            int cx = cell % Width, cy = cell / Width;
            int reach = SiegeLongMatch.BombReach;

            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue;

                    Taken(IndexOf(x, y), colour, hit, queue);
                }
        }

        // ------------------------------------------------------------------ the landing
        /// <summary>
        /// Lands whatever salvo rockets have finished their flight, in the order they were fired.
        /// From <c>Land</c>, after the fuel, so a ward fed by the run that fired them is fed before
        /// the hill is thinned.
        /// </summary>
        void Salvoes(float dt)
        {
            for (int i = 0; i < _salvos.Count;)
            {
                var rocket = _salvos[i];
                rocket.In -= dt;

                if (rocket.In > 0f)
                {
                    _salvos[i] = rocket;
                    i++;
                    continue;
                }

                _salvos.RemoveAt(i);
                Impact(rocket);
            }
        }

        /// <summary>
        /// One rocket arriving: on the body it was aimed at, or the one nearest the line if that one
        /// has gone, or on the ground when the hill is empty - <b>reported either way</b>, because a
        /// payoff that vanished in silence would read as a broken match.
        ///
        /// <para>
        /// <b>What it takes scales with the body it lands on</b>
        /// (<see cref="SiegeLongMatch.RocketDamageAt"/>), and a bulwark's shield soaks it as it soaks
        /// every hit no ward of its colour fired - the armour is what a bulwark <em>is</em>.
        /// </para>
        /// </summary>
        void Impact(Salvo rocket)
        {
            SiegeRaider target = null;

            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Id == rocket.Raider && Touchable(_raiders[i])) { target = _raiders[i]; break; }

            if (target == null) target = Foremost();

            if (target == null)
            {
                _report.Rocketed.Add(new SiegeRocketHit(rocket.Id, -1, 0, false));
                return;
            }

            int damage = SiegeLongMatch.RocketDamageAt(target.Surge.HealthTenths);

            if (target.Kind == SiegeKind.Bulwark)
                damage = Math.Max(1, damage * SiegeTuning.ShieldSoakTenths / 10);

            int took = Wound(target, damage);
            bool killed = took > 0 && Fell(target);

            _report.Rocketed.Add(new SiegeRocketHit(rocket.Id, target.Id, took, killed));
        }

        /// <summary>Rockets still in the air. Nought on every board that forges nothing.</summary>
        public int RocketsInFlight => _salvos.Count;
    }
}
