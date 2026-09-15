using System.Collections.Generic;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// What comes back when a player reports a keeper.
    ///
    /// <para>
    /// <b>Three outcomes, where the server has seven, and the collapse is the security
    /// decision.</b> The server distinguishes "counted", "already hidden", "reached the
    /// threshold", "nothing to report" and "you reported yourself"; a client is told none of
    /// that. A caller who can tell "counted" from "already hidden" can binary-search the
    /// threshold and learn exactly how many accounts a brigade needs, and one who can tell
    /// "counted" from "nothing to report" learns which accounts are worth brigading. Neither
    /// is worth a nicer sentence.
    /// </para>
    /// <para>
    /// The two that survive are the two a player can act on: they have reported this before,
    /// and they have used up a day's reports. Both change what the button should say, and
    /// neither reveals anything about the account being reported.
    /// </para>
    /// <para>
    /// <b>The type keeps its name</b> even though a report is no longer only about a name,
    /// because its ordinals are the ones every analytics event ever recorded carries. Renaming
    /// the members would be free; renumbering them is what the ordinals forbid, and a type
    /// renamed halfway is worse than one whose name is a little old.
    /// </para>
    /// </summary>
    public enum NameReportOutcome
    {
        /// <summary>
        /// The call did not reach the server. Nothing was recorded and it is worth trying
        /// again — the same reading every other best-effort board call gives a failure.
        /// </summary>
        Unavailable = 0,

        /// <summary>Taken. Deliberately says nothing about what happened next.</summary>
        Reported = 1,

        /// <summary>This player had already reported this. Not an error.</summary>
        Duplicate = 2,

        /// <summary>This account has filed its day's reports.</summary>
        Throttled = 3,
    }

    /// <summary>
    /// What this device has already reported, and about whom.
    ///
    /// <para>
    /// <b>Purely so the button can say the right thing, and deliberately not authoritative.</b>
    /// The server is idempotent on the (reporter, subject, target) triple, so a second report
    /// costs one refused write and changes nothing — this exists to avoid asking at all, and to
    /// grey a control the player has already used.
    /// </para>
    /// <para>
    /// <b>Remembered per subject, because the two are two judgements.</b> Somebody who reported
    /// a keeper's name has not reported their grovement, and a single set would tell them they
    /// had — which is the one thing this record can get wrong that a player would notice, and it
    /// would read as the game refusing to take a report it had never been given.
    /// </para>
    /// <para>
    /// <b>It must never enter the save file.</b> "Who this device reported" goes up and down
    /// with a reinstall and is a fact about a device rather than about an account, so it could
    /// never be joined (invariant 11b) — and merged it would arrive on a second device as a
    /// reason to stay quiet, which is backwards. It is held in memory for the session and
    /// nowhere else: the cost of forgetting is one refused write, and the cost of getting
    /// persistence wrong here is a save field that can never be removed.
    /// </para>
    /// </summary>
    public static class KeeperReports
    {
        /// <summary>
        /// How many reported keepers are remembered. Bounded because a set the player can grow
        /// by tapping is a set that grows for the life of the session.
        ///
        /// It counts <em>rows</em> rather than keepers, so a player who reports both halves of
        /// one account spends two — which is the conservative reading: the bound is about memory
        /// and a row is what is held.
        /// </summary>
        public const int MaxRemembered = 256;

        static readonly HashSet<string> Sent = new HashSet<string>();
        static readonly List<string> Order = new List<string>();

        /// <summary>
        /// The row for one judgement about one keeper.
        ///
        /// The subject leads, so a keeper id can never be mistaken for a subject however a
        /// provider spells its uids.
        /// </summary>
        static string Row(ReportSubject subject, string keeperId)
            => ReportSubjects.Wire(subject) + "/" + keeperId;

        /// <summary>Whether this device has already reported that about that keeper this session.</summary>
        public static bool AlreadySent(ReportSubject subject, string keeperId)
            => !string.IsNullOrEmpty(keeperId) && Sent.Contains(Row(subject, keeperId));

        /// <summary>Whether every subject has been reported, which is when a control has nothing left to offer.</summary>
        public static bool AllSent(string keeperId)
        {
            if (string.IsNullOrEmpty(keeperId)) return false;

            foreach (var subject in ReportSubjects.All)
                if (!AlreadySent(subject, keeperId)) return false;

            return true;
        }

        /// <summary>
        /// Remembers a report. Called for <see cref="NameReportOutcome.Reported"/> and for
        /// <see cref="NameReportOutcome.Duplicate"/> alike — the server has told us in both
        /// cases that this pair is on record, and treating only the first as a report would
        /// leave a device that lost a reply asking for ever.
        /// </summary>
        public static void Remember(ReportSubject subject, string keeperId)
        {
            if (string.IsNullOrEmpty(keeperId)) return;

            string row = Row(subject, keeperId);
            if (!Sent.Add(row)) return;

            Order.Add(row);

            while (Order.Count > MaxRemembered)
            {
                Sent.Remove(Order[0]);
                Order.RemoveAt(0);
            }
        }

        /// <summary>
        /// Drops everything. Called when the account changes, because "who I reported" belongs
        /// to the player rather than to the handset — carrying it across a switch would grey a
        /// control for somebody who has never used it.
        /// </summary>
        public static void Forget()
        {
            Sent.Clear();
            Order.Clear();
        }
    }
}
