using System.Collections.Generic;
using System.Text;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The second chapter, and the two questions that are about the pair of chapters rather than
    /// about either one.
    ///
    /// <para>
    /// <b>A second part of the same class rather than a file of its own</b>, because it needs the
    /// harness the first part already has - <c>Rung</c>, <c>Layout</c>, <c>Hold</c>, <c>Aimed</c>
    /// and <c>Health</c> - and a second copy of a player model is a second player. It is a second
    /// <em>file</em> because <c>SiegeRuleTests.cs</c> is already two thousand lines and a chapter
    /// is not a reason to make it three.
    /// </para>
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        // ------------------------------------------------------------------ the second chapter
        /// <summary>
        /// Every rung of Broodmarch, held inline exactly as `Tools/chapters/s03_broodmarch.py`
        /// writes it.
        ///
        /// <b>Inline for <see cref="Chapter"/>'s reason</b>: a fixture that loads JSON goes through
        /// <c>JsonUtility</c>, which is a native call, so the offline runner reports the whole file
        /// as "needs the Editor" and it becomes the one gate nobody runs on the way past. What
        /// keeps a hand copy honest is <c>Tools/verify/rungs.py</c>, which holds these rows to
        /// the shipped body offline and names the line to change when they drift.
        /// </summary>
        static readonly Rung[] Broodmarch =
        {
            new Rung("s03_firstbrood", new[] { "grrgbybg", "brbygrgy", "gbyrbbyr", "rrggrrbr", "rggbbgrg" }, "rgby", "rgby", new[] { "rgbyRGby", "RGBYrgby", "RGBYRGby" }, "", 25),
            new Rung("s03_hollowshell", new[] { "ybrgbgyr", "yrbrrbrb", "gbrbyybg", "ybgbrgry", "bygygybr" }, "rgby", "rgby", new[] { "rgbyRGby", "RGBY#rGby", "RGBY#gRGby" }, "", 25),
            new Rung("s03_mirewalk", new[] { "gbyybrrb", "ybbrgbyy", "gygyrgrg", "bbybyyrb", "yygrrbyb" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY!rRGby", "RGBYRG!gBY!b" }, "", 25),
            new Rung("s03_spinecrest", new[] { "gbygybbr", "rrggrrbr", "rgybyrgy", "ygbgrbyb", "brrgybyr" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGby#gRGby", "RGby#bRGby" }, "", 25),
            new Rung("s03_blightfen", new[] { "bbgygbbg", "grbgryry", "rgyrygyr", "brgbbrgr", "yrgbyrby" }, "rgby", "rgby", new[] { "rgbyRGby", "RGbyRGby", "RGBY#rRGby" }, "blightcaller:b", 25),
            new Rung("s03_stillmire", new[] { "rggybgrr", "yrybbrby", "ggbrggyg", "rryygybr", "bgbrrgrb" }, "rgby", "rgby", new[] { "RRRrrrrg", "GGG#gGGgb", "BBBbbbbYYy" }, "", 25),
            new Rung("s03_thornbrood", new[] { "rbyrgrbb", "rryybgyg", "yggyybrg", "yybrrgyr", "bbggbyyr" }, "rgby", "rgby", new[] { "RGbyrgby", "RGbyRGbyrg", "RGBY#yRGbyrg" }, "", 25),
            new Rung("s03_gloamfield", new[] { "gygrybyb", "byrrbbrb", "brggygyy", "yybbyggb", "grbgrbby" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY!gRGby", "RGBY#bRGby" }, "", 25),
            new Rung("s03_deepmire", new[] { "gygrbgbg", "yrybryyb", "gbgrgbyg", "ryybybbg", "bybbryry" }, "rgby", "rgby", new[] { "rgbyRGby", "RGbyRGby", "RGby#rRGby#gby", "RGBYrgby" }, "", 25),
            new Rung("s03_broodheart", new[] { "rrggrbgb", "ybgyryrg", "gybyggyy", "rgrbgrry", "rgrbybrr" }, "rgby", "rgby", new[] { "RGbyRGby", "RGby#rRGby", "RGBYRG#gby" }, "warbringer:g", 25),
        };

        // ------------------------------------------------------------------ the lines it plays
        /// <summary>
        /// The four turrets a player who has bought nothing stands: the free bolt, four times.
        /// </summary>
        static WardLine Bare() => WardLine.Starter(WardCatalog.Default);

        /// <summary>
        /// The four turrets a player who has bought <em>one rung</em> of the shelf stands.
        ///
        /// <para>
        /// <b>The first credit rung on every colour, and nothing above it.</b> The shelf is one
        /// ladder climbed a rung at a time per colour (invariant 42c), so this is the cheapest
        /// four-turret line that exists at all - and at the roster's own prices it is about twice
        /// what a chapter of three-star clears pays, which is the honest reading of "a little
        /// higher turrets": a player who spent their first chapter's earnings on the line rather
        /// than on the grove.
        /// </para>
        /// <para>
        /// <b>Named rather than indexed</b>, so a re-rung shelf (which has happened twice) fails
        /// this loudly instead of quietly measuring a different turret.
        /// </para>
        /// </summary>
        const string FirstRung = "siphon";

        static WardLine Kitted()
        {
            var catalog = WardCatalog.Default;
            Assert.IsNotNull(catalog.Find(FirstRung),
                             $"'{FirstRung}' is not on the roster any more, so this test is "
                             + "measuring the starter twice");

            var chosen = new List<WardSlot>();
            for (int i = 0; i < WardLine.Colours.Length; i++)
                chosen.Add(new WardSlot(WardLine.Colours[i], FirstRung));

            var line = WardLine.Resolve(catalog, chosen, (model, colour) => true);

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(FirstRung, line.At(i).Id,
                                "the kitted line did not resolve to what it asked for");

            return line;
        }

        // ------------------------------------------------------------------ the sweep
        /// <summary>What a whole chapter did, at every rhythm, on one line.</summary>
        struct Sweep
        {
            public int Held;            // runs finished with two wards or more standing
            public int Starred;         // runs finished inside the three-star line
            public int Runs;            // rhythms times rungs
            public int Walled;          // rungs held at no rhythm at all
            public string Table;
        }

        /// <summary>
        /// Play a whole chapter at every rhythm on one line, and report what happened.
        ///
        /// <b>Nine rhythms rather than one, for invariant 37aq's reason</b>: a match changes the
        /// field, the field decides the next match, and two rhythms twenty milliseconds apart play
        /// out completely differently - so one sample of this is a coin toss and a green tick was
        /// once a coin landing the right way up.
        /// </summary>
        static Sweep Play(Rung[] chapter, WardLine line)
        {
            float[] rhythms = { 2.20f, 2.25f, 2.30f, 2.35f, 2.40f, 2.45f, 2.50f, 2.55f, 2.60f };

            var swept = new Sweep { Runs = chapter.Length * rhythms.Length };
            var table = new StringBuilder();

            for (int i = 0; i < chapter.Length; i++)
            {
                var layout = chapter[i].Built();
                int par = SiegeTuning.Par(layout);
                int gold = (par * 120 + 99) / 100;

                int held = 0, starred = 0, touched = 0;
                int worstLine = int.MaxValue, whole = 0;

                foreach (float rhythm in rhythms)
                {
                    var board = SiegeBoard.Build(layout, line);
                    int matches = Hold(board, rhythm, out int _);

                    whole = board.Wards.Count * SiegeTuning.WardHealth;
                    int standing = Health(board);

                    if (board.IsFinished && board.WardsStanding >= 2) held++;
                    if (board.IsFinished && matches <= gold) starred++;
                    if (standing < whole) touched++;
                    if (standing < worstLine) worstLine = standing;
                }

                swept.Held += held;
                swept.Starred += starred;
                if (held == 0) swept.Walled++;

                table.AppendLine($"  {chapter[i].Id,-18} par {par,3}  3* {gold,3}"
                                 + $"  held {held}/{rhythms.Length}"
                                 + $"  3* in {starred}/{rhythms.Length}"
                                 + $"  worst line {worstLine,3} of {whole,3}"
                                 + $"  reached {touched}/{rhythms.Length}");
            }

            swept.Table = table.ToString();
            return swept;
        }

        // ------------------------------------------------------------------ the gates
        /// <summary>
        /// Every rung of Broodmarch parses, has a legal opening swap, and asks for a line.
        ///
        /// The layout half of what <c>Tools/chapters/s03_broodmarch.py</c> proves, run here as
        /// well because the Python is a mirror and this is the rule that ships.
        /// </summary>
        [Test]
        public void EveryRungOfBroodmarchReads()
        {
            var faults = new List<string>();

            for (int i = 0; i < Broodmarch.Length; i++)
            {
                var rung = Broodmarch[i];
                var layout = rung.Built();

                if (layout.Fault != null)
                {
                    faults.Add($"{rung.Id}: {layout.Fault}");
                    continue;
                }

                if (SiegeTuning.Par(layout) < 1)
                    faults.Add($"{rung.Id}: par came out below one");

                // `AnySwap`, never `AnyMove` - the second answers "is this run still going", which
                // a fresh board is whatever its field looks like (invariant 37ad narrowed it for
                // exactly this reason). What a level has to be authored with is a field somebody
                // can actually touch.
                var board = SiegeBoard.Build(layout);
                if (!board.AnySwap())
                    faults.Add($"{rung.Id}: the field is dealt with no legal swap on it");
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// Each chapter sends <b>two</b> bosses, on its fifth rung and its tenth.
        ///
        /// <para>
        /// <b>A structural rule rather than a tuning one, and the reason is that a count is what a
        /// player feels.</b> The first chapter shipped four bosses across ten rungs - one every two
        /// or three - and the owner's call was that half a chapter being a boss rung leaves no rung
        /// that is remembered for anything else. Five and ten is the shape the genre uses: a
        /// midpoint and a finale.
        /// </para>
        /// <para>
        /// It is checked rather than trusted because it is exactly the sort of thing a content edit
        /// undoes without noticing - a boss token is one field on one rung, and both gates that
        /// read a chapter today are happy with a boss anywhere or nowhere.
        /// </para>
        /// </summary>
        [Test]
        public void EachChapterSendsTwoBossesOnTheFifthAndTheTenthRung()
        {
            var faults = new List<string>();

            foreach (var pair in new[] { ("Thornwatch", Chapter), ("Broodmarch", Broodmarch) })
            {
                var name = pair.Item1;
                var rungs = pair.Item2;

                Assert.AreEqual(10, rungs.Length, $"{name} is not ten rungs long any more");

                for (int i = 0; i < rungs.Length; i++)
                {
                    bool wanted = i == 4 || i == 9;
                    bool sends = rungs[i].Built().HasBoss;

                    if (sends == wanted) continue;

                    faults.Add(sends
                                   ? $"{name} rung {i + 1} ({rungs[i].Id}) sends a boss and should not"
                                   : $"{name} rung {i + 1} ({rungs[i].Id}) sends no boss and should");
                }
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// No boss verb is sent by both chapters.
        ///
        /// <b>Invariant 37z asked across a ladder rather than within one.</b> That entry was bought
        /// by two bosses separated by a run-time hue reading as one boss, and the rule it left is
        /// that four bosses have to be four <em>fights</em>. A player meeting a smite in chapter one
        /// and a smite in chapter two has met one fight twice, however far apart they are - so the
        /// four verbs are dealt one each across the twenty rungs: a warlord and an overlord here, a
        /// blightcaller and a warbringer there.
        /// </summary>
        [Test]
        public void NoBossVerbIsSentByBothChapters()
        {
            var seen = new Dictionary<SiegeSpell, string>();
            var faults = new List<string>();

            foreach (var pair in new[] { ("Thornwatch", Chapter), ("Broodmarch", Broodmarch) })
            {
                foreach (var rung in pair.Item2)
                {
                    var layout = rung.Built();
                    if (!layout.HasBoss) continue;

                    var craft = SiegeTuning.SpellOf(layout.BossKind);

                    if (seen.TryGetValue(craft, out string already))
                        faults.Add($"{rung.Id} sends a {craft} and so does {already}, so one fight "
                                   + "is sent twice across the two chapters");
                    else
                        seen[craft] = rung.Id;
                }
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// **The second chapter is hard on the starter turret and comfortable one rung up the
        /// shelf** — which is the brief it was commissioned against, measured rather than felt.
        ///
        /// <para>
        /// <b>This is the first gate in this mode that plays a <em>chosen</em> line.</b> Everything
        /// else here plays <c>SiegeBoard.Build(layout)</c>, which falls back to the starter - right
        /// for the first chapter, where the free bolt is the whole game, and useless for a question
        /// whose whole subject is whether turrets are the answer. <c>SiegeBoard.Build</c> has taken
        /// a line since the loadout shipped (invariant 42); nothing had asked it for one.
        /// </para>
        /// <para>
        /// <b>Four assertions, and each closes a different way of getting this wrong.</b>
        /// <list type="number">
        /// <item>No rung is <em>walled</em> on the starter - a rung an unhurried player holds at no
        /// rhythm at all with the line they arrive on is a wall rather than a reason to shop, and
        /// invariant 24's argument about the heart gate says the same thing about the moment a
        /// player is stopped.</item>
        /// <item>The starter is <em>strictly harder</em> here than on the first chapter. A second
        /// chapter that plays like the first did not get harder, whatever its par says.</item>
        /// <item>One rung of the shelf really answers it - the kitted line holds strictly more runs
        /// than the bare one. A shelf that changes nothing is invariant 5d's decoration on the one
        /// thing in this game a player pays for.</item>
        /// <item>And the kitted line clears it comfortably, so "doable" is a measurement and not a
        /// hope.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>The floors are a record of where the chapter stands, not targets</b> — the same
        /// bargain <c>AnUnhurriedPlayerHoldsThisLine</c> strikes. A change that moves them wants
        /// measuring and then moving deliberately; a change that drops them several points is a
        /// change that loses runs, whatever one rhythm says.
        /// </para>
        /// </summary>
        [Test]
        public void TheSecondChapterAsksForBetterTurrets()
        {
            // Where this chapter stands, measured. Set below what was read, because a sweep of
            // ninety is steady and not exact.
            // Measured 2026-09-12, after the colour lock and the shorter wave gaps: 65 on the
            // starter, 84 one rung up, against Thornwatch's 81 on the starter. The worst rung on
            // the starter is the finale at 1 of 9, and the shelf is worth nineteen runs across the
            // chapter - which is the whole story this chapter is meant to tell.
            const int BareFloor = 60;       // hard, and nobody is walled out
            const int KittedFloor = 78;     // comfortably clearable once the shelf is used
            const int Answers = 12;         // runs the first rung of the shelf is worth, at least

            var bare = Play(Broodmarch, Bare());
            var kitted = Play(Broodmarch, Kitted());
            var first = Play(Chapter, Bare());

            var faults = new List<string>();

            if (bare.Walled > 0)
                faults.Add($"{bare.Walled} rung(s) of Broodmarch are held at no rhythm at all on "
                           + "the starter line, which is a wall rather than a reason to buy a "
                           + "turret");

            if (bare.Held >= first.Held)
                faults.Add($"on the starter line Broodmarch held {bare.Held} of {bare.Runs} runs "
                           + $"against Thornwatch's {first.Held} of {first.Runs} - the second "
                           + "chapter is not harder than the first, which is what it is for");

            if (kitted.Held <= bare.Held + Answers)
                faults.Add($"one rung of the shelf moved Broodmarch from {bare.Held} to "
                           + $"{kitted.Held} of {kitted.Runs} runs, which is inside the noise of a "
                           + "ninety-run sweep - a shelf that changes nothing is decoration on the "
                           + "one thing in this game a player pays for");

            if (bare.Held < BareFloor)
                faults.Add($"on the starter line Broodmarch held {bare.Held} of {bare.Runs} runs "
                           + $"against a floor of {BareFloor}, so it has become a wall rather than "
                           + "hard - re-measure before moving the floor");

            if (kitted.Held < KittedFloor)
                faults.Add($"one rung up the shelf Broodmarch held {kitted.Held} of "
                           + $"{kitted.Runs} runs against a floor of {KittedFloor}, so it is not "
                           + "doable with better turrets either - re-measure before moving the "
                           + "floor");

            if (kitted.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + $"\n\nBroodmarch on the starter ({bare.Held}/{bare.Runs} held, "
                           + $"{bare.Starred} three-starred):\n" + bare.Table
                           + $"\nBroodmarch on {FirstRung} ({kitted.Held}/{kitted.Runs} held, "
                           + $"{kitted.Starred} three-starred):\n" + kitted.Table
                           + $"\nThornwatch on the starter for comparison "
                           + $"({first.Held}/{first.Runs} held):\n" + first.Table);
        }
    }
}
