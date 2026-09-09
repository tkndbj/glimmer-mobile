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
    /// and a rung they can buy.</b> The earned rung is priced in credits and gated on a keeper
    /// level — credits are what the game pays out for playing, so a credit price is a reward and
    /// carries a wall money cannot climb (invariant 15a). The bought rung is priced in gems and
    /// asks nothing but the gems, which is invariant 16j's ladder exactly. That gives twenty
    /// turrets a player can tell apart at a glance — ten silhouettes, each in two liveries — where
    /// twenty unrelated ones would be a shelf nobody could hold in their head.
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

        WardCatalog(WardModel[] models)
        {
            _models = models ?? Array.Empty<WardModel>();
            _byId = new Dictionary<string, WardModel>(_models.Length, StringComparer.Ordinal);

            foreach (var model in _models) _byId[model.Id] = model;
        }

        /// <summary>Every turret, in shelf order.</summary>
        public IReadOnlyList<WardModel> Models => _models;

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
        /// <b>The credit ladder is the ramp and the gem ladder is the shortcut.</b> Credit prices
        /// climb 1,200 to 9,000 against a keeper level that climbs with them, so the earned rungs
        /// arrive across a long first run at the game; gem prices climb 600 to 2,000, which is the
        /// same band the grove's gem-priced land sits in (invariant 16j) so the two shelves cost a
        /// player the same kind of decision.
        /// </para>
        /// </summary>
        public static readonly WardCatalog Default = new WardCatalog(new[]
        {
            // ---- the yardstick ------------------------------------------------------------
            // Free, and never a weak choice: every turret in this roster fires the same primary
            // bolt, so this is exactly as good against a lone raider as the dearest one here.
            new WardModel("bolt", WardAbility.None, 0, 0, 0, 0, 0, 1),

            // ---- earned: credits, behind a keeper level ------------------------------------
            new WardModel("spark",   WardAbility.Chain,  5,  1, 0, 1200,  2,  2),
            new WardModel("mortar",  WardAbility.Splash, 5,  1, 0, 1800,  3,  3),
            new WardModel("rime",    WardAbility.Frost,  4, 15, 0, 2400,  4,  4),
            new WardModel("lance",   WardAbility.Pierce, 8,  5, 0, 3200,  5,  5),
            new WardModel("cleaver", WardAbility.Rend,   5,  0, 0, 4000,  6,  6),
            new WardModel("siphon",  WardAbility.Siphon, 4,  0, 0, 5000,  8,  7),
            new WardModel("ember",   WardAbility.Ember,  3, 30, 0, 6000, 10,  8),
            new WardModel("beacon",  WardAbility.Beacon, 5,  0, 0, 7500, 12,  9),
            new WardModel("prism",   WardAbility.Prism,  0,  0, 0, 9000, 14, 10),

            // ---- bought: gems, no gate ------------------------------------------------------
            new WardModel("arcstorm",   WardAbility.Chain,  7,  2,  600, 0, 0, 11),
            new WardModel("howitzer",   WardAbility.Splash, 7,  1,  700, 0, 0, 12),
            new WardModel("glacier",    WardAbility.Frost,  6, 25,  800, 0, 0, 13),
            new WardModel("harpoon",    WardAbility.Pierce,10,  4,  900, 0, 0, 14),
            new WardModel("breaker",    WardAbility.Rend,  10,  0, 1000, 0, 0, 15),
            new WardModel("leech",      WardAbility.Siphon, 8,  0, 1100, 0, 0, 16),
            new WardModel("pyre",       WardAbility.Ember,  6, 40, 1200, 0, 0, 17),
            new WardModel("lighthouse", WardAbility.Beacon,10,  0, 1400, 0, 0, 18),
            new WardModel("spectrum",   WardAbility.Prism,  0,  0, 1600, 0, 0, 19),
            new WardModel("apex",       WardAbility.Chain, 10,  3, 2000, 0, 0, 20),
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

                if (entry.minLevel > 0 && entry.coinPrice <= 0)
                {
                    // A gate on something gems buy is a gate that never fires, and a gate that
                    // never fires is the decoration invariant 5d names.
                    problems.Add($"wards entry '{entry.id}' asks for keeper level " +
                                 $"{entry.minLevel} but is not priced in credits; the level gate " +
                                 "is permission to spend credits and means nothing beside gems");
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

                var model = new WardModel(entry.id, ability, entry.magnitude, entry.extent,
                                          entry.gemPrice, entry.coinPrice, entry.minLevel,
                                          entry.order);

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

            models.Sort((a, b) => a.Order.CompareTo(b.Order));
            return new WardCatalog(models.ToArray());
        }
    }
}
