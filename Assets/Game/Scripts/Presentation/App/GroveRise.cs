using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The ceremony a bought region arrives with: the plot is surveyed, the outline is struck,
    /// and the ground rises out of the grove the player already had.
    ///
    /// <para>
    /// <b>Land is the one purchase in this game that is not an object.</b> A companion has a
    /// reveal, a home rung changes the house, a bench is something you then place — every one
    /// of those is a thing the player can look at afterwards and see that they own. A region
    /// is <em>ground</em>, and the version that shipped first had it appear while the player
    /// was still standing in the shop: they walked back to the Grovement and the floor was
    /// simply bigger. The most expensive thing on sale was the only one nobody watched arrive.
    /// </para>
    /// <para>
    /// So the purchase now takes the player back to their grove and plays this. The order the
    /// tiles land in is <see cref="GroveGrowth"/> — in Domain, and tested, because a wave is
    /// arithmetic and arithmetic about motion is the one kind of mistake a screenshot cannot
    /// show. What is here is only the drawing of it.
    /// </para>
    /// <para>
    /// <b>Every effect is generated rather than addressed</b>, which is <c>Art.Bloom</c>'s rule
    /// and unusually load-bearing here: this runs on a screen the player has just been
    /// navigated to, so its art scope is opening in the same breath, and an <c>Image</c> whose
    /// sprite has not arrived is a white rectangle rather than a blank (invariant 7b). A
    /// celebration that can look like a failed load is not one worth firing.
    /// </para>
    /// <para>
    /// <b>It is skippable from the first frame</b>, for <c>CompanionRevealOverlay</c>'s reason.
    /// The latch that stops a stray drag panning the floor mid-ceremony is the same object
    /// that takes the tap to end it, so there is no state of this in which the screen is both
    /// frozen and unanswerable.
    /// </para>
    /// </summary>
    public sealed class GroveRise
    {
        // ------------------------------------------------------------------ timing
        /// <summary>How long the surveyed plot takes to draw itself in.</summary>
        const float SurveySeconds = .55f;

        /// <summary>When the outline is struck — the beat that says the plot is now yours.</summary>
        const float StampAt = .60f;

        /// <summary>When the first ring of ground begins to move.</summary>
        const float RiseAt = .80f;

        /// <summary>How long the finished ground is held before the camera settles.</summary>
        const float HoldSeconds = 1.05f;

        /// <summary>The closing camera move, from the framing shot to where the player is left.</summary>
        const float SettleSeconds = .80f;

        // ------------------------------------------------------------------ shape
        /// <summary>How far below its place a tile starts, in floor pixels.</summary>
        public const float Lift = 74f;

        /// <summary>What a tile is scaled to at the bottom of its rise.</summary>
        public const float RiseFrom = .62f;

        /// <summary>Thickness of the plot outline, in floor pixels.</summary>
        const float EdgeThickness = 11f;

        /// <summary>Resting alpha of a surveyed tile, before its real ground arrives.</summary>
        const float PlotAlpha = .22f;

        /// <summary>
        /// How much of the display's width the framing shot gives the new region.
        ///
        /// The rest is the grove around it, which is the whole point of the shot: a region
        /// framed edge to edge is a rectangle, and a region framed with the old floor beside it
        /// is somewhere the player's grove is about to reach.
        /// </summary>
        const float FrameFill = .82f;

        /// <summary>How far back the camera opens, and how far in it settles, either side of the framing fit.</summary>
        const float OpenBack = .86f, SettleIn = 1.06f;

        /// <summary>
        /// Tightest the framing shot may be. A small region would otherwise be framed so close
        /// that the ground arriving has nothing to arrive next to.
        /// </summary>
        const float MaxFrameZoom = .70f;

        // ------------------------------------------------------------------ state
        readonly GroveFieldView _field;
        readonly GroveFloor _floor;
        readonly GroveRegion _region;
        readonly Action _done;

        readonly HashSet<long> _planted = new HashSet<long>();
        readonly HashSet<long> _arriving = new HashSet<long>();
        readonly Dictionary<long, Image> _plots = new Dictionary<long, Image>();
        readonly List<Image> _edges = new List<Image>();
        readonly List<float> _edgeLength = new List<float>();

        int[] _rings;
        int _ringCount;
        List<int>[] _byRing;

        RectTransform _host, _layer;

        float _lookCol, _lookRow, _centreCol, _centreRow;
        float _openZoom, _fitZoom, _restZoom;
        bool _begun, _finished;

        GroveRise(GroveFieldView field, GroveFloor floor, GroveRegion region, Action done)
        {
            _field = field;
            _floor = floor;
            _region = region;
            _done = done;
        }

        /// <summary>
        /// Frames the new ground and lays the plot out, without moving anything yet. Null —
        /// and <paramref name="done"/> straight away — when there is nothing to celebrate, so
        /// a caller never has to decide whether a ceremony happened.
        ///
        /// <para>
        /// <b>Split from <see cref="Begin"/> because the iris has to open on the framing
        /// shot.</b> A screen builds itself behind the closed transition, so a ceremony that
        /// framed its camera when it started would have the grove appear centred on the hall
        /// and then jump — and one that ran its first beats there would spend them where
        /// nobody could see. Preparing early and starting on <c>OnPresented</c> is the same
        /// bargain <c>View.Ready</c> makes for the map, in the one other place a screen is not
        /// finished when it is built.
        /// </para>
        /// </summary>
        public static GroveRise Play(RectTransform chrome, GroveFieldView field, GroveFloor floor,
                                     GroveRegion region, Func<int, int, bool> ownedBefore, Action done)
        {
            if (chrome == null || field == null || floor == null
                || region == null || !region.IsValid || region.TileCount <= 0)
            {
                done?.Invoke();
                return null;
            }

            var rise = new GroveRise(field, floor, region, done);
            rise.Prepare(chrome, ownedBefore);
            return rise;
        }

        /// <summary>
        /// Starts the sequence. Safe to call more than once and after the ceremony has been
        /// skipped, because both of those are ordinary: the catalog and the transition each
        /// finish in whichever order they finish, and this is attempted from both ends.
        /// </summary>
        public void Begin()
        {
            if (_begun || _finished) return;
            _begun = true;

            Survey();
            Sequence();
        }

        /// <summary>
        /// Whether this tile is ground that has been bought but has not landed yet, and must
        /// therefore not be drawn.
        ///
        /// <para>
        /// Asked of every tile the field considers, so it answers for the region alone and
        /// leaves the rest of the floor to <c>GroveLand</c> — the screen's predicate is an
        /// <c>and</c> of the two, which is what keeps "do I own this" and "has it arrived yet"
        /// from becoming one muddled question.
        /// </para>
        /// </summary>
        public bool Hides(int col, int row)
            => !_finished && _region.Holds(col, row) && !_planted.Contains(Key(col, row));

        /// <summary>
        /// True exactly once for a tile, on the bind that first draws it — the signal a cell
        /// uses to arrive rather than appear.
        ///
        /// <para>
        /// Consumed rather than merely read, because a cell is rebound for reasons that have
        /// nothing to do with this: a repaint, a pan, art landing. A flag that stayed true
        /// would replay the rise every time one of those happened, which on a screen the
        /// player is holding still is a floor that will not stop twitching.
        /// </para>
        /// </summary>
        public bool TakeArrival(int col, int row) => _arriving.Remove(Key(col, row));

        /// <summary>
        /// Whether this ceremony is staged against that floor.
        ///
        /// <para>
        /// For the screen's reload, which runs at least twice in the ordinary case — the
        /// catalog raises its event and <c>Warm</c> calls it directly — and throws every tile
        /// away when it does. Asked by reference rather than by value because a floor is
        /// immutable and republished whole, so identity is exactly the question: the same
        /// object is the same ground, and a different one is ground this ceremony is no longer
        /// about.
        /// </para>
        /// </summary>
        public bool Stages(GroveFloor floor) => ReferenceEquals(_floor, floor);

        // ------------------------------------------------------------------ setup
        void Prepare(RectTransform chrome, Func<int, int, bool> ownedBefore)
        {
            GroveFloor.TryParse(_floor.HallTile, out int hallCol, out int hallRow);

            _rings = GroveGrowth.Rings(_region, ownedBefore, hallCol, hallRow);
            _ringCount = GroveGrowth.RingCount(_rings);

            _byRing = new List<int>[Mathf.Max(1, _ringCount)];
            for (int i = 0; i < _rings.Length; i++)
            {
                int ring = Mathf.Clamp(_rings[i], 0, _byRing.Length - 1);
                (_byRing[ring] ?? (_byRing[ring] = new List<int>())).Add(i);
            }

            Frame();

            _layer = _field.Layer("Rise");
            _host = UIKit.Node("RiseChrome", chrome);

            // The floor stops taking input, and the same gesture that would have panned it ends
            // the ceremony instead. Two mechanisms because there are two kinds of input: the
            // field latches itself against the pinch it polls for (see GroveFieldView.Locked),
            // and an invisible sheet over the screen catches the tap — which is also what puts
            // the header's buttons out of reach for the few seconds this owns the screen.
            // Silent, because the tap is a dismissal rather than a button press.
            _field.Locked = true;
            UIKit.Scrim(_host, 0f, Skip);

            BuildPlot();
        }

        /// <summary>
        /// Where the camera opens, and how far back.
        ///
        /// <para>
        /// The zoom is <em>derived from the region</em> rather than typed, for the reason every
        /// measured thing in this project is: a drop that sells a bigger stretch of ground
        /// should frame it, and a number tuned against the shipped 6x4 would quietly crop a
        /// 10x10. The shot is biased back towards the hall so the old grove is in it — ground
        /// growing out of somewhere is the whole picture, and a region alone in frame is a
        /// rectangle.
        /// </para>
        /// </summary>
        void Frame()
        {
            _centreCol = _region.Col + (_region.Cols - 1) * .5f;
            _centreRow = _region.Row + (_region.Rows - 1) * .5f;

            GroveFloor.TryParse(_floor.HallTile, out int hallCol, out int hallRow);

            const float TowardHall = .22f;
            _lookCol = Mathf.Lerp(_centreCol, hallCol, TowardHall);
            _lookRow = Mathf.Lerp(_centreRow, hallRow, TowardHall);

            // A rectangle of tiles is a diamond on screen, so its width is the sum of its two
            // sides rather than either of them.
            float span = (_region.Cols + _region.Rows) * GroveFloor.TileWidth * .5f;

            _fitZoom = Mathf.Clamp(Flow.Size.x * FrameFill / Mathf.Max(1f, span),
                                   GroveFieldView.MinZoom, MaxFrameZoom);

            _openZoom = Mathf.Clamp(_fitZoom * OpenBack, GroveFieldView.MinZoom, GroveFieldView.MaxZoom);
            _restZoom = Mathf.Clamp(_fitZoom * SettleIn, GroveFieldView.MinZoom, GroveFieldView.MaxZoom);

            _field.ZoomTo(_openZoom);
            _field.CentreOn(_lookCol, _lookRow);
        }

        // ------------------------------------------------------------------- plot
        /// <summary>
        /// The surveyed plot: one faint diamond per tile and a struck outline around the lot.
        ///
        /// <para>
        /// This is the half of the ceremony that carries the <em>information</em> — it says
        /// exactly how much ground was bought and where it is, before a single tile of it
        /// exists. The rise that follows is the reward; without the survey it would be a
        /// surprise, and a player who is surprised by what they paid for cannot enjoy it.
        /// </para>
        /// </summary>
        void BuildPlot()
        {
            var tile = Art.IsoTile(160, 2.5f);

            for (int r = 0; r < _region.Rows; r++)
                for (int c = 0; c < _region.Cols; c++)
                {
                    int col = _region.Col + c, row = _region.Row + r;

                    var plot = UIKit.Img("Plot", _layer, tile, Pal.A(Pal.Gold, 0f),
                                         new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                         new Vector2(.5f, .5f), At(col, row));

                    _plots[Key(col, row)] = plot;
                }

            BuildEdges();
        }

        /// <summary>
        /// The plot draws itself in: every tile of it, then a line round the outside.
        ///
        /// <para>
        /// Staggered by depth so the survey sweeps the way the ground later rises. Two motions
        /// crossing one plot in opposite directions read as two unrelated effects rather than
        /// as one idea stated twice.
        /// </para>
        /// </summary>
        void Survey()
        {
            for (int r = 0; r < _region.Rows; r++)
                for (int c = 0; c < _region.Cols; c++)
                {
                    if (!_plots.TryGetValue(Key(_region.Col + c, _region.Row + r), out var plot)) continue;
                    if (!plot) continue;

                    float lead = SurveySeconds * .45f
                               * (_ringCount <= 1 ? 0f : _rings[r * _region.Cols + c] / (_ringCount - 1f));

                    Tween.Fade(plot, PlotAlpha, SurveySeconds * .55f).Delay(lead);
                }

            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                if (!edge) continue;

                var rt = (RectTransform)edge.transform;
                float length = _edgeLength[i];

                Tween.Run(SurveySeconds * .62f, Ease.OutCubic,
                          t => { if (rt) rt.sizeDelta = new Vector2(EdgeThickness, length * t); }, edge)
                     .Delay(i * SurveySeconds * .12f);
            }
        }

        /// <summary>
        /// The four sides of the lot, each drawn from one corner to the next.
        ///
        /// <para>
        /// Measured from the region's own corners rather than laid out by angle, so the
        /// outline agrees with the floor by construction — the tile grid is not 2:1 (see
        /// <c>GroveFloor.TileFaceRatio</c>) and a rhombus drawn at the angle everyone assumes
        /// isometric means would miss the ground by several pixels at every corner.
        /// </para>
        /// </summary>
        void BuildEdges()
        {
            float c0 = _region.Col - .5f, c1 = _region.Col + _region.Cols - .5f;
            float r0 = _region.Row - .5f, r1 = _region.Row + _region.Rows - .5f;

            Vector2[] corner =
            {
                At(c0, r0), At(c1, r0), At(c1, r1), At(c0, r1)
            };

            var capsule = Art.Capsule(24, 96);

            for (int i = 0; i < 4; i++)
            {
                var a = corner[i];
                var b = corner[(i + 1) & 3];

                var span = b - a;
                float length = span.magnitude;

                var edge = UIKit.Img("Edge", _layer, capsule, Pal.A(Pal.Radiance, .92f),
                                     new Vector2(EdgeThickness, 0f), new Vector2(.5f, .5f), a);

                var rt = (RectTransform)edge.transform;

                // Pivoted at its foot so it grows away from the corner it starts at, and turned
                // so its own length axis — the capsule is drawn vertically — lies along the
                // edge.
                rt.pivot = new Vector2(.5f, 0f);
                rt.anchoredPosition = a;
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f);

                _edges.Add(edge);
                _edgeLength.Add(length);
            }
        }

        // --------------------------------------------------------------- sequence
        void Sequence()
        {
            var cue = new Cue(_host.gameObject);

            cue.With(() => Audio.Sfx("chime", .45f, .92f));

            // The strike. Everything before this is a plan drawn on the ground; this is the
            // moment it becomes the player's, so it is the loudest single beat in the sequence
            // and the only one before the end that is worth a haptic.
            cue.Then(StampAt, Strike);

            float riseFor = GroveGrowth.Spread(_ringCount);

            cue.Then(RiseAt - StampAt, () =>
            {
                // A slow push in under the whole wave. The ground arriving is the subject, and
                // a camera that closes on its subject while it happens is the difference
                // between watching something and being shown it.
                Tween.Run(riseFor + SettleSeconds * .4f, Ease.InOutSine,
                          t => { if (!_finished) _field.ZoomTo(Mathf.Lerp(_openZoom, _fitZoom, t)); },
                          _host.gameObject);
            });

            for (int ring = 0; ring < _ringCount; ring++)
            {
                int at = ring;
                Tween.After(RiseAt + GroveGrowth.DelayOf(at, _ringCount),
                            () => Plant(at), _host.gameObject);
            }

            float bloomAt = RiseAt + riseFor;

            cue.Wait(bloomAt - cue.Playhead).With(Bloom);
            cue.Then(.18f, Banner);
            cue.Then(HoldSeconds, Settle);
            cue.Then(SettleSeconds, Finish);
        }

        /// <summary>The outline flashes, a wave leaves the lot, and the plot is claimed.</summary>
        void Strike()
        {
            if (_finished) return;

            Audio.Sfx("unlock", .7f);
            Haptic.Tap();

            foreach (var edge in _edges)
            {
                if (!edge) continue;

                var rt = (RectTransform)edge.transform;
                float thick = rt.sizeDelta.x;

                Tween.Run(.34f, Ease.OutQuint, t =>
                {
                    if (!rt) return;
                    rt.sizeDelta = new Vector2(Mathf.LerpUnclamped(thick * 2.1f, thick, t), rt.sizeDelta.y);
                }, edge);
            }

            foreach (var plot in _plots.Values)
                if (plot) Tween.Fade(plot, PlotAlpha * 2.2f, .12f)
                               .OnDone(() => { if (plot) Tween.Fade(plot, PlotAlpha, .26f); });

            Shockwave(0f, Pal.Radiance, 2.4f);
            Shockwave(.13f, Pal.Gold, 3.1f);
        }

        /// <summary>One ring of ground lands.</summary>
        void Plant(int ring)
        {
            if (_finished || ring < 0 || ring >= _byRing.Length) return;

            var tiles = _byRing[ring];
            if (tiles == null) return;

            foreach (int index in tiles)
            {
                int col = _region.Col + index % _region.Cols;
                int row = _region.Row + index / _region.Cols;
                long key = Key(col, row);

                _planted.Add(key);
                _arriving.Add(key);

                Flash(col, row);

                if (_plots.TryGetValue(key, out var plot) && plot)
                    Tween.Fade(plot, 0f, GroveGrowth.RiseSeconds * .7f)
                         .OnDone(() => { if (plot) plot.gameObject.SetActive(false); });
            }

            // The field only re-tests which tiles exist when its window moves, and the window
            // is deliberately still here. See GroveFieldView.Revisit.
            _field.Revisit();

            if (!GroveGrowth.Speaks(ring, _ringCount)) return;

            Audio.Sfx("tock", .46f, GroveGrowth.Pitch(ring, _ringCount));

            int first = tiles[0];
            Burst.Sparks(_layer,
                         At(_region.Col + first % _region.Cols, _region.Row + first / _region.Cols),
                         Pal.Radiance, 8, 130f, 18f, .5f);
        }

        /// <summary>The light a tile catches as it lands. One image, gone in a third of a second.</summary>
        void Flash(int col, int row)
        {
            var img = UIKit.Img("Land", _layer, Art.IsoTile(160, 1.5f), Pal.A(Pal.Radiance, .85f),
                                new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                new Vector2(.5f, .5f), At(col, row));

            var rt = (RectTransform)img.transform;

            Tween.Run(.38f, Ease.OutCubic, t =>
            {
                if (!img) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.94f, 1.16f, t);
                img.color = Pal.A(Pal.Radiance, .85f * (1f - t));
            }, img).Delay(GroveGrowth.RiseSeconds * .55f)
                   .OnDone(() => { if (img) UnityEngine.Object.Destroy(img.gameObject); });
        }

        void Shockwave(float delay, Color colour, float to)
        {
            var ring = UIKit.Img("Wave", _layer, Art.Ring(256, 9f), Pal.A(colour, 0f),
                                 new Vector2(GroveFloor.TileWidth * 1.6f, GroveFloor.TileWidth * 1.6f),
                                 new Vector2(.5f, .5f), At(_centreCol, _centreRow));

            Tween.Run(.72f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.25f, to, t);
                ring.color = Pal.A(colour, .85f * (1f - t));
            }, ring).Delay(delay)
                    .OnDone(() => { if (ring) UnityEngine.Object.Destroy(ring.gameObject); });
        }

        /// <summary>The last tile has settled: the whole lot takes the light at once.</summary>
        void Bloom()
        {
            if (_finished) return;

            Audio.Sfx("lit", .62f);
            Haptic.Tap();

            float span = (_region.Cols + _region.Rows) * GroveFloor.TileWidth * .5f;

            var glow = UIKit.Img("Bloom", _layer, Art.Glow(256, 1.9f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(span, span * .62f), new Vector2(.5f, .5f),
                                 At(_centreCol, _centreRow));

            Tween.Run(.90f, Ease.OutQuad, t =>
            {
                if (!glow) return;
                glow.transform.localScale = Vector3.one * Mathf.Lerp(.55f, 1.25f, t);
                glow.color = Pal.A(Pal.Sun, .52f * Mathf.Sin(t * Mathf.PI));
            }, glow).OnDone(() => { if (glow) UnityEngine.Object.Destroy(glow.gameObject); });

            Burst.Sparks(_layer, At(_centreCol, _centreRow), Pal.Verdant, 24, 330f, 34f, .95f);

            foreach (var edge in _edges)
                if (edge) Tween.Fade(edge, 0f, .55f);
        }

        /// <summary>
        /// What was bought, said in words.
        ///
        /// <para>
        /// In the safe layer rather than over the field, because it is chrome and a camera
        /// cutout eats chrome — the fault this screen was reported for. Above centre, so the
        /// ground it is naming is not underneath it.
        /// </para>
        /// </summary>
        void Banner()
        {
            if (_finished) return;

            Audio.Sfx("pop", .55f);

            var safe = SafeArea.Node("Safe", _host);
            var host = UIKit.Box("Claim", safe, new Vector2(880f, 300f), new Vector2(.5f, .5f),
                                 new Vector2(0f, 380f));

            UIKit.Shrinkable(
                UIKit.Titled("Caption", host, Loc.Get("ui.land.claimed").ToUpperInvariant(), 30,
                             Pal.A(Pal.Gold, .95f), TextAnchor.MiddleCenter,
                             new Vector2(760f, 40f), new Vector2(.5f, .5f), new Vector2(0f, 104f), 3f, 3f), 20);

            var ribbon = UIKit.Img("Ribbon", host, Art.S("Ui/ribbon_green"), Color.white,
                                   new Vector2(720f, 168f), new Vector2(.5f, .5f), Vector2.zero);
            ribbon.transform.localRotation = Quaternion.Euler(0f, 0f, -1.6f);

            UIKit.Shrinkable(
                UIKit.Titled("Name", ribbon.transform, Loc.Get(_region.NameKey), 54, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(560f, 74f), new Vector2(.5f, .5f),
                             Vector2.zero, 4f, 4f), 30);

            UIKit.Shrinkable(
                UIKit.Titled("Size", host, Loc.Format("ui.land.size", _region.Cols, _region.Rows), 30,
                             new Color(1f, .96f, .88f, .80f), TextAnchor.MiddleCenter,
                             new Vector2(760f, 40f), new Vector2(.5f, .5f), new Vector2(0f, -108f), 3f, 3f), 20);

            Sheen.Attach((RectTransform)ribbon.transform, 2.6f);

            host.localScale = Vector3.zero;
            Tween.Pop(host, 0f, .52f);
            Tween.Punch(ribbon.transform, .10f, .42f);

            // Floats up and out on its own. A banner that had to be dismissed would be one more
            // tap between the player and the grove they just made bigger.
            Tween.Move(host, new Vector2(0f, 452f), .70f, Ease.OutCubic)
                 .Delay(HoldSeconds + SettleSeconds * .35f);
            Tween.Fade(UIKit.Group(host), 0f, .55f)
                 .Delay(HoldSeconds + SettleSeconds * .45f);
        }

        /// <summary>The camera closes on the new ground and the survey marks go out.</summary>
        void Settle()
        {
            if (_finished) return;

            float fromCol = _lookCol, fromRow = _lookRow, fromZoom = _fitZoom;

            Tween.Run(SettleSeconds, Ease.InOutSine, t =>
            {
                if (_finished) return;
                _field.ZoomTo(Mathf.Lerp(fromZoom, _restZoom, t));
                _field.CentreOn(Mathf.Lerp(fromCol, _centreCol, t), Mathf.Lerp(fromRow, _centreRow, t));
            }, _host.gameObject);

            foreach (var plot in _plots.Values)
                if (plot) Tween.Fade(plot, 0f, .35f);
        }

        // ------------------------------------------------------------------- end
        /// <summary>
        /// Ends the ceremony wherever it had got to: every tile planted, the camera where it
        /// would have finished, and the screen handed back.
        ///
        /// <para>
        /// One method for both the tap and the last beat, which is the rule this project keeps
        /// relearning — <c>AdOfferOverlay</c>'s dismissal, the pause menu's latch, the grove
        /// screens' art scope. A ceremony with two ways out reports through neither reliably,
        /// so the safe outcome has to be what <em>every</em> exit does.
        /// </para>
        /// </summary>
        /// <summary>Ends it early — a tap, or the back key. Safe at any point, including before it started.</summary>
        public void Skip() => Close(true);

        void Finish() => Close(false);

        void Close(bool abrupt)
        {
            if (_finished) return;
            _finished = true;

            _field.Locked = false;

            // Nothing is left hidden. Hides answers false from here on, so the screen's own
            // predicate is the only thing deciding what is drawn — which is the state it has to
            // be handed back in whether the sequence ran or was cut short.
            _arriving.Clear();

            if (abrupt)
            {
                _field.ZoomTo(_restZoom);
                _field.CentreOn(_centreCol, _centreRow);
            }

            _field.Revisit();

            if (_layer) UnityEngine.Object.Destroy(_layer.gameObject);
            if (_host) UnityEngine.Object.Destroy(_host.gameObject);

            _done?.Invoke();
        }

        // ------------------------------------------------------------------ util
        /// <summary>A point on the floor in the field's own space. Y is negated there.</summary>
        static Vector2 At(float col, float row)
            => new Vector2(GroveFloor.TileX(col, row), -GroveFloor.TileY(col, row));

        static long Key(int col, int row) => ((long)col << 32) | (uint)row;
    }
}
