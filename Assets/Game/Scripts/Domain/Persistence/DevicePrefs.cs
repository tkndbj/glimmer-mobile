using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Writing a device-local preference: once, durably, and only when it has changed.
    ///
    /// <para>
    /// <b>Why the flush stays.</b> Unity persists <see cref="PlayerPrefs"/> by itself during
    /// <c>OnApplicationQuit</c>, which on a phone is the ending that almost never happens — an
    /// app is backgrounded and later killed by the OS, and a preference relying on a clean quit
    /// is one that silently fails to stick for most of the people who set it. So every write
    /// here is flushed. That is the same reasoning <see cref="RunGuard"/> spells out for its
    /// marker; the difference is only what it costs to lose one.
    /// </para>
    /// <para>
    /// <b>Why the comparison is in front of it.</b> <see cref="PlayerPrefs.Save"/> serialises
    /// the <em>whole</em> store to disk synchronously, and the preferences a screen writes are
    /// written on <em>arrival</em> — every time, whether or not anything changed, which is
    /// almost always. Two of them on one screen transition is two real file writes on the frame
    /// a player is watching a transition play. Reading first costs an in-memory dictionary
    /// lookup and removes the write entirely in the ordinary case.
    /// </para>
    /// <para>
    /// <b>This is not a cache and must never become one.</b> The comparison reads the store
    /// rather than a remembered last-written value: something else may have written the key
    /// (a test, a migration, a repair), and a shadow copy that disagreed with disk would skip
    /// the one write that actually mattered.
    /// </para>
    /// <para>
    /// In <c>Domain</c> because both halves of the game write preferences — <c>RunGuard</c> and
    /// <c>GroveBoard</c> here, <c>ModeChoice</c> and <c>ChapterChoice</c> in Presentation — and
    /// Domain is the only place both can see. It deliberately does not wrap reading: a read is
    /// already free, and a wrapper over it would only hide which key a class actually owns.
    /// </para>
    /// <para>
    /// <b>Two writers stay outside it, and both are right to.</b> <c>RunGuard</c> must flush a
    /// marker whose whole purpose is to survive the crash — never skipping, because the value
    /// it writes has always just changed. <c>AccountPrompts</c> writes three keys behind one
    /// flush after a count has genuinely moved, and per-key writes here would turn that single
    /// serialisation into three. The rule this class owns is <em>do not flush what has not
    /// changed</em>, not <em>never call <see cref="PlayerPrefs"/></em>; a writer that only ever
    /// writes real changes, and batches them, is already keeping it.
    /// </para>
    /// </summary>
    public static class DevicePrefs
    {
        /// <summary>
        /// Stores <paramref name="value"/> under <paramref name="key"/> and flushes, unless the
        /// store already reads exactly that. Returns whether anything was written — which is
        /// what makes "it skipped the write" a testable claim rather than an assertion.
        /// </summary>
        public static bool WriteString(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return false;

            value = value ?? string.Empty;

            // HasKey as well as the comparison, so the contract is exactly "after this call the
            // store reads back `value`". Without it, writing an empty string over an absent key
            // would compare equal against GetString's own default and be skipped — harmless for
            // today's two callers, which never write one, and a trap for the next.
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetString(key, string.Empty) == value)
                return false;

            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            return true;
        }
    }
}
