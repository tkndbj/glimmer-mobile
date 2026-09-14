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
        /// What a bolt from this ward is worth against <paramref name="colour"/>, in tenths.
        /// Nought means it will not fire at it at all.
        ///
        /// <para>
        /// <b>One predicate rather than two</b>, because "may I shoot this" and "for how much" are
        /// one question under the lock — and the raider overload below is where the one exception
        /// to it lives.
        /// </para>
        /// <para>
        /// <b>It is its own colour and nothing else, and the shelf no longer has a way to widen
        /// that.</b> A prism used to reach the next colour round for a share of a hit; with a line
        /// standing one turret per colour, the seat beside it was already answering that colour at
        /// full weight, so what the ability bought was the moments its own colour happened to be
        /// clear. Withdrawn on the owner's reading and replaced by
        /// <see cref="Wards.WardAbility.Stun"/> on both its rungs (invariant 5d, asked of a
        /// purchase).
        /// </para>
        /// </summary>
        public int ReachTenths(int colour) => colour == Colour ? 10 : 0;

        /// <summary>
        /// What a bolt from this ward is worth against <paramref name="at"/>, in tenths. Nought
        /// means it will not fire at it at all.
        ///
        /// <para>
        /// <b>The raider rather than its colour, and this is the door every played bolt goes
        /// through.</b> One rule on the hill is not a fact about a colour — a boss is answered by
        /// the whole line whatever it wears (<see cref="SiegeTuning.EveryWardReaches"/>) — and a
        /// caller that asked the colour overload would put that rule back in the one place it must
        /// not be: spread across the two sites that aim and the one that fires.
        /// </para>
        /// <para>
        /// <b>The better of the two readings, never their sum.</b> A ward whose own colour the
        /// boss happens to be wearing reaches it in full rather than at a boss's baseline, and
        /// every other ward still lands the un-doubled bolt — so no turret is ever worse against a
        /// boss than the free one (invariant 42).
        /// </para>
        /// </summary>
        public int ReachTenths(SiegeRaider at)
        {
            if (at == null) return 0;

            int share = ReachTenths(at.Colour);
            if (!SiegeTuning.EveryWardReaches(at.Kind)) return share;

            return share > SiegeTuning.OffColourTenths ? share : SiegeTuning.OffColourTenths;
        }

        /// <summary>
        /// Whether a bolt from this ward lands on <paramref name="at"/> at its full doubled
        /// weight — what the view draws in gold and what <c>SiegeTuning.PerfectMatch</c> counts.
        ///
        /// <b>Its own colour and nothing else</b>, which is what keeps the double the thing a
        /// player is <em>told</em> about by the board: a duel answered with the wrong colour still
        /// kills the boss, in white numbers, at half the rate.
        /// </summary>
        public bool Doubles(SiegeRaider at) => ReachTenths(at) >= 10;

        /// <summary>
        /// Whether this ward can hurt <paramref name="colour"/> at all.
        ///
        /// <b>Kept for the bolt report and the preview bench, and deliberately narrow.</b> It used
        /// to mean "is this a double", which under the lock is true of every primary bolt in the
        /// mode and therefore says nothing. What it means now is <em>would this turret fire at
        /// that</em>.
        /// </summary>
        public bool StrongAgainst(int colour) => ReachTenths(colour) > 0;

        /// <summary>
        /// Overcharges this ward is holding: full tubes it has banked and not yet thrown.
        ///
        /// <para>
        /// <b>A stored charge rather than a full tube, and the difference is the whole feature.</b>
        /// It shipped as "the tube is full" and was <em>unusable</em>: a ward fires the instant it
        /// has fuel and a target, so the only way to reach the brim was for its colour to be off
        /// the hill — and an overcharge over an empty hill has nothing to throw at. Reported after
        /// one session as exactly that: <em>it is impossible to use</em>.
        /// </para>
        /// <para>
        /// <b>So a full tube <em>converts</em>.</b> The fuel comes out of the tube and goes in here,
        /// the tube carries on filling for ordinary bolts, and the charge waits until it is thrown.
        /// That is what the owner asked for — <em>it stays even if the turret starts shooting</em> —
        /// and it is also what keeps the thing free of par: the fuel that became a charge can never
        /// also be fired as bolts, so an overcharge moves damage the player already matched for and
        /// conjures none (invariant 39).
        /// </para>
        /// </summary>
        public int Charges;

        /// <summary>Whether an overcharge is ready to be thrown.</summary>
        public bool Armed => Alive && Charges > 0;

        /// <summary>
        /// Pours fuel in, and banks a charge for every whole tube it fills.
        ///
        /// <para>
        /// <b>One door, because there are two ways in.</b> Fuel arrives from a match landing
        /// (<c>SiegeBoard.Land</c>) and from a surge being poured (<c>SiegeBoard.Surge</c>), and a
        /// conversion written at one of them is a ward that can never bank from the other.
        /// </para>
        /// <para>
        /// The overflow is <em>carried</em> rather than dropped, so a cascade that fills a tube and
        /// a half leaves the half in the tube. At the cap the tube clamps exactly as it always did —
        /// charges are bounded and fuel is not a leak.
        /// </para>
        /// </summary>
        public bool Fill(float fuel)
        {
            if (fuel <= 0f) return false;

            Fuel += fuel;

            bool banked = false;

            while (Fuel >= Capacity && Charges < SiegeTuning.MostCharges)
            {
                Fuel -= Capacity;
                Charges++;
                banked = true;
            }

            if (Fuel > Capacity) Fuel = Capacity;

            return banked;
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
