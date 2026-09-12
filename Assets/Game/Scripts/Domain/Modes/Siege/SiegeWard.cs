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
        public int Health;
        public bool Alive = true;

        /// <summary>
        /// What this turret holds when it is whole — its own, not the mode's.
        ///
        /// <b>Read once when the ward is built and then asked by everything that repairs or draws
        /// it.</b> A mending, a rally and the health bar all used the mode's constant, which was
        /// the same number while every turret was; with a roster that trades toughness for weight
        /// it would cap a tough turret's repairs at the baseline and draw its bar as overfull.
        /// </summary>
        public readonly int Full;

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
            : this(colour, new Wards.WardBuild(model ?? Wards.WardCatalog.Default.Starter)) { }

        /// <summary>
        /// One ward, standing the turret a player chose at the star they have taken it to.
        ///
        /// <b>Its figures are read once, here</b>, exactly as its capacity always was: a bolt's
        /// weight and a chassis's health are properties of what is standing, not questions to ask
        /// a ledger mid-run — and a ledger can move under a run when a sync lands.
        /// </summary>
        public SiegeWard(int colour, Wards.WardBuild build)
        {
            Colour = colour;
            Build = build.Has ? build : new Wards.WardBuild(Wards.WardCatalog.Default.Starter);
            Model = Build.Model;
            Capacity = SiegeTuning.CapacityOf(Model);
            Full = SiegeTuning.HealthOf(Build);
            Health = Full;
        }

        /// <summary>What is standing here: the turret and how far it has been upgraded.</summary>
        public readonly Wards.WardBuild Build;

        /// <summary>How far this turret has been upgraded, one to five.</summary>
        public int Stars => Build.Stars;

        /// <summary>Whether it is standing but smothered. Fuel poured in is still fuel.</summary>
        public bool Doused => Alive && Dark > 0f;

        /// <summary>Whether it can get a bolt away. An upgraded ward needs less to do it.</summary>
        public bool Fuelled => Alive && !Doused && Fuel >= SiegeTuning.FuelShot(Rank);

        /// <summary>What this turret does beyond firing.</summary>
        public Wards.WardAbility Ability => Model.Ability;

        /// <summary>
        /// How many colours besides its own this turret is strong against.
        ///
        /// <para>
        /// <b>The ability's magnitude, and it is what tells a prism's two rungs apart.</b> The
        /// field was read by nothing for as long as a prism meant "its own colour and the next",
        /// so a sixteen-hundred-gem spectrum and a nine-thousand-credit prism were one turret at
        /// two prices - the decoration invariant 5d names, on the one thing a player pays for.
        /// An unauthored nought still means one, so an older file and a rolled-back client both
        /// read a prism as the pair they have always drawn.
        /// </para>
        /// <para>
        /// <b>Capped below the number of colours there are</b>, because a turret strong against
        /// all four would not be widening the mode's central rule, it would be deleting it: the
        /// elemental double is what makes the colour of a match matter at all.
        /// </para>
        /// </summary>
        public int Partners
        {
            get
            {
                if (Ability != Wards.WardAbility.Prism) return 0;

                int want = Model.Magnitude <= 0 ? 1 : Model.Magnitude;
                int most = SiegeLayout.Letters.Length - 1;

                return want > most ? most : want;
            }
        }

        /// <summary>
        /// The first colour this turret is strong against besides its own, or -1.
        ///
        /// <b>The colour after its own, and it is arithmetic rather than authored.</b> A prism
        /// answering an authored pairing would be a content field with one legal answer per
        /// colour, and a pairing that could be retuned would move which half of a hill a player's
        /// line answers without the line having changed.
        /// </summary>
        public int Partner
            => Partners > 0 ? (Colour + 1) % SiegeLayout.Letters.Length : -1;

        /// <summary>Whether a bolt at this raider's colour counts as the strong one.</summary>
        public bool StrongAgainst(int colour)
        {
            if (colour == Colour) return true;

            for (int step = 1; step <= Partners; step++)
                if (colour == (Colour + step) % SiegeLayout.Letters.Length) return true;

            return false;
        }

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
