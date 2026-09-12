using System;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// What a turret does <em>beyond</em> putting a bolt into a raider.
    ///
    /// <para>
    /// <b>Every one of these is an addition, and that is the load-bearing rule of the whole
    /// roster.</b> A siege's par is <c>SiegeTuning.Par</c> — the hill's health over the most one
    /// match can ever be worth — and it is computed against the <em>baseline</em> bolt every
    /// turret fires. So a model that hit <em>softer</em> would push three stars out of reach for
    /// anybody who chose it, which is a grade decided by a purchase: exactly what invariant 39
    /// refuses a utility, asked of the line instead. A model that hits harder only makes par
    /// over-state what a good run needs, which is the direction invariant 22 says to err in and
    /// the one invariant 37w already accepted when cogs shipped.
    /// </para>
    /// <para>
    /// <b>So no ability may reduce the primary hit, ever.</b> <c>WardCatalog</c> has no way to
    /// express one, deliberately: a model carries a <em>bonus</em> and never a multiplier below
    /// one, so the rule is enforced by the shape of the data rather than by a check somebody has
    /// to remember to run.
    /// </para>
    /// <para>
    /// <b>What makes choosing one a decision rather than a straight upgrade</b> (invariant 26h's
    /// test) is that a line holds four turrets, one per colour, and a colour is what a level's
    /// hill decides. Rend on red is worth a great deal on a rung sending red bulwarks and worth
    /// nothing at all on one that sends none — so a player can be wrong, and can be wrong in a way
    /// they could have read off the board before they started.
    /// </para>
    /// </summary>
    public enum WardAbility
    {
        /// <summary>
        /// Nothing but the bolt. The starter, and the yardstick every other model is measured
        /// against.
        ///
        /// <b>It is not a weak choice and must never be made one</b>: every model fires the same
        /// primary bolt, so this is exactly as good against a lone target as the dearest turret in
        /// the shop. What the others buy is reach, or an answer to something specific.
        /// </summary>
        None,

        /// <summary>
        /// Its bolt also strikes everything standing within a band of what it hit.
        ///
        /// The answer to a wave that arrives in a clump, and dead weight against a hill walking
        /// down one at a time.
        /// </summary>
        Splash,

        /// <summary>
        /// Its bolt arcs on to the nearest raiders after the one it hit.
        ///
        /// Splash's sibling and deliberately not the same: splash is decided by <em>where</em>
        /// raiders are standing and a chain is decided by how <em>many</em> there are, so a
        /// scattered hill answers one and not the other.
        /// </summary>
        Chain,

        /// <summary>
        /// What it hits walks slower for a while.
        ///
        /// <b>The only ability that buys time rather than damage</b>, which is why it is the one
        /// that changes how a rung is played rather than how fast it ends: a frozen brute is a
        /// brute the rest of the line gets a second pass at, and a frozen boss is a tell the
        /// player can answer.
        /// </summary>
        Frost,

        /// <summary>
        /// Every so often its bolt runs the whole length of its lane, striking everything in it.
        ///
        /// The answer to a column marching in single file — the arrangement splash is worst
        /// against.
        /// </summary>
        Pierce,

        /// <summary>
        /// Its bolts are not blunted by a shield, and bite its magnitude deeper into plating.
        ///
        /// <b>The narrowest ability in the roster on purpose.</b> A bulwark takes half from any
        /// colour but its own (<c>SiegeTuning.ShieldSoakTenths</c>), so this is worth a clean
        /// doubling on a rung that sends them and is worth precisely nothing on one that does not.
        /// A roster where every model is useful everywhere is a roster with no decision in it.
        ///
        /// <b>The bonus is what tells its two rungs apart</b>, and it had to be added: "not
        /// blunted" is a flat answer, so the magnitude was read by nothing at all and the two
        /// rungs of this ability were one turret at two prices (<c>SiegeTuning.RendBonus</c>).
        /// </summary>
        Rend,

        /// <summary>
        /// A kill hands some of its fuel back.
        ///
        /// It rewards feeding the ward whose colour the hill is actually wearing, which is the
        /// mode's own question — so it is the model that gets better the better the player is.
        /// </summary>
        Siphon,

        /// <summary>
        /// What it hits goes on burning for a few seconds afterwards.
        ///
        /// Damage over time rather than at once, so it is strongest against the things that live
        /// longest and weakest against a creeper that would have died anyway.
        /// </summary>
        Ember,

        /// <summary>
        /// It also fires at the <em>next</em> colour round the wheel, for a share of a full hit —
        /// and its magnitude is that share, in tenths.
        ///
        /// <b>The one model that widens the mode's central rule instead of adding to it.</b> A
        /// turret only ever fires at its own colour (<c>SiegeBoard.Aim</c>), so a prism is the one
        /// thing on the shelf that can answer a lane the player has not fed — at the cost of that
        /// slot having no other trick at all.
        ///
        /// <b>Two colours and never three.</b> Under the lock a third would not be a better rung,
        /// it would be the lock coming off, so <c>SiegeWard.MostPartners</c> pins the count at one
        /// and the family climbs on the share instead (<c>SiegeWard.PartnerShare</c>). That also
        /// keeps the two rungs genuinely different, which invariant 37ax requires of any family.
        ///
        /// <b>It can never make a bolt weaker</b>, which is invariant 42's whole promise: the
        /// partner is aimed at only when this ward's own colour has nothing left standing, so
        /// every partner shot is one the turret would otherwise not have fired.
        /// </summary>
        Prism,

        /// <summary>
        /// It holds more fuel, so a big match banks rather than spills.
        ///
        /// The answer to a cascade: a plain ward tops out and throws the rest of a five-wave chain
        /// away.
        /// </summary>
        Beacon,
    }

    /// <summary>Reading an authored ability name, and never throwing on one from the future.</summary>
    public static class WardAbilities
    {
        /// <summary>
        /// The name each ability is authored under. Permanent: content keys on these.
        ///
        /// <b>A table rather than <c>Enum.Parse</c></b>, for <c>UtilityKinds</c>'s reason — a
        /// parse that accepts the C# member name accepts casing and numbers too, and would make
        /// the enum's own spelling a content contract nobody meant to sign.
        /// </summary>
        static readonly (string Name, WardAbility Ability)[] Names =
        {
            ("none",   WardAbility.None),
            ("splash", WardAbility.Splash),
            ("chain",  WardAbility.Chain),
            ("frost",  WardAbility.Frost),
            ("pierce", WardAbility.Pierce),
            ("rend",   WardAbility.Rend),
            ("siphon", WardAbility.Siphon),
            ("ember",  WardAbility.Ember),
            ("prism",  WardAbility.Prism),
            ("beacon", WardAbility.Beacon),
        };

        /// <summary>
        /// The ability this name means, or <see cref="WardAbility.None"/> for one this build has
        /// never heard of.
        ///
        /// <b>Never a failure</b>, and that is invariant 20's answer one level down: a model naming
        /// an ability from a newer build still stands, still fires, and simply does nothing extra —
        /// which is a turret that works rather than a slot in the line that cannot be filled.
        /// </summary>
        public static WardAbility Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return WardAbility.None;

            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i].Name, name, StringComparison.Ordinal))
                    return Names[i].Ability;

            return WardAbility.None;
        }

        /// <summary>The name this ability is authored under.</summary>
        public static string NameOf(WardAbility ability)
        {
            for (int i = 0; i < Names.Length; i++)
                if (Names[i].Ability == ability) return Names[i].Name;

            return "none";
        }

        /// <summary>Whether this build knows the name at all. For a content gate, never for play.</summary>
        public static bool Known(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i].Name, name, StringComparison.Ordinal)) return true;

            return false;
        }
    }
}
