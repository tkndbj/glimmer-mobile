# Gemfire Android analytics verification

## Results from 24 October 2026

Final closeout: the reliability implementation was retained without adding consent changes.
150 selected analytics/privacy/advertising/purchase tests passed, and all managed compilation
passes plus the Android manifest validator passed. The broader 169-test run reported the
pre-existing StoreTests catalogue mismatch (`gg_coins_1`: fallback 2,500 versus shipped 7,500),
also reproduced alone; all involved files match HEAD. No purchase values were changed.
See [ANALYTICS.md](ANALYTICS.md) for the approved scope and exact remaining blockers.

Tested locally on the connected Samsung Galaxy S25 Ultra, Android 16 / API 36.
The owner removed the Play installation and authorized this disposable fresh install.
No build was published and no consent flow, CMP configuration, gameplay or progression code was changed.

Candidate: `Builds/Android/AnalyticsQA/Gemfire-analytics-qa.apk`, version 1.1.2 / code 24,
ARM64, debug signed, **Development Build off**. This distinguishes release-like runtime behavior
from the existing development-only consent geography override. This APK cannot update the
Play-signed installation and is not a Play Billing or production rollout qualification.

SHA-256: `63D61DA804369AC9341ED4585F666A46ADC8FC3BE474B252D487D27D882D9BD5`.

| Check | Observed result |
| --- | --- |
| Unity EditMode tests | 58/58 passed: AnalyticsDeliveryTests, PrivacyTests, LessonAnalyticsTests; 0 failures/skips |
| Android manifest validator | Passed `python -B Tools/verify/android.py` |
| Unity Android build and ADB installation | Succeeded; build reported 0 errors, 222 warnings |
| APK signature | apksigner verify passed; one v2 signer |
| Packaged manifest | Collection defaults to false; backup disabled; target API 36; ARM64 |
| Fresh install / delayed enable | Initial CollectionDisabled; early tutorial activity Excluded; CollectionEnabled after the existing prompt was answered |
| Automatic first_open | One first_open in a Gemfire upload acknowledged with HTTP 204; no second first_open through the tested restarts |
| Online level_completed | Level 1: level_id=s01_firstwatch, chapter_id=s01_thornwatch, first_clear=1, stars=3; acknowledged upload |
| Lessons after enable | lesson_shown and lesson_finished pairs for map_kit, map_gate and lucky_spin; acknowledged uploads |
| Restart with prior eligibility | New process explicitly applied false, then true after UMP refreshed; events continued |
| Offline delivery and process restart | Level 2 s01_ironward completed with Wi-Fi and mobile data off; SDK logged it, process was force-stopped, network restored and app relaunched; saved bundle was acknowledged with HTTP 204 |
| Reward milestones | The offline level-2 bundle included first_step_earned, first_step_shown and first_step_equipped |
| Existing privacy form withdrawal | **Failed as an analytics withdrawal mechanism:** selecting Do not consent did not disable collection; after restart UMP still reported Obtained / CanRequestAds=true and collection enabled; a subsequent level-1 start and completion were uploaded |
| Firebase Console DebugView | **Owner confirmed all listed events visible:** first_open, level_completed for s01_firstwatch and s01_ironward, lesson_shown and lesson_finished. Native logs/HTTP responses were inspected by the agent; console visibility was confirmed by the owner |

This is a selected-test pass, not a full-suite green result. Earlier broader verification found
the existing `ShuffleGradeTests.EveryVectorCaseReadsTheWayTheServerReads` failure, reproduced
against the unchanged baseline; the unrelated long campaign sweep was stopped. Managed assembly
compilation, including the real Firebase-defined telemetry pass, succeeded in that verification.

The SDK attempted the offline bundle once without connectivity and retried it after reconnecting.
The repeated event timestamps appeared in the failed attempt and the successful attempt; they are
not evidence of duplicate application emission. Count successful uploads separately from attempts.
Before the withdrawal check, acknowledged bundles contained one first_open, two level_completed,
four lesson_shown, four lesson_finished, and one each of the three level-2 reward milestones.
An HTTP 204 is upload evidence, not confirmation of a GA4 report row or an exactly-once guarantee.
After the rejection/restart, a further level-1 completion was acknowledged with first_clear=0,
bringing the session's acknowledged completion total to three. The tested process had no
AndroidRuntime fatal exception or analytics delivery-failure diagnostic.

**Consent remains unresolved.** Today's sink eligibility still uses the legacy advertising mapping
documented in [ANALYTICS.md](ANALYTICS.md). The delivery controller correctly closes/clears when
given false in the automated tests, but the existing privacy form need not produce that false.
Do not sign off analytics-purpose consent or withdrawal based on these delivery tests.
The native SDK also started with its persisted true override before the managed startup disabled
it; the managed reconciliation is not a gate before Android native initialization.

Redacted local evidence and test helpers are under ignored `Library/AnalyticsQA/`.
They retain whitelisted event names, bounded game parameters, timestamps, state categories and
upload response codes, not raw payloads, identifiers, receipts or consent strings.
Screenshots are local only. `uploaded_counts` in intermediate snapshots counts upload attempts;
`acknowledged_counts` counts events in Gemfire bundles followed by HTTP 204.
At cleanup the original network/log verbosity settings were restored, Firebase debug mode was
disabled, and the QA app was left installed and force-stopped with the last test choice (Do not
consent). Original Unity architecture/signing settings and resolver-generated changes were restored.

## Quick repeat procedure

Use the disposable QA installation. Keep the current consent flow and regional behavior intact.
On another device, record its original Wi-Fi/mobile-data and logging settings before changing them.
Record APK hash, version, Android version, consent choice, action times and expected level IDs.

### 1. Enable DebugView before the first launch

```powershell
adb shell setprop debug.firebase.analytics.app com.tekoworld.glimmergroove
adb shell setprop log.tag.FA VERBOSE
adb shell setprop log.tag.FA-SVC VERBOSE
adb install -r Builds/Android/AnalyticsQA/Gemfire-analytics-qa.apk
adb shell am start -n com.tekoworld.glimmergroove/com.unity3d.player.UnityPlayerActivity
```

Open the correct Firebase project → Analytics → DebugView and select this test device.
Use the minutes timeline for earlier actions. Click each event to inspect parameters and
`firebase_error`, if present. Normal GA4 funnel totals are not the immediate test oracle.
Allow a few minutes online; investigate missing events rather than passing on elapsed time alone.

In a second terminal, inspect native and application diagnostics:

```powershell
adb logcat -v threadtime FA:V FA-SVC:V Unity:I AndroidRuntime:E '*:S'
```

Do not filter only to Gemfire's PID: this phone uploads through the separate Play services process.
Attribute upload bundles to `com.tekoworld.glimmergroove`; other apps can share these log tags.
Keep logs local and copy only necessary event names, game parameters and state categories.

For a genuinely new first_open test, use a new disposable install or clear only this QA app's data:

```powershell
# Erases this app's local save and consent. Use ONLY on an explicitly disposable QA install.
adb shell pm clear com.tekoworld.glimmergroove
```

Then relaunch. `install -r` preserves data and does not create a fresh first_open test.
Never add a manual first_open event to compensate for an absent automatic event.

### 2. Fresh install and tutorial

1. In the consent-required path, let the opening tutorial appear before answering any prompt.
   Note CollectionDisabled and Excluded diagnostics. Complete or skip it normally.
2. At the hub, answer the existing prompt for this QA run. Look for CollectionEnabled and native
   `Setting app measurement enabled (FE): true`, then one automatic first_open in DebugView.
3. Open Battle and dismiss the map's normal lesson panels. Verify lesson_shown → lesson_finished
   with the same mechanic and screen, e.g. map_kit / LevelsScreen and map_gate / LevelsScreen.
4. Finish level 1. Verify level_completed with level_id=s01_firstwatch and chapter_id=s01_thornwatch.
   Check stars, first_clear and the action time; one completion is expected for one finished run.

The opening lessons use mechanic=siege_fuel and siege_brim, screen=TutorialScreen. They run
before the existing hub consent prompt on the tested first launch. Their absence after enable
is expected exclusion under today's policy, not an event to recover by replay. A fresh rejection
test should also remain playable; inspect its actual state because CanRequestAds can remain true.

The first_step_* events are separate reward milestones, not mandatory opening-tutorial steps:

| Action | Expected order / parameters |
| --- | --- |
| First level-2 victory | first_step_earned(kind=turret) → level_completed(level_id=s01_ironward) |
| Show the level-2 reward | first_step_shown(kind=turret); milestone events use level, not level_id |
| Choose to equip the reward | first_step_equipped; this action is optional |
| First level-15 victory and gift | first_step_earned(kind=chests) → level_completed(level_id=s03_blightfen) → first_step_shown |
| Actually open the gift chests | first_step_chest_opened per opened chest; opening can be deferred |

Chest delivery at level 15 was not exercised in this device session. Do not expect a chest event
from level 2 or use shown → earned → equipped → chest_opened as a mandatory-player funnel.

### 3. Restart without resetting data

```powershell
adb shell am force-stop com.tekoworld.glimmergroove
adb shell am start -n com.tekoworld.glimmergroove/com.unity3d.player.UnityPlayerActivity
```

Expect explicit reconciliation every process: normally CollectionDisabled while unresolved,
then CollectionEnabled when eligible. If eligibility resolves before SDK readiness, an explicit
enable alone is valid. Verify fresh level events arrive and first_open does not repeat.
Repeat after a denied choice and inspect actual eligibility; persisted native collection can
start before managed code, so neither a manifest default nor an old log is proof of current state.

### 4. Offline delivery after collection is enabled

1. Start online, answer the existing prompt and confirm CollectionEnabled.
2. Disable both transports (USB ADB stays connected):

```powershell
adb shell svc wifi disable
adb shell svc data disable
adb shell settings get global wifi_on
adb shell settings get global mobile_data
```

3. Finish a known level, e.g. level 2, and note its completion time. Verify SDK event logging;
   absence from DebugView while offline is expected. Wait several seconds for SDK handoff.
4. Force-stop the app, restore the transports that were originally enabled, and relaunch:

```powershell
adb shell am force-stop com.tekoworld.glimmergroove
adb shell svc wifi enable
adb shell svc data enable
adb shell am start -n com.tekoworld.glimmergroove/com.unity3d.player.UnityPlayerActivity
```

5. Confirm a Gemfire upload succeeds and the original offline event appears in DebugView with
   its original time/level_id. Inspect the timeline's earlier minute, not just the current second.
   A failed upload plus a retry is normal; do not add an application-level resend after SDK handoff.
6. Separately repeat with a cold launch while offline. Record whether existing consent refresh
   resolves eligibility. Events excluded while unresolved must not be replayed after reconnect.
   This policy case does not prove or disprove Firebase offline persistence.

### 5. Withdrawal and regrant

1. With collection active, open Home → Settings → Privacy & ad choices. Select Do not consent.
2. Check for a new CollectionDisabled and native setter false. Start/finish one known level;
   new custom events should be Excluded if collection actually became disallowed.
3. Restart without clearing data and check the state again. Regrant through the same existing
   form; verify only subsequent eligible events arrive, never the excluded/cleared events.
4. If the form leaves CanRequestAds=true and analytics enabled, record **consent integration
   failure**, not queue failure. This is what the current device session reproduced.

The settings control is regional. If absent, record this path as unavailable on that device;
do not edit native preferences or force a fake analytics grant. Actual analytics-purpose
withdrawal and native startup ordering require the separately approved consent integration.
Clearing the controller's queue does not retract events already handed to Firebase or delete
server data. Pending-queue withdrawal cannot reliably be forced by disabling the network after
SDK readiness; its deterministic coverage is in AnalyticsDeliveryTests.

### 6. Finish and remaining release checks

Restore original network settings. The session started with Wi-Fi and mobile data enabled and
all three logging properties empty. Disable DebugView and restore log verbosity:

```powershell
adb shell setprop debug.firebase.analytics.app .none.
adb shell setprop log.tag.FA "''"
adb shell setprop log.tag.FA-SVC "''"
```

Before any later release, still verify:

- Any additional DebugView cases below; the owner confirmed the core events from this run.
- An approved analytics-specific consent source, rejection/withdrawal/regrant, and native startup
  behavior in supported regions. The current production flow does not pass that sign-off.
- Cold offline launch and fresh-install denial, plus level-15 chest events on a disposable account.
- An older supported Android device and a Play-signed candidate's upgrade/persisted-state path.
- A normal non-debug upload and report/filter handling; DebugView traffic is not a normal funnel sample.
- Licensed test purchases/restoration and test-ad callbacks on the appropriate signed build.
  No real purchases or additional purchase/revenue events were introduced or generated here.
- Device SDK failure scenarios if required; readiness/setter/preparation failures, queue limits,
  backoff, poison events and ambiguous monetization handoffs passed deterministic tests, but
  those native faults were not injected into this device build.

Application events still waiting for SDK handoff are process-local and bounded. Abrupt termination
can lose that buffer; Firebase persistence applies after handoff. No exactly-once or zero-loss
production guarantee follows from a successful device smoke test.

References: [Firebase DebugView](https://firebase.google.com/docs/analytics/debugview),
[Android collection control](https://firebase.google.com/docs/analytics/android/configure-data-collection),
[automatic events and first_open](https://support.google.com/analytics/answer/9234069?hl=en).

### 7. LevelPlay advertising revenue: local implementation

Use `Builds/Android/AnalyticsQA/Gemfire-revenue-qa.apk` when available. Install with `adb install -r`
over the existing disposable QA installation; do not uninstall or clear data. If signing differs,
stop and obtain approval for a separate device/install instead of resetting the existing app.

1. Enable DebugView with the commands above, open the existing game normally and verify
   CollectionEnabled. Keep the current UMP flow; do not invent a grant or reset preferences.
2. Watch one normal rewarded ad from start to dismissal, without clicking the advertisement.
   No new placement or test-only gameplay path is added. Record the display time and placement.
3. Use the coin bonus offer: it is the Google Partner Bidding mapping verified on the device.
   Expect one `ad_impression`: `ad_platform=LevelPlay`, `ad_source=admob`,
   `ad_unit_name=Bidding`, the SDK `ad_format`, numeric `value`, and `currency=USD`.
   `revenue_precision` and `ad_placement` are included when available. The value is dollars, not
   micros, eCPM, or a fixed amount per reward. Confirm the value against the SDK ILR result.
4. Check `[Analytics] ad revenue HandedToAnalytics`, native SDK event logging and upload success,
   then confirm the event and parameters in Firebase DebugView. A handoff diagnostic alone does
   not prove Firebase upload or server reporting. No synthetic monetary event is used for testing.
5. One served impression must produce one counted event. Completing/closing the ad must not add
   another revenue event. Compare automatic/native events too, including zero-value events.
   Standard AdMob and unverified instances must not produce a second manual event. ironSource
   Exchange and other Google placement mappings are currently excluded until their exact
   instance identities are verified. Do not infer SDK names from dashboard display names.
6. After a previously loaded ad is available, repeat offline if the SDK allows it. Restore the
   original connection and confirm the original event uploads once. If no ad can be shown offline,
   mark that case unavailable rather than manufacture an impression. SDK-handoff persistence and
   abrupt termination before application-queue handoff are different cases.
7. Restart without clearing data. Existing accepted revenue must not be replayed by application
   code; a new eligible impression should create a new event. Repeat withdrawal/regrant only
   through the existing controls, recording their actual collection state as in section 5.
8. Check processed GA4 Android revenue and impression counts after normal reporting delay, using
   matching time zones and collection-eligible traffic. SDK impression estimates/finalized earnings
   and consent exclusions can prevent exact equality with the LevelPlay dashboard.

Restore the original logging properties afterward. No purchase is required for this ad test.
Android IAP remains a separate Play-installed transaction/link-timing investigation described
in `REVENUE_ANALYTICS.md`; no manual purchase event has been added.
