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
    /// <summary>
    /// Authors the two-level durability curve and broken-leg response without changing
    /// mass, gravity, drag, healthy joint tuning, profiles, or input settings.
    /// </summary>
    public static class LongFunBalanceSetupEditor
    {
        private const string LevelOneScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string LevelTwoScenePath = "Assets/GameData/Scene/CandyLab.unity";
        private const string LevelOneAssetPath = "Assets/GameData/Materials/Gameplay/Level_01.asset";
        private const string LevelTwoAssetPath = "Assets/GameData/Materials/Gameplay/Level_02.asset";
        private const string LollipopPrefabPath = "Assets/GameData/Prefabs/Gameplay/Lollipop.prefab";
        private const string JellyPrefabPath = "Assets/GameData/Prefabs/Gameplay/Jelly.prefab";

        public const float LevelPlayTime = 75f;
        public const float HeadHealth = 500f;
        public const float TorsoHealth = 650f;
        public const float ArmHealth = 300f;
        public const float LegHealth = 360f;
        public const float HeadDamageRatio = .75f;
        public const float MaximumRawDamagePerHit = 16f;
        public const float IncomingDamageMultiplier = .65f;
        public const float RepeatHitCooldown = .45f;
        public const int MinimumCriticalHits = 64;
        public const float HeadMaximumAnchorSeparation = .65f;
        public const float ArmMaximumAnchorSeparation = .9f;
        public const float LegMaximumAnchorSeparation = 1.05f;
        public const int DistanceBreakFixedSteps = 4;
        public const float DistanceBreakImpulse = 1.5f;
        public const float BrokenLegFallDuration = .8f;
        public const float BrokenLegRecoveryDelay = .45f;
        public const float BrokenLegFallTorque = 32f;
        public const float OneLegMotorStrength = .55f;
        public const float OneLegBalanceStrength = .35f;
        public const float OneLegLiftStrength = .12f;
        public const float OneLegHeadStrength = .75f;

        [MenuItem("Tools/Ragdoll/Apply 40-45 Second Level Balance")]
        public static void Install() => Apply(false);

        [MenuItem("Tools/Ragdoll/Validate 40-45 Second Level Balance")]
        public static void ValidateFromMenu() => Validate(false);

        public static void InstallBatch() => Apply(true);
        public static void ValidateBatch() => Validate(true);

        /// <summary>Called by the level builder so rebuilt content retains the same pacing.</summary>
        public static void ApplyForLevelBuild() => Apply(false);

        private static void Apply(bool exitWhenDone)
        {
            try
            {
                ConfigureScene(LevelOneScenePath, 0f, 1.25f, 4f, MaximumRawDamagePerHit);
                ConfigureScene(LevelTwoScenePath, 0f, .85f, 4.5f, 9f);
                ConfigureLollipop();
                ConfigureLevel(LevelOneAssetPath, LevelPlayTime, HeadHealth,
                    "Repeatedly throw the character into the walls until the glass finally breaks.");
                ConfigureLevel(LevelTwoAssetPath, LevelPlayTime, HeadHealth,
                    "Use repeated hard lollipop hits to break the character; sticky jelly only annoys it.");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateInternal();
                Debug.Log("FORTY_SECOND_LEVEL_BALANCE_OK: 75-second timers, 35% global damage reduction, drag-safe limbs, and a permanently grounded one-leg state.");
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Validate(bool exitWhenDone)
        {
            try
            {
                ValidateInternal();
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void ConfigureScene(string scenePath, float baseDamage, float speedDamage,
            float minimumSpeed, float damageCap)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RagdollController controller = FindAuthoredController(scene);
            if (controller == null)
                throw new InvalidOperationException(scene.name + " has no active controller with six authored main parts.");

            ConfigureAuthoredParts(controller);
            ConfigureBrokenLegRecovery(controller);
            ConfigureDamageManager(controller);
            ConfigureWalls(scene, baseDamage, speedDamage, minimumSpeed, damageCap);
            RestoreAuthoredCameraSize(scene);

            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureAuthoredParts(RagdollController controller)
        {
            RagdollRigController2D rig = controller.GetComponent<RagdollRigController2D>();
            SerializedProperty parts = new SerializedObject(rig).FindProperty("authoredParts");
            if (parts == null || parts.arraySize != 6)
                throw new InvalidOperationException(controller.name + " must contain exactly six authored main parts.");

            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty item = parts.GetArrayElementAtIndex(i);
                RagdollPartType type = (RagdollPartType)item.FindPropertyRelative("partType").enumValueIndex;
                RagdollPartHealth health = item.FindPropertyRelative("health").objectReferenceValue as RagdollPartHealth;
                DismemberableLimb structural = item.FindPropertyRelative("dismemberableLimb").objectReferenceValue as DismemberableLimb;
                if (health == null)
                    throw new InvalidOperationException(controller.name + " authored part " + i + " is missing RagdollPartHealth.");

                float maximumHealth = HealthFor(type);
                float damageRatio = DamageRatioFor(type);
                health.Configure(type, maximumHealth, 1f, damageRatio,
                    health.Flexibility, health.DamageReactionStrength, type == RagdollPartType.Head);
                EditorUtility.SetDirty(health);

                if (structural == null) continue;
                SerializedObject structuralData = new SerializedObject(structural);
                Set(structuralData, "jointHealth", maximumHealth);
                Set(structuralData, "canBeSevered", true);
                Set(structuralData, "damageFromJointStress", false);
                structuralData.ApplyModifiedPropertiesWithoutUndo();
                structural.ConfigureDistanceBreak(type != RagdollPartType.Torso,
                    MaximumAnchorSeparationFor(type), DistanceBreakFixedSteps, DistanceBreakImpulse, false);
                EditorUtility.SetDirty(structural);
            }
        }

        private static void ConfigureDamageManager(RagdollController controller)
        {
            RagdollDamageManager damageManager = controller.GetComponent<RagdollDamageManager>();
            if (damageManager == null) throw new InvalidOperationException(controller.name + " is missing RagdollDamageManager.");
            SerializedObject data = new SerializedObject(damageManager);
            Set(data, "incomingDamageMultiplier", IncomingDamageMultiplier);
            Set(data, "repeatHitCooldown", RepeatHitCooldown);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(damageManager);
        }

        private static void ConfigureBrokenLegRecovery(RagdollController controller)
        {
            RagdollPoseController2D pose = controller.GetComponent<RagdollPoseController2D>();
            if (pose == null) throw new InvalidOperationException(controller.name + " is missing RagdollPoseController2D.");
            SerializedObject data = new SerializedObject(pose);
            Set(data, "reactToBrokenLeg", true);
            Set(data, "allowOneLegRecovery", false);
            Set(data, "brokenLegFallDuration", BrokenLegFallDuration);
            Set(data, "brokenLegRecoveryDelay", BrokenLegRecoveryDelay);
            Set(data, "brokenLegFallTorque", BrokenLegFallTorque);
            Set(data, "oneLegMotorStrength", OneLegMotorStrength);
            Set(data, "oneLegBalanceStrength", OneLegBalanceStrength);
            Set(data, "oneLegLiftStrength", OneLegLiftStrength);
            Set(data, "oneLegHeadStrength", OneLegHeadStrength);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pose);
        }

        private static void ConfigureWalls(Scene scene, float baseDamage, float speedDamage,
            float minimumSpeed, float damageCap)
        {
            GameObject room = FindRoot(scene, "Room");
            if (room == null) throw new InvalidOperationException(scene.name + " is missing Room.");
            Collider2D[] boundaries = room.GetComponentsInChildren<Collider2D>(true);
            if (boundaries.Length == 0) throw new InvalidOperationException(scene.name + " has no room boundaries.");

            for (int i = 0; i < boundaries.Length; i++)
            {
                RagdollAttackManager2D attack = boundaries[i].GetComponent<RagdollAttackManager2D>();
                if (attack == null) attack = boundaries[i].gameObject.AddComponent<RagdollAttackManager2D>();
                attack.Configure(RagdollAttackType.Wall, baseDamage, speedDamage, minimumSpeed, damageCap);
                EditorUtility.SetDirty(attack);
            }
        }

        private static void ConfigureLollipop()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LollipopPrefabPath);
            if (prefab == null) throw new InvalidOperationException("Lollipop prefab is missing.");
            RagdollAttackManager2D attack = prefab.GetComponent<RagdollAttackManager2D>();
            if (attack == null) throw new InvalidOperationException("Lollipop prefab is missing its attack manager.");
            attack.Configure(RagdollAttackType.Lollipop, 3f, 1.4f, 3f, MaximumRawDamagePerHit);
            EditorUtility.SetDirty(attack);
            EditorUtility.SetDirty(prefab);
        }

        private static void ConfigureLevel(string assetPath, float timeLimit, float targetDamage, string objective)
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (level == null) throw new InvalidOperationException("Missing level definition: " + assetPath);
            SerializedObject data = new SerializedObject(level);
            Set(data, "timeLimit", timeLimit);
            Set(data, "targetDamage", targetDamage);
            SerializedProperty text = data.FindProperty("objectiveText");
            if (text != null) text.stringValue = objective;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static void ValidateInternal()
        {
            ValidateLevel(LevelOneAssetPath);
            ValidateLevel(LevelTwoAssetPath);
            ValidateScene(LevelOneScenePath, 0f, 1.25f, 4f, MaximumRawDamagePerHit);
            ValidateScene(LevelTwoScenePath, 0f, .85f, 4.5f, 9f);

            GameObject lollipopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LollipopPrefabPath);
            RagdollAttackManager2D lollipop = lollipopPrefab != null
                ? lollipopPrefab.GetComponent<RagdollAttackManager2D>()
                : null;
            if (lollipop == null || lollipop.AttackType != RagdollAttackType.Lollipop)
                throw new InvalidOperationException("Lollipop attack is missing.");
            ValidateAttack(lollipop, 3f, 1.4f, 3f, MaximumRawDamagePerHit, "Lollipop");

            GameObject jellyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JellyPrefabPath);
            RagdollAttackManager2D jelly = jellyPrefab != null
                ? jellyPrefab.GetComponent<RagdollAttackManager2D>()
                : null;
            if (jelly == null || jelly.AttackType != RagdollAttackType.Jelly ||
                jelly.CalculateDamage(0f) != 0f || jelly.CalculateDamage(100f) != 0f)
                throw new InvalidOperationException("Jelly must remain a zero-damage presentation mechanic.");

            float appliedCriticalDamage = MaximumRawDamagePerHit * HeadDamageRatio * IncomingDamageMultiplier;
            int requiredHits = Mathf.CeilToInt(HeadHealth / appliedCriticalDamage);
            if (requiredHits < MinimumCriticalHits)
                throw new InvalidOperationException("Critical head can be destroyed too quickly: " + requiredHits + " hits.");

            Debug.Log($"FORTY_SECOND_LEVEL_BALANCE_VALIDATION_OK: timers={LevelPlayTime:F0}s/{LevelPlayTime:F0}s, " +
                      $"head={HeadHealth:F0}hp, cappedCriticalDamage={appliedCriticalDamage:F0}, minimumHits={requiredHits}, jellyDamage=0.");
        }

        private static void ValidateLevel(string assetPath)
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (level == null || !Mathf.Approximately(level.TimeLimit, LevelPlayTime))
                throw new InvalidOperationException(assetPath + " must use a " + LevelPlayTime + " second timer.");
            SerializedObject data = new SerializedObject(level);
            if (!Mathf.Approximately(ReadFloat(data, "targetDamage"), HeadHealth))
                throw new InvalidOperationException(assetPath + " target damage must match critical-head health.");
        }

        private static void ValidateScene(string scenePath, float baseDamage, float speedDamage,
            float minimumSpeed, float damageCap)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RagdollController controller = FindAuthoredController(scene);
            if (controller == null) throw new InvalidOperationException(scene.name + " has no authored ragdoll.");

            RagdollRigController2D rig = controller.GetComponent<RagdollRigController2D>();
            SerializedProperty parts = new SerializedObject(rig).FindProperty("authoredParts");
            if (parts == null || parts.arraySize != 6) throw new InvalidOperationException(scene.name + " must have six main parts.");
            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty item = parts.GetArrayElementAtIndex(i);
                RagdollPartType type = (RagdollPartType)item.FindPropertyRelative("partType").enumValueIndex;
                RagdollPartHealth health = item.FindPropertyRelative("health").objectReferenceValue as RagdollPartHealth;
                if (health == null || !Mathf.Approximately(health.MaximumHealth, HealthFor(type)) ||
                    !Mathf.Approximately(health.DamageRatio, DamageRatioFor(type)) ||
                    health.IsCritical != (type == RagdollPartType.Head))
                    throw new InvalidOperationException(scene.name + " has an incorrectly balanced " + type + " part.");

                DismemberableLimb structural = item.FindPropertyRelative("dismemberableLimb").objectReferenceValue as DismemberableLimb;
                if (structural != null)
                {
                    SerializedObject structuralData = new SerializedObject(structural);
                    if (!Mathf.Approximately(ReadFloat(structuralData, "jointHealth"), HealthFor(type)) ||
                        ReadBool(structuralData, "damageFromJointStress") ||
                        structural.BreakWhenOverstretched != (type != RagdollPartType.Torso) ||
                        !Mathf.Approximately(structural.MaximumAnchorSeparation, MaximumAnchorSeparationFor(type)) ||
                        structural.RequiredOverstretchFixedSteps != DistanceBreakFixedSteps ||
                        structural.AllowDistanceBreakWhileDragging)
                        throw new InvalidOperationException(scene.name + " has mismatched structural durability on " + type + ".");
                }
            }

            RagdollDamageManager manager = controller.GetComponent<RagdollDamageManager>();
            SerializedObject managerData = manager != null ? new SerializedObject(manager) : null;
            if (manager == null ||
                !Mathf.Approximately(ReadFloat(managerData, "incomingDamageMultiplier"), IncomingDamageMultiplier) ||
                !Mathf.Approximately(ReadFloat(managerData, "repeatHitCooldown"), RepeatHitCooldown))
                throw new InvalidOperationException(scene.name + " global damage tuning is not authored.");

            RagdollPoseController2D pose = controller.GetComponent<RagdollPoseController2D>();
            if (pose == null || !pose.ReactToBrokenLeg || pose.AllowOneLegRecovery ||
                !Mathf.Approximately(pose.BrokenLegFallDuration, BrokenLegFallDuration) ||
                !Mathf.Approximately(pose.BrokenLegRecoveryDelay, BrokenLegRecoveryDelay) ||
                !Mathf.Approximately(pose.BrokenLegFallTorque, BrokenLegFallTorque) ||
                !Mathf.Approximately(pose.OneLegMotorStrength, OneLegMotorStrength) ||
                !Mathf.Approximately(pose.OneLegBalanceStrength, OneLegBalanceStrength) ||
                !Mathf.Approximately(pose.OneLegLiftStrength, OneLegLiftStrength) ||
                !Mathf.Approximately(pose.OneLegHeadStrength, OneLegHeadStrength))
                throw new InvalidOperationException(scene.name + " broken-leg recovery is not authored.");

            GameObject room = FindRoot(scene, "Room");
            Collider2D[] walls = room != null ? room.GetComponentsInChildren<Collider2D>(true) : Array.Empty<Collider2D>();
            if (walls.Length == 0) throw new InvalidOperationException(scene.name + " has no walls.");
            for (int i = 0; i < walls.Length; i++)
            {
                RagdollAttackManager2D wall = walls[i].GetComponent<RagdollAttackManager2D>();
                if (wall == null || wall.AttackType != RagdollAttackType.Wall)
                    throw new InvalidOperationException(scene.name + " wall attack is missing.");
                ValidateAttack(wall, baseDamage, speedDamage, minimumSpeed, damageCap, scene.name + "/" + walls[i].name);
            }
        }

        private static void ValidateAttack(RagdollAttackManager2D attack, float baseDamage, float speedDamage,
            float minimumSpeed, float damageCap, string label)
        {
            SerializedObject data = new SerializedObject(attack);
            if (!Mathf.Approximately(ReadFloat(data, "baseDamage"), baseDamage) ||
                !Mathf.Approximately(ReadFloat(data, "damagePerSpeed"), speedDamage) ||
                !Mathf.Approximately(ReadFloat(data, "minimumImpactSpeed"), minimumSpeed) ||
                !Mathf.Approximately(ReadFloat(data, "maximumDamage"), damageCap))
                throw new InvalidOperationException(label + " damage curve does not match the authored balance.");
        }

        private static float HealthFor(RagdollPartType type)
        {
            switch (type)
            {
                case RagdollPartType.Head: return HeadHealth;
                case RagdollPartType.Torso: return TorsoHealth;
                case RagdollPartType.Arm: return ArmHealth;
                case RagdollPartType.Leg: return LegHealth;
                default: throw new InvalidOperationException("Only the six explicit Head, Torso, Arm, and Leg parts may be balanced.");
            }
        }

        private static float DamageRatioFor(RagdollPartType type)
        {
            switch (type)
            {
                case RagdollPartType.Head: return HeadDamageRatio;
                case RagdollPartType.Torso: return .72f;
                case RagdollPartType.Arm: return .75f;
                case RagdollPartType.Leg: return .72f;
                default: throw new InvalidOperationException("Unsupported authored part type: " + type);
            }
        }

        public static float MaximumAnchorSeparationFor(RagdollPartType type)
        {
            switch (type)
            {
                case RagdollPartType.Head: return HeadMaximumAnchorSeparation;
                case RagdollPartType.Arm: return ArmMaximumAnchorSeparation;
                case RagdollPartType.Leg: return LegMaximumAnchorSeparation;
                case RagdollPartType.Torso: return ArmMaximumAnchorSeparation;
                default: throw new InvalidOperationException("Unsupported authored part type: " + type);
            }
        }

        private static RagdollController FindAuthoredController(Scene scene)
        {
            RagdollController[] controllers = Resources.FindObjectsOfTypeAll<RagdollController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                RagdollController controller = controllers[i];
                if (controller.gameObject.scene != scene || !controller.gameObject.activeInHierarchy) continue;
                RagdollRigController2D rig = controller.GetComponent<RagdollRigController2D>();
                SerializedProperty parts = rig != null ? new SerializedObject(rig).FindProperty("authoredParts") : null;
                if (parts != null && parts.arraySize == 6) return controller;
            }
            return null;
        }

        private static void RestoreAuthoredCameraSize(Scene scene)
        {
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera.gameObject.scene != scene) continue;
                CameraManager2D fit = camera.GetComponent<CameraManager2D>();
                if (fit != null) camera.orthographicSize = fit.referenceOrthoSize;
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            return null;
        }

        private static void Set(SerializedObject target, string property, float value)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field == null) throw new MissingFieldException(target.targetObject.GetType().Name, property);
            field.floatValue = value;
        }

        private static void Set(SerializedObject target, string property, bool value)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field == null) throw new MissingFieldException(target.targetObject.GetType().Name, property);
            field.boolValue = value;
        }

        private static float ReadFloat(SerializedObject target, string property)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field == null) throw new MissingFieldException(target.targetObject.GetType().Name, property);
            return field.floatValue;
        }

        private static bool ReadBool(SerializedObject target, string property)
        {
            SerializedProperty field = target.FindProperty(property);
            if (field == null) throw new MissingFieldException(target.targetObject.GetType().Name, property);
            return field.boolValue;
        }
    }
}
#endif
