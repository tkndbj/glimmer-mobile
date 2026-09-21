using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The bounded "have I already acted on this payment" set that keeps one purchase to one
    /// thank-you — see <c>StoreService.Announce</c> and <c>ReceiptQueue</c>.
    ///
    /// <para>
    /// <b>It is here and not inside the store fixtures because those need the Editor.</b>
    /// <c>StoreReceiptTests</c> drives the whole redemption path and therefore touches an engine
    /// API the offline runner cannot reach, so the rule's proof would run only when somebody
    /// opened Unity — which for the one rule standing between a player and being congratulated
    /// twice for one charge is not good enough. This is plain arithmetic over strings and runs
    /// on every offline sweep.
    /// </para>
    /// </summary>
    public sealed class TransactionMemoryTests
    {
        [Test]
        public void AKeyIsFreshOnceAndNeverAgain()
        {
            var memory = new TransactionMemory();

            Assert.IsTrue(memory.Fresh("google__order-1"), "the first sighting is the payment");
            Assert.IsFalse(memory.Fresh("google__order-1"), "the second is a repeat of it");
            Assert.IsFalse(memory.Fresh("google__order-1"));
        }

        /// <summary>
        /// The direction that matters more: a guard that swallowed the second of two real
        /// payments is a player charged twice and told once, which is worse than the fault it
        /// was written for.
        /// </summary>
        [Test]
        public void TwoTransactionsAreTwoPayments()
        {
            var memory = new TransactionMemory();

            Assert.IsTrue(memory.Fresh("google__order-1"));
            Assert.IsTrue(memory.Fresh("google__order-2"));
            Assert.IsTrue(memory.Fresh("apple__order-1"),
                          "the store is part of the key, so two stores cannot collide on an id");
        }

        /// <summary>
        /// Empty is the absence of an identity, not one every anonymous event shares. Collapsing
        /// those onto each other would drop a real second event for no reason.
        /// </summary>
        [Test]
        public void AnEmptyKeyIsAlwaysFreshAndIsNeverRemembered()
        {
            var memory = new TransactionMemory();

            Assert.IsTrue(memory.Fresh(string.Empty));
            Assert.IsTrue(memory.Fresh(string.Empty));
            Assert.IsTrue(memory.Fresh(null));

            Assert.AreEqual(0, memory.Count, "an empty key took a seat in a bounded set");
            Assert.IsFalse(memory.Holds(string.Empty));
        }

        [Test]
        public void TheOldestKeyIsTheOneEvicted()
        {
            var memory = new TransactionMemory(2);

            memory.Fresh("a");
            memory.Fresh("b");
            memory.Fresh("c");

            Assert.AreEqual(2, memory.Count);
            Assert.IsFalse(memory.Holds("a"), "the oldest should have gone");
            Assert.IsTrue(memory.Holds("b"));
            Assert.IsTrue(memory.Holds("c"));

            Assert.IsTrue(memory.Fresh("a"),
                          "an evicted key is genuinely forgotten, which is the cost of the bound");
        }

        /// <summary>
        /// A repeat must not re-seat its key, or a duplicate arriving over and over would evict
        /// the very transactions the bound is there to keep.
        /// </summary>
        [Test]
        public void ARepeatDoesNotRefreshItsPlaceInTheQueue()
        {
            var memory = new TransactionMemory(2);

            memory.Fresh("a");
            memory.Fresh("b");
            memory.Fresh("a");
            memory.Fresh("a");
            memory.Fresh("c");

            Assert.AreEqual(2, memory.Count, "repeats were counted as arrivals");
            Assert.IsFalse(memory.Holds("a"));
            Assert.IsTrue(memory.Holds("b"));
            Assert.IsTrue(memory.Holds("c"));
        }

        [Test]
        public void ACapacityBelowOneIsStillAMemory()
        {
            var memory = new TransactionMemory(0);

            Assert.IsTrue(memory.Fresh("a"));
            Assert.IsFalse(memory.Fresh("a"), "the duplicate arriving next was let through");
        }

        [Test]
        public void ClearingForgetsEverything()
        {
            var memory = new TransactionMemory();

            memory.Fresh("a");
            memory.Clear();

            Assert.AreEqual(0, memory.Count);
            Assert.IsTrue(memory.Fresh("a"));
        }
    }
}
