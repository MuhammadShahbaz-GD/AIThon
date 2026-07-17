#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Play Mode regression for the permanent grounded state caused by one severed leg.</summary>
    [InitializeOnLoad]
    public static class BrokenLegRecoveryPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-broken-leg-smoke.json";
        private const string ActiveKey = "KickTheBuddy.BrokenLegSmoke.Active";
        private const string BatchKey = "KickTheBuddy.BrokenLegSmoke.Batch";
        private const string StartKey = "KickTheBuddy.BrokenLegSmoke.Start";
        private const string FailureKey = "KickTheBuddy.BrokenLegSmoke.Failure";
        private const string SuccessKey = "KickTheBuddy.BrokenLegSmoke.Success";

        private static RagdollController controller;
        private static RagdollPoseController2D pose;
        private static RagdollController.RagdollPart brokenLeg;
        private static RagdollController.RagdollPart survivingLeg;
        private static CracksModifier brokenLegCracks;
        private static Rigidbody2D torso;
        private static float initialHealth;
        private static float initialTorsoHeight;
        private static float minimumTorsoHeight;
        private static float maximumTorsoTilt;
        private static float severedAt;
        private static bool stumbleObserved;
        private static int responseEvents;
        private static int recoveryEvents;
        private static int explosionEvents;
        private static int stage;

        static BrokenLegRecoveryPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Ragdoll/Validate Broken Leg Grounded State In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the broken-leg smoke.");

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
            if (!EditorApplication.isPlaying || Time.frameCount < 6) return;
            if (Elapsed() > 25d)
            {
                Fail("Timed out waiting for the broken-leg grounded-state regression.");
                return;
            }

            try
            {
                if (stage == 0) SeverOneLeg();
                else ObserveResponse();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void SeverOneLeg()
        {
            if (SceneManager.GetActiveScene().name != "RagdollSandbox" || Time.time < .25f) return;
            controller = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (controller == null || controller.Parts.Count != 6) return;

            pose = controller.GetComponent<RagdollPoseController2D>();
            for (int i = 0; i < controller.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = controller.Parts[i];
                if (part == null) continue;
                if (part.PartType == RagdollPartType.Torso) torso = part.Body;
                if (part.PartType != RagdollPartType.Leg || part.DismemberableLimb == null) continue;
                if (brokenLeg == null) brokenLeg = part;
                else if (survivingLeg == null) survivingLeg = part;
            }

            if (pose == null || torso == null || brokenLeg == null || survivingLeg == null ||
                brokenLeg.Health == null || survivingLeg.Health == null)
                throw new InvalidOperationException("The active ragdoll does not contain two authored legs and a pose controller.");
            if (!pose.ReactToBrokenLeg || pose.AllowOneLegRecovery)
                throw new InvalidOperationException("Inspector-authored broken-leg grounded settings are invalid.");

            brokenLegCracks = brokenLeg.Body.GetComponent<CracksModifier>();
            if (brokenLegCracks == null)
                throw new InvalidOperationException("The broken leg is missing its local explosion event component.");

            pose.BrokenLegResponseStarted += HandleResponseStarted;
            pose.OneLegRecoveryStarted += HandleRecoveryStarted;
            brokenLegCracks.Exploded += HandleLegExploded;
            initialHealth = controller.CurrentHealth;
            initialTorsoHeight = torso.position.y;
            minimumTorsoHeight = initialTorsoHeight;
            maximumTorsoTilt = Mathf.Abs(Mathf.DeltaAngle(torso.rotation, 0f));

            brokenLeg.DismemberableLimb.ForceSever(Vector2.zero, brokenLeg.Body.worldCenterOfMass);
            if (!brokenLeg.DismemberableLimb.IsSevered)
                throw new InvalidOperationException("The authored leg did not sever through the structural API.");

            severedAt = Time.realtimeSinceStartup;
            stumbleObserved = pose.IsBrokenLegStumbling;
            stage = 1;
        }

        private static void ObserveResponse()
        {
            minimumTorsoHeight = Mathf.Min(minimumTorsoHeight, torso.position.y);
            maximumTorsoTilt = Mathf.Max(maximumTorsoTilt, Mathf.Abs(Mathf.DeltaAngle(torso.rotation, 0f)));
            stumbleObserved |= pose.IsBrokenLegStumbling;

            float observationDuration = pose.BrokenLegFallDuration + pose.BrokenLegRecoveryDelay + .55f;
            if (Time.realtimeSinceStartup - severedAt < observationDuration) return;

            float verticalDrop = initialTorsoHeight - minimumTorsoHeight;
            if (responseEvents != 1 || pose.BrokenLegCount != 1)
                throw new InvalidOperationException($"Expected one broken-leg response, got events={responseEvents}, count={pose.BrokenLegCount}.");
            if (!stumbleObserved)
                throw new InvalidOperationException("The ragdoll never entered its forced broken-leg stumble.");
            if (maximumTorsoTilt < 12f && verticalDrop < .12f)
                throw new InvalidOperationException($"The torso did not visibly fall: tilt={maximumTorsoTilt:F1}, drop={verticalDrop:F2}.");
            if (pose.IsOneLegRecoveryActive || recoveryEvents != 0)
                throw new InvalidOperationException("The wounded ragdoll incorrectly started one-leg standing recovery.");
            if (explosionEvents != 1)
                throw new InvalidOperationException($"The severed leg did not emit exactly one explosion event: {explosionEvents}.");
            if (!brokenLeg.Health.IsDepleted || brokenLeg.Body.transform.parent != null)
                throw new InvalidOperationException("The broken leg remained healthy or attached.");
            if (survivingLeg.DismemberableLimb.IsSevered || survivingLeg.Health.IsDepleted)
                throw new InvalidOperationException("The second leg was incorrectly broken by the response.");
            if (controller.CurrentHealth <= 0f || controller.CurrentHealth >= initialHealth)
                throw new InvalidOperationException("One broken leg incorrectly killed or failed to reduce the character health.");

            HingeJoint2D survivingJoint = survivingLeg.Hinges.Length > 0 ? survivingLeg.Hinges[0] : null;
            if (survivingJoint == null || survivingJoint.useMotor)
                throw new InvalidOperationException("The surviving leg incorrectly resumed its standing motor.");

            Debug.Log($"BROKEN_LEG_GROUNDED_PLAYMODE_OK: responseEvents=1, stumble=true, " +
                      $"torsoTilt={maximumTorsoTilt:F1}, torsoDrop={verticalDrop:F2}, " +
                      $"survivingLegMotor=false, recoveryEvents=0, limbExplosion=1, characterAlive=true.");
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }

        private static void HandleResponseStarted(Rigidbody2D body, int count)
        {
            if (brokenLeg != null && body == brokenLeg.Body && count == 1) responseEvents++;
        }

        private static void HandleRecoveryStarted() => recoveryEvents++;
        private static void HandleLegExploded(CracksModifier modifier, Vector2 point) => explosionEvents++;

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
            if (!success) Debug.LogError("BROKEN_LEG_RECOVERY_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ResetRuntimeState()
        {
            controller = null;
            pose = null;
            brokenLeg = null;
            survivingLeg = null;
            brokenLegCracks = null;
            torso = null;
            initialHealth = 0f;
            initialTorsoHeight = 0f;
            minimumTorsoHeight = 0f;
            maximumTorsoTilt = 0f;
            severedAt = 0f;
            stumbleObserved = false;
            responseEvents = 0;
            recoveryEvents = 0;
            explosionEvents = 0;
            stage = 0;
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
