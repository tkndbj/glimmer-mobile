using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Hollowmarch drawn: a haul-road winding across a dark village, a line of lit pods walking
    /// it, and a launcher at the near end with the next three cores stacked in it.
    ///
    /// <para>
    /// <b>Everything here is in service of one moment.</b> A core lands, three alike go off, the
    /// line slides shut — and if the closure makes three more it goes again, louder and a
    /// semitone higher, with the shake growing and a multiplier climbing over the blast. That
    /// chain is the whole reason this mode exists, so it gets the largest drawing in it: that is
    /// invariant 20m's first rule, which the Tanglewood learned by shipping five mechanics whose
    /// payoff was drawn the same size as everything else and hearing "all I see is flowers
    /// popping".
    /// </para>
    /// <para>
    /// <b>The board replays snapshots rather than reconstructing them.</b> <c>MarchShot</c> comes
    /// back with one <c>MarchFrame</c> per beat — as the core landed, after each wave, and after
    /// the march — and every deed is stamped with a track <em>slot</em> rather than a position in
    /// the line. That is invariant 30i taken seriously: the line shifts under its own indices as
    /// pods leave it, so a view that worked out where everything ended up would be doing exactly
    /// the arithmetic that no par, no <c>ways</c>, no validator and no content gate can ever see
    /// going wrong, because all of those read the model and the model is right the whole time.
    /// </para>
    /// <para>
    /// <b>What the aim preview may show is geometry and never outcome</b> (invariant 32c): while
    /// a finger is down the road lights up between the launcher and where the core will wedge,
    /// which is a fact the player can already read off the board drawn faster. What it never
    /// draws is what the blast will catch, because that is the thing they are working out —
    /// Budburst's withdrawn halo, and the reason it was withdrawn.
    /// </para>
    /// </summary>
    public sealed class MarchView : ProtoView
    {
        // ------------------------------------------------------------------ tempo
        /// <summary>How long a core takes to reach the far end of the road. Scaled by distance.</summary>
        const float FlightFar = .42f;
        const float FlightNear = .16f;

        /// <summary>The beat a wave takes: the flash, then the blast, then the slide.</summary>
        const float FlashStep = .13f;
        const float BlastStep = .30f;
        const float SlideStep = .21f;

        /// <summary>The march. Slower than the slide, because it is the thing to be afraid of.</summary>
        const float MarchStep = .30f;

        /// <summary>What a critter's run home takes.</summary>
        const float RunHome = .70f;

        // ------------------------------------------------------------------ what the screen hears
        /// <summary>A story beat the level may want to answer. Raised once per shot at most.</summary>
        public Action<StoryCue> Say { get; set; }

        // ------------------------------------------------------------------ the board
        MarchLayout _layout;
        MarchBoard _board;

        /// <summary>One widget per pod, in line order. Kept in step with the board by hand.</summary>
        readonly List<Pod> _pods = new List<Pod>(32);

        readonly List<Image> _rails = new List<Image>(64);
        readonly List<MarchMove> _moves = new List<MarchMove>(8);
        readonly List<int> _runs = new List<int>(16);

        RectTransform _road, _line, _sky;
        Image _portal, _bolt;
        RectTransform _magazine;
        readonly List<Image> _queue = new List<Image>(3);

        int _held = -1;

        /// <summary>
        /// One drawn pod. A node with the body in it, and the two overlays that may hang on it.
        ///
        /// A class rather than a struct because the list is re-ordered on every wave and every
        /// entry is a live scene node; a struct would be copied into the new list and the tween
        /// would still be pointing at the old one.
        /// </summary>
        sealed class Pod
        {
            public RectTransform Node;
            public Image Body;
            public Image Cage;
            public Image Plate;
            public Image Crew;
            public char What;
            public int Slot;
        }

        // ------------------------------------------------------------------ art
        /// <summary>
        /// One of this mode's sprites, addressed through <see cref="AssetManifest"/>.
        ///
        /// <b>Every art lookup on this screen goes through here and none of them builds a
        /// path</b>, which is invariant 7 and was learned the expensive way: `Boom` spelt its
        /// own folder out, asked for `Art/March/boom_fire` when the explosions live under
        /// `Art/Fx/March/`, and drew a white rectangle two cells wide at every burst. A path
        /// built by hand in two places is two paths.
        /// </summary>
        static UnityEngine.Sprite Piece(string key)
            => AssetLibrary.Sprite(AssetManifest.MarchArt(key));

        /// <summary>
        /// One of this mode's flipbooks, as frames.
        ///
        /// A one-line wrapper rather than the call spelt out inside <see cref="Book"/>, so that
        /// <c>Tools/verify/artnames.py</c> can read it: the gate follows a thin wrapper whose
        /// whole body is a manifest lookup, which is what makes every <c>Reel("bolt")</c> below
        /// a name something checks. Centralising a lookup is only safe if the check follows it.
        /// </summary>
        static UnityEngine.Sprite[] Reel(string key)
            => AssetLibrary.Frames(AssetManifest.MarchArt(key));

        /// <summary>
        /// A flipbook widget, or <b>null</b> when its frames are not there.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand.</b> An <c>Image</c> with a
        /// null sprite is a <em>white rectangle</em> and not a blank (invariant 7b), so
        /// attaching a flipbook to an empty folder puts a solid square on the board rather than
        /// nothing at all. Built this way round, missing art costs the thing it draws and never
        /// costs a square of white over the board.
        /// </para>
        /// <para>
        /// <b>The key is the first argument and that is not a style choice.</b>
        /// <c>Tools/verify/artnames.py</c> reads the literals in a lookup's <em>first</em>
        /// argument, so a widget name in front of the key would be checked against the art
        /// folder and the key itself would go unread — the check would be looking at the wrong
        /// string and saying so confidently.
        /// </para>
        /// </summary>
        Image Book(string key, string name, RectTransform parent, float size, float fps,
                    bool loop = true)
        {
            var frames = Reel(key);
            if (frames == null || frames.Length == 0) return null;

            var img = UIKit.Img(name, parent, frames[0], Color.white, new Vector2(size, size));
            img.raycastTarget = false;
            Flipbook.Attach(img, frames, fps, loop);
            return img;
        }

        // ------------------------------------------------------------------ geometry
        /// <summary>Where a track slot is on the field.</summary>
        Vector2 At(int slot)
        {
            int cell = _layout.CellOf(slot);
            return cell < 0 ? Vector2.zero : CentreOf(cell);
        }

        Vector2 AtLauncher()
        {
            var path = _layout.Path;
            return path == null || path.Length == 0 ? Vector2.zero
                                                    : CentreOf(path[path.Length - 1]);
        }

        // ------------------------------------------------------------------ building
        protected override void Compose()
        {
            _layout = ((MarchRules)Rules).Layout;
            _board = (MarchBoard)Run.Board;

            _pods.Clear();
            _rails.Clear();
            _queue.Clear();
            _held = -1;

            _sky = UIKit.Node("Ground", Field);
            _road = UIKit.Node("Road", Field);
            _line = UIKit.Node("Line", Field);

            foreach (var rt in new[] { _sky, _road, _line })
            {
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.sizeDelta = Field.sizeDelta;
                rt.anchoredPosition = Vector2.zero;
            }

            Ground();
            Rails();
            Gate();
            Launcher();
            Deal();
            Magazine();

            Targets();
        }

        void Ground()
        {
            var grid = _layout.Grid;

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);

                var img = UIKit.Img("g", _sky,
                                    Piece(c == MarchLayout.Rubble ? "rubble" : "ground"),
                                    Color.white, new Vector2(Cell, Cell));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition = CentreOf(i);
            }
        }

        void Rails()
        {
            var path = _layout.Path;

            for (int i = 0; i < path.Length; i++)
            {
                var img = UIKit.Img("r", _road, Piece("road"), Color.white,
                                    new Vector2(Cell, Cell));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition = CentreOf(path[i]);
                _rails.Add(img);
            }
        }

        void Gate()
        {
            var path = _layout.Path;
            if (path.Length == 0) return;

            _portal = UIKit.Img("Portal", _road, Piece("portal"), Color.white,
                                new Vector2(Cell * 1.55f, Cell * 1.55f));
            _portal.raycastTarget = false;
            _portal.rectTransform.anchoredPosition = CentreOf(path[0]);

            // A slow breath, so the gate is the one thing on a still board that is moving. It is
            // what the player is racing, and a still threat reads as scenery.
            Tween.Run(2.4f, Ease.InOutSine, t =>
            {
                if (!_portal) return;
                float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                _portal.transform.localScale = Vector3.one * Mathf.Lerp(.94f, 1.08f, k);
                _portal.color = Pal.A(Color.white, Mathf.Lerp(.72f, 1f, k));
            }, _portal).Loop();
        }

        void Launcher()
        {
            _bolt = Book("bolt", "Bolt", _road, Cell * 1.5f, 12f);
            if (_bolt != null)
                _bolt.rectTransform.anchoredPosition = AtLauncher() + new Vector2(0f, Cell * .18f);
        }

        void Deal()
        {
            var line = _board.Line;
            for (int i = 0; i < line.Count; i++) _pods.Add(Make(line[i], _board.SlotOf(i)));
        }

        Pod Make(char what, int slot)
        {
            var node = UIKit.Node("Pod", _line);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(Cell, Cell);
            node.anchoredPosition = At(slot);

            var pod = new Pod { Node = node, What = what, Slot = slot };

            if (MarchLayout.IsRaider(what))
            {
                // A raider is drawn as itself rather than as a pod with a face on it: the whole
                // point of one is that it wears no colour, so it must not look like something a
                // core could be matched to.
                pod.Crew = Book(what == MarchLayout.Hauler ? "drone" : "brute", "Crew",
                                node, Cell * 1.25f, 12f);

                if (what != MarchLayout.Hauler)
                {
                    pod.Plate = UIKit.Img("Plate", node, Piece("plate"), Color.white,
                                          new Vector2(Cell * 1.05f, Cell * 1.05f));
                    pod.Plate.raycastTarget = false;
                }
            }
            else
            {
                pod.Body = UIKit.Img("Body", node, Sprite(what), Color.white,
                                     new Vector2(Cell * .92f, Cell * .92f));
                pod.Body.raycastTarget = false;

                if (MarchLayout.IsCaged(what))
                {
                    pod.Cage = UIKit.Img("Cage", node, Piece("cage"), Color.white,
                                         new Vector2(Cell * .92f, Cell * .92f));
                    pod.Cage.raycastTarget = false;

                    // A caged pod is the thing the level is about, so it gets the one halo on
                    // the board that is not an explosion.
                    var halo = UIKit.Halo(node, Pal.Cream, Cell * 1.5f, .30f);
                    halo.transform.SetAsFirstSibling();
                    Tween.Run(1.6f, Ease.InOutSine, t =>
                    {
                        if (!halo) return;
                        float k = Mathf.Sin(t * Mathf.PI * 2f) * .5f + .5f;
                        halo.color = Pal.A(Pal.Cream, Mathf.Lerp(.16f, .38f, k));
                    }, halo).Loop();
                }
            }

            return pod;
        }

        static UnityEngine.Sprite Sprite(char what)
        {
            char hue = MarchLayout.Hue(what);
            return Piece(hue == 'R' ? "pod_r" : hue == 'G' ? "pod_g"
                       : hue == 'B' ? "pod_b" : "pod_y");
        }

        static Color Tint(char hue)
            => hue == 'R' ? Pal.Poppy : hue == 'G' ? Pal.Mint
             : hue == 'B' ? Pal.Azure : Pal.Gold;

        // ------------------------------------------------------------------ the magazine
        /// <summary>
        /// The next three cores, stacked at the launcher.
        ///
        /// <para>
        /// <b>Three rather than one, and that is a rule about the puzzle rather than about the
        /// furniture.</b> The magazine is ordered and repeating (invariant 20e), so what is
        /// coming is knowable — and a mode where the player can only see the core in hand is one
        /// where every shot is a reaction. Showing two ahead is what makes "dump this one so the
        /// blue behind it lands where I want" a plan somebody can have.
        /// </para>
        /// </summary>
        void Magazine()
        {
            // **Offset inward rather than downward**, which a render caught and no gate could
            // have: a launcher may be authored in any corner of the board, so a fixed offset
            // puts the cores off the plate entirely on three roads out of four.
            Vector2 home = AtLauncher();
            var inward = new Vector2(home.x > 0f ? -1f : 1f, home.y > 0f ? -1f : 1f);

            _magazine = UIKit.Node("Magazine", _road);
            _magazine.anchorMin = _magazine.anchorMax = new Vector2(.5f, .5f);
            _magazine.sizeDelta = new Vector2(Cell * 2.4f, Cell);
            _magazine.anchoredPosition = home + new Vector2(0f, inward.y * Cell * .95f);

            for (int i = 0; i < 3; i++)
            {
                float size = i == 0 ? Cell * .80f : Cell * .48f;
                var img = UIKit.Img("core" + i, _magazine, null, Color.white,
                                    new Vector2(size, size));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition =
                    new Vector2(inward.x * Cell * (i == 0 ? 0f : .52f + (i - 1) * .42f), 0f);
                _queue.Add(img);
            }
        }

        void Reload()
        {
            string cores = _layout.Cores;
            if (cores.Length == 0) return;

            for (int i = 0; i < _queue.Count; i++)
            {
                var img = _queue[i];
                if (img == null) continue;

                bool spark = i == 0 && _board.Spark;
                img.sprite = spark ? Piece("pod_spark")
                                   : Sprite(cores[(_board.Index + i) % cores.Length]);
                img.color = Color.white;
            }
        }

        // ------------------------------------------------------------------ painting
        protected override void Repaint()
        {
            if (_layout == null) return;

            Reload();

            for (int i = 0; i < _pods.Count; i++)
            {
                var pod = _pods[i];
                if (pod.Node == null) continue;

                pod.Slot = _board.SlotOf(i);
                pod.Node.anchoredPosition = At(pod.Slot);
                pod.Node.localScale = Vector3.one;
            }

            Unlight();
        }

        // ------------------------------------------------------------------ the aim
        /// <summary>The launcher, as a slot value. It is not one, which is why it is negative.</summary>
        const int Barrel = -1;

        /// <summary>
        /// One hit target per road cell, plus the launcher.
        ///
        /// <b>Per cell rather than one catcher over the whole board</b>, which is
        /// <c>BudView</c>'s idiom and right for the same reason: a pod is a disc drawn over a
        /// square, so hit-testing the <em>cell</em> and then asking the board which run that is
        /// keeps the drawing and the rule from ever disagreeing about what was touched. A target
        /// sits on the road whether or not anything is standing on it, so a tap on an empty
        /// stretch is answered rather than swallowed.
        /// </summary>
        void Targets()
        {
            for (int slot = 0; slot < _layout.Track; slot++) Target(slot, _layout.CellOf(slot));

            var path = _layout.Path;
            if (path.Length > 0) Target(Barrel, path[path.Length - 1]);
        }

        void Target(int slot, int cell)
        {
            var img = UIKit.Img("hit", Field, Art.Pixel, new Color(0f, 0f, 0f, 0f),
                                new Vector2(Cell, Cell));
            img.raycastTarget = true;
            img.rectTransform.anchoredPosition = CentreOf(cell);

            img.gameObject.AddComponent<Btn>().Setup(() => Tap(slot), silent: true);

            var hover = img.gameObject.AddComponent<Hover>();
            hover.Enter = () => Aiming(slot);
            hover.Exit = Unlight;
        }

        /// <summary>
        /// Lights the road between the launcher and where a core would land, and lights what it
        /// would land beside. Geometry and never outcome (invariant 32c).
        /// </summary>
        void Aiming(int slot)
        {
            Unlight();
            if (!Playable) return;

            _held = slot;

            if (!Aimed(slot, out MarchMove move)) return;

            int landing = move.Aim == MarchAim.Dump ? _board.Tail - 1 : _board.SlotOf(move.At);
            LightRoad(landing);
            LightRun(move);
        }

        void Tap(int slot)
        {
            _held = -1;
            Unlight();

            if (!Playable) return;

            if (!Aimed(slot, out MarchMove move))
            {
                // A tap on bare road is not worth a sentence; one on a pod whose colour is not
                // the one in hand is the mode's single rule the board cannot show for itself.
                if (slot >= _board.Head && slot < _board.Tail) Refused?.Invoke();
                else Audio.Sfx("blocked", .28f, 1.2f);
                return;
            }

            StartCoroutine(Play(move));
        }

        /// <summary>Which legal move covers a slot, if any.</summary>
        bool Aimed(int slot, out MarchMove move)
        {
            move = default;
            if (_board == null) return false;

            _board.Moves(_moves);

            for (int i = 0; i < _moves.Count; i++)
            {
                var candidate = _moves[i];

                // The launcher is the dump: tapping your own barrel drops the core at the back
                // of the line. It is a real move rather than an escape hatch (see
                // MarchBoard.Moves), so it wants a control somebody can find without being told.
                if (candidate.Aim == MarchAim.Dump)
                {
                    if (slot != Barrel) continue;
                    move = candidate;
                    return true;
                }

                if (slot < 0) continue;

                int at = candidate.Aim == MarchAim.Lance ? LanceFrom(candidate.At) : candidate.At;
                int span = candidate.Aim == MarchAim.Lance ? LanceSpan(candidate.At)
                                                           : RunLength(candidate.At);
                int from = _board.SlotOf(at);

                if (slot < from || slot >= from + span) continue;

                move = candidate;
                return true;
            }

            return false;
        }

        int RunLength(int at)
        {
            _board.Runs(_runs);
            for (int i = 0; i < _runs.Count; i += 2)
                if (_runs[i] == at) return _runs[i + 1];
            return 1;
        }

        void LightRoad(int landing)
        {
            var path = _layout.Path;
            int upto = -1;
            for (int i = 0; i < path.Length; i++)
                if (path[i] == _layout.CellOf(landing)) { upto = i; break; }

            if (upto < 0) return;

            for (int i = upto; i < _rails.Count; i++)
                if (_rails[i] != null) _rails[i].sprite = Piece("road_lit");
        }

        void LightRun(MarchMove move)
        {
            if (move.Aim == MarchAim.Dump)
            {
                if (_bolt != null) _bolt.transform.localScale = Vector3.one * 1.12f;
                return;
            }

            int span = move.Aim == MarchAim.Lance ? LanceSpan(move.At) : RunLength(move.At);
            int from = move.Aim == MarchAim.Lance ? LanceFrom(move.At) : move.At;

            for (int i = from; i < from + span && i < _pods.Count; i++)
            {
                var pod = _pods[i];
                if (pod.Node == null) continue;
                pod.Node.localScale = Vector3.one * 1.14f;
            }
        }

        /// <summary>
        /// Where a lance would reach, asked of the board rather than worked out here.
        ///
        /// A second copy of "the run and the group either side of it" is a second thing that can
        /// come to disagree with what firing actually does — and the preview disagreeing with the
        /// move is the one class of fault a player reads as the game cheating. So it forks and
        /// asks.
        /// </summary>
        int LanceFrom(int at) => Preview(at, out int from, out _) ? from : at;
        int LanceSpan(int at) => Preview(at, out int from, out int to) ? to - from + 1 : 1;

        bool Preview(int at, out int from, out int to)
        {
            from = at;
            to = at;

            var forked = _board.Fork();
            var log = forked.Fire(new MarchMove(MarchAim.Lance, at));
            if (log == null) return false;

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Deed != MarchDeed.Lance) continue;

                from = deed.At - _board.Head;
                to = deed.Span - _board.Head;
                return true;
            }

            return false;
        }

        void Unlight()
        {
            for (int i = 0; i < _rails.Count; i++)
                if (_rails[i] != null) _rails[i].sprite = Piece("road");

            for (int i = 0; i < _pods.Count; i++)
                if (_pods[i].Node != null) _pods[i].Node.localScale = Vector3.one;

            if (_bolt != null) _bolt.transform.localScale = Vector3.one;
        }

        // ------------------------------------------------------------------ the shot
        IEnumerator Play(MarchMove move)
        {
            Busy = true;
            HideCoach();

            bool spark = _board.Spark;
            char hue = _board.Core;

            var log = _board.Fire(move);
            if (log == null) { Busy = false; yield break; }

            Took(log.Took + log.Goals);
            Reload();

            int frame = 0;

            if (move.Aim == MarchAim.Lance)
            {
                yield return Lance(log);
                frame = 1;
            }
            else
            {
                yield return Wedge(log, hue, spark);
                frame = 1;
            }

            for (int wave = 1; wave <= log.Waves; wave++)
            {
                yield return Wave(log, wave, frame);
                frame++;
            }

            if (log.Forged) yield return Forge();

            yield return March(log, log.Frames.Count - 1);

            Voice(log);

            Busy = false;
            Settle();
        }

        /// <summary>The core's flight from the launcher to where it wedges in.</summary>
        IEnumerator Wedge(MarchShot log, char hue, bool spark)
        {
            int landing = -1;
            for (int i = 0; i < log.Deeds.Count; i++)
                if (log.Deeds[i].Deed == MarchDeed.Wedge) landing = log.Deeds[i].At;

            Vector2 from = AtLauncher();
            Vector2 to = landing < 0 ? from : At(landing);

            var shot = UIKit.Img("Core", _line,
                                 spark ? Piece("pod_spark") : Sprite(hue), Color.white,
                                 new Vector2(Cell * .84f, Cell * .84f));
            shot.raycastTarget = false;
            shot.rectTransform.anchoredPosition = from;

            Audio.Sfx("whoosh", .40f, 1.25f);

            float far = Vector2.Distance(from, to) / Mathf.Max(1f, Cell * 8f);
            float flight = Mathf.Lerp(FlightNear, FlightFar, Mathf.Clamp01(far));

            var trail = UIKit.Halo(_line, Tint(hue), Cell * 1.1f, .5f);
            trail.raycastTarget = false;

            Tween.Run(flight, Ease.OutQuad, t =>
            {
                if (!shot) return;
                var p = Vector2.Lerp(from, to, t);
                shot.rectTransform.anchoredPosition = p;
                if (trail) trail.rectTransform.anchoredPosition = p;
            }, shot);

            yield return new WaitForSeconds(flight);

            if (shot) Destroy(shot.gameObject);
            if (trail) Destroy(trail.gameObject);

            // The wedge itself: the new pod arrives and everything behind it takes a step back.
            Rebuild(log.Frames[0], SlideStep * .8f);
            Audio.Sfx("tock", .5f, 1.1f);
            Pop(At(landing), Tint(hue), 1.3f, .22f);

            yield return new WaitForSeconds(SlideStep * .8f);
        }

        /// <summary>The Spark: a beam drawn the length of what it cut, then the cut itself.</summary>
        IEnumerator Lance(MarchShot log)
        {
            int from = -1, to = -1;
            for (int i = 0; i < log.Deeds.Count; i++)
            {
                if (log.Deeds[i].Deed != MarchDeed.Lance) continue;
                from = log.Deeds[i].At;
                to = log.Deeds[i].Span;
                break;
            }

            if (from < 0) yield break;

            Audio.Sfx("shatter", .8f, .9f);
            ShakeBoard(20f);

            // The beam is drawn along the road it cuts rather than as a straight line between
            // the two ends: the road bends, and a lance that cut through a hillside would read
            // as a bug rather than as a weapon.
            for (int slot = from; slot <= to; slot++)
            {
                var flare = UIKit.Img("Lance", _line, Art.Glow(96), Pal.A(Pal.Cream, .95f),
                                      new Vector2(Cell * 1.25f, Cell * 1.25f));
                flare.raycastTarget = false;
                flare.rectTransform.anchoredPosition = At(slot);

                Tween.Scale(flare.transform, 1.7f, .34f, Ease.OutCubic);
                Tween.Fade(flare, 0f, .34f, Ease.OutQuad)
                     .OnDone(() => { if (flare) Destroy(flare.gameObject); });

                yield return new WaitForSeconds(.028f);
            }

            yield return new WaitForSeconds(.06f);
        }

        /// <summary>One wave: the flash, the blast, the rescue, and the line sliding shut.</summary>
        IEnumerator Wave(MarchShot log, int wave, int frame)
        {
            var going = new List<Pod>(8);
            int freed = 0, scrapped = 0, staggered = 0;

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                var deed = log.Deeds[i];
                if (deed.Wave != wave) continue;

                var pod = PodAt(deed.At);

                switch (deed.Deed)
                {
                    case MarchDeed.Burst:
                        if (pod != null) going.Add(pod);
                        break;

                    case MarchDeed.Free:
                        if (pod != null) going.Add(pod);
                        freed++;
                        break;

                    case MarchDeed.Scrap:
                        if (pod != null) going.Add(pod);
                        scrapped++;
                        break;

                    case MarchDeed.Stagger:
                        staggered++;
                        Stagger(pod);
                        break;
                }
            }

            if (going.Count == 0 && staggered == 0) yield break;

            // The flash: everything that is about to go swells and goes white for a beat. It is
            // the cheapest anticipation there is and the reason a burst reads as caused rather
            // than as a cut.
            for (int i = 0; i < going.Count; i++)
            {
                var pod = going[i];
                if (pod.Node == null) continue;

                Tween.Scale(pod.Node, 1.3f, FlashStep, Ease.OutQuad);
                if (pod.Body != null) Tween.Tint(pod.Body, Color.white, FlashStep);
            }

            yield return new WaitForSeconds(FlashStep);

            // The blast. Every wave is louder, higher and shakes harder than the one before it,
            // which is the whole of what makes a chain feel like it is building rather than
            // repeating.
            float pitch = Mathf.Min(1.85f, .92f + (wave - 1) * .13f);
            Audio.Sfx(wave > 1 ? "burst" : "pop", Mathf.Min(1f, .62f + wave * .10f), pitch);
            ShakeBoard(Mathf.Min(34f, 9f + wave * 6f));

            for (int i = 0; i < going.Count; i++)
            {
                var pod = going[i];
                Vector2 where = At(pod.Slot);

                Boom(where, pod.What, wave);

                if (MarchLayout.IsCaged(pod.What)) Rescue(pod, where);

                if (pod.Node != null)
                {
                    var node = pod.Node;
                    Tween.Scale(node, .1f, BlastStep, Ease.InBack)
                         .OnDone(() => { if (node) Destroy(node.gameObject); });
                }

                _pods.Remove(pod);
            }

            if (wave > 1) Multiplier(going.Count > 0 ? At(going[0].Slot) : Vector2.zero, wave);

            if (freed > 0) Audio.Sfx("free", .85f, 1f);
            if (scrapped > 0) Audio.Sfx("shatter", .55f, 1.05f);

            yield return new WaitForSeconds(BlastStep * .55f);

            // And the slide, which is this mode's signature: the line closes the gap it just
            // made, and everything the player is about to be able to do follows from where it
            // ends up.
            Rebuild(log.Frames[frame], SlideStep);
            Audio.Sfx("rotate_b", .30f, 1.15f);

            yield return new WaitForSeconds(SlideStep);
        }

        /// <summary>The armour turning a blast away: the one refusal this board shows for itself.</summary>
        void Stagger(Pod pod)
        {
            if (pod == null) return;

            pod.What = MarchLayout.WardenHit;

            if (pod.Plate != null)
            {
                var plate = pod.Plate;
                Tween.Tint(plate, Color.white, .06f).OnDone(() =>
                {
                    if (plate) Tween.Fade(plate, 0f, .34f, Ease.OutQuad);
                });
            }

            if (pod.Node != null) Tween.Shake(pod.Node, Cell * .14f, .28f);
            Audio.Sfx("blocked", .5f, .9f);
            Burst.Sparks(_line, At(pod.Slot), Pal.Glass, 8, 120f, 18f, .4f);
        }

        /// <summary>
        /// The explosion. Licensed frames rather than a particle spray, because a chain of these
        /// is the largest thing that happens in this mode and a spray reads as dust.
        ///
        /// <para>
        /// <b>The widget is not built until the frames are in hand, and that is invariant 7b
        /// rather than caution.</b> An <c>Image</c> with a null sprite is a <em>white
        /// rectangle</em>, not a blank — so a flipbook attached to an empty folder leaves a
        /// square two cells wide sitting on the road at every burst. Building it the other way
        /// round means a missing folder costs a quieter explosion (the sparks and the shockwave
        /// still play) and never a white square.
        /// </para>
        /// <para>
        /// <b>And the address comes from <see cref="AssetManifest"/> rather than from a string
        /// built here</b>, which is invariant 7 and was learned the expensive way: this method
        /// asked for <c>Art/March/boom_fire</c> when the explosions live under
        /// <c>Art/Fx/March/</c>, so every burst threw an <c>InvalidKeyException</c> and drew the
        /// white rectangle above. A path built by hand in two places is two paths, and one of
        /// them is wrong.
        /// </para>
        /// </summary>
        void Boom(Vector2 where, char what, int wave)
        {
            var frames = AssetLibrary.Frames(AssetManifest.MarchFx(
                MarchLayout.IsRaider(what) ? "boom_smoke"
              : MarchLayout.IsCaged(what) ? "boom_gold"
              : wave > 2 ? "boom_red" : wave > 1 ? "boom_spark" : "boom_fire"));

            if (frames != null && frames.Length > 0)
            {
                var img = UIKit.Img("Boom", _line, frames[0], Color.white,
                                    new Vector2(Cell * 2.1f, Cell * 2.1f));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition = where;

                var book = Flipbook.Attach(img, frames, 26f, false);
                book.OnFinished = () => { if (img) Destroy(img.gameObject); };

                // A guard as well as the callback: a flipbook whose owner is torn down mid-play
                // never reaches OnFinished, and this is the one widget on the board that is
                // created faster than anything else destroys it.
                Tween.After(1.4f, () => { if (img) Destroy(img.gameObject); }, this);
            }

            Shockwave(where, Tint(MarchLayout.Hue(what)), 2.2f, .38f);

            Burst.Sparks(_line, where, Tint(MarchLayout.Hue(what)), 12, 170f, 22f, .5f);
        }

        /// <summary>A critter out of a broken cage, running home to the launcher.</summary>
        void Rescue(Pod pod, Vector2 where)
        {
            // Written out rather than built from the number, so the three folders are names
            // `artnames.py` can hold to disk. A critter that did not load used to be a white
            // square running across the board (invariant 7b).
            int who = Mathf.Abs(pod.Slot) % 3;

            var img = Book(who == 0 ? "mon1_jump" : who == 1 ? "mon2_jump" : "mon3_jump",
                           "Free", _line, Cell * 1.15f, 16f);
            if (img == null) { Shockwave(where, Pal.Gold, 2.6f, .5f); return; }

            img.rectTransform.anchoredPosition = where;

            Vector2 home = AtLauncher();
            float lift = Cell * 1.4f;

            Tween.Run(RunHome, Ease.OutQuad, t =>
            {
                if (!img) return;
                var p = Vector2.Lerp(where, home, t);
                p.y += Mathf.Sin(t * Mathf.PI) * lift;
                img.rectTransform.anchoredPosition = p;
                img.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.15f, .7f, t);
            }, img).OnDone(() =>
            {
                if (!img) return;
                Burst.Sparks(_line, home, Pal.Cream, 10, 130f, 20f, .45f);
                Destroy(img.gameObject);
            });

            Shockwave(where, Pal.Gold, 2.6f, .5f);
        }

        /// <summary>
        /// The chain count, over the blast that caused it.
        ///
        /// It is the one number this mode puts on the board and it is deliberately not a score:
        /// what it says is <em>that this went off because the last one did</em>, which is the
        /// thing the player did and the thing they will try to do again.
        /// </summary>
        void Multiplier(Vector2 where, int wave)
        {
            var text = UIKit.Titled("Chain", _line, "x" + wave, Mathf.RoundToInt(Cell * .62f),
                                    Pal.Gold, TextAnchor.MiddleCenter,
                                    new Vector2(Cell * 3f, Cell), new Vector2(.5f, .5f), where);
            text.raycastTarget = false;

            var rt = text.rectTransform;
            rt.localScale = Vector3.one * .4f;

            Tween.Scale(rt, 1.15f, .26f, Ease.OutBack);
            Tween.Run(.9f, Ease.OutCubic, t =>
            {
                if (!text) return;
                rt.anchoredPosition = where + new Vector2(0f, Cell * 1.1f * t);
                text.color = Pal.A(Pal.Gold, t > .55f ? (1f - t) / .45f : 1f);
            }, text).OnDone(() => { if (text) Destroy(text.gameObject); });

            Audio.Sfx("star", .55f, Mathf.Min(1.9f, 1f + wave * .12f));
        }

        /// <summary>
        /// A Spark forged. The largest single flourish in the mode, because it is the one thing
        /// on this board the player <em>made</em> (invariant 20m's first rule).
        /// </summary>
        IEnumerator Forge()
        {
            Vector2 home = AtLauncher();

            Audio.Sfx("unlock", .9f, 1f);
            Shockwave(home, Pal.Sun, 4.2f, .62f);
            Burst.Sparks(_line, home, Pal.Sun, 22, 230f, 30f, .7f);
            ShakeBoard(18f);

            var core = _queue.Count > 0 ? _queue[0] : null;
            if (core != null)
            {
                core.sprite = Piece("pod_spark");
                var rt = core.rectTransform;
                rt.localScale = Vector3.one * .2f;
                Tween.Scale(rt, 1f, .46f, Ease.OutElastic);
            }

            Say?.Invoke(StoryCue.Forged);

            yield return new WaitForSeconds(.42f);
        }

        /// <summary>One step nearer the gate, and whatever it costs.</summary>
        IEnumerator March(MarchShot log, int frame)
        {
            bool escaped = false, jammed = false;
            char through = '\0';

            for (int i = 0; i < log.Deeds.Count; i++)
            {
                if (log.Deeds[i].Deed == MarchDeed.Escape)
                {
                    escaped = true;
                    through = log.Deeds[i].What;
                }
                else if (log.Deeds[i].Deed == MarchDeed.Jam)
                {
                    jammed = true;
                }
            }

            if (escaped && _pods.Count > 0)
            {
                var lost = _pods[0];
                _pods.RemoveAt(0);

                var node = lost.Node;
                Vector2 gate = CentreOf(_layout.Path[0]);

                Audio.Sfx("whoosh", .55f, .72f);
                if (_portal != null) Tween.Scale(_portal.transform, 1.35f, .18f, Ease.OutQuad);

                Tween.Run(MarchStep, Ease.InQuad, t =>
                {
                    if (!node) return;
                    node.anchoredPosition = Vector2.Lerp(node.anchoredPosition, gate, t);
                    node.localScale = Vector3.one * Mathf.Lerp(1f, .12f, t);
                }, node).OnDone(() => { if (node) Destroy(node.gameObject); });

                Pop(gate, Pal.Foxglove, 2.2f, .34f);
            }

            if (jammed)
            {
                // The picture the whole mode is built around: the cage arrives at the gate and
                // stops there. The raiders will not abandon it, so nothing is lost - and a
                // player looking at it knows exactly how much time they have left.
                if (_pods.Count > 0 && _pods[0].Node != null)
                    Tween.Shake(_pods[0].Node, Cell * .10f, .34f);

                if (_portal != null) Tween.Scale(_portal.transform, 1.25f, .28f, Ease.OutQuad);
                Audio.Sfx("blocked", .35f, .7f);
            }

            Rebuild(log.Frames[frame], MarchStep);

            if (!escaped && !jammed) Audio.Sfx("tick", .22f, .85f);

            yield return new WaitForSeconds(MarchStep);
        }

        /// <summary>
        /// Puts the drawn line back in step with a frame, sliding everything to where it now
        /// stands.
        ///
        /// The widget list is re-indexed against the frame rather than trusted: after a wave the
        /// board has decided which pods survived and this has already destroyed the rest, so the
        /// two are the same length or something has gone wrong — and a silent mismatch here
        /// would draw a line that is not the line being played.
        /// </summary>
        void Rebuild(MarchFrame frame, float seconds)
        {
            int n = Mathf.Min(_pods.Count, frame.Line.Length);

            // A frame carrying a pod the view does not have is a wedge: the core that has just
            // landed is a new widget rather than one that moved.
            if (frame.Line.Length > _pods.Count)
            {
                for (int i = 0; i < frame.Line.Length; i++)
                {
                    if (i < _pods.Count && _pods[i].What == frame.Line[i]) continue;

                    _pods.Insert(i, Make(frame.Line[i], frame.Head + i));
                    break;
                }
                n = Mathf.Min(_pods.Count, frame.Line.Length);
            }

            for (int i = 0; i < n; i++)
            {
                var pod = _pods[i];
                pod.Slot = frame.Head + i;
                pod.What = frame.Line[i];

                if (pod.Node == null) continue;

                Vector2 to = At(pod.Slot);
                if ((pod.Node.anchoredPosition - to).sqrMagnitude < .01f) continue;

                Tween.Move(pod.Node, to, seconds, Ease.OutCubic);
            }
        }

        Pod PodAt(int slot)
        {
            for (int i = 0; i < _pods.Count; i++)
                if (_pods[i].Slot == slot) return _pods[i];
            return null;
        }

        // ------------------------------------------------------------------ the voice
        /// <summary>
        /// One story beat per shot at most, chosen by what the shot was worth.
        ///
        /// Ordered rather than raised for everything that happened, because a board that frees
        /// two critters and scraps a warden in one chain would otherwise say three sentences on
        /// top of each other — which is how a story stops being read (invariant 30d).
        /// </summary>
        void Voice(MarchShot log)
        {
            if (Say == null) return;

            if (Run.Board.IsFinished) { Say(StoryCue.Won); return; }

            if (log.Freed > 0) { Say(StoryCue.Freed); return; }
            if (log.Scrapped > 0) { Say(StoryCue.Kill); return; }
            if (log.Waves > 1) { Say(StoryCue.Fired); return; }

            if (log.Jammed || (Run.Budget.Bounded &&
                               Run.Budget.Left <= Run.Board.GoalsLeft))
                Say(StoryCue.Tight);
        }

        // ------------------------------------------------------------------ the endings
        protected override IEnumerator Triumph()
        {
            // The gate collapses rather than the board merely celebrating: what the player did
            // was stop the convoy, so the thing that was threatening them is the thing that has
            // to visibly stop.
            if (_portal != null)
            {
                Tween.KillAll(_portal);
                Shockwave(_portal.rectTransform.anchoredPosition, Pal.Foxglove, 5f, .6f);
                Tween.Scale(_portal.transform, .05f, .5f, Ease.InBack);
                Audio.Sfx("shatter", .8f, .8f);
            }

            yield return new WaitForSeconds(.24f);
            yield return base.Triumph();
        }

        protected override IEnumerator Ruin()
        {
            if (_portal != null)
            {
                Tween.KillAll(_portal);
                Tween.Scale(_portal.transform, 1.6f, .5f, Ease.OutQuad);
                Audio.Sfx("whoosh", .7f, .62f);
            }

            yield return base.Ruin();
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>The first run the opening core matches, which is what the verb lesson rings.</summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null || _layout == null) return -1;

                _board.Moves(_moves);
                for (int i = 0; i < _moves.Count; i++)
                {
                    if (_moves[i].Aim == MarchAim.Dump) continue;
                    return _layout.CellOf(_board.SlotOf(_moves[i].At));
                }

                return -1;
            }
        }

        /// <summary>The launcher, which is where a Spark arrives and where the critters run to.</summary>
        public override int FriendCell
        {
            get
            {
                var path = _layout != null ? _layout.Path : null;
                return path == null || path.Length == 0 ? -1 : path[path.Length - 1];
            }
        }
    }
}
