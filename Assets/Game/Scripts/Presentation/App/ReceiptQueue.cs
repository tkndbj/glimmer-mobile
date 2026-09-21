using System.Collections.Generic;
using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Shows what each purchase bought, one receipt at a time and never none.
    ///
    /// <para>
    /// <b>Why a queue rather than a second panel.</b> <see cref="Flow.Modal{T}"/> refuses to
    /// raise a panel that is already up, which is right everywhere else in the game and would
    /// be quietly wrong here: two grants can land seconds apart — a purchase interrupted by a
    /// crash is redeemed on the next launch alongside a fresh one — and the second receipt
    /// would be dropped. A player would be charged twice and told once, which is the one
    /// failure a shop cannot have.
    /// </para>
    /// <para>
    /// <b>Stacking them was never right either</b>, which is what makes this a repair rather
    /// than a workaround for the refusal. Two receipts drawn on top of each other run two
    /// payout cascades into the same balance pills at once (<c>RewardFlight</c> claims a
    /// readout precisely so one writer owns it), play two chimes, and leave a player
    /// dismissing a celebration to find an identical one behind it with different numbers on
    /// it. One after another is what somebody would draw on a whiteboard.
    /// </para>
    /// <para>
    /// <b>One payment, one receipt, and that is enforced here rather than assumed.</b> A queue
    /// whose whole job is to draw two grants one after the other cannot tell a second payment
    /// from a second <em>telling</em> of the first — so it has to be told which payment each
    /// one is, and <see cref="StoreGrant.TransactionKey"/> is that. Without it "one payment,
    /// one thank-you" was an emergent property of three separate layers each being idempotent
    /// (the store re-delivers one transaction id, the pending set is keyed on it, the server
    /// records it against a global receipt key and grants nothing twice) rather than a rule
    /// anything held — and the day any of them was not, the player was congratulated twice for
    /// one charge with every gate green.
    /// </para>
    /// <para>
    /// <b><c>StoreService</c> holds the same line one layer down and this is still not
    /// redundant.</b> That one says a transaction is <em>announced</em> once, which is a promise
    /// to every subscriber; this says it is <em>thanked for</em> once, which is a promise to the
    /// player. The second does not follow from the first, because nothing enforces that the
    /// event has a single subscriber — two of them is one announcement drawn twice, and the
    /// guard at the other end cannot see it. Both use one <c>TransactionMemory</c> rather than
    /// two hand-written sets.
    /// </para>
    /// <para>
    /// <b>Dropping a repeat is always correct, and that is a property of the server rather than
    /// a hope about the store.</b> A transaction is honoured exactly once — the receipt document
    /// is global, keyed on store and transaction id, and is never deleted — so a second grant
    /// carrying one key can never be a second payment.
    /// </para>
    /// <para>
    /// <b>The money is not in here.</b> A receipt is informational: the server has already
    /// granted and <c>CurrencyLedger</c> already holds it, so a queue that lost an entry would
    /// cost a celebration and never a coin. That is why this is allowed to be a plain static
    /// with no persistence — it is the one part of the purchase path where being lossy is
    /// survivable, and it still is not lossy.
    /// </para>
    /// </summary>
    public static class ReceiptQueue
    {
        static readonly Queue<StoreGrant> _waiting = new Queue<StoreGrant>();

        /// <summary>
        /// The transactions already thanked for, so a repeat is dropped rather than queued
        /// behind the receipt it is a copy of. Recorded when the grant is <em>taken</em> rather
        /// than when its panel closes: the duplicate this is written for arrives while the
        /// first receipt is still on screen.
        /// </summary>
        static readonly TransactionMemory _thanked = new TransactionMemory();

        /// <summary>True while a receipt is on screen and owns the queue's turn.</summary>
        static bool _showing;

        /// <summary>
        /// What runs once the last receipt has been dismissed — the account prompt, chained by
        /// <c>Boot</c>.
        ///
        /// <para>
        /// <b>After the last, not after each.</b> A prompt raised between two receipts is
        /// exactly the interleaving this queue exists to prevent, and it would land on the
        /// weakest version of its own argument: "keep what you just bought" works because the
        /// goods are still on screen, and here they would have been replaced by the next
        /// purchase's. <c>AccountPrompts</c> has a budget and a quiet period of its own, so
        /// this is about where the sentence lands rather than about how often it is said.
        /// </para>
        /// <para>
        /// Held once rather than captured per grant, because it is a property of the app
        /// rather than of a purchase, and because a queue carrying a closure per entry would
        /// keep whatever that closure held alive for as long as the queue did.
        /// </para>
        /// </summary>
        public static System.Action WhenSettled;

        /// <summary>Takes a grant. Shows it now if nothing is up, and otherwise in turn.</summary>
        public static void Show(StoreGrant grant)
        {
            if (!grant.IsValid) return;

            // A grant with no transaction on it is shown unconditionally: empty is the absence
            // of an identity rather than one every anonymous grant shares. Nothing in the
            // shipped path produces one — see StoreGrant.TransactionKey.
            if (!_thanked.Fresh(grant.TransactionKey))
            {
                Debug.LogWarning($"[Receipts] {grant.TransactionKey} has already been thanked " +
                                 "for; dropping the duplicate rather than queuing a second " +
                                 "panel behind the first.");
                return;
            }

            _waiting.Enqueue(grant);
            Next();
        }

        /// <summary>
        /// Raises the next receipt, if there is one and the screen is free.
        ///
        /// <para>
        /// The turn is handed back from <c>ShopGrantOverlay.Dismissed</c>, which fires from
        /// <c>OnDestroy</c> — so it is raised however the panel ended, including the endings no
        /// button knows about: the hardware key, and a screen swap tearing every modal down.
        /// That is what stops one abandoned receipt wedging the queue for the session.
        /// </para>
        /// </summary>
        static void Next()
        {
            if (_showing || _waiting.Count == 0) return;

            _showing = true;
            var grant = _waiting.Dequeue();

            Flow.Modal<ShopGrantOverlay>(v =>
            {
                v.Grant = grant;
                v.Dismissed = () =>
                {
                    _showing = false;

                    // The gap is what stops it reading as one panel replacing another: Destroy
                    // lands at the end of the frame, so the outgoing receipt is still drawn
                    // while its replacement springs in from scale zero. The hub learned that
                    // twice. It applies to the next receipt exactly as it does to the prompt.
                    Tween.After(.22f, () =>
                    {
                        // Asked before Next, never after: Next dequeues, so reading the count
                        // afterwards says "nothing waiting" about the receipt it has just put
                        // on screen — and the prompt would land on top of it, which is the one
                        // thing this queue exists to prevent.
                        bool more = _waiting.Count > 0;

                        Next();
                        if (!more) WhenSettled?.Invoke();
                    });
                };
            });
        }

        /// <summary>Empties the queue. For tests, and for a sign-out that abandons a session.</summary>
        public static void Reset()
        {
            _waiting.Clear();
            _thanked.Clear();
            _showing = false;
        }
    }
}
