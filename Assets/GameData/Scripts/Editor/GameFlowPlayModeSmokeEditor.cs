#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Deterministic cross-scene Play Mode smoke using an isolated temporary save file.</summary>
    [InitializeOnLoad]
    public static class GameFlowPlayModeSmokeEditor
    {
        private const string SplashPath = "Assets/GameData/Scene/Splash.unity";
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-flow-smoke.json";
        private const string ActiveKey = "KickTheBuddy.GameFlowSmoke.Active";
        private const string StageKey = "KickTheBuddy.GameFlowSmoke.Stage";
        private const string StartTimeKey = "KickTheBuddy.GameFlowSmoke.StartTime";
        private const string FailureKey = "KickTheBuddy.GameFlowSmoke.Failure";
        private const int StageSplash = 0;
        private const int StageGameplay = 1;
        private const int StageReturnMenu = 2;
        private const int StageSucceeded = 3;
        private const int StageFailed = -1;

        static GameFlowPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) HookUpdate();
        }

        [MenuItem("Tools/Game/Validate Full Game Flow In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunFullGameFlowSmokeBatch() => Run(true);

        private static void Run(bool batchMode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before starting the game-flow smoke.");
            ConfigureSplashSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(StageKey, StageSplash);
            SessionState.SetString(FailureKey, string.Empty);
            SessionState.SetString(StartTimeKey, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetBool(ActiveKey + ".Batch", batchMode);
            EditorSceneManager.OpenScene(SplashPath, OpenSceneMode.Single);
            HookUpdate();
            EditorApplication.isPlaying = true;
        }

        private static void HookUpdate()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) { EditorApplication.update -= Tick; return; }
            int stage = SessionState.GetInt(StageKey, StageSplash);
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (stage == StageSucceeded) Finish(true);
                else if (stage == StageFailed) Finish(false);
                return;
            }

            if (!EditorApplication.isPlaying) return;
            if (ElapsedSeconds() > 40d) { Fail("Timed out while waiting for stage " + stage + "."); return; }

            try
            {
                Scene scene = SceneManager.GetActiveScene();
                GameBootstrapper root = GameBootstrapper.Instance;
                switch (stage)
                {
                    case StageSplash:
                        if (scene.name != "MainMenu") return;
                        RequireRoot(root);
                        if (root.Saves.Data.version != 2) throw new InvalidOperationException("Save schema was not migrated to version 2.");
                        root.Flow.ContinueGame();
                        SessionState.SetInt(StageKey, StageGameplay);
                        break;
                    case StageGameplay:
                        if (scene.name != "RagdollSandbox" || root == null || root.Gameplay.State != GameplayState.Playing) return;
                        if (!root.Saves.Data.hasStartedGame || root.Saves.Data.selectedLevel != root.Levels.CurrentLevelIndex)
                            throw new InvalidOperationException("Continue selection was not persisted before gameplay.");
                        if (UnityEngine.Object.FindObjectOfType<KickTheBuddy.Physics.RagdollController>() == null)
                            throw new InvalidOperationException("Gameplay started without a bound ragdoll.");
                        root.Flow.ShowMainMenu();
                        SessionState.SetInt(StageKey, StageReturnMenu);
                        break;
                    case StageReturnMenu:
                        if (scene.name != "MainMenu" || root == null || root.Gameplay.State != GameplayState.MainMenu) return;
                        SessionState.SetInt(StageKey, StageSucceeded);
                        EditorApplication.isPlaying = false;
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void RequireRoot(GameBootstrapper root)
        {
            if (root == null || root.Flow == null || root.Saves == null || root.Levels == null || root.Gameplay == null)
                throw new InvalidOperationException("Persistent Game Systems composition root was not available in MainMenu.");
        }

        private static double ElapsedSeconds()
        {
            string value = SessionState.GetString(StartTimeKey, "0");
            double start;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out start) ? EditorApplication.timeSinceStartup - start : 0d;
        }

        private static void Fail(string message)
        {
            SessionState.SetString(FailureKey, message);
            SessionState.SetInt(StageKey, StageFailed);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else Finish(false);
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool(ActiveKey + ".Batch", false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            ConfigureSplashSaveFile(DefaultSaveFile);
            DeleteSmokeSave();
            EditorSceneManager.OpenScene(SplashPath, OpenSceneMode.Single);

            if (success) Debug.Log("FULL_GAME_FLOW_PLAYMODE_SMOKE_OK: Splash opened MainMenu, Continue opened gameplay in Playing state, and Main Menu return completed.");
            else Debug.LogError("FULL_GAME_FLOW_PLAYMODE_SMOKE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ConfigureSplashSaveFile(string fileName)
        {
            EditorSceneManager.OpenScene(SplashPath, OpenSceneMode.Single);
            GameSaveManager saves = UnityEngine.Object.FindObjectOfType<GameSaveManager>(true);
            if (saves == null) throw new InvalidOperationException("Splash GameSaveManager is missing.");
            SerializedObject serialized = new SerializedObject(saves);
            serialized.FindProperty("fileName").stringValue = fileName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(saves);
            EditorSceneManager.MarkSceneDirty(saves.gameObject.scene);
            EditorSceneManager.SaveScene(saves.gameObject.scene);
        }

        private static void DeleteSmokeSave()
        {
            string path = Path.Combine(Application.persistentDataPath, SmokeSaveFile);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }
}
#endif
