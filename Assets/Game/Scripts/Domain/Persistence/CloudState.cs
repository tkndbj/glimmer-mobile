using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Who this save belongs to, and how far it has got to the server.
    ///
    /// Kept in the save file rather than beside it so that the identity travels with
    /// the data it identifies. A save that has been merged from two devices has to be
    /// able to say which account it is, and a backup restored onto a new handset has
    /// to arrive already knowing.
    /// </summary>
    public static class CloudState
    {
        /// <summary>The authenticated account. Empty until the player signs in.</summary>
        public static string UserId { get; private set; } = string.Empty;

        /// <summary>
        /// Bumped on every local write. Lets a backend order two snapshots without
        /// trusting either device's clock, which on mobile is user-settable and
        /// therefore not evidence of anything.
        /// </summary>
        public static long Revision { get; private set; }

        public static long LastSyncedUnix { get; private set; }

        /// <summary>
        /// Stable per-installation id. Identifies which device wrote a snapshot when
        /// a support case needs untangling; it is not an identity and grants nothing.
        /// </summary>
        public static string DeviceId { get; private set; } = string.Empty;

        public static bool IsSignedIn => !string.IsNullOrEmpty(UserId);

        public static void SignIn(string userId)
        {
            if (string.IsNullOrEmpty(userId) || userId == UserId) return;
            UserId = userId;
            SaveService.Save();
        }

        public static void SignOut()
        {
            if (!IsSignedIn) return;
            UserId = string.Empty;
            SaveService.Save();
        }

        public static void MarkSynced(long unix)
        {
            LastSyncedUnix = unix;
            SaveService.MarkDirty();
        }

        internal static void NextRevision() => Revision++;

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var c = dto.cloud;

            UserId = c?.userId ?? string.Empty;
            Revision = c == null || c.revision < 0 ? 0L : c.revision;
            LastSyncedUnix = c == null || c.lastSyncedUnix < 0 ? 0L : c.lastSyncedUnix;
            DeviceId = string.IsNullOrEmpty(c?.deviceId) ? EnsureDeviceId() : c.deviceId;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.cloud = new CloudStateDto
            {
                userId = UserId,
                revision = Revision,
                lastSyncedUnix = LastSyncedUnix,
                deviceId = string.IsNullOrEmpty(DeviceId) ? EnsureDeviceId() : DeviceId,
            };
        }

        /// <summary>
        /// A fresh id rather than <c>SystemInfo.deviceUniqueIdentifier</c>. That value is
        /// a hardware identifier on some platforms and a privacy problem on all of
        /// them; nothing here needs to survive a reinstall, so a random one is both
        /// sufficient and the version an app review will not ask about.
        /// </summary>
        static string EnsureDeviceId()
        {
            if (!string.IsNullOrEmpty(DeviceId)) return DeviceId;
            DeviceId = Guid.NewGuid().ToString("N").Substring(0, 16);
            return DeviceId;
        }

        /// <summary>Clears the in-memory identity. Tests use this to stay independent.</summary>
        internal static void Reset()
        {
            UserId = string.Empty;
            Revision = 0;
            LastSyncedUnix = 0;
            DeviceId = string.Empty;
        }
    }
}
