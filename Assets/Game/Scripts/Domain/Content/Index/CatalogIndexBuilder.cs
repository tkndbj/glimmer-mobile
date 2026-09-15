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

            // A track this build has never heard of is content from the future, and is dropped
            // in silence for exactly the reason an unknown mode is: filing it on the ordinary
            // ladder would let it gate a real chapter on stars nobody can earn.
            if (!GameTrack.TryParse(entry.track, out var track, out _)) return false;

            if (!_chapterIds.Add(chapterId))
            {
                _problems.Add($"manifest lists chapter '{chapterId}' twice; the later entry is ignored");
                return false;
            }

            var levelIds = ReadLevelIds(entry, chapterId);
            if (levelIds.Count == 0)
                _problems.Add($"chapter '{chapterId}' lists no levels and will show as empty");

            _chapters.Add(new ChapterIndexEntry(chapterId, entry.order, entry.version, levelIds,
                                                mode, track));
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
                                                 entry.unlockLevel, entry.unlockCost,
                                                 entry.groveW, entry.groveH, entry.groveHit));
            return true;
        }

        /// <summary>
        /// Reads one season entry.
        ///
        /// <para>
        /// Stricter than a chapter or a companion, and refused whole rather than salvaged,
        /// because a rung dropped or reordered is a different ladder from the one somebody
        /// authored — and eighty chests is not a thing anybody eyeballs. A half-read season
        /// is not a degraded season, it is a reward table nobody signed off. Skipping it
        /// entirely costs one season and nothing else.
        /// </para>
        /// <para>
        /// <b>Tier ids are not checked against the tier table here, and cannot be.</b> The
        /// ladder lives in <c>manifest.json</c> and the tiers in <c>progression.json</c>,
        /// which version independently (invariant 9b) and are fetched separately — so the
        /// table this build holds at parse time is not necessarily the one it will hold a
        /// minute later. The gates check it (<c>content.py</c> and the Editor validator both
        /// error on a rung naming a tier the tasks block does not define), and at runtime an
        /// unresolvable tier fails closed: the rung simply cannot be claimed.
        /// </para>
        /// </summary>
        public bool AddEvent(ManifestEventDto entry)
        {
            if (entry == null) return false;
            if (entry.disabled) return false;

            if (string.IsNullOrEmpty(entry.id) || !IsCleanId(entry.id))
            {
                _problems.Add($"manifest lists a season with an unusable id '{entry.id}'; ids are " +
                              "lower case letters, digits and underscores, because one names a " +
                              "save row, a loc key and every claim id its chests produce");
                return false;
            }

            if (!_eventIds.Add(entry.id))
            {
                _problems.Add($"manifest lists season '{entry.id}' twice; the later entry is ignored");
                return false;
            }

            if (entry.endUnix <= entry.startUnix)
            {
                _problems.Add($"season '{entry.id}' ends at or before it starts ({entry.startUnix} " +
                              $"to {entry.endUnix}); it is ignored");
                return false;
            }

            long days = (entry.endUnix - entry.startUnix) / Events.EventRules.SecondsPerDay;
            if (days > Events.EventRules.MaxWindowDays)
            {
                _problems.Add($"season '{entry.id}' runs for {days} days, above the supported " +
                              $"{Events.EventRules.MaxWindowDays}; it is ignored. A season that " +
                              "outlives interest in it is content with a countdown attached");
                return false;
            }

            bool sellsPass = entry.passGems > 0;
            if (entry.passGems < 0 || entry.passGems > Events.EventRules.MaxPassGems)
            {
                _problems.Add($"season '{entry.id}' prices its pass at {entry.passGems} gems, " +
                              $"outside 0..{Events.EventRules.MaxPassGems}; nought is a season " +
                              "with a free track only");
                return false;
            }

            var milestones = new List<Events.EventMilestone>();
            int previousGoal = 0;

            foreach (var rung in entry.milestones ?? System.Array.Empty<ManifestEventMilestoneDto>())
            {
                if (rung == null) { _problems.Add($"season '{entry.id}' has an empty rung"); return false; }

                if (rung.goal <= previousGoal)
                {
                    _problems.Add($"season '{entry.id}' rung goals must rise: {rung.goal} " +
                                  $"follows {previousGoal}. An out-of-order ladder is refused rather " +
                                  "than sorted, because sorting it would pay rewards nobody authored");
                    return false;
                }

                if (rung.goal > Events.EventRules.MaxGoal)
                {
                    _problems.Add($"season '{entry.id}' has a rung at {rung.goal} marks, above the " +
                                  $"supported {Events.EventRules.MaxGoal}; it could never be reached");
                    return false;
                }

                // The free column may not have a hole in it: a rung nobody can claim is a row
                // on the page saying nothing, which is worse than a rung paying the humblest
                // chest. The paid column is required exactly when the season sells a pass.
                if (string.IsNullOrEmpty(rung.tier) || !IsCleanId(rung.tier))
                {
                    _problems.Add($"season '{entry.id}' rung at {rung.goal} names free tier " +
                                  $"'{rung.tier}', which is not a usable tier id; every rung pays " +
                                  "something on the free track");
                    return false;
                }

                string premium = rung.premiumTier ?? string.Empty;

                if (premium.Length > 0 && !IsCleanId(premium))
                {
                    _problems.Add($"season '{entry.id}' rung at {rung.goal} names pass tier " +
                                  $"'{premium}', which is not a usable tier id");
                    return false;
                }

                if (sellsPass && premium.Length == 0)
                {
                    _problems.Add($"season '{entry.id}' sells a pass but its rung at {rung.goal} " +
                                  "pays nothing on the pass track; a paid column with a hole in it " +
                                  "is a player looking at what they bought and seeing nothing");
                    return false;
                }

                if (!sellsPass && premium.Length > 0)
                {
                    _problems.Add($"season '{entry.id}' pays pass tier '{premium}' at {rung.goal} " +
                                  "but sells no pass, so nobody could ever claim it");
                    return false;
                }

                milestones.Add(new Events.EventMilestone(rung.goal, rung.tier, premium));
                previousGoal = rung.goal;
            }

            if (milestones.Count == 0)
            {
                _problems.Add($"season '{entry.id}' has no rungs, so it pays nothing and " +
                              "would be a countdown with no reason to watch it");
                return false;
            }

            if (milestones.Count > Events.EventRules.MaxMilestones)
            {
                _problems.Add($"season '{entry.id}' has {milestones.Count} rungs, above the " +
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
                _problems.Add($"season '{entry.id}' asks for icon '{icon}', which is not a usable " +
                              "name; icons are lower case letters, digits and underscores");
                return false;
            }

            _events.Add(new Events.GroveEvent(entry.id, entry.startUnix, entry.endUnix,
                                              milestones, icon, entry.passGems));
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
            // **Laned on the mode *and* the track**, which is the third ordering and the one a
            // second ladder inside one mode needed. Everything that means "what comes next" walks
            // a lane, so an endless chapter cannot gate an ordinary one and an ordinary one
            // cannot chain into it - the same argument the per-mode split already makes about
            // chaining two modes end to end, one level finer.
            var byLane = new Dictionary<ModeLane, List<LevelId>>();
            var chaptersByLane = new Dictionary<ModeLane, List<ChapterIndexEntry>>();

            foreach (var chapter in _chapters)
            {
                var key = chapter.Lane;

                if (!byLane.TryGetValue(key, out var lane))
                {
                    byLane[key] = lane = new List<LevelId>();
                    chaptersByLane[key] = new List<ChapterIndexEntry>();
                }

                chaptersByLane[key].Add(chapter);

                foreach (var levelId in chapter.LevelIds)
                {
                    levelOrder[levelId] = lane.Count;
                    levelMode[levelId] = chapter.Mode;
                    lane.Add(levelId);
                    levelIds.Add(levelId);
                }
            }

            return new CatalogIndex(_chapters.ToArray(), levelIds.ToArray(), levelOrder, _levelChapter,
                                    SortedCompanions(), UsableEvents(), levelMode, byLane,
                                    chaptersByLane);
        }

        /// <summary>
        /// The seasons, in start order.
        ///
        /// <para>
        /// <b>A season no longer names levels, so there is nothing left to check against the
        /// catalog here.</b> That is the whole shape of the v28 change: a track graded on
        /// marks is a track no mode owns, so withdrawing a mode can no longer take a season
        /// down with it — which is exactly how the first one died, and the same bargain
        /// invariant 20a already struck for the star ledger.
        /// </para>
        /// <para>
        /// The sort stays, because the calendar has to be deterministic rather than dependent
        /// on where somebody happened to paste the entry.
        /// </para>
        /// </summary>
        Events.GroveEvent[] UsableEvents()
        {
            var usable = new List<Events.GroveEvent>(_events);

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
