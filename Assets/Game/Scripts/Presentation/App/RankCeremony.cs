using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Ranks;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Which rungs are owed a ceremony, and the one way a run's ending panel is raised.
    ///
    /// <para>
    /// <b>A rank is derived and stored nowhere</b> (invariant 52), so there is no "unclaimed
    /// promotion" anywhere in the save to read — which means the only honest way to know a rung
    /// was <em>just</em> reached is to have known what was held a moment ago. That is the whole
    /// of this file: one baseline ordinal per session, and the difference between it and the
    /// live reading is what is owed.
    /// </para>
    /// <para>
    /// <b>It is a reading rather than a subscription, and that is the substance.</b>
    /// <see cref="RankLedger.Promoted"/> exists and would have been the obvious hook — but it
    /// fires lazily, from inside whichever repaint happens to read <see cref="RankLedger.Held"/>
    /// first, so whether it has fired by the time a panel is raised depends on what else is on
    /// screen. A ceremony that plays on the map and not in a run, or on a phone with a readout
    /// standing and not on one without, is the class of fault nothing here could ever see. A
    /// difference of two integers has no such ordering.
    /// </para>
    /// <para>
    /// <b>Three things force the baseline to be re-taken rather than differenced</b>, and each
    /// of them is a case where the ordinal moved without the player having earned anything.
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>The account changed.</b> A switch is local and both directions are
    /// ordinary (<c>SaveService.SwitchTo</c>), so somebody stepping from a fresh account back to
    /// their own would otherwise be shown every rung they already had. This is the same guard
    /// <see cref="RankLedger"/> now puts on <c>Promoted</c>, for the same reason.</description></item>
    /// <item><description><b>The ladder changed.</b> It is content and retunes without a build
    /// (invariant 52a), so a push that lowers a threshold, inserts a rung or renames one moves
    /// every ordinal under the player. A retune is not a promotion. The cost of this rule is
    /// that a retune which genuinely grants a rank passes in silence, which is the right way
    /// round: a missing ceremony is a shrug, a false one is the game lying about what you
    /// did.</description></item>
    /// <item><description><b>The session is new.</b> Every launch starts by reading where the
    /// account stands, or an account holding Goldbrand would be congratulated on it at every
    /// launch for ever.</description></item>
    /// </list>
    /// <para>
    /// <b>What that last one costs is one case, deliberately.</b> A rung reached in the final
    /// seconds of a session the player then kills is never celebrated, because nothing remembers
    /// it. Remembering it would mean a save field, a <c>hasOnly</c> line, a schema bump and a
    /// merge rule for a flag that pays nothing (invariants 12, 12a, 52e) — a great deal of wire
    /// for a lost flourish. The badge is still theirs on the next launch, because the badge was
    /// never stored either.
    /// </para>
    /// <para>
    /// <b>Every ending goes through <see cref="Before"/>, win or loss.</b> A rank counts runs
    /// played as readily as runs won (<c>RankMeasure</c>), so a rung earned by losing is a rung
    /// earned, and a ceremony that only followed victories would hide the promotion the player
    /// is most likely to have been grinding towards. <c>Tools/verify/compile.py</c> refuses a
    /// file that raises <c>WinOverlay</c> or <c>DefeatOverlay</c> without naming this type, so a
    /// mode added later cannot quietly skip it.
    /// </para>
    /// </summary>
    public static class RankCeremony
    {
        // ------------------------------------------------------------- the baseline
        /// <summary>
        /// The ordinal this session has already accounted for, or -1 for "not yet taken".
        ///
        /// Nought is a real reading — an account below the first rung — so it cannot double as
        /// "unknown", which is <see cref="RankLedger"/>'s own distinction one field over.
        /// </summary>
        static int _seen = -1;

        /// <summary>The account <see cref="_seen"/> describes. See the class remarks.</summary>
        static string _account = string.Empty;

        /// <summary>The ladder <see cref="_seen"/> describes. See <see cref="Fingerprint"/>.</summary>
        static string _ladder = string.Empty;

        /// <summary>Rungs reached and not yet shown, in ladder order.</summary>
        static readonly List<RankDefinition> _owed = new List<RankDefinition>();

        static bool _hooked;

        /// <summary>
        /// Installs the watch. Called once from <c>Boot</c>, and idempotent so a second caller
        /// costs nothing.
        ///
        /// <para>
        /// <b>It deliberately takes no baseline of its own.</b> At the moment <c>Boot</c> runs,
        /// the save has loaded but the content has not — the catalog and
        /// <c>progression.json</c> arrive on the splash — so the ladder is empty and the reading
        /// would be a nought that means "no ladder" rather than "no rank". The first of the
        /// three cues below takes it instead, by which time both halves are in place.
        /// </para>
        /// <para>
        /// The cues are the three things that can move the ordinal without the player having
        /// played: the content publishing or being retuned, the catalog changing under a scoped
        /// measure, and the save being replaced by a load, a merge or an account switch. Each
        /// one either re-takes the baseline or banks what was earned, and
        /// <see cref="Sync"/> decides which.
        /// </para>
        /// </summary>
        public static void Begin()
        {
            if (_hooked) return;
            _hooked = true;

            ProgressionRules.Changed += Sync;
            GameContent.CatalogChanged += Sync;
            PlayerProgress.Reloaded += Sync;

            Sync();
        }

        /// <summary>
        /// Brings the baseline up to date, banking anything earned since it was taken.
        ///
        /// <para>
        /// Cheap and safe to call as often as anything likes: <see cref="RankLedger"/> caches
        /// the walk behind the ordinal, and the only state written here is three fields and a
        /// list that is normally empty.
        /// </para>
        /// </summary>
        public static void Sync()
        {
            try { Walk(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Walk()
        {
            var ladder = RankLedger.Ladder;
            string account = CloudState.UserId ?? string.Empty;
            string print = Fingerprint(ladder);
            int held = ladder.IsEmpty ? 0 : RankLedger.Ordinal;

            bool rebase = _seen < 0
                       || !string.Equals(account, _account, StringComparison.Ordinal)
                       || !string.Equals(print, _ladder, StringComparison.Ordinal);

            if (rebase)
            {
                // Nothing banked under the old account or the old ladder means anything now.
                _owed.Clear();
            }
            else
            {
                for (int ordinal = _seen + 1; ordinal <= held; ordinal++)
                {
                    var rung = ladder.At(ordinal);

                    // A hole in the ladder is not reachable through `RankLadder`, which is built
                    // from a contiguous list — but a null here would be a ceremony with nothing
                    // in it, so the walk stops rather than carrying on past one.
                    if (rung == null) break;

                    _owed.Add(rung);
                }
            }

            _seen = held;
            _account = account;
            _ladder = print;
        }

        /// <summary>
        /// What makes one ladder a different ladder: how many rungs and what they are called.
        ///
        /// <para>
        /// The ids rather than the thresholds, because a threshold moving is exactly the retune
        /// that should <em>not</em> reset anything the player is in the middle of earning — and
        /// an ordinal only shifts under somebody when the <em>list</em> changes. Ids are short,
        /// bounded at <c>RankLadder.MaxRungs</c> and built once per content push, so joining
        /// them is cheaper than any hash worth writing.
        /// </para>
        /// </summary>
        static string Fingerprint(RankLadder ladder)
        {
            if (ladder == null || ladder.IsEmpty) return string.Empty;

            var text = new System.Text.StringBuilder(ladder.Count * 12);

            for (int i = 0; i < ladder.Rungs.Count; i++)
            {
                if (i > 0) text.Append('|');
                text.Append(ladder.Rungs[i].Id);
            }

            return text.ToString();
        }

        // -------------------------------------------------------------- the gateway
        /// <summary>
        /// Shows every ceremony that is owed and then does <paramref name="next"/> — which is
        /// how a run's victory or defeat panel is raised.
        ///
        /// <para>
        /// <b><paramref name="next"/> runs exactly once, whatever happens.</b> It is what tells
        /// the player what the run paid and what the buttons out of it are, so losing it strands
        /// somebody on a finished board: it is fired by the ceremony closing, by the ceremony
        /// being destroyed under a screen change, and by any failure to raise one at all. The
        /// latch is here rather than in the overlay because the chain can be several ceremonies
        /// long and only the last of them may fire it.
        /// </para>
        /// <para>
        /// <b>The whole queue is played, one ceremony each, in ladder order.</b> Crossing two
        /// rungs on one run is close to impossible against the shipped ladder and is not
        /// impossible in general — a fortnight of another device's play landing in one merge
        /// does it — and when it happens, showing the top one and swallowing the other would
        /// mean a badge the player never saw arrive. Each is skippable by a tap, so the honest
        /// answer is also the cheap one.
        /// </para>
        /// </summary>
        public static void Before(Action next)
        {
            if (next == null) return;
            Play(Once(next));
        }

        static void Play(Action next)
        {
            try
            {
                Sync();

                // Nothing owed is the ordinary case, and it costs one integer comparison.
                if (_owed.Count == 0 || Flow.Overlays == null) { next(); return; }

                // A ceremony already standing means this is being driven twice over — the panel
                // behind it will be raised by whichever call owns that one, so the rest of the
                // queue waits for the next ending rather than being raised through it.
                if (Flow.LiveModal<RankUpOverlay>() != null) { next(); return; }

                var rung = _owed[0];
                _owed.RemoveAt(0);

                var ladder = RankLedger.Ladder;
                var from = rung.Ordinal > 1 ? ladder.At(rung.Ordinal - 1) : null;

                var view = Flow.Modal<RankUpOverlay>(v =>
                {
                    v.Rung = rung;
                    v.From = from;
                    v.Then = () => Play(next);
                });

                // Flow.Modal only ever answers null if the stack refused it, and the refusal it
                // can make is the live-modal one already asked above. Belt and braces, because
                // the cost of being wrong is a player on a board with no way forward.
                if (view == null) next();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                next();
            }
        }

        /// <summary>
        /// Wraps an action so it can only ever happen once. See <see cref="Before"/>.
        /// </summary>
        static Action Once(Action action)
        {
            bool spent = false;

            return () =>
            {
                if (spent) return;
                spent = true;

                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            };
        }

        // ------------------------------------------------------------- the quiet half
        /// <summary>
        /// Whether this rung is going to get a ceremony, so whoever else would announce it can
        /// hold its tongue.
        ///
        /// <para>
        /// <b>Asked rather than answered off the queue</b>, and the difference is an ordering
        /// nobody would find twice. <see cref="RankLedger.Promoted"/> is raised from inside the
        /// read that <see cref="Sync"/> itself performs, so a listener asking "is this one
        /// spoken for" during that event would be asking before the queue had been written. The
        /// baseline is already correct at that moment, which is why the question is put to it.
        /// </para>
        /// <para>
        /// The one caller is <see cref="RankBadge"/>, whose toast under the map's back key is
        /// the announcement this replaces. It keeps its pop and its halo — it is a readout and
        /// has to draw the truth the moment the truth moves — and gives up only the sentence,
        /// because a rank announced twice is a rank announced badly.
        /// </para>
        /// </summary>
        public static bool Speaks(RankDefinition rung)
        {
            if (rung == null || _seen < 0) return false;

            var ladder = RankLedger.Ladder;

            return rung.Ordinal > _seen
                && string.Equals(CloudState.UserId ?? string.Empty, _account, StringComparison.Ordinal)
                && string.Equals(Fingerprint(ladder), _ladder, StringComparison.Ordinal);
        }

        /// <summary>How many ceremonies are waiting. For the fixtures and for a screen that asks.</summary>
        public static int Owed => _owed.Count;

        /// <summary>
        /// Forgets the baseline and the queue without announcing anything. For the fixtures,
        /// and for <see cref="RankLedger.Reset"/>'s reason: a static that survives a domain
        /// reload has to have a way back to the state it boots in.
        /// </summary>
        internal static void Reset()
        {
            _seen = -1;
            _account = string.Empty;
            _ladder = string.Empty;
            _owed.Clear();
        }
    }
}
