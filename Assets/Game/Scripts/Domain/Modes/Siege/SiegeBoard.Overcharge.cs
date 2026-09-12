using System.Collections.Generic;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The overcharge: a full tube, tapped and thrown at once.
    ///
    /// <para>
    /// <b>It exists because the colour lock has one honest cost, and this is the answer to it.</b>
    /// A ward that only ever fires at its own colour banks whatever it is given while that colour
    /// is off the hill — which is the whole of what makes fuel a resource — and the mirror of that
    /// is fuel with nowhere to go: a colour that never comes is a quarter of the board matched for
    /// nothing. Tapping the tube turns it into damage on <em>anything</em>, so no match is ever
    /// dead. It is also the relief valve for the lock's other cost, a lane whose ward has fallen.
    /// </para>
    /// <para>
    /// <b>It is free of par by construction, and that is arithmetic rather than a policy.</b> What
    /// it delivers is exactly what the tube would have delivered as ordinary own-colour bolts —
    /// every bolt it holds, at that ward's own weight, doubled as an own-colour hit is
    /// (<see cref="SiegeTuning.PerfectMatch"/> already assumes precisely that of every gem). So a
    /// player who overcharges has moved damage they had already matched for, never conjured any:
    /// neither star line can move, and invariant 39's exchange rate has nothing to charge.
    /// </para>
    /// <para>
    /// <b>What it buys is <em>when</em> and <em>where</em></b> — a burst now instead of a trickle
    /// later, aimed at a colour this ward could never otherwise touch — and it can be wrong, which
    /// is what makes it a decision (invariant 26h): a tube dumped on a creeper is a tube not
    /// standing ready for the brute three beats behind it.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Dumps a full tube as one heavy strike on the furthest raider standing, and the box
        /// around it.
        ///
        /// <para>
        /// <b>The furthest rather than a chosen target</b>, because a second question is not a
        /// second decision: the tap already says <em>now</em>, and asking <em>where</em> on a board
        /// whose hill is walking would be a target picker over a run that does not stop. Furthest
        /// down is what a player would pick every time anyway, and it is the one raider whose
        /// position they can already read.
        /// </para>
        /// <para>
        /// <b>It carries a blast rather than landing on one body</b>, so a line that has been
        /// banking through a quiet is worth watching when it lets go — and because a relief valve
        /// that answered one raider would not answer a lane.
        /// </para>
        /// </summary>
        public SiegeUnleash Overcharge(int ward, List<SiegeStrike> into)
        {
            if (ward < 0 || ward >= _wards.Length) return SiegeUnleash.Refused;

            var post = _wards[ward];
            if (!post.Armed) return SiegeUnleash.Refused;

            var target = Furthest();
            if (target == null) return SiegeUnleash.Refused;

            // **A whole tube's worth of bolts, at this ward's own weight, landing as an own-colour
            // hit does.** The charge *is* a tube - `SiegeWard.Fill` took `Capacity` out of the
            // fuel to make it - so this is exactly what those bolts would have delivered had they
            // been fired one at a time, and neither star line can move for it.
            float each = SiegeTuning.FuelShot(post.Rank);
            int bolts = each <= 0f ? 0 : (int)(post.Capacity / each);

            if (bolts <= 0) return SiegeUnleash.Refused;

            int heavy = bolts * SiegeTuning.DamageTo(SiegeKind.Creeper, post.Rank, true,
                                                     post.Build);

            // **The rank is read at the moment it is thrown rather than the moment it was banked**,
            // so a cog spent in between makes a charge in hand worth more. Generous, which is the
            // direction par is safe to err in (invariant 22) - and the alternative is a stored
            // number the player cannot see going stale.
            post.Charges--;

            int lane = target.Lane;
            int row = SiegeTuning.RowOf(target.March);

            int absorbed = Unleash(post, lane, row, heavy, into);

            return new SiegeUnleash(ward, heavy, lane, row, absorbed);
        }

        /// <summary>
        /// The heavy strike itself: the box the firepot's own reach covers, blunted by plating
        /// wherever this ward's colour is not the answer.
        ///
        /// <b>Separate from <see cref="Blast"/></b>, because a firepot is a bought thing with no
        /// colour and this is a particular turret's fuel — so a bulwark this ward cannot answer
        /// soaks it exactly as it soaks that ward's splash.
        /// </summary>
        int Unleash(SiegeWard ward, int lane, int row, int damage, List<SiegeStrike> into)
        {
            if (damage <= 0) return 0;

            int absorbed = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (!SiegeTuning.Caught(raider.Kind, raider.Lane, raider.March, lane, row))
                    continue;

                int bites = Through(ward, raider, damage);

                int took = bites < raider.Health ? bites : raider.Health;
                absorbed += took;

                raider.Health -= took;
                raider.Flash = .18f;

                bool killed = Fell(raider);

                into?.Add(new SiegeStrike(raider.Id, took, killed));
            }

            return absorbed;
        }

        /// <summary>The raider furthest down the hill, whatever colour it wears.</summary>
        SiegeRaider Furthest()
        {
            SiegeRaider found = null;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (found == null || raider.March > found.March) found = raider;
            }

            return found;
        }

        /// <summary>
        /// What a ward's damage is worth against a raider it did not aim at: blunted by plating
        /// unless this ward answers that colour, or rends.
        ///
        /// <para>
        /// <b>This is where the bulwark's shield went, and it had to go somewhere.</b> Its rule
        /// was "only your own colour cuts me", which was a real decision while a bolt merely
        /// preferred its own colour — and became true of <em>every</em> raider the moment the lock
        /// arrived, so the soak stopped being reachable on a primary hit at all and the shield was
        /// decoration (invariant 5d) on the one raider whose whole identity it was.
        /// </para>
        /// <para>
        /// <b>What it means now is armour against <em>area</em> damage</b>, which is a sharper
        /// identity than the one it lost. A splash, a chain, a lance and an overcharge are the
        /// relief valve for a colour the player has not fed; a bulwark is the raider that valve
        /// does not answer, so it is the one thing on the hill that has to be met with the colour
        /// it wears. Rend is the exception it always was.
        /// </para>
        /// </summary>
        int Through(SiegeWard ward, SiegeRaider at, int damage)
        {
            if (at.Kind != SiegeKind.Bulwark) return damage;
            if (ward.Ability == WardAbility.Rend) return damage;
            if (ward.ReachTenths(at.Colour) > 0) return damage;

            int soaked = damage * SiegeTuning.ShieldSoakTenths / 10;

            // Never nought, for `DamageFineTo`'s reason: a hit that reads as landing and takes
            // nothing is a turret the player will believe is broken.
            return soaked < 1 ? 1 : soaked;
        }
    }
}
