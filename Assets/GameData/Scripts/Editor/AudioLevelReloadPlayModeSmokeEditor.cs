#if UNITY_EDITOR
using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Regression smoke proving that SFX remain bound to persistent services after a level reload.</summary>
    [InitializeOnLoad]
    public static class AudioLevelReloadPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string ActiveKey = "KickTheBuddy.AudioReloadSmoke.Active";
        private const string BatchKey = "KickTheBuddy.AudioReloadSmoke.Batch";
        private const string StageKey = "KickTheBuddy.AudioReloadSmoke.Stage";
        private const string OldControllerKey = "KickTheBuddy.AudioReloadSmoke.OldController";
        private const string SuccessKey = "KickTheBuddy.AudioReloadSmoke.Success";
        private const string FailureKey = "KickTheBuddy.AudioReloadSmoke.Failure";

        static AudioLevelReloadPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Gameplay/Audio/Validate Audio Across Level Reload")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the audio reload smoke.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(StageKey, 0);
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
            if (!EditorApplication.isPlaying || Time.frameCount < 5) return;

            try
            {
                if (SessionState.GetInt(StageKey, 0) == 0) BeginReload();
                else ValidateReloadedLevel();
            }
            catch (Exception exception)
            {
                SessionState.SetString(FailureKey, exception.Message);
                SessionState.SetBool(SuccessKey, false);
                EditorApplication.isPlaying = false;
            }
        }

        private static void BeginReload()
        {
            GameBootstrapper bootstrapper = GameBootstrapper.Instance;
            GameplayLevelSceneController sceneLevels = GameplayLevelSceneController.Active;
            if (bootstrapper == null || sceneLevels == null) return;
            SessionState.SetInt(OldControllerKey, sceneLevels.GetInstanceID());
            SessionState.SetInt(StageKey, 1);
            bootstrapper.Flow.ReloadCurrentLevel();
        }

        private static void ValidateReloadedLevel()
        {
            GameBootstrapper bootstrapper = GameBootstrapper.Instance;
            GameplayLevelSceneController sceneLevels = GameplayLevelSceneController.Active;
            if (bootstrapper == null || sceneLevels == null ||
                sceneLevels.GetInstanceID() == SessionState.GetInt(OldControllerKey, 0) ||
                bootstrapper.Gameplay.State != GameplayState.Playing) return;

            RagdollController ragdoll = sceneLevels.ActiveRagdoll;
            RagdollDamageManager damage = ragdoll != null ? ragdoll.GetComponent<RagdollDamageManager>() : null;
            RagdollController.RagdollPart head = FindPart(ragdoll, RagdollPartType.Head);
            if (ragdoll == null || damage == null || head?.Body == null || head.Health == null)
                throw new InvalidOperationException("Reloaded level has incomplete ragdoll audio test references.");

            int impacts = 0;
            int explosions = 0;
            void Count(GameSound cue)
            {
                if (cue == GameSound.HitLight || cue == GameSound.HitMedium || cue == GameSound.HitHeavy) impacts++;
                if (cue == GameSound.DeathBlast) explosions++;
            }

            bootstrapper.Sounds.SoundPlayed += Count;
            RagdollController.RagdollPart torso = FindPart(ragdoll, RagdollPartType.Torso);
            if (torso?.Body == null || !damage.ApplyDirectDamage(
                    torso.Body, .7f, 8f, Vector2.zero, torso.Body.worldCenterOfMass))
                throw new InvalidOperationException("Reloaded-level impact could not be applied.");
            float lethal = head.Health.CurrentHealth /
                Mathf.Max(.01f, head.Health.DamageRatio * damage.IncomingDamageMultiplier) + 1f;
            if (!damage.ApplyDirectDamage(head.Body, lethal, 24f, Vector2.up, head.Body.worldCenterOfMass))
                throw new InvalidOperationException("Reloaded-level death could not be applied.");
            bootstrapper.Sounds.SoundPlayed -= Count;

            if (impacts < 1)
                throw new InvalidOperationException("Impact SFX did not reach the persistent SoundManager after reload.");
            if (explosions != 1)
                throw new InvalidOperationException(
                    $"Expected one ragdoll explosion after reload, received {explosions}.");

            Debug.Log("AUDIO_LEVEL_RELOAD_OK: impactSfx=true, ragdollExplosion=true, persistentRebind=true.");
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }

        private static RagdollController.RagdollPart FindPart(RagdollController ragdoll, RagdollPartType type)
        {
            if (ragdoll == null) return null;
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i] != null && ragdoll.Parts[i].PartType == type)
                    return ragdoll.Parts[i];
            return null;
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool(BatchKey, false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.OpenScene(ScenePath);
            if (!success) Debug.LogError("AUDIO_LEVEL_RELOAD_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
