using System;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove.Ranks
{
    /// <summary>
    /// The rank this account holds, as the game reads it.
    ///
    /// <para>
    /// <b>Derived on every read and stored nowhere</b> (invariant 14): the ladder is content and
    /// every line of it is a reading of records the save already keeps, so there is no counter
    /// to merge, no claim to adjudicate, no migration to write and nothing for a second device
    /// to disagree about. Two phones signed into one account answer the same rank without
    /// syncing anything that did not already sync.
    /// </para>
    /// <para>
    /// <b>Cached and invalidated like <see cref="PlayerProgression"/>, and for the same
    /// reason</b> — a map readout and a page of rows both ask several times a repaint, and the
    /// walk behind the answer touches every level record. The cache is dropped by the six things
    /// that can move it: a run being recorded, the save being reloaded or merged, the catalog
    /// being republished, the ladder being retuned, an Infinite best landing, and a counted verb
    /// happening. That list <em>is</em> the measure registry read backwards, which is why adding
    /// a measure means adding its cue here — the one thing a new measure costs beyond its
    /// reading.
    /// </para>
    /// <para>
    /// <b><see cref="Promoted"/> fires only on the way up, and cannot fire on the way down,
    /// because there is no way down</b> (invariant 52). It carries the rung reached so a screen
    /// can celebrate it; nothing depends on it, and nothing is paid by it — see
    /// <see cref="RankDefinition"/> for why a rank pays nothing.
    /// </para>
    /// </summary>
    public static class RankLedger
    {
        static RankDefinition _held;
        static int _heldOrdinal = -1;      // -1 is "never computed", which 0 is not
        static bool _dirty = true;
        static bool _hooked;

        /// <summary>Raised when the held rank may have moved. Screens repaint on this.</summary>
        public static event Action Changed;

        /// <summary>Raised when a new rung is reached, carrying it. Only ever upward.</summary>
        public static event Action<RankDefinition> Promoted;

        static RankLedger() => Hook();

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;

            // What a rung can be about, one cue each. A measure added without its cue would be
            // a badge that is correct whenever a screen happens to be rebuilt and stale the rest
            // of the time — which compiles, draws and passes every fixture, because nothing
            // moves during a test (invariant 44j's lesson, about a rank rather than a balance).
            Persistence.PlayerProgress.RecordChanged += _ => Invalidate();
            Persistence.PlayerProgress.Reloaded += Invalidate;
            GameContent.CatalogChanged += Invalidate;
            ProgressionRules.Changed += Invalidate;
            EndlessLedger.Changed += Invalidate;
            Tasks.LifetimeTally.Changed += Invalidate;

            // The keeper level moves on XP alone — an Infinite run, a boost banking, a season
            // chest — with no record changing, so it needs its own cue rather than riding on
            // the record one.
            PlayerProgression.Changed += Invalidate;
        }

        /// <summary>The live ladder. Content, so it is read through the table rather than held.</summary>
        public static RankLadder Ladder => ProgressionRules.Table.Ranks;

        /// <summary>Whether this build has a ladder at all. Both readouts hide when it does not.</summary>
        public static bool IsDrawn => !Ladder.IsEmpty;

        /// <summary>
        /// The highest rung held, or null for an account that has not reached the first.
        ///
        /// Null is an ordinary state rather than a fault — every account is there for its first
        /// few glades — and every screen draws it as the first rung, unheld.
        /// </summary>
        public static RankDefinition Held
        {
            get { EnsureFresh(); return _held; }
        }

        /// <summary>One-based, and nought below the first rung.</summary>
        public static int Ordinal
        {
            get { EnsureFresh(); return _heldOrdinal; }
        }

        /// <summary>The rung being climbed, or null at the top of the ladder.</summary>
        public static RankDefinition Next => Ladder.Next(GameContent.Index);

        /// <summary>How many rungs there are, for a screen saying "3 of 7".</summary>
        public static int Count => Ladder.Count;

        /// <summary>
        /// How far through the rung being climbed the player is, as a fraction — the mean of its
        /// lines, each clamped to its own target.
        ///
        /// <para>
        /// <b>The mean of the lines rather than a sum of the numbers</b>, because the numbers are
        /// not in the same units: a rung asking for ten glades and two hundred and fifty battles
        /// would be a bar that barely moves for the glades and then jumps, which reads as a bar
        /// that is broken. Each line contributes the same share, which is also how the page
        /// draws them — one row each, same width.
        /// </para>
        /// </summary>
        public static float Progress01
        {
            get
            {
                var next = Next;
                if (next == null || next.Requirements.Count == 0) return 1f;

                var index = GameContent.Index;
                float total = 0f;

                for (int i = 0; i < next.Requirements.Count; i++)
                {
                    var line = next.Requirements[i];
                    float share = line.Target <= 0 ? 1f : line.Held(index) / (float)line.Target;
                    total += share > 1f ? 1f : share < 0f ? 0f : share;
                }

                return total / next.Requirements.Count;
            }
        }

        /// <summary>Forces the next read to recompute. Cheap; safe to call often.</summary>
        public static void Invalidate()
        {
            _dirty = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void EnsureFresh()
        {
            if (!_dirty) return;

            int before = _heldOrdinal;

            _held = Ladder.Held(GameContent.Index);
            _heldOrdinal = _held == null ? 0 : _held.Ordinal;
            _dirty = false;

            // `before < 0` is the first read of the session, which is not a promotion however
            // high the account stands — every launch would otherwise announce the rank the
            // player already had.
            if (before >= 0 && _heldOrdinal > before && _held != null)
            {
                Analytics.Telemetry.Track("rank_reached", "rank", _held.Id, "ordinal", _held.Ordinal);

                try { Promoted?.Invoke(_held); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Forgets the cache without announcing anything. For the fixtures and the wipe.</summary>
        internal static void Reset()
        {
            _held = null;
            _heldOrdinal = -1;
            _dirty = true;
        }
    }
}
