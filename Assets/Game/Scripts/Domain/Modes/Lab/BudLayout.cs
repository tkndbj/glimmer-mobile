using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The ordered basket of colours a grove is dealt, one per tap.
    ///
    /// <para>
    /// <b>Pure colour only, and that is the whole trick.</b> A grove is dealt the three pure
    /// channels; every blend on the board — orange, purple, green, white — is one the *player* made
    /// by mixing. So the basket stays three symbols wide however rich the board gets, and the
    /// colour a flower ends up is always something somebody chose.
    /// </para>
    /// <para>
    /// Ordered and repeating, for the reason every procession in this game is (invariant 20e):
    /// the colour in hand is what decides which taps are worth anything, so a colour the player
    /// could pick freely would collapse the grove to "which flower".
    /// </para>
    /// </summary>
    public sealed class BudDeal
    {
        public const int MaxLength = 24;

        readonly int[] _colours;

        BudDeal(int[] colours) => _colours = colours;

        public int Count => _colours.Length;

        /// <summary>The colour in hand after <paramref name="spent"/> taps have gone.</summary>
        public int At(int spent)
        {
            if (_colours.Length == 0) return Energy.None;

            int i = spent % _colours.Length;
            if (i < 0) i += _colours.Length;
            return _colours[i];
        }

        /// <summary>Every channel this basket ever deals. Bounds what a grove can be made of.</summary>
        public int Channels
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _colours.Length; i++) mask |= _colours[i];
                return mask;
            }
        }

        public string Written()
        {
            var chars = new char[_colours.Length];
            for (int i = 0; i < _colours.Length; i++) chars[i] = Energy.Letter(_colours[i]);
            return new string(chars);
        }

        /// <summary>
        /// Reads a basket, or the strip a grove grows from.
        ///
        /// <para>
        /// <b><paramref name="pure"/> is the whole difference between the two, and it is a
        /// difference about choice rather than about colour.</b> A basket is what the player is
        /// handed and therefore what they decide with, so it is pure — every blend on the board
        /// has to be one somebody made, which is the mode's one idea. A strip is <em>scenery</em>:
        /// nobody chooses what grows back, so it may be anything.
        /// </para>
        /// <para>
        /// It is also what keeps the grove playable. A strip of three pure colours refilling a
        /// four-colour board matches itself constantly — measured, two thirds of opening taps ran
        /// straight into <see cref="BudLayout.MostWaves"/>, which is a cascade that never stops
        /// and a par of one. A strip that deals blends as well spreads the board out, so a
        /// cascade runs a few waves and stops because it has run out of matches rather than
        /// because it hit a ceiling.
        /// </para>
        /// </summary>
        public static bool TryParse(string authored, out BudDeal deal, out string error,
                                    bool pure = true)
        {
            deal = null;
            error = null;

            if (string.IsNullOrEmpty(authored))
            {
                error = "a grove has to be told what it is dealt";
                return false;
            }

            var colours = new List<int>(authored.Length);

            for (int i = 0; i < authored.Length; i++)
            {
                char c = authored[i];
                if (c == ' ' || c == '\t' || c == '_') continue;

                if (!Energy.TryParse(c, out int mask) || mask == Energy.None)
                {
                    error = "'" + c + "' at " + i + " is not a colour; a basket is written in " +
                            "R, G and B";
                    return false;
                }

                if (pure && mask != Energy.R && mask != Energy.G && mask != Energy.B)
                {
                    error = "'" + c + "' at " + i + " is a blend. A grove is dealt pure colour, " +
                            "so that every blend on the board is one the player made";
                    return false;
                }

                colours.Add(mask);
            }

            if (colours.Count == 0)
            {
                error = "a basket of nothing but spaces deals nothing";
                return false;
            }

            if (colours.Count > MaxLength)
            {
                error = "a basket of " + colours.Count + " is longer than the " + MaxLength +
                        " a procession may name; it repeats, so one lap is enough";
                return false;
            }

            deal = new BudDeal(colours.ToArray());
            return true;
        }
    }

    /// <summary>What one cell of a grove is.</summary>
    public enum BudGround
    {
        /// <summary>Nothing. A burst washes over it and stops.</summary>
        Bare = 0,

        /// <summary>A flower, wearing a colour. The only thing that can be tapped.</summary>
        Flower = 1,

        /// <summary>A cocoon with a critter in it. Cracks when a burst goes off beside it.</summary>
        Cocoon = 2,

        /// <summary>Old wood. Nothing grows on it and no burst crosses it.</summary>
        Stone = 3,

    }

    /// <summary>
    /// A grove: what is standing in it, what colour each flower wears, how many cracks each
    /// cocoon takes, and the basket it is dealt.
    /// </summary>
    public sealed class BudLayout
    {
        public const int MinWidth = 4, MaxWidth = 9;
        public const int MinHeight = 4, MaxHeight = 9;

        /// <summary>How many alike have to be touching before they go off.</summary>
        public const int Bunch = 3;

        /// <summary>
        /// The most waves one tap may run, and it is a <b>safety bound rather than a tuning</b>.
        ///
        /// <para>
        /// Before the grove regrew, a chain could not run away: every wave took at least three
        /// flowers off a board that never gained any, so the settle was bounded by the grove it
        /// started on. Regrowth removes that argument entirely — new flowers arrive from a
        /// <em>repeating</em> strip, so a grove and a strip that happen to resonate could go off,
        /// refill into another bunch, and do it again for ever. That is not a theoretical worry
        /// about randomness: the strip is deterministic, which is exactly what makes a loop
        /// reproducible rather than unlikely.
        /// </para>
        /// <para>
        /// So the loop stops here and the grove is left as it stands. It is set far above
        /// anything play produces — the deepest chain in the shipped chapter is nine — so it is a
        /// backstop nobody reaches rather than a ceiling anybody plays against, and it bounds the
        /// solver and the animation at the same time.
        /// </para>
        /// </summary>
        public const int MostWaves = 14;

        /// <summary>The most cracks a cocoon may take before the critter is out.</summary>
        public const int ToughestCocoon = 2;

        /// <summary>
        /// How many runners one grove may be strung with.
        ///
        /// <b>A readability bound rather than a cost one.</b> A runner is the one thing here that
        /// moves colour somewhere the player is not looking, so a grove laced with them stops
        /// being a board somebody can read at a glance and becomes a wiring diagram — which is
        /// the failure invariant 20l is about, arriving from the other direction. The search does
        /// not care.
        /// </summary>
        public const int MaxRunners = 6;

        public readonly int Width, Height;

        readonly BudGround[] _ground;
        readonly int[] _value;      // a colour mask on a flower, cracks on a cocoon, 0 otherwise

        /// <summary>The other end of the runner rooted at each cell, or -1 where none is.</summary>
        readonly int[] _runner;

        public readonly BudDeal Deal;

        /// <summary>
        /// What grows into the holes, or null on a grove that does not regrow.
        ///
        /// <b>The single most important field on a Budburst level.</b> With it, a burst is
        /// followed by everything above sliding down and new flowers arriving along the top — so
        /// the grove never thins, a chain can set off the flowers that fall into its own hole, and
        /// cascades compound instead of running out. Without it a grove only ever loses flowers,
        /// which is how this mode shipped first and is why both shapes still parse.
        /// </summary>
        public readonly BudDeal Regrow;

        public bool Grows => Regrow != null;

        public BudLayout(int width, int height, BudGround[] ground, int[] value, BudDeal deal,
                         BudDeal regrow = null, int[] runner = null)
        {
            if (width < MinWidth || width > MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinHeight || height > MaxHeight)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            Deal = deal ?? throw new ArgumentNullException(nameof(deal));
            Regrow = regrow;

            int n = width * height;
            _ground = new BudGround[n];
            _value = new int[n];

            if (ground != null) Array.Copy(ground, _ground, Math.Min(ground.Length, n));
            if (value != null) Array.Copy(value, _value, Math.Min(value.Length, n));

            _runner = new int[n];
            for (int i = 0; i < n; i++) _runner[i] = -1;
            if (runner != null) Array.Copy(runner, _runner, Math.Min(runner.Length, n));
        }

        public int Count => _ground.Length;
        public int Index(int x, int y) => y * Width + x;

        public BudGround GroundAt(int index) => _ground[index];
        public int ValueAt(int index) => _value[index];

        public bool IsFlower(int index) => _ground[index] == BudGround.Flower;
        public bool IsCocoon(int index) => _ground[index] == BudGround.Cocoon;
        public bool IsStone(int index) => _ground[index] == BudGround.Stone;

        public int Flowers => CountOf(BudGround.Flower);
        public int Cocoons => CountOf(BudGround.Cocoon);
        public int Stones => CountOf(BudGround.Stone);

        int CountOf(BudGround kind)
        {
            int n = 0;
            for (int i = 0; i < _ground.Length; i++) if (_ground[i] == kind) n++;
            return n;
        }

        /// <summary>
        /// The far end of the runner rooted at this cell, or -1 where nothing is.
        ///
        /// <para>
        /// <b>A runner belongs to the ground, never to what is standing on it</b>, and that is
        /// the whole of why it survives a living grove. Everything here falls: a flower that
        /// carried its own vine would drag it down the board, and old wood was refused from this
        /// mode for exactly that reason turned round (a barrier sliding down a grove is a wall
        /// that fell over). Two squares of the grove are joined once and for ever, and whatever
        /// is standing on them at the moment a bunch goes off is what the runner carries
        /// between.
        /// </para>
        /// </summary>
        public int FarEnd(int cell)
            => cell >= 0 && cell < _runner.Length ? _runner[cell] : -1;

        public bool IsRunner(int cell) => FarEnd(cell) >= 0;

        /// <summary>How many runners are strung across this grove. A runner has two ends.</summary>
        public int Runners
        {
            get
            {
                int ends = 0;
                for (int i = 0; i < _runner.Length; i++) if (_runner[i] >= 0) ends++;
                return ends / 2;
            }
        }

        public bool HasRunners => Runners > 0;

        /// <summary>Cocoons that take more than one crack.</summary>
        public int ToughCocoons
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++)
                    if (IsCocoon(i) && _value[i] > 1) n++;
                return n;
            }
        }

        /// <summary>Flowers already wearing a blend rather than a pure colour.</summary>
        public int Blends
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++)
                {
                    if (!IsFlower(i)) continue;
                    int c = _value[i];
                    if (c != Energy.R && c != Energy.G && c != Energy.B) n++;
                }
                return n;
            }
        }

        /// <summary>Every colour standing on the grove. What a basket has to be able to move.</summary>
        public int Palette
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _ground.Length; i++) if (IsFlower(i)) mask |= _value[i];
                return mask;
            }
        }

        public void Beside(int index, List<int> into)
        {
            into.Clear();

            int x = index % Width, y = index / Width;
            if (y > 0) into.Add(index - Width);
            if (x < Width - 1) into.Add(index + 1);
            if (y < Height - 1) into.Add(index + Width);
            if (x > 0) into.Add(index - 1);
        }

        /// <summary>
        /// The same grove with every vine cut. What the runners are measured against.
        ///
        /// <b>Invariant 26g's own test, said in code</b>: replace the new object with the nearest
        /// existing one and see whether anything changes. The nearest existing thing to a runner
        /// is no runner, and the difference that can be measured is par — so a grove whose par is
        /// the same with the vines cut is a grove whose vines decided nothing, which is exactly
        /// the state that shipped a mirror and a wick before this mode ever met one.
        /// </summary>
        public BudLayout WithoutRunners()
            => new BudLayout(Width, Height, _ground, _value, Deal, Regrow);

        public BudGround[] Standing()
        {
            var copy = new BudGround[_ground.Length];
            Array.Copy(_ground, copy, _ground.Length);
            return copy;
        }

        public int[] Values()
        {
            var copy = new int[_value.Length];
            Array.Copy(_value, copy, _value.Length);
            return copy;
        }

        /// <summary>
        /// Reads the grid a level authors: <c>R</c>/<c>G</c>/<c>B</c>/<c>Y</c>/<c>M</c>/<c>C</c>/
        /// <c>W</c> a flower of that colour, <c>o</c> a cocoon, <c>O</c> one that takes two
        /// cracks, <c>#</c> old wood, <c>.</c> bare ground.
        /// </summary>
        public static bool TryReadRows(string[] rows, int width, int height,
                                       out BudGround[] ground, out int[] value, out string error)
        {
            ground = null;
            value = null;
            error = null;

            if (rows == null || rows.Length == 0)
            {
                error = "a grove has to be told what is standing in it";
                return false;
            }

            if (rows.Length != height)
            {
                error = "declared " + height + " rows and wrote " + rows.Length;
                return false;
            }

            var cells = new BudGround[width * height];
            var values = new int[width * height];

            for (int y = 0; y < height; y++)
            {
                string row = rows[y] ?? string.Empty;
                int x = 0;

                for (int i = 0; i < row.Length; i++)
                {
                    char c = row[i];
                    if (c == ' ' || c == '\t') continue;

                    if (x >= width)
                    {
                        error = "row " + y + " is wider than the " + width +
                                " columns this grove declares";
                        return false;
                    }

                    int at = y * width + x;

                    if (c == '.' || c == '-') cells[at] = BudGround.Bare;
                    else if (c == '#') cells[at] = BudGround.Stone;
                    else if (c == 'o') { cells[at] = BudGround.Cocoon; values[at] = 1; }
                    else if (c == 'O') { cells[at] = BudGround.Cocoon; values[at] = ToughestCocoon; }
                    else if (Energy.TryParse(c, out int mask) && mask != Energy.None)
                    {
                        cells[at] = BudGround.Flower;
                        values[at] = mask;
                    }
                    else
                    {
                        error = "'" + c + "' at row " + y + " column " + x + " is not part of a " +
                                "grove; it is written in R, G, B, Y, M, C and W for a flower, " +
                                "'o' and 'O' for a cocoon, '#' for old wood and '.' for bare " +
                                "ground";
                        return false;
                    }

                    x++;
                }

                if (x != width)
                {
                    error = "row " + y + " names " + x + " cells, expected " + width;
                    return false;
                }
            }

            ground = cells;
            value = values;
            return true;
        }

        /// <summary>
        /// Reads the second grid a grove may author: which squares are joined by a runner.
        ///
        /// <para>
        /// A letter marks an end and the same letter marks its partner; <c>.</c>, <c>-</c> and a
        /// space mark ordinary ground. A tag has to be written exactly twice, because a runner
        /// has two ends and nothing here would know what a third one meant.
        /// </para>
        /// <para>
        /// <b>A grid of its own rather than a list of coordinates</b>, for the reason
        /// <see cref="TryReadRows"/> is one: an author reads a grove by looking at it, and a
        /// runner is a fact about <em>where</em>. Written as a list, a vine's two ends are four
        /// numbers nobody can picture; written as a layer, it is a shape lying over the board it
        /// belongs to, and a wrong one is visible in the file that caused it.
        /// </para>
        /// </summary>
        public static bool TryReadRunners(string[] rows, int width, int height,
                                          out int[] runner, out string error)
        {
            runner = null;
            error = null;

            var ends = new int[width * height];
            for (int i = 0; i < ends.Length; i++) ends[i] = -1;

            if (rows == null || rows.Length == 0) { runner = ends; return true; }

            if (rows.Length != height)
            {
                error = "the runners are drawn over the grove, so they are " + height +
                        " rows; this one writes " + rows.Length;
                return false;
            }

            // Where each tag was first seen, so the second sighting can be joined to it.
            var seen = new Dictionary<char, int>(4);

            for (int y = 0; y < height; y++)
            {
                string row = rows[y] ?? string.Empty;
                int x = 0;

                for (int i = 0; i < row.Length; i++)
                {
                    char c = row[i];
                    if (c == '\t') continue;

                    if (x >= width)
                    {
                        error = "runner row " + y + " is wider than the " + width +
                                " columns this grove declares";
                        return false;
                    }

                    int at = y * width + x;
                    x++;

                    if (c == '.' || c == '-' || c == ' ') continue;

                    if (c < 'a' || c > 'z')
                    {
                        error = "'" + c + "' at runner row " + y + " column " + (x - 1) +
                                " is not a runner; a runner is written as one lower-case letter " +
                                "on each of its two ends, with '.' everywhere else";
                        return false;
                    }

                    if (!seen.TryGetValue(c, out int first))
                    {
                        seen[c] = at;
                        continue;
                    }

                    if (ends[first] >= 0)
                    {
                        error = "runner '" + c + "' is written three or more times. A runner has " +
                                "two ends; use another letter for another runner";
                        return false;
                    }

                    ends[first] = at;
                    ends[at] = first;
                }

                if (x != width)
                {
                    error = "runner row " + y + " names " + x + " cells, expected " + width;
                    return false;
                }
            }

            foreach (var pair in seen)
            {
                if (ends[pair.Value] >= 0) continue;

                error = "runner '" + pair.Key + "' is written once. A runner joins two squares, " +
                        "so its letter goes on both of them";
                return false;
            }

            runner = ends;
            return true;
        }

        /// <summary>
        /// The runners written back out, or null on a grove strung with none.
        ///
        /// Tagged in reading order — the first runner met is <c>a</c> — so the answer is a pure
        /// function of the grove rather than of whichever letters somebody happened to pick,
        /// which is what makes a round-trip proof a proof.
        /// </summary>
        public string[] WrittenRunners()
        {
            if (!HasRunners) return null;

            var rows = new string[Height];
            var line = new char[Width];
            char tag = 'a';

            var named = new Dictionary<int, char>(MaxRunners * 2);

            for (int i = 0; i < _runner.Length; i++)
            {
                if (_runner[i] < 0 || named.ContainsKey(i)) continue;

                named[i] = tag;
                named[_runner[i]] = tag;
                tag++;
            }

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int at = Index(x, y);
                    line[x] = named.TryGetValue(at, out char c) ? c : '.';
                }

                rows[y] = new string(line);
            }

            return rows;
        }

        /// <summary>The grove written back out, which is what a round-trip proof compares.</summary>
        public string[] Written()
        {
            var rows = new string[Height];
            var line = new char[Width];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int at = Index(x, y);

                    switch (_ground[at])
                    {
                        case BudGround.Flower: line[x] = Energy.Letter(_value[at]); break;
                        case BudGround.Cocoon: line[x] = _value[at] > 1 ? 'O' : 'o'; break;
                        case BudGround.Stone: line[x] = '#'; break;
                        default: line[x] = '.'; break;
                    }
                }

                rows[y] = new string(line);
            }

            return rows;
        }
    }
}
