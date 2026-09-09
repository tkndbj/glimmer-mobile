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

        WardLine(WardModel[] byColour) => _byColour = byColour;

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

            for (int i = 0; i < line.Length; i++) line[i] = starter;
            return new WardLine(line);
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
        /// </summary>
        public static WardLine Resolve(WardCatalog catalog, IReadOnlyList<WardSlot> chosen,
                                       Func<WardModel, bool> held)
        {
            catalog = catalog ?? WardCatalog.Default;

            var starter = catalog.Starter;
            var line = new WardModel[Colours.Length];

            for (int i = 0; i < line.Length; i++) line[i] = starter;
            if (chosen == null) return new WardLine(line);

            for (int i = 0; i < chosen.Count; i++)
            {
                int at = Colours.IndexOf(chosen[i].Colour);
                if (at < 0) continue;

                var model = catalog.Find(chosen[i].Ward);
                if (model == null) continue;
                if (held != null && !held(model)) continue;

                line[at] = model;
            }

            return new WardLine(line);
        }

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
            var list = new List<AssetPipeline.AssetRequest>(Colours.Length * 2);

            for (int i = 0; i < _byColour.Length; i++)
            {
                char colour = Colours[i];

                list.Add(AssetPipeline.AssetRequest.Sprite(
                    AssetPipeline.AssetManifest.SiegeArt(_byColour[i].ArtFor(colour))));

                list.Add(AssetPipeline.AssetRequest.SpriteSet(
                    AssetPipeline.AssetManifest.SiegeArt(_byColour[i].FireFor(colour))));
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
