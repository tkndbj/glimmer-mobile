using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>What one cell did on the wave it did it.</summary>
    public enum BudPulseKind
    {
        /// <summary>A flower going off as part of a bunch, or cleared by a special firing.</summary>
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

        /// <summary>
        /// A big bunch leaving a special behind where it went off. <c>Colour</c> is the bunch's,
        /// <c>Held</c> the <see cref="BudSpecial"/> forged, <c>Bunch</c> how big the bunch was.
        /// </summary>
        Forged = 3,

        /// <summary>A special firing. <c>Held</c> is which kind; the cells it clears follow as bursts.</summary>
        Fired = 4,
    }

    /// <summary>One cell doing something, and which wave of the chain did it.</summary>
    public readonly struct BudPulse
    {
        public readonly int Cell;

        /// <summary>0 for the bunch the tap made, 1 for what that set off, and so on.</summary>
        public readonly int Wave;

        /// <summary>The colour it went off in. <c>Energy.None</c> on anything but a burst or a forge.</summary>
        public readonly int Colour;

        public readonly BudPulseKind Kind;

        /// <summary>
        /// How many flowers were in the bunch this one belonged to. 0 on a cocoon.
        ///
        /// <b>Reported rather than counted back, for the reason every other reading here is.</b>
        /// A bunch is a connected blob of one colour, so working its size out from the pulses
        /// would mean a flood fill over a board that has already moved on — <c>BudWash</c>'s
        /// paragraph, in the other direction. It decides how loud one burst is drawn
        /// (<c>BudChain.Blast</c>), and three alike going off is a different event from nine. A
        /// flower a special clears carries the special's own size, so a bolt's line is drawn
        /// big and a sun's square huge.
        /// </summary>
        public readonly int Bunch;

        /// <summary>Which special, as an <c>int</c> of <see cref="BudSpecial"/>, on a forge or a fire.</summary>
        public readonly int Held;

        /// <summary>
        /// On a burst a special cleared: the cell of the special that fired, so the view can
        /// draw the line racing out from it. -1 on an ordinary burst.
        /// </summary>
        public readonly int From;

        public BudPulse(int cell, int wave, int colour, BudPulseKind kind, int bunch = 0,
                        int held = 0, int from = -1)
        {
            Cell = cell;
            Wave = wave;
            Colour = colour;
            Kind = kind;
            Bunch = bunch;
            Held = held;
            From = from;
        }

        /// <summary>Whether this was a cocoon opening rather than a flower bursting.</summary>
        public bool Freed => Kind == BudPulseKind.Freed;

        /// <summary>Whether this was a cocoon taking a crack and holding.</summary>
        public bool Cracked => Kind == BudPulseKind.Crack;

        /// <summary>Whether this burst was a special clearing the cell rather than a bunch going off.</summary>
        public bool Struck => Kind == BudPulseKind.Burst && From >= 0;
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
    /// One thing moving on the grove, so the view can draw it going.
    ///
    /// <para>
    /// <b>Reported rather than re-derived, for <see cref="BudWash"/>'s reason.</b> By the time
    /// anything is animated the model is already at the end of the chain, so "what was standing
    /// here before" is a question the board can no longer answer. A drop carries where it came
    /// from and what it is, and a new flower carries <see cref="From"/> of -1 — which the view
    /// draws as arriving from above the top row.
    /// </para>
    /// <para>
    /// <b>A wave of -1 is a piece that moved <em>before</em> the chain</b> — two flowers grafted —
    /// rather than one that fell because of it. The view draws those sideways and first.
    /// </para>
    /// </summary>
    public readonly struct BudDrop
    {
        /// <summary>Where it came to rest.</summary>
        public readonly int Cell;

        /// <summary>Where it came from, or -1 for a flower that grew in along the top.</summary>
        public readonly int From;

        public readonly int Wave;

        /// <summary>What is standing there now: a colour mask, or cracks on a cocoon.</summary>
        public readonly int Value;

        /// <summary>What kind of thing it is.</summary>
        public readonly BudGround Kind;

        /// <summary>Whether the flower is a special, and which.</summary>
        public readonly BudSpecial Special;

        public BudDrop(int cell, int from, int wave, int value, BudGround kind,
                       BudSpecial special = BudSpecial.None)
        {
            Cell = cell;
            From = from;
            Wave = wave;
            Value = value;
            Kind = kind;
            Special = special;
        }

        public bool Grew => From < 0;

        public bool Cocoon => Kind == BudGround.Cocoon;

        /// <summary>Whether this piece slid before the chain rather than falling inside it.</summary>
        public bool Slid => Wave < 0;
    }

    /// <summary>What one move came to. The reading a preview and a real move share.</summary>
    public readonly struct BudChainResult
    {
        public readonly int Burst;
        public readonly int Waves;
        public readonly int Freed;
        public readonly int Cracked;

        /// <summary>The biggest single bunch that went off. What the celebration is pitched at.</summary>
        public readonly int Biggest;

        /// <summary>Specials forged and specials fired inside this chain.</summary>
        public readonly int Forged, Fired;

        public BudChainResult(int burst, int waves, int freed, int cracked, int biggest,
                              int forged = 0, int fired = 0)
        {
            Burst = burst;
            Waves = waves;
            Freed = freed;
            Cracked = cracked;
            Biggest = biggest;
            Forged = forged;
            Fired = fired;
        }

        public static readonly BudChainResult Nothing = new BudChainResult(0, 0, 0, 0, 0);

        public bool Any => Burst > 0;

        public override string ToString()
            => $"{Burst} burst, {Waves} wave(s), {Freed} freed, {Cracked} cracked, " +
               $"{Forged} forged, {Fired} fired";
    }

    /// <summary>
    /// <b>The grove as it stands.</b> What colour everything is, who is still shut in, and which
    /// flowers are specials.
    ///
    /// <para>
    /// <b>One tap is a mix, and the mix is what starts everything.</b> The colour in hand is added
    /// to the flower you tap — red with yellow in hand becomes orange — and then the grove settles:
    /// any bunch of three or more touching flowers of one colour <b>bursts</b>, and a burst
    /// <b>washes its colour into every flower touching it</b>. Those flowers change, which can
    /// make new bunches, which burst and wash further. That is the chain, and it is the whole
    /// mode.
    /// </para>
    /// <para>
    /// <b>A big bunch leaves a special behind, and that is the second chapter.</b> Five alike
    /// forge a <b>bolt</b> on the cell the player tapped; eight forge a <b>sun</b>. A special
    /// is a flower the player made, and it fires when tapped — or when a bunch takes it in, or
    /// when another special's reach hits it: a bolt clears its row and column, a sun the
    /// five-by-five around it. What a fired special clears it does not wash, so a chain of
    /// specials is bounded by the specials standing rather than by the grove; what it does do
    /// is crack every cocoon it hits and every cocoon beside what it cleared.
    /// </para>
    /// <para>
    /// <b>Mixing only ever adds channels, so the grove is always being driven toward going off.</b>
    /// Nothing ever becomes less mixed, so every tap moves the board closer to a bunch and closer
    /// to white — which is what makes this chill rather than fiddly. The player is not fighting
    /// the board; they are choosing where it goes off.
    /// </para>
    /// </summary>
    public sealed class BudBoard
    {
        public readonly BudLayout Layout;

        readonly BudGround[] _ground;
        readonly int[] _value;
        readonly BudSpecial[] _special;

        readonly List<int> _bunch = new List<int>(32);
        readonly List<int> _queue = new List<int>(32);
        readonly List<int> _fuse = new List<int>(8);
        readonly List<int> _anvils = new List<int>(4);
        readonly List<int> _reach = new List<int>(32);
        readonly List<int> _cracked = new List<int>(8);
        readonly List<int> _washed = new List<int>(32);
        readonly int[] _wash;

        readonly bool[] _seen;
        readonly List<int> _beside = new List<int>(4);
        readonly List<int> _blast = new List<int>(9);

        /// <summary>
        /// How many flowers have grown in so far, which is where the strip is up to.
        ///
        /// <b>Part of the position, not decoration.</b> Two groves that look identical but have
        /// taken a different number of flowers off the strip will grow different ones next, so
        /// the solver's key carries this — see <see cref="KeyInto"/>.
        /// </summary>
        public int Grown { get; private set; }

        /// <summary>Specials forged and fired since the grove was dealt. Readings, not position.</summary>
        public int Forged { get; private set; }
        public int Fired { get; private set; }

        public BudBoard(BudLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _ground = layout.Standing();
            _value = layout.Values();
            _special = layout.SpecialsStanding();
            _wash = new int[layout.Count];
            _seen = new bool[layout.Count];
        }

        public BudBoard(BudBoard other)
        {
            Layout = other.Layout;

            _ground = new BudGround[other._ground.Length];
            _value = new int[other._value.Length];
            _special = new BudSpecial[other._special.Length];
            Array.Copy(other._ground, _ground, _ground.Length);
            Array.Copy(other._value, _value, _value.Length);
            Array.Copy(other._special, _special, _special.Length);

            _wash = new int[_ground.Length];
            _seen = new bool[_ground.Length];
            Grown = other.Grown;
            Forged = other.Forged;
            Fired = other.Fired;
        }

        public int Width => Layout.Width;
        public int Height => Layout.Height;
        public int Count => _ground.Length;
        public int Index(int x, int y) => Layout.Index(x, y);

        public BudGround At(int index) => _ground[index];
        public int ValueAt(int index) => _value[index];
        public BudSpecial SpecialAt(int index) => _special[index];

        public bool IsFlower(int index) => _ground[index] == BudGround.Flower;
        public bool IsCocoon(int index) => _ground[index] == BudGround.Cocoon;
        public bool IsSpecial(int index) => _ground[index] == BudGround.Flower && _special[index] != BudSpecial.None;

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

        /// <summary>Specials standing on the grove now.</summary>
        public int Specials
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++) if (IsSpecial(i)) n++;
                return n;
            }
        }

        public bool IsFinished => Shut == 0;

        /// <summary>
        /// Whether any move could change anything at all — a tap with any colour the basket
        /// still deals, a special, or a graft.
        ///
        /// <para>
        /// <b>A flower left is not a move left, and that distinction is what the colour rule
        /// added.</b> A white flower holds every channel, so it can never be mixed into by
        /// anything — a grove of nothing but white has flowers on it and no legal tap in it,
        /// which is a board that can be neither won nor ended. That is exactly the state
        /// invariant 20g forbids, and reading <see cref="Flowers"/> here is how it got in.
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

            if (Layout.Grafts)
                for (int i = 0; i < _ground.Length; i++)
                {
                    if (i % Width < Width - 1 && CanGraft(i, i + 1)) return true;
                    if (i / Width < Height - 1 && CanGraft(i, i + Width)) return true;
                }

            return false;
        }

        /// <summary>Whether anything is standing that a chain could still go through.</summary>
        public bool AnyFlower => Flowers > 0;

        /// <summary>
        /// Whether this flower would actually change if the colour in hand were put on it — or
        /// is a special or a bomb, which fire whatever is in hand.
        ///
        /// A tap that mixes nothing in is refused rather than swallowed: it would spend a tap and
        /// leave the grove exactly as it was, which is the one move a player can make by accident
        /// and never means to.
        /// </summary>
        public bool CanTap(int cell, int colour)
        {
            if (cell < 0 || cell >= _ground.Length) return false;
            if (_ground[cell] != BudGround.Flower) return false;

            // **A special fires whatever is in hand**, and so does white. Tapping either is the
            // best tap on the board and the one the player is looking for once they know it is
            // there; neither mixes, so neither is refused for mixing nothing.
            if (_special[cell] != BudSpecial.None) return true;
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
        /// The mix, and the whole chain it sets off — or a special or a bomb going off.
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

            if (_special[cell] != BudSpecial.None) return Strike(cell, pulses, washes, drops);
            if (IsBomb(cell)) return Bomb(cell, pulses, washes, drops);

            _value[cell] |= colour;
            washes?.Add(new BudWash(cell, -1, _value[cell]));

            return Settle(pulses, washes, drops, colour, 0, cell);
        }

        // ------------------------------------------------------------------ the graft
        /// <summary>
        /// Whether these two neighbouring flowers may trade places: different colours, and the
        /// trade makes a bunch.
        ///
        /// <b>Must make a bunch, which is the genre's own rule.</b> A swap that makes nothing
        /// snaps back and costs nothing, so a player can try a pair the way they try a tap — and
        /// every graft the search has to consider bursts something, which is what keeps the
        /// branching within what a phone can prove.
        /// </summary>
        public bool CanGraft(int a, int b)
        {
            if (!Layout.Grafts) return false;
            if (a < 0 || b < 0 || a >= _ground.Length || b >= _ground.Length || a == b) return false;
            if (_ground[a] != BudGround.Flower || _ground[b] != BudGround.Flower) return false;
            if (_value[a] == _value[b] && _special[a] == _special[b]) return false;

            int lo = a < b ? a : b, hi = a < b ? b : a;
            bool touching = hi - lo == Width || (hi - lo == 1 && lo % Width < Width - 1);
            if (!touching) return false;

            Swap(a, b);
            bool bunches = JoinsABunch(a) || JoinsABunch(b);
            Swap(a, b);

            return bunches;
        }

        void Swap(int a, int b)
        {
            int va = _value[a];
            _value[a] = _value[b];
            _value[b] = va;

            var sa = _special[a];
            _special[a] = _special[b];
            _special[b] = sa;
        }

        public BudChainResult PreviewGraft(int a, int b, List<BudPulse> pulses = null,
                                           List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            if (!CanGraft(a, b))
            {
                pulses?.Clear();
                washes?.Clear();
                drops?.Clear();
                return BudChainResult.Nothing;
            }

            return new BudBoard(this).Graft(a, b, pulses, washes, drops);
        }

        /// <summary>
        /// Two neighbours trade places, and the grove settles. The colour in hand is kept.
        ///
        /// The special a bunch forges lands on the flower the player <em>moved</em>, which is the
        /// genre's own rule and the one that reads: what you dragged is what you get.
        /// </summary>
        public BudChainResult Graft(int a, int b, List<BudPulse> pulses,
                                    List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            pulses?.Clear();
            washes?.Clear();
            drops?.Clear();

            if (!CanGraft(a, b)) return BudChainResult.Nothing;

            Swap(a, b);

            drops?.Add(new BudDrop(a, b, -1, _value[a], BudGround.Flower, _special[a]));
            drops?.Add(new BudDrop(b, a, -1, _value[b], BudGround.Flower, _special[b]));

            return Settle(pulses, washes, drops, Energy.None, 0, b);
        }

        // ------------------------------------------------------------------ the specials
        /// <summary>
        /// A tapped special going off: wave nought is the special's own clearing, and the grove
        /// settles from wave one.
        /// </summary>
        BudChainResult Strike(int cell, List<BudPulse> pulses, List<BudWash> washes,
                              List<BudDrop> drops)
        {
            OpenWave();
            _fuse.Clear();
            _anvils.Clear();
            _fuse.Add(cell);

            int burst = 0, freed = 0, cracked = 0, fired = 0, biggest = 0;
            Fire(pulses, 0, ref burst, ref fired, ref biggest);

            Crack(pulses, 0, ref freed, ref cracked);
            Paint(washes, 0);
            Fall(drops, 0);

            var after = Settle(pulses, washes, drops, Energy.None, 1);

            return new BudChainResult(burst + after.Burst, 1 + after.Waves,
                                      freed + after.Freed, cracked + after.Cracked,
                                      after.Biggest > biggest ? after.Biggest : biggest,
                                      after.Forged, fired + after.Fired);
        }

        /// <summary>
        /// Every special on the fuse goes off, and everything in its reach with it.
        ///
        /// <para>
        /// <b>A special in a fired special's reach fires too</b>, in the same wave, which is the
        /// whole of why building two beside each other is worth it. Queued rather than recursed,
        /// so a sun and a bolt that reach each other each fire once — a cell already cleared has
        /// no special left on it, and the queue skips it.
        /// </para>
        /// <para>
        /// <b>What a special clears it does not wash.</b> A bolt that washed the neighbours of a
        /// whole row would set off most of the board, which makes every grove more solvable and
        /// is invariant 20j's solvent; what it does instead is crack every cocoon it hits and
        /// every cocoon beside what it cleared, which is exactly what the player fired it for.
        /// </para>
        /// <para>
        /// The cells are reported in the order <see cref="BudLayout.Reach"/> hands them over —
        /// nearest first — so the view draws the line racing out from the special.
        /// </para>
        /// </summary>
        void Fire(List<BudPulse> pulses, int wave, ref int burst, ref int fired, ref int biggest)
        {
            for (int k = 0; k < _fuse.Count; k++)
            {
                int at = _fuse[k];
                if (_ground[at] != BudGround.Flower || _special[at] == BudSpecial.None) continue;

                var kind = _special[at];
                int size = kind == BudSpecial.Sun ? BudLayout.SunFrom + 1 : BudLayout.BoltFrom;
                if (size > biggest) biggest = size;

                pulses?.Add(new BudPulse(at, wave, _value[at], BudPulseKind.Fired, size, (int)kind));
                Fired++;
                fired++;

                // The special's own cell goes with the rest, and cracks what is beside it.
                Touch(at, _value[at], false);
                pulses?.Add(new BudPulse(at, wave, _value[at], BudPulseKind.Burst, size, (int)kind, at));
                _ground[at] = BudGround.Bare;
                _value[at] = Energy.None;
                _special[at] = BudSpecial.None;
                burst++;

                Layout.Reach(at, kind, _reach);
                for (int j = 0; j < _reach.Count; j++)
                {
                    int r = _reach[j];

                    if (_ground[r] == BudGround.Cocoon)
                    {
                        // Struck directly, which is a crack, whether or not anything bursts
                        // beside it.
                        if (!_cracked.Contains(r)) _cracked.Add(r);
                        continue;
                    }

                    if (_ground[r] != BudGround.Flower) continue;

                    if (_special[r] != BudSpecial.None)
                    {
                        // Left standing for the queue to fire; it clears its own cell then.
                        // **A special forged this very wave is not fired by it**: it arrives
                        // after the wave's bursts, so it is not there yet to be hit — and a
                        // reach that took it would draw it standing up out of a cell it had
                        // already been cleared from.
                        if (!_anvils.Contains(r) && !_fuse.Contains(r)) _fuse.Add(r);
                        continue;
                    }

                    Touch(r, _value[r], false);
                    pulses?.Add(new BudPulse(r, wave, _value[r], BudPulseKind.Burst, size, (int)kind, at));
                    _ground[r] = BudGround.Bare;
                    _value[r] = Energy.None;
                    burst++;
                }
            }

            _fuse.Clear();
        }

        // ------------------------------------------------------------------ the bomb
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
        /// biggest kind of burst there is without being told about it separately. A special in
        /// the square fires with it.
        /// </para>
        /// </summary>
        BudChainResult Bomb(int cell, List<BudPulse> pulses, List<BudWash> washes,
                            List<BudDrop> drops)
        {
            int x = cell % Width, y = cell / Width;
            _blast.Clear();
            _fuse.Clear();
            _anvils.Clear();

            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int a = x + dx, b = y + dy;
                if (a < 0 || a >= Width || b < 0 || b >= Height) continue;

                int at = b * Width + a;
                if (_ground[at] != BudGround.Flower) continue;

                if (_special[at] != BudSpecial.None) _fuse.Add(at);
                else _blast.Add(at);
            }

            int burst = 0, freed = 0, cracked = 0, fired = 0, biggest = _blast.Count;

            // What it touches is read before any of it is taken away, exactly as a wave's wash is.
            OpenWave();
            for (int k = 0; k < _blast.Count; k++) Touch(_blast[k], _value[_blast[k]], false);

            for (int k = 0; k < _blast.Count; k++)
            {
                pulses?.Add(new BudPulse(_blast[k], 0, _value[_blast[k]],
                                         BudPulseKind.Burst, _blast.Count));
                _ground[_blast[k]] = BudGround.Bare;
                _value[_blast[k]] = Energy.None;
                burst++;
            }

            Fire(pulses, 0, ref burst, ref fired, ref biggest);
            Crack(pulses, 0, ref freed, ref cracked);
            Paint(washes, 0);
            Fall(drops, 0);

            var after = Settle(pulses, washes, drops, Energy.None, 1);

            return new BudChainResult(burst + after.Burst, 1 + after.Waves,
                                      freed + after.Freed, cracked + after.Cracked,
                                      after.Biggest > biggest ? after.Biggest : biggest,
                                      after.Forged, fired + after.Fired);
        }

        // ------------------------------------------------------------------ the chain
        /// <summary>
        /// Everything the grove does on its own once something has moved: bunches go off, their
        /// colour washes outward, and whatever that makes goes off in turn.
        /// </summary>
        /// <param name="origin">
        /// The cell the player touched, which is where a special the first wave forges is put.
        /// After the first wave a bunch's special lands on its lowest cell, because there is no
        /// hand on the board to put it under.
        /// </param>
        public BudChainResult Settle(List<BudPulse> pulses, List<BudWash> washes,
                                    List<BudDrop> drops = null, int spent = Energy.None,
                                    int from = 0, int origin = -1)
        {
            int burst = 0, freed = 0, cracked = 0, biggest = 0, forged = 0, fired = 0;
            int waves = from;

            // See BudLayout.MostWaves: once the grove regrows, "every wave removes flowers and
            // nothing is ever added" stops being true and the settle needs a bound of its own.
            while (waves - from < BudLayout.MostWaves)
            {
                OpenWave();
                _queue.Clear();
                _fuse.Clear();
                _anvils.Clear();

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

                    // **A big bunch leaves something behind.** Where the player tapped if the tap
                    // is in this bunch; otherwise the bunch's lowest cell, which is `_bunch[0]`
                    // because the scan is ascending.
                    var forge = !Layout.Forges ? BudSpecial.None
                              : _bunch.Count >= BudLayout.SunFrom ? BudSpecial.Sun
                              : _bunch.Count >= BudLayout.BoltFrom ? BudSpecial.Bolt
                              : BudSpecial.None;
                    int anvil = -1;
                    if (forge != BudSpecial.None)
                        anvil = origin >= 0 && _bunch.Contains(origin) ? origin : _bunch[0];

                    // What the bunch touches is read before any of it is taken away, so the wash
                    // goes out from where the flowers were standing.
                    for (int k = 0; k < _bunch.Count; k++) Touch(_bunch[k], colour, true);

                    for (int k = 0; k < _bunch.Count; k++)
                    {
                        int at = _bunch[k];

                        // The anvil is not a burst: it stays, and is reported only as forged.
                        if (at == anvil) continue;

                        // A special taken into a bunch fires with it, whatever colour it wears —
                        // and `Fire` reports its burst, so it is not reported here as well.
                        if (_special[at] != BudSpecial.None) { _fuse.Add(at); continue; }

                        _queue.Add(at);
                        pulses?.Add(new BudPulse(at, waves, colour, BudPulseKind.Burst, _bunch.Count));
                    }

                    if (anvil >= 0)
                    {
                        _anvils.Add(anvil);
                        _special[anvil] = forge;
                        _value[anvil] = colour;
                        pulses?.Add(new BudPulse(anvil, waves, colour, BudPulseKind.Forged,
                                                 _bunch.Count, (int)forge));
                        Forged++;
                        forged++;
                    }
                }

                if (!any) break;

                for (int k = 0; k < _queue.Count; k++)
                {
                    _ground[_queue[k]] = BudGround.Bare;
                    _value[_queue[k]] = Energy.None;
                    burst++;
                }

                Fire(pulses, waves, ref burst, ref fired, ref biggest);
                Crack(pulses, waves, ref freed, ref cracked);

                // Then the colour goes out. A flower touched by two bunches at once takes both,
                // which is where the long chains come from.
                Paint(washes, waves);

                // **And then the grove falls, and grows.** Everything above a hole slides down
                // into it and new flowers arrive along the top — so the next turn of this loop is
                // looking at a full board rather than a thinner one. That single step is why a
                // chain here compounds instead of running out: the flowers that drop into a
                // burst's own hole can be the ones that set off the wave after it.
                Fall(drops, waves);

                waves++;
                origin = -1;
            }

            // **And only now does the grove grow back.** Falling happens inside the chain, where
            // it compounds; growing happens once, after it has stopped. See <see cref="Grow"/> —
            // that ordering is what keeps a cascade provably finite.
            Grow(drops, waves);
            Creep(spent, washes, waves);

            return new BudChainResult(burst, waves - from, freed, cracked, biggest, forged, fired);
        }

        /// <summary>Clears everything a wave accumulates before it starts reading the board.</summary>
        void OpenWave()
        {
            Array.Clear(_wash, 0, _wash.Length);
            _cracked.Clear();
            _washed.Clear();
        }

        /// <summary>
        /// What one bursting flower reaches: a cocoon takes a crack, a flower takes the colour.
        ///
        /// <b>One method for a bunch, a bomb and a special</b>, because they are the same event
        /// to everything standing beside them, and two copies of "what does a burst touch" is
        /// two things that can disagree about a cocoon.
        /// </summary>
        void Touch(int cell, int colour, bool washes)
        {
            Layout.Beside(cell, _beside);

            for (int j = 0; j < _beside.Count; j++)
            {
                int nb = _beside[j];

                if (_ground[nb] == BudGround.Cocoon)
                {
                    if (!_cracked.Contains(nb)) _cracked.Add(nb);
                }
                else if (_ground[nb] == BudGround.Flower && washes)
                {
                    // Every flower it touches, with no cleverness about which. It used to say
                    // `&& !_seen[nb]`, meaning to skip a flower that is itself bursting — but
                    // `_seen` is the flood fill's *visited* marker, so it also covered every
                    // flower already scanned as part of a group of one or two that was
                    // discarded, and the wash simply stopped in one direction and not the other
                    // purely by index order. The application loop re-checks the ground.
                    if (_wash[nb] == Energy.None) _washed.Add(nb);
                    _wash[nb] |= colour;
                }
            }
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

        /// <summary>The colour this wave gathered lands on every flower it reached.</summary>
        void Paint(List<BudWash> washes, int wave)
        {
            for (int k = 0; k < _washed.Count; k++)
            {
                int at = _washed[k];
                if (_ground[at] != BudGround.Flower) continue;

                int was = _value[at];
                _value[at] |= _wash[at];

                if (_value[at] != was) washes?.Add(new BudWash(at, wave, _value[at]));
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
        /// <b>Cocoons fall too, and a special falls as the flower it is.</b> They are things
        /// hanging in the grove rather than posts driven into it, and a piece that hovered where
        /// its flowers used to be would be the one thing on the board that does not obey what
        /// the player just watched happen.
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
                    _special[to] = _special[at];
                    _ground[at] = BudGround.Bare;
                    _value[at] = Energy.None;
                    _special[at] = BudSpecial.None;

                    drops?.Add(new BudDrop(to, at, wave, _value[to], _ground[to], _special[to]));
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
                _special[at] = BudSpecial.None;

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
                drops?.Add(new BudDrop(at, -1, wave, _value[at], BudGround.Flower));
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
        /// <para>
        /// A graft and a special spend no colour, so nothing ripens after them: the creep is the
        /// colour <em>just spent</em> leaning further, and nothing was.
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
                    if (_ground[at] != BudGround.Flower || _special[at] != BudSpecial.None) continue;
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

        /// <summary>
        /// The state as a string, for the solver's dedup — and it carries <see cref="Grown"/>,
        /// which is the part that is easy to forget, and which flowers are specials.
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
                    case BudGround.Flower:
                        // A plain flower is its letter; a bolt a digit; a sun a mark below the
                        // digits. Three disjoint runs, and none collides with a cocoon's.
                        into[i] = _special[i] == BudSpecial.Bolt ? (char)('0' + _value[i])
                                : _special[i] == BudSpecial.Sun ? (char)('!' + _value[i])
                                : Energy.Letter(_value[i]);
                        break;
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

        public void Save(BudGround[] ground, int[] value, BudSpecial[] special, out int packed)
        {
            Array.Copy(_ground, ground, _ground.Length);
            Array.Copy(_value, value, _value.Length);
            Array.Copy(_special, special, _special.Length);
            packed = (Grown & 0xFFFF) | (Forged & 0xFF) << 16 | (Fired & 0x7F) << 24;
        }

        public void Restore(BudGround[] ground, int[] value, BudSpecial[] special, int packed)
        {
            Array.Copy(ground, _ground, _ground.Length);
            Array.Copy(value, _value, _value.Length);
            Array.Copy(special, _special, _special.Length);
            Grown = packed & 0xFFFF;
            Forged = (packed >> 16) & 0xFF;
            Fired = (packed >> 24) & 0x7F;
        }
    }
}
