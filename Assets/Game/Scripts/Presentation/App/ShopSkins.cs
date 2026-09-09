using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What the storefront is made of: the card frames, the price bars, the tabs, the
    /// currency troughs and the badges, all cut from one bought interface kit.
    ///
    /// <para>
    /// <b>It is a separate class from <see cref="Skins"/> on purpose, and only for now.</b>
    /// <c>Skins</c> names the controls every screen in the game shares — a back key, an
    /// affirmative pill — so repointing it restyles fifteen screens at once, before anybody
    /// has held one of them. These are the same idea confined to the shop, which is the one
    /// screen where the look was judged worth changing first. Rolling the kit out is then
    /// moving names from here to there, rather than rebuilding anything.
    /// </para>
    /// <para>
    /// <b>The kit and the merchandise come from one pack, which is the whole argument.</b>
    /// The coin and gem pictures on these cards are cut by <c>Tools/make_shop_art.py</c> from
    /// the same Layer Lab set this furniture is cut from by <c>Tools/make_ui_kit_art.py</c>.
    /// A storefront whose furniture and whose goods were drawn by different hands is what a
    /// player reads as unfinished without being able to say why, and it is the one screen in
    /// the game where that costs money rather than goodwill.
    /// </para>
    /// <para>
    /// <b>Every frame is nine-sliced, and the border is measured rather than typed.</b> See
    /// the cutting tool: the border lives in the importer, <c>UIKit.Img</c> turns on
    /// <c>Image.Type.Sliced</c> for any sprite that has one, and a sprite that arrived
    /// without one would stretch its corners with nothing anywhere able to notice.
    /// </para>
    /// </summary>
    public static class ShopSkins
    {
        // ------------------------------------------------------------------ the ground
        /// <summary>
        /// The page behind the cards: a deep, flat blue rather than the forest the rest of
        /// the game stands in.
        ///
        /// Flat on purpose. Every card on this screen is a saturated block of colour with a
        /// painted pile on it, so the one thing the ground has to do is not compete — which
        /// is <c>CRAFT.md</c>'s rule about the board's plate, asked of a storefront. It is
        /// the kit's own blue taken two steps down in value, so the furniture sits on a
        /// darker shade of itself instead of on a second designer's idea of blue.
        /// </summary>
        public static readonly Color Ground = new Color(.055f, .118f, .278f, 1f);

        /// <summary>The same blue one step up, for the header band over the cards.</summary>
        public static readonly Color Band = new Color(.043f, .180f, .451f, 1f);

        // ------------------------------------------------------------------ the cards
        /// <summary>
        /// A shelf's card frame. Colour is what tells one shelf from another at a glance,
        /// which is why it keys on the shelf rather than on the rung: a ladder is already
        /// said by the picture growing (<c>ShopArt</c>), and saying it twice would leave
        /// nothing to say which tab you are on.
        ///
        /// Three frames against five shelves, so two pairs share. That is deliberate rather
        /// than a shortage: the two that share are never on screen together.
        /// </summary>
        public static string Frame(StoreShelf shelf)
        {
            switch (shelf)
            {
                case StoreShelf.Coins: return "Ui/Kit/frame_yellow";
                case StoreShelf.Bundles: return "Ui/Kit/frame_green";
                case StoreShelf.Utilities: return "Ui/Kit/frame_green";
                default: return "Ui/Kit/frame_purple";   // gems, and the supplies shelf
            }
        }

        /// <summary>The frame a card wears when it is drawn outside a shelf.</summary>
        public const string PlainFrame = "Ui/Kit/frame_purple";

        // ------------------------------------------------------------------ the buttons
        /// <summary>
        /// A real-money price: the loudest thing on the card, and meant to be.
        ///
        /// <para>
        /// <b>It keys on the shelf because it has to contrast with the frame it stands on.</b>
        /// Yellow is the right answer on a purple or a green card and the wrong one on the
        /// coin shelf, where a yellow bar on a yellow frame is a button that disappears — the
        /// one control on the card that must never be hard to find. Only a render could see
        /// that: every gate here was green over it, because no gate opens a PNG.
        /// </para>
        /// </summary>
        public static string Buy(StoreShelf shelf)
            => shelf == StoreShelf.Coins ? "Ui/Kit/btn_blue" : "Ui/Kit/btn_yellow";

        /// <summary>A price paid in gems, which wears the gems' own colour.</summary>
        public const string Gem = "Ui/Kit/btn_purple";

        /// <summary>
        /// What a button that cannot be pressed is tinted by — owned, included, pending,
        /// mid-purchase.
        ///
        /// <para>
        /// A tint on <em>the same sprite the live button uses</em> rather than a fourth
        /// colour, because <c>Image.color</c> is a multiply: a grey over the face it would
        /// have worn reads as that control turned down, where a second sprite would read as a
        /// different control. Invariant 37l's distinction, met on a button instead of a
        /// turret — and it is why nothing here needs a disabled skin at all.
        /// </para>
        /// </summary>

        /// <summary>What <see cref="Idle"/> is tinted by. See its note.</summary>
        public static readonly Color Muted = new Color(.62f, .66f, .72f, 1f);

        // ------------------------------------------------------------------ the marks
        /// <summary>The starburst a featured card wears.</summary>
        public const string Badge = "Ui/Kit/badge_red";

        /// <summary>The ribbon a card wears when its rung carries a bonus.</summary>
        public const string Ribbon = "Ui/Kit/ribbon";

        // ------------------------------------------------------------------ the header
        /// <summary>The trough a currency readout sits in.</summary>
        public const string Pill = "Ui/Kit/pill";

        /// <summary>The "+" on the end of a currency readout.</summary>
        public const string Add = "Ui/Kit/add_yellow";

        /// <summary>The shelf switcher's chip, lit and unlit.</summary>
        public const string TabOn = "Ui/Kit/tab_on";
        public const string TabOff = "Ui/Kit/tab_off";

        /// <summary>
        /// What a currency trough is filled with. Darker than the ground, because a readout
        /// is a hole in the header rather than a thing standing on it.
        /// </summary>
        public static readonly Color Trough = new Color(.031f, .075f, .180f, .96f);
    }
}
