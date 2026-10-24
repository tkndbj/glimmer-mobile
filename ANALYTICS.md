# Firebase delivery and consent integration

## Scope of this change

The delivery fix changes no game events, reward paths, purchases, ad callbacks, consent prompts,
progression, save schema or server calls. Firebase installation and the bounded retry pump run
independently of the ad provider. Consent preparation and eligibility retain the existing product
behavior pending approval of an analytics-specific consent integration.

The separately approved LevelPlay revenue bridge is documented in `REVENUE_ANALYTICS.md`.
It adds `ad_impression` for reviewed Android bidder instances through this same delivery controller;
it adds no manual purchase events and does not change the eligibility rule described here.

**The legacy `AdPrivacySignals.AllowsPersonalisation` mapping remains in `ApplyCollectionRule`. It is
not proof of analytics permission. This patch must not be described as completing consent integration.**

The approved consent-related scope is the reliability fix only. The owner has not approved regional
analytics defaults, changes to AdMob settings/messages, additional dialogs, native consent
startup changes, or analytics-data/identity resets. None were implemented. Existing eligibility
and analytics availability are retained; no global-disable workaround was added.

Explicit analytics consent remains blocked by the disabled AdMob Consent Mode purposes and the
absence of an approved worldwide regional policy. Changing the current eligibility to require
an unavailable purpose would suppress measurement for existing players; that change is pending.

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

## Product approval still required: analytics-specific consent

No separate analytics permission source exists in the current implementation. `CanRequestAds()`
means advertising requests may proceed; it does not establish `analytics_storage` permission.

The smallest candidate is Google's UMP Consent Mode integration on the existing CMP message:

Google documents this mechanism for existing EEA, UK and Swiss choices. For other supported regions,
the analytics policy must define applicable opt-outs and regional defaults explicitly; the current
advertising geography flag is not sufficient evidence of analytics permission.

1. Review the configured consent message, analytics purpose and applicable regional policy. Decide
   whether the existing message can cover analytics without adding a separate player-facing prompt.
2. Enable the appropriate Consent Mode/analytics purpose in AdMob Privacy & messaging. Confirm native
   UMP and Firebase initialization ordering and purpose updates on real supported devices.
   The analytics checkbox is under European regulations > Settings and appears only when advertising
   consent mode is enabled. Google applies these settings across the account's configured European
   messages, so inspect the existing account configuration and approve the scope before changing it.
3. Read Google's documented `UMP_consentModeValues`, whose fourth digit is `analytics_storage`:
   `1` granted, `2` denied, `0` unavailable, `3` not applicable, `4` purpose unconfigured. Unknown,
   malformed or unconfigured data must not manufacture a grant. Treatment of `3` and regional defaults
   requires an explicit product/privacy decision.
4. Feed that verified decision into `SetCollectionAllowed`, separately from all advertising signals.
   Refresh it at consent updates/withdrawal and make permission acquisition independent of mediation
   availability. Leave the existing ad policy and SDK consent purposes intact.

References:
- https://developers.google.com/funding-choices/fc-sdks
- https://developers.google.com/admob/android/privacy/consent-mode
- https://developers.google.com/tag-platform/security/guides/app-consent?consentmode=basic&platform=android
- https://support.google.com/admob/answer/16053245?hl=en

Changing to that signal while the dashboard purpose is absent could stop measurement for affected
players. Automatically enabling it, adding a new consent prompt, or assuming that a region permits
analytics would exceed this reliability fix. Until approved, consent eligibility/prompt timing and
the existing no-ad-provider consent behavior remain unchanged. Pre-consent tutorial activity cannot
be recovered by queue replay.

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
