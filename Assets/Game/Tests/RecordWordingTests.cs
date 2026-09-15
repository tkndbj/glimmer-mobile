using System;
using System.Collections.Generic;
using System.IO;
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
        /// <summary>
        /// The shipped table, by key.
        ///
        /// <b>Read through <see cref="ShippedStrings"/> rather than here</b>, because a second
        /// fixture came to want the same thing (<c>KeeperReportTests</c>, asking whether a
        /// derived key resolves) and two readers of one table is the drift invariant 44d refuses
        /// for a mirror. The reasoning that put the reader here in the first place moved with it:
        /// nothing publishes the table in a fixture, and <c>JsonUtility</c> is a native call, so
        /// a test that parsed the file the shipping way would only run with the Editor open.
        /// </summary>
        static Dictionary<string, string> Table() => ShippedStrings.Table();

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
