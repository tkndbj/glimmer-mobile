using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// The public boards, as a seam of their own.
    ///
    /// <para>
    /// <b>Separate from <see cref="ICloudSaveBackend"/> on purpose, and the compiler is what
    /// argued for it.</b> These five calls were added to that interface first, and every test
    /// double of a save backend in the suite stopped compiling — which is the right answer to
    /// the wrong question. A fake that exists to prove a chest is granted once has no opinion
    /// about leaderboards and should never have to grow one, and an interface that forces it
    /// to is an interface describing two things.
    /// </para>
    /// <para>
    /// <b>One implementation still, and one session.</b> <c>FirebaseCloudSaveBackend</c>
    /// implements both, so there is nothing extra to authenticate, nothing extra for
    /// <c>Boot</c> to wire and one dark path in a build with no Firebase. A backend that does
    /// not implement this simply has no boards, which <c>GroveBoard</c> reads as "not
    /// available here" — the same answer it gives when there is no backend at all, and the
    /// reason a save-only double disables the feature rather than breaking it.
    /// </para>
    /// <para>
    /// Every call here is best-effort. Nothing in the game waits on one, none of them touch the
    /// save file or the sync latch, and every failure is an empty answer rather than an
    /// exception — <see cref="GroveStats"/>'s stance, which this is the larger instance of.
    /// </para>
    /// </summary>
    public interface IGroveBoardBackend
    {
        /// <summary>
        /// Asks the server to rebuild this account's public grove card.
        ///
        /// <para>
        /// <b>The request carries nothing but the intent, and that is the security design.</b>
        /// No score, no contents, no name — the server reads <c>players/{uid}</c> with its own
        /// credentials, recomputes the worth from the published catalog, clamps the bought half
        /// to currency it derived itself, sanitises the name and writes the card. A client that
        /// could hand any of that in would be a client that could put any number on the
        /// leaderboard, which is precisely what <see cref="GroveCard"/> exists to
        /// prevent. The card comes back so the screen can show what was actually published
        /// rather than what this device predicted.
        /// </para>
        /// <para>
        /// Safe to call repeatedly: it is a rebuild rather than an append, so the second call
        /// writes the same document again. <see cref="GrovePublishPolicy"/> is what stops
        /// it being called for nothing, not any check on the server.
        /// </para>
        /// </summary>
        Task<(CloudResult result, GroveCard card)> PublishGroveAsync(
            string userId, CancellationToken cancellation = default);

        /// <summary>
        /// Takes this account's public card down.
        ///
        /// A distinct call rather than "publish an empty one", because they are different acts
        /// and only one of them is what a player asked for when they turned the board off. A
        /// card that is already absent is a success, not an error — the alternative leaves a
        /// device retrying a withdrawal for the life of the account (invariant 13a).
        /// </summary>
        Task<CloudResult> WithdrawGroveAsync(
            string userId, CancellationToken cancellation = default);

        /// <summary>
        /// Reads who holds a reserved name, if anybody. Empty means nobody does.
        ///
        /// <para>
        /// <b>A direct document read, and that is the cost decision the whole feature turns
        /// on.</b> This is the one call here that happens while a player is typing, so routing
        /// it through a function would put an invocation and a cold start behind every pause in
        /// a text field. Reading `names/{key}` by id is one document read, needs no index and
        /// costs the same at ten players and at ten million — the rules grant `get` and refuse
        /// `list`, so a name can be asked about and the collection cannot be walked.
        /// </para>
        /// <para>
        /// The holder's id comes back rather than a bare boolean so the panel can tell "taken"
        /// from "this is already yours", which are different sentences and only one of them is
        /// a problem.
        /// </para>
        /// </summary>
        Task<(CloudResult result, string holderId)> ReadNameHolderAsync(
            string nameKey, CancellationToken cancellation = default);

        /// <summary>
        /// Takes a name for this account, releasing whatever it held.
        ///
        /// <para>
        /// A callable and not a write, because uniqueness cannot be enforced from a client:
        /// the reservation and the release of the previous one have to happen in one
        /// transaction, and the rules cannot express "one name per account" when the other half
        /// of the constraint is a document the client writes itself. See
        /// <c>functions/src/names.ts</c>.
        /// </para>
        /// <para>
        /// Safe to call with the name already held — the server writes nothing and reports
        /// <see cref="NameClaimOutcome.Unchanged"/>, which is what makes a retry after a lost
        /// reply cost nothing.
        /// </para>
        /// </summary>
        Task<(CloudResult result, NameClaim claim)> ClaimNameAsync(
            string storedName, CancellationToken cancellation = default);

        /// <summary>
        /// Reports a keeper's published name.
        ///
        /// <para>
        /// <b>The request carries one id and nothing else</b> — no reason, no category, no free
        /// text. The server reads the card and the reservation itself, so there is nothing in
        /// the body to forge and no way to report a name that is not actually on a board. It is
        /// <see cref="PublishGroveAsync"/>'s bargain in the other direction: the client says
        /// only that it wants something to happen, and the server decides what.
        /// </para>
        /// <para>
        /// The reply deliberately does not say whether the report counted towards a takedown —
        /// see <see cref="NameReportOutcome"/>. Safe to call twice: the server keys the record
        /// on the pair of accounts, so a retry after a lost reply records nothing new.
        /// </para>
        /// </summary>
        Task<(CloudResult result, NameReportOutcome outcome)> ReportKeeperNameAsync(
            string keeperId, CancellationToken cancellation = default);

        /// <summary>
        /// Reads one keeper's published grove, for a visit.
        ///
        /// <para>
        /// A single document read against a collection the client may read and may never
        /// write. That is the whole cost of visiting somebody — no function, no query, no
        /// fan-out — which is what lets a row on a board be tappable at any player count.
        /// </para>
        /// </summary>
        Task<(CloudResult result, GroveCard card)> ReadGroveCardAsync(
            string ownerId, CancellationToken cancellation = default);

        /// <summary>
        /// Reads one published board: the global hundred, or a league's.
        ///
        /// One document, whole, however many rows it carries — see
        /// <see cref="LeaderboardBoard"/> for why it is denormalised rather than queried.
        /// </summary>
        Task<(CloudResult result, LeaderboardBoard board)> ReadLeaderboardAsync(
            string boardId, CancellationToken cancellation = default);

        /// <summary>
        /// Reads the published distribution of grove worth, and each league's population.
        ///
        /// <see cref="ReadGroveStatsAsync"/>'s twin, and public for the same reasons: it names
        /// no player, it is the same for everybody, and every reader treats an absent table as
        /// "nothing to say".
        /// </summary>
        Task<(CloudResult result, GroveRankTable table,
              Dictionary<string, int> population, long builtUnix)> ReadGroveRanksAsync(
            CancellationToken cancellation = default);
    }
}
