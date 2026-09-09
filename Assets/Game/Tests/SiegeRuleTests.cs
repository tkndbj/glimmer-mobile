using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
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
        // ------------------------------------------------------------------ the fixture siege
        /// <summary>
        /// One siege built for the rule tests, and deliberately not one of the shipped rungs.
        ///
        /// <para>
        /// <b>It was the shipped level, and then the chapter grew to ten.</b> What it is for now is
        /// pinning the <em>arithmetic</em> — par over a known hill, a warlord appended as the last
        /// wave, a bolt worth double against its own colour — which wants one board that never
        /// moves when the content does. What the content is held to is <see cref="Chapter"/>, and
        /// <see cref="EveryRungOfThisChapterCanBeHeld"/> plays every rung of it.
        /// </para>
        /// </summary>
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

        /// <summary>The boss the fixture level ends on. See <see cref="SiegeLayout.Boss"/>.</summary>
        const string Boss = "warlord:r";

        /// <summary>
        /// The fixture siege, dealing cogs like every shipped rung that sends a boss.
        ///
        /// <b>It dealt none, and that made it the one configuration this game never ships.</b>
        /// Every rung from the second on deals them, and a boss rung leans on them hardest — a
        /// ward that never ranks up gets a tenth less damage and a tenth more fuel out of every
        /// bolt it fires. That was survivable while a bolt was worth twenty; once a bolt was worth
        /// ten and a match bought twice as many (<see cref="SiegeTuning.FuelPerGemTenths"/>), a
        /// cog-free duel stopped being winnable by an ordinary player and this fixture was the
        /// only thing in the project that noticed — because it was the only thing shaped that way.
        /// <b>A fixture that is harder than anything shipped is not a stricter test, it is a
        /// different game.</b>
        /// </summary>
        const int Cogs = 3;

        static SiegeLayout Shipped() => Layout(Field, Gems, Wards, Waves, Boss, Cogs);

        static SiegeLayout Layout(string[] rows, string gems, string wards, string[] waves,
                                  string boss = null, int cogs = 0)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            return new SiegeLayout(grid, gems, wards, waves, boss, cogs);
        }

        // ------------------------------------------------------------------ the whole chapter
        /// <summary>One rung of Thornwatch, exactly as `Tools/chapters/s01_thornwatch.py` writes it.</summary>
        sealed class Rung
        {
            public readonly string Id, Gems, Wards, Boss;
            public readonly string[] Rows, Waves;
            public readonly int Cogs;

            public Rung(string id, string[] rows, string gems, string wards, string[] waves,
                        string boss, int cogs)
            {
                Id = id;
                Rows = rows;
                Gems = gems;
                Wards = wards;
                Waves = waves;
                Boss = boss;
                Cogs = cogs;
            }

            public SiegeLayout Built() => Layout(Rows, Gems, Wards, Waves, Boss, Cogs);
        }

        /// <summary>
        /// Every rung of the shipped chapter, held inline.
        ///
        /// <para>
        /// <b>Inline rather than read from the chapter body</b>, for the reason every
        /// <c>*LadderTests</c> in this project is: a fixture that loads JSON goes through
        /// <c>JsonUtility</c>, which is a native call, so the offline runner reports the whole file
        /// as "needs the Editor" and it becomes the one gate nobody runs on the way past.
        /// </para>
        /// <para>
        /// <b>And here it is doing a job nothing else can.</b> Every other mode proves a level by
        /// searching it; a siege has no search (invariant 37a), so the only way to know a rung can
        /// be held is to play one — which is what <see cref="EveryRungOfThisChapterCanBeHeld"/>
        /// does with the real rules, over all ten.
        /// </para>
        /// </summary>
        static readonly Rung[] Chapter =
        {
            new Rung("s01_firstwatch", new[] { "ryybgyyg", "bybgrgyy", "rbryyggr", "grgrgbbr", "yybgrrbg" }, "rgby", "rgby", new[] { "rgby", "rgbyrgby" }, "", 0),
            new Rung("s01_ironward", new[] { "brbybgrg", "yggrbbry", "r*byybgr", "yygbbgby", "bbryygyg" }, "rgby", "rgby", new[] { "rgbyrg", "rgbyrgby", "rgByrgby" }, "", 4),
            new Rung("s01_stonewatch", new[] { "gbrrgbgy", "rybbrbbr", "grgryyrr", "rbybybby", "ryybgrby" }, "rgby", "rgby", new[] { "rgbyRG", "rgbyRGby", "RGBYrgby" }, "blightcaller:b", 3),
            new Rung("s01_thornhollow", new[] { "gbrryrbb", "rygbrrbr", "bbgybggy", "ybygyybb", "gyrbbggr" }, "rgby", "rgby", new[] { "rrrgggbb", "YYYYrrrr", "GGBBYY" }, "", 3),
            new Rung("s01_warlordsgate", new[] { "ygrrbrgg", "ryyrgrrb", "bbggyyby", "brryrbyg", "ggrbggry" }, "rgby", "rgby", new[] { "rgbyrg", "rgbyRGby", "RGbyRG" }, "warlord:r", 3),
            new Rung("s01_bramblerun", new[] { "bbrgrbrg", "bggrbgyy", "rgyygrry", "brbybyyg", "yrbrgrrg" }, "rgby", "rgby", new[] { "RGby#rby", "RGbyRGby", "RGBYRG#gRG" }, "", 3),
            new Rung("s01_ashenfield", new[] { "yrbyrgyy", "brggrbby", "gbrgbyrr", "byrbgybg", "yrbbrrbg" }, "rgby", "rgby", new[] { "#bgyRGby", "RGBY#rG", "RGBYRG#y" }, "", 3),
            new Rung("s01_blackmarch", new[] { "bggbggyr", "rbybgrby", "ybyybryb", "brrgybgr", "bybggybr" }, "rgby", "rgby", new[] { "rgbyRGby", "RGBYRGby", "RGBYrg" }, "warbringer:g", 3),
            new Rung("s01_thornsiege", new[] { "gbyygryr", "rbgbrbry", "rgrbgybb", "yygryyrg", "bbrgyrgg" }, "rgby", "rgby", new[] { "rgbyRGby", "RGBY#rGBY", "RGBY#gRGB#b" }, "", 3),
            new Rung("s01_lastlight", new[] { "bbrgbrry", "bbggbgyg", "ryybrgrr", "ryrgbyby", "ggyrgyry" }, "rgby", "rgby", new[] { "rgbyRGby", "RGB#rY#gG", "RGBYRGby" }, "overlord:y", 3),
        };

        /// <summary>
        /// The first rungs, where a hill that never reaches the line is the design rather than a
        /// fault.
        ///
        /// Invariant 24's argument in the unit this mode is graded in: the worst moment to take a
        /// run away from somebody is while they are still working out what a match is <em>for</em>,
        /// and the rung after that is teaching them what a cog does. From here on a line that
        /// finishes untouched is a fail state that rejects nothing (invariant 5d asked of a threat).
        /// </summary>
        const int TeachingRungs = 4;

        [Test]
        public void TheFixtureSiegeReads()
        {
            var layout = Shipped();

            Assert.IsNull(layout.Fault, layout.Fault);
            Assert.AreEqual(4, layout.Wards.Length);

            // Four waves, and only three of them are authored: the warlord is appended, because
            // the last wave *is* the boss wave by rule rather than by where somebody typed it.
            Assert.AreEqual(4, layout.Waves.Length);
            Assert.AreEqual(3, layout.BossWave);
            Assert.AreEqual('r', layout.Boss);
            Assert.AreEqual(21, layout.RaiderCount);
        }

        [Test]
        public void TheFixtureSiegeIsParThirtySeven()
        {
            // 12 creepers at 200, 8 brutes at 480 and one warlord at 1800 is 8040, over what a
            // match delivers (220). `Tools/verify/siege.py` prints the same number from the same
            // arithmetic; if these two ever disagree, one of the constants moved in one file only.
            Assert.AreEqual(37, SiegeTuning.Par(Shipped()));
        }

        [Test]
        public void AWarlordIsTheLastWaveAndCannotBeAuthoredIntoAnother()
        {
            // The rule this whole shape rests on. A level says *whether* there is a boss and what
            // colour it wears; where it comes is not an authoring decision, so a siege cannot ship
            // with its finale in the middle of it.
            var layout = Layout(Field, Gems, Wards, new[] { "rr", "gg" }, "warlord:b");

            Assert.AreEqual(3, layout.Waves.Length);
            Assert.AreEqual(2, layout.BossWave, "a warlord is always the last wave");
            Assert.AreEqual("b", layout.Waves[layout.BossWave]);

            var none = Layout(Field, Gems, Wards, new[] { "rr" });

            Assert.IsFalse(none.HasBoss);
            Assert.AreEqual(-1, none.BossWave);
            Assert.AreEqual(1, none.Waves.Length);
        }

        [Test]
        public void AWarlordThisModeCannotDrawIsRefusedByNameRatherThanIgnored()
        {
            // Invariant 5f read the other way round: a level naming a boss the mode does not know
            // would otherwise index, validate and ship as a siege with no finale in it, and the
            // only symptom would be a wave that never comes.
            var bad = Layout(Field, Gems, Wards, new[] { "rr" }, "dragon:r");

            Assert.IsNotNull(bad.Fault, "an unknown boss has to be refused");
            StringAssert.Contains("warlord", bad.Fault);

            // **And so is the retired one-letter form**, which is the same rule pointed at content
            // written for a build that no longer exists: salvaging a red warlord out of "r" would
            // ship a fight nobody authored, and now that four bosses wear four verbs, which one it
            // salvaged would be a coin toss.
            var old = Layout(Field, Gems, Wards, new[] { "rr" }, "r");

            Assert.IsNotNull(old.Fault, "the retired one-letter boss form has to be refused");

            var upper = Layout(Field, Gems, Wards, new[] { "rr" }, "warlord:R");

            Assert.IsNotNull(upper.Fault, "a boss colour is lower case; case means nothing now");
        }

        [Test]
        public void NoWardOnTheLineIsStrongAgainstThisWarlordIsRefused()
        {
            // A warlord carries the health of four brutes, so answering it at half rate is a duel
            // nobody could finish - the arithmetic par assumes it is not so.
            var bad = Layout(Field, Gems, "rg", new[] { "rr" }, "warlord:b");

            Assert.IsNotNull(bad.Fault);
            StringAssert.Contains("warlord", bad.Fault);
        }

        [Test]
        public void ParIsTheHillsHealthOverWhatOneMatchDelivers()
        {
            // The claim the star lines rest on, written out: a match clears about five and a half
            // gems once cascades are counted, each spent as a bolt into the ward its target is
            // weak to. **Measured rather than reasoned about** - see SiegeTuning.MatchGemsTenths
            // for the version that was reasoned about and was nearly twice too generous.
            //
            // **Both sides of the division scaled by ten when ward ranks arrived, so nothing
            // graded moved.** `ShotDamage` is 20 rather than 2 and every raider's health went up
            // by the same ten, which is what makes a *ten per cent* rank step exact in integers -
            // see `SiegeTuning.ShotDamage`. Par is unchanged on every board.
            Assert.AreEqual(220, SiegeTuning.PerfectMatch);

            var layout = Layout(Field, Gems, Wards, new[] { "r" });
            Assert.AreEqual(1, SiegeTuning.Par(layout), "one creeper is under one match");

            layout = Layout(Field, Gems, Wards, new[] { "rrr" });
            Assert.AreEqual(3, SiegeTuning.Par(layout), "three creepers is 600 over 220");

            layout = Layout(Field, Gems, Wards, new[] { "R" });
            Assert.AreEqual(3, SiegeTuning.Par(layout), "a brute is 480, which is three matches");
        }

        // ------------------------------------------------------------------ the field
        [Test]
        public void TheFixtureFieldIsAuthoredSettled()
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
        public void TheFixtureFieldHasSomethingToDoOnIt()
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

            // The fuel is in flight - it lands when the motes do. See the test below.
            Frames(board, SiegeTuning.FuelLands(turn.Beats.Count) + .2f);

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
            var turn = board.Swap(a, b);

            Frames(board, SiegeTuning.FuelLands(turn.Beats.Count) + .2f);

            float before = Total(board);
            Assert.Greater(before, 0f);

            // Nothing is on the hill for the first couple of seconds, so there is nothing to
            // fire at and nothing may leave the tubes.
            for (int i = 0; i < 60; i++) board.Advance(1f / 60f);

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
        public void FuelReachesAWardWhenTheMotesDoAndNotWhenTheSwapIsMade()
        {
            // **The one thing a player could see going wrong here and no gate could.** A swap
            // resolves in an instant; its animation takes the better part of a second, and the
            // motes that carry the colour up to the line are the last part of it. Credited at the
            // swap, a ward opens fire before the gems it was paid for have even gone off -
            // reported from play in exactly those words. So the fuel is booked and lands on the
            // schedule the view really draws (`SiegeTuning.FuelLands`), and this is what says the
            // two still agree.
            var board = SiegeBoard.Build(Shipped());

            Assert.IsTrue(First(board, out int a, out int b), "no swap on this field");

            var turn = board.Swap(a, b);
            Assert.IsNotNull(turn);
            Assert.Greater(turn.Worth, 0);

            Assert.AreEqual(0f, Total(board), .0001f,
                            "a ward was fuelled on the frame of the swap, before anything had "
                            + "left the field");

            Assert.IsNotEmpty(board.Flying, "the match booked nothing");

            // Still nothing, right up to the moment the first motes arrive.
            Frames(board, SiegeTuning.FuelLands(0) - .1f);

            Assert.AreEqual(0f, Total(board), .0001f, "fuel arrived ahead of its motes");

            // And then it is there.
            Frames(board, .25f);

            Assert.Greater(Total(board), 0f, "fuel never arrived at all");
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

            // Long enough for every mote to have arrived, so this is about the fallen line and
            // not about the fuel still being in flight.
            Frames(board, SiegeTuning.FuelLands(turn.Beats.Count) + .5f);

            Assert.AreEqual(0f, Total(board), .001f, "a fallen line took fuel");
        }

        [Test]
        public void TheRunIsLostWhenTheLastWardFallsAndAContinueIsAnHonestSale()
        {
            var board = SiegeBoard.Build(Shipped());

            Assert.IsTrue(board.AnyMove);
            Assert.IsFalse(board.Stranded);

            Fell(board);

            Assert.IsFalse(board.AnyMove, "a line with nothing on it has no legal move");

            // **Over, and not stranded.** The two are different questions and this mode is the
            // first here to answer them differently: the run has ended, and a continue puts the
            // line back up with the hill exactly where it stood, so refusing the offer would
            // refuse a rescue to somebody who could still win (invariant 28f).
            Assert.IsFalse(board.Stranded, "a continue puts the line back up");

            var verdict = ProtoVerdict.Read(board, new ProtoBudget(ProtoBudget.Unlimited));

            Assert.AreEqual(ProtoEnding.Stuck, verdict.Ending);
            Assert.AreNotEqual(RunContinueDeficit.None, verdict.Deficit,
                               "a fallen line is a shortage a purchase really does fix");
            Assert.AreEqual(0, verdict.Deficit,
                            "there is no unusable allowance to clear first - the offer is the "
                            + "whole line");
        }

        [Test]
        public void AContinueRaisesEveryWardAtFullHealthAndLeavesTheHillWhereItStood()
        {
            var board = SiegeBoard.Build(Shipped());
            int wards = board.Wards.Count;

            Fell(board);

            int onTheHill = board.OnTheHill;
            int left = board.GoalsLeft;

            Assert.AreEqual(wards, board.Rally(ContinueLimits.DefaultWards),
                            "every fallen ward stands again");

            Assert.AreEqual(wards, board.WardsStanding);
            Assert.IsTrue(board.AnyMove, "the run carries on");

            for (int i = 0; i < wards; i++)
            {
                var ward = board.Wards[i];

                Assert.AreEqual(SiegeTuning.WardHealth, ward.Health,
                                "a line raised at anything less falls again in a breath");
                Assert.AreEqual(0f, ward.Fuel, .001f,
                                "fuel is damage, and a continue may buy a finish and never a grade");
                Assert.IsFalse(ward.Doused, "a raised ward is not still smothered");
            }

            // What "carry on from where you left off" has to mean when the thing that ended the
            // run was the line rather than the board.
            Assert.AreEqual(onTheHill, board.OnTheHill, "the hill did not move");
            Assert.AreEqual(left, board.GoalsLeft, "nothing was cleared for free");
        }

        [Test]
        public void ASecondRallyOnAStandingLineRaisesNothing()
        {
            var board = SiegeBoard.Build(Shipped());

            Fell(board);

            Assert.AreEqual(board.Wards.Count, board.Rally(ContinueLimits.DefaultWards));
            Assert.AreEqual(0, board.Rally(ContinueLimits.DefaultWards),
                            "a standing line has nothing to raise, so a second grant is silent");
        }

        [Test]
        public void ARaisedWardKeepsTheRankItsCogsBought()
        {
            var board = SiegeBoard.Build(Shipped());

            // Set by hand rather than played for, exactly as the overlord's sunder case does it:
            // what is pinned here is that a rally does not touch the rank, and how a rank is
            // earned is the cog cases' business.
            board.Wards[0].Rank = SiegeTuning.MaxRank;
            int rank = board.Wards[0].Rank;

            Fell(board);
            board.Rally(ContinueLimits.DefaultWards);

            Assert.AreEqual(rank, board.Wards[0].Rank,
                            "a rank is the one thing in this mode a player earns, and nothing "
                            + "took it away");
        }

        [Test]
        public void AContinuedSiegeIsChargedUpToTheTwoStarLineAndCanOnlyEverScoreOne()
        {
            // Invariant 23's promise, which every other mode gets for free because its fail state
            // *is* its graded counter. A siege is graded in matches and lost when its line falls,
            // so a run can arrive at the offer having spent almost nothing.
            const int silver = 20;

            Assert.AreEqual(16, RunContinue.Toll(5, silver),
                            "five matches against a two-star line of twenty owes fifteen to reach "
                            + "it and one more to be past it");

            Assert.AreEqual(0, RunContinue.Toll(21, silver),
                            "a run already past the line owes nothing");
            Assert.AreEqual(0, RunContinue.Toll(40, silver),
                            "and a second continue on the same run is never charged twice");

            Assert.AreEqual(0, RunContinue.Toll(0, 0),
                            "a level with no two-star line to be past owes nothing");
        }

        [Test]
        public void TheRunIsWonWhenTheHillIsEmpty()
        {
            var board = SiegeBoard.Build(Shipped());

            Assert.AreEqual(21, board.Goals);
            Assert.AreEqual(21, board.GoalsLeft);
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
            // Four, not three: the warlord is a wave of its own, appended after the authored
            // ones (see `SiegeLayout.Boss`).
            Assert.AreEqual(Waves.Length + 1, board.Wave);
            Assert.IsTrue(board.BossWave, "the last wave of this siege is the warlord's");
        }

        [Test]
        public void ABoltIsWorthDoubleAgainstARaiderOfItsOwnColour()
        {
            // Written as the arithmetic rather than played out, because what this is about is the
            // rule the par calculation assumes - and a par that assumes a rule the board does not
            // have is a level nobody can three-star.
            Assert.AreEqual(10, SiegeTuning.ShotDamage);
            Assert.AreEqual(2, SiegeTuning.WeakMultiplier);

            // And a rank is a tenth on top of it, which is only exact because of the scale above.
            Assert.AreEqual(SiegeTuning.ShotDamage, SiegeTuning.DamageAt(0));

            // **What a match delivers is counted in bolts, not gems**, and the two stopped being
            // the same thing when a gem started buying two bolts (`SiegeTuning.FuelPerGemTenths`).
            // Written out here as the arithmetic it has to be, because the old form — gems times
            // damage — would still have compiled, still have looked plausible, and would have
            // doubled every par in the chapter.
            Assert.AreEqual(SiegeTuning.MatchGemsTenths * SiegeTuning.FuelPerGemTenths
                            * SiegeTuning.ShotDamage * SiegeTuning.WeakMultiplier
                            / (SiegeTuning.FuelPerShotTenths * 10),
                            SiegeTuning.PerfectMatch);

            // And the whole point of the change: it is the number it always was. A gem worth twice
            // the fuel and a bolt worth half the damage is the same match delivering the same
            // damage over twice as many bolts, so no par, no star line and no utility charge moves.
            Assert.AreEqual(220, SiegeTuning.PerfectMatch);
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

        // ------------------------------------------------------------------ the warlord
        /// <summary>
        /// A siege that is nothing but the duel: no authored waves at all, so the warlord is the
        /// whole hill.
        ///
        /// <b>Legal content, and used here because it is the only way to watch a warlord without
        /// a hill full of raiders taking the line apart underneath it.</b> A level shipping this
        /// shape is warned about (one wave, and it never lets up), which is the right answer for a
        /// level and no answer at all for a fixture.
        /// </summary>
        static SiegeLayout Duel() => Layout(Field, Gems, Wards, new string[0], "warlord:r");

        /// <summary>Walks the clock until the warlord is standing on its ground.</summary>
        static SiegeRaider Warlord(SiegeBoard board)
        {
            for (int i = 0; i < 60 * 120; i++)
            {
                board.Advance(1f / 60f);

                var boss = board.Warlord;
                if (boss != null && boss.InPlace) return boss;
            }

            Assert.Fail("the warlord never reached its ground");
            return null;
        }

        [Test]
        public void AWarlordHoldsTheMiddleOfTheHillAndNeverReachesTheLine()
        {
            // **The whole shape of the fight.** Everything else on this hill is answered by
            // killing it before it arrives; a warlord stops where nothing can stop it and hits the
            // line from there, so the pressure it applies cannot be outrun - only out-damaged.
            var board = SiegeBoard.Build(Duel());
            var boss = Warlord(board);

            Assert.AreEqual(SiegeTuning.BossHold, boss.March, .001f);
            Assert.IsFalse(boss.AtTheLine, "a warlord must never stand at the line");
            Assert.AreEqual(0, SiegeTuning.BlowOf(SiegeKind.Boss), "a warlord swings at nothing");

            // And it stays there: a hundred more seconds of clock move it no further.
            for (int i = 0; i < 60 * 100; i++) board.Advance(1f / 60f);

            Assert.AreEqual(SiegeTuning.BossHold, board.Warlord.March, .001f);
        }

        [Test]
        public void AWarlordWalksOnBeforeItStands()
        {
            // The view wears a *walk* until this is true and an idle after it (`SiegeView.Follow`),
            // so the moment it changes is a drawing decision as much as a rules one — it shipped
            // wearing the idle for the whole walk-in and came back from play as "it looks like it
            // is floating". Pinned here because the walk-in is a *duration*, and a duration nobody
            // asserts is one that drifts the next time the pacing is retuned.
            var board = SiegeBoard.Build(Duel());

            float clock = 0f;
            SiegeRaider boss = null;

            for (int i = 0; i < 60 * 120; i++)
            {
                board.Advance(1f / 60f);
                clock += 1f / 60f;

                boss = board.Warlord;
                if (boss == null) continue;

                if (boss.InPlace) break;

                Assert.Less(boss.March, SiegeTuning.BossHold,
                            "a warlord short of its ground is still walking");
            }

            Assert.IsNotNull(boss);
            Assert.IsTrue(boss.InPlace, "the warlord never reached its ground");

            // It steps out with the wave and walks `BossHold` of the hill at its own march.
            float walk = SiegeTuning.BossHold * SiegeTuning.BossMarch;

            Assert.AreEqual(SiegeTuning.FirstWaveAfter + walk, clock, .2f,
                            $"the walk-in is {clock - SiegeTuning.FirstWaveAfter:0.0}s, and it is "
                            + "the stretch a player spends watching the biggest thing in the mode "
                            + "cross the hill");

            Assert.Less(walk, 4.5f,
                        "a warlord that takes longer than this to get into place reads as slow - "
                        + "it was 10.1s, then 6.0s, and the owner has asked for it faster twice");
        }

        [Test]
        public void AWarlordsSpellIsTelegraphedBeforeItLands()
        {
            // Invariant 37s: a move's effect may not land before its animation does. The board
            // reports the cast when it is *decided* and takes the ward's health a whole tell and
            // flight later, which is the window a mending is worth spending in.
            var board = SiegeBoard.Build(Duel());
            Warlord(board);

            float waited = 0f;
            SiegeCast cast = default;

            for (int i = 0; i < 60 * 60 && cast.In <= 0f; i++)
            {
                var report = board.Advance(1f / 60f);
                if (report.Casts.Count > 0) cast = report.Casts[0];
            }

            Assert.Greater(cast.In, 0f, "the warlord never cast");
            Assert.AreEqual(SiegeTuning.BossTell + SiegeTuning.BossFlight, cast.In, .001f);

            int before = board.Wards[cast.Ward].Health;

            // Nothing has landed yet, and the ward it named is untouched for the whole tell.
            for (int i = 0; i < (int)(60 * SiegeTuning.BossTell); i++)
            {
                board.Advance(1f / 60f);
                waited += 1f / 60f;
                Assert.AreEqual(before, board.Wards[cast.Ward].Health,
                                $"the spell landed {cast.In - waited:0.00}s early");
            }

            bool landed = false;
            for (int i = 0; i < 60 && !landed; i++)
                landed = board.Advance(1f / 60f).Spells.Count > 0;

            Assert.IsTrue(landed, "the spell never arrived");
            Assert.AreEqual(before - SiegeTuning.BossCast, board.Wards[cast.Ward].Health);
        }

        [Test]
        public void AWarlordThrowsAtTheFreshestWardStanding()
        {
            // The rule that keeps the fight winnable: a warlord that finished off whatever was
            // nearly down would take the line apart one ward at a time, and the ward it would
            // reach first is the one whose colour answers it.
            var board = SiegeBoard.Build(Duel());
            Warlord(board);

            for (int i = 0; i < 60 * 300; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int c = 0; c < report.Casts.Count; c++)
                {
                    int chosen = report.Casts[c].Ward;

                    for (int w = 0; w < board.Wards.Count; w++)
                    {
                        if (!board.Wards[w].Alive) continue;

                        Assert.LessOrEqual(board.Wards[w].Health, board.Wards[chosen].Health,
                                           $"ward {w} was fresher than the one it threw at");
                    }
                }

                if (board.WardsStanding == 0) return;
            }

            Assert.Fail("the warlord never brought the line down on its own");
        }

        /// <summary>A duel against whichever of the four is asked for.</summary>
        static SiegeLayout Duel(string boss)
            => Layout(Field, Gems, Wards, new string[0], boss);

        /// <summary>Walks the clock until this board's boss is standing on its ground.</summary>
        static SiegeRaider Standing(SiegeBoard board)
        {
            for (int i = 0; i < 60 * 180; i++)
            {
                board.Advance(1f / 60f);

                var boss = board.Warlord;
                if (boss != null && boss.InPlace) return boss;
            }

            Assert.Fail("the boss never reached its ground");
            return null;
        }

        /// <summary>
        /// The four bosses take four different things, and no two of them take the same one.
        ///
        /// <b>This is the whole claim four bosses are for.</b> A chapter shipped two told apart by
        /// their health, their cadence and their hue — every reading green, every gate green, and
        /// a player's verdict was that they looked and played exactly the same. What separates a
        /// kind from a number is that each one has a <em>different answer</em>, so what is pinned
        /// here is that the four spells are four verbs rather than one verb at four strengths.
        /// </summary>
        [Test]
        public void EachOfTheFourBossesTakesADifferentThing()
        {
            Assert.AreEqual(SiegeSpell.Douse, SiegeTuning.SpellOf(SiegeKind.Blightcaller));
            Assert.AreEqual(SiegeSpell.Smite, SiegeTuning.SpellOf(SiegeKind.Boss));
            Assert.AreEqual(SiegeSpell.Rally, SiegeTuning.SpellOf(SiegeKind.Warbringer));
            Assert.AreEqual(SiegeSpell.Sunder, SiegeTuning.SpellOf(SiegeKind.Overlord));

            // Only the blightcaller takes no health at all, which is what stopped "there is a
            // boss" being a fact the build gate could act on (`ModeValidator.Threatens`).
            Assert.AreEqual(0, SiegeTuning.CastOf(SiegeKind.Blightcaller));
            Assert.Greater(SiegeTuning.CastOf(SiegeKind.Overlord), SiegeTuning.CastOf(SiegeKind.Boss));

            // A warbringer's is the smallest of the three that do, because it lands on every ward
            // rather than on one - the same total spread flat instead of concentrated.
            Assert.Less(SiegeTuning.CastOf(SiegeKind.Warbringer), SiegeTuning.CastOf(SiegeKind.Boss));

            // And only three of the four aim at a ward. A roar is thrown at the ground.
            Assert.IsFalse(SiegeTuning.AimsAtAWard(SiegeKind.Warbringer));
            Assert.IsTrue(SiegeTuning.AimsAtAWard(SiegeKind.Blightcaller));

            // A blightcaller cannot bring a ward down however long it stands there, so a level
            // whose only threat were one could not be lost - which the gate now says out loud.
            Assert.IsFalse(SiegeTuning.EndangersTheLine(SiegeKind.Blightcaller));
            Assert.IsTrue(SiegeTuning.EndangersTheLine(SiegeKind.Warbringer),
                          "a warbringer walks to the line and swings there");
        }

        [Test]
        public void ABlightcallerPutsAWardOutAndTakesNoHealth()
        {
            var board = SiegeBoard.Build(Duel("blightcaller:g"));
            Standing(board);

            // Fuel in every tube, so the douse has something to take and the pick is not decided
            // by an empty line.
            for (int w = 0; w < board.Wards.Count; w++) board.Surge(w, 20);

            int whole = 0;
            for (int w = 0; w < board.Wards.Count; w++) whole += board.Wards[w].Health;

            bool doused = false;

            for (int i = 0; i < 60 * 120 && !doused; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int s = 0; s < report.Spells.Count; s++)
                {
                    var spell = report.Spells[s];
                    Assert.AreEqual(SiegeSpell.Douse, spell.Craft);
                    Assert.AreEqual(0, spell.Damage, "a douse takes no health");

                    var ward = board.Wards[spell.Ward];
                    Assert.IsTrue(ward.Doused, "the ward it landed on is out");
                    Assert.AreEqual(0f, ward.Fuel, 1e-4f, "and its fuel went with its fire");
                    Assert.IsFalse(ward.Fuelled, "a doused ward cannot fire whatever is in it");

                    doused = true;
                }
            }

            Assert.IsTrue(doused, "the blightcaller never cast");

            int left = 0;
            for (int w = 0; w < board.Wards.Count; w++) left += board.Wards[w].Health;

            Assert.AreEqual(whole, left, "no health may leave the line to a blightcaller");
        }

        [Test]
        public void ASurgeLiftsADouseBecauseThatIsWhatMakesItTheAnswer()
        {
            // Invariant 39's rule about what a utility may sell, asked of the one boss a mending
            // cannot answer: fuel poured into a ward that cannot fire is fuel spent on nothing
            // until the dark runs out on its own, which would be an item charged for a delay.
            var board = SiegeBoard.Build(Duel("blightcaller:g"));
            Standing(board);

            for (int w = 0; w < board.Wards.Count; w++) board.Surge(w, 20);

            int hit = -1;

            for (int i = 0; i < 60 * 120 && hit < 0; i++)
            {
                var report = board.Advance(1f / 60f);
                for (int s = 0; s < report.Spells.Count; s++) hit = report.Spells[s].Ward;
            }

            Assert.GreaterOrEqual(hit, 0, "the blightcaller never cast");
            Assert.IsTrue(board.Doused(hit));

            board.Surge(hit, 20);

            Assert.IsFalse(board.Wards[hit].Doused, "a surge re-lights a doused ward");
            Assert.IsTrue(board.Wards[hit].Fuelled);
        }

        [Test]
        public void AWarbringerShakesTheWholeLineFromWhereItStands()
        {
            // **It used to walk to the line, and that was withdrawn after play** — a boss that
            // takes ground spends the fight being somewhere else, and the report was that it took
            // forever to get anywhere and start doing damage. It holds the middle like the other
            // three now, and what makes it a different fight is that its roar lands on *every*
            // ward rather than picking one.
            var board = SiegeBoard.Build(Duel("warbringer:y"));
            var boss = Standing(board);

            float ground = boss.Hold;

            Assert.Less(ground, 1f, "it stops short of the line");
            Assert.AreEqual(0, SiegeTuning.BlowOf(SiegeKind.Warbringer),
                            "no boss in this mode reaches the line, so none of them swings");

            var before = new int[board.Wards.Count];
            for (int w = 0; w < board.Wards.Count; w++) before[w] = board.Wards[w].Health;

            var hit = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < 60 * 60 && hit.Count == 0; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int s2 = 0; s2 < report.Spells.Count; s2++)
                {
                    var spell = report.Spells[s2];
                    Assert.AreEqual(SiegeSpell.Rally, spell.Craft);
                    Assert.AreEqual(SiegeTuning.WarbringerCast, spell.Damage);
                    hit.Add(spell.Ward);
                }
            }

            Assert.AreEqual(board.Wards.Count, hit.Count,
                            "one roar lands on every standing ward, not on one of them");

            for (int w = 0; w < board.Wards.Count; w++)
                Assert.AreEqual(before[w] - SiegeTuning.WarbringerCast, board.Wards[w].Health,
                                "ward " + w + " felt the roar");

            Assert.Greater(board.Roaring, 0f, "and the hill is charging");

            // **And it does not move.** This is the whole of what was reported: it stayed put
            // rather than closing on the line one roar at a time.
            Assert.AreEqual(ground, boss.Hold, 1e-4f, "a boss holds its ground");
            Assert.AreEqual(SiegeTuning.WarbringerHold, boss.Hold, 1e-4f);

            // Left alone it still brings the line down - it just does it from where it stands,
            // which is what keeps `ModeValidator.Threatens` able to count it.
            Assert.IsTrue(SiegeTuning.EndangersTheLine(SiegeKind.Warbringer));

            for (int i = 0; i < 60 * 400 && board.WardsStanding > 0; i++) board.Advance(1f / 60f);

            Assert.AreEqual(0, board.WardsStanding,
                            "a warbringer left alone shakes the line apart from the middle");
        }

        [Test]
        public void AnOverlordTakesTheRankAPlayerEarned()
        {
            // The finale attacks the one thing in this chapter a player *earned* (invariant 37w),
            // which is what makes where the cogs went a question the last rung asks.
            var board = SiegeBoard.Build(Duel("overlord:b"));
            Standing(board);

            // A line with one turret plainly the best on it, so the pick is not a tie.
            board.Wards[1].Rank = SiegeTuning.MaxRank;

            bool sundered = false;

            for (int i = 0; i < 60 * 120 && !sundered; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int s = 0; s < report.Spells.Count; s++)
                {
                    var spell = report.Spells[s];
                    Assert.AreEqual(SiegeSpell.Sunder, spell.Craft);
                    Assert.AreEqual(1, spell.Ward, "it hunts the best turret on the line");
                    Assert.IsTrue(spell.Sundered);
                    Assert.Greater(spell.Damage, 0, "and takes health as well");

                    sundered = true;
                }
            }

            Assert.IsTrue(sundered, "the overlord never cast");
            Assert.AreEqual(SiegeTuning.MaxRank - SiegeTuning.OverlordSunder, board.Wards[1].Rank);

            // Never below nought, and it says so rather than reporting a rank it did not take.
            board.Wards[1].Rank = 0;
            Assert.IsFalse(board.Wards[1].Sunder());
            Assert.AreEqual(0, board.Wards[1].Rank);
        }


        /// <summary>
        /// A firepot hits the body a player is aiming at, not the box its feet are in.
        ///
        /// <b>Reported from play as "the bombs don't hit bosses".</b> They did — they hit its feet.
        /// A raider is about a cell tall, so where it stands and what it looks like are the same
        /// box; a boss is three cells on a four-row hill, so most of the thing being aimed at is in
        /// the box above the one it occupies, and a firepot dropped on its chest took nothing.
        /// </summary>
        [Test]
        public void AFirepotHitsABosssBodyAndNotOnlyItsFeet()
        {
            var board = SiegeBoard.Build(Duel("warlord:r"));
            var boss = Standing(board);

            int feet = SiegeTuning.RowOf(boss.March);
            int lane = boss.Lane;

            Assert.Greater(feet, 0, "this test needs a boss with a row above its feet");
            Assert.AreEqual(3, SiegeTuning.RowsOf(boss.Kind));
            Assert.AreEqual(1, SiegeTuning.RowsOf(SiegeKind.Creeper),
                            "an ordinary raider is where it stands and nowhere else");

            // Its chest, one row up the hill from its feet: the box a player aiming at the thing
            // they can see would actually tap.
            int before = boss.Health;
            int took = board.Blast(lane, feet - 1, 400, null);

            Assert.Greater(took, 0, "a firepot on a boss's body has to hit it");
            Assert.Less(boss.Health, before);

            // And its feet, which always worked.
            Assert.Greater(board.Blast(lane, feet, 400, null), 0);

            // Not the whole lane, though: a blast is still aimed, and the ground below a boss is
            // ground it is not standing on.
            if (feet + 1 < SiegeTuning.BlastRows)
                Assert.AreEqual(0, board.Blast(lane, feet + 1, 400, null),
                                "the box below a boss's feet is not part of it");

            // Nor the lane beside it.
            int aside = lane > 0 ? lane - 1 : lane + 1;
            Assert.AreEqual(0, board.Blast(aside, feet - 1, 400, null),
                            "a blast is still aimed at one lane");
        }

        /// <summary>
        /// Every boss reaches further than the box it stands in, and no ordinary raider does.
        ///
        /// <c>SiegeTuning.Caught</c> is asked by <c>SiegeBoard.Blast</c> and drawn by nothing, so
        /// this is the only thing that holds it to what the board looks like.
        /// </summary>
        [Test]
        public void ABossIsCaughtByABlastAnywhereItsBodyReaches()
        {
            foreach (var kind in new[] { SiegeKind.Blightcaller, SiegeKind.Boss,
                                         SiegeKind.Warbringer, SiegeKind.Overlord })
            {
                float hold = SiegeTuning.HoldOf(kind);
                int feet = SiegeTuning.RowOf(hold);

                Assert.IsTrue(SiegeTuning.Caught(kind, hold, feet), kind + " at its own feet");

                if (feet > 0)
                    Assert.IsTrue(SiegeTuning.Caught(kind, hold, feet - 1),
                                  kind + " one row up, which is its chest");

                if (feet + 1 < SiegeTuning.BlastRows)
                    Assert.IsFalse(SiegeTuning.Caught(kind, hold, feet + 1),
                                   kind + " does not reach below its own feet");
            }

            // The shape that must not change: a creeper is one box, wherever it is standing.
            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                float march = (row + .5f) / SiegeTuning.BlastRows;
                for (int other = 0; other < SiegeTuning.BlastRows; other++)
                    Assert.AreEqual(other == row,
                                    SiegeTuning.Caught(SiegeKind.Creeper, march, other));
            }
        }

        [Test]
        public void ASpellWhoseCasterIsGoneFizzles()
        {
            // A ward coming down to something thrown by a warlord the player had already beaten
            // reads as the game getting the last word, and it is also what makes killing one
            // mid-wind-up worth something.
            var board = SiegeBoard.Build(Duel());
            var boss = Warlord(board);

            SiegeCast cast = default;
            for (int i = 0; i < 60 * 60 && cast.In <= 0f; i++)
            {
                var report = board.Advance(1f / 60f);
                if (report.Casts.Count > 0) cast = report.Casts[0];
            }

            Assert.Greater(cast.In, 0f, "the warlord never cast");

            int before = board.Wards[cast.Ward].Health;
            boss.Health = 0;
            boss.Alive = false;

            for (int i = 0; i < 60 * 3; i++)
                Assert.AreEqual(0, board.Advance(1f / 60f).Spells.Count,
                                "a spell landed after its caster had been destroyed");

            Assert.AreEqual(before, board.Wards[cast.Ward].Health);
        }

        [Test]
        public void AWarlordIsAnOrdinaryGoal()
        {
            // Which is what makes it cost the save file nothing (invariant 20a): its record, its
            // stars, its rewards and its merge are the ones every other raider on this hill has,
            // and a run is not won while it is standing because it is simply still on the count.
            Assert.AreEqual(21, Shipped().RaiderCount, "the warlord is one of the goals");

            var duel = SiegeBoard.Build(Duel());

            Assert.AreEqual(1, duel.Goals);
            Assert.AreEqual(1, duel.GoalsLeft);
            Assert.IsFalse(duel.IsFinished);

            Warlord(duel);
            Assert.IsFalse(duel.IsFinished, "a warlord standing on the hill is a goal left");
        }



        // ------------------------------------------------------------------ can it be held
        /// <summary>How often an unhurried player finds and makes a match, in seconds.</summary>
        const float Unhurried = 2.4f;

        [Test]
        public void AnUnhurriedPlayerHoldsTheFixtureLine()
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

            // And the other half, which is what says the star ladder is not decoration: a good
            // run has to land *inside* it.
            //
            // **Not "above par", which is what this asserted first and had to give up.** Par was
            // written as a floor and is not one (see SiegeTuning.MatchGemsTenths); calibrated, it
            // sits at about what a good run really costs, so a model of a good player ties it. The
            // question worth asking is therefore not whether par is beaten but whether the three
            // lines derived from it are all landable: a good run scoring three stars, and par not
            // sitting so far above real play that the bands are meaningless.
            int par = SiegeTuning.Par(Shipped());
            int gold = (par * 120 + 99) / 100;

            Assert.LessOrEqual(matches, gold,
                               $"an unhurried player needed {matches} against a three-star line "
                               + $"of {gold}, so nobody playing this way ever sees three stars");

            Assert.GreaterOrEqual(matches * 2, par,
                                  $"an unhurried player finished in {matches} against par {par}, "
                                  + "so par is more than twice what the level really costs and "
                                  + "every band under it is unreachable");
        }

        [Test]
        public void EveryRungOfThisChapterReads()
        {
            Assert.AreEqual(10, Chapter.Length, "Thornwatch ships ten rungs");

            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (var rung in Chapter)
            {
                Assert.IsTrue(seen.Add(rung.Id), rung.Id + " is in this chapter twice");

                var layout = rung.Built();
                Assert.IsNull(layout.Fault, rung.Id + ": " + layout.Fault);

                Assert.LessOrEqual(rung.Cogs, SiegeLayout.MaxCogRate, rung.Id);

                Assert.GreaterOrEqual(SiegeTuning.Par(layout), 6,
                                      rung.Id + " is over before anything this mode is built on "
                                      + "gets to bite");
            }
        }

        /// <summary>
        /// <b>The one thing about this mode that no gate can answer and only a played run can.</b>
        ///
        /// <para>
        /// Everywhere else par is the depth of a breadth-first walk, so "can this be finished" is
        /// answered on the way to "in how few". A siege has no such walk (invariant 37a), so the
        /// question has to be asked by playing one - and the failure it catches is the worst a mode
        /// can have, which is a level that cannot be held at all and validates perfectly. It caught
        /// exactly that on this mode's first level, and it caught two rungs of this chapter while
        /// the cogs were being tuned.
        /// </para>
        /// <para>
        /// The player modelled is deliberately ordinary rather than good: a match every 2.4
        /// seconds, always aimed at the colour of whatever is furthest down the hill, never looking
        /// for a bigger one and never planning a cascade. If <em>that</em> clears a rung with the
        /// line standing, the rung is winnable by somebody who is enjoying it.
        /// </para>
        /// </summary>



        /// <summary>
        /// <b>A continue that does not continue is a charge</b> (invariant 23), and this mode is
        /// the one where that could not be reasoned about.
        ///
        /// <para>
        /// Everywhere else a continue hands over allowance on a board that has stopped moving, so
        /// "is it enough to be worth buying" is a question about the number. Here the hill is
        /// still walking and every raider the run let through is standing at the line with its
        /// hammer up — so the line is raised into whatever was killing it, and how long that
        /// lasts is a fact about the level rather than about the offer.
        /// </para>
        /// <para>
        /// Measured with <b>nobody playing at all</b>, which is the floor rather than the case: an
        /// unhurried player makes a match every 2.4 seconds and a ward is firing about a second
        /// after the first of them, so the real window is longer and gets longer as it is used.
        /// Ten rungs come out between 10.4 and 13.5 seconds, which is four or five matches before
        /// a finger is lifted. The bar is eight — well under what ships, and it fails the moment
        /// anything makes a rallied line cheap: a partial raise, a smaller
        /// <see cref="SiegeTuning.WardHealth"/>, or a rung whose hill piles up harder than any of
        /// these.
        /// </para>
        /// </summary>
        [Test]
        public void ARalliedLineStandsLongEnoughToBeWorthBuying()
        {
            const float Least = 8f;

            for (int i = 0; i < Chapter.Length; i++)
            {
                var rung = Chapter[i];
                var board = SiegeBoard.Build(rung.Built());

                Fell(board);

                int hill = board.OnTheHill;
                int left = board.GoalsLeft;

                Assert.AreEqual(board.Wards.Count, board.Rally(ContinueLimits.DefaultWards),
                                rung.Id + ": the whole line comes back");

                // Nothing was handed over except the line: the hill stands where it stood, which
                // is what makes this a continue rather than a fresh board.
                Assert.AreEqual(hill, board.OnTheHill, rung.Id + ": the hill moved");
                Assert.AreEqual(left, board.GoalsLeft, rung.Id + ": a raider was cleared for free");

                int frames = 0;
                while (frames < 60 * 120 && board.WardsStanding > 0)
                {
                    board.Advance(1f / 60f);
                    frames++;
                }

                float stood = frames / 60f;

                Assert.GreaterOrEqual(stood, Least,
                                      $"{rung.Id}: a rallied line stood {stood:0.0}s against "
                                      + $"{hill} raider(s) already at it, which is not long enough "
                                      + "to be worth twenty gems");
            }
        }

        [Test]
        public void AnUnhurriedPlayerHoldsThisLine()
        {
            for (int i = 0; i < Chapter.Length; i++)
            {
                var rung = Chapter[i];
                var layout = rung.Built();
                var board = SiegeBoard.Build(layout);

                int matches = Hold(board, out int seconds);

                Assert.IsTrue(board.IsFinished,
                              $"{rung.Id}: the hill was not cleared - {board.GoalsLeft} raider(s) "
                              + $"left and {board.WardsStanding} ward(s) standing after {seconds}s");

                Assert.GreaterOrEqual(board.WardsStanding, 2,
                                      $"{rung.Id}: an unhurried player finished with "
                                      + $"{board.WardsStanding} ward(s) standing");

                int par = SiegeTuning.Par(layout);
                int gold = (par * 120 + 99) / 100;

                Assert.LessOrEqual(matches, gold,
                                   $"{rung.Id}: an unhurried player needed {matches} against a "
                                   + $"three-star line of {gold}, so nobody playing this way ever "
                                   + "sees three stars");

                Assert.GreaterOrEqual(matches * 2, par,
                                      $"{rung.Id}: an unhurried player finished in {matches} "
                                      + $"against par {par}, so par is more than twice what the "
                                      + "rung really costs and every band under it is unreachable");

                // **And the half that says this is a siege at all.** A line nothing ever reaches is
                // a fail state that rejects nothing, which is invariant 5d asked of a threat rather
                // than of a mechanic - the rung would play as a jewel board with scenery over it.
                // The teaching rungs are the deliberate exception (see `TeachingRungs`).
                if (i < TeachingRungs) continue;

                int whole = board.Wards.Count * SiegeTuning.WardHealth;

                Assert.Less(Health(board), whole,
                            $"{rung.Id}: the line finished untouched at {whole}, so nothing on "
                            + "this hill ever reached it");
            }
        }

        /// <summary>
        /// Every rung after the first deals cogs, and the first deals none.
        ///
        /// <b>A fact about the ramp rather than about the rules</b>, and it is here because it is
        /// the one thing a content edit could quietly undo: a cog on the opening rung would put a
        /// second object on the field while somebody is still working out what a match is for, and
        /// no cog anywhere after it would leave a mechanic, its art, its lesson and its badge
        /// shipped and unreachable.
        /// </summary>
        [Test]
        public void TheOpeningRungDealsNoCogsAndEveryRungAfterItDoes()
        {
            Assert.AreEqual(0, Chapter[0].Cogs, "the opening rung teaches the verb and nothing else");

            for (int i = 1; i < Chapter.Length; i++)
                Assert.Greater(Chapter[i].Cogs, 0, Chapter[i].Id + " deals no cogs");
        }

        /// <summary>
        /// A cog is taken by a run of gems beside it, and the colour of that run decides the ward.
        ///
        /// The mechanic's whole decision, pinned on a board built for it: a cog with one colour
        /// beside it goes to that colour's ward and to no other.
        /// </summary>
        [Test]
        public void ACogIsTakenByTheColourThatMatchesBesideIt()
        {
            // A checkerboard - so nothing lines up by accident - with three reds arranged so
            // that one vertical swap closes a run directly under the cog, and nothing else.
            var layout = Layout(new[]
            {
                "by*ybyby",
                "yrbrybyb",
                "byrybyby",
                "ybybybyb",
            }, "rgby", "rgby", new[] { "rgby" }, null, 0);

            Assert.IsNull(layout.Fault, layout.Fault);

            var board = SiegeBoard.Build(layout);

            int red = layout.WardOf('r');
            Assert.AreEqual(0, board.Wards[red].Rank);

            int a = board.IndexOf(2, 1), b = board.IndexOf(2, 2);
            Assert.IsTrue(board.Lines(a, b), "the fixture no longer lines a red run up");

            var turn = board.Swap(a, b);
            Assert.IsNotNull(turn);

            int rises = 0;
            foreach (var beat in turn.Beats)
                foreach (var rise in beat.Rises)
                {
                    rises++;
                    Assert.AreEqual(red, rise.Ward, "a red run gave its cog to another ward");
                    Assert.IsTrue(rise.Rose);
                }

            Assert.AreEqual(1, rises, "the cog beside the run was not taken");
            Assert.AreEqual(1, board.Wards[red].Rank);
            Assert.AreEqual(2, board.Wards[red].Level, "the badge and the rank disagree");
        }

        /// <summary>
        /// What a rank is worth: ten per cent more damage and ten per cent less fuel a bolt, both
        /// of them exact in integers.
        /// </summary>
        [Test]
        public void ARankIsWorthATenthOfADamageAndATenthOfAFuel()
        {
            Assert.AreEqual(10, SiegeTuning.DamageAt(0));
            Assert.AreEqual(11, SiegeTuning.DamageAt(1));
            Assert.AreEqual(12, SiegeTuning.DamageAt(2));
            Assert.AreEqual(13, SiegeTuning.DamageAt(3));
            Assert.AreEqual(14, SiegeTuning.DamageAt(4));

            // **The fuel half of a rank did not move when the fuel unit was subdivided**, which is
            // the whole reason it was done that way round: halving the *bolt* instead would have
            // made this ladder 5, 4, 4, 3, 3 after truncation, so two of the four cogs a player
            // spends would have bought nothing at all.
            Assert.AreEqual(10, SiegeTuning.FuelShotTenths(0));
            Assert.AreEqual(6, SiegeTuning.FuelShotTenths(SiegeTuning.MaxRank));

            Assert.AreEqual(SiegeTuning.DamageAt(SiegeTuning.MaxRank), SiegeTuning.DamageAt(99));
            Assert.AreEqual(SiegeTuning.FuelShotTenths(0), SiegeTuning.FuelShotTenths(-1));
        }

        /// <summary>
        /// A cog never lines up with the cog beside it.
        ///
        /// The failure this refuses is the one every mirror of a match-three in this project has
        /// met: a cell that is not a colour compared to another cell that is not a colour.
        /// </summary>
        [Test]
        public void ThreeCogsInARowAreNotAMatch()
        {
            var layout = Layout(new[]
            {
                "***ybyby",
                "yrbrybyb",
                "byrybyby",
                "ybybybyb",
            }, "rgby", "rgby", new[] { "rgby" }, null, 0);

            Assert.IsNull(layout.Fault, layout.Fault);
        }

        /// <summary>An overlord is the greater warlord, and a level names it by kind.</summary>
        [Test]
        public void AnOverlordIsAWarlordInUpperCase()
        {
            var lesser = Layout(Field, Gems, Wards, new string[0], "warlord:r");
            var greater = Layout(Field, Gems, Wards, new string[0], "overlord:r");

            Assert.IsNull(lesser.Fault, lesser.Fault);
            Assert.IsNull(greater.Fault, greater.Fault);

            Assert.AreEqual(SiegeKind.Boss, lesser.BossKind);
            Assert.AreEqual(SiegeKind.Overlord, greater.BossKind);

            Assert.AreEqual(SiegeTuning.BossHealth, SiegeTuning.HealthOf(SiegeKind.Boss));
            Assert.AreEqual(SiegeTuning.OverlordHealth, SiegeTuning.HealthOf(SiegeKind.Overlord));

            Assert.Greater(SiegeTuning.OverlordHealth, SiegeTuning.BossHealth);
            Assert.Greater(SiegeTuning.OverlordCast, SiegeTuning.BossCast);
            Assert.Less(SiegeTuning.OverlordCastEvery, SiegeTuning.BossCastEvery);

            // It stops further up the hill, which is the compensation for all three.
            Assert.Less(SiegeTuning.OverlordHold, SiegeTuning.BossHold);

            // And par takes it with no special case at all: a boss is a wave (invariant 37t).
            Assert.Greater(SiegeTuning.Par(greater), SiegeTuning.Par(lesser));
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

            // Long enough for the whole level and then some: three waves on a 26-second clock, a
            // warlord walking into place behind them, and the duel that follows.
            for (int i = 0; i < 60 * 600; i++)
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
