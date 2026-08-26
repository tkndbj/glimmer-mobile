using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Store;

namespace GlimmerGrove
{
    /// <summary>
    /// The one way out of a lost run that takes money instead of a heart: the offer, the price,
    /// the debit and the decision to fall through to a defeat when it is declined.
    ///
    /// <para>
    /// <b>It is a collaborator rather than more of <see cref="RunScreen"/>, and one place
    /// rather than one per mode.</b> Both halves of that matter. Every mode has a fail state
    /// and every fail state costs the player something, so what an exit costs has been
    /// <see cref="RunScreen"/>'s and never a mode's since Lightweave shipped a restart that was
    /// free (<c>RunStakeTests</c>) — two copies here would be two prices, two idempotency keys
    /// and two chances to charge somebody for a board that was still lost. But the stake is
    /// already a responsibility, and bolting a second economy onto the class that carries it is
    /// how a base class becomes the thing nobody dares change. So the rule lives once, and it
    /// lives here.
    /// </para>
    /// <para>
    /// <b>What a mode contributes is only what a mode alone can know</b> — the unit it measures
    /// its allowance in, how much of it has to be restored before a bought unit is a usable one,
    /// and how to hand it over. Not the price, not the panel, not the debit.
    /// </para>
    /// <para>
    /// <b>It never charges without a tap.</b> If a grant somehow left the run still lost, the
    /// mode's fail state fires again and the player is <em>asked again</em> rather than silently
    /// billed — so the worst case of a mispriced continue is an offer nobody takes, not money
    /// nobody agreed to.
    /// </para>
    /// <para>
    /// It holds the screen, which is a <c>MonoBehaviour</c>, so <c>if (_run)</c> is Unity's own
    /// lifetime check — the reason <see cref="RunScreen"/> is a base class rather than an
    /// interface, kept. See <see cref="StillOnScreen"/> for why that check is not enough on its
    /// own here.
    /// </para>
    /// </summary>
    public sealed class RunContinueFlow
    {
        readonly RunScreen _run;
        readonly RunHold _hold;

        /// <summary>Continues bought on this run. Reset with the run, not with the screen.</summary>
        int _taken;

        /// <summary>True while the offer, the gem shop or a payment sheet is up.</summary>
        bool _deciding;

        public RunContinueFlow(RunScreen run, RunHold hold)
        {
            _run = run;
            _hold = hold;
        }

        /// <summary>How many continues this run has been bought. For a mode that wants to say so.</summary>
        public int Taken => _taken;

        /// <summary>True while the player is deciding. The run must not advance.</summary>
        public bool Deciding => _deciding;

        /// <summary>Back to nothing bought. Every path that hands out a fresh run calls it.</summary>
        public void Reset() => _taken = 0;

        /// <summary>
        /// Whether a store is reachable that could sell gems right now.
        ///
        /// <para>
        /// Asked because of what the answer changes: a player short of gems is offered a way to
        /// buy some, and in a build with no store SDK that button leads to an empty panel. This
        /// project's rule is that a control which can never work is worse than no control, so
        /// the whole offer is withdrawn instead and the run is simply lost.
        /// </para>
        /// <para>
        /// <b>It is <em>not</em> false in the Editor</b>, and that is worth knowing before
        /// testing this: <c>UnityIapBackend.IsAvailable</c> is a flat <c>true</c> whenever
        /// <c>GLIMMER_IAP</c> is defined, so the Editor's fake store counts and the buy-gems
        /// branch is reachable there. (<c>AdOfferOverlay</c> says otherwise in a comment; it is
        /// describing a build without the package.) What the Editor cannot do is <em>grant</em>
        /// — a fake purchase never reaches <c>redeemPurchase</c>, so the gems never arrive and
        /// this panel never closes itself. The full loop needs a device build.
        /// </para>
        /// </summary>
        static bool GemsForSale => StoreService.IsAvailable && StoreRules.Catalog.HasGems;

        /// <summary>
        /// Offers a way to carry the run on, and loses it when that is declined.
        ///
        /// <para>
        /// <paramref name="lose"/> is what the mode used to do the moment it was beaten: charge
        /// the heart, write the record, put the defeat panel up. It runs unchanged, only later
        /// — and it runs on every path that is not a completed purchase, including the two no
        /// button knows about: the hardware back key, and this panel being destroyed with the
        /// screen. <c>ContinueOverlay</c> reports from <c>OnDestroy</c> for that reason, the way
        /// <c>AdOfferOverlay</c> does. The one report deliberately dropped is one arriving after
        /// this screen has already been replaced — see <see cref="StillOnScreen"/>.
        /// </para>
        /// </summary>
        public void OfferOrLose(Action lose)
        {
            if (lose == null || !_run) return;

            // Guarded rather than assumed: a run decided twice charges two hearts for one loss,
            // and a panel raised twice charges two lots of gems for one continue.
            if (_deciding) return;

            var offer = RunContinue.Offer(_run.MeasuredIn, _run.ContinueDeficit, _taken,
                                          Profile.Gems, GemsForSale);

            if (!offer.Exists) { lose(); return; }

            _deciding = true;
            _hold.Take(RunHold.Deciding);

            LevelAnalytics.TrackContinueOffered(_run.StakeLevel, offer);

            Flow.Modal<ContinueOverlay>(v =>
            {
                v.Offer = offer;
                v.Level = _run.StakeLevel;
                v.Bought = amount => { if (StillOnScreen) Bought(amount); };
                v.Declined = () => { if (!StillOnScreen) return; Done(); lose(); };
            });
        }

        /// <summary>
        /// Whether the run this belongs to is still the one the player is looking at.
        ///
        /// <para>
        /// <b>Not <c>_run != null</c>, which is not enough here.</b> The offer reports from
        /// <c>OnDestroy</c> so that it is heard however it closed, and <c>Flow.Go</c> destroys
        /// every modal and the outgoing screen in the same call — both at the end of the frame,
        /// in no defined order. So a report can arrive after the next screen has already been
        /// built, and a defeat panel raised then would land over it. <c>Flow.Current</c> is
        /// reassigned <em>synchronously</em> inside the swap, which makes it the one reading
        /// that is definitely settled by the time this is asked.
        /// </para>
        /// <para>
        /// Note what the run costs in that case: nothing is written down, so <c>RunGuard</c>'s
        /// marker is still on disk and the heart is charged at the next launch. That is the
        /// designed fallback for a run the process lost track of, and it is the correct answer
        /// here for the same reason.
        /// </para>
        /// </summary>
        bool StillOnScreen => _run && Flow.Current == (View)_run;

        /// <summary>
        /// The gems were taken. Hands the allowance over and gives the board back.
        ///
        /// <para>
        /// The order matters and is the reverse of the loss path's: the run is un-decided
        /// <em>before</em> the mode is extended, so a board that is somehow still lost raises
        /// its fail state again and reaches a fresh offer rather than being swallowed by the
        /// guard that stops one run being decided twice.
        /// </para>
        /// </summary>
        void Bought(int amount)
        {
            _taken++;
            Done();

            _run.ContinueWith(amount);
        }

        /// <summary>
        /// Lets the run go again, whichever way the offer ended.
        ///
        /// Named for what it does rather than something generic, for the reason
        /// <c>ModeScreen</c>'s <c>Resolve</c> coroutine cost this project a charged-twice run:
        /// two members with one name in one hierarchy is a bug waiting for the third.
        /// </summary>
        void Done()
        {
            _deciding = false;
            _hold.Release(RunHold.Deciding);
        }
    }
}
