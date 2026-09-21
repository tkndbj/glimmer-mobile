using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
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
    /// <b>The board is a <see cref="GridView"/>, and that is the whole of why this page stopped
    /// reloading.</b> It drew every row the cap allows — fifty, the owner's instruction of
    /// 2026-09-20 — as fifty built subtrees, and threw the lot away whenever the offer band
    /// changed shape, which replayed the staggered entrance on all of them and lost the scroll.
    /// That is the exact fault <c>GridView</c> was written for and already describes in its own
    /// words. Here the board keeps only the rows that fit on the glass, a redraw is
    /// <see cref="GridView.Refresh"/> (the same cells rebound, no entrance, no jump), and the
    /// entrance is spent once on <see cref="GridView.Show"/>.
    /// </para>
    /// <para>
    /// <b>The offer band is the one thing that changes shape, so it is the only thing rebuilt.</b>
    /// It has three shapes — type a code, the welcome chests, or nothing — and it sits in a
    /// band of its own between the hero and the heading. When it changes,
    /// <see cref="RebuildOffer"/> redraws that band and <see cref="LayoutBelowOffer"/> slides
    /// the heading and the board's viewport; the board itself is not touched, keeps its cells
    /// and keeps the player's place. Nothing else on this page can change shape, so nothing
    /// else can force a restage — and <see cref="Restage"/> is left for the one case that
    /// genuinely is a different page, a content push that retunes the cap or the tiers.
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
        /// <para>
        /// <see cref="RowGap"/> is not a margin any more: a <see cref="GridView"/> cell is
        /// <c>RowH + RowGap</c> tall and the card is centred in it, which puts half the gap
        /// above and half below every card and needs no special case for the last one.
        /// </para>
        /// </summary>
        const float RowH = 196f;
        const float RowGap = 12f;
        const float CellH = RowH + RowGap;

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

        /// <summary>The gap under the offer band, and the room the board leaves the nav bar.</summary>
        const float OfferGap = 14f, BoardFoot = 20f;

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

        /// <summary>Which offer row the page is drawn with. See <see cref="OfferShape"/>.</summary>
        enum Offer { None, Code, Welcome }
        Offer _offer;

        /// <summary>The band the offer row lives in, rebuilt on its own, and what it costs in height.</summary>
        RectTransform _offerBand;
        float _offerTop, _offerHeight;

        RectTransform _heading, _viewport;
        GridView _grid;

        /// <summary>The welcome chests, when the offer band is drawing them. Null otherwise.</summary>
        RowWidgets _welcome;

        /// <summary>
        /// True from the tap on a row until the ceremony it opened is standing in front of the
        /// player.
        ///
        /// <para>
        /// <b>It has to outlast the claim call, and it did not.</b> It was cleared before
        /// <c>Flow.Modal</c> was called, and <see cref="ChestOverlay"/> runs its claim inside
        /// its own <c>Build</c> — so <c>payout.Land()</c> adopted the new state, raised
        /// <see cref="OnChanged"/>, and found the guard already down, on the frame the chest
        /// was opening. It is cleared after the panel is up and the page has caught up.
        /// </para>
        /// </summary>
        bool _collecting;

        /// <summary>
        /// True while <see cref="Restage"/> is drawing the whole page again.
        ///
        /// <para>
        /// It suppresses the entrance — the page is already in front of the player — and it is
        /// the re-entrancy guard: <c>Build</c> asks the server, an answer can arrive on an
        /// already-completed task, and <see cref="OnChanged"/> would otherwise restage a page
        /// that is halfway through being restaged. Same distinction <c>ModalView.Rebuilding</c>
        /// draws for a panel.
        /// </para>
        /// </summary>
        bool _restaging;

        /// <summary>The reels, so the first tap on a lit row finds its lid already loaded (7b).</summary>
        AssetHold _reels;

        // ----------------------------------------------------------------- rows
        /// <summary>
        /// The furniture one reward row is made of, whether it is a friend's row in the board
        /// or the welcome chests on the offer band.
        ///
        /// <para>
        /// <b>Every field here is written by <see cref="Paint"/> on every bind</b>, and that is
        /// a requirement rather than a tidiness: a <see cref="GridView"/> cell is recycled, so
        /// the row a player scrolls onto is a row that was drawing somebody else a moment ago.
        /// Anything a state leaves alone is the previous row's answer showing through — which
        /// is invariant 48l's fault ("a repaint is a drawing of a state, so anything a one-off
        /// path switches off it has to switch back on") with recycling added on top.
        /// </para>
        /// </summary>
        sealed class RowWidgets
        {
            /// <summary>The card. What is punched on a tap and what dims when the row is spent.</summary>
            public RectTransform Root;

            /// <summary>
            /// The node whose sibling order carries this row's halo, or null when nothing needs
            /// reordering.
            ///
            /// <para>
            /// <see cref="Pool"/> is 150 units wider than the card and 130 taller, so it spills
            /// over whatever is drawn beside it. In the board that is the neighbouring cells,
            /// and cells are recycled in no particular order — so the lit one is sunk to the
            /// bottom of the sibling list and its halo passes under its neighbours' plates.
            /// On the offer band there is nothing to sink: the welcome row's pool is built
            /// before its plate and is already behind it.
            /// </para>
            /// </summary>
            public RectTransform Sink;

            public Image Card, Icon, Pool, Rim;
            public RectTransform Seat, Collect, Mark, Seal;
            public Text Title, Sub, MarkText, Count;
            public Btn Tap;
            public CanvasGroup Group;

            /// <summary>Which payment this is currently drawing. Read by the tap, so it can never go stale.</summary>
            public ReferralClaimKind Kind;
            public int Goal;

            /// <summary>Whether the shine loop is running, and for which row it was started.</summary>
            public bool Lit;
            public int LitGoal = -1;
        }

        /// <summary>
        /// One friend's row in the board.
        ///
        /// <para>
        /// Built once and bound many times. It holds no friend number of its own beyond
        /// <see cref="RowWidgets.Goal"/>, which <see cref="Bind"/> rewrites — so the tap
        /// handler asks the widgets what they are drawing rather than closing over a number
        /// that recycling would make a lie.
        /// </para>
        /// </summary>
        sealed class FriendCell : IGridCell
        {
            readonly ReferralScreen _screen;
            readonly RowWidgets _w;

            public RectTransform Root { get; }

            public FriendCell(ReferralScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Friend", parent);
                Root.sizeDelta = new Vector2(Width, CellH);

                // The pool first, so it is behind the plate it haloes — a light drawn over an
                // opaque plate is what invariant 48i was bought by, from the other direction.
                var pool = UIKit.Img("Light", Root, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                     new Vector2(Width + 150f, RowH + 130f), Centre, Vector2.zero);

                // `Skins.PlateNavy`, which is what every reward row in this game is drawn on —
                // the tasks page, the streak board and the season ladder. This board was the one
                // left on `Skins.Card`, and the difference is not a shade: a card is a
                // *container*, flat and unlit with nothing at its edge but a keyline, and at the
                // .74 an unreached row was faded to it read as a hole with the wall showing
                // through. The plate carries the lit top edge and the two-tone face that make a
                // row read as a thing holding a prize. One name, re-cut once (invariant 44).
                var card = UIKit.Img("Card", Root, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                     new Vector2(Width, RowH), Centre, Vector2.zero);

                _w = screen.Furnish((RectTransform)card.transform, Width, RowH);
                _w.Card = card;
                _w.Pool = pool;
                _w.Sink = Root;
            }

            public void Bind(int index)
            {
                int friend = index + 1;
                bool moved = _w.Goal != friend;

                _w.Kind = ReferralClaimKind.Rung;
                _w.Goal = friend;

                if (_w.Title)
                    _w.Title.text = Loc.Format("ui.referral.friend_n", friend).ToUpperInvariant();

                _screen.Paint(_w, _screen._table.PerInvitee,
                              lit: friend == ReferralLedger.FirstClaimableFriend,
                              instant: moved);
            }
        }

        // ---------------------------------------------------------------- build
        protected override void Build()
        {
            _table = ReferralLedger.Table;
            _state = ReferralLedger.State;
            _welcome = null;

            var chapter = GameContent.Index?.FindChapter(_table.Milestone);
            _chapterName = chapter != null ? Loc.Get(chapter.NameKey) : _table.Milestone.Value;

            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);

            // Everything below here moves when the offer band changes shape, so the cursor
            // stops at the band and `LayoutBelowOffer` takes over.
            _offerTop = y;

            _offerBand = UIKit.Node("OfferBand", Safe);
            _offerBand.anchorMin = new Vector2(.5f, 1f);
            _offerBand.anchorMax = new Vector2(.5f, 1f);
            _offerBand.pivot = new Vector2(.5f, 1f);
            _offerBand.sizeDelta = new Vector2(Width, 0f);

            BuildOffer();
            BuildHeading();
            BuildBoard();
            LayoutBelowOffer();

            NavBar.Build(Content, NavBar.Tab.Profile);
            HoldReels();

            // The entrance belongs to a first draw. A restage is the same page in a new state.
            _grid.Show(RowCount, animate: !_restaging);
            OpenOnPending();

            Repaint();

            // The cache paints first and the server replaces it. Nothing waits on this: a
            // failed read leaves the cached page standing, which is what a cache is for. The
            // watch asks once on attach and then keeps asking for as long as the page stands,
            // which is the only way a friend finishing reaches a screen that is already open.
            ReferralWatch.Attach(this);
        }

        void OnEnable()
        {
            ReferralLedger.Changed += OnChanged;

            // The row count, the tiers a row pays and the milestone chapter are all content
            // (invariant 4), and content can be replaced under a standing screen by a remote
            // push. `ShopScreen` watches the same event for the same reason.
            ProgressionRules.Changed += OnChanged;

            // The welcome row's progress line and `MilestoneClearedHere` are read off *this*
            // device's save, so a cloud merge that brings in levels cleared on another phone
            // moves what this page says without any referral call happening at all.
            PlayerProgress.Reloaded += OnChanged;
        }

        void OnDisable()
        {
            ReferralLedger.Changed -= OnChanged;
            ProgressionRules.Changed -= OnChanged;
            PlayerProgress.Reloaded -= OnChanged;
        }

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
        /// Anything the page did not do itself: the ledger adopting a new answer, a remote
        /// content push, or a cloud merge bringing in levels cleared on another device. All
        /// three are the same question — is this still the page it was — so all three go to
        /// <see cref="Settle"/>.
        /// </summary>
        void OnChanged()
        {
            if (this == null || Content == null || !Living) return;

            // A chest is being taken: the ceremony is standing on this page and already shows
            // the state that raised this. `Take` asks the same question when it lets go.
            if (_collecting || _restaging) return;

            Settle();
        }

        /// <summary>
        /// Brings the page up to date with whatever the ledger now holds, at the smallest scale
        /// that can be right.
        ///
        /// <para>
        /// Three scales, and the page nearly always lands on the cheapest. A content push is a
        /// different page and restages it. A change of offer shape rebuilds one band and slides
        /// what is under it, leaving the board alone. Everything else — a friend finishing, a
        /// chest paid, a code minted — is a repaint of the handful of rows on the glass.
        /// </para>
        /// <para>
        /// <b>Every path that changes the state ends here</b> rather than choosing for itself,
        /// which is why this is a method and not two lines inside <see cref="OnChanged"/>: a
        /// change arriving while a chest is being taken is deliberately ignored, so the collect
        /// has to ask the same question when it lets go or a shape that moved under the ceremony
        /// is never drawn.
        /// </para>
        /// </summary>
        void Settle()
        {
            if (this == null || Content == null || _restaging) return;

            if (ContentMoved) { Restage(); return; }

            if (OfferShape != _offer)
            {
                RebuildOffer();
                LayoutBelowOffer();
            }

            Repaint();
        }

        /// <summary>
        /// Draws the whole page again, for the one case that really is a different page: the
        /// content table has been replaced, so the cap, the tiers or the milestone chapter may
        /// all have moved.
        ///
        /// <para>
        /// It opens at the top rather than keeping the player's place, which is
        /// <see cref="GridView.Show"/>'s own rule and right here for its own reason — a board
        /// whose length has just changed is not a board somebody still has a place in.
        /// </para>
        /// </summary>
        void Restage()
        {
            if (Content == null || _restaging) return;

            _restaging = true;
            try
            {
                // `ClearContent` drops the base class's cached safe-area layer, which is the
                // half four hand-written copies of this loop all forgot. What is left here is
                // this screen's own handles, cleared rather than left for `Build` to overwrite
                // because not every one of them is written on every path.
                ClearContent();

                _codeText = _tally = null;
                _offerBand = _heading = _viewport = null;
                _grid = null;
                _welcome = null;

                Build();
            }
            finally
            {
                _restaging = false;
            }
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
        /// <b>And it costs nothing now.</b> When this was fifty built subtrees the count was a
        /// real bill; on a <see cref="GridView"/> the page holds the rows that fit on the glass
        /// whether the cap is fifty or five hundred.
        /// </para>
        /// </summary>
        int RowCount => Mathf.Max(1, _table.MaxBound);

        /// <summary>
        /// Whether the content this page was drawn from is still the content that is live.
        ///
        /// <para>
        /// <b>The comparison it replaces could never be true.</b> <c>OnChanged</c> asked
        /// <c>RowCount != _rowCount</c>, and both sides read <see cref="_table"/> — snapshotted
        /// at the top of <see cref="Build"/> — so the guard the comment described did not
        /// exist. A leftover from the shape this board had before 2026-09-20, when the count
        /// moved with the server's tally rather than with the cap.
        /// </para>
        /// <para>
        /// <b>Identity rather than the row count</b>, because the count is not the only thing
        /// on this table a page draws: the tier a row pays, how many chests it pays and which
        /// chapter the milestone is are all here too, and a retune of any of them leaves a
        /// standing page drawing the old one. <c>ProgressionRules.Table</c> is a field replaced
        /// whole by <c>Publish</c> and <c>Referral</c> is one instance hanging off it, so this
        /// is false exactly when a new table has been published and never otherwise — it cannot
        /// loop. A push that happens to carry an identical block costs one silent restage,
        /// which is the right way round to be wrong.
        /// </para>
        /// </summary>
        bool ContentMoved => !ReferenceEquals(_table, ReferralLedger.Table);

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
            if (!_restaging)
            {
                ribbon.transform.localScale = Vector3.zero;
                Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            }
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

            var codeTap = UIKit.Button("CopyTap", well.transform, Art.Pixel, new Vector2(CodeW, CodeH),
                                       Centre, Vector2.zero, CopyCode);
            codeTap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            codeTap.PressScale = .98f;

            var share = UIKit.TextButton("Share", plate.transform, Skins.Affirm, Loc.Get("ui.referral.share"), 34,
                                         new Vector2(ShareW, ShareH), Right, new Vector2(-176f, 26f),
                                         ShareCode, "ic_share");
            UIKit.OneLine(share, 18);

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
        /// Throws the offer band away and draws it for whatever shape the page is in now.
        ///
        /// <para>
        /// <b>Only this band, and that is the point.</b> It is the one thing on this page that
        /// can change shape while somebody is looking at it, so it is the only thing a change
        /// of shape is allowed to cost. The heading and the board slide down or up by whatever
        /// it now measures (<see cref="LayoutBelowOffer"/>) and the board keeps its cells, its
        /// scroll and its place.
        /// </para>
        /// </summary>
        void RebuildOffer()
        {
            if (_offerBand == null) return;

            for (int i = _offerBand.childCount - 1; i >= 0; i--)
            {
                var child = _offerBand.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _welcome = null;
            BuildOffer();
        }

        /// <summary>
        /// The row between the hero and the board: an invitation to type a friend's code, or
        /// the welcome chests a typed code earns — or nothing, for a player who can do neither.
        /// </summary>
        void BuildOffer()
        {
            _offer = OfferShape;
            _offerHeight = _offer == Offer.None ? 0f : OfferH + OfferGap;
            _offerBand.sizeDelta = new Vector2(Width, _offerHeight);

            if (_offer == Offer.None) return;

            float cy = -(OfferH * .5f);

            // The welcome row's pool of light, under the plate rather than on it. Built first
            // so it is the first sibling, for `Paint`'s reason.
            Image pool = null;
            if (_offer == Offer.Welcome)
                pool = UIKit.Img("Light", _offerBand, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(Width + 150f, OfferH + 130f), Top, new Vector2(0f, cy));

            var plate = UIKit.Img("Offer", _offerBand, Art.S("Ui/" + Skins.PlateBlue), Color.white,
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
                return;
            }

            // The welcome chests, as a row of the same shape as a friend's. Built on the band
            // rather than in the board so it never scrolls away: it is the one thing on the
            // page an invitee came for.
            _welcome = Furnish((RectTransform)plate.transform, Width, OfferH);
            _welcome.Card = plate;
            _welcome.Pool = pool;
            _welcome.Kind = ReferralClaimKind.Invitee;
            _welcome.Goal = 0;

            if (_welcome.Title)
                _welcome.Title.text = Loc.Get("ui.referral.welcome_title").ToUpperInvariant();
        }

        void EnterCode()
        {
            if (Flow.HasModal) return;
            Flow.Modal<ReferralCodeOverlay>(v => v.OnRedeemed = () =>
            {
                if (this == null || Content == null || !Living) return;
                Scenery.Toast(Content, Loc.Format("ui.referral.redeem_bound", _chapterName), Pal.Mint, 3.2f);
            });
        }

        // -------------------------------------------------------------- heading
        void BuildHeading()
        {
            _heading = (RectTransform)UIKit.Shrinkable(
                UIKit.Titled("H", Safe, Loc.Format("ui.referral.heading", _chapterName).ToUpperInvariant(),
                             28, Pal.Gold, TextAnchor.MiddleLeft, new Vector2(Width - 16f, 38f), Top,
                             Vector2.zero, 3f, 3f), 17).transform;
        }

        // ---------------------------------------------------------------- board
        void BuildBoard()
        {
            _viewport = UIKit.Node("Board", Safe);
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);

            // `padTop`/`padBottom` are nought: the gap between cards is half of `RowGap` at each
            // end of a cell, which needs no special case for the first or the last.
            _grid = GridView.Attach(_viewport, 1, Width, CellH,
                                    parent => new FriendCell(this, parent),
                                    padTop: 0f, padBottom: 0f);
        }

        /// <summary>
        /// Puts the band, the heading and the board where the offer's current height says they
        /// go. The only arithmetic on this page that runs more than once.
        /// </summary>
        void LayoutBelowOffer()
        {
            if (_offerBand == null) return;

            _offerBand.anchoredPosition = new Vector2(0f, -_offerTop);

            float y = _offerTop + _offerHeight;

            if (_heading)
            {
                _heading.anchorMin = _heading.anchorMax = Top;
                _heading.pivot = Centre;
                _heading.anchoredPosition = new Vector2(0f, -(y + HeadingH * .5f));
            }

            y += HeadingH;

            if (_viewport)
            {
                _viewport.offsetMin = new Vector2(0f, NavBar.Height + BoardFoot);
                _viewport.offsetMax = new Vector2(0f, -y);

                // The window just changed height under a list that has not changed at all, so
                // the grid is told in the same frame rather than noticing on its next one — a
                // frame late is a frame with a gap at the bottom of the board.
                _grid?.Relayout();
            }
        }

        /// <summary>
        /// Opens the board on the row that can be taken, for the streak page's reason: a list
        /// whose one actionable row is below the fold is a list that has hidden the only thing
        /// on it worth a tap.
        /// </summary>
        void OpenOnPending()
        {
            int friend = ReferralLedger.FirstClaimableFriend;
            if (friend >= 1 && friend <= RowCount) _grid?.ScrollTo(friend - 1);
        }

        // ------------------------------------------------------------ furnishing
        /// <summary>
        /// The furniture every reward row shares: the well and the chest at the left with the
        /// count on its corner, two lines in the middle, and the three answers at the right of
        /// which one shows.
        ///
        /// <para>
        /// It builds and never paints. Everything that depends on what the row is drawing is
        /// <see cref="Paint"/>'s, because this runs once per <em>cell</em> and that runs once
        /// per bind.
        /// </para>
        /// </summary>
        RowWidgets Furnish(RectTransform root, float width, float height)
        {
            var w = new RowWidgets { Root = root };
            w.Group = UIKit.Group(root);

            var seat = UIKit.Img("Seat", root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 new Vector2(SeatSize, SeatSize), Left, new Vector2(SeatX, 0f));
            w.Seat = (RectTransform)seat.transform;

            // Sized and placed in *drawn* units through ChestPack (48j): the closed icon is
            // frame nought of the reel and carries the lid's headroom.
            float tall = RewardTall;
            var box = new Vector2(tall / ChestPack.Fill * ChestPack.Aspect, tall / ChestPack.Fill);
            var chest = UIKit.Img("Chest", root, null, Color.white,
                                  box, Left, new Vector2(SeatX, tall * ChestPack.Lift));
            chest.preserveAspect = true;
            w.Icon = chest;

            // The count, on the well's corner. Built always and shown by `Paint`, because a
            // recycled cell cannot grow a widget it was not built with — and whether the count
            // is worth drawing is a property of the payment, which a retune can move.
            var badge = UIKit.Img("Count", root, Art.Disc(64), Pal.Gold,
                                  new Vector2(54f, 54f), Left,
                                  new Vector2(SeatX + SeatSize * .5f - 14f, -SeatSize * .5f + 16f));
            w.Count = UIKit.Titled("N", badge.transform, string.Empty, 27,
                                   new Color(.30f, .20f, .05f), TextAnchor.MiddleCenter, outline: 0f, shadow: 0f);

            w.Title = UIKit.Shrinkable(
                UIKit.Titled("Title", root, string.Empty, 32, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(TextW, 44f), Left, new Vector2(TextX + TextW * .5f, 32f), 3f, 3f), 18);

            w.Sub = UIKit.Shrinkable(
                UIKit.Titled("Sub", root, string.Empty, 25, Pal.A(Pal.Cream, .84f), TextAnchor.MiddleLeft,
                             new Vector2(TextW, 36f), Left, new Vector2(TextX + TextW * .5f, -28f), 3f, 3f), 15);

            var collect = UIKit.Img("Collect", root, Art.S("Ui/" + Skins.Affirm), Color.white,
                                    new Vector2(KeyW, KeyH), Right, new Vector2(-130f, 0f));
            UIKit.Shrinkable(
                UIKit.Titled("CollectText", collect.transform, Loc.Get("ui.referral.collect").ToUpperInvariant(),
                             34, Pal.Cream, TextAnchor.MiddleCenter, new Vector2(KeyW - 36f, 52f), Centre,
                             new Vector2(0f, KeyH * UIKit.PillFaceLift), 4f, 4f), 20);
            w.Collect = (RectTransform)collect.transform;

            w.MarkText = Scenery.Pill(root, string.Empty, MarkType, new Vector2(KeyW, MarkH),
                                      Right, new Vector2(-130f, 0f),
                                      new Color(.05f, .09f, .18f, .70f));
            w.Mark = (RectTransform)w.MarkText.transform.parent;

            var seal = UIKit.Img("Seal", root, Art.S("Ui/seal_gold"), Color.white,
                                 new Vector2(84f, 84f), Right, new Vector2(-146f, 0f));
            seal.preserveAspect = true;
            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Pal.Cream,
                                 new Vector2(44f, 44f), Centre, Vector2.zero);
            tick.preserveAspect = true;
            w.Seal = (RectTransform)seal.transform;

            w.Rim = UIKit.Img("Rim", root, Art.RoundOutline(30, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)w.Rim.transform, 0f, 0f, 0f, 0f);

            // The whole row is the button and COLLECT is a label on it (the streak's rule). It
            // reads the widgets rather than closing over a friend number, because a cell is
            // recycled and a captured number would be a lie the moment it scrolled.
            w.Tap = UIKit.Button("Tap", root, Art.Pixel, new Vector2(width, height),
                                 Centre, Vector2.zero, () => Take(w));
            w.Tap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            w.Tap.ClickSfx = null;
            w.Tap.PressScale = .98f;

            return w;
        }

        // -------------------------------------------------------------- painting
        /// <summary>
        /// Writes the cached state onto the hero, the welcome row and every row on the glass.
        ///
        /// <para>
        /// <see cref="GridView.Refresh"/> rebinds the cells that are realised and nothing else,
        /// so this is a handful of rows however long the board is. It used to be fifty, each
        /// running a Best Fit pass through <see cref="UIKit.OneLineLabel"/>, on every event the
        /// page listens to.
        /// </para>
        /// </summary>
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

            if (_welcome != null)
                Paint(_welcome, _table.Invitee, ReferralLedger.InviteeClaimable, instant: false);

            _grid?.Refresh();
        }

        /// <summary>
        /// One row's state, derived from the same facts for a friend's row and the welcome
        /// row: reached or not, how many of its chests are paid, and whether it is the one
        /// row that shines.
        ///
        /// <para>
        /// <paramref name="instant"/> says this row is drawing a different payment from the one
        /// it was drawing a moment ago — a recycled cell. A light that faded out over three
        /// tenths of a second would then be the *previous* friend's light going out on a row
        /// that has already become somebody else.
        /// </para>
        /// </summary>
        void Paint(RowWidgets w, ReferralPayment payment, bool lit, bool instant)
        {
            if (w == null || !w.Root) return;

            bool welcome = w.Kind == ReferralClaimKind.Invitee;
            int count = payment.Count;
            int paid = _state.PaidCount(w.Kind, w.Goal, count);
            bool reached = ReferralLedger.Reached(w.Kind, w.Goal);
            bool done = paid >= count;
            bool waiting = reached && !done;
            lit = lit && waiting;

            var (cleared, total) = welcome ? ReferralLedger.MilestoneProgress : (0, 0);
            bool clearedHere = welcome && total > 0 && cleared >= total;

            // The chest's picture is a property of the tier, and a tier is content: it is
            // written on every bind rather than at build time so a recycled cell — or a retune
            // — can never leave the previous tier's lid on this row (invariant 7b).
            if (w.Icon) w.Icon.sprite = payment.Tier != null ? Art.S(payment.Tier.Icon) : null;

            // "x2" on a single chest is a number that says nothing, so the badge is shown only
            // when the payment really pays more than one.
            if (w.Count)
            {
                var badge = w.Count.transform.parent as RectTransform;
                if (badge) badge.gameObject.SetActive(count > 1);
                w.Count.text = "x" + count;
            }

            if (w.Sub)
            {
                string pays = Loc.Format("ui.referral.pays", count,
                                         payment.Tier != null ? Loc.Get(payment.Tier.NameKey) : string.Empty);
                if (welcome && !reached)
                    w.Sub.text = Loc.Format("ui.referral.welcome_hint", _chapterName);
                else if (waiting && paid > 0)
                    w.Sub.text = Loc.Format("ui.referral.opened", paid, count);
                else if (done && welcome)
                    w.Sub.text = Loc.Format("ui.referral.welcome_done_hint", _chapterName);
                else
                    w.Sub.text = pays;
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
            if (w.Group) w.Group.alpha = done ? .62f : 1f;
            if (w.Tap) w.Tap.gameObject.SetActive(waiting);
            if (w.Collect) w.Collect.gameObject.SetActive(waiting);
            if (w.Seal) w.Seal.gameObject.SetActive(done);
            if (w.Mark) w.Mark.gameObject.SetActive(!waiting && !done);

            if (w.MarkText && w.Mark && w.Mark.gameObject.activeSelf)
            {
                // The chapter cleared here and not yet on the server is the one state with a
                // sentence of its own: the next sync settles it, and saying "4/10" over a
                // finished chapter would read as the game having lost a level.
                if (welcome)
                {
                    w.MarkText.text = clearedHere
                        ? Loc.Get("ui.referral.settling").ToUpperInvariant()
                        : Loc.Format("ui.referral.progress", cleared, total);
                    w.MarkText.color = clearedHere ? Pal.Aqua : Pal.Cream;
                }
                else
                {
                    w.MarkText.text = Loc.Format("ui.referral.progress",
                                                 Mathf.Min(_state.Finished, w.Goal), w.Goal);
                    w.MarkText.color = Pal.Cream;
                }

                // Fitted from `MarkType` on every write, because the pill says a word in one
                // state and a pair of figures in another and neither is a fixed width.
                UIKit.OneLineLabel(w.MarkText, MarkRoom, MarkType, MarkLeast);
            }

            if (w.Title) w.Title.color = lit ? Pal.Gold : done ? Pal.Mint : Pal.Cream;

            // Full colour only where there is something to take; the cool grey is the tasks
            // ladder's own tint for a chest that is not yours yet, and it is what carries the
            // "not reached" reading now the card itself no longer fades for it.
            if (w.Icon)
                w.Icon.color = done ? new Color(.90f, .94f, 1f, 1f)
                             : reached ? Color.white
                             : new Color(.78f, .82f, .90f, 1f);

            Shine(w, lit, instant);
        }

        /// <summary>
        /// The pool of light under the one row that can be taken, and the rim on it.
        ///
        /// <para>
        /// <b>The loop is keyed on the row it was started for</b>, which is what makes it
        /// survive recycling: a cell that scrolls off a lit row and back onto a dark one must
        /// stop shining, and a cell rebound to the same lit row must <em>not</em> restart — a
        /// breath that jumps back to its beginning on every repaint is the flicker this page
        /// was reported for, wearing different clothes.
        /// </para>
        /// <para>
        /// The lit row sinks to the bottom of the board's sibling order, because its pool is
        /// 130 units taller than the card and would otherwise be drawn over its neighbours.
        /// Only ever one row is lit (<see cref="ReferralLedger.FirstClaimableFriend"/>), so
        /// this costs one reorder and never fights itself.
        /// </para>
        /// </summary>
        void Shine(RowWidgets w, bool on, bool instant)
        {
            bool already = w.Lit && w.LitGoal == w.Goal;
            if (on && already) return;
            if (!on && !w.Lit) return;

            w.Lit = on;
            w.LitGoal = on ? w.Goal : -1;

            if (w.Pool) Tween.KillChannel(w.Pool.transform, "holy");
            if (w.Rim) Tween.KillChannel(w.Rim.transform, "holy");

            if (!on)
            {
                var clear = Pal.A(Pal.Sun, 0f);
                if (instant)
                {
                    if (w.Pool) w.Pool.color = clear;
                    if (w.Rim) w.Rim.color = clear;
                }
                else
                {
                    if (w.Pool) Tween.Tint(w.Pool, clear, .3f);
                    if (w.Rim) Tween.Tint(w.Rim, clear, .3f);
                }
                return;
            }

            if (w.Sink) w.Sink.SetAsFirstSibling();

            Tween.Run(1.8f, Ease.InOutSine, t =>
            {
                if (w.Pool) w.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.42f, .80f, t));
                if (w.Rim) w.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.55f, 1f, t));
            }, w.Rim, "holy").Loop(-1, true);
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
        /// <para>
        /// <b>The row is read off the widgets, and read again when the reply lands.</b> A cell
        /// is recycled, so by the time the server answers these widgets may be drawing a
        /// different friend — which is why the spark is only thrown if they are still drawing
        /// the one that was asked for.
        /// </para>
        /// </summary>
        void Take(RowWidgets w)
        {
            if (_collecting || w == null || !w.Root || Flow.HasModal) return;

            if (Net.Offline)
            {
                Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                return;
            }

            var kind = w.Kind;
            int goal = w.Goal;

            int index = ReferralLedger.NextIndex(kind, goal);
            if (index <= 0) { Repaint(); return; }

            _collecting = true;
            Audio.Sfx("collect", .6f);
            Tween.Punch(w.Root, .12f, .30f);

            Run(async token =>
            {
                ReferralPayout payout;
                try { payout = await ReferralLedger.ClaimAsync(kind, goal, index, token); }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogException(e);
                    payout = null;
                }

                // The screen is gone. The payout is dropped: its drops bank when the ledger's
                // own in-flight note is redeemed on the next tap (`ReferralLanding`).
                if (!Living) { _collecting = false; return; }

                // Held right through the ceremony being raised, because `ChestOverlay` runs its
                // claim inside its own `Build` — so `payout.Land` adopts the state and raises
                // `OnChanged` on this very frame.
                try
                {
                    if (payout != null && payout.Opens)
                    {
                        if (w.Root && w.Icon && w.Kind == kind && w.Goal == goal)
                            Burst.Sparks(w.Icon.transform, Vector2.zero, Pal.Gold, 18, 320f, 26f, .6f);

                        Flow.Modal<ChestOverlay>(v => v.Claim = ChestClaim.ForReferral(payout.Tier, payout.Land));
                        return;
                    }

                    switch (payout?.Outcome ?? ReferralClaimOutcome.Unavailable)
                    {
                        case ReferralClaimOutcome.AlreadyPaid:
                            // Banked elsewhere. The state was adopted by the ledger; the seal says it.
                            break;

                        case ReferralClaimOutcome.NotYet:
                            // The server's count has not reached this row. It is the one refusal
                            // that says the page was ahead of the truth, so take the truth: the
                            // claim adopted it, and the settle below draws the row as it is.
                            Scenery.Toast(Content, Loc.Get("ui.referral.not_yet"), Pal.Gold, 3f);
                            break;

                        case ReferralClaimOutcome.Unknown:
                            Scenery.Toast(Content, Loc.Get("ui.referral.unknown_rung"), Pal.Rose, 3f);
                            break;

                        default:
                            // Nothing was adopted, so nothing about the page has changed — but
                            // the row must come back out of its pressed state and go on offering
                            // the tap, which is what the settle below does.
                            Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                            break;
                    }
                }
                finally
                {
                    _collecting = false;

                    // `Settle` rather than `Repaint`: anything that arrived while the guard was
                    // down was skipped by `OnChanged`, and one of the things it could have been
                    // is a change of shape.
                    if (Living) Settle();
                }
            });
        }

        public override bool OnBack() { Flow.Go<ProfileScreen>(); return true; }
    }
}
