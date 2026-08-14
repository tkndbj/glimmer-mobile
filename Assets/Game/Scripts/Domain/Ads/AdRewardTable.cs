using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// The ceilings that bound whatever a content file asks for.
    ///
    /// Every number here exists to stop a bad or hostile <c>progression.json</c> from
    /// doing something the build cannot survive — the same job <c>DailyRules.MaxChests</c>
    /// does for the chest panel. Content is allowed to retune the economy; it is not
    /// allowed to redefine what the economy is.
    /// </summary>
    public static class AdRules
    {
        /// <summary>
        /// Most ads one placement may pay out for in a day, however high content sets it.
        ///
        /// Thirty is far above what any mediation network will actually fill for a single
        /// user — which is the point. The cap that binds in practice is the network's, and
        /// this one exists only so a typo cannot turn a daily limit into an unbounded
        /// currency faucet that the server would then have to argue with.
        /// </summary>
        public const int MaxDailyCap = 30;

        /// <summary>
        /// Longest gap a content file may impose between two ads.
        ///
        /// Five minutes. Anything longer stops being pacing and becomes a broken button,
        /// and a player who has just lost their last heart will read it as exactly that.
        /// </summary>
        public const int MaxCooldownSeconds = 300;

        /// <summary>
        /// Most currency a single ad may claim.
        ///
        /// A ceiling on the <em>claim</em>, not on the grant: the server pays its own
        /// figure regardless. This bounds what a tampered content file can make the client
        /// briefly display, which matters because a balance that jumps to a million and
        /// then corrects itself on sync is indistinguishable from a bug to the player who
        /// saw it.
        /// </summary>
        public const int MaxRewardAmount = 5000;

        /// <summary>Default gap between two rewarded ads, in seconds.</summary>
        public const int DefaultCooldownSeconds = 45;
    }

    /// <summary>
    /// What one placement pays, and how often.
    ///
    /// <para>
    /// The kind is a <see cref="ChestDropKind"/>, reused rather than redeclared. The name
    /// says "chest" because that is where the vocabulary was first needed, but the type is
    /// the game's whole reward language — credits, gems, hearts, a heart boost — and there
    /// is exactly one place that maps those ids to currencies and decides which of them
    /// the server has to adjudicate. Declaring a second, parallel enum for ads would
    /// duplicate that decision, and a duplicated rule about which rewards are server-owned
    /// is the specific kind of drift invariant 9a exists to prevent.
    /// </para>
    /// </summary>
    public readonly struct AdOffer
    {
        public readonly string PlacementId;
        public readonly ChestDropKind Kind;
        public readonly int Amount;

        /// <summary>How many times a day this placement pays. Zero disables it outright.</summary>
        public readonly int DailyCap;

        public AdOffer(string placementId, ChestDropKind kind, int amount, int dailyCap)
        {
            PlacementId = placementId ?? string.Empty;
            Kind = kind;
            Amount = amount < 0 ? 0 : amount > AdRules.MaxRewardAmount ? AdRules.MaxRewardAmount : amount;
            DailyCap = dailyCap < 0 ? 0 : dailyCap > AdRules.MaxDailyCap ? AdRules.MaxDailyCap : dailyCap;
        }

        public bool IsValid
            => !string.IsNullOrEmpty(PlacementId) && Kind != ChestDropKind.None && Amount > 0 && DailyCap > 0;

        /// <summary>What this offer hands over, in the same shape a chest drop takes.</summary>
        public ChestDrop AsDrop() => new ChestDrop(Kind, Amount);

        /// <summary>True when the server has to adjudicate this reward rather than the client.</summary>
        public bool IsCurrency => ChestDropKinds.IsCurrency(Kind);

        public static readonly AdOffer None = new AdOffer(string.Empty, ChestDropKind.None, 0, 0);
    }

    /// <summary>
    /// What every rewarded placement pays, immutable once built.
    ///
    /// <para>
    /// Content, for the reason the chest table already argues and one more besides. A
    /// rewarded ad's payout is the lever that balances ad revenue against the heart gate,
    /// and it is tuned against numbers — fill rate, eCPM, session length — that nobody has
    /// until the game is live in a market. A payout that needs a store review to change is
    /// a payout that gets set once, badly, from a guess.
    /// </para>
    /// <para>
    /// Like the daily block, this is deliberately <b>not</b> its own schema version. It is
    /// an optional field: a client that predates it ignores it and keeps the built-in
    /// table, and a client that has it reads a file written before it existed and does the
    /// same. Bumping <see cref="Progression.ProgressionSchema"/> for a new optional field
    /// would invalidate the reward table for every client that has not updated, over a
    /// change that has nothing to do with them.
    /// </para>
    /// </summary>
    public sealed class AdRewardTable
    {
        readonly Dictionary<string, AdOffer> _offers;

        AdRewardTable(int cooldownSeconds, Dictionary<string, AdOffer> offers)
        {
            CooldownSeconds = cooldownSeconds;
            _offers = offers;
        }

        /// <summary>Seconds a player must wait between two rewarded ads, across all placements.</summary>
        public int CooldownSeconds { get; }

        /// <summary>
        /// The offer for a placement, or <see cref="AdOffer.None"/> when it has none.
        ///
        /// A placement with no entry is switched off rather than defaulted, which is what
        /// makes removing an offer a content change: publish the table without it and the
        /// button stops being drawn everywhere, with no build and no dead code path.
        /// </summary>
        public AdOffer Offer(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return AdOffer.None;
            return _offers.TryGetValue(placementId, out var offer) ? offer : AdOffer.None;
        }

        public bool Has(string placementId) => Offer(placementId).IsValid;

        public IEnumerable<AdOffer> Offers => _offers.Values;

        /// <summary>
        /// The table that ships inside the build.
        ///
        /// <para>
        /// The shape of it is the design. Hearts pay two at a time, because one is not
        /// worth a thirty-second video to a player who just lost — the offer has to be
        /// obviously better than waiting, or it teaches people to ignore it. The cap is
        /// ten, which is above what a mediation network will fill and therefore invisible
        /// to an honest player, but present so the number exists and can be lowered from a
        /// config push if a market turns out to behave differently.
        /// </para>
        /// <para>
        /// Coins pay a little under a two-star clear and are capped tighter, because that
        /// placement is player-initiated with no natural trigger: it is the one a
        /// determined farmer would sit on, and the one whose payout competes directly with
        /// playing the game.
        /// </para>
        /// </summary>
        public static readonly AdRewardTable Default = Build(AdRules.DefaultCooldownSeconds, new[]
        {
            new AdOffer(AdPlacement.HeartRefill, ChestDropKind.Hearts, 2, 10),
            new AdOffer(AdPlacement.CoinBonus, ChestDropKind.Credits, 150, 6),
        });

        static AdRewardTable Build(int cooldownSeconds, AdOffer[] offers)
        {
            var map = new Dictionary<string, AdOffer>(StringComparer.Ordinal);
            for (int i = 0; i < offers.Length; i++)
                if (offers[i].IsValid) map[offers[i].PlacementId] = offers[i];

            return new AdRewardTable(cooldownSeconds, map);
        }

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>ads</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in table
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static AdRewardTable Resolve(AdsDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            // Unwritten reads as -1 and inherits, the same tri-state the reward rules use.
            // Zero is honoured here, unlike the chest table's runsPerChest, because "no
            // cooldown" is a coherent thing to want and a network's own pacing still applies.
            int cooldown = dto.cooldownSeconds < 0 ? AdRules.DefaultCooldownSeconds : dto.cooldownSeconds;

            if (cooldown > AdRules.MaxCooldownSeconds)
            {
                problems.Add($"ads cooldownSeconds is {cooldown}, above the supported " +
                             $"{AdRules.MaxCooldownSeconds}; clamped");
                cooldown = AdRules.MaxCooldownSeconds;
            }

            if (dto.placements == null || dto.placements.Length == 0)
            {
                problems.Add("ads block lists no placements; using the built-in table");
                return Default;
            }

            var map = new Dictionary<string, AdOffer>(StringComparer.Ordinal);

            for (int i = 0; i < dto.placements.Length; i++)
            {
                if (!TryReadOffer(dto.placements[i], i, problems, out var offer)) continue;

                if (map.ContainsKey(offer.PlacementId))
                {
                    problems.Add($"ads names placement '{offer.PlacementId}' twice; " +
                                 "an offer that exists twice pays whichever copy was read last");
                    return Default;
                }

                map[offer.PlacementId] = offer;
            }

            // Every entry was rejected. Falling back beats shipping a table that silently
            // switches the whole feature off, which is what an empty map would do.
            if (map.Count == 0)
            {
                problems.Add("ads block has no usable placements; using the built-in table");
                return Default;
            }

            return new AdRewardTable(cooldown, map);
        }

        /// <summary>
        /// One placement, or false when it breaks a rule.
        ///
        /// Unknown placement ids are skipped rather than fatal — that is how a newer
        /// content pack reaches an older build, and dropping the entry degrades the table
        /// instead of the game. Everything else is a mistake in a file we authored.
        /// </summary>
        static bool TryReadOffer(AdPlacementDto dto, int index, List<string> problems, out AdOffer offer)
        {
            offer = AdOffer.None;
            if (dto == null) { problems.Add($"ads placement {index} is empty"); return false; }

            if (!AdPlacement.IsKnown(dto.id))
            {
                problems.Add($"ads placement {index} names unknown placement '{dto.id}'; skipped");
                return false;
            }

            var kind = ChestDropKinds.Parse(dto.kind);
            if (kind == ChestDropKind.None)
            {
                problems.Add($"ads placement '{dto.id}' names unknown reward kind " +
                             $"'{dto.kind}'; skipped");
                return false;
            }

            if (dto.amount < 1)
            {
                problems.Add($"ads placement '{dto.id}' pays {dto.amount}; an ad that rewards " +
                             "nothing is an ad nobody watches twice");
                return false;
            }

            if (dto.amount > AdRules.MaxRewardAmount)
            {
                problems.Add($"ads placement '{dto.id}' pays {dto.amount}, above the supported " +
                             $"{AdRules.MaxRewardAmount}; clamped");
            }

            if (dto.dailyCap < 1)
            {
                problems.Add($"ads placement '{dto.id}' has daily cap {dto.dailyCap}; use a " +
                             "positive cap, or remove the placement to switch it off");
                return false;
            }

            if (dto.dailyCap > AdRules.MaxDailyCap)
            {
                problems.Add($"ads placement '{dto.id}' has daily cap {dto.dailyCap}, above the " +
                             $"supported {AdRules.MaxDailyCap}; clamped");
            }

            offer = new AdOffer(dto.id, kind, dto.amount, dto.dailyCap);
            return offer.IsValid;
        }
    }
}
