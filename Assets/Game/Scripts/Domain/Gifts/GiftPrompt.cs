using System;
using System.Collections.Generic;

namespace GlimmerGrove.Gifts
{
    /// <summary>
    /// When the gift panel is put in front of a player (invariant 63), as a pure rule so
    /// <c>GiftTests</c> can hold it without a frame of Unity. Where the player is standing, and
    /// whether it is calm there, is the caller's to say (<c>GiftMoment</c>).
    ///
    /// <para>
    /// <b>Every waiting gift, whenever one of them is new.</b> The panel is raised once a session
    /// for a gift (a player who put it away with the back key is not nagged until their next
    /// session - the gift waits on the server for ever, so nothing is lost by waiting), and a gift
    /// that arrives mid-session raises it again, carrying every gift still waiting, so the player
    /// never claims half of what they are owed.
    /// </para>
    /// </summary>
    public static class GiftPrompt
    {
        /// <summary>
        /// The gifts to show now: all of <paramref name="waiting"/> if any of them has not been
        /// <paramref name="shown"/> this session; none otherwise.
        /// </summary>
        public static List<AdminGift> Choose(IReadOnlyList<AdminGift> waiting, Func<string, bool> shown)
        {
            var show = new List<AdminGift>();
            if (waiting == null || waiting.Count == 0) return show;

            bool fresh = false;
            foreach (var gift in waiting)
            {
                if (gift == null) continue;
                if (shown == null || !shown(gift.Id)) fresh = true;
            }
            if (!fresh) return show;

            foreach (var gift in waiting) if (gift != null) show.Add(gift);
            return show;
        }
    }
}
