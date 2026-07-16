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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private const string MaterialPath = "Assets/GameData/Materials/VFX/VFX_Particle_Additive.mat";

        [MenuItem("Tools/Ragdoll/VFX/Setup Complete Gameplay VFX")]
        public static void SetupActiveScene()
        {
            SetupScene(EditorSceneManager.GetActiveScene());
        }

        public static void SetupSandboxBatch()
        {
            SetupScene(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));
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
                data.FindProperty("glassShardBodies").arraySize != 12 ||
                data.FindProperty("candyFills").arraySize != 6)
                throw new InvalidOperationException("Death VFX pools are not correctly authored.");
            SerializedObject coinData = new SerializedObject(coins);
            if (coinData.FindProperty("coinPool").arraySize != 12)
                throw new InvalidOperationException("Coin UI pool is not correctly authored.");
            Debug.Log("Complete gameplay VFX validation passed: hit/combo/KO/death, 24 candy debris, 12 shards, 12 UI coins.");
        }

        private static void SetupScene(Scene scene)
        {
            EnsureFolder("Assets/GameData/Art", "VFX");
            EnsureFolder("Assets/GameData/Materials", "VFX");
            Sprite coinSprite = CreateSprite(CoinPath, true);
            Sprite shardSprite = CreateSprite(ShardPath, false);
            Material particleMaterial = CreateParticleMaterial();

            RagdollVFXProfile profile = AssetDatabase.LoadAssetAtPath<RagdollVFXProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RagdollVFXProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            ParticleSystem hit = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_Hit.prefab", particleMaterial, 13, 0);
            ParticleSystem combo = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_Break.prefab", particleMaterial, 16, 16);
            ParticleSystem knockout = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_Damage.prefab", particleMaterial, 14, 14);
            ParticleSystem death = ConfigureParticle(PrefabRoot + "/VFX_Ragdoll_DeathExplosion.prefab", particleMaterial, 20, 18);
            SerializedObject profileData = new SerializedObject(profile);
            profileData.FindProperty("hitPrefab").objectReferenceValue = hit;
            profileData.FindProperty("comboPrefab").objectReferenceValue = combo;
            profileData.FindProperty("knockoutPrefab").objectReferenceValue = knockout;
            profileData.FindProperty("deathPrefab").objectReferenceValue = death;
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
                CreateCandyDebris(poolRoot.transform, i, out candyBodies[i], out candyRenderers[i]);

            Rigidbody2D[] shardBodies = new Rigidbody2D[12];
            SpriteRenderer[] shardRenderers = new SpriteRenderer[12];
            for (int i = 0; i < shardBodies.Length; i++)
                CreateShardDebris(poolRoot.transform, shardSprite, i, out shardBodies[i], out shardRenderers[i]);

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
            vfxData.ApplyModifiedPropertiesWithoutUndo();

            SetupCoinUI(coinSprite);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Complete gameplay VFX installed: pooled impacts, coins, combo/KO, death candy blast and glass shards.");
        }

        private static void SetupCoinUI(Sprite coinSprite)
        {
            GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>();
            if (hud == null) throw new InvalidOperationException("GameplayHUD not found.");
            Canvas canvas = hud.GetComponent<Canvas>() ?? hud.GetComponentInParent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("Gameplay Canvas not found.");

            Transform previous = canvas.transform.Find("UI VFX Layer");
            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
            GameObject layerObject = new GameObject("UI VFX Layer", typeof(RectTransform), typeof(CoinFlyVFXController));
            Undo.RegisterCreatedObjectUndo(layerObject, "Create UI VFX Layer");
            layerObject.transform.SetParent(canvas.transform, false);
            layerObject.transform.SetAsLastSibling();
            RectTransform layer = layerObject.GetComponent<RectTransform>();
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;

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

        private static void CreateCandyDebris(Transform parent, int index,
            out Rigidbody2D body, out SpriteRenderer renderer)
        {
            GameObject go = new GameObject("Candy Debris " + (index + 1).ToString("00"),
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * .42f;
            renderer = go.GetComponent<SpriteRenderer>();
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

        private static void CreateShardDebris(Transform parent, Sprite sprite, int index,
            out Rigidbody2D body, out SpriteRenderer renderer)
        {
            GameObject go = new GameObject("Glass Shard " + (index + 1).ToString("00"),
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(.22f, .38f, 1f);
            renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(.72f, .94f, 1f, .82f);
            renderer.sortingOrder = 106;
            body = go.GetComponent<Rigidbody2D>();
            body.mass = .05f;
            body.gravityScale = 1.35f;
            body.drag = .1f;
            body.angularDrag = .15f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            BoxCollider2D collider = go.GetComponent<BoxCollider2D>();
            collider.size = new Vector2(.8f, .35f);
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
