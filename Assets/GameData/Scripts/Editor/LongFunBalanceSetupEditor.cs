#if UNITY_EDITOR
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Applies a durable, high-feedback preset for longer sandbox sessions.</summary>
    public static class LongFunBalanceSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string LevelPath = "Assets/GameData/Materials/Gameplay/Level_01.asset";

        [MenuItem("Tools/Ragdoll/Apply Long Fun Balance")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController controller = FindSceneComponent<RagdollController>(scene);
            if (controller == null)
                throw new System.InvalidOperationException("RagdollController was not found.");

            ConfigureParts(controller);
            ConfigureRig(controller);
            ConfigureDamageManager(controller);
            ConfigureState(controller);
            ConfigureAnimations(controller);
            ConfigureRoomAttacks(scene);
            ConfigureLevel();

            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Applied Long Fun Balance: durable parts, safer head, gentler walls, stronger reactions, and a 150-second objective.");
        }

        private static void ConfigureParts(RagdollController controller)
        {
            RagdollPartHealth[] parts = controller.GetComponentsInChildren<RagdollPartHealth>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                RagdollPartHealth part = parts[i];
                string name = part.name.ToLowerInvariant();

                if (name.Contains("head"))
                    part.Configure(RagdollPartType.Head, 140f, 2f, .55f, .9f, 1.6f, true);
                else if (name.Contains("torso"))
                    part.Configure(RagdollPartType.Torso, 250f, 2f, .6f, 1f, 1.3f, false);
                else if (name.Contains("upper") && name.Contains("arm"))
                    part.Configure(RagdollPartType.Arm, 100f, 1f, .65f, 1.2f, 1.3f, false);
                else if (name.Contains("lower") && name.Contains("arm"))
                    part.Configure(RagdollPartType.Arm, 80f, 1f, .7f, 1.3f, 1.4f, false);
                else if (name.Contains("upper") && name.Contains("leg"))
                    part.Configure(RagdollPartType.Leg, 130f, 1f, .6f, .9f, 1.1f, false);
                else if (name.Contains("lower") && name.Contains("leg"))
                    part.Configure(RagdollPartType.Leg, 110f, 1f, .65f, 1f, 1.2f, false);
                else
                    part.Configure(RagdollPartType.Other, 100f, 1f, .65f, 1f, 1.2f, false);

                EditorUtility.SetDirty(part);
            }
        }

        private static void ConfigureRig(RagdollController controller)
        {
            RagdollRigController2D rig = controller.GetComponent<RagdollRigController2D>();
            if (rig == null) return;

            SerializedObject serialized = new SerializedObject(rig);
            Set(serialized, "enableLimbBreaking", true);
            Set(serialized, "fallbackLimbHealth", 130f);
            Set(serialized, "jointBreakStress", 900f);
            Set(serialized, "jointStressDamageRate", .006f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureDamageManager(RagdollController controller)
        {
            RagdollDamageManager damage = controller.GetComponent<RagdollDamageManager>();
            if (damage == null) return;

            SerializedObject serialized = new SerializedObject(damage);
            Set(serialized, "repeatHitCooldown", .15f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureState(RagdollController controller)
        {
            RagdollStateController2D state = controller.GetComponent<RagdollStateController2D>();
            if (state == null) return;

            SerializedObject serialized = new SerializedObject(state);
            Set(serialized, "reviveDelay", 2.5f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAnimations(RagdollController controller)
        {
            RagdollAnimationController animations = controller.GetComponent<RagdollAnimationController>();
            if (animations == null) return;

            SerializedObject serialized = new SerializedObject(animations);
            Set(serialized, "idleDelay", 2.5f);
            Set(serialized, "minimumBlinkInterval", 1.8f);
            Set(serialized, "maximumBlinkInterval", 4f);
            Set(serialized, "damageReactionDuration", .4f);
            Set(serialized, "tintStrength", .4f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureRoomAttacks(UnityEngine.SceneManagement.Scene scene)
        {
            RagdollAttackManager2D[] attacks = Resources.FindObjectsOfTypeAll<RagdollAttackManager2D>();
            for (int i = 0; i < attacks.Length; i++)
            {
                RagdollAttackManager2D attack = attacks[i];
                if (attack.gameObject.scene != scene) continue;
                if (attack.AttackType != RagdollAttackType.Wall) continue;

                attack.Configure(RagdollAttackType.Wall, 0f, .5f, 5.5f, 6f);
                EditorUtility.SetDirty(attack);
            }
        }

        private static void ConfigureLevel()
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelPath);
            if (level == null)
                throw new System.InvalidOperationException("Level_01 asset was not found.");

            SerializedObject serialized = new SerializedObject(level);
            Set(serialized, "targetDamage", 900f);
            Set(serialized, "timeLimit", 150f);
            Set(serialized, "completionCoins", 250);
            Set(serialized, "oneStarScore", 900);
            Set(serialized, "twoStarScore", 1400);
            Set(serialized, "threeStarScore", 2000);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static T FindSceneComponent<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
        {
            T[] components = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < components.Length; i++)
                if (components[i].gameObject.scene == scene)
                    return components[i];
            return null;
        }

        private static void Set(SerializedObject target, string property, float value)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field != null) field.floatValue = value;
        }

        private static void Set(SerializedObject target, string property, int value)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field != null) field.intValue = value;
        }

        private static void Set(SerializedObject target, string property, bool value)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field != null) field.boolValue = value;
        }
    }
}
#endif
