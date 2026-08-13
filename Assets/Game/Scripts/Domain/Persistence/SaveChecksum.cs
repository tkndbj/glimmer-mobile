using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Detects a save file that was damaged rather than merely old.
    ///
    /// This guards against truncation, not tampering. A file cut short by a process
    /// kill can still be valid JSON — the tail is simply missing — and would then
    /// load as a plausible-looking save with half the player's levels gone. That
    /// silently-wrong outcome is worse than an obvious failure, so a mismatch sends
    /// the loader to the backup copy instead.
    ///
    /// It is deliberately not cryptographic. Anyone with the device can edit their
    /// own single-player save, and pretending otherwise would only cost the ability
    /// to help a player who has hit a bug.
    /// </summary>
    public static class SaveChecksum
    {
        const ulong FnvOffset = 14695981039346656037;
        const ulong FnvPrime = 1099511628211;

        /// <summary>Hashes the file as it would be written with no checksum in it.</summary>
        public static string Compute(SaveFileDto dto)
        {
            if (dto == null) return string.Empty;

            string keep = dto.checksum;
            dto.checksum = string.Empty;

            string payload = JsonUtility.ToJson(dto, true);
            dto.checksum = keep;

            return Hash(payload);
        }

        /// <summary>
        /// True when the file is intact, or predates checksums. Absence is treated as
        /// trust on purpose: adding this must not invalidate saves already on devices.
        /// </summary>
        public static bool Verify(SaveFileDto dto)
        {
            if (dto == null) return false;
            if (string.IsNullOrEmpty(dto.checksum)) return true;

            return string.Equals(dto.checksum, Compute(dto), System.StringComparison.Ordinal);
        }

        public static string Hash(string text)
        {
            if (text == null) return string.Empty;

            ulong hash = FnvOffset;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }
            return hash.ToString("x16");
        }
    }
}
