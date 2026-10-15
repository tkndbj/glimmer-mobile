using System;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The letter a finished Shuffle run is graded with (MODES.md 59o), worst first.
    ///
    /// <para>
    /// <b>The ordinals are permanent</b>: a grade is stored in the save (<c>ShuffleRunDto.grade</c>)
    /// and published on the keeper's card (<c>shuffleTop.grade</c>), so a member may be appended
    /// above <see cref="SPlusPlus"/> and never renumbered, renamed or reused. Its id and its loc key
    /// are derived from <see cref="ShuffleGrades.Id"/> (5a).
    /// </para>
    /// </summary>
    public enum ShuffleGrade
    {
        D = 0,
        C = 1,
        B = 2,
        A = 3,
        AA = 4,
        S = 5,
        SPlus = 6,
        SPlusPlus = 7,
    }

    /// <summary>
    /// How a run's build becomes a score, and a score a grade. One pure function each, so the
    /// victory panel, the ledger and the calibration all ask the same question (5b's rule).
    ///
    /// <para>
    /// <b>A score is points, and points are a property of the tier, never of the card</b>
    /// (<see cref="CardPoints"/>, <see cref="BondPoints"/>). A card added to the catalog is scored
    /// the day it is written with nothing authored, and because a hand deals its tiers off weights
    /// that do not depend on how many cards exist, adding cards does not move what a hand is
    /// worth: a grade means the same depth of run after the deck grows. What can move it is a new
    /// bond or a new deck table, and that is what <c>ShuffleGradeTests.TheTopGradesStayRare</c>
    /// holds - it deals hundreds of seeded runs to a passive and an expert chooser and goes red if
    /// a grade arrives earlier than the table it was calibrated on.
    /// </para>
    /// <para>
    /// <b>The amount and the combination both count, and the top three need the combination</b>
    /// (the owner, 2026-10-15: "only if they got the best combination and amount of upgrades").
    /// Every copy pays its tier and every secret or evolution - cards held <em>together</em> - pays
    /// more than its parts did. But a good build is not known to end (a strong line outlasts the
    /// deck), so points alone would make S++ a reward for patience. So S, S+ and S++ also have a
    /// <see cref="BondFloors">combination floor</see>, counted on bond points only: no number of
    /// cards reaches them without the bonds those cards were chosen to complete.
    /// </para>
    /// <para>
    /// <b>Cheap at any scale</b>: one walk of the cards held and one of the bonds unlocked, both
    /// already on the build. Nothing is read from a server and nothing is stored but the answer.
    /// </para>
    /// </summary>
    public static class ShuffleGrades
    {
        /// <summary>The highest grade there is.</summary>
        public const ShuffleGrade Most = ShuffleGrade.SPlusPlus;

        /// <summary>
        /// The points a grade needs, by ordinal: <c>Floors[g]</c> is the least score graded
        /// <c>g</c>. Strictly rising from nought, held by <c>ShuffleGradeTests</c>, and read off
        /// that fixture's printed table (MODES.md 59o), never tuned by feel. Against the shipped
        /// deck (2026-10-15), a run taking the first card of every hand is graded C by about wave
        /// 20, B by 40, A by 80, AA by 120 and S not before 160; an expert chasing bonds with
        /// every pick reaches S by about wave 100 and S++ by about 200.
        ///
        /// <para>
        /// <b>Code and not the live tuning</b>, deliberately: a grade is a public record that
        /// stays what it was when earned (the ledger stores the letter), so moving a floor
        /// between releases would make two equal builds read differently on two profiles for a
        /// reason neither player can see. A retune is a build, and the gate above decides it.
        /// </para>
        /// </summary>
        public static readonly int[] Floors =
        {
            0,      // D
            250,    // C
            800,    // B
            2000,   // A
            4500,   // AA
            7500,   // S
            9500,   // S+
            11500,  // S++
        };

        /// <summary>
        /// The points from bonds alone a grade needs, by ordinal: nought below S. What makes the
        /// top three a matter of <em>which</em> cards were taken and never only how many. Against
        /// the shipped catalog (6,390 bond points in all), S++ wants more than three quarters of
        /// every combination in the game - every grand secret among them.
        /// </summary>
        public static readonly int[] BondFloors =
        {
            0, 0, 0, 0, 0,
            1500,   // S
            3000,   // S+
            5000,   // S++
        };

        /// <summary>
        /// The highest ordinal a stored or published grade may carry: room for grades a later build
        /// appends, which this one carries untouched and draws as <see cref="Most"/>.
        /// </summary>
        public const int MaxOrdinal = 63;

        /// <summary>The most points a stored or published score may claim (a bound, not a goal).</summary>
        public const int MaxPoints = 1000000;

        /// <summary>
        /// The grade a score of <paramref name="points"/>, <paramref name="bondPoints"/> of them from
        /// bonds, earns: the highest whose two floors are both met. D for nought.
        /// </summary>
        public static ShuffleGrade Of(int points, int bondPoints)
        {
            for (int g = Floors.Length - 1; g > 0; g--)
                if (points >= Floors[g] && bondPoints >= BondFloors[g]) return (ShuffleGrade)g;

            return ShuffleGrade.D;
        }

        /// <summary>The grade <paramref name="build"/> earns.</summary>
        public static ShuffleGrade Of(ShuffleBuild build)
        {
            Score(build, out int cards, out int bonds);
            return Of(Total(cards, bonds), bonds);
        }

        /// <summary>Whether <paramref name="ordinal"/> names a grade this build knows.</summary>
        public static bool Known(int ordinal) => ordinal >= 0 && ordinal <= (int)Most;

        /// <summary>A stored ordinal held to the grades this build knows (a newer one reads as the top).</summary>
        public static ShuffleGrade Sane(int ordinal)
            => ordinal < 0 ? ShuffleGrade.D : ordinal > (int)Most ? Most : (ShuffleGrade)ordinal;

        /// <summary>The grade's permanent id: <c>d</c>, <c>c</c>, ... <c>s_plus_plus</c>.</summary>
        public static string Id(ShuffleGrade grade)
        {
            switch (grade)
            {
                case ShuffleGrade.D: return "d";
                case ShuffleGrade.C: return "c";
                case ShuffleGrade.B: return "b";
                case ShuffleGrade.A: return "a";
                case ShuffleGrade.AA: return "aa";
                case ShuffleGrade.S: return "s";
                case ShuffleGrade.SPlus: return "s_plus";
                case ShuffleGrade.SPlusPlus: return "s_plus_plus";
                default: throw new ArgumentOutOfRangeException(nameof(grade), grade, "a grade with no id");
            }
        }

        /// <summary>The loc key for the grade's letter. Derived, never authored (5a).</summary>
        public static string NameKey(ShuffleGrade grade) => "shuffle.grade." + Id(grade);

        /// <summary>Whether this is one of the three grades the owner asked to be rare (S and up).</summary>
        public static bool IsTop(ShuffleGrade grade) => grade >= ShuffleGrade.S;

        // ------------------------------------------------------------------ the score
        /// <summary>What one copy of a card of <paramref name="tier"/> is worth.</summary>
        public static int CardPoints(ShuffleTier tier)
        {
            switch (tier)
            {
                case ShuffleTier.Common: return 10;
                case ShuffleTier.Rare: return 25;
                case ShuffleTier.Epic: return 60;
                case ShuffleTier.Legendary: return 150;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "a tier with no points");
            }
        }

        /// <summary>
        /// What an unlocked bond is worth: a secret (several different cards held together) more
        /// than an evolution (one card held to its most), each climbing with its tier, and a secret
        /// always more than the cards that make it (<c>ShuffleGradeTests</c>).
        /// </summary>
        public static int BondPoints(ShuffleBond bond)
        {
            if (bond == null) return 0;

            bool secret = bond.IsSecret;
            switch (bond.Tier)
            {
                case ShuffleTier.Common:
                case ShuffleTier.Rare: return secret ? 150 : 80;
                case ShuffleTier.Epic: return secret ? 300 : 200;
                case ShuffleTier.Legendary: return secret ? 700 : 500;
                default: throw new ArgumentOutOfRangeException(nameof(bond), bond.Tier, "a bond tier with no points");
            }
        }

        /// <summary>
        /// The build's score: every copy of every card held at its tier's points, and every bond
        /// it has unlocked at its own. A function of the cards held and never of their order (59e).
        /// </summary>
        public static int Score(ShuffleBuild build) => Score(build, out _, out _);

        /// <summary><see cref="Score(ShuffleBuild)"/>, with the cards' and the bonds' shares told apart.</summary>
        public static int Score(ShuffleBuild build, out int cardPoints, out int bondPoints)
        {
            long cards = 0, bonds = 0;

            if (build != null)
            {
                var taken = build.Taken;
                for (int i = 0; i < taken.Count; i++) cards += CardPoints(taken[i].Tier);

                var unlocked = build.Bonds;
                for (int i = 0; i < unlocked.Count; i++) bonds += BondPoints(unlocked[i]);
            }

            cardPoints = cards > MaxPoints ? MaxPoints : (int)cards;
            bondPoints = bonds > MaxPoints ? MaxPoints : (int)bonds;
            return Total(cardPoints, bondPoints);
        }

        static int Total(int cards, int bonds)
        {
            long total = (long)cards + bonds;
            return total > MaxPoints ? MaxPoints : (int)total;
        }
    }
}
