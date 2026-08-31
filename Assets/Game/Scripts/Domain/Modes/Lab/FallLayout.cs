using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The ordered procession of motes a well deals, and the one rule about it: never a blend.
    ///
    /// <para>
    /// <b>Authored, never rolled.</b> Lightfall shipped with an xorshift dealing colours, which
    /// was correct for a score attack and impossible for a level: par cannot be derived from a
    /// board whose future is random, so a level could author no goal, no budget and no star
    /// line, and two players on the same glade were not playing the same glade. Deleting the
    /// generator also deletes a whole class of divergence — a deal that differs between .NET,
    /// Mono and IL2CPP is Lightweave's generator float bug wearing a different hat (see *Hard-won facts*), and this
    /// one would have been invisible because nothing was checking.
    /// </para>
    /// <para>
    /// <b>It cycles, and that is load-bearing rather than a convenience.</b> A run may be
    /// handed more drops than the author wrote — a continue does exactly that — so
    /// <see cref="At"/> must answer for any index rather than running off the end into a
    /// colour nobody chose. Cycling is also the honest reading of what the tray shows: a well
    /// deals the same procession over and over, and a player who has watched one lap knows
    /// what the next holds. What that costs is one authoring rule, enforced by the validator:
    /// a deal must carry all three channels, or a mote missing one of them could never be
    /// finished however many drops were bought.
    /// </para>
    /// <para>
    /// <b>Pure colours only.</b> Dealing a blend would hand over a step of the cooking for free,
    /// and the whole mode is that a blend has to be <em>made</em>. It is also what keeps the
    /// rule sayable in one line.
    /// </para>
    /// </summary>
    public sealed class FallDeal
    {
        /// <summary>Most colours a deal may name. Long enough for any procession, short enough to read.</summary>
        public const int MaxLength = 48;

        readonly int[] _colours;

        FallDeal(int[] colours) => _colours = colours;

        public int Count => _colours.Length;

        /// <summary>The colour of drop <paramref name="drop"/>, counting from nought and wrapping.</summary>
        public int At(int drop)
        {
            if (_colours.Length == 0) return Energy.None;

            int i = drop % _colours.Length;
            if (i < 0) i += _colours.Length;
            return _colours[i];
        }

        /// <summary>Every channel this deal can ever supply, as a mask.</summary>
        public int Channels
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _colours.Length; i++) mask |= _colours[i];
                return mask;
            }
        }

        /// <summary>The authored string again, so a tool can round-trip a level.</summary>
        public string Written()
        {
            var chars = new char[_colours.Length];
            for (int i = 0; i < _colours.Length; i++) chars[i] = Energy.Letter(_colours[i]);
            return new string(chars);
        }

        /// <summary>
        /// Reads an authored procession. Spaces and underscores are ignored, so a long one can
        /// be grouped for reading.
        /// </summary>
        public static bool TryParse(string authored, out FallDeal deal, out string error)
        {
            deal = null;
            error = null;

            if (string.IsNullOrEmpty(authored))
            {
                error = "a well has to be told what it deals";
                return false;
            }

            var colours = new List<int>(authored.Length);

            for (int i = 0; i < authored.Length; i++)
            {
                char c = authored[i];
                if (c == ' ' || c == '\t' || c == '_') continue;

                if (!Energy.TryParse(c, out int mask) || mask == Energy.None)
                {
                    error = "'" + c + "' at " + i + " is not a colour; a deal is written in R, G and B";
                    return false;
                }

                // A blend would hand the player a step of the cooking for free. Refused by
                // name rather than silently split, because an author who wrote Y meant
                // something and needs to be told it is not on offer.
                if (mask != Energy.R && mask != Energy.G && mask != Energy.B)
                {
                    error = "'" + c + "' at " + i + " is a blend; a well deals pure light only, " +
                            "so that every blend on the board is one the player made";
                    return false;
                }

                colours.Add(mask);
            }

            if (colours.Count == 0)
            {
                error = "a deal of nothing but spaces deals nothing";
                return false;
            }

            if (colours.Count > MaxLength)
            {
                error = "a deal of " + colours.Count + " is longer than the " + MaxLength +
                        " a procession may name; it repeats, so it never needs to be longer " +
                        "than one lap";
                return false;
            }

            deal = new FallDeal(colours.ToArray());
            return true;
        }
    }

    /// <summary>
    /// One authored well: how big it is, what is already standing in it, and what it deals.
    ///
    /// <para>
    /// <b>Everything a Lightfall level authors, and nothing else.</b> No par, no star line, no
    /// budget — those are derived from this by <see cref="FallSolver"/> and <c>LevelTuning</c>,
    /// for invariant 5's reason: a typed par can drift from the board it claims to describe,
    /// and the drift has no symptom. One too high hands three stars to a careless run for ever;
    /// one too low makes them unreachable; and neither is visible in the file that caused it.
    /// </para>
    /// <para>
    /// <b>Row nought is the brim.</b> The well is drawn with a hard line under its top row, and
    /// a mote that comes to rest above that line has flooded it. So the fill may never touch
    /// row nought — the validator refuses one that does — and <see cref="Headroom"/> counts the
    /// safe rows a careless drop may still spend.
    /// </para>
    /// </summary>
    public sealed class FallLayout
    {
        /// <summary>The row a mote may not come to rest in. See the remarks above.</summary>
        public const int Brim = 0;

        public const int MinWidth = 4, MaxWidth = 8;
        public const int MinHeight = 6, MaxHeight = 14;

        public readonly int Width, Height;

        readonly int[] _fill;

        public readonly FallDeal Deal;

        public FallLayout(int width, int height, int[] fill, FallDeal deal)
        {
            if (width < MinWidth || width > MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinHeight || height > MaxHeight)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            Deal = deal ?? throw new ArgumentNullException(nameof(deal));

            _fill = new int[width * height];
            if (fill != null)
                Array.Copy(fill, _fill, Math.Min(fill.Length, _fill.Length));
        }

        public int Count => _fill.Length;
        public int At(int index) => _fill[index];
        public int At(int x, int y) => _fill[y * Width + x];

        /// <summary>A copy of the ground, for a board about to be played on.</summary>
        public int[] Fill()
        {
            var copy = new int[_fill.Length];
            Array.Copy(_fill, copy, _fill.Length);
            return copy;
        }

        /// <summary>How many motes are standing in the well before anybody touches it.</summary>
        public int Motes
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _fill.Length; i++) if (_fill[i] != Energy.None) n++;
                return n;
            }
        }

        /// <summary>
        /// Lenses standing in it, which is the second chapter's dial and is nought on all of the
        /// first chapter's wells.
        ///
        /// Counted separately from <see cref="Motes"/> rather than instead of it: glass occupies
        /// a cell, falls, blocks a column and has to be got rid of like anything else, so it is
        /// a mote for every purpose except being made of light.
        /// </summary>
        public int Lenses
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _fill.Length; i++) if (FallCell.IsLens(_fill[i])) n++;
                return n;
            }
        }

        /// <summary>
        /// Careless drops the tallest column can still take before the well floods.
        ///
        /// <para>
        /// The difficulty dial that has nothing to do with size or length: a well filled to
        /// within two rows of the brim makes every wasted mote frightening, and one filled
        /// halfway leaves the supply as the only thing that binds. A chapter's ladder is board
        /// size, what is standing in it and this — and every graded number falls out of the
        /// three (invariant 5d: a rule that rejects no arrangement is decoration, so the
        /// arrangements have to be rejected by something authored).
        /// </para>
        /// </summary>
        public int Headroom
        {
            get
            {
                int highest = Height;
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        if (_fill[y * Width + x] == Energy.None) continue;
                        if (y < highest) highest = y;
                        break;
                    }
                }

                // Row nought is the brim itself, so it is not room.
                int safe = highest - 1;
                return safe < 0 ? 0 : safe;
            }
        }

        /// <summary>Every channel some mote in the fill is still missing, as a mask.</summary>
        public int Wanted
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _fill.Length; i++) mask |= FallCell.Wants(_fill[i]);
                return mask;
            }
        }

        /// <summary>
        /// Reads an authored fill: one row per line, one letter per column, a full stop for
        /// empty ground and <c>O</c> for a lens. Spaces are ignored, so a row may be written
        /// spaced out for reading.
        /// </summary>
        public static bool TryReadRows(string[] rows, int width, int height,
                                       out int[] fill, out string error)
        {
            fill = null;
            error = null;

            if (rows == null || rows.Length == 0)
            {
                error = "a well has to be told what is standing in it";
                return false;
            }

            if (rows.Length != height)
            {
                error = "declared " + height + " rows and wrote " + rows.Length;
                return false;
            }

            var cells = new int[width * height];

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
                                " columns this well declares";
                        return false;
                    }

                    if (FallCell.TryParse(c, out int cell))
                    {
                        // White is refused rather than burst at setup: a board that detonates
                        // before anybody has touched it is a board whose author meant something
                        // else, and reading it as an opening cascade would hide the mistake
                        // behind a very pretty animation.
                        if (cell == Energy.All)
                        {
                            error = "row " + y + " column " + x + " is already white, so the " +
                                    "well would burst before the player had touched it";
                            return false;
                        }

                        // The same refusal for glass, and for the same reason: a lens holding all
                        // three fires the moment the board is read, so a board authored with one
                        // goes off before anybody has touched it. `w` is refused exactly as `W`
                        // is - the charge is authorable up to two of three and no further.
                        if (cell == FallCell.Full)
                        {
                            error = "row " + y + " column " + x + " is glass already full, so it " +
                                    "would fire before the player had touched it";
                            return false;
                        }

                        cells[y * width + x] = cell;
                    }
                    else
                    {
                        error = "'" + c + "' at row " + y + " column " + x + " is not a mote; " +
                                "a well is written in R, G, B, Y, M and C for light, " +
                                "the same letters in lower case for glass already that full, " +
                                FallCell.LensLetter + " for empty glass, and '.' for bare ground";
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

            fill = cells;
            return true;
        }
    }
}
