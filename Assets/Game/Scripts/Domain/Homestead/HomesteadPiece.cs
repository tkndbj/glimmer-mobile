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
    /// <b>Owning a piece is permission to draw it, not possession of a copy.</b> A player
    /// who holds <c>fence_low</c> may place it in one slot or in twelve; there is no count
    /// anywhere and no such thing as running out. That is not a simplification, it is the
    /// only shape the save file permits — a number of copies held is exactly the stored
    /// count invariant 11b forbids, because two devices showing 3 and 1 are equally
    /// consistent with "one bought two more" and "one has not heard about a purchase", so
    /// every merge rule over the pair is wrong somewhere. Hearts spent a schema version
    /// learning that. An entitlement set joined by union has no such problem, and it
    /// happens to make the better game: the shop sells variety rather than quantity, which
    /// is what makes two players' groves look different.
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
        public readonly LevelId RequiresLevel;

        /// <summary>
        /// A chapter whose completion earns this piece, or <see cref="ChapterId.None"/>.
        /// Checked as "every glade in it cleared", which is the same thing the plot ladder
        /// asks and the only reading a player can verify by looking at the map.
        /// </summary>
        public readonly ChapterId RequiresChapter;

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
                              HomesteadSlotKind slot = HomesteadSlotKind.Ground, int tier = 0)
        {
            Id = id;
            Art = string.IsNullOrEmpty(art) ? id : art;
            Animated = animated;
            Kind = kind;
            Slot = kind == HomesteadPieceKind.Dwelling ? HomesteadSlotKind.Hearth : slot;
            Tier = tier < 0 ? 0 : tier;
            Cost = cost < 0 ? 0 : cost;
            RequiresLevel = requiresLevel;
            RequiresChapter = requiresChapter;
            Scale = scale > 0f ? scale : 1f;
            Lift = lift;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public bool IsResident => Kind == HomesteadPieceKind.Resident;

        /// <summary>True for a home. See <see cref="HomesteadPieceKind.Dwelling"/>.</summary>
        public bool IsDwelling => Kind == HomesteadPieceKind.Dwelling;

        /// <summary>
        /// Whether this piece may stand in a slot of this kind.
        ///
        /// <para>
        /// Three rules and no more. A <b>dwelling</b> belongs to the hearth and nothing else
        /// may go there, because the hearth is drawn from what the player owns rather than
        /// from what they placed. A <b>resident</b> fits anywhere but the hearth — a creature
        /// standing on a path, in a flower bed or under a tree is right in every case, and
        /// telling somebody their own rescued critter may not stand somewhere is the kind of
        /// rule that makes a toy feel like a form. Everything else fits its own kind exactly.
        /// </para>
        /// </summary>
        public bool Fits(HomesteadSlotKind slot)
        {
            if (!IsValid) return false;

            if (IsDwelling) return slot == HomesteadSlotKind.Hearth;
            if (slot == HomesteadSlotKind.Hearth) return false;
            if (IsResident) return true;

            return Slot == slot;
        }

        /// <summary>True when credits are a way to get this one. See <see cref="Cost"/>.</summary>
        public bool IsForSale => IsValid && Cost > 0;

        /// <summary>True when something in the game has to happen before this is held.</summary>
        public bool HasRequirement => RequiresLevel.IsValid || RequiresChapter.IsValid;

        /// <summary>
        /// True when nothing gates this piece at all: no requirement and no price.
        ///
        /// These are what a brand-new grove is furnished from, and <c>ContentValidation</c>
        /// warns when there are none — a first visit to an empty plot with an empty picker
        /// is a feature that looks broken.
        /// </summary>
        public bool IsStarter => IsValid && !HasRequirement && !IsForSale;

        /// <summary>
        /// A piece's name is a pure function of its id, with no override — the same rule a
        /// level's and a companion's names are under (invariant 5a), and for the same
        /// reason: anything holding an id can label it without reading the catalog, which is
        /// what lets the save file alone tell a support tool what is standing in a slot.
        /// </summary>
        public string NameKey => DefaultNameKey(Id);

        public static string DefaultNameKey(string id) => "ui.piece." + id;

        public override string ToString() => Id ?? "(none)";
    }
}
