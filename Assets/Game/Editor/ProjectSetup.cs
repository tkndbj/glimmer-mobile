using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// One-shot project wiring. The game builds its own scene graph at runtime, so
    /// the only thing the scene has to do is exist and be first in the build list.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Game/Scenes/Glimmer.unity";

        [MenuItem("Glimmer Grove/Set Up Project", false, 1)]
        public static void Setup()
        {
            EnsureScene();
            EnsureBuildSettings();
            ValidateLevels();
            ValidateArt();
            Debug.Log("[Glimmer] project setup complete");
        }

        static void EnsureScene()
        {
            var dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(ScenePath))
            {
                Debug.Log("[Glimmer] scene already present");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Glimmer] created " + ScenePath);
        }

        static void EnsureBuildSettings()
        {
            var scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("[Glimmer] build settings point at " + ScenePath);
        }

        [MenuItem("Glimmer Grove/Validate Levels", false, 20)]
        public static void ValidateLevels()
        {
            for (int i = 0; i < Levels.Count; i++)
            {
                var err = Levels.Validate(i);
                if (err != null) Debug.LogError("[Glimmer] " + err);
                else Debug.Log($"[Glimmer] level {i + 1} \"{Levels.All[i].Name}\" verified " +
                               $"({Levels.All[i].W}x{Levels.All[i].H}, par {Levels.All[i].Par})");
            }
        }

        [MenuItem("Glimmer Grove/Validate Art", false, 21)]
        public static void ValidateArt()
        {
            string[] sprites =
            {
                "Bg/home_sky", "Bg/home_ground", "Bg/home_deco",
                "Bg/map_sky", "Bg/map_ground", "Bg/map_deco", "Bg/play_0", "Bg/play_1", "Bg/play_2",
                "Bg/grove_far", "Bg/grove_near", "Bg/grove_light", "Bg/splash_far",
                "Ui/wood_panel", "Ui/ribbon_flat", "Ui/ic_plus", "Ui/ic_heart", "Ui/ic_gem",
                "Ui/ic_chest", "Ui/ic_chest_open", "Ui/ic_key", "Ui/ic_gift", "Ui/ic_star3d",
                "Ui/potion1", "Ui/potion6",
                "Map/strip0", "Map/strip1", "Map/strip2",
                "Map/rock_grass", "Map/rock_wide", "Map/rock_tall", "Map/rock_chip",
                "Map/rock_plain", "Map/rock_sand",
                "Map/palm", "Map/boulder", "Map/stump", "Map/boat", "Map/post",
                "Ui/panel_main", "Ui/banner", "Ui/ribbon_orange", "Ui/star_full", "Ui/star_empty",
                "Ui/padlock", "Ui/badge_star",
                "Ui/btn_green", "Ui/btn_blue", "Ui/btn_orange", "Ui/btn_red",
                "Ui/sq_green", "Ui/sq_blue", "Ui/sq_orange", "Ui/sq_gray", "Ui/sq_dark",
                "Ui/ic_left", "Ui/ic_pause", "Ui/ic_undo", "Ui/ic_restart", "Ui/ic_hint",
                "Ui/ic_music", "Ui/ic_audio", "Ui/ic_gear", "Ui/ic_star", "Ui/ic_check", "Ui/ic_list",
                "Map/node_open", "Map/node_lock", "Map/node_s1", "Map/node_s2", "Map/node_s3", "Map/pointer",
            };
            int bad = 0;
            foreach (var s in sprites)
                if (Resources.Load<Sprite>("Art/" + s) == null) { Debug.LogError("[Glimmer] missing sprite Art/" + s); bad++; }

            for (int i = 1; i <= 5; i++)
            {
                var f = Resources.LoadAll<Sprite>("Art/Critters/c" + i);
                if (f == null || f.Length == 0) { Debug.LogError($"[Glimmer] missing critter frames c{i}"); bad++; }
            }
            var vic = Resources.LoadAll<Sprite>("Art/Fx/Victory");
            if (vic == null || vic.Length == 0) { Debug.LogError("[Glimmer] missing victory frames"); bad++; }
            var coin = Resources.LoadAll<Sprite>("Art/Ui/Coin");
            if (coin == null || coin.Length == 0) { Debug.LogError("[Glimmer] missing coin frames"); bad++; }

            string[] clips =
            {
                "Audio/Sfx/click", "Audio/Sfx/back", "Audio/Sfx/press", "Audio/Sfx/rotate_a", "Audio/Sfx/rotate_b",
                "Audio/Sfx/lit", "Audio/Sfx/nope", "Audio/Sfx/win", "Audio/Sfx/star", "Audio/Sfx/pop",
                "Audio/Sfx/pop2", "Audio/Sfx/tick", "Audio/Sfx/whoosh", "Audio/Sfx/chime", "Audio/Sfx/unlock",
                "Audio/Sfx/shatter",
                "Audio/Music/mus_menu", "Audio/Music/mus_map", "Audio/Music/mus_play",
            };
            foreach (var c in clips)
                if (Resources.Load<AudioClip>(c) == null) { Debug.LogError("[Glimmer] missing clip " + c); bad++; }

            if (Resources.Load<Font>("Fonts/GameFont") == null)
                Debug.LogWarning("[Glimmer] no bundled font, falling back to the built-in one");

            Debug.Log(bad == 0 ? "[Glimmer] all art and audio present" : $"[Glimmer] {bad} assets missing");
        }
    }

    /// <summary>
    /// Safety net: everything under Game/Resources/Art is a UI sprite, whatever the
    /// meta file happens to say.
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Game/Resources/Art/")) return;
            var ti = (TextureImporter)assetImporter;
            if (ti.textureType == TextureImporterType.Sprite) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
        }
    }
}
