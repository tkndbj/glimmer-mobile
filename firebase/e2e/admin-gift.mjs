// Admin gifts (invariant 63), against the live functions.
//
//     node firebase/e2e/admin-gift.mjs
//
// **Differential for `shop-deal.mjs`'s reason.** A `claimAwards` that has never heard of a `gift:`
// id refuses the claim as an unreadable daily chest, and a `getWallet` that has never heard of the
// pending map answers a valid reply with no `gifts` on it - which the client reads as "not carried"
// and draws nothing. A working feature and a missing one look the same on a device. So this asks
// the live server the questions only the gift code answers:
//
//   - every wallet reply carries `gifts`, and a fresh account's is empty;
//   - a gift written onto the wallet is listed on the next reply, oldest first;
//   - a claim for a gift the wallet never held is REFUSED, as is one in a currency it does not carry;
//   - the honest claim is confirmed and paid exactly the wallet's figure, never the client's;
//   - paying one currency leaves the other listed, paying both removes the row;
//   - claiming again confirms without paying; the permanent record notes when each half was paid;
//   - the admin callables refuse a player's token.
//
// It needs the owner's gcloud token, because the gift is written onto the new account's wallet the
// way `adminGiftPlayer` writes it (the admin callables take only a Google-verified owner token, which
// a script cannot mint) - a merge onto the `gifts` map of this run's own account and nothing else. A
// new anonymous account every run, deleted through `deleteAccount` at the end.

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

const now = Math.floor(Date.now() / 1000);
const stamp = new Date(now * 1000).toISOString().slice(0, 16).replace(/[-T:]/g, "");
const BOTH = { id: `g${stamp}e2e1`, credits: 777, gems: 5, atUnix: now - 120 };
const COINS = { id: `g${stamp}e2e2`, credits: 333, gems: 0, atUnix: now - 60 };

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
  // The readers take the call's answer whole, so a check reads `reply.body` once and never twice.
  const walletsOf = (reply) => reply?.body?.result?.wallets ?? [];
  const rowOf = (reply, currency) => walletsOf(reply).find((w) => w.currency === currency);
  const rejectedOf = (reply) => reply?.body?.result?.rejected ?? [];
  const giftsOf = (reply) => rowOf(reply, "credits")?.gifts;

  const grantId = (g, currency) => `gift:${g.id}:${currency}`;
  const claim = (g, currency, amount) => ({ id: grantId(g, currency), claimedAmount: amount, unix: now, reason: "admin_gift" });

  // 1. The reply carries the list, empty for a fresh account.
  const first = await call("getWallet", {});
  check(first.status === 200, "getWallet answers", JSON.stringify(first.body).slice(0, 160));
  check(Array.isArray(giftsOf(first)), "every wallet reply carries gifts (deploy the wallet functions if not)");
  check((giftsOf(first) ?? []).length === 0, "a fresh account has none waiting");
  const before = { credits: rowOf(first, "credits")?.grantedBaseline, gems: rowOf(first, "gems")?.grantedBaseline };

  // 2. Two gifts, written onto this account's wallet as `adminGiftPlayer` writes them: a merge onto
  // the `gifts` map alone, so nothing else on the document moves.
  const asGift = (g) => ({ mapValue: { fields: {
    credits: { integerValue: String(g.credits) }, gems: { integerValue: String(g.gems) }, atUnix: { integerValue: String(g.atUnix) },
  } } });
  const wrote = await fetch(`${FS}/players/${uid}/private/wallet?updateMask.fieldPaths=gifts.${BOTH.id}&updateMask.fieldPaths=gifts.${COINS.id}`, {
    method: "PATCH", headers: ownerHeaders,
    body: JSON.stringify({ fields: { gifts: { mapValue: { fields: { [BOTH.id]: asGift(BOTH), [COINS.id]: asGift(COINS) } } } } }),
  });
  check(wrote.status === 200, "the gifts are written onto the wallet", String(wrote.status));
  for (const g of [BOTH, COINS]) {
    await fetch(`${FS}/players/${uid}/gifts/${g.id}`, {
      method: "PATCH", headers: ownerHeaders,
      body: JSON.stringify({ fields: { id: { stringValue: g.id }, uid: { stringValue: uid }, credits: { integerValue: String(g.credits) },
        gems: { integerValue: String(g.gems) }, atUnix: { integerValue: String(g.atUnix) }, by: { stringValue: "e2e" } } }),
    });
  }

  const listed = await call("getWallet", {});
  const rows = giftsOf(listed) ?? [];
  check(rows.length === 2 && rows[0].id === BOTH.id && rows[1].id === COINS.id, "the next reply lists both, oldest first", JSON.stringify(rows));
  check(rows[0]?.credits === BOTH.credits && rows[0]?.gems === BOTH.gems, "with the amounts the wallet holds");

  // 3. Refusals: a gift the wallet never held, a currency the gift does not carry.
  const ghost = { id: `g${stamp}e2e9` };
  const refused = await call("claimAwards", { awards: [claim(ghost, "credits", 1000), claim(COINS, "gems", 1000)] });
  check(refused.status === 200, "claimAwards answers the refusals", JSON.stringify(refused.body).slice(0, 160));
  check(rejectedOf(refused).includes(grantId(ghost, "credits")), "a gift the wallet never held is refused");
  check(rejectedOf(refused).includes(grantId(COINS, "gems")), "a currency the gift does not carry is refused");
  check(rowOf(refused, "credits")?.grantedBaseline === before.credits && rowOf(refused, "gems")?.grantedBaseline === before.gems,
        "and nothing was paid");

  // 4. One half of the two-currency gift, claimed at a lying figure: paid the wallet's.
  const half = await call("claimAwards", { awards: [claim(BOTH, "credits", 999_999)] });
  check(rowOf(half, "credits")?.confirmedGrantIds?.includes(grantId(BOTH, "credits")), "the credits half is confirmed");
  check(rowOf(half, "credits")?.grantedBaseline === before.credits + BOTH.credits,
        `and paid exactly ${BOTH.credits}, not the client's figure`, `${rowOf(half, "credits")?.grantedBaseline}`);
  const afterHalf = giftsOf(half) ?? [];
  const bothRow = afterHalf.find((g) => g.id === BOTH.id);
  check(bothRow && bothRow.credits === 0 && bothRow.gems === BOTH.gems, "the gift stays listed with only the gems left", JSON.stringify(afterHalf));

  // 5. The rest, with the coins-only gift, in one call.
  const rest = await call("claimAwards", { awards: [claim(BOTH, "gems", BOTH.gems), claim(COINS, "credits", COINS.credits)] });
  check(rowOf(rest, "gems")?.grantedBaseline === before.gems + BOTH.gems, "the gems half is paid");
  check(rowOf(rest, "credits")?.grantedBaseline === before.credits + BOTH.credits + COINS.credits, "and the coins-only gift");
  check((giftsOf(rest) ?? []).length === 0, "nothing is left waiting", JSON.stringify(giftsOf(rest)));
  check(rejectedOf(rest).length === 0, "nothing refused");

  // 6. Again: confirmed, never paid twice.
  const again = await call("claimAwards", { awards: [claim(BOTH, "credits", BOTH.credits), claim(BOTH, "gems", BOTH.gems), claim(COINS, "credits", COINS.credits)] });
  check(rowOf(again, "credits")?.grantedBaseline === before.credits + BOTH.credits + COINS.credits
        && rowOf(again, "gems")?.grantedBaseline === before.gems + BOTH.gems, "claiming again pays nothing");
  check(rowOf(again, "credits")?.confirmedGrantIds?.includes(grantId(BOTH, "credits"))
        && rowOf(again, "gems")?.confirmedGrantIds?.includes(grantId(BOTH, "gems")), "and confirms every id");
  check(rejectedOf(again).length === 0, "with no refusal");

  // 7. The permanent record says when each half was paid.
  const record = await (await fetch(`${FS}/players/${uid}/gifts/${BOTH.id}`, { headers: ownerHeaders })).json();
  const paid = record.fields?.paid?.mapValue?.fields ?? {};
  check(!!paid.credits?.timestampValue && !!paid.gems?.timestampValue, "the history notes both halves paid", JSON.stringify(paid));
  check(!!record.fields?.claimedAt?.timestampValue, "and the moment the gift was settled");

  // 8. The admin gate, from a player's token.
  for (const name of ["adminGiftPlayer", "adminGetPlayer", "adminListPlayers", "adminFindPlayers"]) {
    const admin = await call(name, { uid, credits: 1, query: uid });
    check(admin.body?.error?.status === "PERMISSION_DENIED", `a player may not call ${name}`, `${admin.status} ${JSON.stringify(admin.body).slice(0, 160)}`);
  }

  // Clean up the account the run made - data first, account last (invariant 27). The gift records
  // live under the save and go with it.
  const gone = await call("deleteAccount", {});
  check(gone.status === 200, "the test account is deleted", JSON.stringify(gone.body).slice(0, 120));
} catch (e) {
  fail++;
  console.error(e);
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail > 0 ? 1 : 0);
