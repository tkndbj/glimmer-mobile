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
    /// <b>It is one ladder climbed one rung at a time, and both walls apply to every rung.</b> A
    /// turret is sealed until the one before it on the shelf is held (<see cref="Before"/>), and
    /// every priced turret carries a keeper level — <em>including</em> the gem half, which is the
    /// reverse of what shipped and the owner's decision. The two rules need each other: a shelf
    /// where money alone could take the dearest rung first is not a ladder, and a ladder whose
    /// gates do not climb is a ladder with no gates on it (<see cref="LadderProblem"/>).
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
        /// atlas rather than the walk. Thirty-two is generous against twenty and small enough that
        /// every walk here is trivial.
        /// </summary>
        public const int MaxModels = 32;

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
        /// prices climb 1,200 to 9,000 against keeper levels 2 to 14; gem prices climb 600 to
        /// 2,000 — the same band the grove's gem-priced land sits in (invariant 16j) — against
        /// levels 15 to 24. Gems still buy a rung far sooner than credits could, which is what
        /// makes them a shortcut; what they no longer do is skip the rungs below.
        /// </para>
        /// <para>
        /// <b>The honest cost of that, said out loud: today's one chapter pays about 1,650 XP,
        /// which is roughly keeper level seven.</b> So most of this shelf is currently gated by
        /// how much content exists rather than by the wall, and the top of it waits on chapters
        /// that have not shipped. Every number here is <em>content</em>, so that is a config push
        /// to retune rather than a store review — see <c>progression.json</c>.
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
            // raider slowed, then armour, then a second colour, then a lane, then a box, then
            // whatever is nearest.
            new WardModel("siphon",     WardAbility.Siphon, 4,  0,    0, 1200,  2,  2, 11, 13),
            new WardModel("beacon",     WardAbility.Beacon, 5,  0,    0, 1800,  3,  3, 10, 14),
            new WardModel("ember",      WardAbility.Ember,  3, 30,    0, 2400,  4,  4, 13,  8),
            new WardModel("rime",       WardAbility.Frost,  4, 15,    0, 3200,  5,  5, 11, 11),
            new WardModel("cleaver",    WardAbility.Rend,   5,  0,    0, 4000,  6,  6, 14,  8),
            new WardModel("prism",      WardAbility.Prism,  6,  0,    0, 5000,  8,  7, 12,  9),
            new WardModel("lance",      WardAbility.Pierce, 8,  5,    0, 6000, 10,  8, 10, 12),
            new WardModel("mortar",     WardAbility.Splash, 5,  1,    0, 7500, 12,  9, 10, 11),
            new WardModel("spark",      WardAbility.Beacon,10,  0,    0, 9000, 14, 10, 11, 15),
            // ---- bought: gems, and behind a keeper level of their own ----------------------
            // The same nine abilities in the same order, each a rung stronger, plus a third rung
            // of the strongest one at the top of the shelf. The gate climbs past the earned
            // ladder's top because these rungs sit above it on one shelf, and a rung that asked
            // for a level an earlier rung had already demanded could never refuse anybody (5d).
            new WardModel("leech",      WardAbility.Siphon, 8,  0,  600,    0, 15, 11, 12, 14),
            new WardModel("lighthouse", WardAbility.Chain,  6,  2,  700,    0, 16, 12, 10,  9),
            new WardModel("pyre",       WardAbility.Ember,  6, 40,  800,    0, 17, 13, 15,  8),
            new WardModel("glacier",    WardAbility.Frost,  6, 25,  900,    0, 18, 14, 12, 12),
            new WardModel("breaker",    WardAbility.Rend,  10,  0, 1000,    0, 19, 15, 16,  8),
            new WardModel("spectrum",   WardAbility.Prism, 10,  0, 1100,    0, 20, 16, 13, 10),
            new WardModel("harpoon",    WardAbility.Pierce,10,  4, 1200,    0, 21, 17, 11, 13),
            new WardModel("howitzer",   WardAbility.Splash, 7,  1, 1400,    0, 22, 18, 11, 12),
            new WardModel("arcstorm",   WardAbility.Chain,  8,  3, 1600,    0, 23, 19, 10, 10),
            new WardModel("apex",       WardAbility.Chain, 10,  3, 2000,    0, 24, 20, 11, 10),
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

                var model = new WardModel(entry.id, ability, entry.magnitude, entry.extent,
                                          entry.gemPrice, entry.coinPrice, entry.minLevel,
                                          entry.order, entry.power, entry.guard);

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
        /// The turret immediately before this one on the shelf that somebody has to buy, or null
        /// for the first rung.
        ///
        /// <para>
        /// <b>This is the sequential unlock, and it is one rung rather than a set.</b> A turret is
        /// sealed until the one before it is held, and that one until the one before <em>it</em> —
        /// so the whole prefix follows by induction and the shelf never has to be walked. It is
        /// <c>HomesteadRegion</c>'s ladder exactly (invariant 16j): ground is offered one rung at
        /// a time because a wall of nine prices asks a player to compare things they cannot
        /// picture, where one offer at a time is a next step.
        /// </para>
        /// <para>
        /// <b>Free turrets are skipped, because a rung nobody buys can never be climbed.</b> A
        /// starter is held from the first launch, so it is not a rung; anything else with no price
        /// would seal the whole shelf behind something that can never be bought, which is why both
        /// content gates refuse a priced-at-nought entry that is not free outright.
        /// </para>
        /// </summary>
        public WardModel Before(WardModel model)
        {
            if (model == null) return null;

            WardModel previous = null;

            for (int i = 0; i < _models.Length; i++)
            {
                var other = _models[i];

                if (other.Order >= model.Order) break;
                if (other.IsStarter) continue;

                previous = other;
            }

            return previous;
        }

        /// <summary>
        /// What is wrong with the shelf read as one ladder, or null.
        ///
        /// <para>
        /// <b>The keeper level has to climb, and that is forced rather than chosen.</b> A turret
        /// is sealed until the rung before it is held, so reaching rung <em>n</em> means having
        /// met every gate below it — and a rung asking for a level one below it already demanded
        /// could therefore never refuse anybody. That is the decoration invariant 5d names,
        /// arriving through a door that was shut for as long as only the credit half carried a
        /// gate at all.
        /// </para>
        /// <para>
        /// <b>Ties are refused for the same reason as a fall.</b> Two rungs at one level is the
        /// second of them gating nothing, which is a wall drawn on a shelf that is not there.
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

                if (model.MinLevel <= highest)
                    return $"turret '{model.Id}' asks for keeper level {model.MinLevel}, which " +
                           $"'{below}' below it on the shelf already asked for; a rung is sealed " +
                           "until the one before it is bought, so a gate that does not climb can " +
                           "never refuse anybody";

                highest = model.MinLevel;
                below = model.Id;
            }

            return null;
        }
    }
}
