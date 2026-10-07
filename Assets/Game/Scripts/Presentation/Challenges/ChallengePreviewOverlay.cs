using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a daily challenge is, shown: the genre's board played by a hand, over and over,
    /// with its one sentence under it - the panel behind a first visit to each genre and
    /// behind the screen's info key.
    ///
    /// <para>
    /// <b>It replaced the tip boxes</b> (2026-10-02, the owner's instruction): each genre
    /// carried between one and three sentences with rings on the board, and the first thing a
    /// player met on a new puzzle was a wall of text in front of it. This is the loadout's
    /// answer to the same problem (<c>WardPreviewOverlay</c>): a turret is shown firing before
    /// it is bought, and a puzzle is shown being played before it is played. The demonstration
    /// is <see cref="ChallengeDemos"/>; what this panel owns is the frame, the words and the gate.
    /// </para>
    /// <para>
    /// <b>It wears the deal sheet's frame</b> (<see cref="VictoryFrame"/>, at the deal sheet's
    /// width), by the owner's instruction, so the two panels a player meets on the way into a
    /// challenge are one family. The banner carries the genre's name; the line over the stage
    /// is the lesson's title and the line under it the lesson's sentence, both the verb's own
    /// strings (<see cref="Mechanic.ChallengeVerb"/>) - a lesson is still a permanent id with
    /// two strings, it is drawn differently now.
    /// </para>
    /// <para>
    /// <b>The gate is the lesson ledger, and that is why no id was minted.</b> Each genre's
    /// verb lesson already meant "this player has been shown how this is played", and
    /// <c>TipLedger</c> already carries it in the save, union-joined across devices (53b's
    /// shape). So the panel is raised unasked when the verb has not been seen, marked seen when
    /// it is put away - on the way out rather than on the way in, so a player interrupted mid-panel
    /// is shown it again - and a player who has already read the old tip box is never shown it
    /// unasked. The info key raises it regardless, because a player who pressed the key asked.
    /// </para>
    /// <para>
    /// <b>Three ways out, one report</b>: the key, the scrim and the hardware key all go through
    /// <see cref="Accept"/>, and a panel torn down under a navigation still reports through
    /// <c>OnDestroy</c>, because the screen is holding its board for the answer
    /// (<c>TipOverlay</c>'s rule and its reason).
    /// </para>
    /// </summary>
    public sealed class ChallengePreviewOverlay : ModalView
    {
        /// <summary>Set before <c>Build</c>. The genre being shown.</summary>
        public ChallengeGenre Genre;

        /// <summary>Run once, however the panel goes away.</summary>
        public System.Action Dismissed;

        /// <summary>The deal sheet's width, so the two sheets read as one family.</summary>
        const float PanelW = 1000f;

        /// <summary>
        /// The stack, measured from the window's top edge: the banner hangs to about 108 below
        /// it, the title sits under that, the stage under the title, the sentence under the
        /// stage, and the key's block is the foot. <b>Every box the kit makes pivots at its
        /// centre</b> (<c>UIKit.Box</c>, invariants 44d and 49h), so a top edge here is placed as
        /// the edge plus half the height - the first cut passed the edge alone, and the stage
        /// stood half its height too high, over the banner and out of the window.
        /// </summary>
        const float TitleY = 150f, TitleH = 64f, StageTop = 226f, LineGap = 18f, LineH = 104f, FootH = 170f;

        static readonly Vector2 StageSize = new Vector2(880f, 560f);
        static readonly Vector2 KeySize = new Vector2(400f, 110f);

        bool _reported;

        protected override void Build()
        {
            var verb = Mechanic.ChallengeVerb(Genre);
            float panelH = StageTop + StageSize.y + LineGap + LineH + FootH;

            Scrim = UIKit.Scrim(Content, .72f, Accept);

            string name = Loc.Get("challenge.genre." + ChallengeGenres.NameOf(Genre) + ".name");
            var frame = VictoryFrame.Build(Content, panelH, name, PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            // No crown over this sheet, at the owner's instruction (2026-10-02): the banner with
            // the genre's name is the whole crest. Taken off rather than left at scale nought,
            // because a hidden widget is a widget somebody later pops.
            if (frame.Crown) Destroy(frame.Crown.gameObject);

            UIKit.Shrinkable(
                UIKit.Titled("Title", Panel, Loc.Get(verb.TitleKey), 40, Color.white, TextAnchor.MiddleCenter,
                             new Vector2(StageSize.x, TitleH), new Vector2(.5f, 1f), new Vector2(0f, -TitleY), 3f, 3f),
                24);

            // The stage: the board's own ground, so the demonstration stands on what the real
            // board stands on, with the sheet's hairline round it. Placed by its centre.
            var well = UIKit.Img("Well", Panel, Art.Round(30), ChallengeScreen.Ground, StageSize,
                                 new Vector2(.5f, 1f), new Vector2(0f, -(StageTop + StageSize.y * .5f)));
            well.type = Image.Type.Sliced;
            var edge = UIKit.Img("Edge", well.transform, Art.RoundOutline(30, 3f), new Color(1f, .96f, .86f, .14f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);
            edge.raycastTarget = false;

            ChallengeDemos.Build(Genre, (RectTransform)well.transform, StageSize, this);

            UIKit.Shrinkable(
                UIKit.Titled("Line", Panel, Loc.Get(verb.BodyKey), 32, Color.white,
                             TextAnchor.MiddleCenter, new Vector2(StageSize.x - 40f, LineH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(StageTop + StageSize.y + LineGap + LineH * .5f)), 2f, 2f, wrap: true),
                20);

            UIKit.TextButton("Play", Panel, Skins.Affirm, Loc.Get("ui.challenges.preview_play").Upper(), 36,
                             KeySize, new Vector2(.5f, 0f), new Vector2(0f, 30f + KeySize.y * .5f), Accept);

            // The entrance, in the deal sheet's order less its crown: the window, then the
            // banner and its word.
            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => Tween.Scale(Panel, 1f, .5f, Ease.OutBack));
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
        }

        /// <summary>
        /// The player has seen it. Marked on the way out rather than on the way in, so a panel
        /// interrupted by a call or a crash is shown again next time (<c>TipOverlay</c>'s rule).
        /// </summary>
        void Accept()
        {
            TipLedger.MarkSeen(Mechanic.ChallengeVerb(Genre));
            Close(Report);
        }

        /// <summary>Says the panel is finished with, once and once only.</summary>
        void Report()
        {
            if (_reported) return;
            _reported = true;

            Dismissed?.Invoke();
        }

        /// <summary>The backstop: a panel torn down under a navigation still releases the screen waiting on it.</summary>
        void OnDestroy() => Report();

        public override bool OnBack()
        {
            Accept();
            return true;
        }
    }
}
