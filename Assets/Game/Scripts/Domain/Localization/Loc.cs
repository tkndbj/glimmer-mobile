using System;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove.Localization
{
    /// <summary>
    /// Every player-facing string in the game goes through here.
    ///
    /// The cost of retrofitting localisation grows with the content: extracting three
    /// levels' worth of strings is free, extracting three hundred is a week. So the
    /// rule holds from the first level — content stores keys, never sentences, and
    /// this resolves them.
    ///
    /// A missing key falls back to English, then to the key itself. It never returns
    /// null and never throws, because a half-translated language shipping on a Friday
    /// should look imperfect rather than crash.
    /// </summary>
    public static class Loc
    {
        public const string FallbackLanguage = "en";

        static LocTable _active = LocTable.Empty;
        static LocTable _fallback = LocTable.Empty;

        public static string Language { get; private set; } = FallbackLanguage;

        /// <summary>Raised when the active language changes, so screens can rebuild.</summary>
        public static event Action LanguageChanged;

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_active.TryGet(key, out string text)) return text;
            if (_fallback.TryGet(key, out text)) return text;

#if UNITY_EDITOR
            // Loud in the Editor, silent in a shipped build: a missing string should
            // be caught while authoring, not become a runtime log spam on a player's
            // device where it can do nothing but cost battery.
            Debug.LogWarning($"[Loc] missing key '{key}'");
#endif
            return key;
        }

        public static string Get(string key, string fallbackText)
        {
            if (string.IsNullOrEmpty(key)) return fallbackText;
            if (_active.TryGet(key, out string text)) return text;
            if (_fallback.TryGet(key, out text)) return text;
            return fallbackText;
        }

        public static string Format(string key, params object[] args)
        {
            string pattern = Get(key);
            try { return string.Format(pattern, args); }
            catch (FormatException) { return pattern; }
        }

        public static bool Has(string key)
            => _active.TryGet(key, out _) || _fallback.TryGet(key, out _);

        /// <summary>
        /// Loads the fallback table and then the player's language. The fallback is
        /// loaded first and kept, so a failure to fetch the chosen language degrades
        /// to English instead of to raw keys.
        /// </summary>
        public static async Task LoadAsync(IContentSource source, CancellationToken cancellation = default)
        {
            _fallback = await LoadTableAsync(source, FallbackLanguage, cancellation);
            _active = _fallback;
            Language = FallbackLanguage;

            string wanted = ResolveWantedLanguage();
            if (wanted == FallbackLanguage) return;

            var table = await LoadTableAsync(source, wanted, cancellation);
            if (table.Count == 0)
            {
                Debug.Log($"[Loc] no table for '{wanted}', staying on {FallbackLanguage}");
                return;
            }

            _active = table;
            Language = wanted;
            Raise();
        }

        /// <summary>Switches language at runtime, e.g. from a settings screen.</summary>
        public static async Task SetLanguageAsync(IContentSource source, string languageCode,
                                                  CancellationToken cancellation = default)
        {
            GameSettings.SetLanguage(languageCode);

            if (string.IsNullOrEmpty(languageCode) || languageCode == FallbackLanguage)
            {
                _active = _fallback;
                Language = FallbackLanguage;
                Raise();
                return;
            }

            var table = await LoadTableAsync(source, languageCode, cancellation);
            if (table.Count == 0) return;

            _active = table;
            Language = languageCode;
            Raise();
        }

        // ------------------------------------------------------------- internals
        static async Task<LocTable> LoadTableAsync(IContentSource source, string language,
                                                   CancellationToken cancellation)
        {
            var fetch = await source.FetchAsync(ContentPaths.Localisation(language), cancellation);
            if (!fetch.Success) return LocTable.Empty;

            var table = LocTable.Parse(fetch.Text, out string error);
            if (error != null) Debug.LogWarning($"[Loc] {language}: {error}");
            return table;
        }

        /// <summary>The player's explicit choice, otherwise the device language.</summary>
        static string ResolveWantedLanguage()
        {
            if (!string.IsNullOrEmpty(GameSettings.Language)) return GameSettings.Language;
            return SystemLanguageCode(Application.systemLanguage);
        }

        public static string SystemLanguageCode(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.English: return "en";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.ChineseSimplified: return "zh_hans";
                case SystemLanguage.ChineseTraditional: return "zh_hant";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Arabic: return "ar";
                default: return FallbackLanguage;
            }
        }

        static void Raise()
        {
            try { LanguageChanged?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
