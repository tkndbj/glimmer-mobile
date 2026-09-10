using System;
using System.Collections.Generic;

namespace GlimmerGrove.Utilities
{
    /// <summary>
    /// How long each utility has left before it may be used again. One of these belongs to one
    /// run, and it is the whole of the rule.
    ///
    /// <para>
    /// <b>Why a cooldown exists at all.</b> A utility is bounded by what a player is holding
    /// (<see cref="UtilityItem.MaxHeld"/>) and by what it costs, and neither of those is a bound
    /// on a <em>moment</em>. A hundred firepots is a hundred taps in four seconds, so the answer
    /// to every wave a player cannot out-match is the same answer, given as fast as a thumb
    /// moves — which is a mode decided by inventory rather than by play. What a cooldown prices
    /// is the one thing a stock cannot: <em>when</em>. It is also what makes the four items
    /// different from each other in a second dimension, so a stormcall at thirty seconds is a
    /// thing held for the wave that needs it rather than the strongest tap on the bar.
    /// </para>
    /// <para>
    /// <b>It can never buy a grade, and that costs no arithmetic.</b> Invariant 39 has to be
    /// proved for anything that changes a board, and it is trivially true here in the direction
    /// that matters: a cooldown only ever <em>refuses</em> a use, so every run playable with it
    /// was playable without it and no charge against the graded count moves. It is the one kind
    /// of change to this feature that needs no exchange rate.
    /// </para>
    /// <para>
    /// <b>Nothing here reaches the save file, and that is a rule rather than an omission.</b>
    /// Seconds remaining is a count that goes both ways, so it is exactly what invariant 11b
    /// refuses a merge: two devices showing 4 and 0 are equally consistent with "one just used
    /// it" and "one has not heard". It is also per <em>run</em> — a cooldown surviving a restart
    /// would punish restarting, and one surviving a level would make what a board asks of a
    /// player depend on the board before it, which is invariant 29c's objection to a companion
    /// that changed what a move does. So it is built with the board and dies with it.
    /// </para>
    /// <para>
    /// <b>It is advanced by the run's own clock, never by wall time.</b> The caller hands it the
    /// same seconds it hands the board, so a cooldown does not burn down behind the shop panel
    /// it was opened to visit, or while a run is waiting to begin. Wall time would make opening
    /// a modal a way of paying a cooldown off, which is a control the player can use and nothing
    /// would ever report.
    /// </para>
    /// </summary>
    public sealed class UtilityCooldown
    {
        /// <summary>
        /// The longest cooldown content may author, in seconds.
        ///
        /// <para>
        /// It is a guard against a unit rather than a balance opinion: five minutes is longer
        /// than any run in this game, so anything past it is an item usable once and almost
        /// certainly a number typed in milliseconds. <c>UtilityCatalog.Resolve</c> refuses it,
        /// which is where a content mistake belongs (a build, never a session).
        /// </para>
        /// </summary>
        public const int MaxSeconds = 300;

        struct Cooling
        {
            public string Id;
            public float Left;
            public float Full;
        }

        // A list rather than a dictionary: a catalog is at most `UtilityCatalog.MaxItems` long,
        // so a linear walk is cheaper than a hash and — the half that matters — it can be walked
        // backwards and compacted in place every frame without allocating.
        readonly List<Cooling> _cooling = new List<Cooling>(UtilityCatalog.MaxItems);

        /// <summary>Whether anything at all is cooling. Lets a caller skip a frame's work.</summary>
        public bool Any => _cooling.Count > 0;

        // ------------------------------------------------------------------ reading
        /// <summary>Seconds left on this utility, or nought when it is ready.</summary>
        public float Left(UtilityItem item) => item == null ? 0f : Left(item.Id);

        public float Left(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;

            for (int i = 0; i < _cooling.Count; i++)
                if (string.Equals(_cooling[i].Id, id, StringComparison.Ordinal))
                    return _cooling[i].Left;

            return 0f;
        }

        /// <summary>
        /// Whether this one may be used now.
        ///
        /// <b>A utility that authors no cooldown is always ready</b>, which is what makes the
        /// field's absence mean "as it was before this existed" — the shape every optional block
        /// in this project takes, because <c>JsonUtility</c> writes a nought into a field an
        /// older file never had.
        /// </summary>
        public bool Ready(UtilityItem item) => Left(item) <= 0f;

        /// <summary>
        /// How much of the cooldown is left, from 1 the instant it starts to 0 when it ends.
        ///
        /// What a sweep is drawn from. Nought for anything ready, so a cell with nothing to say
        /// draws nothing.
        /// </summary>
        public float Fraction(UtilityItem item)
        {
            if (item == null) return 0f;

            for (int i = 0; i < _cooling.Count; i++)
            {
                if (!string.Equals(_cooling[i].Id, item.Id, StringComparison.Ordinal)) continue;

                var entry = _cooling[i];
                if (entry.Full <= 0f) return 0f;

                float share = entry.Left / entry.Full;
                return share < 0f ? 0f : share > 1f ? 1f : share;
            }

            return 0f;
        }

        /// <summary>
        /// Seconds left, rounded <b>up</b>, for a countdown a player reads.
        ///
        /// <para>
        /// Up rather than down, and that is the only honest direction: rounding down shows a
        /// nought for the last whole second of a cooldown, so the number says ready over a cell
        /// that still refuses a tap — a readout disagreeing with the rule it is a readout of.
        /// </para>
        /// </summary>
        public int Seconds(UtilityItem item)
        {
            float left = Left(item);
            return left <= 0f ? 0 : (int)Math.Ceiling(left);
        }

        // ------------------------------------------------------------------ writing
        /// <summary>
        /// Starts this utility's cooldown, because one was just spent.
        ///
        /// <para>
        /// <b>Called for a use that landed and never for one that was refused</b>, which is
        /// <c>UtilityLedger.TryUse</c>'s own rule and for its reason: a firepot that reached
        /// nobody costs the player nothing, so it must not cost them the next ten seconds
        /// either. The two live one line apart in <c>SiegeScreen.Fire</c> so they cannot come
        /// apart.
        /// </para>
        /// <para>
        /// It <em>restarts</em> rather than adds. A cooldown is a bound on when the next one may
        /// be used, not a debt, so a second use — which cannot happen while one is running, but
        /// could the day a caller forgets to ask — resets the clock rather than stacking two.
        /// </para>
        /// </summary>
        public void Spend(UtilityItem item)
        {
            if (item == null || item.CooldownSeconds <= 0) return;

            float full = item.CooldownSeconds;

            for (int i = 0; i < _cooling.Count; i++)
            {
                if (!string.Equals(_cooling[i].Id, item.Id, StringComparison.Ordinal)) continue;

                _cooling[i] = new Cooling { Id = item.Id, Left = full, Full = full };
                return;
            }

            _cooling.Add(new Cooling { Id = item.Id, Left = full, Full = full });
        }

        /// <summary>
        /// Gives every cooldown some seconds of the run, and answers whether one of them ended.
        ///
        /// <para>
        /// The answer is what lets a caller repaint on the frame something became usable again
        /// and not on the other fifty-nine — the edge, rather than a poll, which is
        /// <c>SiegeView.Charge</c>'s rule about a ward's rank.
        /// </para>
        /// </summary>
        public bool Advance(float seconds)
        {
            if (seconds <= 0f || _cooling.Count == 0) return false;

            bool ended = false;

            // Backwards, so removing an entry cannot skip the one after it.
            for (int i = _cooling.Count - 1; i >= 0; i--)
            {
                var entry = _cooling[i];
                entry.Left -= seconds;

                if (entry.Left <= 0f)
                {
                    _cooling.RemoveAt(i);
                    ended = true;
                    continue;
                }

                _cooling[i] = entry;
            }

            return ended;
        }

        /// <summary>
        /// Forgets everything, because this is a fresh run.
        ///
        /// A restart is a new board and a new bar, so what was cooling on the board that was
        /// thrown away is not a debt the next one inherits — the same reason
        /// <c>SiegeScreen.Rewind</c> puts down whatever was armed.
        /// </summary>
        public void Clear() => _cooling.Clear();
    }
}
