using System;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Ranks
{
    /// <summary>
    /// Where a rank's numbers are read from: six monotone readings of one account.
    ///
    /// <para>
    /// <b>This exists because a rank went public.</b> While a badge was only ever drawn on the
    /// player's own map it could be read straight off the live ledgers, which is what
    /// <see cref="RankMeasures"/> did. A rank on a board row is a different thing: it has to be
    /// derived from the <em>save file</em> the server holds, on both sides of the wire — by the
    /// client because the publish fingerprint is built from the pushed file and never from the
    /// device (<c>GroveCard.OfSave</c>), and by the server because a number that goes public
    /// stops being derived-and-trusted and becomes adjudicated (invariant 19a).
    /// </para>
    /// <para>
    /// <b>One rule, two sources.</b> Everything above this — which lines a rung asks for, what
    /// "met" means, and the walk up from the bottom that decides which rung is held — is written
    /// exactly once and takes a source. Only the six readings below are implemented twice, and
    /// the pair is held together by <see cref="LedgerRankSource"/> and
    /// <see cref="SaveRankSource"/> answering identically for a loaded save, which
    /// <c>RankLadderTests</c> asserts. Without this the ladder itself would have been copied,
    /// and a ladder that disagrees with itself is a badge handed out for less than it asks for —
    /// which nobody would ever notice.
    /// </para>
    /// <para>
    /// <b>Every reading is monotone, and that is the invariant the feature rests on</b>
    /// (invariant 52). See <see cref="RankMeasureKind"/> for the argument per kind; a source
    /// that could answer a smaller number tomorrow would take a badge off somebody who did
    /// nothing wrong.
    /// </para>
    /// </summary>
    public interface IRankSource
    {
        /// <summary>Glades cleared in a chapter, or across the catalog when the scope is empty.</summary>
        long Cleared(string chapterScope);

        /// <summary>Stars held in a chapter, or across the catalog when the scope is empty.</summary>
        long Stars(string chapterScope);

        /// <summary>Glades held at three stars, in a chapter or across the catalog.</summary>
        long ThreeStars(string chapterScope);

        /// <summary>The keeper level.</summary>
        long KeeperLevel { get; }

        /// <summary>The furthest wave one Infinite level reached, or the best of all of them.</summary>
        long BestWave(string levelScope);

        /// <summary>A counted verb, for ever. See <see cref="LifetimeTally"/>.</summary>
        long Lifetime(TaskGoal goal);
    }

    /// <summary>
    /// The live reading: the ledgers as they stand on this device right now.
    ///
    /// <para>
    /// What the map's badge and the ranks page are drawn from, because those two have to answer
    /// the instant a run lands and must never wait on a sync. It is a class rather than a struct
    /// so a null index — a game whose content has not finished loading — is expressible and
    /// answers nought rather than throwing: the map draws before the splash is finished on a
    /// slow device, and a readout that crashes there is worse than one that says "not yet".
    /// </para>
    /// </summary>
    public sealed class LedgerRankSource : IRankSource
    {
        readonly CatalogIndex _index;

        public LedgerRankSource(CatalogIndex index) => _index = index;

        public long Cleared(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return PlayerProgress.ClearedCount;

            var chapter = ChapterOf(scope);
            if (chapter == null) return 0L;

            long cleared = 0;
            var ids = chapter.LevelIds;
            for (int i = 0; i < ids.Count; i++)
                if (PlayerProgress.IsCleared(ids[i])) cleared++;

            return cleared;
        }

        public long Stars(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return PlayerProgress.TotalStars(_index);

            var chapter = ChapterOf(scope);
            return chapter == null ? 0L : PlayerProgress.TotalStars(chapter);
        }

        public long ThreeStars(string scope)
        {
            if (string.IsNullOrEmpty(scope))
            {
                if (_index == null) return 0L;

                long all = 0;
                foreach (var id in _index.LevelIds)
                    if (PlayerProgress.Stars(id) >= 3) all++;
                return all;
            }

            var chapter = ChapterOf(scope);
            if (chapter == null) return 0L;

            long full = 0;
            var ids = chapter.LevelIds;
            for (int i = 0; i < ids.Count; i++)
                if (PlayerProgress.Stars(ids[i]) >= 3) full++;

            return full;
        }

        public long KeeperLevel => PlayerProgression.Level.Level;

        public long BestWave(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return EndlessLedger.Best;

            return LevelId.TryParse(scope, out var level, out _) ? EndlessLedger.BestFor(level) : 0L;
        }

        public long Lifetime(TaskGoal goal) => LifetimeTally.Count(goal);

        ChapterIndexEntry ChapterOf(string scope)
        {
            if (_index == null) return null;
            return ChapterId.TryParse(scope, out var id, out _) ? _index.FindChapter(id) : null;
        }
    }

    /// <summary>
    /// The same six readings taken off a <em>save file</em>, which is the shape that can be
    /// published and the shape the shared vectors drive.
    ///
    /// <para>
    /// <b>Bounded by the shipped catalog, deliberately, and that is the one place this may
    /// legitimately answer less than the ledger does.</b> A record naming a level no longer in
    /// the manifest is not counted here, exactly as <c>derivedXp</c> refuses to pay for one on
    /// the server: an unrecognised level must never mint anything, and a rank is now something
    /// strangers read. For a real save the two readings are identical, because every record a
    /// device holds is of a glade it was dealt; the difference can only ever make this side
    /// answer <em>lower</em>, which is invariant 19a's safe direction.
    /// </para>
    /// <para>
    /// <b>The keeper level is handed in rather than derived.</b> It is the one reading whose
    /// answer is already computed by whoever is building the card — the server derives it from
    /// XP it recomputes and refuses the save's own claim, and the client passes the level the
    /// progression ledger holds. Deriving it a second time here would be a third copy of the
    /// curve, and the two that exist are already held together by the shared vectors.
    /// </para>
    /// <para>
    /// <b>The lifetime floor is applied here too.</b> <see cref="LifetimeTally.Count"/> answers
    /// <c>max(counted, proved)</c> so that an account older than the feature reads correctly
    /// rather than starting again from nought, and a card built without that floor would publish
    /// a rank below the one the player's own map is showing them. The proof is read off the same
    /// save, so the two sides agree by construction.
    /// </para>
    /// </summary>
    public sealed class SaveRankSource : IRankSource
    {
        readonly SaveFileDto _save;
        readonly CatalogIndex _index;
        readonly long _keeperLevel;

        public SaveRankSource(SaveFileDto save, CatalogIndex index, int keeperLevel)
        {
            _save = save;
            _index = index;
            _keeperLevel = keeperLevel < 1 ? 1 : keeperLevel;
        }

        public long KeeperLevel => _keeperLevel;

        public long Cleared(string scope) => Walk(scope, stars => stars > 0 ? 1 : 0);

        public long Stars(string scope) => Walk(scope, stars => stars);

        public long ThreeStars(string scope) => Walk(scope, stars => stars >= 3 ? 1 : 0);

        /// <summary>
        /// One walk over the save's level rows, scoped and catalog-bounded, with the per-row
        /// reading handed in.
        ///
        /// <para>
        /// Written once rather than three times because the three differ only in what a row is
        /// worth, and three walks would be three chances to forget the catalog bound or the
        /// star clamp. The clamp is <c>LevelRecord</c>'s own and mirrors <c>MAX_STARS</c> on the
        /// server: a forged row claiming ninety stars must be worth three.
        /// </para>
        /// </summary>
        long Walk(string scope, Func<int, int> worth)
        {
            var rows = _save?.levels;
            if (rows == null || _index == null) return 0L;

            // The scope resolved once into the set of ids that count, rather than asked per
            // row: a chapter entry carries its ids as a list, and a linear search inside a walk
            // over every record in the save is the shape that stops being free the day a
            // catalog grows.
            System.Collections.Generic.HashSet<LevelId> within = null;
            if (!string.IsNullOrEmpty(scope))
            {
                if (!ChapterId.TryParse(scope, out var id, out _)) return 0L;

                var chapter = _index.FindChapter(id);
                if (chapter == null) return 0L;

                within = new System.Collections.Generic.HashSet<LevelId>();
                var ids = chapter.LevelIds;
                for (int i = 0; i < ids.Count; i++) within.Add(ids[i]);
            }

            long total = 0L;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.levelId)) continue;
                if (!LevelId.TryParse(row.levelId, out var level, out _)) continue;

                // The catalog bound. See the class remarks for why this is asked rather than
                // trusting the row, and why answering lower is the safe direction.
                if (within != null)
                {
                    if (!within.Contains(level)) continue;
                }
                else if (!_index.Contains(level)) continue;

                int stars = row.stars;
                if (stars <= 0) continue;
                if (stars > MaxStars) stars = MaxStars;

                total += worth(stars);
            }

            return total;
        }

        /// <summary>
        /// The star clamp, mirroring <c>MAX_STARS</c> in <c>functions/src/grove.ts</c>. A row
        /// claiming more is a forged one, and both sides read it as three.
        /// </summary>
        public const int MaxStars = 3;

        public long BestWave(string scope)
        {
            var rows = _save?.endlessBest;
            if (rows == null) return 0L;

            bool any = !string.IsNullOrEmpty(scope);

            int best = 0;

            // The walk is bounded by the rules' own cap rather than by the array's length,
            // which is the reading `bestWave` takes on the server and the one
            // `EndlessLedger.LifetimeWavesIn` takes here — see its remarks for why a refused
            // row near the top would otherwise let the two sides disagree about a sixty-fifth.
            int walk = rows.Length < EndlessLedger.MaxRows ? rows.Length : EndlessLedger.MaxRows;

            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.level)) continue;

                // The length bound the server applies to the same row (`MAX_LEVEL_ID_LENGTH`,
                // which is this constant). Asked here rather than left out, because a row this
                // side accepted and the server refused is a wave one of them counts and the
                // other does not — which is a rung held on one screen and not on the other.
                if (row.level.Length > LevelId.MaxLength) continue;

                if (any && !string.Equals(row.level, scope, StringComparison.Ordinal)) continue;

                if (row.wave > best) best = row.wave;
            }

            return best <= 0 ? 0L
                 : best > EndlessLedger.MaxWave ? EndlessLedger.MaxWave
                 : best;
        }

        public long Lifetime(TaskGoal goal)
        {
            if (goal == TaskGoal.None) return 0L;

            long counted = 0L;
            var rows = _save?.tasks?.lifetime;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || row.count <= 0) continue;
                    if (TaskGoals.Parse(row.goal) != goal) continue;

                    long value = row.count > LifetimeTally.Ceiling ? LifetimeTally.Ceiling : row.count;
                    if (value > counted) counted = value;
                }

            long proved = Proved(goal);
            return counted > proved ? counted : proved;
        }

        /// <summary>
        /// What this save already proves about a verb — <see cref="LifetimeTally"/>'s floor,
        /// read off the file instead of off the ledgers. The two must agree entry for entry, or
        /// a published rank sits below the one the player's own map draws.
        /// </summary>
        long Proved(TaskGoal goal)
        {
            switch (goal)
            {
                case TaskGoal.Runs:
                case TaskGoal.Wins:
                    return Cleared(string.Empty);

                case TaskGoal.Stars:
                    return Stars(string.Empty);

                case TaskGoal.Waves:
                    return EndlessLedger.LifetimeWavesIn(_save);

                default:
                    return 0L;
            }
        }
    }
}
