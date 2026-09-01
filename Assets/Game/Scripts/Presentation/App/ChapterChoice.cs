using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Which chapter of each mode the player was last looking at.
    ///
    /// <para>
    /// The map shows one chapter at a time (invariant 8) and every way back to it — the back
    /// key, a forfeit, the victory panel, the home screen — arrives carrying no chapter at all,
    /// so without this every one of them opened on <c>LevelUnlock.CurrentChapter</c>: wherever
    /// the player is <em>up to</em>, which on an account that has unlocked everything is the
    /// newest chapter and almost never the one they were just playing. Replaying an early
    /// chapter therefore meant arrowing back to it after every single level.
    /// </para>
    /// <para>
    /// <b>Per mode</b>, because a mode's map is its own place: stepping across the switcher and
    /// back should return each side to where it was, and one shared slot would make the two
    /// overwrite each other. It is the same fact as <see cref="ModeChoice"/> one level finer,
    /// which is why it is stored the same way.
    /// </para>
    /// <para>
    /// <b>Device-local, and never in the save file.</b> A view preference rather than progress:
    /// it moves both ways, so it could never be joined (invariant 11b), and it costs nothing to
    /// get wrong — one arrow tap and it is right again. Nothing keys on it either, so a chapter
    /// id landing here from a build that no longer holds it is dropped on the next read rather
    /// than being a stale id anything has to honour.
    /// </para>
    /// </summary>
    public static class ChapterChoice
    {
        const string Prefix = "glimmer_map_chapter_";

        static string KeyFor(GameMode mode) => Prefix + mode.Value;

        /// <summary>
        /// The remembered chapter of one mode, or null when there is nothing usable to
        /// remember — which is the caller's cue to fall back to wherever the player is up to.
        ///
        /// <para>
        /// Three ways it comes back null, and they are the reason this returns an entry rather
        /// than an id: nothing has been stored; the stored id is no longer in the catalog (a
        /// chapter disabled, a client rolled back, a drop not downloaded); or it is in the
        /// catalog but belongs to another mode now. A map opened on a chapter that is not in
        /// the lane the switcher is showing is a screen whose own arrows lead somewhere else.
        /// </para>
        /// </summary>
        public static ChapterIndexEntry Read(CatalogIndex index, GameMode mode)
        {
            if (index == null || !mode.IsValid) return null;

            string raw = PlayerPrefs.GetString(KeyFor(mode), string.Empty);
            if (!ChapterId.TryParse(raw, out var id, out _)) return null;

            var entry = index.FindChapter(id);
            return entry != null && entry.Mode == mode ? entry : null;
        }

        /// <summary>
        /// Remembers a chapter as the one its mode's map should open on.
        ///
        /// The mode is the chapter's own rather than a parameter, because a chapter belongs to
        /// exactly one and a caller passing the other would file it where nothing will ever
        /// read it.
        ///
        /// <para>
        /// Written on the map's <em>arrival</em>, so the common call is one that changes
        /// nothing — a player stepping in and out of the same chapter. <see cref="DevicePrefs"/>
        /// is what keeps that off the disk.
        /// </para>
        /// </summary>
        public static void Write(ChapterIndexEntry chapter)
        {
            if (chapter == null || !chapter.Id.IsValid || !chapter.Mode.IsValid) return;

            DevicePrefs.WriteString(KeyFor(chapter.Mode), chapter.Id.Value);
        }
    }
}
