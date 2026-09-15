using GlimmerGrove.Localization;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The wall: this build is older than the deployment allows, and the only way on is the
    /// store.
    ///
    /// <para>
    /// <b>It is the one panel in this game with no way out, and every other property follows
    /// from that.</b> No scrim dismissal, no corner cross, no back key, and no second button —
    /// because there is no second answer. Everywhere else this file's rules push the other way
    /// (a panel that cannot end is a game that has stopped; a button that does nothing is a
    /// broken button), and the reason they invert here is that the thing on the other side of
    /// this panel is not a game the player may have: it is a client the deployment has
    /// withdrawn. What replaces "a way out" is that the requirement itself is reversible from
    /// the server — see <see cref="ReleaseGate"/>, where lifting every wall in the world is one
    /// document edit.
    /// </para>
    /// <para>
    /// <b>Being dismissed is not how it ends, and it is not what keeps it up either.</b> A
    /// screen change destroys every modal in the stack, so nothing raised once could survive
    /// the first <c>Flow.Go</c> that happened to run. <see cref="UpdateGate"/> owns the standing
    /// of it, frame by frame, against <see cref="ReleaseGate.IsShut"/> — so the panel is a
    /// <em>drawing of a state</em> rather than an event somebody has to remember to repeat, and
    /// it goes away exactly when the state does.
    /// </para>
    /// <para>
    /// <b>It says one sentence, and the first cut said three.</b> That cut also promised the
    /// player's grove was safe — struck by the owner, and rightly: a wall carrying two
    /// paragraphs reads as a screen that is arguing with somebody, and the reassurance was
    /// answering a worry the panel had created by being long in the first place. A title, a
    /// mark, a line and a key is the whole of it.
    /// </para>
    /// <para>
    /// <b>Its one bought sprite is <c>Ui/ic_update</c>, and everything else is furniture the
    /// game already draws.</b> The parchment, the ribbon and the green pill are on every modal
    /// here; the mark is cut by <c>Tools/make_update_icon.py</c> and registered in the
    /// <em>global</em> set rather than a scope, because a scope has two failure modes and this
    /// is the one screen in the game where an <c>Image</c> with no sprite — a white rectangle,
    /// invariant 7b — would be the last thing a player ever saw of it.
    /// </para>
    /// </summary>
    public sealed class UpdateRequiredOverlay : ModalView
    {
        /// <summary>Above everything. See <see cref="ModalLayer.Blocking"/>.</summary>
        public override int Layer => ModalLayer.Blocking;

        // ------------------------------------------------------------------- geometry
        // Absolute offsets rather than a cursor, on ShopArrivalOverlay's judgement: the rows are
        // fixed and none of them is optional. The total is comfortably inside
        // PanelStack.TallestPanel, which is the shortest canvas this game is drawn on.
        const float PanelW = 860f;
        const float HeadRoom = 165f;
        const float MarkSize = 230f;
        const float ButtonH = 124f;

        /// <summary>
        /// Room for the sentence, and it is one sentence.
        ///
        /// <para>
        /// This was 230 and carried a second paragraph promising the player's grove was safe.
        /// The owner cut it, and the cut is right: a wall with two paragraphs on it reads as a
        /// screen that is arguing, and the reassurance was answering a worry the panel itself
        /// had created by being long. What is left says the one thing the title does not — that
        /// there is a newer *Glimmer Grove*, rather than merely that something is wrong here.
        /// </para>
        /// <para>
        /// Deep enough for three lines all the same, because this is a loc key: German and
        /// Turkish run half again as long as English, and <see cref="UIKit.Shrinkable"/> sets
        /// <c>VerticalWrapMode.Truncate</c> (invariant 19n), so a band cut to the English is a
        /// band that silently drops the end of somebody else's sentence.
        /// </para>
        /// </summary>
        const float BodyH = 132f;

        /// <summary>
        /// Clear air under the button, deeper than a plain margin for
        /// <c>ShopArrivalOverlay.FootRoom</c>'s reason: <c>panel_main</c> carries a 60-unit
        /// nine-sliced rim, so a control seated in the fifties has its foot on the parchment's
        /// lip rather than on its face.
        /// </summary>
        const float FootRoom = 92f;

        /// <summary>
        /// Ink on a light parchment, for <c>ShopArrivalOverlay.Ink</c>'s reason: cream copy on
        /// <c>panel_main</c> is held apart from its ground by its outline alone, and the board's
        /// own accents were reported unreadable on it.
        ///
        /// <para>
        /// The warm accent that stood beside this went with the generated mark it painted. A
        /// colour nothing reads is decoration (invariant 5d), and one left behind next to a
        /// sprite that carries its own palette is worse than decoration — it is an invitation to
        /// tint the sprite back to a hue the artist did not choose.
        /// </para>
        /// </summary>
        static readonly Color Ink = new Color(.36f, .25f, .18f);

        protected override void Build()
        {
            // Every one of these is a **centre**, because UIKit.Box always pivots at centre
            // whatever it is anchored to (invariant 44d). `bodyY` was the band's *top*, which
            // drew the sentence half its own height too high — 89 units of text straight over
            // the mark, reported from a device as exactly that. The others were right, which is
            // what made it survive a reading: the shape of the arithmetic looks uniform.
            float y = HeadRoom;
            float markY = y + MarkSize * .5f;     y += MarkSize + 26f;
            float bodyY = y + BodyH * .5f;        y += BodyH + 26f;
            float buttonY = y + ButtonH * .5f;    y += ButtonH + FootRoom;

            // Opaque enough that the hub behind it stops reading as a place the player is
            // standing. Every other panel here leaves the screen underneath legible because
            // the player is coming back to it; nobody is coming back to this one.
            var panel = MakePanel(new Vector2(PanelW, y), Loc.Get("ui.update.title"),
                                  dismissOnScrim: false);

            UIKit.Halo(panel, Pal.Sun, 700f, .22f, new Vector2(0f, y * .5f - markY));

            BuildMark(panel, markY);

            UIKit.Shrinkable(
                UIKit.Titled("Body", panel, Loc.Get("ui.update.body"), 30, Pal.A(Ink, .92f),
                             // Middle rather than upper, which is what lets the band be deep
                             // enough for a language that wraps to three lines without leaving
                             // English sitting against the mark above a pocket of dead air. A
                             // centred line grows symmetrically, so every language is framed the
                             // same way between the mark and the key.
                             TextAnchor.MiddleCenter, new Vector2(700f, BodyH),
                             new Vector2(.5f, 1f), new Vector2(0f, -bodyY),
                             outline: 0f, shadow: 0f, wrap: true), 21);

            var go = UIKit.TextButton("Update", panel, Skins.Affirm, Loc.Get("ui.update.cta"), 36,
                                      new Vector2(520f, ButtonH), new Vector2(.5f, 1f),
                                      new Vector2(0f, -buttonY), OpenStore);

            UIKit.Shrinkable(go.Label, 24);
        }

        /// <summary>
        /// The gold arrow, pointing down: get this.
        ///
        /// <para>
        /// <b>A bought sprite rather than a generated shape, and the owner rejected the
        /// generated one on sight.</b> What stood here was a ring, a shaft and two rotated
        /// bars — every part of it real and none of it drawn by anybody — which next to a panel
        /// cut from a licensed kit reads as a placeholder. `Tools/make_update_icon.py` cuts
        /// `Ui/ic_update` out of CraftPix's vector map pack and records why that arrow and not
        /// one of the others.
        /// </para>
        /// <para>
        /// It still breathes rather than spinning. A spinner says the game is waiting for
        /// something and it is not: it is waiting for the player to leave. On the unscaled
        /// clock like every animation in this file's chrome, because a modal takes
        /// <c>Time.timeScale</c> to nought (invariant 30h) and a mark that has stopped moving on
        /// a panel that cannot be dismissed reads as a game that has frozen.
        /// </para>
        /// </summary>
        static void BuildMark(RectTransform panel, float markY)
        {
            var mark = UIKit.Img("Mark", panel, Art.S("Ui/ic_update"), Color.white,
                                 Vector2.one * MarkSize, new Vector2(.5f, 1f),
                                 new Vector2(0f, -markY));
            mark.preserveAspect = true;

            var rt = (RectTransform)mark.transform;
            var home = rt.anchoredPosition;
            Tween.Run(1.9f, Ease.Linear, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = home + new Vector2(0f, Mathf.Sin(t * Mathf.PI * 2f) * 9f);
            }, rt, "lift").Loop(-1, false);
        }

        /// <summary>
        /// Leaves for the store, and deliberately does not close first.
        ///
        /// <para>
        /// <c>SettingsOverlay.Link</c> closes before handing a URL to the platform, because on
        /// iOS the browser is a separate app and coming back to a modal nobody dismissed is how
        /// a player ends up tapping Close twice. The opposite is wanted here: a player who comes
        /// back without having updated has to find the wall exactly where they left it. If they
        /// <em>did</em> update, this process is gone and the next one starts on a build that
        /// satisfies the requirement.
        /// </para>
        /// <para>
        /// The URL is checked again at the moment of use. It cannot be unusable — a requirement
        /// with no usable door is never applied, let alone stored — so this is the assertion
        /// that keeps that true rather than a branch anybody expects to take: the alternative to
        /// checking is handing the platform something it answers by doing nothing at all, which
        /// on a device is indistinguishable from a dead button on the one panel that has only
        /// the one.
        /// </para>
        /// </summary>
        static void OpenStore()
        {
            string url = ReleaseGate.StoreUrl;

            if (!Privacy.LegalLinks.Usable(url))
            {
                Debug.LogError($"[Release] refused to open a malformed store link: '{url}'");
                return;
            }

            Application.OpenURL(url);
        }

        /// <summary>
        /// Swallows the hardware key without doing anything.
        ///
        /// <para>
        /// <b>Answering it is the whole of what stops the back key walking past this panel.</b>
        /// <c>Flow.HandleBack</c> walks the stack downwards until something says it dealt with
        /// the press — so a wall that stayed silent would hand the press to the screen
        /// underneath, which would navigate, which would destroy every modal in the stack
        /// including this one. The gate would put it back a frame later, so the visible symptom
        /// is not an escape: it is the whole interface flickering under a panel every time
        /// somebody presses Back. <c>GemShopOverlay.OnBack</c> records the same trap costing
        /// something worse.
        /// </para>
        /// </summary>
        public override bool OnBack() => true;

        /// <summary>
        /// Takes the wall down, for the one caller allowed to decide that.
        ///
        /// <para>
        /// The requirement was rolled back, or this build now satisfies it — see
        /// <see cref="UpdateGate"/>, which is the only thing that asks. Exposed rather than left
        /// to <c>ModalView.Close</c>'s protection because the panel itself deliberately offers no
        /// route to it: an overlay that could close itself is one a stray tap can close.
        /// </para>
        /// </summary>
        public void Lift() => Close();
    }
}
