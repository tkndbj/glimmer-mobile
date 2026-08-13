using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The progression table the game is currently using, read synchronously anywhere.
    ///
    /// Mirrors <see cref="GameContent"/> and <see cref="Localization.Loc"/> exactly:
    /// loaded once during the splash through the same layered source, so a downloaded
    /// content pack can retune the curve the way it can add a chapter. Publishing
    /// swaps a whole immutable table in one assignment.
    ///
    /// Until it loads, <see cref="ProgressionTable.Default"/> is in force, so nothing
    /// has to null-check and a failed read costs a retune rather than a session.
    /// </summary>
    public static class ProgressionRules
    {
        static ProgressionTable _table = ProgressionTable.Default;

        public static ProgressionTable Table => _table;

        public static bool IsLoaded { get; private set; }

        /// <summary>Raised after a new table is published, so HUDs can repaint.</summary>
        public static event Action Changed;

        public static async Task LoadAsync(IContentSource source, CancellationToken cancellation = default)
        {
            if (source == null) return;

            var fetch = await source.FetchAsync(ContentPaths.Progression, cancellation);
            if (!fetch.Success)
            {
                Debug.LogWarning($"[Progression] could not read {ContentPaths.Progression} " +
                                 $"({fetch.Error}); using the built-in curve");
                return;
            }

            var problems = new List<string>();
            if (!ProgressionTable.TryRead(fetch.Text, out var table, problems))
            {
                foreach (var problem in problems) Debug.LogWarning("[Progression] " + problem);
                Debug.LogWarning("[Progression] falling back to the built-in curve");
                return;
            }

            foreach (var problem in problems) Debug.LogWarning("[Progression] " + problem);

            Publish(table);
        }

        public static void Publish(ProgressionTable table)
        {
            if (table == null) return;

            _table = table;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Restores the built-in curve. Tests use this to stay independent.</summary>
        internal static void Reset()
        {
            _table = ProgressionTable.Default;
            IsLoaded = false;
        }
    }
}
