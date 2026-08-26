using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// How a mode is proved fit to ship.
    ///
    /// <para>
    /// <b>A mode is now declared three times, and the third one is this.</b> What it <em>is</em>
    /// lives in <see cref="LevelMode"/> (Domain — its rules, its parser, its tuning). What it
    /// <em>looks like</em> lives in <c>ModeLook</c> (Presentation — its screen, its perch, its
    /// colour), split off because Domain may never reference Presentation. This is the same split
    /// made once more for the same kind of reason: a mode's checks run on a build machine and
    /// never on a phone, so they belong in an assembly no player installs.
    /// </para>
    /// <para>
    /// <b>It was a <c>virtual</c> on <see cref="LevelMode"/>, and that one word shipped the whole
    /// validator.</b> <c>LevelValidator</c> is six hundred lines that prove a board is solvable,
    /// that its arms mate, that its taproots bind, that its star bands are landable — none of
    /// which a player's device has any use for, because content is proved on the machine that
    /// builds it. It could not move while it was reached through a member of a class the runtime
    /// does use: the authoring entry point called into the mode, and the mode called back into
    /// the authoring entry point, so the pair had to live wherever the runtime could see them.
    /// Cutting that cycle is the whole of this file.
    /// </para>
    /// <para>
    /// <b>An unregistered mode is an error, which is the opposite of what <c>ModeLooks</c>
    /// does.</b> A mode missing a <em>look</em> draws as the classic one, because a map with an
    /// odd-looking node is a better failure than a map that will not open. A mode missing a
    /// <em>validator</em> must never fall back to anything, because the fallback would be
    /// "validated nothing" and would be indistinguishable, on every screen and in every log, from
    /// content that passed. So <see cref="LevelValidator.Validate"/> reports it, and
    /// <c>ModeValidatorTests</c> refuses a build where any shipped mode is missing one — the
    /// check exists because the failure is silent, which is the same reason invariant 20h exists
    /// one file over.
    /// </para>
    /// </summary>
    public abstract class ModeValidator
    {
        public abstract GameMode Mode { get; }

        /// <summary>
        /// Proves a level of this mode is worth shipping, adding to <paramref name="issues"/>.
        /// </summary>
        public abstract void Validate(LevelDefinition level, List<LevelIssue> issues);
    }

    /// <summary>
    /// Every mode's checks, registered once.
    ///
    /// Mirrors <see cref="LevelModes"/> and <c>ModeLooks</c>. Registering a mode here is the
    /// third and last thing adding a mode costs, and the suite names it if you forget.
    /// </summary>
    public static class ModeValidators
    {
        static readonly ModeValidator[] _all =
        {
            new GladeValidator(),
            new FallValidator(),
            new KeeperValidator(),
            new WeaveValidator(),
        };

        public static IReadOnlyList<ModeValidator> All => _all;

        /// <summary>This mode's checks, or null when nothing has been registered for it.</summary>
        public static ModeValidator Of(GameMode mode)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Mode.Equals(mode)) return _all[i];
            return null;
        }
    }

    /// <summary>
    /// The classic glade. Its checks are the bulk of <see cref="LevelValidator"/> and stay there
    /// rather than being poured into this file: they are what that file is about, and six hundred
    /// lines do not become better organised by being moved under a different heading.
    /// </summary>
    sealed class GladeValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Glade;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
            => LevelValidator.ValidateGlade(level, issues);
    }

    /// <summary>A well: nothing to solve, so only its shape can be wrong.</summary>
    sealed class FallValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Fall;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var well = (FallRules)level.Rules;

            if (well.Width < 4 || well.Width > 8)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"a well is 4..8 wide; this one is {well.Width}"));

            if (well.Height < 6 || well.Height > 14)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"a well is 6..14 tall; this one is {well.Height}"));
        }
    }

    /// <summary>A keeper's grove: the ground, and how many tiles the run is dealt onto it.</summary>
    sealed class KeeperValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Keeper;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (KeeperRules)level.Rules;

            if (grove.Width < 5 || grove.Width > 11 || grove.Height < 5 || grove.Height > 11)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"a grove is 5..11 each way; this one is {grove.Width}x{grove.Height}"));

            // A run that hands out more tiles than there is ground for ends by having nowhere
            // legal to place, which reads to a player as the game freezing rather than finishing.
            if (grove.Tiles >= grove.Width * grove.Height)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"{grove.Tiles} tiles for {grove.Width * grove.Height} cells of ground; " +
                    "the run would end with nowhere to place"));

            if (grove.Tiles < 8)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{grove.Tiles} tiles is over before a shape emerges"));
        }
    }

    /// <summary>
    /// Lightweave. A weave board is <em>generated</em>, so unlike a glade there is nothing in the
    /// file to read: every check here has to deal the board and look at what came out.
    /// </summary>
    sealed class WeaveValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Weave;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (WeaveRules)level.Rules;
            var layout = grove.LayoutFor(level.Id);

            // The board carries its own proof, so the validator plays it rather than trusting it.
            // Played all the way to IsSolved, which is what proves the beads too: a bead the
            // carved route does not thread is a board its own solution cannot finish.
            // A board rather than a run: the validator is proving a grove can be finished, not
            // playing one, so it neither spends light nor writes down a stroke.
            var board = new WeaveBoard(layout);
            if (!board.DrawSolution())
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "this grove's own solution does not join every pair and thread every bead " +
                    "without crossing, so the generator produced a board nobody can finish"));

            // The same three-line check every glade gets, asked of a mode that now has a fail
            // line to get wrong. Shared rather than restated: a second copy of "is this ladder
            // ordered" is a second thing to keep in step with LevelTuning (invariant 9a).
            LevelValidator.CheckStarBands(level, issues);

            // What no glade has to prove, because a glade's par *is* its minimum: a weave's is
            // the sum of the pairs' own floors plus an allowance, and the arrangement the player
            // actually has to draw costs that floor plus whatever detour the board forces. So
            // the ink has to cover the floor before anything else — a grove whose cheapest
            // possible finish is dearer than the light it is dealt cannot be won by anybody, and
            // it would look perfectly authored in the file.
            //
            // The forced detour is deliberately not asked for here: it is WeaveSolver's
            // exponential search, which is an authoring instrument and never a gate (the seed
            // sweep and WeaveLadderTests are where it runs). This is the half that is cheap and
            // certain.
            if (level.Tuning.HasBudget && layout.StraightTotal > level.Tuning.MoveBudget)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove is dealt {level.Tuning.MoveBudget} cells of ink and its pairs " +
                    $"cannot be joined in fewer than {layout.StraightTotal} even with nobody in " +
                    "anybody's way, so no arrangement of it can be drawn — lower the pair count " +
                    "or raise budgetFactor"));

            if (layout.Beads.Count < grove.BeadCount)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this grove asks for {grove.BeadCount} bead(s) and could only place " +
                    $"{layout.Beads.Count}: a bead is only worth placing on a cell off every " +
                    "shortest route between its pair's ends, and this seed's carved routes did " +
                    "not offer that many — re-seed it with Survey Lightweave's SeedSearch"));

            // Never seen on the shipped shape — the generator holds out for a carve that reaches
            // every cell and finds one within a handful of attempts. It is here for the size
            // somebody authors later that cannot be filled, where the fallback is a board whose
            // endpoints all sit in their own quiet corner.
            if (layout.Coverage < WeaveGenerator.MinCoverage)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"the carve covers {layout.Coverage:P0} of this grove, leaving " +
                    $"{layout.Count - layout.SolutionLength} cell(s) untouched; under " +
                    $"{WeaveGenerator.MinCoverage:P0} the endpoints stop being spread across the " +
                    "grove and the channels stop having to get past each other"));

            // The acceptance bar, asked again of the board that actually shipped. It is the same
            // predicate the generator held out for rather than a second opinion about it, which
            // is the point: a grove that has drifted past the bar is one the generator could not
            // satisfy and settled for, and that is worth saying out loud rather than inferring
            // from a difficulty survey somebody has to remember to run.
            //
            // A warning rather than an error, for the reason nothing else here is a gate: the
            // board is generated, so a build cannot be failed over a number that is a property of
            // a seed somebody would then have to guess their way out of.
            bool taut = WeaveSolver.AnyTautSolution(layout, out bool decided);
            if (decided && taut)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "every pair of this grove can take its own shortest route at once, so it is " +
                    "finished by drawing the obvious line at each critter in turn and asks the " +
                    "player nothing — re-seed the level with Survey Lightweave's SeedSearch, " +
                    "which holds a candidate to the exact bar"));
        }
    }
}
