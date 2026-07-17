#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Haptics;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Play Mode regression for hit-point VFX and scenario haptic routing.</summary>
    [InitializeOnLoad]
    public static class ImpactFeedbackPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-impact-feedback-smoke.json";
        private const string ActiveKey = "KickTheBuddy.ImpactFeedbackSmoke.Active";
        private const string StartKey = "KickTheBuddy.ImpactFeedbackSmoke.Start";
        private const string FailureKey = "KickTheBuddy.ImpactFeedbackSmoke.Failure";
        private const string SuccessKey = "KickTheBuddy.ImpactFeedbackSmoke.Success";

        static ImpactFeedbackPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Ragdoll/VFX/Validate Impact Feedback In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the impact feedback smoke.");
            ConfigureSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ActiveKey + ".Batch", batch);
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
            if (!EditorApplication.isPlaying || Time.frameCount < 3) return;
            if (Elapsed() > 25d)
            {
                Fail("Timed out waiting for the gameplay scene.");
                return;
            }

            try
            {
                ExerciseFeedback();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void ExerciseFeedback()
        {
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (ragdoll == null || SceneManager.GetActiveScene().name != "RagdollSandbox") return;
            RagdollVFXController vfx = ragdoll.GetComponent<RagdollVFXController>();
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            HapticsManager haptics = UnityEngine.Object.FindObjectOfType<HapticsManager>();
            CameraShake2D cameraShake = UnityEngine.Object.FindObjectOfType<CameraShake2D>();
            if (vfx == null || damage == null || haptics == null || cameraShake == null) return;

            ParticleSystem fumes = FindSharedParticle(vfx.transform, "Shared Collision Fumes");
            ParticleSystem glass = FindSharedParticle(vfx.transform, "Shared Impact Glass");
            RagdollController.RagdollPart torso = FindPart(ragdoll, RagdollPartType.Torso);
            RagdollController.RagdollPart head = FindPart(ragdoll, RagdollPartType.Head);
            if (fumes == null || glass == null || torso?.Body == null || head?.Body == null || head.Health == null)
                throw new InvalidOperationException("Active ragdoll impact feedback references are incomplete.");
            if (vfx.MinimumImpactGlassSize < .079f || vfx.LightImpactGlassMaximumSize < .159f ||
                vfx.HeavyImpactGlassMaximumSize < .239f)
                throw new InvalidOperationException("Impact glass size settings are too small to remain visible.");

            int comboCues = 0;
            int blastCues = 0;
            float strongestCombo = 0f;
            float strongestShake = 0f;
            float longestShake = 0f;
            Vector2 reportedPoint = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            void OnHaptic(GameHaptic cue, float intensity)
            {
                if (cue == GameHaptic.Combo) { comboCues++; strongestCombo = Mathf.Max(strongestCombo, intensity); }
                if (cue == GameHaptic.CharacterBlast) blastCues++;
            }
            void OnImpact(Vector2 point, float strength) => reportedPoint = point;
            void OnShake(float amplitude, float duration)
            {
                strongestShake = Mathf.Max(strongestShake, amplitude);
                longestShake = Mathf.Max(longestShake, duration);
            }
            haptics.Configure(true);
            haptics.HapticPlayed += OnHaptic;
            vfx.ImpactEffectPlayed += OnImpact;
            cameraShake.ShakeStarted += OnShake;

            Vector2 impactPoint = torso.Body.worldCenterOfMass + new Vector2(.12f, .08f);
            for (int i = 0; i < 3; i++)
            {
                if (!damage.ApplyDirectDamage(torso.Body, 1f, 12f, Vector2.zero, impactPoint))
                    throw new InvalidOperationException("Direct impact did not reach the legitimate damage event path.");
            }

            if (Vector2.Distance(reportedPoint, impactPoint) > .05f)
                throw new InvalidOperationException("Impact VFX did not use the actual damage point.");
            if (fumes.particleCount <= 0 || glass.particleCount <= 0)
                throw new InvalidOperationException("White fumes or falling glass did not emit on damage.");
            if (comboCues != 1 || strongestCombo < .75f)
                throw new InvalidOperationException("Combo milestone did not produce one strong haptic cue.");

            float lethalDamage = head.Health.CurrentHealth /
                                 Mathf.Max(.01f, head.Health.DamageRatio * damage.IncomingDamageMultiplier) + 1f;
            if (!damage.ApplyDirectDamage(head.Body, lethalDamage, 22f, Vector2.up * 4f, head.Body.worldCenterOfMass))
                throw new InvalidOperationException("Critical head damage was not applied.");
            if (blastCues != 1 || ragdoll.CurrentHealth > 0f)
                throw new InvalidOperationException("Character blast did not produce exactly one success haptic.");
            if (strongestShake < .5f || longestShake < .8f)
                throw new InvalidOperationException($"Death shake was too weak: amplitude={strongestShake:F2}, duration={longestShake:F2}.");

            haptics.HapticPlayed -= OnHaptic;
            vfx.ImpactEffectPlayed -= OnImpact;
            cameraShake.ShakeStarted -= OnShake;
            Debug.Log("IMPACT_FEEDBACK_PLAYMODE_OK: exactHitPoint=true, whiteFumes=true, fallingGlass=true, comboCue=" +
                      comboCues + ", comboIntensity=" + strongestCombo.ToString("F2", CultureInfo.InvariantCulture) +
                      ", characterBlastCue=" + blastCues + ", deathShake=" +
                      strongestShake.ToString("F2", CultureInfo.InvariantCulture) + "x" +
                      longestShake.ToString("F2", CultureInfo.InvariantCulture) + "s, crackSize=" +
                      vfx.MinimumImpactGlassSize.ToString("F2", CultureInfo.InvariantCulture) + "-" +
                      vfx.HeavyImpactGlassMaximumSize.ToString("F2", CultureInfo.InvariantCulture) + ".");
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }

        private static ParticleSystem FindSharedParticle(Transform root, string objectName)
        {
            Transform child = root.Find(objectName);
            return child != null ? child.GetComponent<ParticleSystem>() : null;
        }

        private static RagdollController.RagdollPart FindPart(RagdollController ragdoll, RagdollPartType type)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i] != null && ragdoll.Parts[i].PartType == type) return ragdoll.Parts[i];
            return null;
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
            bool batch = SessionState.GetBool(ActiveKey + ".Batch", false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            ConfigureSaveFile(DefaultSaveFile);
            DeleteSmokeSave();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!success) Debug.LogError("IMPACT_FEEDBACK_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
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
