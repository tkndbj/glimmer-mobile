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
        /// Seconds this ward stands chained, having been bound by a shackler.
        ///
        /// <b>A second countdown rather than a second meaning for <see cref="Dark"/>, because the
        /// two are opposites and a player has to be able to tell them apart.</b> A douse takes the
        /// fuel with the seconds and is answered by pouring more in; a bind takes only the seconds
        /// and pouring is not an answer — what goes in <em>banks</em> and lets go when the chain
        /// does. Folded into one field, a surge would silently lift a shackle
        /// (<c>SiegeBoard.Surge</c> clears <see cref="Dark"/> on purpose) and the one boss whose
        /// whole verb is "this cannot be bought back" would be answerable for eight gems.
        /// </summary>
        public float Bound;

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

        /// <summary>Whether it is standing, loaded, and chained. See <see cref="Bound"/>.</summary>
        public bool Shackled => Alive && Bound > 0f;

        /// <summary>Whether it can get a bolt away. An upgraded ward needs less to do it.</summary>
        public bool Fuelled => Alive && !Doused && !Shackled && !Buried
                            && Fuel >= SiegeTuning.FuelShot(Rank);

        /// <summary>
        /// Pieces of rubble standing on this ward, having been buried by a colossus. Nought is
        /// a clear post.
        ///
        /// <b>A count of taps rather than a countdown</b>, and that is the whole difference
        /// between this and <see cref="Bound"/>: a chain runs out on the clock and a burial runs
        /// out when the player has dug, so the two are two fields for the reason <see cref="Dark"/>
        /// and <see cref="Bound"/> are. What a buried ward keeps is everything - fuel poured in
        /// banks, charges wait - exactly as a chained one does; what it costs is the taps.
        /// </summary>
        public int Rubble;

        /// <summary>Whether it is standing, loaded, and under rubble. See <see cref="Rubble"/>.</summary>
        public bool Buried => Alive && Rubble > 0;

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

            // **A boss wears no colour**, so every ward reaches it at full weight
            // (`SiegeTuning.BossReachTenths`, 37dn) and no ward is doubled against it.
            if (SiegeTuning.EveryWardReaches(at.Kind)) return SiegeTuning.BossReachTenths;

            return ReachTenths(at.Colour);
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

        /// <summary>
        /// Whether an overcharge is ready to be thrown.
        ///
        /// <b>A chained ward is not, and that is what makes a bind take the seconds rather than
        /// the tempo.</b> A shackle that left the tap live would be answered by spending whatever
        /// was banked the moment it landed, which turns "this ward is offline for six seconds"
        /// into "press the button you were going to press anyway" — invariant 5d, on the one boss
        /// whose entire verb is the seconds. The charge is kept, not lost: it is there when the
        /// chain comes off.
        /// </summary>
        public bool Armed => Alive && !Shackled && !Buried && Charges > 0;

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
        /// Chains this ward: what a shackler's spell does when it lands.
        ///
        /// <b>One line, and what it does <em>not</em> do is the mechanic.</b> Beside
        /// <see cref="Snuff"/> the difference is the whole of the difference between the two
        /// bosses: a douse empties the tube and a bind leaves it exactly as full as it was, still
        /// filling, still banking a charge when it brims. So the fuel a player pours into a
        /// chained ward is not wasted and is not available either — which is the decision
        /// (invariant 26h), and the reason no utility answers this.
        /// </summary>
        public void Shackle() => Bound = SiegeTuning.ShacklerBind;

        /// <summary>
        /// Buries this ward: what a colossus's boulder does when it lands.
        ///
        /// <b>Set, never added</b>, for the hourglass's reason: a second boulder on a ward still
        /// under the first is a fresh pile rather than a taller one, so a colossus cannot bury a
        /// post so deep that no amount of tapping reaches it.
        /// </summary>
        public void Bury() => Rubble = SiegeTuning.RubbleTaps;

        /// <summary>
        /// Takes one piece of rubble off, answering whether one came off.
        ///
        /// The player's half of a burial. Nought is the floor, so a tap on a clear post is
        /// refused rather than counted.
        /// </summary>
        public bool Dig()
        {
            if (Rubble <= 0) return false;
            Rubble--;
            return true;
        }

        /// <summary>
        /// Takes every banked charge off this ward, answering how many came off.
        ///
        /// A thunderer's half of a drain: the charges are not spent and not thrown, they are
        /// gone, and what they were worth comes back on the ward as damage
        /// (<c>SiegeTuning.ThundererDrain</c>).
        /// </summary>
        public int Drain()
        {
            int taken = Charges;
            Charges = 0;
            return taken;
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
