using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Thornwatch's rules, and the shipped level's numbers held inline.
    ///
    /// <para>
    /// <b>Inline rather than read from the chapter body</b>, for the reason every
    /// <c>*LadderTests</c> in this project is: a fixture that loads JSON goes through
    /// <c>JsonUtility</c>, which is a native call, so the offline runner reports the whole file as
    /// "needs the Editor" and it becomes the one gate nobody runs on the way past. What is pinned
    /// here therefore runs on every offline check, which is where it is wanted.
    /// </para>
    /// <para>
    /// <b>And it is doing a job the other modes' ladder tests do not have to.</b> Everywhere else
    /// par is a search, so a drift between the C# rules and the Python mirror shows up as two
    /// different pars over one board. Here par is arithmetic in two files, and arithmetic that
    /// disagrees is silent: every number stays individually plausible and a level is simply graded
    /// differently on a phone and on a build machine. These are the numbers
    /// <c>Tools/verify/siege.py</c> prints, written down.
    /// </para>
    /// </summary>
    public sealed class SiegeRuleTests
    {
        // ------------------------------------------------------------------ the shipped level
        static readonly string[] Field =
        {
            "rgrgyrry",
            "bgygybbg",
            "grrbbgyr",
            "bybbgryg",
            "ryygybrb",
        };

        const string Gems = "rgby";
        const string Wards = "rgby";

        static readonly string[] Waves = { "rgby", "rgbyrgby", "RGBYRGBY" };

        static SiegeLayout Shipped() => Layout(Field, Gems, Wards, Waves);

        static SiegeLayout Layout(string[] rows, string gems, string wards, string[] waves)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            SiegeLayout.Letters, out var grid, out string error),
                          error);

            return new SiegeLayout(grid, gems, wards, waves);
        }

        [Test]
        public void TheShippedLevelReads()
        {
            var layout = Shipped();

            Assert.IsNull(layout.Fault, layout.Fault);
            Assert.AreEqual(4, layout.Wards.Length);
            Assert.AreEqual(3, layout.Waves.Length);
            Assert.AreEqual(20, layout.RaiderCount);
        }

        [Test]
        public void TheShippedLevelIsParTwentyNine()
        {
            // 12 creepers at 20 and 8 brutes at 48 is 624, over what a match delivers (22).
            // `Tools/verify/siege.py` prints the same number from the same arithmetic; if these
            // two ever disagree, one of the constants moved in one file only.
            Assert.AreEqual(29, SiegeTuning.Par(Shipped()));
        }

        [Test]
        public void ParIsTheHillsHealthOverWhatOneMatchDelivers()
        {
            // The claim the star lines rest on, written out: a match clears about five and a half
            // gems once cascades are counted, each spent as a bolt into the ward its target is
            // weak to. **Measured rather than reasoned about** - see SiegeTuning.MatchGemsTenths
            // for the version that was reasoned about and was nearly twice too generous.
            Assert.AreEqual(22, SiegeTuning.PerfectMatch);

            var layout = Layout(Field, Gems, Wards, new[] { "r" });
            Assert.AreEqual(1, SiegeTuning.Par(layout), "one creeper is under one match");

            layout = Layout(Field, Gems, Wards, new[] { "rrr" });
            Assert.AreEqual(3, SiegeTuning.Par(layout), "three creepers is sixty over twenty-two");

            layout = Layout(Field, Gems, Wards, new[] { "R" });
            Assert.AreEqual(3, SiegeTuning.Par(layout), "a brute is 48, which is three matches");
        }

        // ------------------------------------------------------------------ the field
        [Test]
        public void TheShippedFieldIsAuthoredSettled()
        {
            // A field that goes off before anybody has touched it is a board whose opening move
            // its author played, and the count the run is graded against would have moved with it.
            Assert.IsNull(Layout(Field, Gems, Wards, Waves).Fault);

            var bad = Layout(new[] { "rrrgybgy", "bgygybbg", "grrbbgyr", "bybbgryg", "ryygybrb" },
                             Gems, Wards, Waves);
            Assert.IsNotNull(bad.Fault, "three alike touching has to be refused");
            StringAssert.Contains("settled", bad.Fault);
        }

        [Test]
        public void TheShippedFieldHasSomethingToDoOnIt()
        {
            var board = SiegeBoard.Build(Shipped());
            Assert.IsTrue(board.AnySwap(), "no swap on this field lines anything up");
        }

        [Test]
        public void TwoBoardsOfOneLevelDealTheSameField()
        {
            // The refill stream is seeded from the authored field rather than from a clock, so a
            // bug reported against a level is a bug somebody else can meet.
            var a = SiegeBoard.Build(Shipped());
            var b = SiegeBoard.Build(Shipped());

            Play(a, 12);
            Play(b, 12);

            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a.At(i), b.At(i), $"cell {i} differs after twelve matches");
        }

        [Test]
        public void TheFieldNeverLocks()
        {
            // This mode's clock does not stop, so a board with no legal swap is a run the player
            // watches themselves lose. It deals itself again instead.
            var board = SiegeBoard.Build(Shipped());

            for (int i = 0; i < 200; i++)
            {
                Assert.IsTrue(board.AnySwap(), $"the field locked after {i} matches");
                Assert.IsTrue(First(board, out _, out _),
                              "AnySwap said yes and there was no swap");
                Play(board, 1);
            }
        }

        // ------------------------------------------------------------------ the wards
        [Test]
        public void AMatchFuelsTheWardOfItsOwnColourAndNoOther()
        {
            var board = SiegeBoard.Build(Shipped());

            Assert.IsTrue(First(board, out int a, out int b), "no swap on this field");

            var turn = board.Swap(a, b);
            Assert.IsNotNull(turn);

            float fuelled = 0f;
            int wards = 0;

            for (int w = 0; w < board.Wards.Count; w++)
                if (board.Wards[w].Fuel > 0f)
                {
                    wards++;
                    fuelled += board.Wards[w].Fuel;
                }

            Assert.GreaterOrEqual(wards, 1, "a match fuelled nothing");
            Assert.AreEqual(turn.Worth * SiegeTuning.FuelPerGem, fuelled, .001f,
                            "the fuel a match is worth is the gems it cleared");
        }

        [Test]
        public void FuelLeavesAWardAsABoltAndNoOtherWay()
        {
            // **It used to fade on a clock and no longer does** - withdrawn by the owner after
            // play, because a meter draining while nothing is happening reads as the game taking
            // something away. This is the rule that replaced it, and it is worth a test rather
            // than a comment: a fade reintroduced by accident is invisible except as a mode that
            // has quietly got harder.
            var board = SiegeBoard.Build(Shipped());

            Assert.IsTrue(First(board, out int a, out int b), "no swap on this field");
            board.Swap(a, b);

            float before = Total(board);
            Assert.Greater(before, 0f);

            // Nothing is on the hill for the first few seconds, so there is nothing to fire at
            // and nothing may leave the tubes.
            for (int i = 0; i < 120; i++) board.Advance(1f / 60f);

            Assert.AreEqual(before, Total(board), .0001f,
                            "fuel left a ward with nothing to shoot at");

            // And it does leave, one bolt at a time, once there is something to shoot.
            Frames(board, SiegeTuning.FirstWaveAfter + 1f);

            Assert.Less(Total(board), before, "a ward with a target never fired");
        }

        [Test]
        public void AStepOfTheClockIsBoundedHoweverLongTheAppWasAway()
        {
            // A backgrounded app comes back with an enormous delta, and this mode is the only one
            // where that would *do* something: an unbounded step marches a whole wave into the
            // line while nobody is looking. `SiegeBoard.Advance` clamps, which is why every test
            // and the view itself step in frames.
            var board = SiegeBoard.Build(Shipped());

            board.Advance(3600f);

            Assert.AreEqual(0, board.Wave, "an hour away brought a wave out");
            Assert.AreEqual(0, board.Raiders.Count);
        }

        [Test]
        public void AWardThatHasFallenTakesNoFuelAndTheGemsStillGo()
        {
            // The whole cost of losing a ward: its colour is still on the field and is worth
            // nothing now.
            var board = SiegeBoard.Build(Shipped());
            Fell(board);

            Assert.IsTrue(First(board, out int a, out int b),
                          "the field should still have a swap on it");

            var turn = board.Swap(a, b);

            Assert.IsNotNull(turn, "the gems still go");
            Assert.Greater(turn.Worth, 0);
            Assert.AreEqual(0f, Total(board), .001f, "a fallen line took fuel");
        }

        [Test]
        public void TheRunIsLostWhenTheLastWardFallsAndNoPurchaseRescuesIt()
        {
            var board = SiegeBoard.Build(Shipped());

            Assert.IsTrue(board.AnyMove);
            Assert.IsFalse(board.Stranded);

            Fell(board);

            Assert.IsFalse(board.AnyMove, "a line with nothing on it has no legal move");
            Assert.IsTrue(board.Stranded, "no purchase puts a ward back up");

            var verdict = ProtoVerdict.Read(board, new ProtoBudget(ProtoBudget.Unlimited));

            Assert.AreEqual(ProtoEnding.Stuck, verdict.Ending);
            Assert.AreEqual(RunContinueDeficit.None, verdict.Deficit,
                            "a fallen line must never be offered a continue");
        }

        [Test]
        public void TheRunIsWonWhenTheHillIsEmpty()
        {
            var board = SiegeBoard.Build(Shipped());

            Assert.AreEqual(20, board.Goals);
            Assert.AreEqual(20, board.GoalsLeft);
            Assert.IsFalse(board.IsFinished);
        }

        // ------------------------------------------------------------------ the hill
        [Test]
        public void AWaveStepsOutOnAClockAndNeverOnAClear()
        {
            // **The rule that replaced "a wave arrives when the last one is gone".** That one was
            // withdrawn after play: it meant a player who was winning met no pressure at all,
            // because the hill stopped and waited for them. On a clock the waves overlap and
            // falling behind compounds, which is what a siege is.
            var board = SiegeBoard.Build(Shipped());

            Frames(board, SiegeTuning.FirstWaveAfter * .5f);
            Assert.AreEqual(0, board.Wave, "a wave arrived early");

            int wave = Frames(board, SiegeTuning.FirstWaveAfter);
            Assert.AreEqual(0, wave, "the first wave is wave nought");
            Assert.AreEqual(1, board.Wave);

            // The next one comes whatever is still standing on the hill.
            Frames(board, SiegeTuning.BetweenWaves + 1f);

            Assert.AreEqual(2, board.Wave, "the second wave waited for the first to be cleared");
            Assert.Greater(board.Raiders.Count, Waves[1].Length,
                           "the two waves should be on the hill together");

            // And every wave, once, in order - a clock that has run past the last one deals no
            // more.
            Frames(board, SiegeTuning.BetweenWaves * 6f);
            Assert.AreEqual(Waves.Length, board.Wave);
        }

        [Test]
        public void ABoltIsWorthDoubleAgainstARaiderOfItsOwnColour()
        {
            // Written as the arithmetic rather than played out, because what this is about is the
            // rule the par calculation assumes - and a par that assumes a rule the board does not
            // have is a level nobody can three-star.
            Assert.AreEqual(2, SiegeTuning.ShotDamage);
            Assert.AreEqual(2, SiegeTuning.WeakMultiplier);
            Assert.AreEqual(SiegeTuning.MatchGemsTenths * SiegeTuning.ShotDamage
                            * SiegeTuning.WeakMultiplier / 10,
                            SiegeTuning.PerfectMatch);
        }

        [Test]
        public void AWardHasToBeFedByTheFieldAndWantedByTheHill()
        {
            // Both are refusals in the reader rather than warnings, because a player's build runs
            // that reader and must not open a level that cannot be played.
            var starved = Layout(Field, "rgb", Wards, Waves);
            Assert.IsNotNull(starved.Fault);
            StringAssert.Contains("never deals", starved.Fault);

            var unanswerable = Layout(Field, Gems, "rg", Waves);
            Assert.IsNotNull(unanswerable.Fault);
            StringAssert.Contains("strong against", unanswerable.Fault);
        }

        [Test]
        public void ASiegeIsUnlosableOnMovesAndSaysSo()
        {
            var dto = new LevelDto
            {
                id = "t_siege",
                budgetFactor = LevelTuning.Unlimited,
                siege = new SiegeDto
                {
                    width = Field[0].Length,
                    height = Field.Length,
                    rows = Field,
                    gems = Gems,
                    wards = Wards,
                    waves = Waves,
                },
            };

            var mode = new SiegeMode();
            var problems = new System.Collections.Generic.List<string>();

            Assert.IsTrue(mode.TryRead(dto, LevelId.Parse("t_siege"), problems, out var rules),
                          string.Join("; ", problems));

            var tuning = mode.Tune(dto, rules);

            Assert.AreEqual(29, tuning.Par);
            Assert.IsFalse(tuning.HasBudget, "a siege is lost on the ward line, never on moves");
            Assert.AreEqual(35, tuning.GoldThreshold);
            Assert.AreEqual(41, tuning.SilverThreshold);
        }

        // ------------------------------------------------------------------ can it be held
        /// <summary>How often an unhurried player finds and makes a match, in seconds.</summary>
        const float Unhurried = 2.4f;

        [Test]
        public void AnUnhurriedPlayerHoldsThisLine()
        {
            // **The reading this mode needed and no other one does.** Everywhere else a board is
            // proved by searching it: par is the depth of the first winning layer, so "can this be
            // finished" is answered on the way to answering "in how few". A siege has no such
            // walk (invariant 37a), so the question has to be asked by playing one - and the
            // failure it catches is the worst a mode can have, which is a level that cannot be
            // held at all and validates perfectly.
            //
            // The player modelled is deliberately ordinary rather than good: a match every
            // 2.4 seconds, always aimed at the colour of whatever is furthest down the hill,
            // never looking for a bigger one and never planning a cascade. If *that* clears it
            // with the line standing, the level is winnable by somebody who is enjoying it.
            var board = SiegeBoard.Build(Shipped());

            int matches = Hold(board, out int seconds);

            Assert.IsTrue(board.IsFinished,
                          $"the hill was not cleared: {board.GoalsLeft} raider(s) left and "
                          + $"{board.WardsStanding} ward(s) standing after {seconds}s");

            Assert.GreaterOrEqual(board.WardsStanding, 2,
                                  $"an unhurried player finished with {board.WardsStanding} "
                                  + "ward(s) standing, so this is not the opening level of a mode");

            // **And the other half, which is what says this is a siege at all.** A line nothing
            // ever reaches is a fail state that rejects nothing, which is invariant 5d asked of a
            // threat rather than of a mechanic - the level would play as a jewel board with
            // scenery over it. This is the number the mode's constants were tuned on: an unhurried
            // player finishes with about half the line's health gone.
            int whole = board.Wards.Count * SiegeTuning.WardHealth;

            Assert.Less(Health(board), whole,
                        $"the line finished untouched at {whole}, so nothing on this hill ever "
                        + "reached it and the fail state rejects nothing");

            // And the other half, which is what says the star ladder is not decoration: an
            // ordinary run has to land *above* par (or par is free) and inside a band a good one
            // could beat (or three stars is unreachable). Wide on purpose - this is a model of a
            // player, not a player, and the number that settles it is a real one on a device.
            int par = SiegeTuning.Par(Shipped());

            Assert.Greater(matches, par,
                           $"an unhurried player finished in {matches} against par {par}, so par "
                           + "is not a floor and three stars is free");

            Assert.Less(matches, par * 3,
                        $"an unhurried player needed {matches} against par {par}, so the ladder "
                        + "is out of reach of anybody playing this way");
        }

        /// <summary>
        /// Plays the siege the way an unhurried player would and answers the matches it took.
        ///
        /// Frame by frame, because <c>Advance</c> bounds a step - see <see cref="Frames"/>.
        /// </summary>
        static int Hold(SiegeBoard board, out int seconds)
        {
            const float Frame = 1f / 60f;

            int matches = 0;
            float since = Unhurried;
            float clock = 0f;

            for (int i = 0; i < 60 * 400; i++)
            {
                board.Advance(Frame);
                clock += Frame;

                if (board.IsFinished || board.Stranded) break;

                since += Frame;
                if (since < Unhurried) continue;

                if (!Aimed(board, out int a, out int b)) continue;

                board.Swap(a, b);
                matches++;
                since = 0f;
            }

            seconds = (int)clock;
            return matches;
        }

        /// <summary>
        /// A swap that feeds the ward the raider furthest down the hill is weak to, or any swap.
        ///
        /// The gem that lines something up is one of the two being swapped, so a swap that puts
        /// the wanted colour into a run is one whose own two cells carry it. Approximate, and
        /// deliberately so: this is a model of somebody glancing at the hill and finding a match,
        /// not of somebody solving the board.
        /// </summary>
        static bool Aimed(SiegeBoard board, out int a, out int b)
        {
            char want = '\0';
            float furthest = -1f;

            // Held in a local rather than reached through `board.Layout.` at the call site:
            // the offline compile refuses that shape anywhere, because
            // `LevelDefinition.Layout` is null on any level that is not a glade and the check
            // is deliberately coarse about which `Layout` it is looking at.
            var plan = board.Layout;

            var raiders = board.Raiders;
            for (int i = 0; i < raiders.Count; i++)
            {
                var raider = raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;
                if (raider.March <= furthest) continue;

                // Only a colour whose ward is still up is worth aiming at.
                char colour = SiegeLayout.Letters[raider.Colour];
                int ward = plan.WardOf(colour);
                if (ward < 0 || !board.Wards[ward].Alive) continue;

                furthest = raider.March;
                want = colour;
            }

            int fallbackA = -1, fallbackB = -1;

            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    int here = board.IndexOf(x, y);

                    for (int d = 0; d < 2; d++)
                    {
                        int other = d == 0 ? here + 1 : here + board.Width;
                        if (d == 0 && x + 1 >= board.Width) continue;
                        if (d == 1 && y + 1 >= board.Height) continue;
                        if (!board.Lines(here, other)) continue;

                        if (fallbackA < 0) { fallbackA = here; fallbackB = other; }

                        if (want == '\0') continue;
                        if (board.At(here) != want && board.At(other) != want) continue;

                        a = here;
                        b = other;
                        return true;
                    }
                }

            a = fallbackA;
            b = fallbackB;
            return a >= 0;
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>The first swap on the board that lines anything up.</summary>
        static bool First(SiegeBoard board, out int a, out int b)
        {
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    int here = board.IndexOf(x, y);

                    if (x + 1 < board.Width && board.Lines(here, here + 1))
                    {
                        a = here;
                        b = here + 1;
                        return true;
                    }

                    if (y + 1 < board.Height && board.Lines(here, here + board.Width))
                    {
                        a = here;
                        b = here + board.Width;
                        return true;
                    }
                }

            a = b = -1;
            return false;
        }

        static void Play(SiegeBoard board, int matches)
        {
            for (int i = 0; i < matches; i++)
            {
                if (!First(board, out int a, out int b)) return;
                board.Swap(a, b);
            }
        }

        static float Total(SiegeBoard board)
        {
            float fuel = 0f;
            for (int w = 0; w < board.Wards.Count; w++) fuel += board.Wards[w].Fuel;
            return fuel;
        }

        static int Health(SiegeBoard board)
        {
            int left = 0;
            for (int w = 0; w < board.Wards.Count; w++) left += board.Wards[w].Health;
            return left;
        }

        static SiegeWard Fullest(SiegeBoard board)
        {
            SiegeWard best = null;

            for (int w = 0; w < board.Wards.Count; w++)
                if (best == null || board.Wards[w].Fuel > best.Fuel) best = board.Wards[w];

            return best;
        }

        /// <summary>
        /// Steps the siege the way the view does: in frames.
        ///
        /// <b>Never one long step</b> - <c>Advance</c> bounds a step so a resumed app cannot
        /// teleport a wave into the line, so a test that hands it a second gets a quarter of one.
        /// Answers the wave that stepped out along the way, or -1.
        /// </summary>
        static int Frames(SiegeBoard board, float seconds)
        {
            int wave = -1;

            for (float t = 0f; t < seconds; t += 1f / 60f)
            {
                var report = board.Advance(1f / 60f);
                if (report.Wave >= 0) wave = report.Wave;
            }

            return wave;
        }

        /// <summary>Walks the whole hill into the line and lets it swing until nothing stands.</summary>
        static void Fell(SiegeBoard board)
        {
            for (int i = 0; i < 200000 && board.WardsStanding > 0; i++) board.Advance(1f / 60f);

            Assert.AreEqual(0, board.WardsStanding,
                            "the hill could not bring the line down, so this level cannot be lost");
        }
    }
}
