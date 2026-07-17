#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors and validates the zero-damage Jelly contact presentation for Level 2.</summary>
    public static class JellyLiquidMechanicSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/CandyLab.unity";
        private const string LevelAssetPath = "Assets/GameData/Materials/Gameplay/Level_02.asset";
        private const string JellyPrefabPath = "Assets/GameData/Prefabs/Gameplay/Jelly.prefab";
        private const string LollipopPrefabPath = "Assets/GameData/Prefabs/Gameplay/Lollipop.prefab";
        private const string TexturePath = "Assets/GameData/Art/VFX/TEX_Jelly_Splat.png";
        private const string MaterialPath = "Assets/GameData/Materials/VFX/MAT_Jelly_Splat.mat";
        private const string PoolPrefabPath = "Assets/GameData/Prefabs/VFX/VFX_Jelly_ContactPool.prefab";
        private const string PoolName = "Jelly Liquid Effects";
        private const int PoolSize = 6;

        private static readonly Color LiquidColor = new Color(.63f, .22f, .95f, .82f);

        [MenuItem("Tools/Game/Setup Smooth Jelly Liquid Mechanic")]
        public static void SetupFromMenu()
        {
            SetupInternal();
            ValidateInternal();
        }

        [MenuItem("Tools/Game/Validate Smooth Jelly Liquid Mechanic")]
        public static void ValidateFromMenu() => ValidateInternal();

        /// <summary>Keeps the liquid mechanic intact when the complete two-level authoring tool is rerun.</summary>
        public static void SetupForLevelBuild()
        {
            SetupInternal();
            ValidateInternal();
        }

        public static void SetupJellyLiquidMechanicBatch()
        {
            try
            {
                SetupInternal();
                ValidateInternal();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateJellyLiquidMechanicBatch()
        {
            try
            {
                ValidateInternal();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void SetupInternal()
        {
            EnsureFolder("Assets/GameData/Art/VFX");
            EnsureFolder("Assets/GameData/Materials/VFX");
            EnsureFolder("Assets/GameData/Prefabs/VFX");

            Sprite splatSprite = GenerateSplatSprite();
            Material splatMaterial = CreateSplatMaterial(splatSprite.texture);
            GameObject poolPrefab = CreatePoolPrefab(splatSprite, splatMaterial);
            ConfigureJellyPrefabAsPresentationOnly();
            ConfigureLevelObjective();
            ConfigureScene(poolPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Sprite GenerateSplatSprite()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + .5f) / size * 2f - 1f;
                    float ny = (y + .5f) / size * 2f - 1f;
                    float body = 1f - Mathf.Sqrt(nx * nx / .88f + ny * ny / .34f);
                    float leftDrop = Blob(nx, ny, -.52f, -.42f, .22f, .42f);
                    float centerDrop = Blob(nx, ny, .02f, -.56f, .17f, .5f);
                    float rightDrop = Blob(nx, ny, .55f, -.35f, .2f, .34f);
                    float coverage = Mathf.Max(body, Mathf.Max(leftDrop, Mathf.Max(centerDrop, rightDrop)));
                    if (coverage <= 0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage * 9f) * 255f);
                    float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(nx, ny), new Vector2(-.3f, .2f)) * 2.6f);
                    byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(225f, 255f, shine));
                    pixels[y * size + x] = new Color32(shade, shade, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(TexturePath), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not import the Jelly splat texture.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 96f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 128;
            android.format = TextureImporterFormat.ETC2_RGBA8;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
            if (sprite == null) throw new InvalidOperationException("Generated Jelly splat sprite could not be loaded.");
            return sprite;
        }

        private static float Blob(float x, float y, float cx, float cy, float radiusX, float radiusY)
        {
            float dx = (x - cx) / radiusX;
            float dy = (y - cy) / radiusY;
            return 1f - Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Material CreateSplatMaterial(Texture texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = "MAT_Jelly_Splat" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else material.shader = shader;
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreatePoolPrefab(Sprite sprite, Material material)
        {
            GameObject root = new GameObject(PoolName);
            JellyContactVFXController controller = root.AddComponent<JellyContactVFXController>();
            GameObject splatRoot = new GameObject("Splat Pool");
            splatRoot.transform.SetParent(root.transform, false);
            SpriteRenderer[] renderers = new SpriteRenderer[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject child = new GameObject("Splat " + (i + 1), typeof(SpriteRenderer));
                child.transform.SetParent(splatRoot.transform, false);
                SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.color = LiquidColor;
                renderer.sortingOrder = 114;
                renderer.enabled = false;
                renderers[i] = renderer;
            }

            GameObject particleObject = new GameObject("Shared Droplets", typeof(ParticleSystem));
            particleObject.transform.SetParent(root.transform, false);
            ParticleSystem particle = particleObject.GetComponent<ParticleSystem>();
            ConfigureDropletParticle(particle, material);

            SerializedObject data = new SerializedObject(controller);
            SerializedProperty pool = data.FindProperty("splatRenderers");
            pool.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
                pool.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            data.FindProperty("dropletParticle").objectReferenceValue = particle;
            data.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PoolPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new InvalidOperationException("Could not save the Jelly contact pool prefab.");
            return prefab;
        }

        private static void ConfigureDropletParticle(ParticleSystem particle, Material material)
        {
            ParticleSystem.MainModule main = particle.main;
            main.duration = .45f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.4f, .7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.04f, .085f);
            main.startColor = LiquidColor;
            main.gravityModifier = .55f;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = false;
            ParticleSystem.VelocityOverLifetimeModule velocity = particle.velocityOverLifetime;
            velocity.enabled = false;
            ParticleSystem.NoiseModule noise = particle.noise;
            noise.enabled = false;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(.72f, .34f, 1f), 0f),
                    new GradientColorKey(new Color(.34f, .08f, .55f), 1f)
                },
                new[] { new GradientAlphaKey(.9f, 0f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = particle.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule size = particle.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, .15f)));

            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 115;
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void ConfigureJellyPrefabAsPresentationOnly()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(JellyPrefabPath);
            try
            {
                RagdollAttackManager2D attack = root.GetComponent<RagdollAttackManager2D>();
                if (attack == null) throw new InvalidOperationException("Jelly prefab is missing its attack descriptor.");
                attack.Configure(RagdollAttackType.Jelly, 0f, 0f, 0f, 0f);
                EditorUtility.SetDirty(attack);
                PrefabUtility.SaveAsPrefabAsset(root, JellyPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureLevelObjective()
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (level == null) throw new InvalidOperationException("Level 2 definition is missing.");
            SerializedObject data = new SerializedObject(level);
            data.FindProperty("objectiveText").stringValue =
                "Break the character with the hard lollipop; use sticky jelly to annoy and distract it.";
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static void ConfigureScene(GameObject poolPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SandboxToolInput2D input = FindSceneComponent<SandboxToolInput2D>(scene);
            if (input == null) input = CreateLevelTools(scene);
            GameObject toolsRoot = input.gameObject;
            toolsRoot.SetActive(true);
            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            RagdollController ragdoll = FindActiveSceneComponent<RagdollController>(scene);
            if (jelly == null || jelly.Attack == null || ragdoll == null)
                throw new InvalidOperationException("CandyLab Jelly or active ragdoll references are incomplete.");
            RagdollAnimationController animation = ragdoll.GetComponent<RagdollAnimationController>();
            if (animation == null) throw new InvalidOperationException("Active ragdoll is missing RagdollAnimationController.");

            jelly.Attack.Configure(RagdollAttackType.Jelly, 0f, 0f, 0f, 0f);
            EditorUtility.SetDirty(jelly.Attack);
            PrefabUtility.RecordPrefabInstancePropertyModifications(jelly.Attack);

            Transform existing = toolsRoot.transform.Find(PoolName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            GameObject instance = PrefabUtility.InstantiatePrefab(poolPrefab, scene) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate the Jelly contact pool.");
            instance.name = PoolName;
            instance.transform.SetParent(toolsRoot.transform, false);
            JellyContactVFXController controller = instance.GetComponent<JellyContactVFXController>();
            SerializedObject references = new SerializedObject(controller);
            references.FindProperty("jellyTool").objectReferenceValue = jelly;
            references.FindProperty("ragdoll").objectReferenceValue = ragdoll;
            references.FindProperty("animationController").objectReferenceValue = animation;
            references.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ValidateInternal()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SandboxToolInput2D input = FindSceneComponent<SandboxToolInput2D>(scene);
            GameObject toolsRoot = input != null ? input.gameObject : null;
            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            RagdollController ragdoll = FindActiveSceneComponent<RagdollController>(scene);
            JellyContactVFXController liquid = toolsRoot != null
                ? toolsRoot.GetComponentInChildren<JellyContactVFXController>(true)
                : null;
            if (jelly == null || jelly.Attack == null || ragdoll == null || liquid == null)
                throw new InvalidOperationException("Jelly liquid scene composition is incomplete.");
            if (jelly.Attack.AttackType != RagdollAttackType.Jelly ||
                jelly.Attack.CalculateDamage(0f) != 0f || jelly.Attack.CalculateDamage(8f) != 0f ||
                jelly.Attack.CalculateDamage(100f) != 0f || jelly.Attack.CanDamage(ragdoll.gameObject))
                throw new InvalidOperationException("Jelly can still enter the ragdoll damage path.");

            SerializedObject data = new SerializedObject(liquid);
            if (data.FindProperty("jellyTool").objectReferenceValue != jelly ||
                data.FindProperty("ragdoll").objectReferenceValue != ragdoll ||
                data.FindProperty("animationController").objectReferenceValue == null ||
                data.FindProperty("dropletParticle").objectReferenceValue == null ||
                data.FindProperty("splatRenderers").arraySize != PoolSize)
                throw new InvalidOperationException("Jelly liquid pool references are not fully authored.");
            SerializedProperty splats = data.FindProperty("splatRenderers");
            for (int i = 0; i < splats.arraySize; i++)
                if (splats.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    throw new InvalidOperationException("Jelly liquid pool contains an unassigned splat renderer.");

            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (level == null || level.ObjectiveText.IndexOf("annoy", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Level 2 objective does not explain the Jelly nuisance mechanic.");
            if (CountMissingScripts(scene) != 0) throw new InvalidOperationException("CandyLab contains missing scripts.");

            Debug.Log("JELLY_LIQUID_MECHANIC_VALIDATION_OK: damage=0 at speeds 0/8/100, six pooled sliding splats, one shared droplet emitter, authored active-ragdoll references, annoyed reaction, missingScripts=0.");
        }

        private static SandboxTool2D FindTool(SandboxToolInput2D input, SandboxToolKind kind)
        {
            if (input == null) return null;
            for (int i = 0; i < input.Tools.Count; i++)
                if (input.Tools[i] != null && input.Tools[i].Kind == kind) return input.Tools[i];
            return null;
        }

        private static SandboxToolInput2D CreateLevelTools(Scene scene)
        {
            GameObject lollipopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LollipopPrefabPath);
            GameObject jellyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JellyPrefabPath);
            if (lollipopPrefab == null || jellyPrefab == null)
                throw new InvalidOperationException("Level 2 tool prefabs are missing.");

            GameObject root = new GameObject("Level 2 Tools");
            SceneManager.MoveGameObjectToScene(root, scene);
            GameObject lollipopObject = PrefabUtility.InstantiatePrefab(lollipopPrefab, scene) as GameObject;
            GameObject jellyObject = PrefabUtility.InstantiatePrefab(jellyPrefab, scene) as GameObject;
            if (lollipopObject == null || jellyObject == null)
                throw new InvalidOperationException("Could not create the Level 2 tools.");
            lollipopObject.transform.SetParent(root.transform, true);
            jellyObject.transform.SetParent(root.transform, true);
            lollipopObject.transform.SetPositionAndRotation(new Vector3(-4.25f, -2.2f, 0f), Quaternion.Euler(0f, 0f, -12f));
            jellyObject.transform.SetPositionAndRotation(new Vector3(4.25f, -2.75f, 0f), Quaternion.identity);

            SandboxTool2D lollipop = lollipopObject.GetComponent<SandboxTool2D>();
            SandboxTool2D jelly = jellyObject.GetComponent<SandboxTool2D>();
            SandboxToolInput2D input = root.AddComponent<SandboxToolInput2D>();
            SerializedObject data = new SerializedObject(input);
            data.FindProperty("inputCamera").objectReferenceValue = FindSceneComponent<Camera>(scene);
            SerializedProperty tools = data.FindProperty("tools");
            tools.arraySize = 2;
            tools.GetArrayElementAtIndex(0).objectReferenceValue = lollipop;
            tools.GetArrayElementAtIndex(1).objectReferenceValue = jelly;
            data.FindProperty("toolLayers").intValue = 1 << jellyObject.layer;
            data.ApplyModifiedPropertiesWithoutUndo();
            return input;
        }

        private static T FindActiveSceneComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T[] values = roots[i].GetComponentsInChildren<T>(true);
                for (int j = 0; j < values.Length; j++)
                    if (values[j].gameObject.activeInHierarchy) return values[j];
            }
            return null;
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

        private static int CountMissingScripts(Scene scene)
        {
            int count = 0;
            Stack<Transform> stack = new Stack<Transform>();
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
    }
}
#endif
