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

        protected override void Readouts(System.Collections.Generic.List<Readout> into)
        {
            string score = "0", best = "0", height = "0";
            if (_view != null) _view.Readouts(out score, out best, out height);

            into.Add(new Readout(Loc.Get("mode.cap.score"), score));
            into.Add(new Readout(Loc.Get("mode.cap.best"), best));
            into.Add(new Readout(Loc.Get("mode.cap.height"), height));
        }

        protected override void Rewind()
        {
            if (_view == null) return;

            var rules = Level.RulesAs<FallRules>();
            _view.Begin(Host, rules.Width, rules.Height, rules.SeedFor(Level.Id));
            Repaint();
            Audio.Sfx("rotate_a", .55f);
        }
    }
}
