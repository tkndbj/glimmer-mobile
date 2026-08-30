using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Sound, haptics and language.
    ///
    /// Split out of progress because they answer a different question and have a
    /// different lifetime — a player who erases their progress keeps their sound
    /// settings. They share a file with progress only so there is one atomic write.
    /// </summary>
    public static class GameSettings
    {
        public static bool MusicOn { get; private set; } = true;
        public static bool SfxOn { get; private set; } = true;

        /// <summary>
        /// <b>Retired in place.</b> The game no longer vibrates at all — <c>Haptic</c> is gone,
        /// with it every call site, and with those the control that used to switch this — so
        /// nothing reads this and nothing ever should again.
        ///
        /// <para>
        /// It is kept for <c>SaveFileDto.bestMillis</c>'s reason and it is the same reason:
        /// <c>settings</c> travels in the save file, <c>firestore.rules</c> gates that document
        /// with a <c>hasOnly</c> allow-list, and a client that writes a key the rules do not
        /// list loses <em>every</em> save write rather than that one key (invariant 12a). So the
        /// field stays on the wire until a schema version in which no shipped client writes one,
        /// and this property is what keeps writing it.
        /// </para>
        /// <para>
        /// <b>Why the buzz went.</b> <c>Handheld.Vibrate</c> on Android is one fixed-length
        /// heavy pulse with no way to make a second lighter than the first, so every use of it
        /// here was the same blunt knock whatever it was answering — and on a mode that opens
        /// four cocoons in one chain it fired four times inside a second, which is one rumble
        /// rather than four taps.
        /// </para>
        /// </summary>
        public static bool HapticsOn { get; private set; } = true;

        /// <summary>Empty means follow the device language.</summary>
        public static string Language { get; private set; } = string.Empty;

        /// <summary>
        /// Whether this keeper's grove appears on the public boards.
        ///
        /// <para>
        /// The one setting here that is about other people rather than about this device, and
        /// the only one whose "off" has to reach a server to mean anything — turning it off
        /// raises a withdrawal, which takes the published card down rather than merely
        /// stopping the next rebuild. See <c>GroveBoard</c> and <see cref="SettingsDto.board"/>.
        /// </para>
        /// </summary>
        public static bool BoardOptIn { get; private set; } = true;

        /// <summary>
        /// Raised after any setting changes. The audio player subscribes to this
        /// rather than being called directly, which is what keeps settings — a piece
        /// of saved state — from having to know that a sound system exists.
        /// </summary>
        public static event Action Changed;

        public static void SetMusic(bool on)
        {
            if (MusicOn == on) return;
            MusicOn = on;
            Commit();
        }

        public static void SetSfx(bool on)
        {
            if (SfxOn == on) return;
            SfxOn = on;
            Commit();
        }

        /// <summary>Retired with <see cref="HapticsOn"/>. Nothing calls this.</summary>
        public static void SetHaptics(bool on)
        {
            if (HapticsOn == on) return;
            HapticsOn = on;
            Commit();
        }

        /// <summary>
        /// Joins or leaves the public boards.
        ///
        /// Raised through <see cref="Changed"/> like every other setting, so the board service
        /// hears about it by subscribing once rather than by the profile panel remembering to
        /// call it — the wiring lesson this project has now paid for three times.
        /// </summary>
        public static void SetBoardOptIn(bool on)
        {
            if (BoardOptIn == on) return;
            BoardOptIn = on;
            Commit();
        }

        public static void SetLanguage(string languageCode)
        {
            languageCode ??= string.Empty;
            if (Language == languageCode) return;
            Language = languageCode;
            Commit();
        }

        static void Commit()
        {
            SaveService.Save();
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var s = dto.settings ?? new SettingsDto();
            MusicOn = s.music.Resolve(true);
            SfxOn = s.sfx.Resolve(true);
            HapticsOn = s.haptics.Resolve(true);
            BoardOptIn = s.board.Resolve(true);
            Language = s.language ?? string.Empty;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.settings = new SettingsDto
            {
                music = StoredFlag.From(MusicOn),
                sfx = StoredFlag.From(SfxOn),
                haptics = StoredFlag.From(HapticsOn),
                board = StoredFlag.From(BoardOptIn),
                language = Language,
            };
        }
    }
}
