using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// One Shuffle run: the build it holds, the hands it has been dealt, and when the next one
    /// is owed.
    ///
    /// <para>
    /// <b>A hand every <see cref="PicksEvery"/> waves, and the board decides when.</b> The
    /// screen asks <see cref="Owed"/> every frame it runs; a hand is owed once the hill has been
    /// cleared of wave <c>2n</c> (the breather, where a player has a moment to read three cards)
    /// or, if the clock sent wave <c>2n+1</c> first, the moment it steps out. Either way the
    /// panel holds the run (<c>RunHold.Covered</c>), so the choice is never made against a
    /// walking hill.
    /// </para>
    /// <para>
    /// <b>Seeded per run and stored nowhere.</b> A build is a thing that happens, not a thing
    /// that is kept: a run that ends is gone, and the next one is dealt afresh. The seed is the
    /// screen's to choose (the clock, or a fixture's constant), so a test can deal the same hands
    /// twice and a player never meets the same run twice.
    /// </para>
    /// </summary>
    public sealed class ShuffleRun
    {
        /// <summary>Waves between one hand and the next.</summary>
        public const int PicksEvery = 2;

        readonly List<List<ShuffleCard>> _hands = new List<List<ShuffleCard>>(16);

        public ShuffleBuild Build { get; }

        public uint Seed { get; }

        public ShuffleRun(uint seed)
        {
            Seed = seed;
            Build = new ShuffleBuild(seed);
        }

        /// <summary>Hands dealt so far.</summary>
        public int Hands => _hands.Count;

        /// <summary>The hand on the table, or null when none is waiting to be taken from.</summary>
        public IReadOnlyList<ShuffleCard> Open { get; private set; }

        /// <summary>
        /// How many hands a run that has got to <paramref name="wavesCleared"/> waves has earned.
        /// </summary>
        public static int Earned(int wavesCleared)
            => wavesCleared < PicksEvery ? 0 : wavesCleared / PicksEvery;

        /// <summary>
        /// Whether a hand is owed and may be dealt now: the board has cleared enough waves for a
        /// hand this run has not had, and no hand is already on the table.
        ///
        /// <para>
        /// <b>Cleared is read two ways</b>: the waves the board counts as seen off, or - on an
        /// empty hill - the wave that was just on it, so the hand comes in the breather after
        /// wave two rather than as wave three steps out. The second reading can only ever be one
        /// ahead of the first, and only while nothing walks.
        /// </para>
        /// </summary>
        public bool Owed(SiegeBoard board)
        {
            if (board == null || Open != null || !board.Opened || board.IsFinished) return false;

            int cleared = board.WavesCleared;
            if (board.OnTheHill == 0 && board.Wave > cleared) cleared = board.Wave;

            return Earned(cleared) > _hands.Count;
        }

        /// <summary>
        /// Deals the next hand and puts it on the table. Empty when the deck has nothing left the
        /// build can take, in which case nothing is on the table and the run carries on.
        /// </summary>
        public IReadOnlyList<ShuffleCard> Deal()
        {
            var hand = ShuffleDeck.Deal(Build, _hands.Count + 1, Build.Boosts.Roll100);
            _hands.Add(hand);

            Open = hand.Count > 0 ? hand : null;
            return hand;
        }

        /// <summary>
        /// Takes <paramref name="card"/> off the open hand into the build, and answers whether it
        /// was. A card not on the table is refused, so a stale panel cannot take anything.
        /// </summary>
        public bool Take(ShuffleCard card)
        {
            if (Open == null || card == null) return false;

            bool offered = false;
            for (int i = 0; i < Open.Count; i++)
                if (ReferenceEquals(Open[i], card)) { offered = true; break; }

            if (!offered || !Build.Take(card)) return false;

            Open = null;
            return true;
        }

        /// <summary>
        /// Every hand dealt so far, for a fixture that asks what a run was offered.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<ShuffleCard>> Dealt => _hands;

        /// <summary>
        /// A seed for a fresh run: the clock's ticks folded to thirty-two bits, never nought.
        /// </summary>
        public static uint SeedNow()
        {
            long ticks = DateTime.UtcNow.Ticks;
            uint seed = unchecked((uint)ticks ^ (uint)(ticks >> 32));
            return seed == 0u ? 1u : seed;
        }
    }
}
