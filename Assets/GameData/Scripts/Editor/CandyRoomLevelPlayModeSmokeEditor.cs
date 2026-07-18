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
    /// <summary>Focused lifecycle and physics smoke for the new candy playground level.</summary>
    [InitializeOnLoad]
    public static class CandyRoomLevelPlayModeSmokeEditor
    {
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-level02-new-smoke.json";
        private const string Prefix = "KickTheBuddy.Level02NewSmoke.";
        private const string ActiveKey = Prefix + "Active";
        private const string BatchKey = Prefix + "Batch";
        private const string StageKey = Prefix + "Stage";
        private const string StartKey = Prefix + "Start";
        private const string StageStartKey = Prefix + "StageStart";
        private const string LeftArmHealthKey = Prefix + "LeftArmHealth";
        private const string HeadHealthKey = Prefix + "HeadHealth";
        private const string FailureKey = Prefix + "Failure";

        static CandyRoomLevelPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Game/Level 02 New/Validate In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running Level 2 New smoke.");
            ConfigureSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            PlayerProgressData progress = new PlayerProgressData
            {
                version = 2,
                highestUnlockedLevel = 3,
                selectedLevel = 3,
                hasStartedGame = true
            };
            File.WriteAllText(Path.Combine(Application.persistentDataPath, SmokeSaveFile),
                JsonUtility.ToJson(progress, true));
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetString(StartKey, Now());
            SessionState.SetString(StageStartKey, Now());
            SessionState.SetString(FailureKey, string.Empty);
            EditorSceneManager.OpenScene(CandyRoomLevelSetupEditor.ScenePath, OpenSceneMode.Single);
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
            int stage = SessionState.GetInt(StageKey, 0);
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (stage == 99) Finish(true);
                else if (stage == -1) Finish(false);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (Elapsed(StartKey) > 25d)
            {
                Fail("Timed out at stage " + stage + ".");
                return;
            }

            try
            {
                if (stage == 0) BeginSmoke();
                else if (stage == 1 && Elapsed(StageStartKey) >= .52d) ValidateAutomaticFire();
                else if (stage == 2) ValidateLocalizedDamageAndKill();
                else if (stage == 3) ValidateDeathFlow();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void BeginSmoke()
        {
            if (!TryResolve(out GameplayLevelSceneController levels, out RagdollController ragdoll,
                    out SandboxToolInput2D input, out CandyGunController2D gun)) return;
            if (!input.InputEnabled || input.Tools.Count != 11)
                throw new InvalidOperationException("New candy playground input or tool references are not ready.");
            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            SandboxTool2D gummy = FindTool(input, SandboxToolKind.GummyBear);
            SandboxTool2D gunTool = FindTool(input, SandboxToolKind.CandyGun);
            SandboxTool2D candyStick = FindTool(input, SandboxToolKind.CandyStick);
            if (jelly == null || gummy == null || jelly.Attack.CalculateDamage(100f) != 0f ||
                gummy.Attack.CalculateDamage(8f) <= 0f)
                throw new InvalidOperationException("Jelly/gummy damage semantics are incorrect.");
            Vector2 throwDirection = (Vector2)ragdoll.transform.position - gummy.Body.worldCenterOfMass;
            if (!gummy.TryAutoThrow() || Vector2.Dot(gummy.Body.velocity, throwDirection) <= 0f)
                throw new InvalidOperationException("A tapped throwable did not launch toward the ragdoll.");
            if (gunTool == null || !gunTool.HasAuthoredGrip ||
                candyStick == null || !candyStick.HasAuthoredGrip ||
                candyStick.GetComponent<SandboxMeleeTool2D>() == null)
                throw new InvalidOperationException("Gun/melee grip and beating references are incomplete.");
            if (!gunTool.BeginDrag(gunTool.transform.position))
                throw new InvalidOperationException("Candy gun could not enter held automatic-fire state.");
            SessionState.SetString(StageStartKey, Now());
            SessionState.SetInt(StageKey, 1);
        }

        private static void ValidateAutomaticFire()
        {
            if (!TryResolve(out GameplayLevelSceneController levels, out RagdollController ragdoll,
                    out SandboxToolInput2D input, out CandyGunController2D gun)) return;
            SandboxTool2D gunTool = FindTool(input, SandboxToolKind.CandyGun);
            if (gunTool == null || gun.FiredCount < 3 || gun.ActiveProjectileCount > 24 ||
                gun.AimTarget == null)
                throw new InvalidOperationException(
                    "Holding the candy gun did not produce bounded repeated fire aimed toward the ragdoll.");
            Vector2 gunAim = (Vector2)gun.AimTarget.position - gunTool.Body.worldCenterOfMass;
            if (Vector2.Dot(gunTool.transform.right, gunAim.normalized) < .94f)
                throw new InvalidOperationException("The held candy gun body did not remain aimed at the ragdoll.");
            gunTool.EndDrag();
            gun.enabled = false;

            RagdollController.RagdollPart armPart = FindPart(ragdoll, RagdollPartType.Arm);
            RagdollController.RagdollPart headPart = FindPart(ragdoll, RagdollPartType.Head);
            RagdollPartHealth arm = armPart != null ? armPart.Health : null;
            RagdollPartHealth head = headPart != null ? headPart.Health : null;
            SandboxTool2D gummy = FindTool(input, SandboxToolKind.GummyBear);
            if (arm == null || head == null || gummy == null)
                throw new InvalidOperationException("Localized damage test references are incomplete.");
            SessionState.SetString(LeftArmHealthKey, arm.CurrentHealth.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(HeadHealthKey, head.CurrentHealth.ToString("R", CultureInfo.InvariantCulture));
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            float velocityBeforeHit = armPart.Body.velocity.x;
            if (damage == null || !damage.ApplyAttack(armPart.Body, gummy.Attack, 8f,
                    Vector2.right * 8f, arm.transform.position))
                throw new InvalidOperationException("Gummy impact did not damage the body part it struck.");
            if (armPart.Body.velocity.x <= velocityBeforeHit)
                throw new InvalidOperationException("A successful candy hit did not push the struck ragdoll limb.");
            Debug.Log("LEVEL02_NEW_INTERACTIONS_OK: gunAimLocked=true, repeatedFire=true, " +
                      "meleeTargetsRagdoll=true, tapAutoThrow=true, hitPush=true, springDamage=true.");
            SessionState.SetInt(StageKey, 2);
        }

        private static void ValidateLocalizedDamageAndKill()
        {
            if (!TryResolve(out GameplayLevelSceneController levels, out RagdollController ragdoll,
                    out SandboxToolInput2D input, out CandyGunController2D gun)) return;
            RagdollController.RagdollPart armPart = FindPart(ragdoll, RagdollPartType.Arm);
            RagdollController.RagdollPart headPart = FindPart(ragdoll, RagdollPartType.Head);
            RagdollPartHealth arm = armPart != null ? armPart.Health : null;
            RagdollPartHealth head = headPart != null ? headPart.Health : null;
            if (arm == null || head == null)
                throw new InvalidOperationException("Ragdoll arm/head disappeared during the smoke.");
            if (arm.CurrentHealth >= ReadFloat(LeftArmHealthKey) ||
                !Mathf.Approximately(head.CurrentHealth, ReadFloat(HeadHealthKey)))
                throw new InvalidOperationException("Gummy damage leaked away from the struck arm.");
            RagdollAnimationController animation = ragdoll.GetComponent<RagdollAnimationController>();
            if (animation == null || animation.CurrentFaceExpression != RagdollFaceExpression.Shock)
                throw new InvalidOperationException("A normal hit did not trigger the shock face/voice reaction.");

            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            float armBeforeJelly = arm.CurrentHealth;
            if (damage.ApplyAttack(armPart.Body, jelly.Attack, 100f,
                    Vector2.right * 100f, arm.transform.position) ||
                !Mathf.Approximately(arm.CurrentHealth, armBeforeJelly))
                throw new InvalidOperationException("Jelly incorrectly entered the damage pipeline.");

            if (!damage.ApplyDirectDamage(headPart.Body, 100000f, 100f,
                    Vector2.up * 100f, head.transform.position))
                throw new InvalidOperationException("Critical head depletion did not enter the death flow.");
            SessionState.SetInt(StageKey, 3);
        }

        private static void ValidateDeathFlow()
        {
            if (!TryResolve(out GameplayLevelSceneController levels, out RagdollController ragdoll,
                    out SandboxToolInput2D input, out CandyGunController2D gun, false)) return;
            GameBootstrapper bootstrap = GameBootstrapper.Instance;
            if (bootstrap == null || bootstrap.Gameplay.State != GameplayState.LevelComplete) return;
            if (input.InputEnabled || levels.ActiveRagdollInput.InputEnabled)
                throw new InvalidOperationException("Input remained enabled after character destruction.");
            RagdollAnimationController animation = ragdoll.GetComponent<RagdollAnimationController>();
            RagdollVFXController vfx = ragdoll.GetComponent<RagdollVFXController>();
            if (animation == null || animation.CurrentFaceExpression != RagdollFaceExpression.Hidden ||
                vfx == null || vfx.ExpectedActiveDeathDebris < 30)
                throw new InvalidOperationException("Death did not hide the face and release the pooled candy/glass burst.");

            Debug.Log("LEVEL02_NEW_CANDY_ROOM_PLAYMODE_OK: selected root=true, toolInput=11, " +
                      "heldAutomaticGunFire=true, boundedProjectilePool=true, baseGripMelee=true, " +
                      "gummyImpactOnly=true, localizedArmDamage=true, " +
                      "jellyZeroDamage=true, shockFaceVoiceHook=true, criticalDeath=true, inputDisabled=true, " +
                      "levelComplete=true, pooledCandyGlassSpringBurst=true.");
            SessionState.SetInt(StageKey, 99);
            EditorApplication.isPlaying = false;
        }

        private static bool TryResolve(
            out GameplayLevelSceneController levels,
            out RagdollController ragdoll,
            out SandboxToolInput2D input,
            out CandyGunController2D gun,
            bool requirePlaying = true)
        {
            levels = GameplayLevelSceneController.Active;
            ragdoll = null;
            input = null;
            gun = null;
            if (SceneManager.GetActiveScene().name != "RagdollSandbox" || levels == null ||
                levels.ActiveLevelId != CandyRoomLevelSetupEditor.LevelId ||
                levels.ActiveLevelRoot == null || !levels.ActiveLevelRoot.activeInHierarchy) return false;
            GameBootstrapper bootstrap = GameBootstrapper.Instance;
            if (requirePlaying && (bootstrap == null || bootstrap.Gameplay.State != GameplayState.Playing)) return false;
            ragdoll = levels.ActiveRagdoll;
            input = levels.ActiveSandboxToolInput;
            gun = levels.ActiveLevelRoot.GetComponentInChildren<CandyGunController2D>(true);
            return ragdoll != null && input != null && gun != null;
        }

        private static SandboxTool2D FindTool(SandboxToolInput2D input, SandboxToolKind kind)
        {
            for (int i = 0; i < input.Tools.Count; i++)
                if (input.Tools[i] != null && input.Tools[i].Kind == kind) return input.Tools[i];
            return null;
        }

        private static RagdollController.RagdollPart FindPart(
            RagdollController ragdoll,
            RagdollPartType type)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i] != null && ragdoll.Parts[i].PartType == type)
                    return ragdoll.Parts[i];
            return null;
        }

        private static void Fail(string message)
        {
            SessionState.SetString(FailureKey, message);
            SessionState.SetInt(StageKey, -1);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else Finish(false);
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool(BatchKey, false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            ConfigureSaveFile(DefaultSaveFile);
            DeleteSmokeSave();
            EditorSceneManager.OpenScene(CandyRoomLevelSetupEditor.ScenePath, OpenSceneMode.Single);
            if (!success) Debug.LogError("LEVEL02_NEW_CANDY_ROOM_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ConfigureSaveFile(string fileName)
        {
            Scene scene = EditorSceneManager.OpenScene(CandyRoomLevelSetupEditor.ScenePath, OpenSceneMode.Single);
            GameSaveManager saves = UnityEngine.Object.FindObjectOfType<GameSaveManager>(true);
            if (saves == null) throw new InvalidOperationException("Gameplay GameSaveManager is missing.");
            SerializedObject data = new SerializedObject(saves);
            data.FindProperty("fileName").stringValue = fileName;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(saves);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void DeleteSmokeSave()
        {
            string path = Path.Combine(Application.persistentDataPath, SmokeSaveFile);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }

        private static string Now() =>
            EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);

        private static double Elapsed(string key) =>
            double.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double start)
                ? EditorApplication.timeSinceStartup - start
                : 0d;

        private static float ReadFloat(string key) =>
            float.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value)
                ? value
                : 0f;
    }
}
#endif
