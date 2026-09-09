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
        /// change was for.</b> A gem now buys two bolts (<see cref="FuelPerGemTenths"/>) and each
        /// is worth half (<see cref="ShotDamage"/>), so at an unchanged cadence a fed ward
        /// <em>keeps firing for twice as long</em> — which is what "see them shoot more" means.
        /// Halving this as well was tried and undoes exactly that: the same bolts go through the
        /// same window twice as densely and a ward stops firing when it always did, so there is
        /// nothing more to watch. <b>The rate is what makes a ward's fire last; the fuel is what
        /// makes it long.</b>
        /// </para>
        /// <para>
        /// <b>What that costs is peak damage, and the levels pay it rather than this number.</b>
        /// Bolts a second times damage a bolt is the line's output, so half-weight bolts at this
        /// cadence is half the <em>peak</em> — which barely touches attrition (a ward lit twice as
        /// long kills a marching column better, because less of each burst is spent as overkill)
        /// and bites hard on an emergency, where one big health pool has to be answered at once.
        /// Measured through <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, and it moved
        /// three things in three different directions: a duel got dearer
        /// (<c>s01_warlordsgate</c> wanted more hill and got it), a long attrition rung got
        /// <em>safer</em> until nothing reached its line at all (<c>s01_thornsiege</c>, two more
        /// brutes), and the rule-test fixture — the one siege in this project that sent a boss and
        /// dealt no cogs — stopped being holdable, and now deals them like every shipped rung.
        /// </para>
        /// </summary>
        public const float FireEvery = .22f;

        /// <summary>
        /// Fuel one bolt spends at rank nought, in tenths.
        ///
        /// <b>Unchanged when the fuel unit was subdivided</b>, which is the point of having done it
        /// that way: every number in <see cref="FuelShotTenths"/> and every rank the cogs pay for
        /// is bit-identical to what shipped, and what moved is how much fuel a <em>gem</em> is
        /// worth.
        /// </summary>
        public const int FuelPerShotTenths = 10;

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
        /// <b>Then ten, because the owner asked to see the wards shoot more.</b> A gem now buys two
        /// bolts rather than one (<see cref="FuelPerGemTenths"/>) and each is worth half, so the
        /// same match delivers the same damage over twice as many bolts and a fed ward stays alight
        /// for twice as long. <see cref="PerfectMatch"/> is unmoved, so par, both star lines and
        /// every utility's charge are unmoved with it. <b>Ten is the floor for the ten per cent
        /// step</b> — at ten the ladder is 10, 11, 12, 13, 14, which is still exact and has no room
        /// under it, so anything that halves this again has to give the rank ladder a finer unit
        /// first.
        /// </para>
        /// </summary>
        public const int ShotDamage = 10;

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
        /// What a bolt from a ward of <paramref name="rank"/> takes off <paramref name="kind"/>,
        /// given whether it is the raider's own colour.
        ///
        /// <b>One place, because a second copy is a second opinion about what a shield is worth.</b>
        /// The soak is applied after the double, so a bulwark's own colour reaches it in full.
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak)
            => DamageTo(kind, rank, weak, Wards.WardAbility.None);

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
        public static int DamageTo(SiegeKind kind, int rank, bool weak, Wards.WardAbility ability)
        {
            int hit = DamageAt(rank) * (weak ? WeakMultiplier : 1);
            if (kind != SiegeKind.Bulwark) return hit;

            if (ability == Wards.WardAbility.Rend) return hit;
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

        /// <summary>Seconds a bulwark takes to cross the hill. The slowest thing that is not a boss.</summary>
        public const float BulwarkMarch = 34f;

        /// <summary>What one bulwark swing costs a ward. A brute's, because it arrives armoured.</summary>
        public const int BulwarkBlow = 2;

        // ------------------------------------------------------------------ the field raiders
        /// <summary>
        /// The weaver and the thief hold this far down the hill, and no further.
        ///
        /// <para>
        /// <b>Between the bosses and the line, and clear of both.</b> A boss holds at .38-.58 and
        /// the line is at 1.0, so these two stand in the run of hill nothing else occupies - which
        /// matters for a reason that is about reading rather than about collision: a player has to
        /// be able to tell at a glance which of the things on the hill is coming for the wards and
        /// which is coming for the board, and standing still somewhere nothing else stands is the
        /// cheapest way to say it.
        /// </para>
        /// <para>
        /// <b>The thief stands nearer than the weaver</b>, because its effect is the harsher of
        /// the two and a player has to be able to reach it: nearer down the hill means more of the
        /// line has it in range, and it is the one of the pair that has to die.
        /// </para>
        /// </summary>
        public const float WeaverHold = .66f, ThiefHold = .78f;

        /// <summary>
        /// Seconds a weaver and a thief take to reach where they stop.
        ///
        /// Quick, deliberately: these are not a countdown the way a warbringer is, and a raider
        /// that spends ten seconds arriving before it does anything is ten seconds of a level with
        /// its own mechanic not on the board.
        /// </summary>
        public const float WeaverMarch = 11f, ThiefMarch = 13f;

        /// <summary>
        /// What they are worth killing.
        ///
        /// <para>
        /// <b>Between a creeper and a bulwark, and that is a decision about the whole hill.</b>
        /// These two take no ward health at all, so every bolt spent on one is a bolt not spent on
        /// something that does - which means their health <em>is</em> the price of answering them,
        /// and a price above a bulwark's would make ignoring them correct at every difficulty.
        /// What stops ignoring them being correct is that the cost compounds: a web stays until
        /// the weaver is dead, so the field a player is left matching on gets worse for every
        /// second they leave it standing.
        /// </para>
        /// </summary>
        public const int WeaverHealth = 620, ThiefHealth = 540;

        /// <summary>
        /// Seconds between one web and the next, and between one theft and the next.
        ///
        /// <b>A thief is slower and its effect is worse</b>, which is the same bargain the two
        /// healths strike from the other side. Both are pinned by
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>: a rate fast enough to silt the
        /// field before an ordinary player can answer it is a mechanic that decides the run, and
        /// this mode has exactly one instrument that can see that (invariant 37j).
        /// </summary>
        public const float WeaveEvery = 3.6f, SnatchEvery = 5.4f;

        /// <summary>
        /// The first web and the first theft come this long after the raider stops, rather than on
        /// the instant.
        ///
        /// A raider whose effect lands in the frame it arrives gives the player nothing to read; a
        /// beat of standing there winding up is what turns "something appeared" into "something is
        /// about to happen".
        /// </summary>
        public const float MeddleWakes = 1.6f;

        /// <summary>
        /// How long a web and a theft take to cross the hill and land on the field.
        ///
        /// <b>The same split every boss spell keeps</b> (invariant 37s): the raider is told to
        /// cast, the view draws it leaving, and the cell is not touched until it arrives. Without
        /// it a gem locks before the thing that locked it has visibly thrown anything.
        /// </summary>
        public const float MeddleTell = .55f, MeddleFlight = .40f;

        /// <summary>
        /// The most webs, and the most sacks, that may stand on a field at once.
        ///
        /// <para>
        /// <b>A cap on the board rather than on the raider, for <see cref="MostCogs"/>'s
        /// reason.</b> How often one is spun is a rate; how many are standing is a fact about the
        /// field, and it is the second one a player experiences. Two weavers on one hill are twice
        /// the pressure and never twice the ceiling.
        /// </para>
        /// <para>
        /// <b>And it is what keeps the field playable.</b> A locked cell can never be matched and
        /// a sack is not a colour, so both narrow what <c>SiegeBoard.AnySwap</c> can find; past
        /// about a fifth of a field the board starts re-dealing itself every turn, which reads as
        /// the game shuffling rather than as the raider doing something. Six of an eight-by-five
        /// field is fifteen per cent each, and the board refuses to place one that would leave no
        /// legal swap at all.
        /// </para>
        /// </summary>
        public const int MostWebs = 6, MostSacks = 6;

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
        public static bool HoldsTheField(SiegeKind kind)
            => kind == SiegeKind.Weaver || kind == SiegeKind.Thief;

        /// <summary>Seconds between one of this kind's meddles and the next.</summary>
        public static float MeddleEveryFor(SiegeKind kind)
            => kind == SiegeKind.Thief ? SnatchEvery : WeaveEvery;

        /// <summary>The most of this mark that may stand on the field at once.</summary>
        public static int MostOf(SiegeSpell craft)
            => craft == SiegeSpell.Snatch ? MostSacks : MostWebs;

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

        /// <summary>How much a bolt from a ward of this rank takes off, before the weak double.</summary>
        public static int DamageAt(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > MaxRank) rank = MaxRank;

            return ShotDamage * (10 + rank * RankDamageTenths) / 10;
        }

        /// <summary>What one bolt from a ward of this rank costs it, in tenths of fuel.</summary>
        public static int FuelShotTenths(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > MaxRank) rank = MaxRank;

            return FuelPerShotTenths - rank * RankFuelTenths;
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
        public const float CreeperMarch = 15f;
        public const int CreeperBlow = 1;

        /// <summary>A brute: slower, far tougher, and twice as expensive to let through.</summary>
        public const int BruteHealth = 480;
        public const float BruteMarch = 26f;
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
        /// (<see cref="WarbringerAfter"/>), the same player finishes with three and the line down
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
        /// How many of the hill's rows this kind's <em>body</em> covers, counting up from its feet.
        ///
        /// <para>
        /// <b>A firepot hits what a player can see, and for a boss those were two different
        /// things.</b> A raider stands at one point on the hill and a blast takes the box that
        /// point is in (<see cref="RowOf"/>), which is exactly right while a raider is about a cell
        /// tall and its body and its feet are in the same box. A boss is drawn three cells and more
        /// on a hill four rows deep, so most of what a player is aiming at is in the box *above*
        /// the one it occupies — tap the thing and nothing happens, which came back from play as
        /// "the bombs don't hit bosses". They did; they hit its feet.
        /// </para>
        /// <para>
        /// <b>A drawing number living in the rules</b>, exactly as <see cref="BossTell"/> and
        /// <see cref="SwapFor"/> are and for the same reason: what a blast hits and what the board
        /// draws have to be one fact, and the cheapest way to guarantee that is for there to be
        /// only one. <c>SiegeView.TallOf</c> is the height this is the row count of, and
        /// <c>SiegeRuleTests</c> holds the two in step.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>Three, and it is measured rather than picked.</b> The hill is 4.34 cells deep across
        /// <see cref="BlastRows"/> rows, so a row is about 1.09 cells; a boss is drawn 3.0 to 3.5
        /// cells tall (<c>SiegeView.TallOf</c>), which is 2.8 to 3.2 rows of body. Ordinary raiders
        /// are one and a bit cells and stay at one.
        /// </remarks>
        public static int RowsOf(SiegeKind kind) => IsBoss(kind) ? 3 : 1;

        /// <summary>
        /// Whether a raider standing at <paramref name="march"/> is caught by a blast on
        /// <paramref name="row"/>.
        ///
        /// Its feet are in <see cref="RowOf"/> and its body reaches <em>up</em> the hill from
        /// there — up the screen is toward the top, which is the lower row index, because row
        /// nought is where a wave steps out and the last row is the ward line.
        /// </summary>
        public static bool Caught(SiegeKind kind, float march, int row)
        {
            int feet = RowOf(march);
            return row <= feet && row > feet - RowsOf(kind);
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
             : kind == SiegeKind.Weaver ? SiegeSpell.Weave
             : kind == SiegeKind.Thief ? SiegeSpell.Snatch
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
            return craft != SiegeSpell.Rally && craft != SiegeSpell.Weave
                && craft != SiegeSpell.Snatch;
        }

        /// <summary>What a boss is called, for a message. Never shown to a player.</summary>
        public static string NameOf(SiegeKind kind)
        {
            for (int i = 0; i < SiegeLayout.BossNames.Length; i++)
                if (SiegeLayout.BossNames[i].Kind == kind) return SiegeLayout.BossNames[i].Name;

            return kind == SiegeKind.Bulwark ? "bulwark"
                 : kind == SiegeKind.Weaver ? "weaver"
                 : kind == SiegeKind.Thief ? "thief"
                 : kind == SiegeKind.Brute ? "brute" : "creeper";
        }

        public static int HealthOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordHealth
             : kind == SiegeKind.Warbringer ? WarbringerHealth
             : kind == SiegeKind.Boss ? BossHealth
             : kind == SiegeKind.Blightcaller ? BlightHealth
             : kind == SiegeKind.Bulwark ? BulwarkHealth
             : kind == SiegeKind.Weaver ? WeaverHealth
             : kind == SiegeKind.Thief ? ThiefHealth
             : kind == SiegeKind.Brute ? BruteHealth : CreeperHealth;

        public static float MarchOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordMarch
             : kind == SiegeKind.Warbringer ? WarbringerMarch
             : kind == SiegeKind.Boss ? BossMarch
             : kind == SiegeKind.Blightcaller ? BlightMarch
             : kind == SiegeKind.Bulwark ? BulwarkMarch
             : kind == SiegeKind.Weaver ? WeaverMarch
             : kind == SiegeKind.Thief ? ThiefMarch
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
            => IsBoss(kind) || HoldsTheField(kind) ? 0
             : kind == SiegeKind.Bulwark ? BulwarkBlow
             : kind == SiegeKind.Brute ? BruteBlow : CreeperBlow;

        /// <summary>How far down the hill this kind comes before it stops. A boss stops early.</summary>
        public static float HoldOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordHold
             : kind == SiegeKind.Warbringer ? WarbringerHold
             : kind == SiegeKind.Boss ? BossHold
             : kind == SiegeKind.Blightcaller ? BlightHold
             : kind == SiegeKind.Weaver ? WeaverHold
             : kind == SiegeKind.Thief ? ThiefHold : 1f;

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
        /// The quiet before a wave, which is longer before a boss and shortest before a
        /// warbringer.
        ///
        /// <para>
        /// <b>Per kind, and the two exceptions point opposite ways for the same reason.</b>
        /// Invariant 37t made the quiet before a <em>warlord</em> longer than any other so that a
        /// duel is never stacked on a wave still swinging at the line — two fail states arriving
        /// together read as being cheated. A warbringer wants precisely the opposite: half its
        /// spell is a rally, and a rally over an empty hill is a mechanic that rejects nothing
        /// (invariant 5d), so it comes while the last wave is still walking and the two are the
        /// fight.
        /// </para>
        /// <para>
        /// Both numbers are pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> rather
        /// than argued about, because a pacing change is a difficulty change (37s).
        /// </para>
        /// </summary>
        public static float RestBefore(SiegeKind kind)
            => kind == SiegeKind.Warbringer ? WarbringerAfter
             : IsBoss(kind) ? BossAfter : BetweenWaves;

        /// <summary>
        /// The quiet before a warbringer: short, so it arrives into a hill worth rallying.
        ///
        /// <b>Shorter than <see cref="BetweenWaves"/> and much shorter than
        /// <see cref="BossAfter"/></b>, which is the whole of why it is a number of its own — see
        /// <see cref="RestBefore"/>. It was 14, which with a rally of 1.9 cost an unhurried player
        /// three of four wards; 17 with a rally of 1.55 leaves three standing on a line bled to 38
        /// of 56. Neither number was reasoned about.
        /// </summary>
        public const float WarbringerAfter = 17f;

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
