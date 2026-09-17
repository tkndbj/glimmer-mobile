namespace GlimmerGrove.Referral
{
    /// <summary>
    /// Whether the drops a claim came back with should be banked on this device.
    ///
    /// <para>
    /// <b>The server pays currency exactly once; this decides who banks the rest.</b> Hearts,
    /// boosts and utilities in a referral chest are not currency, so the server cannot pay
    /// them — the client applies them, and the question is which client. A reply that says
    /// <em>paid now</em> is banked, always. A reply that says <em>already paid</em> is banked
    /// only when <em>this device</em> asked for that chest and never heard the answer: the
    /// in-flight note is written before the call and cleared after the banking, so a lost
    /// reply leaves it standing and a second device, a reinstall or a double tap never sees it.
    /// </para>
    /// <para>
    /// The direction to be wrong in is chosen. A lost reply on a device with no note — a
    /// reinstall between the tap and the retry — banks nothing, and the player keeps the
    /// currency the server paid; banking on every <em>already paid</em> would hand out the
    /// same hearts on every device the account ever opens the page on.
    /// </para>
    /// </summary>
    public static class ReferralLanding
    {
        public enum Verdict
        {
            /// <summary>Apply the drops here.</summary>
            Bank,

            /// <summary>Somebody else banked them. Draw the chest as taken and apply nothing.</summary>
            Skip,

            /// <summary>Nothing was paid. Nothing to apply.</summary>
            Nothing,
        }

        public static Verdict Decide(ReferralClaimOutcome outcome, bool inFlightHere)
        {
            switch (outcome)
            {
                case ReferralClaimOutcome.Paid: return Verdict.Bank;
                case ReferralClaimOutcome.AlreadyPaid: return inFlightHere ? Verdict.Bank : Verdict.Skip;
                default: return Verdict.Nothing;
            }
        }

        /// <summary>
        /// The subject a chest is noted under, shared with the grant id the server writes:
        /// <c>rung:{friend}:{n}</c> for the referrer's n-th chest for their {friend}-th
        /// finished invitee, <c>invitee:{n}</c> for the invitee's n-th chest. Contract.
        /// </summary>
        public static string Subject(ReferralClaimKind kind, int goal, int index)
            => kind == ReferralClaimKind.Invitee ? "invitee:" + index : "rung:" + goal + ":" + index;
    }
}
