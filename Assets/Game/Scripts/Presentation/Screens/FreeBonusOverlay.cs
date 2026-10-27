using System;
using System.Collections.Generic;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The free bonus panel (invariant 67): one row per task the server still offers this account -
    /// link a sign-in, choose a nickname - each with the gems it pays and one key that either goes to do
    /// the task or, once it is done, claims it.
    ///
    /// <para>
    /// <b>The limited deals panel's design, by the owner's instruction</b> (<see cref="DealOverlay"/>):
    /// the same <see cref="VictoryFrame"/> window, crown and banner, the same open card per row
    /// (<see cref="DealCard.BuildOpen"/>, painted by <see cref="DealCard.PaintGems"/>) with the task's name
    /// in the trough where a deal's clock stands and the key under it, the same white rule between rows,
    /// and the same NOT NOW key at the foot. Two rows at most, so they are built, not recycled (44ma).
    /// </para>
    /// <para>
    /// <b>The rows are decided when the panel opens</b> - the unpaid tasks - and a row claimed while it is
    /// open stays where it is, saying CLAIMED, so nothing jumps under a thumb (the deal rows' rule). The
    /// panel asks the server afresh as it opens (<see cref="FreeBonusLedger.RefreshAsync"/>), so a task
    /// taken on another phone repaints as claimed.
    /// </para>
    /// <para>
    /// <b>Doing a task is the game's own door</b>: the account panel to link (<see cref="AccountOverlay"/>),
    /// the rename panel to choose a name (<see cref="RenameOverlay"/>), each stacked over this one. Coming
    /// back, the row reads the new fact - a link raises <c>IdentityChanged</c>, a rename
    /// <c>ProfileChanged</c>, and the ledger repaints on both - and offers CLAIM. Claiming is the server
    /// paying; the gems fly from the row into the gem readout underneath, panel standing.
    /// </para>
    /// </summary>
    public sealed class FreeBonusOverlay : ModalView
    {
        // ------------------------------------------------------------------ geometry
        /// <summary>The deals panel's figures (<see cref="DealOverlay"/>), so the two sheets cannot drift.</summary>
        const float PanelW = 1030f;
        const float CellW = 950f, CellH = BonusRow.CardH + 30f;
        const float ListPad = 6f;
        const float RowsTop = 196f, Tail = 30f, FootH = 170f;
        const float KeyW = 400f, KeyH = 110f, KeyY = 85f;

        readonly List<BonusRow> _rows = new List<BonusRow>();
        int _flying;
        bool _leaving;

        protected override void Build()
        {
            var tasks = new List<FreeBonusTask>();
            foreach (var row in FreeBonusLedger.Rows) if (!row.Paid) tasks.Add(row.Task);
            if (tasks.Count == 0) { Flow.Dismiss(this); return; }

            float listH = ListPad + tasks.Count * CellH + ListPad;
            float panelH = RowsTop + listH + Tail + FootH;

            // Not dismissed by the scrim, the deals panel's rule: a stray tap must not put the offer away.
            Scrim = UIKit.Scrim(Content, .72f, null);
            var frame = VictoryFrame.Build(Content, panelH, Loc.Get("ui.free_bonus.title"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            for (int i = 0; i < tasks.Count; i++)
            {
                float y = -(RowsTop + ListPad + CellH * i);
                _rows.Add(new BonusRow(this, Panel, tasks[i], i, y));
            }

            UIKit.TextButton("NotNow", Panel, Skins.Alternate, Loc.Get("ui.common.cancel").Upper(), 36,
                             new Vector2(KeyW, KeyH), new Vector2(.5f, 0f), new Vector2(0f, KeyY),
                             () => Leave());

            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => { if (Panel) Tween.Scale(Panel, 1f, .5f, Ease.OutBack); });
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });

            FreeBonusLedger.Changed += Repaint;
            Repaint();

            // Asked afresh as it opens: another phone may have taken a task since the hub last asked.
            _ = RefreshObserved();
        }

        void OnDestroy() => FreeBonusLedger.Changed -= Repaint;

        public override bool OnBack() { Leave(); return true; }

        void Repaint()
        {
            if (!this) return;
            foreach (var row in _rows) row.Paint();
        }

        async System.Threading.Tasks.Task RefreshObserved()
        {
            try { await FreeBonusLedger.RefreshAsync(force: true); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Leave(bool quiet = false)
        {
            if (_leaving) return;
            _leaving = true;
            Close(quiet: quiet);
        }

        // ------------------------------------------------------------------ the keys
        void Do(FreeBonusTask task)
        {
            if (_leaving) return;

            if (task == FreeBonusTask.Link) Flow.Modal<AccountOverlay>();
            else Flow.Modal<RenameOverlay>(v => v.OnRenamed = Repaint);
        }

        void Claim(BonusRow row)
        {
            if (_leaving || FreeBonusLedger.IsClaiming(row.Task)) return;

            if (Net.Offline)
            {
                Scenery.Toast(Content, Loc.Get("ui.free_bonus.offline"), Pal.Sun, 2.6f);
                return;
            }
            _ = ClaimObserved(row);
        }

        /// <summary>
        /// The claim, observed rather than fire-and-forget: a fault is logged and the row takes taps again.
        /// Every answer is said - a refusal is never silence (13a: the player can act on each of them).
        /// </summary>
        async System.Threading.Tasks.Task ClaimObserved(BonusRow row)
        {
            FreeBonusClaim claim;
            try
            {
                claim = await FreeBonusLedger.ClaimAsync(row.Task);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                claim = new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);
            }

            if (!this || _leaving) return;
            Repaint();

            switch (claim.Outcome)
            {
                case FreeBonusClaimOutcome.Paid:
                    Audio.Sfx("coin", .6f);
                    PayOut(row, claim.Gems);
                    return;

                case FreeBonusClaimOutcome.Spent:
                    Scenery.Toast(Content, Loc.Get("ui.free_bonus.spent"), Pal.Sun, 2.6f);
                    return;

                case FreeBonusClaimOutcome.NotDone:
                    Scenery.Toast(Content, Loc.Get(row.Task == FreeBonusTask.Link
                                                       ? "ui.free_bonus.not_linked" : "ui.free_bonus.not_named"),
                                  Pal.Sun, 2.8f);
                    return;

                case FreeBonusClaimOutcome.NameRefused:
                    Scenery.Toast(Content, Loc.Get("ui.free_bonus.name_refused"), Pal.Sun, 2.8f);
                    Flow.Modal<RenameOverlay>(v => v.OnRenamed = Repaint);
                    return;

                case FreeBonusClaimOutcome.Used:
                    Scenery.Toast(Content, Loc.Get("ui.free_bonus.used"), Pal.Sun, 2.8f);
                    return;

                case FreeBonusClaimOutcome.None:
                    Scenery.Toast(Content, Loc.Get("ui.free_bonus.gone"), Pal.Sun, 2.6f);
                    return;

                default:
                    // `Unavailable`, and anything added later: said as a connection fault, never as paid (44e).
                    Scenery.Toast(Content, Loc.Get("ui.free_bonus.offline"), Pal.Sun, 2.6f);
                    return;
            }
        }

        /// <summary>
        /// The gems flying out of the row into the gem readout of the screen underneath, panel standing -
        /// <see cref="DealOverlay"/>'s payout. <see cref="RewardFlight.AfterGrant"/>, because the server has
        /// paid and the wallet already holds them. The last task this panel can pay closes it once they land.
        /// </summary>
        void PayOut(BonusRow row, int gems)
        {
            Scenery.Toast(Content, Loc.Format("ui.free_bonus.paid", gems.ToString("N0")), Pal.Gold, 2.4f);
            if (gems <= 0) return;

            bool last = true;
            foreach (var other in _rows) if (!other.IsPaid) last = false;

            var flight = RewardFlight.AfterGrant(0L, gems);
            flight.Hold(this);

            // A stand-in over the row's picture, which has to stay drawn, saying CLAIMED.
            var source = UIKit.Box("BonusGems", Content, new Vector2(200f, 200f), new Vector2(.5f, .5f), Vector2.zero);
            if (row.Picture) source.position = row.Picture.position;

            _flying++;
            flight.Add(new ChestDrop(ChestDropKind.Gems, gems), source);
            flight.Play(Content, () =>
            {
                if (source) Destroy(source.gameObject);
                if (!this) return;
                _flying--;
                if (last && _flying == 0) Leave(quiet: true);
            });
        }

        // ------------------------------------------------------------------ one row
        /// <summary>
        /// One task: the deal row's anatomy (<c>DealOverlay.DealRow</c>) - the open card with a gem heap and
        /// the amount, the task's name in the trough, and the key. Painted from the ledger on every change.
        /// Mirrored by <c>Tools/render_free_bonus.py</c>.
        /// </summary>
        sealed class BonusRow
        {
            public const float CardH = 330f;
            const float CardW = 940f, ColumnW = 520f;
            const float NameY = -16f, NameW = 480f, NameH = 62f, KeyY = -108f, KeyW = 400f, KeyH = 96f;
            const float RuleW = 860f, RuleH = 3f;

            readonly FreeBonusOverlay _panel;
            readonly DealCard.Parts _card;
            readonly Btn _key;
            readonly Image _keyPlate;

            public readonly FreeBonusTask Task;

            /// <summary>The gem heap at the left, which a paid task's gems fly out of.</summary>
            public RectTransform Picture => _card.Art ? _card.Art.rectTransform : _card.Root;

            public bool IsPaid => FreeBonusLedger.TryGet(Task, out var row) && row.Paid;

            public BonusRow(FreeBonusOverlay panel, RectTransform parent, FreeBonusTask task, int index, float top)
            {
                _panel = panel;
                Task = task;

                var root = UIKit.Box("Task", parent, new Vector2(CellW, CellH), new Vector2(.5f, 1f),
                                     new Vector2(0f, top - CellH * .5f));

                // The rule between rows, on the cell's top edge, behind the card - every row but the first.
                if (index > 0)
                {
                    var rule = UIKit.Img("Rule", root, Art.Pixel, new Color(1f, 1f, 1f, .85f), new Vector2(RuleW, RuleH),
                                         new Vector2(.5f, .5f), new Vector2(0f, CellH * .5f));
                    rule.raycastTarget = false;
                }

                _card = DealCard.BuildOpen("Card", root, new Vector2(CardW, CardH), new Vector2(.5f, .5f),
                                           Vector2.zero, ColumnW);
                FreeBonusLedger.TryGet(task, out var offered);
                DealCard.PaintGems(_card, offered.Gems);

                // The task's name where a deal's clock stands: one line, best fit, never wider than its trough.
                var trough = (RectTransform)UIKit.Img("Trough", _card.Root, Art.S("Ui/" + Skins.Trough), Color.white,
                                                      new Vector2(NameW, NameH), new Vector2(0f, .5f),
                                                      new Vector2(_card.ColumnX, NameY)).transform;
                UIKit.Shrinkable(
                    UIKit.Titled("Name", trough,
                                 Loc.Get(task == FreeBonusTask.Link ? "ui.free_bonus.link" : "ui.free_bonus.name"),
                                 34, Color.white, TextAnchor.MiddleCenter, new Vector2(NameW - 28f, NameH),
                                 new Vector2(.5f, .5f), Vector2.zero, 3f, 2f), 18);

                _key = UIKit.TextButton("Key", _card.Root, Skins.Gem, string.Empty, 40,
                                        new Vector2(KeyW, KeyH), new Vector2(0f, .5f), new Vector2(_card.ColumnX, KeyY),
                                        Tapped);
                _keyPlate = _key.GetComponent<Image>();
            }

            /// <summary>The key, from the ledger: CLAIMED, a claim in flight, CLAIM, or the way to do the task.</summary>
            public void Paint()
            {
                bool offered = FreeBonusLedger.TryGet(Task, out var row);

                string caption, skin;
                bool live;
                if (offered && row.Paid)
                {
                    caption = Loc.Get("ui.free_bonus.claimed");
                    skin = Skins.Alternate;
                    live = false;
                }
                else if (!offered)
                {
                    // Withdrawn under the panel (another phone, or a sign-in used elsewhere): closed, never sold (44e).
                    caption = Loc.Get("ui.free_bonus.closed");
                    skin = Skins.Shut;
                    live = false;
                }
                else if (FreeBonusLedger.IsClaiming(Task))
                {
                    caption = Loc.Get("ui.free_bonus.claim");
                    skin = Skins.Buy;
                    live = false;
                }
                else if (FreeBonusLedger.IsDone(Task))
                {
                    caption = Loc.Get("ui.free_bonus.claim");
                    skin = Skins.Buy;
                    live = true;
                }
                else
                {
                    caption = Loc.Get(Task == FreeBonusTask.Link ? "ui.free_bonus.do_link" : "ui.free_bonus.do_name");
                    skin = Skins.Gem;
                    live = true;
                }

                _key.SetCaption(caption.Upper());
                _keyPlate.sprite = Art.S("Ui/" + skin);
                _key.Interactable = live;
            }

            void Tapped()
            {
                if (!FreeBonusLedger.TryGet(Task, out var row) || row.Paid) return;
                if (FreeBonusLedger.IsDone(Task)) _panel.Claim(this);
                else _panel.Do(Task);
            }
        }
    }
}
