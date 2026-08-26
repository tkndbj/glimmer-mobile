using System.Collections.Generic;
using GlimmerGrove.Store;

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
            _showing = false;
        }
    }
}
