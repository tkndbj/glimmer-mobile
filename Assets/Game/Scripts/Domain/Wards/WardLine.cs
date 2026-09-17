using System;
using System.Collections.Generic;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which turret stands on each colour: the thing the board reads, and the only part of the
    /// loadout that reaches a run.
    ///
    /// <para>
    /// <b>Immutable, resolved once, and handed to the board</b> rather than looked up from a
    /// static while a siege is running. That is what keeps the rules testable: a fixture can play
    /// a hill against the starter line and against a decked-out one without touching a save, and
    /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> can prove every rung is holdable with
    /// the <em>weakest</em> line a player could bring — which is the only version of that proof
    /// worth having.
    /// </para>
    /// <para>
    /// <b>Keyed on colour rather than on slot.</b> A level authors its own ward line
    /// (<c>SiegeLayout.Wards</c>) and may stand fewer than four, so a loadout keyed by position
    /// would put the player's frost turret on a different colour depending on which rung they
    /// opened. Colour is the thing the decision is actually about: the elemental double, a
    /// bulwark's soak and which ward a cog upgrades are all colour questions.
    /// </para>
    /// </summary>
    public sealed class WardLine
    {
        /// <summary>The colours a line may be drawn on, in the order the save writes them.</summary>
        public const string Colours = "rgby";

        readonly WardModel[] _byColour;

        /// <summary>
        /// How far each seat's turret has been upgraded, in colour order.
        ///
        /// <b>Beside the models rather than looked up when a board is built</b>, because a line is
        /// resolved once from the save and then handed around: a board that asked a ledger would
        /// be asking a *later* question than the one the line answered, and the two could differ
        /// across a sync. What a line is, is the whole answer to "what is standing here".
        /// </summary>
        readonly int[] _stars;

        WardLine(WardModel[] byColour, int[] stars)
        {
            _byColour = byColour;
            _stars = stars;
        }

        /// <summary>The turret standing on this colour. Never null once built.</summary>
        public WardModel this[char colour]
        {
            get
            {
                int i = Colours.IndexOf(colour);
                return i < 0 ? _byColour[0] : _byColour[i];
            }
        }

        /// <summary>The turret standing on this colour index (0..3). Never null once built.</summary>
        public WardModel At(int colour)
            => _byColour[colour < 0 || colour >= _byColour.Length ? 0 : colour];

        /// <summary>Every turret on the line, in colour order. Four entries, duplicates allowed.</summary>
        public IReadOnlyList<WardModel> Models => _byColour;

        /// <summary>
        /// The turret on this colour index <em>and how far it has been taken</em> — what the board
        /// actually stands.
        ///
        /// <b>One value rather than a model and a number</b>, for <see cref="WardBuild"/>'s reason:
        /// a caller that took the model and forgot the stars would silently play an un-upgraded
        /// turret, and nothing would report it.
        /// </summary>
        public WardBuild BuildAt(int colour)
        {
            int at = colour < 0 || colour >= _byColour.Length ? 0 : colour;
            return new WardBuild(_byColour[at], _stars[at]);
        }

        /// <summary>
        /// The line every player starts on, and the one a run falls back to.
        ///
        /// <b>Four of the roster's starter</b>, which is what a save that has never been written
        /// resolves to — and what every content gate and every rule test plays against.
        /// </summary>
        public static WardLine Starter(WardCatalog catalog)
        {
            catalog = catalog ?? WardCatalog.Default;

            var starter = catalog.Starter;
            var line = new WardModel[Colours.Length];
            var ladder = new int[Colours.Length];

            // The starter at the first star, which is where every turret begins and what every
            // content gate, offline mirror and rule test plays against.
            for (int i = 0; i < line.Length; i++)
            {
                line[i] = starter;
                ladder[i] = WardStars.Least;
            }

            return new WardLine(line, ladder);
        }

        /// <summary>
        /// The line a stored choice resolves to, with every gap and every refusal filled by the
        /// starter.
        ///
        /// <para>
        /// <b>A stored id is a <em>hint</em> and never an authority</b>, which is
        /// <c>ChapterChoice.Read</c>'s rule (invariant 8b): the roster is content, so a turret the
        /// player chose can leave the catalog on a drop, be renamed by a mistake, or belong to a
        /// build newer than this one. Every one of those has to land on a line that plays rather
        /// than on an empty slot, so the answer is always four turrets.
        /// </para>
        /// <para>
        /// <b>And ownership is re-checked here rather than trusted from the save</b> — the same
        /// clause for the same reason. A save says which turret was chosen; whether it may be
        /// stood on the line is a question about what the player holds <em>now</em>, and a device
        /// that lost a purchase to a failed sync must fall back rather than play a turret it
        /// cannot account for.
        /// </para>
        /// <para>
        /// <b>The ownership question takes the <em>seat's</em> colour</b>, because a turret is
        /// bought for one colour rather than for the line (<see cref="WardHolding"/>). Asked
        /// without it, a turret bought for red would stand on blue the moment somebody chose it
        /// there — which is the rule this feature exists to have, undone at the one place that
        /// decides what a board plays.
        /// </para>
        /// </summary>
        public static WardLine Resolve(WardCatalog catalog, IReadOnlyList<WardSlot> chosen,
                                       Func<WardModel, char, bool> held)
            => Resolve(catalog, chosen, held, null);

        /// <summary>
        /// The same, told how far each seat's turret has been upgraded.
        ///
        /// <b><paramref name="stars"/> may be null</b>, which is every turret at the first star —
        /// what a content gate, an offline mirror and every rule test play against, and what a
        /// save written before the ladder shipped means.
        /// </summary>
        public static WardLine Resolve(WardCatalog catalog, IReadOnlyList<WardSlot> chosen,
                                       Func<WardModel, char, bool> held,
                                       Func<WardModel, char, int> stars)
        {
            catalog = catalog ?? WardCatalog.Default;

            var starter = catalog.Starter;
            var line = new WardModel[Colours.Length];
            var ladder = new int[Colours.Length];

            for (int i = 0; i < line.Length; i++)
            {
                line[i] = starter;
                ladder[i] = Rung(stars, starter, Colours[i]);
            }

            if (chosen == null) return new WardLine(line, ladder);

            for (int i = 0; i < chosen.Count; i++)
            {
                int at = Colours.IndexOf(chosen[i].Colour);
                if (at < 0) continue;

                var model = catalog.Find(chosen[i].Ward);
                if (model == null) continue;
                if (held != null && !held(model, Colours[at])) continue;

                line[at] = model;
                ladder[at] = Rung(stars, model, Colours[at]);
            }

            return new WardLine(line, ladder);
        }

        /// <summary>How far this turret has been taken on this seat, with no lookup meaning one.</summary>
        static int Rung(Func<WardModel, char, int> stars, WardModel model, char colour)
            => stars == null ? WardStars.Least : WardStars.Sane(stars(model, colour));

        /// <summary>
        /// Every address this line's art needs, as a flat list, so a screen can scope it.
        ///
        /// <b>The line and never the roster</b> (invariant 7b): four turrets are on the screen and
        /// twenty are in the shop, so a run pays for four. The shop browses thumbnails out of one
        /// atlas instead (invariant 16c), which is why nothing here is ever asked for the whole
        /// catalog.
        /// </summary>
        public List<AssetPipeline.AssetRequest> Art()
        {
            var list = new List<AssetPipeline.AssetRequest>(Colours.Length * 5);

            // **Four seats and never four of anything else.** A line may stand the same turret
            // twice and, since the legendary band, may stand one whose four colours are one
            // address (`WardModel.Colourless`) - so the same request can arrive up to four times.
            // That is a duplicate claim on a scope rather than four things loading, and it is
            // dropped here rather than relied on being dropped downstream.
            var asked = new HashSet<string>(StringComparer.Ordinal);

            void Ask(AssetPipeline.AssetRequest request)
            {
                if (asked.Add(request.Address)) list.Add(request);
            }

            for (int i = 0; i < _byColour.Length; i++)
            {
                char colour = Colours[i];

                Ask(AssetPipeline.AssetRequest.Sprite(
                    AssetPipeline.AssetManifest.SiegeArt(_byColour[i].ArtFor(colour))));

                Ask(AssetPipeline.AssetRequest.SpriteSet(
                    AssetPipeline.AssetManifest.SiegeArt(_byColour[i].FireFor(colour))));

                // **The three reels a turret throws, and only for the ones that own a set.** The
                // one model that draws the shared elemental reels instead (`WardModel.Elemental`)
                // takes them from the mode's own cast, where they are resident — asking for them
                // here would be a second claim on an address the global set already owns, which
                // invariant 7b refuses. The two have to stay in step: drop them from `SiegeMode`
                // and that turret draws nothing at all.
                if (!_byColour[i].OwnShot) continue;

                Ask(AssetPipeline.AssetRequest.SpriteSet(
                    AssetPipeline.AssetManifest.SiegeFx(_byColour[i].ShotFor(colour))));

                Ask(AssetPipeline.AssetRequest.SpriteSet(
                    AssetPipeline.AssetManifest.SiegeFx(_byColour[i].MuzzleFor(colour))));

                Ask(AssetPipeline.AssetRequest.SpriteSet(
                    AssetPipeline.AssetManifest.SiegeFx(_byColour[i].HitFor(colour))));
            }

            return list;
        }

        public override string ToString()
        {
            var text = new System.Text.StringBuilder();

            for (int i = 0; i < _byColour.Length; i++)
                text.Append(i == 0 ? "" : " ").Append(Colours[i]).Append(':').Append(_byColour[i].Id);

            return text.ToString();
        }
    }

    /// <summary>
    /// One row of a stored loadout: a colour, and the turret the player put on it.
    ///
    /// <b>A pair rather than a positional array</b>, because the save writes it and a positional
    /// array cannot tell "the player chose nothing for blue" from "blue is the third element and
    /// the file was written by a build with three colours". A row that is not written is a colour
    /// that falls back, which is exactly the state a fresh account is in.
    /// </summary>
    public readonly struct WardSlot
    {
        public readonly char Colour;
        public readonly string Ward;

        public WardSlot(char colour, string ward)
        {
            Colour = colour;
            Ward = ward ?? string.Empty;
        }

        public bool IsValid => WardLine.Colours.IndexOf(Colour) >= 0 && Ward.Length > 0;
    }
}
