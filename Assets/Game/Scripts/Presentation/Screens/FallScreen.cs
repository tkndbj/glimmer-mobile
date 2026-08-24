using GlimmerGrove.Content;
using GlimmerGrove.Localization;

namespace GlimmerGrove
{
    /// <summary>
    /// Lightfall's screen: a well, a tray, and the chrome every mode shares.
    ///
    /// It owns almost nothing. <c>FallBoard</c> is the rules, <c>FallView</c> is the board and
    /// its input, and <c>ModeScreen</c> is the furniture — so this file is the wiring between
    /// them and stays that way however far the mode grows.
    /// </summary>
    public sealed class FallScreen : ModeScreen
    {
        FallView _view;

        protected override void Play()
        {
            var rules = Level.RulesAs<FallRules>();
            if (rules == null) return;

            _view = Host.gameObject.AddComponent<FallView>();
            _view.Changed = Repaint;
            _view.Over = Finish;
            _view.Begin(Host, rules.Width, rules.Height, rules.SeedFor(Level.Id));
        }

        protected override void Readouts(out string leftCap, out string left, out string middleCap,
                                         out string middle, out string rightCap, out string right)
        {
            leftCap = Loc.Get("mode.cap.score");
            middleCap = Loc.Get("mode.cap.best");
            rightCap = Loc.Get("mode.cap.height");

            if (_view == null) { left = middle = right = "0"; return; }
            _view.Readouts(out left, out middle, out right);
        }

        public override void RestartLevel()
        {
            if (_view == null) return;

            var rules = Level.RulesAs<FallRules>();
            _view.Begin(Host, rules.Width, rules.Height, rules.SeedFor(Level.Id));
            Repaint();
            Audio.Sfx("rotate_a", .55f);
        }
    }
}
