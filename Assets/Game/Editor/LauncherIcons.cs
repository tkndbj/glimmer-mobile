using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.iOS;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Puts the app icon into PlayerSettings, and proves it stayed there.
    ///
    /// The icons themselves are generated — <c>Tools/make_launcher_icons.py</c> derives
    /// every shape below from one authored image, so re-skinning the app is a re-run of
    /// that script and a click of "Apply Launcher Icons", not thirty-seven drag-and-drops
    /// into the inspector. This file is what turns the files on disk into settings.
    ///
    /// Assignment is by asset path rather than by object reference: PlayerSettings stores
    /// icons as GUIDs, and a GUID that no longer resolves silently becomes an empty slot
    /// and a default Unity logo on a store listing. <see cref="Validate"/> fails loudly
    /// on that instead, which is why it runs at the end of <see cref="Apply"/>.
    ///
    /// The icon kinds are named rather than discovered, because Android's Round and
    /// Legacy kinds are indistinguishable at runtime — same slot sizes, same layer count —
    /// and guessing between them would put a circle where a rounded square belongs.
    /// Naming them costs a reference to the Android and iOS module assemblies, which
    /// every machine that can build this game already has.
    ///
    /// Icons deliberately live outside <c>Assets/Game/Art</c>. Everything under that
    /// folder is forced to a UI sprite by <c>ArtImportRules</c> and swept into an
    /// Addressables group by <c>AddressablesMigration</c>; an app icon is neither — it is
    /// consumed by the build pipeline, never loaded through <c>AssetLibrary</c>.
    /// </summary>
    public static class LauncherIcons
    {
        const string Root = "Assets/Game/Branding/Icons";

        const string Master     = Root + "/icon_master_1024.png";
        const string Legacy     = Root + "/icon_android_legacy_512.png";
        const string Round      = Root + "/icon_android_round_512.png";
        const string Background = Root + "/icon_android_adaptive_background_432.png";
        const string Foreground = Root + "/icon_android_adaptive_foreground_432.png";

        [MenuItem("Glimmer Grove/Apply Launcher Icons", false, 22)]
        public static void Apply()
        {
            var master     = Load(Master);
            var background = Load(Background);
            var foreground = Load(Foreground);
            var round      = Load(Round);
            var legacy     = Load(Legacy);

            if (master == null || background == null || foreground == null
                || round == null || legacy == null) return;

            // iOS wants one square, opaque image per slot and masks the corners itself.
            // The same master feeds every kind; Unity resamples per slot at build time.
            foreach (var kind in IosKinds)
                Assign(NamedBuildTarget.iOS, kind, new[] { master });

            // Android 8 and up — every device this game supports, AndroidMinSdkVersion is
            // 26 — draws the adaptive pair and ignores the other two kinds. Layer order is
            // background first, foreground second; swapping them hides the character.
            Assign(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive,
                   new[] { background, foreground });

            // Unity deprecated Round and Legacy in favour of Adaptive, but the manifest
            // still carries android:icon and android:roundIcon, and an OEM launcher that
            // reads those and finds nothing falls back to the default Unity logo. Filling
            // them costs two PNGs and removes that failure mode entirely.
#pragma warning disable 618
            Assign(NamedBuildTarget.Android, AndroidPlatformIconKind.Round, new[] { round });
            Assign(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy, new[] { legacy });
#pragma warning restore 618

            // The default icon is what the Editor, and any desktop build made while
            // debugging, shows. It is not shipped, but an unset one is confusing.
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { master }, IconKind.Any);

            AssetDatabase.SaveAssets();
            Debug.Log("[Glimmer] launcher icons applied");
            Validate();
        }

        [MenuItem("Glimmer Grove/Validate Launcher Icons", false, 23)]
        public static void Validate()
        {
            var unset = new List<string>();
            var total = 0;

            foreach (var (target, kinds) in Slots)
            {
                foreach (var kind in kinds)
                {
                    foreach (var icon in PlayerSettings.GetPlatformIcons(target, kind))
                    {
                        total++;
                        var filled = icon.GetTextures().Count(t => t != null);
                        if (filled < icon.maxLayerCount)
                            unset.Add($"{target.TargetName} {icon.width}x{icon.height}" +
                                      $" has {filled} of {icon.maxLayerCount} layer(s)");
                    }
                }
            }

            foreach (var slot in unset) Debug.LogError("[Glimmer] unset launcher icon — " + slot);

            Debug.Log(unset.Count == 0
                ? $"[Glimmer] all {total} launcher icon slot(s) assigned"
                : $"[Glimmer] {unset.Count} of {total} launcher icon slot(s) unassigned");
        }

        static readonly PlatformIconKind[] IosKinds =
        {
            iOSPlatformIconKind.Application, iOSPlatformIconKind.Settings,
            iOSPlatformIconKind.Notification, iOSPlatformIconKind.Spotlight,
            iOSPlatformIconKind.Marketing,
        };

#pragma warning disable 618 // see Apply: the deprecated kinds still back android:icon
        static readonly PlatformIconKind[] AndroidKinds =
        {
            AndroidPlatformIconKind.Adaptive, AndroidPlatformIconKind.Round,
            AndroidPlatformIconKind.Legacy,
        };
#pragma warning restore 618

        static (NamedBuildTarget, PlatformIconKind[])[] Slots =>
            new[] { (NamedBuildTarget.iOS, IosKinds), (NamedBuildTarget.Android, AndroidKinds) };

        static void Assign(NamedBuildTarget target, PlatformIconKind kind, Texture2D[] layers)
        {
            var icons = PlayerSettings.GetPlatformIcons(target, kind);
            foreach (var icon in icons) icon.SetTextures(layers);
            PlayerSettings.SetPlatformIcons(target, kind, icons);
        }

        static Texture2D Load(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                Debug.LogError($"[Glimmer] missing launcher icon {path} — " +
                               "run 'python Tools/make_launcher_icons.py' from the repo root");
            return texture;
        }
    }
}
