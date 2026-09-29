using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Infinite lane's checkpoints: one row for the beginning and one per checkpoint the file
    /// carries - its wave on a disc, what it gives the line, and a key to start there (MODES.md 43f).
    ///
    /// <para>
    /// <b>It wears the deal sheet's frame</b>, at the owner's instruction on 2026-09-29 - the green
    /// window, the fan and the crown over a banner carrying one word, built by
    /// <see cref="VictoryFrame"/> so the three panels cannot drift - and the deal sheet's row: a
    /// dark inset with a rim, a picture on the left, a name over a sentence, and the answer on the
    /// right. What differs is only what a row has to say, so the rows are shorter
    /// (<see cref="RowH"/>) and the sheet stands every checkpoint the table may carry
    /// (<see cref="EndlessCheckpointLimits.MaxRows"/>) on one screen with no scroller.
    /// </para>
    /// <para>
    /// <b>Every row is drawn, open or not</b>, because a start a player cannot see is a start
    /// nobody knows to earn - the shelf's rule (42a) said of a checkpoint. A shut row says the
    /// wave that opens it and the player's own best beside it, and its key answers a tap with
    /// that sentence rather than swallowing it (16o).
    /// </para>
    /// <para>
    /// <b>Choosing closes the sheet.</b> The choice is one tap with nothing to confirm - it costs
    /// nothing and moves both ways - and the bar under the sheet is watched, so it already says
    /// the new wave by the time the sheet is gone.
    /// </para>
    /// </summary>
    public sealed class EndlessCheckpointOverlay : ModalView
    {
        /// <summary>The lane this sheet chooses a start on. Set by the caller before it is built.</summary>
        public LevelId Level;

        /// <summary>
        /// The stack, measured from the window's top edge - the deal sheet's, with a row cut to
        /// what a checkpoint says: the crest hangs to about 108 below the edge, the note sits
        /// under it, the rows under the note and the CLOSE key's block at the foot.
        /// </summary>
        const float NoteY = 150f, RowsTop = 200f, RowH = 150f, RowGap = 12f, Tail = 30f, FootH = 170f;

        /// <summary>The deal sheet's widths, so the two read as one family.</summary>
        const float PanelW = 1000f, RowW = 880f, DiscSize = 118f, DiscX = 84f, TextX = 168f, TextW = 420f;
        static readonly Vector2 KeySize = new Vector2(250f, 100f);
        const float KeyInset = 16f;

        protected override void Build()
        {
            var table = EndlessCheckpoints.Table;
            int best = EndlessLedger.BestFor(Level);
            var chosen = EndlessCheckpoints.Chosen(Level);

            int rowCount = table.Rows.Count + 1;
            float rows = rowCount * (RowH + RowGap) - RowGap;
            float panelH = RowsTop + rows + Tail + FootH;

            Scrim = UIKit.Scrim(Content, .72f, () => Close());

            var frame = VictoryFrame.Build(Content, panelH, Loc.Get("ui.endless.checkpoint.title"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            // The one line every row shares: what a checkpoint is, and what it does not pay.
            UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get("ui.endless.checkpoint.note"), 32,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter, new Vector2(860f, 60f),
                             new Vector2(.5f, 1f), new Vector2(0f, -NoteY), 2f, 2f, wrap: true),
                20);

            float y = -(RowsTop + RowH * .5f);

            // The beginning first: it is always open, and it is what every row below is measured
            // against - the whole watch, paid for every wave.
            BuildRow(default, best, !chosen.IsValid, y);
            y -= RowH + RowGap;

            foreach (var row in table.Rows)
            {
                BuildRow(row, best, chosen.IsValid && chosen.Wave == row.Wave, y);
                y -= RowH + RowGap;
            }

            UIKit.TextButton("Close", Panel, Skins.Alternate,
                             Loc.Get("ui.endless.checkpoint.close").ToUpperInvariant(), 36,
                             new Vector2(400f, 110f), new Vector2(.5f, 0f), new Vector2(0f, 30f + 55f),
                             () => Close());

            if (Rebuilding)
            {
                frame.Settle();
                return;
            }

            // The deal sheet's entrance, beat for beat: crown, window, banner, word.
            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => Tween.Scale(Panel, 1f, .5f, Ease.OutBack));
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
        }

        /// <summary>
        /// One start. <paramref name="row"/> is the default for the beginning, which is always
        /// open; <paramref name="selected"/> is whether the next run opens here.
        /// </summary>
        void BuildRow(EndlessCheckpoint row, int best, bool selected, float y)
        {
            bool beginning = !row.IsValid;
            bool open = beginning || row.OpenAt(best);
            int wave = beginning ? 1 : row.Wave;

            // The deal sheet's inset: dark on the green window, a gold rim on the row that is
            // chosen, so the state is said by the row's edge as well as by its key.
            var plate = UIKit.Img("Row_" + wave, Panel, Art.Round(28), new Color(0f, 0f, 0f, .32f),
                                  new Vector2(RowW, RowH), new Vector2(.5f, 1f), new Vector2(0f, y));
            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f),
                                 selected ? Pal.A(Pal.Gold, .62f) : new Color(1f, .96f, .86f, .14f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);
            edge.raycastTarget = false;
            var t = plate.transform;

            // The wave on the kit's own medallion - the medal the hub draws the record on - lit
            // when it can be started from and dark when it cannot. Global art, so never a white
            // rectangle (7b).
            var disc = UIKit.Img("Disc", t, Art.S("Ui/" + (open ? Skins.CapOn : Skins.CapOff)), Color.white,
                                 Vector2.one * DiscSize, new Vector2(0f, .5f), new Vector2(DiscX, 0f));
            disc.preserveAspect = true;
            disc.raycastTarget = false;
            UIKit.Halo(disc.transform, selected ? Pal.Gold : Pal.Bloom, DiscSize * 1.8f, selected ? .34f : .14f);

            // An open start carries its wave in the medal's own dark ink; a shut one carries the
            // padlock alone - the row's name already says the wave, and a number under a padlock
            // was a number with a padlock over it (render_checkpoints.py).
            if (open)
            {
                UIKit.Shrinkable(
                    UIKit.Titled("Number", disc.transform, wave.ToString(), 50, new Color(.32f, .21f, .06f),
                                 TextAnchor.MiddleCenter, new Vector2(DiscSize * .78f, DiscSize * .56f),
                                 new Vector2(.5f, .5f), new Vector2(0f, 3f), 0f, 0f),
                    28);
            }
            else
            {
                var padlock = UIKit.Img("Padlock", disc.transform, Art.S("Ui/ic_padlock"), Color.white,
                                        Vector2.one * (DiscSize * .46f), new Vector2(.5f, .5f), Vector2.zero);
                padlock.preserveAspect = true;
                padlock.raycastTarget = false;
            }

            UIKit.Shrinkable(
                UIKit.Titled("Name", t, Loc.Format("ui.endless.wave", wave), 46,
                             selected ? Pal.Gold : open ? Pal.Cream : Pal.A(Pal.Cream, .62f),
                             TextAnchor.MiddleLeft, new Vector2(TextW, 58f), new Vector2(0f, .5f),
                             new Vector2(TextX + TextW * .5f, 30f), 3f, 3f),
                26);

            UIKit.Shrinkable(
                UIKit.Titled("Line", t, LineFor(row, best, open), 28,
                             new Color(1f, .96f, .88f, open ? .88f : .62f), TextAnchor.UpperLeft,
                             new Vector2(TextW, 66f), new Vector2(0f, .5f),
                             new Vector2(TextX + TextW * .5f, -30f), 2f, 0f, wrap: true),
                18);

            var keyPos = new Vector2(-(KeyInset + KeySize.x * .5f), 0f);

            if (selected)
            {
                var tag = UIKit.Img("Chosen", t, Art.Round(24), Pal.A(Pal.Gold, .95f), KeySize,
                                    new Vector2(1f, .5f), keyPos);
                tag.raycastTarget = false;
                UIKit.Shrinkable(
                    UIKit.Titled("ChosenText", tag.transform,
                                 Loc.Get("ui.endless.checkpoint.chosen").ToUpperInvariant(), 32, Pal.Ink,
                                 TextAnchor.MiddleCenter, KeySize, default, default, 0f, 0f),
                    18);
                return;
            }

            var key = open
                ? UIKit.TextButton("Choose", t, Skins.Affirm,
                                   Loc.Get("ui.endless.checkpoint.choose").ToUpperInvariant(), 34, KeySize,
                                   new Vector2(1f, .5f), keyPos, () => Choose(wave))
                : UIKit.TextButton("Shut", t, Skins.Shut,
                                   Loc.Get("ui.endless.checkpoint.locked").ToUpperInvariant(), 34, KeySize,
                                   new Vector2(1f, .5f), keyPos, () => Refuse(row), "ic_padlock");

            UIKit.OneLine(key, 20);
        }

        /// <summary>What a row says under its wave: the head start, or what opens it.</summary>
        static string LineFor(EndlessCheckpoint row, int best, bool open)
        {
            if (!row.IsValid) return Loc.Get("ui.endless.checkpoint.beginning");

            if (!open) return Loc.Format("ui.endless.checkpoint.locked_line", row.UnlockAt, best);

            // The head start in the player's own unit: a turret's badge reads its level, and a
            // cog spent is a level up (`SiegeWard.Level`).
            return row.Cogs <= 0 ? Loc.Get("ui.endless.checkpoint.bare")
                 : row.Cogs == 1 ? Loc.Get("ui.endless.checkpoint.boost_one")
                 : Loc.Format("ui.endless.checkpoint.boost", row.Cogs);
        }

        void Choose(int wave)
        {
            if (!EndlessCheckpoints.Choose(Level, wave))
            {
                // Only reachable when the best moved under an open sheet - a sync on the other
                // phone - so the honest answer is the sheet as it now stands.
                Rebuild();
                return;
            }

            Audio.Sfx("collect", .7f);
            Close();
        }

        void Refuse(EndlessCheckpoint row)
        {
            Scenery.Toast(Content, Loc.Format("ui.endless.checkpoint.locked_hint", row.UnlockAt), Pal.Gold, 2.4f);
        }

        public override bool OnBack()
        {
            Close();
            return true;
        }
    }
}
