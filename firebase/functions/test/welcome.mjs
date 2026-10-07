// The server half of the welcome bonus (invariant 58): a finished quest taken as its turret's
// price. What is under contract: the id's spelling (`GrantEntry.WelcomeId`), that the amount is
// the published roster's and never the client's, that a claim the save does not yet vouch for is
// left unconfirmed rather than refused, and that only a wrong currency is refused.
//
// Run through `lib/`, the compiled output, so what is tested is what deploys.

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const compiled = join(REPO, "firebase", "functions", "lib", "welcome.js");

if (!existsSync(compiled)) {
  console.error("build first: npm run build (lib/welcome.js is missing)");
  process.exit(1);
}

const {
  WELCOME_MAX_QUESTS, isWelcomeGrantId, judgeWelcomeClaim, parseWelcomeClaim, usableWelcomeConfig,
  welcomeCoinedOf,
} = await import(pathToFileURL(compiled).href);

let failed = 0;
const check = (ok, what, detail = "") => {
  if (ok) console.log("  ok   " + what);
  else { failed++; console.log("  FAIL " + what + (detail ? "  " + detail : "")); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what, `got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

console.log("welcome: the block resolves");
const block = {
  quests: {
    w1_leech: { ward: "leech", currency: "credits", amount: 11000 },
    w4_glacier: { ward: "glacier", currency: "credits", amount: 16000 },
    w9_eclipse: { ward: "eclipse", currency: "gems", amount: 1800 },
  },
};
const config = usableWelcomeConfig(block);
check(config !== null, "a well-formed block is usable");
equal("and keeps its quests", Object.keys(config.quests), ["w1_leech", "w4_glacier", "w9_eclipse"]);
equal("absent is nothing", usableWelcomeConfig(undefined), null);
equal("an empty block is nothing", usableWelcomeConfig({ quests: {} }), null);
equal("a nought price is nothing", usableWelcomeConfig({ quests: { w: { ward: "x", currency: "credits", amount: 0 } } }), null);
equal("a third currency is nothing", usableWelcomeConfig({ quests: { w: { ward: "x", currency: "hearts", amount: 5 } } }), null);
equal("a bad id is nothing", usableWelcomeConfig({ quests: { "W-1": { ward: "x", currency: "credits", amount: 5 } } }), null);
{
  const many = {};
  for (let i = 0; i <= WELCOME_MAX_QUESTS; i++) many[`q${i}`] = { ward: "x", currency: "credits", amount: 5 };
  equal("too many quests is nothing", usableWelcomeConfig({ quests: many }), null);
}

console.log("\nwelcome: the id");
check(isWelcomeGrantId("welcome:w1_leech:credits"), "a welcome id is recognised");
check(!isWelcomeGrantId("task:daily:1:x:credits"), "a task id is not");
equal("parses", parseWelcomeClaim("welcome:w1_leech:credits"), { questId: "w1_leech", currency: "credits", dayKey: 0 });
equal("refuses a fourth part", parseWelcomeClaim("welcome:w1:credits:x"), null);
equal("refuses a capital", parseWelcomeClaim("welcome:W1:credits"), null);
equal("refuses an empty currency", parseWelcomeClaim("welcome:w1:"), null);
equal("refuses a leading digit", parseWelcomeClaim("welcome:1w:credits"), null);

console.log("\nwelcome: the save's view");
equal("reads the coined list", [...welcomeCoinedOf({ tasks: { welcome: { coined: ["w1_leech", "nope!", 3] } } })], ["w1_leech"]);
equal("an absent save is empty", [...welcomeCoinedOf(undefined)], []);
equal("a save with no block is empty", [...welcomeCoinedOf({ tasks: {} })], []);

console.log("\nwelcome: the judge");
const claim = parseWelcomeClaim("welcome:w1_leech:credits");
const coined = new Set(["w1_leech"]);
equal("pays the published price when the save vouches", judgeWelcomeClaim(config, claim, coined),
      { kind: "ok", amount: 11000, ward: "leech" });
equal("never the client's figure (the claim carries none)", judgeWelcomeClaim(config, claim, coined).amount, 11000);
equal("unconfirmed with no block", judgeWelcomeClaim(null, claim, coined).kind, "unknown");
equal("unconfirmed for a quest the block does not name",
      judgeWelcomeClaim(config, parseWelcomeClaim("welcome:w2_nobody:credits"), coined).kind, "unknown");
equal("unconfirmed until the save records the choice", judgeWelcomeClaim(config, claim, new Set()).kind, "unknown");
equal("unconfirmed with no save at all", judgeWelcomeClaim(config, claim, null).kind, "unknown");
equal("refused in the wrong currency",
      judgeWelcomeClaim(config, parseWelcomeClaim("welcome:w1_leech:gems"), coined).kind, "refuse");
equal("a gem turret pays gems",
      judgeWelcomeClaim(config, parseWelcomeClaim("welcome:w9_eclipse:gems"), new Set(["w9_eclipse"])),
      { kind: "ok", amount: 1800, ward: "eclipse" });

console.log(failed ? `\n${failed} FAILED` : "\nall welcome checks passed");
process.exit(failed ? 1 : 0);
