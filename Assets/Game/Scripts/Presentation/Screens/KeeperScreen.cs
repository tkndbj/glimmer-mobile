using GlimmerGrove.Content;
using GlimmerGrove.Localization;

namespace GlimmerGrove
{
    /// <summary>Grovekeeper's screen. See <see cref="FallScreen"/> for the shape.</summary>
    public sealed class KeeperScreen : ModeScreen
    {
        KeeperView _view;

        protected override void Play()
        {
            var rules = Level.RulesAs<KeeperRules>();
            if (rules == null) return;

            _view = Host.gameObject.AddComponent<KeeperView>();
            _view.Changed = Repaint;
            _view.Over = Finish;
            _view.Begin(Host, rules.Width, rules.Height, rules.Tiles, rules.SeedFor(Level.Id));
        }

        protected override void Readouts(out string leftCap, out string left, out string middleCap,
                                         out string middle, out string rightCap, out string right)
        {
            leftCap = Loc.Get("mode.cap.score");
            middleCap = Loc.Get("mode.cap.blooms");
            rightCap = Loc.Get("mode.cap.tiles");

            if (_view == null) { left = middle = right = "0"; return; }
            _view.Readouts(out left, out middle, out right);
        }

        public override void RestartLevel()
        {
            if (_view == null) return;

            var rules = Level.RulesAs<KeeperRules>();
            _view.Begin(Host, rules.Width, rules.Height, rules.Tiles, rules.SeedFor(Level.Id));
            Repaint();
            Audio.Sfx("rotate_a", .55f);
        }
    }
}
