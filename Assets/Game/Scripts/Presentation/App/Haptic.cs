using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>Short confirmation buzz, honouring the player's setting.</summary>
    public static class Haptic
    {
        public static void Tap()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (!GameSettings.HapticsOn || Application.isEditor) return;
            Handheld.Vibrate();
#endif
        }
    }
}
