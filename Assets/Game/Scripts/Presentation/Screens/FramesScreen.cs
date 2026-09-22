using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Frames;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The frames a player can wear round their name, reached from the profile's keeper card.
    ///
    /// <para>
    /// <b>Two things on the page and both are the same picture.</b> A stage at the top stands
    /// the worn frame at full width with the player's own name inside it — which is the only
    /// honest preview of a thing whose whole job is to sit round a name — and a card per frame
    /// under it carrying the painting, its name and one key: <em>wear</em>, or <em>take off</em>
    /// for the one being worn. A frame not held would carry its price here, and that is the
    /// whole of what changes when frames go on sale (see <see cref="FrameLedger"/>).
    /// </para>
    /// <para>
    /// <b>The paintings are a scope of this screen</b> (7b): all of them are held while it
    /// stands, the stage and the cards repaint when they land, and the hold goes with the
    /// screen. The profile and the boards each hold only the worn one.
    /// </para>
    /// </summary>
    public sealed class FramesScreen : View
    {
        /// <summary>
        /// Whether the profile offers the way in. Switched off for the build submitted to Apple
        /// on 2026-09-22 and on again the same day, at the owner's instruction. <b>Off, it hides
        /// the profile's key and takes the frame off every board row</b> — a worn frame draws
        /// nowhere — so this is the one switch for the whole feature; nothing else in the game
        /// reaches this screen.
        /// </summary>
        public const bool Offered = true;

        public override string Track => "mus_menu";

        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float CardWidth = 980f;
        const float Gap = 24f;

        // The stage: the frame at nearly the card's width, its hole holding the name.
        const float StageH = 420f;
        const float StageFrameW = 900f;

        // A card: the painting small on the left, the name and the key on the right.
        const float CardH = 210f;
        const float ThumbW = 390f;
        const float KeyW = 260f, KeyH = 92f;

        AssetHold _art;
        NameFrame _stage;
        Text _stageName;
        RectTransform _stack;

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            BuildHeader();
            BuildPage();
            NavBar.Build(Content, NavBar.Tab.Profile);

            FrameLedger.Changed += Repaint;
            HoldArt();
        }

        void OnDestroy()
        {
            FrameLedger.Changed -= Repaint;
            _art?.Dispose();
            _art = null;
        }

        void BuildHeader()
        {
            float cy = -(22f + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<ProfileScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.frames.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), new Vector2(.5f, 1f),
                                             new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
        }

        /// <summary>
        /// The page below the banner. Redrawn whole on a change, through <see cref="View.ClearContent"/>'s
        /// rule read one level down: the stack is emptied by destroying it and building again,
        /// never by reaching into its children (44k).
        /// </summary>
        void BuildPage()
        {
            if (_stack != null) Destroy(_stack.gameObject);

            _stack = UIKit.Node("Stack", Safe);
            float cursor = -(22f + BannerH + Gap);

            cursor = BuildStage(cursor);
            foreach (var frame in FrameCatalog.All)
                cursor = BuildCard(frame, cursor);
        }

        float BuildStage(float top)
        {
            var worn = FrameLedger.Worn;

            var plate = UIKit.Img("Stage", _stack, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                  new Vector2(CardWidth, StageH), new Vector2(.5f, 1f),
                                  new Vector2(0f, top - StageH * .5f));

            var size = new Vector2(StageFrameW, StageFrameW / 3f);
            _stage = NameFrame.Build("Frame", plate.transform, size, new Vector2(.5f, .5f), new Vector2(0f, 12f));
            _stage.Show(worn);

            // The name sits in the frame's hole when there is one, and in the middle of the
            // plate when there is not — which is what the page looks like with nothing worn.
            var hole = worn != null ? NameFrame.HoleIn(worn, size) : new Rect(-300f, -40f, 600f, 80f);
            _stageName = UIKit.Shrinkable(
                UIKit.Titled("Name", _stage.transform, Profile.Name, 46, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(hole.width - 24f, hole.height), new Vector2(.5f, .5f),
                             hole.center, 4f, 3f), 20);

            if (worn == null)
            {
                UIKit.Shrinkable(
                    UIKit.Titled("Hint", plate.transform, Loc.Get("ui.frames.none"), 26,
                                 Pal.A(Pal.Cream, .62f), TextAnchor.MiddleCenter,
                                 new Vector2(760f, 60f), new Vector2(.5f, .5f), new Vector2(0f, -140f),
                                 3f, 0f, wrap: true), 18);
            }

            Tween.Pop(plate.transform, 0f, .55f, .08f);
            return top - StageH - Gap;
        }

        float BuildCard(FrameDefinition frame, float top)
        {
            bool worn = FrameLedger.Worn == frame;

            var card = UIKit.Img("Card_" + frame.Id, _stack, Art.S("Ui/" + (worn ? Skins.PlateOrange : Skins.PlateBlue)),
                                 Color.white, new Vector2(CardWidth, CardH), new Vector2(.5f, 1f),
                                 new Vector2(0f, top - CardH * .5f));

            // The painting, still, at a third of the card. A second living frame here would be
            // a second skin for a picture the stage above already animates.
            var thumb = UIKit.Img("Thumb", card.transform, AssetLibrary.Peek<Sprite>(frame.Address), Color.white,
                                  new Vector2(ThumbW, ThumbW / 3f), new Vector2(0f, .5f),
                                  new Vector2(26f + ThumbW * .5f, 0f));
            thumb.preserveAspect = true;
            thumb.enabled = thumb.sprite != null;   // an Image with no sprite is a white rectangle (7b)

            UIKit.Shrinkable(
                UIKit.Titled("Name", card.transform, Loc.Get(frame.NameKey), 30, Pal.Gold, TextAnchor.MiddleLeft,
                             new Vector2(CardWidth - ThumbW - KeyW - 100f, 44f), new Vector2(0f, .5f),
                             new Vector2(ThumbW + 52f + (CardWidth - ThumbW - KeyW - 100f) * .5f, 0f),
                             3f, 2f), 18);

            var key = UIKit.TextButton("Key", card.transform, worn ? Skins.Shut : Skins.Affirm,
                                       Loc.Get(worn ? "ui.frames.take_off" : "ui.frames.wear"), 28,
                                       new Vector2(KeyW, KeyH), new Vector2(1f, .5f),
                                       new Vector2(-26f - KeyW * .5f, 0f),
                                       () => Toggle(frame));
            UIKit.Shrinkable(key.Label, 18);
            UIKit.FitLabel(key);

            Tween.Pop(card.transform, 0f, .55f, .15f);
            return top - CardH - Gap;
        }

        void Toggle(FrameDefinition frame)
        {
            bool worn = FrameLedger.Worn == frame;
            if (!FrameLedger.Wear(worn ? null : frame.Id)) return;
            Audio.Sfx("chime2", .5f);
            // The ledger's event repaints; nothing more to do here.
        }

        void Repaint()
        {
            if (!Living) return;
            BuildPage();
        }

        void HoldArt() => Run(async token =>
        {
            _art = _art ?? AssetLibrary.Hold("frames");
            var wanted = new System.Collections.Generic.List<AssetRequest>();
            foreach (var frame in FrameCatalog.All)
                wanted.AddRange(AssetManifest.FrameAssets(frame.Id));
            await _art.LoadAsync(wanted, null, token);
            if (!Living) return;
            BuildPage();
        });

        public override bool OnBack() { Flow.Go<ProfileScreen>(); return true; }
    }
}
