using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Carries progress written by the original index-keyed PlayerPrefs build over to
    /// the id-keyed save file, once, on first launch after the update.
    ///
    /// The mapping below is frozen forever. It has to be a literal list rather than a
    /// walk of the live catalog: the moment a level is inserted or reordered, "index 1"
    /// stops meaning what it meant when those PlayerPrefs were written, and deriving
    /// the mapping from today's catalog would quietly move a player's stars onto the
    /// wrong levels. Never edit, reorder or extend this array — it is a record of what
    /// shipped, not a description of the game.
    /// </summary>
    public static class LegacyPlayerPrefsImport
    {
        /// <summary>Level ids in the exact order the pre-1.0 build indexed them.</summary>
        static readonly string[] LegacyIndexOrder =
        {
            "c01_first_light",      // was index 0
            "c01_twin_streams",     // was index 1
            "c01_prism_heart",      // was index 2
        };

        const string KeyStars = "gg.stars.";
        const string KeyBest = "gg.best.";
        const string KeyMusic = "gg.music";
        const string KeySfx = "gg.sfx";
        const string KeyHaptic = "gg.haptic";
        const string KeyTutorial = "gg.seentutorial";
        const string KeyCoins = "gg.coins";
        const string KeyGems = "gg.gems";
        const string KeyHearts = "gg.hearts";
        const string KeyName = "gg.name";

        /// <summary>
        /// Folds any legacy data into <paramref name="dto"/>. Returns true if anything
        /// was imported, in which case the caller should write the file.
        /// </summary>
        public static bool Apply(SaveFileDto dto)
        {
            if (dto == null || dto.legacyImportDone) return false;

            bool found = false;
            var records = new List<LevelRecordDto>(dto.levels);
            var byId = new Dictionary<string, LevelRecordDto>();
            foreach (var r in records) if (r?.levelId != null) byId[r.levelId] = r;

            long now = SaveSchema.NowUnix();

            for (int index = 0; index < LegacyIndexOrder.Length; index++)
            {
                int stars = PlayerPrefs.GetInt(KeyStars + index, 0);
                int best = PlayerPrefs.GetInt(KeyBest + index, 0);
                if (stars <= 0 && best <= 0) continue;

                string id = LegacyIndexOrder[index];
                found = true;

                if (!byId.TryGetValue(id, out var record))
                {
                    record = new LevelRecordDto { levelId = id };
                    records.Add(record);
                    byId[id] = record;
                }

                // The new file always wins if it somehow already holds a better result.
                if (stars > record.stars) record.stars = stars;
                if (best > 0 && (record.bestMoves == 0 || best < record.bestMoves)) record.bestMoves = best;
                if (record.clears == 0 && stars > 0) record.clears = 1;
                if (record.firstClearedUnix == 0 && stars > 0) record.firstClearedUnix = now;
            }

            found |= ImportSettings(dto);
            found |= ImportWallet(dto);

            dto.levels = records.ToArray();
            dto.legacyImportDone = true;

            if (found) Debug.Log("[Save] imported progress from the previous PlayerPrefs build");
            return true;
        }

        static bool ImportSettings(SaveFileDto dto)
        {
            bool found = false;

            if (dto.settings.music.state == StoredFlag.Unset && PlayerPrefs.HasKey(KeyMusic))
            {
                dto.settings.music.Set(PlayerPrefs.GetInt(KeyMusic, 1) == 1);
                found = true;
            }
            if (dto.settings.sfx.state == StoredFlag.Unset && PlayerPrefs.HasKey(KeySfx))
            {
                dto.settings.sfx.Set(PlayerPrefs.GetInt(KeySfx, 1) == 1);
                found = true;
            }
            if (dto.settings.haptics.state == StoredFlag.Unset && PlayerPrefs.HasKey(KeyHaptic))
            {
                dto.settings.haptics.Set(PlayerPrefs.GetInt(KeyHaptic, 1) == 1);
                found = true;
            }
            return found;
        }

        static bool ImportWallet(SaveFileDto dto)
        {
            bool found = false;
            dto.wallet ??= WalletDto.Unwritten();

            if (dto.wallet.coins < 0 && PlayerPrefs.HasKey(KeyCoins))
            {
                dto.wallet.coins = PlayerPrefs.GetInt(KeyCoins);
                found = true;
            }
            if (dto.wallet.gems < 0 && PlayerPrefs.HasKey(KeyGems))
            {
                dto.wallet.gems = PlayerPrefs.GetInt(KeyGems);
                found = true;
            }
            if (dto.wallet.hearts < 0 && PlayerPrefs.HasKey(KeyHearts))
            {
                dto.wallet.hearts = PlayerPrefs.GetInt(KeyHearts);
                found = true;
            }
            if (string.IsNullOrEmpty(dto.wallet.displayName) && PlayerPrefs.HasKey(KeyName))
            {
                dto.wallet.displayName = PlayerPrefs.GetString(KeyName);
                found = true;
            }
            return found;
        }

        /// <summary>Whether the old build's tutorial flag was set, for a one-off read.</summary>
        public static bool LegacyTutorialSeen => PlayerPrefs.GetInt(KeyTutorial, 0) == 1;

        /// <summary>
        /// Removes the legacy keys. Called only after a successful write of the new
        /// file, so a crash between the two leaves the old data intact to try again.
        /// </summary>
        public static void ClearLegacyKeys()
        {
            for (int i = 0; i < LegacyIndexOrder.Length; i++)
            {
                PlayerPrefs.DeleteKey(KeyStars + i);
                PlayerPrefs.DeleteKey(KeyBest + i);
            }
            PlayerPrefs.DeleteKey(KeyMusic);
            PlayerPrefs.DeleteKey(KeySfx);
            PlayerPrefs.DeleteKey(KeyHaptic);
            PlayerPrefs.DeleteKey(KeyCoins);
            PlayerPrefs.DeleteKey(KeyGems);
            PlayerPrefs.DeleteKey(KeyHearts);
            PlayerPrefs.DeleteKey(KeyName);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Sanity net for the frozen table: every legacy id must still exist in the
        /// catalog, or an updating player's stars would land nowhere. Editor-time check.
        /// </summary>
        public static IEnumerable<string> MissingFromCatalog(CatalogIndex index)
        {
            foreach (var raw in LegacyIndexOrder)
                if (!LevelId.TryParse(raw, out var id, out _) || index == null || !index.Contains(id))
                    yield return raw;
        }
    }
}
