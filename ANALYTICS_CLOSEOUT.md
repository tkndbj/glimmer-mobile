# Analytics engineering closeout — 24 October 2026

This task changes only analytics delivery, reviewed advertising-revenue ownership and bounded
diagnostics. Consent handling/messages, regional defaults, ad serving/mediation, placements,
purchases/restoration, rewards, progression and saves remain unchanged. Nothing is published.

## Android IAP: engineering investigation closed

Five server-validated Google purchase grants match the owner's October orders. Their retained
server test flags are false and no revocation is recorded. This establishes a reporting discrepancy;
it does not establish the historical analytics state, exclude promotions, or prove live refund status.

The reviewed implementation has:

- The correct package, Firebase app/project and Android stream mapping; Analytics SDK/native
  initialization components are packaged. Current SDKs are Firebase Unity 13.15.0, Android
  Analytics 23.2.0, Unity IAP 5.4.2 and Google Play Billing 9.0.0.
- Firebase dependency checking started independently on Boot, shared with the cloud backend.
  The reliability fix explicitly reconciles native collection state and recovers safe send failures.
- Purchase initialization connecting Billing, fetching priced products (including actual local
  price/currency), then fetching unfinished orders. Native automatic purchase tracking is separate
  from the game's custom Firebase queue and server reward grants.
- `ProcessPendingOrdersOnPurchasesFetched(false)` controlling Unity's order-processing callbacks,
  not disabling Firebase automatic revenue measurement. Changing it could affect grant/recovery
  behavior and is not an analytics fix.
- No manual `purchase` or `in_app_purchase` writer. No confirmed missing required Android Firebase
  purchase API call: Google documents automatic Google Play purchase measurement after linking.
- Owner-confirmed Play linking/revenue sharing enabled now, with an unknown activation date.
  Internal Traffic is Testing; Developer Traffic is not configured; the standard Events report
  confirms the Android gap independently of exploration filters.

**No additional confirmed IAP code defect was found. No speculative IAP code change is made.**
The existing collection/consent timing can exclude startup recovery events; its actual state on
the historical purchasing devices is unavailable, and changing that policy/timing is outside the
approved scope. Current source and a healthy QA run cannot prove what each buyer's release did.

Remaining external verification is limited to the historical Play-link activation and one
qualifying Play-installed/Play-signed transaction with collection state observed, then reconciliation
of native automatic measurement with processed GA4 reports. Licensed test purchases can be excluded
from monetary reports; an eligible charged order is needed for final revenue reconciliation.
No purchase is initiated here. No receipts are replayed or backfilled as new Analytics purchases.

This investigation stops here unless that external evidence identifies a specific defect.

## Advertising and verification

Completed code: all eight Google rewarded units are enabled after their SDK callbacks confirmed
the exact known partner ID; duplicate protection, finite USD values, collection gating and safe
failure handling are retained. Standard AdMob and unverified sources remain excluded. ironSource
Exchange stays disabled because no exact instance mapping was available in the observed callbacks
or local configuration; no auction/network settings were changed to force one.

Passed: 184 selected Unity tests, all managed compile passes, Android manifest checks, a successful
local Android build, SDK loads for all eight units, native revenue-parameter/HTTP-204 verification,
and a genuine offline capture followed by force-stop/reconnect delivery without an extra replay.
Progression-locked placements were not forcibly displayed. No real purchase was initiated.

Engineering disposition: ready for release-candidate QA with the disclosed ironSource exclusion.
Publishing is not authorized. Full production financial sign-off still requires processed GA4
reconciliation, a qualifying Play transaction and the separately owned consent/configuration checks.

Final enabled mappings, test results and physical-device evidence are recorded in
`REVENUE_ANALYTICS.md`. `ANDROID_ANALYTICS_TESTING.md` contains the remaining device procedure.
Unverified mappings remain disabled; a network name or a missing historical revenue total does
not authorize another writer. Loaded-ad metadata is used only to review configuration, never to
emit revenue. Monetary events come exclusively from `OnAdImpressionDataReady`.

## Outside this code task

- Consent integration and regional/privacy-policy decisions remain unchanged and require separate
  approval. This task is not a consent-compliance sign-off.
- AdMob/GA4 account configuration and processed-report reconciliation are external checks; no
  account settings, consent messages, identities, native event caches or player data are reset.
- A release candidate is a validation artifact, not permission to publish or a claim of complete
  production revenue coverage. Any disabled/unverified sources must be disclosed in its notes.

References: [Firebase Unity purchase measurement](https://firebase.google.com/docs/analytics/unity/measure-in-app-purchases),
[Google Play ingestion timing](https://support.google.com/analytics/answer/11548051),
[Partner Bidding revenue](https://support.google.com/admob/answer/11322405).
