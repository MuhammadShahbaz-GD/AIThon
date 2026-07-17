#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Physics and lifecycle regression for Level 3 tutorial cannon fire.</summary>
    [InitializeOnLoad]
    public static class LevelThreeCandyCannonsPlayModeSmokeEditor
    {
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-level03-cannons-smoke.json";
        private const string ActiveKey = "KickTheBuddy.Level03CannonSmoke.Active";
        private const string StageKey = "KickTheBuddy.Level03CannonSmoke.Stage";
        private const string StartKey = "KickTheBuddy.Level03CannonSmoke.Start";
        private const string TorsoBeforeKey = "KickTheBuddy.Level03CannonSmoke.TorsoBefore";
        private const string TorsoAfterLeftKey = "KickTheBuddy.Level03CannonSmoke.TorsoAfterLeft";
        private const string HeadBeforeKey = "KickTheBuddy.Level03CannonSmoke.HeadBefore";
        private const string BurstBaselineKey = "KickTheBuddy.Level03CannonSmoke.BurstBaseline";
        private const string HoldBaselineKey = "KickTheBuddy.Level03CannonSmoke.HoldBaseline";
        private const string HoldStartKey = "KickTheBuddy.Level03CannonSmoke.HoldStart";
        private const string HoldEndedKey = "KickTheBuddy.Level03CannonSmoke.HoldEnded";
        private const string FailureKey = "KickTheBuddy.Level03CannonSmoke.Failure";
        private const int StageLaunchLeft = 0;
        private const int StageWaitLeftHit = 1;
        private const int StageWaitRightHit = 2;
        private const int StageWaitTapBurst = 3;
        private const int StageWaitHoldBurst = 4;
        private const int StageSucceeded = 5;
        private const int StageFailed = -1;

        static LevelThreeCandyCannonsPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Game/Level 03/Validate Candy Cannons In Play Mode")]
        public static void RunFromMenu() => Run(false);
        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the Level 3 cannon smoke.");
            ConfigureSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            CreateLevelThreeSmokeSave();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ActiveKey + ".Batch", batch);
            SessionState.SetInt(StageKey, StageLaunchLeft);
            SessionState.SetString(FailureKey, string.Empty);
            SessionState.SetString(StartKey, Now());
            EditorSceneManager.OpenScene(LevelThreeCandyCannonsSetupEditor.ScenePath, OpenSceneMode.Single);
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
            int stage = SessionState.GetInt(StageKey, StageLaunchLeft);
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (stage == StageSucceeded) Finish(true);
                else if (stage == StageFailed) Finish(false);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (Elapsed(StartKey) > 30d)
            {
                Fail("Timed out at stage " + stage + ".");
                return;
            }

            try
            {
                if (stage == StageLaunchLeft) LaunchLeft();
                else if (stage == StageWaitLeftHit) WaitForLeftHit();
                else if (stage == StageWaitRightHit) WaitForRightHit();
                else if (stage == StageWaitTapBurst) WaitForTapBurst();
                else if (stage == StageWaitHoldBurst) WaitForHoldBurst();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void LaunchLeft()
        {
            if (!TryResolve(out CandyCannonController2D cannons, out RagdollController ragdoll,
                    out RagdollPartHealth torso, out RagdollPartHealth head)) return;
            if (!cannons.InputEnabled ||
                cannons.TutorialPhase != CandyCannonTutorialPhase.AwaitingLeftHit)
                throw new InvalidOperationException("Level 3 did not start with enabled left-cannon tutorial input.");
            if (cannons.RequestFire(CandyCannonSide.Right))
                throw new InvalidOperationException("Right cannon fired before the required left tutorial hit.");

            SessionState.SetString(TorsoBeforeKey, torso.CurrentHealth.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(HeadBeforeKey, head.CurrentHealth.ToString("R", CultureInfo.InvariantCulture));
            if (!cannons.RequestFire(CandyCannonSide.Left))
                throw new InvalidOperationException("The first left-cannon press did not queue exactly one projectile.");
            SessionState.SetInt(StageKey, StageWaitLeftHit);
        }

        private static void WaitForLeftHit()
        {
            if (!TryResolve(out CandyCannonController2D cannons, out RagdollController ragdoll,
                    out RagdollPartHealth torso, out RagdollPartHealth head)) return;
            if (cannons.TutorialPhase != CandyCannonTutorialPhase.AwaitingRightHit) return;
            CandyCannonVFXController2D vfx = ResolveVFX(cannons);
            if (vfx.FirePlayCount != 1 || vfx.ImpactPlayCount != 1)
                throw new InvalidOperationException(
                    "The first cannon shot did not produce exactly one muzzle and one impact VFX burst.");
            float before = ReadFloat(TorsoBeforeKey);
            if (torso.CurrentHealth >= before)
                throw new InvalidOperationException("The left candy did not damage the torso it physically struck.");
            if (!Mathf.Approximately(head.CurrentHealth, ReadFloat(HeadBeforeKey)))
                throw new InvalidOperationException("The left torso hit incorrectly damaged the head.");
            if (Vector2.Distance(vfx.LastImpactPoint, torso.transform.position) > 1.5f)
                throw new InvalidOperationException("Candy hit VFX was not emitted at the struck torso.");
            if (cannons.ActiveProjectileCount > 1)
                throw new InvalidOperationException("One cannon press activated more than one pooled projectile.");

            SessionState.SetString(TorsoAfterLeftKey,
                torso.CurrentHealth.ToString("R", CultureInfo.InvariantCulture));
            if (!cannons.RequestFire(CandyCannonSide.Right))
                throw new InvalidOperationException("The right cannon did not unlock after the successful left hit.");
            SessionState.SetInt(StageKey, StageWaitRightHit);
        }

        private static void WaitForRightHit()
        {
            if (!TryResolve(out CandyCannonController2D cannons, out RagdollController ragdoll,
                    out RagdollPartHealth torso, out RagdollPartHealth head)) return;
            if (cannons.TutorialPhase != CandyCannonTutorialPhase.FreePlay) return;
            if (torso.CurrentHealth >= ReadFloat(TorsoAfterLeftKey))
                throw new InvalidOperationException("The right candy did not apply its second local torso hit.");
            if (!Mathf.Approximately(head.CurrentHealth, ReadFloat(HeadBeforeKey)))
                throw new InvalidOperationException("Tutorial torso hits leaked damage into another body part.");

            int baseline = cannons.CompletedShotCount;
            if (!cannons.RequestFire(CandyCannonSide.Left) ||
                !cannons.RequestFire(CandyCannonSide.Left) ||
                !cannons.RequestFire(CandyCannonSide.Left))
                throw new InvalidOperationException("Rapid taps were not buffered as individual cannon shots.");
            if (cannons.PendingShotCount != 3)
                throw new InvalidOperationException("Three taps did not create exactly three queued shots.");
            SessionState.SetInt(BurstBaselineKey, baseline);
            SessionState.SetInt(StageKey, StageWaitTapBurst);
        }

        private static void WaitForTapBurst()
        {
            if (!TryResolve(out CandyCannonController2D cannons, out RagdollController ragdoll,
                    out RagdollPartHealth torso, out RagdollPartHealth head)) return;
            if (cannons.CompletedShotCount < SessionState.GetInt(BurstBaselineKey, 0) + 3) return;
            int baseline = cannons.CompletedShotCount;
            if (!cannons.BeginContinuousFire(CandyCannonSide.Left))
                throw new InvalidOperationException("Held cannon fire did not begin with an immediate shot.");
            SessionState.SetInt(HoldBaselineKey, baseline);
            SessionState.SetString(HoldStartKey, Now());
            SessionState.SetBool(HoldEndedKey, false);
            SessionState.SetInt(StageKey, StageWaitHoldBurst);
        }

        private static void WaitForHoldBurst()
        {
            if (!TryResolve(out CandyCannonController2D cannons, out RagdollController ragdoll,
                    out RagdollPartHealth torso, out RagdollPartHealth head)) return;
            if (!SessionState.GetBool(HoldEndedKey, false) && Elapsed(HoldStartKey) >= .7d)
            {
                cannons.EndContinuousFire(CandyCannonSide.Left);
                SessionState.SetBool(HoldEndedKey, true);
            }
            if (!SessionState.GetBool(HoldEndedKey, false) ||
                cannons.CompletedShotCount < SessionState.GetInt(HoldBaselineKey, 0) + 3) return;
            CandyCannonVFXController2D vfx = ResolveVFX(cannons);
            if (vfx.FirePlayCount < cannons.CompletedShotCount ||
                vfx.ImpactPlayCount != cannons.CompletedShotCount)
                throw new InvalidOperationException(
                    "Tap/hold cannon shots did not produce matching pooled fire and hit VFX.");

            cannons.SetInputEnabled(false);
            if (cannons.RequestFire(CandyCannonSide.Left))
                throw new InvalidOperationException("Cannon input still fired after gameplay disabled it.");
            Debug.Log("LEVEL03_CANDY_CANNONS_PLAYMODE_OK: level_03 selected, left-before-right gating=true, " +
                      "one projectile per press=true, physical torso hits=true, other-part damage=false, " +
                      "right tutorial unlock=true, rapidTapQueue=3, heldAutoFire=true, freePlay=true, " +
                      "pooledMuzzleAndImpactVFX=true, disabledInputStopsFire=true.");
            SessionState.SetInt(StageKey, StageSucceeded);
            EditorApplication.isPlaying = false;
        }

        private static bool TryResolve(out CandyCannonController2D cannons, out RagdollController ragdoll,
            out RagdollPartHealth torso, out RagdollPartHealth head)
        {
            cannons = null;
            ragdoll = null;
            torso = null;
            head = null;
            if (SceneManager.GetActiveScene().name != "RagdollSandbox") return false;
            GameplayLevelSceneController levels = GameplayLevelSceneController.Active;
            if (levels == null || levels.ActiveLevelId != LevelThreeCandyCannonsSetupEditor.LevelId ||
                levels.ActiveLevelIndex != 2 || levels.ActiveLevelRoot == null ||
                !levels.ActiveLevelRoot.activeInHierarchy) return false;
            GameBootstrapper bootstrapper = GameBootstrapper.Instance;
            if (bootstrapper == null || bootstrapper.Gameplay.State != GameplayState.Playing) return false;

            cannons = levels.ActiveCandyCannons;
            ragdoll = levels.ActiveRagdoll;
            if (cannons == null || ragdoll == null) return false;
            torso = FindPart(ragdoll, RagdollPartType.Torso);
            head = FindPart(ragdoll, RagdollPartType.Head);
            if (torso == null || head == null)
                throw new InvalidOperationException("Level 3 ragdoll health references are incomplete.");
            return true;
        }

        private static RagdollPartHealth FindPart(RagdollController ragdoll, RagdollPartType type)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i] != null && ragdoll.Parts[i].PartType == type)
                    return ragdoll.Parts[i].Health;
            return null;
        }

        private static CandyCannonVFXController2D ResolveVFX(CandyCannonController2D cannons)
        {
            CandyCannonVFXController2D vfx =
                cannons.GetComponentInChildren<CandyCannonVFXController2D>(true);
            if (vfx == null || vfx.ImpactPoolSize != 4)
                throw new InvalidOperationException("The four-slot candy cannon VFX pool is unavailable.");
            return vfx;
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
            ConfigureSaveFile(DefaultSaveFile);
            DeleteSmokeSave();
            EditorSceneManager.OpenScene(LevelThreeCandyCannonsSetupEditor.ScenePath, OpenSceneMode.Single);
            if (!success) Debug.LogError("LEVEL03_CANDY_CANNONS_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ConfigureSaveFile(string fileName)
        {
            Scene scene = EditorSceneManager.OpenScene(
                LevelThreeCandyCannonsSetupEditor.ScenePath, OpenSceneMode.Single);
            GameSaveManager saves = UnityEngine.Object.FindObjectOfType<GameSaveManager>(true);
            if (saves == null) throw new InvalidOperationException("Gameplay GameSaveManager is missing.");
            SerializedObject data = new SerializedObject(saves);
            data.FindProperty("fileName").stringValue = fileName;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(saves);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void CreateLevelThreeSmokeSave()
        {
            PlayerProgressData progress = new PlayerProgressData
            {
                version = 2,
                highestUnlockedLevel = 2,
                selectedLevel = 2,
                hasStartedGame = true
            };
            File.WriteAllText(Path.Combine(Application.persistentDataPath, SmokeSaveFile),
                JsonUtility.ToJson(progress, true));
        }

        private static void DeleteSmokeSave()
        {
            string path = Path.Combine(Application.persistentDataPath, SmokeSaveFile);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }

        private static float ReadFloat(string key)
        {
            if (float.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value)) return value;
            return 0f;
        }

        private static string Now() =>
            EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);

        private static double Elapsed(string key)
        {
            return double.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double start)
                ? EditorApplication.timeSinceStartup - start
                : 0d;
        }
    }
}
#endif
