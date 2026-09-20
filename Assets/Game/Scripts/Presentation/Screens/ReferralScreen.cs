using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Referral;
using GlimmerGrove.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Invite friends: the account's code, the row for a friend's code (or the chests it
    /// earned), and one row per friend who finished, each paying the referrer's chests.
    ///
    /// <para>
    /// <b>The page is the streak page's page</b> (invariant 47g): the kit's plates on
    /// <see cref="Scenery.Plain"/>, a title ribbon over the wallet, one hero plate carrying
    /// the thing the page is about, one offer row, and a board under a heading. A list of
    /// chests is a list of chests whatever earns it.
    /// </para>
    /// <para>
    /// <b>Everything it draws is the server's answer, cached</b> (invariant 51). The page
    /// paints from <see cref="ReferralLedger.State"/> the moment it opens and asks the server
    /// for a fresh copy in the same frame; a row goes lit when the server's count reaches it
    /// and never when this device believes it should have. A tap on a lit row asks the server
    /// to pay one chest, and only what the server answers is opened — through the same
    /// ceremony every chest in this game opens (<see cref="ChestOverlay"/> via
    /// <see cref="ChestClaim.ForReferral"/>). A row paying two chests is tapped twice, and
    /// says so between the taps.
    /// </para>
    /// <para>
    /// <b>The board is one row per friend who has finished, plus the next few</b>, so a page
    /// with fifty possible rows never draws fifty empty ones. The row count moves with the
    /// server's count, and when it does the page is rebuilt; the offer row has three shapes
    /// and rebuilds the same way. Everything else is a repaint (<c>CRAFT.md</c>: Show
    /// animates, Refresh does not).
    /// </para>
    /// </summary>
    public sealed class ReferralScreen : View
    {
        public override string Track => "mus_menu";

        // The stack, in canvas units from the top of the safe area.
        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float HeroH = 300f;
        const float HeadingH = 62f;
        const float Width = 1000f;

        /// <summary>
        /// One friend is one row, the full width of the page — the streak's row (48g), and the
        /// tasks page's card.
        ///
        /// <para>
        /// <b>The same height as a task's row, deliberately.</b> This board is a list of chests
        /// and so is that one; a reward row that is two thirds the size of the reward row two
        /// taps away is two answers to a question the kit settles once (invariant 44). What the
        /// extra forty units buy is the thing the page is actually about: the chest grows with
        /// the row, from 88 drawn to <see cref="RewardTall"/>.
        /// </para>
        /// </summary>
        const float RowH = 196f;
        const float RowGap = 12f;

        /// <summary>
        /// The <b>COLLECT</b> key at the right end of a row, the pill that stands in the same
        /// place when there is nothing to collect yet, and the room <see cref="Scenery.Pill"/>
        /// really leaves that pill's words.
        ///
        /// <para>
        /// The streak board's numbers, because this is the streak board's row — one width for
        /// the two, since the right end carries exactly one of them at a time, and the line is
        /// fitted on write from <see cref="MarkType"/> rather than drawn at whatever size it was
        /// built at. English fits either way here; a translation of <c>ui.referral.settling</c>
        /// half again as long is what the fit is for, and a <c>Text</c> that overflows is not
        /// clipped and says nothing (<see cref="UIKit.OneLineLabel"/>).
        /// </para>
        /// </summary>
        const float KeyW = 212f, KeyH = 84f, MarkH = 62f;
        const int MarkType = 23, MarkLeast = 14;
        const float MarkRoom = KeyW - 20f - 16f;

        /// <summary>
        /// The offer band between the hero and the board.
        ///
        /// <b>The same height as a row, because in one of its two shapes it <em>is</em> one</b> —
        /// the welcome chests are furnished by <see cref="Furnish"/> exactly as a friend's row
        /// is, so a band shorter than a row would draw the same furniture in a smaller box and
        /// the seat would stand proud of its own plate.
        /// </summary>
        const float OfferH = RowH;

        /// <summary>The well a reward stands in, and its drawn height. See <see cref="ChestPack"/>.</summary>
        const float SeatSize = 160f, SeatX = 118f, RewardTall = 124f;

        const float TextX = 220f, TextW = 450f;

        /// <summary>The code well and the share key on the hero, side by side.</summary>
        const float CodeW = 470f, CodeH = 96f, ShareW = 300f, ShareH = 104f;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Right = new Vector2(1f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        // --------------------------------------------------------------- state
        ReferralTable _table;
        ReferralState _state;
        string _chapterName;

        Text _codeText, _tally;
        Btn _codeTap, _shareBtn;

        /// <summary>Which offer row the page was built with, so a repaint can notice it must rebuild.</summary>
        enum Offer { None, Code, Welcome }
        Offer _offer;
        int _rowCount;

        RowTile _welcome;
        readonly List<RowTile> _rows = new List<RowTile>();

        RectTransform _board;
        ScrollRect _scroll;
        float _boardH, _bandH;

        /// <summary>True while a chest is being asked for or handed over; a rebuild underneath would destroy the tiles.</summary>
        bool _collecting;

        /// <summary>The reels, so the first tap on a lit row finds its lid already loaded (7b).</summary>
        AssetHold _reels;

        /// <summary>The pieces of one row a repaint writes to.</summary>
        sealed class RowTile
        {
            public int Goal;
            public ReferralClaimKind Kind;
            public ReferralPayment Payment;
            public RectTransform Root;
            public Image Card, Icon, Pool, Rim;
            public RectTransform Seat, Collect, Mark, Seal;
            public Text Title, Sub, MarkText, Count;
            public Btn Tap;
            public CanvasGroup Group;
            public bool Lit;
        }

        // ---------------------------------------------------------------- build
        protected override void Build()
        {
            _table = ReferralLedger.Table;
            _state = ReferralLedger.State;
            _rows.Clear();
            _welcome = null;

            var chapter = GameContent.Index?.FindChapter(_table.Milestone);
            _chapterName = chapter != null ? Loc.Get(chapter.NameKey) : _table.Milestone.Value;

            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            _offer = OfferShape;
            _rowCount = RowCount;

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);
            y = BuildOffer(y);
            y = BuildHeading(y);
            BuildBoard(y);

            NavBar.Build(Content, NavBar.Tab.Profile);
            HoldReels();

            Repaint();
            FocusOnPending();

            // The cache paints first and the server replaces it. Nothing waits on this: a
            // failed read leaves the cached page standing, which is what a cache is for.
            Run(async token => { await ReferralLedger.RefreshAsync(token); });
        }

        void OnEnable() { ReferralLedger.Changed += OnChanged; }
        void OnDisable() { ReferralLedger.Changed -= OnChanged; }

        void OnDestroy()
        {
            _reels?.Dispose();
            _reels = null;
        }

        void HoldReels() => Run(async token =>
        {
            _reels = _reels ?? AssetLibrary.Hold("chests");
            await _reels.LoadAsync(AssetManifest.ChestAssets(ProgressionRules.Table.Tasks), null, token);
        });

        /// <summary>
        /// Redraws on any change the page did not make itself. A payout raises this too and
        /// must not act on it: the page is halfway through a ceremony that already shows the
        /// new state.
        /// </summary>
        void OnChanged()
        {
            if (this == null || Content == null || _collecting) return;

            if (OfferShape != _offer || RowCount != _rowCount) { Rebuild(); return; }
            Repaint();
        }

        void Rebuild()
        {
            if (Content == null) return;

            ClearContent();

            _codeText = _tally = null;
            _codeTap = _shareBtn = null;
            _board = null;
            _scroll = null;

            Build();
        }

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Which offer row the page draws.
        ///
        /// Before the server has ever answered on this device, the device's own ledger decides
        /// whether a code could still be typed — a veteran is shown nothing rather than a row
        /// the server will refuse. Once the server has answered, its word governs.
        /// </summary>
        Offer OfferShape
        {
            get
            {
                var state = ReferralLedger.State;
                if (state.Referred) return Offer.Welcome;

                bool could = state.IsKnown ? state.CanRedeem : !ReferralLedger.MilestoneClearedHere;
                return could ? Offer.Code : Offer.None;
            }
        }

        /// <summary>
        /// Every friend the cap allows, always — the whole board, at the owner's instruction
        /// (2026-09-20).
        ///
        /// <para>
        /// It used to draw the finished ones plus the next three, which is the right shape for
        /// a ladder whose rungs differ and the wrong one for this board: the payment is flat
        /// (invariant 51b), so the three rows a new player met said FRIEND 1, FRIEND 2,
        /// FRIEND 3 and nothing said there was a fourth. A list that ends at three <em>is</em>
        /// a cap of three to the person reading it, whatever the tally line above it says.
        /// </para>
        /// <para>
        /// <b>It costs no new maximum.</b> The board already built <see cref="ReferralTable.MaxBound"/>
        /// rows for anybody with that many finished friends, and it already scrolls; what
        /// changes is when it does, not how big it can get.
        /// </para>
        /// </summary>
        int RowCount => Mathf.Max(1, _table.MaxBound);

        // --------------------------------------------------------------- header
        float BuildHeader(float y)
        {
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<ProfileScreen>());
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * ChromeSize,
                             new Vector2(1f, 1f), new Vector2(-76f, cy),
                             () => { if (!Flow.HasModal) Flow.Modal<ReferralInfoOverlay>(); });

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.referral.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            y += BannerH + 4f;

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Format("ui.referral.subtitle", _chapterName), 24,
                             new Color(.86f, .90f, 1f, .78f), TextAnchor.MiddleCenter,
                             new Vector2(880f, 32f), Top, new Vector2(0f, -(y + 16f)), 3f, 3f), 15);
            y += 32f + 14f;

            float py = -(y + ChromeSize * .5f);
            Pill(ResourceSlots.Kind.Hearts, -232f, py, Pal.Rose, Art.S("Ui/ic_heart"),
                 Profile.HeartsLabel(), v => Profile.HeartsLabel((int)v));
            Pill(ResourceSlots.Kind.Credits, 0f, py, Pal.Gold, null,
                 Compact.Number(Profile.Coins), v => Compact.Number(v));
            Pill(ResourceSlots.Kind.Gems, 232f, py, Pal.Bloom, Art.S("Ui/ic_gem"),
                 Compact.Number(Profile.Gems), v => Compact.Number(v));

            // All three, for the streak page's reason (44j): hearts move on a refill timer.
            WalletWatch.Attach(this, ResourceSlots.Kind.Hearts, ResourceSlots.Kind.Credits,
                               ResourceSlots.Kind.Gems);

            return y + ChromeSize + 16f;
        }

        void Pill(ResourceSlots.Kind kind, float x, float y, Color tint, Sprite icon,
                  string value, Func<long, string> format)
        {
            var bg = UIKit.Img("Pill", Safe, Art.S("Ui/" + Skins.Trough), Color.white,
                               new Vector2(212f, 78f), Top, new Vector2(x, y));

            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(96f, 96f), Left, new Vector2(52f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, icon, Color.white,
                               new Vector2(52f, 52f), Left, new Vector2(52f, 0f));
            ic.preserveAspect = true;
            if (icon == null) Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .05f, 2.4f, x * .01f);

            var text = UIKit.Titled("V", bg.transform, value, 30, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(112f, 44f), Centre, new Vector2(24f, 0f), 3f, 3f);

            ResourceSlots.Register(kind, (RectTransform)ic.transform, text, glow, tint, format);
        }

        // ----------------------------------------------------------------- hero
        /// <summary>
        /// The code, in the middle of the plate: the gift, the code in a well that copies on a
        /// tap, the key that shares it, and one line of what it has done so far.
        /// </summary>
        float BuildHero(float y)
        {
            var plate = UIKit.Img("Hero", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                  new Vector2(Width, HeroH), Top, new Vector2(0f, -(y + HeroH * .5f)));

            var clip = UIKit.Node("Clip", plate.transform);
            UIKit.StretchTo(clip, 6f, 6f, 6f, 6f);
            clip.gameObject.AddComponent<RectMask2D>();

            var rays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .14f),
                                 new Vector2(760f, 760f), Left, new Vector2(150f, 10f));
            Tween.Run(42f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      rays, "spin").Loop(-1, false);

            UIKit.Img("Glow", plate.transform, Art.Glow(128, 2f), Pal.A(Pal.Sun, .30f),
                      new Vector2(260f, 260f), Left, new Vector2(128f, 6f));

            var gift = UIKit.Img("Gift", plate.transform, Art.S("Ui/ic_gift"), Color.white,
                                 new Vector2(150f, 150f), Left, new Vector2(128f, 6f));
            gift.preserveAspect = true;
            Tween.Breathe(gift.transform, .05f, 2.6f);

            const float TextLeft = 236f;

            UIKit.Shrinkable(
                UIKit.Titled("CodeCap", plate.transform, Loc.Get("ui.referral.your_code").ToUpperInvariant(),
                             24, Pal.A(Pal.Cream, .78f), TextAnchor.MiddleLeft, new Vector2(CodeW, 30f), Left,
                             new Vector2(TextLeft + CodeW * .5f, 92f), 3f, 3f), 15);

            // The well the code sits in, and the whole well is the copy key: a tap on a code
            // is what everybody tries first, and a button that did nothing would be a broken
            // button (16o).
            var well = UIKit.Img("CodeWell", plate.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                                 new Vector2(CodeW, CodeH), Left, new Vector2(TextLeft + CodeW * .5f, 26f));

            _codeText = UIKit.Shrinkable(
                UIKit.Titled("Code", well.transform, string.Empty, 46, Pal.Gold, TextAnchor.MiddleCenter,
                             new Vector2(CodeW - 40f, 60f), Centre, Vector2.zero, 3f, 4f), 18);

            _codeTap = UIKit.Button("CopyTap", well.transform, Art.Pixel, new Vector2(CodeW, CodeH),
                                    Centre, Vector2.zero, CopyCode);
            _codeTap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            _codeTap.PressScale = .98f;

            _shareBtn = UIKit.TextButton("Share", plate.transform, Skins.Affirm, Loc.Get("ui.referral.share"), 34,
                                         new Vector2(ShareW, ShareH), Right, new Vector2(-176f, 26f),
                                         ShareCode, "ic_share");
            UIKit.OneLine(_shareBtn, 18);

            _tally = UIKit.Shrinkable(
                UIKit.Titled("Tally", plate.transform, string.Empty, 24, Pal.A(Pal.Cream, .84f),
                             TextAnchor.MiddleLeft, new Vector2(Width - TextLeft - 40f, 32f), Left,
                             new Vector2(TextLeft + (Width - TextLeft - 40f) * .5f, -72f), 3f, 3f), 15);

            return y + HeroH + 14f;
        }

        void CopyCode()
        {
            var state = ReferralLedger.State;
            if (state.Code.Length == 0)
            {
                Scenery.Toast(Content, Loc.Get("ui.referral.code_unknown"), Pal.Rose, 2.4f);
                return;
            }

            ShareSheet.Copy(ReferralCode.Display(state.Code));
            Audio.Sfx("click", .5f);
            Scenery.Toast(Content, Loc.Get("ui.referral.copied"), Pal.Mint, 1.8f);
        }

        void ShareCode()
        {
            var state = ReferralLedger.State;
            if (state.Code.Length == 0)
            {
                Scenery.Toast(Content, Loc.Get("ui.referral.code_unknown"), Pal.Rose, 2.4f);
                return;
            }

            Telemetry.Track("referral_shared", "bound", state.Bound, "finished", state.Finished);

            // The Editor, and any platform with no sheet, copies instead and says so — a
            // key that did nothing visible would read as broken.
            if (!ShareSheet.Share(ReferralLedger.ShareMessage, Loc.Get("ui.referral.share_title")))
                Scenery.Toast(Content, Loc.Get("ui.referral.copied"), Pal.Mint, 1.8f);
        }

        // ---------------------------------------------------------------- offer
        /// <summary>
        /// The row between the hero and the board: an invitation to type a friend's code, or
        /// the welcome chests a typed code earns — or nothing, for a player who can do neither.
        /// </summary>
        float BuildOffer(float y)
        {
            if (_offer == Offer.None) return y;

            float cy = -(y + OfferH * .5f);
            var plate = UIKit.Img("Offer", Safe, Art.S("Ui/" + Skins.PlateBlue), Color.white,
                                  new Vector2(Width, OfferH), Top, new Vector2(0f, cy));

            if (_offer == Offer.Code)
            {
                // On the seat's own column, so the two shapes this band can take start their
                // picture in the same place — `Furnish` puts the welcome row's well at SeatX.
                UIKit.Img("Glow", plate.transform, Art.Glow(128, 2f), Pal.A(Pal.Aqua, .24f),
                          new Vector2(286f, 286f), Left, new Vector2(SeatX, 0f));
                var key = UIKit.Img("Key", plate.transform, Art.S("Ui/ic_key"), Color.white,
                                    new Vector2(132f, 132f), Left, new Vector2(SeatX, 0f));
                key.preserveAspect = true;

                // The row's own text column, rather than a width of its own: the seat moved
                // right when the well grew, and a second figure would have kept the sentence
                // where it was and run it into the ENTER key.
                const float HintW = TextW;
                float hx = TextX + HintW * .5f;

                // **The hint was drawn at 14 and 23 was never possible**, which is invariant
                // 44l's tell exactly: a `Shrinkable` settling on its floor is a box too small
                // for its sentence, not a sentence that wanted to be small. This one wraps to
                // two lines at any size, and two lines of 23 is 55 units in a box 34 tall — so
                // Best Fit walked all the way down to the floor and drew the page's only
                // explanation of what a code is for at half the size of everything round it.
                //
                // **What buys the size is height, not width.** The column keeps `TextW`, which
                // is the row's own and is why the sentence starts where a friend's name starts;
                // 96 units takes three lines, and the sentence settles at 26 — measured through
                // `render_referral.py`'s mirror of Best Fit, not eyed. The floor goes 14 to 16
                // for the same reason it exists: a translation half again as long still has two
                // lines of room above the floor before it truncates.
                //
                // The heading rises to 54 so the pair stays centred on the band: 44 + 12 + 96
                // is 152 of a 196 plate, which is 22 of margin top and bottom.
                const float HintH = 96f;

                UIKit.Shrinkable(
                    UIKit.Titled("Title", plate.transform, Loc.Get("ui.referral.offer_title"), 36, Pal.Cream,
                                 TextAnchor.MiddleLeft, new Vector2(HintW, 44f), Left, new Vector2(hx, 54f), 3f, 3f), 20);
                UIKit.Shrinkable(
                    UIKit.Titled("Hint", plate.transform, Loc.Format("ui.referral.offer_hint", _chapterName), 26,
                                 Pal.A(Pal.Cream, .82f), TextAnchor.MiddleLeft, new Vector2(HintW, HintH), Left,
                                 new Vector2(hx, -28f), 3f, 0f), 16);

                var enter = UIKit.TextButton("Enter", plate.transform, Skins.Alternate, Loc.Get("ui.referral.enter"), 30,
                                             new Vector2(272f, 104f), Right, new Vector2(-150f, 0f), EnterCode);
                UIKit.OneLine(enter, 16);
                return y + OfferH + 14f;
            }

            // The welcome chests, as a row of the same shape as a friend's. Built on the plate
            // rather than in the board so it never scrolls away: it is the one thing on the
            // page an invitee came for.
            var tile = new RowTile
            {
                Goal = 0,
                Kind = ReferralClaimKind.Invitee,
                Payment = _table.Invitee,
                Root = (RectTransform)plate.transform,
                Card = plate,
            };
            _welcome = tile;
            tile.Group = UIKit.Group(tile.Root);

            tile.Pool = UIKit.Img("Light", Safe, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                  new Vector2(Width + 150f, OfferH + 130f), Top, new Vector2(0f, cy));
            tile.Pool.transform.SetSiblingIndex(plate.transform.GetSiblingIndex());

            Furnish(tile, Loc.Get("ui.referral.welcome_title").ToUpperInvariant());

            return y + OfferH + 14f;
        }

        void EnterCode()
        {
            if (Flow.HasModal) return;
            Flow.Modal<ReferralCodeOverlay>(v => v.OnRedeemed = () =>
            {
                if (this == null || Content == null) return;
                Scenery.Toast(Content, Loc.Format("ui.referral.redeem_bound", _chapterName), Pal.Mint, 3.2f);
            });
        }

        // -------------------------------------------------------------- heading
        float BuildHeading(float y)
        {
            float cy = -(y + HeadingH * .5f);

            UIKit.Shrinkable(
                UIKit.Titled("H", Safe, Loc.Format("ui.referral.heading", _chapterName).ToUpperInvariant(),
                             28, Pal.Gold, TextAnchor.MiddleLeft, new Vector2(Width - 16f, 38f), Top,
                             new Vector2(0f, cy), 3f, 3f), 17);

            return y + HeadingH;
        }

        // ---------------------------------------------------------------- board
        void BuildBoard(float top)
        {
            float bottom = NavBar.Height + 20f;

            var band = UIKit.Node("Board", Safe);
            UIKit.StretchTo(band, 0f, bottom, 0f, top);
            band.gameObject.AddComponent<RectMask2D>();

            _bandH = Mathf.Max(0f, Flow.Size.y - top - bottom);
            _boardH = _rowCount * (RowH + RowGap) - RowGap;

            var rows = UIKit.Node("Rows", band);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(.5f, 1f);
            rows.sizeDelta = new Vector2(0f, _boardH);
            rows.anchoredPosition = Vector2.zero;
            _board = rows;

            // Every lit row's pool of light, under every card. The streak's rule (48i).
            var lights = UIKit.Node("Lights", rows);
            UIKit.StretchTo(lights, 0f, 0f, 0f, 0f);

            for (int i = 0; i < _rowCount; i++)
                Row(rows, lights, i + 1, i * (RowH + RowGap), i);

            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            _scroll = band.gameObject.AddComponent<ScrollRect>();
            _scroll.content = rows;
            _scroll.viewport = band;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = .14f;
            _scroll.inertia = true;
            _scroll.decelerationRate = .04f;
            _scroll.scrollSensitivity = 55f;
        }

        /// <summary>Opens the board on the row that can be taken, for the streak's reason.</summary>
        void FocusOnPending()
        {
            if (_board == null || _boardH <= _bandH) return;

            int friend = ReferralLedger.FirstClaimableFriend;
            if (friend <= 0 || friend > _rowCount) return;

            float rowTop = (friend - 1) * (RowH + RowGap);
            float want = Mathf.Clamp(rowTop - (_bandH - RowH) * .5f, 0f, _boardH - _bandH);
            _board.anchoredPosition = new Vector2(0f, want);
        }

        void Row(RectTransform parent, RectTransform lights, int friend, float top, int index)
        {
            var tile = new RowTile { Goal = friend, Kind = ReferralClaimKind.Rung, Payment = _table.PerInvitee };
            _rows.Add(tile);

            float cy = -(top + RowH * .5f);

            tile.Pool = UIKit.Img("Light_" + friend, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                  new Vector2(Width + 150f, RowH + 130f), Top, new Vector2(0f, cy));

            // `Skins.PlateNavy`, which is what every reward row in this game is drawn on — the
            // tasks page, the streak board and the season ladder. This board was the one left
            // on `Skins.Card`, and the difference is not a shade: a card is a *container*, flat
            // and unlit with nothing at its edge but a keyline, and at the .74 an unreached row
            // is faded to it reads as a hole with the wall showing through. The plate carries
            // the lit top edge and the two-tone face that make a row read as a thing holding a
            // prize. One name, re-cut once (invariant 44).
            var card = UIKit.Img("F" + friend, parent, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                 new Vector2(Width, RowH), Top, new Vector2(0f, cy));
            tile.Card = card;
            tile.Root = (RectTransform)card.transform;
            tile.Group = UIKit.Group(tile.Root);

            Furnish(tile, Loc.Format("ui.referral.friend_n", friend).ToUpperInvariant());

            tile.Root.localScale = Vector3.zero;
            Tween.Pop(tile.Root, 0f, .46f, .20f + Mathf.Min(index, 8) * .04f);
        }

        /// <summary>
        /// The furniture every reward row shares: the well and the chest at the left with the
        /// count on its corner, two lines in the middle, and the three answers at the right of
        /// which one shows.
        /// </summary>
        void Furnish(RowTile tile, string title)
        {
            var seat = UIKit.Img("Seat", tile.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 new Vector2(SeatSize, SeatSize), Left, new Vector2(SeatX, 0f));
            tile.Seat = (RectTransform)seat.transform;

            // Sized and placed in *drawn* units through ChestPack (48j): the closed icon is
            // frame nought of the reel and carries the lid's headroom.
            float tall = RewardTall;
            var box = new Vector2(tall / ChestPack.Fill * ChestPack.Aspect, tall / ChestPack.Fill);
            var chest = UIKit.Img("Chest", tile.Root, Art.S(tile.Payment.Tier.Icon), Color.white,
                                  box, Left, new Vector2(SeatX, tall * ChestPack.Lift));
            chest.preserveAspect = true;
            tile.Icon = chest;

            // The count, on the well's corner, drawn only when it is more than one: "x2" on a
            // single chest is a number that says nothing.
            if (tile.Payment.Count > 1)
            {
                var badge = UIKit.Img("Count", tile.Root, Art.Disc(64), Pal.Gold,
                                      new Vector2(54f, 54f), Left,
                                      new Vector2(SeatX + SeatSize * .5f - 14f, -SeatSize * .5f + 16f));
                tile.Count = UIKit.Titled("N", badge.transform, "x" + tile.Payment.Count, 27,
                                          new Color(.30f, .20f, .05f), TextAnchor.MiddleCenter, outline: 0f, shadow: 0f);
            }

            tile.Title = UIKit.Shrinkable(
                UIKit.Titled("Title", tile.Root, title, 32, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(TextW, 44f), Left, new Vector2(TextX + TextW * .5f, 32f), 3f, 3f), 18);

            tile.Sub = UIKit.Shrinkable(
                UIKit.Titled("Sub", tile.Root, string.Empty, 25, Pal.A(Pal.Cream, .84f), TextAnchor.MiddleLeft,
                             new Vector2(TextW, 36f), Left, new Vector2(TextX + TextW * .5f, -28f), 3f, 3f), 15);

            var collect = UIKit.Img("Collect", tile.Root, Art.S("Ui/" + Skins.Affirm), Color.white,
                                    new Vector2(KeyW, KeyH), Right, new Vector2(-130f, 0f));
            UIKit.Shrinkable(
                UIKit.Titled("CollectText", collect.transform, Loc.Get("ui.referral.collect").ToUpperInvariant(),
                             34, Pal.Cream, TextAnchor.MiddleCenter, new Vector2(KeyW - 36f, 52f), Centre,
                             new Vector2(0f, KeyH * UIKit.PillFaceLift), 4f, 4f), 20);
            tile.Collect = (RectTransform)collect.transform;
            tile.Collect.gameObject.SetActive(false);

            tile.MarkText = Scenery.Pill(tile.Root, string.Empty, MarkType, new Vector2(KeyW, MarkH),
                                         Right, new Vector2(-130f, 0f),
                                         new Color(.05f, .09f, .18f, .70f));
            tile.Mark = (RectTransform)tile.MarkText.transform.parent;
            tile.Mark.gameObject.SetActive(false);

            var seal = UIKit.Img("Seal", tile.Root, Art.S("Ui/seal_gold"), Color.white,
                                 new Vector2(84f, 84f), Right, new Vector2(-146f, 0f));
            seal.preserveAspect = true;
            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Pal.Cream,
                                 new Vector2(44f, 44f), Centre, Vector2.zero);
            tick.preserveAspect = true;
            tile.Seal = (RectTransform)seal.transform;
            tile.Seal.gameObject.SetActive(false);

            tile.Rim = UIKit.Img("Rim", tile.Root, Art.RoundOutline(30, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)tile.Rim.transform, 0f, 0f, 0f, 0f);

            // The whole row is the button and COLLECT is a label on it (the streak's rule).
            bool welcome = tile.Kind == ReferralClaimKind.Invitee;
            tile.Tap = UIKit.Button("Tap", tile.Root, Art.Pixel, new Vector2(Width, welcome ? OfferH : RowH),
                                    Centre, Vector2.zero, () => Take(tile));
            tile.Tap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            tile.Tap.ClickSfx = null;
            tile.Tap.PressScale = .98f;
            tile.Tap.gameObject.SetActive(false);
        }

        // -------------------------------------------------------------- painting
        /// <summary>Writes the cached state onto the hero and every row. No entrance and no rebuild.</summary>
        void Repaint()
        {
            if (this == null || Content == null) return;

            _state = ReferralLedger.State;

            if (_codeText)
            {
                bool known = _state.Code.Length > 0;
                _codeText.text = known ? ReferralCode.Display(_state.Code) : Loc.Get("ui.referral.code_unknown");
                _codeText.color = known ? Pal.Gold : Pal.A(Pal.Cream, .55f);
                _codeText.fontSize = known ? 46 : 24;
            }

            if (_tally)
                _tally.text = Loc.Format("ui.referral.tally", _state.Bound, _table.MaxBound,
                                         _state.Finished, _chapterName);

            if (_welcome != null) Paint(_welcome, ReferralLedger.InviteeClaimable);

            int lit = ReferralLedger.FirstClaimableFriend;
            foreach (var tile in _rows) Paint(tile, tile.Goal == lit);
        }

        /// <summary>
        /// One row's state, derived from the same facts for a friend's row and the welcome
        /// row: reached or not, how many of its chests are paid, and whether it is the one
        /// row that shines.
        /// </summary>
        void Paint(RowTile tile, bool lit)
        {
            if (tile == null || !tile.Root) return;

            bool welcome = tile.Kind == ReferralClaimKind.Invitee;
            int count = tile.Payment.Count;
            int paid = _state.PaidCount(tile.Kind, tile.Goal, count);
            bool reached = ReferralLedger.Reached(tile.Kind, tile.Goal);
            bool done = paid >= count;
            bool waiting = reached && !done;
            lit = lit && waiting;

            var (cleared, total) = welcome ? ReferralLedger.MilestoneProgress : (0, 0);
            bool clearedHere = welcome && total > 0 && cleared >= total;

            if (tile.Sub)
            {
                string pays = Loc.Format("ui.referral.pays", count, Loc.Get(tile.Payment.Tier.NameKey));
                if (welcome && !reached)
                    tile.Sub.text = Loc.Format("ui.referral.welcome_hint", _chapterName);
                else if (waiting && paid > 0)
                    tile.Sub.text = Loc.Format("ui.referral.opened", paid, count);
                else if (done && welcome)
                    tile.Sub.text = Loc.Format("ui.referral.welcome_done_hint", _chapterName);
                else
                    tile.Sub.text = pays;
            }

            // **The tasks page's rule, not the streak board's, and the difference is what this
            // board looks like on the account that matters.** Both were `ahead ? .74f : 1f`,
            // which works on the streak board because at most a night or two is ahead and the
            // rest of the list is solid beside it. Here *every* row is ahead until a friend
            // finishes — so a new player, who is the whole audience for an invite page, met a
            // board of ghosts with the wall showing through it. A task's row dims only when it
            // is **spent**, and that reads correctly at any mix: a paid row steps back, and
            // everything still owed is a solid card. What says "not yet" is the count on the
            // right and the cool tint on the reward, both of which are drawn either way.
            if (tile.Group) tile.Group.alpha = done ? .62f : 1f;
            if (tile.Tap) tile.Tap.gameObject.SetActive(waiting);
            if (tile.Collect) tile.Collect.gameObject.SetActive(waiting);
            if (tile.Seal) tile.Seal.gameObject.SetActive(done);
            if (tile.Mark) tile.Mark.gameObject.SetActive(!waiting && !done);

            if (tile.MarkText && tile.Mark.gameObject.activeSelf)
            {
                // The chapter cleared here and not yet on the server is the one state with a
                // sentence of its own: the next sync settles it, and saying "4/10" over a
                // finished chapter would read as the game having lost a level.
                if (welcome)
                {
                    tile.MarkText.text = clearedHere
                        ? Loc.Get("ui.referral.settling").ToUpperInvariant()
                        : Loc.Format("ui.referral.progress", cleared, total);
                    tile.MarkText.color = clearedHere ? Pal.Aqua : Pal.Cream;
                }
                else
                {
                    tile.MarkText.text = Loc.Format("ui.referral.progress",
                                                    Mathf.Min(_state.Finished, tile.Goal), tile.Goal);
                    tile.MarkText.color = Pal.Cream;
                }

                // Fitted from `MarkType` on every write, because the pill says a word in one
                // state and a pair of figures in another and neither is a fixed width.
                UIKit.OneLineLabel(tile.MarkText, MarkRoom, MarkType, MarkLeast);
            }

            if (tile.Title) tile.Title.color = lit ? Pal.Gold : done ? Pal.Mint : Pal.Cream;
            // Full colour only where there is something to take; the cool grey is the tasks
            // ladder's own tint for a chest that is not yours yet, and it is what carries the
            // "not reached" reading now the card itself no longer fades for it.
            if (tile.Icon)
                tile.Icon.color = done ? new Color(.90f, .94f, 1f, 1f)
                                : reached ? Color.white
                                : new Color(.78f, .82f, .90f, 1f);

            Shine(tile, lit);
        }

        static void Shine(RowTile tile, bool on)
        {
            if (tile.Lit == on) return;
            tile.Lit = on;

            if (tile.Pool) Tween.KillChannel(tile.Pool.transform, "holy");
            if (tile.Rim) Tween.KillChannel(tile.Rim.transform, "holy");

            if (!on)
            {
                if (tile.Pool) Tween.Tint(tile.Pool, Pal.A(Pal.Sun, 0f), .3f);
                if (tile.Rim) Tween.Tint(tile.Rim, Pal.A(Pal.Sun, 0f), .3f);
                return;
            }

            Tween.Run(1.8f, Ease.InOutSine, t =>
            {
                if (tile.Pool) tile.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.42f, .80f, t));
                if (tile.Rim) tile.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.55f, 1f, t));
            }, tile.Pool, "holy").Loop(-1, true);
        }

        // ------------------------------------------------------------ collecting
        /// <summary>
        /// Takes the next unpaid chest of a row: asks the server, then opens the ceremony on
        /// what it answered.
        ///
        /// <para>
        /// The ask needs the network, and that is said before it is tried (the offline
        /// messages rule). What the server answers is opened through
        /// <see cref="ChestClaim.ForReferral"/>, whose closure banks the drops at the start of
        /// the ceremony; a chest another device already took is repainted as taken and
        /// nothing is opened. A row paying two chests is left lit after the first, with its
        /// line saying one of two is opened, and the second tap takes the second.
        /// </para>
        /// </summary>
        void Take(RowTile tile)
        {
            if (_collecting || tile == null || Flow.HasModal) return;

            if (Net.Offline)
            {
                Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                return;
            }

            int index = ReferralLedger.NextIndex(tile.Kind, tile.Goal);
            if (index <= 0) { Repaint(); return; }

            _collecting = true;
            Audio.Sfx("collect", .6f);
            Tween.Punch(tile.Root, .12f, .30f);

            var kind = tile.Kind;
            int goal = tile.Goal;

            Run(async token =>
            {
                ReferralPayout payout;
                try { payout = await ReferralLedger.ClaimAsync(kind, goal, index, token); }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogException(e);
                    payout = null;
                }

                if (!Living) return;
                _collecting = false;

                if (payout != null && payout.Opens)
                {
                    if (tile.Icon) Burst.Sparks(tile.Icon.transform, Vector2.zero, Pal.Gold, 18, 320f, 26f, .6f);
                    Flow.Modal<ChestOverlay>(v => v.Claim = ChestClaim.ForReferral(payout.Tier, payout.Land));
                    Repaint();
                    return;
                }

                switch (payout?.Outcome ?? ReferralClaimOutcome.Unavailable)
                {
                    case ReferralClaimOutcome.AlreadyPaid:
                        // Banked elsewhere. The state was adopted by the ledger; the seal says it.
                        Repaint();
                        break;

                    case ReferralClaimOutcome.NotYet:
                        Scenery.Toast(Content, Loc.Get("ui.referral.not_yet"), Pal.Gold, 3f);
                        Repaint();
                        break;

                    case ReferralClaimOutcome.Unknown:
                        Scenery.Toast(Content, Loc.Get("ui.referral.unknown_rung"), Pal.Rose, 3f);
                        break;

                    default:
                        Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                        break;
                }
            });
        }

        public override bool OnBack() { Flow.Go<ProfileScreen>(); return true; }
    }
}
