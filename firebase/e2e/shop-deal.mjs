// Limited-time shop deals (invariant 60), against the live functions.
//
//     node firebase/e2e/shop-deal.mjs
//
// **Differential for `keeper-spend.mjs`'s reason.** A `submitSpends` that has never heard of a
// `deal:` id charges whatever amount is named and pays no coins, and a `claimAwards` that has
// never heard of one refuses the claim as an unreadable daily chest - both of which a device
// shows as a purchase that half happened. So this asks the live server the questions only the
// deal code answers:
//
//   - every wallet reply carries `dealsBought`, and a fresh account's is empty;
//   - a deal claim sent before its debit is left waiting, neither paid nor refused;
//   - a debit under the price, in credits, for an unknown deal or for a closed one is REFUSED;
//   - a claim for a closed deal with nothing paid is refused;
//   - the honest debit is confirmed, takes exactly the price in gems, grants exactly the deal's
//     coins in the same reply, confirms the claim id, and names the deal in `dealsBought`;
//   - buying again charges nothing and pays nothing, and the claim then confirms without paying.
//
// It needs the owner's gcloud token, because it adds its own test deals to `config/deals` for the
// length of the run - the document only the admin callables write - and takes only those away
// again, with a precondition on every write so a deal the owner makes or ends meanwhile is never
// overwritten. **Players on a build that sells deals can see the test deals for the minute it
// runs** (777 coins for 5 gems, priced so a stray purchase costs nothing). A new anonymous account
// every run, deleted through `deleteAccount` at the end.

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

/** The "Tekoworld Admin" web app's key: the browser identity this project already publishes. */
const KEY = process.env.GLIMMER_WEB_KEY ?? "AIzaSyBI72XrlFjrbNuEBl7rATMDK4MjWuwGX6I";

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log(`  ok   ${what}`); }
  else { fail++; console.log(`  FAIL ${what} ${detail}`); }
};

const owner = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
const ownerHeaders = { Authorization: `Bearer ${owner}`, "Content-Type": "application/json" };

// ------------------------------------------------------------------ the test deal
const now = Math.floor(Date.now() / 1000);
const stamp = new Date(now * 1000).toISOString().slice(0, 16).replace(/[-T:]/g, "");
const LIVE = { id: `d${stamp}e2e1`, credits: 777, gems: 5, startUnix: now - 60, endUnix: now + 600 };
const CLOSED = { id: `d${stamp}e2e2`, credits: 999, gems: 1, startUnix: now - 7200, endUnix: now - 3600 };

// A deal ended from the admin page a few seconds ago: still inside the grace a deal that ran out
// would have, and refused anyway, because "End now" withdraws a mistake (`graceOf`).
const EARLY = { id: `d${stamp}e2e4`, credits: 555, gems: 1, startUnix: now - 120, endUnix: now - 5, endedEarly: true };
const OURS = new Set([LIVE.id, CLOSED.id, EARLY.id]);

const asRow = (d) => ({
  mapValue: { fields: {
    id: { stringValue: d.id }, credits: { integerValue: String(d.credits) }, gems: { integerValue: String(d.gems) },
    startUnix: { integerValue: String(d.startUnix) }, endUnix: { integerValue: String(d.endUnix) },
    ...(d.endedEarly ? { endedEarly: { booleanValue: true } } : {}),
  } },
});

// **Non-destructive.** The test deals are added beside whatever the owner has on sale and only
// they are taken away again; every write carries the document's last update time as a
// precondition, so a deal the owner makes or ends while this runs is never overwritten - the write
// fails, the document is read again, and the change is made over the new one.
async function readDeals() {
  const r = await fetch(`${FS}/config/deals`, { headers: ownerHeaders });
  if (r.status === 404) return { exists: false, updateTime: null, rows: [] };
  if (r.status !== 200) throw new Error(`could not read config/deals with the owner token (${r.status}); is gcloud signed in?`);
  const doc = await r.json();
  return { exists: true, updateTime: doc.updateTime, rows: doc.fields?.deals?.arrayValue?.values ?? [] };
}

const idOf = (row) => row.mapValue?.fields?.id?.stringValue ?? "";

async function writeDeals(change) {
  for (let attempt = 0; attempt < 5; attempt++) {
    const current = await readDeals();
    const rows = change(current.rows);
    const pre = current.exists ? `currentDocument.updateTime=${encodeURIComponent(current.updateTime)}` : "currentDocument.exists=false";

    const r = rows.length === 0 && current.exists
      ? await fetch(`${FS}/config/deals?${pre}`, { method: "DELETE", headers: ownerHeaders })
      // Masked to the two fields this changes, so the admin callables' own fields (`updatedAt`) are
      // left exactly as they were.
      : await fetch(`${FS}/config/deals?${pre}&updateMask.fieldPaths=schema&updateMask.fieldPaths=deals`, {
          method: "PATCH", headers: ownerHeaders,
          body: JSON.stringify({ fields: { schema: { integerValue: "1" }, deals: { arrayValue: { values: rows } } } }),
        });
    if (r.status === 200) return true;
    if (r.status !== 400 && r.status !== 409 && r.status !== 412) throw new Error(`config/deals write failed (${r.status})`);
    // The document moved under us: read it again and redo the change over it.
  }
  return false;
}

async function restore() {
  const ok = await writeDeals((rows) => rows.filter((row) => !OURS.has(idOf(row))));
  console.log(ok ? "\nthe test deals are gone; everything else in config/deals was left as it was"
                 : "\nFAIL could not take the test deals out of config/deals - remove them by hand");
  if (!ok) process.exitCode = 1;
}

if (!(await writeDeals((rows) => [...rows.filter((row) => !OURS.has(idOf(row))), asRow(CLOSED), asRow(EARLY), asRow(LIVE)]))) {
  console.error("could not add the test deals to config/deals");
  process.exit(1);
}
console.log(`added test deals ${LIVE.id} (live), ${CLOSED.id} (closed) and ${EARLY.id} (ended early) beside the real ones`);

let headers, uid;
try {
  // ---------------------------------------------------------------- the player
  const signUp = await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`, {
    method: "POST", headers: { "Content-Type": "application/json", Referer: "https://www.tekoworld.com/" },
    body: JSON.stringify({ returnSecureToken: true }),
  });
  const auth = await signUp.json();
  if (!auth.idToken) throw new Error("anonymous sign-in failed " + JSON.stringify(auth));
  uid = auth.localId;
  headers = { Authorization: `Bearer ${auth.idToken}`, "Content-Type": "application/json" };
  console.log(`signed in anonymously as ${uid}\n`);

  const call = async (name, data) => {
    const r = await fetch(`${FN}/${name}`, { method: "POST", headers, body: JSON.stringify({ data }) });
    return { status: r.status, body: await r.json().catch(() => ({})) };
  };
  const walletsOf = (reply) => reply?.result?.wallets ?? [];
  const rowOf = (reply, currency) => walletsOf(reply).find((w) => w.currency === currency);
  const rejectedOf = (reply) => reply?.result?.rejected ?? [];

  const spendId = (d) => `deal:${d.id}`;
  const grantId = (d) => `deal:${d.id}:credits`;
  const debit = (d, amount, currency = "gems", unix = now) =>
    ({ id: spendId(d), currency, amount, unix, reason: "shop_deal" });
  const claim = (d) => ({ id: grantId(d), claimedAmount: d.credits, unix: now, reason: "shop_deal" });

  // 1. The reply carries the list, and the player may read the deals.
  const wallet = await call("getWallet", {});
  check(wallet.status === 200, "getWallet responds", JSON.stringify(wallet.body).slice(0, 160));
  check(walletsOf(wallet.body).length > 0 && walletsOf(wallet.body).every((w) => Array.isArray(w.dealsBought)),
        "every wallet row carries dealsBought");
  check(walletsOf(wallet.body).every((w) => w.dealsBought.length === 0), "and a fresh account's is empty");
  const gems0 = rowOf(wallet.body, "gems");
  const credits0 = rowOf(wallet.body, "credits");
  check((gems0?.grantedBaseline ?? 0) - (gems0?.spentBaseline ?? 0) >= LIVE.gems, "the seed affords the test deal");

  const read = await fetch(`${FS}/config/deals`, { headers });
  check(read.status === 200, "a signed-in player may read config/deals (one get, the rules' config grant)");
  const list = await fetch(`${FS}/config`, { headers });
  check(list.status === 403, "and may not list the config collection", `status ${list.status}`);
  const write = await fetch(`${FS}/config/deals`, { method: "PATCH", headers, body: JSON.stringify({ fields: {} }) });
  check(write.status === 403, "nor write the deals", `status ${write.status}`);

  // 2. A claim before its debit waits.
  const early = await call("claimAwards", { awards: [claim(LIVE)] });
  check(!rejectedOf(early.body).includes(grantId(LIVE)), "a deal claim sent before its debit is not refused");
  check(!(rowOf(early.body, "credits")?.confirmedGrantIds ?? []).includes(grantId(LIVE)), "nor paid");
  check(rowOf(early.body, "credits")?.grantedBaseline === credits0?.grantedBaseline, "and no coins moved");

  // 3. The refusals - each one a debit an older server would have charged.
  const under = await call("submitSpends", { spends: [debit(LIVE, LIVE.gems - 1)] });
  check(rejectedOf(under.body).includes(spendId(LIVE)), "a debit one gem under the price is refused");
  check((rowOf(under.body, "gems")?.spentBaseline ?? -1) === (gems0?.spentBaseline ?? 0), "and nothing was charged");

  const credits = await call("submitSpends", { spends: [debit(LIVE, LIVE.gems, "credits")] });
  check(rejectedOf(credits.body).includes(spendId(LIVE)), "a debit in credits is refused");

  const unknown = { id: `deal:d${stamp}zzzz`, currency: "gems", amount: 1, unix: now, reason: "shop_deal" };
  const r3 = await call("submitSpends", { spends: [unknown] });
  check(rejectedOf(r3.body).includes(unknown.id), "a debit for a deal nobody published is refused");

  const closed = await call("submitSpends", { spends: [debit(CLOSED, CLOSED.gems)] });
  check(rejectedOf(closed.body).includes(spendId(CLOSED)), "a debit for a closed deal is refused");

  const closedClaim = await call("claimAwards", { awards: [claim(CLOSED)] });
  check(rejectedOf(closedClaim.body).includes(grantId(CLOSED)), "a claim for a closed deal with nothing paid is refused");

  const endedNow = await call("submitSpends", { spends: [debit(EARLY, EARLY.gems)] });
  check(rejectedOf(endedNow.body).includes(spendId(EARLY)), "a debit seconds after End now is refused - no grace for a deal ended early");
  const earlyClaim = await call("claimAwards", { awards: [claim(EARLY)] });
  check(rejectedOf(earlyClaim.body).includes(grantId(EARLY)), "and so is its claim");

  const malformed = { id: "deal:not-a-deal", currency: "gems", amount: 1, unix: now, reason: "shop_deal" };
  const r4 = await call("submitSpends", { spends: [malformed] });
  check(rejectedOf(r4.body).includes(malformed.id), "a malformed deal id is refused rather than charged");

  // 4. The honest purchase.
  const honest = await call("submitSpends", { spends: [debit(LIVE, LIVE.gems)] });
  const gemsRow = rowOf(honest.body, "gems"), creditsRow = rowOf(honest.body, "credits");
  check((gemsRow?.confirmedSpendIds ?? []).includes(spendId(LIVE)), "the honest debit is confirmed",
        JSON.stringify(honest.body).slice(0, 240));
  check(gemsRow?.spentBaseline === (gems0?.spentBaseline ?? 0) + LIVE.gems, "and takes exactly the price in gems");
  check(creditsRow?.grantedBaseline === (credits0?.grantedBaseline ?? 0) + LIVE.credits,
        "the coins are granted in the same reply", `granted=${creditsRow?.grantedBaseline}`);
  check((creditsRow?.confirmedGrantIds ?? []).includes(grantId(LIVE)), "and the claim id is confirmed with them");
  check(walletsOf(honest.body).every((w) => w.dealsBought.includes(LIVE.id)), "and every row names the deal as bought");

  // 5. Once per account.
  const again = await call("submitSpends", { spends: [debit(LIVE, LIVE.gems, "gems", now + 1)] });
  check(rowOf(again.body, "gems")?.spentBaseline === gemsRow?.spentBaseline, "buying again charges nothing");
  check(rowOf(again.body, "credits")?.grantedBaseline === creditsRow?.grantedBaseline, "and pays nothing");
  check((rowOf(again.body, "credits")?.confirmedGrantIds ?? []).includes(grantId(LIVE)), "and confirms the claim again");

  const late = await call("claimAwards", { awards: [claim(LIVE)] });
  check((rowOf(late.body, "credits")?.confirmedGrantIds ?? []).includes(grantId(LIVE)), "the waiting claim now confirms");
  check(rowOf(late.body, "credits")?.grantedBaseline === creditsRow?.grantedBaseline, "without paying a second time");

  const fresh = await call("getWallet", {});
  check(walletsOf(fresh.body).every((w) => w.dealsBought.includes(LIVE.id)), "getWallet names the deal as bought");

  // 6. What the admin page asks, asked the way `adminListDeals` / `adminDealHistory` ask it, with
  // the owner's token: the buyer count through the `spendLog.dealId` collection-group index (a
  // missing index answers FAILED_PRECONDITION, which is exactly what this is here to catch), and
  // one history page in the callable's own order and cursor shape.
  const runQuery = async (body) => {
    const r = await fetch(`${FS}:${body.structuredAggregationQuery ? "runAggregationQuery" : "runQuery"}`,
      { method: "POST", headers: ownerHeaders, body: JSON.stringify(body) });
    return { status: r.status, body: await r.json().catch(() => ({})) };
  };
  const countFor = async (dealId) => runQuery({ structuredAggregationQuery: {
    structuredQuery: {
      from: [{ collectionId: "spendLog", allDescendants: true }],
      where: { fieldFilter: { field: { fieldPath: "dealId" }, op: "EQUAL", value: { stringValue: dealId } } },
    },
    aggregations: [{ alias: "n", count: {} }],
  } });

  let counted = null;
  for (let attempt = 0; attempt < 24; attempt++) {
    counted = await countFor(LIVE.id);
    if (counted.status === 200) break;
    await new Promise((r) => setTimeout(r, 10000));          // a fresh index builds in the background
  }
  const n = Number(counted?.body?.[0]?.result?.aggregateFields?.n?.integerValue ?? -1);
  check(counted?.status === 200, "the buyer count runs on the spendLog.dealId index", JSON.stringify(counted?.body).slice(0, 200));
  check(n === 1, "and counts the one buyer", `count=${n}`);
  const none = await countFor(CLOSED.id);
  check(Number(none.body?.[0]?.result?.aggregateFields?.n?.integerValue ?? -1) === 0, "a deal nobody bought counts nought");

  const history = (d) => fetch(`${FS}/dealHistory/${d.id}`, {
    method: "PATCH", headers: ownerHeaders,
    body: JSON.stringify({ fields: asRow(d).mapValue.fields }),
  });
  await history(CLOSED);
  await history({ ...CLOSED, id: `d${stamp}e2e3`, endUnix: CLOSED.endUnix });
  const page = (after) => runQuery({ structuredQuery: {
    from: [{ collectionId: "dealHistory" }],
    where: { fieldFilter: { field: { fieldPath: "endUnix" }, op: "LESS_THAN_OR_EQUAL", value: { integerValue: String(now) } } },
    orderBy: [{ field: { fieldPath: "endUnix" }, direction: "DESCENDING" },
              { field: { fieldPath: "__name__" }, direction: "DESCENDING" }],
    ...(after ? { startAt: { values: [{ integerValue: String(after.endUnix) },
      { referenceValue: `projects/${PROJECT}/databases/(default)/documents/dealHistory/${after.id}` }], before: false } } : {}),
    limit: 1,
  } });
  const p1 = await page(null);
  const first = (p1.body ?? []).map((x) => x.document?.name?.split("/").pop()).filter(Boolean);
  check(p1.status === 200 && first.length === 1, "a history page runs on the automatic index", JSON.stringify(p1.body).slice(0, 200));
  const p2 = await page({ endUnix: CLOSED.endUnix, id: first[0] });
  const second = (p2.body ?? []).map((x) => x.document?.name?.split("/").pop()).filter(Boolean);
  check(p2.status === 200 && second.length === 1 && second[0] !== first[0], "and the cursor reaches the next row, never the same one",
        JSON.stringify([first, second]));
  for (const id of [CLOSED.id, `d${stamp}e2e3`]) await fetch(`${FS}/dealHistory/${id}`, { method: "DELETE", headers: ownerHeaders });

  // 7. The admin gate, from a player's token.
  const admin = await call("adminCreateDeal", { credits: 1, gems: 1, durationSeconds: 600 });
  check(admin.body?.error?.status === "PERMISSION_DENIED", "a player may not make deals",
        JSON.stringify(admin.body).slice(0, 160));

  // Clean up the account the run made - data first, account last (invariant 27).
  const gone = await call("deleteAccount", {});
  check(gone.status === 200, "the test account is deleted", JSON.stringify(gone.body).slice(0, 120));
} catch (e) {
  fail++;
  console.error(e);
} finally {
  await restore();
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail > 0 ? 1 : 0);
