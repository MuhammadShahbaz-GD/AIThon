#if UNITY_EDITOR
using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    public static class AllLevelsFastDamageSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";
        private const float WallDamage = 120f;

        [MenuItem("Tools/Game/Balance/Apply Fast Maximum Damage To All Levels")]
        public static void ApplyFromMenu()
        {
            ApplyDefinitions();
            ApplyScene();
            AssetDatabase.SaveAssets();
            ValidateOrThrow();
            Debug.Log("ALL_LEVELS_FAST_MAX_DAMAGE_OK: every catalog level, wall, gun, cannon, pipe and reusable attack object now uses the fast maximum-damage profile.");
        }

        [MenuItem("Tools/Game/Balance/Validate Fast Maximum Damage On All Levels")]
        public static void ValidateFromMenu() => ValidateOrThrow();

        private static void ApplyDefinitions()
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Level Catalog is missing.");
            for (int i = 0; i < catalog.Count; i++)
            {
                LevelDefinition level = catalog.Get(i);
                if (level == null) throw new InvalidOperationException("Level Catalog contains an empty entry at index " + i + ".");
                Undo.RecordObject(level, "Apply fast maximum wall damage");
                SerializedObject data = new SerializedObject(level);
                data.FindProperty("wallBaseDamage").floatValue = WallDamage;
                data.FindProperty("wallDamagePerSpeed").floatValue = 0f;
                data.FindProperty("wallMinimumImpactSpeed").floatValue = 0f;
                data.FindProperty("wallMaximumDamage").floatValue = WallDamage;
                data.ApplyModifiedProperties();
                EditorUtility.SetDirty(level);
            }
        }

        private static void ApplyScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            GameplayLevelSceneController sceneController = null;
            RagdollAttackManager2D floorAttack = null;
            for (int i = 0; i < roots.Length; i++)
            {
                if (sceneController == null)
                    sceneController = roots[i].GetComponentInChildren<GameplayLevelSceneController>(true);
                RagdollAttackManager2D[] attacks = roots[i].GetComponentsInChildren<RagdollAttackManager2D>(true);
                for (int j = 0; j < attacks.Length; j++)
                {
                    ConfigureAttack(attacks[j]);
                    if (attacks[j] != null && IsFloor(attacks[j])) floorAttack = attacks[j];
                }

                SandboxTool2D[] tools = roots[i].GetComponentsInChildren<SandboxTool2D>(true);
                for (int j = 0; j < tools.Length; j++) ConfigureTool(tools[j]);

                LevelFourPipeController2D[] pipes = roots[i].GetComponentsInChildren<LevelFourPipeController2D>(true);
                for (int j = 0; j < pipes.Length; j++) ConfigurePipes(pipes[j]);
            }
            if (sceneController == null || floorAttack == null)
                throw new InvalidOperationException("The shared floor or Levels controller is missing.");
            Undo.RecordObject(sceneController, "Assign non-damaging shared floor");
            SerializedObject controllerData = new SerializedObject(sceneController);
            controllerData.FindProperty("sharedFloorAttack").objectReferenceValue = floorAttack;
            controllerData.ApplyModifiedProperties();
            EditorUtility.SetDirty(sceneController);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureAttack(RagdollAttackManager2D attack)
        {
            if (attack == null) return;
            Undo.RecordObject(attack, "Apply fast maximum attack damage");
            if (IsFloor(attack))
            {
                attack.Configure(RagdollAttackType.Wall, 0f, 0f, 0f, 0f);
                attack.SetDamageEnabled(false);
                EditorUtility.SetDirty(attack);
                return;
            }
            float damage = DamageFor(attack.AttackType);
            attack.Configure(attack.AttackType, damage, 0f, 0f, damage);
            EditorUtility.SetDirty(attack);
        }

        private static void ConfigureTool(SandboxTool2D tool)
        {
            if (tool == null || tool.Attack == null || tool.Attack.AttackType == RagdollAttackType.Jelly) return;
            Undo.RecordObject(tool, "Apply maximum ballistic damage");
            SerializedObject data = new SerializedObject(tool);
            data.FindProperty("ballisticBaseDamage").floatValue = 120f;
            data.FindProperty("ballisticDamagePerSpeed").floatValue = 0f;
            data.FindProperty("ballisticMaximumDamage").floatValue = 120f;
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(tool);
        }

        private static void ConfigurePipes(LevelFourPipeController2D pipes)
        {
            if (pipes == null) return;
            Undo.RecordObject(pipes, "Apply maximum pipe damage");
            SerializedObject data = new SerializedObject(pipes);
            data.FindProperty("bombBaseDamage").floatValue = 150f;
            data.FindProperty("bombDamagePerSpeed").floatValue = 0f;
            data.FindProperty("bombMaximumDamage").floatValue = 150f;
            data.FindProperty("bombBlastDamage").floatValue = 65f;
            data.FindProperty("sodaBaseDamage").floatValue = 75f;
            data.FindProperty("sodaDamagePerSpeed").floatValue = 0f;
            data.FindProperty("sodaMaximumDamage").floatValue = 75f;
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipes);
        }

        private static float DamageFor(RagdollAttackType type)
        {
            switch (type)
            {
                case RagdollAttackType.Jelly: return 0f;
                case RagdollAttackType.Wall: return WallDamage;
                case RagdollAttackType.Explosion: return 150f;
                case RagdollAttackType.Hammer:
                case RagdollAttackType.Lollipop:
                case RagdollAttackType.CandyStick:
                case RagdollAttackType.ChocolateBar: return 95f;
                case RagdollAttackType.GummyBear: return 80f;
                case RagdollAttackType.CandyJar:
                case RagdollAttackType.CandyProjectile: return 70f;
                case RagdollAttackType.Bullet: return 85f;
                default: return 85f;
            }
        }

        private static bool IsFloor(RagdollAttackManager2D attack) =>
            attack != null && string.Equals(attack.name, "Floor", StringComparison.OrdinalIgnoreCase);

        private static void ValidateOrThrow()
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null || catalog.Count == 0) throw new InvalidOperationException("Level Catalog is empty.");
            for (int i = 0; i < catalog.Count; i++)
            {
                LevelDefinition level = catalog.Get(i);
                if (level == null || !Mathf.Approximately(level.WallBaseDamage, WallDamage) ||
                    !Mathf.Approximately(level.WallDamagePerSpeed, 0f) ||
                    !Mathf.Approximately(level.WallMinimumImpactSpeed, 0f) ||
                    !Mathf.Approximately(level.WallMaximumDamage, WallDamage))
                    throw new InvalidOperationException("Fast wall damage is missing from catalog entry " + i + ".");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int attackCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                RagdollAttackManager2D[] attacks = roots[i].GetComponentsInChildren<RagdollAttackManager2D>(true);
                for (int j = 0; j < attacks.Length; j++)
                {
                    RagdollAttackManager2D attack = attacks[j];
                    if (attack == null) continue;
                    if (IsFloor(attack))
                    {
                        if (attack.DamageEnabled || !Mathf.Approximately(attack.CalculateDamage(100f), 0f))
                            throw new InvalidOperationException("The shared floor must remain non-damaging.");
                        attackCount++;
                        continue;
                    }
                    float expected = DamageFor(attack.AttackType);
                    if (!Mathf.Approximately(attack.CalculateDamage(0f), expected))
                        throw new InvalidOperationException(attack.name + " is not configured for maximum collision damage.");
                    attackCount++;
                }
            }
            if (attackCount == 0) throw new InvalidOperationException("No attack profiles were found in the gameplay scene.");
            Debug.Log("ALL_LEVELS_FAST_MAX_DAMAGE_VALIDATION_OK: " + catalog.Count +
                " levels and " + attackCount + " attack profiles passed.");
        }
    }
}
#endif
