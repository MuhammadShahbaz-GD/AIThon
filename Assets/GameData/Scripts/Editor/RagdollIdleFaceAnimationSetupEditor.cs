#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KickTheBuddy.Editor
{
    /// <summary>
    /// One-click importer, scene authoring pass, and validator for the event-driven face system.
    /// Existing Laugh/Smile GUIDs are preserved while the old Idle folders are migrated.
    /// </summary>
    public static class RagdollIdleFaceAnimationSetupEditor
    {
        private const string SourceRoot = @"D:\Daily Share AiThon\Talha\Laughing Animations";
        private const string FaceRoot = "Assets/GameData/Animations/Face";
        private const string ExpressionsRoot = FaceRoot + "/Expressions";
        private const string LegacyIdleRoot = FaceRoot + "/Idle";
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string OverlayName = "Face Expression Renderer";
        private const string LegacyOverlayName = "Idle Face Animation";
        private const float FramesPerSecond = 24f;
        private const int MobileTextureSize = 256;

        private sealed class ExpressionSpec
        {
            public readonly string SourceFolder;
            public readonly string AssetFolder;
            public readonly string SerializedField;
            public readonly int ExpectedFrames;

            public ExpressionSpec(string sourceFolder, string assetFolder, string serializedField, int expectedFrames)
            {
                SourceFolder = sourceFolder;
                AssetFolder = ExpressionsRoot + "/" + assetFolder;
                SerializedField = serializedField;
                ExpectedFrames = expectedFrames;
            }
        }

        private static readonly ExpressionSpec[] Expressions =
        {
            new ExpressionSpec("smiling", "Smile", "smileFrames", 28),
            new ExpressionSpec("Laughing", "Laugh", "laughFrames", 48),
            new ExpressionSpec("shocked", "Shock", "shockFrames", 36),
            new ExpressionSpec("crying", "Cry", "cryFrames", 26),
            new ExpressionSpec("Depressed", "Depressed", "depressedFrames", 34)
        };

        [MenuItem("Tools/Ragdoll/Face Expressions/Import And Configure")]
        public static void SetupFromMenu() => Setup(false);

        public static void SetupBatch() => Setup(true);

        [MenuItem("Tools/Ragdoll/Face Expressions/Validate")]
        public static void ValidateFromMenu() => Validate(false);

        public static void ValidateBatch() => Validate(true);

        private static void Setup(bool batchMode)
        {
            string originalScene = SceneManager.GetActiveScene().path;
            try
            {
                if (!batchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EnsureAssetFolder(ExpressionsRoot);
                MigrateLegacyFolder(LegacyIdleRoot + "/Laugh_01", ExpressionsRoot + "/Laugh");
                MigrateLegacyFolder(LegacyIdleRoot + "/Laugh_02", ExpressionsRoot + "/Smile");

                for (int i = 0; i < Expressions.Length; i++) SyncSourceFrames(Expressions[i]);
                if (AssetDatabase.IsValidFolder(LegacyIdleRoot)) AssetDatabase.DeleteAsset(LegacyIdleRoot);

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                for (int i = 0; i < Expressions.Length; i++) ConfigureSpriteFolder(Expressions[i]);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                Sprite[][] frames = LoadAllFrames();
                int configured = ConfigureScene(frames);
                AssetDatabase.SaveAssets();
                int validated = ValidateSceneAndAssets(frames);
                Debug.Log($"FACE_EXPRESSIONS_SETUP_OK: imported 172 frames, configured {configured} controller(s), validated {validated}; Smile/normal, Shock/hit+drag, Cry/combo+break, Depressed/KO+low-health+jelly, rare idle Laugh.");
                Complete(batchMode, originalScene, 0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Complete(batchMode, originalScene, 1);
                if (!batchMode) throw;
            }
        }

        private static void Validate(bool batchMode)
        {
            string originalScene = SceneManager.GetActiveScene().path;
            try
            {
                if (!batchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                Sprite[][] frames = LoadAllFrames();
                int validated = ValidateSceneAndAssets(frames);
                Debug.Log($"FACE_EXPRESSIONS_VALIDATION_OK: {validated} controller(s), five exact frame arrays, explicit overlays, death-hide wiring, mobile imports, and no procedural Life Face objects.");
                Complete(batchMode, originalScene, 0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Complete(batchMode, originalScene, 1);
                if (!batchMode) throw;
            }
        }

        private static void Complete(bool batchMode, string originalScene, int exitCode)
        {
            if (batchMode)
            {
                EditorApplication.Exit(exitCode);
                return;
            }

            if (!string.IsNullOrEmpty(originalScene) && File.Exists(ToAbsolutePath(originalScene)))
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
        }

        private static void MigrateLegacyFolder(string oldPath, string newPath)
        {
            if (!AssetDatabase.IsValidFolder(oldPath) || AssetDatabase.IsValidFolder(newPath)) return;
            EnsureAssetFolder(Path.GetDirectoryName(newPath)?.Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(oldPath, newPath);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException($"Could not migrate {oldPath} to {newPath}: {error}");
        }

        private static void SyncSourceFrames(ExpressionSpec spec)
        {
            string sourceFolder = Path.Combine(SourceRoot, spec.SourceFolder);
            if (!Directory.Exists(sourceFolder))
            {
                // CI and other workstations can reconfigure from the committed in-project sprites.
                string existingFolder = ToAbsolutePath(spec.AssetFolder);
                int existingCount = Directory.Exists(existingFolder)
                    ? Directory.GetFiles(existingFolder, "*.png", SearchOption.TopDirectoryOnly).Length
                    : 0;
                if (existingCount == spec.ExpectedFrames) return;
                throw new DirectoryNotFoundException("Missing face source folder and incomplete in-project fallback: " + sourceFolder);
            }

            string[] sourceFiles = Directory.GetFiles(sourceFolder, "*.png", SearchOption.TopDirectoryOnly);
            Array.Sort(sourceFiles, StringComparer.OrdinalIgnoreCase);
            if (sourceFiles.Length != spec.ExpectedFrames)
                throw new InvalidOperationException($"{spec.SourceFolder} expected {spec.ExpectedFrames} PNGs but found {sourceFiles.Length}.");

            EnsureAssetFolder(spec.AssetFolder);
            string targetFolder = ToAbsolutePath(spec.AssetFolder);
            Directory.CreateDirectory(targetFolder);

            var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                string fileName = Path.GetFileName(sourceFiles[i]);
                expectedNames.Add(fileName);
                string destination = Path.Combine(targetFolder, fileName);
                if (!FilesHaveSameContent(sourceFiles[i], destination))
                    File.Copy(sourceFiles[i], destination, true);
            }

            string[] currentFiles = Directory.GetFiles(targetFolder, "*.png", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < currentFiles.Length; i++)
            {
                string fileName = Path.GetFileName(currentFiles[i]);
                if (!expectedNames.Contains(fileName))
                    AssetDatabase.DeleteAsset(spec.AssetFolder + "/" + fileName);
            }
        }

        private static void ConfigureSpriteFolder(ExpressionSpec spec)
        {
            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { spec.AssetFolder });
            if (textureGuids.Length != spec.ExpectedFrames)
                throw new InvalidOperationException($"{spec.AssetFolder} imported {textureGuids.Length}/{spec.ExpectedFrames} textures.");

            for (int i = 0; i < textureGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing TextureImporter for " + path);

                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                bool alreadyConfigured = importer.textureType == TextureImporterType.Sprite &&
                                         importer.spriteImportMode == SpriteImportMode.Single &&
                                         Mathf.Approximately(importer.spritePixelsPerUnit, 500f) &&
                                         importer.alphaIsTransparency && importer.sRGBTexture &&
                                         !importer.mipmapEnabled && !importer.isReadable &&
                                         importer.wrapMode == TextureWrapMode.Clamp &&
                                         importer.filterMode == FilterMode.Bilinear &&
                                         importer.npotScale == TextureImporterNPOTScale.None &&
                                         importer.maxTextureSize == MobileTextureSize &&
                                         importer.textureCompression == TextureImporterCompression.CompressedHQ &&
                                         importer.compressionQuality == 80 && android != null &&
                                         android.overridden && android.maxTextureSize == MobileTextureSize &&
                                         android.format == TextureImporterFormat.ASTC_6x6 &&
                                         android.compressionQuality == 80;
                if (alreadyConfigured) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 500f;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = MobileTextureSize;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.compressionQuality = 80;

                android = importer.GetPlatformTextureSettings("Android");
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = MobileTextureSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.compressionQuality = 80;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static Sprite[][] LoadAllFrames()
        {
            var result = new Sprite[Expressions.Length][];
            for (int i = 0; i < Expressions.Length; i++) result[i] = LoadFrames(Expressions[i]);
            return result;
        }

        private static Sprite[] LoadFrames(ExpressionSpec spec)
        {
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { spec.AssetFolder });
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            if (paths.Length != spec.ExpectedFrames)
                throw new InvalidOperationException($"{spec.AssetFolder} expected {spec.ExpectedFrames} sprites but found {paths.Length}.");

            var frames = new Sprite[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
                if (frames[i] == null) throw new InvalidOperationException("Could not load face frame: " + paths[i]);
            }
            return frames;
        }

        private static int ConfigureScene(Sprite[][] frames)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollAnimationController[] controllers = Object.FindObjectsOfType<RagdollAnimationController>(true);
            int configured = 0;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].gameObject.scene != scene) continue;
                ConfigureController(controllers[i], frames);
                configured++;
            }

            if (configured == 0) throw new InvalidOperationException("RagdollSandbox has no RagdollAnimationController.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + ScenePath);
            return configured;
        }

        private static void ConfigureController(RagdollAnimationController controller, Sprite[][] frames)
        {
            Transform head = FindHead(controller);
            if (head == null) throw new InvalidOperationException(controller.name + " has no authored Head part.");

            Transform overlay = head.Find(OverlayName);
            Transform legacyOverlay = head.Find(LegacyOverlayName);
            if (overlay == null && legacyOverlay != null) overlay = legacyOverlay;
            if (overlay == null)
            {
                GameObject created = new GameObject(OverlayName, typeof(SpriteRenderer));
                created.transform.SetParent(head, false);
                overlay = created.transform;
            }
            overlay.name = OverlayName;
            if (legacyOverlay != null && legacyOverlay != overlay) Object.DestroyImmediate(legacyOverlay.gameObject);

            Transform procedural = head.Find("Life Face");
            if (procedural != null) Object.DestroyImmediate(procedural.gameObject);

            SpriteRenderer renderer = overlay.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = overlay.gameObject.AddComponent<SpriteRenderer>();
            SpriteRenderer headRenderer = head.GetComponent<SpriteRenderer>();
            if (headRenderer != null)
            {
                renderer.sortingLayerID = headRenderer.sortingLayerID;
                renderer.sortingOrder = headRenderer.sortingOrder + 15;
            }
            renderer.color = Color.white;
            renderer.sprite = frames[0][0];
            renderer.enabled = true;
            renderer.maskInteraction = SpriteMaskInteraction.None;

            var serialized = new SerializedObject(controller);
            RequireProperty(serialized, "faceRenderer").objectReferenceValue = renderer;
            RequireProperty(serialized, "faceFramesPerSecond").floatValue = FramesPerSecond;
            SetSpriteArray(RequireProperty(serialized, "smileFrames"), frames[0]);
            SetSpriteArray(RequireProperty(serialized, "laughFrames"), frames[1]);
            SetSpriteArray(RequireProperty(serialized, "shockFrames"), frames[2]);
            SetSpriteArray(RequireProperty(serialized, "cryFrames"), frames[3]);
            SetSpriteArray(RequireProperty(serialized, "depressedFrames"), frames[4]);
            RequireProperty(serialized, "normalHitShockDuration").floatValue = 1.55f;
            RequireProperty(serialized, "comboCryDuration").floatValue = 1.9f;
            RequireProperty(serialized, "maximumDamageCryDuration").floatValue = 2.2f;
            RequireProperty(serialized, "limbBreakCryDuration").floatValue = 2.7f;
            RequireProperty(serialized, "comboCryThreshold").intValue = 3;
            RequireProperty(serialized, "maximumDamageThreshold").floatValue = 6f;
            RequireProperty(serialized, "maximumDamageHealthRatio").floatValue = .02f;
            RequireProperty(serialized, "depressedHealthThreshold").floatValue = .3f;
            RequireProperty(serialized, "idleLaughChancePerSmileLoop").floatValue = .07f;
            AssignBodyRenderers(controller, RequireProperty(serialized, "bodyRenderers"), renderer);
            Vector2 offset = RequireProperty(serialized, "facePositionOffset").vector2Value;
            Vector2 scale = RequireProperty(serialized, "faceScale").vector2Value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            overlay.localPosition = new Vector3(offset.x, offset.y, -.02f);
            overlay.localScale = new Vector3(Mathf.Max(.01f, scale.x), Mathf.Max(.01f, scale.y), 1f);
            IncludeFaceInDeathRenderers(controller, renderer);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(renderer);
        }

        private static void AssignBodyRenderers(
            RagdollAnimationController controller,
            SerializedProperty property,
            SpriteRenderer face)
        {
            var renderers = new List<SpriteRenderer>(8);
            IReadOnlyList<RagdollController.RagdollPart> parts = controller.GetComponent<RagdollController>().Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                SpriteRenderer visual = parts[i] != null ? parts[i].Visual : null;
                if (visual != null && visual != face && !renderers.Contains(visual)) renderers.Add(visual);
            }

            // Editor-only fallback for an inactive legacy authored rig whose runtime cache is not initialized.
            if (renderers.Count == 0)
            {
                RagdollPartHealth[] healthParts = controller.GetComponentsInChildren<RagdollPartHealth>(true);
                for (int i = 0; i < healthParts.Length; i++)
                {
                    SpriteRenderer visual = healthParts[i].GetComponent<SpriteRenderer>();
                    if (visual != null && visual != face && !renderers.Contains(visual)) renderers.Add(visual);
                }
            }

            if (renderers.Count == 0)
                throw new InvalidOperationException(controller.name + " has no explicit ragdoll-part renderers.");

            property.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }

        private static void IncludeFaceInDeathRenderers(RagdollAnimationController controller, SpriteRenderer face)
        {
            RagdollVFXController vfx = controller.GetComponentInChildren<RagdollVFXController>(true);
            if (vfx == null) return;
            var serialized = new SerializedObject(vfx);
            SerializedProperty renderers = serialized.FindProperty("characterRenderers");
            if (renderers == null) throw new InvalidOperationException("RagdollVFXController.characterRenderers is missing.");
            for (int i = 0; i < renderers.arraySize; i++)
                if (renderers.GetArrayElementAtIndex(i).objectReferenceValue == face) return;
            int index = renderers.arraySize;
            renderers.arraySize++;
            renderers.GetArrayElementAtIndex(index).objectReferenceValue = face;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vfx);
        }

        private static Transform FindHead(RagdollAnimationController animation)
        {
            RagdollController ragdoll = animation.GetComponent<RagdollController>();
            if (ragdoll != null)
            {
                IReadOnlyList<RagdollController.RagdollPart> parts = ragdoll.Parts;
                for (int i = 0; i < parts.Count; i++)
                    if (parts[i] != null && parts[i].PartType == RagdollPartType.Head && parts[i].Body != null)
                        return parts[i].Body.transform;
            }

            Transform[] children = animation.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (string.Equals(children[i].name, "Head", StringComparison.OrdinalIgnoreCase)) return children[i];
            return null;
        }

        private static int ValidateSceneAndAssets(Sprite[][] frames)
        {
            if (AssetDatabase.IsValidFolder(LegacyIdleRoot))
                throw new InvalidOperationException("Legacy Assets/GameData/Animations/Face/Idle still exists.");
            for (int i = 0; i < Expressions.Length; i++) ValidateImports(Expressions[i]);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollAnimationController[] controllers = Object.FindObjectsOfType<RagdollAnimationController>(true);
            int validated = 0;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].gameObject.scene != scene) continue;
                ValidateController(controllers[i], frames);
                validated++;
            }
            if (validated == 0) throw new InvalidOperationException("No face controller was validated in RagdollSandbox.");

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] descendants = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < descendants.Length; j++)
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(descendants[j].gameObject) > 0)
                        throw new InvalidOperationException("Missing script found on " + descendants[j].name);
            }
            return validated;
        }

        private static bool FilesHaveSameContent(string source, string destination)
        {
            if (!File.Exists(destination)) return false;
            var sourceInfo = new FileInfo(source);
            var destinationInfo = new FileInfo(destination);
            if (sourceInfo.Length != destinationInfo.Length) return false;

            const int bufferSize = 8192;
            var sourceBuffer = new byte[bufferSize];
            var destinationBuffer = new byte[bufferSize];
            using (FileStream sourceStream = File.OpenRead(source))
            using (FileStream destinationStream = File.OpenRead(destination))
            {
                while (true)
                {
                    int sourceRead = sourceStream.Read(sourceBuffer, 0, bufferSize);
                    int destinationRead = destinationStream.Read(destinationBuffer, 0, bufferSize);
                    if (sourceRead != destinationRead) return false;
                    if (sourceRead == 0) return true;
                    for (int i = 0; i < sourceRead; i++)
                        if (sourceBuffer[i] != destinationBuffer[i]) return false;
                }
            }
        }

        private static void ValidateController(RagdollAnimationController controller, Sprite[][] frames)
        {
            var serialized = new SerializedObject(controller);
            SpriteRenderer renderer = RequireProperty(serialized, "faceRenderer").objectReferenceValue as SpriteRenderer;
            if (renderer == null || renderer.name != OverlayName || !renderer.enabled)
                throw new InvalidOperationException(controller.name + " does not have an enabled explicit Face Expression Renderer.");
            if (renderer.sprite != frames[0][0])
                throw new InvalidOperationException(controller.name + " does not preview the first Smile frame.");

            for (int i = 0; i < Expressions.Length; i++)
                ValidateSpriteArray(RequireProperty(serialized, Expressions[i].SerializedField), frames[i], controller.name);
            if (RequireProperty(serialized, "bodyRenderers").arraySize == 0)
                throw new InvalidOperationException(controller.name + " has no explicit body renderer references.");

            Transform head = FindHead(controller);
            if (head == null || renderer.transform.parent != head)
                throw new InvalidOperationException(controller.name + " face is not parented directly to Head.");
            if (head.Find("Life Face") != null || head.Find(LegacyOverlayName) != null)
                throw new InvalidOperationException(controller.name + " still contains an old procedural/idle face object.");

            RagdollVFXController vfx = controller.GetComponentInChildren<RagdollVFXController>(true);
            if (vfx != null && !ArrayContains(new SerializedObject(vfx).FindProperty("characterRenderers"), renderer))
                throw new InvalidOperationException(controller.name + " face will remain visible after the death explosion.");
        }

        private static void ValidateImports(ExpressionSpec spec)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { spec.AssetFolder });
            if (guids.Length != spec.ExpectedFrames)
                throw new InvalidOperationException($"{spec.AssetFolder} contains {guids.Length}/{spec.ExpectedFrames} textures.");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                TextureImporterPlatformSettings android = importer != null
                    ? importer.GetPlatformTextureSettings("Android")
                    : null;
                if (importer == null || importer.textureType != TextureImporterType.Sprite ||
                    importer.mipmapEnabled || importer.isReadable || importer.spritePixelsPerUnit != 500f ||
                    importer.maxTextureSize != MobileTextureSize || android == null || !android.overridden ||
                    android.maxTextureSize != MobileTextureSize || android.format != TextureImporterFormat.ASTC_6x6)
                    throw new InvalidOperationException("Face import settings are not mobile-safe: " + path);
            }
        }

        private static void SetSpriteArray(SerializedProperty property, Sprite[] frames)
        {
            property.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
        }

        private static void ValidateSpriteArray(SerializedProperty property, Sprite[] expected, string owner)
        {
            if (property.arraySize != expected.Length)
                throw new InvalidOperationException($"{owner}.{property.name} has {property.arraySize}/{expected.Length} frames.");
            for (int i = 0; i < expected.Length; i++)
                if (property.GetArrayElementAtIndex(i).objectReferenceValue != expected[i])
                    throw new InvalidOperationException($"{owner}.{property.name}[{i}] is not in source order.");
        }

        private static bool ArrayContains(SerializedProperty property, Object value)
        {
            if (property == null) return false;
            for (int i = 0; i < property.arraySize; i++)
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
            return false;
        }

        private static SerializedProperty RequireProperty(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new InvalidOperationException(serialized.targetObject.name + " is missing serialized field " + name + ".");
            return property;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath)) return;
            string normalized = assetPath.Replace('\\', '/');
            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string ToAbsolutePath(string assetPath) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }
}
#endif
