import { usableNormalScoring } from "../functions/lib/normal-scores.js";

/** The manifest owns eligibility, including disabled chapters and non-Normal tracks. */
export function readNormalScoring(progression, manifest) {
  const scoring = progression.normalScoring;
  if (!usableNormalScoring(scoring)) throw new Error("Missing or invalid normalScoring v1");
  const eligible = new Set((manifest.chapters ?? [])
    .filter((chapter) => !chapter.disabled && (chapter.track ?? "main") === "main")
    .flatMap((chapter) => chapter.levels ?? []));
  const targets = new Set(scoring.levels.map((row) => row.level));
  if (eligible.size !== targets.size || [...eligible].some((id) => !targets.has(id)))
    throw new Error("Normal targets must exactly match active Normal chapter levels");
  return scoring;
}
