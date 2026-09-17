using System;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
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
    /// The hub. Rank and grove progress are real; hearts, coins and gems come from
    /// <see cref="Profile"/> and are placeholders until the economy is built.
    /// </summary>
    public sealed class HomeScreen : View
    {
        public override string Track => "mus_menu";

        // ------------------------------------------------------- the feature row
        /// <summary>
        /// The two boxes under the daily panel: the streak, and whatever the player is
        /// working toward.
        ///
        /// <para>
        /// They are equals on purpose. Both were smaller than the thing they carried — the
        /// streak was a chip squeezed into the top bar beside the settings gear, and the
        /// event was a 56-high strip that had to fit a name, a clock, a bar and a count.
        /// Between them they are the two reasons a player opens the game on a day they had
        /// not planned to, which is a strange thing to draw at the size of chrome.
        /// </para>
        /// <para>
        /// The right box holds the event when one is running and the next companion when
        /// one is not. That is not a fallback bolted on: the strip already chose between
        /// exactly those two and simply hid the loser, so the only change is that the loser
        /// now gets the slot when the winner is absent. It also means the row never draws
        /// one box and a hole.
        /// </para>
        /// </summary>
        const float RowTop = 570f;
        const float RowHeight = 300f;
        const float RowWidth = 960f;    // every element above the play key shares it
        const float RowGap = 24f;

        RectTransform _tasksPanel;
        RectTransform _resourceRow;
        RectTransform _streakBox;
        RectTransform _focusBox;

        protected override void Build()
        {
            BuildBackdrop();
            BuildTopBar();
            BuildResources();
            BuildTasks();
            BuildFeature();
            BuildLoadout();
            BuildPlay();
            BuildChallenges();
            NavBar.Build(Content, NavBar.Tab.Home);

            // Midnight arrives while a screen is open exactly as often as it arrives while
            // it is not, and a run finishing changes the box from under itself.
            TaskLedger.Changed += OnTasksChanged;
            DailyStreak.Changed += OnStreakChanged;

            // The wallet's own two cues are not here: `BuildResources` attaches a
            // `WalletWatch`, which is the one place in the game that subscribes to them.
        }

        void OnDestroy()
        {
            TaskLedger.Changed -= OnTasksChanged;
            DailyStreak.Changed -= OnStreakChanged;
            WardLoadout.Changed -= OnLoadoutChanged;

            _line?.Dispose();
        }

        void OnTasksChanged()
        {
            // Guarded because the event can arrive from a save load during teardown, and
            // painting onto a destroyed screen throws where nobody is looking.
            if (this == null || !_tasksPanel) return;
            PaintTasks();
        }

        void OnStreakChanged()
        {
            if (this == null || !_streakBox) return;
            PaintStreak();
        }

        /// <summary>
        /// Takes something off the screen and then destroys it, in that order.
        ///
        /// <para>
        /// <c>Destroy</c> only lands at the end of the frame, so a panel replaced in place is
        /// drawn <em>over</em> its own replacement for the rest of the frame it was replaced
        /// in — and since everything here enters from <c>Tween.Pop</c> at scale zero, what
        /// the player sees is the old panel, then a gap, then the new one springing in. One
        /// of those reads as a flourish; two in a second reads as a fault.
        /// </para>
        /// <para>
        /// The other five screens that rebuild a region already do this (<c>ProfileScreen</c>,
        /// <c>EventScreen</c>, <c>StreakScreen</c>, <c>CompanionScreen</c>,
        /// <c>CompanionUnlockOverlay</c>); this was the screen that did not, which is the
        /// screen the doubling was reported from.
        /// </para>
        /// </summary>
        static void Hide(GameObject go)
        {
            if (!go) return;
            go.SetActive(false);
            Destroy(go);
        }

        // ------------------------------------------------------------- backdrop
        /// <summary>
        /// The interface kit's own machine room, and a rail across the top of it.
        ///
        /// <para>
        /// <b>It replaced three painted forest layers, a pulsing shaft of light and thirty
        /// fireflies</b>, and the trade is worth stating because what went was not bad. Those
        /// layers were a *place* and the panels standing on them were primitives — rounded
        /// rectangles with a traced outline — so the hub read as a good painting with a
        /// prototype UI on it. The room is a worse painting and the whole screen is now one
        /// object, which is the trade the owner asked for.
        /// </para>
        /// <para>
        /// Nothing here is per-screen: the storefront draws the same room and the same rail, so
        /// the two tabs a player crosses most often are one place seen twice rather than two
        /// screens that happen to share a nav bar.
        /// </para>
        /// </summary>
        void BuildBackdrop()
        {
            Scenery.Room(Content);
        }

        // -------------------------------------------------------------- top bar
        void BuildTopBar()
        {
            var bar = UIKit.Box("TopBar", Safe, new Vector2(0f, 168f), new Vector2(.5f, 1f), new Vector2(0f, -116f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, 168f);

            // 620 wide rather than 500: the streak chip used to sit at the far end of this
            // bar and the card was cut back to make room for it. With the chip gone the
            // space belongs to the name and the rank bar, which were both short of it —
            // a name is the one string here whose length the game does not choose.
            //
            // The kit's card, nine-sliced, rather than a rounded rectangle and a traced
            // outline. The outline went with it rather than being kept: the sprite carries its
            // own keyline, and a second one at a radius the sprite no longer has is a halo a
            // hair off the shape it is following.
            var card = UIKit.Img("Card", bar, Art.S("Ui/" + Skins.Card), Color.white,
                                 new Vector2(620f, 138f), new Vector2(0f, .5f), new Vector2(352f, 0f));

            // The kit's inset slot — the one piece in it that reads as a hole rather than as a
            // thing standing on the screen, which is what an avatar wants to sit in.
            var frame = UIKit.Img("Avatar", card.transform, Art.S("Ui/" + Skins.Slot), Color.white,
                                  new Vector2(116f, 116f), new Vector2(0f, .5f), new Vector2(70f, 0f));
            var face = UIKit.Img("Face", frame.transform, null, Color.white,
                                 new Vector2(84f, 84f), new Vector2(.5f, .5f), new Vector2(0f, 2f));
            face.preserveAspect = true;

            // The companion the player chose, not a hardcoded critter. Animated when it
            // has frames, which the boot preload has already warmed.
            CompanionArt.Paint(face, Profile.Avatar, animate: true);
            var fb = face.GetComponent<Flipbook>();
            if (fb) fb.Offset = 3;

            var rank = UIKit.Img("RankBadge", frame.transform, Art.Disc(64), Pal.Gold,
                                 new Vector2(50f, 50f), new Vector2(1f, 0f), new Vector2(-4f, 4f));
            UIKit.Titled("N", rank.transform, Profile.Rank.ToString(), 30, new Color(.32f, .21f, .06f),
                         TextAnchor.MiddleCenter, outline: 0f, shadow: 0f);

            UIKit.Shrinkable(
                UIKit.Titled("Name", card.transform, Profile.Name, 36, Pal.Cream, TextAnchor.LowerLeft,
                             new Vector2(460f, 46f), new Vector2(0f, .5f), new Vector2(362f, 22f),
                             3f, 3f), 24);

            // The rank bar, filled by stars toward the next rank — the kit's own trough with
            // the kit's own fill in it, rather than two drawn rounded rectangles.
            //
            // **The fill is a white sprite tinted**, which is what `Skins.Fill` is cut for: it
            // keeps the lighter top half the pack draws into every bar, so this reads as a tube
            // with something in it rather than as a green rectangle inside a black one. One
            // sprite serves every bar in the app, and a bar can no longer come to disagree with
            // the trough it sits in.
            var track = UIKit.Img("XpTrack", card.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                                  new Vector2(440f, 30f), new Vector2(0f, .5f), new Vector2(356f, -24f));
            var xp = UIKit.Img("XpFill", track.transform, Art.S("Ui/" + Skins.Fill), Pal.Mint,
                               new Vector2(0f, 22f), new Vector2(0f, .5f), new Vector2(4f, 0f));
            var xpRT = (RectTransform)xp.transform;
            xpRT.pivot = new Vector2(0f, .5f);
            xpRT.sizeDelta = new Vector2(0f, 22f);
            float w = 432f * Profile.RankProgress;
            Tween.Run(.7f, Ease.OutCubic, t => { if (xpRT) xpRT.sizeDelta = new Vector2(w * t, 22f); }, xp).Delay(.35f);

            // corner buttons
            UIKit.IconButton("Settings", bar, Skins.Aside, "ic_gear", new Vector2(106f, 106f),
                             new Vector2(1f, .5f), new Vector2(-92f, 0f), () => Flow.Modal<SettingsOverlay>());
            UIKit.IconButton("Info", bar, Skins.Aside, "ic_info", new Vector2(106f, 106f),
                             new Vector2(1f, .5f), new Vector2(-208f, 0f), () => Flow.Modal<HowToOverlay>());
        }

        /// <summary>
        /// Hearts, coins and gems.
        ///
        /// <para>
        /// <b>Painted on change rather than rebuilt, and the row itself only on navigation.</b>
        /// All three move while this screen is open — a heart lands on a timer, and a chest
        /// pays out into an overlay drawn on top of it — and a pill still showing the number
        /// from thirty seconds ago is how a player concludes the reward did not arrive. It used
        /// to answer that by rebuilding the row, which replayed the pills' entrance from scale
        /// zero: one of those is a flourish, and several in a second (returning to the game
        /// fires the cues several times over) is the row flashing in and out.
        /// </para>
        /// <para>
        /// <b>That also removed a re-entrancy that was quietly worse than the flashing.</b>
        /// Reading <c>Wallet.Hearts</c> commits a refill and raises <c>HeartsChanged</c> from
        /// inside the getter, so this method — which reads it while placing the first pill —
        /// could be re-entered halfway through itself. The inner call built the row the player
        /// ends up with; the outer one carried on and registered <em>its</em> pills with
        /// <see cref="ResourceSlots"/>, which are on the row destroyed at the end of the frame,
        /// and the chest's prizes then had nowhere to fly to. Registration stays here for the
        /// same reason it always did: this is the one thing that runs exactly once per row.
        /// </para>
        /// </summary>
        void BuildResources()
        {
            int sibling = _resourceRow ? _resourceRow.GetSiblingIndex() : -1;
            if (_resourceRow) Hide(_resourceRow.gameObject);

            var row = UIKit.Box("Resources", Safe, new Vector2(RowWidth, 104f), new Vector2(.5f, 1f), new Vector2(0f, -254f));
            _resourceRow = row;
            if (sibling >= 0) row.SetSiblingIndex(sibling);

            // The plus always opens the same panel, and that is the change worth explaining.
            // It used to choose between three destinations by asking whether an ad happened
            // to be loaded — the offer panel, the out-of-hearts gate, or a toast — which
            // meant the one control on this screen that looks like a question mark answered
            // a different question depending on the state of an ad network. A player who
            // tapped it twice got two different screens and learned nothing from either.
            //
            // It now opens the resource's own panel, which explains how the resource works
            // whatever the network is doing and offers the video when there is one. The
            // states that used to hide it are states the panel renders honestly: at the
            // ceiling it says so, with nothing loaded it says it is looking.
            ResourcePill(row, -322f, Pal.Rose, "ic_heart", Profile.HeartsLabel(), false,
                         () => Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.HeartRefill),
                         ResourceSlots.Kind.Hearts, n => Profile.HeartsLabel((int)n));
            ResourcePill(row, 0f, Pal.Gold, null, Compact.Number(Profile.Coins), true,
                         () => Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.CoinBonus),
                         ResourceSlots.Kind.Credits, Compact.Number);
            // The gem shelf, not a "coming soon" panel. It said gems would arrive one day and
            // was left behind when they did — they buy hearts, boosts, a continue on a lost run
            // and a heart rescue, all shipped — so the one control on this row that promises
            // something was the only one that did nothing, in hardcoded English that the loc
            // gate could not see because it is not key-shaped.
            //
            // GemShopOverlay rather than the shop tab, and that is the same answer a lost run
            // gets: it brings the shelf to the player instead of navigating, so the hub is
            // still underneath when they close it. It also keeps the house rule this row
            // exists to obey — a `+` beside a resource always opens that resource's panel,
            // whatever the state of the world.
            ResourcePill(row, 322f, Pal.Bloom, "ic_gem", Compact.Number(Profile.Gems), false,
                         () => Flow.Modal<GemShopOverlay>(),
                         ResourceSlots.Kind.Gems, Compact.Number);

            // And the three are watched for the life of the screen. Idempotent, which is what
            // lets it sit beside the registration in a builder that runs again on navigation
            // and can re-enter itself.
            WalletWatch.Attach(this, ResourceSlots.Kind.Hearts, ResourceSlots.Kind.Credits,
                               ResourceSlots.Kind.Gems);
        }

        /// <summary>
        /// Resource readout with an add button. Coins use the spinning sprite.
        /// </summary>
        /// <remarks>
        /// Each pill registers itself with <see cref="ResourceSlots"/> as it is built, which
        /// is what lets the daily chest fly its prizes into this row from an overlay drawn on
        /// top of it. Registration happens here rather than at the call site precisely because
        /// this row is destroyed and rebuilt whenever the wallet moves — anything holding a
        /// reference from the last build would be pointing at a dead object, and the one place
        /// guaranteed to run on every rebuild is the builder itself.
        /// </remarks>
        void ResourcePill(Transform parent, float x, Color tint, string icon, string value,
                          bool animatedCoin, Action onAdd,
                          ResourceSlots.Kind kind, Func<long, string> format)
        {
            // The kit's trough, drawn at white: it carries its own near-black interior, its own
            // orange rim and its own side clips, so there is nothing here to tint and nothing to
            // trace. It is the same rim the storefront's balances sit in and the same rim a
            // title is written on — one shape for "a number or a word the game owns", which is
            // one thing for a player to learn instead of three.
            var bg = UIKit.Img("Pill", parent, Art.S("Ui/" + Skins.Trough), Color.white,
                               new Vector2(304f, 96f), new Vector2(.5f, .5f), new Vector2(x, 0f));

            // 66 in from the edge, because the kit clips each end of the trough and a clip is
            // 43 units wide however wide the plate is drawn - a nine-sliced border is one sprite
            // pixel per unit, so it does not shrink with the box.
            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(120f, 120f), new Vector2(0f, .5f), new Vector2(66f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, animatedCoin ? null : Art.S("Ui/" + icon), Color.white,
                               new Vector2(62f, 62f), new Vector2(0f, .5f), new Vector2(66f, 0f));
            ic.preserveAspect = true;
            if (animatedCoin) Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .06f, 2.2f, x * .01f);

            var t = UIKit.Titled("V", bg.transform, value, 37, Pal.Cream, TextAnchor.MiddleCenter,
                                 new Vector2(128f, 52f), new Vector2(.5f, .5f), new Vector2(22f, 0f), 3f, 3f);

            // The kit's own "+", which is a painted control rather than a glyph on a square —
            // so it is one image instead of two, and it is the same "+" the storefront draws.
            var add = UIKit.Button("Add", bg.transform, Art.S("Ui/" + Skins.Add), new Vector2(64f, 64f),
                                   new Vector2(1f, .5f), new Vector2(-16f, 0f), onAdd);
            add.GetComponent<Image>().preserveAspect = true;

            ResourceSlots.Register(kind, (RectTransform)ic.transform, t, glow, tint, format);

            bg.transform.localScale = Vector3.zero;
            Tween.Pop(bg.transform, 0f, .55f, .18f + Mathf.Abs(x) * .0004f);
        }

        // ------------------------------------------------------ tasks & bonuses
        /// <summary>One chest on the hub's ladder, kept so a repaint can light it.</summary>
        sealed class HubChest
        {
            public string TierId;
            public Image Img;
            public RectTransform Halo, Shine;
            public bool Lit;
        }

        Btn _taskCard;
        Beacon _taskBeacon;
        Badge _taskBadge;
        readonly System.Collections.Generic.List<HubChest> _taskChests = new System.Collections.Generic.List<HubChest>();

        /// <summary>
        /// The Tasks &amp; Bonuses box, drawn as a chest pack: a burst of light behind one row
        /// of big chests packed until they overlap, a starburst counting what is ready, and a
        /// countdown. The whole card is the door.
        ///
        /// <para>
        /// The first cut of this box was a plate with four small icons on it and a line of
        /// text, and the owner's verdict was that it was not creative — the reference was a
        /// store's chest-pack card, which sells a pack by making the chests the picture. So
        /// the chests are the picture here, and everything else on the card is furniture
        /// around that.
        /// </para>
        /// <para>
        /// <b>The second cut took the furniture away again.</b> The card carried a ribbon
        /// naming it, a caption saying how many tasks were on the slate and a green OPEN pill,
        /// and against the reference the owner's verdict was that the chests had to be
        /// <em>bigger and closer</em>: a title, a sentence and a button between them had
        /// pushed the pack down to a third of the plate, so the one thing the box is about was
        /// the smallest thing on it. All three are gone, which leaves the row the whole 240 to
        /// stand in — <see cref="BuildChestRow"/>. Two of them cost nothing to lose (the card
        /// has always been the door, and the badge already counts what is ready); the third
        /// is the only thing worth stating plainly: <b>nothing on this card says the word
        /// "tasks" any more.</b> What is left saying it is the chests themselves, the clock,
        /// and the hub's own place for it.
        /// </para>
        /// <para>
        /// <b>Built once, painted from the ledger</b> (<see cref="PaintTasks"/>): the clock,
        /// the starburst, the beacon and which chests are lit exist from the first frame and
        /// are switched, so a counter moving behind the hub redraws a line rather than popping
        /// the card in again. The rays and the shelf live in a clipped child, because they
        /// reach past the plate; the chests and the starburst hang over its edges on purpose
        /// and are not clipped.
        /// </para>
        /// </summary>
        void BuildTasks()
        {
            const float H = 240f;
            const float Margin = 40f;

            var card = UIKit.Button("Tasks", Safe, Art.S("Ui/" + Skins.PlateViolet),
                                    new Vector2(RowWidth, H), new Vector2(.5f, 1f),
                                    new Vector2(0f, -436f), OpenTasks);
            card.PressScale = .985f;
            _taskCard = card;
            _tasksPanel = (RectTransform)card.transform;

            // Straight after the card, so the lit rim and the ring sit under everything else.
            _taskBeacon = FeatureBeacon(_tasksPanel);

            // Everything that reaches past the plate's edge is clipped here: the rays, the
            // shelf's light, the sparks. The plate's own rounded corners are the mask's, near
            // enough — the rays are far dimmer than the keyline at that radius.
            var clip = UIKit.Node("Clip", card.transform);
            UIKit.StretchTo(clip, 6f, 6f, 6f, 6f);
            clip.gameObject.AddComponent<RectMask2D>();

            // A burst of rays turning slowly behind the chests, which is what makes a row of
            // pictures read as treasure rather than as an inventory. Warm on the violet, and
            // low: it is a light source, not a pattern. Centred on the pack rather than on the
            // plate, because it is the pack's own light.
            var rays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .22f),
                                 new Vector2(860f, 860f), new Vector2(.5f, .5f), new Vector2(0f, -34f));
            Tween.Run(36f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      rays, "spin").Loop(-1, false);

            // The shelf: the pool the chests stand in, so they read as placed rather than
            // floating on the plate. **It is a shadow rather than a light, and that is the
            // plate's fault rather than a preference** — the violet is saturated enough that a
            // warm glow over it is invisible at any alpha worth using (44g from the other
            // end: you cannot light a bright colour, you can only darken it), while a pool
            // under the feet reads immediately.
            UIKit.Img("Shelf", clip, Art.Glow(128, 1.7f), new Color(.16f, .02f, .24f, .34f),
                      new Vector2(840f, 170f), new Vector2(.5f, .5f), new Vector2(0f, -84f));

            Fireflies.Spawn(clip, 10, new Color(1f, .92f, .62f), 3f, 9f);

            BuildChestRow(card.transform, ProgressionRules.Table.Tasks.Tiers);

            // The name, across the top. **The whole title, not a corner label** — it is the one
            // thing on the card that says the pack is a feature rather than a shop shelf, and
            // it is the page's own `ui.tasks.title` rather than a key of its own, because the
            // words are the same words and one string cannot come to disagree with itself.
            UIKit.Shrinkable(
                UIKit.Titled("Name", card.transform, Loc.Get("ui.tasks.title").ToUpperInvariant(), 27,
                             Pal.Gold, TextAnchor.MiddleCenter, new Vector2(RowWidth - 2f * Margin, 36f),
                             new Vector2(.5f, 1f), new Vector2(0f, -26f), 3f, 3f), 17);

            // The starburst, top left, counting what is ready. Built last so it sits over
            // the card's own tap area.
            _taskBadge = BurstBadge(card.transform);

            PaintTasks();

            _tasksPanel.localScale = Vector3.zero;
            Tween.Pop(_tasksPanel, 0f, .6f, .3f).OnDone(() =>
            {
                if (card) card.Rehome();
            });
        }

        /// <summary>
        /// The pack itself: one row of chests, humblest to grandest left to right, drawn as a
        /// symmetric arch — biggest in the middle, standing on a floor that dips with it, each
        /// pushed into its neighbour until they overlap.
        ///
        /// <para>
        /// <b>The shape is the ask.</b> Four chests spread evenly across a 960-wide plate at a
        /// size that fits under a title read as an inventory of four icons; the reference the
        /// owner gave is a pack — chests big enough to fill the plate's height, touching, with
        /// the tallest in the middle so the row's top arches and its feet dip. Both readings of
        /// "a V" are the same drawing.
        /// </para>
        /// <para>
        /// <b>The arithmetic is <see cref="ChestPack"/>'s</b>, because the tasks page draws this
        /// row too and a pack is a shape rather than a picture. What is here is the five numbers
        /// this plate has room for and the widgets a hub card wants — a contact shadow under
        /// each chest, and a halo that lights when a chest can be taken.
        /// </para>
        /// <para>
        /// <b>The middles are drawn last.</b> An arch where the far end overlaps the crest is an
        /// arch drawn back to front — and a lit chest still steps to the front of it
        /// (<see cref="PaintTasks"/>).
        /// </para>
        /// </summary>
        void BuildChestRow(Transform host, System.Collections.Generic.IReadOnlyList<ChestTier> tiers)
        {
            const float Tall = 188f;     // the drawn height of the chest at the crest
            const float Short = 130f;    // ... and of the two on the ends
            const float Dip = 20f;       // how much lower than the ends the crest stands
            const float Floor = -92f;    // where an end chest's feet are — the title is the ceiling
            const float Overlap = .07f;  // of the narrower of two neighbours

            _taskChests.Clear();

            var seats = ChestPack.Lay(tiers, Tall, Short, Dip, Floor, Overlap);
            if (seats.Length == 0) return;

            // The contact shadows go down first, all of them, because a shadow is a sibling
            // rather than a child: a child of the chest draws *over* it, which is what the
            // halo does on purpose and what a shadow must never do.
            foreach (var seat in seats)
                UIKit.Img("S_" + seat.Tier.Id, host, Art.Glow(128, 1.9f), new Color(.10f, .02f, .16f, .42f),
                          new Vector2(seat.Width * 1.30f, seat.Tall * .22f), new Vector2(.5f, .5f),
                          new Vector2(seat.X, seat.Foot - 2f));

            foreach (var seat in ChestPack.InDrawOrder(seats))
                _taskChests.Add(BuildHubChest(host, seat.Tier, seat.Middle, seat.Tall, seat.Index));
        }

        /// <summary>
        /// One chest of the pack: the closed icon, with its light built dark. A lit chest is
        /// drawn over its neighbours, which the row's overlap makes matter.
        ///
        /// <para>
        /// <paramref name="at"/> is where the <em>drawn</em> chest's middle goes and
        /// <paramref name="tall"/> is how tall it is drawn; the sprite is hung higher and
        /// larger than both (see <see cref="ChestPack.Fill"/>). The halo and the shine are children
        /// of the sprite and so are pushed back down by hand — a glow centred on a box a
        /// quarter of which is empty is a glow that lights the air above a chest.
        /// </para>
        /// </summary>
        HubChest BuildHubChest(Transform host, ChestTier tier, Vector2 at, float tall, int index)
        {
            float box = tall / ChestPack.Fill;

            var img = UIKit.Img("C_" + tier.Id, host, Art.S(tier.Icon), Color.white,
                                new Vector2(box * ChestPack.Aspect, box), new Vector2(.5f, .5f),
                                at + new Vector2(0f, tall * ChestPack.Lift));
            img.preserveAspect = true;

            var halo = UIKit.Halo(img.transform, Pal.Gold, tall * 1.9f, .55f);
            var shine = Shine(img.transform, tall * 1.6f, index * .6f);
            ((RectTransform)halo.transform).anchoredPosition = new Vector2(0f, -tall * ChestPack.Lift);
            shine.anchoredPosition = new Vector2(0f, -tall * ChestPack.Lift);
            halo.gameObject.SetActive(false);
            shine.gameObject.SetActive(false);

            Tween.Bob((RectTransform)img.transform, 2f, 3.4f + index * .35f, index * .8f);

            return new HubChest
            {
                TierId = tier.Id, Img = img,
                Halo = (RectTransform)halo.transform, Shine = shine, Lit = false,
            };
        }

        /// <summary>
        /// The kit's starburst with a count on it, built dark and painted like the disc badge.
        /// A starburst rather than a disc here because the card is a pack, and a pack's corner
        /// says "+N" the way a store's does.
        /// </summary>
        static Badge BurstBadge(Transform card)
        {
            var burst = UIKit.Img("Waiting", card, Art.S("Ui/" + Skins.Badge), Pal.Gold,
                                  new Vector2(104f, 104f), new Vector2(0f, 1f), new Vector2(46f, -44f));

            var count = UIKit.Shrinkable(
                UIKit.Titled("N", burst.transform, "+0", 30, new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(80f, 50f),
                             new Vector2(.5f, .5f), new Vector2(0f, 2f), 0f, 0f), 18);

            UIKit.Halo(burst.transform, Pal.Gold, 190f, .40f);
            burst.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            burst.gameObject.SetActive(false);

            return new Badge { Root = (RectTransform)burst.transform, Count = count, Prefix = "+" };
        }

        /// <summary>
        /// Writes the ledger onto the card: the clock, the starburst, the beacon and which
        /// chests are lit. Only a chest that <em>changed</em> starts or stops breathing —
        /// a breathe restarted on every repaint is a chest that jumps each time a counter moves.
        /// </summary>
        void PaintTasks()
        {
            if (!_tasksPanel) return;

            int ready = TaskLedger.ReadyCount;
            var lit = ReadyTiers();

            _taskBeacon?.Show(ready > 0);
            _taskBadge?.Paint(ready);

            for (int i = 0; i < _taskChests.Count; i++)
            {
                var chest = _taskChests[i];
                bool on = lit.Contains(chest.TierId);
                if (on == chest.Lit || !chest.Img) continue;

                chest.Lit = on;
                chest.Halo.gameObject.SetActive(on);
                chest.Shine.gameObject.SetActive(on);
                chest.Img.color = on ? Color.white : new Color(.90f, .92f, .96f, 1f);

                Tween.KillChannel(chest.Img.transform, "breathe");
                chest.Img.transform.localScale = Vector3.one;
                if (on)
                {
                    chest.Img.transform.SetAsLastSibling();
                    Tween.Breathe(chest.Img.transform, .075f, 1.5f, i * .4f);
                }
            }
        }

        /// <summary>Which tiers have a finished, unclaimed task on them right now.</summary>
        static System.Collections.Generic.HashSet<string> ReadyTiers()
        {
            var lit = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var period in TaskPeriods.All)
                foreach (var task in TaskLedger.Active(period))
                    if (TaskLedger.StateOf(task) == TaskState.Ready) lit.Add(task.Tier.Id);
            return lit;
        }

        /// <summary>
        /// Four soft capsules turning behind a lit chest. Cheap, and it reads as light
        /// coming off the thing rather than a ring drawn round it.
        /// </summary>
        static RectTransform Shine(Transform parent, float size, float phase)
        {
            var rays = UIKit.Box("Shine", parent, Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            rays.SetAsFirstSibling();

            for (int i = 0; i < 4; i++)
            {
                var ray = UIKit.Img("r" + i, rays, Art.SoftCapsule(40, 200), Pal.A(Pal.Sun, .22f),
                                    new Vector2(size * .16f, size * 1.15f), new Vector2(.5f, .5f), Vector2.zero);
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 45f);
            }

            Tween.Run(9f, Ease.Linear,
                      t => { if (rays) rays.localRotation = Quaternion.Euler(0, 0, t * 360f + phase * 40f); },
                      rays.gameObject, "spin").Loop(-1, false);

            return rays;
        }

        /// <summary>
        /// Opens the tasks page. A page rather than a panel over the hub, because the page
        /// has two slates, a ladder and a ceremony on it, and a panel that grew all three
        /// would be a screen wearing a modal's name.
        /// </summary>
        void OpenTasks()
        {
            if (Flow.HasModal) return;
            Flow.Go<TasksScreen>();
        }


        // ------------------------------------------------------- the feature row
        /// <summary>
        /// Builds both boxes and decides how wide each of them is.
        ///
        /// <para>
        /// The streak is always drawn, including for a player who has never had one — that
        /// is how they learn the thing exists before they have one to lose. So the only
        /// question here is whether there is a second box: when no event is running and the
        /// roster holds nothing further, the streak takes the whole row rather than sitting
        /// beside a hole.
        /// </para>
        /// <para>
        /// The boxes are built from their own left edge rather than from the centre, which
        /// is what lets the same code lay out a half-row and a full one.
        /// </para>
        /// </summary>
        void BuildFeature()
        {
            float half = (RowWidth - RowGap) * .5f;
            float y = -(RowTop + RowHeight * .5f);
            float x = (RowWidth - half) * .5f;

            _focusBox = UIKit.Box("Focus", Safe, new Vector2(half, RowHeight),
                                  new Vector2(.5f, 1f), new Vector2(x, y));

            if (!BuildFocusBox(half))
            {
                Destroy(_focusBox.gameObject);
                _focusBox = null;
            }

            bool paired = _focusBox != null;

            _streakBox = UIKit.Box("Streak", Safe,
                                   new Vector2(paired ? half : RowWidth, RowHeight),
                                   new Vector2(.5f, 1f), new Vector2(paired ? -x : 0f, y));
            BuildStreakBox();

            _streakBox.localScale = Vector3.zero;
            Tween.Pop(_streakBox, 0f, .55f, .28f);

            if (!paired) return;
            _focusBox.localScale = Vector3.zero;
            Tween.Pop(_focusBox, 0f, .55f, .34f);
        }

        /// <summary>
        /// The shell every box on this row shares: one radius, one fill, and a rim in the
        /// box's own colour.
        ///
        /// <para>
        /// The card is the button rather than carrying an invisible one, so a press squashes
        /// the whole box — glyph, number and strip together — instead of an overlay nobody
        /// can see. There is no plate behind the row and no rule between the two: each box
        /// earns its own contrast from its fill and rim, which it has to, because
        /// <c>grove_near</c> runs from near-white inside the light shaft to dark teal beside
        /// it and a flat shape would vanish over half of it.
        /// </para>
        /// </summary>
        RectTransform FeatureCard(Transform host, float w, string skin, Color tint, float edge,
                                  Action onTap)
        {
            var btn = UIKit.Button("Card", host, Art.S("Ui/" + skin), new Vector2(w, RowHeight),
                                   new Vector2(.5f, .5f), Vector2.zero, onTap);
            btn.PressScale = .985f;

            // The kit's card carries its own keyline, so the traced rim is gone — but the
            // box's own colour is not, because it is what tells the streak from the event at a
            // glance and the plate is now the same teal on both. It is a light along the top
            // edge rather than an outline around the whole card: a coloured rectangle traced
            // over a frame that already has one is a halo a hair off the shape it follows,
            // which is the fault this restyle removed from six other places.
            var lamp = UIKit.Img("Lamp", btn.transform, Art.Glow(96, 1.9f),
                                 Pal.A(Color.white, edge * .55f),
                                 new Vector2(w * .78f, 86f), new Vector2(.5f, 1f), new Vector2(0f, -14f));
            lamp.transform.SetAsFirstSibling();
            return (RectTransform)btn.transform;
        }

        /// <summary>
        /// The box's name, and the one fact that has to sit beside it rather than under it —
        /// a countdown, or that a companion is still locked.
        ///
        /// Both are placed from the card's own edges rather than by eye, and both shrink:
        /// the title is a translated name and the meta is a translated sentence with a clock
        /// in it, so neither width is under our control.
        /// </summary>
        void FeatureHeader(Transform card, float w, string title, Color tint, string meta,
                           bool badged = false)
        {
            // The meta is 200 wide inset 26 from the right, so the title gets everything left
            // of it less a gap. Worked out from the edges rather than by eye: both strings
            // are translated, both are right up against their box, and nothing clips them.
            //
            // A box with no meta still does not get the full width — the top-right corner is
            // where a count badge hangs, and a title long enough to reach it would run under
            // one rather than being clipped by it.
            //
            // `badged` moves the meta out of that corner as well. The streak box never needed
            // it because it carries no meta, so the collision only appeared when the event box
            // gained a badge and its countdown ran straight under the disc. The corner is 66
            // wide and hangs 30 out, so 70 of clearance puts the text clear of it whatever the
            // translation is.
            bool hasMeta = !string.IsNullOrEmpty(meta);
            float inset = badged ? 70f : 0f;
            float tw = hasMeta ? w - 262f - inset : w - 110f;

            UIKit.Shrinkable(
                UIKit.Titled("Title", card, title.ToUpperInvariant(), 25, tint, TextAnchor.MiddleLeft,
                             new Vector2(tw, 34f), new Vector2(0f, 1f),
                             new Vector2(30f + tw * .5f, -36f), 0f, 2f), 16);

            if (!hasMeta) return;

            UIKit.Shrinkable(
                UIKit.Titled("Meta", card, meta, 23, Pal.Cream,
                             TextAnchor.MiddleRight, new Vector2(200f, 32f), new Vector2(1f, 1f),
                             new Vector2(-126f - inset, -36f), 0f, 0f), 15);
        }

        /// <summary>
        /// The number the box is about, and what it counts.
        ///
        /// Both are shrinkable and neither is a two-digit field: a streak does not stop at
        /// the end of the ladder, and the caption under it is the shortest string on the
        /// screen in English and one of the longest in German.
        /// </summary>
        (Text value, Text caption) FeatureValue(Transform card, float w, string value, Color colour,
                                                string caption, Color tint)
        {
            float vx = -w * .5f + 299f;

            // **The box grew with the type, and it had to.** `UIKit.Shrinkable` is Best Fit,
            // which takes the largest size that fits in *both* directions — and at 62 this was
            // height-limited on the nail: Gemfire Display's line box is 72 units at 62pt inside
            // a box 74 tall. Raising the number alone would have drawn exactly what it drew
            // before, silently, which is the kind of change that gets reported as "nothing
            // happened". 92 holds 76pt's 88-unit line with room to spare.
            //
            // It grows **upward**: the centre moves by half the extra height, so the bottom
            // edge stays exactly where it was and the caption under it does not move. There is
            // 40 units of air between this box and the header above, and the growth spends 18
            // of it.
            var v = UIKit.Shrinkable(
                UIKit.Titled("V", card, value, 76, colour, TextAnchor.MiddleCenter,
                             new Vector2(200f, 92f), new Vector2(.5f, .5f), new Vector2(vx, 29f),
                             3f, 4f), 30);

            var cap = UIKit.Shrinkable(
                UIKit.Titled("Cap", card, caption, 22, tint, TextAnchor.MiddleCenter,
                             new Vector2(210f, 30f), new Vector2(.5f, .5f), new Vector2(vx, -32f),
                             0f, 0f), 15);

            return (v, cap);
        }

        /// <summary>The inset that runs along the bottom of a box, holding its one live detail.</summary>
        RectTransform FeatureStrip(Transform card, float w)
        {
            var strip = UIKit.Img("Strip", card, Art.S("Ui/" + Skins.Trough), Color.white,
                                  new Vector2(w - 72f, 58f), new Vector2(.5f, .5f),
                                  new Vector2(0f, -(RowHeight * .5f) + 46f));
            return (RectTransform)strip.transform;
        }

        /// <summary>
        /// Lights a box that is holding something the player can take right now.
        ///
        /// <para>
        /// The corner badge says <em>that</em> there is a reward and how many; this says
        /// <em>which box</em> from across the screen. They are not redundant — a 54px disc is
        /// something you find once you are already looking at the row, and the whole problem
        /// with a collect-by-hand reward is getting somebody to look at the row at all. A
        /// player who opens the hub for the daily chests has no reason to read the box beside
        /// them.
        /// </para>
        /// <para>
        /// Three layers, because each does a job the others cannot. A soft gold light sits
        /// <em>behind</em> the box and breathes — the same trick the nav caps use to hold
        /// their own contrast, run in reverse: a seat of light instead of a seat of shadow,
        /// and the only part of this visible from the far corner of the screen. Over the
        /// resting rim, a gold edge brightens with it, which is what says the border is lit
        /// rather than merely coloured. And a pale ring steps out of the border and fades,
        /// which is the "tap me": motion that <em>leaves</em> the shape is what the eye
        /// catches peripherally, and a rim that only brightens does not.
        /// </para>
        /// <para>
        /// The ring is cream rather than gold, and that is not a taste call — the first
        /// version was gold, travelling out of a gold rim into a gold glow, and it was
        /// invisible on the screen while looking perfectly reasonable in the code.
        /// </para>
        /// <para>
        /// It grows by a fixed number of pixels rather than by a scale factor, because a
        /// percentage would drift the moment a box changes width — which it does: the streak
        /// takes the whole row when there is no second box. Eighteen is further than the 24px
        /// gap between the boxes would allow a solid shape, and it can be, because the ring is
        /// down to a tenth of its alpha before it gets there.
        /// </para>
        /// <para>
        /// Built once and <em>switched</em>: a box lights and goes dark as counters move, and
        /// a beacon rebuilt on every event would replay its pulse from the start each time.
        /// The tweens keep running while it is hidden, which costs three interpolations a
        /// frame and buys a light that is mid-pulse the instant it is shown.
        /// </para>
        /// </summary>
        sealed class Beacon
        {
            public Image Seat, Lit, Ring;

            public void Show(bool on)
            {
                if (Seat) Seat.gameObject.SetActive(on);
                if (Lit) Lit.gameObject.SetActive(on);
                if (Ring) Ring.gameObject.SetActive(on);
            }
        }

        static Beacon FeatureBeacon(RectTransform card)
        {
            const float Reach = 18f;

            // Behind the card, so it lights the box rather than washing its contents:
            // a child would draw over the card's own fill, which is what everything
            // inside one of these boxes relies on not happening.
            var seat = UIKit.Img("Beacon", card.parent, Art.Glow(128, 1.7f), Pal.A(Pal.Gold, 0f),
                                 card.sizeDelta + new Vector2(96f, 96f),
                                 new Vector2(.5f, .5f), Vector2.zero);
            seat.transform.SetAsFirstSibling();

            var lit = UIKit.Img("Lit", card, Art.RoundOutline(28, 4f), Pal.A(Pal.Gold, 0f));
            UIKit.StretchTo((RectTransform)lit.transform, 0, 0, 0, 0);

            Tween.Run(1.5f, Ease.InOutSine, t =>
            {
                if (lit) lit.color = Pal.A(Pal.Gold, Mathf.Lerp(.22f, .95f, t));
                if (seat) seat.color = Pal.A(Pal.Gold, Mathf.Lerp(.10f, .34f, t));
            }, lit, "lit").Loop(-1, true);

            var ring = UIKit.Img("Ring", card, Art.RoundOutline(28, 5f), Pal.A(Pal.Cream, 0f));
            var ringRT = (RectTransform)ring.transform;

            // Not ping-ponged: a ring that steps out and then walks back in reads as
            // something breathing rather than as something leaving. It also rests for the
            // back half of the cycle — a pulse that never stops is a pulse nobody sees.
            Tween.Run(2.4f, Ease.Linear, t =>
            {
                if (!ringRT) return;

                float k = Mathf.Clamp01(t / .55f);
                float out01 = Ease.OutCubic(k);
                UIKit.StretchTo(ringRT, -Reach * out01, -Reach * out01, -Reach * out01, -Reach * out01);

                // Held bright for the first third of the step, then gone. Fading from the
                // first frame is what made the previous ring read as a flicker.
                ring.color = Pal.A(Pal.Cream, .75f * Mathf.Clamp01((1f - k) * 1.6f) * (k < 1f ? 1f : 0f));
            }, ring, "ring").Loop(-1, false);

            return new Beacon { Seat = seat, Lit = lit, Ring = ring };
        }

        /// <summary>
        /// A count on the corner of a box, built once and painted. Pops the first time it is
        /// shown and breathes after; a badge rebuilt on every event would pop every time.
        /// </summary>
        sealed class Badge
        {
            public RectTransform Root;
            public Text Count;
            public string Prefix = string.Empty;
            bool _shown;

            public void Paint(int n)
            {
                if (!Root) return;

                if (n <= 0)
                {
                    Root.gameObject.SetActive(false);
                    _shown = false;
                    return;
                }

                if (Count) Count.text = Prefix + n;
                if (_shown) return;

                _shown = true;
                Root.gameObject.SetActive(true);
                Tween.KillChannel(Root, "breathe");
                Root.localScale = Vector3.zero;
                var root = Root;
                Tween.Pop(root, 0f, .5f, .18f)
                     .OnDone(() => { if (root) Tween.Breathe(root, .10f, 1.3f); });
            }
        }

        static Badge CornerBadge(Transform card)
        {
            var badge = UIKit.Img("Waiting", card, Art.Disc(64), Pal.Gold,
                                  new Vector2(66f, 66f), new Vector2(1f, 1f), new Vector2(-30f, -28f));

            var rim = UIKit.Img("Rim", badge.transform, Art.Ring(64, 7f), new Color(.16f, .12f, .04f, .95f));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);

            // Shrinkable, because this is not a one-digit field: a player who is away for a
            // fortnight comes back to two figures.
            var count = UIKit.Shrinkable(
                UIKit.Titled("N", badge.transform, "0", 36, new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(50f, 50f),
                             new Vector2(.5f, .5f), Vector2.zero, 0f, 0f), 22);

            UIKit.Halo(badge.transform, Pal.Gold, 146f, .45f);
            badge.gameObject.SetActive(false);

            return new Badge { Root = (RectTransform)badge.transform, Count = count };
        }

        /// <summary>A bar inside a strip. Returns the track, so a caller can pin things to it.</summary>
        RectTransform FeatureBar(Transform strip, float w, float fill01, Color tint)
        {
            float track = w - 132f;

            var bar = UIKit.Img("Track", strip, Art.S("Ui/" + Skins.Trough), Color.white,
                                new Vector2(track, 22f), new Vector2(.5f, .5f), Vector2.zero);
            var fill = UIKit.Img("Fill", bar.transform, Art.S("Ui/" + Skins.Fill), tint,
                                 new Vector2(0f, 14f), new Vector2(0f, .5f), new Vector2(3f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            fillRT.sizeDelta = new Vector2(0f, 14f);

            float full = (track - 6f) * Mathf.Clamp01(fill01);
            Tween.Run(.85f, Ease.OutCubic,
                      t => { if (fillRT) fillRT.sizeDelta = new Vector2(full * t, 14f); },
                      fill).Delay(.5f);

            return (RectTransform)bar.transform;
        }

        // -------------------------------------------------------------- streak
        Image _streakLamp, _streakGlow, _streakFlame, _streakStripIcon;
        Text _streakValue, _streakCaption, _streakLine;
        Beacon _streakBeacon;
        Badge _streakBadge;
        bool _streakRiskPulse;

        /// <summary>
        /// The flame, the number of days behind it, and what tomorrow is worth.
        ///
        /// <para>
        /// This is the one thing on the screen the player already owns, and the only one
        /// they can lose by doing nothing — which is exactly why it is the strongest reason
        /// on the screen to come back tomorrow, and why it is no longer a chip beside the
        /// settings gear.
        /// </para>
        /// <para>
        /// Three states, drawn differently on purpose. Held and fed today: a lit flame. Held
        /// but not yet fed: the same flame, dimmed and pulsing, with the hours left in place
        /// of the caption — the only moment in the game where the screen says "this is going
        /// to be taken away", and it earns that by being true. Not held at all: a grey flame
        /// and an invitation, so a new player learns the thing exists before they have one
        /// to lose.
        /// </para>
        /// <para>
        /// <b>Built once, painted from the ledger</b> (<see cref="PaintStreak"/>). The three
        /// states differ in what is lit, what the words say and whether the corner carries a
        /// count — all of which exist from the first frame and are switched, so a run
        /// finishing behind the hub changes a number rather than popping the box in again.
        /// </para>
        /// </summary>
        void BuildStreakBox()
        {
            if (!_streakBox) return;

            float w = _streakBox.sizeDelta.x;

            // **The streak's orange moved from the words to the plate.** It was asked for as
            // the settings key's own orange on the title and the countdown; the box is now
            // drawn in that orange, and orange words on an orange plate is the one thing that
            // cannot be read. Same identity, and the flame still says lit from at-risk from
            // cold in three different drawings.
            var tint = Pal.Cream;
            float gx = -w * .5f + 115f;

            var card = FeatureCard(_streakBox, w, Skins.PlateOrange, tint, .30f, OpenStreak);
            _streakLamp = card.Find("Lamp")?.GetComponent<Image>();

            // Straight after the card, so the lit rim and the ring sit under everything the
            // box draws. They live at the border, where nothing else does.
            _streakBeacon = FeatureBeacon(card);

            FeatureHeader(card, w, Loc.Get("ui.home.streak"), tint, null);

            _streakGlow = UIKit.Img("Glow", card, Art.Glow(128, 2f), Pal.A(Pal.Sun, .32f),
                                    new Vector2(200f, 200f), new Vector2(.5f, .5f), new Vector2(gx, 4f));

            // **A calendar, still, in place of the flipbook flame.** What the number under it
            // counts is nights in a row, which a calendar says without being taught; and the
            // flame was the one animated thing on a screen of still ones, which is a lot of
            // motion to spend on a readout. The three states are still told apart — by the
            // number, by the caption, and by the pulse below when the flame is at risk.
            _streakFlame = UIKit.Img("Flame", card, Art.S("Ui/ic_streak"), Color.white,
                                     new Vector2(130f, 130f), new Vector2(.5f, .5f), new Vector2(gx, 4f));
            _streakFlame.preserveAspect = true;

            (_streakValue, _streakCaption) = FeatureValue(card, w, "—", Pal.Cream, string.Empty, tint);

            var strip = FeatureStrip(card, w);
            float sw = w - 72f;
            float lw = sw - 90f;

            _streakStripIcon = UIKit.Img("M", strip, Art.S("Ui/ic_gift"), Color.white,
                                         new Vector2(40f, 40f), new Vector2(0f, .5f), new Vector2(38f, 0f));
            _streakStripIcon.preserveAspect = true;

            _streakLine = UIKit.Shrinkable(
                UIKit.Titled("L", strip, string.Empty, 24, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(lw, 34f), new Vector2(0f, .5f),
                             new Vector2(70f + lw * .5f, 0f), 0f, 0f), 15);

            // Built last so it sits over the card's own tap area, and drawn as a number rather
            // than a dot because "3" is a reason to go and a dot is only a hint that there
            // might be one. FeatureHeader keeps its title clear of that corner.
            _streakBadge = CornerBadge(card);

            PaintStreak();
        }

        /// <summary>
        /// Writes the streak onto the box. The at-risk pulse is started only on the paint
        /// that made the flame at risk and killed on the one that fed it, so a repaint in
        /// the middle of a pulse leaves it running rather than snapping it back to full.
        /// </summary>
        void PaintStreak()
        {
            if (!_streakBox) return;

            int days = DailyStreak.Days;
            int pending = DailyStreak.Pending;
            bool atRisk = DailyStreak.AtRisk;
            bool lit = days > 0 && !atRisk;

            if (_streakLamp) _streakLamp.color = Pal.A(Color.white, (lit ? .52f : .30f) * .55f);
            _streakBeacon?.Show(pending > 0);
            if (_streakGlow) _streakGlow.gameObject.SetActive(lit);

            if (_streakFlame)
            {
                if (atRisk != _streakRiskPulse)
                {
                    _streakRiskPulse = atRisk;
                    Tween.KillChannel(_streakFlame, "risk");
                    if (atRisk)
                    {
                        var flame = _streakFlame;
                        Tween.Run(1.1f, Ease.InOutSine,
                                  t => { if (flame) flame.color = Color.Lerp(new Color(1f, 1f, 1f, .45f), Color.white, t); },
                                  flame, "risk").Loop(-1, true);
                    }
                }

                if (!atRisk)
                    _streakFlame.color = days > 0 ? Color.white : new Color(.78f, .82f, .88f, 1f);
            }

            if (_streakValue) _streakValue.text = days > 0 ? days.ToString() : "—";
            if (_streakCaption) _streakCaption.text = StreakCaption(days, atRisk);

            PaintStreakStrip(pending);
            _streakBadge?.Paint(pending);
        }

        /// <summary>
        /// What keeping the flame another night is actually worth.
        ///
        /// <para>
        /// It is most of the reason the box earns its size. The count above says how long
        /// the player has kept the streak, which is a record; this says what tomorrow pays,
        /// which is an argument. The old chip had room for one of the two and kept the
        /// record.
        /// </para>
        /// <para>
        /// A reward already waiting outranks tomorrow's, because it is something to do now
        /// rather than a reason to return. Neither is a number when the next rung pays
        /// nothing, which the shipped ladder never does but a retuned one could.
        /// </para>
        /// <para>
        /// <b>A chest night says the chest.</b> A rung pays credits, gems or a chest, and a
        /// chest has no amount — so the strip draws the tier's own closed icon and names it,
        /// which is the same picture and the same words the streak board draws. Reaching for
        /// <c>RewardArt</c> here would have meant inventing a second way to say "a Royal
        /// Chest" on the one screen that has the least room to say anything.
        /// </para>
        /// </summary>
        void PaintStreakStrip(int pending)
        {
            var rung = DailyStreak.NextReward;
            var drop = rung.AsDrop();

            bool chest = rung.IsChest;
            bool plain = pending > 0 || (!chest && !drop.IsValid);

            if (_streakStripIcon)
            {
                // A coin is a flipbook and everything else is a still, so the reel comes off
                // before the sprite is swapped — Glyph attaches one, and a still drawn over a
                // running reel is a still for one frame.
                Flipbook.Detach(_streakStripIcon);
                _streakStripIcon.sprite = plain ? Art.S("Ui/ic_gift")
                                        : chest ? Art.S(rung.Tier.Icon)
                                        : RewardArt.Icon(drop.Kind, drop.Item);
                _streakStripIcon.color = Color.white;
                _streakStripIcon.preserveAspect = true;
                if (!plain && !chest) RewardArt.Glyph(_streakStripIcon, drop.Kind, 10f);
            }

            if (_streakLine)
                _streakLine.text = pending > 0 ? Loc.Get("ui.home.streak_waiting")
                                 : plain ? Loc.Get("ui.home.streak_keep")
                                 : Loc.Format("ui.home.streak_next",
                                              chest ? Loc.Get(rung.Tier.NameKey)
                                                    : RewardArt.Amount(drop));
        }

        static string StreakCaption(int days, bool atRisk)
        {
            if (days <= 0) return Loc.Get("ui.streak.none");
            if (atRisk) return Loc.Format("ui.streak.at_risk",
                                          Profile.Countdown(DailyStreak.SecondsUntilLost));
            return Loc.Get(days == 1 ? "ui.streak.day" : "ui.streak.days");
        }

        /// <summary>
        /// Opens the streak page.
        ///
        /// A page rather than the toast this used to raise. The box answers "how many days";
        /// the question a player actually has is "and then what", and a reward two nights
        /// away is the thing that makes them open the game tomorrow. A line of text that
        /// fades after three seconds cannot carry that.
        /// </summary>
        void OpenStreak()
        {
            if (Flow.HasModal) return;
            Flow.Go<StreakScreen>();
        }

        // ------------------------------------------------------------- the focus
        /// <summary>
        /// The right-hand box, and whichever of two things most deserves it.
        ///
        /// <para>
        /// A live event wins, and the rule is worth stating plainly: an event is the only
        /// thing on this screen with a deadline. The unlock goal will still be there next
        /// week; the event will not.
        /// </para>
        /// <para>
        /// This is the choice the old 56-high strip already made — it simply hid the loser.
        /// The difference now is that the loser takes the slot when the winner is absent,
        /// which is why there is no state of this screen with one box and a gap.
        /// </para>
        /// <para>
        /// Drawn once per navigation and not repainted. An event's countdown is measured in
        /// days and the keeper level cannot move while this screen is up, so a live path
        /// would be code that never runs.
        /// </para>
        /// </summary>
        bool BuildFocusBox(float w)
        {
            // Featured, not Live. Chests are opened by hand, so a window closing must not take
            // one the player earned and never opened — and the box is the only way back to the
            // page holding it, so `Featured` answers the *oldest* season still owing something
            // and falls back to the live one (GroveEvents.Featured).
            //
            // **The shipped season repeats, so there is always a live one and this box is now
            // permanent.** The goal box below takes the slot only when the catalog has no
            // season at all — a build standing before the first window opens, which is the
            // state the game shipped in and which a content push leaves behind for good.
            var featured = GroveEvents.Featured;
            if (featured == null) return BuildGoalBox(w);

            BuildEventBox(featured, w);
            return true;
        }

        /// <summary>
        /// The live event: its name, its clock, how much of the track is done, and the mark
        /// it wears.
        ///
        /// The mark is the interesting part. It is <see cref="SeasonCrest"/> rather than a
        /// fixed glyph, so the season picks it from the manifest — and the shipped one is a
        /// crest cut from this app's own interface kit rather than a shape drawn here, for the
        /// reason <see cref="SeasonCrest.Watch"/> gives. How far through the track the player
        /// is was said by the crest and by the bar under it; it is said by the bar alone now,
        /// which is one reading drawn by an artist rather than two and one of them generated.
        /// </summary>
        void BuildEventBox(GroveEvent live, float w)
        {
            var progress = GroveEvents.ProgressOf(live);
            int goal = Mathf.Max(1, live.FinalGoal);
            float done = Mathf.Clamp01(progress.Marks / (float)goal);
            float gx = -w * .5f + 115f;
            long left = live.SecondsLeftAt(GameClock.NowUnix());

            var card = FeatureCard(_focusBox, w, Skins.PlateViolet, Pal.Cream, .50f, OpenEvent);

            // Straight after the card, for the reason the streak's is: the lit rim and the
            // ring live at the border, under everything else the box draws.
            if (progress.AnyWaiting) FeatureBeacon(card);

            FeatureHeader(card, w, Loc.Get(live.NameKey), Pal.Bloom,
                          left > 0 ? Loc.Format("ui.event.ends_in", Profile.LongCountdown(left))
                                   : Loc.Get("ui.event.ended"),
                          progress.AnyWaiting);

            UIKit.Img("Glow", card, Art.Glow(128, 2f), Pal.A(Pal.Bloom, .28f),
                      new Vector2(200f, 200f), new Vector2(.5f, .5f), new Vector2(gx, 4f));

            var mark = UIKit.Box("Mark", card, new Vector2(148f, 148f),
                                 new Vector2(.5f, .5f), new Vector2(gx, 4f));
            SeasonCrest.Paint(mark, live.Icon, Pal.Bloom, done);
            Tween.Breathe(mark, .055f, 2.6f);

            // The caption gives way to the instruction when there is something to take. The
            // fraction is still drawn right above it, so nothing is lost — and a box whose
            // border is lit and whose corner carries a count should say what to do about it.
            FeatureValue(card, w, Loc.Format("ui.home.fraction", progress.Marks, goal),
                         Pal.Cream,
                         progress.AnyWaiting ? Loc.Get("ui.home.event_waiting")
                                             : Loc.Get("ui.home.marks"),
                         progress.AnyWaiting ? Pal.Gold : Pal.Bloom);

            Milestones(FeatureBar(FeatureStrip(card, w), w, done, Pal.Bloom),
                       w, live, progress.Marks, goal);

            CornerBadge(card).Paint(progress.Waiting);

            Sheen.Attach(card, 4.6f);
        }

        /// <summary>
        /// The count of marks waiting, pinned to the corner.
        ///
        /// The same disc the streak wears, and deliberately the same: a gold badge on a
        /// feature box means one thing across this screen. Not redundant with the beacon
        /// either, for the reason stated there — the lit border is what is visible from
        /// across the room, the badge is what says how much once you are looking.
        /// </summary>
        void OpenEvent()
        {
            if (Flow.HasModal) return;
            Flow.Go<EventScreen>();
        }

        /// <summary>
        /// The track's rungs, as pips on the bar.
        ///
        /// A bar says how far along; the pips say how far to the next thing worth having,
        /// which is the question that actually moves somebody.
        ///
        /// <para>
        /// <b>Every fifth rung, not every rung.</b> A season carries forty of them and this
        /// bar is under six hundred units wide, so one pip each is a dotted line — measured,
        /// they overlap, which is a row of pictures pretending to be a scale (the tasks
        /// ladder's captions, 45h, on an axis rather than a row). The ones drawn are the
        /// rungs that pay a better chest than the one below, which is also what makes them
        /// worth aiming at.
        /// </para>
        /// </summary>
        static void Milestones(RectTransform bar, float w, GroveEvent live, int marks, int goal)
        {
            float track = w - 132f;
            int every = Mathf.Max(1, Mathf.CeilToInt(live.Milestones.Count / 8f));

            for (int i = every - 1; i < live.Milestones.Count; i += every)
            {
                int rung = live.Milestones[i].Goal;
                float x = -track * .5f + 3f + (track - 6f) * Mathf.Clamp01(rung / (float)goal);

                var pip = UIKit.Img("P" + i, bar, Art.Disc(32),
                                    marks >= rung ? Pal.Gold : new Color(.47f, .51f, .55f, .95f),
                                    new Vector2(18f, 18f), new Vector2(.5f, .5f), new Vector2(x, 0f));

                var rim = UIKit.Img("Rim", pip.transform, Art.Ring(32, 7f),
                                    new Color(.01f, .02f, .04f, .9f));
                UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// The next companion the keeper level will unlock, and how far along the way the
        /// player already is. False when the roster holds nothing further, which is what
        /// hands the whole row to the streak.
        ///
        /// <para>
        /// The rank bar in the card at the top measures progress <em>through</em> the current
        /// rank, which has a specific weakness: it empties every time it fills, and it never
        /// says what any of it is for. This measures the span between the unlock just earned
        /// and the one coming, and names it. A player four ranks into a five-rank span sees a
        /// bar four fifths full because it is four fifths full — the endowed-progress effect
        /// used honestly, as a change of framing rather than a fabricated head start. See
        /// <see cref="UnlockGoal"/>.
        /// </para>
        /// </summary>
        bool BuildGoalBox(float w)
        {
            var goal = UnlockGoal.Next(Profile.Level);

            // Nothing left to unlock. Drawing an empty box would be worse than drawing none:
            // a goal bar with no goal reads as something the game forgot to fill in.
            if (!goal.IsValid) return false;

            float gx = -w * .5f + 115f;

            // The meta says the condition rather than the state — "at rank 8" rather than
            // "locked" — which is the same job the event's countdown does on the other box:
            // both name what the player is waiting on, and only one of the two is useful.
            var card = FeatureCard(_focusBox, w, Skins.PlateViolet, Pal.Cream, .38f, () => Flow.Go<ProfileScreen>());
            FeatureHeader(card, w, Loc.Get(goal.NameKey), Pal.Bloom,
                          Loc.Format("ui.home.at_rank", goal.AtLevel));

            var portrait = UIKit.Img("Face", card, null, Color.white,
                                     new Vector2(132f, 132f), new Vector2(.5f, .5f),
                                     new Vector2(gx, 4f));
            portrait.preserveAspect = true;
            CompanionArt.Paint(portrait, AvatarCatalog.Find(goal.AvatarId), animate: false);

            // Locked, and shown as such. The picture is the reason to care; dimming it is
            // what makes it a thing to earn rather than a thing already owned.
            //
            // Only once the art has actually arrived, though: CompanionArt hides the frame
            // by dropping alpha to zero when a portrait is missing, and an Image with no
            // sprite draws a white rectangle rather than a blank. Overwriting that alpha
            // unconditionally is how a locked companion becomes a grey slab on a slow load.
            if (portrait.sprite != null) portrait.color = new Color(.42f, .48f, .54f, .95f);

            FeatureValue(card, w, goal.RanksToGo.ToString(), Pal.Cream,
                         Loc.Get(goal.RanksToGo == 1 ? "ui.home.rank_left" : "ui.home.ranks_left"),
                         Pal.Bloom);

            FeatureBar(FeatureStrip(card, w), w, goal.Progress01, Pal.Bloom);
            return true;
        }

        // ----------------------------------------------------- the foot of the hub
        /// <summary>
        /// What the bottom of the hub is made of, measured up from the nav bar.
        ///
        /// <para>
        /// <b>One stack rather than three anchors, because they have to clear each other.</b>
        /// Everything here is bottom-anchored on <see cref="View.Content"/> — the nav bar's own
        /// frame of reference — and each row's centre is the sum of what is under it, so moving
        /// one number moves the rest rather than leaving a gap for somebody to find on a phone.
        /// The one thing the arithmetic cannot check is the <em>top</em>: the loadout strip is
        /// the tallest of the three and the feature row hangs from the other end of the screen,
        /// so the clearance between them is smallest on the squarest canvas this game is drawn
        /// on. <c>render_home.py</c> is what looks at it.
        /// </para>
        /// </summary>
        const float FootGap = 14f;
        const float ChallengeW = 960f, ChallengeH = 280f;

        /// <summary>How far inside the plate the banner's window sits. See BuildChallenges.</summary>
        const float BannerInset = 8f;
        const float PlayW = 620f, PlayH = 178f;
        const float LineW = 960f;

        /// <summary>
        /// The challenge banner's centre, and the two rows stacked above it.
        ///
        /// <b>The key sits above the line rather than between it and the banner</b>, at the
        /// owner's instruction after playing it: the thing the screen is for wants the top of
        /// the stack, where the readout under it reads as what the key is about to be spent on.
        /// </summary>
        static float ChallengeY => NavBar.Height + FootGap + ChallengeH * .5f;
        static float LineY => ChallengeY + ChallengeH * .5f + FootGap + LineH * .5f;
        static float PlayY => LineY + LineH * .5f + FootGap + PlayH * .5f;

        void BuildPlay()
        {
            var play = UIKit.TextButton("Play", Content, Skins.Battle, "BATTLE", 62,
                                        new Vector2(PlayW, PlayH), new Vector2(.5f, 0f),
                                        new Vector2(0f, PlayY),
                                        () => Flow.Go<LevelsScreen>(), "ic_battle");

            // The kit sizes a pill's glyph at a third of its height, which is right for a small
            // mark beside a word and small for the one control this screen is about. Two things
            // have to follow the resize: the glyph is a *painted* picture rather than a
            // silhouette, so it is drawn white rather than in the caption's cream; and
            // `FitLabel` re-centres the glyph and the caption as one block off the glyph's own
            // width, so it has to run again or the pair sits off-centre.
            if (play.Icon)
            {
                ((RectTransform)play.Icon.transform).sizeDelta = Vector2.one * 112f;
                play.Icon.color = Color.white;
                UIKit.FitLabel(play);
            }
            UIKit.Halo(play.transform, Pal.Sun, 760f, .26f);
            play.transform.localScale = Vector3.zero;
            Tween.Pop(play.transform, 0f, .7f, .62f).OnDone(() =>
            {
                if (!play) return;
                play.Rehome();
                Tween.Breathe(play.transform, .03f, 2.1f);
                Sheen.Attach((RectTransform)play.transform, 3.4f);
            });
        }

        // -------------------------------------------------------- daily challenges
        /// <summary>
        /// The second door out of the hub: one painted banner on the kit's blue plate, with a
        /// shine crossing it.
        ///
        /// <para>
        /// <b>The picture is the caption.</b> The words <em>Daily Challenges</em> are painted
        /// into the art, so nothing here writes them a second time — a caption under a banner
        /// that already says the same thing is the furniture the tasks card had to lose
        /// (<see cref="BuildTasks"/>). That does cost this control the one thing every other
        /// string in the game has, which is a translation (invariant 6): <b>the banner is
        /// English and only English until it is re-cut</b>, and it is named here so that is a
        /// decision on the record rather than an oversight.
        /// </para>
        /// <para>
        /// <b>The plate is <see cref="Skins.PlateBlue"/> rather than a button face.</b>
        /// <c>btn_blue</c> is sliced across its width only (its border is <c>15,0,15,0</c>), so
        /// drawn at this height it stretches its own moulded face by 1.7 and reads as a smeared
        /// pill — invariant 44a, from the other end. The plate is sliced on all four edges and
        /// scales to any box, which is what "wide and smooth-cornered" actually needs.
        /// </para>
        /// <para>
        /// <b>The banner <em>covers</em> the plate rather than standing on it</b>, which is the
        /// owner's call after playing it: fitted inside, the art was an island with a hand's
        /// width of blue at each end and read as a picture somebody had dropped on a button.
        /// The art is 2.67:1 and the plate is 3.43:1, so covering is width-led and the crop
        /// comes off the top and bottom — about a ninth at each end, which is glow and empty
        /// sky above the lettering and leaf and rock below it.
        /// </para>
        /// <para>
        /// <b>So the plate becomes a window, and the window is cut from its own sprite.</b> A
        /// <c>Mask</c> whose graphic is the plate clips the picture to exactly the corners the
        /// plate draws, which is the only way a rounded button can hold a rectangular picture
        /// without either squashing it or showing its square corners. The scale is read off the
        /// sprite rather than typed, for <c>IdentWordmark</c>'s reason: a re-cut of the banner
        /// at another aspect must not leave a number here quietly describing the last one.
        /// </para>
        /// <para>
        /// <b>And the window is <see cref="BannerInset"/> inside the plate rather than flush
        /// with it.</b> Flush, the picture covered the plate's own painted bevel — the border
        /// that is what makes the sprite read as a button at all — so the art went right to the
        /// outside edge and past the curve at the four corners, and was reported as overflowing
        /// its box. The inset is small on purpose: enough to leave the plate's frame drawn all
        /// the way round, not enough to turn the picture back into an island.
        /// </para>
        /// <para>
        /// <b>And the halo went with the change rather than being kept.</b> It was a bloom
        /// around a small picture on a big plate; with the picture covering the plate there is
        /// nothing of the glow left to see, and the art carries its own.
        /// </para>
        /// </summary>
        void BuildChallenges()
        {
            var card = UIKit.Button("Challenges", Content, Art.S("Ui/" + Skins.PlateBlue),
                                    new Vector2(ChallengeW, ChallengeH), new Vector2(.5f, 0f),
                                    new Vector2(0f, ChallengeY),
                                    () => Flow.Go<DailyChallengesScreen>());

            // A press-scale that squashes a plate this wide reads as the screen flinching
            // rather than as a key going down — the same reason the loadout shelf and the
            // tasks card both hold theirs near one.
            card.PressScale = .985f;

            // The window. `showMaskGraphic` is false, so the plate is not painted twice; the
            // near-nothing alpha is what writes the stencil (`Sheen` cuts its own the same way).
            //
            // A compressed sprite used as a stencil leaks a few texels where its alpha should
            // be nought, which is what `ArtImportRules.Grades` grades the publisher card's
            // wordmark uncompressed for. It is deliberately *not* worth it here: this plate is
            // drawn on every screen in the game, the leak is a pixel or two at four corners
            // rather than a field of holes in a full-screen black sheet, and `Sheen` has cut
            // this same sprite this same way on every key in the app since the kit landed.
            var clip = UIKit.Img("Clip", card.transform, Art.S("Ui/" + Skins.PlateBlue),
                                 new Color(1f, 1f, 1f, .004f));
            var crt = (RectTransform)clip.transform;
            UIKit.StretchTo(crt, BannerInset, BannerInset, BannerInset, BannerInset);
            clip.type = Image.Type.Sliced;
            clip.raycastTarget = false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var banner = Art.S("Ui/challenges");
            float aspect = banner != null && banner.rect.height > 0f
                         ? banner.rect.width / banner.rect.height
                         : 0f;

            // Cover: the larger of the two scales that fill an axis. An address that has not
            // arrived leaves the plate plain rather than drawing a white bar (invariant 7b).
            float windowW = ChallengeW - BannerInset * 2f, windowH = ChallengeH - BannerInset * 2f;
            float drawnW = windowW, drawnH = aspect > 0f ? drawnW / aspect : 0f;
            if (drawnH < windowH) { drawnH = windowH; drawnW = drawnH * aspect; }

            var art = UIKit.Img("Banner", crt, banner, Color.white,
                                new Vector2(drawnW, drawnH), new Vector2(.5f, .5f), Vector2.zero);
            art.raycastTarget = false;
            art.enabled = aspect > 0f;

            card.transform.localScale = Vector3.zero;
            Tween.Pop(card.transform, 0f, .7f, .70f).OnDone(() =>
            {
                if (!card) return;
                card.Rehome();

                // The shine the owner asked for, and it is the one this game already has: a
                // band travelling left to right behind a mask cut to the plate's own shape,
                // so it cannot spill past the corners. See Sheen.
                Sheen.Attach((RectTransform)card.transform, 3.1f);
            });
        }

        // ------------------------------------------------------------- the loadout
        /// <summary>
        /// How big a turret cell on the hub is, how far apart the four stand, and how wide a
        /// star is drawn under one.
        ///
        /// <para>
        /// <b>The same furniture as the map's shelf and the action bar</b> (<c>LoadoutBar</c>,
        /// invariant 42b): the kit's card, the seat's own colour on the rim, the turret's real
        /// sprite and the shelf's own five-star row. A fourth drawing of one object would be a
        /// fourth thing to keep in step; this is the third place it appears and it is the same
        /// object in all three, at a smaller size.
        /// </para>
        /// <para>
        /// <b>Turrets only, and the kits stay on the map.</b> A kit is a consumable that runs
        /// out and is chosen on the way into a run, which is where the shelf that shows it
        /// stands; the line is a fact about the account, and it is the half a player is proud
        /// of. The hub has room for one of those.
        /// </para>
        /// </summary>
        const float LineCell = 168f, LineCellGap = 20f, LineStar = 20f;

        /// <summary>
        /// The strip, top to bottom: air, the caption's band, air, the row of cells, air.
        ///
        /// <para>
        /// <b>Summed rather than typed, because the first cut typed it and was wrong.</b> The
        /// strip was 206 with a 34-unit caption at the top and a 140 cell hung 14 above the
        /// middle, which reads as though it fits and does not: <c>UIKit.Box</c> pivots at centre
        /// (invariant 44d), so the cell's top edge was nine units <em>past</em> the caption's,
        /// and four turrets were drawn over the word LOADOUT. It was <c>render_home.py</c> that
        /// said so, which is the whole reason that file exists.
        /// </para>
        /// </summary>
        const float LinePad = 8f, LineHeadH = 34f, LineHeadGap = 8f, LineFoot = 10f;

        static float LineH => LinePad + LineHeadH + LineHeadGap + LineCell + LineFoot;

        /// <summary>Where a cell's centre sits, against the strip's own centre.</summary>
        static float LineCellY => LineH * .5f - LinePad - LineHeadH - LineHeadGap - LineCell * .5f;

        /// <summary>The four turret pictures, in the order <c>WardLine.Colours</c> names.</summary>
        readonly System.Collections.Generic.List<Image> _lineArt =
            new System.Collections.Generic.List<Image>(WardLine.Colours.Length);

        RectTransform _lineHost;

        /// <summary>
        /// This screen's own hold on the four bodies standing on the line.
        ///
        /// <b>Never the map bar's and never a board's</b> — invariant 7b's second rule: an
        /// address owned by another scope is never re-claimed, so taking one of theirs here
        /// would drop a live screen's turrets when the hub went away.
        /// </summary>
        AssetHold _line;

        /// <summary>
        /// The four turrets the player is standing on the line and how far each has been taken
        /// — a readout on the hub, and the door to the shelf that changes it.
        ///
        /// <para>
        /// <b>Built once and dressed when the art lands.</b> An <c>Image</c> with no sprite is a
        /// white rectangle rather than a blank (invariant 7b), so a cell is off until its
        /// picture arrives.
        /// </para>
        /// </summary>
        void BuildLoadout()
        {
            var host = UIKit.Button("Loadout", Content, Art.S("Ui/" + Skins.Panel),
                                    new Vector2(LineW, LineH), new Vector2(.5f, 0f),
                                    new Vector2(0f, LineY),
                                    () => Flow.Go<LoadoutScreen>());
            host.PressScale = .985f;
            _lineHost = (RectTransform)host.transform;

            UIKit.Titled("Head", _lineHost, Loc.Get("ui.loadout.title").ToUpperInvariant(), 28,
                         Pal.Gold, TextAnchor.MiddleCenter, new Vector2(LineW - 140f, LineHeadH),
                         new Vector2(.5f, 1f), new Vector2(0f, -(LinePad + LineHeadH * .5f)), 3f, 3f);

            var gear = UIKit.Img("Gear", _lineHost, Art.S("Ui/ic_gear"), Pal.A(Pal.Cream, .7f),
                                 Vector2.one * LineHeadH, new Vector2(1f, 1f),
                                 new Vector2(-34f, -(LinePad + LineHeadH * .5f)));
            gear.preserveAspect = true;
            gear.raycastTarget = false;

            PaintLoadout();

            // A line re-stood on the shelf, a turret bought, a star spent: all three change
            // what this says, and all three can happen while the hub is still underneath.
            WardLoadout.Changed -= OnLoadoutChanged;
            WardLoadout.Changed += OnLoadoutChanged;

            _lineHost.localScale = Vector3.zero;
            Tween.Pop(_lineHost, 0f, .6f, .54f);

            Run(LoadLineArt);
        }

        void OnLoadoutChanged()
        {
            if (this == null || !_lineHost) return;
            PaintLoadout();
            Run(LoadLineArt);
        }

        /// <summary>
        /// Draws the four cells for the line as it stands now.
        ///
        /// <para>
        /// <b>A redraw rather than an entrance</b> (invariant 16d): the cells are remade because
        /// a whole cell changes when its seat does, and nothing in here animates — the pop
        /// belongs to <see cref="BuildLoadout"/>, which runs once.
        /// </para>
        /// <para>
        /// The cells are found by name rather than kept in a list, so the caption and the gear
        /// standing on the same plate are not swept away with them.
        /// </para>
        /// </summary>
        void PaintLoadout()
        {
            for (int i = _lineHost.childCount - 1; i >= 0; i--)
            {
                var child = _lineHost.GetChild(i);
                if (child.name.StartsWith("Cell", StringComparison.Ordinal)) Hide(child.gameObject);
            }

            _lineArt.Clear();

            var line = WardLoadout.Line;
            int n = WardLine.Colours.Length;
            float step = LineCell + LineCellGap;

            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * .5f) * step;

                var cell = UIKit.Img("Cell" + i, _lineHost, Art.S("Ui/" + Skins.Card), Color.white,
                                     Vector2.one * LineCell, new Vector2(.5f, .5f),
                                     new Vector2(x, LineCellY));
                cell.raycastTarget = false;

                // The colour of the seat, which is the one thing this row has to say that a
                // turret's own silhouette does not. `LoadoutBar.BuildTurrets`, at a radius cut
                // for a cell this size.
                var rim = UIKit.Img("Rim", cell.transform, Art.RoundOutline(26, 5f),
                                    Pal.A(SiegeView.TintOf(i), .85f),
                                    Vector2.one * (LineCell - 10f));
                rim.raycastTarget = false;

                // Lifted, because the star row stands inside the card under it: a turret centred
                // in a square cell and a row of stars at its foot are the same pixels.
                var body = UIKit.Img("Body", cell.transform, null, Color.white,
                                     Vector2.one * (LineCell - 50f), new Vector2(.5f, .5f),
                                     new Vector2(0f, 14f));
                body.preserveAspect = true;
                body.raycastTarget = false;
                body.enabled = false;
                _lineArt.Add(body);

                WardStarRow.Build((RectTransform)cell.transform,
                                  new Vector2(0f, -LineCell + 26f), line.BuildAt(i).Stars, LineStar);
            }

            DressLoadout();
        }

        /// <summary>Puts whichever bodies are already in hand onto the row. <c>LoadoutBar.Dress</c>.</summary>
        void DressLoadout()
        {
            var line = WardLoadout.Line;

            for (int i = 0; i < _lineArt.Count; i++)
            {
                var model = line.At(i);
                if (model == null || _lineArt[i] == null) continue;

                var sprite = AssetLibrary.Sprite(AssetManifest.WardArt(model, i));

                _lineArt[i].sprite = sprite;
                _lineArt[i].enabled = sprite != null;
            }
        }

        async Task LoadLineArt(CancellationToken cancellation)
        {
            var line = WardLoadout.Line;
            var wanted = new System.Collections.Generic.List<AssetRequest>(WardLine.Colours.Length);

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                var model = line.At(i);
                if (model != null) wanted.Add(AssetRequest.Sprite(AssetManifest.WardArt(model, i)));
            }

            _line = _line ?? AssetLibrary.Hold("hub_loadout");
            await _line.LoadAsync(wanted, null, cancellation);

            if (Living) DressLoadout();
        }

        static string NextGladeLine()
        {
            var index = GameContent.Index;

            // The catalog's own default rather than the classic mode, because the classic mode
            // can be absent from a catalog (every glade chapter disabled, a client rolled back)
            // and this line would then read "every glade is awake" to a player with a whole
            // mode still in front of them. See CatalogIndex.DefaultMode.
            var next = LevelUnlock.NextToPlay(index, index != null ? index.DefaultMode
                                                                   : GameMode.Default);

            if (!next.IsValid) return Loc.Get("ui.home.all_awake");

            // Named from the id alone, so the home screen never reads a chapter file
            // just to draw one line of text.
            if (!PlayerProgress.IsCleared(next))
                return Loc.Format("ui.home.next_up", Loc.Get(LevelDefinition.DefaultNameKey(next)));

            // Everything the player may open is finished, and since the chapter boundary
            // became a star gate that is two different situations. One is a finished game and
            // the other is a player two stars short of the next chapter — telling the second
            // one that every level is awake would be the game congratulating them for being
            // stuck, on the screen whose whole job is to say what to do next. The gate names
            // itself instead, in the number that moves.
            var gate = LevelUnlock.GateAfter(index, index.ChapterOf(next));

            return gate.Exists && !gate.IsOpen
                ? Loc.Format("ui.home.next_chapter", gate.Held, gate.Required)
                : Loc.Get("ui.home.all_awake");
        }

        /// <summary>
        /// Says so when a heart went on a run the last launch never finished — a force-quit, a
        /// crash, a flat battery. See <see cref="RunGuard"/>.
        ///
        /// <para>
        /// Reported rather than left to be noticed. A resource that quietly decrements is a
        /// resource players feel cheated by later, which is the rule the defeat panel's heart
        /// row is built on, and it applies twice as hard here: this is the one charge in the
        /// game the player did not watch happen.
        /// </para>
        /// <para>
        /// Here rather than on the splash because a toast over a loading screen is a toast
        /// nobody reads, and it is <see cref="OnPresented"/> rather than <c>Build</c> so it
        /// lands after the iris has opened rather than under it. <c>NoteReported</c> makes it
        /// once per launch, not once per visit to the hub.
        /// </para>
        /// </summary>
        public override void OnPresented()
        {
            // the line under PLAY already says where to go, so no toast is needed for that

            if (!RunGuard.Unfinished.IsValid) return;

            string glade = Loc.Get(LevelDefinition.DefaultNameKey(RunGuard.Unfinished));
            bool charged = RunGuard.UnfinishedWasCharged;
            RunGuard.NoteReported();

            // Only when a heart was actually taken. A player who was already at zero owes
            // nothing, and telling them about a charge that did not happen is worse than
            // saying nothing at all.
            if (!charged) return;

            Tween.After(.5f, () =>
            {
                if (!this) return;
                Scenery.Toast(Content, Loc.Format("ui.forfeit.unfinished", glade),
                              Pal.Ember, 3.2f, new Vector2(.5f, 1f), -300f);
            }, this);
        }
    }
}
