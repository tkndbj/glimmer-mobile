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

        /// <summary>
        /// The figures this run deals and plays on: the snapshot taken when it opened (MODES.md
        /// 59l), never re-read, so a tuning that arrives mid-run changes nothing until the next.
        /// </summary>
        public ShuffleTuning Tuning { get; }

        /// <summary>A run on the shipped figures. What a fixture deals.</summary>
        public ShuffleRun(uint seed) : this(seed, ShuffleTuning.Shipped) { }

        /// <summary>A run on <paramref name="tuning"/> (the shipped figures for null).</summary>
        public ShuffleRun(uint seed, ShuffleTuning tuning)
        {
            Seed = seed;
            Tuning = tuning ?? ShuffleTuning.Shipped;
            Build = new ShuffleBuild(seed, Tuning.Arsenal);
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
            var hand = ShuffleDeck.Deal(Build, _hands.Count + 1, Build.Boosts.Roll100, Tuning);
            _hands.Add(hand);

            Open = hand.Count > 0 ? hand : null;
            return hand;
        }

        /// <summary>
        /// Refreshes bought on this run (MODES.md 59p). Never reset by a new hand, only by a new
        /// run - which is a new <see cref="ShuffleRun"/>.
        /// </summary>
        public int Refreshes { get; private set; }

        /// <summary>What the next refresh of this run costs, in gems (<see cref="ShuffleRefresh.PriceFor"/>).</summary>
        public long RefreshPrice => ShuffleRefresh.PriceFor(Refreshes);

        /// <summary>
        /// Whether the open hand can be refreshed: a hand is on the table and the deck holds at
        /// least one card the build can take that the hand does not already offer. A refresh
        /// that could only deal the same cards back is never sold.
        /// </summary>
        public bool CanRefresh => Open != null && ShuffleDeck.HasFresh(Build, Open, Tuning);

        /// <summary>
        /// Swaps the open hand for a fresh one and counts it, answering the new hand, or null
        /// (changing nothing) when <see cref="CanRefresh"/> is false. Pays for nothing itself:
        /// the debit is <see cref="ShuffleRefresh.TryBuy"/>'s.
        ///
        /// <para>
        /// <b>The same hand, dealt again</b>: the same hand number, so the same tier table and
        /// the same promise on its first slot (a refresh of an epic-promised hand still opens on
        /// an epic or better), off the run's own stream (59g). The cards just offered are spared
        /// while anything else is left to deal (<see cref="ShuffleDeck.Deal(ShuffleBuild, int, System.Func{int}, ShuffleTuning, IReadOnlyList{ShuffleCard})"/>),
        /// and the hand replaces the one it refreshed rather than adding one, so
        /// <see cref="Owed"/> and <see cref="Hands"/> never see it.
        /// </para>
        /// </summary>
        public IReadOnlyList<ShuffleCard> Refresh()
        {
            if (!CanRefresh) return null;

            var hand = ShuffleDeck.Deal(Build, _hands.Count, Build.Boosts.Roll100, Tuning, Open);

            // Unreachable while `HasFresh` holds (a fresh card is a card the first slot can
            // always fall back to), and guarded anyway: an empty hand would leave a panel with
            // nothing on it to take.
            if (hand.Count == 0) return null;

            _hands[_hands.Count - 1] = hand;
            Open = hand;
            Refreshes++;
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
        /// Every hand dealt so far, for a fixture that asks what a run was offered: a refreshed
        /// hand as it last stood.
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
