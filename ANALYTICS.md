# Firebase delivery and consent integration

## Scope of this change

The delivery fix changes no game events, reward paths, purchases, ad callbacks, consent prompts,
progression, save schema or server calls. Firebase installation and the bounded retry pump run
independently of the ad provider.

The separately approved LevelPlay revenue bridge is documented in `REVENUE_ANALYTICS.md`.
It adds `ad_impression` for reviewed Android bidder instances through this same delivery controller;
it adds no manual purchase events and does not change the eligibility rule described here.

**Superseded (2026-10-26):** the legacy rule that gated collection on
`AdPrivacySignals.AllowsPersonalisation` (`ApplyCollectionRule`) is gone. Analytics consent is now
Google's consent mode, read per purpose from UMP - see "Analytics consent: Google consent mode" below.
The delivery guarantees that follow are unchanged; "collection allowed" now means "not a
child-directed install", and what an event may carry is decided by the consent-mode purposes.

## Delivery guarantees and limits

- Native collection state starts unknown in each process. After the shared Firebase dependency check,
  the requested state is explicitly applied, including an initial false. A failed setter invalidates
  the cache and retries with backoff; it cannot open custom delivery prematurely.
- Events are eligible only if collection was allowed when they entered this sink. Excluded events are
  never buffered for a future grant. Withdrawal immediately clears pending application events and
  closes delivery, even if the native disabling call fails and must be retried.
- The queue holds at most 64 scalar snapshots, each with at most 25 bounded properties. A frame pumps
  at most four events per pump, normally once a frame. Retry delays are 1, 2, 4, 8, ... seconds,
  capped at 60 seconds. The patch adds no direct network requests, disk writes, worker threads,
  synchronous waits or backend resources; Firebase retains ownership of its existing upload path.
- Known failures before `LogEvent` retain the head for up to five attempts. A permanently bad payload
  is discarded with a `RetryLimit` diagnostic so later events can proceed.
- Successful handoffs are removed once. `LogEvent` provides no receipt or idempotency guarantee. A
  thrown call is reported as `UnconfirmedHandoff` and is not replayed: it might already have been
  accepted, and blind replay could duplicate a purchase or revenue event.
- Firebase owns offline persistence/upload after handoff. This application queue is process-local;
  events still waiting for the SDK or a retry can be lost on process termination. No claim of durable
  exactly-once delivery is made. Automatic `first_open` and purchase events bypass this queue.
- The Android manifest false value is a default, not a reset of Firebase's persisted override. The
  first managed reconciliation occurs only after dependencies resolve. A strict gate before native
  SDK startup would require a separate Android startup/consent integration.
- The shared dependency task is not restarted on failure, because other Firebase services share it.
  Dependency failures leave delivery closed with a diagnostic; remediation needs device evidence.

Diagnostics use fixed categories, aggregate counts and pending counts. They never include event
names, payloads, player/device identifiers, receipts, consent strings or exception messages. Repeated
diagnostics are logged on the first occurrence and powers of two, keeping volume bounded.

## Analytics consent: Google consent mode (approved by the owner, 2026-10-26)

Analytics no longer waits for the advertising answer, and no event is dropped for want of one.
It runs in Google's **consent mode, advanced**: collection is on, and four purposes say what an
event may carry. With `analytics_storage` denied, Analytics on a mobile app sends events without any
device or user identifier and uses them for behavioural modelling; granted, they are full events.
So EEA players are measured, unidentified, from the first launch - before and without the consent
form - and the form can wait for the hub (invariant 55) without the funnel in front of it going dark.

How it is built (invariant 55b):

- **Defaults ship denied.** `GlimmerMeasurement.androidlib` and `IosPrivacyPlist` set the four
  `google_analytics_default_allow_*` / `GOOGLE_ANALYTICS_DEFAULT_ALLOW_*` flags to false, beside the
  existing collection-off flag, so nothing the native SDK does before managed code runs can carry an
  identifier.
- **The purposes come from UMP itself.** `ConsentModeStore` reads `UMP_consentModeValues` (Android
  default SharedPreferences, iOS standard user defaults; digits `ad_storage`, `ad_user_data`,
  `ad_personalization`, `analytics_storage`; `0` not ready, `1` granted, `2` denied, `3` not
  applicable, `4` unconfigured) with every privacy snapshot. `AnalyticsConsent.For` grants a purpose
  only on `1` or `3`, or, when no digit speaks (`0`/`4`), when UMP has resolved the player outside
  every regulated region. An advertising answer never grants analytics. Do-not-sell and
  child-directed withhold `ad_user_data` and `ad_personalization`; child-directed collects nothing.
- **Order (2026-10-28).** UMP's answer, then `setConsent`, then collection on (`MeasurementGate`,
  `AnalyticsOpening`). Firebase logs `first_open` and `session_start` the moment collection turns on;
  Android 1.1.4 turned it on as soon as the dependency check passed, with every purpose still denied,
  seconds before the splash's answer granted them, so both went out as cookieless pings and GA showed
  no first opens for the build. Now delivery opens on the first of: an answer committed by
  `AdPrivacy`; the splash's refresh finishing with the form owed (opens denied - what an EEA player
  is owed until the form); or UMP silent for 3 s after the splash asked it (20 s if nothing ever
  asks), which opens denied and applies the answer when it comes. Events wait in the delivery queue
  meanwhile. Every later privacy change is pushed the same way; `setConsent` persists across
  launches. The log says `[Analytics] consent mode applied: ...` and then `[Analytics] opened on the
  consent answer` (or `... before the consent answer; every purpose denied until it arrives`).
- **AppsFlyer on the same gate.** Consent data and `anonymizeUser` before `startSDK`; events raised
  before the start (the tutorial's lessons) are held, at most 32, and flushed after it under the
  consent it applied. A time-out starts it anonymised as a GDPR player; an owed form waits for the
  form, because an install reported anonymised is never attributed (the owner, 2026-10-28). A later
  answer is re-applied. Log: `[Attribution] started (identified|anonymised...)`.
- **`l5_reached`** (AppsFlyer and Analytics): the first clear of the level `progression.json`
  names in `attribution.l5Reached` (`s01_warlordsgate`), read off the save's record before the run,
  so once per account with nothing stored. `af_level_achieved` is unchanged.

**Required console step (do this before the release reaches the stores):** AdMob ▸ Privacy &
messaging ▸ European regulations ▸ Settings ▸ turn **consent mode** on and include the
**analytics** purpose. Without it UMP reports `4` for `analytics_storage` in the EEA, and EEA players
stay modelled-only even after agreeing. UMP versions in use meet Google's minimums (Android 4.0.0 ≥
3.2.0, iOS 3.1.0 ≥ 3.0.0).

References:
- https://developers.google.com/funding-choices/fc-sdks
- https://developers.google.com/admob/android/privacy/consent-mode
- https://developers.google.com/tag-platform/security/guides/app-consent?consentmode=advanced&platform=android
- https://support.google.com/analytics/answer/9976101

## Validation

Final local verification on 24 October 2026:

- All managed compile passes succeeded, including Firebase telemetry, UMP, Android/iOS variants,
  and the test assembly. The Android manifest validator passed.
- 150 selected Unity EditMode tests passed (0 failures/skips): analytics delivery, privacy,
  lessons, rewarded-ad behavior, store receipts/arrival, and utility/companion purchases.
- An expanded 169-test run reported one failure in
  `StoreTests.TheBuiltInLadderMatchesTheShippedContent`: built-in `gg_coins_1` credits are 2,500,
  while shipped content grants 7,500. It also fails in isolation. StoreCatalog.cs, StoreTests.cs,
  and progression.json match HEAD, confirming the mismatch predates the analytics changes.
  Purchase values and tests were not changed. This is not a full-suite green result.
- The connected Android phone's installed APK exactly matched the previously tested local QA
  APK. Another cold restart explicitly applied false, then true after the existing UMP refresh.
  Logging settings were restored and the QA app was stopped; no data was reset.
- Earlier actual-device testing and owner-confirmed DebugView showed first_open, level completions,
  lessons, and offline event delivery across a force-stop. The existing privacy-form rejection
  failed to disable analytics; that consent-integration defect remains pending.
- Live ad delivery/revenue, Play Billing purchases/restoration, supported-region consent behavior,
  and the native startup window are not signed off by these tests. No build was released.

The current local Android run and repeatable DebugView procedure are recorded in
[ANDROID_ANALYTICS_TESTING.md](ANDROID_ANALYTICS_TESTING.md), including the reproduced consent
withdrawal gap and the checks still awaiting device/console verification.

`python -B Tools/verify/tests.py AnalyticsDeliveryTests` exercises SDK/eligibility ordering, restarts,
withdrawal, failed setters, bounded batches/payloads, FIFO retries, poison events, ambiguous handoffs,
reentrant callbacks and bounded diagnostics. The compiler also builds `telemetry-firebase` with the
real Firebase define and plugin references, alongside the package-absent build.

Android device verification is still required on the current production SDK and a Play-installed
candidate build. Compare native FA/FA-SVC logs and Firebase DebugView with the scripted actions:

```powershell
adb shell setprop debug.firebase.analytics.app com.tekoworld.glimmergroove
adb shell setprop log.tag.FA VERBOSE
adb shell setprop log.tag.FA-SVC VERBOSE
adb logcat -v threadtime FA:V FA-SVC:V Unity:I '*:S'
```

- Fresh installs with early and delayed eligibility: check exactly when collection is applied,
  whether a deferred `first_open` appears once, and post-enable lesson/level events.
- Returning users: force-stop/relaunch without resetting UMP; verify reconciliation and no duplicate
  first-open event. The registered development test device forces EEA; release builds do not.
- Withdrawal and regrant: verify immediate custom-delivery closure, native state changes, and no
  replay of the cleared queue. Actual analytics-purpose withdrawal awaits the approved integration.
- Offline after enable: complete known distinct levels, restart/reconnect and verify Firebase's
  native persistence. A normal network outage does not necessarily delay SDK readiness.
- Analytics-only fault injection: delay readiness, fail a setter, fail preparation, simulate an
  uncertain native handoff, and exceed 64 pending events. Verify backoff and each diagnostic.
- Test ads and licensed test purchases/recovery: verify the existing custom events and automatic
  purchase events separately. No new purchase/revenue events are emitted by this change. Sandbox
  transactions may be excluded from monetization reports.
- Disable debug mode and repeat a normal-upload case; inspect GA4 traffic filters and reporting
  delays before treating absent report rows as lost events.

```powershell
adb shell setprop debug.firebase.analytics.app .none.
```
