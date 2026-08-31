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

        /// <summary>
        /// Whether this is the grove ripening one between taps rather than a bunch washing its
        /// colour into a neighbour.
        ///
        /// <para>
        /// <b>Reported because the two are the same event to the model and nothing like the same
        /// event to the player.</b> A wash happens beside a bunch that has just gone off, so its
        /// cause is on screen a tenth of a second earlier. A ripen has no cause anywhere near it
        /// — it is <see cref="BudBoard.Creep"/> leaning the grove toward the player, and it can
        /// land right across the board from the tap. Drawn identically, which is how it shipped,
        /// it reads as a flower changing colour for no reason: reported as
        /// <em>"I tap on a flower, but another far flower's colour changes"</em>, with the
        /// player unsure whether it was a bug. It is not, and invariant 20g is the rule it was
        /// breaking — a mechanic the board cannot show is one the player is always being
        /// surprised by.
        /// </para>
        /// </summary>
        public readonly bool Ripened;

        public BudWash(int cell, int wave, int to, bool ripened = false)
        {
            Cell = cell;
            Wave = wave;
            To = to;
            Ripened = ripened;
        }
    }

    /// <summary>
    /// One thing moving down the grove, so the view can draw it falling.
    ///
    /// <para>
    /// <b>Reported rather than re-derived, for <see cref="BudWash"/>'s reason.</b> By the time
    /// anything is animated the model is already at the end of the chain, so "what was standing
    /// here before" is a question the board can no longer answer. A drop carries where it came
    /// from and what it is, and a new flower carries <see cref="From"/> of -1 — which the view
    /// draws as arriving from above the top row.
    /// </para>
    /// </summary>
    public readonly struct BudDrop
    {
        /// <summary>Where it came to rest.</summary>
        public readonly int Cell;

        /// <summary>Where it fell from, or -1 for a flower that grew in along the top.</summary>
        public readonly int From;

        public readonly int Wave;

        /// <summary>What is standing there now: a colour mask, or cracks if it is a cocoon.</summary>
        public readonly int Value;

        public readonly bool Cocoon;

        public BudDrop(int cell, int from, int wave, int value, bool cocoon)
        {
            Cell = cell;
            From = from;
            Wave = wave;
            Value = value;
            Cocoon = cocoon;
        }

        public bool Grew => From < 0;
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

        /// <summary>
        /// How many flowers have grown in so far, which is where the strip is up to.
        ///
        /// <b>Part of the position, not decoration.</b> Two groves that look identical but have
        /// taken a different number of flowers off the strip will grow different ones next, so
        /// the solver's key carries this — see <see cref="KeyInto"/>.
        /// </summary>
        public int Grown { get; private set; }

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
            Grown = other.Grown;
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

            // **White is the one flower that takes no colour and is therefore the best tap on the
            // board.** It used to be the opposite: a flower holding every channel could not be
            // mixed into, so it was a dead cell and a mistake the player had made — the one state
            // in the mode that punished them for playing it well. Tapping it now sets it off
            // (see <see cref="Bomb"/>), which turns the trap into the reward and gives every
            // grove an obvious, spectacular button without adding a single new object to the
            // board.
            if (IsBomb(cell)) return true;

            return (_value[cell] | colour) != _value[cell];
        }

        /// <summary>
        /// Whether this cell is a white flower, which on a living grove is a bomb.
        ///
        /// <b>Gated on <see cref="BudLayout.Grows"/> with the other three.</b> Falling, growing,
        /// the bomb and the creep were commissioned together and arrive together: the strip is
        /// what says a grove is <em>alive</em>, and a grove without one behaves exactly as this
        /// mode shipped — which is not politeness to old content, it is what keeps eight vector
        /// cases pinning the base rule (mix, burst, wash) in isolation from everything built on
        /// top of it.
        /// </summary>
        public bool IsBomb(int cell)
            => Layout.Grows && cell >= 0 && cell < _ground.Length
            && _ground[cell] == BudGround.Flower && _value[cell] == Energy.All;

        /// <summary>What the colour in hand would make of this flower. For the preview.</summary>
        public int Mixed(int cell, int colour)
            => _ground[cell] == BudGround.Flower ? _value[cell] | colour : Energy.None;

        public BudChainResult Preview(int cell, int colour) => Preview(cell, colour, null, null);

        public BudChainResult Preview(int cell, int colour, List<BudPulse> pulses,
                                      List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            if (!CanTap(cell, colour))
            {
                pulses?.Clear();
                washes?.Clear();
                drops?.Clear();
                return BudChainResult.Nothing;
            }

            return new BudBoard(this).Tap(cell, colour, pulses, washes, drops);
        }

        /// <summary>
        /// The mix, and the whole chain it sets off.
        ///
        /// Resolved wave by wave rather than all at once, because <em>when</em> a cell goes is
        /// what the view animates and what the count on screen climbs through — a chain reported
        /// as one number would draw as one flash however far it ran.
        /// </summary>
        public BudChainResult Tap(int cell, int colour, List<BudPulse> pulses,
                                  List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            pulses?.Clear();
            washes?.Clear();
            drops?.Clear();

            if (!CanTap(cell, colour)) return BudChainResult.Nothing;

            if (IsBomb(cell)) return Bomb(cell, pulses, washes, drops);

            _value[cell] |= colour;
            washes?.Add(new BudWash(cell, -1, _value[cell]));

            return Settle(pulses, washes, drops, colour);
        }

        /// <summary>
        /// A white flower going off: everything in the square around it bursts at once.
        ///
        /// <para>
        /// <b>Three by three rather than a wash</b>, because it has to read as a different event
        /// from a bunch and not as a very good one. A wash would spread colour and set off
        /// whatever it completed, which is what every ordinary burst already does; clearing a
        /// block is the one thing nothing else on this board does, and it is what the player is
        /// looking for once they know it is there.
        /// </para>
        /// <para>
        /// It is reported as wave nought with a bunch of nine, so the view draws it as the
        /// biggest kind of burst there is without being told about it separately.
        /// </para>
        /// </summary>
        BudChainResult Bomb(int cell, List<BudPulse> pulses, List<BudWash> washes,
                            List<BudDrop> drops)
        {
            int x = cell % Width, y = cell / Width;
            _blast.Clear();

            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int a = x + dx, b = y + dy;
                if (a < 0 || a >= Width || b < 0 || b >= Height) continue;

                int at = b * Width + a;
                if (_ground[at] == BudGround.Flower) _blast.Add(at);
            }

            int burst = 0, freed = 0, cracked = 0;

            // What it touches is read before any of it is taken away, exactly as a wave's wash is.
            _cracked.Clear();
            for (int k = 0; k < _blast.Count; k++)
            {
                Layout.Beside(_blast[k], _beside);
                for (int j = 0; j < _beside.Count; j++)
                    if (_ground[_beside[j]] == BudGround.Cocoon && !_cracked.Contains(_beside[j]))
                        _cracked.Add(_beside[j]);
            }

            for (int k = 0; k < _blast.Count; k++)
            {
                pulses?.Add(new BudPulse(_blast[k], 0, _value[_blast[k]],
                                         BudPulseKind.Burst, _blast.Count));
                _ground[_blast[k]] = BudGround.Bare;
                _value[_blast[k]] = Energy.None;
                burst++;
            }

            Crack(pulses, 0, ref freed, ref cracked);
            Fall(drops, 0);

            var after = Settle(pulses, washes, drops, Energy.None, 1);

            return new BudChainResult(burst + after.Burst, 1 + after.Waves,
                                      freed + after.Freed, cracked + after.Cracked,
                                      after.Biggest > _blast.Count ? after.Biggest : _blast.Count);
        }

        readonly List<int> _blast = new List<int>(9);

        /// <summary>
        /// Everything the grove does on its own once a colour has moved: bunches go off, their
        /// colour washes outward, and whatever that makes goes off in turn.
        /// </summary>
        public BudChainResult Settle(List<BudPulse> pulses, List<BudWash> washes,
                                    List<BudDrop> drops = null, int spent = Energy.None,
                                    int from = 0)
        {
            int burst = 0, freed = 0, cracked = 0, biggest = 0;
            int waves = from;

            // See BudLayout.MostWaves: once the grove regrows, "every wave removes flowers and
            // nothing is ever added" stops being true and the settle needs a bound of its own.
            while (waves - from < BudLayout.MostWaves)
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

                Crack(pulses, waves, ref freed, ref cracked);

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

                // **And then the grove falls, and grows.** Everything above a hole slides down
                // into it and new flowers arrive along the top — so the next turn of this loop is
                // looking at a full board rather than a thinner one. That single step is why a
                // chain here compounds instead of running out: the flowers that drop into a
                // burst's own hole can be the ones that set off the wave after it.
                Fall(drops, waves);

                waves++;
            }

            // **And only now does the grove grow back.** Falling happens inside the chain, where
            // it compounds; growing happens once, after it has stopped. See <see cref="Grow"/> —
            // that ordering is what keeps a cascade provably finite.
            Grow(drops, waves);
            Creep(spent, washes, waves);

            return new BudChainResult(burst, waves - from, freed, cracked, biggest);
        }

        /// <summary>One crack per cocoon per wave, however many bunches of that wave touched it.</summary>
        void Crack(List<BudPulse> pulses, int wave, ref int freed, ref int cracked)
        {
            for (int k = 0; k < _cracked.Count; k++)
            {
                int at = _cracked[k];
                _value[at]--;

                if (_value[at] > 0)
                {
                    cracked++;
                    pulses?.Add(new BudPulse(at, wave, Energy.None, BudPulseKind.Crack));
                    continue;
                }

                _ground[at] = BudGround.Bare;
                _value[at] = Energy.None;
                freed++;
                pulses?.Add(new BudPulse(at, wave, Energy.None, BudPulseKind.Freed));
            }
        }

        /// <summary>
        /// Everything slides down into the holes under it, and new flowers grow in along the top.
        ///
        /// <para>
        /// <b>Column by column, bottom up, which is the only order that needs no second pass.</b>
        /// Walking a column from the floor and pulling the nearest thing above each hole down
        /// into it moves every occupant exactly once and cannot leave a gap behind — where
        /// pushing from the top has to be repeated until nothing moves.
        /// </para>
        /// <para>
        /// <b>Cocoons fall too.</b> They are pods hanging in the grove rather than posts driven
        /// into it, and a cocoon that hovered where its flowers used to be would be the one thing
        /// on the board that does not obey what the player just watched happen. It also matters
        /// to the rules: a cocoon is cracked by what goes off <em>beside</em> it, so one that fell
        /// into the middle of the grove is a cocoon that can now be reached.
        /// </para>
        /// <para>
        /// A grove with no strip does not grow — it only falls — because both shapes ship and the
        /// first chapter was authored the other way before it was authored this way.
        /// </para>
        /// </summary>
        void Fall(List<BudDrop> drops, int wave)
        {
            // **Falling and growing are one rule, and a grove either has it or does not.** They
            // are gated together on the strip rather than separately, because a grove that fell
            // but never refilled would drain into a heap along the floor — and because the fixed
            // boards this mode shipped with are still legal content whose every vector case would
            // otherwise change meaning under a rule they were never authored against.
            if (Layout.Regrow == null) return;

            for (int x = 0; x < Width; x++)
            {
                int floor = Height - 1;

                for (int y = Height - 1; y >= 0; y--)
                {
                    int at = y * Width + x;
                    if (_ground[at] == BudGround.Bare) continue;

                    int to = floor * Width + x;
                    floor--;

                    if (to == at) continue;

                    _ground[to] = _ground[at];
                    _value[to] = _value[at];
                    _ground[at] = BudGround.Bare;
                    _value[at] = Energy.None;

                    drops?.Add(new BudDrop(to, at, wave, _value[to],
                                           _ground[to] == BudGround.Cocoon));
                }
            }
        }

        /// <summary>
        /// New flowers grow into every hole, once the chain has finished.
        ///
        /// <para>
        /// <b>After the chain and never inside it, and that ordering is the whole safety
        /// argument.</b> A cascade used to be bounded by a plain fact: every wave removed at
        /// least three flowers and nothing was ever added, so it could not run for ever. Growing
        /// inside the loop destroys that — new flowers arrive from a <em>repeating</em> strip, so
        /// a grove and a strip that resonate go off, refill into another bunch, and do it again.
        /// That is not a worry about luck; the strip is deterministic, so a loop is reproducible
        /// rather than unlikely. Measured on the first cut, <b>two thirds of opening taps ran
        /// straight into the wave ceiling</b> and par collapsed to one.
        /// </para>
        /// <para>
        /// So the chain keeps its old proof — inside it the grove only ever <em>falls</em>, which
        /// is bounded and is where the compounding comes from — and the grove fills up once,
        /// afterwards, ready for the next tap. That is the half of regrowth that actually
        /// mattered: the board never thins, so the fortieth tap is as good as the first.
        /// </para>
        /// <para>
        /// <b>And it never grows a bunch.</b> A hole takes the first colour off the strip that
        /// does not put three alike together — so the grove at rest is always settled, which is
        /// the rule every level is authored to and is now true after every tap as well as before
        /// the first. Without it the player would be handed a cascade they did not cause, which
        /// is the one thing <c>BudValidator.Settled</c> exists to prevent.
        /// </para>
        /// </summary>
        void Grow(List<BudDrop> drops, int wave)
        {
            if (Layout.Regrow == null) return;

            for (int y = Height - 1; y >= 0; y--)
            for (int x = 0; x < Width; x++)
            {
                int at = y * Width + x;
                if (_ground[at] != BudGround.Bare) continue;

                _ground[at] = BudGround.Flower;

                // Walk the strip for one that settles. One lap and no more: if every colour it
                // deals would match, the first is taken anyway and the chain simply carries on —
                // a board that refused to grow would be a hole nothing could ever fill.
                int lap = Layout.Regrow.Count;
                for (int k = 0; k < lap; k++)
                {
                    _value[at] = Layout.Regrow.At(Grown);
                    if (!JoinsABunch(at)) break;
                    Grown++;
                }

                Grown++;
                drops?.Add(new BudDrop(at, -1, wave, _value[at], false));
            }
        }

        /// <summary>Whether this cell is now part of three or more alike touching.</summary>
        bool JoinsABunch(int cell)
        {
            Array.Clear(_seen, 0, _seen.Length);
            Bunch(cell);
            return _bunch.Count >= BudLayout.Bunch;
        }

        /// <summary>
        /// One flower ripens on its own between taps, and it is always one standing beside
        /// somebody still shut in.
        ///
        /// <para>
        /// <b>The grove leans toward the player rather than waiting to be solved.</b> Every tap
        /// leaves the board a little closer to going off, and it leaves it closer <em>where it
        /// matters</em> — beside a cocoon, which is the only place a burst is worth anything. So
        /// a grove that has been played for a few taps is more explosive than the one that was
        /// dealt, and the player is never staring at a board that has drifted away from them.
        /// </para>
        /// <para>
        /// <b>Exactly one cell, and chosen with no randomness anywhere.</b> The palest flower
        /// beside a shut cocoon takes the colour just spent, ties broken by position. That is a
        /// pure function of the position and the tap, so par is still searchable and two players
        /// on the same grove are still playing the same board — which is the property invariant
        /// 26 threw a whole mode away for lacking.
        /// </para>
        /// </summary>
        void Creep(int spent, List<BudWash> washes, int wave)
        {
            if (!Layout.Grows || spent == Energy.None) return;

            int best = -1, palest = int.MaxValue;

            for (int i = 0; i < _ground.Length; i++)
            {
                if (_ground[i] != BudGround.Cocoon) continue;

                Layout.Beside(i, _beside);

                for (int j = 0; j < _beside.Count; j++)
                {
                    int at = _beside[j];
                    if (_ground[at] != BudGround.Flower) continue;
                    if ((_value[at] | spent) == _value[at]) continue;

                    int channels = Channels(_value[at]);
                    if (channels >= palest) continue;

                    palest = channels;
                    best = at;
                }
            }

            if (best < 0) return;

            // It may not make a bunch either, for <see cref="Grow"/>'s reason: the grove at rest
            // is settled, so nothing the player did not do sets a chain off.
            int was = _value[best];
            _value[best] |= spent;

            if (JoinsABunch(best)) { _value[best] = was; return; }

            washes?.Add(new BudWash(best, wave, _value[best], ripened: true));
        }

        /// <summary>How many of the three channels a colour holds. One is pure, three is white.</summary>
        static int Channels(int mask)
        {
            int n = 0;
            if ((mask & Energy.R) != 0) n++;
            if ((mask & Energy.G) != 0) n++;
            if ((mask & Energy.B) != 0) n++;
            return n;
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
        /// <summary>
        /// The state as a string, for the solver's dedup — and it carries <see cref="Grown"/>,
        /// which is the part that is easy to forget.
        ///
        /// Two groves can look identical and still be different positions: one that has taken
        /// more flowers off the strip will grow different ones next. Leaving that out of the key
        /// prunes a position that was never really visited, which is a solver that quietly
        /// answers the wrong par.
        /// </summary>
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

            // Where the strip is up to is part of the position. A repeating strip means only its
            // phase matters, so one character is enough however long the chain has run.
            int lap = Layout.Regrow == null ? 0 : Grown % Layout.Regrow.Count;
            into[_ground.Length] = (char)('a' + lap);

            length = _ground.Length + 1;
        }

        public void Save(BudGround[] ground, int[] value, out int grown)
        {
            Array.Copy(_ground, ground, _ground.Length);
            Array.Copy(_value, value, _value.Length);
            grown = Grown;
        }

        public void Restore(BudGround[] ground, int[] value, int grown)
        {
            Array.Copy(ground, _ground, _ground.Length);
            Array.Copy(value, _value, _value.Length);
            Grown = grown;
        }
    }
}
