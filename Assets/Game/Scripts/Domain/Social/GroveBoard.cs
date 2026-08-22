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
    /// <b>Subscriptions, not call sites.</b> Publishing is asked for by the three ledgers and
    /// the profile raising their own events, wired once in <see cref="Attach"/>. This file's
    /// project has paid three times over for the alternative — a step every new call site has
    /// to remember, forgotten by the third one — and a grove that silently stops updating on
    /// the board is exactly the failure nobody notices until a player reports it.
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

        static readonly Dictionary<string, GroveCard> _cards =
            new Dictionary<string, GroveCard>(StringComparer.Ordinal);
        static readonly List<string> _cardOrder = new List<string>();

        static int _publishing;
        static double _now;
        static bool _attached;
        static bool _ranksAsked;

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

            HomesteadLedger.Changed += RequestPublish;   // pieces, and residents through it
            GroveLand.Changed += RequestPublish;
            HomesteadLayout.Changed += RequestPublish;
            Wallet.ProfileChanged += RequestPublish;     // the name and the worn companion
            GameSettings.Changed += OnSettingsChanged;

            // And once when the account is known, which is what puts a grove built before the
            // boards existed onto them. Every event above fires on a *change*, so without this
            // a player who had already bought everything would never publish anything —
            // silently, and for ever. It is nearly free: the remembered fingerprint below
            // means an unchanged grove asks for nothing.
            CloudSaveService.Synced += OnSynced;
        }

        static void OnSynced()
        {
            Remember();
            RequestPublish();
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
        const string PublishedKey = "grove.published.";

        static void Remember()
        {
            string uid = CloudState.UserId;
            if (string.IsNullOrEmpty(uid)) return;

            _policy.Adopt(UnityEngine.PlayerPrefs.GetString(PublishedKey + uid, string.Empty));
        }

        static void Note(string fingerprint)
        {
            string uid = CloudState.UserId;
            if (string.IsNullOrEmpty(uid)) return;

            UnityEngine.PlayerPrefs.SetString(PublishedKey + uid, fingerprint ?? string.Empty);
        }

        /// <summary>
        /// Something visible changed, so the card is owed a rebuild.
        ///
        /// Cheap enough to call on every event: the policy compares fingerprints and drops a
        /// request that would republish what is already there, which is most of them.
        /// </summary>
        public static void RequestPublish()
        {
            if (!IsAvailable) return;

            if (!OptedIn)
            {
                _policy.RequestWithdrawal();
                return;
            }

            var card = BuildMine();
            _policy.Request(card.Fingerprint(), card.Score >= GrovePublishPolicy.Worth);
        }

        static void OnSettingsChanged()
        {
            // Turning it off has to take the card down rather than merely stop rebuilding it,
            // and turning it back on has to republish rather than wait for the next purchase.
            if (OptedIn) RequestPublish();
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
        }

        // ------------------------------------------------------------------- ticking
        /// <summary>
        /// Drives the publish policy, and holds the clock the caches age against.
        ///
        /// <para>
        /// Called from <see cref="CloudSaveService.Tick"/> rather than from <c>Boot</c>
        /// directly, so the board follows the save's lifecycle exactly and there is one place
        /// that has to be wired instead of two. Elapsed time is handed in a frame at a time
        /// for <c>RunClock</c>'s reason: a device clock can jump, and a cache aged against one
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
                    _ = RunPublishAsync(_policy.InFlightFingerprint);
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

        static async Task RunPublishAsync(string fingerprint)
        {
            if (Interlocked.Exchange(ref _publishing, 1) != 0) return;

            try
            {
                var (result, card) = await Backend.PublishGroveAsync(CloudState.UserId);

                if (result.Ok)
                {
                    Mine = card ?? GroveCard.Empty;
                    _policy.Succeeded(fingerprint);
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
        /// A card is not aged out the way a board is: it is a picture of somebody's grove and
        /// a five-minute-old one is not wrong in any way a visitor could notice, whereas a
        /// board carries this player's own row and wants to move when they buy something. The
        /// cache is bounded by count instead, oldest first.
        /// </para>
        /// </summary>
        public static async Task<(CloudResult result, GroveCard card)> FetchCardAsync(
            string ownerId, CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(ownerId))
                return (CloudResult.Failed(CloudFailure.Rejected, "no owner"), GroveCard.Empty);

            if (_cards.TryGetValue(ownerId, out var held)) return (CloudResult.Success, held);

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
            _cards[ownerId] = card;

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
