using System;
using System.Collections.Generic;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// A bounded record of the transactions something has already acted on, so it can act once.
    ///
    /// <para>
    /// <b>Written because the same question is asked twice and must not be answered twice.</b>
    /// "Has this payment already been announced" and "has this payment already been thanked for"
    /// are different rules in different layers — see <c>StoreService.Announce</c> and
    /// <c>ReceiptQueue</c> — but they are one mechanism, and a second hand-written copy of a
    /// bounded set is the shape invariant 5b is written about: both correct until one of them
    /// grows a bound, an eviction or an opinion about an empty key that the other does not.
    /// </para>
    /// <para>
    /// <b>An empty key is always fresh, and that is the one rule worth reading twice.</b> Empty
    /// is not an identity every unidentified thing shares — it is the absence of one — so
    /// collapsing two of them onto each other would drop a real second event for no reason.
    /// </para>
    /// <para>
    /// <b>Bounded, because a set that only ever grows is one more thing to be wrong about in the
    /// feature where being wrong costs money.</b> The oldest key is evicted, which is the right
    /// direction: a duplicate arrives seconds after its original, never a capacity of sales
    /// later. Nothing here is persisted — a fact about a payment in the save is exactly what
    /// invariant 18a keeps out of it, and the launch after a purchase is already silent for its
    /// own reason (the server reports the receipt as already granted).
    /// </para>
    /// <para>
    /// Plain arithmetic over strings, in Domain, so the rules built on it are provable with no
    /// Editor open.
    /// </para>
    /// </summary>
    public sealed class TransactionMemory
    {
        /// <summary>
        /// How many transactions back it remembers, when a caller does not say.
        ///
        /// A payment needs a payment sheet, so this is more purchases than any one session has
        /// ever held.
        /// </summary>
        public const int DefaultCapacity = 64;

        readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The same keys in arrival order, so the oldest is the one evicted.</summary>
        readonly Queue<string> _order = new Queue<string>();

        readonly int _capacity;

        public TransactionMemory(int capacity = DefaultCapacity)
            => _capacity = capacity < 1 ? 1 : capacity;

        /// <summary>How many keys are remembered right now. For the fixtures.</summary>
        public int Count => _keys.Count;

        /// <summary>
        /// True the first time a key is offered and false for every repeat of it, recording it
        /// either way. An empty key is always true and is never recorded — see the remarks.
        /// </summary>
        public bool Fresh(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            if (!_keys.Add(key)) return false;

            _order.Enqueue(key);
            while (_order.Count > _capacity) _keys.Remove(_order.Dequeue());

            return true;
        }

        /// <summary>Whether a key is remembered, without recording it.</summary>
        public bool Holds(string key) => !string.IsNullOrEmpty(key) && _keys.Contains(key);

        /// <summary>Forgets everything. For a <c>Reset</c>, and for the fixtures.</summary>
        public void Clear()
        {
            _keys.Clear();
            _order.Clear();
        }
    }
}
