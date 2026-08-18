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
        /// Drawn size of a map node, and where a glyph has to sit inside one to land on
        /// the disc's white face rather than on its rim. Derived from the art via
        /// <see cref="UIKit.NodeFaceLift"/> so the three glyphs that ride a node — the
        /// glade number, the onward chevron and the sealed teaser's question mark — cannot
        /// drift apart the way three hand-tuned offsets did.
        /// </summary>
        const float NodeSize = 196f;
        const float NodeFaceY = NodeSize * UIKit.NodeFaceLift;

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
            //
            // No nav bar here, and that is deliberate. The map is the one screen you are
            // *inside* rather than one of the places you can be: every tab on the bar led
            // away from a chapter the player had just chosen to open, and the bar cost the
            // bottom 206px of the only screen whose whole job is showing a vertical chain.
            // The way out is the back key in the top-left corner, where every other
            // second-level screen in the game already keeps it.
            BuildHeader();

            if (_entry == null) return;

            ChapterId = _entry.Id;
            StartCoroutine(BuildChapter());

            // The day's population lands once a session, from the cloud, on its own schedule.
            // Usually that is long before anybody opens a map — but when it is not, this is
            // the screen the promotion is visible on, so it repaints rather than waiting for
            // the player to leave the chapter and come back.
            PlayerProgress.RanksChanged += RepaintRanks;
        }

        void OnDestroy()
        {
            PlayerProgress.RanksChanged -= RepaintRanks;
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

            // Behind the chrome, and this is not cosmetic. The header is built in Build()
            // because it is index knowledge and must not wait on a file, while the scroller
            // cannot exist until the chapter body has resolved a layout — so the viewport is
            // always the *younger* sibling, and uGUI draws younger siblings in front. Left
            // alone it covered the back key, the chapter banner and the star count with an
            // opaque map, and its own invisible drag-catcher swallowed every tap meant for
            // them; the header only became visible at all when an elastic overscroll dragged
            // the map off the top of it, which is a strange thing to discover about your own
            // navigation. Anything added to the header from now on lands in front of the map
            // for the same reason.
            _viewport.SetAsFirstSibling();

            // Full screen: nothing reserves the bottom any more, so the offsets Node()
            // already set are what we want and the old NavBar.Height inset is gone.
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
                                    new Vector2(ChapterMap.Width, ChapterMap.StripHeight), new Vector2(.5f, 0f),
                                    new Vector2(0f, _layout.StripBottom(i) + ChapterMap.StripHeight * .5f));
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
            var btn = UIKit.Button("Btn", node, Art.S("Map/" + skin), new Vector2(NodeSize, NodeSize),
                                   new Vector2(.5f, .5f), new Vector2(0f, 2f), () => Open(id, unlocked));
            btn.GetComponent<Image>().preserveAspect = true;

            // A glade has its own voice: Open() plays `unlock` when it lets you in, and
            // deliberately nothing when it does not — a refusal here is carried by the
            // node's shake and its toast, because the game has no rejection sound. Either
            // way the generic click would be a second sound saying less than the first.
            btn.ClickSfx = null;

            if (unlocked)
                UIKit.Titled("Num", btn.transform, displayNumber.ToString(), 62, new Color(.30f, .21f, .13f),
                             TextAnchor.MiddleCenter, new Vector2(190f, 110f), new Vector2(.5f, .5f),
                             new Vector2(0f, NodeFaceY), 0f, 2f);

            Plate(node, unlocked ? Loc.Get(level.NameKey) : Loc.Get("ui.levels.locked"),
                  unlocked ? Pal.Cream : new Color(1f, 1f, 1f, .62f), -196f);

            float delay = PopDelay(indexInChapter, _layout.Levels.Count);

            if (stars > 0) RankMark(node, level.Id, delay);

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
                                 new Vector2(NodeSize, NodeSize), new Vector2(.5f, .5f), new Vector2(0f, 2f));
            disc.preserveAspect = true;

            if (reachable)
            {
                var target = next.Id;
                var btn = UIKit.Button("Btn", node, Art.S("Map/node_open"), new Vector2(NodeSize, NodeSize),
                                       new Vector2(.5f, .5f), new Vector2(0f, 2f), () => GoToChapter(target));
                btn.GetComponent<Image>().preserveAspect = true;
                UIKit.Titled("Arrow", btn.transform, "»", 64, new Color(.30f, .21f, .13f),
                             TextAnchor.MiddleCenter, new Vector2(190f, 110f), new Vector2(.5f, .5f),
                             new Vector2(0f, NodeFaceY), 0f, 2f);
                Plate(node, Loc.Get(next.NameKey), Pal.Cream, -196f);
            }
            else
            {
                // hangs off the perch rather than the seal, so it carries the seal's own
                // offset as well as the face lift
                UIKit.Titled("Q", node, "?", 64, new Color(.36f, .38f, .44f), TextAnchor.MiddleCenter,
                             new Vector2(190f, 110f), new Vector2(.5f, .5f), new Vector2(0f, 2f + NodeFaceY),
                             0f, 2f);
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

        /// <summary>
        /// Where the standing mark sits, and how big it is.
        ///
        /// <para>
        /// Directly above the disc rather than pinned to a corner of it, and that is a
        /// collision decision rather than a taste one. <c>mapX</c>/<c>mapY</c> are authored,
        /// and <see cref="ChapterMap"/> proves nodes do not overlap using the perch's own
        /// footprint — so a mark that grew sideways could be validated as clear and still
        /// touch its neighbour on somebody's phone. This stays inside the 360×420 perch and
        /// reaches less far up than the pointer already does, so it adds nothing the build
        /// gate is not already checking.
        /// </para>
        /// </summary>
        /// <summary>
        /// Where the mark's <em>bottom edge</em> sits above the node's centre, and the pill
        /// size in each of its two shapes.
        ///
        /// <para>
        /// Pinned by the bottom rather than the middle so the gap above the disc is the same
        /// whether the pill carries one line or two — anchoring the centre made the shorter
        /// pill float further away, which read as two different components rather than one in
        /// two states. It grows upward instead, and the tall shape still finishes inside the
        /// 420px perch that <see cref="ChapterMap"/> already proves does not collide.
        /// </para>
        /// </summary>
        /// <remarks>
        /// The sizes are safe against the shipped maps, where consecutive glades sit 756px
        /// apart vertically and ~370px across — but note that
        /// <see cref="ChapterMap.MinimumNodeSeparation"/> only <em>guarantees</em> 220px,
        /// because it is derived from the 196px disc and knows nothing about what rides above
        /// it. A future chapter authored near that floor would overlap these. Raising the
        /// guarantee is a content-authoring decision rather than a layout one, so it is not
        /// made here.
        /// </remarks>
        /// <remarks>
        /// The two-line width is measured, not chosen. "You are in the top 25%" generates
        /// 358px in <c>GameFont</c> at 32pt, so the inner line box has to clear that or
        /// <see cref="UIKit.Shrinkable"/> folds it — which is what wrapping means here, since
        /// best-fit only shrinks text that fails <em>vertically</em>. At 392 the box was 356
        /// and the standing wrapped by two pixels. 408 leaves 14px, which also covers the
        /// widest record line ("108 turns · 12:04", 245px at 28pt) with room to spare.
        /// </remarks>
        const float RankMarkBottom = 106f;
        static readonly Vector2 RankMarkTwoLine = new Vector2(408f, 196f);
        static readonly Vector2 RankMarkOneLine = new Vector2(344f, 74f);

        /// <summary>Medal disc size, and how far above the pill centre it sits.</summary>
        const float MedalSize = 78f, MedalY = 54f;

        /// <summary>
        /// Inset from the pill's edge to a line's own box, and how far each line sits from the
        /// pill centre in the two-line shape.
        ///
        /// <para>
        /// Both lines are anchored at the pill's centre with an explicit offset, never to its
        /// top or bottom edge. <see cref="UIKit.Box"/> always pivots at the centre, so an
        /// edge-anchored box reaches half its own height <em>past</em> that edge — which is
        /// exactly how both of these ended up hanging out of the pill.
        /// </para>
        /// </summary>
        const float RankMarkPad = 18f, RankMarkLineHeight = 44f;

        /// <summary>
        /// The record line, written out rather than assembled, so the build's loc gate can
        /// see every key. Four of them because "1 turns" is wrong in English and worse in
        /// languages with real plural rules, and because a glade cleared before the clock
        /// existed has a move count and no time — a dash where the time goes would read as
        /// a broken record rather than an untimed one.
        /// </summary>
        static string RecordKey(int moves, int millis)
            => millis > 0
                ? (moves == 1 ? "ui.rank.record_one" : "ui.rank.record")
                : (moves == 1 ? "ui.rank.untimed_one" : "ui.rank.untimed");

        /// <summary>
        /// The permanent standing on a cleared glade: "TOP 10%".
        ///
        /// <para>
        /// Drawn from the save, never from <see cref="Social.GroveStats"/>, which is what
        /// makes it instant and available offline — see <see cref="LevelRecord.BestRank"/>
        /// for why a stored standing is the honest one here. An unranked glade draws nothing
        /// at all rather than an empty frame: most of a catalog is unranked on any given day,
        /// and a row of blanks would turn the absence into the message.
        /// </para>
        /// <para>
        /// Colour carries the tier, because three identical pills reading different numbers
        /// are three things to compare rather than one thing to notice. Gold is reserved for
        /// the top tier for the reason the companion reveal reserves it — it is what this UI
        /// already means by "best", so spending it lower devalues every other use of it.
        /// </para>
        /// </summary>
        static void RankMark(Transform parent, LevelId id, float delay)
        {
            int moves = PlayerProgress.BestMoves(id);
            if (moves <= 0) return;

            int millis = PlayerProgress.BestMillis(id);
            var band = Social.RankTier.Of(PlayerProgress.BestRank(id));

            bool ranked = band != Social.RankBand.None;
            bool top = band == Social.RankBand.Top10;

            Color ink = band == Social.RankBand.Top10 ? Pal.Gold
                      : band == Social.RankBand.Top25 ? Pal.Parchment
                      : new Color(1f, .95f, .86f, .82f);

            var size = ranked ? RankMarkTwoLine : RankMarkOneLine;

            var host = UIKit.Node("Rank", parent);
            host.anchorMin = host.anchorMax = host.pivot = new Vector2(.5f, .5f);
            host.sizeDelta = size;
            host.anchoredPosition = new Vector2(0f, RankMarkBottom + size.y * .5f);

            float lineWidth = size.x - RankMarkPad * 2f;

            // Radiance behind the whole plate for the top tier. A soft gradient rather than
            // anything with an edge, so it reads as light around an award and can never be
            // mistaken for a mislaid rectangle — which is the risk with any layer that leaves
            // the container it belongs to.
            if (top)
            {
                UIKit.Img("Rays", host, Art.Rays(256, 12), new Color(1f, .80f, .32f, .16f),
                          Vector2.one * (size.x * .96f), new Vector2(.5f, .5f), Vector2.zero);
                UIKit.Img("Seat", host, Art.Glow(96, 2.2f), new Color(1f, .76f, .24f, .26f),
                          size + new Vector2(96f, 76f), new Vector2(.5f, .5f), Vector2.zero);
            }

            var bg = UIKit.Img("Pill", host, Art.Round(22), new Color(.04f, .09f, .13f, .82f),
                               size, new Vector2(.5f, .5f), Vector2.zero);

            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(22, 3f),
                                 new Color(ink.r, ink.g, ink.b, ranked ? (top ? .68f : .34f) : .18f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            // The standing, when there is one — a struck medal over two lines of text, which
            // is the shape a certificate has and the reason this reads as an award rather than
            // as a caption. Optional, because for most of a catalog on most days there is no
            // population to compare against.
            if (ranked)
            {
                Medal(bg.transform, ink, top);

                var line = UIKit.Titled("Band", bg.transform, Loc.Get(Social.RankTier.KeyOf(band)),
                                        32, ink, TextAnchor.MiddleCenter,
                                        new Vector2(lineWidth, RankMarkLineHeight),
                                        new Vector2(.5f, .5f), new Vector2(0f, -22f), 3f, 3f);
                UIKit.Shrinkable(line, 20);
            }

            // The record always. This is the half that says something on a brand new install
            // with no backend at all, which is why the mark is no longer conditional on being
            // ranked — an empty node above a cleared glade was the whole problem. Unranked it
            // takes a tick rather than a medal: it is a result, and dressing a median run as a
            // trophy is how a trophy stops meaning anything.
            if (!ranked)
            {
                var tick = UIKit.Img("Cleared", bg.transform, Art.S("Ui/ic_check"),
                                     new Color(1f, .96f, .88f, .62f), Vector2.one * 34f,
                                     new Vector2(0f, .5f), new Vector2(38f, 0f));
                tick.preserveAspect = true;
            }

            var record = UIKit.Titled("Record", bg.transform,
                                      Loc.Format(RecordKey(moves, millis), moves, RunClock.Format(millis)),
                                      ranked ? 28 : 29,
                                      new Color(1f, .96f, .88f, ranked ? .80f : .92f),
                                      TextAnchor.MiddleCenter,
                                      new Vector2(ranked ? lineWidth : lineWidth - 40f, RankMarkLineHeight),
                                      new Vector2(.5f, .5f),
                                      new Vector2(ranked ? 0f : 20f, ranked ? -70f : 0f), 3f, 2f);

            // Both lines shrink rather than overflow. Label defaults to
            // HorizontalWrapMode.Overflow, which has no clipping at all — so an unshrinkable
            // line does not get truncated, it simply keeps drawing outside the pill. That is
            // the other half of why these were hanging out of it, and it is the half a
            // translation would have reintroduced even with the geometry fixed.
            UIKit.Shrinkable(record, 19);

            // Its own entrance, a beat after the perch it rides. The perch scales from zero
            // and takes the mark with it on a first build, but a repaint lands on a perch
            // already at rest — this is the one path that has to animate either way.
            host.localScale = Vector3.zero;
            Tween.Pop(host, 0f, .55f, .18f + delay + .16f);
        }

        /// <summary>
        /// The struck medal at the top of a ranked mark: a halo, a filled disc, a cream rim
        /// and a trophy.
        ///
        /// <para>
        /// <b>A trophy and not a star</b>, which matters more than it looks. The node's own
        /// disc is already <c>node_s1</c>/<c>s2</c>/<c>s3</c> — its art *is* the star rating —
        /// so a star sitting 100px above it would be the same symbol counting a different
        /// thing, and a player would reasonably read a gold star on the badge as a fourth
        /// star on the glade. A trophy is rank vocabulary and collides with nothing. It is
        /// also why the tiers are one glyph in three colours rather than three glyphs: a
        /// medal ladder is something everybody already knows how to read, and swapping the
        /// symbol per tier would mean inventing an ordering nobody has been taught.
        /// </para>
        /// <para>
        /// The rim is cream on every tier. Ringing a bronze medal in bronze makes the rim
        /// disappear, which is the same mistake the feature beacon made travelling gold out
        /// of gold — the contrast has to come from somewhere that is not the tier colour.
        /// </para>
        /// </summary>
        static void Medal(Transform parent, Color ink, bool top)
        {
            var seat = new Vector2(0f, MedalY);

            UIKit.Img("Halo", parent, Art.Glow(96, 2.2f), new Color(ink.r, ink.g, ink.b, top ? .42f : .24f),
                      Vector2.one * (MedalSize * 1.7f), new Vector2(.5f, .5f), seat);

            var disc = UIKit.Img("Disc", parent, Art.Disc(128), ink,
                                 Vector2.one * MedalSize, new Vector2(.5f, .5f), seat);

            UIKit.Img("Rim", parent, Art.Ring(128, 9f), new Color(1f, .98f, .90f, top ? .92f : .70f),
                      Vector2.one * MedalSize, new Vector2(.5f, .5f), seat);

            var glyph = UIKit.Img("Trophy", parent, Art.S("Ui/ic_trophy"),
                                  new Color(.20f, .13f, .07f, .92f),
                                  Vector2.one * (MedalSize * .52f), new Vector2(.5f, .5f), seat);
            glyph.preserveAspect = true;

            // Only the best tier breathes. Motion is the loudest thing on a map full of
            // bobbing rocks, so spending it on every ranked glade would spend it on most of
            // them and single out none.
            if (top) Tween.Breathe(disc.transform, .055f, 2.4f);
        }

        /// <summary>
        /// Redraws every standing mark after a freshly published population promoted some.
        ///
        /// <para>
        /// The table is fetched once a session and normally lands before the player ever
        /// reaches a map, so this is the uncommon path — but it is the one where a player who
        /// has just been promoted is looking at the very screen that says so, and a screen
        /// that draws asynchronous data has to repaint when it arrives.
        /// </para>
        /// </summary>
        void RepaintRanks()
        {
            foreach (var pair in _nodes)
            {
                var perch = pair.Value;
                if (!perch) continue;

                var existing = perch.Find("Rank");
                if (existing) Destroy(existing.gameObject);

                if (PlayerProgress.Stars(pair.Key) > 0)
                    RankMark(perch, pair.Key, 0f);
            }
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

            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(118f, 118f),
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
                                     new Vector2(0f, 118f), 3f, 0f);
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
                UIKit.IconButton("PrevChapter", Content, Skins.Nav, "ic_left", new Vector2(104f, 104f),
                                 new Vector2(0f, .5f), new Vector2(78f, 0f), () => GoToChapter(target));
            }

            if (next != null && LevelUnlock.IsChapterUnlocked(_index, next.Id))
            {
                var target = next.Id;
                UIKit.IconButton("NextChapter", Content, Skins.Nav, "ic_right", new Vector2(104f, 104f),
                                 new Vector2(1f, .5f), new Vector2(-78f, 0f), () => GoToChapter(target));
            }
        }

        void GoToChapter(ChapterId id)
        {
            // No sound here either: the arrow that was tapped has already made one, and
            // this used to add a whoosh on top of the one Flow.Go played, so stepping
            // between chapters was three sounds at once.
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
                if (_nodes.TryGetValue(id, out var node)) Tween.Shake(node, 12f, .35f);
                Scenery.Toast(Content, Loc.Get("ui.levels.locked_hint"), Pal.Parchment, 1.8f);
                return;
            }
            // The gate. Checked on the way in rather than on the way out of a defeat,
            // so a player is never dropped into a glade they cannot afford to lose —
            // being told at the door is a wait, being told at the blast is a wasted run.
            if (!Profile.CanPlay)
            {
                if (_nodes.TryGetValue(id, out var barred)) Tween.Shake(barred, 10f, .35f);
                Flow.Modal<OutOfHeartsOverlay>();
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
