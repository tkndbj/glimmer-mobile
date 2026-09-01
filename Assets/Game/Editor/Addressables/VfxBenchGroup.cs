#if GLIMMER_HAS_ADDRESSABLES

using System.Collections.Generic;
using GlimmerGrove.Dev;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Files the bought VFX pack into a bundle of its own, and switches that bundle off for
    /// every build that has not asked for it.
    ///
    /// <para>
    /// <b>Why the pack is not simply managed like the game's own art.</b>
    /// <see cref="AddressableAddresses"/> files everything under <c>Assets/Game/Art</c> into the
    /// global group, and the global group is downloaded by every player before they see
    /// anything. This pack is 74 textures, 59 of them 2048 square — 362MB of runtime texture
    /// memory as it ships, which is not a bundle, it is an uninstall. So it keeps its own root,
    /// its own group, and a switch.
    /// </para>
    /// <para>
    /// <b>The switch is <c>IncludeInBuild</c> rather than deleting the group</b>, because a
    /// deleted group is a thing somebody has to remember to recreate, and this file exists
    /// precisely because the project already learned that a step somebody has to remember on
    /// shipping week will be forgotten (invariant 7a). Flipping a flag is idempotent, and
    /// <see cref="Gate"/> flips it from the define on every build, so the bundle cannot be in a
    /// player build by accident and cannot be missing from a bench build by accident either.
    /// </para>
    /// <para>
    /// <b>Only prefabs get entries.</b> Their materials, meshes, shaders and textures come along
    /// as dependencies, which is Addressables doing the job properly: 175 entries rather than
    /// eight hundred, and nothing addressed that nothing asks for.
    /// </para>
    /// </summary>
    public static class VfxBenchGroup
    {
        /// <summary>
        /// The define that says a build wants the bench. Named here rather than only in the
        /// Player Settings, because <see cref="Gate"/> has to read it for a target that may not
        /// be the one the Editor is compiled against.
        /// </summary>
        public const string Define = "GLIMMER_BENCH";

        /// <summary>
        /// What the pack's textures are allowed to cost on a device.
        ///
        /// <para>
        /// 2048 is the right size for a projectile filling a desktop screen and absurd for one
        /// judged at a fifth of the width of a phone: capping at 512 is a sixteenth of the memory
        /// — 362MB down to about 23MB across the set — and is invisible at the size the bench
        /// draws them. It is set as a <em>platform override</em> rather than by lowering
        /// <c>maxTextureSize</c>, so the source art keeps its full resolution for
        /// <c>Tools/make_bud_fx.py</c>, which cuts the shipped Budburst frames out of the same
        /// pack and wants every pixel.
        /// </para>
        /// </summary>
        const int MobileTextureCap = 512;

        /// <summary>How they are encoded there. See <see cref="CapTextures"/> for why it is named.</summary>
        const TextureImporterFormat MobileTextureFormat = TextureImporterFormat.ASTC_6x6;

        // ------------------------------------------------------------------ menu
        [MenuItem("Glimmer Grove/Addressables/Sync VFX Bench", false, 62)]
        public static void SyncMenu()
        {
            var settings = AddressableRegistry.Settings(true);
            if (settings == null)
            {
                Debug.LogError("[Glimmer] could not create Addressables settings");
                return;
            }

            var summary = new AddressableRegistry.Summary();
            Sync(settings, ref summary);
            CapTextures();
            AddressableRegistry.Commit(settings, summary);

            bool on = WantedBy(EditorUserBuildSettings.selectedBuildTargetGroup);
            AddressableRegistry.Log($"vfx bench synced (in build: {on})", summary);
        }

        // ------------------------------------------------------------------ registration
        /// <summary>
        /// Puts every prefab of every kind in the group, addressed and labelled.
        ///
        /// Idempotent for <c>AddressableRegistry.Register</c>'s reason: run from a menu item, from
        /// the full sweep and from a build, so it has to report no change when there is none.
        /// </summary>
        public static void Sync(AddressableAssetSettings settings, ref AddressableRegistry.Summary summary)
        {
            if (settings == null) return;
            if (!AssetDatabase.IsValidFolder(VfxBench.PackRoot)) return;   // pack not imported

            var group = AddressableRegistry.EnsureGroup(settings, VfxBench.GroupName);
            if (group == null) return;

            var wanted = new HashSet<string>();

            for (int kind = 0; kind < VfxBench.Kinds.Length; kind++)
            {
                string folder = VfxBench.PackRoot + "/" + VfxBench.Kinds[kind];
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                string label = VfxBench.LabelFor(kind);
                settings.AddLabel(label, false);

                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    string address = VfxBench.AddressFor(kind, System.IO.Path.GetFileNameWithoutExtension(path));
                    Register(settings, group, path, address, label, wanted, ref summary);
                }
            }

            DropStrays(settings, group, wanted, ref summary);
        }

        /// <summary>
        /// Files one asset at one address, optionally under one label. Idempotent: an asset
        /// already in the right place reports no change and dirties nothing.
        /// </summary>
        static void Register(AddressableAssetSettings settings, AddressableAssetGroup group,
                             string path, string address, string label,
                             HashSet<string> wanted, ref AddressableRegistry.Summary summary)
        {
            wanted.Add(address);

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;

            bool isNew = settings.FindAssetEntry(guid) == null;

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null) return;

            if (entry.address != address) entry.SetAddress(address, false);
            if (isNew) summary.Registered++;

            if (label == null || entry.labels.Contains(label)) return;

            settings.AddLabel(label, false);
            entry.SetLabel(label, true, true, false);
            summary.Relabelled++;
        }

        /// <summary>
        /// Drops entries for prefabs the pack no longer has.
        ///
        /// The reason is the one <c>AddressableAudit.CheckEntriesStillExist</c> records: a dead
        /// entry does not break the game, it breaks <c>BuildPlayer</c> twenty minutes in with one
        /// file name buried in package internals. A bench whose pack is deleted must take its
        /// entries with it.
        /// </summary>
        static void DropStrays(AddressableAssetSettings settings, AddressableAssetGroup group,
                               HashSet<string> wanted, ref AddressableRegistry.Summary summary)
        {
            var doomed = new List<string>();

            foreach (var entry in group.entries)
            {
                if (entry == null) continue;
                if (wanted.Contains(entry.address)) continue;
                doomed.Add(entry.guid);
            }

            foreach (var guid in doomed)
                AddressableRegistry.Remove(settings, guid, ref summary);
        }

        /// <summary>
        /// Holds the pack's textures to <see cref="MobileTextureCap"/> on the two platforms this
        /// game ships to, in <b>ASTC</b>, and leaves the source art alone everywhere else.
        ///
        /// <para>
        /// <b>The format is named rather than left Automatic, and that is not a detail.</b>
        /// Automatic follows the build's texture-compression setting, which for this project is
        /// ETC2 — and ETC2 falls apart on exactly what a particle pack is made of: large, smooth,
        /// near-black gradients. The smoke behind a fireball came back from the device as grey
        /// rectangles, which reads as a broken effect rather than as compression. ASTC 6x6 is
        /// better on gradients <em>and</em> smaller (3.56 bits a pixel against ETC2 RGBA's 8), and
        /// every device this game targets supports it. It is set per texture rather than by
        /// changing the build setting, because that setting governs the whole game's own art.
        /// </para>
        /// </summary>
        static void CapTextures()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/GabrielAguiarProductions" });
            int changed = 0;

            // WriteImportSettingsIfDirty then ImportAsset, and deliberately *not*
            // StartAssetEditing wrapped around SaveAndReimport. That pairing is the one the
            // project's art rules use and it is wrong here: SaveAndReimport forces an immediate
            // import, which is exactly what the editing block exists to defer, and the two
            // together wedged the Editor on this set with nothing to show for it. Written this
            // way the whole pack re-imports in about fourteen seconds and each texture is a
            // step somebody can watch.
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                bool touched = false;
                foreach (var platform in new[] { "Android", "iPhone" })
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    if (settings.overridden && settings.maxTextureSize == MobileTextureCap
                                            && settings.format == MobileTextureFormat) continue;

                    settings.overridden = true;
                    settings.maxTextureSize = MobileTextureCap;
                    settings.format = MobileTextureFormat;
                    settings.textureCompression = TextureImporterCompression.Compressed;
                    importer.SetPlatformTextureSettings(settings);
                    touched = true;
                }

                if (!touched) continue;

                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                changed++;
            }

            if (changed > 0)
                Debug.Log($"[Glimmer] vfx bench: capped {changed} texture(s) at {MobileTextureCap} on mobile");
        }

        // ------------------------------------------------------------------ the switch
        /// <summary>Whether <paramref name="group"/>'s scripting defines ask for the bench.</summary>
        public static bool WantedBy(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Unknown) return false;

            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group));
            if (string.IsNullOrEmpty(defines)) return false;

            foreach (var symbol in defines.Split(';'))
                if (symbol.Trim() == Define) return true;

            return false;
        }

        /// <summary>
        /// Switches the bundle on or off to match the define, and answers what it did.
        ///
        /// <para>
        /// Returns quietly when there is no group, which is the ordinary state of a clone that
        /// has never imported the pack — a build gate that failed on a missing bench would make
        /// an optional tool a required one.
        /// </para>
        /// </summary>
        public static bool Gate(BuildTargetGroup target)
        {
            var settings = AddressableRegistry.Settings(false);
            var group = settings == null ? null : settings.FindGroup(VfxBench.GroupName);
            if (group == null) return false;

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) return false;

            bool wanted = WantedBy(target);
            if (schema.IncludeInBuild == wanted) return wanted;

            schema.IncludeInBuild = wanted;
            EditorUtility.SetDirty(schema);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Glimmer] vfx bench bundle {(wanted ? "included in" : "excluded from")} this build");
            return wanted;
        }
    }
}

#endif
