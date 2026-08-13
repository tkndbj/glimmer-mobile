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
        const string DefaultBackdrop = "play_0";

        /// <param name="orderFallback">
        /// Sort order from the manifest, used when the chapter file does not state one.
        /// The manifest is the index, so it is allowed to place a chapter that has no
        /// opinion about where it belongs.
        /// </param>
        public static bool TryReadChapter(string json, LevelCatalogBuilder builder, int orderFallback,
                                          out ChapterDefinition chapter, out List<LevelDefinition> levels)
        {
            chapter = null;
            levels = null;

            ChapterDto dto;
            try
            {
                dto = JsonUtility.FromJson<ChapterDto>(json);
            }
            catch (System.Exception e)
            {
                builder.Report($"chapter file is not valid JSON: {e.Message}");
                return false;
            }

            if (dto == null) { builder.Report("chapter file is empty"); return false; }

            string schemaProblem = ContentSchema.Explain(dto.schemaVersion);
            if (schemaProblem != null)
            {
                builder.Report($"chapter '{dto.id}' {schemaProblem}");
                return false;
            }

            if (!ChapterId.TryParse(dto.id, out var chapterId, out string idError))
            {
                builder.Report($"chapter id '{dto.id}' rejected: {idError}");
                return false;
            }

            levels = new List<LevelDefinition>();
            var levelIds = new List<LevelId>();

            if (dto.levels != null)
            {
                foreach (var levelDto in dto.levels)
                {
                    if (!TryReadLevel(levelDto, chapterId, builder, out var level)) continue;
                    levels.Add(level);
                    levelIds.Add(level.Id);
                }
            }

            chapter = new ChapterDefinition(
                chapterId,
                dto.order != 0 ? dto.order : orderFallback,
                dto.nameKey,
                ParseColour(dto.accent, ParseColour(DefaultAccentHex, Color.white)),
                ParseColour(dto.slate, ParseColour(DefaultSlateHex, Color.black)),
                string.IsNullOrEmpty(dto.backdrop) ? DefaultBackdrop : dto.backdrop,
                dto.mapStrips,
                levelIds);

            return true;
        }

        static bool TryReadLevel(LevelDto dto, ChapterId chapter, LevelCatalogBuilder builder,
                                 out LevelDefinition level)
        {
            level = null;
            if (dto == null) return false;

            if (!LevelId.TryParse(dto.id, out var id, out string idError))
            {
                builder.Report($"level id '{dto.id}' in chapter '{chapter}' rejected: {idError}");
                return false;
            }

            if (dto.rows == null || dto.rows.Length == 0)
            {
                builder.Report($"level '{id}' has no rows");
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
                builder.Report($"level '{id}' has an unusable grid: {e.Message}");
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
                    builder.Report($"level '{id}' cannot be parsed: {string.Join("; ", parsed.Errors)}");
                    return false;
                }
                par = PuzzleFactory.MinimumMoves(parsed.Cells);
            }

            var tuning = new LevelTuning(
                par,
                dto.goldFactor > 0f ? dto.goldFactor : LevelTuning.DefaultGoldFactor,
                dto.silverFactor > 0f ? dto.silverFactor : LevelTuning.DefaultSilverFactor,
                dto.hintAllowance > 0 ? dto.hintAllowance : LevelTuning.DefaultHintAllowance);

            var presentation = new LevelPresentation(
                new Vector2(dto.mapX, dto.mapY),
                OptionalColour(dto.accent),
                OptionalColour(dto.slate),
                dto.backdrop);

            level = new LevelDefinition(id, chapter, layout, tuning, presentation,
                                        dto.nameKey, dto.taglineKey, dto.lessonKey);
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
