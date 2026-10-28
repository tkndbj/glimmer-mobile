using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Which levels the attribution milestones stand on: the <c>attribution</c> block of
    /// progression.json. Optional; absent or faulty, no milestone is ever sent.
    ///
    /// <para>
    /// <b>Keyed on a level id, never a position</b> (invariant 1). <c>l5_reached</c> is a name the ad
    /// networks optimise against, so it is permanent and code; which board it stands on is content,
    /// so inserting a level moves nothing and moving the milestone is a content push. Whether the
    /// level is one the ladder ships is the gates' question (<c>content.py</c>,
    /// <c>AttributionMilestoneTests</c>): this table is read with no catalog.
    /// </para>
    /// <para>
    /// <b>Reaches no server</b>: it pays nothing and decides only what is reported.
    /// </para>
    /// </summary>
    public sealed class AttributionTable
    {
        public static readonly AttributionTable Empty = new AttributionTable(LevelId.None);

        AttributionTable(LevelId l5Reached) => L5Reached = l5Reached;

        /// <summary>The level whose first clear sends <see cref="AttributionMilestones.L5Reached"/>; <c>None</c> for off.</summary>
        public LevelId L5Reached { get; }

        public static AttributionTable Resolve(AttributionDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;

            if (!LevelId.TryParse(dto.l5Reached, out var level, out string error))
            {
                problems.Add($"attribution.l5Reached names level '{dto.l5Reached}': {error}");
                return Empty;
            }

            return new AttributionTable(level);
        }
    }

    /// <summary>
    /// The milestones forwarded to the attribution partner (<c>AppsFlyerSink</c>) for ad networks
    /// to optimise against. Each is raised through <see cref="Telemetry"/>, so Analytics records
    /// it too.
    ///
    /// <para>
    /// <b>Once per account, derived from the save.</b> A milestone is raised on a level's
    /// <em>first clear</em> - a win with stars on a level the save held no clear of - which is
    /// <see cref="RunLedger.Win"/>'s own reading before the record is written. A restart, a replay
    /// or a reinstall that restores the save finds the clear already there and raises nothing; a new
    /// account on the same device has no clear and raises it for that account. No flag is stored,
    /// so nothing here can disagree with the record.
    /// </para>
    /// <para>
    /// Consent is the sinks' business, exactly as for every other event: the attribution sink sends
    /// it under the consent its start applied, and holds it if the SDK has not started yet.
    /// </para>
    /// </summary>
    public static class AttributionMilestones
    {
        /// <summary>The first clear of the ladder's fifth level, named by content. Permanent: an ad network optimises on it.</summary>
        public const string L5Reached = "l5_reached";

        public static AttributionTable Table => ProgressionRules.Table.Attribution;

        /// <summary>
        /// A level was won. <paramref name="wasCleared"/> is the save's reading <em>before</em> this
        /// run was recorded.
        /// </summary>
        public static void NoteWin(LevelId level, bool wasCleared, int stars)
        {
            if (!Reaches(Table, level, wasCleared, stars)) return;
            Telemetry.Track(L5Reached, "level_id", level.Value);
        }

        /// <summary>The rule, pure: the named level, cleared now for the first time.</summary>
        public static bool Reaches(AttributionTable table, LevelId level, bool wasCleared, int stars)
            => table != null && table.L5Reached.IsValid && level.IsValid
            && level == table.L5Reached && !wasCleared && stars > 0;
    }
}
