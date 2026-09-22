using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using UnityEngine;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The published challenge slate, loaded once at boot beside <c>ProgressionRules</c> and
    /// read by the screens.
    ///
    /// <b>Its own static, its own file, its own version</b> — see <see cref="ChallengeTableDto"/>
    /// for why a challenge shares nothing with the reward table. A missing or unreadable file
    /// leaves the slate empty, which the hub's door reads as shut; it never falls back to a
    /// built-in challenge, because there is none (invariant 4: content is data).
    /// </summary>
    public static class ChallengeRules
    {
        static ChallengeTable _table = ChallengeTable.Default;

        public static ChallengeTable Table => _table;

        public static bool IsLoaded { get; private set; }

        public static event Action Changed;

        public static async Task LoadAsync(IContentSource source, CancellationToken cancellation = default)
        {
            if (source == null) return;

            var fetch = await source.FetchAsync(ContentPaths.Challenges, cancellation);
            if (!fetch.Success)
            {
                Debug.LogWarning($"[Challenges] could not read {ContentPaths.Challenges} " +
                                 $"({fetch.Error}); no challenge is offered");
                return;
            }

            var problems = new List<string>();
            bool usable = ChallengeTable.TryRead(fetch.Text, out var table, problems);

            foreach (var problem in problems) Debug.LogWarning("[Challenges] " + problem);

            if (!usable)
            {
                Debug.LogWarning("[Challenges] the file is unusable; no challenge is offered");
                return;
            }

            Publish(table);
        }

        public static void Publish(ChallengeTable table)
        {
            if (table == null) return;

            _table = table;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal static void Reset()
        {
            _table = ChallengeTable.Default;
            IsLoaded = false;
        }
    }
}
