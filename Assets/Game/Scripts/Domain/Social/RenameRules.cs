namespace GlimmerGrove.Social
{
    /// <summary>How a line under the rename field reads. The panel maps these to colours.</summary>
    public enum NameTone
    {
        /// <summary>Information. Nothing has gone wrong and nothing has gone right yet.</summary>
        Muted = 0,

        /// <summary>The name is free.</summary>
        Good,

        /// <summary>Something is in the way, and the player can do something about it.</summary>
        Bad,
    }

    /// <summary>What the panel says under the field, and whether saving is worth offering.</summary>
    public readonly struct NamePanelLine
    {
        /// <summary>The loc key to draw, or empty to say nothing at all.</summary>
        public readonly string Key;

        public readonly NameTone Tone;

        /// <summary>Whether the line takes the name's minimum length as an argument.</summary>
        public readonly bool TakesMinimum;

        /// <summary>Whether the save button does anything useful in this state.</summary>
        public readonly bool CanSave;

        public NamePanelLine(string key, NameTone tone, bool canSave, bool takesMinimum = false)
        {
            Key = key ?? string.Empty;
            Tone = tone;
            CanSave = canSave;
            TakesMinimum = takesMinimum;
        }
    }

    /// <summary>What the panel does with a claim's answer.</summary>
    public readonly struct RenameResolution
    {
        /// <summary>Whether the typed name becomes this device's name.</summary>
        public readonly bool StoresName;

        /// <summary>Whether the panel closes. False means the player is left able to act.</summary>
        public readonly bool Closes;

        /// <summary>The loc key to show, or empty. Shown before closing when both are set.</summary>
        public readonly string MessageKey;

        public readonly NameTone Tone;

        /// <summary>Whether the message carries the remaining cooldown as an argument.</summary>
        public readonly bool TakesCooldown;

        /// <summary>Whether this is a setback worth a sound.</summary>
        public readonly bool IsSetback;

        public RenameResolution(bool storesName, bool closes, string messageKey, NameTone tone,
                                bool takesCooldown = false, bool isSetback = false)
        {
            StoresName = storesName;
            Closes = closes;
            MessageKey = messageKey ?? string.Empty;
            Tone = tone;
            TakesCooldown = takesCooldown;
            IsSetback = isSetback;
        }
    }

    /// <summary>
    /// What the rename panel says and does. Two tables, and nothing else.
    ///
    /// <para>
    /// <b>Here rather than in the panel, for <c>NameCheckScheduler</c>'s reason.</b> These are
    /// the two branching decisions in the whole feature — what a line under the field reads,
    /// and what a claim's answer is worth — and both were <c>switch</c> statements inside a
    /// <c>MonoBehaviour</c>, which is the one place in this project nothing can be proved
    /// about. Moving them costs the panel nothing: it still owns every pixel, and now owns no
    /// rules.
    /// </para>
    /// <para>
    /// <b>The property worth proving is that a rename is never silently dropped.</b> For every
    /// answer the server can give, either the name is stored or the panel stays open with
    /// something to read — never neither. That is one assertion over an enum, it holds for
    /// members added later, and it is the failure a player would describe as "renaming does
    /// not work", which this codebase has already shipped once for a different reason
    /// (invariant 11c).
    /// </para>
    /// </summary>
    public static class RenameRules
    {
        /// <summary>
        /// What to say under the field for an availability, and whether saving is offered.
        ///
        /// <paramref name="fieldIsBlank"/> is asked because an empty field is not a mistake —
        /// it stores the default name — so it must not be scolded for being too short. It is
        /// the state the panel opens in for a player who has never renamed.
        /// </summary>
        public static NamePanelLine LineFor(NameAvailability availability, bool fieldIsBlank)
        {
            switch (availability)
            {
                case NameAvailability.TooShort:
                    // Blank is a real choice and saving it is allowed; anything else short is
                    // told why, before a read is spent on it.
                    return fieldIsBlank
                        ? new NamePanelLine(string.Empty, NameTone.Muted, canSave: true)
                        : new NamePanelLine("ui.profile.name_short", NameTone.Muted,
                                            canSave: false, takesMinimum: true);

                case NameAvailability.Checking:
                    return new NamePanelLine("ui.profile.name_checking", NameTone.Muted, canSave: true);

                case NameAvailability.Free:
                    return new NamePanelLine("ui.profile.name_free", NameTone.Good, canSave: true);

                case NameAvailability.Taken:
                    // The one state where the button is refused, and it is refused with the
                    // reason directly above it — which is the whole of `AdOfferState`'s rule
                    // about never greying a control without saying why.
                    return new NamePanelLine("ui.profile.name_taken", NameTone.Bad, canSave: false);

                case NameAvailability.Mine:
                    return new NamePanelLine("ui.profile.name_mine", NameTone.Muted, canSave: true);

                default:
                    // Nothing was decided: no backend, no signal, or a read that failed. Say
                    // nothing rather than guess, and let saving work — uniqueness is not this
                    // device's to enforce.
                    return new NamePanelLine(string.Empty, NameTone.Muted, canSave: true);
            }
        }

        /// <summary>
        /// What a claim's answer is worth.
        ///
        /// <para>
        /// Two outcomes keep the panel up, and both are things a player acts on: a name
        /// somebody else holds, which they change, and a cooldown, which they wait out. The
        /// rest store the name, because the name is theirs and the only question was whether a
        /// board would show it.
        /// </para>
        /// </summary>
        public static RenameResolution ResolveClaim(NameClaimOutcome outcome)
        {
            switch (outcome)
            {
                case NameClaimOutcome.Claimed:
                case NameClaimOutcome.Unchanged:
                    return new RenameResolution(storesName: true, closes: true,
                                                string.Empty, NameTone.Muted);

                case NameClaimOutcome.Taken:
                    // Not stored: the field still holds what has to change, and closing would
                    // leave the player believing they had been renamed.
                    return new RenameResolution(storesName: false, closes: false,
                                                "ui.profile.name_taken", NameTone.Bad,
                                                isSetback: true);

                case NameClaimOutcome.Cooldown:
                    // Rare — a second rename inside a minute — and deliberately not applied.
                    // Applying it would leave this device and the board disagreeing until the
                    // cooldown expired, which is a worse thing to explain than a countdown.
                    return new RenameResolution(storesName: false, closes: false,
                                                "ui.profile.name_cooldown", NameTone.Bad,
                                                takesCooldown: true, isSetback: true);

                case NameClaimOutcome.Refused:
                    // Never a rejection (invariant 19b): the name is kept and drawn on the
                    // player's own screens, and the boards show a generated handle. Said out
                    // loud rather than silently, because a name that quietly does not appear
                    // reads as the boards being broken — and saying it leaks that a filter
                    // exists without leaking what is in it.
                    return new RenameResolution(storesName: true, closes: true,
                                                "ui.profile.name_hidden", NameTone.Bad);

                default:
                    // Offline, no backend, or a server this build does not understand. The
                    // rename stands and the next publish claims it.
                    return new RenameResolution(storesName: true, closes: true,
                                                string.Empty, NameTone.Muted);
            }
        }
    }
}
