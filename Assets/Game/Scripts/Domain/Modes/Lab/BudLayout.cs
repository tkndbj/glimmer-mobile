using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The ordered basket of colours a grove is dealt, one per tap.
    ///
    /// <para>
    /// <b>Pure colour only, and that is the whole trick.</b> A grove is dealt red, green and
    /// blue; every blend on the board — yellow, magenta, cyan, white — is one the *player* made
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

        public static bool TryParse(string authored, out BudDeal deal, out string error)
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

                if (mask != Energy.R && mask != Energy.G && mask != Energy.B)
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

        /// <summary>The most cracks a cocoon may take before the critter is out.</summary>
        public const int ToughestCocoon = 2;

        public readonly int Width, Height;

        readonly BudGround[] _ground;
        readonly int[] _value;      // a colour mask on a flower, cracks on a cocoon, 0 otherwise

        public readonly BudDeal Deal;

        public BudLayout(int width, int height, BudGround[] ground, int[] value, BudDeal deal)
        {
            if (width < MinWidth || width > MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinHeight || height > MaxHeight)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            Deal = deal ?? throw new ArgumentNullException(nameof(deal));

            int n = width * height;
            _ground = new BudGround[n];
            _value = new int[n];

            if (ground != null) Array.Copy(ground, _ground, Math.Min(ground.Length, n));
            if (value != null) Array.Copy(value, _value, Math.Min(value.Length, n));
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
