using System.Collections.Generic;
using GlimmerGrove.Utilities;

namespace GlimmerGrove.Modes
{
    /// <summary>Where a utility is being aimed. Which half is read depends on the kind.</summary>
    public readonly struct SiegeAim
    {
        /// <summary>Across the hill, 0..1. Read by a blast.</summary>
        public readonly float Lane;

        /// <summary>Down the hill, 0..1. Read by a blast.</summary>
        public readonly float March;

        /// <summary>Which ward. Read by a mend and a surge.</summary>
        public readonly int Ward;

        SiegeAim(float lane, float march, int ward)
        {
            Lane = lane;
            March = march;
            Ward = ward;
        }

        public static SiegeAim OnTheHill(float lane, float march) => new SiegeAim(lane, march, -1);

        public static SiegeAim AtWard(int ward) => new SiegeAim(0f, 0f, ward);
    }

    /// <summary>
    /// What using a utility did: whether it landed, what it cost the grade, and what the view
    /// has to draw.
    /// </summary>
    public readonly struct SiegeUse
    {
        /// <summary>Whether anything happened. A use that did not land is never charged.</summary>
        public readonly bool Landed;

        /// <summary>
        /// What this costs the run, in the unit a siege is graded in.
        ///
        /// See <see cref="SiegeUtility"/>: nought for anything that delivers no damage, and
        /// otherwise the fewest matches that could have delivered the same.
        /// </summary>
        public readonly int Matches;

        /// <summary>Damage actually absorbed, health actually mended, or fuel-tenths poured.</summary>
        public readonly int Delivered;

        /// <summary>Which ward, or -1 for a blast.</summary>
        public readonly int Ward;

        public SiegeUse(bool landed, int matches, int delivered, int ward)
        {
            Landed = landed;
            Matches = matches < 0 ? 0 : matches;
            Delivered = delivered < 0 ? 0 : delivered;
            Ward = ward;
        }

        public static readonly SiegeUse Refused = new SiegeUse(false, 0, 0, -1);
    }

    /// <summary>
    /// What a utility does to a siege, and — the half that matters — what it costs.
    ///
    /// <para>
    /// <b>Invariant 39 lives here, and it is arithmetic rather than a policy.</b> A utility must
    /// be able to buy a <em>finish</em> and never a <em>grade</em>, because a grade is not a
    /// private number: stars derive credits, credits are a grove's worth, and a grove's worth
    /// reaches a public leaderboard (invariant 19a). A consumable that made a run score better
    /// would therefore be a consumable that moved a public figure — and utilities are not
    /// adjudicated, so a forged one would move it for free.
    /// </para>
    /// <para>
    /// What closes that is the exchange rate the mode already has.
    /// <c>SiegeTuning.PerfectMatch</c> is <em>the most</em> one match can ever deliver — three
    /// gems, every one spent as a bolt, every bolt landing on a raider that ward is strong
    /// against — which is exactly why <c>SiegeTuning.Par</c> is allowed to divide by it and call
    /// itself a floor. Charge a utility <c>ceil(damage / PerfectMatch)</c> and the charge is a
    /// floor on the matches it saved, so the run's spent count can never come out lower than a
    /// run that did the same work by playing. Using one is therefore, at best, exactly neutral to
    /// the grade, and usually slightly dearer.
    /// </para>
    /// <para>
    /// <b>A mending charges nothing, and that is the same rule rather than an exception.</b> It
    /// delivers no damage, so it saves no matches: what it buys is survival, which invariant 23
    /// already says a purchase may sell ("the offer sells a finish, never a grade"). The whole
    /// design falls out of one question asked of each kind — <em>how many matches would this have
    /// taken?</em> — and answering nought is a real answer.
    /// </para>
    /// <para>
    /// <b>Which means a utility can never be the difficulty a board was tuned against.</b>
    /// Invariant 29c refuses a companion's ability the right to change what a move does, because
    /// par is fixed per board and an ability that varied it would give two players two different
    /// games. A utility varies the board and pays for it in the graded unit, so the ladder it is
    /// measured against does not move.
    /// </para>
    /// </summary>
    public static class SiegeUtility
    {
        /// <summary>
        /// The fewest matches that could have delivered this much damage.
        ///
        /// <b>The one place the exchange rate is written</b>, so a blast and a surge cannot come
        /// to price themselves differently, and integer throughout for <c>LevelTuning</c>'s
        /// reason.
        /// </summary>
        public static int MatchesFor(int damage)
        {
            if (damage <= 0) return 0;

            int per = SiegeTuning.PerfectMatch;
            if (per < 1) per = 1;

            return (damage + per - 1) / per;
        }

        /// <summary>
        /// The damage a ward could get out of this much fuel, in the same currency
        /// <see cref="MatchesFor"/> reads.
        ///
        /// <para>
        /// The most, not the likely: every tenth spent as a bolt, every bolt landing on something
        /// that ward is strong against. Over-stating what a surge is worth over-charges the grade,
        /// which is the safe direction — under-stating it would let a player buy a star.
        /// </para>
        /// </summary>
        public static int DamageOfFuel(int tenths)
            => tenths <= 0 ? 0
             : tenths * SiegeTuning.ShotDamage * SiegeTuning.WeakMultiplier / 10;

        /// <summary>
        /// Whether this utility would do anything at all on this board, aimed here.
        ///
        /// <para>
        /// <b>Asked before an item is spent and never after.</b> A player charged for a utility
        /// that changed nothing has lost something they may have paid gems for, which is
        /// <c>ProtoView.Took</c>'s rule about a move applied to the one resource here that costs
        /// money to replace — and invariant 23's rule about a continue that does not continue.
        /// </para>
        /// <para>
        /// A blast is the one kind this cannot answer honestly in advance and does not try to: it
        /// is allowed on any point of the hill, because "would it hit anything" is a question the
        /// player can see the answer to and 32c says a preview shows geometry rather than outcome.
        /// What stops a wasted one is that <see cref="Apply"/> charges nothing and spends nothing
        /// when it absorbs nothing.
        /// </para>
        /// </summary>
        public static bool Would(SiegeBoard board, UtilityItem item, SiegeAim aim)
        {
            if (board == null || item == null || board.Stranded) return false;

            switch (item.Kind)
            {
                case UtilityKind.Blast:
                    return true;

                case UtilityKind.Mend:
                    return board.RoomForHealth(aim.Ward) > 0;

                case UtilityKind.Surge:
                    // Half, rather than all of it. Refusing a ward that cannot take the whole
                    // pour would make a surge unusable on exactly the ward that is firing, and
                    // accepting one with a tenth of room would spend an item for nothing.
                    return board.RoomForFuel(aim.Ward) * 2 >= item.Magnitude;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Uses one, and answers what it did.
        ///
        /// <para>
        /// <b>Nothing is taken from the player here.</b> This applies the effect and reports it;
        /// <c>UtilityLedger.TryUse</c> is called by the screen afterwards and only when
        /// <see cref="SiegeUse.Landed"/> is true, so the board is always asked first. Two orders
        /// were possible and only one of them cannot charge for nothing.
        /// </para>
        /// </summary>
        public static SiegeUse Apply(SiegeBoard board, UtilityItem item, SiegeAim aim,
                                     List<SiegeStrike> strikes)
        {
            if (!Would(board, item, aim)) return SiegeUse.Refused;

            switch (item.Kind)
            {
                case UtilityKind.Blast:
                {
                    int absorbed = board.Blast(aim.Lane, aim.March, item.Reach / 100f,
                                               item.Magnitude, strikes);

                    // A firepot that reached nobody is not spent. It is the one refusal that can
                    // only be known after the fact, and handing the item back is the only honest
                    // answer — see Would.
                    if (absorbed <= 0) return SiegeUse.Refused;

                    return new SiegeUse(true, MatchesFor(absorbed), absorbed, -1);
                }

                case UtilityKind.Mend:
                {
                    int given = board.Mend(aim.Ward, item.Magnitude);
                    if (given <= 0) return SiegeUse.Refused;

                    // Nought matches, and that is the rule rather than a gap in it: it delivers
                    // no damage, so it saves no matches. It buys a finish and never a grade.
                    return new SiegeUse(true, 0, given, aim.Ward);
                }

                case UtilityKind.Surge:
                {
                    int poured = board.Surge(aim.Ward, item.Magnitude);
                    if (poured <= 0) return SiegeUse.Refused;

                    // Charged for the *whole* pour rather than for what the ward took, so the
                    // charge is a constant a player can learn and never a number that depends on
                    // how full the ward happened to be. Over-charging is the safe direction; the
                    // ward that could take less than half of it was refused by Would.
                    int matches = MatchesFor(DamageOfFuel(item.Magnitude));

                    return new SiegeUse(true, matches, poured, aim.Ward);
                }

                default:
                    return SiegeUse.Refused;
            }
        }
    }
}
