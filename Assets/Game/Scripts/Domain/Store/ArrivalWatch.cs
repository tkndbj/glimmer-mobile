using System.Collections.Generic;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// What a panel standing over a purchase somebody has just paid for needs to know: whether
    /// it is still waiting, and whether it has waited long enough to owe the player a way out.
    ///
    /// <para>
    /// <b>It is in Domain because "this cannot get stuck" is a claim, and a claim in
    /// Presentation is one no gate can check.</b> The panel this drives is raised over whatever
    /// the player is looking at, including a live siege (invariant 39i, where a modal holds the
    /// run) — so a rule that could leave it up for ever is a game that has stopped, and the one
    /// place it must not be written is inside the thing it would trap. Everything here is plain
    /// arithmetic over a list of strings, so <c>StoreArrivalTests</c> can drive a thousand
    /// seconds of it with no Editor open.
    /// </para>
    /// <para>
    /// <b>It waits on <see cref="StoreService.IsPending"/> rather than on an event, and that is
    /// the whole safety argument.</b> An event pair only reports the endings somebody
    /// remembered to announce; the dictionary is emptied by <em>every</em> ending there is — the
    /// grant, the one refusal that is closed out rather than retried (invariant 18a), and a
    /// <c>Reset</c>. A watcher that asks the authority cannot be left waiting by an outcome
    /// nobody thought to raise, which is a stronger promise than "we handled all four cases",
    /// and it is the promise this particular panel has to make.
    /// </para>
    /// <para>
    /// <b>And the clock is the second half, because the first cannot cover everything.</b> A
    /// receipt the server will not honour is deliberately retried for the life of the install,
    /// so "wait until it is no longer pending" is honestly unbounded: offline, in a tunnel, or
    /// against a product missing from <c>config/products</c>, the transaction stays in that
    /// dictionary and no amount of correctness elsewhere brings the panel down. After
    /// <see cref="Patience"/> the panel stops being a wait and becomes a statement with a
    /// button on it — the shop's own <c>ui.shop.awaiting</c> sentence, which is true and
    /// actionable and has always been the right thing to say about a purchase that is taking a
    /// while.
    /// </para>
    /// </summary>
    public sealed class ArrivalWatch
    {
        /// <summary>
        /// How long a transaction may take to be honoured before anything is drawn about it.
        ///
        /// <para>
        /// <b>A panel that flashes for two frames reads as a stutter</b>, which is
        /// <c>BusyVeil.AppearAfter</c>'s rule arriving on a modal, where it costs more: this one
        /// springs in with a chime and the receipt panel is a beat behind it, so a redemption
        /// that lands on a warm connection would put a panel on screen and take it off again
        /// between the payment sheet closing and the thank-you. Under this, nothing is shown at
        /// all and the receipt goes straight up — which is what the fast path should look like.
        /// </para>
        /// </summary>
        public const float Grace = .35f;

        /// <summary>
        /// How long the panel stays a wait before it becomes a statement the player can leave.
        ///
        /// <para>
        /// Long enough that an ordinary redemption on a poor connection finishes inside it and
        /// the player never sees the second state; short enough that nobody is held in front of
        /// a spinner wondering whether the game has died. The retry policy behind it backs off
        /// to minutes, so there is no version of this where waiting it out is the design.
        /// </para>
        /// </summary>
        public const float Patience = 6f;

        /// <summary>
        /// Transaction keys, not product ids. Two transactions can carry one product — a player
        /// who buys the same gem pack twice in a row — and a set keyed on the product would
        /// settle the panel on the first of them while the second was still owed.
        /// </summary>
        readonly List<string> _keys = new List<string>(2);

        float _waited;

        /// <summary>How many transactions this panel is still waiting on.</summary>
        public int Count => _keys.Count;

        /// <summary>
        /// True once nothing is owed. The panel closes on this and on nothing else, so every way
        /// a transaction can leave the queue is a way this panel can go away.
        /// </summary>
        public bool Settled => _keys.Count == 0;

        /// <summary>
        /// True once the wait has gone on long enough to owe the player a button and a sentence.
        ///
        /// <para>
        /// Latched rather than recomputed, so a panel that has offered a way out never takes it
        /// back — a control that appears and disappears under a thumb is worse than one that was
        /// never offered, and the only thing that could un-latch it is a second purchase
        /// arriving, which is not a reason to trap anybody.
        /// </para>
        /// </summary>
        public bool Relaxed { get; private set; }

        /// <summary>
        /// Starts waiting on one transaction. Answers false for a key that is already watched or
        /// already finished with, so a caller can tell a new arrival from a repeat.
        /// </summary>
        public bool Watch(string transactionKey)
        {
            if (string.IsNullOrEmpty(transactionKey)) return false;

            // Asked of the authority rather than assumed from the event that carried it here:
            // the announcement and the panel are a frame or two apart (see Grace), and a
            // redemption on a warm connection finishes inside that.
            if (!StoreService.IsPending(transactionKey)) return false;

            if (_keys.Contains(transactionKey)) return false;

            _keys.Add(transactionKey);
            return true;
        }

        /// <summary>
        /// One frame. Returns whether anything the panel draws has changed, so a caller repaints
        /// when there is news rather than every frame.
        ///
        /// <para>
        /// <paramref name="unscaledSeconds"/>, and the name is the instruction: a modal takes
        /// <c>Time.timeScale</c> to nought (invariant 30h), so a panel counting scaled time would
        /// wait for ever behind any other modal — which is precisely the state this exists to
        /// make impossible. Zero is a legal step and is how a caller prunes on the spot when it
        /// hears a grant land.
        /// </para>
        /// <para>
        /// <b>A second purchase does not restart the clock.</b> The player has been waiting since
        /// the first one, and a wait that renews itself every time something joins it is a
        /// promise that can be broken indefinitely by the store being busy.
        /// </para>
        /// </summary>
        public bool Tick(float unscaledSeconds)
        {
            bool moved = Prune();

            if (_keys.Count == 0 || Relaxed) return moved;

            if (unscaledSeconds > 0f) _waited += unscaledSeconds;
            if (_waited < Patience) return moved;

            Relaxed = true;
            return true;
        }

        /// <summary>
        /// Stops waiting on everything. For a panel the player has dismissed: the purchase is
        /// still owed and is still being retried on <c>StoreService</c>'s own clock, and what has
        /// ended is this panel's interest in it.
        /// </summary>
        public void Clear() => _keys.Clear();

        bool Prune()
        {
            bool moved = false;

            for (int i = _keys.Count - 1; i >= 0; i--)
            {
                if (StoreService.IsPending(_keys[i])) continue;
                _keys.RemoveAt(i);
                moved = true;
            }

            return moved;
        }
    }
}
