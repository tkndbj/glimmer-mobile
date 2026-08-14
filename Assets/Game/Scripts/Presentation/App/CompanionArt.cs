using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Draws a companion, and owns the roster's art lifetime.
    ///
    /// <para>
    /// Every companion has a still portrait; a few also have the flipbook they use as a
    /// board critter. Motion is spent only where it is looked at — the hero on the
    /// profile and the hub's top bar — while a grid of thirty companions draws stills.
    /// Thirty flipbooks is thirty folders of forty frames; thirty portraits is 1.4 MB.
    /// </para>
    /// <para>
    /// Screens showing the whole roster bracket themselves with <see cref="OpenAsync"/>
    /// and <see cref="Close"/> so the portraits live exactly as long as they are on
    /// screen. The worn companion is deliberately outside that bargain — see
    /// <see cref="Profile.WarmWornAvatar"/>.
    /// </para>
    /// </summary>
    public static class CompanionArt
    {
        public static Sprite Portrait(AvatarDefinition avatar)
            => avatar.IsValid ? Art.S("Companions/" + avatar.Portrait) : null;

        /// <summary>
        /// Puts a companion on an <see cref="Image"/>. Animates only when asked and only
        /// when that companion actually has frames; otherwise the portrait stands, which
        /// is what every companion added by a content drop will do.
        /// </summary>
        public static void Paint(Image target, AvatarDefinition avatar, bool animate = false)
        {
            if (target == null) return;

            // Deferred destruction means the old flipbook would spend a frame fighting
            // the new sprite for the same Image.
            var running = target.GetComponent<Flipbook>();
            if (running) { running.enabled = false; UnityEngine.Object.Destroy(running); }

            if (animate && avatar.HasAnimation)
            {
                target.color = Colour(target.color, 1f);
                Flipbook.Attach(target, "Critters/" + avatar.Animated, 13f);
                return;
            }

            var portrait = Portrait(avatar);
            target.sprite = portrait;

            // An Image with no sprite draws a solid white rectangle, and the roster's
            // art arrives a moment after the screen does. Hiding the frame rather than
            // flashing a white block is the difference between a load and a glitch.
            target.color = Colour(target.color, portrait == null ? 0f : 1f);
        }

        static Color Colour(Color c, float alpha) { c.a = alpha; return c; }

        /// <summary>
        /// Loads every portrait into the companion scope, then calls back so the screen
        /// can redraw with the art in hand.
        ///
        /// The callback is the whole point: the load is asynchronous, a screen is built
        /// in the same frame it is asked for, and without a repaint the first paint is
        /// the only one — which is exactly how a picker ends up showing blanks for
        /// every companion but the one the boot preload happened to warm.
        /// </summary>
        public static void OpenAsync(Action onReady = null)
        {
            if (AssetLibrary.IsScopeLoaded(AssetLibrary.CompanionScope))
            {
                onReady?.Invoke();
                return;
            }

            Load(onReady);
        }

        static async void Load(Action onReady)
        {
            try
            {
                await AssetLibrary.EnsureScopeAsync(AssetLibrary.CompanionScope,
                                                    AssetManifest.CompanionAssets(AvatarCatalog.All));
                onReady?.Invoke();
            }
            catch (Exception e)
            {
                // async void swallows exceptions; a roster that failed to load must not
                // vanish silently.
                Debug.LogException(e);
            }
        }

        /// <summary>Drops the roster's portraits. The worn one is global and survives.</summary>
        public static void Close() => AssetLibrary.ReleaseScope(AssetLibrary.CompanionScope);
    }
}
