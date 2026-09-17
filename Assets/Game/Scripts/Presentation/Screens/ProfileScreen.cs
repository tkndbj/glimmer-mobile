using GlimmerGrove.AssetPipeline;
using System;
using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using GlimmerGrove.Progression;
using GlimmerGrove.Referral;
using GlimmerGrove.Content;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Who the player is: their companion, their name, what their grove has become,
    /// and whether any of it is safe.
    ///
    /// <para>
    /// The body is a scroller built from a <see cref="Section"/> cursor rather than
    /// absolute coordinates. That is the whole point of the shape: badges, friends and
    /// achievements are all going to want a card here, and adding one should be a
    /// method call at the end of <see cref="BuildBody"/> rather than a re-layout of
    /// everything below it.
    /// </para>
    /// <para>
    /// Nothing on this screen is stored. The level, the stars, the counts and the
    /// honorific are all derived, so a retune or a content drop changes what it says
    /// without anything here being migrated.
    /// </para>
    /// </summary>
    public sealed class ProfileScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>The roster's portraits, kept alive for exactly as long as this screen is.</summary>
        AssetHold _portraits;

        const float CardWidth = 980f;

        /// <summary>
        /// What the account card is made of, top to bottom.
        ///
        /// <para>
        /// <see cref="TitleRow"/> is the room <see cref="CardTitle"/> takes; the rest are the
        /// rows themselves and the air after each. The card's height is the sum of whichever it
        /// actually draws, so adding a row is one term here and one block there — rather than a
        /// typed height and a second place to forget.
        /// </para>
        /// </summary>
        const float TitleRow = 100f;
        const float StatusH = 48f, AccountH = 36f, HintH = 78f;
        const float ManageH = 110f, DeleteH = 68f;
        const float AfterStatus = 12f, AfterAccount = 10f, AfterHint = 30f;
        const float FootMargin = 32f;
        const float Gap = 28f;
        const float HeaderHeight = 250f;

        /// <summary>Companions shown on the profile itself; the rest live behind See All.</summary>
        const int PreviewCount = 4;

        RectTransform _viewport, _stack;
        float _cursor;                       // top of the next card, negative and falling

        /// <summary>
        /// False until the body has been built once, which is what tells a card whether it is
        /// <em>arriving</em> or being <em>redrawn</em>.
        ///
        /// <para>
        /// The same split <c>GridView</c> makes between <c>Show</c> and <c>Refresh</c>, and for
        /// its reason: the staggered pop is this screen's entrance, and replaying it because a
        /// toggle was tapped or an account was linked is a page that flinches at the player.
        /// Anything raised by an event is a redraw.
        /// </para>
        /// </summary>
        bool _entered;

        Image _portrait;
        Text _nameLabel;
        Transform _companionRow;
        Text _companionCount;

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            BuildBody();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Profile);

            // The preview row draws a handful of portraits, so the roster's art is
            // wanted here too — and released the moment this screen goes away. Requested
            // after the row exists so the repaint has something to paint.
            _portraits = CompanionArt.Open(this, () => { if (Living) PaintCompanions(); });

            // See CompanionScreen for why this is an event and not a callback: the unlock
            // panel has three exits and only one of them used to report a purchase.
            Progression.CompanionLedger.Changed += RepaintCompanions;

            // And on the worn companion separately, because a purchase records the two one
            // after the other and the ledger's event arrives before the wear — see
            // Profile.AvatarChanged. The medallion showed the old friend until this existed.
            Profile.AvatarChanged += RepaintCompanions;

            AvatarCatalog.Changed += RepaintCompanions;

            // The account card is the one place in the game that says "your progress is saved
            // online", so it has to follow the account rather than whatever was true when the
            // screen opened. AccountOverlay is a modal raised over this screen and has four
            // exits, so a callback from it reports through some of them and not others — the
            // companion screens' bug exactly. An event cannot be forgotten.
            CloudSaveService.IdentityChanged += BuildBody;

            // The record card is six derived figures and two of them are currency, so all six
            // move while this screen is open — a sync applying another device's run, an ad's
            // credits confirmed by the server, a chest opened before the player walked in here.
            // A repaint rather than `BuildBody`, which is what `IdentityChanged` gets: a rebuild
            // replays every card's entrance, and a wallet landing is not an arrival (16d).
            PlayerProgression.Changed += PaintRecord;
        }

        /// <summary>
        /// The row, the count and the hero portrait, which move together when the held set
        /// changes — buying a companion also wears it.
        /// </summary>
        void RepaintCompanions()
        {
            if (!this) return;

            PaintCompanions();

            if (_companionCount)
                _companionCount.text = Loc.Format("ui.profile.unlocked", Profile.CompanionsHeld,
                                                  AvatarCatalog.All.Count);

            if (_portrait) CompanionArt.Paint(_portrait, Profile.Avatar, animate: true);
        }

        /// <summary>
        /// Drops the roster's portraits, unless the showcase is what we are leaving for
        /// — it wants the very same set and would only reload it.
        /// </summary>
        void OnDestroy()
        {
            Progression.CompanionLedger.Changed -= RepaintCompanions;
            Profile.AvatarChanged -= RepaintCompanions;
            AvatarCatalog.Changed -= RepaintCompanions;
            CloudSaveService.IdentityChanged -= BuildBody;
            PlayerProgression.Changed -= PaintRecord;

            _portraits?.Dispose();
        }

        // -------------------------------------------------------------- scroller
        /// <summary>
        /// Builds the scrolling body, and rebuilds it in place when something changes that
        /// reaches more than one card.
        ///
        /// <para>
        /// <b>Rebuilt wholesale rather than patched</b>, which is <c>PaintCompanions</c>' call
        /// one level up: a card's position is the running cursor rather than a number written
        /// down, so a card that changes height moves every card below it. Redrawing five cards
        /// is far cheaper than the bugs of keeping their offsets in step by hand.
        /// </para>
        /// <para>
        /// <b>The outgoing body is hidden before it is destroyed.</b> <c>Destroy</c> lands at
        /// the end of the frame, so a region replaced in place is drawn over its replacement
        /// until then. This screen already followed that rule in <c>PaintCompanions</c> and did
        /// not follow it here — and here it was worse than a flicker, because the old viewport
        /// was neither hidden nor destroyed: every rebuild left one behind, stacked over the
        /// live one, still carrying its invisible drag catcher. So the toggle leaked a whole
        /// page each time it was tapped and the page underneath stopped scrolling properly.
        /// </para>
        /// <para>
        /// <b>The scroll position survives.</b> A rebuild that returns somebody to the top of
        /// the page has lost their place for a reason they did not cause — <c>GridView</c>'s
        /// lesson, and it costs two lines here.
        /// </para>
        /// </summary>
        void BuildBody()
        {
            if (!this) return;

            // Read before anything is torn down, and restored after the new stack is measured.
            float scrolled = _stack ? _stack.anchoredPosition.y : 0f;

            if (_viewport)
            {
                _viewport.gameObject.SetActive(false);
                Destroy(_viewport.gameObject);
            }

            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);       // invisible, but drags land on it
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _stack = UIKit.Node("Stack", _viewport);
            _stack.anchorMin = new Vector2(0f, 1f);
            _stack.anchorMax = new Vector2(1f, 1f);
            _stack.pivot = new Vector2(.5f, 1f);
            _stack.anchoredPosition = Vector2.zero;

            _cursor = -Gap;
            BuildKeeperCard();
            BuildAccountCard();
            BuildRecordCard();
            BuildInviteCard();
            BuildCompanionCard();
            BuildBoardCard();
            BuildDeleteRow();
            _stack.sizeDelta = new Vector2(0f, -_cursor + Gap);

            // Straight to the content rather than through verticalNormalizedPosition, which a
            // ScrollRect resolves against bounds it recomputes in its own LateUpdate — so in
            // the frame the content is built it is read against nothing. GridView.Show carries
            // the same note for the same reason.
            //
            // Clamped, because the rebuild may be shorter than what it replaced: the account
            // card grows a line when the provider gives the account a name, and the board card
            // changes height with the opt-in.
            float reach = Mathf.Max(0f, _stack.sizeDelta.y - _viewport.rect.height);
            _stack.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrolled, 0f, reach));

            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _stack;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;

            // Last, so that every card built above has seen the value from the build before
            // this one. Set here rather than by the caller because there are three callers and
            // this is the only place that can be sure the body exists.
            _entered = true;
        }

        /// <summary>
        /// A card at the cursor, which then moves below it. Returns the card's own
        /// transform, so everything inside is positioned relative to its centre and a
        /// card can be reordered without touching a single number in it.
        /// </summary>
        RectTransform Section(string name, float height, int order)
        {
            // **The hub's own plate, which is the Battle key's mould sliced both ways.** These
            // were a translucent near-black rounded box with a 13%-white outline traced round
            // it - the shape this UI drew before it had a kit, and the last place on either of
            // these two screens still drawing one. Nothing traces a border any more: the sprite
            // carries its own keyline, and a second outline at a radius the sprite does not
            // have is a halo a hair off the shape it is following.
            var card = UIKit.Img(name, _stack, Art.S("Ui/" + Skins.PlateBlue), Color.white,
                                 new Vector2(CardWidth, height), new Vector2(.5f, 1f),
                                 new Vector2(0f, _cursor - height * .5f));

            Seated(card.transform, height, order);
            return (RectTransform)card.transform;
        }

        /// <summary>
        /// The same card, as a button: one plate that <em>is</em> the control rather than a
        /// plate with a control on it.
        ///
        /// <para>
        /// The hub's second door is built this way (<c>HomeScreen.BuildChallenges</c>) and this
        /// is the second of them, so the two go through one shape. A card carrying a painted
        /// banner has nothing on it to press <em>except</em> the banner, and a key drawn inside
        /// one would be a smaller target for the only thing the card does — the tasks page's
        /// rule about a row being its own button, one size up.
        /// </para>
        /// </summary>
        Btn SectionKey(string name, float height, int order, Action tap)
        {
            var card = UIKit.Button(name, _stack, Art.S("Ui/" + Skins.PlateBlue),
                                    new Vector2(CardWidth, height), new Vector2(.5f, 1f),
                                    new Vector2(0f, _cursor - height * .5f), tap);

            // A press-scale that squashes a plate this wide reads as the screen flinching
            // rather than as a key going down. The hub holds its banner's at the same number.
            card.PressScale = .985f;

            Seated(card.transform, height, order);
            return card;
        }

        /// <summary>Moves the cursor past a card just built, and plays its entrance.</summary>
        void Seated(Transform card, float height, int order)
        {
            _cursor -= height + Gap;

            // Only on the way in. A redraw leaves the card at full size — see _entered.
            if (_entered) return;

            card.localScale = Vector3.zero;
            Tween.Pop(card, 0f, .55f, .08f + order * .07f);
        }

        static void CardTitle(Transform card, string key, float width)
            => UIKit.Titled("Head", card, Loc.Get(key).ToUpperInvariant(), 30, Pal.Gold,
                            TextAnchor.MiddleLeft, new Vector2(width - 80f, 40f), new Vector2(0f, 1f),
                            new Vector2(40f + (width - 80f) * .5f, -44f), 3f, 3f);

        // ------------------------------------------------------------ the keeper
        void BuildKeeperCard()
        {
            var card = Section("Keeper", 440f, 0);
            var level = Profile.Level;

            // portrait
            var medallion = UIKit.Img("Medallion", card, Art.Disc(256), Pal.A(Pal.Hex("#08333C"), .95f),
                                      new Vector2(268f, 268f), new Vector2(.5f, .5f), new Vector2(-300f, 34f));
            UIKit.Halo(medallion.transform, Pal.Gold, 340f, .26f);
            var ring = UIKit.Img("Ring", medallion.transform, Art.Ring(256, 13f), Pal.A(Pal.Gold, .92f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            _portrait = UIKit.Img("Critter", medallion.transform, null, Color.white,
                                  new Vector2(198f, 198f), new Vector2(.5f, .5f), new Vector2(0f, 6f));
            _portrait.preserveAspect = true;
            CompanionArt.Paint(_portrait, Profile.Avatar, animate: true);
            Tween.Bob((RectTransform)_portrait.transform, 7f, 3.2f);

            var badge = UIKit.Img("LevelBadge", medallion.transform, Art.Disc(128), Pal.Gold,
                                  new Vector2(92f, 92f), new Vector2(1f, 0f), new Vector2(-6f, 6f));
            UIKit.Titled("N", badge.transform, level.Level.ToString(), 44, new Color(.30f, .20f, .05f),
                         TextAnchor.MiddleCenter, outline: 0f, shadow: 0f);

            // name, and the pencil that changes it
            _nameLabel = UIKit.Titled("Name", card, Profile.Name, 52, Pal.Cream, TextAnchor.MiddleLeft,
                                      new Vector2(500f, 62f), new Vector2(.5f, .5f), new Vector2(140f, 126f), 4f, 4f);

            UIKit.IconButton("Rename", card, Skins.Aside, "ic_pencil", new Vector2(96f, 96f),
                             new Vector2(.5f, .5f), new Vector2(436f, 126f),
                             () => Flow.Modal<RenameOverlay>(v => v.OnRenamed = Refresh), .48f);

            var ribbon = UIKit.Img("Title", card, Art.S("Ui/ribbon_flat"), Color.white,
                                   new Vector2(380f, 74f), new Vector2(.5f, .5f), new Vector2(80f, 52f));
            UIKit.Titled("T", ribbon.transform, Loc.Get(KeeperTitle.KeyFor(level.Level)), 32,
                         new Color(.34f, .22f, .12f), TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);

            // experience toward the next keeper level
            //
            // **The column clears the level badge, and that is what these numbers are.** The
            // badge hangs off the medallion's bottom-right corner, so its right edge lands at
            // -126 while this column used to start at -128: the XP figure and the next-title
            // line both began *inside* the disc, and both sit squarely in its vertical band.
            // The column keeps its right edge (484) and gives up its left, which is the only
            // half that collides — widening the card or moving the badge would move something
            // that is already where it belongs.
            const float XpLeft = -64f, XpRight = 484f;
            const float XpW = XpRight - XpLeft, XpX = (XpLeft + XpRight) * .5f;

            var track = UIKit.Img("XpTrack", card, Art.S("Ui/" + Skins.Trough), Color.white,
                                  new Vector2(XpW, 40f), new Vector2(.5f, .5f), new Vector2(XpX, -34f));
            var fill = UIKit.Img("XpFill", track.transform, Art.S("Ui/" + Skins.Fill), Pal.Mint,
                                 new Vector2(0f, 30f), new Vector2(0f, .5f), new Vector2(5f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            float full = (XpW - 10f) * level.Progress01;
            Tween.Run(.85f, Ease.OutCubic, t =>
            {
                if (!fillRT) return;
                fillRT.sizeDelta = new Vector2(full * t, 30f);
                fill.color = Color.Lerp(Pal.Aqua, Pal.Mint, t);
            }, fill).Delay(.35f);

            // White, and no outline. Both of these were dimmed cream (.72 and .5) carrying the
            // 3-unit dark border, which on this plate is more dark mass than the stem itself —
            // the two lines read as smudges rather than as sentences. The shadow stays; it is
            // what lifts a light line off the plate without thickening it.
            UIKit.Titled("XpText", card,
                         level.IsMaxLevel
                             ? Loc.Get("ui.profile.xp_max")
                             : Loc.Format("ui.profile.xp", level.XpIntoLevel, level.XpForNextLevel),
                         27, Color.white, TextAnchor.MiddleLeft,
                         new Vector2(XpW, 34f), new Vector2(.5f, .5f), new Vector2(XpX, -84f), 0f, 2f);

            int nextTier = KeeperTitle.NextTierLevel(level.Level);
            if (nextTier > 0)
            {
                UIKit.Titled("NextTitle", card,
                             Loc.Format("ui.profile.next_title", Loc.Get(KeeperTitle.KeyFor(nextTier)), nextTier),
                             25, Color.white, TextAnchor.MiddleLeft,
                             new Vector2(XpW, 32f), new Vector2(.5f, .5f), new Vector2(XpX, -128f), 0f, 2f);
            }
        }

        // ----------------------------------------------------------- inviting
        /// <summary>How far inside the plate the banner's window sits. See BuildInviteCard.</summary>
        const float BannerInset = 8f;

        /// <summary>
        /// How far the banner swells and how long it takes. See <see cref="BuildInviteCard"/>
        /// for why the picture is cut oversize by exactly this much.
        /// </summary>
        const float BannerSwell = .03f, BannerPeriod = 3.6f;

        /// <summary>
        /// The way to the invite page, on the one page in the game about who the player is: one
        /// painted banner that is the whole card and the whole button.
        ///
        /// <para>
        /// A card rather than a row in Settings for the account card's reason: a feature that
        /// pays chests and is three taps deep in a preferences panel is a feature nobody
        /// finds. Drawn only where a backend exists and the content offers a ladder
        /// (<see cref="ReferralLedger.IsAvailable"/>); a card promising chests a server cannot
        /// pay would be the one lie this page could tell about money.
        /// </para>
        /// <para>
        /// <b>It is the hub's second door, built twice</b> — the same plate, the same window
        /// cut with a <c>Mask</c>, the same cover-fit, at the owner's instruction. What it
        /// costs is the one thing that shape costs: the words are <em>painted into the
        /// picture</em>, so this control and the hub's are the only two in the game outside
        /// invariant 6, and neither is translated until its art is re-cut. The title, the
        /// milestone sentence and the key it replaced were all loc keys; the page it opens
        /// still says all three.
        /// </para>
        /// <para>
        /// <b>The swell is why the picture is cut <em>smaller</em> than the window, which is the
        /// opposite of what it looks like it should be.</b> The banner's ground is transparent —
        /// the plate behind it is the card's colour — so a trough that pulls the art inside the
        /// window exposes nothing; what the swell can do is push it <em>out</em>, and at the
        /// crest a cover-fitted banner runs 29 units past the window and the mask takes a slice
        /// off the leaves at each end. Cut at <c>1 / (1 + swell)</c> the crest is exactly the
        /// window, so the sides are never cut at any point in the breath and the mask is there
        /// to guarantee it rather than to do it. Invariant 44a's rule in a second costume: size
        /// a thing for the size it has to draw at, not for the size it sits at.
        /// </para>
        /// </summary>
        void BuildInviteCard()
        {
            if (!ReferralLedger.IsAvailable) return;

            const float InviteH = 300f;
            var card = SectionKey("Invite", InviteH, 3, () => Flow.Go<ReferralScreen>());

            // The window. `showMaskGraphic` is false, so the plate is not painted twice; the
            // near-nothing alpha is what writes the stencil. See `HomeScreen.BuildChallenges`
            // for why this plate is deliberately *not* graded uncompressed to cut it.
            var clip = UIKit.Img("Clip", card.transform, Art.S("Ui/" + Skins.PlateBlue),
                                 new Color(1f, 1f, 1f, .004f));
            var crt = (RectTransform)clip.transform;
            UIKit.StretchTo(crt, BannerInset, BannerInset, BannerInset, BannerInset);
            clip.type = Image.Type.Sliced;
            clip.raycastTarget = false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var banner = Art.S("Ui/refer");
            float aspect = banner != null && banner.rect.height > 0f
                         ? banner.rect.width / banner.rect.height
                         : 0f;

            // Cover: the larger of the two scales that fill an axis, then taken back down so
            // the *crest* of the breath is what fits. An address that has not arrived leaves the
            // plate plain rather than drawing a white bar (invariant 7b).
            float windowW = CardWidth - BannerInset * 2f, windowH = InviteH - BannerInset * 2f;
            float drawnW = windowW, drawnH = aspect > 0f ? drawnW / aspect : 0f;
            if (drawnH < windowH) { drawnH = windowH; drawnW = drawnH * aspect; }

            float seated = 1f / (1f + BannerSwell);
            drawnW *= seated;
            drawnH *= seated;

            var art = UIKit.Img("Banner", crt, banner, Color.white,
                                new Vector2(drawnW, drawnH), new Vector2(.5f, .5f), Vector2.zero);
            art.raycastTarget = false;
            art.enabled = aspect > 0f;

            // Slow, and on the picture rather than on the card: a plate that breathed would
            // move against the two cards it is stacked between, where a picture breathing
            // behind a fixed window is the card's own light moving.
            if (art.enabled) Tween.Breathe(art.transform, BannerSwell, BannerPeriod);
        }

        // ------------------------------------------------------------ the record
        void BuildRecordCard()
        {
            var card = Section("Record", 430f, 2);
            CardTitle(card, "ui.profile.record", CardWidth);

            // Shown against the total the catalog holds, which is where the home screen's
            // old grove bar went when that panel became the daily one. A bare star count
            // says how far you have come; this one also says how far there is to go.
            _record.Clear();

            Tile(card, -310f, 40f, "ic_star", () => $"{Profile.TotalStars}/{Profile.MaxStars}",
                 "ui.profile.stars", Pal.Gold);
            Tile(card, 0f, 40f, "ic_home", () => $"{Profile.ChaptersCompleted}/{Profile.ChapterCount}",
                 "ui.profile.chapters", Pal.Aqua);
            Tile(card, 310f, 40f, "ic_star3d", () => $"{Profile.PerfectGlades}", "ui.profile.perfect", Pal.Sun);

            Tile(card, -310f, -128f, "ic_check", () => $"{PlayerProgression.ClearedGlades}", "ui.profile.glades", Pal.Mint);
            Tile(card, 0f, -128f, "ic_chest", () => Compact.Number(Profile.Coins), "ui.profile.coins", Pal.Gold);
            Tile(card, 310f, -128f, "ic_gem", () => Compact.Number(Profile.Gems), "ui.profile.gems", Pal.Bloom);
        }

        /// <summary>
        /// The record card's six figures, each with the question it answers rather than the
        /// answer it was built with.
        ///
        /// <para>
        /// <b>A reader rather than a string is what makes the repaint possible at all.</b> A
        /// tile built from <c>Profile.Coins</c> is a photograph of the moment it was built, and
        /// the alternative to keeping the question is six fields and a second copy of what each
        /// one means — two places that can come to disagree about what "glades" counts.
        /// </para>
        /// <para>
        /// <b>Deliberately not registered with <see cref="ResourceSlots"/>.</b> These are record
        /// tiles rather than a purse: nothing on this screen pays out, the registry holds one
        /// slot per currency, and a chest's tokens flying into a statistic on a page nobody
        /// opened a chest from would be a reward landing in the wrong place.
        /// </para>
        /// </summary>
        readonly List<Recorded> _record = new List<Recorded>(6);

        sealed class Recorded
        {
            public Text Value;
            public Func<string> Read;
        }

        /// <summary>Writes the six figures again. Same tiles, same place, no entrance.</summary>
        void PaintRecord()
        {
            if (!this) return;

            for (int i = 0; i < _record.Count; i++)
            {
                var tile = _record[i];
                if (tile.Value) tile.Value.text = tile.Read();
            }
        }

        void Tile(Transform card, float x, float y, string icon, Func<string> read, string labelKey, Color tint)
        {
            // The tiles go the *other* way now the card under them is bright: a 5%-white wash
            // was a lighter shape on a near-black card and is invisible on a lit one, so this is
            // an inset - the same navy every plate in the game is drawn in, sunk into the plate
            // rather than floated on it. The tint stays on the glow and the caption, which is
            // where it was doing the work anyway.
            var bg = UIKit.Img("Tile_" + labelKey, card, Art.Round(24), Skins.Plate,
                               new Vector2(290f, 148f), new Vector2(.5f, .5f), new Vector2(x, y));

            UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .22f),
                      new Vector2(120f, 120f), new Vector2(0f, .5f), new Vector2(56f, 12f));
            var ic = UIKit.Img("Icon", bg.transform, Art.S("Ui/" + icon), Color.white,
                               new Vector2(62f, 62f), new Vector2(0f, .5f), new Vector2(56f, 12f));
            ic.preserveAspect = true;

            _record.Add(new Recorded
            {
                Value = UIKit.Titled("V", bg.transform, read(), 44, Pal.Cream, TextAnchor.MiddleLeft,
                                     new Vector2(160f, 54f), new Vector2(0f, .5f),
                                     new Vector2(186f, 14f), 3f, 3f),
                Read = read,
            });

            UIKit.Titled("L", bg.transform, Loc.Get(labelKey), 24, new Color(1f, .96f, .88f, .58f),
                         TextAnchor.MiddleCenter, new Vector2(266f, 32f), new Vector2(.5f, 0f),
                         new Vector2(0f, 26f), 3f, 0f);
        }

        // -------------------------------------------------------- the companions
        void BuildCompanionCard()
        {
            var card = Section("Companions", 340f, 3);
            int level = Profile.Rank;

            CardTitle(card, "ui.profile.companions", CardWidth);
            _companionCount = UIKit.Titled("Count", card,
                         Loc.Format("ui.profile.unlocked", CompanionLedger.HeldCount(level),
                                    AvatarCatalog.All.Count),
                         26, new Color(1f, .96f, .88f, .60f), TextAnchor.MiddleRight,
                         new Vector2(300f, 36f), new Vector2(1f, 1f), new Vector2(-190f, -44f), 3f, 0f);

            _companionRow = UIKit.Box("Row", card, new Vector2(CardWidth, 220f), new Vector2(.5f, .5f),
                                      new Vector2(0f, -34f));
            PaintCompanions();
        }

        /// <summary>
        /// Rebuilt wholesale rather than patched, because choosing one changes the
        /// selected ring, the portrait and the caption together, and a redraw of eleven
        /// images is far cheaper than the bugs of keeping three of them in step.
        /// </summary>
        void PaintCompanions()
        {
            if (_companionRow == null) return;
            for (int i = _companionRow.childCount - 1; i >= 0; i--)
            {
                var old = _companionRow.GetChild(i).gameObject;
                old.SetActive(false);        // Destroy only lands at end of frame
                Destroy(old);
            }

            int level = Profile.Rank;
            string worn = Profile.Avatar.Id;
            var preview = Preview(worn, PreviewCount);

            // Always PreviewCount + 1 slots wide, so the See All tile sits in the same
            // place whether the roster is five companions or a hundred.
            const float Step = 186f;
            float left = -(PreviewCount) * Step * .5f;

            for (int i = 0; i < preview.Count; i++)
                // The whole rule — reached by level or bought. See CompanionLedger.
                Companion(preview[i], left + i * Step, CompanionLedger.IsHeld(preview[i], level),
                          string.Equals(preview[i].Id, worn, StringComparison.Ordinal));

            SeeAllTile(left + PreviewCount * Step, AvatarCatalog.All.Count - preview.Count);
        }

        /// <summary>
        /// The few companions worth showing on the profile itself: the one being worn,
        /// then the rest in roster order. The worn one leads because the card is about
        /// the player, not about the catalogue — the catalogue is what See All is for.
        /// </summary>
        static List<AvatarDefinition> Preview(string worn, int count)
        {
            var picked = new List<AvatarDefinition>(count);

            var current = AvatarCatalog.Find(worn);
            if (current.IsValid) picked.Add(current);

            foreach (var avatar in AvatarCatalog.All)
            {
                if (picked.Count >= count) break;
                if (string.Equals(avatar.Id, worn, StringComparison.Ordinal)) continue;
                picked.Add(avatar);
            }

            return picked;
        }

        /// <summary>The door to the showcase, sized and placed like a companion.</summary>
        void SeeAllTile(float x, int remaining)
        {
            var cell = UIKit.Button("SeeAll", _companionRow, Art.Pixel, new Vector2(168f, 210f),
                                    new Vector2(.5f, .5f), new Vector2(x, 0f),
                                    () => Flow.Go<CompanionScreen>());
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            var disc = UIKit.Img("Disc", cell.transform, Art.Disc(160), Pal.A(Pal.Hex("#0B4C55"), .95f),
                                 new Vector2(148f, 148f), new Vector2(.5f, .5f), new Vector2(0f, 22f));
            var ring = UIKit.Img("Ring", disc.transform, Art.Ring(160, 6f), Pal.A(Pal.Mint, .55f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            UIKit.Titled("N", disc.transform, remaining > 0 ? "+" + remaining : "…", 46, Pal.Cream,
                         TextAnchor.MiddleCenter, new Vector2(140f, 60f), new Vector2(.5f, .5f),
                         new Vector2(0f, 2f), 3f, 3f);

            UIKit.Titled("L", cell.transform, Loc.Get("ui.profile.see_all"), 24, Pal.Mint,
                         TextAnchor.MiddleCenter, new Vector2(180f, 32f), new Vector2(.5f, 0f),
                         new Vector2(0f, 22f), 3f, 0f);
        }

        void Companion(AvatarDefinition avatar, float x, bool unlocked, bool worn)
        {
            var cell = UIKit.Button("A_" + avatar.Id, _companionRow, Art.Pixel, new Vector2(168f, 210f),
                                    new Vector2(.5f, .5f), new Vector2(x, 0f), () => Choose(avatar, unlocked));
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            var disc = UIKit.Img("Disc", cell.transform, Art.Disc(160),
                                 unlocked ? Pal.A(Pal.Hex("#08333C"), .92f) : new Color(.02f, .06f, .08f, .70f),
                                 new Vector2(148f, 148f), new Vector2(.5f, .5f), new Vector2(0f, 22f));

            if (worn) UIKit.Halo(cell.transform, Pal.Gold, 200f, .34f);

            var ring = UIKit.Img("Ring", disc.transform, Art.Ring(160, worn ? 11f : 6f),
                                 worn ? Pal.A(Pal.Gold, .95f) : new Color(1f, 1f, 1f, unlocked ? .22f : .10f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            var face = UIKit.Img("Face", disc.transform, null,
                                 unlocked ? Color.white : new Color(.16f, .22f, .26f, .95f),
                                 new Vector2(110f, 110f), new Vector2(.5f, .5f), new Vector2(0f, 4f));
            face.preserveAspect = true;
            CompanionArt.Paint(face, avatar);

            if (!unlocked)
            {
                // **Over the companion rather than under it, and never tinted.** It hung at the
                // foot of the disc, which reads as a badge sitting beside a portrait rather than
                // as the portrait being shut away — and the padlock is a painted picture, so
                // anything but white is a multiply that eats the gold it is drawn in.
                //
                // Built after the face, so it draws over it: uGUI paints in sibling order and
                // there is nothing else here that decides it.
                var lockIcon = UIKit.Img("Lock", disc.transform, Art.S("Ui/ic_padlock"), Color.white,
                                         new Vector2(78f, 78f), new Vector2(.5f, .5f), new Vector2(0f, 4f));
                lockIcon.preserveAspect = true;
                lockIcon.raycastTarget = false;
            }

            UIKit.Shrinkable(
                UIKit.Titled("L", cell.transform,
                             unlocked ? Loc.Get(avatar.NameKey)
                                      : avatar.IsForSale
                                          ? Loc.Format("ui.profile.cost", Compact.Number(avatar.UnlockCost))
                                          : Loc.Format("ui.profile.locked_at", avatar.UnlockLevel),
                             24, unlocked ? (worn ? Pal.Cream : new Color(1f, .96f, .88f, .66f))
                                          : avatar.IsForSale ? Pal.A(Pal.Sun, .88f)
                                                             : new Color(1f, .8f, .7f, .55f),
                             TextAnchor.MiddleCenter, new Vector2(180f, 32f), new Vector2(.5f, 0f),
                             new Vector2(0f, 22f), 3f, 0f), 18);

            if (worn) Tween.Breathe(disc.transform, .03f, 2.6f);
        }

        void Choose(AvatarDefinition avatar, bool unlocked)
        {
            if (!unlocked)
            {
                // The panel, not a toast naming a level the catalog cannot reach. See
                // CompanionUnlockOverlay.
                Audio.Sfx("chime", .45f);
                Flow.Modal<CompanionUnlockOverlay>(v => v.Avatar = avatar);
                return;
            }

            // The row, the count and the medallion are repainted by Profile.AvatarChanged.
            // What stays here is only what belongs to the *tap* rather than to the state —
            // a sound, a bump and the sparks off the medallion.
            if (!Profile.TryWearAvatar(avatar.Id)) return;

            // **Its own slot rather than `chime2`.** That bell is the confirmation three other
            // things ring, and one of them is `ToggleBoardVisibility` two cards down this very
            // screen - so wearing a friend and joining a leaderboard said exactly the same thing.
            Audio.Sfx("wear", .5f);
            if (_portrait) Burst.Sparks(_portrait.transform, Vector2.zero, Pal.Gold, 12, 190f, 26f, .6f);
        }

        // ----------------------------------------------------------- the account
        // ------------------------------------------------------------- the boards
        /// <summary>
        /// Where this keeper stands among everybody else, and whether they appear at all.
        ///
        /// <para>
        /// <b>It reads the Endless Watch, because that is the board there is.</b> It used to
        /// read what the grove was worth, which was the figure the finest-groves board was
        /// ordered on — and that board is <b>held</b> with the Grovement it ranks. Left as it
        /// was, this card would have printed "buy something for your grove and you will be
        /// ranked" to a player with no grove shop to reach and no board to be ranked on, which
        /// is the worst kind of stale copy: every word of it true when it was written, every
        /// word of it a wrong instruction now.
        /// </para>
        /// <para>
        /// <b>The opt-out is untouched and that is the point of keeping the card at all.</b>
        /// What it governs is whether a card is published, and a card is what <em>both</em>
        /// boards are built from — so holding one board does not make the control less
        /// needed, it makes it the only way off the one that is left. Removing it with the
        /// grove's own figures would have taken away a player's only way out of a public list.
        /// </para>
        ///
        /// <para>
        /// <b>The opt-out lives here rather than in Settings, and for the reason the account
        /// section moved here.</b> Appearing on a public list under a name is a question about
        /// <em>who the player is</em>, and burying it in a preferences panel beside the music
        /// volume is how it stays unfound — by the people who most want it, who are exactly
        /// the people it exists for. It sits directly above the account card because the two
        /// are one subject.
        /// </para>
        /// <para>
        /// Turning it off is not a preference that takes effect later: it takes the published
        /// card down. See <c>GroveBoard</c> and <c>GrovePublishPolicy.RequestWithdrawal</c>.
        /// </para>
        /// </summary>
        void BuildBoardCard()
        {
            var card = Section("Boards", 330f, 4);
            CardTitle(card, "ui.board.title", CardWidth);

            var glyph = UIKit.Img("Glyph", card, Art.S("Ui/ic_trophy"), Color.white,
                                  new Vector2(76f, 76f), new Vector2(0f, .5f), new Vector2(104f, 54f));
            glyph.preserveAspect = true;
            UIKit.Halo(glyph.transform, Pal.Gold, 150f, .26f);

            // `Waves` rather than `Table`, which is the same distribution document read on a
            // different field — the two populations are deliberately different lengths, because
            // a percentile only means anything against keepers who have actually done the
            // thing (invariant 19m). Both refuse to answer under their own sample floor, and
            // `TopPercent` returning nought is what `ui.board.building` is for.
            int best = EndlessLedger.Best;
            int top = GroveRanks.Waves.TopPercent(best);

            // The connection case sits *under* the percentile rather than over it, which is the
            // whole of what makes it safe to add. A standing this device already has is still
            // true in a tunnel — the distribution is a document read minutes ago and the best
            // wave is read out of the save — so a phone with no signal must not replace a
            // correct number with an apology. What it may replace is the one branch that means
            // "we have no percentile for you": "the standing arrives with the next tally" is a
            // promise about a job that has not run, and offline we did not get far enough to
            // know whether it has.
            string line = !GroveBoard.IsAvailable ? Loc.Get("ui.board.offline")
                        : !GroveBoard.OptedIn ? Loc.Get("ui.board.opted_out")
                        : best <= 0 ? Loc.Get("ui.board.unranked")
                        : top > 0 ? Loc.Format("ui.board.top_percent", top)
                        : Net.Offline ? Loc.Get("ui.board.no_connection")
                        : Loc.Get("ui.board.building");

            UIKit.Shrinkable(
                UIKit.Titled("Standing", card, line, 30, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(560f, 60f), new Vector2(0f, .5f), new Vector2(440f, 54f),
                             3f, 3f), 18);

            UIKit.TextButton("Open", card, "btn_orange", Loc.Get("ui.board.open"), 32,
                             // 268 rather than 280, and the toggle's -280 moves to -268 with it.
                             // Both are 420 wide on a 980 card, so at the old numbers they met
                             // at exactly 490 and touched. Moving each 12 out gives 24 between
                             // them and leaves the card's two margins equal at 58.
                             new Vector2(420f, 104f), new Vector2(0f, 0f), new Vector2(268f, 62f),
                             () => Flow.Go<LeaderboardScreen>());

            // A toggle rather than a line in a menu somewhere else, and it says which state it
            // is in rather than which state it would move to — the ambiguity that makes every
            // "Disable notifications?" button in the world a coin flip.
            // The same box as the key beside it, to the unit. Two controls in one row at two
            // sizes reads as one of them having been added later, which is exactly what
            // happened — and the caption is `Shrinkable`, so matching the larger of the two
            // costs the longer word nothing.
            var toggle = UIKit.TextButton("Visibility", card,
                                          Skins.Battle,
                                          Loc.Get(GroveBoard.OptedIn ? "ui.board.leave"
                                                                     : "ui.board.join"), 32,
                                          new Vector2(420f, 104f), new Vector2(1f, 0f),
                                          new Vector2(-268f, 62f),
                                          ToggleBoardVisibility);
            UIKit.Shrinkable(toggle.Label, 19);
            UIKit.FitLabel(toggle);
        }

        /// <summary>
        /// Joins or leaves the boards, and rebuilds the card so it describes the new state.
        ///
        /// <c>GameSettings</c> raises its own event and <c>GroveBoard</c> is subscribed to it,
        /// so the withdrawal or the republish happens without this method knowing anything
        /// about either — the wiring rule this project has paid for three times.
        /// </summary>
        void ToggleBoardVisibility()
        {
            GameSettings.SetBoardOptIn(!GameSettings.BoardOptIn);
            Audio.Sfx("chime2", .5f);
            BuildBody();
        }

        void BuildAccountCard()
        {
            bool available = CloudSaveService.IsAvailable;

            // Asked before IsLinked, and it has to be. A device caught between two accounts
            // *is* signed in, so reading only IsLinked here would put "your progress is saved
            // online" on the one screen a player checks when they suspect it is not — and
            // nothing is being saved at all in that state. It is the only lie this card can
            // tell, so it is the first thing it rules out.
            bool mismatched = available && CloudSaveService.AccountMismatched;
            bool linked = available && !mismatched && CloudSaveService.IsLinked;

            string statusKey = !available ? "ui.account.unavailable"
                             : mismatched ? "ui.account.mismatch"
                             : linked ? "ui.account.linked"
                             : "ui.account.guest";

            // What the account is called, when the provider gave one. Display only — see
            // CloudIdentity.Label — and absent for a guest, who has no account to name.
            //
            // The card grows a line to hold it rather than squeezing one in, and everything
            // above the hint moves up by exactly that line. Reserving the room unconditionally
            // would leave a hole on the guest card, which is what most players see first; the
            // hint and the button keep their distance from the bottom either way, because the
            // button is the one thing anchored to it.
            string account = linked ? CloudSaveService.AccountLabel : string.Empty;
            bool showAccount = !string.IsNullOrEmpty(account);

            // ------------------------------------------------------------------ measure
            // Every row is stacked from the card's top edge and the card is sized to hold
            // exactly what it draws — the same arrangement AccountOverlay uses, and for its
            // reason: this card grows a line when the provider names the account, so a typed
            // height is a height that ends up printing a sentence through a button. It already
            // did: the guest hint was drawn tight against the button under it.
            float height = TitleRow + StatusH + AfterStatus
                         + (showAccount ? AccountH + AfterAccount : 0f)
                         + HintH + AfterHint
                         + (available ? ManageH : 0f)
                         + FootMargin;

            var card = Section("Account", height, 1);
            CardTitle(card, "ui.profile.account", CardWidth);

            float cursor = TitleRow;

            // --------------------------------------------------------------------- status
            // Centred, and there is deliberately no key glyph beside it any more. The icon sat
            // in the left margin with a coloured halo behind it and pushed every line on the
            // card off-centre to make room, so the one sentence a player opens this screen to
            // read — whether their grove is safe — was the only thing on the profile that was
            // not centred. The status colour already says everything the halo said.
            UIKit.Titled("Status", card, Loc.Get(statusKey), 34,
                         !available ? new Color(1f, .95f, .86f, .6f) : linked ? Pal.Mint : Pal.Rose,
                         TextAnchor.MiddleCenter, new Vector2(CardWidth - 120f, StatusH),
                         new Vector2(.5f, 1f), new Vector2(0f, -(cursor + StatusH * .5f)), 3f, 3f);

            cursor += StatusH + AfterStatus;

            // Shrinkable rather than wrapping: an address is one token, and a second line of it
            // reads as a second fact rather than as the same one continued.
            if (showAccount)
            {
                UIKit.Shrinkable(
                    UIKit.Titled("Account", card, account, 26, Color.white,
                                 TextAnchor.MiddleCenter, new Vector2(CardWidth - 160f, AccountH),
                                 new Vector2(.5f, 1f), new Vector2(0f, -(cursor + AccountH * .5f)),
                                 0f, 2f),
                    18);

                cursor += AccountH + AfterAccount;
            }

            // White, no outline, for the XP line's reason and one more: this is the longest
            // sentence on the page and the only wrapped one, so the border was being drawn
            // round every stem of two full lines of 25pt text. It is the sentence a player
            // opens this card to read.
            UIKit.Titled("Why", card, Loc.Get(mismatched ? "ui.profile.mismatch_hint"
                                             : linked ? "ui.profile.linked_hint" : "ui.profile.guest_hint"),
                         25, Color.white, TextAnchor.UpperCenter,
                         new Vector2(800f, HintH), new Vector2(.5f, 1f),
                         new Vector2(0f, -(cursor + HintH * .5f)), 0f, 2f, wrap: true);

            cursor += HintH + AfterHint;

            if (!available) return;

            // btn_orange for the unfinished-switch state rather than btn_red. Red is the
            // deletion's colour now, and two red buttons on one card — one that fixes an
            // account and one that destroys it — is the worst possible place for that
            // ambiguity.
            UIKit.TextButton("Manage", card, mismatched ? "btn_orange" : linked ? "btn_blue" : "btn_green",
                             Loc.Get(mismatched ? "ui.profile.fix"
                                   : linked ? "ui.profile.manage" : "ui.profile.protect"), 34,
                             new Vector2(460f, ManageH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(cursor + ManageH * .5f)),
                             () => Flow.Modal<AccountOverlay>());
        }

        // ------------------------------------------------------------- deletion
        /// <summary>
        /// Ending the account, at the foot of the page and standing on nothing.
        ///
        /// <para>
        /// <b>It is off the account card now, and the point of that is what it is no longer
        /// beside.</b> That card is about protecting a grove — its heading, its status line and
        /// its one button all say so — and the control that destroys the grove was standing on
        /// it, eighteen units under the control that saves it. Two buttons on one plate read as
        /// two answers to one question, which is exactly the shape a misfire wants. Down here it
        /// is its own question, asked on its own, after everything else the page has to say.
        /// </para>
        /// <para>
        /// <b>The bottom of the page rather than the bottom of the display.</b> A red key pinned
        /// over the nav bar would be the most permanent thing on the screen and would follow the
        /// player past every card — and this page is a scroller, so "the bottom" is a place
        /// somebody arrives at rather than a place they are held. Both stores require this to be
        /// <em>reachable</em>; neither asks for it to be in the way.
        /// </para>
        /// <para>
        /// <b>No plate under it.</b> Every other row here is a card because a card is what groups
        /// things, and there is one thing here — a plate holding one small red button would
        /// draw more attention to it than the button does.
        /// </para>
        /// <para>
        /// Drawn wherever a deletion could possibly succeed, which is wherever there is a
        /// backend, including the mismatched state (<c>AccountDeletion.Offered</c>). It stays on
        /// this page rather than moving into Settings for the reason the account row does: this
        /// is the one page in the game about <em>who the player is</em>, and burying the control
        /// that ends an account three taps deep in a preferences panel is how the last one
        /// stayed unfound — and, for this one, how a review gets refused.
        /// </para>
        /// </summary>
        void BuildDeleteRow()
        {
            if (!AccountDeletion.Offered(CloudSaveService.IsAvailable)) return;

            // A gap of its own on top of the stack's own, so it plainly is not part of the card
            // above it — the one thing this control must never read as is the next row down.
            _cursor -= Gap;

            var button = UIKit.TextButton("Delete", _stack, "btn_red", Loc.Get("ui.profile.delete"), 26,
                                          new Vector2(460f, DeleteH), new Vector2(.5f, 1f),
                                          new Vector2(0f, _cursor - DeleteH * .5f),
                                          () => Flow.Modal<DeleteAccountOverlay>());
            UIKit.Shrinkable(button.Label, 16);

            _cursor -= DeleteH + Gap;

            // The stagger every card gets, and the last beat of it. See Section.
            if (_entered) return;

            button.transform.localScale = Vector3.zero;
            Tween.Pop(button.transform, 0f, .55f, .08f + 5 * .07f);
        }

        // -------------------------------------------------------------- chrome
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .80f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 300f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomeScreen>());

            var banner = Scenery.TitleRibbon(Safe, Loc.Get("ui.profile.title").ToUpperInvariant(),
                                             new Vector2(520f, 148f), new Vector2(.5f, 1f),
                                             new Vector2(0f, -142f));
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            UIKit.IconButton("Settings", Safe, Skins.Aside, "ic_gear", new Vector2(118f, 118f),
                             new Vector2(1f, 1f), new Vector2(-96f, -132f), () => Flow.Modal<SettingsOverlay>());
        }

        /// <summary>Redraws what a rename changed, without rebuilding the screen.</summary>
        void Refresh()
        {
            if (_nameLabel) _nameLabel.text = Profile.Name;
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }
}
