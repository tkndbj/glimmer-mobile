using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The glade map: one chapter's island chain, dragged through vertically.
    ///
    /// One chapter at a time is the load-bearing decision. It bounds the node count
    /// and the loaded texture count by a chapter's size rather than by the size of
    /// the catalog, so the map costs the same at chapter fifty as at chapter one —
    /// no virtualisation, no pooling, no cleverness required. Arrows at the edges
    /// move between chapters, and the screen opens on wherever the player is up to.
    /// </summary>
    public sealed class LevelsScreen : View
    {
        /// <summary>Which chapter to show. Defaults to wherever the player is up to.</summary>
        public ChapterId ChapterId;

        public override string Track => "mus_map";

        ScrollRect _scroll;
        RectTransform _viewport, _map;
        LevelCatalog _catalog;
        CatalogIndex _index;

        /// <summary>What the manifest knows: identity, order, membership. Always here.</summary>
        ChapterIndexEntry _entry;

        /// <summary>This chapter's grids and art keys. Awaited before the map is drawn.</summary>
        ChapterBody _body;

        MapLayout _layout;
        readonly Dictionary<LevelId, RectTransform> _nodes = new Dictionary<LevelId, RectTransform>();

        static readonly string[] Rocks = { "rock_grass", "rock_wide", "rock_tall", "rock_chip", "rock_plain" };

        /// <summary>
        /// Seconds between one glade popping in and the next, and the longest the whole
        /// arrival is allowed to take. The step shrinks as a chapter grows rather than
        /// the entrance growing with it — at a flat 0.11s a thirty glade chapter would
        /// still be assembling itself three seconds after the player arrived.
        /// </summary>
        const float PopStagger = .11f;
        const float PopStaggerTotal = 1.4f;

        static float PopDelay(int index, int count)
        {
            float step = count > 1 ? Mathf.Min(PopStagger, PopStaggerTotal / (count - 1)) : 0f;
            return index * step;
        }

        protected override void Build()
        {
            _catalog = GameContent.Catalog;
            _index = _catalog.Index;

            _entry = ChapterId.IsValid
                ? _index.FindChapter(ChapterId)
                : LevelUnlock.CurrentChapter(_index);

            // The header is index knowledge - chapter name, total stars - so it draws
            // immediately and never waits on a file.
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.None);

            if (_entry == null) return;

            ChapterId = _entry.Id;
            StartCoroutine(BuildChapter());
        }

        /// <summary>
        /// Draws the map once this chapter's body is in hand.
        ///
        /// Usually there is nothing to wait for: the splash loaded the opening chapter,
        /// and stepping to a neighbour normally finds it still resident, so the task is
        /// already complete and not a single frame is lost. The wait only materialises
        /// on a genuine first visit - which is the one moment the file is actually
        /// needed, and the only content the player ever pays to load.
        /// </summary>
        IEnumerator BuildChapter()
        {
            var bodyTask = _catalog.ChapterAsync(_entry.Id);
            while (!bodyTask.IsCompleted) yield return null;

            if (bodyTask.IsFaulted) { Debug.LogException(bodyTask.Exception); yield break; }

            _body = bodyTask.Result;
            if (_body == null || !this) yield break;

            _layout = MapLayout.Build(_body, _entry.LevelIds);

            // Swapping chapters swaps their art; this is where the previous
            // chapter's backdrops and strips are actually released.
            _ = AssetLibrary.EnsureChapterAsync(_body);

            BuildScroller();
            BuildMapArt();
            BuildTrails();
            BuildNodes();
            BuildChapterEnd();
            BuildChapterArrows();

            yield return FocusCurrent();
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
            _map.sizeDelta = new Vector2(0f, _layout.TotalHeight);
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
            for (int i = 0; i < _layout.Strips.Count; i++)
            {
                var sprite = Art.S("Map/" + _layout.Strips[i]);
                if (sprite == null) continue;

                var img = UIKit.Img("Strip" + i, _map, sprite, Color.white,
                                    new Vector2(1080f, MapLayout.StripHeight), new Vector2(.5f, 0f),
                                    new Vector2(0f, _layout.StripBottom(i) + MapLayout.StripHeight * .5f));
                img.type = Image.Type.Simple;
            }

            BuildScenery();

            // unify the strips and let the chrome read on top of them
            var shade = UIKit.Img("Shade", _map, Art.Pixel, new Color(.04f, .09f, .13f, .17f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, 0, 0, 0);
            shade.transform.SetAsLastSibling();
        }

        /// <summary>
        /// Decorative props, positioned as fractions of this chapter's map so the
        /// same arrangement works for a chapter of any height.
        /// </summary>
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
        void BuildTrails()
        {
            var levels = _layout.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                bool last = i == levels.Count - 1;
                var from = _layout.PositionOf(levels[i].Id);
                var to = last ? _layout.TeaserPosition : _layout.PositionOf(levels[i + 1].Id);
                bool live = last
                    ? PlayerProgress.IsCleared(levels[i].Id)
                    : LevelUnlock.IsUnlocked(_index, levels[i + 1].Id);

                var trail = _map.gameObject.AddComponent<Trail>();
                trail.Setup(_map, from, to, 13, live ? Pal.Gold : new Color(1f, .99f, .92f, .8f), live);
            }
        }

        void BuildNodes()
        {
            var levels = _layout.Levels;
            for (int i = 0; i < levels.Count; i++) BuildNode(levels[i], i);
        }

        void BuildNode(LevelDefinition level, int indexInChapter)
        {
            int stars = PlayerProgress.Stars(level.Id);
            bool unlocked = LevelUnlock.IsUnlocked(_index, level.Id);

            // Numbering runs across the whole game, so glade 3 of chapter 2 still
            // reads as its true position in the catalog.
            int displayNumber = _index.OrderOf(level.Id) + 1;

            var node = MakePerch(_layout.PositionOf(level.Id), Rocks[indexInChapter % Rocks.Length], indexInChapter);
            _nodes[level.Id] = node;

            string skin = !unlocked ? "node_lock" : (stars > 0 ? "node_s" + stars : "node_open");
            if (unlocked && stars == 0)
                UIKit.Halo(node, level.Presentation.ResolveAccent(_body.Definition), 360f, .34f);

            var id = level.Id;
            var btn = UIKit.Button("Btn", node, Art.S("Map/" + skin), new Vector2(196f, 196f),
                                   new Vector2(.5f, .5f), new Vector2(0f, 2f), () => Open(id, unlocked));
            btn.GetComponent<Image>().preserveAspect = true;

            if (unlocked)
                UIKit.Titled("Num", btn.transform, displayNumber.ToString(), 62, new Color(.30f, .21f, .13f),
                             TextAnchor.MiddleCenter, new Vector2(190f, 110f), new Vector2(.5f, .5f),
                             new Vector2(0f, stars > 0 ? 14f : 4f), 0f, 2f);

            Plate(node, unlocked ? Loc.Get(level.NameKey) : Loc.Get("ui.levels.locked"),
                  unlocked ? Pal.Cream : new Color(1f, 1f, 1f, .62f), -196f);

            float delay = PopDelay(indexInChapter, _layout.Levels.Count);

            node.localScale = Vector3.zero;
            Tween.Pop(node, 0f, .6f, .18f + delay).OnDone(() => { if (btn) btn.Rehome(); });
            Tween.After(.2f + delay,
                        () => Audio.Sfx("pop", .32f, 1f + indexInChapter * .09f), this);

            if (unlocked && stars == 0)
            {
                Tween.After(.55f + delay,
                            () => { if (btn) Tween.Breathe(btn.transform, .045f, 1.6f); }, this);
                var arrow = UIKit.Img("Pointer", node, Art.S("Map/pointer"), Color.white,
                                      new Vector2(92f, 100f), new Vector2(.5f, .5f), new Vector2(0f, 178f));
                arrow.preserveAspect = true;
                Tween.Bob((RectTransform)arrow.transform, 16f, 1.1f);
            }
        }

        /// <summary>
        /// Caps the chain: either a signpost onward to the next chapter, or the
        /// sealed teaser when this is the newest content there is.
        /// </summary>
        void BuildChapterEnd()
        {
            var next = LevelUnlock.ChapterAfter(_index, _entry.Id);
            var node = MakePerch(_layout.TeaserPosition, "rock_sand", 99);

            bool onward = next != null;
            bool reachable = onward && LevelUnlock.IsChapterUnlocked(_index, next.Id);

            var disc = UIKit.Img("Seal", node, Art.S("Map/" + (reachable ? "node_open" : "node_lock")),
                                 reachable ? Color.white : new Color(.88f, .90f, .94f, .95f),
                                 new Vector2(196f, 196f), new Vector2(.5f, .5f), new Vector2(0f, 2f));
            disc.preserveAspect = true;

            if (reachable)
            {
                var target = next.Id;
                var btn = UIKit.Button("Btn", node, Art.S("Map/node_open"), new Vector2(196f, 196f),
                                       new Vector2(.5f, .5f), new Vector2(0f, 2f), () => GoToChapter(target));
                btn.GetComponent<Image>().preserveAspect = true;
                UIKit.Titled("Arrow", btn.transform, "»", 64, new Color(.30f, .21f, .13f),
                             TextAnchor.MiddleCenter, new Vector2(190f, 110f), new Vector2(.5f, .5f),
                             new Vector2(0f, -2f), 0f, 2f);
                Plate(node, Loc.Get(next.NameKey), Pal.Cream, -196f);
            }
            else
            {
                UIKit.Titled("Q", node, "?", 64, new Color(.36f, .38f, .44f), TextAnchor.MiddleCenter,
                             new Vector2(190f, 110f), new Vector2(.5f, .5f), new Vector2(0f, -2f), 0f, 2f);
                Plate(node, Loc.Get(onward ? "ui.levels.chapter_locked" : "ui.levels.more_soon"),
                      new Color(1f, 1f, 1f, .62f), -196f);
            }

            int count = _layout.Levels.Count;

            node.localScale = Vector3.zero;
            Tween.Pop(node, 0f, .6f, .18f + PopDelay(count, count));
        }

        /// <summary>Floating rock with a soft shadow, gently bobbing.</summary>
        RectTransform MakePerch(Vector2 frac, string rock, int seed)
        {
            var node = UIKit.Node("Perch", _map);
            node.anchorMin = node.anchorMax = frac;
            node.pivot = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(360f, 420f);
            node.anchoredPosition = Vector2.zero;

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
            string title = _entry != null ? Loc.Get(_entry.NameKey) : Loc.Get("ui.levels.title");
            UIKit.Titled("Title", banner.transform, title.ToUpperInvariant(), 40, new Color(.36f, .24f, .16f),
                         TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            Scenery.Pill(Content,
                         $"{PlayerProgress.TotalStars(_index)} / {PlayerProgress.MaxStars(_index)}",
                         36, new Vector2(196f, 78f), new Vector2(1f, 1f), new Vector2(-106f, -132f), null, "ic_star");

            if (_entry == null) return;

            var swipe = UIKit.Titled("Swipe", Content, Loc.Get("ui.levels.swipe"), 26,
                                     new Color(1f, .96f, .88f, .5f), TextAnchor.MiddleCenter,
                                     new Vector2(700f, 36f), new Vector2(.5f, 0f),
                                     new Vector2(0f, NavBar.Height + 52f), 3f, 0f);
            Tween.Tint(swipe, new Color(1f, .96f, .88f, 0f), .8f).Delay(4.2f);
        }

        /// <summary>Left and right arrows for stepping between chapters.</summary>
        void BuildChapterArrows()
        {
            var previous = LevelUnlock.ChapterBefore(_index, _entry.Id);
            var next = LevelUnlock.ChapterAfter(_index, _entry.Id);

            if (previous != null)
            {
                var target = previous.Id;
                UIKit.IconButton("PrevChapter", Content, "sq_dark", "ic_left", new Vector2(104f, 104f),
                                 new Vector2(0f, .5f), new Vector2(78f, 0f), () => GoToChapter(target));
            }

            if (next != null && LevelUnlock.IsChapterUnlocked(_index, next.Id))
            {
                var target = next.Id;
                UIKit.IconButton("NextChapter", Content, "sq_dark", "ic_right", new Vector2(104f, 104f),
                                 new Vector2(1f, .5f), new Vector2(-78f, 0f), () => GoToChapter(target));
            }
        }

        void GoToChapter(ChapterId id)
        {
            Audio.SfxVaried("whoosh", .4f);
            Flow.Go<LevelsScreen>(v => v.ChapterId = id);
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

            var target = LevelUnlock.NextToPlay(_index);
            if (!target.IsValid || !_layout.Has(target)) yield break;

            float want = NormalisedFor(_layout.PositionOf(target).y);
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
            float range = _layout.TotalHeight - v;
            if (range <= 1f) return 0f;
            return Mathf.Clamp01((fraction * _layout.TotalHeight - v * .5f) / range);
        }

        void Open(LevelId id, bool unlocked)
        {
            if (!unlocked)
            {
                Audio.Sfx("nope", .55f);
                if (_nodes.TryGetValue(id, out var node)) Tween.Shake(node, 12f, .35f);
                Scenery.Toast(Content, Loc.Get("ui.levels.locked_hint"), Pal.Parchment, 1.8f);
                return;
            }
            Audio.Sfx("unlock", .55f);
            Flow.Go<PlayScreen>(v => v.LevelId = id);
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
