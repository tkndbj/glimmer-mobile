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
    /// rule holds from the first level - content stores keys, never sentences, and
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
        /// Capitals as the active language writes them.
        ///
        /// <para>
        /// <c>ToUpperInvariant</c> is right for every language this game ships except Turkish,
        /// which has two letters i: dotted <c>i</c> capitalises to <c>İ</c> and dotless <c>ı</c>
        /// to <c>I</c>. The invariant rule turns both into <c>I</c>, so "giriş" would read
        /// "GIRIŞ" - a different and wrong word on every banner. The two letters are mapped by
        /// hand rather than through <c>CultureInfo("tr-TR")</c> because what a player's runtime
        /// carries of the culture tables is the platform's business, and this has to be the
        /// same answer on every device. Every caption the UI capitalises goes through
        /// <see cref="LocCase.Upper"/>, and <c>compile.py</c> refuses a bare
        /// <c>ToUpperInvariant()</c> in Presentation.
        /// </para>
        /// </summary>
        public static string Upper(string text) => Upper(text, Language);

        /// <summary><see cref="Upper(string)"/> for a named language - the pure half, so it can be tested.</summary>
        public static string Upper(string text, string language)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (language == TurkishLanguage) text = text.Replace('i', 'İ').Replace('ı', 'I');
            return text.ToUpperInvariant();
        }

        const string TurkishLanguage = "tr";

        /// <summary>
        /// One language the game ships a table for, and the key of its name as its own speakers
        /// write it - "Deutsch", never "German", because the list is read by somebody who may not
        /// read the language it is currently drawn in.
        /// </summary>
        public readonly struct Shipped
        {
            public readonly string Code, NameKey;
            public Shipped(string code, string nameKey) { Code = code; NameKey = nameKey; }
        }

        /// <summary>
        /// Every language with a table under <c>loc/</c>, in the order the picker lists them.
        /// <c>TranslationTests</c> holds this list and the folder to each other, so a table
        /// nobody can choose, or a choice with no table, fails a fixture rather than a player.
        /// </summary>
        public static readonly Shipped[] Languages =
        {
            new Shipped("en", "ui.language.en"),
            new Shipped("es", "ui.language.es"),
            new Shipped("pt", "ui.language.pt"),
            new Shipped("fr", "ui.language.fr"),
            new Shipped("de", "ui.language.de"),
            new Shipped("it", "ui.language.it"),
            new Shipped("tr", "ui.language.tr"),
            new Shipped("pl", "ui.language.pl"),
        };

        /// <summary>The key naming <paramref name="code"/> in its own language, or null for one not shipped.</summary>
        public static string NameKeyOf(string code)
        {
            foreach (var l in Languages) if (l.Code == code) return l.NameKey;
            return null;
        }

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

    /// <summary>
    /// <see cref="Loc.Upper"/> as a suffix, so a caption reads <c>Loc.Get(key).Upper()</c>
    /// wherever it used to read <c>.ToUpperInvariant()</c>.
    /// </summary>
    public static class LocCase
    {
        public static string Upper(this string text) => Loc.Upper(text);
    }
}
