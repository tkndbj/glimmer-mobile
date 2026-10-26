#!/usr/bin/env node
/** Default/--check: local validation. --apply: patch only normalScoring. --verify: live check. */
import { readFileSync } from "node:fs";
import { execSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { readNormalScoring } from "./normal-config.mjs";

async function main() {
  const args = process.argv.slice(2);
  if (args.some((arg) => !["--check", "--apply", "--verify"].includes(arg)) || args.length > 1)
    throw new Error("Use one of --check, --apply or --verify. Default is a local check.");
  const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
  const read = (path) => JSON.parse(readFileSync(resolve(root, path), "utf8"));
  const scoring = readNormalScoring(read("Assets/StreamingAssets/Content/progression.json"),
                                    read("Assets/StreamingAssets/Content/manifest.json"));
  if (!args.includes("--apply") && !args.includes("--verify")) {
    console.log(`Normal v${scoring.version}: ${scoring.levels.length} targets validated; no writes.`);
    return;
  }
  const project = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
  const url = `https://firestore.googleapis.com/v1/projects/${encodeURIComponent(project)}/databases/(default)/documents/config/progression`;
  const token = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const encode = (value) => typeof value === "string" ? { stringValue: value }
    : typeof value === "number" ? { integerValue: String(value) }
    : Array.isArray(value) ? { arrayValue: { values: value.map(encode) } }
    : { mapValue: { fields: Object.fromEntries(Object.entries(value).map(([key, item]) => [key, encode(item)])) } };
  const decode = (value) => value?.integerValue !== undefined ? Number(value.integerValue)
    : value?.stringValue !== undefined ? value.stringValue
    : value?.arrayValue ? (value.arrayValue.values ?? []).map(decode)
    : value?.mapValue ? Object.fromEntries(Object.entries(value.mapValue.fields ?? {}).map(([key, item]) => [key, decode(item)]))
    : undefined;
  const canonical = (block) => block && JSON.stringify({ version: block.version,
    levels: [...(block.levels ?? [])].sort((a, b) => a.level.localeCompare(b.level))
      .map((row) => ({ level: row.level, target: row.target })) });
  const response = await fetch(url, { headers });
  if (!response.ok) throw new Error(`Reading config/progression failed: ${response.status}`);
  const current = await response.json();
  const deployedLevels = decode(current.fields?.levelChapters) ?? {};
  if (scoring.levels.some((row) => typeof deployedLevels[row.level] !== "string"))
    throw new Error("A Normal target is absent from the deployed level catalog; deploy its content first");
  const live = decode(current.fields?.normalScoring);
  if (canonical(live) === canonical(scoring)) {
    console.log(`${project}: Normal scoring already matches (${scoring.levels.length} targets).`);
    return;
  }
  if (args.includes("--verify")) throw new Error("Live Normal scoring does not match this release");
  if (live) {
    const targets = new Map(scoring.levels.map((row) => [row.level, row.target]));
    if (live.version !== scoring.version || live.levels.some((row) => targets.get(row.level) !== row.target))
      throw new Error("Refusing to replace or remove an existing frozen Normal target");
  }
  const patched = await fetch(url + "?updateMask.fieldPaths=normalScoring&currentDocument.updateTime="
    + encodeURIComponent(current.updateTime), {
    method: "PATCH", headers, body: JSON.stringify({ fields: { normalScoring: encode(scoring) } }),
  });
  if (!patched.ok) throw new Error(`Seeding Normal failed: ${patched.status} ${await patched.text()}`);
  console.log(`${project}: patched only config/progression.normalScoring (${scoring.levels.length} targets).`);
}

await main();
