using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Difficulty knobs, deliberately kept apart from the layout.
    ///
    /// These are the numbers you will want to change after launch, once analytics
    /// show a level is too punishing in some markets. Because they live here and
    /// not in the grid, retuning is a small remote payload that never touches the
    /// board a player already has records against.
    /// </summary>
    public sealed class LevelTuning
    {
        /// <summary>
        /// Where the two star lines sit, as multiples of par.
        ///
        /// <para>
        /// <b>They are thirds of the slack, and that is what keeps all three bands reachable.</b>
        /// A run is over at <see cref="DefaultBudgetFactor"/>, so everything a player can
        /// actually score lives in <c>[par, par × 1.60]</c> — a slack of 0.60 par. Cutting it in
        /// three puts three stars at 1.20, two at 1.40 and the end of the run at 1.60, each band
        /// exactly 0.20 par wide.
        /// </para>
        /// <para>
        /// <b>This is a retune and it carries its reason.</b> They were 1.35 and 2.00, chosen
        /// when the fail line was 2.60 and a clock decided most losses. Dropping the budget to
        /// 1.60 left the two-star line *outside* the survivable range, so one star became
        /// arithmetically unreachable — every clear was worth two or three and the bottom band
        /// existed only in old records. A star band nothing can land in is the same fault
        /// invariant 5d names for mechanics: it rejects no run, so it is decoration.
        /// </para>
        /// <para>
        /// <b>What this does and does not do to the economy.</b> Earned credits derive from the
        /// star ledger, so what matters is the *ceiling*, and the ceiling is unmoved: three
        /// stars a level, 52 levels, exactly as before. What changed is how well you have to
        /// play to reach it — which is the point. Do not reach for these to make the game
        /// harder in general; that is the boards' job (invariant 5d) and the budget's. Move
        /// these only to keep the bands fitted inside the budget, and move all three together.
        /// </para>
        /// <para>
        /// <c>LevelValidator</c> and <c>Tools/verify/content.py</c> both prove the ordering
        /// holds — <c>gold &lt; silver &lt; budget</c> — because the failure is silent: the
        /// numbers stay individually plausible and a whole band quietly stops existing.
        /// </para>
        /// </summary>
        public const float DefaultGoldFactor = 1.20f;
        public const float DefaultSilverFactor = 1.40f;

        /// <summary>
        /// How many pars' worth of turns a player gets before the run is lost.
        ///
        /// <para>
        /// <b>1.60, and it sits between the two star lines on purpose.</b> Three stars is
        /// <c>par × 1.35</c> and two is <c>par × 2.00</c>, so a budget of 1.60 means a run can
        /// end while the player was still on course for two — which the older 2.60 and 2.30
        /// values were explicitly shaped to prevent. That protection was removed deliberately
        /// (see <see cref="MoveBudget"/>): with the clock gone this is the only way a glade can
        /// be lost, and a fail line sitting past the point where a player has already stopped
        /// earning stars is not a fail line, it is a formality. Running out costs a heart and
        /// pays nothing, which is the whole rule and is meant to be explainable in one
        /// sentence.
        /// </para>
        /// <para>
        /// <b>The star lines were refitted to sit inside it</b>, and had to be. At 1.60 against
        /// the old 1.35/2.00 the two-star line was *outside* the survivable range, so one star
        /// could never be scored — see <see cref="DefaultGoldFactor"/>. The three lines are now
        /// even thirds of the slack this factor creates, so changing it means changing them:
        /// they are one decision in three numbers, and both validators prove the ordering.
        /// </para>
        /// <para>
        /// What keeps this fair rather than merely tight is that the meter counts
        /// <em>committed</em> wrong turns only. <c>BoardView.Undo</c> hands a turn back and is
        /// unlimited, and a hint charges none, so trying a crossing and taking it back is free
        /// — which it has to be, because a straight conduit and a straight crossing read the
        /// same half a turn round (invariant 5c), so exploring is correct play here.
        /// </para>
        /// <para>
        /// It is a factor rather than a number so it needs no per-level authoring and scales
        /// with a board's real difficulty. Note what it is <em>not</em>: a difficulty curve.
        /// That is the boards (invariant 5d). The only level that authors one is the first
        /// glade in the game, which turns the budget off entirely.
        /// </para>
        /// </summary>
        public const float DefaultBudgetFactor = 1.60f;

        /// <summary>A level authored with this has no budget and cannot be lost on moves.</summary>
        public const float Unlimited = -1f;

        /// <summary>Minimum turns needed to solve the board, computed at authoring time.</summary>
        public readonly int Par;

        /// <summary>Move budget multipliers over par for three and two stars.</summary>
        public readonly float GoldFactor;
        public readonly float SilverFactor;

        /// <summary>Turns allowed, as a multiple of par. <see cref="Unlimited"/> for none.</summary>
        public readonly float BudgetFactor;

        /// <summary>
        /// The three factors again as hundredths, and <b>these are what the thresholds are
        /// derived from</b>. The floats above are what an author writes and what a retune
        /// moves; nothing that produces a number a player is graded against reads them.
        ///
        /// <para>
        /// <c>1.20f</c> is not 1.2 — it is 1.20000004768…, so <c>Mathf.CeilToInt(45 * 1.20f)</c>
        /// is <b>55</b> where the design says 54, and the same at par 50 and at every par where
        /// the product ought to land exactly on an integer. It shipped that way on four glades,
        /// with the offline mirror (which had always used integers) reporting the design's
        /// number and the game quietly granting a turn more. Every number here is a multiple of
        /// a hundredth, so hundredths are exact and this class of fault cannot come back.
        /// </para>
        /// <para>
        /// It is <c>WeaveGenerator</c>'s <c>1.3f</c> a second time — see *Hard-won facts* —
        /// and worse in one way: that one differed between .NET and Mono, so a diff could
        /// catch it, while this one is wrong the same way everywhere and only disagrees with
        /// arithmetic. IL2CPP is a third code generator again, which is reason enough on its
        /// own never to let a float decide a threshold.
        /// </para>
        /// </summary>
        public readonly int GoldHundredths, SilverHundredths, BudgetHundredths;

        public LevelTuning(int par, float goldFactor, float silverFactor,
                           float budgetFactor = 0f)
        {
            Par = Mathf.Max(1, par);
            GoldFactor = goldFactor > 0f ? goldFactor : DefaultGoldFactor;
            SilverFactor = silverFactor > 0f ? silverFactor : DefaultSilverFactor;

            // 0 means "not authored", which takes the default. Only a deliberate
            // negative turns the budget off, so a level cannot lose its fail state by
            // omission — see the DTO convention in ContentDto.
            BudgetFactor = budgetFactor == 0f ? DefaultBudgetFactor
                         : budgetFactor < 0f ? Unlimited
                         : budgetFactor;

            GoldHundredths = Hundredths(GoldFactor);
            SilverHundredths = Hundredths(SilverFactor);
            BudgetHundredths = Hundredths(BudgetFactor);
        }

        /// <summary>An authored factor as an exact count of hundredths, rounded once, here.</summary>
        static int Hundredths(float factor) => Mathf.RoundToInt(factor * 100f);

        /// <summary>Ceiling of <c>par × hundredths/100</c> in integer arithmetic.</summary>
        static int Over(int par, int hundredths) => (par * hundredths + 99) / 100;

        public static LevelTuning Default(int par)
            => new LevelTuning(par, DefaultGoldFactor, DefaultSilverFactor);

        public int GoldThreshold => Over(Par, GoldHundredths);
        public int SilverThreshold => Over(Par, SilverHundredths);

        public bool HasBudget => BudgetHundredths > 0;

        /// <summary>
        /// Turns allowed before the run is lost. <see cref="int.MaxValue"/> when the
        /// level has no budget, so callers can compare without special-casing.
        ///
        /// <para>
        /// <b>An authored factor means exactly what it says.</b> This used to clamp to
        /// <c>SilverThreshold + 1</c> so that a run still earning stars could never be the run
        /// that ended — a sound rule while the clock was the fail state and this was a backstop
        /// under somebody drumming. It is gone on purpose. The clock went (invariant 22), this
        /// became the only way to lose a glade, and a floor at the two-star line put the fail
        /// line beyond the point where the player had already stopped earning anything. The
        /// consequence is deliberate and worth stating plainly: below <c>silverFactor</c> a
        /// player can lose a run they were on course to two-star, and below
        /// <see cref="GoldFactor"/> they could lose one they were on course to three-star.
        /// </para>
        /// <para>
        /// Nothing bounds this now except the author, so a nonsensical value produces a
        /// nonsensical glade. <c>LevelValidator</c> and <c>Tools/verify/content.py</c> both
        /// report a budget at or under the three-star line, because that is the one setting
        /// with no honest reading — every surviving run would be a three-star run, so the star
        /// ladder would stop existing rather than merely tighten.
        /// </para>
        /// </summary>
        public int MoveBudget
            => HasBudget ? Over(Par, BudgetHundredths) : int.MaxValue;

        // ------------------------------------------------------------------ the stars
        /// <summary>
        /// The stars a run earns, from the turns it took and nothing else.
        ///
        /// <para>
        /// <b>Turns alone, and that is the whole rule.</b> It used to be the worse of this and
        /// what a clock allowed, which meant the reading a thoughtful player got was almost
        /// always the clock's — so the move thresholds, the only half that measures whether a
        /// glade was actually solved well, were dead weight for exactly the players who engage
        /// with the board. A puzzle that is graded on how fast it is tapped is not graded on the
        /// puzzle. See <see cref="GoldFactor"/> for what the thresholds mean.
        /// </para>
        /// <para>
        /// Nothing already earned moves: <c>LevelRecord.Stars</c> is stored and only ever
        /// promoted, so removing the clock loosens future clears and re-grades none.
        /// </para>
        /// </summary>
        public int StarsFor(int moves)
        {
            if (moves <= GoldThreshold) return 3;
            if (moves <= SilverThreshold) return 2;
            return 1;
        }
    }
}
