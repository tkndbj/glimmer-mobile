using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Whether a chapter's declared mode is the mode its levels actually are.
    ///
    /// <para>
    /// <b>The mistake this exists for is completely silent, and it shipped the first time a mode
    /// grew a second chapter.</b> A chapter's mode lives in <c>manifest.json</c> (invariant 20)
    /// and decides three things: which screen opens its levels, which lane of the switcher it
    /// appears in, and — through <see cref="Progression.LevelUnlock.GateFor"/> — whose stars
    /// unlock it. A weave chapter whose entry forgets to say <c>"mode": "weave"</c> is indexed as
    /// a glade chapter, and nothing anywhere refuses it: every level parses, every board is
    /// proved solvable, every string resolves, every address loads and the build goes green. What
    /// ships is a chapter gated on a stranger's stars, filed under the wrong tab and routed to a
    /// screen that cannot play it.
    /// </para>
    /// <para>
    /// <b>Two answers rather than one, because the two callers need different halves.</b>
    /// <c>Sync Manifest</c> asks <see cref="TryDerive"/> and writes the answer, which is
    /// invariant 4a's rule for the level list applied to the one field of an entry that was still
    /// hand-written — the manifest owns membership and order, and which way of playing a
    /// chapter's levels are is content. <c>ContentValidation</c> asks
    /// <see cref="TryDisagreement"/> and fails the build, because deriving makes the mistake
    /// unlikely and only a check proves it did not happen anyway: a manifest is a text file, and
    /// the lesson this project has already written down twice is that a step somebody has to
    /// remember is a step that gets skipped.
    /// </para>
    /// <para>
    /// In <c>GlimmerGrove.Authoring</c> rather than beside either caller, for
    /// <see cref="ChapterMapValidator"/>'s reason: it is a fact about content, so it wants to be
    /// provable offline against the chapters that actually ship rather than looked at once in the
    /// Editor — and the suite reaches <c>Authoring</c> where it cannot reach <c>Editor</c>. It is
    /// not in Domain, because no shipped type calls it and a player would carry it for nothing.
    /// </para>
    /// </summary>
    public static class ChapterModeValidator
    {
        /// <summary>
        /// The one mode every level of a chapter is, or false when they are not all the same.
        ///
        /// <para>
        /// A chapter holding two modes has no honest answer to derive, and writing whichever came
        /// first would be a guess. It is refused here and named in full by
        /// <see cref="TryDisagreement"/>.
        /// </para>
        /// </summary>
        public static bool TryDerive(IReadOnlyList<LevelDefinition> levels, out GameMode mode)
        {
            mode = GameMode.Default;
            if (levels == null || levels.Count == 0) return false;

            mode = levels[0].Mode;

            for (int i = 1; i < levels.Count; i++)
                if (!levels[i].Mode.Equals(mode)) return false;

            return true;
        }

        /// <summary>
        /// What is wrong with this chapter's mode, if anything is.
        ///
        /// <para>
        /// An error rather than a warning, unlike everything in
        /// <see cref="ChapterMapValidator"/>. A misplaced map node looks wrong; this one is
        /// unplayable in the way that matters, so a build carrying it has to be stopped.
        /// </para>
        /// </summary>
        /// <param name="chapter">How the chapter is named in messages.</param>
        /// <param name="declared">The mode the index says the chapter is.</param>
        /// <param name="levels">Its levels, in any order.</param>
        /// <param name="issue">What to report. Only meaningful when this returns true.</param>
        /// <returns>True when the chapter and its levels disagree.</returns>
        public static bool TryDisagreement(ChapterId chapter, GameMode declared,
                                           IReadOnlyList<LevelDefinition> levels,
                                           out LevelIssue issue)
        {
            issue = default;
            if (levels == null || levels.Count == 0) return false;

            if (!TryDerive(levels, out var derived))
            {
                issue = new LevelIssue(LevelIssueSeverity.Error,
                    $"chapter '{chapter}' holds levels of more than one mode ('{levels[0].Mode}' " +
                    $"and '{Odd(levels)}'). A chapter is one way of playing — its mode decides " +
                    "which screen opens it and whose stars unlock it, so it cannot be two");
                return true;
            }

            if (derived.Equals(declared)) return false;

            issue = new LevelIssue(LevelIssueSeverity.Error,
                $"chapter '{chapter}' is indexed as a '{declared}' chapter and its levels are " +
                $"'{derived}' levels. That decides which screen opens them, which lane of the " +
                "switcher the chapter sits in and whose stars unlock it — run " +
                "Content > Sync Manifest, which derives the field from the body");
            return true;
        }

        /// <summary>The first level that is not the mode the rest of the chapter is.</summary>
        static GameMode Odd(IReadOnlyList<LevelDefinition> levels)
        {
            for (int i = 1; i < levels.Count; i++)
                if (!levels[i].Mode.Equals(levels[0].Mode)) return levels[i].Mode;

            return levels[0].Mode;
        }
    }
}
