using System.Collections;
using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Merge: a gem per cell in the colour of its rank, with the rank's value written on it,
    /// growing a little with each rank so a 32 reads as heavier than a 2. A swipe anywhere on
    /// the board slides it.
    /// </summary>
    public sealed class MergeView : PuzzleView
    {
        MergePuzzle _merge;
        Image[] _gem;
        Text[] _value;

        protected override void Build()
        {
            _merge = (MergePuzzle)Run.Puzzle;
            int n = Columns * Rows;

            Sockets();
            _gem = new Image[n];
            _value = new Text[n];

            for (int i = 0; i < n; i++)
            {
                _gem[i] = GemAt(i, 0);
                _value[i] = UIKit.Titled("Value", Field, string.Empty, Mathf.RoundToInt(Cell * .30f), Pal.Cream,
                                         TextAnchor.MiddleCenter, Vector2.one * Cell, default, default, 2f, 2f);
                _value[i].rectTransform.anchoredPosition = CentreOf(i);
                _value[i].raycastTarget = false;
            }

            Swipes(dir => Send(ChallengeInput.Swipe(dir.x, dir.y)));
        }

        public override void Repaint()
        {
            for (int i = 0; i < _gem.Length; i++) Paint(i);
        }

        void Paint(int i)
        {
            int rank = _merge.RankAt(i);
            bool shown = rank > 0;

            _gem[i].sprite = shown ? ChallengeArt.Gem(MergePuzzle.ColourOf(rank)) : null;
            _gem[i].enabled = shown && _gem[i].sprite != null;
            _gem[i].rectTransform.sizeDelta = Vector2.one * Cell * (.66f + Mathf.Min(rank, 8) * .03f);
            _gem[i].transform.localScale = Vector3.one;

            _value[i].enabled = shown;
            _value[i].text = shown ? (1 << rank).ToString() : string.Empty;
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            Repaint();

            var merged = _merge.LastMerged;
            for (int i = 0; i < merged.Count; i++)
            {
                Tween.Punch(_gem[merged[i]].transform, .28f, .26f);
                Burst.Sparks(Field, CentreOf(merged[i]), ChallengeArt.Tint(MergePuzzle.ColourOf(_merge.RankAt(merged[i]))),
                             8, Cell * 1.3f, Cell * .1f, .38f);
            }

            if (_merge.LastDealt >= 0) Tween.Pop(_gem[_merge.LastDealt].transform, .3f, .22f);

            Audio.SfxVaried(merged.Count > 0 ? "chime" : "whoosh", merged.Count > 0 ? .6f : .35f);

            if (merged.Count > 0) yield return new WaitForSecondsRealtime(.14f);
        }
    }
}
