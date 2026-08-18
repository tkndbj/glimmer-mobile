using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Press-to-squash button with sound, guard against double fire and a disabled look.</summary>
    public sealed class Btn : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        Action _click;
        Vector3 _home;
        bool _down, _silent, _interactable = true;
        Image _self;
        public Image Icon;

        /// <summary>
        /// The one sound this button makes, or null for a control that has its own voice —
        /// the streak rung that plays <c>unlock</c>, the companion the player pokes, the
        /// nav tab you are already standing on.
        ///
        /// <para>
        /// <b>One sound, on the way down.</b> A tap used to make two, a <c>press</c> as the
        /// finger landed and a <c>click</c> as it lifted, and the pair was never heard as
        /// tactile depth — it was heard as a button that stutters. Down rather than up
        /// because that is the frame the squash starts on, so the sound and the movement
        /// are the same event; firing on release put the audio a reaction-time behind the
        /// animation and gave the whole interface a lag it did not have.
        /// </para>
        /// </summary>
        public string ClickSfx = "click";

        public float PressScale = .93f;

        /// <summary>
        /// The caption, when this button has one. Held rather than looked up by name, so a
        /// repaint costs nothing and renaming the child cannot silently break the layout.
        /// </summary>
        public Text Label;

        /// <summary>How wide the caption and any glyph beside it may be, together.</summary>
        public float LabelWidth;

        /// <summary>
        /// Changes the caption, re-centring the glyph beside it.
        ///
        /// <para>
        /// The pair is what makes this worth a method rather than an assignment: a button
        /// carrying a glyph has to be re-measured whenever its caption changes, and a
        /// caller that has to remember a second call is a caller that will eventually
        /// forget one — these captions are countdowns, so the failure would be a glyph that
        /// drifts off centre as the clock ticks. Returns whether anything actually changed,
        /// so a per-frame paint can skip the mesh rebuild on the frames that read the same.
        /// </para>
        /// </summary>
        public bool SetCaption(string caption)
        {
            if (Label == null) return false;
            if (string.Equals(Label.text, caption, StringComparison.Ordinal)) return false;

            Label.text = caption;
            UIKit.FitLabel(this);
            return true;
        }

        public bool Interactable
        {
            get => _interactable;
            set
            {
                _interactable = value;
                if (_self == null) _self = GetComponent<Image>();
                var c = value ? Color.white : new Color(.62f, .66f, .70f, .85f);
                if (_self) _self.color = c;
                if (Icon) Icon.color = value ? Pal.Cream : new Color(1, 1, 1, .45f);
            }
        }

        public void Setup(Action onClick, bool silent = false)
        {
            _click = onClick;
            _silent = silent;
            _home = transform.localScale;
            _self = GetComponent<Image>();
        }

        void OnEnable() { if (_home == Vector3.zero) _home = transform.localScale; }

        public void OnPointerDown(PointerEventData e)
        {
            if (!_interactable) return;
            _down = true;
            if (PressScale < .999f) Tween.Scale(transform, _home * PressScale, .09f, Ease.OutQuad);
            if (!_silent && !string.IsNullOrEmpty(ClickSfx)) Audio.SfxVaried(ClickSfx, .7f, .04f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_down) return;
            _down = false;
            Tween.Scale(transform, _home, .3f, Ease.OutBack);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!_interactable) return;
            // Silent: OnPointerDown already spoke for this tap. See ClickSfx.
            _click?.Invoke();
        }

        /// <summary>Re-read the resting scale, e.g. after an intro animation.</summary>
        public void Rehome() => _home = transform.localScale;
    }

    /// <summary>Slow drifting motes of light. Pure eye candy, very cheap.</summary>
    public sealed class Fireflies : MonoBehaviour
    {
        struct Mote
        {
            public RectTransform rt;
            public Vector2 home;
            public float phase, speedX, speedY, ampX, ampY, twinkle, size;
        }

        Mote[] _motes;
        RectTransform _rt;

        public static Fireflies Spawn(Transform parent, int count = 22, Color? tint = null,
                                      float minSize = 6f, float maxSize = 20f)
        {
            var rt = UIKit.Node("Fireflies", parent);
            var f = rt.gameObject.AddComponent<Fireflies>();
            f.Build(count, tint ?? new Color(1f, .95f, .72f), minSize, maxSize);
            return f;
        }

        void Build(int count, Color tint, float minSize, float maxSize)
        {
            _rt = (RectTransform)transform;
            _motes = new Mote[count];
            for (int i = 0; i < count; i++)
            {
                float size = UnityEngine.Random.Range(minSize, maxSize);
                var img = UIKit.Img("m" + i, transform, Art.Glow(64, 1.7f),
                                    Pal.A(tint, UnityEngine.Random.Range(.25f, .7f)),
                                    Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
                var mrt = (RectTransform)img.transform;
                var home = new Vector2(UnityEngine.Random.Range(-540f, 540f),
                                       UnityEngine.Random.Range(-980f, 980f));
                mrt.anchoredPosition = home;
                _motes[i] = new Mote
                {
                    rt = mrt,
                    home = home,
                    phase = UnityEngine.Random.Range(0f, 20f),
                    speedX = UnityEngine.Random.Range(.09f, .28f),
                    speedY = UnityEngine.Random.Range(.05f, .18f),
                    ampX = UnityEngine.Random.Range(30f, 130f),
                    ampY = UnityEngine.Random.Range(40f, 200f),
                    twinkle = UnityEngine.Random.Range(.5f, 1.6f),
                    size = size
                };
            }
        }

        void Update()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < _motes.Length; i++)
            {
                ref var m = ref _motes[i];
                if (m.rt == null) continue;
                m.rt.anchoredPosition = m.home + new Vector2(
                    Mathf.Sin((t + m.phase) * m.speedX * 2.1f) * m.ampX,
                    Mathf.Cos((t + m.phase) * m.speedY * 1.7f) * m.ampY);
                var img = m.rt.GetComponent<Image>();
                if (img)
                {
                    var c = img.color;
                    c.a = .18f + .46f * (.5f + .5f * Mathf.Sin((t + m.phase) * m.twinkle * 2.4f));
                    img.color = c;
                }
                float s = 1f + .16f * Mathf.Sin((t * 1.3f + m.phase));
                m.rt.localScale = new Vector3(s, s, 1f);
            }
        }
    }

    /// <summary>Layer that slides a little against device tilt / cursor, for depth.</summary>
    public sealed class Parallax : MonoBehaviour
    {
        RectTransform _rt;
        Vector2 _home, _vel, _target;
        public float Strength = 26f;

        public static Parallax Attach(RectTransform rt, float strength)
        {
            var p = rt.gameObject.AddComponent<Parallax>();
            p._rt = rt;
            p._home = rt.anchoredPosition;
            p.Strength = strength;
            return p;
        }

        void Update()
        {
            Vector2 tilt;
#if UNITY_ANDROID || UNITY_IOS
            var a = Input.acceleration;
            tilt = new Vector2(Mathf.Clamp(a.x, -.6f, .6f) / .6f, Mathf.Clamp(a.y + .5f, -.6f, .6f) / .6f);
#else
            var m = Input.mousePosition;
            tilt = new Vector2(m.x / Mathf.Max(1, Screen.width) - .5f, m.y / Mathf.Max(1, Screen.height) - .5f) * 2f;
#endif
            _target = _home - tilt * Strength;
            _rt.anchoredPosition = Vector2.SmoothDamp(_rt.anchoredPosition, _target, ref _vel, .35f,
                                                      Mathf.Infinity, Time.unscaledDeltaTime);
        }
    }

    /// <summary>One-shot particle spray built from pooled Images.</summary>
    public static class Burst
    {
        public static void Sparks(Transform parent, Vector2 at, Color colour, int count = 14,
                                  float power = 190f, float size = 26f, float life = .62f)
        {
            var host = UIKit.Box("Burst", parent, Vector2.zero, new Vector2(.5f, .5f), at);
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-.22f, .22f);
                float spd = power * UnityEngine.Random.Range(.55f, 1.3f);
                float sz = size * UnityEngine.Random.Range(.55f, 1.35f);
                var img = UIKit.Img("s", host, Art.Spark(64), Pal.A(Pal.Lift(colour, .35f), 1f),
                                    Vector2.one * sz, new Vector2(.5f, .5f), Vector2.zero);
                var rt = (RectTransform)img.transform;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float spin = UnityEngine.Random.Range(-260f, 260f);
                float dur = life * UnityEngine.Random.Range(.75f, 1.25f);
                Tween.Run(dur, Ease.OutCubic, t =>
                {
                    if (!rt) return;
                    rt.anchoredPosition = dir * spd * t;
                    rt.localScale = Vector3.one * Mathf.Lerp(1.15f, .05f, t * t);
                    rt.localRotation = Quaternion.Euler(0, 0, spin * t);
                    var c = img.color; c.a = 1f - t * t; img.color = c;
                }, img);
            }
            // bright core flash
            var core = UIKit.Img("core", host, Art.Glow(128, 2.4f), Pal.A(Pal.Lift(colour, .5f), .9f),
                                 Vector2.one * size * 5f, new Vector2(.5f, .5f), Vector2.zero);
            Tween.Run(.42f, Ease.OutQuint, t =>
            {
                if (!core) return;
                core.transform.localScale = Vector3.one * Mathf.Lerp(.35f, 1.6f, t);
                var c = core.color; c.a = .9f * (1f - t); core.color = c;
            }, core);

            Tween.After(life * 1.6f + .3f, () => { if (host) UnityEngine.Object.Destroy(host.gameObject); });
        }

        /// <summary>Falling confetti for the victory moment.</summary>
        public static void Confetti(Transform parent, int count = 70)
        {
            var host = UIKit.Node("Confetti", parent);
            Color[] palette = { Pal.Gold, Pal.Rose, Pal.Mint, Pal.Azure, Pal.Bloom, Pal.Cream };
            for (int i = 0; i < count; i++)
            {
                var col = palette[UnityEngine.Random.Range(0, palette.Length)];
                float w = UnityEngine.Random.Range(14f, 30f);
                var img = UIKit.Img("c", host, Art.Round(6), col,
                                    new Vector2(w, w * UnityEngine.Random.Range(.4f, 1.1f)),
                                    new Vector2(.5f, .5f),
                                    new Vector2(UnityEngine.Random.Range(-560f, 560f), UnityEngine.Random.Range(1000f, 1500f)));
                var rt = (RectTransform)img.transform;
                var start = rt.anchoredPosition;
                float fall = UnityEngine.Random.Range(1700f, 2500f);
                float sway = UnityEngine.Random.Range(40f, 150f);
                float swayHz = UnityEngine.Random.Range(1.2f, 3.4f);
                float spin = UnityEngine.Random.Range(-620f, 620f);
                float dur = UnityEngine.Random.Range(1.9f, 3.4f);
                float delay = UnityEngine.Random.Range(0f, .9f);
                Tween.Run(dur, Ease.Linear, t =>
                {
                    if (!rt) return;
                    rt.anchoredPosition = start + new Vector2(Mathf.Sin(t * swayHz * 6.28f) * sway, -fall * t);
                    rt.localRotation = Quaternion.Euler(0, 0, spin * t);
                    var c = img.color; c.a = t > .82f ? (1f - t) / .18f : 1f; img.color = c;
                }, img).Delay(delay);
            }
            Tween.After(4.6f, () => { if (host) UnityEngine.Object.Destroy(host.gameObject); });
        }
    }

    /// <summary>Diagonal shine that sweeps a button now and then.</summary>
    public sealed class Sheen : MonoBehaviour
    {
        public static void Attach(RectTransform target, float period = 3.4f, float width = .22f)
        {
            var mask = target.gameObject.GetComponent<Mask>();
            var holder = UIKit.Node("Sheen", target);
            holder.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
            var m = holder.gameObject.AddComponent<Mask>();
            m.showMaskGraphic = false;
            var img = holder.GetComponent<Image>();
            img.sprite = target.GetComponent<Image>() ? target.GetComponent<Image>().sprite : Art.Round(24);
            img.type = Image.Type.Sliced;
            img.color = new Color(1, 1, 1, 0.004f);

            // Decoration, and it sits on top of whatever it decorates. On a button that
            // costs nothing — the press bubbles up to the Btn on the parent — but on
            // anything whose tap target is a *sibling* below it, a raycasting sheen
            // swallows the tap and the element silently stops working.
            img.raycastTarget = false;

            float w = target.sizeDelta.x;
            var bar = UIKit.Img("bar", holder, Art.Glow(64, 1.2f), new Color(1, 1, 1, .5f),
                                new Vector2(Mathf.Max(60f, w * width), target.sizeDelta.y * 2.4f),
                                new Vector2(.5f, .5f), new Vector2(-w, 0));
            bar.transform.localRotation = Quaternion.Euler(0, 0, 18f);

            var brt = (RectTransform)bar.transform;
            Tween.Run(period, Ease.Linear, t =>
            {
                if (!brt) return;
                float k = Mathf.Repeat(t, 1f);
                float travel = Mathf.Clamp01((k - .55f) / .28f);
                brt.anchoredPosition = new Vector2(Mathf.Lerp(-w * .9f, w * .9f, travel), 0f);
                var c = bar.color;
                c.a = travel > 0f && travel < 1f ? .45f * Mathf.Sin(travel * Mathf.PI) : 0f;
                bar.color = c;
            }, holder).Loop(-1, false);
        }
    }

    /// <summary>Plays a sprite sheet folder on an Image.</summary>
    public sealed class Flipbook : MonoBehaviour
    {
        Sprite[] _frames;
        Image _img;
        float _fps = 24f, _t;
        bool _loop = true, _done;
        public Action OnFinished;

        public static Flipbook Attach(Image img, string folder, float fps = 24f, bool loop = true)
        {
            var f = img.gameObject.AddComponent<Flipbook>();
            f._img = img;
            f._frames = Art.Frames(folder);
            f._fps = fps;
            f._loop = loop;
            if (f._frames != null && f._frames.Length > 0) img.sprite = f._frames[0];
            return f;
        }

        public float Duration => _frames == null ? 0f : _frames.Length / _fps;
        public float Offset { get; set; }

        void Update()
        {
            if (_frames == null || _frames.Length == 0 || _done) return;
            _t += Time.unscaledDeltaTime * _fps;
            int i = Mathf.FloorToInt(_t + Offset);
            if (!_loop && i >= _frames.Length)
            {
                _img.sprite = _frames[_frames.Length - 1];
                _done = true;
                OnFinished?.Invoke();
                return;
            }
            _img.sprite = _frames[((i % _frames.Length) + _frames.Length) % _frames.Length];
        }
    }
}
