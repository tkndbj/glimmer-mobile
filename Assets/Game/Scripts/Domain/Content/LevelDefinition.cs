using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// One authored level, fully validated and immutable.
    ///
    /// <para>
    /// Nothing in here is an array index. A level knows its own name and its chapter's name, and
    /// that is all the identity the rest of the game needs; ordering is a property of the
    /// <see cref="LevelCatalog"/>, not of the level, so inserting a tutorial level in the middle
    /// of a shipped chapter is a content edit rather than a save-data disaster.
    /// </para>
    /// <para>
    /// <b>How it is played lives in one field</b>, <see cref="Rules"/>, which is never null. It
    /// used to be a nullable field per mode with exactly one of them set, and that shape is what
    /// made every new mode expensive: every reader had to know the whole list, adding one meant
    /// editing all of them, and "not a board, therefore a hollow" was correct until the day it
    /// silently was not. One field means a reader asks <see cref="Rules"/> what it is holding
    /// instead of guessing by elimination.
    /// </para>
    /// </summary>
    public sealed class LevelDefinition
    {
        public readonly LevelId Id;
        public readonly ChapterId Chapter;

        /// <summary>How this level is played. Never null — see <see cref="ILevelRules"/>.</summary>
        public readonly ILevelRules Rules;

        public readonly LevelTuning Tuning;
        public readonly LevelPresentation Presentation;

        /// <summary>
        /// A classic glade, built from its grid.
        ///
        /// Kept because the conduit mode is most of the game and most of the test suite, and
        /// because writing <c>new GladeRules(layout)</c> at every call site would be ceremony
        /// with no reader. It is the only mode with a shorthand, which is right: the others are
        /// read from content and never hand-built.
        /// </summary>
        public LevelDefinition(LevelId id, ChapterId chapter, LevelLayout layout,
                               LevelTuning tuning, LevelPresentation presentation)
            : this(id, chapter,
                   new GladeRules(layout ?? throw new ArgumentNullException(
                                      nameof(layout), "a glade needs a grid")),
                   tuning, presentation) { }

        public LevelDefinition(LevelId id, ChapterId chapter, ILevelRules rules,
                               LevelTuning tuning, LevelPresentation presentation)
        {
            if (!id.IsValid) throw new ArgumentException("level needs a valid id", nameof(id));
            if (!chapter.IsValid) throw new ArgumentException("level needs a valid chapter", nameof(chapter));

            Id = id;
            Chapter = chapter;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules),
                                                            "a level needs rules to be played by");
            Tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        }

        /// <summary>Which way of playing this level belongs to.</summary>
        public GameMode Mode => Rules.Mode;

        /// <summary>
        /// The conduit grid, or null on a level of any other mode.
        ///
        /// Kept as a property because the classic mode is most of the game and every one of its
        /// readers would otherwise cast. <c>HasBoard</c> is the guard, and
        /// <c>Tools/verify/compile.py</c> refuses a file that reads this without ever admitting
        /// it can be absent — that check exists because forgetting has crashed a build twice.
        /// </summary>
        public LevelLayout Layout => (Rules as GladeRules)?.Layout;

        /// <summary>Whether this level is played on a conduit board at all.</summary>
        public bool HasBoard => Rules is GladeRules;

        /// <summary>The rules as a particular mode's, or null if it is not that mode.</summary>
        public T RulesAs<T>() where T : class, ILevelRules => Rules as T;

        /// <summary>Localisation keys. Never display these raw - go through Loc.</summary>
        public string NameKey => DefaultNameKey(Id);
        public string TaglineKey => DefaultTaglineKey(Id);

        /// <summary>
        /// A level's strings are a pure function of its id, by convention and with no override.
        /// That is what lets anything holding a <see cref="LevelId"/> — the map, the home
        /// screen's "next up" line, the win overlay naming what just opened — label a level
        /// without reading its chapter body.
        /// </summary>
        public static string DefaultNameKey(LevelId id) => "level." + id.Value + ".name";
        public static string DefaultTaglineKey(LevelId id) => "level." + id.Value + ".tagline";

        // A third key, `level.<id>.lesson`, is retired. It was a line of flavour floated along
        // the bottom of any run with nothing new to teach — so on every level of every mode
        // after the first few, which is furniture rather than something anybody reads. The tips
        // are what a board has to say. The key is not re-pointed at anything else: a level's
        // strings are a pure function of its id, so re-using the suffix for a different sentence
        // would silently re-label sixty levels of authored prose.

        public override string ToString() => Id.Value;
    }
}
