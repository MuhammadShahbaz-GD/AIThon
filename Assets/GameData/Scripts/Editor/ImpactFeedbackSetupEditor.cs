#if UNITY_EDITOR
using System;
using System.IO;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Creates and validates the shared, mobile-budgeted impact feedback assets.</summary>
    public static class ImpactFeedbackSetupEditor
    {
        private const string ProfilePath = "Assets/GameData/Materials/Ragdoll VFX Profile.asset";
        private const string ArtRoot = "Assets/GameData/Art/VFX";
        private const string FumeTexturePath = ArtRoot + "/CollisionFumeSoft.png";
        private const string GlassTexturePath = ArtRoot + "/GlassShard.png";
        private const string MaterialRoot = "Assets/GameData/Materials/VFX";
        private const string FumeMaterialPath = MaterialRoot + "/MAT_CollisionFume.mat";
        private const string GlassMaterialPath = MaterialRoot + "/MAT_ImpactGlassShard.mat";
        private const string PrefabRoot = "Assets/GameData/Prefabs/VFX";
        private const string FumePrefabPath = PrefabRoot + "/VFX_Ragdoll_CollisionFume.prefab";
        private const string GlassPrefabPath = PrefabRoot + "/VFX_Ragdoll_ImpactGlass.prefab";
        private const string RagdollScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string CandyLabScenePath = "Assets/GameData/Scene/CandyLab.unity";
        private const float MinimumVisibleCrackSize = .08f;
        private const float LightVisibleCrackMaximumSize = .16f;
        private const float HeavyVisibleCrackMaximumSize = .24f;

        [MenuItem("Tools/Ragdoll/VFX/Setup Enhanced Impact Feedback")]
        public static void SetupFromMenu() => SetupBatch();

        public static void SetupBatch()
        {
            EnsureFolder("Assets/GameData/Art", "VFX");
            EnsureFolder("Assets/GameData/Materials", "VFX");
            EnsureFolder("Assets/GameData/Prefabs", "VFX");

            Texture2D fumeTexture = CreateSoftFumeTexture();
            Texture2D glassTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GlassTexturePath);
            if (glassTexture == null)
                throw new InvalidOperationException("Impact glass texture is missing: " + GlassTexturePath);

            Material fumeMaterial = CreateOrUpdateMaterial(FumeMaterialPath, "MAT_CollisionFume", fumeTexture);
            Material glassMaterial = CreateOrUpdateMaterial(GlassMaterialPath, "MAT_ImpactGlassShard", glassTexture);
            ParticleSystem fumePrefab = ConfigureFumePrefab(fumeMaterial);
            ParticleSystem glassPrefab = ConfigureGlassPrefab(glassMaterial);

            RagdollVFXProfile profile = AssetDatabase.LoadAssetAtPath<RagdollVFXProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("Ragdoll VFX profile is missing: " + ProfilePath);
            SerializedObject profileData = new SerializedObject(profile);
            profileData.FindProperty("collisionFumePrefab").objectReferenceValue = fumePrefab;
            profileData.FindProperty("impactGlassPrefab").objectReferenceValue = glassPrefab;
            profileData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateBatch();
            Debug.Log("IMPACT_FEEDBACK_SETUP_OK: one shared soft-white fume emitter and one shared falling-glass emitter are profile-wired.");
        }

        [MenuItem("Tools/Ragdoll/VFX/Validate Enhanced Impact Feedback")]
        public static void ValidateFromMenu() => ValidateBatch();

        public static void ValidateBatch()
        {
            RagdollVFXProfile profile = AssetDatabase.LoadAssetAtPath<RagdollVFXProfile>(ProfilePath);
            if (profile == null || profile.CollisionFumePrefab == null || profile.ImpactGlassPrefab == null)
                throw new InvalidOperationException("Impact VFX profile references are incomplete.");

            ParticleSystem fumes = profile.CollisionFumePrefab;
            ParticleSystem.VelocityOverLifetimeModule velocity = fumes.velocityOverLifetime;
            if (!velocity.enabled || velocity.x.mode != velocity.y.mode || velocity.x.mode != velocity.z.mode)
                throw new InvalidOperationException("Fume X/Y/Z velocity curves must be enabled and use the same curve mode.");
            if (fumes.main.maxParticles > 32 || fumes.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture == null)
                throw new InvalidOperationException("Fume texture or mobile particle budget is invalid.");

            ParticleSystem glass = profile.ImpactGlassPrefab;
            if (glass.main.maxParticles > 40 || glass.main.gravityModifier.constantMax < .9f)
                throw new InvalidOperationException("Impact glass gravity or mobile particle budget is invalid.");
            if (glass.main.startSize.constantMin < MinimumVisibleCrackSize - .001f ||
                glass.main.startSize.constantMax < HeavyVisibleCrackMaximumSize - .001f)
                throw new InvalidOperationException("Impact glass preview sizes are too small to remain readable.");
            ParticleSystemRenderer glassRenderer = glass.GetComponent<ParticleSystemRenderer>();
            if (glassRenderer == null || glassRenderer.sharedMaterial == null || glassRenderer.sharedMaterial.mainTexture == null)
                throw new InvalidOperationException("Impact glass material/texture is missing.");

            Debug.Log("IMPACT_FEEDBACK_VALIDATION_OK: textured fumes<=32, falling glass<=40, shared profile references valid.");
        }

        [MenuItem("Tools/Ragdoll/VFX/Apply Visible Hit Crack Sizes")]
        public static void ApplyVisibleCrackSizesFromMenu() => ApplyVisibleCrackSizes(false);

        public static void ApplyVisibleCrackSizesBatch() => ApplyVisibleCrackSizes(true);

        private static void ApplyVisibleCrackSizes(bool batchMode)
        {
            string originalScene = SceneManager.GetActiveScene().path;
            try
            {
                ConfigureGlassPrefabPreviewSize();
                int configured = ConfigureVisibleCrackSizesInScene(RagdollScenePath);
                configured += ConfigureVisibleCrackSizesInScene(CandyLabScenePath);
                AssetDatabase.SaveAssets();
                ValidateBatch();
                ValidateVisibleCrackSizesInScene(RagdollScenePath);
                ValidateVisibleCrackSizesInScene(CandyLabScenePath);
                Debug.Log($"VISIBLE_HIT_CRACK_SIZES_OK: {configured} controller(s), min={MinimumVisibleCrackSize:F2}, lightMax={LightVisibleCrackMaximumSize:F2}, heavyMax={HeavyVisibleCrackMaximumSize:F2}.");
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

        private static void ConfigureGlassPrefabPreviewSize()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GlassPrefabPath);
            if (root == null) throw new InvalidOperationException("Unable to load impact glass prefab.");
            try
            {
                ParticleSystem system = root.GetComponent<ParticleSystem>();
                if (system == null) throw new InvalidOperationException("Impact glass prefab has no ParticleSystem.");
                ParticleSystem.MainModule main = system.main;
                main.startSize = new ParticleSystem.MinMaxCurve(MinimumVisibleCrackSize, HeavyVisibleCrackMaximumSize);
                PrefabUtility.SaveAsPrefabAsset(root, GlassPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int ConfigureVisibleCrackSizesInScene(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RagdollVFXController[] controllers = UnityEngine.Object.FindObjectsOfType<RagdollVFXController>(true);
            if (controllers.Length == 0)
                throw new InvalidOperationException("No RagdollVFXController exists in " + scenePath + ".");

            for (int i = 0; i < controllers.Length; i++)
            {
                Undo.RecordObject(controllers[i], "Configure Visible Hit Crack Sizes");
                SerializedObject data = new SerializedObject(controllers[i]);
                data.FindProperty("minimumImpactGlassSize").floatValue = MinimumVisibleCrackSize;
                data.FindProperty("lightImpactGlassMaximumSize").floatValue = LightVisibleCrackMaximumSize;
                data.FindProperty("heavyImpactGlassMaximumSize").floatValue = HeavyVisibleCrackMaximumSize;
                data.ApplyModifiedProperties();
                EditorUtility.SetDirty(controllers[i]);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Unable to save " + scenePath + ".");
            return controllers.Length;
        }

        private static void ValidateVisibleCrackSizesInScene(string scenePath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RagdollVFXController[] controllers = UnityEngine.Object.FindObjectsOfType<RagdollVFXController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].MinimumImpactGlassSize < MinimumVisibleCrackSize - .001f ||
                    controllers[i].LightImpactGlassMaximumSize < LightVisibleCrackMaximumSize - .001f ||
                    controllers[i].HeavyImpactGlassMaximumSize < HeavyVisibleCrackMaximumSize - .001f)
                    throw new InvalidOperationException(controllers[i].name + " has unreadable impact glass size settings.");
            }
        }

        private static void RestoreScene(string scenePath)
        {
            if (!string.IsNullOrEmpty(scenePath) && File.Exists(scenePath))
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private static Texture2D CreateSoftFumeTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + .5f) / size * 2f - 1f;
                    float ny = (y + .5f) / size * 2f - 1f;
                    float radial = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    float cloud = Mathf.Lerp(.76f, 1f, Mathf.PerlinNoise(x * .115f + 2.3f, y * .115f + 7.1f));
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Pow(radial, 1.65f) * cloud * 235f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(FumeTexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(FumeTexturePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(FumeTexturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Unable to import the generated fume texture.");
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 64;
            android.format = TextureImporterFormat.ETC2_RGBA8;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FumeTexturePath);
        }

        private static Material CreateOrUpdateMaterial(string path, string materialName, Texture2D texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("Sprites/Default shader is unavailable.");
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = texture;
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ParticleSystem ConfigureFumePrefab(Material material)
        {
            GameObject root = LoadOrCreateParticlePrefab(FumePrefabPath, "VFX_Ragdoll_CollisionFume");
            ParticleSystem system = root.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.duration = .85f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .78f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.12f, .34f);
            main.startSize = new ParticleSystem.MinMaxCurve(.16f, .32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .98f, .96f, .72f), new Color(.9f, .97f, 1f, .9f));

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .045f;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-.16f, .16f);
            velocity.y = new ParticleSystem.MinMaxCurve(.42f, .85f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(new Color(1f, .98f, .95f), 0f), new GradientColorKey(new Color(.84f, .93f, 1f), 1f) },
                new[] { new GradientAlphaKey(.84f, 0f), new GradientAlphaKey(.5f, .48f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, .68f), new Keyframe(.55f, 1.18f), new Keyframe(1f, 1.5f)));

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = .1f;
            noise.frequency = 1.15f;
            noise.scrollSpeed = .2f;
            noise.octaveCount = 1;
            noise.damping = true;

            DisableExpensiveModules(system);
            SaveParticlePrefab(root, FumePrefabPath, material, 109);
            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(FumePrefabPath);
        }

        private static ParticleSystem ConfigureGlassPrefab(Material material)
        {
            GameObject root = LoadOrCreateParticlePrefab(GlassPrefabPath, "VFX_Ragdoll_ImpactGlass");
            ParticleSystem system = root.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 36;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .95f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(MinimumVisibleCrackSize, HeavyVisibleCrackMaximumSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.9f, .98f, 1f, .92f), new Color(1f, .72f, .82f, 1f));

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = false;

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = false;
            rotation.z = new ParticleSystem.MinMaxCurve(-5.2f, 5.2f);

            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, .72f, .84f), 1f) },
                new[] { new GradientAlphaKey(.98f, 0f), new GradientAlphaKey(.8f, .68f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.72f, .78f), new Keyframe(1f, .12f)));

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = false;
            DisableExpensiveModules(system);
            SaveParticlePrefab(root, GlassPrefabPath, material, 111);
            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(GlassPrefabPath);
        }

        private static GameObject LoadOrCreateParticlePrefab(string path, string objectName)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                GameObject source = new GameObject(objectName, typeof(ParticleSystem));
                PrefabUtility.SaveAsPrefabAsset(source, path);
                UnityEngine.Object.DestroyImmediate(source);
            }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) throw new InvalidOperationException("Unable to load VFX prefab: " + path);
            root.name = objectName;
            return root;
        }

        private static void DisableExpensiveModules(ParticleSystem system)
        {
            ParticleSystem.CollisionModule collision = system.collision;
            collision.enabled = false;
            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = false;
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters = system.subEmitters;
            subEmitters.enabled = false;
        }

        private static void SaveParticlePrefab(GameObject root, string path, Material material, int sortingOrder)
        {
            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            renderer.enableGPUInstancing = false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
