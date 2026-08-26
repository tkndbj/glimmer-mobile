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
        /// The key for "31 turns · 2:14", or the hollow's "4 sparks · 1:12".
        ///
        /// Four keys per mode because "1 turns" is wrong in English and worse in languages with
        /// real plural rules, and because a run that resolved before the clock could read
        /// anything has a move count and no time — a dash where the time goes reads as a broken
        /// record rather than an untimed one.
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
