using System;

namespace GlimmerGrove.Utilities
{
    /// <summary>
    /// What a utility does when it is used, which is the only thing about one that is code.
    ///
    /// <para>
    /// <b>An enum rather than a string, and that is the line between what content may add and
    /// what it may not.</b> Which utilities exist, what they cost, how strong they are and how
    /// many a player may hold are all authored (<see cref="UtilityCatalog"/>); what a
    /// <em>kind</em> means is a rule with a fail state and a grade attached, so it is code, for
    /// invariant 20's reason applied one level down — content can never add a way of playing.
    /// A drop naming a kind this build has never heard of is skipped whole, exactly as a chapter
    /// naming an unknown mode is.
    /// </para>
    /// </summary>
    public enum UtilityKind
    {
        /// <summary>Nothing. What an unreadable or unknown entry resolves to.</summary>
        None = 0,

        /// <summary>
        /// Damage, over an area of the hill the player picks.
        ///
        /// <b>The only kind that delivers damage, and therefore the only one that is charged
        /// against the grade</b> — see <see cref="UtilityUse"/> and invariant 39.
        /// </summary>
        Blast = 1,

        /// <summary>
        /// Health, into one ward the player picks.
        ///
        /// <b>Mends a standing ward and never raises a fallen one</b>, which is not kindness
        /// withheld but the thing that keeps <c>SiegeBoard.Stranded</c> a certainty: a run here
        /// ends when the last ward falls, and <c>ProtoVerdict</c> is allowed to say so out loud
        /// only because nothing can put one back up (invariant 28f).
        /// </summary>
        Mend = 2,

        /// <summary>
        /// Fuel, into one ward the player picks.
        ///
        /// Fuel becomes bolts becomes damage, so this is charged against the grade at the same
        /// exchange rate a blast is — the conversion is written once, in
        /// <c>SiegeUtility.MatchesFor</c>.
        /// </summary>
        Surge = 3,

        /// <summary>
        /// Damage to <b>everything on the hill at once</b>, aimed at nothing.
        ///
        /// <para>
        /// <b>It is charged like any other damage and that is what keeps it honest.</b> A storm
        /// that empties a full hill delivers thousands, so <c>SiegeUtility.MatchesFor</c> bills it
        /// dozens of matches against the grade — which is the same arithmetic a firepot pays and
        /// the reason invariant 39 needs no special case for it. It buys a <em>finish</em>, never
        /// a grade, and on a hill worth enough to be worth clearing it buys a finish that scores
        /// one star at most.
        /// </para>
        /// <para>
        /// <b>It does not kill a boss and must not.</b> Every raider takes the same magnitude, so
        /// a warlord or an overlord — which carry the health of several waves — is hurt and
        /// survives. A consumable that ended the finale would be the fight sold rather than
        /// played, and the mode's one duel is the thing a chapter is built toward.
        /// </para>
        /// <para>
        /// <b>And it ignores a shield.</b> A bulwark halves what a <em>ward's bolt</em> does to it
        /// because the shield is answered by colour, and a storm has no colour to answer — so the
        /// soak would be a rule about a bolt applied to something that is not one. It is also what
        /// gives the item a reason to exist beyond "more damage": it is the answer to a wave of
        /// armour, which is exactly the wave a player cannot out-match.
        /// </para>
        /// </summary>
        Storm = 4,
    }

    /// <summary>
    /// What a utility is aimed at. Decides which targeting the board offers and nothing else.
    /// </summary>
    public enum UtilityTarget
    {
        /// <summary>A point on the hill, wherever the finger lands.</summary>
        Hill = 0,

        /// <summary>One ward on the line.</summary>
        Ward = 1,

        /// <summary>
        /// Nothing at all — it lands everywhere the moment it is used.
        ///
        /// <b>A third answer rather than a point nobody picks</b>, because "aimed at the whole
        /// board" and "aimed at a place" are different interactions: there is no targeting layer,
        /// no ring to move and nothing to cancel, so a slot carrying one fires on the tap that
        /// arms it. Making it aim at a hill box the rule then ignored would be a control that
        /// rejects nothing, which is invariant 5d asked of an input.
        /// </b>
        /// </summary>
        Everywhere = 2,
    }

    /// <summary>
    /// The permanent ids of the kinds a content file may name.
    ///
    /// Strings, never renamed and never reused, for <c>ChestDropKinds</c>' reason: published
    /// content names them and an enum's numbering is an implementation detail that must not be
    /// able to leak into data.
    /// </summary>
    public static class UtilityKinds
    {
        public const string Blast = "blast";
        public const string Mend = "mend";
        public const string Surge = "surge";
        public const string Storm = "storm";

        public static UtilityKind Parse(string id)
        {
            if (string.Equals(id, Blast, StringComparison.Ordinal)) return UtilityKind.Blast;
            if (string.Equals(id, Mend, StringComparison.Ordinal)) return UtilityKind.Mend;
            if (string.Equals(id, Surge, StringComparison.Ordinal)) return UtilityKind.Surge;
            if (string.Equals(id, Storm, StringComparison.Ordinal)) return UtilityKind.Storm;
            return UtilityKind.None;
        }

        public static string Id(UtilityKind kind)
        {
            switch (kind)
            {
                case UtilityKind.Blast: return Blast;
                case UtilityKind.Mend: return Mend;
                case UtilityKind.Surge: return Surge;
                case UtilityKind.Storm: return Storm;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// What a kind is aimed at.
        ///
        /// Derived from the kind rather than authored beside it, because it is not a decision:
        /// a blast lands on the hill and a mend lands on a ward, and a file able to say
        /// otherwise would be a file able to author a utility no screen can point at.
        /// </summary>
        public static UtilityTarget TargetOf(UtilityKind kind)
            => kind == UtilityKind.Storm ? UtilityTarget.Everywhere
             : kind == UtilityKind.Blast ? UtilityTarget.Hill
             : UtilityTarget.Ward;
    }

    /// <summary>
    /// One utility, as authored: a permanent id, what it does, how strong it is, and what a gem
    /// buys.
    ///
    /// <para>
    /// <b>Its id is permanent and invariant 1 reaches it</b>, because the save keys the player's
    /// stock on it (<see cref="UtilityStock"/>) and a published drop table names it. Renaming one
    /// strands whatever a player is holding; reusing one hands them somebody else's.
    /// </para>
    /// <para>
    /// <b>Everything a player can see about it is derived from that id.</b> The name, the
    /// sentence under it and the picture are <c>utility.{id}.name</c>, <c>utility.{id}.note</c>
    /// and <c>Ui/Utility/{id}</c> — never authored, never concatenated at a call site, for the
    /// reason invariant 5a gives about a glade's loc keys: it is what lets anything holding an id
    /// draw the thing without reading the catalog it came from.
    /// </para>
    /// </summary>
    public sealed class UtilityItem
    {
        /// <summary>Permanent. Save data and published drop tables key on it.</summary>
        public readonly string Id;

        public readonly UtilityKind Kind;

        /// <summary>
        /// How strong one is, in the unit its kind measures: damage for a blast, health for a
        /// mend, fuel-tenths for a surge.
        ///
        /// <b>Tenths for the surge and whole numbers for the other two</b>, because fuel is the
        /// one quantity in <c>SiegeTuning</c> that is a float and a graded number decided by a
        /// float is a number three code generators round three ways.
        /// </summary>
        public readonly int Magnitude;

        // No reach. A blast takes exactly what is standing in the box that was tapped
        // (`SiegeTuning.BlastRows`), so how far it carries is a rule rather than a number — and a
        // content field with one legal value is the decoration invariant 5d names.

        /// <summary>
        /// What one costs in gems, or nought for something only a chest hands out.
        ///
        /// <b>Gems and never credits</b>, and never real money. A utility is consumed, so a
        /// real-money product that granted one would be the stored amount invariant 18d forbids;
        /// gems are the soft sink and a gem debit is an ordinary <c>CurrencyLedger.TrySpend</c>
        /// that <c>submitSpends</c> already refuses when the derived balance cannot cover it.
        /// </summary>
        public readonly int GemPrice;

        /// <summary>
        /// The most of this one a player may hold.
        ///
        /// <b>A published ceiling and therefore enforced only at the moment of a grant</b>, never
        /// by re-reading a save — <c>RegenBounds.Ceiling</c>'s rule, and for its reason: lowering
        /// one from a config push must refuse new ones without ever reaching back into a file to
        /// take one, or two devices would restore and re-clamp each other for ever.
        /// </summary>
        public readonly int MaxHeld;

        /// <summary>Where it sits on the bar. Authored, for <c>HomesteadRegion.Order</c>'s reason.</summary>
        public readonly int Order;

        /// <summary>
        /// The keeper level before this one may be bought or carried at all. Nought for one that
        /// asks nothing.
        ///
        /// <para>
        /// <b>A gate on the <em>shelf</em> rather than on the bar, and it costs the save
        /// nothing.</b> Whether a player may buy one is derived from a keeper level that is itself
        /// derived from the star ledger (invariant 9), so a locked utility needs no field, no
        /// merge rule and nothing for the server to adjudicate — which is the same bargain the
        /// companion gate makes (invariant 15a) and the reason a ward's credit price carries one
        /// too.
        /// </para>
        /// <para>
        /// <b>What it may never do is confiscate.</b> A utility already granted is spendable
        /// whatever the gate says: <c>UtilityLedger</c> reads stock and never re-checks this, for
        /// the reason <c>CompanionLedger.IsHeld</c> does not re-check its gate — a retune must not
        /// take back something a player paid gems for.
        /// </para>
        /// </summary>
        public readonly int MinLevel;

        /// <summary>
        /// How long after one is used before another may be, in whole seconds. Nought means
        /// none, and that is what an older file — or one written before this existed — says.
        ///
        /// <para>
        /// <b>Content, for the reason the price and the magnitude are.</b> It is the number that
        /// decides whether a wave is answered with one firepot or four, so it is the number most
        /// certain to be wrong first guess and the one a live game most needs to move without a
        /// store review. What it may never do is vary per level: a cooldown is a fact about the
        /// item and never about the board, or two players would be playing the same board with
        /// two different allowances (invariant 29c).
        /// </para>
        /// <para>
        /// <b>Whole seconds, never a float.</b> Nothing graded is decided by it, so the usual
        /// argument does not bite — but a countdown a player reads is written down here once and
        /// mirrored by <c>content.py</c>, and an integer is the one shape three code generators
        /// cannot round three ways. <see cref="UtilityCooldown.MaxSeconds"/> is the ceiling.
        /// </para>
        /// </summary>
        public readonly int CooldownSeconds;

        public UtilityItem(string id, UtilityKind kind, int magnitude,
                           int gemPrice, int maxHeld, int order, int minLevel = 0,
                           int cooldownSeconds = 0)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Magnitude = magnitude < 1 ? 1 : magnitude;
            GemPrice = gemPrice < 0 ? 0 : gemPrice;
            MaxHeld = maxHeld < 1 ? 1 : maxHeld > UtilityStock.MaxHeld ? UtilityStock.MaxHeld : maxHeld;
            Order = order;
            MinLevel = minLevel < 0 ? 0 : minLevel;
            CooldownSeconds = cooldownSeconds < 0 ? 0
                            : cooldownSeconds > UtilityCooldown.MaxSeconds
                              ? UtilityCooldown.MaxSeconds : cooldownSeconds;
        }

        public UtilityTarget Target => UtilityKinds.TargetOf(Kind);

        /// <summary>Whether this keeper level may buy one. Never asked about spending one.</summary>
        public bool ReachedBy(int keeperLevel) => keeperLevel >= MinLevel;

        /// <summary>Whether gems can buy one. A chest-only utility answers false.</summary>
        public bool ForSale => GemPrice > 0;

        /// <summary>Whether using one puts it out of reach for a while.</summary>
        public bool Cools => CooldownSeconds > 0;

        /// <summary>The loc key for its name. Derived, never authored (invariant 5a's rule).</summary>
        public string NameKey => "utility." + Id + ".name";

        /// <summary>The loc key for the one line explaining it.</summary>
        public string NoteKey => "utility." + Id + ".note";

        /// <summary>The address of its picture, under the shared UI set.</summary>
        public string Art => "Ui/Utility/" + Id;

        public override string ToString() => Id + " (" + UtilityKinds.Id(Kind) + " " + Magnitude + ")";
    }
}
