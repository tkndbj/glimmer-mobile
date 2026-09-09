using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Which mode's map the player was last looking at.
    ///
    /// <para>
    /// <b>Device-local, and never in the save file.</b> It is a view preference rather than
    /// progress: it moves both ways, so it could never be joined (invariant 11b), and merged by
    /// recency it would need a stamp of its own to carry one bit that costs nothing to get
    /// wrong — the player taps the switcher and it is right again. <c>RunGuard</c> and
    /// <c>GrovePublishPolicy</c> keep their state here for the same reason.
    /// </para>
    /// </summary>
    public static class ModeChoice
    {
        const string Key = "glimmer_map_mode";

        /// <summary>
        /// The remembered mode, or the catalog's own default when nothing has been chosen or
        /// the remembered one is no longer in it.
        ///
        /// <para>
        /// The second half matters more than it looks: a mode can leave a build — a chapter
        /// disabled, a client rolled back, content that has not downloaded yet — and a map
        /// opening onto a mode with no chapters in it is a blank screen with a back arrow.
        /// </para>
        /// <para>
        /// <b>The fallback is <see cref="CatalogIndex.DefaultMode"/> rather than
        /// <see cref="GameMode.Default"/>, and that is the same rule applied to the fallback
        /// itself.</b> The constant is a parsing answer — a chapter with no <c>mode</c> field is
        /// a glade — and it is neither the mode this game leads with nor, once every glade
        /// chapter is disabled, a mode this catalog can open at all: exactly the blank screen the
        /// clause above exists to prevent. What the catalog answers is the first row of the
        /// switcher, so a player who has never touched the control lands on what it offers first.
        /// </para>
        /// </summary>
        public static GameMode Read(CatalogIndex index)
        {
            string raw = PlayerPrefs.GetString(Key, string.Empty);

            // **Nothing remembered is answered before anything is parsed, and it has to be.**
            // `GameMode.TryParse` answers *true* for an empty string, with the glade — because a
            // chapter with no `mode` field is a glade, for ever, and that is the question that
            // method exists to answer. Reading a stored preference through it therefore cannot
            // tell "this player has never touched the switcher" from "this player chose the
            // classic mode", and both came back as the glade. That was invisible for as long as
            // the glade was also the fallback, and became a map that opened on the classic mode
            // on a device that had never chosen it the moment the front door moved.
            if (string.IsNullOrEmpty(raw))
                return index != null ? index.DefaultMode : GameMode.Default;

            // **`TracksIn` rather than `ChaptersIn`**, which answers the ordinary ladder alone
            // (see `CatalogIndex.ChaptersIn`): a mode whose only chapters were on a second lane
            // would read as a mode with nothing in it and be refused, which is the blank screen
            // this clause exists to prevent, arrived at from the other direction.
            if (GameMode.TryParse(raw, out var mode, out _) && mode.IsPlayable
                && index != null && index.TracksIn(mode).Count > 0)
                return mode;

            return index != null ? index.DefaultMode : GameMode.Default;
        }

        /// <summary>
        /// Remembers a mode. Written on the map's <em>arrival</em> rather than on the tap, so
        /// it is set far more often than it changes — hence <see cref="DevicePrefs.WriteString"/>,
        /// which does nothing at all when it already says this.
        /// </summary>
        public static void Write(GameMode mode)
        {
            if (!mode.IsValid) return;

            DevicePrefs.WriteString(Key, mode.Value);
        }
    }

    /// <summary>
    /// Which ladder of each mode the player was last looking at.
    ///
    /// <para>
    /// <see cref="ModeChoice"/> one level finer, stored the same way and for the same reasons:
    /// device-local and never in the save, because it moves both ways and so could never be
    /// joined (invariant 11b), and it costs nothing to get wrong — one tap and it is right again.
    /// Nothing keys on it, so invariant 1 does not reach it.
    /// </para>
    /// <para>
    /// <b>Per mode</b>, because a mode is where a ladder lives: a player who has been running the
    /// endless lane of one mode has said nothing at all about another mode's lanes.
    /// </para>
    /// <para>
    /// <b>A hint and never an authority</b>, which is <see cref="ChapterChoice"/>'s rule: it
    /// answers the main track the moment the remembered one is not a lane this catalog has
    /// chapters on — a rollback, a disabled chapter, a drop that has not downloaded — and the
    /// caller falls back rather than opening onto nothing.
    /// </para>
    /// </summary>
    public static class TrackChoice
    {
        const string Prefix = "glimmer_map_track_";

        static string KeyFor(GameMode mode) => Prefix + mode.Value;

        public static GameTrack Read(CatalogIndex index, GameMode mode)
        {
            if (index == null || !mode.IsValid) return GameTrack.Main;

            string raw = PlayerPrefs.GetString(KeyFor(mode), string.Empty);

            // Nothing remembered is answered before anything is parsed, for `ModeChoice.Read`'s
            // reason: `GameTrack.TryParse` answers *true* for an empty string with the main track,
            // because a chapter with no `track` field is on the main ladder for ever.
            if (string.IsNullOrEmpty(raw)) return GameTrack.Main;

            if (GameTrack.TryParse(raw, out var track, out _)
                && index.ChaptersIn(mode, track).Count > 0)
                return track;

            return GameTrack.Main;
        }

        /// <summary>
        /// Remembers a ladder. Written on the map's <em>arrival</em> rather than on the tap, so it
        /// is set far more often than it changes — hence <see cref="DevicePrefs.WriteString"/>,
        /// which does nothing at all when it already says this.
        /// </summary>
        public static void Write(GameMode mode, GameTrack track)
        {
            if (!mode.IsValid) return;

            DevicePrefs.WriteString(KeyFor(mode), track.Value);
        }
    }
}
