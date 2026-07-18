#if UNITY_EDITOR
using System;
using System.Globalization;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Focused Play Mode verification for the animated splash duration and transition.</summary>
    [InitializeOnLoad]
    public static class AnimatedSplashPlayModeSmokeEditor
    {
        private const string SplashPath = "Assets/GameData/Scene/Splash.unity";
        private const string ActiveKey = "KickTheBuddy.AnimatedSplashSmoke.Active";
        private const string BatchKey = "KickTheBuddy.AnimatedSplashSmoke.Batch";
        private const string StartKey = "KickTheBuddy.AnimatedSplashSmoke.Start";
        private const string SuccessKey = "KickTheBuddy.AnimatedSplashSmoke.Success";
        private const string FailureKey = "KickTheBuddy.AnimatedSplashSmoke.Failure";

        static AnimatedSplashPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Gameplay/UI/Validate Animated Splash In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the animated splash smoke.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(FailureKey, string.Empty);
            SessionState.SetString(StartKey, string.Empty);
            EditorSceneManager.OpenScene(SplashPath);
            Hook();
            EditorApplication.isPlaying = true;
        }

        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Finish(SessionState.GetBool(SuccessKey, false));
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (SceneManager.GetActiveScene().name == "Splash")
            {
                if (string.IsNullOrEmpty(SessionState.GetString(StartKey, string.Empty)))
                    SessionState.SetString(StartKey,
                        EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (SceneManager.GetActiveScene().name != "MainMenu") return;

            try
            {
                double start = double.Parse(SessionState.GetString(StartKey, "0"), CultureInfo.InvariantCulture);
                double elapsed = EditorApplication.timeSinceStartup - start;
                if (elapsed < 3.4d || elapsed > 4.8d)
                    throw new InvalidOperationException($"Splash transition took {elapsed:F2}s; expected the authored 3–4 second presentation window.");
                if (UnityEngine.Object.FindObjectOfType<SceneUIEntranceAnimator>(true) == null)
                    throw new InvalidOperationException("Main Menu did not load with its DOTween UI entrance animator.");
                Debug.Log($"ANIMATED_SPLASH_PLAYMODE_OK: transition={elapsed:F2}s, mainMenuTween=true.");
                SessionState.SetBool(SuccessKey, true);
                EditorApplication.isPlaying = false;
            }
            catch (Exception exception)
            {
                SessionState.SetString(FailureKey, exception.Message);
                SessionState.SetBool(SuccessKey, false);
                EditorApplication.isPlaying = false;
            }
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool(BatchKey, false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.OpenScene(SplashPath);
            if (!success) Debug.LogError("ANIMATED_SPLASH_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
