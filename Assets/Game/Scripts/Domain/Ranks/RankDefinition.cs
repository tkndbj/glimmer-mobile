using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;

namespace GlimmerGrove.Ranks
{
    /// <summary>
    /// One line of a rung: a measure, what it is about, and how much of it.
    ///
    /// <para>
    /// Authored as three fields and nothing else. There is no operator, no "any of these", and
    /// no expression — every requirement is <c>reading &gt;= target</c>, and a rung is met when
    /// all of its lines are. That is a deliberate floor rather than a first version: an
    /// expression language would be a second thing to validate, a second thing to translate and
    /// a second thing a player has to work out from a screen, and every requirement anybody has
    /// asked for so far is a threshold. A rung that wants "either of two things" is two rungs,
    /// and a ladder is already ordered.
    /// </para>
    /// </summary>
    public sealed class RankRequirement
    {
        public RankRequirement(RankMeasure measure, string scope, int target)
        {
            Measure = measure;
            Scope = scope ?? string.Empty;
            Target = target < 1 ? 1 : target;
        }

        public RankMeasure Measure { get; }

        /// <summary>
        /// The chapter or level this is about, or empty for the whole account. What it may
        /// name is <see cref="RankMeasure.Scope"/>; a scope on a measure that takes none is
        /// refused by the reader rather than ignored.
        /// </summary>
        public string Scope { get; }

        public bool IsScoped => Scope.Length > 0;

        /// <summary>How much of the measure finishes this line. Always at least one.</summary>
        public int Target { get; }

        /// <summary>What the account holds against this line right now. Never falls.</summary>
        public long Held(CatalogIndex index) => RankMeasures.Read(Measure, Scope, index);

        public bool IsMet(CatalogIndex index) => Held(index) >= Target;

        /// <summary>The same line read off a source — a save file, or the live ledgers.</summary>
        public long Held(IRankSource source) => RankMeasures.Read(Measure, Scope, source);

        public bool IsMet(IRankSource source) => Held(source) >= Target;

        /// <summary>
        /// The line as the player reads it.
        ///
        /// <para>
        /// The target is <c>{0}</c> and the scope's own name is <c>{1}</c>, so a retune changes
        /// the sentence without touching a translation and a chapter renamed in
        /// <c>loc/en.json</c> renames itself here. A scope this build cannot resolve — a chapter
        /// a newer pack named, a level withdrawn from the manifest — falls back to the unscoped
        /// sentence rather than printing a raw id at a player.
        /// </para>
        /// </summary>
        public string Sentence(CatalogIndex index)
        {
            if (IsScoped)
            {
                string named = ScopeName(index);
                if (named.Length > 0)
                {
                    string key = RankMeasures.SentenceKey(Measure, scoped: true);
                    if (Loc.Has(key)) return Loc.Format(key, Target, named);
                }
            }

            return Loc.Format(RankMeasures.SentenceKey(Measure, scoped: false), Target);
        }

        /// <summary>The scope's player-facing name, or empty when nothing in this build names it.</summary>
        public string ScopeName(CatalogIndex index)
        {
            if (!IsScoped) return string.Empty;

            switch (Measure.Scope)
            {
                case RankScopeKind.Chapter:
                {
                    if (!ChapterId.TryParse(Scope, out var id, out _)) return string.Empty;
                    string key = ChapterDefinition.DefaultNameKey(id);
                    return Loc.Has(key) ? Loc.Get(key) : string.Empty;
                }

                case RankScopeKind.Level:
                {
                    if (!LevelId.TryParse(Scope, out var id, out _)) return string.Empty;
                    string key = LevelDefinition.DefaultNameKey(id);
                    return Loc.Has(key) ? Loc.Get(key) : string.Empty;
                }

                default: return string.Empty;
            }
        }

        public override string ToString()
            => Measure.Id + (IsScoped ? "@" + Scope : string.Empty) + " >= " + Target;
    }

    /// <summary>
    /// One rung of the rank ladder: a permanent id, its place, and what it asks for.
    ///
    /// <para>
    /// <b>A rank is derived and stored nowhere</b>, which is invariant 14 taken as far as it
    /// goes: there is no counter to merge, no claim to adjudicate, no floor to seed and no
    /// migration to write, and the same account answers the same rank on every device and on a
    /// fresh install without a single byte of new save state. That is only sound because every
    /// measure it is built out of is monotone (invariant 52) — a derived badge over a reading
    /// that could fall would be a badge taken away from somebody who did nothing wrong.
    /// </para>
    /// <para>
    /// <b>The id names art and copy and is therefore permanent</b> in the way a chest tier's is:
    /// the badge is <c>Ui/Rank/{id}</c> and the name is <c>rank.{id}.name</c>, both derived, so
    /// anything holding a rank can draw and name it without reading the ladder. Nothing in a
    /// save or on the wire holds one, so retiring a rung costs a picture and a string rather
    /// than a spent id — but a rung that is <em>renamed</em> has to move its picture in the same
    /// change, which is what <c>check_ranks</c> refuses to let anyone forget.
    /// </para>
    /// <para>
    /// <b>It pays nothing, deliberately.</b> A rank is a reading of what has already been
    /// rewarded — stars paid credits, waves paid XP — so paying again for the same play would
    /// be the economy counting one thing twice, and a currency the client could hand out on a
    /// figure the server cannot recompute is what invariant 13 exists to refuse. The seam if
    /// that ever changes is invariant 14a's: one monotonic integer per rung merged by
    /// <c>max</c>, and never a stored amount.
    /// </para>
    /// </summary>
    public sealed class RankDefinition
    {
        public RankDefinition(string id, int ordinal, IReadOnlyList<RankRequirement> requirements)
        {
            Id = id ?? string.Empty;
            Ordinal = ordinal < 1 ? 1 : ordinal;
            Requirements = requirements ?? Array.Empty<RankRequirement>();
        }

        public string Id { get; }

        /// <summary>One for the first badge, counting up. Position on the ladder.</summary>
        public int Ordinal { get; }

        /// <summary>Every line, in authored order. Never null and never empty in a built ladder.</summary>
        public IReadOnlyList<RankRequirement> Requirements { get; }

        /// <summary>Derived, never authored, for <c>ChestTier.NameKey</c>'s reason.</summary>
        public string NameKey => "rank." + Id + ".name";

        /// <summary>The one-line flavour under the name, in the same shape.</summary>
        public string BlurbKey => "rank." + Id + ".blurb";

        /// <summary>The badge: <c>Ui/Rank/{id}</c>, small and global. See <c>AssetManifest</c>.</summary>
        public string Icon => "Ui/Rank/" + Id;

        public string Name => Loc.Get(NameKey);

        /// <summary>
        /// Whether every line of <em>this rung alone</em> is met. Holding the rung also needs
        /// the one below it — see <see cref="RankLadder.Held"/>, which is where that is decided
        /// so it cannot be decided differently twice.
        /// </summary>
        public bool IsMet(CatalogIndex index) => IsMet(new LedgerRankSource(index));

        /// <summary>The same question asked of a save file. See <see cref="IRankSource"/>.</summary>
        public bool IsMet(IRankSource source)
        {
            for (int i = 0; i < Requirements.Count; i++)
                if (!Requirements[i].IsMet(source)) return false;

            return Requirements.Count > 0;
        }

        /// <summary>How many lines are met, for a screen that wants to say "2 of 4".</summary>
        public int MetCount(CatalogIndex index)
        {
            int met = 0;
            for (int i = 0; i < Requirements.Count; i++)
                if (Requirements[i].IsMet(index)) met++;

            return met;
        }

        /// <summary>
        /// What a legal rung id looks like: the same alphabet a task id uses, because it has to
        /// survive a loc key, a file name on disk and an Addressables address.
        /// </summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }

            return true;
        }

        public const int MaxIdLength = 32;

        public override string ToString() => Id + " #" + Ordinal + " (" + Requirements.Count + " line(s))";
    }
}
