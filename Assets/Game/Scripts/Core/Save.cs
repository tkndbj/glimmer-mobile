using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>Player progress and settings. Tiny enough for PlayerPrefs.</summary>
    public static class Save
    {
        const string KStars = "gg.stars.";
        const string KBest = "gg.best.";
        const string KMusic = "gg.music";
        const string KSfx = "gg.sfx";
        const string KHaptic = "gg.haptic";
        const string KSeen = "gg.seentutorial";

        public static bool MusicOn { get; private set; } = true;
        public static bool SfxOn { get; private set; } = true;
        public static bool HapticOn { get; private set; } = true;

        static bool _loaded;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            MusicOn = PlayerPrefs.GetInt(KMusic, 1) == 1;
            SfxOn = PlayerPrefs.GetInt(KSfx, 1) == 1;
            HapticOn = PlayerPrefs.GetInt(KHaptic, 1) == 1;
        }

        public static int Stars(int level) => PlayerPrefs.GetInt(KStars + level, 0);
        public static int BestMoves(int level) => PlayerPrefs.GetInt(KBest + level, 0);

        /// <summary>Returns true when this run beat the stored record.</summary>
        public static bool Record(int level, int stars, int moves)
        {
            bool improved = false;
            if (stars > Stars(level)) { PlayerPrefs.SetInt(KStars + level, stars); improved = true; }
            int best = BestMoves(level);
            if (best == 0 || moves < best) { PlayerPrefs.SetInt(KBest + level, moves); improved = true; }
            PlayerPrefs.Save();
            return improved;
        }

        public static bool Unlocked(int level) => level == 0 || Stars(level - 1) > 0;

        public static int TotalStars()
        {
            int n = 0;
            for (int i = 0; i < Levels.Count; i++) n += Stars(i);
            return n;
        }

        public static bool AllCleared()
        {
            for (int i = 0; i < Levels.Count; i++) if (Stars(i) == 0) return false;
            return true;
        }

        public static bool TutorialSeen
        {
            get => PlayerPrefs.GetInt(KSeen, 0) == 1;
            set { PlayerPrefs.SetInt(KSeen, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static void SetMusic(bool on)
        {
            MusicOn = on; PlayerPrefs.SetInt(KMusic, on ? 1 : 0); PlayerPrefs.Save();
            Audio.ApplyMusicSetting();
        }

        public static void SetSfx(bool on)
        {
            SfxOn = on; PlayerPrefs.SetInt(KSfx, on ? 1 : 0); PlayerPrefs.Save();
        }

        public static void SetHaptic(bool on)
        {
            HapticOn = on; PlayerPrefs.SetInt(KHaptic, on ? 1 : 0); PlayerPrefs.Save();
        }

        public static void WipeProgress()
        {
            for (int i = 0; i < Levels.Count; i++)
            {
                PlayerPrefs.DeleteKey(KStars + i);
                PlayerPrefs.DeleteKey(KBest + i);
            }
            PlayerPrefs.Save();
        }
    }

    public static class Haptic
    {
        public static void Tap()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (!Save.HapticOn || Application.isEditor) return;
            Handheld.Vibrate();
#endif
        }
    }
}
