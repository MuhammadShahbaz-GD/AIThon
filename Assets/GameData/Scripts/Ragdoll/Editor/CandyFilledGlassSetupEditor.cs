#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors visible candy fill and heavy, reinforced glass-ragdoll physics.</summary>
    public static class CandyFilledGlassSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string ProfilePath = "Assets/GameData/Materials/Resources/Ragdoll Profiles/Solid Robot.asset";
        private const string CandyFolder = "Assets/GameData/Art/Candies";

        [MenuItem("Tools/Ragdoll/VFX/Setup Candy Filled Heavy Glass")]
        public static void SetupSelectedOrActive()
        {
            RagdollController controller = ResolveController();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Candy Filled Glass",
                    "Select a configured ragdoll root containing RagdollController.", "OK");
                return;
            }

            Setup(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = controller.gameObject;
        }

        public static void SetupSandboxBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController controller = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (controller == null) throw new InvalidOperationException("RagdollSandbox has no RagdollController.");
            Setup(controller);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void ValidateSandboxBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController controller = UnityEngine.Object.FindObjectOfType<RagdollController>();
            RagdollCandyFill2D[] fills = controller.GetComponentsInChildren<RagdollCandyFill2D>(true);
            int candyCount = 0;
            for (int i = 0; i < fills.Length; i++)
            {
                candyCount += fills[i].CandyCount;
                if (fills[i].Body == null || fills[i].GlassRenderer == null || fills[i].CandyCount == 0)
                    throw new InvalidOperationException("Candy fill is incomplete on " + fills[i].name + ".");
                if (fills[i].Body.mass < .75f || fills[i].Body.gravityScale < 1f)
                    throw new InvalidOperationException("Heavy-glass Rigidbody2D tuning is missing on " + fills[i].name + ".");
            }
            if (fills.Length != 6 || candyCount != 39)
                throw new InvalidOperationException("Expected 6 candy-filled parts and 39 candy visuals.");

            Rigidbody2D[] allBodies = controller.GetComponentsInChildren<Rigidbody2D>(true);
            for (int fillIndex = 0; fillIndex < fills.Length; fillIndex++)
            {
                Transform generatedRoot = fills[fillIndex].GlassRenderer.transform.Find("Candy Fill");
                if (generatedRoot == null) throw new InvalidOperationException("Generated Candy Fill root is missing.");
                for (int bodyIndex = 0; bodyIndex < allBodies.Length; bodyIndex++)
                    if (allBodies[bodyIndex].transform.IsChildOf(generatedRoot))
                        throw new InvalidOperationException("Generated candy visuals must not add Rigidbody2D components.");
            }

            Debug.Log("Candy-filled glass validation passed: 6 parts, 39 candies, zero extra candy rigidbodies.");
        }

        private static void Setup(RagdollController controller)
        {
            RagdollRigController2D rig = controller.GetComponent<RagdollRigController2D>();
            if (rig == null) throw new InvalidOperationException("Run explicit ragdoll setup before candy fill.");

            Sprite[] candySprites = LoadCandySprites();
            SerializedProperty parts = new SerializedObject(rig).FindProperty("authoredParts");
            if (parts == null || parts.arraySize != 6)
                throw new InvalidOperationException("Candy fill requires exactly six explicitly authored main parts.");

            Undo.SetCurrentGroupName("Setup Candy Filled Heavy Glass");
            int undoGroup = Undo.GetCurrentGroup();

            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty part = parts.GetArrayElementAtIndex(i);
                Rigidbody2D body = part.FindPropertyRelative("body").objectReferenceValue as Rigidbody2D;
                SpriteRenderer glass = part.FindPropertyRelative("visual").objectReferenceValue as SpriteRenderer;
                RagdollPartType type = (RagdollPartType)part.FindPropertyRelative("partType").enumValueIndex;
                if (body == null || glass == null || glass.sprite == null)
                    throw new InvalidOperationException("Explicit part " + i + " has no body/glass sprite reference.");

                int candyCount = ResolveCandyCount(type);
                float emptyMass;
                float ballastMass;
                ResolveMass(type, out emptyMass, out ballastMass);
                float bakedMass = emptyMass + ballastMass;

                Transform oldFill = glass.transform.Find("Candy Fill");
                if (oldFill != null) Undo.DestroyObjectImmediate(oldFill.gameObject);

                GameObject root = new GameObject("Candy Fill");
                Undo.RegisterCreatedObjectUndo(root, "Create Candy Fill");
                root.transform.SetParent(glass.transform, false);

                GameObject[] candyVisuals = CreateCandyVisuals(
                    root.transform, glass, candySprites, candyCount, i, type);

                RagdollCandyFill2D fill = body.GetComponent<RagdollCandyFill2D>();
                if (fill == null) fill = Undo.AddComponent<RagdollCandyFill2D>(body.gameObject);
                SerializedObject fillData = new SerializedObject(fill);
                fillData.FindProperty("body").objectReferenceValue = body;
                fillData.FindProperty("glassRenderer").objectReferenceValue = glass;
                fillData.FindProperty("fillRoot").objectReferenceValue = root;
                AssignArray(fillData.FindProperty("candyVisuals"), candyVisuals);
                fillData.FindProperty("emptyPartMass").floatValue = emptyMass;
                fillData.FindProperty("candyBallastMass").floatValue = ballastMass;
                fillData.FindProperty("bakedPartMass").floatValue = bakedMass;
                fillData.FindProperty("visualFillRatio").floatValue = ResolveFillRatio(type);
                fillData.ApplyModifiedPropertiesWithoutUndo();

                body.useAutoMass = false;
                body.mass = bakedMass;
                body.gravityScale = 1f;
                body.drag = .4f;
                body.angularDrag = .8f;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                HingeJoint2D[] hinges = body.GetComponents<HingeJoint2D>();
                for (int jointIndex = 0; jointIndex < hinges.Length; jointIndex++)
                {
                    JointMotor2D motor = hinges[jointIndex].motor;
                    motor.maxMotorTorque = 520f;
                    hinges[jointIndex].motor = motor;
                }
                EditorUtility.SetDirty(body);
            }

            TuneRootPhysics(controller, rig);
            TuneSolidGlassProfile();
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("Candy-filled glass setup authored 39 candies and reinforced six weighted main parts.", controller);
        }

        private static GameObject[] CreateCandyVisuals(Transform parent, SpriteRenderer glass,
            Sprite[] sprites, int count, int partIndex, RagdollPartType type)
        {
            GameObject[] result = new GameObject[count];
            Bounds bounds = glass.sprite.bounds;
            float fillRatio = ResolveFillRatio(type);
            float marginX = bounds.size.x * .16f;
            float marginBottom = bounds.size.y * .14f;
            float usableWidth = Mathf.Max(.05f, bounds.size.x - marginX * 2f);
            float usableHeight = Mathf.Max(.05f, bounds.size.y * fillRatio - marginBottom);
            float aspect = usableWidth / usableHeight;
            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count * Mathf.Max(.45f, aspect))));
            int rows = Mathf.CeilToInt((float)count / columns);
            float stepX = usableWidth / Mathf.Max(1, columns);
            float stepY = usableHeight / Mathf.Max(1, rows);
            float targetDiameter = Mathf.Min(stepX, stepY) * .82f;

            for (int i = 0; i < count; i++)
            {
                Sprite sprite = sprites[(partIndex * 7 + i * 5) % sprites.Length];
                GameObject candy = new GameObject("Candy " + (i + 1).ToString("00"), typeof(SpriteRenderer));
                Undo.RegisterCreatedObjectUndo(candy, "Create Candy");
                candy.transform.SetParent(parent, false);

                int column = i % columns;
                int row = i / columns;
                float x = bounds.min.x + marginX + stepX * (column + .5f);
                float y = bounds.min.y + marginBottom + stepY * (row + .5f);
                float stagger = row % 2 == 0 ? 0f : stepX * .12f;
                candy.transform.localPosition = new Vector3(x + stagger, y, -.01f);
                candy.transform.localRotation = Quaternion.Euler(0f, 0f, ((i * 47 + partIndex * 19) % 70) - 35f);

                float nativeDiameter = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
                float scale = targetDiameter / Mathf.Max(.001f, nativeDiameter);
                candy.transform.localScale = Vector3.one * scale;

                SpriteRenderer renderer = candy.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerID = glass.sortingLayerID;
                renderer.sortingOrder = glass.sortingOrder + 2;
                result[i] = candy;
            }
            return result;
        }

        private static void TuneRootPhysics(RagdollController controller, RagdollRigController2D rig)
        {
            SetFloat(rig, "fallbackLimbHealth", 90f);
            SetFloat(rig, "jointBreakStress", 900f);
            SetFloat(rig, "jointStressDamageRate", .005f);

            RagdollPoseController2D pose = controller.GetComponent<RagdollPoseController2D>();
            SetFloat(pose, "standingMotorTorque", 280f);
            SetFloat(pose, "getUpMotorTorque", 560f);
            SetFloat(pose, "standingBalanceTorque", 75f);
            SetFloat(pose, "getUpBalanceTorque", 230f);
            SetFloat(pose, "balanceDamping", 9f);
            SetFloat(pose, "getUpLiftForce", 145f);
            SetFloat(pose, "maximumGetUpVelocity", 3f);
            SetFloat(pose, "headUprightTorque", 12f);
            SetFloat(pose, "headUprightDamping", 3f);
            SetFloat(pose, "maximumHeadUprightTorque", 95f);
            SetFloat(pose, "standingArmTorqueMultiplier", .85f);
            SetFloat(pose, "standingLegTorqueMultiplier", 1.75f);
            SetFloat(pose, "getUpArmTorqueMultiplier", 1.15f);
            SetFloat(pose, "getUpLegTorqueMultiplier", 2.2f);

            RagdollStateController2D state = controller.GetComponent<RagdollStateController2D>();
            SetFloat(state, "gravityScaleDefault", 1.2f);
            SetFloat(state, "gravityScaleKnockedOut", 1.35f);
            SetFloat(state, "dragDefault", 1.8f);
            SetFloat(state, "angularDragDefault", 3f);

            RagdollInputManager input = controller.GetComponent<RagdollInputManager>();
            SerializedObject inputData = new SerializedObject(input);
            SerializedProperty drag = inputData.FindProperty("drag");
            drag.FindPropertyRelative("frequency").floatValue = 6.5f;
            drag.FindPropertyRelative("dampingRatio").floatValue = .92f;
            drag.FindPropertyRelative("maximumForce").floatValue = 4800f;
            drag.FindPropertyRelative("maximumTargetSpeed").floatValue = 95f;
            drag.FindPropertyRelative("headForceMultiplier").floatValue = 2.2f;
            inputData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TuneSolidGlassProfile()
        {
            RagdollProfile profile = AssetDatabase.LoadAssetAtPath<RagdollProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("Solid Robot profile is missing.");
            SerializedObject data = new SerializedObject(profile);
            data.FindProperty("massMultiplier").floatValue = 4f;
            data.FindProperty("linearDrag").floatValue = 2.5f;
            data.FindProperty("angularDrag").floatValue = 3.5f;
            data.FindProperty("useGravity").boolValue = true;
            data.FindProperty("gravityScaleModifier").floatValue = 1.25f;
            data.FindProperty("jointSpringForce").floatValue = 420f;
            data.FindProperty("jointDamping").floatValue = 14f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private static Sprite[] LoadCandySprites()
        {
            var sprites = new List<Sprite>(24);
            for (int i = 1; i <= 24; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    CandyFolder + "/Candy " + i + ".png");
                if (sprite != null) sprites.Add(sprite);
            }
            if (sprites.Count == 0) throw new InvalidOperationException("No candy sprites were found.");
            return sprites.ToArray();
        }

        private static int ResolveCandyCount(RagdollPartType type)
        {
            switch (type)
            {
                case RagdollPartType.Torso: return 14;
                case RagdollPartType.Head: return 5;
                case RagdollPartType.Arm: return 4;
                case RagdollPartType.Leg: return 6;
                default: return 3;
            }
        }

        private static float ResolveFillRatio(RagdollPartType type)
        {
            switch (type)
            {
                case RagdollPartType.Torso: return .82f;
                case RagdollPartType.Head: return .62f;
                default: return .72f;
            }
        }

        private static void ResolveMass(RagdollPartType type, out float emptyMass, out float ballastMass)
        {
            switch (type)
            {
                case RagdollPartType.Torso: emptyMass = 1.5f; ballastMass = 1.3f; break;
                case RagdollPartType.Head: emptyMass = .7f; ballastMass = .55f; break;
                case RagdollPartType.Arm: emptyMass = .45f; ballastMass = .35f; break;
                case RagdollPartType.Leg: emptyMass = .75f; ballastMass = .65f; break;
                default: emptyMass = .5f; ballastMass = .3f; break;
            }
        }

        private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
        {
            SerializedObject data = new SerializedObject(target);
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(
                target.name + " is missing property " + propertyName + ".");
            property.floatValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignArray(SerializedProperty property, GameObject[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static RagdollController ResolveController()
        {
            if (Selection.activeGameObject != null)
            {
                RagdollController selected = Selection.activeGameObject.GetComponentInParent<RagdollController>();
                if (selected != null) return selected;
            }
            return UnityEngine.Object.FindObjectOfType<RagdollController>();
        }
    }
}
#endif
