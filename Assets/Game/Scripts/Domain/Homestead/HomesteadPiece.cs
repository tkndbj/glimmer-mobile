using System;
using GlimmerGrove.Content;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// What sort of thing a piece is, which decides the one rule it may not break.
    ///
    /// <para>
    /// The distinction is <b>not</b> mechanical — a resident and a bench are placed into
    /// the same slot by the same code and drawn by the same method. It exists so the
    /// catalog can say, and the build gate can prove, that <see cref="Resident"/> is
    /// never for sale. That is the whole endowment argument for this feature: a resident
    /// is proof of a glade the player finished, and the moment one can be bought the
    /// grove stops being a record of what they did and becomes a receipt.
    /// </para>
    /// <para>
    /// One catalog with a kind on it rather than two catalogs, because everything else
    /// about them is identical: one content file, one validator, one asset scope, one
    /// ownership rule, one shop grid, one picker. Two parallel systems is how the second
    /// one ends up missing whatever the first one learned.
    /// </para>
    /// </summary>
    public enum HomesteadPieceKind
    {
        /// <summary>Scenery. May be earned, may be bought, usually bought.</summary>
        Decor,

        /// <summary>A creature that lives here. Earned by playing, and never for sale.</summary>
        Resident,

        /// <summary>
        /// A home. One per grove, drawn on the hearth, and <b>never placed by hand</b>.
        ///
        /// <para>
        /// <b>Why the home is a ladder of ids rather than a stored level.</b> A player's home
        /// is the one thing in the grove that should get visibly better for years, which
        /// sounds like a number that goes up and must not be one: a stored count is exactly
        /// the shape invariant 11b forbids, because two devices reading 3 and 1 are equally
        /// consistent with "one upgraded" and "one has not heard yet". So each tier is its own
        /// permanent id in the same union-joined set that already holds purchases — invariant
        /// 15, unchanged — and the grove draws the best one owned. Buying is irreversible,
        /// union is the join, and there is no schema bump and no new merge rule.
        /// </para>
        /// <para>
        /// <b>And why it is drawn rather than placed.</b> A dwelling the player has to
        /// remember to put down is a dwelling they can buy and not see, which is precisely the
        /// confusion the shop's copy already caused once. Deriving it means the purchase
        /// <em>is</em> the moment the home changes — which is the whole feature.
        /// </para>
        /// </summary>
        Dwelling,
    }

    /// <summary>
    /// One thing a player can put in their grove.
    ///
    /// <para>
    /// <b>A priced piece is bought by the copy; everything else is permission.</b> A player
    /// who buys <c>fence_low</c> gets <see cref="Bundle"/> of them and may stand each one
    /// somewhere; a player who <em>earned</em> <c>rune_stone</c> by clearing a glade may draw
    /// it in one tile or in twelve. That split is not a compromise between two designs, it is
    /// the two things being genuinely different: stock is the shop's half of the feature and
    /// an entitlement is play's, and only one of them should ever run out.
    /// </para>
    /// <para>
    /// <b>What made the count representable.</b> Until v20 there was no count anywhere,
    /// because a number of copies <em>remaining</em> is exactly the stored count invariant 11b
    /// forbids — two devices showing 3 and 1 are equally consistent with "one bought two
    /// more" and "one has not heard about a purchase", so every merge rule over the pair is
    /// wrong somewhere, and hearts spent a schema version learning it. What is stored instead
    /// is copies <b>ever bought</b>, which only rises and therefore joins by <c>max</c>, with
    /// what is left derived against the placements. See <see cref="GroveStock"/>.
    /// </para>
    /// <para>
    /// A piece is held when its requirement is met <em>or</em> it was bought — the
    /// composite rule lives in <see cref="HomesteadLedger"/> alone, exactly as invariant
    /// 15a puts the companion rule in <c>CompanionLedger</c>. Nothing here answers it;
    /// <see cref="RequiresLevel"/> and <see cref="RequiresChapter"/> are half of it.
    /// </para>
    /// </summary>
    public readonly struct HomesteadPiece
    {
        /// <summary>
        /// Permanent id. It is written into the save file — both into the owned set and
        /// into every slot holding one — so it is under the same rule as a
        /// <see cref="LevelId"/>: never renamed, never reused, never derived from position.
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// Art key, relative to <c>Art/</c>. Deliberately a separate string from
        /// <see cref="Id"/> and deliberately a whole relative path rather than a leaf under
        /// one fixed folder.
        ///
        /// <para>
        /// Separate from the id for <c>AvatarDefinition.Portrait</c>'s reason: art is re-cut
        /// and re-named between drops and a save file must never be holding a path. A whole
        /// relative path because residents draw the board's own critter flipbooks — which
        /// are global art the game has already paid for — while decor lives under
        /// <c>Art/Homestead/</c>. One field expresses both, and a future resident that is not
        /// a board critter needs no new rule.
        /// </para>
        /// </summary>
        public readonly string Art;

        /// <summary>
        /// True when <see cref="Art"/> names a folder of frames rather than one sprite.
        ///
        /// Every resident so far is animated and no decor is, but this is its own field
        /// rather than derived from <see cref="Kind"/>: a still resident and a flickering
        /// lantern are both obviously reasonable, and inferring it would make the first of
        /// either an engine change instead of a content row.
        /// </summary>
        public readonly bool Animated;

        public readonly HomesteadPieceKind Kind;

        /// <summary>
        /// Which slots this piece belongs in. See <see cref="HomesteadSlotKind"/>.
        ///
        /// Read for decor only: a resident stands wherever the player likes and a dwelling
        /// only ever stands on the hearth, both of which <see cref="Fits"/> settles without
        /// consulting this.
        /// </summary>
        public readonly HomesteadSlotKind Slot;

        /// <summary>
        /// Where a dwelling sits on the ladder — 1 is the cabin every grove starts with.
        /// Zero for everything else.
        ///
        /// <para>
        /// It orders the ladder and it drives how much life the home shows: smoke from the
        /// chimney, a lit window, lanterns at the door. That second use is what lets the tiers
        /// read as five different homes while they share one placeholder sprite, and it is why
        /// the number lives in the content rather than being inferred from the catalog's order
        /// — an author inserting a tier in the middle must not silently repaint every home in
        /// the world.
        /// </para>
        /// </summary>
        public readonly int Tier;

        /// <summary>
        /// Credits that buy this piece outright. <b>Zero or absent means it cannot be
        /// bought.</b>
        ///
        /// <para>
        /// The sentinel points that way for <c>ManifestCompanionDto.unlockCost</c>'s reason
        /// and one more. <c>JsonUtility</c> writes a zero into every field an older file
        /// never had, so "absent" and "free" would be the same value if free were the
        /// meaning — a catalog written before this field existed would put the whole shop on
        /// sale for nothing. Here it also carries the rule: a resident has no price, and
        /// zero is how it says so rather than a second flag that could disagree.
        /// </para>
        /// </summary>
        public readonly int Cost;

        /// <summary>
        /// A glade whose clear earns this piece, or <see cref="LevelId.None"/>.
        ///
        /// The earned half of the rule, and <em>derived</em> — it is a question about the
        /// star ledger, which every device recomputes identically and no merge can lose. A
        /// retune that moves a requirement takes nothing away, because <see cref="Cost"/>
        /// and the placement are separate facts.
        /// </summary>
        /// <summary>
        /// How many copies one purchase grants. Always at least 1.
        ///
        /// <para>
        /// <b>Content, and per piece rather than per kind.</b> A fence, a flower and a paving
        /// stone are wanted by the dozen and a well is wanted once, so the shop sells the first
        /// three in tens at the price the single one used to cost. Which pieces those are is a
        /// judgement that moves with every pack imported, so it rides the catalog and can be
        /// retuned in a drop with no app update — the argument <see cref="Cost"/> is already
        /// under. It is authored per piece rather than derived from <see cref="Slot"/> because
        /// the slot kind is the shop's <em>shelf</em> rather than a statement about how many of
        /// a thing anybody wants: the first oversized gate that belongs on the edge shelf and
        /// sells one at a time would otherwise be an engine change.
        /// </para>
        /// <para>
        /// Zero or absent means 1, which is <see cref="Scale"/>'s convention and the reason a
        /// catalog written before this field existed reads correctly rather than selling
        /// nothing at all.
        /// </para>
        /// <para>
        /// It is <b>not</b> a divisor anybody stores. <see cref="GroveStock"/> counts copies and
        /// never purchases, so retuning a bundle changes what the next purchase grants and never
        /// what a player already holds — the only version of this that is safe to change in a
        /// live drop.
        /// </para>
        /// </summary>
        public readonly int Bundle;

        public readonly LevelId RequiresLevel;

        /// <summary>
        /// A chapter whose completion earns this piece, or <see cref="ChapterId.None"/>.
        /// Checked as "every glade in it cleared", which is the same thing the plot ladder
        /// asks and the only reading a player can verify by looking at the map.
        /// </summary>
        public readonly ChapterId RequiresChapter;

        /// <summary>
        /// Keeper level that earns this piece, or 0 for none.
        ///
        /// <para>
        /// The third earned half, and the only one a resident uses — because a resident is a
        /// companion (see <see cref="GroveResidents"/>) and a companion's free route has always
        /// been the keeper ladder. It is not authorable in <c>homestead.json</c> and never will
        /// be: decor is earned by clearing a named thing, which a player can go and do, while a
        /// level gate on a bench would be a wait with nothing to aim at.
        /// </para>
        /// <para>
        /// Derived like the other two — it is a question about the star ledger by way of
        /// <c>PlayerProgression</c>, so it recomputes everywhere, survives every merge and can
        /// be retuned for players who already hold the piece.
        /// </para>
        /// </summary>
        public readonly int RequiresKeeperLevel;

        /// <summary>
        /// How big this piece draws, as a multiple of its slot's own scale. 1 is the
        /// authored size of the art.
        ///
        /// <para>
        /// The slot decides where and roughly how large; the piece decides its own
        /// proportion within that. Two numbers rather than one because a slot is a place in
        /// a composition — the front of the near meadow is drawn bigger than the back of the
        /// far one — while a pebble is smaller than an oak wherever either of them stands.
        /// Folding them into a single number would mean re-tuning every piece whenever a plot
        /// was re-laid out.
        /// </para>
        /// </summary>
        public readonly float Scale;

        /// <summary>
        /// How far up its slot this piece sits, as a fraction of its own drawn height.
        ///
        /// Zero puts the sprite's centre on the slot's point; the shipped art is drawn
        /// standing on the ground, so most pieces want about a half. It is a property of the
        /// art rather than of the slot for the reason <c>UIKit.PillFaceLift</c> exists: where
        /// the visual base of a painted shape sits inside its rectangle is a fact about that
        /// image, and centring instead of measuring is a mistake this project has already
        /// made three times.
        /// </summary>
        public readonly float Lift;

        public HomesteadPiece(string id, string art, bool animated, HomesteadPieceKind kind,
                              int cost, LevelId requiresLevel, ChapterId requiresChapter,
                              float scale, float lift,
                              HomesteadSlotKind slot = HomesteadSlotKind.Ground, int tier = 0,
                              int requiresKeeperLevel = 0, int bundle = 1)
        {
            Bundle = bundle < 1 ? 1 : bundle;
            Id = id;
            Art = string.IsNullOrEmpty(art) ? id : art;
            Animated = animated;
            Kind = kind;
            Slot = kind == HomesteadPieceKind.Dwelling ? HomesteadSlotKind.Hearth : slot;
            Tier = tier < 0 ? 0 : tier;
            Cost = cost < 0 ? 0 : cost;
            RequiresLevel = requiresLevel;
            RequiresChapter = requiresChapter;
            RequiresKeeperLevel = requiresKeeperLevel < 0 ? 0 : requiresKeeperLevel;
            Scale = scale > 0f ? scale : 1f;
            Lift = lift;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public bool IsResident => Kind == HomesteadPieceKind.Resident;

        /// <summary>True for a home. See <see cref="HomesteadPieceKind.Dwelling"/>.</summary>
        public bool IsDwelling => Kind == HomesteadPieceKind.Dwelling;

        /// <summary>
        /// Whether this piece can be placed by hand at all.
        ///
        /// <para>
        /// <b>One rule now, where there were three.</b> On the islands a slot had a role and a
        /// piece fitted only its own — the rim took fences, the back took trees — and that rule
        /// was what stopped a sprinkle of pre-placed dots looking accidental. The floor has no
        /// dots: every tile is identical and empty, and where a thing goes is as much the
        /// player's decision as what it is. Constraining that would be taking the feature back
        /// out. See <c>GroveFloor</c>.
        /// </para>
        /// <para>
        /// What survives is the one exception that was never about composition: a
        /// <b>dwelling</b> is drawn from what the player owns rather than placed, so it cannot
        /// be put anywhere and nothing else may stand on the hall's tile. The kind is still
        /// carried, because the shop pages by it (see <c>GroveShelf</c>) — it went from a rule
        /// a player could hit to a way of finding things.
        /// </para>
        /// </summary>
        public bool CanBePlaced => IsValid && !IsDwelling;

        /// <summary>True when credits are a way to get this one. See <see cref="Cost"/>.</summary>
        public bool IsForSale => IsValid && Cost > 0;

        /// <summary>
        /// True when the player holds a <em>number</em> of these rather than the right to draw
        /// one — that is, when copies are counted and can run out.
        ///
        /// <para>
        /// <b>Priced decor, and nothing else.</b> A resident is a companion and lives in
        /// <c>companionsOwned</c> as an entitlement (invariant 16a); a home rung is a rung; and
        /// anything free or earned by playing is derived from the star ledger, so writing a
        /// count of it down would be a second answer for a retune to put out of step with the
        /// first (invariant 14). Only the shop's half of the catalog is stock, which is what
        /// keeps the twelve starter pieces and the eight earned ones behaving exactly as they
        /// did before v20.
        /// </para>
        /// <para>
        /// Asked of the <em>piece</em> and never of the player, so it cannot come to depend on
        /// who is looking. Whether a particular player has run out is
        /// <see cref="HomesteadLedger.Available(HomesteadPiece)"/>.
        /// </para>
        /// </summary>
        public bool IsStocked => IsValid && Kind == HomesteadPieceKind.Decor && IsForSale;

        /// <summary>
        /// What one copy is worth, which is the price divided by the bundle.
        ///
        /// <para>
        /// Read by <see cref="GroveScore"/> so a grove's worth is what was paid for it however
        /// the shop happened to package it — ten fences bought as one bundle are worth the
        /// bundle, not ten of them. <c>ContentValidation</c> refuses a price its bundle does not
        /// divide, so the division is exact rather than quietly rounding a player's grove down
        /// by nine credits a fence.
        /// </para>
        /// </summary>
        public int UnitCost => Bundle <= 1 ? Cost : Cost / Bundle;

        /// <summary>True when something in the game has to happen before this is held.</summary>
        public bool HasRequirement
            => RequiresLevel.IsValid || RequiresChapter.IsValid || RequiresKeeperLevel > 0;

        /// <summary>
        /// True when nothing gates this piece at all: no requirement and no price.
        ///
        /// These are what a brand-new grove is furnished from, and <c>ContentValidation</c>
        /// warns when there are none — a first visit to an empty plot with an empty picker
        /// is a feature that looks broken.
        /// </summary>
        public bool IsStarter => IsValid && !HasRequirement && !IsForSale;

        /// <summary>
        /// A piece's name is a pure function of its id and its kind, with no override — the
        /// same rule a level's and a companion's names are under (invariant 5a), and for the
        /// same reason: anything holding an id can label it without reading the catalog, which
        /// is what lets the save file alone tell a support tool what is standing in a slot.
        ///
        /// <para>
        /// A resident answers with the <em>companion's</em> key, because a resident is a
        /// companion (see <see cref="GroveResidents"/>) and the two screens must call somebody
        /// by one name. Still derived and still unoverridable — the function simply takes the
        /// kind as well as the id, and the kind is knowable from the catalog the id came out
        /// of. Duplicating thirty-one names under a second prefix was the alternative, and a
        /// translated string that exists twice is a translated string that will one day differ.
        /// </para>
        /// </summary>
        public string NameKey => IsResident
            ? Progression.AvatarDefinition.DefaultNameKey(GroveResidents.CompanionIdOf(Id))
            : DefaultNameKey(Id);

        public static string DefaultNameKey(string id) => "ui.piece." + id;

        public override string ToString() => Id ?? "(none)";
    }
}
