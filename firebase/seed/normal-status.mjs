#!/usr/bin/env node
/** Read-only by default. Explicit flags can run the bounded backfill or snapshot job. */
import { execSync } from "node:child_process";

const args = process.argv.slice(2);
if (args.length > 1 || args.some((arg) => !["--advance", "--publish", "--assert-done", "--finish"].includes(arg)))
  throw new Error("Use no flag, --advance, --publish, --assert-done or --finish");
const project = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
if (!/^[a-z0-9-]+$/.test(project)) throw new Error("Invalid project id");
const token = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
const headers = { Authorization: `Bearer ${token}` };
const decode = (raw) => raw?.integerValue !== undefined ? Number(raw.integerValue)
  : raw?.doubleValue !== undefined ? raw.doubleValue
  : raw?.booleanValue !== undefined ? raw.booleanValue
  : raw?.stringValue !== undefined ? raw.stringValue
  : raw?.timestampValue !== undefined ? raw.timestampValue
  : raw?.arrayValue ? (raw.arrayValue.values ?? []).map(decode)
  : raw?.mapValue ? Object.fromEntries(Object.entries(raw.mapValue.fields ?? {}).map(([key, value]) => [key, decode(value)])) : null;
async function read(path) {
  const response = await fetch(`https://firestore.googleapis.com/v1/projects/${project}/databases/(default)/documents/${path}`, { headers });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Reading ${path}: ${response.status} ${await response.text()}`);
  return decode({ mapValue: { fields: (await response.json()).fields } });
}
const [state, board] = await Promise.all([read("maintenance/normalLeaderboardV1"), read("leaderboards/normal")]);
async function runJob(job) {
  const response = await fetch(`https://cloudscheduler.googleapis.com/v1/projects/${project}/locations/europe-west1/jobs/firebase-schedule-${job}-europe-west1:run`,
    { method: "POST", headers });
  if (!response.ok) throw new Error(`Starting ${job}: ${response.status} ${await response.text()}`);
}
const rows = board?.entries ?? [];
const ordered = rows.every((row, index) => index === 0 || rows[index - 1].normalScore > row.normalScore
  || (rows[index - 1].normalScore === row.normalScore && rows[index - 1].uid > row.uid));
const unique = new Set(rows.map((row) => row.uid)).size === rows.length;
const ageSeconds = board?.builtUnix ? Math.max(0, Math.floor(Date.now() / 1000) - board.builtUnix) : null;
console.log(JSON.stringify({ project, backfill: { processed: state?.processed ?? 0,
  done: state?.done === true, leased: !!state?.leaseUntil }, board: { rows: rows.length,
  scoringVersion: board?.scoringVersion ?? null, ageSeconds, ordered, unique,
  topScore: rows[0]?.normalScore ?? 0 } }, null, 2));
if (args.includes("--assert-done")) {
  if (!state?.done || board?.scoringVersion !== 1 || !ordered || !unique || ageSeconds === null || ageSeconds > 180)
    throw new Error("Normal backfill or fresh, ordered snapshot is not ready");
}
if (args.includes("--advance") && state?.done) {
  console.log("Backfill already complete; no job started.");
} else if (args.includes("--advance") || args.includes("--publish")) {
  const job = args.includes("--advance") ? "backfillNormalLeaderboard" : "publishNormalLeaderboard";
  await runJob(job);
  console.log(`Queued ${job}; check status again after it finishes.`);
}
if (args.includes("--finish")) {
  const countResponse = await fetch(`https://firestore.googleapis.com/v1/projects/${project}/databases/(default)/documents:runAggregationQuery`, {
    method: "POST", headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ structuredAggregationQuery: { structuredQuery: { from: [{ collectionId: "players" }] },
      aggregations: [{ alias: "players", count: {} }] } }),
  });
  if (!countResponse.ok) throw new Error(`Counting remaining saves: ${countResponse.status}`);
  const counted = await countResponse.json();
  console.log(`Existing saves: ${counted.at(-1)?.result?.aggregateFields?.players?.integerValue ?? "unknown"}`);
  const sleep = () => new Promise((resolve) => setTimeout(resolve, 3000));
  const until = Date.now() + 600000;
  let progress = state, dispatchedAt = 0, dispatchedProgress = -1, printed = -1;
  while (progress?.done !== true && Date.now() < until) {
    if ((progress?.processed ?? 0) !== printed) {
      printed = progress?.processed ?? 0;
      console.log(`Backfill processed: ${printed}`);
    }
    const leased = progress?.leaseUntil && Date.parse(progress.leaseUntil) > Date.now();
    if (!leased && (dispatchedProgress !== printed || Date.now() - dispatchedAt > 30000)) {
      await runJob("backfillNormalLeaderboard");
      dispatchedAt = Date.now(); dispatchedProgress = printed;
    }
    await sleep();
    progress = await read("maintenance/normalLeaderboardV1");
  }
  if (!progress?.done) throw new Error("Backfill is still running; its durable checkpoint is preserved");
  console.log(`Backfill complete: ${progress.processed} saves processed`);
  const requested = Math.floor(Date.now() / 1000);
  await runJob("publishNormalLeaderboard");
  let fresh;
  for (let attempt = 0; attempt < 20; attempt++) {
    await sleep(); fresh = await read("leaderboards/normal");
    if (fresh?.builtUnix >= requested) break;
  }
  if (!fresh || fresh.builtUnix < requested) throw new Error("Waiting for the final Normal snapshot");
  console.log(`Fresh Normal snapshot: ${fresh.entries?.length ?? 0} rows, score version ${fresh.scoringVersion}`);
}
