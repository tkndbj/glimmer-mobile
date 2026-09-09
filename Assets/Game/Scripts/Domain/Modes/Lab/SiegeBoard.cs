using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a Thornwatch level authors: the gem field, the colours it refills from, the ward
    /// line standing in front of it, and the raiders walking down at it.
    ///
    /// <para>
    /// <b>Four things and no numbers</b>, which is invariant 20d's rule read across. How much
    /// fuel a match is worth, how hard a bolt hits, how fast a ward's fuel fades, how long a
    /// raider takes to cross the hill and what a blow costs a ward are all <see cref="SiegeTuning"/>
    /// — constants in code, one place, retuned for every level at once. A level says what is
    /// standing there and what is coming; the mode says what any of it does.
    /// </para>
    /// <para>
    /// <b>This is the one mode in this game that runs on a clock, and that is a deliberate
    /// departure worth naming rather than hiding.</b> Everything else here is turn-based and
    /// searchable: a move is a layer of a breadth-first walk, par is the depth of the first
    /// layer that wins, and both star lines fall out of it (invariant 20j). Raiders that walk
    /// while nobody is touching the board have no state graph, so par cannot be searched — it is
    /// <see cref="SiegeTuning.Par"/>, arithmetic over the raiders a level authors. Note what that
    /// does <em>not</em> change: a run is still graded on a count the player spends (matches), the
    /// star lines are still the same multiples of par every other mode uses, the fail state is
    /// still real, and the level is still an ordinary level with its own permanent id — so this
    /// mode cost the save file no schema version, no merge rule and no server work (invariant
    /// 20a), and can be withdrawn for the price of a chapter body exactly as five modes before it
    /// were.
    /// </para>
    /// </summary>
    public sealed class SiegeLayout
    {
        /// <summary>Gems the field is drawn with. Four, so a ward line of four has one each.</summary>
        public const string Letters = "rgby";

        /// <summary>What a ward may be. The same four, because a ward is fuelled by its own colour.</summary>
        public const string WardLetters = "rgby";

        /// <summary>
        /// What a wave may hold: a creeper in lower case, a brute in upper.
        ///
        /// One letter per raider rather than a count and a kind, so a wave reads as the thing
        /// that is coming — <c>"rrGb"</c> is two creepers, a brute and a creeper, in that order,
        /// and its shape is visible in the file.
        /// </summary>
        public const string RaiderLetters = "rgbyRGBY";

        /// <summary>What a warlord may be. The same four, because a boss is answered by a ward.</summary>
        public const string BossLetters = "rgby";

        /// <summary>How many wards a line may hold. Four colours, so four is the whole line.</summary>
        public const int MaxWards = 4;

        /// <summary>The most raiders one level may author. A bound on the run, not a taste.</summary>
        public const int MaxRaiders = 60;

        /// <summary>The opening field, exactly as it is dealt. Authored settled — no run of three.</summary>
        public readonly ProtoGrid Grid;

        /// <summary>
        /// The colours the field refills from, in the order they are written.
        ///
        /// An alphabet rather than a queue: a match-three board that refills has no fixed future
        /// whichever way the next gem is chosen, so nothing is bought by making the stream
        /// authorable. What it does buy is a level that can leave a colour <em>out</em>, which is
        /// the one thing about a refill an author might genuinely want to say.
        /// </summary>
        public readonly string Deal;

        /// <summary>The ward line, left to right. One colour each, and no two the same.</summary>
        public readonly char[] Wards;

        /// <summary>
        /// The waves, in the order they come — <b>including the warlord's</b>, which is appended
        /// rather than authored.
        ///
        /// See <see cref="Boss"/> for why a level says only <em>whether</em> there is one.
        /// </summary>
        public readonly string[] Waves;

        /// <summary>
        /// The warlord that comes last, as a colour letter, or <c>'\0'</c> for a siege that sends
        /// none.
        ///
        /// <para>
        /// <b>A level says whether there is one and what colour it wears, and never which wave it
        /// is in</b> — the last wave <em>is</em> the boss wave, by rule, so it cannot be authored
        /// into the middle of a siege and cannot be left off the end of one. That is the same
        /// bargain invariant 33g strikes with the haul-road and invariant 4a with the manifest:
        /// where a fact can be derived from a shape rather than typed beside it, the two can never
        /// come apart.
        /// </para>
        /// <para>
        /// <b>It is still a wave, and that is what makes it cost nothing.</b> The warlord is
        /// appended to <see cref="Waves"/> as a one-raider wave, so the wave count the header
        /// reads, the muster, the banner, <see cref="RaiderCount"/> and
        /// <see cref="SiegeTuning.Par"/> all take it without a single special case — the only
        /// question anything has to ask is <see cref="BossWave"/>, and only because a warlord's
        /// health and its way of fighting are not a creeper's.
        /// </para>
        /// </summary>
        public readonly char Boss;

        /// <summary>Which wave the warlord is, or -1. Always the last one when there is one.</summary>
        public readonly int BossWave = -1;

        /// <summary>Whether this siege ends with a warlord.</summary>
        public bool HasBoss => Boss != '\0';

        /// <summary>
        /// Where the refill stream starts, derived from the authored field rather than typed.
        ///
        /// <b>Derived, so no level ever authors a seed.</b> Two devices dealing the same board the
        /// same way is worth having — a bug reported against a level is a bug somebody else can
        /// meet — and a number in the file is a number an author has to invent and can mistype.
        /// </summary>
        public readonly uint Seed;

        /// <summary>What is wrong with this level, or null.</summary>
        public readonly string Fault;

        public SiegeLayout(ProtoGrid grid, string deal, string wards, string[] waves, string boss)
        {
            Grid = grid;
            Deal = Tidy(deal, Letters);
            Wards = Tidy(wards, WardLetters).ToCharArray();

            // **Exactly one legal letter, or nothing** — not `Tidy`, which keeps whatever it
            // recognises and throws the rest away. "dragon" would salvage an 'r' out of itself and
            // ship a red warlord nobody authored, which is the shape of accident invariant 5f
            // exists to refuse: content written for a build that is not this one has to be said
            // out loud rather than quietly interpreted.
            string named = (boss ?? string.Empty).Trim();

            Boss = named.Length == 1 && BossLetters.IndexOf(named[0]) >= 0 ? named[0] : '\0';

            var coming = new List<string>(Trim(waves));

            if (HasBoss)
            {
                BossWave = coming.Count;
                coming.Add(Boss.ToString());
            }

            Waves = coming.ToArray();
            Seed = Hash(grid);
            Fault = Check(boss);
        }

        static string Tidy(string raw, string legal)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var kept = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] != ' ' && legal.IndexOf(raw[i]) >= 0) kept.Append(raw[i]);

            return kept.ToString();
        }

        static string[] Trim(string[] waves)
        {
            if (waves == null) return Array.Empty<string>();

            var kept = new List<string>(waves.Length);
            for (int i = 0; i < waves.Length; i++)
            {
                string wave = Tidy(waves[i], RaiderLetters);
                if (wave.Length > 0) kept.Add(wave);
            }

            return kept.ToArray();
        }

        /// <summary>
        /// FNV-1a over the authored field. Thirty-two bit throughout, for
        /// <c>DailyChestTable</c>'s reason: the offline mirror runs in Python and has to reach the
        /// same board.
        /// </summary>
        static uint Hash(ProtoGrid grid)
        {
            uint h = 2166136261u;

            for (int i = 0; grid != null && i < grid.Count; i++)
            {
                h ^= grid.At(i);
                h = unchecked(h * 16777619u);
            }

            return h == 0u ? 1u : h;
        }

        string Check(string boss)
        {
            if (Grid == null) return "no field";

            // Refused by name rather than ignored, which is invariant 5f's rule for a token a
            // build no longer knows read the other way round: a level that names a warlord this
            // mode cannot draw would otherwise index, validate and ship as a siege with no boss in
            // it, and nothing anywhere would say so.
            if (!string.IsNullOrEmpty(boss) && boss.Trim().Length > 0 && !HasBoss)
                return $"'{boss}' is not a warlord this mode knows; a boss is one of "
                     + $"'{BossLetters}', and an empty field is how a siege says it sends none";

            if (Wards.Length < 2 || Wards.Length > MaxWards)
                return $"a ward line holds 2 to {MaxWards} wards; this one names {Wards.Length}";

            for (int i = 0; i < Wards.Length; i++)
                for (int j = i + 1; j < Wards.Length; j++)
                    if (Wards[i] == Wards[j])
                        return $"two wards on this line are both '{Wards[i]}', so one of them can " +
                               "never be the only thing a colour feeds and half the line is a "
                               + "spare part";

            if (Deal.Length < 2)
                return "the field refills from fewer than two colours, so every arrangement is a "
                     + "match and nothing is ever decided";

            // A ward nothing feeds is decoration standing where a ward should be (invariant 5d).
            for (int i = 0; i < Wards.Length; i++)
                if (Deal.IndexOf(Wards[i]) < 0)
                    return $"the '{Wards[i]}' ward stands on a field that never deals a "
                         + $"'{Wards[i]}' gem, so nothing the player does could ever fuel it";

            if (Waves.Length == 0) return "nothing is coming, so there is nothing to hold";

            int raiders = 0;
            for (int i = 0; i < Waves.Length; i++) raiders += Waves[i].Length;

            if (raiders > MaxRaiders)
                return $"this level sends {raiders} raiders; {MaxRaiders} is the most a run may "
                     + "hold, and a longer siege wants a second level rather than a longer wave";

            // A raider no ward on the line is strong against is one the player cannot answer
            // properly - it is still killable, at half rate, which is a level asking for
            // something it never taught. Certain, and the arithmetic par assumes it is not so.
            for (int w = 0; w < Waves.Length; w++)
                for (int i = 0; i < Waves[w].Length; i++)
                {
                    char colour = char.ToLowerInvariant(Waves[w][i]);
                    if (Array.IndexOf(Wards, colour) >= 0) continue;

                    // The warlord is the one this really matters for: it has the health of a whole
                    // wave, so answering it at half rate is a duel nobody can finish.
                    string what = w == BossWave ? "a '" + colour + "' warlord"
                                                : "a '" + colour + "' raider";

                    return $"wave {w + 1} sends {what} and no ward on this line carries "
                         + $"'{colour}', so nothing here is strong against it";
                }

            // Authored settled. A field that goes off before anybody has touched it is a board
            // whose opening move its author played, and the count the run is graded against would
            // already have moved - Budburst's rule, and Prismvale's, asked of a jewel board.
            var cells = Grid.Copy();
            if (Runs(cells, Grid.Width, Grid.Height).Count > 0)
                return "three alike are already touching on this field, so it would go off before "
                     + "anybody had moved a gem - a field is authored settled";

            return null;
        }

        /// <summary>Every cell standing in a run of three or more, as one set.</summary>
        internal static HashSet<int> Runs(char[] cells, int width, int height)
        {
            var hit = new HashSet<int>();

            for (int y = 0; y < height; y++)
            {
                int run = 1;
                for (int x = 1; x <= width; x++)
                {
                    bool same = x < width && cells[y * width + x] != SiegeBoard.Hole
                             && cells[y * width + x] == cells[y * width + x - 1];

                    if (same) { run++; continue; }

                    if (run >= SiegeTuning.MinRun)
                        for (int k = x - run; k < x; k++) hit.Add(y * width + k);

                    run = 1;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int run = 1;
                for (int y = 1; y <= height; y++)
                {
                    bool same = y < height && cells[y * width + x] != SiegeBoard.Hole
                             && cells[y * width + x] == cells[(y - 1) * width + x];

                    if (same) { run++; continue; }

                    if (run >= SiegeTuning.MinRun)
                        for (int k = y - run; k < y; k++) hit.Add(k * width + x);

                    run = 1;
                }
            }

            return hit;
        }

        /// <summary>Which ward carries this colour, or -1.</summary>
        public int WardOf(char colour) => Array.IndexOf(Wards, colour);

        /// <summary>Every raider this level sends, in the order they arrive.</summary>
        public int RaiderCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Waves.Length; i++) n += Waves[i].Length;
                return n;
            }
        }
    }

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

        /// <summary>Fuel a matched gem is worth. One each, so a match is worth what it looks like.</summary>
        public const float FuelPerGem = 1f;

        /// <summary>The most fuel a ward holds. A big match tops it up rather than banking.</summary>
        public const float WardCapacity = 14f;

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
        /// instrument (invariant 37j). It is also what picked this number: .20 and .22 hold the
        /// line, and <b>.26 loses it outright</b> - the hill unclear and every ward down. So the
        /// band between "too fast to watch" and "too slow to hold" is narrow, and anything slower
        /// than this has to buy the time back somewhere else.
        /// </para>
        /// </summary>
        public const float FireEvery = .22f;

        /// <summary>Fuel one bolt spends.</summary>
        public const float FuelPerShot = 1f;

        /// <summary>What one bolt takes off a raider it is not strong against.</summary>
        public const int ShotDamage = 2;

        /// <summary>What a bolt is worth against a raider of its own colour.</summary>
        public const int WeakMultiplier = 2;

        /// <summary>A creeper: the ordinary raider, and what most of a wave is.</summary>
        public const int CreeperHealth = 20;
        public const float CreeperMarch = 15f;
        public const int CreeperBlow = 1;

        /// <summary>A brute: slower, far tougher, and twice as expensive to let through.</summary>
        public const int BruteHealth = 48;
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
        public const int BossHealth = 180;

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
        /// <b>It was 22, which is ten seconds of entrance, and the owner asked for it faster after
        /// playing.</b> A creeper crosses the whole hill in fifteen; there was no reason for the
        /// warlord to be slower per unit of ground than the smallest thing on the board except that
        /// slow reads as heavy — and past a few seconds it stops reading as heavy and starts reading
        /// as a wait. What carries the weight instead is its size, its own frames and the shake it
        /// arrives with. <c>SiegeRuleTests.AWarlordWalksOnBeforeItStands</c> holds the entrance
        /// under eight seconds so the number cannot drift back.
        /// </summary>
        public const float BossMarch = 13f;

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

        /// <summary>
        /// Which of the three a wave's token is.
        ///
        /// <b>The wave decides, never the letter.</b> A warlord is written as its colour — the
        /// same character a creeper is — because what makes it a warlord is standing alone in the
        /// last wave (<see cref="SiegeLayout.Boss"/>). Asking the letter would be a second opinion
        /// about a fact the layout already holds.
        /// </summary>
        public static SiegeKind KindOf(char token, bool boss)
            => boss ? SiegeKind.Boss
             : char.IsUpper(token) ? SiegeKind.Brute
             : SiegeKind.Creeper;

        public static int HealthOf(SiegeKind kind)
            => kind == SiegeKind.Boss ? BossHealth
             : kind == SiegeKind.Brute ? BruteHealth : CreeperHealth;

        public static float MarchOf(SiegeKind kind)
            => kind == SiegeKind.Boss ? BossMarch
             : kind == SiegeKind.Brute ? BruteMarch : CreeperMarch;

        /// <summary>
        /// What one swing at the line costs a ward.
        ///
        /// <b>A warlord swings at nothing</b>, because it never reaches the line — what it costs
        /// the line is <see cref="BossCast"/>, from where it stands.
        /// </summary>
        public static int BlowOf(SiegeKind kind)
            => kind == SiegeKind.Boss ? 0
             : kind == SiegeKind.Brute ? BruteBlow : CreeperBlow;

        /// <summary>How far down the hill this kind comes before it stops. A warlord stops early.</summary>
        public static float HoldOf(SiegeKind kind) => kind == SiegeKind.Boss ? BossHold : 1f;

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
        /// What one match delivers: the gems it clears, each spent as a bolt into the ward the
        /// raider in front of it is weak to.
        ///
        /// Integer arithmetic throughout, for <c>LevelTuning</c>'s reason: a graded number decided
        /// by a float is a number three code generators round three ways.
        /// </summary>
        public const int PerfectMatch =
            MatchGemsTenths * ShotDamage * WeakMultiplier / 10;

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
        /// </summary>
        public static int Par(SiegeLayout layout)
        {
            if (layout == null) return 1;

            int health = 0;
            for (int w = 0; w < layout.Waves.Length; w++)
                for (int i = 0; i < layout.Waves[w].Length; i++)
                    health += HealthOf(KindOf(layout.Waves[w][i], w == layout.BossWave));

            int par = (health + PerfectMatch - 1) / PerfectMatch;
            return par < 1 ? 1 : par;
        }
    }

    /// <summary>
    /// What is walking down the hill: an ordinary raider, a brute, or the warlord.
    ///
    /// <b>An enum rather than a second bool, and the third member is why.</b> It was
    /// <c>bool Brute</c>, which is exactly right for two kinds and becomes a pair of flags that
    /// can both be true the moment there is a third — and every table keyed on it (health, march,
    /// blow, how far it comes) would then have had to agree about which flag wins.
    /// </summary>
    public enum SiegeKind
    {
        Creeper,
        Brute,

        /// <summary>The warlord: it holds the middle of the hill and hits the line from there.</summary>
        Boss,
    }

    /// <summary>One raider on the hill.</summary>
    public sealed class SiegeRaider
    {
        public readonly int Id;

        /// <summary>Which of <see cref="SiegeLayout.Letters"/> it wears, as an index.</summary>
        public readonly int Colour;

        public readonly SiegeKind Kind;

        /// <summary>Which lane it walks down, 0 at the left.</summary>
        public readonly int Lane;

        /// <summary>Seconds before it steps out. Not on the hill until this reaches nought.</summary>
        public float Wait;

        /// <summary>How far down the hill it is: 0 at the top, 1 at the ward line.</summary>
        public float March;

        /// <summary>How far down it comes before it stops. One for everything but a warlord.</summary>
        public readonly float Hold;

        public int Health;
        public readonly int MaxHealth;

        public bool Alive = true;

        /// <summary>Seconds until its next blow, once it has arrived.</summary>
        public float Blow;

        /// <summary>Seconds until its next spell. A warlord's only, and only once it is in place.</summary>
        public float Spell;

        /// <summary>Set for one frame after it has been hit, so the view can flash it.</summary>
        public float Flash;

        public SiegeRaider(int id, int colour, SiegeKind kind, int lane, float wait)
        {
            Id = id;
            Colour = colour;
            Kind = kind;
            Lane = lane;
            Wait = wait;
            Hold = SiegeTuning.HoldOf(kind);
            MaxHealth = SiegeTuning.HealthOf(kind);
            Health = MaxHealth;
            Blow = SiegeTuning.BlowEvery * .5f;
            Spell = SiegeTuning.BossWakes;
        }

        public bool Brute => Kind == SiegeKind.Brute;

        public bool Boss => Kind == SiegeKind.Boss;

        public bool OnTheHill => Wait <= 0f;

        /// <summary>
        /// Whether it is standing at the line and swinging.
        ///
        /// <b>A warlord never is</b>, because <see cref="Hold"/> stops it short of it — which is
        /// what makes <see cref="SiegeBoard.Swing"/> need no clause about bosses at all.
        /// </summary>
        public bool AtTheLine => Alive && OnTheHill && Hold >= 1f && March >= 1f;

        /// <summary>Whether it has reached the ground it holds and may start casting.</summary>
        public bool InPlace => Alive && OnTheHill && March >= Hold;
    }

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

        public SiegeWard(int colour) => Colour = colour;

        public bool Fuelled => Alive && Fuel >= SiegeTuning.FuelPerShot;

        public float Charge => Fuel / SiegeTuning.WardCapacity;
    }

    /// <summary>A bolt that left a ward, for the view to draw.</summary>
    public readonly struct SiegeBolt
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Weak, Killed;

        public SiegeBolt(int ward, int raider, int damage, bool weak, bool killed)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Weak = weak;
            Killed = killed;
        }
    }

    /// <summary>A blow that landed on the line, for the view to draw.</summary>
    /// <summary>
    /// What a utility did to one raider - a hit that came from the player's own hand rather
    /// than out of a ward.
    ///
    /// Its own record and not a <see cref="SiegeBolt"/>, because a bolt names the ward that
    /// fired it and this has no ward: the view draws the two differently and analytics has to
    /// be able to tell them apart.
    /// </summary>
    public readonly struct SiegeStrike
    {
        public readonly int Raider, Damage;
        public readonly bool Killed;

        public SiegeStrike(int raider, int damage, bool killed)
        {
            Raider = raider;
            Damage = damage;
            Killed = killed;
        }
    }

    public readonly struct SiegeBlow
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Felled;

        public SiegeBlow(int ward, int raider, int damage, bool felled)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Felled = felled;
        }
    }

    /// <summary>
    /// A warlord beginning a spell: which ward it has chosen, and how long the player has.
    ///
    /// <b>Its own record and raised the moment the spell is <em>decided</em></b>, which is the
    /// point of it: the wind-up is what the view draws over the ward that is about to be hit, and
    /// it is the window a <c>mending</c> is worth spending in. A cast that was reported only when
    /// it landed would be a fail state arriving with no warning, which is the one thing this mode
    /// already refuses to do with a wave.
    /// </summary>
    public readonly struct SiegeCast
    {
        public readonly int Raider, Ward;

        /// <summary>Seconds from now until it lands. <see cref="SiegeTuning.BossTell"/> of that is the tell.</summary>
        public readonly float In;

        public SiegeCast(int raider, int ward, float @in)
        {
            Raider = raider;
            Ward = ward;
            In = @in;
        }
    }

    /// <summary>
    /// A spell landing on the line.
    ///
    /// Its own record and not a <see cref="SiegeBlow"/>, for <see cref="SiegeStrike"/>'s reason:
    /// a blow is swung at the line by something standing at it, a spell is thrown from the middle
    /// of the hill, and the view draws the two nothing alike.
    /// </summary>
    public readonly struct SiegeSpell
    {
        public readonly int Raider, Ward, Damage;
        public readonly bool Felled;

        public SiegeSpell(int raider, int ward, int damage, bool felled)
        {
            Raider = raider;
            Ward = ward;
            Damage = damage;
            Felled = felled;
        }
    }

    /// <summary>Everything that happened in one step of the clock.</summary>
    public sealed class SiegeReport
    {
        public readonly List<SiegeBolt> Bolts = new List<SiegeBolt>(16);
        public readonly List<SiegeBlow> Blows = new List<SiegeBlow>(4);
        public readonly List<SiegeCast> Casts = new List<SiegeCast>(2);
        public readonly List<SiegeSpell> Spells = new List<SiegeSpell>(2);
        public readonly List<int> Arrived = new List<int>(8);

        /// <summary>The wave that has just stepped out, or -1.</summary>
        public int Wave = -1;

        public void Clear()
        {
            Bolts.Clear();
            Blows.Clear();
            Casts.Clear();
            Spells.Clear();
            Arrived.Clear();
            Wave = -1;
        }

        public bool Any => Bolts.Count > 0 || Blows.Count > 0 || Casts.Count > 0
                        || Spells.Count > 0 || Arrived.Count > 0 || Wave >= 0;
    }

    /// <summary>Fuel a match has earned that has not reached its ward yet.</summary>
    public struct SiegeCharge
    {
        public int Ward;
        public float Fuel;
        public float In;
    }

    /// <summary>One gem falling, or arriving. <see cref="From"/> below nought is a new gem.</summary>
    public readonly struct SiegeDrop
    {
        public readonly int Column, From, To, Colour;

        public SiegeDrop(int column, int from, int to, int colour)
        {
            Column = column;
            From = from;
            To = to;
            Colour = colour;
        }

        public bool IsNew => From < 0;
    }

    /// <summary>One beat of a cascade: what went, what it fuelled, and what fell into the gap.</summary>
    public sealed class SiegeBeat
    {
        public readonly List<int> Cleared = new List<int>(12);
        public readonly List<SiegeDrop> Drops = new List<SiegeDrop>(24);

        /// <summary>Fuel this beat put into each ward, in ward order.</summary>
        public float[] Fuel;

        /// <summary>Which beat of the cascade this is, counting from one.</summary>
        public int Depth;
    }

    /// <summary>What one swap turned into.</summary>
    public sealed class SiegeTurn
    {
        public int A, B;
        public readonly List<SiegeBeat> Beats = new List<SiegeBeat>(4);

        /// <summary>Gems cleared altogether, which is what the flourish readout counts.</summary>
        public int Worth;
    }

    /// <summary>
    /// A siege: a field of gems, a line of wards and a hill with raiders coming down it.
    ///
    /// <para>
    /// <b>The clock is passed in.</b> Nothing here reads <c>Time</c> — <see cref="Advance"/> takes
    /// the seconds that have gone by, so the whole mode is Domain and can be stepped by a test at
    /// whatever rate a test likes. That is the same bargain every board here makes and the reason
    /// this one is not simply written inside the view.
    /// </para>
    /// </summary>
    public sealed class SiegeBoard : IProtoBoard
    {
        /// <summary>A cell with nothing in it, which only ever exists mid-resolve.</summary>
        public const char Hole = '.';

        public readonly SiegeLayout Layout;

        readonly char[] _cells;
        readonly SiegeWard[] _wards;
        readonly List<SiegeRaider> _raiders = new List<SiegeRaider>(24);
        readonly SiegeReport _report = new SiegeReport();
        readonly List<SiegeCharge> _flying = new List<SiegeCharge>(16);

        /// <summary>A spell that has been cast and has not arrived. See <see cref="Conjure"/>.</summary>
        struct Flight
        {
            public int Raider, Ward;
            public float In;
        }

        readonly List<Flight> _spells = new List<Flight>(4);

        uint _rng;
        int _wave;
        int _minted;
        int _felled;
        float _rest;

        SiegeBoard(SiegeLayout layout)
        {
            Layout = layout;
            _cells = layout.Grid.Copy();
            _rng = layout.Seed;

            _wards = new SiegeWard[layout.Wards.Length];
            for (int i = 0; i < _wards.Length; i++)
                _wards[i] = new SiegeWard(SiegeLayout.Letters.IndexOf(layout.Wards[i]));

            _rest = SiegeTuning.FirstWaveAfter;
        }

        public static SiegeBoard Build(SiegeLayout layout) => new SiegeBoard(layout);

        // ------------------------------------------------------------------ the field
        public int Width => Layout.Grid.Width;
        public int Height => Layout.Grid.Height;
        public int Count => _cells.Length;

        public char At(int index) => index >= 0 && index < _cells.Length ? _cells[index] : Hole;

        public int ColourAt(int index) => SiegeLayout.Letters.IndexOf(At(index));

        public int IndexOf(int x, int y) => y * Width + x;

        public IReadOnlyList<SiegeWard> Wards => _wards;

        /// <summary>Fuel booked and still crossing the field. Nothing grades on it.</summary>
        public IReadOnlyList<SiegeCharge> Flying => _flying;
        public IReadOnlyList<SiegeRaider> Raiders => _raiders;

        public int WardsStanding
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _wards.Length; i++) if (_wards[i].Alive) n++;
                return n;
            }
        }

        /// <summary>The wave now on the hill, counting from one. Nought before the first.</summary>
        public int Wave => _wave;

        /// <summary>How many waves this siege sends, the warlord's included.</summary>
        public int Waves => Layout.Waves.Length;

        /// <summary>Whether the wave now on the hill is the warlord's.</summary>
        public bool BossWave => Layout.HasBoss && _wave == Layout.BossWave + 1;

        /// <summary>The warlord, while it is standing, or null.</summary>
        public SiegeRaider Warlord
        {
            get
            {
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Boss && _raiders[i].Alive) return _raiders[i];

                return null;
            }
        }

        // ------------------------------------------------------------------ IProtoBoard
        public bool IsFinished => GoalsLeft == 0;

        public int Goals => Layout.RaiderCount;

        public int GoalsLeft => Goals - _felled;

        /// <summary>
        /// Whether a move on this board could still mean anything.
        ///
        /// <b>A line with nothing standing on it is where a run of this mode ends</b>, and it is
        /// honestly this question rather than a special case: with every ward down, a match feeds
        /// nothing, so there is no legal move left however many gems are on the field. The gem
        /// field itself always has a swap — <see cref="Settle"/> deals it again rather than
        /// letting it lock (invariant 20j's second test: every input has to move something).
        /// </summary>
        public bool AnyMove => WardsStanding > 0;

        /// <summary>
        /// <b>Never rescued, and that is the arithmetic rather than a policy.</b> A run here ends
        /// when the last ward falls, and no purchase puts one back up — so this is invariant 28f's
        /// certainty, and <c>ProtoVerdict</c> answers <c>NoContinue</c> rather than selling a
        /// finish that could not happen. The mistake money cannot fix is the one the mode is
        /// about.
        /// </summary>
        public bool Stranded => WardsStanding == 0;

        // ------------------------------------------------------------------ swapping
        public bool Adjacent(int a, int b)
        {
            if (a < 0 || b < 0 || a >= Count || b >= Count || a == b) return false;

            int ax = a % Width, ay = a / Width, bx = b % Width, by = b / Width;
            return Math.Abs(ax - bx) + Math.Abs(ay - by) == 1;
        }

        /// <summary>Whether swapping these two would line anything up.</summary>
        public bool Lines(int a, int b)
        {
            if (!Adjacent(a, b)) return false;
            if (_cells[a] == _cells[b]) return false;

            char keepA = _cells[a], keepB = _cells[b];
            _cells[a] = keepB;
            _cells[b] = keepA;

            bool any = SiegeLayout.Runs(_cells, Width, Height).Count > 0;

            _cells[a] = keepA;
            _cells[b] = keepB;
            return any;
        }

        /// <summary>Whether any swap on this field would line anything up.</summary>
        public bool AnySwap()
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int here = IndexOf(x, y);
                    if (x + 1 < Width && Lines(here, here + 1)) return true;
                    if (y + 1 < Height && Lines(here, here + Width)) return true;
                }

            return false;
        }

        /// <summary>
        /// Swaps two gems and resolves everything that follows, fuelling the wards as it goes.
        ///
        /// Answers null for a swap that lines nothing up: the field is left exactly as it was and
        /// the run is charged nothing, which is what makes trying a swap free and keeping one
        /// expensive.
        /// </summary>
        public SiegeTurn Swap(int a, int b)
        {
            if (!Lines(a, b)) return null;

            char keep = _cells[a];
            _cells[a] = _cells[b];
            _cells[b] = keep;

            var turn = new SiegeTurn { A = a, B = b };

            int depth = 0;
            while (true)
            {
                var hit = SiegeLayout.Runs(_cells, Width, Height);
                if (hit.Count == 0) break;

                depth++;
                var beat = new SiegeBeat { Depth = depth, Fuel = new float[_wards.Length] };

                foreach (int cell in hit)
                {
                    beat.Cleared.Add(cell);

                    int ward = Layout.WardOf(_cells[cell]);
                    if (ward >= 0) beat.Fuel[ward] += SiegeTuning.FuelPerGem;

                    _cells[cell] = Hole;
                }

                beat.Cleared.Sort();
                turn.Worth += beat.Cleared.Count;

                for (int w = 0; w < _wards.Length; w++)
                {
                    if (beat.Fuel[w] <= 0f) continue;

                    // **Booked, not paid.** It lands when the motes do - see
                    // `SiegeTuning.FuelLands`. A fallen ward is not filtered here either: it may
                    // still be standing now and down by the time this arrives, and the ward that
                    // is asked is the one that exists when the fuel gets there.
                    _flying.Add(new SiegeCharge
                    {
                        Ward = w,
                        Fuel = beat.Fuel[w],
                        In = SiegeTuning.FuelLands(depth - 1),
                    });
                }

                Collapse(beat);
                turn.Beats.Add(beat);
            }

            Settle();
            return turn;
        }

        /// <summary>Gravity, then a refill, both written into the beat for the view to animate.</summary>
        void Collapse(SiegeBeat beat)
        {
            for (int x = 0; x < Width; x++)
            {
                int write = Height - 1;

                for (int y = Height - 1; y >= 0; y--)
                {
                    char c = _cells[IndexOf(x, y)];
                    if (c == Hole) continue;

                    if (write != y)
                    {
                        _cells[IndexOf(x, write)] = c;
                        _cells[IndexOf(x, y)] = Hole;
                        beat.Drops.Add(new SiegeDrop(x, y, write, SiegeLayout.Letters.IndexOf(c)));
                    }

                    write--;
                }

                // Everything above the write head is new, and it falls in from above the field.
                int fresh = 0;
                for (int y = write; y >= 0; y--)
                {
                    char c = Deal();
                    _cells[IndexOf(x, y)] = c;
                    beat.Drops.Add(new SiegeDrop(x, -1 - fresh, y, SiegeLayout.Letters.IndexOf(c)));
                    fresh++;
                }
            }
        }

        /// <summary>
        /// Deals the field again if nothing on it lines up.
        ///
        /// <b>A field that cannot be played is the one thing this mode may never show</b>, because
        /// its clock does not stop: a locked board with raiders still walking is a run the player
        /// watches themselves lose. It is deterministic, so two devices reshuffle the same way.
        /// </summary>
        void Settle()
        {
            for (int attempt = 0; attempt < 40 && !AnySwap(); attempt++)
            {
                for (int i = _cells.Length - 1; i > 0; i--)
                {
                    int j = (int)(Next() % (uint)(i + 1));
                    char keep = _cells[i];
                    _cells[i] = _cells[j];
                    _cells[j] = keep;
                }

                // A shuffle that lands three alike together would go off with nobody having
                // touched it, so it is dealt again rather than resolved.
                if (SiegeLayout.Runs(_cells, Width, Height).Count > 0) continue;
            }
        }

        char Deal() => Layout.Deal[(int)(Next() % (uint)Layout.Deal.Length)];

        /// <summary>xorshift32. Thirty-two bit throughout so the Python mirror reaches the same field.</summary>
        uint Next()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x == 0u ? 2463534242u : x;
            return _rng;
        }

        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// Steps the siege by <paramref name="dt"/> seconds and says what happened.
        ///
        /// <para>
        /// The order matters and is the order a player would want: the wave arrives, then the
        /// raiders walk, then the wards shoot at where the raiders now are, then the warlord casts,
        /// then whatever reached the line swings. A ward that has just been fuelled therefore gets
        /// its bolt away in the same step, and a raider killed by that bolt never lands the blow it
        /// was about to — nor does a warlord killed by it ever start the spell it was about to.
        /// </para>
        /// </summary>
        public SiegeReport Advance(float dt)
        {
            _report.Clear();

            if (dt <= 0f) return _report;
            if (dt > .25f) dt = .25f;      // a resumed app must not teleport a wave into the line

            Land(dt);
            Muster(dt);
            Walk(dt);
            Shoot(dt);
            Conjure(dt);
            Swing(dt);

            for (int i = _raiders.Count - 1; i >= 0; i--)
                if (!_raiders[i].Alive) _raiders.RemoveAt(i);

            return _report;
        }

        /// <summary>
        /// Lands whatever fuel has finished crossing the field.
        ///
        /// <b>Before anything else in the step</b>, so a ward fed on this frame may fire on it -
        /// the delay is the flight, not a further beat of hesitation once it has arrived.
        /// </summary>
        void Land(float dt)
        {
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                var charge = _flying[i];
                charge.In -= dt;

                if (charge.In > 0f)
                {
                    _flying[i] = charge;
                    continue;
                }

                _flying.RemoveAt(i);

                // A fallen ward takes nothing. The gems still went, which is the whole cost of
                // losing one: the colour is still on the field and is worth nothing now.
                var ward = _wards[charge.Ward];
                if (!ward.Alive) continue;

                ward.Fuel = Math.Min(SiegeTuning.WardCapacity, ward.Fuel + charge.Fuel);
            }

            Arrive(dt);
        }

        /// <summary>
        /// Lands whatever spell has finished crossing the hill.
        ///
        /// <para>
        /// <b>A spell whose caster has been destroyed fizzles</b>, and that is a decision rather
        /// than tidiness. The alternative is a ward coming down — and a run being lost — to
        /// something thrown by a warlord the player had already beaten, which reads as the game
        /// getting the last word. It also makes killing a warlord mid-wind-up worth something,
        /// which the tell is long enough to make possible.
        /// </para>
        /// <para>
        /// A spell aimed at a ward that has since fallen is dropped for <see cref="Land"/>'s own
        /// reason: the ward that is asked is the one standing when it gets there.
        /// </para>
        /// </summary>
        void Arrive(float dt)
        {
            for (int i = _spells.Count - 1; i >= 0; i--)
            {
                var spell = _spells[i];
                spell.In -= dt;

                if (spell.In > 0f) { _spells[i] = spell; continue; }

                _spells.RemoveAt(i);

                var caster = Find(spell.Raider);
                if (caster == null || !caster.Alive) continue;

                var ward = _wards[spell.Ward];
                if (!ward.Alive) continue;

                ward.Health -= SiegeTuning.BossCast;

                bool felled = ward.Health <= 0;
                if (felled)
                {
                    ward.Health = 0;
                    ward.Alive = false;
                    ward.Fuel = 0f;
                }

                _report.Spells.Add(new SiegeSpell(spell.Raider, spell.Ward,
                                                  SiegeTuning.BossCast, felled));
            }
        }

        void Muster(float dt)
        {
            if (_wave >= Layout.Waves.Length) return;

            // **On a clock, or the moment the hill is empty — whichever comes first.**
            //
            // The clock alone was the fix for waiting-on-a-clear, which let a winning player
            // stroll; a clear alone is what it replaced. Both together are what the mode actually
            // wants: the clock is the pressure and never lets up, and the shortcut means a player
            // who is *ahead* of it is rewarded with the next wave rather than made to stand and
            // watch an empty field. Note the guard — the shortcut cannot fire before the first
            // wave, because the hill is legitimately empty at the start of every run.
            _rest -= dt;

            if (_rest > 0f)
            {
                if (_wave == 0) return;

                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive) return;
            }

            string wave = Layout.Waves[_wave];
            bool boss = _wave == Layout.BossWave;

            for (int i = 0; i < wave.Length; i++)
            {
                char token = wave[i];
                var kind = SiegeTuning.KindOf(token, boss);
                int colour = SiegeLayout.Letters.IndexOf(char.ToLowerInvariant(token));

                // Lanes are dealt from the same stream the field is, so a wave arrives spread out
                // rather than in a column - and spread the same way on every device.
                //
                // **The warlord is the exception and walks down the middle**, because where it
                // stands is not a fact anybody should have to hunt for: it is the largest thing on
                // the board and it stays put for the rest of the run, so a dealt lane would put it
                // over a ward on some devices and off the edge of the hill on others.
                int lane = boss ? SiegeTuning.Lanes / 2 : (int)(Next() % SiegeTuning.Lanes);

                _raiders.Add(new SiegeRaider(_minted++, colour, kind, lane,
                                             i * SiegeTuning.RaiderSpacing));
            }

            _report.Wave = _wave;
            _wave++;

            // The warlord gets a longer quiet in front of him than any other wave - see
            // `SiegeTuning.BossAfter`. The shortcut above is unaffected, so a player who has
            // cleared the hill still gets him at once.
            _rest = _wave == Layout.BossWave ? SiegeTuning.BossAfter : SiegeTuning.BetweenWaves;
        }

        void Walk(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive) continue;

                if (raider.Flash > 0f) raider.Flash = Math.Max(0f, raider.Flash - dt);

                if (raider.Wait > 0f)
                {
                    raider.Wait -= dt;
                    continue;
                }

                if (raider.March >= raider.Hold) continue;

                raider.March += dt / SiegeTuning.MarchOf(raider.Kind);

                if (raider.March < raider.Hold) continue;

                // **Stopped where its kind stops**, which for a warlord is the middle of the hill
                // and for everything else is the line. Nothing here needs to know which: the two
                // differ by one number the raider was minted with.
                raider.March = raider.Hold;

                if (!raider.AtTheLine) continue;

                raider.Blow = SiegeTuning.BlowEvery;
                _report.Arrived.Add(raider.Id);
            }
        }

        void Shoot(float dt)
        {
            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                if (!ward.Fuelled) { ward.Cool = 0f; continue; }

                ward.Cool -= dt;
                if (ward.Cool > 0f) continue;

                var target = Aim(ward.Colour);
                if (target == null) { ward.Cool = 0f; continue; }

                ward.Cool = SiegeTuning.FireEvery;
                ward.Fuel = Math.Max(0f, ward.Fuel - SiegeTuning.FuelPerShot);

                bool weak = target.Colour == ward.Colour;
                int damage = SiegeTuning.ShotDamage * (weak ? SiegeTuning.WeakMultiplier : 1);

                target.Health -= damage;
                target.Flash = .18f;

                bool killed = target.Health <= 0;
                if (killed)
                {
                    target.Alive = false;
                    _felled++;
                }

                _report.Bolts.Add(new SiegeBolt(w, target.Id, damage, weak, killed));
            }
        }

        /// <summary>
        /// What a ward shoots at: the raider of its own colour that is furthest down the hill,
        /// and otherwise whichever raider is furthest down.
        ///
        /// <b>Its own colour first, so the rule is visible in play.</b> A player who has fed the
        /// right ward sees the bolts go to the thing that colour hurts; one who has not sees them
        /// spread. Nothing has to be told about it.
        /// </summary>
        SiegeRaider Aim(int colour)
        {
            SiegeRaider weak = null, near = null;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (near == null || raider.March > near.March) near = raider;

                if (raider.Colour != colour) continue;
                if (weak == null || raider.March > weak.March) weak = raider;
            }

            return weak ?? near;
        }

        /// <summary>
        /// The warlord's spells: chosen, telegraphed, and thrown at the line from where it stands.
        ///
        /// <para>
        /// <b>Nothing lands here.</b> A cast decides a target and books a
        /// <see cref="Flight"/>; <see cref="Arrive"/> is what takes the ward's health, a whole
        /// <see cref="SiegeTuning.BossTell"/> plus <see cref="SiegeTuning.BossFlight"/> later. That
        /// split is invariant 37s — a move's effect may not land before its animation does, and on
        /// a board whose clock never stops the only way to guarantee it is for the schedule to be
        /// a rule the view reads rather than a duration the view invents.
        /// </para>
        /// </summary>
        void Conjure(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var boss = _raiders[i];
                if (!boss.Boss || !boss.Alive || !boss.InPlace) continue;

                boss.Spell -= dt;
                if (boss.Spell > 0f) continue;

                boss.Spell = SiegeTuning.BossCastEvery;

                int ward = Wanted();
                if (ward < 0) continue;

                float lands = SiegeTuning.BossTell + SiegeTuning.BossFlight;

                _spells.Add(new Flight { Raider = boss.Id, Ward = ward, In = lands });
                _report.Casts.Add(new SiegeCast(boss.Id, ward, lands));
            }
        }

        /// <summary>
        /// Which ward a warlord throws at: the standing one with the most health left.
        ///
        /// <para>
        /// <b>The freshest rather than the weakest, and that is what keeps the fight winnable.</b>
        /// A warlord that finished off whatever was nearly down would take the line apart one ward
        /// at a time — and the ward it would reach first is the one whose colour the player has to
        /// feed to answer it, so the mode's own answer would be the thing it destroyed. Picking the
        /// freshest spreads the damage instead: the line comes down evenly, no colour is ever
        /// locked out, and a run that is losing is losing to arithmetic rather than to a trap.
        /// </para>
        /// <para>
        /// It is also what keeps <see cref="Stranded"/> an honest certainty (invariant 28f): the
        /// warlord can never leave a player alive with no way to hurt it.
        /// </para>
        /// </summary>
        int Wanted()
        {
            int best = -1, most = -1;

            for (int w = 0; w < _wards.Length; w++)
            {
                if (!_wards[w].Alive || _wards[w].Health <= most) continue;

                most = _wards[w].Health;
                best = w;
            }

            return best;
        }

        void Swing(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.AtTheLine) continue;

                raider.Blow -= dt;
                if (raider.Blow > 0f) continue;

                raider.Blow = SiegeTuning.BlowEvery;

                int w = Nearest(raider.Lane);
                if (w < 0) continue;

                var ward = _wards[w];
                int damage = SiegeTuning.BlowOf(raider.Kind);
                ward.Health -= damage;

                bool felled = ward.Health <= 0;
                if (felled)
                {
                    ward.Health = 0;
                    ward.Alive = false;
                    ward.Fuel = 0f;
                }

                _report.Blows.Add(new SiegeBlow(w, raider.Id, damage, felled));
            }
        }

        /// <summary>
        /// The standing ward nearest a lane, or -1 when the line is gone.
        ///
        /// A raider whose own ward has fallen walks along the line to the next one rather than
        /// standing in front of a hole, which is what stops a run being decided by which lane a
        /// raider happened to be dealt.
        /// </summary>
        int Nearest(int lane)
        {
            // Lanes and wards are both spread evenly across the same width, so a lane's place on
            // the line is a fraction rather than an index.
            float want = SiegeTuning.Lanes <= 1 ? 0f : lane / (float)(SiegeTuning.Lanes - 1);

            int best = -1;
            float closest = float.MaxValue;

            for (int w = 0; w < _wards.Length; w++)
            {
                if (!_wards[w].Alive) continue;

                float at = _wards.Length <= 1 ? 0f : w / (float)(_wards.Length - 1);
                float gap = Math.Abs(at - want);

                if (gap >= closest) continue;

                closest = gap;
                best = w;
            }

            return best;
        }

        // ------------------------------------------------------------------ utilities
        /// <summary>
        /// Burns everything standing in one box of the hill.
        ///
        /// <para>
        /// <b>A box rather than a radius, and that is a change of kind rather than of degree.</b>
        /// A blast used to take everything within a distance of the point a finger left; exact in
        /// the rule, and on the screen it asked the player to judge a radius against raiders that
        /// were moving. The hill is a grid now (<see cref="SiegeTuning.BlastRows"/> bands by
        /// <see cref="SiegeTuning.Lanes"/> lanes), the boxes are drawn, and a firepot takes
        /// exactly what is standing in the one that was tapped - which is invariant 33g's rule at
        /// its strongest, because the drawn thing and the played thing are now the same integers.
        /// </para>
        /// <para>
        /// <b>It reports damage <em>absorbed</em>, not damage offered</b>, and that number is
        /// what the run is charged for (invariant 39). Overkill on a raider with three health
        /// left is not work the player was spared, so charging for it would price a firepot
        /// above what it saved - safe, but wrong in a way the player would feel.
        /// </para>
        /// <para>
        /// A raider still walking on (<c>Wait &gt; 0</c>) is untouched: it is not on the hill
        /// yet, so it is not drawn there, and burning something the player cannot see is the
        /// class of fault invariant 32c refuses.
        /// </para>
        /// </summary>
        public int Blast(int lane, int row, int damage, List<SiegeStrike> into)
        {
            if (damage <= 0) return 0;
            if (lane < 0 || lane >= SiegeTuning.Lanes) return 0;
            if (row < 0 || row >= SiegeTuning.BlastRows) return 0;

            int absorbed = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (raider.Lane != lane) continue;
                if (SiegeTuning.RowOf(raider.March) != row) continue;

                int took = damage < raider.Health ? damage : raider.Health;
                absorbed += took;

                raider.Health -= took;
                raider.Flash = .18f;

                bool killed = raider.Health <= 0;
                if (killed)
                {
                    raider.Alive = false;
                    _felled++;
                }

                into?.Add(new SiegeStrike(raider.Id, took, killed));
            }

            // Felled raiders are cleared at the top of the next Advance, exactly as a bolt's
            // are, so the view sees them one last time and can play the death it was handed.
            return absorbed;
        }

        /// <summary>
        /// Mends a ward, and answers how much health it actually took.
        ///
        /// <para>
        /// <b>It mends a standing ward and can never raise a fallen one.</b> That is not a
        /// kindness withheld: <see cref="Stranded"/> is a <em>certainty</em> that decides
        /// whether money changes hands (invariant 28f), and it is only allowed to say "no
        /// purchase rescues this" because nothing can put a ward back up. A mending that
        /// revived one would make that claim false and leave <c>ProtoVerdict</c> refusing
        /// continues it should have sold.
        /// </para>
        /// </summary>
        public int Mend(int ward, int health)
        {
            if (ward < 0 || ward >= _wards.Length || health <= 0) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = SiegeTuning.WardHealth - post.Health;
            if (room <= 0) return 0;

            int given = health < room ? health : room;
            post.Health += given;

            return given;
        }

        /// <summary>
        /// Pours fuel into a ward, in tenths, and answers how many tenths it took.
        ///
        /// <para>
        /// <b>Tenths rather than the ward's own float</b>, because what comes back decides a
        /// graded number: <c>SiegeUtility</c> converts it to matches, and a graded number
        /// decided by a float is one three code generators round three ways. The ward's live
        /// fuel stays a float because it is drained by a clock, which is the one quantity here
        /// that genuinely is continuous.
        /// </para>
        /// <para>
        /// Room is <em>floored</em> to whole tenths, so this can never report taking more than
        /// it gave. What refuses a ward too full to be worth it is <c>SiegeUtility</c>, before
        /// an item is spent.
        /// </para>
        /// </summary>
        public int Surge(int ward, int tenths)
        {
            if (ward < 0 || ward >= _wards.Length || tenths <= 0) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = RoomForFuel(ward);
            if (room <= 0) return 0;

            int given = tenths < room ? tenths : room;
            post.Fuel += given / 10f;

            if (post.Fuel > SiegeTuning.WardCapacity) post.Fuel = SiegeTuning.WardCapacity;

            return given;
        }

        /// <summary>
        /// How much more fuel a ward could take, in whole tenths. Nought for a fallen one.
        ///
        /// Floored, so an offer is never made on room that turns out not to be there.
        /// </summary>
        public int RoomForFuel(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            float room = SiegeTuning.WardCapacity - post.Fuel;
            return room <= 0f ? 0 : (int)(room * 10f);
        }

        /// <summary>How much more health a ward could take. Nought for a fallen one.</summary>
        public int RoomForHealth(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = SiegeTuning.WardHealth - post.Health;
            return room < 0 ? 0 : room;
        }

        /// <summary>The raider with this id, or null once it has been taken off the hill.</summary>
        public SiegeRaider Find(int id)
        {
            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Id == id) return _raiders[i];

            return null;
        }
    }
}
