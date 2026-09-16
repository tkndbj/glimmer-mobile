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

        /// <summary>
        /// The four colours a ward line is painted in, in <c>WardLine.Colours</c> order.
        ///
        /// <para>
        /// <b>The fourth is <see cref="Pal.Amber"/> rather than <see cref="Pal.Sun"/>, and the
        /// gem is what decided it.</b> The jewel this colour is matched on is cut from the pack
        /// at roughly 34 degrees of hue - an orange - while <c>Sun</c> sits at 43, so the one
        /// slot in this mode was painted two ways: an orange gem feeding a yellow turret, with a
        /// yellow raider walking down at it. Nothing numeric could see it, because every half of
        /// it was individually correct; it was reported off a device in four words. The other
        /// three tints sit within about eight degrees of their own gem, so this now does too.
        /// </para>
        /// <para>
        /// <b><c>Amber</c> rather than a fifth orange</b>, which is that entry's own instruction
        /// - it is named for the colour rather than for the line that first wanted it, precisely
        /// so the next warm accent does not invent a second orange a shade away from it.
        /// </para>
        /// </summary>
        static readonly Color[] Tints =
        {
            Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Amber,
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

                // A prism: a gem of no colour at all, which joins a run of any colour and is
                // paid as the colour it joined (`SiegeCharm.Prism`). It is a *face* rather than a
                // mark worn over one, because it is the one charm whose whole sentence is "this
                // is not one of the four" - and a mark on a coloured jewel would be saying the
                // opposite of that.
                case PrismColour: return Piece("gem_prism");

                // A thief's sack: not a colour, and deliberately the dullest thing on the field.
                case SackColour: return Piece("sack");

                // A bomber's bomb, drawn as the firepot it turns into when it is tapped.
                case BombColour: return Art.S("Ui/Utility/firepot");

                // A cog, which is what `SiegeBoard.ColourAt` answers -1 for. It is the one cell of
                // this field that is not a colour, so it is the one that falls off the end of a
                // table keyed on colour - and that is the right shape rather than a gap in one
                // (`SiegeLayout.Cells`).
                default: return Piece("gem_cog");
            }
        }

        /// <summary>
        /// The face a gem carrying a charm wears, or null for a charm that has no face of its own.
        ///
        /// <para>
        /// <b>A gem of its own rather than a mark worn over one</b>, which is the correction this
        /// pair exists for — see <c>SiegeMode.Cast</c> for the sentence that bought it. A lance is
        /// a stellated star and a stormglass a vortex orb, each cut in all four gem colours, so a
        /// charmed cell is a <em>different stone</em> and is still unmistakably the colour it is
        /// worth (invariant 37f, and 34f's rule that pieces differ in silhouette as well as hue).
        /// </para>
        /// <para>
        /// <b>Null for the prism and for no charm at all, and that is the right shape rather than
        /// a gap in one.</b> A prism is not a colour, so it has no per-colour face to pick: it is
        /// already a face in <see cref="GemArt"/>, reached through <c>PrismColour</c>. A
        /// <c>default</c> that answered one of these would be invariant 44e's fault exactly — the
        /// next charm added would fall through it and ship wearing somebody else's stone.
        /// </para>
        /// <para>
        /// <b>Every name is a literal at the point it is looked up</b>, which is invariant 6's
        /// rule for loc keys read across to art: <c>Tools/verify/artnames.py</c> reads the string
        /// off the call site, so a name built from a charm and a colour would be eight pictures
        /// nothing checks — and a white rectangle two cells wide is where that ends (7b).
        /// </para>
        /// </summary>
        static Sprite CharmFace(int colour, SiegeCharm charm)
        {
            switch (charm)
            {
                case SiegeCharm.Lance:
                    switch (colour)
                    {
                        case 0: return Piece("gem_lance_r");
                        case 1: return Piece("gem_lance_g");
                        case 2: return Piece("gem_lance_b");
                        case 3: return Piece("gem_lance_y");
                        default: return null;
                    }

                case SiegeCharm.Storm:
                    switch (colour)
                    {
                        case 0: return Piece("gem_storm_r");
                        case 1: return Piece("gem_storm_g");
                        case 2: return Piece("gem_storm_b");
                        case 3: return Piece("gem_storm_y");
                        default: return null;
                    }

                case SiegeCharm.Prism:
                case SiegeCharm.None:
                default: return null;
            }
        }

        /// <summary>
        /// The reel a charm detonates in, in the colour it was <b>paid</b>.
        ///
        /// <b>Its own bake rather than the ward impact it used to borrow</b> — see
        /// <c>SiegeShotBake.Charms</c> for the measurement. Keyed on the colour it was paid and
        /// never on the letter underneath, which is what makes it the payoff rather than
        /// decoration: a prism is drawn colourless and is worth the run it completed, so the burst
        /// is the one moment its choice is visible.
        /// </summary>
        static Sprite[] CharmBlast(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("charm_blast_r");
                case 1: return Blast("charm_blast_g");
                case 2: return Blast("charm_blast_b");
                case 3: return Blast("charm_blast_y");
                default: return null;
            }
        }

        /// <summary>What a cell holding a cog answers when it is asked its colour.</summary>
        const int CogColour = -1;

        /// <summary>
        /// What a cell carrying a prism is drawn as.
        ///
        /// <b>Its own number for <see cref="SackColour"/>'s reason.</b> A prism has a letter
        /// underneath it - the deal had to hand it something - and that letter is not what the
        /// cell is worth, so a view that drew the letter would be showing a red gem that pays
        /// green and reading as a bug. It is <em>not</em> the cog's -1 either: the two are
        /// opposite things, one is a gem that joins everything and the other is not a gem at all.
        /// </summary>
        const int PrismColour = -4;

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
        /// A bomb standing on the field, which is drawn as the <em>firepot the player already
        /// owns</em>.
        ///
        /// <b>The same picture on purpose.</b> A bomb tapped throws exactly what a firepot throws
        /// (invariant 39's charge included), so drawing it as anything else would be teaching a
        /// second name for one thing. It costs no art at all: <c>Utility/firepot</c> is resident
        /// because the action bar draws it on every siege.
        /// </summary>
        const int BombColour = -3;

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
        /// no ability throws the shared elemental reels the starter has always thrown
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
                // A sun, and the biggest thing on the board. **A fact about the effect rather
                // than about the rung or the id**: the shelf was re-rung (invariant 37ax) without
                // moving this, and the sun itself then moved from `prism` to `apex` (37ay) — and
                // the scale went with the picture, because what wants the room is the sun.
                case "apex": return 1.55f;
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
                case SiegeKind.Gravemaw: return Reel("maw");
                case SiegeKind.Bonecaller: return Reel("caller");
                case SiegeKind.Shackler: return Reel("snare");
                case SiegeKind.Ironclad: return Reel("clad");
            }

            // **A body per colour, which is what removing the tint bought.** It used to be
            // three creeper models handed out on `Colour % 3`, so two of the four colours shared a
            // body and the only thing separating them was a wash that has now gone. Four bodies
            // means the silhouette says the colour on its own — which is the half of the rule a
            // player who cannot separate red from green depends on.
            //
            // **One set rather than two, and that is the insect roster's size rather than a change
            // of mind.** A chapter used to draw one of two twelve-body casts by its ordinal, out
            // of eighty-three characters across nine monster, alien and robot packs. The raid is
            // insects now, the one pack on this machine that draws any holds fifteen, and the four
            // bosses take four of them — so there is exactly one cast and no arithmetic to do. A
            // second insect pack buys the second set back for one table in
            // `make_siege_art.RAIDER_SET` and nothing here.
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

            // **The address comes out of the same array the chapter preloaded.** Three switches of
            // twelve literals used to live here - the names written down twice, once to load and
            // once to draw - which is `SiegeGroundTests`' fault waiting to happen on thirty-six
            // names instead of ten. `SiegeMode.CastAddress` is the one copy.
            return AssetLibrary.Frames(SiegeMode.CastAddress(CastSet, raider.Kind, raider.Colour));
        }

        /// <summary>
        /// What this raider swings at the ward line, or <b>null</b> when its cast drew none.
        ///
        /// <b>Null is an ordinary answer and not a failure</b> — the insects and the brood have no
        /// attack animation in their packs, so their bodies keep walking where they stand exactly
        /// as they always have, and <b>six of the Infinite lane's twelve</b> are dealt from those
        /// two and answer the same way one body at a time (<c>SiegeMode.MedleySwings</c>). Asking
        /// the address first and the library second is what keeps any of them from requesting a
        /// reel that is not on disk: an <c>Image</c> with no sprite is a white rectangle over a
        /// raider (invariant 7b).
        /// </summary>
        Sprite[] Swing(SiegeRaider raider)
        {
            if (raider == null || raider.Boss) return null;

            string at = SiegeMode.CastSwing(CastSet, raider.Kind, raider.Colour);
            return string.IsNullOrEmpty(at) ? null : AssetLibrary.Frames(at);
        }

        /// <summary>
        /// Which cast this level draws — one of <see cref="SiegeMode.Insects"/>,
        /// <see cref="SiegeMode.Brood"/> or <see cref="SiegeMode.Medley"/>.
        ///
        /// <para>
        /// <b>Decided by <see cref="SiegeMode.CastFor"/> and nowhere else</b>, because the same
        /// answer has to reach two places: this, which <em>draws</em> the bodies, and
        /// <c>SiegeMode.ArtFor</c>, which <em>loads</em> them. Two switches forming their own
        /// opinions would load one cast and draw another, and an <c>Image</c> with a null sprite is
        /// a white rectangle over every raider on the hill rather than a blank (invariant 7b).
        /// <c>SiegeCastTests</c> is the comparison.
        /// </para>
        /// <para>
        /// Set by <c>SiegeScreen</c> before the board is built; nought when nothing has said
        /// otherwise, so a siege opened outside a chapter draws the insects rather than nothing.
        /// </para>
        /// </summary>
        public int CastSet;

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

                // **Three of the six throw nothing across the hill.** A roar is aimed at the line
                // from where the boss stands, a devour is aimed at the ground it is standing on,
                // and a raise puts bodies at the top of the hill - so none of the three has a
                // flight, and none of their flight reels is ever scoped in (`SiegeMode.Bosses`).
                case SiegeKind.Warbringer:
                case SiegeKind.Gravemaw:
                case SiegeKind.Bonecaller: return null;

                // **Both of the fourth chapter's do throw something**, which is what separates
                // them from the three above: a shackler looses an arrow at a ward and an ironclad
                // brings an axe down on one, so both have a flight and both are drawn crossing
                // the hill.
                case SiegeKind.Shackler: return Blast("snare");
                case SiegeKind.Ironclad: return Blast("quake");

                default: return Blast("spell");
            }
        }

        static Sprite[] SpellMuzzleArt(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Blast("omen_muzzle");
                case SiegeKind.Blightcaller: return Blast("hex_muzzle");
                case SiegeKind.Warbringer:
                case SiegeKind.Gravemaw:
                case SiegeKind.Bonecaller: return Blast("roar_muzzle");
                case SiegeKind.Shackler: return Blast("snare_muzzle");
                case SiegeKind.Ironclad: return Blast("quake_muzzle");
                default: return Blast("spell_muzzle");
            }
        }

        static Sprite[] SpellHitArt(SiegeKind kind)
        {
            switch (kind)
            {
                case SiegeKind.Overlord: return Blast("omen_hit");
                case SiegeKind.Blightcaller: return Blast("hex_hit");
                case SiegeKind.Warbringer:
                case SiegeKind.Gravemaw:
                case SiegeKind.Bonecaller: return Blast("roar_hit");
                case SiegeKind.Shackler: return Blast("snare_hit");
                case SiegeKind.Ironclad: return Blast("quake_hit");
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
        /// <c>Poppy</c>, <c>Mint</c>, <c>Azure</c> or <c>Amber</c>, and the four bosses take one
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

                // **The first two bosses here that are not magic at all**, and that is what lets
                // them past the rule this method is about rather than around it. A shackler is an
                // archer and an ironclad is a man with an axe, so what they throw is iron and
                // dust: `Dormant` is the unpowered slate, which is the only thing in this palette
                // that reads as *metal*, and a slam's dust is the same colourless `Radiance` a
                // roar is — pressure has no hue, and the two are two chapters apart. Neither can
                // be read as "this hurts the red ward more", which is the whole constraint.
                case SiegeKind.Shackler: return Pal.Dormant;
                case SiegeKind.Ironclad: return Pal.Radiance;

                // **The two that land on the hill rather than on the line**, which is what lets
                // them take the two colours left. A devour is `Verdant` and a raise is `Glass`,
                // and neither can be read as "this hurts the green ward more" for the reason the
                // paragraph above gives from the other side: the confusion this rule is about is
                // a spell in a ward's colour arriving *at that ward*, and nothing either of these
                // does ever reaches the line. `Verdant` is the sicklier of the two greens on the
                // wheel and sits on a mouth; `Glass` is the field's own ice white against the
                // roar's warm `Radiance`, and it comes up out of the ground.
                case SiegeKind.Gravemaw: return Pal.Verdant;
                case SiegeKind.Bonecaller: return Pal.Glass;

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
