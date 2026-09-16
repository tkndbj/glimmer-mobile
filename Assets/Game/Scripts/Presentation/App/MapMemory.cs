using System;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Whose map a remembered mode, lane or chapter belongs to.
    ///
    /// <para>
    /// <see cref="ModeChoice"/>, <see cref="TrackChoice"/> and <see cref="ChapterChoice"/> are
    /// device-local view preferences (invariant 8b) — and the device is not the player. A phone
    /// that switches account used to open the map on the chapter the <em>previous</em> account
    /// was looking at, which on a fresh account is a chapter three walls away, and on the
    /// owner's own second account read as "the map did not reset". The preference is right for
    /// the account that wrote it and wrong for every other, so every value written carries the
    /// account that wrote it and is answered only to that account.
    /// </para>
    /// <para>
    /// <b>The stamp rides in the value rather than in a key of its own</b>, so there is no
    /// moment at which a fresh owner stamp vouches for a stale chapter under a key nobody has
    /// rewritten yet. A value written before this existed carries no stamp, reads as nothing
    /// remembered, and is replaced on the next arrival — one map opened on the default once.
    /// </para>
    /// <para>
    /// Keyed on <see cref="CloudState.UserId"/>: empty until the first sign-in, so a first
    /// launch remembers its map under nobody and forgets it the moment an account is minted.
    /// That is the cheapest honest answer, and it costs one arrow tap on the first day.
    /// </para>
    /// </summary>
    public static class MapMemory
    {
        /// <summary>Never a character of an account id or a content id, so the split is exact.</summary>
        const char Stamp = '|';

        /// <summary>The value under <paramref name="key"/> if this account wrote it, else empty.</summary>
        public static string Read(string key)
        {
            string raw = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            int at = raw.IndexOf(Stamp);
            if (at < 0) return string.Empty;

            return string.Equals(raw.Substring(0, at), CloudState.UserId, StringComparison.Ordinal)
                ? raw.Substring(at + 1)
                : string.Empty;
        }

        /// <summary>Remembers <paramref name="value"/> for this account, and nobody else.</summary>
        public static void Write(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
            DevicePrefs.WriteString(key, CloudState.UserId + Stamp + value);
        }
    }
}
