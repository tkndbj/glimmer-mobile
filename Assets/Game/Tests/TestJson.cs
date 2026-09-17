using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A JSON reader and a repo-root finder that work <em>outside</em> the Editor.
    ///
    /// <para>
    /// <b>This exists for one reason: invariant 29e.</b> "A vector file that only the Editor can
    /// read is not a guard on the rule it pins." Every shared-vector fixture in this suite reaches
    /// the file through <c>Application.dataPath</c> and <c>JsonUtility.FromJson</c>, and both are
    /// engine <c>ECall</c>s with no implementation outside a player or the Editor — so
    /// <c>Tools/verify/tests.py</c> reports them as "needs the Editor" and walks past. That has
    /// already cost this project twice, on the one guard that stops the client and the server
    /// paying different amounts: the fixtures went red when a block was added to the reader and
    /// nobody saw it until somebody happened to open Unity (<c>RewardVectorTests</c> carries the
    /// account).
    /// </para>
    /// <para>
    /// <b>Nothing here ships and nothing here is clever.</b> It is a straight recursive-descent
    /// reader over the subset the vector files use — objects, arrays, strings, numbers, booleans
    /// and null — written out rather than pulled in because the test assembly's reference set is
    /// Unity's, which carries no JSON library of its own. It is deliberately <em>strict</em>: a
    /// malformed file throws rather than answering an empty object, because a reader that returns
    /// nothing on a file it did not understand turns every vector case into a silent pass.
    /// </para>
    /// </summary>
    public static class TestJson
    {
        // ------------------------------------------------------------ finding the repo
        /// <summary>
        /// The repository root, found without asking the engine where anything is.
        ///
        /// <para>
        /// Walks up from the working directory and then from this assembly's own location,
        /// looking for the marker below. The Editor runs with the project root as the working
        /// directory and the offline runner does not, so both starting points are needed; the
        /// marker is a file rather than a folder name so a checkout renamed by a clone still
        /// answers (the repo folder is called <c>GlimmerGrove</c> in some places and
        /// <c>glimmergroove</c> in others, which is exactly the sort of thing that would make a
        /// name check fail on one machine only).
        /// </para>
        /// </summary>
        public static string RepoRoot()
        {
            const string marker = "Assets/StreamingAssets/Content/manifest.json";

            foreach (string start in Starts())
            {
                var dir = new DirectoryInfo(start);

                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, marker.Replace('/', Path.DirectorySeparatorChar))))
                        return dir.FullName;

                    dir = dir.Parent;
                }
            }

            throw new FileNotFoundException(
                "could not find the repository root by walking up from either the working " +
                "directory or the test assembly; looked for " + marker);
        }

        static IEnumerable<string> Starts()
        {
            yield return Directory.GetCurrentDirectory();

            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(here)) yield return here;

            string asm = Path.GetDirectoryName(typeof(TestJson).Assembly.Location);
            if (!string.IsNullOrEmpty(asm)) yield return asm;
        }

        /// <summary>A file under <c>firebase/shared</c>, which is where both halves read vectors from.</summary>
        public static string SharedPath(string file)
            => Path.Combine(RepoRoot(), "firebase", "shared", file);

        /// <summary>Reads and parses a shared vector file.</summary>
        public static Dictionary<string, object> ReadShared(string file)
        {
            string path = SharedPath(file);
            if (!File.Exists(path)) throw new FileNotFoundException("shared vectors not found", path);

            return Object(Parse(File.ReadAllText(path)));
        }

        // ------------------------------------------------------------------ reading
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            int at = 0;
            object value = ReadValue(json, ref at);

            SkipSpace(json, ref at);
            if (at != json.Length)
                throw new FormatException($"trailing content at {at}");

            return value;
        }

        static object ReadValue(string s, ref int at)
        {
            SkipSpace(s, ref at);
            if (at >= s.Length) throw new FormatException("ended early");

            switch (s[at])
            {
                case '{': return ReadObject(s, ref at);
                case '[': return ReadArray(s, ref at);
                case '"': return ReadString(s, ref at);
                case 't': Expect(s, ref at, "true"); return true;
                case 'f': Expect(s, ref at, "false"); return false;
                case 'n': Expect(s, ref at, "null"); return null;
                default: return ReadNumber(s, ref at);
            }
        }

        static Dictionary<string, object> ReadObject(string s, ref int at)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            at++;                                                  // '{'

            SkipSpace(s, ref at);
            if (at < s.Length && s[at] == '}') { at++; return map; }

            while (true)
            {
                SkipSpace(s, ref at);
                string key = ReadString(s, ref at);

                SkipSpace(s, ref at);
                if (at >= s.Length || s[at] != ':') throw new FormatException($"expected ':' at {at}");
                at++;

                map[key] = ReadValue(s, ref at);

                SkipSpace(s, ref at);
                if (at >= s.Length) throw new FormatException("object ended early");

                if (s[at] == ',') { at++; continue; }
                if (s[at] == '}') { at++; return map; }

                throw new FormatException($"expected ',' or '}}' at {at}");
            }
        }

        static List<object> ReadArray(string s, ref int at)
        {
            var list = new List<object>();
            at++;                                                  // '['

            SkipSpace(s, ref at);
            if (at < s.Length && s[at] == ']') { at++; return list; }

            while (true)
            {
                list.Add(ReadValue(s, ref at));

                SkipSpace(s, ref at);
                if (at >= s.Length) throw new FormatException("array ended early");

                if (s[at] == ',') { at++; continue; }
                if (s[at] == ']') { at++; return list; }

                throw new FormatException($"expected ',' or ']' at {at}");
            }
        }

        static string ReadString(string s, ref int at)
        {
            if (at >= s.Length || s[at] != '"') throw new FormatException($"expected a string at {at}");
            at++;

            var built = new StringBuilder();

            while (at < s.Length)
            {
                char c = s[at++];

                if (c == '"') return built.ToString();

                if (c != '\\') { built.Append(c); continue; }

                if (at >= s.Length) break;
                char escape = s[at++];

                switch (escape)
                {
                    case '"': built.Append('"'); break;
                    case '\\': built.Append('\\'); break;
                    case '/': built.Append('/'); break;
                    case 'b': built.Append('\b'); break;
                    case 'f': built.Append('\f'); break;
                    case 'n': built.Append('\n'); break;
                    case 'r': built.Append('\r'); break;
                    case 't': built.Append('\t'); break;

                    case 'u':
                        if (at + 4 > s.Length) throw new FormatException("truncated \\u escape");
                        built.Append((char)ushort.Parse(s.Substring(at, 4), NumberStyles.HexNumber,
                                                        CultureInfo.InvariantCulture));
                        at += 4;
                        break;

                    default: throw new FormatException($"unknown escape \\{escape} at {at}");
                }
            }

            throw new FormatException("string ended early");
        }

        static double ReadNumber(string s, ref int at)
        {
            int start = at;

            if (at < s.Length && (s[at] == '-' || s[at] == '+')) at++;

            while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.' ||
                                     s[at] == 'e' || s[at] == 'E' || s[at] == '-' || s[at] == '+'))
                at++;

            string raw = s.Substring(start, at - start);
            if (raw.Length == 0) throw new FormatException($"expected a number at {start}");

            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Expect(string s, ref int at, string word)
        {
            if (at + word.Length > s.Length || string.CompareOrdinal(s, at, word, 0, word.Length) != 0)
                throw new FormatException($"expected '{word}' at {at}");

            at += word.Length;
        }

        static void SkipSpace(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
        }

        // ------------------------------------------------------------------ accessors
        //
        // Strict on purpose. A missing key is a vector file that has drifted away from the
        // fixture reading it, and answering a default there is how a case would quietly stop
        // testing anything.

        public static Dictionary<string, object> Object(object value)
            => value as Dictionary<string, object>
               ?? throw new FormatException("expected an object, got " + Describe(value));

        public static List<object> Array(object value)
            => value as List<object>
               ?? throw new FormatException("expected an array, got " + Describe(value));

        public static Dictionary<string, object> Child(Dictionary<string, object> map, string key)
            => Object(Get(map, key));

        public static List<object> Children(Dictionary<string, object> map, string key)
            => Array(Get(map, key));

        public static string Str(Dictionary<string, object> map, string key, string fallback = null)
        {
            if (!map.TryGetValue(key, out object value) || value == null) return fallback;
            return value as string ?? throw new FormatException($"'{key}' is not a string");
        }

        /// <summary>
        /// A whole number. <b>Absent means <paramref name="fallback"/></b>, because a vector row
        /// leaves a field out to say "nought here", and a fractional value throws — every number
        /// these files carry is a count, and a rounded one would be an arithmetic difference the
        /// fixture silently absorbed.
        /// </summary>
        public static long Long(Dictionary<string, object> map, string key, long fallback = 0L)
        {
            if (!map.TryGetValue(key, out object value) || value == null) return fallback;

            if (!(value is double number))
                throw new FormatException($"'{key}' is not a number, it is " + Describe(value));

            if (number != Math.Floor(number))
                throw new FormatException($"'{key}' is {number}, which is not a whole number");

            return (long)number;
        }

        public static int Int(Dictionary<string, object> map, string key, int fallback = 0)
        {
            long value = Long(map, key, fallback);

            if (value < int.MinValue || value > int.MaxValue)
                throw new FormatException($"'{key}' is {value}, which does not fit an int");

            return (int)value;
        }

        static object Get(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out object value)
                ? value
                : throw new FormatException($"the vector file has no '{key}'");

        static string Describe(object value)
            => value == null ? "null" : value.GetType().Name;
    }
}
