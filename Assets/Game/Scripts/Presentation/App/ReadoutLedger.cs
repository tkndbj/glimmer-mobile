using System;
using System.Collections.Generic;

namespace GlimmerGrove
{
    /// <summary>
    /// What one balance readout owes the player's eye: the rewards that have been banked but not
    /// yet seen landing, each walking its own share of the pill.
    ///
    /// <para>
    /// <b>The pill reads the balance less what is still in the air, and nothing else.</b> A reward
    /// is banked the moment it is earned (a player who kills the app mid-animation has still been
    /// paid), so for a second or two the number on screen is deliberately behind the balance while
    /// tokens walk it forward. This class is the whole of that rule. It owns no Unity type and reads
    /// no clock of its own (the caller hands in the balance and the time), so every case is pinned
    /// offline by <c>RewardFlightTests</c>.
    /// </para>
    /// <para>
    /// <b>Each reward owes the amount it banked, measured, never inferred from balances.</b> The
    /// first ledger kept one snapshot per reward and let each owe the gap to the next snapshot,
    /// which is exact only when every change to the balance belongs to some reward in it: a
    /// purchase banked between two snapshots was walked in by whichever reward came before it, and
    /// a receipt whose snapshot had to be derived (it is raised by its own grant) could sort ahead
    /// of a chest opened a moment earlier and leave the chest owing nothing. An amount cannot be
    /// confused with anyone else's money, and amounts add, so rewards landing in one pill at once
    /// need no order between them at all.
    /// </para>
    /// <para>
    /// <b>The one amount that cannot be measured is money the server has still to pay</b> - a
    /// rewarded video's coins (invariant 10d), granted by the network's callback a few seconds
    /// after the video closed. That entry carries a ceiling (<see cref="Entry.Await"/>) instead,
    /// and counts as arrived whatever the balance has gained above its own base that no newer
    /// entry's banked amount explains, never more than the ceiling. Until the server answers it
    /// owes nothing, so the pill never dips below a figure it has already shown; when it answers,
    /// the coins are walked in rather than jumped to.
    /// </para>
    /// <para>
    /// <b>Every entry ends by itself.</b> A held entry is owed in full only while the panel holding
    /// it stands (<see cref="Entry.Holder"/>); a playing one owes nothing once its last token has
    /// landed or once its deadline has passed, whether or not anything told it so. The callbacks
    /// that close an entry early (the last landing, a lease on a destroyed panel) only make the
    /// pill catch up sooner. Nothing they fail to do can leave it standing below the balance.
    /// </para>
    /// </summary>
    public sealed class ReadoutLedger
    {
        /// <summary>One reward's share of one readout.</summary>
        public sealed class Entry
        {
            /// <summary>
            /// When the reward was opened, against every other. Orders entries for the one rule
            /// that needs an order: attributing money that arrives after a server-paid entry's base.
            /// </summary>
            public readonly long Sequence;

            /// <summary>What the grant measurably put on this balance. Never negative.</summary>
            public long Banked { get; private set; }

            /// <summary>The most the server may still pay into this balance on this reward's behalf.</summary>
            public long Awaited { get; private set; }

            /// <summary>
            /// The balance just after this reward's own grant. Money the server pays later is
            /// measured above it. Raised when a newer entry is dropped (<see cref="Prune"/>), so the
            /// newer entry's money is never mistaken for the server's.
            /// </summary>
            public long Base { get; internal set; }

            /// <summary>
            /// While the entry is held (not yet playing): true for as long as the panel holding it
            /// stands. Null holds until played. Asked on every read, so a panel destroyed without
            /// any callback running (edit mode sends no <c>OnDestroy</c>) still lets the pill go.
            /// </summary>
            public Func<bool> Holder { get; set; }

            public int Tokens { get; private set; }
            public int Landed { get; private set; }
            public bool Playing { get; private set; }

            /// <summary>On the caller's clock: past this, a playing entry owes nothing.</summary>
            public double Deadline { get; private set; }

            /// <summary>Paid in full or abandoned. Owes nothing from now on.</summary>
            public bool Closed { get; private set; }

            public Entry(long sequence, long balance)
            {
                Sequence = sequence;
                Base = balance;
            }

            /// <summary>
            /// The grant has landed: <paramref name="amount"/> is what it measurably added, and
            /// <paramref name="balance"/> what the balance reads now. Adds, so a reward granted in
            /// two steps banks both.
            /// </summary>
            public void Bank(long amount, long balance)
            {
                if (Closed) return;
                if (amount > 0) Banked += amount;
                Base = balance;
            }

            /// <summary>
            /// The server has still to pay up to <paramref name="amount"/> into this balance for
            /// this reward. See the class remarks.
            ///
            /// <para>
            /// A ceiling, so the largest one stated stands rather than adding: the video's flight
            /// states the offer as soon as it is redeemed, and the bonus wheel then states the
            /// prize its slice multiplied that offer to. Either way only what actually arrives is
            /// ever counted.
            /// </para>
            /// </summary>
            public void Await(long amount)
            {
                if (Closed || amount <= Awaited) return;
                Awaited = amount;
            }

            /// <summary>
            /// The tokens are in the air: <paramref name="tokens"/> of them, and whatever has not
            /// landed by <paramref name="deadline"/> counts as landed.
            /// </summary>
            public void Play(int tokens, double deadline)
            {
                if (Closed) return;
                Playing = true;
                Tokens = Math.Max(0, tokens);
                Landed = 0;
                Deadline = deadline;
            }

            /// <summary>How many tokens have landed. Only ever rises.</summary>
            public void Land(int landed)
            {
                if (landed > Landed) Landed = Math.Min(landed, Tokens);
            }

            /// <summary>Finished with this readout, paid or abandoned. Idempotent.</summary>
            public void Close() => Closed = true;

            /// <summary>
            /// What this entry still keeps off the pill, given what it is worth. Integer arithmetic
            /// (no float may decide a figure) and exact at the ends: a held entry owes all of it, the
            /// last landing owes nothing.
            /// </summary>
            internal long Owes(long worth, double now)
            {
                if (Closed || worth <= 0) return 0L;
                if (!Playing) return Held ? worth : 0L;
                if (Tokens <= 0 || Landed >= Tokens || now >= Deadline) return 0L;
                return worth - worth * Landed / Tokens;
            }

            /// <summary>True while nothing about this entry can ever owe again.</summary>
            internal bool Spent(double now)
                => Closed
                || (Playing ? Tokens <= 0 || Landed >= Tokens || now >= Deadline : !Held);

            bool Held => Holder == null || Holder();
        }

        /// <summary>Oldest first, by <see cref="Entry.Sequence"/>.</summary>
        readonly List<Entry> _entries = new List<Entry>();

        /// <summary>Scratch for <see cref="Arrivals"/>; one readout is read many times a frame.</summary>
        long[] _arrived = new long[4];

        public int Count => _entries.Count;

        /// <summary>Enters a reward on this readout. Entering one twice is a no-op.</summary>
        public void Add(Entry entry)
        {
            if (entry == null || _entries.Contains(entry)) return;

            int at = _entries.Count;
            while (at > 0 && _entries[at - 1].Sequence > entry.Sequence) at--;
            _entries.Insert(at, entry);
        }

        /// <summary>What the readout should say: the live balance less everything still owed, never below nought.</summary>
        public long Shown(long live, double now)
        {
            if (_entries.Count == 0) return live;

            Arrivals(live);

            long owed = 0L;
            for (int i = 0; i < _entries.Count; i++)
                owed += _entries[i].Owes(_entries[i].Banked + _arrived[i], now);

            return Math.Max(0L, live - owed);
        }

        /// <summary>True while any entry still owes, or may (a held panel with nothing banked yet).</summary>
        public bool Busy(double now)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (!_entries[i].Spent(now)) return true;
            return false;
        }

        /// <summary>
        /// Drops every entry that can never owe again. What a dropped entry banked moves into the
        /// base of every older server-paid entry, so its money is not attributed to the server
        /// once it is no longer in the list to explain it - and what it was found to have received
        /// from the server moves into the base of every newer one, for the same reason.
        /// </summary>
        public void Prune(long live, double now)
        {
            if (_entries.Count == 0) return;

            Arrivals(live);

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var gone = _entries[i];
                if (!gone.Spent(now)) continue;

                long arrived = _arrived[i];
                for (int j = 0; j < _entries.Count; j++)
                {
                    if (j == i || _entries[j].Awaited <= 0) continue;
                    _entries[j].Base += j < i ? gone.Banked : arrived;
                }

                _entries.RemoveAt(i);
                for (int j = i; j < _entries.Count; j++) _arrived[j] = _arrived[j + 1];
            }
        }

        /// <summary>Forgets everything. For a fixture's teardown.</summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// How much of each server-paid entry has arrived, oldest first: whatever the balance has
        /// gained above the entry's base that no newer entry's banked amount and no older entry's
        /// arrival explains, held between nought and the ceiling. Conservative on purpose: when two
        /// readings are possible it takes the smaller, so the pill shows the money rather than
        /// holding it back to walk in.
        /// </summary>
        void Arrivals(long live)
        {
            int n = _entries.Count;
            if (_arrived.Length < n + 1) _arrived = new long[Math.Max(n + 1, _arrived.Length * 2)];

            long newer = 0L;
            for (int i = n - 1; i >= 0; i--)
            {
                _arrived[i] = newer;               // the banked total of everything newer than i
                newer += _entries[i].Banked;
            }
            _arrived[n] = 0L;

            long claimed = 0L;
            for (int i = 0; i < n; i++)
            {
                var entry = _entries[i];
                long newerBanked = _arrived[i];

                if (entry.Awaited <= 0) { _arrived[i] = 0L; continue; }

                long gained = live - entry.Base - newerBanked - claimed;
                long arrived = Math.Max(0L, Math.Min(gained, entry.Awaited));
                _arrived[i] = arrived;
                claimed += arrived;
            }
        }
    }
}
