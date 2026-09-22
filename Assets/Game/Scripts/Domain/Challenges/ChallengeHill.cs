using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    public sealed class ChallengeRaider
    {
        public readonly int Id, Colour, MaxHealth;
        public int Health;

        /// <summary>Steps from the line. Nought is standing at it.</summary>
        public int Distance;

        public bool Alive => Health > 0;
        public bool AtTheLine => Alive && Distance <= 0;

        public ChallengeRaider(int id, int colour, int health, int distance)
        {
            Id = id;
            Colour = colour;
            MaxHealth = health;
            Health = health;
            Distance = distance;
        }
    }

    public sealed class ChallengeWard
    {
        public readonly int Colour, MaxHealth;
        public int Health;

        /// <summary>Bolts fed and not yet fired, because nothing of this colour was on the hill.</summary>
        public int Banked;

        public bool Alive => Health > 0;

        public ChallengeWard(int colour, int health)
        {
            Colour = colour;
            MaxHealth = health;
            Health = health;
        }
    }

    public enum ChallengeEventKind
    {
        /// <summary>A ward fired at a raider. <c>Amount</c> is the damage; <c>Ended</c> whether it fell.</summary>
        Bolt,

        /// <summary>A raider walked one step. <c>Amount</c> is its new distance.</summary>
        Stepped,

        /// <summary>A raider at the line hit a ward. <c>Amount</c> is the damage; <c>Ended</c> whether the ward fell.</summary>
        Struck,

        /// <summary>A wave's raider walked onto the top of the hill.</summary>
        Mustered,
    }

    /// <summary>
    /// One thing that happened on the hill, in the order it happened. The view replays the
    /// list; nothing here is read by a rule.
    /// </summary>
    public readonly struct ChallengeEvent
    {
        public readonly ChallengeEventKind Kind;
        public readonly int Raider, Ward, Amount;
        public readonly bool Ended;

        public ChallengeEvent(ChallengeEventKind kind, int raider, int ward, int amount, bool ended)
        {
            Kind = kind;
            Raider = raider;
            Ward = ward;
            Amount = amount;
            Ended = ended;
        }
    }

    /// <summary>
    /// The hill every challenge is fought on: four wards, one per colour, and raiders walking
    /// at them one step per move the player makes.
    ///
    /// <para>
    /// <b>Turn-based, which is the whole fusion.</b> The live mode's hill walks on a clock;
    /// this one walks when the player moves, so a puzzle can be thought about for as long as a
    /// puzzle wants and still costs something per move. Every quantity here is an integer over
    /// a finite list, so a challenge is countable (invariant 5d): a test can replay a solution
    /// and say exactly which turn the line would have fallen on.
    /// </para>
    /// <para>
    /// <b>A colour lock, as the live mode has one.</b> A ward fires only at raiders of its
    /// colour and a raider at the line strikes the ward of its colour (or the nearest standing
    /// one once that has fallen). That is what makes <em>which</em> colour a puzzle move feeds
    /// a decision rather than decoration — the count 5d asks for is "how many moves feed the
    /// colour that is closest", and it is never every move.
    /// </para>
    /// <para>
    /// <b>Fuel banks.</b> A bolt fed with nothing of its colour on the hill waits on the ward
    /// and fires the turn a target appears, so a move made early is never wasted — and so a
    /// puzzle that fires in bursts (a flood-filled corner, a cleared line) is paid in full.
    /// </para>
    /// <para>
    /// <b>The order within a turn is fire, step, strike, muster</b>, and it is the same order
    /// every time: the bolts a move earned land before the hill moves, a raider that reaches the
    /// line this turn strikes this turn, and a wave mustered this turn stands at the top and
    /// walks next turn. Nothing here reads a clock.
    /// </para>
    /// </summary>
    public sealed class ChallengeHill
    {
        public readonly ChallengeLine Line;
        public readonly int Length;

        readonly ChallengeWave[] _waves;
        readonly ChallengeWard[] _wards;
        readonly List<ChallengeRaider> _raiders = new List<ChallengeRaider>(16);

        int _nextId;
        int _turn;

        public ChallengeHill(ChallengeLine line, int length, ChallengeWave[] waves)
        {
            Line = line;
            Length = length < 1 ? 1 : length;
            _waves = waves ?? new ChallengeWave[0];

            _wards = new ChallengeWard[ChallengeColours.Count];
            for (int i = 0; i < _wards.Length; i++) _wards[i] = new ChallengeWard(i, line.Health);

            // What is on the hill before the first move.
            Muster(0, null);
        }

        public IReadOnlyList<ChallengeWard> Wards => _wards;
        public IReadOnlyList<ChallengeRaider> Raiders => _raiders;

        /// <summary>How many moves have been resolved.</summary>
        public int Turn => _turn;

        public bool LineStanding
        {
            get
            {
                for (int i = 0; i < _wards.Length; i++) if (_wards[i].Alive) return true;
                return false;
            }
        }

        public int WardsStanding
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _wards.Length; i++) if (_wards[i].Alive) n++;
                return n;
            }
        }

        public int Standing
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _raiders.Count; i++) if (_raiders[i].Alive) n++;
                return n;
            }
        }

        /// <summary>Whether every wave has mustered and every raider has fallen.</summary>
        public bool Cleared
        {
            get
            {
                if (Standing > 0) return false;
                for (int w = 0; w < _waves.Length; w++) if (_waves[w].Turn > _turn) return false;
                return true;
            }
        }

        /// <summary>The next wave still to come, or null.</summary>
        public ChallengeWave NextWave
        {
            get
            {
                for (int w = 0; w < _waves.Length; w++) if (_waves[w].Turn > _turn) return _waves[w];
                return null;
            }
        }

        public ChallengeRaider Find(int id)
        {
            for (int i = 0; i < _raiders.Count; i++) if (_raiders[i].Id == id) return _raiders[i];
            return null;
        }

        // ------------------------------------------------------------------ feeding
        /// <summary>Bank bolts on the ward of a colour. A dead ward takes nothing.</summary>
        public void Feed(int colour, int bolts)
        {
            if (colour < 0 || colour >= _wards.Length || bolts <= 0) return;
            if (!_wards[colour].Alive) return;

            _wards[colour].Banked += bolts;
        }

        // ------------------------------------------------------------------ one turn
        /// <summary>
        /// Resolve one move's worth of hill: fire what is banked, walk, strike, muster.
        /// </summary>
        public void Resolve(List<ChallengeEvent> into)
        {
            _turn++;

            Fire(into);
            Step(into);
            Strike(into);
            Muster(_turn, into);
        }

        void Fire(List<ChallengeEvent> into)
        {
            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                while (ward.Banked > 0)
                {
                    var target = FrontMost(ward.Colour);
                    if (target == null) break;

                    ward.Banked--;
                    target.Health -= Line.Damage;
                    if (target.Health < 0) target.Health = 0;

                    into?.Add(new ChallengeEvent(ChallengeEventKind.Bolt, target.Id, w, Line.Damage,
                                                 !target.Alive));
                }
            }
        }

        /// <summary>The living raider of a colour nearest the line; ties go to the older body.</summary>
        ChallengeRaider FrontMost(int colour)
        {
            ChallengeRaider best = null;
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!r.Alive || r.Colour != colour) continue;
                if (best == null || r.Distance < best.Distance) best = r;
            }
            return best;
        }

        void Step(List<ChallengeEvent> into)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!r.Alive || r.Distance <= 0) continue;

                r.Distance--;
                into?.Add(new ChallengeEvent(ChallengeEventKind.Stepped, r.Id, -1, r.Distance, false));
            }
        }

        void Strike(List<ChallengeEvent> into)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!r.AtTheLine) continue;

                int at = TargetWard(r.Colour);
                if (at < 0) return;

                var ward = _wards[at];
                ward.Health -= Line.Strike;
                if (ward.Health < 0) ward.Health = 0;

                // A ward that falls takes its bank with it: nothing fires from a broken post.
                if (!ward.Alive) ward.Banked = 0;

                into?.Add(new ChallengeEvent(ChallengeEventKind.Struck, r.Id, at, Line.Strike, !ward.Alive));
            }
        }

        /// <summary>The ward a raider of this colour hits: its own, else the nearest standing one.</summary>
        int TargetWard(int colour)
        {
            if (colour >= 0 && colour < _wards.Length && _wards[colour].Alive) return colour;

            int best = -1, bestGap = int.MaxValue;
            for (int i = 0; i < _wards.Length; i++)
            {
                if (!_wards[i].Alive) continue;
                int gap = i > colour ? i - colour : colour - i;
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        void Muster(int turn, List<ChallengeEvent> into)
        {
            for (int w = 0; w < _waves.Length; w++)
            {
                var wave = _waves[w];
                if (wave.Turn != turn) continue;

                for (int i = 0; i < wave.Raiders.Length; i++)
                {
                    var spec = wave.Raiders[i];
                    var raider = new ChallengeRaider(_nextId++, spec.Colour, spec.Health, Length);
                    _raiders.Add(raider);

                    into?.Add(new ChallengeEvent(ChallengeEventKind.Mustered, raider.Id, -1, Length, false));
                }
            }
        }
    }
}
