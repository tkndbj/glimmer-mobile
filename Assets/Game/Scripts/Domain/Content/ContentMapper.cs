using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Turns parsed JSON into validated domain objects.
    ///
    /// Every rejection is reported rather than thrown. Content can arrive from a CDN,
    /// so this layer treats it as hostile input: one malformed level is dropped and
    /// named, and the rest of the chapter still loads. The alternative — an exception
    /// on a background thread three days after a content drop — is how live games
    /// lose a weekend.
    ///
    /// Problems go to a plain collection rather than to a particular builder, because
    /// the same mapping serves the runtime loader, the Editor validator and the tests,
    /// and none of them should have to own each other's reporting type.
    /// </summary>
    public static class ContentMapper
    {
        /// <summary>
        /// Fallbacks for a chapter that does not state its own colours. Written as hex
        /// here rather than pulled from the UI palette, because content must be
        /// readable and checkable without a renderer — the build gate parses every
        /// chapter with no UI assembly loaded at all.
        /// </summary>
        const string DefaultAccentHex = "#FFC93C";
        const string DefaultSlateHex = "#16222E";

        /// <summary>
        /// Used only to keep a chapter renderable when its body forgot to name a
        /// backdrop. It is reported as a problem rather than applied silently: a
        /// chapter inheriting art from a constant is how one chapter's backdrop ends
        /// up owned by another chapter's asset bundle. Validation fails the build on
        /// it, so this fallback can only ever be seen by a partial download.
        /// </summary>
        const string LastResortBackdrop = "play_0";

        public static bool TryReadChapter(string json, ICollection<string> problems, out ChapterBody body)
        {
            body = null;

            ChapterDto dto;
            try
            {
                dto = JsonUtility.FromJson<ChapterDto>(json);
            }
            catch (System.Exception e)
            {
                problems.Add($"chapter file is not valid JSON: {e.Message}");
                return false;
            }

            if (dto == null) { problems.Add("chapter file is empty"); return false; }

            string schemaProblem = ContentSchema.Explain(dto.schemaVersion);
            if (schemaProblem != null)
            {
                problems.Add($"chapter '{dto.id}' {schemaProblem}");
                return false;
            }

            if (!ChapterId.TryParse(dto.id, out var chapterId, out string idError))
            {
                problems.Add($"chapter id '{dto.id}' rejected: {idError}");
                return false;
            }

            // Order lives in the manifest. A body carrying one is a stale file whose
            // author believes a number that does nothing — say so rather than discard it.
            if (dto.order != 0)
                problems.Add($"chapter '{chapterId}' sets \"order\": {dto.order} in its body; " +
                             "order belongs in manifest.json and this value is ignored");

            if (string.IsNullOrEmpty(dto.backdrop))
                problems.Add($"chapter '{chapterId}' does not name a backdrop; " +
                             $"falling back to '{LastResortBackdrop}', which belongs to another chapter's bundle");

            var levels = new List<LevelDefinition>();
            if (dto.levels != null)
            {
                foreach (var levelDto in dto.levels)
                    if (TryReadLevel(levelDto, chapterId, problems, out var level))
                        levels.Add(level);
            }

            var definition = new ChapterDefinition(
                chapterId,
                dto.nameKey,
                ParseColour(dto.accent, ParseColour(DefaultAccentHex, Color.white)),
                ParseColour(dto.slate, ParseColour(DefaultSlateHex, Color.black)),
                string.IsNullOrEmpty(dto.backdrop) ? LastResortBackdrop : dto.backdrop,
                dto.mapStrips);

            body = new ChapterBody(definition, levels);
            return true;
        }

        static bool TryReadLevel(LevelDto dto, ChapterId chapter, ICollection<string> problems,
                                 out LevelDefinition level)
        {
            level = null;
            if (dto == null) return false;

            if (!LevelId.TryParse(dto.id, out var id, out string idError))
            {
                problems.Add($"level id '{dto.id}' in chapter '{chapter}' rejected: {idError}");
                return false;
            }

            if (dto.rows == null || dto.rows.Length == 0)
            {
                problems.Add($"level '{id}' has no rows");
                return false;
            }

            int width = dto.width > 0 ? dto.width : WidestRow(dto.rows);
            int height = dto.height > 0 ? dto.height : dto.rows.Length;

            LevelLayout layout;
            try
            {
                layout = new LevelLayout(width, height, dto.rows);
            }
            catch (System.Exception e)
            {
                problems.Add($"level '{id}' has an unusable grid: {e.Message}");
                return false;
            }

            // Par is derivable from the board, so an omitted par is not an error —
            // it is the recommended way to author, since a hand-typed one can drift.
            int par = dto.par;
            if (par <= 0)
            {
                var parsed = LevelGridParser.Parse(layout);
                if (!parsed.Ok)
                {
                    problems.Add($"level '{id}' cannot be parsed: {string.Join("; ", parsed.Errors)}");
                    return false;
                }
                par = PuzzleFactory.MinimumMoves(parsed.Cells);
            }

            var tuning = new LevelTuning(
                par,
                dto.goldFactor > 0f ? dto.goldFactor : LevelTuning.DefaultGoldFactor,
                dto.silverFactor > 0f ? dto.silverFactor : LevelTuning.DefaultSilverFactor,
                dto.hintAllowance > 0 ? dto.hintAllowance : LevelTuning.DefaultHintAllowance,
                dto.budgetFactor);

            var presentation = new LevelPresentation(
                new Vector2(dto.mapX, dto.mapY),
                OptionalColour(dto.accent),
                OptionalColour(dto.slate),
                dto.backdrop);

            level = new LevelDefinition(id, chapter, layout, tuning, presentation);
            return true;
        }

        public static ManifestDto ReadManifest(string json, out string error)
        {
            error = null;
            try
            {
                var dto = JsonUtility.FromJson<ManifestDto>(json);
                if (dto == null) { error = "manifest is empty"; return null; }

                string schemaProblem = ContentSchema.Explain(dto.schemaVersion);
                if (schemaProblem != null) { error = "manifest " + schemaProblem; return null; }

                if (dto.chapters == null) dto.chapters = new ManifestChapterDto[0];
                return dto;
            }
            catch (System.Exception e)
            {
                error = "manifest is not valid JSON: " + e.Message;
                return null;
            }
        }

        // ------------------------------------------------------------- helpers
        static int WidestRow(string[] rows)
        {
            int widest = 0;
            foreach (var row in rows)
            {
                int count = 0;
                bool inToken = false;
                foreach (char c in row ?? string.Empty)
                {
                    bool space = char.IsWhiteSpace(c);
                    if (!space && !inToken) count++;
                    inToken = !space;
                }
                if (count > widest) widest = count;
            }
            return widest;
        }

        static Color? OptionalColour(string hex)
            => string.IsNullOrEmpty(hex) ? (Color?)null : ParseColour(hex, Color.white);

        static Color ParseColour(string hex, Color fallback)
            => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
