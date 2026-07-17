#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Deterministic Play Mode regression for structural limb distance breaks.</summary>
    [InitializeOnLoad]
    public static class LimbDistanceBreakPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-limb-distance-smoke.json";
        private const string ActiveKey = "KickTheBuddy.LimbDistanceSmoke.Active";
        private const string BatchKey = "KickTheBuddy.LimbDistanceSmoke.Batch";
        private const string StartKey = "KickTheBuddy.LimbDistanceSmoke.Start";
        private const string FailureKey = "KickTheBuddy.LimbDistanceSmoke.Failure";
        private const string SuccessKey = "KickTheBuddy.LimbDistanceSmoke.Success";

        private static RagdollController controller;
        private static RagdollController.RagdollPart targetPart;
        private static RagdollController.RagdollPart headPart;
        private static CracksModifier cracks;
        private static ParticleSystem sharedGlass;
        private static GameObject connectedSpringVisual;
        private static Vector2 forcedPosition;
        private static float initialCombinedHealth;
        private static float triggerTime;
        private static int stage;
        private static int distanceEvents;
        private static int severEvents;
        private static int explodeEvents;
        private static int controllerBreakEvents;
        private static float measuredDistance;
        private static float measuredLimit;

        static LimbDistanceBreakPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Ragdoll/Validate Limb Distance Break In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the limb distance smoke.");

            ResetRuntimeState();
            ConfigureSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(StartKey, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(FailureKey, string.Empty);
            SessionState.SetBool(SuccessKey, false);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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
            if (!EditorApplication.isPlaying || Time.frameCount < 4) return;
            if (Elapsed() > 25d)
            {
                Fail("Timed out waiting for the distance-break regression.");
                return;
            }

            try
            {
                if (stage == 0) BeginOverstretch();
                else if (stage == 1) ContinueOverstretch();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void BeginOverstretch()
        {
            if (SceneManager.GetActiveScene().name != "RagdollSandbox") return;
            controller = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (controller == null || controller.Parts.Count != 6) return;

            for (int i = 0; i < controller.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = controller.Parts[i];
                if (part == null) continue;
                if (part.PartType == RagdollPartType.Head) headPart = part;
                if (targetPart == null && part.PartType == RagdollPartType.Arm &&
                    part.DismemberableLimb != null && part.Body != null)
                    targetPart = part;
            }

            if (targetPart == null || targetPart.Health == null || headPart?.DismemberableLimb == null)
                throw new InvalidOperationException("The six authored ragdoll parts are incomplete.");
            if (!targetPart.DismemberableLimb.BreakWhenOverstretched ||
                targetPart.DismemberableLimb.RequiredOverstretchFixedSteps != LongFunBalanceSetupEditor.DistanceBreakFixedSteps ||
                targetPart.DismemberableLimb.AllowDistanceBreakWhileDragging)
                throw new InvalidOperationException("The arm distance-break settings were not authored correctly.");

            cracks = targetPart.Body.GetComponent<CracksModifier>();
            RagdollVFXController vfx = controller.GetComponent<RagdollVFXController>();
            Transform glassTransform = vfx != null ? vfx.transform.Find("Shared Impact Glass") : null;
            sharedGlass = glassTransform != null ? glassTransform.GetComponent<ParticleSystem>() : null;
            if (cracks == null || sharedGlass == null)
                throw new InvalidOperationException("The target limb crack event or shared glass VFX is missing.");
            connectedSpringVisual = cracks.OnExplode.GetPersistentEventCount() > 0
                ? cracks.OnExplode.GetPersistentTarget(0) as GameObject
                : null;
            if (connectedSpringVisual == null || !connectedSpringVisual.activeSelf)
                throw new InvalidOperationException("The target limb has no active spring visual bound to its explode event.");

            targetPart.DismemberableLimb.DistanceLimitExceeded += HandleDistanceLimitExceeded;
            targetPart.DismemberableLimb.Severed += HandleSevered;
            cracks.Exploded += HandleExploded;
            controller.OnLimbBroken += HandleLimbBroken;

            initialCombinedHealth = controller.CurrentHealth;
            float forcedDistance = targetPart.DismemberableLimb.MaximumAnchorSeparation + 2f;
            forcedPosition = targetPart.Body.position + Vector2.right * forcedDistance;
            triggerTime = Time.realtimeSinceStartup;
            stage = 1;
        }

        private static void ContinueOverstretch()
        {
            if (targetPart == null || targetPart.DismemberableLimb == null)
                throw new InvalidOperationException("The target limb disappeared before validation.");

            if (!targetPart.DismemberableLimb.IsSevered)
            {
                // Hold the body beyond the hard limit long enough to reproduce a sustained drag/solver failure.
                targetPart.Body.position = forcedPosition;
                targetPart.Body.velocity = Vector2.zero;
                Physics2D.SyncTransforms();
                if (Time.realtimeSinceStartup - triggerTime > 4f)
                    throw new InvalidOperationException("The overstretched limb did not break within four seconds.");
                return;
            }

            if (distanceEvents != 1 || severEvents != 1 || explodeEvents != 1 || controllerBreakEvents != 1)
                throw new InvalidOperationException($"Expected one distance/sever/explode/controller event, got " +
                                                    $"{distanceEvents}/{severEvents}/{explodeEvents}/{controllerBreakEvents}.");
            if (measuredDistance <= measuredLimit)
                throw new InvalidOperationException("The structural break was reported inside its authored distance limit.");
            if (!targetPart.Health.IsDepleted || targetPart.DismemberableLimb.JointHealth > 0f)
                throw new InvalidOperationException("The detached limb did not synchronize both health sources to zero.");
            if (targetPart.Body.transform.parent != null)
                throw new InvalidOperationException("The broken limb branch is still parented to the live ragdoll.");
            if (headPart.DismemberableLimb.IsSevered || controller.CurrentHealth <= 0f ||
                controller.CurrentHealth >= initialCombinedHealth)
                throw new InvalidOperationException("A noncritical arm break incorrectly killed or failed to damage the remaining character.");
            if (sharedGlass.particleCount <= 0 && !sharedGlass.isPlaying)
                throw new InvalidOperationException("The pooled local glass burst did not play at the broken limb.");
            if (connectedSpringVisual.activeSelf)
                throw new InvalidOperationException("The broken limb left its long connector spring visible.");

            Debug.Log($"LIMB_DISTANCE_BREAK_PLAYMODE_OK: part={targetPart.DisplayName}, " +
                      $"distance={measuredDistance:F2}, limit={measuredLimit:F2}, events=1/1/1/1, " +
                      $"limbHealth=0, characterAlive=true, pooledGlass=true, connectorHidden=true.");
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }

        private static void HandleDistanceLimitExceeded(DismemberableLimb limb, float distance, float limit, Vector2 point)
        {
            distanceEvents++;
            measuredDistance = distance;
            measuredLimit = limit;
        }

        private static void HandleSevered(DismemberableLimb limb) => severEvents++;
        private static void HandleExploded(CracksModifier modifier, Vector2 point) => explodeEvents++;

        private static void HandleLimbBroken(Rigidbody2D body, Vector2 point)
        {
            if (targetPart != null && body == targetPart.Body) controllerBreakEvents++;
        }

        private static void Fail(string message)
        {
            SessionState.SetString(FailureKey, message);
            SessionState.SetBool(SuccessKey, false);
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
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ResetRuntimeState();
            if (!success) Debug.LogError("LIMB_DISTANCE_BREAK_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ResetRuntimeState()
        {
            controller = null;
            targetPart = null;
            headPart = null;
            cracks = null;
            sharedGlass = null;
            connectedSpringVisual = null;
            stage = 0;
            distanceEvents = 0;
            severEvents = 0;
            explodeEvents = 0;
            controllerBreakEvents = 0;
            measuredDistance = 0f;
            measuredLimit = 0f;
        }

        private static void ConfigureSaveFile(string fileName)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameSaveManager saves = UnityEngine.Object.FindObjectOfType<GameSaveManager>(true);
            if (saves == null) throw new InvalidOperationException("RagdollSandbox GameSaveManager is missing.");
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

        private static double Elapsed()
        {
            return double.TryParse(SessionState.GetString(StartKey, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double start)
                ? EditorApplication.timeSinceStartup - start
                : 0d;
        }
    }
}
#endif
