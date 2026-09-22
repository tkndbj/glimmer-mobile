using System;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Frames
{
    /// <summary>
    /// Which frame the keeper wears, and which they hold.
    ///
    /// <para>
    /// <b>The worn frame is in the save, as a recency-joined pair</b> — <c>frameWorn</c> and
    /// <c>frameWornSetUnix</c> (v34) — because it is a thing a stranger sees: it rides onto the
    /// published card and every board row (<c>GroveCard.FrameId</c>), so it has to follow the
    /// account rather than the device. It is the turret loadout's shape (<c>WardLoadout</c>) with
    /// one deliberate difference: <b>an empty value with a stamp is an instruction</b>. Taking a
    /// frame off is something a stranger can see, so "none, as of Tuesday" has to beat "the
    /// dragon, as of Monday" on the other device — where the loadout, which can only ever be
    /// added to, treats empty as no opinion. What still reads as no opinion is the pair no build
    /// has ever written: empty with a stamp of nought (invariant 11c).
    /// </para>
    /// <para>
    /// <b>What is placeholder here, said out loud</b>: every catalogued frame is held. Selling
    /// one makes the held set a union-joined array of permanent ids in <c>SaveFileDto</c>, the
    /// fourth of that shape, and a price on the frames screen; nothing outside this class knows
    /// which of those is true today — every caller asks <see cref="Worn"/> and
    /// <see cref="IsHeld"/>.
    /// </para>
    /// <para>
    /// <b>The raw id is kept, never the resolved frame.</b> A build that cannot draw an id keeps
    /// it and writes it back, so a frame worn on a newer build is not confiscated by an older
    /// one saving over it — <c>tipsSeen</c>'s rule. It draws as nothing meanwhile (7b).
    /// </para>
    /// </summary>
    public static class FrameLedger
    {
        /// <summary>The most an id may be; <c>firestore.rules</c> bounds the field to the same.</summary>
        public const int MaxIdLength = 32;

        /// <summary>Raised when the worn frame changes, by a choice or by a merge landing.</summary>
        public static event Action Changed;

        static string _worn = string.Empty;
        static long _setUnix;

        /// <summary>The worn frame's id as stored, which may name a frame this build cannot draw.</summary>
        public static string WornId => _worn;

        /// <summary>When the choice was made, or nought for a keeper who never has.</summary>
        public static long WornSetUnix => _setUnix;

        /// <summary>The frame the player wears, or null: none, or an id this build cannot resolve.</summary>
        public static FrameDefinition Worn => FrameCatalog.Find(_worn);

        /// <summary>
        /// Whether the player holds this frame. Every catalogued frame, until frames are sold
        /// (see the class note); an unknown id is never held.
        /// </summary>
        public static bool IsHeld(string id) => FrameCatalog.Find(id) != null;

        /// <summary>
        /// Wear this frame, or none. Refused for a frame not held, which is what keeps the gate
        /// a gate the day one exists (15a). Answers whether anything changed, so a screen
        /// redrawing itself stamps no choice nobody made.
        /// </summary>
        public static bool Wear(string id)
        {
            id = Clip(id);
            if (id.Length > 0 && !IsHeld(id)) return false;
            if (string.Equals(id, _worn, StringComparison.Ordinal)) return false;

            _worn = id;
            _setUnix = SaveSchema.NowUnix();

            SaveService.Save();
            Raise();
            return true;
        }

        // ------------------------------------------------------------- the file
        internal static void LoadFrom(SaveFileDto dto)
        {
            _worn = Clip(dto?.frameWorn);
            _setUnix = dto?.frameWornSetUnix ?? 0L;
            if (_setUnix < 0L) _setUnix = 0L;
            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.frameWorn = _worn;
            dto.frameWornSetUnix = _setUnix;
        }

        /// <summary>
        /// Two files' choices, joined by recency against the choice's own stamp. The later
        /// stamp wins whatever it says, so a frame taken off on one device is taken off on the
        /// other; a tie is settled by ordinal so both devices agree; and a pair no build ever
        /// wrote — empty at nought — is no opinion and loses to any dated choice.
        /// </summary>
        public static (string Value, long At) Join(string mine, long mineAt, string other, long otherAt)
        {
            mine = Clip(mine);
            other = Clip(other);
            if (mineAt < 0L) mineAt = 0L;
            if (otherAt < 0L) otherAt = 0L;

            if (mineAt == 0L && otherAt == 0L) return (string.Empty, 0L);
            if (mineAt != otherAt)
                return mineAt > otherAt ? (mine, mineAt) : (other, otherAt);

            return string.CompareOrdinal(mine, other) >= 0 ? (mine, mineAt) : (other, otherAt);
        }

        static string Clip(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            return id.Length > MaxIdLength ? id.Substring(0, MaxIdLength) : id;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
