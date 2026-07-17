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
        private const string StageStartTimeKey = "KickTheBuddy.GameFlowSmoke.StageStartTime";
        private const string TimerSampleKey = "KickTheBuddy.GameFlowSmoke.TimerSample";
        private const string FailureKey = "KickTheBuddy.GameFlowSmoke.Failure";
        private const int StageSplash = 0;
        private const int StageGameplay = 1;
        private const int StageIdleFace = 2;
        private const int StagePausedTimer = 3;
        private const int StageResumedTimer = 4;
        private const int StageNextLevel = 5;
        private const int StageReturnMenu = 6;
        private const int StageSucceeded = 7;
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
            if (ElapsedSeconds() > 60d) { Fail("Timed out while waiting for stage " + stage + "."); return; }

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
                        float configuredTime = root.Levels.CurrentLevel.TimeLimit;
                        if (configuredTime < LevelDefinition.MinimumPlayTimeSeconds || Mathf.Abs(root.Gameplay.RemainingTime - configuredTime) > 0.1f)
                            throw new InvalidOperationException($"Gameplay timer did not begin at the configured minimum-safe duration ({configuredTime:F1}s). Actual: {root.Gameplay.RemainingTime:F1}s.");
                        BeginTimedStage(StageIdleFace);
                        break;
                    case StageIdleFace:
                        // Allow the initial physics settling impact to finish before measuring the no-interaction idle delay.
                        if (root == null || root.Gameplay.State != GameplayState.Playing || StageElapsedSeconds() < 6.5d) return;
                        KickTheBuddy.Physics.RagdollAnimationController[] animations =
                            UnityEngine.Object.FindObjectsOfType<KickTheBuddy.Physics.RagdollAnimationController>();
                        bool idleFacePlaying = false;
                        string idleFaceDiagnostics = string.Empty;
                        for (int i = 0; i < animations.Length; i++)
                        {
                            idleFaceDiagnostics += $" [{animations[i].name}: state={animations[i].CurrentState}, authored={animations[i].HasAuthoredIdleFaceAnimation}, playing={animations[i].IsAuthoredIdleFacePlaying}]";
                            if (animations[i].CurrentState == KickTheBuddy.Physics.RagdollAnimationState.Idle &&
                                animations[i].HasAuthoredIdleFaceAnimation && animations[i].IsAuthoredIdleFacePlaying)
                            {
                                idleFacePlaying = true;
                                break;
                            }
                        }
                        if (!idleFacePlaying)
                            throw new InvalidOperationException("The authored face sequence did not begin after the non-interacting idle delay." + idleFaceDiagnostics);
                        root.Gameplay.Pause();
                        SetTimerSample(root.Gameplay.RemainingTime);
                        BeginTimedStage(StagePausedTimer);
                        break;
                    case StagePausedTimer:
                        if (root == null || root.Gameplay.State != GameplayState.Paused || StageElapsedSeconds() < 0.25d) return;
                        if (Mathf.Abs(root.Gameplay.RemainingTime - GetTimerSample()) > 0.001f)
                            throw new InvalidOperationException("Gameplay timer continued while the game was paused.");
                        root.Gameplay.Resume();
                        SetTimerSample(root.Gameplay.RemainingTime);
                        BeginTimedStage(StageResumedTimer);
                        break;
                    case StageResumedTimer:
                        if (root == null || root.Gameplay.State != GameplayState.Playing || StageElapsedSeconds() < 0.25d) return;
                        if (root.Gameplay.RemainingTime >= GetTimerSample() - 0.05f)
                            throw new InvalidOperationException("Gameplay timer did not continue after resume.");
                        root.Gameplay.Restart();
                        if (Mathf.Abs(root.Gameplay.RemainingTime - root.Levels.CurrentLevel.TimeLimit) > 0.1f)
                            throw new InvalidOperationException("Restart did not reset the gameplay timer to the full level duration.");
                        root.Gameplay.CompleteLevel();
                        if (root.Gameplay.State != GameplayState.LevelComplete)
                            throw new InvalidOperationException("The completed level did not enter LevelComplete before Next was requested.");
                        root.Gameplay.NextLevel();
                        if (!root.Flow.IsTransitioning || root.Gameplay.State != GameplayState.Loading)
                            throw new InvalidOperationException("Next did not enter the guarded single-scene loading flow.");
                        SessionState.SetInt(StageKey, StageNextLevel);
                        break;
                    case StageNextLevel:
                        if (scene.name != "CandyLab" || root == null || root.Gameplay.State != GameplayState.Playing) return;
                        if (root.Levels.CurrentLevelIndex != 1 || root.Saves.Data.selectedLevel != 1)
                            throw new InvalidOperationException("Next scene loaded without selecting and persisting Level 2.");
                        if (Mathf.Abs(root.Gameplay.RemainingTime - root.Levels.CurrentLevel.TimeLimit) > .15f)
                            throw new InvalidOperationException("Level 2 did not start with its full configured play time.");
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

        private static void BeginTimedStage(int stage)
        {
            SessionState.SetInt(StageKey, stage);
            SessionState.SetString(StageStartTimeKey, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        }

        private static double StageElapsedSeconds()
        {
            string value = SessionState.GetString(StageStartTimeKey, "0");
            double start;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out start) ? EditorApplication.timeSinceStartup - start : 0d;
        }

        private static void SetTimerSample(float value) => SessionState.SetString(TimerSampleKey, value.ToString("R", CultureInfo.InvariantCulture));

        private static float GetTimerSample()
        {
            string value = SessionState.GetString(TimerSampleKey, "0");
            float sample;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out sample) ? sample : 0f;
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

            if (success) Debug.Log("FULL_GAME_FLOW_PLAYMODE_SMOKE_OK: Splash opened MainMenu, idle face and timer checks passed, Next performed one guarded Level 2 scene load, Level 2 started, and Main Menu return completed.");
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
