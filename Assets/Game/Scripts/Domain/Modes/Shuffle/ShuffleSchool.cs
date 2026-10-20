using System;
using System.Collections.Generic;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// <b>An element a build can be made of</b> (MODES.md 59r, the owner, 2026-10-20: "players
    /// can go laser builds, poison builds, fire builds, frost builds ... formed very hard ... truly
    /// rewarding once assembled"). A card belongs to at most one school, read off what it does
    /// (<see cref="ShuffleSchools.Of(ShuffleEffect)"/>), never authored per row.
    ///
    /// <para>
    /// <b>The ordinals are permanent</b>: analytics names a run's schools, and a school is the
    /// stem of its ascension's colour and its loc keys. Appended, never renumbered.
    /// </para>
    /// </summary>
    public enum ShuffleSchool
    {
        None = 0,
        Laser = 1,
        Poison = 2,
        Fire = 3,
        Frost = 4,
        Storm = 5,
    }

    /// <summary>
    /// <b>The schools' rules</b>: which cards belong to which, how far a build has gone down one,
    /// when its keystone may be dealt, and how strongly a hand leans toward a school the build has
    /// started. One pure function each, so the deck, the odds, the hand's marks and the
    /// calibration all ask the same question (5b's rule).
    ///
    /// <para>
    /// <b>A build is formed in three steps</b>, each a function of the cards held (59e):
    /// <i>points</i> - every copy of a school's ordinary cards is one; <i>attuned</i> - at
    /// <see cref="AttuneAt"/> points the school's <b>keystone</b> (a legendary, once) may be
    /// dealt; <i>ascended</i> - the keystone held with <see cref="AscendAt"/> points unlocks the
    /// school's ascension (<see cref="ShuffleBondKind.Ascension"/>), its super ability.
    /// <b>At most <see cref="MostAscensions"/> keystones a run</b> (<c>ShuffleBuild.Takeable</c>),
    /// so at most two ascensions: a run commits.
    /// </para>
    /// <para>
    /// <b>The lean</b> (<see cref="Weight"/>): within the tier a hand rolled, a card of a school the
    /// build holds points in is <see cref="AffinityStep"/> per cent likelier per point, up to
    /// <see cref="AffinityMostPoints"/> points. Within the tier only, so a rarity's odds (59p, Apple
    /// 3.1.1) never move; and capped, so a hand stays a hand of the whole deck. Calibrated by
    /// <c>ShuffleSchoolTests.TheAscensionTableIsPrinted</c>, never by feel.
    /// </para>
    /// </summary>
    public static class ShuffleSchools
    {
        /// <summary>Every school, in ordinal order.</summary>
        public static readonly ShuffleSchool[] All =
        {
            ShuffleSchool.Laser, ShuffleSchool.Poison, ShuffleSchool.Fire, ShuffleSchool.Frost, ShuffleSchool.Storm,
        };

        /// <summary>Points in a school before its keystone may be dealt.</summary>
        public const int AttuneAt = 3;

        /// <summary>Points in a school, beside its keystone, that ascend it.</summary>
        public const int AscendAt = 7;

        /// <summary>The most keystones - and so ascensions - one run may hold (the owner, 2026-10-20).</summary>
        public const int MostAscensions = 2;

        /// <summary>How much likelier a card is per point its school holds, per cent of its plain weight.</summary>
        public const int AffinityStep = 20;

        /// <summary>The points past which the lean stops growing: three cards in, a school's cards weigh 160.</summary>
        public const int AffinityMostPoints = 3;

        /// <summary>A card's weight in its tier's pool with no lean: every card equal, as the deck always dealt.</summary>
        public const int PlainWeight = 100;

        /// <summary>The school <paramref name="effect"/> belongs to, or <see cref="ShuffleSchool.None"/>.</summary>
        public static ShuffleSchool Of(ShuffleEffect effect)
        {
            switch (effect)
            {
                case ShuffleEffect.Pierce:
                case ShuffleEffect.Ray:
                case ShuffleEffect.Laser:
                case ShuffleEffect.Focus:
                case ShuffleEffect.Refract:
                case ShuffleEffect.Photon:
                    return ShuffleSchool.Laser;

                case ShuffleEffect.Toxic:
                case ShuffleEffect.Venom:
                case ShuffleEffect.Contagion:
                case ShuffleEffect.Rot:
                case ShuffleEffect.Blight:
                    return ShuffleSchool.Poison;

                case ShuffleEffect.Burn:
                case ShuffleEffect.Wildfire:
                case ShuffleEffect.Meteor:
                case ShuffleEffect.Trail:
                case ShuffleEffect.Combust:
                case ShuffleEffect.Inferno:
                    return ShuffleSchool.Fire;

                case ShuffleEffect.Frost:
                case ShuffleEffect.Shatter:
                case ShuffleEffect.HillPace:
                case ShuffleEffect.ColdSnap:
                case ShuffleEffect.Brittle:
                case ShuffleEffect.Winter:
                    return ShuffleSchool.Frost;

                case ShuffleEffect.Chain:
                case ShuffleEffect.Tesla:
                case ShuffleEffect.Stun:
                case ShuffleEffect.Static:
                case ShuffleEffect.Thunderclap:
                case ShuffleEffect.Conduct:
                    return ShuffleSchool.Storm;

                default:
                    return ShuffleSchool.None;
            }
        }

        /// <summary>The school <paramref name="card"/> belongs to, or <see cref="ShuffleSchool.None"/>.</summary>
        public static ShuffleSchool Of(ShuffleCard card) => card == null ? ShuffleSchool.None : Of(card.Effect);

        /// <summary>The keystone effect of <paramref name="school"/>: the one card its ascension names.</summary>
        public static ShuffleEffect KeystoneOf(ShuffleSchool school)
        {
            switch (school)
            {
                case ShuffleSchool.Laser: return ShuffleEffect.Photon;
                case ShuffleSchool.Poison: return ShuffleEffect.Blight;
                case ShuffleSchool.Fire: return ShuffleEffect.Inferno;
                case ShuffleSchool.Frost: return ShuffleEffect.Winter;
                case ShuffleSchool.Storm: return ShuffleEffect.Conduct;
                default: throw new ArgumentOutOfRangeException(nameof(school), school, "no school has no keystone");
            }
        }

        /// <summary>Whether <paramref name="card"/> is a school's keystone.</summary>
        public static bool IsKeystone(ShuffleCard card) => card != null && IsKeystone(card.Effect);

        public static bool IsKeystone(ShuffleEffect effect)
            => effect == ShuffleEffect.Photon || effect == ShuffleEffect.Blight || effect == ShuffleEffect.Inferno
               || effect == ShuffleEffect.Winter || effect == ShuffleEffect.Conduct;

        /// <summary>The permanent id of a school: the stem of its loc key and its analytics name.</summary>
        public static string Id(ShuffleSchool school)
        {
            switch (school)
            {
                case ShuffleSchool.Laser: return "laser";
                case ShuffleSchool.Poison: return "poison";
                case ShuffleSchool.Fire: return "fire";
                case ShuffleSchool.Frost: return "frost";
                case ShuffleSchool.Storm: return "storm";
                default: return "none";
            }
        }

        /// <summary>
        /// <b>Points</b> in <paramref name="school"/>: every copy <paramref name="build"/> holds of the
        /// school's cards, its keystone not counted (a keystone is the gate, not a step to it).
        /// </summary>
        public static int Points(ShuffleBuild build, ShuffleSchool school)
        {
            if (build == null || school == ShuffleSchool.None) return 0;

            int points = 0;
            var taken = build.Taken;
            for (int i = 0; i < taken.Count; i++)
                if (!IsKeystone(taken[i]) && Of(taken[i]) == school) points++;

            return points;
        }

        /// <summary>Keystones <paramref name="build"/> holds: at most <see cref="MostAscensions"/>.</summary>
        public static int Keystones(ShuffleBuild build)
        {
            if (build == null) return 0;

            int held = 0;
            var taken = build.Taken;
            for (int i = 0; i < taken.Count; i++)
                if (IsKeystone(taken[i])) held++;

            return held;
        }

        /// <summary>Whether <paramref name="build"/> holds <paramref name="school"/>'s keystone.</summary>
        public static bool HoldsKeystone(ShuffleBuild build, ShuffleSchool school)
        {
            if (build == null || school == ShuffleSchool.None) return false;

            var keystone = KeystoneOf(school);
            var taken = build.Taken;
            for (int i = 0; i < taken.Count; i++)
                if (taken[i].Effect == keystone) return true;

            return false;
        }

        /// <summary>
        /// <b>Whether a hand may offer <paramref name="card"/> to <paramref name="build"/></b>: the
        /// build can take it (<c>ShuffleBuild.Takeable</c> - copies, and the two-keystone cap), and a
        /// keystone only once its school is attuned. What the deck deals, the odds count and a
        /// refresh asks after (59p), so the three can never disagree.
        /// </summary>
        public static bool Dealable(ShuffleCard card, ShuffleBuild build)
        {
            if (card == null || build == null || !build.Takeable(card)) return false;
            return !IsKeystone(card) || Points(build, Of(card)) >= AttuneAt;
        }

        /// <summary>
        /// <b>The lean</b>: <paramref name="card"/>'s weight among the cards of the tier a hand
        /// rolled. <see cref="PlainWeight"/> for a card of no school or of a school the build holds no
        /// points in, so a build of no school is dealt exactly as the deck always dealt.
        /// </summary>
        public static int Weight(ShuffleCard card, ShuffleBuild build)
        {
            var school = Of(card);
            if (school == ShuffleSchool.None) return PlainWeight;

            int points = Points(build, school);
            if (points > AffinityMostPoints) points = AffinityMostPoints;
            return PlainWeight + AffinityStep * points;
        }

        /// <summary>The ascension of <paramref name="school"/> (a bond of <see cref="ShuffleBondKind.Ascension"/>).</summary>
        public static ShuffleBond AscensionOf(ShuffleSchool school)
        {
            var all = ShuffleBonds.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Kind == ShuffleBondKind.Ascension && all[i].School == school) return all[i];

            return null;
        }

        /// <summary>
        /// Whether <paramref name="build"/> may still ascend <paramref name="school"/>: it holds the
        /// keystone, or has room for one more under <see cref="MostAscensions"/>.
        /// </summary>
        public static bool Reachable(ShuffleBuild build, ShuffleSchool school)
            => HoldsKeystone(build, school) || Keystones(build) < MostAscensions;

        /// <summary>Every school <paramref name="build"/> holds a point or a keystone in, in ordinal order.</summary>
        public static List<ShuffleSchool> Started(ShuffleBuild build)
        {
            var found = new List<ShuffleSchool>(All.Length);
            for (int i = 0; i < All.Length; i++)
                if (Points(build, All[i]) > 0 || HoldsKeystone(build, All[i])) found.Add(All[i]);

            return found;
        }
    }
}
