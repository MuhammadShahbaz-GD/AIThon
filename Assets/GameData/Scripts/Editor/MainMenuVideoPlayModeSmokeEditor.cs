#if UNITY_EDITOR
using System;
using System.Globalization;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KickTheBuddy.Editor
{
    /// <summary>Play Mode regression for video completion, delayed Play, and saved-level loading.</summary>
    [InitializeOnLoad]
    public static class MainMenuVideoPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/MainMenu.unity";
        private const string ActiveKey = "KickTheBuddy.VideoMenuSmoke.Active";
        private const string BatchKey = "KickTheBuddy.VideoMenuSmoke.Batch";
        private const string StageKey = "KickTheBuddy.VideoMenuSmoke.Stage";
        private const string StartKey = "KickTheBuddy.VideoMenuSmoke.Start";
        private const string ExpectedLevelKey = "KickTheBuddy.VideoMenuSmoke.ExpectedLevel";
        private const string SuccessKey = "KickTheBuddy.VideoMenuSmoke.Success";
        private const string FailureKey = "KickTheBuddy.VideoMenuSmoke.Failure";

        static MainMenuVideoPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Gameplay/UI/Validate Video Main Menu In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the video menu smoke.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetString(StartKey, string.Empty);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(FailureKey, string.Empty);
            EditorSceneManager.OpenScene(ScenePath);
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

            try
            {
                if (SessionState.GetInt(StageKey, 0) == 0) ValidateMenuSequence();
                else ValidateSavedLevelStarted();
            }
            catch (Exception exception)
            {
                SessionState.SetString(FailureKey, exception.Message);
                SessionState.SetBool(SuccessKey, false);
                EditorApplication.isPlaying = false;
            }
        }

        private static void ValidateMenuSequence()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu") return;
            string startValue = SessionState.GetString(StartKey, string.Empty);
            if (string.IsNullOrEmpty(startValue))
            {
                SessionState.SetString(StartKey,
                    EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            double start = double.Parse(startValue, CultureInfo.InvariantCulture);
            double elapsed = EditorApplication.timeSinceStartup - start;
            MainMenuVideoSequenceController sequence =
                UnityEngine.Object.FindObjectOfType<MainMenuVideoSequenceController>(true);
            Button play = GameObject.Find("Play Saved Level")?.GetComponent<Button>();
            GameBootstrapper root = GameBootstrapper.Instance;
            if (sequence == null || play == null || root == null) return;
            if (elapsed < 2d && (sequence.IsReadyToPlay || play.interactable))
                throw new InvalidOperationException("Play became interactable before the intro video finished.");
            if (!sequence.IsReadyToPlay)
            {
                if (elapsed > 18d) throw new InvalidOperationException("Video did not reveal the static Play screen.");
                return;
            }
            if (elapsed < 7.5d)
                throw new InvalidOperationException($"Static Play screen appeared too early at {elapsed:F2}s.");

            int lastUnlocked = Mathf.Min(root.Saves.Data.highestUnlockedLevel,
                Mathf.Max(0, root.Levels.Count - 1));
            SessionState.SetInt(ExpectedLevelKey,
                Mathf.Clamp(root.Saves.Data.selectedLevel, 0, lastUnlocked));
            SessionState.SetInt(StageKey, 1);
            play.onClick.Invoke();
        }

        private static void ValidateSavedLevelStarted()
        {
            if (SceneManager.GetActiveScene().name != "RagdollSandbox") return;
            GameBootstrapper root = GameBootstrapper.Instance;
            if (root == null || root.Gameplay.State != GameplayState.Playing) return;
            int expected = SessionState.GetInt(ExpectedLevelKey, -1);
            if (root.Levels.CurrentLevelIndex != expected)
                throw new InvalidOperationException(
                    $"Play loaded level {root.Levels.CurrentLevelIndex}, expected saved level {expected}.");
            Debug.Log($"VIDEO_MAIN_MENU_PLAYMODE_OK: delayedPlay=true, savedLevel={expected}, gameplayStarted=true.");
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool(BatchKey, false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.OpenScene(ScenePath);
            if (!success) Debug.LogError("VIDEO_MAIN_MENU_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
