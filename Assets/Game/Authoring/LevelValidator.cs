using System.Collections.Generic;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Proves a level is solvable before anyone ever sees it.
    ///
    /// A puzzle game ships a broken level exactly once before it costs a week of
    /// reviews, so this runs over every level on every build and blocks the build
    /// on any error. It is pure and Editor-free by design: the same code guards the
    /// build, the authoring tool and any test.
    ///
    /// <para>
    /// In <c>GlimmerGrove.Authoring</c>, which is Editor-only. It used to be in Domain and
    /// therefore in every player build, and what held it there was one word: <c>virtual</c> on
    /// <c>LevelMode.Validate</c>. A level's checks were reached through the mode, and the mode
    /// reached back into this file, so the pair could only live where the runtime could see
    /// both. <see cref="ModeValidator"/> cut that cycle — a mode declares what it is in Domain
    /// and how it is proved here, exactly as it already declares how it looks in Presentation.
    /// </para>
    /// </summary>
    public static class LevelValidator
    {
        /// <summary>
        /// Proves a level is worth shipping, by asking its own mode.
        ///
        /// <para>
        /// <b>No branch per mode.</b> Each mode brings its own checks (see
        /// <see cref="ModeValidator"/>); this runs them and adds the checks every level shares
        /// whatever it is played on. A mode added tomorrow is validated without this file being
        /// opened — which is what stops it turning into the switch it used to be, where "not a
        /// board, therefore a hollow" was true until it silently was not.
        /// </para>
        /// <para>
        /// <b>A mode with no registered checks is an error rather than a pass.</b> That is the
        /// one thing this dispatch must never do quietly: "nothing was checked" and "everything
        /// checked out" are the same green tick on every screen that reports this, so the
        /// distinction has to be made here or it is never made at all.
        /// </para>
        /// </summary>
        public static LevelValidationReport Validate(LevelDefinition level)
        {
            var issues = new List<LevelIssue>();

            if (LevelModes.Find(level.Mode) == null)
            {
                Error(issues, $"nothing in this build knows how to play a '{level.Mode}' level");
                return new LevelValidationReport(level.Id, issues, 0);
            }

            var checks = ModeValidators.Of(level.Mode);
            if (checks == null)
            {
                Error(issues, $"nothing in this build knows how to check a '{level.Mode}' level, " +
                              "so it would ship unproven — register a ModeValidator for it");
                return new LevelValidationReport(level.Id, issues, 0);
            }

            checks.Validate(level, issues);
            CheckPresentation(level, issues);

            return new LevelValidationReport(level.Id, issues,
                                             level.HasBoard ? level.Tuning.Par : 0);
        }

        /// <summary>
        /// Everything a conduit board has to prove. Called by <c>GladeMode</c> rather than from
        /// <see cref="Validate"/>, so the classic mode's checks belong to the classic mode.
        /// </summary>
        internal static void ValidateGlade(LevelDefinition level, List<LevelIssue> issues)
        {
            var parsed = LevelGridParser.Parse(level.Layout);
            foreach (var e in parsed.Errors) Error(issues, e);
            if (!parsed.Ok) return;

            var cells = parsed.Cells;
            int computedPar = PuzzleFactory.MinimumMoves(cells);

            CheckPopulation(cells, issues);
            CheckArmsMate(level, cells, issues);
            CheckRootedTiles(level, cells, issues);
            CheckAuthoredSolution(level, cells, issues);
            CheckFragileConduits(level, cells, issues);
            CheckBoundConduits(level, cells, issues);
            CheckCrossings(level, cells, issues);
            CheckDecidableTiles(level, cells, issues);
            CheckPar(level, computedPar, issues);
            CheckStarBands(level, issues);
        }

        /// <summary>
        /// Proves the three lines a run is measured against are ordered and all landable.
        ///
        /// <para>
        /// Three stars, two stars and the end of the run are separate multiples of par
        /// (<c>LevelTuning</c>), and nothing in the type stops them being set into an order
        /// that makes a band impossible to land in. <b>That failure is silent in the way this
        /// project keeps paying for</b>: every number stays individually plausible, the level
        /// validates, the board is winnable, and a whole star band simply stops existing.
        /// </para>
        /// <para>
        /// It has already happened once. The budget was cut to <c>par × 1.60</c> while the
        /// two-star line was still <c>par × 2.00</c>, which put the two-star line *outside* the
        /// survivable range — a run still alive had spent fewer turns than the budget, so every
        /// clear was worth two stars or three and one star could never be scored by anybody.
        /// Nothing said so. This is what says so.
        /// </para>
        /// <para>
        /// The severities are not the same, because the two failures are not. A budget at or
        /// under the three-star line is an <b>error</b>: no run can be graded at all, which is
        /// a broken level rather than a tuning opinion. A budget inside the two-star band is a
        /// <b>warning</b>: the glade is playable and the grading is coherent, it just spends a
        /// third of its ladder on a band nobody can reach — the same "rejects nothing, so it is
        /// decoration" reading invariant 5d applies to mechanics.
        /// </para>
        /// <para>
        /// <b>Asked by every mode that has a fail line, not only by the glade.</b> Lightweave
        /// took one when its ink arrived (invariant 22a), and it is the same three numbers over
        /// the same par — so it asks this rather than carrying a copy, which is what stops the
        /// two drifting the next time <c>LevelTuning</c> is retuned.
        /// </para>
        /// <para>
        /// <b>It reads the factors, not the thresholds it derives.</b> That is not a shortcut,
        /// it is the difference between a check and a nuisance: the thresholds are
        /// <c>ceil(par × factor)</c>, so on a board of par 1 or 2 all three round onto the same
        /// number however the factors are set, and a check on thresholds would report a tuning
        /// fault whose real cause is that the board has two turns in it. Such a board is
        /// already reported by <c>CheckPar</c>. The factors are what an author writes and what
        /// a live retune moves, so they are what this is about.
        /// </para>
        /// </summary>
        internal static void CheckStarBands(LevelDefinition level, List<LevelIssue> issues)
        {
            var tuning = level.Tuning;

            // Compared as hundredths, which is what the thresholds are actually derived from
            // (LevelTuning.GoldHundredths). Comparing the floats would let this pass a pair
            // the grading then treats as equal, or fail one it treats as ordered — a check
            // that disagrees with the thing it checks is worse than no check.
            if (tuning.GoldHundredths >= tuning.SilverHundredths)
                Error(issues, $"goldFactor is {tuning.GoldFactor:0.##} and silverFactor is " +
                              $"{tuning.SilverFactor:0.##}, so the two-star band is empty — " +
                              "three stars must ask for fewer turns than two");

            // An unbudgeted glade cannot strand a band: every run is graded, and the bottom
            // band is simply everything past silver. The first glade in the game is one.
            if (!tuning.HasBudget) return;

            // A budget measured in moves rather than as a multiple of par is compared as moves.
            // Reading the factors here would be reading a number the mode does not use, and a
            // check that disagrees with the thing it checks is worse than no check at all — the
            // same reason the branch below reads the factors rather than the thresholds they
            // derive. See LevelTuning.Slack.
            if (tuning.Slack > 0)
            {
                if (tuning.MoveBudget <= tuning.GoldThreshold)
                    Error(issues, $"the run ends after {tuning.MoveBudget} and three stars is " +
                                  $"{tuning.GoldThreshold}, so no run can be graded — give it " +
                                  "more room above par");
                else if (tuning.MoveBudget <= tuning.SilverThreshold)
                    Warn(issues, $"the run ends after {tuning.MoveBudget} and two stars is " +
                                 $"{tuning.SilverThreshold}, so one star can never be scored: " +
                                 "every clear is worth two or three");
                return;
            }

            if (tuning.BudgetHundredths <= tuning.GoldHundredths)
                Error(issues, $"the run ends at par × {tuning.BudgetFactor:0.##} and three stars " +
                              $"is par × {tuning.GoldFactor:0.##}, so no run can be graded — " +
                              "raise budgetFactor or lower goldFactor");
            else if (tuning.BudgetHundredths <= tuning.SilverHundredths)
                Warn(issues, $"the run ends at par × {tuning.BudgetFactor:0.##} and two stars is " +
                             $"par × {tuning.SilverFactor:0.##}, so one star can never be " +
                             "scored: every clear is worth two or three");
        }

        /// <summary>
        /// Validates a set of levels. Takes the definitions rather than a catalog
        /// because validation is an Editor pass over content that is already in hand —
        /// the game never has every level loaded at once, and a signature implying it
        /// could would invite exactly that.
        /// </summary>
        public static List<LevelValidationReport> ValidateAll(IEnumerable<LevelDefinition> levels)
        {
            var reports = new List<LevelValidationReport>();
            if (levels == null) return reports;

            foreach (var level in levels) reports.Add(Validate(level));
            return reports;
        }

        // ------------------------------------------------------------- the checks
        static void CheckPopulation(Cell[] cells, List<LevelIssue> issues)
        {
            int sources = 0, lamps = 0, turnable = 0;
            foreach (var c in cells)
            {
                if (c.kind == Kind.Source) sources++;
                else if (c.kind == Kind.Lamp) lamps++;
                if (c.kind != Kind.Empty && !c.locked && !Puzzle.Alike(c, 1)) turnable++;
            }

            if (sources == 0) Error(issues, "no heart-crystal, nothing can ever light up");
            if (lamps == 0) Error(issues, "no sleeping critters, the level can never be won");
            if (turnable == 0) Warn(issues, "no tile can be turned, the player has nothing to do");
        }

        /// <summary>
        /// A crossing has to actually cross something.
        ///
        /// <para>
        /// The mechanic's whole promise is that the two flows through a tile are different
        /// flows. When the authored solution joins them somewhere else on the board the tile
        /// is telling the player a lie in the one place the game asks them to trust their
        /// eyes — and it is a lie that costs turns, because they will route around a
        /// separation that was never there. Invisible everywhere else: the arms mate, the
        /// solution lights, par comes out a sensible number, and the board draws beautifully.
        /// </para>
        /// <para>
        /// A warning rather than an error, and the distinction is real. Two strands meeting
        /// elsewhere is sometimes exactly the shape a finale wants — a loop that leaves by one
        /// arm and comes back by another has to close somewhere. So this is a question about
        /// intent, which is what warnings are for here.
        /// </para>
        /// </summary>
        static void CheckCrossings(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            var solved = Copy(cells);
            for (int i = 0; i < solved.Length; i++) solved[i].rot = 0;

            Puzzle probe = null;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind != Kind.Crossing) continue;

                probe ??= new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, solved);

                if (probe.EnergyOn(i, 0) == 0 && probe.EnergyOn(i, 1) == 0)
                {
                    Warn(issues, $"neither strand of the crossing at {i % w},{i / w} carries any " +
                                 "light in the authored solution, so nothing on the board ever " +
                                 "demonstrates that it keeps two flows apart");
                    continue;
                }

                // Joined elsewhere: the two strands end up in one network anyway, so the tile
                // looks like it is separating something it is not.
                if (probe.Comp(i, 0) == probe.Comp(i, 1))
                    Warn(issues, $"the two strands of the crossing at {i % w},{i / w} are joined " +
                                 "elsewhere in the authored solution, so it crosses nothing; " +
                                 "the player will route around a separation that is not there");
            }
        }

        /// <summary>
        /// A tile the arms can never settle has to be settled by something else.
        ///
        /// <para>
        /// A crossing and a briar both wear all four arms at every angle, so every neighbour
        /// mates them however they are turned and <b>nothing about the pipe-fitting says which
        /// way either one goes</b>. That is exactly why they are the two tiles worth authoring
        /// with (invariant 5d) and exactly how they fail: if the glade is still won with one of
        /// them turned a step off its solution, the player has a tile they cannot place by
        /// looking, no reason on the board to place it either way, and a par that charged them
        /// for it. It is invisible everywhere else — the arms mate, the solution lights, par
        /// comes out a sensible number and the board draws beautifully.
        /// </para>
        /// <para>
        /// <b>It asks the consequence rather than the topology, and that is the whole change.</b>
        /// This replaces a check that asked only whether <em>lifting</em> a briar's thorns would
        /// join two networks, which was wrong in both directions. It missed the tile that
        /// separates two networks of compatible colour — turn it, join them, and no critter goes
        /// out. And it fired on the shape that replaced the duskcap (invariant 5f), where the
        /// open pair feeds a pocket carrying a heart of its own: both thorned ways lead back
        /// into the grove, so the old reading called it decoration, while turning it strands the
        /// pocket and puts its critter out. Turning the tile and reading <see cref="Puzzle.Won"/>
        /// answers both, costs one board evaluation per four-armed tile, and cannot drift from
        /// what the player experiences because it <em>is</em> what the player experiences.
        /// </para>
        /// <para>
        /// Three things are skipped and each for a reason. A <b>straight</b> crossing is
        /// <see cref="Puzzle.Alike"/> at every angle — architecture, and Stonebridge roots four
        /// of them on purpose. A <b>rooted</b> tile cannot be turned at all, so it decides
        /// nothing by construction and saying so would be noise. And the whole check is skipped
        /// unless the authored solution wins, because on a board where it does not,
        /// <see cref="CheckAuthoredSolution"/> has the real complaint and this would bury it.
        /// </para>
        /// <para>
        /// A <b>warning</b> rather than an error, deliberately: the glade that teaches what a
        /// briar is may legitimately carry one as scenery, which is the first board of a mode
        /// and nowhere else. The build gate fails on errors, so this is a design reading — but
        /// it is counted in <c>Validate Content</c>'s summary, and <c>BriarTests</c> drives it
        /// from both sides, because a check with no failing case is not a check.
        /// </para>
        /// </summary>
        static void CheckDecidableTiles(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width, h = level.Layout.Height;

            var solved = Copy(cells);
            for (int i = 0; i < solved.Length; i++) solved[i].rot = 0;

            Puzzle probe = null;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind != Kind.Crossing && cells[i].kind != Kind.Briar) continue;
                if (cells[i].locked) continue;
                if (Puzzle.Alike(cells[i], 1)) continue;

                if (probe == null)
                {
                    probe = new Puzzle(level.Id, w, h, level.Tuning, Copy(solved));
                    if (!probe.Won) return;
                }

                // A fresh board each time: Turn writes the rotation back into the cell array it
                // was handed, so one probe cannot be reused and then restored by turning it
                // round. `wear: false` because a brittle briar must not crumble under a check.
                var turned = new Puzzle(level.Id, w, h, level.Tuning, Copy(solved));
                turned.Turn(i, 1, wear: false);
                turned.Evaluate();

                if (!turned.Won) continue;

                string what = cells[i].kind == Kind.Briar ? "briar" : "crossing";
                string why = cells[i].kind == Kind.Briar && !ThornsSeparate(probe, cells[i], i)
                    ? " — every way it has leads back into one network, so the thorns are shutting" +
                      " a door onto the room they are already in"
                    : " — the two things it holds apart are answering the same colour, so joining" +
                      " them costs no critter anything";

                Warn(issues, $"turning the {what} at {i % w},{i / w} one step from its solution " +
                             $"still finishes the glade, so nothing on this board settles it{why}");
            }
        }

        /// <summary>
        /// Whether taking a briar's thorns off would join two different networks.
        ///
        /// <para>
        /// No longer the rule — <see cref="CheckDecidableTiles"/> is — but kept as the *reason*
        /// attached to that rule's warning, because it is the commonest cause and the most
        /// actionable one. Only the thorned ways are asked about: the open pair is the network
        /// the tile is already in, so it can never disagree with itself. The way has to be open
        /// on the far side too, or lifting these thorns would still join nothing, which is what
        /// two briars back to back are.
        /// </para>
        /// </summary>
        static bool ThornsSeparate(Puzzle probe, in Cell cell, int i)
        {
            int mine = probe.Comp(i, 0);

            for (int d = 0; d < 4; d++)
            {
                if ((cell.gate & Puzzle.Bits[d]) != 0) continue;
                if ((cell.solved & Puzzle.Bits[d]) == 0) continue;

                int j = probe.Neighbour(i, d);
                if (j < 0) continue;

                int back = (d + 2) & 3;
                if ((probe.Live(j) & Puzzle.Bits[back]) == 0) continue;

                if (probe.Comp(j, probe.StrandAt(j, back)) != mine) return true;
            }

            return false;
        }

        /// <summary>Every arm must point at a neighbour whose arm points back.</summary>
        static void CheckArmsMate(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width, h = level.Layout.Height;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind == Kind.Empty) continue;
                int x = i % w, y = i / w;

                for (int d = 0; d < 4; d++)
                {
                    if ((cells[i].solved & Puzzle.Bits[d]) == 0) continue;

                    int nx = x + Puzzle.Step[d].x, ny = y + Puzzle.Step[d].y;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    {
                        Error(issues, $"arm at {x},{y} points off the board");
                        continue;
                    }

                    int j = ny * w + nx;
                    if (cells[j].kind == Kind.Empty)
                    {
                        Error(issues, $"arm at {x},{y} points at an empty cell");
                        continue;
                    }


                    if ((cells[j].solved & Puzzle.Bits[(d + 2) & 3]) == 0)
                        Error(issues, $"arm at {x},{y} is not mated by its neighbour at {nx},{ny}");
                }
            }
        }

        /// <summary>
        /// The authored orientation must actually be a winning board.
        ///
        /// Judged by building a real <see cref="Puzzle"/> and asking it, rather than by
        /// reimplementing the rules here. That is what stops the validator and the game
        /// from ever disagreeing about what "solved" or "detonated" means — a drift that
        /// would ship a level nobody can finish.
        /// </summary>
        static void CheckAuthoredSolution(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            var solved = new Cell[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                solved[i] = cells[i];
                solved[i].rot = 0;
            }

            var probe = new Puzzle(level.Id, level.Layout.Width, level.Layout.Height, level.Tuning, solved);

            if (probe.LampsLit != probe.LampCount)
                Error(issues, $"the authored solution lights only {probe.LampsLit} of {probe.LampCount} critters");
        }

        /// <summary>
        /// Taproots must be able to reach their own solution, and be worth having.
        ///
        /// <para>
        /// The error is the same shape as the brittle-conduit one and exists for the same
        /// reason: a root whose conduits can never all be right at once is a level nobody
        /// can finish, and it looks perfectly authored — every arm mates, the solved board
        /// lights, par comes out a plausible number. The player would simply lose hearts
        /// against it for ever. It is cheap to prove, because one tap turns the whole root:
        /// either some offset in 0..3 solves every member or none does.
        /// </para>
        /// <para>
        /// A rune only one conduit carries is an error rather than a shrug. It draws a
        /// binding mark on a tile that is bound to nothing, which is a promise the board
        /// makes to the player and does not keep — and the overwhelmingly likely cause is a
        /// partner that was mistyped, which is worth stopping a build for.
        /// </para>
        /// </summary>
        static void CheckBoundConduits(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;
            var probe = new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, Copy(cells));

            for (int rune = 1; rune <= Puzzle.MaxRunes; rune++)
            {
                int members = 0, movable = 0, first = -1;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].link != rune) continue;
                    members++;
                    if (first < 0) first = i;
                    if (!probe.InertAlone(i)) movable++;
                }

                if (members == 0) continue;

                char letter = (char)('A' + rune - 1);

                if (members == 1)
                {
                    Error(issues, $"taproot '{letter}' has only the conduit at " +
                                  $"{first % w},{first / w} on it; a root of one wears a binding " +
                                  "mark and binds nothing");
                    continue;
                }

                if (PuzzleFactory.RootTurnsOwed(cells, rune) < 0)
                {
                    Error(issues, $"the conduits on taproot '{letter}' can never all be right at " +
                                  "once — no number of turns solves every one of them, so the " +
                                  "glade cannot be finished");
                    continue;
                }

                if (movable == 0)
                    Warn(issues, $"every conduit on taproot '{letter}' looks the same in every " +
                                 "orientation, so turning the root can never matter");
            }

            // Past this the marks stop telling the roots apart, and two roots wearing one
            // identity is worse than no mark at all — the player reads a binding that is not
            // there. Said out loud rather than clamped quietly; the drawing reads the same
            // number, so the two cannot drift.
            if (probe.RootCount > Puzzle.MaxReadableRunes)
                Warn(issues, $"this board carries {probe.RootCount} taproots but a mark can only " +
                             $"tell {Puzzle.MaxReadableRunes} of them apart; split the glade in two " +
                             "or merge some of the roots");
        }


        /// <summary>
        /// Fragile conduits must be able to reach their own solution.
        ///
        /// This is the check that keeps the mechanic honest. A conduit authored three
        /// turns from solved but able to survive only two is a level nobody can finish —
        /// and unlike most authoring mistakes it looks completely fine, because every
        /// arm mates and the solved board lights perfectly. The player would simply lose
        /// hearts against it forever.
        ///
        /// Cheap to prove: turns owed at the opening rotation is exactly how many turns
        /// the tile needs, and its count is exactly how many it has.
        /// </summary>
        /// <summary>
        /// A rooted tile has to start in an orientation that already reads as solved.
        ///
        /// <para>
        /// This is the one check that guards the other checks. Every proof below runs
        /// against a copy of the board with every rotation set to zero, because that is
        /// the authored solution — but a rooted tile can never be turned, so a rooted
        /// tile authored away from zero is a tile the player is stuck with at an angle
        /// the proof never sees. What is proved is then a different board from the one
        /// that ships, and nothing else here can notice: every arm mates, the solved
        /// probe lights, par is unaffected because <see cref="PuzzleFactory.MinimumMoves"/>
        /// skips rooted tiles, and the glade draws perfectly.
        /// </para>
        /// <para>
        /// It also breaks the one promise <see cref="Puzzle.TurnsToSolution"/> makes.
        /// That count includes rooted tiles, so one stuck off its solution adds turns
        /// that can never be paid: a player who has in fact reached the solution is told
        /// they were one turn away, which is the near-miss line being generous — the
        /// single thing it exists not to be.
        /// </para>
        /// <para>
        /// Asked as <see cref="Puzzle.Alike"/> rather than as <c>rot == 0</c>, because a
        /// straight conduit and a straight crossing genuinely read the same half a turn
        /// round and refusing those would be refusing a tile that is already correct.
        /// </para>
        /// </summary>
        static void CheckRootedTiles(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].locked || cells[i].kind == Kind.Empty) continue;
                if (Puzzle.Alike(cells[i], cells[i].rot)) continue;

                Error(issues, $"the rooted tile at {i % w},{i / w} starts {PuzzleFactory.TurnsOwed(cells[i])} " +
                              "turn(s) from its solution and can never be turned, so the board that was " +
                              "proved solvable is not the board the player gets; author it at /0");
            }
        }

        static void CheckFragileConduits(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            var probe = new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, Copy(cells));

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].fragile == 0) continue;

                int x = i % w, y = i / w;

                if (cells[i].locked)
                {
                    Warn(issues, $"the fragile conduit at {x},{y} is also rooted, so it can never be " +
                                 "turned and never crumbles — one of the two is a mistake");
                    continue;
                }

                if (probe.Inert(i))
                {
                    Warn(issues, $"the conduit at {x},{y} looks the same in every orientation, so " +
                                 "turning it is pointless and its fragility can never matter");
                    continue;
                }

                int owed = probe.TurnsOwed(i);
                if (owed > cells[i].fragile)
                    Error(issues, $"the fragile conduit at {x},{y} needs {owed} turn(s) to reach its " +
                                  $"solution but survives only {cells[i].fragile}; it would crumble on the " +
                                  "way and lose the glade");
            }
        }

        static Cell[] Copy(Cell[] cells)
        {
            var copy = new Cell[cells.Length];
            for (int i = 0; i < cells.Length; i++) copy[i] = cells[i];
            return copy;
        }

        static void CheckPar(LevelDefinition level, int computedPar, List<LevelIssue> issues)
        {
            if (computedPar == 0)
            {
                Warn(issues, "the board starts already solved, par would be zero");
                return;
            }
            if (level.Tuning.Par != computedPar)
                Warn(issues, $"par is {level.Tuning.Par} but the board needs {computedPar} turns; " +
                             "omit par in the content file to derive it automatically");
        }

        static void CheckPresentation(LevelDefinition level, List<LevelIssue> issues)
        {
            var p = level.Presentation.MapPosition;
            if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)
                Warn(issues, $"map position {p} falls outside the 0..1 strip and will not be visible");
        }

        static void Error(List<LevelIssue> issues, string message)
            => issues.Add(new LevelIssue(LevelIssueSeverity.Error, message));

        static void Warn(List<LevelIssue> issues, string message)
            => issues.Add(new LevelIssue(LevelIssueSeverity.Warning, message));
    }
}
