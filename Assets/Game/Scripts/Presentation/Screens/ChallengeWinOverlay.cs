using GlimmerGrove.Challenges;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The daily challenge's victory panel: the run's own design (<see cref="VictoryFrame"/>
    /// — the green window, the crown over a banner, the light behind), carrying what the
    /// clear paid and nothing about the day's allowance.
    ///
    /// <para>
    /// <b>The same panel the ladder shows, by the owner's instruction (2026-09-24)</b>: a
    /// player who clears a challenge should be met by the victory they know, not by a
    /// sentence over a scrim. What is <em>not</em> here is everything that belongs to a
    /// level rather than to a puzzle — no stars, no route, no record, no rank, no chapter
    /// opened, no streak — because a challenge has none of them (invariant 56). And no
    /// "1 / 2 plays left": the allowance is the list page's business, and a countdown on a
    /// celebration reads as a bill.
    /// </para>
    /// <para>
    /// <b>The payout is <see cref="Payout"/>, exactly as the victory panel's.</b> The XP chip
    /// and the coin chip fly their tokens out of the crest rather than out of a star row,
    /// because that is what there is to fly them from; the boost line under them says what
    /// part of the XP a running boost paid, for the reason <c>WinOverlay.BoostXp</c> gives —
    /// a multiplier nobody sees is one nobody buys.
    /// </para>
    /// <para>
    /// <b>Not through <c>RankCeremony</c>, deliberately.</b> A rank counts runs, and a
    /// challenge is not a run: nothing it does reaches a task counter, a lifetime tally or a
    /// rung, so there is never a ceremony owed here (the compile rule about the two run
    /// panels is about those two panels).
    /// </para>
    /// </summary>
    public sealed class ChallengeWinOverlay : ModalView
    {
        /// <summary>The genre cleared, so NEXT can deal the day's next level of it.</summary>
        public ChallengeGenre Genre;

        /// <summary>What the clear paid, already banked by the ledger. <c>None</c> pays nothing.</summary>
        public ChallengeReward Reward;

        /// <summary>The boost that paid <see cref="ChallengeReward.BonusXp"/>, as a percentage. 0 when none ran.</summary>
        public int BoostPercent;

        /// <summary>The level cleared, for its name on the panel.</summary>
        public ChallengeDefinition Level;

        /// <summary>The room the crest takes at the top of the window before the first line.</summary>
        const float CrestRoom = 230f;

        /// <summary>The victory panel's own rows, so the two panels stack alike.</summary>
        const float NameRow = 70f, PayoutRow = 148f, GoldenRow = 74f, Tail = 40f, ButtonBlock = 208f;

        /// <summary>Where the button row sits above the frame's bottom edge, and how far out.</summary>
        const float ButtonY = 132f, SideButtonX = 340f;

        /// <summary>How much of the first chip the second waits for before it flies. <c>WinOverlay</c>'s figure.</summary>
        const float PayoutOverlap = .55f;

        protected override void Build()
        {
            bool paid = Reward.Any;
            bool boosted = paid && Reward.BonusXp > 0L && BoostPercent > 0;
            bool again = ChallengeLedger.CanPlay(Genre);

            // ---------------------------------------------------------- the stack
            float y = CrestRoom;
            float nameY = y + NameRow * .5f;
            y += NameRow;

            float payY = 0f, boostY = 0f, solvedY = 0f;
            if (paid) { payY = y + 70f; y += PayoutRow; }
            else { solvedY = y + 30f; y += GoldenRow; }
            if (boosted) { boostY = y + 34f; y += GoldenRow; }

            float panelH = y + Tail + ButtonBlock;

            UIKit.Scrim(Content, .66f);

            var frame = VictoryFrame.Build(Content, panelH, Loc.Get("ui.challenges.victory"));
            Backing = frame.Backing;
            Panel = frame.Panel;

            // ------------------------------------------------------------ the lines
            var name = Row("Name", -nameY, Level != null ? Loc.Get(Level.NameKey) : string.Empty, 44,
                           Pal.Cream, 780f, 26);

            Payout xpChip = null, coinChip = null;
            if (paid)
            {
                float spread = Reward.Xp > 0 && Reward.Coins > 0 ? 190f : 0f;
                long xp = Reward.Xp + Reward.BonusXp;

                if (xp > 0)
                {
                    xpChip = Payout.Chip("Xp", Panel, new Vector2(.5f, 1f), new Vector2(-spread, -payY),
                                         Art.Gem(128, Pal.Mint), Pal.Mint,
                                         n => Loc.Format("ui.win.xp", n), xp,
                                         Art.Gem(64, Pal.Mint), Color.white, sfx: "tick");
                    xpChip.Root.localScale = Vector3.zero;
                }

                if (Reward.Coins > 0)
                {
                    var frames = Art.Frames("Ui/Coin");
                    bool minted = frames != null && frames.Length > 0;

                    coinChip = Payout.Chip("Coins", Panel, new Vector2(.5f, 1f), new Vector2(spread, -payY),
                                           null, Pal.Gold, n => Loc.Format("ui.win.coins", Compact.Number(n)), Reward.Coins,
                                           minted ? frames[0] : Art.Disc(128),
                                           minted ? Color.white : Pal.Gold, sfx: "coin");
                    RewardArt.Glyph(coinChip.Glyph, ChestDropKind.Credits, 14f);
                    coinChip.Root.localScale = Vector3.zero;
                }
            }

            var solvedLine = paid ? null : Row("Solved", -solvedY, Loc.Get("ui.challenges.won"), 40, Pal.Parchment, 780f, 26);

            var boostLine = boosted
                ? Row("Boost", -boostY, Loc.Format("ui.win.xp_boosted", Compact.Number(Reward.BonusXp), BoostPercent),
                      40, Pal.Aqua, 780f, 26)
                : null;

            // ------------------------------------------------------------ the exits
            // NEXT deals the day's next level of this genre while a play is left; when none is,
            // the one green key is the list, which is where tomorrow and the deals are said.
            var go = UIKit.TextButton("Next", Panel, "btn_green",
                                      Loc.Get(again ? "ui.win.next" : "ui.challenges.title").ToUpperInvariant(), 50,
                                      new Vector2(520f, 152f), new Vector2(.5f, 0f), new Vector2(0f, ButtonY),
                                      () => Close(again ? (System.Action)ToNext : ToList));
            UIKit.Halo(go.transform, Pal.Mint, 620f, .28f);

            UIKit.IconButton("List", Panel, Skins.Nav, "ic_list", new Vector2(138f, 138f),
                             new Vector2(.5f, 0f), new Vector2(SideButtonX, ButtonY),
                             () => Close(ToList));

            if (Rebuilding)
            {
                frame.Settle();
                return;
            }

            // --------------------------------------------------------- the sequence
            // The victory panel's order: the crown, the window under a crown still settling,
            // the banner and its word, then the lines, then the payout lane out of the crest.
            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.42f, () => Tween.Scale(Panel, 1f, .55f, Ease.OutBack));
            cue.Then(.20f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
            cue.Then(.22f, () => Reveal(name));
            if (solvedLine) cue.Then(.18f, () => Reveal(solvedLine));

            if (paid)
            {
                cue.Then(.10f, () =>
                {
                    if (xpChip != null) Tween.Pop(xpChip.Root, .4f, .44f);
                    if (coinChip != null) Tween.Pop(coinChip.Root, .4f, .44f, .09f);
                });

                var origin = frame.Banner != null ? frame.Banner.transform : Panel;

                if (xpChip != null)
                {
                    cue.Then(.30f, () => xpChip.Play(origin));
                    if (coinChip != null) cue.Wait(xpChip.Duration * PayoutOverlap);
                }

                if (boostLine)
                {
                    cue.Then(xpChip != null ? 0f : .30f, () =>
                    {
                        if (!boostLine) return;
                        Tween.Pop(boostLine.transform, 0f, .55f);
                        Burst.Sparks(boostLine.transform, Vector2.zero, Pal.Aqua, 14, 280f, 22f, .6f);
                    });
                    cue.Wait(.22f);
                }

                if (coinChip != null)
                {
                    cue.Then(xpChip != null ? 0f : .30f, () => coinChip.Play(origin));
                    cue.Wait(coinChip.Duration);
                }
            }

            cue.Then(.30f, () =>
            {
                if (!go) return;
                Sheen.Attach((RectTransform)go.transform, 3.2f);
                Tween.Breathe(go.transform, .025f, 2f);
            });
        }

        void ToNext()
        {
            var genre = Genre;
            Flow.Go<ChallengeScreen>(v => v.Genre = genre);
        }

        static void ToList() => Flow.Go<DailyChallengesScreen>();

        /// <summary>A centred line of the panel's own copy, hidden until its beat.</summary>
        Text Row(string name, float y, string text, int size, Color ink, float width, int minSize)
        {
            var t = UIKit.Titled(name, Panel, text, size, ink, TextAnchor.MiddleCenter,
                                 new Vector2(width, size + 12f), new Vector2(.5f, 1f),
                                 new Vector2(0f, y), 4f, 3f);
            UIKit.Shrinkable(t, minSize);
            t.transform.localScale = Rebuilding ? Vector3.one : Vector3.zero;
            return t;
        }

        static void Reveal(Text t)
        {
            if (t) Tween.Pop(t.transform, 0f, .4f);
        }

        public override bool OnBack()
        {
            Close(ToList);
            return true;
        }
    }
}
