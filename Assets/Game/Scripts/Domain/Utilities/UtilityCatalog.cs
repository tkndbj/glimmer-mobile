using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Utilities
{
    /// <summary>
    /// Which utilities exist, what they do and what they cost. Immutable once built.
    ///
    /// <para>
    /// <b>Content, not code</b>, for the reason the reward curve and the chest table are: the
    /// price of a consumable and how strong it is are the two numbers a live game retunes most
    /// often, and a build that has to go through two store reviews to move one is a build that
    /// never gets tuned. It rides in <c>progression.json</c> beside the chest table because a
    /// utility arrives out of a chest and is bought with gems, and both of those are tuned in
    /// the sitting this is.
    /// </para>
    /// <para>
    /// <b>Deliberately not its own schema version</b>, exactly as the daily block is not: it is an
    /// optional block, so a client that predates it ignores the field and keeps its built-in
    /// catalog, and a client that has it reads a file written before it existed and does the same
    /// (invariant 9b).
    /// </para>
    /// <para>
    /// <b>What content may not do is invent a kind.</b> A utility's <em>kind</em> is a rule with a
    /// fail state and a grade attached, so an entry naming one this build has never heard of is
    /// skipped whole and reported to nobody — invariant 20's answer for a chapter naming an
    /// unknown mode, one level down. The honest response to content from the future is to lose
    /// that entry rather than to draw a button that cannot do anything.
    /// </para>
    /// </summary>
    public sealed class UtilityCatalog
    {
        /// <summary>
        /// The most this will read from one file. A bar a player can read is four or five slots;
        /// this is generous against that and small enough that every walk here is trivial.
        /// </summary>
        public const int MaxItems = 16;

        readonly UtilityItem[] _items;
        readonly Dictionary<string, UtilityItem> _byId;

        UtilityCatalog(UtilityItem[] items)
        {
            _items = items ?? Array.Empty<UtilityItem>();
            _byId = new Dictionary<string, UtilityItem>(_items.Length, StringComparer.Ordinal);

            foreach (var item in _items) _byId[item.Id] = item;
        }

        /// <summary>Every utility, in the order they are drawn on the bar.</summary>
        public IReadOnlyList<UtilityItem> Items => _items;

        public int Count => _items.Length;

        /// <summary>The utility with this id, or null. The one door every lookup goes through.</summary>
        public UtilityItem Find(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var item) ? item : null;

        public bool Knows(string id) => Find(id) != null;

        /// <summary>
        /// The catalog that ships inside the build.
        ///
        /// <para>
        /// Present so the feature works on a first launch that has not reached the content yet,
        /// and so a malformed file costs a retune rather than a session — the bargain
        /// <c>ProgressionTable.Default</c> and <c>DailyChestTable.Default</c> both make.
        /// </para>
        /// <para>
        /// <b>The shape of it is the design.</b> Three utilities, and each answers a question the
        /// board can put to a player that the other two cannot. The firepot answers "they are
        /// nearly at the line and I have no matching gems"; the mending answers "this ward has one
        /// blow left in it"; the surge answers "the colour I need has not come up". Two of the
        /// three deliver damage and are charged for it in matches (invariant 39); the mending
        /// delivers none and is charged nothing, because it buys a finish rather than a grade,
        /// which is exactly what invariant 23 says a purchase may sell.
        /// </para>
        /// <para>
        /// <b>The prices are a day of free play apart on purpose.</b> Free play collects about six
        /// gems a day, so a firepot at 12 is two days and a mending at 8 is a day and a half —
        /// dear enough that a chest drop is the ordinary way to hold one and cheap enough that the
        /// gem price is a real answer on the evening somebody is stuck. They are content, and
        /// they are the numbers most likely to be wrong first guess.
        /// </para>
        /// <para>
        /// <b>The ceiling is a hundred, and it is a bound on a pack rather than a rationing of
        /// one.</b> It shipped at nine, which read as a ration — a shelf that refuses a tenth is
        /// a shop telling somebody they have bought enough — and nine is also low enough that the
        /// gem price could never be the answer to anything but tonight. What the ceiling is
        /// actually for is keeping a grant bounded and a badge legible; a hundred does both and
        /// asks nothing of the save, since <c>UtilityStock</c>'s structural clamp is 9,999 and
        /// was never this number. Raising it is safe in the direction that matters: a published
        /// ceiling is enforced at the moment of a grant and never by re-reading a file, so
        /// nothing anybody is already holding moves.
        /// </para>
        /// </summary>
        public static readonly UtilityCatalog Default = new UtilityCatalog(new[]
        {
            // Damage, into everything standing in the one box it is thrown at. Two creepers
            // die; a brute is left with four health for a ward to finish.
            new UtilityItem("firepot", UtilityKind.Blast, magnitude: 44,
                            gemPrice: 12, maxHeld: 100, order: 1),

            new UtilityItem("mending", UtilityKind.Mend, magnitude: 6,
                            gemPrice: 8, maxHeld: 100, order: 2),

            // Magnitude is fuel in tenths, so 90 is nine shots — a ward that had run dry firing
            // for about two seconds, which is most of a creeper.
            new UtilityItem("surge", UtilityKind.Surge, magnitude: 90,
                            gemPrice: 10, maxHeld: 100, order: 3),
        });

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>utilities</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in catalog stands,
        /// because a content mistake must fail a build and never a session.
        /// </summary>
        public static UtilityCatalog Resolve(UtilitiesDto dto, List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            if (dto == null) return Default;                    // absent is not an error

            if (dto.items == null || dto.items.Length == 0)
            {
                problems.Add("utilities block lists no items; using the built-in catalog");
                return Default;
            }

            if (dto.items.Length > MaxItems)
            {
                problems.Add($"utilities lists {dto.items.Length} items, more than the supported " +
                             $"{MaxItems}; using the built-in catalog");
                return Default;
            }

            var items = new List<UtilityItem>(dto.items.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();

            foreach (var entry in dto.items)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id))
                {
                    problems.Add("utilities entry has no id; an id is what the save keys on");
                    return Default;
                }

                if (!seen.Add(entry.id))
                {
                    problems.Add($"utilities names '{entry.id}' twice; an id is permanent and " +
                                 "two entries under one would be two things in one save row");
                    return Default;
                }

                var kind = UtilityKinds.Parse(entry.kind);
                if (kind == UtilityKind.None)
                {
                    // Skipped rather than fatal, which is `DailyChestTable`'s rule for an unknown
                    // reward kind: content naming a kind this build has never heard of is content
                    // from the future, and degrading the bar is better than losing the catalog.
                    problems.Add($"utilities entry '{entry.id}' names unknown kind " +
                                 $"'{entry.kind}'; skipped");
                    continue;
                }

                if (entry.magnitude < 1)
                {
                    problems.Add($"utilities entry '{entry.id}' has magnitude {entry.magnitude}; " +
                                 "a utility that does nothing is a slot on the bar that spends " +
                                 "one and gives nothing back");
                    return Default;
                }

                if (entry.gemPrice < 0)
                {
                    problems.Add($"utilities entry '{entry.id}' has gem price {entry.gemPrice}; " +
                                 "nought means chest-only and a negative price means nothing");
                    return Default;
                }

                if (entry.maxHeld < 1)
                {
                    problems.Add($"utilities entry '{entry.id}' may be held {entry.maxHeld} " +
                                 "times, so a grant could never land");
                    return Default;
                }

                if (entry.order < 1 || !orders.Add(entry.order))
                {
                    // The ladder is authored and never derived, which is `HomesteadRegion`'s rule
                    // (invariant 16j): an unauthored or duplicated rung reorders the bar under a
                    // player between one content push and the next.
                    problems.Add($"utilities entry '{entry.id}' has order {entry.order}; every " +
                                 "entry needs its own order from 1 up, or the bar reshuffles " +
                                 "itself on a retune");
                    return Default;
                }

                items.Add(new UtilityItem(entry.id, kind, entry.magnitude,
                                          entry.gemPrice, entry.maxHeld, entry.order));
            }

            if (items.Count == 0)
            {
                problems.Add("utilities block resolved to nothing; using the built-in catalog");
                return Default;
            }

            items.Sort((a, b) => a.Order.CompareTo(b.Order));
            return new UtilityCatalog(items.ToArray());
        }
    }
}
