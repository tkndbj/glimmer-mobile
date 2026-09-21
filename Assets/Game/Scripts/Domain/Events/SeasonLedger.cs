using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Events
{
    /// <summary>What one season's row in the save holds. Three numbers, all monotone.</summary>
    public sealed class SeasonState
    {
        /// <summary>Marks grown inside this season's window.</summary>
        public int Marks;

        /// <summary>The largest free-track goal already claimed.</summary>
        public int FreeGoal;

        /// <summary>The largest pass-track goal already claimed.</summary>
        public int PassGoal;

        /// <summary>Whether the pass has been bought. Only ever goes true.</summary>
        public bool Pass;

        public SeasonState Copy()
            => new SeasonState { Marks = Marks, FreeGoal = FreeGoal, PassGoal = PassGoal, Pass = Pass };

        /// <summary>The larger of two, field by field. See <see cref="SeasonLedger.Join"/>.</summary>
        public void Absorb(SeasonState other)
        {
            if (other == null) return;
            if (other.Marks > Marks) Marks = other.Marks;
            if (other.FreeGoal > FreeGoal) FreeGoal = other.FreeGoal;
            if (other.PassGoal > PassGoal) PassGoal = other.PassGoal;

            // `or`, because buying is irreversible. The same join owned companions and owned
            // land take, on a single season rather than on a set (invariant 15).
            Pass |= other.Pass;
        }

        public bool IsEmpty => Marks <= 0 && FreeGoal <= 0 && PassGoal <= 0 && !Pass;

        /// <summary>
        /// Whether this row might still be holding a chest nobody has opened.
        ///
        /// <para>
        /// <b>A sound over-approximation, and it has to be one.</b> Answering exactly needs the
        /// season's own ladder, and the ledger deliberately does not hold a catalog — so this
        /// asks the question the row alone can answer: a claim floor is the goal of the highest
        /// rung taken, and every rung still waiting has a goal above that floor and at or below
        /// the marks earned. So <c>Marks &gt; floor</c> is true of every row that owes something
        /// and of some that owe nothing, which is the direction to be wrong in — the cost of a
        /// false yes is one row kept, and the cost of a false no is a player's chest deleted.
        /// </para>
        /// <para>
        /// The pass floor counts only for somebody who bought the pass, because the paid column
        /// of a season nobody paid for was never claimable.
        /// </para>
        /// </summary>
        public bool MayOwe => Marks > FreeGoal || (Pass && Marks > PassGoal);
    }

    /// <summary>
    /// The season: how many marks this player has grown in it, how far up each track they
    /// have claimed, and the act of claiming a rung.
    ///
    /// <para>
    /// <b>Everything stored here only ever rises, and that is the whole of why a season is
    /// mergeable.</b> Three integers per season id — marks grown, and a claim floor per
    /// track — joined by <c>max</c> (invariant 11b). A count of chests <em>remaining</em>
    /// could not be joined; a count of marks <em>grown</em> can, because two devices
    /// showing 40 and 12 are not ambiguous: the larger knows more. A set of claimed rungs
    /// would merge too, but a floor makes "claiming a later rung takes the earlier ones with
    /// it" the only representable behaviour rather than a rule somebody has to remember, and
    /// a floor is one integer where a set of forty is a list the security rules would have
    /// to bound.
    /// </para>
    /// <para>
    /// <b>Marks come from chests, and only from the live season.</b> Every
    /// <see cref="ChestTier"/> carries what opening one is worth
    /// (<see cref="ChestTier.Marks"/>), and <see cref="Note"/> is called wherever a chest is
    /// claimed. Nothing else grows a season, which is what makes the pace bounded by the
    /// calendar rather than by how long somebody is willing to replay a level: the slates
    /// deal what they deal, and a board already beaten pays no chest at all. A chest claimed
    /// while no season is running grows nothing — there is no track for it to belong to, and
    /// banking it for a season that has not started would be paying a player for a week they
    /// were not here.
    /// </para>
    /// <para>
    /// <b>Claiming pays exactly as a task chest pays</b> (<see cref="TaskLedger"/>): the
    /// banked kinds — hearts, boosts, utilities — are applied here and now, and currency
    /// leaves as a claim whose id is derived from what earned it
    /// (<see cref="GrantEntry.MarkChestId"/>), so two devices claiming one rung produce one
    /// entry and the server re-rolls the chest from the same three facts and pays its own
    /// answer. Nothing about what a chest contained is stored anywhere.
    /// </para>
    /// <para>
    /// <b>The pass track needs a receipt, and this type never decides that.</b>
    /// <see cref="SeasonPass"/> holds what the server said about the purchase, written by the
    /// cloud layer and never inferred from a local save (invariant 10). A client that has
    /// not been told cannot claim the paid column, which is the same fail-closed shape
    /// <see cref="TaskLedger.CanClaim"/> has, and the server refuses it a second time on top.
    /// </para>
    /// </summary>
    public static class SeasonLedger
    {
        static readonly Dictionary<string, SeasonState> _seasons =
            new Dictionary<string, SeasonState>(StringComparer.Ordinal);

        /// <summary>The seed tag, shared with the server. Contract (invariant 9c).</summary>
        public const string SeedTag = "mark";

        /// <summary>
        /// The most seasons a save may carry rows for.
        ///
        /// A calendar's worth and then some: at the ninety-day ceiling
        /// (<see cref="EventRules.MaxWindowDays"/>) this is more seasons than the game can
        /// run in a decade, and it is here because the security rules bound the list and a
        /// client writing a longer one loses <em>every</em> save write (invariant 12a).
        /// </summary>
        public const int MaxSeasons = 64;

        /// <summary>Raised when a season's marks or floors moved. The cue a page redraws on.</summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when a rung was handed over, carrying the season, the rung and the track.
        /// For the page drawing it at the time; nothing depends on it.
        /// </summary>
        public static event Action<GroveEvent, EventMilestone, SeasonTrack> Claimed;

        static string PlayerKey => RewardSeed.PlayerKey;

        /// <summary>
        /// Whether a chest may be claimed yet. The task chests' gate, for the task chests'
        /// reason: a chest is rolled from the account id so the server can recompute it, and
        /// before the first sign-in there is no account id to roll from.
        /// </summary>
        public static bool CanClaim => RewardSeed.IsAdjudicable;

        // ------------------------------------------------------------- reading
        /// <summary>This season's row, or an empty one. Never null, never the stored instance.</summary>
        public static SeasonState StateOf(string seasonId)
        {
            if (string.IsNullOrEmpty(seasonId)) return new SeasonState();
            return _seasons.TryGetValue(seasonId, out var state) ? state.Copy() : new SeasonState();
        }

        /// <summary>
        /// Every season this save carries a row for, in calendar order.
        ///
        /// <para>
        /// What <see cref="GroveEvents.All"/> joins onto the authored calendar, so a season that
        /// has closed and left no trace in the manifest — every cycle of a repeating season, the
        /// moment the next one opens — stays reachable while the player still has something in
        /// it (invariant 47c). Ids rather than seasons, because turning one back into a season
        /// is the catalog's job and this assembly's ledger has no catalog in it.
        /// </para>
        /// <para>
        /// Ordinal order, which for a cycle id is calendar order by construction
        /// (<see cref="SeasonCycle.IndexDigits"/>).
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> HeldIds
        {
            get
            {
                if (_seasons.Count == 0) return Array.Empty<string>();

                var ids = new List<string>(_seasons.Count);
                foreach (var pair in _seasons)
                    if (!pair.Value.IsEmpty) ids.Add(pair.Key);

                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        /// <summary>Marks grown in this season.</summary>
        public static int MarksIn(string seasonId)
            => string.IsNullOrEmpty(seasonId) ? 0
             : _seasons.TryGetValue(seasonId, out var state) ? state.Marks : 0;

        /// <summary>
        /// Whether this account has bought a season's pass.
        ///
        /// <para>
        /// <b>The client's copy, and it does not gate money.</b> It says what the page draws
        /// and what a tap on the paid column does; the server keeps its own, written by
        /// <c>submitSpends</c> in the same transaction that takes the gems, and that is what
        /// is read before a paid-track chest is paid. A forged <c>true</c> buys a page that
        /// draws the paid column and a claim the server refuses.
        /// </para>
        /// <para>
        /// It used to be session state read back from a callable, which is what a
        /// <em>receipt</em> needs — a purchase made with real money is the server's fact and
        /// nothing else may assert it (invariant 10). A gem debit is not: it is an ordinary
        /// spend, so the entitlement it buys is stored exactly as every other gem-bought
        /// permanent thing in this game is, and the page works offline.
        /// </para>
        /// </summary>
        public static bool OwnsPass(string seasonId)
            => !string.IsNullOrEmpty(seasonId)
            && _seasons.TryGetValue(seasonId, out var state) && state.Pass;

        /// <summary>The largest goal already claimed on one track. 0 when none.</summary>
        public static int ClaimedGoal(string seasonId, SeasonTrack track)
        {
            if (string.IsNullOrEmpty(seasonId) || !_seasons.TryGetValue(seasonId, out var state)) return 0;
            return track == SeasonTrack.Pass ? state.PassGoal : state.FreeGoal;
        }

        /// <summary>How far through a season this player is, both tracks.</summary>
        public static EventProgress ProgressOf(GroveEvent season)
        {
            if (season == null) return EventProgress.None;

            var state = StateOf(season.Id);
            return EventLedger.ProgressOf(season, state.Marks, state.FreeGoal, state.PassGoal,
                                          state.Pass);
        }

        /// <summary>True when tapping this rung on this track would hand something over.</summary>
        public static bool IsClaimable(GroveEvent season, EventMilestone rung, SeasonTrack track)
        {
            if (season == null) return false;

            var state = StateOf(season.Id);
            int floor = track == SeasonTrack.Pass ? state.PassGoal : state.FreeGoal;
            return EventLedger.IsClaimable(season, rung, track, state.Marks, floor, state.Pass);
        }

        /// <summary>True when this rung's chest is already in the player's hands.</summary>
        public static bool IsClaimed(GroveEvent season, EventMilestone rung, SeasonTrack track)
            => season != null && rung.Pays(track) && rung.Goal <= ClaimedGoal(season.Id, track);

        /// <summary>
        /// What a rung's chest holds, without claiming it. For the opening overlay and the
        /// collect animation, so both read one list.
        /// </summary>
        public static List<ChestDrop> Preview(GroveEvent season, EventMilestone rung, SeasonTrack track)
        {
            var tier = rung.TierOn(track);
            if (season == null || tier == null) return new List<ChestDrop>();

            return tier.Chest.Roll(SeedFor(season.Id, track, rung.Goal));
        }

        /// <summary>
        /// The seed a rung's chest is rolled from: the player, this feature, and the season,
        /// track and rung that earned it. The subject layout is contract with the server's
        /// <c>subjectSeed</c>; see <see cref="ChestSeed"/>.
        /// </summary>
        public static ChestSeed SeedFor(string seasonId, SeasonTrack track, int goal)
            => ChestSeed.ForSubject(PlayerKey, SeedTag, Subject(seasonId, track, goal));

        /// <summary>The subject half of the seed and of the claim id: <c>{season}:{track}:{goal}</c>.</summary>
        public static string Subject(string seasonId, SeasonTrack track, int goal)
            => seasonId + ":" + SeasonTracks.Id(track) + ":" + goal;

        // ------------------------------------------------------------- growing
        /// <summary>
        /// Records that a chest worth <paramref name="marks"/> was claimed.
        ///
        /// <para>
        /// The live season and nothing else, judged on the trusted clock. A season that has
        /// closed stops counting the moment it closes — the rewards it is already holding do
        /// not expire, but the track does, which is what makes a season a deadline rather
        /// than a backlog.
        /// </para>
        /// <para>
        /// Bounded at the ladder's own top, so the stored number cannot grow without limit:
        /// there is nothing above the last rung to earn, and a counter that only ever needs
        /// to reach a goal has no business being a million. That also keeps the merge's
        /// <c>max</c> comparing two numbers inside the same small range.
        /// </para>
        /// </summary>
        public static void Note(int marks)
        {
            if (marks <= 0) return;

            var season = GroveEvents.Live;
            if (season == null || !season.IsValid) return;

            int cap = season.FinalGoal;
            if (cap <= 0) return;

            var state = Mutable(season.Id);
            if (state.Marks >= cap) return;

            int after = state.Marks + marks;
            state.Marks = after > cap ? cap : after;

            SaveService.MarkDirty();
            Raise();
        }

        /// <summary>
        /// What a claimed chest is worth to the season. One call site today
        /// (<see cref="TaskLedger"/>), and the seam every future chest source uses.
        /// </summary>
        public static void NoteChest(ChestTier tier)
        {
            if (tier != null) Note(tier.Marks);
        }

        // ------------------------------------------------------------- buying
        /// <summary>Why a pass could not be bought, so a screen can say which wall it is.</summary>
        public enum PassBuy
        {
            Bought,

            /// <summary>Already held. Not a failure; nothing is charged and nothing is said.</summary>
            Held,

            /// <summary>This season sells no pass at all.</summary>
            NotSold,

            /// <summary>The window has closed. A pass buys rungs, and there are no more to reach.</summary>
            Closed,

            /// <summary>Not enough gems.</summary>
            TooPoor,
        }

        /// <summary>
        /// Buys a season's pass with gems.
        ///
        /// <para>
        /// <b>The debit goes first and the entitlement is only written if it succeeded</b>,
        /// which is <c>WardLedger.TryBuy</c>'s ordering and its argument: a process killed
        /// between the two leaves a player who paid and did not receive, which the spend log
        /// can see and support can put right — where the other order leaves a pass nobody paid
        /// for, which is indistinguishable from a forgery and therefore invisible.
        /// </para>
        /// <para>
        /// <b>A closed season cannot be sold one.</b> A pass buys the paid column of rungs the
        /// player goes on to reach, and marks stop counting at the deadline — so after it, the
        /// purchase is gems for nothing. Rungs already reached stay claimable for ever, which
        /// is why this is the one refusal here that is about the clock.
        /// </para>
        /// </summary>
        public static PassBuy TryBuyPass(GroveEvent season)
        {
            if (season == null || !season.IsValid || !season.HasPremium) return PassBuy.NotSold;
            if (OwnsPass(season.Id)) return PassBuy.Held;
            if (!season.IsLiveAt(GameClock.NowUnix())) return PassBuy.Closed;

            if (!PlayerProgression.TrySpend(Currency.Gems, season.PassGems,
                                            SpendEntry.SeasonPassReason,
                                            SpendEntry.SeasonPassId(season.Id)))
                return PassBuy.TooPoor;

            Mutable(season.Id).Pass = true;

            SaveService.Save();
            Raise();

            Telemetry.Track("season_pass_bought", "season", season.Id, "gems", season.PassGems);

            return PassBuy.Bought;
        }

        // ------------------------------------------------------------ claiming
        /// <summary>
        /// Claims one rung on one track and applies what its chest holds.
        ///
        /// <para>
        /// Two independent guards stop a rung paying twice. The floor refuses a second
        /// attempt at the same goal; and every currency award carries an id derived from the
        /// season, the track and the goal, so even a save edited to lower the floor collides
        /// with an entry already in the ledger, and the server refuses it a third time on top.
        /// </para>
        /// <para>
        /// The floor is raised to <em>this rung's goal</em> rather than swept to the top,
        /// because each rung is a chest with its own ceremony: a sweep would grant several
        /// chests with one animation, which is the "reward that arrives while a panel is up"
        /// failure this game has now made twice. A player holding three waiting rungs taps
        /// three times and opens three chests.
        /// </para>
        /// </summary>
        public static bool TryClaim(GroveEvent season, EventMilestone rung, SeasonTrack track,
                                    out List<ChestDrop> drops)
        {
            drops = null;
            if (season == null || !season.IsValid) return false;

            var tier = rung.TierOn(track);
            if (tier == null) return false;

            // Checked here as well as in the UI. A reward the server would recompute
            // differently must not be claimable through any path, and a guard that lives only
            // in a screen is a guard the next screen forgets.
            if (!CanClaim) return false;

            var state = Mutable(season.Id);
            int floor = track == SeasonTrack.Pass ? state.PassGoal : state.FreeGoal;

            // The entitlement goes in with the floors rather than being asked separately, so
            // this refuses a paid rung nobody bought by the same rule that stops the badge
            // counting one (`EventLedger.Opens`).
            if (!EventLedger.IsClaimable(season, rung, track, state.Marks, floor, state.Pass))
                return false;

            var rolled = tier.Chest.Roll(SeedFor(season.Id, track, rung.Goal));

            if (track == SeasonTrack.Pass) state.PassGoal = rung.Goal;
            else state.FreeGoal = rung.Goal;

            Apply(rolled, season.Id, track, rung.Goal);

            SaveService.Save();
            Raise();

            Telemetry.Track("mark_claimed",
                            "season", season.Id,
                            "track", SeasonTracks.Id(track),
                            "goal", rung.Goal,
                            "tier", tier.Id,
                            "drops", Describe(rolled));

            try { Claimed?.Invoke(season, rung, track); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            drops = rolled;
            return true;
        }

        /// <summary>
        /// Hands out one chest's contents: banked kinds here and now, currency as a claim.
        /// The split <see cref="DailyChests"/> drew, for its reason — currency is the thing
        /// an attacker forges, so it is the thing the server adjudicates.
        /// </summary>
        static void Apply(List<ChestDrop> drops, string seasonId, SeasonTrack track, int goal)
        {
            long now = GameClock.NowUnix();

            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (!drop.IsValid) continue;
                if (BankedDrop.Apply(drop)) continue;
                if (!drop.IsCurrency) continue;

                string currency = ChestDropKinds.CurrencyOf(drop.Kind);
                PlayerProgression.Award(
                    currency, drop.Amount,
                    GrantEntry.MarkChestId(seasonId, track, goal, currency),
                    GrantEntry.MarkChestReason, now);
            }
        }

        static string Describe(List<ChestDrop> drops)
        {
            var parts = new string[drops.Count];
            for (int i = 0; i < drops.Count; i++) parts[i] = drops[i].ToString();
            return string.Join(",", parts);
        }

        static SeasonState Mutable(string seasonId)
        {
            if (!_seasons.TryGetValue(seasonId, out var state))
            {
                state = new SeasonState();
                _seasons[seasonId] = state;
            }

            return state;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _seasons.Clear();
            Absorb(_seasons, dto?.events);
            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            if (dto == null) return;
            dto.events = Rows(_seasons);
        }

        /// <summary>
        /// Joins two calendars, taking the larger of every number.
        ///
        /// A join in the strict sense — idempotent and order-independent — because every
        /// value in it only rises. A season one device has never heard of is carried through
        /// untouched, which is what lets a client on last month's content sync with one that
        /// has the new calendar without either of them losing a track.
        /// </summary>
        public static EventStateDto[] Join(EventStateDto[] mine, EventStateDto[] other)
        {
            // No early return for an empty side, deliberately. Handing one array straight
            // back would skip the sort and the deduplication, so a malformed file joined
            // against nothing would come out still malformed — and `SaveDelta` walks these in
            // order, so it would then read as changed on every single sync.
            var byId = new Dictionary<string, SeasonState>(StringComparer.Ordinal);
            Absorb(byId, mine);
            Absorb(byId, other);

            return Rows(byId);
        }

        /// <summary>
        /// The seasons as rows, <b>sorted by id</b> and capped at <see cref="MaxSeasons"/>.
        ///
        /// <para>
        /// The sort is not tidiness. <see cref="SaveChecksum"/> hashes the serialised file and
        /// <c>SaveDelta</c> decides whether to sync by walking these in order, so rows in
        /// dictionary order would make an unchanged save look changed on every launch — a write
        /// and an upload for nothing, forever.
        /// </para>
        /// <para>
        /// <b>The cap used to truncate the sorted list, and that was a live bug waiting for a
        /// repeating season.</b> Ordinal order is calendar order, so lopping off the tail keeps
        /// the <em>oldest</em> sixty-four rows and throws away the newest — which on a calendar
        /// that never ends means the season being played is the first thing deleted, silently, at
        /// the moment the sixty-fifth opens. It was unreachable while seasons were authored one
        /// at a time and a real ending on the day one was not.
        /// </para>
        /// <para>
        /// So the cap now <b>evicts rather than truncates</b>, and it evicts by what a row is
        /// worth: anything that might still be holding an unopened chest (<see
        /// cref="SeasonState.MayOwe"/>) is kept ahead of anything settled, and within each group
        /// the newest survives. A player who somehow has more than sixty-four unsettled seasons
        /// still loses the oldest of them — there is no arrangement in which a bounded list keeps
        /// everything — but that is sixty-four seasons of never opening a chest, against the old
        /// rule's "the one you are playing".
        /// </para>
        /// </summary>
        static EventStateDto[] Rows(Dictionary<string, SeasonState> seasons)
        {
            if (seasons == null || seasons.Count == 0) return Array.Empty<EventStateDto>();

            var ids = new List<string>(seasons.Count);
            foreach (var pair in seasons)
                if (!pair.Value.IsEmpty) ids.Add(pair.Key);

            if (ids.Count == 0) return Array.Empty<EventStateDto>();

            if (ids.Count > MaxSeasons)
            {
                // Worth keeping first, then newest first. Ordinal is calendar order for a cycle
                // id and an arbitrary-but-stable order for an authored one, which is all this
                // needs: the rule has to be deterministic, because two devices evicting
                // differently would each push rows the other had dropped and the merge would
                // resurrect them for ever.
                ids.Sort((a, b) =>
                {
                    bool owedA = seasons[a].MayOwe, owedB = seasons[b].MayOwe;
                    if (owedA != owedB) return owedA ? -1 : 1;
                    return string.CompareOrdinal(b, a);
                });

                ids.RemoveRange(MaxSeasons, ids.Count - MaxSeasons);
            }

            ids.Sort(StringComparer.Ordinal);

            var rows = new EventStateDto[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                var state = seasons[ids[i]];
                rows[i] = new EventStateDto
                {
                    id = ids[i],
                    marks = state.Marks,
                    collectedGoal = state.FreeGoal,
                    premiumGoal = state.PassGoal,
                    pass = state.Pass,
                };
            }

            return rows;
        }

        static void Absorb(Dictionary<string, SeasonState> into, EventStateDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.id)) continue;

                var read = new SeasonState
                {
                    Marks = row.marks < 0 ? 0 : row.marks,
                    FreeGoal = row.collectedGoal < 0 ? 0 : row.collectedGoal,
                    PassGoal = row.premiumGoal < 0 ? 0 : row.premiumGoal,
                    Pass = row.pass,
                };

                // Two rows for one season is a malformed file, not two tracks. The larger of
                // each field wins for the same reason the merge takes the larger: every one of
                // them only rises, so the bigger number is the one that knows more.
                if (into.TryGetValue(row.id, out var held)) held.Absorb(read);
                else into[row.id] = read;
            }
        }

        /// <summary>Test seam: forgets everything, as a fresh install would.</summary>
        internal static void ResetForTests()
        {
            _seasons.Clear();
        }
    }
}
