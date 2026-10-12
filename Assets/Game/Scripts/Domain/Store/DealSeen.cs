using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// Which shop deals this account has been shown as a popup (invariant 60c). <b>A record only:
    /// nothing gates on it.</b> The popup is paced once a UTC day by <c>DealMoment</c>; this list
    /// is still written and joined so the save's shape and wire stay as shipped (11f).
    ///
    /// <para>
    /// <b>In the save.</b> It rides inside the <c>tasks</c> map
    /// (<see cref="TaskStateDto.dealsSeen"/>) for <c>lifetime</c>'s reason: a key inside a map
    /// already on <c>hasOnly</c> needs no rules release.
    /// </para>
    /// <para>
    /// <b>The newest <see cref="MaxIds"/> ids, and that is the whole merge.</b> A deal id begins
    /// with the UTC minute it opened (<c>d202610091530k3x9</c>), so ordinal order is
    /// chronological, and "the newest N of the union" is associative, commutative and idempotent -
    /// a join, which is what invariant 11b asks of anything a merge touches. Nothing is pruned
    /// against the published list: that would make the set depend on a network read, and a loader
    /// that drops what it read makes every write owe a sync (11f). Sixty-four is far more than can
    /// ever be live at once (one, today), so an id that falls off the end belongs to a deal ended
    /// long ago.
    /// </para>
    /// </summary>
    public static class DealSeen
    {
        /// <summary>The most ids kept. Matches the rules' bound on <c>tasks.dealsSeen</c>.</summary>
        public const int MaxIds = 64;

        static readonly SortedSet<string> _ids = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Raised when an id is added, by a showing or by a sync.</summary>
        public static event Action Changed;

        public static bool Has(string dealId) => !string.IsNullOrEmpty(dealId) && _ids.Contains(dealId);

        public static int Count => _ids.Count;

        /// <summary>
        /// Records that these deals were put in front of the player. Saved at once and synced soon.
        /// </summary>
        public static void Mark(IEnumerable<string> dealIds)
        {
            if (dealIds == null) return;

            bool moved = false;
            foreach (string id in dealIds)
                if (ShopDeals.IsId(id) && _ids.Add(id)) moved = true;

            if (!moved) return;

            Trim(_ids);
            SaveService.Save();
            Cloud.CloudSaveService.RequestSyncEventually();
            Raise();
        }

        // --------------------------------------------------- file bridge (internal)
        /// <summary>Reads the save's list. Keeps exactly what <see cref="Write"/> writes, so a round trip moves nothing.</summary>
        internal static void LoadFrom(string[] ids)
        {
            _ids.Clear();
            if (ids != null) foreach (string id in ids) if (ShopDeals.IsId(id)) _ids.Add(id);
            Trim(_ids);
            Raise();
        }

        /// <summary>Sorted, so <c>SaveDelta</c> can walk two lists in order.</summary>
        internal static string[] Write()
        {
            var ids = new string[_ids.Count];
            _ids.CopyTo(ids);
            return ids;
        }

        /// <summary>Two devices' lists: the newest <see cref="MaxIds"/> of the union.</summary>
        internal static string[] Join(string[] mine, string[] other)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            if (mine != null) foreach (string id in mine) if (ShopDeals.IsId(id)) ids.Add(id);
            if (other != null) foreach (string id in other) if (ShopDeals.IsId(id)) ids.Add(id);
            Trim(ids);

            var joined = new string[ids.Count];
            ids.CopyTo(joined);
            return joined;
        }

        internal static void Reset()
        {
            _ids.Clear();
            Raise();
        }

        /// <summary>Drops the oldest ids past <see cref="MaxIds"/>. Ordinal order is time order.</summary>
        static void Trim(SortedSet<string> ids)
        {
            while (ids.Count > MaxIds) ids.Remove(ids.Min);
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
