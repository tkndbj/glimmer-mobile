namespace GlimmerGrove
{
    /// <summary>
    /// Which jelly skin each button role wears.
    ///
    /// <para>
    /// This exists because of one collision. <see cref="Btn.Interactable"/> paints a
    /// disabled button by tinting it toward a desaturated grey — that is the game's
    /// word for "you cannot press this". The chrome controls were built from the grey
    /// skins (<c>sq_dark</c>, <c>btn_gray</c>), so back, pause, settings, info, rename,
    /// the chapter arrows, every overlay's dismiss cross and every unselected nav tab
    /// were drawn in the same colour as a dead control. There is no reading of that a
    /// player can get right: the loudest thing about a button is its fill, and every
    /// piece of chrome in the game was filled with "off".
    /// </para>
    ///
    /// <para>
    /// So the rule is now: <b>grey means "not a control right now", and nothing else.</b>
    /// A switch that is off and a streak night that has not arrived are grey. Anything
    /// a finger can usefully land on carries colour.
    /// </para>
    ///
    /// <para>
    /// It is a lookup rather than a literal at each call site for the ordinary reason —
    /// this is a taste decision that will be revisited, and eighteen screens naming a
    /// sprite each is eighteen places to miss. A skin named here is also guaranteed to
    /// be in <c>AssetManifest</c>, which a hand-typed one is not.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The pill skins the game already used keep their meanings and are still named at
    /// their call sites, because they were never ambiguous: <c>btn_green</c> is the
    /// affirmative action (play, collect, retry, resume, buy), <c>btn_blue</c> the other
    /// pill in the same panel, <c>btn_orange</c> restart, <c>btn_red</c> leaving or
    /// wiping, <c>btn_violet</c> the event. Only the greys moved.
    /// </remarks>
    public static class Skins
    {
        /// <summary>
        /// The ordinary square control: back, forward, pause, an overlay's dismiss cross,
        /// an unselected nav tab. Blue because it is the colour this UI already spends on
        /// a secondary action (<c>btn_blue</c>, the undo and map keys), so chrome reads as
        /// live without competing with the green that means "do the thing".
        /// </summary>
        public const string Nav = "sq_blue";

        /// <summary>
        /// A square control that explains or configures rather than moving you: the "i",
        /// the gear, the rename pencil. Separated from <see cref="Nav"/> because these sit
        /// in the opposite top corner from a back key on four screens, and two controls
        /// that do different things should not be the same button in two places.
        /// </summary>
        public const string Aside = "sq_aqua";

        /// <summary>
        /// Not a control. An off switch, a streak night that has not come round yet — the
        /// only things allowed to wear the disabled colour on purpose.
        /// </summary>
        public const string Resting = "sq_dark";

        /// <summary>
        /// The second pill in a panel that has two: the one that is not the affirmative.
        /// Named here only because the cancel key used to be <c>btn_gray</c> and read as
        /// broken; every other pill still names its colour where it is built.
        /// </summary>
        public const string Alternate = "btn_blue";
    }
}
