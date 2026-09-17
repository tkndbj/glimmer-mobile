using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Daily Challenges: the room behind the hub's banner, and at the moment an empty one.
    ///
    /// <para>
    /// <b>It is deliberately a screen with nothing in it rather than a panel that says "soon".</b>
    /// The banner on the hub is a door, and a door that opens onto a modal saying the room is not
    /// built is a door the player learns not to press. What is here is the room's own chrome —
    /// the ground, the heading, the way back and the nav bar — so the thing that has to be
    /// decided later is <em>what stands in it</em>, and everything around that is already
    /// settled and already looks like the rest of the game.
    /// </para>
    /// <para>
    /// <b>This is the one genuine placeholder in the build, and it says so</b> (CLAUDE.md's rule
    /// about placeholder architecture): the sentence in the middle is a real loc key and comes
    /// out when the first challenge does. Nothing else in the game references this screen except
    /// <c>HomeScreen.BuildChallenges</c>, so filling it in is one file.
    /// </para>
    /// <para>
    /// <b>The ground is <c>Scenery.Plain</c> rather than <c>Scenery.Room</c></b>, which is the
    /// rule the tasks page already follows: the room is a painting of somewhere, and a page made
    /// of plates laid across it only ever shows the painting in the gaps between them.
    /// </para>
    /// </summary>
    public sealed class DailyChallengesScreen : View
    {
        public override string Track => "mus_menu";

        const float ChromeSize = 92f;

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            BuildHeader();

            // The empty room. One line, centred between the heading and the nav bar, wrapped
            // and shrinkable because it is a sentence rather than a title and a translation of
            // it is not this project's to choose the length of.
            UIKit.Shrinkable(
                UIKit.Titled("Empty", Safe, Loc.Get("ui.challenges.soon"), 30,
                             new Color(1f, .96f, .88f, .62f), TextAnchor.MiddleCenter,
                             new Vector2(820f, 220f), new Vector2(.5f, .5f),
                             new Vector2(0f, NavBar.Height * .5f), 3f, 0f, wrap: true),
                20);

            NavBar.Build(Content, NavBar.Tab.Home);
        }

        void BuildHeader()
        {
            const float BannerH = 138f;
            float cy = -(22f + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<HomeScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.challenges.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), new Vector2(.5f, 1f),
                                             new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
        }

        /// <summary>The hardware key goes back to the hub, which is the only way in.</summary>
        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }
}
