using System;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// What a chest can contain.
    ///
    /// An enum rather than the string ids used for currencies and levels, and the
    /// difference is deliberate: nothing persists a reward kind. Drops are recomputed
    /// from the day and the chest index every time they are needed, so no save file and
    /// no server document holds one of these, and reordering the members cannot move
    /// anybody's history. The ids in the content file are strings for the usual reason —
    /// see <see cref="ChestDropKinds"/> — and are mapped here on read.
    /// </summary>
    public enum ChestDropKind
    {
        None = 0,

        /// <summary>Soft currency. Server-owned once the backend is live.</summary>
        Credits,

        /// <summary>Hard currency. Server-owned, and the one an attacker actually wants.</summary>
        Gems,

        /// <summary>
        /// Hearts, clamped at <see cref="HeartRules.Ceiling"/> like every other grant.
        ///
        /// <para>
        /// A chest opened at a full bar now <em>keeps</em> its hearts — they stack past
        /// <see cref="HeartRules.RefillCap"/>, which is where the timer stops rather than
        /// where a player's holding ends. That closes the one place a chest could pay
        /// nothing at all, and it does so without touching the property the whole design
        /// rests on: the drop is still a pure function of (account, day, chest), because
        /// what is granted never depends on how many hearts the player happened to hold.
        /// </para>
        /// <para>
        /// A player sitting on fifty still loses the surplus, and the obvious kindness —
        /// paying it out as credits instead — is still refused, for the original reason:
        /// the payout would then depend on the player's holding, and the server has no view
        /// of that, so it could no longer recompute what a chest was worth.
        /// </para>
        /// </summary>
        Hearts,

        /// <summary>
        /// Faster heart regeneration for a fixed window. See <see cref="HeartBoost"/>.
        /// Amount is the duration in hours, so the band is authored the way it reads.
        /// </summary>
        HeartBoost,

        /// <summary>
        /// More time on the run that is happening right now. Amount is seconds.
        ///
        /// <para>
        /// The odd one out, and the difference is worth stating because it changes where the
        /// reward is applied. Every other kind lands in the <see cref="Persistence.Wallet"/>
        /// and outlives the moment it was granted; this one lands on a
        /// <see cref="RunClock"/> that belongs to a single screen and stops existing when
        /// that run resolves. So <c>RewardedAds.Apply</c> deliberately does nothing with it
        /// and the caller applies it — Domain statics have no view of a live board, and
        /// giving them one would be a far worse trade than the empty case.
        /// </para>
        /// <para>
        /// It is <b>not a chest drop</b>, and <c>DailyChestTable</c> refuses it. A chest is
        /// opened on the home screen where there is no run to extend, and a chest that could
        /// roll one would pay a third of its players nothing — the exact failure the
        /// <see cref="Hearts"/> ceiling note is careful to avoid.
        /// </para>
        /// <para>
        /// Nothing about it reaches the server. It is not currency, so
        /// <c>adCurrencyOf</c> returns null for the placement that pays it and the signed
        /// callback grants nothing — correct, and it needed no server change to be true.
        /// </para>
        /// </summary>
        RunTime,
    }

    /// <summary>
    /// The permanent ids a content file uses for drop kinds.
    ///
    /// Strings, and never renamed or reused, for the same reason a <c>LevelId</c> is a
    /// string: a published content file names them, and an enum's numbering is an
    /// implementation detail that must not be able to leak into data.
    /// </summary>
    public static class ChestDropKinds
    {
        public const string Credits = "credits";
        public const string Gems = "gems";
        public const string Hearts = "hearts";
        public const string HeartBoost = "heart_boost";
        public const string RunTime = "run_time";

        public static ChestDropKind Parse(string id)
        {
            if (string.Equals(id, Credits, StringComparison.Ordinal)) return ChestDropKind.Credits;
            if (string.Equals(id, Gems, StringComparison.Ordinal)) return ChestDropKind.Gems;
            if (string.Equals(id, Hearts, StringComparison.Ordinal)) return ChestDropKind.Hearts;
            if (string.Equals(id, HeartBoost, StringComparison.Ordinal)) return ChestDropKind.HeartBoost;
            if (string.Equals(id, RunTime, StringComparison.Ordinal)) return ChestDropKind.RunTime;
            return ChestDropKind.None;
        }

        public static string Id(ChestDropKind kind)
        {
            switch (kind)
            {
                case ChestDropKind.Credits: return Credits;
                case ChestDropKind.Gems: return Gems;
                case ChestDropKind.Hearts: return Hearts;
                case ChestDropKind.HeartBoost: return HeartBoost;
                case ChestDropKind.RunTime: return RunTime;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Which kinds are currency, and therefore adjudicated by the server rather than
        /// applied by the client. Asked in one place so the two halves of the grant path
        /// cannot come to different conclusions about what needs a receipt.
        /// </summary>
        public static bool IsCurrency(ChestDropKind kind)
            => kind == ChestDropKind.Credits || kind == ChestDropKind.Gems;

        /// <summary>
        /// Whether a kind is spent inside the run that granted it, rather than banked.
        ///
        /// <para>
        /// Asked in two places that would otherwise each have to know the list. A chest may
        /// not roll one of these (there is no run open when a chest is opened), and the
        /// shared ad cooldown does not apply to one (the cooldown exists to pace a faucet,
        /// and a reward that cannot leave the run it was granted in is not one).
        /// </para>
        /// </summary>
        public static bool IsTransient(ChestDropKind kind) => kind == ChestDropKind.RunTime;

        /// <summary>The currency ledger a drop belongs to, or empty when it is not currency.</summary>
        public static string CurrencyOf(ChestDropKind kind)
            => kind == ChestDropKind.Credits ? Currency.Credits
             : kind == ChestDropKind.Gems ? Currency.Gems
             : string.Empty;
    }

    /// <summary>One resolved reward: a kind and how much of it.</summary>
    public readonly struct ChestDrop
    {
        public readonly ChestDropKind Kind;
        public readonly int Amount;

        public ChestDrop(ChestDropKind kind, int amount)
        {
            Kind = kind;
            Amount = amount < 0 ? 0 : amount;
        }

        public bool IsValid => Kind != ChestDropKind.None && Amount > 0;

        public static readonly ChestDrop None = new ChestDrop(ChestDropKind.None, 0);

        public bool IsCurrency => ChestDropKinds.IsCurrency(Kind);

        public override string ToString() => $"{Amount} {ChestDropKinds.Id(Kind)}";
    }
}
