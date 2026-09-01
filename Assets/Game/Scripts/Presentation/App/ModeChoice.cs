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
        /// The remembered mode, or the ordinary one when nothing has been chosen or the
        /// remembered one is no longer in the catalog.
        ///
        /// The second half matters more than it looks: a mode can leave a build — a chapter
        /// disabled, a client rolled back, content that has not downloaded yet — and a map
        /// opening onto a mode with no chapters in it is a blank screen with a back arrow.
        /// </summary>
        public static GameMode Read(CatalogIndex index)
        {
            string raw = PlayerPrefs.GetString(Key, string.Empty);

            if (GameMode.TryParse(raw, out var mode, out _) && mode.IsPlayable
                && index != null && index.ChaptersIn(mode).Count > 0)
                return mode;

            return GameMode.Default;
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
}
