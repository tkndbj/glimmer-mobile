using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What this UI is made of: which skin each role wears, and the few colours that are not
    /// carried by a sprite.
    ///
    /// <para>
    /// <b>Everything here is cut from one bought interface kit</b>
    /// (<c>Tools/make_hud_kit_art.py</c>) — rails, plates, troughs, caps, pills,
    /// squares, the hero's lander and the world they all stand in. It is the cartoon UI kit:
    /// saturated two-tone faces inside one heavy navy keyline, ribbons with tails, discs in a
    /// white ring. Two kits have worn these names before it and the names did not move either
    /// time, which is the method rather than luck.
    /// </para>
    /// <para>
    /// <b>What was wrong with the last one is worth keeping, because it is not about that
    /// kit.</b> Its plates were a cream rim around the ground colour — card interiors within a
    /// few points of the backdrop — so nothing on either screen read as an object standing on
    /// anything, and one rim of one width on every surface left no hierarchy either. Behind it
    /// all was a flat near-black wash with no world in it. **A screen made of outlines on a
    /// void is boring however well each outline is drawn.** What replaced it is opaque navy
    /// plates with a material of their own, over a bright illustrated world, with gold, green
    /// and orange as the only saturated things on top — so the contrast is a property of the
    /// art rather than something a rim is asked to buy.
    /// </para>
    ///
    /// <para>
    /// <b>The colour names did not move and that is the method.</b> <c>btn_green</c> has meant
    /// "do the thing" since this UI was written, <c>btn_red</c> "leave", <c>sq_dark</c> "not a
    /// control right now" — so those names were already roles, and ninety-odd call sites were
    /// already naming a role. Re-cutting what the names *point at* moved the whole app onto the
    /// kit in one commit with no sweep, nothing missed, and one <c>git checkout</c> to undo.
    /// The names that are new are the ones the kit brought pieces the game had never had:
    /// there was no rail, no cap and no lander to name.
    /// </para>
    ///
    /// <para>
    /// <b>This absorbed <c>ShopSkins</c>, which is what that class said would happen.</b> Its
    /// note read "rolling the kit out is then moving names from here to there, rather than
    /// rebuilding anything" — the storefront was where the look was judged worth changing
    /// first, and it is now every screen's look, so there is one table again.
    /// </para>
    ///
    /// <para>
    /// <b>Grey means "not a control right now", and nothing else.</b> The rule that bought the
    /// original class, and it survives the restyle unchanged: <c>Btn.Interactable</c> paints a
    /// dead control by draining it, so chrome built from greys reads as dead chrome. Anything a
    /// finger can usefully land on carries colour.
    /// </para>
    /// </summary>
    public static class Skins
    {
        // ------------------------------------------------------------- square controls
        /// <summary>
        /// The ordinary square control: back, forward, pause, an overlay's dismiss cross, an
        /// unselected tab. Blue because it is what this UI already spends on a secondary
        /// action, so chrome reads as live without competing with the affirmative.
        /// </summary>
        public const string Nav = "sq_blue";

        /// <summary>
        /// A square control that explains or configures rather than moving you: the "i", the
        /// gear, the rename pencil. Separate from <see cref="Nav"/> because these sit in the
        /// opposite top corner from a back key on four screens.
        ///
        /// <para>
        /// <b>Orange, and it stays orange under this kit for the reason it became orange
        /// under the last one.</b> The merge kit cuts its squares orange and every other
        /// colour on them is a rotation of that, so orange is what this family is actually
        /// drawn in — a mint square in the corner was the one control on the screen wearing a
        /// colour nothing else wore, which a render showed as two glowing chips above a dark
        /// room. <c>sq_aqua</c> is still cut and still named by two call sites; it is simply
        /// no longer what this role means.
        /// </para>
        /// </summary>
        public const string Aside = "sq_orange";

        /// <summary>
        /// Not a control. An off switch, a streak night that has not come round yet — the only
        /// things allowed to wear the disabled colour on purpose.
        /// </summary>
        public const string Resting = "sq_dark";

        // ---------------------------------------------------------------- pill controls
        /// <summary>
        /// The second pill in a panel that has two: the one that is not the affirmative.
        /// Named here only because the cancel key used to be <c>btn_gray</c> and read as
        /// broken; every other pill still names its colour where it is built.
        /// </summary>
        public const string Alternate = "btn_blue";

        /// <summary>
        /// A real-money price: the loudest thing on a card, and meant to be.
        ///
        /// <para>
        /// <b>One colour across every shelf, where it used to key on the shelf.</b> It had to
        /// before, because the card underneath was the shelf's colour and a yellow bar on a
        /// yellow frame is a button that disappears — the one control on a card that must
        /// never be hard to find. Every card is now the kit's navy (<see cref="Card"/>), so
        /// the price is orange on navy on all five shelves and the contrast is a property of
        /// the kit rather than a table somebody has to keep right.
        /// </para>
        /// </summary>
        public const string Buy = "btn_orange";

        /// <summary>A price paid in gems, which wears the gems' own colour.</summary>
        public const string Gem = "btn_violet";

        /// <summary>
        /// The key that does the thing a panel exists for, whether or not it costs — the turret
        /// preview's UPGRADE and its price, and the upgrade panel's own.
        ///
        /// <para>
        /// <b>A role of its own rather than <see cref="Buy"/> turned green</b>, because the price
        /// pill is on the real-money storefront as well (<c>ProductCard</c>) and repainting every
        /// product card is not what was asked for. It is the same argument <see cref="Battle"/>
        /// makes: a screen's own affirmative is a role, and a role gets a name.
        /// </para>
        /// <para>
        /// <b>The same green as <see cref="Settled"/> today, and the two are not the same
        /// thing.</b> That one says <em>this is already so</em> and this one says <em>do it</em>,
        /// and a player who taps one turret that is for sale and the next one that is equipped
        /// sees one colour meaning both — which is precisely the confusion <see cref="Settled"/>
        /// was split off to end. It stands because the owner asked for green on the keys they
        /// press and nothing yet asks these two to be told apart; the day something does, the one
        /// to move is <see cref="Settled"/>, because it is the dead key and <c>btn_gray</c> and
        /// <c>btn_dark</c> are both cut.
        /// </para>
        /// </summary>
        public const string Affirm = "btn_green";

        /// <summary>
        /// A control that states where things already stand rather than offering to change
        /// them: the loadout preview's EQUIPPED key, which pays nothing, moves nothing and only
        /// closes the panel it is on.
        ///
        /// <para>
        /// <b>Named because <c>btn_green</c> has meant "do the thing" everywhere else since this
        /// UI was written, and this is the one place it does not.</b> Left on <see cref="Buy"/>
        /// it was the price pill, so a turret already standing on the line shouted exactly as
        /// loudly as a nine-thousand-credit one — and the two states a player is actually
        /// choosing between, <em>equip this</em> and <em>this is equipped</em>, were drawn
        /// identically. Green is what this UI already spends on <em>you have this</em>
        /// (<c>Pal.Mint</c> on the grove shelf's held line); this raises it off a status line
        /// and onto the control saying the same thing.
        /// </para>
        /// </summary>
        public const string Settled = "btn_green";

        /// <summary>
        /// The hub's affirmative — the one control on that screen a player is meant to press.
        /// Its own mould rather than a colour name, because #FFC83D is not one of the eight
        /// the pill family is re-cut in and a tint on a green sprite cannot reach it.
        /// </summary>
        public const string Battle = "Hud/btn_gold";

        /// <summary>
        /// A key that is drawn because the thing behind it is worth wanting, and cannot be
        /// pressed yet — the Infinite lane's BATTLE before its keeper wall is met.
        ///
        /// <para>
        /// <b>A role rather than a colour, and a role rather than a tint.</b>
        /// <c>Image.color</c> is a multiply (invariant 37l), so dimming the gold key gives a
        /// brown one: a key that looks damaged rather than one that looks shut. Grey is what
        /// this kit already cuts for a control that is not live, and naming it here is what
        /// lets a re-cut move every shut key at once (invariant 44).
        /// </para>
        /// <para>
        /// <b>Distinct from <see cref="Resting"/>, which is not a control at all.</b> That one
        /// is an off switch or a streak night not yet come round; this is a button a player is
        /// meant to press — later. It still takes a tap, because a control that says nothing
        /// when it is pressed is indistinguishable from a broken one: what it answers with is
        /// the wall.
        /// </para>
        /// </summary>
        public const string Shut = "btn_gray";

        // -------------------------------------------------------- the hub's feature plates
        /// <summary>
        /// The fill every feature plate on the hub carries — sampled off <see cref="Card"/>,
        /// which is the keeper card at the top of the same screen. One navy for everything
        /// that is a plate, so the column reads as one object rather than as three.
        /// </summary>
        public static readonly Color Plate = new Color(0.031f, 0.122f, 0.271f, 1f);   // #081F45

        /// <summary>
        /// The three bright plates the hub's feature boxes are drawn on — the Battle key's own
        /// mould, sliced on both axes so it can be any size, in three hues.
        ///
        /// <para>
        /// <b>They replaced a drawn rounded rectangle with a traced rim, and the difference is
        /// not the colour.</b> A bought mould carries a two-tone face, a highlight along its
        /// top edge and a keyline that turns with the hue; a drawn rectangle carries none of
        /// those and no amount of tinting gives it one. That keyline is also why nothing traces
        /// a border round these any more — the sprite has one, and a second outline at a radius
        /// the sprite does not have is a halo a hair off the shape it follows.
        /// </para>
        /// </summary>
        public const string PlateBlue = "Hud/plate_blue";
        public const string PlateOrange = "Hud/plate_orange";
        public const string PlateViolet = "Hud/plate_violet";

        /// <summary>
        /// <see cref="PlateBlue"/> taken down in value: the same mould, the same blue, dark
        /// enough to be a row in a list rather than a plate on a hub.
        ///
        /// <para>
        /// <b>It is a card, not a fourth hue.</b> The tasks page is a list of reward cards and
        /// <see cref="Card"/> draws each one as a container and nothing else — no lit top edge,
        /// no two-tone face, no keyline that turns with the colour, which are the three things
        /// that made the profile's sections read as objects. Drawn in <see cref="PlateBlue"/>
        /// itself, six of them fight the wall they stand on; halved, the face lands between
        /// <see cref="Plate"/> and the bright mould and the list reads as a list.
        /// </para>
        /// <para>
        /// Cut from <see cref="PlateBlue"/>'s own hue and saturation with nothing but a
        /// <c>dim</c>, so a re-cut that moves the profile's boxes moves these with them and
        /// the two can never drift into two blues.
        /// </para>
        /// </summary>
        public const string PlateNavy = "Hud/plate_navy";

        /// <summary>
        /// The same mould in <see cref="Battle"/>'s own colour, for a box the pill cannot be
        /// drawn in.
        ///
        /// <para>
        /// <b>It is not a fourth hue, it is the affirmative's hue on a plate.</b> A glade's
        /// name banner is 340x62 and the owner asked for it to wear what the hub's BATTLE key
        /// wears; <c>btn_gold</c> is sliced across its width only, so at 62 tall it compresses
        /// its whole 166-tall face by 2.7 and its keyline comes out thin on the top and bottom
        /// edges and full width on the sides. Sliced both ways the corner stays 15 units at any
        /// box, which is the only reason this exists (invariant 44a).
        /// </para>
        /// <para>
        /// <b>So the two have to move together.</b> `make_hud_kit_art.py` cuts them from one
        /// artboard at one hue and one saturation, and a re-cut that moves the key and leaves
        /// this behind is a banner that no longer matches the button it was asked to match.
        /// </para>
        /// </summary>
        public const string PlateGold = "Hud/plate_gold";

        /// <summary>The frame drawn round a plate.</summary>
        public static readonly Color PlateRim = new Color(0.016f, 0.031f, 0.063f, 1f);

        /// <summary>The colour the affirmative wears, and the live nav tab's frame.</summary>
        public static readonly Color PlateEdge = new Color(1f, 0.784f, 0.239f, 1f);   // #FFC83D

        /// <summary>
        /// The orange the settings and info keys are cut in (<see cref="Aside"/>), read off
        /// the sprite so a label written in it cannot drift from the button it is matching.
        /// </summary>
        public static readonly Color AsideTint = new Color(0.973f, 0.478f, 0.137f, 1f); // #F87A23

        /// <summary>
        /// What every screen stands on. One flat blue, chosen dark enough that
        /// <see cref="Plate"/> reads as a thing standing on it — see <c>Scenery.Room</c>.
        /// </summary>
        public static readonly Color Sky = new Color(0.055f, 0.204f, 0.392f, 1f);      // #0E3464

        // ------------------------------------------------------------------- the rails
        /// <summary>
        /// The bar across the top of a screen, and the one across the foot of it — one long
        /// navy trough cut from the kit, the foot one flipped. Flipped rather than cut twice,
        /// because the trough is lit along its top edge: a foot rail that is not flipped is lit
        /// on the edge facing away from the screen.
        ///
        /// <para>
        /// <b>A rail is the quietest thing on a screen</b>, which is why it is the kit's one
        /// genuinely dark piece taken further down rather than any of its coloured bars. It
        /// frames content, so anything it does beyond marking the edge is competing with what
        /// it frames — the last kit put a saturated cyan slab across the top and another across
        /// the foot, and they were the loudest things on either screen.
        /// </para>
        /// </summary>
        public const string Rail = "Hud/rail_top";
        public const string RailFoot = "Hud/rail_bottom";

        // ------------------------------------------------------------------ the plates
        /// <summary>
        /// The modal plate: the kit's bar family painted navy with a shallow well sunk into
        /// it. Anything standing in front of the screen rather than on it.
        ///
        /// <para>
        /// A panel stands <em>in front of</em> the screen where a <see cref="Card"/> stands
        /// <em>on</em> it, so it keeps more of its face and is sunk less far. Its border is
        /// measured rather than typed, which is new: this source is an honest rounded rectangle
        /// with no ornament painted on it, where both kits before it had a tab or a clip in the
        /// middle of the very edge a nine-slice stretches.
        /// </para>
        /// </summary>
        public const string Panel = "Hud/panel";

        /// <summary>
        /// A card — the same plate sunk further, which is what a product stands in: a product
        /// in the shop, a feature box on the hub.
        ///
        /// <para>
        /// Cut at native size, which is invariant 44a in the easy direction for once. This
        /// kit's corner is 16 pixels on a 400-pixel source, so it draws as 16 units on a
        /// 474-unit card and nothing has to be scaled down to keep a middle to stretch — where
        /// the last kit's 107-pixel frame would have eaten 211 units of that card.
        /// </para>
        /// </summary>
        public const string Card = "Hud/card";

        /// <summary>
        /// A hole rather than a thing standing on the screen: an avatar's seat, an empty shop
        /// cell, a chest's socket. The kit's own navy chip, which is the one piece in it that
        /// is already drawn inset — every kit says "inset" by being darker than what stands on
        /// it, and this is the only one that shipped a piece already that dark.
        /// </summary>
        public const string Slot = "Hud/slot";

        // ------------------------------------------------------- troughs and title bars
        /// <summary>
        /// What a readout sits in: the kit's own long navy trough, cut as it ships.
        ///
        /// <para>
        /// <b>This is the first kit that did not have to be sunk to be readable.</b> Two in a
        /// row drew their readouts <em>cream</em>, their own screens being light ones, so the
        /// sprite they shipped was cream under <see cref="Pal.Cream"/> with an outline doing
        /// all the work — the yellow-bar-on-a-yellow-card fault, arriving through the art. This
        /// pack writes light numbers on dark troughs exactly as this game does, so the piece
        /// arrives right and `welled` is not called on it at all.
        /// </para>
        /// </summary>
        public const string Trough = "Hud/trough";

        /// <summary>
        /// What goes <em>in</em> a trough: a rank bar, a chest track, an event's progress.
        ///
        /// <para>
        /// Cut near-white with the kit's two-tone shading kept, so the call site tints it —
        /// <c>Image.color</c> is a multiply, so one white fill takes every colour this UI
        /// spends and keeps the lighter top half that makes it read as a filled tube rather
        /// than a block of colour. One sprite instead of a colour per bar, and a bar that
        /// cannot come to disagree with the trough it sits in.
        /// </para>
        /// </summary>
        public const string Fill = "Hud/fill";

        /// <summary>
        /// A <em>word</em> the game owns rather than a number: the kit's ribbon, with tails
        /// and a heavy keyline, which is the thing every version of this UI has been missing.
        /// Gold is what is written on it.
        ///
        /// <para>
        /// <b>It is drawn at its own aspect and never nine-sliced.</b> The tails are the
        /// silhouette and the top edge is a curve, so a measured border reads the tails and a
        /// stretched middle flattens the curve away from wherever the slice sampled it.
        /// </para>
        /// </summary>
        public const string Title = "Hud/title";

        /// <summary>
        /// How far above <see cref="Title"/>'s own middle a caption written on it belongs, as a
        /// fraction of the height it is drawn at.
        ///
        /// <para>
        /// Measured rather than eyeballed: the kit's ribbon hangs two tails below the flag, so
        /// the sprite's centre is not the writable band's centre. Over the middle third of the
        /// columns the opaque run is rows 0..165 of 208, whose middle sits 10.3% of the height
        /// above the sprite's. A word set at the sprite's centre reads as one that has slipped
        /// down, which is what it was doing on the storefront's title and on every bonus mark
        /// in the grid.
        /// </para>
        /// </summary>
        public const float RibbonLift = 0.103f;

        // -------------------------------------------------------------------- the caps
        /// <summary>
        /// A nav tab's face, lit and unlit — and a shop tab's, which is the same control
        /// asking the same question one level down.
        ///
        /// <para>
        /// <b>The glyph is never the kit's.</b> Its buttons carry coins, bins and speakers,
        /// which name nothing in this game, so what ships is the kit's blank face wearing the
        /// icons this UI already owns — the lit and unlit pair off one mould, which is exactly
        /// what a tab needs. That is what stops a restyle quietly renaming five destinations.
        /// </para>
        /// </summary>
        public const string CapOn = "Hud/cap_on";
        public const string CapOff = "Hud/cap_off";


        // ------------------------------------------------------------------- the marks
        /// <summary>
        /// The "+" on the end of a readout, which is what makes it read as a control. Neither
        /// pack has a plus anywhere, so this is the kit's own square with one drawn on it —
        /// still one image rather than a square plus a glyph, and still the kit's own keyline
        /// beside the trough's rim.
        /// </summary>
        public const string Add = "Hud/add";

        /// <summary>
        /// The starburst a card wears when it is the one worth pointing at. Drawn, and white,
        /// so a call site can tint it: the kit's own star is gold at fifty pixels, and gold
        /// multiplied by <see cref="Pal.Rose"/> is a muddy orange rather than a rose seal.
        /// </summary>
        public const string Badge = "Hud/burst";

        // The kit also draws a notification dot, a tick, a padlock, a pair of arrows and a
        // flat plate, and none of them is cut. Nothing here draws one, and an addressed sprite
        // nothing asks for is still built into the bundle and still decoded at every launch —
        // which is the judgement this file's own `jelly_*` entry was deleted over. Adding one
        // back is a line here and a line in `make_hud_kit_art.KIT`.

        // ------------------------------------------------------------------- the stage
        /// <summary>
        /// The hub's centrepiece: a lander to stand a companion on, the beam it stands in,
        /// and the room both of them are in.
        ///
        /// <para>
        /// <b>The lander is the one piece taken from the tower-defence pack</b>, and the only
        /// thing any of the three has that is a place to <em>stand</em> — the cartoon kit is a
        /// sheet of chrome and has no ground in it at all. It is that pack's level-select
        /// node, painted to the kit's gold <em>and then sunk</em>, so it is a ring round a
        /// warmer middle rather than a flat disc: left flat at that size a gold disc under a
        /// companion is a <b>coin</b>, which the render said of this kit exactly as loudly as
        /// it said of the last. The beam is drawn, because no pack here has one.
        /// </para>
        /// </summary>
        public const string Lander = "Hud/lander";
        public const string Beam = "Hud/beam";
        public const string Room = "Hud/room";

        // ------------------------------------------------------------------ the ground
        /// <summary>
        /// What a screen paints behind the room, and what one that does not draw the room
        /// paints instead. Sampled from the kit's own background at its darkest, so a screen
        /// that stops short of the picture still stands on the same colour.
        /// </summary>
        /// <para>
        /// <b>It is the kit's keyline navy now, and it does much less work than it used
        /// to.</b> Under the last two kits this was most of what a player saw: the room was a
        /// translucent wash and every card interior was within a few points of it, so the
        /// ground colour <em>was</em> the screen. The world here is an opaque picture and
        /// every plate is opaque over it, so this is what shows for one frame on a cold boot
        /// and past the picture's edge on a tall device — and it is the keyline's own navy so
        /// that a screen which stops short of the world still stands on the kit's colour.
        /// </para>
        /// </summary>
        public static readonly Color Ground = new Color(.024f, .094f, .220f, 1f);

        /// <summary>The same ground one step up, for a band drawn over it.</summary>
        public static readonly Color Band = new Color(.055f, .148f, .318f, 1f);

        /// <summary>
        /// What a button that cannot be pressed is tinted by — owned, included, pending,
        /// mid-purchase.
        ///
        /// <para>
        /// A tint on <em>the same sprite the live button uses</em> rather than a second
        /// colour, because <c>Image.color</c> is a multiply: a grey over the face it would
        /// have worn reads as that control turned down, where a different sprite reads as a
        /// different control. Invariant 37l's distinction, met on a button instead of a
        /// turret.
        /// </para>
        /// </summary>
        public static readonly Color Muted = new Color(.62f, .66f, .72f, 1f);

        // ------------------------------------------------------------------- the shelf
        /// <summary>
        /// The colour a shelf is told apart by.
        ///
        /// <para>
        /// <b>It used to be the whole card and it is now the light behind the picture.</b>
        /// Three coloured frames across five shelves was how a shelf said which one it was,
        /// and it cost the storefront its material: five saturated blocks of colour side by
        /// side read as five different games rather than as one shop. The kit gives every card
        /// one teal plate, which is what a vending machine looks like — so the shelf is said by
        /// the *lit tab*, which the old dark chips could not say at all, and by a coloured
        /// light under the goods, which is where a player is already looking.
        /// </para>
        /// </summary>
        public static Color Accent(StoreShelf shelf)
        {
            switch (shelf)
            {
                case StoreShelf.Coins: return Pal.Gold;
                case StoreShelf.Bundles: return Pal.Mint;
                case StoreShelf.Supplies: return Pal.Rose;
                case StoreShelf.Utilities: return Pal.Sun;

                // The Bloom Pass, which is the event shelf and does not draw a tab today.
                // Named anyway, and that is the point of `SkinsTests.NoTwoShelvesShareAnAccent`:
                // it fell into the `default` with gems, so the one shelf nobody was looking at
                // was the one that would have shipped identical cards the day it was turned on.
                // A switch over an enum whose default is a real colour hides exactly this.
                case StoreShelf.EventPass: return Pal.Aqua;

                default: return Pal.Bloom;              // gems
            }
        }
    }
}
