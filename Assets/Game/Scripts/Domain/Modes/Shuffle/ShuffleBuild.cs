using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The cards a run holds, and the one <see cref="SiegeBoosts"/> they fold into.
    ///
    /// <para>
    /// <b>A build is a function of the cards held and never of the order they came in.</b> Every
    /// take puts the boosts back to the plain line and folds every copy of every card on again
    /// (<see cref="Compile"/>), so two runs holding the same cards play the same board whatever
    /// hands dealt them - which is what makes a build a thing a player can reason about, and
    /// what makes <c>ShuffleBuildTests</c> able to hold each card to the figure it names.
    /// </para>
    /// <para>
    /// <b>The one object the board reads is this build's <see cref="Boosts"/></b>, handed to
    /// <c>SiegeBoard.Boosts</c> once when the run opens and written in place thereafter, so a
    /// card taken between two bolts reaches the second with no re-attach and no second copy.
    /// </para>
    /// </summary>
    public sealed class ShuffleBuild
    {
        readonly Dictionary<string, int> _held = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<ShuffleCard> _taken = new List<ShuffleCard>(24);

        /// <summary>What the board reads. One object for the life of the run.</summary>
        public SiegeBoosts Boosts { get; }

        public ShuffleBuild(uint seed)
        {
            Boosts = new SiegeBoosts(seed);
        }

        /// <summary>Every card taken, in the order it was taken, copies repeated.</summary>
        public IReadOnlyList<ShuffleCard> Taken => _taken;

        /// <summary>How many copies of <paramref name="card"/> this build holds.</summary>
        public int Copies(ShuffleCard card)
            => card != null && _held.TryGetValue(card.Id, out int n) ? n : 0;

        /// <summary>Whether another copy of <paramref name="card"/> could still be taken.</summary>
        public bool Takeable(ShuffleCard card) => card != null && Copies(card) < card.Most;

        /// <summary>Distinct cards held, for a readout.</summary>
        public int Distinct => _held.Count;

        /// <summary>
        /// Takes a card, answering whether it was taken. A card at its most is refused rather
        /// than clamped, so a hand that offered one is the fault and not the fold.
        /// </summary>
        public bool Take(ShuffleCard card)
        {
            if (!Takeable(card)) return false;

            _held[card.Id] = Copies(card) + 1;
            _taken.Add(card);
            Compile();
            return true;
        }

        /// <summary>The fold: the plain line, then every copy of every card held.</summary>
        public void Compile()
        {
            Boosts.Reset();

            for (int i = 0; i < _taken.Count; i++) Fold(Boosts, _taken[i]);
        }

        /// <summary>
        /// One copy of one card onto the boosts. The one switch that says what a card does.
        ///
        /// <b>Every arm is a named setter on <see cref="SiegeBoosts"/></b>, so a card cannot
        /// reach a figure the board does not read, and a figure the board reads is reachable by
        /// a card only through here. The stacking cards use <c>Add</c>; the ones whose second
        /// copy means a bigger single effect use <c>Set</c> with the copy count folded in, which
        /// is why this reads the build's own count for them.
        /// </summary>
        public void Fold(SiegeBoosts into, ShuffleCard card)
        {
            if (into == null || card == null) return;

            int n = Copies(card);
            if (n < 1) n = 1;

            switch (card.Effect)
            {
                case ShuffleEffect.Damage: into.AddDamage(card.Magnitude); break;
                case ShuffleEffect.Fire: into.AddFire(card.Magnitude); break;
                case ShuffleEffect.FuelShot: into.AddFuelShot(-card.Magnitude); break;
                case ShuffleEffect.FuelGem: into.AddFuelGem(card.Magnitude); break;
                case ShuffleEffect.Guard: into.AddGuard(card.Magnitude); break;
                case ShuffleEffect.Capacity: into.AddCapacity(card.Magnitude); break;
                case ShuffleEffect.Armour: into.AddArmour(card.Magnitude); break;
                case ShuffleEffect.Crit: into.AddCrit(card.Magnitude, card.Extent); break;
                case ShuffleEffect.Rest: into.AddRest(card.Magnitude); break;
                case ShuffleEffect.Siphon: into.AddSiphon(card.Magnitude); break;
                case ShuffleEffect.Thorns: into.AddThorns(card.Magnitude); break;
                case ShuffleEffect.Leech: into.AddLeech(card.Magnitude); break;
                case ShuffleEffect.HillPace: into.AddHillPace(-card.Magnitude); break;
                case ShuffleEffect.Heavy: into.AddHeavy(card.Magnitude); break;
                case ShuffleEffect.SecondWind: into.AddSecondWinds(card.Magnitude); break;
                case ShuffleEffect.Phoenix: into.AddPhoenix(card.Magnitude); break;
                case ShuffleEffect.Spill: into.AddSpill(card.Magnitude); break;

                // **One fold per build rather than per copy**, read off the copy count: these are
                // rules with a strength, and the strength climbs with the copies.
                case ShuffleEffect.Regen:
                    into.SetRegen(card.Magnitude / 10f / n);
                    break;

                case ShuffleEffect.Splash:
                    into.SetSplash(card.Magnitude + (n - 1) * 2, card.Extent);
                    break;

                case ShuffleEffect.Chain:
                    into.SetChain(card.Magnitude, card.Extent * n);
                    break;

                case ShuffleEffect.Pierce:
                    into.SetPierce(card.Extent - (n - 1) > 2 ? card.Extent - (n - 1) : 2, card.Magnitude);
                    break;

                case ShuffleEffect.Frost:
                    into.SetFrost(card.Magnitude + (n - 1) * 2, card.Extent / 10f);
                    break;

                case ShuffleEffect.Burn:
                    into.SetBurn(card.Magnitude * n, card.Extent / 10f);
                    break;

                case ShuffleEffect.Stun:
                    into.SetStun(card.Magnitude * n, card.Extent / 10f);
                    break;

                case ShuffleEffect.Charges:
                    into.AddOvercharge(card.Magnitude);
                    if (n == 1) into.AddCharges(card.Extent);
                    break;

                case ShuffleEffect.WaveFuel:
                    into.SetWaveFuel(card.Magnitude * n);
                    break;

                case ShuffleEffect.WaveStill:
                    into.SetWaveStill((card.Magnitude + (n - 1) * 15) / 10f);
                    break;

                case ShuffleEffect.Repel:
                    into.SetRepel(card.Magnitude * n);
                    break;

                case ShuffleEffect.Execute:
                    into.SetExecute(card.Magnitude + (n - 1) * 10);
                    break;

                case ShuffleEffect.Twin:
                    into.SetTwin(card.Magnitude * n, card.Extent);
                    break;

                case ShuffleEffect.OffColour:
                    into.SetOffColour(card.Magnitude);
                    break;

                case ShuffleEffect.Blast:
                    into.SetBlast(card.Magnitude * n);
                    break;

                case ShuffleEffect.Hex:
                    into.SetHex(card.Magnitude * n, card.Extent / 10f);
                    break;

                case ShuffleEffect.WaveCharges:
                    into.SetWaveCharges(card.Magnitude * n);
                    break;

                case ShuffleEffect.Overdrive:
                    into.AddDamage(card.Magnitude);
                    into.AddFire(card.Extent);
                    break;

                case ShuffleEffect.Desperate:
                    into.SetDesperate(card.Magnitude, card.Extent);
                    break;

                // ---------------------------------------------------------- the attack cards
                // Rules with a strength, read off the copy count like the arms above: a second
                // copy is the same attack, more of it - another pellet, another bounce, another
                // missile, a quake more often, a deeper pool, another arc, a faster spin, a wider
                // burst, a sooner meteor or vortex.
                case ShuffleEffect.Scatter:
                    into.SetScatter(card.Magnitude + (n - 1), card.Extent + (n - 1));
                    break;

                case ShuffleEffect.Ricochet:
                    into.SetRicochet(card.Magnitude, card.Extent * n);
                    break;

                case ShuffleEffect.Missiles:
                    into.SetMissiles(card.Extent, n + 1, card.Magnitude);
                    break;

                case ShuffleEffect.Quake:
                    into.SetQuake(card.Extent - (n - 1) > 3 ? card.Extent - (n - 1) : 3,
                                  card.Magnitude + (n - 1) * 2);
                    break;

                case ShuffleEffect.Toxic:
                    into.SetToxic(card.Magnitude * n, card.Extent / 10f);
                    break;

                case ShuffleEffect.Tesla:
                    into.SetTesla(card.Magnitude + (n - 1) * 2, card.Extent + (n - 1));
                    break;

                case ShuffleEffect.SpinUp:
                    into.SetSpin(card.Magnitude * n, card.Extent);
                    break;

                case ShuffleEffect.Shatter:
                    into.SetShatter(card.Magnitude + (n - 1) * 2, card.Extent + (n - 1));
                    break;

                case ShuffleEffect.Wildfire:
                    into.SetWildfire(card.Extent + (n - 1), card.Magnitude * n);
                    break;

                case ShuffleEffect.Meteor:
                    into.SetMeteor(card.Extent / 10f - (n - 1) * 1.5f, card.Magnitude);
                    break;

                case ShuffleEffect.Vortex:
                    into.SetVortex(card.Extent / 10f - (n - 1) * 2f, card.Magnitude + (n - 1) * 2);
                    break;

                case ShuffleEffect.Nuke:
                    into.SetNuke(card.Extent, card.Magnitude);
                    break;

                case ShuffleEffect.Ray:
                    into.SetRay(card.Extent / 10f, card.Magnitude);
                    break;

                case ShuffleEffect.Hydra:
                    into.SetHydra(card.Magnitude, card.Extent);
                    break;

                case ShuffleEffect.Wild:
                    into.SetWild(card.Magnitude);
                    break;

                // Two figures the line already has, moved together as far as no common card may:
                // a machine gun that does not run dry.
                case ShuffleEffect.BulletHell:
                    into.AddFire(card.Magnitude);
                    into.AddFuelShot(-card.Extent);
                    break;

                default:
                    throw new InvalidOperationException($"card '{card.Id}' names an effect the fold does not know");
            }
        }
    }
}
