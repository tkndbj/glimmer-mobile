using System;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The player's level, XP and balances, as the game reads them.
    ///
    /// Everything here is recomputed from the star ledger, the catalog and the reward
    /// table. Nothing is stored except the floors in <see cref="ProgressionStore"/> and
    /// the non-derivable halves of the wallet, so there is no value to drift out of
    /// step with the records it came from.
    ///
    /// The result is cached because a HUD may ask several times a frame, and the cache
    /// is invalidated by the three things that can change the answer: a run being
    /// recorded, the catalog being republished, and the reward table being retuned.
    /// </summary>
    public static class PlayerProgression
    {
        static ProgressionTotals _totals = ProgressionTotals.Zero;
        static PlayerLevel _level;
        static bool _dirty = true;
        static bool _hooked;

        /// <summary>Raised after a recompute changes anything a screen might be showing.</summary>
        public static event Action Changed;

        static PlayerProgression() => Hook();

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;

            PlayerProgress.RecordChanged += _ => Invalidate();
            PlayerProgress.Reloaded += Invalidate;
            GameContent.CatalogChanged += Invalidate;
            ProgressionRules.Changed += Invalidate;

            // Collecting an event rung moves a floor that this total reads. Without this the
            // credits would not appear until something else happened to invalidate — which
            // is exactly the silent arrival collecting by hand exists to avoid.
            Events.EventCollection.Changed += Invalidate;
        }

        /// <summary>Forces the next read to recompute. Cheap; safe to call often.</summary>
        public static void Invalidate()
        {
            _dirty = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------- reading
        public static ProgressionTotals Totals { get { EnsureFresh(); return _totals; } }

        /// <summary>Lifetime XP, floored so it can never fall. See <see cref="ProgressionStore"/>.</summary>
        public static long Xp { get { EnsureFresh(); return _level.TotalXp; } }

        public static PlayerLevel Level { get { EnsureFresh(); return _level; } }

        /// <summary>Credits earned from play, before grants and spends.</summary>
        public static long EarnedCredits { get { EnsureFresh(); return _totals.EarnedCredits; } }

        public static int ClearedGlades { get { EnsureFresh(); return _totals.ClearedGlades; } }

        public static int TotalStars { get { EnsureFresh(); return _totals.TotalStars; } }

        /// <summary>The spendable balance of a currency.</summary>
        public static long Balance(string currency)
        {
            EnsureFresh();
            return Wallet.Ledger(currency).BalanceFrom(DerivedEarnedFor(currency));
        }

        public static long Credits => Balance(Currency.Credits);

        public static long Gems => Balance(Currency.Gems);

        /// <summary>
        /// What a finished run added, for the win screen to count up.
        ///
        /// Computed from the record before and after rather than from the run itself,
        /// so a replay that does not beat the old result correctly shows nothing.
        /// </summary>
        public static ProgressionTotals RewardFor(LevelRecord before, LevelRecord after)
        {
            // The index answers this without reading anything: which chapter a glade
            // belongs to is manifest knowledge, and the win screen must not stall on a
            // file read to show a number.
            var chapter = GameContent.ChapterOf(after?.Id ?? LevelId.None);

            return ProgressionLedger.Delta(before, after, chapter, ProgressionRules.Table,
                                           RewardSeed.PlayerKey);
        }

        /// <summary>
        /// What multiplier this player's copy of a glade pays, as a percentage. 100 is the
        /// ordinary reward.
        ///
        /// Exposed so the victory panel can say when a glade paid more than it should have.
        /// That announcement is the entire reason the bonus is worth having — a variable
        /// reward the player never notices is just noise in the economy — but it is only
        /// ever a report of arithmetic that has already happened, and asking here gives the
        /// same answer the ledger used.
        /// </summary>
        public static int GoldenPercentFor(LevelId level)
            => ProgressionRules.Table.Golden.PercentFor(RewardSeed.PlayerKey, level);

        // ------------------------------------------------------------- spending
        /// <summary>
        /// Debits a currency if the player can afford it.
        ///
        /// Applied locally at once so a shop stays usable offline, but recorded with an
        /// idempotency key so the backend can charge it exactly once however many times
        /// the submission is retried.
        /// </summary>
        public static bool TrySpend(string currency, long amount, string reason)
        {
            EnsureFresh();

            var ledger = Wallet.Ledger(currency);
            if (!ledger.TrySpend(amount, DerivedEarnedFor(currency), reason, out _)) return false;

            SaveService.Save();
            Invalidate();
            return true;
        }

        public static bool CanAfford(string currency, long amount)
            => amount <= 0 || Balance(currency) >= amount;

        // ------------------------------------------------------------- awarding
        /// <summary>
        /// Credits a currency for something the player was given rather than earned.
        ///
        /// <para>
        /// Note what this does <em>not</em> do: it does not raise the granted baseline.
        /// That field is the server's, because a client that can raise it is a client that
        /// can print money, and it is the only field an attacker with real purchases in
        /// play is interested in. What lands here instead is a claim carrying an id
        /// derived from whatever earned it, which counts toward the balance immediately —
        /// so a reward opened on a plane is spendable on that plane — and which the server
        /// either confirms into the baseline or replaces with its own figure on the next
        /// sync.
        /// </para>
        /// <para>
        /// Returns false when an award with that id is already held, which is how a reward
        /// that can only be given once says so. Callers do not need to remember anything.
        /// </para>
        /// </summary>
        public static bool Award(string currency, long amount, string id, string reason, long unix)
        {
            if (amount <= 0 || string.IsNullOrEmpty(id)) return false;

            EnsureFresh();

            if (!Wallet.Ledger(currency).TryAward(id, amount, unix, reason, out _)) return false;

            SaveService.Save();
            Invalidate();
            return true;
        }

        // ------------------------------------------------------------- internals
        /// <summary>
        /// Which derived earnings back a currency. Only credits are earned by playing;
        /// gems are granted, so their derived component is zero and their balance is
        /// entirely grants minus spends.
        /// </summary>
        static long DerivedEarnedFor(string currency)
            => currency == Currency.Credits ? _totals.EarnedCredits : 0L;

        static void EnsureFresh()
        {
            if (!_dirty) return;
            _dirty = false;

            var table = ProgressionRules.Table;

            // Once, on the first derivation that has a catalog to read: a file from a build
            // which paid event milestones automatically has already been paid for every rung
            // it reached, so those rungs start collected rather than lighting up the page.
            // Here rather than in the save migration because a migration cannot see content.
            if (GameContent.IsLoaded)
            {
                Events.EventCollection.SeedIfNeeded(GameContent.Index.Events,
                                                    PlayerProgress.RecordsById);
            }

            // The seed is read here, at the one place the live totals are derived, rather
            // than inside the ledger — see ProgressionLedger.Value for why that has to stay
            // a pure function. Empty before the first sign-in, which pays the base and can
            // only ever be revised upward afterwards.
            _totals = ProgressionLedger.Compute(PlayerProgress.Records, GameContent.Index, table,
                                                RewardSeed.PlayerKey, GameContent.Index.Events,
                                                Events.EventCollection.Floors);

            // Three floors, applied as one: whichever demands the most XP wins, and
            // everything downstream — level, progress bar, remaining XP — then stays
            // internally consistent instead of being patched up afterwards.
            long effectiveXp = _totals.Xp;
            if (ProgressionStore.XpHighWater > effectiveXp) effectiveXp = ProgressionStore.XpHighWater;

            long levelFloorXp = table.XpToReach(ProgressionStore.LevelHighWater);
            if (levelFloorXp > effectiveXp) effectiveXp = levelFloorXp;

            _level = table.LevelFor(effectiveXp);

            RatchetFloors(effectiveXp);
        }

        /// <summary>
        /// Records today's values as tomorrow's minimum.
        ///
        /// Marks the save dirty rather than writing: this runs inside a read, possibly
        /// mid-frame, and a floor moving is never urgent enough to justify touching the
        /// disk there. The next ordinary save or the flush on backgrounding carries it.
        /// </summary>
        static void RatchetFloors(long effectiveXp)
        {
            bool moved = ProgressionStore.RaiseXp(effectiveXp);
            moved |= ProgressionStore.RaiseLevel(_level.Level);
            moved |= Wallet.Ledger(Currency.Credits).RaiseEarnedHighWater(_totals.EarnedCredits);

            // Keeps the retired v1 balance fields meaningful for a rolled-back client.
            Wallet.MirrorLegacyBalance(Currency.Credits,
                                       Wallet.Ledger(Currency.Credits).BalanceFrom(_totals.EarnedCredits));
            Wallet.MirrorLegacyBalance(Currency.Gems, Wallet.Ledger(Currency.Gems).BalanceFrom(0L));

            if (moved) SaveService.MarkDirty();
        }
    }
}
