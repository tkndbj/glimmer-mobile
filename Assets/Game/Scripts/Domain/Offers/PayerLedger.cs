using System;
using GlimmerGrove.Cloud;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// <b>What the server has recorded this account paying</b> (invariant 64): how many real-money
    /// purchases, what they cost in all and at most, and when the last one was - the four facts the
    /// offer engine's money segments read.
    ///
    /// <para>
    /// <b>The server's, never the device's.</b> <c>redeemPurchase</c> raises the counters in the
    /// transaction that grants a purchase (<c>functions/src/payer.ts</c>), so they count only what a
    /// store validated, and every wallet reply carries them. They only climb - a refund reverses the
    /// currency, never the history of having paid - so the ladder of offers never steps back down
    /// because a sync was late. Nothing is in the save (18a).
    /// </para>
    /// <para>
    /// <b>Nothing is offered until a reply has said</b> (<see cref="Known"/>): a deployment whose
    /// wallet predates these fields says nothing, and a phone that guessed "never paid" would offer a
    /// first-purchase price to somebody who already paid more - presence first, the wheel's trick.
    /// Forgotten on an account switch, because it is a fact about one account.
    /// </para>
    /// </summary>
    public static class PayerLedger
    {
        static bool _hooked;

        public static event Action Changed;

        /// <summary>Whether a wallet reply has said what this account paid.</summary>
        public static bool Known { get; private set; }

        public static int Purchases { get; private set; }

        /// <summary>Everything paid, in reference US cents (the catalog's, never a storefront's).</summary>
        public static long SpendCents { get; private set; }

        /// <summary>The largest single purchase, in reference US cents.</summary>
        public static long TopCents { get; private set; }

        /// <summary>When the last purchase was granted, or nought.</summary>
        public static long LastUnix { get; private set; }

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CloudSaveService.IdentityChanged += Forget;
        }

        /// <summary>
        /// Folds in a wallet reply. Ignored when the reply did not carry the fields; each figure only
        /// climbs, so a reply that crossed a purchase in flight cannot take one back.
        /// </summary>
        public static void ApplyServerState(bool carried, int purchases, long spendCents, long topCents, long lastUnix)
        {
            if (!carried) return;

            bool changed = !Known;
            Known = true;

            if (purchases > Purchases) { Purchases = purchases; changed = true; }
            if (spendCents > SpendCents) { SpendCents = spendCents; changed = true; }
            if (topCents > TopCents) { TopCents = topCents; changed = true; }
            if (lastUnix > LastUnix) { LastUnix = lastUnix; changed = true; }

            if (changed) Raise();
        }

        static void Forget()
        {
            if (!Known && Purchases == 0) return;
            Known = false;
            Purchases = 0;
            SpendCents = 0L;
            TopCents = 0L;
            LastUnix = 0L;
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>Forgets everything, as a fresh process would. For fixtures.</summary>
        internal static void ResetForTests()
        {
            Known = false;
            Purchases = 0;
            SpendCents = 0L;
            TopCents = 0L;
            LastUnix = 0L;
        }
    }
}
