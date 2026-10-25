using System.Collections.Generic;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed partial class SiegeRuleTests
    {
        /// <summary>
        /// The line the one live account that had cleared every level stood on 2026-10-24: pyre on
        /// red and green, leech on blue, harpoon on yellow, every seat at <paramref name="stars"/>.
        ///
        /// <para>
        /// <b>A real player's line, not one of this file's four-of-a-kind</b>, because the gate's own
        /// "strongest" line had stopped meaning the strongest a player brings (MODES.md 37fh): this is
        /// what the chapter is commissioned against - "players easily climb levels without ever
        /// needing to buy coins". At three stars it is the line as it was found; at four it is that
        /// line one upgrade a seat further on, which is what the brief asks a player to want to buy.
        /// </para>
        /// </summary>
        static WardLine Veteran(int stars)
        {
            var chosen = new List<WardSlot>
            {
                new WardSlot('r', "pyre"), new WardSlot('g', "pyre"),
                new WardSlot('b', "leech"), new WardSlot('y', "harpoon"),
            };

            var line = WardLine.Resolve(WardCatalog.Default, chosen, (model, colour) => true,
                                        (model, colour) => stars);

            Assert.AreEqual("pyre", line.At(0).Id, "the veteran line did not resolve to what it asked for");
            Assert.AreEqual("harpoon", line.At(3).Id, "the veteran line did not resolve to what it asked for");
            return line;
        }

        /// <summary>
        /// **The fourteenth chapter is harder, in waves, and not a wall** (MODES.md 37fh).
        ///
        /// <para>
        /// <b>Harder</b>: strictly fewer runs held than Wyrmforge on every line read - the gate's
        /// three-star embers and strongest line, and the veteran line at three stars and at four. A
        /// chapter that only reads harder on the gate's lines is the fault this chapter was
        /// commissioned to fix, so the veteran line is asserted, not printed.
        /// </para>
        /// <para>
        /// <b>Not a wall</b>: on the veteran line at four stars - one upgrade a seat past what the
        /// best live player stood - every rung is held at some rhythm. The brief was "challenging, not
        /// impossible": a rung no upgraded line can hold is a rung only quitting answers.
        /// </para>
        /// <para>
        /// <b>Measured on 2026-10-25</b>, against Wyrmforge: three-star embers 10 of 180 against 19,
        /// the strongest line 77 against 97, the veteran line at three stars 29 against 47 (one rung
        /// walled) and at four stars 60 against 70 with nothing walled and every rung held 1 to 5 of 9.
        /// MODES.md 37fh has the reading.
        /// </para>
        /// </summary>
        [Test]
        public void TheFourteenthChapterIsHarder()
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
                var here = Play(Rustwaste, line);
                var before = Play(Wyrmforge, line, walls: false);

                report.Append($"Rustwaste on {name}: {here.Held}/{here.Runs} held, {here.Starred} three-starred, "
                              + $"{here.Walled} walled - Wyrmforge {before.Held}/{before.Runs} held, "
                              + $"{before.Starred} three-starred\n" + here.Table + "\n");

                if (here.Held >= before.Held)
                    faults.Add($"on {name} Rustwaste held {here.Held} against Wyrmforge's {before.Held} - "
                               + "no harder than the thirteenth");

                upgraded = here;
            }

            if (upgraded.Walled > 0)
                faults.Add($"{upgraded.Walled} rung(s) of Rustwaste are held at no rhythm at all on the "
                           + "veteran line at four stars - a wall, not a challenge");

            System.Console.WriteLine(report.ToString());
            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\n" + report);
        }
    }
}
