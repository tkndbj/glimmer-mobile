using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// Decides what the player is allowed to open.
    ///
    /// Kept apart from both the catalog and the save file on purpose. The index knows
    /// the order, progress knows what is cleared, and this is the only place that turns
    /// those two facts into a rule — so changing the rule later (star gates between
    /// chapters, a skip-ahead offer, an event track) is a change here and nowhere else.
    ///
    /// It works entirely in <see cref="LevelId"/> against the <see cref="CatalogIndex"/>,
    /// never in loaded definitions. Deciding what is unlocked is something the map does
    /// for a whole chapter at a time and the home screen does at launch, so it must not
    /// be able to pull a chapter body in behind itself.
    /// </summary>
    public static class LevelUnlock
    {
        /// <summary>
        /// Two rules, and which one applies is decided by where the level sits.
        ///
        /// <para>
        /// <b>Inside a chapter</b> a glade opens when the one before it is cleared. That is the
        /// chain, it is what makes a map readable, and it has not changed.
        /// </para>
        /// <para>
        /// <b>At a chapter boundary</b> the chain gives way to a star gate — see
        /// <see cref="GateFor"/>. Clearing every glade of a chapter is not the price of the next
        /// one; earning most of its stars is. The reason is that the two are different
        /// questions and only one of them is about mastery: a player can clear ten glades on
        /// one star each without ever meeting what the chapter was teaching, and a player stuck
        /// on the ninth of ten has no route forward at all except the glade that beat them. A
        /// star gate answers both — it can be met from anywhere in the chapter, so being stuck
        /// on one board is never being stuck on the game, and it cannot be met without having
        /// played most of it well.
        /// </para>
        /// <para>
        /// <b>A chapter may also carry a keeper wall</b>, and it rides the same boundary rather
        /// than being a third rule: <see cref="GateFor"/> answers both halves and
        /// <c>ChapterGate.IsOpen</c> is the conjunction, which is what stops anything here
        /// asking for half of it (invariant 15a). It is a fact about the chapter being entered
        /// rather than the one behind, so it is the only gate a lane's <em>first</em> chapter
        /// can have — see <c>ManifestChapterDto.minKeeperLevel</c>.
        /// </para>
        /// <para>
        /// Note what the boundary rule does <em>not</em> do: it never opens a glade in the
        /// middle of a chapter. Only the first level of a chapter asks the gate, so the chain
        /// inside is exactly as it was and a chapter is still entered at its start.
        /// </para>
        /// </summary>
        public static bool IsUnlocked(CatalogIndex index, LevelId id)
        {
            if (index == null || !index.Contains(id)) return false;

            // Nothing already played is ever taken back, and that clause is load-bearing rather
            // than kind. Unlocking has to be monotonic in what the player has done, because the
            // rule in front of it is not fixed: the gate is content, so a retune can raise it,
            // and this rule *did* change under everybody who was already playing. Without this
            // line, a player who cleared three chapters at one star each opens the map and finds
            // the first level of a chapter they finished padlocked while the nine behind it are
            // open, because the chain inside a chapter and the gate at its head would be
            // answering different questions about the same save. Note that it cannot weaken the
            // gate: a level nobody has played is a level nobody has opened.
            if (HasEverPlayed(id)) return true;

            // **The head is asked before the chain, and the order is load-bearing.** This used
            // to shortcut on "no level before it" first, which was harmless while the only gate
            // was on the chapter behind — a lane's first level has no chapter behind it, so the
            // gate would have answered open anyway. It stopped being harmless the moment a
            // chapter could carry a wall of its own: the Infinite lane is one chapter of one
            // level, so that shortcut returned true before anything looked at the wall, and the
            // gate would have been dead code on the only lane that has one. `IsChapterHead` is
            // already true when there is nothing before it, so the very first level of the game
            // still walks the same path and `GateFor` still answers open for it.
            if (IsChapterHead(index, id)) return GateFor(index, index.ChapterOf(id)).IsOpen;

            return PlayerProgress.IsCleared(index.Previous(id));
        }

        /// <summary>
        /// Whether this player has ever played this level at all — the fact the monotonic
        /// clause above is really about.
        ///
        /// <para>
        /// <b>Two ledgers, because a clear is not the only thing a run leaves behind.</b> Every
        /// laddered level records a <c>LevelRecord</c> when it is beaten; an endless one is
        /// never beaten and leaves a wave count in <see cref="EndlessLedger"/> instead
        /// (invariant 43). Asking only about clears would take the Infinite lane back off
        /// somebody who has run it, the first time a wall was put in front of it — which is the
        /// exact failure the monotonic clause exists to prevent, arriving through the one lane
        /// that does not use the ledger it reads.
        /// </para>
        /// <para>
        /// Both halves only ever rise, so this can never weaken a gate.
        /// </para>
        /// </summary>
        public static bool HasEverPlayed(LevelId id)
            => PlayerProgress.IsCleared(id) || EndlessLedger.BestFor(id) > 0;

        /// <summary>
        /// True when this glade is the one a chapter is entered at, so the gate applies to it
        /// rather than the chain.
        ///
        /// <para>
        /// Asked by comparing the chapter either side of the step rather than by looking up the
        /// chapter's <c>FirstLevel</c>: it costs two dictionary reads, stays right for a chapter
        /// of any size, and cannot be fooled by a manifest whose level list and whose first
        /// level disagree. It is public because the map needs the same question when it decides
        /// which of two refusals to print, and a second copy of this comparison in Presentation
        /// is a second place the rule can drift.
        /// </para>
        /// </summary>
        public static bool IsChapterHead(CatalogIndex index, LevelId id)
        {
            if (index == null || !index.Contains(id)) return false;

            var previous = index.Previous(id);
            return !previous.IsValid || index.ChapterOf(previous) != index.ChapterOf(id);
        }

        /// <summary>
        /// What stands between the player and <paramref name="chapter"/>.
        ///
        /// <para>
        /// The <em>star</em> gate is on the chapter <em>before</em> this one in the same lane.
        /// That is the whole of what keeps the modes independent (invariant 20a): the ladders
        /// never chain, so the first chapter of a mode is always open however little of another
        /// mode has been played, and a mode's own second chapter asks only about its own first.
        /// </para>
        /// <para>
        /// <b>The keeper wall is on <em>this</em> chapter, and it is read before the early
        /// return.</b> A lane's first chapter has nothing behind it and so no star gate at all —
        /// which is every mode's opening chapter and the whole of the Infinite lane — so a wall
        /// computed after that return would be a field the one lane that needs it could never
        /// use. It comes from the manifest and costs nothing to move
        /// (<c>ManifestChapterDto.minKeeperLevel</c>).
        /// </para>
        /// <para>
        /// <b>The keeper level is read off <see cref="PlayerProgression"/> rather than taken as
        /// an argument</b>, which is <c>HomesteadLedger</c>'s shape and for its reason: every
        /// caller is a screen asking about the player in front of it, and a second way of
        /// supplying it would be a second answer. It is memoised behind a dirty flag, so the map
        /// asking this once per node costs one recompute per change.
        /// </para>
        /// <para>
        /// Returns a reading rather than a verdict, because four callers want different halves
        /// of it — this one wants <c>IsOpen</c>, the map wants <c>Held</c> and <c>Required</c>,
        /// the victory panel wants to know whether the gate opened on this run and the
        /// information panel wants the rule itself. Answering with a bool would have each of
        /// them recomputing the rest.
        /// </para>
        /// </summary>
        public static ChapterGate GateFor(CatalogIndex index, ChapterId chapter)
        {
            var entry = index?.FindChapter(chapter);
            if (entry == null) return ChapterGate.Missing;

            // This chapter's own wall, and the keeper level it is measured against. Read first
            // so that every return below carries it - the two early returns are about the star
            // half alone, and a lane's first chapter takes both of them.
            int wall = entry.MinKeeperLevel;
            int keeper = wall > 0 ? PlayerProgression.Level.Level : 0;

            var behind = index.ChapterNeighbour(chapter, -1);
            if (behind == null)                            // the first chapter of its lane
                return wall > 0 ? new ChapterGate(ChapterId.None, 0, 0, 0, wall, keeper)
                                : ChapterGate.Open;

            int required = ChapterGateRules.Table.RequiredStars(behind.LevelCount);
            if (required <= 0)                             // the star gate is switched off
                return wall > 0 ? new ChapterGate(ChapterId.None, 0, 0, 0, wall, keeper)
                                : ChapterGate.Open;

            return new ChapterGate(behind.Id, required,
                                   PlayerProgress.TotalStars(behind),
                                   PlayerProgress.MaxStars(behind),
                                   wall, keeper);
        }

        /// <summary>
        /// The same reading for the chapter <em>after</em> this one — what the map is asking
        /// when it draws the signpost at the end of a chain, and what the victory panel asks
        /// after a run. Open when there is nothing after it, because nothing is being withheld.
        /// </summary>
        public static ChapterGate GateAfter(CatalogIndex index, ChapterId chapter)
        {
            var next = index?.ChapterNeighbour(chapter, +1);
            return next == null ? ChapterGate.Open : GateFor(index, next.Id);
        }

        /// <summary>
        /// Where the player should be sent by default: the first unlocked level they
        /// have not cleared, or the last level once the grove is finished.
        /// </summary>
        public static LevelId NextToPlay(CatalogIndex index) => NextToPlay(index, GameMode.Default);

        /// <summary>
        /// The same question asked of one way of playing.
        ///
        /// <para>
        /// Every mode keeps its own place, which is the whole of what "the modes are
        /// independent" means in code: the ladders never chain, so finishing one is never the
        /// price of opening another, and a player halfway through both is halfway through both
        /// rather than at whichever the flattened order happened to reach first. The rule
        /// itself is unchanged and is still expressed once - <see cref="IsUnlocked"/> asks
        /// <c>CatalogIndex.Previous</c>, which already stays inside a mode.
        /// </para>
        /// <para>
        /// The mode-less overload answers for the ordinary one, which is what the hub's
        /// continue button and the splash want: a new player has never chosen a mode, and
        /// sending them anywhere else would be the game choosing for them.
        /// </para>
        /// </summary>
        public static LevelId NextToPlay(CatalogIndex index, GameMode mode)
        {
            if (index == null) return LevelId.None;

            var lane = index.LevelsIn(mode);
            if (lane.Count == 0) return LevelId.None;

            // The furthest glade the player may actually open, cleared or not. It is what the
            // fall-through returns, and it has to be tracked rather than assumed: since the
            // chapter boundary became a star gate, "nothing uncleared is open" no longer implies
            // "the mode is finished". A player who clears every glade of a chapter on one star
            // each is in exactly that state, and handing them the last level of the catalog —
            // which is what this used to do — would drop the hub's continue button onto a
            // padlocked board. Sending them to the last thing they can play is the honest
            // answer, and it is a glade whose stars are worth going back for.
            var furthest = LevelId.None;

            for (int i = 0; i < lane.Count; i++)
            {
                if (!IsUnlocked(index, lane[i])) continue;

                furthest = lane[i];
                if (!PlayerProgress.IsCleared(lane[i])) return lane[i];
            }

            return furthest.IsValid ? furthest : index.FirstIn(mode);
        }

        /// <summary>The level a "next" button should lead to, or none at the end.</summary>
        public static LevelId After(CatalogIndex index, LevelId id)
            => index?.Next(id) ?? LevelId.None;

        // ------------------------------------------------------------- chapters
        /// <summary>
        /// A chapter opens once its first level does. Expressed in terms of the level
        /// rule rather than duplicating it, so a change to how levels unlock carries through
        /// to chapters automatically — which is exactly what happened when the boundary
        /// became a star gate, and this method did not move.
        /// </summary>
        public static bool IsChapterUnlocked(CatalogIndex index, ChapterId chapter)
        {
            var entry = index?.FindChapter(chapter);
            if (entry == null || entry.IsEmpty) return false;

            return IsUnlocked(index, entry.FirstLevel);
        }

        /// <summary>The chapter the map should open on: wherever the player is up to.</summary>
        public static ChapterIndexEntry CurrentChapter(CatalogIndex index)
            => CurrentChapter(index, GameMode.Default);

        /// <summary>The chapter one mode's map should open on: wherever the player is up to in it.</summary>
        public static ChapterIndexEntry CurrentChapter(CatalogIndex index, GameMode mode)
        {
            if (index == null) return null;

            var next = NextToPlay(index, mode);
            var chapter = next.IsValid ? index.FindChapter(index.ChapterOf(next)) : null;
            return chapter ?? index.FirstChapterIn(mode) ?? index.FirstChapter;
        }

        public static ChapterIndexEntry ChapterBefore(CatalogIndex index, ChapterId chapter)
            => index?.ChapterNeighbour(chapter, -1);

        public static ChapterIndexEntry ChapterAfter(CatalogIndex index, ChapterId chapter)
            => index?.ChapterNeighbour(chapter, +1);
    }
}
