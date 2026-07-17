#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.U2D;

namespace KickTheBuddy.Editor
{
    public static class GameplayVFXDeathSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string ProfilePath = "Assets/GameData/Materials/Ragdoll VFX Profile.asset";
        private const string PrefabRoot = "Assets/GameData/Prefabs/VFX";
        private const string ArtRoot = "Assets/GameData/Art/VFX";
        private const string CoinPath = ArtRoot + "/Coin.png";
        private const string ShardPath = ArtRoot + "/GlassShard.png";
        private const string BrokenPiecesRoot = ArtRoot + "/Broken Pieces";
        private const string CandyArtRoot = "Assets/GameData/Art/Candies";
        private const string MaterialPath = "Assets/GameData/Materials/VFX/VFX_Particle_Additive.mat";
        private const string FumeMaterialPath = "Assets/GameData/Materials/VFX/MAT_CollisionFume.mat";
        private const string DebrisMaterialPath = "Assets/GameData/Materials/VFX/PMAT_GlassDebris.physicsMaterial2D";
        private const string FumePrefabPath = PrefabRoot + "/VFX_Ragdoll_CollisionFume.prefab";
        private const string BrokenPiecesAtlasPath = BrokenPiecesRoot + "/Broken Pieces.spriteatlas";

        private static readonly string[] BrokenPieceNames =
        {
            "Brocken Piece 1.png", "Brocken Piece 2.png", "Brocken Piece 3.png", "Brocken Piece 4.png",
            "Brocken Piece 5.png", "Brocken Piece 6.png", "Brocken Piece 7.png", "Brocken Piece 8.png",
            "Brocken Piece 9.png", "Brocken Piece 10.png", "Brocken Piece 11.png", "Brocken Piece 12.png",
            "Brocken Piece 19.png", "Brocken Piece 21.png"
        };

        [MenuItem("Tools/Ragdoll/VFX/Setup Complete Gameplay VFX")]
        public static void SetupActiveScene()
        {
            SetupScene(EditorSceneManager.GetActiveScene());
        }

        public static void SetupSandboxBatch()
        {
            SetupScene(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));
        }

        [MenuItem("Tools/Ragdoll/VFX/Preview Death Explosion _F8")]
        public static void PreviewDeathExplosion()
        {
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (!Application.isPlaying || ragdoll == null) return;
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            if (damage == null) return;
            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = ragdoll.Parts[i];
                if (part?.Health == null || !part.Health.IsCritical || part.Body == null) continue;
                float ratio = Mathf.Max(.01f, part.Health.DamageRatio);
                Vector2 point = part.Body.worldCenterOfMass;
                bool applied = damage.ApplyDirectDamage(part.Body, part.Health.CurrentHealth / ratio + 1f,
                    25f, new Vector2(2f, 9f), point);
                Transform pool = ragdoll.transform.Find("VFX Death Debris Pool");
                Rigidbody2D[] debris = pool != null ? pool.GetComponentsInChildren<Rigidbody2D>(true) : Array.Empty<Rigidbody2D>();
                int activeDebris = 0;
                for (int debrisIndex = 0; debrisIndex < debris.Length; debrisIndex++)
                    if (debris[debrisIndex] != null && debris[debrisIndex].gameObject.activeSelf && debris[debrisIndex].simulated)
                        activeDebris++;
                if (!applied || activeDebris != 40)
                    Debug.LogError($"Death VFX preview failed: damageApplied={applied}, activeDebris={activeDebris}/40.", ragdoll);
                else
                    Debug.Log("Death VFX preview passed: 24 candies + 16 glass/spring pieces are physically active. Restart the level to reset.", ragdoll);
                return;
            }
            Debug.LogWarning("No active critical ragdoll part was found for the death preview.", ragdoll);
        }

        [MenuItem("Tools/Ragdoll/VFX/Preview Death Explosion _F8", true)]
        private static bool CanPreviewDeathExplosion() => Application.isPlaying;

        public static void ValidateSandboxBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            RagdollVFXController vfx = ragdoll != null ? ragdoll.GetComponent<RagdollVFXController>() : null;
            CoinFlyVFXController coins = UnityEngine.Object.FindObjectOfType<CoinFlyVFXController>(true);
            if (vfx == null || coins == null) throw new InvalidOperationException("Gameplay VFX controllers are missing.");
            SerializedObject data = new SerializedObject(vfx);
            if (data.FindProperty("candyDebrisBodies").arraySize != 24 ||
                data.FindProperty("glassShardBodies").arraySize != 16 ||
                data.FindProperty("candyFills").arraySize != 6 ||
                data.FindProperty("maximumActiveCandyDebris").intValue != 24)
                throw new InvalidOperationException("Death VFX pools are not correctly authored.");
            RagdollVFXProfile configuredProfile = data.FindProperty("profile").objectReferenceValue as RagdollVFXProfile;
            if (configuredProfile == null || configuredProfile.CollisionFumePrefab == null)
                throw new InvalidOperationException("The shared collision fume prefab is not assigned.");
            ParticleSystem.VelocityOverLifetimeModule velocity = configuredProfile.CollisionFumePrefab.velocityOverLifetime;
            if (velocity.x.mode != velocity.y.mode || velocity.x.mode != velocity.z.mode)
                throw new InvalidOperationException("Collision fume velocity curves do not share the same mode.");
            if (AssetDatabase.LoadAssetAtPath<SpriteAtlas>(BrokenPiecesAtlasPath) == null)
                throw new InvalidOperationException("The broken-piece sprite atlas is missing.");
            CameraShake2D shake = UnityEngine.Object.FindObjectOfType<CameraShake2D>();
            if (shake == null) throw new InvalidOperationException("CameraShake2D is missing from the main camera.");
            GameplayManager gameplay = UnityEngine.Object.FindObjectOfType<GameplayManager>();
            if (gameplay == null || new SerializedObject(gameplay).FindProperty("deathCompletionDelay").floatValue < 1.5f)
                throw new InvalidOperationException("Death presentation delay is too short to show the floor burst.");
            SerializedObject coinData = new SerializedObject(coins);
            if (coinData.FindProperty("coinPool").arraySize != 12)
                throw new InvalidOperationException("Coin UI pool is not correctly authored.");
            Debug.Log("Complete gameplay VFX validation passed: shared hit/fume emitters, damage/death shake, 24 candy debris, 16 authored glass/spring pieces, 12 UI coins.");
        }

        private static void SetupScene(Scene scene)
        {
            EnsureFolder("Assets/GameData/Art", "VFX");
            EnsureFolder("Assets/GameData/Materials", "VFX");
            Sprite coinSprite = CreateSprite(CoinPath, true);
            CreateSprite(ShardPath, false);
            Sprite[] brokenPieceSprites = ImportBrokenPieceSprites();
            Sprite[] candySprites = LoadCandySprites();
            CreateBrokenPiecesAtlas();
            Material particleMaterial = CreateParticleMaterial();
            Material fumeMaterial = CreateFumeMaterial();
            PhysicsMaterial2D debrisMaterial = CreateDebrisMaterial();

            RagdollVFXProfile profile = AssetDatabase.LoadAssetAtPath<RagdollVFXProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RagdollVFXProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            ParticleSystem hit = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_Hit.prefab", particleMaterial, 13, 0);
            ParticleSystem combo = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_Break.prefab", particleMaterial, 16, 16);
            ParticleSystem knockout = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_Damage.prefab", particleMaterial, 14, 14);
            ParticleSystem death = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_DeathExplosion.prefab", particleMaterial, 24, 22);
            ParticleSystem fumes = ConfigureFumeParticle(fumeMaterial);
            SerializedObject profileData = new SerializedObject(profile);
            profileData.FindProperty("hitPrefab").objectReferenceValue = hit;
            profileData.FindProperty("comboPrefab").objectReferenceValue = combo;
            profileData.FindProperty("knockoutPrefab").objectReferenceValue = knockout;
            profileData.FindProperty("deathPrefab").objectReferenceValue = death;
            profileData.FindProperty("collisionFumePrefab").objectReferenceValue = fumes;
            profileData.ApplyModifiedPropertiesWithoutUndo();

            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (ragdoll == null) throw new InvalidOperationException("RagdollController not found.");
            RagdollVFXController vfx = ragdoll.GetComponent<RagdollVFXController>();
            if (vfx == null) vfx = Undo.AddComponent<RagdollVFXController>(ragdoll.gameObject);

            Transform oldPool = ragdoll.transform.Find("VFX Death Debris Pool");
            if (oldPool != null) Undo.DestroyObjectImmediate(oldPool.gameObject);
            GameObject poolRoot = new GameObject("VFX Death Debris Pool");
            Undo.RegisterCreatedObjectUndo(poolRoot, "Create Death Debris Pool");
            poolRoot.transform.SetParent(ragdoll.transform, false);

            Rigidbody2D[] candyBodies = new Rigidbody2D[24];
            SpriteRenderer[] candyRenderers = new SpriteRenderer[24];
            for (int i = 0; i < candyBodies.Length; i++)
                CreateCandyDebris(poolRoot.transform, candySprites[i % candySprites.Length], i,
                    out candyBodies[i], out candyRenderers[i]);

            Rigidbody2D[] shardBodies = new Rigidbody2D[16];
            SpriteRenderer[] shardRenderers = new SpriteRenderer[16];
            for (int i = 0; i < shardBodies.Length; i++)
            {
                // Every supplied piece is used once; the spring is repeated to visibly eject the suspension hardware.
                Sprite sprite = brokenPieceSprites[Mathf.Min(i, brokenPieceSprites.Length - 1)];
                CreateShardDebris(poolRoot.transform, sprite, debrisMaterial, i, out shardBodies[i], out shardRenderers[i]);
            }

            RagdollCandyFill2D[] fills = ragdoll.GetComponentsInChildren<RagdollCandyFill2D>(true);
            SpriteRenderer[] allRenderers = ragdoll.GetComponentsInChildren<SpriteRenderer>(true);
            var sourceRenderers = new List<SpriteRenderer>(allRenderers.Length);
            for (int i = 0; i < allRenderers.Length; i++)
                if (!allRenderers[i].transform.IsChildOf(poolRoot.transform))
                    sourceRenderers.Add(allRenderers[i]);

            SerializedObject vfxData = new SerializedObject(vfx);
            vfxData.FindProperty("controller").objectReferenceValue = ragdoll;
            vfxData.FindProperty("profile").objectReferenceValue = profile;
            AssignArray(vfxData.FindProperty("candyFills"), fills);
            AssignArray(vfxData.FindProperty("characterRenderers"), sourceRenderers.ToArray());
            AssignArray(vfxData.FindProperty("candyDebrisBodies"), candyBodies);
            AssignArray(vfxData.FindProperty("candyDebrisRenderers"), candyRenderers);
            AssignArray(vfxData.FindProperty("glassShardBodies"), shardBodies);
            AssignArray(vfxData.FindProperty("glassShardRenderers"), shardRenderers);
            vfxData.FindProperty("maximumActiveCandyDebris").intValue = 24;
            vfxData.FindProperty("maximumActiveGlassShards").intValue = 16;
            vfxData.FindProperty("debrisLifetime").floatValue = 10f;
            vfxData.ApplyModifiedPropertiesWithoutUndo();

            SetupCameraShake(ragdoll);
            SetupDeathPresentationDelay();

            SetupCoinUI(coinSprite);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Complete gameplay VFX installed: pooled impacts/fumes, camera shake, coins, combo/KO, candy blast, authored glass and spring debris.");
        }

        private static void SetupCoinUI(Sprite coinSprite)
        {
            GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>();
            if (hud == null) throw new InvalidOperationException("GameplayHUD not found.");
            Canvas canvas = hud.GetComponent<Canvas>() ?? hud.GetComponentInParent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("Gameplay Canvas not found.");

            Transform previous = canvas.transform.Find("UI VFX Layer");
            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
            GameObject layerObject = new GameObject("UI VFX Layer", typeof(RectTransform), typeof(Canvas), typeof(CoinFlyVFXController));
            Undo.RegisterCreatedObjectUndo(layerObject, "Create UI VFX Layer");
            layerObject.transform.SetParent(canvas.transform, false);
            layerObject.transform.SetAsLastSibling();
            RectTransform layer = layerObject.GetComponent<RectTransform>();
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            Canvas isolatedCanvas = layerObject.GetComponent<Canvas>();
            isolatedCanvas.overrideSorting = true;
            isolatedCanvas.sortingOrder = 50;
            isolatedCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;

            Image[] images = new Image[12];
            for (int i = 0; i < images.Length; i++)
            {
                GameObject coin = new GameObject("Coin " + (i + 1).ToString("00"), typeof(RectTransform), typeof(Image));
                coin.transform.SetParent(layer, false);
                RectTransform rect = coin.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(52f, 52f);
                images[i] = coin.GetComponent<Image>();
                images[i].sprite = coinSprite;
                images[i].raycastTarget = false;
                coin.SetActive(false);
            }

            SerializedObject hudData = new SerializedObject(hud);
            Text score = hudData.FindProperty("scoreText").objectReferenceValue as Text;
            CoinFlyVFXController controller = layerObject.GetComponent<CoinFlyVFXController>();
            SerializedObject coinData = new SerializedObject(controller);
            coinData.FindProperty("canvas").objectReferenceValue = canvas;
            coinData.FindProperty("vfxLayer").objectReferenceValue = layer;
            coinData.FindProperty("scoreTarget").objectReferenceValue = score != null ? score.rectTransform : null;
            coinData.FindProperty("worldCamera").objectReferenceValue = Camera.main;
            AssignArray(coinData.FindProperty("coinPool"), images);
            coinData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateCandyDebris(Transform parent, Sprite sprite, int index,
            out Rigidbody2D body, out SpriteRenderer renderer)
        {
            GameObject go = new GameObject("Candy Debris " + (index + 1).ToString("00"),
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * .42f;
            renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 105;
            body = go.GetComponent<Rigidbody2D>();
            body.mass = .08f;
            body.gravityScale = 1.25f;
            body.drag = .2f;
            body.angularDrag = .35f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            CircleCollider2D collider = go.GetComponent<CircleCollider2D>();
            collider.radius = .22f;
            body.simulated = false;
            go.SetActive(false);
        }

        private static void CreateShardDebris(Transform parent, Sprite sprite, PhysicsMaterial2D material, int index,
            out Rigidbody2D body, out SpriteRenderer renderer)
        {
            bool spring = sprite != null && sprite.name.Contains("21");
            GameObject go = new GameObject((spring ? "Spring Debris " : "Glass Debris ") + (index + 1).ToString("00"),
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            go.transform.SetParent(parent, false);
            int debrisLayer = LayerMask.NameToLayer("DeathDebris");
            if (debrisLayer >= 0) go.layer = debrisLayer;
            renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = 106;
            float longestSide = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
            float desiredSize = spring ? .62f : sprite != null && sprite.name.Contains("19")
                ? 1.1f
                : Mathf.Lerp(.44f, .70f, (index % 5) / 4f);
            go.transform.localScale = Vector3.one * (desiredSize / Mathf.Max(.01f, longestSide));
            body = go.GetComponent<Rigidbody2D>();
            body.mass = spring ? .13f : Mathf.Lerp(.055f, .09f, (index % 4) / 3f);
            body.gravityScale = spring ? 1.7f : 1.5f;
            body.drag = .14f;
            body.angularDrag = spring ? .32f : .16f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            BoxCollider2D collider = go.GetComponent<BoxCollider2D>();
            collider.size = sprite != null ? sprite.bounds.size * .72f : new Vector2(.6f, .3f);
            collider.sharedMaterial = material;
            body.simulated = false;
            go.SetActive(false);
        }

        private static ParticleSystem ConfigureParticle(string path, Material material, int maxParticles, int burstCount)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) throw new InvalidOperationException("Missing VFX prefab: " + path);
            ParticleSystem system = root.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = system.emission;
            if (burstCount > 0)
            {
                emission.enabled = true;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });
            }
            else emission.enabled = false;
            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 110;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
        }

        private static Material CreateParticleMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "VFX Particle Additive" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static Material CreateFumeMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FumeMaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader is unavailable.");
            material = new Material(shader) { name = "MAT_CollisionFume" };
            AssetDatabase.CreateAsset(material, FumeMaterialPath);
            return material;
        }

        private static PhysicsMaterial2D CreateDebrisMaterial()
        {
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(DebrisMaterialPath);
            if (material != null) return material;
            material = new PhysicsMaterial2D("PMAT_GlassDebris")
            {
                friction = .42f,
                bounciness = .22f
            };
            AssetDatabase.CreateAsset(material, DebrisMaterialPath);
            return material;
        }

        private static ParticleSystem ConfigureFumeParticle(Material material)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FumePrefabPath) == null)
            {
                GameObject source = new GameObject("VFX_Ragdoll_CollisionFume", typeof(ParticleSystem));
                PrefabUtility.SaveAsPrefabAsset(source, FumePrefabPath);
                UnityEngine.Object.DestroyImmediate(source);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(FumePrefabPath);
            ParticleSystem system = root.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.duration = .8f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 18;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.38f, .62f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.18f, .48f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .24f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, .42f), new Color(1f, 1f, 1f, .68f));

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .035f;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-.14f, .14f);
            velocity.y = new ParticleSystem.MinMaxCurve(.35f, .72f);
            // Unity requires X/Y/Z velocity curves to share the same MinMaxCurve mode.
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(.86f, .92f, 1f), 1f) },
                new[] { new GradientAlphaKey(.62f, 0f), new GradientAlphaKey(.34f, .45f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, .62f), new Keyframe(1f, 1.45f)));

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = .08f;
            noise.frequency = 1.2f;
            noise.scrollSpeed = .22f;
            noise.octaveCount = 1;
            noise.damping = true;

            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 109;
            PrefabUtility.SaveAsPrefabAsset(root, FumePrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(FumePrefabPath);
        }

        private static Sprite[] ImportBrokenPieceSprites()
        {
            if (!AssetDatabase.IsValidFolder(BrokenPiecesRoot))
                throw new DirectoryNotFoundException("Missing supplied broken-piece art folder: " + BrokenPiecesRoot);
            Sprite[] sprites = new Sprite[BrokenPieceNames.Length];
            for (int i = 0; i < BrokenPieceNames.Length; i++)
            {
                string path = BrokenPiecesRoot + "/" + BrokenPieceNames[i];
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Unable to import broken-piece sprite: " + path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = 256;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = 256;
                android.format = TextureImporterFormat.ASTC_4x4;
                android.compressionQuality = 50;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprites[i] == null) throw new InvalidOperationException("Broken-piece sprite failed to load: " + path);
            }
            return sprites;
        }

        private static Sprite[] LoadCandySprites()
        {
            Sprite[] sprites = new Sprite[24];
            for (int i = 0; i < sprites.Length; i++)
            {
                string path = CandyArtRoot + "/Candy " + (i + 1) + ".png";
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprites[i] == null)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
                if (sprites[i] == null) throw new InvalidOperationException("Missing candy debris sprite: " + path);
            }
            return sprites;
        }

        private static void CreateBrokenPiecesAtlas()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(BrokenPiecesAtlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas { name = "Broken Pieces" };
                AssetDatabase.CreateAsset(atlas, BrokenPiecesAtlasPath);
            }
            UnityEngine.Object[] oldPackables = atlas.GetPackables();
            if (oldPackables.Length > 0) atlas.Remove(oldPackables);
            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BrokenPiecesRoot);
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
                maxTextureSize = 512,
                format = TextureImporterFormat.ETC2_RGBA8,
                compressionQuality = 50
            });
            SpriteAtlasUtility.PackAtlases(new[] { atlas }, BuildTarget.Android);
            EditorUtility.SetDirty(atlas);
        }

        private static void SetupCameraShake(RagdollController ragdoll)
        {
            Camera camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (camera == null) throw new InvalidOperationException("Main Camera was not found.");
            CameraShake2D shake = camera.GetComponent<CameraShake2D>();
            if (shake == null) shake = Undo.AddComponent<CameraShake2D>(camera.gameObject);
            SerializedObject data = new SerializedObject(shake);
            data.FindProperty("controller").objectReferenceValue = ragdoll;
            data.FindProperty("minimumImpactSpeed").floatValue = 4f;
            data.FindProperty("speedForMaximumShake").floatValue = 18f;
            data.FindProperty("damageAmplitude").floatValue = .08f;
            data.FindProperty("damageDuration").floatValue = .14f;
            data.FindProperty("deathAmplitude").floatValue = .24f;
            data.FindProperty("deathDuration").floatValue = .55f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shake);
        }

        private static void SetupDeathPresentationDelay()
        {
            GameplayManager gameplay = UnityEngine.Object.FindObjectOfType<GameplayManager>();
            if (gameplay == null) return;
            SerializedObject data = new SerializedObject(gameplay);
            data.FindProperty("deathCompletionDelay").floatValue = 1.6f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gameplay);
        }

        private static Sprite CreateSprite(string path, bool coin)
        {
            if (!File.Exists(path))
            {
                const int size = 64;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color32 clear = new Color32(0, 0, 0, 0);
                Color32[] pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
                if (coin)
                {
                    Vector2 center = new Vector2(31.5f, 31.5f);
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float distance = Vector2.Distance(new Vector2(x, y), center);
                            if (distance <= 27f)
                                pixels[y * size + x] = distance > 22f
                                    ? new Color32(255, 183, 20, 255)
                                    : new Color32(255, 226, 66, 255);
                        }
                }
                else
                {
                    for (int y = 5; y < 60; y++)
                    {
                        int left = 31 - y / 3;
                        int right = 33 + y / 4;
                        for (int x = Mathf.Max(2, left); x <= Mathf.Min(61, right); x++)
                            pixels[y * size + x] = new Color32(210, 244, 255, 210);
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void AssignArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
