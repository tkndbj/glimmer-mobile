using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One Groovekeeper run: a grove, the basket it is dealt, and the dozen lines that let the two
    /// move together.
    ///
    /// <para>
    /// <b>It is small on purpose.</b> All of this lived inside <c>KeeperBoard</c> once, and one
    /// change turned that class into a puzzle model with an economy and a fail state in it —
    /// after which none of the three could be tested without building a grove. <c>FallRun</c> and
    /// <c>WeaveRun</c> are the same split for the same reason, and it is what makes "a tile is
    /// taken exactly once per landed planting" and "a lost run is only ever ended once"
    /// arithmetic over integers rather than a claim about a screen.
    /// </para>
    /// </summary>
    public sealed class KeeperRun
    {
        public readonly KeeperLayout Layout;
        public readonly KeeperBoard Board;
        public readonly KeeperBasket Basket;

        /// <summary>Beds this grove opened with, for the readout and for the abandonment note.</summary>
        public readonly int Beds;

        readonly List<int> _bloomed = new List<int>(5);

        public KeeperRun(KeeperLayout layout, int budget)
        {
            Layout = layout;
            Board = new KeeperBoard(layout);
            Basket = new KeeperBasket(budget);
            Beds = Board.BedsLeft;
        }

        public KeeperDeal Deal => Layout.Deal;

        /// <summary>The tile in hand.</summary>
        public int Next => Basket.Any ? Deal.At(Basket.Spent) : Energy.None;

        /// <summary>What is queued behind it, for the basket. <paramref name="n"/> counts from nought.</summary>
        public int Ahead(int n)
            => n >= 0 && (!Basket.Bounded || n < Basket.Left) ? Deal.At(Basket.Spent + n)
                                                              : Energy.None;

        /// <summary>Tiles spent, which is the grade and the record.</summary>
        public int Spent => Basket.Spent;

        /// <summary>Beds still waiting.</summary>
        public int Left => Board.BedsLeft;

        /// <summary>The most tiles a single planting of this run has bloomed at once.</summary>
        public int Best { get; private set; }

        /// <summary>Blooms this run has opened altogether, beds and ordinary tiles alike.</summary>
        public int Blooms { get; private set; }

        /// <summary>How the run stands. Recomputed rather than cached: it is one walk of a small grove.</summary>
        public KeeperVerdict Verdict => KeeperVerdict.Read(Board, Basket);

        /// <summary>Whether the tile in hand may be planted here right now.</summary>
        public bool CanPlant(int index)
            => Basket.Any && !Verdict.IsOver && Board.CanPlant(Next, index);

        /// <summary>What planting the tile in hand here would make, without planting it.</summary>
        public KeeperGain Preview(int index) => Board.Preview(Next, index);

        /// <summary>Every cell the tile in hand may go on.</summary>
        public void Openings(List<int> into) => Board.Openings(Next, into);

        /// <summary>
        /// Plants the tile in hand and reports what it made, filling <paramref name="bloomed"/>
        /// with the cells that burst.
        ///
        /// <para>
        /// <b>The basket is taken from here and nowhere else</b>, and only once the tile is known
        /// to have landed. A run charged for a tap that could not be honoured is a run that
        /// quietly costs a player a tile for touching stone.
        /// </para>
        /// </summary>
        public KeeperGain Plant(int index, List<int> bloomed)
        {
            if (!CanPlant(index))
            {
                bloomed?.Clear();
                return KeeperGain.Nothing;
            }

            var gain = Board.Plant(Next, index, bloomed ?? _bloomed);
            Basket.Plant();

            Blooms += gain.Blooms;
            if (gain.Blooms > Best) Best = gain.Blooms;

            return gain;
        }

        /// <summary>
        /// Turns the tile in hand back into the ground to bring the next one round.
        ///
        /// <para>
        /// <b>The one move in this mode that changes nothing on the board</b>, and it exists
        /// because a heartbed refuses every colour but its own — so a run can be holding exactly
        /// the wrong tile with the right bed waiting, and without this the honest reading of that
        /// position would be a dead end nobody caused. It costs a tile, which is what stops it
        /// being a free re-deal: the procession is the puzzle (invariant 20e), so moving it on
        /// has to be paid for out of the same pot everything else comes from.
        /// </para>
        /// <para>
        /// <b>Allowed on the last tile too, and that is deliberate rather than an oversight.</b>
        /// Withholding it there reads as protective and is the one setting that can produce a
        /// grove which will not end: a last tile that no cell will take — every opening a
        /// heartbed of another colour — would be unplayable and unspendable at once, which is
        /// invariant 20g's state exactly. Composting it loses the run, visibly, with the basket
        /// reading one; that is a decision somebody takes with their eyes open, and it is the
        /// only reading under which every position in this mode has a move.
        /// </para>
        /// </summary>
        public bool CanCompost => Basket.Any && !Verdict.IsOver;

        /// <summary>Spends the tile in hand without planting it. See <see cref="CanCompost"/>.</summary>
        public bool Compost()
        {
            if (!CanCompost) return false;

            Basket.Compost();
            return true;
        }

        /// <summary>
        /// Deals more tiles, because a continue was paid for.
        ///
        /// Nothing else moves: the grove stands exactly as it stood, and <see cref="Spent"/> —
        /// the grade — is untouched, so a bought run scores what it spent. Since a run only
        /// reaches the offer after spending past the two-star line, it can score one star at most
        /// (invariant 23).
        /// </summary>
        public void Grant(int tiles) => Basket.Grant(tiles);
    }
}
