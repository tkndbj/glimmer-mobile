using System.Collections.Generic;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Assembles a <see cref="CatalogIndex"/> from manifest entries.
    ///
    /// Every rejection is recorded rather than thrown, for the same reason the rest of
    /// the content system works that way: a manifest can arrive from a CDN, and one
    /// malformed entry must cost that chapter rather than the launch. The builder
    /// salvages what it can and hands the problems back to be reported — as warnings
    /// at runtime, as build failures in the Editor.
    /// </summary>
    public sealed class CatalogIndexBuilder
    {
        readonly List<ChapterIndexEntry> _chapters = new List<ChapterIndexEntry>();
        readonly HashSet<ChapterId> _chapterIds = new HashSet<ChapterId>();
        readonly Dictionary<LevelId, ChapterId> _levelChapter = new Dictionary<LevelId, ChapterId>();
        readonly List<AvatarDefinition> _companions = new List<AvatarDefinition>();
        readonly HashSet<string> _companionIds = new HashSet<string>(System.StringComparer.Ordinal);
        readonly List<Events.GroveEvent> _events = new List<Events.GroveEvent>();
        readonly HashSet<string> _eventIds = new HashSet<string>(System.StringComparer.Ordinal);
        readonly List<string> _problems = new List<string>();

        public IReadOnlyList<string> Problems => _problems;
        public bool HasProblems => _problems.Count > 0;

        public void Report(string problem) => _problems.Add(problem);

        /// <summary>
        /// Reads one manifest entry. Returns false when the chapter is not for this
        /// client — retired, or needing newer code — which is a decision rather than a
        /// problem and so is reported to nobody.
        /// </summary>
        public bool Add(ManifestChapterDto entry, int appVersion)
        {
            if (entry == null) return false;
            if (entry.disabled) return false;

            if (!ChapterId.TryParse(entry.id, out var chapterId, out string idError))
            {
                _problems.Add($"manifest lists chapter '{entry.id}' which is rejected: {idError}");
                return false;
            }

            // Content that needs newer client code is skipped whole, never half-read.
            if (entry.minAppVersion > 0 && appVersion < entry.minAppVersion) return false;

            if (!GameMode.TryParse(entry.mode, out var mode, out string modeError))
            {
                _problems.Add($"chapter '{chapterId}' names mode '{entry.mode}' which is " +
                              $"rejected: {modeError}");
                return false;
            }

            // A mode this build cannot play is content from the future, not a mistake - so it
            // is skipped in silence exactly as minAppVersion is, and reported to nobody. A
            // chapter opened into a mode the client has no interaction for is a dead screen,
            // which is strictly worse than a chapter that is simply not there yet.
            if (!mode.IsPlayable) return false;

            if (!_chapterIds.Add(chapterId))
            {
                _problems.Add($"manifest lists chapter '{chapterId}' twice; the later entry is ignored");
                return false;
            }

            var levelIds = ReadLevelIds(entry, chapterId);
            if (levelIds.Count == 0)
                _problems.Add($"chapter '{chapterId}' lists no levels and will show as empty");

            _chapters.Add(new ChapterIndexEntry(chapterId, entry.order, entry.version, levelIds, mode));
            return true;
        }

        List<LevelId> ReadLevelIds(ManifestChapterDto entry, ChapterId chapterId)
        {
            var levelIds = new List<LevelId>();
            if (entry.levels == null) return levelIds;

            foreach (var raw in entry.levels)
            {
                if (!LevelId.TryParse(raw, out var levelId, out string error))
                {
                    _problems.Add($"chapter '{chapterId}' lists level '{raw}' which is rejected: {error}");
                    continue;
                }

                // A level id belongs to exactly one chapter, forever. Two chapters
                // claiming one would make a save record ambiguous about what it paid.
                if (_levelChapter.TryGetValue(levelId, out var owner))
                {
                    _problems.Add($"level id '{levelId}' is claimed by both '{owner}' and '{chapterId}'");
                    continue;
                }

                _levelChapter[levelId] = chapterId;
                levelIds.Add(levelId);
            }

            return levelIds;
        }

        /// <summary>
        /// Reads one companion entry. Rejections are recorded and the companion is
        /// dropped, never thrown on — a malformed roster entry must cost that companion
        /// rather than the launch, exactly like a malformed chapter.
        /// </summary>
        public bool AddCompanion(ManifestCompanionDto entry)
        {
            if (entry == null) return false;
            if (entry.disabled) return false;

            if (string.IsNullOrEmpty(entry.id))
            {
                _problems.Add("manifest lists a companion with no id; it is ignored");
                return false;
            }

            if (!IsCleanId(entry.id))
            {
                _problems.Add($"companion id '{entry.id}' is rejected: ids are lower case letters, " +
                              "digits and underscores, because they are written into save files");
                return false;
            }

            if (!_companionIds.Add(entry.id))
            {
                _problems.Add($"manifest lists companion '{entry.id}' twice; the later entry is ignored");
                return false;
            }

            if (entry.unlockLevel < 0)
                _problems.Add($"companion '{entry.id}' has a negative unlock level; treated as 0");

            // Reported rather than clamped silently, because a negative price is the one
            // authoring slip here that could look like a working feature: it reads as "not
            // for sale", so the companion simply loses its buy button and nothing else
            // complains. Zero is a legitimate value and says exactly that on purpose.
            if (entry.unlockCost < 0)
                _problems.Add($"companion '{entry.id}' has a negative unlock cost " +
                              $"({entry.unlockCost}); treated as not for sale");

            _companions.Add(new AvatarDefinition(entry.id, entry.portrait, entry.animated,
                                                 entry.unlockLevel, entry.unlockCost));
            return true;
        }

        /// <summary>
        /// Reads one event entry.
        ///
        /// <para>
        /// Stricter than a chapter or a companion, and refused whole rather than salvaged,
        /// because an event's reward is <em>derived</em>: a milestone that was dropped or
        /// reordered changes what every player who finished the track has earned, and the
        /// earned floor means they keep the higher figure forever. A half-read event is
        /// therefore not a degraded event, it is a permanent economy change nobody
        /// authored. Skipping it entirely costs one event and nothing else.
        /// </para>
        /// <para>
        /// Level ids are <b>not</b> checked against the catalog here, and cannot be: an
        /// event may be added in the same manifest as the chapter it runs over, and this
        /// reads entries in file order. <see cref="Build"/> does it once every chapter is
        /// known.
        /// </para>
        /// </summary>
        public bool AddEvent(ManifestEventDto entry)
        {
            if (entry == null) return false;
            if (entry.disabled) return false;

            if (string.IsNullOrEmpty(entry.id) || !IsCleanId(entry.id))
            {
                _problems.Add($"manifest lists an event with an unusable id '{entry.id}'; ids are " +
                              "lower case letters, digits and underscores, because a player's " +
                              "earned credits depend on them");
                return false;
            }

            if (!_eventIds.Add(entry.id))
            {
                _problems.Add($"manifest lists event '{entry.id}' twice; the later entry is ignored");
                return false;
            }

            if (entry.endUnix <= entry.startUnix)
            {
                _problems.Add($"event '{entry.id}' ends at or before it starts ({entry.startUnix} " +
                              $"to {entry.endUnix}); it is ignored");
                return false;
            }

            long days = (entry.endUnix - entry.startUnix) / Events.EventRules.SecondsPerDay;
            if (days > Events.EventRules.MaxWindowDays)
            {
                _problems.Add($"event '{entry.id}' runs for {days} days, above the supported " +
                              $"{Events.EventRules.MaxWindowDays}; it is ignored. An event that " +
                              "outlives interest in it is content with a countdown attached");
                return false;
            }

            var levels = new List<LevelId>();
            foreach (var raw in entry.levels ?? System.Array.Empty<string>())
            {
                if (!LevelId.TryParse(raw, out var levelId, out string error))
                {
                    _problems.Add($"event '{entry.id}' lists level '{raw}' which is rejected: {error}");
                    return false;
                }
                if (levels.Contains(levelId))
                {
                    _problems.Add($"event '{entry.id}' lists level '{levelId}' twice; one glade " +
                                  "cannot count for two");
                    return false;
                }
                levels.Add(levelId);
            }

            if (levels.Count == 0)
            {
                _problems.Add($"event '{entry.id}' names no glades, so nothing could ever " +
                              "advance it; it is ignored");
                return false;
            }

            var milestones = new List<Events.EventMilestone>();
            int previousGoal = 0;

            foreach (var rung in entry.milestones ?? System.Array.Empty<ManifestEventMilestoneDto>())
            {
                if (rung == null) { _problems.Add($"event '{entry.id}' has an empty milestone"); return false; }

                if (rung.goal <= previousGoal)
                {
                    _problems.Add($"event '{entry.id}' milestone goals must rise: {rung.goal} " +
                                  $"follows {previousGoal}. An out-of-order track is refused rather " +
                                  "than sorted, because sorting it would pay rewards nobody authored");
                    return false;
                }

                if (rung.goal > levels.Count)
                {
                    _problems.Add($"event '{entry.id}' has a milestone at {rung.goal} glades but " +
                                  $"only names {levels.Count}; it could never be reached");
                    return false;
                }

                if (rung.credits < 0 || rung.credits > Events.EventRules.MaxMilestoneCredits)
                {
                    _problems.Add($"event '{entry.id}' milestone at {rung.goal} pays {rung.credits} " +
                                  $"credits, outside 0..{Events.EventRules.MaxMilestoneCredits}");
                    return false;
                }

                milestones.Add(new Events.EventMilestone(rung.goal, rung.credits));
                previousGoal = rung.goal;
            }

            if (milestones.Count == 0)
            {
                _problems.Add($"event '{entry.id}' has no milestones, so it pays nothing and " +
                              "would be a countdown with no reason to watch it");
                return false;
            }

            if (milestones.Count > Events.EventRules.MaxMilestones)
            {
                _problems.Add($"event '{entry.id}' has {milestones.Count} milestones, above the " +
                              $"supported {Events.EventRules.MaxMilestones}");
                return false;
            }

            // A mark is refused only for being unusable as a name. Whether the client has
            // one drawn is not knowable here and must not be checked here: content ships
            // ahead of builds, so a manifest naming a mark an older client lacks has to
            // stay valid — that client draws the default, which is a working screen.
            string icon = entry.icon ?? string.Empty;
            if (icon.Length > 0 && !IsCleanId(icon))
            {
                _problems.Add($"event '{entry.id}' asks for icon '{icon}', which is not a usable " +
                              "name; icons are lower case letters, digits and underscores");
                return false;
            }

            _events.Add(new Events.GroveEvent(entry.id, entry.startUnix, entry.endUnix,
                                              levels, milestones, icon));
            return true;
        }

        /// <summary>
        /// Save-file safe: an id becomes a loc key and an analytics dimension, and both
        /// break in ways nobody notices for weeks if it can contain a space or a dot.
        /// </summary>
        static bool IsCleanId(string id)
        {
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Companions in unlock order, ties broken by their place in the manifest.
        ///
        /// Sorted by insertion rather than <c>List.Sort</c> because that one is not
        /// stable: two companions unlocking at the same level would swap places between
        /// runs, and the picker would reshuffle under a player for no reason. The roster
        /// is tens of entries, so the cost is irrelevant and the determinism is not.
        /// </summary>
        AvatarDefinition[] SortedCompanions()
        {
            var sorted = new List<AvatarDefinition>(_companions.Count);

            foreach (var companion in _companions)
            {
                int at = sorted.Count;
                while (at > 0 && sorted[at - 1].UnlockLevel > companion.UnlockLevel) at--;
                sorted.Insert(at, companion);
            }

            return sorted.ToArray();
        }

        public CatalogIndex Build()
        {
            // Sparse orders let a chapter slot between two shipped ones. Ties break on
            // id so the result is deterministic rather than dependent on file order —
            // a tie is still an authoring mistake, and validation says so.
            _chapters.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : a.Id.CompareTo(b.Id);
            });

            // Two orderings, and the split is the point. The flat list is every glade in the
            // game and is what totals are taken over - stars, XP, credits - because a glade is
            // a glade whichever way it is played and the reward path has no opinion about
            // modes. The per-mode list is what *order* means to a player: the next glade, the
            // previous one, what unlocks what. Sharing one list would chain the second mode
            // onto the end of the first, so finishing the classic game would be the price of
            // opening the second one - which is precisely what a second mode must not cost.
            var levelIds = new List<LevelId>();
            var levelOrder = new Dictionary<LevelId, int>();
            var levelMode = new Dictionary<LevelId, GameMode>();
            var byMode = new Dictionary<GameMode, List<LevelId>>();
            var chaptersByMode = new Dictionary<GameMode, List<ChapterIndexEntry>>();

            foreach (var chapter in _chapters)
            {
                if (!byMode.TryGetValue(chapter.Mode, out var lane))
                {
                    byMode[chapter.Mode] = lane = new List<LevelId>();
                    chaptersByMode[chapter.Mode] = new List<ChapterIndexEntry>();
                }

                chaptersByMode[chapter.Mode].Add(chapter);

                foreach (var levelId in chapter.LevelIds)
                {
                    levelOrder[levelId] = lane.Count;
                    levelMode[levelId] = chapter.Mode;
                    lane.Add(levelId);
                    levelIds.Add(levelId);
                }
            }

            return new CatalogIndex(_chapters.ToArray(), levelIds.ToArray(), levelOrder, _levelChapter,
                                    SortedCompanions(), UsableEvents(), levelMode, byMode, chaptersByMode);
        }

        /// <summary>
        /// Events whose glades all exist, in start order.
        ///
        /// <para>
        /// The catalog check happens here rather than in <see cref="AddEvent"/> because
        /// entries are read in file order and an event may legitimately be listed before
        /// the chapter it runs over. By the time this runs every chapter is known.
        /// </para>
        /// <para>
        /// An event naming a glade the catalog does not have is dropped whole. The
        /// alternative — running it over the glades that do exist — silently lowers every
        /// goal on the track relative to what was authored, and a player who then finishes
        /// it keeps the credits forever because the earned floor never falls.
        /// </para>
        /// </summary>
        Events.GroveEvent[] UsableEvents()
        {
            var usable = new List<Events.GroveEvent>(_events.Count);

            foreach (var groveEvent in _events)
            {
                bool complete = true;

                foreach (var levelId in groveEvent.Levels)
                {
                    if (_levelChapter.ContainsKey(levelId)) continue;

                    _problems.Add($"event '{groveEvent.Id}' names glade '{levelId}', which no " +
                                  "chapter in this manifest holds; the event is ignored");
                    complete = false;
                    break;
                }

                if (complete) usable.Add(groveEvent);
            }

            // Start order, ties broken on id, so the calendar is deterministic rather than
            // dependent on where somebody happened to paste the entry.
            usable.Sort((a, b) =>
            {
                int byStart = a.StartUnix.CompareTo(b.StartUnix);
                return byStart != 0 ? byStart : string.CompareOrdinal(a.Id, b.Id);
            });

            return usable.ToArray();
        }
    }
}
