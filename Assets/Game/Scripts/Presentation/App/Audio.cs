using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>Pooled one-shots plus a two-deck crossfading music player.</summary>
    public sealed class Audio : MonoBehaviour
    {
        public static Audio I { get; private set; }

        const int Voices = 10;
        readonly AudioSource[] _voices = new AudioSource[Voices];
        int _next;
        AudioSource _deckA, _deckB;
        bool _onA = true;
        string _currentTrack;

        /// <summary>
        /// How loud the live deck is meant to be.
        ///
        /// <para>
        /// Remembered rather than written out at each of the three places that restore a
        /// volume, because those three have to agree and nothing would say so if they
        /// stopped: <see cref="Music"/> takes a level per call, and both
        /// <see cref="ApplyMusicSetting"/> and <see cref="Duck"/> put the music *back*
        /// afterwards. Spelt as a constant in each, a track faded in at anything but the
        /// default would be silently promoted to the default by the next settings toggle or
        /// fanfare — a track getting louder on its own, which is the one failure a music bed
        /// must not have.
        /// </para>
        /// </summary>
        float _level = DefaultLevel;

        /// <summary>What a track plays at unless a caller says otherwise.</summary>
        public const float DefaultLevel = .42f;

        public static void Boot(Transform parent)
        {
            if (I != null) return;
            var go = new GameObject("~Audio");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<Audio>();
            I.Init();
        }

        void Init()
        {
            for (int i = 0; i < Voices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.ignoreListenerPause = true;
                _voices[i] = s;
            }
            _deckA = MakeDeck("MusicA");
            _deckB = MakeDeck("MusicB");

            // Settings do not know the audio player exists; the player listens.
            GameSettings.Changed += ApplyMusicSetting;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyMusicSetting;
            if (I == this) I = null;
        }

        AudioSource MakeDeck(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.volume = 0f;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            return s;
        }

        static AudioClip Clip(string path) => AssetLibrary.Clip(path);

        // ---------------------------------------------------------------- sfx
        public static void Sfx(string name, float volume = 1f, float pitch = 1f, float delay = 0f)
        {
            if (I == null || !GameSettings.SfxOn) return;
            if (delay > 0f) { Tween.After(delay, () => Sfx(name, volume, pitch)); return; }
            I.PlayOne(name, volume, pitch);
        }

        /// <summary>Same sound with a little random detune, so repeats never grate.</summary>
        public static void SfxVaried(string name, float volume = 1f, float spread = .06f)
            => Sfx(name, volume, 1f + Random.Range(-spread, spread));

        void PlayOne(string name, float volume, float pitch)
        {
            var clip = Clip(AssetManifest.Sfx(name));
            if (clip == null) return;
            var v = _voices[_next];
            // A hush left running on this voice would keep writing its volume down while
            // the new sound plays on it. See Hush.
            Tween.KillChannel(this, HushChannel(_next));
            _next = (_next + 1) % Voices;
            v.Stop();
            v.clip = clip;
            v.volume = Mathf.Clamp01(volume) * .9f;
            v.pitch = Mathf.Clamp(pitch, .25f, 3f);
            v.Play();
        }

        static string HushChannel(int voice) => "hush" + voice;

        /// <summary>
        /// Cut a sound that is still ringing, over a fade short enough not to click.
        ///
        /// <para>
        /// This exists for one shape, and it is a shape the interface has everywhere: a
        /// button makes a noise on pointer *down*, and the thing it opens makes its own a
        /// moment later on pointer *up*. That is two sounds for one tap, and it was
        /// reported as exactly that — "when I click on menu button multiple sounds appear".
        /// </para>
        /// <para>
        /// <b>Why this rather than silencing the button.</b> The idiom already in the
        /// codebase is <c>Btn.ClickSfx = null</c>, and five call sites use it. Applying it
        /// to every control that opens a panel means finding all of them and remembering it
        /// on the next one — a step somebody has to remember, which is the failure mode the
        /// importer hook exists to avoid one folder over. A panel opening is a single place,
        /// so the rule lives there and covers all twenty-five modals and every one added
        /// later.
        /// </para>
        /// <para>
        /// The fade is what makes it safe: a voice stopped mid-waveform steps to zero, and a
        /// step is a click — which would be a worse artefact than the doubled sound it is
        /// removing. 50 ms is below the ear's threshold for a fade and far above the step.
        /// </para>
        /// </summary>
        public static void Hush(string name, float fade = .05f)
        {
            if (I == null) return;
            var clip = Clip(AssetManifest.Sfx(name));
            if (clip == null) return;

            for (int i = 0; i < Voices; i++)
            {
                var v = I._voices[i];
                if (v == null || !v.isPlaying || v.clip != clip) continue;

                var voice = v;
                float from = voice.volume;
                Tween.Run(fade, Ease.Linear, t => { if (voice) voice.volume = from * (1f - t); },
                          I, HushChannel(i))
                     .OnDone(() => { if (voice) voice.Stop(); });
            }
        }

        // -------------------------------------------------------------- music
        /// <summary>Crossfade to a track. Passing the current track is a no-op.</summary>
        public static void Music(string name, float fade = .9f, float volume = DefaultLevel)
        {
            if (I == null) return;
            I.SwapTrack(name, fade, volume);
        }

        void SwapTrack(string name, float fade, float volume)
        {
            if (_currentTrack == name) return;

            // Resolved before anything is written down, and that ordering is the whole of
            // this guard. The track name used to be recorded and the decks swapped *first*,
            // so a clip that did not resolve left the player in a state nothing recovers
            // from: the live deck is the empty one, the outgoing track keeps playing at the
            // volume it had, and every later ApplyMusicSetting and Duck writes to the deck
            // with no clip in it — so turning the music off in settings leaves it playing.
            var clip = Clip(AssetManifest.Music(name));
            if (clip == null) return;

            _currentTrack = name;
            _level = volume;

            var from = _onA ? _deckA : _deckB;
            var to = _onA ? _deckB : _deckA;
            _onA = !_onA;

            to.clip = clip;
            to.volume = 0f;
            to.time = 0f;
            to.Play();
            if (!GameSettings.MusicOn) { to.volume = 0f; from.Stop(); return; }

            float fromStart = from.volume;
            Tween.Run(fade, Ease.InOutSine, t =>
            {
                if (to) to.volume = volume * t;
                if (from) from.volume = fromStart * (1f - t);
            }, this, "music").OnDone(() => { if (from) from.Stop(); });
        }

        public static void ApplyMusicSetting()
        {
            if (I == null) return;
            var live = I._onA ? I._deckA : I._deckB;
            if (GameSettings.MusicOn)
            {
                if (live.clip != null && !live.isPlaying) live.Play();
                float back = I._level;
                Tween.Run(.4f, Ease.OutQuad, t => { if (live) live.volume = back * t; }, I, "music");
            }
            else
            {
                float v = live.volume;
                Tween.Run(.3f, Ease.OutQuad, t => { if (live) live.volume = v * (1f - t); }, I, "music")
                     .OnDone(() => { if (live) live.Pause(); });
            }
        }

        /// <summary>Duck the music briefly so a fanfare can breathe.</summary>
        public static void Duck(float amount = .35f, float hold = 1.4f)
        {
            if (I == null || !GameSettings.MusicOn) return;
            var live = I._onA ? I._deckA : I._deckB;
            float full = I._level;
            Tween.Run(.18f, Ease.OutQuad, t => { if (live) live.volume = Mathf.Lerp(full, full * amount, t); }, I, "duck")
                 .OnDone(() => Tween.Run(.9f, Ease.InOutSine,
                     t => { if (live) live.volume = Mathf.Lerp(full * amount, full, t); }, I, "duck").Delay(hold));
        }
    }
}
