using System;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove.Frames
{
    /// <summary>
    /// Which frame the player wears, and which they hold.
    ///
    /// <para>
    /// <b>What is placeholder here, said out loud.</b> The choice lives in a device preference
    /// keyed by account (8b's shape: local, and never joined) and every catalogued frame is
    /// held. Both are deliberate and both are the whole of what changes when frames go on
    /// sale: the held set becomes a union-joined array of permanent ids in <c>SaveFileDto</c>
    /// (the fourth time this game has built that shape — <c>companionsOwned</c>,
    /// <c>heartContainersOwned</c>, <c>wardsOwned</c>) and the worn id a recency-joined pair
    /// like the turret loadout, which is a schema version, four wire places and a rules
    /// release (12, 12a). The frame a stranger sees on a board is then a field on the published
    /// card. Nothing outside this class knows which of those is true today: every caller asks
    /// <see cref="Worn"/> and <see cref="IsHeld"/>.
    /// </para>
    /// </summary>
    public static class FrameLedger
    {
        /// <summary>Raised when the worn frame changes. Screens repaint, never rebuild (44m).</summary>
        public static event Action Changed;

        const string Prefix = "glimmer_frame_";

        static string Key => Prefix + (CloudState.UserId ?? string.Empty);

        /// <summary>The frame the player wears, or null. An id this build cannot resolve is null.</summary>
        public static FrameDefinition Worn => FrameCatalog.Find(PlayerPrefs.GetString(Key, string.Empty));

        /// <summary>
        /// Whether the player holds this frame. Every catalogued frame, until frames are sold
        /// (see the class note); an unknown id is never held.
        /// </summary>
        public static bool IsHeld(string id) => FrameCatalog.Find(id) != null;

        /// <summary>
        /// Wear this frame, or none. Refused for a frame not held, which is what keeps the gate
        /// a gate the day one exists (15a). Answers whether anything changed.
        /// </summary>
        public static bool Wear(string id)
        {
            if (!string.IsNullOrEmpty(id) && !IsHeld(id)) return false;

            if (!DevicePrefs.WriteString(Key, id ?? string.Empty)) return false;

            Changed?.Invoke();
            return true;
        }
    }
}
