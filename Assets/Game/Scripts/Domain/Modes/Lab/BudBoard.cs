using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>What one cell did on the wave it did it.</summary>
    public enum BudPulseKind
    {
        /// <summary>A flower going off as part of a bunch.</summary>
        Burst = 0,

        /// <summary>
        /// A cocoon taking a crack and holding.
        ///
        /// <b>It was reported by nothing at all, and that is why it is here.</b> A cocoon that
        /// takes its first of two cracks changed one ring's alpha on the next repaint — so the
        /// single most encouraging thing that can happen short of freeing somebody (you got
        /// halfway) arrived as a colour appearing quietly behind thirteen flowers going off. A
        /// wave the player cannot see is a wave that pays out nothing, which is this mode's
        /// whole argument, and the view cannot draw one it is never told about.
        /// </summary>
        Crack = 1,

        /// <summary>A cocoon opening and the critter coming out.</summary>
        Freed = 2,
    }

    /// <summary>One cell doing something, and which wave of the chain did it.</summary>
    public readonly struct BudPulse
    {
        public readonly int Cell;

        /// <summary>0 for the bunch the tap made, 1 for what that set off, and so on.</summary>
        public readonly int Wave;

        /// <summary>The colour it went off in. <c>Energy.None</c> on anything but a burst.</summary>
        public readonly int Colour;

        public readonly BudPulseKind Kind;

        /// <summary>
        /// How many flowers were in the bunch this one belonged to. 0 on a cocoon.
        ///
        /// <b>Reported rather than counted back, for the reason every other reading here is.</b>
        /// A bunch is a connected blob of one colour, so working its size out from the pulses
        /// would mean a flood fill over a board that has already moved on — <c>BudWash</c>'s
        /// paragraph, in the other direction. It decides how loud one burst is drawn
        /// (<c>BudChain.Blast</c>), and three alike going off is a different event from nine.
        /// </summary>
        public readonly int Bunch;

        public BudPulse(int cell, int wave, int colour, BudPulseKind kind, int bunch = 0)
        {
            Cell = cell;
            Wave = wave;
            Colour = colour;
            Kind = kind;
            Bunch = bunch;
        }

        /// <summary>Whether this was a cocoon opening rather than a flower bursting.</summary>
        public bool Freed => Kind == BudPulseKind.Freed;

        /// <summary>Whether this was a cocoon taking a crack and holding.</summary>
        public bool Cracked => Kind == BudPulseKind.Crack;
    }

    /// <summary>
    /// One flower taking colour and changing, and which wave did it.
    ///
    /// <b>Reported rather than re-derived.</b> The view has to draw a chain *travelling* — colour
    /// leaving a burst, landing on the flower beside it, and that flower turning — which means it
    /// needs the colour at every wave rather than only at the end. Working that out from the
    /// pulses would be a second copy of the rule living in a <c>MonoBehaviour</c> (invariant 9a).
    /// </summary>
    public readonly struct BudWash
    {
        public readonly int Cell;
        public readonly int Wave;

        /// <summary>What it turned into. -1 for the wave means the player's own tap.</summary>
        public readonly int To;

        public BudWash(int cell, int wave, int to)
        {
            Cell = cell;
            Wave = wave;
            To = to;
        }
    }

    /// <summary>What one tap came to. The reading a preview and a real tap share.</summary>
    public readonly struct BudChainResult
    {
        public readonly int Burst;
        public readonly int Waves;
        public readonly int Freed;
        public readonly int Cracked;

        /// <summary>The biggest single bunch that went off. What the celebration is pitched at.</summary>
        public readonly int Biggest;

        public BudChainResult(int burst, int waves, int freed, int cracked, int biggest)
        {
            Burst = burst;
            Waves = waves;
            Freed = freed;
            Cracked = cracked;
            Biggest = biggest;
        }

        public static readonly BudChainResult Nothing = new BudChainResult(0, 0, 0, 0, 0);

        public bool Any => Burst > 0;

        public override string ToString()
            => $"{Burst} burst, {Waves} wave(s), {Freed} freed, {Cracked} cracked";
    }

    /// <summary>
    /// <b>The grove as it stands.</b> What colour everything is, and who is still shut in.
    ///
    /// <para>
    /// <b>One tap is a mix, and the mix is what starts everything.</b> The colour in hand is added
    /// to the flower you tap — red with green in hand becomes yellow — and then the grove settles:
    /// any bunch of three or more touching flowers of one colour <b>bursts</b>, and a burst
    /// <b>washes its colour into every flower touching it</b>. Those flowers change, which can
    /// make new bunches, which burst and wash further. That is the chain, and it is the whole
    /// mode.
    /// </para>
    /// <para>
    /// <b>Mixing only ever adds channels, so the grove is always being driven toward going off.</b>
    /// Nothing ever becomes less mixed, so every tap moves the board closer to a bunch and closer
    /// to white — which is what makes this chill rather than fiddly. The player is not fighting
    /// the board; they are choosing where it goes off.
    /// </para>
    /// <para>
    /// <b>And it cannot run away.</b> Every wave removes at least three flowers and nothing is
    /// ever added, so a chain is bounded by the grove it started on and the settle always
    /// terminates. That is the property the two designs before this one could not manage: one
    /// asked the player to predict four beats ahead, and the other froze into a position where no
    /// input changed anything.
    /// </para>
    /// </summary>
    public sealed class BudBoard
    {
        public readonly BudLayout Layout;

        readonly BudGround[] _ground;
        readonly int[] _value;

        readonly List<int> _bunch = new List<int>(32);
        readonly List<int> _queue = new List<int>(32);
        readonly List<int> _cracked = new List<int>(8);
        readonly List<int> _washed = new List<int>(32);
        readonly int[] _wash;
        readonly bool[] _seen;
        readonly List<int> _beside = new List<int>(4);

        public BudBoard(BudLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _ground = layout.Standing();
            _value = layout.Values();
            _wash = new int[layout.Count];
            _seen = new bool[layout.Count];
        }

        public BudBoard(BudBoard other)
        {
            Layout = other.Layout;

            _ground = new BudGround[other._ground.Length];
            _value = new int[other._value.Length];
            Array.Copy(other._ground, _ground, _ground.Length);
            Array.Copy(other._value, _value, _value.Length);

            _wash = new int[_ground.Length];
            _seen = new bool[_ground.Length];
        }

        public int Width => Layout.Width;
        public int Height => Layout.Height;
        public int Count => _ground.Length;
        public int Index(int x, int y) => Layout.Index(x, y);

        public BudGround At(int index) => _ground[index];
        public int ValueAt(int index) => _value[index];

        public bool IsFlower(int index) => _ground[index] == BudGround.Flower;
        public bool IsCocoon(int index) => _ground[index] == BudGround.Cocoon;

        public int Flowers
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++) if (IsFlower(i)) n++;
                return n;
            }
        }

        public int Shut
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++) if (IsCocoon(i)) n++;
                return n;
            }
        }

        public bool IsFinished => Shut == 0;

        /// <summary>
        /// Whether a tap could change anything at all — with any colour the basket still deals,
        /// not merely with the one in hand.
        ///
        /// <para>
        /// <b>A flower left is not a move left, and that distinction is what the colour rule
        /// added.</b> Under the ripeness rule the two were the same question: a bud was always
        /// tappable, so "any bud left" answered it. A white flower holds every channel, so it can
        /// never be mixed into by anything — a grove of nothing but white has flowers on it and no
        /// legal tap in it, which is a board that can be neither won nor ended. That is exactly
        /// the state invariant 20g forbids, and reading <see cref="Flowers"/> here is how it got
        /// in.
        /// </para>
        /// <para>
        /// It asks about the <em>whole</em> basket because the basket repeats for ever, so a
        /// colour that cannot be spent now comes round again — and because the answer decides
        /// whether it would be honest to sell a continue (invariant 28f), so it must under-report
        /// and never over-report.
        /// </para>
        /// </summary>
        public bool AnyMove()
        {
            var deal = Layout.Deal;
            if (deal == null) return AnyFlower;

            for (int c = 0; c < deal.Count; c++)
            {
                int colour = deal.At(c);

                for (int i = 0; i < _ground.Length; i++)
                    if (CanTap(i, colour)) return true;
            }

            return false;
        }

        /// <summary>Whether anything is standing that a chain could still go through.</summary>
        public bool AnyFlower => Flowers > 0;

        /// <summary>
        /// Whether this flower would actually change if the colour in hand were put on it.
        ///
        /// A tap that mixes nothing in is refused rather than swallowed: it would spend a tap and
        /// leave the grove exactly as it was, which is the one move a player can make by accident
        /// and never means to.
        /// </summary>
        public bool CanTap(int cell, int colour)
        {
            if (cell < 0 || cell >= _ground.Length) return false;
            if (_ground[cell] != BudGround.Flower) return false;

            return (_value[cell] | colour) != _value[cell];
        }

        /// <summary>What the colour in hand would make of this flower. For the preview.</summary>
        public int Mixed(int cell, int colour)
            => _ground[cell] == BudGround.Flower ? _value[cell] | colour : Energy.None;

        public BudChainResult Preview(int cell, int colour) => Preview(cell, colour, null, null);

        public BudChainResult Preview(int cell, int colour, List<BudPulse> pulses,
                                      List<BudWash> washes = null)
        {
            if (!CanTap(cell, colour))
            {
                pulses?.Clear();
                washes?.Clear();
                return BudChainResult.Nothing;
            }

            return new BudBoard(this).Tap(cell, colour, pulses, washes);
        }

        /// <summary>
        /// The mix, and the whole chain it sets off.
        ///
        /// Resolved wave by wave rather than all at once, because <em>when</em> a cell goes is
        /// what the view animates and what the count on screen climbs through — a chain reported
        /// as one number would draw as one flash however far it ran.
        /// </summary>
        public BudChainResult Tap(int cell, int colour, List<BudPulse> pulses,
                                  List<BudWash> washes = null)
        {
            pulses?.Clear();
            washes?.Clear();

            if (!CanTap(cell, colour)) return BudChainResult.Nothing;

            _value[cell] |= colour;
            washes?.Add(new BudWash(cell, -1, _value[cell]));

            return Settle(pulses, washes);
        }

        /// <summary>
        /// Everything the grove does on its own once a colour has moved: bunches go off, their
        /// colour washes outward, and whatever that makes goes off in turn.
        /// </summary>
        public BudChainResult Settle(List<BudPulse> pulses, List<BudWash> washes)
        {
            int burst = 0, waves = 0, freed = 0, cracked = 0, biggest = 0;

            while (true)
            {
                Array.Clear(_wash, 0, _wash.Length);
                _cracked.Clear();
                _washed.Clear();
                _queue.Clear();

                Array.Clear(_seen, 0, _seen.Length);
                bool any = false;

                for (int i = 0; i < _ground.Length; i++)
                {
                    if (_seen[i] || _ground[i] != BudGround.Flower) continue;

                    Bunch(i);
                    if (_bunch.Count < BudLayout.Bunch) continue;

                    any = true;
                    if (_bunch.Count > biggest) biggest = _bunch.Count;

                    int colour = _value[_bunch[0]];

                    // What the bunch touches is read before any of it is taken away, so the wash
                    // goes out from where the flowers were standing.
                    for (int k = 0; k < _bunch.Count; k++)
                    {
                        Layout.Beside(_bunch[k], _beside);

                        for (int j = 0; j < _beside.Count; j++)
                        {
                            int nb = _beside[j];

                            if (_ground[nb] == BudGround.Cocoon)
                            {
                                if (!_cracked.Contains(nb)) _cracked.Add(nb);
                            }
                            else if (_ground[nb] == BudGround.Flower)
                            {
                                // Every flower it touches, with no cleverness about which. It
                                // used to say `&& !_seen[nb]`, meaning to skip a flower that is
                                // itself bursting — but `_seen` is the flood fill's *visited*
                                // marker, so it also covered every flower already scanned as part
                                // of a group of one or two that was discarded. Those are ordinary
                                // flowers, and whether one was scanned before or after the bunch
                                // is an accident of index order: the wash simply stopped in one
                                // direction and not the other. The guard was redundant as well as
                                // wrong, because the application loop below re-checks the ground.
                                if (_wash[nb] == Energy.None) _washed.Add(nb);
                                _wash[nb] |= colour;
                            }
                        }
                    }

                    for (int k = 0; k < _bunch.Count; k++) _queue.Add(_bunch[k]);
                    for (int k = 0; k < _bunch.Count; k++)
                        pulses?.Add(new BudPulse(_bunch[k], waves, colour,
                                                 BudPulseKind.Burst, _bunch.Count));
                }

                if (!any) break;

                for (int k = 0; k < _queue.Count; k++)
                {
                    _ground[_queue[k]] = BudGround.Bare;
                    _value[_queue[k]] = Energy.None;
                    burst++;
                }

                // One crack per cocoon per wave, however many bunches of that wave touched it.
                for (int k = 0; k < _cracked.Count; k++)
                {
                    int at = _cracked[k];
                    _value[at]--;

                    if (_value[at] > 0)
                    {
                        cracked++;
                        pulses?.Add(new BudPulse(at, waves, Energy.None, BudPulseKind.Crack));
                        continue;
                    }

                    _ground[at] = BudGround.Bare;
                    _value[at] = Energy.None;
                    freed++;
                    pulses?.Add(new BudPulse(at, waves, Energy.None, BudPulseKind.Freed));
                }

                // Then the colour goes out. A flower touched by two bunches at once takes both,
                // which is where the long chains come from.
                for (int k = 0; k < _washed.Count; k++)
                {
                    int at = _washed[k];
                    if (_ground[at] != BudGround.Flower) continue;

                    int was = _value[at];
                    _value[at] |= _wash[at];

                    if (_value[at] != was) washes?.Add(new BudWash(at, waves, _value[at]));
                }

                waves++;
            }

            return new BudChainResult(burst, waves, freed, cracked, biggest);
        }

        /// <summary>Every flower of one colour joined to this one. Fills <c>_bunch</c>.</summary>
        void Bunch(int from)
        {
            _bunch.Clear();
            _bunch.Add(from);
            _seen[from] = true;

            int colour = _value[from];

            for (int head = 0; head < _bunch.Count; head++)
            {
                Layout.Beside(_bunch[head], _beside);

                for (int j = 0; j < _beside.Count; j++)
                {
                    int nb = _beside[j];
                    if (_seen[nb]) continue;
                    if (_ground[nb] != BudGround.Flower || _value[nb] != colour) continue;

                    _seen[nb] = true;
                    _bunch.Add(nb);
                }
            }
        }

        /// <summary>Whether anything is already in a bunch. An authored grove must start settled.</summary>
        public bool AnyBunch()
        {
            Array.Clear(_seen, 0, _seen.Length);

            for (int i = 0; i < _ground.Length; i++)
            {
                if (_seen[i] || _ground[i] != BudGround.Flower) continue;

                Bunch(i);
                if (_bunch.Count >= BudLayout.Bunch) return true;
            }

            return false;
        }

        /// <summary>The state as a string, for the solver's dedup.</summary>
        public void KeyInto(char[] into, out int length)
        {
            for (int i = 0; i < _ground.Length; i++)
            {
                switch (_ground[i])
                {
                    case BudGround.Flower: into[i] = Energy.Letter(_value[i]); break;
                    case BudGround.Cocoon: into[i] = (char)('a' + _value[i]); break;
                    case BudGround.Stone: into[i] = '#'; break;
                    default: into[i] = '.'; break;
                }
            }

            length = _ground.Length;
        }

        public void Save(BudGround[] ground, int[] value)
        {
            Array.Copy(_ground, ground, _ground.Length);
            Array.Copy(_value, value, _value.Length);
        }

        public void Restore(BudGround[] ground, int[] value)
        {
            Array.Copy(ground, _ground, _ground.Length);
            Array.Copy(value, _value, _value.Length);
        }
    }
}
