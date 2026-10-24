# Gemfire revenue analytics: source audit and local implementation

The approved LevelPlay revenue bridge is implemented locally. Gameplay, mediation, placements,
purchases, consent, account settings and player data have not been changed. No build was released.

## Final closeout: 24 October 2026 — authoritative current status

All eight Android Google rewarded mappings are now verified and enabled. Each unit's actual SDK
load callback independently reported network `admob`, instance `Bidding`, and the already verified
Google Partner Bidding ID `ca-app-pub-4604184534032643/7655758570`:

| Placement | Verified LevelPlay unit |
| --- | --- |
| heart_refill | y06ctzokuzrk5me4 |
| coin_bonus | tcmo4y9h32c6l1o7 |
| win_bonus | jjrnpy9yqv47kspn |
| hint_refill | ta2i9vbn3y9hh66q |
| xp_boost | w1rzfmmmyrfhhv9r |
| challenge_play | dyu6bempmr8jje3c |
| turret_discount | hn39e91n5ru9lvaa |
| continue_video | pz2v7agzyfoxv4ny |

The existing coin mapping is retained. Every additional mapping pins both the observed LevelPlay
unit and exact Google partner ID. Standard AdMob and unknown sources remain excluded. Bounded
load/info-change diagnostics collect configuration evidence only; they never emit revenue or
authorize a source automatically. No mediation configuration or availability policy was changed.

ironSource Exchange did not appear in the available SDK load/info/impression callbacks and no
exact instance ID was present in the available local configuration. Its branch remains disabled;
the network's name or historical revenue alone is not an instance mapping. Investigation stops
here rather than changing mediation to force an impression or guessing an ID.

**Verification:** 184 selected Unity EditMode tests passed, zero failures/skips (34 revenue cases),
including all configured placements sharing one partner without collapsing legitimate events or
admitting an unverified standard unit. All managed compile passes and Android manifest checks
passed. The final Android build succeeded with zero errors and 220 warnings; all eight units
loaded on the connected Samsung Android 16 device. Full gameplay views of placements locked by
the QA player's progression were not forced; their eligibility/reward paths have automated coverage.

Native Firebase parameters and HTTP 204 delivery were verified with SDK-reported USD values,
including `0.0045421459`. A final genuine offline check disabled both transports, waited until
Android reported **no default network**, then captured a new eligible ad event at
`1792869894703` with value **0.0046500149 USD**. The QA app was force-stopped while offline.
After connectivity restoration/relaunch, that same event uploaded once, with HTTP 204 at
`1792869901.183`; no extra revenue replay or new first_open appeared in the observed restart window.
The payload retained `LevelPlay`, `admob`, `Bidding`, `rewarded_video`, `coin_bonus` and `BID`.

This is native payload/upload verification, not authenticated console/report reconciliation.
All eight mappings have SDK load verification and automated delivery coverage; live monetary
verification used the accessible coin offer. No synthetic monetary event or purchase was generated.

Final local QA APK SHA-256:
`CC1C3DACBAF45169F7741AC41B8335159A186DA9F8DB60598ED4510AD56FB8ED`.
Version 1.1.2/code 24, debug-signed ARM64, BuildOptions.None. Original signing/architecture/build
settings and device logging/network settings are restored; the QA app is stopped. No save, identity,
native cache, consent flow/account setting or purchase code was reset/changed. No build was released.

IAP engineering investigation is closed with no newly confirmed code defect; historical link timing
and a qualifying Play transaction remain external checks. See `ANALYTICS_CLOSEOUT.md`.

Evidence: `Library/AnalyticsQA/FinalRevenue/source-map.json`, `offline-result.json`,
`offline-capture.json`, `revenue-upload-parameters.json`, plus
`Library/AnalyticsQA/final-revenue-test-result.json` and `revenue-android-build-result.json`.

The sections below preserve earlier investigation and intermediate coin-only device results.
This final closeout supersedes their earlier enabled-mapping and offline-test limitations.

## Earlier implementation status: 24 October 2026

- The owner verified active Rewarded instance `google_bidding` in LevelPlay's Bidding section,
  assigned to nine mediation groups, rather than standard AdMob waterfall. The two contributing
  networks are Google Bidding and ironSource Exchange.
- A physical-device callback then established the SDK mapping for `coin_bonus`: mediation unit
  `tcmo4y9h32c6l1o7`, network `admob`, instance name `Bidding`, instance ID
  `ca-app-pub-4604184534032643/7655758570`. The SDK's instance ID here is the mapped Google ad-unit
  ID; its name differs from the dashboard's `google_bidding`. Android forwarding is enabled only
  for this exact network + mediation unit + partner unit ID. No display-name or network-wide
  fallback is enabled in the shipping configuration.
- Standard AdMob units and unverified instances are excluded. ironSource Exchange's exact instance
  identity has not been supplied, so its branch remains disabled. The other Google placements'
  partner unit mappings are also unverified and remain disabled. iOS/Editor forwarding is disabled;
  Editor mock impressions cannot create monetary events.
- `LevelPlayAdProvider` subscribes once per existing ad object to `OnAdImpressionDataReady`, before
  loading. It copies selected fields into immutable metadata and hands off through `AdRevenue`.
  It does not call Firebase, Unity gameplay APIs, or Telemetry fan-out on the SDK background thread.
- `AdRevenueTracker` matches reviewed ownership, validates finite non-negative USD revenue, and
  pumps at most four events per frame into the existing Firebase sink. Missing revenue is dropped
  with a fixed diagnostic; a real zero is retained. No micros conversion or reward-value estimate.
- The inbox is limited to 64 events and local deduplication to 512 `(auction ID, ad-unit ID)` keys.
  A rewarded ad unit has one format, so the unit identifies the format in this key. Auction IDs stay
  local and never enter Firebase parameters or diagnostics. Repeated callbacks, including excluded
  callbacks observed before a grant, are suppressed within this bounded process-local window.
- Existing collection eligibility is applied unchanged at capture and delivery. Withdrawal clears
  the bridge inbox and existing application delivery queue. Regrant cannot revive their events.
  This does not change the pending analytics-specific consent integration or purge native data.
- The existing Firebase queue handles safe pre-handoff retries and native offline persistence after
  handoff. An uncertain handoff is diagnosed without a second monetary submission. No receipt replay,
  persistent application revenue queue, identity reset, or manual purchase event was added.
- Diagnostics contain fixed categories and counts, emitted on the main thread with rate limiting.
  Unknown-source diagnostics also contain bounded network, instance name/ID and ad-unit ID so the
  dashboard mapping can be reconciled. These are advertising configuration, not advertising IDs
  identifying a person/device. No raw ILR JSON, player/device identifiers, auction IDs, tokens,
  or exception text is logged. Withdrawal clears the last unknown-source configuration sample.

Changed code: `Domain/Analytics/AdRevenue.cs`, `Telemetry/AdRevenueSources.cs`,
`Telemetry/AnalyticsSetup.Firebase.cs`, and the ILR listener in `Ads/LevelPlayAdProvider.cs`.
Ownership configuration is separate from `AdConfig`; it does not change ad serving.

Automated verification: 183 selected Unity EditMode tests passed, zero failures/skips, including
33 new `AdRevenueTests` cases plus analytics delivery, privacy, lessons, rewarded ads/shows and
store receipt/arrival/purchase fixtures. All managed compile passes succeeded, including the new
shipping Android Firebase/ownership branch. Android backup-manifest checks passed. This is not a
claim that the entire game suite or live ad/purchase tests passed.

Physical-device native payload/upload and restart checks passed as recorded below. Processed GA4
report reconciliation, other network/unit mappings and a native offline-ad case remain release
checks. The bridge cannot recreate historical revenue,
guarantee exactly-once delivery across process death, or include players excluded by collection.

### Device audit before enabling the exact mapping

The debug-signed Android QA build installed and upgraded without clearing app data. The visible
profile balances and progression survived the update. Collection explicitly reconciled false then
true on restart; rewarded units loaded, a normal coin-bonus ad played, and the existing 300-coin
reward was granted. No new prompt, purchase or gameplay path was introduced.

The callback originally failed the ownership check because the SDK uses `admob` / `Bidding`, not
the dashboard's `Google Bidding` / `google_bidding`. The guard excluded it instead of guessing.
The pinned partner unit above was obtained from that callback after the owner verified the bidder
configuration. Other mappings are not extrapolated from it.

Native logs also contain automatic `ad_reward` events with an `ad_unit_id(_ai)` parameter. A text
search for `_ai` anywhere in a log line incorrectly classifies those as impressions. Matching the
event name and inspecting the package-attributed upload bundles found no automatic `ad_impression`
revenue event in this sample before forwarding was enabled. Keep that distinction when checking
for duplicates; general DebugView event visibility is not confirmation of revenue parameters.

### Earlier Android device result (coin-only mapping)

Samsung SM-S938B, Android 16; local debug-signed ARM64 QA APK, version 1.1.2/code 24,
BuildOptions.None. Native build succeeded with zero errors and 220 warnings. Original production
signing/architecture/build configuration was restored afterward. Final APK SHA-256:
`D670D81EF8A4563CDCB1C910BBA642C7760CB2A46F42B1A5FC0873C08FBFD5F4`.

The enabled Google coin-bonus mapping produced these native Firebase upload records:

| Event timestamp (ms) | Event | USD value | Origin |
| --- | --- | --- | --- |
| 1792867264087 | ad_impression | 0.005224296 | app |
| 1792867396016 | ad_impression | 0.012118236 | app |
| 1792867540710 | ad_impression | 0.002913908 | app |

All three carried `ad_platform=LevelPlay`, `ad_source=admob`, `ad_unit_name=Bidding`,
`ad_format=rewarded_video`, `currency=USD`, `revenue_precision=BID` and
`ad_placement=coin_bonus`. Package-attributed upload responses were HTTP 204. Different per-ad
values confirm the transport is not substituting the game's 300-coin reward or a fixed estimate.
These are SDK-reported bid values, not a promise of finalized settlement revenue.

After force-stop/relaunch, collection explicitly reconciled false then true, the same three
revenue records remained, and no additional revenue replay or first_open was observed in that
restart window. The existing completed-ad reward stayed 300 coins and saved progression/balances
survived upgrades and restarts. No native crash was observed. Purchase flows were not exercised on
this debug-signed build; purchase regression coverage here is automated.

Native offline-ad delivery is **unverified**. Both transports were temporarily disabled and
restored, but the recorded impression preceded that offline window; a subsequent attempt stopped
when the expected QA screen was not foreground. It is not counted as an offline delivery pass.
The bridge/retry/withdrawal/restart failure scenarios passed deterministic tests, and the earlier
session verified native Firebase offline delivery for level events. A new eligible ad impression
captured offline still needs its own native persistence/reconnect test.

The owner confirmed general DebugView visibility but did not independently confirm this new event's
parameter panel. This report therefore claims native payload and upload verification, not direct
authenticated DebugView or processed-report verification. No console access/settings were changed.
QA logging properties were restored to their original values, Wi-Fi/mobile data restored, and the
QA app stopped. No app data, Firebase identity, native event cache or player save was reset.

Evidence is under ignored `Library/AnalyticsQA/revenue-test-result.json`,
`revenue-android-build-result.json`, `revenue-upload-parameters.json`,
`revenue-device-result.json`, and `revenue-offline-network-result.json`. Only selected analytics
parameters/configuration and aggregate diagnostic data are retained; no raw logcat is persisted.

## Sources confirmed during the initial audit

| Source | Evidence |
| --- | --- |
| Firebase Android registration | Firebase API confirms active Gemfire app, package com.tekoworld.glimmergroove, app ID 1:310440655052:android:532b286945f36891492a69 |
| Firebase project | glimmer-groove-1cd60 |
| GA4 property / Android stream | Firebase API confirms property 552601530 / stream 15651226418 |
| Google Play | Owner confirmed Gemfire linked, Google Analytics revenue sharing enabled |
| AdMob | Owner confirmed Gemfire Android and iOS linked; Android app ID in the build is ca-app-pub-4604184534032643~9319978435 |
| Installed SDKs | LevelPlay Unity 9.5.0; Firebase Unity 13.15.0 / Android Analytics 23.2.0; generated Android build uses Billing Library 9.0.0 |
| Application revenue writers | No manual ad_impression, custom_ad_impression, purchase or in_app_purchase writers found in game or backend source |
| Store events | Existing store_purchase_* events describe checkout/grants; they do not supply monetary value/currency and must not be counted as another purchase revenue feed |
| LevelPlay callbacks | Initial provider had no impression-level revenue subscription; the approved local change adds the supported per-ad-object callback. The global callback is obsolete |

Google documents automatic Android purchase measurement with Google Play linking, and automatic
AdMob measurement for standard AdMob ad units with the AdMob/Firebase integration. Partner Bidding
is a separate case: Google's current documentation says its impression revenue is no longer sent
through AdMob and publishers must obtain it through their mediation platform. A linked AdMob app
alone therefore does not establish revenue coverage for LevelPlay's Google bidder.

## Updated evidence: 1-24 October 2026

The owner checked GA4 and reported:

| Stream | Verified report result |
| --- | --- |
| Android 15651226418 | Receiving traffic; no matching ad_impression, in_app_purchase or purchase events in the exploration; no Android purchase events in the standard Events report |
| iOS 15650918562 | Receiving traffic; the property's 2 in_app_purchase events and USD 17.02 purchase revenue were attributed to this stream |
| Advertising revenue | Total reported ad revenue USD 0.00 |
| Standard Events report | 2 in_app_purchase events and 2 purchasing users across the property, USD 17.02; this confirms the earlier purchase result without exploration filters |
| GA4 data filters | Internal Traffic is Testing, not excluding; Developer Traffic is not configured |
| LevelPlay Android | USD 8.40 advertising revenue from 1,238 impressions; Google Bidding and ironSource Exchange contributed, with Google Bidding generating nearly all revenue; exact network split was not supplied |

These are owner-confirmed report observations, not direct GA4 API results. Zero historical ad
revenue does not establish that standard AdMob automatic reporting is disabled or safe to duplicate.
The Google Play-to-Analytics link/revenue-sharing activation date is unknown.

The advertising measurement gap is confirmed by actual served impressions and revenue. The initial
game had no LevelPlay impression-revenue listener. Google's Partner Bidding documentation explains why
linking AdMob does not fill this gap for Google's LevelPlay bidder. Inspect the existing Google
ad units' serving type and LevelPlay instance mapping before enabling the new writer; do not change
those units. A blanket exclusion of all Google/AdMob-labelled LevelPlay callbacks would omit
the main revenue source and is not the recommended implementation.

The owner supplied five Processed Google Play orders. A separate read-only Firestore audit
inspected 59 Google receipt records, without retaining order IDs, player IDs or tokens, and found
these five matching server-validated grants in the October UTC date range:

| Date | Product | Play amount supplied by owner | Server test flag | Server revocation |
| --- | --- | --- | --- | --- |
| Oct 2 | gg_gems_1 | TRY 56.99 | false | none recorded |
| Oct 3 | gg_gems_1 | TRY 56.99 | false | none recorded |
| Oct 7 | gg_bundle_starter | EUR 3.09 | false | none recorded |
| Oct 12 | gg_gem_pass_7d | TRY 119.99 | false | none recorded |
| Oct 14 | gg_bundle_starter | BRL 9.99 | false | none recorded |

Google receipt validation accepts only purchaseState=0, obtains the order ID from Google's
response, and marks purchaseType=0 as sandbox. The matching grants strengthen the purchase
discrepancy: this is not just a lack of recorded transactions. They do not prove collection was
permitted on those devices, exclude promotional purchases, or independently establish current
Play refund status. The server does not retain charged value/currency or the full purchaseType.
Do not convert these receipt grants into another in_app_purchase feed.

## Android purchase investigation: findings and limits

| Check | Finding |
| --- | --- |
| Package / app / stream mapping | Current Firebase config, generated Android resource and backend package match Gemfire; Android app ID is stable across inspected pre-October and October revisions |
| SDK present in source history | Analytics package and telemetry version define were added September 13, before the listed orders; exact installed buyer versions remain unknown |
| Packaged initialization | Current merged manifest contains FirebaseInitProvider and AnalyticsConnectorRegistrar, Billing permission/service visibility, and Billing Library 9.0.0; minification is off |
| Collection state | Manifest defaults false; managed enable depends on the unchanged advertising-derived rule and successful shared Firebase readiness |
| Timing | Store connection/fetched purchases can start before UMP preparation completes, so startup purchase recovery can occur while collection is disabled |
| Ad-provider dependency | Existing consent preparation still originates in RewardedAds.StartAsync, which returns when no provider is installed. This is a possible collection-availability dependency, not a demonstrated cause for these five purchases |
| Current device proof | Prior DebugView tests verified first_open, lessons and levels; cold restart explicitly applied false then true. This proves general delivery on the QA build, not automatic purchase detection on the historical buyer builds |
| Purchase instrumentation | Google Play automatic events bypass the custom game-event queue. Fixing that queue cannot itself repair or replay missing native IAP events |
| GA4 filtering | Owner confirmed the standard Events report and non-excluding Internal/Developer filter settings; these do not explain the missing Android purchases. Account-side created/modified event rules remain uninspected |
| Direct reporting access | GA4 Data API is scope-blocked. The expected BigQuery export dataset analytics_552601530 returned 404, so it was unavailable for raw historical-event verification; no export was created |

**The historical IAP root cause is not yet established.** Plausible branches are integration/link
timing, collection eligibility/timing on the purchasing devices, or native SDK purchase detection.
The reported data filters and exploration constraints have been ruled out as explanations by the
owner's checks. Google states ingestion starts when the Play link is created; its current enabled
state does not establish that ingestion was enabled for these October orders. No reviewed evidence
currently identifies a specific Firebase/Billing compatibility bug. Currency used for payment must
not be used to infer location or consent.

The next discriminating check is one purchase on an existing Play-installed build, with collection
state and native FA/FA-SVC logs observed before and after the transaction. Confirm the exact Android
stream in GA4's Google Play links as part of that check. Licensed test purchases can validate client
behavior but are excluded from purchase reporting; do not require a test order to produce monetary
revenue in GA4. Reconcile against an eligible real order for final revenue sign-off. Use the current
Play build or a separately approved internal-track build; no build is uploaded as part of this
investigation, and no existing app data is cleared.

- No native purchase event despite enabled collection: investigate SDK/Billing detection logs.
- Native purchase event logged/uploaded but absent from reports: investigate filtering, test-order
  handling, stream/link mapping and backend processing, using DebugView for separation.
- Collection disabled: identify the existing eligibility/readiness reason; do not bypass consent
  to force a revenue event or replay historical receipts as new transactions.

## Reporting-access limitation

Read-only Firebase metadata requests succeeded. Aggregate GA4 revenue queries failed with
403 ACCESS_TOKEN_SCOPE_INSUFFICIENT: the existing Google Cloud token lacks Analytics reporting
scope. APIs, permissions and account settings were not changed.

The attempted reports filter stream 15651226418, 1-24 October 2026, and request:

- Event counts, event value, total ad revenue and gross purchase revenue for ad_impression,
  custom_ad_impression, in_app_purchase and purchase.
- ad_impression counts and ad revenue broken down by adSourceName and adFormat.

Local diagnostic script/results are under ignored Library/AnalyticsQA/revenue_audit.py and
revenue-audit-result.json. They retain metadata and aggregate results/errors, not credentials
or player-level events. A linked account or an event count alone does not prove correct revenue.

## Approved implementation design

1. Preserve Google Play automatic in_app_purchase tracking. Add no manual purchase or
   in_app_purchase event to StoreService, confirmation, restoration or server redemption.
2. Assign one revenue source per existing mediation instance/ad-unit serving type:
   - Google Partner Bidding through LevelPlay: forward LevelPlay ILR. Google's documentation
     explicitly excludes Partner Bidding from the AdMob impression-revenue feed.
   - Other LevelPlay networks without an existing automatic Firebase revenue feed: forward
     LevelPlay ILR.
   - Standard AdMob units, including any waterfall instances: retain the automatic AdMob route
     and exclude them from manual forwarding. Investigate that route separately if it is missing.
   Google's exact `google_bidding` instance was verified by the owner as described above. Do not classify
   ownership solely from the AdNetwork display name, since that does not establish serving type.
   Unknown/ambiguous Google instances get a bounded diagnostic instead of a second revenue writer.
   Keep both existing Firebase/AdMob links and advertising configuration unchanged.
3. For the LevelPlay-owned revenue above, subscribe once to OnAdImpressionDataReady on each
   existing rewarded-ad object immediately after construction. Do not also subscribe globally,
   to reward completion, or to ad-close callbacks for revenue. No new ad objects or placements.
4. Marshal immutable, bounded snapshots from the SDK background callback to the existing main
   thread analytics pump. Check collection eligibility at capture and delivery, and reject
   stale buffered callbacks across withdrawal/regrant. Retain existing collection controls;
   this integration does not resolve the separately pending analytics-consent issue.
5. Deduplicate repeated callbacks locally using the SDK auction ID plus ad-unit/format identity,
   with a bounded cache. Auction IDs remain local. Handle missing IDs explicitly with a diagnostic;
   never claim GA4 automatically deduplicates ad_impression using a custom ID. Do not replay an
   uncertain Firebase handoff. Process-local deduplication is not an exactly-once server guarantee.

Do not also wire Google's paid-event callback, the obsolete global LevelPlay callback, reward
completion or server receipt grants into revenue. Check existing GA4 created/modified-event rules
before enabling forwarding. Those controls prevent a second writer; the local callback cache
alone cannot deduplicate a separate native automatic event or an account-side revenue feed.

Proposed Firebase event: **ad_impression**, once for each eligible impression whose revenue is
not already reported by the automatic source.

| Parameter | Value |
| --- | --- |
| ad_platform | LevelPlay |
| ad_source | SDK AdNetwork |
| ad_unit_name | SDK InstanceName, with bounded ad-unit metadata fallback |
| ad_format | SDK AdFormat |
| value | SDK Revenue as a finite, non-negative double in USD; no per-reward estimate or scaling |
| currency | USD, the currency documented for LevelPlay ILR |
| revenue_precision | SDK Precision, when present, to distinguish BID/RATE/CPM |
| ad_placement | Existing placement ID, optionally, for a bounded placement breakdown |

Missing/invalid revenue must not be invented or replaced by a fixed rewarded-ad value. A reported
zero is different from missing revenue. LevelPlay's SDK value can itself be estimated depending
on its precision; it is the platform's per-impression figure, not guaranteed final settled earnings.

Firebase recommends ad_impression for GA4 ad-revenue metrics. Unity's current example uses
custom_ad_impression; the proposed integration uses Firebase's standard event and does not emit
both names for the same impression. No separate bridge plugin or SDK upgrade is required.

## Verification still required before implementation / sign-off

- Check existing GA4 created/modified event rules or other external writers that could duplicate
  standard revenue events; those account-side rules were not visible to this audit.
- The owner verified the Google bidder instance and named ironSource Exchange as the second
  contributing network. Obtain ironSource Exchange's exact SDK instance identity before enabling
  that branch. Optionally pin Google's numeric instance ID when supplied. No account settings need to change
  for the proposed LevelPlay Partner Bidding bridge. If standard AdMob units are present, inspect
  AdMob's impression-level ad revenue setting read-only; any change requires separate approval.
- Compare LevelPlay/AdMob totals by network and unit with the same reporting time zone. Historical
  Android totals are confirmed, but per-network values and live callback field identities are not.
- For ad events, verify actual numeric value/currency and one counted impression per SDK callback,
  including Google Partner Bidding, the second network, and any standard AdMob automatic events.
  Check processed GA4 impression counts as well as revenue so a zero-value automatic event cannot
  hide a duplicate impression. Do not assert cross-source deduplication from a local ID cache.
- For Google Play, verify in_app_purchase product_id, quantity, value/price and currency against
  an actual store transaction. Reported totals alone cannot establish raw event currency.
- Test one live/test-enabled impression per configured network, offline upload and app restarts;
  compare SDK ILR, DebugView and processed GA4 reporting. Do not click live ads for testing.
- Use an approved Play-signed/internal-track build and licensed tester for purchases, deferred
  payments and restoration. Confirm restoration/re-delivery creates no extra purchase revenue.
  Sandbox transactions may be excluded from monetization totals.
- After implementation, test malformed/null revenue, duplicate callbacks, bounded buffers,
  background-thread capture, collection disabled, withdrawal/regrant and uncertain handoffs.
- GA4 can only include events allowed by the existing collection controls. Full equality with
  LevelPlay/Play revenue is not expected if collection is unavailable for some players, or if
  time zones, reporting delays, SDK revenue precision and finalized earnings differ. This bridge
  neither resolves the pending analytics-specific consent integration nor backfills old revenue.

## Official references

- [Firebase ad revenue](https://firebase.google.com/docs/analytics/measure-ad-revenue)
- [LevelPlay Unity ILR integration](https://docs.unity.com/en-us/grow/levelplay/sdk/unity/impression-level-revenue-integration)
- [LevelPlay ILR fields and precision](https://docs.unity.com/en-us/grow/levelplay/platform/fundamentals/impression-level-revenue-sdk-api)
- [Firebase Unity purchases](https://firebase.google.com/docs/analytics/unity/measure-in-app-purchases)
- [Google Play linking](https://support.google.com/firebase/answer/6392038)
- [Google Play ingestion timing](https://support.google.com/analytics/answer/11548051)
- [AdMob linking](https://support.google.com/firebase/answer/6387949)
- [Partner Bidding revenue exception](https://support.google.com/admob/answer/11322405)
- [LevelPlay Google Partner Bidding setup](https://docs.unity.com/en-us/grow/levelplay/sdk/unity/networks/guides/google-bidding)
