using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Every way of playing this build can honour, in the order the switcher offers them.
    ///
    /// <para>
    /// <b>This list is the only place a mode is registered.</b> Everything else — the content
    /// mapper, the validator, the catalog index, the map's switcher — asks here rather than
    /// enumerating modes it happens to know about, so a fifth mode is a subclass and one line
    /// and nothing else in the game has to be edited or even recompiled against a new case.
    /// </para>
    /// <para>
    /// The classic mode is first and stays first: it is where a new player is, and a switcher
    /// that reorders itself as modes are added would move the entry somebody reaches for without
    /// looking. Order after that is the order they shipped.
    /// </para>
    /// <para>
    /// A chapter naming a mode this build has never heard of is skipped whole and reported to
    /// nobody — content ships ahead of builds, so an unknown mode is content from the future and
    /// the honest response is to lose that chapter rather than open it into a screen that cannot
    /// run it. That is invariant 20, and it is <see cref="Find"/> answering null.
    /// </para>
    /// </summary>
    public static class LevelModes
    {
        static readonly LevelMode[] _all =
        {
            new GladeMode(),
            new FallMode(),
            new KeeperMode(),
            new BudMode(),
        };

        public static IReadOnlyList<LevelMode> All => _all;

        /// <summary>The mode with this id, or null if this build cannot play it.</summary>
        public static LevelMode Find(GameMode mode)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Mode == mode) return _all[i];
            return null;
        }

        public static LevelMode Find(string id)
            => GameMode.TryParse(id, out var mode, out _) ? Find(mode) : null;

        /// <summary>Whether this build knows how to play it.</summary>
        public static bool CanPlay(GameMode mode) => Find(mode) != null;

        /// <summary>
        /// The mode that claims this level's authored block, or null if none does.
        ///
        /// Asked in registration order, so a level carrying two blocks is read as the earlier
        /// mode rather than by whichever reader happened to run first — deterministic, and the
        /// validator complains about the second block separately.
        /// </summary>
        public static LevelMode Claimant(LevelDto dto)
        {
            if (dto == null) return null;

            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Claims(dto)) return _all[i];
            return null;
        }

        /// <summary>Every mode's id, for anything that needs the list rather than the behaviour.</summary>
        public static IReadOnlyList<GameMode> Ids
        {
            get
            {
                if (_ids != null) return _ids;

                var ids = new GameMode[_all.Length];
                for (int i = 0; i < _all.Length; i++) ids[i] = _all[i].Mode;
                _ids = ids;
                return _ids;
            }
        }

        static GameMode[] _ids;
    }
}
