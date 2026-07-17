using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.U2D;

namespace KickTheBuddy.Editor
{
    /// <summary>Audits and applies the project's measured mobile performance budget.</summary>
    public static class AndroidPerformanceSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string CandyFolder = "Assets/GameData/Art/Candies";
        private const string CandyAtlasPath = CandyFolder + "/Candies.spriteatlas";
        private const string LauncherIconPath = "Assets/GameData/Art/testIcon.png";
        private const string DebrisLayerName = "DeathDebris";

        [MenuItem("Tools/Performance/Apply Android 60 FPS Budget")]
        public static void ApplyAndroidPerformanceMenu() => ApplyAndroidPerformance();

        public static void ApplyAndroidPerformanceBatch() => ApplyAndroidPerformance();

        [MenuItem("Tools/Performance/Validate Android 60 FPS Budget")]
        public static void ValidateAndroidPerformanceMenu() => ValidateAndroidPerformance();

        public static void ValidateAndroidPerformanceBatch() => ValidateAndroidPerformance();

        public static void BuildAndroidReleaseBatch()
        {
            ValidateAndroidPerformance();
            ConfigureAndroidBuildTarget(buildAppBundle: true);
            string outputDirectory = Path.GetFullPath("Builds/Android");
            Directory.CreateDirectory(outputDirectory);
            string[] scenes = GetValidatedBuildScenes();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDirectory, "AIThon.aab"),
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Android build failed: {report.summary.result}, errors={report.summary.totalErrors}.");
            Debug.Log($"ANDROID_RELEASE_BUILD passed output={options.locationPathName} " +
                      $"sizeBytes={report.summary.totalSize} duration={report.summary.totalTime}");
        }

        [MenuItem("Tools/Performance/Build Android Testing APK")]
        public static void BuildAndroidTestingApkMenu() => BuildAndroidTestingApkBatch();

        /// <summary>
        /// Produces a device-installable, non-development APK with the same optimized settings as release.
        /// This is intentionally separate from the Play Store AAB path above.
        /// </summary>
        public static void BuildAndroidTestingApkBatch()
        {
            ValidateAndroidPerformance();
            ConfigureAndroidBuildTarget(buildAppBundle: false);

            string outputDirectory = Path.GetFullPath("Builds/Android");
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "AIThon_Test.apk");
            string[] scenes = GetValidatedBuildScenes();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Android testing APK failed: {report.summary.result}, " +
                    $"errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}.");
            }

            var file = new FileInfo(outputPath);
            Debug.Log($"ANDROID_TEST_APK_BUILD_OK output={outputPath} sizeBytes={file.Length} " +
                      $"duration={report.summary.totalTime} warnings={report.summary.totalWarnings} " +
                      $"scenes={scenes.Length} package={PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)}");
        }

        [MenuItem("Tools/Performance/Audit Android Gameplay Scene")]
        public static void AuditSceneMenu()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AuditScene(scene);
        }

        public static void AuditSceneBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AuditScene(scene);
        }

        private static void ApplyAndroidPerformance()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int removedBodies = ConvertCandyFillToVisualOnly(scene);
            ConfigureAnimationTintBudget(scene);
            ConfigureCoinCanvas(scene);
            ConfigureDeathVfxBudget(scene);
            CreateCandyAtlas();
            ConfigureAndroidTextureImports();
            ConfigureAndroidPlayer();
            ConfigureSimulationBudget();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AuditScene(scene);
            Debug.Log($"ANDROID_PERF_APPLIED removedCandyPhysics={removedBodies} target=60fps " +
                      "physicsCatchup=0.10s arm64=IL2CPP candyAtlas=enabled debrisSelfCollision=disabled");
        }

        private static void ValidateAndroidPerformance()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Rigidbody2D[] bodies = FindSceneComponents<Rigidbody2D>(scene);
            int activeDynamicBodies = bodies.Count(body => body.gameObject.activeInHierarchy &&
                                                           body.simulated &&
                                                           body.bodyType == RigidbodyType2D.Dynamic);
            RagdollCandyFill2D[] fills = FindSceneComponents<RagdollCandyFill2D>(scene);
            int candyPhysics = 0;
            Rigidbody2D[] sceneBodies = FindSceneComponents<Rigidbody2D>(scene);
            for (int i = 0; i < sceneBodies.Length; i++)
                if (HasAncestorNamed(sceneBodies[i].transform, "CandiesFill")) candyPhysics++;
            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i].FillRoot != null)
                    candyPhysics += fills[i].FillRoot.GetComponentsInChildren<Rigidbody2D>(true).Length;
                GameObject[] visuals = fills[i].CandyVisuals;
                for (int j = 0; j < visuals.Length; j++)
                    if (visuals[j] != null &&
                        (visuals[j].GetComponent<Rigidbody2D>() != null || visuals[j].GetComponent<Collider2D>() != null))
                        candyPhysics++;
            }

            CoinFlyVFXController coins = FindSceneComponents<CoinFlyVFXController>(scene).FirstOrDefault();
            Canvas isolatedCanvas = coins != null ? coins.GetComponent<Canvas>() : null;
            bool playerReady = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) == ScriptingImplementation.IL2CPP &&
                               PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64;
            SerializedObject timeSettings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            bool catchupReady = timeSettings.FindProperty("Maximum Allowed Timestep").floatValue <= .1001f;
            if (activeDynamicBodies > 8 || candyPhysics != 0 || isolatedCanvas == null ||
                AssetDatabase.LoadAssetAtPath<SpriteAtlas>(CandyAtlasPath) == null || !playerReady || !catchupReady)
            {
                throw new InvalidOperationException(
                    $"Android budget failed: activeDynamic={activeDynamicBodies}, candyPhysics={candyPhysics}, " +
                    $"isolatedCoinCanvas={isolatedCanvas != null}, playerReady={playerReady}, " +
                    $"catchupReady={catchupReady}.");
            }

            Debug.Log($"ANDROID_PERF_VALIDATION passed activeDynamicBodies={activeDynamicBodies} " +
                      $"candyPhysics={candyPhysics} rigidbodiesTotal={bodies.Length} ARM64_IL2CPP=true");
        }

        private static int ConvertCandyFillToVisualOnly(Scene scene)
        {
            int removed = 0;
            Rigidbody2D[] authoredFillBodies = FindSceneComponents<Rigidbody2D>(scene);
            for (int i = 0; i < authoredFillBodies.Length; i++)
            {
                Rigidbody2D body = authoredFillBodies[i];
                if (!HasAncestorNamed(body.transform, "CandiesFill")) continue;
                Collider2D[] colliders = body.GetComponents<Collider2D>();
                for (int j = 0; j < colliders.Length; j++)
                {
                    Undo.DestroyObjectImmediate(colliders[j]);
                    removed++;
                }
                Undo.DestroyObjectImmediate(body);
                removed++;
            }
            RagdollCandyFill2D[] fills = FindSceneComponents<RagdollCandyFill2D>(scene);
            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i].FillRoot != null)
                {
                    Rigidbody2D[] fillBodies = fills[i].FillRoot.GetComponentsInChildren<Rigidbody2D>(true);
                    for (int j = 0; j < fillBodies.Length; j++)
                    {
                        Collider2D[] bodyColliders = fillBodies[j].GetComponents<Collider2D>();
                        for (int k = 0; k < bodyColliders.Length; k++)
                        {
                            Undo.DestroyObjectImmediate(bodyColliders[k]);
                            removed++;
                        }
                        Undo.DestroyObjectImmediate(fillBodies[j]);
                        removed++;
                    }
                }
                GameObject[] visuals = fills[i].CandyVisuals;
                for (int j = 0; j < visuals.Length; j++)
                {
                    GameObject visual = visuals[j];
                    if (visual == null) continue;
                    Collider2D[] colliders = visual.GetComponents<Collider2D>();
                    for (int k = 0; k < colliders.Length; k++)
                    {
                        Undo.DestroyObjectImmediate(colliders[k]);
                        removed++;
                    }
                    Rigidbody2D body = visual.GetComponent<Rigidbody2D>();
                    if (body == null) continue;
                    Undo.DestroyObjectImmediate(body);
                    removed++;
                }
            }
            return removed;
        }

        private static void ConfigureAnimationTintBudget(Scene scene)
        {
            RagdollController controller = FindSceneComponents<RagdollController>(scene)
                .FirstOrDefault(candidate => candidate.gameObject.activeInHierarchy);
            RagdollAnimationController animation = controller != null
                ? controller.GetComponent<RagdollAnimationController>()
                : null;
            if (controller == null || animation == null) return;
            var renderers = new List<SpriteRenderer>(controller.Parts.Count);
            for (int i = 0; i < controller.Parts.Count; i++)
                if (controller.Parts[i].Visual != null) renderers.Add(controller.Parts[i].Visual);
            // Parts can be runtime-only until the rig initializes. Never erase authored tint
            // references when this editor tool opens a scene outside Play Mode.
            if (renderers.Count == 0)
            {
                RagdollPartHealth[] authoredParts = controller.GetComponentsInChildren<RagdollPartHealth>(true);
                for (int i = 0; i < authoredParts.Length; i++)
                {
                    SpriteRenderer visual = authoredParts[i].GetComponent<SpriteRenderer>();
                    if (visual != null && !renderers.Contains(visual)) renderers.Add(visual);
                }
            }
            SerializedObject data = new SerializedObject(animation);
            if (renderers.Count > 0)
                AssignObjectArray(data.FindProperty("bodyRenderers"), renderers.Cast<UnityEngine.Object>().ToArray());
            SerializedProperty faceFrameRate = data.FindProperty("faceFramesPerSecond");
            if (faceFrameRate != null) faceFrameRate.floatValue = 24f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(animation);
        }

        private static void ConfigureCoinCanvas(Scene scene)
        {
            CoinFlyVFXController coins = FindSceneComponents<CoinFlyVFXController>(scene).FirstOrDefault();
            if (coins == null) return;
            Canvas isolated = coins.GetComponent<Canvas>();
            if (isolated == null) isolated = Undo.AddComponent<Canvas>(coins.gameObject);
            isolated.overrideSorting = true;
            isolated.sortingOrder = 50;
            Image[] images = coins.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++) images[i].raycastTarget = false;
            EditorUtility.SetDirty(isolated);
        }

        private static void ConfigureDeathVfxBudget(Scene scene)
        {
            int debrisLayer = EnsureLayer(DebrisLayerName);
            Physics2D.IgnoreLayerCollision(debrisLayer, debrisLayer, true);
            RagdollVFXController vfx = FindSceneComponents<RagdollVFXController>(scene)
                .FirstOrDefault(candidate => candidate.gameObject.activeInHierarchy &&
                                             new SerializedObject(candidate).FindProperty("candyDebrisBodies").arraySize > 0);
            if (vfx == null) return;
            SerializedObject data = new SerializedObject(vfx);
            data.FindProperty("maximumActiveCandyDebris").intValue = 18;
            data.FindProperty("maximumActiveGlassShards").intValue = 8;
            data.FindProperty("debrisLifetime").floatValue = 4f;
            ConfigureDebrisBodies(data.FindProperty("candyDebrisBodies"), debrisLayer);
            ConfigureDebrisBodies(data.FindProperty("glassShardBodies"), debrisLayer);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vfx);
        }

        private static void ConfigureDebrisBodies(SerializedProperty bodies, int layer)
        {
            for (int i = 0; i < bodies.arraySize; i++)
            {
                Rigidbody2D body = bodies.GetArrayElementAtIndex(i).objectReferenceValue as Rigidbody2D;
                if (body == null) continue;
                body.gameObject.layer = layer;
                body.simulated = false;
                body.interpolation = RigidbodyInterpolation2D.None;
                body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
                body.sleepMode = RigidbodySleepMode2D.StartAsleep;
                body.gameObject.SetActive(false);
                EditorUtility.SetDirty(body);
            }
        }

        private static void CreateCandyAtlas()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(CandyAtlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, CandyAtlasPath);
            }
            UnityEngine.Object[] oldPackables = atlas.GetPackables();
            if (oldPackables.Length > 0) atlas.Remove(oldPackables);
            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CandyFolder);
            atlas.Add(new[] { folder });
            atlas.SetIncludeInBuild(true);
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                enableRotation = false,
                enableTightPacking = true,
                padding = 2
            });
            atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                filterMode = FilterMode.Bilinear,
                sRGB = true
            });
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 1024,
                format = TextureImporterFormat.ETC2_RGBA8,
                compressionQuality = 50
            });
            EditorUtility.SetDirty(atlas);
        }

        private static void ConfigureAndroidTextureImports()
        {
            string[] folders =
            {
                CandyFolder,
                "Assets/GameData/Art/Themes/Bg/Glass characters",
                "Assets/GameData/Art/Themes/Cracks",
                "Assets/GameData/Art/VFX"
            };
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", folders);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.mipmapEnabled = false;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = path.StartsWith(CandyFolder, StringComparison.Ordinal) ? 256 : 1024;
                android.format = importer.DoesSourceTextureHaveAlpha()
                    ? TextureImporterFormat.ETC2_RGBA8
                    : TextureImporterFormat.ETC2_RGB4;
                android.compressionQuality = 50;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureAndroidPlayer()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Medium);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.optimizedFramePacing = true;
            EditorUserBuildSettings.buildAppBundle = true;
        }

        private static void ConfigureAndroidBuildTarget(bool buildAppBundle)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unity could not switch to the installed Android build target.");

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Medium);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.optimizedFramePacing = true;
            ConfigureLauncherIconImport();
            EditorUserBuildSettings.buildAppBundle = buildAppBundle;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureLauncherIconImport()
        {
            TextureImporter importer = AssetImporter.GetAtPath(LauncherIconPath) as TextureImporter;
            if (importer == null)
                throw new FileNotFoundException("Android launcher icon is missing.", LauncherIconPath);

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            bool requiresReimport = importer.textureCompression != TextureImporterCompression.Uncompressed ||
                                    !android.overridden ||
                                    android.textureCompression != TextureImporterCompression.Uncompressed ||
                                    android.format != TextureImporterFormat.RGBA32;
            if (!requiresReimport) return;

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 512;
            android.format = TextureImporterFormat.RGBA32;
            android.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static string[] GetValidatedBuildScenes()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Android build has no enabled scenes in Build Settings.");

            for (int i = 0; i < scenes.Length; i++)
            {
                if (File.Exists(Path.GetFullPath(scenes[i]))) continue;
                throw new FileNotFoundException($"Enabled build scene does not exist: {scenes[i]}", scenes[i]);
            }
            return scenes;
        }

        private static void ConfigureSimulationBudget()
        {
            Time.fixedDeltaTime = .02f;
            SerializedObject timeSettings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            timeSettings.FindProperty("Fixed Timestep").floatValue = .02f;
            timeSettings.FindProperty("Maximum Allowed Timestep").floatValue = .1f;
            timeSettings.ApplyModifiedPropertiesWithoutUndo();
            Physics2D.velocityIterations = 6;
            Physics2D.positionIterations = 3;
            Physics2D.reuseCollisionCallbacks = true;

            int previousQuality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(2, false);
            QualitySettings.vSyncCount = 0;
            QualitySettings.pixelLightCount = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;
            QualitySettings.antiAliasing = 0;
            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.SetQualityLevel(previousQuality, false);
        }

        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;
            SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = layerName;
                tags.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            throw new InvalidOperationException("No free user layer is available for death debris.");
        }

        private static void AssignObjectArray(SerializedProperty property, UnityEngine.Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void AuditScene(Scene scene)
        {
            Rigidbody2D[] bodies = FindSceneComponents<Rigidbody2D>(scene);
            Collider2D[] colliders = FindSceneComponents<Collider2D>(scene);
            SpriteRenderer[] sprites = FindSceneComponents<SpriteRenderer>(scene);
            ParticleSystem[] particles = FindSceneComponents<ParticleSystem>(scene);
            MonoBehaviour[] behaviours = FindSceneComponents<MonoBehaviour>(scene);

            int activeBodies = 0;
            int simulatedBodies = 0;
            int dynamicBodies = 0;
            var bodyGroups = new Dictionary<string, int>(StringComparer.Ordinal);
            var activeBodyParents = new Dictionary<string, int>(StringComparer.Ordinal);
            var activeDynamicPaths = new List<string>(64);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                if (body.gameObject.activeInHierarchy) activeBodies++;
                if (body.simulated) simulatedBodies++;
                if (body.simulated && body.bodyType == RigidbodyType2D.Dynamic) dynamicBodies++;
                string group = TopLevelPath(body.transform);
                bodyGroups[group] = bodyGroups.TryGetValue(group, out int count) ? count + 1 : 1;
                if (body.gameObject.activeInHierarchy)
                {
                    string parent = body.transform.parent != null ? body.transform.parent.name : "<root>";
                    activeBodyParents[parent] = activeBodyParents.TryGetValue(parent, out int parentCount)
                        ? parentCount + 1
                        : 1;
                    if (body.simulated && body.bodyType == RigidbodyType2D.Dynamic)
                        activeDynamicPaths.Add(HierarchyPath(body.transform));
                }
            }

            string groups = string.Join(", ", bodyGroups.OrderByDescending(pair => pair.Value)
                .Select(pair => pair.Key + "=" + pair.Value));
            string parents = string.Join(", ", activeBodyParents.OrderByDescending(pair => pair.Value)
                .Select(pair => pair.Key + "=" + pair.Value));
            string dynamicPaths = string.Join(" | ", activeDynamicPaths);
            Debug.Log(
                $"ANDROID_PERF_AUDIT scene={scene.name} gameObjects={CountGameObjects(scene)} " +
                $"behaviours={behaviours.Length} rigidbodies={bodies.Length} activeBodies={activeBodies} " +
                $"simulatedBodies={simulatedBodies} dynamicBodies={dynamicBodies} colliders={colliders.Length} " +
                $"sprites={sprites.Length} particles={particles.Length} groups=[{groups}] activeParents=[{parents}] " +
                $"activeDynamicPaths=[{dynamicPaths}]");
        }

        private static T[] FindSceneComponents<T>(Scene scene) where T : Component
        {
            var results = new List<T>(128);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                results.AddRange(roots[i].GetComponentsInChildren<T>(true));
            return results.ToArray();
        }

        private static int CountGameObjects(Scene scene)
        {
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                count += roots[i].GetComponentsInChildren<Transform>(true).Length;
            return count;
        }

        private static string TopLevelPath(Transform value)
        {
            Transform current = value;
            while (current.parent != null) current = current.parent;
            return current.name;
        }

        private static string HierarchyPath(Transform value)
        {
            string path = value.name;
            Transform current = value.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private static bool HasAncestorNamed(Transform value, string name)
        {
            Transform current = value.parent;
            while (current != null)
            {
                if (string.Equals(current.name, name, StringComparison.Ordinal)) return true;
                current = current.parent;
            }
            return false;
        }
    }
}
