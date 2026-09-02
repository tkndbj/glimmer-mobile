using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// The public boards: keeping this account's card current, and fetching everybody else's.
    ///
    /// <para>
    /// <b>It shares <see cref="CloudSaveService"/>'s backend rather than owning one.</b> There
    /// is one session, one set of credentials and one <see cref="CloudFailure"/> vocabulary,
    /// and a second backend would mean a second thing to authenticate, a second thing
    /// <c>Boot</c> has to remember to wire and a second dark path to keep working when no
    /// backend is configured. It is a different <em>service</em> because it answers a different
    /// question — the save is this player's progress and must never be lost, a card is a
    /// picture of it that can be rebuilt at any time — and mixing the two would put a
    /// leaderboard read on the critical path of the one operation progress depends on.
    /// </para>
    /// <para>
    /// <b>Everything here is best-effort and nothing is load-bearing.</b> A card that never
    /// publishes costs a row on a list; a board that never arrives draws as "not yet". So no
    /// call here can fail a launch, block a sync or hold the save latch, and every failure is
    /// an empty answer rather than an exception. That is <see cref="GroveStats"/>'s stance,
    /// which this is the second and larger instance of.
    /// </para>
    /// <para>
    /// <b>A card is asked for after the sync, never after the change.</b> The server builds
    /// the card from the save under <c>players/{uid}</c>, so a publish requested the moment a
    /// piece was placed was answered from the save pushed <em>last</em> time — and the
    /// fingerprint then noted as published stopped the real one ever being sent. Every board
    /// in the game showed every grove one session behind, for a week, with a successful call
    /// and a well-formed card on each publish. So the only thing that asks for a publish is
    /// <see cref="CloudSaveService.Settled"/>, the card is built from the save the receipt
    /// carries (<see cref="GroveCard.OfSave"/>) rather than from the live ledgers, and the
    /// reply is checked against the revision the receipt named
    /// (<see cref="GrovePublication.Proves"/>). What makes the change reach the server
    /// promptly is <see cref="SyncTriggers"/>, wired once in <c>Boot</c>: a placement or a
    /// purchase asks for a sync, and the sync asks for the card.
    /// </para>
    /// </summary>
    public static class GroveBoard
    {
        /// <summary>
        /// How long a fetched board is reused before it is asked for again.
        ///
        /// Boards are rebuilt once a day, so anything shorter buys nothing but reads. Five
        /// minutes rather than a day because a player who has just bought something wants to
        /// see whether it moved them, and because a cache that outlives the session is a cache
        /// that has to be invalidated by something.
        /// </summary>
        public const double CacheSeconds = 300d;

        /// <summary>
        /// How many visited groves are remembered.
        ///
        /// Small on purpose: a card is a couple of kilobytes and the point of the cache is to
        /// make going back one row free, not to hold a leaderboard in memory. Evicted oldest
        /// first, because a player walks a list downward.
        /// </summary>
        public const int MaxRememberedCards = 24;

        static readonly GrovePublishPolicy _policy = new GrovePublishPolicy();

        static readonly Dictionary<string, (LeaderboardBoard board, double at)> _boards =
            new Dictionary<string, (LeaderboardBoard, double)>(StringComparer.Ordinal);

        static readonly Dictionary<string, (GroveCard card, double at)> _cards =
            new Dictionary<string, (GroveCard, double)>(StringComparer.Ordinal);
        static readonly List<string> _cardOrder = new List<string>();

        static int _publishing;
        static double _now;
        static bool _attached;
        static bool _ranksAsked;

        /// <summary>
        /// The last receipt a sync handed over, kept so it can be judged again.
        ///
        /// A receipt arriving before the grove catalog has loaded cannot be scored — every
        /// piece is worth nothing against an empty catalog — so it is held here and evaluated
        /// again when the catalog is published. Without that, a rename made before the player
        /// ever opened their grove would wait for the next sync that happened to run with the
        /// catalog loaded. Judging an old receipt again is safe: what it proves is that the
        /// server holds at least that revision, which only becomes more true.
        /// </summary>
        static SyncReceipt _receipt;

        /// <summary>Raised when this account's published card changed, so a screen can repaint.</summary>
        public static event Action Published;

        /// <summary>
        /// The board half of the backend, or null when there is none.
        ///
        /// Resolved on every call rather than cached, because <c>Boot</c> chooses the backend
        /// after this type may first have been touched and a test may replace it between two
        /// calls — the same reason <see cref="CloudSaveService"/> reads its own field rather
        /// than handing it out.
        /// </summary>
        static IGroveBoardBackend Backend => CloudSaveService.Backend as IGroveBoardBackend;

        /// <summary>
        /// Whether boards work here at all.
        ///
        /// False in a build with no Firebase, and equally false behind a backend that does not
        /// implement <see cref="IGroveBoardBackend"/> — which is what makes a save-only test
        /// double disable the feature rather than break it.
        /// </summary>
        public static bool IsAvailable => CloudSaveService.IsAvailable && Backend != null;

        /// <summary>Whether this keeper is taking part. See <c>GameSettings.BoardOptIn</c>.</summary>
        public static bool OptedIn => GameSettings.BoardOptIn;

        /// <summary>What the server last published for this account. Empty until it has.</summary>
        public static GroveCard Mine { get; private set; } = GroveCard.Empty;

        /// <summary>True while a publish is in flight, for a panel that wants to say so.</summary>
        public static bool IsPublishing => Volatile.Read(ref _publishing) != 0;

        // ------------------------------------------------------------------ wiring
        /// <summary>
        /// Subscribes to everything that can change what a visitor would see. Called once,
        /// from <c>Boot</c>, and safe to call again.
        ///
        /// <para>
        /// The four sources are the three entitlement ledgers and the arrangement — which is
        /// precisely the set <see cref="GroveCard.Fingerprint"/> covers, and the agreement
        /// between those two lists is the whole correctness of the debounce. A fifth thing
        /// that changes a card and does not raise one of these would simply never publish, so
        /// anything added to the card belongs on this list in the same commit.
        /// </para>
        /// </summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            // The one source. A settled sync is the only moment the server is known to hold
            // the grove a card would be built from, and the receipt says which save and which
            // revision — see the type's remarks. Nothing else may ask, because a request
            // raised by the change itself was the bug this file's header describes.
            CloudSaveService.Settled += OnSettled;

            // A receipt held back for want of a catalog is judged when the catalog arrives.
            HomesteadCatalog.Changed += Reconsider;

            // Turning the board off takes the card down; turning it on has to reach the
            // server's copy of the setting first, so it asks for a sync rather than a card.
            GameSettings.Changed += OnSettingsChanged;
        }

        static void OnSettled(SyncReceipt receipt)
        {
            if (!receipt.IsValid) return;

            _receipt = receipt;
            Remember();
            Consider(receipt);
        }

        static void Reconsider()
        {
            if (_receipt.IsValid) Consider(_receipt);
        }

        /// <summary>
        /// Decides whether the save the server now holds is worth a card, and asks for one.
        ///
        /// <para>
        /// Built from the receipt's save rather than from the ledgers, for
        /// <see cref="GroveCard.OfSave"/>'s reason: a change made while the push was in
        /// flight is on the device and not on the server, and only the file the server holds
        /// can say what the server will publish. The request carries the revision that file
        /// has there, and the reply is held to it.
        /// </para>
        /// </summary>
        static void Consider(SyncReceipt receipt)
        {
            if (!IsAvailable) return;

            if (!OptedIn)
            {
                _policy.RequestWithdrawal();
                return;
            }

            // Nothing can be scored against an empty catalog, and "worth nothing" would be
            // the wrong answer; the receipt is kept and judged when the catalog is published.
            if (!HomesteadCatalog.IsLoaded) return;

            var card = GroveCard.OfSave(HomesteadCatalog.Current, receipt.Save, CloudState.UserId,
                                        PlayerProgression.Level.Level, SaveSchema.NowUnix());

            _policy.Request(card.Fingerprint(), receipt.ServerRevision,
                            card.Score >= GrovePublishPolicy.Worth);
        }

        // ------------------------------------------------------------- remembering
        /// <summary>
        /// Where this device notes what it last put on the board.
        ///
        /// <para>
        /// <b>Local, not in the save, and for <c>RunGuard</c>'s reason.</b> "What this device
        /// has already uploaded" is a fact about the device rather than about the account:
        /// merged, a phone would believe a card its tablet published, and a tablet that had
        /// never run would believe one it did not. It also goes both up and down, so it could
        /// never be joined — invariant 11b, straightforwardly.
        /// </para>
        /// <para>
        /// Keyed by account so a switch cannot inherit the other player's note, which would
        /// suppress the incoming grove's first publish entirely.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Versioned, and the bump is deliberate. Every note written under the first key
        /// vouched for a card built from the previous session's save (this file's header), so
        /// each of them is a claim that a stale card is current — and the proof that would
        /// catch that only runs on a reply, which no old note is ever going to get. A new key
        /// makes every device republish exactly once on its next settled sync. Bump it again
        /// only for the same reason: a fingerprint that has been recorded against a wrong card.
        /// </remarks>
        const string PublishedKey = "grove.published.2.";

        static void Remember()
        {
            string uid = CloudState.UserId;
            if (string.IsNullOrEmpty(uid)) return;

            _policy.Adopt(UnityEngine.PlayerPrefs.GetString(PublishedKey + uid, string.Empty));
        }

        /// <summary>
        /// Written through <see cref="DevicePrefs"/> rather than staged, because this note is
        /// taken at the one moment it can be lost: immediately after a network round trip, on
        /// an app that has been in the background long enough to do one. Left to Unity's own
        /// <c>OnApplicationQuit</c> it would survive a clean quit and almost nothing else, and
        /// a lost note is a card republished on the next launch — harmless, server-side, and
        /// paid for by every device it happens to.
        /// </summary>
        static void Note(string fingerprint)
        {
            string uid = CloudState.UserId;
            if (string.IsNullOrEmpty(uid)) return;

            DevicePrefs.WriteString(PublishedKey + uid, fingerprint ?? string.Empty);
        }

        static void OnSettingsChanged()
        {
            if (!IsAvailable) return;

            // Turning it off has to take the card down rather than merely stop rebuilding it.
            // Turning it back on cannot publish here: the server reads the opt-in off the save
            // it holds, and the save it holds still says off — asked now, it would withdraw
            // the card the player has just asked for. So the setting goes up first, and the
            // sync that carries it asks for the card through Settled like any other change.
            if (OptedIn) CloudSaveService.RequestSync();
            else _policy.RequestWithdrawal();
        }

        /// <summary>
        /// Forgets everything keyed to the account. Called when the device changes account.
        ///
        /// <para>
        /// Invariant 17's discipline applied to a cache: the published fingerprint describes
        /// <em>an account's</em> card, and carrying one across a switch would let the incoming
        /// player's grove look already-published and never reach the board. The visited cards
        /// go too — not for correctness, but because a switch is the one moment somebody may
        /// be looking at a stranger's grove that is about to stop being reachable.
        /// </para>
        /// </summary>
        public static void Forget()
        {
            // The note is keyed by account, so it is left where it is rather than cleared: a
            // player switching back to this account should keep the saving it represents.
            _policy.Forget();
            _boards.Clear();
            _cards.Clear();
            _cardOrder.Clear();

            // "Who this device reported" belongs to the player rather than to the handset, so
            // it goes with the account. Keeping it would grey the report control for somebody
            // who has never used it, on a grove they have never seen.
            NameReports.Forget();
            Mine = GroveCard.Empty;
            _ranksAsked = false;

            // A receipt describes an account's save on the server; the next account's
            // arrives with its own sync.
            _receipt = default;
        }

        // ------------------------------------------------------------------- ticking
        /// <summary>
        /// Drives the publish policy, and holds the clock the caches age against.
        ///
        /// <para>
        /// Called from <see cref="CloudSaveService.Tick"/> rather than from <c>Boot</c>
        /// directly, so the board follows the save's lifecycle exactly and there is one place
        /// that has to be wired instead of two. Elapsed time is handed in a frame at a time
        /// for <c>RunScreen.Tick</c>'s reason: a device clock can jump, and a cache aged against one
        /// would either never expire or expire on every frame.
        /// </para>
        /// </summary>
        public static void Tick(float deltaSeconds, bool networkReachable)
        {
            if (!IsAvailable) return;

            if (deltaSeconds > 0f) _now += deltaSeconds;

            _policy.NetworkChanged(networkReachable);

            switch (_policy.Tick(deltaSeconds))
            {
                case GrovePublishAction.Publish:
                    _ = RunPublishAsync(_policy.InFlightFingerprint, _policy.InFlightRevision);
                    break;

                case GrovePublishAction.Withdraw:
                    _ = RunWithdrawAsync();
                    break;
            }
        }

        // ---------------------------------------------------------------- publishing
        static GroveCard BuildMine()
            => GroveCard.OfPlayer(HomesteadCatalog.Current,
                                  CloudState.UserId,
                                  Wallet.DisplayName,
                                  Wallet.AvatarId,
                                  PlayerProgression.Level.Level,
                                  SaveSchema.NowUnix());

        /// <summary>
        /// The card this device would publish right now — the local prediction, drawn while
        /// the server's own answer is still in flight or absent.
        ///
        /// It is only a prediction because the server recomputes the worth and clamps its
        /// bought half; the two agree for every honest player, which is what makes drawing
        /// this the right thing rather than a lie waiting to be corrected.
        /// </summary>
        public static GroveCard Predicted() => BuildMine();

        static async Task RunPublishAsync(string fingerprint, long revision)
        {
            if (Interlocked.Exchange(ref _publishing, 1) != 0) return;

            try
            {
                var (result, published) = await Backend.PublishGroveAsync(CloudState.UserId);

                if (result.Ok)
                {
                    // The proof. The request was made after a sync that left the server's
                    // document at `revision`; a card built from anything older is a card of
                    // a grove this device has already replaced, however well-formed.
                    if (!published.Proves(revision))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[Boards] the card was built from save revision {published.SaveRevision} " +
                            $"where this device had settled {revision}");

                        if (_policy.Stale())
                        {
                            // Push again, and the sync that lands asks for the card again;
                            // the policy holds the work meanwhile with a backoff.
                            CloudSaveService.RequestSync();
                            return;
                        }

                        // Retries spent. Taken as published rather than retried for ever
                        // (invariant 13a); the next real change asks afresh.
                        UnityEngine.Debug.LogWarning("[Boards] accepting the stale card after " +
                                                     $"{GrovePublishPolicy.MaxStaleRetries} retries");
                    }
                    else
                    {
                        _policy.Succeeded(fingerprint);
                    }

                    Mine = published.Card ?? GroveCard.Empty;
                    Note(fingerprint);

                    // Our own row on any cached board is now stale. Cheaper and more honest
                    // to drop the caches than to patch a row into a list the server sorted.
                    _boards.Clear();

                    Raise();
                }
                else if (result.Failure == CloudFailure.Rejected)
                {
                    // Permanent for this card — an opted-out account, a save the server will
                    // not vouch for. Retrying it forever is invariant 13a's loop.
                    _policy.Refused();
                }
                else
                {
                    _policy.Failed();
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                _policy.Failed();
            }
            finally
            {
                Volatile.Write(ref _publishing, 0);
            }
        }

        static async Task RunWithdrawAsync()
        {
            if (Interlocked.Exchange(ref _publishing, 1) != 0) return;

            try
            {
                var result = await Backend.WithdrawGroveAsync(CloudState.UserId);

                if (result.Ok)
                {
                    Mine = GroveCard.Empty;
                    _policy.Succeeded(string.Empty);
                    Note(string.Empty);
                    _boards.Clear();
                    Raise();
                }
                else if (result.Failure == CloudFailure.Rejected) _policy.Refused();
                else _policy.Failed();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                _policy.Failed();
            }
            finally
            {
                Volatile.Write(ref _publishing, 0);
            }
        }

        static void Raise()
        {
            try { Published?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// Fetches the published distribution once a session, and forgets about it.
        ///
        /// <see cref="CloudSaveService.BeginStatsRefresh"/>'s twin, separate from the sync for
        /// the same reason: it needs no sign-in, writes nothing, and nothing waits on it.
        /// </summary>
        public static void BeginRanksRefresh(CancellationToken cancellation = default)
        {
            if (!IsAvailable || _ranksAsked) return;

            _ranksAsked = true;
            _ = RefreshRanksAsync(cancellation);
        }

        public static async Task<CloudResult> RefreshRanksAsync(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");

            var (result, table, population, builtUnix) =
                await Backend.ReadGroveRanksAsync(cancellation);

            if (result.Ok) GroveRanks.Publish(table, population, builtUnix);
            else _ranksAsked = false;               // asked and missed; a later screen may retry

            return result;
        }

        /// <summary>
        /// One board, from the cache when it is fresh enough.
        ///
        /// An unknown id is refused here rather than at the server, because a path this client
        /// composed can only name a document that cannot exist, and a read nobody should pay
        /// for is still a read somebody pays for.
        /// </summary>
        public static async Task<(CloudResult result, LeaderboardBoard board)> FetchBoardAsync(
            string boardId, CancellationToken cancellation = default)
        {
            if (!LeaderboardBoard.IsKnown(boardId))
                return (CloudResult.Failed(CloudFailure.Rejected, "unknown board"), LeaderboardBoard.None);

            if (_boards.TryGetValue(boardId, out var held) && _now - held.at < CacheSeconds)
                return (CloudResult.Success, held.board);

            var (result, board) = await Backend.ReadLeaderboardAsync(boardId, cancellation);

            if (result.Ok && board != null) _boards[boardId] = (board, _now);

            return (result, board ?? LeaderboardBoard.None);
        }

        /// <summary>
        /// One keeper's grove. Held briefly, so stepping back up a list is free.
        ///
        /// <para>
        /// Aged the way a board is, against the same <see cref="CacheSeconds"/>, and bounded
        /// by count as well, oldest first. A card used to be kept for the life of the process
        /// on the argument that a picture of somebody else's grove is never wrong in a way a
        /// visitor could notice — which is true of a five-minute-old one and false of a
        /// week-old one, and a phone that is never quite closed keeps a process for weeks.
        /// </para>
        /// </summary>
        public static async Task<(CloudResult result, GroveCard card)> FetchCardAsync(
            string ownerId, CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(ownerId))
                return (CloudResult.Failed(CloudFailure.Rejected, "no owner"), GroveCard.Empty);

            if (_cards.TryGetValue(ownerId, out var held) && _now - held.at < CacheSeconds)
                return (CloudResult.Success, held.card);

            var (result, card) = await Backend.ReadGroveCardAsync(ownerId, cancellation);

            if (result.Ok && card != null && card.IsValid) Remember(ownerId, card);

            return (result, card ?? GroveCard.Empty);
        }

        /// <summary>
        /// Reports a keeper's published name.
        ///
        /// <para>
        /// Best-effort like everything else here: a failure is an empty answer rather than an
        /// exception, and nothing in the game waits on it. A successful report is remembered
        /// for the session so the control can say it has been used — see
        /// <see cref="NameReports"/> for why that record must never reach the save file.
        /// </para>
        /// <para>
        /// The visited card is <b>not</b> evicted from the cache on a report. The name does not
        /// change for the reporter — a takedown needs more than one of them — so dropping it
        /// would buy a document read and an identical picture.
        /// </para>
        /// </summary>
        public static async Task<(CloudResult result, NameReportOutcome outcome)> ReportNameAsync(
            string keeperId, CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(keeperId))
                return (CloudResult.Failed(CloudFailure.Rejected, "no keeper"),
                        NameReportOutcome.Unavailable);

            if (!IsAvailable)
                return (CloudResult.Failed(CloudFailure.Offline, "no boards here"),
                        NameReportOutcome.Unavailable);

            var (result, outcome) = await Backend.ReportKeeperNameAsync(keeperId, cancellation);

            // Remembered for a duplicate as well as for a fresh report: both mean the server
            // holds this pair, and treating only the first as recorded would leave a device
            // that lost a reply offering the control for ever.
            if (result.Ok && outcome != NameReportOutcome.Unavailable
                && outcome != NameReportOutcome.Throttled)
            {
                NameReports.Remember(keeperId);
            }

            return (result, outcome);
        }

        static void Remember(string ownerId, GroveCard card)
        {
            if (!_cards.ContainsKey(ownerId)) _cardOrder.Add(ownerId);
            _cards[ownerId] = (card, _now);

            while (_cardOrder.Count > MaxRememberedCards)
            {
                _cards.Remove(_cardOrder[0]);
                _cardOrder.RemoveAt(0);
            }
        }

        /// <summary>The board this player's own grove is ranked on right now.</summary>
        public static string MyLeagueId()
            => GroveLeague.IdFor(GroveScore.Of(HomesteadCatalog.Current).Stars);
    }
}
