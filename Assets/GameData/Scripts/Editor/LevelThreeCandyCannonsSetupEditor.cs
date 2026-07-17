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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Idempotent Level 3 authoring and validation for the single gameplay scene.</summary>
    public static class LevelThreeCandyCannonsSetupEditor
    {
        public const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        public const string LevelRootName = "Level 03 - Candy Cannons";
        public const string LevelId = "level_03";

        private const string ArtRoot = "Assets/GameData/Art/Level 03";
        private const string BackgroundPath = ArtRoot + "/BG.png";
        private const string CannonPath = ArtRoot + "/Canon.png";
        private const string BagPath = ArtRoot + "/Bag mask.png";
        private const string LevelAssetPath = "Assets/GameData/Materials/Gameplay/Level_03.asset";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";
        private const string TrailMaterialPath = "Assets/GameData/Materials/VFX/MAT_Level03_CannonTrail.mat";
        private const string GlowMaterialPath = "Assets/GameData/Materials/VFX/MAT_Level03_CannonGlow.mat";
        private const string CandyParticleMaterialPath =
            "Assets/GameData/Materials/VFX/MAT_Level03_CannonCandy.mat";
        private const string ProjectilePhysicsMaterialPath =
            "Assets/GameData/Materials/VFX/PMAT_Level03_CandyProjectile.physicsMaterial2D";
        private const string CandyArtRoot = "Assets/GameData/Art/Candies";
        private const int ProjectilePoolSize = 12;
        private const int ImpactVFXPoolSize = 4;

        private static readonly string[] LollipopPaths =
        {
            ArtRoot + "/lillipop 1.png", ArtRoot + "/lillipop 2.png",
            ArtRoot + "/lillipop 3.png", ArtRoot + "/lillipop 4.png"
        };

        private static readonly string[] PropPaths =
        {
            ArtRoot + "/Prop 6.png", ArtRoot + "/Prop 7.png",
            ArtRoot + "/Prop 9.png", ArtRoot + "/Prop-8.png"
        };

        private sealed class CannonAuthoring
        {
            public CandyCannonSide Side;
            public GameObject Root;
            public Collider2D PressCollider;
            public Transform Muzzle;
            public Transform RecoilVisual;
            public ParticleSystem MuzzleFlash;
            public GameObject TutorialIndicator;
        }

        [MenuItem("Tools/Game/Level 03/Build Candy Cannons")]
        public static void BuildFromMenu() => Build(false);
        public static void BuildBatch() => Build(true);

        [MenuItem("Tools/Game/Level 03/Validate Candy Cannons")]
        public static void ValidateFromMenu() => Validate(false);
        public static void ValidateBatch() => Validate(true);

        private static void Build(bool exitWhenDone)
        {
            try
            {
                ConfigureArtImporters();
                Material trailMaterial = GetOrCreateTrailMaterial();
                Material glowMaterial = GetOrCreateGlowMaterial();
                Material candyParticleMaterial = GetOrCreateCandyParticleMaterial();
                PhysicsMaterial2D projectileMaterial = GetOrCreateProjectileMaterial();
                Sprite[] candySprites = LoadCandySprites();
                LevelDefinition definition = ConfigureLevelDefinition();
                ConfigureCatalog(definition);

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject levels = FindRoot(scene, SingleSceneLevelsSetupEditor.LevelsRootName);
                GameplayLevelSceneController sceneController =
                    levels != null ? levels.GetComponent<GameplayLevelSceneController>() : null;
                RagdollController ragdoll = FindConfiguredRagdoll(scene);
                RagdollInputManager ragdollInput =
                    ragdoll != null ? ragdoll.GetComponent<RagdollInputManager>() : null;
                Camera camera = FindSceneComponent<Camera>(scene);
                SoundManager sounds = FindSceneComponent<SoundManager>(scene);
                if (levels == null || sceneController == null || ragdoll == null ||
                    ragdollInput == null || camera == null)
                    throw new InvalidOperationException("Level 3 requires the existing Levels controller, ragdoll and camera.");

                Transform previous = FindDirectChild(levels.transform, LevelRootName);
                if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
                GameObject levelRoot = new GameObject(LevelRootName);
                Undo.RegisterCreatedObjectUndo(levelRoot, "Build Level 03 Candy Cannons");
                levelRoot.transform.SetParent(levels.transform, false);

                CreateBackground(levelRoot.transform, camera);
                CreateCandyDecor(levelRoot.transform);
                CannonAuthoring left = CreateCannon(levelRoot.transform, CandyCannonSide.Left,
                    new Vector2(-5.45f, -2.72f), glowMaterial, candyParticleMaterial, candySprites);
                CannonAuthoring right = CreateCannon(levelRoot.transform, CandyCannonSide.Right,
                    new Vector2(5.45f, -2.72f), glowMaterial, candyParticleMaterial, candySprites);

                CandyCannonController2D cannons = levelRoot.AddComponent<CandyCannonController2D>();
                Rigidbody2D[] aimBodies = ResolveAimBodies(ragdoll);
                Collider2D[] partColliders = ResolvePartColliders(ragdoll);
                ConfigureCannonController(cannons, camera, ragdoll, sounds, aimBodies, partColliders,
                    left, right, candySprites, trailMaterial, projectileMaterial, levelRoot.transform);
                CreateImpactVFX(levelRoot.transform, cannons, left, right, glowMaterial,
                    candyParticleMaterial, candySprites);
                ConfigureSceneController(sceneController, levelRoot, ragdoll, ragdollInput, cannons);

                Transform levelOne = FindDirectChild(levels.transform, SingleSceneLevelsSetupEditor.LevelOneRootName);
                Transform levelTwo = FindDirectChild(levels.transform, SingleSceneLevelsSetupEditor.LevelTwoRootName);
                if (levelOne != null) levelOne.gameObject.SetActive(true);
                if (levelTwo != null) levelTwo.gameObject.SetActive(false);
                levelRoot.SetActive(false);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateOrThrow();
                Debug.Log("LEVEL03_CANDY_CANNONS_BUILD_OK: supplied factory art, mirrored dual cannons, " +
                          "12 pooled candy projectiles, layered muzzle flashes, four pooled impact bursts, " +
                          "tutorial gating and level_03 progression are authored.");
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
                ValidateOrThrow();
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        public static void ValidateOrThrow()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (definition == null || definition.LevelId != LevelId ||
                definition.ScenePath != ScenePath ||
                definition.CompletionRule != LevelCompletionRule.CharacterDestroyed ||
                definition.TimeLimit < LevelDefinition.MinimumPlayTimeSeconds)
                throw new InvalidOperationException("Level_03 data must use the single scene and destruction completion.");
            if (catalog == null || catalog.Count != 3 || catalog.Get(2) != definition)
                throw new InvalidOperationException("Level Catalog must contain Level_03 as its third entry.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject levels = FindRoot(scene, SingleSceneLevelsSetupEditor.LevelsRootName);
            Transform levelRoot = levels != null ? FindDirectChild(levels.transform, LevelRootName) : null;
            GameplayLevelSceneController sceneController =
                levels != null ? levels.GetComponent<GameplayLevelSceneController>() : null;
            if (levelRoot == null || sceneController == null || sceneController.LevelCount != 3)
                throw new InvalidOperationException("The shared Levels hierarchy is missing its third authored entry.");
            if (levelRoot.gameObject.activeSelf)
                throw new InvalidOperationException("Level 03 must be inactive in the Level 01 authoring preview.");
            if (FindDirectChild(levelRoot, "Room") != null)
                throw new InvalidOperationException("Room must stay shared under Levels, not inside Level 03.");

            CandyCannonController2D cannons = levelRoot.GetComponent<CandyCannonController2D>();
            if (cannons == null) throw new InvalidOperationException("Level 03 has no cannon controller.");
            SerializedObject data = new SerializedObject(cannons);
            ValidateObjectReference(data, "inputCamera");
            ValidateObjectReference(data, "ragdoll");
            if (data.FindProperty("aimBodies").arraySize != 6 ||
                data.FindProperty("ragdollPartColliders").arraySize < 6 ||
                data.FindProperty("projectilePool").arraySize != ProjectilePoolSize)
                throw new InvalidOperationException(
                    "Level 03 must explicitly reference six aim bodies, their colliders and 12 projectiles.");
            if (data.FindProperty("holdDelay").floatValue <= 0f ||
                data.FindProperty("holdFireInterval").floatValue <= 0f ||
                data.FindProperty("maximumQueuedShots").intValue != ProjectilePoolSize)
                throw new InvalidOperationException(
                    "Level 03 tap buffering and held automatic fire are not configured.");
            ValidateCannonSlot(data.FindProperty("leftCannon"), CandyCannonSide.Left);
            ValidateCannonSlot(data.FindProperty("rightCannon"), CandyCannonSide.Right);
            ValidateProjectilePool(data.FindProperty("projectilePool"));
            CandyCannonVFXController2D cannonVFX =
                FindNamedComponent<CandyCannonVFXController2D>(levelRoot, "Candy Cannon VFX");
            if (cannonVFX == null)
                throw new InvalidOperationException("Level 03 candy cannon impact VFX is missing.");
            SerializedObject vfxData = new SerializedObject(cannonVFX);
            if (vfxData.FindProperty("cannons").objectReferenceValue != cannons ||
                vfxData.FindProperty("impactPool").arraySize != ImpactVFXPoolSize)
                throw new InvalidOperationException(
                    "Level 03 impact VFX must reference the cannon and four pre-authored pooled bursts.");
            ValidateMuzzleLayers(vfxData.FindProperty("leftMuzzle"), "Left");
            ValidateMuzzleLayers(vfxData.FindProperty("rightMuzzle"), "Right");
            ValidateImpactPool(vfxData.FindProperty("impactPool"));

            SpriteRenderer background = FindNamedComponent<SpriteRenderer>(levelRoot, "Level 03 Background");
            if (background == null || AssetDatabase.GetAssetPath(background.sprite) != BackgroundPath)
                throw new InvalidOperationException("The supplied Level 3 background is not authored.");
            if (FindNamedComponent<SpriteRenderer>(levelRoot, "Left Candy Cannon Visual") == null ||
                FindNamedComponent<SpriteRenderer>(levelRoot, "Right Candy Cannon Visual") == null)
                throw new InvalidOperationException("Both supplied-art cannons must be present.");

            SerializedObject controllerData = new SerializedObject(sceneController);
            SerializedProperty entry = controllerData.FindProperty("levels").GetArrayElementAtIndex(2);
            if (entry.FindPropertyRelative("levelId").stringValue != LevelId ||
                entry.FindPropertyRelative("root").objectReferenceValue != levelRoot.gameObject ||
                entry.FindPropertyRelative("ragdoll").objectReferenceValue == null ||
                entry.FindPropertyRelative("ragdollInput").objectReferenceValue == null ||
                entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue != null ||
                entry.FindPropertyRelative("candyCannons").objectReferenceValue != cannons)
                throw new InvalidOperationException("The Level 03 scene-controller entry is incomplete.");
            if (CountMissingScripts(scene) != 0)
                throw new InvalidOperationException("Level 03 authoring introduced a missing script.");

            Debug.Log("LEVEL03_CANDY_CANNONS_VALIDATION_OK: 3-level catalog, shared Room, supplied art, " +
                      "dual gated cannons, 12 inactive pooled projectiles, layered pooled fire/hit VFX, " +
                      "local-hit damage and fixed landscape camera.");
        }

        private static void ConfigureArtImporters()
        {
            ConfigureSpriteImporter(BackgroundPath);
            ConfigureSpriteImporter(CannonPath);
            ConfigureSpriteImporter(BagPath);
            for (int i = 0; i < LollipopPaths.Length; i++) ConfigureSpriteImporter(LollipopPaths[i]);
            for (int i = 0; i < PropPaths.Length; i++) ConfigureSpriteImporter(PropPaths[i]);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureSpriteImporter(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing supplied Level 3 art.", path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        private static LevelDefinition ConfigureLevelDefinition()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(definition, LevelAssetPath);
            }
            SerializedObject data = new SerializedObject(definition);
            data.FindProperty("levelId").stringValue = LevelId;
            data.FindProperty("displayName").stringValue = "Level 3 - Candy Cannons";
            data.FindProperty("scenePath").stringValue = ScenePath;
            data.FindProperty("objectiveText").stringValue =
                "Alternate the two candy cannons and shatter the glass character.";
            data.FindProperty("completionRule").enumValueIndex = (int)LevelCompletionRule.CharacterDestroyed;
            data.FindProperty("targetDamage").floatValue = 500f;
            data.FindProperty("timeLimit").floatValue = 90f;
            data.FindProperty("completionCoins").intValue = 650;
            data.FindProperty("oneStarScore").intValue = 220;
            data.FindProperty("twoStarScore").intValue = 420;
            data.FindProperty("threeStarScore").intValue = 700;
            data.FindProperty("wallBaseDamage").floatValue = 0f;
            data.FindProperty("wallDamagePerSpeed").floatValue = .55f;
            data.FindProperty("wallMinimumImpactSpeed").floatValue = 5f;
            data.FindProperty("wallMaximumDamage").floatValue = 8f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void ConfigureCatalog(LevelDefinition levelThree)
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Level Catalog asset is missing.");
            SerializedObject data = new SerializedObject(catalog);
            SerializedProperty levels = data.FindProperty("levels");
            if (levels.arraySize < 2)
                throw new InvalidOperationException("Level Catalog must retain Level 01 and Level 02.");
            levels.arraySize = 3;
            levels.GetArrayElementAtIndex(2).objectReferenceValue = levelThree;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void CreateBackground(Transform parent, Camera camera)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            GameObject go = CreateSpriteObject("Level 03 Background", parent, sprite,
                new Vector3(camera.transform.position.x, camera.transform.position.y, 0f), -5);
            float targetHeight = camera.orthographic ? camera.orthographicSize * 2f : 10.8f;
            float scale = sprite != null && sprite.bounds.size.y > 0f ? targetHeight / sprite.bounds.size.y : 1f;
            go.transform.localScale = Vector3.one * scale;
        }

        private static void CreateCandyDecor(Transform parent)
        {
            for (int i = 0; i < LollipopPaths.Length; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LollipopPaths[i]);
                float x = 6.45f + i * .38f;
                GameObject go = CreateSpriteObject("Level 03 Lollipop " + (i + 1), parent, sprite,
                    new Vector3(x, -2.48f + (i & 1) * .12f, 0f), 7);
                go.transform.localScale = Vector3.one * .72f;
                go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-13f, 13f, i / 3f));
            }
            GameObject bag = CreateSpriteObject("Level 03 Lollipop Bag", parent,
                AssetDatabase.LoadAssetAtPath<Sprite>(BagPath), new Vector3(7.05f, -3.45f, 0f), 8);
            bag.transform.localScale = Vector3.one * .78f;

            for (int i = 0; i < PropPaths.Length; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PropPaths[i]);
                float x = -7.55f + i * .55f;
                GameObject go = CreateSpriteObject("Level 03 Floor Candy " + (i + 1), parent, sprite,
                    new Vector3(x, -3.65f + (i & 1) * .08f, 0f), 7);
                go.transform.localScale = Vector3.one * .72f;
            }
        }

        private static CannonAuthoring CreateCannon(Transform parent, CandyCannonSide side,
            Vector2 position, Material glowMaterial, Material candyMaterial, Sprite[] candySprites)
        {
            string label = side == CandyCannonSide.Left ? "Left Candy Cannon" : "Right Candy Cannon";
            GameObject root = new GameObject(label, typeof(BoxCollider2D));
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            BoxCollider2D press = root.GetComponent<BoxCollider2D>();
            press.isTrigger = true;
            press.size = new Vector2(3.25f, 3.2f);

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CannonPath);
            GameObject visual = CreateSpriteObject(label + " Visual", root.transform, sprite,
                Vector3.zero, 45);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * .76f;
            SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
            renderer.flipX = side == CandyCannonSide.Left;

            GameObject muzzleObject = new GameObject(label + " Muzzle");
            muzzleObject.transform.SetParent(root.transform, false);
            muzzleObject.transform.localPosition = side == CandyCannonSide.Left
                ? new Vector3(1.18f, 1.02f, 0f)
                : new Vector3(-1.18f, 1.02f, 0f);

            ParticleSystem flash = CreateMuzzleFlash(
                muzzleObject.transform, glowMaterial, candyMaterial, candySprites);
            GameObject indicator = CreateTutorialIndicator(root.transform, label + " Tutorial Glow");
            indicator.transform.localPosition = new Vector3(0f, -.48f, 0f);
            indicator.SetActive(false);

            return new CannonAuthoring
            {
                Side = side,
                Root = root,
                PressCollider = press,
                Muzzle = muzzleObject.transform,
                RecoilVisual = visual.transform,
                MuzzleFlash = flash,
                TutorialIndicator = indicator
            };
        }

        private static ParticleSystem CreateMuzzleFlash(Transform muzzle, Material glowMaterial,
            Material candyMaterial, Sprite[] candySprites)
        {
            ParticleSystem core = CreateParticleSystem("Muzzle Flash", muzzle, glowMaterial, 64);
            ConfigureMain(core, .18f, 2, new ParticleSystem.MinMaxCurve(.08f, .13f),
                new ParticleSystem.MinMaxCurve(0f), new ParticleSystem.MinMaxCurve(.48f, .72f),
                new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, .72f, .16f, 1f)));
            SetBurst(core, 1);
            SetFadeAndShrink(core, new Color(1f, 1f, .75f, 1f));

            ParticleSystem rays = CreateParticleSystem(
                "Sugar Light Rays", core.transform, glowMaterial, 63);
            rays.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            ConfigureMain(rays, .24f, 8, new ParticleSystem.MinMaxCurve(.12f, .22f),
                new ParticleSystem.MinMaxCurve(4.8f, 7.5f), new ParticleSystem.MinMaxCurve(.045f, .085f),
                new ParticleSystem.MinMaxGradient(
                    new Color(1f, .98f, .52f, 1f), new Color(1f, .28f, .65f, 1f)));
            SetBurst(rays, 6);
            ConfigureCone(rays, 13f, .025f);
            SetFadeAndShrink(rays, Color.white);
            ParticleSystemRenderer rayRenderer = rays.GetComponent<ParticleSystemRenderer>();
            rayRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            rayRenderer.lengthScale = 2.8f;
            rayRenderer.velocityScale = .18f;

            ParticleSystem candy = CreateParticleSystem(
                "Candy Muzzle Sprinkle", core.transform, candyMaterial, 65);
            candy.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            ConfigureMain(candy, .32f, 6, new ParticleSystem.MinMaxCurve(.22f, .38f),
                new ParticleSystem.MinMaxCurve(2.4f, 4.2f), new ParticleSystem.MinMaxCurve(.09f, .15f),
                new ParticleSystem.MinMaxGradient(Color.white));
            SetBurst(candy, 4);
            ConfigureCone(candy, 24f, .04f);
            ParticleSystem.MainModule candyMain = candy.main;
            candyMain.gravityModifier = .35f;
            AddCandySprites(candy, candySprites);
            SetFadeAndShrink(candy, Color.white);

            core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return core;
        }

        private static CandyCannonVFXController2D CreateImpactVFX(Transform parent,
            CandyCannonController2D cannons, CannonAuthoring left, CannonAuthoring right,
            Material glowMaterial, Material candyMaterial, Sprite[] candySprites)
        {
            GameObject root = new GameObject("Candy Cannon VFX", typeof(CandyCannonVFXController2D));
            root.transform.SetParent(parent, false);
            var impactPool = new ParticleSystem[ImpactVFXPoolSize];
            for (int i = 0; i < impactPool.Length; i++)
                impactPool[i] = CreateImpactBurst(root.transform, i, glowMaterial,
                    candyMaterial, candySprites);

            CandyCannonVFXController2D controller = root.GetComponent<CandyCannonVFXController2D>();
            SerializedObject data = new SerializedObject(controller);
            data.FindProperty("cannons").objectReferenceValue = cannons;
            ConfigureMuzzleLayers(data.FindProperty("leftMuzzle"), left.MuzzleFlash);
            ConfigureMuzzleLayers(data.FindProperty("rightMuzzle"), right.MuzzleFlash);
            AssignObjectArray(data.FindProperty("impactPool"), impactPool);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ConfigureMuzzleLayers(SerializedProperty property, ParticleSystem core)
        {
            Transform rays = core != null ? core.transform.Find("Sugar Light Rays") : null;
            Transform candy = core != null ? core.transform.Find("Candy Muzzle Sprinkle") : null;
            property.FindPropertyRelative("core").objectReferenceValue = core;
            property.FindPropertyRelative("rays").objectReferenceValue =
                rays != null ? rays.GetComponent<ParticleSystem>() : null;
            property.FindPropertyRelative("candy").objectReferenceValue =
                candy != null ? candy.GetComponent<ParticleSystem>() : null;
        }

        private static ParticleSystem CreateImpactBurst(Transform parent, int index,
            Material glowMaterial, Material candyMaterial, Sprite[] candySprites)
        {
            ParticleSystem core = CreateParticleSystem(
                "Candy Impact " + (index + 1).ToString("00"), parent, glowMaterial, 72);
            ConfigureMain(core, .22f, 2, new ParticleSystem.MinMaxCurve(.09f, .15f),
                new ParticleSystem.MinMaxCurve(0f), new ParticleSystem.MinMaxCurve(.52f, .82f),
                new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, .88f, 1f), new Color(1f, .42f, .72f, 1f)));
            SetBurst(core, 1);
            SetFadeAndShrink(core, new Color(1f, .92f, .38f, 1f));

            ParticleSystem rays = CreateParticleSystem(
                "Impact Sugar Rays", core.transform, glowMaterial, 71);
            ConfigureMain(rays, .3f, 9, new ParticleSystem.MinMaxCurve(.13f, .28f),
                new ParticleSystem.MinMaxCurve(3.8f, 7.2f), new ParticleSystem.MinMaxCurve(.045f, .09f),
                new ParticleSystem.MinMaxGradient(
                    new Color(1f, .96f, .38f, 1f), new Color(1f, .24f, .66f, 1f)));
            SetBurst(rays, 7);
            ConfigureCircle(rays, .035f);
            SetFadeAndShrink(rays, Color.white);
            ParticleSystemRenderer rayRenderer = rays.GetComponent<ParticleSystemRenderer>();
            rayRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            rayRenderer.lengthScale = 2.6f;
            rayRenderer.velocityScale = .16f;

            ParticleSystem candy = CreateParticleSystem(
                "Impact Candy Confetti", core.transform, candyMaterial, 73);
            ConfigureMain(candy, .7f, 7, new ParticleSystem.MinMaxCurve(.38f, .68f),
                new ParticleSystem.MinMaxCurve(1.5f, 3.6f), new ParticleSystem.MinMaxCurve(.12f, .2f),
                new ParticleSystem.MinMaxGradient(Color.white));
            ParticleSystem.MainModule candyMain = candy.main;
            candyMain.gravityModifier = .85f;
            candyMain.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            SetBurst(candy, 5);
            ConfigureCircle(candy, .045f);
            AddCandySprites(candy, candySprites);
            SetFadeAndShrink(candy, Color.white);
            ParticleSystem.RotationOverLifetimeModule rotation = candy.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-3.2f, 3.2f);

            core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return core;
        }

        private static ParticleSystem CreateParticleSystem(string name, Transform parent,
            Material material, int sortingOrder)
        {
            GameObject go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.GetComponent<ParticleSystem>();
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            return system;
        }

        private static void ConfigureMain(ParticleSystem system, float duration, int maximumParticles,
            ParticleSystem.MinMaxCurve lifetime, ParticleSystem.MinMaxCurve speed,
            ParticleSystem.MinMaxCurve size, ParticleSystem.MinMaxGradient color)
        {
            ParticleSystem.MainModule main = system.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maximumParticles;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
        }

        private static void SetBurst(ParticleSystem system, short count)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void ConfigureCone(ParticleSystem system, float angle, float radius)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.radiusThickness = 1f;
        }

        private static void ConfigureCircle(ParticleSystem system, float radius)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            shape.arc = 360f;
        }

        private static void SetFadeAndShrink(ParticleSystem system, Color color)
        {
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .08f),
                    new GradientAlphaKey(.9f, .55f), new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, .35f), new Keyframe(.12f, 1f),
                    new Keyframe(.72f, .72f), new Keyframe(1f, 0f)));
        }

        private static void AddCandySprites(ParticleSystem system, Sprite[] candySprites)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Sprites;
            int count = Mathf.Min(8, candySprites != null ? candySprites.Length : 0);
            for (int i = 0; i < count; i++)
                if (candySprites[i] != null) sheet.AddSprite(candySprites[i]);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, .999f);
        }

        private static GameObject CreateTutorialIndicator(Transform parent, string name)
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            GameObject go = CreateSpriteObject(name, parent, sprite, Vector3.zero, 62);
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            renderer.color = new Color(1f, .88f, .18f, .76f);
            go.transform.localScale = Vector3.one * .65f;
            return go;
        }

        private static void ConfigureCannonController(CandyCannonController2D controller, Camera camera,
            RagdollController ragdoll, SoundManager sounds, Rigidbody2D[] aimBodies,
            Collider2D[] partColliders,
            CannonAuthoring left, CannonAuthoring right, Sprite[] candySprites,
            Material trailMaterial, PhysicsMaterial2D projectileMaterial, Transform parent)
        {
            Transform poolRoot = new GameObject("Candy Projectile Pool").transform;
            poolRoot.SetParent(parent, false);
            var bodies = new Rigidbody2D[ProjectilePoolSize];
            var colliders = new Collider2D[ProjectilePoolSize];
            var renderers = new SpriteRenderer[ProjectilePoolSize];
            var trails = new TrailRenderer[ProjectilePoolSize];
            var attacks = new RagdollAttackManager2D[ProjectilePoolSize];
            for (int i = 0; i < ProjectilePoolSize; i++)
                CreateProjectile(poolRoot, i, candySprites[i % candySprites.Length], trailMaterial,
                    projectileMaterial, out bodies[i], out colliders[i], out renderers[i],
                    out trails[i], out attacks[i]);

            SerializedObject data = new SerializedObject(controller);
            data.FindProperty("inputCamera").objectReferenceValue = camera;
            data.FindProperty("ragdoll").objectReferenceValue = ragdoll;
            data.FindProperty("animationController").objectReferenceValue =
                ragdoll.GetComponent<RagdollAnimationController>();
            data.FindProperty("soundManager").objectReferenceValue = sounds;
            AssignObjectArray(data.FindProperty("aimBodies"), aimBodies);
            AssignObjectArray(data.FindProperty("ragdollPartColliders"), partColliders);
            data.FindProperty("perCannonCooldown").floatValue = .11f;
            data.FindProperty("globalFireInterval").floatValue = .055f;
            data.FindProperty("holdDelay").floatValue = .28f;
            data.FindProperty("holdFireInterval").floatValue = .11f;
            data.FindProperty("maximumQueuedShots").intValue = ProjectilePoolSize;
            ConfigureCannonSlot(data.FindProperty("leftCannon"), left);
            ConfigureCannonSlot(data.FindProperty("rightCannon"), right);
            SerializedProperty pool = data.FindProperty("projectilePool");
            pool.arraySize = ProjectilePoolSize;
            for (int i = 0; i < ProjectilePoolSize; i++)
            {
                SerializedProperty slot = pool.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("body").objectReferenceValue = bodies[i];
                slot.FindPropertyRelative("collider").objectReferenceValue = colliders[i];
                slot.FindPropertyRelative("renderer").objectReferenceValue = renderers[i];
                slot.FindPropertyRelative("trail").objectReferenceValue = trails[i];
                slot.FindPropertyRelative("attack").objectReferenceValue = attacks[i];
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static void CreateProjectile(Transform parent, int index, Sprite sprite, Material trailMaterial,
            PhysicsMaterial2D physicsMaterial, out Rigidbody2D body, out Collider2D collider,
            out SpriteRenderer renderer, out TrailRenderer trail, out RagdollAttackManager2D attack)
        {
            GameObject go = new GameObject("Candy Projectile " + (index + 1).ToString("00"),
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D),
                typeof(TrailRenderer), typeof(RagdollAttackManager2D));
            go.transform.SetParent(parent, false);
            renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 55;
            renderer.enabled = false;
            body = go.GetComponent<Rigidbody2D>();
            body.mass = .06f;
            body.gravityScale = 0f;
            body.drag = 0f;
            body.angularDrag = .05f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.simulated = false;
            CircleCollider2D circle = go.GetComponent<CircleCollider2D>();
            circle.radius = .22f;
            circle.sharedMaterial = physicsMaterial;
            circle.enabled = false;
            collider = circle;
            trail = go.GetComponent<TrailRenderer>();
            trail.time = .18f;
            trail.minVertexDistance = .08f;
            trail.startWidth = .18f;
            trail.endWidth = 0f;
            trail.numCornerVertices = 1;
            trail.numCapVertices = 1;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.sharedMaterial = trailMaterial;
            trail.startColor = Color.white;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
            trail.sortingOrder = 54;
            trail.enabled = false;
            attack = go.GetComponent<RagdollAttackManager2D>();
            attack.Configure(RagdollAttackType.Bullet, 5f, 1f, 5f, 14f);
            go.SetActive(false);
        }

        private static void ConfigureSceneController(GameplayLevelSceneController controller,
            GameObject levelRoot, RagdollController ragdoll, RagdollInputManager input,
            CandyCannonController2D cannons)
        {
            SerializedObject data = new SerializedObject(controller);
            SerializedProperty levels = data.FindProperty("levels");
            if (levels.arraySize < 2)
                throw new InvalidOperationException("Level 01 and Level 02 entries must already exist.");
            levels.arraySize = 3;
            for (int i = 0; i < 2; i++)
            {
                SerializedProperty existing = levels.GetArrayElementAtIndex(i);
                SerializedProperty cannonProperty = existing.FindPropertyRelative("candyCannons");
                if (cannonProperty != null) cannonProperty.objectReferenceValue = null;
            }
            SerializedProperty entry = levels.GetArrayElementAtIndex(2);
            entry.FindPropertyRelative("levelId").stringValue = LevelId;
            entry.FindPropertyRelative("root").objectReferenceValue = levelRoot;
            entry.FindPropertyRelative("ragdoll").objectReferenceValue = ragdoll;
            entry.FindPropertyRelative("ragdollInput").objectReferenceValue = input;
            entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue = null;
            entry.FindPropertyRelative("candyCannons").objectReferenceValue = cannons;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static void ConfigureCannonSlot(SerializedProperty property, CannonAuthoring source)
        {
            property.FindPropertyRelative("side").enumValueIndex = (int)source.Side;
            property.FindPropertyRelative("pressCollider").objectReferenceValue = source.PressCollider;
            property.FindPropertyRelative("muzzle").objectReferenceValue = source.Muzzle;
            property.FindPropertyRelative("recoilVisual").objectReferenceValue = source.RecoilVisual;
            property.FindPropertyRelative("muzzleFlash").objectReferenceValue = source.MuzzleFlash;
            property.FindPropertyRelative("tutorialIndicator").objectReferenceValue = source.TutorialIndicator;
        }

        private static Rigidbody2D[] ResolveAimBodies(RagdollController ragdoll)
        {
            var ordered = new List<Rigidbody2D>(6);
            RagdollRigController2D rig = ragdoll.GetComponent<RagdollRigController2D>();
            SerializedProperty authoredParts = rig != null
                ? new SerializedObject(rig).FindProperty("authoredParts")
                : null;
            if (authoredParts == null)
                throw new InvalidOperationException("The ragdoll has no authored part table.");
            AddFirstPart(authoredParts, RagdollPartType.Torso, ordered);
            AddFirstPart(authoredParts, RagdollPartType.Head, ordered);
            AddAllParts(authoredParts, RagdollPartType.Arm, ordered);
            AddAllParts(authoredParts, RagdollPartType.Leg, ordered);
            if (ordered.Count != 6)
                throw new InvalidOperationException("The cannon aim table requires torso, head, two arms and two legs.");
            return ordered.ToArray();
        }

        private static Collider2D[] ResolvePartColliders(RagdollController ragdoll)
        {
            var colliders = new List<Collider2D>(6);
            RagdollRigController2D rig = ragdoll.GetComponent<RagdollRigController2D>();
            SerializedProperty authoredParts = rig != null
                ? new SerializedObject(rig).FindProperty("authoredParts")
                : null;
            if (authoredParts == null)
                throw new InvalidOperationException("The ragdoll has no authored part table.");

            for (int i = 0; i < authoredParts.arraySize; i++)
            {
                SerializedProperty partColliders =
                    authoredParts.GetArrayElementAtIndex(i).FindPropertyRelative("colliders");
                if (partColliders == null) continue;
                for (int j = 0; j < partColliders.arraySize; j++)
                {
                    Collider2D collider =
                        partColliders.GetArrayElementAtIndex(j).objectReferenceValue as Collider2D;
                    if (collider != null && !colliders.Contains(collider)) colliders.Add(collider);
                }
            }

            if (colliders.Count < 6)
                throw new InvalidOperationException(
                    "The cannon collision table requires the six authored ragdoll part colliders.");
            return colliders.ToArray();
        }

        private static void AddFirstPart(SerializedProperty parts, RagdollPartType type,
            List<Rigidbody2D> output)
        {
            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty part = parts.GetArrayElementAtIndex(i);
                if (part.FindPropertyRelative("partType").enumValueIndex == (int)type)
                {
                    Rigidbody2D body = part.FindPropertyRelative("body").objectReferenceValue as Rigidbody2D;
                    if (body != null) output.Add(body);
                    return;
                }
            }
        }

        private static void AddAllParts(SerializedProperty parts, RagdollPartType type,
            List<Rigidbody2D> output)
        {
            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty part = parts.GetArrayElementAtIndex(i);
                if (part.FindPropertyRelative("partType").enumValueIndex != (int)type) continue;
                Rigidbody2D body = part.FindPropertyRelative("body").objectReferenceValue as Rigidbody2D;
                if (body != null) output.Add(body);
            }
        }

        private static Material GetOrCreateTrailMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader is unavailable.");
            material = new Material(shader) { name = "MAT_Level03_CannonTrail" };
            AssetDatabase.CreateAsset(material, TrailMaterialPath);
            return material;
        }

        private static Material GetOrCreateGlowMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (material != null) return material;
            Material builtin =
                AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
            Shader shader = builtin != null ? builtin.shader : Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null)
                throw new InvalidOperationException("A built-in additive particle shader is unavailable.");
            material = builtin != null ? new Material(builtin) : new Material(shader);
            material.name = "MAT_Level03_CannonGlow";
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 4f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
            AssetDatabase.CreateAsset(material, GlowMaterialPath);
            return material;
        }

        private static Material GetOrCreateCandyParticleMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CandyParticleMaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader is unavailable.");
            material = new Material(shader) { name = "MAT_Level03_CannonCandy" };
            AssetDatabase.CreateAsset(material, CandyParticleMaterialPath);
            return material;
        }

        private static PhysicsMaterial2D GetOrCreateProjectileMaterial()
        {
            PhysicsMaterial2D material =
                AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(ProjectilePhysicsMaterialPath);
            if (material != null) return material;
            material = new PhysicsMaterial2D("PMAT_Level03_CandyProjectile")
            {
                friction = .12f,
                bounciness = .2f
            };
            AssetDatabase.CreateAsset(material, ProjectilePhysicsMaterialPath);
            return material;
        }

        private static Sprite[] LoadCandySprites()
        {
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { CandyArtRoot });
            Array.Sort(guids, StringComparer.Ordinal);
            var sprites = new List<Sprite>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (sprite != null) sprites.Add(sprite);
            }
            if (sprites.Count == 0) throw new InvalidOperationException("No candy sprites are available.");
            return sprites.ToArray();
        }

        private static GameObject CreateSpriteObject(string name, Transform parent, Sprite sprite,
            Vector3 position, int sortingOrder)
        {
            if (sprite == null) throw new InvalidOperationException("Missing sprite for " + name + ".");
            GameObject go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return go;
        }

        private static void ValidateObjectReference(SerializedObject data, string propertyName)
        {
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == null)
                throw new InvalidOperationException("Missing cannon reference: " + propertyName + ".");
        }

        private static void ValidateCannonSlot(SerializedProperty slot, CandyCannonSide expectedSide)
        {
            if (slot.FindPropertyRelative("side").enumValueIndex != (int)expectedSide ||
                slot.FindPropertyRelative("pressCollider").objectReferenceValue == null ||
                slot.FindPropertyRelative("muzzle").objectReferenceValue == null ||
                slot.FindPropertyRelative("recoilVisual").objectReferenceValue == null ||
                slot.FindPropertyRelative("muzzleFlash").objectReferenceValue == null ||
                slot.FindPropertyRelative("tutorialIndicator").objectReferenceValue == null)
                throw new InvalidOperationException(expectedSide + " cannon has incomplete authored references.");
        }

        private static void ValidateProjectilePool(SerializedProperty pool)
        {
            for (int i = 0; i < pool.arraySize; i++)
            {
                SerializedProperty slot = pool.GetArrayElementAtIndex(i);
                Rigidbody2D body = slot.FindPropertyRelative("body").objectReferenceValue as Rigidbody2D;
                Collider2D collider = slot.FindPropertyRelative("collider").objectReferenceValue as Collider2D;
                TrailRenderer trail = slot.FindPropertyRelative("trail").objectReferenceValue as TrailRenderer;
                RagdollAttackManager2D attack =
                    slot.FindPropertyRelative("attack").objectReferenceValue as RagdollAttackManager2D;
                if (body == null || collider == null || trail == null || attack == null ||
                    body.gameObject.activeSelf || body.simulated || collider.enabled || trail.enabled ||
                    body.gravityScale != 0f || body.collisionDetectionMode != CollisionDetectionMode2D.Continuous ||
                    attack.AttackType != RagdollAttackType.Bullet ||
                    attack.CalculateDamage(14.5f) <= 0f || attack.CalculateDamage(14.5f) > 14f)
                    throw new InvalidOperationException("Candy projectile " + i + " is not pool-safe or balanced.");
            }
        }

        private static void ValidateMuzzleLayers(SerializedProperty layers, string label)
        {
            ParticleSystem core =
                layers.FindPropertyRelative("core").objectReferenceValue as ParticleSystem;
            ParticleSystem rays =
                layers.FindPropertyRelative("rays").objectReferenceValue as ParticleSystem;
            ParticleSystem candy =
                layers.FindPropertyRelative("candy").objectReferenceValue as ParticleSystem;
            if (core == null || rays == null || candy == null)
                throw new InvalidOperationException(label + " cannon muzzle VFX references are incomplete.");
            ValidateParticleSystem(core, 2);
            ValidateParticleSystem(rays, 8);
            ValidateParticleSystem(candy, 6);
        }

        private static void ValidateImpactPool(SerializedProperty pool)
        {
            for (int i = 0; i < pool.arraySize; i++)
            {
                ParticleSystem root = pool.GetArrayElementAtIndex(i).objectReferenceValue as ParticleSystem;
                if (root == null)
                    throw new InvalidOperationException("Candy impact VFX pool contains a missing slot.");
                ParticleSystem[] layers = root.GetComponentsInChildren<ParticleSystem>(true);
                if (layers.Length != 3)
                    throw new InvalidOperationException("Every candy impact needs core, rays and confetti layers.");
                for (int j = 0; j < layers.Length; j++)
                    ValidateParticleSystem(layers[j], 9);
            }
        }

        private static void ValidateParticleSystem(ParticleSystem system, int maximumParticles)
        {
            ParticleSystem.MainModule main = system.main;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (main.loop || main.playOnAwake ||
                main.simulationSpace != ParticleSystemSimulationSpace.World ||
                main.maxParticles > maximumParticles || system.collision.enabled ||
                system.noise.enabled || system.trails.enabled || system.subEmitters.enabled ||
                renderer == null || renderer.sharedMaterial == null || renderer.sortingOrder <= 55)
                throw new InvalidOperationException(
                    system.name + " is not configured for pooled mobile-safe cannon feedback.");
        }

        private static RagdollController FindConfiguredRagdoll(Scene scene)
        {
            RagdollController[] candidates = UnityEngine.Object.FindObjectsOfType<RagdollController>(true);
            RagdollController fallback = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                RagdollController candidate = candidates[i];
                if (candidate == null || candidate.gameObject.scene != scene) continue;
                RagdollRigController2D rig = candidate.GetComponent<RagdollRigController2D>();
                SerializedProperty authoredParts = rig != null
                    ? new SerializedObject(rig).FindProperty("authoredParts")
                    : null;
                if (authoredParts == null || authoredParts.arraySize != 6) continue;
                if (candidate.gameObject.activeSelf) return candidate;
                fallback = candidate;
            }
            return fallback;
        }

        private static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            T[] candidates = UnityEngine.Object.FindObjectsOfType<T>(true);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].gameObject.scene == scene) return candidates[i];
            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        private static T FindNamedComponent<T>(Transform root, string name) where T : Component
        {
            T[] candidates = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].name == name) return candidates[i];
            return null;
        }

        private static void AssignObjectArray<T>(SerializedProperty property, T[] values)
            where T : UnityEngine.Object
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static int CountMissingScripts(Scene scene)
        {
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < transforms.Length; t++)
                    count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[t].gameObject);
            }
            return count;
        }
    }
}
#endif
