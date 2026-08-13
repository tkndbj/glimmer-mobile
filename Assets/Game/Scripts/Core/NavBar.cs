using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The bottom navigation shared by the hub and the glade map. Built from the same
    /// dark rounded chrome as the resource pills, so it reads as one system.
    /// </summary>
    public static class NavBar
    {
        /// <summary>Vertical space the bar occupies. Screens keep their content above this.</summary>
        public const float Height = 206f;

        public enum Tab { None, Shop, Items, Home, Ranks }

        public static RectTransform Build(Transform parent, Tab active)
        {
            var bar = UIKit.Box("NavBar", parent, new Vector2(0f, Height), new Vector2(.5f, 0f),
                                new Vector2(0f, Height * .5f));
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.sizeDelta = new Vector2(0f, Height);

            // soft lift above the bar so it detaches from whatever scrolls behind it
            var lift = UIKit.Img("Lift", bar, Art.FadeUp(64), new Color(.02f, .07f, .09f, .48f),
                                 new Vector2(0f, 90f), new Vector2(.5f, 1f), new Vector2(0f, 44f));
            var liftRT = (RectTransform)lift.transform;
            liftRT.anchorMin = new Vector2(0f, 1f);
            liftRT.anchorMax = new Vector2(1f, 1f);
            liftRT.sizeDelta = new Vector2(0f, 90f);

            // the plate itself: wider and taller than the bar so only its top corners show
            var plate = UIKit.Img("Plate", bar, Art.Round(46), Pal.A(Pal.Hex("#0B4C55"), .97f),
                                  new Vector2(0f, Height + 90f), new Vector2(.5f, 1f), new Vector2(0f, -(Height + 90f) * .5f));
            var plateRT = (RectTransform)plate.transform;
            plateRT.anchorMin = new Vector2(0f, 1f);
            plateRT.anchorMax = new Vector2(1f, 1f);
            plateRT.offsetMin = new Vector2(-56f, -(Height + 90f));
            plateRT.offsetMax = new Vector2(56f, 0f);

            // emerald wash across the top of the plate, so the bar reads as lit rather than flat
            var wash = UIKit.Img("Wash", plate.transform, Art.FadeUp(64), Pal.A(Pal.Hex("#1AA184"), .95f));
            var washRT = (RectTransform)wash.transform;
            washRT.anchorMin = new Vector2(0f, 1f);
            washRT.anchorMax = new Vector2(1f, 1f);
            // pivot stays centred: rotating about a top pivot would throw the rect
            // upward and wash over whatever sits above the bar
            washRT.pivot = new Vector2(.5f, .5f);
            washRT.sizeDelta = new Vector2(0f, 168f);
            washRT.anchoredPosition = new Vector2(0f, -84f);
            washRT.localRotation = Quaternion.Euler(0, 0, 180f);

            var rim = UIKit.Img("Rim", plate.transform, Art.RoundOutline(46, 4f), new Color(1f, 1f, 1f, .16f));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);

            // a thin bright seam along the top edge
            var seam = UIKit.Img("Seam", bar, Art.Round(3), Pal.A(Pal.Gold, .70f),
                                 new Vector2(0f, 5f), new Vector2(.5f, 1f), new Vector2(0f, -2f));
            var seamRT = (RectTransform)seam.transform;
            seamRT.anchorMin = new Vector2(0f, 1f);
            seamRT.anchorMax = new Vector2(1f, 1f);
            seamRT.offsetMin = new Vector2(26f, -7f);
            seamRT.offsetMax = new Vector2(-26f, -2f);
            UIKit.Img("SeamGlow", bar, Art.Glow(128, 2.2f), Pal.A(Pal.Gold, .20f),
                      new Vector2(820f, 150f), new Vector2(.5f, 1f), new Vector2(0f, -6f));

            Item(bar, -315f, "ic_chest", "shop", active == Tab.Shop,
                 () => Flow.Modal<ComingSoonOverlay>(v => v.Configure("Shop", "ic_chest",
                     "Chests, critter skins and conduit styles will live here.")));
            Item(bar, -105f, "ic_gift", "items", active == Tab.Items,
                 () => Flow.Modal<ComingSoonOverlay>(v => v.Configure("Items", "ic_gift",
                     "Boosters and keepsakes you collect in the glades.")));
            Item(bar, 105f, "ic_home", "home", active == Tab.Home,
                 active == Tab.Home ? (Action)null : () => Flow.Go<HomeScreen>());
            Item(bar, 315f, "ic_trophy", "ranks", active == Tab.Ranks,
                 () => Flow.Modal<ComingSoonOverlay>(v => v.Configure("Ranks", "ic_trophy",
                     "Weekly leaderboards for the fastest, calmest solvers.")));

            return bar;
        }

        static void Item(Transform parent, float x, string icon, string label, bool active, Action onClick)
        {
            float size = active ? 142f : 124f;
            var b = UIKit.IconButton("Nav_" + label, parent, active ? "sq_green" : "sq_dark", icon,
                                     new Vector2(size, size), new Vector2(.5f, .5f),
                                     new Vector2(x, active ? 22f : 16f),
                                     onClick ?? (() => { }), active ? .52f : .5f);
            if (onClick == null) { b.ClickSfx = null; b.PressScale = .97f; }

            UIKit.Titled("L_" + label, parent, label, 24,
                         active ? Pal.Cream : new Color(1f, .96f, .88f, .60f), TextAnchor.MiddleCenter,
                         new Vector2(200f, 32f), new Vector2(.5f, .5f), new Vector2(x, active ? -70f : -64f), 3f, 0f);

            if (active) UIKit.Halo(b.transform, Pal.Mint, 210f, .32f);

            b.transform.localScale = Vector3.zero;
            Tween.Pop(b.transform, 0f, .5f, .62f + Mathf.Abs(x) * .00035f)
                 .OnDone(() => { if (b) b.Rehome(); });
        }
    }
}
