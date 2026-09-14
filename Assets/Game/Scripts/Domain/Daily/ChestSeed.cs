namespace GlimmerGrove.Daily
{
    /// <summary>
    /// What a chest is rolled from: who owns it and which chest it is, in one of the two
    /// layouts <see cref="ChestRandom"/> hashes.
    ///
    /// <para>
    /// A chest used to be identified by a day and an index and nothing else, so the seed
    /// was three loose arguments on every roll. A task's chest is identified by a task and
    /// the period it was finished in — a <em>subject</em>, in <see cref="ChestRandom"/>'s
    /// terms — and threading a second trio through every method of
    /// <see cref="ChestDefinition"/> would have doubled the one loop whose stream numbers
    /// the server mirrors. So the identity is a value and the roll takes the value.
    /// </para>
    /// <para>
    /// <b>Both layouts are contract</b> (invariant 9c). Which one a reward uses is decided
    /// once, where the reward is built, and the TypeScript half recomputes the same hash
    /// from the same three parts: <c>chestSeed</c> for a day, <c>subjectSeed</c> for a
    /// subject. A chest seeded the wrong way is paid a different amount from the one the
    /// player was shown.
    /// </para>
    /// </summary>
    public readonly struct ChestSeed
    {
        readonly string _playerKey;
        readonly bool _bySubject;

        readonly int _dayKey;
        readonly int _chestIndex;

        readonly string _tag;
        readonly string _subject;

        ChestSeed(string playerKey, int dayKey, int chestIndex)
        {
            _playerKey = playerKey ?? string.Empty;
            _bySubject = false;
            _dayKey = dayKey;
            _chestIndex = chestIndex;
            _tag = string.Empty;
            _subject = string.Empty;
        }

        ChestSeed(string playerKey, string tag, string subject)
        {
            _playerKey = playerKey ?? string.Empty;
            _bySubject = true;
            _dayKey = 0;
            _chestIndex = 0;
            _tag = tag ?? string.Empty;
            _subject = subject ?? string.Empty;
        }

        /// <summary>A chest that belongs to a calendar day: the daily ladder's shape.</summary>
        public static ChestSeed ForDay(string playerKey, int dayKey, int chestIndex)
            => new ChestSeed(playerKey, dayKey, chestIndex);

        /// <summary>
        /// A chest that belongs to a <em>thing</em>: a task done in a period, a level's
        /// golden bonus. <paramref name="tag"/> names the feature so two features naming
        /// the same subject cannot share a roll.
        /// </summary>
        public static ChestSeed ForSubject(string playerKey, string tag, string subject)
            => new ChestSeed(playerKey, tag, subject);

        /// <summary>The generator for one independent draw. See <see cref="ChestRandom"/>.</summary>
        public ChestRandom At(int stream)
            => _bySubject
             ? new ChestRandom(_playerKey, _tag, _subject, stream)
             : new ChestRandom(_playerKey, _dayKey, _chestIndex, stream);

        public override string ToString()
            => _bySubject ? $"{_tag}|{_subject}" : $"day {_dayKey} chest {_chestIndex}";
    }
}
