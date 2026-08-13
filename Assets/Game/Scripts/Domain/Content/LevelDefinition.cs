using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// One authored level, fully validated and immutable.
    ///
    /// Nothing in here is an array index. A level knows its own name and its
    /// chapter's name, and that is all the identity the rest of the game needs;
    /// ordering is a property of the <see cref="LevelCatalog"/>, not of the level,
    /// so inserting a tutorial level in the middle of a shipped chapter is a
    /// content edit rather than a save-data disaster.
    /// </summary>
    public sealed class LevelDefinition
    {
        public readonly LevelId Id;
        public readonly ChapterId Chapter;

        public readonly LevelLayout Layout;
        public readonly LevelTuning Tuning;
        public readonly LevelPresentation Presentation;

        /// <summary>Localisation keys. Never display these raw — go through Loc.</summary>
        public readonly string NameKey;
        public readonly string TaglineKey;
        public readonly string LessonKey;

        public LevelDefinition(LevelId id, ChapterId chapter, LevelLayout layout, LevelTuning tuning,
                               LevelPresentation presentation,
                               string nameKey, string taglineKey, string lessonKey)
        {
            if (!id.IsValid) throw new ArgumentException("level needs a valid id", nameof(id));
            if (!chapter.IsValid) throw new ArgumentException("level needs a valid chapter", nameof(chapter));

            Id = id;
            Chapter = chapter;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));

            NameKey = string.IsNullOrEmpty(nameKey) ? DefaultNameKey(id) : nameKey;
            TaglineKey = string.IsNullOrEmpty(taglineKey) ? DefaultTaglineKey(id) : taglineKey;
            LessonKey = string.IsNullOrEmpty(lessonKey) ? DefaultLessonKey(id) : lessonKey;
        }

        /// <summary>
        /// Authors may leave the keys out; these conventions mean a level only has to
        /// name itself once. Anything unconventional can still be set explicitly.
        /// </summary>
        public static string DefaultNameKey(LevelId id) => "level." + id.Value + ".name";
        public static string DefaultTaglineKey(LevelId id) => "level." + id.Value + ".tagline";
        public static string DefaultLessonKey(LevelId id) => "level." + id.Value + ".lesson";

        public override string ToString() => Id.Value;
    }
}
