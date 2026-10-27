using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// A Shuffle run as it stood when its last hand was owed: everything a resumed run needs
    /// to carry on from there (MODES.md 59s).
    ///
    /// <para>
    /// <b>Taken at a hand</b> (<see cref="ShuffleRun.Owed"/>), because a hand is the one moment
    /// a run is fully described by a handful of facts: the hill is clear or a wave has only just
    /// stepped out, so the board's own state is the line and nothing mid-flight. The run reopens
    /// on the wave after the hand (<see cref="Wave"/>, a <see cref="SiegeStart"/> without ranks),
    /// and the field is dealt fresh. <b>And at the run's first move</b>, with no hand yet, so a
    /// run is kept before its first card: that one reopens on wave one.
    /// </para>
    /// <para>
    /// <b>Cards by their permanent ids, in the order taken</b> (59d), and the build is folded
    /// back from them (59e), so a card added after this was written changes nothing and a card
    /// withdrawn since cannot be resumed (<see cref="ShuffleRun.Resume"/> refuses it). The
    /// run's tuning is the document it opened on (59l), kept whole, so a retune published
    /// while the run was away does not reach it either.
    /// </para>
    /// <para>
    /// <b>A managed codec, not <c>JsonUtility</c></b>: written by hand and read through
    /// <see cref="StrictJson"/>, so a missing or misspelt field is a refusal rather than a
    /// nought, and the whole round trip runs in the offline suite.
    /// </para>
    /// </summary>
    public sealed class ShuffleSnapshot
    {
        /// <summary>The shape this build writes and reads. A snapshot of any other is refused.</summary>
        public const int Version = 1;

        /// <summary>
        /// The longest encoding read back. A tuning document is bounded at a few kilobytes; this
        /// only stops a corrupt preference being parsed whole.
        /// </summary>
        public const int MostLength = 256 * 1024;

        /// <summary>Most refreshes, continues or lives a snapshot may say a run held. Far past any real run.</summary>
        const int MostCount = 10000;

        public LevelId Level { get; }
        public uint Seed { get; }

        /// <summary>The run's tuning document, or empty for the build's own figures (<see cref="ShuffleTuning.Shipped"/>).</summary>
        public string Tuning { get; }

        /// <summary>The wave the resumed run opens on: the one after the last hand.</summary>
        public int Wave { get; }

        /// <summary>Hands dealt, the open one included; nought for a run kept before its first.</summary>
        public int Hands { get; }

        /// <summary>The hand on the table when the run stopped, by id; empty when none was.</summary>
        public IReadOnlyList<string> Open { get; }

        /// <summary>Every card taken, by id, in the order taken.</summary>
        public IReadOnlyList<string> Taken { get; }

        public int Refreshes { get; }

        /// <summary>The build's own stream (<see cref="SiegeBoosts"/>), so the next hand is the one this run was owed.</summary>
        public uint Stream { get; }

        public int SecondWinds { get; }
        public int Phoenixes { get; }

        /// <summary>Continues bought and watched for on this run, so the price climbs on from where it was.</summary>
        public int Continues { get; }
        public int Watched { get; }

        public IReadOnlyList<Post> Posts { get; }

        /// <summary>One post on the line, as <see cref="SiegeBoard"/> seats it back.</summary>
        public readonly struct Post
        {
            /// <summary>The post's colour, checked against the layout so a snapshot never seats the wrong post.</summary>
            public readonly int Colour;
            public readonly bool Standing;
            public readonly int Health;
            public readonly float Fuel;
            public readonly int Charges;
            public readonly int Rank;

            public Post(int colour, bool standing, int health, float fuel, int charges, int rank)
            {
                Colour = colour;
                Standing = standing;
                Health = health;
                Fuel = fuel;
                Charges = charges;
                Rank = rank;
            }
        }

        ShuffleSnapshot(LevelId level, uint seed, string tuning, int wave, int hands,
                        IReadOnlyList<string> open, IReadOnlyList<string> taken, int refreshes,
                        uint stream, int secondWinds, int phoenixes, int continues, int watched,
                        IReadOnlyList<Post> posts)
        {
            Level = level;
            Seed = seed;
            Tuning = tuning ?? string.Empty;
            Wave = wave;
            Hands = hands;
            Open = open;
            Taken = taken;
            Refreshes = refreshes;
            Stream = stream;
            SecondWinds = secondWinds;
            Phoenixes = phoenixes;
            Continues = continues;
            Watched = watched;
            Posts = posts;
        }

        /// <summary>The wave a run that has been dealt <paramref name="hands"/> hands reopens on.</summary>
        public static int WaveAfter(int hands) => hands * ShuffleRun.PicksEvery + 1;

        // ------------------------------------------------------------------ taking one
        /// <summary>
        /// <paramref name="run"/> on <paramref name="board"/> as it stands, or null when there is
        /// nothing a resume could carry on from: a board already lost.
        /// </summary>
        /// <param name="tuning">The document the run's tuning was read from; null or empty for the build's own.</param>
        public static ShuffleSnapshot Of(LevelId level, ShuffleRun run, SiegeBoard board, string tuning,
                                         int continues, int watched)
        {
            if (!level.IsValid || run == null || board == null || board.IsFinished) return null;

            var open = new List<string>();
            if (run.Open != null)
                for (int i = 0; i < run.Open.Count; i++) open.Add(run.Open[i].Id);

            var taken = new List<string>(run.Build.Taken.Count);
            for (int i = 0; i < run.Build.Taken.Count; i++) taken.Add(run.Build.Taken[i].Id);

            var posts = new List<Post>(board.Wards.Count);
            for (int i = 0; i < board.Wards.Count; i++)
            {
                var w = board.Wards[i];
                posts.Add(new Post(w.Colour, w.Alive, w.Health, w.Fuel, w.Charges, w.Rank));
            }

            var boosts = run.Build.Boosts;
            return new ShuffleSnapshot(level, run.Seed, tuning, WaveAfter(run.Hands), run.Hands, open, taken,
                                       run.Refreshes, boosts.Stream, boosts.SecondWinds, boosts.Phoenixes,
                                       continues < 0 ? 0 : continues, watched < 0 ? 0 : watched, posts);
        }

        // ------------------------------------------------------------------ the board
        /// <summary>
        /// Whether this snapshot can be seated on <paramref name="layout"/>: the same posts in the
        /// same colours, on a lane whose waves never end. Asked before the board is built, so a
        /// snapshot that does not fit is dropped rather than half applied.
        /// </summary>
        public bool Fits(SiegeLayout layout, out string fault)
        {
            fault = null;
            if (layout == null || !layout.IsEndless) { fault = "the level is not an endless lane"; return false; }
            if (layout.Wards.Length != Posts.Count) { fault = $"the line has {layout.Wards.Length} posts; the snapshot {Posts.Count}"; return false; }

            for (int i = 0; i < Posts.Count; i++)
            {
                int colour = SiegeLayout.Letters.IndexOf(layout.Wards[i]);
                if (colour != Posts[i].Colour) { fault = $"post {i} is colour {colour}; the snapshot's is {Posts[i].Colour}"; return false; }
            }

            return true;
        }

        /// <summary>
        /// Puts every post back on <paramref name="board"/>. Called once, after the build has been
        /// fitted (<see cref="SiegeBoard.Refit"/>), on a board <see cref="Fits"/> said yes to.
        /// </summary>
        public void Seat(SiegeBoard board)
        {
            if (board == null) return;

            for (int i = 0; i < Posts.Count && i < board.Wards.Count; i++)
            {
                var p = Posts[i];
                board.Seat(i, p.Standing, p.Health, p.Fuel, p.Charges, p.Rank);
            }
        }

        // ------------------------------------------------------------------ writing
        public string Encode()
        {
            var o = new StringBuilder(256 + Tuning.Length * 11 / 10);
            o.Append("{\"v\":").Append(Version);
            o.Append(",\"level\":"); Str(o, Level.Value);
            o.Append(",\"seed\":").Append(Seed.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"wave\":").Append(Wave.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"hands\":").Append(Hands.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"refreshes\":").Append(Refreshes.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"stream\":").Append(Stream.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"winds\":").Append(SecondWinds.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"phoenixes\":").Append(Phoenixes.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"continues\":").Append(Continues.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"watched\":").Append(Watched.ToString(CultureInfo.InvariantCulture));
            o.Append(",\"taken\":"); Strs(o, Taken);
            o.Append(",\"open\":"); Strs(o, Open);

            o.Append(",\"posts\":[");
            for (int i = 0; i < Posts.Count; i++)
            {
                var p = Posts[i];
                if (i > 0) o.Append(',');
                o.Append('[').Append(p.Colour.ToString(CultureInfo.InvariantCulture))
                 .Append(',').Append(p.Standing ? '1' : '0')
                 .Append(',').Append(p.Health.ToString(CultureInfo.InvariantCulture))
                 // "R": the shortest text that reads back as exactly this float, so a tube is
                 // restored to the bit and never rounded across a shot's cost.
                 .Append(',').Append(p.Fuel.ToString("R", CultureInfo.InvariantCulture))
                 .Append(',').Append(p.Charges.ToString(CultureInfo.InvariantCulture))
                 .Append(',').Append(p.Rank.ToString(CultureInfo.InvariantCulture))
                 .Append(']');
            }
            o.Append(']');

            o.Append(",\"tuning\":"); Str(o, Tuning);
            o.Append('}');
            return o.ToString();
        }

        static void Strs(StringBuilder o, IReadOnlyList<string> values)
        {
            o.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) o.Append(',');
                Str(o, values[i]);
            }
            o.Append(']');
        }

        static void Str(StringBuilder o, string value)
        {
            o.Append('"');
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': o.Append("\\\""); break;
                    case '\\': o.Append("\\\\"); break;
                    case '\n': o.Append("\\n"); break;
                    case '\r': o.Append("\\r"); break;
                    case '\t': o.Append("\\t"); break;
                    default:
                        if (c < ' ') o.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else o.Append(c);
                        break;
                }
            }
            o.Append('"');
        }

        // ------------------------------------------------------------------ reading
        static readonly string[] Keys =
        {
            "v", "level", "seed", "wave", "hands", "refreshes", "stream", "winds", "phoenixes",
            "continues", "watched", "taken", "open", "posts", "tuning",
        };

        /// <summary>
        /// Reads an encoding back, whole or not at all. Never throws: anything wrong is a fault
        /// naming what, and <paramref name="snapshot"/> is null.
        /// </summary>
        public static bool TryDecode(string text, out ShuffleSnapshot snapshot, out string fault)
        {
            snapshot = null;

            try
            {
                snapshot = Decode(text, out fault);
                return snapshot != null;
            }
            catch (Exception e)
            {
                fault = "the snapshot could not be read: " + e.Message;
                return false;
            }
        }

        static ShuffleSnapshot Decode(string text, out string fault)
        {
            if (string.IsNullOrEmpty(text)) { fault = "the snapshot is empty"; return null; }
            if (text.Length > MostLength) { fault = $"the snapshot is {text.Length} characters, over {MostLength}"; return null; }

            if (!StrictJson.TryParse(text, out object parsed, out fault)) return null;
            if (!(parsed is Dictionary<string, object> root)) { fault = "the snapshot is not an object"; return null; }

            foreach (var key in root.Keys)
                if (Array.IndexOf(Keys, key) < 0) { fault = $"an unknown field '{key}'"; return null; }

            foreach (var key in Keys)
                if (!root.ContainsKey(key)) { fault = $"the field '{key}' is missing"; return null; }

            if (!Whole(root, "v", 0, int.MaxValue, out long version, out fault)) return null;
            if (version != Version) { fault = $"the snapshot is version {version}; this build reads version {Version}"; return null; }

            if (!(root["level"] is string levelText)) { fault = "'level' is not a string"; return null; }
            if (!LevelId.TryParse(levelText, out var level, out string why)) { fault = $"'level' is not a level id: {why}"; return null; }

            if (!Whole(root, "seed", 1, uint.MaxValue, out long seed, out fault)) return null;
            if (!Whole(root, "hands", 0, MostCount, out long hands, out fault)) return null;
            if (!Whole(root, "wave", 1, int.MaxValue, out long wave, out fault)) return null;
            if (wave != WaveAfter((int)hands)) { fault = $"wave {wave} is not the wave after hand {hands}"; return null; }

            if (!Whole(root, "refreshes", 0, MostCount, out long refreshes, out fault)) return null;
            if (!Whole(root, "stream", 0, uint.MaxValue, out long stream, out fault)) return null;
            if (!Whole(root, "winds", 0, MostCount, out long winds, out fault)) return null;
            if (!Whole(root, "phoenixes", 0, MostCount, out long phoenixes, out fault)) return null;
            if (!Whole(root, "continues", 0, MostCount, out long continues, out fault)) return null;
            if (!Whole(root, "watched", 0, MostCount, out long watched, out fault)) return null;

            if (!Ids(root, "taken", (int)hands, out var taken, out fault)) return null;
            if (!Ids(root, "open", ShuffleDeck.MostHand, out var open, out fault)) return null;

            // Every card taken was taken from a hand, and an open hand is a hand nothing was taken from yet.
            if (taken.Count + (open.Count > 0 ? 1 : 0) > hands)
            {
                fault = $"{taken.Count} cards taken and {open.Count} on the table from {hands} hands";
                return null;
            }

            if (!(root["tuning"] is string tuning)) { fault = "'tuning' is not a string"; return null; }

            if (!(root["posts"] is List<object> rows) || rows.Count < 1 || rows.Count > SiegeLayout.MaxWards)
            {
                fault = "'posts' is not a list of one to " + SiegeLayout.MaxWards + " posts";
                return null;
            }

            var posts = new List<Post>(rows.Count);
            bool anyStanding = false;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!(rows[i] is List<object> row) || row.Count != 6) { fault = $"post {i} is not six figures"; return null; }

                if (!Whole(row[0], $"post {i} colour", 0, SiegeLayout.Letters.Length - 1, out long colour, out fault) ||
                    !Whole(row[1], $"post {i} standing", 0, 1, out long standing, out fault) ||
                    !Whole(row[2], $"post {i} health", 0, int.MaxValue, out long health, out fault) ||
                    !Real(row[3], $"post {i} fuel", out float fuel, out fault) ||
                    !Whole(row[4], $"post {i} charges", 0, MostCount, out long charges, out fault) ||
                    !Whole(row[5], $"post {i} rank", 0, SiegeTuning.MaxRank, out long rank, out fault))
                    return null;

                anyStanding |= standing == 1;
                posts.Add(new Post((int)colour, standing == 1, (int)health, fuel, (int)charges, (int)rank));
            }

            // A snapshot is never taken of a lost board (`Of`), so a line with nobody standing is not one.
            if (!anyStanding) { fault = "no post is standing"; return null; }

            fault = null;
            return new ShuffleSnapshot(level, (uint)seed, tuning, (int)wave, (int)hands, open, taken,
                                       (int)refreshes, (uint)stream, (int)winds, (int)phoenixes,
                                       (int)continues, (int)watched, posts);
        }

        static bool Whole(Dictionary<string, object> root, string key, long least, long most,
                          out long value, out string fault)
            => Whole(root[key], "'" + key + "'", least, most, out value, out fault);

        static bool Whole(object written, string what, long least, long most, out long value, out string fault)
        {
            value = 0;
            fault = null;

            if (!(written is long n)) { fault = $"{what} is not a whole number"; return false; }
            if (n < least || n > most) { fault = $"{what} is {n}, outside {least}..{most}"; return false; }

            value = n;
            return true;
        }

        static bool Real(object written, string what, out float value, out string fault)
        {
            value = 0f;
            fault = null;

            double d;
            if (written is long whole) d = whole;
            else if (written is double real) d = real;
            else { fault = $"{what} is not a number"; return false; }

            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0d || d > float.MaxValue)
            {
                fault = $"{what} is {d}, not a fuel";
                return false;
            }

            value = (float)d;
            return true;
        }

        static bool Ids(Dictionary<string, object> root, string key, int most, out List<string> ids, out string fault)
        {
            ids = null;
            fault = null;

            if (!(root[key] is List<object> list)) { fault = $"'{key}' is not a list"; return false; }
            if (list.Count > most) { fault = $"'{key}' holds {list.Count}, over {most}"; return false; }

            ids = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is string id) || id.Length == 0) { fault = $"'{key}' entry {i} is not a card id"; return false; }
                ids.Add(id);
            }

            return true;
        }
    }
}
