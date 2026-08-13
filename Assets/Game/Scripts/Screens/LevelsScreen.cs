using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The glade map: a tall island chain you drag through. Every glade sits on its
    /// own floating rock, locked ones included, so the road ahead is always visible.
    /// </summary>
    public sealed class LevelsScreen : View
    {
        public override string Track => "mus_map";

        /// <summary>Content height in canvas units. Three 1200-unit map strips.</summary>
        const float MapHeight = 3600f;
        const int Strips = 3;

        ScrollRect _scroll;
        RectTransform _viewport, _map;
        readonly List<RectTransform> _nodes = new List<RectTransform>();

        static readonly string[] Rocks = { "rock_grass", "rock_wide", "rock_tall", "rock_chip", "rock_plain" };
        static readonly Vector2 SealedPos = new Vector2(0.66f, 0.845f);

        protected override void Build()
        {
            BuildScroller();
            BuildMapArt();

            for (int i = 0; i < Levels.Count - 1; i++) BuildTrail(i, i + 1);
            BuildTrail(Levels.Count - 1, -1);              // on to the sealed glade

            BuildScenery();

            for (int i = 0; i < Levels.Count; i++) BuildNode(i);
            BuildSealedNode();

            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.None);
            StartCoroutine(FocusCurrent());
        }

        // ------------------------------------------------------------- scroller
        void BuildScroller()
        {
            _viewport = UIKit.Node("Viewport", Content);
            // the map stops above the navigation bar, so nothing is ever hidden behind it
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);         // invisible, but drags land on it
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _map = UIKit.Node("Map", _viewport);
            _map.anchorMin = new Vector2(0f, 1f);
            _map.anchorMax = new Vector2(1f, 1f);
            _map.pivot = new Vector2(.5f, 1f);
            _map.sizeDelta = new Vector2(0f, MapHeight);
            _map.anchoredPosition = Vector2.zero;

            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _map;
            _scroll.viewport = _viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = .14f;
            _scroll.inertia = true;
            _scroll.decelerationRate = .04f;
            _scroll.scrollSensitivity = 55f;
        }

        void BuildMapArt()
        {
            for (int i = 0; i < Strips; i++)
            {
                var s = Art.S("Map/strip" + i);
                if (s == null) continue;
                var img = UIKit.Img("Strip" + i, _map, s, Color.white,
                                    new Vector2(1080f, 1200f), new Vector2(.5f, 0f),
                                    new Vector2(0f, 600f + i * 1200f));
                img.type = Image.Type.Simple;
            }
            // unify the strips and let the chrome read on top of them
            var shade = UIKit.Img("Shade", _map, Art.Pixel, new Color(.04f, .09f, .13f, .17f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, 0, 0, 0);
        }

        void BuildScenery()
        {
            (string art, float x, float y, float size, float bob)[] props =
            {
                ("palm",    0.14f, 0.055f, 190f, 6f),
                ("boulder", 0.86f, 0.135f, 150f, 0f),
                ("boat",    0.80f, 0.215f, 210f, 12f),
                ("stump",   0.16f, 0.315f, 150f, 0f),
                ("palm",    0.88f, 0.455f, 200f, 7f),
                ("post",    0.46f, 0.505f, 120f, 0f),
                ("boulder", 0.18f, 0.545f, 140f, 0f),
                ("stump",   0.82f, 0.665f, 160f, 0f),
                ("palm",    0.13f, 0.745f, 180f, 6f),
                ("boat",    0.84f, 0.975f, 190f, 11f),
                ("boulder", 0.30f, 0.945f, 130f, 0f),
            };
            foreach (var (art, x, y, size, bob) in props)
            {
                var s = Art.S("Map/" + art);
                if (s == null) continue;
                var img = UIKit.Img("Prop_" + art, _map, s, new Color(1f, 1f, 1f, .95f),
                                    Vector2.one * size, new Vector2(x, y), Vector2.zero);
                img.preserveAspect = true;
                if (bob > 0f) Tween.Bob((RectTransform)img.transform, bob, Random.Range(2.6f, 4.2f), Random.value * 6f);
            }
        }

        // ------------------------------------------------------------ the nodes
        void BuildTrail(int from, int to)
        {
            var a = Levels.All[from].MapPos;
            var b = to >= 0 ? Levels.All[to].MapPos : SealedPos;
            bool live = to >= 0 ? Save.Unlocked(to) : Save.AllCleared();
            var trail = _map.gameObject.AddComponent<Trail>();
            trail.Setup(_map, a, b, 13, live ? Pal.Gold : new Color(1f, .99f, .92f, .8f), live);
        }

        void BuildNode(int index)
        {
            var def = Levels.All[index];
            int stars = Save.Stars(index);
            bool unlocked = Save.Unlocked(index);

            var node = MakePerch(def.MapPos, Rocks[index % Rocks.Length], index);

            string skin = !unlocked ? "node_lock" : (stars > 0 ? "node_s" + stars : "node_open");
            if (unlocked && stars == 0) UIKit.Halo(node, def.Accent, 360f, .34f);

            var btn = UIKit.Button("Btn", node, Art.S("Map/" + skin), new Vector2(196f, 196f),
                                   new Vector2(.5f, .5f), new Vector2(0f, 2f), () => Open(index, unlocked));
            btn.GetComponent<Image>().preserveAspect = true;

            if (unlocked)
                UIKit.Titled("Num", btn.transform, (index + 1).ToString(), 62, new Color(.30f, .21f, .13f),
                             TextAnchor.MiddleCenter, new Vector2(190f, 110f), new Vector2(.5f, .5f),
                             new Vector2(0f, stars > 0 ? 14f : 4f), 0f, 2f);

            Plate(node, unlocked ? def.Name : "locked",
                  unlocked ? Pal.Cream : new Color(1f, 1f, 1f, .62f), -196f);

            node.localScale = Vector3.zero;
            Tween.Pop(node, 0f, .6f, .18f + index * .11f).OnDone(() => { if (btn) btn.Rehome(); });
            Tween.After(.2f + index * .11f, () => Audio.Sfx("pop", .32f, 1f + index * .09f), this);

            if (unlocked && stars == 0)
            {
                Tween.After(.55f + index * .11f, () => { if (btn) Tween.Breathe(btn.transform, .045f, 1.6f); }, this);
                var arrow = UIKit.Img("Pointer", node, Art.S("Map/pointer"), Color.white,
                                      new Vector2(92f, 100f), new Vector2(.5f, .5f), new Vector2(0f, 178f));
                arrow.preserveAspect = true;
                Tween.Bob((RectTransform)arrow.transform, 16f, 1.1f);
            }
        }

        /// <summary>A teaser at the top of the chain, so the road clearly continues.</summary>
        void BuildSealedNode()
        {
            var node = MakePerch(SealedPos, "rock_sand", 99);
            var disc = UIKit.Img("Seal", node, Art.S("Map/node_lock"), new Color(.88f, .90f, .94f, .95f),
                                 new Vector2(196f, 196f), new Vector2(.5f, .5f), new Vector2(0f, 2f));
            disc.preserveAspect = true;
            UIKit.Titled("Q", node, "?", 64, new Color(.36f, .38f, .44f), TextAnchor.MiddleCenter,
                         new Vector2(190f, 110f), new Vector2(.5f, .5f), new Vector2(0f, -2f), 0f, 2f);
            Plate(node, "more glades soon", new Color(1f, 1f, 1f, .62f), -196f);

            node.localScale = Vector3.zero;
            Tween.Pop(node, 0f, .6f, .18f + Levels.Count * .11f);
        }

        /// <summary>Floating rock with a soft shadow, gently bobbing.</summary>
        RectTransform MakePerch(Vector2 frac, string rock, int seed)
        {
            var node = UIKit.Node("Perch", _map);
            node.anchorMin = node.anchorMax = frac;
            node.pivot = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(360f, 420f);
            node.anchoredPosition = Vector2.zero;
            _nodes.Add(node);

            UIKit.Img("Shadow", node, Art.Glow(96, 2.2f), new Color(.03f, .10f, .16f, .38f),
                      new Vector2(370f, 150f), new Vector2(.5f, .5f), new Vector2(0f, -150f));

            var img = UIKit.Img("Rock", node, Art.S("Map/" + rock), Color.white,
                                new Vector2(360f, 290f), new Vector2(.5f, .5f), new Vector2(0f, -50f));
            img.preserveAspect = true;

            // contact shadow, so the glade disc looks planted rather than floating
            UIKit.Img("Contact", node, Art.Glow(96, 2.6f), new Color(.02f, .08f, .12f, .45f),
                      new Vector2(232f, 74f), new Vector2(.5f, .5f), new Vector2(0f, -44f));

            Tween.Bob(node, 8f, 3.1f + (seed % 5) * .27f, seed * 1.1f);
            return node;
        }

        static void Plate(Transform parent, string text, Color colour, float y)
        {
            var bg = UIKit.Img("Plate", parent, Art.Round(20), new Color(.04f, .09f, .13f, .74f),
                               new Vector2(340f, 62f), new Vector2(.5f, .5f), new Vector2(0f, y));
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(20, 3f), new Color(1, 1, 1, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);
            var t = UIKit.Titled("T", bg.transform, text, 32, colour, TextAnchor.MiddleCenter,
                                 outline: 3f, shadow: 3f);
            UIKit.StretchTo((RectTransform)t.transform, 12, 4, 12, 8);
        }

        // ---------------------------------------------------------------- chrome
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .78f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 300f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Content, "sq_dark", "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomeScreen>());

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
                                   new Vector2(520f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -142f));
            UIKit.Titled("Title", banner.transform, "CHOOSE A GLADE", 40, new Color(.36f, .24f, .16f),
                         TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            Scenery.Pill(Content, $"{Save.TotalStars()} / {Levels.Count * 3}", 36, new Vector2(196f, 78f),
                         new Vector2(1f, 1f), new Vector2(-106f, -132f), null, "ic_star");

            var swipe = UIKit.Titled("Swipe", Content, "swipe to explore the chain", 26,
                                     new Color(1f, .96f, .88f, .5f), TextAnchor.MiddleCenter,
                                     new Vector2(700f, 36f), new Vector2(.5f, 0f),
                                     new Vector2(0f, NavBar.Height + 52f), 3f, 0f);
            Tween.Tint(swipe, new Color(1f, .96f, .88f, 0f), .8f).Delay(4.2f);
        }

        // -------------------------------------------------------------- focusing
        /// <summary>Open at the bottom, then glide up to whichever glade is next.</summary>
        IEnumerator FocusCurrent()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            int guard = 0;
            while (_viewport.rect.height < 40f && guard++ < 60) yield return null;

            _scroll.verticalNormalizedPosition = 0f;

            int target = 0;
            for (int i = 0; i < Levels.Count; i++)
                if (Save.Unlocked(i) && Save.Stars(i) == 0) { target = i; break; }
            if (Save.AllCleared()) target = Levels.Count - 1;

            float want = NormalisedFor(Levels.All[target].MapPos.y);
            if (want <= 0.001f) yield break;

            yield return new WaitForSecondsRealtime(.35f);
            float from = _scroll.verticalNormalizedPosition;
            Tween.Run(.95f, Ease.InOutCubic, t =>
            {
                if (_scroll) _scroll.verticalNormalizedPosition = Mathf.Lerp(from, want, t);
            }, _scroll);
        }

        /// <summary>Scroll position that centres a fraction of the map in the viewport.</summary>
        float NormalisedFor(float fraction)
        {
            float v = _viewport.rect.height;
            float range = MapHeight - v;
            if (range <= 1f) return 0f;
            return Mathf.Clamp01((fraction * MapHeight - v * .5f) / range);
        }

        void Open(int index, bool unlocked)
        {
            if (!unlocked)
            {
                Audio.Sfx("nope", .55f);
                Tween.Shake(_nodes[index], 12f, .35f);
                Scenery.Toast(Content, "clear the glade before it to open this one", Pal.Parchment, 1.8f);
                return;
            }
            Audio.Sfx("unlock", .55f);
            Flow.Go<PlayScreen>(v => v.LevelIndex = index);
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }

    /// <summary>Row of drifting dots joining two points on the map.</summary>
    public sealed class Trail : MonoBehaviour
    {
        RectTransform _area;
        Vector2 _a, _b;
        Image[] _dots;
        bool _live;

        public void Setup(RectTransform area, Vector2 fracA, Vector2 fracB, int count, Color colour, bool live)
        {
            _area = area; _a = fracA; _b = fracB; _live = live;
            _dots = new Image[count];
            var host = UIKit.Node("Trail", area);
            for (int i = 0; i < count; i++)
            {
                float k = (i + 1f) / (count + 1f);
                float size = Mathf.Lerp(22f, 34f, Mathf.Sin(k * Mathf.PI));
                _dots[i] = UIKit.Img("d" + i, host, Art.Disc(64), colour,
                                     Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            }
        }

        void LateUpdate()
        {
            if (_area == null || _dots == null) return;
            var size = _area.rect.size;
            Vector2 pa = new Vector2((_a.x - .5f) * size.x, (_a.y - .5f) * size.y);
            Vector2 pb = new Vector2((_b.x - .5f) * size.x, (_b.y - .5f) * size.y);
            float bow = Mathf.Min(150f, Vector2.Distance(pa, pb) * .17f);
            var n = (pb - pa).normalized;
            float t = Time.unscaledTime;

            for (int i = 0; i < _dots.Length; i++)
            {
                if (!_dots[i]) continue;
                float k = (i + 1f) / (_dots.Length + 1f);
                var rt = (RectTransform)_dots[i].transform;
                var p = Vector2.Lerp(pa, pb, k) + new Vector2(-n.y, n.x) * Mathf.Sin(k * Mathf.PI) * bow;
                rt.anchoredPosition = p;
                var c = _dots[i].color;
                c.a = _live ? .32f + .48f * (.5f + .5f * Mathf.Sin(t * 2.6f - k * 7f)) : .38f;
                _dots[i].color = c;
                float s = _live ? 1f + .17f * Mathf.Sin(t * 2.6f - k * 7f) : 1f;
                rt.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
