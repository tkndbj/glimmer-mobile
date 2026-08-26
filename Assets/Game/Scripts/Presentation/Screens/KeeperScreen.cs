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

        protected override void Readouts(System.Collections.Generic.List<Readout> into)
        {
            string score = "0", blooms = "0", tiles = "0";
            if (_view != null) _view.Readouts(out score, out blooms, out tiles);

            into.Add(new Readout(Loc.Get("mode.cap.score"), score));
            into.Add(new Readout(Loc.Get("mode.cap.blooms"), blooms));
            into.Add(new Readout(Loc.Get("mode.cap.tiles"), tiles));
        }

        protected override void Rewind()
        {
            if (_view == null) return;

            var rules = Level.RulesAs<KeeperRules>();
            _view.Begin(Host, rules.Width, rules.Height, rules.Tiles, rules.SeedFor(Level.Id));
            Repaint();
            Audio.Sfx("rotate_a", .55f);
        }
    }
}
