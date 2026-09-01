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
    /// no virtualisation, no pooling, no cleverness required. Arrows either side of
    /// the name plaque step between chapters — forward even into a locked one, which
    /// is how the ladder stays visible — and the screen opens on the chapter of
    /// this mode the player was last looking at, falling back to wherever they are up to.
    /// </summary>
    public sealed class LevelsScreen : View
    {
        /// <summary>Which chapter to show. Defaults to wherever the player is up to.</summary>
        public ChapterId ChapterId;

        /// <summary>
        /// Which way of playing this map is showing.
        ///
        /// <para>
        /// A field rather than a read of the remembered choice, so the one place a mode is
        /// decided is <see cref="Build"/> - and so a caller that already knows (the switcher,
        /// or a chapter arrow carrying its own chapter's mode) can say so without writing the
        /// preference first. An unset one falls back to what the player last looked at.
        /// </para>
        /// <para>
        /// A chapter's own mode always wins over this: a chapter belongs to exactly one mode,
        /// so opening one is choosing a mode whether or not anybody said so.
        /// </para>
        /// </summary>
        public GameMode Mode;

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

        /// <summary>
        /// How much larger than <see cref="ChapterMap.Width"/> the map is actually drawn.
        /// One on every phone; see where it is set, in <c>BuildScroller</c>.
        /// </summary>
        float _mapScale = 1f;

        /// <summary>
        /// The map's height as drawn, which is what the scroller measures against.
        ///
        /// <see cref="MapLayout.TotalHeight"/> is the height it was <em>authored</em> at, and
        /// scrolling to a fraction of the wrong one puts every glade off by however much the
        /// two differ.
        /// </summary>
        float MapHeight => _layout != null ? _layout.TotalHeight * _mapScale : 0f;

        /// <summary>
        /// The mode pill under the header, or null when the catalog holds one mode and
        /// <see cref="ModeSwitch"/> therefore drew nothing. It is kept for one reason: it is
        /// what the first-run lesson rings. See <see cref="Teach"/>.
        /// </summary>
        RectTransform _modes;

        /// <summary>Whether this visit has already decided about the mode lesson.</summary>
        bool _taught;

        /// <summary>Whether the incoming transition has finished. See <see cref="Teach"/>.</summary>
        bool _presented;

        readonly Dictionary<LevelId, RectTransform> _nodes = new Dictionary<LevelId, RectTransform>();

        /// <summary>
        /// The map is the one screen that cannot finish building inside <c>Build</c>: it needs
        /// its chapter's body read and that chapter's art resident, and on a chapter the player
        /// has not opened before neither is in hand. So it tells the transition to stay shut —
        /// see <see cref="View.Ready"/>.
        ///
        /// <para>
        /// Set on <b>every</b> way out of <see cref="BuildChapter"/>, including the two that
        /// give up, because a screen that never says it is ready holds a slate disc over the
        /// game until <c>Flow</c>'s timeout rescues it. A chapter that failed to load should
        /// show whatever it managed, immediately.
        /// </para>
        /// </summary>
        bool _built;

        public override bool Ready => _built;


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

            // The chapter decides the mode when there is one, because a chapter belongs to
            // exactly one; otherwise the field, and failing that whatever the player was last
            // looking at. ModeChoice.Read also refuses a mode this catalog has no chapters for,
            // which is what stops a rolled-back client or an undownloaded drop opening the map
            // onto nothing at all.
            if (ChapterId.IsValid)
            {
                _entry = _index.FindChapter(ChapterId);
            }
            else
            {
                var mode = Mode.IsPlayable ? Mode : ModeChoice.Read(_index);

                // The chapter the player was last looking at in this mode, and only then
                // wherever they are up to in it. Every way back to the map except the chapter
                // arrows arrives with no chapter named - the back key, a forfeit, the victory
                // panel, the home screen - so on an account that has unlocked everything, "up
                // to" is the newest chapter and replaying an early one meant arrowing back to
                // it after every level. ChapterChoice.Read answers null the moment the
                // remembered chapter is not a chapter of this mode in this catalog, which is
                // what keeps a rollback or an undownloaded drop from opening onto nothing.
                _entry = ChapterChoice.Read(_index, mode) ?? LevelUnlock.CurrentChapter(_index, mode);
            }

            if (_entry != null) Mode = _entry.Mode;
            else if (!Mode.IsPlayable) Mode = ModeChoice.Read(_index);

            // Remembered on arrival rather than on the tap, so the map a player is returned to
            // after a run is the one they left - the tap is only one of the ways to get here.
            ModeChoice.Write(Mode);
            ChapterChoice.Write(_entry);

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

            if (_entry == null) { _built = true; return; }

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

            if (bodyTask.IsFaulted) { Debug.LogException(bodyTask.Exception); _built = true; yield break; }

            _body = bodyTask.Result;
            if (_body == null || !this) { _built = true; yield break; }

            _layout = MapLayout.Build(_body, _entry.LevelIds);

            // Swapping chapters swaps their art; this is where the previous chapter's
            // backdrops and strips are actually released.
            //
            // *Awaited*, and that is the whole of it. This used to be started and dropped
            // on the floor, so the map was drawn on the same frame the load began — and
            // the map strips live in this scope, so Art.S returned null for every one of
            // them and BuildMapArt skipped them all. The chapter opened onto nothing but
            // the shade layer. It was invisible for as long as there was one chapter: the
            // splash preloads the opening one, so the only map anybody could reach was
            // already resident and the race never ran. Stepping into a second chapter is
            // the first thing in the game that asks for art it does not already hold.
            //
            // Awaiting rather than repainting on arrival because *everything* below needs
            // it — strips, scenery, and the backdrop each glade will want. A repaint would
            // be four subscriptions to cover one wait that is normally already over.
            var art = AssetLibrary.EnsureChapterAsync(_body);
            while (!art.IsCompleted) yield return null;
            if (!this) { _built = true; yield break; }

            // A chapter whose art failed still draws: the nodes, the trails and the names
            // are all catalog knowledge, and a map with no backdrop is worth more to a
            // player than a screen with nothing on it at all.
            if (art.IsFaulted) Debug.LogException(art.Exception);

            BuildScroller();
            BuildMapArt();
            BuildTrails();
            BuildNodes();
            BuildChapterEnd();

            // Ready the moment the map exists, not once it has finished arriving. The nodes
            // pop in over about a second and that entrance is the point — it should play
            // while the iris opens, the way it always did on a chapter whose art was already
            // resident. Waiting for it would replace one abrupt cut with a long stare at a
            // slate disc.
            _built = true;

            // The other half of the lesson's timing. A chapter slow enough to hit Flow's
            // ready timeout is presented before any of this ran, so OnPresented found no map
            // to teach over and correctly declined; this is where it becomes possible again.
            Teach();

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

            // How much larger than its authored width the map is being drawn.
            //
            // A chapter's map is a *painting* — a column of strips a fixed number of units
            // across, with every glade, trail and prop placed as a fraction of it — and until
            // there was a tablet the canvas was that width exactly, so the question never came
            // up. It does now: `Layout.CanvasFit` widens the canvas on anything squarer than a
            // phone, and a strip drawn at its authored 1080 in a 1620-unit canvas is a painting
            // with 270 units of nothing down each side and glades that no longer stand on it.
            // Stretching the strips instead is worse — that is the same painting 50% wider than
            // it was drawn.
            //
            // So the whole map is scaled uniformly to the width it is given, which costs
            // nothing anywhere else: every position on it is already a fraction. Exactly 1 on
            // every phone, so this is a no-op on everything that ships today. The glade discs
            // are deliberately *not* scaled with it — they are controls rather than scenery, and
            // a control that stays the size it was on a canvas that grew is a control that has
            // got smaller, which is the whole point of the widening.
            _mapScale = Mathf.Max(1f, Boot.CanvasWidth / ChapterMap.Width);

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
            for (int i = 0; i < _layout.Strips.Count; i++)
            {
                var sprite = Art.S("Map/" + _layout.Strips[i]);
                if (sprite == null) continue;

                var img = UIKit.Img("Strip" + i, _map, sprite, Color.white,
                                    new Vector2(ChapterMap.Width, ChapterMap.StripHeight) * _mapScale,
                                    new Vector2(.5f, 0f),
                                    new Vector2(0f, (_layout.StripBottom(i) + ChapterMap.StripHeight * .5f)
                                                    * _mapScale));
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
                                    Vector2.one * size * _mapScale, new Vector2(x, y), Vector2.zero);
                img.preserveAspect = true;
                if (bob > 0f)
                    Tween.Bob((RectTransform)img.transform, bob * _mapScale,
                              Random.Range(2.6f, 4.2f), Random.value * 6f);
            }
        }

        // ------------------------------------------------------------ the nodes
        void BuildTrails()
        {
            var levels = _layout.Levels;

            // What the last trail leads to, which is the one place the two unlock rules meet.
            // Inside the chapter a trail is lit when the island it leads to is open; the trail
            // out of the chapter has to ask the same question of what it leads to, and since
            // the boundary became a star gate that is no longer "was the last level cleared".
            // A lit trail running into a padlocked signpost is the map contradicting itself.
            var onward = LevelUnlock.ChapterAfter(_index, _entry.Id);

            for (int i = 0; i < levels.Count; i++)
            {
                bool last = i == levels.Count - 1;
                var from = _layout.PositionOf(levels[i].Id);
                var to = last ? _layout.TeaserPosition : _layout.PositionOf(levels[i + 1].Id);
                bool live = last
                    ? (onward != null
                        ? LevelUnlock.IsChapterUnlocked(_index, onward.Id)
                        : PlayerProgress.IsCleared(levels[i].Id))
                    : LevelUnlock.IsUnlocked(_index, levels[i + 1].Id);

                var trail = _map.gameObject.AddComponent<Trail>();
                trail.Setup(_map, from, to, 13,
                            live ? ModeLooks.Of(Mode).Accent : new Color(1f, .99f, .92f, .8f), live,
                            _mapScale);
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

            var node = MakePerch(_layout.PositionOf(level.Id), ModeLooks.Of(Mode).Perch,
                                 indexInChapter);
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
            // Silent. A node arriving is motion, not news - and a chapter is ten to twenty
            // of them, so any sound here is a rising run played every single time the map
            // opens, which is the screen a player passes through most. The nodes pop, the
            // tap that opened the map spoke, and entering a glade has its own sound.

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
            var node = MakePerch(_layout.TeaserPosition, ModeLooks.Of(Mode).Perch, 99);

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
                // The gate, as a number rather than as an instruction. "Clear this chapter to
                // go on" was true when the boundary was a chain and is now both wrong and
                // unactionable - a player can be holding nine cleared glades and still be
                // short. What they need is the count they are working towards, on the node
                // that is withholding the chapter, so that going back for a second star on an
                // earlier glade is visibly the thing to do.
                Plate(node, onward ? GateLine(LevelUnlock.GateFor(_index, next.Id))
                                   : Loc.Get("ui.levels.more_soon"),
                      new Color(1f, 1f, 1f, .62f), -196f);
            }

            int count = _layout.Levels.Count;

            node.localScale = Vector3.zero;
            Tween.Pop(node, 0f, .6f, .18f + PopDelay(count, count));
        }

        /// <summary>
        /// What a shut gate says, in stars.
        ///
        /// <para>
        /// One string used in two places — the signpost at the end of a chain and the refusal
        /// a padlocked glade gives — because they are the same sentence and a player who read
        /// one and then tapped the other would otherwise be told two different things about
        /// one rule. Falls back to the plain "locked" line for a gate with no chapter behind
        /// it, which is a catalog nobody can reach and so a validator's problem rather than a
        /// sentence worth composing.
        /// </para>
        /// </summary>
        static string GateLine(ChapterGate gate)
            => gate.Exists
                ? Loc.Format("ui.levels.chapter_gate", gate.Held, gate.Required)
                : Loc.Get("ui.levels.chapter_locked");

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

            // Tinted rather than re-cut, which is the whole of what a second mode costs the map
            // art: one multiply over sprites that are already loaded, moving every island of a
            // chapter together. The rock *set* differs too, so the two maps are still told apart
            // by somebody who cannot see the colour.
            var img = UIKit.Img("Rock", node, Art.S("Map/" + rock), ModeLooks.Of(Mode).Wash,
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
        /// widest record line with room to spare — that used to be "108 turns · 12:04" at
        /// 245px, and a record has carried no time since invariant 22.
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
        /// see every key. Two of them because "1 turns" is wrong in English and worse in
        /// languages with real plural rules.
        /// </summary>
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
                                      Loc.Format(RunWording.RecordKey(id, moves), moves),
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

                // Hidden before it is destroyed: Destroy lands at the end of the frame, so
                // the old mark would otherwise be drawn on top of the one replacing it for
                // the rest of this one. The house rule everywhere a region is rebuilt.
                var existing = perch.Find("Rank");
                if (existing)
                {
                    existing.gameObject.SetActive(false);
                    Destroy(existing.gameObject);
                }

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

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * CornerSize,
                             new Vector2(0f, 1f), new Vector2(CornerX, CornerY), () => Flow.Go<HomeScreen>());

            // What a glade pays, and under what rule — the one thing this screen is full of
            // and cannot draw. A node shows its stars and says nothing about what the stars
            // were worth, or what a second run at a glade already three-starred is worth,
            // which is the question the map invites and the victory panel answers only once.
            // Top-right corner, the same place the streak and event pages keep theirs.
            var chapter = _entry != null ? _entry.Id : ChapterId;
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * CornerSize,
                             new Vector2(1f, 1f), new Vector2(-CornerX, CornerY),
                             () => { if (!Flow.HasModal) Flow.Modal<GladeRewardsOverlay>(v => v.For(chapter)); });

            _banner = UIKit.Img("Banner", Safe, Art.S("Ui/banner"), Color.white,
                                new Vector2(BannerWidth, BannerHeight), new Vector2(.5f, 1f),
                                new Vector2(0f, BannerY));
            string title = _entry != null ? Loc.Get(_entry.NameKey) : Loc.Get("ui.levels.title");
            var name = UIKit.Titled("Title", _banner.transform, title.ToUpperInvariant(), 40,
                                    BannerInk, TextAnchor.MiddleCenter,
                                    new Vector2(NameWidth, 96f), new Vector2(.5f, .5f),
                                    Vector2.zero, outline: 0f, shadow: 2f);

            // One unwrapped line, narrowed until it fits between the chevrons rather than
            // trusted to be short. A chapter name is authored per drop and translated after
            // that, so it is the string on this screen most likely to arrive longer than
            // the space it was measured against.
            while (name.fontSize > 26 && name.preferredWidth > NameWidth) name.fontSize--;

            _name = name;

            _banner.transform.localScale = Vector3.zero;
            Tween.Pop(_banner.transform, 0f, .6f, .1f);

            if (_entry == null) return;

            // Counting *this chapter*, in the top-right corner under the "i". It used to
            // total the whole catalog, so the one number on a screen showing ten glades was
            // out of 90 and moved by a thirtieth when a glade was three-starred: a progress
            // readout for a chapter that could not show progress through it. The catalog
            // total still exists and still has a home, on the profile, where a lifetime
            // figure belongs.
            //
            // The corner rather than the column: a count and the "i" beside it are both
            // *readings* of the chapter and neither is a way through the map, so they belong
            // in the same stack — which leaves the centre line a control column, plaque then
            // switcher, with nothing standing between the name and the thing that changes
            // what the name is naming.
            Scenery.Pill(Safe,
                         $"{PlayerProgress.TotalStars(_entry)} / {PlayerProgress.MaxStars(_entry)}",
                         36, new Vector2(StarsWidth, StarsHeight), new Vector2(1f, 1f),
                         new Vector2(StarsX, StarsY), null, "ic_star");

            BuildChapterArrows();

            // In the safe layer with the rest of the chrome, and drawn last so it sits over the
            // map. It builds nothing at all while the catalog holds one mode, which is what
            // makes calling it unconditionally safe.
            //
            // Second in the header column, and told where that is rather than finding out: the
            // plaque and the switcher are one column measured downwards from BannerY, so a
            // switcher holding its own offset would be a second copy of the same arithmetic and
            // would stop agreeing with the plaque the first time it was resized.
            _modes = ModeSwitch.Build(Safe, _index, Mode, SwitchTo, ModesY);

            var swipe = UIKit.Titled("Swipe", Safe, Loc.Get("ui.levels.swipe"), 26,
                                     new Color(1f, .96f, .88f, .5f), TextAnchor.MiddleCenter,
                                     new Vector2(700f, 36f), new Vector2(.5f, 0f),
                                     new Vector2(0f, 118f), 3f, 0f);
            Tween.Tint(swipe, new Color(1f, .96f, .88f, 0f), .8f).Delay(4.2f);
        }

        /// <summary>
        /// Left and right arrows for stepping between chapters, one either side of the
        /// name plaque.
        /// </summary>
        /// <remarks>
        /// <para>
        /// They used to sit at the vertical middle of the two screen edges, where they were
        /// two floating controls with nothing to say what they stepped through — a map is
        /// dragged vertically, so an arrow halfway up its side reads as being about the map
        /// rather than about the chapter. On the plaque they are what the name is
        /// <em>for</em>: <c>&lt; THE SHALLOWS &gt;</c> needs no label and no lesson.
        /// </para>
        /// <para>
        /// <b>Inside the plaque, not beside it.</b> Two square nav buttons flanking it were
        /// tried first and read as chrome — a third and fourth control on a header row that
        /// already has a back key and a star count, competing with the plaque instead of
        /// belonging to it. A chevron cut in the same ink as the name, inside the same piece
        /// of wood, is one object that says "there is more of this either way". It also
        /// costs the row nothing: the plaque is the width it always was, so neither corner
        /// had to give up space and there is no arrangement of them that can collide.
        /// </para>
        /// <para>
        /// <b>Forward is offered whether or not the chapter is unlocked.</b> Looking ahead is
        /// not the same as playing ahead, and it costs nothing: every glade in a locked
        /// chapter draws padlocked and <see cref="Open"/> already refuses one, so the worst a
        /// browsing player can do is see what they are working towards. Hiding it made the
        /// last chapter of the catalog look like the last chapter of the game, which is the
        /// opposite of what a ladder is for. The lock badge is what keeps that honest — the
        /// arrow works, so it is not drawn as a dead control, but it says where it goes.
        /// </para>
        /// <para>
        /// Built from <see cref="BuildHeader"/> rather than after the body loads, because
        /// which chapters exist either side of this one is index knowledge — the same reason
        /// the name and the star count do not wait on a file.
        /// </para>
        /// </remarks>
        void BuildChapterArrows()
        {
            var previous = LevelUnlock.ChapterBefore(_index, _entry.Id);
            var next = LevelUnlock.ChapterAfter(_index, _entry.Id);

            if (previous != null) Chevron("PrevChapter", "<", -1f, previous, true);

            if (next != null)
                Chevron("NextChapter", ">", 1f, next, LevelUnlock.IsChapterUnlocked(_index, next.Id));
        }

        /// <summary>
        /// The plaque carrying the chapter name: its size, where it sits under the fade, the
        /// ink everything carved into it is written in, and how much of it the name may use.
        /// </summary>
        const float BannerWidth = 476f, BannerHeight = 138f, BannerY = -142f;
        const float ChevronX = 180f, NameWidth = 246f;

        /// <summary>
        /// The two corner keys — back on the left, "i" on the right — as one set of numbers,
        /// because the star count is now stacked under the second of them and a typed copy of
        /// where that corner is would stop agreeing with it the first time either key moved.
        /// </summary>
        const float CornerSize = 118f, CornerX = 96f, CornerY = -132f;

        /// <summary>
        /// Where the chapter's star count sits: under the "i", right-aligned with it, measured
        /// from the top-<em>right</em> corner rather than from the plaque's centre line.
        ///
        /// <para>
        /// Both numbers are derived from the corner key above it for the reason
        /// <see cref="ModesY"/> is derived from <see cref="BannerY"/> — the two are one stack,
        /// and half the key's height plus half the pill's is what turns a gap between two
        /// controls of different sizes into a gap between their <em>faces</em>.
        /// </para>
        /// <para>
        /// Right edges are what line up, not centres: the pill is wider than the key, so
        /// sharing a centre would push it past the safe edge on the narrowest canvas this
        /// game is drawn on, and hanging it inwards is what makes the pair read as a column
        /// against the right margin rather than as two things that happen to be near each
        /// other. The pill pivots centre (<c>UIKit.Box</c> always does), so its x is its own
        /// half-width plus the key's clearance inside that margin.
        /// </para>
        /// </summary>
        const float StarsGap = 22f;
        const float StarsX = -(CornerX - CornerSize * .5f + StarsWidth * .5f);
        const float StarsY = CornerY - CornerSize * .5f - StarsGap - StarsHeight * .5f;

        /// <summary>The star count's own size, named because its placement is derived from it.</summary>
        const float StarsWidth = 196f, StarsHeight = 78f;

        /// <summary>
        /// Where the mode switcher sits: directly under the plaque, on the same centre line.
        ///
        /// <para>
        /// Derived from <see cref="BannerY"/> because the plaque is centred on it, so its
        /// underside moves whenever its height does and a typed number here would have to be
        /// re-found every time the banner was resized. The switcher keeps the place directly
        /// beneath the name because it is the control that changes <em>which</em> catalog that
        /// name is drawn from; the star count is a reading rather than a control, so it lives
        /// with the other reading, in the corner under the "i".
        /// </para>
        /// <para>
        /// Half the plaque's height plus half the switcher's is what turns a gap between two
        /// controls of different sizes into a gap between their <em>faces</em> — the trap
        /// <c>UIKit.Corner</c> records, in a stack rather than in a corner. The switcher's height
        /// is read from <see cref="ModeSwitch.PillHeight"/> rather than typed here, so resizing
        /// the control cannot leave the map placing it against the size it used to be.
        /// </para>
        /// </summary>
        const float ModesGap = 20f;
        const float ModesY = BannerY - BannerHeight * .5f - ModesGap - ModeSwitch.PillHeight * .5f;

        /// <summary>
        /// How far the header column reaches down the screen, measured from the top of the
        /// safe area. The switcher is the last thing in it, so this is its lower edge.
        /// </summary>
        /// <remarks>
        /// Public because the map's own geometry has to clear it and cannot see it: the
        /// end-of-chapter marker is placed by <see cref="ChapterMap.TeaserHeadroom"/>, which
        /// lives in Domain and may not read Presentation, so the two are held together by a
        /// test that adds this to <see cref="ChapterMap.TeaserTopInset"/> and
        /// <see cref="ChapterMap.TeaserReach"/> rather than by a comment hoping somebody
        /// re-measures. That hope is exactly what failed: the headroom was sized against the
        /// banner, the switcher was added underneath it, and the marker sat behind the new
        /// control in every chapter of every mode with no file naming a wrong coordinate.
        /// </remarks>
        public const float HeaderUnderside = -ModesY + ModeSwitch.PillHeight * .5f;

        static readonly Color BannerInk = new Color(.36f, .24f, .16f);

        /// <summary>The plaque, kept so the chevrons can be carved into it.</summary>
        Image _banner;

        /// <summary>The name, kept so the chevrons can be set level with its lettering.</summary>
        Text _name;

        /// <summary>
        /// A chapter chevron, cut into the plaque itself.
        /// </summary>
        /// <remarks>
        /// The glyph is drawn by a <see cref="Text"/> and the tap is taken by an invisible
        /// box around it, rather than the glyph being a label on a button. A chevron on a
        /// button skin would be a control sitting on the plaque; this is a mark carved into
        /// it. The box is 84×112 because the mark is small and a target has to be reachable
        /// with a thumb — the two sizes are unrelated on purpose.
        /// </remarks>
        void Chevron(string name, string glyph, float side, ChapterIndexEntry chapter, bool unlocked)
        {
            var box = UIKit.Box(name, _banner.transform, new Vector2(84f, 112f),
                                new Vector2(.5f, .5f), new Vector2(side * ChevronX, 0f));

            var hit = box.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var target = chapter.Id;
            box.gameObject.AddComponent<Btn>().Setup(() => GoToChapter(target));

            // Bold, and the same shadow the name carries, because it is lettering on the
            // same plaque rather than a symbol placed on top of one — at the book weight it
            // read as a stray punctuation mark beside a heavy title.
            //
            // Dimmed when it leads somewhere still locked, and that is the whole marking it
            // gets. A padlock badge was tried and is wrong here: the plaque is 148px tall
            // and already carries a name, so a second glyph beside the chevron is clutter —
            // and the chevron is not disabled, it genuinely goes there. Dimmed only to .68,
            // because on the first chapter this is the *only* thing offering the rest of the
            // game, and fading the one invitation to look ahead defeats the point of it.
            // What the player finds on arrival is a chapter of padlocked glades, which says
            // "not yet" better than anything that would fit in this space.
            var mark = UIKit.Titled("Glyph", box, glyph, 64, Pal.A(BannerInk, unlocked ? 1f : .68f),
                                    TextAnchor.MiddleCenter, new Vector2(84f, 112f),
                                    new Vector2(.5f, .5f), Vector2.zero, outline: 0f, shadow: 2f);
            mark.fontStyle = FontStyle.Bold;

            // Level with the *lettering*, not with the plaque. Both boxes are centred on the
            // banner, which lines up their line boxes and still reads wrong: a chevron's ink
            // sits about the font's maths axis while capitals run from the baseline to the
            // cap height, so the mark drew 6.5px low beside the name. Measured rather than
            // nudged, because the name shrinks to fit — THE SHALLOWS renders at 34, not the
            // 40 it asks for, and a longer translation lands smaller still, so any constant
            // here would be right for one chapter and wrong for the next.
            box.anchoredPosition = new Vector2(box.anchoredPosition.x, InkMid(_name) - InkMid(mark));
        }

        /// <summary>
        /// The middle of a label's drawn ink, in the label's own local space.
        /// </summary>
        /// <remarks>
        /// Not the middle of its box. A line box belongs to the font and every glyph sits
        /// somewhere different inside it, so two labels centred on the same point are only
        /// level when they happen to be the same size and shape. Reading the generated
        /// vertices is the one way to set a 64pt chevron level with a 34pt word, and it is
        /// measured for the reason <see cref="UIKit.PillFaceLift"/> is a fraction of the art
        /// rather than a number somebody liked.
        ///
        /// <para>
        /// The layout generator is used rather than the render one — the same one
        /// <c>preferredWidth</c> and <c>preferredHeight</c> read — so measuring never
        /// disturbs what is on screen.
        /// </para>
        /// </remarks>
        static float InkMid(Text label)
        {
            if (label == null) return 0f;

            var gen = label.cachedTextGeneratorForLayout;
            gen.Populate(label.text, label.GetGenerationSettings(label.rectTransform.rect.size));

            var verts = gen.verts;
            float lo = float.MaxValue, hi = float.MinValue;

            for (int i = 0; i < verts.Count; i++)
            {
                float y = verts[i].position.y;
                if (y < lo) lo = y;
                if (y > hi) hi = y;
            }

            return lo > hi ? 0f : (lo + hi) * .5f / label.pixelsPerUnit;
        }

        /// <summary>
        /// Opens another mode's map where that mode was left: the chapter last looked at in
        /// it, or wherever the player is up to when there is none.
        ///
        /// A fresh screen rather than a repaint: the map is a chapter's body, its art scope and
        /// a scroll position, and rebuilding those in place is the same work as arriving plus
        /// the risk of leaving half of the old one behind. Going through <c>Flow</c> also gives
        /// the change the transition every other navigation here gets, so a mode swap reads as
        /// going somewhere rather than as the screen glitching.
        /// </summary>
        void SwitchTo(GameMode mode)
        {
            if (!mode.IsValid || mode == Mode) return;

            ModeChoice.Write(mode);

            // Silent on purpose. The row that was tapped has already clicked, and the
            // screen change is its own report - a swoop on top of it read as a noise
            // nothing had asked for.
            Flow.Go<LevelsScreen>(v => v.Mode = mode);
        }

        void GoToChapter(ChapterId id)
        {
            // No sound here either: the arrow that was tapped has already made one, and
            // this used to add a whoosh on top of the one Flow.Go played, so stepping
            // between chapters was three sounds at once.
            Flow.Go<LevelsScreen>(v => v.ChapterId = id);
        }

        // ------------------------------------------------------------------ tips
        /// <summary>
        /// A beat after the iris, so the first thing a player sees is the chapter they asked
        /// for and the second is somebody pointing at the switcher.
        ///
        /// Long enough to read as a separate moment and short enough that it is plainly about
        /// this screen; the nodes are still arriving underneath it, which is the point.
        /// </summary>
        const float TeachDelay = .55f;

        public override void OnPresented()
        {
            _presented = true;
            Teach();
        }

        /// <summary>
        /// Points a first-timer at the mode switcher, once in their life.
        ///
        /// <para>
        /// <b>Why this one control gets a lesson at all.</b> Every other way of playing in the
        /// game is reached through the pill under this screen's header and through nothing else,
        /// and that pill is a closed menu naming only the mode you are already in. A player who
        /// never presses it never learns that the rest of the game is there. Everything else the
        /// map does is either self-evident or costs nothing to miss. Moving it out of the bottom
        /// corner and onto the header makes it far harder to miss and does not retire the
        /// lesson: a closed drop-down still says nothing about what is inside it.
        /// </para>
        /// <para>
        /// <b>Nothing is taught over a control that is not there.</b> <see cref="ModeSwitch"/>
        /// draws no pill while the catalog holds one mode — a rolled-back client, a drop that
        /// has not downloaded, or simply the day before a second mode ships — and
        /// <see cref="TipLedger"/> is a once-in-a-lifetime record joined across every device the
        /// player owns. Spending the lesson on an absent control would mean it can never be
        /// shown again, on the very install that most needs it later. So an absent pill is not
        /// a decision at all: nothing is marked, and the next map asks the same question.
        /// </para>
        /// <para>
        /// It is marked seen by <see cref="TipOverlay"/> on the OK button rather than here, for
        /// that overlay's reason: a player interrupted mid-tip — a call, a crash, the app swapped
        /// out — is taught next time instead of never.
        /// </para>
        /// </summary>
        void Teach()
        {
            if (_taught || !this || !_presented) return;

            // Never over an empty screen. This is the one screen in the game that can be
            // presented before it has drawn anything — Flow gives up waiting on View.Ready
            // after five seconds, and a chapter body that slow still arrives eventually — and
            // a lesson spent pointing at a blank map is spent for good. So the
            // map has to exist first, and the wait costs nothing because BuildChapter calls
            // this again the moment it does. Exactly the bargain HomesteadScreen makes with
            // its catalog, for exactly the same reason.
            if (_layout == null) return;

            if (_modes == null) return;

            if (TipLedger.HasSeen(Mechanic.ModeSwitch)) { _taught = true; return; }

            // Something else is already speaking. Nothing on this screen raises a modal of its
            // own, so this is a guard rather than a case — and giving up costs nothing here,
            // because the map is the screen a player is returned to after every run.
            if (Flow.HasModal) return;

            _taught = true;

            Tween.After(TeachDelay, () =>
            {
                if (!this || _modes == null || Flow.HasModal) return;

                Flow.Modal<TipOverlay>(v =>
                {
                    v.Mechanic = Mechanic.ModeSwitch;

                    // The pill itself, so the ring is cut around the real control on the real
                    // screen rather than around a description of where it is.
                    v.Target = _modes;
                });
            }, this);
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

            var target = LevelUnlock.NextToPlay(_index, Mode);
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
            float range = MapHeight - v;
            if (range <= 1f) return 0f;
            return Mathf.Clamp01((fraction * MapHeight - v * .5f) / range);
        }

        void Open(LevelId id, bool unlocked)
        {
            if (!unlocked)
            {
                if (_nodes.TryGetValue(id, out var node)) Tween.Shake(node, 12f, .35f);

                // Two rules refuse a glade and they need different sentences. Inside a chapter
                // it is the chain - clear the one before this. At the head of a chapter it is
                // the star gate, and telling that player to clear the level before it would
                // point them at a glade in another chapter they may well have already cleared.
                var gate = LevelUnlock.IsChapterHead(_index, id)
                    ? LevelUnlock.GateFor(_index, _index.ChapterOf(id))
                    : ChapterGate.Open;

                Scenery.Toast(Content,
                              gate.Exists ? GateLine(gate) : Loc.Get("ui.levels.locked_hint"),
                              Pal.Parchment, gate.Exists ? 2.4f : 1.8f);
                return;
            }
            // The gate. Checked on the way in rather than on the way out of a defeat,
            // so a player is never dropped into a glade they cannot afford to lose —
            // being told at the door is a wait, being told at the blast is a wasted run.
            //
            // A free run walks straight past it, and it has to: a glade that costs nothing to
            // lose cannot coherently be refused for lack of something to lose. That is a mode's
            // opening glades — where the one player this door would shut out is the one who has
            // just met the mode — and every glade this player has already finished, which is
            // the door standing open on the whole of what somebody has beaten while their
            // hearts fill. See HeartStake.
            //
            // Asked through PlayRoute rather than assembled here, because this is not the only
            // door and the three others were opening charged runs on an empty bar. What stays
            // here is what only the map can do about it: shake the node that was refused.
            if (!PlayRoute.CanOpen(id))
            {
                if (_nodes.TryGetValue(id, out var barred)) Tween.Shake(barred, 10f, .35f);
                Flow.Modal<OutOfHeartsOverlay>();
                return;
            }

            Audio.Sfx("enter", .55f);

            // Which screen a mode opens on lives in PlayRoute, because this is not the only
            // door into a run - the victory panel's "next glade" and its replay are two more,
            // and both of them opened the classic screen on a hollow until it was moved.
            PlayRoute.Open(id);
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

        /// <summary>
        /// <paramref name="scale"/> is the map's own, and the dots take it because a trail is
        /// part of the painting rather than a control on top of it: the ends it joins are
        /// fractions, so on a map drawn half again as large the gaps between the dots grow and
        /// the dots would not — a dotted path becoming a sparser one for no reason a player
        /// could name. One on every phone.
        /// </summary>
        public void Setup(RectTransform area, Vector2 fracA, Vector2 fracB, int count, Color colour,
                          bool live, float scale = 1f)
        {
            _area = area; _a = fracA; _b = fracB; _live = live;
            _dots = new Image[count];
            var host = UIKit.Node("Trail", area);
            for (int i = 0; i < count; i++)
            {
                float k = (i + 1f) / (count + 1f);
                float size = Mathf.Lerp(22f, 34f, Mathf.Sin(k * Mathf.PI)) * scale;
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
