namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Where an endless run begins: the wave it opens on and the ranks every turret stands at.
    ///
    /// <para>
    /// <b>A checkpoint moves the clock and the line, and nothing else</b> (MODES.md 43f). The wave
    /// a run opens on is exactly the wave a run that walked there meets, because an endless lane
    /// is a pure function of the wave number (<see cref="SiegeEndless"/>) - so nothing here has to
    /// reproduce a difficulty, only name an index. What a walked run has and a checkpoint run does
    /// not is the cogs it picked up on the way, which is the head start: every turret stands
    /// <see cref="Ranks"/> rungs up, exactly as if that many cogs had been spent on it.
    /// </para>
    /// <para>
    /// <b>Laddered levels ignore it.</b> A chapter's waves are authored as a list with a boss at
    /// its end, and opening one part-way would be a different level wearing the same id - so
    /// <see cref="SiegeBoard.Build(SiegeLayout, Wards.WardLine, SiegeStart)"/> honours a start
    /// only on a layout that <see cref="SiegeLayout.IsEndless">never ends</see>.
    /// </para>
    /// </summary>
    public readonly struct SiegeStart
    {
        /// <summary>The wave the run opens on, counting from one.</summary>
        public readonly int Wave;

        /// <summary>The ranks every turret stands at before the first cog, nought to <see cref="SiegeTuning.MaxRank"/>.</summary>
        public readonly int Ranks;

        public SiegeStart(int wave, int ranks)
        {
            Wave = wave < 1 ? 1 : wave;
            Ranks = ranks < 0 ? 0 : ranks > SiegeTuning.MaxRank ? SiegeTuning.MaxRank : ranks;
        }

        /// <summary>The start every run had before checkpoints: wave one, every turret at rank nought.</summary>
        public static SiegeStart Opening => new SiegeStart(1, 0);

        /// <summary>Whether this is the ordinary opening rather than a checkpoint.</summary>
        public bool IsOpening => Wave <= 1 && Ranks <= 0;

        /// <summary>
        /// How many waves a run opening here has skipped. They are never paid: a run is paid
        /// for what it saw off (<see cref="SiegeBoard.WavesThisRun"/>).
        /// </summary>
        public int Skipped => Wave - 1;
    }
}
