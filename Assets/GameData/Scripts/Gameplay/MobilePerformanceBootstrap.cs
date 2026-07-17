using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Applies deterministic frame pacing before gameplay starts on mobile players.</summary>
    public static class MobilePerformanceBootstrap
    {
        private const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
#if UNITY_ANDROID || UNITY_IOS
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
#endif
        }
    }
}
