using GlimmerGrove.Localization;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The beat between paying and being thanked: a turning ring, the thing that was bought,
    /// and the word for what is happening to it.
    ///
    /// <para>
    /// <b>What it replaces was a word on the face of a button.</b> A purchase arrives from the
    /// store as an unfinished transaction and is only ours once the server has honoured it
    /// (invariant 18a), which is a round trip away — and for the whole of that round trip the
    /// only thing this game said about somebody's money was that the card they tapped now read
    /// ARRIVING. On the shop screen that is small; raised over a lost run from
    /// <c>GemShopOverlay</c> it is a player who has paid to save a glade, looking at a shelf
    /// that has quietly greyed one cell. The payment is the biggest thing that happens on
    /// either screen and it deserves the middle of it.
    /// </para>
    /// <para>
    /// <b>Raised from <c>Boot</c>, for <c>ReceiptQueue</c>'s reason exactly.</b> A payment sheet
    /// outlives the screen that opened it — on Android it outlives the process — so a shop
    /// screen listening for its own purchases would miss the ones that matter most. Hung on
    /// <c>StoreService.CheckoutLanded</c>, this appears wherever the player is standing, and the
    /// receipt panel that follows is hung on the same place for the same reason.
    /// </para>
    /// <para>
    /// <b>Three things make it impossible to get stuck, and they are deliberately not the same
    /// mechanism.</b> It closes when the transaction stops being owed, which
    /// <see cref="ArrivalWatch"/> reads off <c>StoreService</c> itself rather than off an event,
    /// so no ending can be missed. It offers a way out after <see cref="ArrivalWatch.Patience"/>,
    /// because a refused receipt is retried for the life of the install and "wait for it" is
    /// therefore not bounded. And it is an ordinary modal, so the back key closes it and a
    /// screen change destroys it like everything else in the stack. Nothing here is the only
    /// thing standing between the player and their game.
    /// </para>
    /// <para>
    /// <b>Nothing depends on it, which is the property worth keeping.</b> The purchase is banked
    /// by the store and honoured by the server whether this panel is drawn, dismissed, or never
    /// raised at all; the receipt is raised by <c>ReceiptQueue</c> off its own event. This is
    /// reporting and only reporting — the same bargain <c>ReceiptQueue</c> makes when it says
    /// the money is not in there.
    /// </para>
    /// <para>
    /// Drawn by <c>Tools/render_arrival.py</c>, which is the only thing that can say whether any
    /// of it reads.
    /// </para>
    /// </summary>
    public sealed class ShopArrivalOverlay : ModalView
    {
        /// <summary>
        /// Says that a purchase the player just paid for has landed and is being honoured.
        ///
        /// <para>
        /// <b>Deferred by <see cref="ArrivalWatch.Grace"/> rather than raised on the spot.</b> A
        /// redemption on a warm connection can finish inside a few hundred milliseconds, and a
        /// panel that springs in with a chime and leaves again before the receipt arrives is a
        /// stutter between the payment sheet and the thank-you. The wait is re-checked when the
        /// beat comes round, so the fast path draws nothing at all.
        /// </para>
        /// <para>
        /// <b>A second arrival joins the panel that is up rather than raising another.</b>
        /// <c>Flow.Modal</c> refuses a duplicate by type and hands back the live one
        /// <em>unconfigured</em>, which is right and would quietly drop the second transaction —
        /// so the live panel is asked for by name and told, which is the shape that refusal is
        /// meant to be used with.
        /// </para>
        /// </summary>
        public static void Show(string transactionKey, StoreProduct product)
        {
            if (string.IsNullOrEmpty(transactionKey)) return;

            // Owned by nothing, on purpose. A tween owned by the screen that was up when the
            // purchase landed would be dropped the moment the player navigated
            // (Tween.Orphaned), and a purchase is not the screen's business — it is the app's.
            Tween.After(ArrivalWatch.Grace, () => Raise(transactionKey, product));
        }

        static void Raise(string transactionKey, StoreProduct product)
        {
            // Asked again rather than remembered: the whole point of the grace period is that
            // the answer may have changed inside it.
            if (!StoreService.IsPending(transactionKey)) return;

            var live = Flow.LiveModal<ShopArrivalOverlay>();
            if (live != null) { live.Also(transactionKey); return; }

            Flow.Modal<ShopArrivalOverlay>(v =>
            {
                v._first = transactionKey;
                v._product = product;
            });
        }

        // ------------------------------------------------------------------- geometry
        // Absolute offsets rather than a cursor, on ShopSupplyOverlay's judgement: the rows are
        // fixed and the only thing that moves is whether the last one is there at all, which is
        // a term in the sum below rather than a second layout. Both heights are comfortably
        // inside PanelStack.TallestPanel (1716) — the shortest canvas this game is drawn on,
        // with the title ribbon's overhang counted at both ends.
        const float PanelW = 820f;
        const float HeadRoom = 150f;
        const float RingSize = 340f;
        const float ArtSize = 260f;
        const float NameH = 56f;
        const float NoteH = 96f;
        const float ButtonH = 112f;

        /// <summary>
        /// Clear air under the last row, and it is deeper than the panels this borrows from.
        ///
        /// <para>
        /// <c>panel_main</c> carries a 60-unit nine-slice rim, so a button seated in the fifties
        /// — which is what <c>ShopGrantOverlay</c> and <c>ShopSupplyOverlay</c> both do — has its
        /// foot on the parchment's own lip rather than on its face. Caught by the render and
        /// nothing else; every numeric gate here is happy with a panel that adds up.
        /// </para>
        /// </summary>
        const float FootRoom = 88f;

        /// <summary>
        /// Ink and a warm accent, for <c>ShopSupplyOverlay.Ink</c>'s reason: <c>panel_main</c> is
        /// a light parchment, so cream copy on it is held apart from its ground by an outline
        /// alone and the board's own accents were reported unreadable on it.
        /// </summary>
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Accent = new Color(.86f, .47f, .12f);

        // --------------------------------------------------------------------- state
        readonly ArrivalWatch _watch = new ArrivalWatch();

        string _first;
        StoreProduct _product;

        /// <summary>
        /// Which of the two panels is currently drawn, so the rebuild happens once rather than
        /// on every frame the watch reports something.
        /// </summary>
        bool _drawnRelaxed;

        RectTransform _art;
        Text _name;

        /// <summary>
        /// Heard outside <see cref="Build"/>, because <see cref="ModalView.Rebuild"/> runs it
        /// again — and a subscription taken there would be taken twice and dropped once.
        /// </summary>
        void Awake() => StoreService.Granted += OnGranted;

        void OnDestroy() => StoreService.Granted -= OnGranted;

        /// <summary>
        /// The panel, in whichever of its two states the watch is in.
        ///
        /// <para>
        /// <b>Two heights rather than one with the button's room reserved</b>, which is what
        /// <see cref="ModalView.Rebuild"/> exists for. Reserving it leaves a third of the panel
        /// empty for the entire ordinary case — every purchase that lands inside
        /// <see cref="ArrivalWatch.Patience"/>, which is meant to be all of them — and an empty
        /// band under a sentence reads as a panel that has lost something. The growth happens
        /// once, six seconds in, on a panel nobody is touching, and it happens in the same beat
        /// as the button springing into it.
        /// </para>
        /// </summary>
        protected override void Build()
        {
            // Idempotent, and it has to be: a rebuild runs this again, by which time the key is
            // either already watched or already finished with. Both answer false and change
            // nothing.
            _watch.Watch(_first);
            _drawnRelaxed = _watch.Relaxed;

            float y = HeadRoom;
            float ringY = y + RingSize * .5f;   y += RingSize + 18f;
            float nameY = y + NameH * .5f;      y += NameH + 12f;
            float noteY = y + NoteH * .5f;      y += NoteH + 22f;

            float buttonY = y + ButtonH * .5f;
            if (_drawnRelaxed) y += ButtonH + 22f;

            y += FootRoom;

            // Never dismissed by a stray tap on the scrim, for ShopGrantOverlay's reason: this
            // is about a payment that has just been taken, and one a thumb landing anywhere can
            // flick away is one a player can miss entirely and then wonder about. The back key
            // works from the first frame, and the button arrives once the wait stops being
            // ordinary — see OnBack and Advance.
            var panel = MakePanel(new Vector2(PanelW, y), Loc.Get("ui.shop.arriving"),
                                  dismissOnScrim: false);

            UIKit.Halo(panel, Pal.Sun, 620f, .20f, new Vector2(0f, y * .5f - ringY));

            BuildRing(panel, ringY);

            // Most of the ring rather than a fraction of it, which is the rule ShopArt.Paint
            // already follows on a card: every one of these sprites is square with a good deal
            // of air baked in, so a box fitted to the hole leaves the picture floating in the
            // middle of it. Measured rather than guessed — the widest of them fills 94% of its
            // own frame, so this is the number that keeps even that one clear of the track.
            _art = UIKit.Box("Art", panel, Vector2.one * ArtSize, new Vector2(.5f, 1f),
                             new Vector2(0f, -ringY));

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", panel, string.Empty, 34, Ink, TextAnchor.MiddleCenter,
                             new Vector2(660f, NameH), new Vector2(.5f, 1f),
                             new Vector2(0f, -nameY), outline: 0f, shadow: 0f), 20);

            UIKit.Shrinkable(
                UIKit.Titled("Note", panel,
                             Loc.Get(_drawnRelaxed ? "ui.shop.awaiting" : "ui.shop.arriving_note"),
                             27, Pal.A(Ink, .88f), TextAnchor.UpperCenter,
                             new Vector2(660f, NoteH), new Vector2(.5f, 1f),
                             new Vector2(0f, -noteY), outline: 0f, shadow: 0f, wrap: true), 18);

            if (_drawnRelaxed)
            {
                // Skins.Alternate rather than the resting grey: grey means "not a control right
                // now" (see Skins) and this is the one control on the panel. It springs in
                // rather than appearing, because the panel grew to make room for it and the two
                // ought to read as one movement.
                var way = UIKit.TextButton("Out", panel, Skins.Alternate,
                                           Loc.Get("ui.common.got_it"), 32,
                                           new Vector2(440f, ButtonH), new Vector2(.5f, 1f),
                                           new Vector2(0f, -buttonY), () => Close());

                way.transform.localScale = Vector3.zero;
                Tween.Scale(way.transform, 1f, .34f, Ease.OutBack);
            }

            PaintSubject();
        }

        /// <summary>
        /// The ring: a faint track, and a sweep turning inside it.
        ///
        /// <para>
        /// A track as well as a sweep because an arc alone on a light ground reads as a stray
        /// mark rather than as a dial — the same reason <c>BusyVeil</c> can draw its arc bare and
        /// this cannot: that one sits on a dark plate of its own. On the unscaled clock like
        /// every other animation here, because a modal takes <c>Time.timeScale</c> to nought
        /// (invariant 30h) and a spinner that has stopped turning is worse than no spinner at all.
        /// </para>
        /// </summary>
        static void BuildRing(RectTransform panel, float ringY)
        {
            UIKit.Img("Track", panel, Art.Ring(160, 11f), Pal.A(Ink, .26f),
                      Vector2.one * RingSize, new Vector2(.5f, 1f), new Vector2(0f, -ringY));

            var sweep = UIKit.Img("Sweep", panel, Art.Arc(160, 11f, .28f), Accent,
                                  Vector2.one * RingSize, new Vector2(.5f, 1f),
                                  new Vector2(0f, -ringY));

            var spin = (RectTransform)sweep.transform;
            Tween.Run(1.15f, Ease.Linear,
                      t => { if (spin) spin.localRotation = Quaternion.Euler(0f, 0f, -360f * t); },
                      spin, "spin").Loop(-1, false);
        }

        /// <summary>
        /// Takes on a second transaction that landed while this panel was up.
        ///
        /// <para>
        /// <b>And stops naming a product the moment it is about more than one.</b> Two purchases
        /// seconds apart is ordinary — a mistap, or a second pack bought straight after the
        /// first — and a panel that went on showing the first one's picture would be telling the
        /// player something true about half of what they are waiting for. The ring and the word
        /// are honest about any number of them; the picture is not, so it goes.
        /// </para>
        /// </summary>
        void Also(string transactionKey)
        {
            if (!_watch.Watch(transactionKey)) return;

            _product = null;
            PaintSubject();
        }

        /// <summary>What is arriving, when that can be said, and nothing when it cannot.</summary>
        void PaintSubject()
        {
            bool named = _product != null && _product.IsValid && _watch.Count <= 1;

            ShopArt.Paint(_art, named ? _product : null);

            // A count rather than a blank when there is more than one. An empty ring over an
            // empty line reads as a panel that has lost its picture; the number says what the
            // panel is actually about, and it can only ever be two or more here — a single
            // purchase is named, so there is no plural to get wrong.
            _name.text = named ? Loc.Get(_product.NameKey)
                       : _watch.Count > 1 ? Loc.Format("ui.shop.arriving_many", _watch.Count)
                       : string.Empty;
        }

        void OnGranted(StoreGrant grant) => Advance(0f);

        void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>
        /// One frame of the wait. The watch owns both questions; this only draws the answers.
        /// </summary>
        void Advance(float step)
        {
            if (IsLeaving) return;
            if (!_watch.Tick(step)) return;

            if (_watch.Settled) { Settle(); return; }
            if (_watch.Relaxed != _drawnRelaxed) { Rebuild(); return; }

            PaintSubject();
        }

        /// <summary>
        /// The purchase is honoured. Steps out from under the receipt rather than waiting to be
        /// dismissed.
        ///
        /// <para>
        /// Quiet, for <c>GemShopOverlay.OnGranted</c>'s reason: the receipt's own chime is
        /// already playing and a backing-out whoosh underneath a celebration is one sound too
        /// many. And it can settle with no receipt behind it — a re-delivery the server had
        /// already granted celebrates nothing (see <c>StoreService</c>'s <c>worthShowing</c>) —
        /// which is exactly why this closes on the transaction rather than on the panel it
        /// usually hands over to.
        /// </para>
        /// </summary>
        void Settle() => Close(quiet: true);

        /// <summary>
        /// The hardware key closes this, and answering it is not optional furniture.
        ///
        /// <para>
        /// <c>Flow.HandleBack</c> walks the stack downwards until something says it dealt with
        /// the press, so a panel that stays silent hands it to whatever is <em>underneath</em> —
        /// which over a lost run is the continue offer, whose back key declines and ends the run
        /// (<c>GemShopOverlay.OnBack</c> records that exact bug). Nothing is lost by closing: the
        /// purchase is with the store and the server either way, and <c>StoreService</c> goes on
        /// retrying it on <c>Boot</c>'s clock.
        /// </para>
        /// </summary>
        public override bool OnBack()
        {
            Close();
            return true;
        }
    }
}
