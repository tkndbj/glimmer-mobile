# Normal leaderboard

Normal v1 sums each active chapter level's independent saved bests: 1,000 completion
points, 100 per star (up to three), and an efficiency bonus capped at 200:
`clamp(truncate(400 * (target - bestMoves) / target), 0, 200)`.
Missing moves earn no bonus. Uncleared, unknown, disabled, Infinite and Shuffle
levels contribute nothing. Clear counts and rewards do not affect the score.

The client displays `floor(total / 100)` for both leaderboard rows and the personal
score: 245,840 raw points display as 2,458. Convert after summing, never per level.
Stored totals and ordering retain their precision; this display scale needs no
backfill or backend migration. The current 250-level displayed maximum is 3,750.

Targets in `progression.json.normalScoring` are frozen three-star move thresholds.
Difficulty changes never rewrite existing targets. `Tools/normal_scoring.py --check`
validates eligibility; `--write` appends targets for new Normal levels. A formula
change needs a new version and migration. C# and TypeScript run the shared vectors.
The inputs remain client-reported achievements; the formula does not attest gameplay.

`normalCards/{uid}` holds the server's public projection. It is separate from
`groves` to preserve Infinite's sampled percentile population. The same name,
moderation and loadout builder serves both. Client saves, wallets and existing
boards' scoring, live placement and refresh cadences are unchanged.

`publishNormalProgress` follows synced bests, opt-outs and profile changes, including
old clients that do not know the Normal tab. Duplicate events recompute; they never
increment a total. A transaction verifies the save snapshot is still current before
writing a card. Identical Normal cards are not rewritten. Deleted saves trigger a
final public cleanup to close in-flight publication races.

`publishNormalLeaderboard` publishes the indexed top 100 once per minute, reading
the query and writing its snapshot in one transaction. Score ties follow Firestore's
document-id descending order. Clients read one document, cache for 60 seconds, and
refresh only while Normal is visible. Failed refreshes keep the cached snapshot.
The personal score can update before the shared snapshot.

Refreshes adopt new timestamps without rebinding identical cards. Changed rows bind
in place; count changes resize the existing grid, preserving scroll and suppressing
entrance animations. Publishing the player's card updates the personal score without
fetching the shared snapshot ahead of its next scheduled refresh.

## Validation

```powershell
npm.cmd --prefix firebase/functions test
python Tools/verify/tests.py
python Tools/verify/loc.py
python Tools/normal_scoring.py --check
node firebase/seed/seed-config.mjs --check
node firebase/e2e/rules-budget.mjs
```

`functions/test/normal-emulator.mjs` additionally exercises actual transactions,
backfill failure/retry, leases, multiple pages, existing-board isolation, Normal-only
name moderation and client access rules. It refuses to run without a Firestore
emulator and a `demo-` project. Example after starting the emulator with this ruleset:

```powershell
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8185'
$env:GCLOUD_PROJECT = 'demo-normal-leaderboard'
node firebase/functions/test/normal-emulator.mjs
```

## Production rollout

1. Pass validation, including the required 250-expression save-rule headroom.
2. `node firebase/seed/seed-normal.mjs --apply` patches only the new scoring field
   on `config/progression`; it refuses changes/removals of previously frozen targets.
3. Deploy the additive Firestore rules and `normalCards` single-field indexes.
4. Deploy `publishGrove`, `publishGroveBoards`, `publishGroveRanks`, `repairGroveCards`,
   `reportKeeper`, `deleteAccount`, `publishNormalProgress`,
   `publishNormalLeaderboard`, and `backfillNormalLeaderboard` together. Updating
   the old board publishers is necessary: their previous prune list lacks `normal`.
5. `node firebase/seed/seed-normal.mjs --verify` checks the live targets. Check
   `leaderboards/normal` for a recent `builtUnix` and ordered rows.
6. Release the client build containing the tab. Old clients continue working.

`node firebase/seed/normal-status.mjs` reads a compact live summary without listing
player identities. `--advance` starts one bounded backfill page through its Scheduler
job, `--publish` starts a fresh snapshot, and `--assert-done` verifies completion,
unique ordered rows, scoring version and a snapshot no more than three minutes old.

The backfill runs every five minutes, 200 saves per page, four publishers at a time.
Its durable checkpoint is `maintenance/normalLeaderboardV1`: `cursor`, `processed`,
`done` and a nine-minute lease. Only successful pages advance the cursor. Retried
pages derive the same totals. Opted-out and non-Normal saves are skipped; no player
save or wallet is changed. After `done: true`, the job does no further scan; its
Cloud Scheduler job may be paused to remove the remaining checkpoint checks.

Account deletion and opt-out remove both public projections and scrub all board
documents. The moderation desk also removes both public cards. Normal's minute
publisher picks up name changes and removals on its next run.
