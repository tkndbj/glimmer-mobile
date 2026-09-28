using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>What the next level on offer is, and whether it can be bought at all.</summary>
    public readonly struct KeeperOffer
    {
        /// <summary>Whether a level is for sale right now. False for every other field's sake.</summary>
        public readonly bool Sold;

        /// <summary>Whether the player already stands at the top of the ladder.</summary>
        public readonly bool AtTop;

        /// <summary>The level a purchase would reach.</summary>
        public readonly int Level;

        /// <summary>The ordinal this purchase would be - the first bought level is 1.</summary>
        public readonly int Ordinal;

        public readonly string Currency;
        public readonly long Price;

        public KeeperOffer(bool sold, bool atTop, int level, int ordinal, string currency, long price)
        {
            Sold = sold;
            AtTop = atTop;
            Level = level;
            Ordinal = ordinal;
            Currency = currency;
            Price = price;
        }

        public static readonly KeeperOffer None = new KeeperOffer(false, false, 0, 0, null, 0L);
    }

    /// <summary>How a purchase ended.</summary>
    public enum KeeperBuy
    {
        Bought,
        /// <summary>The ladder sells nothing, or this build has no published ladder.</summary>
        NotSold,
        /// <summary>The player already stands at the ladder's top.</summary>
        AtTop,
        /// <summary>The wallet could not cover the price.</summary>
        TooPoor,
    }

    /// <summary>
    /// The keeper levels this account has bought - the client half of invariant 57.
    ///
    /// <para>
    /// <b>The count is what is stored; the level is derived from it.</b> A purchase is the
    /// season pass's shape (47e): the debit goes first under a derived id
    /// (<see cref="SpendEntry.KeeperLevelId"/>), the count is raised only if it succeeded, and
    /// the server prices the same id against the published ladder and raises its own count on
    /// the wallet document in the transaction that takes the money. The client's copy draws the
    /// screens and gates what a level gates on the device; it is a hint the server's answer
    /// overrides in both directions (<see cref="ApplyServerState"/>).
    /// </para>
    /// <para>
    /// <b>A refused debit takes the level back</b>, and every level bought on top of it: the
    /// ordinals above a refused one were priced against a level that was never reached. The
    /// money is already back - the ledger drops a refused entry before announcing it (47o) - so
    /// what this undoes is the count beside it.
    /// </para>
    /// </summary>
    public static class KeeperLedger
    {
        static bool _hooked;

        /// <summary>Raised whenever the bought count moves, by purchase, refusal or sync.</summary>
        public static event Action Changed;

        static KeeperLedger() => Hook();

        /// <summary>
        /// Installs the refusal listener. Idempotent, and called from
        /// <c>PlayerProgression</c>'s own hook as well as from here so that a refusal arriving
        /// before any screen has asked this class a question is still heard.
        /// </summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CurrencyLedger.SpendRejected += OnSpendRejected;
        }

        /// <summary>How many levels this account has bought. Never negative.</summary>
        public static int Bought => Wallet.KeeperLevelsBought;

        /// <summary>The ladder this build sells from - content, so retunable without a build.</summary>
        public static KeeperLadder Ladder => ProgressionRules.Table.KeeperLevels;

        /// <summary>
        /// The next level on offer: the one above where the player stands now, priced off the
        /// ladder. Reads the <em>effective</em> level - earned plus bought - because that is the
        /// rung the purchase reaches and the one the server prices.
        /// </summary>
        public static KeeperOffer Next()
        {
            var ladder = Ladder;
            if (!ladder.Sells) return KeeperOffer.None;

            int standing = PlayerProgression.Level.Level;
            int level = standing + 1;
            int ordinal = Bought + 1;

            if (standing >= ladder.Top) return new KeeperOffer(false, true, level, ordinal, null, 0L);
            if (!ladder.PriceFor(level, out string currency, out long price)) return KeeperOffer.None;

            return new KeeperOffer(true, false, level, ordinal, currency, price);
        }

        /// <summary>
        /// Buys the next level.
        ///
        /// <b>The debit goes first and the count is raised only if it succeeded</b>, which is
        /// <c>SeasonLedger.TryBuyPass</c>'s ordering and its argument: a process killed between
        /// the two leaves a player who paid and did not receive, which the spend log can see and
        /// support can put right, where the other order leaves a level nobody paid for.
        /// </summary>
        public static KeeperBuy TryBuy()
        {
            var offer = Next();
            if (offer.AtTop) return KeeperBuy.AtTop;
            if (!offer.Sold) return KeeperBuy.NotSold;

            if (!PlayerProgression.TrySpend(offer.Currency, offer.Price, SpendEntry.KeeperLevelReason,
                                            SpendEntry.KeeperLevelId(offer.Ordinal, offer.Level)))
                return KeeperBuy.TooPoor;

            Wallet.RaiseKeeperLevelsBought(offer.Ordinal);
            SaveService.Save();
            Raise();

            Telemetry.Track("keeper_level_bought",
                            "level", offer.Level, "ordinal", offer.Ordinal,
                            "currency", offer.Currency, "price", offer.Price);

            return KeeperBuy.Bought;
        }

        /// <summary>
        /// Folds the server's count in.
        ///
        /// <para>
        /// <b>Upward always, downward only when nothing is in flight.</b> The wallet document is
        /// the entitlement, so a count it holds that this device does not is a purchase made on
        /// another phone, taken at once. A count <em>below</em> this device's is either a
        /// purchase this device made that the server has not yet seen - in which case the debit
        /// is still pending here and the count stands until the server answers it - or a level
        /// this device believes in that the server never recorded, which a refused debit would
        /// ordinarily have taken back and a lost reply did not. The pending test tells the two
        /// apart: with no keeper debit outstanding, the server's lower figure is the truth.
        /// </para>
        /// <para>
        /// <b>Carried is asked separately from the number</b>, for <c>EndlessCoins</c>'s reason:
        /// a fresh account honestly answers nought and a deployment that predates the field also
        /// sends nothing, and only one of those two is something to believe.
        /// </para>
        /// </summary>
        public static void ApplyServerState(bool carried, int bought)
        {
            if (!carried || bought < 0) return;

            int mine = Bought;
            if (bought > mine)
            {
                Wallet.RaiseKeeperLevelsBought(bought);
                Raise();
            }
            else if (bought < mine && !HasPendingDebit())
            {
                UnityEngine.Debug.LogWarning($"[Keeper] the server holds {bought} bought level(s) against " +
                                             $"{mine} here with no purchase in flight; taking the server's figure");
                Wallet.SetKeeperLevelsBought(bought);
                Raise();
            }
        }

        /// <summary>Whether a keeper debit this device raised is still waiting for the server.</summary>
        public static bool HasPendingDebit()
        {
            foreach (string currency in new[] { Currency.Credits, Currency.Gems })
            {
                var ledger = Wallet.Ledger(currency);
                if (ledger == null) continue;

                var pending = ledger.PendingSpends;
                for (int i = 0; i < pending.Count; i++)
                    if (SpendEntry.TryParseKeeperLevelId(pending[i].Id, out _, out _)) return true;
            }
            return false;
        }

        /// <summary>
        /// A keeper debit the server refused takes its level back, and every level bought above
        /// it. Only a refusal at or below the held count moves anything - a refusal for an
        /// ordinal this device never reached is a stale device's business.
        /// </summary>
        internal static void OnSpendRejected(string currency, string spendId)
        {
            if (!SpendEntry.TryParseKeeperLevelId(spendId, out int ordinal, out int level)) return;
            if (ordinal > Bought) return;

            UnityEngine.Debug.LogWarning($"[Keeper] the purchase of level {level} (bought level #{ordinal}) " +
                                         "was refused by the server; it and anything bought above it are " +
                                         "no longer held and the money is back");
            Wallet.SetKeeperLevelsBought(ordinal - 1);
            SaveService.Save();
            Raise();
        }

        static void Raise()
        {
            PlayerProgression.Invalidate();
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
