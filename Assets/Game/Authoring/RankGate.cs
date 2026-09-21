using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Ranks
{
    /// <summary>
    /// Whether the rank ladder opens when the lane it ranks does (invariant 52i).
    ///
    /// <para>
    /// <b>The mistake this exists for shipped.</b> A rank is this game's competitive readout — it
    /// is on a board row and on a stranger's public profile (invariant 52h) — and the competitive
    /// mode is the Infinite lane, which stands behind a keeper wall. Cinderling asked for eight
    /// Thornwatch clears and fifteen runs and nothing else, so it was worn at keeper level 7
    /// against a lane that opens at 10: a badge for a mode the keeper could not open, with every
    /// gate in this repo green.
    /// </para>
    /// <para>
    /// <b>The gate itself is one ordinary <c>keeper_level</c> line on the <em>first</em> rung, and
    /// that is deliberately the whole mechanism.</b> Both readings of the ladder walk up from the
    /// bottom and stop at the first rung they cannot meet (<see cref="RankLadder.Held"/>, and
    /// <c>rungOf</c> in <c>functions/src/ranks.ts</c>), so one line closes every rung, every badge,
    /// every board row and every public profile — with no new code on either side of the wire, and
    /// therefore with nothing that could disagree. It is also <em>drawn</em>: the ranks page lists
    /// it beside the rung's other lines, so a keeper below the wall is told why.
    /// </para>
    /// <para>
    /// <b>What is left is that the figure exists twice</b> — the wall in <c>manifest.json</c>, the
    /// gate in <c>progression.json</c> — and nothing can make them share one number, because the
    /// server never reads a manifest. So they are held together by being checked wherever both
    /// files are legible at once: here (the build gate and the suite), <c>check_ranks</c> in
    /// <c>Tools/verify/content.py</c>, and <c>readRanks</c> in <c>seed-config.mjs</c>, which is the
    /// one a re-seed from a shadow tree cannot skip. Three copies of one predicate is the stance
    /// <c>ValidateStore</c> and the seeder already take about the shop, and for its reason: they
    /// run on different sides of a wire, and the disagreement is exactly the thing worth catching.
    /// </para>
    /// <para>
    /// <b>Two answers, not one</b> — an error below the wall and a warning above it. Below is a
    /// badge handed out for a mode nobody can open, which is the fault itself and can only be a
    /// mistake. Above is a ladder that no longer opens <em>with</em> its lane: legal, possibly
    /// wanted, and silent, so it is said out loud and never refused. That is
    /// <c>ValidateKeeperWalls</c>' split, one level up.
    /// </para>
    /// <para>
    /// In <c>GlimmerGrove.Authoring</c> rather than beside the build gate, for
    /// <see cref="ChapterModeValidator"/>'s reason and one sharper: the first cut of this rule
    /// lived in <c>ContentValidation</c>, where the suite cannot reach it, and so shipped having
    /// never once executed. A validator with no failing case is not a check.
    /// </para>
    /// </summary>
    public static class RankGate
    {
        /// <summary>
        /// The keeper level at which this catalog's first ranked lane opens, or nought when it
        /// ships none.
        ///
        /// <para>
        /// <b>The lowest wall of every Infinite chapter, rather than a named one.</b> The rule is
        /// "the ladder may not open before ranked play can be reached", so what matters is the
        /// first lane that opens — and a wall read off the lane itself cannot be pointed at the
        /// wrong chapter, which an authored <c>opensWith</c> could. A chapter the manifest has
        /// disabled is not in the index at all, so it cannot anchor anything.
        /// </para>
        /// </summary>
        public static int WallOf(CatalogIndex index)
        {
            if (index == null) return 0;

            int wall = 0;

            foreach (var chapter in index.Chapters)
            {
                if (!chapter.Track.Equals(GameTrack.Infinite)) continue;
                if (chapter.MinKeeperLevel <= 0) continue;
                if (wall == 0 || chapter.MinKeeperLevel < wall) wall = chapter.MinKeeperLevel;
            }

            return wall;
        }

        /// <summary>
        /// The keeper level the ladder opens at: what its <em>first</em> rung asks for, and
        /// nought when it asks for none.
        ///
        /// <para>
        /// Only the first rung, because only the first rung can gate anything — the walk stops at
        /// the first unmet rung, so a keeper line further up closes the rungs above it and none
        /// below. A line carrying a scope is ignored rather than read: <c>keeper_level</c> takes
        /// none, both content gates refuse one, and honouring it here would be this file agreeing
        /// with a shape the rest of the project rejects.
        /// </para>
        /// </summary>
        public static int OpensAt(RankLadder ladder)
        {
            if (ladder == null || ladder.IsEmpty) return 0;

            int opens = 0;

            foreach (var line in ladder.Rungs[0].Requirements)
            {
                if (line.Measure.Kind != RankMeasureKind.KeeperLevel) continue;
                if (line.IsScoped) continue;
                if (line.Target > opens) opens = line.Target;
            }

            return opens;
        }

        /// <summary>
        /// Holds the two figures together. Adds nothing when this catalog ships no ranked lane,
        /// or when there is no ladder to gate.
        /// </summary>
        public static void Check(RankLadder ladder, CatalogIndex index,
                                 ICollection<string> errors, ICollection<string> warnings)
        {
            if (ladder == null || ladder.IsEmpty) return;

            int wall = WallOf(index);
            if (wall <= 0) return;

            int opens = OpensAt(ladder);
            string first = ladder.Rungs[0].Id;

            if (opens < wall)
            {
                errors?.Add($"the Infinite lane opens at keeper level {wall} and the rank " +
                            $"ladder's first rung ('{first}') opens at " +
                            (opens > 0 ? opens.ToString() : "nothing") + "; a rank is what a " +
                            "board row and a stranger's profile draw, so it may not be worn by " +
                            "somebody who cannot yet open the lane it ranks - give that rung a " +
                            $"'keeper_level' line of at least {wall}, and move it with the wall");
                return;
            }

            if (opens > wall)
                warnings?.Add($"the rank ladder's first rung ('{first}') opens at keeper level " +
                              $"{opens} and the Infinite lane opens at {wall}, so the ladder no " +
                              "longer opens with the lane it ranks; legal, and worth being sure " +
                              "it was meant");
        }
    }
}
