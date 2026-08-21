using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Social
{
    /// <summary>How a claim ended. Only two of these are failures.</summary>
    public enum NameClaimOutcome
    {
        /// <summary>Reserved. The name is now this account's.</summary>
        Claimed = 0,

        /// <summary>Already this account's. A retry, or a name that never changed.</summary>
        Unchanged,

        /// <summary>Somebody else holds it. The player picks another.</summary>
        Taken,

        /// <summary>
        /// The server will not publish it — too short once folded, or the word filter.
        ///
        /// Permanent, and the one outcome the client must not retry (invariant 13a). The
        /// player keeps the name on their own screens regardless.
        /// </summary>
        Refused,

        /// <summary>Renamed too recently. <see cref="NameClaim.CooldownSeconds"/> says how long.</summary>
        Cooldown,

        /// <summary>
        /// Nothing was adjudicated: no backend, no session, or no network.
        ///
        /// <b>Not a refusal, and the rename goes ahead anyway.</b> The name is stored locally
        /// and the next publish claims it — see <c>functions/src/index.ts</c>. Blocking a
        /// rename on reachability would make the one thing in this game a player does about
        /// their own identity the only thing that needs a signal.
        /// </summary>
        Unavailable,
    }

    /// <summary>What a claim came back with.</summary>
    public struct NameClaim
    {
        public NameClaimOutcome Outcome;

        /// <summary>What the account is called after the call, whatever the outcome.</summary>
        public string Name;

        /// <summary>The fold the account now holds. Empty when it holds none.</summary>
        public string Key;

        /// <summary>Seconds left on the rename cooldown. Zero unless <see cref="Outcome"/> says so.</summary>
        public int CooldownSeconds;

        public static NameClaim Unavailable => new NameClaim
        {
            Outcome = NameClaimOutcome.Unavailable,
            Name = string.Empty,
            Key = string.Empty,
        };
    }

    /// <summary>
    /// Reserving a keeper name, so no two groves stand on a board under the same one.
    ///
    /// <para>
    /// <b>Two calls with deliberately different costs.</b> <see cref="CheckAsync"/> is a
    /// direct document read — one read, no function invocation, no index — because it happens
    /// while somebody is typing and is the only part of this feature that could ever be
    /// expensive. <see cref="ClaimAsync"/> is a callable, because taking a name has to be
    /// adjudicated in a transaction and happens once or twice in the life of an account. That
    /// split is the whole cost design; see <c>functions/src/names.ts</c>.
    /// </para>
    /// <para>
    /// <b>Uniqueness is a rule about the <em>published</em> name, not about the save.</b>
    /// <c>wallet.displayName</c> stays exactly what it was — a preference, merged by recency,
    /// stamped by <c>displayNameSetUnix</c> (invariant 11c) — because a global fact cannot be
    /// enforced by a merge between two devices. So the name joins invariant 13's fourth clause:
    /// the client's copy is what its own screens draw, and the server's reservation is what a
    /// stranger sees. Nothing about this adds a field to the save file.
    /// </para>
    /// <para>
    /// <b>Every call is best-effort and nothing in the game waits on one.</b> With no backend —
    /// a build with no Firebase, a player with no signal — there is no uniqueness and renaming
    /// works exactly as it always did, which is the same stance <see cref="GroveBoard"/> takes
    /// towards the boards themselves.
    /// </para>
    /// </summary>
    public static class KeeperNames
    {
        static IGroveBoardBackend Backend => CloudSaveService.Backend as IGroveBoardBackend;

        /// <summary>
        /// Whether names can be adjudicated at all here.
        ///
        /// False in a build with no cloud backend, where every name is free because nothing
        /// anywhere else can be seen.
        /// </summary>
        public static bool IsAvailable => CloudSaveService.IsAvailable && Backend != null;

        /// <summary>
        /// Asks whether a folded name is spoken for.
        ///
        /// <para>
        /// One document read against a collection the client may <c>get</c> and may never
        /// <c>list</c> — so a player can ask about a name they have typed and nobody can walk
        /// the reservations. An absent document is success with "free", which is the ordinary
        /// answer and the one that must not cost anything.
        /// </para>
        /// </summary>
        public static async Task<(CloudResult result, bool taken, bool mine)> CheckAsync(
            string key, CancellationToken cancellation = default)
        {
            if (!IsAvailable || string.IsNullOrEmpty(key))
                return (CloudResult.Failed(CloudFailure.Offline, "names unavailable"), false, false);

            var (result, holderId) = await Backend.ReadNameHolderAsync(key, cancellation);
            if (!result.Ok) return (result, false, false);

            bool taken = !string.IsNullOrEmpty(holderId);
            bool mine = taken && string.Equals(holderId, CloudState.UserId, System.StringComparison.Ordinal);

            return (result, taken, mine);
        }

        /// <summary>
        /// Takes a name for this account, releasing whatever it held.
        ///
        /// <para>
        /// Safe to call with the name already held: the server answers
        /// <see cref="NameClaimOutcome.Unchanged"/> and writes nothing, which is what makes a
        /// retry after a lost reply free rather than a second write.
        /// </para>
        /// </summary>
        public static async Task<NameClaim> ClaimAsync(
            string stored, CancellationToken cancellation = default)
        {
            if (!IsAvailable) return NameClaim.Unavailable;

            var (result, claim) = await Backend.ClaimNameAsync(stored, cancellation);
            return result.Ok ? claim : NameClaim.Unavailable;
        }
    }
}
