using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>One ward on the line.</summary>
    public sealed class SiegeWard
    {
        /// <summary>Which of <see cref="SiegeLayout.Letters"/> it burns, as an index.</summary>
        public readonly int Colour;

        public float Fuel;
        public int Health = SiegeTuning.WardHealth;
        public bool Alive = true;

        /// <summary>Seconds until its next bolt.</summary>
        public float Cool;

        /// <summary>
        /// How many cogs have been spent on this ward, nought to <see cref="SiegeTuning.MaxRank"/>.
        ///
        /// <b>Rank rather than level, so the arithmetic has no off-by-one in it.</b> Every table
        /// that reads it is a multiplier on nought (<see cref="SiegeTuning.DamageAt"/>,
        /// <see cref="SiegeTuning.FuelShotTenths"/>); the number a <em>player</em> is shown is
        /// <see cref="Level"/>, which is this plus one, and it exists exactly once so a badge and
        /// a bolt can never disagree about what tier a turret is.
        /// </summary>
        public int Rank;

        /// <summary>What the badge on this ward says: one to five.</summary>
        public int Level => Rank + 1;

        /// <summary>
        /// Seconds this ward stands dark, having been doused by a blightcaller.
        ///
        /// <b>A countdown rather than a flag</b>, because what a player has to read off it is
        /// <em>how long</em>: the decision the blightcaller asks is which colour to feed next, and
        /// that is only a decision if the answer changes as the seconds run out.
        /// </summary>
        public float Dark;

        /// <summary>
        /// The turret the player has stood on this colour.
        ///
        /// <para>
        /// <b>Handed in rather than looked up, and never null.</b> A board that reached into a
        /// static for the loadout would be a board no test could put a second line in front of —
        /// and <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> has to prove every rung is
        /// holdable with the <em>weakest</em> line a player could bring, which is the only version
        /// of that proof worth having.
        /// </para>
        /// </summary>
        public readonly Wards.WardModel Model;

        /// <summary>
        /// How much fuel this turret holds. <c>SiegeTuning.WardCapacity</c> plus whatever a beacon
        /// adds.
        ///
        /// <b>Per ward rather than a constant</b>, because it is the one thing an ability changes
        /// that is not a hit: a beacon banks a cascade a plain ward would have spilled.
        /// </summary>
        public readonly float Capacity;

        /// <summary>
        /// Bolts this turret has fired, ever.
        ///
        /// Only a pierce reads it, and it counts every bolt rather than resetting: a counter that
        /// restarted when the ward ran dry would make a pierce arrive on a schedule the player
        /// could not follow.
        /// </summary>
        public int Shots;

        public SiegeWard(int colour, Wards.WardModel model = null)
        {
            Colour = colour;
            Model = model ?? Wards.WardCatalog.Default.Starter;
            Capacity = SiegeTuning.CapacityOf(Model);
        }

        /// <summary>Whether it is standing but smothered. Fuel poured in is still fuel.</summary>
        public bool Doused => Alive && Dark > 0f;

        /// <summary>Whether it can get a bolt away. An upgraded ward needs less to do it.</summary>
        public bool Fuelled => Alive && !Doused && Fuel >= SiegeTuning.FuelShot(Rank);

        /// <summary>What this turret does beyond firing.</summary>
        public Wards.WardAbility Ability => Model.Ability;

        /// <summary>
        /// The second colour this turret is strong against, or -1.
        ///
        /// <b>The colour after its own, and it is arithmetic rather than authored.</b> A prism
        /// answering an authored pairing would be a content field with one legal answer per
        /// colour, and a pairing that could be retuned would move which half of a hill a player's
        /// line answers without the line having changed.
        /// </summary>
        public int Partner
            => Ability == Wards.WardAbility.Prism ? (Colour + 1) % SiegeLayout.Letters.Length : -1;

        /// <summary>Whether a bolt at this raider's colour counts as the strong one.</summary>
        public bool StrongAgainst(int colour) => colour == Colour || colour == Partner;

        public float Charge => Capacity <= 0f ? 0f : Fuel / Capacity;

        /// <summary>Whether another cog would be worth anything to this ward.</summary>
        public bool Upgradable => Alive && Rank < SiegeTuning.MaxRank;

        /// <summary>
        /// Puts this ward out: what a blightcaller's spell does when it lands.
        ///
        /// It takes the fuel <em>and</em> the seconds, because taking only one of the two is not
        /// a mechanic — emptying a full ward it is about to fire from costs nothing a moment
        /// later, and smothering a ward with nothing in it costs nothing at all.
        /// </summary>
        public void Snuff()
        {
            Fuel = 0f;
            Dark = SiegeTuning.Douse;
        }

        /// <summary>
        /// Knocks a rank off: what an overlord's spell does on top of its damage.
        ///
        /// Clamped at nought and answering whether it really took one, so a view can draw the
        /// badge falling and say nothing when there was nothing to take.
        /// </summary>
        public bool Sunder()
        {
            if (Rank <= 0) return false;

            Rank -= SiegeTuning.OverlordSunder;
            if (Rank < 0) Rank = 0;
            return true;
        }
    }
}
