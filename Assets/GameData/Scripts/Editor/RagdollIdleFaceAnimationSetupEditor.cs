#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KickTheBuddy.Editor
{
    /// <summary>Imports and explicitly wires the two authored idle-face sequences.</summary>
    public static class RagdollIdleFaceAnimationSetupEditor
    {
        private const string SequenceOneFolder = "Assets/GameData/Animations/Face/Idle/Laugh_01";
        private const string SequenceTwoFolder = "Assets/GameData/Animations/Face/Idle/Laugh_02";
        private const string RagdollScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string CandyLabScenePath = "Assets/GameData/Scene/CandyLab.unity";
        private const string OverlayName = "Idle Face Animation";
        private const float FramesPerSecond = 24f;
        private const int ExpectedSequenceOneFrames = 48;
        private const int ExpectedSequenceTwoFrames = 28;

        [MenuItem("Tools/Ragdoll/Setup Authored Idle Face Animations")]
        public static void SetupFromMenu() => Setup(false);

        public static void SetupBatch() => Setup(true);

        [MenuItem("Tools/Ragdoll/Validate Authored Idle Face Animations")]
        public static void ValidateFromMenu() => Validate(false);

        public static void ValidateBatch() => Validate(true);

        private static void Setup(bool batchMode)
        {
            string originalScene = SceneManager.GetActiveScene().path;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureSpriteFolder(SequenceOneFolder);
                ConfigureSpriteFolder(SequenceTwoFolder);

                Sprite[] sequenceOne = LoadFrames(SequenceOneFolder, ExpectedSequenceOneFrames);
                Sprite[] sequenceTwo = LoadFrames(SequenceTwoFolder, ExpectedSequenceTwoFrames);
                int configured = ConfigureScene(RagdollScenePath, sequenceOne, sequenceTwo);
                configured += ConfigureScene(CandyLabScenePath, sequenceOne, sequenceTwo);

                AssetDatabase.SaveAssets();
                ValidateScenes(sequenceOne, sequenceTwo);
                Debug.Log($"IDLE_FACE_ANIMATION_SETUP_OK: wired {configured} ragdoll controller(s), sequence 1={sequenceOne.Length} frames, sequence 2={sequenceTwo.Length} frames, {FramesPerSecond:F0} FPS.");
                if (batchMode) EditorApplication.Exit(0);
                else RestoreScene(originalScene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (batchMode) EditorApplication.Exit(1);
                else
                {
                    RestoreScene(originalScene);
                    throw;
                }
            }
        }

        private static void Validate(bool batchMode)
        {
            string originalScene = SceneManager.GetActiveScene().path;
            try
            {
                Sprite[] sequenceOne = LoadFrames(SequenceOneFolder, ExpectedSequenceOneFrames);
                Sprite[] sequenceTwo = LoadFrames(SequenceTwoFolder, ExpectedSequenceTwoFrames);
                int validated = ValidateScenes(sequenceOne, sequenceTwo);
                Debug.Log($"IDLE_FACE_ANIMATION_VALIDATION_OK: {validated} ragdoll controller(s), explicit overlay references, sequences 48/28, idle-only runtime playback.");
                if (batchMode) EditorApplication.Exit(0);
                else RestoreScene(originalScene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (batchMode) EditorApplication.Exit(1);
                else
                {
                    RestoreScene(originalScene);
                    throw;
                }
            }
        }

        private static void ConfigureSpriteFolder(string folder)
        {
            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            for (int i = 0; i < textureGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 500f;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.compressionQuality = 80;

                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = 256;
                android.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.textureCompression = TextureImporterCompression.CompressedHQ;
                android.compressionQuality = 80;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static Sprite[] LoadFrames(string folder, int expectedCount)
        {
            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            List<Sprite> frames = new List<Sprite>(textureGuids.Length);
            for (int i = 0; i < textureGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) frames.Add(sprite);
            }

            frames.Sort(CompareFrameNames);
            if (frames.Count != expectedCount)
                throw new InvalidOperationException($"{folder} requires {expectedCount} sprite frames but contains {frames.Count}.");
            return frames.ToArray();
        }

        private static int ConfigureScene(string scenePath, Sprite[] sequenceOne, Sprite[] sequenceTwo)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RagdollAnimationController[] controllers = Object.FindObjectsOfType<RagdollAnimationController>(true);
            if (controllers.Length == 0)
                throw new InvalidOperationException($"No RagdollAnimationController exists in {scenePath}.");

            for (int i = 0; i < controllers.Length; i++)
                ConfigureController(controllers[i], sequenceOne, sequenceTwo);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Unity could not save {scenePath}.");
            return controllers.Length;
        }

        private static void ConfigureController(
            RagdollAnimationController controller,
            Sprite[] sequenceOne,
            Sprite[] sequenceTwo)
        {
            Transform head = FindChildContaining(controller.transform, "head");
            if (head == null)
                throw new InvalidOperationException($"{controller.name} has no authored head transform for its idle face overlay.");

            Transform overlay = head.Find(OverlayName);
            SpriteRenderer renderer;
            if (overlay == null)
            {
                GameObject overlayObject = new GameObject(OverlayName, typeof(SpriteRenderer));
                Undo.RegisterCreatedObjectUndo(overlayObject, "Create Idle Face Animation Overlay");
                overlay = overlayObject.transform;
                overlay.SetParent(head, false);
                renderer = overlayObject.GetComponent<SpriteRenderer>();
            }
            else
            {
                renderer = overlay.GetComponent<SpriteRenderer>();
                if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(overlay.gameObject);
            }

            Undo.RecordObjects(new Object[] { controller, renderer, overlay }, "Configure Idle Face Animation");
            SerializedObject serialized = new SerializedObject(controller);
            Vector2 position = serialized.FindProperty("facePositionOffset").vector2Value;
            Vector2 scale = serialized.FindProperty("faceScale").vector2Value;
            overlay.localPosition = new Vector3(position.x, position.y, -.02f);
            overlay.localRotation = Quaternion.identity;
            overlay.localScale = new Vector3(scale.x, scale.y, 1f);

            SpriteRenderer headRenderer = head.GetComponent<SpriteRenderer>();
            if (headRenderer != null)
            {
                renderer.sortingLayerID = headRenderer.sortingLayerID;
                renderer.sortingOrder = Mathf.Max(20, headRenderer.sortingOrder + 10);
            }
            renderer.sprite = sequenceOne[0];
            renderer.color = Color.white;
            renderer.enabled = false;

            serialized.FindProperty("idleFaceRenderer").objectReferenceValue = renderer;
            serialized.FindProperty("idleFaceFramesPerSecond").floatValue = FramesPerSecond;
            SetSpriteArray(serialized.FindProperty("idleFaceSequenceOne"), sequenceOne);
            SetSpriteArray(serialized.FindProperty("idleFaceSequenceTwo"), sequenceTwo);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(renderer);
        }

        private static int ValidateScenes(Sprite[] sequenceOne, Sprite[] sequenceTwo)
        {
            int validated = ValidateScene(RagdollScenePath, sequenceOne, sequenceTwo);
            validated += ValidateScene(CandyLabScenePath, sequenceOne, sequenceTwo);
            return validated;
        }

        private static int ValidateScene(string scenePath, Sprite[] sequenceOne, Sprite[] sequenceTwo)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RagdollAnimationController[] controllers = Object.FindObjectsOfType<RagdollAnimationController>(true);
            if (controllers.Length == 0)
                throw new InvalidOperationException($"No RagdollAnimationController exists in {scenePath}.");

            for (int i = 0; i < controllers.Length; i++)
            {
                SerializedObject serialized = new SerializedObject(controllers[i]);
                SpriteRenderer renderer = serialized.FindProperty("idleFaceRenderer").objectReferenceValue as SpriteRenderer;
                if (renderer == null || renderer.transform.parent == null ||
                    renderer.transform.parent.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException($"{controllers[i].name} does not have an explicit head idle-face overlay reference.");
                if (renderer.enabled)
                    throw new InvalidOperationException($"{controllers[i].name} idle-face overlay must begin disabled until the Idle state.");

                ValidateSpriteArray(serialized.FindProperty("idleFaceSequenceOne"), sequenceOne, controllers[i].name);
                ValidateSpriteArray(serialized.FindProperty("idleFaceSequenceTwo"), sequenceTwo, controllers[i].name);
                if (!Mathf.Approximately(serialized.FindProperty("idleFaceFramesPerSecond").floatValue, FramesPerSecond))
                    throw new InvalidOperationException($"{controllers[i].name} idle-face frame rate is not {FramesPerSecond:F0} FPS.");
            }
            return controllers.Length;
        }

        private static void SetSpriteArray(SerializedProperty property, Sprite[] sprites)
        {
            property.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }

        private static void ValidateSpriteArray(SerializedProperty property, Sprite[] expected, string owner)
        {
            if (property.arraySize != expected.Length)
                throw new InvalidOperationException($"{owner} has {property.arraySize} frames where {expected.Length} are required.");
            for (int i = 0; i < expected.Length; i++)
                if (property.GetArrayElementAtIndex(i).objectReferenceValue != expected[i])
                    throw new InvalidOperationException($"{owner} frame {i} is missing or out of order.");
        }

        private static int CompareFrameNames(Sprite left, Sprite right)
        {
            int leftNumber;
            int rightNumber;
            bool leftValid = int.TryParse(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(left)), out leftNumber);
            bool rightValid = int.TryParse(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(right)), out rightNumber);
            if (leftValid && rightValid) return leftNumber.CompareTo(rightNumber);
            return string.CompareOrdinal(left.name, right.name);
        }

        private static Transform FindChildContaining(Transform root, string value)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i].name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return children[i];
            return null;
        }

        private static void RestoreScene(string scenePath)
        {
            if (!string.IsNullOrEmpty(scenePath) && File.Exists(scenePath))
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
    }
}
#endif
