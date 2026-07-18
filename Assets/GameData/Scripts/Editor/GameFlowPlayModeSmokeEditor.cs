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
        private const int StageRetryReload = 5;
        private const int StageNextLevel = 6;
        private const int StageReturnMenu = 7;
        private const int StageSucceeded = 8;
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
                        GameplayLevelSceneController initialLevels = GameplayLevelSceneController.Active;
                        if (initialLevels == null || initialLevels.ActiveLevelId != "level_01")
                            throw new InvalidOperationException("Initial gameplay did not activate Level 01 content.");
                        ValidateSharedRoom(initialLevels, 5f, 16f, "Initial Level 01");
                        BeginTimedStage(StageIdleFace);
                        break;
                    case StageIdleFace:
                        // Allow the initial physics settling impact to finish before measuring the no-interaction idle delay.
                        if (root == null || root.Gameplay.State != GameplayState.Playing || StageElapsedSeconds() < 6.5d) return;
                        KickTheBuddy.Physics.RagdollAnimationController[] animations =
                            UnityEngine.Object.FindObjectsOfType<KickTheBuddy.Physics.RagdollAnimationController>();
                        bool idleFacePlaying = false;
                        KickTheBuddy.Physics.RagdollAnimationController idleAnimation = null;
                        string idleFaceDiagnostics = string.Empty;
                        for (int i = 0; i < animations.Length; i++)
                        {
                            idleFaceDiagnostics += $" [{animations[i].name}: state={animations[i].CurrentState}, expression={animations[i].CurrentFaceExpression}, authored={animations[i].HasAuthoredFaceAnimation}, playing={animations[i].IsAuthoredFacePlaying}]";
                            bool validIdleExpression =
                                animations[i].CurrentFaceExpression == KickTheBuddy.Physics.RagdollFaceExpression.Smile ||
                                animations[i].CurrentFaceExpression == KickTheBuddy.Physics.RagdollFaceExpression.Laugh;
                            if (animations[i].CurrentState == KickTheBuddy.Physics.RagdollAnimationState.Idle &&
                                validIdleExpression &&
                                animations[i].HasAuthoredFaceAnimation && animations[i].IsAuthoredFacePlaying)
                            {
                                idleFacePlaying = true;
                                idleAnimation = animations[i];
                                break;
                            }
                        }
                        if (!idleFacePlaying)
                            throw new InvalidOperationException("The authored Smile sequence did not begin after the non-interacting idle delay." + idleFaceDiagnostics);

                        // Verify real damage routing, not presentation-only helper methods: one small hit must
                        // shock the face, while the third hit in the same combo must promote it to Cry.
                        KickTheBuddy.Physics.RagdollController faceRagdoll =
                            idleAnimation.GetComponent<KickTheBuddy.Physics.RagdollController>();
                        KickTheBuddy.Physics.RagdollDamageManager faceDamage =
                            idleAnimation.GetComponent<KickTheBuddy.Physics.RagdollDamageManager>();
                        KickTheBuddy.Physics.RagdollPoseController2D facePose =
                            idleAnimation.GetComponent<KickTheBuddy.Physics.RagdollPoseController2D>();
                        KickTheBuddy.Physics.RagdollController.RagdollPart faceTarget = null;
                        float strongestSafeHealth = 3f;
                        for (int i = 0; i < faceRagdoll.Parts.Count; i++)
                        {
                            KickTheBuddy.Physics.RagdollController.RagdollPart candidate = faceRagdoll.Parts[i];
                            if (candidate != null && candidate.Body != null && candidate.Health != null &&
                                !candidate.Health.IsCritical && !candidate.Health.IsDepleted &&
                                candidate.Health.CurrentHealth > strongestSafeHealth)
                            {
                                faceTarget = candidate;
                                strongestSafeHealth = candidate.Health.CurrentHealth;
                            }
                        }
                        if (faceDamage == null || faceTarget == null)
                            throw new InvalidOperationException("Could not find a safe non-critical part for the face reaction smoke.");
                        Vector2 facePoint = faceTarget.Body.worldCenterOfMass;
                        if (!faceDamage.ApplyDirectDamage(faceTarget.Body, 1f, 1f, Vector2.zero, facePoint) ||
                            idleAnimation.CurrentFaceExpression != KickTheBuddy.Physics.RagdollFaceExpression.Shock)
                            throw new InvalidOperationException("A normal resolved hit did not play the authored Shock expression.");
                        if (facePose == null || facePose.CurrentDamageElasticity <= 0f)
                            throw new InvalidOperationException("A resolved hit did not relax the active ragdoll joints.");
                        if (!faceDamage.ApplyDirectDamage(faceTarget.Body, 1f, 1f, Vector2.zero, facePoint) ||
                            !faceDamage.ApplyDirectDamage(faceTarget.Body, 1f, 1f, Vector2.zero, facePoint) ||
                            faceRagdoll.CurrentCombo < 3 ||
                            idleAnimation.CurrentFaceExpression != KickTheBuddy.Physics.RagdollFaceExpression.Cry)
                            throw new InvalidOperationException("The third resolved combo hit did not promote the face to authored Cry.");
                        root.Gameplay.Pause();
                        BeginTimedStage(StagePausedTimer);
                        break;
                    case StagePausedTimer:
                        if (root == null || root.Gameplay.State != GameplayState.Paused || StageElapsedSeconds() < 0.25d) return;
                        root.Gameplay.Resume();
                        BeginTimedStage(StageResumedTimer);
                        break;
                    case StageResumedTimer:
                        if (root == null || root.Gameplay.State != GameplayState.Playing || StageElapsedSeconds() < 0.25d) return;
                        root.Gameplay.Restart();
                        if (!root.Flow.IsTransitioning || root.Gameplay.State != GameplayState.Loading)
                            throw new InvalidOperationException("Retry did not enter the guarded gameplay-scene reload flow.");
                        SessionState.SetInt(StageKey, StageRetryReload);
                        break;
                    case StageRetryReload:
                        if (scene.name != "RagdollSandbox" || root == null || root.Gameplay.State != GameplayState.Playing)
                            return;
                        GameplayLevelSceneController retryLevels = GameplayLevelSceneController.Active;
                        if (root.Levels.CurrentLevelIndex != 0 || retryLevels == null ||
                            retryLevels.ActiveLevelId != "level_01" || retryLevels.ActiveLevelIndex != 0)
                            throw new InvalidOperationException("Retry did not reactivate the saved Level 01 content.");
                        ValidateSharedRoom(retryLevels, 5f, 16f, "Level 01");
                        root.Gameplay.CompleteLevel();
                        if (root.Gameplay.State != GameplayState.LevelComplete)
                            throw new InvalidOperationException("The completed level did not enter LevelComplete before Next was requested.");
                        root.Gameplay.NextLevel();
                        if (!root.Flow.IsTransitioning || root.Gameplay.State != GameplayState.Loading)
                            throw new InvalidOperationException("Next did not enter the guarded single-scene loading flow.");
                        SessionState.SetInt(StageKey, StageNextLevel);
                        break;
                    case StageNextLevel:
                        if (scene.name != "RagdollSandbox" || root == null || root.Gameplay.State != GameplayState.Playing) return;
                        if (root.Levels.CurrentLevelIndex != 1 || root.Saves.Data.selectedLevel != 1)
                            throw new InvalidOperationException("Next reload did not select and persist Level 02.");
                        GameplayLevelSceneController nextLevels = GameplayLevelSceneController.Active;
                        if (nextLevels == null || nextLevels.ActiveLevelId != "level_02" ||
                            nextLevels.ActiveLevelIndex != 1 || nextLevels.ActiveSandboxToolInput == null ||
                            !nextLevels.ActiveLevelRoot.activeInHierarchy)
                            throw new InvalidOperationException("Next reload did not activate the Level 02 content and tools.");
                        ValidateSharedRoom(nextLevels, 2.975f, 9f, "Level 02");
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

        private static void ValidateSharedRoom(GameplayLevelSceneController sceneLevels,
            float expectedDamageAtSpeedEight, float expectedDamageCap, string label)
        {
            if (sceneLevels.SharedRoom == null || !sceneLevels.SharedRoom.activeInHierarchy ||
                sceneLevels.SharedRoomAttacks.Count == 0)
                throw new InvalidOperationException(label + " did not keep the shared Levels/Room active.");
            for (int i = 0; i < sceneLevels.SharedRoomAttacks.Count; i++)
            {
                KickTheBuddy.Physics.RagdollAttackManager2D attack = sceneLevels.SharedRoomAttacks[i];
                if (attack == null || !Mathf.Approximately(
                        attack.CalculateDamage(8f), expectedDamageAtSpeedEight) ||
                    !Mathf.Approximately(attack.CalculateDamage(1000f), expectedDamageCap))
                    throw new InvalidOperationException(label + " did not apply its damage profile to the shared Room.");
            }
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

            if (success) Debug.Log("FULL_GAME_FLOW_PLAYMODE_SMOKE_OK: Splash opened MainMenu, Retry reloaded Level 01, Next reloaded the same gameplay scene with Level 02 active, and Main Menu return completed.");
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
