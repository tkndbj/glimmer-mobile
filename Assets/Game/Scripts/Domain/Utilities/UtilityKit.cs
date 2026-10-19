using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Utilities
{
    /// <summary>Why a change to the bar's kit was refused, in one word the caller can act on.</summary>
    public enum KitRefusal
    {
        /// <summary>Nothing was refused.</summary>
        None = 0,

        /// <summary>No such utility in this build's catalog.</summary>
        Unknown = 1,

        /// <summary>The bar already carries <see cref="UtilityKit.Size"/>; one has to come off first.</summary>
        Full = 2,

        /// <summary>That is the last item on the bar, and a bar carrying nothing is a bar nobody can use.</summary>
        Last = 3,
    }

    /// <summary>
    /// Which utilities ride on the action bar: the player's own choice of up to
    /// <see cref="Size"/> out of the whole catalog, the same on every board of every lane.
    ///
    /// <para>
    /// <b>A choice of which, never of how many.</b> What a player holds is
    /// <see cref="UtilityLedger"/>'s and is untouched by this - an item taken off the bar is still
    /// in the pack, still bought, still dropped by chests, and comes back the moment it is chosen
    /// again. So the kit can never confiscate anything (invariant 15a's rule), and nothing here
    /// reaches a grade: which five are carried decides what a player can answer a hill with, and
    /// every one of them is charged in matches exactly as it was (invariant 39).
    /// </para>
    /// <para>
    /// <b>Never chosen means the catalog's first five</b>, which is the bar every player had
    /// before the choice existed - so the update moves nobody's thumb, and a new player starts on
    /// the firepot, mending, surge, stormcall and gravity hole. It is derived rather than stored,
    /// so there is no "default" value in any file for a merge to be wrong about (11c).
    /// </para>
    /// <para>
    /// <b>Drawn in catalog order whatever order it was chosen in</b>
    /// (<see cref="UtilityItem.Order"/>), so a slot's place on the bar is a fact about the item
    /// rather than about the evening it was picked.
    /// </para>
    /// <para>
    /// <b>Stored as a setting</b> (<c>SettingsDto.kit</c>) and therefore synced: a preference
    /// the player made on purpose, joined by the newest file the way the language is.
    /// </para>
    /// </summary>
    public static class UtilityKit
    {
        /// <summary>
        /// How many the bar carries. The shelf is cut for five (<c>Tools/make_utility_art.py</c>'s
        /// <c>SLOTS</c>), and both bars that draw a kit read this.
        /// </summary>
        public const int Size = 5;

        /// <summary>Raised when the chosen kit changes. Never raised by a load or a sync.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------- reading
        /// <summary>The kit in force: what the bar draws, in catalog order, at most <see cref="Size"/>.</summary>
        public static IReadOnlyList<UtilityItem> Selected()
            => Resolve(UtilityLedger.Catalog, GameSettings.Kit);

        /// <summary>Whether the bar carries this one.</summary>
        public static bool IsSelected(UtilityItem item)
        {
            return item != null && Carries(Selected(), item);
        }

        /// <summary>
        /// What a stored choice means against a catalog. Pure, and the one place it is worked out.
        ///
        /// <para>
        /// An id the catalog does not know is skipped rather than refused (content may withdraw
        /// one; the setting is left as it was written - see <c>SettingsDto.kit</c>), a duplicate
        /// counts once, and the answer is never longer than <see cref="Size"/>. A choice that
        /// resolves to nothing - never made, or every id since withdrawn - is the catalog's first
        /// <see cref="Size"/>, so a bar is never empty for a reason the player did not choose.
        /// </para>
        /// </summary>
        public static IReadOnlyList<UtilityItem> Resolve(UtilityCatalog catalog, IReadOnlyList<string> chosen)
        {
            var items = catalog?.Items;
            var kit = new List<UtilityItem>(Size);
            if (items == null) return kit;

            if (chosen != null && chosen.Count > 0)
            {
                var wanted = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < chosen.Count; i++)
                    if (!string.IsNullOrEmpty(chosen[i])) wanted.Add(chosen[i]);

                for (int i = 0; i < items.Count && kit.Count < Size; i++)
                    if (wanted.Contains(items[i].Id)) kit.Add(items[i]);
            }

            if (kit.Count > 0) return kit;

            for (int i = 0; i < items.Count && kit.Count < Size; i++) kit.Add(items[i]);
            return kit;
        }

        // ------------------------------------------------------------- choosing
        /// <summary>Why toggling this one would be refused, or <see cref="KitRefusal.None"/>.</summary>
        public static KitRefusal WhyNotToggle(UtilityItem item)
            => WhyNotToggle(UtilityLedger.Catalog, GameSettings.Kit, item);

        /// <summary>The pure half of <see cref="WhyNotToggle(UtilityItem)"/>.</summary>
        public static KitRefusal WhyNotToggle(UtilityCatalog catalog, IReadOnlyList<string> chosen,
                                              UtilityItem item)
        {
            if (item == null || catalog == null || catalog.Find(item.Id) != item) return KitRefusal.Unknown;

            var kit = Resolve(catalog, chosen);
            bool carried = Carries(kit, item);

            if (carried) return kit.Count <= 1 ? KitRefusal.Last : KitRefusal.None;
            return kit.Count >= Size ? KitRefusal.Full : KitRefusal.None;
        }

        /// <summary>
        /// The choice after toggling <paramref name="item"/>, as the ids to store: in catalog
        /// order, the ones the catalog knows. Null when the toggle is refused. Pure.
        /// </summary>
        public static string[] Toggled(UtilityCatalog catalog, IReadOnlyList<string> chosen, UtilityItem item)
        {
            if (WhyNotToggle(catalog, chosen, item) != KitRefusal.None) return null;

            var kit = Resolve(catalog, chosen);
            bool carried = Carries(kit, item);

            var ids = new List<string>(Size);
            var items = catalog.Items;

            for (int i = 0; i < items.Count; i++)
            {
                var at = items[i];
                bool keep = at == item ? !carried : Carries(kit, at);
                if (keep) ids.Add(at.Id);
            }

            return ids.ToArray();
        }

        static bool Carries(IReadOnlyList<UtilityItem> kit, UtilityItem item)
        {
            for (int i = 0; i < kit.Count; i++)
                if (kit[i] == item) return true;

            return false;
        }

        /// <summary>
        /// Puts one on the bar or takes it off. Answers why not when it cannot.
        ///
        /// <b>Refused rather than swapped when the bar is full</b>: which of five comes off is
        /// the player's decision, and a shelf that quietly dropped one would take away the item
        /// they rely on to make room for one they were only looking at.
        /// </summary>
        public static KitRefusal Toggle(UtilityItem item)
        {
            var catalog = UtilityLedger.Catalog;
            var refusal = WhyNotToggle(catalog, GameSettings.Kit, item);
            if (refusal != KitRefusal.None) return refusal;

            if (GameSettings.SetKit(Toggled(catalog, GameSettings.Kit, item)))
            {
                try { Changed?.Invoke(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }

            return KitRefusal.None;
        }
    }
}
