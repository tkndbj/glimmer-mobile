using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Who somebody else is: their keeper level, the companions they have gathered, how far they
    /// have held the Infinite lane, and the line they take into a siege.
    ///
    /// <para>
    /// <b>A second screen rather than a mode on <see cref="ProfileScreen"/>, for
    /// <see cref="GroveVisitScreen"/>'s reason.</b> That screen is identity <em>and</em> the
    /// controls that change it — a rename pencil, a companion you tap to wear, a boards toggle,
    /// an account card with a delete in it — and every one of those would need a branch saying
    /// "not while visiting". A mode toggle that changes what every control on a screen does is
    /// what invariant 16 refused for the grove's own editing, and refusing it here costs one file
    /// that can only read.
    /// </para>
    /// <para>
    /// <b>It draws a <see cref="GroveCard"/> and nothing else</b>, which is what makes it
    /// read-only by construction rather than by discipline: there is nothing here to write to.
    /// Everything it shows is a field the server published, and the two fields it added for this
    /// screen — the companions bought and the line arranged — are the same facts the score was
    /// computed from, so the portraits and the number under them cannot disagree.
    /// </para>
    /// <para>
    /// <b>Ids this build does not know are drawn as nothing rather than as an error</b>, exactly
    /// as a visited grove's unknown pieces are. A visitor one content drop behind meets
    /// companions and turrets that do not exist for them yet:
    /// <see cref="AvatarCatalog.Resolve"/> falls back to the starter and
    /// <see cref="WardLine.Resolve"/> to the roster's, so a slightly plainer profile is what
    /// arrives rather than a refusal.
    /// </para>
    /// <para>
    /// <b>Two scopes, both released with the screen.</b> The companion roster's portraits and
    /// the four turret bodies this keeper stands — never the roster's twenty, which is what the
    /// shelf's atlas is for (invariants 7b and 16c). Both arrive asynchronously, so every cell
    /// they fill is repainted when they land and every <c>Image</c> begins with no sprite and
    /// disabled: an <c>Image</c> with no sprite is a white rectangle, not a blank.
    /// </para>
    /// </summary>
    public sealed class PublicProfileScreen : View
    {
        public override string Track => "mus_menu";

        const float CardWidth = 980f;
        const float Gap = 28f;
        const float HeaderHeight = 250f;

        /// <summary>Its own scope, never a run's or the map bar's — see <see cref="LoadoutBar"/>.</summary>
        const string TurretScope = "public_profile_line";

        string _ownerId = string.Empty;
        LeaderboardEntry _known;

        GroveCard _card = GroveCard.Empty;
        bool _fetching;
        bool _failed;

        RectTransform _viewport, _stack;
        float _cursor;
        bool _entered;

        Text _status;
        Btn _report;
        bool _reporting;

        BusyVeil _busy;
        AssetHold _portraits, _turrets;

        /// <summary>The turret cells, in colour order, painted when the scope lands.</summary>
        readonly List<Image> _bodies = new List<Image>(WardLine.Colours.Length);

        /// <summary>
        /// Opens on a keeper. The row's own figures are kept so the screen can say <em>whose</em>
        /// profile is being fetched while it is still in flight — the same bargain
        /// <see cref="GroveVisitScreen.Visit"/> strikes with a name, and it matters more here,
        /// because a visit at least draws a floor and this draws nothing at all until the card
        /// lands.
        /// </summary>
        public void Show(string ownerId, LeaderboardEntry known)
        {
            _ownerId = ownerId ?? string.Empty;
            _known = known;
        }

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 18, new Color(1f, .93f, .70f), 6f, 22f);

            BuildBody();
            BuildHeader();

            // Built last so it draws over everything and both of the states it describes are
            // already on screen underneath it.
            // Named when the board knew a name, generic when it did not — a caller with no row
            // in hand (a deep link, a later drop) gets a sentence rather than a gap.
            string looking = string.IsNullOrEmpty(_known.Name)
                ? Loc.Get("ui.profile.public_looking")
                : Loc.Format("ui.profile.public_loading", _known.Name);

            _busy = BusyVeil.Attach(Safe, looking);

            // Arriving from a board the scope is usually warm and this repaints immediately.
            _portraits = CompanionArt.Open(this, () => { if (Living) BuildBody(); });

            Open();
        }

        void OnDestroy()
        {
            _portraits?.Dispose();
            _turrets?.Dispose();
        }

        // ------------------------------------------------------------------ fetching
        void Open() => Run(async token =>
        {
            _fetching = true;
            _failed = false;
            BuildBody();

            var (result, card) = await GroveBoard.FetchCardAsync(_ownerId, token);

            _fetching = false;
            if (!Living) return;

            _failed = !result.Ok || card == null || !card.IsValid;
            _card = card ?? GroveCard.Empty;

            BuildBody();

            if (!_card.IsValid) { _busy?.Done(); return; }

            await LoadLineAsync(token);

            if (!Living) return;

            _busy?.Done();
            PaintLine();
        });

        /// <summary>
        /// The four turret bodies this keeper stands, and only those.
        ///
        /// <b>The line and never the roster</b> (invariant 7b): four turrets are on the screen
        /// and twenty are in the shop, so a profile pays for four — the same bargain
        /// <see cref="WardLine.Art"/> exists to make, narrowed to the one picture this screen
        /// draws per seat.
        /// </summary>
        async Task LoadLineAsync(CancellationToken cancellation)
        {
            var line = _card.Line(WardLedger.Catalog);
            var wanted = new List<AssetRequest>(WardLine.Colours.Length);

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                var model = line.At(i);
                if (model != null) wanted.Add(AssetRequest.Sprite(AssetManifest.WardArt(model, i)));
            }

            _turrets = _turrets ?? AssetLibrary.Hold(TurretScope);
            await _turrets.LoadAsync(wanted, null, cancellation);
        }

        // ------------------------------------------------------------------ the body
        /// <summary>
        /// Builds the scrolling body, and rebuilds it wholesale when the card or the art lands.
        ///
        /// <b>Rebuilt rather than patched</b>, which is <see cref="ProfileScreen.BuildBody"/>'s
        /// call for its reason: a card's position is a running cursor rather than a number
        /// written down, so a card that changes height moves every card below it — and this one
        /// genuinely does, because a keeper who has never played the Infinite lane draws a
        /// shorter card than one who has. Redrawing five cards is far cheaper than the bugs of
        /// keeping their offsets in step by hand.
        /// </summary>
        void BuildBody()
        {
            if (!this) return;

            float scrolled = _stack ? _stack.anchoredPosition.y : 0f;

            if (_viewport)
            {
                // Hidden before it is destroyed: `Destroy` lands at the end of the frame, so a
                // region replaced in place is drawn over its replacement until then — and the
                // outgoing viewport still carries its invisible drag catcher.
                _viewport.gameObject.SetActive(false);
                Destroy(_viewport.gameObject);
            }

            _bodies.Clear();

            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, 24f);
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

            if (_card.IsValid)
            {
                BuildKeeperCard();
                BuildWatchCard();
                BuildCompanionCard();
                BuildLineCard();

                // **The grove card is held**, with the feature it was a door into. It carried
                // the piece count, when the picture was taken and the key into their
                // grovement, and all three are about a thing this build does not draw. The
                // stack is a cursor rather than a table of positions, so a card being absent
                // costs nothing and adding it back is one call (`BuildGroveCard`, deleted with
                // it — the history is the copy).
            }

            _stack.sizeDelta = new Vector2(0f, -_cursor + Gap);

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

            _entered = true;

            PaintStatus();
            PaintReport();
            PaintLine();
        }

        /// <summary>A card at the cursor, which then moves below it. <see cref="ProfileScreen.Section"/>.</summary>
        RectTransform Section(string name, float height, int order)
        {
            var card = UIKit.Img(name, _stack, Art.S("Ui/" + Skins.PlateBlue), Color.white,
                                 new Vector2(CardWidth, height), new Vector2(.5f, 1f),
                                 new Vector2(0f, _cursor - height * .5f));

            _cursor -= height + Gap;

            if (!_entered)
            {
                card.transform.localScale = Vector3.zero;
                Tween.Pop(card.transform, 0f, .55f, .08f + order * .07f);
            }

            return (RectTransform)card.transform;
        }

        static void CardTitle(Transform card, string key)
            => UIKit.Titled("Head", card, Loc.Get(key).ToUpperInvariant(), 30, Pal.Gold,
                            TextAnchor.MiddleLeft, new Vector2(CardWidth - 80f, 40f),
                            new Vector2(0f, 1f), new Vector2(40f + (CardWidth - 80f) * .5f, -44f),
                            3f, 3f);

        // ------------------------------------------------------------- the keeper
        void BuildKeeperCard()
        {
            // **Sized to the medallion rather than typed.** It was 400, which is the
            // portrait plus 34 of air at the top and 114 at the bottom — the whole card reading
            // as bottom-heavy because the number was a guess and the column beside the portrait
            // is what actually fills it. `Disc + 2 * Margin` is the one measurement that cannot
            // drift from the thing it is measuring.
            const float Disc = 252f, Margin = 34f;
            const float Height = Disc + 2f * Margin;

            var card = Section("Keeper", Height, 0);

            var medallion = UIKit.Img("Medallion", card, Art.Disc(256),
                                      Pal.A(Pal.Hex("#08333C"), .95f),
                                      new Vector2(Disc, Disc), new Vector2(.5f, .5f),
                                      new Vector2(-306f, 0f));
            UIKit.Halo(medallion.transform, Pal.Gold, 320f, .26f);
            var ring = UIKit.Img("Ring", medallion.transform, Art.Ring(256, 13f), Pal.A(Pal.Gold, .92f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            var face = UIKit.Img("Critter", medallion.transform, null, Color.white,
                                 new Vector2(186f, 186f), new Vector2(.5f, .5f), new Vector2(0f, 6f));
            face.preserveAspect = true;
            CompanionArt.Paint(face, _card.Companion());
            Tween.Bob((RectTransform)face.transform, 7f, 3.2f);

            var badge = UIKit.Img("LevelBadge", medallion.transform, Art.Disc(128), Pal.Gold,
                                  new Vector2(92f, 92f), new Vector2(1f, 0f), new Vector2(-6f, 6f));
            UIKit.Shrinkable(
                UIKit.Titled("N", badge.transform, _card.KeeperLevel.ToString(), 44,
                             new Color(.30f, .20f, .05f), TextAnchor.MiddleCenter,
                             new Vector2(84f, 60f), new Vector2(.5f, .5f), Vector2.zero,
                             outline: 0f, shadow: 0f), 22);

            // The two lines beside it, stacked from the card's own top edge so the column
            // and the portrait are measured against one thing rather than against each other.
            //
            // **It was four and the bottom two were the grove's** — what it is worth and the
            // stars that buys — so they are held with it. The remaining pair is re-centred
            // rather than left where it was: a column that keeps its old coordinates after
            // losing its lower half is a card whose contents have quietly climbed into its top
            // third, beside a portrait that still fills the whole of it.
            const float NameY = 44f, RibbonY = -42f;

            UIKit.Shrinkable(
                UIKit.Titled("Name", card, _card.Name, 50, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(560f, 62f), new Vector2(.5f, .5f),
                             new Vector2(160f, NameY), 4f, 4f), 28);

            var ribbon = UIKit.Img("Title", card, Art.S("Ui/ribbon_flat"), Color.white,
                                   new Vector2(380f, 74f), new Vector2(.5f, .5f),
                                   new Vector2(100f, RibbonY));
            UIKit.Shrinkable(
                UIKit.Titled("T", ribbon.transform, Loc.Get(KeeperTitle.KeyFor(_card.KeeperLevel)), 32,
                             new Color(.34f, .22f, .12f), TextAnchor.MiddleCenter,
                             new Vector2(340f, 50f), new Vector2(.5f, .5f), Vector2.zero,
                             outline: 0f, shadow: 2f), 20);

        }

        // ------------------------------------------------------- the Endless Watch
        /// <summary>
        /// How far this keeper has held the Infinite lane.
        ///
        /// <b>The percentile is read off the published distribution</b> rather than off a
        /// position, which is invariant 19c: nothing in this game maintains a global ordering, so
        /// "top 12%" is one document read shared by everybody and "rank 4,182" is a query that
        /// grows with the population for ever. An account with no run has no standing and no
        /// nought either — a bad score and no score are different things (invariant 43b).
        /// </summary>
        void BuildWatchCard()
        {
            // **A left-aligned label anchored to a card's left edge is positioned at its own
            // centre**, because `UIKit.Box` always pivots there (invariant 44d) — so the column
            // these two lines share is `left + width / 2` and not `left`. Written down once
            // rather than twice, because the second copy is the one that drifts.
            const float WatchTextLeft = 190f, WatchTextW = 560f;
            const float WatchTextX = WatchTextLeft + WatchTextW * .5f;

            var card = Section("Watch", 214f, 1);
            CardTitle(card, "ui.board.endless");

            bool played = _card.BestWave > 0;

            var glyph = UIKit.Img("Glyph", card, Art.S("Ui/ic_battle"), Color.white,
                                  new Vector2(76f, 76f), new Vector2(0f, .5f),
                                  new Vector2(104f, -14f));
            glyph.preserveAspect = true;
            if (played) UIKit.Halo(glyph.transform, Pal.Sun, 150f, .26f);

            UIKit.Shrinkable(
                UIKit.Titled("Wave", card,
                             played ? Loc.Format("ui.endless.best", _card.BestWave)
                                    : Loc.Get("ui.endless.unplayed"),
                             38, played ? Pal.Cream : new Color(1f, .96f, .88f, .55f),
                             TextAnchor.MiddleLeft, new Vector2(WatchTextW, 50f),
                             new Vector2(0f, .5f), new Vector2(WatchTextX, 2f), 3f, 3f), 22);

            int top = played ? GroveRanks.Waves.TopPercent(_card.BestWave) : 0;
            if (top <= 0) return;

            UIKit.Shrinkable(
                UIKit.Titled("Standing", card, Loc.Format("ui.endless.standing", top), 26,
                             Pal.Sun, TextAnchor.MiddleLeft, new Vector2(WatchTextW, 36f),
                             new Vector2(0f, .5f), new Vector2(WatchTextX, -44f), 3f, 0f), 18);
        }

        // ---------------------------------------------------------- the companions
        /// <summary>
        /// Which companions this keeper has gathered.
        ///
        /// <para>
        /// <b>Held is asked of the card, not of the catalog</b> — <see cref="GroveCard.Holds"/>
        /// composes the same two halves the player's own profile composes
        /// (<see cref="CompanionLedger.IsHeld(AvatarDefinition, int, System.Func{string, bool})"/>),
        /// over *their* purchases and *their* keeper level. Asking
        /// <see cref="AvatarCatalog.ReachedBy"/> here would answer half the rule under a name
        /// promising all of it, which is invariant 15a's trap said about somebody else's screen.
        /// </para>
        /// <para>
        /// <b>Nothing here is a control, and nothing unheld is drawn at all.</b> No prices, no
        /// padlocks and no tap: a padlock on a stranger's profile says "they have not bought
        /// this", which is not a fact this screen has any business drawing attention to — and a
        /// grid of thirty-one discs with nine lit reads as an inventory of somebody else's gaps
        /// rather than as their collection. The count in the corner says how far along they are;
        /// the grid says what they have.
        /// </para>
        /// </summary>
        void BuildCompanionCard()
        {
            var roster = AvatarCatalog.All;

            // **What they hold, not what the roster is.** Every companion drawn dim is a
            // sentence about what somebody else has *not* bought, on a screen with no control to
            // do anything about it — thirty-one discs of which nine are lit reads as an
            // inventory of their gaps rather than as a collection. Held-only reads as theirs,
            // and it is also what keeps the card proportional to what they have done: a keeper
            // who has gathered two draws two, and the count in the corner says the rest.
            var held = new List<AvatarDefinition>();
            foreach (var avatar in roster) if (_card.Holds(avatar)) held.Add(avatar);

            const int PerRow = 6;
            const float Cell = 148f, Step = 156f, RowGap = 26f;
            const float TitleRoom = 96f, Foot = 24f;

            int rows = Mathf.Max(1, (held.Count + PerRow - 1) / PerRow);

            // A keeper with none draws one short line rather than an empty grid, because an
            // empty grid says the same thing a failed load says — `AdOfferState`'s rule, which
            // this screen follows everywhere else.
            float height = held.Count > 0
                ? TitleRoom + rows * Cell + (rows - 1) * RowGap + Foot
                : TitleRoom + 70f;

            var card = Section("Companions", height, 2);
            CardTitle(card, "ui.profile.companions");

            UIKit.Shrinkable(
                UIKit.Titled("Count", card,
                             Loc.Format("ui.profile.unlocked", held.Count, roster.Count),
                             26, new Color(1f, .96f, .88f, .60f), TextAnchor.MiddleRight,
                             new Vector2(300f, 36f), new Vector2(1f, 1f), new Vector2(-190f, -44f),
                             3f, 0f), 18);

            if (held.Count == 0)
            {
                UIKit.Shrinkable(
                    UIKit.Titled("None", card, Loc.Get("ui.profile.public_no_companions"), 26,
                                 new Color(1f, .96f, .88f, .55f), TextAnchor.MiddleCenter,
                                 new Vector2(CardWidth - 160f, 40f), new Vector2(.5f, 1f),
                                 new Vector2(0f, -(TitleRoom + 20f)), 3f, 0f), 18);
                return;
            }

            for (int i = 0; i < held.Count; i++)
            {
                int row = i / PerRow;
                int col = i % PerRow;

                // Each row is centred on its own count, so a final short row does not lean left.
                int inRow = Mathf.Min(PerRow, held.Count - row * PerRow);
                float x = (col - (inRow - 1) * .5f) * Step;
                float y = -(TitleRoom + row * (Cell + RowGap) + Cell * .5f);

                var disc = UIKit.Img("A_" + held[i].Id, card, Art.Disc(160),
                                     Pal.A(Pal.Hex("#08333C"), .92f),
                                     new Vector2(Cell, Cell), new Vector2(.5f, 1f),
                                     new Vector2(x, y));

                // The one they are wearing is ringed in gold, which is the only distinction
                // this card draws — it is the companion on their row on the board, and a
                // profile that could not say which is the one thing the row already said.
                bool worn = string.Equals(held[i].Id, _card.AvatarId,
                                          System.StringComparison.Ordinal);

                var ring = UIKit.Img("Ring", disc.transform, Art.Ring(160, worn ? 9f : 6f),
                                     worn ? Pal.A(Pal.Gold, .95f) : new Color(1f, 1f, 1f, .22f));
                UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

                var portrait = UIKit.Img("Face", disc.transform, null, Color.white,
                                         new Vector2(Cell - 44f, Cell - 44f),
                                         new Vector2(.5f, .5f), new Vector2(0f, 4f));
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;
                CompanionArt.Paint(portrait, held[i]);
            }
        }

        // ---------------------------------------------------------------- the line
        /// <summary>
        /// The turrets this keeper takes into a siege, one per colour.
        ///
        /// <b>Resolved through <see cref="GroveCard.Line"/></b>, so a seat the card does not name
        /// and a turret this build has never heard of both draw the roster's starter — which is
        /// exactly what their own game draws. The seat's colour is the one thing a turret's
        /// silhouette cannot say, so it is the rim, which is how the map's own readout says it
        /// (<see cref="LoadoutBar"/>).
        /// </summary>
        void BuildLineCard()
        {
            // 350 rather than 330, and every number under it is measured from the title
            // rather than from the middle: the row is a cell, a caption and a five-rung ladder,
            // which is 196 + 34 + 26 of content plus the title's own 76 — at 330 the cell's top
            // edge stood 11 units under the title and the ladder ran out of the plate.
            const float Cell = 196f, StepX = 212f;
            const float TitleRoom = 84f, CaptionH = 34f, LadderH = 30f, Foot = 16f;

            float height = TitleRoom + Cell + CaptionH + LadderH + Foot;

            var card = Section("Line", height, 3);
            CardTitle(card, "ui.loadout.wards");

            var line = _card.Line(WardLedger.Catalog);

            // Measured down from the plate's top edge, so adding a line under a seat moves what
            // is under it rather than everything on the card.
            float cellY = height * .5f - TitleRoom - Cell * .5f;
            float captionY = cellY - Cell * .5f - CaptionH * .5f;
            float ladderY = captionY - CaptionH * .5f - LadderH * .5f;

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                char colour = WardLine.Colours[i];
                var model = line.At(i);

                float x = (i - (WardLine.Colours.Length - 1) * .5f) * StepX;

                var cell = UIKit.Img("Seat" + i, card, Art.S("Ui/" + Skins.Card), Color.white,
                                     new Vector2(Cell, Cell), new Vector2(.5f, .5f),
                                     new Vector2(x, cellY));

                var rim = UIKit.Img("Rim", cell.transform, Art.RoundOutline(26, 5f),
                                    Pal.A(SiegeView.TintOf(i), .85f),
                                    new Vector2(Cell - 10f, Cell - 10f));
                rim.raycastTarget = false;

                var body = UIKit.Img("Body", cell.transform, null, Color.white,
                                     new Vector2(Cell - 46f, Cell - 46f));
                body.preserveAspect = true;
                body.raycastTarget = false;

                // An `Image` with no sprite is a white rectangle rather than a blank (invariant
                // 7b), so it is off until the scope lands and `PaintLine` turns it on.
                body.enabled = false;
                _bodies.Add(body);

                UIKit.Shrinkable(
                    UIKit.Titled("Name" + i, card,
                                 model != null ? Loc.Get(model.NameKey) : string.Empty, 24,
                                 new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter,
                                 new Vector2(StepX - 8f, CaptionH), new Vector2(.5f, .5f),
                                 new Vector2(x, captionY), 3f, 0f), 16);

                // How far it has been taken. Drawn under the cell rather than on it, because a
                // badge over a turret is where the map's own readout puts a kit count and two
                // different meanings in one corner is how one of them stops being read.
                StarRow.Create(card, new Vector2(.5f, .5f), new Vector2(x, ladderY),
                               22f, 26f, _card.StarsOn(colour), false, WardStars.Most);
            }
        }

        /// <summary>Puts whichever turret bodies are in hand onto the row. <see cref="LoadoutBar.Dress"/>.</summary>
        void PaintLine()
        {
            if (_bodies.Count == 0 || !_card.IsValid) return;

            var line = _card.Line(WardLedger.Catalog);

            for (int i = 0; i < _bodies.Count; i++)
            {
                var model = line.At(i);
                if (model == null || _bodies[i] == null) continue;

                var sprite = AssetLibrary.Sprite(AssetManifest.WardArt(model, i));

                _bodies[i].sprite = sprite;
                _bodies[i].enabled = sprite != null;
            }
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, HeaderHeight + 30f + SafeArea.Top);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            var chrome = Safe;

            var banner = Scenery.TitleRibbon(chrome, Loc.Get("ui.profile.public_title").ToUpperInvariant(),
                                             new Vector2(470f, 128f), new Vector2(.5f, 1f),
                                             new Vector2(0f, -106f), 36, 20f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            UIKit.IconButton("Back", chrome, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<LeaderboardScreen>());

            // Small, quiet, and in the corner opposite Back. A report control is not something a
            // screen should invite — it is something a player has to be able to find once they
            // have already decided — so it is sized and coloured like chrome rather than like an
            // action. `GroveVisitScreen` draws the same control in the same corner on purpose:
            // the two screens are one keeper, and a control that moves between them is a control
            // somebody has to look for twice.
            _report = UIKit.TextButton("Report", chrome, Skins.Alternate,
                                       Loc.Get("ui.visit.report"), 24,
                                       new Vector2(232f, 72f),
                                       new Vector2(1f, 1f), new Vector2(-136f, -104f),
                                       OnReport);

            if (_report != null && _report.Label != null) UIKit.Shrinkable(_report.Label, 16);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Safe, string.Empty, 28, new Color(1f, .96f, .88f, .78f),
                             TextAnchor.UpperCenter, new Vector2(760f, 160f), new Vector2(.5f, 1f),
                             new Vector2(0f, -(HeaderHeight + 120f)), 3f, 0f), 18);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            PaintStatus();
            PaintReport();
        }

        /// <summary>
        /// Why the page is empty, when it is.
        ///
        /// Four states and each renders its own sentence, which is <c>AdOfferState</c>'s rule: a
        /// blank page with no explanation is how a player concludes a feature is broken, and
        /// three of these four are perfectly ordinary — a keeper who opted out after the board
        /// was built, a fetch that has not landed, and a network that is not there.
        /// </summary>
        void PaintStatus()
        {
            if (!_status) return;

            if (_card.IsValid) _status.text = string.Empty;
            else if (_fetching) _status.text = Loc.Get("ui.board.loading");
            else if (!GroveBoard.IsAvailable) _status.text = Loc.Get("ui.board.offline");

            // **The fourth state this method's own summary claimed to have, and did not.** A
            // card that could not be fetched was reported as `ui.visit.gone` — "this keeper is
            // no longer on the boards" — whichever way the request had failed, so a player who
            // opened a row in a tunnel was told something false about another person, in a
            // sentence with no hint that the phone was the problem. The failure is the same
            // `CloudResult` either way and nothing in it distinguishes them, which is why the
            // radio is what separates the two sentences here.
            else if (_failed)
                _status.text = Loc.Get(Net.Offline ? "ui.visit.no_connection" : "ui.visit.gone");

            else _status.text = Loc.Get("ui.board.loading");
        }

        // ---------------------------------------------------------------- report
        /// <summary>
        /// Whether the control is worth offering. <see cref="GroveVisitScreen.PaintReport"/>'s
        /// rule, said in the one other place a keeper is looked at.
        /// </summary>
        void PaintReport()
        {
            if (_report == null) return;

            bool worth = _card.IsValid
                         && !string.IsNullOrEmpty(_ownerId)
                         && _ownerId != CloudState.UserId
                         && GroveBoard.IsAvailable;

            _report.gameObject.SetActive(worth);
            if (!worth) return;

            // Dead only once *every* subject has been reported. A control greyed after one of
            // two would tell somebody they had already reported a grovement they have never
            // looked at — see `KeeperReports`.
            bool spent = KeeperReports.AllSent(_ownerId);

            _report.Interactable = !spent && !_reporting;

            if (_report.Label != null)
                _report.Label.text = Loc.Get(spent ? "ui.visit.report_sent" : "ui.visit.report");
        }

        void OnReport()
        {
            if (_reporting || KeeperReports.AllSent(_ownerId)) return;

            Flow.Modal<ReportOverlay>(v =>
            {
                v.KeeperId = _ownerId;
                v.OnConfirm = Send;
            });
        }

        void Send(ReportSubject subject) => Run(async token =>
        {
            if (_reporting) return;

            _reporting = true;
            PaintReport();

            // Captured before the await rather than read after it, which is
            // `GroveVisitScreen.Send`'s rule: a moderation action recorded against the wrong
            // account is not a failure worth leaving one line away.
            string owner = _ownerId;

            var (result, outcome) = await GroveBoard.ReportAsync(owner, subject, token);

            _reporting = false;

            if (!Living) return;

            PaintReport();

            string line =
                !result.Ok || outcome == NameReportOutcome.Unavailable ? "ui.visit.report_failed"
                : outcome == NameReportOutcome.Throttled ? "ui.visit.report_limit"
                : "ui.visit.report_done";

            Scenery.Toast(Content, Loc.Get(line), Pal.Cream, 2.6f, new Vector2(.5f, 0f), 320f);
        });

        public override bool OnBack()
        {
            Flow.Go<LeaderboardScreen>();
            return true;
        }
    }
}
