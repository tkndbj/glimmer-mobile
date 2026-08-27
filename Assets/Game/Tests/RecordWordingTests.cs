using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every mode's record line can be written with the one number both callers pass.
    ///
    /// <para>
    /// This is the check that was missing when the clock went (invariant 22). A record used to
    /// read "31 turns · 2:14", so both stems carried a second placeholder;
    /// <c>RunWording.RecordKey</c> dropped the timed forms and the table kept the timed text.
    /// After that every record line in the game — the mark above a cleared node on the map and
    /// the victory panel's own run — printed the literal "{0} turns · {1}". Nothing existing
    /// could see it: <c>Loc.Format</c> catches the <see cref="FormatException"/> a missing
    /// argument raises and hands the pattern back, which is the right behaviour on a player's
    /// screen and is also what made this silent; the keys themselves all resolve, so invariant
    /// 6's gate passed; and a placeholder nobody fills is not a compile error.
    /// </para>
    /// <para>
    /// It reads the shipped table rather than a fixture, because the fault was in the table, and
    /// it walks <see cref="LevelModes.All"/> rather than naming stems, so a fifth mode is covered
    /// by existing here. <c>Tools/verify/loc.py</c> holds the general half — that a literal
    /// <c>Loc.Format</c> call site passes as many arguments as its string asks for — which this
    /// call site escapes, since its key is computed from the level's mode.
    /// </para>
    /// <para>
    /// The table is found by walking up from this assembly rather than from
    /// <c>Application.dataPath</c>, and read by the small parser below rather than by
    /// <c>JsonUtility</c>, for one reason: both of those are native calls, and a test that makes
    /// one runs only with the Editor open. A guard for a fault that ships silently has to run on
    /// every offline pass, not on the ones where somebody happened to open Unity.
    /// </para>
    /// </summary>
    public sealed class RecordWordingTests
    {
        static readonly string[] TableParts =
            { "Assets", "StreamingAssets", "Content", "loc", "en.json" };

        /// <summary>
        /// The shipped English table, found from wherever this is being run.
        ///
        /// Two starting points because the two runners differ: the Editor's working directory is
        /// the project root, and the offline runner's is wherever the tool was invoked from. The
        /// assembly's own folder is what covers the second when it is neither.
        /// </summary>
        static string TablePath()
        {
            var seeds = new List<string> { Directory.GetCurrentDirectory() };

            string here = Path.GetDirectoryName(typeof(RecordWordingTests).Assembly.Location);
            if (!string.IsNullOrEmpty(here)) seeds.Add(here);

            foreach (string seed in seeds)
                for (var dir = new DirectoryInfo(seed); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, Path.Combine(TableParts));
                    if (File.Exists(candidate)) return candidate;
                }

            Assert.Fail("the shipped string table could not be found from " + string.Join(", ", seeds));
            return null;
        }

        static readonly Regex Entry = new Regex(
            @"\{\s*""key""\s*:\s*""(?<key>[^""]*)""\s*,\s*""text""\s*:\s*""(?<text>(?:[^""\\]|\\.)*)""",
            RegexOptions.Singleline);

        /// <summary>
        /// The entries, by key. A deliberately small reader: it is looking at placeholders, and
        /// the only escapes that could hide one are the ones decoded here.
        /// </summary>
        static Dictionary<string, string> Table()
        {
            string path = TablePath();
            var table = new Dictionary<string, string>();

            foreach (Match m in Entry.Matches(File.ReadAllText(path)))
                table[m.Groups["key"].Value] = Unescape(m.Groups["text"].Value);

            Assert.Greater(table.Count, 0, "no strings were read from " + path);
            return table;
        }

        static string Unescape(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\' || i + 1 >= text.Length) { sb.Append(text[i]); continue; }

                char c = text[++i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(text.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// The count reaches the line, and nothing else is left asking for an argument.
        ///
        /// Both halves matter and only the second one failed here: a pattern wanting a second
        /// argument does not lose the second half, it loses the whole line.
        /// </summary>
        static void CheckWritable(Dictionary<string, string> table, string key, int moves)
        {
            Assert.IsTrue(table.TryGetValue(key, out string text), $"no string for '{key}'");

            string written;
            try
            {
                written = string.Format(text, moves);
            }
            catch (FormatException e)
            {
                Assert.Fail($"'{key}' cannot be written with one number (\"{text}\"): {e.Message}");
                return;
            }

            StringAssert.Contains(moves.ToString(), written,
                                  $"'{key}' drops the number it is supposed to report (\"{text}\")");
            Assert.IsFalse(written.Contains("{"),
                           $"'{key}' still wants an argument nobody passes (\"{text}\")");
        }

        [Test]
        public void EveryModeWordsARecordWithTheOneNumberItIsGiven()
        {
            var table = Table();

            foreach (var mode in LevelModes.All)
            {
                Assert.IsNotEmpty(mode.RecordStem, $"{mode.Mode} names no record wording");

                CheckWritable(table, mode.RecordStem, 31);
                CheckWritable(table, mode.RecordStem + "_one", 1);
            }
        }

        /// <summary>
        /// The fallback stem is a real key too. <c>RunWording.RecordKey</c> falls back to it for a
        /// level whose chapter has been disabled underneath the player, which is a path no mode in
        /// the registry covers.
        /// </summary>
        [Test]
        public void TheFallbackRecordWordingIsWritableToo()
        {
            var table = Table();

            CheckWritable(table, "ui.rank.record", 31);
            CheckWritable(table, "ui.rank.record_one", 1);
        }
    }
}
