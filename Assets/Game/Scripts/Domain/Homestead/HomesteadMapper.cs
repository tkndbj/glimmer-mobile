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

            // The grove body has its own floor beneath the shared schema gate. A v2 body
            // describes floating islands with hand-authored slots, which this build has no way
            // to draw - and reading it would produce a grove with no ground rather than a clear
            // refusal, which is the failure ContentSchema exists to prevent.
            if (dto.schemaVersion < FloorSchema)
            {
                problems.Add($"grove catalog is schema v{dto.schemaVersion}; the grove is a tile " +
                             $"floor from v{FloorSchema} and the islands it describes cannot be drawn");
                return false;
            }

            var floor = ReadFloor(dto.floor, problems);

            var pieceIds = new HashSet<string>(StringComparer.Ordinal);
            var pieces = new List<HomesteadPiece>();

            if (dto.pieces != null)
                foreach (var entry in dto.pieces)
                    if (TryReadPiece(entry, pieceIds, problems, out var piece))
                        pieces.Add(piece);

            catalog = new HomesteadCatalog(floor, pieces, ReadScores(dto.score, problems));
            return true;
        }

        // ----------------------------------------------------------------- score
        /// <summary>
        /// The star ladder, or null for a body that does not carry one — which is what a body
        /// written before the field existed produces, and is not an error.
        ///
        /// <para>
        /// Every rejection is reported and survivable, for this file's usual reason: a
        /// malformed rung is dropped and the rest of the ladder still stands, because the
        /// alternative is a grove screen with no score on it because one number was typed
        /// wrong. A ladder that ends up empty falls back to the built-in one rather than
        /// awarding no stars at all. <c>ContentValidation</c> refuses all of this before it
        /// can ship; this is what stops a bad drop reaching a player as a blank readout.
        /// </para>
        /// </summary>
        static GroveScoreTable ReadScores(GroveScoreDto dto, ICollection<string> problems)
        {
            if (dto == null || dto.stars == null || dto.stars.Length == 0) return null;

            var kept = new List<long>(dto.stars.Length);
            long previous = 0L;

            foreach (int at in dto.stars)
            {
                if (at <= 0)
                {
                    problems.Add($"the grove's star ladder holds {at}, which no score can be " +
                                 "below; the rung is dropped");
                    continue;
                }

                if (at <= previous)
                {
                    problems.Add($"the grove's star ladder does not rise: {at} comes after " +
                                 $"{previous}; the rung is dropped");
                    continue;
                }

                previous = at;
                kept.Add(at);
            }

            if (kept.Count == 0)
            {
                problems.Add("the grove's star ladder has no usable rung; the built-in one stands");
                return null;
            }

            if (kept.Count > GroveScoreTable.MaxStars)
            {
                problems.Add($"the grove's star ladder has {kept.Count} rungs, more than the " +
                             $"{GroveScoreTable.MaxStars} the readout can draw; the rest are dropped");
            }

            return new GroveScoreTable(kept);
        }

        // ----------------------------------------------------------------- floor
        /// <summary>
        /// The first grove schema that describes a floor. A body below this is refused rather
        /// than half-read - see <see cref="TryRead"/>.
        /// </summary>
        public const int FloorSchema = 3;

        /// <summary>Largest field this build will draw, in tiles on a side.</summary>
        public const int MaxFloorSide = 200;

        /// <summary>
        /// The ground, and the regions it is sold in.
        ///
        /// Every rejection is reported and survivable: a malformed region is dropped and the
        /// rest of the floor still loads, because content can arrive from a CDN and one bad row
        /// must not cost the player their whole grove. The one thing that cannot be salvaged is
        /// a field with no size, which produces <see cref="GroveFloor.Empty"/> and a screen that
        /// says so.
        /// </summary>
        static GroveFloor ReadFloor(GroveFloorDto dto, ICollection<string> problems)
        {
            if (dto == null)
            {
                problems.Add("grove catalog has no floor; there is nowhere to build");
                return GroveFloor.Empty;
            }

            if (dto.cols <= 0 || dto.rows <= 0)
            {
                problems.Add($"grove floor is {dto.cols}x{dto.rows}; a field needs both sides");
                return GroveFloor.Empty;
            }

            int cols = dto.cols, rows = dto.rows;

            // Clamped rather than refused: an oversized field is a content mistake that would
            // otherwise be a memory failure on a phone, and the safe half is a smaller grove.
            if (cols > MaxFloorSide || rows > MaxFloorSide)
            {
                problems.Add($"grove floor is {cols}x{rows}, larger than the {MaxFloorSide} " +
                             "tile limit; it is clamped");
                cols = Math.Min(cols, MaxFloorSide);
                rows = Math.Min(rows, MaxFloorSide);
            }

            var regionIds = new HashSet<string>(StringComparer.Ordinal);
            var regions = new List<GroveRegion>();

            if (dto.regions != null)
                foreach (var entry in dto.regions)
                    if (TryReadRegion(entry, cols, rows, regionIds, problems, out var region))
                        regions.Add(region);

            if (regions.Count == 0)
                problems.Add("grove floor has no regions; no tile belongs to anything, so none " +
                             "of it can be owned");

            string tileArt = dto.tileArt ?? string.Empty;

            string hall = CheckTile(dto.hallTile, cols, rows, "hallTile", problems);
            string starter = CheckTile(dto.starterTile, cols, rows, "starterTile", problems);

            if (!string.IsNullOrEmpty(hall) && string.Equals(hall, starter, StringComparison.Ordinal))
            {
                problems.Add("the grove's hall and its starter companion are on the same tile; " +
                             "the companion is dropped");
                starter = string.Empty;
            }

            return new GroveFloor(cols, rows, tileArt, hall, starter, regions);
        }

        /// <summary>
        /// A named tile that has to be on the field, or empty.
        ///
        /// Reported and dropped rather than clamped, because a hall moved silently to a tile
        /// nobody authored is worse than a grove with no hall: one is visibly wrong and the
        /// other is wrong somewhere the author will not look.
        /// </summary>
        static string CheckTile(string tileId, int cols, int rows, string field,
                                ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(tileId)) return string.Empty;

            if (!GroveFloor.TryParse(tileId, out int col, out int row))
            {
                problems.Add($"grove floor's {field} is '{tileId}', which is not a tile id");
                return string.Empty;
            }

            if (col < 0 || row < 0 || col >= cols || row >= rows)
            {
                problems.Add($"grove floor's {field} '{tileId}' is off a {cols}x{rows} field");
                return string.Empty;
            }

            return GroveFloor.TileId(col, row);
        }

        static bool TryReadRegion(GroveRegionDto dto, int cols, int rows,
                                  HashSet<string> ids, ICollection<string> problems,
                                  out GroveRegion region)
        {
            region = null;
            if (dto == null) return false;

            if (!IsCleanId(dto.id))
            {
                problems.Add($"grove region id '{dto.id}' is rejected: ids are written into save " +
                             "files, so they are lower case letters, digits and underscores, and " +
                             "no longer than " + MaxIdLength);
                return false;
            }

            if (!ids.Add(dto.id))
            {
                problems.Add($"grove lists region '{dto.id}' twice; the later entry is ignored");
                return false;
            }

            if (dto.cols <= 0 || dto.rows <= 0)
            {
                problems.Add($"grove region '{dto.id}' is {dto.cols}x{dto.rows}; it holds no tiles");
                return false;
            }

            if (dto.col < 0 || dto.row < 0
                || dto.col + dto.cols > cols || dto.row + dto.rows > rows)
            {
                problems.Add($"grove region '{dto.id}' runs off a {cols}x{rows} field");
                return false;
            }

            int cost = dto.cost;
            if (cost < 0)
            {
                problems.Add($"grove region '{dto.id}' has a negative cost ({cost}); " +
                             "treated as free");
                cost = 0;
            }

            region = new GroveRegion(dto.id, dto.col, dto.row, dto.cols, dto.rows, cost);
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

            // A resident is not authorable here any more: residents are the companion roster,
            // projected in by GroveResidents, so a row claiming to be one would be a second
            // creature list with its own price and its own unlock rule — which is the exact
            // duplication that projection removed. Dropped and named rather than read as decor,
            // because reading it as decor would put a critter on the fences tab and sell it.
            if (kind == HomesteadPieceKind.Resident)
            {
                problems.Add($"grove piece '{dto.id}' is authored as a resident; residents are " +
                             "the companion roster now and are projected in from the manifest, " +
                             "so this row is ignored — delete it, and see GroveResidents");
                return false;
            }

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

            // A bundle is clamped rather than reported unless it is nonsense: the build gate
            // has already refused a catalog whose price a bundle does not divide (see
            // ContentValidation.ValidateGrove), so anything arriving here that is out of range
            // came from a CDN rather than from an author, and a shop that sells a fence one at
            // a time is a worse outcome than a shop that does not open. Zero means one, for
            // `scale`'s reason.
            int bundle = dto.bundle;
            if (bundle < 0 || bundle > GroveStock.MaxCopies)
            {
                problems.Add($"grove piece '{dto.id}' is sold in bundles of {bundle}, which is " +
                             "outside what a purchase may grant; it is sold singly instead");
                bundle = 1;
            }

            piece = new HomesteadPiece(dto.id, art, dto.animated, kind, cost,
                                       requiresLevel, requiresChapter, dto.scale, dto.lift,
                                       ReadSlotKind(dto.slot, dto.id, problems), tier, bundle: bundle);
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
