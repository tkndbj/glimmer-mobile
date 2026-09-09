using System;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// One turret a player may stand on the line: a permanent id, what it does beyond firing, and
    /// what it costs to own.
    ///
    /// <para>
    /// <b>Its id is permanent and invariant 1 reaches it</b>, because the save keys both halves of
    /// the feature on it — the set of turrets a player has bought, and which one they have put on
    /// each colour. Renaming one confiscates a purchase; reusing one hands somebody a turret they
    /// never bought.
    /// </para>
    /// <para>
    /// <b>Everything a player can see about it is derived from that id</b> — the name, the line
    /// under it, the picture and the reel it fires with are all <c>ward.{id}.name</c>,
    /// <c>ward.{id}.note</c> and <c>Siege/Wards/{id}_{colour}</c>. That is invariant 5a's rule:
    /// anything holding an id can draw the turret without reading the catalog it came from, and no
    /// key is ever built by concatenation at a call site.
    /// </para>
    /// <para>
    /// <b>Two prices and only one of them has a gate, which is the owner's rule and matches the
    /// grove's.</b> Credits are what the game pays out for playing, so a credit price is a
    /// <em>reward</em> and carries a keeper level with it; gems are bought or earned slowly, so a
    /// gem price is a <em>shortcut</em> and asks nothing but the gems. That is invariant 15a's
    /// shape — the level gate is permission to pay rather than a second way to be handed the thing
    /// — and 16j's, where the same floor is priced two ways up one ladder.
    /// </para>
    /// </summary>
    public sealed class WardModel
    {
        /// <summary>Permanent. The save keys on it. Lower case, no spaces.</summary>
        public readonly string Id;

        /// <summary>What it does beyond putting a bolt into a raider.</summary>
        public readonly WardAbility Ability;

        /// <summary>
        /// How strong the ability is, in tenths of whatever it measures: tenths of a baseline bolt
        /// for the ones that deal damage, tenths of a march for frost, tenths of a fuel unit for
        /// siphon, tenths of a capacity for a beacon.
        ///
        /// <b>Tenths throughout, and never a float.</b> Everything this multiplies ends up in a
        /// graded number, and a threshold decided by a float is a number three code generators
        /// round three ways — this project has paid for that once already
        /// (<c>Mathf.CeilToInt(45 * 1.20f)</c>).
        /// </summary>
        public readonly int Magnitude;

        /// <summary>
        /// A second number the ability needs, or nought: how many extra raiders a chain reaches,
        /// how many seconds a frost or an ember lasts in tenths, how often a pierce comes round.
        ///
        /// <b>One field rather than one per ability</b>, because a per-ability record would make
        /// <c>WardCatalog</c> a union type and every reader a switch. What it means is documented
        /// on the ability, which is where a reader is already looking.
        /// </summary>
        public readonly int Extent;

        /// <summary>What one costs in gems. Nought means it cannot be bought with gems.</summary>
        public readonly int GemPrice;

        /// <summary>What one costs in credits. Nought means it cannot be bought with credits.</summary>
        public readonly int CoinPrice;

        /// <summary>
        /// The keeper level a credit purchase asks for. Ignored by a gem purchase.
        ///
        /// <b>Nought is a real answer</b> and is how a starter says it asks nothing.
        /// </summary>
        public readonly int MinLevel;

        /// <summary>
        /// Whether this one is handed over rather than bought.
        ///
        /// <b>Derived from having no price at all, and read through <see cref="IsStarter"/> rather
        /// than tested at a call site.</b> That is invariant 16j's hard-won correction: "free" was
        /// <c>Cost &lt;= 0</c> for as long as there was one currency, and the day a second one
        /// arrived every gem-priced region read as free and half the world was handed over at
        /// launch. Both prices are asked here, once.
        /// </summary>
        public bool IsStarter => GemPrice <= 0 && CoinPrice <= 0;

        /// <summary>Whether gems can buy it outright, with no keeper level asked.</summary>
        public bool ForGems => GemPrice > 0;

        /// <summary>Whether credits can buy it, once the keeper level is reached.</summary>
        public bool ForCoins => CoinPrice > 0;

        /// <summary>Where it sits on the shelf. Authored, for <c>HomesteadRegion.Order</c>'s reason.</summary>
        public readonly int Order;

        public WardModel(string id, WardAbility ability, int magnitude, int extent,
                         int gemPrice, int coinPrice, int minLevel, int order)
        {
            Id = id ?? string.Empty;
            Ability = ability;
            Magnitude = magnitude < 0 ? 0 : magnitude;
            Extent = extent < 0 ? 0 : extent;
            GemPrice = gemPrice < 0 ? 0 : gemPrice;
            CoinPrice = coinPrice < 0 ? 0 : coinPrice;
            MinLevel = minLevel < 0 ? 0 : minLevel;
            Order = order;
        }

        /// <summary>The loc key for its name. Derived, never authored (invariant 5a).</summary>
        public string NameKey => "ward." + Id + ".name";

        /// <summary>The loc key for the one line saying what it does.</summary>
        public string NoteKey => "ward." + Id + ".note";

        /// <summary>
        /// The address of the turret standing in the line, in this colour.
        ///
        /// <para>
        /// <b>Built rather than written out as a literal, which is the one place this project
        /// allows that and says why.</b> <c>Tools/verify/artnames.py</c> reads literals off a call
        /// site, so a built name is a name it cannot check — and twenty models times four colours
        /// is eighty literals nobody would keep in step with the catalog anyway. What replaces the
        /// literal is a stronger gate rather than a weaker one: the roster is <em>content</em>, so
        /// <c>ContentValidation</c> and <c>content.py</c> both walk it and error on a model whose
        /// addresses <c>AssetManifest</c> cannot name — which catches a missing picture and a
        /// misspelled id at once, where a literal only ever catches the second.
        /// </para>
        /// </summary>
        public string ArtFor(char colour) => "Wards/" + Id + "_" + colour;

        /// <summary>The reel it fires with, in this colour.</summary>
        public string FireFor(char colour) => "Wards/" + Id + "_" + colour + "_fire";

        /// <summary>The one uncoloured picture the shelf browses it with.</summary>
        public string Thumb => "Wards/" + Id;

        public override string ToString()
            => Id + " (" + WardAbilities.NameOf(Ability) + " " + Magnitude + "/" + Extent + ")";
    }
}
