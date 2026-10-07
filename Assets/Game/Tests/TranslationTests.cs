using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GlimmerGrove.Localization;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every translation is a whole copy of English, and the picker offers exactly the tables
    /// that ship.
    ///
    /// <para>
    /// <b>Why a fixture as well as <c>Tools/verify/loc.py</c>.</b> <c>Loc</c> falls back to
    /// English key by key and never says so, which is right on a player's screen and invisible
    /// everywhere else: a table missing a key draws that line in English, and a translation that
    /// drops a <c>{1}</c> prints a sentence with no number in it. <c>loc.py</c> is the offline
    /// gate; this is the same promise held where the Editor's Test Runner looks, plus the two
    /// things only C# can see - the <see cref="Loc.Languages"/> list and <see cref="Loc.Upper(string, string)"/>.
    /// </para>
    /// </summary>
    public class TranslationTests
    {
        static readonly Regex Slot = new Regex(@"\{(\d+)[^}]*\}");
        static readonly Regex Tag = new Regex(@"</?[a-z]+(?:=[^>]*)?>");

        static IEnumerable<string> Translations()
            => ShippedStrings.Languages().Where(code => code != Loc.FallbackLanguage);

        [Test]
        public void ThePickerOffersExactlyTheTablesThatShip()
        {
            var offered = Loc.Languages.Select(l => l.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();

            CollectionAssert.AreEqual(ShippedStrings.Languages(), offered,
                "Loc.Languages and the files under loc/ must name the same languages");
        }

        /// <summary>
        /// The chooser grows a row per language, and a centred panel taller than
        /// <see cref="Layout.PanelStack.TallestPanel"/> draws its ribbon off the top of a tablet -
        /// so the seventh language that does not fit fails here, not on a device.
        /// </summary>
        [Test]
        public void TheLanguageChooserFitsTheShortestCanvas()
        {
            float height = LanguageOverlay.TopRoom + Loc.Languages.Length * LanguageOverlay.RowPitch
                         + LanguageOverlay.FootRoom;

            Assert.LessOrEqual(height, Layout.PanelStack.TallestPanel,
                               $"the language chooser is {height} against a ceiling of {Layout.PanelStack.TallestPanel}");
        }

        /// <summary>
        /// The sentence iOS draws inside its tracking prompt, in every shipped language.
        ///
        /// <para>
        /// The iOS build writes it into <c>Info.plist</c> and one <c>InfoPlist.strings</c> per
        /// language (<c>IosPrivacyPlist</c>), and fails on a Mac if one is missing - this is the
        /// same promise held where it is cheap. It must name the game (Apple reads it as the
        /// app explaining itself) and carry none of the three characters the <c>.strings</c>
        /// format has to escape, so the file written is the sentence written here.
        /// </para>
        /// </summary>
        [Test]
        public void TheTrackingPromptSaysWhyInEveryLanguage()
        {
            const string key = "ui.privacy.tracking_usage";

            foreach (var language in Loc.Languages)
            {
                var table = ShippedStrings.Table(language.Code);
                Assert.IsTrue(table.TryGetValue(key, out string sentence), $"{language.Code}.json has no {key}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(sentence), $"{language.Code}: {key} is empty");
                StringAssert.Contains("Gemfire", sentence, $"{language.Code}: the tracking sentence must name the game");
                Assert.IsFalse(sentence.IndexOfAny(new[] { '"', '\\', '\n' }) >= 0,
                               $"{language.Code}: the tracking sentence carries a character .strings must escape");
                Assert.LessOrEqual(sentence.Length, 200, $"{language.Code}: the tracking sentence is too long for the prompt");
            }
        }

        [Test]
        public void EveryLanguageIsNamedInTheEnglishTable()
        {
            var english = ShippedStrings.Table();

            foreach (var language in Loc.Languages)
                Assert.IsTrue(english.ContainsKey(language.NameKey),
                              $"{language.Code} has no name: {language.NameKey} is not in en.json");
        }

        [Test]
        public void EveryTranslationHoldsEveryEnglishKeyAndNoOther()
        {
            var english = ShippedStrings.Table();

            foreach (string code in Translations())
            {
                var table = ShippedStrings.Table(code);

                var missing = english.Keys.Where(k => !table.ContainsKey(k)).ToList();
                var extra = table.Keys.Where(k => !english.ContainsKey(k)).ToList();

                Assert.IsEmpty(missing, $"{code}.json is missing {missing.Count} key(s): {string.Join(", ", missing.Take(10))}");
                Assert.IsEmpty(extra, $"{code}.json has {extra.Count} key(s) English does not: {string.Join(", ", extra.Take(10))}");
            }
        }

        [Test]
        public void EveryTranslationCarriesEnglishsSlotsTagsAndLineBreaks()
        {
            var english = ShippedStrings.Table();
            var wrong = new List<string>();

            foreach (string code in Translations())
                foreach (var pair in ShippedStrings.Table(code))
                {
                    if (!english.TryGetValue(pair.Key, out string source)) continue;
                    if (Shape(pair.Value) != Shape(source)) wrong.Add($"{code}: {pair.Key}");
                }

            Assert.IsEmpty(wrong, "these strings do not carry what English carries:\n" + string.Join("\n", wrong));
        }

        static string Shape(string text)
        {
            var slots = Slot.Matches(text).Cast<Match>().Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal);
            var tags = Tag.Matches(text).Cast<Match>().Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal);
            int breaks = text.Count(c => c == '\n');
            return string.Join("|", slots) + "#" + string.Join("|", tags) + "#" + breaks;
        }

        [Test]
        public void TurkishCapitalsKeepTheDotOnTheirI()
        {
            Assert.AreEqual("GİRİŞ", Loc.Upper("giriş", "tr"));
            Assert.AreEqual("ILIK", Loc.Upper("ılık", "tr"));
            Assert.AreEqual("İLK SEVİYE", Loc.Upper("ilk seviye", "tr"));
        }

        [Test]
        public void EveryOtherLanguageCapitalisesAsBefore()
        {
            foreach (var language in Loc.Languages.Where(l => l.Code != "tr"))
            {
                Assert.AreEqual("GIRIŞ", Loc.Upper("giriş", language.Code), language.Code);
                Assert.AreEqual("ÉTÉ", Loc.Upper("été", language.Code), language.Code);
            }
        }

        [Test]
        public void CapitalisingNothingIsNothing()
        {
            Assert.AreEqual(string.Empty, Loc.Upper(null, "tr"));
            Assert.AreEqual(string.Empty, Loc.Upper(string.Empty, "en"));
        }
    }
}
