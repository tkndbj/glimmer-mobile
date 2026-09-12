namespace GlimmerGrove.Wards
{
    /// <summary>
    /// A turret as it actually stands: which model it is, and how far it has been upgraded.
    ///
    /// <para>
    /// <b>One value rather than two arguments, because they are two halves of one answer.</b> What
    /// a bolt weighs is the model's <c>PowerTenths</c> scaled by its stars; what a turret can take
    /// is its <c>GuardTenths</c> scaled the same way. Passed separately they are two chances for a
    /// call site to hand over one of them — which is how <c>SiegeView.Drop</c> came to draw every
    /// charging drop as a landed one (invariant 26f), and why <c>SiegeTuning.DamageTo</c> was
    /// already changed once to take a whole model instead of three of its fields.
    /// </para>
    /// <para>
    /// <b>It is the seam between content and save state, and naming it is most of its value.</b> A
    /// <see cref="WardModel"/> is content — the same on every device, retunable from a config push.
    /// A star count is the player's, stored and merged. Anything that needs the figures a player
    /// actually plays with needs both, and this is the one place they meet.
    /// </para>
    /// <para>
    /// <b>A struct, and the default is a real answer.</b> <c>default(WardBuild)</c> is a turret
    /// nobody chose at the least star, which is what every caller means by "nothing stood here"
    /// — so there is no null to test and no sentinel to remember.
    /// </para>
    /// </summary>
    public readonly struct WardBuild
    {
        /// <summary>Which turret, or null for a seat nobody has filled.</summary>
        public readonly WardModel Model;

        /// <summary>How far it has been upgraded, <c>WardStars.Least</c> to <c>WardStars.Most</c>.</summary>
        public readonly int Stars;

        public WardBuild(WardModel model, int stars)
        {
            Model = model;
            Stars = WardStars.Sane(stars);
        }

        /// <summary>A turret at the star it is bought with.</summary>
        public WardBuild(WardModel model) : this(model, WardStars.Least) { }

        public bool Has => Model != null;

        /// <summary>
        /// What its bolt weighs, in <b>hundredths</b> of the baseline, with its upgrades in it.
        ///
        /// <para>
        /// <b>Hundredths and not tenths, and that is a bug rather than a preference.</b> A model's
        /// weight is tenths and a star is ten per cent of it, so the two multiply into hundredths
        /// — and folding them back into tenths here truncates: <c>ember</c> carries guard 8, and
        /// 8 x 11 / 10 is 8.8, which comes back as 8. A player bought the second star and watched
        /// both figures stay exactly where they were. <b>Anything that multiplies two tenths keeps
        /// the hundredths and divides once, at the end.</b>
        /// </para>
        /// <para>
        /// <b>Never below the baseline.</b> The scale only ever adds, so the floor cannot be
        /// crossed from here — it is clamped anyway, because a floor that is only true by argument
        /// is a floor nobody can check.
        /// </para>
        /// </summary>
        public int PowerHundredths
        {
            get
            {
                if (Model == null) return WardModel.Baseline * 10;

                int fine = Model.PowerTenths * WardStars.ScaleTenths(Stars);
                int floor = WardModel.Baseline * 10;

                return fine < floor ? floor : fine;
            }
        }

        /// <summary>What it can take, in hundredths of the baseline. See <see cref="PowerHundredths"/>.</summary>
        public int GuardHundredths
        {
            get
            {
                if (Model == null) return WardModel.Baseline * 10;

                int fine = GuardFine();

                return fine < 1 ? 1 : fine;
            }
        }

        int GuardFine() => Model.GuardTenths * WardStars.ScaleTenths(Stars);

        /// <summary>What the next star costs, or nought at the top of the ladder.</summary>
        public int NextStarPrice => WardStars.PriceOf(Model, Stars);

        /// <summary>Whether there is another star to buy at all.</summary>
        public bool CanRise => Model != null && Stars < WardStars.Most;

        /// <summary>The same turret one star further up. Never past the top.</summary>
        public WardBuild Risen() => new WardBuild(Model, Stars + 1);

        public override string ToString()
            => (Model == null ? "(empty)" : Model.Id) + " " + Stars + "*";
    }
}
