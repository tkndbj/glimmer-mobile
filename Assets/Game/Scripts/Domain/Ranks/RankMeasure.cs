using System;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Ranks
{
    /// <summary>
    /// What a rank requirement can be <em>about</em>: one monotone reading of an account.
    ///
    /// <para>
    /// <b>A measure is code and a requirement is content</b>, which is <see cref="TaskGoal"/>'s
    /// split one level up and for its reason. Reading a number means knowing where it lives, so
    /// a measure is a build; how much of it a rung asks for is a row in <c>progression.json</c>,
    /// because "reach wave fifteen" and "reach wave forty" are the same reading against a
    /// different target. A whole ladder is therefore retunable without a store review, and only
    /// a genuinely new kind of fact costs a build.
    /// </para>
    /// <para>
    /// <b>Every measure must be monotone, and that is the invariant the whole feature rests
    /// on</b> (invariant 52). A rank is <em>derived</em> from these on every read and stored
    /// nowhere, so if one of them could fall, a badge could be taken away from somebody who did
    /// nothing wrong — a population shifting under a percentile, a retune lowering a keeper
    /// level, a record being replayed worse. Every reading below only ever rises: stars and
    /// clears are bests, the keeper level stands on a high-water floor, an endless best is a
    /// <c>max</c>, and a lifetime tally is a count of things that happened. Adding a measure
    /// means arguing that case first.
    /// </para>
    /// <para>
    /// <b>The lifetime kind is why a new mode costs this file nothing.</b> Anything already in
    /// <see cref="TaskGoals"/> is addressable as a measure by its own id with no entry here, so
    /// a verb added for a task — a new mode's raiders, blocks, rescues — is a rank requirement
    /// the same day, authored rather than written. That is the same registry the task slate
    /// counts against, so there is one list of counted verbs in the game rather than two that
    /// drift.
    /// </para>
    /// </summary>
    public enum RankMeasureKind : byte
    {
        /// <summary>Unreadable: an id this build has never heard of.</summary>
        None = 0,

        /// <summary>Glades cleared, in a chapter or across the catalog.</summary>
        LevelsCleared = 1,

        /// <summary>Stars held, in a chapter or across the catalog.</summary>
        Stars = 2,

        /// <summary>Glades held at three stars, in a chapter or across the catalog.</summary>
        ThreeStars = 3,

        /// <summary>The keeper level.</summary>
        KeeperLevel = 4,

        /// <summary>The furthest wave an Infinite run ever reached.</summary>
        BestWave = 5,

        /// <summary>A <see cref="TaskGoal"/> counted for ever. See <see cref="LifetimeTally"/>.</summary>
        Lifetime = 6,
    }

    /// <summary>What a measure's <c>scope</c> is allowed to name.</summary>
    public enum RankScopeKind : byte
    {
        /// <summary>Nothing. A scope on one of these is an authoring mistake.</summary>
        None = 0,

        /// <summary>A chapter id, or empty for the whole catalog.</summary>
        Chapter = 1,

        /// <summary>A level id, or empty for the best of all of them.</summary>
        Level = 2,
    }

    /// <summary>
    /// One measure: a kind, plus the goal when the kind is <see cref="RankMeasureKind.Lifetime"/>.
    ///
    /// A value type with no allocation, because a screen reads every requirement of every rung
    /// on every repaint and the ladder is read from a table that is rebuilt whenever content is.
    /// </summary>
    public readonly struct RankMeasure : IEquatable<RankMeasure>
    {
        public readonly RankMeasureKind Kind;

        /// <summary>The counted verb, and <see cref="TaskGoal.None"/> for every other kind.</summary>
        public readonly TaskGoal Goal;

        public RankMeasure(RankMeasureKind kind, TaskGoal goal = TaskGoal.None)
        {
            Kind = kind;
            Goal = kind == RankMeasureKind.Lifetime ? goal : TaskGoal.None;
        }

        public static readonly RankMeasure None = new RankMeasure(RankMeasureKind.None);

        public bool IsNone => Kind == RankMeasureKind.None
                           || (Kind == RankMeasureKind.Lifetime && Goal == TaskGoal.None);

        /// <summary>The permanent id content names this by. Empty for <see cref="None"/>.</summary>
        public string Id => RankMeasures.Id(this);

        /// <summary>What a <c>scope</c> on this measure may name.</summary>
        public RankScopeKind Scope => RankMeasures.ScopeOf(Kind);

        public bool Equals(RankMeasure other) => Kind == other.Kind && Goal == other.Goal;

        public override bool Equals(object obj) => obj is RankMeasure m && Equals(m);

        public override int GetHashCode() => ((int)Kind << 8) ^ (int)Goal;

        public override string ToString() => Id;
    }

    /// <summary>
    /// The measure registry: the permanent ids, the parse, and the one place a measure is
    /// actually read off the account.
    ///
    /// <para>
    /// <b>Ids are permanent</b> for invariant 1's reason applied to content rather than to a
    /// save: a shipped <c>progression.json</c> names them, a loc key is derived from them
    /// (<see cref="SentenceKey"/>), and a published ladder that stopped resolving one would
    /// silently lose a requirement — which is a rank handed out for less than it asks for.
    /// </para>
    /// <para>
    /// <b>The derived ids shadow two goals on purpose.</b> <c>stars</c> and <c>three_stars</c>
    /// both exist in <see cref="TaskGoals"/> as counts of what a <em>run</em> scored, and both
    /// are read here as what the <em>ledger holds</em> instead. The held reading is strictly
    /// better for a rank: it is retroactive, so an account that cleared the game before ranks
    /// existed reads correctly on the first launch; it cannot be inflated by replaying an easy
    /// glade; and it is the number every other screen in the game already prints, so a player
    /// comparing the two never sees them disagree. The goal is still counted for the task
    /// slates; it is simply not what a rank asks about.
    /// </para>
    /// </summary>
    public static class RankMeasures
    {
        public const string LevelsCleared = "levels_cleared";
        public const string Stars = "stars";
        public const string ThreeStars = "three_stars";
        public const string KeeperLevel = "keeper_level";
        public const string BestWave = "best_wave";

        /// <summary>
        /// Resolves a content id. <see cref="RankMeasure.None"/> for anything this build has
        /// never heard of, which is a newer content pack reaching an older client — the table
        /// drops the rung rather than the file, exactly as <c>TaskTable</c> drops a task naming
        /// an unknown goal.
        /// </summary>
        public static RankMeasure Parse(string id)
        {
            if (string.IsNullOrEmpty(id)) return RankMeasure.None;

            switch (id)
            {
                case LevelsCleared: return new RankMeasure(RankMeasureKind.LevelsCleared);
                case Stars: return new RankMeasure(RankMeasureKind.Stars);
                case ThreeStars: return new RankMeasure(RankMeasureKind.ThreeStars);
                case KeeperLevel: return new RankMeasure(RankMeasureKind.KeeperLevel);
                case BestWave: return new RankMeasure(RankMeasureKind.BestWave);
            }

            // Everything else is a counted verb, addressable with no entry here. See the
            // class remarks: this is what makes a new mode's goal a rank requirement for free.
            var goal = TaskGoals.Parse(id);
            return goal == TaskGoal.None
                 ? RankMeasure.None
                 : new RankMeasure(RankMeasureKind.Lifetime, goal);
        }

        public static string Id(RankMeasure measure)
        {
            switch (measure.Kind)
            {
                case RankMeasureKind.LevelsCleared: return LevelsCleared;
                case RankMeasureKind.Stars: return Stars;
                case RankMeasureKind.ThreeStars: return ThreeStars;
                case RankMeasureKind.KeeperLevel: return KeeperLevel;
                case RankMeasureKind.BestWave: return BestWave;
                case RankMeasureKind.Lifetime: return TaskGoals.Id(measure.Goal);
                default: return string.Empty;
            }
        }

        /// <summary>
        /// What a scope on this kind may name.
        ///
        /// Written out rather than defaulted, for invariant 44e's reason: a <c>switch</c> whose
        /// <c>default</c> is a real answer hides the case nobody is looking at, and here that
        /// case would be a scope silently ignored — a requirement that reads as "clear ten
        /// glades of Barrowfell" and is met by clearing ten glades anywhere.
        /// </summary>
        public static RankScopeKind ScopeOf(RankMeasureKind kind)
        {
            switch (kind)
            {
                case RankMeasureKind.LevelsCleared: return RankScopeKind.Chapter;
                case RankMeasureKind.Stars: return RankScopeKind.Chapter;
                case RankMeasureKind.ThreeStars: return RankScopeKind.Chapter;
                case RankMeasureKind.BestWave: return RankScopeKind.Level;
                case RankMeasureKind.KeeperLevel: return RankScopeKind.None;
                case RankMeasureKind.Lifetime: return RankScopeKind.None;
                default: return RankScopeKind.None;
            }
        }

        /// <summary>
        /// Every measure a ladder may name, for the gates and the fixtures. The derived kinds
        /// followed by every counted verb that is not shadowed by one.
        /// </summary>
        public static string[] All()
        {
            var goals = TaskGoals.All;
            var list = new System.Collections.Generic.List<string>(goals.Length + 5)
            {
                LevelsCleared, Stars, ThreeStars, KeeperLevel, BestWave,
            };

            foreach (var goal in goals)
            {
                string id = TaskGoals.Id(goal);
                if (!list.Contains(id)) list.Add(id);
            }

            return list.ToArray();
        }

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// What the account currently holds against this measure. Never negative, and never
        /// falls between two calls — see <see cref="RankMeasureKind"/>.
        ///
        /// <para>
        /// <paramref name="index"/> is handed in rather than fetched so this stays a pure
        /// function of (account, catalog) and can be exercised with a catalog a fixture built.
        /// A null index is a game whose content has not loaded yet, and answers nought for the
        /// catalog-shaped readings rather than throwing — the map draws before the splash is
        /// finished on a slow device, and a readout that crashes there is worse than one that
        /// says "not yet".
        /// </para>
        /// </summary>
        public static long Read(RankMeasure measure, string scope, CatalogIndex index)
        {
            switch (measure.Kind)
            {
                case RankMeasureKind.LevelsCleared: return ClearedIn(scope, index);
                case RankMeasureKind.Stars: return StarsIn(scope, index);
                case RankMeasureKind.ThreeStars: return ThreeStarsIn(scope, index);
                case RankMeasureKind.KeeperLevel: return PlayerProgression.Level.Level;
                case RankMeasureKind.BestWave: return BestWaveOn(scope);
                case RankMeasureKind.Lifetime: return LifetimeTally.Count(measure.Goal);
                default: return 0L;
            }
        }

        static long ClearedIn(string scope, CatalogIndex index)
        {
            if (string.IsNullOrEmpty(scope)) return PlayerProgress.ClearedCount;

            var chapter = ChapterOf(scope, index);
            if (chapter == null) return 0L;

            long cleared = 0;
            var ids = chapter.LevelIds;
            for (int i = 0; i < ids.Count; i++)
                if (PlayerProgress.IsCleared(ids[i])) cleared++;

            return cleared;
        }

        static long StarsIn(string scope, CatalogIndex index)
        {
            if (string.IsNullOrEmpty(scope)) return PlayerProgress.TotalStars(index);

            var chapter = ChapterOf(scope, index);
            return chapter == null ? 0L : PlayerProgress.TotalStars(chapter);
        }

        static long ThreeStarsIn(string scope, CatalogIndex index)
        {
            if (string.IsNullOrEmpty(scope))
            {
                if (index == null) return 0L;

                long all = 0;
                foreach (var id in index.LevelIds)
                    if (PlayerProgress.Stars(id) >= 3) all++;
                return all;
            }

            var chapter = ChapterOf(scope, index);
            if (chapter == null) return 0L;

            long full = 0;
            var ids = chapter.LevelIds;
            for (int i = 0; i < ids.Count; i++)
                if (PlayerProgress.Stars(ids[i]) >= 3) full++;

            return full;
        }

        /// <summary>
        /// The best wave on one Infinite level, or across every one of them when no level is
        /// named — which is what makes a second Infinite lane cost this nothing.
        /// </summary>
        static long BestWaveOn(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return EndlessLedger.Best;

            return LevelId.TryParse(scope, out var level, out _) ? EndlessLedger.BestFor(level) : 0L;
        }

        static ChapterIndexEntry ChapterOf(string scope, CatalogIndex index)
        {
            if (index == null) return null;
            return ChapterId.TryParse(scope, out var id, out _) ? index.FindChapter(id) : null;
        }

        // ------------------------------------------------------------------ the sentence
        /// <summary>
        /// The loc key a requirement's line is written from: <c>rank.req.{measure}</c>, or
        /// <c>rank.req.{measure}.in</c> when it names a scope.
        ///
        /// <para>
        /// <b>Derived, never authored</b>, for <c>TaskDefinition.NameKey</c>'s reason — a target
        /// retuned in content changes the sentence without touching a translation, because the
        /// target is the sentence's <c>{0}</c>. The scoped key is a second key rather than a
        /// second argument on the first, because "clear 10 glades" and "clear 10 glades of the
        /// Barrowfell" are different sentences in most languages and not one with a piece
        /// missing.
        /// </para>
        /// <para>
        /// <c>loc.py</c> cannot see either — they are built from an id (invariant 5a) — so
        /// <c>check_ranks</c> in <c>content.py</c> is what proves every shipped requirement
        /// resolves one.
        /// </para>
        /// </summary>
        public static string SentenceKey(RankMeasure measure, bool scoped)
            => "rank.req." + Id(measure) + (scoped ? ".in" : string.Empty);
    }
}
