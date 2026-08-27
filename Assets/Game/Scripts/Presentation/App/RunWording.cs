using GlimmerGrove.Content;

namespace GlimmerGrove
{
    /// <summary>
    /// How a finished run is written down, in the one place both screens that write one ask.
    ///
    /// <para>
    /// A glade counts <em>turns</em> and a hollow counts <em>sparks</em>, and the same run has
    /// to read the same way wherever it appears — the node on the map and the victory panel are
    /// already careful to quote a record in exactly one format, and a second mode would have
    /// broken that by having each of them decide the word for itself. So the choice is made
    /// here, from the level's own mode, and both callers ask.
    /// </para>
    /// <para>
    /// The glade keys keep the names they shipped with rather than being renamed to a scheme.
    /// They are on every device's cached string table and in every translation already
    /// delivered; renaming them to tidy up would retranslate four strings to change nothing a
    /// player can see.
    /// </para>
    /// </summary>
    public static class RunWording
    {
        /// <summary>
        /// The key for "31 turns", or a weave's "woven with 44".
        ///
        /// <para>
        /// Two keys per mode, and the <em>strings</em> behind them are as much a part of that
        /// contract as the names are. Both stems shipped reading "{0} turns · {1}", because a
        /// record used to carry a time as well as a count — and when the clock went (invariant
        /// 22) the two timed forms were dropped from this method while the table kept the timed
        /// text. <see cref="Loc.Format"/> swallows the <c>FormatException</c> a missing argument
        /// raises and hands back the pattern, so every map node and every victory panel in the
        /// game printed the literal "{0} turns · {1}" instead of a number. Nothing could see
        /// it: the keys all resolve, so invariant 6's gate passes, and a placeholder the caller
        /// never fills is not a compile error. <c>Tools/verify/loc.py</c> now counts placeholders
        /// against arguments for exactly this.
        /// </para>
        /// </summary>
        public static string RecordKey(LevelId level, int moves)
        {
            var mode = Content.LevelModes.Find(ModeOf(level));
            string stem = mode != null ? mode.RecordStem : "ui.rank.record";

            // Two keys per stem, because "1 turns" is wrong in English and worse in languages
            // with real plural rules. It used to be four: a run also carried a time, and one
            // that resolved before the clock could read anything needed a form with no time
            // in it. There is no clock and a record is a count, so the two timed forms went
            // with it — see LevelRecord.BestMillis for what became of the number itself.
            return moves == 1 ? stem + "_one" : stem;
        }

        /// <summary>
        /// The mode a level belongs to, or the ordinary one when the catalog cannot say.
        ///
        /// Falling back rather than refusing matters here: this is called while a victory panel
        /// is being built, and a level whose chapter has been disabled underneath the player
        /// should cost them a slightly wrong noun rather than an exception over the top of
        /// their reward.
        /// </summary>
        public static GameMode ModeOf(LevelId level)
        {
            var index = GameContent.Index;
            return index != null ? index.ModeOf(level) : GameMode.Default;
        }
    }
}
