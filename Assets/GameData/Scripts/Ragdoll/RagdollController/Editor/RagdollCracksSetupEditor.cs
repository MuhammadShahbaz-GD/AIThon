#if UNITY_EDITOR
using System.Collections.Generic;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Installs lightweight crack-skin stages on every damageable part of a selected ragdoll.</summary>
    public static class RagdollCracksSetupEditor
    {
        private const string MenuPath = "Tools/Ragdoll/VFX/Setup Crack Skins On Selected Ragdoll";

        [MenuItem(MenuPath + " %#c")]
        public static void SetupSelectedRagdoll()
        {
            GameObject root = Selection.activeGameObject;
            if (root == null || root.GetComponentInChildren<RagdollPartHealth>(true) == null)
            {
                RagdollController controller = Object.FindObjectOfType<RagdollController>();
                root = controller != null ? controller.gameObject : null;
            }
            if (root == null)
            {
                EditorUtility.DisplayDialog("Ragdoll Cracks", "Select the Buddy/ragdoll root first.", "OK");
                return;
            }

            RagdollPartHealth[] parts = root.GetComponentsInChildren<RagdollPartHealth>(true);
            if (parts.Length == 0)
            {
                EditorUtility.DisplayDialog("Ragdoll Cracks",
                    "The selection has no RagdollPartHealth components. Run the ragdoll setup first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Setup Ragdoll Crack Skins");
            int undoGroup = Undo.GetCurrentGroup();

            for (int i = 0; i < parts.Length; i++)
                SetupPart(parts[i]);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"Configured crack skins on {parts.Length} ragdoll parts under '{root.name}'.");
        }

        [MenuItem(MenuPath + " %#c", true)]
        private static bool ValidateSetupSelectedRagdoll() =>
            Selection.activeGameObject != null && !EditorApplication.isPlaying;

        private static void SetupPart(RagdollPartHealth partHealth)
        {
            GameObject part = partHealth.gameObject;
            DismemberableLimb limb = part.GetComponent<DismemberableLimb>();
            if (limb == null) limb = Undo.AddComponent<DismemberableLimb>(part);

            CracksModifier modifier = part.GetComponent<CracksModifier>();
            if (modifier == null) modifier = Undo.AddComponent<CracksModifier>(part);

            Transform container = part.transform.Find("Crack Skins");
            if (container == null)
            {
                GameObject value = new GameObject("Crack Skins");
                Undo.RegisterCreatedObjectUndo(value, "Create Crack Skins");
                value.transform.SetParent(part.transform, false);
                container = value.transform;
            }

            ClearGeneratedStages(container);
            Bounds localBounds = ResolveLocalBounds(part);
            GameObject[] stages = new GameObject[3];
            stages[0] = CreateStage(container, "Cracks 01 - Light", localBounds, 1, .035f, new Color(.16f, .12f, .12f, .72f));
            stages[1] = CreateStage(container, "Cracks 02 - Medium", localBounds, 3, .045f, new Color(.12f, .08f, .08f, .86f));
            stages[2] = CreateStage(container, "Cracks 03 - Critical", localBounds, 5, .06f, new Color(.08f, .025f, .025f, 1f));

            SerializedObject data = new SerializedObject(modifier);
            data.FindProperty("limbHealth").objectReferenceValue = partHealth;
            data.FindProperty("dismemberableLimb").objectReferenceValue = limb;
            SerializedProperty skins = data.FindProperty("crackSkins");
            skins.arraySize = stages.Length;
            for (int i = 0; i < stages.Length; i++)
                skins.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            data.FindProperty("cracksStartAtNormalizedHealth").floatValue = .85f;
            data.FindProperty("breakLimbAtZeroHealth").boolValue = true;
            data.ApplyModifiedProperties();

            for (int i = 0; i < stages.Length; i++) stages[i].SetActive(false);
            EditorUtility.SetDirty(modifier);
        }

        private static GameObject CreateStage(Transform parent, string name, Bounds bounds,
            int branchCount, float width, Color color)
        {
            GameObject stage = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(stage, "Create Crack Stage");
            stage.transform.SetParent(parent, false);

            for (int branch = 0; branch < branchCount; branch++)
            {
                GameObject lineObject = new GameObject($"Crack Branch {branch + 1:00}");
                Undo.RegisterCreatedObjectUndo(lineObject, "Create Crack Branch");
                lineObject.transform.SetParent(stage.transform, false);
                LineRenderer line = Undo.AddComponent<LineRenderer>(lineObject);
                line.useWorldSpace = false;
                line.alignment = LineAlignment.TransformZ;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 2;
                line.numCornerVertices = 2;
                line.startWidth = width;
                line.endWidth = width * .45f;
                line.startColor = color;
                line.endColor = color;
                line.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
                line.sortingOrder = 50;

                float seed = branch * 1.618f + branchCount * .73f;
                float x = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Repeat(.24f + seed * .31f, 1f));
                float y = Mathf.Lerp(bounds.min.y, bounds.max.y, Mathf.Repeat(.18f + seed * .47f, 1f));
                float sx = Mathf.Max(.12f, bounds.size.x * .18f);
                float sy = Mathf.Max(.16f, bounds.size.y * .24f);
                line.positionCount = 4;
                line.SetPosition(0, new Vector3(x - sx, y + sy, -.01f));
                line.SetPosition(1, new Vector3(x + sx * .15f, y + sy * .2f, -.01f));
                line.SetPosition(2, new Vector3(x - sx * .22f, y - sy * .2f, -.01f));
                line.SetPosition(3, new Vector3(x + sx, y - sy, -.01f));
            }

            stage.SetActive(false);
            return stage;
        }

        private static Bounds ResolveLocalBounds(GameObject part)
        {
            SpriteRenderer renderer = part.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null) return renderer.sprite.bounds;

            Collider2D collider = part.GetComponent<Collider2D>();
            if (collider != null)
            {
                Vector3 localSize = part.transform.InverseTransformVector(collider.bounds.size);
                return new Bounds(Vector3.zero,
                    new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), .1f));
            }

            return new Bounds(Vector3.zero, new Vector3(.8f, 1.2f, .1f));
        }

        private static void ClearGeneratedStages(Transform container)
        {
            var children = new List<GameObject>();
            for (int i = 0; i < container.childCount; i++)
                children.Add(container.GetChild(i).gameObject);
            for (int i = 0; i < children.Count; i++)
                Undo.DestroyObjectImmediate(children[i]);
        }
    }
}
#endif

