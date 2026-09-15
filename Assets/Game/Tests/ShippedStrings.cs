using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The shipped English string table, read off disk without asking Unity for anything.
    ///
    /// <para>
    /// <b>A reader of its own rather than <c>Loc</c>, and that is not a preference.</b> Nothing
    /// publishes the table in a fixture — it arrives with the content at boot — so a test that
    /// asked <c>Loc.Has</c> would be asking an empty table and failing for a reason that has
    /// nothing to do with what it is checking. And <c>JsonUtility</c> is a native call, so a
    /// fixture that parsed the file the shipping way would be reported as "needs the Editor" and
    /// become the one gate nobody runs on the way past (invariant 29e).
    /// </para>
    /// <para>
    /// <b>One reader, because two would drift.</b> <c>RecordWordingTests</c> wrote the first copy
    /// of this to prove a placeholder was fillable; it is here now, so a second fixture that
    /// wants to know whether a derived key resolves does not grow a third — which is invariant
    /// 44d's rule about a mirror, applied to a test helper.
    /// </para>
    /// <para>
    /// Deliberately a small regex rather than a JSON parser: what it is looking at is a flat list
    /// of key/text pairs, and the only escapes that could hide one are the ones
    /// <see cref="Unescape"/> decodes.
    /// </para>
    /// </summary>
    public static class ShippedStrings
    {
        static readonly string[] TableParts =
            { "Assets", "StreamingAssets", "Content", "loc", "en.json" };

        /// <summary>
        /// The table, found from wherever this is being run.
        ///
        /// Two starting points because the two runners differ: the Editor's working directory is
        /// the project root, and the offline runner's is wherever the tool was invoked from. The
        /// assembly's own folder is what covers the second when it is neither.
        /// </summary>
        public static string Path()
        {
            var seeds = new List<string> { Directory.GetCurrentDirectory() };

            string here = System.IO.Path.GetDirectoryName(
                typeof(ShippedStrings).Assembly.Location);
            if (!string.IsNullOrEmpty(here)) seeds.Add(here);

            foreach (string seed in seeds)
                for (var dir = new DirectoryInfo(seed); dir != null; dir = dir.Parent)
                {
                    string candidate = System.IO.Path.Combine(
                        dir.FullName, System.IO.Path.Combine(TableParts));
                    if (File.Exists(candidate)) return candidate;
                }

            Assert.Fail("the shipped string table could not be found from " + string.Join(", ", seeds));
            return null;
        }

        static readonly Regex Entry = new Regex(
            @"\{\s*""key""\s*:\s*""(?<key>[^""]*)""\s*,\s*""text""\s*:\s*""(?<text>(?:[^""\\]|\\.)*)""",
            RegexOptions.Singleline);

        /// <summary>Every entry, by key.</summary>
        public static Dictionary<string, string> Table()
        {
            string path = Path();
            var table = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match m in Entry.Matches(File.ReadAllText(path)))
                table[m.Groups["key"].Value] = Unescape(m.Groups["text"].Value);

            Assert.Greater(table.Count, 0, "no strings were read from " + path);
            return table;
        }

        /// <summary>The JSON escapes this table actually uses.</summary>
        public static string Unescape(string text)
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
    }
}
