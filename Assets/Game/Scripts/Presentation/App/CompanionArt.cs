using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Async;
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
    /// Screens showing the whole roster take a hold through <see cref="Open"/> and dispose it
    /// when they go, so the portraits live exactly as long as somebody is drawing them. The
    /// worn companion is deliberately outside that bargain — see
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
        /// Takes a hold on every portrait in the roster. Dispose it when the screen goes.
        ///
        /// <para>
        /// <b>A hold rather than a named scope, and the marker interface is gone with it.</b>
        /// Three screens draw the roster and any of them can hand over to any other, so a
        /// leaving screen used to ask "does the one replacing me draw these too?" by reading
        /// <c>Flow.Current</c> against an <c>IDrawsCompanionArt</c> marker — a question that has
        /// to be re-answered every time a screen is added, and that was wrong twice. Counting
        /// answers it for every pair at once, including pairs nobody has written yet: the
        /// incoming screen has already taken its hold by the time the outgoing one lets go.
        /// </para>
        /// <para>
        /// The caller repaints from <paramref name="onReady"/>, which is the whole point of the
        /// shape: the load is asynchronous, a screen is built in the frame it is asked for, and
        /// without a repaint the first paint is the only one — exactly how a picker ends up
        /// showing blanks for every companion but the one the boot preload happened to warm.
        /// </para>
        /// </summary>
        public static AssetHold Open(MonoBehaviour host, Action onReady = null)
            => Take("companions", AssetManifest.CompanionAssets(AvatarCatalog.All), host, onReady);

        /// <summary>
        /// A hold on <em>one</em> companion's portrait, for a panel that draws exactly one.
        ///
        /// <para>
        /// The reveal and the unlock panels used to open the whole roster to draw a single face
        /// — thirty portraits for one, on a panel that can be raised over any screen in the
        /// game. Nothing about a hold made that necessary; it was the only shape the named scope
        /// offered, because a scope was all-or-nothing and a second one asking for less would
        /// have replaced the first.
        /// </para>
        /// </summary>
        public static AssetHold OpenOne(MonoBehaviour host, AvatarDefinition avatar,
                                        Action onReady = null)
            => Take("companion", AssetManifest.CompanionAssets(new[] { avatar }), host, onReady);

        static AssetHold Take(string name, System.Collections.Generic.List<AssetRequest> requests,
                              MonoBehaviour host, Action onReady)
        {
            var hold = AssetLibrary.Hold(name);

            Fire.AndForget(
                async () =>
                {
                    await hold.LoadAsync(requests);

                    // The host can be gone: these panels are raised over screens the player is
                    // free to leave, and a callback into a destroyed object is a null reference
                    // in whichever field it touches first.
                    if (host != null) onReady?.Invoke();
                },
                "CompanionArt." + name);

            return hold;
        }
    }
}
