using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The welcome bonus: four turrets, each handed over after a few different days of one
    /// thing - a battle played, a task chest opened, a daily challenge won, a streak night
    /// collected (invariant 58; the owner, 2026-10-07).
    ///
    /// <para>
    /// <b>The page is the tasks page's page</b>, for the streak page's reason: the kit's plates
    /// on <see cref="Scenery.Plain"/>, a title ribbon, one sentence saying what the page is, and a
    /// list of reward rows - the turret in a seat on the left, what earns it and a box ticked
    /// per day collected in the middle, and the answer on the right. A quest, a task and a streak
    /// night are the same idea at three cadences and there was never a reason for them to be
    /// three designs (47g).
    /// </para>
    /// <para>
    /// <b>A row's state is the ledger's and nothing else</b> (<see cref="WelcomeLedger"/>): the
    /// days are a set the save carries, so a day played on another phone counts here the moment
    /// a sync lands, and the page repaints on <see cref="WelcomeLedger.Changed"/> rather than on
    /// a timer. Rows open in any order - every one is its own quest, so there is no floor to
    /// sweep (the owner's rule about chests, asked of turrets).
    /// </para>
    /// <para>
    /// <b>A finished row's boxes give way to a COLLECT key</b> (the owner, 2026-10-07), which
    /// opens <see cref="WelcomeChoiceOverlay"/>: the turret on every seat, or its shelf price.
    /// The turret's payoff is the loadout's own reveal (<see cref="WardRevealOverlay"/>, ending
    /// on EQUIP); the price's payoff is the wallet's flight to the one readout this page keeps,
    /// the credits pill in the header (44j: watched, never drawn once).
    /// </para>
    /// </summary>
    public sealed class WelcomeScreen : View
    {
        public override string Track => "mus_menu";

        // The stack, in canvas units from the top of the safe area.
        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float Width = 1000f;

        /// <summary>
        /// One quest is one row, the full width of the page, tall enough for the turret and two
        /// lines of large type (the owner, 2026-10-07: "bigger, hard to read"). The header above
        /// is as short as it can be - the ribbon, one sentence - so the rows start high and
        /// <see cref="WelcomeTable.MaxQuests"/> of them still stand above the nav bar on the
        /// squarest canvas with no scroll.
        /// </summary>
        const float RowH = 250f, RowGap = 14f;

        /// <summary>The seat the turret stands in at the left of its row, and how tall it is drawn.</summary>
        const float SeatSize = 148f, SeatX = 96f, BodyTall = 120f;

        /// <summary>Where the words start, and how much of the row they may have.</summary>
        const float TextX = 190f, TextW = 560f;

        /// <summary>
        /// A day box: the kit's inset slot with a tick in it once the day is in. Rows asking more
        /// than <see cref="MostBoxes"/> days draw a bar instead, because ten boxes is the width
        /// the column has.
        /// </summary>
        const float BoxSize = 48f, BoxGap = 8f, TickSize = 30f;
        const int MostBoxes = 10;
        const float BarW = 360f, BarH = 26f;

        /// <summary>The COLLECT key that stands where the boxes were once every day is in.</summary>
        static readonly Vector2 CollectSize = new Vector2(280f, 88f);

        /// <summary>The pill at the right end: days to go, or COLLECTED.</summary>
        static readonly Vector2 PillSize = new Vector2(232f, 76f);
        const float PillX = -136f;

        /// <summary>The credits readout at the header's right, narrower than the tasks page's so it clears the ribbon.</summary>
        static readonly Vector2 WalletSize = new Vector2(160f, 72f);
        const float WalletX = 444f;

        /// <summary>The radius the row's plate is rounded to; the rim and the mirror agree on it.</summary>
        const int CardRound = 30;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Right = new Vector2(1f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        static readonly Color BarOrange = new Color(1f, .59f, .12f);
        static readonly Color BarFull = new Color(.38f, .92f, .27f);
        static readonly Color BoxDim = new Color(1f, 1f, 1f, .55f);

        sealed class Row
        {
            public WelcomeQuest Quest;
            public RectTransform Root, Seat;
            public CanvasGroup Group;
            public Image Card, Body, Pool, Rim, Halo, Bar;
            public RectTransform Fill, Seal, Boxes;
            public Image[] Ticks;
            public Text Count, Pill;
            public Btn Collect;
            public bool Lit;
            public bool? Full;
        }

        readonly List<Row> _rows = new List<Row>();
        Text _subtitle;
        AssetHold _art;
        bool _claiming;

        protected override void Build()
        {
            _rows.Clear();

            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            float y = 22f;
            y = BuildHeader(y);
            BuildRows(y);

            NavBar.Build(Content, NavBar.Tab.Home);

            Repaint();
            Run(LoadArt);
        }

        void OnEnable() { WelcomeLedger.Changed += OnChanged; }
        void OnDisable() { WelcomeLedger.Changed -= OnChanged; }

        void OnDestroy()
        {
            _art?.Dispose();
            _art = null;
        }

        void OnChanged()
        {
            // Guarded because the event can arrive from a save load during teardown.
            if (this == null) return;
            Repaint();
        }

        // ---------------------------------------------------------------- header
        float BuildHeader(float y)
        {
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<HomeScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.welcome.title").Upper(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);

            // The one readout this page keeps: where the coins land when a quest is taken as its
            // price. Registered and watched (44j), never written by hand.
            BuildWallet(cy);
            y += BannerH + 4f;

            // Two lines allowed: the sentence says the one thing a player has to know - the
            // days need not be in a row - and a translation of it is longer than English. The
            // same slot says every turret is theirs once the last quest is taken (Repaint).
            _subtitle = UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Get("ui.welcome.subtitle"), 26, new Color(.86f, .90f, 1f, .82f),
                             TextAnchor.MiddleCenter, new Vector2(920f, 64f), Top,
                             new Vector2(0f, -(y + 32f)), 3f, 3f, wrap: true), 16);
            y += 64f + 10f;

            return y;
        }

        /// <summary>The credits pill, the tasks page's at a narrower cut (<c>TasksScreen.Pill</c>).</summary>
        void BuildWallet(float cy)
        {
            var bg = UIKit.Img("Wallet", Safe, Art.S("Ui/" + Skins.Trough), Color.white,
                               WalletSize, Top, new Vector2(WalletX, cy));

            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(Pal.Gold, .30f),
                                 new Vector2(88f, 88f), Left, new Vector2(44f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, null, Color.white,
                               new Vector2(48f, 48f), Left, new Vector2(44f, 0f));
            ic.preserveAspect = true;
            Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .05f, 2.4f);

            var text = UIKit.Shrinkable(
                UIKit.Titled("V", bg.transform, Compact.Number(Profile.Coins), 28, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(88f, 40f), Centre, new Vector2(26f, 0f), 3f, 3f), 16);

            ResourceSlots.Register(ResourceSlots.Kind.Credits, (RectTransform)ic.transform, text, glow, Pal.Gold,
                                   v => Compact.Number(v));
            WalletWatch.Attach(this, ResourceSlots.Kind.Credits);
        }

        // ------------------------------------------------------------------ rows
        void BuildRows(float y)
        {
            // The pools of light stand in a layer under every card, so a lit row's glow reaches
            // past its neighbours rather than being clipped by them - `TasksScreen.BuildSlates`.
            //
            // **Stretched nodes, not boxes placed at `y`.** Every row is positioned from the top
            // of its parent by `y` already; a parent that was itself a box standing `y` down put
            // every row twice as far down the page as the header - the "cards in the middle of
            // the screen" the owner photographed on 2026-10-07, which the render mirror, drawing
            // the arithmetic rather than the hierarchy, could not see.
            var lights = UIKit.Node("Lights", Safe);
            var list = UIKit.Node("Rows", Safe);

            var quests = ProgressionRules.Table.Welcome.Quests;
            for (int i = 0; i < quests.Count; i++)
            {
                BuildRow(list, lights, quests[i], y, i);
                y += RowH + RowGap;
            }
        }

        /// <summary>
        /// One quest, on the kit's card: the turret in a seat, what earns it, the day boxes and
        /// the answer. Everything that changes with state is written by <see cref="Paint"/>; this
        /// builds the furniture once, and every state's furniture, so a paint only switches.
        /// </summary>
        void BuildRow(RectTransform list, RectTransform lights, WelcomeQuest quest, float y, int index)
        {
            var row = new Row { Quest = quest };
            var tint = SiegeView.TintOf(quest.Colour);

            row.Pool = UIKit.Img("Light_" + quest.Id, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(Width + 150f, RowH + 130f), Top,
                                 new Vector2(0f, -(y + RowH * .5f)));
            row.Pool.raycastTarget = false;

            var card = UIKit.Img("Row_" + quest.Id, list, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                 new Vector2(Width, RowH), Top, new Vector2(0f, -(y + RowH * .5f)));
            row.Card = card;
            row.Root = (RectTransform)card.transform;
            row.Group = UIKit.Group(row.Root);

            // The turret, on the kit's inset seat, with its seat colour on the rim - the loadout
            // strip's own cell, one size up. Its picture arrives later (7b).
            var seat = UIKit.Img("Seat", row.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 new Vector2(SeatSize, SeatSize), Left, new Vector2(SeatX, 0f));
            row.Seat = (RectTransform)seat.transform;
            var rim = UIKit.Img("Rim", seat.transform, Art.RoundOutline(26, 5f), Pal.A(tint, .85f),
                                Vector2.one * (SeatSize - 10f));
            rim.raycastTarget = false;

            var halo = UIKit.Img("Halo", seat.transform, Art.Glow(128, 2f), Pal.A(tint, 0f),
                                 Vector2.one * (SeatSize * 1.9f), Centre, Vector2.zero);
            halo.raycastTarget = false;
            row.Halo = halo;

            var body = UIKit.Img("Body", seat.transform, null, Color.white,
                                 new Vector2(BodyTall * .8f, BodyTall), Centre, new Vector2(0f, 4f));
            body.preserveAspect = true;
            body.raycastTarget = false;
            body.enabled = false;
            row.Body = body;

            // The turret's name, then what earns it. Both large, both fitted to the column.
            UIKit.Shrinkable(
                UIKit.Titled("Title", row.Root, Loc.Get(quest.Ward.NameKey), 40, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(TextW, 50f), Left,
                             new Vector2(TextX + TextW * .5f, 78f), 3f, 3f), 22);

            UIKit.Shrinkable(
                UIKit.Titled("Sentence", row.Root, Loc.Format(quest.SentenceKey, quest.Days), 30,
                             Pal.A(Pal.Cream, .92f), TextAnchor.MiddleLeft, new Vector2(TextW, 40f), Left,
                             new Vector2(TextX + TextW * .5f, 28f), 3f, 3f), 18);

            // One box per day asked for, ticked as the days come in (the owner, 2026-10-07:
            // boxes with a checkmark, not dots). The kit's inset slot, so the box is the shelf's
            // own furniture; the tick is the seal's. A longer ladder draws a bar.
            row.Boxes = UIKit.Box("Boxes", row.Root, new Vector2(TextW, BoxSize), Left,
                                  new Vector2(TextX + TextW * .5f, -30f));
            if (quest.Days <= MostBoxes)
            {
                row.Ticks = new Image[quest.Days];
                for (int i = 0; i < quest.Days; i++)
                {
                    var box = UIKit.Img("B" + i, row.Boxes, Art.S("Ui/" + Skins.Slot), BoxDim,
                                        Vector2.one * BoxSize, Left,
                                        new Vector2(BoxSize * .5f + i * (BoxSize + BoxGap), 0f));
                    box.raycastTarget = false;
                    var tick = UIKit.Img("Tick", box.transform, Art.S("Ui/ic_check"), Pal.Mint,
                                         Vector2.one * TickSize, Centre, new Vector2(0f, 1f));
                    tick.preserveAspect = true;
                    tick.raycastTarget = false;
                    tick.enabled = false;
                    row.Ticks[i] = tick;
                }
            }
            else
            {
                var trough = UIKit.Img("Trough", row.Boxes, Art.S("Ui/" + Skins.Trough), Color.white,
                                       new Vector2(BarW, 30f), Left, new Vector2(BarW * .5f, 0f));
                var fill = UIKit.Img("Fill", trough.transform, Art.S("Ui/" + Skins.Fill), BarOrange,
                                     new Vector2(0f, BarH), Left, new Vector2(4f, 0f));
                row.Fill = (RectTransform)fill.transform;
                row.Fill.pivot = new Vector2(0f, .5f);
                row.Bar = fill;

                row.Count = UIKit.Shrinkable(
                    UIKit.Titled("Count", row.Boxes, string.Empty, 30, Pal.Cream, TextAnchor.MiddleLeft,
                                 new Vector2(TextW - BarW - 16f, 40f), Left,
                                 new Vector2(BarW + 16f + (TextW - BarW - 16f) * .5f, 0f), 3f, 3f), 16);
            }

            // The COLLECT key, where the boxes stood, once every day is in. Fitted to one line
            // so no translation can leave the pill (19n).
            row.Collect = UIKit.TextButton("Collect", row.Root, Skins.Affirm, Loc.Get("ui.chest.collect"), 34,
                                           CollectSize, Left, new Vector2(TextX + CollectSize.x * .5f, -30f),
                                           () => Collect(row));
            UIKit.OneLine(row.Collect, 18);
            row.Collect.gameObject.SetActive(false);

            // The answer at the right end while there is one: days to go, or COLLECTED.
            row.Pill = Scenery.Pill(row.Root, string.Empty, 28, PillSize, Right, new Vector2(PillX, 0f));
            UIKit.Shrinkable(row.Pill, 16);

            // The seal a taken quest wears on its seat, so it reads as stamped *on* the prize.
            var seal = UIKit.Img("Seal", row.Root, Art.Disc(96), Pal.Mint,
                                 new Vector2(64f, 64f), Left, new Vector2(SeatX + 54f, -46f));
            var sealTick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Color.white,
                                     new Vector2(38f, 38f), Centre, Vector2.zero);
            sealTick.preserveAspect = true;
            sealTick.raycastTarget = false;
            row.Seal = (RectTransform)seal.transform;
            row.Seal.gameObject.SetActive(false);

            // The rim, on the card's own edge and over everything on it - `TasksScreen.BuildRow`.
            row.Rim = UIKit.Img("Rim", row.Root, Art.RoundOutline(CardRound, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)row.Rim.transform, 0f, 0f, 0f, 0f);
            row.Rim.raycastTarget = false;

            _rows.Add(row);

            row.Root.localScale = Vector3.zero;
            Tween.Pop(row.Root, 0f, .46f, .18f + index * .06f);
        }

        // -------------------------------------------------------------- painting
        /// <summary>Writes the ledger onto every row and the header. No entrance and no rebuild.</summary>
        void Repaint()
        {
            foreach (var row in _rows) Paint(row);

            if (_subtitle)
            {
                bool done = WelcomeLedger.Live && WelcomeLedger.IsDone;
                _subtitle.text = Loc.Get(done ? "ui.welcome.all_done" : "ui.welcome.subtitle");
                _subtitle.color = done ? Pal.Gold : new Color(.86f, .90f, 1f, .82f);
            }
        }

        void Paint(Row row)
        {
            if (row == null || !row.Root) return;

            var quest = row.Quest;
            var state = WelcomeLedger.StateOf(quest);
            int done = WelcomeLedger.DaysDone(quest);
            bool ready = state == WelcomeState.Ready;
            bool claimed = state == WelcomeState.Claimed;
            bool full = done >= quest.Days;

            // The boxes while there are days to tick off or to show ticked; the key while the
            // quest waits to be taken.
            if (row.Boxes) row.Boxes.gameObject.SetActive(!ready);
            if (row.Collect) row.Collect.gameObject.SetActive(ready);

            if (row.Ticks != null)
            {
                for (int i = 0; i < row.Ticks.Length; i++)
                {
                    var tick = row.Ticks[i];
                    if (!tick) continue;
                    bool lit = i < done;
                    tick.enabled = lit;
                    var box = tick.transform.parent ? tick.transform.parent.GetComponent<Image>() : null;
                    if (box) box.color = lit ? Color.white : BoxDim;
                }
            }

            if (row.Fill)
            {
                float target = (BarW - 8f) * Mathf.Clamp01(done / (float)quest.Days);
                Tween.KillChannel(row.Fill, "bar");
                float from = row.Fill.sizeDelta.x;
                Tween.Run(.6f, Ease.OutCubic,
                          t => { if (row.Fill) row.Fill.sizeDelta = new Vector2(Mathf.Lerp(from, target, t), BarH); },
                          row.Fill, "bar");
            }

            if (row.Bar && row.Full != full)
            {
                var colour = full ? BarFull : BarOrange;
                if (row.Full == null) row.Bar.color = colour;
                else Tween.Tint(row.Bar, colour, .35f);
                row.Full = full;
            }

            if (row.Count) row.Count.text = Loc.Format("ui.tasks.fraction", done, quest.Days);

            // Exactly one answer at the right end, or none while the key is out.
            if (row.Pill)
            {
                if (claimed) row.Pill.text = Loc.Get("ui.welcome.taken").Upper();
                else
                {
                    int left = quest.Days - done;
                    row.Pill.text = left == 1 ? Loc.Get("ui.welcome.days_left_one")
                                              : Loc.Format("ui.welcome.days_left", left);
                }
                row.Pill.transform.parent.gameObject.SetActive(!ready);
            }

            if (row.Group) row.Group.alpha = claimed ? .62f : 1f;
            if (row.Seal) row.Seal.gameObject.SetActive(claimed);
            if (row.Body) row.Body.color = claimed ? new Color(.78f, .82f, .88f, 1f) : Color.white;

            // The light says "this one is yours to take", so it is drawn only where that is true,
            // and only starts on the paint that made it so - a breathe restarted on every repaint
            // is a turret that jumps each time a day lands.
            if (ready && !row.Lit)
            {
                if (row.Halo) Tween.Tint(row.Halo, Pal.A(SiegeView.TintOf(quest.Colour), .55f), .4f);
                if (row.Body)
                {
                    Tween.Breathe(row.Body.transform, .07f, 1.5f);
                    Sheen.Attach(row.Root, 2.8f);
                }
                Shine(row, true);
                row.Lit = true;
            }
            else if (!ready && row.Lit)
            {
                if (row.Halo) Tween.Tint(row.Halo, Pal.A(SiegeView.TintOf(quest.Colour), 0f), .3f);
                if (row.Body) Tween.KillChannel(row.Body.transform, "breathe");
                Shine(row, false);
                row.Lit = false;
            }
        }

        /// <summary>The pool and the rim a ready row stands in, breathing on one tween - `TasksScreen.Shine`.</summary>
        static void Shine(Row row, bool on)
        {
            if (row.Pool) Tween.KillChannel(row.Pool.transform, "holy");
            if (row.Rim) Tween.KillChannel(row.Rim.transform, "holy");

            if (!on)
            {
                if (row.Pool) Tween.Tint(row.Pool, Pal.A(Pal.Sun, 0f), .3f);
                if (row.Rim) Tween.Tint(row.Rim, Pal.A(Pal.Sun, 0f), .3f);
                return;
            }

            Tween.Run(1.8f, Ease.InOutSine, t =>
            {
                if (row.Pool) row.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.45f, .85f, t));
                if (row.Rim) row.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.55f, 1f, t));
            }, row.Pool, "holy").Loop(-1, true);
        }

        // ------------------------------------------------------------------- art
        /// <summary>Puts whichever turret pictures are in hand onto their seats.</summary>
        void Dress()
        {
            foreach (var row in _rows)
            {
                if (row.Body == null || row.Quest?.Ward == null) continue;
                var sprite = AssetLibrary.Sprite(AssetManifest.WardArt(row.Quest.Ward, row.Quest.Colour));
                row.Body.sprite = sprite;
                row.Body.enabled = sprite != null;
            }
        }

        /// <summary>This page's own hold on its turrets - never the hub's, never a board's (7b).</summary>
        async Task LoadArt(CancellationToken cancellation)
        {
            var wanted = new List<AssetRequest>(_rows.Count);
            foreach (var row in _rows)
                if (row.Quest?.Ward != null)
                    wanted.Add(AssetRequest.Sprite(AssetManifest.WardArt(row.Quest.Ward, row.Quest.Colour)));

            _art = _art ?? AssetLibrary.Hold("welcome_page");
            await _art.LoadAsync(wanted, null, cancellation);

            if (Living) Dress();
        }

        // -------------------------------------------------------------- claiming
        /// <summary>
        /// Opens the choice. The sheet takes the quest through the ledger and says which prize;
        /// this page then plays the payoff that fits. A second device that got there first is
        /// answered by the sheet closing and the row repainting.
        /// </summary>
        void Collect(Row row)
        {
            if (_claiming || Flow.HasModal || row == null) return;
            if (WelcomeLedger.StateOf(row.Quest) != WelcomeState.Ready) return;

            var quest = row.Quest;
            Flow.Modal<WelcomeChoiceOverlay>(v =>
            {
                v.Quest = quest;
                v.Taken = reward => OnTaken(row, reward);
            });
        }

        void OnTaken(Row row, WelcomeReward reward)
        {
            if (!Living || row == null || !row.Root) return;

            Repaint();
            Tween.Punch(row.Root, .12f, .3f);
            if (row.Body)
                Burst.Sparks(row.Body.transform, Vector2.zero, SiegeView.TintOf(row.Quest.Colour), 16, 300f, 26f, .6f);

            if (reward == WelcomeReward.Turret)
            {
                // The loadout's own ceremony, ending on EQUIP. It takes its own hold on the art.
                var quest = row.Quest;
                Flow.Modal<WardRevealOverlay>(v =>
                {
                    v.Model = quest.Ward;
                    v.Colour = quest.Colour;
                    v.Changed = Repaint;
                });
                return;
            }

            PlayPrice(row);
        }

        /// <summary>
        /// The coins flying from the row's seat to the header's readout.
        ///
        /// <para>
        /// <b><see cref="RewardFlight.AfterGrant"/>, because the grant has already landed</b>: the
        /// ledger awarded it inside the choice sheet, which is the one place a payout here cannot
        /// be opened before its grant. Credits and gems are the two things nothing clamps, so the
        /// rewind is exact (that method's own argument). A currency with no readout on this page
        /// is said rather than flown.
        /// </para>
        /// </summary>
        void PlayPrice(Row row)
        {
            var quest = row.Quest;
            bool gems = quest.PriceCurrency == Currency.Gems;
            long amount = quest.PriceAmount;
            if (amount <= 0) return;

            var flight = RewardFlight.AfterGrant(gems ? 0L : amount, gems ? amount : 0L);
            flight.Hold(this);

            var drop = new ChestDrop(gems ? ChestDropKind.Gems : ChestDropKind.Credits,
                                     (int)Math.Min(amount, int.MaxValue));
            bool flown = flight.Add(drop, row.Seat ? row.Seat : row.Root);

            if (!flown)
                Scenery.Toast(Content, Loc.Format(gems ? "ui.welcome.gems" : "ui.welcome.coins", Compact.Number(amount)),
                              Pal.Gold, 2.4f);

            flight.Play(Content, null);
        }

        public override bool OnBack()
        {
            Flow.Go<HomeScreen>();
            return true;
        }
    }
}
