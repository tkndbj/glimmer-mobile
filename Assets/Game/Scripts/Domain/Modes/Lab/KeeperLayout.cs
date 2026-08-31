using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The ordered procession of tiles a grove is dealt, and the one rule about it: a tile is a
    /// pure colour or a prism, never a blend.
    ///
    /// <para>
    /// <b>Authored, never rolled.</b> Groovekeeper shipped with an xorshift dealing colours,
    /// which is what made it a prototype rather than a level: par cannot be derived from a board
    /// whose future is random, so it could author no goal, no budget and no star line, and two
    /// players on the same level were not playing the same board. It is <c>FallDeal</c>'s
    /// argument word for word, and deleting the generator deletes the same class of divergence
    /// with it — a deal that differs between .NET, Mono and IL2CPP is Lightweave's generator's
    /// float bug wearing a different hat.
    /// </para>
    /// <para>
    /// <b>It cycles, and that is load-bearing rather than a convenience.</b> A run may be handed
    /// more tiles than the author wrote — a continue does exactly that — so <see cref="At"/> has
    /// to answer for any index rather than running off the end into a colour nobody chose. It is
    /// also the honest reading of what the basket shows: a keeper is handed the same procession
    /// over and over, and somebody who has watched one lap knows what the next holds. What it
    /// costs is one authoring rule, enforced by the validator: a deal must carry all three
    /// channels, or a bed wanting one of them could never be opened however many tiles were
    /// bought.
    /// </para>
    /// <para>
    /// <b>A blend is refused and a prism is not, and the difference is who made it.</b> Dealing
    /// a blend would hand over a seam already made, and the whole mode is that a blend is
    /// something the player <em>arranges</em> by putting unlike beside unlike. A prism carries
    /// all three at once, which is not a step of that work done for free — it is a different
    /// thing entirely, and one worth being rare (see <c>Mechanic.KeeperPrism</c>).
    /// </para>
    /// </summary>
    public sealed class KeeperDeal
    {
        /// <summary>Most tiles a deal may name. Long enough for any procession, short enough to read.</summary>
        public const int MaxLength = 48;

        /// <summary>What a prism is written as. It carries every channel, so it opens any bed.</summary>
        public const char PrismLetter = 'P';

        readonly int[] _tiles;

        KeeperDeal(int[] tiles) => _tiles = tiles;

        public int Count => _tiles.Length;

        /// <summary>The colour of tile <paramref name="spent"/>, counting from nought and wrapping.</summary>
        public int At(int spent)
        {
            if (_tiles.Length == 0) return Energy.None;

            int i = spent % _tiles.Length;
            if (i < 0) i += _tiles.Length;
            return _tiles[i];
        }

        /// <summary>Every channel this deal can ever supply, as a mask.</summary>
        public int Channels
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _tiles.Length; i++) mask |= _tiles[i];
                return mask;
            }
        }

        /// <summary>How many prisms one lap of the procession holds.</summary>
        public int Prisms
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _tiles.Length; i++) if (_tiles[i] == Energy.All) n++;
                return n;
            }
        }

        /// <summary>The authored string again, so a tool can round-trip a level.</summary>
        public string Written()
        {
            var chars = new char[_tiles.Length];
            for (int i = 0; i < _tiles.Length; i++)
                chars[i] = _tiles[i] == Energy.All ? PrismLetter : Energy.Letter(_tiles[i]);
            return new string(chars);
        }

        /// <summary>
        /// Reads an authored procession. Spaces and underscores are ignored, so a long one can be
        /// grouped for reading.
        /// </summary>
        public static bool TryParse(string authored, out KeeperDeal deal, out string error)
        {
            deal = null;
            error = null;

            if (string.IsNullOrEmpty(authored))
            {
                error = "a grove has to be told what it is dealt";
                return false;
            }

            var tiles = new List<int>(authored.Length);

            for (int i = 0; i < authored.Length; i++)
            {
                char c = authored[i];
                if (c == ' ' || c == '\t' || c == '_') continue;

                if (c == PrismLetter) { tiles.Add(Energy.All); continue; }

                if (!Energy.TryParse(c, out int mask) || mask == Energy.None)
                {
                    error = "'" + c + "' at " + i + " is not a tile; a deal is written in R, G, B" +
                            " and " + PrismLetter;
                    return false;
                }

                if (mask == Energy.R || mask == Energy.G || mask == Energy.B)
                {
                    tiles.Add(mask);
                    continue;
                }

                // A blend hands the player a seam already made, and every seam on this board is
                // supposed to be one they arranged. Refused by name rather than silently split,
                // because an author who wrote Y meant something and needs to be told it is not
                // on offer. W is refused with the rest: a prism is written P, so that the one
                // tile which breaks the mode's rule cannot be written by accident.
                error = "'" + c + "' at " + i + " is a blend; a grove is dealt pure light, so " +
                        "that every seam on it is one the player made. A tile carrying all " +
                        "three is a prism and is written " + PrismLetter;
                return false;
            }

            if (tiles.Count == 0)
            {
                error = "a deal of nothing but spaces deals nothing";
                return false;
            }

            if (tiles.Count > MaxLength)
            {
                error = "a deal of " + tiles.Count + " is longer than the " + MaxLength +
                        " a procession may name; it repeats, so it never needs to be longer " +
                        "than one lap";
                return false;
            }

            deal = new KeeperDeal(tiles.ToArray());
            return true;
        }
    }

    /// <summary>What one cell of the ground is before anybody plants anything on it.</summary>
    public enum KeeperGround
    {
        /// <summary>Bare ground. A tile may be planted here.</summary>
        Open = 0,

        /// <summary>Stone. Nothing grows on it, and a grove has to reach round it.</summary>
        Stone = 1,

        /// <summary>A bed: ground that has to hold a bloomed tile before the grove is finished.</summary>
        Bed = 2,
    }

    /// <summary>
    /// One authored grove: the ground, what is already standing on it, which beds have to bloom,
    /// and the procession of tiles to do it with.
    ///
    /// <para>
    /// <b>Everything a Groovekeeper level authors, and nothing else.</b> No par, no star line, no
    /// budget — those are derived from this by <see cref="KeeperSolver"/> and <c>LevelTuning</c>,
    /// for invariant 5's reason: a typed par can drift from the board it claims to describe, and
    /// the drift has no symptom. One too high hands three stars to a careless run for ever, one
    /// too low makes them unreachable, and neither is visible in the file that caused it.
    /// </para>
    /// <para>
    /// <b>The grid is written one letter per cell.</b> <c>.</c> is bare ground, <c>#</c> is
    /// stone, <c>*</c> is a bed that any bloom opens, <c>r</c>/<c>g</c>/<c>b</c> is a
    /// <em>heartbed</em> that only its own colour may be planted on, and a capital
    /// <c>R</c>/<c>G</c>/<c>B</c> is a <em>sprig</em> — a tile already standing when the level
    /// opens, which is what the grove grows out from. A grove with no sprig has nothing to grow
    /// from and is refused.
    /// </para>
    /// </summary>
    public sealed class KeeperLayout
    {
        public const int MinWidth = 4, MaxWidth = 9;
        public const int MinHeight = 4, MaxHeight = 9;

        public readonly int Width, Height;

        readonly KeeperGround[] _ground;

        /// <summary>The colour a bed insists on, or <see cref="Energy.None"/> for one that takes any.</summary>
        readonly int[] _wants;

        /// <summary>The tiles standing before anybody plays. Nought everywhere but the sprigs.</summary>
        readonly int[] _sprigs;

        public readonly KeeperDeal Deal;

        public KeeperLayout(int width, int height, KeeperGround[] ground, int[] wants,
                            int[] sprigs, KeeperDeal deal)
        {
            if (width < MinWidth || width > MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinHeight || height > MaxHeight)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            Deal = deal ?? throw new ArgumentNullException(nameof(deal));

            int n = width * height;
            _ground = new KeeperGround[n];
            _wants = new int[n];
            _sprigs = new int[n];

            if (ground != null) Array.Copy(ground, _ground, Math.Min(ground.Length, n));
            if (wants != null) Array.Copy(wants, _wants, Math.Min(wants.Length, n));
            if (sprigs != null) Array.Copy(sprigs, _sprigs, Math.Min(sprigs.Length, n));
        }

        public int Count => _ground.Length;
        public int Index(int x, int y) => y * Width + x;
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public KeeperGround GroundAt(int index) => _ground[index];
        public KeeperGround GroundAt(int x, int y) => _ground[Index(x, y)];

        /// <summary>Whether a tile may ever stand here. Stone never; a bed and bare ground both may.</summary>
        public bool IsPlantable(int index) => _ground[index] != KeeperGround.Stone;

        public bool IsBed(int index) => _ground[index] == KeeperGround.Bed;

        /// <summary>The colour this bed insists on, or <see cref="Energy.None"/> when any will do.</summary>
        public int Wants(int index) => _wants[index];

        public int SprigAt(int index) => _sprigs[index];

        /// <summary>A copy of what is standing, for a board about to be played on.</summary>
        public int[] Standing()
        {
            var copy = new int[_sprigs.Length];
            Array.Copy(_sprigs, copy, _sprigs.Length);
            return copy;
        }

        /// <summary>How many beds this grove has to open. The goal, counted.</summary>
        public int Beds
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++) if (IsBed(i)) n++;
                return n;
            }
        }

        /// <summary>How many beds insist on a colour. The half of the goal the deal has to serve.</summary>
        public int Heartbeds
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++)
                    if (IsBed(i) && _wants[i] != Energy.None) n++;
                return n;
            }
        }

        /// <summary>How many tiles are standing before anybody plays.</summary>
        public int Sprigs
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _sprigs.Length; i++) if (_sprigs[i] != Energy.None) n++;
                return n;
            }
        }

        /// <summary>Cells a tile could ever stand on. The room the whole run has, counted once.</summary>
        public int Room
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ground.Length; i++) if (IsPlantable(i)) n++;
                return n;
            }
        }

        /// <summary>Every colour some bed insists on, as a mask.</summary>
        public int Wanted
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _ground.Length; i++) if (IsBed(i)) mask |= _wants[i];
                return mask;
            }
        }

        /// <summary>The cells beside <paramref name="index"/> that are on the board, clockwise from north.</summary>
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
        /// Reads an authored ground: one row per line, one letter per column. Spaces are ignored,
        /// so a row may be written spaced out for reading.
        /// </summary>
        public static bool TryReadRows(string[] rows, int width, int height,
                                       out KeeperGround[] ground, out int[] wants,
                                       out int[] sprigs, out string error)
        {
            ground = null;
            wants = null;
            sprigs = null;
            error = null;

            if (rows == null || rows.Length == 0)
            {
                error = "a grove has to be told what its ground is";
                return false;
            }

            if (rows.Length != height)
            {
                error = "declared " + height + " rows and wrote " + rows.Length;
                return false;
            }

            var cells = new KeeperGround[width * height];
            var want = new int[width * height];
            var seed = new int[width * height];

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

                    switch (c)
                    {
                        case '.':
                        case '-':
                            cells[at] = KeeperGround.Open;
                            break;

                        case '#':
                            cells[at] = KeeperGround.Stone;
                            break;

                        case '*':
                            cells[at] = KeeperGround.Bed;
                            break;

                        case 'r': cells[at] = KeeperGround.Bed; want[at] = Energy.R; break;
                        case 'g': cells[at] = KeeperGround.Bed; want[at] = Energy.G; break;
                        case 'b': cells[at] = KeeperGround.Bed; want[at] = Energy.B; break;

                        case 'R': cells[at] = KeeperGround.Open; seed[at] = Energy.R; break;
                        case 'G': cells[at] = KeeperGround.Open; seed[at] = Energy.G; break;
                        case 'B': cells[at] = KeeperGround.Open; seed[at] = Energy.B; break;

                        default:
                            error = "'" + c + "' at row " + y + " column " + x + " is not " +
                                    "ground; a grove is written in '.', '#', '*', r/g/b for a " +
                                    "heartbed and R/G/B for a sprig";
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
            wants = want;
            sprigs = seed;
            return true;
        }

        /// <summary>The authored rows again, so a tool can round-trip a level.</summary>
        public string[] Written()
        {
            var rows = new string[Height];
            var line = new char[Width];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int at = Index(x, y);

                    if (_sprigs[at] != Energy.None) line[x] = Energy.Letter(_sprigs[at]);
                    else if (_ground[at] == KeeperGround.Stone) line[x] = '#';
                    else if (_ground[at] != KeeperGround.Bed) line[x] = '.';
                    else if (_wants[at] == Energy.None) line[x] = '*';
                    else line[x] = char.ToLowerInvariant(Energy.Letter(_wants[at]));
                }

                rows[y] = new string(line);
            }

            return rows;
        }
    }
}
