using System;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Which challenge is today's.
    ///
    /// <para>
    /// <b>Rotation is global and pure</b> (invariant 45b's rule, said of one row): day
    /// <c>k</c> deals row <c>k mod n</c> of the slate and nothing is stored, so every device
    /// agrees and a re-authored slate re-deals every day after it — accepted rather than
    /// refused, because a slate that cannot change is a slate nobody will tune.
    /// </para>
    /// <para>
    /// <b>UTC</b>, as every adjudicated day in this game is (45e), so two phones in two time
    /// zones are on the same challenge.
    /// </para>
    /// </summary>
    public static class ChallengeCalendar
    {
        /// <summary>Days since the Unix epoch, UTC.</summary>
        public static int DayOf(DateTime utcNow)
            => (int)((utcNow.ToUniversalTime() - DateTime.UnixEpoch).TotalDays);

        public static ChallengeDefinition Today(ChallengeTable table, int day)
        {
            if (table == null || table.IsEmpty) return null;

            int n = table.Count;
            int at = ((day % n) + n) % n;
            return table.All[at];
        }
    }
}
