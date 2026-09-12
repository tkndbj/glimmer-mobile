using GlimmerGrove.Persistence;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// The grove's one door into and out of a save file.
    ///
    /// <para>
    /// <b>Why the three ledgers are not called directly any more.</b> What a player has bought
    /// (<see cref="HomesteadLedger"/>), which ground they own (<see cref="GroveLand"/>) and
    /// where things stand (<see cref="HomesteadLayout"/>) are three sections, and every rule
    /// that spans them has to be applied to all three or to none — a grove whose purchases were
    /// kept and whose placements were dropped is a floor standing on pieces nobody owns. There
    /// was no such rule until <see cref="GroveEpoch"/>, so three calls at each of three call
    /// sites was fine; the moment one appeared, three call sites became three places for it to
    /// be forgotten. This project has paid for that shape more than once — a rule each caller
    /// remembers is a rule missing from whichever caller was written last.
    /// </para>
    /// <para>
    /// So the epoch is asked <em>here</em>, once, and a fourth grove section added next year is
    /// one line in this file rather than a hunt through <c>SaveService</c>.
    /// </para>
    /// </summary>
    public static class GroveSave
    {
        /// <summary>
        /// Brings the three grove ledgers up from a save.
        ///
        /// <para>
        /// A grove from an earlier generation of the catalogue is <b>not read at all</b>: the
        /// ledgers load from an empty stand-in, so the player starts on bare starter land with
        /// the free home, and the next snapshot writes that out stamped with this build's
        /// epoch. It is not a migration, because there is nothing to migrate to — every id in
        /// such a file names a piece the catalogue no longer has, so what it describes is a
        /// floor of tiles that read as occupied and draw nothing.
        /// </para>
        /// <para>
        /// The stand-in keeps the <em>rest</em> of the file: only the grove's own fields are
        /// blanked, so nothing outside the grove notices the epoch at all.
        /// </para>
        /// </summary>
        public static void LoadFrom(SaveFileDto dto)
        {
            var source = GroveEpoch.IsCurrent(dto) ? dto : Retired(dto);

            HomesteadLedger.LoadFrom(source);
            GroveLand.LoadFrom(source);
            HomesteadLayout.LoadFrom(source);
        }

        /// <summary>
        /// Writes the three grove ledgers into a save, stamped with this build's epoch.
        ///
        /// The stamp goes on unconditionally, which is what makes the reset stick: a device
        /// that loaded an older grove loaded nothing, and this is where it says so in a form
        /// the merge can act on. Without it the device would read empty, write epoch 0, and
        /// take the whole grove back from the cloud on its very next pull.
        /// </summary>
        public static void WriteInto(SaveFileDto dto)
        {
            if (dto == null) return;

            HomesteadLedger.WriteInto(dto);
            GroveLand.WriteInto(dto);
            HomesteadLayout.WriteInto(dto);

            dto.groveEpoch = GroveEpoch.Current;
        }

        /// <summary>
        /// The same save with its grove blanked — what a file from an earlier generation of the
        /// catalogue is read as.
        ///
        /// <para>
        /// A fresh DTO carrying only the grove's fields rather than a copy of the whole file:
        /// the three loaders read nothing else, and a shallow copy would be one more thing to
        /// keep in step with <see cref="SaveFileDto"/> every time a field is added.
        /// <c>homesteadOwned</c> is blanked with the rest, because <c>GroveStock.In</c> falls
        /// back to it for a document written before v20 and would otherwise re-grant the whole
        /// of an old grove's purchases.
        /// </para>
        /// </summary>
        static SaveFileDto Retired(SaveFileDto dto)
            => new SaveFileDto
            {
                schemaVersion = dto?.schemaVersion ?? SaveSchema.Version,
                homesteadStock = System.Array.Empty<HomesteadStockDto>(),
                homesteadOwned = System.Array.Empty<string>(),
                homesteadPlaced = System.Array.Empty<HomesteadPlacementDto>(),
                groveLandOwned = System.Array.Empty<string>(),
                groveEpoch = GroveEpoch.Current,
            };
    }
}
