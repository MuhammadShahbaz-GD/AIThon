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
    /// <summary>Isolated Play Mode regression for tool damage ownership and jelly attach/release.</summary>
    [InitializeOnLoad]
    public static class TwoLevelCandyGameplayPlayModeSmokeEditor
    {
        private const string ScenePath = SingleSceneLevelsSetupEditor.GameplayScenePath;
        private const string DefaultSaveFile = "player-progress.json";
        private const string SmokeSaveFile = "player-progress-candy-tools-smoke.json";
        private const string ActiveKey = "KickTheBuddy.CandyToolSmoke.Active";
        private const string StageKey = "KickTheBuddy.CandyToolSmoke.Stage";
        private const string StartKey = "KickTheBuddy.CandyToolSmoke.Start";
        private const string DetachKey = "KickTheBuddy.CandyToolSmoke.Detach";
        private const string FailureKey = "KickTheBuddy.CandyToolSmoke.Failure";
        private const string LollipopDamageKey = "KickTheBuddy.CandyToolSmoke.LollipopDamage";
        private const string JellyDamageKey = "KickTheBuddy.CandyToolSmoke.JellyDamage";
        private const int StageExercise = 0;
        private const int StageWaitForDetach = 1;
        private const int StageSucceeded = 2;
        private const int StageFailed = -1;

        static TwoLevelCandyGameplayPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Game/Validate Level 2 Tools In Play Mode")]
        public static void RunFromMenu() => Run(false);

        public static void RunBatch() => Run(true);

        private static void Run(bool batch)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the Level 2 tool smoke.");
            ConfigureSaveFile(SmokeSaveFile);
            DeleteSmokeSave();
            CreateLevelTwoSmokeSave();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ActiveKey + ".Batch", batch);
            SessionState.SetInt(StageKey, StageExercise);
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
            int stage = SessionState.GetInt(StageKey, StageExercise);
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (stage == StageSucceeded) Finish(true);
                else if (stage == StageFailed) Finish(false);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (Elapsed(StartKey) > 25d) { Fail("Timed out at stage " + stage + "."); return; }

            try
            {
                if (stage == StageExercise) ExerciseTools();
                else if (stage == StageWaitForDetach && Elapsed(DetachKey) >= 1.85d) VerifyDetach();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void ExerciseTools()
        {
            if (SceneManager.GetActiveScene().name != "RagdollSandbox") return;
            GameplayLevelSceneController sceneLevels = GameplayLevelSceneController.Active;
            if (sceneLevels == null || sceneLevels.ActiveLevelId != "level_02" ||
                sceneLevels.ActiveLevelIndex != 1 || sceneLevels.ActiveLevelRoot == null ||
                !sceneLevels.ActiveLevelRoot.activeInHierarchy)
                return;
            SandboxToolInput2D input = UnityEngine.Object.FindObjectOfType<SandboxToolInput2D>();
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (input == null || ragdoll == null || input.Tools.Count != 2) return;
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            if (damage == null) throw new InvalidOperationException("RagdollDamageManager is missing in Level 2.");

            SandboxTool2D lollipop = FindTool(input, SandboxToolKind.Lollipop);
            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            RagdollController.RagdollPart head = FindPart(ragdoll, RagdollPartType.Head);
            RagdollController.RagdollPart torso = FindPart(ragdoll, RagdollPartType.Torso);
            if (lollipop == null || jelly == null || head == null || torso == null || head.Body == null || head.Health == null || torso.Health == null)
                throw new InvalidOperationException("Level 2 tool or authored ragdoll part references are incomplete.");

            float lollipopDamage = lollipop.Attack.CalculateDamage(8f);
            float jellyDamage = jelly.Attack.CalculateDamage(8f);
            if (lollipopDamage <= 0f || jellyDamage != 0f ||
                jelly.Attack.CalculateDamage(0f) != 0f || jelly.Attack.CalculateDamage(100f) != 0f)
                throw new InvalidOperationException("Jelly did not preserve its zero-damage invariant in Play Mode.");

            float headBefore = head.Health.CurrentHealth;
            float torsoBefore = torso.Health.CurrentHealth;
            bool applied = damage.ApplyAttack(head.Body, lollipop.Attack, 8f, Vector2.left * 8f, head.Body.worldCenterOfMass);
            if (!applied || head.Health.CurrentHealth >= headBefore)
                throw new InvalidOperationException("Lollipop did not damage the actual head body.");
            if (!Mathf.Approximately(torso.Health.CurrentHealth, torsoBefore))
                throw new InvalidOperationException("Lollipop head hit incorrectly damaged another body part.");

            float headAfterLollipop = head.Health.CurrentHealth;
            bool jellyAppliedDamage = damage.ApplyAttack(
                head.Body, jelly.Attack, 100f, Vector2.left * 100f, head.Body.worldCenterOfMass);
            if (jellyAppliedDamage || !Mathf.Approximately(head.Health.CurrentHealth, headAfterLollipop))
                throw new InvalidOperationException("Jelly entered the health pipeline at high speed.");

            JellyContactVFXController liquid = UnityEngine.Object.FindObjectOfType<JellyContactVFXController>();
            RagdollAnimationController animation = ragdoll.GetComponent<RagdollAnimationController>();
            if (liquid == null || animation == null)
                throw new InvalidOperationException("Jelly liquid or annoyed reaction presenter is missing.");
            bool annoyedEvent = false;
            Action<Rigidbody2D, float> annoyedHandler = (part, strength) =>
                annoyedEvent = part == head.Body && strength > 0f;
            animation.AnnoyedReactionPlayed += annoyedHandler;
            bool liquidPlayed = liquid.TryPlay(jelly, head.Body, 8f, head.Body.worldCenterOfMass);
            animation.AnnoyedReactionPlayed -= annoyedHandler;
            if (!liquidPlayed || liquid.ActiveEffectCount <= 0 || !annoyedEvent ||
                Vector2.Distance(liquid.LastContactPoint, head.Body.worldCenterOfMass) > .01f)
                throw new InvalidOperationException("Jelly did not play its pooled contact liquid and annoyed reaction at the hit point.");

            jelly.Body.position = head.Body.worldCenterOfMass + Vector2.right * .25f;
            jelly.Body.velocity = Vector2.zero;
            jelly.Body.angularVelocity = 0f;
            Physics2D.SyncTransforms();
            if (!jelly.TryStickTo(head.Body, head.Body.worldCenterOfMass, 8f) || !jelly.IsStuck)
                throw new InvalidOperationException("Jelly did not enter its temporary sticky state.");

            SessionState.SetString(LollipopDamageKey, lollipopDamage.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(JellyDamageKey, jellyDamage.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(DetachKey, Now());
            SessionState.SetInt(StageKey, StageWaitForDetach);
        }

        private static void VerifyDetach()
        {
            SandboxToolInput2D input = UnityEngine.Object.FindObjectOfType<SandboxToolInput2D>();
            SandboxTool2D jelly = input != null ? FindTool(input, SandboxToolKind.Jelly) : null;
            if (jelly == null) throw new InvalidOperationException("Jelly disappeared before detach validation.");
            if (jelly.IsStuck) throw new InvalidOperationException("Jelly stayed attached beyond its configured brief stick duration.");
            if (jelly.Body.gravityScale <= 0f) throw new InvalidOperationException("Detached jelly cannot fall and slide because gravity is disabled.");
            JellyContactVFXController liquid = UnityEngine.Object.FindObjectOfType<JellyContactVFXController>();
            if (liquid == null || liquid.CompletedEffectCount <= 0 || liquid.ActiveEffectCount > 6)
                throw new InvalidOperationException("Jelly liquid did not complete/recycle cleanly within its fixed six-splat pool.");

            string lollipopDamage = SessionState.GetString(LollipopDamageKey, "?");
            string jellyDamage = SessionState.GetString(JellyDamageKey, "?");
            Debug.Log("TWO_LEVEL_CANDY_PLAYMODE_SMOKE_OK: lollipopDamage=" + lollipopDamage +
                      ", jellyDamage=" + jellyDamage +
                      ", hitPartOnly=true, liquidSlideFade=true, annoyedReaction=true, jellyAttachedThenReleased=true, gravitySlide=true.");
            SessionState.SetInt(StageKey, StageSucceeded);
            EditorApplication.isPlaying = false;
        }

        private static SandboxTool2D FindTool(SandboxToolInput2D input, SandboxToolKind kind)
        {
            for (int i = 0; i < input.Tools.Count; i++)
                if (input.Tools[i] != null && input.Tools[i].Kind == kind) return input.Tools[i];
            return null;
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
            if (!success) Debug.LogError("TWO_LEVEL_CANDY_PLAYMODE_SMOKE_FAILED: " + failure);
            if (batch) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ConfigureSaveFile(string fileName)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameSaveManager saves = UnityEngine.Object.FindObjectOfType<GameSaveManager>(true);
            if (saves == null) throw new InvalidOperationException("The gameplay scene GameSaveManager is missing.");
            SerializedObject data = new SerializedObject(saves);
            data.FindProperty("fileName").stringValue = fileName;
            data.ApplyModifiedPropertiesWithoutUndo();
            Camera camera = UnityEngine.Object.FindObjectOfType<Camera>(true);
            if (camera != null) camera.orthographicSize = 5.25f;
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

        private static void CreateLevelTwoSmokeSave()
        {
            PlayerProgressData progress = new PlayerProgressData
            {
                version = 2,
                highestUnlockedLevel = 1,
                selectedLevel = 1,
                hasStartedGame = true
            };
            File.WriteAllText(Path.Combine(Application.persistentDataPath, SmokeSaveFile),
                JsonUtility.ToJson(progress, true));
        }

        private static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);

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
