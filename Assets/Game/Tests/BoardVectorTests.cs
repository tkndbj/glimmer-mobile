using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of the four-armed-tile contract.
    ///
    /// <para>
    /// A crossing and a briar mate every neighbour at every angle, so nothing about the
    /// pipe-fitting settles either one: the rule is that turning one a step off its solution
    /// has to stop the glade finishing. It exists in three places — this assembly's
    /// <c>LevelValidator.CheckDecidableTiles</c>, <c>Tools/verify/content.py</c>'s
    /// <c>decidable</c> and <c>Tools/verify/author.py</c>'s <c>Board.decides</c> — because the
    /// Editor, the offline gate and the authoring aid each need it and none of them can call
    /// the others.
    /// </para>
    /// <para>
    /// Three copies of one rule drift, and the comments saying "keep these in sync" were never
    /// going to survive a year of content drops. So all three run
    /// <c>Tools/verify/board-vectors.json</c>: this file proves the C# copy matches it, and
    /// <c>content.py</c> proves both Python copies do on every offline run. Invariant 9a's
    /// shape, for a board rule rather than for money — the first time this project has drifted
    /// a board rule was the check this replaced, which two tools disagreed about for a whole
    /// chapter without anything noticing.
    /// </para>
    /// <para>
    /// Needs the Editor, because <c>JsonUtility</c> is a native call. <c>BriarTests</c> and
    /// <c>CrossingTests</c> carry the same shapes inline for the same reason
    /// <c>GoldenGladeTests</c> does: those run on every offline compile, so the rule is checked
    /// without anybody opening Unity, and a green run here means all three agree.
    /// </para>
    /// </summary>
    public sealed class BoardVectorTests
    {
        [Serializable]
        public sealed class VectorFile
        {
            public int schemaVersion;
            public VectorCase[] cases;
        }

        [Serializable]
        public sealed class VectorCase
        {
            public string name;
            public string why;
            public string[] rows;

            /// <summary>Tiles the rule must complain about, as "x,y". Empty means none.</summary>
            public string[] undecided;
        }

        static string VectorPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "verify",
                                          "board-vectors.json"));

        static VectorFile Load()
        {
            Assert.IsTrue(File.Exists(VectorPath), $"board vectors not found at {VectorPath}");

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(VectorPath));
            Assert.IsNotNull(file, "the vector file did not parse");
            Assert.IsNotNull(file.cases, "the vector file has no cases");
            Assert.Greater(file.cases.Length, 0);

            return file;
        }

        static LevelDefinition Level(string[] rows)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);
            var parsed = LevelGridParser.Parse(layout);
            int par = parsed.Ok ? Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells)) : 1;

            return new LevelDefinition(
                LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                layout, LevelTuning.Default(par),
                new LevelPresentation(new Vector2(.5f, .5f), null, null, null));
        }

        /// <summary>The marker the rule's warning carries, shared with both Python copies.</summary>
        const string Marker = "still finishes the glade";

        [Test]
        public void EveryBoardVectorAgreesWithTheValidator()
        {
            var file = Load();
            var failures = new List<string>();

            foreach (var c in file.cases)
            {
                var report = LevelValidator.Validate(Level(c.rows));

                foreach (var issue in report.Issues)
                    if (issue.Severity == LevelIssueSeverity.Error)
                        failures.Add($"{c.name}: the board itself is invalid — {issue.Message}");

                var said = new List<string>();
                foreach (var issue in report.Issues)
                    if (issue.Message.Contains(Marker))
                        said.Add(issue.Message);

                var want = c.undecided ?? new string[0];

                if (said.Count != want.Length)
                    failures.Add($"{c.name}: expected {want.Length} undecided tile(s), " +
                                 $"got {said.Count} — {string.Join(" | ", said)}");

                foreach (var at in want)
                {
                    bool found = false;
                    foreach (var message in said)
                        if (message.Contains(" at " + at + " ")) found = true;

                    if (!found)
                        failures.Add($"{c.name}: nothing complained about the tile at {at}. " +
                                     $"{c.why}");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// Every vector's board has to be a glade that actually finishes, or a case proves
        /// nothing: the rule is skipped entirely on a board whose authored solution does not
        /// win, so a broken vector would pass by being ignored rather than by being right.
        /// </summary>
        [Test]
        public void EveryBoardVectorIsAGladeThatFinishes()
        {
            var file = Load();
            var failures = new List<string>();

            foreach (var c in file.cases)
            {
                int width = c.rows[0].Split(' ').Length;
                var parsed = LevelGridParser.Parse(new LevelLayout(width, c.rows.Length, c.rows));

                if (!parsed.Ok)
                {
                    failures.Add($"{c.name}: {string.Join("; ", parsed.Errors)}");
                    continue;
                }

                var solved = new Cell[parsed.Cells.Length];
                for (int i = 0; i < solved.Length; i++)
                {
                    solved[i] = parsed.Cells[i];
                    solved[i].rot = 0;
                }

                var board = new Puzzle(LevelId.Parse("t_level"), width, c.rows.Length,
                                       LevelTuning.Default(1), solved);

                if (!board.Won) failures.Add($"{c.name}: the authored solution does not win");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
