using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which turrets exist, what each of them does and what it costs. Immutable once built.
    ///
    /// <para>
    /// <b>Content, not code</b>, for the reason the reward curve, the chest table and the utility
    /// roster are: the price of a turret and how strong its trick is are the two numbers a live
    /// game retunes most, and a build that has to go through two store reviews to move one is a
    /// build that never gets tuned. What content may <em>not</em> do is invent an
    /// <see cref="WardAbility"/> — an ability is a rule the board runs, so an entry naming one
    /// this build has never heard of still stands and simply fires a plain bolt (invariant 20's
    /// answer, one level down).
    /// </para>
    /// <para>
    /// <b>The shape of the roster is the design: ten abilities, each with a rung a player earns
    /// and a rung they can buy.</b> The earned rung is priced in credits and the bought rung in
    /// gems, which is invariant 16j's two-currency ladder exactly. That gives twenty turrets a
    /// player can tell apart at a glance — ten silhouettes, each in two liveries — where twenty
    /// unrelated ones would be a shelf nobody could hold in their head.
    /// </para>
    /// <para>
    /// <b>A keeper level is the whole of what opens a rung, and the sequential unlock is
    /// gone.</b> It was there: a turret was sealed until the one before it on the shelf was held,
    /// on the argument that a wall of twenty prices is not a next step. What that made, once the
    /// walls were real, was two conditions saying nearly the same thing — a player at keeper
    /// level twenty-two who had skipped one credit turret could not buy the gem turret they had
    /// earned, and the shelf's answer named a turret they did not want. <b>The owner's
    /// decision</b>: reach the level and the rung is open, in whatever order a player likes.
    /// </para>
    /// <para>
    /// <b>So the three bands are the ladder now.</b> What used to be forced one rung at a time is
    /// three stretches of keeper level a player climbs through — under twenty, twenty to thirty,
    /// thirty to forty (<see cref="WardTier"/>) — and every priced turret carries a wall inside
    /// its own band, gem half included. That is what <see cref="LadderProblem"/> holds the roster
    /// to: the walls may not fall as the shelf climbs, and none of them may stand outside the
    /// band whose header a player is reading it under.
    /// </para>
    /// <para>
    /// <b>And a turret is bought for one colour of the line rather than for the line.</b> See
    /// <see cref="WardHolding"/>: the ladder above is therefore climbed four times, once per seat,
    /// because a line holds four turrets and which colour a trick is worth having on is the whole
    /// of what makes the shelf a choice (invariant 26h).
    /// </para>
    /// <para>
    /// <b>No entry may make a bolt weaker, and there is no field that could.</b> A siege's par is
    /// the hill's health over the baseline bolt, so a turret that hit softer would put three stars
    /// out of reach of whoever chose it — a grade decided by a purchase, which is what invariant
    /// 39 refuses a utility. Every ability is an <em>addition</em>; see <see cref="WardAbility"/>.
    /// </para>
    /// </summary>
    public sealed class WardCatalog
    {
        /// <summary>
        /// The most this will read from one file.
        ///
        /// The loadout screen browses these in a grid off a single atlas
        /// (<c>AssetLibrary.AtlasSprite</c>, invariant 16c), so the bound that matters is the
        /// atlas rather than the walk.
        ///
        /// <para>
        /// <b>Forty, and the number is a rules bound rather than a taste.</b> It was thirty-two
        /// against a roster of twenty; the legendary band makes it thirty, and forty leaves the
        /// same headroom. What it may not exceed is <c>firestore.rules</c>' bound on
        /// <c>wardsOwned</c> divided by four - a turret is bought per colour, so the worst case a
        /// client can write is this times <see cref="WardLine.Colours"/>, which is exactly the
        /// pair <c>CloudWireTests</c> holds together offline (invariant 12b). Forty times four is
        /// a hundred and sixty, which is the bound the rules carry today, so raising this again
        /// is a rules release <em>before</em> a client one.
        /// </para>
        /// </summary>
        public const int MaxModels = 40;

        readonly WardModel[] _models;
        readonly Dictionary<string, WardModel> _byId;

        /// <summary>
        /// Holds the roster in <b>shelf order</b>, which is the catalog's own job rather than the
        /// caller's.
        ///
        /// <para>
        /// <b>Sorted here rather than at the one call site that reads a file.</b> It used to be,
        /// and that left <see cref="Default"/> - which is an array written by hand - showing
        /// whatever sequence it happened to be typed in, while a roster read from content showed
        /// the authored rungs. The two agreed only for as long as nobody re-rung the shelf without
        /// also re-typing the fallback, which is exactly the drift invariant 5b is about: one rule,
        /// written twice, correct until the day the two are asked a question that separates them.
        /// </para>
        /// <para>
        /// <b>Ties break on the id</b>, because <c>Array.Sort</c> is not stable and two runtimes
        /// are not obliged to settle an equal pair the same way - <c>SiegeBoard</c>'s own rule
        /// about a <c>HashSet</c> walk. A tie is refused by both content gates anyway; this is
        /// what happens while a bad file is still being loaded.
        /// </para>
        /// </summary>
        WardCatalog(WardModel[] models)
        {
            _models = models ?? Array.Empty<WardModel>();

            Array.Sort(_models, (a, b) =>
            {
                int by = a.Order.CompareTo(b.Order);
                return by != 0 ? by : string.CompareOrdinal(a.Id, b.Id);
            });

            _byId = new Dictionary<string, WardModel>(_models.Length, StringComparer.Ordinal);

            MostPowerTenths = WardModel.Baseline;
            MostGuardTenths = WardModel.Baseline;

            foreach (var model in _models)
            {
                _byId[model.Id] = model;

                if (model.PowerTenths > MostPowerTenths) MostPowerTenths = model.PowerTenths;
                if (model.GuardTenths > MostGuardTenths) MostGuardTenths = model.GuardTenths;
            }
        }

        /// <summary>Every turret, in shelf order.</summary>
        public IReadOnlyList<WardModel> Models => _models;

        /// <summary>
        /// The heaviest bolt and the toughest chassis on the shelf, in tenths.
        ///
        /// <b>What a stat bar is measured against, so it stays honest as the roster grows.</b> A
        /// typed ceiling would leave the day's hardest-hitting turret pinned at full and every bar
        /// below it wrong the moment a drop added a harder one; read off the roster, the whole
        /// shelf re-scales by itself. Computed once when the catalog is built, because a catalog
        /// is immutable.
        /// </summary>
        public int MostPowerTenths { get; private set; }

        /// <summary>See <see cref="MostPowerTenths"/>.</summary>
        public int MostGuardTenths { get; private set; }

        public int Count => _models.Length;

        /// <summary>The turret with this id, or null. The one door every lookup goes through.</summary>
        public WardModel Find(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var model) ? model : null;

        public bool Knows(string id) => Find(id) != null;

        /// <summary>
        /// The turret every player starts with, and the one a slot falls back to.
        ///
        /// <para>
        /// <b>The first starter in shelf order, derived rather than named.</b> A named default
        /// would be a second place the roster says which one is free, and the two would drift the
        /// first time a drop reordered the shelf — <c>AvatarCatalog.Starter</c>'s rule.
        /// </para>
        /// <para>
        /// It is never null while the catalog holds anything: a roster with no starter at all is
        /// refused at read time, because a player who owns nothing would have an empty line and a
        /// siege with no line cannot be played.
        /// </para>
        /// </summary>
        public WardModel Starter
        {
            get
            {
                for (int i = 0; i < _models.Length; i++)
                    if (_models[i].IsStarter) return _models[i];

                return _models.Length > 0 ? _models[0] : null;
            }
        }

        /// <summary>Whether this id names a turret handed over rather than bought.</summary>
        public bool IsStarter(string id)
        {
            var model = Find(id);
            return model != null && model.IsStarter;
        }

        // ------------------------------------------------------------- the built-in roster
        /// <summary>
        /// The roster that ships inside the build.
        ///
        /// <para>
        /// Present so the line works on a first launch that has not reached the content yet, and
        /// so a malformed file costs a retune rather than a session — the bargain
        /// <c>ProgressionTable.Default</c>, <c>DailyChestTable.Default</c> and
        /// <c>UtilityCatalog.Default</c> all make.
        /// </para>
        /// <para>
        /// <b>The credit ladder is the ramp and the gem ladder is what is above it.</b> Credit
        /// prices climb 1,200 to 9,000 against keeper levels 2 to 18, two levels a rung; gem
        /// prices climb 600 to 2,000 — the same band the grove's gem-priced land sits in
        /// (invariant 16j) — against levels 20 to 40. Gems still buy a rung far sooner than
        /// credits could, which is what makes them a shortcut; what they no longer do is skip a
        /// wall.
        /// </para>
        /// <para>
        /// <b>The honest cost of the owner's re-banding, said out loud: today's three chapters
        /// pay for about keeper level nine, so tiers two and three are shut to every player
        /// alive.</b> That is the same state the home ladder is in and it is deliberate — a shelf
        /// whose top is reachable on the content that exists is a shelf with nothing left in it
        /// the week after. Every number here is <em>content</em>, so it is a config push to
        /// retune rather than a store review; see <c>progression.json</c>.
        /// </para>
        /// <para>
        /// <b>And what decides the rung is how much of the hill an ability can reach, not how
        /// big its number is.</b> The shelf shipped the other way up - a chain, a lance and a
        /// mortar were the three cheapest turrets in the game - and the owner's verdict was the
        /// obvious one: the cheap half was stronger than the dear half, because reaching two or
        /// three raiders a bolt beats any amount of extra help aimed at one. So the order is fuel
        /// back, fuel banked, one raider burnt, one raider slowed, armour, a second colour, a
        /// lane, a box, whatever is nearest - single target first, multi-target last, in both
        /// halves of the shelf. <b>The two halves must agree about it</b>, which they also did not:
        /// frost was the cheapest thing gems could buy and the fourth thing credits could, so the
        /// shelf said two different things about the same ability depending on which currency you
        /// read it in.
        /// </para>
        /// <para>
        /// <b>Nothing about a turret moved except its rung.</b> An id is permanent (invariant 1)
        /// and everything a player sees is derived from it - the name, the note, the picture and
        /// the bolt it throws (<see cref="WardModel"/>) - so re-rungeing the shelf is a change to
        /// <em>order</em>, <em>price</em> and <em>keeper level</em> and to nothing else. The hull
        /// follows, because <c>Tools/make_siege_art.py</c>'s own rule is that the hull <em>is</em>
        /// the shelf rung: re-rung the shelf, re-cut that list in the same order and the ladder
        /// still climbs visibly from a single barrel to a four-barrel mount.
        /// </para>
        /// <para>
        /// <b>The shelf is one ladder read top to bottom: free, then the nine earned, then the ten
        /// bought</b> - and within each of those the price only ever climbs. It is authored
        /// (<c>WardModel.Order</c>) rather than derived, for invariant 16j's reason: 600 gems and
        /// 5,000 credits do not compare, so there is no arithmetic that could put these twenty in
        /// one sequence. What an author owes in exchange is that the rung, the price and the gate
        /// move <em>together</em> - a shelf whose prices go up, down and up again is one a player
        /// reads as arbitrary, and the fix is never to sort it at run time (that reshuffles the
        /// shelf under somebody part-way up it) but to re-rung the file.
        /// </para>
        /// </summary>
        public static readonly WardCatalog Default = new WardCatalog(new[]
        {
            // ---- the yardstick ------------------------------------------------------------
            // Free, and never a weak choice: every turret in this roster fires the same primary
            // bolt, so this is exactly as good against a lone raider as the dearest one here.
            new WardModel("bolt",       WardAbility.None,   0,  0,    0,    0,  0,  1, 10, 10),
            // ---- earned: credits, behind a keeper level ------------------------------------
            // Weakest ability first, which is the ladder's whole point. A rung buys a *kind* of
            // help rather than a bigger number, so they are sorted by how much of the hill each
            // kind can reach: fuel back, then fuel banked, then one raider hurt harder, then one
            // raider slowed, then armour, then one raider stopped, then a lane, then a box, then
            // whatever is nearest.
            //
            // **A stun hits one raider and stands above armour anyway**, which is the one rung
            // here whose place is an argument rather than a count: what it reaches is the *raid*
            // rather than the raider. A slowed brute is still walking, swinging and casting at a
            // rate; a stunned one has been taken out of all three for the length of it, and every
            // other turret on the line collects those seconds as well.
            //
            // **Two keeper levels a rung, so tier one spans 2 to 18 and stops under twenty.**
            // Nothing here is sealed behind anything: a player at level ten holds the wall to
            // five of these and buys whichever of the five is worth having on the colour they are
            // filling, which is the whole of what the shelf is for (invariant 26h).
            new WardModel("siphon",     WardAbility.Siphon, 4,  0,    0, 1200,  2,  2, 11, 13),
            new WardModel("beacon",     WardAbility.Beacon, 5,  0,    0, 1800,  4,  3, 10, 14),
            new WardModel("ember",      WardAbility.Ember,  3, 30,    0, 2400,  6,  4, 13,  8),
            new WardModel("rime",       WardAbility.Frost,  4, 15,    0, 3200,  8,  5, 11, 11),
            new WardModel("cleaver",    WardAbility.Rend,   5,  0,    0, 4000, 10,  6, 14,  8),
            new WardModel("prism",      WardAbility.Stun,   0,  5,    0, 5000, 12,  7, 12,  9),
            new WardModel("lance",      WardAbility.Pierce, 8,  5,    0, 6000, 14,  8, 10, 12),
            new WardModel("mortar",     WardAbility.Splash, 4,  1,    0, 7500, 16,  9, 10, 11),
            new WardModel("spark",      WardAbility.Beacon,10,  0,    0, 9000, 18, 10, 11, 15),
            // ---- bought: gems, and behind a keeper level of their own ----------------------
            // The same nine abilities in the same order, each a rung stronger, plus a third rung
            // of the strongest one at the top of the shelf.
            //
            // **Tier two spans 20 to 29 and tier three 30 to 40**, which is the owner's shape and
            // the reason the bands stopped being punctuation: with no seal in front of them these
            // walls are the only thing holding the gem half above the credit half, so the header
            // a player reads and the level it asks for have to be one fact (`WardTier.Gates`).
            new WardModel("leech",      WardAbility.Siphon, 8,  0,  600,    0, 20, 11, 12, 14),
            new WardModel("lighthouse", WardAbility.Chain,  6,  2,  700,    0, 22, 12, 10,  9),
            new WardModel("pyre",       WardAbility.Ember,  6, 40,  800,    0, 23, 13, 15,  8),
            new WardModel("glacier",    WardAbility.Frost,  6, 25,  900,    0, 25, 14, 12, 12),
            new WardModel("breaker",    WardAbility.Rend,  10,  0, 1000,    0, 26, 15, 16,  8),
            new WardModel("spectrum",   WardAbility.Stun,   0, 10, 1100,    0, 28, 16, 13, 10),
            new WardModel("harpoon",    WardAbility.Pierce,10,  4, 1200,    0, 29, 17, 11, 13),
            new WardModel("howitzer",   WardAbility.Splash, 7,  1, 1400,    0, 30, 18, 11, 12),
            new WardModel("arcstorm",   WardAbility.Chain,  8,  3, 1600,    0, 35, 19, 10, 10),
            new WardModel("apex",       WardAbility.Chain, 10,  3, 2000,    0, 40, 20, 11, 10),
            // ---- legendary: colourless, and the only turrets that break the lock ------------
            // **What a legendary buys is *reach across the board* rather than a bigger number.**
            // Every other turret on this shelf fires at one colour and is bought for one seat; a
            // legendary wears none, stands on any seat and answers anything on the hill
            // (`WardModel.Legendary`). That is the largest thing this mode has ever sold, which
            // is why the band sits at keeper forty-five and above and is priced in **gems** - the
            // shipped shelf is priced entirely in credits, so this is also what fills the gem
            // hole the all-credit re-pricing left.
            //
            // **It is still an addition, and that is not an argument but an arithmetic.** Par is
            // the hill's health over a perfect match computed against the baseline bolt, so a
            // turret reaching every colour only ever fires bolts that would otherwise not have
            // been fired: a run ends sooner and par over-states what a good one needs, which is
            // the direction invariant 22 says to err in. **No star line moves.**
            //
            // **The order within the band is the shelf's own rule**, cheapest first and reach
            // last: a chain, a bouncing chain, fire, cold, armour, a stop, a lane, a box, fuel,
            // and then the widest chain in the game.
            new WardModel("tempest",    WardAbility.Chain, 12,  4, 1800,    0, 45, 21, 14, 14, true),
            new WardModel("ricochet",   WardAbility.Chain, 14,  5, 2100,    0, 46, 22, 15, 13, true),
            new WardModel("pyroclast",  WardAbility.Ember, 12, 60, 2400,    0, 48, 23, 17, 12, true),
            new WardModel("permafrost", WardAbility.Frost,  8, 40, 2800,    0, 50, 24, 15, 16, true),
            new WardModel("sunderer",   WardAbility.Rend,  16,  0, 3200,    0, 52, 25, 20, 11, true),
            new WardModel("stasis",     WardAbility.Stun,   0, 14, 3600,    0, 53, 26, 16, 15, true),
            new WardModel("railgun",    WardAbility.Pierce,16,  3, 4000,    0, 55, 27, 18, 12, true),
            new WardModel("starfall",   WardAbility.Splash,12,  2, 4500,    0, 56, 28, 17, 13, true),
            new WardModel("wellspring", WardAbility.Siphon,14,  0, 5000,    0, 58, 29, 16, 18, true),
            new WardModel("eclipse",    WardAbility.Chain, 16,  6, 6000,    0, 60, 30, 22, 16, true),
        });

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>wards</c> block. Never throws and never returns null: anything
        /// wrong is named in <paramref name="problems"/> and the built-in roster stands, because a
        /// content mistake must fail a build and never a session.
        /// </summary>
        public static WardCatalog Resolve(WardsDto dto, List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            if (dto.models == null || dto.models.Length == 0)
            {
                problems.Add("wards block lists no models; using the built-in roster");
                return Default;
            }

            if (dto.models.Length > MaxModels)
            {
                problems.Add($"wards lists {dto.models.Length} models, more than the supported " +
                             $"{MaxModels}; using the built-in roster");
                return Default;
            }

            var models = new List<WardModel>(dto.models.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();
            bool starter = false;

            foreach (var entry in dto.models)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id))
                {
                    problems.Add("wards entry has no id; an id is what the save keys on");
                    return Default;
                }

                if (!seen.Add(entry.id))
                {
                    problems.Add($"wards names '{entry.id}' twice; an id is permanent and two " +
                                 "entries under one would be two turrets in one save row");
                    return Default;
                }

                if (entry.gemPrice < 0 || entry.coinPrice < 0)
                {
                    problems.Add($"wards entry '{entry.id}' has a negative price; nought is how " +
                                 "a turret says it cannot be bought that way");
                    return Default;
                }

                if (entry.gemPrice > 0 && entry.coinPrice > 0)
                {
                    // One price or the other, which is `HomesteadRegion`'s rule (invariant 16j):
                    // two prices for one thing is two answers to "what does this cost", and the
                    // shelf can only draw one of them.
                    problems.Add($"wards entry '{entry.id}' is priced in both gems and credits; " +
                                 "a turret carries one price or the other");
                    return Default;
                }

                if (entry.minLevel <= 0 && (entry.gemPrice > 0 || entry.coinPrice > 0))
                {
                    // **Every priced turret carries a keeper level, gems included**, which is the
                    // owner's rule and the reverse of what shipped: a gate used to belong to a
                    // credit price alone, on the argument that gems are the shortcut and a
                    // shortcut asks nothing. What that made was a shelf where the dearest half
                    // could be taken in any order by anybody holding gems, which is the ladder
                    // below not being a ladder at all.
                    problems.Add($"wards entry '{entry.id}' is priced but asks for no keeper " +
                                 "level; every turret on the shelf is behind one, so nought is " +
                                 "no longer how an entry says it is ungated");
                    return Default;
                }

                if (!WardHolding.Spellable(entry.id))
                {
                    // A holding is `{id}:{colour}` (see `WardHolding`), so an id carrying the
                    // mark would make every row about it ambiguous — and a save is not the place
                    // to find that out.
                    problems.Add($"wards entry '{entry.id}' contains '{WardHolding.Mark}', which " +
                                 "separates a turret from the colour it was bought for");
                    return Default;
                }

                if (entry.order < 1 || !orders.Add(entry.order))
                {
                    problems.Add($"wards entry '{entry.id}' has order {entry.order}; every entry " +
                                 "needs its own order from 1 up, or the shelf reshuffles itself " +
                                 "under a player on a retune");
                    return Default;
                }

                var ability = WardAbilities.Parse(entry.ability);

                // Skipped rather than fatal for an ability from the future, which is
                // `UtilityCatalog`'s rule: it still stands and still fires a plain bolt, so a
                // player on an older build gets a turret that works rather than an empty slot.
                if (ability == WardAbility.None && !string.IsNullOrEmpty(entry.ability)
                    && !WardAbilities.Known(entry.ability))
                    problems.Add($"wards entry '{entry.id}' names unknown ability " +
                                 $"'{entry.ability}'; it fires a plain bolt");

                // **Refused rather than clamped**, because a softer bolt is not a mistake the
                // reader can repair: par is computed against the baseline, so an entry asking to
                // go under it is asking for something this mode cannot give without moving every
                // level's star lines (see `WardModel.PowerTenths`).
                if (entry.power > 0 && entry.power < WardModel.Baseline)
                {
                    problems.Add($"wards entry '{entry.id}' asks for power {entry.power}, under " +
                                 $"the baseline {WardModel.Baseline}; a turret that hits softer " +
                                 "than the free one pushes three stars out of reach of whoever " +
                                 "bought it");
                    return Default;
                }

                if (entry.guard > 0 && entry.guard < Modes.SiegeTuning.LeastGuardTenths)
                {
                    problems.Add($"wards entry '{entry.id}' asks for guard {entry.guard}, under " +
                                 $"the floor {Modes.SiegeTuning.LeastGuardTenths}; a turret may " +
                                 "be a fragile choice and may not be an impossible one");
                    return Default;
                }

                if (entry.legendary && entry.gemPrice <= 0 && entry.coinPrice <= 0)
                {
                    // A free turret that ignored the colour lock would be handed to every player
                    // on every seat at the first launch, which is the mode's central rule given
                    // away rather than sold (invariant 5d, asked of the thing a shelf exists for).
                    problems.Add($"wards entry '{entry.id}' is legendary and free; a turret that " +
                                 "wears no colour and answers the whole hill is the dearest thing " +
                                 "on this shelf and may not be the starter");
                    return Default;
                }

                var model = new WardModel(entry.id, ability, entry.magnitude, entry.extent,
                                          entry.gemPrice, entry.coinPrice, entry.minLevel,
                                          entry.order, entry.power, entry.guard, entry.legendary);

                starter |= model.IsStarter;
                models.Add(model);
            }

            if (!starter)
            {
                // A player who owns nothing would have no line at all, and a siege with no line
                // cannot be played. This is the one clause here that is about the run rather than
                // about the file.
                problems.Add("wards lists no free turret; a player who has bought nothing would " +
                             "stand an empty line");
                return Default;
            }

            // Shelf order is the constructor's, so a hand-written fallback and a roster read from
            // a file cannot come out in two different sequences.
            var catalog = new WardCatalog(models.ToArray());

            // Asked of the *sorted* roster rather than of the file's order, because what it is
            // about is the shelf a player reads top to bottom.
            string climb = catalog.LadderProblem();

            if (climb != null)
            {
                problems.Add(climb);
                return Default;
            }

            return catalog;
        }

        // ------------------------------------------------------------- the ladder
        /// <summary>
        /// What is wrong with the shelf read as one ladder, or null.
        ///
        /// <para>
        /// <b>Two rules, and both of them moved when the sequential unlock went.</b> A turret used
        /// to be sealed until the rung below it was held, so reaching rung <em>n</em> meant having
        /// met every wall under it — which is what made a wall that did not <em>strictly</em>
        /// climb a wall that could never refuse anybody (invariant 5d). That argument is gone with
        /// the seal: a level of twenty refuses everybody under twenty whatever stands beside it,
        /// so two rungs may now share a wall and this asks only that none of them <em>falls</em>.
        /// </para>
        /// <para>
        /// <b>A fall is still refused, and for a plainer reason than 5d.</b> The shelf is ordered
        /// by how much of the hill an ability reaches and its prices climb with it (invariant
        /// 37ax), so a wall that drops as the shelf rises opens a dearer, further-reaching turret
        /// <em>earlier</em> than a cheaper one under it — which a player reads as arbitrary, and
        /// which is very nearly always a typed digit rather than a decision.
        /// </para>
        /// <para>
        /// <b>And there is a third now: the legendary flag and the legendary band are one
        /// fact.</b> <see cref="WardModel.Legendary"/> is authored rather than derived from the
        /// rung, for <see cref="WardTier"/>'s own reason — a band is punctuation and may not
        /// decide what a turret does — so something has to hold the two together, and this is it.
        /// </para>
        /// <para>
        /// <b>And every wall has to stand inside its own band, which is the rule the removal
        /// left uncovered.</b> With nothing forcing the order, the three headers are all a player
        /// has to go on — TIER II now means "this stretch of the shelf opens between keeper level
        /// twenty and twenty-nine" and nothing else says so. A rung authored outside its band
        /// parses, prices, validates and plays; what it does is put a lie in a header
        /// (<see cref="WardTier.OpensAtLevel"/>).
        /// </para>
        /// </summary>
        public string LadderProblem()
        {
            int highest = 0;
            string below = null;

            for (int i = 0; i < _models.Length; i++)
            {
                var model = _models[i];
                if (model.IsStarter) continue;

                if (model.MinLevel < highest)
                    return $"turret '{model.Id}' asks for keeper level {model.MinLevel}, under " +
                           $"the {highest} that '{below}' below it on the shelf asks for; the " +
                           "shelf climbs by reach and by price, so a wall that falls opens the " +
                           "dearer turret first";

                int band = WardTier.Of(model);
                int opens = WardTier.OpensAtLevel(band);
                int closes = WardTier.ClosesAtLevel(band);

                if (model.MinLevel < opens || model.MinLevel > closes)
                    return $"turret '{model.Id}' stands in band {band}, which opens between " +
                           $"keeper level {opens} and {closes}, and asks for {model.MinLevel}; " +
                           "a band is the only thing saying when a stretch of the shelf opens " +
                           "now that no rung is sealed behind another";

                // **The legendary flag and the legendary band have to be one fact**, which is
                // what makes it safe for `WardModel.Legendary` to be authored rather than read
                // off the rung. A turret that ignored the colour lock under a TIER II header
                // would be the mode's central rule quietly suspended where nothing says so; a
                // turret in the legendary band that still obeyed it would be the header lying
                // the other way. Refused in both directions, by both content gates.
                if (model.Legendary != (band == WardTier.Count))
                    return model.Legendary
                         ? $"turret '{model.Id}' is legendary and stands in band {band}; a turret " +
                           $"that wears no colour belongs under the band {WardTier.Count} header " +
                           "and nowhere else, because that header is the only thing telling a " +
                           "player the colour lock is off"
                         : $"turret '{model.Id}' stands in band {WardTier.Count} and is not " +
                           "legendary; every rung under that header wears no colour, so one that " +
                           "does is a turret a player cannot tell from the four beside it";

                highest = model.MinLevel;
                below = model.Id;
            }

            return null;
        }
    }
}
