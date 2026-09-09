using System;
using System.Collections.Generic;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What the player's chosen turrets do beyond putting a bolt into a raider.
    ///
    /// <para>
    /// <b>Every one of these is an addition to a bolt that has already landed at full strength,
    /// and that is the load-bearing rule of the whole loadout.</b> A siege's par is
    /// <c>SiegeTuning.Par</c> — the hill's health over the most one match could ever be worth —
    /// and it is computed against the baseline bolt. A turret that hit <em>softer</em> would push
    /// three stars out of reach of whoever chose it, which is a grade decided by a purchase and
    /// the one thing invariant 39 refuses outright. A turret that hits harder only makes par
    /// over-state what a good run needs, which is the direction invariant 22 says to err in and
    /// exactly what invariant 37w already accepted when cogs shipped.
    /// </para>
    /// <para>
    /// <b>So the primary hit is computed and applied by <c>Shoot</c> before anything here
    /// runs.</b> Nothing in this file may reduce it, and there is no path through which it could:
    /// every method below either damages <em>somebody else</em>, adds a lasting state, or gives
    /// fuel back.
    /// </para>
    /// <para>
    /// <b>Its own file because it is the seam between two features.</b> The hill and the field are
    /// content; the line is the player. Keeping the ability rules here means a new turret is one
    /// case in one switch, and it means the rest of the clock reads the same as it did when every
    /// ward was identical.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Runs a ward's ability after its bolt has landed.
        ///
        /// <para>
        /// <b>Extra hits are reported as ordinary bolts marked <c>Extra</c></b> rather than as a
        /// record of their own. The view already knows how to draw a bolt arriving from a ward at
        /// a raider, and a second kind of record would be a second drawing path that could come to
        /// disagree with it — which is how a mode ends up with an effect nobody can tell from
        /// another. The flag exists so the view can draw a splash smaller than the shot that
        /// caused it, and for no other reason.
        /// </para>
        /// </summary>
        void Ability(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            if (target == null) return;

            var model = ward.Model;

            switch (model.Ability)
            {
                case WardAbility.Splash:
                    Spread(ward, index, target, Share(damage, model.Magnitude), model.Extent);
                    break;

                case WardAbility.Chain:
                    Arc(ward, index, target, Share(damage, model.Magnitude), model.Extent);
                    break;

                case WardAbility.Frost:
                    target.Freeze(model.Magnitude, model.Extent / 10f);
                    break;

                case WardAbility.Pierce:
                    if (model.Extent > 0 && ward.Shots % model.Extent == 0)
                        Lance(ward, index, target, Share(damage, model.Magnitude));
                    break;

                case WardAbility.Ember:
                    target.Kindle(Share(damage, model.Magnitude), model.Extent / 10f, index);
                    break;

                // Rend, Prism and Beacon are not applied here at all: the first two change what
                // the *primary* hit is worth and are read by `SiegeTuning.DamageTo`, and a beacon
                // changes what the ward holds and is read once when it is built. Said out loud
                // rather than left as a missing case, because a reader looking for a turret's
                // effect has to be told where it lives.
                case WardAbility.Siphon:
                case WardAbility.Rend:
                case WardAbility.Prism:
                case WardAbility.Beacon:
                case WardAbility.None:
                default:
                    break;
            }
        }

        /// <summary>
        /// A share of a bolt, in tenths, never below one.
        ///
        /// <b>Never nought</b>, for <c>SiegeTuning.DamageTo</c>'s reason: an extra hit that reads
        /// as landing and takes nothing is a turret the player will believe is broken, and at a
        /// low rank against a small share the division gets there.
        /// </summary>
        static int Share(int damage, int tenths)
        {
            int share = damage * tenths / 10;
            return share < 1 ? 1 : share;
        }

        /// <summary>Everything standing in the box around what a splash turret hit.</summary>
        void Spread(SiegeWard ward, int index, SiegeRaider target, int damage, int reach)
        {
            if (reach < 1) reach = 1;

            int row = SiegeTuning.RowOf(target.March);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var other = _raiders[i];
                if (other == target || !other.Alive || !other.OnTheHill) continue;

                if (Math.Abs(SiegeTuning.RowOf(other.March) - row) > reach) continue;
                if (Math.Abs(other.Lane - target.Lane) > reach) continue;

                Splinter(ward, index, other, damage);
            }
        }

        /// <summary>
        /// The nearest raiders after the one a chain turret hit.
        ///
        /// <b>Nearest by how far down the hill they are</b>, which is what the view can draw as an
        /// arc going somewhere legible — and what makes a chain worth more the more raiders are
        /// bunched, which is the shape that tells it apart from a splash.
        /// </summary>
        void Arc(SiegeWard ward, int index, SiegeRaider target, int damage, int extra)
        {
            if (extra < 1) extra = 1;

            for (int hop = 0; hop < extra; hop++)
            {
                SiegeRaider next = null;
                float best = float.MaxValue;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var other = _raiders[i];
                    if (other == target || !other.Alive || !other.OnTheHill) continue;
                    if (_arced.Contains(other.Id)) continue;

                    float gap = Math.Abs(other.March - target.March)
                              + Math.Abs(other.Lane - target.Lane) * .04f;

                    if (gap >= best) continue;

                    best = gap;
                    next = other;
                }

                if (next == null) break;

                _arced.Add(next.Id);
                Splinter(ward, index, next, damage);
            }

            _arced.Clear();
        }

        /// <summary>Everything in the lane a pierce turret fired down.</summary>
        void Lance(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var other = _raiders[i];
                if (other == target || !other.Alive || !other.OnTheHill) continue;
                if (other.Lane != target.Lane) continue;

                Splinter(ward, index, other, damage);
            }
        }

        /// <summary>
        /// One extra hit: takes the health, reports the bolt, and takes the raider off the hill
        /// through the one door if that killed it.
        /// </summary>
        void Splinter(SiegeWard ward, int index, SiegeRaider other, int damage)
        {
            if (damage < 1) damage = 1;

            other.Health -= damage;
            other.Flash = .18f;

            bool killed = Fell(other);
            if (killed) Refund(ward);

            _report.Bolts.Add(new SiegeBolt(index, other.Id, damage,
                                            ward.StrongAgainst(other.Colour), killed, true));
        }

        /// <summary>
        /// Hands a siphon turret some of its fuel back for a kill.
        ///
        /// <b>Capped at the ward's own capacity</b> like every other way fuel arrives, so a line
        /// that is clearing a wave cannot bank past what it could have banked from a match.
        /// </summary>
        void Refund(SiegeWard ward)
        {
            if (ward.Model.Ability != WardAbility.Siphon || ward.Model.Magnitude <= 0) return;
            if (!ward.Alive) return;

            ward.Fuel = Math.Min(ward.Capacity, ward.Fuel + ward.Model.Magnitude / 10f);
        }

        /// <summary>
        /// Burns down whatever is alight.
        ///
        /// <para>
        /// <b>The remainder is carried rather than dropped</b> (<c>SiegeRaider.Smoulder</c>),
        /// because a burn is a rate per second sampled at a frame rate: truncating each frame
        /// would make an ember turret worth two-thirds of its authored strength on a fast phone
        /// and nothing at all on a slow one, which is a difficulty that varies with the device.
        /// </para>
        /// </summary>
        void Smoulder(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];

                if (raider.Chill > 0f) raider.Chill = Math.Max(0f, raider.Chill - dt);
                if (!raider.Alive || raider.Burn <= 0f) continue;

                float span = Math.Min(dt, raider.Burn);
                raider.Burn -= span;
                raider.Smoulder += raider.BurnRate * span;

                int took = (int)raider.Smoulder;
                if (took < 1) continue;

                raider.Smoulder -= took;
                raider.Health -= took;

                bool killed = Fell(raider);

                _report.Bolts.Add(new SiegeBolt(raider.BurnFrom, raider.Id, took, false, killed,
                                                true));
            }
        }

        /// <summary>Scratch for <see cref="Arc"/>. One list, because a bolt is resolved at once.</summary>
        readonly HashSet<int> _arced = new HashSet<int>();
    }
}
