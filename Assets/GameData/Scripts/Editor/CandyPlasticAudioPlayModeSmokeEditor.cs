#if UNITY_EDITOR
using System;
using KickTheBuddy.Audio;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Play Mode smoke for candy-plastic cue routing, pooling, and protected death audio.</summary>
    [InitializeOnLoad]
    public static class CandyPlasticAudioPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string ActiveKey = "KickTheBuddy.CandyPlasticAudioSmoke.Active";
        private const string SuccessKey = "KickTheBuddy.CandyPlasticAudioSmoke.Success";
        private const string FailureKey = "KickTheBuddy.CandyPlasticAudioSmoke.Failure";

        static CandyPlasticAudioPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Gameplay/Audio/Validate Candy-Plastic Doll Audio In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the audio smoke.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ActiveKey + ".Batch", batch);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(FailureKey, string.Empty);
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

            try
            {
                ExerciseAudio();
            }
            catch (Exception exception)
            {
                SessionState.SetString(FailureKey, exception.Message);
                SessionState.SetBool(SuccessKey, false);
                EditorApplication.isPlaying = false;
            }
        }

        private static void ExerciseAudio()
        {
            SoundManager sounds = UnityEngine.Object.FindObjectOfType<SoundManager>();
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (sounds == null || ragdoll == null ||
                SceneManager.GetActiveScene().name != "RagdollSandbox") return;

            GameplayAudioController audio = ragdoll.GetComponent<GameplayAudioController>();
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            RagdollController.RagdollPart torso = FindPart(ragdoll, RagdollPartType.Torso);
            RagdollController.RagdollPart head = FindPart(ragdoll, RagdollPartType.Head);
            if (audio == null || damage == null || torso?.Body == null || head?.Body == null ||
                head.Health == null)
                throw new InvalidOperationException("Active ragdoll audio references are incomplete.");

            int lightHits = 0;
            int combos = 0;
            int deaths = 0;
            int cannonFires = 0;
            void OnSound(GameSound cue)
            {
                if (cue == GameSound.HitLight) lightHits++;
                if (cue == GameSound.Combo || cue == GameSound.ComboHigh) combos++;
                if (cue == GameSound.DeathBlast) deaths++;
                if (cue == GameSound.CannonFire) cannonFires++;
            }

            sounds.SoundPlayed += OnSound;
            Vector2 point = torso.Body.worldCenterOfMass;
            for (int i = 0; i < 3; i++)
                if (!damage.ApplyDirectDamage(torso.Body, .7f, 8f, Vector2.zero, point))
                    throw new InvalidOperationException("Legitimate damage did not reach audio routing.");

            AudioPlaybackHandle cannon = sounds.PlaySfx(GameSound.CannonFire, point, .8f);
            if (!cannon.IsValid)
                throw new InvalidOperationException("Pooled cannon playback did not return a valid handle.");

            float lethal = head.Health.CurrentHealth /
                           Mathf.Max(.01f, head.Health.DamageRatio * damage.IncomingDamageMultiplier) + 1f;
            if (!damage.ApplyDirectDamage(head.Body, lethal, 24f, Vector2.up, head.Body.worldCenterOfMass))
                throw new InvalidOperationException("Critical head damage could not be applied.");

            sounds.SoundPlayed -= OnSound;
            if (lightHits != 1)
                throw new InvalidOperationException($"Expected one cooldown-bounded light hit, received {lightHits}.");
            if (combos < 1 || combos > 2)
                throw new InvalidOperationException(
                    $"Expected one or two bounded escalation cues, received {combos}.");
            if (cannonFires != 1)
                throw new InvalidOperationException($"Expected one cannon cue, received {cannonFires}.");
            if (deaths != 1)
                throw new InvalidOperationException($"Expected one protected death cue, received {deaths}.");

            Debug.Log("CANDY_PLASTIC_AUDIO_PLAYMODE_OK: hitCooldown=true, combo=true, cannonPool=true, " +
                      "deathPriority=true, controllers=authored.");
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }

        private static RagdollController.RagdollPart FindPart(
            RagdollController ragdoll, RagdollPartType type)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i] != null && ragdoll.Parts[i].PartType == type)
                    return ragdoll.Parts[i];
            return null;
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool(ActiveKey + ".Batch", false);
            string failure = SessionState.GetString(FailureKey, "Unknown failure.");
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!success) Debug.LogError("CANDY_PLASTIC_AUDIO_PLAYMODE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
