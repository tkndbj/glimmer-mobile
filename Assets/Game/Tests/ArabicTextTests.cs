using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GlimmerGrove.Localization;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Arabic reaches the screen shaped, in visual order, and in a face that can draw it.
    ///
    /// <para>
    /// The rules are <see cref="ArabicText"/>'s; the answers are <c>Tools/verify/arabic-vectors.json</c>,
    /// written by hand and also run by <c>Tools/arabic_text.py --check</c>, so the C# shaper and the
    /// mirror the proof renders use cannot drift apart. The rest is what only C# or the importer
    /// settings can see: rich-text tags, the untouched path for every other language, and the
    /// fallback that puts the Arabic face behind <c>GameFont</c>.
    /// </para>
    /// </summary>
    public class ArabicTextTests
    {
        /// <summary>
        /// The repository root, found the way <see cref="ShippedStrings"/> finds the tables - never
        /// through <c>Application.dataPath</c>, which is a native call and would make these
        /// fixtures Editor-only (a vector file only the Editor can read is not a guard).
        /// </summary>
        static string Repo => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ShippedStrings.Path()),
                                                            "..", "..", "..", ".."));

        // One case of the vector file, read by pattern rather than by JsonUtility (native, and it
        // truncates a string at an escape - which is why the file carries code points at all).
        static readonly Regex Case = new Regex(
            "\"name\": \"(?<name>[^\"]*)\",\\s*\"rule\": \"(?<rule>\\w+)\".*?" +
            "\"textCodes\": \\[(?<text>[\\d,\\s]*)\\].*?\"expectedCodes\": \\[(?<expected>[\\d,\\s]*)\\]",
            RegexOptions.Singleline);

        static string FromCodes(string list)
        {
            var sb = new StringBuilder();
            foreach (string n in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                sb.Append((char)int.Parse(n.Trim()));
            return sb.ToString();
        }

        static string Codes(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4")));

        [Test]
        public void EveryVectorShapesAndOrdersAsWritten()
        {
            string path = Path.Combine(Repo, "Tools", "verify", "arabic-vectors.json");
            Assert.IsTrue(File.Exists(path), $"arabic vectors not found at {path}");

            string json = File.ReadAllText(path);
            var cases = Case.Matches(json).Cast<Match>().ToList();
            Assert.Greater(cases.Count, 0, "the vector file holds no case this could read");
            Assert.AreEqual(Regex.Matches(json, "\"name\":").Count, cases.Count,
                            "a case in the vector file was not read, so it would never be checked");

            foreach (var c in cases)
            {
                string text = FromCodes(c.Groups["text"].Value);
                string got = c.Groups["rule"].Value == "shape" ? ArabicText.Shape(text) : ArabicText.Visual(text);
                Assert.AreEqual(Codes(FromCodes(c.Groups["expected"].Value)), Codes(got), c.Groups["name"].Value);
            }
        }

        [Test]
        public void AStringWithNoArabicIsTheSameObject()
        {
            // Every other language goes through the stock Text path untouched.
            const string english = "Level 5 (3/5) +20% <b>bold</b>";
            Assert.IsFalse(ArabicText.Needs(english));
            Assert.AreSame(english, ArabicText.Visual(english, true));
            Assert.AreSame(english, ArabicText.Shape(english));
            Assert.IsFalse(ArabicText.Needs(null));
            Assert.AreEqual(string.Empty, ArabicText.Visual(null));
        }

        [Test]
        public void ABoldArabicWordStaysBoldAndOpensOnTheLeft()
        {
            // "<b>بب</b> ب": the tag pair must still read open-then-close after the line is reversed.
            string visual = ArabicText.Visual("<b>بب</b> ب", richText: true);
            Assert.AreEqual("ﺏ <b>ﺐﺑ</b>", visual);
        }

        [Test]
        public void ABoldLatinWordInsideArabicKeepsItsTagsInPlace()
        {
            string visual = ArabicText.Visual("ب <b>XP</b>", richText: true);
            Assert.AreEqual("<b>XP</b> ﺏ", visual);
        }

        [Test]
        public void WithoutRichTextATagIsJustCharacters()
        {
            // A player's name is never rich text (UIKit.Titled): "<b>" after an Arabic letter is three
            // characters, laid to its left with each bracket mirrored, so it still reads "<b>".
            string visual = ArabicText.Visual("ب<b>", richText: false);
            Assert.AreEqual("<b>ﺏ", visual);
        }

        [Test]
        public void EveryShapedFormIsAPresentationForm()
        {
            // What Shape emits for a letter must be a code point the Arabic face was cut to draw:
            // the table is generated from that face, and a form outside these blocks would mean
            // a hand edit.
            foreach (var row in ArabicForms.Letters)
                foreach (int form in row.Value.Where(f => f != 0))
                    Assert.IsTrue((form >= 0xFB50 && form <= 0xFDFF) || (form >= 0xFE70 && form <= 0xFEFF),
                                  $"U+{row.Key:X4} maps to U+{form:X4}, which is not a presentation form");
        }

        [Test]
        public void EveryArabicStringHasNoMarkTheRendererWouldDrawBeside()
        {
            // Shape drops harakat; the table should not rely on that - a translation carrying them
            // reads differently to its author than on screen.
            var table = ShippedStrings.Table("ar");
            var marked = table.Where(p => p.Value.Any(c => (c >= 0x064B && c <= 0x065F) || c == 0x0670))
                              .Select(p => p.Key).ToList();
            Assert.IsEmpty(marked, "ar.json strings carrying harakat: " + string.Join(", ", marked.Take(10)));
        }

        [Test]
        public void TheArabicFaceIsGameFontsFallback()
        {
            // The legacy renderer draws a code point GameFont lacks from its fallback list, and
            // draws nothing at all - no box, no log - when there is none.
            string fonts = Path.Combine(Repo, "Assets", "Game", "Fonts");
            string arabicMeta = File.ReadAllText(Path.Combine(fonts, "GameFontArabic.ttf.meta"));
            string primaryMeta = File.ReadAllText(Path.Combine(fonts, "GameFont.ttf.meta"));

            string guid = Regex.Match(arabicMeta, @"^guid: ([0-9a-f]{32})", RegexOptions.Multiline).Groups[1].Value;
            Assert.IsNotEmpty(guid, "GameFontArabic.ttf.meta has no guid");
            StringAssert.Contains($"- {{fileID: 12800000, guid: {guid}, type: 3}}", primaryMeta,
                                  "GameFont.ttf.meta must name GameFontArabic in fallbackFontReferences");
            StringAssert.Contains("includeFontData: 1", arabicMeta,
                                  "a fallback that does not include its font data draws nothing on a device");
        }
    }
}
