using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// Turns <c>homestead.json</c> into a validated <see cref="HomesteadCatalog"/>.
    ///
    /// <para>
    /// Every rejection is reported rather than thrown, for <c>ContentMapper</c>'s reason:
    /// content can arrive from a CDN, so this treats it as hostile input. One malformed
    /// piece is dropped and named and the rest of the catalog still loads — an exception on
    /// a background thread three days after a content drop is how live games lose a weekend.
    /// </para>
    /// <para>
    /// It is stricter about <em>identity</em> than about anything else. A piece with a
    /// duplicate id, or a slot with one, is refused rather than salvaged: both are written
    /// into save files, so a second row claiming the same id would put two different things
    /// in one player's grove depending on which the reader happened to index last, and
    /// nothing downstream could tell.
    /// </para>
    /// </summary>
    public static class HomesteadMapper
    {
        /// <summary>Slot and piece ids are written into save files, so they are kept plain.</summary>
        public const int MaxIdLength = 48;

        public static bool TryRead(string json, ICollection<string> problems, out HomesteadCatalog catalog)
        {
            catalog = null;

            HomesteadBodyDto dto;
            try
            {
                dto = JsonUtility.FromJson<HomesteadBodyDto>(json);
            }
            catch (Exception e)
            {
                problems.Add($"grove catalog is not valid JSON: {e.Message}");
                return false;
            }

            if (dto == null) { problems.Add("grove catalog is empty"); return false; }

            string schemaProblem = ContentSchema.Explain(dto.schemaVersion);
            if (schemaProblem != null)
            {
                problems.Add("grove catalog " + schemaProblem);
                return false;
            }

            var slotIds = new HashSet<string>(StringComparer.Ordinal);
            var plotIds = new HashSet<string>(StringComparer.Ordinal);
            var plots = new List<HomesteadPlot>();

            if (dto.plots != null)
                foreach (var entry in dto.plots)
                    if (TryReadPlot(entry, plotIds, slotIds, problems, out var plot))
                        plots.Add(plot);

            var pieceIds = new HashSet<string>(StringComparer.Ordinal);
            var pieces = new List<HomesteadPiece>();

            if (dto.pieces != null)
                foreach (var entry in dto.pieces)
                    if (TryReadPiece(entry, pieceIds, problems, out var piece))
                        pieces.Add(piece);

            catalog = new HomesteadCatalog(plots, pieces);
            return true;
        }

        // ----------------------------------------------------------------- plots
        static bool TryReadPlot(HomesteadPlotDto dto, HashSet<string> plotIds,
                                HashSet<string> slotIds, ICollection<string> problems,
                                out HomesteadPlot plot)
        {
            plot = null;
            if (dto == null) return false;

            if (!IsCleanId(dto.id))
            {
                problems.Add($"grove plot id '{dto.id}' is rejected: ids are lower case letters, " +
                             "digits and underscores, and no longer than " + MaxIdLength);
                return false;
            }

            if (!plotIds.Add(dto.id))
            {
                problems.Add($"grove lists plot '{dto.id}' twice; the later entry is ignored");
                return false;
            }

            var requires = ChapterId.None;
            if (!string.IsNullOrEmpty(dto.requiresChapter))
            {
                if (!ChapterId.TryParse(dto.requiresChapter, out requires, out string error))
                {
                    problems.Add($"grove plot '{dto.id}' requires chapter '{dto.requiresChapter}', " +
                                 $"which is rejected: {error}");
                    return false;
                }
            }

            if (string.IsNullOrEmpty(dto.art))
                problems.Add($"grove plot '{dto.id}' names no art; it will draw as nothing");

            var slots = new List<HomesteadSlot>();
            if (dto.slots != null)
            {
                foreach (var slotDto in dto.slots)
                {
                    if (slotDto == null) continue;

                    if (!IsCleanId(slotDto.id))
                    {
                        problems.Add($"grove plot '{dto.id}' has a slot with an unusable id " +
                                     $"'{slotDto.id}'; ids are written into save files, so they " +
                                     "are lower case letters, digits and underscores");
                        continue;
                    }

                    // Across the whole grove, not just this plot. A slot id is the key of a
                    // map in the save file, so two plots sharing one would make a tree placed
                    // on the first island appear on the second — and the merge would then
                    // treat two independent choices as one.
                    if (!slotIds.Add(slotDto.id))
                    {
                        problems.Add($"grove slot id '{slotDto.id}' is used twice; slot ids key " +
                                     "the save file and must be unique across every plot");
                        continue;
                    }

                    slots.Add(new HomesteadSlot(slotDto.id, slotDto.x, slotDto.y, slotDto.scale,
                                                ReadSlotKind(slotDto.kind, slotDto.id, problems)));
                }
            }

            if (slots.Count == 0)
                problems.Add($"grove plot '{dto.id}' has no slots; nothing can be placed on it");

            plot = new HomesteadPlot(dto.id, dto.art, dto.x, dto.width, requires, slots);
            return true;
        }

        // ---------------------------------------------------------------- pieces
        static bool TryReadPiece(HomesteadPieceDto dto, HashSet<string> pieceIds,
                                 ICollection<string> problems, out HomesteadPiece piece)
        {
            piece = default;
            if (dto == null) return false;
            if (dto.disabled) return false;

            if (!IsCleanId(dto.id))
            {
                problems.Add($"grove piece id '{dto.id}' is rejected: ids are written into save " +
                             "files, so they are lower case letters, digits and underscores, and " +
                             "no longer than " + MaxIdLength);
                return false;
            }

            if (!pieceIds.Add(dto.id))
            {
                problems.Add($"grove lists piece '{dto.id}' twice; the later entry is ignored");
                return false;
            }

            var kind = ReadKind(dto, problems);

            var requiresLevel = LevelId.None;
            if (!string.IsNullOrEmpty(dto.requiresLevel) &&
                !LevelId.TryParse(dto.requiresLevel, out requiresLevel, out string levelError))
            {
                problems.Add($"grove piece '{dto.id}' requires level '{dto.requiresLevel}', " +
                             $"which is rejected: {levelError}");
                return false;
            }

            var requiresChapter = ChapterId.None;
            if (!string.IsNullOrEmpty(dto.requiresChapter) &&
                !ChapterId.TryParse(dto.requiresChapter, out requiresChapter, out string chapterError))
            {
                problems.Add($"grove piece '{dto.id}' requires chapter '{dto.requiresChapter}', " +
                             $"which is rejected: {chapterError}");
                return false;
            }

            int cost = dto.cost;

            // Reported rather than clamped silently, because a negative price is the one
            // authoring slip here that looks like a working feature: it reads as "not for
            // sale", so the piece simply loses its buy button and nothing else complains.
            if (cost < 0)
            {
                problems.Add($"grove piece '{dto.id}' has a negative cost ({cost}); " +
                             "treated as not for sale");
                cost = 0;
            }

            // The one rule the two kinds do not share, enforced here as well as in the build
            // gate. A resident is proof of a glade the player finished; a priced one turns the
            // grove from a record of what they did into a receipt, which is the whole reason
            // this feature exists. Dropping the price is the safe half — the piece stays
            // earnable and nothing anybody bought is affected.
            if (kind == HomesteadPieceKind.Resident && cost > 0)
            {
                problems.Add($"grove resident '{dto.id}' has a price ({cost}); residents are " +
                             "earned by playing and are never for sale, so the price is ignored");
                cost = 0;
            }

            int tier = dto.tier;

            // A tier is what orders the home ladder, so an unnumbered dwelling would make
            // "the best one owned" depend on the order of the file. Reported and read as the
            // first rung, which is the safe half: the ladder still works and the worst case is
            // a home that does not upgrade, rather than one that silently downgrades.
            if (kind == HomesteadPieceKind.Dwelling && tier <= 0)
            {
                problems.Add($"grove dwelling '{dto.id}' has no tier; read as tier 1");
                tier = 1;
            }

            string art = string.IsNullOrEmpty(dto.art) ? DefaultArt(dto.id) : dto.art;

            piece = new HomesteadPiece(dto.id, art, dto.animated, kind, cost,
                                       requiresLevel, requiresChapter, dto.scale, dto.lift,
                                       ReadSlotKind(dto.slot, dto.id, problems), tier);
            return true;
        }

        /// <summary>
        /// A slot kind by name, defaulting to ground.
        ///
        /// Unknown names are reported and read as <see cref="HomesteadSlotKind.Ground"/>, never
        /// refused. A slot is a key in the save file: dropping one because a newer catalog
        /// called it something this build has not heard of would punch a hole in an island the
        /// player arranged, and ground is the safe half — it is the kind that accepts the
        /// ordinary catalog.
        /// </summary>
        static HomesteadSlotKind ReadSlotKind(string name, string owner, ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(name)) return HomesteadSlotKind.Ground;

            foreach (HomesteadSlotKind kind in Enum.GetValues(typeof(HomesteadSlotKind)))
                if (string.Equals(name, kind.ToString(), StringComparison.OrdinalIgnoreCase))
                    return kind;

            problems.Add($"grove '{owner}' names slot kind '{name}', which this build does not " +
                         "know; read as ground");
            return HomesteadSlotKind.Ground;
        }

        /// <summary>
        /// Where a piece that names no art of its own looks for it.
        ///
        /// Deliberately only expressible for decor: a resident's art is a critter set the
        /// board already owns, and there is no folder this could guess that would be right.
        /// </summary>
        public static string DefaultArt(string id) => "Homestead/" + id;

        static HomesteadPieceKind ReadKind(HomesteadPieceDto dto, ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(dto.kind)) return HomesteadPieceKind.Decor;

            if (string.Equals(dto.kind, "resident", StringComparison.OrdinalIgnoreCase))
                return HomesteadPieceKind.Resident;

            if (string.Equals(dto.kind, "decor", StringComparison.OrdinalIgnoreCase))
                return HomesteadPieceKind.Decor;

            if (string.Equals(dto.kind, "dwelling", StringComparison.OrdinalIgnoreCase))
                return HomesteadPieceKind.Dwelling;

            // Read as decor rather than refused, for the reason an unknown event mark draws
            // the default one: a piece this build does not fully understand is still a piece,
            // and the kind decides a shop tab rather than anything a player can lose. Decor is
            // the safe half — it is the kind that may carry a price.
            problems.Add($"grove piece '{dto.id}' has kind '{dto.kind}', which this build does " +
                         "not know; read as decor");
            return HomesteadPieceKind.Decor;
        }

        /// <summary>
        /// The same rule <c>CatalogIndexBuilder</c> applies to companion and event ids, and
        /// for the same reason: these are written into save files and into analytics keys.
        /// </summary>
        static bool IsCleanId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;

            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }

            return true;
        }
    }
}
