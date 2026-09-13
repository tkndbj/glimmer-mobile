using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Dev
{
    /// <summary>
    /// A bench for looking at every turret in the roster, in any of the four ward colours, one
    /// after another.
    ///
    /// <para>
    /// <b>Kept, and unreachable.</b> Nothing in the game navigates here — the door in the loadout
    /// header came out once the shelf grew a preview panel of its own
    /// (<c>WardPreviewOverlay</c>), which is the same stage in front of a player rather than in
    /// front of whoever is tuning the art. This survives because judging nineteen effects against
    /// each other is a different job from judging one: the panel shows the turret a player tapped,
    /// and this shows all twenty in a grid with a colour switch.
    /// </para>
    /// <para>
    /// To reach it again, put one line behind a button in <c>LoadoutScreen.BuildHeader</c>:
    /// <c>Flow.Go&lt;Dev.WardDemoScreen&gt;()</c>.
    /// </para>
    /// <para>
    /// <b>It draws through <see cref="WardFiringStage"/></b>, which is the widget the shipping
    /// panel draws through — a bench with its own copy of the anchors, sizes and timings would be a
    /// bench answering a question nobody asked.
    /// </para>
    /// </summary>
    public sealed class WardDemoScreen : View
    {
        public override string Track => "mus_menu";

        // ----------------------------------------------------------------- layout
        const float HeaderHeight = 190f;
        const float CaptionHeight = 100f;
        const float StageHeight = 760f;
        const float SwatchHeight = 122f;

        const int Columns = 4;
        const float CellW = 250f, CellH = 232f;

        /// <summary>A real board's cell, so what is judged here is the size that ships.</summary>
        const float Cell = 128f;

        /// <summary>Its own scope, so the bench cannot disturb a line a run is standing.</summary>
        const string DemoScope = "ward_demo";

        // ----------------------------------------------------------------- state
        WardModel _model;
        int _colour;

        RectTransform _grid, _viewport, _swatches;
        WardFiringStage _stage;
        Text _caption, _note;

        readonly List<Btn> _cells = new List<Btn>();
        readonly List<Btn> _chips = new List<Btn>();

        // **Asked of the board rather than written out again.** This bench exists to judge a
        // turret's effect against the colour it will really wear, so a second table of the four
        // is a second answer to the question invariant 37f settles by there being exactly one —
        // and a bench that flatters a bolt with a colour the board does not paint is worse than
        // no bench. `SiegeView.TintOf` is public for exactly this and for `WardFiringStage`.
        Color Tint => SiegeView.TintOf(Mathf.Clamp(_colour, 0, WardLine.Colours.Length - 1));

        // ----------------------------------------------------------------- build
        protected override void Build()
        {
            Scenery.Plain(Content);

            _model = WardLedger.Catalog.Starter;

            BuildHeader();
            BuildStage();
            BuildSwatches();
            BuildGrid();

            Paint();
            Browse();

            if (_stage != null) _stage.Show(_model, _colour);
        }

        AssetHold _shelfArt;

        void OnDestroy() => _shelfArt?.Dispose();

        public override bool OnBack() { Flow.Go<LoadoutScreen>(); return true; }

        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64),
                                 new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, HeaderHeight);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);
            frt.SetAsFirstSibling();

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -100f),
                             () => Flow.Go<LoadoutScreen>());

            UIKit.Titled("Title", Safe, "TURRET VFX", 40, Pal.Cream, TextAnchor.MiddleCenter,
                         new Vector2(560f, 90f), new Vector2(.5f, 1f), new Vector2(0f, -100f),
                         4f, 5f);
        }

        void BuildStage()
        {
            // **Above the stage rather than on it.** An impact is drawn 3.2 cells across, which at
            // this cell is 410 units — anchored on the target it reached the top of the stage and
            // drew straight through a caption sitting there.
            _caption = UIKit.Titled("Name", Safe, "", 34, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(760f, 52f), new Vector2(.5f, 1f),
                                    new Vector2(0f, -(HeaderHeight + 12f)), 3f, 4f);

            _note = UIKit.Label("Note", Safe, "", 24, new Color(.72f, .78f, .85f),
                                TextAnchor.MiddleCenter, new Vector2(820f, 40f),
                                new Vector2(.5f, 1f), new Vector2(0f, -(HeaderHeight + 62f)));

            var plate = UIKit.Img("Plate", Safe, Art.Round(34), Pal.Board,
                                  new Vector2(1000f, StageHeight - 20f), new Vector2(.5f, 1f),
                                  new Vector2(0f, -(HeaderHeight + CaptionHeight + 10f)));
            plate.raycastTarget = false;

            _stage = WardFiringStage.Attach(Safe, new Vector2(1000f, StageHeight),
                                            new Vector2(.5f, 1f),
                                            new Vector2(0f, -(HeaderHeight + CaptionHeight)),
                                            Cell, DemoScope);
        }

        void BuildSwatches()
        {
            // Half its own height lower than the band it follows: `UIKit.Box` pivots at centre
            // whatever it is anchored to, so a row placed at the foot of the stage is placed with
            // its *middle* there and half of it draws over the stage.
            _swatches = UIKit.Row("Colours", Safe, new Vector2(640f, SwatchHeight),
                                  new Vector2(.5f, 1f),
                                  new Vector2(0f, -(HeaderHeight + CaptionHeight + StageHeight
                                                    + 8f + SwatchHeight * .5f)),
                                  22f);

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                int at = i;
                var chip = UIKit.Button("C" + WardLine.Colours[i], _swatches, Art.Round(26),
                                        new Vector2(112f, 84f), new Vector2(.5f, .5f),
                                        Vector2.zero, () => Choose(at));

                var img = chip.GetComponent<Image>();
                if (img != null) img.color = SiegeView.TintOf(i);

                _chips.Add(chip);
            }
        }

        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, 30f);
            _viewport.offsetMax =
                new Vector2(0f, -(HeaderHeight + CaptionHeight + StageHeight + SwatchHeight + 16f));

            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _grid = UIKit.Node("Grid", _viewport);
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = new Vector2(1f, 1f);
            _grid.pivot = new Vector2(.5f, 1f);
            _grid.anchoredPosition = Vector2.zero;

            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _grid;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;

            var models = WardLedger.Catalog.Models;
            int rows = (models.Count + Columns - 1) / Columns;
            _grid.sizeDelta = new Vector2(0f, rows * CellH + 20f);

            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];

                float x = (i % Columns - (Columns - 1) * .5f) * CellW;
                float y = -(i / Columns) * CellH - CellH * .5f - 10f;

                var cell = UIKit.Button("W" + model.Id, _grid, Art.Round(24),
                                        new Vector2(CellW - 16f, CellH - 16f),
                                        new Vector2(.5f, 1f), new Vector2(x, y),
                                        () => Choose(model));

                var face = cell.GetComponent<Image>();
                if (face != null) face.color = new Color(1f, 1f, 1f, .06f);

                var thumb = UIKit.Img("Icon", cell.transform,
                                      AssetLibrary.Sprite(AssetManifest.WardThumb(model.Id)),
                                      Color.white, Vector2.one * (CellH - 96f),
                                      new Vector2(.5f, 1f), new Vector2(0f, -12f));
                thumb.preserveAspect = true;

                UIKit.Label("Name", cell.transform, Loc.Get(model.NameKey), 22, Pal.Cream,
                            TextAnchor.MiddleCenter, new Vector2(CellW - 30f, 34f),
                            new Vector2(.5f, 0f), new Vector2(0f, 40f));

                UIKit.Label("Ability", cell.transform, WardAbilities.NameOf(model.Ability), 19,
                            new Color(.62f, .70f, .78f), TextAnchor.MiddleCenter,
                            new Vector2(CellW - 30f, 28f), new Vector2(.5f, 0f),
                            new Vector2(0f, 14f));

                _cells.Add(cell);
            }
        }

        // ----------------------------------------------------------------- choosing
        void Choose(WardModel model)
        {
            if (model == null || model == _model) return;

            _model = model;
            Paint();

            if (_stage != null) _stage.Show(_model, _colour);
        }

        void Choose(int colour)
        {
            if (colour == _colour) return;

            _colour = colour;
            Paint();

            if (_stage != null) _stage.Show(_model, _colour);
        }

        void Paint()
        {
            if (_caption != null) _caption.text = Loc.Get(_model.NameKey).ToUpperInvariant();

            if (_note != null)
                _note.text = WardAbilities.NameOf(_model.Ability) + "  ·  " + _model.Id
                    + (_model.OwnShot ? "" : "  ·  a different element on each colour");

            for (int i = 0; i < _chips.Count; i++)
            {
                var img = _chips[i].GetComponent<Image>();
                var tint = SiegeView.TintOf(i);
                if (img != null) img.color = i == _colour ? tint : Pal.A(tint, .34f);
            }

            var models = WardLedger.Catalog.Models;
            for (int i = 0; i < _cells.Count && i < models.Count; i++)
            {
                var face = _cells[i].GetComponent<Image>();
                if (face == null) continue;

                face.color = models[i] == _model
                    ? Pal.A(Pal.Lift(Tint, .3f), .34f)
                    : new Color(1f, 1f, 1f, .06f);
            }
        }

        /// <summary>
        /// The shelf's twenty thumbnails.
        ///
        /// <b><c>async void</c> with the exception caught</b>, which is <c>CompanionArt.Load</c>'s
        /// shape and for its reason: a scope that failed to load must not vanish silently.
        /// </summary>
        void Browse() => Run(async token =>
        {
            _shelfArt = _shelfArt ?? AssetLibrary.Hold("ward_shelf");
            await _shelfArt.LoadAsync(AssetManifest.WardShelfAssets(WardLedger.Catalog.Models),
                                      null, token);

            if (Living) Paint();
        });
    }
}
