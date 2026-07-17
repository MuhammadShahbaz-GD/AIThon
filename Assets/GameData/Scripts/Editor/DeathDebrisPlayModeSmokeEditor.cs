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
    /// <summary>Isolated Play Mode smoke for the pooled torso-to-floor death burst.</summary>
    [InitializeOnLoad]
    public static class DeathDebrisPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-vfx-smoke.json";
        private const string ActiveKey = "KickTheBuddy.DeathDebrisSmoke.Active";
        private const string StageKey = "KickTheBuddy.DeathDebrisSmoke.Stage";
        private const string StartKey = "KickTheBuddy.DeathDebrisSmoke.Start";
        private const string BurstKey = "KickTheBuddy.DeathDebrisSmoke.Burst";
        private const string FailureKey = "KickTheBuddy.DeathDebrisSmoke.Failure";
        private const int StageTrigger = 0;
        private const int StageMeasure = 1;
        private const int StageSucceeded = 2;
        private const int StageFailed = -1;

        static DeathDebrisPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Ragdoll/VFX/Validate Torso Floor Burst In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before running the debris smoke.");
            ConfigureSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ActiveKey + ".Batch", batch);
            SessionState.SetInt(StageKey, StageTrigger);
            SessionState.SetString(FailureKey, string.Empty);
            SessionState.SetString(StartKey, Now());
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
            if (!SessionState.GetBool(ActiveKey, false)) { EditorApplication.update -= Tick; return; }
            int stage = SessionState.GetInt(StageKey, StageTrigger);
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (stage == StageSucceeded) Finish(true);
                else if (stage == StageFailed) Finish(false);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (Elapsed(StartKey) > 35d) { Fail("Timed out at stage " + stage + "."); return; }

            try
            {
                if (stage == StageTrigger) TriggerBurst();
                else if (stage == StageMeasure && Elapsed(BurstKey) >= 1.8d) MeasureBurst();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void TriggerBurst()
        {
            if (SceneManager.GetActiveScene().name != "RagdollSandbox") return;
            GameBootstrapper root = GameBootstrapper.Instance;
            if (root == null || root.Gameplay.State != GameplayState.Playing) return;
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            RagdollVFXController vfx = ragdoll != null ? ragdoll.GetComponent<RagdollVFXController>() : null;
            Transform pool = ragdoll != null ? ragdoll.transform.Find("VFX Death Debris Pool") : null;
            if (ragdoll == null || vfx == null || pool == null) throw new InvalidOperationException("Ragdoll death VFX pool is missing in Play Mode.");

            Vector2 origin = ResolveTorsoCenter(ragdoll);
            DisableRagdollPhysicsLikeDeathState(ragdoll);
            vfx.SendMessage("HandleDeath", origin, SendMessageOptions.RequireReceiver);
            if (vfx.transform.Find("Shared Candy Burst") == null)
                throw new InvalidOperationException("The shared candy-sprite particle burst was not created.");
            Rigidbody2D[] bodies = pool.GetComponentsInChildren<Rigidbody2D>(true);
            int active = 0;
            for (int i = 0; i < bodies.Length; i++)
                if (bodies[i] != null && bodies[i].gameObject.activeSelf && bodies[i].simulated) active++;
            if (active != vfx.ExpectedActiveDeathDebris)
                throw new InvalidOperationException("Burst activated " + active + "/" + vfx.ExpectedActiveDeathDebris + " pooled bodies.");
            SessionState.SetString(BurstKey, Now());
            SessionState.SetInt(StageKey, StageMeasure);
        }

        private static void MeasureBurst()
        {
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            Transform pool = ragdoll != null ? ragdoll.transform.Find("VFX Death Debris Pool") : null;
            Camera camera = Camera.main;
            Collider2D floor = FindCollider("Floor");
            RagdollVFXController vfx = ragdoll != null ? ragdoll.GetComponent<RagdollVFXController>() : null;
            if (pool == null || camera == null || floor == null || vfx == null)
                throw new InvalidOperationException("Burst measurement references are missing.");

            Rigidbody2D[] bodies = pool.GetComponentsInChildren<Rigidbody2D>(true);
            float halfWidth = camera.orthographicSize * camera.aspect;
            float left = camera.transform.position.x - halfWidth;
            float right = camera.transform.position.x + halfWidth;
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            int candyVisible = 0;
            int candyInside = 0;
            int candyOnFloor = 0;
            int glassActive = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                if (body == null) continue;
                bool candy = body.name.StartsWith("Candy Debris", StringComparison.Ordinal);
                if (!candy)
                {
                    if (body.gameObject.activeSelf && body.simulated) glassActive++;
                    continue;
                }

                SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
                if (body.gameObject.activeSelf && body.simulated && renderer != null && renderer.enabled) candyVisible++;
                minimumX = Mathf.Min(minimumX, body.position.x);
                maximumX = Mathf.Max(maximumX, body.position.x);
                if (body.position.x >= left && body.position.x <= right) candyInside++;
                if (body.position.y <= floor.bounds.max.y + .85f) candyOnFloor++;
            }

            float spread = maximumX - minimumX;
            int candyParticles = vfx.ActiveCandyBurstParticles;
            int expectedGlass = vfx.ExpectedActiveDeathDebris - 24;
            if (candyVisible != 24 || glassActive != expectedGlass || candyParticles < 20 ||
                candyInside < 22 || candyOnFloor < 18 || spread < 3.5f)
                throw new InvalidOperationException($"visibleCandy={candyVisible}/24, candyParticles={candyParticles}, glass={glassActive}/{expectedGlass}, inside={candyInside}/24, floor={candyOnFloor}/24, spread={spread:F2}.");

            Debug.Log($"TORSO_FLOOR_DEATH_BURST_SMOKE_OK: 24 physical candies, {candyParticles} gravity candy particles, {expectedGlass} glass/springs, {candyInside} candies in camera, {candyOnFloor} on floor, spread={spread:F2}.");
            SessionState.SetInt(StageKey, StageSucceeded);
            EditorApplication.isPlaying = false;
        }

        private static Vector2 ResolveTorsoCenter(RagdollController ragdoll)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i].PartType == RagdollPartType.Torso && ragdoll.Parts[i].Body != null)
                    return ragdoll.Parts[i].Body.worldCenterOfMass;
            return ragdoll.transform.position;
        }

        private static void DisableRagdollPhysicsLikeDeathState(RagdollController ragdoll)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = ragdoll.Parts[i];
                for (int c = 0; c < part.Colliders.Length; c++)
                    if (part.Colliders[c] != null) part.Colliders[c].enabled = false;
                if (part.Body == null) continue;
                part.Body.velocity = Vector2.zero;
                part.Body.angularVelocity = 0f;
                part.Body.simulated = false;
            }
        }

        private static Collider2D FindCollider(string objectName)
        {
            Collider2D[] colliders = UnityEngine.Object.FindObjectsOfType<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].name == objectName) return colliders[i];
            return null;
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
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!success) Debug.LogError("TORSO_FLOOR_DEATH_BURST_SMOKE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ConfigureSaveFile(string fileName)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameSaveManager saves = UnityEngine.Object.FindObjectOfType<GameSaveManager>(true);
            if (saves == null) throw new InvalidOperationException("Gameplay GameSaveManager is missing.");
            SerializedObject data = new SerializedObject(saves);
            data.FindProperty("fileName").stringValue = fileName;
            data.ApplyModifiedPropertiesWithoutUndo();
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

        private static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
        private static double Elapsed(string key)
        {
            double start;
            return double.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out start)
                ? EditorApplication.timeSinceStartup - start
                : 0d;
        }
    }
}
#endif
