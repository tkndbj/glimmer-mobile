using System;
using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The gems a Pairs board is dealt from, and the one alphabet a row is written in.
    ///
    /// <para>
    /// <b>A pair is a gem, never a colour</b> (CLAUDE.md 56m). Each of the four turret colours
    /// is <see cref="Variants"/> different stones, written as the colour letter and a variant
    /// (<c>r1</c> .. <c>y6</c>), and two cards pair only when they are the <em>same</em> stone.
    /// The first cut paired on colour alone, so a 6x3 board held four or five of each and any
    /// two reds matched - a memory game with nothing to remember. A kind still belongs to one
    /// colour, and a pair of it feeds that colour's turret, which is the whole fusion.
    /// </para>
    /// <para>
    /// <b>The cursed stone is <c>o</c></b>: no colour, never pairs, and turning it over ends the
    /// turn and walks the hill an extra step (<see cref="PairsPuzzle"/>). It goes back face
    /// down like a miss, so where it lies is one more thing to remember.
    /// </para>
    /// <para>
    /// <b>A kind's picture is derived from its token</b> (<see cref="ArtKey"/>, 7c's shape), so a
    /// row says what it draws and nothing lists the twenty-four by hand; the one gate that walks
    /// them all is <c>ChallengeTests.EveryPairsGemIsOnDisk</c>, because a built address is
    /// invisible to <c>artnames.py</c>.
    /// </para>
    /// </summary>
    public static class PairsGems
    {
        /// <summary>Stones a colour is cut into. Cut by <c>Tools/make_pairs_art.py</c>, one file each.</summary>
        public const int Variants = 6;

        /// <summary>Every kind a row may name: four colours of <see cref="Variants"/> stones.</summary>
        public const int Kinds = ChallengeColours.Count * Variants;

        /// <summary>The cursed stone's kind. Never a pair and never a colour.</summary>
        public const int Curse = -1;

        /// <summary>The cursed stone's token.</summary>
        public const string CurseToken = "o";

        /// <summary>The most cursed stones one board may hold: a quarter of a small board is noise, not memory.</summary>
        public const int MostCurses = 3;

        /// <summary>Reads one token of a row. False for anything that is not a gem or the curse.</summary>
        public static bool TryParse(string token, out int kind)
        {
            kind = Curse;
            if (string.IsNullOrEmpty(token)) return false;
            if (token == CurseToken) return true;
            if (token.Length != 2) return false;

            int colour = ChallengeColours.IndexOf(token[0]);
            int variant = token[1] - '0';
            if (colour < 0 || variant < 1 || variant > Variants) return false;

            kind = colour * Variants + (variant - 1);
            return true;
        }

        /// <summary>The turret a kind feeds, or -1 for the curse.</summary>
        public static int ColourOf(int kind) => kind < 0 ? -1 : kind / Variants;

        /// <summary>A kind's token as a row spells it: <c>r1</c>, or <c>o</c> for the curse.</summary>
        public static string TokenOf(int kind)
            => kind < 0 ? CurseToken : ChallengeColours.LetterOf(ColourOf(kind)).ToString() + (kind % Variants + 1);

        /// <summary>The picture's address under <c>Art/Challenge/</c>: <c>pair_r1</c>, <c>pair_curse</c>.</summary>
        public static string ArtKey(int kind) => kind < 0 ? "pair_curse" : "pair_" + TokenOf(kind);

        /// <summary>A row split into its tokens, however many spaces stand between them.</summary>
        public static string[] Tokens(string row)
            => string.IsNullOrEmpty(row) ? new string[0] : row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Pairs: a grid of face-down cards. Turn two; the same gem twice is a pair, and a pair
    /// fires the turret of its colour.
    ///
    /// <para>
    /// <b>A turn is two flips, whichever way they went.</b> The first flip is free - it is half
    /// a decision - and the second settles it: a match stays up and feeds the ward of its
    /// colour, a miss goes back down and the hill still walks. So a remembered board is a fast
    /// line and a forgotten one is a slow one, which is the whole of what memory is worth here.
    /// </para>
    /// <para>
    /// <b>Pairs made back to back are a combo</b>: the <c>n</c>-th match in a row pays
    /// <c>Bolts x min(n, ComboCap)</c>. It is what makes <em>when</em> to cash a pair a
    /// decision (invariant 5d asked of a readout): a player holding two known pairs can take
    /// them together for a combo or spend a turn exploring between them and break it, and the
    /// hill is what prices the wait. A miss or a curse ends the run of matches.
    /// </para>
    /// <para>
    /// <b>A cursed stone</b> (<see cref="PairsGems.Curse"/>) ends the turn the moment it is
    /// turned, first flip or second, sends any card already up back down, breaks the combo, and
    /// walks the hill one step more than a turn does (<see cref="ChallengeMove.Stumbles"/>). It
    /// goes back face down, so the first one is luck and every one after is memory.
    /// </para>
    /// <para>
    /// <b>The row authors which cards are dealt, never where</b>: every play is shuffled by a
    /// deal the ledger derives from the day and the attempt (<c>ChallengePlay.Deal</c>), so every
    /// player's first try of a day is the same board and a retry is a fresh one. An authored
    /// layout would be learned by losing it once - the one exploit a memory game has - and
    /// shared the morning it was dealt. A deal of nought keeps the authored layout, which is
    /// what lets a fixture name the cells it turns.
    /// </para>
    /// </summary>
    public sealed class PairsPuzzle : IChallengePuzzle
    {
        public enum Face { Hidden, Up, Matched }

        /// <summary>The most a combo multiplies a pair by: the third match in a row and every one after.</summary>
        public const int ComboCap = 3;

        readonly int[] _kind;
        readonly Face[] _face;
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();

        int _first = -1;

        public PairsPuzzle(ChallengeDefinition def, uint deal = 0u)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts < 1 ? 1 : def.Bolts;

            _kind = new int[Width * Height];
            _face = new Face[_kind.Length];

            for (int y = 0; y < Height; y++)
            {
                var tokens = PairsGems.Tokens(def.Rows[y]);
                for (int x = 0; x < Width; x++)
                {
                    PairsGems.TryParse(tokens[x], out int kind);
                    _kind[y * Width + x] = kind;
                    if (kind == PairsGems.Curse) Curses++;
                }
            }

            if (deal != 0u) Shuffle(def.Seed, deal);
        }

        /// <summary>
        /// Fisher-Yates over <see cref="ChallengeRng"/>, seeded by the row and the deal mixed
        /// together, so one row dealt twice on one attempt is one board on every device.
        /// </summary>
        void Shuffle(uint seed, uint deal)
        {
            var rng = new ChallengeRng(ChallengeCalendar.Mix(seed * 0x9E3779B1u ^ deal) | 1u);
            for (int i = _kind.Length - 1; i > 0; i--)
            {
                int j = rng.Below(i + 1);
                int t = _kind[i];
                _kind[i] = _kind[j];
                _kind[j] = t;
            }
        }

        public ChallengeGenre Genre => ChallengeGenre.Pairs;
        public int Width { get; }
        public int Height { get; }

        /// <summary>The gem a cell holds (<see cref="PairsGems"/>), or <see cref="PairsGems.Curse"/>.</summary>
        public int KindAt(int cell) => cell >= 0 && cell < _kind.Length ? _kind[cell] : PairsGems.Curse;

        /// <summary>The turret a cell's gem feeds, or -1 for a cursed stone.</summary>
        public int ColourAt(int cell) => PairsGems.ColourOf(KindAt(cell));

        public bool IsCurse(int cell) => cell >= 0 && cell < _kind.Length && _kind[cell] == PairsGems.Curse;

        public Face FaceAt(int cell) => cell >= 0 && cell < _face.Length ? _face[cell] : Face.Hidden;

        /// <summary>The cell turned up and waiting for its partner, or -1.</summary>
        public int First => _first;

        /// <summary>Cursed stones on the board.</summary>
        public int Curses { get; }

        /// <summary>Pairs on the board, which is every card that is not cursed, halved.</summary>
        public int Pairs => (_kind.Length - Curses) / 2;

        public int Matched { get; private set; }

        /// <summary>Matches made back to back, up to now. Nought after a miss or a curse.</summary>
        public int Streak { get; private set; }

        public bool Solved => Matched == Pairs;
        public bool Failed => false;

        // ------------------------------------------------------------------ the last turn, for the view
        /// <summary>The two cells of the last completed turn, or -1. A curse turned first leaves <see cref="LastA"/> at -1.</summary>
        public int LastA { get; private set; } = -1;
        public int LastB { get; private set; } = -1;
        public bool LastMatched { get; private set; }

        /// <summary>Whether the last completed turn was ended by a cursed stone, which is <see cref="LastB"/>.</summary>
        public bool LastCursed { get; private set; }

        /// <summary>What the last match paid its turret, combo counted in. Nought for anything else.</summary>
        public int LastPaid { get; private set; }

        /// <summary>What the last match's combo multiplied it by: one for a match on its own.</summary>
        public int LastCombo { get; private set; }

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();

            if (input.Kind != ChallengeInputKind.Tap || input.Cell < 0 || input.Cell >= _face.Length
                || _face[input.Cell] != Face.Hidden)
            {
                _move.Refused = true;
                return _move;
            }

            int cell = input.Cell;

            if (_kind[cell] == PairsGems.Curse)
            {
                // The turn ends here, whichever flip this was: a card already up goes back
                // down with it, and the combo is broken.
                LastA = _first;
                LastB = cell;
                LastMatched = false;
                LastCursed = true;
                LastPaid = 0;
                LastCombo = 0;

                if (_first >= 0) _face[_first] = Face.Hidden;
                _first = -1;
                Streak = 0;

                _move.Turn = true;
                _move.Stumbles = 1;
                return _move;
            }

            if (_first < 0)
            {
                _face[cell] = Face.Up;
                _first = cell;
                return _move;
            }

            int a = _first, b = cell;
            _first = -1;

            LastA = a;
            LastB = b;
            LastCursed = false;
            LastMatched = _kind[a] == _kind[b];

            if (LastMatched)
            {
                _face[a] = _face[b] = Face.Matched;
                Matched++;
                Streak++;
                LastCombo = Math.Min(Streak, ComboCap);
                LastPaid = _bolts * LastCombo;
                _move.Feed(PairsGems.ColourOf(_kind[a]), LastPaid);
            }
            else
            {
                _face[a] = _face[b] = Face.Hidden;
                Streak = 0;
                LastPaid = 0;
                LastCombo = 0;
            }

            _move.Turn = true;
            return _move;
        }

        /// <summary>
        /// Why a row cannot be dealt, or null. Each row is <c>width</c> tokens separated by
        /// spaces; every gem appears an even number of times; there is a pair to make; and no
        /// more than <see cref="PairsGems.MostCurses"/> cursed stones.
        /// </summary>
        public static string Fault(ChallengeDefinition def)
        {
            if (def.Rows == null || def.Rows.Length != def.Height)
                return $"pairs must have {def.Height} row(s), has {(def.Rows == null ? 0 : def.Rows.Length)}";

            var counts = new Dictionary<int, int>();
            int curses = 0;

            for (int y = 0; y < def.Height; y++)
            {
                var tokens = PairsGems.Tokens(def.Rows[y]);
                if (tokens.Length != def.Width)
                    return $"pairs row {y} must hold {def.Width} gem token(s) separated by spaces (like 'r1 g2'), holds {tokens.Length}";

                foreach (var token in tokens)
                {
                    if (!PairsGems.TryParse(token, out int kind))
                    {
                        if (token.Length == 1 && ChallengeColours.IndexOf(token[0]) >= 0)
                            return $"pairs row {y} holds '{token}', which is a colour and not a gem; name the stone ('{token}1' .. '{token}{PairsGems.Variants}')";
                        return $"pairs row {y} holds '{token}', which is not a gem ('r1' .. 'y{PairsGems.Variants}') or the curse ('{PairsGems.CurseToken}')";
                    }

                    if (kind == PairsGems.Curse) { curses++; continue; }
                    counts.TryGetValue(kind, out int n);
                    counts[kind] = n + 1;
                }
            }

            foreach (var pair in counts)
                if ((pair.Value & 1) == 1)
                    return $"pairs deals {pair.Value} '{PairsGems.TokenOf(pair.Key)}', which cannot all pair";

            if (counts.Count == 0) return "pairs needs at least one pair";
            if (curses > PairsGems.MostCurses)
                return $"pairs deals {curses} cursed stones; at most {PairsGems.MostCurses} fit on one board";

            return null;
        }
    }
}
