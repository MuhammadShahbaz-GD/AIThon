#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Idempotent authoring for the wall-smash tutorial and candy-tool laboratory.</summary>
    public static class TwoLevelCandyGameplaySetupEditor
    {
        private const string SplashPath = "Assets/GameData/Scene/Splash.unity";
        private const string MenuPath = "Assets/GameData/Scene/MainMenu.unity";
        private const string LevelOneScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string LevelTwoScenePath = "Assets/GameData/Scene/CandyLab.unity";
        private const string LevelOneAssetPath = "Assets/GameData/Materials/Gameplay/Level_01.asset";
        private const string LevelTwoAssetPath = "Assets/GameData/Materials/Gameplay/Level_02.asset";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";
        private const string PrefabFolder = "Assets/GameData/Prefabs/Gameplay";
        private const string ArtFolder = "Assets/GameData/Art/Gameplay Tools";
        private const string LollipopPrefabPath = PrefabFolder + "/Lollipop.prefab";
        private const string JellyPrefabPath = PrefabFolder + "/Jelly.prefab";
        private const string LollipopSpritePath = ArtFolder + "/Lollipop.png";
        private const string JellySpritePath = ArtFolder + "/Jelly.png";
        private const string LollipopMaterialPath = "Assets/GameData/Materials/Gameplay/PMAT_LollipopHard.physicsMaterial2D";
        private const string JellyMaterialPath = "Assets/GameData/Materials/Gameplay/PMAT_JellySoft.physicsMaterial2D";
        private const string ToolsRootName = "Level 2 Interactive Tools";
        private const string ToolLayerName = "SandboxTool";
        private const float LevelOnePlayTime = LongFunBalanceSetupEditor.LevelPlayTime;
        private const float LevelTwoPlayTime = LongFunBalanceSetupEditor.LevelPlayTime;

        [MenuItem("Tools/Game/Build Level 1 And Level 2 Candy Gameplay")]
        public static void BuildFromMenu() => SingleSceneLevelsSetupEditor.BuildFromMenu();

        public static void BuildBatch() => SingleSceneLevelsSetupEditor.BuildBatch();

        [MenuItem("Tools/Game/Validate Level 1 And Level 2 Candy Gameplay")]
        public static void ValidateFromMenu() => SingleSceneLevelsSetupEditor.ValidateFromMenu();

        public static void ValidateBatch() => SingleSceneLevelsSetupEditor.ValidateBatch();

        [MenuItem("Tools/Game/Apply Per-Level Play Times")]
        public static void ApplyPlayTimesFromMenu() => ApplyPlayTimes(false);

        public static void ApplyPlayTimesBatch() => ApplyPlayTimes(true);

        private static void Build(bool exitWhenDone)
        {
            try
            {
                EnsureFolder(PrefabFolder);
                EnsureFolder(ArtFolder);
                GenerateToolSprites();
                int toolLayer = EnsureLayer(ToolLayerName);
                PhysicsMaterial2D hardMaterial = CreateMaterial(LollipopMaterialPath, .72f, .08f);
                PhysicsMaterial2D jellyMaterial = CreateMaterial(JellyMaterialPath, .86f, .12f);
                GameObject lollipopPrefab = CreateLollipopPrefab(toolLayer, hardMaterial);
                GameObject jellyPrefab = CreateJellyPrefab(toolLayer, jellyMaterial);

                LevelDefinition levelOne = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelOneAssetPath);
                if (levelOne == null) throw new InvalidOperationException("Level_01.asset is missing.");
                LevelDefinition levelTwo = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelTwoAssetPath);
                if (levelTwo == null)
                {
                    levelTwo = ScriptableObject.CreateInstance<LevelDefinition>();
                    AssetDatabase.CreateAsset(levelTwo, LevelTwoAssetPath);
                }
                ConfigureLevel(levelOne, "level_01", "Level 1 - Wall Smash", LevelOneScenePath,
                    "Repeatedly throw the character into the walls until the glass finally breaks.",
                    LongFunBalanceSetupEditor.HeadHealth, LevelOnePlayTime, 150, 120, 220, 350);
                ConfigureLevel(levelTwo, "level_02", "Level 2 - Candy Lab", LevelTwoScenePath,
                    "Use repeated hard lollipop hits to break the character; sticky jelly only annoys it.",
                    LongFunBalanceSetupEditor.HeadHealth, LevelTwoPlayTime, 250, 180, 320, 520);

                LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
                if (catalog == null) throw new InvalidOperationException("Level Catalog.asset is missing.");
                SerializedObject catalogData = new SerializedObject(catalog);
                SerializedProperty levels = catalogData.FindProperty("levels");
                levels.arraySize = 2;
                levels.GetArrayElementAtIndex(0).objectReferenceValue = levelOne;
                levels.GetArrayElementAtIndex(1).objectReferenceValue = levelTwo;
                catalogData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);

                ConfigureLevelOneScene();
                ConfigureLevelTwoScene(lollipopPrefab, jellyPrefab, toolLayer);
                JellyLiquidMechanicSetupEditor.SetupForLevelBuild();
                LongFunBalanceSetupEditor.ApplyForLevelBuild();
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(SplashPath, true),
                    new EditorBuildSettingsScene(MenuPath, true),
                    new EditorBuildSettingsScene(LevelOneScenePath, true),
                    new EditorBuildSettingsScene(LevelTwoScenePath, true)
                };

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateInternal();
                Debug.Log("TWO_LEVEL_CANDY_GAMEPLAY_BUILD_OK: Level 1 wall-smash and Level 2 lollipop/jelly gameplay are authored and sequential.");
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

        private static void ApplyPlayTimes(bool exitWhenDone)
        {
            try
            {
                SetLevelPlayTime(LevelOneAssetPath, LevelOnePlayTime);
                SetLevelPlayTime(LevelTwoAssetPath, LevelTwoPlayTime);
                AssetDatabase.SaveAssets();
                ValidateInternal();
                Debug.Log($"PER_LEVEL_PLAY_TIMES_OK: Level 1={LevelOnePlayTime:F0}s, Level 2={LevelTwoPlayTime:F0}s, minimum={LevelDefinition.MinimumPlayTimeSeconds:F0}s.");
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void ValidateInternal()
        {
            SingleSceneLevelsSetupEditor.ValidateOrThrow();
        }

        private static void SetLevelPlayTime(string assetPath, float seconds)
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (level == null) throw new InvalidOperationException("Missing level definition: " + assetPath);
            SerializedObject data = new SerializedObject(level);
            data.FindProperty("timeLimit").floatValue = Mathf.Max(LevelDefinition.MinimumPlayTimeSeconds, seconds);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static void ConfigureLevel(LevelDefinition level, string id, string displayName, string scenePath,
            string objective, float targetDamage, float timeLimit, int coins, int oneStar, int twoStars, int threeStars)
        {
            SerializedObject data = new SerializedObject(level);
            data.FindProperty("levelId").stringValue = id;
            data.FindProperty("displayName").stringValue = displayName;
            data.FindProperty("scenePath").stringValue = scenePath;
            data.FindProperty("objectiveText").stringValue = objective;
            data.FindProperty("completionRule").enumValueIndex = (int)LevelCompletionRule.CharacterDestroyed;
            data.FindProperty("targetDamage").floatValue = targetDamage;
            data.FindProperty("timeLimit").floatValue = timeLimit;
            data.FindProperty("completionCoins").intValue = coins;
            data.FindProperty("oneStarScore").intValue = oneStar;
            data.FindProperty("twoStarScore").intValue = twoStars;
            data.FindProperty("threeStarScore").intValue = threeStars;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static void ConfigureLevelOneScene()
        {
            Scene scene = EditorSceneManager.OpenScene(LevelOneScenePath, OpenSceneMode.Single);
            GameObject oldTools = FindRoot(scene, ToolsRootName);
            if (oldTools != null) UnityEngine.Object.DestroyImmediate(oldTools);
            ConfigureWalls(scene, 0f, 1.25f, 4f, LongFunBalanceSetupEditor.MaximumRawDamagePerHit);
            SetAuthoredCameraSize(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!EditorSceneManager.SaveScene(scene, LevelTwoScenePath, true))
                throw new InvalidOperationException("Could not create the Level 2 CandyLab scene copy.");
        }

        private static void ConfigureLevelTwoScene(GameObject lollipopPrefab, GameObject jellyPrefab, int toolLayer)
        {
            Scene scene = EditorSceneManager.OpenScene(LevelTwoScenePath, OpenSceneMode.Single);
            ConfigureWalls(scene, 0f, .85f, 4.5f, 9f);
            GameObject oldTools = FindRoot(scene, ToolsRootName);
            if (oldTools != null) UnityEngine.Object.DestroyImmediate(oldTools);

            GameObject root = new GameObject(ToolsRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            GameObject lollipopObject = PrefabUtility.InstantiatePrefab(lollipopPrefab, scene) as GameObject;
            GameObject jellyObject = PrefabUtility.InstantiatePrefab(jellyPrefab, scene) as GameObject;
            if (lollipopObject == null || jellyObject == null) throw new InvalidOperationException("Could not instantiate Level 2 tool prefabs.");
            lollipopObject.transform.SetParent(root.transform, true);
            jellyObject.transform.SetParent(root.transform, true);
            lollipopObject.transform.SetPositionAndRotation(new Vector3(-4.25f, -2.2f, 0f), Quaternion.Euler(0f, 0f, -12f));
            jellyObject.transform.SetPositionAndRotation(new Vector3(4.25f, -2.75f, 0f), Quaternion.identity);

            SandboxToolInput2D input = root.AddComponent<SandboxToolInput2D>();
            SerializedObject inputData = new SerializedObject(input);
            inputData.FindProperty("inputCamera").objectReferenceValue = FindSceneComponent<Camera>(scene);
            SerializedProperty tools = inputData.FindProperty("tools");
            tools.arraySize = 2;
            tools.GetArrayElementAtIndex(0).objectReferenceValue = lollipopObject.GetComponent<SandboxTool2D>();
            tools.GetArrayElementAtIndex(1).objectReferenceValue = jellyObject.GetComponent<SandboxTool2D>();
            inputData.FindProperty("toolLayers").intValue = 1 << toolLayer;
            inputData.ApplyModifiedPropertiesWithoutUndo();

            SetAuthoredCameraSize(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureWalls(Scene scene, float baseDamage, float speedDamage, float minimumSpeed, float cap)
        {
            GameObject room = FindRoot(scene, "Room");
            if (room == null) throw new InvalidOperationException(scene.name + " is missing Room boundaries.");
            Collider2D[] boundaries = room.GetComponentsInChildren<Collider2D>(true);
            if (boundaries.Length == 0) throw new InvalidOperationException(scene.name + " has no boundary colliders.");
            for (int i = 0; i < boundaries.Length; i++)
            {
                RagdollAttackManager2D attack = boundaries[i].GetComponent<RagdollAttackManager2D>();
                if (attack == null) attack = boundaries[i].gameObject.AddComponent<RagdollAttackManager2D>();
                attack.Configure(RagdollAttackType.Wall, baseDamage, speedDamage, minimumSpeed, cap);
                EditorUtility.SetDirty(attack);
            }
        }

        private static void ValidateWalls(Scene scene)
        {
            GameObject room = FindRoot(scene, "Room");
            if (room == null) throw new InvalidOperationException(scene.name + " is missing Room.");
            Collider2D[] boundaries = room.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < boundaries.Length; i++)
                if (boundaries[i].GetComponent<RagdollAttackManager2D>() == null)
                    throw new InvalidOperationException(scene.name + "/Room/" + boundaries[i].name + " is missing its wall attack.");
        }

        private static GameObject CreateLollipopPrefab(int layer, PhysicsMaterial2D material)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LollipopSpritePath);
            if (sprite == null) throw new InvalidOperationException("Generated lollipop sprite could not be loaded.");
            GameObject root = new GameObject("Lollipop");
            root.layer = layer;
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.mass = 2.2f;
            body.gravityScale = 1.2f;
            body.drag = .22f;
            body.angularDrag = .55f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            CapsuleCollider2D collider = root.AddComponent<CapsuleCollider2D>();
            collider.direction = CapsuleDirection2D.Vertical;
            collider.size = new Vector2(.82f, 2.55f);
            collider.offset = new Vector2(0f, .05f);
            collider.sharedMaterial = material;
            TargetJoint2D dragJoint = root.AddComponent<TargetJoint2D>();
            dragJoint.enabled = false;
            RagdollAttackManager2D attack = root.AddComponent<RagdollAttackManager2D>();
            attack.Configure(RagdollAttackType.Lollipop, 3f, 1.4f, 3f,
                LongFunBalanceSetupEditor.MaximumRawDamagePerHit);
            SandboxTool2D tool = root.AddComponent<SandboxTool2D>();
            Transform visual = CreateVisual(root.transform, sprite, new Vector3(.66f, .66f, 1f));
            ConfigureTool(tool, SandboxToolKind.Lollipop, body, dragJoint, null, attack, visual, 1700f);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, LollipopPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateJellyPrefab(int layer, PhysicsMaterial2D material)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(JellySpritePath);
            if (sprite == null) throw new InvalidOperationException("Generated jelly sprite could not be loaded.");
            GameObject root = new GameObject("Jelly");
            root.layer = layer;
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.mass = .38f;
            body.gravityScale = 1.05f;
            body.drag = .75f;
            body.angularDrag = 1.2f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            CapsuleCollider2D collider = root.AddComponent<CapsuleCollider2D>();
            collider.direction = CapsuleDirection2D.Horizontal;
            collider.size = new Vector2(1.22f, .88f);
            collider.sharedMaterial = material;
            TargetJoint2D dragJoint = root.AddComponent<TargetJoint2D>();
            dragJoint.enabled = false;
            FixedJoint2D stickyJoint = root.AddComponent<FixedJoint2D>();
            stickyJoint.enabled = false;
            stickyJoint.enableCollision = false;
            RagdollAttackManager2D attack = root.AddComponent<RagdollAttackManager2D>();
            attack.Configure(RagdollAttackType.Jelly, 0f, 0f, 0f, 0f);
            SandboxTool2D tool = root.AddComponent<SandboxTool2D>();
            Transform visual = CreateVisual(root.transform, sprite, new Vector3(.72f, .72f, 1f));
            ConfigureTool(tool, SandboxToolKind.Jelly, body, dragJoint, stickyJoint, attack, visual, 900f);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, JellyPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static Transform CreateVisual(Transform parent, Sprite sprite, Vector3 scale)
        {
            GameObject visual = new GameObject("Visual", typeof(SpriteRenderer));
            visual.transform.SetParent(parent, false);
            visual.transform.localScale = scale;
            SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 25;
            return visual.transform;
        }

        private static void ConfigureTool(SandboxTool2D tool, SandboxToolKind kind, Rigidbody2D body,
            TargetJoint2D dragJoint, FixedJoint2D stickyJoint, RagdollAttackManager2D attack,
            Transform visual, float maximumForce)
        {
            SerializedObject data = new SerializedObject(tool);
            data.FindProperty("kind").enumValueIndex = (int)kind;
            data.FindProperty("body").objectReferenceValue = body;
            data.FindProperty("dragJoint").objectReferenceValue = dragJoint;
            data.FindProperty("stickyJoint").objectReferenceValue = stickyJoint;
            data.FindProperty("attack").objectReferenceValue = attack;
            data.FindProperty("visual").objectReferenceValue = visual;
            data.FindProperty("dragMaximumForce").floatValue = maximumForce;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static PhysicsMaterial2D CreateMaterial(string path, float friction, float bounciness)
        {
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (material == null)
            {
                material = new PhysicsMaterial2D(Path.GetFileNameWithoutExtension(path));
                AssetDatabase.CreateAsset(material, path);
            }
            material.friction = friction;
            material.bounciness = bounciness;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void GenerateToolSprites()
        {
            GenerateSprite(LollipopSpritePath, 128, 256, DrawLollipop);
            GenerateSprite(JellySpritePath, 128, 96, DrawJelly);
        }

        private static void GenerateSprite(string path, int width, int height, Func<int, int, Color32> pixel)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            Color32[] colors = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) colors[y * width + x] = pixel(x, y);
            texture.SetPixels32(colors);
            texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not configure generated sprite: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 256;
            android.format = TextureImporterFormat.ETC2_RGBA8;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static Color32 DrawLollipop(int x, int y)
        {
            Color32 clear = new Color32(0, 0, 0, 0);
            int dx = x - 64;
            int dy = y - 186;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            if (radius <= 52f)
            {
                float angle = Mathf.Atan2(dy, dx);
                float spiral = Mathf.Sin(angle * 3f + radius * .23f);
                Color32 blue = new Color32(45, 166, 255, 255);
                Color32 cyan = new Color32(105, 235, 255, 255);
                Color32 white = new Color32(246, 252, 255, 255);
                Color32 color = spiral > .35f ? white : (spiral > -.35f ? cyan : blue);
                if (dx < -12 && dy > 12 && radius < 32f) color = Color32.Lerp(color, white, .55f);
                color.a = (byte)Mathf.Clamp(Mathf.RoundToInt((52f - radius) * 255f), 0, 255);
                if (radius < 50.5f) color.a = 255;
                return color;
            }
            if (Mathf.Abs(dx) <= 5 && y >= 12 && y <= 143)
            {
                bool stripe = ((y + x * 2) / 13) % 2 == 0;
                return stripe ? new Color32(255, 247, 250, 255) : new Color32(255, 111, 170, 255);
            }
            return clear;
        }

        private static Color32 DrawJelly(int x, int y)
        {
            Color32 clear = new Color32(0, 0, 0, 0);
            float nx = (x - 64f) / 52f;
            float ny = (y - 48f) / 36f;
            bool body = nx * nx + ny * ny <= 1f && y >= 16;
            bool lobe = y < 26 &&
                        (Circle(x, y, 34, 23, 18) || Circle(x, y, 64, 20, 20) || Circle(x, y, 94, 23, 18));
            if (!body && !lobe) return clear;
            float edge = Mathf.Clamp01(1f - Mathf.Abs(nx));
            byte red = (byte)Mathf.Lerp(128f, 210f, edge);
            byte blue = (byte)Mathf.Lerp(214f, 255f, Mathf.Clamp01((y - 12f) / 75f));
            Color32 color = new Color32(red, 76, blue, 235);
            if (Circle(x, y, 48, 67, 10)) color = new Color32(245, 219, 255, 220);
            return color;
        }

        private static bool Circle(int x, int y, int cx, int cy, int radius)
        {
            int dx = x - cx;
            int dy = y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Invalid asset folder: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = name;
                tags.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            throw new InvalidOperationException("No free user layer is available for " + name + ".");
        }

        private static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T value = roots[i].GetComponentInChildren<T>(true);
                if (value != null) return value;
            }
            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            return null;
        }

        private static int CountMissingScripts(Scene scene)
        {
            int count = 0;
            var stack = new Stack<Transform>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) stack.Push(roots[i].transform);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(current.gameObject);
                for (int i = 0; i < current.childCount; i++) stack.Push(current.GetChild(i));
            }
            return count;
        }

        private static void SetAuthoredCameraSize(Scene scene)
        {
            Camera camera = FindSceneComponent<Camera>(scene);
            if (camera == null) throw new InvalidOperationException(scene.name + " is missing its camera.");
            camera.orthographicSize = 5.25f;
            EditorUtility.SetDirty(camera);
        }
    }
}
#endif
