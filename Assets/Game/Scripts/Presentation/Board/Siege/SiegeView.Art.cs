using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Every sprite and reel this board draws, and the one place a key is written down.
    ///
    /// <para>
    /// <b>Every name is a literal</b> — <c>Tools/verify/artnames.py</c> reads the literal at a
    /// lookup's call site, so a key assembled from an index is a name nothing checks, and this
    /// mode has already paid for that once (a white rectangle two cells wide, found by a player).
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ art
        /// <summary>
        /// One of this mode's sprites, addressed through <see cref="AssetManifest"/>.
        ///
        /// <b>Every art lookup here goes through this and none of them builds a path</b>
        /// (invariant 7). The key is the first argument because <c>Tools/verify/artnames.py</c>
        /// reads the literals in a lookup's first argument, and a widget name in front of it would
        /// have the gate checking the wrong string and saying so confidently.
        /// </summary>
        static Sprite Piece(string key) => AssetLibrary.Sprite(AssetManifest.SiegeArt(key));

        /// <summary>
        /// Which rung of its chapter this level is, which is the only thing that decides its
        /// ground.
        ///
        /// Set by <c>SiegeScreen</c> before the board is built. Nought when nothing has said
        /// otherwise, so a siege opened outside a chapter draws the first rung's floor rather than
        /// a white rectangle (invariant 7b).
        /// </summary>
        public int Rung { get; set; }

        /// <summary>
        /// The address of the ground a rung is fought over — the same answer
        /// <see cref="SiegeMode.Ground"/> gives, said in the assembly that draws it.
        ///
        /// <para>
        /// <b>Ten literal cases</b>, for the reason that method gives: <c>artnames.py</c> reads the
        /// literal at a lookup's call site, so a key built from <see cref="Rung"/> would be ten
        /// names nothing checks.
        /// </para>
        /// <para>
        /// <b>It answers an address rather than a sprite so that it can be compared.</b> The two
        /// switches have to agree or a chapter loads one floor while the board asks for another,
        /// which draws a white rectangle over the whole hill on one rung with every gate green
        /// (invariant 7b) — <c>SiegeGroundTests</c> is what stops that, and it can only make the
        /// comparison if the answer is a string rather than something needing a loaded scope.
        /// </para>
        /// </summary>
        public static string GroundAddress(int place)
        {
            switch (((place % SiegeMode.Grounds) + SiegeMode.Grounds) % SiegeMode.Grounds)
            {
                case 1:  return AssetManifest.SiegeArt("hill2");
                case 2:  return AssetManifest.SiegeArt("hill3");
                case 3:  return AssetManifest.SiegeArt("hill4");
                case 4:  return AssetManifest.SiegeArt("hill5");
                case 5:  return AssetManifest.SiegeArt("hill6");
                case 6:  return AssetManifest.SiegeArt("hill7");
                case 7:  return AssetManifest.SiegeArt("hill8");
                case 8:  return AssetManifest.SiegeArt("hill9");
                case 9:  return AssetManifest.SiegeArt("hill10");
                default: return AssetManifest.SiegeArt("hill1");
            }
        }

        static Sprite Hill(int place) => AssetLibrary.Sprite(GroundAddress(place));

        static Sprite[] Reel(string key) => AssetLibrary.Frames(AssetManifest.SiegeArt(key));

        static Sprite[] Blast(string key) => AssetLibrary.Frames(AssetManifest.SiegeFx(key));

        /// <summary>
        /// A flipbook widget, or <b>null</b> when its frames are not there.
        ///
        /// An <c>Image</c> with a null sprite is a white rectangle rather than a blank (invariant
        /// 7b), so the widget is not built until the frames are in hand — missing art then costs
        /// the thing it draws and never costs a white square over the board.
        /// </summary>
        static Image Book(Sprite[] frames, string name, RectTransform parent, Vector2 size,
                          float fps, bool loop = true)
        {
            if (frames == null || frames.Length == 0) return null;

            var img = UIKit.Img(name, parent, frames[0], Color.white, size);
            img.raycastTarget = false;
            img.preserveAspect = true;
            Flipbook.Attach(img, frames, fps, loop);
            return img;
        }

        static readonly Color[] Tints =
        {
            Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Sun,
        };

        /// <summary>
        /// What colour a ward burns, as the board paints it.
        ///
        /// <b>Public because <c>WardFiringStage</c> asks it</b>, and that widget draws a turret
        /// firing outside a board — a second table of these four would be a second answer to a
        /// question invariant 37f settles by there being exactly one.
        /// </summary>
        public static Color TintOf(int colour)
            => colour >= 0 && colour < Tints.Length ? Tints[colour] : Pal.Cream;

        /// <summary>
        /// A gem's picture.
        ///
        /// <b>The name is written out at the point it is looked up rather than returned as a
        /// string</b>, which is invariant 6's rule for loc keys read across to art. A helper that
        /// answers <c>"gem_r"</c> and is then passed to <see cref="Piece"/> puts the literal one
        /// call away from the lookup, and <c>Tools/verify/artnames.py</c> counts that as a name
        /// nothing checks. Written this way every one of them is held to what is on disk.
        /// </summary>
        static Sprite GemArt(int colour)
        {
            switch (colour)
            {
                case 0: return Piece("gem_r");
                case 1: return Piece("gem_g");
                case 2: return Piece("gem_b");
                case 3: return Piece("gem_y");

                // A thief's sack: not a colour, and deliberately the dullest thing on the field.
                case SackColour: return Piece("sack");

                // A cog, which is what `SiegeBoard.ColourAt` answers -1 for. It is the one cell of
                // this field that is not a colour, so it is the one that falls off the end of a
                // table keyed on colour - and that is the right shape rather than a gap in one
                // (`SiegeLayout.Cells`).
                default: return Piece("gem_cog");
            }
        }

        /// <summary>What a cell holding a cog answers when it is asked its colour.</summary>
        const int CogColour = -1;

        /// <summary>
        /// What a cell holding a thief's sack answers.
        ///
        /// <b>Its own number rather than sharing the cog's -1</b>, which is what
        /// <c>SiegeBoard.ColourAt</c> answers for both: a cog and a sack are the two cells of this
        /// field that are not colours, and they are opposite things — one is a prize the player is
        /// reaching for and the other is dead weight. A view that could not tell them apart would
        /// draw the wrong one, which is a mechanic reading as its opposite.
        /// </summary>
        const int SackColour = -2;

        /// <summary>
        /// A ward's body. Four models rather than four paint jobs.
        ///
        /// <para>
        /// <b>No colour is baked into the art</b> — the sprite is a bare turret and the colour it
        /// burns is <see cref="Coat"/>, applied here to the same <c>Pal</c> entry the gems and the
        /// raiders take theirs from. So the four cannot drift apart, and a ward, its bullets, its
        /// muzzle flash and the gems that feed it are one colour by construction rather than by
        /// four people agreeing.
        /// </para>
        /// <para>
        /// Four <em>models</em> because a tint alone is a difference only some people can see
        /// (CRAFT.md's rule about the board's vocabulary), and a line of four identical turrets
        /// told apart only by hue is exactly that.
        /// </para>
        /// </summary>
        /// <summary>
        /// The turret standing on this colour, as the player chose it.
        ///
        /// <para>
        /// <b>Built from the model's id rather than written out as a literal, and this is the one
        /// place in this mode that is allowed to be.</b> <c>Tools/verify/artnames.py</c> reads
        /// literals off a call site, so a built name is a name it cannot check - and twenty models
        /// times four colours is eighty literals nobody would keep in step with a roster that is
        /// <em>content</em>. What replaces the literal is a stronger gate rather than a weaker one:
        /// <c>ContentValidation</c> and <c>content.py</c> both walk the roster and error on a model
        /// whose pictures are not on disk, which catches a missing file and a misspelled id at
        /// once where a literal only ever catches the second.
        /// </para>
        /// <para>
        /// <b>Never null on a run</b>: <c>WardLine.Resolve</c> answers four turrets whatever the
        /// save holds, and the starter's four colours are in the mode's own cast so they are
        /// resident before any scope arrives (invariant 7b - an Image with no sprite is a white
        /// rectangle, not a blank).
        /// </para>
        /// </summary>
        Sprite WardArt(SiegeWard ward)
            => Piece(ward.Model.ArtFor(WardLine.Colours[Hue(ward.Colour)]));

        /// <summary>That turret's recoil, as frames.</summary>
        Sprite[] FireArt(SiegeWard ward)
            => Reel(ward.Model.FireFor(WardLine.Colours[Hue(ward.Colour)]));

        /// <summary>
        /// What colour a ward's badge is at this rank.
        ///
        /// <para>
        /// <b>A ladder of metals, so a rank is read at a glance rather than read as a number.</b>
        /// The five-tier turret ladder used to carry the rank in the <em>silhouette</em>
        /// (invariant 37w) and twenty player-chosen models cannot — the silhouette belongs to the
        /// choice now. A plinth under the turret was tried and thrown away: invariant 37y already
        /// records that a turret's foot is behind the field's plate on every screen this mode is
        /// drawn at, so what went there was invisible on the board and only a render said so.
        /// </para>
        /// <para>
        /// <b>Steel, bronze, silver, gold, white-hot</b> — a ladder anybody has seen before, and
        /// climbing in <em>value</em> as well as in hue so it survives a player who cannot
        /// separate two of the colours. The number stays on top of it: the tint says "this one is
        /// better" and the digit says by how much.
        /// </para>
        /// </summary>
        static Color RankTint(int rank)
        {
            switch (rank < 0 ? 0 : rank > SiegeTuning.MaxRank ? SiegeTuning.MaxRank : rank)
            {
                case 1: return new Color(.85f, .55f, .32f);      // bronze
                case 2: return new Color(.86f, .89f, .94f);      // silver
                case 3: return new Color(1f, .80f, .30f);        // gold
                case 4: return new Color(1f, .98f, .90f);        // white-hot
                default: return new Color(.62f, .68f, .74f);     // steel
            }
        }

        static Sprite RetiredWardArt(int colour, int rank)
        {
            switch (Tier(rank) * 4 + Hue(colour))
            {
                case  0: return Piece("ward1_r");
                case  1: return Piece("ward1_g");
                case  2: return Piece("ward1_b");
                case  3: return Piece("ward1_y");
                case  4: return Piece("ward2_r");
                case  5: return Piece("ward2_g");
                case  6: return Piece("ward2_b");
                case  7: return Piece("ward2_y");
                case  8: return Piece("ward3_r");
                case  9: return Piece("ward3_g");
                case 10: return Piece("ward3_b");
                case 11: return Piece("ward3_y");
                case 12: return Piece("ward4_r");
                case 13: return Piece("ward4_g");
                case 14: return Piece("ward4_b");
                case 15: return Piece("ward4_y");
                case 16: return Piece("ward5_r");
                case 17: return Piece("ward5_g");
                case 18: return Piece("ward5_b");
                default: return Piece("ward5_y");
            }
        }

        static Sprite[] RetiredFireArt(int colour, int rank)
        {
            switch (Tier(rank) * 4 + Hue(colour))
            {
                case  0: return Reel("fire1_r");
                case  1: return Reel("fire1_g");
                case  2: return Reel("fire1_b");
                case  3: return Reel("fire1_y");
                case  4: return Reel("fire2_r");
                case  5: return Reel("fire2_g");
                case  6: return Reel("fire2_b");
                case  7: return Reel("fire2_y");
                case  8: return Reel("fire3_r");
                case  9: return Reel("fire3_g");
                case 10: return Reel("fire3_b");
                case 11: return Reel("fire3_y");
                case 12: return Reel("fire4_r");
                case 13: return Reel("fire4_g");
                case 14: return Reel("fire4_b");
                case 15: return Reel("fire4_y");
                case 16: return Reel("fire5_r");
                case 17: return Reel("fire5_g");
                case 18: return Reel("fire5_b");
                default: return Reel("fire5_y");
            }
        }

        /// <summary>Which of the five turrets a rank is drawn as, clamped.</summary>
        static int Tier(int rank)
            => rank < 0 ? 0 : rank > SiegeTuning.MaxRank ? SiegeTuning.MaxRank : rank;

        /// <summary>Which of the four hues a colour is drawn in, clamped.</summary>
        static int Hue(int colour) => colour < 0 || colour > 3 ? 3 : colour;

        /// <summary>
        /// What a ward <em>fires</em>, as frames.
        ///
        /// <para>
        /// <b>The turret decides the shape and the ward decides the colour</b>, which is the
        /// division that let nineteen bought turrets each get an effect of their own. A model with
        /// an ability throws a reel named after its own id — a crescent that cuts, an arrow that
        /// runs a lane, a wisp that drains — baked in each of the four ward colours; a model with
        /// no ability throws the four elemental bolts the starter has always thrown
        /// (<c>WardModel.ShotFor</c>). Nothing here is tinted: invariant 37l's rule is that a
        /// multiply can only darken, so what a reel is worn in has to be what was baked into it.
        /// </para>
        /// <para>
        /// <b>Keyed by the model and never by ward index</b>, which was the old rule's point and
        /// still is: which colour a ward burns is content, so a table by post would put a fireball
        /// on whichever ward happened to stand first and disagree with the turret beside it.
        /// </para>
        /// <para>
        /// <b>Built rather than written out as a literal</b>, which <c>Tools/verify/artnames.py</c>
        /// cannot check — the same bargain <c>WardModel.ArtFor</c> already strikes and for the same
        /// reason (invariant 42). What replaces the literal is stronger: the roster is content, so
        /// <c>ContentValidation</c> and <c>content.py</c> both walk it and error on a model whose
        /// reels are not on disk, which catches a missing bake and a misspelled id at once.
        /// </para>
        /// </summary>
        static Sprite[] ShotArt(Wards.WardModel model, int colour)
            => Blast(model.ShotFor(Wards.WardLine.Colours[Hue(colour)]));

        /// <summary>
        /// How big this turret's bolt is drawn, as a multiple of the ordinary one.
        ///
        /// <para>
        /// <b>The one thing about a turret's projectile that is a decision rather than a bake.</b>
        /// Every reel is framed round its own content, so how much of a frame an effect fills says
        /// nothing about how big it is on the hill - that is this number, and without it a "big"
        /// effect is only a differently-shaped one. It was asked for by name: the top of the credit
        /// ladder is a heavy turret and its sun should read as the largest thing either line throws.
        /// </para>
        /// <para>
        /// <b>A short table rather than a field on the model</b>, because it is not content: a
        /// picture is not content and adding a turret is a build (invariant 39c), and the size a
        /// particular effect wants is a fact about that effect. Everything not named here is drawn
        /// the ordinary size, which is nearly all of them - a line where every bolt is special is a
        /// line where none of them is.
        /// </para>
        /// </summary>
        public static float BoltScale(Wards.WardModel model)
        {
            switch (model.Id)
            {
                case "prism": return 1.55f;     // a sun, and the biggest thing on the board
                case "breaker": return 1.24f;   // a wrecking slug, and it has to have weight
                case "spectrum": return 1.16f;
                case "harpoon": return 1.10f;
                default: return 1f;
            }
        }

        /// <summary>The flash a ward throws as it lets one go. See <see cref="ShotArt"/>.</summary>
        static Sprite[] MuzzleArt(Wards.WardModel model, int colour)
            => Blast(model.MuzzleFor(Wards.WardLine.Colours[Hue(colour)]));

        /// <summary>What a bolt does when it arrives. See <see cref="ShotArt"/>.</summary>
        static Sprite[] HitArt(Wards.WardModel model, int colour)
            => Blast(model.HitFor(Wards.WardLine.Colours[Hue(colour)]));

        /// <summary>
        /// How far a thing is pulled toward the colour it wears.
        ///
        /// <para>
        /// One number for the raiders, the wards, their bullets and their muzzle flashes, because
        /// all four are answering the same question — <em>which of the board's colours is this</em>
        /// — and four numbers would be four answers.
        /// </para>
        /// <para>
        /// <b>It is a multiply, so it can only ever darken</b> — which is fine for the cast,
        /// whose art is bright and whose colour only has to be legible, and was not fine for the
        /// wards. Pulled the whole way to a saturated <c>Pal</c> entry a turret came back dark
        /// ("too dim"); lifted toward white first it came back pastel. The wards therefore carry a
        /// real hue baked into the sprite and are drawn at full brightness (see
        /// <c>Tools/make_siege_art.py</c>), and nothing here tints them.
        /// </para>
        /// </summary>
        static Color Coat(int colour) => Color.Lerp(Color.white, TintOf(colour), .62f);

        /// <summary>
        /// How wide a reel drawn <paramref name="tall"/> high comes out, read off its own art.
        ///
        /// <b>Read rather than assumed, and it is the only thing here that is.</b> Invariant 16i
        /// says a piece's drawn <em>size</em> is authored and never measured off the loaded sprite,
        /// so a tile laid out before its art arrives is not laid out around a placeholder — and
        /// that is exactly what <paramref name="tall"/> is. What cannot be authored is the shape a
        /// cutting tool happened to give an animation's bounding box, which is a fact about the
        /// picture. Answers a square when the frames are not in hand, so a missing reel costs the
        /// picture and never the layout.
        /// </summary>
        static float Frame(Sprite[] frames, float tall)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return tall;

            var rect = frames[0].rect;
            return rect.height > 0f ? tall * rect.width / rect.height : tall;
        }

        /// <summary>Which of the cast a raider is drawn as, as frames. See <see cref="GemArt"/>.</summary>
        Sprite[] Skin(SiegeRaider raider)
        {
            // **Four bosses, four bodies, four packs.** Every name here is written out at the
            // lookup rather than built from the kind, which is what keeps
            // `Tools/verify/artnames.py` able to hold all twelve reels to what is on disk — a
            // table of strings would be twelve names nothing checks (invariant 7's rule read into
            // the gate that enforces it).
            switch (raider.Kind)
            {
                case SiegeKind.Overlord: return Reel("over");
                case SiegeKind.Warbringer: return Reel("bringer");
                case SiegeKind.Boss: return Reel("boss");
                case SiegeKind.Blightcaller: return Reel("blight");
            }

            // **A body per colour, which is what removing the tint bought.** It used to be
            // three creeper models handed out on `Colour % 3`, so two of the four colours shared a
            // body and the only thing separating them was a wash that has now gone. Four bodies
            // means the silhouette says the colour on its own — which is the half of the rule a
            // player who cannot separate red from green depends on — and it costs nothing but
            // folder names, out of eighty-three characters these packs hold.
            // **The two that work on the field rather than on the line, and they are one set
            // whatever the chapter.** A creeper is scenery that can be re-cast per chapter; a
            // weaver and a thief are *rules*, and a player who has learned that the crawling
            // beetle locks cells must not have to learn it again in the next chapter because the
            // beetle has become something else.
            switch (raider.Kind)
            {
                case SiegeKind.Weaver:
                    switch (raider.Colour)
                    {
                        case 0: return Reel("weaver_r");
                        case 1: return Reel("weaver_g");
                        case 2: return Reel("weaver_b");
                        default: return Reel("weaver_y");
                    }

                case SiegeKind.Thief:
                    switch (raider.Colour)
                    {
                        case 0: return Reel("thief_r");
                        case 1: return Reel("thief_g");
                        case 2: return Reel("thief_b");
                        default: return Reel("thief_y");
                    }
            }

            return CastSet == 1 ? SecondSet(raider) : FirstSet(raider);
        }

        /// <summary>
        /// Which cast a chapter draws, by its <b>ordinal inside its own mode</b>.
        ///
        /// <b>Arithmetic rather than a choice</b>, which is invariant 7c's rule and the ground's
        /// shape exactly: two sets serve every siege chapter that ever ships, a third costs one
        /// table row and no code, and no chapter can be published drawing a cast nobody decided
        /// on. Set by <c>SiegeScreen</c> before the board is built, so a run without a chapter
        /// draws the first set rather than nothing.
        /// </summary>
        public int CastSet;

        /// <summary>How many casts this mode ships. See <see cref="CastSet"/>.</summary>
        public const int CastSets = 2;

        static Sprite[] FirstSet(SiegeRaider raider)
        {
            switch (raider.Kind)
            {
                case SiegeKind.Bulwark:
                    switch (raider.Colour)
                    {
                        case 0: return Reel("bulwark_r");
                        case 1: return Reel("bulwark_g");
                        case 2: return Reel("bulwark_b");
                        default: return Reel("bulwark_y");
                    }

                case SiegeKind.Brute:
                    switch (raider.Colour)
                    {
                        case 0: return Reel("brute_r");
                        case 1: return Reel("brute_g");
                        case 2: return Reel("brute_b");
                        default: return Reel("brute_y");
                    }
            }

            switch (raider.Colour)
            {
                case 0: return Reel("mon_r");
                case 1: return Reel("mon_g");
                case 2: return Reel("mon_b");
                default: return Reel("mon_y");
            }
        }

        static Sprite[] SecondSet(SiegeRaider raider)
        {
            switch (raider.Kind)
            {
                case SiegeKind.Bulwark:
                    switch (raider.Colour)
                    {
                        case 0: return Reel("bulwarkB_r");
                        case 1: return Reel("bulwarkB_g");
                        case 2: return Reel("bulwarkB_b");
                        default: return Reel("bulwarkB_y");
                    }

                case SiegeKind.Brute:
                    switch (raider.Colour)
                    {
                        case 0: return Reel("bruteB_r");
                        case 1: return Reel("bruteB_g");
                        case 2: return Reel("bruteB_b");
                        default: return Reel("bruteB_y");
                    }
            }

            switch (raider.Colour)
            {
                case 0: return Reel("monB_r");
                case 1: return Reel("monB_g");
                case 2: return Reel("monB_b");
                default: return Reel("monB_y");
            }
        }

        /// <summary>
        /// How tall a raider is drawn, in cells.
        ///
        /// <b>The number itself lives in <see cref="SiegeTuning.TallOf"/>, in the rules.</b> A
        /// firepot has to hit the body a player can see, so how big that body is stopped being a
        /// fact only the view knew — see the rule's own remarks, and invariant 33g for why a
        /// drawn thing and a played thing may not be two facts that agree.
        /// </summary>
        static float TallOf(SiegeRaider raider) => SiegeTuning.TallOf(raider.Kind);

        /// <summary>
        /// Where a raider carries its health bar and its colour, as a fraction of its own height.
        ///
        /// <b>Over the head for a raider, and nowhere near a warlord</b> — see
        /// <see cref="Crown"/> for where a warlord's goes and why it had to leave its body.
        /// </summary>
        static float ReadoutAt(SiegeRaider raider) => .58f;

        /// <summary>
        /// A warlord's health bar, pinned across the top of the board.
        ///
        /// <para>
        /// <b>Off its body, and that is what let the warlord be big.</b> A carried bar has to sit
        /// somewhere, and on a hill four cells deep there is nowhere for one that belongs to
        /// something three cells tall: three renders put it outside the board's top edge (two
        /// widgets that had come loose), then on the ward line's own health bars (two readouts
        /// overlapping, which is two readouts nobody can read), and each time the answer was to
        /// make the warlord smaller — until it was barely taller than the turrets it was supposed
        /// to be looming over.
        /// </para>
        /// <para>
        /// A bar across the top is the genre's own answer and it is better for a second reason:
        /// this is the one health bar in the mode a player watches for half a minute rather than
        /// glances at, and it is the only one whose *place* can be learned. Nothing else on this
        /// board lives up there.
        /// </para>
        /// </summary>
        RectTransform Crown(int slot)
        {
            var node = UIKit.Node("Warlord", _fx);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(Span.x, Cell * .5f);

            // **A rung down per boss already up there**, which an endless lane's pair waves need
            // and no authored rung ever did: a crown is anchored rather than carried, so a second
            // one drew across the first and the two bars read as one bar at a strength nobody
            // could account for. Down rather than up, because the top of the hill is where the
            // first one has always been and that is the place a player learns.
            node.anchoredPosition = new Vector2(0f, _hillTop + Cell * .12f - slot * Cell * .42f);
            return node;
        }

        /// <summary>
        /// The lowest rung of the top of the board nothing is hanging from.
        ///
        /// Asked of the crowns that are actually <em>up</em> rather than counted off the wave,
        /// because a boss's bar outlives its body by half a second (<see cref="Fall"/>) and a
        /// pair whose first member dies early must not have the survivor jump.
        /// </summary>
        int FreeCrown()
        {
            for (int slot = 0; ; slot++)
            {
                bool taken = false;

                for (int i = 0; i < _mob.Count; i++)
                    if (_mob[i].Crown != null && _mob[i].CrownSlot == slot) { taken = true; break; }

                if (!taken) return slot;
            }
        }

        /// <summary>
        /// What a warlord throws, as frames.
        ///
        /// <b>One set rather than four, graded to a colour no ward and no gem wears.</b> The
        /// elemental double is a rule about bolts going <em>into</em> a raider, so a spell coming
        /// out of one that wore one of the board's four colours would be saying something the
        /// rules do not mean — a player would reasonably read it as "this hurts the blue ward
        /// more". See <c>SiegeShotBake</c>.
        /// </summary>
        /// <summary>
        /// What crosses the hill, and <b>null for a warbringer, which throws nothing</b>.
        ///
        /// The one place the four are not four: a roar is aimed at no ward, so it has no flight
        /// and its flight reel is never loaded (<c>SiegeMode.Bosses</c>). Answering null rather
        /// than a reel nothing scoped in is what keeps a missing address from ever being asked
        /// for — an <c>Image</c> with no sprite is a white rectangle (invariant 7b).
        /// </summary>
        static Sprite[] SpellArt(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Blast("omen");
                case SiegeKind.Blightcaller: return Blast("hex");
                case SiegeKind.Warbringer: return null;
                default: return Blast("spell");
            }
        }

        static Sprite[] SpellMuzzleArt(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Blast("omen_muzzle");
                case SiegeKind.Blightcaller: return Blast("hex_muzzle");
                case SiegeKind.Warbringer: return Blast("roar_muzzle");
                default: return Blast("spell_muzzle");
            }
        }

        static Sprite[] SpellHitArt(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Blast("omen_hit");
                case SiegeKind.Blightcaller: return Blast("hex_hit");
                case SiegeKind.Warbringer: return Blast("roar_hit");
                default: return Blast("spell_hit");
            }
        }

        /// <summary>
        /// The colour each boss's magic is drawn in, and none of the four is a colour a gem wears.
        ///
        /// <para>
        /// <b>The elemental double is a rule about bolts going <em>into</em> a raider</b>, so a
        /// spell coming <em>out</em> of one in one of the board's four colours would be saying
        /// something the rules do not mean — a player would reasonably read it as "this hurts the
        /// blue ward more". <c>Pal</c>'s board set has exactly four entries that are none of
        /// <c>Poppy</c>, <c>Mint</c>, <c>Azure</c> or <c>Sun</c>, and the four bosses take one
        /// each.
        /// </para>
        /// <para>
        /// <b>Colour is the weakest of the three things that tell them apart, and it is here for
        /// completeness rather than as the answer.</b> A hex is a teal wisp, a smite a violet orb,
        /// a roar a white ring and an omen a magenta sun — different shapes, different sizes and
        /// different <em>effects on the line</em>, which is what a player actually reads. Teal
        /// against the blue gem is the closest pair of hues in the mode, which is exactly why the
        /// hex is the one drawn as a trailing wisp rather than as anything round.
        /// </para>
        /// </summary>
        static Color Casting(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Pal.Bloom;
                case SiegeKind.Blightcaller: return Pal.Aqua;
                case SiegeKind.Warbringer: return Pal.Radiance;
                default: return Pal.Foxglove;
            }
        }

        /// <summary>
        /// Rounded at the top and square at the foot, because the action bar is stacked directly
        /// under it.
        ///
        /// A fully rounded plate over a square shelf leaves two notches at the join, and at the
        /// bottom of a board they read as a gap rather than as two things meeting — which is what
        /// they are. This is the only mode with something under its board.
        /// </summary>
        protected override Sprite PlateSkin => Art.RoundTop(34);
    }
}
