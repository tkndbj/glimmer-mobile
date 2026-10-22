using System.Collections;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    public sealed partial class SiegeView
    {
        enum IntroductionBeat { Waiting, Rift, Arrived, Speaking, Summoning }

        /// <summary>
        /// A scene actor, deliberately separate from combat bosses: it summons the authored
        /// waves and retreats when they are beaten. No extra wave, health bar or combat lesson.
        /// Every widget is owned by this deal's plate and rebuilt on restart. Persistent motes
        /// and shock rings are sampled in place, with no allocations or random draws per frame.
        /// </summary>
        sealed class Introduction
        {
            public RectTransform Root, Actor, Bubble;
            public Image Veil, Body, Shadow, Aura, Tail;
            public Image[] Motes, Rings;
            public CanvasGroup ActorAlpha, BubbleAlpha;
            public GraphicRaycaster Raycaster;
            public Text Speech;
            public Pit Portal;
            public Sprite[] Idle, Cast;
            public Vector2 At, Stand;
            public float Tall, Age, CastAt = -10f, SaidAt = -10f, PulseAt = -10f;
            public float PulseStrength;
            public float Retreat = -1f;
            public IntroductionBeat Beat;
            public bool Released;
        }

        Introduction _introduction;
        const int IntroMotes = 24, IntroRings = 3;
        static readonly Color IntroLight = new Color(.65f, .38f, 1f);

        void ComposeIntroduction()
        {
            if (_introduction != null && _introduction.Root)
                _introduction.Root.gameObject.SetActive(false);
            _introduction = null;
            if (_layout == null || !_layout.Introduction) return;

            var scene = new Introduction();
            _introduction = scene;
            scene.Root = Layer("Firstwatch introduction");
            // Isolate the continuously moving scene from the gem field's canvas rebuilds.
            scene.Root.gameObject.AddComponent<Canvas>().overrideSorting = false;
            scene.Raycaster = scene.Root.gameObject.AddComponent<GraphicRaycaster>();
            scene.Root.gameObject.AddComponent<RectMask2D>();

            scene.Veil = UIKit.Fill(scene.Root, Color.clear, "Input shield");
            scene.Veil.raycastTarget = true;
            var advance = scene.Veil.gameObject.AddComponent<Btn>();
            advance.PressScale = 1f;
            advance.Setup(() =>
            {
                // A tap may shorten the spoken opening, never skip the entrance or start a
                // run behind a pause/lesson. Keep the summoning gesture before control returns.
                if (Live && scene.Age >= SiegeIntroduction.SpeakAt
                    && scene.Age < SiegeIntroduction.SummonAt)
                    scene.Age = SiegeIntroduction.SummonAt;
            }, silent: true);

            // Leave room for the portal's luminous rim at the top of the battlefield. Raiders
            // now fan out from here and traverse nearly the whole hill before reaching the line.
            scene.At = new Vector2(0f, _hillTop - Cell * 1.05f);
            scene.Tall = Mathf.Min(Cell * 2.5f, (_hillTop - _hillFoot) * .58f);
            scene.Stand = scene.At + new Vector2(-Cell * 1.8f, -scene.Tall * .48f);
            scene.Portal = Raise(scene.At, scene.Root, scene.Root, Cell * 4.5f, Cell * 1.6f);

            scene.Actor = UIKit.Box("Warlord", scene.Root, Vector2.one * scene.Tall,
                                    new Vector2(.5f, .5f), scene.Stand);
            scene.ActorAlpha = UIKit.Group(scene.Actor);
            scene.ActorAlpha.alpha = 0f;
            scene.Shadow = UIKit.Img("Shadow", scene.Actor, Art.Glow(64, ShadowFalloff),
                                     new Color(0f, 0f, 0f, .6f),
                                     new Vector2(scene.Tall * .85f, scene.Tall * .27f));
            scene.Shadow.rectTransform.anchoredPosition = new Vector2(0f, -scene.Tall * .23f);
            scene.Aura = Additive.Lit(UIKit.Img("Spell light", scene.Actor, Art.Glow(128, 2f),
                                              Color.clear, Vector2.one * scene.Tall * 1.7f));
            scene.Idle = Reel("boss");
            scene.Cast = Reel("boss_cast");
            // Missing art must remain transparent, never an Image's default white rectangle.
            scene.Body = UIKit.Img("Body", scene.Actor,
                                   scene.Idle != null && scene.Idle.Length > 0 ? scene.Idle[0] : null,
                                   Color.white, new Vector2(Frame(scene.Idle, scene.Tall), scene.Tall));
            scene.Body.enabled = scene.Body.sprite != null;
            scene.Body.preserveAspect = true;
            scene.Portal.Over.SetAsLastSibling();

            scene.Motes = new Image[IntroMotes];
            for (int i = 0; i < scene.Motes.Length; i++)
            {
                float size = Cell * (.045f + (i % 4) * .012f);
                scene.Motes[i] = Additive.Lit(UIKit.Img("Rift ember", scene.Root, Art.Spark(64),
                                                      Color.clear, Vector2.one * size));
            }
            scene.Rings = new Image[IntroRings];
            for (int i = 0; i < scene.Rings.Length; i++)
            {
                var ring = Additive.Lit(UIKit.Img("Summoning shock", scene.Root, Art.Ring(128, 4f),
                                                 Color.clear, Vector2.one * Cell));
                ring.rectTransform.anchoredPosition = scene.At;
                scene.Rings[i] = ring;
            }

            BuildIntroductionBubble(scene);
            Busy = true;
        }

        void BuildIntroductionBubble(Introduction scene)
        {
            // Beside the actor, so a short phone never puts the sentence over its face.
            float wide = Mathf.Min(Span.x * .65f, Cell * 4.45f);
            float tall = Cell * 1.60f;
            float x = Mathf.Min(Cell * 1.75f, (Span.x - wide) * .5f - Cell * .12f);
            float y = Mathf.Min(_hillTop - tall * .5f - Cell * .12f,
                                scene.Stand.y - scene.Tall * .05f);
            scene.Bubble = UIKit.Box("Boss speech", scene.Root, new Vector2(wide, tall),
                                     new Vector2(.5f, .5f), new Vector2(x, y));
            scene.BubbleAlpha = UIKit.Group(scene.Bubble);
            scene.BubbleAlpha.alpha = 0f;
            var shade = UIKit.Img("Shadow", scene.Bubble, Art.Round(22),
                                  new Color(0f, 0f, 0f, .45f), new Vector2(wide + 10f, tall + 10f));
            shade.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .06f);
            UIKit.Img("Rim", scene.Bubble, Art.Round(22), Pal.A(IntroLight, .95f),
                       new Vector2(wide, tall));
            UIKit.Img("Plate", scene.Bubble, Art.Round(20), new Color(.065f, .04f, .13f, .98f),
                       new Vector2(wide - 5f, tall - 5f));
            scene.Tail = UIKit.Img("Tail", scene.Bubble, Art.Pixel, IntroLight,
                                   Vector2.one * Cell * .17f);
            scene.Tail.rectTransform.anchoredPosition = new Vector2(-wide * .5f, -tall * .15f);
            scene.Tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var name = UIKit.Label("Name", scene.Bubble, Loc.Get("mode.siege.boss"),
                                    Mathf.RoundToInt(Cell * .21f), Pal.Gold,
                                    TextAnchor.MiddleLeft, new Vector2(wide - Cell * .42f, Cell * .3f));
            name.rectTransform.anchoredPosition = new Vector2(0f, tall * .5f - Cell * .25f);
            scene.Speech = UIKit.Label("Speech", scene.Bubble, string.Empty,
                                       Mathf.RoundToInt(Cell * .29f), Pal.Cream,
                                       TextAnchor.MiddleCenter,
                                       new Vector2(wide - Cell * .45f, tall - Cell * .55f), wrap: true);
            scene.Speech.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .14f);
            scene.Speech.resizeTextForBestFit = true;
            scene.Speech.resizeTextMinSize = Mathf.RoundToInt(Cell * .23f);
            scene.Speech.resizeTextMaxSize = Mathf.RoundToInt(Cell * .29f);
        }

        bool TickIntroduction(float seconds)
        {
            var scene = _introduction;
            if (scene == null) return false;
            if (!Live) return !scene.Released;

            float dt = Mathf.Clamp(seconds, 0f, .1f);
            scene.Age += dt;
            if (scene.Beat == IntroductionBeat.Waiting && scene.Age >= SiegeIntroduction.RiftAt)
            {
                scene.Beat = IntroductionBeat.Rift;
                PulseIntroduction(scene, .65f);
                Audio.Sfx("charge", .72f, .55f);
                Audio.Sfx("whoosh", .48f, .6f);
            }
            if (scene.Beat == IntroductionBeat.Rift && scene.Age >= SiegeIntroduction.LandAt)
            {
                scene.Beat = IntroductionBeat.Arrived;
                PulseIntroduction(scene, 1f);
                ShakeBoard(Cell * .10f);
                Audio.Sfx("boss", .85f, .85f);
                Audio.Sfx("boom", .6f, .7f);
            }
            if (scene.Beat == IntroductionBeat.Arrived && scene.Age >= SiegeIntroduction.SpeakAt)
            {
                scene.Beat = IntroductionBeat.Speaking;
                SayIntroduction(scene, "mode.siege.intro.claim");
            }
            if (scene.Beat == IntroductionBeat.Speaking && scene.Age >= SiegeIntroduction.SummonAt)
            {
                scene.Beat = IntroductionBeat.Summoning;
                SayIntroduction(scene, "mode.siege.intro.summon");
                CastIntroduction(scene);
            }
            if (!scene.Released && scene.Age >= SiegeIntroduction.PlayAt)
            {
                scene.Released = true;
                Busy = false;
                scene.Veil.raycastTarget = false;
                scene.Raycaster.enabled = false;
                PulseIntroduction(scene, .8f);
                Audio.Sfx("whoosh", .48f, 1.1f);
                Changed?.Invoke();
            }
            DrawIntroduction(scene);
            return !scene.Released;
        }

        void SayIntroduction(Introduction scene, string key)
        {
            scene.Speech.text = Loc.Get(key);
            scene.SaidAt = scene.Age;
        }

        void CastIntroduction(Introduction scene)
        {
            scene.CastAt = scene.Age;
            PulseIntroduction(scene, 1f);
            Audio.Sfx("roar", .6f, .95f);
            Audio.Sfx("whoosh", .55f, .78f);
        }

        static void PulseIntroduction(Introduction scene, float strength)
        {
            scene.PulseAt = scene.Age;
            // The aura and rings share one bounded gesture; they never stack tweens.
            scene.PulseStrength = Mathf.Clamp01(strength);
        }

        bool IntroductionWave(int wave)
        {
            var scene = _introduction;
            if (scene == null) return false;
            if (wave > 0)
            {
                SayIntroduction(scene, "mode.siege.intro.challenge");
                CastIntroduction(scene);
            }
            return true;
        }

        Vector2 IntroductionPosition(SiegeRaider raider, Vector2 ordinary)
        {
            var scene = _introduction;
            if (scene == null || raider.Boss) return ordinary;
            float march = Mathf.Clamp01(raider.March);
            float fan = Ease.OutCubic(Mathf.Clamp01(march / .30f));
            return new Vector2(ordinary.x * fan, Mathf.Lerp(scene.At.y - Cell * .18f, _hillFoot, march));
        }

        void DrawIntroduction(Introduction scene)
        {
            float age = scene.Age;
            float emerge = Mathf.Clamp01((age - SiegeIntroduction.EmergeAt)
                                         / (SiegeIntroduction.LandAt - SiegeIntroduction.EmergeAt));
            float close = scene.Retreat < 0f ? 0f
                        : Mathf.Clamp01((scene.Retreat - 1.55f) / 1.15f);
            float portal = Ease.OutCubic(Mathf.Clamp01((age - SiegeIntroduction.RiftAt) / .7f));
            float shut = scene.Retreat < 0f ? 0f
                       : Ease.InCubic(Mathf.Clamp01((scene.Retreat - 2.75f) / .5f));
            float light = portal * (1f - shut);
            float pulse = Mathf.Clamp01(1f - (age - scene.PulseAt) / .8f) * scene.PulseStrength;
            float size = portal * (1f - shut) * (1f + pulse * .17f + Mathf.Sin(age * 3.4f) * .025f);
            var pit = scene.Portal;
            pit.Under.localScale = pit.Over.localScale = Vector3.one * size;
            Turn(pit.DiscBack, -age * 105f, light * .82f);
            Turn(pit.DiscFront, -age * 105f, light * .82f);
            Turn(pit.ArmsBack, age * 155f, light * .85f);
            Turn(pit.ArmsFront, age * 155f, light * .85f);
            Turn(pit.Inner, age * 52f, light * .65f);
            if (pit.Ring) pit.Ring.color = Pal.A(Color.white, light * (.75f + pulse * .25f));
            if (pit.Veil) pit.Veil.color = Pal.A(WellDark, light * .48f);

            float reveal = Ease.OutCubic(emerge);
            float returning = Ease.InOutCubic(close);
            var standing = Vector2.Lerp(scene.At, scene.Stand, reveal)
                         + Vector2.up * (Mathf.Sin(age * 2.4f) * Cell * .025f * reveal);
            scene.Actor.anchoredPosition = Vector2.Lerp(standing, scene.At, returning);
            // Travel visibly into the portal before disappearing. Its rim stays open until
            // the actor has reached its centre; closing then precedes the victory celebration.
            scene.ActorAlpha.alpha = emerge * (1f - Mathf.Clamp01((close - .6f) / .4f));
            float actorScale = Mathf.Lerp(.48f, 1f, reveal) * (1f - returning * .8f);
            float cast = Mathf.Clamp01(1f - (age - scene.CastAt) / 1.25f);
            scene.Actor.localScale = new Vector3(actorScale * (1f + cast * .035f),
                                                actorScale * (1f - cast * .025f), 1f);
            scene.Aura.color = Pal.A(IntroLight, pulse * .36f + cast * .16f);
            var frames = cast > 0f && scene.Cast != null && scene.Cast.Length > 0
                       ? scene.Cast : scene.Idle;
            if (frames != null && frames.Length > 0)
            {
                int frame = cast > 0f ? Mathf.Min(frames.Length - 1, (int)((age - scene.CastAt) * BossFps))
                                     : (int)(age * BossFps) % frames.Length;
                var sprite = frames[Mathf.Max(0, frame)];
                if (scene.Body.sprite != sprite) scene.Body.sprite = sprite;
            }

            float spoken = age - scene.SaidAt;
            float speech = Mathf.Clamp01(spoken / .16f) * Mathf.Clamp01((3.5f - spoken) / .3f);
            if (scene.Retreat >= 0f) speech *= 1f - Mathf.Clamp01((scene.Retreat - 1.7f) / .25f);
            scene.BubbleAlpha.alpha = speech;
            scene.Bubble.localScale = Vector3.one * Mathf.Lerp(.9f, 1f, Ease.OutBack(Mathf.Clamp01(spoken / .25f)));

            for (int i = 0; i < scene.Motes.Length; i++)
            {
                float t = Mathf.Repeat(age * .42f + i * .618034f, 1f);
                float angle = i * 2.399963f + age * 2.6f + t * 2f;
                float radius = Cell * Mathf.Lerp(2.15f, .35f, t);
                var mote = scene.Motes[i];
                mote.rectTransform.anchoredPosition = scene.At
                    + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * .37f + t * Cell * .65f);
                mote.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
                mote.color = Pal.A(i % 3 == 0 ? Pal.Gold : IntroLight,
                                   light * Mathf.Sin(t * Mathf.PI) * .85f);
            }
            for (int i = 0; i < scene.Rings.Length; i++)
            {
                float t = (age - scene.PulseAt - i * .08f) / .8f;
                float a = t >= 0f && t < 1f ? (1f - t) * light * .8f : 0f;
                float r = Mathf.Lerp(.8f, 6.6f, Ease.OutCubic(Mathf.Clamp01(t)));
                scene.Rings[i].rectTransform.localScale = new Vector3(r, r * .55f, 1f);
                scene.Rings[i].color = Pal.A(i == 0 ? Pal.Gold : IntroLight, a);
            }
        }

        IEnumerator IntroductionTriumph()
        {
            var scene = _introduction;
            if (scene != null)
            {
                // Combat has ended. The scene is bounded and belongs to this deal's coroutine
                // lifetime, so a restart or departure cannot leave an old director running.
                scene.Retreat = 0f;
                SayIntroduction(scene, "mode.siege.intro.retreat");
                PulseIntroduction(scene, 1f);
                Audio.Sfx("roar", .55f, .72f);
                while (scene.Retreat < SiegeIntroduction.RetreatFor && scene.Root)
                {
                    // Like the run's latch, a modal or suspended app must hold the scene.
                    if (Time.timeScale > 0f && !Flow.HasModal)
                    {
                        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, .1f);
                        scene.Retreat += dt;
                        scene.Age += dt;
                        DrawIntroduction(scene);
                    }
                    yield return null;
                }
                if (scene.Root) scene.Root.gameObject.SetActive(false);
                Audio.Sfx("whoosh", .65f, .7f);
            }
            yield return base.Triumph();
        }
    }
}
