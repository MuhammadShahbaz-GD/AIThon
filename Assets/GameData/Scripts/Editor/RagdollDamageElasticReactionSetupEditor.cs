#if UNITY_EDITOR
using System;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Applies and validates the recommended hit-driven elastic pose settings.</summary>
    public static class RagdollDamageElasticReactionSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        [MenuItem("Tools/Ragdoll/Apply Damage Elastic Reaction")]
        public static void ApplyFromMenu() => Apply(false);

        public static void ApplySandboxBatch() => Apply(true);

        public static void ValidateSandboxBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                RagdollPoseController2D[] poses = UnityEngine.Object.FindObjectsOfType<RagdollPoseController2D>(true);
                if (poses.Length == 0) throw new InvalidOperationException("No RagdollPoseController2D exists in RagdollSandbox.");
                for (int i = 0; i < poses.Length; i++)
                {
                    SerializedObject data = new SerializedObject(poses[i]);
                    if (!data.FindProperty("enableDamageElasticReaction").boolValue)
                        throw new InvalidOperationException(poses[i].name + " has damage elasticity disabled.");
                    if (data.FindProperty("minimumMotorStrength").floatValue >= 1f)
                        throw new InvalidOperationException(poses[i].name + " does not relax its joint motors.");
                    if (data.FindProperty("elasticRecoveryDuration").floatValue <= 0f)
                        throw new InvalidOperationException(poses[i].name + " has no elastic recovery duration.");
                }
                Debug.Log("RAGDOLL_DAMAGE_ELASTIC_REACTION_VALIDATION_OK: all active ragdolls have temporary whole-body joint relaxation.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Apply(bool exitWhenDone)
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                RagdollPoseController2D[] poses = UnityEngine.Object.FindObjectsOfType<RagdollPoseController2D>(true);
                if (poses.Length == 0) throw new InvalidOperationException("No RagdollPoseController2D exists in RagdollSandbox.");

                for (int i = 0; i < poses.Length; i++)
                {
                    SerializedObject data = new SerializedObject(poses[i]);
                    Set(data, "enableDamageElasticReaction", true);
                    Set(data, "minimumDamageReaction", .32f);
                    Set(data, "damageForMaximumReaction", 16f);
                    Set(data, "speedForMaximumReaction", 14f);
                    Set(data, "looseHoldDuration", .32f);
                    Set(data, "elasticRecoveryDuration", .95f);
                    Set(data, "minimumMotorStrength", .07f);
                    Set(data, "minimumBalanceStrength", .08f);
                    Set(data, "minimumHeadStrength", .15f);
                    Set(data, "springReboundAngle", 6.5f);
                    Set(data, "springReboundFrequency", 3.4f);
                    Set(data, "healthBasedLooseness", .24f);
                    data.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(poses[i]);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("RAGDOLL_DAMAGE_ELASTIC_REACTION_SETUP_OK: inspector-authored hit relaxation applied to " + poses.Length + " ragdoll(s).");
                if (exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Set(SerializedObject data, string name, bool value)
        {
            SerializedProperty property = data.FindProperty(name);
            if (property == null) throw new InvalidOperationException("Missing serialized property: " + name);
            property.boolValue = value;
        }

        private static void Set(SerializedObject data, string name, float value)
        {
            SerializedProperty property = data.FindProperty(name);
            if (property == null) throw new InvalidOperationException("Missing serialized property: " + name);
            property.floatValue = value;
        }
    }
}
#endif
