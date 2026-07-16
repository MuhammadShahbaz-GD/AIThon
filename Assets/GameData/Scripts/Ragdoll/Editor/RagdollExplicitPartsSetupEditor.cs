#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors the six main ragdoll parts and all runtime dependencies into the Inspector.</summary>
    public static class RagdollExplicitPartsSetupEditor
    {
        private const string MenuPath = "Tools/Ragdoll/Setup Explicit Main Parts";
        private const string SandboxScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        [MenuItem(MenuPath)]
        public static void SetupSelectedOrActiveRagdoll()
        {
            RagdollController controller = ResolveController();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Explicit Ragdoll Setup",
                    "Select a ragdoll root containing RagdollController.", "OK");
                return;
            }

            SetupController(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene);
            Selection.activeGameObject = controller.gameObject;
        }

        public static void SetupSandboxBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
            RagdollController controller = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (controller == null) throw new InvalidOperationException("RagdollSandbox has no RagdollController.");
            SetupController(controller);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void ValidateSandboxBatch()
        {
            EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
            RagdollController controller = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (controller == null) throw new InvalidOperationException("RagdollSandbox has no RagdollController.");
            RagdollRigController2D rig = controller.GetComponent<RagdollRigController2D>();
            SerializedProperty configured = new SerializedObject(rig).FindProperty("authoredParts");
            if (configured == null || configured.arraySize != 6)
                throw new InvalidOperationException("Expected exactly 6 explicit main parts.");

            var mainBodies = new HashSet<Rigidbody2D>();
            for (int i = 0; i < configured.arraySize; i++)
            {
                SerializedProperty item = configured.GetArrayElementAtIndex(i);
                Rigidbody2D body = item.FindPropertyRelative("body").objectReferenceValue as Rigidbody2D;
                if (body == null || !mainBodies.Add(body))
                    throw new InvalidOperationException("Main-part body reference is null or duplicated at index " + i + ".");
                if (body.GetComponent<RagdollLimb>() == null || body.GetComponent<ActiveRagdollLimb>() == null ||
                    body.GetComponent<DismemberableLimb>() == null || body.GetComponent<DamageReceiver2D>() == null ||
                    body.GetComponent<RagdollPartHealth>() == null || body.GetComponent<TargetJoint2D>() == null)
                    throw new InvalidOperationException("Required authored components are missing from " + body.name + ".");
            }

            Rigidbody2D[] allBodies = controller.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < allBodies.Length; i++)
            {
                Rigidbody2D body = allBodies[i];
                if (mainBodies.Contains(body)) continue;
                if (body.GetComponent<RagdollLimb>() != null || body.GetComponent<ActiveRagdollLimb>() != null ||
                    body.GetComponent<DismemberableLimb>() != null || body.GetComponent<DamageReceiver2D>() != null ||
                    body.GetComponent<RagdollPartHealth>() != null)
                    throw new InvalidOperationException("Non-main Rigidbody2D still contains ragdoll controller scripts: " + body.name);
            }
            Debug.Log("Explicit ragdoll validation passed: 6 main parts fully authored; " +
                      (allBodies.Length - mainBodies.Count) + " non-main Rigidbody2D objects remain clean.");
        }
        private static RagdollController ResolveController()
        {
            if (Selection.activeGameObject != null)
            {
                RagdollController selected = Selection.activeGameObject.GetComponentInParent<RagdollController>();
                if (selected != null) return selected;
                selected = Selection.activeGameObject.GetComponentInChildren<RagdollController>(true);
                if (selected != null) return selected;
            }
            return UnityEngine.Object.FindObjectOfType<RagdollController>();
        }

        private static void SetupController(RagdollController controller)
        {
            Undo.SetCurrentGroupName("Setup Explicit Ragdoll Main Parts");
            int undoGroup = Undo.GetCurrentGroup();

            RagdollRigController2D rig = Ensure<RagdollRigController2D>(controller.gameObject);
            RagdollPoseController2D pose = Ensure<RagdollPoseController2D>(controller.gameObject);
            RagdollProfileController2D profiles = Ensure<RagdollProfileController2D>(controller.gameObject);
            RagdollStateController2D state = Ensure<RagdollStateController2D>(controller.gameObject);
            RagdollDamageManager damage = Ensure<RagdollDamageManager>(controller.gameObject);
            RagdollElementalEffects elements = Ensure<RagdollElementalEffects>(controller.gameObject);
            RagdollAnimationController animationController = Ensure<RagdollAnimationController>(controller.gameObject);
            RagdollInputManager input = Ensure<RagdollInputManager>(controller.gameObject);

            List<Rigidbody2D> mainBodies = CollectMainBodies(controller);
            if (mainBodies.Count != 6)
                Debug.LogWarning("Expected 6 main ragdoll bodies but configured " + mainBodies.Count +
                                 ". Expected Head, Torso/Belly, two upper arms and two upper legs.", controller);

            RemoveRuntimeGameplayComponentsFromNonMainBodies(controller, mainBodies);

            SerializedObject rigData = new SerializedObject(rig);
            SerializedProperty authoredParts = rigData.FindProperty("authoredParts");
            authoredParts.arraySize = mainBodies.Count;
            DamageReceiver2D[] receivers = new DamageReceiver2D[mainBodies.Count];

            for (int i = 0; i < mainBodies.Count; i++)
            {
                Rigidbody2D body = mainBodies[i];
                HingeJoint2D[] hinges = body.GetComponents<HingeJoint2D>();
                Collider2D[] colliders = body.GetComponents<Collider2D>();
                HingeJoint2D firstHinge = hinges.Length > 0 ? hinges[0] : null;

                RagdollLimb relay = Ensure<RagdollLimb>(body.gameObject);
                ActiveRagdollLimb active = Ensure<ActiveRagdollLimb>(body.gameObject);
                DismemberableLimb dismemberable = Ensure<DismemberableLimb>(body.gameObject);
                RagdollPartHealth health = Ensure<RagdollPartHealth>(body.gameObject);
                DamageReceiver2D receiver = Ensure<DamageReceiver2D>(body.gameObject);
                TargetJoint2D targetJoint = Ensure<TargetJoint2D>(body.gameObject);
                targetJoint.autoConfigureTarget = false;
                targetJoint.enabled = false;
                receivers[i] = receiver;

                RagdollPartType type = ResolvePartType(body.name);
                ConfigureHealthIfNeeded(health, type);
                SetReference(relay, "body", body);
                SetReference(active, "body", body);
                SetReference(active, "joint", firstHinge);
                SetReference(dismemberable, "body", body);
                SetReference(dismemberable, "parentJoint", firstHinge);
                SetReference(dismemberable, "owner", controller);
                SetReference(health, "structuralLimb", dismemberable);

                SerializedObject receiverData = new SerializedObject(receiver);
                receiverData.FindProperty("body").objectReferenceValue = body;
                receiverData.FindProperty("controller").objectReferenceValue = controller;
                receiverData.FindProperty("dismemberable").objectReferenceValue = dismemberable;
                receiverData.FindProperty("elements").objectReferenceValue = elements;
                receiverData.FindProperty("damageManager").objectReferenceValue = damage;
                receiverData.FindProperty("partHealth").objectReferenceValue = health;
                receiverData.FindProperty("dragJoint").objectReferenceValue = targetJoint;
                receiverData.ApplyModifiedPropertiesWithoutUndo();

                SerializedProperty item = authoredParts.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("displayName").stringValue = body.name;
                item.FindPropertyRelative("partType").enumValueIndex = (int)type;
                item.FindPropertyRelative("upperLimb").boolValue =
                    body.name.IndexOf("upper", StringComparison.OrdinalIgnoreCase) >= 0;
                item.FindPropertyRelative("body").objectReferenceValue = body;
                AssignArray(item.FindPropertyRelative("colliders"), colliders);
                AssignArray(item.FindPropertyRelative("hinges"), hinges);
                item.FindPropertyRelative("visual").objectReferenceValue =
                    body.GetComponent<SpriteRenderer>() ?? body.GetComponentInChildren<SpriteRenderer>(true);
                item.FindPropertyRelative("collisionRelay").objectReferenceValue = relay;
                item.FindPropertyRelative("activeLimb").objectReferenceValue = active;
                item.FindPropertyRelative("dismemberableLimb").objectReferenceValue = dismemberable;
                item.FindPropertyRelative("damageReceiver").objectReferenceValue = receiver;
                item.FindPropertyRelative("health").objectReferenceValue = health;
                item.FindPropertyRelative("dragJoint").objectReferenceValue = targetJoint;
            }
            rigData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject controllerData = new SerializedObject(controller);
            controllerData.FindProperty("rig").objectReferenceValue = rig;
            controllerData.FindProperty("pose").objectReferenceValue = pose;
            controllerData.FindProperty("profiles").objectReferenceValue = profiles;
            controllerData.FindProperty("state").objectReferenceValue = state;
            controllerData.FindProperty("damageManager").objectReferenceValue = damage;
            controllerData.FindProperty("elementalEffects").objectReferenceValue = elements;
            controllerData.FindProperty("animationController").objectReferenceValue = animationController;
            controllerData.FindProperty("inputManager").objectReferenceValue = input;
            controllerData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject damageData = new SerializedObject(damage);
            damageData.FindProperty("controller").objectReferenceValue = controller;
            damageData.FindProperty("elementalEffects").objectReferenceValue = elements;
            damageData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject inputData = new SerializedObject(input);
            Camera camera = Camera.main;
            if (camera != null) inputData.FindProperty("inputCamera").objectReferenceValue = camera;
            AssignArray(inputData.FindProperty("receivers"), receivers);
            inputData.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rig);
            EditorUtility.SetDirty(input);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("Explicit ragdoll setup authored " + mainBodies.Count +
                      " main parts. Runtime discovery and component creation are disabled.", controller);
        }

        private static List<Rigidbody2D> CollectMainBodies(RagdollController controller)
        {
            Rigidbody2D[] bodies = controller.GetComponentsInChildren<Rigidbody2D>(true);
            var result = new List<Rigidbody2D>(6);
            for (int i = 0; i < bodies.Length; i++)
                if (IsMainPartName(bodies[i].name))
                    result.Add(bodies[i]);
            result.Sort(CompareBodies);
            return result;
        }

        private static bool IsMainPartName(string partName)
        {
            string name = partName.ToLowerInvariant();
            if (name.Contains("head") || name.Contains("torso") || name.Contains("belly")) return true;
            if (name.Contains("lower")) return false;
            if (name.Contains("upper arm") || name.Contains("upper leg")) return true;
            return name == "left arm" || name == "right arm" || name == "left leg" || name == "right leg";
        }

        private static int CompareBodies(Rigidbody2D a, Rigidbody2D b)
        {
            int type = ResolvePartType(a.name).CompareTo(ResolvePartType(b.name));
            return type != 0 ? type : string.CompareOrdinal(a.name, b.name);
        }

        private static RagdollPartType ResolvePartType(string partName)
        {
            string name = partName.ToLowerInvariant();
            if (name.Contains("head")) return RagdollPartType.Head;
            if (name.Contains("torso") || name.Contains("belly")) return RagdollPartType.Torso;
            if (name.Contains("arm")) return RagdollPartType.Arm;
            if (name.Contains("leg")) return RagdollPartType.Leg;
            return RagdollPartType.Other;
        }

        private static void ConfigureHealthIfNeeded(RagdollPartHealth health, RagdollPartType type)
        {
            if (health.PartType == type) return;
            switch (type)
            {
                case RagdollPartType.Head: health.Configure(type, 40f, 2f, 1.25f, .9f, 1.4f, true); break;
                case RagdollPartType.Torso: health.Configure(type, 100f, 2f, 1f, 1f, 1f, false); break;
                case RagdollPartType.Arm: health.Configure(type, 45f, 1f, .9f, 1.2f, .85f, false); break;
                case RagdollPartType.Leg: health.Configure(type, 60f, 1f, .85f, .9f, .8f, false); break;
            }
            EditorUtility.SetDirty(health);
        }

        private static void RemoveRuntimeGameplayComponentsFromNonMainBodies(
            RagdollController controller, List<Rigidbody2D> mainBodies)
        {
            Rigidbody2D[] allBodies = controller.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < allBodies.Length; i++)
            {
                Rigidbody2D body = allBodies[i];
                if (mainBodies.Contains(body)) continue;
                DestroyIfPresent<RagdollLimb>(body.gameObject);
                DestroyIfPresent<ActiveRagdollLimb>(body.gameObject);
                DestroyIfPresent<DismemberableLimb>(body.gameObject);
                DestroyIfPresent<DamageReceiver2D>(body.gameObject);
                DestroyIfPresent<RagdollPartHealth>(body.gameObject);
            }
        }

        private static void DestroyIfPresent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null) Undo.DestroyObjectImmediate(component);
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }

        private static void SetReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject data = new SerializedObject(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property != null) property.objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
        {
            property.arraySize = values != null ? values.Length : 0;
            for (int i = 0; i < property.arraySize; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif
