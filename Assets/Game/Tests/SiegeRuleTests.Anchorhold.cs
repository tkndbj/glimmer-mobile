using System.Collections.Generic;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed partial class SiegeRuleTests
    {
        /// <summary>
        /// **The sixteenth chapter is harder than the fifteenth, in spikes, and not a wall**
        /// (MODES.md 37fj).
        ///
        /// <para>
        /// <b>Harder</b>: strictly fewer runs held than Cinderworks on every line read - the gate's
        /// three-star embers and strongest line, and the veteran line (<see cref="Veteran"/>, the one
        /// live account that had cleared every level) at three stars and at four. The brief was
        /// written off production data: players clear the ladder without ever buying an upgrade, so
        /// the line a real player stands is asserted, not printed.
        /// </para>
        /// <para>
        /// <b>Not a wall</b>: on the veteran line at four stars - one upgrade a seat past what the
        /// best live player stood - every rung is held at some rhythm. A spike at 2.7 is meant to
        /// make a player want that upgrade; a rung no upgraded line can hold is a rung only quitting
        /// answers.
        /// </para>
        /// <para>
        /// <b>Measured on 2026-10-25</b>, against Cinderworks: three-star embers 2 of 180 against 3,
        /// the strongest line 50 against 55, the veteran line at three stars 14 against 16 (seven
        /// rungs walled) and at four stars 50 against 53 with nothing walled and every rung held 2 to 4
        /// of 9. MODES.md 37fj has the reading.
        /// </para>
        /// </summary>
        [Test]
        public void TheSixteenthChapterIsHarder()
        {
            var lines = new (string Name, WardLine Line)[]
            {
                ("three-star " + Workhorse, Starred(Workhorse, 3)),
                ("the strongest line", Strongest()),
                ("the veteran line at three stars", Veteran(3)),
                ("the veteran line at four stars", Veteran(4)),
            };

            var faults = new List<string>();
            var report = new System.Text.StringBuilder();
            Sweep upgraded = default;

            foreach (var (name, line) in lines)
            {
                var here = Play(Anchorhold, line);
                var before = Play(Cinderworks, line, walls: false);

                report.Append($"Anchorhold on {name}: {here.Held}/{here.Runs} held, {here.Starred} three-starred, "
                              + $"{here.Walled} walled - Cinderworks {before.Held}/{before.Runs} held, "
                              + $"{before.Starred} three-starred\n" + here.Table + "\n");

                if (here.Held >= before.Held)
                    faults.Add($"on {name} Anchorhold held {here.Held} against Cinderworks' {before.Held} - "
                               + "no harder than the fifteenth");

                upgraded = here;
            }

            if (upgraded.Walled > 0)
                faults.Add($"{upgraded.Walled} rung(s) of Anchorhold are held at no rhythm at all on the "
                           + "veteran line at four stars - a wall, not a challenge");

            System.Console.WriteLine(report.ToString());
            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\n" + report);
        }
    }
}
