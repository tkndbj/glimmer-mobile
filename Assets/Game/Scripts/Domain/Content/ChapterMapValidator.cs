using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Checks the things about a chapter's map that only make sense with every glade
    /// in that chapter in hand.
    ///
    /// <see cref="LevelValidator"/> judges one level at a time, which is what lets it
    /// run against a single board in the authoring tool, and it already refuses a
    /// position outside the 0..1 map. But two glades sitting on top of each other is
    /// not a property of either of them, and a trail running back down the map is a
    /// property of the pair — no amount of per-level checking can see either.
    ///
    /// These are the mistakes somebody makes hand-placing twenty nodes on a Thursday,
    /// and they are invisible everywhere else in the pipeline: the JSON is valid, the
    /// board is solvable, the art resolves, the map draws. It just looks broken.
    ///
    /// Warnings throughout, never errors. A switchback may well be deliberate and a
    /// near-touch may be the look that was wanted; the build should say so once and
    /// let a person decide, not refuse to run.
    /// </summary>
    public static class ChapterMapValidator
    {
        /// <summary>
        /// Validates a chapter's node placement. Levels must be given in play order —
        /// the index's order, not the body's — because half of what this checks is
        /// about consecutive glades, and the player walks the index's order.
        /// </summary>
        public static List<LevelIssue> Validate(ChapterDefinition chapter,
                                                IReadOnlyList<LevelDefinition> inPlayOrder)
        {
            var issues = new List<LevelIssue>();
            if (chapter == null || inPlayOrder == null || inPlayOrder.Count == 0) return issues;

            int strips = chapter.StripCount;

            CheckSpacing(inPlayOrder, strips, issues);
            CheckAscending(inPlayOrder, issues);
            CheckTeaserClearance(inPlayOrder, strips, issues);

            return issues;
        }

        /// <summary>
        /// No two glades may sit close enough for their discs to overlap.
        ///
        /// This is also what catches a chapter that declared too few strips for the
        /// number of glades in it: twenty nodes spread over one strip cannot help but
        /// collide, and they are reported as the collisions they are rather than as a
        /// rule about how many levels a strip may hold.
        /// </summary>
        static void CheckSpacing(IReadOnlyList<LevelDefinition> levels, int strips, List<LevelIssue> issues)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                var a = ChapterMap.Place(levels[i].Presentation.MapPosition);

                for (int k = i + 1; k < levels.Count; k++)
                {
                    var b = ChapterMap.Place(levels[k].Presentation.MapPosition);

                    float gap = ChapterMap.Separation(a, b, strips);
                    if (gap >= ChapterMap.MinimumNodeSeparation) continue;

                    Warn(issues, $"'{levels[i].Id}' and '{levels[k].Id}' are {gap:F0} canvas units apart " +
                                 $"but a glade disc is {ChapterMap.NodeDiameter:F0} across, so they overlap " +
                                 "on the map; move one, or give the chapter another strip");
                }
            }
        }

        /// <summary>
        /// The map is walked upward, so each glade should sit above the one before it.
        /// A pair that does not makes the trail between them double back, which reads
        /// to a player as the path going the wrong way.
        /// </summary>
        static void CheckAscending(IReadOnlyList<LevelDefinition> levels, List<LevelIssue> issues)
        {
            for (int i = 1; i < levels.Count; i++)
            {
                float below = ChapterMap.Place(levels[i - 1].Presentation.MapPosition).y;
                float here = ChapterMap.Place(levels[i].Presentation.MapPosition).y;

                if (here > below) continue;

                Warn(issues, $"'{levels[i].Id}' sits at or below '{levels[i - 1].Id}' " +
                             $"(mapY {here:0.###} after {below:0.###}), so the trail between them " +
                             "runs back down the map");
            }
        }

        /// <summary>
        /// The end-of-chapter marker is placed automatically above the highest glade,
        /// but it stops climbing at the ceiling. A chapter whose last glades are
        /// authored near the top therefore pushes the marker into them — a collision
        /// nobody wrote a coordinate for, which is exactly why it is easy to miss.
        /// </summary>
        static void CheckTeaserClearance(IReadOnlyList<LevelDefinition> levels, int strips,
                                         List<LevelIssue> issues)
        {
            float highest = 0f;
            foreach (var level in levels)
            {
                float y = ChapterMap.Place(level.Presentation.MapPosition).y;
                if (y > highest) highest = y;
            }

            var teaser = ChapterMap.TeaserPosition(highest);

            foreach (var level in levels)
            {
                var p = ChapterMap.Place(level.Presentation.MapPosition);

                float gap = ChapterMap.Separation(p, teaser, strips);
                if (gap >= ChapterMap.MinimumNodeSeparation) continue;

                Warn(issues, $"'{level.Id}' is {gap:F0} canvas units from the end-of-chapter marker at " +
                             $"({teaser.x:0.##}, {teaser.y:0.##}); leave room above the last glade " +
                             "or give the chapter another strip");
            }
        }

        static void Warn(List<LevelIssue> issues, string message)
            => issues.Add(new LevelIssue(LevelIssueSeverity.Warning, message));
    }
}
