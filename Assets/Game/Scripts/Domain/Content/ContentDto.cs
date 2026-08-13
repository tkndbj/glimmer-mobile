using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The wire format, kept separate from the runtime model on purpose.
    ///
    /// These types mirror the JSON exactly and nothing else depends on them, so the
    /// file format can gain fields, rename things or grow a v2 without any of the
    /// game's logic changing shape. They are written for Unity's JsonUtility, which
    /// means public fields, no dictionaries and no properties — and, usefully, it
    /// silently ignores unknown fields, which is precisely the forward compatibility
    /// an old client needs when it meets newer content.
    ///
    /// Empty or zero always means "not specified, fall back" rather than a real
    /// value, so a content author only writes what actually differs.
    /// </summary>
    [Serializable]
    public sealed class ManifestDto
    {
        public int schemaVersion;
        public ManifestChapterDto[] chapters;
    }

    [Serializable]
    public sealed class ManifestChapterDto
    {
        public string id;

        /// <summary>Bumped whenever the chapter file changes, so the cache knows to refetch.</summary>
        public int version;

        /// <summary>Sort order across chapters. Leave gaps so later chapters can slot in.</summary>
        public int order;

        /// <summary>Set false to retire a chapter without deleting it from the server.</summary>
        public bool disabled;

        /// <summary>Minimum app build that may load this chapter. 0 means anything.</summary>
        public int minAppVersion;
    }

    [Serializable]
    public sealed class ChapterDto
    {
        public int schemaVersion;
        public string id;
        public int order;
        public string nameKey;

        // shared art, inherited by every level in the chapter
        public string accent;
        public string slate;
        public string backdrop;

        /// <summary>Map strips stacked bottom to top, forming this chapter's road.</summary>
        public string[] mapStrips;

        public LevelDto[] levels;
    }

    [Serializable]
    public sealed class LevelDto
    {
        public string id;

        // ---- layout, frozen once shipped -----------------------------------
        public int width;
        public int height;
        public string[] rows;

        // ---- tuning, safe to change after launch ---------------------------
        /// <summary>Leave at 0 to derive par from the board, which is always correct.</summary>
        public int par;
        public float goldFactor;
        public float silverFactor;
        public int hintAllowance;

        // ---- presentation, all optional ------------------------------------
        public float mapX;
        public float mapY;
        public string accent;
        public string slate;
        public string backdrop;

        // ---- text, referenced by key and never shown raw --------------------
        public string nameKey;
        public string taglineKey;
        public string lessonKey;
    }

    [Serializable]
    public sealed class LocTableDto
    {
        public string language;
        public LocEntryDto[] entries;
    }

    [Serializable]
    public sealed class LocEntryDto
    {
        public string key;
        public string text;
    }
}
