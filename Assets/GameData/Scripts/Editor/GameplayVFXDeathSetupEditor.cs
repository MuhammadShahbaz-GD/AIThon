#if UNITY_EDITOR
using System;
using System.Collections;
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

        [MenuItem("Tools/Ragdoll/VFX/Apply Torso Floor Death Burst")]
        public static void ApplyTorsoFloorBurstActiveScene() => ApplyTorsoFloorBurst(EditorSceneManager.GetActiveScene());

        public static void ApplyTorsoFloorBurstBatch() =>
            ApplyTorsoFloorBurst(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));

        private static void ApplyTorsoFloorBurst(Scene scene)
        {
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            RagdollVFXController vfx = ragdoll != null ? ragdoll.GetComponent<RagdollVFXController>() : null;
            if (ragdoll == null || vfx == null) throw new InvalidOperationException("Active ragdoll VFX controller was not found.");

            Rigidbody2D torsoBody = ResolveTorsoBody(ragdoll);
            Camera gameplayCamera = Camera.main;
            Collider2D floorCollider = ResolveSceneCollider("Floor");
            if (torsoBody == null || gameplayCamera == null || floorCollider == null)
                throw new InvalidOperationException($"Death debris references missing: torso={torsoBody != null}, camera={gameplayCamera != null}, floor={floorCollider != null}.");

            SerializedObject data = new SerializedObject(vfx);
            Rigidbody2D[] candyBodies = ReadBodyArray(data.FindProperty("candyDebrisBodies"));
            Rigidbody2D[] glassBodies = ReadBodyArray(data.FindProperty("glassShardBodies"));
            if (candyBodies.Length != 24 || glassBodies.Length != 16)
                throw new InvalidOperationException("Expected the existing 24-candy and 16-glass pooled bodies.");

            Vector2 packingSize = ResolveTorsoPackingSize(torsoBody);
            ConfigureExistingPool(candyBodies, true);
            ConfigureExistingPool(glassBodies, false);
            MapPoolToTorso(candyBodies, torsoBody, packingSize, 0f);
            MapPoolToTorso(glassBodies, torsoBody, packingSize, 74f);

            data.FindProperty("deathDebrisOriginBody").objectReferenceValue = torsoBody;
            data.FindProperty("gameplayCamera").objectReferenceValue = gameplayCamera;
            data.FindProperty("torsoPackingSize").vector2Value = packingSize;
            data.FindProperty("floorWorldY").floatValue = floorCollider.bounds.max.y;
            data.FindProperty("screenEdgePadding").floatValue = .55f;
            data.FindProperty("candyFlightTimeRange").vector2Value = new Vector2(1.08f, 1.36f);
            data.FindProperty("glassFlightTimeRange").vector2Value = new Vector2(.92f, 1.22f);
            data.FindProperty("maximumDebrisSpeed").floatValue = 12f;
            data.FindProperty("angularVelocityRange").vector2Value = new Vector2(220f, 520f);
            data.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(vfx);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Torso floor death burst applied in place: existing 24 candy and 16 glass/spring bodies preserved and remapped.");
        }

        [MenuItem("Tools/Ragdoll/VFX/Preview Death Explosion _F8")]
        public static void PreviewDeathExplosion()
        {
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>();
            if (!Application.isPlaying || ragdoll == null) return;
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            RagdollVFXController vfx = ragdoll.GetComponent<RagdollVFXController>();
            if (damage == null || vfx == null) return;
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
                {
                    Debug.Log("Death VFX preview passed: 24 candies + 16 glass/spring pieces are physically active. Restart the level to reset.", ragdoll);
                    vfx.StartCoroutine(ValidateFloorBurstAfterFlight(pool, ragdoll));
                }
                return;
            }
            Debug.LogWarning("No active critical ragdoll part was found for the death preview.", ragdoll);
        }

        [MenuItem("Tools/Ragdoll/VFX/Preview Death Explosion _F8", true)]
        private static bool CanPreviewDeathExplosion() => Application.isPlaying;

        private static IEnumerator ValidateFloorBurstAfterFlight(Transform pool, RagdollController context)
        {
            yield return new WaitForSecondsRealtime(1.65f);
            Camera camera = Camera.main;
            Collider2D floor = ResolveSceneCollider("Floor");
            if (pool == null || camera == null || floor == null)
            {
                Debug.LogError("Death floor-burst validation is missing its pool, camera, or floor.", context);
                yield break;
            }

            Rigidbody2D[] bodies = pool.GetComponentsInChildren<Rigidbody2D>(true);
            float halfWidth = camera.orthographicSize * camera.aspect;
            float left = camera.transform.position.x - halfWidth;
            float right = camera.transform.position.x + halfWidth;
            float minimumCandyX = float.PositiveInfinity;
            float maximumCandyX = float.NegativeInfinity;
            int visibleCandy = 0;
            int candyInsideView = 0;
            int candyOnFloor = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                if (body == null || !body.name.StartsWith("Candy Debris", StringComparison.Ordinal)) continue;
                SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
                if (body.gameObject.activeSelf && body.simulated && renderer != null && renderer.enabled) visibleCandy++;
                float x = body.position.x;
                minimumCandyX = Mathf.Min(minimumCandyX, x);
                maximumCandyX = Mathf.Max(maximumCandyX, x);
                if (x >= left && x <= right) candyInsideView++;
                if (body.position.y <= floor.bounds.max.y + .85f) candyOnFloor++;
            }

            float spread = maximumCandyX - minimumCandyX;
            if (visibleCandy != 24 || candyInsideView < 22 || candyOnFloor < 18 || spread < 3.5f)
                Debug.LogError($"Death floor burst failed: visibleCandy={visibleCandy}/24, insideView={candyInsideView}/24, onFloor={candyOnFloor}/24, spread={spread:F2}.", context);
            else
                Debug.Log($"DEATH_FLOOR_BURST_PLAYMODE_OK: 24 candy bodies visible, {candyInsideView} inside camera, {candyOnFloor} on floor, spread={spread:F2}.", context);
        }

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
            Rigidbody2D torsoBody = data.FindProperty("deathDebrisOriginBody").objectReferenceValue as Rigidbody2D;
            Camera gameplayCamera = data.FindProperty("gameplayCamera").objectReferenceValue as Camera;
            Vector2 packingSize = data.FindProperty("torsoPackingSize").vector2Value;
            if (torsoBody == null || gameplayCamera == null)
                throw new InvalidOperationException("Torso-mapped death burst references are missing.");
            ValidatePoolMappedToTorso(data.FindProperty("candyDebrisBodies"), torsoBody, packingSize);
            ValidatePoolMappedToTorso(data.FindProperty("glassShardBodies"), torsoBody, packingSize);
            Collider2D floor = ResolveSceneCollider("Floor");
            if (floor == null || Mathf.Abs(data.FindProperty("floorWorldY").floatValue - floor.bounds.max.y) > .05f)
                throw new InvalidOperationException("Death debris floor target is not mapped to the authored Floor collider.");
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
            Debug.Log("Complete gameplay VFX validation passed: torso-mapped pools, camera-contained ballistic floor burst, 24 visible candies, 16 glass/spring pieces, shared impacts/fumes, and 12 UI coins.");
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
            Rigidbody2D torsoBody = ResolveTorsoBody(ragdoll);
            Camera gameplayCamera = Camera.main;
            Collider2D floorCollider = ResolveSceneCollider("Floor");
            if (torsoBody == null || gameplayCamera == null || floorCollider == null)
                throw new InvalidOperationException($"Death debris references missing: torso={torsoBody != null}, camera={gameplayCamera != null}, floor={floorCollider != null}.");
            Vector2 torsoPackingSize = ResolveTorsoPackingSize(torsoBody);

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
            MapPoolToTorso(candyBodies, torsoBody, torsoPackingSize, 0f);
            MapPoolToTorso(shardBodies, torsoBody, torsoPackingSize, 74f);

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
            vfxData.FindProperty("deathDebrisOriginBody").objectReferenceValue = torsoBody;
            vfxData.FindProperty("gameplayCamera").objectReferenceValue = gameplayCamera;
            vfxData.FindProperty("torsoPackingSize").vector2Value = torsoPackingSize;
            vfxData.FindProperty("floorWorldY").floatValue = floorCollider.bounds.max.y;
            vfxData.FindProperty("screenEdgePadding").floatValue = .55f;
            vfxData.FindProperty("candyFlightTimeRange").vector2Value = new Vector2(1.08f, 1.36f);
            vfxData.FindProperty("glassFlightTimeRange").vector2Value = new Vector2(.92f, 1.22f);
            vfxData.FindProperty("maximumDebrisSpeed").floatValue = 12f;
            vfxData.FindProperty("angularVelocityRange").vector2Value = new Vector2(220f, 520f);
            vfxData.ApplyModifiedPropertiesWithoutUndo();

            SetupCameraShake(ragdoll);
            SetupDeathPresentationDelay();

            SetupCoinUI(coinSprite);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ImpactFeedbackSetupEditor.SetupBatch();
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
            go.transform.localScale = Vector3.one * .55f;
            renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 112;
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
            renderer.sortingOrder = 113;
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
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
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

        private static Rigidbody2D ResolveTorsoBody(RagdollController ragdoll)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = ragdoll.Parts[i];
                if (part != null && part.PartType == RagdollPartType.Torso && part.Body != null)
                    return part.Body;
            }

            RagdollRigController2D rig = ragdoll.GetComponent<RagdollRigController2D>();
            if (rig == null) return null;
            SerializedProperty authoredParts = new SerializedObject(rig).FindProperty("authoredParts");
            for (int i = 0; i < authoredParts.arraySize; i++)
            {
                SerializedProperty part = authoredParts.GetArrayElementAtIndex(i);
                if (part.FindPropertyRelative("partType").enumValueIndex != (int)RagdollPartType.Torso) continue;
                return part.FindPropertyRelative("body").objectReferenceValue as Rigidbody2D;
            }
            return null;
        }

        private static void ValidatePoolMappedToTorso(SerializedProperty pool, Rigidbody2D torsoBody, Vector2 packingSize)
        {
            Quaternion inverseRotation = Quaternion.Euler(0f, 0f, -torsoBody.rotation);
            Vector2 origin = torsoBody.worldCenterOfMass;
            for (int i = 0; i < pool.arraySize; i++)
            {
                Rigidbody2D body = pool.GetArrayElementAtIndex(i).objectReferenceValue as Rigidbody2D;
                if (body == null) throw new InvalidOperationException("Death debris pool contains a missing body.");
                Vector2 local = inverseRotation * ((Vector2)body.transform.position - origin);
                if (Mathf.Abs(local.x) > packingSize.x * .55f || Mathf.Abs(local.y) > packingSize.y * .55f)
                    throw new InvalidOperationException(body.name + " is not authored inside the torso packing area.");
                if (body.gameObject.activeSelf || body.simulated)
                    throw new InvalidOperationException(body.name + " must begin inactive and unsimulated in the pool.");
            }
        }

        private static Collider2D ResolveSceneCollider(string objectName)
        {
            Collider2D[] colliders = UnityEngine.Object.FindObjectsOfType<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].name == objectName)
                    return colliders[i];
            return null;
        }

        private static Vector2 ResolveTorsoPackingSize(Rigidbody2D torsoBody)
        {
            Collider2D torsoCollider = torsoBody.GetComponent<Collider2D>();
            Vector2 size = torsoCollider != null ? torsoCollider.bounds.size : new Vector2(2f, 2f);
            return new Vector2(Mathf.Max(.5f, size.x * .68f), Mathf.Max(.5f, size.y * .68f));
        }

        private static Rigidbody2D[] ReadBodyArray(SerializedProperty property)
        {
            var bodies = new Rigidbody2D[property.arraySize];
            for (int i = 0; i < bodies.Length; i++)
                bodies[i] = property.GetArrayElementAtIndex(i).objectReferenceValue as Rigidbody2D;
            return bodies;
        }

        private static void ConfigureExistingPool(Rigidbody2D[] pool, bool candy)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                Rigidbody2D body = pool[i];
                if (body == null) throw new InvalidOperationException("Death debris pool contains a missing Rigidbody2D.");
                Undo.RecordObject(body, "Configure Death Debris Physics");
                Undo.RecordObject(body.transform, "Map Death Debris To Torso");
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.simulated = false;
                if (candy) body.transform.localScale = Vector3.one * .55f;
                SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, "Configure Death Debris Visibility");
                    renderer.sortingOrder = candy ? 112 : 113;
                    renderer.enabled = true;
                    EditorUtility.SetDirty(renderer);
                }
                body.gameObject.SetActive(false);
                EditorUtility.SetDirty(body);
                EditorUtility.SetDirty(body.transform);
            }
        }

        private static void MapPoolToTorso(Rigidbody2D[] pool, Rigidbody2D torsoBody, Vector2 packingSize, float phaseDegrees)
        {
            Vector2 origin = torsoBody.worldCenterOfMass;
            Quaternion bodyRotation = Quaternion.Euler(0f, 0f, torsoBody.rotation);
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null) continue;
                float radius = Mathf.Sqrt((i + .5f) / pool.Length);
                float angle = (i * 137.50777f + phaseDegrees) * Mathf.Deg2Rad;
                Vector2 localOffset = new Vector2(
                    Mathf.Cos(angle) * packingSize.x * .5f * radius,
                    Mathf.Sin(angle) * packingSize.y * .5f * radius);
                pool[i].transform.position = origin + (Vector2)(bodyRotation * localOffset);
                pool[i].transform.rotation = Quaternion.Euler(0f, 0f, i * 29f + phaseDegrees);
                pool[i].simulated = false;
                pool[i].gameObject.SetActive(false);
            }
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
