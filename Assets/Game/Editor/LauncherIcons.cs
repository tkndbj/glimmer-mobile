using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
#if UNITY_IOS
using UnityEditor.iOS;
#endif

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
    ///
    /// Naming them costs a reference to the Android and iOS module assemblies, and those
    /// are <b>not</b> present on every machine that can build this game — which this file
    /// used to claim. A Mac set up to build only iOS has no Android module, and
    /// <c>AndroidPlatformIconKind</c> then does not exist as a type, so the whole file
    /// fails to compile and takes the Editor into safe mode on a fresh clone. Each half is
    /// therefore compiled only when its platform's module is present.
    ///
    /// <c>UNITY_ANDROID</c> and <c>UNITY_IOS</c> are the right guards for that despite
    /// being <em>build target</em> defines rather than module ones, because the target
    /// cannot be selected without the module: if the define is set the assembly is there.
    /// The cost is that icons are applied for the active platform only, which is why
    /// <see cref="Apply"/> says so out loud rather than quietly doing half the job — an
    /// unset icon becomes a default Unity logo on a store listing, and that is exactly the
    /// failure this file exists to prevent.
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

#if UNITY_IOS
            // iOS wants one square, opaque image per slot and masks the corners itself.
            // The same master feeds every kind; Unity resamples per slot at build time.
            foreach (var kind in IosKinds)
                Assign(NamedBuildTarget.iOS, kind, new[] { master });
#endif

#if UNITY_ANDROID
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
#endif

            // Said plainly, because the half that was skipped is invisible otherwise and
            // the symptom — a default Unity logo on one store's listing — turns up weeks
            // later in a review queue. Switch platform and run this again.
#if !UNITY_ANDROID
            Debug.LogWarning("[Glimmer] Android launcher icons were NOT applied: this Editor " +
                             "is not on the Android build target (or has no Android module). " +
                             "Switch to Android and run this again before shipping an AAB.");
#endif
#if !UNITY_IOS
            Debug.LogWarning("[Glimmer] iOS launcher icons were NOT applied: this Editor is " +
                             "not on the iOS build target (or has no iOS module). Switch to " +
                             "iOS and run this again before archiving in Xcode.");
#endif

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

            if (Slots.Length == 0)
            {
                Debug.LogWarning("[Glimmer] no platform module is active, so there are no " +
                                 "launcher icon slots to check. Switch to Android or iOS.");
                return;
            }

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

#if UNITY_IOS
        static readonly PlatformIconKind[] IosKinds =
        {
            iOSPlatformIconKind.Application, iOSPlatformIconKind.Settings,
            iOSPlatformIconKind.Notification, iOSPlatformIconKind.Spotlight,
            iOSPlatformIconKind.Marketing,
        };
#endif

#if UNITY_ANDROID
#pragma warning disable 618 // see Apply: the deprecated kinds still back android:icon
        static readonly PlatformIconKind[] AndroidKinds =
        {
            AndroidPlatformIconKind.Adaptive, AndroidPlatformIconKind.Round,
            AndroidPlatformIconKind.Legacy,
        };
#pragma warning restore 618
#endif

        /// <summary>
        /// The platforms this Editor can actually be asked about.
        ///
        /// Built rather than declared, because <see cref="Validate"/> must not report an
        /// unset icon for a platform whose module is not installed — that would be an error
        /// about something the machine cannot fix, on every fresh clone.
        /// </summary>
        static (NamedBuildTarget, PlatformIconKind[])[] Slots
        {
            get
            {
                var slots = new List<(NamedBuildTarget, PlatformIconKind[])>(2);
#if UNITY_IOS
                slots.Add((NamedBuildTarget.iOS, IosKinds));
#endif
#if UNITY_ANDROID
                slots.Add((NamedBuildTarget.Android, AndroidKinds));
#endif
                return slots.ToArray();
            }
        }

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
