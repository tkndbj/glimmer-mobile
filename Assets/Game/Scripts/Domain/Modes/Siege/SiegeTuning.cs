using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Every number this mode runs on, in one place.
    ///
    /// <para>
    /// <b>Constants rather than content, and that is the shape invariant 20d asks for.</b> A level
    /// authors what is standing there and what is coming; how much a match is worth and how hard
    /// a bolt lands are facts about the <em>mode</em>, so a retune is one edit and cannot leave
    /// two levels disagreeing about what a gem does. Par is derived from them, so moving one of
    /// these moves every star line in the mode at once — which is correct, and is why they are
    /// here where that is obvious rather than spread over a board and a view.
    /// </para>
    /// </summary>
    public static class SiegeTuning
    {
        /// <summary>Gems that have to line up. Three, as every game of this shape.</summary>
        public const int MinRun = 3;

        /// <summary>
        /// Fuel a matched gem is worth, in tenths.
        ///
        /// <para>
        /// <b>Two fuel a gem against a bolt's one, which is the whole of "the wards should shoot
        /// more".</b> It was one each and a bolt cost one, so a gem was exactly a bolt — an
        /// identity nothing said out loud and that <see cref="PerfectMatch"/> quietly depended on.
        /// Doubling it and halving <see cref="ShotDamage"/> is the same match delivering the same
        /// damage as <em>twice as many, half as heavy</em> bolts: a fed ward stays alight for
        /// twice as long, which is what a player watches, and not one graded number moves.
        /// </para>
        /// <para>
        /// <b>Subdividing the fuel unit rather than making a bolt cost half is what keeps the rank
        /// ladder exact.</b> A rank is ten per cent less fuel a bolt
        /// (<see cref="FuelShotTenths"/>), which at a base of ten tenths is 10, 9, 8, 7, 6 — five
        /// exact integers. Halving the bolt to five tenths would make the same ladder 5, 4, 4, 3, 3
        /// after truncation, so two pairs of ranks would buy nothing and the 2.33× a rank-four ward
        /// is worth would quietly stop being true. Everything measured in <em>fuel</em> doubles
        /// (this, <see cref="WardCapacity"/>, the surge's authored magnitude); everything measured
        /// <em>per bolt</em> is untouched.
        /// </para>
        /// </summary>
        public const int FuelPerGemTenths = 20;

        /// <summary>Fuel a matched gem is worth. See <see cref="FuelPerGemTenths"/>.</summary>
        public const float FuelPerGem = FuelPerGemTenths / 10f;

        /// <summary>
        /// The most fuel a ward holds. A big match tops it up rather than banking.
        ///
        /// <b>In fuel, so it doubled with the unit</b> — it is fourteen <em>gems</em>' worth
        /// before and after, which is what it was chosen as and what a player experiences.
        /// </summary>
        public const float WardCapacity = 28f;

        // **Fuel used to fade on a clock and no longer does.** The rule was that a standing
        // ward lost fuel every second whether or not it was shooting, so a colour matched early
        // was a colour wasted and the question was always which ward wanted feeding *now*. It was
        // withdrawn by the owner after playing it: what it actually produced was a meter draining
        // while nothing was happening, which reads as the game taking something away rather than
        // as a reason to hurry. Fuel now leaves a ward one way only - as a bolt - so a match is
        // worth what it is worth whenever it is spent, and what makes the colour of a match a
        // decision is the elemental double and the capacity below rather than a clock.
        //
        // Note what that costs and what it does not. It costs the banking pressure, so a player
        // may now feed a ward before its colour arrives; `WardCapacity` is what stops that being
        // unlimited, and it is the number to move if hoarding turns out to be the whole game.
        // It costs nothing about par, which counts matches and never counted time.

        /// <summary>
        /// Seconds between a ward's bolts while it has fuel.
        ///
        /// <b>It buys no damage and it buys the whole feel.</b> A bolt costs one fuel whatever the
        /// cadence, so a ward with three fuel does twelve damage at any speed - what this decides
        /// is whether feeding a ward reads as *opening fire* or as a lamp ticking over.
        ///
        /// <para>
        /// <b>It was .14 and that was too fast, which is the owner's verdict after playing it.</b>
        /// Seven bolts a second per ward is twenty-eight across a lit line, and at that rate a
        /// bolt is not an event - it is a hose, and nothing crossing the hill can be looked at.
        /// The fix is here rather than in the view, because a shorter flight or a smaller bolt
        /// would be treating the symptom: what was wrong is how often the thing happens. At .22 a
        /// ward still opens fire rather than ticking over, and a bolt has all but landed before
        /// the next one leaves (<c>SiegeView.LongestFlight</c> is .20).
        /// </para>
        /// <para>
        /// <b>And it is not free, which is why it went back through the hold simulation.</b> Fuel
        /// still buys the same damage, but it arrives later - and the raiders do not wait, so a
        /// slower line lets them further down the hill and takes more blows. See
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which is this mode's only
        /// instrument (invariant 37j). It is also what picked that number: .20 and .22 hold the
        /// line, and <b>.26 loses it outright</b> - the hill unclear and every ward down. So the
        /// band between "too fast to watch" and "too slow to hold" is narrow, and anything slower
        /// than this has to buy the time back somewhere else.
        /// </para>
        /// <para>
        /// <b>And it stayed at .22 when a bolt's damage halved, which is the whole of what that
        /// change was for.</b> A gem bought two bolts (<see cref="FuelPerGemTenths"/>) and each
        /// was worth half (<see cref="ShotDamage"/>), so at an unchanged cadence a fed ward
        /// <em>kept firing for twice as long</em> — which is what "see them shoot more" means.
        /// Halving this as well was tried then and undoes exactly that: the same bolts go through
        /// the same window twice as densely and a ward stops firing when it always did, so there
        /// is nothing more to watch. <b>The rate is what makes a ward's fire last; the fuel is
        /// what makes it long.</b>
        /// </para>
        /// <para>
        /// <b>What that cost is peak damage, and the levels paid it rather than this number.</b>
        /// Bolts a second times damage a bolt is the line's output, so half-weight bolts at that
        /// cadence was half the <em>peak</em> — which barely touches attrition (a ward lit twice
        /// as long kills a marching column better, because less of each burst is spent as
        /// overkill) and bites hard on an emergency, where one big health pool has to be answered
        /// at once. Measured through <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, and it
        /// moved three things in three different directions: a duel got dearer
        /// (<c>s01_warlordsgate</c> wanted more hill and got it), a long attrition rung got
        /// <em>safer</em> until nothing reached its line at all (<c>s01_thornsiege</c>, two more
        /// brutes), and the rule-test fixture — the one siege in this project that sent a boss and
        /// dealt no cogs — stopped being holdable, and now deals them like every shipped rung.
        /// </para>
        /// <para>
        /// <b>It is .44 now, because the owner's next verdict was that the mode reads as super
        /// fast paced — and what paid for it is the exact inverse of the change above rather than
        /// a cut to the line's output.</b> Slowing this <em>alone</em> was measured first and is
        /// not available: fuel still buys the same damage but it arrives later, and the raiders do
        /// not wait, so the line's damage a second falls with the cadence. <b>At .24 the
        /// warbringer rung was lost outright</b> — the hill unclear after ten minutes and every
        /// ward down — because a boss that shells the line every nine seconds grinds it for as
        /// long as the fight lasts, and a slower line makes the fight last longer. That is a
        /// difficulty change, and a large one, which is not what "shoot a little slower" asks for.
        /// </para>
        /// <para>
        /// <b>So a bolt got twice as heavy in the same breath</b> (<see cref="ShotDamage"/> 10 to
        /// 20) <b>and costs twice the fuel</b> (<see cref="FuelPerShotTenths"/> 10 to 20), which
        /// holds every one of the things a player can feel exactly where it was: a match still
        /// delivers <see cref="PerfectMatch"/>, a fed ward still fires for the same 2.4 seconds
        /// (half as many bolts at twice the interval), a full tube still empties in the same 6.2
        /// (so <see cref="WardCapacity"/> needs no change for the same reason), and a surge still
        /// pours the same seconds of fire for the same charge. What moves is the one thing that
        /// was asked to move: <b>half as many bolts, each twice as heavy.</b> Par, both star
        /// lines, every utility's charge and every rung of the chapter came back bit-identical
        /// through the hold simulation, which is what says this one was free.
        /// </para>
        /// <para>
        /// <b>Before reaching for this number again, work out which half of "too fast" is being
        /// complained about.</b> Bolts a second is this; how long a ward keeps firing is the fuel.
        /// Moving this on its own moves the line's output and is a chapter retune; moving it
        /// against the fuel unit is free. And note what it costs the view: at .44 a bolt lands
        /// before the next one leaves (<c>SiegeView.LongestFlight</c> is .40), so a ward no longer
        /// holds two in the air — which is the picture the change was asked for.
        /// </para>
        /// </summary>
        public const float FireEvery = .44f;

        /// <summary>
        /// Fuel one bolt spends at rank nought, in tenths.
        ///
        /// <para>
        /// <b>It was unchanged when the fuel unit was subdivided</b>, which was the point of
        /// having done it that way round: every rank the cogs pay for stayed bit-identical, and
        /// what moved was how much fuel a <em>gem</em> is worth.
        /// </para>
        /// <para>
        /// <b>Twenty now, and it is this number rather than the cadence that lets the wards shoot
        /// slower without shooting weaker.</b> A bolt costing two fuel is half as many bolts out
        /// of the same tube; paired with <see cref="ShotDamage"/> doubling and
        /// <see cref="FireEvery"/> doubling, a match delivers the same damage over the same
        /// seconds through half the bolts. <b>A multiple of ten is not optional</b> — a rank is
        /// ten per cent off this (<see cref="FuelShotTenths"/>), so a base that is not a multiple
        /// of ten truncates and some of the four cogs a player spends buy nothing, which is the
        /// failure the subdivision above existed to avoid.
        /// </para>
        /// </summary>
        public const int FuelPerShotTenths = 20;

        /// <summary>Fuel one bolt spends at rank nought. See <see cref="FuelShotTenths"/>.</summary>
        public const float FuelPerShot = FuelPerShotTenths / 10f;

        /// <summary>
        /// What one bolt takes off a raider it is not strong against, at rank nought.
        ///
        /// <para>
        /// <b>Twenty rather than two, and every raider's health went up by the same ten, so not
        /// one graded number moved.</b> Par is <see cref="Par"/> — health over
        /// <see cref="PerfectMatch"/> — and both sides of that division scaled together, so every
        /// shipped level's par, both its star lines and every utility's charge came out
        /// identical. What the scale buys is the one thing the old numbers could not represent: a
        /// <b>ten per cent</b> step. A ward that gains a rank fires for <c>ShotDamage * 11 / 10</c>,
        /// which at two is two and at twenty is twenty-two — so the upgrade the cogs pay for is
        /// exact integer arithmetic rather than a float three code generators round three ways
        /// (see <c>LevelTuning</c>, and this project's own hard-won note about
        /// <c>Mathf.CeilToInt(45 * 1.20f)</c>).
        /// </para>
        /// <para>
        /// <b>Then ten, because the owner asked to see the wards shoot more.</b> A gem bought two
        /// bolts rather than one (<see cref="FuelPerGemTenths"/>) and each was worth half, so the
        /// same match delivered the same damage over twice as many bolts and a fed ward stayed
        /// alight for twice as long. <see cref="PerfectMatch"/> was unmoved, so par, both star
        /// lines and every utility's charge were unmoved with it. Ten was the floor for the ten
        /// per cent step — at ten the ladder is 10, 11, 12, 13, 14, exact and with no room under
        /// it — so anything that halved it again had to give the rank ladder a finer unit first.
        /// </para>
        /// <para>
        /// <b>And twenty again, because the verdict after that was that the mode reads as super
        /// fast paced.</b> A bolt is twice as heavy, costs twice the fuel
        /// (<see cref="FuelPerShotTenths"/>) and leaves at twice the interval
        /// (<see cref="FireEvery"/>), so the wards fire half as often for exactly the same output
        /// — which is the only way to slow the shooting down that is not a chapter retune. See
        /// <see cref="FireEvery"/> for what slowing the cadence on its own cost.
        /// </para>
        /// <para>
        /// <b>A bolt's weight and a bolt's cost have to move together, and that is the trap
        /// here.</b> Moving this one alone changes what a match delivers and so halves or doubles
        /// every par in the mode, with each number still looking perfectly plausible — which is
        /// the identity <see cref="PerfectMatch"/> exists to say out loud.
        /// </para>
        /// </summary>
        public const int ShotDamage = 20;

        /// <summary>What a bolt is worth against a raider of its own colour.</summary>
        public const int WeakMultiplier = 2;

        // ------------------------------------------------------------------ the bulwark
        /// <summary>
        /// What a bolt of the <em>wrong</em> colour is worth against a shield, in tenths.
        ///
        /// <para>
        /// <b>Halved, and its own colour is not reduced at all</b> — which is what makes this the
        /// only raider on the hill whose answer is a colour rather than a quantity. Against a
        /// creeper the difference between the right ward and any other is
        /// <see cref="WeakMultiplier"/>: two to one. Against a bulwark it is <b>four</b> to one,
        /// because the wrong answer is also halved. That is a big enough spread that a player who
        /// keeps taking the biggest match on the field loses ground while one who reads the hill
        /// gains it, and it needs no explaining: the bolts visibly bounce.
        /// </para>
        /// <para>
        /// <b>The shield is pierced rather than worn down</b>, deliberately. A shield with its own
        /// health would be a second bar on a raider that already has one, and the player's answer
        /// to it would be "shoot it more" — which is what every other raider already asks for.
        /// Piercing keeps the whole mechanic in the one decision this mode is about.
        /// </para>
        /// </summary>
        public const int ShieldSoakTenths = 5;

        /// <summary>
        /// How much harder a rend turret of <paramref name="magnitude"/> bites into plating, in
        /// tenths, on top of not being blunted by it.
        ///
        /// <para>
        /// <b>Clamped rather than trusted</b>, because it is content: a magnitude nobody authored
        /// reads as nought and leaves the ability exactly what it used to be, so an older file
        /// and a rolled-back client both keep working, and a wild one cannot make a bolt worth
        /// more than a boss's health.
        /// </para>
        /// </summary>
        public static int RendBonus(int magnitude)
            => magnitude <= 0 ? 0 : (magnitude > 20 ? 20 : magnitude);

        /// <summary>
        /// What a bolt from a ward of <paramref name="rank"/> takes off <paramref name="kind"/>,
        /// given whether it is the raider's own colour.
        ///
        /// <b>One place, because a second copy is a second opinion about what a shield is worth.</b>
        /// The soak is applied after the double, so a bulwark's own colour reaches it in full.
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak)
            => DamageTo(kind, rank, weak, Wards.WardAbility.None, 0);

        /// <summary>
        /// What a bolt from a ward of <paramref name="rank"/> carrying <paramref name="ability"/>
        /// takes off <paramref name="kind"/>.
        ///
        /// <para>
        /// <b>Only one ability reaches the primary hit, and it can only ever raise it.</b> A rend
        /// turret is not blunted by a shield and takes a bonus against one on top, which makes it
        /// the answer to a rung sending bulwarks and worth precisely nothing on one that does not.
        /// Every other ability is an addition applied after this (see
        /// <c>SiegeBoard.Line.cs</c>), because a turret that hit softer would push three stars out
        /// of reach of whoever chose it.
        /// </para>
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak,
                                   Wards.WardAbility ability)
            => DamageTo(kind, rank, weak, ability, 0);

        /// <summary>
        /// The same, told how strong the ability is. See the overload above.
        ///
        /// <b>Only <see cref="Wards.WardAbility.Rend"/> reads the magnitude here</b>, and only
        /// against a bulwark; everything else is an addition applied after the hit has landed.
        /// </summary>
        /// <summary>
        /// What a bolt from <paramref name="model"/> at <paramref name="rank"/> takes off
        /// <paramref name="kind"/>.
        ///
        /// <b>The one door a played board goes through</b>, because a turret's ability, how strong
        /// that ability is and how heavy its bolt is are three fields of one record: passed
        /// separately they are three chances for a call site to hand over two of them.
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak, Wards.WardModel model)
            => DamageTo(kind, rank, weak, new Wards.WardBuild(model));

        /// <summary>
        /// What a bolt from <paramref name="build"/> at <paramref name="rank"/> takes off
        /// <paramref name="kind"/> — the turret's own weight with its upgrades in it.
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak, Wards.WardBuild build)
            => build.Model == null
             ? DamageFineTo(kind, rank, weak, Wards.WardAbility.None, 0,
                            Wards.WardModel.Baseline * 10)
             : DamageFineTo(kind, rank, weak, build.Model.Ability, build.Model.Magnitude,
                            build.PowerHundredths);

        public static int DamageTo(SiegeKind kind, int rank, bool weak,
                                   Wards.WardAbility ability, int magnitude)
            => DamageTo(kind, rank, weak, ability, magnitude, Wards.WardModel.Baseline);

        /// <summary>
        /// The same, told how heavy the bolt is. See <c>WardModel.PowerTenths</c>.
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak,
                                   Wards.WardAbility ability, int magnitude, int powerTenths)
            => DamageFineTo(kind, rank, weak, ability, magnitude, powerTenths * 10);

        /// <summary>The same, told the weight in hundredths. See <see cref="DamageFine"/>.</summary>
        public static int DamageFineTo(SiegeKind kind, int rank, bool weak,
                                       Wards.WardAbility ability, int magnitude,
                                       int powerHundredths)
        {
            int hit = DamageFine(rank, powerHundredths) * (weak ? WeakMultiplier : 1);
            if (kind != SiegeKind.Bulwark) return hit;

            // **A rend turret is not blunted, and bites its own magnitude harder on top.** The
            // bonus is what tells its two rungs apart: the field was read by nothing at all for
            // as long as "not blunted" was the whole ability, so a thousand-gem breaker and a
            // four-thousand-credit cleaver were the same turret at two prices - which is the
            // decoration invariant 5d names, arriving on the one thing a player pays for.
            // Upward only, which is what keeps it out of par's way (see the summary).
            if (ability == Wards.WardAbility.Rend)
                return hit * (10 + RendBonus(magnitude)) / 10;

            if (weak) return hit;

            int soaked = hit * ShieldSoakTenths / 10;

            // Never nought: a bolt that reads as landing and takes nothing is a turret the player
            // will believe is broken, and at rank nought against a low `ShotDamage` the division
            // can get there.
            return soaked < 1 ? 1 : soaked;
        }

        /// <summary>
        /// A bulwark: slower than a creeper and much slower than a brute, so the shield is paid
        /// for in the one currency this mode measures in — time on the hill.
        ///
        /// <b>Slower is the whole price, and it is a real one.</b> A raider that is hard to kill
        /// and quick would be a wave the player simply cannot answer; one that is hard to kill and
        /// slow is a wave they may choose to <em>ignore</em> for a while, which is a decision.
        /// Its health sits between a creeper's and a brute's, because the shield is already doing
        /// the work and stacking both would make it a boss.
        /// </summary>
        public const int BulwarkHealth = 700;

        /// <summary>
        /// Seconds a bulwark takes to cross the hill. The slowest thing that is not a boss, and
        /// paced with <see cref="CreeperMarch"/>.
        /// </summary>
        public const float BulwarkMarch = 39f;

        /// <summary>What one bulwark swing costs a ward. A brute's, because it arrives armoured.</summary>
        public const int BulwarkBlow = 2;

        // ------------------------------------------------------------------ the player's line
        /// <summary>
        /// How much fuel a turret carrying <paramref name="model"/> holds.
        ///
        /// <b>A beacon is the one ability that changes what a ward <em>holds</em> rather than what
        /// it does with what it holds</b>, so it is read once when the ward is built rather than
        /// on every bolt. What it buys is a cascade banked rather than spilled, which is why it is
        /// worth most to the player who is already playing well.
        /// </summary>
        public static float CapacityOf(Wards.WardModel model)
        {
            if (model == null || model.Ability != Wards.WardAbility.Beacon) return WardCapacity;

            int tenths = 10 + (model.Magnitude < 0 ? 0 : model.Magnitude);
            return WardCapacity * tenths / 10f;
        }

        /// <summary>
        /// Whether this kind stops on the hill and works on the <em>field</em> rather than on the
        /// line.
        ///
        /// <b>Asked rather than assumed, exactly as <see cref="IsBoss"/> is.</b> Two places used
        /// to be able to tell a raider that holds from one that walks by testing
        /// <c>Hold &lt; 1</c>, which was true only of a boss and is now true of four kinds that
        /// want three different things done with them.
        /// </summary>
        /// <summary>Whether killing this one leaves a live bomb standing where it fell.</summary>
        public static bool LeavesABomb(SiegeKind kind) => kind == SiegeKind.Bomber;

        // ------------------------------------------------------------------ ward ranks
        /// <summary>
        /// How many times a ward may be upgraded. Five tiers, so four ranks above the one it
        /// stands up in.
        /// </summary>
        public const int MaxRank = 4;

        /// <summary>
        /// What each rank is worth, in tenths: ten per cent more damage and ten per cent less fuel
        /// a bolt.
        ///
        /// <para>
        /// <b>Both halves of one upgrade, and they multiply rather than add.</b> A rank-four ward
        /// hits for forty per cent more and gets a bolt out of six tenths rather than ten, so the
        /// same match is worth <em>2.33 times</em> as much damage through it — which is why a
        /// level built around cogs has to send considerably more hill than one that is not, and
        /// why <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> is run over every rung of the
        /// chapter rather than over the first.
        /// </para>
        /// </summary>
        public const int RankDamageTenths = 1, RankFuelTenths = 1;

        /// <summary>The whole of a tenths-based proportion, so a rank is a share and not a step.</summary>
        const int Whole = 10;

        /// <summary>How much a bolt from a ward of this rank takes off, before the weak double.</summary>
        public static int DamageAt(int rank) => DamageAt(rank, Wards.WardModel.Baseline);

        /// <summary>
        /// The same, for a turret whose bolt is <paramref name="powerTenths"/> of the baseline.
        ///
        /// <para>
        /// <b>The power is applied after the rank and never before</b>, so the two multiply and a
        /// cog is worth the same ten per cent on every turret. Taken the other way round the
        /// truncation would eat a rank on a light bolt and the ladder would stop being five even
        /// steps, which is the fault invariant 37aa records about the fuel unit.
        /// </para>
        /// <para>
        /// <b>Never below the baseline</b>: <c>WardModel</c> clamps the field, and par is computed
        /// against the baseline. See <c>WardModel.PowerTenths</c> for what a softer bolt would
        /// cost.
        /// </para>
        /// </summary>
        public static int DamageAt(int rank, int powerTenths)
            => DamageFine(rank, powerTenths * 10);

        /// <summary>
        /// The same, for a turret whose bolt is <paramref name="powerHundredths"/> of the baseline
        /// — its model's weight with its stars already multiplied in.
        ///
        /// <para>
        /// <b>One division, at the end.</b> The rank, the model's weight and the star ladder are
        /// three tenths figures, and folding them together a pair at a time truncates each time:
        /// a ten per cent star on a small figure disappears entirely, which shipped as an upgrade
        /// a player paid for and could not see (see <c>WardBuild.PowerHundredths</c>). Every
        /// multiplier is gathered first and the division happens once, where the figure becomes a
        /// number somebody reads.
        /// </para>
        /// <para>
        /// <b>It answers exactly what the old arithmetic did at the first star</b>, which is what
        /// lets every par, every star line and every gate stand unmoved.
        /// </para>
        /// </summary>
        public static int DamageFine(int rank, int powerHundredths)
        {
            if (rank < 0) rank = 0;
            if (rank > MaxRank) rank = MaxRank;

            int floor = Wards.WardModel.Baseline * 10;
            if (powerHundredths < floor) powerHundredths = floor;

            return ShotDamage * (10 + rank * RankDamageTenths) * powerHundredths / 1000;
        }

        /// <summary>What one bolt from a ward of this rank costs it, in tenths of fuel.</summary>
        public static int FuelShotTenths(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > MaxRank) rank = MaxRank;

            // **A share of the base rather than a subtraction from it**, which is the same five
            // numbers while the base is ten tenths and stops being so the moment it is not. It
            // read `FuelPerShotTenths - rank * RankFuelTenths`, which says "a tenth less" and
            // really meant "one tenth of a fuel less" — true only at a base of ten, and silently
            // a five per cent step once a bolt cost two fuel. As a proportion it is 10, 9, 8, 7, 6
            // at a base of ten and 20, 18, 16, 14, 12 at a base of twenty, both exact, and it is
            // the rule this constant is named for rather than an arithmetic coincidence of one
            // scale.
            return FuelPerShotTenths * (Whole - rank * RankFuelTenths) / Whole;
        }

        /// <summary>What one bolt from a ward of this rank costs it.</summary>
        public static float FuelShot(int rank) => FuelShotTenths(rank) / 10f;

        /// <summary>
        /// The most cogs that may stand on a field at once.
        ///
        /// <b>A cap on the board rather than on the deal</b>, because the deal is a rate and a
        /// rate has no upper bound over a long run. Three is enough for the player to have a
        /// choice about which one to take next and few enough that the field is still a field.
        /// </summary>
        public const int MostCogs = 3;

        /// <summary>A creeper: the ordinary raider, and what most of a wave is.</summary>
        public const int CreeperHealth = 200;

        /// <summary>
        /// Seconds a creeper takes to cross the hill, and the number the whole hill is paced by.
        ///
        /// <para>
        /// <b>Fifteen, then seventeen, because the owner's verdict was that the mode reads as
        /// super fast paced.</b> Every non-boss march went up by the same seventh
        /// (<see cref="BruteMarch"/>, <see cref="BulwarkMarch"/>, <see cref="BomberMarch"/>), so
        /// the hill keeps its shape and only its speed moves — a brute is still the thing that is
        /// slow because it is heavy, and a bulwark is still the slowest thing that is not a boss.
        /// <b>A boss's march is deliberately not in that list</b>: a warlord's entrance is timed
        /// against its own cadence and the quiet before it (<see cref="BossAfter"/>), and it was
        /// already the one raider on the hill nobody is waiting for.
        /// </para>
        /// <para>
        /// <b>A seventh is close to the ceiling, and what stops it is invariant 5d rather than
        /// taste.</b> A slower column spends longer under the wards, so more of it dies before it
        /// arrives — and a hill that never reaches the line is a fail state that rejects nothing,
        /// which would leave the rung playing as a jewel board with scenery over it. Measured
        /// across the whole chapter and nine player rhythms: at this pace every rung past the
        /// teaching ones still draws blood at some rhythm, and <b>at a third slower
        /// <c>s01_bramblerun</c> finishes untouched at every one of them</b>. Anything past this
        /// has to buy the threat back in the waves.
        /// </para>
        /// </summary>
        public const float CreeperMarch = 17f;

        public const int CreeperBlow = 1;

        /// <summary>
        /// A bomber: an ordinary raider carrying something that outlives it.
        ///
        /// <para>
        /// <b>A creeper's numbers exactly, and that is the measurement rather than a preference.</b>
        /// Nothing about fighting one is different — it walks, it swings, it dies — so what it is
        /// worth killing has to be set by what it <em>leaves</em>: a bomb pays a firepot's 440 back
        /// to a player who spends it well, and a raider costing much more than a creeper's 200
        /// turns a gift into a toll. Measured over the whole chapter at nine player rhythms, a
        /// brute-weight bomber cost 16 runs in 90 and a creeper-weight one gained 4.
        /// </para>
        /// <para>
        /// <b>So a bomber is a creeper the player is pleased to see</b>, and the only thing keeping
        /// that from being free money is that a level authors one and never a wave of them. See
        /// <see cref="BombDamage"/> for the other half.
        /// </para>
        /// </summary>
        public const int BomberHealth = 200;

        public const int BomberBlow = 1;

        /// <summary>Seconds a bomber takes to cross the hill. A brute's: it is a brute with cargo.</summary>
        public const float BomberMarch = 17f;

        /// <summary>
        /// What a bomb takes off everything in its reach when it is tapped.
        ///
        /// <para>
        /// <b>A firepot's, and read from the same place a firepot's is read</b> — the utility
        /// catalog is content, so this is the one number here that is deliberately <em>not</em> a
        /// constant: <c>SiegeScreen</c> hands the board the published magnitude. What is here is
        /// the fallback for a build with no store configured at all, and it is the shipped
        /// firepot's figure so the two cannot read differently on a device that has never synced.
        /// </para>
        /// <para>
        /// <b>It is charged in matches exactly as a firepot is</b> (<c>SiegeUtility.MatchesFor</c>,
        /// invariant 39). A bomb costs no stock, no gems and no cooldown; what it cannot be is
        /// free of the <em>grade</em>, because a grade here is not a private number (19a) and a
        /// player who leant on bombs would otherwise beat a three-star line without matching.
        /// </para>
        /// </summary>
        public const int BombDamage = 440;

        /// <summary>
        /// How far a bomb's blast reaches, in boxes of the hill's targeting grid.
        ///
        /// <b>The firepot's plus, said once</b> — <see cref="BlastReach"/> — so what a bomb takes
        /// and what a firepot takes cannot come apart. A player who has used one knows the other.
        /// </summary>
        public static int BombReach => BlastReach;

        /// <summary>
        /// The most bombs that may be standing on the hill at once.
        ///
        /// <b>A cap on a good thing, so it is tight.</b> A hill littered with bombs is a hill the
        /// player is hoarding rather than clearing, and a level that authored six bombers would
        /// turn the mode into a tapping game. Three is enough to save one for a wave.
        /// </summary>
        public const int MostBombs = 3;

        /// <summary>A brute: slower, far tougher, and twice as expensive to let through.</summary>
        public const int BruteHealth = 480;

        /// <summary>Seconds a brute takes to cross the hill. Paced with <see cref="CreeperMarch"/>.</summary>
        public const float BruteMarch = 30f;
        public const int BruteBlow = 2;

        // ------------------------------------------------------------------ the warlord
        /// <summary>
        /// The warlord's health, and it is deliberately under a quarter of the whole hill's.
        ///
        /// <para>
        /// <b>A boss has to be worth a fight rather than a raider with a bigger number.</b> At a
        /// hundred and eighty it is nearly four brutes standing still, which under the ordinary
        /// play <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> models is about thirteen
        /// matches of nothing else and twenty-five seconds of clock — long enough that the player
        /// has to keep choosing the right colour under fire, short enough that the last stretch of
        /// a two-minute level is not a grind.
        /// </para>
        /// <para>
        /// <b>And the ceiling on it is arithmetic rather than taste, which is the half worth
        /// knowing before anybody makes a warlord bigger.</b> <see cref="PerfectMatch"/> assumes
        /// every gem a match clears is spent as a bolt that lands <em>double</em> — which a hill
        /// wearing all four colours very nearly allows, because each ward finds its own. A duel
        /// cannot: the warlord is one colour, so one ward doubles and three do not, and a match
        /// therefore delivers about 13.75 rather than 22. Par is still a genuine floor (no run of
        /// fewer matches could destroy this), it is simply a <b>looser</b> one over a duel, so
        /// every point of warlord pushes the three-star line further from real play. Measured on
        /// the shipped level: at 180 an unhurried player needs 44 matches against a three-star line
        /// of 44, and at 200 it is 47 against 46 and the ladder's top rung is gone. <b>Health moved
        /// from the hill to the warlord makes three stars harder without par saying so.</b>
        /// </para>
        /// </summary>
        public const int BossHealth = 1800;

        /// <summary>
        /// The overlord: the thing the last rung of a chapter ends on.
        ///
        /// <para>
        /// <b>A kind rather than a bigger number, and what makes it one is that all three of these
        /// move together.</b> It carries a little under twice a warlord's health, it throws half as
        /// often again, and each spell takes nearly twice as much off a ward — so it is not
        /// answered by doing what beat a warlord for longer. It stops <em>further up</em> the
        /// hill, which is the half a player feels first: there is more ground between it and the
        /// line, so the wards have longer to work on it, and that is the compensation for
        /// everything above.
        /// </para>
        /// <para>
        /// Its ceiling is <see cref="BossHealth"/>'s ceiling, and the same arithmetic: a duel is
        /// fought against <em>one</em> colour, so one ward doubles and three do not and a match
        /// really delivers about 62% of <see cref="PerfectMatch"/>. Every point of overlord
        /// therefore pushes the three-star line further from real play without par saying so, and
        /// the only instrument that can see it is
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>.
        /// </para>
        /// </summary>
        public const int OverlordHealth = 3200;

        public const float OverlordHold = .38f;
        public const float OverlordMarch = 8f;

        /// <summary>
        /// Seconds between one omen and the next, and the cliff either side of it is sharp.
        ///
        /// <b>It was 3.4 while an omen only took health, and a sunder is a second cost on the same
        /// spell</b> — every rank it knocks off the line is ten per cent of a turret's damage and
        /// ten per cent of its fuel, compounding for the rest of the duel, and none of that is
        /// visible to par (invariant 37w). Measured on the shipped finale through
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which is the only instrument that
        /// can see it: at 3.4 the line falls with a raider left, at 3.8 it still falls, at 4.0 an
        /// unhurried player finishes on 16 of 56, and at 4.1 on 20. Two tenths of a second is the
        /// difference between a lost run and a comfortable one, which is itself the argument for
        /// measuring this rather than arguing about it.
        /// </summary>
        public const float OverlordCastEvery = 4.0f;

        public const int OverlordCast = 5;

        /// <summary>
        /// What an overlord's spell also does: knocks the ward it lands on down a rank.
        ///
        /// <b>It attacks the thing the chapter taught rather than the thing every boss attacks.</b>
        /// A cog is the one upgrade a player <em>earns</em> in this mode (invariant 37w), so the
        /// finale is the one fight where where those cogs went is a question with a wrong answer —
        /// pour every one into a single turret and the overlord can take the whole investment off
        /// it, and a mending cannot put a rank back. It is bounded at nought by
        /// <see cref="SiegeWard.Sunder"/> and never touches health beyond
        /// <see cref="OverlordCast"/>, so it can neither fell a ward on its own nor make
        /// <c>Stranded</c> anything but the certainty invariant 28f needs.
        /// </summary>
        public const int OverlordSunder = 1;

        // ------------------------------------------------------------------ the blightcaller
        /// <summary>
        /// The blightcaller: the first boss a chapter shows, and the only one that takes no health.
        ///
        /// <para>
        /// <b>It puts a ward <em>out</em>.</b> Its spell empties the fuel a ward is holding and
        /// smothers it for <see cref="Douse"/> seconds, in which it cannot fire at all — so what
        /// it costs is not the line's health but the player's <em>work</em>, and it is the one
        /// boss whose answer is not a mending. The decision it asks is the mode's own decision
        /// (which colour is worth feeding next) under a constraint that moves every few seconds,
        /// and the way to be wrong about it is to keep pouring into the ward that has just gone
        /// dark. A <c>surge</c> re-lights one, which is the first time in this chapter a utility
        /// answers a boss rather than a wave.
        /// </para>
        /// <para>
        /// <b>Taking no health is what makes it the boss a chapter opens with.</b> It teaches the
        /// tell — the ring closing over a ward, the second and a bit to react in — without the
        /// punishment a warlord's version carries, so a player meets the shape of a boss fight
        /// before they meet its cost. It is also why <c>ModeValidator.Threatens</c> stopped
        /// counting "there is a boss" as a threat: a level whose only threat were this could not
        /// be lost, which is invariant 5d asked of a fail state.
        /// </para>
        /// </summary>
        public const int BlightHealth = 1100;

        public const float BlightHold = .58f;
        public const float BlightMarch = 7f;
        public const float BlightCastEvery = 4.5f;

        /// <summary>Seconds a doused ward stands dark. Long enough to notice, short enough to wait out.</summary>
        public const float Douse = 5f;

        // ------------------------------------------------------------------ the warbringer
        /// <summary>
        /// The warbringer: the boss that hits the <em>whole line</em> at once.
        ///
        /// <para>
        /// <b>It roars, and the roar does two things.</b> It takes <see cref="WarbringerCast"/>
        /// off every ward still standing — not one ward, all of them — and it sets every raider on
        /// the hill charging at <see cref="Rally"/> times its own pace for
        /// <see cref="RallyFor"/> seconds. That is a different verb from the other three: a warlord
        /// and an overlord pick one ward and hit it hard, so the line comes down unevenly and the
        /// player chooses what to save; a warbringer shakes the entire line, so nothing it does can
        /// be answered by protecting one turret and the only answer is to kill it faster.
        /// </para>
        /// <para>
        /// <b>It used to walk to the line, and that was withdrawn after play.</b> The design was
        /// that it took <em>ground</em>: each roar lunged it further down the hill until it arrived
        /// and swung with its hands, which made the fight a countdown rather than a duel. What it
        /// actually produced was a boss that spent most of the level walking — reported as "it
        /// takes forever to move down and start doing its damage, because it keeps moving
        /// downwards". <b>A boss standing still is not a limitation of this mode, it is the shape
        /// of the fight</b>: every one of them holds the middle of the hill where nothing can reach
        /// it and the player answers it with the board, and a boss that walks spends the fight
        /// being somewhere else. So it holds like the rest and the ground it used to take is gone.
        /// </para>
        /// <para>
        /// <b>The rally half survives and is why it still comes early</b> — see
        /// <see cref="RestBefore"/>. A roar over an empty hill would be half a mechanic doing
        /// nothing, which is invariant 5d: the whole point of a rally is that there is something to
        /// rally.
        /// </para>
        /// <para>
        /// <b>It adds no health to the hill, so par does not move</b> — which is exactly why the
        /// only instrument that can say whether it is tuned is
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> (invariant 37j). A warbringer that
        /// is too strong shows up there as a line that falls and nowhere else at all.
        /// </para>
        /// </summary>
        public const int WarbringerHealth = 2400;

        /// <summary>
        /// Where it stops, and it stays there.
        ///
        /// The warlord's ground, because it is the warlord's kind of fight: stand out of reach and
        /// work on the line. It was .34 and moving while it had a lunge.
        /// </summary>
        public const float WarbringerHold = .46f;

        public const float WarbringerMarch = 8f;

        /// <summary>
        /// Seconds between one roar and the next, and it is the slowest cadence of the four.
        ///
        /// <b>Because a roar lands four times, the cadence is what prices it.</b> Four wards at
        /// <see cref="WarbringerCast"/> every nine seconds is 0.89 health a second off the line,
        /// which sits where the rung does: a warlord takes 0.60 and an overlord 1.25, so rungs 5,
        /// 8 and 10 climb. It was 6, which is 1.33 — <em>more</em> than the finale, and measured
        /// on the shipped rung it left an unhurried player on 6 of 56 (the chapter's bloodiest
        /// line, one rung before its climax, which is the ramp inverted).
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> is the only thing that can see any
        /// of that.
        /// </summary>
        public const float WarbringerCastEvery = 9f;

        /// <summary>
        /// What one roar takes off <em>every</em> standing ward.
        ///
        /// <b>Small, because it lands four times.</b> Two off a four-ward line is eight health a
        /// roar against a warlord's three, which is more in total and much less per ward — so the
        /// line comes down flat rather than one turret at a time, and no single mending answers it.
        /// Spread damage is the weaker shape for the same total, which is the right side to err on
        /// for the rung before the finale.
        /// </summary>
        public const int WarbringerCast = 2;

        /// <summary>
        /// How much faster the hill walks while a warbringer's roar is on it.
        ///
        /// <b>It was 1.9 and that took the line apart.</b> Measured on the shipped rung through
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>: at 1.9 an unhurried player
        /// finished with one ward standing out of four, which is a rung that is lost by anybody
        /// having a worse afternoon. At 1.55, with a longer quiet in front of it
        /// (<see cref="CrowdAfter"/>), the same player finishes with three and the line down
        /// to 38 of 56 — bled hard, which is what a penultimate rung should feel like.
        /// </summary>
        public const float Rally = 1.55f;

        /// <summary>Seconds a roar lasts. Two roars never stack — the later one restarts it.</summary>
        public const float RallyFor = 5f;

        /// <summary>
        /// Where the warlord stops, as a march reading.
        ///
        /// <b>It never reaches the line, and that is the whole shape of the fight.</b> Everything
        /// else on this hill is answered by killing it before it arrives; a warlord walks to the
        /// middle of the hill, stands there, and hits the line from where nothing can stop it
        /// except the wards themselves. So the pressure it applies cannot be outrun — it can only
        /// be out-damaged, which is what makes the last wave a duel rather than a longer wave.
        /// </summary>
        public const float BossHold = .46f;

        /// <summary>
        /// Seconds it would take to cross the whole hill. It only walks <see cref="BossHold"/> of
        /// it, so the entrance a player actually watches is a little under six seconds.
        ///
        /// <b>It was 22, then 13, and it is 7 — the owner has now asked for it faster twice.</b> A
        /// creeper crosses the whole hill in fifteen; there was no reason for a boss to be slower
        /// per unit of ground than the smallest thing on the board except that slow reads as heavy
        /// — and past a few seconds it stops reading as heavy and starts reading as a wait. What
        /// carries the weight instead is its size, its own frames and the shake it arrives with.
        /// <c>SiegeRuleTests.AWarlordWalksOnBeforeItStands</c> holds the entrance under
        /// <b>four</b> seconds now, so the number cannot drift back.
        /// <para>
        /// <b>All four bosses moved together and the entrances stay in the order the sizes are
        /// in</b>, because the ladder a player reads is size-then-weight: the blightcaller walks on
        /// in about 4.1 seconds, the warlord in 3.2, the overlord in 3.0 and the warbringer in 2.7.
        /// Halving these is not free — a boss on its ground is a boss casting, so every one of them
        /// now opens fire three to four seconds earlier, which is why this went back through
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> like every other pacing change
        /// (37s).
        /// </para>
        /// </summary>
        public const float BossMarch = 7f;

        /// <summary>
        /// The quiet before the warlord, which is longer than the quiet before any other wave.
        ///
        /// <para>
        /// <b>He sends his raiders first and comes himself after them</b>, which is the pacing the
        /// finale wants and is also the one place this mode's own wave rule needed an exception.
        /// Invariant 37k's shortcut still applies — a player who has cleared the hill gets the
        /// warlord at once rather than standing about — so what this longer clock buys is entirely
        /// for the player who is <em>behind</em>: it stops a duel being stacked on top of a wave
        /// still swinging at the line, which is two fail states arriving together and reads as
        /// being cheated rather than as being outpaced.
        /// </para>
        /// <para>
        /// It is pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> like everything
        /// else here, and it is the number that moved when the warlord was first put on the hill:
        /// at <see cref="BetweenWaves"/> the shipped level's last brutes and its warlord reached
        /// the line within seconds of each other and the run was lost with two raiders left.
        /// </para>
        /// <para>
        /// <b>It came back down from 34 to 28 for the same verdict that shortened
        /// <see cref="BossMarch"/>, and that is not free.</b> The warlord now opens fire about
        /// eleven seconds earlier, which is eleven seconds of it overlapping the tail of the last
        /// wave — measured, an unhurried player finishes with 15 of the line's 56 rather than 21.
        /// <b>A pacing change is a difficulty change</b> (37s said the same thing about the fuel
        /// flight), and this one is the level getting tighter rather than the boss getting
        /// stronger. If it plays too tight, <see cref="BossCastEvery"/> is the constant to move —
        /// not this one, which is what the player asked for.
        /// </para>
        /// </summary>
        public const float BossAfter = 28f;

        /// <summary>Quiet between the warlord reaching its ground and its first spell.</summary>
        public const float BossWakes = 3.4f;

        /// <summary>Seconds between one spell and the next.</summary>
        public const float BossCastEvery = 5f;

        /// <summary>
        /// How long a boss waits before asking again, having found nothing worth casting at.
        ///
        /// <para>
        /// <b>Short, and it is a retry rather than a cadence.</b> A cast that finds no target
        /// used to spend a whole <see cref="CastEveryFor"/> on nothing, because the timer was
        /// re-armed before the target was known — on a blightcaller, whose spell already takes no
        /// health, that is a boss standing silent for four and a half seconds and is how "it
        /// attacks and my turrets lose nothing" came to be true.
        /// </para>
        /// <para>
        /// It can only ever make a spell arrive <em>sooner than it would have</em> and never more
        /// often than the cadence, because a cast that lands re-arms in full — so nothing about
        /// a level's tuning moves except that a boss stops wasting its turns.
        /// </para>
        /// </summary>
        public const float CastRetry = .3f;

        /// <summary>
        /// How long a spell is telegraphed before it leaves, and how long it is then in the air.
        ///
        /// <para>
        /// <b>Drawing numbers living in the rules, for <see cref="SwapFor"/>'s reason and one
        /// more.</b> A spell that landed on the frame the warlord decided to cast would take a
        /// ward down before anything had crossed the hill, which is the fault invariant 37s is
        /// about. And the wind-up is not only an animation: it is the window in which a
        /// <c>mending</c> is worth pouring into the ward that is about to be hit, so how long it
        /// lasts is a rule the player plays against rather than a flourish.
        /// </para>
        /// </summary>
        public const float BossTell = 1.15f, BossFlight = .45f;

        /// <summary>What one spell takes off a ward.</summary>
        public const int BossCast = 3;

        /// <summary>
        /// Blows a ward takes before it falls.
        ///
        /// <b>Drawn as a bar rather than as pips, because it is too many to count.</b> It was four
        /// while fuel faded on a clock; taking the fade out roughly doubled what a match delivers,
        /// so the hill had to grow, and a hill that can put four brutes on the line at once takes
        /// a line apart in three seconds against four. What this number really sets is *how long
        /// a leak is survivable*.
        ///
        /// <para>
        /// <b>It went from ten to fourteen when the warlord arrived, and that is a fact about the
        /// shape of a level rather than about how hard one should be.</b> At ten the shipped siege
        /// ended within seconds of the line coming down — which is the right texture for a level
        /// whose last wave is its climax, and the wrong one for a level that has a duel *after*
        /// its last wave: measured, the run was lost with two raiders left, the line falling to
        /// brutes that used to be the finale. A longer level needs a line that can carry a leak
        /// into the next phase, and the compensation is that a leak now bleeds for longer rather
        /// than that the level got easier — an unhurried player still finishes 43% down
        /// (<c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, invariant 37j).
        /// </para>
        /// </summary>
        public const int WardHealth = 14;

        /// <summary>
        /// The least of the baseline a turret's own toughness may be authored at, in tenths.
        ///
        /// <para>
        /// <b>A floor rather than a taste, because the one thing a loadout may not do is make a
        /// rung impossible.</b> A player who stands four fragile turrets has chosen a harder
        /// game, which is the decision the shelf exists to offer; a player who has chosen a rung
        /// that cannot be held however well they play has been sold a trap.
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> plays the whole chapter with a
        /// line of the flimsiest turret in the roster, which is the only thing that can say where
        /// this number belongs.
        /// </para>
        /// </summary>
        public const int LeastGuardTenths = 7;

        /// <summary>
        /// How much <paramref name="model"/> can take before it falls.
        ///
        /// <b>Read once when the ward is built</b>, exactly as its capacity is
        /// (<see cref="CapacityOf"/>), and held on the ward as <c>SiegeWard.Full</c> — so a
        /// mending, a rally and the health bar all ask the ward rather than the mode's constant.
        /// Every one of those three used to read <see cref="WardHealth"/> directly, which was
        /// right while every turret held the same, and would have quietly capped a tough turret's
        /// repairs at the baseline.
        /// </summary>
        public static int HealthOf(Wards.WardModel model) => HealthOf(new Wards.WardBuild(model));

        /// <summary>The same, with the turret's upgrades in it. See <see cref="HealthOf"/>.</summary>
        public static int HealthOf(Wards.WardBuild build)
        {
            // Hundredths, and divided once - see `WardBuild.PowerHundredths` for the star a
            // tenths-at-a-time fold used to eat.
            int fine = build.Model == null ? Wards.WardModel.Baseline * 10 : build.GuardHundredths;
            int floor = LeastGuardTenths * 10;

            if (fine < floor) fine = floor;

            int health = WardHealth * fine / (Wards.WardModel.Baseline * 10);

            // Never nought, for `DamageTo`'s reason: a turret that falls to the first blow is a
            // turret the player will believe is broken.
            return health < 1 ? 1 : health;
        }

        /// <summary>Seconds between a raider's blows once it has reached the line.</summary>
        public const float BlowEvery = 1.9f;

        /// <summary>
        /// Quiet before the first wave, and between one wave and the next.
        ///
        /// <para>
        /// <b>A wave now comes on a clock rather than when the last one is gone</b>, which is the
        /// owner's verdict after play: waiting for a clear meant a player who was winning was
        /// never under any pressure at all, because the hill politely stopped until they had
        /// finished. On a clock the waves overlap, so falling behind compounds - which is what a
        /// siege is.
        /// </para>
        /// <para>
        /// What it costs is the one guarantee the old rule bought for free: a run can now be
        /// *outpaced*, so <see cref="BetweenWaves"/> is the number that decides whether the level
        /// is holdable at all, and it is pinned by the simulation in
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> rather than reasoned about.
        /// </para>
        /// </summary>
        public const float FirstWaveAfter = 3.4f;
        public const float BetweenWaves = 26f;

        /// <summary>Seconds between one raider of a wave stepping out and the next.</summary>
        public const float RaiderSpacing = 1.35f;

        /// <summary>Lanes a raider may walk down. Wider than the ward line, so blows travel.</summary>
        public const int Lanes = 5;

        /// <summary>
        /// Which lane a boss holds: the middle when it is the only one, and either side of it when
        /// a pair arrives together.
        ///
        /// <para>
        /// <b>A lone boss walks down the middle rather than down a dealt lane</b>, because where
        /// it stands is not a fact anybody should have to hunt for — it is the largest thing on
        /// the board and it stays put for the rest of the run, so a dealt lane would put it over a
        /// ward on some devices and off the edge of the hill on others.
        /// </para>
        /// <para>
        /// <b>And a pair stands either side of that middle</b>, which is the one thing an endless
        /// lane's pair waves had to say about placement (invariant 43): the middle is where a
        /// player looks for a boss, so a second one arriving there would be two enormous bodies in
        /// one column. It is a rule here rather than a conditional inside <c>Muster</c> so that
        /// <c>SiegeView</c> and <c>Tools/render_siege.py</c> can draw the same hill the board is
        /// playing — the drawn thing and the played thing being one pair of integers is invariant
        /// 33g, and it is what lets a picture answer whether a pair reads as a climax.
        /// </para>
        /// </summary>
        public static int BossLane(int index, int bosses)
        {
            return bosses < 2 ? Lanes / 2 : index == 0 ? 1 : Lanes - 2;
        }

        /// <summary>
        /// How many bands the hill is cut into for aiming, down its length.
        ///
        /// <para>
        /// <b>The hill is a grid, and it is a grid because a target has to be a thing you can
        /// see.</b> A blast used to be a radius around wherever a finger left the board, which is
        /// exact in the rule and unreadable on the screen: the ring said how far it reached and
        /// nothing said what was in it, so the player was asked to judge a distance against
        /// raiders that were walking. Four rows against five lanes makes twenty boxes, each about
        /// a raider and a half wide - big enough to be tapped, small enough that which box is
        /// obviously a decision.
        /// </para>
        /// <para>
        /// It is <c>SiegeView</c>'s number too: the boxes it draws are these boxes, which is
        /// invariant 33g's rule about the haul-road - the drawn thing and the played thing have
        /// to be one thing, and the cheapest way to guarantee that is for there to be only one.
        /// <b>And for a long time there were two.</b> The panes were laid out from a third of a
        /// cell above the top of the hill, where <c>march</c> nought is the top of the hill exactly,
        /// so every boundary a player could see sat up to a third of a cell above the boundary the
        /// rule read - and they were drawn a fifth wider than the pitch they were spaced on, so
        /// neighbours overlapped and the higher lane won a tap in the seam. <c>SiegeView.BoxAt</c>
        /// is the one arithmetic now, and it is written in terms of <c>MarchY</c> rather than
        /// alongside it.
        /// </para>
        /// </summary>
        public const int BlastRows = 4;

        /// <summary>Which band of the hill a march reading falls in.</summary>
        public static int RowOf(float march)
        {
            int row = (int)(march * BlastRows);
            return row < 0 ? 0 : row >= BlastRows ? BlastRows - 1 : row;
        }

        /// <summary>
        /// How far a firepot carries, in boxes, measured out from the one that was tapped.
        ///
        /// <para>
        /// <b>One, and the shape it makes is a plus rather than a square.</b> A blast used to take
        /// exactly the box it was dropped on, which is the smallest thing a player can aim at and
        /// the least forgiving: a raider is drawn a little over a box tall and walks the whole time
        /// the finger is travelling, so a tap that was right when it was aimed can be a box out by
        /// the time it lands. That came back from play as <em>"it says there is nothing there, and
        /// I tapped the thing"</em> — and the player was right, because what they were tapping was
        /// a picture and what the rule read was a point.
        /// </para>
        /// <para>
        /// <b>A plus rather than a two-by-two block</b>, because a block has to lean somewhere. Four
        /// boxes cannot be centred on one, so a block needs an anchor rule — which way it goes, and
        /// what it does against an edge — and that is a rule the player has to learn about a thing
        /// whose whole job is to land where they pointed. A plus is centred by construction, forgives
        /// a miss by the same amount in every direction, and near an edge simply burns fewer boxes
        /// rather than sliding somewhere nobody aimed.
        /// </para>
        /// <para>
        /// <b>It cannot buy a grade, and that is arithmetic rather than a judgement.</b> A firepot is
        /// charged <c>ceil(absorbed / PerfectMatch)</c> matches (invariant 39), so a wider blast that
        /// catches three raiders instead of one is charged for three raiders — the exchange rate
        /// prices the change by itself and neither star line moves. What it buys is forgiveness,
        /// which costs the player nothing and the run nothing.
        /// </para>
        /// </summary>
        public const int BlastReach = 1;

        /// <summary>
        /// Whether a box of the hill is inside a firepot dropped on <paramref name="atLane"/>,
        /// <paramref name="atRow"/>.
        ///
        /// Manhattan rather than Chebyshev, which is what makes it a plus and not a three-by-three:
        /// see <see cref="BlastReach"/>.
        /// </summary>
        public static bool InBlast(int atLane, int atRow, int lane, int row)
        {
            int across = lane > atLane ? lane - atLane : atLane - lane;
            int down = row > atRow ? row - atRow : atRow - row;

            return across + down <= BlastReach;
        }

        /// <summary>
        /// How tall each kind is drawn, in cells.
        ///
        /// <para>
        /// <b>In the rules rather than in the view, because a firepot has to hit what a player can
        /// see.</b> This is the same class of number as <see cref="BossTell"/> and
        /// <see cref="SwapFor"/> — a fact about the drawing that a rule reads — and it is here for
        /// the reason invariant 33g gives about the haul-road: the thing that is drawn and the
        /// thing that is played have to be one fact, and the cheapest way to guarantee that is for
        /// there to be only one. <c>SiegeView</c> reads it; nothing else may hold a second copy.
        /// </para>
        /// <para>
        /// <b>The warlord is nearly three times a creeper, and that is the whole of what makes it
        /// read as a boss before anything about it has happened.</b> Its health bar, its damage
        /// numbers and its silhouette all follow from this one number, and it is bounded by the
        /// hill rather than by taste: a warlord at three cells fills most of the band a raider
        /// walks down, and anything larger would stand in the ward line's own space.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>The blightcaller is the smallest of the four, and 2.6 was too small.</b> It is the
        /// first boss a chapter shows and the only one that takes no health, so it should not
        /// arrive with the finale's silhouette — but a render put it beside a creeper and it read
        /// as one: its frame is a floating eye with a long tail under it, so a third of its height
        /// is not body at all, where every other boss here fills its own frame.
        /// <br/><b>A bulwark is drawn bigger than a brute</b>, because the one thing a player has
        /// to read about it before it is in range is that it is carrying something. Its shield is a
        /// third of its own frame, so at a brute's height the shield is the size of a gem and the
        /// whole mechanic is invisible until the bolts start bouncing.
        /// </remarks>
        public static float TallOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? 3.5f
             : kind == SiegeKind.Warbringer ? 3.3f
             : kind == SiegeKind.Boss ? 3.1f
             : kind == SiegeKind.Blightcaller ? 3.0f
             : kind == SiegeKind.Bulwark ? 1.85f
             : kind == SiegeKind.Bomber ? 1.30f
             : kind == SiegeKind.Brute ? 1.55f : 1.15f;

        /// <summary>
        /// The shallowest the hill is ever drawn, in cells.
        ///
        /// <para>
        /// <b>The one number that reconciles a body measured in cells with a grid measured in
        /// rows, and it is a floor rather than a measurement — deliberately.</b> How deep the hill
        /// is depends on the screen: the field is laid out to the width and the hill takes what is
        /// left, so measured across the phone shapes this game ships to it runs from about three
        /// cells on a 4:3 tablet to five and a half on a 20:9 phone. A body of a fixed height in
        /// cells therefore covers a different number of rows on every device — which is why a
        /// constant row count (this used to be <c>RowsOf</c>, answering three for every boss) was
        /// only ever right on one screen, and was mean by a whole row on two of them.
        /// </para>
        /// <para>
        /// <b>Taking the shallowest hill makes the footprint the most generous a screen could
        /// justify, and generosity is the safe direction here.</b> A hit box larger than the
        /// picture costs a player a firepot that caught something a hair outside what they were
        /// looking at; a hit box smaller than the picture costs them the item and tells them
        /// nothing was there. Only one of those is a bug report, and it is the one this replaced.
        /// It cannot reach the economy either, because a firepot is charged for what it absorbs
        /// (invariant 39) — a more generous blast bills more matches and buys no better a grade.
        /// </para>
        /// </summary>
        const float ShallowestHill = 3f;

        /// <summary>
        /// How much of the hill's length a kind's body covers, as a share of it, centred on where
        /// the raider is standing.
        ///
        /// <b>Centred, because that is where the body is drawn.</b> A raider's node is the middle
        /// of its picture rather than its feet — the old rule counted rows <em>upward</em> from the
        /// node on the belief that the node was the feet, so the band it caught sat about a row
        /// above the thing on the screen.
        ///
        /// <b>Measured, not picked</b>: a creeper is 1.15 cells against a hill some 3.6 cells deep
        /// on the reference screen, so it is a little over one row and straddles a boundary about
        /// as often as not; a warlord is three and a half rows and is on most of the hill at once.
        /// </summary>
        public static float BodySpan(SiegeKind kind)
        {
            float span = TallOf(kind) / ShallowestHill;
            return span > 1f ? 1f : span;
        }

        /// <summary>
        /// How many lanes across a kind's body reaches, counting the one it stands in.
        ///
        /// <para>
        /// <b>A boss spans the hill and everything else fits its own lane</b>, and both halves are
        /// facts about the art rather than opinions. The cast reels are cut to a fixed height and
        /// whatever width the animation's box came out as, and every boss frame is wider than it is
        /// tall — measured on the shipped art, between 1.27 and 1.91 — so a warlord drawn three
        /// cells tall is drawn close to six cells <em>wide</em>, which at five lanes across the
        /// board is four of them. Tapping the arm of a thing that fills the screen and being told
        /// nothing is there is the loudest form of this bug, and it was the reported one.
        /// </para>
        /// <para>
        /// <b>Stated in lanes rather than measured off the sprite</b>, for invariant 16i's reason:
        /// a size measured off whatever art happened to have loaded is a size that is wrong before
        /// the art arrives and wrong again the day the art is re-cut. A lane count can only ever be
        /// generous — a boss really is somewhere between three and four lanes wide, so five is the
        /// forgiveness <see cref="ShallowestHill"/> argues for, said in the other axis.
        /// </para>
        /// </summary>
        public static int LanesOf(SiegeKind kind) => IsBoss(kind) ? Lanes : 1;

        /// <summary>
        /// Whether a raider standing in <paramref name="lane"/> at <paramref name="march"/> is
        /// caught by a firepot dropped on <paramref name="atLane"/>, <paramref name="atRow"/>.
        ///
        /// <para>
        /// <b>The raider's drawn body against the boxes the firepot burns</b>, which is two
        /// rectangles overlapping and nothing cleverer. Its body is
        /// <see cref="BodySpan"/> of the hill's length and <see cref="LanesOf"/> lanes across,
        /// centred on where it stands; the firepot is the plus <see cref="InBlast"/> describes.
        /// </para>
        /// <para>
        /// <b>Asked by <c>SiegeBoard.Blast</c> and drawn by nothing</b>, so
        /// <c>SiegeRuleTests</c> is the only thing holding it to what the board looks like.
        /// </para>
        /// </summary>
        public static bool Caught(SiegeKind kind, int lane, float march, int atLane, int atRow)
        {
            for (int row = 0; row < BlastRows; row++)
                for (int at = 0; at < Lanes; at++)
                    if (InBlast(atLane, atRow, at, row) && OnBody(kind, lane, march, at, row))
                        return true;

            return false;
        }

        /// <summary>
        /// Whether one box of the hill is standing on the body of a raider in
        /// <paramref name="lane"/> at <paramref name="march"/>.
        ///
        /// <para>
        /// <b>The box is on the raider when the box is pointing at it</b> — the middle of the box
        /// against the body's own extent, not any overlap at all. The looser test reads well and
        /// is wrong on this hill: it is four rows deep and a creeper is over a row tall, so a body
        /// poking a tenth of a row into its neighbour would make that neighbour a hit, and with
        /// <see cref="BlastReach"/> on top of it every firepot would clear its whole lane. The row
        /// a player taps has to decide something (invariant 5d).
        /// </para>
        /// <para>
        /// <b>Split out from <see cref="Caught"/> so each half can be held on its own.</b> The
        /// footprint is a fact about the drawing and the plus is a fact about the item; a single
        /// predicate that was both would be a predicate no test could pin, and every reading of it
        /// would be the two answers multiplied together.
        /// </para>
        /// </summary>
        public static bool OnBody(SiegeKind kind, int lane, float march, int boxLane, int boxRow)
        {
            if (boxLane < 0 || boxLane >= Lanes || boxRow < 0 || boxRow >= BlastRows) return false;

            int reach = (LanesOf(kind) - 1) / 2;
            if (boxLane < lane - reach || boxLane > lane + reach) return false;

            float half = BodySpan(kind) * .5f;
            float middle = (boxRow + .5f) / BlastRows;

            return middle - march <= half && march - middle <= half;
        }

        // ------------------------------------------------------------------ fuel in flight
        /// <summary>
        /// How long a match takes to reach the wards it feeds, and the three numbers it is made
        /// of.
        ///
        /// <para>
        /// <b>These are drawing numbers living in the rules, and that is deliberate.</b> A swap
        /// resolves in an instant and its animation takes the better part of a second — the gems
        /// swap, they burst, motes fly up to the line. Fuel credited at the *instant of the swap*
        /// therefore reaches the wards before the player has seen anything leave the field, and
        /// what that looks like is a turret killing a raider before the gems it was paid for have
        /// gone off. Reported from play in exactly those words.
        /// </para>
        /// <para>
        /// So fuel is <b>in flight</b>: <see cref="Swap"/> books it and <see cref="Advance"/>
        /// lands it, on the schedule the view really draws. Putting the schedule here rather than
        /// in the view is what stops the two drifting — a mote that arrives before or after its
        /// fuel does is the same bug again, and there is no gate that could see it.
        /// </para>
        /// </summary>
        public const float SwapFor = .16f, BeatFor = .40f, FuelFlight = .42f;

        /// <summary>When a cascade's <paramref name="beat"/>th wave of fuel reaches the line.</summary>
        public static float FuelLands(int beat) => SwapFor + beat * BeatFor + FuelFlight;

        /// <summary>Whether this kind is one of the four bosses — the things that stand and cast.</summary>
        public static bool IsBoss(SiegeKind kind)
            => kind == SiegeKind.Boss || kind == SiegeKind.Overlord
            || kind == SiegeKind.Blightcaller || kind == SiegeKind.Warbringer;

        /// <summary>
        /// What each of the four bosses does when its spell lands.
        ///
        /// <para>
        /// <b>Four verbs rather than four numbers, and that is the whole of what makes them four
        /// bosses.</b> A chapter shipped two of these told apart by their health, their cadence
        /// and their hue, and it read — correctly — as one fight with the dial moved. So each one
        /// takes a different thing: <see cref="SiegeSpell.Smite"/> takes a ward's <em>health</em>,
        /// <see cref="SiegeSpell.Douse"/> takes its <em>fire</em>,
        /// <see cref="SiegeSpell.Rally"/> takes the player's <em>clock</em>, and
        /// <see cref="SiegeSpell.Sunder"/> takes the <em>rank</em> they earned. Each has a
        /// different answer, and only two of the four are answered by a mending.
        /// </para>
        /// <para>
        /// This is invariant 26h's test asked of a boss: what does the player decide about it, and
        /// can they be wrong. A boss that only ever did what the boss before it did, harder, is
        /// the mirror and the wick a sister mode withdrew twice for competing on degree rather
        /// than on kind (26g).
        /// </para>
        /// </summary>
        public static SiegeSpell SpellOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? SiegeSpell.Sunder
             : kind == SiegeKind.Blightcaller ? SiegeSpell.Douse
             : kind == SiegeKind.Warbringer ? SiegeSpell.Rally
             : SiegeSpell.Smite;

        /// <summary>
        /// Whether this boss's spell is aimed at a ward at all.
        ///
        /// <b>A warbringer's is not</b>, and that is the one clause the cast/flight/land spine
        /// needed for a fourth boss: a roar is thrown at the hill, so its flight carries no ward
        /// and everything downstream — the tell, <c>Arrive</c>, the view's ring — asks this rather
        /// than testing an index nobody set.
        /// </summary>
        public static bool AimsAtAWard(SiegeKind kind)
        {
            var craft = SpellOf(kind);
            return craft != SiegeSpell.Rally;
        }

        /// <summary>What a boss is called, for a message. Never shown to a player.</summary>
        public static string NameOf(SiegeKind kind)
        {
            for (int i = 0; i < SiegeLayout.BossNames.Length; i++)
                if (SiegeLayout.BossNames[i].Kind == kind) return SiegeLayout.BossNames[i].Name;

            return kind == SiegeKind.Bulwark ? "bulwark"
                 : kind == SiegeKind.Bomber ? "bomber"
                 : kind == SiegeKind.Brute ? "brute" : "creeper";
        }

        public static int HealthOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordHealth
             : kind == SiegeKind.Warbringer ? WarbringerHealth
             : kind == SiegeKind.Boss ? BossHealth
             : kind == SiegeKind.Blightcaller ? BlightHealth
             : kind == SiegeKind.Bulwark ? BulwarkHealth
             : kind == SiegeKind.Bomber ? BomberHealth
             : kind == SiegeKind.Brute ? BruteHealth : CreeperHealth;

        public static float MarchOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordMarch
             : kind == SiegeKind.Warbringer ? WarbringerMarch
             : kind == SiegeKind.Boss ? BossMarch
             : kind == SiegeKind.Blightcaller ? BlightMarch
             : kind == SiegeKind.Bulwark ? BulwarkMarch
             : kind == SiegeKind.Bomber ? BomberMarch
             : kind == SiegeKind.Brute ? BruteMarch : CreeperMarch;

        /// <summary>Seconds between one spell and the next, for whichever boss this is.</summary>
        public static float CastEveryFor(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordCastEvery
             : kind == SiegeKind.Warbringer ? WarbringerCastEvery
             : kind == SiegeKind.Blightcaller ? BlightCastEvery : BossCastEvery;

        /// <summary>
        /// What one of this boss's spells takes off a ward's health.
        ///
        /// <b>Nought for the blightcaller, and that is not an omission</b> — it takes a ward's
        /// fire rather than its health, so asking what a mending is worth against it has the answer
        /// "nothing". The warbringer's number is per ward and lands on <em>all</em> of them
        /// (<see cref="WarbringerCast"/>), which is why it is the smallest of the three.
        /// </summary>
        public static int CastOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordCast
             : kind == SiegeKind.Warbringer ? WarbringerCast
             : kind == SiegeKind.Boss ? BossCast : 0;

        /// <summary>
        /// What one swing at the line costs a ward.
        ///
        /// <b>A boss swings at nothing</b>, because none of the four reaches the line — what they
        /// cost the line is their spell, from where they stand. The warbringer briefly was the
        /// exception, and holding the middle is what it was changed back to.
        /// </summary>
        public static int BlowOf(SiegeKind kind)
            => IsBoss(kind) ? 0
             : kind == SiegeKind.Bulwark ? BulwarkBlow
             : kind == SiegeKind.Bomber ? BomberBlow
             : kind == SiegeKind.Brute ? BruteBlow : CreeperBlow;

        /// <summary>How far down the hill this kind comes before it stops. A boss stops early.</summary>
        public static float HoldOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordHold
             : kind == SiegeKind.Warbringer ? WarbringerHold
             : kind == SiegeKind.Boss ? BossHold
             : kind == SiegeKind.Blightcaller ? BlightHold
             : 1f;

        /// <summary>
        /// Whether this boss can bring a ward down at all, given long enough.
        ///
        /// <b>One of the four cannot.</b> A blightcaller takes fuel and never health, so a level
        /// whose only threat were one could not be lost — which is what
        /// <c>ModeValidator.Threatens</c> asks rather than assuming that a boss is by definition
        /// dangerous. It was two while the warbringer took ground instead of health.
        /// </summary>
        public static bool EndangersTheLine(SiegeKind kind) => CastOf(kind) > 0;

        /// <summary>
        /// Whether this boss's spell is worth less on an empty hill than on a full one.
        ///
        /// <para>
        /// <b>Asked of the spell, never of the kind, and that is what this predicate is for.</b>
        /// It was written out as <c>kind == SiegeKind.Warbringer</c> inside
        /// <see cref="RestBefore"/> — correct for the warbringer, whose rally charges the hill and
        /// so needs a hill — and it silently left the <em>blightcaller</em> on the long quiet.
        /// A douse takes a ward's <em>fire</em>, and fire is worth exactly what there is to burn:
        /// arriving onto a hill somebody has already cleared, it takes fuel that was going to
        /// nothing, and the whole boss reads as a thing that attacks and does not hurt. That was
        /// reported from play twice — once on the endless lane, where the answer was an escort
        /// (invariant 43), and once on this chapter's third rung, where the answer was supposed
        /// to be this and never was.
        /// </para>
        /// <para>
        /// A warlord and an overlord want the opposite and get it: they shell the line for as long
        /// as they live, so an empty hill is the <em>point</em> and stacking a duel on a wave
        /// still swinging is two fail states arriving together (invariant 37t).
        /// </para>
        /// </summary>
        public static bool WantsACrowd(SiegeKind kind)
        {
            var craft = SpellOf(kind);
            return craft == SiegeSpell.Rally || craft == SiegeSpell.Douse;
        }

        /// <summary>
        /// The quiet before a wave: long before a boss that shells the line, short before one
        /// whose spell needs a hill to be worth anything.
        ///
        /// <para>
        /// <b>Per spell rather than per kind, and the two exceptions point opposite ways for the
        /// same reason.</b> Invariant 37t made the quiet before a <em>warlord</em> longer than any
        /// other so that a duel is never stacked on a wave still swinging at the line — two fail
        /// states arriving together read as being cheated. A warbringer and a blightcaller want
        /// precisely the opposite, and <see cref="WantsACrowd"/> is why: a rally over an empty
        /// hill and a douse over an empty hill are both a mechanic that rejects nothing (invariant
        /// 5d), so they come while the last wave is still walking and the two are the fight.
        /// </para>
        /// <para>
        /// <b>And a boss that cannot bring a ward down gets the ordinary quiet</b>, because it is
        /// not a wave of its own at all: <c>SiegeLayout</c> stands it at the head of the last
        /// authored wave, so what this is being asked about is that wave rather than a duel. That
        /// is the answer a shorter quiet could not give — <c>SiegeBoard.Muster</c> sends the next
        /// wave the moment the hill is clear (invariant 37k), so a player who is ahead of the
        /// clock meets a lone boss however long the quiet is.
        /// </para>
        /// <para>
        /// Both numbers are pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> rather
        /// than argued about, because a pacing change is a difficulty change (37s).
        /// </para>
        /// </summary>
        public static float RestBefore(SiegeKind kind)
            => !IsBoss(kind) || !EndangersTheLine(kind) ? BetweenWaves
             : WantsACrowd(kind) ? CrowdAfter : BossAfter;

        /// <summary>
        /// The quiet before a boss that needs a hill: short, so it arrives into one worth rallying
        /// and worth dousing.
        ///
        /// <b>Shorter than <see cref="BetweenWaves"/> and much shorter than
        /// <see cref="BossAfter"/></b>, which is the whole of why it is a number of its own — see
        /// <see cref="RestBefore"/>. It was 14, which with a rally of 1.9 cost an unhurried player
        /// three of four wards; 17 with a rally of 1.55 leaves three standing on a line bled to 38
        /// of 56. Neither number was reasoned about.
        ///
        /// <b>It is named for the question rather than for the warbringer now</b>, because it
        /// answers two bosses and a name that answers one is how the blightcaller came to be left
        /// out of the rule it needed.
        /// </summary>
        public const float CrowdAfter = 17f;

        /// <summary>
        /// Gems an ordinary match clears, cascades included, in tenths.
        ///
        /// <para>
        /// <b>Measured, not reasoned about — and the first version of this number was reasoned
        /// about and wrong.</b> Par was <c>health / (3 gems x damage)</c> on the argument that a
        /// match clears at least three, so no run of fewer matches could deliver the health: a
        /// floor. It is not one, because a match on a full field <em>cascades</em>, and the gems a
        /// cascade takes are fuel too. Measured over a played run it is about five and a half, so
        /// the "floor" sat nearly twice above real play and three stars was free.
        /// </para>
        /// <para>
        /// <b>So par here is a calibrated estimate and not a proof, and that is the honest name
        /// for it.</b> It is pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which
        /// plays a level and fails if an ordinary run lands outside the ladder - which is the only
        /// instrument a mode with no search has. It is also a property of a *four-colour* field:
        /// a level dealing five colours would cascade less and want a smaller number, which is
        /// the limitation to remember before authoring one.
        /// </para>
        /// </summary>
        public const int MatchGemsTenths = 55;

        /// <summary>
        /// What one match delivers: the gems it clears, turned into bolts and every one of them
        /// landing on the raider that ward is strong against.
        ///
        /// <para>
        /// <b>Bolts, not gems, and that was an identity rather than a rule.</b> It read
        /// <c>gems x damage x 2</c>, which is only the same thing while a gem buys exactly one
        /// bolt — true for as long as a gem was worth one fuel and a bolt cost one, and silently
        /// load-bearing under every par, every star line and every utility charge in the mode. The
        /// moment a gem was worth two (<see cref="FuelPerGemTenths"/>) that formula would have
        /// halved what a match delivers and doubled every par in the chapter, with each number
        /// still looking perfectly plausible. It says <c>bolts</c> out loud now.
        /// </para>
        /// <para>
        /// Integer arithmetic throughout, for <c>LevelTuning</c>'s reason: a graded number decided
        /// by a float is a number three code generators round three ways. One division, at the
        /// end, so nothing truncates on the way.
        /// </para>
        /// </summary>
        public const int PerfectMatch =
            MatchGemsTenths * FuelPerGemTenths * ShotDamage * WeakMultiplier
            / (FuelPerShotTenths * 10);

        /// <summary>
        /// The fewest matches that could hold this level, and so what a run is graded against.
        ///
        /// <para>
        /// <b>Arithmetic rather than a search, and it is a genuine floor.</b> A match puts at most
        /// <see cref="PerfectMatch"/> damage into the hill — three gems, every one of them spent
        /// as a bolt, every bolt landing on a raider of that ward's own colour — so no run of
        /// fewer matches than this can have destroyed what the level sends. Everything a good
        /// player does beyond that is free upside the count does not model: a four-match is worth
        /// more than a three, and a cascade fuels a second ward for no move at all. That is the
        /// right direction for a floor to be wrong in — three stars stays reachable, which is the
        /// failure invariant 22 says never to ship (a band nothing can land in).
        /// </para>
        /// <para>
        /// It is <em>not</em> a proof the way every other mode's par is, and that is the honest
        /// summary of what a clock costs. See <see cref="SiegeLayout"/>.
        /// </para>
        /// <para>
        /// <b>And a level that deals cogs loosens it further, deliberately.</b> A rank-four ward
        /// turns a match into 2.33 times what <see cref="PerfectMatch"/> assumes, so on such a
        /// level this is not even the floor it is elsewhere — it over-states the matches a good
        /// run needs, which keeps three stars reachable and is the direction invariant 22 says to
        /// err in. Modelling ranks here was the alternative and is worse: it would have to guess
        /// how many cogs a player takes and which turret they spend them on, and a par built on a
        /// guess about play is a par nobody can check. What checks it instead is somebody playing
        /// it — <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, run over every rung.
        /// </para>
        /// </summary>
        public static int Par(SiegeLayout layout)
        {
            if (layout == null) return 1;

            int health = 0;
            for (int w = 0; w < layout.Coming.Length; w++)
                for (int i = 0; i < layout.Coming[w].Length; i++)
                    health += HealthOf(layout.KindAt(w, i));

            int par = (health + PerfectMatch - 1) / PerfectMatch;
            return par < 1 ? 1 : par;
        }
    }
}
