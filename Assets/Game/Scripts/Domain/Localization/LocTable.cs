using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Localization
{
    /// <summary>
    /// One language's strings, immutable once built.
    ///
    /// Tables travel the same road as level content — the same JSON, the same source
    /// chain, the same cache — so shipping a new language is a content drop rather
    /// than an app update.
    /// </summary>
    public sealed class LocTable
    {
        public static readonly LocTable Empty = new LocTable(string.Empty, new Dictionary<string, string>());

        readonly Dictionary<string, string> _entries;

        LocTable(string language, Dictionary<string, string> entries)
        {
            Language = language;
            _entries = entries;
        }

        public string Language { get; }

        public int Count => _entries.Count;

        public bool TryGet(string key, out string text)
        {
            if (!string.IsNullOrEmpty(key)) return _entries.TryGetValue(key, out text);
            text = null;
            return false;
        }

        public IEnumerable<string> Keys => _entries.Keys;

        public static LocTable Parse(string json, out string error)
        {
            error = null;
            try
            {
                var dto = JsonUtility.FromJson<LocTableDto>(json);
                if (dto == null) { error = "empty localisation table"; return Empty; }

                var entries = new Dictionary<string, string>();
                if (dto.entries != null)
                {
                    foreach (var e in dto.entries)
                    {
                        if (e == null || string.IsNullOrEmpty(e.key)) continue;
                        entries[e.key] = e.text ?? string.Empty;
                    }
                }
                return new LocTable(dto.language ?? string.Empty, entries);
            }
            catch (System.Exception e)
            {
                error = "localisation table is not valid JSON: " + e.Message;
                return Empty;
            }
        }
    }
}
