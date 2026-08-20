using System;
using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
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
    public sealed class ProfileScreen : View, IDrawsCompanionArt
    {
        public override string Track => "mus_menu";

        const float CardWidth = 980f;
        const float Gap = 28f;
        const float HeaderHeight = 250f;

        /// <summary>Companions shown on the profile itself; the rest live behind See All.</summary>
        const int PreviewCount = 4;

        RectTransform _viewport, _stack;
        float _cursor;                       // top of the next card, negative and falling

        Image _portrait;
        Text _nameLabel;
        Transform _companionRow;
        Text _companionCount;

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .22f);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            BuildBody();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Profile);

            // The preview row draws a handful of portraits, so the roster's art is
            // wanted here too — and released the moment this screen goes away. Requested
            // after the row exists so the repaint has something to paint.
            CompanionArt.OpenAsync(() => { if (this) PaintCompanions(); });

            // See CompanionScreen for why this is an event and not a callback: the unlock
            // panel has three exits and only one of them used to report a purchase.
            Progression.CompanionLedger.Changed += RepaintCompanions;

            // And on the worn companion separately, because a purchase records the two one
            // after the other and the ledger's event arrives before the wear — see
            // Profile.AvatarChanged. The medallion showed the old friend until this existed.
            Profile.AvatarChanged += RepaintCompanions;

            AvatarCatalog.Changed += RepaintCompanions;
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

            if (Flow.Current is CompanionScreen) return;
            CompanionArt.CloseUnlessWanted();
        }

        // -------------------------------------------------------------- scroller
        void BuildBody()
        {
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
            BuildRecordCard();
            BuildCompanionCard();
            BuildAccountCard();
            _stack.sizeDelta = new Vector2(0f, -_cursor + Gap);

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
        }

        /// <summary>
        /// A card at the cursor, which then moves below it. Returns the card's own
        /// transform, so everything inside is positioned relative to its centre and a
        /// card can be reordered without touching a single number in it.
        /// </summary>
        RectTransform Section(string name, float height, int order)
        {
            var card = UIKit.Img(name, _stack, Art.Round(34), new Color(.03f, .10f, .13f, .80f),
                                 new Vector2(CardWidth, height), new Vector2(.5f, 1f),
                                 new Vector2(0f, _cursor - height * .5f));
            var edge = UIKit.Img("Edge", card.transform, Art.RoundOutline(34, 3f), new Color(1f, 1f, 1f, .13f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            _cursor -= height + Gap;

            card.transform.localScale = Vector3.zero;
            Tween.Pop(card.transform, 0f, .55f, .08f + order * .07f);
            return (RectTransform)card.transform;
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
            var track = UIKit.Img("XpTrack", card, Art.Round(18), new Color(.01f, .05f, .07f, .92f),
                                  new Vector2(612f, 40f), new Vector2(.5f, .5f), new Vector2(178f, -34f));
            var fill = UIKit.Img("XpFill", track.transform, Art.Round(15), Pal.Mint,
                                 new Vector2(0f, 30f), new Vector2(0f, .5f), new Vector2(5f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            float full = 602f * level.Progress01;
            Tween.Run(.85f, Ease.OutCubic, t =>
            {
                if (!fillRT) return;
                fillRT.sizeDelta = new Vector2(full * t, 30f);
                fill.color = Color.Lerp(Pal.Aqua, Pal.Mint, t);
            }, fill).Delay(.35f);

            UIKit.Titled("XpText", card,
                         level.IsMaxLevel
                             ? Loc.Get("ui.profile.xp_max")
                             : Loc.Format("ui.profile.xp", level.XpIntoLevel, level.XpForNextLevel),
                         27, new Color(1f, .96f, .86f, .72f), TextAnchor.MiddleLeft,
                         new Vector2(612f, 34f), new Vector2(.5f, .5f), new Vector2(178f, -84f), 3f, 0f);

            int nextTier = KeeperTitle.NextTierLevel(level.Level);
            if (nextTier > 0)
            {
                UIKit.Titled("NextTitle", card,
                             Loc.Format("ui.profile.next_title", Loc.Get(KeeperTitle.KeyFor(nextTier)), nextTier),
                             25, new Color(1f, .95f, .84f, .5f), TextAnchor.MiddleLeft,
                             new Vector2(612f, 32f), new Vector2(.5f, .5f), new Vector2(178f, -128f), 3f, 0f);
            }
        }

        // ------------------------------------------------------------ the record
        void BuildRecordCard()
        {
            var card = Section("Record", 430f, 1);
            CardTitle(card, "ui.profile.record", CardWidth);

            // Shown against the total the catalog holds, which is where the home screen's
            // old grove bar went when that panel became the daily one. A bare star count
            // says how far you have come; this one also says how far there is to go.
            Tile(card, -310f, 40f, "ic_star", $"{Profile.TotalStars}/{Profile.MaxStars}",
                 "ui.profile.stars", Pal.Gold);
            Tile(card, 0f, 40f, "ic_home", $"{Profile.ChaptersCompleted}/{Profile.ChapterCount}",
                 "ui.profile.chapters", Pal.Aqua);
            Tile(card, 310f, 40f, "ic_star3d", $"{Profile.PerfectGlades}", "ui.profile.perfect", Pal.Sun);

            Tile(card, -310f, -128f, "ic_check", $"{PlayerProgression.ClearedGlades}", "ui.profile.glades", Pal.Mint);
            Tile(card, 0f, -128f, "ic_chest", Compact.Number(Profile.Coins), "ui.profile.coins", Pal.Gold);
            Tile(card, 310f, -128f, "ic_gem", Compact.Number(Profile.Gems), "ui.profile.gems", Pal.Bloom);
        }

        static void Tile(Transform card, float x, float y, string icon, string value, string labelKey, Color tint)
        {
            var bg = UIKit.Img("Tile_" + labelKey, card, Art.Round(24), new Color(1f, 1f, 1f, .055f),
                               new Vector2(290f, 148f), new Vector2(.5f, .5f), new Vector2(x, y));
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(24, 2f), Pal.A(tint, .32f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .22f),
                      new Vector2(120f, 120f), new Vector2(0f, .5f), new Vector2(56f, 12f));
            var ic = UIKit.Img("Icon", bg.transform, Art.S("Ui/" + icon), Color.white,
                               new Vector2(62f, 62f), new Vector2(0f, .5f), new Vector2(56f, 12f));
            ic.preserveAspect = true;

            UIKit.Titled("V", bg.transform, value, 44, Pal.Cream, TextAnchor.MiddleLeft,
                         new Vector2(160f, 54f), new Vector2(0f, .5f), new Vector2(186f, 14f), 3f, 3f);
            UIKit.Titled("L", bg.transform, Loc.Get(labelKey), 24, new Color(1f, .96f, .88f, .58f),
                         TextAnchor.MiddleCenter, new Vector2(266f, 32f), new Vector2(.5f, 0f),
                         new Vector2(0f, 26f), 3f, 0f);
        }

        // -------------------------------------------------------- the companions
        void BuildCompanionCard()
        {
            var card = Section("Companions", 340f, 2);
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
                var lockIcon = UIKit.Img("Lock", disc.transform, Art.S("Ui/padlock"), Color.white,
                                         new Vector2(64f, 64f), new Vector2(.5f, 0f), new Vector2(0f, 6f));
                lockIcon.preserveAspect = true;
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

            Audio.Sfx("chime2", .5f);
            Haptic.Tap();
            if (_portrait) Burst.Sparks(_portrait.transform, Vector2.zero, Pal.Gold, 12, 190f, 26f, .6f);
        }

        // ----------------------------------------------------------- the account
        void BuildAccountCard()
        {
            var card = Section("Account", 360f, 3);
            CardTitle(card, "ui.profile.account", CardWidth);

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

            var glyph = UIKit.Img("Glyph", card, Art.S("Ui/ic_key"), Color.white,
                                  new Vector2(76f, 76f), new Vector2(0f, .5f), new Vector2(104f, 58f));
            glyph.preserveAspect = true;
            UIKit.Halo(glyph.transform, linked ? Pal.Mint : Pal.Rose, 150f, .28f);

            UIKit.Titled("Status", card, Loc.Get(statusKey), 34,
                         !available ? new Color(1f, .95f, .86f, .6f) : linked ? Pal.Mint : Pal.Rose,
                         TextAnchor.MiddleLeft, new Vector2(560f, 44f), new Vector2(0f, .5f),
                         new Vector2(440f, 58f), 3f, 3f);

            UIKit.Titled("Why", card, Loc.Get(mismatched ? "ui.profile.mismatch_hint"
                                             : linked ? "ui.profile.linked_hint" : "ui.profile.guest_hint"),
                         25, new Color(1f, .96f, .88f, .58f), TextAnchor.UpperCenter,
                         new Vector2(800f, 70f), new Vector2(.5f, .5f), new Vector2(0f, 6f), 3f, 0f,
                         wrap: true);

            if (!available) return;

            UIKit.TextButton("Manage", card, mismatched ? "btn_red" : linked ? "btn_blue" : "btn_green",
                             Loc.Get(mismatched ? "ui.profile.fix"
                                   : linked ? "ui.profile.manage" : "ui.profile.protect"), 34,
                             new Vector2(460f, 110f), new Vector2(.5f, 0f), new Vector2(0f, 64f),
                             () => Flow.Modal<AccountOverlay>());
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

            var banner = UIKit.Img("Banner", Safe, Art.S("Ui/banner"), Color.white,
                                   new Vector2(520f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -142f));
            UIKit.Titled("Title", banner.transform, Loc.Get("ui.profile.title").ToUpperInvariant(), 40,
                         new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);
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
